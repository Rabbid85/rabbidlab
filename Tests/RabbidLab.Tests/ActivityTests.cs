using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Settings, Activity log: found on the server by kind, person, words, time and problems, a page at a time, and as CSV.</summary>
public sealed class ActivityTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-activity-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public ActivityTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
    }

    public void Dispose()
    {
        app.Dispose();
        try { Directory.Delete(data, true); } catch (IOException) { }
    }

    [Fact]
    public async Task The_log_is_narrowed_on_the_server_and_handed_back_a_page_at_a_time()
    {
        var client = await app.SignedIn();
        var log = app.Services.GetRequiredService<ActivityLog>();
        for (var i = 0; i < 60; i++)
        {
            log.Record("jsmith", "10.0.3.9", "clients", "client.approve", $"Let DESKTOP-{i:00} into Seats", "ok");
        }
        log.Record("lbrooks", null, "users", "ad.password", "Set a new password for Priya Raman", "ok");
        log.Record("admin", "10.0.3.120", "sign-in", "signin.failed", "Sign-in failed", "failed");

        var seats = await Find(client, "kind=seats&count=50");
        var priya = await Find(client, "q=priya");
        var lbrooks = await Find(client, "by=lbrooks");
        var problems = await Find(client, "problems=true");
        var later = await Find(client, $"since={Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(5).ToString("o"))}");

        Assert.Equal((50, 60), (seats.GetProperty("entries").GetArrayLength(), seats.GetProperty("total").GetInt32()));
        Assert.Equal("Let DESKTOP-59 into Seats", seats.GetProperty("entries")[0].GetProperty("title").GetString()); // newest first
        Assert.Equal(1, priya.GetProperty("total").GetInt32());
        Assert.Equal("Set a new password for Priya Raman", lbrooks.GetProperty("entries")[0].GetProperty("title").GetString());
        Assert.Equal((1, 1), (problems.GetProperty("total").GetInt32(), problems.GetProperty("problems").GetInt32()));
        Assert.Equal(0, later.GetProperty("total").GetInt32());
        Assert.Contains("jsmith", seats.GetProperty("people").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task Export_is_a_CSV_with_formulas_kept_out()
    {
        var client = await app.SignedIn();
        app.Services.GetRequiredService<ActivityLog>().Record("=cmd|' /C calc'!A0", null, "links", "link.add", "Added the link \"Grafana\", for dashboards", "ok");

        var csv = await client.GetStringAsync("/api/activity/export?kind=lab");

        Assert.StartsWith("When (UTC),Who,From,Area,What,Result,Detail", csv.TrimStart('﻿'));
        Assert.Contains("'=cmd", csv);
        Assert.Contains("\"Added the link \"\"Grafana\"\", for dashboards\"", csv);
    }

    private static async Task<JsonElement> Find(HttpClient client, string query) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/activity/find?{query}");
}
