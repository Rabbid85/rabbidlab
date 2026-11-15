using System.Net.Http.Json;
using System.Text.Json;

namespace RabbidLab.Tests;

/// <summary>How open pages hear about an update: the one that didn't click is told as soon as the one that did.</summary>
public sealed class UpdatePageTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-updatepage-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public UpdatePageTests()
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
    public async Task A_page_that_shows_the_current_step_is_held_until_the_step_changes()
    {
        var admin = await app.SignedIn();
        var now = (await admin.GetFromJsonAsync<JsonElement>("/api/update")).GetProperty("status").GetString();

        // Asked without saying what it shows, or showing something else: answered at once.
        await admin.GetAsync("/api/update").WaitAsync(TimeSpan.FromSeconds(5));
        await admin.GetAsync("/api/update?after=downloading").WaitAsync(TimeSpan.FromSeconds(5));

        // Showing the step the server is on: it waits, and is answered the moment Install now is clicked on another page.
        var other = await app.SignedIn();
        var waiting = admin.GetAsync($"/api/update?after={now}");
        await Task.Delay(500);
        Assert.False(waiting.IsCompleted);
        var clicked = await other.PostAsync("/api/update/install", null);
        clicked.EnsureSuccessStatusCode();
        Assert.True((await clicked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("chosen").GetBoolean());

        var heard = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        heard.EnsureSuccessStatusCode();
    }
}
