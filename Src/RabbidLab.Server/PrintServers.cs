using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RabbidLab.Server;

/// <summary>
/// A printer a server shares, as the server itself lists it. Published: the server also listed it in Active Directory.
/// Ip: the printer's own address, when its port's name says it. Status: what's wrong with the queue in a word or two, null
/// when nothing is. Jobs: documents waiting in it.
/// </summary>
public sealed record SharedPrinter(string Name, string Server, string Share, string? Driver, string? Location, string? Description, string? Port, bool Published,
    string? Ip = null, string? Status = null, int? Jobs = null);

/// <summary>What one server answered: the printers it shares (none when it isn't a print server), or why it couldn't be asked.</summary>
public sealed record PrintServerAnswer(List<SharedPrinter> Printers, string? Problem = null);

/// <summary>A server that shares printers or couldn't be asked: how many printers, or the problem, and when.</summary>
public sealed record PrintServerSeen(string Server, string Host, int Printers, string? Problem, DateTime At);

/// <summary>
/// What the search has now: the printers, the servers worth showing, when it last finished, whether it's still asking, and
/// every server that answered (print server or not).
/// </summary>
public sealed record PrintScan(List<SharedPrinter> Printers, List<PrintServerSeen> Servers, DateTime? At, bool Scanning, HashSet<string> Answered);

/// <summary>
/// One row of Devices, Printers from the domain: in Active Directory (Published), listed by its server (Found), or both, with
/// what looks wrong with it in plain words (Problems).
/// </summary>
public sealed record ListedPrinter(string Name, string? Server, string? Share, string? Driver, string? Location, string? Description, string? Port, bool Published, bool Found,
    string? Ip, string? Status, int? Jobs, List<string> Problems);

/// <summary>Asks one server which printers it shares. It only reads, and never throws.</summary>
public interface IPrintServerProbe
{
    PrintServerAnswer Ask(AdServer server);
}

/// <summary>Where there's no Windows to ask with (the container): nothing is found.</summary>
public sealed class NoPrintServerProbe : IPrintServerProbe
{
    public PrintServerAnswer Ask(AdServer server) => new([]);
}

/// <summary>
/// Finds the print servers: asks every candidate Active Directory names (DirectoryExtras.cs) for the printers it shares, a few
/// at a time, and keeps the answers for six hours. It asks as this server's own computer account, never as the service account,
/// and only reads. A read waits a few seconds for a search under way and then gives what there is; the rest shows on the next read.
/// </summary>
public sealed class PrintServers(IPrintServerProbe probe, TimeProvider? clock = null)
{
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(6);
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(1); // asked again no sooner than this, whoever presses Refresh
    private static readonly TimeSpan Longest = TimeSpan.FromSeconds(90); // a search is over by then; a late answer still counts
    private const int AtOnce = 6;

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<string, (PrintServerSeen Seen, List<SharedPrinter> Printers)> answers = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? at;
    private Task? running;

    /// <summary>How long a read waits for a search that's under way.</summary>
    public TimeSpan Wait { get; init; } = TimeSpan.FromSeconds(8);

