using System.DirectoryServices;
using System.DirectoryServices.ActiveDirectory;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RabbidLab.Setup;

namespace RabbidLab.Server;

/// <summary>An account typed to change DNS: used for that one change, never saved.</summary>
public sealed record DnsLogin(string Username, string Password);

/// <summary>Why DNS couldn't be read or changed, in plain words.</summary>
public sealed class DnsProblem(string message) : Exception(message);

/// <summary>Where a name leads now, as this computer looks it up: nowhere, here, or somewhere else (and where).</summary>
public enum NameLeads { Nowhere, Here, Elsewhere }

/// <summary>
/// The domain's DNS, where a console address gets its alias (CNAME): read, added to and taken out of on a domain
/// controller, as the account typed. The same rules as Setup's (DnsPlan): only ever a new alias, never a change to a
/// record that's there, never a computer's name.
/// </summary>
public interface IConsoleDns
{
    /// <summary>The Active Directory domain this computer is in, such as amw.com; null when it isn't in one.</summary>
    string? Domain { get; }

    /// <summary>This computer's full name, which an alias points at.</summary>
    string Target { get; }

    /// <summary>Where a name leads now, from this computer, and where to.</summary>
    (NameLeads Leads, string Where) Lookup(string name);

    /// <summary>What's in DNS around a name, read from the nearest domain controller that runs DNS.</summary>
    DnsFacts Read(string name, DnsLogin login);

    /// <summary>Adds the alias DnsPlan said may be added, and checks it's there.</summary>
    void Add(DnsFacts facts, string zone, DnsLogin login);

    /// <summary>Takes out an alias added here: only a CNAME for that name that still points at this computer. Says what it did.</summary>
    string Remove(string name, string zone, string server, string target, DnsLogin login);
}

