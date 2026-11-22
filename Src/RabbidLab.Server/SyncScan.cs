using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>A domain controller: its short name, DNS name, site, and whether it's read-only.</summary>
public sealed record SyncDc(string Name, string Host, string? Site, bool ReadOnly);

/// <summary>
/// One change made in Active Directory since the last sync: the object (by GUID), what changed in words, each detail, the
/// domain controller it was made on and when, and where it is: for each DC origin, ok, pending or unknown, and for
/// Microsoft 365 ok, pending, notyet or na (the engine fills that in).
/// </summary>
public sealed record SyncChange(string Id, string Kind, string Name, string? Sub, string What, IReadOnlyList<string> Details, string? On, DateTime At,
    Dictionary<string, string> Places);

/// <summary>What changed since a time, on which domain controllers, and the ones that couldn't be read (name, why).</summary>
public sealed record SyncScan(IReadOnlyList<SyncChange> Changes, IReadOnlyDictionary<string, string> Unreadable, bool More);

/// <summary>An attribute's replication metadata: its version, when and on which domain controller (its NTDS Settings) it last changed.</summary>
public sealed record AttrMeta(string Name, int Version, DateTime At, string? Dsa);

/// <summary>A linked value's metadata (a group's member): which value, its version, when it changed, and whether it was taken away.</summary>
public sealed record ValueMeta(string Name, string Value, int Version, DateTime At, bool Deleted, string? Dsa)
{
    public string Key => $"{Name}|{Value}";
}

/// <summary>What one domain controller holds of an object: its attributes' and values' metadata, and the row itself for the values.</summary>
public sealed record SyncSeen(IReadOnlyDictionary<string, AttrMeta> Attrs, IReadOnlyDictionary<string, ValueMeta> Values, LdapRow Row);

/// <summary>
/// Working out what changed and where it has got to, from msDS-ReplAttributeMetaData and msDS-ReplValueMetaData read on
/// every domain controller. A DC has a change when its version of each changed attribute or value is at least the newest.
/// </summary>
public static partial class SyncCompare
{
    public const string Origin = "origin";
    public const string Ok = "ok";
    public const string Pending = "pending";
    public const string Unknown = "unknown";