    public async Task<PrintScan> Read(Func<Task<List<AdServer>>> candidates, bool again, CancellationToken stop)
    {
        TaskCompletionSource? mine = null;
        lock (gate)
        {
            var age = at is { } t ? clock.GetUtcNow().UtcDateTime - t : (TimeSpan?) null;
            if (running is null && (age is null || age > Fresh || (again && age > Gap)))
            {
                mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                running = mine.Task;
            }
        }
        if (mine is not null)
        {
            List<AdServer> servers;
            try
            {
                servers = await candidates(); // read here, as the person asking
            }
            catch
            {
                lock (gate)
                {
                    running = null;
                }
                mine.TrySetResult();
                throw;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await Search(servers);
                }
                finally
                {
                    lock (gate)
                    {
                        at = clock.GetUtcNow().UtcDateTime;
                        running = null;
                    }
                    mine.TrySetResult();
                }
            }, CancellationToken.None);
        }
        Task? wait;
        lock (gate)
        {
            wait = running;
        }
        if (wait is not null)
        {
            await Task.WhenAny(wait, Task.Delay(Wait, stop));
        }
        lock (gate)
        {
            return new PrintScan(
                [.. answers.Values.SelectMany(a => a.Printers).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)],
                [.. answers.Values.Select(a => a.Seen).Where(s => s.Printers > 0 || s.Problem is not null)
                    .OrderByDescending(s => s.Printers).ThenBy(s => s.Server, StringComparer.OrdinalIgnoreCase)],
                at, running is not null,
                new HashSet<string>(answers.Values.Where(a => a.Seen.Problem is null).Select(a => a.Seen.Server), StringComparer.OrdinalIgnoreCase));
        }
    }

    private async Task Search(List<AdServer> servers)
    {
        lock (gate)
        {
            // Servers Active Directory no longer names go; the rest keep their last answer until the new one is in.
            foreach (var gone in answers.Keys.Where(k => !servers.Any(s => s.Name.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                answers.Remove(gone);
            }
        }
        var slots = new SemaphoreSlim(AtOnce); // never disposed: a server that answers after the search is over still lets go of its slot
        var all = Task.WhenAll(servers.Select(async server =>
        {
            await slots.WaitAsync();
            try
            {
                PrintServerAnswer answer;
                try
                {
                    answer = await Task.Run(() => probe.Ask(server));
                }
                catch (Exception e)
                {
                    answer = new([], e.Message);
                }
                lock (gate)
                {
                    answers[server.Name] = (new PrintServerSeen(server.Name, server.Host, answer.Printers.Count, answer.Problem, clock.GetUtcNow().UtcDateTime), answer.Printers);
                }
            }
            finally
            {
                slots.Release();
            }
        }));
        await Task.WhenAny(all, Task.Delay(Longest));
    }

    /// <summary>
    /// The printers Active Directory has and the ones the servers listed, as one list: a printer in both is one row. They're
    /// the same when the server's short name and the share's name match.
    /// </summary>
    public static List<ListedPrinter> Together(IEnumerable<AdPrinter> published, PrintScan scan)
    {
        static string Short(string? server) => (server ?? "").Split('.')[0];
        static string Key(string? server, string? share, string name)
        {
            var last = share is { Length: > 0 } ? share[(share.LastIndexOf('\\') + 1)..] : name;
            return $"{Short(server)}\\{last}".ToUpperInvariant();
        }
        var listed = published.ToList();
        // A server that lists some of its printers in Active Directory means to list them: one it left out is worth a word.
        var lists = new HashSet<string>(listed.Select(p => Short(p.Server)).Concat(scan.Printers.Where(p => p.Published).Select(p => p.Server)), StringComparer.OrdinalIgnoreCase);
        var rows = new Dictionary<string, ListedPrinter>();
        var byIp = new Dictionary<string, SharedPrinter>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in scan.Printers)
        {
            var problems = new List<string>();
            if (p.Status is { Length: > 0 })
            {
                problems.Add($"The queue reports: {p.Status}.");
            }
            if (p.Ip is { Length: > 0 } && !byIp.TryAdd(p.Ip, p))
            {
                problems.Add($"Same address as {byIp[p.Ip].Name} on {byIp[p.Ip].Server}: likely one printer shared twice.");
            }
            rows[Key(p.Server, p.Share, p.Name)] = new ListedPrinter(p.Name, p.Server, p.Share, p.Driver, p.Location, p.Description, p.Port, p.Published, Found: true, p.Ip, p.Status, p.Jobs, problems);
        }
        foreach (var p in listed)
        {
            var key = Key(p.Server, p.Share, p.Name);
            static string? Or(string? a, string? b) => a is { Length: > 0 } ? a : b;
            if (rows.TryGetValue(key, out var f))
            {
                rows[key] = f with { Driver = Or(f.Driver, p.Driver), Location = Or(f.Location, p.Location), Description = Or(f.Description, p.Description), Port = Or(f.Port, p.Port), Published = true };
                continue;
            }
            // Its server answered and no longer shares it: Active Directory is out of date. A server that didn't answer says nothing.
            var stale = scan.Answered.Contains(Short(p.Server));
            rows[key] = new ListedPrinter(p.Name, p.Server, p.Share, p.Driver, p.Location, p.Description, p.Port, Published: true, Found: false, IpIn(p.Port), null, null,
                stale ? [$"Listed in Active Directory, but {Short(p.Server)} doesn't share it any more. The listing is stale."] : []);
        }
        foreach (var (key, row) in rows.Where(r => r.Value is { Found: true, Published: false } && lists.Contains(r.Value.Server ?? "")).ToList())
        {
            rows[key] = row with { Problems = [.. row.Problems, "Shared but not listed in Active Directory, so it doesn't show when people search for printers."] };
        }
        return [.. rows.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Server, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The printer's own address when a port's name carries it, as the standard ones do: IP_10.1.2.30, 10.1.2.30_1.</summary>
    public static string? IpIn(string? port)
    {
        var m = System.Text.RegularExpressions.Regex.Match(port ?? "", @"(?<!\d)(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?!\d)");
        return m.Success && Enumerable.Range(1, 4).All(i => int.Parse(m.Groups[i].Value, System.Globalization.CultureInfo.InvariantCulture) <= 255)
            ? string.Join('.', Enumerable.Range(1, 4).Select(i => int.Parse(m.Groups[i].Value, System.Globalization.CultureInfo.InvariantCulture))) : null;
    }

    /// <summary>What's wrong with a queue, from Windows' status and attribute bits, worst first. Null when it's fine or just busy.</summary>
    public static string? QueueStatus(uint status, uint attributes)
    {
        if ((attributes & 0x400) != 0)
        {
            return "Offline (Use Printer Offline is on)";
        }
        (uint Bit, string Text)[] known =
        [
            (0x80, "Offline"), (0x2, "Error"), (0x8, "Paper jam"), (0x10, "Out of paper"), (0x40000, "Out of toner"), (0x400000, "Door open"),
            (0x100000, "Needs attention"), (0x1, "Paused"), (0x1000, "Not available"), (0x40, "Paper problem"), (0x800, "Output bin full"),
            (0x200000, "Out of memory"), (0x20000, "Toner low"),
        ];
        return known.Where(k => (status & k.Bit) != 0).Select(k => k.Text).FirstOrDefault();
    }
}

/// <summary>
/// Asks a Windows server over file and printer sharing: its shares say whether it shares printers at all (NetShareEnum), and
/// its spooler gives each one's driver, port, location and comment (EnumPrinters). When the spooler won't say, the shares'
/// names and comments are used. Both calls go out as the account this service runs as.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPrintServerProbe : IPrintServerProbe
{
    private const int Port = 445;
    private static readonly TimeSpan Knock = TimeSpan.FromMilliseconds(1500);

    public PrintServerAnswer Ask(AdServer server)
    {
        try
        {
            using (var tcp = new TcpClient())
            {
                if (!tcp.ConnectAsync(server.Host, Port).Wait(Knock))
                {
                    return new([], $"No answer on port {Port} (file and printer sharing).");
                }
            }
        }
        catch (Exception e) when (e is AggregateException or SocketException)
        {
            var inner = (e as AggregateException)?.GetBaseException() ?? e;
            return new([], inner is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData }
                ? "Its name isn't in DNS." : $"No answer on port {Port} (file and printer sharing).");
        }
        List<(string Name, string? Remark)> shares;
        try
        {
            shares = PrintShares(server.Host);
        }
        catch (Win32Exception e)
        {
            return new([], e.NativeErrorCode == 5 ? $"It didn't let {Environment.MachineName} list its shares (access denied)." : $"Couldn't list its shares: {e.Message}");
        }
        if (shares.Count == 0)
        {
            return new([]);
        }
        var printers = new List<SharedPrinter>();
        try
        {
            printers = Printers(server, shared: true);
        }
        catch (Win32Exception)
        {
            // The spooler didn't answer (stopped here or there, or closed to remote calls): the shares still say what there is.
        }
        foreach (var (name, remark) in shares.Where(s => !printers.Any(p => p.Share.EndsWith($@"\{s.Name}", StringComparison.OrdinalIgnoreCase))))
        {
            printers.Add(new SharedPrinter(name, server.Name, $@"\\{server.Host}\{name}", null, null, remark is { Length: > 0 } ? remark : null, null, false));
        }
        return new(printers);
    }

    /// <summary>The printers a server's spooler lists. shared: only the shared ones.</summary>
    public static List<SharedPrinter> Printers(AdServer server, bool shared)
    {
        var flags = EnumName | (shared ? EnumShared : 0);
        var target = $@"\\{server.Host}";
        // Asked once with no room, to hear how much room the list takes.
        if (!EnumPrinters(flags, target, 2, IntPtr.Zero, 0, out var needed, out _) && Marshal.GetLastWin32Error() is var first and not InsufficientBuffer)
        {
            throw new Win32Exception(first);
        }
        var list = new List<SharedPrinter>();
        for (var attempt = 0; attempt < 3 && needed > 0; attempt++)
        {
            var buffer = Marshal.AllocHGlobal((int) needed);
            try
            {
                if (!EnumPrinters(flags, target, 2, buffer, needed, out needed, out var count))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == InsufficientBuffer)
                    {
                        continue; // a printer was added between the two calls
                    }
                    throw new Win32Exception(error);
                }
                var size = Marshal.SizeOf<PrinterInfo2>();
                for (var i = 0; i < count; i++)
                {
                    var info = Marshal.PtrToStructure<PrinterInfo2>(buffer + (i * size));
                    var name = Marshal.PtrToStringUni(info.PrinterName) ?? "";
                    // Another server's printers come named \\server\Name.
                    var cut = name.StartsWith(@"\\", StringComparison.Ordinal) ? name.IndexOf('\\', 2) : -1;
                    name = cut > 0 ? name[(cut + 1)..] : name;
                    var share = Marshal.PtrToStringUni(info.ShareName);
                    if (name.Length == 0 || (shared && ((info.Attributes & AttributeShared) == 0 || string.IsNullOrEmpty(share))))
                    {
                        continue;
                    }
                    list.Add(new SharedPrinter(name, server.Name, $@"\\{server.Host}\{(string.IsNullOrEmpty(share) ? name : share)}", Text(info.DriverName), Text(info.Location),
                        Text(info.Comment), Text(info.PortName), (info.Attributes & AttributePublished) != 0, PrintServers.IpIn(Text(info.PortName)),
                        PrintServers.QueueStatus(info.Status, info.Attributes), (int) info.Jobs));
                }
                return list;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return list;
    }

    private static string? Text(IntPtr text) => Marshal.PtrToStringUni(text) is { Length: > 0 } s ? s : null;

    /// <summary>The printer shares of a server: each one's name and comment.</summary>
    public static List<(string Name, string? Remark)> PrintShares(string host)
    {
        var resume = 0;
        var code = NetShareEnum($@"\\{host}", 1, out var buffer, -1, out var read, out _, ref resume);
        if (code != 0)
        {
            throw new Win32Exception(code);
        }
        try
        {
            var list = new List<(string, string?)>();
            var size = Marshal.SizeOf<ShareInfo1>();
            for (var i = 0; i < read; i++)
            {
                var info = Marshal.PtrToStructure<ShareInfo1>(buffer + (i * size));
                if ((info.Type & 0x0FFFFFFF) == SharePrintQueue && Marshal.PtrToStringUni(info.Name) is { Length: > 0 } name)
                {
                    list.Add((name, Marshal.PtrToStringUni(info.Remark)));
                }
            }
            return list;
        }
        finally
        {
            _ = NetApiBufferFree(buffer);
        }
    }

    private const uint EnumName = 0x8; // PRINTER_ENUM_NAME: the printers of the server named
    private const uint EnumShared = 0x20;
    private const uint AttributeShared = 0x8;
    private const uint AttributePublished = 0x2000;
    private const uint SharePrintQueue = 1; // STYPE_PRINTQ
    private const int InsufficientBuffer = 122;

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo2
    {
        public IntPtr ServerName, PrinterName, ShareName, PortName, DriverName, Comment, Location, DevMode, SepFile, PrintProcessor, Datatype, Parameters, SecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, Jobs, AveragePpm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShareInfo1
    {
        public IntPtr Name;
        public uint Type;
        public IntPtr Remark;
    }

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumPrinters(uint flags, string? name, uint level, IntPtr buffer, uint size, out uint needed, out uint returned);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareEnum(string server, int level, out IntPtr buffer, int preferred, out int read, out int total, ref int resume);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
