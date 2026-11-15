using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Scripts, jobs and workflows on the computers in Devices: who may run what, how a job reaches its computer, and what starts a workflow.</summary>
public sealed class DevicesTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-devices-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public DevicesTests()
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

    private static async Task<string> Id(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await Json(r)).GetProperty("id").GetString()!;
    }

    /// <summary>An administrator who has just confirmed who they are, with computers joining without approval.</summary>
    private async Task<HttpClient> Admin(bool approve = false)
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/clients/settings", new { approve })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/confirm", new { password = "correct horse" })).EnsureSuccessStatusCode();
        return admin;
    }

    /// <summary>Minutes pass with the page open, so its session stays: one ends five minutes after its page was last heard from.</summary>
    private async Task Later(HttpClient page, int minutes)
    {
        for (; minutes > 0; minutes -= 2)
        {
            app.Clock.Advance(TimeSpan.FromMinutes(Math.Min(2, minutes)));
            (await page.GetAsync("/api/confirm")).EnsureSuccessStatusCode();
        }
    }

    /// <summary>A computer's client: joined, and signing what it sends.</summary>
    private async Task<(string Id, HttpClient Client)> Computer(string computer = "SHOP-PC")
    {
        var j = await Json(await app.CreateClient().PostAsJsonAsync("/api/clients/join", new { computer, os = "Windows 11 Pro", version = "1.1.168" }));
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("RabbidLab-Client", $"{j.GetProperty("id").GetString()}:{j.GetProperty("secret").GetString()}");
        return (j.GetProperty("id").GetString()!, client);
    }

    private static async Task<List<JsonElement>> Take(HttpClient client) =>
        [.. (await Json(await client.PostAsync("/api/clients/jobs/take", null))).GetProperty("jobs").EnumerateArray()];

    private static async Task<List<JsonElement>> Jobs(HttpClient admin) => [.. (await admin.GetFromJsonAsync<JsonElement>("/api/devices/jobs")).EnumerateArray()];

    private static async Task<string> Script(HttpClient admin, string name = "Flush DNS", string text = "ipconfig /flushdns") =>
        await Id(await admin.PostAsJsonAsync("/api/devices/scripts", new { name, language = "cmd", text, minutes = 5 }));

    [Fact]
    public async Task A_printer_goes_out_as_a_job_with_what_was_typed_checked_and_quoted()
    {
        var admin = await Admin();
        var (id, client) = await Computer();

        var ipp = await Id(await admin.PostAsJsonAsync("/api/devices/run", new { kind = "printer", clients = new[] { id }, printer = new { action = "add", way = "ipp", name = "Bob's printer", address = "192.168.3.20" } }));
        var share = await Id(await admin.PostAsJsonAsync("/api/devices/run", new { kind = "printer", clients = new[] { id }, printer = new { action = "add", way = "share", share = @"\\dc01\Office" } }));
        var badShare = await admin.PostAsJsonAsync("/api/devices/run", new { kind = "printer", clients = new[] { id }, printer = new { action = "add", way = "share", share = "dc01; Remove-Item" } });
        var badHost = await admin.PostAsJsonAsync("/api/devices/run", new { kind = "printer", clients = new[] { id }, printer = new { action = "add", way = "tcpip", name = "X", address = "1.2.3.4; calc", driver = "Generic / Text Only" } });
        var noDriver = await admin.PostAsJsonAsync("/api/devices/run", new { kind = "printer", clients = new[] { id }, printer = new { action = "add", way = "lpr", name = "X", address = "printer.amw.com", queue = "lp" } });
        var taken = await Take(client);
        var log = await (await admin.GetAsync("/api/activity")).Content.ReadAsStringAsync();

        var first = taken.Single(j => j.GetProperty("id").GetString() == ipp).GetProperty("steps")[0].GetProperty("text").GetString()!;
        Assert.Contains("-IppURL 'http://192.168.3.20:631/ipp/print'", first);
        Assert.Contains("$name = 'Bob''s printer'", first); // a quote in the name stays inside the string
        Assert.Contains("/ga", taken.Single(j => j.GetProperty("id").GetString() == share).GetProperty("steps")[0].GetProperty("text").GetString()!);
        Assert.Equal(HttpStatusCode.BadRequest, badShare.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badHost.StatusCode);
        Assert.Contains("driver", await noDriver.Content.ReadAsStringAsync());
        Assert.Contains("Add the printer Bob's printer on SHOP-PC", log.Replace("\\u0027", "'"));
    }

    [Fact]
    public async Task Running_a_script_takes_the_right_to_and_confirming_who_you_are()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/clients/settings", new { approve = false })).EnsureSuccessStatusCode();
        var (id, _) = await Computer();
        var run = new { kind = "script", clients = new[] { id }, language = "powershell", text = "Get-Date" };

        // Signed in isn't enough: the page is told what to ask for.
        var first = await admin.PostAsJsonAsync("/api/devices/run", run);
        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);
        Assert.Equal("password", (await Json(first)).GetProperty("confirm").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/confirm", new { password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/devices/run", run)).StatusCode);
        (await admin.PostAsJsonAsync("/api/confirm", new { password = "correct horse" })).EnsureSuccessStatusCode();
        await Id(await admin.PostAsJsonAsync("/api/devices/run", run));

        // Ten minutes later it's asked again.
        await Later(admin, 11);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/devices/run", run)).StatusCode);

        // Someone whose roles don't allow it gets nowhere, confirmed or not.
        app.Services.GetRequiredService<Accounts>().Add("mlopez", "user password", Roles.User);
        var user = app.Page();
        (await user.PostAsJsonAsync("/api/signin", new { username = "mlopez", password = "user password" })).EnsureSuccessStatusCode();
        (await user.PostAsJsonAsync("/api/confirm", new { password = "user password" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/devices/run", run)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/devices/run", new { kind = "restart", clients = new[] { id } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/devices/jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/devices/scripts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/devices/scripts", new { name = "x", language = "cmd", text = "ver" })).StatusCode);
    }

    [Fact]
    public async Task With_MFA_on_the_confirmation_is_a_code_from_the_app()
    {
        var admin = await app.SignedIn();
        var secret = (await Json(await admin.PostAsJsonAsync("/api/mfa/setup", new { }))).GetProperty("secret").GetString()!;
        (await admin.PostAsJsonAsync("/api/mfa/on", new { code = Totp.Code(secret, Totp.Step(app.Clock.GetUtcNow())) })).EnsureSuccessStatusCode();

        Assert.Equal("code", (await admin.GetFromJsonAsync<JsonElement>("/api/confirm")).GetProperty("method").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/confirm", new { password = "correct horse" })).StatusCode); // the password isn't asked for any more
        app.Clock.Advance(TimeSpan.FromSeconds(31)); // a code is taken once
        (await admin.PostAsJsonAsync("/api/confirm", new { code = Totp.Code(secret, Totp.Step(app.Clock.GetUtcNow())) })).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.String, (await admin.GetFromJsonAsync<JsonElement>("/api/confirm")).GetProperty("until").ValueKind);
    }

    [Fact]
    public async Task A_job_is_handed_to_its_computer_once_and_keeps_what_each_step_wrote()
    {
        var admin = await Admin();
        var (id, client) = await Computer();
        var script = await Script(admin);
        var job = await Id(await admin.PostAsJsonAsync("/api/devices/run", new { kind = "script", clients = new[] { id }, script }));

        // The client's open request is told jobs wait, and it takes them.
        var order = await Json(await client.PostAsync("/api/clients/next?jobs=1", null));
        Assert.Equal("jobs", order.GetProperty("action").GetString());
        var taken = await Take(client);
        Assert.Equal(job, Assert.Single(taken).GetProperty("id").GetString());
        var step = Assert.Single(taken[0].GetProperty("steps").EnumerateArray());
        Assert.Equal("cmd", step.GetProperty("language").GetString());
        Assert.Equal("ipconfig /flushdns\r\n", step.GetProperty("text").GetString());
        Assert.Empty(await Take(client));
        Assert.False(app.Services.GetRequiredService<DeviceJobs>().Waits(id));
        Assert.Equal(1, (await Jobs(admin))[0].GetProperty("states").GetProperty("running").GetInt32());

        // Its answer: the job is done, and the output can be read.
        (await client.PostAsJsonAsync("/api/clients/jobs/done", new { id = job, steps = new[] { new { exit = 0, output = "Successfully flushed the DNS Resolver Cache.", seconds = 0.4 } } })).EnsureSuccessStatusCode();
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/devices/jobs/{job}");
        Assert.Equal("done", detail.GetProperty("targets")[0].GetProperty("state").GetString());
        Assert.Equal("ipconfig /flushdns\r\n", detail.GetProperty("steps")[0].GetProperty("text").GetString());
        Assert.Equal("Successfully flushed the DNS Resolver Cache.", (await admin.GetFromJsonAsync<JsonElement>($"/api/devices/jobs/{job}/output/{id}")).GetProperty("output")[0].GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/clients/jobs/done", new { id = job })).StatusCode); // answered once

        // Who ran what is in the activity log, with the script's SHA-256.
        var ran = (await admin.GetFromJsonAsync<JsonElement>("/api/activity")).EnumerateArray().First(e => e.GetProperty("action").GetString() == "job.run");
        Assert.Equal("Ran the script Flush DNS on SHOP-PC", ran.GetProperty("title").GetString());
        Assert.Contains("SHA-256", ran.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_failed_step_a_refusal_and_another_computers_answer_each_show_as_what_they_are()
    {
        var admin = await Admin();
        var (one, first) = await Computer("ONE");
        var (two, second) = await Computer("TWO");
        var job = await Id(await admin.PostAsJsonAsync("/api/devices/run", new { kind = "updates", clients = new[] { one, two } }));
        Assert.Single(await Take(first));
        Assert.Single(await Take(second));

        (await first.PostAsJsonAsync("/api/clients/jobs/done", new { id = job, steps = new[] { new { exit = 1, output = "Failed (0x80240017)", seconds = 9 } } })).EnsureSuccessStatusCode();
        (await second.PostAsJsonAsync("/api/clients/jobs/done", new { id = job, refused = true, problem = "Scripts and actions are turned off by Group Policy on TWO." })).EnsureSuccessStatusCode();

        var targets = (await admin.GetFromJsonAsync<JsonElement>($"/api/devices/jobs/{job}")).GetProperty("targets").EnumerateArray().ToDictionary(t => t.GetProperty("computer").GetString()!);
        Assert.Equal("failed", targets["ONE"].GetProperty("state").GetString());
        Assert.Equal("Install Windows updates ended with exit code 1.", targets["ONE"].GetProperty("problem").GetString());
        Assert.Equal("refused", targets["TWO"].GetProperty("state").GetString());
        Assert.Equal("refused", (await admin.GetFromJsonAsync<JsonElement>($"/api/devices/jobs?client={two}"))[0].GetProperty("state").GetString());
    }

    [Fact]
    public async Task More_than_ten_computers_takes_typing_how_many()
    {
        var admin = await Admin();
        List<string> ids = [];
        for (var i = 1; i <= 11; i++)
        {
            ids.Add((await Computer($"PC-{i:00}")).Id);
        }
        var held = await admin.PostAsJsonAsync("/api/devices/run", new { kind = "restart", clients = ids });
        Assert.Equal(HttpStatusCode.BadRequest, held.StatusCode);
        Assert.Equal(11, (await Json(held)).GetProperty("count").GetInt32());
        Assert.Empty(await Jobs(admin));

        await Id(await admin.PostAsJsonAsync("/api/devices/run", new { kind = "restart", clients = ids, count = 11 }));
        Assert.Equal(11, Assert.Single(await Jobs(admin)).GetProperty("computers").GetInt32());
    }

    [Fact]
    public async Task A_job_nobody_takes_expires_one_never_answered_is_given_up_on_and_waiting_ones_can_be_cancelled()
    {
        var admin = await Admin();
        var (id, client) = await Computer();
        var jobs = app.Services.GetRequiredService<DeviceJobs>();
        async Task<string?> State(string job) => (await admin.GetFromJsonAsync<JsonElement>($"/api/devices/jobs/{job}")).GetProperty("targets")[0].GetProperty("state").GetString();

        var cancelled = await Id(await admin.PostAsJsonAsync("/api/devices/run", new { kind = "restart", clients = new[] { id } }));
        Assert.Equal(1, (await Json(await admin.PostAsync($"/api/devices/jobs/{cancelled}/cancel", null))).GetProperty("cancelled").GetInt32());
        Assert.Equal("cancelled", await State(cancelled));
        Assert.Empty(await Take(client));

        var late = await Id(await admin.PostAsJsonAsync("/api/devices/run", new { kind = "restart", clients = new[] { id } }));
        app.Clock.Advance(TimeSpan.FromHours(25));
        jobs.Sweep();
        admin = await Admin(); // a day on, signed in again
        Assert.Equal("expired", await State(late));
        Assert.Empty(await Take(client));

        var silent = await Id(await admin.PostAsJsonAsync("/api/devices/run", new { kind = "restart", clients = new[] { id } }));
        Assert.Single(await Take(client));
        await Later(admin, 5);
        jobs.Sweep();
        Assert.Equal("running", await State(silent));
        await Later(admin, 10); // its two minutes and the ten of grace are up
        jobs.Sweep();
        Assert.Equal("noanswer", await State(silent));
    }

    [Fact]
    public async Task A_workflow_runs_its_steps_in_order_when_a_computer_is_approved()
    {
        var admin = await Admin(approve: true);
        var script = await Script(admin, "Install Chrome", "winget install Google.Chrome");
        await Id(await admin.PostAsJsonAsync("/api/devices/workflows", new { name = "New computer", on = true, when = "approved", steps = new object[] { new { kind = "script", script }, new { kind = "updates" }, new { kind = "restart-if-needed" } } }));
        await Id(await admin.PostAsJsonAsync("/api/devices/workflows", new { name = "Off", on = false, when = "approved", steps = new object[] { new { kind = "restart" } } }));

        var (id, client) = await Computer();
        Assert.Empty(await Jobs(admin)); // waiting for approval
        (await admin.PostAsync($"/api/clients/{id}/approve", null)).EnsureSuccessStatusCode();

        var job = Assert.Single(await Jobs(admin));
        Assert.Equal("workflow", job.GetProperty("kind").GetString());
        Assert.Equal("New computer", job.GetProperty("title").GetString());
        var steps = Assert.Single(await Take(client)).GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(3, steps.Count);
        Assert.Equal("winget install Google.Chrome\r\n", steps[0].GetProperty("text").GetString());
        Assert.Contains("Microsoft.Update.Session", steps[1].GetProperty("text").GetString());

        // A script a workflow runs can't be deleted from under it.
        var delete = await admin.PostAsync($"/api/devices/scripts/{script}/delete", null);
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Contains("New computer", (await Json(delete)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_scheduled_workflow_runs_once_at_its_time_on_the_computers_it_fits()
    {
        var admin = await Admin();
        await Computer("SHOP-1");
        await Computer("OFFICE-1");
        var at = app.Clock.GetLocalNow().AddMinutes(1);
        await Id(await admin.PostAsJsonAsync("/api/devices/workflows", new
        {
            name = "Patch night", on = true, when = "schedule", days = new[] { (int) at.DayOfWeek }, time = at.ToString("HH:mm"),
            @if = "name", ifText = "shop-*", steps = new object[] { new { kind = "updates" }, new { kind = "restart-if-needed" } },
        }));
        var workflows = app.Services.GetRequiredService<Workflows>();

        workflows.Tick();
        Assert.Empty(await Jobs(admin));
        app.Clock.Advance(TimeSpan.FromMinutes(1));
        workflows.Tick();
        workflows.Tick();

        var job = Assert.Single(await Jobs(admin));
        Assert.Equal(1, job.GetProperty("computers").GetInt32());
        Assert.Equal("SHOP-1", job.GetProperty("names")[0].GetString());
        Assert.Equal("Workflow", job.GetProperty("by").GetString());
    }

    [Fact]
    public async Task Low_disk_space_starts_its_workflow_when_the_drive_drops_below_and_not_again_while_it_stays_there()
    {
        var admin = await Admin();
        var (_, client) = await Computer();
        await Id(await admin.PostAsJsonAsync("/api/devices/workflows", new { name = "Clean up", on = true, when = "disk", below = 10, steps = new object[] { new { kind = "script", script = await Script(admin, "Clear temp", "del /q %TEMP%\\*") } } }));
        static object Disk(long free) => new { disks = new[] { new { name = "C:", total = 100L, free } } };

        (await client.PostAsJsonAsync("/api/clients/inventory", Disk(50))).EnsureSuccessStatusCode();
        Assert.Empty(await Jobs(admin));
        (await client.PostAsJsonAsync("/api/clients/inventory", Disk(5))).EnsureSuccessStatusCode();
        Assert.Single(await Jobs(admin));
        app.Clock.Advance(TimeSpan.FromHours(1));
        (await client.PostAsJsonAsync("/api/clients/inventory", Disk(4))).EnsureSuccessStatusCode();
        Assert.Single(await Jobs(await Admin()));
    }

    [Fact]
    public async Task A_workflow_can_only_hold_steps_its_maker_may_run_by_hand_and_run_now_takes_confirming()
    {
        var admin = await Admin();
        var (id, _) = await Computer();
        var script = await Script(admin);
        var patch = await Id(await admin.PostAsJsonAsync("/api/devices/workflows", new { name = "Patch", on = false, when = "updates", steps = new object[] { new { kind = "updates" } } }));

        // A role with workflows and restarts, but not scripts.
        var made = await admin.PostAsJsonAsync("/api/roles", new { name = "Patchers", color = "#5FD4A0" });
        var role = (await Json(made)).GetProperty("id").GetString()!;
        (await admin.PostAsJsonAsync($"/api/roles/{role}", new { name = "Patchers", color = "#5FD4A0", permissions = new[] { "seats.view", "seats.actions", "seats.workflows" } })).EnsureSuccessStatusCode();
        app.Services.GetRequiredService<Accounts>().Add("pat", "patch password", Roles.User);
        (await admin.PostAsJsonAsync($"/api/roles/{role}/members", new { kind = "local", name = "pat" })).EnsureSuccessStatusCode();
        var pat = app.Page();
        (await pat.PostAsJsonAsync("/api/signin", new { username = "pat", password = "patch password" })).EnsureSuccessStatusCode();
        (await pat.PostAsJsonAsync("/api/confirm", new { password = "patch password" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await pat.PostAsJsonAsync("/api/devices/workflows", new { name = "Sneaky", on = true, when = "online", steps = new object[] { new { kind = "script", script } } })).StatusCode);
        await Id(await pat.PostAsJsonAsync("/api/devices/workflows", new { name = "Restarts", on = true, when = "restart", steps = new object[] { new { kind = "restart" } } }));

        // Run now starts it on every approved computer it fits, by the person who clicked.
        await Later(admin, 11);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"/api/devices/workflows/{patch}/run", null)).StatusCode);
        (await admin.PostAsJsonAsync("/api/confirm", new { password = "correct horse" })).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/devices/workflows/{patch}/run", null)).EnsureSuccessStatusCode();
        var job = Assert.Single(await Jobs(admin));
        Assert.Equal("admin", job.GetProperty("by").GetString());
        Assert.Equal(id, (await admin.GetFromJsonAsync<JsonElement>($"/api/devices/jobs/{job.GetProperty("id").GetString()}")).GetProperty("targets")[0].GetProperty("client").GetString());
    }
}