    // Changes nobody made on purpose; a computer's own 30-day password change counts as one too.
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "lastLogonTimestamp", "dSCorePropagationData", "msDS-LastSuccessfulInteractiveLogonTime", "msDS-LastFailedInteractiveLogonTime", "msDS-FailedInteractiveLogonCount",
        "msDS-FailedInteractiveLogonCountAtLastSuccessfulLogon", "logonCount", "badPwdCount", "badPasswordTime", "lastLogon", "msDS-KeyCredentialLink", "msDS-SupportedEncryptionTypes",
        "whenChanged", "uSNChanged", "objectClass", "instanceType", "nTSecurityDescriptor", "objectCategory", "objectSid", "sAMAccountType", "objectGUID", "whenCreated", "uSNCreated",
        "isCriticalSystemObject", "msDS-RevealedUsers", "msDS-RevealedList", "replPropertyMetaData",
    };

    private static readonly HashSet<string> Password = new(StringComparer.OrdinalIgnoreCase)
    {
        "unicodePwd", "dBCSPwd", "lmPwdHistory", "ntPwdHistory", "supplementalCredentials", "pwdLastSet",
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["displayName"] = "Display name", ["givenName"] = "First name", ["sn"] = "Last name", ["title"] = "Title", ["department"] = "Department",
        ["company"] = "Company", ["manager"] = "Manager", ["telephoneNumber"] = "Phone", ["mobile"] = "Mobile", ["mail"] = "Email",
        ["physicalDeliveryOfficeName"] = "Office", ["description"] = "Description", ["userPrincipalName"] = "Sign-in name", ["sAMAccountName"] = "Username",
        ["accountExpires"] = "Account expires", ["userAccountControl"] = "Account", ["lockoutTime"] = "Locked out", ["name"] = "Name or place",
        ["member"] = "Members", ["info"] = "Notes", ["homeDirectory"] = "Home folder", ["profilePath"] = "Profile", ["scriptPath"] = "Logon script",
        ["logonHours"] = "Logon hours", ["userWorkstations"] = "Log on to", ["managedBy"] = "Managed by", ["groupType"] = "Group type",
        ["dNSHostName"] = "DNS name", ["operatingSystem"] = "Windows", ["servicePrincipalName"] = "Service names", ["gPLink"] = "Linked GPOs",
    };

    /// <summary>One msDS-ReplAttributeMetaData value. Read by hand: thousands come back, and parsing each as XML is slow.</summary>
    public static AttrMeta Attribute(string xml) =>
        new(Tag(xml, "pszAttributeName") ?? "", Int(Tag(xml, "dwVersion")), Time(Tag(xml, "ftimeLastOriginatingChange")), Tag(xml, "pszLastOriginatingDsaDN"));

    /// <summary>One msDS-ReplValueMetaData value. A value taken away keeps its metadata, with ftimeDeleted set.</summary>
    public static ValueMeta Value(string xml) =>
        new(Tag(xml, "pszAttributeName") ?? "", Tag(xml, "pszObjectDn") ?? "", Int(Tag(xml, "dwVersion")), Time(Tag(xml, "ftimeLastOriginatingChange")),
            Time(Tag(xml, "ftimeDeleted")) > new DateTime(1602, 1, 1, 0, 0, 0, DateTimeKind.Utc), Tag(xml, "pszLastOriginatingDsaDN"));

    /// <summary>The domain controller an NTDS Settings DN belongs to: DC01 for CN=NTDS Settings,CN=DC01,CN=Servers,…</summary>
    public static string? Server(string? dsa) => dsa is not null && NtdsSettings().Match(dsa) is { Success: true } m ? m.Groups[1].Value.ToUpperInvariant() : null;

    /// <summary>The object as one domain controller holds it, from a row with both metadata attributes.</summary>
    public static SyncSeen Seen(LdapRow row)
    {
        var attrs = new Dictionary<string, AttrMeta>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in row.TextsInRanges("msDS-ReplAttributeMetaData").Select(Attribute).Where(a => a.Name.Length > 0))
        {
            attrs[a.Name] = a;
        }
        var values = new Dictionary<string, ValueMeta>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in row.TextsInRanges("msDS-ReplValueMetaData").Select(Value).Where(v => v.Name.Length > 0))
        {
            values[v.Key] = v;
        }
        return new SyncSeen(attrs, values, row);
    }

    /// <summary>
    /// Where one object's changes since then are: for each domain controller (null: it doesn't have the object yet; left out:
    /// it couldn't be read). Null when nothing anyone meant changed. created: the object itself is new since then.
    /// </summary>
    public static SyncChange? Compare(string id, IReadOnlyDictionary<string, SyncSeen?> byDc, IReadOnlyCollection<string> allDcs, DateTime since, string baseDn)
    {
        var held = byDc.Values.OfType<SyncSeen>().ToList();
        if (held.Count == 0)
        {
            return null;
        }
        var newestAttrs = held.SelectMany(s => s.Attrs.Values).GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(a => a.Version).ThenByDescending(a => a.At).First()).ToList();
        var newestValues = held.SelectMany(s => s.Values.Values).GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(v => v.Version).ThenByDescending(v => v.At).First()).ToList();
        var newest = held.OrderByDescending(s => s.Attrs.Values.Sum(a => (long) a.Version) + s.Values.Values.Sum(v => (long) v.Version)).First();
        var kind = Kind(newest.Row.Texts("objectClass"));
        var created = When(newest.Row.Text("whenCreated")) >= since;
        var attrs = newestAttrs.Where(a => a.At >= since && !Noise.Contains(a.Name) && !(kind == "computer" && Password.Contains(a.Name))).ToList();
        var values = newestValues.Where(v => v.At >= since).ToList();
        if (attrs.Count == 0 && values.Count == 0 && !created)
        {
            return null;
        }
        var latest = attrs.Select(a => (a.At, a.Dsa)).Concat(values.Select(v => (v.At, v.Dsa))).OrderByDescending(x => x.At).FirstOrDefault();
        var on = Server(latest.Dsa);
        var at = latest.At == default ? When(newest.Row.Text("whenCreated")) ?? since : latest.At;
        var places = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dc in allDcs)
        {
            if (!byDc.TryGetValue(dc, out var seen))
            {
                places[dc] = Unknown;
                continue;
            }
            var has = seen is not null
                && attrs.All(a => seen.Attrs.TryGetValue(a.Name, out var mine) && mine.Version >= a.Version)
                && values.All(v => seen.Values.TryGetValue(v.Key, out var mine) && mine.Version >= v.Version);
            places[dc] = !has ? Pending : string.Equals(dc, on, StringComparison.OrdinalIgnoreCase) ? Origin : Ok;
        }
        var (what, details) = Describe(kind, created, attrs.Select(a => a.Name).ToList(), values, newest.Row, baseDn);
        var row = newest.Row;
        var name = kind == "user" ? row.Text("displayName") ?? row.Text("cn") : row.Text("cn") ?? row.Text("name") ?? Ldap.Name(row.Dn);
        var sub = kind is "user" or "computer" ? row.Text("sAMAccountName")?.TrimEnd('$') : KindWord(kind);
        return new SyncChange(id, kind, name ?? id, sub, what, details, on, at, places);
    }

    /// <summary>What changed, in a few words, and each change as "Title: Senior analyst" (new values only: Active Directory doesn't keep old ones).</summary>
    public static (string What, List<string> Details) Describe(string kind, bool created, IReadOnlyList<string> attrs, IReadOnlyList<ValueMeta> values, LdapRow row, string baseDn)
    {
        var details = new List<string>();
        if (created)
        {
            details.Add($"Where: {Place(Ldap.Parent(row.Dn), baseDn)}");
            foreach (var a in new[] { "title", "department", "mail" }.Where(a => row.Text(a) is { Length: > 0 }))
            {
                details.Add($"{Labels[a]}: {row.Text(a)}");
            }
            return ($"New {KindWord(kind).ToLowerInvariant()}", details);
        }
        var words = new List<string>();
        var password = attrs.Any(Password.Contains);
        if (password)
        {
            words.Add("Password changed");
            details.Add("Password: changed");
        }
        foreach (var a in attrs.Where(a => !Password.Contains(a)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            switch (a.ToLowerInvariant())
            {
                case "useraccountcontrol":
                    var off = (Long(row.Text("userAccountControl")) & 2) != 0;
                    words.Add(off ? "Disabled" : "Enabled");
                    details.Add($"Account: {(off ? "disabled" : "enabled")}");
                    break;
                case "lockouttime":
                    var locked = Long(row.Text("lockoutTime")) != 0;
                    words.Add(locked ? "Locked out" : "Unlocked");
                    details.Add($"Locked out: {(locked ? "yes" : "no")}");
                    break;
                case "name":
                    words.Add("Renamed or moved");
                    details.Add($"Now: {Place(row.Dn, baseDn)}");
                    break;
                default:
                    var label = Labels.GetValueOrDefault(a, a);
                    words.Add(label);
                    var value = string.Join(", ", row.Texts(a).Take(3).Select(v => v.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ? Ldap.Name(v) : v));
                    details.Add(value.Length > 0 ? $"{label}: {Cut(value, 80)}" : $"{label}: cleared");
                    break;
            }
        }
        var added = values.Where(v => !v.Deleted).ToList();
        var removed = values.Where(v => v.Deleted).ToList();
        details.AddRange(added.Take(5).Select(v => $"Added {Ldap.Name(v.Value)}"));
        details.AddRange(removed.Take(5).Select(v => $"Removed {Ldap.Name(v.Value)}"));
        if (added.Count + removed.Count > 10)
        {
            details.Add($"{added.Count + removed.Count} member changes in all");
        }
        if (added.Count > 0 || removed.Count > 0)
        {
            words.Add(removed.Count == 0 ? (added.Count == 1 ? "Member added" : $"{added.Count} members added")
                : added.Count == 0 ? (removed.Count == 1 ? "Member removed" : $"{removed.Count} members removed") : "Members changed");
        }
        // Password, Enabled and the like read well as they are; plain labels read as a list.
        var what = words.Count == 0 ? "Changed"
            : words.Count == 1 ? words[0]
            : words.Count <= 3 ? $"{string.Join(", ", words.Take(words.Count - 1))} and {words[^1].ToLowerInvariant()}"
            : $"{words.Count} things changed";
        return (what, details);
    }

    public static string Kind(IEnumerable<string> classes)
    {
        var c = classes.ToList();
        bool Is(string x) => c.Contains(x, StringComparer.OrdinalIgnoreCase);
        return Is("computer") ? "computer" : Is("group") ? "group" : Is("contact") ? "contact" : Is("user") ? "user" : Is("organizationalUnit") ? "ou" : "other";
    }

    public static string KindWord(string kind) => kind switch
    {
        "user" => "User",
        "group" => "Group",
        "computer" => "Computer",
        "contact" => "Contact",
        "ou" => "OU",
        _ => "Object",
    };

    /// <summary>A GUID from objectGUID's 16 bytes, as text, and as an LDAP filter value (\xx for each byte).</summary>
    public static string Id(byte[] guid) => new Guid(guid).ToString();

    public static string Filter(string id)
    {
        var b = Guid.Parse(id).ToByteArray();
        var s = new StringBuilder();
        foreach (var x in b)
        {
            s.Append('\\').Append(x.ToString("x2", CultureInfo.InvariantCulture));
        }
        return s.ToString();
    }

    public static DateTime? When(string? generalized) =>
        DateTime.TryParseExact(generalized, "yyyyMMddHHmmss.0Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    private static string Place(string dn, string baseDn)
    {
        var path = Ldap.Path(dn, baseDn);
        return path.Length == 0 ? "the domain" : path.Replace(" / ", " › ", StringComparison.Ordinal);
    }

    private static long Long(string? text) => long.TryParse(text, out var n) ? n : 0;

    private static int Int(string? text) => int.TryParse(text, out var n) ? n : 0;

    private static string Cut(string text, int most) => text.Length <= most ? text : text[..(most - 1)] + "…";

    private static DateTime Time(string? text) => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : DateTime.MinValue;

    private static string? Tag(string xml, string tag)
    {
        var from = xml.IndexOf($"<{tag}>", StringComparison.Ordinal);
        var to = from < 0 ? -1 : xml.IndexOf("</", from, StringComparison.Ordinal);
        return to < 0 ? null : System.Net.WebUtility.HtmlDecode(xml[(from + tag.Length + 2)..to]);
    }

    [GeneratedRegex(@"^CN=NTDS Settings[^,]*,CN=([^,]+),", RegexOptions.IgnoreCase)]
    private static partial Regex NtdsSettings();
}

public sealed partial class DirectoryTools
{
    private static readonly string[] SyncAttributes =
    [
        "objectGUID", "objectClass", "cn", "name", "displayName", "sAMAccountName", "whenCreated", "msDS-ReplAttributeMetaData", "msDS-ReplValueMetaData",
        "title", "department", "company", "manager", "telephoneNumber", "mobile", "mail", "physicalDeliveryOfficeName", "description", "userPrincipalName",
        "userAccountControl", "lockoutTime", "givenName", "sn", "info", "managedBy",
    ];

    private const int SyncMost = 300; // changes shown at most, the newest

    /// <summary>The domain's domain controllers, read-only ones too, with their sites, as Active Directory lists them.</summary>
    public async Task<(string Domain, List<SyncDc> Dcs)> SyncDcs(CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var rows = await ldap.Find(ldap.BaseDn, "(&(objectCategory=computer)(|(userAccountControl:1.2.840.113556.1.4.803:=8192)(userAccountControl:1.2.840.113556.1.4.803:=67108864)))",
            true, ["cn", "dNSHostName", "serverReferenceBL", "userAccountControl"], 200, stop);
        var dcs = rows.Select(r => new SyncDc((r.Text("cn") ?? Ldap.Name(r.Dn)).ToUpperInvariant(), r.Text("dNSHostName") ?? r.Text("cn") ?? "",
                r.Texts("serverReferenceBL").Select(Site).FirstOrDefault(s => s is not null), ((r.Number("userAccountControl") ?? 0) & 0x04000000) != 0))
            .Where(d => d.Host.Length > 0).OrderBy(d => d.ReadOnly).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return (Ready().Domain!, dcs);
    }

    /// <summary>The account and password the sync signs in to each domain controller with; null in read-only mode, which can't sync.</summary>
    public (string User, string Password)? SyncLogin()
    {
        var f = Ready();
        return f.ReadOnly || secrets.TryDecrypt(f.Password) is not { } password ? null : ($"{f.Account}@{f.Domain}", password);
    }

    /// <summary>
    /// What changed since then, read on every domain controller: each lists what it changed lately (its own changes and
    /// those it took in), then each is asked for the ones it didn't list, so a DC that's missing one says so.
    /// </summary>
    public async Task<SyncScan> SyncScan(IReadOnlyList<SyncDc> dcs, DateTime since, CancellationToken stop)
    {
        var f = Ready();
        var stamp = since.ToString("yyyyMMddHHmmss.0Z", CultureInfo.InvariantCulture);
        var filter = $"(&(whenChanged>={stamp})(|(&(objectCategory=person)(objectClass=user))(objectCategory=group)(objectCategory=computer)(objectClass=organizationalUnit)(objectClass=contact)))";
        var unreadable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, Dictionary<string, LdapRow>>(StringComparer.OrdinalIgnoreCase);
        var open = new Dictionary<string, ILdap>(StringComparer.OrdinalIgnoreCase);
        var more = false;
        var baseDn = "";
        try
        {
            foreach (var dc in dcs)
            {
                try
                {
                    var ldap = await OpenOn(f, dc.Host, stop);
                    open[dc.Name] = ldap;
                    baseDn = ldap.BaseDn;
                    var rows = await ldap.Find(ldap.BaseDn, filter, true, SyncAttributes, Limit, stop);
                    more |= rows.Count >= Limit;
                    seen[dc.Name] = rows.Where(r => r.Bytes("objectGUID") is { Length: 16 }).ToDictionary(r => SyncCompare.Id(r.Bytes("objectGUID")!), StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception e) when (e is DirectoryProblem or ToolProblem or LdapRefused or TimeoutException or System.Net.Sockets.SocketException)
                {
                    unreadable[dc.Name] = e.Message;
                }
            }
            // The newest few hundred, by when the DC that has it newest last wrote it.
            var ids = seen.Values.SelectMany(d => d).GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Max(p => SyncCompare.Seen(p.Value).Attrs.Values.Select(a => a.At).DefaultIfEmpty().Max())).Select(g => g.Key).ToList();
            more |= ids.Count > SyncMost;
            ids = [.. ids.Take(SyncMost)];
            // Each DC, for the ones it didn't list: fewer requests by asking for forty at a time.
            foreach (var (name, ldap) in open)
            {
                var missing = ids.Where(id => !seen[name].ContainsKey(id)).ToList();
                foreach (var chunk in missing.Chunk(40))
                {
                    var rows = await ldap.Find(ldap.BaseDn, $"(|{string.Concat(chunk.Select(id => $"(objectGUID={SyncCompare.Filter(id)})"))})", true, SyncAttributes, chunk.Length, stop);
                    foreach (var r in rows.Where(r => r.Bytes("objectGUID") is { Length: 16 }))
                    {
                        seen[name][SyncCompare.Id(r.Bytes("objectGUID")!)] = r;
                    }
                }
            }
            var all = dcs.Select(d => d.Name).ToList();
            var changes = new List<SyncChange>();
            foreach (var id in ids)
            {
                var byDc = new Dictionary<string, SyncSeen?>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in open.Keys)
                {
                    byDc[name] = seen[name].TryGetValue(id, out var row) ? SyncCompare.Seen(row) : null;
                }
                if (SyncCompare.Compare(id, byDc, all, since, baseDn) is { } change)
                {
                    changes.Add(change);
                }
            }
            return new SyncScan([.. changes.OrderByDescending(c => c.At)], unreadable, more);
        }
        finally
        {
            foreach (var ldap in open.Values)
            {
                ldap.Dispose();
            }
        }
    }

    /// <summary>The server Entra Connect runs on, from its MSOL_ account in Active Directory; null when it isn't set up.</summary>
    public async Task<string?> EntraServer(CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var rows = await ldap.Find(ldap.BaseDn, "(&(objectCategory=person)(objectClass=user)(sAMAccountName=MSOL_*))", true,
            ["description", "userAccountControl", "lastLogonTimestamp"], 20, stop);
        return rows.Where(r => ((r.Number("userAccountControl") ?? 0) & Disabled) == 0).OrderByDescending(r => r.Number("lastLogonTimestamp") ?? 0)
            .Select(r => SyncEntra.Server(r.Text("description"))).FirstOrDefault(s => s is not null);
    }

    // A connection to one domain controller, as the service account (or this computer, read-only).
    private async Task<ILdap> OpenOn(DirectoryToolsFile f, string host, CancellationToken stop)
    {
        if (f.ReadOnly)
        {
            return await AsComputer!(f.Domain!, host, stop) ?? throw new ToolProblem($"{host} couldn't be read as this computer.");
        }
        var password = secrets.TryDecrypt(f.Password) ?? throw new ToolProblem($"{Product.Title}'s Active Directory password can't be read here any more. Set it up again.", StatusCodes.Status409Conflict);
        return await open(f.Domain!, host, $"{f.Account}@{f.Domain}", password, stop) ?? throw new ToolProblem($"{host} turned down {f.Account}'s password.");
    }

    // The site from a serverReferenceBL: Lab-Site for CN=DC01,CN=Servers,CN=Lab-Site,CN=Sites,CN=Configuration,…
    private static string? Site(string dn)
    {
        var parts = dn.Split(',');
        return parts.Length > 3 && parts[1].Equals("CN=Servers", StringComparison.OrdinalIgnoreCase) ? Ldap.Name(parts[2]) : null;
    }
}
