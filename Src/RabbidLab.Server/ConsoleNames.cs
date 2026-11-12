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
}

/// <summary>A name to add or remove; with Dns, its alias is added or taken out too, as the account typed.</summary>
public sealed record NameForm(string? Name, bool Dns = false, string? Username = null, string? Password = null);

/// <summary>One thing adding or removing a name did, for the dialog: done, or something to know.</summary>
public sealed record NameStep(bool Ok, string Title, string Text);

/// <summary>
/// The certificate for the added names. The console's own certificate never changes, because computers in Seats pin
/// it: an added name gets one of its own, which the console shows only to a browser that asks for that name (SNI).
/// </summary>
public interface INamesCertificate
{
    /// <summary>Whether the console is served over HTTPS, so a name needs a certificate.</summary>
    bool Https { get; }

    /// <summary>The certificate for the added names, or null when there's none.</summary>
    X509Certificate2? Current { get; }

    /// <summary>Makes the certificate for these names, in place of the last one; with no names it's taken away. Null for none.</summary>
    X509Certificate2? Make(IReadOnlyList<string> names);
}

/// <summary>The added names' certificate in the computer's store, trusted on this computer, and kept for the console to serve.</summary>
[SupportedOSPlatform("windows")]
public sealed class StoreNamesCertificate(IConfiguration config) : INamesCertificate
{
    // Not "Rabbid Lab" or "RabbidLab-Dev": the console finds its own certificate by a subject containing that.
    private const string Subject = "CN=Console addresses";
    private static string Friendly => $"{Product.Name} console addresses";
    private static readonly Lock Gate = new();
    private static X509Certificate2? current;
    private static HashSet<string> covers = new(StringComparer.OrdinalIgnoreCase);
    private static bool read;

    /// <summary>The certificate to show a browser that asked for this name, or null for the console's own.</summary>
    public static X509Certificate2? For(string? name)
    {
        lock (Gate)
        {
            Load();
            return name is not null && covers.Contains(name.TrimEnd('.')) ? current : null;
        }
    }

    public bool Https => ClientFiles.Certificate(config) is not null;

    public X509Certificate2? Current
    {
        get
        {
            lock (Gate)
            {
                Load();
                return current;
            }
        }
    }

    public X509Certificate2? Make(IReadOnlyList<string> names)
    {
        lock (Gate)
        {
            using var my = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            using var root = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            my.Open(OpenFlags.ReadWrite);
            root.Open(OpenFlags.ReadWrite);
            var old = my.Certificates.Where(x => x.FriendlyName == Friendly).ToList();
            X509Certificate2? made = null;
            if (names.Count > 0)
            {
                using var rsa = RSA.Create(2048);
                var request = new CertificateRequest(Subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var san = new SubjectAlternativeNameBuilder();
                foreach (var name in names)
                {
                    san.AddDnsName(name);
                }
                request.CertificateExtensions.Add(san.Build());
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                using var self = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));
                // Into the computer's store with its private key, which only administrators and SYSTEM can read.
                made = X509CertificateLoader.LoadPkcs12(self.Export(X509ContentType.Pfx, ""), "", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
                made.FriendlyName = Friendly;
                my.Add(made);
                root.Add(X509CertificateLoader.LoadCertificate(self.RawData));
            }
            foreach (var gone in old)
            {
                foreach (var trusted in root.Certificates.Find(X509FindType.FindByThumbprint, gone.Thumbprint, false))
                {
                    root.Remove(trusted);
                }
                DeletePrivateKey(gone);
                my.Remove(gone);
            }
            current = made;
            covers = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            read = true;
            return made;
        }
    }

    // The one kept from before, once: the newest with its private key.
    private static void Load()
    {
        if (read)
        {
            return;
        }
        read = true;
        try
        {
            using var my = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            my.Open(OpenFlags.ReadOnly);
            current = my.Certificates.Where(x => x.FriendlyName == Friendly && x.HasPrivateKey && x.NotAfter > DateTime.Now).OrderByDescending(x => x.NotAfter).FirstOrDefault();
            covers = new HashSet<string>(current?.Extensions.OfType<X509SubjectAlternativeNameExtension>().SelectMany(e => e.EnumerateDnsNames()) ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (CryptographicException)
        {
            current = null;
        }
    }

    private static void DeletePrivateKey(X509Certificate2 cert)
    {
        try
        {
            using var key = cert.GetRSAPrivateKey();
            if (key is RSACng cng)
            {
                cng.Key.Delete();
            }
        }
        catch (CryptographicException)
        {
            // Already gone, or not readable: taking the certificate out of the store still stops it being used.
        }
    }
}

/// <summary>Off Windows (Docker): no certificate store, so no certificate for added names.</summary>
public sealed class NoNamesCertificate : INamesCertificate
{
    public bool Https => false;

    public X509Certificate2? Current => null;

    public X509Certificate2? Make(IReadOnlyList<string> names) => null;
}

/// <summary>
/// Settings, Console address: names of your own to open the console by. Adding one can add its alias (CNAME) to the
/// domain's DNS, as an account typed for that, by Setup's rules (DnsPlan), and makes the names a certificate of their own.
/// Removing one takes its alias out again, only if Rabbid Lab added it, and only while it still points here.
/// </summary>
public sealed class ConsoleNames(DataFolder folder, IConsoleDns dns, INamesCertificate certificate, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<ConsoleNamesFile> file = folder.Json<ConsoleNamesFile>("console-names.json", Json);

    private Lock gate => file.Gate;

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
        var mine = new[] { Environment.MachineName, dns.Target, "localhost", ConsoleAddress.ExtraName() ?? "" };
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
        steps.Add(Certify(names, full));
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
            return (null, null, "That address isn't here any more.");
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
        if (certificate.Https && (certificate.Current is not null || names.Count > 0))
        {
            steps.Add(Certify(names, null));
        }
        return (steps, n.Full, null);
    }

    // Every added name, short and full, which the names' certificate covers.
    private static List<string> Covered(ConsoleNamesFile f) => [.. f.Names.SelectMany(n => new[] { n.Name, n.Full }).Distinct(StringComparer.OrdinalIgnoreCase)];

    // The certificate made again for the names now; a step saying so, or what went wrong.
    private NameStep Certify(List<string> names, string? added)
    {
        if (!certificate.Https)
        {
            return new(true, "No certificate needed", "The console is served over HTTP.");
        }
        try
        {
            var made = certificate.Make(names);
            return made is null
                ? new(true, "Certificate taken away", "No added names are left, so their certificate went too. The console's own certificate didn't change.")
                : new(true, added is null ? "Certificate made again" : "Certificate made", $"For {string.Join(", ", names)} · expires {made.NotAfter:MMM d, yyyy}. The console's own certificate, which computers in Seats trust, didn't change.");
        }
        catch (Exception e) when (e is CryptographicException or UnauthorizedAccessException)
        {
            return new(false, "No certificate for it", $"The certificate couldn't be made ({e.Message.Trim().TrimEnd('.')}), so browsers warn about the name.");
        }
    }
}

public static class ConsoleNamesApi
{
    /// <summary>
    /// The console shows a browser that asks for an added name (SNI) the names' own certificate, and everyone else its
    /// own certificate, which Seats pin. Only for the Console endpoint Setup writes in console.json.
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
    }
}
