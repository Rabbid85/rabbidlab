using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// The change journal and undo: what each AD write keeps of before and after, putting it back, and when that's refused.
/// The real DirectoryTools works against FakeLdap through LiveLdap, which makes the changes as Active Directory would.
/// </summary>
public sealed class ChangesTests : IDisposable
{
    private const string Lab = "OU=Lab,DC=amw,DC=com";
    private const string Jordan = $"CN=Jordan Reyes,{Lab}";
    private const string Staff = $"CN=Lab Staff,{Lab}";
    private const string Pat = $"CN=Pat Primary,{Lab}";
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-changes-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;
    private readonly FakeLdap ad = new();
    private readonly LiveLdap live;
    private readonly WebApplicationFactory<Program> host;

    public ChangesTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
        live = new LiveLdap(ad);
        // Jordan has a GUID, as every object in AD does, so undo finds him after a move.
        ad.Rows.Single(r => r.Dn == Jordan).Values["objectGUID"] = [Guid.NewGuid().ToByteArray()];
        // The real tools behind every AD page, against the fake domain.
        host = app.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<DirectoryTools>();
            s.AddSingleton(p => new DirectoryTools(p.GetRequiredService<DataFolder>(), p.GetRequiredService<SettingsStore>(), p.GetRequiredService<Accounts>(),
                p.GetRequiredService<IDomainFinder>(), live.Open) { AsComputer = null });
            s.RemoveAll<IDirectoryTools>();
            s.AddSingleton<IDirectoryTools>(p => p.GetRequiredService<DirectoryTools>());
        }));
    }

    public void Dispose()
    {
        app.Dispose();
        try
        {
            Directory.Delete(data, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temp folder's own cleanup.
        }
    }

    private DirectoryTools Tools => host.Services.GetRequiredService<DirectoryTools>();

    private ChangeJournal Journal => host.Services.GetRequiredService<ChangeJournal>();

    private Task Connect() => Tools.Connect(new SetupForm("amw.com", "svc-existing", "right", Existing: true, null, null, Lab), "admin", None);

    private FakeLdap.Item Row(string dn) => ad.Rows.Single(r => string.Equals(r.Dn, dn, StringComparison.OrdinalIgnoreCase));

    private static string? Value(FakeLdap.Item row, string attribute) => row.Values.TryGetValue(attribute, out var v) && v.Length > 0 ? v[0] as string : null;

    private static List<string> Values(FakeLdap.Item row, string attribute) => row.Values.TryGetValue(attribute, out var v) ? [.. v.OfType<string>()] : [];

    // The Recycle Bin, on for the forest.
    private void BinOn() => ad.Add("CN=Recycle Bin Feature,CN=Optional Features,CN=Directory Service,CN=Windows NT,CN=Services,CN=Configuration,DC=amw,DC=com",
        ("msDS-EnabledFeatureBL", "CN=NTDS Settings,CN=DC01,CN=Servers,CN=Default-First-Site-Name,CN=Sites,CN=Configuration,DC=amw,DC=com"));

    /// <summary>What one write noted, as the activity log keeps it.</summary>
    private async Task<Change?> Made(string permission, Func<Task> work, bool confirm = false)
    {
        var note = new ChangeNote(Journal, permission, confirm);
        ChangeNote.Set(note);
        try
        {
            await work();
        }
        finally
        {
            ChangeNote.Set(null);
        }
        return note.Keep("admin", "10.0.0.5", "A change");
    }

    private async Task<string> Undo(Change c)
    {
        var note = new ChangeNote(Journal, c.Permission, c.Confirm);
        note.Undoing(c);
        ChangeNote.Set(note);
        try
        {
            return await Tools.Undo(c, None);
        }
        finally
        {
            ChangeNote.Set(null);
        }
    }

    private static Task<ToolProblem> Refused(Func<Task> work) => Assert.ThrowsAsync<ToolProblem>(work);

    [Fact]
    public async Task Each_kind_of_write_keeps_its_values_before_and_after()
    {
        await Connect();
        var disabled = await Made(Permissions.AdEnable, () => Tools.Enable("jreyes", false, None));
        var smartcard = await Made(Permissions.AdChange, () => Tools.SetFlag("jreyes", "smartcard", true, None));
        var must = await Made(Permissions.AdPassword, () => Tools.SetFlag("jreyes", "mustchange", true, None));
        var expires = await Made(Permissions.AdChange, () => Tools.SetExpiry("jreyes", new DateOnly(2026, 12, 31), None));
        var edited = await Made(Permissions.AdChange, () => Tools.Edit("jreyes", new UserEdit(null, null, null, "Field tech", null, null, null, "Tech", "Sales", null, null), None));
        var joined = await Made(Permissions.AdGroups, () => Tools.ChangeGroups("jreyes", [Staff], [], None), confirm: true);
        var members = await Made(Permissions.AdGroups, () => Tools.ChangeMembers(Staff, [Pat], [], None), confirm: true);
        var moved = await Made(Permissions.AdChange, () => Tools.Move("jreyes", $"OU=People,{Lab}", None));
        var renamed = await Made(Permissions.AdChange, () => Tools.Rename("jreyes", "Jordan Reyes-Hall", None));
        var group = await Made(Permissions.AdChange, () => Tools.EditObject(new ObjectEdit(Staff, "Lab people", null, null, null), None));
        var pc = await Made(Permissions.AdChange, () => Tools.MoveObject($"CN=PC1,{Lab}", $"OU=People,{Lab}", None));
        var pcOff = await Made(Permissions.AdEnable, () => Tools.EnableComputer("PC1", false, None));
        var same = await Made(Permissions.AdEnable, () => Tools.EnableComputer("PC1", false, None));

        Assert.Equal(("Account", "Enabled", "Disabled"), (disabled!.Steps[0].Label, disabled.Steps[0].Before[0], disabled.Steps[0].After[0]));
        Assert.Equal(("bit", 2, false), (disabled.Steps[0].Undo!.Kind, disabled.Steps[0].Undo!.Bit, disabled.Steps[0].Undo!.On));
        Assert.Equal(("Smart card required", "No", "Yes"), (smartcard!.Steps[0].Label, smartcard.Steps[0].Before[0], smartcard.Steps[0].After[0]));
        Assert.Equal("mustchange", must!.Steps[0].Undo!.Kind);
        Assert.Equal(["Never"], expires!.Steps[0].Before);
        Assert.Equal(["End of 2026-12-31"], expires.Steps[0].After);
        Assert.Equal(["Description", "Job title", "Department"], edited!.Steps.Select(s => s.Label));
        Assert.Equal((0, "Sales"), (edited.Steps[2].Before.Count, edited.Steps[2].After[0]));
        Assert.Equal(("Member of", 0, "Lab Staff"), (joined!.Steps[0].Label, joined.Steps[0].Before.Count, joined.Steps[0].After[0]));
        Assert.Equal([Staff], joined.Steps[0].Undo!.Remove);
        Assert.True(joined.Confirm);
        Assert.Equal(Permissions.AdGroups, joined.Permission);
        Assert.Equal(["Jordan Reyes"], members!.Steps[0].Before);
        Assert.Equal(["Jordan Reyes", "Pat Primary"], members.Steps[0].After);
        Assert.Equal(("Lab", "Lab / People"), (moved!.Steps[0].Before[0], moved.Steps[0].After[0]));
        Assert.Equal($"CN=Jordan Reyes,OU=People,{Lab}", moved.Object.Dn);
        Assert.NotNull(moved.Object.Guid);
        Assert.Equal(["Name", "Display name"], renamed!.Steps.Select(s => s.Label));
        Assert.Equal(("Jordan Reyes-Hall", $"CN=Jordan Reyes-Hall,OU=People,{Lab}"), (renamed.Object.Name, renamed.Object.Dn));
        Assert.Equal(("Description", "group"), (group!.Steps[0].Label, group.Object.Kind));
        Assert.Equal(("computer", "Lab / People"), (pc!.Object.Kind, pc.Steps[0].After[0]));
        Assert.Equal("Disabled", pcOff!.Steps[0].After[0]);
        Assert.Null(same); // nothing changed, so nothing to undo
        Assert.All(new[] { disabled, smartcard, must, expires, edited, joined, members, moved, renamed, group, pc, pcOff }, c => Assert.Null(c!.Why));
    }

    [Fact]
    public async Task Changes_that_cannot_be_undone_are_kept_with_why()
    {
        await Connect();
        var password = await Made(Permissions.AdPassword, () => Tools.Password("jreyes", "New pass 1", true, None), confirm: true);
        var unlocked = await Made(Permissions.AdPassword, () => Tools.Unlock("jreyes", None));
        var created = await Made(Permissions.AdCreate, () => Tools.Create(new NewAdUser("New", "Person", null, "nperson", null, null, false, true, false, null, null, null, null), "Pass word 1", None), confirm: true);
        var gone = await Made(Permissions.AdDelete, () => Tools.Delete("pat", None));

        Assert.Contains("previous password is not stored", password!.Why);
        Assert.Equal(["Password", "Must change password at next sign-in"], password.Steps.Select(s => s.Label));
        Assert.Contains("unlock cannot be undone", unlocked!.Why);
        Assert.Equal("Undoing a create would delete the user. Delete the user instead.", created!.Why);
        Assert.Equal("The AD Recycle Bin is not enabled, so the deleted user cannot be restored.", gone!.Why);
        var refused = await Refused(() => Undo(password));
        Assert.Contains("cannot be undone", refused.Message);
    }

    [Fact]
    public async Task Undo_puts_each_kind_back()
    {
        await Connect();
        BinOn();
        var title = await Made(Permissions.AdChange, () => Tools.Edit("jreyes", new UserEdit(null, null, null, null, null, null, null, "Tech", null, null, null), None));
        var joined = await Made(Permissions.AdGroups, () => Tools.ChangeGroups("jreyes", [Staff], [], None));
        var disabled = await Made(Permissions.AdEnable, () => Tools.Enable("jreyes", false, None));
        var expires = await Made(Permissions.AdChange, () => Tools.SetExpiry("jreyes", new DateOnly(2027, 1, 31), None));
        var moved = await Made(Permissions.AdChange, () => Tools.Move("jreyes", $"OU=People,{Lab}", None));
        var renamed = await Made(Permissions.AdChange, () => Tools.Rename("jreyes", "Jordan Hall", None));
        var members = await Made(Permissions.AdGroups, () => Tools.ChangeMembers(Staff, [Pat], [], None));
        var patGuid = Guid.NewGuid();
        Row(Pat).Values["objectGUID"] = [patGuid.ToByteArray()];
        var deleted = await Made(Permissions.AdDelete, () => Tools.Delete("pat", None));

        // Newest first, as the Activity log's Undo does for several.
        var said = new List<string>();
        foreach (var c in new[] { deleted, members, renamed, moved, expires, disabled, joined, title })
        {
            said.Add(await Undo(c!));
        }

        var jordan = Row(Jordan);
        Assert.Null(Value(jordan, "title"));
        Assert.Equal("Jordan Reyes", Value(jordan, "displayName"));
        Assert.Equal("512", Value(jordan, "userAccountControl"));
        Assert.Equal("0", Value(jordan, "accountExpires"));
        Assert.DoesNotContain(Jordan, Values(Row(Staff), "member"));
        Assert.DoesNotContain(Pat, Values(Row(Staff), "member"));
        Assert.Contains(ad.Rows, r => r.Dn == Pat);
        Assert.Contains($@"restore CN=Pat Primary\0ADEL:{patGuid},CN=Deleted Objects,DC=amw,DC=com -> {Pat}", ad.Log);
        Assert.Equal([
            "Pat Primary was restored from the Recycle Bin.",
            "Pat Primary was removed from Lab Staff again.",
            "Jordan Hall was renamed back to Jordan Reyes.",
            "Jordan Reyes was moved back to Lab.",
            "Jordan Reyes's Account expires was set back to Never.",
            "Jordan Reyes was enabled again.",
            "Jordan Reyes was removed from Lab Staff again.",
            "Jordan Reyes's Job title was cleared.",
        ], said);
    }

    [Fact]
    public async Task Undo_is_refused_when_the_object_changed_since()
    {
        await Connect();
        var department = await Made(Permissions.AdChange, () => Tools.Edit("jreyes", new UserEdit(null, null, null, null, null, null, null, null, "Sales", null, null), None));
        var joined = await Made(Permissions.AdGroups, () => Tools.ChangeGroups("jreyes", [Staff], [], None));
        var moved = await Made(Permissions.AdChange, () => Tools.MoveObject($"CN=PC1,{Lab}", $"OU=People,{Lab}", None));
        var disabled = await Made(Permissions.AdEnable, () => Tools.Enable("jreyes", false, None));

        // Someone changes each again in another tool.
        Row(Jordan).Values["department"] = ["Sales and marketing"];
        await live.Modify(Staff, [new(LdapChange.Delete, "member", Jordan)], None);
        ad.Rows.Remove(Row($"CN=PC1,OU=People,{Lab}"));
        Row(Jordan).Values["userAccountControl"] = ["512"];
        ad.Log.Clear();

        var a = await Refused(() => Undo(department!));
        var b = await Refused(() => Undo(joined!));
        var c = await Refused(() => Undo(moved!));
        var d = await Refused(() => Undo(disabled!));

        Assert.Equal("Jordan Reyes's Department was changed again since. Undo would overwrite Sales and marketing.", a.Message);
        Assert.Equal(409, a.Status);
        Assert.Equal("Jordan Reyes was removed from Lab Staff since.", b.Message);
        Assert.Equal(404, c.Status);
        Assert.Equal("Jordan Reyes was enabled again since.", d.Message);
        Assert.DoesNotContain(ad.Log, l => l.StartsWith("modify", StringComparison.Ordinal) || l.StartsWith("rename", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_AD_change_answers_with_its_change_and_undo_takes_it_back_from_anywhere()
    {
        await Connect();
        var admin = await SignedIn();

        var joined = await Json(await admin.PostAsJsonAsync("/api/ad/users/jreyes/groups", new { add = new[] { Staff } }));
        var id = joined.GetProperty("change").GetProperty("id").GetString()!;
        var log = await admin.GetFromJsonAsync<JsonElement>("/api/activity/find?kind=accounts");
        var entry = log.GetProperty("entries").EnumerateArray().First(e => e.GetProperty("change").GetString() == id);
        var history = await admin.GetFromJsonAsync<JsonElement>($"/api/changes?dn={Uri.EscapeDataString(Jordan)}");
        var undone = await admin.PostAsync($"/api/changes/{id}/undo", null);
        var after = await admin.GetFromJsonAsync<JsonElement>($"/api/changes/{id}");
        var again = await admin.PostAsync($"/api/changes/{id}/undo", null);
        var log2 = await admin.GetFromJsonAsync<JsonElement>("/api/activity/find?kind=accounts");

        Assert.True(joined.GetProperty("change").GetProperty("undo").GetBoolean());
        Assert.StartsWith("Changed group membership for jreyes", entry.GetProperty("title").GetString());
        Assert.True(log.GetProperty("changes").GetProperty(id).GetProperty("undo").GetBoolean());
        Assert.Equal(id, history[0].GetProperty("id").GetString());
        Assert.Equal("Member of", history[0].GetProperty("steps")[0].GetProperty("label").GetString());
        Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
        Assert.Equal("Jordan Reyes was removed from Lab Staff again.", (await Json(undone)).GetProperty("said").GetString());
        Assert.DoesNotContain(Jordan, Values(Row(Staff), "member"));
        Assert.False(after.GetProperty("undo").GetBoolean());
        Assert.Equal("admin", after.GetProperty("undone").GetProperty("by").GetString());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already undone", await Error(again));
        var undid = log2.GetProperty("entries")[0];
        Assert.Equal($"Undid: {entry.GetProperty("title").GetString()}", undid.GetProperty("title").GetString());
        Assert.False(log2.GetProperty("changes").GetProperty(id).GetProperty("undo").GetBoolean());
        Assert.Equal("admin", log2.GetProperty("changes").GetProperty(id).GetProperty("undone").GetProperty("by").GetString());
    }

    [Fact]
    public async Task Undo_takes_the_same_permission_and_Confirm_it_s_you_when_the_change_did()
    {
        await Connect();
        var admin = await SignedIn();
        var joined = (await Json(await admin.PostAsJsonAsync("/api/ad/users/jreyes/groups", new { add = new[] { Staff } }))).GetProperty("change").GetProperty("id").GetString()!;
        var titled = (await Json(await admin.PostAsJsonAsync("/api/ad/users/jreyes/edit", new { title = "Tech" }))).GetProperty("change").GetProperty("id").GetString()!;
        var role = await Role(admin, "Help desk", "ad.view", "ad.change", "activity.view");
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "help1", password = "user password", roles = new[] { role } })).EnsureSuccessStatusCode();
        var help = await SignedIn("help1", "user password");

        var seen = await help.GetFromJsonAsync<JsonElement>($"/api/changes/{joined}");
        var notAllowed = await help.PostAsync($"/api/changes/{joined}/undo", null);
        var unconfirmed = await (await SignedIn(confirmed: false)).PostAsync($"/api/changes/{joined}/undo", null);
        var allowed = await help.PostAsync($"/api/changes/{titled}/undo", null);

        Assert.False(seen.GetProperty("undo").GetBoolean());
        Assert.Equal("Undoing this change requires the Change group membership permission.", seen.GetProperty("why").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, notAllowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unconfirmed.StatusCode);
        Assert.True((await Json(unconfirmed)).TryGetProperty("confirm", out _));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Contains(Jordan, Values(Row(Staff), "member"));
        Assert.Null(Value(Row(Jordan), "title"));
    }

    [Fact]
    public async Task A_change_can_be_undone_for_30_days()
    {
        await Connect();
        var admin = await SignedIn();
        var id = (await Json(await admin.PostAsJsonAsync("/api/ad/users/jreyes/edit", new { title = "Tech" }))).GetProperty("change").GetProperty("id").GetString()!;

        app.Clock.Advance(TimeSpan.FromDays(30.5));
        var late = await (await SignedIn()).PostAsync($"/api/changes/{id}/undo", null);
        app.Clock.Advance(TimeSpan.FromDays(1));
        var gone = await (await SignedIn()).GetAsync($"/api/changes/{id}");

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("This change is more than 30 days old. Changes can be undone for 30 days.", await Error(late));
        Assert.Equal("Tech", Value(Row(Jordan), "title"));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Changes_that_cannot_be_undone_say_why_and_get_no_undo()
    {
        await Connect();
        var admin = await SignedIn();

        var reset = (await Json(await admin.PostAsJsonAsync("/api/ad/users/jreyes/password", new { password = "New pass 1", mustChange = false }))).GetProperty("change");
        var refused = await admin.PostAsync($"/api/changes/{reset.GetProperty("id").GetString()}/undo", null);
        var deleted = (await Json(await admin.PostAsJsonAsync("/api/ad/users/pat/delete", new { }))).GetProperty("change");

        Assert.False(reset.GetProperty("undo").GetBoolean());
        Assert.Equal("The previous password is not stored, so a password reset cannot be undone.", reset.GetProperty("why").GetString());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("password reset cannot be undone", await Error(refused));
        Assert.False(deleted.GetProperty("undo").GetBoolean());
        Assert.Contains("Recycle Bin is not enabled", deleted.GetProperty("why").GetString());
    }

    [Fact]
    public async Task Several_are_undone_at_once_newest_first_and_each_is_answered_on_its_own()
    {
        await Connect();
        var admin = await SignedIn();
        async Task<string> Change(string path, object body) => (await Json(await admin.PostAsJsonAsync(path, body))).GetProperty("change").GetProperty("id").GetString()!;
        var first = await Change("/api/ad/users/jreyes/edit", new { department = "Sales" });
        var second = await Change("/api/ad/users/jreyes/edit", new { department = "Marketing" });
        var joined = await Change("/api/ad/users/jreyes/groups", new { add = new[] { Staff } });
        var reset = await Change("/api/ad/users/jreyes/password", new { password = "New pass 1" });

        var unconfirmed = await (await SignedIn(confirmed: false)).PostAsJsonAsync("/api/changes/undo", new { ids = new[] { joined } });
        var answer = await Json(await admin.PostAsJsonAsync("/api/changes/undo", new { ids = new[] { first, second, joined, reset, "nothere" } }));
        var results = answer.GetProperty("results").EnumerateArray().ToDictionary(r => r.GetProperty("id").GetString()!);

        Assert.Equal(HttpStatusCode.Forbidden, unconfirmed.StatusCode);
        Assert.Equal(3, answer.GetProperty("done").GetInt32());
        Assert.Equal("3 changes were undone.", answer.GetProperty("said").GetString());
        Assert.True(results[first].GetProperty("ok").GetBoolean());
        Assert.True(results[second].GetProperty("ok").GetBoolean());
        Assert.True(results[joined].GetProperty("ok").GetBoolean());
        Assert.False(results[reset].GetProperty("ok").GetBoolean());
        Assert.Contains("password reset cannot be undone", results[reset].GetProperty("error").GetString());
        Assert.Contains("no longer in the change history", results["nothere"].GetProperty("error").GetString());
        Assert.Null(Value(Row(Jordan), "department"));
        Assert.DoesNotContain(Jordan, Values(Row(Staff), "member"));
    }

    [Fact]
    public async Task The_journal_is_kept_in_the_data_folder_and_read_back()
    {
        await Connect();
        var made = await Made(Permissions.AdChange, () => Tools.Edit("jreyes", new UserEdit(null, null, null, null, null, null, null, "Tech", null, null, null), None));
        Journal.Undone(made!.Id, "admin", null);
        var again = new ChangeJournal(host.Services.GetRequiredService<DataFolder>(), app.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<ChangeJournal>.Instance);

        var read = again.Find(made.Id);

        Assert.True(File.Exists(Path.Combine(host.Services.GetRequiredService<DataFolder>().Path, "changes.jsonl")));
        Assert.Equal(("Job title", "Tech"), (read!.Steps[0].Label, read.Steps[0].After[0]));
        Assert.Equal("admin", read.UndoneBy);
    }

    // --- A browser on the host with the real tools ---------------------------------------------------------------

    private async Task<HttpClient> SignedIn(string username = "admin", string password = "correct horse", bool confirmed = true)
    {
        var page = host.CreateDefaultClient(new Uri("http://localhost"), new AutoMfa(app), new CookieContainerHandler());
        page.DefaultRequestHeaders.Add("X-Rabbid-Lab", "1");
        if (!(await page.PostAsJsonAsync("/api/setup/local", new { username, password })).IsSuccessStatusCode)
        {
            (await page.PostAsJsonAsync("/api/signin", new { username, password })).EnsureSuccessStatusCode();
        }
        if (confirmed)
        {
            (await page.PostAsJsonAsync("/api/confirm", new { password, code = app.MfaSecrets.ContainsKey(username) ? app.MfaCode(username) : null })).EnsureSuccessStatusCode();
        }
        return page;
    }

    private static async Task<string> Role(HttpClient admin, string name, params string[] permissions)
    {
        var id = (await Json(await admin.PostAsJsonAsync("/api/roles", new { name, color = "#5FD4A0" }))).GetProperty("id").GetString()!;
        (await admin.PostAsJsonAsync($"/api/roles/{id}", new { name, color = "#5FD4A0", permissions })).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> Error(HttpResponseMessage r) => (await Json(r)).GetProperty("error").GetString()!;
}

/// <summary>
/// FakeLdap that also makes the changes it's asked for, as Active Directory would: values set, group members and their
/// memberOf kept in step, renames carried into memberships, deletes into the Recycle Bin and back, and finding by objectGUID.
/// </summary>
public sealed partial class LiveLdap(FakeLdap ad) : ILdap
{
    private const string Bin = "CN=Deleted Objects,DC=amw,DC=com";

    public string BaseDn => ad.BaseDn;

    public bool Encrypted => ad.Encrypted;

    public async Task<ILdap?> Open(string domain, string host, string username, string password, CancellationToken stop) =>
        await ad.Open(domain, host, username, password, stop) is null ? null : this;

    [GeneratedRegex(@"\(objectGUID=((?:\\[0-9a-fA-F]{2}){16})\)")]
    private static partial Regex GuidShape();

    private static byte[]? GuidIn(string filter) => GuidShape().Match(filter) is { Success: true } m
        ? [.. m.Groups[1].Value.Split('\\', StringSplitOptions.RemoveEmptyEntries).Select(h => Convert.ToByte(h, 16))] : null;

    private static List<LdapRow> WithGuid(List<FakeLdap.Item> rows, byte[] id) =>
        Copies([.. rows.Where(r => r.Values.TryGetValue("objectGUID", out var g) && g.FirstOrDefault() is byte[] b && b.SequenceEqual(id)).Take(1).Select(r => new LdapRow(r.Dn, r.Values))], rows);

    // Each answer is a copy of what's there now, as Active Directory sends, so a row read before a change keeps its values.
    public async Task<List<LdapRow>> Find(string root, string filter, bool subtree, string[] attributes, int limit, CancellationToken stop) =>
        GuidIn(filter) is { } id ? WithGuid(ad.Rows, id) : Copies(await ad.Find(root, filter, subtree, attributes, limit, stop), ad.Rows);

    public async Task<List<LdapRow>> FindDeleted(string root, string filter, bool subtree, string[] attributes, int limit, CancellationToken stop) =>
        GuidIn(filter) is { } id ? WithGuid(ad.Deleted, id) : Copies(await ad.FindDeleted(root, filter, subtree, attributes, limit, stop), ad.Deleted);

    private static List<LdapRow> Copies(List<LdapRow> found, List<FakeLdap.Item> rows) =>
        [.. found.Select(f => rows.First(r => Same(r.Dn, f.Dn))).Select(r => new LdapRow(r.Dn, new Dictionary<string, object[]>(r.Values, StringComparer.OrdinalIgnoreCase)))];

    public Task<List<LdapRow>> FindChildren(string dn, string[] attributes, int limit, CancellationToken stop) => ad.FindChildren(dn, attributes, limit, stop);

    public Task Add(string dn, IReadOnlyDictionary<string, object[]> attributes, CancellationToken stop) => ad.Add(dn, attributes, stop);

    public async Task Modify(string dn, IReadOnlyList<LdapMod> changes, CancellationToken stop)
    {
        await ad.Modify(dn, changes, stop);
        if (ad.Rows.FirstOrDefault(r => Same(r.Dn, dn)) is not { } row)
        {
            return;
        }
        foreach (var m in changes)
        {
            var had = row.Values.TryGetValue(m.Attribute, out var h) ? h.ToList() : [];
            var left = m.Change switch
            {
                LdapChange.Replace => [.. m.Values],
                LdapChange.Add => [.. had, .. m.Values],
                _ => m.Values.Length == 0 ? [] : had.Where(x => !m.Values.Any(v => v is string s && x is string t ? Same(s, t) : Equals(v, x))).ToList(),
            };
            if (left.Count == 0)
            {
                row.Values.Remove(m.Attribute);
            }
            else
            {
                row.Values[m.Attribute] = [.. left];
            }
            if (m.Attribute.Equals("member", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var who in m.Values.OfType<string>().Select(v => ad.Rows.FirstOrDefault(r => Same(r.Dn, v))).OfType<FakeLdap.Item>())
                {
                    var of = who.Values.TryGetValue("memberOf", out var o) ? o.OfType<string>().ToList() : [];
                    of.RemoveAll(x => Same(x, dn));
                    if (m.Change == LdapChange.Add)
                    {
                        of.Add(dn);
                    }
                    who.Values["memberOf"] = [.. of];
                }
            }
        }
    }

    public async Task Delete(string dn, CancellationToken stop)
    {
        var row = ad.Rows.FirstOrDefault(r => Same(r.Dn, dn));
        await ad.Delete(dn, stop);
        if (row?.Values.GetValueOrDefault("objectGUID")?.FirstOrDefault() is byte[] g)
        {
            var values = new Dictionary<string, object[]>(row.Values, StringComparer.OrdinalIgnoreCase)
            {
                ["isDeleted"] = ["TRUE"], ["msDS-LastKnownRDN"] = [Ldap.Name(dn)], ["lastKnownParent"] = [Ldap.Parent(dn)],
            };
            ad.Deleted.Add(new FakeLdap.Item($@"CN={Ldap.Name(dn)}\0ADEL:{new Guid(g)},{Bin}", values));
        }
    }

    public Task WriteDacl(string dn, byte[] descriptor, CancellationToken stop) => ad.WriteDacl(dn, descriptor, stop);

    public async Task Rename(string dn, string rdn, string parent, CancellationToken stop)
    {
        await ad.Rename(dn, rdn, parent, stop);
        var to = $"{rdn},{parent}";
        foreach (var r in ad.Rows)
        {
            foreach (var a in new[] { "member", "memberOf" })
            {
                if (r.Values.TryGetValue(a, out var v))
                {
                    r.Values[a] = [.. v.Select(x => x is string s && Same(s, dn) ? to : x)];
                }
            }
        }
    }

    public async Task Restore(string dn, string newDn, CancellationToken stop)
    {
        await ad.Restore(dn, newDn, stop);
        if (ad.Deleted.FirstOrDefault(r => Same(r.Dn, dn)) is { } row)
        {
            ad.Deleted.Remove(row);
            var values = new Dictionary<string, object[]>(row.Values, StringComparer.OrdinalIgnoreCase);
            foreach (var a in new[] { "isDeleted", "msDS-LastKnownRDN", "lastKnownParent" })
            {
                values.Remove(a);
            }
            ad.Rows.Add(new FakeLdap.Item(newDn, values));
        }
    }

    public Task<Dictionary<string, byte[][]>?> ReadAll(string dn, CancellationToken stop) => ad.ReadAll(dn, stop);

    public Task<string?> WhoAmI(CancellationToken stop) => ad.WhoAmI(stop);

    public void Dispose()
    {
        // The one connection is handed out again: nothing to close.
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
