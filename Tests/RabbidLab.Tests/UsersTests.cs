using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// The user tools: Windows accounts on this server and on seats (through their clients), and Active Directory accounts
/// as Rabbid Lab's own AD account. The seat here is played by the test, answering orders the way the client does.
/// </summary>
public sealed class UsersTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-users-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public UsersTests()
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

    private static async Task<string> Error(HttpResponseMessage r) => (await Json(r)).GetProperty("error").GetString()!;

    /// <summary>A computer that joined, was let in and checked in with the client version given; its own client for the server.</summary>
    private async Task<(string Id, HttpClient Client)> Seat(HttpClient admin, string version = "0.4.0", bool letIn = true)
    {
        var joined = await Json(await app.CreateClient().PostAsJsonAsync("/api/clients/join", new { computer = "AMW-LT-022", os = "Windows 11 Pro", version }));
        var id = joined.GetProperty("id").GetString()!;
        if (letIn)
        {
            (await admin.PostAsync($"/api/clients/{id}/approve", null)).EnsureSuccessStatusCode();
        }
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("RabbidLab-Client", $"{id}:{joined.GetProperty("secret").GetString()}");
        (await client.PostAsJsonAsync("/api/clients/checkin", new { computer = "AMW-LT-022", os = "Windows 11 Pro", version })).EnsureSuccessStatusCode();
        return (id, client);
    }

    /// <summary>Takes the seat's next order and answers it as the client would; the order it was given.</summary>
    private async Task<JsonElement> Serve(string id, HttpClient client, Func<JsonElement, object> answer)
    {
        var next = client.PostAsync("/api/clients/next", null);
        var commands = app.Services.GetRequiredService<ClientCommands>();
        while (!commands.Listening(id) && !next.IsCompleted)
        {
            await Task.Delay(10);
        }
        var r = await next;
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var order = await Json(r);
        (await client.PostAsJsonAsync("/api/clients/done", answer(order))).EnsureSuccessStatusCode();
        return order;
    }

    /// <summary>The seat starts listening, then the page asks; the order the seat got, and what the page got.</summary>
    private async Task<(JsonElement Order, HttpResponseMessage Page)> Ask(string id, HttpClient client, Func<Task<HttpResponseMessage>> page, Func<JsonElement, object> answer)
    {
        var served = Serve(id, client, answer);
        var commands = app.Services.GetRequiredService<ClientCommands>();
        while (!commands.Listening(id))
        {
            await Task.Delay(10);
        }
        var said = await page();
        return (await served, said);
    }

    private async Task<HttpClient> SignIn(string username, string password)
    {
        var page = app.Page();
        (await page.PostAsJsonAsync("/api/signin", new { username, password })).EnsureSuccessStatusCode();
        return page;
    }

    private static async Task<string> Role(HttpClient admin, string name, params string[] permissions)
    {
        var id = (await Json(await admin.PostAsJsonAsync("/api/roles", new { name, color = "#5FD4A0" }))).GetProperty("id").GetString()!;
        (await admin.PostAsJsonAsync($"/api/roles/{id}", new { name, color = "#5FD4A0", permissions })).EnsureSuccessStatusCode();
        return id;
    }

    [Fact]
    public async Task A_seat_s_accounts_come_from_its_client_once_it_listens()
    {
        var admin = await app.SignedIn();
        var (id, client) = await Seat(admin);

        var notYet = await admin.GetAsync($"/api/windows/{id}/accounts");
        var (order, page) = await Ask(id, client, () => admin.GetAsync($"/api/windows/{id}/accounts"),
            o => new { id = o.GetProperty("id").GetString(), ok = true, accounts = new[] { new { name = "kiosk", enabled = true } }, groups = new[] { new { name = "Administrators" } } });
        var computers = (await Json(await admin.GetAsync("/api/windows/computers"))).EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.Conflict, notYet.StatusCode);
        Assert.Contains("isn't listening", await Error(notYet));
        Assert.Equal("list", order.GetProperty("action").GetString());
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("kiosk", (await Json(page)).GetProperty("accounts")[0].GetProperty("name").GetString());
        var seat = computers.Single(c => c.GetProperty("id").GetString() == id);
        Assert.True(seat.GetProperty("ready").GetBoolean());
        Assert.Equal("AMW-LT-022", seat.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Making_a_Windows_account_on_a_seat_gives_the_password_once_and_logs_none()
    {
        var admin = await app.SignedIn();
        var (id, client) = await Seat(admin);

        var (order, page) = await Ask(id, client,
            () => admin.PostAsJsonAsync($"/api/windows/{id}/accounts", new { name = "kiosk2", fullName = "Front desk", mustChange = true, groups = new[] { "Administrators" } }),
            o => new { id = o.GetProperty("id").GetString(), ok = true });
        var password = (await Json(page)).GetProperty("password").GetString()!;
        var (_, refused) = await Ask(id, client, () => admin.PostAsJsonAsync($"/api/windows/{id}/accounts/kiosk2/delete", new { }),
            o => new { id = o.GetProperty("id").GetString(), ok = false, error = "Windows on AMW-LT-022 said no." });
        var log = await (await admin.GetAsync("/api/activity")).Content.ReadAsStringAsync();

        Assert.Equal("create", order.GetProperty("action").GetString());
        Assert.Equal("kiosk2", order.GetProperty("name").GetString());
        Assert.Equal(password, order.GetProperty("newPassword").GetString());
        Assert.True(order.GetProperty("mustChange").GetBoolean());
        Assert.Equal("Administrators", order.GetProperty("add")[0].GetString());
        Assert.Equal(16, password.Length);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("Windows on AMW-LT-022 said no.", await Error(refused));
        Assert.Contains("Made the Windows account kiosk2 on AMW-LT-022", log);
        Assert.DoesNotContain(password, log);
    }

    [Fact]
    public async Task A_seat_that_can_t_take_changes_says_why()
    {
        var admin = await app.SignedIn();
        var (old, _) = await Seat(admin, version: "0.3.0");
        var (waiting, _) = await Seat(admin, letIn: false);

        var tooOld = await admin.GetAsync($"/api/windows/{old}/accounts");
        var notLetIn = await admin.GetAsync($"/api/windows/{waiting}/accounts");
        app.Clock.Advance(TimeSpan.FromMinutes(5));
        var offline = await (await app.SignedIn()).GetAsync($"/api/windows/{old}/accounts");

        Assert.Equal(HttpStatusCode.Conflict, tooOld.StatusCode);
        Assert.Contains("0.3.0", await Error(tooOld));
        Assert.Equal(HttpStatusCode.NotFound, notLetIn.StatusCode);
        Assert.Contains("offline", await Error(offline));
    }

    [Fact]
    public async Task The_server_s_own_Windows_accounts_change_here()
    {
        var admin = await app.SignedIn();

        var listed = await Json(await admin.GetAsync("/api/windows/server/accounts"));
        var made = await Json(await admin.PostAsJsonAsync("/api/windows/server/accounts", new { name = "backup", password = "Typed pass 1", groups = new[] { "Users" } }));
        (await admin.PostAsJsonAsync("/api/windows/server/accounts/backup/enable", new { on = false })).EnsureSuccessStatusCode();
        var off = app.Windows.Accounts.Single(a => a.Name == "backup").Enabled;
        (await admin.PostAsJsonAsync("/api/windows/server/accounts/backup/delete", new { })).EnsureSuccessStatusCode();
        var badName = await admin.PostAsJsonAsync("/api/windows/server/accounts", new { name = "bad/name" });
        var unknown = await admin.PostAsJsonAsync("/api/windows/server/accounts/nobody/enable", new { on = true });

        Assert.Equal(["Administrator", "localadmin"], listed.GetProperty("accounts").EnumerateArray().Select(a => a.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Null, made.GetProperty("password").ValueKind); // typed, so not said back
        Assert.Equal("Typed pass 1", app.Windows.Orders.Single(o => o.Action == "create").NewPassword);
        Assert.False(off);
        Assert.DoesNotContain(app.Windows.Accounts, a => a.Name == "backup");
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Rabbid_Lab_s_AD_account_is_set_up_once_then_makes_people()
    {
        var admin = await app.SignedIn();

        var before = await Json(await admin.GetAsync("/api/ad"));
        var notSetUp = await admin.GetAsync("/api/ad/users");
        var wrong = await admin.PostAsJsonAsync("/api/ad/look", new { username = "awiles", password = "nope" });
        var connected = await admin.PostAsJsonAsync("/api/ad/connect", new { username = "awiles", password = "domain pass", account = "svc-rabbidlab" });
        var made = await Json(await admin.PostAsJsonAsync("/api/ad/users", new { first = "Maria", last = "Lopez", username = "mlopez", mustChange = true, enabled = true }));
        var twice = await admin.PostAsJsonAsync("/api/ad/users", new { username = "mlopez" });
        var reset = await Json(await admin.PostAsJsonAsync("/api/ad/users/mlopez/password", new { mustChange = true }));
        var nobody = await admin.PostAsJsonAsync("/api/ad/users/nobody/password", new { });
        var listed = await Json(await admin.GetAsync("/api/ad/users"));
        var log = await (await admin.GetAsync("/api/activity")).Content.ReadAsStringAsync();

        Assert.Equal(JsonValueKind.Null, before.GetProperty("state").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, notSetUp.StatusCode);
        Assert.Equal("That username or password is wrong.", await Error(wrong));
        Assert.Equal("svc-rabbidlab", (await Json(connected)).GetProperty("state").GetProperty("account").GetString());
        var first = made.GetProperty("password").GetString()!;
        Assert.Equal(reset.GetProperty("password").GetString(), app.Tools.Passwords["mlopez"]);
        Assert.Equal(HttpStatusCode.BadRequest, twice.StatusCode);
        Assert.NotEqual(first, reset.GetProperty("password").GetString());
        Assert.Equal(HttpStatusCode.NotFound, nobody.StatusCode);
        Assert.Equal(2, listed.GetProperty("users").GetArrayLength());
        Assert.Contains("Made svc-rabbidlab in amw.com", log);
        Assert.Contains("Made the AD account mlopez", log);
        Assert.DoesNotContain(first, log);
        Assert.DoesNotContain("domain pass", log);
    }

    [Fact]
    public async Task Each_tool_takes_its_own_permission()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/ad/connect", new { username = "awiles", password = "domain pass", account = "svc-rabbidlab" })).EnsureSuccessStatusCode();
        var help = await Role(admin, "Help desk", "windows.view", "ad.view", "ad.change");
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "mlopez", password = "user password", roles = new[] { help } })).EnsureSuccessStatusCode();
        var desk = await SignIn("mlopez", "user password");

        Assert.Equal(HttpStatusCode.OK, (await desk.GetAsync("/api/windows/server/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.PostAsJsonAsync("/api/windows/server/accounts", new { name = "x1", password = "Pass word 1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.PostAsJsonAsync("/api/windows/server/accounts/localadmin/enable", new { on = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await desk.GetAsync("/api/ad/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await desk.PostAsJsonAsync("/api/ad/users/jreyes/unlock", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.PostAsJsonAsync("/api/ad/users", new { username = "x2" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.PostAsJsonAsync("/api/ad/users/jreyes/delete", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.PostAsJsonAsync("/api/ad/disconnect", new { })).StatusCode);
    }

    [Fact]
    public async Task The_directory_tools_take_their_permissions_and_wait_for_Rabbid_Lab_s_account()
    {
        var admin = await app.SignedIn();
        var help = await Role(admin, "Viewer", "ad.view");
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "mlopez", password = "user password", roles = new[] { help } })).EnsureSuccessStatusCode();
        var viewer = await SignIn("mlopez", "user password");
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "nobody1", password = "user password" })).EnsureSuccessStatusCode();
        var user = await SignIn("nobody1", "user password");

        foreach (var path in new[] { "/api/ad/domain", "/api/ad/audit", "/api/ad/recycle", "/api/ad/browse", "/api/ad/object?dn=DC=amw,DC=com" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await viewer.GetAsync(path)).StatusCode); // allowed, but there's no AD account yet
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/ad/recycle/restore", new { dn = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/ad/recycle/enable", new { username = "a", password = "b" })).StatusCode);
    }

    [Fact]
    public async Task A_group_change_that_only_partly_worked_is_done_with_a_warning_and_logged()
    {
        var admin = await app.SignedIn();
        var (id, client) = await Seat(admin);

        // The seat added the account to Administrators but found no group called Typo: done, with a warning.
        var (order, page) = await Ask(id, client,
            () => admin.PostAsJsonAsync($"/api/windows/{id}/accounts/kiosk/groups", new { add = new[] { "Administrators", "Typo" }, remove = new[] { "Users" } }),
            o => new { id = o.GetProperty("id").GetString(), ok = true, warning = "There's no group called Typo." });
        var log = await (await admin.GetAsync("/api/activity")).Content.ReadAsStringAsync();

        Assert.Equal("groups", order.GetProperty("action").GetString());
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("There's no group called Typo.", (await Json(page)).GetProperty("warning").GetString());
        Assert.Contains("Changed kiosk's groups on AMW-LT-022: into Administrators, Typo; out of Users", log);
        Assert.Contains("There's no group called Typo.", log);
        Assert.Contains("\"warning\"", log);
    }

    [Fact]
    public async Task Plain_lists_carry_a_header_when_Active_Directory_had_more_than_it_sent()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/ad/connect", new { username = "awiles", password = "domain pass", account = "svc-rabbidlab" })).EnsureSuccessStatusCode();

        var all = await admin.GetAsync("/api/ad/groups");
        app.Tools.More = true;
        var cut = await admin.GetAsync("/api/ad/groups");
        var members = await admin.GetAsync("/api/ad/groups/members?dn=" + Uri.EscapeDataString("CN=Lab Admins,DC=amw,DC=com"));

        Assert.False(all.Headers.Contains(UsersApi.MoreHeader));
        Assert.Equal("true", cut.Headers.GetValues(UsersApi.MoreHeader).Single());
        Assert.Equal(JsonValueKind.Array, (await Json(cut)).ValueKind); // still the plain list the page takes
        Assert.Equal("true", members.Headers.GetValues(UsersApi.MoreHeader).Single());
    }

    [Fact]
    public void Delegation_goes_after_the_explicit_entries_and_isn_t_added_twice()
    {
        byte[] admins = [1, 2, 0, 0, 0, 0, 0, 5, 32, 0, 0, 0, 32, 2, 0, 0];
        byte[] svc = [1, 5, 0, 0, 0, 0, 0, 5, 21, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 0x51, 4, 0, 0];
        var mine = Dacl.Ace(0, 0x20094, null, null, admins);
        var inherited = Dacl.Ace(0x10 | 0x02, 0x000F01FF, null, null, admins);
        var descriptor = Descriptor(admins, mine, inherited);

        var once = Dacl.Grant(descriptor, Dacl.Delegation(svc));
        var twice = Dacl.Grant(once, Dacl.Delegation(svc));
        var aces = Aces(once);

        Assert.Equal(8, aces.Count);
        Assert.Equal(mine, aces[0]);
        Assert.Equal(Dacl.Delegation(svc), aces.Skip(1).Take(6));
        Assert.Equal(inherited, aces[7]);
        Assert.Equal(once, twice);
        Assert.All(Dacl.Delegation(svc), e => Assert.Contains(aces, a => a.SequenceEqual(e)));
        Assert.All(Dacl.Delegation(svc), e => Assert.DoesNotContain(Aces(descriptor), a => a.SequenceEqual(e)));
        Assert.Equal(admins, Owner(once));
    }

    [Fact]
    public void Names_and_passwords_are_made_the_way_Active_Directory_takes_them()
    {
        var password = Passwords.Make();

        Assert.Equal(@"Smith\, John", Ldap.Escape("Smith, John"));
        Assert.Equal("Smith, John", Ldap.Name(@"CN=Smith\, John,OU=People,DC=amw,DC=com"));
        Assert.Equal("Lab / People", Ldap.Path("OU=People,OU=Lab,DC=amw,DC=com", "DC=amw,DC=com"));
        Assert.Equal("OU=People,DC=amw,DC=com", Ldap.Parent(@"CN=Smith\, John,OU=People,DC=amw,DC=com"));
        Assert.True(Ldap.Under("OU=People,OU=Lab,DC=amw,DC=com", "OU=Lab,DC=amw,DC=com"));
        Assert.False(Ldap.Under("OU=People,OU=NotLab,DC=amw,DC=com", "OU=Lab,DC=amw,DC=com"));
        Assert.Equal("\"pw\""u8.ToArray().SelectMany(b => new[] { b, (byte) 0 }), Ldap.Password("pw"));
        Assert.Equal(16, password.Length);
        Assert.Contains(password, c => char.IsUpper(c));
        Assert.Contains(password, c => char.IsLower(c));
        Assert.Contains(password, c => char.IsDigit(c));
        Assert.Contains(password, c => !char.IsLetterOrDigit(c));
        Assert.Null(DirectoryTools.BadName("mlopez"));
        Assert.NotNull(DirectoryTools.BadName("m lopez"));
        Assert.NotNull(DirectoryTools.BadName("averyveryverylongusername"));
    }

    // A self-relative security descriptor: header, DACL, owner.
    private static byte[] Descriptor(byte[] owner, params byte[][] aces)
    {
        var size = 8 + aces.Sum(a => a.Length);
        var dacl = new List<byte> { 4, 0, (byte) size, (byte) (size >> 8), (byte) aces.Length, 0, 0, 0 };
        foreach (var a in aces)
        {
            dacl.AddRange(a);
        }
        var sd = new List<byte> { 1, 0, 0x04, 0x80 };
        var daclAt = 20;
        var ownerAt = daclAt + dacl.Count;
        sd.AddRange(BitConverter.GetBytes(ownerAt));
        sd.AddRange(BitConverter.GetBytes(0));
        sd.AddRange(BitConverter.GetBytes(0));
        sd.AddRange(BitConverter.GetBytes(daclAt));
        sd.AddRange(dacl);
        sd.AddRange(owner);
        return [.. sd];
    }

    private static List<byte[]> Aces(byte[] sd)
    {
        var at = BitConverter.ToInt32(sd, 16);
        var count = BitConverter.ToUInt16(sd, at + 4);
        var list = new List<byte[]>();
        var p = at + 8;
        for (var i = 0; i < count; i++)
        {
            var size = BitConverter.ToUInt16(sd, p + 2);
            list.Add(sd[p..(p + size)]);
            p += size;
        }
        return list;
    }

    private static byte[] Owner(byte[] sd)
    {
        var at = BitConverter.ToInt32(sd, 4);
        return sd[at..(at + 8 + (4 * sd[at + 1]))];
    }
}
