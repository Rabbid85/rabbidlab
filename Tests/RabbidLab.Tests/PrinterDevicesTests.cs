using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>What the printers say about themselves: which addresses are asked, what's kept, and how supply levels read.</summary>
public sealed class PrinterDevicesTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-prdev-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public PrinterDevicesTests()
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

    // A computer that joined, was approved and sent the printers it has.
    private async Task Computer(HttpClient admin, params object[] printers)
    {
        var joined = await (await app.CreateClient().PostAsJsonAsync("/api/clients/join", new { computer = "AMW-LT-022", os = "Windows 11 Pro", version = "1.1.190" })).Content.ReadFromJsonAsync<JsonElement>();
        var (id, secret) = (joined.GetProperty("id").GetString()!, joined.GetProperty("secret").GetString()!);
        (await admin.PostAsync($"/api/clients/{id}/approve", null)).EnsureSuccessStatusCode();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("RabbidLab-Client", $"{id}:{secret}");
        (await client.PostAsJsonAsync("/api/clients/inventory", new { model = "OptiPlex 7090", printers })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_printers_computers_have_are_asked_once_at_their_own_private_addresses_and_say_their_model_and_supplies()
    {
        var admin = await app.SignedIn();
        await Computer(admin,
            new { name = "Front office", driver = "HP Universal Printing PCL 6", port = "IP_10.1.2.30", kind = "tcpip", address = "10.1.2.30" },
            new { name = "NPIA68F28 (HP Color LaserJet MFP M281fdw)", driver = "Microsoft IPP Class Driver", port = "WSD-6d91f834", kind = "wsd", location = "http://192.168.3.20:3911/" },
            new { name = "Somewhere else", driver = "Generic", port = "IP_8.8.8.8", kind = "tcpip", address = "8.8.8.8" },
            new { name = "Microsoft Print to PDF", driver = "Microsoft Print To PDF", port = "PORTPROMPT:", kind = "virtual" });
        app.PrinterSnmp.Says["192.168.3.20"] = new PrinterDevice("192.168.3.20", true, "HP Color LaserJet MFP M281fdw", "VNB123", 1955,
            [new("Black Cartridge HP CF500A", 68, false), new("Cyan Cartridge HP CF501A", 4, false)], DateTime.UtcNow);

        var list = (await admin.GetFromJsonAsync<JsonElement>("/api/devices/printers")).GetProperty("printers").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);
        var first = await admin.GetFromJsonAsync<JsonElement>("/api/devices/printers/supplies");
        var second = await admin.GetFromJsonAsync<JsonElement>("/api/devices/printers/supplies?again=true"); // the first Refresh does ask again
        var third = await admin.GetFromJsonAsync<JsonElement>("/api/devices/printers/supplies?again=true"); // and not again within the minute

        Assert.Equal("10.1.2.30", list["Front office"].GetProperty("ip").GetString());
        Assert.Equal("192.168.3.20", list["NPIA68F28 (HP Color LaserJet MFP M281fdw)"].GetProperty("ip").GetString());
        Assert.Equal(JsonValueKind.Null, list["Somewhere else"].GetProperty("ip").ValueKind); // not a private address: never asked
        Assert.False(first.GetProperty("asking").GetBoolean());
        var hp = first.GetProperty("devices").EnumerateArray().Single(d => d.GetProperty("ip").GetString() == "192.168.3.20");
        Assert.Equal("HP Color LaserJet MFP M281fdw", hp.GetProperty("model").GetString());
        Assert.Equal(1955, hp.GetProperty("pages").GetInt64());
        Assert.Equal(4, hp.GetProperty("supplies")[1].GetProperty("percent").GetInt32());
        Assert.False(first.GetProperty("devices").EnumerateArray().Single(d => d.GetProperty("ip").GetString() == "10.1.2.30").GetProperty("answered").GetBoolean());
        Assert.Equal(2, second.GetProperty("devices").GetArrayLength());
        Assert.Equal(2, third.GetProperty("devices").GetArrayLength());
        Assert.Equal(["10.1.2.30", "10.1.2.30", "192.168.3.20", "192.168.3.20"], app.PrinterSnmp.Asked.Order());
    }

    [Fact]
    public async Task Supply_levels_take_the_permission_to_see_devices()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "nobody1", password = "user password" })).EnsureSuccessStatusCode();
        var user = app.Page();
        (await user.PostAsJsonAsync("/api/signin", new { username = "nobody1", password = "user password" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/devices/printers/supplies")).StatusCode);
        Assert.Empty(app.PrinterSnmp.Asked);
    }

    [Theory]
    [InlineData("Black Cartridge", 68L, 100L, 68, false)]
    [InlineData("Black Toner", 1200L, 6000L, 20, false)]
    [InlineData("Black Toner", 7000L, 6000L, 100, false)] // a printer that counts past full
    [InlineData("Toner", -3L, -2L, null, true)] // only says there's some left
    [InlineData("Waste box", -2L, -2L, null, false)] // doesn't know
    [InlineData("Drum", 50L, 0L, null, false)] // no size to measure against
    public void A_supply_is_a_percentage_when_the_printer_gives_a_level_and_a_size(string name, long level, long max, int? percent, bool some)
    {
        var s = PrinterSnmp.Supply(name, level, max);

        Assert.Equal(new PrinterSupply(name, percent, some), s);
    }

    [Fact]
    public void An_empty_supply_slot_is_no_supply() => Assert.Null(PrinterSnmp.Supply("  ", 50, 100));

    [Theory]
    [InlineData("HP Color LaserJet MFP M281fdw", "HP ETHERNET MULTI-ENVIRONMENT,SN:VNB1,FN:1,PID:HP Color LaserJet MFP M281fdw", "HP Color LaserJet MFP M281fdw")]
    [InlineData(null, "HP ETHERNET MULTI-ENVIRONMENT,SN:VNB1,FN:1,SVCID:2,PID:HP LaserJet 400 M401dn", "HP LaserJet 400 M401dn")]
    [InlineData(null, "KONICA MINOLTA bizhub C308", "KONICA MINOLTA bizhub C308")]
    [InlineData(" ", null, null)]
    public void The_model_is_the_printers_own_name_for_itself(string? device, string? description, string? model) =>
        Assert.Equal(model, PrinterSnmp.Model(device, description));

    [Theory]
    [InlineData("tcpip", "10.1.2.30", null, null, "10.1.2.30")]
    [InlineData("lpr", "172.20.1.9", null, null, "172.20.1.9")]
    [InlineData("tcpip", "printer7.amw.com", "IP_192.168.7.7", null, "192.168.7.7")] // a name for an address: the port's name still says
    [InlineData("wsd", null, "WSD-6d91", "http://192.168.3.20:3911/", "192.168.3.20")]
    [InlineData("wsd", null, "WSD-6d91", "Second floor", null)]
    [InlineData("tcpip", "8.8.8.8", "IP_8.8.8.8", null, null)] // out on the internet: not asked
    [InlineData("virtual", null, "PORTPROMPT:", null, null)]
    public void A_printers_own_address_comes_from_its_port_or_its_location(string kind, string? address, string? port, string? location, string? ip) =>
        Assert.Equal(ip, PrinterDevices.IpOf(new DevicePrinter { Name = "P", Kind = kind, Address = address, Port = port, Location = location }));
}
