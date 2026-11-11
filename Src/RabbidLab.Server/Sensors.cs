using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using DnsClient;
using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>Something Rabbid Lab checks on a timer: a machine that answers ping, an open port, a web page, a DNS name or a certificate.</summary>
public sealed class Sensor
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>ping, port, web, dns or cert.</summary>
    public string Kind { get; set; } = "ping";

    /// <summary>The host to reach, the page's address for web, or the name to look up for dns.</summary>
    public string Target { get; set; } = "";
    public int? Port { get; set; }

    /// <summary>For dns: the DNS server to ask; null for the one this computer uses.</summary>
    public string? Server { get; set; }

    /// <summary>For web: null when any answer counts, ok for a status from 200 to 399, text for that status with Text on the page.</summary>
    public string? Expect { get; set; }
    public string? Text { get; set; }

    /// <summary>For dns: the IP address the name should resolve to; null for any.</summary>
    public string? Address { get; set; }

    /// <summary>For cert: days left below which it warns; null for the usual 21.</summary>
    public int? WarnDays { get; set; }

    /// <summary>For kinds that sign in: the saved credentials (SensorLogins) by id.</summary>
    public string? Login { get; set; }

    /// <summary>For hw and nas: the parts it watches (Redfish.Parts, Storage.Parts); null for all of them.</summary>
    public List<string>? Watch { get; set; }

    /// <summary>For nas: truenas (an API key) or synology (a DSM account).</summary>
    public string? Product { get; set; }

    /// <summary>For nas and vmware: the share of free space below which it warns, in percent; null for 10.</summary>
    public int? WarnFree { get; set; }

    /// <summary>For snmp: uptime, ups, iface or oid; the OID for oid, and the interface's name for iface.</summary>
    public string? Value { get; set; }
    public string? Oid { get; set; }
    public string? Interface { get; set; }

    /// <summary>For svc and disk: the computer in Seats, by id, whose client makes the check; the service, or the drive such as C:.</summary>
    public string? Seat { get; set; }
    public string? Service { get; set; }
    public string? Drive { get; set; }

    /// <summary>The fingerprint of the device's certificate when Windows doesn't trust it: credentials only go to that one.</summary>
    public string? Pin { get; set; }

    /// <summary>Credentials typed for Check now, never saved with the sensor.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Login? Typed { get; set; }

    /// <summary>Check now: any certificate is read, to offer it to pin.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Trying { get; set; }
    public string? Group { get; set; }

    /// <summary>Seconds between checks.</summary>
    public int Every { get; set; } = 60;

    /// <summary>The saved alert settings it uses, by id, or custom for its own (Custom).</summary>
    public string Rule { get; set; } = "normal";

    /// <summary>With Rule custom: when it emails, again, and on coming back.</summary>
    public AlertRule? Custom { get; set; }
    public bool Paused { get; set; }
    public DateTime Added { get; set; }
    public string? AddedBy { get; set; }
}

/// <summary>How long a sensor has to be down before anyone is emailed, and what happens after.</summary>
public sealed class AlertRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";

    /// <summary>Minutes down before the email; 0 for right away, -1 for never.</summary>
    public int After { get; set; }

    /// <summary>Minutes between more emails while it stays down; 0 for only the one.</summary>
    public int Again { get; set; }

    /// <summary>Whether coming back sends an email too.</summary>
    public bool Back { get; set; } = true;
}

public sealed class SensorsFile
{
    public List<Sensor> Sensors { get; set; } = [];
    public List<string> Groups { get; set; } = [];
    public List<AlertRule> Rules { get; set; } = [];

    /// <summary>The saved alert settings new sensors start with; null for normal.</summary>
    public string? Default { get; set; }

    /// <summary>Whether Rules is the whole list as kept (named, added, deleted), not changes to the four it started with.</summary>
    public bool RulesOwned { get; set; }
}

/// <summary>One check: when, and the reply time in milliseconds, or -1 for none.</summary>
public sealed record Sample(DateTime At, int Ms);

/// <summary>An hour's checks counted, for the uptime figures.</summary>
public sealed class HourCount
{
    public DateTime At { get; set; }
    public int Ok { get; set; }
    public int Failed { get; set; }
}

/// <summary>Something that happened to a sensor: down, up, warn, email or problem (an email that didn't go).</summary>
public sealed record SensorEvent(DateTime At, string What, string Text);

/// <summary>How a sensor is doing, kept in sensor-state.json so a restart doesn't lose its history or email twice.</summary>
public sealed class SensorState
{
    /// <summary>new (not checked yet), up, down or warn.</summary>
    public string Status { get; set; } = "new";
    public DateTime? Since { get; set; }
    public int Fails { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? Checked { get; set; }
    public int? Ms { get; set; }
    public string? Note { get; set; }

    /// <summary>When the last email about it being down went out.</summary>
    public DateTime? Alerted { get; set; }

    /// <summary>Someone was emailed that it's down, so they're told when it's back.</summary>
    public bool Told { get; set; }
    public List<Sample> Recent { get; set; } = [];
    public List<HourCount> Hours { get; set; } = [];
    public List<SensorEvent> Events { get; set; } = [];
}

public sealed class SensorStatesFile
{
    public Dictionary<string, SensorState> States { get; set; } = [];
}

/// <summary>What a check found. Warn is an answer that needs attention soon, such as a certificate about to expire. Detail is for Check now only.</summary>
public sealed record Probe(bool Ok, int? Ms = null, string? Note = null, bool Warn = false, string? Detail = null, string? Title = null, List<ProbePart>? Parts = null, string? Pin = null);

/// <summary>One thing a sensor that signs in can watch, such as the power supplies: what it is now, and ok, warn or bad.</summary>
public sealed record ProbePart(string Key, string Name, string Now, string State);

public interface ISensorProbe
{
    Task<Probe> Check(Sensor sensor, CancellationToken stop);
}

/// <summary>The checks themselves, made from this server. Each one answers within a few seconds or counts as no reply.</summary>
public sealed class NetworkProbe(TimeProvider clock, SensorLogins? logins = null, ClientCommands? commands = null, Clients? clients = null) : ISensorProbe
{
    private static readonly HttpClient Http = MakeHttp();

