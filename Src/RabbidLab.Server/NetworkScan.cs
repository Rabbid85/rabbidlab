using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace RabbidLab.Server;

/// <summary>A range of addresses to scan: a name, its CIDR, whether it's scanned, and where it came from (local or added).</summary>
public sealed class NetRange
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Cidr { get; set; } = "";
    public bool On { get; set; } = true;
    public string From { get; set; } = "added";
}

/// <summary>A device that answered on the network: what it is, as worked out from its reply, its ports, name, vendor and SNMP.</summary>
public sealed class NetDevice
{
    public string Id { get; set; } = "";
    public string Ip { get; set; } = "";
    public string? Mac { get; set; }
    public string? Name { get; set; }
    public string? NameFrom { get; set; }
    public string? Vendor { get; set; }
    public string Kind { get; set; } = "unknown";
    public string? Why { get; set; }
    public string? Os { get; set; }
    public string? Custom { get; set; }
    public string? CustomKind { get; set; }
    public List<int> Ports { get; set; } = [];
    public int? Ping { get; set; }
    public int? Ttl { get; set; }
    public string? Snmp { get; set; }
    public DateTime First { get; set; }
    public DateTime Last { get; set; }
    public bool New { get; set; }
    public bool Online { get; set; } = true;
    public string Range { get; set; } = "";
}

public sealed class NetworkFile
{
    public List<NetRange> Ranges { get; set; } = [];
    public bool Seeded { get; set; }
    public string Schedule { get; set; } = "day";
    public string Community { get; set; } = "public";
    public List<NetDevice> Devices { get; set; } = [];
    public DateTime? LastScan { get; set; }
}

public sealed record NetRangeForm(string? Id, string? Name, string? Cidr, bool On);
public sealed record NetSettingsForm(List<NetRangeForm>? Ranges, string? Schedule, string? Community);
public sealed record NetEditForm(string? Name, string? Kind);
public sealed record NetIdsForm(List<string>? Ids);
public sealed record NetScanForm(string? Range);

/// <summary>What one address said: whether it answered, how fast, its TTL, MAC, names, open ports and SNMP.</summary>
public sealed record NetFacts(string Ip, bool Alive, int? Ping, int? Ttl, string? Mac, string? Dns, string? NetBios, List<int> Ports, string? SnmpDescr, string? SnmpName,
    bool Gateway, string? Os);

/// <summary>How the scan reaches the network. Tests stand in for it.</summary>
public interface INetProbe
{
    Task<(bool Alive, int? Ms, int? Ttl)> Ping(IPAddress ip, CancellationToken stop);
    Task<bool?> Tcp(IPAddress ip, int port, CancellationToken stop); // true open, false refused (it's there), null no answer
    Dictionary<string, string> Arp();
    Task<string?> Dns(IPAddress ip, CancellationToken stop);
    Task<(string? Name, string? Mac)> NetBios(IPAddress ip, CancellationToken stop);
    Task<(string? Descr, string? Name)> Snmp(IPAddress ip, string community, CancellationToken stop);
    IReadOnlyList<string> Gateways();
    IReadOnlyList<(string Cidr, string Name)> LocalNetworks();
}

/// <summary>
/// Network discovery, like Angry IP Scanner: every address in the ranges is pinged, and those that block ping are still found
/// when they answer on a common port or show in this server's ARP table. What answers is identified by its ports, its
/// names (DNS, NetBIOS), its MAC vendor, SNMP and the TTL of its reply. Kept in network.json. Reading only: nothing is sent
/// to a device beyond the questions any scanner asks.
/// </summary>
public sealed class NetworkScan(DataFolder folder, INetProbe probe, Clients clients, ActivityLog log, TimeProvider clock, ILogger<NetworkScan> logger)
{
    public static readonly int[] PortList = [21, 22, 23, 53, 80, 88, 135, 139, 389, 443, 445, 515, 554, 631, 902, 3389, 5000, 5001, 5985, 8000, 8080, 9100, 62078];
    // The first ports tried on an address that doesn't answer ping: Windows, remote access and the web.
    private static readonly int[] Knock = [445, 3389, 135, 22, 80, 443];
    public static readonly IReadOnlyDictionary<string, string> Schedules = new Dictionary<string, string>
    {
        ["10m"] = "Every 10 minutes", ["30m"] = "Every 30 minutes", ["1h"] = "Every hour", ["2h"] = "Every 2 hours", ["6h"] = "Every 6 hours", ["day"] = "Every day at 6:00 AM", ["off"] = "Only when someone selects Scan now",
    };
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>
    {
        ["router"] = "Router", ["switch"] = "Switch", ["ap"] = "Access point", ["server"] = "Server", ["pc"] = "PC", ["printer"] = "Printer", ["camera"] = "Camera",
        ["phone"] = "Phone or tablet", ["nas"] = "NAS", ["iot"] = "Smart device", ["unknown"] = "Unknown",
    };
    public const int MostRanges = 16;
    public const int MostAddresses = 4094; // a /20
    private const int AtOnce = 96, Identify = 24;
    private static readonly TimeSpan IdentifyFor = TimeSpan.FromSeconds(6); // the longest one device's questions may take
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<NetworkFile> file = folder.Json<NetworkFile>("network.json", Json);
    private readonly object gate = new();
    private Running? now;

