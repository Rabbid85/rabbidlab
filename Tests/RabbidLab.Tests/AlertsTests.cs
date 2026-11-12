using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Depends on, timed pauses, test alerts and gathered alerts.</summary>
public sealed class AlertsTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-alerts-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public AlertsTests()
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

    private Sensors Sensors => app.Services.GetRequiredService<Sensors>();

    private Sensor Named(string name) => Sensors.All().Single(s => s.Name == name);

    private async Task Minute(int minutes = 1)
    {
        for (var i = 0; i < minutes; i++)
        {
            app.Clock.Advance(TimeSpan.FromMinutes(1));
            await Sensors.Tick(CancellationToken.None);
        }
    }

    private static async Task Add(HttpClient admin, object sensor) => (await admin.PostAsJsonAsync("/api/sensors", sensor)).EnsureSuccessStatusCode();

    [Fact]
    public async Task A_sensor_behind_one_thats_down_waits_and_alerts_only_if_its_still_down_once_that_one_is_back()
    {
        var admin = await app.SignedIn();
        await Add(admin, new { kind = "ping", target = "192.168.2.1", name = "Gateway", rule = "critical" });
        var gateway = Named("Gateway").Id;
        await Add(admin, new { kind = "ping", target = "192.168.2.21", name = "esxi01", rule = "critical", dependsOn = gateway });
        await Add(admin, new { kind = "ping", target = "192.168.2.18", name = "esxi02", rule = "critical", dependsOn = gateway });
        foreach (var down in new[] { "192.168.2.1", "192.168.2.21", "192.168.2.18" })
        {
            app.Probe.Finds[down] = new Probe(false, Note: "No reply");
        }

        await Minute(3);
        var view = await admin.GetFromJsonAsync<JsonElement>("/api/sensors");
        var esxi = view.GetProperty("sensors").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "esxi01");
        var gatewayAlert = Assert.Single(app.Mail.Sent);

        Assert.Equal("Gateway is down", gatewayAlert.Subject);
        Assert.Contains(gatewayAlert.Facts, f => f.Label == "Waiting on it" && f.Value == "esxi01 and esxi02");
        Assert.Equal("wait", esxi.GetProperty("status").GetString());
        Assert.Equal("Gateway", esxi.GetProperty("waits").GetString());

        // The gateway comes back with esxi02 behind it; esxi01 stays down, and only then alerts.
        app.Probe.Finds.Remove("192.168.2.1");
        app.Probe.Finds.Remove("192.168.2.18");
        await Minute(2);

        Assert.Contains(app.Mail.Sent, m => m.Subject == "esxi01 is down");
        Assert.DoesNotContain(app.Mail.Sent, m => m.Subject == "esxi02 is down");
    }

    [Fact]
    public async Task A_sensor_cant_depend_on_one_that_depends_on_it()
    {
        var admin = await app.SignedIn();
        await Add(admin, new { kind = "ping", target = "192.168.2.1", name = "Gateway" });
        await Add(admin, new { kind = "ping", target = "192.168.2.2", name = "Switch", dependsOn = Named("Gateway").Id });

        var loop = await admin.PostAsJsonAsync($"/api/sensors/{Named("Gateway").Id}", new { kind = "ping", target = "192.168.2.1", name = "Gateway", dependsOn = Named("Switch").Id });

        Assert.Equal(HttpStatusCode.BadRequest, loop.StatusCode);
        Assert.Contains("depends on it", await loop.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_timed_pause_ends_by_itself_and_takes_what_depends_on_it_along()
    {
        var admin = await app.SignedIn();
        await Add(admin, new { kind = "ping", target = "192.168.2.1", name = "Gateway" });
        var gateway = Named("Gateway").Id;
        await Add(admin, new { kind = "ping", target = "192.168.2.21", name = "esxi01", dependsOn = gateway });

        (await admin.PostAsJsonAsync($"/api/sensors/{gateway}/pause", new { paused = true, minutes = 15, dependents = true })).EnsureSuccessStatusCode();
        Assert.True(Named("esxi01").Paused);
        Assert.Equal(gateway, Named("esxi01").PausedWith);
        await Minute(16);

        Assert.False(Named("Gateway").Paused);
        Assert.False(Named("esxi01").Paused);

        admin = await app.SignedIn(); // the session ran out while the clock moved on
        // Paused until started again: starting the gateway starts what went with it.
        (await admin.PostAsJsonAsync($"/api/sensors/{gateway}/pause", new { paused = true, dependents = true })).EnsureSuccessStatusCode();
        await Minute(60);
        Assert.True(Named("esxi01").Paused);
        admin = await app.SignedIn();
        (await admin.PostAsJsonAsync($"/api/sensors/{gateway}/pause", new { paused = false })).EnsureSuccessStatusCode();
        Assert.False(Named("esxi01").Paused);
    }

    [Fact]
    public async Task A_test_alert_goes_to_the_sensors_channels_marked_as_a_test()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/channels", new { kind = "ntfy", url = "https://ntfy.sh/lab-alerts" })).EnsureSuccessStatusCode();
        await Add(admin, new { kind = "ping", target = "192.168.2.1", name = "Gateway" });
        var id = Named("Gateway").Id;

        var sent = await admin.PostAsJsonAsync($"/api/sensors/{id}/test", new { });
        var again = await admin.PostAsJsonAsync($"/api/sensors/{id}/test", new { });

        sent.EnsureSuccessStatusCode();
        Assert.Contains(app.Mail.Sent, m => m.Subject == "Test: Gateway is down");
        Assert.Contains(app.Hooks.Sent, h => h.Kind == "ntfy" && h.Note.Subject == "Test: Gateway is down");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Single(app.Mail.Sent);
    }

    [Fact]
    public async Task Down_alerts_close_together_go_out_as_one_when_gathered()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/sensors/gather", new { on = true, secs = 60 })).EnsureSuccessStatusCode();
        foreach (var (name, target) in new[] { ("TrueNAS", "192.168.2.23"), ("Plex", "192.168.2.24"), ("pihole", "192.168.2.25"), ("Switch", "192.168.2.2") })
        {
            await Add(admin, new { kind = "ping", target, name, rule = "critical" });
            if (name != "Switch")
            {
                app.Probe.Finds[target] = new Probe(false, Note: "No reply");
            }
        }

        await Minute(2); // down, and the first alert waits for others
        Assert.Empty(app.Mail.Sent);
        await Minute();

        var one = Assert.Single(app.Mail.Sent);
        Assert.Equal("3 sensors are down", one.Subject);
        Assert.Contains(one.Facts, f => f.Label == "Still up" && f.Value == "Switch");
        Assert.Contains("in one alert with 2 others", await admin.GetStringAsync($"/api/sensors/{Named("Plex").Id}"));
    }

    [Fact]
    public async Task A_watched_link_gets_a_sensor_that_gives_it_its_dot_and_goes_with_it()
    {
        var admin = await app.SignedIn();
        var watch = new { on = true, after = 0, again = 0, back = true };
        var made = await (await admin.PostAsJsonAsync("/api/links", new { name = "Grafana", url = "https://grafana.amw.com", group = "Monitoring", watch })).Content.ReadFromJsonAsync<JsonElement>();
        var id = made.GetProperty("id").GetString();
        var sensor = Named("Grafana");
        app.Probe.Finds[sensor.Target] = new Probe(false, Note: "No reply");
        await Minute(3);
        var states = await admin.GetFromJsonAsync<JsonElement>("/api/links/states");
        var refused = await admin.PostAsJsonAsync("/api/links", new { name = "NAS shell", url = "ssh://192.168.2.23", watch });

        Assert.Equal(sensor.Id, made.GetProperty("sensor").GetString());
        Assert.Equal(("web", "Monitoring", 0), (sensor.Kind, sensor.Group, sensor.Custom!.After));
        Assert.True(states.GetProperty("states").GetProperty(id!).GetProperty("down").GetBoolean());
        Assert.Contains(app.Mail.Sent, m => m.Subject == "Grafana is down");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        admin = await app.SignedIn();
        var off = await (await admin.PostAsJsonAsync($"/api/links/{id}", new { name = "Grafana", url = "https://grafana.amw.com", watch = new { on = false } })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, off.GetProperty("sensor").ValueKind);
        Assert.Empty(Sensors.All());

        (await admin.PostAsJsonAsync($"/api/links/{id}", new { name = "Grafana", url = "https://grafana.amw.com", watch })).EnsureSuccessStatusCode();
        Assert.Single(Sensors.All());
        (await admin.PostAsJsonAsync($"/api/links/{id}/delete", new { })).EnsureSuccessStatusCode();
        Assert.Empty(Sensors.All());
    }

    [Fact]
    public async Task Links_ticked_together_are_watched_together_and_unticked_ones_stop()
    {
        var admin = await app.SignedIn();
        async Task<string> Link(string name, string url) => (await (await admin.PostAsJsonAsync("/api/links", new { name, url, group = "Network" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var gateway = await Link("Gateway", "https://192.168.2.1");
        var nas = await Link("TrueNAS", "https://192.168.2.23");
        var shell = await Link("NAS shell", "ssh://192.168.2.23");

        var first = await (await admin.PostAsJsonAsync("/api/links/watch", new { watch = new[] { gateway, nas, shell }, after = 10 })).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await admin.PostAsJsonAsync("/api/links/watch", new { unwatch = new[] { nas } })).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, first.GetProperty("watched").GetInt32());
        Assert.Contains("NAS shell", first.GetProperty("skipped")[0].GetString());
        Assert.Equal(1, second.GetProperty("stopped").GetInt32());
        Assert.Equal("Gateway", Assert.Single(Sensors.All()).Name);
        Assert.Equal(10, Sensors.All()[0].Custom!.After);
    }

    [Fact]
    public async Task Dragging_a_sensor_to_another_group_changes_only_its_group()
    {
        var admin = await app.SignedIn();
        await Add(admin, new
        {
            kind = "web", target = "https://192.168.2.1", name = "UniFi", expect = "text", text = "UniFi OS", tags = new[] { "network" },
            rule = "custom", custom = new { after = 10, again = 0, back = true },
        });

        (await admin.PostAsJsonAsync($"/api/sensors/{Named("UniFi").Id}/move", new { group = "Network" })).EnsureSuccessStatusCode();
        var moved = Named("UniFi");

        Assert.Equal("Network", moved.Group);
        Assert.Equal("UniFi OS", moved.Text);
        Assert.Equal(["network"], moved.Tags);
        Assert.Equal(10, moved.Custom!.After);
    }
}