/// <summary>DNS on a domain controller over WMI (root\MicrosoftDNS), as Setup does it, with the account typed.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsConsoleDns : IConsoleDns
{
    private readonly Lazy<string?> domain = new(() =>
    {
        try
        {
            using var d = System.DirectoryServices.ActiveDirectory.Domain.GetComputerDomain();
            return d.Name.ToLowerInvariant();
        }
        catch (Exception e) when (e is ActiveDirectoryObjectNotFoundException or ActiveDirectoryOperationException or COMException)
        {
            return null;
        }
    });

    public string? Domain => domain.Value;

    public string Target
    {
        get
        {
            var suffix = (IPGlobalProperties.GetIPGlobalProperties().DomainName ?? "").ToLowerInvariant();
            var name = Environment.MachineName.ToLowerInvariant();
            return suffix.Length == 0 ? name : $"{name}.{suffix}";
        }
    }

    public (NameLeads Leads, string Where) Lookup(string name)
    {
        Forget(name);
        try
        {
            var entry = Dns.GetHostEntry(name);
            var mine = new HashSet<string>(ConsoleNames.Ips()) { "127.0.0.1", "::1" };
            if (string.Equals(entry.HostName, Target, StringComparison.OrdinalIgnoreCase) || entry.AddressList.Any(a => mine.Contains(a.ToString())))
            {
                return (NameLeads.Here, entry.HostName);
            }
            var ip = entry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return (NameLeads.Elsewhere, ip?.ToString() ?? entry.HostName);
        }
        catch (Exception e) when (e is SocketException or ArgumentException)
        {
            return (NameLeads.Nowhere, "");
        }
    }

    public DnsFacts Read(string name, DnsLogin login) => Plainly(() => ReadFacts(name, login));

    public void Add(DnsFacts facts, string zone, DnsLogin login) => Plainly(() => AddAlias(facts, zone, login));

    public string Remove(string name, string zone, string server, string target, DnsLogin login) => Plainly(() => RemoveAlias(name, zone, server, target, login));

    // Windows' errors as a DnsProblem in plain words, the only kind the page sees.
    private static T Plainly<T>(Func<T> act)
    {
        try
        {
            return act();
        }
        catch (Exception e) when (e is COMException or ManagementException or UnauthorizedAccessException or ActiveDirectoryObjectNotFoundException or ActiveDirectoryOperationException)
        {
            throw new DnsProblem(Refused(e) ?? (e is ActiveDirectoryObjectNotFoundException ? "No domain controller answered." : $"The DNS server couldn't be reached: {e.Message.Trim().TrimEnd('.')}."));
        }
    }

    private static void Plainly(Action act) => Plainly(() =>
    {
        act();
        return true;
    });

    private DnsFacts ReadFacts(string name, DnsLogin login)
    {
        name = DnsPlan.Clean(name);
        var target = DnsPlan.Clean(Target);
        var skipped = new List<string>();
        foreach (var server in DnsServers())
        {
            ManagementScope scope;
            try
            {
                scope = Connect(server, login);
            }
            catch (ManagementException e) when (e.ErrorCode == ManagementStatus.InvalidNamespace)
            {
                skipped.Add($"{server} doesn't have the DNS Server role");
                continue;
            }
            catch (Exception e) when (Refused(e) is null && e is COMException or ManagementException)
            {
                skipped.Add($"{server} didn't answer");
                continue;
            }
            var facts = new DnsFacts
            {
                Name = name,
                Server = server.ToLowerInvariant(),
                Target = target,
                MyAddresses = ConsoleNames.Ips(),
                Zones = Zones(scope),
            };
            if (DnsPlan.ZoneFor(name, facts.Zones) is { } zone)
            {
                facts.Records = Records(scope, DnsPlan.Clean(zone.Name), name);
            }
            facts.TargetAddresses = DnsPlan.ZoneFor(target, facts.Zones) is { } targetZone
                ? [.. Records(scope, DnsPlan.Clean(targetZone.Name), target).Where(r => r.Type == "A").Select(r => r.Data.Trim())]
                : [.. Dns.GetHostAddresses(target).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString())];
            facts.ClashingComputer = ComputerNamed(name, target, login);
            return facts;
        }
        throw new DnsProblem(skipped.Count == 0 ? "No domain controller answered." : $"No DNS server could be asked: {string.Join(", ", skipped)}.");
    }

    private void AddAlias(DnsFacts facts, string zone, DnsLogin login)
    {
        var scope = Connect(facts.Server, login);
        using (var type = new ManagementClass(scope, new ManagementPath("MicrosoftDNS_CNAMEType"), null))
        {
            var data = type.GetMethodParameters("CreateInstanceFromPropertyData");
            data["DnsServerName"] = facts.Server;
            data["ContainerName"] = zone;
            data["OwnerName"] = facts.Name;
            data["PrimaryName"] = facts.Target + ".";
            type.InvokeMethod("CreateInstanceFromPropertyData", data, null);
        }
        if (!Records(scope, zone, facts.Name).Any(r => r.Type == "CNAME" && DnsPlan.Clean(r.Data) == facts.Target))
        {
            throw new DnsProblem($"{facts.Server} took the record for {facts.Name}, but it isn't in the {zone} zone. Check the zone on {facts.Server}.");
        }
        Forget(facts.Name);
    }

    private string RemoveAlias(string name, string zone, string server, string target, DnsLogin login)
    {
        name = DnsPlan.Clean(name);
        target = DnsPlan.Clean(target);
        Exception? last = null;
        foreach (var dns in new[] { server }.Concat(DnsServers().Where(s => !string.Equals(s, server, StringComparison.OrdinalIgnoreCase))))
        {
            ManagementScope scope;
            try
            {
                scope = Connect(dns, login);
            }
            catch (Exception e) when (Refused(e) is null && e is COMException or ManagementException)
            {
                last = e;
                continue;
            }
            var mine = RecordObjects(scope, zone, name).Where(r => PointsAt(r, target)).ToList();
            if (mine.Count == 0)
            {
                return Records(scope, zone, name).Count == 0 ? $"The DNS record for {name} was already gone." : $"The DNS record for {name} points somewhere else now, so it was left alone.";
            }
            mine.ForEach(r => r.Delete());
            Forget(name);
            return $"Took the alias {name} out of DNS on {dns}.";
        }
        throw new DnsProblem(last is null ? "No domain controller answered." : $"The DNS server couldn't be reached: {last.Message.Trim().TrimEnd('.')}.");
    }

    /// <summary>Why the account was refused, in plain words, or null when that isn't what went wrong.</summary>
    public static string? Refused(Exception e) =>
        e is COMException { HResult: unchecked((int) 0x8007052E) } ? "The username or password is wrong."
        : e is UnauthorizedAccessException || e is ManagementException { ErrorCode: ManagementStatus.AccessDenied } || e is COMException { HResult: unchecked((int) 0x80070005) }
            ? "That account was refused. It needs DNS admin rights, such as Domain Admins or DnsAdmins, and the password has to be right."
            : null;

    // This domain's domain controllers, the nearest first.
    private static List<string> DnsServers()
    {
        var servers = new List<string>();
        using var d = System.DirectoryServices.ActiveDirectory.Domain.GetComputerDomain();
        try
        {
            using var nearest = d.FindDomainController();
            servers.Add(nearest.Name);
        }
        catch (ActiveDirectoryObjectNotFoundException)
        {
            // None answered just now; they're all tried below.
        }
        foreach (DomainController dc in d.DomainControllers)
        {
            using (dc)
            {
                if (!servers.Contains(dc.Name, StringComparer.OrdinalIgnoreCase))
                {
                    servers.Add(dc.Name);
                }
            }
        }
        return servers;
    }

    // WMI on the DNS server, as the account typed. On this computer (Rabbid Lab on a domain controller) Windows doesn't
    // take an account for a local connection, so it's made as Rabbid Lab, which is an administrator there.
    private ManagementScope Connect(string server, DnsLogin login)
    {
        var here = string.Equals(server, Target, StringComparison.OrdinalIgnoreCase) || string.Equals(server.Split('.')[0], Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        var options = new ConnectionOptions
        {
            Impersonation = ImpersonationLevel.Impersonate,
            Authentication = AuthenticationLevel.PacketPrivacy,
            Timeout = TimeSpan.FromSeconds(30),
        };
        if (!here)
        {
            options.Username = login.Username;
            options.Password = login.Password;
        }
        var scope = new ManagementScope($@"\\{server}\root\MicrosoftDNS", options);
        scope.Connect();
        return scope;
    }

    private static List<DnsZoneInfo> Zones(ManagementScope scope)
    {
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name, ZoneType, Reverse FROM MicrosoftDNS_Zone"));
        return [.. searcher.Get().Cast<ManagementObject>().Select(z => new DnsZoneInfo
        {
            Name = (string?) z["Name"] ?? "",
            Primary = Convert.ToUInt32(z["ZoneType"] ?? 0u) == 1,
            Reverse = z["Reverse"] is true,
        })];
    }

    private static List<DnsRecordInfo> Records(ManagementScope scope, string zone, string name) =>
        [.. RecordObjects(scope, zone, name).Select(r => new DnsRecordInfo
        {
            Type = ((string?) r["__CLASS"] ?? "").Replace("MicrosoftDNS_", "").Replace("Type", "").ToUpperInvariant(),
            Data = (string?) r["RecordData"] ?? "",
        })];

    // Names are checked DNS names, so they're safe in the query.
    private static List<ManagementObject> RecordObjects(ManagementScope scope, string zone, string name)
    {
        if (!ConsoleNames.IsDnsName(zone) || !ConsoleNames.IsDnsName(name))
        {
            throw new DnsProblem($"{name} isn't a DNS name.");
        }
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM MicrosoftDNS_ResourceRecord WHERE ContainerName = '{zone}' AND OwnerName = '{name}'"));
        return [.. searcher.Get().Cast<ManagementObject>()];
    }

    private static bool PointsAt(ManagementObject record, string target) =>
        (string?) record["__CLASS"] == "MicrosoftDNS_CNAMEType" && DnsPlan.Clean((string?) record["RecordData"]) == DnsPlan.Clean(target);

    // Another computer in Active Directory with this name, whose own record would clash with the alias. This one doesn't count.
    private string? ComputerNamed(string name, string target, DnsLogin login)
    {
        var label = name.Split('.')[0];
        var inDomain = Domain is { } d && name == $"{label}.{d}";
        try
        {
            using var root = new System.DirectoryServices.DirectoryEntry($"LDAP://{Domain}", login.Username, login.Password);
            using var searcher = new DirectorySearcher(root, $"(&(objectCategory=computer)(|(dNSHostName={name}){(inDomain ? $"(sAMAccountName={label}$)" : "")}))", ["dNSHostName", "name"]) { SizeLimit = 5 };
            using var found = searcher.FindAll();
            foreach (SearchResult r in found)
            {
                var host = r.Properties["dNSHostName"].Count > 0 ? DnsPlan.Clean(r.Properties["dNSHostName"][0] as string) : "";
                if (host != target)
                {
                    return r.Properties["name"].Count > 0 ? (string?) r.Properties["name"][0] : name;
                }
            }
        }
        catch (COMException)
        {
            // Couldn't search the domain; DNS still shows any record that's there.
        }
        return null;
    }

    // Makes this computer forget what it remembered about the name, so the next lookup asks DNS again.
    private static void Forget(string name)
    {
        try
        {
            DnsFlushResolverCacheEntry(name);
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows: the remembered answer runs out by itself.
        }
    }

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCacheEntry_W", CharSet = CharSet.Unicode)]
    private static extern bool DnsFlushResolverCacheEntry(string name);
}

/// <summary>Off Windows (Docker): no domain, and nothing to change.</summary>
public sealed class NoConsoleDns : IConsoleDns
{
    public string? Domain => null;

    public string Target => Environment.MachineName.ToLowerInvariant();

    public (NameLeads Leads, string Where) Lookup(string name)
    {
        try
        {
            var ip = Dns.GetHostAddresses(name).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return ip is null ? (NameLeads.Nowhere, "") : ConsoleNames.Ips().Contains(ip.ToString()) ? (NameLeads.Here, ip.ToString()) : (NameLeads.Elsewhere, ip.ToString());
        }
        catch (Exception e) when (e is SocketException or ArgumentException)
        {
            return (NameLeads.Nowhere, "");
        }
    }

    public DnsFacts Read(string name, DnsLogin login) => throw new DnsProblem("This computer isn't in a domain.");

    public void Add(DnsFacts facts, string zone, DnsLogin login) => throw new DnsProblem("This computer isn't in a domain.");

    public string Remove(string name, string zone, string server, string target, DnsLogin login) => throw new DnsProblem("This computer isn't in a domain.");
}
