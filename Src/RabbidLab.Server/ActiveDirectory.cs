using System.Net.NetworkInformation;

namespace RabbidLab.Server;

/// <summary>Someone Active Directory signed in: their username, display name, and which of the groups asked about they're in.</summary>
public sealed record DirectoryUser(string Username, string Name, IReadOnlyList<string> Groups);

/// <summary>A security group ("group") or a person ("user") found in the domain.</summary>
public sealed record DirectoryEntry(string Kind, string Name, string? Display, string? Description);

/// <summary>What a search found, and whether there was more than was sent.</summary>
public sealed record DirectoryList(List<DirectoryEntry> Entries, bool More);

/// <summary>Active Directory couldn't be used: unreachable, or refused the connection.</summary>
public sealed class DirectoryProblem(string message) : Exception(message);

public interface IDirectory
{
    /// <summary>
    /// The user, when the password is theirs; null when it isn't. DirectoryProblem when the domain can't be used. With it,
    /// which of these groups they're in, nested groups and the primary group counting. Groups that aren't there are skipped.
    /// </summary>
    Task<DirectoryUser?> SignIn(string domain, string host, IReadOnlyCollection<string> groups, string username, string password, CancellationToken stop);

    /// <summary>
    /// Security groups and people whose names match the search; without a search, just the groups. Read as this computer
    /// (Windows) or anonymously (elsewhere). Null when the domain won't say.
    /// </summary>
    Task<DirectoryList?> Search(string domain, string host, string? search, CancellationToken stop);

    /// <summary>
    /// The domains whose accounts may sign in: this one first, then the others in its forest and the domains it trusts.
    /// Read as this computer (Windows) or anonymously (elsewhere); when the domain won't say, just the domain itself.
    /// </summary>
    Task<IReadOnlyList<string>> Domains(string domain, CancellationToken stop);
}

/// <summary>
/// Active Directory for signing in, over the same connections as the user tools (Ldap.cs). On Windows that's Kerberos or
/// NTLM, signed and sealed, so the password never crosses the network and no certificate is needed. In Docker and on
/// Linux it's LDAPS, StartTLS, or plain LDAP when the domain has neither. Group membership comes from tokenGroups, so
/// nested groups and the primary group count.
/// </summary>
public sealed class ActiveDirectory : IDirectory
{
    private const int Limit = 500;

    // The other domains in the forest, and the Active Directory domains this one trusts (one way or both).
    private const string ForestFilter = "(&(objectClass=crossRef)(systemFlags:1.2.840.113556.1.4.803:=2))";
    private const string TrustFilter = "(&(objectClass=trustedDomain)(trustType=2)(trustDirection:1.2.840.113556.1.4.803:=2))";

