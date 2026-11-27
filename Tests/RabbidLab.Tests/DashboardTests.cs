using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RabbidLab.Tests;

/// <summary>Your own dashboard (Dashboard.cs): the cards, their order and sizes are kept for you, checked, and Reset puts the page's own back.</summary>
public sealed class DashboardTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-dash-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public DashboardTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
    }

    public void Dispose()
    {
        app.Dispose();
        try { Directory.Delete(data, true); } catch (IOException) { }
    }

    private static async Task<JsonElement> Cards(HttpResponseMessage answer) => (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cards");

    [Fact]
    public async Task A_dashboard_is_kept_checked_and_reset()
    {
        var client = await app.SignedIn();
        Assert.Equal(JsonValueKind.Null, (await Cards(await client.GetAsync("/api/me/dashboard"))).ValueKind);

        var saved = await client.PostAsJsonAsync("/api/me/dashboard", new { cards = new object[] { new { type = "sensors", w = 2, h = 1 }, new { type = "news", w = 2, h = 2, title = "  Releases  " } } });
        saved.EnsureSuccessStatusCode();
        var kept = await Cards(await client.GetAsync("/api/me/dashboard"));
        Assert.Equal(["sensors", "news"], kept.EnumerateArray().Select(c => c.GetProperty("type").GetString()));
        Assert.Equal("Releases", kept[1].GetProperty("title").GetString());
        Assert.Equal(2, kept[1].GetProperty("h").GetInt32());

        // A card there isn't, the same card twice, or a size no card has, is refused with why, and nothing is kept of it.
        foreach (var bad in new object[][]
        {
            [new { type = "payroll", w = 1, h = 1 }],
            [new { type = "links", w = 1, h = 1 }, new { type = "links", w = 2, h = 1 }],
            [new { type = "links", w = 5, h = 1 }],
            [new { type = "links", w = 1, h = 3 }],
            [new { type = "links", w = 1, h = 1, title = new string('x', 41) }],
        })
        {
            var refused = await client.PostAsJsonAsync("/api/me/dashboard", new { cards = bad });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
        Assert.Equal(2, (await Cards(await client.GetAsync("/api/me/dashboard"))).GetArrayLength());

        // No cards at all is an arrangement too; Reset is the page's own again.
        (await client.PostAsJsonAsync("/api/me/dashboard", new { cards = Array.Empty<object>() })).EnsureSuccessStatusCode();
        Assert.Equal(0, (await Cards(await client.GetAsync("/api/me/dashboard"))).GetArrayLength());
        (await client.PostAsync("/api/me/dashboard/reset", null)).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await Cards(await client.GetAsync("/api/me/dashboard"))).ValueKind);
    }

    [Fact]
    public async Task The_cards_read_answers_in_a_few_lines()
    {
        var client = await app.SignedIn();
        var now = await (await client.GetAsync("/api/overview/cards")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, now.GetProperty("alerts").GetProperty("fresh").GetInt32());
        Assert.True(now.GetProperty("network").GetProperty("all").GetInt32() >= 0);
    }
}