    public async Task<Probe> Check(Sensor s, CancellationToken stop)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(TimeSpan.FromSeconds(s.Kind switch { "web" => 10, "hw" or "nas" or "vmware" => 45, "snmp" => 15, "svc" or "disk" => 45, _ => 5 }));
        try
        {
            return s.Kind switch
            {
                "port" => await Port(s.Target, s.Port ?? 0, limit.Token),
                "web" => await Web(s, limit.Token),
                "dns" => await Name(s.Target, s.Server, s.Address, limit.Token),
                "cert" => await Certificate(s.Target, s.Port ?? 443, s.WarnDays ?? Sensors.CertificateWarnDays, limit.Token),
                "hw" => await Signed(s, (login) => Redfish.Check(s, login, s.Trying, limit.Token)),
                "nas" => await Signed(s, (login) => s.Product == "synology" ? Synology.Check(s, login, s.Trying, limit.Token) : TrueNas.Check(s, login, s.Trying, limit.Token)),
                "vmware" => await Signed(s, (login) => VMware.Check(s, login, s.Trying, limit.Token)),
                "snmp" => await Signed(s, (login) => Snmp.Check(s, login, limit.Token)),
                "svc" or "disk" => await Seat(s, limit.Token),
                _ => await Ping(s.Target, limit.Token),
            };
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            return new Probe(false, Note: "No reply");
        }
        catch (SocketException e)
        {
            return new Probe(false, Note: e.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "No such name",
                SocketError.ConnectionRefused => s.Kind == "ping" ? "Refused" : $"Port {s.Port ?? 443} is closed",
                SocketError.NetworkUnreachable or SocketError.HostUnreachable => "No route to it",
                _ => "No reply",
            });
        }
        catch (Exception e) when (e is IOException or HttpRequestException or PingException or System.Security.Authentication.AuthenticationException or DnsResponseException or InvalidOperationException)
        {
            return new Probe(false, Note: e.InnerException is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData } ? "No such name" : "No reply");
        }
    }

    // A check the Rabbid Lab client on a seat makes, as SYSTEM: a service's state, or a drive's free space.
    private async Task<Probe> Seat(Sensor s, CancellationToken stop)
    {
        if (commands is null || clients?.Find(s.Seat ?? "") is not { } client)
        {
            return new Probe(false, Note: "That computer isn't in Seats any more");
        }
        if (!commands.Listening(client.Id))
        {
            return new Probe(false, Note: $"The client on {client.Computer} isn't connected");
        }
        var watch = Stopwatch.StartNew();
        var answer = await commands.Send(client.Id, new AccountOrder { Action = s.Kind == "svc" ? AccountOrder.Service : AccountOrder.Disk, Name = s.Kind == "svc" ? s.Service : s.Drive }, stop);
        var ms = (int) watch.ElapsedMilliseconds;
        if (answer is null)
        {
            return new Probe(false, Note: $"The client on {client.Computer} didn't answer");
        }
        if (!answer.Ok)
        {
            return new Probe(false, Note: answer.Error ?? "The client couldn't read it");
        }
        if (s.Kind == "svc")
        {
            var state = Spaced(answer.State);
            return new Probe(answer.State == "Running", ms, state, Title: $"{answer.Detail} is {state.ToLowerInvariant()} on {client.Computer}",
                Detail: $"Startup type {Spaced(answer.StartType)} · read by the client in {ms} ms");
        }
        var percent = 100.0 * (answer.Free ?? 0) / Math.Max(1, answer.Total ?? 1);
        var limit = s.WarnFree ?? 10;
        var said = $"{s.Drive} has {percent:0} % free";
        return new Probe(true, ms, said, Warn: percent < limit, Title: $"{said} on {client.Computer}",
            Detail: $"{(answer.Free ?? 0) / 1e9:0} GB free of {(answer.Total ?? 0) / 1e9:0} GB{(string.IsNullOrEmpty(answer.Detail) ? "" : $" · {answer.Detail}")} · warns below {limit} %");
    }

    // StopPending as Stop pending.
    private static string Spaced(string? word) => Regex.Replace(word ?? "Unknown", "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant() is { Length: > 0 } w ? char.ToUpperInvariant(w[0]) + w[1..] : "Unknown";

    // A check that signs in: with the credentials typed for Check now, or the saved ones it picked.
    private async Task<Probe> Signed(Sensor s, Func<Login, Task<Probe>> check) =>
        (s.Typed ?? logins?.Get(s.Login)) is { } login ? await check(login) : new Probe(false, Note: "No credentials: open the sensor and pick them again");

    private static async Task<Probe> Ping(string host, CancellationToken stop)
    {
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(3), cancellationToken: stop);
        return reply.Status == IPStatus.Success ? new Probe(true, (int) reply.RoundtripTime)
            : new Probe(false, Note: reply.Status is IPStatus.TimedOut ? "No reply" : reply.Status is IPStatus.DestinationHostUnreachable or IPStatus.DestinationNetworkUnreachable ? "No route to it" : "No reply");
    }

    private static async Task<Probe> Port(string host, int port, CancellationToken stop)
    {
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        var watch = Stopwatch.StartNew();
        await socket.ConnectAsync(host, port, stop);
        return new Probe(true, (int) watch.ElapsedMilliseconds);
    }

    private static async Task<Probe> Web(Sensor s, CancellationToken stop)
    {
        var watch = Stopwatch.StartNew();
        using var response = await Http.GetAsync(s.Target, HttpCompletionOption.ResponseHeadersRead, stop);
        var ms = (int) watch.ElapsedMilliseconds;
        var code = (int) response.StatusCode;
        var said = $"HTTP {code} {response.ReasonPhrase}".TrimEnd();
        // A sign-in page or a refusal is the site answering; a 5xx is the site saying it's broken.
        if (s.Expect is null)
        {
            return code < 500 ? new Probe(true, ms, said) : new Probe(false, Note: $"HTTP {code}");
        }
        if (code is < 200 or > 399)
        {
            return new Probe(false, Note: said);
        }
        if (s.Expect == "text" && !(await Body(response, stop)).Contains(s.Text ?? "", StringComparison.OrdinalIgnoreCase))
        {
            return new Probe(false, Note: "The text isn't on the page");
        }
        return new Probe(true, ms, said);
    }

    // The page, up to 2 MB: the text looked for is on the page a person sees, not past the end of a download.
    private static async Task<string> Body(HttpResponseMessage response, CancellationToken stop)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(stop);
        var buffer = new byte[2 * 1024 * 1024];
        var read = 0;
        int n;
        while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), stop)) > 0)
        {
            read += n;
        }
        return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static async Task<Probe> Name(string name, string? server, string? expected, CancellationToken stop)
    {
        var watch = Stopwatch.StartNew();
        IPAddress[] found;
        if (string.IsNullOrWhiteSpace(server))
        {
            found = await Dns.GetHostAddressesAsync(name, stop);
        }
        else
        {
            var at = IPAddress.TryParse(server, out var ip) ? ip : (await Dns.GetHostAddressesAsync(server, stop)).First();
            var client = new LookupClient(new LookupClientOptions(at) { Timeout = TimeSpan.FromSeconds(3), Retries = 0, UseCache = false });
            var answer = await client.QueryAsync(name, QueryType.A, cancellationToken: stop);
            if (!answer.HasError && answer.Answers.Count == 0)
            {
                answer = await client.QueryAsync(name, QueryType.AAAA, cancellationToken: stop);
            }
            found = answer.HasError ? [] : [.. answer.Answers.OfType<DnsClient.Protocol.ARecord>().Select(r => r.Address), .. answer.Answers.OfType<DnsClient.Protocol.AaaaRecord>().Select(r => r.Address)];
        }
        if (found.Length == 0)
        {
            return new Probe(false, Note: "No such name");
        }
        var ms = (int) watch.ElapsedMilliseconds;
        var list = string.Join(", ", found.Take(3).Select(a => a.ToString()));
        return expected is not null && IPAddress.TryParse(expected, out var want) && !found.Any(a => Addresses.Normal(a).Equals(Addresses.Normal(want)))
            ? new Probe(false, Note: $"Resolves to {list}, not {expected}")
            : new Probe(true, ms, $"Resolves to {list}");
    }

    private async Task<Probe> Certificate(string host, int port, int warnDays, CancellationToken stop)
    {
        using var tcp = new TcpClient();
        var watch = Stopwatch.StartNew();
        await tcp.ConnectAsync(host, port, stop);
        // Any certificate is read: the check is when it expires, not whether this server trusts it.
        using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, stop);
        if (ssl.RemoteCertificate is not { } raw)
        {
            return new Probe(false, Note: "No certificate");
        }
        using var certificate = new X509Certificate2(raw);
        var days = (int) Math.Floor((certificate.NotAfter.ToUniversalTime() - clock.GetUtcNow().UtcDateTime).TotalDays);
        var ms = (int) watch.ElapsedMilliseconds;
        var detail = $"{certificate.GetNameInfo(X509NameType.SimpleName, false)} · issued by {certificate.GetNameInfo(X509NameType.SimpleName, true)} · expires {certificate.NotAfter.ToUniversalTime():MMM d, yyyy}";
        return days < 0 ? new Probe(false, Note: $"Expired {-days} day{(days == -1 ? "" : "s")} ago", Detail: detail)
            : new Probe(true, ms, days == 0 ? "Expires today" : $"Expires in {days} day{(days == 1 ? "" : "s")}", Warn: days < warnDays, Detail: detail);
    }

    private static HttpClient MakeHttp()
    {
        // Homelab sites mostly have self-signed certificates: the check is whether the site answers.
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectTimeout = TimeSpan.FromSeconds(4),
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(9) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"RabbidLab/{Product.Version}");
        return http;
    }
}

