using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>One domain controller, opened for replication: the partitions it holds, its partners for each, and pulling one.</summary>
public interface IReplicaDc : IDisposable
{
    IReadOnlyList<string> Partitions { get; }

    /// <summary>The DNS names of the domain controllers it replicates this partition from.</summary>
    IReadOnlyList<string> From(string partition);

    /// <summary>Copies what that partner has of the partition to this one now (repadmin /replicate).</summary>
    void Pull(string partition, string from);
}

/// <summary>Opens domain controllers for replication, signed in as an account. Tests stand in for it.</summary>
public interface IReplicator
{
    IReplicaDc Open(string host, string user, string password);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsReplicator : IReplicator
{
    public IReplicaDc Open(string host, string user, string password)
    {
        var context = new System.DirectoryServices.ActiveDirectory.DirectoryContext(System.DirectoryServices.ActiveDirectory.DirectoryContextType.DirectoryServer, host, user, password);
        return new Dc(System.DirectoryServices.ActiveDirectory.DomainController.GetDomainController(context));
    }

    private sealed class Dc(System.DirectoryServices.ActiveDirectory.DomainController dc) : IReplicaDc
    {
        public IReadOnlyList<string> Partitions { get; } = [.. dc.Partitions.Cast<string>()];

        public IReadOnlyList<string> From(string partition) =>
            [.. dc.GetReplicationNeighbors(partition).Cast<System.DirectoryServices.ActiveDirectory.ReplicationNeighbor>().Select(n => n.SourceServer).OfType<string>()];

        public void Pull(string partition, string from) => dc.SyncReplicaFromServer(partition, from);

        public void Dispose() => dc.Dispose();
    }
}

/// <summary>In Docker or on Linux: Windows does the replication calls, so there's none here.</summary>
public sealed class NoReplicator : IReplicator
{
    public IReplicaDc Open(string host, string user, string password) => throw new ToolProblem($"Syncing domain controllers needs {Product.Title} on Windows.");
}

/// <summary>Why a replication call failed, in words, and what to do. Windows' error codes from ActiveDirectoryOperationException.</summary>
public static class SyncWhy
{
    public static (string Said, string Advice) Explain(Exception e)
    {
        var code = e.GetType().GetProperty("ErrorCode")?.GetValue(e) is int c ? c : e.HResult & 0xFFFF;
        var said = e.Message.Trim().TrimEnd('.');
        return code switch
        {
            1722 or 1727 or 1753 => ("RPC server unavailable", "Check that it is online and that the firewall allows TCP 135 and 49152-65535 between sites."),
            8453 or 5 => ("Replication access was denied", "The service account may not start replication. Give it Replication Synchronization on the domain, Configuration and Schema partitions, or use a domain admin as the service account."),
            1256 => ("The remote system is not available", "Check that it is online and reachable on the network."),
            8524 or 1908 => ("DNS name not found", "Check its DNS records (_msdcs) and that this server uses the domain's DNS servers."),
            8614 => ("Offline longer than the tombstone lifetime", "Demote it, clean up its metadata, and promote it again."),
            1326 or 1311 => ("The service account's password was rejected", "Enter the service account again in Settings > Active Directory."),
            _ when e is TimeoutException => ("Timed out", "Check that it is online and reachable on the network."),
            _ => (said.Length > 0 ? said : "Replication failed", "Run repadmin /showrepl on this domain controller for details."),
        };
    }

    /// <summary>A domain controller's short name, as people say it: DC01 for dc01.amw.com.</summary>
    public static string Short(string host) => host.Split('.')[0].ToUpperInvariant();

    /// <summary>A partition in words: Domain, Configuration, Schema, Domain DNS zones or Forest DNS zones.</summary>
    public static string Part(string dn) => dn switch
    {
        _ when dn.StartsWith("CN=Schema,", StringComparison.OrdinalIgnoreCase) => "Schema",
        _ when dn.StartsWith("CN=Configuration,", StringComparison.OrdinalIgnoreCase) => "Configuration",
        _ when dn.StartsWith("DC=DomainDnsZones,", StringComparison.OrdinalIgnoreCase) => "Domain DNS zones",
        _ when dn.StartsWith("DC=ForestDnsZones,", StringComparison.OrdinalIgnoreCase) => "Forest DNS zones",
        _ => "Domain",
    };
}

/// <summary>Entra Connect: where it runs (from its MSOL_ account), the PowerShell its server's client runs, and what that said.</summary>
public static partial class SyncEntra
{
    /// <summary>The server in an MSOL_ account's description: "… running on computer SYNC01 configured to synchronize to tenant …".</summary>
    public static string? Server(string? description) =>
        description is not null && Running().Match(description) is { Success: true } m ? m.Groups[1].Value.ToUpperInvariant() : null;

