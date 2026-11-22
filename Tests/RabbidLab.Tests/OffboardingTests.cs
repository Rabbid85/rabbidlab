using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// Offboarding in three stages, run by the real tools against a fake domain (LiveLdap): what each stage changes, when it
/// runs, that each change goes in the journal and the log, and the reminder, failure, retry, edit and cancel.
/// </summary>
public sealed class OffboardingTests : IDisposable
{
    private const string Lab = "OU=Lab,DC=amw,DC=com";
    private const string People = $"OU=People,{Lab}";
    private const string Jordan = $"CN=Jordan Reyes,{Lab}";
    private const string Staff = $"CN=Lab Staff,{Lab}";
    private const string Pat = $"CN=Pat Primary,{Lab}";
    private const string Boss = $"CN=Boss,{Lab}";
    private const string Anthony = $"CN=Anthony Wiles,{Lab}";

    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-offboard-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;
    private readonly FakeLdap ad = new();
    private readonly FakeHomes homes = new();
    private readonly WebApplicationFactory<Program> host;

    public OffboardingTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
        var live = new LiveLdap(ad);
        // Jordan reports to Boss, is in Lab Staff, has a phone and a home folder; Pat reports to Jordan.
        Row(Jordan).Values["manager"] = [Boss];
        Row(Jordan).Values["telephoneNumber"] = ["555-0100"];
        Row(Jordan).Values["homeDirectory"] = [@"\\FS01\Home$\jreyes"];
        Row(Jordan).Values["memberOf"] = [Staff];
        Row(Staff).Values["member"] = [Jordan];
        Row(Pat).Values["manager"] = [Jordan];
        Row(Boss).Values["mail"] = ["boss@amw.com"];
        Row(Anthony).Values["mail"] = ["anthony@amw.com"];
        host = app.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<DirectoryTools>();
            s.AddSingleton(p => new DirectoryTools(p.GetRequiredService<DataFolder>(), p.GetRequiredService<SettingsStore>(), p.GetRequiredService<Accounts>(),
                p.GetRequiredService<IDomainFinder>(), live.Open) { AsComputer = null });
            s.RemoveAll<IDirectoryTools>();
            s.AddSingleton<IDirectoryTools>(p => p.GetRequiredService<DirectoryTools>());
            s.RemoveAll<IHomeFolders>();
            s.AddSingleton<IHomeFolders>(homes);
            foreach (var worker in s.Where(d => d.ImplementationType == typeof(OffboardWorker)).ToList())
            {
                s.Remove(worker);
            }
        }));
    }

    public void Dispose()
    {
        host.Dispose();
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

    private Offboarding Off => host.Services.GetRequiredService<Offboarding>();

    private Task Connect() => host.Services.GetRequiredService<DirectoryTools>().Connect(new SetupForm("amw.com", "svc-existing", "right", Existing: true, null, null, Lab), "admin", CancellationToken.None);

    private FakeLdap.Item Row(string dn) => ad.Rows.Single(r => string.Equals(r.Dn, dn, StringComparison.OrdinalIgnoreCase));

    private bool Disabled(string dn) => (int.Parse((string) Row(dn).Values["userAccountControl"][0]) & 2) != 0;

    private static object Everything(string? moveTo = People) => new
    {
        disable = true, password = true, signOut = true, hide = true, manager = true, reportsTo = Anthony, reportsToName = "Anthony Wiles", contact = true, note = true,
        noteText = "Left the company.", email = true, groups = true, keep = Array.Empty<string>(), move = true, moveTo, moveToName = "Lab / People", computers = true,
        archive = true, archiveTo = @"\\FS01\Archive$", purge = true, delete = true,
    };

    private Task<HttpResponseMessage> Offboard(HttpClient page, string when, object? options = null, string user = "jreyes", int days2 = 7, int days3 = 90) =>
        page.PostAsJsonAsync("/api/offboarding", new { users = new[] { user }, when, days2, days3, options = options ?? Everything() });

    private async Task<JsonElement> Plan(HttpClient page, string user = "jreyes") => (await page.GetFromJsonAsync<JsonElement>($"/api/offboarding/{user}")).GetProperty("plan");

    private static IEnumerable<JsonElement> Stages(JsonElement plan) => plan.GetProperty("stages").EnumerateArray();

    private static string Result(JsonElement plan, int stage, string key) =>
        Stages(plan).Single(s => s.GetProperty("n").GetInt32() == stage).GetProperty("steps").EnumerateArray().Single(s => s.GetProperty("key").GetString() == key)
            .GetProperty("result").GetProperty("state").GetString()!;

    [Fact]
    public async Task Now_runs_the_last_day_then_each_later_stage_runs_when_it_is_due()
    {
        await Connect();
        var admin = await SignedIn();
        await TestApp.Notification(admin);

        (await Offboard(admin, "now")).EnsureSuccessStatusCode();
        var first = await Plan(admin);

        // Stage 1, at once: disabled, the reports moved to Anthony, the manager and phone cleared, the note added, the manager told.
        Assert.True(Disabled(Jordan));
        Assert.Equal(Anthony, (string) Row(Pat).Values["manager"][0]);
        Assert.False(Row(Jordan).Values.TryGetValue("manager", out var m) && m.Length > 0);
        Assert.False(Row(Jordan).Values.TryGetValue("telephoneNumber", out var t) && t.Length > 0);
        Assert.StartsWith("Left the company.", (string) Row(Jordan).Values["description"][0]);
        Assert.Equal("done", Stages(first).First().GetProperty("state").GetString());
        Assert.Equal("skipped", Result(first, 1, "signout"));
        Assert.Equal("skipped", Result(first, 1, "hide"));
        var told = Assert.Single(app.Mail.Sent);
        Assert.Equal("Jordan Reyes has been offboarded", told.Subject);
        Assert.Equal(["boss@amw.com"], app.Mail.With.Single().To);
        Assert.Contains(Jordan, (List<string>) [.. Row(Staff).Values["member"].OfType<string>()]);
        Assert.Equal("Stage 2 on " + app.Clock.GetLocalNow().AddDays(7).ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture), first.GetProperty("status").GetString());

        // Nothing more until a week has gone by.
        Assert.Equal(0, await Off.Tick(CancellationToken.None));
        app.Clock.Advance(TimeSpan.FromDays(7));
        Assert.Equal(1, await Off.Tick(CancellationToken.None));
        Assert.DoesNotContain(Jordan, Row(Staff).Values.GetValueOrDefault("member", []).OfType<string>());
        var moved = ad.Rows.Single(r => r.Values.TryGetValue("sAMAccountName", out var s) && (string) s[0] == "jreyes");
        Assert.Equal($"CN=Jordan Reyes,{People}", moved.Dn);
        Assert.Equal([(@"\\FS01\Home$\jreyes", @"\\FS01\Archive$")], homes.Moved);

        // Then deleted, and the archived folder with it.
        app.Clock.Advance(TimeSpan.FromDays(83));
        Assert.Equal(1, await Off.Tick(CancellationToken.None));
        Assert.DoesNotContain(ad.Rows, r => r.Values.TryGetValue("sAMAccountName", out var s) && (string) s[0] == "jreyes");
        Assert.Equal([@"\\FS01\Archive$\jreyes"], homes.Deleted);
        admin = await SignedIn();
        Assert.Equal("Complete", (await Plan(admin)).GetProperty("status").GetString());

        // Each change is its own entry, in the log and in the journal, as if made by hand.
        var entries = (await admin.GetFromJsonAsync<JsonElement>("/api/activity?count=500")).EnumerateArray().ToList();
        var log = entries.Select(e => e.GetProperty("title").GetString()).ToList();
        Assert.Contains("Disabled jreyes", log);
        Assert.Contains("Reset the password for jreyes", log);
        Assert.Contains("Moved jreyes", log);
        Assert.Contains("Deleted the AD user jreyes", log);
        Assert.Contains("Ran offboarding stage 2 of 3 for jreyes", log);
        Assert.Contains("Started offboarding for jreyes", log);
    }

    [Fact]
    public async Task Each_change_of_a_stage_can_be_undone_on_its_own()
    {
        await Connect();
        var admin = await SignedIn();

        (await Offboard(admin, "now", new { disable = true, contact = true })).EnsureSuccessStatusCode();
        var entries = (await admin.GetFromJsonAsync<JsonElement>("/api/activity?count=100")).EnumerateArray().ToList();
        var disabled = entries.First(e => e.GetProperty("title").GetString() == "Disabled jreyes").GetProperty("change").GetString()!;
        var change = host.Services.GetRequiredService<ChangeJournal>().Find(disabled);
        (await admin.PostAsync($"/api/changes/{disabled}/undo", null)).EnsureSuccessStatusCode();

        Assert.Equal((Permissions.AdEnable, "admin"), (change!.Permission, change.By));
        Assert.False(Disabled(Jordan));
        Assert.False(Row(Jordan).Values.TryGetValue("telephoneNumber", out var t) && t.Length > 0); // the other change stays
    }

    [Fact]
    public async Task A_scheduled_offboarding_changes_nothing_until_its_time_and_can_be_changed_or_cancelled()
    {
        await Connect();
        var admin = await SignedIn();
        var friday = app.Clock.GetLocalNow().AddDays(3).Date.AddHours(17).ToString("yyyy-MM-dd'T'HH:mm");

        (await Offboard(admin, friday)).EnsureSuccessStatusCode();
        await Off.Tick(CancellationToken.None);

        Assert.False(Disabled(Jordan));
        Assert.Equal("scheduled", Stages(await Plan(admin)).First().GetProperty("state").GetString());

        // Changed to a later day and to stage 2 two weeks on: the stages are replaced.
        var later = app.Clock.GetLocalNow().AddDays(5).Date.AddHours(9).ToString("yyyy-MM-dd'T'HH:mm");
        (await Offboard(admin, later, days2: 14)).EnsureSuccessStatusCode();
        var changed = await Plan(admin);
        Assert.Equal(14, changed.GetProperty("days2").GetInt32());
        Assert.Equal(3, Stages(changed).Count());

        // Cancelled: nothing runs, even when the day comes.
        var cancelled = await admin.PostAsync("/api/offboarding/jreyes/cancel", null);
        cancelled.EnsureSuccessStatusCode();
        app.Clock.Advance(TimeSpan.FromDays(200));
        Assert.Equal(0, await Off.Tick(CancellationToken.None));
        Assert.False(Disabled(Jordan));
        Assert.Equal("Cancelled", (await Plan(await SignedIn())).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_time_already_past_and_a_change_with_nothing_chosen_are_refused()
    {
        await Connect();
        var admin = await SignedIn();
        var past = app.Clock.GetLocalNow().AddDays(-1).ToString("yyyy-MM-dd'T'HH:mm");

        var late = await Offboard(admin, past);
        var empty = await Offboard(admin, "now", new { disable = false });
        var unconfirmed = await Offboard(await SignedIn(confirmed: false), "now");

        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
        Assert.Equal("Pick a time later than now.", (await late.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unconfirmed.StatusCode);
        Assert.False(Disabled(Jordan));
    }

    [Fact]
    public async Task A_step_that_fails_holds_the_later_stages_until_it_is_retried()
    {
        await Connect();
        var admin = await SignedIn();
        ad.Fail = s => s.StartsWith($"modify {Jordan} userAccountControl", StringComparison.OrdinalIgnoreCase) ? new LdapRefused(LdapRefused.InsufficientRights, "Insufficient access rights") : null;

        (await Offboard(admin, "now")).EnsureSuccessStatusCode();
        var failed = await Plan(admin);
        app.Clock.Advance(TimeSpan.FromDays(8));
        await Off.Tick(CancellationToken.None);

        Assert.Equal("Stage 1 failed", failed.GetProperty("status").GetString());
        Assert.Equal("failed", Result(failed, 1, "disable"));
        Assert.Equal("done", Result(failed, 1, "contact"));
        Assert.Contains(Jordan, Row(Staff).Values["member"].OfType<string>()); // stage 2 waited

        ad.Fail = null;
        admin = await SignedIn();
        (await admin.PostAsync("/api/offboarding/jreyes/run", null)).EnsureSuccessStatusCode();
        var retried = await Plan(admin);

        Assert.True(Disabled(Jordan));
        Assert.Equal("done", Result(retried, 1, "disable"));
        Assert.StartsWith("Stage 2 on", retried.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_person_who_scheduled_it_is_reminded_the_day_before_and_a_stage_they_may_no_longer_run_fails()
    {
        await Connect();
        var admin = await SignedIn();
        await TestApp.Notification(admin);
        var jordan = await host.Services.GetRequiredService<IDirectoryTools>().User("jreyes", CancellationToken.None);
        var when = app.Clock.GetUtcNow().UtcDateTime.AddDays(2);
        // Saved by Anthony, signed in with Active Directory, who is in no role.
        Off.Save(jordan, when, 7, 90, new OffboardOptions { Disable = true }, new Who(SignIn.Directory, "awiles", "AMW", []), @"AMW\awiles");

        app.Clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(5));
        await Off.Tick(CancellationToken.None);
        await Off.Tick(CancellationToken.None);

        var reminder = Assert.Single(app.Mail.Sent);
        Assert.Equal("Offboarding tomorrow: stage 1 for Jordan Reyes", reminder.Subject);
        Assert.Equal(["anthony@amw.com"], app.Mail.With.Single().To);

        app.Clock.Advance(TimeSpan.FromDays(1));
        await Off.Tick(CancellationToken.None);
        var plan = Off.Find("jreyes")!;
        Assert.Equal(Offboarding.Failed, plan.Stages[0].State);
        Assert.Equal(@"AMW\awiles no longer has the Enable and disable accounts permission.", plan.Stages[0].Results.Single().Detail);
        Assert.False(Disabled(Jordan));
    }

    [Fact]
    public async Task The_window_gets_each_persons_groups_reports_and_home_folder()
    {
        await Connect();
        var admin = await SignedIn();

        var look = await (await admin.PostAsJsonAsync("/api/offboarding/look", new { users = new[] { "jreyes" } })).Content.ReadFromJsonAsync<JsonElement>();
        var jordan = look.GetProperty("people")[0];

        Assert.Equal("Jordan Reyes", jordan.GetProperty("name").GetString());
        Assert.Equal("Lab Staff", jordan.GetProperty("groups")[0].GetProperty("name").GetString());
        Assert.Equal("Pat Primary", jordan.GetProperty("reports")[0].GetProperty("name").GetString());
        Assert.Equal(@"\\FS01\Home$\jreyes", jordan.GetProperty("home").GetString());
        Assert.False(look.GetProperty("exchange").GetBoolean());
        Assert.Equal([7, 14, 30], look.GetProperty("days2").EnumerateArray().Select(d => d.GetInt32()));
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
}

/// <summary>Home folders that are only written down: what was moved where, and what was deleted.</summary>
public sealed class FakeHomes : IHomeFolders
{
    public List<(string From, string Into)> Moved { get; } = [];
    public List<string> Deleted { get; } = [];

    public Task<string> Create(string path, string sid, CancellationToken stop) => Task.FromResult(path);

    public Task<string> Move(string path, string intoFolder, CancellationToken stop)
    {
        Moved.Add((path, intoFolder));
        return Task.FromResult($@"{intoFolder}\{path.Split('\\')[^1]}");
    }

    public Task Delete(string path, CancellationToken stop)
    {
        Deleted.Add(path);
        return Task.CompletedTask;
    }
}
