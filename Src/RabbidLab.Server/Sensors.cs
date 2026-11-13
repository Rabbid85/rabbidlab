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

    /// <summary>A sensor on a Developer tools test address: played rather than checked, and gone at Expires.</summary>
    public DateTime? Expires { get; set; }

    /// <summary>Check now: any certificate is read, to offer it to pin.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Trying { get; set; }
    public string? Group { get; set; }

    /// <summary>Seconds between checks.</summary>
    public int Every { get; set; } = 60;

    /// <summary>follow for its alert rule's times and notifications, custom for its own (Custom), or a saved setting from before rules by id.</summary>
    public string Rule { get; set; } = "normal";

    /// <summary>It watches a link on Links, so its down alerts come under the watched-link rule.</summary>
    public bool Link { get; set; }

    /// <summary>With Rule custom: when it emails, again, and on coming back.</summary>
    public AlertRule? Custom { get; set; }

    /// <summary>Where its alerts go (Channels.cs): email and channel ids. Null, for a sensor from before channels, is email.</summary>
    public List<string>? Channels { get; set; }

    /// <summary>Misses in a row before it counts as down: 1, 2, 3 or 5; null for 2 (1 for an hourly check).</summary>
    public int? DownAfter { get; set; }

    /// <summary>For web over https: warn this many days before the certificate expires; null not to look.</summary>
    public int? CertDays { get; set; }

    /// <summary>For ping, port and web: down when it answers, for something that should be off.</summary>
    public bool Invert { get; set; }

    /// <summary>Seconds one check waits for an answer: 5, 10 or 30; null for the kind's usual.</summary>
    public int? Timeout { get; set; }

    /// <summary>Words to find it by, lower case.</summary>
    public List<string>? Tags { get; set; }

    /// <summary>The sensor it sits behind, by id: while that one is down, this one waits instead of alerting.</summary>
    public string? DependsOn { get; set; }
    public bool Paused { get; set; }

    /// <summary>When a timed pause ends; null for a pause that lasts until it's started again.</summary>
    public DateTime? PausedUntil { get; set; }

    /// <summary>The sensor it was paused along with, by id: starting that one again starts this one too.</summary>
    public string? PausedWith { get; set; }
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

    /// <summary>Down alerts close together go out as one: the first waits GatherSecs for others to join it.</summary>
    public bool Gather { get; set; }
    public int GatherSecs { get; set; } = 60;

    /// <summary>From before alert rules: when new sensors alerted. The down rules started from it.</summary>
    public AlertRule? Start { get; set; }

    /// <summary>The sensors whose times matched their rule were made to follow it, once.</summary>
    public bool Followed { get; set; }
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

    /// <summary>For hw, nas, vmware and snmp: the watched parts as the last answer found them, for the cards.</summary>
    public List<ProbePart>? Parts { get; set; }

    /// <summary>When the last email about it being down went out.</summary>
    public DateTime? Alerted { get; set; }

    /// <summary>When the last alert about its warning went out.</summary>
    public DateTime? Warned { get; set; }

    /// <summary>Someone was emailed that it's down, so they're told when it's back.</summary>
    public bool Told { get; set; }

    /// <summary>When it last came back from down: what depends on it waits until its own next check.</summary>
    public DateTime? Back { get; set; }
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
        if (s.Expires is not null && DevTools.IsTest(s.Target))
        {
            return await DevTools.Check(s, stop);
        }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(TimeSpan.FromSeconds(s.Timeout ?? s.Kind switch { "web" => 10, "hw" or "nas" or "vmware" => 45, "snmp" => 15, "svc" or "disk" => 45, _ => 5 }));
        try
        {
            return s.Kind switch
            {
                "port" => await Port(s.Target, s.Port ?? 0, limit.Token),
                "web" => await WebAndCertificate(s, limit.Token),
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

    // Certificates read for web sensors, by host and port: a certificate doesn't change by the minute, so once an hour.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, Probe Found)> Certificates = new();

    // The page, then for a sensor that asks, its certificate: a warning when it's close to expiring, its days left otherwise.
    private async Task<Probe> WebAndCertificate(Sensor s, CancellationToken stop)
    {
        var page = await Web(s, stop);
        if (!page.Ok || s.CertDays is not { } days || !Uri.TryCreate(s.Target, UriKind.Absolute, out var u) || u.Scheme != "https")
        {
            return page;
        }
        var key = $"{u.Host}:{u.Port}";
        var now = clock.GetUtcNow().UtcDateTime;
        if (s.Trying || !Certificates.TryGetValue(key, out var seen) || (now - seen.At).TotalHours >= 1)
        {
            try
            {
                seen = (now, await Certificate(u.Host, u.Port, days, stop));
                Certificates[key] = seen;
            }
            catch (Exception e) when (e is SocketException or IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException)
            {
                return page; // the page answered; a certificate that can't be read now is tried again next time
            }
        }
        var cert = seen.Found;
        var said = cert.Note is { Length: > 0 } n ? char.ToLowerInvariant(n[0]) + n[1..] : "";
        return !cert.Ok || cert.Warn ? page with { Warn = true, Note = $"Certificate {said}", Detail = page.Note }
            : page with { Detail = $"Certificate {said}" };
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
            ConnectTimeout = TimeSpan.FromSeconds(8),
            // A new connection each check (redirects within one still share it), so reply times don't jump between reused and new.
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15),
            // No resumed TLS sessions, as for links: Netflix never answers on one.
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true, AllowTlsResume = false },
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(31) }; // each check has its own limit: 5, 10 or 30 seconds
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"RabbidLab/{Product.Version}");
        return http;
    }
}

/// <summary>A sensor as the page sends it.</summary>
public sealed record SensorForm(string? Name, string? Kind, string? Target, int? Port, string? Server, string? Group, int? Every, string? Rule,
    string? Expect = null, string? Text = null, string? Address = null, int? WarnDays = null, string? Login = null, LoginForm? NewLogin = null, List<string>? Watch = null, string? Pin = null,
    string? Product = null, int? WarnFree = null, string? Value = null, string? Oid = null, string? Interface = null, RuleForm? Custom = null,
    string? Seat = null, string? Service = null, string? Drive = null, List<string>? Channels = null, int? DownAfter = null, int? CertDays = null, bool? Invert = null,
    int? Timeout = null, List<string>? Tags = null, string? DependsOn = null);

public sealed record RuleForm(int? After, int? Again, bool? Back, string? Name = null);

