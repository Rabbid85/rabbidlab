using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>The Links page: links kept and checked, who may change them, and reading a site for its name and icon.</summary>
public sealed class LinksTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-links-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public LinksTests()
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

    [Fact]
    public async Task Links_are_kept_checked_and_only_changed_by_people_allowed_to()
    {
        var admin = await app.SignedIn();
        var made = await Json(await admin.PostAsJsonAsync("/api/links", new { url = "192.168.2.23", group = "Storage", description = "Pool tank" }));
        var bad = await admin.PostAsJsonAsync("/api/links", new { url = "javascript:alert(1)" });
        var badIcon = await admin.PostAsJsonAsync("/api/links", new { url = "https://nas", icon = "https://elsewhere/x.png" });
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "viewer", password = "viewer password" })).EnsureSuccessStatusCode();
        var viewer = app.Page();
        (await viewer.PostAsJsonAsync("/api/signin", new { username = "viewer", password = "viewer password" })).EnsureSuccessStatusCode();

        var seen = await Json(await viewer.GetAsync("/api/links"));
        var refused = await viewer.PostAsJsonAsync("/api/links", new { url = "https://router" });
        var id = made.GetProperty("id").GetString();
        (await admin.PostAsJsonAsync($"/api/links/{id}", new { url = "https://192.168.2.23", name = "TrueNAS" })).EnsureSuccessStatusCode();
        var changed = await Json(await admin.GetAsync("/api/links"));
        (await admin.PostAsJsonAsync($"/api/links/{id}/delete", new { })).EnsureSuccessStatusCode();
        var gone = await Json(await admin.GetAsync("/api/links"));
        var plain = await Json(await admin.PostAsJsonAsync("/api/links", new { url = "https://printer", icon = "none" }));

        Assert.Equal("http://192.168.2.23/", made.GetProperty("url").GetString());
        Assert.Equal("192.168.2.23", made.GetProperty("name").GetString()); // the host, until it's named
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badIcon.StatusCode);
        Assert.Equal(1, seen.GetProperty("links").GetArrayLength()); // User can see links to begin with
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("TrueNAS", changed.GetProperty("links")[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, changed.GetProperty("links")[0].GetProperty("group").ValueKind);
        Assert.Equal(0, gone.GetProperty("links").GetArrayLength());
        Assert.Equal("none", plain.GetProperty("icon").GetString()); // kept as no icon at all
    }

    [Fact]
    public async Task Groups_are_made_renamed_reordered_and_deleted_and_their_links_follow()
    {
        var admin = await app.SignedIn();
        static string[] Names(JsonElement j, string list, string? field = null) =>
            [.. j.GetProperty(list).EnumerateArray().Select(e => (field is null ? e : e.GetProperty(field)).GetString() ?? "")];
        (await admin.PostAsJsonAsync("/api/links", new { url = "https://nas", name = "NAS", group = "Storage" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/links", new { url = "https://router", name = "Router", group = "Network" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/links", new { url = "https://loose", name = "Loose" })).EnsureSuccessStatusCode();

        var empty = await Json(await admin.PostAsJsonAsync("/api/links/groups", new { name = " Cameras " }));
        var twice = await admin.PostAsJsonAsync("/api/links/groups", new { name = "cameras" });
        var nameless = await admin.PostAsJsonAsync("/api/links/groups", new { name = " " });
        // A link typed into a group in other capitals joins the group that's there.
        (await admin.PostAsJsonAsync("/api/links", new { url = "https://switch", name = "Switch", group = "network" })).EnsureSuccessStatusCode();
        var ordered = await Json(await admin.PostAsJsonAsync("/api/links/groups/order", new { names = new[] { "Cameras", "Network" } }));
        var renamed = await Json(await admin.PostAsJsonAsync("/api/links/groups/rename", new { name = "network", to = "Core" }));
        var taken = await admin.PostAsJsonAsync("/api/links/groups/rename", new { name = "Core", to = "storage" });
        var deleted = await Json(await admin.PostAsJsonAsync("/api/links/groups/delete", new { name = "Storage" }));
        var missing = await admin.PostAsJsonAsync("/api/links/groups/delete", new { name = "Storage" });
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "viewer", password = "viewer password" })).EnsureSuccessStatusCode();
        var viewer = app.Page();
        (await viewer.PostAsJsonAsync("/api/signin", new { username = "viewer", password = "viewer password" })).EnsureSuccessStatusCode();
        var refused = await viewer.PostAsJsonAsync("/api/links/groups", new { name = "Mine" });
        var kept = await Json(await viewer.GetAsync("/api/links"));

        Assert.Equal(["Storage", "Network", "Cameras"], Names(empty, "groups")); // made in the order they came
        Assert.Equal(HttpStatusCode.BadRequest, twice.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nameless.StatusCode);
        Assert.Equal(["Cameras", "Network", "Storage"], Names(ordered, "groups")); // the one left out goes after
        Assert.Equal(["Router", "Switch", "NAS", "Loose"], Names(ordered, "links", "name")); // by group, then name, no group last
        Assert.Equal(["Cameras", "Core", "Storage"], Names(renamed, "groups"));
        Assert.Equal(["Core", "Core", "Storage", ""], Names(renamed, "links", "group"));
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Equal(["Cameras", "Core"], Names(deleted, "groups"));
        Assert.Equal(["Router", "Switch", "Loose", "NAS"], Names(deleted, "links", "name")); // the NAS is kept, with no group
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(["Cameras", "Core"], Names(kept, "groups"));
    }

    [Fact]
    public async Task A_link_shows_as_down_only_after_going_unanswered_for_the_time_set()
    {
        using var site = new TinySite(new() { ["/"] = ("text/html", "<html></html>"u8.ToArray()) });
        var admin = await app.SignedIn();
        var up = (await Json(await admin.PostAsJsonAsync("/api/links", new { url = $"http://127.0.0.1:{site.Port}", name = "Up" }))).GetProperty("id").GetString()!;
        var quiet = (await Json(await admin.PostAsJsonAsync("/api/links", new { url = "http://127.0.0.1:1", name = "Quiet" }))).GetProperty("id").GetString()!;
        var start = await Json(await admin.GetAsync("/api/links"));

        var first = await Json(await admin.GetAsync("/api/links/states"));
        app.Clock.Advance(TimeSpan.FromSeconds(10));
        var second = await Json(await admin.GetAsync("/api/links/states"));
        app.Clock.Advance(TimeSpan.FromSeconds(20));
        var third = await Json(await admin.GetAsync("/api/links/states"));
        var unknown = await admin.PostAsJsonAsync("/api/links/checks", new { every = 7 });
        var set = await Json(await admin.PostAsJsonAsync("/api/links/checks", new { every = 15, downAfter = 0 }));

        static (bool Up, bool Down) State(JsonElement j, string id) => (j.GetProperty("states").GetProperty(id).GetProperty("up").GetBoolean(), j.GetProperty("states").GetProperty(id).GetProperty("down").GetBoolean());
        Assert.Equal(60, start.GetProperty("checks").GetProperty("every").GetInt32());
        Assert.Equal(30, start.GetProperty("checks").GetProperty("downAfter").GetInt32());
        Assert.Equal((true, false), State(first, up));
        Assert.Equal((false, false), State(first, quiet)); // one check with no answer isn't down yet
        Assert.Equal(10, first.GetProperty("next").GetInt32()); // and it's tried again sooner
        Assert.Equal((false, false), State(second, quiet));
        Assert.Equal((false, true), State(third, quiet)); // 30 seconds with no answer
        Assert.Equal((true, false), State(third, up));
        Assert.Equal(60, third.GetProperty("next").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(15, set.GetProperty("checks").GetProperty("every").GetInt32());
        Assert.Equal(0, set.GetProperty("checks").GetProperty("downAfter").GetInt32());
    }

    [Fact]
    public async Task Links_groups_and_check_times_are_exported_and_imported_beside_what_is_here()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/links", new { url = "https://nas", name = "NAS", group = "Storage", icon = "none" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/links/groups", new { name = "Empty" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/links/checks", new { every = 15, downAfter = 0 })).EnsureSuccessStatusCode();
        var file = await Json(await admin.GetAsync("/api/links/export"));

        // Everything is taken away, then the file is brought back in, twice.
        var id = (await Json(await admin.GetAsync("/api/links"))).GetProperty("links")[0].GetProperty("id").GetString();
        (await admin.PostAsJsonAsync($"/api/links/{id}/delete", new { })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/links/groups/delete", new { name = "Storage" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/links/groups/delete", new { name = "Empty" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/links/checks", new { every = 60, downAfter = 30 })).EnsureSuccessStatusCode();
        var back = await Json(await admin.PostAsJsonAsync("/api/links/import", file));
        var again = await Json(await admin.PostAsJsonAsync("/api/links/import", file));
        var other = await admin.PostAsJsonAsync("/api/links/import", new { kind = "something else", links = Array.Empty<object>() });
        var now = again.GetProperty("now");

        Assert.Equal("rabbidlab-links", file.GetProperty("kind").GetString());
        Assert.False(file.GetProperty("links")[0].TryGetProperty("id", out _)); // a link is new wherever it lands
        Assert.Equal(1, back.GetProperty("links").GetInt32());
        Assert.Equal(2, back.GetProperty("groups").GetInt32());
        Assert.True(back.GetProperty("checks").GetBoolean());
        Assert.Equal(0, again.GetProperty("links").GetInt32());
        Assert.Equal(1, again.GetProperty("skipped").GetInt32()); // its address is here already
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
        Assert.Equal(1, now.GetProperty("links").GetArrayLength());
        Assert.Equal("NAS", now.GetProperty("links")[0].GetProperty("name").GetString());
        Assert.Equal("Storage", now.GetProperty("links")[0].GetProperty("group").GetString());
        Assert.Equal("none", now.GetProperty("links")[0].GetProperty("icon").GetString());
        Assert.Equal(["Storage", "Empty"], now.GetProperty("groups").EnumerateArray().Select(g => g.GetString()));
        Assert.Equal(15, now.GetProperty("checks").GetProperty("every").GetInt32());
        Assert.Equal(0, now.GetProperty("checks").GetProperty("downAfter").GetInt32());
    }

    [Fact]
    public async Task Links_dragged_into_an_order_keep_it_and_the_rest_follow_by_name()
    {
        var admin = await app.SignedIn();
        async Task<string> Add(string name, string? group = null) =>
            (await Json(await admin.PostAsJsonAsync("/api/links", new { url = $"https://{name.ToLowerInvariant()}", name, group }))).GetProperty("id").GetString()!;
        var alpha = await Add("Alpha", "Lab");
        await Add("Bravo", "Lab");
        var charlie = await Add("Charlie");
        await Add("Delta", "Lab");

        // Charlie is dragged into Lab, ahead of Alpha.
        var now = await Json(await admin.PostAsJsonAsync("/api/links/arrange", new { group = "Lab", ids = new[] { charlie, alpha } }));
        var links = now.GetProperty("links").EnumerateArray().ToList();

        Assert.Equal(["Charlie", "Alpha", "Bravo", "Delta"], links.Select(l => l.GetProperty("name").GetString()));
        Assert.All(links, l => Assert.Equal("Lab", l.GetProperty("group").GetString()));
    }

    [Fact]
    public async Task Looking_a_site_up_reads_its_name_icon_and_address()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
        using var site = new TinySite(new()
        {
            ["/"] = ("text/html", Encoding.UTF8.GetBytes("<html><head><title>  My   NAS &amp; more </title><meta name=\"description\" content=\"Shares\"><link rel=\"icon\" href=\"/i.png\"></head></html>")),
            ["/i.png"] = ("application/octet-stream", png),
        });

        // The stand-in site is on this computer, which a real lookup doesn't read: unguarded, for the test.
        var look = await Links.Look($"http://127.0.0.1:{site.Port}", CancellationToken.None, guarded: false);
        var nothing = await Links.Look("http://127.0.0.1:1", CancellationToken.None, guarded: false);

        Assert.Equal("My NAS & more", look.Name);
        Assert.Equal("Shares", look.Description);
        Assert.Equal("127.0.0.1", look.Address);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(png), look.Icon);
        Assert.Null(look.Problem);
        Assert.NotNull(nothing.Problem);
        Assert.Equal("127.0.0.1", nothing.Address);
    }

    [Fact]
    public async Task A_lookup_follows_a_front_page_to_the_real_one_and_leaves_a_broken_icon()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
        // ESXi's front page only sends the browser on to /ui.
        using var sends = new TinySite(new()
        {
            ["/"] = ("text/html", Encoding.UTF8.GetBytes("<html><head><meta http-equiv=\"refresh\" content=\"0;URL='/ui'\"/></head></html>")),
            ["/ui"] = ("text/html", Encoding.UTF8.GetBytes("<html><head><title>Host client</title><link rel=\"icon\" href=\"/ui/i.png\"></head></html>")),
            ["/ui/i.png"] = ("image/png", png),
        });
        // vCenter's has a title a script writes and no icon: the page behind its first real link has both.
        using var launcher = new TinySite(new()
        {
            ["/"] = ("text/html", Encoding.UTF8.GetBytes("<html><head><script>document.write(\"<title>\" + ID_VC_Welcome + \"</title>\");</script></head><body><a href=\"#top\">Top</a><a href=\"/ui/\">Launch</a></body></html>")),
            ["/ui/"] = ("text/html", Encoding.UTF8.GetBytes("<html><head><title>vSphere Client</title><link rel=\"icon\" href=\"i.png\"></head></html>")),
            ["/ui/i.png"] = ("image/png", png),
        });
        // An icon file that stops after its header can't be drawn.
        using var broken = new TinySite(new()
        {
            ["/"] = ("text/html", Encoding.UTF8.GetBytes("<html><head><title>iDRAC</title><link rel=\"icon\" href=\"/i.ico\"></head></html>")),
            ["/i.ico"] = ("image/x-icon", [0, 0, 1, 0, 1, 0, 16, 16]),
        });

        var sent = await Links.Look($"http://127.0.0.1:{sends.Port}", CancellationToken.None, guarded: false);
        var launched = await Links.Look($"http://127.0.0.1:{launcher.Port}", CancellationToken.None, guarded: false);
        var plain = await Links.Look($"http://127.0.0.1:{broken.Port}", CancellationToken.None, guarded: false);

        Assert.Equal("Host client", sent.Name);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(png), sent.Icon);
        Assert.Equal("vSphere Client", launched.Name);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(png), launched.Icon);
        Assert.Equal("iDRAC", plain.Name);
        Assert.Null(plain.Icon);
    }

    [Fact]
    public void Known_products_are_recognised_by_name_host_name_or_page()
    {
        Assert.Equal("TrueNAS storage", KnownSites.Describe("nas01", "https://truenas.amw.com"));
        Assert.Equal("Dell iDRAC out-of-band management", KnownSites.Describe("idrac-r730", "https://192.168.2.7"));
        Assert.Equal("UniFi network console", KnownSites.Describe("UniFi OS", "https://192.168.2.1"));
        // Pages whose title a script fills in, as the real ones send them.
        Assert.Equal("VMware ESXi host", KnownSites.Describe("r640", "https://192.168.2.21/ui/", "<html ng-app=\"esxUiApp\"><meta name=\"description\" content=\"ESXi Host Client\">"));
        Assert.Equal("TrueNAS storage", KnownSites.Describe(null, "https://192.168.2.23", "<title id=\"main-page-title\"></title><body><ix-root></ix-root></body>"));
        Assert.Equal("VMware vCenter Server", KnownSites.Describe(null, "https://192.168.2.19", "<meta name=\"description\" content=\"VMware vSphere is ... VMware ESX Server ...\">"));
        Assert.Null(KnownSites.Describe("Complex", "https://example.lan", "<p>Nothing to see</p>"));
        Assert.True(KnownSites.IsOurs("TrueNAS storage"));
        Assert.False(KnownSites.IsOurs("ZFS storage, SMB shares"));
    }

    [Fact]
    public async Task A_lookup_describes_a_known_product_only_when_asked_to()
    {
        using var site = new TinySite(new() { ["/"] = ("text/html", "<html><head><title>Pi-hole - dns01</title><meta name=\"description\" content=\"Network-wide ad blocking\"></head></html>"u8.ToArray()) });

        var plain = await Links.Look($"http://127.0.0.1:{site.Port}", CancellationToken.None, guarded: false);
        var described = await Links.Look($"http://127.0.0.1:{site.Port}", CancellationToken.None, guarded: false, describe: true);

        Assert.Equal("Network-wide ad blocking", plain.Description);
        Assert.Equal("Pi-hole DNS filtering", described.Description);
    }

    [Fact]
    public async Task Turning_descriptions_off_removes_the_written_ones_and_on_puts_them_back()
    {
        var admin = await app.SignedIn();
        await admin.PostAsJsonAsync("/api/links", new { url = "https://truenas.lab", name = "truenas" });
        await admin.PostAsJsonAsync("/api/links", new { url = "https://idrac.lab", name = "iDRAC R730", description = "Dell iDRAC out-of-band management" });
        await admin.PostAsJsonAsync("/api/links", new { url = "http://127.0.0.1:1", name = "Grafana", description = "Mine" });
        static string? About(JsonElement now, string name) =>
            now.GetProperty("links").EnumerateArray().First(l => l.GetProperty("name").GetString() == name).GetProperty("description").GetString();

        var off = await Json(await admin.PostAsJsonAsync("/api/links/checks", new { describe = false }));
        var on = await Json(await admin.PostAsJsonAsync("/api/links/checks", new { describe = true }));

        Assert.False(off.GetProperty("checks").GetProperty("describe").GetBoolean());
        Assert.Null(About(off, "iDRAC R730"));
        Assert.Equal("Mine", About(off, "Grafana"));
        Assert.True(on.GetProperty("checks").GetProperty("describe").GetBoolean());
        Assert.Equal("TrueNAS storage", About(on, "truenas"));
        Assert.Equal("Dell iDRAC out-of-band management", About(on, "iDRAC R730"));
        Assert.Equal("Mine", About(on, "Grafana")); // typed, so it stays although Grafana is a known product
    }

    [Fact]
    public async Task A_lookup_never_reads_a_site_on_this_server_or_a_link_local_address()
    {
        using var site = new TinySite(new() { ["/"] = ("text/html", "<html><head><title>Something private</title></head></html>"u8.ToArray()) });

        var here = await Links.Look($"http://127.0.0.1:{site.Port}", CancellationToken.None);
        var byName = await Links.Look($"http://localhost:{site.Port}", CancellationToken.None);

        Assert.Contains("on this server", here.Problem);
        Assert.Equal("127.0.0.1", here.Name); // the host as typed, nothing the site says
        Assert.Contains("on this server", byName.Problem);
        Assert.Equal("localhost", byName.Name);
        // Refused: this computer, the link-local ranges (where cloud metadata lives) and "any address".
        Assert.All(new[] { "127.0.0.1", "127.8.9.10", "::1", "::ffff:127.0.0.1", "169.254.169.254", "fe80::1", "0.0.0.0", "::" }, a => Assert.True(Links.Refused(IPAddress.Parse(a)), a));
        // Read: the rest of the LAN, which is what Links is for.
        Assert.All(new[] { "192.0.2.10", "198.51.100.7", "fd12:3456::1", "8.8.8.8" }, a => Assert.False(Links.Refused(IPAddress.Parse(a)), a));
    }

    [Fact]
    public void Addresses_get_a_scheme_and_scripts_are_turned_away()
    {
        Assert.Equal("http://truenas.amw.com:8080/ui", Links.Clean("truenas.amw.com:8080/ui"));
        Assert.Equal("ssh://192.168.2.18/", Links.Clean("ssh://192.168.2.18"));
        Assert.Null(Links.Clean("javascript:alert(1)"));
        Assert.Null(Links.Clean("data:text/html,hi"));
        Assert.Null(Links.Clean(""));
    }

    /// <summary>A web server on a real port, answering GETs for the paths it's given.</summary>
    internal sealed class TinySite : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();

        public TinySite(Dictionary<string, (string Type, byte[] Body)> pages)
        {
            listener.Start();
            Port = ((IPEndPoint) listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    var stream = client.GetStream();
                    var request = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var path = (await request.ReadLineAsync())?.Split(' ').ElementAtOrDefault(1) ?? "/";
                    while (!string.IsNullOrEmpty(await request.ReadLineAsync()))
                    {
                        // The headers: not needed.
                    }
                    var (type, body) = pages.TryGetValue(path, out var page) ? page : ("text/plain", "no"u8.ToArray());
                    var head = $"HTTP/1.1 {(pages.ContainsKey(path) ? "200 OK" : "404 Not Found")}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                    await stream.WriteAsync(body);
                }
            });
        }

        public int Port { get; }

        public void Dispose()
        {
            stop.Cancel();
            listener.Stop();
        }
    }
}
