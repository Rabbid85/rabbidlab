using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using RabbidLab.Setup;

namespace RabbidLab.Server;

/// <summary>A name added in Settings, Console address, to open the console by: as typed (lab), in full (lab.amw.com), and its DNS alias when Rabbid Lab added one.</summary>
public sealed class ConsoleName
{
    public string Name { get; set; } = "";
    public string Full { get; set; } = "";

    /// <summary>Where Rabbid Lab added the alias (CNAME), so removing the name can take it out: the zone, the DNS server, and the name it points at. Null when it didn't add one.</summary>
    public string? Zone { get; set; }
    public string? Server { get; set; }
    public string? Target { get; set; }
    public DateTime Added { get; set; }
    public string? AddedBy { get; set; }
}

public sealed class ConsoleNamesFile
{
    public List<ConsoleName> Names { get; set; } = [];

    /// <summary>This computer's own addresses (its name, full name or an IP) taken off the list. The console still answers there.</summary>
    public List<string> Hidden { get; set; } = [];
}

/// <summary>A name to add or remove; with Dns, its alias is added or taken out too, as the account typed.</summary>
public sealed record NameForm(string? Name, bool Dns = false, string? Username = null, string? Password = null);

/// <summary>One thing adding or removing a name did, for the dialog: done, or something to know.</summary>
public sealed record NameStep(bool Ok, string Title, string Text);

/// <summary>
/// Settings, Console address: names of your own to open the console by. Adding one can add its alias (CNAME) to the
/// domain's DNS, as an account typed for that, by Setup's rules (DnsPlan), and makes the names a certificate of their own.
/// Removing one takes its alias out again, only if Rabbid Lab added it, and only while it still points here.
/// </summary>
public sealed class ConsoleNames(DataFolder folder, IConsoleDns dns, INamesCertificate certificate, TimeProvider clock, IInstallName install) : IDisposable
{
    private Timer? daily;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<ConsoleNamesFile> file = folder.Json<ConsoleNamesFile>("console-names.json", Json);

    private Lock gate => file.Gate;

    /// <summary>A name was added or removed.</summary>
    public event Action? Changed;

    /// <summary>Setup's host name and its alias, which Remove can take away too.</summary>
    public IInstallName Install => install;

    /// <summary>This computer's own addresses taken off the list, lower case.</summary>
    public IReadOnlyList<string> Hidden()
    {
        lock (gate)
        {
            return [.. file.Read().Hidden];
        }
    }