/// <summary>A sensor as the page sends it.</summary>
public sealed record SensorForm(string? Name, string? Kind, string? Target, int? Port, string? Server, string? Group, int? Every, string? Rule,
    string? Expect = null, string? Text = null, string? Address = null, int? WarnDays = null, string? Login = null, LoginForm? NewLogin = null, List<string>? Watch = null, string? Pin = null,
    string? Product = null, int? WarnFree = null, string? Value = null, string? Oid = null, string? Interface = null, RuleForm? Custom = null,
    string? Seat = null, string? Service = null, string? Drive = null);

public sealed record RuleForm(int? After, int? Again, bool? Back, string? Name = null);

/// <summary>
/// The Sensors page's sensors, kept in sensors.json, and how each is doing, kept in sensor-state.json. A sensor is down
/// after two failed checks in a row (one, for a sensor checked hourly). Its alert rule says how long it stays down before
/// the email goes, so something that comes straight back emails nobody.
/// </summary>
public sealed partial class Sensors(DataFolder folder, TimeProvider clock, ISensorProbe probe, IMailSender mail, ILogger<Sensors> log, SensorLogins logins)
{
    public const int CertificateWarnDays = 21;
    private const int Most = 200;
    private static readonly int[] Intervals = [30, 60, 300, 3600];
    private static readonly string[] Kinds = ["ping", "port", "web", "dns", "cert", "hw", "nas", "vmware", "snmp", "svc", "disk"];

    /// <summary>The kinds that sign in, with saved or typed credentials.</summary>
    public static bool SignsIn(string kind) => kind is "hw" or "nas" or "vmware" or "snmp";

    // What a kind can watch, and whether its credentials have a username (a TrueNAS API key has none).
    private static string[] PartsOf(string kind) => kind switch { "hw" => Redfish.Parts, "nas" => Storage.Parts, "vmware" => VMware.Parts, "snmp" => Snmp.Parts, _ => [] };

    // The kinds of credentials a sensor takes: an API key for TrueNAS, a community or v3 user for SNMP, else a username and password.
    private static string[] LoginKinds(Sensor s) => s.Kind switch { "snmp" => ["community", "snmpv3"], "nas" when s.Product != "synology" => ["key"], _ => ["user"] };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions Compact = new(JsonSerializerDefaults.Web);
    private readonly JsonFile<SensorsFile> file = folder.Json<SensorsFile>("sensors.json", Json);
    private readonly JsonFile<SensorStatesFile> states = folder.Json<SensorStatesFile>("sensor-state.json", Compact);
    private readonly HashSet<string> busy = []; // being checked right now
    private bool dirty;
    private DateTime saved;

    private Lock gate => file.Gate;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>The saved alert settings: the four it starts with (their timings as changed), until the person names, adds or deletes one.</summary>
    private static List<AlertRule> Rules(SensorsFile f)
    {
        if (f.RulesOwned)
        {
            return f.Rules;
        }
        AlertRule[] start =
        [
            new() { Id = "critical", Name = "Critical", Color = "#FF6B7D", After = 0, Again = 60 },
            new() { Id = "normal", Name = "Normal", Color = "#FF8A3D", After = 5 },
            new() { Id = "relaxed", Name = "Relaxed", Color = "#6FA8FF", After = 30 },
            new() { Id = "quiet", Name = "Quiet", Color = "#8A8FA8", After = -1, Back = false },
        ];
        return [.. start.Select(r => f.Rules.FirstOrDefault(x => x.Id == r.Id) is { } kept
            ? new AlertRule { Id = r.Id, Name = r.Name, Color = r.Color, After = kept.After, Again = kept.Again, Back = kept.Back } : r)];
    }

    private static List<string> Known(SensorsFile f) => Grouping.Known(f.Groups, f.Sensors.Select(s => s.Group));

    private static readonly string[] RuleColours = ["#FF6B7D", "#FF8A3D", "#6FA8FF", "#8A8FA8", "#5FD4A0", "#B07CE8", "#F2BE6B", "#3CC7C7"];

    // The saved settings new sensors start with.
    private static string DefaultRule(SensorsFile f, List<AlertRule> rules) =>
        f.Default is { } d && rules.Any(r => r.Id == d) ? d : rules.Any(r => r.Id == "normal") ? "normal" : rules[0].Id;

    // Timings as offered on the page; null when they're not.
    private static string? Timings(RuleForm form) =>
        form.After is not (-1 or 0 or 1 or 5 or 10 or 30 or 60) || form.Again is not (0 or 60 or 360 or 1440) ? "Pick one of the times offered." : null;

    private static AlertRule Timed(AlertRule r, RuleForm form)
    {
        r.After = form.After!.Value;
        r.Again = form.After == -1 ? 0 : form.Again!.Value;
        r.Back = form.After != -1 && (form.Back ?? true);
        return r;
    }

    private SensorState State(string id)
    {
        var all = states.Read().States;
        if (!all.TryGetValue(id, out var state))
        {
            all[id] = state = new SensorState();
        }
        return state;
    }

    // --- What the page reads ------------------------------------------------------------------------------------------

    /// <summary>Everything the Sensors page shows: the sensors in their groups' order with how each is doing, the groups and the rules.</summary>
    public object View()
    {
        lock (gate)
        {
            var f = file.Read();
            var groups = Known(f);
            var place = groups.Select((g, i) => (g, i)).ToDictionary(p => p.g, p => p.i, Grouping.Same);
            var counts = f.Sensors.GroupBy(s => s.Rule).ToDictionary(g => g.Key, g => g.Count());
            return new
            {
                sensors = f.Sensors.OrderBy(s => s.Group is { } g && place.TryGetValue(g, out var at) ? at : int.MaxValue).ThenBy(s => s.Name, Grouping.Same).Select(s => Row(s, State(s.Id))).ToList(),
                groups,
                rules = Rules(f).Select(r => new { r.Id, r.Name, r.Color, r.After, r.Again, r.Back, used = counts.GetValueOrDefault(r.Id) }).ToList(),
                @default = DefaultRule(f, Rules(f)),
                mail = mail.Ready,
                last = f.Sensors.Select(s => State(s.Id).Checked).Max(),
            };
        }
    }

