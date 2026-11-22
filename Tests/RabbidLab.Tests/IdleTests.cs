using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Sign out when idle: a session nobody clicks, types or scrolls in ends, however often its page checks in.</summary>
public sealed class IdleTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-idle-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public IdleTests()
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

    /// <summary>The page asking by itself, every minute for this long: what an open tab nobody touches does.</summary>
    private async Task Polling(HttpClient page, int minutes)
    {
        foreach (var _ in Enumerable.Range(0, minutes))
        {
            app.Clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(HttpStatusCode.OK, (await page.GetAsync("/api/status")).StatusCode);
        }
    }

    private async Task<string> Log()
    {
        var admin = app.Page();
        (await admin.PostAsJsonAsync("/api/signin", new { username = "admin", password = "correct horse" })).EnsureSuccessStatusCode();
        return (await admin.GetFromJsonAsync<JsonElement>("/api/activity")).GetRawText();
    }

    [Fact]
    public async Task A_session_nobody_uses_ends_after_30_minutes_even_with_its_page_checking_in()
    {
        var page = await app.SignedIn();
        Assert.Equal(30, (await page.GetFromJsonAsync<JsonElement>("/api/signin")).GetProperty("idle").GetInt32());

        // 30 minutes and the minute's slack for the page's report: still signed in.
        await Polling(page, 31);
        app.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await page.GetAsync("/api/status")).StatusCode);
        Assert.Contains("Signed out after 30 minutes of inactivity", await Log());
    }

    [Fact]
    public async Task Clicking_typing_or_scrolling_keeps_it_signed_in()
    {
        var page = await app.SignedIn();
        foreach (var _ in Enumerable.Range(0, 4))
        {
            await Polling(page, 25);
            var told = await page.PostAsync("/api/sessions/active", null);
            Assert.Equal(HttpStatusCode.OK, told.StatusCode);
            Assert.Equal(30, (await told.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("idle").GetInt32());
        }
        await Polling(page, 25); // 125 minutes in, 25 since the last click

        // Without the page's header, it isn't the page saying so.
        page.DefaultRequestHeaders.Remove("X-Rabbid-Lab");
        Assert.Equal(HttpStatusCode.Forbidden, (await page.PostAsync("/api/sessions/active", null)).StatusCode);
    }

    [Fact]
    public async Task It_can_be_set_longer_shorter_or_never()
    {
        var admin = await app.SignedIn();
        Assert.Equal(30, (await admin.GetFromJsonAsync<JsonElement>("/api/settings/signin")).GetProperty("idle").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/settings/signin", new { idle = 7 })).StatusCode);

        // Never: three hours of nothing.
        (await admin.PostAsJsonAsync("/api/settings/signin", new { idle = 0 })).EnsureSuccessStatusCode();
        await Polling(admin, 180);

        // 15 minutes, counted from the last click.
        (await admin.PostAsync("/api/sessions/active", null)).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/settings/signin", new { idle = 15 })).EnsureSuccessStatusCode();
        await Polling(admin, 16);
        app.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/status")).StatusCode);

        var log = await Log();
        Assert.Contains("Disabled Sign out when idle", log);
        Assert.Contains("Set Sign out when idle to 15 minutes", log);
        Assert.Contains("Signed out after 15 minutes of inactivity", log);

        // Only someone who can change roles changes it.
        app.Services.GetRequiredService<Accounts>().Add("mlopez", "user password", Roles.User);
        var user = app.Page();
        (await user.PostAsJsonAsync("/api/signin", new { username = "mlopez", password = "user password" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/settings/signin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/settings/signin", new { idle = 0 })).StatusCode);
    }

    [Fact]
    public async Task The_page_signing_itself_out_says_why()
    {
        var page = await app.SignedIn();
        (await page.PostAsync("/api/signout?idle=true", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await page.GetAsync("/api/status")).StatusCode);
        Assert.Contains("Signed out after 30 minutes of inactivity", await Log());
    }
}
