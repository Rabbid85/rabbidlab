using System.Net;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.Equal("it refused the connection", third.GetProperty("states").GetProperty(quiet).GetProperty("why").GetString()); // and why
        Assert.Equal((true, false), State(third, up));
        Assert.Equal(60, third.GetProperty("next").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(15, set.GetProperty("checks").GetProperty("every").GetInt32());
        Assert.Equal(0, set.GetProperty("checks").GetProperty("downAfter").GetInt32());
    }

    [Fact]
    public async Task One_missed_check_is_never_down_however_late_the_page_asks_again()
    {
        // A port nothing listens on, then a site on it.
        var held = new TcpListener(IPAddress.Loopback, 0);
        held.Start();
        var port = ((IPEndPoint) held.LocalEndpoint).Port;
        held.Stop();
        var admin = await app.SignedIn();
        var id = (await Json(await admin.PostAsJsonAsync("/api/links", new { url = $"http://127.0.0.1:{port}", name = "Slow" }))).GetProperty("id").GetString()!;

        var missed = await Json(await admin.GetAsync("/api/links/states"));
        var back = new TcpListener(IPAddress.Loopback, port);
        back.Start();
        _ = Task.Run(async () =>
        {
            using var client = await back.AcceptTcpClientAsync();
            await client.GetStream().ReadAsync(new byte[4096]);
            await client.GetStream().WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray());
        });
        app.Clock.Advance(TimeSpan.FromSeconds(45)); // past the 30 seconds, and before the next check a minute on
        var late = await Json(await admin.GetAsync("/api/links/states"));
        back.Stop();

        Assert.False(missed.GetProperty("states").GetProperty(id).GetProperty("up").GetBoolean());
        Assert.False(missed.GetProperty("states").GetProperty(id).GetProperty("down").GetBoolean());
        Assert.True(late.GetProperty("states").GetProperty(id).GetProperty("up").GetBoolean()); // asked again, not shown red from the clock
        Assert.False(late.GetProperty("states").GetProperty(id).GetProperty("down").GetBoolean());
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
    public async Task A_test_address_is_read_as_a_test_site_and_its_link_goes_after_30_seconds()
    {
        var links = app.Services.GetRequiredService<Links>();

        var up = await DevTools.Look("https://up-07.rabbidlab.test", CancellationToken.None);
        var down = await DevTools.Look("down-03.rabbidlab.test", CancellationToken.None);
        var (test, _) = links.Save(null, new LinkForm("Test 07", "https://up-07.rabbidlab.test", null, null, null, null), "test", testing: true);
        var (gone, _) = links.Save(null, new LinkForm("Down", "https://down-03.rabbidlab.test", null, null, null, null), "test", testing: true);
        var (plain, _) = links.Save(null, new LinkForm("Plain", "https://up-08.rabbidlab.test", null, null, null, null), "test");
        var states = (await links.States(CancellationToken.None)).States;
        app.Clock.Advance(TimeSpan.FromSeconds(31));
        var left = links.All().Select(l => l.Name).ToList();

        Assert.Equal("Test 07", up.Name);
        Assert.StartsWith("data:image/svg+xml", up.Icon);
        Assert.NotNull(down.Problem);
        Assert.True(states[test!.Id].Up);
        Assert.True(states[gone!.Id].Down);
        Assert.Null(plain!.Expires); // without Developer tools on, it's an ordinary link
        Assert.Equal(["Plain"], left);
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
    public async Task An_icon_the_page_does_not_list_is_found_where_an_iDRAC_keeps_it()
    {
        // Like an iDRAC 8: a front page that sends the browser on by script, an empty icon, and the real one in /images.
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9, 9, 9];
        using var site = new TinySite(new()
        {
            ["/"] = ("text/html", Encoding.UTF8.GetBytes("<html><head><link rel=\"shortcut icon\" href=\"data:image/x-icon;,\"><script>top.document.location.href = \"/login.html\";</script></head></html>")),
            ["/images/favicon.png"] = ("image/png", png),
        });

        var look = await Links.Look($"http://127.0.0.1:{site.Port}", CancellationToken.None, guarded: false);

        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(png), look.Icon);
    }

    [Fact]
    public async Task A_page_saying_unified_is_not_UniFi_and_a_big_ico_is_cut_down_to_one_picture()
    {
        // A .ico holding a 256 pixel picture too big to keep and a 64 pixel PNG, as Pelican's does: the PNG is kept.
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7, 7, 7];
        var big = new byte[300 * 1024];
        var ico = new byte[6 + 32 + big.Length + png.Length];
        ico[2] = 1;
        ico[4] = 2;
        void Entry(int at, byte side, int size, int offset)
        {
            ico[at] = side;
            ico[at + 1] = side;
            BitConverter.GetBytes((ushort) 1).CopyTo(ico, at + 4);
            BitConverter.GetBytes((ushort) 32).CopyTo(ico, at + 6);
            BitConverter.GetBytes((uint) size).CopyTo(ico, at + 8);
            BitConverter.GetBytes((uint) offset).CopyTo(ico, at + 12);
        }
        Entry(6, 0, big.Length, 38);
        Entry(22, 64, png.Length, 38 + big.Length);
        png.CopyTo(ico, 38 + big.Length);
        using var site = new TinySite(new()
        {
            ["/"] = ("text/html", Encoding.UTF8.GetBytes("<html><head><title>Phones</title><meta name=\"description\" content=\"Calls and chat\"><link rel=\"icon\" href=\"/big.ico\"></head><body>Unified communications</body></html>")),
            ["/big.ico"] = ("image/x-icon", ico),
        });

        var look = await Links.Look($"http://127.0.0.1:{site.Port}", CancellationToken.None, guarded: false, describe: true);

        Assert.Equal("Calls and chat", look.Description);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(png), look.Icon);
        Assert.Null(KnownSites.Describe("Hulu", "https://www.hulu.com/")); // and an internet site isn't described from its page at all
    }

    [Fact]
    public async Task An_icon_saved_before_origins_were_kept_gives_way_to_the_tab_icon_when_the_site_serves_it()
    {
        // Like Plex: the tab's icon, and a phone icon that's another design, which used to be taken first.
        byte[] tab = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 1, 1, 1];
        byte[] phone = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 2, 2, 2, 2];
        var page = Encoding.UTF8.GetBytes("<html><head><title>Plex</title><link rel=\"shortcut icon\" href=\"/tab.png\"><link rel=\"apple-touch-icon-precomposed\" sizes=\"144x144\" href=\"/phone.png\"></head></html>");
        Dictionary<string, (string Type, byte[] Body)> pages = new() { ["/"] = ("text/html", page), ["/b/"] = ("text/html", page), ["/tab.png"] = ("image/png", tab), ["/phone.png"] = ("image/png", phone) };
        using var site = new TinySite(pages);
        static string Png(byte[] b) => "data:image/png;base64," + Convert.ToBase64String(b);
        var admin = await app.SignedIn();
        async Task<string> Add(object link) => (await Json(await admin.PostAsJsonAsync("/api/links", link))).GetProperty("id").GetString()!;
        var old = await Add(new { url = $"http://127.0.0.1:{site.Port}/", name = "Plex", description = "Media", icon = Png(phone) });
        var mine = await Add(new { url = $"http://127.0.0.1:{site.Port}/b/", name = "Mine", description = "Media", icon = Png([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9, 9, 9]) });
        var quiet = await Add(new { url = "http://127.0.0.1:1/", name = "Quiet", description = "Media", icon = Png(phone) });
        // As a version that didn't keep where icons came from saved them.
        var links = app.Services.GetRequiredService<Links>();
        foreach (var l in links.All())
        {
            l.IconFrom = null;
        }
        Assert.All(links.All(), l => Assert.Null(l.IconFrom));

        var swapped = await links.Refresh(old, CancellationToken.None, guarded: false);
        var kept = await links.Refresh(mine, CancellationToken.None, guarded: false);
        await links.Refresh(quiet, CancellationToken.None, guarded: false);
        var all = links.All().ToDictionary(l => l.Id);

        Assert.Equal(["icon"], swapped!.Changed);
        Assert.Equal(Png(tab), all[old].Icon);
        Assert.Equal("/tab.png", all[old].IconFrom);
        Assert.Empty(kept!.Changed);
        Assert.Equal("own", all[mine].IconFrom); // someone's own: never swapped, and not checked again
        Assert.Equal("kept", all[quiet].IconFrom); // no answer: left as it is, and not read again for this

        // The site changes its icon: one that came from the site follows it, one of someone's own doesn't.
        byte[] fresh = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 3, 3, 3, 3];
        pages["/tab.png"] = ("image/png", fresh);
        var followed = await links.Refresh(old, CancellationToken.None, guarded: false);
        await links.Refresh(mine, CancellationToken.None, guarded: false);
        all = links.All().ToDictionary(l => l.Id);
        Assert.Equal(["icon"], followed!.Changed);
        Assert.Equal(Png(fresh), all[old].Icon);
        Assert.Equal("own", all[mine].IconFrom);
        Assert.Equal(Png(phone), all[quiet].Icon);
    }

    [Fact]
    public async Task Refresh_fills_in_what_a_link_is_missing_and_keeps_what_was_set_by_hand()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 5, 6, 7, 8];
        var page = Encoding.UTF8.GetBytes("<html><head><title>Shelfy</title><meta name=\"description\" content=\"Pantry list\"><link rel=\"icon\" href=\"/i.png\"></head></html>");
        using var site = new TinySite(new() { ["/"] = ("text/html", page), ["/b/"] = ("text/html", page), ["/i.png"] = ("application/octet-stream", png) });
        var admin = await app.SignedIn();
        async Task<string> Add(object link) => (await Json(await admin.PostAsJsonAsync("/api/links", link))).GetProperty("id").GetString()!;
        var bare = await Add(new { url = $"http://127.0.0.1:{site.Port}/", name = "Pantry" });
        var mine = await Add(new { url = $"http://127.0.0.1:{site.Port}/b/", name = "My pantry", description = "My own words", icon = "none", address = "pantry.lan" });
        var quiet = await Add(new { url = "http://127.0.0.1:1", name = "Quiet" });
        var links = app.Services.GetRequiredService<Links>();

        // The stand-in site is on this computer, which a real lookup doesn't read: unguarded, for the test.
        var filled = await links.Refresh(bare, CancellationToken.None, guarded: false);
        var kept = await links.Refresh(mine, CancellationToken.None, guarded: false);
        var none = await links.Refresh(quiet, CancellationToken.None, guarded: false);
        var again = await links.Refresh(bare, CancellationToken.None, guarded: false);
        var missing = await admin.PostAsync("/api/links/nope/refresh", null);
        var all = links.All().ToDictionary(l => l.Id);

        Assert.Equal(["icon", "description"], filled!.Changed);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(png), all[bare].Icon);
        Assert.Equal("Pantry list", all[bare].Description);
        Assert.Equal("Pantry", all[bare].Name);
        Assert.Empty(kept!.Changed);
        Assert.Equal("none", all[mine].Icon);
        Assert.Equal("My own words", all[mine].Description);
        Assert.Equal("pantry.lan", all[mine].Address);
        Assert.Empty(none!.Changed);
        Assert.NotNull(none.Problem);
        Assert.Empty(again!.Changed); // nothing left to fill in
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
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
                    // A page asked for with its query, such as /webapi/entry.cgi?api=SYNO.DSM.Info, also answers a longer query that starts the same.
                    var key = pages.ContainsKey(path) ? path : pages.Keys.FirstOrDefault(k => k.Contains('?') && path.StartsWith(k, StringComparison.Ordinal)) ?? path;
                    var (type, body) = pages.TryGetValue(key, out var page) ? page : ("text/plain", "no"u8.ToArray());
                    var head = $"HTTP/1.1 {(pages.ContainsKey(key) ? "200 OK" : "404 Not Found")}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
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