    private static object Row(Sensor s, SensorState st) => new
    {
        s.Id, s.Name, s.Kind, s.Target, s.Port, s.Server, s.Expect, s.Text, s.Address, s.WarnDays, s.Login, s.Watch, s.Pin, s.Product, s.WarnFree, s.Value, s.Oid, s.Interface, s.Seat, s.Service, s.Drive, s.Group, s.Every, s.Rule, s.Custom, s.Paused,
        status = s.Paused ? "paused" : st.Status,
        since = st.Since,
        ms = st.Ms,
        note = st.Note,
        @checked = st.Checked,
        recent = st.Recent.Select(x => x.Ms).ToList(),
        up7 = Uptime(st, 168),
    };

    /// <summary>The sensors as they are saved.</summary>
    public IReadOnlyList<Sensor> All()
    {
        lock (gate)
        {
            return [.. file.Read().Sensors];
        }
    }

    /// <summary>One sensor with its history, or null when it isn't there.</summary>
    public object? Detail(string id)
    {
        lock (gate)
        {
            if (file.Read().Sensors.FirstOrDefault(s => s.Id == id) is not { } s)
            {
                return null;
            }
            var st = State(id);
            return new { sensor = Row(s, st), up24 = Uptime(st, 24), up7 = Uptime(st, 168), samples = st.Recent.ToList(), events = st.Events.AsEnumerable().Reverse().ToList(), alerted = st.Alerted };
        }
    }

    // The share of checks answered over the last hours; null when there were none.
    private static double? Uptime(SensorState st, int hours)
    {
        var from = st.Hours.Count == 0 ? DateTime.MinValue : st.Hours[^1].At.AddHours(1 - hours);
        var counted = st.Hours.Where(h => h.At >= from).ToList();
        var total = counted.Sum(h => h.Ok + h.Failed);
        return total == 0 ? null : Math.Round(100.0 * counted.Sum(h => h.Ok) / total, 2);
    }

    // --- Changing sensors, groups and rules -----------------------------------------------------------------------------

    /// <summary>A form made into a sensor that can be checked, or why it can't be.</summary>
    private static (Sensor? Sensor, string? Problem) Read(SensorForm form)
    {
        var kind = Kinds.Contains(form.Kind) ? form.Kind! : "ping";
        if (kind is "svc" or "disk")
        {
            return ReadSeat(form, kind);
        }
        var target = form.Target?.Trim() ?? "";
        if (target.Length is 0 or > 300)
        {
            return (null, kind == "web" ? "Type the page's address, such as https://192.168.2.23." : "Type the address to check, such as 192.168.2.23 or nas.example.com.");
        }
        if (kind == "web")
        {
            if (!target.Contains("://", StringComparison.Ordinal))
            {
                target = "http://" + target;
            }
            if (!Uri.TryCreate(target, UriKind.Absolute, out var page) || page.Scheme is not ("http" or "https") || page.Host.Length == 0)
            {
                return (null, "A web page's address starts with http:// or https://.");
            }
            target = page.ToString();
        }
        else if (!HostName().IsMatch(target))
        {
            return (null, "That isn't a name or an IP address. Leave off http:// and anything after the name.");
        }
        int? port = kind == "port" ? form.Port : kind == "cert" ? form.Port ?? 443 : null;
        if (kind is "port" or "cert" && port is not (>= 1 and <= 65535))
        {
            return (null, "Type the port, from 1 to 65535. A file share is 445, LDAPS is 636, remote desktop is 3389.");
        }
        var server = kind == "dns" && !string.IsNullOrWhiteSpace(form.Server) ? form.Server.Trim() : null;
        if (server is not null && !HostName().IsMatch(server))
        {
            return (null, "The DNS server has to be a name or an IP address.");
        }
        var expect = kind == "web" && form.Expect is "ok" or "text" ? form.Expect : null;
        var text = expect == "text" ? form.Text?.Trim() : null;
        if (expect == "text" && text is not { Length: > 0 and <= 200 })
        {
            return (null, "Type the text the page shows when it works, 200 characters at most.");
        }
        var address = kind == "dns" && !string.IsNullOrWhiteSpace(form.Address) ? form.Address.Trim() : null;
        if (address is not null && !IPAddress.TryParse(address, out _))
        {
            return (null, "Should resolve to is an IP address, such as 192.168.2.23. Leave it empty for any address.");
        }
        int? warn = kind == "cert" && form.WarnDays is 14 or 30 or 60 ? form.WarnDays : null; // 21, the usual, is kept as null
        var name = string.IsNullOrWhiteSpace(form.Name) ? (kind == "web" ? new Uri(target).Host : target) : form.Name.Trim();
        if (name.Length > 60)
        {
            return (null, "Names are 60 characters at most.");
        }
        // A certificate doesn't change by the minute: hourly unless something else was asked for. Hardware: every 5 minutes.
        var every = Intervals.Contains(form.Every ?? 0) ? form.Every!.Value : kind switch { "cert" => 3600, "hw" or "nas" or "vmware" => 300, _ => 60 };
        var parts = PartsOf(kind);
        var watch = parts.Length > 0 && form.Watch is { } w && !parts.All(w.Contains) ? parts.Where(w.Contains).ToList() : null;
        var product = kind == "nas" ? (form.Product == "synology" ? "synology" : "truenas") : null;
        int? free = kind is "nas" or "vmware" && form.WarnFree is 5 or 20 ? form.WarnFree : null; // 10, the usual, is kept as null
        var value = kind == "snmp" ? (Snmp.Values.Contains(form.Value) ? form.Value : "uptime") : null;
        var oid = value == "oid" ? form.Oid?.Trim().TrimStart('.') : null;
        if (value == "oid" && !OidPattern().IsMatch(oid ?? ""))
        {
            return (null, "An OID is numbers with dots, such as 1.3.6.1.2.1.1.3.0.");
        }
        var face = value == "iface" ? form.Interface?.Trim() : null;
        if (value == "iface" && face is not { Length: > 0 and <= 100 })
        {
            return (null, "Type the interface's name as the device has it, such as Port 5 or eth0.");
        }
        return (new Sensor { Name = name, Kind = kind, Target = target, Port = port, Server = server, Expect = expect, Text = text, Address = address, WarnDays = warn, Watch = watch, Product = product, WarnFree = free, Value = value, Oid = oid, Interface = face,
            Pin = SignsIn(kind) && !string.IsNullOrWhiteSpace(form.Pin) ? form.Pin.Trim() : null, Every = every }, null);
    }

