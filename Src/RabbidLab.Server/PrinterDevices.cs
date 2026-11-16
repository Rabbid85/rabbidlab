using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace RabbidLab.Server;

/// <summary>
/// One supply of a printer (a toner, a drum, a waste box) and how much is left. Percent is null when the printer gives no
/// gauge; Some says it only reports that there's some left (Brother's mono toners do).
/// </summary>
public sealed record PrinterSupply(string Name, int? Percent, bool Some);

/// <summary>What a printer said about itself over SNMP, or that it didn't answer: its model, serial number, pages printed and supplies.</summary>
public sealed record PrinterDevice(string Ip, bool Answered, string? Model, string? Serial, long? Pages, List<PrinterSupply> Supplies, DateTime At);

/// <summary>Asks one printer about itself. It only reads, and never throws.</summary>
public interface IPrinterSnmp
{
    PrinterDevice Ask(string ip, DateTime now);
}

/// <summary>
/// What the printers say about themselves, kept for six hours: every address a server's queue or a computer's printer
/// points at is asked once over SNMP (PrinterSnmp), a few at a time, and one printer only ever by one conversation.
/// A read gives what's known and starts asking the ones that aren't, waiting a moment for quick answers.
/// </summary>
public sealed partial class PrinterDevices(IPrinterSnmp snmp, TimeProvider? clock = null)
{
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(6);
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(1); // asked again no sooner than this, whoever presses Refresh
    private const int AtOnce = 4; // few on purpose: a burst of SNMP across a site link reads as a scan to its firewall
    private const int Most = 500;

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<string, PrinterDevice> known = [];
    private readonly HashSet<string> asking = [];
    private readonly SemaphoreSlim slots = new(AtOnce);
    private DateTime? forced;

    /// <summary>How long a read waits for the printers it just started asking.</summary>
    public TimeSpan Wait { get; init; } = TimeSpan.FromSeconds(3);