    /// <summary>A scan under way: who started it, and how far each range has got.</summary>
    private sealed class Running
    {
        public required string By { get; init; }
        public required DateTime Started { get; init; }
        public required List<NetRange> Ranges { get; init; }
        public ConcurrentDictionary<string, int> Done { get; } = new();
        public ConcurrentDictionary<string, byte> Busy { get; } = new(); // the addresses being pinged or identified just now
        public int Found;
        public int Asked; // devices identified so far
        public bool Identifying;
    }

    public bool Busy => now is not null;

    // --- Ranges --------------------------------------------------------------------------------------------------------

    /// <summary>The addresses a CIDR holds, without its network and broadcast addresses; null when it isn't IPv4 CIDR or is too big.</summary>
    public static List<IPAddress>? Addresses(string cidr)
    {
        var slash = cidr.IndexOf('/');
        if (slash < 0 || !IPAddress.TryParse(cidr[..slash].Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || !int.TryParse(cidr[(slash + 1)..].Trim(), out var bits) || bits is < 20 or > 30)
        {
            return null;
        }
        var mask = uint.MaxValue << (32 - bits);
        var start = BinaryPrimitives.ReadUInt32BigEndian(ip.GetAddressBytes()) & mask;
        var size = 1u << (32 - bits);
        var list = new List<IPAddress>((int) size - 2);
        for (var i = 1u; i < size - 1; i++)
        {
            var b = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(b, start + i);
            list.Add(new IPAddress(b));
        }
        return list;
    }

    /// <summary>A CIDR written the usual way, with its network address: 10.10.1.7/24 is 10.10.1.0/24. Null when it's no good.</summary>
    public static string? Normal(string? cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr) || Addresses(cidr.Trim()) is null)
        {
            return null;
        }
        var slash = cidr.IndexOf('/');
        var bits = int.Parse(cidr[(slash + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        var mask = uint.MaxValue << (32 - bits);
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, BinaryPrimitives.ReadUInt32BigEndian(IPAddress.Parse(cidr[..slash].Trim()).GetAddressBytes()) & mask);
        return $"{new IPAddress(b)}/{bits}";
    }

    private static bool InRange(string ip, string cidr) => Addresses(cidr)?.Any(a => a.ToString() == ip) == true;

    // The networks this server is on, once, as the first ranges.
    private NetworkFile Seeded()
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Seeded)
            {
                return f;
            }
            var ranges = probe.LocalNetworks().Select((n, i) => new NetRange { Id = $"r{i + 1}", Name = n.Name, Cidr = n.Cidr, From = "local" }).Take(MostRanges).ToList();
            var next = Copy(f);
            next.Ranges = ranges;
            next.Seeded = true;
            file.Save(next);
            return next;
        }
    }

    private static NetworkFile Copy(NetworkFile f) => JsonSerializer.Deserialize<NetworkFile>(JsonSerializer.Serialize(f, Json), Json)!;

    // --- Scanning ------------------------------------------------------------------------------------------------------

    /// <summary>Starts a scan of every range that's on, or of one range. False when one is already running or there's nothing to scan.</summary>
    public bool Start(string by, string? rangeId = null)
    {
        var f = Seeded();
        var ranges = f.Ranges.Where(r => r.On && (rangeId is null || r.Id == rangeId)).ToList();
        lock (gate)
        {
            if (now is not null || ranges.Count == 0)
            {
                return false;
            }
            now = new Running { By = by, Started = clock.GetUtcNow().UtcDateTime, Ranges = ranges };
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Scan(now!, CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "The network scan failed");
            }
            finally
            {
                lock (gate)
                {
                    now = null;
                }
            }
        });
        return true;
    }

    /// <summary>The whole scan, for tests and the schedule: waits until it's done.</summary>
    public async Task<bool> Run(string by, string? rangeId = null)
    {
        if (!Start(by, rangeId))
        {
            return false;
        }
        while (Busy)
        {
            await Task.Delay(20);
        }
        return true;
    }

    private async Task Scan(Running run, CancellationToken stop)
    {
        var gateways = probe.Gateways();
        var oses = Oses();
        var first = file.Read().LastScan is null;
        var community = file.Read().Community;
        var seen = new ConcurrentDictionary<string, byte>();
        var asking = new ConcurrentBag<Task>();
        using var gate = new SemaphoreSlim(Identify);
        // This server's ARP table, read again at most once a second: a device that blocks ping and every port still
        // answers ARP, so it's found right where the sweep is, not after it.
        var arpGate = new object();
        var arpNow = probe.Arp();
        var arpAt = Environment.TickCount64;
        Dictionary<string, string> Arp()
        {
            lock (arpGate)
            {
                if (Environment.TickCount64 - arpAt > 1000)
                {
                    arpNow = probe.Arp();
                    arpAt = Environment.TickCount64;
                }
                return arpNow;
            }
        }
        // What one is, as soon as it has answered, a few at a time while the ping goes on: so each device is identified,
        // and shows on the page, right where the sweep finds it. One that's slow at everything gets IdentifyFor at most,
        // then is kept with what's known: it never holds the scan up.
        async Task Identify1(NetRange r, IPAddress ip, int? ms, int? ttl)
        {
            await gate.WaitAsync(stop);
            var key = ip.ToString();
            run.Busy[key] = 0;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
            limit.CancelAfter(IdentifyFor);
            try
            {
                Keep(r, await Ask(ip, ms, ttl, Arp(), gateways, community, oses, limit.Token).WaitAsync(IdentifyFor, stop), first);
            }
            catch (Exception e) when (!stop.IsCancellationRequested && e is System.TimeoutException or OperationCanceledException)
            {
                Keep(r, new NetFacts(key, true, ms, ttl, Arp().GetValueOrDefault(key), null, null, [], null, null, gateways.Contains(key), null), first, partial: true);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogDebug(e, "Could not identify {Ip}", ip);
            }
            finally
            {
                run.Busy.TryRemove(key, out _);
                Interlocked.Increment(ref run.Asked);
                gate.Release();
            }
        }
        // Every address: ping, then a knock on a few ports for those that block ping.
        foreach (var r in run.Ranges)
        {
            var list = Addresses(r.Cidr) ?? [];
            run.Done[r.Id] = 0;
            await Parallel.ForEachAsync(list, new ParallelOptions { MaxDegreeOfParallelism = AtOnce, CancellationToken = stop }, async (ip, token) =>
            {
                var key = ip.ToString();
                run.Busy[key] = 0;
                (bool Up, int? Ms, int? Ttl) reply = (false, null, null);
                bool? tcp = null;
                try
                {
                    reply = await probe.Ping(ip, token);
                    if (!reply.Up)
                    {
                        foreach (var port in Knock)
                        {
                            if ((tcp = await probe.Tcp(ip, port, token)) is not null)
                            {
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    run.Busy.TryRemove(key, out _);
                    run.Done.AddOrUpdate(r.Id, 1, (_, n) => n + 1);
                }
                // Started after its ping is done with Busy, so the two never take each other's mark away.
                if ((reply.Up || tcp is not null || Arp().ContainsKey(key)) && seen.TryAdd(key, 0))
                {
                    Interlocked.Increment(ref run.Found);
                    asking.Add(Identify1(r, ip, reply.Ms, reply.Ttl));
                }
            });
        }
        // Anything that answered ARP only after its own turn (rare): found now.
        var arp = probe.Arp();
        foreach (var r in run.Ranges)
        {
            foreach (var (ip, _) in arp.Where(a => !seen.ContainsKey(a.Key) && InRange(a.Key, r.Cidr)))
            {
                if (seen.TryAdd(ip, 0))
                {
                    Interlocked.Increment(ref run.Found);
                    asking.Add(Identify1(r, IPAddress.Parse(ip), null, null));
                }
            }
        }
        run.Identifying = true;
        await Task.WhenAll(asking);
        // Whatever didn't answer in these ranges is offline now.
        lock (file.Gate)
        {
            var f = Copy(file.Read());
            foreach (var d in f.Devices.Where(d => run.Ranges.Any(r => r.Id == d.Range) && !seen.ContainsKey(d.Ip)))
            {
                d.Online = false;
                d.Ping = null;
            }
            f.LastScan = clock.GetUtcNow().UtcDateTime;
            file.Save(f);
        }
    }

    /// <summary>Asks one address everything: its ports, names, MAC and SNMP.</summary>
    private async Task<NetFacts> Ask(IPAddress ip, int? ms, int? ttl, Dictionary<string, string> arp, IReadOnlyList<string> gateways, string community, Dictionary<string, string> oses, CancellationToken stop)
    {
        var ports = new ConcurrentBag<int>();
        await Parallel.ForEachAsync(PortList, new ParallelOptions { MaxDegreeOfParallelism = 12, CancellationToken = stop }, async (p, token) =>
        {
            if (await probe.Tcp(ip, p, token) == true)
            {
                ports.Add(p);
            }
        });
        var dns = await probe.Dns(ip, stop);
        var (nb, nbMac) = await probe.NetBios(ip, stop);
        var windows = ports.Contains(445) || ports.Contains(135);
        var (descr, sysName) = windows ? (null, null) : await probe.Snmp(ip, community, stop);
        var mac = arp.GetValueOrDefault(ip.ToString()) ?? nbMac;
        var name = Short(dns) ?? nb ?? Short(sysName);
        return new NetFacts(ip.ToString(), true, ms, ttl, mac, dns, nb, [.. ports.Order()], descr, sysName, gateways.Contains(ip.ToString()),
            name is null ? null : oses.GetValueOrDefault(name.ToUpperInvariant()));
    }

    // The operating systems Devices knows, by computer name.
    private Dictionary<string, string> Oses()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in clients.All().Where(c => c.Os is not null))
        {
            map[c.Computer.ToUpperInvariant()] = c.Os!;
        }
        return map;
    }

    private static string? Short(string? name) => string.IsNullOrWhiteSpace(name) || IPAddress.TryParse(name, out _) ? null : name.Split('.')[0];

    // What was found joins the list: a device already known by its MAC (or address) is updated; a new one is New, unless
    // this is the very first scan, and says so in the activity log, which alerts can watch. partial: only that it
    // answered (its questions ran out of time), so a known device keeps its ports, type and names from before.
    private void Keep(NetRange r, NetFacts facts, bool first, bool partial = false)
    {
        var at = clock.GetUtcNow().UtcDateTime;
        var (kind, why) = Classify(facts, Vendor(facts.Mac));
        NetDevice? added = null;
        lock (file.Gate)
        {
            var f = Copy(file.Read());
            var d = f.Devices.FirstOrDefault(x => facts.Mac is not null && string.Equals(x.Mac, facts.Mac, StringComparison.OrdinalIgnoreCase))
                ?? f.Devices.FirstOrDefault(x => x.Ip == facts.Ip && (x.Mac is null || facts.Mac is null));
            if (d is null)
            {
                d = new NetDevice { Id = Guid.NewGuid().ToString("N")[..12], First = at, New = !first };
                f.Devices.Add(d);
                added = d;
            }
            d.Ip = facts.Ip;
            d.Mac = facts.Mac ?? d.Mac;
            d.Range = r.Id;
            if (partial && added is null)
            {
                d.Ping = facts.Ping;
                d.Ttl = facts.Ttl ?? d.Ttl;
                d.Last = at;
                d.Online = true;
                file.Save(f);
                return;
            }
            var name = Short(facts.Dns) ?? facts.NetBios ?? Short(facts.SnmpName);
            if (name is not null)
            {
                d.Name = name;
                d.NameFrom = Short(facts.Dns) is not null ? "DNS" : facts.NetBios is not null ? "NetBIOS" : "SNMP";
            }
            d.Vendor = Vendor(d.Mac) ?? d.Vendor;
            d.Kind = kind;
            d.Why = why;
            d.Os = facts.Os ?? d.Os;
            d.Ports = facts.Ports;
            d.Ping = facts.Ping;
            d.Ttl = facts.Ttl ?? d.Ttl;
            d.Snmp = facts.SnmpDescr ?? d.Snmp;
            d.Last = at;
            d.Online = true;
            file.Save(f);
        }
        if (added is { New: true })
        {
            log.Record("Network scan", facts.Ip, "network", "network.new", $"{added.Name ?? facts.Ip} ({facts.Ip}) joined the network", "ok",
                string.Join(" · ", new[] { Kinds.GetValueOrDefault(kind), added.Vendor, added.Mac }.Where(x => !string.IsNullOrWhiteSpace(x))));
        }
    }

    /// <summary>Scans a few devices again, now: their ping, ports and names.</summary>
    public async Task Rescan(IReadOnlyCollection<string> ids, CancellationToken stop)
    {
        var f = Seeded();
        var arp = probe.Arp();
        var gateways = probe.Gateways();
        var oses = Oses();
        foreach (var d in f.Devices.Where(d => ids.Contains(d.Id)).ToList())
        {
            var ip = IPAddress.Parse(d.Ip);
            var (up, ms, ttl) = await probe.Ping(ip, stop);
            var r = f.Ranges.FirstOrDefault(x => x.Id == d.Range) ?? new NetRange { Id = d.Range };
            if (!up && await probe.Tcp(ip, 445, stop) is null && await probe.Tcp(ip, 80, stop) is null && !probe.Arp().ContainsKey(d.Ip))
            {
                lock (file.Gate)
                {
                    var g = Copy(file.Read());
                    if (g.Devices.FirstOrDefault(x => x.Id == d.Id) is { } x)
                    {
                        x.Online = false;
                        x.Ping = null;
                    }
                    file.Save(g);
                }
                continue;
            }
            Keep(r, await Ask(ip, ms, ttl, arp, gateways, f.Community, oses, stop), first: false);
        }
    }

    // --- What it is ----------------------------------------------------------------------------------------------------

    private static readonly Lazy<Dictionary<string, string>> Oui = new(() =>
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var s = typeof(NetworkScan).Assembly.GetManifestResourceStream("oui.tsv.gz");
        if (s is null)
        {
            return map;
        }
        using var gz = new GZipStream(s, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            var tab = line.IndexOf('\t');
            if (tab == 6)
            {
                map[line[..6]] = line[7..];
            }
        }
        return map;
    });

    /// <summary>The maker of a network card, from the first half of its MAC address; private for a randomised one, as phones use on Wi-Fi.</summary>
    public static string? Vendor(string? mac)
    {
        var hex = new string((mac ?? "").Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (hex.Length < 6)
        {
            return null;
        }
        if ((Convert.ToByte(hex[..2], 16) & 0x02) != 0)
        {
            return "Private address";
        }
        return Oui.Value.GetValueOrDefault(hex[..6]);
    }

    private static bool Has(string? text, params string[] words) => text is not null && words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What a device is from what it answered, and why: its gateway role, SNMP description, ports, vendor, the OS Devices knows
    /// for its name, and the TTL of its ping reply (128 is Windows, 64 Linux, macOS, iOS and Android, 255 network gear).
    /// </summary>
    public static (string Kind, string Why) Classify(NetFacts f, string? vendor)
    {
        var p = f.Ports;
        bool Open(params int[] ports) => ports.Any(p.Contains);
        if (f.Gateway)
        {
            return ("router", "It is the default gateway.");
        }
        if (Has(f.SnmpDescr, "printer", "laserjet", "jetdirect", "officejet", "brother", "epson", "canon", "kyocera", "xerox", "ricoh", "lexmark", "konica", "sharp mx") || Open(9100, 631, 515))
        {
            return ("printer", Open(9100, 631, 515) ? "Printing ports 9100, 631 or 515 are open." : "SNMP reports a printer.");
        }
        if (Open(554) || Has(vendor, "hikvision", "dahua", "reolink", "axis comm", "amcrest", "hanwha", "vivotek", "uniview"))
        {
            return ("camera", Open(554) ? "The RTSP video port 554 is open." : $"{vendor} makes cameras.");
        }
        if (Open(902) || Has(f.SnmpDescr, "esxi", "vmware esx"))
        {
            return ("server", "VMware ESXi answers on port 902.");
        }
        if (Has(vendor, "synology", "qnap", "asustor", "western digital", "buffalo") || (Open(5000, 5001) && Open(445)))
        {
            return ("nas", Has(vendor, "synology", "qnap", "asustor", "western digital", "buffalo") ? $"{vendor} makes NAS devices." : "Ports 5000 and 445 are open, as on a NAS.");
        }
        if (Has(f.SnmpDescr, "access point", "uap", "unifi ap", "aironet", "instant on ap", "wap"))
        {
            return ("ap", "SNMP reports a wireless access point.");
        }
        if (Has(f.SnmpDescr, "switch", "cisco ios", "edgeswitch", "unifi switch", "procurve", "aruba", "junos", "routeros", "netgear", "tp-link", "dell networking"))
        {
            return ("switch", "SNMP reports network gear.");
        }
        // Windows: from what Devices knows of it, else its ports and TTL.
        var windowsTtl = f.Ttl is > 64 and <= 128;
        if (f.Os is not null || Open(135, 445, 3389) || windowsTtl)
        {
            if (Has(f.Os, "server") || Open(88, 389, 53) && Open(445))
            {
                return ("server", Has(f.Os, "server") ? $"{f.Os}." : "Domain controller ports 53, 88 and 389 are open.");
            }
            if (f.Os is not null)
            {
                return ("pc", $"{f.Os}.");
            }
            return ("pc", Open(135, 445, 3389) ? "Windows ports 135, 445 or 3389 are open." : "It answers ping with TTL 128, as Windows does.");
        }
        if (Has(vendor, "ubiquiti", "cisco", "aruba", "juniper", "mikrotik", "fortinet", "netgear", "tp-link", "d-link", "zyxel", "meraki", "ruckus", "extreme"))
        {
            return (Has(vendor, "ruckus") ? "ap" : "switch", $"{vendor} makes network gear.");
        }
        if (f.Ttl is > 128)
        {
            return ("switch", "It answers ping with TTL 255, as network gear does.");
        }
        if (Has(vendor, "espressif", "tuya", "shelly", "sonos", "nest", "ring", "ecobee", "philips lighting", "signify", "amazon technologies", "roku", "lifx", "wiz"))
        {
            return ("iot", $"{vendor} makes smart devices.");
        }
        if (Has(vendor, "apple", "samsung", "google", "xiaomi", "oneplus", "huawei", "motorola", "oppo", "vivo", "realme", "nothing technology") || vendor == "Private address")
        {
            return ("phone", vendor == "Private address" ? "It uses a private Wi-Fi address, as phones and tablets do." : $"{vendor} makes phones and tablets, and no computer ports are open.");
        }
        if (Has(vendor, "raspberry"))
        {
            return ("iot", "A Raspberry Pi.");
        }
        return ("unknown", "Nothing it answered says what it is.");
    }

    // --- What the page sees and changes ----------------------------------------------------------------------------------

    /// <summary>What the page shows. The SNMP community is a read password of sorts: only someone who can change the settings gets it.</summary>
    public object View(bool settings = true)
    {
        var f = Seeded();
        var run = now;
        // A device is managed when an approved client has its name; the page opens that computer in Devices.
        var managed = clients.All().Where(c => c.Approved is not null).GroupBy(c => c.Computer.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First().Id);
        string? ClientOf(NetDevice d) => (d.Custom ?? d.Name) is { } n ? managed.GetValueOrDefault(n.ToUpperInvariant()) : null;
        var at = clock.GetUtcNow().UtcDateTime;
        return new
        {
            devices = f.Devices.OrderBy(d => IpOrder(d.Ip)).Select(d => new
            {
                d.Id, d.Ip, d.Mac, name = d.Custom ?? d.Name, reported = d.Name, d.NameFrom, d.Vendor, kind = d.CustomKind ?? d.Kind, detected = d.Kind, d.Why, d.Os, custom = d.Custom is not null || d.CustomKind is not null,
                d.Ports, d.Ping, d.Ttl, d.Snmp, d.First, d.Last, isNew = d.New, d.Online, d.Range,
                client = ClientOf(d), managed = ClientOf(d) is not null,
            }),
            ranges = f.Ranges.Select(r => new { r.Id, r.Name, r.Cidr, r.On, r.From, size = Addresses(r.Cidr)?.Count ?? 0, done = run?.Done.GetValueOrDefault(r.Id) }),
            scan = run is null ? null : new { run.By, run.Started, ranges = run.Ranges.Select(r => r.Id), run.Found, asked = run.Asked, run.Identifying, busy = run.Busy.Keys.ToList() },
            lastScan = f.LastScan,
            schedule = f.Schedule,
            scheduleText = Schedules.GetValueOrDefault(f.Schedule, Schedules["day"]),
            next = Next(f, at),
            kinds = Kinds,
            community = settings ? f.Community : null,
        };
    }

    private static uint IpOrder(string ip) => IPAddress.TryParse(ip, out var a) && a.AddressFamily == AddressFamily.InterNetwork ? BinaryPrimitives.ReadUInt32BigEndian(a.GetAddressBytes()) : uint.MaxValue;

    /// <summary>When the schedule scans next; null when it only scans on request.</summary>
    public static DateTime? Next(NetworkFile f, DateTime utc)
    {
        var last = f.LastScan ?? DateTime.MinValue;
        TimeSpan? every = f.Schedule switch { "10m" => TimeSpan.FromMinutes(10), "30m" => TimeSpan.FromMinutes(30), "1h" => TimeSpan.FromHours(1), "2h" => TimeSpan.FromHours(2), "6h" => TimeSpan.FromHours(6), _ => null };
        if (every is { } e)
        {
            return last == DateTime.MinValue ? utc : last + e;
        }
        if (f.Schedule != "day")
        {
            return null;
        }
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local);
        var six = local.Date.AddHours(6);
        var lastLocal = last == DateTime.MinValue ? DateTime.MinValue : TimeZoneInfo.ConvertTimeFromUtc(last, TimeZoneInfo.Local);
        var due = lastLocal >= six || local < six ? (local < six ? six : six.AddDays(1)) : six;
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(due, DateTimeKind.Unspecified), TimeZoneInfo.Local);
    }

    /// <summary>Whether the schedule says it's time.</summary>
    public bool Due() => Next(Seeded(), clock.GetUtcNow().UtcDateTime) is { } next && next <= clock.GetUtcNow().UtcDateTime && !Busy && Seeded().Ranges.Any(r => r.On);

    public void Known(IReadOnlyCollection<string> ids)
    {
        lock (file.Gate)
        {
            var f = Copy(file.Read());
            f.Devices.Where(d => ids.Contains(d.Id)).ToList().ForEach(d => d.New = false);
            file.Save(f);
        }
    }

    /// <summary>A name and type of our own for a device, over what it reported; empty puts back what was detected.</summary>
    public NetDevice? Edit(string id, NetEditForm form)
    {
        if (form.Kind is { Length: > 0 } k && !Kinds.ContainsKey(k))
        {
            throw new ToolProblem("Pick a type from the list.");
        }
        lock (file.Gate)
        {
            var f = Copy(file.Read());
            var d = f.Devices.FirstOrDefault(x => x.Id == id);
            if (d is null)
            {
                return null;
            }
            d.Custom = string.IsNullOrWhiteSpace(form.Name) || form.Name.Trim() == d.Name ? null : form.Name.Trim()[..Math.Min(64, form.Name.Trim().Length)];
            d.CustomKind = string.IsNullOrWhiteSpace(form.Kind) || form.Kind == d.Kind ? null : form.Kind;
            file.Save(f);
            return d;
        }
    }

    public void Forget(IReadOnlyCollection<string> ids)
    {
        lock (file.Gate)
        {
            var f = Copy(file.Read());
            f.Devices.RemoveAll(d => ids.Contains(d.Id));
            file.Save(f);
        }
    }

    /// <summary>The ranges, the schedule and the SNMP community. A range that isn't IPv4 CIDR of /20 to /30 is refused with why.</summary>
    public void Set(NetSettingsForm form)
    {
        var ranges = new List<NetRange>();
        foreach (var r in form.Ranges ?? [])
        {
            var cidr = Normal(r.Cidr) ?? throw new ToolProblem($"{(string.IsNullOrWhiteSpace(r.Cidr) ? "A range" : r.Cidr)} is not a range Rabbid Lab can scan. Enter IPv4 CIDR from /20 to /30, such as 10.10.1.0/24.");
            if (ranges.Any(x => x.Cidr == cidr))
            {
                continue;
            }
            ranges.Add(new NetRange { Id = string.IsNullOrWhiteSpace(r.Id) ? $"r{Guid.NewGuid():N}"[..9] : r.Id, Name = string.IsNullOrWhiteSpace(r.Name) ? cidr : r.Name.Trim(), Cidr = cidr, On = r.On,
                From = file.Read().Ranges.FirstOrDefault(x => x.Id == r.Id)?.From ?? "added" });
        }
        if (ranges.Count > MostRanges)
        {
            throw new ToolProblem($"Up to {MostRanges} ranges can be scanned.");
        }
        if (form.Schedule is { } s && !Schedules.ContainsKey(s))
        {
            throw new ToolProblem("Pick how often to scan from the list.");
        }
        lock (file.Gate)
        {
            var f = Copy(Seeded());
            f.Ranges = ranges;
            f.Schedule = form.Schedule ?? f.Schedule;
            f.Community = string.IsNullOrWhiteSpace(form.Community) ? "public" : form.Community.Trim();
            file.Save(f);
        }
    }
}