/// <summary>
/// The Sensors page's sensors, kept in sensors.json, and how each is doing, kept in sensor-state.json. A sensor is down
/// after its misses in a row (two unless it says, one for a sensor checked hourly). Its alert rule says how long it stays
/// down before the alert goes to its channels, so something that comes straight back tells nobody.
/// </summary>
public sealed partial class Sensors(DataFolder folder, TimeProvider clock, ISensorProbe probe, Alerts alerts, ILogger<Sensors> log, SensorLogins logins, Channels channels)
{
    /// <summary>How many sensors, and how many watched links, send their down alerts to a notification, for Settings, Notifications.</summary>
    public (int Sensors, int Links) Using(string channel)
    {
        lock (gate)
        {
            var using_ = file.Read().Sensors.Where(s => Sends(s).Contains(channel)).ToList();
            return (using_.Count(s => !s.Link), using_.Count(s => s.Link));
        }
    }

    // Where a sensor's down alerts go: its rule's notifications when it follows its rule, else its own.
    private List<string> Sends(Sensor s) => s.Rule == "follow" ? alerts.Rule(s.Link ? Alerts.LinkDown : Alerts.SensorDown).To : s.Channels ?? [Channels.Email];

    // The notifications an alert of this kind goes to now: down alerts as the sensor sends them, warnings by their rule.
    private List<string> To(Sensor s, string key) => [.. (key is Alerts.SensorDown or Alerts.LinkDown ? Sends(s) : alerts.Rule(key).To).Where(channels.Ready)];

    // The rule a sensor's down alerts come under.
    private static string DownKey(Sensor s) => s.Link ? Alerts.LinkDown : Alerts.SensorDown;

    // The rule a warning comes under: a certificate, space running low, or a part of a server.
    private static string WarnKey(Sensor s, SensorState st) =>
        s.Kind == "cert" || st.Note?.StartsWith("Certificate", StringComparison.Ordinal) == true ? "sensor.cert"
        : s.Kind == "disk" || st.Parts?.Any(p => p.Key == "space" && p.State != "ok") == true ? "sensor.space"
        : "sensor.hardware";

    /// <summary>For each alert rule: how many sensors or links it covers, and for the down rules how many set their own times.</summary>
    public object RuleCounts()
    {
        lock (gate)
        {
            var all = file.Read().Sensors;
            var plain = all.Where(s => !s.Link && s.Expires is null).ToList();
            var links = all.Where(s => s.Link).ToList();
            bool Parts(Sensor s, params string[] keys) => s.Watch is null ? PartsOf(s.Kind).Any(keys.Contains) : s.Watch.Any(keys.Contains);
            return new Dictionary<string, object>
            {
                [Alerts.SensorDown] = new { follow = plain.Count(s => s.Rule == "follow"), own = plain.Where(s => s.Rule != "follow").Select(s => s.Name).ToList() },
                [Alerts.LinkDown] = new { follow = links.Count(s => s.Rule == "follow"), own = links.Where(s => s.Rule != "follow").Select(s => s.Name).ToList() },
                ["sensor.cert"] = new { count = all.Count(s => s.Kind == "cert" || s.Kind == "web" && s.CertDays is not null) },
                ["sensor.space"] = new { count = all.Count(s => s.Kind == "disk" || s.Kind is "nas" or "vmware" && Parts(s, "space")) },
                ["sensor.hardware"] = new { count = all.Count(s => s.Kind == "hw" || s.Kind == "nas" && Parts(s, "pools", "disks") || s.Kind == "vmware" && Parts(s, "hosts", "alarms") || s.Kind == "snmp" && Parts(s, "battery", "power")) },
            };
        }
    }

    /// <summary>Whether down alerts close together go as one, and how long the first waits.</summary>
    public object Gathering()
    {
        lock (gate)
        {
            var f = file.Read();
            return new { on = f.Gather, secs = f.GatherSecs };
        }
    }

    /// <summary>Makes the sensors with their own times (or the watched links with theirs) follow their rule. How many changed.</summary>
    public int Follow(bool links)
    {
        lock (gate)
        {
            var f = file.Read();
            var changed = 0;
            foreach (var s in f.Sensors.Where(s => s.Link == links && s.Rule != "follow" && s.Expires is null))
            {
                (s.Rule, s.Custom, s.Channels) = ("follow", null, null);
                changed++;
            }
            file.Save(f);
            return changed;
        }
    }

    /// <summary>
    /// At start: marks the sensors that watch links, and the first time, makes every sensor whose times and notifications
    /// are what its rule says follow that rule, so a change to the rule changes them too.
    /// </summary>
    public void Settle(IEnumerable<string> linkSensors)
    {
        var watching = linkSensors.ToHashSet();
        lock (gate)
        {
            var f = file.Read();
            foreach (var s in f.Sensors)
            {
                s.Link = watching.Contains(s.Id);
            }
            if (!f.Followed)
            {
                foreach (var s in f.Sensors.Where(s => s.Rule != "follow" && s.Expires is null))
                {
                    var own = Alerting(f, s);
                    var rule = alerts.Rule(DownKey(s));
                    var sends = (s.Channels ?? [Channels.Email]).Where(channels.Known).ToHashSet();
                    if (rule.On && own.After == rule.After && own.Again == rule.Again && own.Back == rule.Back && sends.SetEquals(rule.To))
                    {
                        (s.Rule, s.Custom, s.Channels) = ("follow", null, null);
                    }
                }
                f.Followed = true;
            }
            file.Save(f);
        }
    }

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
    private readonly HashSet<string> soon = []; // checked on the next round, whenever they were last checked
    private readonly Dictionary<string, DateTime> held = []; // down alerts waiting for others to join them (Gather), from when
    private readonly HashSet<string> quiet = []; // sensor:rule alerts held for quiet hours, written down once
    private readonly Dictionary<string, DateTime> tested = []; // when each sensor last sent a test alert
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

    private static AlertRule Own(RuleForm form) => Timed(new AlertRule { Id = "custom", Name = "Custom", Color = "#8A8FA8" }, form);

    private static AlertRule Own(AlertRule r) => new() { Id = "custom", Name = "Custom", Color = "#8A8FA8", After = r.After, Again = r.Again, Back = r.Back };

    // When a sensor alerts: its rule's times, its own, or for one from before sensors had them, the saved settings it was on.
    private AlertRule Alerting(SensorsFile f, Sensor s)
    {
        if (s.Rule == "follow")
        {
            return alerts.Times(DownKey(s));
        }
        if (s.Rule == "custom" && s.Custom is { } own)
        {
            return own;
        }
        var rules = Rules(f);
        return rules.FirstOrDefault(r => r.Id == s.Rule) ?? rules.First(r => r.Id == DefaultRule(f, rules));
    }

    // When new sensors alert.
    private static AlertRule StartRule(SensorsFile f)
    {
        var rules = Rules(f);
        return Own(f.Start ?? rules.First(r => r.Id == DefaultRule(f, rules)));
    }

    /// <summary>When new sensors alerted before alert rules, which the down rules start from.</summary>
    public AlertRule StartAlerts()
    {
        lock (gate)
        {
            return StartRule(file.Read());
        }
    }

