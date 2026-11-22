using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Xml.Linq;

namespace RabbidLab.Server;

/// <summary>A document in a print queue: its job ID, who sent it from where, how far it got, and its state in a word.</summary>
public sealed record PrintJob(int Id, string Document, string? Owner, string? Computer, string State, int Pages, int Printed, DateTime? Submitted, int Position);

/// <summary>One queue as last read: its jobs, or why it couldn't be read, and when.</summary>
public sealed record QueueRead(string Share, string Printer, string Server, List<PrintJob> Jobs, string? Problem, DateTime At);

/// <summary>A job that finished: printed (the server's PrintService log) or cancelled here, by whom and when.</summary>
public sealed record PrintedJob(DateTime At, string Server, string Printer, string Document, string? Owner, string? Computer, int Pages, string Result, long Record = 0);

/// <summary>A print server's history: the last log record read, and why it couldn't be read, when it couldn't.</summary>
public sealed class PrintLogState
{
    public long Last { get; set; }
    public string? Problem { get; set; }
    public DateTime? At { get; set; }
}

public sealed class PrintHistoryFile
{
    public int Keep { get; set; } = PrintQueues.KeepDefault;
    public Dictionary<string, PrintLogState> Servers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record JobActionForm(string? Share, List<int>? Ids, string? Action);

public sealed record QueueActionForm(string? Share, string? Action);

public sealed record KeepForm(int Days);

/// <summary>A print server's queues: reading them as this server, and changing jobs and printers as the service account.</summary>
public interface IPrintQueueProbe
{
    /// <summary>The jobs in a queue (\\server\share), or why they couldn't be read.</summary>
    (List<PrintJob> Jobs, string? Problem) Jobs(string share);

    /// <summary>pause, resume, restart or cancel one job, signed in as user. Why it didn't work, or null.</summary>
    string? Job(string share, int id, string action, string user, string password);

    /// <summary>pause, resume or purge (cancel every job) a printer, signed in as user. Why it didn't work, or null.</summary>
    string? Printer(string share, string action, string user, string password);
}

/// <summary>Reads a print server's PrintService Operational log, signed in as an account. Tests stand in for it.</summary>
public interface IPrintLog
{
    /// <summary>The events' XML, oldest first. Throws PrintLogProblem when the log can't be read.</summary>
    IReadOnlyList<string> Read(string host, string user, string password, string query, int most);
}

public sealed class PrintLogProblem(string message) : Exception(message);

public sealed class NoPrintQueueProbe : IPrintQueueProbe
{
    private const string Not = "Print queues can only be read when Rabbid Lab runs on Windows.";

    public (List<PrintJob> Jobs, string? Problem) Jobs(string share) => ([], Not);

    public string? Job(string share, int id, string action, string user, string password) => Not;

