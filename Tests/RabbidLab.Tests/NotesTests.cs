using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Notes on computers, AD objects and GPOs: what text is kept, and who reads, adds, changes and deletes them.</summary>
public sealed class NotesTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-notes-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public NotesTests()
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

    private async Task<HttpClient> Person(HttpClient admin, string username, params string[] permissions)
    {
        var made = await admin.PostAsJsonAsync("/api/roles", new { name = username + " role", color = "#5FD4A0" });
        made.EnsureSuccessStatusCode();
        var id = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        (await admin.PostAsJsonAsync($"/api/roles/{id}", new { name = username + " role", color = "#5FD4A0", permissions })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/accounts", new { username, password = "user password", roles = new[] { id } })).EnsureSuccessStatusCode();
        var page = app.Page();
        (await page.PostAsJsonAsync("/api/signin", new { username, password = "user password" })).EnsureSuccessStatusCode();
        await TestApp.Confirm(page, "user password");
        return page;
    }

    private static async Task<List<JsonElement>> Read(HttpClient page, string on) =>
        [.. (await page.GetFromJsonAsync<JsonElement>($"/api/notes?on={Uri.EscapeDataString(on)}")).EnumerateArray()];

    [Fact]
    public void Only_the_editors_own_formatting_is_kept()
    {
        Assert.Null(Notes.BadText("<b>Swapped</b> the <i>drive</i><br><ul><li>one</li></ul>"));
        Assert.Null(Notes.BadText("""See <a href="https://example.com/a?b=1&amp;c=2">the ticket</a> &lt;now&gt;"""));
        Assert.Null(Notes.BadText("""<a href="mailto:help@example.com">mail</a>"""));
        Assert.NotNull(Notes.BadText("<script>alert(1)</script>"));
        Assert.NotNull(Notes.BadText("""<b onclick="x()">hi</b>"""));
        Assert.NotNull(Notes.BadText("""<a href="javascript:alert(1)">x</a>"""));
        Assert.NotNull(Notes.BadText("""<img src=x onerror=alert(1)>"""));
        Assert.NotNull(Notes.BadText("a < b"));
        Assert.NotNull(Notes.BadText("<br>&nbsp;<div></div>"));
        Assert.NotNull(Notes.BadText(new string('a', Notes.MaxText + 1)));
        Assert.False(Notes.Fits("links:abc"));
        Assert.False(Notes.Fits("seat:../x"));
        Assert.True(Notes.Fits("gpo:{31B2F340-016D-11D2-945F-00C04FB984F9}"));
        Assert.True(Notes.Fits("ad:S-1-5-21-1004336348-1177238915-682003330-1105"));
    }

    [Fact]
    public async Task People_who_see_a_thing_add_notes_and_change_only_their_own()
    {
        var admin = await app.SignedIn();
        var on = "seat:abc123";
        var added = await admin.PostAsJsonAsync("/api/notes", new { on, text = "Replaced the <b>SSD</b>.", name = "GARAGE-PC" });
        Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync());
        var id = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var viewer = await Person(admin, "mlopez", "seats.view");
        // The page reads without the X-Rabbid-Lab header (R.get sends it only with changes).
        viewer.DefaultRequestHeaders.Remove(Access.Header);
        var notes = await Read(viewer, on);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/notes/counts?kind=seat")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/notes", new { on, text = "No header." })).StatusCode);
        viewer.DefaultRequestHeaders.Add(Access.Header, "1");
        Assert.Single(notes);
        Assert.Equal("Replaced the <b>SSD</b>.", notes[0].GetProperty("text").GetString());
        Assert.False(notes[0].GetProperty("mine").GetBoolean());
        Assert.False(notes[0].GetProperty("canDelete").GetBoolean());

        // They add their own, but can't change or delete the admin's.
        Assert.True((await viewer.PostAsJsonAsync("/api/notes", new { on, text = "Called the user." })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/notes/{id}", new { text = "Changed." })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/notes/{id}/delete", new { })).StatusCode);
        var theirs = (await Read(viewer, on)).Single(n => n.GetProperty("mine").GetBoolean()).GetProperty("id").GetString()!;
        Assert.True((await viewer.PostAsJsonAsync($"/api/notes/{theirs}", new { text = "Called the user <i>twice</i>." })).IsSuccessStatusCode);

        // Bad formatting is refused, and the counts say how many each thing has.
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.PostAsJsonAsync("/api/notes", new { on, text = "<img src=x onerror=alert(1)>" })).StatusCode);
        var counts = await viewer.GetFromJsonAsync<Dictionary<string, int>>("/api/notes/counts?kind=seat");
        Assert.Equal(2, counts![on]);

        // Whoever may act on computers deletes anyone's, and it's in the activity log.
        Assert.True((await admin.PostAsJsonAsync($"/api/notes/{theirs}/delete", new { name = "GARAGE-PC" })).IsSuccessStatusCode);
        Assert.Single(await Read(admin, on));
        var log = await admin.GetStringAsync("/api/activity");
        Assert.Contains("Added a note on GARAGE-PC", log);
        Assert.Contains("Deleted mlopez's note on GARAGE-PC", log);
    }

    [Fact]
    public async Task Notes_on_something_need_the_permission_that_shows_it()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/notes", new { on = "gpo:{31B2F340-016D-11D2-945F-00C04FB984F9}", text = "Default Domain Policy: don't touch." })).EnsureSuccessStatusCode();
        var devicesOnly = await Person(admin, "jreyes", "seats.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await devicesOnly.GetAsync("/api/notes?on=gpo:{31B2F340-016D-11D2-945F-00C04FB984F9}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await devicesOnly.PostAsJsonAsync("/api/notes", new { on = "ad:S-1-5-21-1-2-3-1105", text = "Hi" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await devicesOnly.GetAsync("/api/notes/counts?kind=gpo")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await devicesOnly.GetAsync("/api/notes?on=links:1")).StatusCode);
    }
}