    private static object Times(AlertRule r) => new { r.After, r.Again, r.Back };

    private SensorState State(string id)
    {
        var all = states.Read().States;
        if (!all.TryGetValue(id, out var state))
        {
            all[id] = state = new SensorState();
        }
        return state;
    }

    // What a down sensor waits on instead of alerting: the furthest one up its chain that's down, or that came back
    // since this one was last checked. Null when it doesn't wait. Called under the lock.
    private Sensor? Waits(Dictionary<string, Sensor> byId, Sensor s)
    {
        var mine = State(s.Id);
        if (s.Paused || mine.Status != "down")
        {
            return null;
        }
        Sensor? found = null;
        var seen = new HashSet<string> { s.Id };
        for (var p = Above(byId, s); p is not null && seen.Add(p.Id); p = Above(byId, p))
        {
            var ps = State(p.Id);
            if (!p.Paused && (ps.Status == "down" || ps.Back is { } back && (mine.Checked ?? DateTime.MinValue) < back))
            {
                found = p;
            }
        }
        return found;
    }

    private static Sensor? Above(Dictionary<string, Sensor> byId, Sensor s) => s.DependsOn is { } id ? byId.GetValueOrDefault(id) : null;

    // Everything that depends on a sensor, directly or through others.
    private static List<Sensor> Behind(SensorsFile f, string id)
    {
        var found = new List<Sensor>();
        var seen = new HashSet<string> { id };
        var next = new Queue<string>([id]);
        while (next.TryDequeue(out var at))
        {
            foreach (var s in f.Sensors.Where(s => s.DependsOn == at && seen.Add(s.Id)))
            {
                found.Add(s);
                next.Enqueue(s.Id);
            }
        }
        return found;
    }

    // Names as a list for an alert: a, b and c; past a few, a count.
    private static string Few(List<string> names, int most = 8) => names.Count == 0 ? "Nothing else"
        : names.Count > most ? $"{string.Join(", ", names.Take(most))} and {names.Count - most} more"
        : names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";

    // --- What the page reads ------------------------------------------------------------------------------------------

    /// <summary>Everything the Sensors page shows: the sensors in their groups' order with how each is doing, the groups and the rules.</summary>
    public object View()
    {
        lock (gate)
        {
            Sweep();
            var f = file.Read();
            var groups = Known(f);
            var place = groups.Select((g, i) => (g, i)).ToDictionary(p => p.g, p => p.i, Grouping.Same);
            var counts = f.Sensors.GroupBy(s => s.Rule).ToDictionary(g => g.Key, g => g.Count());
            var byId = f.Sensors.ToDictionary(s => s.Id);
            return new
            {
                sensors = f.Sensors.OrderBy(s => s.Group is { } g && place.TryGetValue(g, out var at) ? at : int.MaxValue).ThenBy(s => s.Name, Grouping.Same).Select(s => Row(s, State(s.Id), Waits(byId, s), Alerting(f, s))).ToList(),
                groups,
                rules = Rules(f).Select(r => new { r.Id, r.Name, r.Color, r.After, r.Again, r.Back, used = counts.GetValueOrDefault(r.Id) }).ToList(),
                @default = DefaultRule(f, Rules(f)),
                // What a sensor or a watched link that follows its rule gets, and the numbers new sensors warn at.
                start = Times(alerts.Times(Alerts.SensorDown)),
                startTo = alerts.Rule(Alerts.SensorDown).To,
                linkStart = Times(alerts.Times(Alerts.LinkDown)),
                linkTo = alerts.Rule(Alerts.LinkDown).To,
                limits = new { cert = alerts.Rule("sensor.cert").Limit, space = alerts.Rule("sensor.space").Limit },
                gather = new { on = f.Gather, secs = f.GatherSecs },
                last = f.Sensors.Select(s => State(s.Id).Checked).Max(),
            };
        }
    }