    /// <summary>
    /// Waits for a sync that's already going, starts one (Delta, or Initial for full), waits for it to finish, then says
    /// how it went as one RESULT line of JSON: the runs since it started, with what each sent to Entra ID.
    /// </summary>
    public static string Script(bool full) => """
        $ErrorActionPreference = 'Stop'
        try {
          Import-Module ADSync
          $waited = $false
          $until = (Get-Date).AddMinutes(10)
          while ((Get-ADSyncScheduler).SyncCycleInProgress) { $waited = $true; if ((Get-Date) -gt $until) { throw 'A sync was already running and did not finish within 10 minutes.' }; Start-Sleep -Seconds 5 }
          $started = (Get-Date).ToUniversalTime()
          Start-ADSyncSyncCycle -PolicyType KIND | Out-Null
          Start-Sleep -Seconds 5
          $until = (Get-Date).AddMinutes(170)
          while ((Get-ADSyncScheduler).SyncCycleInProgress) { if ((Get-Date) -gt $until) { break }; Start-Sleep -Seconds 5 }
          $s = Get-ADSyncScheduler
          $runs = @(Get-ADSyncRunProfileResult -NumberRequested 30 | Where-Object { $_.StartDate.ToUniversalTime() -ge $started.AddSeconds(-30) } | ForEach-Object {
            $sent = 0; foreach ($r in @($_.RunStepResults)) { $sent += [int]$r.ExportAdd + [int]$r.ExportUpdate + [int]$r.ExportRename + [int]$r.ExportDelete }
            [pscustomobject]@{ connector = [string]$_.ConnectorName; profile = [string]$_.RunProfileName; result = [string]$_.Result; sent = $sent } })
          'RESULT ' + ([pscustomobject]@{ ok = $true; waited = $waited; done = -not $s.SyncCycleInProgress; next = $s.NextSyncCycleStartTimeInUTC; runs = $runs } | ConvertTo-Json -Depth 4 -Compress)
        } catch {
          'RESULT ' + ([pscustomobject]@{ ok = $false; error = $_.Exception.Message } | ConvertTo-Json -Compress)
        }
        """.Replace("KIND", full ? "Initial" : "Delta", StringComparison.Ordinal);

    /// <summary>How it went: ok, what to say when it isn't, when the next one runs on its own, and how many objects went to Entra ID.</summary>
    public sealed record Said(bool Ok, string? Error, DateTime? Next, int? Sent, bool Waited, bool Finished);

