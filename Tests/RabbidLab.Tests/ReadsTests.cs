using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace RabbidLab.Tests;

/// <summary>
/// The page's reads carry no X-Rabbid-Lab header: only its changes do. Every GET is asked the way the page asks it, signed in
/// and without the header, and none may answer that the header is missing.
/// </summary>
public sealed class ReadsTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-reads-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public ReadsTests()
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
    public async Task No_read_asks_for_the_header_that_only_changes_carry()
    {
        var client = await app.SignedIn();
        await client.PostAsJsonAsync("/api/brand", new { accent = "#1FA3A8" });
        client.DefaultRequestHeaders.Remove("X-Rabbid-Lab"); // signed in, and from here on asking as the page's reads do

        var reads = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .Where(path => path.StartsWith("/api/", StringComparison.Ordinal) && !path.Contains('{'))
            .Distinct().Order().ToList();
        var refused = new List<string>();
        foreach (var path in reads)
        {
            using var answer = await client.GetAsync(path);
            if ((await answer.Content.ReadAsStringAsync()).Contains("header is missing", StringComparison.Ordinal))
            {
                refused.Add(path);
            }
        }
        var brand = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/brand");

        Assert.Contains("/api/brand", reads); // the list is the real one
        Assert.True(reads.Count > 20, $"only {reads.Count} reads were found");
        Assert.Empty(refused);
        Assert.Equal("#1FA3A8", brand.GetProperty("accent").GetString());
    }
}
