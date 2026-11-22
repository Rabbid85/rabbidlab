using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>What counts as a missing update, and what a computer's Group Policy "errors" leave out because they aren't errors.</summary>
public sealed class UpdatesAndPolicyNoiseTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-updnoise-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public UpdatesAndPolicyNoiseTests()
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

    // A computer that joined, was approved and sent this inventory. Gives its id.
    private async Task<string> Computer(HttpClient admin, object inventory)
    {
        var joined = await (await app.CreateClient().PostAsJsonAsync("/api/clients/join", new { computer = "AZU01-VM-GEN-01", os = "Windows Server 2022", version = "1.1.236" })).Content.ReadFromJsonAsync<JsonElement>();
        var (id, secret) = (joined.GetProperty("id").GetString()!, joined.GetProperty("secret").GetString()!);
        (await admin.PostAsync($"/api/clients/{id}/approve", null)).EnsureSuccessStatusCode();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("RabbidLab-Client", $"{id}:{secret}");
        (await client.PostAsJsonAsync("/api/clients/inventory", inventory)).EnsureSuccessStatusCode();
        return id;
    }

    [Fact]
    public async Task Antivirus_definitions_are_listed_but_not_counted_as_missing()
    {
        var admin = await app.SignedIn();
        var id = await Computer(admin, new
        {
            model = "Virtual Machine", missingChecked = DateTime.UtcNow,
            missing = new object[]
            {
                new { kb = "KB2267602", title = "Security Intelligence Update for Microsoft Defender Antivirus", kind = "Definition Updates", definition = true },
                new { kb = "KB890830", title = "Windows Malicious Software Removal Tool x64 - v5.147", kind = "Update Rollups" },
                new { kb = "KB5131090", title = "2026-11 Cumulative Update for Microsoft server operating system version 21H2", kind = "Security Updates" },
                new { kb = "", title = "HP - Printer - 4/11/2019 12:00:00 AM - 8.0.1329.4953", kind = "Drivers" },
            },
        });

        var row = (await admin.GetFromJsonAsync<JsonElement>("/api/clients"))[0].GetProperty("inventory");
        var whole = (await admin.GetFromJsonAsync<JsonElement>($"/api/clients/{id}/inventory")).GetProperty("inventory").GetProperty("missing");

        Assert.Equal(3, row.GetProperty("missing").GetInt32());
        Assert.Equal(4, whole.GetArrayLength());
        Assert.True(whole[0].GetProperty("definition").GetBoolean());
        Assert.False(whole[1].GetProperty("definition").GetBoolean());
    }

    [Fact]
    public async Task What_an_older_client_calls_a_Group_Policy_error_and_isn_t_is_left_out()
    {
        var admin = await app.SignedIn();
        var id = await Computer(admin, new
        {
            model = "Virtual Machine",
            groupPolicy = new
            {
                @checked = DateTime.UtcNow,
                errors = new[]
                {
                    new { when = DateTime.UtcNow, source = "Group Policy", text = "Completed MDM Policy Extension Processing in 16 milliseconds.", warning = false },
                    new { when = DateTime.UtcNow, source = "Group Policy", text = "Group Policy bandwidth estimation failed. Group Policy processing will continue. Assuming fast link.", warning = true },
                    new { when = DateTime.UtcNow, source = "Drive Maps", text = "The user 'S:' preference item failed with error code 0x80070035 The network path was not found.", warning = false },
                },
            },
        });

        var row = (await admin.GetFromJsonAsync<JsonElement>("/api/clients"))[0].GetProperty("inventory").GetProperty("gp");
        var errors = (await admin.GetFromJsonAsync<JsonElement>($"/api/clients/{id}/inventory")).GetProperty("inventory").GetProperty("groupPolicy").GetProperty("errors");

        Assert.Equal(1, row.GetProperty("errors").GetInt32());
        Assert.Equal(0, row.GetProperty("warnings").GetInt32());
        Assert.Equal("Drive Maps", Assert.Single(errors.EnumerateArray()).GetProperty("source").GetString());
    }

    [Theory]
    [InlineData("Completed MDM Policy Extension Processing in 16 milliseconds.", true)]
    [InlineData("Completed Group Policy Registry Extension Processing in 1203 milliseconds.", true)]
    [InlineData("Group Policy bandwidth estimation failed. Group Policy processing will continue. Assuming fast link.", true)]
    [InlineData("MDM Policy failed: error 0x80180002.", false)]
    [InlineData("The processing of Group Policy failed. Windows could not resolve the computer name.", false)]
    [InlineData(null, false)]
    public void Only_the_two_known_non_errors_are_noise(string? text, bool noise) => Assert.Equal(noise, Inventories.GpNoise(text));
}