    public static Said Read(string? output)
    {
        var line = output?.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("RESULT ", StringComparison.Ordinal));
        if (line is null)
        {
            return new(false, "The Entra Connect server returned no result.", null, null, false, false);
        }
        try
        {
            using var doc = JsonDocument.Parse(line[7..]);
            var j = doc.RootElement;
            if (!j.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            {
                var why = j.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
                return new(false, Words(why), null, null, false, false);
            }
            DateTime? next = j.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String && DateTime.TryParse(n.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : null;
            int? sent = null;
            if (j.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
            {
                // What went to Entra ID: the runs on its connector (named after the tenant, or "… - AAD").
                sent = runs.EnumerateArray().Where(r => r.TryGetProperty("connector", out var c) && c.GetString() is { } name
                        && (name.Contains("onmicrosoft.com", StringComparison.OrdinalIgnoreCase) || name.EndsWith("AAD", StringComparison.OrdinalIgnoreCase)))
                    .Sum(r => r.TryGetProperty("sent", out var s) && s.TryGetInt32(out var v) ? v : 0);
            }
            return new(true, null, next, sent, j.TryGetProperty("waited", out var w) && w.ValueKind == JsonValueKind.True,
                !j.TryGetProperty("done", out var d) || d.ValueKind != JsonValueKind.False);
        }
        catch (JsonException)
        {
            return new(false, "The result from the Entra Connect server could not be read.", null, null, false, false);
        }
    }

    private static string Words(string why) => why switch
    {
        _ when why.Contains("ADSync", StringComparison.OrdinalIgnoreCase) && why.Contains("module", StringComparison.OrdinalIgnoreCase) => "Entra Connect is not installed on that server.",
        _ when why.Contains("already", StringComparison.OrdinalIgnoreCase) => why,
        _ when why.Contains("denied", StringComparison.OrdinalIgnoreCase) => "Access denied when starting the sync on the Entra Connect server.",
        _ => why.Length > 0 ? why.TrimEnd('.') + "." : "The sync could not be started.",
    };

    [GeneratedRegex(@"running on computer\s+([A-Za-z0-9][A-Za-z0-9-]{0,62})", RegexOptions.IgnoreCase)]
    private static partial Regex Running();
}

/// <summary>A copy the sync saw happen, or fail: a change, from where it was made to a domain controller or Microsoft 365.</summary>
public sealed class SyncEvent
{
    public string Id { get; set; } = "";
    public DateTime At { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Change { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Sub { get; set; }
    public string What { get; set; } = "";
    public List<string> Details { get; set; } = [];
    public string S { get; set; } = SyncCompare.Ok; // ok, bad or notyet
    public bool M { get; set; } // to Microsoft 365
    public string? Why { get; set; }
}

/// <summary>What Microsoft 365's last sync from here said.</summary>
public sealed class SyncEntraFile
{
    public string? Server { get; set; }
    public DateTime? At { get; set; }
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public DateTime? Next { get; set; }
    public int? Sent { get; set; }
    public bool Full { get; set; }
}

/// <summary>sync.json: the copies seen (the newest 500), when every DC last had every change, Microsoft 365's last sync, and what it was sent.</summary>
public sealed class SyncFile
{
    public DateTime? Clean { get; set; }
    public DateTime? Last { get; set; }
    public SyncEntraFile? Entra { get; set; }
    public List<SyncEvent> Feed { get; set; } = [];

    /// <summary>Changes (id|time) Microsoft 365 was sent from here: ok, or notyet when it wasn't on every DC then.</summary>
    public Dictionary<string, string> Sent { get; set; } = [];
}

/// <summary>
/// Sync all, Sync DCs and Sync Microsoft 365, one at a time for everyone. Domain controllers always all sync with each
/// other: each pulls every partition from every partner, up to three times, until each has every change. Microsoft 365
/// syncs through Entra Connect's server's client. The page asks how it's going; each sync goes in the activity log.
/// </summary>
public sealed class SyncEngine(DirectoryTools tools, IReplicator replicator, DeviceJobs jobs, Clients clients, ActivityLog activity, DataFolder folder,
    ILogger<SyncEngine> log)
{
    private const int KeepFeed = 500;
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly JsonFile<SyncFile> file = folder.Json<SyncFile>("sync.json", Json);
    private readonly Lock gate = new();
    private Live? run; // the sync going, or the last one
    private (DateTime At, List<SyncDc> Dcs, SyncScan Scan, string? Entra)? seen;

    /// <summary>A domain controller or Microsoft 365 while it syncs: its state, how far (0 to 1), what to say, and what went wrong.</summary>
    private sealed class Part
    {
        public string S { get; set; } = "wait";
        public double F { get; set; }
        public string Label { get; set; } = "Waiting";
        public string? Error { get; set; }
        public string? Advice { get; set; }
    }

    private sealed class Live
    {
        public string What { get; init; } = "";
        public bool Full { get; init; }
        public string By { get; init; } = "";
        public DateTime Started { get; init; }
        public DateTime? Finished { get; set; }
        public Dictionary<string, Part> Dcs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Part? M365 { get; set; }
        public string? Problem { get; set; }

        /// <summary>How many changes were copied to a domain controller; none means there was nothing to sync.</summary>
        public int Copied { get; set; }

        /// <summary>No Entra Connect in the domain: Sync all synced the domain controllers only.</summary>
        public bool NoEntra { get; set; }
    }

    public bool Running
    {
        get
        {
            lock (gate)
            {
                return run is { Finished: null };
            }
        }
    }

    /// <summary>Starts one: null when it started, else why it can't.</summary>
    public string? Start(string what, bool full, string by, string? from)
    {
        if (what is not ("all" or "dcs" or "m365"))
        {
            return "Pick Sync all, Sync DCs or Sync Microsoft 365.";
        }
        if (what != "m365" && !OperatingSystem.IsWindows())
        {
            return $"Syncing domain controllers needs {Product.Title} on Windows.";
        }
        if (tools.SyncLogin() is not { } login)
        {
            return "Syncing requires a service account. Set one up in Settings > Active Directory.";
        }
        Live started;
        lock (gate)
        {
            if (run is { Finished: null })
            {
                return "A sync is already running.";
            }
            started = run = new Live { What = what, Full = full, By = by, Started = DateTime.UtcNow };
            // Every row waits from the moment it starts, so the page never shows the last sync's rows in between.
            if (what != "m365" && seen is { } known)
            {
                foreach (var d in known.Dcs)
                {
                    started.Dcs[d.Name] = new Part();
                }
            }
            if (what != "dcs")
            {
                started.M365 = new Part();
            }
        }
        var caller = Caller.Current;
        _ = Task.Run(async () =>
        {
            Caller.Set(caller!);
            await Go(started, login, from);
        });
        return null;
    }

    /// <summary>Everything the page shows: the domain controllers and Microsoft 365, what changed and where it is, and the copies seen.</summary>
    public async Task<object> View(bool fresh, CancellationToken stop)
    {
        if (seen is null || (!Running && (fresh || DateTime.UtcNow - seen.Value.At > Fresh)))
        {
            await Rescan(stop);
        }
        var f = file.Read();
        lock (gate)
        {
            var (_, dcs, scan, entra) = seen!.Value;
            var recent = run is { } r && (r.Finished is null || DateTime.UtcNow - r.Finished < TimeSpan.FromMinutes(30)) ? r : null;
            var client = entra is null ? null : Client(entra);
            return new
            {
                Running = run is { Finished: null },
                What = run?.What,
                Full = run?.Full,
                Started = run?.Started,
                Problem = recent?.Problem,
                Copied = recent?.Copied,
                CanSync = tools.SyncLogin() is not null,
                Windows = OperatingSystem.IsWindows(),
                Since = Since(f),
                f.Last,
                scan.More,
                Dcs = dcs.Select(d => DcView(d, scan, recent)),
                M365 = M365View(entra, client, f, recent),
                Changes = scan.Changes.Select(c => new { c.Id, c.Kind, c.Name, c.Sub, c.What, c.Details, c.On, c.At, Places = Places(c, f) }),
                Feed = f.Feed.Take(200),
            };
        }
    }

    // The window of changes: the last day, so a change stays listed after it has reached every DC, or from when every DC
    // last had every change when that's longer ago, a week at most.
    private static DateTime Since(SyncFile f)
    {
        var day = DateTime.UtcNow.AddDays(-1);
        var week = DateTime.UtcNow.AddDays(-7);
        return f.Clean is { } clean && clean < day ? (clean < week ? week : clean) : day;
    }

    private async Task Rescan(CancellationToken stop)
    {
        var (_, dcs) = await tools.SyncDcs(stop);
        var scan = await tools.SyncScan(dcs, Since(file.Read()), stop);
        string? entra = null;
        try
        {
            entra = await tools.EntraServer(stop);
        }
        catch (Exception e) when (e is LdapRefused or DirectoryProblem)
        {
            // Without it, Microsoft 365 just doesn't show.
        }
        lock (gate)
        {
            seen = (DateTime.UtcNow, dcs, scan, entra);
        }
    }

    private Client? Client(string server) =>
        clients.All().FirstOrDefault(c => c.Approved is not null && SyncWhy.Short(c.Computer).Equals(server, StringComparison.OrdinalIgnoreCase));

    private static object DcView(SyncDc d, SyncScan scan, Live? recent)
    {
        var missing = scan.Changes.Count(c => c.Places.TryGetValue(d.Name, out var p) && p == SyncCompare.Pending);
        if (recent?.Dcs.GetValueOrDefault(d.Name) is { } p)
        {
            return new { d.Name, d.Host, d.Site, d.ReadOnly, p.S, p.F, p.Label, p.Error, p.Advice, Missing = missing };
        }
        if (scan.Unreadable.TryGetValue(d.Name, out var why))
        {
            return new { d.Name, d.Host, d.Site, d.ReadOnly, S = "bad", F = 0.0, Label = "Unreachable", Error = why, Advice = (string?) "Check that it is online and reachable on the network.", Missing = missing };
        }
        return new { d.Name, d.Host, d.Site, d.ReadOnly, S = "idle", F = 0.0, Label = missing > 0 ? $"{missing} change{(missing == 1 ? "" : "s")} pending" : "In sync",
            Error = (string?) null, Advice = (string?) null, Missing = missing };
    }

    private object? M365View(string? server, Client? client, SyncFile f, Live? recent)
    {
        if (server is null)
        {
            return null;
        }
        var online = client is not null && clients.IsOnline(client);
        if (recent?.M365 is { } p)
        {
            return new { Server = server, Client = client is not null, Online = online, p.S, p.F, p.Label, p.Error, p.Advice, f.Entra?.At, f.Entra?.Next, f.Entra?.Sent };
        }
        var (s, label, error, advice) = client is null ? ("bad", "Client not installed", $"{Product.Title} syncs Microsoft 365 through its client on {server}.", $"Install the {Product.Title} client on {server} from Devices.")
            : !online ? ("bad", "Client offline", $"The client on {server} has not checked in recently.", $"Check that {server} is online and the {Product.Title} client service is running.")
            : f.Entra is { Ok: false } e ? ("bad", "The last sync failed", e.Error, (string?) null)
            : f.Entra is { At: not null } ? ("idle", "Last synced at {at}", (string?) null, (string?) null)
            : ("idle", "Never synced", (string?) null, (string?) null);
        return new { Server = server, Client = client is not null, Online = online, S = s, F = 0.0, Label = label, Error = error, Advice = advice, f.Entra?.At, f.Entra?.Next, f.Entra?.Sent };
    }

    private static Dictionary<string, string> Places(SyncChange c, SyncFile f)
    {
        var places = new Dictionary<string, string>(c.Places, StringComparer.OrdinalIgnoreCase)
        {
            ["M365"] = c.Kind is "computer" or "ou" or "other" ? "na" : f.Sent.GetValueOrDefault(Key(c), SyncCompare.Pending),
        };
        return places;
    }

    private static string Key(SyncChange c) => $"{c.Id}|{c.At:O}";

    // --- The sync itself ----------------------------------------------------------------------------------------------

    private async Task Go(Live live, (string User, string Password) login, string? from)
    {
        var stop = CancellationToken.None;
        var failed = new List<string>();
        try
        {
            var (_, dcs) = await tools.SyncDcs(stop);
            var since = Since(file.Read());
            lock (gate)
            {
                if (live.What != "m365")
                {
                    foreach (var d in dcs)
                    {
                        live.Dcs.TryAdd(d.Name, new Part());
                    }
                }
                if (live.What != "dcs")
                {
                    live.M365 ??= new Part();
                }
            }
            var before = await tools.SyncScan(dcs, since, stop);
            var after = before;
            if (live.What != "m365")
            {
                after = await SyncDcs(live, dcs, login, before, since, failed, stop);
            }
            if (live.What != "dcs")
            {
                after = await SyncM365(live, after, since, failed, stop) ?? after;
            }
            var f = file.Read();
            f.Last = live.Started;
            // Every DC that could be read has every change: the next look starts from here.
            if (after.Unreadable.Count == 0 && after.Changes.All(c => c.Places.Values.All(p => p != SyncCompare.Pending)) && failed.Count == 0)
            {
                f.Clean = live.Started;
            }
            file.Save(f);
        }
        catch (Exception e) when (e is ToolProblem or DirectoryProblem or LdapRefused or InvalidOperationException or IOException)
        {
            log.LogWarning(e, "The sync stopped");
            lock (gate)
            {
                live.Problem = e.Message;
            }
            failed.Add(e.Message);
        }
        finally
        {
            lock (gate)
            {
                live.Finished = DateTime.UtcNow;
                seen = null; // look again
            }
            var what = live.What switch
            {
                "dcs" => "Replicated all domain controllers",
                "m365" => $"Synced Microsoft 365 ({(live.Full ? "full" : "delta")})",
                _ when live.NoEntra => "Replicated all domain controllers",
                _ => $"Replicated all domain controllers and synced Microsoft 365 ({(live.Full ? "full" : "delta")})",
            };
            activity.Record(live.By, from, "users", "ad.sync", what, failed.Count == 0 ? "ok" : "failed", failed.Count == 0 ? null : string.Join(" ", failed.Distinct().Take(3)));
        }
    }

    private async Task<SyncScan> SyncDcs(Live live, List<SyncDc> dcs, (string User, string Password) login, SyncScan before, DateTime since, List<string> failed, CancellationToken stop)
    {
        var open = new Dictionary<string, IReplicaDc>(StringComparer.OrdinalIgnoreCase);
        var after = before;
        try
        {
            // Each one opened first, so a partner that can't be reached isn't asked for anything.
            foreach (var d in dcs)
            {
                Set(live, d.Name, "run", 0.05, "Connecting");
                try
                {
                    open[d.Name] = await Task.Run(() => replicator.Open(d.Host, login.User, login.Password), stop);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    var (said, advice) = SyncWhy.Explain(e);
                    Fail(live, d.Name, $"Unreachable: {said}", said, advice);
                    failed.Add($"{d.Name}: {said}.");
                }
            }
            for (var pass = 1; pass <= 3; pass++)
            {
                foreach (var (name, dc) in open)
                {
                    if (Get(live, name)?.S == "bad")
                    {
                        continue;
                    }
                    var steps = dc.Partitions.SelectMany(p => dc.From(p).Where(n => open.ContainsKey(SyncWhy.Short(n))).Select(n => (Part: p, From: n))).ToList();
                    string? problem = null;
                    for (var i = 0; i < steps.Count; i++)
                    {
                        var (p, partner) = steps[i];
                        Set(live, name, "run", (i + 0.5) / Math.Max(1, steps.Count), $"Replicating {SyncWhy.Part(p)} from {SyncWhy.Short(partner)}{(pass > 1 ? $" (pass {pass})" : "")}");
                        try
                        {
                            await Task.Run(() => dc.Pull(p, partner), stop);
                        }
                        catch (Exception e) when (e is not OutOfMemoryException)
                        {
                            var (said, advice) = SyncWhy.Explain(e);
                            problem ??= said;
                            Fail(live, name, $"Failed: {said}", $"{said} (replicating {SyncWhy.Part(p)} from {SyncWhy.Short(partner)})", advice, keepGoing: true);
                        }
                    }
                    if (problem is not null)
                    {
                        failed.Add($"{name}: {problem}.");
                        Fail(live, name, $"Failed: {problem}", Get(live, name)?.Error, Get(live, name)?.Advice);
                    }
                    else
                    {
                        Set(live, name, "run", 1, "Verifying");
                    }
                }
                after = await tools.SyncScan(dcs, since, stop);
                var reachable = open.Keys.Where(n => Get(live, n)?.S != "bad").ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!after.Changes.Any(c => reachable.Any(n => c.Places.GetValueOrDefault(n) == SyncCompare.Pending)))
                {
                    break;
                }
            }
            foreach (var d in dcs.Where(d => Get(live, d.Name)?.S != "bad"))
            {
                var missing = after.Changes.Count(c => c.Places.GetValueOrDefault(d.Name) == SyncCompare.Pending);
                if (missing > 0)
                {
                    Fail(live, d.Name, $"{missing} change{(missing == 1 ? "" : "s")} still pending", $"{missing} change{(missing == 1 ? " was" : "s were")} still pending after three replication passes.",
                        "Its replication partners may not have these changes yet. Run repadmin /showrepl on this domain controller to check its inbound partners.");
                    failed.Add($"{d.Name} is still missing {missing}.");
                }
                else
                {
                    // Nothing was missing: say so, rather than that it synced.
                    var got = before.Changes.Count(c => c.Places.GetValueOrDefault(d.Name) is SyncCompare.Pending or SyncCompare.Unknown);
                    Set(live, d.Name, "ok", 1, got == 0 ? "Already in sync" : $"Replicated {got} change{(got == 1 ? "" : "s")}");
                }
            }
            Record(live, before, after, dcs);
            return after;
        }
        finally
        {
            foreach (var dc in open.Values)
            {
                dc.Dispose();
            }
        }
    }

    // The copies seen: what was missing on a DC before and is there now, and what's still missing on one that failed.
    private void Record(Live live, SyncScan before, SyncScan after, List<SyncDc> dcs)
    {
        var events = new List<SyncEvent>();
        var was = before.Changes.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var c in after.Changes)
        {
            foreach (var d in dcs)
            {
                var then = was.GetValueOrDefault(c.Id)?.Places.GetValueOrDefault(d.Name);
                var now = c.Places.GetValueOrDefault(d.Name);
                if (then is SyncCompare.Pending or SyncCompare.Unknown && now == SyncCompare.Ok)
                {
                    events.Add(Event(c, c.On ?? "?", d.Name, SyncCompare.Ok, false, null));
                }
                else if (now is SyncCompare.Pending or SyncCompare.Unknown && Get(live, d.Name) is { S: "bad" } p)
                {
                    events.Add(Event(c, c.On ?? "?", d.Name, "bad", false, p.Error));
                }
            }
        }
        lock (gate)
        {
            live.Copied += events.Count(e => e.S == SyncCompare.Ok);
        }
        Save(events);
    }

    private async Task<SyncScan?> SyncM365(Live live, SyncScan scan, DateTime since, List<string> failed, CancellationToken stop)
    {
        var server = await tools.EntraServer(stop);
        if (server is null)
        {
            // Nothing to sync with: Sync all is then Sync DCs, and that isn't a failure.
            lock (gate)
            {
                live.NoEntra = true;
                live.M365 = null;
            }
            return null;
        }
        var client = Client(server);
        if (client is null || !clients.IsOnline(client))
        {
            var (label, why, advice) = client is null ? ("Client not installed", $"{Product.Title} syncs Microsoft 365 through its client on {server}.", $"Install the {Product.Title} client on {server} from Devices.")
                : ("Client offline", $"The client on {server} has not checked in recently.", $"Check that {server} is online and the {Product.Title} client service is running.");
            Fail(live, null, label, why, advice);
            failed.Add($"Microsoft 365: {why}");
            return null;
        }
        var kind = live.Full ? "full" : "delta";
        var job = jobs.Add(live.By, DeviceJobs.Script, $"Sync Microsoft 365 ({kind})", [new JobStep { Title = "Sync Entra Connect", Text = SyncEntra.Script(live.Full), Minutes = live.Full ? 180 : 20 }],
            [client], TimeSpan.FromMinutes(5));
        var started = DateTime.UtcNow;
        var longest = live.Full ? TimeSpan.FromMinutes(185) : TimeSpan.FromMinutes(25);
        string state;
        while (true)
        {
            state = jobs.Read(all => all.FirstOrDefault(j => j.Id == job.Id)?.Targets.FirstOrDefault()?.State) ?? DeviceJobs.Failed;
            if (state is not (DeviceJobs.Waiting or DeviceJobs.Running) || DateTime.UtcNow - started > longest)
            {
                break;
            }
            // How long a delta usually takes: the flask creeps towards full, never quite.
            var t = (DateTime.UtcNow - started).TotalSeconds;
            Set(live, null, "run", 0.95 * (1 - Math.Exp(-t / (live.Full ? 600 : 60))), state == DeviceJobs.Waiting ? $"Waiting for the client on {server}" : $"Running sync on {server}");
            await Task.Delay(1000, stop);
        }
        var said = state == DeviceJobs.Done || state == DeviceJobs.Failed ? SyncEntra.Read(jobs.Output(job.Id, client.Id)?.FirstOrDefault())
            : new SyncEntra.Said(false, state == DeviceJobs.Waiting || state == DeviceJobs.Running ? $"The sync on {server} timed out." : $"The client on {server} did not run the job ({state}).", null, null, false, false);
        var f = file.Read();
        f.Entra = new SyncEntraFile { Server = server, At = started, Ok = said.Ok, Error = said.Error, Next = said.Next, Sent = said.Sent, Full = live.Full };
        if (!said.Ok)
        {
            file.Save(f);
            Fail(live, null, $"Failed: {said.Error}", said.Error, null);
            failed.Add($"Microsoft 365: {said.Error}");
            return null;
        }
        // What Entra Connect could send: changes made before it started, from DCs that all had them by then.
        var events = new List<SyncEvent>();
        foreach (var c in scan.Changes.Where(c => c.At <= started && c.Kind is not ("computer" or "ou" or "other")))
        {
            var everywhere = c.Places.Values.All(p => p is SyncCompare.Ok or SyncCompare.Origin);
            var key = Key(c);
            if (f.Sent.GetValueOrDefault(key) == SyncCompare.Ok)
            {
                continue;
            }
            f.Sent[key] = everywhere ? SyncCompare.Ok : "notyet";
            events.Add(Event(c, "Entra Connect", "Microsoft 365", everywhere ? SyncCompare.Ok : "notyet", true,
                everywhere ? null : "This change had not replicated to every domain controller yet, so Entra Connect may not have exported it."));
        }
        // Forget what's long gone.
        foreach (var old in f.Sent.Keys.Where(k => DateTime.TryParse(k[(k.IndexOf('|') + 1)..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) && at < since.AddDays(-7)).ToList())
        {
            f.Sent.Remove(old);
        }
        file.Save(f);
        Save(events);
        Set(live, null, "ok", 1, $"Synced at {{at}}{(said.Sent is { } n ? $" ({n} change{(n == 1 ? "" : "s")} exported)" : "")}");
        return scan;
    }

    private static SyncEvent Event(SyncChange c, string from, string to, string s, bool m, string? why) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..12], At = DateTime.UtcNow, From = from, To = to, Change = c.Id, Kind = c.Kind, Name = c.Name, Sub = c.Sub, What = c.What,
        Details = [.. c.Details], S = s, M = m, Why = why,
    };

    private void Save(List<SyncEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }
        var f = file.Read();
        f.Feed = [.. events, .. f.Feed.Take(KeepFeed - events.Count)];
        file.Save(f);
    }

