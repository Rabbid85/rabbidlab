using System.Net.NetworkInformation;
using System.Net.Sockets;
using DnsClient;
using Microsoft.Extensions.Caching.Memory;

namespace RabbidLab.Server;

/// <summary>A domain controller DNS gives for a domain, and whether it answered on the LDAP port, in how many milliseconds.</summary>
public sealed record FoundController(string Name, bool Online, int? Ms);

/// <summary>The Active Directory domains this machine can find, and their domain controllers.</summary>
public interface IDomainFinder
{
    Task<IReadOnlyList<string>> Domains(CancellationToken stop);

    Task<IReadOnlyList<FoundController>> Controllers(string domain, CancellationToken stop);

    /// <summary>Where to connect for a domain: the domain controller chosen, else the quickest that answers now, else the domain's own name.</summary>
    async Task<string> Host(string domain, string? server, CancellationToken stop) =>
        !string.IsNullOrWhiteSpace(server) ? server.Trim().ToLowerInvariant()
        : (await Controllers(domain, stop)).FirstOrDefault(c => c.Online)?.Name ?? domain;
}

/// <summary>
/// What setup offers in its Domain and Domain controller boxes, found through DNS so it works the same in Docker as on
/// Windows. The domains are this machine's own and its network cards' DNS suffixes, and their parents, kept when DNS has
/// domain controllers for them. A domain's controllers are each tried on the LDAP port for 1.5 seconds at most, all at
/// once, the quickest first. Kept for a minute.
/// </summary>
public sealed class DomainFinder(IMemoryCache cache) : IDomainFinder
{
    private static readonly LookupClient Dns = new(new LookupClientOptions { Timeout = TimeSpan.FromSeconds(2), Retries = 1, UseCache = true });

    public async Task<IReadOnlyList<string>> Domains(CancellationToken stop) =>
        await cache.GetOrCreateAsync("domains", async entry =>
        {
            var found = await Task.WhenAll(Candidates().Select(async d => (Domain: d, Dcs: await Srv(d, stop))));
            var domains = found.Where(f => f.Dcs.Count > 0).Select(f => f.Domain).ToList();
            entry.AbsoluteExpirationRelativeToNow = Keep(domains.Count);
            return (IReadOnlyList<string>) domains;
        }) ?? [];

    public async Task<IReadOnlyList<FoundController>> Controllers(string domain, CancellationToken stop) =>
        await cache.GetOrCreateAsync("controllers:" + domain.ToLowerInvariant(), async entry =>
        {
            var tried = await Task.WhenAll((await Srv(domain, stop)).Take(50).Select(async host =>
            {
                var ms = await Answers(host);
                return new FoundController(host, ms is not null, ms);
            }));
            entry.AbsoluteExpirationRelativeToNow = Keep(tried.Length);
            return (IReadOnlyList<FoundController>) tried.OrderByDescending(c => c.Online).ThenBy(c => c.Ms ?? int.MaxValue).ThenBy(c => c.Name).ToList();
        }) ?? [];

    // Nothing found may just be DNS failing for a moment, so that's asked again soon.
    private static TimeSpan Keep(int found) => found > 0 ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(5);

    /// <summary>Names this machine is told it's in: its own domain, its network cards' DNS suffixes, the resolver's search list, and their parents.</summary>
    private static IEnumerable<string> Candidates()
    {
        var names = new List<string> { IPGlobalProperties.GetIPGlobalProperties().DomainName };
        try
        {
            names.AddRange(NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Select(n => n.GetIPProperties().DnsSuffix));
        }
        catch (NetworkInformationException)
        {
            // No network cards to read: the machine's own domain still counts.
        }
        if (File.Exists("/etc/resolv.conf"))
        {
            names.AddRange(File.ReadLines("/etc/resolv.conf").Select(l => l.Trim().Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length > 1 && p[0] is "search" or "domain").SelectMany(p => p.Skip(1)));
        }
        var all = new List<string>();
        foreach (var name in names.Select(n => (n ?? "").Trim().TrimEnd('.').ToLowerInvariant()).Where(n => n.Contains('.') && Uri.CheckHostName(n) == UriHostNameType.Dns))
        {
            for (var d = name; d.Contains('.'); d = d[(d.IndexOf('.') + 1)..])
            {
                all.Add(d);
            }
        }
        return all.Distinct().Take(12);
    }

    /// <summary>The domain controllers DNS gives for a domain (its _ldap._tcp.dc._msdcs record), or none.</summary>
    private static async Task<IReadOnlyList<string>> Srv(string domain, CancellationToken stop)
    {
        try
        {
            var answer = await Dns.QueryAsync($"_ldap._tcp.dc._msdcs.{domain}", QueryType.SRV, cancellationToken: stop);
            return answer.Answers.SrvRecords().OrderBy(r => r.Priority).ThenByDescending(r => r.Weight).Select(r => r.Target.Value.TrimEnd('.').ToLowerInvariant())
                .Where(h => h.Length > 0).Distinct().ToList();
        }
        // A request that was cancelled says so, and nothing is kept for the next one; DNS's own timeout is just no answer.
        catch (Exception e) when (e is DnsResponseException or SocketException || (e is OperationCanceledException && !stop.IsCancellationRequested))
        {
            return [];
        }
    }

    /// <summary>How many milliseconds a domain controller took to accept a connection on the LDAP port, or null when it didn't.</summary>
    private static async Task<int?> Answers(string host)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, 389, stop.Token);
            return (int) Math.Max(1, watch.ElapsedMilliseconds);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            return null;
        }
    }
}