    // A check on a seat: the computer, its name shown, and the service or the drive.
    private static (Sensor? Sensor, string? Problem) ReadSeat(SensorForm form, string kind)
    {
        if (string.IsNullOrWhiteSpace(form.Seat))
        {
            return (null, "Pick the computer.");
        }
        var computer = form.Target?.Trim() is { Length: > 0 and <= 100 } shown ? shown : form.Seat.Trim();
        string? service = null, drive = null;
        if (kind == "svc")
        {
            service = form.Service?.Trim();
            if (service is not { Length: > 0 and <= 256 })
            {
                return (null, "Type the service's name, such as Spooler or Print Spooler.");
            }
        }
        else
        {
            var letter = (form.Drive ?? "").Trim().TrimEnd(':', '\\');
            if (letter.Length != 1 || !char.IsAsciiLetter(letter[0]))
            {
                return (null, "Type the drive letter, such as C.");
            }
            drive = letter.ToUpperInvariant() + ":";
        }
        var name = string.IsNullOrWhiteSpace(form.Name) ? $"{service ?? drive} on {computer}" : form.Name.Trim();
        if (name.Length > 60)
        {
            name = string.IsNullOrWhiteSpace(form.Name) ? name[..60] : "";
            if (name.Length == 0)
            {
                return (null, "Names are 60 characters at most.");
            }
        }
        var every = Intervals.Contains(form.Every ?? 0) ? form.Every!.Value : kind == "disk" ? 300 : 60;
        int? free = kind == "disk" && form.WarnFree is 5 or 20 ? form.WarnFree : null;
        return (new Sensor { Name = name, Kind = kind, Target = computer, Seat = form.Seat.Trim(), Service = service, Drive = drive, WarnFree = free, Every = every }, null);
    }

    /// <summary>The sensor saved (new when id is null), or why it can't be.</summary>
    public (Sensor? Sensor, string? Problem) Save(string? id, SensorForm form, string by)
    {
        var (next, problem) = Read(form);
        if (next is null)
        {
            return (null, problem);
        }
        // Credentials: saved ones picked, or typed ones saved now for this sensor and any other that picks them later.
        if (SignsIn(next.Kind))
        {
            var kinds = LoginKinds(next);
            if (form.NewLogin is not null)
            {
                var kind = kinds.Contains(form.NewLogin.Kind ?? "") ? form.NewLogin.Kind! : kinds[0];
                if (SensorLogins.Problem(form.NewLogin, kind) is { } bad)
                {
                    return (null, bad);
                }
                next.Login = logins.Add(form.NewLogin, kind, by);
            }
            else if (logins.KindOf(form.Login) is { } saved && kinds.Contains(saved))
            {
                next.Login = form.Login;
            }
            else
            {
                return (null, "Pick saved credentials, or type a username and password.");
            }
        }
        lock (gate)
        {
            var f = file.Read();
            var at = id is null ? -1 : f.Sensors.FindIndex(s => s.Id == id);
            if (id is not null && at < 0)
            {
                return (null, "That sensor isn't here any more.");
            }
            if (at < 0 && f.Sensors.Count >= Most)
            {
                return (null, $"That's {Most} sensors, which is as many as Rabbid Lab keeps.");
            }
            var known = Known(f);
            var (group, full) = Grouping.Join(known, form.Group);
            if (full is not null)
            {
                return (null, full);
            }
            var old = at < 0 ? null : f.Sensors[at];
            next.Id = old?.Id ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            next.Group = group;
            var rules = Rules(f);
            if (form.Rule == "custom")
            {
                if (form.Custom is null || Timings(form.Custom) is not null)
                {
                    return (null, "Pick the times for this sensor's own alerts.");
                }
                next.Rule = "custom";
                next.Custom = Timed(new AlertRule { Id = "custom", Name = "Custom", Color = "#8A8FA8" }, form.Custom);
            }
            else
            {
                next.Rule = rules.Any(r => r.Id == form.Rule) ? form.Rule! : old?.Rule is { } kept && rules.Any(r => r.Id == kept) ? kept : DefaultRule(f, rules);
            }
            next.Paused = old?.Paused ?? false;
            next.Added = old?.Added ?? Now;
            next.AddedBy = old is null ? by : old.AddedBy;
            f.Groups = known;
            if (at < 0)
            {
                f.Sensors.Add(next);
            }
            else
            {
                f.Sensors[at] = next;
                // Something else is checked now: what the old check found says nothing about it.
                if (old!.Kind != next.Kind || old.Target != next.Target || old.Port != next.Port || old.Server != next.Server || old.Expect != next.Expect || old.Text != next.Text
                    || old.Address != next.Address || old.WarnDays != next.WarnDays || old.Product != next.Product
                    || old.Value != next.Value || old.Oid != next.Oid || old.Interface != next.Interface
                    || old.Seat != next.Seat || old.Service != next.Service || old.Drive != next.Drive)
                {
                    states.Read().States.Remove(next.Id);
                    dirty = true;
                }
            }
            file.Save(f);
            logins.Prune(f.Sensors);
            return (next, null);
        }
    }

    public Sensor? Delete(string id)
    {
        lock (gate)
        {
            var f = file.Read();
            var gone = f.Sensors.FirstOrDefault(s => s.Id == id);
            if (gone is not null)
            {
                f.Sensors.Remove(gone);
                file.Save(f);
                logins.Prune(f.Sensors);
                states.Read().States.Remove(id);
                dirty = true;
            }
            return gone;
        }
    }

    /// <summary>Pauses a sensor or starts it again. A paused sensor isn't checked and emails nobody.</summary>
    public Sensor? Pause(string id, bool paused)
    {
        lock (gate)
        {
            var f = file.Read();
            var at = f.Sensors.FindIndex(s => s.Id == id);
            if (at < 0)
            {
                return null;
            }
            var s = f.Sensors[at];
            f.Sensors[at] = new Sensor { Id = s.Id, Name = s.Name, Kind = s.Kind, Target = s.Target, Port = s.Port, Server = s.Server, Group = s.Group, Every = s.Every, Rule = s.Rule, Paused = paused, Added = s.Added, AddedBy = s.AddedBy };
            file.Save(f);
            if (!paused)
            {
                // Starting again is a clean start: it's checked at once, and an old outage isn't carried on.
                var st = State(id);
                st.Checked = null;
                st.Fails = 0;
                st.FailedAt = null;
                st.Alerted = null;
                st.Told = false;
                if (st.Status == "down")
                {
                    st.Status = "new";
                    st.Since = null;
                }
                dirty = true;
            }
            return f.Sensors[at];
        }
    }

    public string? AddGroup(string? name) => Groups(known => Grouping.Add(known, name));

    public string? RenameGroup(string? name, string? to) => Groups(known =>
    {
        if (Grouping.Rename(known, name, to, out var old) is { } problem)
        {
            return problem;
        }
        Regroup(old, Grouping.Clean(to));
        return null;
    });

    /// <summary>Deletes a group and keeps its sensors, with no group. False when the group isn't there.</summary>
    public bool DeleteGroup(string? name) => Groups(known =>
    {
        if (!Grouping.Remove(known, name, out var old))
        {
            return "gone";
        }
        Regroup(old, null);
        return null;
    }) is null;

    public void OrderGroups(IReadOnlyList<string>? names)
    {
        lock (gate)
        {
            var f = file.Read();
            f.Groups = Grouping.Order(Known(f), names);
            file.Save(f);
        }
    }

    private string? Groups(Func<List<string>, string?> change)
    {
        lock (gate)
        {
            var f = file.Read();
            var known = Known(f);
            if (change(known) is { } problem)
            {
                return problem;
            }
            f.Groups = known;
            file.Save(f);
            return null;
        }
    }

    private void Regroup(string from, string? to)
    {
        foreach (var s in file.Read().Sensors.Where(s => Grouping.Same.Equals(s.Group, from)))
        {
            s.Group = to;
        }
    }