    public string? Printer(string share, string action, string user, string password) => Not;
}

public sealed class NoPrintLog : IPrintLog
{
    public IReadOnlyList<string> Read(string host, string user, string password, string query, int most) => throw new PrintLogProblem("Print history can only be read when Rabbid Lab runs on Windows.");
}

/// <summary>
/// The print queues of the printers the print servers share (PrintServers.cs): read as this server's computer account,
/// kept a few seconds so many people watching cost one read, and changed as the service account, since changing other
/// people's jobs takes Manage documents on the printer. The history comes from each server's PrintService log (event 307),
/// read every few minutes as the service account and kept for the days picked in Settings (print-history.jsonl).
/// </summary>
public sealed class PrintQueues(IPrintQueueProbe probe, IPrintLog printLog, PrintServers servers, IServiceLogin service, DataFolder folder, TimeProvider clock,
    ILogger<PrintQueues> log)
{
    public const int KeepDefault = 30;
    public static readonly int[] KeepChoices = [5, 15, 30, 60, 90];
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(4);
    private const int AtOnce = 6;
    private const int MostRead = 2000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, QueueRead> kept = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock gate = new();
    private readonly JsonFile<PrintHistoryFile> state = folder.Json<PrintHistoryFile>("print-history.json", new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    private readonly string rows = Path.Combine(folder.Path, "print-history.jsonl");
    private readonly Lock rowsGate = new();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>A printer a server shares, by its share as the page has it; null when it isn't one (nothing else is ever asked).</summary>
    public SharedPrinter? Find(string? share) => servers.Known().Printers.FirstOrDefault(p => string.Equals(p.Share, share?.Trim(), StringComparison.OrdinalIgnoreCase));

    public async Task<QueueRead> Read(SharedPrinter p)
    {
        lock (gate)
        {
            if (kept.TryGetValue(p.Share, out var k) && Now - k.At < Fresh)
            {
                return k;
            }
        }
        var (jobs, problem) = await Task.Run(() => probe.Jobs(p.Share));
        var read = new QueueRead(p.Share, p.Name, p.Server, [.. jobs.OrderBy(j => j.Position)], problem, Now);
        lock (gate)
        {
            kept[p.Share] = read;
        }
        return read;
    }

    /// <summary>Every shared printer's queue, a few at a time.</summary>
    public async Task<List<QueueRead>> ReadAll()
    {
        var all = servers.Known().Printers;
        var reads = new QueueRead[all.Count];
        using var limit = new SemaphoreSlim(AtOnce);
        await Task.WhenAll(all.Select(async (p, i) =>
        {
            await limit.WaitAsync();
            try
            {
                reads[i] = await Read(p);
            }
            finally
            {
                limit.Release();
            }
        }));
        return [.. reads];
    }

    private (string User, string Password) Login() =>
        service.Login() ?? throw new ToolProblem("The Active Directory service account is not configured. Configure it first, then give it Manage documents on the printers.", StatusCodes.Status409Conflict);

    /// <summary>What each job's action came to: null for done, or why not. Cancelled jobs go in the history as cancelled here.</summary>
    public async Task<Dictionary<int, string?>> Act(SharedPrinter p, IReadOnlyList<int> ids, string action, string by)
    {
        var (user, password) = Login();
        var before = (await Read(p)).Jobs;
        var said = new Dictionary<int, string?>();
        foreach (var id in ids.Distinct())
        {
            said[id] = await Task.Run(() => probe.Job(p.Share, id, action, user, password));
            if (said[id] is null && action == "cancel" && before.FirstOrDefault(j => j.Id == id) is { } j)
            {
                Keep([new PrintedJob(Now, p.Server, p.Name, j.Document, j.Owner, j.Computer, j.Pages, $"Cancelled by {by}")]);
            }
        }
        Forget(p);
        return said;
    }

    public async Task<string?> Printer(SharedPrinter p, string action)
    {
        var (user, password) = Login();
        var said = await Task.Run(() => probe.Printer(p.Share, action, user, password));
        Forget(p);
        return said;
    }

    private void Forget(SharedPrinter p)
    {
        lock (gate)
        {
            kept.Remove(p.Share);
        }
    }

    // --- History -------------------------------------------------------------------------------------------------

    public PrintHistoryFile State()
    {
        lock (state.Gate)
        {
            var f = state.Read();
            return new PrintHistoryFile { Keep = f.Keep, Servers = new(f.Servers, StringComparer.OrdinalIgnoreCase) };
        }
    }

    public void SetKeep(int days)
    {
        lock (state.Gate)
        {
            var f = state.Read();
            f.Keep = days;
            state.Save(f);
        }
        Prune();
    }

    /// <summary>The jobs that finished, newest first, for one printer or all of them.</summary>
    public List<PrintedJob> History(string? server = null, string? printer = null)
    {
        lock (rowsGate)
        {
            if (!File.Exists(rows))
            {
                return [];
            }
            return [.. File.ReadLines(rows).Select(Row).OfType<PrintedJob>()
                .Where(r => (server is null || string.Equals(r.Server, server, StringComparison.OrdinalIgnoreCase)) && (printer is null || string.Equals(r.Printer, printer, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(r => r.At)];
        }
    }

    private static PrintedJob? Row(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<PrintedJob>(line, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Keep(IEnumerable<PrintedJob> list)
    {
        lock (rowsGate)
        {
            File.AppendAllLines(rows, list.Select(r => JsonSerializer.Serialize(r, Json)));
        }
    }

    // Rows older than the days kept go.
    private void Prune()
    {
        var keep = State().Keep;
        lock (rowsGate)
        {
            if (!File.Exists(rows))
            {
                return;
            }
            var all = File.ReadAllLines(rows);
            var left = all.Where(l => Row(l) is { } r && r.At >= Now.AddDays(-keep)).ToList();
            if (left.Count != all.Length)
            {
                File.WriteAllLines(rows, left);
            }
        }
    }

    /// <summary>One look at every print server's log: what printed since the last look. How many were added.</summary>
    public async Task<int> Collect(CancellationToken stop)
    {
        var known = servers.Known().Servers;
        if (known.Count == 0 || service.Login() is not { } login)
        {
            return 0;
        }
        var (user, password) = login;
        var added = 0;
        foreach (var s in known)
        {
            stop.ThrowIfCancellationRequested();
            var at = State().Servers.GetValueOrDefault(s.Host)?.Last ?? 0;
            // The first look takes only what's within the days kept.
            var query = at > 0 ? $"*[System[(EventID=307) and (EventRecordID > {at})]]"
                : $"*[System[(EventID=307) and TimeCreated[timediff(@SystemTime) <= {(long) TimeSpan.FromDays(State().Keep).TotalMilliseconds}]]]";
            PrintLogState now;
            try
            {
                var events = await Task.Run(() => printLog.Read(s.Host, user, password, query, MostRead), stop);
                var found = events.Select(x => Parse(x, s.Server)).OfType<PrintedJob>().ToList();
                Keep(found);
                added += found.Count;
                now = new PrintLogState { Last = found.Select(r => r.Record).DefaultIfEmpty(at).Max(), At = Now };
            }
            catch (PrintLogProblem e)
            {
                now = new PrintLogState { Last = at, Problem = e.Message, At = Now };
                log.LogInformation("The print history on {Server} could not be read: {Why}", s.Server, e.Message);
            }
            lock (state.Gate)
            {
                var f = state.Read();
                f.Servers[s.Host] = now;
                state.Save(f);
            }
        }
        Prune();
        return added;
    }

    /// <summary>Event 307, Document printed: Param2 is the document, 3 the owner, 4 the computer, 5 the printer, 8 the pages.</summary>
    public static PrintedJob? Parse(string xml, string server)
    {
        try
        {
            var e = XDocument.Parse(xml).Root!;
            string? Of(string name) => e.Descendants().FirstOrDefault(d => d.Name.LocalName == name)?.Value;
            var record = long.TryParse(Of("EventRecordID"), out var r) ? r : 0;
            var at = e.Descendants().FirstOrDefault(d => d.Name.LocalName == "TimeCreated")?.Attribute("SystemTime")?.Value;
            if (!DateTime.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
            {
                return null;
            }
            var computer = Of("Param4")?.TrimStart('\\');
            return new PrintedJob(when, server, Of("Param5") ?? "", Of("Param2") ?? "", Of("Param3"), computer, int.TryParse(Of("Param8"), out var pages) ? pages : 0, "Printed", record);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}

/// <summary>Reads the print servers' logs every three minutes.</summary>
public sealed class PrintHistoryWorker(PrintQueues queues, TimeProvider clock, ILogger<PrintHistoryWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(3), clock, stop);
            try
            {
                await queues.Collect(stop);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "The print history could not be read");
            }
        }
    }
}

public static class PrintQueuesApi
{
    private static readonly string[] JobActions = ["pause", "resume", "restart", "cancel"];
    private static readonly string[] PrinterActions = ["pause", "resume", "purge"];

    public static void AddPrintQueues(this IServiceCollection services)
    {
        services.AddSingleton<IPrintQueueProbe>(_ => OperatingSystem.IsWindows() ? new WindowsPrintQueueProbe() : new NoPrintQueueProbe());
        services.AddSingleton<IPrintLog>(_ => OperatingSystem.IsWindows() ? new WindowsPrintLog() : new NoPrintLog());
        services.AddSingleton<PrintQueues>();
        services.AddHostedService<PrintHistoryWorker>();
    }

    public static void MapPrintQueuesApi(this WebApplication app)
    {
        // One printer's queue, for its Queue tab, asked every few seconds while it's open.
        app.MapGet("/api/devices/printers/queue", async (string? share, HttpContext context, PrintQueues queues) =>
        {
            if (!Access.Can(context, Permissions.SeatsView))
            {
                return Access.NotAllowed();
            }
            return queues.Find(share) is { } p ? Results.Ok(await queues.Read(p)) : Results.NotFound(new { error = "That printer is not shared by a print server Rabbid Lab knows." });
        });

        // Every queue, for Print jobs.
        app.MapGet("/api/devices/printers/jobs", async (HttpContext context, PrintQueues queues) =>
            Access.Can(context, Permissions.SeatsView) ? Results.Ok(await queues.ReadAll()) : Access.NotAllowed());

        app.MapPost("/api/devices/printers/jobs", async (JobActionForm form, HttpContext context, PrintQueues queues, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsPrinters) is { } refused)
            {
                return refused;
            }
            var action = form.Action?.Trim().ToLowerInvariant() ?? "";
            var ids = form.Ids ?? [];
            if (!JobActions.Contains(action) || ids.Count is 0 or > 200)
            {
                return Results.BadRequest(new { error = "Select between 1 and 200 jobs, and pause, resume, restart or cancel." });
            }
            if (queues.Find(form.Share) is not { } p)
            {
                return Results.NotFound(new { error = "That printer is not shared by a print server Rabbid Lab knows." });
            }
            try
            {
                var said = await queues.Act(p, ids, action, ActivityLog.Who(context));
                var done = said.Count(x => x.Value is null);
                var problems = said.Where(x => x.Value is not null).Select(x => x.Value!).Distinct().ToList();
                var verb = action switch { "pause" => "Paused", "resume" => "Resumed", "restart" => "Restarted", _ => "Cancelled" };
                log.Record(context, "clients", $"printers.jobs.{action}", $"{verb} {done} print {(done == 1 ? "job" : "jobs")} on {p.Name} ({p.Server})",
                    problems.Count == 0 ? "ok" : done > 0 ? "warning" : "failed", problems.Count == 0 ? null : string.Join(" ", problems));
                return Results.Ok(new { done, problems, results = said.Select(x => new { id = x.Key, error = x.Value }) });
            }
            catch (ToolProblem e)
            {
                return Results.Json(new { error = e.Message }, statusCode: e.Status);
            }
        });

        // A printer itself: pause or resume printing, or cancel every job (purge).
        app.MapPost("/api/devices/printers/queue", async (QueueActionForm form, HttpContext context, PrintQueues queues, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsPrinters) is { } refused)
            {
                return refused;
            }
            var action = form.Action?.Trim().ToLowerInvariant() ?? "";
            if (!PrinterActions.Contains(action))
            {
                return Results.BadRequest(new { error = "Pause, resume or cancel all jobs." });
            }
            if (queues.Find(form.Share) is not { } p)
            {
                return Results.NotFound(new { error = "That printer is not shared by a print server Rabbid Lab knows." });
            }
            try
            {
                var said = await queues.Printer(p, action);
                var title = action switch { "pause" => $"Paused printing on {p.Name} ({p.Server})", "resume" => $"Resumed printing on {p.Name} ({p.Server})", _ => $"Cancelled all print jobs on {p.Name} ({p.Server})" };
                log.Record(context, "clients", $"printers.queue.{action}", title, said is null ? "ok" : "failed", said);
                return said is null ? Results.Ok(new { }) : Results.BadRequest(new { error = said });
            }
            catch (ToolProblem e)
            {
                return Results.Json(new { error = e.Message }, statusCode: e.Status);
            }
        });

        // What finished: one printer's (share), or every printer's, with each server's log state and the days kept.
        app.MapGet("/api/devices/printers/history", (string? share, int? count, HttpContext context, PrintQueues queues, PrintServers servers) =>
        {
            if (!Access.Can(context, Permissions.SeatsView))
            {
                return Access.NotAllowed();
            }
            var p = share is null ? null : queues.Find(share);
            if (share is not null && p is null)
            {
                return Results.NotFound(new { error = "That printer is not shared by a print server Rabbid Lab knows." });
            }
            var s = queues.State();
            var hosts = servers.Known().Servers;
            return Results.Ok(new
            {
                rows = queues.History(p?.Server, p?.Name).Take(Math.Clamp(count ?? 5000, 0, 5000)), keep = s.Keep, choices = PrintQueues.KeepChoices,
                servers = hosts.Where(h => p is null || string.Equals(h.Server, p.Server, StringComparison.OrdinalIgnoreCase))
                    .Select(h => new { server = h.Server, host = h.Host, problem = s.Servers.GetValueOrDefault(h.Host)?.Problem, read = s.Servers.GetValueOrDefault(h.Host)?.At }),
            });
        });

        // Settings: how long finished jobs are kept.
        app.MapPost("/api/devices/printers/history/keep", (KeepForm form, HttpContext context, PrintQueues queues, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsSettings) is { } refused)
            {
                return refused;
            }
            if (!PrintQueues.KeepChoices.Contains(form.Days))
            {
                return Results.BadRequest(new { error = "Keep print job history for 5, 15, 30, 60 or 90 days." });
            }
            queues.SetKeep(form.Days);
            log.Record(context, "admin", "printers.history.keep", $"Set print job history to be kept for {form.Days} days");
            return Results.Ok(new { keep = form.Days });
        });
    }
}

