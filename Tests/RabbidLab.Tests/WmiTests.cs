using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;
using RabbidLab.Shared;

namespace RabbidLab.Tests;

/// <summary>WMI queries from Devices: only reads, only in a computer's own namespaces, only with the right, and each computer's rows come back.</summary>
public sealed class WmiTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-wmi-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public WmiTests()
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

    /// <summary>An administrator, with computers joining without approval. No confirming who they are: a query is a read.</summary>
    private async Task<HttpClient> Admin()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/clients/settings", new { approve = false })).EnsureSuccessStatusCode();
        return admin;
    }

    private async Task<(string Id, HttpClient Client)> Computer(string computer, string version = "1.1.173")
    {
        var j = await Json(await app.CreateClient().PostAsJsonAsync("/api/clients/join", new { computer, os = "Windows 11 Pro", version }));
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("RabbidLab-Client", $"{j.GetProperty("id").GetString()}:{j.GetProperty("secret").GetString()}");
        return (j.GetProperty("id").GetString()!, client);
    }

    /// <summary>The computer starts listening and answers its next order as the client would; the order it was given.</summary>
    private async Task<Task<JsonElement>> Listening(string id, HttpClient client, Func<JsonElement, object> answer)
    {
        var next = client.PostAsync("/api/clients/next", null);
        var commands = app.Services.GetRequiredService<ClientCommands>();
        while (!commands.Listening(id) && !next.IsCompleted)
        {
            await Task.Delay(10);
        }
        return Serve(next, client, answer);
    }

    private static async Task<JsonElement> Serve(Task<HttpResponseMessage> next, HttpClient client, Func<JsonElement, object> answer)
    {
        var r = await next;
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var order = await Json(r);
        (await client.PostAsJsonAsync("/api/clients/done", answer(order))).EnsureSuccessStatusCode();
        return order;
    }

    [Fact]
    public async Task A_query_goes_to_each_computer_and_its_rows_come_back()
    {
        var admin = await Admin();
        var (shop, shopClient) = await Computer("SHOP-PC");
        var (old, _) = await Computer("OLD-PC", "1.1.168");
        var (away, _) = await Computer("AWAY-PC");

        var served = await Listening(shop, shopClient, o => new
        {
            id = o.GetProperty("id").GetString(), ok = true,
            columns = new[] { "Manufacturer", "ReleaseDate" }, kinds = new[] { "text", "date" },
            rows = new[] { new[] { "Dell Inc.", "2026-07-12 20:00:00" } }, more = false,
        });
        var r = await admin.PostAsJsonAsync("/api/devices/wmi", new { clients = new[] { shop, old, away }, space = "", query = "SELECT Manufacturer, ReleaseDate FROM Win32_BIOS" });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());

        // The order carried the query and the namespace it runs in.
        var order = await served;
        Assert.Equal(AccountOrder.Wmi, order.GetProperty("action").GetString());
        Assert.Equal(@"root\cimv2", order.GetProperty("space").GetString());
        Assert.Equal("SELECT Manufacturer, ReleaseDate FROM Win32_BIOS", order.GetProperty("query").GetString());

        var results = (await Json(r)).GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(3, results.Count);
        var mine = results[0];
        Assert.True(mine.GetProperty("ok").GetBoolean());
        Assert.Equal("SHOP-PC", mine.GetProperty("computer").GetString());
        Assert.Equal("ReleaseDate", mine.GetProperty("columns")[1].GetString());
        Assert.Equal("date", mine.GetProperty("kinds")[1].GetString());
        Assert.Equal("Dell Inc.", mine.GetProperty("rows")[0][0].GetString());

        // An older client isn't asked; one that isn't listening says so.
        Assert.False(results[1].GetProperty("ok").GetBoolean());
        Assert.Contains("1.1.173", results[1].GetProperty("error").GetString());
        Assert.Contains("isn't connected", results[2].GetProperty("error").GetString());

        // Each query is in the activity log, with what was asked.
        var log = await admin.GetFromJsonAsync<JsonElement>("/api/activity");
        Assert.Contains("Ran a WMI query on 3 computers", log.GetRawText());
        Assert.Contains("Win32_BIOS", log.GetRawText());
    }

    [Fact]
    public async Task A_computer_that_cant_read_it_says_why()
    {
        var admin = await Admin();
        var (id, client) = await Computer("SHOP-PC");
        var served = await Listening(id, client, o => new { id = o.GetProperty("id").GetString(), ok = false, error = "SHOP-PC has no class like that in root\\cimv2." });
        var r = await admin.PostAsJsonAsync("/api/devices/wmi", new { clients = new[] { id }, query = "SELECT * FROM Win32_Nothing" });
        await served;
        var one = (await Json(r)).GetProperty("results")[0];
        Assert.False(one.GetProperty("ok").GetBoolean());
        Assert.Equal("SHOP-PC has no class like that in root\\cimv2.", one.GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_class_list_comes_from_one_computer()
    {
        var admin = await Admin();
        var (id, client) = await Computer("SHOP-PC");
        var served = await Listening(id, client, o => new
        {
            id = o.GetProperty("id").GetString(), ok = true, spaces = new[] { @"root\cimv2\mdm" }, classes = new[] { "Win32_BIOS", "Win32_Process" },
        });
        var r = await admin.PostAsJsonAsync("/api/devices/wmi/classes", new { clients = new[] { id }, space = @"root\cimv2" });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        Assert.Equal(AccountOrder.WmiClasses, (await served).GetProperty("action").GetString());
        var j = await Json(r);
        Assert.Equal(@"root\cimv2\mdm", j.GetProperty("spaces")[0].GetString());
        Assert.Equal("Win32_Process", j.GetProperty("classes")[1].GetString());
    }

    [Fact]
    public async Task Only_reads_only_this_computers_namespaces_and_only_with_the_right()
    {
        // What may be asked, on both sides (the client checks again).
        Assert.Null(WmiAsk.BadQuery("select * from Win32_BIOS"));
        Assert.Null(WmiAsk.BadQuery("ASSOCIATORS OF {Win32_Service.Name='Spooler'}"));
        Assert.Null(WmiAsk.BadQuery("REFERENCES OF {Win32_Service.Name='Spooler'}"));
        Assert.NotNull(WmiAsk.BadQuery("DELETE FROM Win32_BIOS"));
        Assert.NotNull(WmiAsk.BadQuery("   "));
        Assert.NotNull(WmiAsk.BadQuery("SELECT " + new string('x', WmiAsk.MaxQuery)));
        Assert.Null(WmiAsk.BadSpace(null));
        Assert.Null(WmiAsk.BadSpace(@"root\cimv2\Security\MicrosoftVolumeEncryption"));
        Assert.Null(WmiAsk.BadSpace("root/wmi"));
        Assert.NotNull(WmiAsk.BadSpace(@"\\dc01\root\cimv2"));
        Assert.NotNull(WmiAsk.BadSpace(@"root\..\x"));

        var admin = await Admin();
        var (id, _) = await Computer("SHOP-PC");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/devices/wmi", new { clients = new[] { id }, query = "DELETE FROM Win32_BIOS" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/devices/wmi", new { clients = new[] { id }, space = @"\\dc01\root\cimv2", query = "SELECT * FROM Win32_BIOS" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/devices/wmi", new { clients = Array.Empty<string>(), query = "SELECT * FROM Win32_BIOS" })).StatusCode);
        var many = Enumerable.Range(0, DevicesApi.WmiMost + 1).Select(i => $"pc{i}").ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/devices/wmi", new { clients = many, query = "SELECT * FROM Win32_BIOS" })).StatusCode);

        // Someone whose roles don't have it gets nowhere.
        app.Services.GetRequiredService<Accounts>().Add("mlopez", "user password", Roles.User);
        var user = app.Page();
        (await user.PostAsJsonAsync("/api/signin", new { username = "mlopez", password = "user password" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/devices/wmi", new { clients = new[] { id }, query = "SELECT * FROM Win32_BIOS" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/devices/wmi/classes", new { clients = new[] { id } })).StatusCode);
    }
}
