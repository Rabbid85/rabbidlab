using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Computers with the Rabbid Lab client: joining, checking in, and what admins do with them.</summary>
public sealed class ClientsTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-clients-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public ClientsTests()
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

    private async Task<(string Id, string Secret, bool Approved)> Join(string computer = "AMW-LT-022")
    {
        var r = await app.CreateClient().PostAsJsonAsync("/api/clients/join", new { computer, os = "Windows 11 Pro", version = "0.2.0" });
        r.EnsureSuccessStatusCode();
        var j = await Json(r);
        return (j.GetProperty("id").GetString()!, j.GetProperty("secret").GetString()!, j.GetProperty("approved").GetBoolean());
    }

    private async Task<HttpResponseMessage> CheckIn(string id, string secret, string? user = @"AMW\jreyes")
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("RabbidLab-Client", $"{id}:{secret}");
        return await client.PostAsJsonAsync("/api/clients/checkin", new { computer = "AMW-LT-022", os = "Windows 11 Pro", version = "0.2.0", user });
    }

    [Fact]
    public async Task A_computer_joins_with_just_the_address_and_waits_until_someone_lets_it_in()
    {
        var admin = await app.SignedIn();

        var (id, secret, approved) = await Join();
        var waiting = await Json(await CheckIn(id, secret));
        (await admin.PostAsync($"/api/clients/{id}/approve", null)).EnsureSuccessStatusCode();
        var letIn = await Json(await CheckIn(id, secret));
        var listed = Assert.Single((await Json(await admin.GetAsync("/api/clients"))).EnumerateArray());

        Assert.False(approved);
        Assert.False(waiting.GetProperty("approved").GetBoolean());
        Assert.True(letIn.GetProperty("approved").GetBoolean());
        Assert.Equal(@"AMW\jreyes", listed.GetProperty("user").GetString());
        Assert.True(listed.GetProperty("online").GetBoolean());
    }

    [Fact]
    public async Task Uninstalling_the_client_is_locked_until_someone_allows_it_and_only_for_an_hour()
    {
        var admin = await app.SignedIn();
        var (id, secret, _) = await Join();
        (await admin.PostAsync($"/api/clients/{id}/approve", null)).EnsureSuccessStatusCode();

        var locked = await Json(await CheckIn(id, secret));
        var allowedBy = await Json(await admin.PostAsJsonAsync($"/api/clients/{id}/removal", new { allow = true }));
        var allowed = await Json(await CheckIn(id, secret));
        var listed = Assert.Single((await Json(await admin.GetAsync("/api/clients"))).EnumerateArray());
        app.Clock.Advance(TimeSpan.FromMinutes(61));
        var expired = await Json(await CheckIn(id, secret));
        admin = await app.SignedIn();
        (await admin.PostAsJsonAsync($"/api/clients/{id}/removal", new { allow = true, now = true })).EnsureSuccessStatusCode();
        var ordered = await Json(await CheckIn(id, secret));
        var onlyOnce = await Json(await CheckIn(id, secret));
        (await admin.PostAsJsonAsync($"/api/clients/{id}/removal", new { allow = false })).EnsureSuccessStatusCode();
        var lockedAgain = await Json(await CheckIn(id, secret));
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "viewer", password = "viewer password" })).EnsureSuccessStatusCode();
        var viewer = app.Page();
        (await viewer.PostAsJsonAsync("/api/signin", new { username = "viewer", password = "viewer password" })).EnsureSuccessStatusCode();
        var refused = await viewer.PostAsJsonAsync($"/api/clients/{id}/removal", new { allow = true });
        var missing = await admin.PostAsJsonAsync("/api/clients/nobody/removal", new { allow = true });

        Assert.Equal(JsonValueKind.Null, locked.GetProperty("removalUntil").ValueKind);
        Assert.False(locked.GetProperty("uninstall").GetBoolean());
        Assert.Equal(JsonValueKind.String, allowedBy.GetProperty("removalUntil").ValueKind);
        Assert.Equal(JsonValueKind.String, allowed.GetProperty("removalUntil").ValueKind);
        Assert.False(allowed.GetProperty("uninstall").GetBoolean()); // allowed isn't ordered
        Assert.Equal(JsonValueKind.String, listed.GetProperty("removalUntil").ValueKind);
        Assert.Equal(JsonValueKind.Null, expired.GetProperty("removalUntil").ValueKind);
        Assert.True(ordered.GetProperty("uninstall").GetBoolean());
        Assert.False(onlyOnce.GetProperty("uninstall").GetBoolean());
        Assert.Equal(JsonValueKind.Null, lockedAgain.GetProperty("removalUntil").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task A_removed_computer_or_a_wrong_secret_is_turned_away()
    {
        var admin = await app.SignedIn();
        var (id, secret, _) = await Join();

        var wrongSecret = await CheckIn(id, "not the secret");
        (await admin.PostAsync($"/api/clients/{id}/remove", null)).EnsureSuccessStatusCode();
        var removed = await CheckIn(id, secret);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, removed.StatusCode);
    }

    [Fact]
    public async Task Joining_again_while_waiting_replaces_the_old_entry_and_a_flood_of_joins_is_slowed()
    {
        var admin = await app.SignedIn();
        await Join();
        await Join();
        var listed = (await Json(await admin.GetAsync("/api/clients"))).GetArrayLength();
        for (var i = 0; i < 28; i++)
        {
            await app.CreateClient().PostAsJsonAsync("/api/clients/join", new { computer = $"PC-{i}" });
        }
        var flood = await app.CreateClient().PostAsJsonAsync("/api/clients/join", new { computer = "PC-LAST" });

        Assert.Equal(1, listed);
        Assert.Equal((HttpStatusCode) 429, flood.StatusCode);
    }

    [Fact]
    public async Task Without_approval_computers_join_straight_away_and_go_offline_when_they_stop_checking_in()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/clients/settings", new { approve = false })).EnsureSuccessStatusCode();
        var (_, _, approved) = await Join();

        app.Clock.Advance(TimeSpan.FromMinutes(4));
        var listed = Assert.Single((await Json(await (await app.SignedIn()).GetAsync("/api/clients"))).EnumerateArray());

        Assert.True(approved);
        Assert.False(listed.GetProperty("online").GetBoolean());
    }

    [Fact]
    public async Task What_a_client_says_about_itself_is_kept_short()
    {
        var admin = await app.SignedIn();
        var joined = await Json(await app.CreateClient().PostAsJsonAsync("/api/clients/join", new { computer = "AMW-LT-022", os = new string('o', 5000), version = new string('v', 5000) }));
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("RabbidLab-Client", $"{joined.GetProperty("id").GetString()}:{joined.GetProperty("secret").GetString()}");

        (await client.PostAsJsonAsync("/api/clients/checkin", new { computer = new string('c', 5000), os = new string('O', 5000), user = new string('u', 5000) })).EnsureSuccessStatusCode();
        var listed = Assert.Single((await Json(await admin.GetAsync("/api/clients"))).EnumerateArray());

        Assert.Equal("AMW-LT-022", listed.GetProperty("computer").GetString()); // a name that long isn't a computer's
        Assert.Equal(new string('O', 128), listed.GetProperty("os").GetString());
        Assert.Equal(new string('v', 128), listed.GetProperty("version").GetString());
        Assert.Equal(new string('u', 128), listed.GetProperty("user").GetString());
        Assert.True(new FileInfo(Path.Combine(data, "clients.json")).Length < 4000);
    }

    [Fact]
    public async Task A_removed_computer_s_open_request_ends_and_nothing_is_kept_for_it()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/clients/settings", new { approve = false })).EnsureSuccessStatusCode();
        var (id, secret, _) = await Join();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("RabbidLab-Client", $"{id}:{secret}");
        var commands = app.Services.GetRequiredService<ClientCommands>();

        var waiting = client.PostAsync("/api/clients/next", null);
        while (!commands.Listening(id))
        {
            await Task.Delay(10);
        }
        (await admin.PostAsync($"/api/clients/{id}/remove", null)).EnsureSuccessStatusCode();
        var ended = await waiting.WaitAsync(TimeSpan.FromSeconds(10)); // not the 50 seconds it would otherwise be held

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.False(commands.Listening(id));
    }

    [Fact]
    public async Task Only_people_allowed_to_see_clients_or_change_their_settings()
    {
        await app.SignedIn();
        app.Services.GetRequiredService<Accounts>().Add("mlopez", "user password", Roles.User);
        var user = app.Page();
        (await user.PostAsJsonAsync("/api/signin", new { username = "mlopez", password = "user password" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/clients")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/clients/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/clients/settings", new { approve = false })).StatusCode);
    }
    // A check-in as the client makes it since uninstall codes: the hash of the check it holds, and when the code was typed.
    private async Task<JsonElement> CheckInSaying(string id, string secret, string? code = null, string? codeUsed = null)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("RabbidLab-Client", $"{id}:{secret}");
        var r = await client.PostAsJsonAsync("/api/clients/checkin", new { computer = "AMW-LT-022", os = "Windows 11 Pro", version = "1.1.152", code, codeUsed });
        r.EnsureSuccessStatusCode();
        return await Json(r);
    }

    [Fact]
    public async Task Each_computer_has_an_uninstall_code_an_admin_can_show_and_the_computer_can_check_offline()
    {
        var admin = await app.SignedIn();
        var (id, secret, _) = await Join();
        var (other, _, _) = await Join("AMW-LT-023");

        var first = await CheckInSaying(id, secret);
        var check = first.GetProperty("code").GetString()!;
        var shown = await Json(await admin.PostAsync($"/api/clients/{id}/code", null));
        var code = shown.GetProperty("code").GetString()!;
        await CheckInSaying(id, secret, code: check.Split(':')[1]);
        var again = await Json(await admin.PostAsync($"/api/clients/{id}/code", null));
        var others = (await Json(await admin.PostAsync($"/api/clients/{other}/code", null))).GetProperty("code").GetString()!;
        var log = await (await admin.GetAsync("/api/activity")).Content.ReadAsStringAsync();

        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{5}(-[0-9A-HJKMNP-TV-Z]{5}){3}$", code);
        Assert.False(shown.GetProperty("held").GetBoolean());
        Assert.True(again.GetProperty("held").GetBoolean());
        Assert.Equal(code, again.GetProperty("code").GetString());
        Assert.NotEqual(code, others);
        Assert.Equal(30, first.GetProperty("unlockAfterDays").GetInt32());
        // The computer's side: only the right code passes the check, typed however.
        Assert.True(RabbidLab.Client.UninstallCode.Matches(code, check));
        Assert.True(RabbidLab.Client.UninstallCode.Matches(" " + code.ToLowerInvariant().Replace("-", " "), check));
        Assert.False(RabbidLab.Client.UninstallCode.Matches(others, check));
        Assert.False(RabbidLab.Client.UninstallCode.Matches(code[..^1], check));
        Assert.False(RabbidLab.Client.UninstallCode.Matches(code, null));
        Assert.DoesNotContain(code.Replace("-", ""), await File.ReadAllTextAsync(Path.Combine(data, "clients.json")));
        Assert.Contains("Showed the uninstall code for AMW-LT-022", log);
    }

    [Fact]
    public async Task Using_the_code_is_logged_once_and_showing_it_needs_the_right_to_remove_computers()
    {
        var admin = await app.SignedIn();
        var (id, secret, _) = await Join();
        app.Services.GetRequiredService<Accounts>().Add("mlopez", "user password", Roles.User);
        var user = app.Page();
        (await user.PostAsJsonAsync("/api/signin", new { username = "mlopez", password = "user password" })).EnsureSuccessStatusCode();
        var used = DateTime.UtcNow.AddSeconds(-20).ToString("o");

        await CheckInSaying(id, secret, codeUsed: used);
        await CheckInSaying(id, secret, codeUsed: used);
        var log = await (await admin.GetAsync("/api/activity")).Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync($"/api/clients/{id}/code", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/clients/nobody/code", null)).StatusCode);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(log, "The uninstall code was used on AMW-LT-022"));
    }

    [Fact]
    public async Task The_lock_letting_go_after_30_days_offline_can_be_turned_off_without_changing_approval()
    {
        var admin = await app.SignedIn();
        var (id, secret, _) = await Join();

        (await admin.PostAsJsonAsync("/api/clients/settings", new { unlockAfterDays = 0 })).EnsureSuccessStatusCode();
        var off = await CheckInSaying(id, secret);
        var settings = await admin.GetFromJsonAsync<JsonElement>("/api/clients/settings");
        (await admin.PostAsJsonAsync("/api/clients/settings", new { approve = false })).EnsureSuccessStatusCode();
        var after = await admin.GetFromJsonAsync<JsonElement>("/api/clients/settings");

        Assert.Equal(0, off.GetProperty("unlockAfterDays").GetInt32());
        Assert.True(settings.GetProperty("approve").GetBoolean());
        Assert.Equal(0, settings.GetProperty("unlockAfterDays").GetInt32());
        Assert.False(after.GetProperty("approve").GetBoolean());
        Assert.Equal(0, after.GetProperty("unlockAfterDays").GetInt32());
    }
    [Fact]
    public async Task A_client_that_says_its_service_is_stopping_is_offline_at_once_until_it_checks_in_again()
    {
        var admin = await app.SignedIn();
        var (id, secret, _) = await Join();
        (await admin.PostAsync($"/api/clients/{id}/approve", null)).EnsureSuccessStatusCode();
        await CheckInSaying(id, secret);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("RabbidLab-Client", $"{id}:{secret}");
        var stranger = app.CreateClient();
        stranger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("RabbidLab-Client", $"{id}:not-its-secret");
        async Task<bool> Online() => (await admin.GetFromJsonAsync<JsonElement>("/api/clients"))[0].GetProperty("online").GetBoolean();

        var before = await Online();
        var refused = await stranger.PostAsJsonAsync("/api/clients/bye", new { computer = "AMW-LT-022" });
        var stillOn = await Online();
        (await client.PostAsJsonAsync("/api/clients/bye", new { computer = "AMW-LT-022" })).EnsureSuccessStatusCode();
        var stopped = await Online();
        await CheckInSaying(id, secret);
        var back = await Online();

        Assert.True(before);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.True(stillOn);
        Assert.False(stopped);
        Assert.True(back);
    }

    [Fact]
    public async Task An_ordered_uninstall_ends_its_allowance_so_a_client_installed_again_is_locked()
    {
        var admin = await app.SignedIn();
        var (id, secret, _) = await Join();
        (await admin.PostAsync($"/api/clients/{id}/approve", null)).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/clients/{id}/removal", new { allow = true, now = true })).EnsureSuccessStatusCode();
        static bool Allowed(JsonElement answer) => answer.TryGetProperty("removalUntil", out var until) && until.ValueKind == JsonValueKind.String;

        var ordered = await CheckInSaying(id, secret);
        var installedAgain = await CheckInSaying(id, secret);

        Assert.True(ordered.GetProperty("uninstall").GetBoolean());
        Assert.True(Allowed(ordered));
        Assert.False(installedAgain.GetProperty("uninstall").GetBoolean());
        Assert.False(Allowed(installedAgain));
    }
    // A computer's inventory, as its client sends it.
    private async Task<HttpResponseMessage> SendInventory(string id, string secret, object inventory)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("RabbidLab-Client", $"{id}:{secret}");
        return await client.PostAsJsonAsync("/api/clients/inventory", inventory);
    }

    private static object Inventory(string chrome, bool missing) => new
    {
        manufacturer = "Dell Inc.", model = "OptiPlex 7090", serial = "J8K2LM3", processor = "Intel Core i7-11700", memory = 34359738368L,
        started = DateTime.UtcNow.AddDays(-3), restartNeeded = missing,
        disks = new[] { new { name = "C:", total = 500_000_000_000L, free = 200_000_000_000L }, new { name = "D:", total = 1_000_000_000_000L, free = 10_000_000_000L } },
        software = new[] { new { name = "Google Chrome", version = chrome, publisher = "Google LLC", installed = "2026-10-02" }, new { name = "7-Zip 24.08 (x64)", version = "24.08", publisher = "Igor Pavlov", installed = "2026-01-15" } },
        updates = new[] { new { kb = "KB5045935", title = "Servicing Stack Update", kind = "Update", installed = "2026-10-14" } },
        missing = missing ? new[] { new { kb = "KB5046617", title = "2026-11 Cumulative Update for Windows 11", kind = "Security Updates" } } : [],
        missingChecked = DateTime.UtcNow,
    };

    [Fact]
    public async Task A_computers_client_sends_its_inventory_and_the_list_and_the_computer_show_it()
    {
        var admin = await app.SignedIn();
        var (id, secret, _) = await Join();
        var waiting = await SendInventory(id, secret, Inventory("131.0", true));
        (await admin.PostAsync($"/api/clients/{id}/approve", null)).EnsureSuccessStatusCode();

        (await SendInventory(id, secret, Inventory("131.0", true))).EnsureSuccessStatusCode();
        var row = (await admin.GetFromJsonAsync<JsonElement>("/api/clients"))[0].GetProperty("inventory");
        var whole = (await admin.GetFromJsonAsync<JsonElement>($"/api/clients/{id}/inventory")).GetProperty("inventory");

        Assert.Equal(HttpStatusCode.Forbidden, waiting.StatusCode);
        Assert.Equal("OptiPlex 7090", row.GetProperty("model").GetString());
        Assert.Equal(40, row.GetProperty("disk").GetInt32());
        Assert.Equal(1, row.GetProperty("missing").GetInt32());
        Assert.True(row.GetProperty("restartNeeded").GetBoolean());
        Assert.Equal(2, row.GetProperty("software").GetInt32());
        Assert.Equal(2, whole.GetProperty("software").GetArrayLength());
        Assert.Equal("KB5046617", whole.GetProperty("missing")[0].GetProperty("kb").GetString());
        Assert.True(File.Exists(Path.Combine(data, "Inventory", id + ".json")));

        // The client restarts (its own update, say) and reports before Windows Update has answered it: the last answer stays.
        (await SendInventory(id, secret, new { model = "OptiPlex 7090", disks = Array.Empty<object>() })).EnsureSuccessStatusCode();
        var after = (await admin.GetFromJsonAsync<JsonElement>("/api/clients"))[0].GetProperty("inventory");
        Assert.Equal(1, after.GetProperty("missing").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, after.GetProperty("missingChecked").ValueKind);
    }

    [Fact]
    public async Task Software_and_updates_are_listed_across_computers_and_go_when_a_computer_is_removed()
    {
        var admin = await app.SignedIn();
        var (one, oneSecret, _) = await Join();
        var (two, twoSecret, _) = await Join("AMW-LT-023");
        (await admin.PostAsync($"/api/clients/{one}/approve", null)).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/clients/{two}/approve", null)).EnsureSuccessStatusCode();
        (await SendInventory(one, oneSecret, Inventory("131.0", true))).EnsureSuccessStatusCode();
        (await SendInventory(two, twoSecret, Inventory("130.2", false))).EnsureSuccessStatusCode();

        var software = await admin.GetFromJsonAsync<JsonElement>("/api/devices/software");
        var updates = await admin.GetFromJsonAsync<JsonElement>("/api/devices/updates");
        (await admin.PostAsync($"/api/clients/{two}/remove", null)).EnsureSuccessStatusCode();
        var after = await admin.GetFromJsonAsync<JsonElement>("/api/devices/software");

        var chrome = software.EnumerateArray().Single(s => s.GetProperty("name").GetString() == "Google Chrome");
        Assert.Equal(2, software.GetArrayLength());
        Assert.Equal(2, chrome.GetProperty("computers").GetArrayLength());
        Assert.Equal(["130.2", "131.0"], chrome.GetProperty("versions").EnumerateArray().Select(v => v.GetString()));
        var cumulative = updates.EnumerateArray().Single(u => u.GetProperty("kb").GetString() == "KB5046617");
        Assert.Equal(1, cumulative.GetProperty("missing").GetArrayLength());
        Assert.Equal(2, updates.EnumerateArray().Single(u => u.GetProperty("kb").GetString() == "KB5045935").GetProperty("installed").GetArrayLength());
        Assert.Equal(1, after.EnumerateArray().Single(s => s.GetProperty("name").GetString() == "Google Chrome").GetProperty("computers").GetArrayLength());
        Assert.False(File.Exists(Path.Combine(data, "Inventory", two + ".json")));
    }

    [Fact]
    public async Task Inventory_is_cut_to_size_and_only_for_people_who_may_see_computers()
    {
        var admin = await app.SignedIn();
        var (id, secret, _) = await Join();
        (await admin.PostAsync($"/api/clients/{id}/approve", null)).EnsureSuccessStatusCode();
        app.Services.GetRequiredService<Accounts>().Add("mlopez", "user password", Roles.User);
        var user = app.Page();
        (await user.PostAsJsonAsync("/api/signin", new { username = "mlopez", password = "user password" })).EnsureSuccessStatusCode();

        (await SendInventory(id, secret, new { model = new string('m', 5000), software = new[] { new { name = new string('n', 5000) }, new { name = " " } } })).EnsureSuccessStatusCode();
        var whole = (await admin.GetFromJsonAsync<JsonElement>($"/api/clients/{id}/inventory")).GetProperty("inventory");

        Assert.Equal(Inventories.MaxText, whole.GetProperty("model").GetString()!.Length);
        Assert.Equal(1, whole.GetProperty("software").GetArrayLength());
        Assert.Equal(Inventories.MaxText, whole.GetProperty("software")[0].GetProperty("name").GetString()!.Length);
        Assert.Equal(JsonValueKind.Null, (await admin.GetFromJsonAsync<JsonElement>("/api/clients"))[0].GetProperty("inventory").GetProperty("missing").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"/api/clients/{id}/inventory")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/devices/software")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/devices/updates")).StatusCode);
    }
}
