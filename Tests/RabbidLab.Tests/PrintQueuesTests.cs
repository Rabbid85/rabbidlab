using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// Print queues and their history: one read shared by everyone watching, job and printer actions as the service account
/// (refused without Manage print queues, or when Windows says no), and finished jobs read from the servers' logs and kept
/// as long as Settings says.
/// </summary>
public sealed class PrintQueuesTests : IDisposable
{
    private const string Front = @"\\print01.amw.com\Front";

    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-printq-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;
    private readonly FakeQueues queues = new();
    private readonly FakePrintLog printLog = new();
    private readonly WebApplicationFactory<Program> host;

    public PrintQueuesTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
        app.PrintProbe.Answers["PRINT01"] = new([new SharedPrinter("Front office", "PRINT01", Front, "HP PCL6", null, null, "IP_10.10.1.20", false, "10.10.1.20")]);
        host = app.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IPrintQueueProbe>();
            s.AddSingleton<IPrintQueueProbe>(queues);
            s.RemoveAll<IPrintLog>();
            s.AddSingleton<IPrintLog>(printLog);
            s.RemoveAll<IServiceLogin>();
            s.AddSingleton<IServiceLogin>(new FakeLogin());
            foreach (var worker in s.Where(d => d.ImplementationType == typeof(PrintHistoryWorker)).ToList())
            {
                s.Remove(worker);
            }
        }));
        queues.Queue[Front] = [new PrintJob(7, "Payroll summary.xlsx", "gsutton", "GREG-LT", "Queued", 6, 0, DateTime.UtcNow, 1), new PrintJob(8, "Floor plan.pdf", "lortiz", "LENA-PC", "Printing", 4, 3, DateTime.UtcNow, 2)];
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

    private PrintQueues Q => host.Services.GetRequiredService<PrintQueues>();

    // The print servers as found: PRINT01 shares Front office.
    private Task Found() => host.Services.GetRequiredService<PrintServers>().Read(() => Task.FromResult<List<AdServer>>([new("PRINT01", "print01.amw.com")]), again: true, CancellationToken.None);

    [Fact]
    public async Task A_queue_is_read_once_for_everyone_watching_and_only_for_printers_the_servers_share()
    {
        await Found();
        var admin = await SignedIn();

        var first = await admin.GetFromJsonAsync<JsonElement>($"/api/devices/printers/queue?share={Uri.EscapeDataString(Front)}");
        await admin.GetFromJsonAsync<JsonElement>($"/api/devices/printers/queue?share={Uri.EscapeDataString(Front)}");
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/devices/printers/jobs");
        var elsewhere = await admin.GetAsync($"/api/devices/printers/queue?share={Uri.EscapeDataString(@"\\evil\c$")}");

        Assert.Equal(["Payroll summary.xlsx", "Floor plan.pdf"], first.GetProperty("jobs").EnumerateArray().Select(j => j.GetProperty("document").GetString()));
        Assert.Equal(1, queues.Reads);
        Assert.Equal("Front office", all[0].GetProperty("printer").GetString());
        Assert.Equal(HttpStatusCode.NotFound, elsewhere.StatusCode);
        app.Clock.Advance(PrintQueues.Fresh);
        await admin.GetFromJsonAsync<JsonElement>($"/api/devices/printers/queue?share={Uri.EscapeDataString(Front)}");
        Assert.Equal(2, queues.Reads);
    }

    [Fact]
    public async Task Jobs_are_paused_and_cancelled_as_the_service_account_and_a_cancel_goes_in_the_history()
    {
        await Found();
        var admin = await SignedIn();

        var paused = await (await admin.PostAsJsonAsync("/api/devices/printers/jobs", new { share = Front, ids = new[] { 7 }, action = "pause" })).Content.ReadFromJsonAsync<JsonElement>();
        var cancelled = await admin.PostAsJsonAsync("/api/devices/printers/jobs", new { share = Front, ids = new[] { 8 }, action = "cancel" });
        var history = await admin.GetFromJsonAsync<JsonElement>("/api/devices/printers/history");

        Assert.Equal(1, paused.GetProperty("done").GetInt32());
        Assert.Equal([(7, "pause", @"AMW\svc-rabbidlab"), (8, "cancel", @"AMW\svc-rabbidlab")], queues.Done);
        cancelled.EnsureSuccessStatusCode();
        var row = history.GetProperty("rows")[0];
        Assert.Equal(("Floor plan.pdf", "Cancelled by admin"), (row.GetProperty("document").GetString(), row.GetProperty("result").GetString()));
    }

    [Fact]
    public async Task What_Windows_refuses_is_said_plainly_and_changes_need_Manage_print_queues()
    {
        await Found();
        var admin = await SignedIn();
        queues.Refuse = "Access denied. Give AMW\\svc-rabbidlab the Manage documents permission on \\\\print01.amw.com\\Front (printer properties, Security tab).";

        var refused = await (await admin.PostAsJsonAsync("/api/devices/printers/jobs", new { share = Front, ids = new[] { 7 }, action = "cancel" })).Content.ReadFromJsonAsync<JsonElement>();
        var purge = await admin.PostAsJsonAsync("/api/devices/printers/queue", new { share = Front, action = "purge" });
        var wrong = await admin.PostAsJsonAsync("/api/devices/printers/jobs", new { share = Front, ids = new[] { 7 }, action = "explode" });

        Assert.Equal(0, refused.GetProperty("done").GetInt32());
        Assert.Contains("Manage documents permission", refused.GetProperty("problems")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, purge.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains(Permissions.SeatsPrinters, Permissions.All);
    }

    [Fact]
    public async Task Finished_jobs_come_from_each_servers_log_once_and_are_kept_for_the_days_picked()
    {
        await Found();
        var admin = await SignedIn();
        var at = app.Clock.GetUtcNow().UtcDateTime;
        printLog.Events.Add(Event(41, at.AddHours(-2), "Board pack.pdf", "gsutton", @"\\GREG-LT", "Front office", 30));
        printLog.Events.Add(Event(42, at.AddDays(-20), "Old memo.docx", "lortiz", @"\\LENA-PC", "Front office", 2));

        Assert.Equal(2, await Q.Collect(CancellationToken.None));
        Assert.Equal(0, await Q.Collect(CancellationToken.None)); // read from where it left off
        Assert.Contains("EventRecordID > 42", printLog.Queries[^1]);
        (await admin.PostAsJsonAsync("/api/devices/printers/history/keep", new { days = 15 })).EnsureSuccessStatusCode();
        var history = await admin.GetFromJsonAsync<JsonElement>($"/api/devices/printers/history?share={Uri.EscapeDataString(Front)}");
        var refused = await admin.PostAsJsonAsync("/api/devices/printers/history/keep", new { days = 7 });

        var row = Assert.Single(history.GetProperty("rows").EnumerateArray());
        Assert.Equal(("Board pack.pdf", "gsutton", "GREG-LT", 30, "Printed"), (row.GetProperty("document").GetString(), row.GetProperty("owner").GetString(),
            row.GetProperty("computer").GetString(), row.GetProperty("pages").GetInt32(), row.GetProperty("result").GetString()));
        Assert.Equal(15, history.GetProperty("keep").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_log_that_is_off_says_how_to_turn_it_on()
    {
        await Found();
        var admin = await SignedIn();
        printLog.Fails = "The PrintService Operational log is turned off on print01.amw.com. On the print server, run: wevtutil sl Microsoft-Windows-PrintService/Operational /e:true";

        await Q.Collect(CancellationToken.None);
        var history = await admin.GetFromJsonAsync<JsonElement>("/api/devices/printers/history");

        Assert.Contains("wevtutil sl", history.GetProperty("servers")[0].GetProperty("problem").GetString());
    }

    [Fact]
    public void A_jobs_status_bits_read_as_one_word()
    {
        Assert.Equal("Queued", WindowsPrintQueueProbe.State(0));
        Assert.Equal("Printing", WindowsPrintQueueProbe.State(0x10));
        Assert.Equal("Paused", WindowsPrintQueueProbe.State(0x1));
        Assert.Equal("Error", WindowsPrintQueueProbe.State(0x12));
        Assert.Equal("Out of paper", WindowsPrintQueueProbe.State(0x50));
    }

    private static string Event(long record, DateTime at, string document, string owner, string computer, string printer, int pages) =>
        $"<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\"><System><EventID>307</EventID><TimeCreated SystemTime=\"{at:yyyy-MM-ddTHH:mm:ss.fffffffZ}\"/>"
        + $"<EventRecordID>{record}</EventRecordID></System><UserData><DocumentPrinted xmlns=\"http://manifests.microsoft.com/win/2005/08/windows/printing/spooler/core/events\">"
        + $"<Param1>5</Param1><Param2>{document}</Param2><Param3>{owner}</Param3><Param4>{computer}</Param4><Param5>{printer}</Param5><Param6>IP_10.10.1.20</Param6><Param7>1024</Param7>"
        + $"<Param8>{pages}</Param8></DocumentPrinted></UserData></Event>";

    private async Task<HttpClient> SignedIn()
    {
        var page = host.CreateDefaultClient(new Uri("http://localhost"), new AutoMfa(app), new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler());
        page.DefaultRequestHeaders.Add("X-Rabbid-Lab", "1");
        if (!(await page.PostAsJsonAsync("/api/setup/local", new { username = "admin", password = "correct horse" })).IsSuccessStatusCode)
        {
            (await page.PostAsJsonAsync("/api/signin", new { username = "admin", password = "correct horse" })).EnsureSuccessStatusCode();
        }
        return page;
    }

    private sealed class FakeLogin : IServiceLogin
    {
        public (string Username, string Password)? Login() => (@"AMW\svc-rabbidlab", "right");
    }

    /// <summary>Queues as the test says; what was done to them, and as whom.</summary>
    private sealed class FakeQueues : IPrintQueueProbe
    {
        public Dictionary<string, List<PrintJob>> Queue { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(int, string, string)> Done { get; } = [];
        public string? Refuse { get; set; }
        public int Reads { get; private set; }

        public (List<PrintJob> Jobs, string? Problem) Jobs(string share)
        {
            Reads++;
            return (Queue.GetValueOrDefault(share) ?? [], null);
        }

        public string? Job(string share, int id, string action, string user, string password)
        {
            if (Refuse is not null)
            {
                return Refuse;
            }
            Done.Add((id, action, user));
            return null;
        }

        public string? Printer(string share, string action, string user, string password) => Refuse ?? "The spooler on print01 is not running.";
    }

    /// <summary>A log that answers with the events the test put in, after the record asked for.</summary>
    private sealed class FakePrintLog : IPrintLog
    {
        public List<string> Events { get; } = [];
        public List<string> Queries { get; } = [];
        public string? Fails { get; set; }

        public IReadOnlyList<string> Read(string host, string user, string password, string query, int most)
        {
            Queries.Add(query);
            if (Fails is not null)
            {
                throw new PrintLogProblem(Fails);
            }
            var after = System.Text.RegularExpressions.Regex.Match(query, @"EventRecordID > (\d+)") is { Success: true } m ? long.Parse(m.Groups[1].Value) : 0;
            return [.. Events.Where(e => long.Parse(System.Text.RegularExpressions.Regex.Match(e, @"<EventRecordID>(\d+)<").Groups[1].Value) > after)];
        }
    }
}