    public async Task<(List<PrinterDevice> Devices, bool Asking)> Read(IEnumerable<string> addresses, bool again, CancellationToken stop)
    {
        var ips = addresses.Where(a => !string.IsNullOrEmpty(a)).Distinct().Take(Most).ToList();
        var started = new List<Task>();
        lock (gate)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var force = again && (forced is null || now - forced > Gap);
            if (force)
            {
                forced = now;
            }
            foreach (var ip in ips.Where(ip => !asking.Contains(ip) && (force || !known.TryGetValue(ip, out var d) || now - d.At > Fresh)))
            {
                asking.Add(ip);
                started.Add(Task.Run(() => Ask(ip), CancellationToken.None));
            }
        }
        if (started.Count > 0)
        {
            await Task.WhenAny(Task.WhenAll(started), Task.Delay(Wait, stop));
        }
        lock (gate)
        {
            return ([.. ips.Where(known.ContainsKey).Select(ip => known[ip])], ips.Any(asking.Contains));
        }
    }

    private async Task Ask(string ip)
    {
        await slots.WaitAsync();
        try
        {
            var now = clock.GetUtcNow().UtcDateTime;
            PrinterDevice said;
            try
            {
                said = snmp.Ask(ip, now);
            }
            catch (Exception)
            {
                said = new PrinterDevice(ip, false, null, null, null, [], now);
            }
            lock (gate)
            {
                known[ip] = said;
            }
        }
        finally
        {
            slots.Release();
            lock (gate)
            {
                asking.Remove(ip);
            }
        }
    }

    /// <summary>
    /// The printer's own address, from what a computer says about a printer it has: a TCP/IP or LPR port's address, the
    /// address in a WSD printer's location (http://192.168.3.20:3911/), or one in its port's name. Only private addresses:
    /// nothing out on the internet is asked.
    /// </summary>
    public static string? IpOf(DevicePrinter p)
    {
        var from = p.Kind is "tcpip" or "lpr" && IPAddress.TryParse(p.Address, out var given) && given.AddressFamily == AddressFamily.InterNetwork ? given.ToString()
            : WebAddress().Match(p.Location ?? "") is { Success: true } m ? PrintServers.IpIn(m.Groups[1].Value)
            : PrintServers.IpIn(p.Port);
        return Private(from) ? from : null;
    }

    /// <summary>Whether an address is on a private network (10, 172.16 to 31, 192.168) or link-local.</summary>
    public static bool Private(string? ip) => IPAddress.TryParse(ip, out var a) && a.AddressFamily == AddressFamily.InterNetwork
        && a.GetAddressBytes() is [10, ..] or [172, >= 16 and <= 31, ..] or [192, 168, ..] or [169, 254, ..];

    [GeneratedRegex(@"^https?://(\d{1,3}(?:\.\d{1,3}){3})(?:[:/]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex WebAddress();
}

/// <summary>
/// Asks a printer about itself over SNMP v2c with the usual read community, public: the standard Printer MIB (RFC 3805)
/// that network printers answer. Few and small packets, paced, because site firewalls drop bursts on UDP 161 and some
/// printers drop big requests: the first values in one request, then one request for each supply. A printer that won't
/// answer several values at once is asked for one at a time. It stops after three requests in a row go unanswered, or
/// two supply slots in a row are empty.
/// </summary>
public sealed class PrinterSnmp : IPrinterSnmp
{
    private const string Community = "public";
    private const int WaitMs = 1200, PaceMs = 75, Slots = 10;
    private const string SysDescr = "1.3.6.1.2.1.1.1.0", DeviceName = "1.3.6.1.2.1.25.3.2.1.3.1", Serial = "1.3.6.1.2.1.43.5.1.1.17.1", LifeCount = "1.3.6.1.2.1.43.10.2.1.4.1.1";
    private const string SupplyName = "1.3.6.1.2.1.43.11.1.1.6.1.", SupplyMax = "1.3.6.1.2.1.43.11.1.1.8.1.", SupplyLevel = "1.3.6.1.2.1.43.11.1.1.9.1.";

    public PrinterDevice Ask(string ip, DateTime now)
    {
        var none = new PrinterDevice(ip, false, null, null, null, [], now);
        if (!IPAddress.TryParse(ip, out var address))
        {
            return none;
        }
        var end = new IPEndPoint(address, 161);
        var batched = true;
        var head = Get(end, SysDescr, DeviceName, Serial, LifeCount);
        if (head is null)
        {
            // It may only answer one value at a time: asked that way from here on.
            batched = false;
            head = Get(end, SysDescr);
            if (head is null)
            {
                return none; // no SNMP here, or UDP 161 is blocked on the way
            }
            head = [head[0], One(end, DeviceName), One(end, Serial), One(end, LifeCount)];
        }
        var supplies = new List<PrinterSupply>();
        var (misses, empty) = (0, 0);
        for (var i = 1; i <= Slots && misses < 3 && empty < 2; i++)
        {
            ISnmpData?[]? slot = batched ? Get(end, SupplyName + i, SupplyLevel + i, SupplyMax + i) : [One(end, SupplyName + i), One(end, SupplyLevel + i), One(end, SupplyMax + i)];
            if (slot is null || slot.All(v => v is null))
            {
                misses++;
                continue;
            }
            misses = 0;
            if (Supply(Text(slot[0]), Number(slot[1]), Number(slot[2])) is { } supply)
            {
                empty = 0;
                supplies.Add(supply);
            }
            else
            {
                empty++;
            }
        }
        return new PrinterDevice(ip, true, Model(Text(head[1]), Text(head[0])), Text(head[2]), Number(head[3]), supplies, now);
    }

    /// <summary>One supply from what its slot says: null for an empty slot. A level of -3 means some left, -2 unknown.</summary>
    public static PrinterSupply? Supply(string? name, long? level, long? max) => string.IsNullOrWhiteSpace(name) ? null
        : level == -3 ? new PrinterSupply(name, null, true)
        : level >= 0 && max > 0 ? new PrinterSupply(name, (int) Math.Min(100, level.Value * 100 / max.Value), false)
        : new PrinterSupply(name, null, false);

    /// <summary>
    /// The model as the printer names itself: its device name where it has one, else its description. HP's description is a
    /// list of settings with the model at the end, after PID.
    /// </summary>
    public static string? Model(string? device, string? description)
    {
        if (!string.IsNullOrWhiteSpace(device))
        {
            return device;
        }
        var at = description?.IndexOf("PID:", StringComparison.OrdinalIgnoreCase) ?? -1;
        var text = at >= 0 ? description![(at + 4)..].Split(',')[0].Trim() : description;
        return string.IsNullOrWhiteSpace(text) ? null : text.Length > 120 ? text[..120] : text;
    }

    // One request for these values, tried twice. Null when nothing came back; a value the printer doesn't have is null in its place.
    private static ISnmpData?[]? Get(IPEndPoint end, params string[] oids)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Thread.Sleep(PaceMs);
            try
            {
                var reply = Messenger.Get(VersionCode.V2, end, new OctetString(Community), [.. oids.Select(o => new Variable(new ObjectIdentifier(o)))], WaitMs);
                return [.. oids.Select((_, i) => i < reply.Count && reply[i].Data.TypeCode is not (SnmpType.NoSuchObject or SnmpType.NoSuchInstance or SnmpType.EndOfMibView or SnmpType.Null)
                    ? reply[i].Data : null)];
            }
            catch (Exception e) when (e is Lextm.SharpSnmpLib.Messaging.TimeoutException or SocketException or SnmpException or ErrorException)
            {
                // Asked again, once.
            }
        }
        return null;
    }

    private static ISnmpData? One(IPEndPoint end, string oid) => Get(end, oid)?[0];

    // Printable characters only: serial numbers and names sometimes come padded with zeros.
    private static string? Text(ISnmpData? data) => data is OctetString text && new string([.. text.ToString().Where(c => !char.IsControl(c))]).Trim() is { Length: > 0 } clean ? clean : null;

    private static long? Number(ISnmpData? data) => data switch
    {
        Integer32 i => i.ToInt32(),
        Gauge32 g => g.ToUInt32(),
        Counter32 c => c.ToUInt32(),
        Counter64 big => (long) big.ToUInt64(),
        _ => null,
    };
}
