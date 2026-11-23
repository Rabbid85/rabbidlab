using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// Connected apps against an app kept in memory and a stand-in for Active Directory: how accounts match, what the rules
/// owe, that one rule can give several groups, and that nothing changes in the app except what is applied.
/// </summary>
public sealed class ConnectedAppsTests : IDisposable
{
    private const string Lab = "OU=Lab,DC=amw,DC=com";
    private const string Staff = $"CN=Lab Staff,{Lab}";
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-apps-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;
    private readonly FakeLdap ad = new();
    private readonly DirectoryTools tools;
    private readonly FakeApp there = new();
    private readonly ConnectedApps apps;
    private readonly ActivityLog log;

    public ConnectedAppsTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
        var s = app.Services;
        tools = new DirectoryTools(s.GetRequiredService<DataFolder>(), s.GetRequiredService<SettingsStore>(), s.GetRequiredService<Accounts>(), app.Finder, ad.Open);
        log = s.GetRequiredService<ActivityLog>();
        apps = new ConnectedApps(s.GetRequiredService<DataFolder>(), s.GetRequiredService<SettingsStore>(), [there], tools, app.Clock, log);
        // Jordan, Pat and Anthony are in Lab Staff; Jane is not.
        Person("jreyes", "jreyes@amw.com", staff: true);
        Person("pat", "pat@amw.com", staff: true);
        Person("awiles", "awiles@amw.com", staff: true);
        Person("jsmith", "jane.smith@amw.com", staff: false);
        there.Accounts.AddRange([Account("a1", "jreyes@amw.com"), Account("a2", "jane.smith@amw.com"), Account("a3", "awiles@amw.com"), Account("a4", "awiles@amw.com"), Account("admin", "")]);
        there.Groups.AddRange([new AppGroup { Id = "g1", Name = "Editors", Type = "Role", Members = ["a2"] }, new AppGroup { Id = "g2", Name = "Viewers", Type = "Role" }]);
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

    private void Person(string sam, string mail, bool staff)
    {
        var row = ad.Rows.First(r => r.Values.TryGetValue("sAMAccountName", out var v) && (string) v[0] == sam);
        row.Values["mail"] = [mail];
        if (staff)
        {
            row.Values["memberOf"] = [Staff];
        }
    }

    private static AppAccount Account(string id, string email) => new() { Id = id, Name = id, Email = email, Login = id };

    private async Task<string> Connected()
    {
        await tools.Connect(new SetupForm("amw.com", "svc-existing", "right", Existing: true, null, null, Lab), "admin", None);
        return (await apps.Connect(new ConnectedAppForm(null, "fake", "Help desk", new() { ["url"] = "https://helpdesk.amw.com" }, "the token", "email", true), "admin", None)).Id;
    }

    private Task<List<PlanItem>> Plan() => apps.Plan(None);

    [Fact]
    public async Task Connecting_reads_the_app_once_keeps_its_secret_encrypted_and_matches_accounts_by_email()
    {
        var id = await Connected();
        var kept = apps.Read().Apps.Single();
        var match = ConnectedApps.Match(kept, await apps.People(None));
        var signedInWith = there.Secrets.Last();
        there.Fail = "The token was refused.";
        var refused = await Assert.ThrowsAsync<AppProblem>(() => apps.Connect(new ConnectedAppForm(null, "fake", "Another", new() { ["url"] = "https://x" }, "t", "email", true), "admin", None));

        Assert.Equal(id, kept.Id);
        Assert.Equal(5, kept.Accounts.Count);
        Assert.NotEqual("the token", kept.Secret);
        Assert.DoesNotContain("the token", File.ReadAllText(Path.Combine(data, "connected-apps.json")));
        Assert.Equal("the token", signedInWith);
        Assert.Equal("jreyes", match.UserOf["a1"]);
        Assert.Equal("jsmith", match.UserOf["a2"]);
        Assert.Contains("awiles", match.Torn); // two accounts claim Anthony: neither is his until one is picked
        Assert.False(match.UserOf.ContainsKey("a3"));
        Assert.False(match.UserOf.ContainsKey("admin"));
        Assert.Equal("The token was refused.", refused.Message);
        Assert.Single(apps.Read().Apps); // one that can't be read isn't saved
    }

