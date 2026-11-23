using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Alerts people add: what happened read from the activity log and the AD audit, rules matched with their conditions, counts, devices, acknowledging and a workflow's approval.</summary>
public sealed class EventAlertsTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-alerts-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public EventAlertsTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
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

    private static ActivityEntry Act(string action, string title, string by = "admin", string result = "ok") => new(DateTime.UtcNow, by, "10.0.0.5", "users", action, title, result, null);

    private static AuditEntry Audit(string what, string dn, string kind, string attribute, List<string> old, List<string> now, string account = "AMW\\priya", string op = "op1") =>
        new($"DC01:{op}", DateTime.UtcNow, "DC01", 5136, op, what, dn, null, kind, attribute, old, now, account, "Priya Nair");

    [Fact]
    public void The_activity_log_becomes_what_alerts_are_about()
    {
        Assert.Equal(("signin.fail", "jsmith"), Pair(EventAlerts.FromActivity(Act("signin.failed", "Sign-in failed", "jsmith", "failed"))));
        Assert.Null(EventAlerts.FromActivity(Act("signin.failed", "Sign-in failed", SignIn.Unknown, "failed")));
        Assert.Equal(("ad.password", "gsutton"), Pair(EventAlerts.FromActivity(Act("ad.password", "Reset the password for gsutton (must change at next sign-in)"))));
        Assert.Equal("rl.role", EventAlerts.FromActivity(Act("role.member.add", "Added Owen Park to Administrators"))?.Event);
        Assert.Equal(("rl.flow", "gsutton"), Pair(EventAlerts.FromActivity(Act("ad.offboard.stage", "Ran offboarding stage 1 of 3 for gsutton", result: "failed"))));
        Assert.Null(EventAlerts.FromActivity(Act("ad.offboard.stage", "Ran offboarding stage 1 of 3 for gsutton")));
        Assert.Equal("dev.new", EventAlerts.FromActivity(Act("client.join", "GREG-LT connected and is pending approval"))?.Event);
        Assert.Null(EventAlerts.FromActivity(Act("gpo.view", "Opened Default Domain Policy")));
    }

    private static (string, string?) Pair(AlertFact? f) => (f?.Event ?? "", f?.Subject);

    [Fact]
    public void The_ad_audit_becomes_group_changes_deletes_and_changes_made_elsewhere()
    {
        var facts = EventAlerts.FromAudit(
        [
            Audit("changed", "CN=Domain Admins,CN=Users,DC=amw,DC=com", "group", "member", [], ["CN=Owen Park,OU=IT,DC=amw,DC=com"]),
            Audit("deleted", "CN=Sam Holt,OU=Staff,DC=amw,DC=com", "user", AuditEntries.Whole, [], [], op: "op2"),
            Audit("changed", "CN=Tom Becker,OU=Staff,DC=amw,DC=com", "user", "userAccountControl", ["512"], ["514"], "AMW\\svc-rabbidlab", "op3"),
        ], "svc-rabbidlab");
        var added = Assert.Single(facts, f => f.Event == "ad.group.add");
        Assert.Equal(("Owen Park was added to Domain Admins", "Domain Admins", "Owen Park"), (added.Title, added.Group, added.SubjectName));
        Assert.Contains(facts, f => f.Event == "ad.delete" && f.What == "user" && f.SubjectName == "Sam Holt");
        Assert.Contains(facts, f => f.Event == "ad.disable" && f.Title == "The user Tom Becker was disabled" && f.Text.StartsWith("By Rabbid Lab", StringComparison.Ordinal));
        // Changed elsewhere: once per operation, and never for the service account.
        Assert.Equal(2, facts.Count(f => f.Event == "ad.outside"));
    }

    [Fact]
    public void A_rule_matches_its_event_its_person_and_its_conditions()
    {
        var group = new EventRule { Event = "ad.group.add", Conditions = [new() { Field = "group", Op = "is", Value = "Domain Admins" }] };
        Assert.True(EventAlerts.Matches(group, new("ad.group.add", "t", "x", Group: "domain admins")));
        Assert.False(EventAlerts.Matches(group, new("ad.group.add", "t", "x", Group: "VPN users")));
        var about = new EventRule { Event = "signin.lock", About = "gsutton", AboutName = "Greg Sutton" };
        Assert.True(EventAlerts.Matches(about, new("signin.lock", "t", "x", "GSUTTON")));
        Assert.True(EventAlerts.Matches(about, new("signin.lock", "t", "x", SubjectName: "Greg Sutton")));
        Assert.False(EventAlerts.Matches(about, new("signin.lock", "t", "x", "lortiz")));
        var servers = new EventRule { Event = "dev.updates", Computers = "servers" };
        Assert.True(EventAlerts.Matches(servers, new("dev.updates", "t", "x", Os: "Windows Server 2022")));
        Assert.False(EventAlerts.Matches(servers, new("dev.updates", "t", "x", Os: "Windows 11 Pro")));
        Assert.False(EventAlerts.Matches(new EventRule { Event = "dev.updates", On = false }, new("dev.updates", "t", "x")));
    }

    [Fact]
    public async Task Rules_are_added_in_the_page_and_fire_after_their_count()
    {
        var page = await SignedIn();
        var r = await page.PostAsJsonAsync("/api/event-alerts/rules", new { @event = "signin.fail", times = 3, minutes = 10, severity = "critical" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var notYet = await page.PostAsJsonAsync("/api/event-alerts/rules", new { @event = "dev.admins" });
        Assert.Equal(HttpStatusCode.BadRequest, notYet.StatusCode);
        var badField = await page.PostAsJsonAsync("/api/event-alerts/rules", new { @event = "signin.fail", conditions = new[] { new { field = "group", op = "is", value = "Sales" } } });
        Assert.Equal(HttpStatusCode.BadRequest, badField.StatusCode);

        var alerts = app.Services.GetRequiredService<EventAlerts>();
        var fact = new AlertFact("signin.fail", "jsmith failed to sign in", "From 10.0.0.5.", "jsmith");
        Assert.Equal(0, await alerts.Handle(fact, CancellationToken.None));
        Assert.Equal(0, await alerts.Handle(fact, CancellationToken.None));
        Assert.Equal(1, await alerts.Handle(fact, CancellationToken.None)); // 3 times in 10 minutes
        var fired = Assert.Single(alerts.Fired());
        Assert.Equal((id, "critical", "new", "nobody"), (fired.Rule, fired.Severity, fired.State, fired.Sent));
        Assert.StartsWith("3 times in 10 minutes.", fired.Text, StringComparison.Ordinal);

        var ack = await page.PostAsJsonAsync("/api/event-alerts/ack", new { ids = (string[]?) null });
        Assert.Equal(1, (await ack.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("acknowledged").GetInt32());
        Assert.Equal("ack", alerts.Fired()[0].State);

        Assert.Equal(HttpStatusCode.OK, (await page.PostAsync($"/api/event-alerts/rules/{id}/delete", null)).StatusCode);
        Assert.DoesNotContain(alerts.Rules(), x => x.Id == id);
        Assert.Equal(10, alerts.Rules().Count); // the alerts there to start with: AD security, and the service account
        Assert.Contains(alerts.Rules(), x => x.Id == AlertEvents.ServiceAccountRule && x.Event == "rl.svc" && x.Severity == "critical");
    }

    [Fact]
    public async Task The_ad_security_alerts_are_there_to_start_with_and_change_like_any_other()
    {
        var alerts = app.Services.GetRequiredService<EventAlerts>();
        var admins = Assert.Single(alerts.Rules(), r => r.Id == "p-admins");
        Assert.Equal(("Added to a privileged group", "critical", "builtin"), (admins.Name, admins.Severity, admins.From));
        var added = new AlertFact("ad.group.add", "Owen Park was added to Domain Admins", "By Priya Nair.", SubjectName: "Owen Park", Group: "Domain Admins", What: "group", By: "Priya Nair", At: DateTime.UtcNow);
        Assert.Equal(1, await alerts.Handle(added, CancellationToken.None));
        Assert.Equal(0, await alerts.Handle(added with { Group = "VPN users" }, CancellationToken.None));
        // Our own condition on a starting alert: not when Priya Nair makes the change.
        var (saved, problem) = alerts.Save("p-admins", new EventRuleForm(Conditions: [.. admins.Conditions, new RuleCondition { Field = "by", Op = "isnot", Value = "Priya Nair" }]), "admin", [], []);
        Assert.Null(problem);
        Assert.Equal(2, saved!.Conditions.Count);
        Assert.Equal(0, await alerts.Handle(added, CancellationToken.None));
        Assert.Equal(1, await alerts.Handle(added with { By = "Tom Becker" }, CancellationToken.None));
        // Anything that happened more than 15 minutes ago, as a first read of a Security log brings, teaches but never alerts.
        Assert.Equal(0, await alerts.Handle(added with { By = "Tom Becker", At = DateTime.UtcNow.AddHours(-3) }, CancellationToken.None));
    }

    [Fact]
    public async Task Password_spraying_counts_different_accounts_from_one_address()
    {
        var alerts = app.Services.GetRequiredService<EventAlerts>();
        alerts.Save("p-spray", new EventRuleForm(Times: 3), "admin", [], []);
        AlertFact Fail(string user, string ip) => new("ad.signin.fail", $"{user} failed to sign in", "Wrong password.", user, Address: ip, Reason: "Wrong password", At: DateTime.UtcNow);
        foreach (var f in new[] { Fail("amy", "10.0.0.9"), Fail("amy", "10.0.0.9"), Fail("ben", "10.0.0.9"), Fail("cal", "10.0.0.7"), Fail("dee", "10.0.0.9") })
        {
            await alerts.Handle(f, CancellationToken.None);
        }
        var spray = Assert.Single(alerts.Fired(), x => x.Rule == "p-spray");
        Assert.StartsWith("3 different accounts in 10 minutes.", spray.Text, StringComparison.Ordinal);
        Assert.Equal("dee", spray.Subject);
    }

    [Fact]
    public void Conditions_compare_text_lists_and_hours()
    {
        var f = new AlertFact("ad.signin.fail", "t", "x", "svc-backup", Address: "10.0.0.5", Computer: "FS01", At: new DateTime(2026, 11, 22, 3, 0, 0, DateTimeKind.Utc));
        Assert.True(EventAlerts.Test(new() { Field = "account", Op = "starts", Value = "svc-" }, f));
        Assert.False(EventAlerts.Test(new() { Field = "account", Op = "noneof", Value = "jsmith, svc-backup" }, f));
        Assert.True(EventAlerts.Test(new() { Field = "computer", Op = "oneof", Value = "fs01, DC01" }, f));
        var hour = int.Parse(EventAlerts.Value("hour", f)!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(EventAlerts.Test(new() { Field = "hour", Op = "between", Value = $"{hour}-{(hour + 1) % 24}" }, f));
        Assert.False(EventAlerts.Test(new() { Field = "hour", Op = "between", Value = $"{(hour + 1) % 24}-{(hour + 2) % 24}" }, f));
        Assert.Equal((22, 6), EventAlerts.Hours("22-6"));
        Assert.Null(EventAlerts.Hours("late"));
    }

    [Fact]
    public void Domain_sign_ins_come_from_the_security_log_and_teach_what_is_usual()
    {
        SecurityEvent Ev(int id, string user, string status, string ip, DateTime at) => new(id, 1, at, "DC01", new Dictionary<string, string> { ["TargetUserName"] = user, ["Status"] = status, ["IpAddress"] = $"::ffff:{ip}" });
        var now = DateTime.UtcNow;
        var facts = EventAlerts.FromSignIns(
        [
            Ev(4771, "jsmith", "0x18", "10.0.0.5", now), Ev(4768, "jsmith", "0x12", "10.0.0.5", now), Ev(4768, "GS-LT$", "0x0", "10.0.0.8", now),
            Ev(4768, "jsmith", "0x0", "10.0.0.5", now), new(4740, 2, now, "DC01", new Dictionary<string, string> { ["TargetUserName"] = "jsmith", ["CallerComputerName"] = "JS-LT" }),
        ], ip => ip == "10.0.0.5" ? "JS-LT" : null);
        Assert.Equal(["ad.signin.fail", "ad.signin.disabled", "ad.signin", "ad.lockout"], facts.Select(f => f.Event));
        Assert.Equal(("Wrong password", "JS-LT", "10.0.0.5"), (facts[0].Reason, facts[0].Computer, facts[0].Address));

        var learning = app.Services.GetRequiredService<SignInLearning>();
        var start = now.AddDays(-20);
        for (var d = 0; d < 20; d++)
        {
            Assert.Null(learning.See("jsmith", "JS-LT", start.AddDays(d).Date.AddHours(14)));
        }
        Assert.Null(learning.See("jsmith", "JS-LT", now.Date.AddHours(14)));
        Assert.Equal("new", learning.See("jsmith", "FS01", now.Date.AddHours(14)));
        Assert.Contains("hours", learning.See("jsmith", "JS-LT", now.Date.AddHours(14).AddHours(12)) ?? "");
        Assert.Null(learning.See("newbie", "PC9", now)); // not learned yet: nothing is unusual
    }

    [Fact]
    public async Task Alert_me_is_about_one_user_and_a_workflow_waits_for_approval()
    {
        var page = await SignedIn();
        var alerts = app.Services.GetRequiredService<EventAlerts>();
        var flows = app.Services.GetRequiredService<PeopleWorkflows>();
        var flow = flows.Save(new FlowForm(null, "Close account", true, [], [new FlowStep { Kind = "disable" }]), "admin");
        var r = await page.PostAsJsonAsync("/api/event-alerts/rules", new { @event = "signin.lock", about = "gsutton", aboutName = "Greg Sutton", from = "user", workflow = flow.Id });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0, await alerts.Handle(new("signin.lock", "lortiz is locked out", "x", "lortiz"), CancellationToken.None));
        Assert.Equal(1, await alerts.Handle(new("signin.lock", "gsutton is locked out", "x", "gsutton"), CancellationToken.None));
        var fired = Assert.Single(alerts.Fired());
        Assert.Equal("waiting", fired.Approval);
        var approve = await page.PostAsJsonAsync($"/api/event-alerts/fired/{fired.Id}/approval", new { state = "approved", run = "run1" });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(("approved", "run1", "ack"), (alerts.Fired()[0].Approval, alerts.Fired()[0].Run, alerts.Fired()[0].State));
    }

    [Fact]
    public async Task A_computer_that_stops_checking_in_alerts_once_and_not_after_a_restart()
    {
        var alerts = app.Services.GetRequiredService<EventAlerts>();
        var (rule, _) = alerts.Save(null, new EventRuleForm(Event: "dev.offline", After: 15, From: "list"), "admin", [], []);
        Assert.NotNull(rule);
        var quiet = new RabbidLab.Server.Client { Id = "c1", Computer = "FS01", Os = "Windows Server 2022", Approved = DateTime.UtcNow.AddDays(-9), LastSeen = DateTime.UtcNow.AddHours(-2) };
        await alerts.Poll([quiet], _ => null, CancellationToken.None); // already so when the server started: noted, not alerted
        Assert.Empty(alerts.Fired());
        var later = new RabbidLab.Server.Client { Id = "c2", Computer = "PRINT01", Approved = DateTime.UtcNow.AddDays(-9), LastSeen = DateTime.UtcNow };
        await alerts.Poll([quiet, later], _ => null, CancellationToken.None);
        later.LastSeen = DateTime.UtcNow.AddMinutes(-20);
        await alerts.Poll([quiet, later], _ => null, CancellationToken.None);
        await alerts.Poll([quiet, later], _ => null, CancellationToken.None);
        Assert.Equal("PRINT01 stopped checking in", Assert.Single(alerts.Fired()).Title);
    }

    private async Task<HttpClient> SignedIn()
    {
        var page = app.CreateDefaultClient(new Uri("http://localhost"), new AutoMfa(app), new CookieContainerHandler());
        page.DefaultRequestHeaders.Add("X-Rabbid-Lab", "1");
        if (!(await page.PostAsJsonAsync("/api/setup/local", new { username = "admin", password = "correct horse" })).IsSuccessStatusCode)
        {
            (await page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "correct horse" })).EnsureSuccessStatusCode();
        }
        return page;
    }
}
