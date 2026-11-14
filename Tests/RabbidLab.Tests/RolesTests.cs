using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Roles and people: what each role allows, who's in them, the order they're in, and who can sign in.</summary>
public sealed class RolesTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-roles-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public RolesTests()
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

    private static IEnumerable<string?> Ids(JsonElement me) => me.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("id").GetString());

    /// <summary>Makes a role and returns its id.</summary>
    private static async Task<string> Make(HttpClient page, string name, params string[] permissions)
    {
        var made = await page.PostAsJsonAsync("/api/roles", new { name, color = "#5FD4A0" });
        made.EnsureSuccessStatusCode();
        var id = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        (await page.PostAsJsonAsync($"/api/roles/{id}", new { name, color = "#5FD4A0", permissions })).EnsureSuccessStatusCode();
        return id;
    }

    private async Task<HttpClient> SignIn(string username, string password)
    {
        var page = app.Page();
        (await page.PostAsJsonAsync("/api/signin", new { username, password })).EnsureSuccessStatusCode();
        return page;
    }

    [Fact]
    public async Task The_groups_from_before_roles_become_Administrator_and_User()
    {
        File.WriteAllText(Path.Combine(data, "accounts.json"),
            """{ "local": [], "directory": { "domain": "amw.com", "group": "Domain Users", "adminGroup": "Domain Admins", "stamp": "S1" } }""");

        var awiles = await SignIn("awiles", "domain pass");
        var jreyes = await SignIn("jreyes", "user pass");

        Assert.Equal(["admin", "user"], Ids(await Json(awiles, "/api/signin")));
        Assert.Equal(["user"], Ids(await Json(jreyes, "/api/signin")));
        Assert.True(File.Exists(Path.Combine(data, "roles.json")));
    }

    [Fact]
    public async Task A_role_allows_what_it_says_and_nothing_else()
    {
        var admin = await app.SignedIn();
        var help = await Make(admin, "Help desk", "seats.view", "activity.view");
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "mlopez", password = "user password", roles = new[] { help } })).EnsureSuccessStatusCode();
        var mlopez = await SignIn("mlopez", "user password");

        Assert.Equal([help, "user"], Ids(await Json(mlopez, "/api/signin")));
        Assert.Equal(HttpStatusCode.OK, (await mlopez.GetAsync("/api/activity")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mlopez.GetAsync("/api/clients")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mlopez.PostAsJsonAsync("/api/update/settings", new { automatic = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mlopez.GetAsync("/api/roles")).StatusCode);

        // Taking a permission away counts straight away, without signing in again.
        (await admin.PostAsJsonAsync($"/api/roles/{help}", new { name = "Help desk", color = "#5FD4A0", permissions = new[] { "seats.view" } })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await mlopez.GetAsync("/api/activity")).StatusCode);
    }

    [Fact]
    public async Task Nobody_changes_a_role_at_or_above_their_own_or_gives_what_they_cannot_do()
    {
        var admin = await app.SignedIn();
        var help = await Make(admin, "Help desk", "access.view", "access.roles", "access.members", "seats.view");
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "mlopez", password = "user password", roles = new[] { help } })).EnsureSuccessStatusCode();
        var mlopez = await SignIn("mlopez", "user password");

        var ownRole = await mlopez.PostAsJsonAsync($"/api/roles/{help}", new { name = "Help desk", color = "#5FD4A0", permissions = new[] { "seats.view", "updates.install" } });
        var intoAdmin = await mlopez.PostAsJsonAsync("/api/roles/admin/members", new { kind = "local", name = "mlopez" });
        var lower = await Make(mlopez, "Interns", "seats.view");
        var beyond = await mlopez.PostAsJsonAsync($"/api/roles/{lower}", new { name = "Interns", color = "#5FD4A0", permissions = new[] { "seats.view", "updates.install" } });
        var above = await mlopez.PostAsJsonAsync("/api/roles/order", new { ids = new[] { lower, help } });

        Assert.Equal(HttpStatusCode.BadRequest, ownRole.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, intoAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, beyond.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, above.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/roles/order", new { ids = new[] { lower, help } })).StatusCode);
    }

    [Fact]
    public async Task Active_Directory_accounts_need_a_role_to_sign_in()
    {
        var setup = app.Page();
        (await setup.PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" })).EnsureSuccessStatusCode();
        app.Directory.ListsAnonymously = true;
        (await setup.PostAsJsonAsync("/api/roles/user/members/remove", new { kind = "everyone", name = "ad" })).EnsureSuccessStatusCode();

        var refused = await app.Page().PostAsJsonAsync("/api/signin", new { username = "jreyes", password = "user pass" });
        var missing = await setup.PostAsJsonAsync("/api/roles/user/members", new { kind = "group", name = "Nobody Here" });
        (await setup.PostAsJsonAsync("/api/roles/user/members", new { kind = "group", name = "Domain Users" })).EnsureSuccessStatusCode();
        var jreyes = await SignIn("jreyes", "user pass");
        (await setup.PostAsJsonAsync("/api/roles/user/members/remove", new { kind = "group", name = "Domain Users" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await jreyes.GetAsync("/api/status")).StatusCode); // in no role now: signed out
        Assert.Equal(HttpStatusCode.OK, (await setup.GetAsync("/api/status")).StatusCode);
    }

    [Fact]
    public async Task Administrator_always_keeps_someone_and_nobody_removes_their_own_account()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "spare", password = "spare password" })).EnsureSuccessStatusCode();

        var emptyAdmins = await admin.PostAsJsonAsync("/api/roles/admin/members/remove", new { kind = "local", name = "admin" });
        var removeSelf = await admin.PostAsync("/api/accounts/admin/remove", null);
        var removeOther = await admin.PostAsync("/api/accounts/spare/remove", null);
        var deleteBuiltIn = await admin.PostAsync("/api/roles/user/delete", null);

        Assert.Equal(HttpStatusCode.BadRequest, emptyAdmins.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, removeSelf.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removeOther.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, deleteBuiltIn.StatusCode);
        Assert.Equal(["admin", "user"], Ids(await Json(admin, "/api/signin")));
    }

    [Fact]
    public async Task Nobody_resets_or_removes_a_local_account_in_a_role_they_do_not_manage()
    {
        var admin = await app.SignedIn();
        var help = await Make(admin, "Help desk", "access.view", "access.local");
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "mlopez", password = "user password", roles = new[] { help } })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "peer", password = "peer password", roles = new[] { help } })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "spare", password = "spare password", roles = new[] { "admin" } })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "kiosk", password = "kiosk password" })).EnsureSuccessStatusCode();
        var mlopez = await SignIn("mlopez", "user password");

        // Above them, or beside them: refused.
        var takeOver = await mlopez.PostAsJsonAsync("/api/accounts/admin/password", new { password = "taken over!" });
        var removeAdmin = await mlopez.PostAsync("/api/accounts/spare/remove", null);
        var besideThem = await mlopez.PostAsJsonAsync("/api/accounts/peer/password", new { password = "taken over!" });
        // In no role but User, and their own password: allowed.
        var below = await mlopez.PostAsJsonAsync("/api/accounts/kiosk/password", new { password = "new kiosk pass" });
        var removeBelow = await mlopez.PostAsync("/api/accounts/kiosk/remove", null);
        var own = await mlopez.PostAsJsonAsync("/api/accounts/mlopez/password", new { password = "my new password" });
        // An administrator manages every role.
        var byAdmin = await admin.PostAsJsonAsync("/api/accounts/peer/password", new { password = "set by admin" });
        var removeByAdmin = await admin.PostAsync("/api/accounts/spare/remove", null);

        Assert.Equal(HttpStatusCode.BadRequest, takeOver.StatusCode);
        Assert.Contains("Administrator", await takeOver.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, removeAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, besideThem.StatusCode);
        Assert.Equal(HttpStatusCode.OK, below.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removeBelow.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removeByAdmin.StatusCode);
        await SignIn("admin", "correct horse"); // still the administrator's own password
        await SignIn("peer", "set by admin");
    }

    [Fact]
    public async Task People_change_their_own_password_with_the_current_one_and_every_session_of_theirs_ends()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "kiosk", password = "kiosk password" })).EnsureSuccessStatusCode();
        var kiosk = await SignIn("kiosk", "kiosk password");
        var elsewhere = await SignIn("kiosk", "kiosk password");
        var windows = await SignIn(@".\localadmin", "windows pass");

        var wrong = await kiosk.PostAsJsonAsync("/api/me/password", new { current = "not it", password = "a new password" });
        var tooShort = await kiosk.PostAsJsonAsync("/api/me/password", new { current = "kiosk password", password = "short" });
        var notOurs = await windows.PostAsJsonAsync("/api/me/password", new { current = "windows pass", password = "a new password" });
        var right = await kiosk.PostAsJsonAsync("/api/me/password", new { current = "kiosk password", password = "a new password" });
        var here = await kiosk.GetAsync("/api/sessions");
        var there = await elsewhere.GetAsync("/api/sessions");
        var adminStill = await admin.GetAsync("/api/sessions");

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("current password is wrong", await wrong.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notOurs.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, here.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, there.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminStill.StatusCode);
        await SignIn("kiosk", "a new password");
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Page().PostAsJsonAsync("/api/signin", new { username = "kiosk", password = "kiosk password" })).StatusCode);
    }

    [Fact]
    public async Task A_lost_roles_file_makes_nobody_an_administrator_who_was_not_one()
    {
        var setup = app.Page();
        (await setup.PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" })).EnsureSuccessStatusCode();
        app.Services.GetRequiredService<Accounts>().AddAdmin("rescue", "rescue pass");
        (await setup.PostAsJsonAsync("/api/accounts", new { username = "kiosk", password = "kiosk password" })).EnsureSuccessStatusCode();
        app.Dispose();
        File.Delete(Path.Combine(data, "roles.json"));
        File.Delete(Path.Combine(data, "roles.json.bak"));

        using var again = new TestApp(data);
        async Task<IEnumerable<string?>> RolesOf(string username, string password)
        {
            var page = again.Page();
            (await page.PostAsJsonAsync("/api/signin", new { username, password })).EnsureSuccessStatusCode();
            return Ids(await Json(page, "/api/signin"));
        }

        // Everyone in the domain is a user again, and nobody an administrator because of it.
        Assert.Equal(["user"], await RolesOf("awiles", "domain pass"));
        Assert.Equal(["user"], await RolesOf("jreyes", "user pass"));
        Assert.Equal(["user"], await RolesOf(@".\kiosk", "kiosk password"));
        Assert.Equal(["admin", "user"], await RolesOf(@".\rescue", "rescue pass")); // made as an administrator, with --add-admin
    }

    [Fact]
    public async Task A_broken_roles_file_is_read_from_its_backup_copy_and_without_one_nobody_becomes_an_administrator()
    {
        var setup = app.Page();
        (await setup.PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" })).EnsureSuccessStatusCode();
        app.Dispose();
        Assert.True(File.Exists(Path.Combine(data, "roles.json.bak")));
        File.WriteAllText(Path.Combine(data, "roles.json"), "{ \"roles\": [ {");

        // The copy from before the last change: awiles is still the administrator.
        using (var again = new TestApp(data))
        {
            var page = again.Page();
            (await page.PostAsJsonAsync("/api/signin", new { username = "awiles", password = "domain pass" })).EnsureSuccessStatusCode();
            Assert.Equal(["admin", "user"], Ids(await Json(page, "/api/signin")));
        }

        File.WriteAllText(Path.Combine(data, "roles.json"), "null");
        File.WriteAllText(Path.Combine(data, "roles.json.bak"), "");
        using (var again = new TestApp(data))
        {
            var page = again.Page();
            (await page.PostAsJsonAsync("/api/signin", new { username = "awiles", password = "domain pass" })).EnsureSuccessStatusCode();
            Assert.Equal(["user"], Ids(await Json(page, "/api/signin")));
        }
    }

    [Fact]
    public async Task Roles_from_before_the_split_keep_everything_Change_used_to_allow()
    {
        var admin = await app.SignedIn();
        var old = await Make(admin, "Old desk", "ad.view", "ad.change");
        var viewer = await Make(admin, "Viewer", "ad.view");
        app.Dispose();
        // As a version before the split wrote it: no version, and Change alone.
        var path = Path.Combine(data, "roles.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"version\": 2,", ""));
        File.Delete(path + ".bak");

        using var again = new TestApp(data);
        var page = again.Page();
        (await page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "correct horse" })).EnsureSuccessStatusCode();
        var roles = (await Json(page, "/api/roles")).GetProperty("roles").EnumerateArray().ToDictionary(r => r.GetProperty("id").GetString()!,
            r => r.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList());

        Assert.Equal(["ad.change", "ad.enable", "ad.groups", "ad.password", "ad.view"], roles[old]);
        Assert.Equal(["ad.view"], roles[viewer]);
        // Once upgraded it stays as it's set: taking one away isn't undone at the next start.
        (await page.PostAsJsonAsync($"/api/roles/{old}", new { name = "Old desk", color = "#5FD4A0", permissions = new[] { "ad.view", "ad.change" } })).EnsureSuccessStatusCode();
        Assert.Contains("\"version\": 2", File.ReadAllText(path));
    }

    [Fact]
    public async Task A_role_s_scope_is_OUs_and_nobody_sets_one_wider_than_their_own()
    {
        const string lab = "OU=Lab,DC=amw,DC=com";
        var admin = await app.SignedIn();
        var lead = await Make(admin, "Lead", "access.view", "access.roles", "ad.view", "ad.password");
        var desk = await Make(admin, "Desk", "ad.view", "ad.password");
        object Form(string name, string[] permissions, params string[] scope) => new { name, color = "#5FD4A0", permissions, scope };
        string[] leads = ["access.view", "access.roles", "ad.view", "ad.password"];
        string[] desks = ["ad.view", "ad.password"];
        (await admin.PostAsJsonAsync($"/api/roles/{lead}", Form("Lead", leads, lab))).EnsureSuccessStatusCode();
        // One inside another is the same as the outer one alone.
        (await admin.PostAsJsonAsync($"/api/roles/{desk}", Form("Desk", desks, $"OU=People,{lab}", $"OU=Temps,OU=People,{lab}"))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "lead1", password = "user password", roles = new[] { lead } })).EnsureSuccessStatusCode();
        var lead1 = await SignIn("lead1", "user password");

        var notAnOu = await admin.PostAsJsonAsync($"/api/roles/{desk}", Form("Desk", desks, "People"));
        var elsewhere = await lead1.PostAsJsonAsync($"/api/roles/{desk}", Form("Desk", desks, "OU=Other,DC=amw,DC=com"));
        var everywhere = await lead1.PostAsJsonAsync($"/api/roles/{desk}", Form("Desk", desks));
        var beyond = await lead1.PostAsJsonAsync($"/api/roles/{desk}", Form("Desk", ["ad.view", "ad.password", "ad.delete"], $"OU=People,{lab}"));
        var inside = await lead1.PostAsJsonAsync($"/api/roles/{desk}", Form("Desk", desks, lab));
        var shown = (await Json(admin, "/api/roles")).GetProperty("roles").EnumerateArray().First(r => r.GetProperty("id").GetString() == desk).GetProperty("scope");
        var roles = app.Services.GetRequiredService<Roles>();
        var who = new Who(RabbidLab.Server.SignIn.Local, "lead1", null, []);

        Assert.Equal(HttpStatusCode.BadRequest, notAnOu.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, everywhere.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, beyond.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inside.StatusCode);
        Assert.Equal("Lab", shown[0].GetProperty("path").GetString());
        Assert.Equal([lab], roles.Reach(who, "ad.password"));
        Assert.Null(roles.Reach(new Who(RabbidLab.Server.SignIn.Local, "admin", null, []), "ad.password"));
        // A second role without a scope that allows the same thing makes it everywhere.
        var wide = await Make(admin, "Wide", "ad.password");
        (await admin.PostAsJsonAsync($"/api/roles/{wide}/members", new { kind = "local", name = "lead1" })).EnsureSuccessStatusCode();
        Assert.Null(roles.Reach(who, "ad.password"));
        Assert.Equal([lab], roles.Reach(who, "ad.view"));
    }

    [Fact]
    public async Task Search_finds_groups_people_and_local_accounts()
    {
        var setup = app.Page();
        (await setup.PostAsJsonAsync("/api/setup/directory", new { domain = "amw.com", username = "awiles", password = "domain pass" })).EnsureSuccessStatusCode();
        app.Directory.ListsAnonymously = true;
        (await setup.PostAsJsonAsync("/api/accounts", new { username = "jr-kiosk", password = "kiosk password" })).EnsureSuccessStatusCode();

        var found = (await Json(setup, "/api/roles/search?q=jr")).GetProperty("entries").EnumerateArray().Select(e => $"{e.GetProperty("kind").GetString()}:{e.GetProperty("name").GetString()}");

        Assert.Equal(["user:jreyes", "local:jr-kiosk"], found);
    }
}