/// <summary>The network as Windows reaches it: ping, TCP connects, the ARP table, DNS, NetBIOS and SNMP.</summary>
public sealed class WindowsNetProbe : INetProbe
{
    public async Task<(bool Alive, int? Ms, int? Ttl)> Ping(IPAddress ip, CancellationToken stop)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, TimeSpan.FromMilliseconds(800), cancellationToken: stop);
            return reply.Status == IPStatus.Success ? (true, (int) reply.RoundtripTime, reply.Options?.Ttl) : (false, null, null);
        }
        catch (PingException)
        {
            return (false, null, null);
        }
    }

    public async Task<bool?> Tcp(IPAddress ip, int port, CancellationToken stop)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(stop);
        wait.CancelAfter(400);
        using var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(ip, port, wait.Token);
            return true;
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return false; // something is there: it said no
        }
        catch (Exception e) when (e is SocketException || (e is OperationCanceledException && !stop.IsCancellationRequested))
        {
            return null;
        }
    }

    public Dictionary<string, string> Arp() => OperatingSystem.IsWindows() ? ArpTable() : [];

    [SupportedOSPlatform("windows")]
    private static Dictionary<string, string> ArpTable()
    {
        var size = 0;
        _ = GetIpNetTable(IntPtr.Zero, ref size, false);
        if (size <= 0)
        {
            return [];
        }
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetIpNetTable(buffer, ref size, false) != 0)
            {
                return [];
            }
            var bytes = new byte[size];
            Marshal.Copy(buffer, bytes, 0, size);
            return ArpRows(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>MIB_IPNETTABLE: a count, then 24-byte rows (index, MAC length, 8 bytes of MAC, the address, its type). Dynamic and static rows with a real MAC.</summary>
    public static Dictionary<string, string> ArpRows(byte[] bytes)
    {
        var map = new Dictionary<string, string>();
        if (bytes.Length < 4)
        {
            return map;
        }
        var n = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        for (var i = 0; i < n && 4 + (i + 1) * 24 <= bytes.Length; i++)
        {
            var row = bytes.AsSpan(4 + i * 24, 24);
            var len = BinaryPrimitives.ReadInt32LittleEndian(row[4..]);
            var type = BinaryPrimitives.ReadInt32LittleEndian(row[20..]);
            var mac = row.Slice(8, Math.Min(6, Math.Max(0, len)));
            if (len != 6 || type is not (3 or 4) || mac.ToArray().All(b => b == 0) || mac.ToArray().All(b => b == 0xFF) || (mac[0] & 1) != 0)
            {
                continue;
            }
            var ip = new IPAddress(row.Slice(16, 4));
            map[ip.ToString()] = string.Join(':', mac.ToArray().Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
        }
        return map;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetIpNetTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order);

    public async Task<string?> Dns(IPAddress ip, CancellationToken stop)
    {
        var look = System.Net.Dns.GetHostEntryAsync(ip.ToString(), stop);
        try
        {
            await Task.WhenAny(look, Task.Delay(1500, stop));
        }
        catch (OperationCanceledException)
        {
            // Stopped: no name.
        }
        if (look.IsCompletedSuccessfully)
        {
            return look.Result.HostName;
        }
        _ = look.ContinueWith(t => _ = t.Exception, TaskScheduler.Default); // no name for it; a late failure isn't left unobserved
        return null;
    }

    public async Task<(string? Name, string? Mac)> NetBios(IPAddress ip, CancellationToken stop)
    {
        using var udp = new UdpClient();
        try
        {
            await udp.SendAsync(NbStat, new IPEndPoint(ip, 137), stop);
            var got = udp.ReceiveAsync(stop).AsTask();
            await Task.WhenAny(got, Task.Delay(900, stop));
            if (got.IsCompletedSuccessfully)
            {
                return NbStatAnswer(got.Result.Buffer);
            }
            _ = got.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            return (null, null);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            return (null, null);
        }
    }

    /// <summary>A NetBIOS node status request for "*", the question nbtstat -A asks.</summary>
    public static readonly byte[] NbStat = Build();

    private static byte[] Build()
    {
        var b = new byte[50];
        b[0] = 0x52;
        b[1] = 0x4C; // transaction id
        b[5] = 1; // one question
        b[12] = 0x20;
        b[13] = (byte) 'C';
        b[14] = (byte) 'K';
        for (var i = 15; i < 45; i++)
        {
            b[i] = (byte) 'A';
        }
        b[47] = 0x21; // NBSTAT
        b[49] = 0x01; // IN
        return b;
    }

    /// <summary>The computer's name (the first unique name of type 00) and the MAC the answer ends with.</summary>
    public static (string? Name, string? Mac) NbStatAnswer(byte[] r)
    {
        if (r.Length < 57)
        {
            return (null, null);
        }
        var count = r[56];
        string? name = null;
        for (var i = 0; i < count && 57 + (i + 1) * 18 <= r.Length; i++)
        {
            var at = 57 + i * 18;
            var suffix = r[at + 15];
            var group = (r[at + 16] & 0x80) != 0;
            if (suffix == 0 && !group)
            {
                name = Encoding.ASCII.GetString(r, at, 15).Trim();
                break;
            }
        }
        var macAt = 57 + count * 18;
        string? mac = r.Length >= macAt + 6 && r.AsSpan(macAt, 6).ToArray().Any(b => b != 0)
            ? string.Join(':', r.AsSpan(macAt, 6).ToArray().Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))) : null;
        return (string.IsNullOrWhiteSpace(name) ? null : name, mac);
    }

    public Task<(string? Descr, string? Name)> Snmp(IPAddress ip, string community, CancellationToken stop) => Task.Run<(string?, string?)>(() =>
    {
        try
        {
            var reply = Messenger.Get(VersionCode.V2, new IPEndPoint(ip, 161), new OctetString(community),
                [new Variable(new ObjectIdentifier("1.3.6.1.2.1.1.1.0")), new Variable(new ObjectIdentifier("1.3.6.1.2.1.1.5.0"))], 700);
            string? Text(int i) => i < reply.Count && reply[i].Data is OctetString s && s.ToString().Trim() is { Length: > 0 } t ? t[..Math.Min(200, t.Length)] : null;
            return (Text(0), Text(1));
        }
        catch (Exception e) when (e is Lextm.SharpSnmpLib.Messaging.TimeoutException or SocketException or SnmpException or ErrorException)
        {
            return (null, null);
        }
    }, stop);

    public IReadOnlyList<string> Gateways() => [.. NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().GatewayAddresses).Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork).Select(g => g.Address.ToString()).Distinct()];

    /// <summary>The IPv4 networks this server is on, as ranges: a network bigger than /22 becomes the /24 around this server.</summary>
    public IReadOnlyList<(string Cidr, string Name)> LocalNetworks()
    {
        var list = new List<(string, string)>();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)))
        {
            foreach (var u in n.GetIPProperties().UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork && !u.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)))
            {
                var bits = Math.Max(u.PrefixLength, 22) is var b && u.PrefixLength < 22 ? 24 : b;
                if (NetworkScan.Normal($"{u.Address}/{bits}") is { } cidr && !list.Any(x => x.Item1 == cidr))
                {
                    list.Add((cidr, list.Count == 0 ? "This network" : $"Network {list.Count + 1}"));
                }
            }
        }
        return list;
    }
}