    // A sensor as the page has it. One that's down while something it depends on is down shows as wait.
    private object Row(Sensor s, SensorState st, Sensor? waits, AlertRule alert) => new
    {
        s.Id, s.Name, s.Kind, s.Target, s.Port, s.Server, s.Expect, s.Text, s.Address, s.WarnDays, s.Login, s.Watch, s.Pin, s.Product, s.WarnFree, s.Value, s.Oid, s.Interface, s.Seat, s.Service, s.Drive, s.Group, s.Every, s.Rule, s.Custom, s.Paused,
        alert = Times(alert), follows = s.Rule == "follow", s.Link,
        channels = Sends(s), s.DownAfter, s.CertDays, s.Invert, s.Timeout, tags = s.Tags ?? [], s.DependsOn, s.PausedUntil,
        status = s.Paused ? "paused" : waits is not null ? "wait" : st.Status,
        waits = waits?.Name,
        since = st.Since,
        ms = st.Ms,
        note = st.Note,
        parts = st.Parts,
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
            var f = file.Read();
            if (f.Sensors.FirstOrDefault(s => s.Id == id) is not { } s)
            {
                return null;
            }
            var st = State(id);
            return new { sensor = Row(s, st, Waits(f.Sensors.ToDictionary(x => x.Id), s), Alerting(f, s)), up24 = Uptime(st, 24), up7 = Uptime(st, 168), samples = st.Recent.ToList(), events = st.Events.AsEnumerable().Reverse().ToList(), alerted = st.Alerted };
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
            Pin = SignsIn(kind) && !string.IsNullOrWhiteSpace(form.Pin) ? form.Pin.Trim() : null, Every = every, Tags = Tagged(form.Tags), DownAfter = form.DownAfter is 1 or 2 or 3 or 5 ? form.DownAfter : null,
            CertDays = kind == "web" && target.StartsWith("https:", StringComparison.OrdinalIgnoreCase) && form.CertDays is 7 or 14 or 30 ? form.CertDays : null,
            Invert = form.Invert == true && kind is "ping" or "port" or "web", Timeout = kind is "ping" or "port" or "web" or "dns" && form.Timeout is 5 or 10 or 30 ? form.Timeout : null }, null);
    }

    // Tags as kept: lower case, trimmed, no repeats, 8 at most of 24 characters each.
    private static List<string>? Tagged(List<string>? tags)
    {
        var kept = (tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length is > 0 and <= 24 && !t.Any(char.IsControl)).Distinct().Take(8).ToList();
        return kept.Count == 0 ? null : kept;
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
        return (new Sensor { Name = name, Kind = kind, Target = computer, Seat = form.Seat.Trim(), Service = service, Drive = drive, WarnFree = free, Every = every,
            Tags = Tagged(form.Tags), DownAfter = form.DownAfter is 1 or 2 or 3 or 5 ? form.DownAfter : null }, null);
    }

    // Test sensors past their time go, with their history. Called under the lock.
    private void Sweep()
    {
        var f = file.Read();
        var now = Now;
        var gone = f.Sensors.Where(s => s.Expires is { } end && end <= now).Select(s => s.Id).ToList();
        if (gone.Count == 0)
        {
            return;
        }
        f.Sensors.RemoveAll(s => gone.Contains(s.Id));
        file.Save(f);
        gone.ForEach(id => states.Read().States.Remove(id));
        dirty = true;
    }

    /// <summary>The sensor saved (new when id is null), or why it can't be.</summary>
    public (Sensor? Sensor, string? Problem) Save(string? id, SensorForm form, string by, bool testing = false)
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
                return (null, $"That's {Most} sensors, which is as many as {Product.Title} keeps.");
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
            next.Link = old?.Link ?? false;
            if (form.Rule == "follow" || old is null && form.Rule != "custom" && !rules.Any(r => r.Id == form.Rule))
            {
                // Its rule's times and notifications, and whatever they're changed to later.
                next.Rule = "follow";
                next.Custom = null;
            }
            else if (form.Rule == "custom")
            {
                if (form.Custom is null || Timings(form.Custom) is not null)
                {
                    return (null, "Pick the times for this sensor's own alerts.");
                }
                next.Rule = "custom";
                next.Custom = Own(form.Custom);
            }
            else if (rules.Any(r => r.Id == form.Rule))
            {
                next.Rule = form.Rule!;
            }
            else
            {
                // Not sent: it keeps the times it has.
                next.Rule = old!.Rule;
                next.Custom = old.Custom;
            }
            // Where its down alerts go: following its rule, the rule's; else the notifications sent, or the ones it had.
            next.Channels = next.Rule == "follow" ? null
                : form.Channels is { } picked ? [.. picked.Where(channels.Known).Distinct()] : old?.Channels ?? alerts.Rule(DownKey(next)).To;
            // A new certificate sensor warns at what the certificate rule says, unless it says otherwise.
            if (old is null && next.Kind == "cert" && form.WarnDays is null)
            {
                next.WarnDays = alerts.Rule("sensor.cert").Limit;
            }
            // What it depends on: a sensor that's here and doesn't depend on it already. Not sent keeps what it had.
            var above = form.DependsOn is null ? old?.DependsOn : form.DependsOn.Trim();
            if (string.IsNullOrEmpty(above) || form.DependsOn is null && f.Sensors.All(s => s.Id != above))
            {
                next.DependsOn = null;
            }
            else if (f.Sensors.All(s => s.Id != above))
            {
                return (null, "The sensor it depends on isn't here any more.");
            }
            else if (above == next.Id || Behind(f, next.Id).Any(s => s.Id == above))
            {
                return (null, "It can't depend on that one, because that one depends on it.");
            }
            else
            {
                next.DependsOn = above;
            }
            next.Paused = old?.Paused ?? false;
            next.PausedUntil = old?.PausedUntil;
            next.PausedWith = old?.PausedWith;
            next.Added = old?.Added ?? Now;
            next.AddedBy = old is null ? by : old.AddedBy;
            if (testing && DevTools.IsTest(next.Target))
            {
                next.Expires = Now + DevTools.Lasts;
                next.Every = 5;
                next.Rule = "custom";
                next.Custom = new AlertRule { Id = "custom", Name = "Custom", Color = "#8A8FA8", After = -1, Back = false };
            }
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
                    || old.Address != next.Address || old.WarnDays != next.WarnDays || old.Product != next.Product || old.Invert != next.Invert || old.CertDays != next.CertDays
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
                // What depended on it or was paused with it is on its own now.
                foreach (var s in f.Sensors.Where(s => s.DependsOn == id || s.PausedWith == id))
                {
                    s.DependsOn = s.DependsOn == id ? null : s.DependsOn;
                    s.PausedWith = s.PausedWith == id ? null : s.PausedWith;
                }
                file.Save(f);
                logins.Prune(f.Sensors);
                states.Read().States.Remove(id);
                dirty = true;
            }
            return gone;
        }
    }

    /// <summary>
    /// Pauses a sensor, for some minutes or until it's started again, or starts it again. A paused sensor isn't checked and
    /// alerts nobody. With dependents, what depends on it and isn't paused already pauses with it, and starts again with it.
    /// The sensor and how many others went with it, or null when it isn't there.
    /// </summary>
    public (Sensor Sensor, int Also)? Pause(string id, bool paused, int? minutes = null, bool dependents = false)
    {
        lock (gate)
        {
            var f = file.Read();
            if (f.Sensors.FirstOrDefault(s => s.Id == id) is not { } s)
            {
                return null;
            }
            var also = 0;
            if (paused)
            {
                var until = minutes is > 0 ? Now.AddMinutes(minutes.Value) : (DateTime?) null;
                Hold(s, until, null);
                foreach (var d in dependents ? Behind(f, id).Where(d => !d.Paused) : [])
                {
                    Hold(d, until, s);
                    also++;
                }
            }
            else
            {
                Start(s, false);
                foreach (var d in f.Sensors.Where(d => d.Paused && d.PausedWith == id))
                {
                    Start(d, false);
                    also++;
                }
            }
            file.Save(f);
            return (s, also);
        }
    }

    // Paused until a time, or until started again; with, when it went along with another. Under the lock.
    private void Hold(Sensor s, DateTime? until, Sensor? with)
    {
        s.Paused = true;
        s.PausedUntil = until;
        s.PausedWith = with?.Id;
        held.Remove(s.Id);
        Event(State(s.Id), Now, "pause", (with is null ? "Paused" : $"Paused with {with.Name}") + (until is { } u ? $" until {Local(u)}" : ""));
        dirty = true;
    }

    // Starting again is a clean start: it's checked at once, and an old outage isn't carried on. Under the lock.
    private void Start(Sensor s, bool timed)
    {
        s.Paused = false;
        s.PausedUntil = null;
        s.PausedWith = null;
        var st = State(s.Id);
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
        Event(st, Now, "resume", timed ? "Started again when its pause ended" : "Started again");
        dirty = true;
    }

    /// <summary>Puts a sensor in another group, or in none, and changes nothing else. Why not, or null.</summary>
    public string? MoveTo(string id, string? group)
    {
        lock (gate)
        {
            var f = file.Read();
            if (f.Sensors.FirstOrDefault(s => s.Id == id) is not { } s)
            {
                return "That sensor isn't here any more.";
            }
            var known = Known(f);
            var (name, full) = Grouping.Join(known, group);
            if (full is not null)
            {
                return full;
            }
            s.Group = name;
            f.Groups = known;
            file.Save(f);
            return null;
        }
    }

    /// <summary>Whether down alerts close together go out as one, and how long the first waits for others. Why not, or null.</summary>
    public string? GatherAlerts(bool on, int? secs)
    {
        if (secs is not (30 or 60 or 120 or 300))
        {
            return "Pick one of the times offered.";
        }
        lock (gate)
        {
            var f = file.Read();
            f.Gather = on;
            f.GatherSecs = secs.Value;
            file.Save(f);
            return null;
        }
    }

    /// <summary>
    /// Gives sensors these alert times, and these channels when sent. What each depends on is left as it is.
    /// How many it changed, or why not.
    /// </summary>
    public (int Changed, string? Problem) CopyAlerts(RuleForm form, List<string>? to, IReadOnlyList<string>? ids)
    {
        if (Timings(form) is { } problem)
        {
            return (0, problem);
        }
        lock (gate)
        {
            var f = file.Read();
            var picked = (ids ?? []).ToHashSet();
            var changed = 0;
            foreach (var s in f.Sensors.Where(s => picked.Contains(s.Id)))
            {
                s.Rule = "custom";
                s.Custom = Own(form);
                if (to is not null)
                {
                    s.Channels = [.. to.Where(channels.Known).Distinct()];
                }
                changed++;
            }
            file.Save(f);
            return (changed, null);
        }
    }

    /// <summary>Whether a sensor with this id is here.</summary>
    public bool Has(string id)
    {
        lock (gate)
        {
            return file.Read().Sensors.Any(s => s.Id == id);
        }
    }

    /// <summary>How the sensor watching a link is doing, as Links shows a link; null when it's paused, gone or not checked yet.</summary>
    public LinkState? AsLink(string id)
    {
        lock (gate)
        {
            var f = file.Read();
            if (f.Sensors.FirstOrDefault(s => s.Id == id) is not { Paused: false } s)
            {
                return null;
            }
            var st = State(id);
            if (st.Status is not ("up" or "warn" or "down"))
            {
                return null;
            }
            var down = st.Status == "down";
            var why = st.Note is { Length: > 0 } n ? char.ToLowerInvariant(n[0]) + n[1..] : null;
            return new LinkState(!down, down, st.Ms, null, down ? st.Since : null, st.Checked, down ? why : null);
        }
    }

    /// <summary>Why a link can't be watched as asked, or null: it has to be a web address, the times have to be offered ones, and there has to be room.</summary>
    public string? CanWatch(string? sensor, string? url, RuleForm alerts)
    {
        if (url is null || !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "Only web addresses (http:// or https://) can be watched.";
        }
        if (Timings(alerts) is { } problem)
        {
            return problem;
        }
        lock (gate)
        {
            var f = file.Read();
            return f.Sensors.Any(s => s.Id == sensor) || f.Sensors.Count < Most ? null : $"That's {Most} sensors, which is as many as {Product.Title} keeps.";
        }
    }

    /// <summary>
    /// The sensor that watches a link: a web page sensor made for it, or the one it has, given the link's name, address and
    /// group. It follows the watched-link rule, or has the alerts asked for; channels null keeps the ones it has. Its id, or why not.
    /// </summary>
    public (string? Id, string? Problem) Watch(string? sensor, string name, string url, string? group, bool follow, RuleForm own, List<string>? to, string by)
    {
        Sensor? old;
        lock (gate)
        {
            old = file.Read().Sensors.FirstOrDefault(s => s.Id == sensor && s.Kind == "web");
        }
        var form = old is null
            ? new SensorForm(name, "web", url, null, null, group, 60, follow ? "follow" : "custom", Custom: follow ? null : own, Channels: to)
            : FormOf(old) with { Name = name, Target = url, Group = group, Rule = follow ? "follow" : "custom", Custom = follow ? null : own, Channels = to };
        var (saved, problem) = Save(old?.Id, form, by);
        if (saved is not null)
        {
            lock (gate)
            {
                var f = file.Read();
                if (f.Sensors.FirstOrDefault(s => s.Id == saved.Id) is { Link: false } made)
                {
                    made.Link = true;
                    file.Save(f);
                }
            }
        }
        return (saved?.Id, problem);
    }

    // A sensor as a form that saves it as it is.
    private static SensorForm FormOf(Sensor s) => new(s.Name, s.Kind, s.Target, s.Port, s.Server, s.Group, s.Every, s.Rule, s.Expect, s.Text, s.Address, s.WarnDays, s.Login, null, s.Watch, s.Pin,
        s.Product, s.WarnFree, s.Value, s.Oid, s.Interface, s.Custom is { } c ? new RuleForm(c.After, c.Again, c.Back) : null, s.Seat, s.Service, s.Drive, s.Channels, s.DownAfter, s.CertDays, s.Invert,
        s.Timeout, s.Tags, s.DependsOn);

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
                return (null, $"That's 20 saved alert settings, which is as many as {Product.Title} keeps.");
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
    public async Task<(Probe? Probe, string? Problem)> Try(SensorForm form, CancellationToken stop, bool testing = false)
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
        if (testing && DevTools.IsTest(sensor.Target))
        {
            sensor.Expires = Now + DevTools.Lasts;
        }
        return (Flip(sensor, await probe.Check(sensor, stop)), null);
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
        await SendAlerts(stop);
        return true;
    }

    /// <summary>One round: every sensor that's due is checked, then whoever should hear about one is emailed. Run every few seconds.</summary>
    public async Task Tick(CancellationToken stop)
    {
        List<Sensor> due;
        lock (gate)
        {
            Sweep();
            var now = Now;
            var f = file.Read();
            // Timed pauses that are over.
            var over = f.Sensors.Where(s => s.Paused && s.PausedUntil is { } end && end <= now).ToList();
            over.ForEach(s => Start(s, true));
            if (over.Count > 0)
            {
                file.Save(f);
            }
            due = [.. f.Sensors.Where(s => !s.Paused && !busy.Contains(s.Id) && (soon.Contains(s.Id) || State(s.Id).Checked is not { } last || (now - last).TotalSeconds >= s.Every - 1)).Take(64)];
            due.ForEach(s =>
            {
                busy.Add(s.Id);
                soon.Remove(s.Id);
            });
        }
        await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = stop }, async (s, t) => await Check(s, t));
        await SendAlerts(stop);
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
        Record(s, Flip(s, result));
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
                st.Parts = result.Parts?.Where(p => s.Watch is null || s.Watch.Contains(p.Key)).ToList() is { Count: > 0 } parts ? parts : null;
                st.Fails = 0;
                st.FailedAt = null;
                if (before == "down")
                {
                    Event(st, now, "up", $"Came back after {Span(now - (st.Since ?? now))}");
                    // What depends on it and is down is checked again now: back too, or down on its own and alerting.
                    st.Back = now;
                    Behind(file.Read(), s.Id).Where(d => State(d.Id).Status == "down").ToList().ForEach(d => soon.Add(d.Id));
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
                st.Parts = null;
                // One lost reply isn't an outage unless the sensor says so, or the next check is an hour away.
                if (st.Status != "down" && st.Fails >= (s.DownAfter ?? (s.Every >= 3600 ? 1 : 2)))
                {
                    st.Status = "down";
                    st.Since = st.FailedAt;
                    Event(st, now, "down", st.Note);
                }
            }
            dirty = true;
        }
    }

    // For a sensor that alerts when something answers: an answer is the problem, no answer is how it should be.
    private static Probe Flip(Sensor s, Probe p) => !s.Invert ? p
        : p.Ok ? new Probe(false, Note: "It answers, and it shouldn't") : new Probe(true, 0, "Not answering, as it should be");

    private static void Event(SensorState st, DateTime at, string what, string text)
    {
        st.Events.Add(new SensorEvent(at, what, text));
        if (st.Events.Count > 100)
        {
            st.Events.RemoveRange(0, st.Events.Count - 100);
        }
    }

    // Who should hear about what, worked out under the lock and sent outside it, through the alert rules. A down sensor
    // waiting on one it depends on says nothing. With Gather on, a first down alert waits a little for others to join it.
    // In quiet hours what the rules hold back isn't marked as told, so it goes once they're over if it's still so.
    private async Task SendAlerts(CancellationToken stop)
    {
        var send = new List<(List<string> Ids, MailNote Note, string Key, List<string> To)>();
        var said = new Dictionary<string, string>(); // what each sensor's history says went
        lock (gate)
        {
            var f = file.Read();
            var now = Now;
            var byId = f.Sensors.ToDictionary(s => s.Id);
            var waits = f.Sensors.ToDictionary(s => s.Id, s => Waits(byId, s));
            var down = f.Sensors.Where(s => !s.Paused && State(s.Id).Status == "down" && waits[s.Id] is null).ToList();
            void Queue(Sensor s, MailNote note, string text, string key)
            {
                send.Add(([s.Id], note, key, To(s, key)));
                said[s.Id] = text;
                quiet.Remove($"{s.Id}:{key}");
            }
            // Held for quiet hours: written down once, and nothing marked, so it goes when they're over.
            bool Hold(Sensor s, string key, string title)
            {
                if (!alerts.Holds(key, now))
                {
                    return false;
                }
                if (quiet.Add($"{s.Id}:{key}"))
                {
                    alerts.Held(key, title, To(s, key));
                }
                return true;
            }
            foreach (var s in f.Sensors.Where(s => !s.Paused))
            {
                var st = State(s.Id);
                var rule = Alerting(f, s);
                var key = DownKey(s);
                if (waits[s.Id] is not null)
                {
                    continue;
                }
                if (st.Status != "down" && st.Status != "warn")
                {
                    quiet.RemoveWhere(k => k.StartsWith(s.Id + ":", StringComparison.Ordinal));
                }
                if (st.Status == "down" && rule.After >= 0 && To(s, key).Count > 0)
                {
                    var first = st.Alerted is null && (now - (st.Since ?? now)).TotalMinutes >= rule.After;
                    var again = st.Alerted is { } last && rule.Again > 0 && (now - last).TotalMinutes >= rule.Again;
                    if ((first || again) && Hold(s, key, $"{s.Name} is {(again ? "still " : "")}down"))
                    {
                        continue;
                    }
                    if (first && f.Gather)
                    {
                        held.TryAdd(s.Id, now);
                    }
                    else if (first || again)
                    {
                        Queue(s, DownNote(s, st, now, again, down, waits, byId), again ? "Sent again: still down" : "Sent: down", key);
                        st.Alerted = now;
                        st.Told = true;
                        dirty = true;
                    }
                }
                else if (st.Status == "warn")
                {
                    // A warning comes under its own rule: a certificate, space running low, or a part of a server.
                    var wkey = WarnKey(s, st);
                    var wrule = alerts.Rule(wkey);
                    var first = st.Warned is null;
                    var again = st.Warned is { } last && wrule.Again > 0 && (now - last).TotalMinutes >= wrule.Again;
                    if (!wrule.On || To(s, wkey).Count == 0 || !first && !again || Hold(s, wkey, $"{s.Name}: {st.Note}"))
                    {
                        continue;
                    }
                    Queue(s, new MailNote($"{s.Name}: {st.Note}", "warn", [("Sensor", What(s)), ("Group", s.Group ?? "No group")],
                        new AlertCard("warn", $"{s.Name}: {st.Note}", "It's still answering. This is a heads-up.", [new("🟡 Details", [What(s), Where(s)])], Product.Title)),
                        again ? "Sent again: still needs attention" : "Sent: warning", wkey);
                    st.Warned = now;
                    dirty = true;
                }
                else if (st.Status == "up" && (st.Told || st.Warned is not null))
                {
                    // Back: said only when a down alert went and the rule wants it. A warning that cleared just ends.
                    if (st.Alerted is not null && rule.Back && To(s, key).Count > 0)
                    {
                        if (Hold(s, key, $"{s.Name} is back up"))
                        {
                            continue;
                        }
                        var outage = st.Events.LastOrDefault(e => e.What == "up");
                        Queue(s, new MailNote($"{s.Name} is back up", "up",
                            [("Sensor", What(s)), ("Group", s.Group ?? "No group"), ("What happened", outage?.Text ?? "Came back"), ("Answering in", st.Ms is { } ms ? $"{ms} ms" : "time")],
                            new AlertCard("up", $"{s.Name} is back up", "It answered again at {when}.",
                                [new("🟢 Now", [st.Ms is { } took ? $"Answering in {took} ms" : "Answering", outage?.Text ?? "Came back"]), new("📍 Where", [What(s), Where(s)])],
                                Product.Title, st.Since ?? now)), "Sent: back up", key);
                    }
                    st.Told = false;
                    st.Alerted = null;
                    st.Warned = null;
                    dirty = true;
                }
            }
            // Gathered alerts go once the first has waited long enough (at once if Gather was turned off): the ones still down, together.
            if (held.Count > 0 && (!f.Gather || (now - held.Values.Min()).TotalSeconds >= f.GatherSecs))
            {
                var ready = held.Keys.Where(byId.ContainsKey).Select(id => byId[id]).Where(s => down.Contains(s) && To(s, DownKey(s)).Count > 0).ToList();
                held.Clear();
                foreach (var channel in ready.SelectMany(s => To(s, DownKey(s))).Distinct().ToList())
                {
                    var with = ready.Where(s => To(s, DownKey(s)).Contains(channel)).ToList();
                    send.Add(([.. with.Select(s => s.Id)], with.Count == 1 ? DownNote(with[0], State(with[0].Id), now, false, down, waits, byId) : Gathered(with, now, f, down, waits, byId),
                        DownKey(with[0]), [channel]));
                }
                foreach (var s in ready)
                {
                    var st = State(s.Id);
                    st.Alerted = now;
                    st.Told = true;
                    said[s.Id] = ready.Count == 1 ? "Sent: down" : $"Sent: down, in one alert with {ready.Count - 1} other{(ready.Count == 2 ? "" : "s")}";
                }
                dirty = true;
            }
        }
        var went = new Dictionary<string, List<string>>();
        var failed = new Dictionary<string, List<string>>();
        foreach (var (ids, note, key, to) in send)
        {
            var (ok, bad) = await alerts.Send(key, note, to, stop);
            foreach (var id in ids)
            {
                (went.TryGetValue(id, out var w) ? w : went[id] = []).AddRange(ok);
                (failed.TryGetValue(id, out var x) ? x : failed[id] = []).AddRange(bad.Select(b => $"Didn't go to {b}"));
            }
        }
        if (said.Count == 0)
        {
            return;
        }
        lock (gate)
        {
            foreach (var (id, text) in said)
            {
                var st = State(id);
                if (went.TryGetValue(id, out var to) && to.Count > 0)
                {
                    Event(st, Now, "email", $"{text} · {string.Join(", ", to)}");
                }
                failed.GetValueOrDefault(id)?.ForEach(t => Event(st, Now, "problem", t));
            }
            dirty = true;
        }
    }

    // The alert for one sensor that's down: what it is, since when, what it said, what else is down, and what waits on it.
    private static MailNote DownNote(Sensor s, SensorState st, DateTime now, bool again, List<Sensor> down, Dictionary<string, Sensor?> waits, Dictionary<string, Sensor> byId)
    {
        var behind = waits.Where(w => w.Value?.Id == s.Id).Select(w => byId[w.Key].Name).ToList();
        List<(string, string)> facts =
        [
            ("Sensor", What(s)), ("Group", s.Group ?? "No group"), ("Down since", $"{Local(st.Since ?? now)} ({Span(now - (st.Since ?? now))})"),
            ("What it said", st.Note ?? "No reply"), ("Also down", Few([.. down.Where(x => x.Id != s.Id).Select(x => x.Name)])),
        ];
        if (behind.Count > 0)
        {
            facts.Add(("Waiting on it", Few(behind)));
        }
        var since = st.Since ?? now;
        var others = down.Where(x => x.Id != s.Id).Select(x => x.Name).ToList();
        List<AlertSection> sections = [new("🔴 What happened", [st.Note ?? "No reply", What(s), $"Down for {Span(now - since)}{(again ? "" : " so far")}"])];
        if (behind.Count > 0)
        {
            sections.Add(new("⏳ Waiting on it", [$"{Few(behind)} {(behind.Count == 1 ? "waits" : "wait")} instead of alerting"]));
        }
        if (others.Count > 0)
        {
            sections.Add(new("🔻 Also down", [Few(others)]));
        }
        sections.Add(new("📍 Where", [Where(s)]));
        return new MailNote($"{s.Name} is {(again ? "still " : "")}down", "down", facts,
            new AlertCard("down", $"{s.Name} is {(again ? "still " : "")}down", again ? "Down since {when}." : "It stopped answering at {when}.", sections, Product.Title, since));
    }

    // Where a sensor sits, in a few words: Network · checked every minute.
    private static string Where(Sensor s) =>
        $"{s.Group ?? "No group"} · checked every {(s.Every switch { 30 => "30 seconds", 60 => "minute", 300 => "5 minutes", 3600 => "hour", _ => $"{s.Every} seconds" })}";

    // One alert for several that went down close together: what's down, what waits on each, when, and what's still up. Under the lock.
    private MailNote Gathered(List<Sensor> with, DateTime now, SensorsFile f, List<Sensor> down, Dictionary<string, Sensor?> waits, Dictionary<string, Sensor> byId)
    {
        List<(string, string)> facts = [("Down", Few([.. with.Select(s => s.Name)], 20))];
        foreach (var s in with)
        {
            var behind = waits.Where(w => w.Value?.Id == s.Id).Select(w => byId[w.Key].Name).ToList();
            if (behind.Count > 0)
            {
                facts.Add(($"Waiting on {s.Name}", Few(behind)));
            }
        }
        var times = with.Select(s => State(s.Id).Since ?? now).ToList();
        var spread = times.Max() - times.Min();
        facts.Add(("Since", $"{Local(times.Min())} ({(spread.TotalSeconds < 1 ? "all at once" : $"all within {Span(spread)}")})"));
        var others = down.Where(s => !with.Contains(s)).Select(s => s.Name).ToList();
        if (others.Count > 0)
        {
            facts.Add(("Also down", Few(others)));
        }
        var up = f.Sensors.Where(s => !s.Paused && State(s.Id).Status is "up" or "warn").Select(s => s.Name).ToList();
        facts.Add(("Still up", up.Count <= 8 ? Few(up) : $"{up.Count} others"));
        List<AlertSection> sections = [new("🔴 Down", [.. with.Take(15).Select(s => $"{s.Name}: {State(s.Id).Note ?? "no reply"}")])];
        foreach (var s in with)
        {
            var behind = waits.Where(w => w.Value?.Id == s.Id).Select(w => byId[w.Key].Name).ToList();
            if (behind.Count > 0)
            {
                sections.Add(new($"⏳ Waiting on {s.Name}", [Few(behind)]));
            }
        }
        if (others.Count > 0)
        {
            sections.Add(new("🔻 Also down", [Few(others)]));
        }
        sections.Add(new("🟢 Still up", [up.Count == 0 ? "Nothing else" : up.Count <= 8 ? Few(up) : $"{up.Count} others"]));
        return new MailNote($"{with.Count} sensors are down", "down", facts,
            new AlertCard("many", $"{with.Count} sensors are down", spread.TotalSeconds < 1 ? "All at {when}." : $"All within {Span(spread)} of {{when}}.", sections, Product.Title, times.Min()));
    }

    /// <summary>
    /// Sends a sensor's down alert, marked as a test, to the channels it alerts. Where it went and what didn't, or why it
    /// couldn't be sent at all. One every 10 seconds per sensor, so a double click doesn't send two.
    /// </summary>
    public async Task<(List<string> Went, List<string> Failed, string? Problem)> TestAlert(string id, string by, CancellationToken stop)
    {
        MailNote note;
        List<string> to;
        lock (gate)
        {
            if (file.Read().Sensors.FirstOrDefault(x => x.Id == id) is not { } s)
            {
                return ([], [], "That sensor isn't here any more.");
            }
            to = To(s, DownKey(s));
            if (to.Count == 0)
            {
                return ([], [], s.Rule == "follow" ? "Its alert rule has nowhere to send. Pick notifications for it in Settings, Alert rules." : "It has nowhere to send alerts. Change it and pick notifications under Send to.");
            }
            if (tested.TryGetValue(id, out var last) && (Now - last).TotalSeconds < 10)
            {
                return ([], [], "A test alert just went. Try again in a few seconds.");
            }
            tested[id] = Now;
            note = new MailNote($"Test: {s.Name} is down", "down", [("Sensor", What(s)), ("Group", s.Group ?? "No group"), ("This is a test", $"{by} sent it from the sensor's page. Nothing is down.")],
                new AlertCard("test", $"Test alert for {s.Name}", "Nothing is down. Its alerts come here, looking like this.", [new("📍 The sensor", [What(s), Where(s)])], $"{Product.Title} · sent by {by}"));
        }
        var went = new List<string>();
        var failed = new List<string>();
        foreach (var channel in to)
        {
            var problem = await channels.Send(channel, note, stop);
            var name = channels.Name(channel);
            if (problem is null)
            {
                went.Add(name);
            }
            else
            {
                failed.Add($"Didn't go to {name}: {problem}");
            }
        }
        lock (gate)
        {
            var st = State(id);
            if (went.Count > 0)
            {
                Event(st, Now, "email", $"Sent a test alert · {string.Join(", ", went)}");
            }
            failed.ForEach(t => Event(st, Now, "problem", t));
            dirty = true;
        }
        return (went, failed, null);
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
        app.MapPost("/api/sensors/try", async (SensorForm form, HttpContext context, Sensors sensors, SettingsStore settings) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsEdit) is { } refused)
            {
                return refused;
            }
            var (found, problem) = await sensors.Try(form, context.RequestAborted, DevTools.On(settings));
            return found is null ? Results.BadRequest(new { error = problem }) : Results.Ok(new { found.Ok, found.Ms, found.Note, found.Warn, found.Detail, found.Title, found.Parts, found.Pin });
        });

        app.MapPost("/api/sensors", (SensorForm form, HttpContext context, Sensors sensors, ActivityLog log, SettingsStore settings) => Save(null, form, context, sensors, log, settings));

        // The computers in Seats a sensor can check through, and whether their client is connected now.
        app.MapGet("/api/sensors/seats", (HttpContext context, Clients clients, ClientCommands commands) =>
            !Access.Can(context, Permissions.SensorsEdit) ? Access.NotAllowed()
            : Results.Ok(clients.All().Where(c => c.Approved is not null).OrderBy(c => c.Computer, StringComparer.OrdinalIgnoreCase)
                .Select(c => new { c.Id, c.Computer, connected = commands.Listening(c.Id) })));

        // The saved credentials, for the dialog: names and usernames, never passwords.
        app.MapGet("/api/sensors/logins", (HttpContext context, Sensors sensors, SensorLogins logins) =>
            !Access.Can(context, Permissions.SensorsEdit) ? Access.NotAllowed() : Results.Ok(logins.List(sensors.All())));

        app.MapPost("/api/sensors/{id}", (string id, SensorForm form, HttpContext context, Sensors sensors, ActivityLog log, SettingsStore settings) => Save(id, form, context, sensors, log, settings));

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
            if (form.Minutes is not (null or (>= 1 and <= 10080)))
            {
                return Results.BadRequest(new { error = "A pause is a week at most." });
            }
            if (sensors.Pause(id, form.Paused, form.Minutes, form.Dependents) is not var (sensor, also))
            {
                return Results.NotFound(new { error = "That sensor isn't here any more." });
            }
            var others = also == 0 ? "" : $" and the {also} that depend on it";
            log.Record(context, "sensors", form.Paused ? "sensor.pause" : "sensor.resume", form.Paused
                ? $"Paused the sensor {sensor.Name}{others}{(form.Minutes is { } m ? $" for {(m % 60 == 0 ? $"{m / 60} hour{(m == 60 ? "" : "s")}" : $"{m} minutes")}" : "")}"
                : $"Started the sensor {sensor.Name}{others} again");
            return Results.Ok(sensors.View());
        });

        // One sensor's alerts, or the ones new sensors start with, given to other sensors.
        app.MapPost("/api/sensors/copy", (CopyForm form, HttpContext context, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var (changed, problem) = sensors.CopyAlerts(new RuleForm(form.After, form.Again, form.Back), form.Channels, form.Ids);
            if (problem is not null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "sensor.copy", $"Changed the alerts on {changed} sensor{(changed == 1 ? "" : "s")}");
            return Results.Ok(new { changed, now = sensors.View() });
        });

        // The sensor's down alert, marked as a test, to the channels it alerts.
        app.MapPost("/api/sensors/{id}/test", async (string id, HttpContext context, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var (went, failed, problem) = await sensors.TestAlert(id, ActivityLog.Who(context), context.RequestAborted);
            if (problem is not null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "sensor.test", $"Sent a test alert for {sensors.All().FirstOrDefault(s => s.Id == id)?.Name ?? id}");
            return Results.Ok(new { went, failed });
        });

        // A sensor dragged to another group: only its group changes.
        app.MapPost("/api/sensors/{id}/move", (string id, MoveForm form, HttpContext context, Sensors sensors) =>
            Access.RefuseUnless(context, Permissions.SensorsEdit) ?? (sensors.MoveTo(id, form.Group) is { } problem
                ? (IResult) Results.BadRequest(new { error = problem }) : Results.Ok(sensors.View())));

        app.MapPost("/api/sensors/gather", (GatherForm form, HttpContext context, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            if (sensors.GatherAlerts(form.On, form.Secs) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "sensor.gather", form.On ? $"Turned on Gather alerts, within {(form.Secs < 60 ? $"{form.Secs} seconds" : $"{form.Secs / 60} minute{(form.Secs == 60 ? "" : "s")}")}" : "Turned off Gather alerts");
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

    private static async Task<IResult> Save(string? id, SensorForm form, HttpContext context, Sensors sensors, ActivityLog log, SettingsStore settings)
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
        var (sensor, problem) = sensors.Save(id, form, ActivityLog.Who(context), DevTools.On(settings));
        if (sensor is null)
        {
            return Results.BadRequest(new { error = problem });
        }
        log.Record(context, "sensors", id is null ? "sensor.add" : "sensor.change", id is null ? $"Added the sensor {sensor.Name}" : $"Changed the sensor {sensor.Name}");
        return Results.Ok(sensors.View());
    }
}

/// <summary>Pause or start again; Minutes for a timed pause; Dependents to take what depends on it along.</summary>
public sealed record PauseForm(bool Paused, int? Minutes = null, bool Dependents = false);

public sealed record MoveForm(string? Group);

public sealed record GatherForm(bool On, int? Secs);

/// <summary>Alert times, and channels when sent, for the sensors listed.</summary>
public sealed record CopyForm(int? After, int? Again, bool? Back, List<string>? Channels, List<string>? Ids);
