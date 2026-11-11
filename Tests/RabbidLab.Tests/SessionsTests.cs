using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Sessions kept by the server, and what admins and users may each do.</summary>
public sealed class SessionsTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-sessions-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public SessionsTests()
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

    private static async Task<JsonElement> Json(HttpClient page, string path) => await (await page.GetAsync(path)).Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task An_admin_sees_every_session_and_can_sign_one_out()
    {
        var mine = await app.SignedIn();
        var other = app.Page();
        (await other.PostAsJsonAsync("/api/signin", new { username = "admin", password = "correct horse" })).EnsureSuccessStatusCode();

        var list = await Json(mine, "/api/sessions");
        var theirs = list.EnumerateArray().Single(s => !s.GetProperty("current").GetBoolean()).GetProperty("id").GetString();
        var ended = await mine.PostAsync($"/api/sessions/{theirs}/signout", null);

        Assert.Equal(2, list.GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mine.GetAsync("/api/status")).StatusCode);
    }

    [Fact]
    public async Task A_user_sees_only_their_own_sessions_and_cannot_change_Rabbid_Lab()
    {
        var admin = await app.SignedIn();
        app.Services.GetRequiredService<Accounts>().Add("mlopez", "user password", Roles.User);
        var user = app.Page();
        (await user.PostAsJsonAsync("/api/signin", new { username = "mlopez", password = "user password" })).EnsureSuccessStatusCode();

        var me = await Json(user, "/api/signin");
        var sessions = await Json(user, "/api/sessions");
        var adminSession = (await Json(admin, "/api/sessions")).EnumerateArray().First(s => s.GetProperty("username").GetString() == "admin").GetProperty("id").GetString();

        Assert.Equal(["user"], me.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("id").GetString()));
        Assert.Equal("mlopez", Assert.Single(sessions.EnumerateArray()).GetProperty("username").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/update/install", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/update/settings", new { automatic = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/activity")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync($"/api/sessions/{adminSession}/signout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/activity")).StatusCode);
    }

    [Fact]
    public async Task Whoever_sets_up_Active_Directory_is_the_administrator_and_everyone_else_a_user()
    {
        var setup = app.Page();
        (await setup.PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" })).EnsureSuccessStatusCode();
        var jreyes = app.Page();
        (await jreyes.PostAsJsonAsync("/api/signin", new { username = "jreyes", password = "user pass" })).EnsureSuccessStatusCode();

        static IEnumerable<string?> Ids(JsonElement me) => me.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("id").GetString());
        Assert.Equal(["admin", "user"], Ids(await Json(setup, "/api/signin")));
        Assert.Equal(["user"], Ids(await Json(jreyes, "/api/signin")));
        Assert.Equal(HttpStatusCode.Forbidden, (await jreyes.GetAsync("/api/activity")).StatusCode);
    }

    [Fact]
    public async Task Restarting_signs_nobody_out()
    {
        var setup = await app.Page().PostAsJsonAsync("/api/setup/local", new { username = "admin", password = "correct horse" });
        var cookie = setup.Headers.GetValues("Set-Cookie").First().Split(';')[0];
        app.Dispose();

        using var again = new TestApp(data);
        var page = again.CreateClient();
        page.DefaultRequestHeaders.Add("Cookie", cookie);

        Assert.Equal(HttpStatusCode.OK, (await page.GetAsync("/api/status")).StatusCode);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1", "Safari on iOS")]
    [InlineData("Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("", "Unknown")]
    public void Sessions_say_the_browser_in_a_few_words(string agent, string device) => Assert.Equal(device, Sessions.Device(agent));
}