/// <summary>Scans on the schedule set in Settings › Network.</summary>
public sealed class NetworkWorker(NetworkScan scan, ILogger<NetworkWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stop); // after startup settles
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (scan.Due())
                {
                    scan.Start("Scheduled scan");
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Could not start the scheduled network scan");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stop);
        }
    }
}

public static class NetworkApi
{
    public static void AddNetworkScan(this IServiceCollection services)
    {
        services.AddSingleton<INetProbe, WindowsNetProbe>();
        services.AddSingleton<NetworkScan>();
        services.AddHostedService<NetworkWorker>();
    }

    public static void MapNetworkApi(this WebApplication app)
    {
        // Everything found on the network ranges, the ranges, and how a scan is going.
        app.MapGet("/api/network", (HttpContext context, NetworkScan scan) => Access.Can(context, Permissions.NetworkView) ? Results.Ok(scan.View(Access.Can(context, Permissions.NetworkSettings))) : Access.NotAllowed());

        app.MapPost("/api/network/scan", (NetScanForm? form, HttpContext context, NetworkScan scan, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.NetworkScan) is { } refused)
            {
                return refused;
            }
            var who = Who.Of(context.User).Username;
            if (!scan.Start(who, string.IsNullOrWhiteSpace(form?.Range) ? null : form.Range))
            {
                return Results.Conflict(new { error = scan.Busy ? "A scan is already running." : "No range is on. Add or turn on a range in Settings, Network." });
            }
            log.Record(context, "network", "network.scan", "Started a network scan");
            return Results.Ok(scan.View(Access.Can(context, Permissions.NetworkSettings)));
        });

        app.MapPost("/api/network/rescan", async (NetIdsForm form, HttpContext context, NetworkScan scan) =>
        {
            if (Access.RefuseUnless(context, Permissions.NetworkScan) is { } refused)
            {
                return refused;
            }
            await scan.Rescan(form.Ids ?? [], context.RequestAborted);
            return Results.Ok(scan.View(Access.Can(context, Permissions.NetworkSettings)));
        });

        app.MapPost("/api/network/known", (NetIdsForm form, HttpContext context, NetworkScan scan) =>
        {
            if (Access.RefuseUnless(context, Permissions.NetworkScan) is { } refused)
            {
                return refused;
            }
            scan.Known(form.Ids ?? []);
            return Results.Ok(scan.View(Access.Can(context, Permissions.NetworkSettings)));
        });

        app.MapPost("/api/network/forget", (NetIdsForm form, HttpContext context, NetworkScan scan, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.NetworkScan) is { } refused)
            {
                return refused;
            }
            scan.Forget(form.Ids ?? []);
            log.Record(context, "network", "network.forget", $"Removed {form.Ids?.Count ?? 0} devices from Network");
            return Results.Ok(scan.View(Access.Can(context, Permissions.NetworkSettings)));
        });

        app.MapPost("/api/network/devices/{id}", (string id, NetEditForm form, HttpContext context, NetworkScan scan, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.NetworkScan) is { } refused)
            {
                return refused;
            }
            try
            {
                var d = scan.Edit(id, form);
                if (d is null)
                {
                    return Results.NotFound(new { error = "That device is no longer in the list." });
                }
                log.Record(context, "network", "network.edit", $"Edited {d.Custom ?? d.Name ?? d.Ip} ({d.Ip}) in Network");
                return Results.Ok(scan.View(Access.Can(context, Permissions.NetworkSettings)));
            }
            catch (ToolProblem e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
        });

        app.MapPost("/api/network/settings", (NetSettingsForm form, HttpContext context, NetworkScan scan, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.NetworkSettings) is { } refused)
            {
                return refused;
            }
            try
            {
                scan.Set(form);
                log.Record(context, "network", "network.settings", "Changed the network scan ranges and schedule");
                return Results.Ok(scan.View(Access.Can(context, Permissions.NetworkSettings)));
            }
            catch (ToolProblem e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
        });
    }
}
