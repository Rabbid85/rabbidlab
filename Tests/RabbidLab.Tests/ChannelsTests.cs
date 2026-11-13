using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Where alerts go: notifications set up once by name, the rules that pick them, quiet hours, and what a sensor keeps.</summary>
public sealed class ChannelsTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-channels-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public ChannelsTests()
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
    public async Task A_notification_needs_a_name_of_its_own_takes_only_its_kind_of_address_and_never_shows_or_stores_it_whole()
    {
        var admin = await app.SignedIn();

        var wrong = await admin.PostAsJsonAsync("/api/channels", new { kind = "discord", name = "Lab", url = "https://example.com/hook" });
        var nameless = await admin.PostAsJsonAsync("/api/channels", new { kind = "discord", url = "https://discord.com/api/webhooks/1302/secrettoken" });
        await TestApp.Notification(admin, "Lab alerts", "discord", "https://discord.com/api/webhooks/1302/secrettoken");
        var twice = await admin.PostAsJsonAsync("/api/channels", new { kind = "ntfy", name = "lab ALERTS", url = "https://ntfy.sh/lab" });
        var listed = await admin.GetStringAsync("/api/channels");

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("Discord webhook", await wrong.Content.ReadAsStringAsync());
        Assert.Contains("Give it a name", await nameless.Content.ReadAsStringAsync());
        Assert.Contains("called lab ALERTS already", await twice.Content.ReadAsStringAsync());
        Assert.Contains("Lab alerts", listed);
        Assert.Contains("…oken", listed);
        Assert.DoesNotContain("secrettoken", listed);
        Assert.DoesNotContain("secrettoken", await File.ReadAllTextAsync(Path.Combine(data, "channels.json")));
    }

    [Fact]
    public async Task An_email_notification_keeps_its_password_encrypted_and_refuses_what_cannot_work()
    {
        var admin = await app.SignedIn();
        object Smtp(object mail) => new { kind = "email", name = "Ops team", mail };

        var noPassword = await admin.PostAsJsonAsync("/api/channels", new { kind = "email", name = "Me", mail = new { provider = "gmail", username = "lab@gmail.com", to = new[] { "me@example.com" } } });
        var nobody = await admin.PostAsJsonAsync("/api/channels", Smtp(new { provider = "smtp", host = "smtp.amw.com", port = 465, security = "ssl", from = "lab@amw.com" }));
        var badFrom = await admin.PostAsJsonAsync("/api/channels", Smtp(new { provider = "smtp", host = "smtp.amw.com", port = 587, from = "nobody", to = new[] { "ops@amw.com" } }));
        var noSecret = await admin.PostAsJsonAsync("/api/channels", Smtp(new { provider = "m365", tenant = "contoso.onmicrosoft.com", clientId = Guid.NewGuid().ToString(), from = "lab@contoso.com", to = new[] { "ops@amw.com" } }));
        var ssl = await admin.PostAsJsonAsync("/api/channels", Smtp(new { provider = "smtp", host = "smtp.amw.com", port = 465, security = "ssl", username = "lab@amw.com", password = "s3cret-pass", from = "lab@amw.com", to = new[] { "ops@amw.com" } }));
        ssl.EnsureSuccessStatusCode();
        var id = (await ssl.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetString();
        // Changed without typing the password again: the stored one stays.
        (await admin.PostAsJsonAsync($"/api/channels/{id}", Smtp(new { provider = "smtp", host = "smtp.amw.com", port = 465, security = "ssl", username = "lab@amw.com", from = "lab@amw.com", to = new[] { "ops@amw.com", "me@amw.com" } }))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/channels/test", new { id })).EnsureSuccessStatusCode();
        var listed = await admin.GetStringAsync("/api/channels");

        Assert.Contains("app password", await noPassword.Content.ReadAsStringAsync());
        Assert.Contains("at least one address", await nobody.Content.ReadAsStringAsync());
        Assert.Contains("From has to be", await badFrom.Content.ReadAsStringAsync());
        Assert.Contains("client secret", await noSecret.Content.ReadAsStringAsync());
        Assert.Equal("ssl", app.Mail.With.Single().Security);
        Assert.Equal(465, app.Mail.With.Single().Port);
        Assert.Equal(2, app.Mail.With.Single().To.Count);
        Assert.Contains("\"hasPassword\":true", listed);
        Assert.Contains("SSL/TLS", listed);
        Assert.DoesNotContain("s3cret-pass", listed);
        Assert.DoesNotContain("s3cret-pass", await File.ReadAllTextAsync(Path.Combine(data, "channels.json")));
    }

    [Fact]
    public async Task The_one_mail_server_from_before_becomes_the_notification_called_email()
    {
        await File.WriteAllTextAsync(Path.Combine(data, "mail.json"),
            """{"provider":"smtp","host":"smtp.gmail.com","port":587,"security":"starttls","username":"lab@gmail.com","from":"lab@gmail.com","to":["me@example.com"]}""");
        var admin = await app.SignedIn();

        var listed = await admin.GetStringAsync("/api/channels");

        Assert.Contains("\"id\":\"email\"", listed);
        Assert.Contains("\"name\":\"Email\"", listed);
        Assert.Contains("\"provider\":\"gmail\"", listed);
    }

    [Fact]
    public async Task A_new_sensor_follows_its_rule_and_alerts_reach_each_notification_on_it()
    {
        var admin = await app.SignedIn();
        var me = await TestApp.Notification(admin);
        var phone = await TestApp.Notification(admin, "Phone", "ntfy", "https://ntfy.sh/lab-alerts");
        await TestApp.Alerting(admin, 0, me, phone);
        (await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "192.168.2.1", name = "Gateway" })).EnsureSuccessStatusCode();

        app.Probe.Finds["192.168.2.1"] = new Probe(false, Note: "No reply");
        await Minute(3);
        var sensor = app.Services.GetRequiredService<Sensors>().All().Single();
        var history = await admin.GetStringAsync("/api/alerts/history");

        Assert.Equal("follow", sensor.Rule);
        Assert.Null(sensor.Channels);
        Assert.Contains(app.Mail.Sent, m => m.Subject == "Gateway is down");
        Assert.Equal("gmail", app.Mail.With[0].Provider);
        Assert.Contains(app.Hooks.Sent, h => h.Kind == "ntfy" && h.Url == "https://ntfy.sh/lab-alerts" && h.Note.Subject == "Gateway is down");
        Assert.Contains("Gateway is down", history);
        Assert.Contains("\"outcome\":\"sent\"", history);
    }

    [Fact]
    public async Task An_alert_is_laid_out_as_a_card_with_its_banner_for_each_service()
    {
        var admin = await app.SignedIn();
        await TestApp.Alerting(admin, 0, await TestApp.Notification(admin, "Lab", "discord", "https://discord.com/api/webhooks/1302/secrettoken"));
        (await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "192.168.2.1", name = "Gate_way" })).EnsureSuccessStatusCode();
        app.Probe.Finds["192.168.2.1"] = new Probe(false, Note: "No reply");
        await Minute(3);
        var note = app.Hooks.Sent.Single(h => h.Kind == "discord").Note;

        var discord = System.Text.Json.JsonSerializer.Serialize(ChannelSender.Discord(note.Card!));
        var embed = System.Text.Json.JsonSerializer.Serialize(ChannelSender.Body("discord", note));
        var slack = System.Text.Json.JsonSerializer.Serialize(ChannelSender.Body("slack", note));
        var teams = System.Text.Json.JsonSerializer.Serialize(ChannelSender.Body("teams", note));
        var hook = System.Text.Json.JsonSerializer.Serialize(ChannelSender.Body("webhook", note));

        Assert.Equal("down", note.Card!.Banner);
        Assert.Contains("\"flags\":32768", discord);
        Assert.Contains("\"type\":17", discord);
        Assert.Contains("Assets/alerts/down.png", discord);
        Assert.Contains("## Gate\\\\_way is down", discord); // a name's _ isn't read as formatting
        Assert.Matches(@"t:[0-9]+:t", discord); // <t:...:t>, which Discord shows in each reader's own zone
        Assert.Contains("\"image\":{\"url\":\"https://raw.githubusercontent.com/", embed);
        Assert.Contains("\"type\":\"image\"", slack);
        Assert.Contains("\"width\":\"Full\"", teams);
        Assert.Contains("\"card\":{\"state\":\"down\"", hook);
    }

    [Fact]
    public async Task A_sensor_that_alerts_when_it_answers_is_down_after_one_miss_when_asked()
    {
        var admin = await app.SignedIn();
        await TestApp.Alerting(admin, 0, await TestApp.Notification(admin));
        (await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "192.168.2.50", name = "Test server", invert = true, downAfter = 1 })).EnsureSuccessStatusCode();

        await Minute(); // the stand-in answers every ping: for this sensor, that's the problem

        Assert.Contains(app.Mail.Sent, m => m.Subject == "Test server is down");
    }

    [Fact]
    public async Task Quiet_hours_hold_a_warning_until_they_are_over_but_a_down_alert_still_goes()
    {
        var admin = await app.SignedIn();
        var me = await TestApp.Notification(admin);
        await TestApp.Alerting(admin, 0, me);
        (await admin.PostAsJsonAsync("/api/alerts/rules/sensor.cert", new { on = true, limit = 21, again = 0, to = new[] { me } })).EnsureSuccessStatusCode();
        // Quiet all day but the last half hour before midnight, server time, so the test doesn't depend on when it runs.
        var local = TimeZoneInfo.ConvertTimeFromUtc(app.Clock.GetUtcNow().UtcDateTime, TimeZoneInfo.Local);
        var from = local.AddMinutes(-local.Minute % 30 - 60).ToString("HH:mm");
        var to = local.AddMinutes(-local.Minute % 30 + 60).ToString("HH:mm");
        (await admin.PostAsJsonAsync("/api/alerts/quiet", new { on = true, from, to, downStillGoes = true })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/sensors", new { kind = "cert", target = "nas.amw.com", name = "NAS certificate" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "192.168.2.9", name = "Switch" })).EnsureSuccessStatusCode();

        app.Probe.Finds["nas.amw.com"] = new Probe(true, 10, "Expires in 5 days", Warn: true);
        app.Probe.Finds["192.168.2.9"] = new Probe(false, Note: "No reply");
        await Minute(3);
        var during = app.Mail.Sent.Select(m => m.Subject).ToList();
        var history = await admin.GetStringAsync("/api/alerts/history");
        await Minute(90);

        Assert.Contains("Switch is down", during);
        Assert.DoesNotContain(during, s => s.StartsWith("NAS certificate", StringComparison.Ordinal));
        Assert.Contains("\"outcome\":\"held\"", history);
        Assert.Contains(app.Mail.Sent, m => m.Subject == "NAS certificate: Expires in 5 days");
    }

    [Fact]
    public async Task Sign_ins_that_keep_failing_and_a_seat_gone_quiet_alert_by_their_rules()
    {
        var admin = await app.SignedIn();
        var me = await TestApp.Notification(admin);
        (await admin.PostAsJsonAsync("/api/alerts/rules/signin.failing", new { on = true, limit = 3, to = new[] { me } })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/alerts/rules/seat.offline", new { on = true, limit = 15, back = true, to = new[] { me } })).EnsureSuccessStatusCode();
        var clients = app.Services.GetRequiredService<Clients>();
        var (client, _, _) = clients.Join("PC1", "Windows 11", "1.1.5", "192.0.2.5");
        clients.Approve(client!.Id, "test");
        var watch = app.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<AlertWatch>().Single();

        for (var i = 0; i < 3; i++)
        {
            await app.Page().PostAsJsonAsync("/api/signin", new { username = "admin", password = "not it" });
        }
        await watch.Round(CancellationToken.None);
        var signIns = app.Mail.Sent.Select(m => m.Subject).ToList();
        app.Clock.Advance(TimeSpan.FromMinutes(16));
        await watch.Round(CancellationToken.None);

        Assert.Equal(["3 failed sign-ins for admin"], signIns);
        Assert.Contains(app.Mail.Sent, m => m.Subject == "PC1 stopped checking in");
    }

    [Fact]
    public async Task Pausing_a_sensor_keeps_everything_it_was_set_up_with()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/sensors", new
        {
            kind = "web", target = "https://192.168.2.1", name = "UniFi", expect = "text", text = "UniFi OS", tags = new[] { "network", " Core " }, certDays = 14, timeout = 30,
        })).EnsureSuccessStatusCode();
        var sensors = app.Services.GetRequiredService<Sensors>();

        (await admin.PostAsJsonAsync($"/api/sensors/{sensors.All().Single().Id}/pause", new { paused = true })).EnsureSuccessStatusCode();
        var paused = sensors.All().Single();

        Assert.True(paused.Paused);
        Assert.Equal("UniFi OS", paused.Text);
        Assert.Equal(["network", "core"], paused.Tags);
        Assert.Equal(14, paused.CertDays);
        Assert.Equal(30, paused.Timeout);
    }
}
