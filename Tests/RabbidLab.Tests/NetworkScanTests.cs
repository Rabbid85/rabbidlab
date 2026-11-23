using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Network: ranges, what a device is from what it answered, the vendor from its MAC, and scans that keep what they find.</summary>
public sealed class NetworkScanTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-net-" + Guid.NewGuid().ToString("N"));

    public NetworkScanTests() => Directory.CreateDirectory(data);

    public void Dispose()
    {
        try
        {
            Directory.Delete(data, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temp folder's own cleanup.
        }
    }

    [Fact]
    public void Ranges_are_ipv4_cidr_of_a_sensible_size()
    {
        Assert.Equal(254, NetworkScan.Addresses("10.10.1.0/24")!.Count);
        Assert.Equal("10.10.1.1", NetworkScan.Addresses("10.10.1.0/24")![0].ToString());
        Assert.Equal("10.10.1.254", NetworkScan.Addresses("10.10.1.0/24")![^1].ToString());
        Assert.Equal("10.10.1.0/24", NetworkScan.Normal("10.10.1.77/24"));
        Assert.Null(NetworkScan.Normal("10.0.0.0/8")); // too big
        Assert.Null(NetworkScan.Normal("fe80::/64"));
        Assert.Null(NetworkScan.Normal("printer"));
    }

    [Fact]
    public void A_device_is_identified_by_what_it_answered()
    {
        NetFacts F(int? ttl = null, int[]? ports = null, string? snmp = null, bool gateway = false, string? os = null) => new("10.0.0.5", true, 2, ttl, null, null, null, [.. ports ?? []], snmp, null, gateway, os);
        Assert.Equal("router", NetworkScan.Classify(F(64, [22, 443], gateway: true), "Ubiquiti").Kind);
        Assert.Equal("printer", NetworkScan.Classify(F(255, [80, 9100]), "HP").Kind);
        Assert.Equal("camera", NetworkScan.Classify(F(64, [80, 554]), "Reolink").Kind);
        Assert.Equal("server", NetworkScan.Classify(F(128, [53, 88, 389, 445]), "VMware").Kind); // a domain controller
        Assert.Equal("server", NetworkScan.Classify(F(128, [445], os: "Windows Server 2022 Standard"), "Dell").Kind);
        Assert.Equal("pc", NetworkScan.Classify(F(128, [135, 445]), "Dell").Kind);
        Assert.Equal("pc", NetworkScan.Classify(F(128), null).Kind); // TTL 128 is Windows, even with every port shut
        Assert.Equal("nas", NetworkScan.Classify(F(64, [80, 445, 5000]), "Synology").Kind);
        Assert.Equal("switch", NetworkScan.Classify(F(64, [22], snmp: "Cisco IOS Software, C2960"), "Cisco").Kind);
        Assert.Equal("ap", NetworkScan.Classify(F(64, [22], snmp: "UniFi AP-AC-Pro"), "Ubiquiti").Kind);
        Assert.Equal("phone", NetworkScan.Classify(F(64), "Apple").Kind);
        Assert.Equal("phone", NetworkScan.Classify(F(64), "Private address").Kind); // a randomised Wi-Fi address
        Assert.Equal("iot", NetworkScan.Classify(F(64, [80]), "Espressif").Kind);
        Assert.Equal("unknown", NetworkScan.Classify(F(64, [80]), null).Kind);
    }

    [Fact]
    public void Vendors_come_from_the_mac_address()
    {
        Assert.Equal("Synology", NetworkScan.Vendor("00:11:32:AB:CD:01"));
        Assert.Equal("Ubiquiti", NetworkScan.Vendor("74-AC-B9-1E-22-01"));
        Assert.Equal("VMware", NetworkScan.Vendor("005056b06414"));
        Assert.Equal("Private address", NetworkScan.Vendor("DA:A1:19:00:00:01")); // locally administered
        Assert.Null(NetworkScan.Vendor(null));
    }

    [Fact]
    public void Netbios_and_arp_answers_are_read()
    {
        // A node status answer: the header and question echoed (56 bytes), two names, then the MAC.
        var answer = new byte[57 + 2 * 18 + 6];
        answer[56] = 2;
        System.Text.Encoding.ASCII.GetBytes("AMW            ").CopyTo(answer, 57);
        answer[57 + 15] = 0x00;
        answer[57 + 16] = 0x80; // a group name
        System.Text.Encoding.ASCII.GetBytes("DC01           ").CopyTo(answer, 75);
        answer[75 + 15] = 0x00;
        new byte[] { 0x00, 0x50, 0x56, 0xB0, 0x64, 0x14 }.CopyTo(answer, 93);
        Assert.Equal(("DC01", "00:50:56:B0:64:14"), WindowsNetProbe.NbStatAnswer(answer));

        var table = new byte[4 + 2 * 24];
        table[0] = 2;
        void Row(int i, byte[] mac, byte[] ip, int type)
        {
            var at = 4 + i * 24;
            table[at + 4] = 6;
            mac.CopyTo(table, at + 8);
            ip.CopyTo(table, at + 16);
            table[at + 20] = (byte) type;
        }
        Row(0, [0x74, 0xAC, 0xB9, 0x1E, 0x22, 0x01], [192, 168, 2, 1], 3);
        Row(1, [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], [192, 168, 2, 255], 4);
        Assert.Equal(new Dictionary<string, string> { ["192.168.2.1"] = "74:AC:B9:1E:22:01" }, WindowsNetProbe.ArpRows(table));
    }

    [Fact]
    public async Task A_scan_keeps_what_it_finds_marks_new_devices_and_what_went_quiet()
    {
        using var app = new TestApp(data);
        var net = app.Net;
        net.Local = [("10.10.1.0/24", "This network")];
        net.Gateway = "10.10.1.1";
        net.Hosts["10.10.1.1"] = new(true, 1, 64, [22, 53, 443], "74:AC:B9:1E:22:01", null, null, null);
        net.Hosts["10.10.1.10"] = new(true, 1, 128, [53, 88, 135, 389, 445], "00:50:56:B0:64:14", "dc01.amw.com", "DC01", null);
        net.Hosts["10.10.1.50"] = new(false, null, null, [445, 135], "A4:BB:6D:21:9C:40", null, "GREG-LT", null); // blocks ping
        var scan = app.Services.GetRequiredService<NetworkScan>();

        await scan.Run("admin");
        net.Hosts["10.10.1.60"] = new(true, 3, 64, [9100, 631], "3C:52:82:6A:10:F4", null, null, "HP LaserJet Pro M404");
        net.Hosts.Remove("10.10.1.50");
        await scan.Run("admin");
        var view = JsonSerializer.SerializeToElement(scan.View(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var devices = view.GetProperty("devices").EnumerateArray().ToDictionary(d => d.GetProperty("ip").GetString()!);
        Assert.Equal(["10.10.1.1", "10.10.1.10", "10.10.1.50", "10.10.1.60"], devices.Keys);
        Assert.Equal("router", devices["10.10.1.1"].GetProperty("kind").GetString());
        Assert.Equal("Ubiquiti", devices["10.10.1.1"].GetProperty("vendor").GetString());
        Assert.Equal("server", devices["10.10.1.10"].GetProperty("kind").GetString());
        Assert.Equal("dc01", devices["10.10.1.10"].GetProperty("name").GetString());
        Assert.Equal("pc", devices["10.10.1.50"].GetProperty("kind").GetString()); // found though it blocks ping
        Assert.False(devices["10.10.1.50"].GetProperty("online").GetBoolean()); // gone quiet in the second scan
        Assert.False(devices["10.10.1.10"].GetProperty("isNew").GetBoolean()); // the first scan learns, it doesn't flag
        Assert.True(devices["10.10.1.60"].GetProperty("isNew").GetBoolean());
        Assert.Equal("printer", devices["10.10.1.60"].GetProperty("kind").GetString());
        Assert.Contains(app.Services.GetRequiredService<ActivityLog>().Recent(), e => e.Action == "network.new" && e.Title.Contains("10.10.1.60"));
        Assert.Equal("net.new", EventAlerts.FromActivity(app.Services.GetRequiredService<ActivityLog>().Recent().First(e => e.Action == "network.new"))?.Event);
    }

    [Fact]
    public async Task A_device_that_never_answers_its_questions_cannot_hold_the_scan_up()
    {
        using var app = new TestApp(data);
        var net = app.Net;
        net.Local = [("10.10.1.0/24", "This network")];
        net.Hosts["10.10.1.20"] = new(true, 2, 128, [135, 445], "A4:BB:6D:21:9C:41", "pc20.amw.com", null, null);
        var scan = app.Services.GetRequiredService<NetworkScan>();
        await scan.Run("admin");

        net.Stuck.Add("10.10.1.20");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await scan.Run("admin");
        timer.Stop();
        var view = JsonSerializer.SerializeToElement(scan.View(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var d = view.GetProperty("devices").EnumerateArray().Single();

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), $"the scan took {timer.Elapsed}");
        Assert.True(d.GetProperty("online").GetBoolean()); // it answered, so it's online
        Assert.Equal("pc", d.GetProperty("kind").GetString()); // and keeps what the last scan learned
        Assert.Equal("pc20", d.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Scanning_and_settings_take_their_own_permissions()
    {
        using var app = new TestApp(data);
        app.Net.Local = [("10.10.1.0/24", "This network")];
        var admin = await app.SignedIn();
        var role = (await (await admin.PostAsJsonAsync("/api/roles", new { name = "Viewer", color = "#5FD4A0" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        (await admin.PostAsJsonAsync($"/api/roles/{role}", new { name = "Viewer", color = "#5FD4A0", permissions = new[] { "network.view" } })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "viewer1", password = "user password", roles = new[] { role } })).EnsureSuccessStatusCode();
        var viewer = await app.SignedIn("viewer1", "user password");

        var seen = await viewer.GetAsync("/api/network");
        var refused = await viewer.PostAsJsonAsync("/api/network/scan", new { });
        var bad = await admin.PostAsJsonAsync("/api/network/settings", new { ranges = new[] { new { name = "Everything", cidr = "10.0.0.0/8", on = true } }, schedule = "1h" });
        var good = await admin.PostAsJsonAsync("/api/network/settings", new { ranges = new[] { new { name = "Office", cidr = "10.10.1.9/24", on = true } }, schedule = "1h" });

        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var view = await good.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("10.10.1.0/24", view.GetProperty("ranges")[0].GetProperty("cidr").GetString());
        Assert.Equal("Every hour", view.GetProperty("scheduleText").GetString());
    }
}

/// <summary>A pretend network: what each address answers, this server's networks and its gateway.</summary>
public sealed class FakeNetProbe : INetProbe
{
    public sealed record Host(bool Pings, int? Ms, int? Ttl, int[] Ports, string? Mac, string? Dns, string? NetBios, string? Snmp);

    public Dictionary<string, Host> Hosts { get; } = [];

    public IReadOnlyList<(string Cidr, string Name)> Local { get; set; } = [];

    public string? Gateway { get; set; }

    private Host? Of(IPAddress ip) => Hosts.GetValueOrDefault(ip.ToString());

    public Task<(bool Alive, int? Ms, int? Ttl)> Ping(IPAddress ip, CancellationToken stop) =>
        Task.FromResult(Of(ip) is { Pings: true } h ? (true, h.Ms, h.Ttl) : (false, (int?) null, (int?) null));

    public Task<bool?> Tcp(IPAddress ip, int port, CancellationToken stop) => Task.FromResult(Of(ip) is { } h ? h.Ports.Contains(port) ? true : (bool?) false : null);

    public Dictionary<string, string> Arp() => Hosts.Where(h => h.Value.Mac is not null).ToDictionary(h => h.Key, h => h.Value.Mac!);

    // Addresses whose DNS question never comes back, and doesn't stop when asked to: a device that's slow at everything.
    public HashSet<string> Stuck { get; } = [];

    public Task<string?> Dns(IPAddress ip, CancellationToken stop) => Stuck.Contains(ip.ToString()) ? new TaskCompletionSource<string?>().Task : Task.FromResult(Of(ip)?.Dns);

    public Task<(string? Name, string? Mac)> NetBios(IPAddress ip, CancellationToken stop) => Task.FromResult((Of(ip)?.NetBios, Of(ip)?.NetBios is null ? null : Of(ip)?.Mac));

    public Task<(string? Descr, string? Name)> Snmp(IPAddress ip, string community, CancellationToken stop) => Task.FromResult((Of(ip)?.Snmp, (string?) null));

    public IReadOnlyList<string> Gateways() => Gateway is null ? [] : [Gateway];

    public IReadOnlyList<(string Cidr, string Name)> LocalNetworks() => Local;
}