/// <summary>A print server's spooler over the network: jobs read as this computer, changed signed in as the service account.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPrintQueueProbe : IPrintQueueProbe
{
    private const uint UseAccess = 0x8; // PRINTER_ACCESS_USE
    private const uint AdministerAccess = 0x4; // PRINTER_ACCESS_ADMINISTER
    private const int InsufficientBuffer = 122;

    public (List<PrintJob> Jobs, string? Problem) Jobs(string share)
    {
        try
        {
            return (Read(share), null);
        }
        catch (Win32Exception e)
        {
            return ([], e.NativeErrorCode == 5 ? $"Access denied. {Environment.MachineName} cannot read the queue on {share}." : $"Could not read the queue: {e.Message}");
        }
    }

    public string? Job(string share, int id, string action, string user, string password) => As(user, password, share, () =>
    {
        var command = action switch { "pause" => 1u, "resume" => 2u, "restart" => 4u, _ => 5u }; // JOB_CONTROL_PAUSE, RESUME, RESTART, DELETE
        var h = Open(share, UseAccess);
        try
        {
            return SetJob(h, (uint) id, 0, IntPtr.Zero, command) ? null : Said(Marshal.GetLastWin32Error(), user, share, "Manage documents");
        }
        finally
        {
            ClosePrinter(h);
        }
    });

    public string? Printer(string share, string action, string user, string password) => As(user, password, share, () =>
    {
        var command = action switch { "pause" => 1u, "resume" => 2u, _ => 3u }; // PRINTER_CONTROL_PAUSE, RESUME, PURGE
        var h = Open(share, AdministerAccess);
        try
        {
            return SetPrinter(h, 0, IntPtr.Zero, command) ? null : Said(Marshal.GetLastWin32Error(), user, share, "Manage this printer");
        }
        finally
        {
            ClosePrinter(h);
        }
    });

    private static string? As(string user, string password, string share, Func<string?> work)
    {
        try
        {
            return NetworkLogon.Run(user, password, work, $"Windows could not sign in as {user} to reach {share}.");
        }
        catch (Win32Exception e)
        {
            return Said(e.NativeErrorCode, user, share, "Manage documents");
        }
        catch (ToolProblem e)
        {
            return e.Message;
        }
    }

    private static string Said(int code, string user, string share, string right) => code switch
    {
        5 => $"Access denied. Give {user} the {right} permission on {share} (printer properties, Security tab).",
        87 => "That job is no longer in the queue.",
        _ => new Win32Exception(code).Message,
    };

    private static IntPtr Open(string share, uint access)
    {
        var defaults = new PrinterDefaults { DesiredAccess = access };
        return OpenPrinter(share, out var h, ref defaults) ? h : throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static List<PrintJob> Read(string share)
    {
        var h = Open(share, UseAccess);
        try
        {
            EnumJobs(h, 0, 500, 1, IntPtr.Zero, 0, out var needed, out _);
            if (needed == 0)
            {
                return [];
            }
            var buffer = Marshal.AllocHGlobal((int) needed);
            try
            {
                if (!EnumJobs(h, 0, 500, 1, buffer, needed, out _, out var count))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                var size = Marshal.SizeOf<JobInfo1>();
                var list = new List<PrintJob>();
                for (var i = 0; i < count; i++)
                {
                    var j = Marshal.PtrToStructure<JobInfo1>(buffer + (i * size));
                    var t = j.Submitted;
                    DateTime? at = t.Year > 1600 ? new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, DateTimeKind.Utc) : null;
                    list.Add(new PrintJob((int) j.JobId, Marshal.PtrToStringUni(j.Document) ?? "", Marshal.PtrToStringUni(j.UserName), Marshal.PtrToStringUni(j.MachineName)?.TrimStart('\\'),
                        State(j.Status), (int) j.TotalPages, (int) j.PagesPrinted, at, (int) j.Position));
                }
                return list;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ClosePrinter(h);
        }
    }

    /// <summary>A job's status bits as one word, the most pressing first.</summary>
    public static string State(uint s) =>
        (s & 0x40) != 0 ? "Out of paper" : (s & 0x20) != 0 ? "Offline" : (s & 0x602) != 0 ? "Error" : (s & 0x4) != 0 ? "Cancelling" : (s & 0x1) != 0 ? "Paused"
        : (s & 0x10) != 0 ? "Printing" : (s & 0x8) != 0 ? "Spooling" : (s & 0x1080) != 0 ? "Printed" : "Queued";

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterDefaults
    {
        public IntPtr Datatype, DevMode;
        public uint DesiredAccess;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobInfo1
    {
        public uint JobId;
        public IntPtr PrinterName, MachineName, UserName, Document, Datatype, StatusText;
        public uint Status, Priority, Position, TotalPages, PagesPrinted;
        public SystemTime Submitted;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenPrinter(string name, out IntPtr handle, ref PrinterDefaults defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClosePrinter(IntPtr handle);

    [DllImport("winspool.drv", EntryPoint = "EnumJobsW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumJobs(IntPtr handle, uint first, uint count, uint level, IntPtr buffer, uint size, out uint needed, out uint returned);

    [DllImport("winspool.drv", EntryPoint = "SetJobW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetJob(IntPtr handle, uint id, uint level, IntPtr job, uint command);

    [DllImport("winspool.drv", EntryPoint = "SetPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPrinter(IntPtr handle, uint level, IntPtr printer, uint command);
}

/// <summary>A print server's PrintService Operational log, read over the network signed in as the service account.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPrintLog : IPrintLog
{
    private const string Log = "Microsoft-Windows-PrintService/Operational";

    public IReadOnlyList<string> Read(string host, string user, string password, string query, int most)
    {
        using var secret = new System.Security.SecureString();
        foreach (var c in password)
        {
            secret.AppendChar(c);
        }
        var cut = user.IndexOf('\\');
        try
        {
            using var session = new EventLogSession(host, cut > 0 ? user[..cut] : null, cut > 0 ? user[(cut + 1)..] : user, secret, SessionAuthentication.Default);
            var config = new EventLogConfiguration(Log, session);
            if (!config.IsEnabled)
            {
                throw new PrintLogProblem($"The PrintService Operational log is turned off on {host}. On the print server, run: wevtutil sl {Log} /e:true");
            }
            using var reader = new EventLogReader(new EventLogQuery(Log, PathType.LogName, query) { Session = session });
            var list = new List<string>();
            while (list.Count < most && reader.ReadEvent(TimeSpan.FromSeconds(30)) is { } e)
            {
                using (e)
                {
                    list.Add(e.ToXml());
                }
            }
            return list;
        }
        catch (UnauthorizedAccessException)
        {
            throw new PrintLogProblem($"Access denied. Add {user} to the Event Log Readers group on {host}.");
        }
        catch (EventLogException e)
        {
            throw new PrintLogProblem($"Could not read the print history on {host}: {e.Message.Trim()}");
        }
    }
}
