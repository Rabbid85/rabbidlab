using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// Wrong passwords (SignInLocks.cs): each makes the name's next try wait longer, the tenth locks it, and only an
/// administrator unlocks it. A test's page is a stranger unless it has signed in before; the clock is the tests' own.
/// </summary>
public sealed class SignInLockTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-locks-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public SignInLockTests()
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

    private static Task<HttpResponseMessage> Try(HttpClient page, string username, string password, string? code = null) =>
        page.PostAsJsonAsync("/api/signin", new { username, password, code });

    // Read as text: an answer is looked at more than once here, and its stream only reads once.
    private static async Task<JsonElement> Said(HttpResponseMessage answer) => JsonDocument.Parse(await answer.Content.ReadAsStringAsync()).RootElement;

    private static async Task<int> Wait(HttpResponseMessage answer) => (await Said(answer)).TryGetProperty("wait", out var wait) ? wait.GetInt32() : 0;

    private static async Task<bool> Locked(HttpResponseMessage answer) => (await Said(answer)).TryGetProperty("locked", out var locked) && locked.GetBoolean();

    /// <summary>So many wrong passwords for a name, each sent once the wait the one before it set is over. The last answer.</summary>
    private async Task<HttpResponseMessage> Wrong(HttpClient page, string username, int times)
    {
        HttpResponseMessage last = null!;
        for (var i = 0; i < times; i++)
        {
            last = await Try(page, username, "wrong " + i);
            app.Clock.Advance(TimeSpan.FromSeconds(await Wait(last) + 1));
        }
        return last;
    }

    private SignInLocks Locks => app.Services.GetRequiredService<SignInLocks>();

    private string File => System.IO.File.ReadAllText(Path.Combine(data, "signin-locks.json"));

    [Fact]
    public async Task Each_wrong_password_doubles_the_wait_and_a_try_during_it_is_refused_without_a_look_at_the_password()
    {
        await app.SignedIn();
        var page = app.Page();

        var first = await Try(page, "admin", "wrong");
        var during = await Try(page, "admin", "correct horse");
        app.Clock.Advance(TimeSpan.FromSeconds(3));
        var second = await Try(page, "admin", "wrong");
        app.Clock.Advance(TimeSpan.FromSeconds(5));
        var right = await Try(page, "admin", "correct horse");
        var after = await Try(app.Page(), "admin", "wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(2, await Wait(first));
        Assert.Equal(HttpStatusCode.TooManyRequests, during.StatusCode); // the right password, and still no
        Assert.InRange(await Wait(during), 1, 2);
        Assert.Equal(4, await Wait(second));
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal(2, await Wait(after)); // a sign-in that went through starts the count over
        Assert.Equal([2, 4, 8, 16, 32, 64, 128, 256, 300, 300], Enumerable.Range(1, 10).Select(SignInLocks.WaitAfter));
    }

    [Fact]
    public async Task The_tenth_wrong_password_locks_the_name_until_an_administrator_unlocks_it_and_a_made_up_name_answers_the_same()
    {
        await app.SignedIn();
        app.Services.GetRequiredService<Accounts>().Add("mlopez", "user password", Roles.User);
        var page = app.Page();

        var ninth = await Wrong(page, "mlopez", 9);
        var tenth = await Wrong(page, "mlopez", 1);
        var right = await Try(page, "mlopez", "user password");
        var madeUp = await Wrong(page, "Hunter2-My-Password", 10);
        var madeUpAgain = await Try(page, "Hunter2-My-Password", "anything");

        Assert.False(await Locked(ninth));
        Assert.Equal(HttpStatusCode.Unauthorized, tenth.StatusCode);
        Assert.True(await Locked(tenth));
        Assert.Equal(HttpStatusCode.Forbidden, right.StatusCode); // the right password, and nothing is looked at
        Assert.True(await Locked(right));
        // A name that isn't an account locks the same way, so the answer never says which names exist.
        Assert.Equal(tenth.StatusCode, madeUp.StatusCode);
        Assert.True(await Locked(madeUp));
        Assert.Equal(right.StatusCode, madeUpAgain.StatusCode);
        Assert.Equal((await Said(right)).GetProperty("error").GetString()!.Replace("mlopez", "X"), (await Said(madeUpAgain)).GetProperty("error").GetString()!.Replace("Hunter2-My-Password", "X"));

        // Settings lists the account, and only counts the other: what was typed there may be a password.
        var admin = await app.SignedIn(); // signed in now: the waits above took longer than a session lasts
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/settings/signin/locks");
        Assert.Equal(["mlopez"], list.GetProperty("locked").EnumerateArray().Select(l => l.GetProperty("name").GetString()));
        Assert.Equal(1, list.GetProperty("others").GetInt32());
        Assert.DoesNotContain("hunter2", File, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Hunter2", System.IO.File.ReadAllText(Path.Combine(data, "activity.jsonl")));

        // Only someone who may change roles unlocks, from the console's own page.
        var key = list.GetProperty("locked")[0].GetProperty("key").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Page().PostAsJsonAsync("/api/settings/signin/locks/unlock", new { key })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/settings/signin/locks/unlock", new { key })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/settings/signin/locks/unlock", new { key })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Try(page, "mlopez", "user password")).StatusCode);
        var log = System.IO.File.ReadAllText(Path.Combine(data, "activity.jsonl"));
        Assert.Contains("signin.locked", log);
        Assert.Contains("Unlocked sign-in for mlopez", log);
    }

    [Fact]
    public async Task Unlocking_takes_the_permission_to_unlock_sign_ins()
    {
        await app.SignedIn();
        app.Services.GetRequiredService<Accounts>().Add("mlopez", "user password", Roles.User);
        await Wrong(app.Page(), "admin", 10);
        var user = app.Page();
        (await user.PostAsJsonAsync("/api/signin", new { username = "mlopez", password = "user password" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/settings/signin/locks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/settings/signin/locks/unlock", new { key = "local:admin" })).StatusCode);
        Assert.Equal(LockState.Locked, Locks.Check("local:admin").State);
    }

    [Fact]
    public async Task Wrong_codes_count_toward_the_lock_and_a_right_password_alone_starts_nothing_over()
    {
        await app.SignedIn();
        var page = app.RawPage();
        await Wrong(page, "admin", 9);

        var password = await Try(page, "admin", "correct horse");
        var ticket = (await Said(password)).GetProperty("ticket").GetString();
        var code = await page.PostAsJsonAsync("/api/signin/mfa", new { ticket, code = "000000" });
        var again = await page.PostAsJsonAsync("/api/signin/mfa", new { ticket, code = app.MfaCode("admin") });

        Assert.Equal(HttpStatusCode.OK, password.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, code.StatusCode); // the tenth wrong one, a code this time
        Assert.True((await Said(code)).GetProperty("again").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode); // its ticket went with the lock
        Assert.Equal(HttpStatusCode.Forbidden, (await Try(app.RawPage(), "admin", "correct horse")).StatusCode);
    }

    [Fact]
    public async Task A_locked_administrator_gets_back_in_with_the_password_and_the_code_together_and_nobody_else_does()
    {
        await app.SignedIn();
        app.Services.GetRequiredService<Accounts>().Add("mlopez", "user password", Roles.User);
        (await app.Page().PostAsJsonAsync("/api/signin", new { username = "mlopez", password = "user password" })).EnsureSuccessStatusCode(); // sets its app up
        var page = app.RawPage();
        await Wrong(page, "admin", 10);
        await Wrong(page, "mlopez", 10);

        var alone = await Try(page, "admin", "correct horse");
        var badCode = await Try(page, "admin", "correct horse", "000000");
        var tooSoon = await Try(page, "admin", "correct horse", app.MfaCode("admin"));
        app.Clock.Advance(TimeSpan.FromSeconds(SignInLocks.LongestWait + 1));
        var badPassword = await Try(page, "admin", "wrong", app.MfaCode("admin"));
        app.Clock.Advance(TimeSpan.FromSeconds(SignInLocks.LongestWait + 1));
        var notAdmin = await Try(page, "mlopez", "user password", app.MfaCode("mlopez"));
        var both = await Try(page, "admin", "correct horse", app.MfaCode("admin"));

        Assert.Equal(HttpStatusCode.Forbidden, alone.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, badCode.StatusCode);
        Assert.Equal("The username, password or code is incorrect.", (await Said(badCode)).GetProperty("error").GetString());
        Assert.Equal(SignInLocks.LongestWait, await Wait(badCode)); // five minutes to the next try
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, badPassword.StatusCode);
        Assert.Equal((await Said(badCode)).GetProperty("error").GetString(), (await Said(badPassword)).GetProperty("error").GetString()); // never which of the two
        Assert.Equal(HttpStatusCode.Unauthorized, notAdmin.StatusCode); // both right, and still not an administrator
        Assert.Equal(HttpStatusCode.OK, both.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await page.GetAsync("/api/status")).StatusCode); // signed in, the code already given
        Assert.Equal(LockState.Open, Locks.Check("local:admin").State);
        Assert.Equal(LockState.Locked, Locks.Check("local:mlopez").State);
        Assert.Contains("Unlocked their own sign-in", System.IO.File.ReadAllText(Path.Combine(data, "activity.jsonl")));
    }

    [Fact]
    public async Task A_domain_account_locks_here_one_short_of_where_the_domain_locks_it_and_no_password_goes_to_the_domain_after()
    {
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });
        app.Directory.Threshold = 5;
        var page = app.Page();

        var third = await Wrong(page, "jreyes", 3);
        var fourth = await Wrong(page, @"AMW\JReyes", 1); // another spelling of the same name
        var asked = app.Directory.SignIns;
        var right = await Try(page, "jreyes@amw.com", "user pass");

        Assert.False(await Locked(third));
        Assert.True(await Locked(fourth));
        Assert.Equal(HttpStatusCode.Forbidden, right.StatusCode);
        Assert.Equal(asked, app.Directory.SignIns); // nothing was sent to the domain controller
    }

    [Fact]
    public async Task A_browser_that_has_signed_in_as_the_name_keeps_its_own_count_and_is_not_locked_by_anyone_else()
    {
        await app.SignedIn();
        var mine = app.Page();
        (await Try(mine, "admin", "correct horse")).EnsureSuccessStatusCode(); // this browser has signed in as admin
        await Wrong(app.Page(), "admin", 10); // a stranger locks the name

        var stranger = await Try(app.Page(), "admin", "correct horse");
        var known = await Try(mine, "admin", "correct horse");
        var other = await Try(mine, "mlopez", "anything"); // its cookie is for admin only

        Assert.Equal(HttpStatusCode.Forbidden, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(2, await Wait(other));
        Assert.Equal(LockState.Locked, Locks.Check("local:admin").State); // still locked for everyone else
    }

    [Fact]
    public void The_server_itself_never_locks_and_a_remembered_browser_gets_ten_tries_of_its_own()
    {
        var locks = Locks;
        const string key = "local:admin";
        var here = new Asker(Here: true);
        for (var i = 0; i < 25; i++)
        {
            Assert.NotEqual(LockState.Locked, locks.Wrong(key, "::1", here, known: true).State);
        }
        Assert.Equal(SignInLocks.LongestWait, locks.Check(key, here).Seconds); // slowed all the same
        Assert.Equal(LockState.Open, locks.Check(key).State); // and nobody else's count moved

        var token = locks.Remember(null, key);
        var browser = new Asker(locks.Browser(token, key));
        Assert.NotNull(browser.Browser);
        Assert.Equal(token, locks.Remember(token, key)); // the same browser keeps its token
        Assert.Null(locks.Browser(token, "local:mlopez"));
        Assert.Null(locks.Browser("made-up-token", key));
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(LockState.Wait, locks.Wrong(key, "10.0.0.5", browser, known: true).State);
        }
        Assert.Equal(LockState.Open, locks.Check(key).State);
        locks.Wrong(key, "10.0.0.5", browser, known: true); // its tenth: a stranger from here on
        Assert.Null(locks.Browser(token, key));
        Assert.True(locks.Check(key).Seconds > 0); // and that one counted as a stranger's
        var old = locks.Remember(null, "local:old");
        app.Clock.Advance(TimeSpan.FromDays(SignInLocks.BrowserDays + 1));
        Assert.Null(locks.Browser(old, "local:old")); // a browser not seen for 180 days is forgotten
    }

    [Fact]
    public void Locks_survive_a_restart_an_unlock_from_outside_is_seen_and_counts_drop_after_a_day()
    {
        var locks = Locks;
        locks.Wrong("local:mlopez", "10.0.0.5", known: true);
        Assert.Equal(4, locks.Wrong("local:mlopez", "10.0.0.5", known: true).Seconds);
        app.Clock.Advance(TimeSpan.FromHours(25));
        Assert.Equal(LockState.Open, locks.Check("local:mlopez").State);
        Assert.Equal(2, locks.Wrong("local:mlopez", "10.0.0.5", known: true).Seconds); // a day on, the first again
        for (var i = 0; i < 10; i++)
        {
            locks.Wrong("local:admin", "10.0.0.5", known: true);
        }

        // Rabbid Lab started again: the same file, and the same lock.
        var folder = app.Services.GetRequiredService<DataFolder>();
        using (var again = new SignInLocks(folder, app.Services.GetRequiredService<SettingsStore>(), app.Clock))
        {
            Assert.Equal(LockState.Locked, again.Check("local:admin").State);
        }
        // --unlock is another process, and changes the file as it is: the running one sees that.
        Assert.Equal(0, SignInLocks.UnlockInFile(folder, ["local:mlopez", "ad:mlopez"]));
        Assert.Equal(1, SignInLocks.UnlockInFile(folder, ["local:admin", "ad:admin"]));
        Assert.Equal(LockState.Open, locks.Check("local:admin").State);
    }

    [Fact]
    public async Task Tries_for_one_name_sent_at_the_same_moment_are_checked_one_at_a_time()
    {
        await app.SignedIn();
        var page = app.Page();

        var together = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Try(page, "admin", i == 9 ? "correct horse" : "wrong " + i)));
        app.Clock.Advance(TimeSpan.FromSeconds(3));
        var next = await Try(page, "admin", "wrong again");

        // One was checked (whichever came first) and the rest were told to wait, so the count moved by one, not ten.
        Assert.Equal(1, together.Count(r => r.StatusCode != HttpStatusCode.TooManyRequests));
        Assert.Equal(9, together.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.InRange(await Wait(next), 2, 4);
    }

    [Fact]
    public async Task Another_spelling_of_a_locked_account_cannot_be_used_to_test_passwords()
    {
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });
        app.Directory.Upns["jordan.reyes"] = "jreyes"; // signs in as jreyes, but isn't spelled like it
        var page = app.Page();
        await Wrong(page, "jreyes", 10);

        var right = await Try(page, "jordan.reyes@amw.com", "user pass");
        app.Clock.Advance(TimeSpan.FromSeconds(await Wait(right) + 1));
        var wrong = await Try(page, "jordan.reyes@amw.com", "not it");

        Assert.Equal(HttpStatusCode.Unauthorized, right.StatusCode); // right, and answered like a wrong one
        Assert.Equal(wrong.StatusCode, right.StatusCode);
        Assert.Equal((await Said(wrong)).GetProperty("error").GetString(), (await Said(right)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_name_with_another_domain_in_it_typed_as_a_local_account_never_goes_to_Windows()
    {
        await app.SignedIn();
        app.Windows.Rechecked.Clear();

        var answer = await app.Page().PostAsJsonAsync("/api/signin", new { username = @"AMW\localadmin", password = "windows pass", local = true });

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode); // localadmin's right password, but not this computer's name in front
        Assert.DoesNotContain(app.Windows.Rechecked, n => n.Contains("localadmin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Names_that_are_spelled_differently_but_mean_the_same_account_share_one_count()
    {
        Assert.Equal("ad:awiles", SignInLocks.Key("ad", @"AMW\Awiles"));
        Assert.Equal("ad:awiles", SignInLocks.Key("ad", "awiles@amw.com "));
        Assert.Equal("ad:awiles", SignInLocks.Key("ad", "awilés")); // accents, which the directory looks past
        Assert.Equal("ad:awiles", SignInLocks.Key("ad", "ａｗｉｌｅｓ")); // wide letters
        Assert.Equal("local:admin", SignInLocks.Key("local", @".\Admin"));
    }

    [Fact]
    public void Unlocking_with_a_right_code_and_a_wrong_password_five_times_makes_it_a_day_between_tries()
    {
        var locks = Locks;
        for (var i = 0; i < 10; i++)
        {
            locks.Wrong("local:admin", "10.0.0.5", known: true);
        }

        Assert.Equal(SignInLocks.LongestWait, locks.Wrong("local:admin", "10.0.0.5", known: true).Seconds); // a wrong code: five minutes, however many
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(SignInLocks.LongestWait, locks.Wrong("local:admin", "10.0.0.5", known: true, heavy: true).Seconds);
        }
        Assert.Equal(86400, locks.Wrong("local:admin", "10.0.0.5", known: true, heavy: true).Seconds);
        Assert.Equal(86400, locks.Check("local:admin").Seconds);
    }

    [Fact]
    public void Made_up_names_cannot_fill_the_file_and_an_account_is_never_dropped_for_them()
    {
        var locks = Locks;
        locks.Wrong("local:mlopez", "10.0.0.5", known: true);
        for (var i = 0; i < 5200; i++)
        {
            locks.Wrong("local:made-up-" + i, "10.0.0.5");
        }
        for (var i = 0; i < 10; i++)
        {
            locks.Wrong("local:admin", "10.0.0.5", known: true); // the lock writes the file
        }

        var names = JsonDocument.Parse(File).RootElement.GetProperty("names");
        Assert.InRange(names.GetArrayLength(), 4900, 5002);
        Assert.Equal(4, locks.Wrong("local:mlopez", "10.0.0.5", known: true).Seconds); // still counted: its second
        Assert.DoesNotContain("made-up", File);
    }

    [Fact]
    public async Task An_account_that_locks_is_told_about_under_the_failing_sign_ins_alert_and_a_made_up_name_is_not()
    {
        var admin = await app.SignedIn();
        var me = await TestApp.Notification(admin);
        (await admin.PostAsJsonAsync("/api/alerts/rules/signin.failing", new { on = true, limit = 10, to = new[] { me } })).EnsureSuccessStatusCode();
        var watch = app.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<AlertWatch>().Single();

        await Wrong(app.Page(), "admin", 10);
        await Wrong(app.Page(), "made-up-name", 10);
        await watch.Round(CancellationToken.None);

        Assert.Equal(["admin is locked out after too many failed sign-ins"], app.Mail.Sent.Select(m => m.Subject));
    }

    [Fact]
    public void An_IPv6_computer_cannot_get_fresh_tries_by_changing_its_address()
    {
        var limiter = new Limiter(2, new FakeClock(), byAddress: true);

        Assert.True(limiter.Take("2001:db8:1:2::1"));
        Assert.True(limiter.Take("2001:db8:1:2:aaaa:bbbb:cccc:5"));
        Assert.False(limiter.Take("2001:db8:1:2:ffff::9")); // the same /64
        Assert.True(limiter.Take("2001:db8:1:3::1")); // the network next door
        Assert.True(limiter.Take("192.168.2.5") && limiter.Take("192.168.2.5"));
        Assert.False(limiter.Take("192.168.2.5"));
        Assert.True(limiter.Take("192.168.2.6")); // IPv4 stays one address each
        limiter.GiveBack("2001:db8:1:2::77");
        Assert.True(limiter.Take("2001:db8:1:2::1"));
    }
}