    // A part of the sync (a DC by name, or Microsoft 365 for null) moving on.
    private void Set(Live live, string? dc, string s, double f, string label)
    {
        lock (gate)
        {
            var p = dc is null ? live.M365 : live.Dcs.GetValueOrDefault(dc);
            if (p is null || (p.S == "bad" && s != "bad"))
            {
                return;
            }
            (p.S, p.F, p.Label) = (s, Math.Clamp(f, 0, 1), label);
        }
    }

    private void Fail(Live live, string? dc, string label, string? error, string? advice, bool keepGoing = false)
    {
        lock (gate)
        {
            var p = dc is null ? live.M365 : live.Dcs.GetValueOrDefault(dc);
            if (p is null)
            {
                return;
            }
            p.Error ??= error;
            p.Advice ??= advice;
            if (!keepGoing)
            {
                (p.S, p.Label) = ("bad", label);
            }
        }
    }

    private Part? Get(Live live, string dc)
    {
        lock (gate)
        {
            return live.Dcs.GetValueOrDefault(dc);
        }
    }
}

public static class SyncApi
{
    public sealed record SyncForm(string? What, bool Full);

    public static void AddSync(this IServiceCollection services)
    {
        services.AddSingleton<IReplicator>(_ => OperatingSystem.IsWindows() ? new WindowsReplicator() : new NoReplicator());
        services.AddSingleton<SyncEngine>();
    }

    public static void MapSyncApi(this WebApplication app)
    {
        // The page: domain controllers, Microsoft 365, what changed and where it is, and the copies seen. Quick while a sync runs.
        app.MapGet("/api/sync", (bool? fresh, HttpContext context, SyncEngine sync) => UsersApi.Try(context, Permissions.AdView, async () =>
            Results.Ok(await sync.View(fresh == true, context.RequestAborted)), change: false, whole: true));

        // Sync all, Sync DCs or Sync Microsoft 365 (Full: an Initial sync). It runs on; the page asks how it's going.
        app.MapPost("/api/sync", (SyncForm form, HttpContext context, SyncEngine sync) => UsersApi.Try(context, Permissions.AdChange, async () =>
            sync.Start(form.What ?? "", form.Full, ActivityLog.Who(context), ActivityLog.Address(context)) is { } problem
                ? Results.Json(new { error = problem }, statusCode: StatusCodes.Status409Conflict)
                : Results.Ok(await sync.View(false, context.RequestAborted)), whole: true));
    }
}
