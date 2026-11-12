using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Where alerts go: channels set up once, picked on each sensor, and what a sensor keeps.</summary>
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
    public async Task A_channel_takes_only_its_kind_of_address_and_never_shows_or_stores_it_whole()
    {
        var admin = await app.SignedIn();

        var wrong = await admin.PostAsJsonAsync("/api/channels", new { kind = "discord", url = "https://example.com/hook" });
        (await admin.PostAsJsonAsync("/api/channels", new { kind = "discord", name = "#homelab", url = "https://discord.com/api/webhooks/1302/secrettoken" })).EnsureSuccessStatusCode();
        var listed = await admin.GetStringAsync("/api/channels");

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("Discord webhook", await wrong.Content.ReadAsStringAsync());
        Assert.Contains("#homelab", listed);
        Assert.Contains("…oken", listed);
        Assert.DoesNotContain("secrettoken", listed);
        Assert.DoesNotContain("secrettoken", await File.ReadAllTextAsync(Path.Combine(data, "channels.json")));
    }

    [Fact]
    public async Task A_new_sensor_starts_with_the_channels_on_for_new_ones_and_alerts_reach_each()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/channels", new { kind = "ntfy", url = "https://ntfy.sh/lab-alerts" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "192.168.2.1", name = "Gateway", rule = "critical" })).EnsureSuccessStatusCode();

        app.Probe.Finds["192.168.2.1"] = new Probe(false, Note: "No reply");
        await Minute(3);
        var sensor = app.Services.GetRequiredService<Sensors>().All().Single();

        Assert.Equal(2, sensor.Channels!.Count);
        Assert.Equal(Channels.Email, sensor.Channels[0]);
        Assert.Contains(app.Mail.Sent, m => m.Subject == "Gateway is down");
        Assert.Contains(app.Hooks.Sent, h => h.Kind == "ntfy" && h.Url == "https://ntfy.sh/lab-alerts" && h.Note.Subject == "Gateway is down");
    }

    [Fact]
    public async Task An_alert_is_laid_out_as_a_card_with_its_banner_for_each_service()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/channels", new { kind = "discord", name = "#homelab", url = "https://discord.com/api/webhooks/1302/secrettoken" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "192.168.2.1", name = "Gate_way", rule = "critical" })).EnsureSuccessStatusCode();
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
        (await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "192.168.2.50", name = "Test server", rule = "critical", invert = true, downAfter = 1 })).EnsureSuccessStatusCode();

        await Minute(); // the stand-in answers every ping: for this sensor, that's the problem

        Assert.Contains(app.Mail.Sent, m => m.Subject == "Test server is down");
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
