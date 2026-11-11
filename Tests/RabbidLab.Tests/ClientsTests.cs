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
}