    /// <summary>The domain this computer is in, or null when it isn't in one.</summary>
    public static string? ThisDomain()
    {
        try
        {
            var domain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            return string.IsNullOrWhiteSpace(domain) || !domain.Contains('.') ? null : domain.ToLowerInvariant();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    public async Task<DirectoryUser?> SignIn(string domain, string host, IReadOnlyCollection<string> groups, string username, string password, CancellationToken stop)
    {
        var name = username.Trim();
        // An empty password is an anonymous sign-in to most directories, so it never gets that far.
        if (password.Length == 0 || name.Length == 0)
        {
            return null;
        }
        var wanted = groups.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        using var ldap = await Ldap.Open(domain, host, name, password, stop);
        if (ldap is null)
        {
            return null;
        }
        try
        {
            var user = await Ldap.Bound(ldap, name, ["sAMAccountName", "displayName"], stop)
                ?? throw new DirectoryProblem($"{Ldap.Account(name)} signed in, but wasn't found in {domain}.");
            var sam = user.Text("sAMAccountName") ?? Ldap.Account(name);
            List<string> member = [];
            if (wanted.Count > 0)
            {
                // tokenGroups is worked out on request, so it's read from the user's own entry: nested groups and the primary group count.
                var tokens = (await ldap.Find(user.Dn, "(objectClass=*)", false, ["tokenGroups"], 1, stop)).FirstOrDefault()?.AllBytes("tokenGroups").ToList() ?? [];
                foreach (var g in await ldap.Find(ldap.BaseDn, GroupsFilter(wanted), true, ["objectSid", "cn", "sAMAccountName"], 1000, stop))
                {
                    if (g.Bytes("objectSid") is { } sid && tokens.Any(t => t.AsSpan().SequenceEqual(sid)))
                    {
                        member.AddRange(Named(wanted, g.Text("cn"), g.Text("sAMAccountName")));
                    }
                }
            }
            return new DirectoryUser(sam, user.Text("displayName") ?? sam, [.. member.Distinct(StringComparer.OrdinalIgnoreCase)]);
        }
        catch (LdapRefused e)
        {
            throw new DirectoryProblem($"{domain} couldn't be searched: {e.Message}.");
        }
    }

    public async Task<DirectoryList?> Search(string domain, string host, string? search, CancellationToken stop)
    {
        const string groups = "(objectCategory=group)(groupType:1.2.840.113556.1.4.803:=2147483648)";
        var s = Escape(search?.Trim() ?? "");
        // Enabled people only: bit 2 of userAccountControl is a disabled account.
        var filter = s.Length == 0 ? $"(&{groups})"
            : $"(|(&{groups}(|(cn=*{s}*)(sAMAccountName=*{s}*)))"
              + $"(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(|(cn=*{s}*)(sAMAccountName=*{s}*)(displayName=*{s}*))))";
        using var ldap = await Ldap.OpenAnonymous(domain, host, stop);
        if (ldap is null)
        {
            return null;
        }
        List<LdapRow> rows;
        try
        {
            rows = await ldap.Find(ldap.BaseDn, filter, true, ["objectClass", "sAMAccountName", "cn", "displayName", "description"], Limit, stop);
        }
        catch (LdapRefused)
        {
            return null; // signed in as this computer or anonymously, but not allowed to read the directory (AD's default for anonymous)
        }
        var entries = rows.Select(r =>
        {
            var sam = r.Text("sAMAccountName");
            // A group goes by its readable name (some sAMAccountNames are generated, like $751000-...); a person by their username.
            return r.Texts("objectClass").Contains("group", StringComparer.OrdinalIgnoreCase)
                ? new DirectoryEntry("group", r.Text("cn") ?? sam ?? "", null, r.Text("description"))
                : new DirectoryEntry("user", sam ?? "", r.Text("displayName") ?? r.Text("cn"), r.Text("description"));
        }).Where(e => e.Name.Length > 0).ToList();
        entries.Sort((a, b) => a.Kind != b.Kind ? (a.Kind == "group" ? -1 : 1) : string.Compare(a.Display ?? a.Name, b.Display ?? b.Name, StringComparison.OrdinalIgnoreCase));
        return new DirectoryList(entries, rows.Count >= Limit);
    }

    public async Task<IReadOnlyList<string>> Domains(string domain, CancellationToken stop)
    {
        var found = new List<string>();
        try
        {
            using var ldap = await Ldap.OpenAnonymous(domain, domain, stop);
            if (ldap is not null)
            {
                var config = (await ldap.Find("", "(objectClass=*)", false, ["configurationNamingContext"], 1, stop)).FirstOrDefault()?.Text("configurationNamingContext");
                if (config is not null)
                {
                    found.AddRange((await ldap.Find($"CN=Partitions,{config}", ForestFilter, true, ["dnsRoot"], Limit, stop)).Select(r => r.Text("dnsRoot")).OfType<string>());
                }
                found.AddRange((await ldap.Find($"CN=System,{ldap.BaseDn}", TrustFilter, true, ["trustPartner"], Limit, stop)).Select(r => r.Text("trustPartner")).OfType<string>());
            }
        }
        catch (Exception e) when (e is DirectoryProblem or LdapRefused)
        {
            // Unreachable now, or not allowed to read them (AD's default for anonymous): the domain set up still signs in.
        }
        var others = found.Select(d => d.Trim().TrimEnd('.').ToLowerInvariant()).Where(d => d.Length > 0 && d != domain).Distinct().Order();
        return [domain, .. others];
    }

    /// <summary>The groups with these names, by sAMAccountName or cn.</summary>
    internal static string GroupsFilter(IEnumerable<string> groups) =>
        "(&(objectClass=group)(|" + string.Concat(groups.Select(g => $"(sAMAccountName={Escape(g)})(cn={Escape(g)})")) + "))";

    /// <summary>Which of the groups asked about are these found groups, by either of their names.</summary>
    private static IEnumerable<string> Named(IEnumerable<string> wanted, params string?[] names) =>
        wanted.Where(g => names.Any(n => string.Equals(n, g, StringComparison.OrdinalIgnoreCase)));

    /// <summary>A value as an LDAP filter takes it (RFC 4515), so a username can't change the filter.</summary>
    public static string Escape(string value) => string.Concat(value.Select(c => c switch
    {
        '\\' => @"\5c",
        '*' => @"\2a",
        '(' => @"\28",
        ')' => @"\29",
        '\0' => @"\00",
        _ => c.ToString(),
    }));
}
