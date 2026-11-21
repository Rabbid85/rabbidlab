using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// What stands in front of the sign-in besides the lock itself: the stopper for a script hammering it (SignInFlood), the
/// number of wrong passwords that locks a name (Settings, Sign-in), and forgetting an account's remembered browsers.
/// </summary>
public sealed class SignInGuardTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-guard-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public SignInGuardTests()
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

    [Fact]
    public void A_script_hammering_the_sign_in_is_turned_away_and_one_that_keeps_at_it_for_fifteen_minutes()
    {
        var clock = new FakeClock();
        var flood = new SignInFlood(clock);

        for (var i = 0; i < SignInFlood.PerMinute; i++)
        {
            Assert.True(flood.Let("10.0.0.5", out _));
        }
        Assert.False(flood.Let("10.0.0.5", out var first));
        Assert.False(first);
        Assert.True(flood.Let("10.0.0.6", out _)); // each address has its own count
        for (var i = 0; i < SignInFlood.PerMinute; i++)
        {
            Assert.True(flood.Let(i % 2 == 0 ? "2001:db8:1:2::1" : "2001:db8:1:2:aaaa::" + i, out _));
        }
        Assert.False(flood.Let("2001:db8:1:2:ffff::9", out _)); // an IPv6 computer can't change address for more
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.True(flood.Let("10.0.0.5", out _)); // a new minute

        var turnedAway = 0;
        for (var i = 0; i < SignInFlood.PerMinute * 10; i++)
        {
            flood.Let("10.0.0.9", out var justOut);
            turnedAway += justOut ? 1 : 0;
        }
        Assert.Equal(1, turnedAway); // said once, when it happens
        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.False(flood.Let("10.0.0.9", out _)); // a new minute, and still out
        Assert.True(flood.Let("10.0.0.5", out _)); // nobody else is
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(flood.Let("10.0.0.9", out _));
    }

    [Fact]
    public async Task Past_the_limit_a_sign_in_request_is_answered_before_anything_is_checked()
    {
        await app.SignedIn();
        HttpResponseMessage last = null!;
        // Without the page's own header a request is refused as ever, and isn't counted: another site can't use up a visitor's.
        var bare = app.CreateClient();
        for (var i = 0; i < SignInFlood.PerMinute + 5; i++)
        {
            last = await bare.PostAsJsonAsync("/api/signin", new { username = "", password = "" });
        }
        Assert.Equal(HttpStatusCode.Forbidden, last.StatusCode);

        var page = app.Page();
        for (var i = 0; i < SignInFlood.PerMinute; i++)
        {
            last = await page.PostAsJsonAsync("/api/signin", new { username = "", password = "" });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.Equal(60, (await last.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("wait").GetInt32());
        Assert.Equal(HttpStatusCode.TooManyRequests, (await page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "correct horse" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await page.GetAsync("/api/signin")).StatusCode); // the page itself still opens
        app.Clock.Advance(TimeSpan.FromMinutes(11)); // past the minute, and past the ten wrong tries that got through
        Assert.Equal(HttpStatusCode.OK, (await page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "correct horse" })).StatusCode);
    }

    [Fact]
    public async Task How_many_wrong_passwords_lock_a_name_is_a_setting_of_3_5_or_10()
    {
        var admin = await app.SignedIn();

        var odd = await admin.PostAsJsonAsync("/api/settings/signin", new { lockAfter = 4 });
        var three = await admin.PostAsJsonAsync("/api/settings/signin", new { lockAfter = 3 });
        var page = app.Page();
        var answers = new List<bool>();
        for (var i = 0; i < 3; i++)
        {
            var wrong = await (await page.PostAsJsonAsync("/api/signin", new { username = "mlopez", password = "wrong " + i })).Content.ReadFromJsonAsync<JsonElement>();
            answers.Add(wrong.GetProperty("locked").GetBoolean());
            app.Clock.Advance(TimeSpan.FromSeconds(wrong.GetProperty("wait").GetInt32() + 1));
        }
        var now = await admin.GetFromJsonAsync<JsonElement>("/api/settings/signin");

        Assert.Equal(HttpStatusCode.BadRequest, odd.StatusCode);
        Assert.Equal(HttpStatusCode.OK, three.StatusCode);
        Assert.Equal([false, false, true], answers);
        Assert.Equal(3, now.GetProperty("lockAfter").GetInt32());
        Assert.Equal(30, now.GetProperty("idle").GetInt32()); // the other setting on the page is left as it was
    }

    [Fact]
    public async Task Unlocking_is_a_permission_of_its_own_and_changing_roles_does_not_come_with_it()
    {
        var admin = await app.SignedIn();
        var parts = await admin.GetFromJsonAsync<JsonElement>("/api/roles");

        Assert.Contains(Permissions.AccessUnlock, parts.ToString());
        Assert.Contains(Permissions.AccessUnlock, Permissions.Parts.SelectMany(p => p.Actions).Select(a => a.Key));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/settings/signin/locks")).StatusCode); // an administrator may do everything
    }

    [Fact]
    public void A_changed_password_makes_strangers_of_the_browsers_remembered_for_the_account()
    {
        var locks = app.Services.GetRequiredService<SignInLocks>();
        var mine = locks.Remember(null, "local:admin");
        var theirs = locks.Remember(null, "local:mlopez");

        locks.ForgetBrowsers("local:admin");

        Assert.Null(locks.Browser(mine, "local:admin"));
        Assert.NotNull(locks.Browser(theirs, "local:mlopez"));
    }
}