    /// <summary>Changes a rule's timings; the answer is why it can't be.</summary>
    public string? SaveRule(string id, RuleForm form)
    {
        if (Timings(form) is { } problem)
        {
            return problem;
        }
        lock (gate)
        {
            var f = file.Read();
            var rules = Rules(f);
            if (rules.FirstOrDefault(r => r.Id == id) is not { } rule)
            {
                return "Those alert settings aren't here any more.";
            }
            if (form.Name is not null)
            {
                if (RuleName(rules, form.Name, id) is { } bad)
                {
                    return bad;
                }
                rule.Name = form.Name.Trim();
            }
            Timed(rule, form);
            Own(f, rules);
            return null;
        }
    }

    /// <summary>New saved alert settings, by name; the id, or why not.</summary>
    public (string? Id, string? Problem) AddRule(RuleForm form)
    {
        if (Timings(form) is { } problem)
        {
            return (null, problem);
        }
        lock (gate)
        {
            var f = file.Read();
            var rules = Rules(f);
            if (RuleName(rules, form.Name, null) is { } bad)
            {
                return (null, bad);
            }
            if (rules.Count >= 20)
            {
                return (null, "That's 20 saved alert settings, which is as many as Rabbid Lab keeps.");
            }
            var rule = Timed(new AlertRule
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(),
                Name = form.Name!.Trim(),
                Color = RuleColours.FirstOrDefault(c => rules.All(r => r.Color != c)) ?? RuleColours[rules.Count % RuleColours.Length],
            }, form);
            rules.Add(rule);
            Own(f, rules);
            return (rule.Id, null);
        }
    }

    /// <summary>Makes saved alert settings the default for new sensors; false when they're not there.</summary>
    public bool DefaultTo(string id)
    {
        lock (gate)
        {
            var f = file.Read();
            var rules = Rules(f);
            if (rules.All(r => r.Id != id))
            {
                return false;
            }
            f.Default = id;
            Own(f, rules);
            return true;
        }
    }

    /// <summary>Deletes saved alert settings; the sensors on them move to the default. Why not, or null.</summary>
    public string? DeleteRule(string id)
    {
        lock (gate)
        {
            var f = file.Read();
            var rules = Rules(f);
            if (rules.All(r => r.Id != id))
            {
                return "Those alert settings aren't here any more.";
            }
            var fallback = DefaultRule(f, rules);
            if (fallback == id)
            {
                return "That's the default. Make another one the default first.";
            }
            rules.RemoveAll(r => r.Id == id);
            foreach (var s in f.Sensors.Where(s => s.Rule == id))
            {
                s.Rule = fallback;
            }
            Own(f, rules);
            return null;
        }
    }

    private static string? RuleName(List<AlertRule> rules, string? name, string? id) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40 ? "Give the alert settings a name, 40 characters at most."
        : rules.Any(r => r.Id != id && string.Equals(r.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) ? $"There are alert settings called {name.Trim()} already."
        : null;

    // From here on the list is kept as it is, not as changes to the four it started with.
    private void Own(SensorsFile f, List<AlertRule> rules)
    {
        f.Rules = rules;
        f.RulesOwned = true;
        file.Save(f);
    }

    // --- Checking -------------------------------------------------------------------------------------------------------

    /// <summary>Checks something once without keeping it, for Check now in the dialog.</summary>
    public async Task<(Probe? Probe, string? Problem)> Try(SensorForm form, CancellationToken stop)
    {
        var (sensor, problem) = Read(form);
        if (sensor is null)
        {
            return (null, problem);
        }
        if (SignsIn(sensor.Kind))
        {
            var kinds = LoginKinds(sensor);
            if (form.NewLogin is not null)
            {
                var kind = kinds.Contains(form.NewLogin.Kind ?? "") ? form.NewLogin.Kind! : kinds[0];
                if (SensorLogins.Problem(form.NewLogin, kind) is { } bad)
                {
                    return (null, bad);
                }
                var typed = form.NewLogin;
                sensor.Typed = new Login(typed.Username?.Trim(), typed.Password!, kind, typed.Auth == "SHA-1" ? "SHA-1" : "SHA-256", typed.Privacy == "None" ? "None" : "AES-128", typed.PrivacyPassword);
            }
            else if (logins.KindOf(form.Login) is { } saved && kinds.Contains(saved))
            {
                sensor.Login = form.Login;
            }
            else
            {
                return (null, "Pick saved credentials, or type a username and password.");
            }
        }
        sensor.Trying = true;
        return (await probe.Check(sensor, stop), null);
    }

    /// <summary>Checks one sensor now, whenever it was last checked. False when it isn't there.</summary>
    public async Task<bool> CheckNow(string id, CancellationToken stop)
    {
        Sensor? sensor;
        lock (gate)
        {
            sensor = file.Read().Sensors.FirstOrDefault(s => s.Id == id);
            if (sensor is null || !busy.Add(id))
            {
                return sensor is not null;
            }
        }
        await Check(sensor, stop);
        await Alerts(stop);
        return true;
    }

    /// <summary>One round: every sensor that's due is checked, then whoever should hear about one is emailed. Run every few seconds.</summary>
    public async Task Tick(CancellationToken stop)
    {
        List<Sensor> due;
        lock (gate)
        {
            var now = Now;
            due = [.. file.Read().Sensors.Where(s => !s.Paused && !busy.Contains(s.Id) && (State(s.Id).Checked is not { } last || (now - last).TotalSeconds >= s.Every - 1)).Take(64)];
            due.ForEach(s => busy.Add(s.Id));
        }
        await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = stop }, async (s, t) => await Check(s, t));
        await Alerts(stop);
        lock (gate)
        {
            if (dirty && (Now - saved).TotalSeconds >= 30)
            {
                Flush();
            }
        }
    }

    /// <summary>Writes how the sensors are doing to disk, when something changed.</summary>
    public void Flush()
    {
        lock (gate)
        {
            if (dirty)
            {
                var kept = file.Read().Sensors.Select(s => s.Id).ToHashSet();
                var all = states.Read();
                foreach (var id in all.States.Keys.Where(id => !kept.Contains(id)).ToList())
                {
                    all.States.Remove(id);
                }
                states.Save(all);
                dirty = false;
                saved = Now;
            }
        }
    }

    private async Task Check(Sensor s, CancellationToken stop)
    {
        Probe result;
        try
        {
            result = await probe.Check(s, stop);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Checking {Sensor} failed", s.Name);
            result = new Probe(false, Note: "The check itself failed");
        }
        finally
        {
            lock (gate)
            {
                busy.Remove(s.Id);
            }
        }
        Record(s, result);
    }

    // What a check found, added to the sensor's history; turns it down, up or to a warning.
    private void Record(Sensor s, Probe result)
    {
        lock (gate)
        {
            if (file.Read().Sensors.All(x => x.Id != s.Id))
            {
                return; // deleted while it was being checked
            }
            var now = Now;
            var st = State(s.Id);
            st.Checked = now;
            st.Recent.Add(new Sample(now, result.Ok ? result.Ms ?? 0 : -1));
            st.Recent.RemoveAll(x => (now - x.At).TotalMinutes > 60);
            if (st.Recent.Count > 120)
            {
                st.Recent.RemoveRange(0, st.Recent.Count - 120);
            }
            var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
            if (st.Hours.Count == 0 || st.Hours[^1].At != hour)
            {
                st.Hours.Add(new HourCount { At = hour });
                st.Hours.RemoveAll(h => (hour - h.At).TotalHours >= 168);
            }
            if (result.Ok)
            {
                st.Hours[^1].Ok++;
                var before = st.Status;
                st.Status = result.Warn ? "warn" : "up";
                st.Ms = result.Ms;
                st.Note = result.Note;
                st.Fails = 0;
                st.FailedAt = null;
                if (before == "down")
                {
                    Event(st, now, "up", $"Came back after {Span(now - (st.Since ?? now))}");
                }
                if (before != st.Status)
                {
                    if (st.Status == "warn")
                    {
                        Event(st, now, "warn", result.Note ?? "Needs attention");
                    }
                    st.Since = now;
                }
            }
            else
            {
                st.Hours[^1].Failed++;
                st.Fails++;
                st.FailedAt ??= now;
                st.Ms = null;
                st.Note = result.Note ?? "No reply";
                // One lost reply isn't an outage, unless the next check is an hour away.
                if (st.Status != "down" && st.Fails >= (s.Every >= 3600 ? 1 : 2))
                {
                    st.Status = "down";
                    st.Since = st.FailedAt;
                    Event(st, now, "down", st.Note);
                }
            }
            dirty = true;
        }
    }

    private static void Event(SensorState st, DateTime at, string what, string text)
    {
        st.Events.Add(new SensorEvent(at, what, text));
        if (st.Events.Count > 100)
        {
            st.Events.RemoveRange(0, st.Events.Count - 100);
        }
    }

    // Who should hear about what, worked out under the lock and sent outside it.
    private async Task Alerts(CancellationToken stop)
    {
        if (!mail.Ready)
        {
            return;
        }
        var send = new List<(string Id, MailNote Note, string Said)>();
        lock (gate)
        {
            var f = file.Read();
            var rules = Rules(f).ToDictionary(r => r.Id);
            var now = Now;
            var down = f.Sensors.Where(s => !s.Paused && State(s.Id).Status == "down").Select(s => s.Name).ToList();
            foreach (var s in f.Sensors.Where(s => !s.Paused))
            {
                var st = State(s.Id);
                var rule = s.Rule == "custom" && s.Custom is { } own ? own : rules.GetValueOrDefault(s.Rule) ?? rules[DefaultRule(f, [.. rules.Values])];
                if (st.Status == "down" && rule.After >= 0)
                {
                    var first = st.Alerted is null && (now - (st.Since ?? now)).TotalMinutes >= rule.After;
                    var again = st.Alerted is { } last && rule.Again > 0 && (now - last).TotalMinutes >= rule.Again;
                    if (first || again)
                    {
                        var others = down.Where(n => n != s.Name).ToList();
                        send.Add((s.Id, new MailNote($"{s.Name} is {(again ? "still " : "")}down", "down",
                        [
                            ("Sensor", What(s)), ("Group", s.Group ?? "No group"), ("Down since", $"{Local(st.Since ?? now)} ({Span(now - (st.Since ?? now))})"),
                            ("What it said", st.Note ?? "No reply"), ("Also down", others.Count == 0 ? "Nothing else" : string.Join(", ", others)),
                        ]), again ? "Emailed again: still down" : "Emailed: down"));
                        st.Alerted = now;
                        st.Told = true;
                        dirty = true;
                    }
                }
                else if (st.Status == "warn" && !st.Told && rule.After >= 0)
                {
                    send.Add((s.Id, new MailNote($"{s.Name}: {st.Note}", "warn", [("Sensor", What(s)), ("Group", s.Group ?? "No group")]), "Emailed: warning"));
                    st.Told = true;
                    dirty = true;
                }
                else if (st.Status == "up" && st.Told)
                {
                    // Back, or a warning cleared: said only when the outage was, and the rule wants it.
                    var outage = st.Events.LastOrDefault(e => e.What == "up");
                    if (st.Alerted is not null && rule.Back)
                    {
                        send.Add((s.Id, new MailNote($"{s.Name} is back up", "up",
                            [("Sensor", What(s)), ("Group", s.Group ?? "No group"), ("What happened", outage?.Text ?? "Came back"), ("Answering in", st.Ms is { } ms ? $"{ms} ms" : "time")]), "Emailed: back up"));
                    }
                    st.Told = false;
                    st.Alerted = null;
                    dirty = true;
                }
            }
        }
        foreach (var (id, note, said) in send)
        {
            var problem = await mail.Send(note, stop);
            lock (gate)
            {
                Event(State(id), Now, problem is null ? "email" : "problem", problem is null ? said : $"The email didn't go: {problem}");
                dirty = true;
            }
        }
    }

    /// <summary>What a sensor checks, in a few words: Ping · 192.168.2.27.</summary>
    public static string What(Sensor s) => s.Kind switch
    {
        "port" => $"Port {s.Port} · {s.Target}",
        "web" => $"Web page · {s.Target}",
        "dns" => $"DNS lookup · {s.Target}" + (s.Server is null ? "" : $" on {s.Server}"),
        "cert" => $"Certificate · {s.Target}" + (s.Port is null or 443 ? "" : $":{s.Port}"),
        "hw" => $"Server hardware · {s.Target}",
        "nas" => $"Storage · {s.Target}",
        "vmware" => $"VMware · {s.Target}",
        "snmp" => $"SNMP · {s.Target}",
        "svc" => $"Windows service · {s.Service} on {s.Target}",
        "disk" => $"Disk space · {s.Drive} on {s.Target}",
        _ => $"Ping · {s.Target}",
    };

    private static string Span(TimeSpan t) => t.TotalSeconds < 90 ? $"{Math.Max(1, (int) t.TotalSeconds)} seconds"
        : t.TotalMinutes < 90 ? $"{(int) Math.Round(t.TotalMinutes)} minutes" : t.TotalHours < 36 ? $"{(int) Math.Round(t.TotalHours)} hours" : $"{(int) Math.Round(t.TotalDays)} days";

    private static string Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local).ToString("MMM d, HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\d+(\.\d+){2,127}$")]
    private static partial Regex OidPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9._:-]{0,252}$")]
    private static partial Regex HostName();
}

