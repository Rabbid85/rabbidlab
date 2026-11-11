using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace RabbidLab.Server;

/// <summary>
/// SNMP v2c (a community) or v3 (a user with SHA authentication and AES privacy): one value watched, such as a UPS's battery
/// or whether a switch port is up.
/// </summary>
public static class Snmp
{
    public static readonly string[] Values = ["uptime", "ups", "iface", "oid"];
    public static readonly string[] Parts = ["uptime", "battery", "power", "iface", "oid"];
    private const int Wait = 3000;
    private const string SysDescr = "1.3.6.1.2.1.1.1.0", SysUpTime = "1.3.6.1.2.1.1.3.0", SysName = "1.3.6.1.2.1.1.5.0";

    public static Task<Probe> Check(Sensor s, Login login, CancellationToken stop) => Task.Run(() => CheckNow(s, login), stop);

    private static Probe CheckNow(Sensor s, Login login)
    {
        var watch = Stopwatch.StartNew();
        IPAddress ip;
        try
        {
            ip = IPAddress.TryParse(s.Target, out var given) ? given
                : Dns.GetHostAddresses(s.Target).OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).First();
        }
        catch (Exception e) when (e is SocketException or InvalidOperationException)
        {
            return new Probe(false, Note: "No such name");
        }
        var device = new Device(new IPEndPoint(ip, s.Port ?? 161), login);
        try
        {
            var sys = device.Get([SysName, SysDescr, SysUpTime]);
            var ms = (int) watch.ElapsedMilliseconds;
            var found = new List<ProbePart>();
            switch (s.Value)
            {
                case "ups":
                    Ups(device, found);
                    break;
                case "iface":
                    if (Interface(device, s.Interface ?? "") is { } problem)
                    {
                        return new Probe(false, Note: problem);
                    }
                    Port(device, s.Interface ?? "", found);
                    break;
                case "oid":
                    var value = device.Get([s.Oid ?? SysUpTime])[0];
                    if (value.Data.TypeCode is SnmpType.NoSuchObject or SnmpType.NoSuchInstance or SnmpType.EndOfMibView)
                    {
                        return new Probe(false, Note: $"Nothing at {s.Oid}");
                    }
                    found.Add(new ProbePart("oid", s.Oid ?? "Value", Said(value.Data), "ok"));
                    break;
                default:
                    found.Add(new ProbePart("uptime", "Uptime", Span(sys[2].Data), "ok"));
                    break;
            }
            var describe = sys[1].Data.ToString() ?? "";
            return JsonBits.Result(s, found, ms, $"SNMP {(login.Kind == "snmpv3" ? "v3" : "v2c")} answered in {ms} ms",
                $"sysName {sys[0].Data} · {(describe.Length > 70 ? describe[..70] + "…" : describe)}", null);
        }
        catch (Lextm.SharpSnmpLib.Messaging.TimeoutException)
        {
            return new Probe(false, Note: login.Kind == "snmpv3" ? "No answer: check the address and the SNMPv3 user" : "No answer: check the address and the community");
        }
        catch (Refused r)
        {
            return new Probe(false, Note: r.Message);
        }
        catch (Exception e) when (e is SnmpException or SocketException or ErrorException)
        {
            return new Probe(false, Note: $"SNMP said: {e.Message}");
        }
    }

    // A UPS by the standard UPS-MIB, or APC's own when it doesn't have that.
    private static void Ups(Device device, List<ProbePart> found)
    {
        var std = device.Get(["1.3.6.1.2.1.33.1.2.4.0", "1.3.6.1.2.1.33.1.4.1.0"]);
        int? charge, source;
        if (Number(std[0].Data) is { } c)
        {
            charge = c;
            source = Number(std[1].Data) switch { 5 => 2, 4 => 3, 2 => 4, _ => 1 }; // on battery, bypass, no output, else line
        }
        else
        {
            var apc = device.Get(["1.3.6.1.4.1.318.1.1.1.2.2.1.0", "1.3.6.1.4.1.318.1.1.1.4.1.1.0"]);
            charge = Number(apc[0].Data);
            source = Number(apc[1].Data) switch { 3 => 2, 4 or 6 => 3, null => null, _ => 1 }; // onBattery, onSmartBoost/bypass
        }
        if (charge is { } n)
        {
            found.Add(new ProbePart("battery", "Battery charge", $"{n} %", n < 20 ? "bad" : n < 50 ? "warn" : "ok"));
        }
        if (source is { } from)
        {
            found.Add(new ProbePart("power", "Power", from switch { 2 => "On battery", 3 => "On bypass", 4 => "No output", _ => "On line power" }, from switch { 1 => "ok", 4 => "bad", _ => "warn" }));
        }
        if (found.Count == 0)
        {
            throw new Refused("It answers, but not as a UPS (no UPS-MIB or APC values)");
        }
    }

    private static string? Interface(Device device, string name) => device.Index(name) is null
        ? $"No interface named {name}. It has: {string.Join(", ", device.Names().Take(10))}" : null;

    private static void Port(Device device, string name, List<ProbePart> found)
    {
        var index = device.Index(name)!;
        var status = device.Get([$"1.3.6.1.2.1.2.2.1.8.{index}", $"1.3.6.1.2.1.2.2.1.7.{index}"]);
        var (now, state) = (Number(status[1].Data), Number(status[0].Data)) switch
        {
            (2, _) => ("Turned off", "warn"),
            (_, 1) => ("Up", "ok"),
            (_, 5) => ("Dormant", "ok"),
            (_, 7) => ("Down: nothing connected", "bad"),
            _ => ("Down", "bad"),
        };
        found.Add(new ProbePart("iface", $"Interface {name}", now, state));
    }

    private static int? Number(ISnmpData data) => data switch
    {
        Integer32 i => i.ToInt32(),
        Gauge32 g => (int) g.ToUInt32(),
        Counter32 c => (int) c.ToUInt32(),
        _ => null,
    };

    private static string Said(ISnmpData data) => data is TimeTicks ? Span(data) : data.ToString() ?? "";

    private static string Span(ISnmpData data)
    {
        if (data is not TimeTicks ticks)
        {
            return data.ToString() ?? "";
        }
        var t = ticks.ToTimeSpan();
        return t.TotalDays >= 1 ? $"Up {(int) t.TotalDays} day{((int) t.TotalDays == 1 ? "" : "s")}" : t.TotalHours >= 1 ? $"Up {(int) t.TotalHours} hours" : $"Up {(int) t.TotalMinutes} minutes";
    }

    private sealed class Refused(string message) : Exception(message);

    // One device, asked with the credentials' version: v2c with the community, v3 after discovering its engine.
    private sealed class Device(IPEndPoint end, Login login)
    {
        private ISnmpMessage? report;
        private IPrivacyProvider? privacy;
        private List<Variable>? names;

        private bool V3 => login.Kind == "snmpv3";

        public IList<Variable> Get(string[] oids)
        {
            var asked = oids.Select(o => new Variable(new ObjectIdentifier(o))).ToList();
            if (!V3)
            {
                return Messenger.Get(VersionCode.V2, end, new OctetString(login.Password), asked, Wait);
            }
            Ready();
            var reply = new GetRequestMessage(VersionCode.V3, Messenger.NextMessageId, Messenger.NextRequestId, new OctetString(login.Username ?? ""), OctetString.Empty, asked, privacy!, Messenger.MaxMessageSize, report!)
                .GetResponse(Wait, end);
            if (reply.Pdu().TypeCode == SnmpType.ReportPdu)
            {
                var said = reply.Pdu().Variables.FirstOrDefault()?.Id.ToString() ?? "";
                throw new Refused(said.StartsWith("1.3.6.1.6.3.15.1.1.3", StringComparison.Ordinal) ? "Sign-in failed: no such SNMPv3 user"
                    : said.StartsWith("1.3.6.1.6.3.15.1.1.5", StringComparison.Ordinal) ? "Sign-in failed: check the auth password and the authentication type"
                    : said.StartsWith("1.3.6.1.6.3.15.1.1.6", StringComparison.Ordinal) ? "Sign-in failed: check the privacy password and type"
                    : "Sign-in failed: the device turned the SNMPv3 user away");
            }
            if (reply.Pdu().ErrorStatus.ToInt32() != 0)
            {
                throw new Refused($"SNMP said: error {reply.Pdu().ErrorStatus.ToInt32()}");
            }
            return reply.Pdu().Variables;
        }

        // The interface names, by ifName where the device has it and ifDescr where it doesn't.
        public IEnumerable<string> Names() => Table().Select(v => v.Data.ToString() ?? "");

        public string? Index(string name) => Table().FirstOrDefault(v => string.Equals(v.Data.ToString(), name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } hit
            ? hit.Id.ToNumerical()[^1].ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

        private List<Variable> Table()
        {
            if (names is not null)
            {
                return names;
            }
            names = Walk("1.3.6.1.2.1.31.1.1.1.1");
            if (names.Count == 0)
            {
                names = Walk("1.3.6.1.2.1.2.2.1.2");
            }
            return names;
        }

        private List<Variable> Walk(string table)
        {
            var found = new List<Variable>();
            if (!V3)
            {
                Messenger.Walk(VersionCode.V2, end, new OctetString(login.Password), new ObjectIdentifier(table), found, Wait, WalkMode.WithinSubtree);
            }
            else
            {
                Ready();
                Messenger.BulkWalk(VersionCode.V3, end, new OctetString(login.Username ?? ""), OctetString.Empty, new ObjectIdentifier(table), found, Wait, 20, WalkMode.WithinSubtree, privacy!, report!);
            }
            return found;
        }

        private void Ready()
        {
            if (report is not null)
            {
                return;
            }
            report = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu).GetResponse(Wait, end);
            // SHA-1 stays for devices that only have it, such as older APC network cards.
#pragma warning disable CS0618
            IAuthenticationProvider auth = login.Auth == "SHA-1" ? new SHA1AuthenticationProvider(new OctetString(login.Password)) : new SHA256AuthenticationProvider(new OctetString(login.Password));
#pragma warning restore CS0618
            privacy = login.Privacy == "None" ? new DefaultPrivacyProvider(auth) : new AESPrivacyProvider(new OctetString(login.PrivacyPassword ?? ""), auth);
        }
    }
}
