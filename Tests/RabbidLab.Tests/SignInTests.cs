using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
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
    public async Task Keep_me_signed_in_lasts_for_days()
    {
        await app.SignedIn();
        var page = app.Page();
        (await page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "correct horse", remember = true })).EnsureSuccessStatusCode();

        app.Clock.Advance(TimeSpan.FromDays(6));

        Assert.Equal(HttpStatusCode.OK, (await page.GetAsync("/api/status")).StatusCode);
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
        var together = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "wrong " + i })));

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
        var admin = await app.SignedIn();
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
        await app.Page().PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" });
        app.Services.GetRequiredService<Accounts>().AddAdmin("rescue", "rescue pass");
        var domain = app.Page();
        var windows = app.Page();
        var local = app.Page();
        (await domain.PostAsJsonAsync("/api/signin", new { username = "awiles", password = "domain pass", remember = true })).EnsureSuccessStatusCode();
        (await windows.PostAsJsonAsync("/api/signin", new { username = @".\localadmin", password = "windows pass", remember = true })).EnsureSuccessStatusCode();
        (await local.PostAsJsonAsync("/api/signin", new { username = @".\rescue", password = "rescue pass", remember = true })).EnsureSuccessStatusCode();

        foreach (var _ in Enumerable.Range(0, 3))
        {
            app.Clock.Advance(TimeSpan.FromDays(2));
            Assert.Equal(HttpStatusCode.OK, (await domain.GetAsync("/api/status")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await windows.GetAsync("/api/status")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await local.GetAsync("/api/status")).StatusCode);
        }
        app.Clock.Advance(TimeSpan.FromDays(2)); // eight days since they signed in

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
