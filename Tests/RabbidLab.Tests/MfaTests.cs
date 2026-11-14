using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>MFA: a code from an authenticator app after the password, set up in Settings or at sign-in when a role requires it.</summary>
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

    /// <summary>Turns MFA on for whoever the page is signed in as, and gives the secret their app holds.</summary>
    private async Task<string> TurnOn(HttpClient page)
    {
        var secret = (await Json(await page.PostAsJsonAsync("/api/mfa/setup", new { }))).GetProperty("secret").GetString()!;
        (await page.PostAsJsonAsync("/api/mfa/on", new { code = Code(secret) })).EnsureSuccessStatusCode();
        return secret;
    }

    private Task<HttpResponseMessage> Password(HttpClient page, string username, string password = "user password") =>
        page.PostAsJsonAsync("/api/signin", new { username, password });

    private static async Task<bool> SignedIn(HttpClient page) => (await Json(await page.GetAsync("/api/signin"))).GetProperty("user").ValueKind == JsonValueKind.Object;

    /// <summary>A role with these permissions and a local account in it that has signed in. Roles made later rank lower.</summary>
    private async Task<(string Role, HttpClient Page)> InRole(HttpClient admin, string role, string account, params string[] permissions)
    {
        var id = (await Json(await admin.PostAsJsonAsync("/api/roles", new { name = role, color = "#5FD4A0" }))).GetProperty("id").GetString()!;
        (await admin.PostAsJsonAsync($"/api/roles/{id}", new { name = role, color = "#5FD4A0", permissions })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = account, password = "user password", roles = new[] { id } })).EnsureSuccessStatusCode();
        var page = app.Page();
        (await Password(page, account)).EnsureSuccessStatusCode();
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

    [Fact]
    public async Task Once_it_s_on_signing_in_takes_the_password_then_a_code_and_a_code_works_once()
    {
        var admin = await app.SignedIn();
        Assert.Equal(JsonValueKind.Null, (await Json(await admin.GetAsync("/api/mfa"))).GetProperty("since").ValueKind);
        var secret = await TurnOn(admin);
        Assert.Equal(JsonValueKind.String, (await Json(await admin.GetAsync("/api/mfa"))).GetProperty("since").ValueKind);
        Assert.DoesNotContain(secret, File.ReadAllText(Path.Combine(data, "mfa.json"))); // kept encrypted

        var page = app.Page();
        var first = await Json(await Password(page, "admin", "correct horse"));
        var ticket = first.GetProperty("ticket").GetString();
        Assert.Equal("code", first.GetProperty("mfa").GetString());
        Assert.False(first.TryGetProperty("user", out _));
        Assert.False(first.TryGetProperty("setup", out _));
        Assert.False(await SignedIn(page));

        // The code that turned it on was taken then; a wrong one and no ticket get nowhere either.
        Assert.Equal(HttpStatusCode.Unauthorized, (await page.PostAsJsonAsync("/api/signin/mfa", new { ticket, code = Code(secret) })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await page.PostAsJsonAsync("/api/signin/mfa", new { ticket, code = Wrong(secret) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await page.PostAsJsonAsync("/api/signin/mfa", new { ticket = "nope", code = Code(secret) })).StatusCode);
        Assert.False(await SignedIn(page));

        app.Clock.Advance(TimeSpan.FromSeconds(30));
        var code = Code(secret);
        var second = await page.PostAsJsonAsync("/api/signin/mfa", new { ticket, code });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("admin", (await Json(second)).GetProperty("user").GetProperty("username").GetString());
        Assert.True(await SignedIn(page));

        // The ticket is spent, and so is the code: another browser with the password and that same code stays out.
        Assert.Equal(HttpStatusCode.BadRequest, (await page.PostAsJsonAsync("/api/signin/mfa", new { ticket, code })).StatusCode);
        var other = app.Page();
        var again = (await Json(await Password(other, "admin", "correct horse"))).GetProperty("ticket").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsJsonAsync("/api/signin/mfa", new { ticket = again, code })).StatusCode);

        // A ticket is good for ten minutes.
        app.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/signin/mfa", new { ticket = again, code = Code(secret) })).StatusCode);
    }

    [Fact]
    public async Task A_role_that_requires_it_ends_sessions_without_a_code_and_sets_it_up_at_the_next_sign_in()
    {
        var admin = await app.SignedIn();
        var (desk, mlopez) = await InRole(admin, "Help desk", "mlopez", "seats.view");
        Assert.Equal(HttpStatusCode.OK, (await mlopez.GetAsync("/api/clients")).StatusCode);

        (await admin.PostAsJsonAsync($"/api/roles/{desk}/mfa", new { on = true })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await mlopez.GetAsync("/api/clients")).StatusCode);
        var first = await Json(await Password(mlopez, "mlopez"));
        var ticket = first.GetProperty("ticket").GetString();
        var secret = first.GetProperty("setup").GetProperty("secret").GetString()!;
        Assert.Equal("setup", first.GetProperty("mfa").GetString());
        Assert.Equal("mlopez", first.GetProperty("setup").GetProperty("account").GetString());
        Assert.StartsWith("<svg", first.GetProperty("setup").GetProperty("qr").GetString());
        Assert.False(await SignedIn(mlopez));
        Assert.Equal(HttpStatusCode.Unauthorized, (await mlopez.PostAsJsonAsync("/api/signin/mfa", new { ticket, code = Wrong(secret) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mlopez.PostAsJsonAsync("/api/signin/mfa", new { ticket, code = Code(secret) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mlopez.GetAsync("/api/clients")).StatusCode);
        Assert.Equal(["Help desk"], (await Json(await mlopez.GetAsync("/api/mfa"))).GetProperty("requiredBy").EnumerateArray().Select(r => r.GetString()));

        // A ticket to set up with, got with the password before the account had MFA, can't replace the app set up since.
        var late = await Json(await Password(app.Page(), "admin", "correct horse"));
        Assert.True(late.TryGetProperty("user", out _)); // admin's role doesn't require it yet
        var (strict, kiosk) = await InRole(admin, "Kiosk", "kiosk1", "seats.view");
        (await admin.PostAsJsonAsync($"/api/roles/{strict}/mfa", new { on = true })).EnsureSuccessStatusCode();
        var early = await Json(await Password(kiosk, "kiosk1"));
        var real = await Json(await Password(app.Page(), "kiosk1"));
        var realSecret = real.GetProperty("setup").GetProperty("secret").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await app.Page().PostAsJsonAsync("/api/signin/mfa", new { ticket = real.GetProperty("ticket").GetString(), code = Code(realSecret) })).StatusCode);
        var stolen = await kiosk.PostAsJsonAsync("/api/signin/mfa", new { ticket = early.GetProperty("ticket").GetString(), code = Code(early.GetProperty("setup").GetProperty("secret").GetString()!) });
        Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
        Assert.False(await SignedIn(kiosk));

        // From then on it's the code, and the roles page shows who has it.
        app.Clock.Advance(TimeSpan.FromSeconds(30));
        var next = app.Page();
        var asked = await Json(await Password(next, "mlopez"));
        Assert.Equal("code", asked.GetProperty("mfa").GetString());
        Assert.Equal(HttpStatusCode.OK, (await next.PostAsJsonAsync("/api/signin/mfa", new { ticket = asked.GetProperty("ticket").GetString(), code = Code(secret) })).StatusCode);
        var roles = await Json(await admin.GetAsync("/api/roles"));
        Assert.True(roles.GetProperty("roles").EnumerateArray().First(r => r.GetProperty("id").GetString() == desk).GetProperty("mfa").GetBoolean());
        Assert.Equal(JsonValueKind.String, roles.GetProperty("people").EnumerateArray().First(p => p.GetProperty("username").GetString() == "mlopez").GetProperty("mfa").ValueKind);
        Assert.Equal(JsonValueKind.Null, roles.GetProperty("people").EnumerateArray().First(p => p.GetProperty("username").GetString() == "admin").GetProperty("mfa").ValueKind);

        // Only someone above a role sets it, and Administrator's can be set too: the administrator who did is asked as well.
        Assert.Equal(HttpStatusCode.Forbidden, (await next.PostAsJsonAsync($"/api/roles/{desk}/mfa", new { on = false })).StatusCode);
        (await admin.PostAsJsonAsync("/api/roles/admin/mfa", new { on = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/roles")).StatusCode);
        Assert.Equal("setup", (await Json(await Password(admin, "admin", "correct horse"))).GetProperty("mfa").GetString());
    }

    [Fact]
    public async Task Turning_it_off_takes_a_code_and_five_wrong_codes_wait()
    {
        var admin = await app.SignedIn();
        var secret = await TurnOn(admin);
        app.Clock.Advance(TimeSpan.FromSeconds(30));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/mfa/off", new { code = Wrong(secret) })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await admin.PostAsJsonAsync("/api/mfa/off", new { code = Code(secret) })).StatusCode);
        Assert.Equal(JsonValueKind.String, (await Json(await admin.GetAsync("/api/mfa"))).GetProperty("since").ValueKind);

        // The session ran out while waiting: back in with one code, and the next one turns it off.
        app.Clock.Advance(TimeSpan.FromMinutes(11));
        var back = (await Json(await Password(admin, "admin", "correct horse"))).GetProperty("ticket").GetString();
        (await admin.PostAsJsonAsync("/api/signin/mfa", new { ticket = back, code = Code(secret) })).EnsureSuccessStatusCode();
        app.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/mfa/off", new { code = Code(secret) })).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Json(await admin.GetAsync("/api/mfa"))).GetProperty("since").ValueKind);
        var page = app.Page();
        Assert.True((await Json(await Password(page, "admin", "correct horse"))).TryGetProperty("user", out _));

        // Setting it up again from Settings: the first code has to be the new secret's.
        var again = (await Json(await admin.PostAsJsonAsync("/api/mfa/setup", new { }))).GetProperty("secret").GetString()!;
        Assert.NotEqual(secret, again);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/mfa/on", new { code = Wrong(again) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/mfa/on", new { code = Code(again) })).StatusCode);
        // With it on, a new app can't take the old one's place without a code from the old one.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/mfa/on", new { code = Code(again) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/mfa/setup", new { })).StatusCode);
    }

    [Fact]
    public async Task Reset_MFA_clears_it_for_people_below_and_the_command_line_for_anyone()
    {
        var admin = await app.SignedIn();
        var (_, lead) = await InRole(admin, "Lead", "lead1", "access.view", "access.mfa");
        var (_, desk) = await InRole(admin, "Help desk", "desk1", "seats.view");
        await TurnOn(admin);
        await TurnOn(desk);
        object Whose(string username) => new { source = "local", username };

        var above = await lead.PostAsJsonAsync("/api/mfa/reset", Whose("admin"));
        var notAllowed = await desk.PostAsJsonAsync("/api/mfa/reset", Whose("lead1"));
        var none = await lead.PostAsJsonAsync("/api/mfa/reset", Whose("lead1"));
        var below = await lead.PostAsJsonAsync("/api/mfa/reset", Whose("desk1"));
        var own = await admin.PostAsJsonAsync("/api/mfa/reset", Whose("admin")); // that's Turn off, which takes a code
        Assert.Equal(HttpStatusCode.BadRequest, own.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, above.StatusCode);
        Assert.Contains("Only someone above Administrator can reset their MFA", (await Json(above)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, notAllowed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.OK, below.StatusCode);
        Assert.True((await Json(await Password(app.Page(), "desk1"))).TryGetProperty("user", out _));
        Assert.Equal("code", (await Json(await Password(app.Page(), "admin", "correct horse"))).GetProperty("mfa").GetString());

        // --reset-mfa does this for an account nobody else can reset.
        Assert.Equal(1, app.Services.GetRequiredService<Mfa>().RemoveAll("ADMIN"));
        Assert.True((await Json(await Password(app.Page(), "admin", "correct horse"))).TryGetProperty("user", out _));
    }
}
