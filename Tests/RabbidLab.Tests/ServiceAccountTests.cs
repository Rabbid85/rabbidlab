using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// The service account once there are two kinds of it: a domain admin made or taken, the sign-in test, what happens
/// while its own sign-in fails, the fixes, and the password Rabbid Lab changes itself. All against FakeLdap.
/// </summary>
public sealed class ServiceAccountTests : IDisposable
{
    private const string Lab = "OU=Lab,DC=amw,DC=com";
    private const string Admins = "CN=Domain Admins,OU=Other,DC=amw,DC=com";
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-svc-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;
    private readonly FakeLdap ad = new() { ChainIsMemberOf = true };
    private readonly DirectoryTools tools;

    public ServiceAccountTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
        var services = app.Services;
        tools = new DirectoryTools(services.GetRequiredService<DataFolder>(), services.GetRequiredService<SettingsStore>(), services.GetRequiredService<Accounts>(), app.Finder, ad.Open);
        // The domain's own SID (S-1-5-21-1-2-3), and Domain Admins under it with RID 512.
        Row("DC=amw,DC=com").Values["objectSid"] = [new byte[] { 1, 4, 0, 0, 0, 0, 0, 5, 21, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0 }];
        ad.Add(Admins, ("objectClass", "group"), ("objectCategory", "group"), ("cn", "Domain Admins"), ("sAMAccountName", "Domain Admins"),
            ("objectSid", new byte[] { 1, 5, 0, 0, 0, 0, 0, 5, 21, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 0, 2, 0, 0 }));
    }

    public void Dispose()
    {
        tools.Dispose();
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

    private FakeLdap.Item Row(string dn) => ad.Rows.First(r => string.Equals(r.Dn, dn, StringComparison.OrdinalIgnoreCase));

    private FakeLdap.Item Account(string sam) => ad.Rows.First(r => r.Values.TryGetValue("sAMAccountName", out var v) && (string) v[0] == sam);

    private void MakeAdmin(string sam) => Account(sam).Values["memberOf"] = [Admins];

    private static SetupForm Form(string username, bool existing, string? kind = null, string? account = null, string? scope = null, string password = "right") =>
        new("amw.com", username, password, existing, account, Lab, scope, kind);

    /// <summary>Set up with an account it was given, limited to the Lab OU, on a server that can read the domain as itself.</summary>
    private async Task Given()
    {
        await tools.Connect(Form("svc-existing", existing: true, scope: Lab), "admin", None);
        tools.AsComputer = (domain, host, stop) => ad.Open(domain, host, "", "right", stop);
    }

    [Fact]
    public async Task Test_sign_in_says_who_signed_in_and_whether_they_are_a_domain_admin_and_changes_nothing()
    {
        ad.Who = @"u:AMW\awiles";
        MakeAdmin("awiles");

        var admin = await tools.TestSignIn(Form("awiles", existing: false), None);
        ad.Who = @"u:AMW\jreyes";
        var person = await tools.TestSignIn(Form("jreyes", existing: false), None);
        var wrong = await Assert.ThrowsAsync<ToolProblem>(() => tools.TestSignIn(Form("awiles", existing: false, password: "wrong"), None));

        Assert.Equal(new SignInTest("awiles", true, 3), admin); // Lab, Lab / People and Other
        Assert.False(person.DomainAdmin);
        Assert.Equal("The username or password is incorrect.", wrong.Message);
        Assert.Empty(ad.Log);
        Assert.Null(tools.State());
    }

    [Fact]
    public async Task A_domain_admin_account_is_made_put_in_Domain_Admins_and_has_its_password_changed_by_Rabbid_Lab()
    {
        ad.Who = @"u:AMW\awiles";

        await tools.Connect(Form("awiles", existing: false, kind: "admin", account: "svc-rabbidlab-admin"), "awiles", None);
        var state = tools.State()!;
        ad.AnyPassword = true; // its password is one only Rabbid Lab knows
        var next = await tools.ChangePassword(None);

        Assert.Equal("admin", state.Kind);
        Assert.True(state.Made);
        Assert.True(state.Rotates);
        Assert.Equal("DC=amw,DC=com", state.Scope); // a domain admin has the whole domain, whatever OU was asked for
        Assert.InRange(state.PasswordNext!.Value, DateTime.UtcNow.AddDays(29), DateTime.UtcNow.AddDays(31));
        var dn = $"CN=svc-rabbidlab-admin,{Lab}";
        Assert.Contains(ad.Mods, m => m.Dn == Admins && m.Mod.Attribute == "member" && (string) m.Mod.Values[0] == dn);
        Assert.DoesNotContain(ad.Log, l => l.StartsWith("dacl", StringComparison.Ordinal)); // no delegation: Domain Admins is the permission
        Assert.Equal(2, ad.Mods.Count(m => m.Dn == dn && m.Mod.Attribute == "unicodePwd")); // when made, and the change
        Assert.InRange(next, DateTime.UtcNow.AddDays(29), DateTime.UtcNow.AddDays(31));
        Assert.False(tools.PasswordDue(DateTime.UtcNow.AddDays(29)));
        Assert.True(tools.PasswordDue(DateTime.UtcNow.AddDays(31)));
    }

    [Fact]
    public async Task An_existing_account_is_taken_as_a_domain_admin_only_when_it_is_one_and_its_password_is_left_alone()
    {
        var refused = await Assert.ThrowsAsync<ToolProblem>(() => tools.Connect(Form("svc-existing", existing: true, kind: "admin"), "admin", None));
        var nothing = tools.State();
        MakeAdmin("svc-existing");

        await tools.Connect(Form("svc-existing", existing: true, kind: "admin"), "admin", None);
        var state = tools.State()!;
        var notOurs = await Assert.ThrowsAsync<ToolProblem>(() => tools.ChangePassword(None));

        Assert.Contains("is not a member of Domain Admins", refused.Message);
        Assert.Null(nothing);
        Assert.Equal("admin", state.Kind);
        Assert.False(state.Made);
        Assert.False(state.Rotates);
        Assert.Null(state.PasswordNext);
        Assert.Equal("DC=amw,DC=com", state.Scope);
        Assert.False(tools.PasswordDue(DateTime.UtcNow.AddYears(1)));
        Assert.Equal(StatusCodes.Status409Conflict, notOurs.Status);
        Assert.DoesNotContain(ad.Mods, m => m.Mod.Attribute == "unicodePwd");
    }

    [Fact]
    public async Task While_its_sign_in_fails_lists_are_read_as_this_computer_and_changes_are_refused_until_it_is_unlocked()
    {
        await Given();
        var before = await tools.Check(None);
        var svc = Account("svc-existing");
        svc.Values["lockoutTime"] = ["134000000000000000"];
        ad.Refuse = (name, _) => name.StartsWith("svc-existing", StringComparison.Ordinal) && !ad.Mods.Any(m => m.Dn == svc.Dn && m.Mod.Attribute == "lockoutTime");

        var locked = await tools.Check(None);
        var state = tools.State()!;
        var (users, _) = await tools.Users(None);
        var change = await Assert.ThrowsAsync<ToolProblem>(() => tools.Unlock("jreyes", None));
        var wrong = await Assert.ThrowsAsync<ToolProblem>(() => tools.Repair(new RepairForm("awiles", "wrong", null), None));
        var repaired = await tools.Repair(new RepairForm("awiles", "right", null), None);
        await tools.Unlock("jreyes", None);

        Assert.Equal("ok", before!.State);
        Assert.Equal("locked", locked!.State);
        Assert.True(state.ReadOnly);
        Assert.Equal("locked", state.Problem);
        Assert.Contains("locked out", state.ProblemText);
        Assert.Contains(users, u => u.Username == "jreyes");
        Assert.Equal(DirectoryTools.SignInFailedText, change.Message);
        Assert.Equal(StatusCodes.Status409Conflict, change.Status);
        Assert.Equal("The username or password is incorrect.", wrong.Message);
        Assert.Equal("ok", repaired.State);
        Assert.Null(tools.State()!.Problem);
        Assert.False(tools.State()!.ReadOnly);
    }

    [Fact]
    public async Task A_disabled_account_is_enabled_and_a_changed_password_of_an_account_it_was_given_is_typed_in()
    {
        await Given();
        var svc = Account("svc-existing");
        svc.Values["userAccountControl"] = ["514"];
        ad.Refuse = (name, _) => name.StartsWith("svc-existing", StringComparison.Ordinal) && !ad.Mods.Any(m => m.Dn == svc.Dn && m.Mod.Attribute == "userAccountControl");

        var disabled = await tools.Check(None);
        var enabled = await tools.Repair(new RepairForm("awiles", "right", null), None);
        svc.Values["userAccountControl"] = ["512"]; // the stand-in notes changes without making them
        // Its owner changes the password: the one kept is turned away, the new one signs in.
        ad.AnyPassword = true;
        ad.Refuse = (name, password) => name.StartsWith("svc-existing", StringComparison.Ordinal) && password == "right";
        var rejected = await tools.Check(None);
        var typedWrong = await Assert.ThrowsAsync<ToolProblem>(() => tools.Repair(new RepairForm(null, null, "right"), None));
        var updated = await tools.Repair(new RepairForm(null, null, "the new one"), None);

        Assert.Equal("disabled", disabled!.State);
        Assert.Equal("ok", enabled.State);
        var flags = ad.Mods.Last(m => m.Dn == svc.Dn && m.Mod.Attribute == "userAccountControl");
        Assert.Equal("512", flags.Mod.Values[0]);
        Assert.Contains(ad.Mods, m => m.Dn == svc.Dn && m.Mod.Attribute == "accountExpires" && (string) m.Mod.Values[0] == "0");
        Assert.Equal("rejected", rejected!.State);
        Assert.Contains("rejected that password", typedWrong.Message);
        Assert.Equal("ok", updated.State);
        Assert.DoesNotContain(ad.Mods, m => m.Mod.Attribute == "unicodePwd"); // never reset: it isn't Rabbid Lab's to change
    }

    [Fact]
    public async Task The_watch_says_in_the_activity_log_when_sign_in_starts_failing_once_and_when_it_works_again()
    {
        await Given();
        var log = app.Services.GetRequiredService<ActivityLog>();
        var watch = new ServiceAccountWatch(tools, log, TimeProvider.System, NullLogger<ServiceAccountWatch>.Instance);
        List<ActivityEntry> Said() => [.. log.Recent().Where(e => e.Action.StartsWith("ad.service.", StringComparison.Ordinal)).OrderBy(e => e.At)];

        await watch.Look(None);
        var working = Said();
        ad.Refuse = (name, _) => name.StartsWith("svc-existing", StringComparison.Ordinal);
        await watch.Look(None);
        await watch.Look(None);
        var failing = Said();
        ad.Refuse = null;
        await watch.Look(None);

        Assert.Empty(working);
        var failed = Assert.Single(failing);
        Assert.Equal("ad.service.failed", failed.Action);
        Assert.Equal("failed", failed.Result);
        Assert.Equal("rl.svc", EventAlerts.FromActivity(failed)!.Event);
        Assert.Equal(["ad.service.failed", "ad.service.ok"], Said().Select(e => e.Action));
    }
}
