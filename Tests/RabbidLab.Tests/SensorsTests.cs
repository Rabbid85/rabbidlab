using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>The Sensors page: checks on a timer, down after two failures, emails by each sensor's alert rule, and who may change what.</summary>
public sealed class SensorsTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-sensors-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public SensorsTests()
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

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static JsonElement Named(JsonElement view, string name) => view.GetProperty("sensors").EnumerateArray().First(s => s.GetProperty("name").GetString() == name);

    // A minute passes and the sensors are checked, as the worker does every few seconds.
    private async Task Minute(int minutes = 1)
    {
        var sensors = app.Services.GetRequiredService<Sensors>();
        for (var i = 0; i < minutes; i++)
        {
            app.Clock.Advance(TimeSpan.FromMinutes(1));
            await sensors.Tick(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_sensor_is_down_after_two_failed_checks_and_emails_when_its_rule_says()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "192.168.2.27", name = "ESXi 3", group = "Hypervisors", rule = "normal" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "192.168.2.1", name = "Gateway", rule = "critical" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "192.168.2.40", name = "UPS", rule = "quiet" })).EnsureSuccessStatusCode();
        var no = new Probe(false, Note: "No reply");

        await Minute();
        var fresh = await Json(await (await app.SignedIn()).GetAsync("/api/sensors"));

        // One lost reply isn't down. Two are, but Normal waits five minutes before it emails.
        app.Probe.Finds["192.168.2.27"] = no;
        await Minute();
        var once = Named(await Json(await (await app.SignedIn()).GetAsync("/api/sensors")), "ESXi 3").GetProperty("status").GetString();
        await Minute();
        var twice = Named(await Json(await (await app.SignedIn()).GetAsync("/api/sensors")), "ESXi 3");
        var early = app.Mail.Sent.Count;
        await Minute(4);
        var told = app.Mail.Sent.Select(m => m.Subject).ToList();
        await Minute(30);
        var notAgain = app.Mail.Sent.Count;
        app.Probe.Finds.Remove("192.168.2.27");
        await Minute();
        var back = app.Mail.Sent.Select(m => m.Subject).ToList();

        // Down and straight back: nobody is emailed about it, either way.
        app.Probe.Finds["192.168.2.27"] = no;
        await Minute(3);
        app.Probe.Finds.Remove("192.168.2.27");
        await Minute();
        var blip = app.Mail.Sent.Count;
        var history = (await Json(await (await app.SignedIn()).GetAsync($"/api/sensors/{twice.GetProperty("id").GetString()}"))).GetProperty("events").EnumerateArray().Select(e => e.GetProperty("what").GetString()).ToList();

        // Critical emails as soon as it's down, and again every hour. Quiet never emails.
        app.Probe.Finds["192.168.2.1"] = no;
        app.Probe.Finds["192.168.2.40"] = no;
        await Minute(2);
        var critical = app.Mail.Sent.Select(m => m.Subject).Skip(blip).ToList();
        await Minute(60);
        var again = app.Mail.Sent.Select(m => m.Subject).Skip(blip).ToList();
        var quiet = Named(await Json(await (await app.SignedIn()).GetAsync("/api/sensors")), "UPS").GetProperty("status").GetString();

        Assert.Equal("up", Named(fresh, "ESXi 3").GetProperty("status").GetString());
        Assert.Equal(5, Named(fresh, "ESXi 3").GetProperty("ms").GetInt32());
        Assert.Equal("up", once);
        Assert.Equal("down", twice.GetProperty("status").GetString());
        Assert.Equal("No reply", twice.GetProperty("note").GetString());
        Assert.Equal(0, early);
        Assert.Equal(["ESXi 3 is down"], told);
        Assert.Equal(1, notAgain); // Normal emails once
        Assert.Equal(["ESXi 3 is down", "ESXi 3 is back up"], back);
        Assert.Equal(2, blip);
        Assert.Equal(["up", "down", "email", "up", "email", "down"], history); // newest first: the blip, then back up and its email, then down and its email
        Assert.Equal(["Gateway is down"], critical);
        Assert.Equal(["Gateway is down", "Gateway is still down"], again);
        Assert.Equal("down", quiet);
    }

    [Fact]
    public async Task A_paused_sensor_is_not_checked_and_a_certificate_about_to_expire_is_a_warning()
    {
        var admin = await app.SignedIn();
        var view = await Json(await admin.PostAsJsonAsync("/api/sensors", new { kind = "cert", target = "rabbidlab.amw.com" }));
        var id = Named(view, "rabbidlab.amw.com").GetProperty("id").GetString();
        app.Probe.Finds["rabbidlab.amw.com"] = new Probe(true, 30, "Expires in 12 days", Warn: true);

        await Minute();
        var warned = Named(await Json(await (await app.SignedIn()).GetAsync("/api/sensors")), "rabbidlab.amw.com");
        var mails = app.Mail.Sent.Select(m => m.Subject).ToList();
        var paused = Named(await Json(await (await app.SignedIn()).PostAsJsonAsync($"/api/sensors/{id}/pause", new { paused = true })), "rabbidlab.amw.com");
        app.Probe.Finds["rabbidlab.amw.com"] = new Probe(false, Note: "No reply");
        await Minute(180);
        var still = (await Json(await (await app.SignedIn()).GetAsync($"/api/sensors/{id}"))).GetProperty("sensor");

        Assert.Equal(3600, warned.GetProperty("every").GetInt32()); // hourly unless asked otherwise
        Assert.Equal(443, warned.GetProperty("port").GetInt32());
        Assert.Equal("warn", warned.GetProperty("status").GetString());
        Assert.Equal(["rabbidlab.amw.com: Expires in 12 days"], mails);
        Assert.Equal("paused", paused.GetProperty("status").GetString());
        Assert.Equal("Expires in 12 days", still.GetProperty("note").GetString()); // as it was when it was paused
        Assert.Single(app.Mail.Sent);
    }

    [Fact]
    public async Task Sensors_groups_and_rules_are_only_changed_by_people_allowed_to_and_bad_ones_are_refused()
    {
        var admin = await app.SignedIn();
        static string[] Names(JsonElement j, string list, string field) => [.. j.GetProperty(list).EnumerateArray().Select(e => e.GetProperty(field).GetString() ?? "")];
        static string[] Texts(JsonElement j) => [.. j.GetProperty("groups").EnumerateArray().Select(g => g.GetString() ?? "")];
        var noAddress = await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = " " });
        var notAName = await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "https://nas/" });
        var noPort = await admin.PostAsJsonAsync("/api/sensors", new { kind = "port", target = "nas" });
        var notAPage = await admin.PostAsJsonAsync("/api/sensors", new { kind = "web", target = "ftp://nas" });
        var web = await Json(await admin.PostAsJsonAsync("/api/sensors", new { kind = "web", target = "192.168.2.23", group = "Storage" }));
        (await admin.PostAsJsonAsync("/api/sensors", new { kind = "port", target = "nas", port = 445, name = "Shares", group = "storage" })).EnsureSuccessStatusCode();
        var tried = await Json(await admin.PostAsJsonAsync("/api/sensors/try", new { kind = "ping", target = "192.168.2.23" }));

        var made = await Json(await admin.PostAsJsonAsync("/api/sensors/groups", new { name = "Network" }));
        var ordered = await Json(await admin.PostAsJsonAsync("/api/sensors/groups/order", new { names = new[] { "Network" } }));
        var renamed = await Json(await admin.PostAsJsonAsync("/api/sensors/groups/rename", new { name = "Storage", to = "NAS" }));
        var deleted = await Json(await admin.PostAsJsonAsync("/api/sensors/groups/delete", new { name = "NAS" }));
        var rule = await Json(await admin.PostAsJsonAsync("/api/sensors/rules/relaxed", new { after = 10, again = 360, back = false }));
        var badRule = await admin.PostAsJsonAsync("/api/sensors/rules/relaxed", new { after = 7, again = 0 });

        (await admin.PostAsJsonAsync("/api/accounts", new { username = "viewer", password = "viewer password" })).EnsureSuccessStatusCode();
        var viewer = app.Page();
        (await viewer.PostAsJsonAsync("/api/signin", new { username = "viewer", password = "viewer password" })).EnsureSuccessStatusCode();
        var unseen = await viewer.GetAsync("/api/sensors");
        var refused = await viewer.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "10.0.0.1" });
        var groupRefused = await viewer.PostAsJsonAsync("/api/sensors/groups", new { name = "Mine" });
        var ruleRefused = await viewer.PostAsJsonAsync("/api/sensors/rules/normal", new { after = 0, again = 0 });
        var mailRefused = await viewer.GetAsync("/api/mail");
        var after = await Json(await (await app.SignedIn()).GetAsync("/api/sensors"));

        Assert.All([noAddress, notAName, noPort, notAPage, badRule], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal("http://192.168.2.23/", Named(web, "192.168.2.23").GetProperty("target").GetString());
        Assert.True(tried.GetProperty("ok").GetBoolean());
        Assert.Equal(["Storage", "Network"], Texts(made));
        Assert.Equal(["Network", "Storage"], Texts(ordered));
        Assert.Equal(["NAS", "NAS"], Names(renamed, "sensors", "group"));
        Assert.Equal(["Network"], Texts(deleted));
        Assert.Equal(2, deleted.GetProperty("sensors").GetArrayLength()); // kept, with no group
        var relaxed = rule.GetProperty("rules").EnumerateArray().First(r => r.GetProperty("id").GetString() == "relaxed");
        Assert.Equal(10, relaxed.GetProperty("after").GetInt32());
        Assert.False(relaxed.GetProperty("back").GetBoolean());
        Assert.All([unseen, refused, groupRefused, ruleRefused, mailRefused], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Equal(["Network"], Texts(after)); // the viewer could not make one
    }

    [Fact]
    public async Task Email_settings_keep_their_secrets_and_refuse_what_cannot_work()
    {
        var admin = await app.SignedIn();
        var smtp = new { provider = "smtp", host = "smtp.gmail.com", port = 587, security = "starttls", username = "lab@example.com", password = "app password", from = "lab@example.com", to = new[] { "me@example.com", " me@example.com ", "you@example.com" } };

        var saved = await admin.PostAsJsonAsync("/api/mail", smtp);
        var text = await saved.Content.ReadAsStringAsync();
        var kept = await Json(await admin.PostAsJsonAsync("/api/mail", smtp with { password = "" }));
        var onDisk = await File.ReadAllTextAsync(Path.Combine(data, "mail.json"));
        var badFrom = await admin.PostAsJsonAsync("/api/mail", smtp with { from = "nobody" });
        var badTo = await admin.PostAsJsonAsync("/api/mail", smtp with { to = ["not an address"] });
        var port465 = await admin.PostAsJsonAsync("/api/mail", smtp with { port = 465 });
        var noSecret = await admin.PostAsJsonAsync("/api/mail", new { provider = "m365", tenant = "contoso.onmicrosoft.com", clientId = Guid.NewGuid().ToString(), from = "lab@example.com", to = new[] { "me@example.com" } });
        var badTenant = await admin.PostAsJsonAsync("/api/mail", new { provider = "m365", tenant = "contoso/../x", clientId = Guid.NewGuid().ToString(), clientSecret = "s", from = "lab@example.com", to = new[] { "me@example.com" } });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain("app password", text);
        Assert.DoesNotContain("app password", onDisk);
        Assert.True(kept.GetProperty("hasPassword").GetBoolean());
        Assert.True(kept.GetProperty("ready").GetBoolean());
        Assert.Equal(2, kept.GetProperty("to").GetArrayLength());
        Assert.All([badFrom, badTo, port465, noSecret, badTenant], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
    }
}
