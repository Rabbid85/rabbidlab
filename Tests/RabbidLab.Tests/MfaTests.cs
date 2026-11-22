using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// MFA: a code from an authenticator app after the password, required for everyone. Set up at the first sign-in (or when
/// setting Rabbid Lab up); a reset leaves a one-time code that setting it up again takes, Datto's way.
/// </summary>
public sealed class MfaTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-mfa-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public MfaTests()
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

    /// <summary>The code the app would show now.</summary>
    private string Code(string secret) => Totp.Code(secret, Totp.Step(app.Clock.GetUtcNow()));

    /// <summary>A code the app wouldn't show now, nor just before or after.</summary>
    private string Wrong(string secret)
    {
        var step = Totp.Step(app.Clock.GetUtcNow());
        var right = new[] { step - 1, step, step + 1 }.Select(s => Totp.Code(secret, s)).ToHashSet();
        return Enumerable.Range(0, 10).Select(i => i.ToString("D6")).First(c => !right.Contains(c));
    }

    private Task<HttpResponseMessage> Password(HttpClient page, string username, string password = "user password") =>
        page.PostAsJsonAsync("/api/signin", new { username, password });

    private static async Task<bool> SignedIn(HttpClient page) => (await Json(await page.GetAsync("/api/signin"))).GetProperty("user").ValueKind == JsonValueKind.Object;

    /// <summary>A right code at an account's first sign-in: the Use at your own risk notice comes next, with no session yet, and is accepted.</summary>
    private static async Task Noticed(HttpClient page, HttpResponseMessage coded)
    {
        Assert.Equal(HttpStatusCode.OK, coded.StatusCode);
        var said = await Json(coded);
        Assert.False(await SignedIn(page));
        (await page.PostAsJsonAsync("/api/signin/notice", new { ticket = said.GetProperty("ticket").GetString(), accept = true })).EnsureSuccessStatusCode();
    }

    /// <summary>A role with these permissions and a local account in it that has signed in (and so set MFA up). Roles made later rank lower.</summary>
    private async Task<(string Role, HttpClient Page)> InRole(HttpClient admin, string role, string account, params string[] permissions)
    {
        var id = (await Json(await admin.PostAsJsonAsync("/api/roles", new { name = role, color = "#5FD4A0" }))).GetProperty("id").GetString()!;
        (await admin.PostAsJsonAsync($"/api/roles/{id}", new { name = role, color = "#5FD4A0", permissions })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = account, password = "user password", roles = new[] { id } })).EnsureSuccessStatusCode();
        var page = app.Page();
        (await Password(page, account)).EnsureSuccessStatusCode();
        await TestApp.Confirm(page, "user password");
        return (id, page);
    }

    [Fact]
    public void The_codes_are_the_ones_every_authenticator_app_shows()
    {
        // RFC 6238, appendix B: the SHA-1 secret, and the last six digits of its codes.
        var secret = Totp.Encode(Encoding.ASCII.GetBytes("12345678901234567890"));
        string At(long seconds) => Totp.Code(secret, Totp.Step(DateTimeOffset.FromUnixTimeSeconds(seconds)));
        var now = DateTimeOffset.FromUnixTimeSeconds(1111111109);

        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", secret);
        Assert.Equal("12345678901234567890", Encoding.ASCII.GetString(Totp.Decode("gezd gnbv gy3t qojq gezd gnbv gy3t qojq")));
        Assert.Equal(["287082", "081804", "005924", "279037"], new[] { At(59), At(1111111109), At(1234567890), At(2000000000) });
        Assert.Equal(Totp.Step(now), Totp.Match(secret, "081 804", now));
        Assert.Equal(Totp.Step(now) - 1, Totp.Match(secret, Totp.Code(secret, Totp.Step(now) - 1), now));
        Assert.Equal(Totp.Step(now) + 1, Totp.Match(secret, Totp.Code(secret, Totp.Step(now) + 1), now));
        Assert.Null(Totp.Match(secret, Totp.Code(secret, Totp.Step(now) + 2), now));
        Assert.Null(Totp.Match(secret, "81804", now));
        Assert.Null(Totp.Match(secret, null, now));
        Assert.Equal(32, Totp.NewSecret().Length);
        Assert.StartsWith("otpauth://totp/Rabbid%20Lab:awiles?secret=ABC&issuer=Rabbid%20Lab", Totp.Uri("Rabbid Lab", "awiles", "ABC"));
        Assert.StartsWith("<svg", Totp.Qr(Totp.Uri("Rabbid Lab", "awiles", secret)));
    }

    [Theory]
    [InlineData(true, true, "127.0.0.1", "127.0.0.1", true)]
    [InlineData(true, true, "::1", "::1", true)]
    [InlineData(true, true, "::ffff:192.168.2.50", "192.168.2.50", true)] // a browser on the server, by the server's own name
    [InlineData(true, true, "192.168.2.40", "192.168.2.50", false)] // another computer
    [InlineData(true, false, "127.0.0.1", "127.0.0.1", false)] // switched off in Settings, Developer
    [InlineData(false, true, "127.0.0.1", "127.0.0.1", false)] // the release never skips
    public void Skipping_MFA_is_Rabbid_Lab_Dev_only_switched_on_and_on_the_server_itself(bool dev, bool allowed, string from, string to, bool skips) =>
        Assert.Equal(skips, RabbidLab.Server.SignIn.SkipsMfa(dev, allowed, System.Net.IPAddress.Parse(from), System.Net.IPAddress.Parse(to)));

    [Fact]
    public async Task The_release_never_offers_or_takes_a_skip()
    {
        await app.SignedIn();
        var page = app.RawPage();
        var first = await Json(await Password(page, "admin", "correct horse"));
        Assert.False(first.GetProperty("skip").GetBoolean());
        var skipped = await page.PostAsJsonAsync("/api/signin/mfa", new { ticket = first.GetProperty("ticket").GetString(), skip = true });
        Assert.Equal(HttpStatusCode.Forbidden, skipped.StatusCode);
        Assert.False(await SignedIn(page));
    }

    [Fact]
    public async Task Setting_Rabbid_Lab_up_asks_for_MFA_before_anyone_is_signed_in()
    {
        var page = app.RawPage();
        var first = await Json(await page.PostAsJsonAsync("/api/setup/local", new { username = "admin", password = "correct horse" }));
        var secret = first.GetProperty("setup").GetProperty("secret").GetString()!;

        Assert.Equal("setup", first.GetProperty("mfa").GetString());
        Assert.False(first.GetProperty("reset").GetBoolean());
        Assert.False(first.TryGetProperty("user", out _));
        Assert.False(await SignedIn(page));
        await Noticed(page, await page.PostAsJsonAsync("/api/signin/mfa", new { ticket = first.GetProperty("ticket").GetString(), code = Code(secret) }));
        Assert.True(await SignedIn(page));
        Assert.Equal(1, (await Json(await page.GetAsync("/api/mfa"))).GetProperty("admins").GetInt32());
    }

    [Fact]
    public async Task Signing_in_takes_the_password_then_a_code_and_a_code_works_once()
    {
        var admin = await app.SignedIn();
        var secret = app.MfaSecrets["admin"];
        Assert.Equal(JsonValueKind.String, (await Json(await admin.GetAsync("/api/mfa"))).GetProperty("since").ValueKind);
        Assert.DoesNotContain(secret, File.ReadAllText(Path.Combine(data, "mfa.json"))); // kept encrypted

        var page = app.RawPage();
        var first = await Json(await Password(page, "admin", "correct horse"));
        var ticket = first.GetProperty("ticket").GetString();
        Assert.Equal("code", first.GetProperty("mfa").GetString());
        Assert.False(first.TryGetProperty("user", out _));
        Assert.False(first.TryGetProperty("setup", out _));
        Assert.False(await SignedIn(page));

        // The code the first sign-in typed was taken then; a wrong one and no ticket get nowhere either.
        Assert.Equal(HttpStatusCode.Unauthorized, (await page.PostAsJsonAsync("/api/signin/mfa", new { ticket, code = Code(secret) })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await page.PostAsJsonAsync("/api/signin/mfa", new { ticket, code = Wrong(secret) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await page.PostAsJsonAsync("/api/signin/mfa", new { ticket = "nope", code = Code(secret) })).StatusCode);
        Assert.False(await SignedIn(page));

        app.Clock.Advance(TimeSpan.FromSeconds(60)); // past the codes the first sign-in and its Confirm it's you took
        var code = Code(secret);
        var second = await page.PostAsJsonAsync("/api/signin/mfa", new { ticket, code });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("admin", (await Json(second)).GetProperty("user").GetProperty("username").GetString());
        Assert.True(await SignedIn(page));

        // The ticket is spent, and so is the code: another browser with the password and that same code stays out.
        Assert.Equal(HttpStatusCode.BadRequest, (await page.PostAsJsonAsync("/api/signin/mfa", new { ticket, code })).StatusCode);
        var other = app.RawPage();
        var again = (await Json(await Password(other, "admin", "correct horse"))).GetProperty("ticket").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsJsonAsync("/api/signin/mfa", new { ticket = again, code })).StatusCode);

        // A ticket is good for ten minutes.
        app.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/signin/mfa", new { ticket = again, code = Code(secret) })).StatusCode);
    }

    [Fact]
    public async Task Everyone_sets_it_up_at_their_first_sign_in_and_roles_have_no_switch_for_it()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "mlopez", password = "user password", roles = Array.Empty<string>() })).EnsureSuccessStatusCode();

        var mlopez = app.RawPage();
        var first = await Json(await Password(mlopez, "mlopez"));
        var ticket = first.GetProperty("ticket").GetString();
        var secret = first.GetProperty("setup").GetProperty("secret").GetString()!;
        Assert.Equal("setup", first.GetProperty("mfa").GetString());
        Assert.Equal("mlopez", first.GetProperty("setup").GetProperty("account").GetString());
        Assert.StartsWith("<svg", first.GetProperty("setup").GetProperty("qr").GetString());
        Assert.False(await SignedIn(mlopez));
        Assert.Equal(HttpStatusCode.Unauthorized, (await mlopez.PostAsJsonAsync("/api/signin/mfa", new { ticket, code = Wrong(secret) })).StatusCode);
        await Noticed(mlopez, await mlopez.PostAsJsonAsync("/api/signin/mfa", new { ticket, code = Code(secret) }));
        Assert.True(await SignedIn(mlopez));
        Assert.Equal(JsonValueKind.Null, (await Json(await mlopez.GetAsync("/api/mfa"))).GetProperty("admins").ValueKind); // not theirs to see

        // A ticket to set up with, got with the password before the account had MFA, can't replace the app set up since.
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "kiosk1", password = "user password", roles = Array.Empty<string>() })).EnsureSuccessStatusCode();
        var kiosk = app.RawPage();
        var early = await Json(await Password(kiosk, "kiosk1"));
        var real = await Json(await Password(app.RawPage(), "kiosk1"));
        var realSecret = real.GetProperty("setup").GetProperty("secret").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await app.RawPage().PostAsJsonAsync("/api/signin/mfa", new { ticket = real.GetProperty("ticket").GetString(), code = Code(realSecret) })).StatusCode);
        var stolen = await kiosk.PostAsJsonAsync("/api/signin/mfa", new { ticket = early.GetProperty("ticket").GetString(), code = Code(early.GetProperty("setup").GetProperty("secret").GetString()!) });
        Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
        Assert.False(await SignedIn(kiosk));

        // From then on it's the code, and the roles page shows who has it.
        app.Clock.Advance(TimeSpan.FromSeconds(30));
        var next = app.RawPage();
        var asked = await Json(await Password(next, "mlopez"));
        Assert.Equal("code", asked.GetProperty("mfa").GetString());
        Assert.Equal(HttpStatusCode.OK, (await next.PostAsJsonAsync("/api/signin/mfa", new { ticket = asked.GetProperty("ticket").GetString(), code = Code(secret) })).StatusCode);
        var roles = await Json(await admin.GetAsync("/api/roles"));
        Assert.Equal(JsonValueKind.String, roles.GetProperty("people").EnumerateArray().First(p => p.GetProperty("username").GetString() == "mlopez").GetProperty("mfa").ValueKind);

        // There's no role switch to turn it off.
        var off = await admin.PostAsJsonAsync("/api/roles/admin/mfa", new { on = false });
        Assert.Equal(HttpStatusCode.BadRequest, off.StatusCode);
        Assert.Contains("required for everyone", (await Json(off)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/roles")).StatusCode);
    }

    [Fact]
    public async Task Moving_to_a_new_phone_takes_a_code_and_five_wrong_codes_wait()
    {
        var admin = await app.SignedIn();
        var secret = app.MfaSecrets["admin"];
        app.Clock.Advance(TimeSpan.FromSeconds(30));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/mfa/off", new { code = Wrong(secret) })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await admin.PostAsJsonAsync("/api/mfa/off", new { code = Code(secret) })).StatusCode);
        Assert.Equal(JsonValueKind.String, (await Json(await admin.GetAsync("/api/mfa"))).GetProperty("since").ValueKind);

        // Once the wait is over, a code from the old app takes it off this session's account...
        app.Clock.Advance(TimeSpan.FromMinutes(11));
        (await Password(admin, "admin", "correct horse")).EnsureSuccessStatusCode();
        app.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/mfa/off", new { code = Code(secret) })).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Json(await admin.GetAsync("/api/mfa"))).GetProperty("since").ValueKind);
        // ...and a new sign-in has to set one up again: it's required.
        Assert.Equal("setup", (await Json(await Password(app.RawPage(), "admin", "correct horse"))).GetProperty("mfa").GetString());

        // The new phone, from Settings: the first code has to be the new secret's.
        var again = (await Json(await admin.PostAsJsonAsync("/api/mfa/setup", new { }))).GetProperty("secret").GetString()!;
        Assert.NotEqual(secret, again);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/mfa/on", new { code = Wrong(again) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/mfa/on", new { code = Code(again) })).StatusCode);
        // With it on, a new app can't take the old one's place without a code from the old one.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/mfa/on", new { code = Code(again) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/mfa/setup", new { })).StatusCode);
    }

    [Fact]
    public async Task A_reset_gives_a_one_time_code_that_setting_it_up_again_takes()
    {
        var admin = await app.SignedIn();
        var (_, lead) = await InRole(admin, "Lead", "lead1", "access.view", "access.mfa");
        var (_, desk) = await InRole(admin, "Help desk", "desk1", "seats.view");
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "fresh1", password = "user password", roles = Array.Empty<string>() })).EnsureSuccessStatusCode();
        object Whose(string username) => new { source = "local", username };

        var above = await lead.PostAsJsonAsync("/api/mfa/reset", Whose("admin"));
        var notAllowed = await desk.PostAsJsonAsync("/api/mfa/reset", Whose("lead1"));
        var none = await lead.PostAsJsonAsync("/api/mfa/reset", Whose("fresh1")); // never signed in, so never set it up
        var own = await admin.PostAsJsonAsync("/api/mfa/reset", Whose("admin")); // that's Move to a new phone, which takes a code
        var below = await lead.PostAsJsonAsync("/api/mfa/reset", Whose("desk1"));

        Assert.Equal(HttpStatusCode.Forbidden, above.StatusCode);
        Assert.Contains("Only admins in a role above Administrator can reset their MFA", (await Json(above)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, notAllowed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, own.StatusCode);
        Assert.Equal(HttpStatusCode.OK, below.StatusCode);
        var given = await Json(below);
        var oneTime = given.GetProperty("code").GetString()!;
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", oneTime);
        Assert.Equal(24, given.GetProperty("hours").GetInt32());
        Assert.DoesNotContain(oneTime.Replace("-", ""), File.ReadAllText(Path.Combine(data, "mfa.json"))); // only its hash

        // desk1's old app no longer works; setting up a new one takes the password, the one-time code and the new app's code.
        var page = app.RawPage();
        var first = await Json(await Password(page, "desk1"));
        var ticket = first.GetProperty("ticket").GetString();
        var secret = first.GetProperty("setup").GetProperty("secret").GetString()!;
        Assert.Equal("setup", first.GetProperty("mfa").GetString());
        Assert.True(first.GetProperty("reset").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await page.PostAsJsonAsync("/api/signin/mfa", new { ticket, code = Code(secret) })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await page.PostAsJsonAsync("/api/signin/mfa", new { ticket, code = Code(secret), reset = "AAAA-BBBB-CCCC" })).StatusCode);
        Assert.False(await SignedIn(page));
        var ok = await page.PostAsJsonAsync("/api/signin/mfa", new { ticket, code = Code(secret), reset = oneTime.ToLowerInvariant().Replace("-", " ") });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True(await SignedIn(page));

        // The code is used up: from now on it's just the new app's code.
        app.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal("code", (await Json(await Password(app.RawPage(), "desk1"))).GetProperty("mfa").GetString());
        Assert.Equal("code", (await Json(await Password(app.RawPage(), "admin", "correct horse"))).GetProperty("mfa").GetString());

        // --reset-mfa does this for an account nobody else can reset: it takes the same kind of one-time code.
        var mfa = app.Services.GetRequiredService<Mfa>();
        Assert.Single(mfa.WithName("ADMIN"));
        mfa.Reset("local", "admin");
        var afterCommand = await Json(await Password(app.RawPage(), "admin", "correct horse"));
        Assert.Equal("setup", afterCommand.GetProperty("mfa").GetString());
        Assert.True(afterCommand.GetProperty("reset").GetBoolean());
    }
}
