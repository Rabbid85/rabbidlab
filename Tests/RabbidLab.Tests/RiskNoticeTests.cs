using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// The Use at your own risk notice: every account accepts it before its first session starts. Declining starts none, so
/// the console never opens. It's asked once, and again only when its words change.
/// </summary>
public sealed class RiskNoticeTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-notice-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public RiskNoticeTests()
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

    private static async Task<bool> SignedIn(HttpClient page) => (await Json(await page.GetAsync("/api/signin"))).GetProperty("user").ValueKind == JsonValueKind.Object;

    /// <summary>A new account's password and MFA set up, as far as the answer after the code.</summary>
    private async Task<(HttpClient Page, JsonElement Said)> PastTheCode(HttpClient admin, string account)
    {
        (await admin.PostAsJsonAsync("/api/accounts", new { username = account, password = "user password", roles = Array.Empty<string>() })).EnsureSuccessStatusCode();
        var page = app.RawPage();
        var first = await Json(await page.PostAsJsonAsync("/api/signin", new { username = account, password = "user password" }));
        var secret = first.GetProperty("setup").GetProperty("secret").GetString()!;
        app.MfaSecrets[account] = secret;
        var coded = await page.PostAsJsonAsync("/api/signin/mfa", new { ticket = first.GetProperty("ticket").GetString(), code = app.MfaCode(account) });
        Assert.Equal(HttpStatusCode.OK, coded.StatusCode);
        return (page, await Json(coded));
    }

    [Fact]
    public void The_notice_is_read_from_notice_txt()
    {
        Assert.Equal("Use at your own risk", RiskNotice.Title);
        Assert.False(string.IsNullOrWhiteSpace(RiskNotice.Version));
        Assert.Equal(7, RiskNotice.Sections.Count);
        Assert.All(RiskNotice.Sections, s => Assert.True(s.Heading.Length > 0 && s.Text.Length > 40));
        Assert.Equal("No warranty", RiskNotice.Sections[1].Heading);
        Assert.Equal(16, RiskNotice.Wording.Length);

        var (title, version, sections) = RiskNotice.Parse("Title\r\nMay 2027\r\n\r\nFirst\r\nOne line\r\nwrapped.\r\n  \r\nSecond\nTwo.\n");
        Assert.Equal(("Title", "May 2027"), (title, version));
        Assert.Equal([new RiskNoticeSection("First", "One line wrapped."), new RiskNoticeSection("Second", "Two.")], sections);
    }

    [Fact]
    public async Task There_is_no_session_until_the_notice_is_accepted_and_it_is_asked_once()
    {
        var admin = await app.SignedIn();
        var (page, said) = await PastTheCode(admin, "mlopez");

        Assert.Equal("Use at your own risk", said.GetProperty("notice").GetProperty("title").GetString());
        Assert.Equal(7, said.GetProperty("notice").GetProperty("sections").GetArrayLength());
        Assert.False(said.TryGetProperty("user", out _));
        Assert.False(await SignedIn(page));
        Assert.Equal(HttpStatusCode.Unauthorized, (await page.GetAsync("/api/status")).StatusCode);

        var accepted = await Json(await page.PostAsJsonAsync("/api/signin/notice", new { ticket = said.GetProperty("ticket").GetString(), accept = true }));
        Assert.Equal("mlopez", accepted.GetProperty("user").GetProperty("username").GetString());
        Assert.True(await SignedIn(page));
        var log = await (await admin.GetAsync("/api/activity")).Content.ReadAsStringAsync();
        Assert.Contains("Accepted the Use at your own risk notice", log);

        // Next time: the password and the code, and straight in.
        var again = app.RawPage();
        var first = await Json(await again.PostAsJsonAsync("/api/signin", new { username = "mlopez", password = "user password" }));
        var after = await Json(await again.PostAsJsonAsync("/api/signin/mfa", new { ticket = first.GetProperty("ticket").GetString(), code = app.MfaCode("mlopez") }));
        Assert.False(after.TryGetProperty("notice", out _));
        Assert.True(await SignedIn(again));
    }

    [Fact]
    public async Task Accepted_on_the_sign_in_page_before_the_password_signs_straight_in()
    {
        var admin = await app.SignedIn();
        var shown = (await Json(await app.RawPage().GetAsync("/api/signin"))).GetProperty("notice");
        Assert.Equal(RiskNotice.Wording, shown.GetProperty("wording").GetString());
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "owenp", password = "user password", roles = Array.Empty<string>() })).EnsureSuccessStatusCode();

        var page = app.RawPage();
        var first = await Json(await page.PostAsJsonAsync("/api/signin", new { username = "owenp", password = "user password", notice = RiskNotice.Wording }));
        app.MfaSecrets["owenp"] = first.GetProperty("setup").GetProperty("secret").GetString()!;
        var after = await Json(await page.PostAsJsonAsync("/api/signin/mfa", new { ticket = first.GetProperty("ticket").GetString(), code = app.MfaCode("owenp"), notice = RiskNotice.Wording }));

        Assert.False(after.TryGetProperty("notice", out _));
        Assert.True(await SignedIn(page));
        Assert.Contains("on the sign-in page", await (await admin.GetAsync("/api/activity")).Content.ReadAsStringAsync());

        // Older words than the notice's own don't count: the notice comes after the password.
        var other = app.RawPage();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "samh", password = "user password", roles = Array.Empty<string>() })).EnsureSuccessStatusCode();
        var pw = await Json(await other.PostAsJsonAsync("/api/signin", new { username = "samh", password = "user password", notice = "0000000000000000" }));
        app.MfaSecrets["samh"] = pw.GetProperty("setup").GetProperty("secret").GetString()!;
        var stale = await Json(await other.PostAsJsonAsync("/api/signin/mfa", new { ticket = pw.GetProperty("ticket").GetString(), code = app.MfaCode("samh"), notice = "0000000000000000" }));
        Assert.True(stale.TryGetProperty("notice", out _));
        Assert.False(await SignedIn(other));
    }

    [Fact]
    public async Task Declining_opens_nothing_and_is_logged()
    {
        var admin = await app.SignedIn();
        var (page, said) = await PastTheCode(admin, "kiosk1");
        var ticket = said.GetProperty("ticket").GetString();

        var declined = await Json(await page.PostAsJsonAsync("/api/signin/notice", new { ticket, accept = false }));
        Assert.True(declined.GetProperty("declined").GetBoolean());
        Assert.False(await SignedIn(page));
        Assert.Equal(HttpStatusCode.Unauthorized, (await page.GetAsync("/api/status")).StatusCode);
        // The ticket is gone with it: accepting afterwards takes signing in again.
        Assert.Equal(HttpStatusCode.BadRequest, (await page.PostAsJsonAsync("/api/signin/notice", new { ticket, accept = true })).StatusCode);
        Assert.False(await SignedIn(page));
        Assert.Contains("Declined the Use at your own risk notice", await (await admin.GetAsync("/api/activity")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_code_ticket_and_a_notice_ticket_are_never_each_other()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "desk1", password = "user password", roles = Array.Empty<string>() })).EnsureSuccessStatusCode();
        var page = app.RawPage();
        var first = await Json(await page.PostAsJsonAsync("/api/signin", new { username = "desk1", password = "user password" }));
        var codeTicket = first.GetProperty("ticket").GetString();

        // The password's ticket can't skip the code by going straight to the notice.
        Assert.Equal(HttpStatusCode.BadRequest, (await page.PostAsJsonAsync("/api/signin/notice", new { ticket = codeTicket, accept = true })).StatusCode);
        Assert.False(await SignedIn(page));

        app.MfaSecrets["desk1"] = first.GetProperty("setup").GetProperty("secret").GetString()!;
        var said = await Json(await page.PostAsJsonAsync("/api/signin/mfa", new { ticket = codeTicket, code = app.MfaCode("desk1") }));
        var noticeTicket = said.GetProperty("ticket").GetString();
        // And the notice's ticket isn't a code ticket either.
        Assert.Equal(HttpStatusCode.BadRequest, (await page.PostAsJsonAsync("/api/signin/mfa", new { ticket = noticeTicket, code = app.MfaCode("desk1") })).StatusCode);
        Assert.False(await SignedIn(page));
    }
}
