using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>People workflows run by the real tools against a fake domain: fields filled in, steps in order, the first failure stopping the rest, and a run undone as one.</summary>
public sealed class PeopleWorkflowsTests : IDisposable
{
    private const string Lab = "OU=Lab,DC=amw,DC=com";
    private const string Staff = $"CN=Lab Staff,{Lab}";
    private const string Jordan = $"CN=Jordan Reyes,{Lab}";

    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-flows-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;
    private readonly FakeLdap ad = new();
    private readonly WebApplicationFactory<Program> host;

    public PeopleWorkflowsTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
        var live = new LiveLdap(ad);
        host = app.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<DirectoryTools>();
            s.AddSingleton(p => new DirectoryTools(p.GetRequiredService<DataFolder>(), p.GetRequiredService<SettingsStore>(), p.GetRequiredService<Accounts>(),
                p.GetRequiredService<IDomainFinder>(), live.Open) { AsComputer = null });
            s.RemoveAll<IDirectoryTools>();
            s.AddSingleton<IDirectoryTools>(p => p.GetRequiredService<DirectoryTools>());
        }));
    }

    public void Dispose()
    {
        host.Dispose();
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

    private Task Connect() => host.Services.GetRequiredService<DirectoryTools>().Connect(new SetupForm("amw.com", "svc-existing", "right", Existing: true, null, null, Lab), "admin", CancellationToken.None);

    private FakeLdap.Item? Row(string sam) => ad.Rows.FirstOrDefault(r => r.Values.TryGetValue("sAMAccountName", out var s) && (string) s[0] == sam);

    private static object Step(string kind, params (string Key, string Value)[] values) => new { kind, values = values.ToDictionary(v => v.Key, v => v.Value) };

    private static async Task<string> Save(HttpClient page, string name, object[] fields, object[] steps)
    {
        var r = await page.PostAsJsonAsync("/api/ad/workflows", new { name, on = true, fields, steps });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> Run(HttpClient page, string id, object body)
    {
        var r = await page.PostAsJsonAsync($"/api/ad/workflows/{id}/run", body);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task A_new_user_workflow_fills_in_its_fields_and_runs_its_steps_in_order()
    {
        await Connect();
        var admin = await SignedIn();
        var id = await Save(admin, "New user",
            [new { label = "First name", kind = "text", required = true }, new { label = "Last name", kind = "text", required = true },
                new { label = "Department", kind = "choice", required = true, choices = new[] { "Sales", "IT" } }, new { label = "Manager", kind = "user" }],
            [Step("create", ("name", "{First name} {Last name}"), ("logon", "{First name}.{Last name}"), ("ou", Lab)), Step("groups", ("groups", Staff)),
                Step("props", ("department", "{Department}")), Step("manager", ("manager", "{Manager}"))]);

        var run = await Run(admin, id, new { values = new Dictionary<string, string> { ["First name"] = "Maya", ["Last name"] = "Chen", ["Department"] = "Sales", ["Manager"] = "awiles" } });

        Assert.Equal("done", run.GetProperty("state").GetString());
        Assert.True(run.GetProperty("created").GetBoolean());
        var maya = Row("maya.chen");
        Assert.NotNull(maya);
        Assert.Equal("Sales", (string) maya!.Values["department"][0]);
        Assert.Equal($"CN=Anthony Wiles,{Lab}", (string) maya.Values["manager"][0]);
        Assert.Contains(maya.Dn, Row("Lab Staff")!.Values["member"].OfType<string>());
        Assert.Equal(4, run.GetProperty("changes").GetArrayLength());
        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/ad/workflows");
        var flow = listed.GetProperty("flows")[0];
        Assert.Equal("Create {First name} {Last name} ({First name}.{Last name}) in Lab", flow.GetProperty("steps")[0].GetProperty("sentence").GetString());
        Assert.Equal("Maya Chen", flow.GetProperty("runs")[0].GetProperty("userName").GetString());
    }

    [Fact]
    public async Task The_first_failure_stops_the_rest_and_a_run_is_undone_as_one()
    {
        await Connect();
        var admin = await SignedIn();
        var id = await Save(admin, "Close account", [],
            [Step("disable"), Step("props", ("title", "Former employee")), Step("move", ("ou", "OU=Nowhere,DC=amw,DC=com")), Step("expire", ("date", "2026-12-31"))]);

        var run = await Run(admin, id, new { user = "jreyes" });
        var states = run.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("state").GetString()).ToList();
        var changes = run.GetProperty("changes").EnumerateArray().Select(c => c.GetString()).ToList();

        Assert.Equal("failed", run.GetProperty("state").GetString());
        Assert.Equal(["done", "done", "failed", "skipped"], states);
        Assert.Equal(2, changes.Count);
        Assert.Equal("514", (string) ad.Rows.Single(r => r.Dn == Jordan).Values["userAccountControl"][0]);

        (await admin.PostAsJsonAsync("/api/changes/undo", new { ids = changes })).EnsureSuccessStatusCode();

        Assert.Equal("512", (string) ad.Rows.Single(r => r.Dn == Jordan).Values["userAccountControl"][0]);
        Assert.False(ad.Rows.Single(r => r.Dn == Jordan).Values.TryGetValue("title", out var t) && t.Length > 0);
    }

    [Fact]
    public async Task What_is_wrong_with_a_workflow_or_a_run_is_said_before_anything_changes()
    {
        await Connect();
        var admin = await SignedIn();
        var unknownField = await admin.PostAsJsonAsync("/api/ad/workflows", new { name = "Bad", on = true, fields = Array.Empty<object>(), steps = new[] { Step("manager", ("manager", "{Boss}")) } });
        var createLater = await admin.PostAsJsonAsync("/api/ad/workflows", new { name = "Bad", on = true, fields = Array.Empty<object>(), steps = new[] { Step("disable"), Step("create", ("name", "x"), ("logon", "x")) } });
        var id = await Save(admin, "Needs a name", [new { label = "Reason", kind = "text", required = true }], [Step("disable")]);
        var missing = await admin.PostAsJsonAsync($"/api/ad/workflows/{id}/run", new { user = "jreyes" });
        var unconfirmed = await (await SignedIn(confirmed: false)).PostAsJsonAsync($"/api/ad/workflows/{id}/run", new { user = "jreyes", values = new { Reason = "Left" } });

        Assert.Equal("{Boss} is not a field of this workflow.", (await unknownField.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, createLater.StatusCode);
        Assert.Equal("Enter Reason.", (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.OK, unconfirmed.StatusCode); // disable doesn't take Confirm it's you
        Assert.Contains(Permissions.AdWorkflows, Permissions.All);
    }

    private PlannedRunWorker Worker() => host.Services.GetServices<IHostedService>().OfType<PlannedRunWorker>().Single();

    [Fact]
    public async Task A_run_saved_for_a_date_waits_on_the_Lifecycle_page_and_runs_when_it_is_due_and_what_it_gave_is_on_record()
    {
        await Connect();
        var admin = await SignedIn();
        var id = await Save(admin, "New user", [new { label = "First name", kind = "text", required = true }, new { label = "Last name", kind = "text", required = true }],
            [Step("create", ("name", "{First name} {Last name}"), ("logon", "{First name}.{Last name}"), ("ou", Lab)), Step("groups", ("groups", Staff))]);
        var when = app.Clock.GetUtcNow().UtcDateTime.AddDays(7);

        var saved = await Run(admin, id, new { values = new Dictionary<string, string> { ["First name"] = "Alex", ["Last name"] = "Kim" }, when });
        var early = await Worker().Tick(CancellationToken.None);
        var waiting = (await admin.GetFromJsonAsync<JsonElement>("/api/lifecycle")).GetProperty("planned")[0];
        var notYet = Row("alex.kim");
        app.Clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        var ran = await Worker().Tick(CancellationToken.None);
        admin = await SignedIn(); // a week on, the first session has ended
        var done = (await admin.GetFromJsonAsync<JsonElement>("/api/lifecycle")).GetProperty("planned")[0];

        Assert.Equal("Alex Kim", saved.GetProperty("name").GetString());
        Assert.Equal(0, early);
        Assert.Null(notYet);
        Assert.Equal("scheduled", waiting.GetProperty("state").GetString());
        Assert.True(waiting.GetProperty("creates").GetBoolean());
        Assert.Equal("Create Alex Kim (Alex.Kim) in Lab", waiting.GetProperty("steps")[0].GetProperty("text").GetString());
        Assert.Equal(1, ran);
        Assert.Contains(Row("alex.kim")!.Dn, Row("Lab Staff")!.Values["member"].OfType<string>());
        Assert.Equal("done", done.GetProperty("state").GetString());
        Assert.Equal("alex.kim", done.GetProperty("user").GetString());
        // It is among the workflow's runs, and the person's record says what Rabbid Lab gave them.
        var flow = (await admin.GetFromJsonAsync<JsonElement>("/api/ad/workflows")).GetProperty("flows")[0];
        Assert.Equal("Alex Kim", flow.GetProperty("runs")[0].GetProperty("userName").GetString());
        var given = (await admin.GetFromJsonAsync<JsonElement>("/api/ad/users/alex.kim/given")).GetProperty("entries").EnumerateArray().ToList();
        Assert.Contains(given, e => e.GetProperty("kind").GetString() == "account");
        Assert.Contains(given, e => e.GetProperty("key").GetString() == Staff && e.GetProperty("what").GetString() == "Group Lab Staff");
    }

    [Fact]
    public async Task A_saved_run_can_be_cancelled_or_run_now_and_then_does_not_run_again_on_its_date()
    {
        await Connect();
        var admin = await SignedIn();
        var id = await Save(admin, "Close account", [], [Step("disable")]);
        var when = app.Clock.GetUtcNow().UtcDateTime.AddDays(2);
        var first = (await Run(admin, id, new { user = "jreyes", when })).GetProperty("planned").GetString();
        var second = (await Run(admin, id, new { user = "awiles", when })).GetProperty("planned").GetString();

        (await admin.PostAsync($"/api/lifecycle/planned/{first}/cancel", null)).EnsureSuccessStatusCode();
        var now = await admin.PostAsync($"/api/lifecycle/planned/{second}/run", null);
        var again = await admin.PostAsync($"/api/lifecycle/planned/{second}/run", null);
        app.Clock.Advance(TimeSpan.FromDays(3));
        var due = await Worker().Tick(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, now.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(0, due);
        Assert.Equal("512", (string) Row("jreyes")!.Values["userAccountControl"][0]); // cancelled: never disabled
        Assert.Equal("514", (string) Row("awiles")!.Values["userAccountControl"][0]);
    }

    private async Task<HttpClient> SignedIn(bool confirmed = true)
    {
        var page = host.CreateDefaultClient(new Uri("http://localhost"), new AutoMfa(app), new CookieContainerHandler());
        page.DefaultRequestHeaders.Add("X-Rabbid-Lab", "1");
        if (!(await page.PostAsJsonAsync("/api/setup/local", new { username = "admin", password = "correct horse" })).IsSuccessStatusCode)
        {
            (await page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "correct horse" })).EnsureSuccessStatusCode();
        }
        if (confirmed)
        {
            (await page.PostAsJsonAsync("/api/confirm", new { password = "correct horse", code = app.MfaSecrets.ContainsKey("admin") ? app.MfaCode("admin") : null })).EnsureSuccessStatusCode();
        }
        return page;
    }
}