/// <summary>Checks the sensors every few seconds for as long as Rabbid Lab runs, and saves how they're doing when it stops.</summary>
public sealed class SensorWorker(Sensors sensors, ILogger<SensorWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await sensors.Tick(stop);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogError(e, "A round of sensor checks failed");
                }
                await Task.Delay(TimeSpan.FromSeconds(5), stop);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
        finally
        {
            sensors.Flush();
        }
    }
}

public static class SensorsApi
{
    public static void MapSensorsApi(this WebApplication app)
    {
        app.MapGet("/api/sensors", (HttpContext context, Sensors sensors) =>
            !Access.Can(context, Permissions.SensorsView) ? Access.NotAllowed() : Results.Ok(sensors.View()));

        app.MapGet("/api/sensors/{id}", (string id, HttpContext context, Sensors sensors) =>
            !Access.Can(context, Permissions.SensorsView) ? Access.NotAllowed()
            : sensors.Detail(id) is { } detail ? Results.Ok(detail) : Results.NotFound(new { error = "That sensor isn't here any more." }));

        // Checks what the form describes once, without keeping it.
        app.MapPost("/api/sensors/try", async (SensorForm form, HttpContext context, Sensors sensors) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsEdit) is { } refused)
            {
                return refused;
            }
            var (found, problem) = await sensors.Try(form, context.RequestAborted);
            return found is null ? Results.BadRequest(new { error = problem }) : Results.Ok(new { found.Ok, found.Ms, found.Note, found.Warn, found.Detail, found.Title, found.Parts, found.Pin });
        });

        app.MapPost("/api/sensors", (SensorForm form, HttpContext context, Sensors sensors, ActivityLog log) => Save(null, form, context, sensors, log));

        // The computers in Seats a sensor can check through, and whether their client is connected now.
        app.MapGet("/api/sensors/seats", (HttpContext context, Clients clients, ClientCommands commands) =>
            !Access.Can(context, Permissions.SensorsEdit) ? Access.NotAllowed()
            : Results.Ok(clients.All().Where(c => c.Approved is not null).OrderBy(c => c.Computer, StringComparer.OrdinalIgnoreCase)
                .Select(c => new { c.Id, c.Computer, connected = commands.Listening(c.Id) })));

        // The saved credentials, for the dialog: names and usernames, never passwords.
        app.MapGet("/api/sensors/logins", (HttpContext context, Sensors sensors, SensorLogins logins) =>
            !Access.Can(context, Permissions.SensorsEdit) ? Access.NotAllowed() : Results.Ok(logins.List(sensors.All())));

        app.MapPost("/api/sensors/{id}", (string id, SensorForm form, HttpContext context, Sensors sensors, ActivityLog log) => Save(id, form, context, sensors, log));

        app.MapPost("/api/sensors/{id}/delete", (string id, HttpContext context, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsEdit) is { } refused)
            {
                return refused;
            }
            if (sensors.Delete(id) is not { } gone)
            {
                return Results.NotFound(new { error = "That sensor isn't here any more." });
            }
            log.Record(context, "sensors", "sensor.delete", $"Deleted the sensor {gone.Name}");
            return Results.Ok(sensors.View());
        });

        app.MapPost("/api/sensors/{id}/pause", (string id, PauseForm form, HttpContext context, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsEdit) is { } refused)
            {
                return refused;
            }
            if (sensors.Pause(id, form.Paused) is not { } sensor)
            {
                return Results.NotFound(new { error = "That sensor isn't here any more." });
            }
            log.Record(context, "sensors", form.Paused ? "sensor.pause" : "sensor.resume", form.Paused ? $"Paused the sensor {sensor.Name}" : $"Started the sensor {sensor.Name} again");
            return Results.Ok(sensors.View());
        });

        app.MapPost("/api/sensors/{id}/check", async (string id, HttpContext context, Sensors sensors) =>
            Access.RefuseUnless(context, Permissions.SensorsEdit) is { } refused ? refused
            : await sensors.CheckNow(id, context.RequestAborted) ? Results.Ok(sensors.View()) : Results.NotFound(new { error = "That sensor isn't here any more." }));

        app.MapPost("/api/sensors/groups", (GroupForm form, HttpContext context, Sensors sensors, ActivityLog log) =>
            Group(context, sensors, log, () => sensors.AddGroup(form.Name), "sensor.group.add", $"Made the sensor group {form.Name?.Trim()}"));

        app.MapPost("/api/sensors/groups/rename", (GroupForm form, HttpContext context, Sensors sensors, ActivityLog log) =>
            Group(context, sensors, log, () => sensors.RenameGroup(form.Name, form.To), "sensor.group.rename", $"Renamed the sensor group {form.Name?.Trim()} to {form.To?.Trim()}"));

        app.MapPost("/api/sensors/groups/delete", (GroupForm form, HttpContext context, Sensors sensors, ActivityLog log) =>
            Group(context, sensors, log, () => sensors.DeleteGroup(form.Name) ? null : "That group isn't here any more.", "sensor.group.delete", $"Deleted the sensor group {form.Name?.Trim()}"));

        app.MapPost("/api/sensors/groups/order", (GroupForm form, HttpContext context, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsEdit) is { } refused)
            {
                return refused;
            }
            sensors.OrderGroups(form.Names);
            log.Record(context, "sensors", "sensor.group.order", "Reordered the sensor groups");
            return Results.Ok(sensors.View());
        });

        app.MapPost("/api/sensors/rules/{id}", (string id, RuleForm form, HttpContext context, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            if (sensors.SaveRule(id, form) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "sensor.rule", form.Name is null ? $"Changed the alert settings {id}" : $"Changed the alert settings {form.Name.Trim()}");
            return Results.Ok(sensors.View());
        });

        // Saved alert settings: a new one, the default for new sensors, and deleting one (its sensors move to the default).
        app.MapPost("/api/sensors/rules", (RuleForm form, HttpContext context, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var (id, problem) = sensors.AddRule(form);
            if (id is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "sensor.rule.add", $"Saved the alert settings {form.Name!.Trim()}");
            return Results.Ok(new { id, now = sensors.View() });
        });

        app.MapPost("/api/sensors/rules/{id}/default", (string id, HttpContext context, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            if (!sensors.DefaultTo(id))
            {
                return Results.NotFound(new { error = "Those alert settings aren't here any more." });
            }
            log.Record(context, "sensors", "sensor.rule.default", $"Made {id} the default alert settings");
            return Results.Ok(sensors.View());
        });

        app.MapPost("/api/sensors/rules/{id}/delete", (string id, HttpContext context, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            if (sensors.DeleteRule(id) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "sensor.rule.delete", $"Deleted the alert settings {id}");
            return Results.Ok(sensors.View());
        });
    }

    // A change to the groups, made only once the person is known to be allowed to.
    private static IResult Group(HttpContext context, Sensors sensors, ActivityLog log, Func<string?> change, string action, string title)
    {
        if (Access.RefuseUnless(context, Permissions.SensorsEdit) is { } refused)
        {
            return refused;
        }
        if (change() is { } problem)
        {
            return Results.BadRequest(new { error = problem });
        }
        log.Record(context, "sensors", action, title);
        return Results.Ok(sensors.View());
    }

    private static async Task<IResult> Save(string? id, SensorForm form, HttpContext context, Sensors sensors, ActivityLog log)
    {
        if (Access.RefuseUnless(context, Permissions.SensorsEdit) is { } refused)
        {
            return refused;
        }
        // Saved without Check now: the certificate the device has now is the one trusted (none, when Windows trusts it).
        if (Sensors.SignsIn(form.Kind ?? "") && string.IsNullOrWhiteSpace(form.Pin) && !string.IsNullOrWhiteSpace(form.Target))
        {
            form = form with { Pin = await DeviceHttp.Peek(form.Target.Trim(), context.RequestAborted) };
        }
        var (sensor, problem) = sensors.Save(id, form, ActivityLog.Who(context));
        if (sensor is null)
        {
            return Results.BadRequest(new { error = problem });
        }
        log.Record(context, "sensors", id is null ? "sensor.add" : "sensor.change", id is null ? $"Added the sensor {sensor.Name}" : $"Changed the sensor {sensor.Name}");
        return Results.Ok(sensors.View());
    }
}

public sealed record PauseForm(bool Paused);
