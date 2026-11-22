using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using DnsClient;
using Microsoft.Extensions.Caching.Memory;

namespace RabbidLab.Server;

/// <summary>A domain controller DNS gives for a domain, and whether it answered on the LDAP port, in how many milliseconds.</summary>
public sealed record FoundController(string Name, bool Online, int? Ms);

/// <summary>Why no domain controller answers, as the sign-in page's warning says it: a bold title, then what to do.</summary>
public sealed record NoController(string Title, string Text)
{
    /// <summary>The warning for what was found: a VPN on the server, names its DNS can't look up, no controllers in DNS, or no answer.</summary>
    public static NoController Say(string domain, string server, string? vpn, int found, string? lost)
    {
        var who = $"The {Product.Title} server ({server})";
        if (vpn is not null)
        {
            return new("VPN detected.", $"{who} is connected to a VPN ({vpn}) and can't reach a domain controller for {domain}. Disconnect from the VPN, or use a local account.");
        }
        if (found == 0)
        {
            return new("Domain controllers not found.", $"{who} finds no domain controllers for {domain} in DNS. Check that it is on the {domain} network and uses its DNS servers, or use a local account.");
        }
        return lost is not null
            ? new("Domain controllers not found.", $"{who} can't look up {lost} in DNS. Check that it is on the {domain} network and uses its DNS servers, or use a local account.")
            : new("Domain controllers not reachable.", $"{who} can't reach a domain controller for {domain}. Check its network connection and any firewall in between, or use a local account.");
    }
}

/// <summary>The Active Directory domains this machine can find, and their domain controllers.</summary>
public interface IDomainFinder
{
    Task<IReadOnlyList<string>> Domains(CancellationToken stop);

    Task<IReadOnlyList<FoundController>> Controllers(string domain, CancellationToken stop);

    /// <summary>Why none of a domain's controllers answer, for the sign-in page; null when one does.</summary>
    Task<NoController?> WhyNone(string domain, IReadOnlyList<FoundController> found, CancellationToken stop) => Task.FromResult<NoController?>(null);

    /// <summary>Where to connect for a domain: the domain controller chosen, else the quickest that answers now, else the domain's own name.</summary>
    async Task<string> Host(string domain, string? server, CancellationToken stop) =>
        !string.IsNullOrWhiteSpace(server) ? server.Trim().ToLowerInvariant()
        : (await Controllers(domain, stop)).FirstOrDefault(c => c.Online)?.Name ?? domain;
}

/// <summary>
/// What setup offers in its Domain and Domain controller boxes, found through DNS so it works the same in Docker as on
/// Windows. The domains are this machine's own and its network cards' DNS suffixes, and their parents, kept when DNS has
/// domain controllers for them. A domain's controllers are each tried on the LDAP port for 1.5 seconds at most, all at
/// once, the quickest first. Kept for a minute once one answers, else for 5 seconds.
/// </summary>
public sealed partial class DomainFinder(IMemoryCache cache) : IDomainFinder
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
            entry.AbsoluteExpirationRelativeToNow = Keep(tried.Count(c => c.Online));
            return (IReadOnlyList<FoundController>) tried.OrderByDescending(c => c.Online).ThenBy(c => c.Ms ?? int.MaxValue).ThenBy(c => c.Name).ToList();
        }) ?? [];

    public async Task<NoController?> WhyNone(string domain, IReadOnlyList<FoundController> found, CancellationToken stop)
    {
        if (found.Any(c => c.Online))
        {
            return null;
        }
        var looked = await Task.WhenAll(found.Take(5).Select(async c => (c.Name, Found: await Resolves(c.Name, stop))));
        return NoController.Say(domain, Environment.MachineName, Vpn(), found.Count, looked.FirstOrDefault(x => !x.Found).Name);
    }

    /// <summary>A VPN connected on this machine, by its name: a dial-up style (PPP) connection or a known VPN adapter.</summary>
    private static string? Vpn()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up
                && (n.NetworkInterfaceType == NetworkInterfaceType.Ppp || VpnName().IsMatch($"{n.Name} {n.Description}")))?.Name;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\bvpn\b|wireguard|wintun|openvpn|tap-windows|anyconnect|globalprotect|fortinet|forticlient|zscaler|pulse secure|nordlynx", RegexOptions.IgnoreCase)]
    private static partial Regex VpnName();

    /// <summary>Whether this machine's own resolver finds an address for a name, as a connection to it would.</summary>
    private static async Task<bool> Resolves(string host, CancellationToken stop)
    {
        using var time = CancellationTokenSource.CreateLinkedTokenSource(stop);
        time.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            return (await System.Net.Dns.GetHostAddressesAsync(host, time.Token)).Length > 0;
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

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
