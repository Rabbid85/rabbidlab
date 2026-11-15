using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;
using RabbidLab.Shared;

namespace RabbidLab.Tests;

/// <summary>Wake-on-LAN from Devices: the magic packet, the cards clients report, and who sends it for which network.</summary>
public sealed class WakeTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-wake-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public WakeTests()
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

    private async Task<HttpClient> Admin()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/clients/settings", new { approve = false })).EnsureSuccessStatusCode();
        return admin;
    }

    private async Task<(string Id, HttpClient Client)> Computer(string computer, string version = "1.1.195")
    {
        var j = await Json(await app.CreateClient().PostAsJsonAsync("/api/clients/join", new { computer, os = "Windows 11 Pro", version }));
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("RabbidLab-Client", $"{j.GetProperty("id").GetString()}:{j.GetProperty("secret").GetString()}");
        return (j.GetProperty("id").GetString()!, client);
    }

    private static object Card(string mac, string ip) => new { nics = new[] { new { name = "Ethernet", mac, kind = "wired", ip, prefix = 24, up = true, wake = true } } };

    private static async Task<List<JsonElement>> Wake(HttpClient admin, params string[] clients)
    {
        var r = await admin.PostAsJsonAsync("/api/devices/wake", new { clients });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return [.. (await Json(r)).GetProperty("results").EnumerateArray()];
    }

    [Fact]
    public void The_magic_packet_is_six_FFs_then_the_MAC_sixteen_times()
    {
        var mac = WakeOnLan.Mac("60-cf-84-65-16-51")!;
        Assert.Equal("60:CF:84:65:16:51", WakeOnLan.Text(mac));
        Assert.Equal(mac, WakeOnLan.Mac("60CF84651651"));
        Assert.Null(WakeOnLan.Mac("00:00:00:00:00:00"));
        Assert.Null(WakeOnLan.Mac("60:CF:84:65:16"));
        var packet = WakeOnLan.Packet(mac);
        Assert.Equal(102, packet.Length);
        Assert.All(packet.Take(6), b => Assert.Equal(0xFF, b));
        Assert.Equal(mac, packet.Skip(96).ToArray());
        Assert.Equal("192.168.2.255", WakeOnLan.Broadcast(IPAddress.Parse("192.168.2.28"), 24)!.ToString());
        Assert.Equal("10.1.7.255", WakeOnLan.Broadcast(IPAddress.Parse("10.1.5.9"), 22)!.ToString());
        Assert.Equal("10.1.4.0/22", WakeOnLan.Network(IPAddress.Parse("10.1.5.9"), 22));
        Assert.True(WakeOnLan.SameNetwork(IPAddress.Parse("192.168.2.10"), IPAddress.Parse("192.168.2.186"), 24));
        Assert.False(WakeOnLan.SameNetwork(IPAddress.Parse("192.168.2.10"), IPAddress.Parse("192.168.3.20"), 24));
    }

    [Fact]
    public async Task A_computer_on_the_servers_network_is_woken_by_the_server_and_one_without_a_card_says_so()
    {
        var admin = await Admin();
        var (garage, garageClient) = await Computer("GARAGE-PC");
        var (old, _) = await Computer("OLD-PC", "1.1.168");
        (await garageClient.PostAsJsonAsync("/api/clients/inventory", Card("60-cf-84-65-16-51", "192.168.2.37"))).EnsureSuccessStatusCode();

        var results = await Wake(admin, garage, old);
        Assert.True(results[0].GetProperty("sent").GetBoolean());
        Assert.Equal(Waker.Server, results[0].GetProperty("via")[0].GetString());
        Assert.False(results[1].GetProperty("sent").GetBoolean());
        Assert.Contains("network card", results[1].GetProperty("problem").GetString());
        var sent = Assert.Single(app.Wake.Sent);
        Assert.Equal("192.168.2.10", sent.From);
        Assert.Equal(["60:CF:84:65:16:51"], sent.Macs);

        // A client that sends no cards (older, or Windows wouldn't say) leaves the last ones, since Wake needs them while it's off.
        (await garageClient.PostAsJsonAsync("/api/clients/inventory", new { disks = Array.Empty<object>() })).EnsureSuccessStatusCode();
        var inventory = (await Json(await admin.GetAsync($"/api/clients/{garage}/inventory"))).GetProperty("inventory");
        Assert.Equal("60:CF:84:65:16:51", inventory.GetProperty("nics")[0].GetProperty("mac").GetString());
    }

    [Fact]
    public async Task A_computer_on_another_network_is_woken_by_an_online_client_there()
    {
        var admin = await Admin();
        var (lab, labClient) = await Computer("LAB-PC");
        var (relay, relayClient) = await Computer("LAB-HELPER");
        (await labClient.PostAsJsonAsync("/api/clients/inventory", Card("AA:BB:CC:00:11:22", "192.168.5.20"))).EnsureSuccessStatusCode();
        (await relayClient.PostAsJsonAsync("/api/clients/inventory", Card("AA:BB:CC:00:11:33", "192.168.5.30"))).EnsureSuccessStatusCode();

        // Nobody there listening: it can't go anywhere, and says why.
        var nobody = Assert.Single(await Wake(admin, lab));
        Assert.False(nobody.GetProperty("sent").GetBoolean());
        Assert.Contains("192.168.5.0/24", nobody.GetProperty("problem").GetString());
        Assert.Empty(app.Wake.Sent);

        var next = relayClient.PostAsync("/api/clients/next", null);
        var commands = app.Services.GetRequiredService<ClientCommands>();
        while (!commands.Listening(relay) && !next.IsCompleted)
        {
            await Task.Delay(10);
        }
        var waking = Wake(admin, lab);
        var r = await next;
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var order = await Json(r);
        Assert.Equal("wake", order.GetProperty("action").GetString());
        Assert.Equal("AA:BB:CC:00:11:22", order.GetProperty("macs")[0].GetString());
        (await relayClient.PostAsJsonAsync("/api/clients/done", new { id = order.GetProperty("id").GetString(), ok = true })).EnsureSuccessStatusCode();

        var woke = Assert.Single(await waking);
        Assert.True(woke.GetProperty("sent").GetBoolean());
        Assert.Equal("LAB-HELPER", woke.GetProperty("via")[0].GetString());
    }
}
