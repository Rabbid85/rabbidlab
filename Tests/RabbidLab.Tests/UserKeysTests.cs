using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RabbidLab.Tests;

/// <summary>Your own keyboard shortcuts (UserKeys.cs): kept for you, one letter per page, and Reset puts the defaults back.</summary>
public sealed class UserKeysTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-keys-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public UserKeysTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
    }

    public void Dispose()
    {
        app.Dispose();
        try { Directory.Delete(data, true); } catch (IOException) { }
    }

    private static async Task<Dictionary<string, string>> Keys(HttpResponseMessage answer) =>
        (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("keys").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);

    [Fact]
    public async Task Keys_are_kept_checked_and_reset()
    {
        var client = await app.SignedIn();
        Assert.Empty(await Keys(await client.GetAsync("/api/me/keys")));

        var saved = await client.PostAsJsonAsync("/api/me/keys", new { keys = new Dictionary<string, string> { ["network"] = "X", ["users"] = "u", ["whats-new"] = "" } });
        saved.EnsureSuccessStatusCode();
        Assert.Equal(new Dictionary<string, string> { ["network"] = "x", ["users"] = "u", ["whats-new"] = "-" }, await Keys(saved));
        Assert.Equal("x", (await Keys(await client.GetAsync("/api/me/keys")))["network"]);

        // The same letter twice, more than one letter, or a page key that isn't one, is refused with why.
        var twice = await client.PostAsJsonAsync("/api/me/keys", new { keys = new Dictionary<string, string> { ["network"] = "u", ["users"] = "u" } });
        Assert.Equal(HttpStatusCode.BadRequest, twice.StatusCode);
        Assert.Contains("set for two pages", await twice.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/me/keys", new { keys = new Dictionary<string, string> { ["network"] = "ab" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/me/keys", new { keys = new Dictionary<string, string> { ["../x"] = "a" } })).StatusCode);

        (await client.PostAsync("/api/me/keys/reset", null)).EnsureSuccessStatusCode();
        Assert.Empty(await Keys(await client.GetAsync("/api/me/keys")));
    }

    [Fact]
    public async Task Changing_keys_needs_the_page_header()
    {
        var client = await app.SignedIn();
        client.DefaultRequestHeaders.Remove("X-Rabbid-Lab");
        var answer = await client.PostAsJsonAsync("/api/me/keys", new { keys = new Dictionary<string, string> { ["network"] = "x" } });
        Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
    }
}
