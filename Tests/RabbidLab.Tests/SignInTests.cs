using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;
using RabbidLab.Updater;

namespace RabbidLab.Tests;

/// <summary>Signing in: setup on the first visit, then nothing but sign-in until someone has.</summary>
public sealed class SignInTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-signin-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public SignInTests()
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
    public async Task Every_page_leads_to_the_sign_in_page_and_the_api_wants_a_sign_in()
    {
        var page = app.Page();

        var home = await page.GetAsync("/");
        var status = await page.GetAsync("/api/status");
        var health = await page.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.Equal("/signin.html", home.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await page.GetAsync("/signin.html")).StatusCode);
    }

    [Fact]
    public async Task The_sign_in_page_sees_where_updating_stands_and_nothing_more()
    {
        var page = app.Page();

        var watch = await page.GetAsync("/api/update/watch?opened=true");
        using var said = JsonDocument.Parse(await watch.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, watch.StatusCode);
        Assert.True(said.RootElement.TryGetProperty("status", out _));
        Assert.False(said.RootElement.TryGetProperty("message", out _)); // why an update failed stays behind sign-in
        Assert.Equal(HttpStatusCode.Unauthorized, (await page.GetAsync("/api/update")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await page.PostAsync("/api/update/check", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await page.PostAsync("/api/update/install", null)).StatusCode);
    }

    [Fact]
    public async Task Local_setup_signs_in_and_only_happens_once()
    {
        var page = app.Page();

        var setup = await page.PostAsJsonAsync("/api/setup/local", new { username = "admin", password = "correct horse" });
        var status = await page.GetAsync("/api/status");
        var again = await app.Page().PostAsJsonAsync("/api/setup/local", new { username = "other", password = "another pass" });
        var home = await app.Page().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("/signin.html", home.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task The_password_is_stored_only_as_a_hash()
    {
        await app.SignedIn("admin", "correct horse");

        var file = File.ReadAllText(Path.Combine(data, "accounts.json"));

        Assert.DoesNotContain("correct horse", file);
        Assert.Contains("pbkdf2-sha256", file);
    }

    [Fact]
    public async Task A_short_password_is_refused()
    {
        var setup = await app.Page().PostAsJsonAsync("/api/setup/local", new { username = "admin", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, setup.StatusCode);
        Assert.Contains("8 characters", await setup.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sign_in_takes_the_right_password_only()
    {
        await app.SignedIn("admin", "correct horse");

        var wrong = await app.Page().PostAsJsonAsync("/api/signin", new { username = "admin", password = "wrong one" });
        app.Clock.Advance(TimeSpan.FromSeconds(3)); // a wrong password makes the name's next try wait
        var page = app.Page();
        var right = await page.PostAsJsonAsync("/api/signin", new { username = "ADMIN", password = "correct horse" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await page.GetAsync("/api/status")).StatusCode);
    }

    [Fact]
    public async Task Without_keep_me_signed_in_the_session_ends_minutes_after_the_page_stops_checking_in()
    {
        var page = await app.SignedIn();

        // An open console checks in at least once a minute, so it stays signed in.
        foreach (var _ in Enumerable.Range(0, 10))
        {
            app.Clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(HttpStatusCode.OK, (await page.GetAsync("/api/status")).StatusCode);
        }
        // Closed, or the browser kept the cookie: six minutes without a check-in ends it.
        app.Clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(HttpStatusCode.Unauthorized, (await page.GetAsync("/api/status")).StatusCode);
    }

    [Fact]
    public async Task Asking_to_be_kept_signed_in_is_ignored_and_the_session_ends_with_the_browser()
    {
        await app.SignedIn();
        app.Services.GetRequiredService<SettingsStore>().Change(s => s.IdleMinutes = 0); // Sign out when idle off
        var page = app.Page();
        var signedIn = await page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "correct horse", remember = true });
        signedIn.EnsureSuccessStatusCode();

        // A cookie for this browser session only: no expiry date, so it goes when the browser closes.
        Assert.DoesNotContain(signedIn.Headers.GetValues("Set-Cookie"), c => c.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        app.Clock.Advance(TimeSpan.FromMinutes(6)); // no page open
        Assert.Equal(HttpStatusCode.Unauthorized, (await page.GetAsync("/api/status")).StatusCode);
    }

    /// <summary>A request from another address, as Kestrel would give it: plain HTTP, with the headers given.</summary>
    private async Task<(int Status, string Body, string? Cookie)> From(string address, string method, string path, object? body = null,
        Dictionary<string, string>? headers = null, string? cookie = null)
    {
        var c = await app.Server.SendAsync(c =>
        {
            c.Request.Method = method;
            c.Request.Path = path;
            c.Request.Scheme = "http";
            c.Connection.RemoteIpAddress = IPAddress.Parse(address);
            c.Request.Headers["X-Rabbid-Lab"] = "1";
            foreach (var (k, v) in headers ?? [])
            {
                c.Request.Headers[k] = v;
            }
            if (cookie is not null)
            {
                c.Request.Headers.Cookie = cookie;
            }
            if (body is not null)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                c.Request.ContentType = "application/json";
                c.Request.ContentLength = bytes.Length;
                c.Request.Body = new MemoryStream(bytes);
                c.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>(new HasBody());
            }
        });
        var said = await new StreamReader(c.Response.Body).ReadToEndAsync();
        var set = c.Response.Headers.SetCookie.FirstOrDefault(v => v?.StartsWith(RabbidLab.Server.SignIn.Cookie + "=", StringComparison.Ordinal) == true);
        return (c.Response.StatusCode, said, set?.Split(';')[0]);
    }

    // TestServer's SendAsync otherwise takes a request as one without a body.
    private sealed class HasBody : Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    [Fact]
    public async Task Over_plain_HTTP_from_another_computer_nobody_signs_in_unless_HTTPS_came_through_a_trusted_proxy()
    {
        await app.SignedIn();
        var me = new { username = "admin", password = "correct horse", local = true };

        var plain = await From("192.168.2.40", "POST", "/api/signin", me);
        var page = await From("192.168.2.40", "GET", "/api/signin");
        var here = await From("127.0.0.1", "POST", "/api/signin", me); // on the server itself: nothing crosses the network
        var proxied = await From("127.0.0.1", "POST", "/api/signin", me, new() { ["X-Forwarded-Proto"] = "https", ["X-Forwarded-For"] = "192.168.2.40" });
        var claimed = await From("192.168.2.50", "POST", "/api/signin", me, new() { ["X-Forwarded-Proto"] = "https" }); // a proxy nobody trusts

        Assert.Equal(403, plain.Status);
        Assert.Contains("plain HTTP", plain.Body);
        Assert.Null(plain.Cookie);
        Assert.Contains("\"plain\":\"refused\"", page.Body);
        Assert.True(here.Status == 200, here.Body);
        Assert.Equal(200, proxied.Status);
        Assert.Equal(403, claimed.Status);
        Assert.Equal(403, (await From("192.168.2.40", "POST", "/api/setup/local", new { username = "x", password = "a long password" })).Status);
        // The password step on the server itself, then its MFA code: that's the session.
        var ticket = JsonDocument.Parse(here.Body).RootElement.GetProperty("ticket").GetString();
        here = await From("127.0.0.1", "POST", "/api/signin/mfa", new { ticket, code = app.MfaCode("admin") });
        Assert.True(here.Status == 200, here.Body);

        // A session taken over to plain HTTP from another computer ends, here too.
        Assert.Equal(200, (await From("127.0.0.1", "GET", "/api/status", cookie: here.Cookie)).Status);
        Assert.Equal(401, (await From("192.168.2.40", "GET", "/api/status", cookie: here.Cookie)).Status);
        Assert.Equal(401, (await From("127.0.0.1", "GET", "/api/status", cookie: here.Cookie)).Status);
    }

    [Theory]
    [InlineData("192.168.2.40", false, false, true, "refused")]
    [InlineData("192.168.2.40", false, true, false, "allowed")] // AllowPlainHttp: let through, and the page warns
    [InlineData("192.168.2.40", true, false, false, null)]
    [InlineData("127.0.0.1", false, false, false, null)]
    [InlineData("::1", false, false, false, null)]
    [InlineData("::ffff:127.0.0.1", false, false, false, null)]
    public void Plain_HTTP_is_from_another_computer_without_HTTPS(string address, bool https, bool allowed, bool exposed, string? said)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AllowPlainHttp"] = allowed ? "true" : null }).Build();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config).BuildServiceProvider(),
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        context.Request.Scheme = https ? "https" : "http";

        Assert.Equal(exposed, PlainHttp.Exposed(context));
        Assert.Equal(said, PlainHttp.Said(context));
    }

    [Fact]
    public async Task Closing_the_last_console_tab_ends_the_session_and_a_reload_or_another_tab_keeps_it()
    {
        var admin = await app.SignedIn();
        app.Services.GetRequiredService<SettingsStore>().Change(s => s.IdleMinutes = 0);
        async Task Tab(string id, bool gone) => (await admin.PostAsJsonAsync("/api/sessions/tab", new { id, gone })).EnsureSuccessStatusCode();
        await Tab("a1", false);

        // A reload: the page goes, and the new one says it's open a moment later.
        await Tab("a1", true);
        app.Clock.Advance(TimeSpan.FromSeconds(1));
        await Tab("a2", false);
        app.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/status")).StatusCode);

        // Two tabs: one closes, the other keeps the session going.
        await Tab("b1", false);
        await Tab("a2", true);
        app.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/status")).StatusCode);

        // The last tab closes: a few seconds later the session is over, and reopening means signing in.
        await Tab("b1", true);
        app.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/status")).StatusCode);
        Assert.Empty(app.Services.GetRequiredService<Sessions>().All().Where(s => s.Username == "admin" && s.LastSeen > app.Clock.GetUtcNow().AddSeconds(-30)));
    }

    [Fact]
    public async Task A_failed_sign_in_is_logged_by_name_only_when_the_name_is_an_account_here()
    {
        await app.SignedIn();
        var page = app.Page();
        await page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "wrong" });
        await page.PostAsJsonAsync("/api/signin", new { username = "Hunter2-My-Password!", password = "" }); // the password in the username box
        await page.PostAsJsonAsync("/api/signin", new { username = @".\localadmin", password = "wrong" }); // an administrator of this computer

        var log = File.ReadAllText(Path.Combine(data, "activity.jsonl"));
        Assert.DoesNotContain("Hunter2", log);
        var failed = log.Split('\n').Where(l => l.Contains("signin.failed")).ToList();
        Assert.Equal(3, failed.Count);
        Assert.Contains("\"admin\"", failed[0]);
        Assert.Contains(RabbidLab.Server.SignIn.Unknown, failed[1]);
        Assert.Contains("localadmin", failed[2]);
    }

    [Fact]
    public async Task A_wrong_password_asks_Windows_about_the_name_whether_or_not_it_is_an_account_here()
    {
        await app.SignedIn();
        var page = app.Page();
        app.Windows.Rechecked.Clear();

        await page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "wrong" });
        await page.PostAsJsonAsync("/api/signin", new { username = "nobody", password = "wrong" });

        // The same steps for both, so how long the answer takes doesn't say which names are accounts.
        Assert.Equal(["admin", "nobody"], app.Windows.Rechecked);
    }

    [Fact]
    public async Task A_failed_domain_sign_in_is_logged_by_name_only_for_an_account_that_has_signed_in_here()
    {
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });
        var page = app.Page();
        await page.PostAsJsonAsync("/api/signin", new { username = "awiles", password = "wrong" });
        await page.PostAsJsonAsync("/api/signin", new { username = "jreyes", password = "wrong" }); // in the domain, never signed in here
        await page.PostAsJsonAsync("/api/signin", new { username = @"AMW\Hunter2-My-Password!", password = "x" });

        var failed = File.ReadAllText(Path.Combine(data, "activity.jsonl")).Split('\n').Where(l => l.Contains("signin.failed")).ToList();
        Assert.Equal(3, failed.Count);
        Assert.Contains("awiles", failed[0]);
        Assert.Contains(RabbidLab.Server.SignIn.Unknown, failed[1]);
        Assert.DoesNotContain("Hunter2", failed[2]);
    }

    [Fact]
    public async Task An_MFA_secret_being_set_up_is_kept_encrypted_until_its_first_code()
    {
        var admin = await app.SignedIn();
        // Moving to a new phone: off with the old app's code, then the new secret is a draft until its first code.
        (await admin.PostAsJsonAsync("/api/mfa/off", new { code = app.MfaCode("admin") })).EnsureSuccessStatusCode();
        var secret = (await (await admin.PostAsJsonAsync("/api/mfa/setup", new { })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;

        // What the server holds for the draft isn't the secret.
        var mfa = app.Services.GetRequiredService<Mfa>();
        var drafts = (System.Collections.IDictionary) typeof(Mfa).GetField("drafts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(mfa)!;
        var held = drafts.Values.Cast<(string Secret, DateTimeOffset Until)>().Single().Secret;
        Assert.NotEqual(secret, held);
        Assert.DoesNotContain(secret, held);

        // And it still turns on with a code from it.
        (await admin.PostAsJsonAsync("/api/mfa/on", new { code = Totp.Code(secret, Totp.Step(app.Clock.GetUtcNow())) })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_domain_account_that_is_disabled_reset_or_out_of_every_role_is_signed_out_within_minutes()
    {
        app.Services.GetRequiredService<SettingsStore>().Change(s => s.IdleMinutes = 0);
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });
        var admin = app.Page();
        (await admin.PostAsJsonAsync("/api/signin", new { username = "awiles", password = "domain pass" })).EnsureSuccessStatusCode();
        var user = app.Page();
        (await user.PostAsJsonAsync("/api/signin", new { username = "jreyes", password = "user pass" })).EnsureSuccessStatusCode();
        // A page left open asks every minute or so.
        async Task Open(int minutes)
        {
            for (var i = 0; i < minutes; i++)
            {
                app.Clock.Advance(TimeSpan.FromMinutes(1));
                await admin.GetAsync("/api/status");
                await user.GetAsync("/api/status");
            }
        }

        // The domain can't be asked: both go on as they were.
        app.Directory.Unreachable = true;
        await Open(6);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/status")).StatusCode);
        app.Directory.Unreachable = false;

        // jreyes is turned off in the domain; awiles's password is reset. Each is signed out at the next check.
        app.Directory.Disabled.Add("jreyes");
        app.Directory.PasswordSet["awiles"] = 200;
        await Open(6);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/status")).StatusCode);
        var log = File.ReadAllText(Path.Combine(data, "activity.jsonl"));
        Assert.Contains("The account was disabled.", log);
        Assert.Contains("password was changed or reset", log);

        // Signing in again with the new password works.
        var again = app.Page();
        (await again.PostAsJsonAsync("/api/signin", new { username = "awiles", password = "domain pass" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/api/status")).StatusCode);
    }

    [Fact]
    public async Task A_Windows_administrator_taken_out_of_Administrators_is_signed_out_within_minutes()
    {
        await app.SignedIn();
        app.Services.GetRequiredService<SettingsStore>().Change(s => s.IdleMinutes = 0);
        var page = app.Page();
        (await page.PostAsJsonAsync("/api/signin", new { username = @".\localadmin", password = "windows pass" })).EnsureSuccessStatusCode();

        app.Windows.Accounts.Single(a => a.Name == "localadmin").Groups = ["Users"];
        for (var i = 0; i < 6; i++)
        {
            app.Clock.Advance(TimeSpan.FromMinutes(1));
            await page.GetAsync("/api/status");
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await page.GetAsync("/api/status")).StatusCode);
    }

    [Theory]
    [InlineData("https://lab.amw.com/", true)]
    [InlineData("https://lab.amw.com:8420/", false)] // http://lab.amw.com would be sent to port 443, which may have nothing on it
    [InlineData("http://lab.amw.com/", false)]
    [InlineData("https://localhost/", false)]
    [InlineData("https://192.168.2.40/", false)]
    public async Task HSTS_is_sent_only_over_HTTPS_on_port_443_by_a_name(string address, bool sent)
    {
        var page = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri(address), AllowAutoRedirect = false });
        var answer = await page.GetAsync("/api/health");
        Assert.Equal(sent, answer.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task The_sign_in_page_starts_an_update_only_when_one_is_waiting_to_install_by_itself()
    {
        var page = app.Page();

        var watch = await (await page.GetAsync("/api/update/watch")).Content.ReadFromJsonAsync<JsonElement>();
        var nothingWaiting = await page.PostAsync("/api/update/watch/install", null);
        var elsewhere = await app.CreateClient().PostAsync("/api/update/watch/install", null);

        Assert.False(watch.GetProperty("installHere").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, nothingWaiting.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var page = await app.SignedIn();

        await page.PostAsync("/api/signout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await page.GetAsync("/api/status")).StatusCode);
    }

    [Fact]
    public async Task Signing_out_needs_the_page_header_so_another_site_cannot_do_it()
    {
        var page = await app.SignedIn();
        page.DefaultRequestHeaders.Remove("X-Rabbid-Lab");

        Assert.Equal(HttpStatusCode.Forbidden, (await page.PostAsync("/api/signout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await page.GetAsync("/api/status")).StatusCode);
    }

    [Fact]
    public async Task Setup_needs_the_page_header()
    {
        var bare = app.CreateClient();

        var setup = await bare.PostAsJsonAsync("/api/setup/local", new { username = "admin", password = "correct horse" });

        Assert.Equal(HttpStatusCode.Forbidden, setup.StatusCode);
    }

    [Fact]
    public async Task Ten_wrong_passwords_and_it_waits()
    {
        await app.SignedIn();
        var page = app.Page();

        for (var i = 0; i < 10; i++)
        {
            await page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "wrong " + i });
        }
        var next = await page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "correct horse" });

        Assert.Equal(HttpStatusCode.TooManyRequests, next.StatusCode);
    }

    [Fact]
    public async Task Sign_ins_that_work_use_up_none_of_the_tries_and_tries_at_the_same_moment_all_count()
    {
        await app.SignedIn();

        for (var i = 0; i < 12; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await app.Page().PostAsJsonAsync("/api/signin", new { username = "admin", password = "correct horse" })).StatusCode);
        }
        var page = app.Page();
        // Thirty names, since tries for one name are checked one at a time (SignInLockTests): this is the limit per address.
        var together = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => page.PostAsJsonAsync("/api/signin", new { username = "nobody" + i, password = "wrong " + i })));

        Assert.Equal(10, together.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
        Assert.Equal(20, together.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public void A_limiter_gives_tries_back_and_forgets_addresses_it_has_nothing_on()
    {
        var clock = new FakeClock();
        var limiter = new Limiter(3, clock);

        Assert.True(limiter.Take("a") && limiter.Take("a") && limiter.Take("a"));
        Assert.False(limiter.Take("a"));
        Assert.True(limiter.Take("b")); // each address has its own
        limiter.GiveBack("a");
        Assert.True(limiter.Take("a"));
        limiter.GiveBack("b");
        Assert.Equal(1, limiter.Tracked); // b gave back all it took
        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.True(limiter.Take("c")); // a's tries ran out, so it's forgotten
        Assert.Equal(1, limiter.Tracked);
        Assert.True(limiter.Take("a"));
    }

    [Fact]
    public async Task A_very_long_username_or_request_is_refused_and_not_written_down()
    {
        await app.SignedIn();
        var name = new string('a', 300);

        var longName = await app.Page().PostAsJsonAsync("/api/signin", new { username = name, password = "whatever it is" });
        var large = await app.Page().PostAsync("/api/signin",
            new StringContent("{\"username\":\"" + new string('b', 100_000) + "\",\"password\":\"x\"}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, longName.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        Assert.DoesNotContain(name, File.ReadAllText(Path.Combine(data, "activity.jsonl")));
    }

    [Fact]
    public async Task Only_a_signed_in_person_doing_something_holds_an_update_back()
    {
        var engine = app.Services.GetRequiredService<UpdateEngine>();
        var anyone = app.Page();

        // Anyone at all, such as an uptime monitor.
        await anyone.GetAsync("/api/health");
        await anyone.GetAsync("/api/signin");
        await anyone.GetAsync("/api/status");
        Assert.False(engine.InUseNow);

        // What an open page asks by itself.
        var admin = await app.SignedIn(confirmed: false);
        foreach (var poll in new[] { "/api/update", "/api/clients", "/api/sessions", "/api/links/states", "/api/clients/here", "/api/health", "/api/update/watch" })
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(poll)).StatusCode);
        }
        Assert.False(engine.InUseNow);

        await admin.GetAsync("/api/status");
        Assert.True(engine.InUseNow);
    }

    [Fact]
    public async Task The_sign_in_page_cannot_start_an_update_while_someone_is_working()
    {
        using var key = RSA.Create(2048);
        app.Services.GetRequiredService<UpdateOptions>().ReleasePublicKey = key.ExportSubjectPublicKeyInfoPem();
        var engine = app.Services.GetRequiredService<UpdateEngine>();
        var admin = await app.SignedIn();
        await admin.GetAsync("/api/status"); // someone is working
        var signature = Convert.ToBase64String(key.SignData(Release.Message("99.0.0", "ab12", 42, Product.Name), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        app.Updates.Offer = new UpdateOffer("99.0.0", 42, "ab12", "https://source.test/99.0.0", signature);
        for (var i = 0; i < 100 && (await engine.TryNow(CancellationToken.None)).Status != UpdateStatus.Available; i++)
        {
            await Task.Delay(20); // the engine's own look was under way
        }

        var watch = await (await app.Page().GetAsync("/api/update/watch")).Content.ReadFromJsonAsync<JsonElement>();
        var install = await app.Page().PostAsync("/api/update/watch/install", null);

        Assert.Equal(UpdateStatus.Available, engine.State.Status); // ready, and waiting for whoever is working
        Assert.Equal("99.0.0", watch.GetProperty("available").GetString());
        Assert.False(watch.GetProperty("installHere").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, install.StatusCode);
        Assert.Equal(UpdateStatus.Available, engine.State.Status);
    }

    [Fact]
    public async Task Active_Directory_and_Windows_sessions_end_a_week_after_signing_in_however_much_they_are_used()
    {
        app.Services.GetRequiredService<SettingsStore>().Change(s => s.IdleMinutes = 0); // Sign out when idle off: days untouched
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });
        app.Services.GetRequiredService<Accounts>().AddAdmin("rescue", "rescue pass");
        var domain = app.Page();
        var windows = app.Page();
        var local = app.Page();
        (await domain.PostAsJsonAsync("/api/signin", new { username = "awiles", password = "domain pass" })).EnsureSuccessStatusCode();
        (await windows.PostAsJsonAsync("/api/signin", new { username = @".\localadmin", password = "windows pass" })).EnsureSuccessStatusCode();
        (await local.PostAsJsonAsync("/api/signin", new { username = @".\rescue", password = "rescue pass" })).EnsureSuccessStatusCode();

        // Kept open and in use for a week: as if they had signed in eight days ago.
        foreach (var s in app.Services.GetRequiredService<Sessions>().All())
        {
            s.Started -= TimeSpan.FromDays(8);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await domain.GetAsync("/api/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await windows.GetAsync("/api/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await local.GetAsync("/api/status")).StatusCode); // Rabbid Lab's own account: its password is checked here
    }

    [Fact]
    public async Task An_unreadable_accounts_file_never_opens_setup_again()
    {
        await app.SignedIn();
        app.Dispose();
        File.WriteAllText(Path.Combine(data, "accounts.json"), "{ \"local\": [ {");
        File.Delete(Path.Combine(data, "accounts.json.bak"));

        using var again = new TestApp(data);
        var page = again.Page();
        var info = await (await page.GetAsync("/api/signin")).Content.ReadFromJsonAsync<JsonElement>();
        var setup = await page.PostAsJsonAsync("/api/setup/local", new { username = "intruder", password = "another pass" });
        var gone = await page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "correct horse" });
        // --add-admin, on the server itself, is the way back in.
        again.Services.GetRequiredService<Accounts>().AddAdmin("rescue", "rescue pass");
        again.Services.GetRequiredService<Roles>().Grant(Roles.Admin, new Member { Kind = Member.Local, Name = "rescue" });
        var rescued = await page.PostAsJsonAsync("/api/signin", new { username = "rescue", password = "rescue pass" });

        Assert.True(info.GetProperty("setUp").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, setup.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, gone.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rescued.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await page.GetAsync("/api/activity")).StatusCode); // an administrator
    }

    [Fact]
    public async Task Directory_setup_signs_in_whoever_set_it_up()
    {
        var page = app.Page();

        var setup = await page.PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = @"AMW\awiles", password = "domain pass" });

        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await page.GetAsync("/api/status")).StatusCode);
    }

    [Fact]
    public async Task Directory_users_sign_in_once_it_is_set_up()
    {
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });
        var page = app.Page();

        var wrong = await page.PostAsJsonAsync("/api/signin", new { username = "awiles", password = "nope" });
        app.Clock.Advance(TimeSpan.FromSeconds(3)); // a wrong password makes the name's next try wait
        var right = await page.PostAsJsonAsync("/api/signin", new { username = "awiles", password = "domain pass" });
        var me = await (await page.GetAsync("/api/signin")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal("Anthony Wiles", me.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal("amw.com", me.GetProperty("domain").GetString());
        Assert.Equal("amw.com", me.GetProperty("user").GetProperty("domain").GetString());
        Assert.Equal("dc01.amw.com", me.GetProperty("user").GetProperty("server").GetString()); // the controller that checked the password
    }

    [Fact]
    public async Task A_domain_amw_com_trusts_signs_in_and_any_other_is_refused()
    {
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });
        var page = app.Page();

        var domains = await (await page.GetAsync("/api/signin/domains")).Content.ReadFromJsonAsync<JsonElement>();
        var theirs = await page.GetAsync("/api/signin/controllers?domain=lab.amw.com");
        var stranger = await page.GetAsync("/api/signin/controllers?domain=evil.example");
        var refused = await page.PostAsJsonAsync("/api/signin", new { username = "awiles", password = "domain pass", domain = "evil.example" });
        var askedFor = app.Directory.LastDomain;
        var trusted = await page.PostAsJsonAsync("/api/signin", new { username = "awiles", password = "domain pass", domain = "lab.amw.com" });
        var me = await (await page.GetAsync("/api/signin")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(new[] { "amw.com", "lab.amw.com" }, domains.GetProperty("domains").EnumerateArray().Select(d => d.GetString()!));
        Assert.Equal(HttpStatusCode.OK, theirs.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("amw.com", askedFor); // the stranger's domain never saw the password
        Assert.Equal(HttpStatusCode.OK, trusted.StatusCode);
        Assert.Equal("lab.amw.com", app.Directory.LastDomain);
        Assert.Equal("awiles@lab.amw.com", me.GetProperty("user").GetProperty("username").GetString());
        Assert.Equal("lab.amw.com", me.GetProperty("user").GetProperty("domain").GetString());
        Assert.Equal("dc1.lab.amw.com", me.GetProperty("user").GetProperty("server").GetString());
    }

    [Fact]
    public async Task Directory_setup_without_a_group_lets_in_anyone_in_the_domain()
    {
        var setup = await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });

        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        Assert.Contains("\"group\": \"\"", File.ReadAllText(Path.Combine(data, "accounts.json")));
    }

    [Fact]
    public async Task Setup_offers_the_domains_and_their_controllers()
    {
        var page = app.Page();

        var domains = await (await page.GetAsync("/api/setup/directory/domains")).Content.ReadFromJsonAsync<JsonElement>();
        var controllers = await (await page.GetAsync("/api/setup/directory/controllers?domain=amw.com")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("amw.com", domains.GetProperty("domains")[0].GetString());
        Assert.Equal("dc01.amw.com", controllers.GetProperty("controllers")[0].GetProperty("name").GetString());
        Assert.True(controllers.GetProperty("controllers")[0].GetProperty("online").GetBoolean());
    }

    [Fact]
    public async Task Automatic_uses_the_quickest_controller_that_answers()
    {
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });

        Assert.Equal("dc01.amw.com", app.Directory.LastHost);
    }

    [Fact]
    public async Task A_chosen_controller_is_kept_and_used_to_sign_in()
    {
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", server = "dc02.amw.com", username = "awiles", password = "domain pass" });

        await app.Page().PostAsJsonAsync("/api/signin", new { username = "awiles", password = "domain pass" });

        Assert.Equal("dc02.amw.com", app.Directory.LastHost);
        Assert.Contains("dc02.amw.com", File.ReadAllText(Path.Combine(data, "accounts.json")));
    }

    [Fact]
    public async Task Sign_in_shows_the_domain_and_its_controllers_once_set_up()
    {
        var before = await app.Page().GetAsync("/api/signin/controllers");
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });

        var page = app.Page();
        var info = await (await page.GetAsync("/api/signin")).Content.ReadFromJsonAsync<JsonElement>();
        var controllers = await (await page.GetAsync("/api/signin/controllers")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        Assert.Equal("amw.com", info.GetProperty("domain").GetString());
        Assert.Equal(JsonValueKind.Null, info.GetProperty("server").ValueKind);
        Assert.Equal("dc01.amw.com", controllers.GetProperty("controllers")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Dot_backslash_signs_in_a_local_account_when_the_domain_is_set_up()
    {
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });
        app.Services.GetRequiredService<Accounts>().AddAdmin("rescue", "rescue pass");

        var local = await app.Page().PostAsJsonAsync("/api/signin", new { username = @".\rescue", password = "rescue pass" });
        var slash = await app.Page().PostAsJsonAsync("/api/signin", new { username = "./rescue", password = "rescue pass" });
        var plain = await app.Page().PostAsJsonAsync("/api/signin", new { username = "rescue", password = "rescue pass" });

        Assert.Equal(HttpStatusCode.OK, local.StatusCode);
        Assert.Equal(HttpStatusCode.OK, slash.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, plain.StatusCode); // a plain name is the domain's
    }

    [Fact]
    public async Task A_computer_administrator_signs_in_with_dot_backslash()
    {
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });
        var page = app.Page();

        var wrong = await app.Page().PostAsJsonAsync("/api/signin", new { username = @".\localadmin", password = "nope" });
        app.Clock.Advance(TimeSpan.FromSeconds(3)); // a wrong password makes the name's next try wait
        var right = await page.PostAsJsonAsync("/api/signin", new { username = @".\localadmin", password = "windows pass" });
        var me = await (await page.GetAsync("/api/signin")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Contains("wrong", await wrong.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal("windows", me.GetProperty("user").GetProperty("source").GetString());
        Assert.Equal(HttpStatusCode.OK, (await page.GetAsync("/api/status")).StatusCode);
    }

    [Fact]
    public async Task Sign_in_only_uses_a_domain_controller_dns_lists()
    {
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });

        var elsewhere = await app.Page().PostAsJsonAsync("/api/signin", new { username = "awiles", password = "domain pass", server = "ldap.example.com" });
        var listed = await app.Page().PostAsJsonAsync("/api/signin", new { username = "awiles", password = "domain pass", server = "dc02.amw.com" });

        Assert.Equal(HttpStatusCode.BadRequest, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal("dc02.amw.com", app.Directory.LastHost);
    }

    [Fact]
    public async Task An_unreachable_domain_says_why()
    {
        app.Directory.Problem = "amw.com wasn't found. Check the domain name.";

        var setup = await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });

        Assert.Equal(HttpStatusCode.BadRequest, setup.StatusCode);
        Assert.Contains("wasn't found", await setup.Content.ReadAsStringAsync());
    }
}
