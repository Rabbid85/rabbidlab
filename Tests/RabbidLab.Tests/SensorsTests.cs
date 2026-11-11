using System.Net;
using System.Net.Http.Json;
using System.Text;
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

    [Fact]
    public async Task A_web_page_can_need_a_good_status_or_text_and_a_name_an_address()
    {
        using var site = new LinksTests.TinySite(new() { ["/"] = ("text/html", "<h1>Welcome to the NAS</h1>"u8.ToArray()) });
        var probe = new NetworkProbe(TimeProvider.System);
        Task<Probe> Web(string path, string? expect, string? text = null) =>
            probe.Check(new Sensor { Kind = "web", Target = $"http://127.0.0.1:{site.Port}{path}", Expect = expect, Text = text }, CancellationToken.None);

        Assert.True((await Web("/missing", null)).Ok); // a 404 is the site answering
        Assert.False((await Web("/missing", "ok")).Ok);
        Assert.True((await Web("/", "text", "welcome")).Ok);
        var missing = await Web("/", "text", "Sign in");
        Assert.False(missing.Ok);
        Assert.Equal("The text isn't on the page", missing.Note);

        var named = await probe.Check(new Sensor { Kind = "dns", Target = "localhost", Address = "127.0.0.1" }, CancellationToken.None);
        var wrong = await probe.Check(new Sensor { Kind = "dns", Target = "localhost", Address = "192.0.2.9" }, CancellationToken.None);
        Assert.True(named.Ok);
        Assert.False(wrong.Ok);
        Assert.StartsWith("Resolves to", wrong.Note);
    }

    [Fact]
    public async Task The_new_options_are_checked_and_kept()
    {
        var admin = await app.SignedIn();

        var noText = await admin.PostAsJsonAsync("/api/sensors", new { kind = "web", target = "https://nas.lab", expect = "text" });
        var notIp = await admin.PostAsJsonAsync("/api/sensors", new { kind = "dns", target = "nas.lab", address = "nas" });
        var view = await Json(await admin.PostAsJsonAsync("/api/sensors", new { kind = "web", target = "https://nas.lab", expect = "text", text = "Welcome", name = "NAS page" }));
        view = await Json(await admin.PostAsJsonAsync("/api/sensors", new { kind = "cert", target = "nas.lab", warnDays = 60, name = "NAS certificate" }));

        Assert.Equal(HttpStatusCode.BadRequest, noText.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notIp.StatusCode);
        Assert.Equal("Welcome", Named(view, "NAS page").GetProperty("text").GetString());
        Assert.Equal("text", Named(view, "NAS page").GetProperty("expect").GetString());
        Assert.Equal(60, Named(view, "NAS certificate").GetProperty("warnDays").GetInt32());
    }

    [Fact]
    public async Task Hardware_is_read_over_Redfish_and_only_what_is_watched_counts()
    {
        static (string, byte[]) J(string json) => ("application/json", Encoding.UTF8.GetBytes(json));
        using var bmc = new LinksTests.TinySite(new()
        {
            ["/redfish/v1/Systems"] = J("""{"Members":[{"@odata.id":"/redfish/v1/Systems/S1"}]}"""),
            ["/redfish/v1/Systems/S1"] = J("""{"Manufacturer":"Dell Inc.","Model":"PowerEdge R730","SKU":"4XK2Q12","Status":{"Health":"OK","HealthRollup":"OK"},"Links":{"Chassis":[{"@odata.id":"/redfish/v1/Chassis/C1"}],"ManagedBy":[{"@odata.id":"/redfish/v1/Managers/M1"}]},"Storage":{"@odata.id":"/redfish/v1/Systems/S1/Storage"}}"""),
            ["/redfish/v1/Chassis/C1"] = J("""{"Power":{"@odata.id":"/redfish/v1/Chassis/C1/Power"},"Thermal":{"@odata.id":"/redfish/v1/Chassis/C1/Thermal"}}"""),
            ["/redfish/v1/Chassis/C1/Power"] = J("""{"PowerSupplies":[{"Name":"PS1","Status":{"State":"Enabled","Health":"OK"}},{"Name":"PS2","Status":{"State":"Enabled","Health":"Critical"}}]}"""),
            ["/redfish/v1/Chassis/C1/Thermal"] = J("""{"Fans":[{"Name":"Fan1","Status":{"Health":"OK"}},{"Name":"Fan2","Status":{"State":"Absent"}}],"Temperatures":[{"Name":"System Board Inlet Temp","ReadingCelsius":22,"Status":{"Health":"OK"}},{"Name":"System Board Exhaust Temp","ReadingCelsius":34,"Status":{"Health":"OK"}}]}"""),
            ["/redfish/v1/Managers/M1"] = J("""{"FirmwareVersion":"2.80.80.80","Model":"13G Monolithic"}"""),
            ["/redfish/v1/Systems/S1/Storage"] = J("""{"Members":[{"@odata.id":"/redfish/v1/Systems/S1/Storage/RAID1"}]}"""),
            ["/redfish/v1/Systems/S1/Storage/RAID1"] = J("""{"Drives":[{"@odata.id":"/d1"},{"@odata.id":"/d2"}]}"""),
            ["/d1"] = J("""{"Name":"Disk 0","Status":{"Health":"OK"}}"""),
            ["/d2"] = J("""{"Name":"Disk 1","Status":{"Health":"Warning"}}"""),
        });
        using var nothing = new LinksTests.TinySite(new());
        var login = new Login("monitor", "secret");

        var all = await Redfish.Check(new Sensor { Kind = "hw", Target = $"http://127.0.0.1:{bmc.Port}" }, login, false, CancellationToken.None);
        var some = await Redfish.Check(new Sensor { Kind = "hw", Target = $"http://127.0.0.1:{bmc.Port}", Watch = ["fans", "temps"] }, login, false, CancellationToken.None);
        var none = await Redfish.Check(new Sensor { Kind = "hw", Target = $"http://127.0.0.1:{nothing.Port}" }, login, false, CancellationToken.None);

        Assert.False(all.Ok);
        Assert.Equal("Power supplies: 1 of 2 OK · PS2 Critical", all.Note);
        Assert.Equal("Signed in to the iDRAC on a PowerEdge R730", all.Title);
        Assert.StartsWith("Service tag 4XK2Q12 · firmware 2.80.80.80 · as monitor", all.Detail);
        var parts = all.Parts!.ToDictionary(p => p.Key);
        Assert.Equal("1 of 1 OK", parts["fans"].Now); // the empty bay isn't counted
        Assert.Equal("Inlet 22 °C · exhaust 34 °C", parts["temps"].Now);
        Assert.Equal("warn", parts["disks"].State);
        Assert.True(some.Ok);
        Assert.False(some.Warn);
        Assert.Equal("All OK", some.Note);
        Assert.False(none.Ok);
        Assert.Contains("No Redfish", none.Note);
    }

    [Fact]
    public async Task Credentials_are_saved_once_listed_without_the_password_and_go_with_the_last_sensor()
    {
        var admin = await app.SignedIn();
        var typed = new { name = "iDRAC read-only", username = "monitor", password = "hunter2" };

        var tried = await admin.PostAsJsonAsync("/api/sensors/try", new { kind = "hw", target = "127.0.0.1:1", newLogin = typed });
        var none = await admin.PostAsJsonAsync("/api/sensors", new { kind = "hw", target = "127.0.0.1:1", name = "R730" });
        await admin.PostAsJsonAsync("/api/sensors", new { kind = "hw", target = "127.0.0.1:1", name = "R730", newLogin = typed });
        var saved = (await Json(await admin.GetAsync("/api/sensors/logins"))).EnumerateArray().Single();
        var view = await Json(await admin.PostAsJsonAsync("/api/sensors", new { kind = "hw", target = "127.0.0.1:2", name = "R640", login = saved.GetProperty("id").GetString() }));
        var listed = await (await admin.GetAsync("/api/sensors/logins")).Content.ReadAsStringAsync();
        var onDisk = await File.ReadAllTextAsync(Path.Combine(data, "sensor-logins.json"));
        foreach (var name in new[] { "R730", "R640" })
        {
            await admin.PostAsJsonAsync($"/api/sensors/{Named(view, name).GetProperty("id").GetString()}/delete", new { });
        }
        var after = await Json(await admin.GetAsync("/api/sensors/logins"));

        Assert.Equal(HttpStatusCode.OK, tried.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal("monitor", saved.GetProperty("username").GetString());
        Assert.Equal(saved.GetProperty("id").GetString(), Named(view, "R640").GetProperty("login").GetString());
        Assert.Contains("\"used\": 2", listed.Replace("\"used\":2", "\"used\": 2"));
        Assert.DoesNotContain("hunter2", listed);
        Assert.DoesNotContain("hunter2", onDisk);
        Assert.Equal(0, after.GetArrayLength());
    }

    [Fact]
    public async Task Storage_is_read_from_TrueNAS_and_Synology()
    {
        static (string, byte[]) J(string json) => ("application/json", Encoding.UTF8.GetBytes(json));
        using var truenas = new LinksTests.TinySite(new()
        {
            ["/api/v2.0/system/info"] = J("""{"version":"TrueNAS-SCALE-24.10.1","hostname":"truenas"}"""),
            ["/api/v2.0/pool"] = J("""[{"name":"tank","status":"ONLINE","healthy":true,"size":1000,"free":410,"topology":{"data":[{"type":"MIRROR","status":"ONLINE","children":[{"type":"DISK","status":"ONLINE"},{"type":"DISK","status":"ONLINE"}]}],"cache":[]}},{"name":"backup","status":"DEGRADED","healthy":false,"size":1000,"free":50,"topology":{"data":[{"type":"RAIDZ1","status":"DEGRADED","children":[{"type":"DISK","status":"ONLINE"},{"type":"DISK","status":"FAULTED"}]}]}}]"""),
        });
        using var synology = new LinksTests.TinySite(new()
        {
            ["/webapi/auth.cgi"] = J("""{"success":true,"data":{"sid":"abc"}}"""),
            ["/webapi/entry.cgi?api=SYNO.DSM.Info"] = J("""{"success":true,"data":{"model":"DS920+","version_string":"DSM 7.2.1-69057"}}"""),
            ["/webapi/entry.cgi?api=SYNO.Storage.CGI.Storage"] = J("""{"success":true,"data":{"volumes":[{"id":"volume_1","display_name":"Volume 1","status":"normal","size":{"total":"1000","used":"480"}}],"disks":[{"id":"sata1","status":"normal","smart_status":"normal"},{"id":"sata2","status":"normal","smart_status":"normal"}]}}"""),
        });
        using var refused = new LinksTests.TinySite(new() { ["/webapi/auth.cgi"] = J("""{"success":false,"error":{"code":400}}""") });
        var key = new Login(null, "1-abc");
        var dsm = new Login("monitor", "secret");

        var nas = await TrueNas.Check(new Sensor { Kind = "nas", Target = $"http://127.0.0.1:{truenas.Port}" }, key, false, CancellationToken.None);
        var space = await TrueNas.Check(new Sensor { Kind = "nas", Target = $"http://127.0.0.1:{truenas.Port}", Watch = ["space"] }, key, false, CancellationToken.None);
        var ds = await Synology.Check(new Sensor { Kind = "nas", Product = "synology", Target = $"http://127.0.0.1:{synology.Port}" }, dsm, false, CancellationToken.None);
        var wrong = await Synology.Check(new Sensor { Kind = "nas", Product = "synology", Target = $"http://127.0.0.1:{refused.Port}" }, dsm, false, CancellationToken.None);

        Assert.False(nas.Ok);
        Assert.Equal("Pool health: tank ONLINE · backup DEGRADED", nas.Note);
        Assert.Equal("Signed in to TrueNAS-SCALE-24.10.1", nas.Title);
        Assert.Equal("3 of 4 online", nas.Parts!.Single(p => p.Key == "disks").Now);
        Assert.True(space.Ok);
        Assert.True(space.Warn);
        Assert.Equal("Free space: Warn below 10 % · backup has 5 % free", space.Note);
        Assert.True(ds.Ok);
        Assert.Equal("Signed in to DSM 7.2.1-69057 on a DS920+", ds.Title);
        Assert.Equal("Warn below 10 % · Volume 1 has 52 % free", ds.Parts!.Single(p => p.Key == "space").Now);
        Assert.Equal("2 of 2 normal", ds.Parts!.Single(p => p.Key == "disks").Now);
        Assert.False(wrong.Ok);
        Assert.Equal("Sign-in failed: check the username and password", wrong.Note);
    }

    [Fact]
    public async Task VMware_hosts_datastores_and_alarms_are_read_over_the_vSphere_API()
    {
        using var vcenter = new FakeVSphere();
        using var refusing = new FakeVSphere(refuseLogin: true);
        var login = new Login("monitor", "secret");

        var all = await VMware.Check(new Sensor { Kind = "vmware", Target = $"http://127.0.0.1:{vcenter.Port}" }, login, false, CancellationToken.None);
        var quiet = await VMware.Check(new Sensor { Kind = "vmware", Target = $"http://127.0.0.1:{vcenter.Port}", Watch = ["space", "alarms"] }, login, false, CancellationToken.None);
        var wrong = await VMware.Check(new Sensor { Kind = "vmware", Target = $"http://127.0.0.1:{refusing.Port}" }, login, false, CancellationToken.None);

        Assert.False(all.Ok);
        Assert.Equal("Hosts connected: 1 of 2 · esx02 notResponding", all.Note);
        Assert.Equal("Signed in to VMware vCenter Server 8.0.3 build-24322831", all.Title);
        Assert.Equal("2 hosts · 1 datastore · as monitor", all.Detail);
        Assert.Equal("Warn below 10 % · datastore1 has 18 % free", all.Parts!.Single(p => p.Key == "space").Now);
        Assert.Equal("1 warning", all.Parts!.Single(p => p.Key == "alarms").Now);
        Assert.True(quiet.Ok);
        Assert.True(quiet.Warn);
        Assert.Equal("Triggered alarms: 1 warning", quiet.Note);
        Assert.False(wrong.Ok);
        Assert.Equal("Sign-in failed: check the username and password", wrong.Note);
    }

    [Fact]
    public async Task A_UPS_is_read_over_SNMP_and_a_wrong_community_gets_no_answer()
    {
        using var ups = new FakeSnmpAgent("lab", new()
        {
            ["1.3.6.1.2.1.1.5.0"] = new Lextm.SharpSnmpLib.OctetString("ups01"),
            ["1.3.6.1.2.1.1.1.0"] = new Lextm.SharpSnmpLib.OctetString("APC Web/SNMP Management Card"),
            ["1.3.6.1.2.1.1.3.0"] = new Lextm.SharpSnmpLib.TimeTicks(8_640_000u),
            ["1.3.6.1.2.1.33.1.2.4.0"] = new Lextm.SharpSnmpLib.Integer32(96),
            ["1.3.6.1.2.1.33.1.4.1.0"] = new Lextm.SharpSnmpLib.Integer32(5),
        });
        Sensor At(string value) => new() { Kind = "snmp", Target = "127.0.0.1", Port = ups.Port, Value = value };

        var battery = await Snmp.Check(At("ups"), new Login(null, "lab", "community"), CancellationToken.None);
        var uptime = await Snmp.Check(At("uptime"), new Login(null, "lab", "community"), CancellationToken.None);
        var wrong = await Snmp.Check(At("uptime"), new Login(null, "public", "community"), CancellationToken.None);

        Assert.True(battery.Ok);
        Assert.True(battery.Warn);
        Assert.Equal("Power: On battery", battery.Note);
        Assert.Equal("96 %", battery.Parts!.Single(p => p.Key == "battery").Now);
        Assert.StartsWith("SNMP v2c answered", battery.Title);
        Assert.StartsWith("sysName ups01 · APC", battery.Detail);
        Assert.Equal("Up 1 day", uptime.Parts!.Single().Now);
        Assert.False(wrong.Ok);
        Assert.Equal("No answer: check the address and the community", wrong.Note);
    }

    [Fact]
    public async Task Alert_settings_are_saved_by_name_one_is_the_default_and_a_sensor_can_have_its_own()
    {
        var admin = await app.SignedIn();
        var made = await Json(await admin.PostAsJsonAsync("/api/sensors/rules", new { name = "Lab gear", after = 30, again = 0, back = false }));
        var id = made.GetProperty("id").GetString()!;
        var twice = await admin.PostAsJsonAsync("/api/sensors/rules", new { name = "lab gear", after = 5, again = 0 });
        await admin.PostAsJsonAsync($"/api/sensors/rules/{id}/default", new { });
        var plain = await Json(await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "192.168.2.50", name = "Switch" }));
        var own = await Json(await admin.PostAsJsonAsync("/api/sensors", new { kind = "ping", target = "192.168.2.51", name = "Printer", rule = "custom", custom = new { after = 60, again = 1440, back = true } }));
        var undeletable = await admin.PostAsJsonAsync($"/api/sensors/rules/{id}/delete", new { });
        await admin.PostAsJsonAsync("/api/sensors/rules/normal/default", new { });
        var gone = await Json(await admin.PostAsJsonAsync($"/api/sensors/rules/{id}/delete", new { }));

        Assert.Equal(HttpStatusCode.BadRequest, twice.StatusCode);
        Assert.Equal(id, plain.GetProperty("default").GetString());
        Assert.Equal(id, Named(plain, "Switch").GetProperty("rule").GetString()); // new sensors start with the default
        Assert.Equal("custom", Named(own, "Printer").GetProperty("rule").GetString());
        Assert.Equal(1440, Named(own, "Printer").GetProperty("custom").GetProperty("again").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, undeletable.StatusCode); // the default can't go
        Assert.Equal("normal", Named(gone, "Switch").GetProperty("rule").GetString()); // moved to the default
        Assert.DoesNotContain(gone.GetProperty("rules").EnumerateArray(), r => r.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task A_seat_sensor_asks_the_client_on_that_computer()
    {
        var clients = app.Services.GetRequiredService<Clients>();
        var commands = app.Services.GetRequiredService<ClientCommands>();
        var (client, _, _) = clients.Join("PC1", "Windows 11", "1.1.5", "192.0.2.5");
        clients.Approve(client!.Id, "test");
        var probe = new NetworkProbe(TimeProvider.System, null, commands, clients);
        var offline = await probe.Check(new Sensor { Kind = "svc", Seat = client.Id, Service = "Spooler", Target = "PC1" }, CancellationToken.None);
        // The client: takes two orders and answers them as Windows would.
        var serving = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                var order = await commands.Next(client.Id, CancellationToken.None);
                commands.Done(client.Id, order!.Action == RabbidLab.Shared.AccountOrder.Service
                    ? new RabbidLab.Shared.AccountAnswer { Id = order.Id, Ok = true, State = "StopPending", StartType = "Automatic", Detail = "Print Spooler (Spooler)" }
                    : new RabbidLab.Shared.AccountAnswer { Id = order.Id, Ok = true, Total = 1_000_000_000_000, Free = 50_000_000_000, Detail = "OS" });
            }
        });
        for (var wait = 0; !commands.Listening(client.Id) && wait < 50; wait++)
        {
            await Task.Delay(20);
        }

        var service = await probe.Check(new Sensor { Kind = "svc", Seat = client.Id, Service = "Spooler", Target = "PC1" }, CancellationToken.None);
        var disk = await probe.Check(new Sensor { Kind = "disk", Seat = client.Id, Drive = "C:", Target = "PC1" }, CancellationToken.None);
        await serving;

        Assert.False(offline.Ok);
        Assert.Equal("The client on PC1 isn't connected", offline.Note);
        Assert.False(service.Ok);
        Assert.Equal("Stop pending", service.Note);
        Assert.Equal("Print Spooler (Spooler) is stop pending on PC1", service.Title);
        Assert.True(disk.Ok);
        Assert.True(disk.Warn);
        Assert.Equal("C: has 5 % free", disk.Note);
    }

    [Fact]
    public async Task A_test_sensor_goes_after_30_seconds_with_its_history()
    {
        var sensors = app.Services.GetRequiredService<Sensors>();

        sensors.AddTest(true, "test");
        await sensors.Tick(CancellationToken.None);
        var before = sensors.All().Count;
        app.Clock.Advance(TimeSpan.FromSeconds(31));
        await sensors.Tick(CancellationToken.None);

        Assert.Equal(1, before);
        Assert.Empty(sensors.All());
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
