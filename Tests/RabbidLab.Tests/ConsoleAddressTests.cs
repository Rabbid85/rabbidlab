using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RabbidLab.Tests;

/// <summary>Settings, Console address: where the console opens, and its certificate.</summary>
public sealed class ConsoleAddressTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-console-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public ConsoleAddressTests()
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

    [Fact]
    public async Task ListsTheComputerNameAndNeedsASignIn()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Page().GetAsync("/api/console")).StatusCode);
        var page = await app.SignedIn();
        var answer = await page.GetFromJsonAsync<JsonElement>("/api/console");
        var addresses = answer.GetProperty("addresses").EnumerateArray().ToList();
        Assert.Contains(addresses, a => a.GetProperty("what").GetString() == "Computer name"
            && a.GetProperty("url").GetString()!.Contains(Environment.MachineName.ToLowerInvariant(), StringComparison.Ordinal));
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("certificate").ValueKind); // the test server has no HTTPS
    }

    [Fact]
    public async Task NoCertificateToDownloadOverHttp()
    {
        var page = await app.SignedIn();
        Assert.Equal(HttpStatusCode.NotFound, (await page.GetAsync("/api/console/certificate")).StatusCode);
    }
}