    [Fact]
    public async Task One_rule_gives_several_roles_and_the_plan_says_what_is_owed_what_is_blocked_and_what_was_given_by_hand()
    {
        var id = await Connected();
        var rule = apps.SaveRule(new AccessRuleForm(null, Staff, "Lab Staff", [new() { App = id, Group = "g1" }, new() { App = id, Group = "g2" }], "auto"), "admin");

        var plan = await Plan();

        Assert.Equal(2, rule.To.Count);
        Assert.Equal(["g1", "g2"], plan.Where(i => i.Kind == "add" && i.User == "jreyes").Select(i => i.Group));
        Assert.Single(plan, i => i.Kind == "create" && i.User == "pat"); // one account, though it joins two roles
        Assert.Equal(2, plan.Count(i => i.Kind == "add" && i.User == "pat"));
        Assert.All(plan.Where(i => i.User == "awiles"), i => Assert.Equal("blocked", i.Kind));
        var drift = Assert.Single(plan, i => i.Kind == "drift");
        Assert.Equal(("jsmith", "g1", "a2"), (drift.User, drift.Group, drift.Account));
        Assert.Empty(there.Calls); // working out the plan changes nothing
    }

    [Fact]
    public async Task Applying_makes_the_account_first_then_gives_the_roles_and_only_what_was_picked()
    {
        var id = await Connected();
        apps.SaveRule(new AccessRuleForm(null, Staff, "Lab Staff", [new() { App = id, Group = "g1" }, new() { App = id, Group = "g2" }], "hand"), "admin");
        var plan = await Plan();

        var (done, failed) = await apps.Apply([.. plan.Where(i => i.User == "pat").Select(i => i.Key)], "admin", None);
        var after = await Plan();

        Assert.Equal((3, 0), (done, failed));
        Assert.Equal(["create pat", "add g1 new-pat", "add g2 new-pat"], there.Calls);
        Assert.DoesNotContain(after, i => i.User == "pat");
        Assert.Equal(2, after.Count(i => i.Kind == "add" && i.User == "jreyes")); // not picked: still owed
        Assert.Contains(apps.Read().Apps.Single().Log, l => l.Text.StartsWith("Made user new-pat for Pat Primary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_role_given_by_hand_is_only_taken_away_when_applied_and_an_account_left_in_nothing_is_disabled()
    {
        var id = await Connected();
        apps.SaveRule(new AccessRuleForm(null, Staff, "Lab Staff", [new() { App = id, Group = "g1" }], "auto"), "admin");
        var owed = await apps.Apply([], "Rabbid Lab", None, onlyAuto: true);
        var still = there.Groups[0].Members.Contains("a2");
        var drift = (await Plan()).Single(i => i.Kind == "drift");

        await apps.Apply([drift.Key], "admin", None);

        Assert.Equal(3, owed.Done); // Pat's account and role, and Jordan's role
        Assert.True(still); // a sync never takes a role away by itself
        Assert.Contains("remove g1 a2", there.Calls);
        Assert.Contains("disable a2", there.Calls);
        Assert.DoesNotContain(await Plan(), i => i.Kind == "drift");
    }

    [Fact]
    public async Task Picking_one_of_two_accounts_by_hand_unblocks_the_person_and_keeping_a_role_stops_it_showing()
    {
        var id = await Connected();
        var rule = apps.SaveRule(new AccessRuleForm(null, Staff, "Lab Staff", [new() { App = id, Group = "g1" }], "hand"), "admin");

        apps.Pick(id, "a3", "awiles", "admin");
        apps.Pick(id, "a4", "-", "admin");
        apps.Keep(rule.Id, id, "g1", "a2");
        var plan = await Plan();
        await apps.Sync(id, "admin", None);

        Assert.Contains(plan, i => i.Kind == "add" && i.User == "awiles" && i.Account == "a3");
        Assert.DoesNotContain(plan, i => i.Kind is "blocked" or "drift");
        Assert.Equal("awiles", apps.Read().Apps.Single().Accounts.Single(a => a.Id == "a3").Pick); // a match by hand survives the next read
    }

    [Fact]
    public async Task A_sync_that_fails_keeps_what_was_read_says_why_and_is_logged_once()
    {
        var id = await Connected();
        there.Fail = "helpdesk.amw.com answered 401";

        var first = await apps.Sync(id, "Rabbid Lab", None);
        var second = await apps.Sync(id, "Rabbid Lab", None);
        var failing = apps.Read().Apps.Single();
        there.Fail = null;
        var third = await apps.Sync(id, "Rabbid Lab", None);

        Assert.False(first);
        Assert.False(second);
        Assert.Equal("helpdesk.amw.com answered 401", failing.Problem);
        Assert.Equal(5, failing.Accounts.Count);
        var said = Assert.Single(log.Recent(), e => e.Action == "apps.sync.failed");
        Assert.Equal("apps.sync", EventAlerts.FromActivity(said)!.Event);
        Assert.True(third);
        Assert.Null(apps.Read().Apps.Single().Problem);
    }
}

/// <summary>An app kept in memory: it reads back what it holds, makes each change it's asked for, and notes it.</summary>
public sealed class FakeApp : IAppConnector
{
    public List<AppAccount> Accounts { get; } = [];
    public List<AppGroup> Groups { get; } = [];
    public List<string> Calls { get; } = [];
    public List<string?> Secrets { get; } = [];

    /// <summary>What every call fails with, or null.</summary>
    public string? Fail { get; set; }

    public string Kind => "fake";
    public string Name => "Fake app";
    public string About => "For tests.";
    public string Account => "user";
    public string Group => "role";
    public bool CanCreate => true;
    public IReadOnlyList<ConnectorField> Fields { get; } = [new("url", "URL"), new("token", "Token", Secret: true)];

    public string? Bad(AppLink link) => link["url"].Length == 0 ? "Enter the URL." : null;

    private void Check(AppLink link)
    {
        Secrets.Add(link.Secret);
        if (Fail is { } why)
        {
            throw new AppProblem(why);
        }
    }

    public Task<AppSnapshot> Read(AppLink link, CancellationToken stop)
    {
        Check(link);
        return Task.FromResult(new AppSnapshot([.. Accounts.Select(a => new AppAccount { Id = a.Id, Name = a.Name, Email = a.Email, Login = a.Login, Active = a.Active })],
            [.. Groups.Select(g => new AppGroup { Id = g.Id, Name = g.Name, Type = g.Type, Members = [.. g.Members] })]));
    }

    public Task<string> Create(AppLink link, AppPerson who, CancellationToken stop)
    {
        Check(link);
        Calls.Add($"create {who.Username}");
        Accounts.Add(new AppAccount { Id = $"new-{who.Username}", Name = who.Name, Email = who.Email, Login = who.Username });
        return Task.FromResult($"new-{who.Username}");
    }

    public Task AddMember(AppLink link, AppGroup group, AppAccount account, CancellationToken stop)
    {
        Check(link);
        Calls.Add($"add {group.Id} {account.Id}");
        Groups.First(g => g.Id == group.Id).Members.Add(account.Id);
        return Task.CompletedTask;
    }

    public Task RemoveMember(AppLink link, AppGroup group, AppAccount account, CancellationToken stop)
    {
        Check(link);
        Calls.Add($"remove {group.Id} {account.Id}");
        Groups.First(g => g.Id == group.Id).Members.Remove(account.Id);
        return Task.CompletedTask;
    }

    public Task Disable(AppLink link, AppAccount account, CancellationToken stop)
    {
        Check(link);
        Calls.Add($"disable {account.Id}");
        Accounts.First(a => a.Id == account.Id).Active = false;
        return Task.CompletedTask;
    }
}