    /// <summary>Takes one of this computer's own addresses off the list, or with show false puts every one back.</summary>
    public void Hide(string? address, bool show = false)
    {
        var host = (address ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        lock (gate)
        {
            var f = file.Read();
            if (show)
            {
                f.Hidden.Clear();
            }
            else if (host.Length > 0 && !f.Hidden.Contains(host))
            {
                f.Hidden.Add(host);
            }
            file.Save(f);
        }
    }

    /// <summary>Setup's host name: its alias out of DNS when asked and Setup added it, then forgotten, so it stops opening the console.</summary>
    private (List<NameStep>? Steps, string? Full, string? Problem) RemoveSetupName(NameForm form, string name)
    {
        var steps = new List<NameStep>();
        var alias = Install.Alias();
        var full = alias?.Name ?? (name.Contains('.') || dns.Domain is null ? name : $"{name}.{dns.Domain}");
        if (alias is { } a)
        {
            if (!form.Dns)
            {
                steps.Add(new(false, "Left in DNS", $"The alias {a.Name} is still on {Short(a.Server)}. Take it out there if nothing else uses it."));
            }
            else if (Login(form) is not { } login)
            {
                return (null, null, "Type the username and password of an account that can change DNS, or leave the alias in DNS.");
            }
            else
            {
                try
                {
                    steps.Add(new(true, "Out of DNS", dns.Remove(a.Name, a.Zone, a.Server, a.Target, login)));
                    Install.ForgetAlias();
                }
                catch (DnsProblem e)
                {
                    return (null, null, e.Message);
                }
            }
        }
        Install.Forget();
        steps.Add(new(true, "Taken off this console", "Setup's certificate still lists it, which does no harm."));
        Changed?.Invoke();
        return (steps, full, null);
    }

    public IReadOnlyList<ConsoleName> All()
    {
        lock (gate)
        {
            return [.. file.Read().Names];
        }
    }

    /// <summary>The domain this computer is in, for the dialog: null when there's none, so DNS is up to you.</summary>
    public string? Domain => dns.Domain;

    /// <summary>A name as DNS has it: letters, digits and hyphens between the dots.</summary>
    public static bool IsDnsName(string name) =>
        name.Length is > 0 and <= 253 && name.Split('.').All(part => part.Length is > 0 and <= 63
            && part.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-') && part[0] != '-' && part[^1] != '-');

    /// <summary>This computer's IPv4 addresses on network cards that are up, as Setup lists them.</summary>
    public static List<string> Ips()
    {
        try
        {
            return [.. NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(a => a.ToString())
                .Distinct()];
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>A name in full: lab in the domain amw.com is lab.amw.com; one with dots, or with no domain, is as typed.</summary>
    public string Full(string name) => name.Contains('.') || dns.Domain is null ? name : $"{name}.{dns.Domain}";

    private static string Clean(string? typed) => DnsPlan.Clean(typed);

    // Why a name can't be added, or null.
    private string? Problem(string name)
    {
        if (!IsDnsName(name))
        {
            return "Type a name of letters, numbers and hyphens, such as lab or monitor.";
        }
        var full = Full(name);
        var mine = new[] { Environment.MachineName, dns.Target, "localhost", install.Name() ?? "" };
        if (mine.Any(m => m.Length > 0 && (string.Equals(m, name, StringComparison.OrdinalIgnoreCase) || string.Equals(m, full, StringComparison.OrdinalIgnoreCase))))
        {
            return "That's one of this computer's own names. It's in the list already.";
        }
        lock (gate)
        {
            return file.Read().Names.Any(n => n.Name == name || n.Full == full || n.Full == name) ? $"{name} is in the list already." : null;
        }
    }

    // Setup's words for what it found, with this console's name in them.
    private static string Ours(string message) => message.Replace("Setup does not modify", $"{Product.Title} doesn't change").Replace("Setup", Product.Title);

    private static DnsLogin? Login(NameForm form) =>
        string.IsNullOrWhiteSpace(form.Username) || string.IsNullOrEmpty(form.Password) ? null : new DnsLogin(form.Username.Trim(), form.Password);

    private static string Short(string server) => server.Split('.')[0].ToUpperInvariant();

    /// <summary>As the name is typed: its full name, whether it can be added at all, and where it leads now from this computer.</summary>
    public object Look(string? typed)
    {
        var name = Clean(typed);
        var full = name.Length == 0 ? "" : Full(name);
        if (Problem(name) is { } problem)
        {
            return new { full, problem };
        }
        var (leads, where) = dns.Lookup(full);
        return new { full, leads = leads.ToString().ToLowerInvariant(), where };
    }

    /// <summary>What DNS has around the name, read with the account typed: whether its alias can be added, in a sentence.</summary>
    public (bool Ok, string Message) Check(NameForm form)
    {
        var name = Clean(form.Name);
        if (Problem(name) is { } problem)
        {
            return (false, problem);
        }
        if (dns.Domain is null)
        {
            return (false, "This computer isn't in a domain, so the name has to be added on your DNS server.");
        }
        if (Login(form) is not { } login)
        {
            return (false, "Type the username and password of an account that can change DNS.");
        }
        try
        {
            var verdict = DnsPlan.Decide(dns.Read(Full(name), login), out _, out var message);
            return (verdict is DnsVerdict.CanAdd or DnsVerdict.AlreadyHere, Ours(message));
        }
        catch (DnsProblem e)
        {
            return (false, e.Message);
        }
    }

    /// <summary>Adds a name: its alias in DNS when asked, then the certificate, then whether it answers. What it did, or why not.</summary>
    public (List<NameStep>? Steps, string? Full, string? Problem) Add(NameForm form, string by)
    {
        var name = Clean(form.Name);
        if (Problem(name) is { } problem)
        {
            return (null, null, problem);
        }
        var full = Full(name);
        var steps = new List<NameStep>();
        var added = new ConsoleName { Name = name, Full = full, Added = clock.GetUtcNow().UtcDateTime, AddedBy = by };
        if (form.Dns)
        {
            if (dns.Domain is null)
            {
                return (null, null, "This computer isn't in a domain, so the name has to be added on your DNS server.");
            }
            if (Login(form) is not { } login)
            {
                return (null, null, "Type the username and password of an account that can change DNS.");
            }
            try
            {
                var facts = dns.Read(full, login);
                var verdict = DnsPlan.Decide(facts, out var zone, out var message);
                if (verdict == DnsVerdict.AlreadyHere)
                {
                    steps.Add(new(true, "Already in DNS", Ours(message)));
                }
                else if (verdict == DnsVerdict.CanAdd)
                {
                    dns.Add(facts, zone, login);
                    (added.Zone, added.Server, added.Target) = (zone, facts.Server, facts.Target);
                    steps.Add(new(true, "Alias added in DNS", $"{full} → {facts.Target}, in the {zone} zone on {Short(facts.Server)}"));
                }
                else
                {
                    return (null, null, Ours(message));
                }
            }
            catch (DnsProblem e)
            {
                return (null, null, e.Message);
            }
        }
        List<string> names;
        lock (gate)
        {
            var f = file.Read();
            f.Names.Add(added);
            file.Save(f);
            names = Covered(f);
        }
        Changed?.Invoke();
        steps.AddRange(Certify(names));
        var (leads, where) = dns.Lookup(full);
        steps.Add(leads switch
        {
            NameLeads.Here => new(true, $"{full} answers", "It leads to this computer, from here."),
            NameLeads.Elsewhere => new(false, $"{full} leads somewhere else", $"From this computer it goes to {where}. Check the record on your DNS server."),
            _ => new(false, $"{full} doesn't answer yet", form.Dns
                ? "The other domain controllers get the alias within a few minutes, and this computer asks again after that."
                : "Add it on your DNS server, such as your router or Pi-hole. Check now on this page shows when it works."),
        });
        return (steps, full, null);
    }

    /// <summary>Removes a name: its alias out of DNS when asked and Rabbid Lab added it, then the certificate made again without it.</summary>
    public (List<NameStep>? Steps, string? Full, string? Problem) Remove(NameForm form)
    {
        var name = Clean(form.Name);
        ConsoleName? n;
        lock (gate)
        {
            n = file.Read().Names.FirstOrDefault(x => x.Name == name);
        }
        if (n is null)
        {
            return Install.Name() == name && name.Length > 0 ? RemoveSetupName(form, name) : (null, null, "That address isn't here any more.");
        }
        var steps = new List<NameStep>();
        if (n.Zone is not null && n.Server is not null && n.Target is not null)
        {
            if (!form.Dns)
            {
                steps.Add(new(false, "Left in DNS", $"The alias {n.Full} is still on {Short(n.Server)}. Take it out there if nothing else uses it."));
            }
            else if (Login(form) is not { } login)
            {
                return (null, null, "Type the username and password of an account that can change DNS, or leave the alias in DNS.");
            }
            else
            {
                try
                {
                    steps.Add(new(true, "Out of DNS", dns.Remove(n.Full, n.Zone, n.Server, n.Target, login)));
                }
                catch (DnsProblem e)
                {
                    return (null, null, e.Message);
                }
            }
        }
        List<string> names;
        lock (gate)
        {
            var f = file.Read();
            f.Names.RemoveAll(x => x.Name == name);
            file.Save(f);
            names = Covered(f);
        }
        Changed?.Invoke();
        if (certificate.Https && (certificate.Now is { Signed: not null } or { Local: not null } || names.Count > 0))
        {
            steps.AddRange(Certify(names));
        }
        return (steps, n.Full, null);
    }

    // Every added name, short and full, which the names' certificate covers.
    private static List<string> Covered(ConsoleNamesFile f) => [.. f.Names.SelectMany(n => new[] { n.Name, n.Full }).Distinct(StringComparer.OrdinalIgnoreCase)];

    // The certificates made again for the names now; a step for each saying so, or what went wrong.
    private List<NameStep> Certify(List<string> names)
    {
        if (!certificate.Https)
        {
            return [new(true, "No certificate needed", "The console is served over HTTP.")];
        }
        try
        {
            var made = certificate.Make(names);
            var steps = new List<NameStep>();
            if (made.Signed is not null)
            {
                steps.Add(new(true, "Certificate signed", $"For {string.Join(", ", made.SignedNames)}, by this console's certificate authority. Computers that trust the authority open it without a warning."));
            }
            if (made.Local is not null)
            {
                steps.Add(new(true, made.Signed is null ? "Certificate made" : "Certificate for the short name", $"For {string.Join(", ", made.LocalNames)} · self-signed, trusted on this computer."));
            }
            if (steps.Count == 0)
            {
                steps.Add(new(true, "Certificate taken away", "No added names are left, so their certificate went too."));
            }
            return steps;
        }
        catch (Exception e) when (e is CryptographicException or UnauthorizedAccessException or IOException)
        {
            return [new(false, "No certificate for it", $"The certificate couldn't be made ({e.Message.Trim().TrimEnd('.')}), so browsers warn about the name.")];
        }
    }

    /// <summary>
    /// At the start, then once a day: the names' certificates made again when they're missing, close to expiring, or
    /// from before 1.1.150 (one self-signed certificate with a key of its own).
    /// </summary>
    public void RenewDaily(ILogger log)
    {
        Renew(log);
        daily ??= new Timer(_ => Renew(log), null, TimeSpan.FromDays(1), TimeSpan.FromDays(1));
    }

    private void Renew(ILogger log)
    {
        try
        {
            List<string> names;
            lock (gate)
            {
                names = Covered(file.Read());
            }
            if (certificate.Https && !certificate.Good(names))
            {
                certificate.Make(names);
                log.LogInformation("Made the certificates for the console addresses again ({Names})", string.Join(", ", names));
            }
        }
        catch (Exception e)
        {
            // Whatever it is, the console still starts: the names are answered with the console's own certificate until this works.
            log.LogWarning(e, "The certificates for the console addresses could not be made again");
        }
    }

    public void Dispose() => daily?.Dispose();
}

public static class ConsoleNamesApi
{
    /// <summary>
    /// The console shows a browser that asks for an added name (SNI) the names' own certificate, and everyone else its
    /// own certificate, which computers in Devices pin. Only for the Console endpoint Setup writes in console.json.
    /// </summary>
    public static void ServeNames(this IWebHostBuilder host, Func<string?, X509Certificate2?> forName) =>
        host.ConfigureKestrel((context, kestrel) => kestrel.Configure(context.Configuration.GetSection("Kestrel")).Endpoint("Console", endpoint =>
        {
            if (endpoint.IsHttps && endpoint.HttpsOptions.ServerCertificate is { } own)
            {
                endpoint.HttpsOptions.ServerCertificateSelector = (_, name) => forName(name) ?? own;
            }
        }));

    public static void MapConsoleNamesApi(this WebApplication app)
    {
        app.MapPost("/api/console/names/look", (NameForm form, HttpContext context, ConsoleNames names) =>
            Access.RefuseUnless(context, Permissions.ConsoleAddresses) ?? Results.Ok(names.Look(form.Name)));

        app.MapPost("/api/console/names/check", (NameForm form, HttpContext context, ConsoleNames names) =>
        {
            if (Access.RefuseUnless(context, Permissions.ConsoleAddresses) is { } refused)
            {
                return refused;
            }
            var (ok, message) = names.Check(form);
            return Results.Ok(new { ok, message });
        });

        app.MapPost("/api/console/names", (NameForm form, HttpContext context, ConsoleNames names, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.ConsoleAddresses) is { } refused)
            {
                return refused;
            }
            var (steps, full, problem) = names.Add(form, ActivityLog.Who(context));
            if (steps is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "settings", "console.name.add", $"Added the console address {full}{(steps.Any(s => s.Title == "Alias added in DNS") ? ", with its alias in DNS" : "")}");
            return Results.Ok(new { full, steps });
        });

        app.MapPost("/api/console/names/remove", (NameForm form, HttpContext context, ConsoleNames names, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.ConsoleAddresses) is { } refused)
            {
                return refused;
            }
            var (steps, full, problem) = names.Remove(form);
            if (steps is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "settings", "console.name.remove", $"Removed the console address {full}{(steps.Any(s => s.Title == "Out of DNS") ? " and its alias in DNS" : "")}");
            return Results.Ok(new { full, steps });
        });

        // This computer's own name, full name or an IP taken off the list (the console still answers there), or all put back.
        app.MapPost("/api/console/names/hide", (NameForm form, HttpContext context, ConsoleNames names, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.ConsoleAddresses) is { } refused)
            {
                return refused;
            }
            names.Hide(form.Name);
            log.Record(context, "settings", "console.name.hide", $"Took {form.Name} off the console's address list");
            return Results.Ok(new { });
        });

        app.MapPost("/api/console/names/show", (HttpContext context, ConsoleNames names, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.ConsoleAddresses) is { } refused)
            {
                return refused;
            }
            names.Hide(null, show: true);
            log.Record(context, "settings", "console.name.show", "Put this computer's addresses back on the console's address list");
            return Results.Ok(new { });
        });
    }
}
