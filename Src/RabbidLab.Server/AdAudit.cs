using System.Collections.Concurrent;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace RabbidLab.Server;

// The AD audit: directory changes read from each domain controller's Security log (events 5136 to 5141), with the old
// and new value of each attribute, who made the change and on which DC. Kept in ad-audit.jsonl for 30 days, 90 days or a
// year. Where no log recorded a change, replication metadata (DirectoryTools.Changes) still says what changed and its value now.

/// <summary>One event from a domain controller's Security log: its ID, record number, when, the DC, and its named data.</summary>
public sealed record SecurityEvent(int Id, long Record, DateTime At, string Dc, IReadOnlyDictionary<string, string> Data)
{
    /// <summary>A data value, or null when it's missing, empty or "-".</summary>
    public string? Get(string name) => Data.TryGetValue(name, out var v) && v.Length > 0 && v != "-" ? v : null;
}

/// <summary>Which Security log events are read, how they're asked for, and reading one from its XML.</summary>
public static class SecurityEvents
{
    // Directory service changes. Sign-ins and lockouts (4624, 4625, 4740) join this list when alerts need them.
    public static readonly IReadOnlyList<int> Watched = [5136, 5137, 5138, 5139, 5141];

    /// <summary>The events after the last one read; the first time (after is 0), the ones since then.</summary>
    public static string Query(IEnumerable<int> ids, long after, DateTime since) =>
        $"*[System[({string.Join(" or ", ids.Select(i => $"EventID={i}"))}) and {(after > 0 ? $"EventRecordID>{after}" : $"TimeCreated[@SystemTime>='{Iso(since)}']")}]]";

    /// <summary>Any event numbered up to the last one read but newer than it: the log was cleared and numbers started again.</summary>
    public static string Cleared(long after, DateTime lastAt) => $"*[System[EventRecordID<={after} and TimeCreated[@SystemTime>'{Iso(lastAt)}']]]";

    private static string Iso(DateTime at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>An event from its XML (EventRecord.ToXml), or null when it isn't one.</summary>
    public static SecurityEvent? Parse(string xml, string dc)
    {
        try
        {
            var root = XElement.Parse(xml);
            var ns = root.Name.Namespace;
            var system = root.Element(ns + "System");
            if (system is null || !int.TryParse(system.Element(ns + "EventID")?.Value, out var id) || !long.TryParse(system.Element(ns + "EventRecordID")?.Value, out var record)
                || !DateTime.TryParse(system.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at))
            {
                return null;
            }
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in root.Element(ns + "EventData")?.Elements(ns + "Data") ?? [])
            {
                if (d.Attribute("Name")?.Value is { Length: > 0 } name)
                {
                    data[name] = d.Value;
                }
            }
            return new SecurityEvent(id, record, at, dc, data);
        }
        catch (XmlException)
        {
            return null;
        }
    }
}

/// <summary>
/// One change as a Security log recorded it, as ad-audit.jsonl keeps it: the operation (OpCorrelationID) it was part of,
/// made, changed, moved, deleted or restored, the object, the attribute with its values taken away (Old) and added (New),
/// the account that made it and that account's display name.
/// </summary>
public sealed record AuditEntry(string Id, DateTime At, string Dc, int Event, string Op, string What, string Dn, string? Guid, string Kind, string Attribute,
    List<string> Old, List<string> New, string? Account, string? ByName = null);

/// <summary>A Security log's directory events made into changes: the deleted and added values of an attribute in one operation are its old and new value.</summary>
public static class AuditEntries
{
    /// <summary>The attribute of a change to the whole object: made, deleted or restored.</summary>
    public const string Whole = "(object)";

    /// <summary>The attribute of a move or rename: from the old DN (Old) to the new (New).</summary>
    public const string Moved = "(moved)";

    // Values people add and take away one at a time: shown as added or removed, not as an old and new value.
    public static readonly IReadOnlyDictionary<string, string> Listed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["member"] = "Member", ["servicePrincipalName"] = "Service principal name", ["msDS-AllowedToDelegateTo"] = "Delegation target",
    };

    public static List<AuditEntry> From(IEnumerable<SecurityEvent> events)
    {
        var list = new List<AuditEntry>();
        var ops = events.Where(e => SecurityEvents.Watched.Contains(e.Id)).OrderBy(e => e.Record)
            .GroupBy(e => (Op: OpOf(e), Of: Guid(e.Get("ObjectGUID")) ?? e.Get("ObjectDN") ?? e.Get("NewObjectDN") ?? ""));
        foreach (var op in ops)
        {
            var first = op.First();
            if (KindOf(first.Get("ObjectClass")) is not { } kind)
            {
                continue; // not a user, group, computer or OU: GPOs, DNS records, the system's own
            }
            var guid = Guid(first.Get("ObjectGUID"));
            var account = first.Get("SubjectUserName") is { } user ? first.Get("SubjectDomainName") is { } domain ? $"{domain}\\{user}" : user : null;
            var id = op.Key.Op.Trim('{', '}').ToLowerInvariant();
            AuditEntry Entry(SecurityEvent e, string what, string dn, string attribute, List<string> old, List<string> now) =>
                new($"{e.Dc}:{e.Record}:{e.At:yyyyMMddHHmmss}", e.At, e.Dc, e.Id, id, what, dn, guid, kind, attribute, old, now, account);
            foreach (var e in op.Where(e => e.Id != 5136))
            {
                switch (e.Id)
                {
                    case 5137 when e.Get("ObjectDN") is { } dn && !Gone(dn):
                        list.Add(Entry(e, "made", dn, Whole, [], []));
                        break;
                    case 5141 when e.Get("ObjectDN") is { } dn:
                        list.Add(Entry(e, "deleted", dn, Whole, [], []));
                        break;
                    case 5138 when e.Get("NewObjectDN") is { } dn:
                        list.Add(Entry(e, "restored", dn, Whole, [e.Get("OldObjectDN") ?? ""], [dn]));
                        break;
                    // Into Deleted Objects is the delete itself (5141).
                    case 5139 when e.Get("NewObjectDN") is { } dn && !Gone(dn):
                        list.Add(Entry(e, "moved", dn, Moved, [e.Get("OldObjectDN") ?? ""], [dn]));
                        break;
                }
            }
            foreach (var attribute in op.Where(e => e.Id == 5136 && e.Get("AttributeLDAPDisplayName") is not null).GroupBy(e => e.Get("AttributeLDAPDisplayName")!, StringComparer.OrdinalIgnoreCase))
            {
                var name = attribute.Key;
                if (attribute.First().Get("ObjectDN") is not { } dn || Gone(dn) || DirectoryTools.Noise.Contains(name) || (kind == "computer" && DirectoryTools.Passwordy.Contains(name)))
                {
                    continue;
                }
                // %%14674 is Value Added, %%14675 Value Deleted.
                var added = attribute.Where(e => (e.Get("OperationType") ?? "").Contains("14674", StringComparison.Ordinal)).ToList();
                var removed = attribute.Except(added).ToList();
                // A secret's value is never kept, should a log ever carry one.
                List<string> Values(List<SecurityEvent> es) => [.. es.Select(e => DirectoryTools.Secret.Contains(name) ? "(hidden)" : e.Data.GetValueOrDefault("AttributeValue") ?? "")];
                if (Listed.ContainsKey(name))
                {
                    if (added.Count > 0)
                    {
                        list.Add(Entry(added[0], "changed", dn, name, [], Values(added)));
                    }
                    if (removed.Count > 0)
                    {
                        list.Add(Entry(removed[0], "changed", dn, name, Values(removed), []));
                    }
                }
                else
                {
                    list.Add(Entry(attribute.First(), "changed", dn, name, Values(removed), Values(added)));
                }
            }
        }
        return list;
    }

    /// <summary>The operation an event belongs to: everything one LDAP request did has the same OpCorrelationID.</summary>
    public static string OpOf(SecurityEvent e) => e.Get("OpCorrelationID") ?? $"r{e.Record}";

    private static string? KindOf(string? objectClass) => objectClass?.ToLowerInvariant() switch
    {
        "user" or "inetorgperson" => "user",
        "computer" or "msds-groupmanagedserviceaccount" or "msds-managedserviceaccount" => "computer",
        "group" => "group",
        "organizationalunit" => "ou",
        _ => null,
    };

    private static string? Guid(string? text) => System.Guid.TryParse(text?.Trim('%', '{', '}', ' '), out var g) ? g.ToString() : null;

    // In Deleted Objects, or in the DNS partitions.
    private static bool Gone(string dn) => dn.Contains("\\0ADEL:", StringComparison.OrdinalIgnoreCase)
        || dn.Contains(",DC=DomainDnsZones,", StringComparison.OrdinalIgnoreCase) || dn.Contains(",DC=ForestDnsZones,", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One row of the AD audit or a History tab: an attribute change, when, Modified by (the person in Rabbid Lab when it made
/// the change, App), the object, the attribute's label, its old and new values in words, the originating DC and the
/// operation. Event is null when no Security log recorded it: Old is then null (not recorded), and New is the value now
/// (null when it isn't read). Change is the Undo journal's id for a change made in Rabbid Lab.
/// </summary>
public sealed record AuditRow(string Id, DateTime At, string? By, string? Account, bool App, string Dn, string Name, string Kind, string Where, string? Guid,
    string Attribute, string Label, List<string>? Old, List<string>? New, string? Dc, string Op, string What, int? Event, string? Change = null);

/// <summary>A writable domain controller as the audit sees it: recording, silent (its log is read, but it didn't record changes made on it), waiting, denied, unreachable, error or unsupported.</summary>
public sealed record AuditDc(string Name, string State, string? Problem);

/// <summary>The rows the page shows: events in words, and replication metadata for what no event covers.</summary>
public static class AuditView
{
    // An event and the metadata about the same change can be this far apart.
    private static readonly TimeSpan Slack = TimeSpan.FromMinutes(2);

    // A change made on a DC this long before its log was last read should have been read with it.
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);

    // What a move or rename changes in the metadata.
    private static readonly HashSet<string> Renames = new(StringComparer.OrdinalIgnoreCase) { "name", "cn", "ou", "distinguishedName" };

    // The words users.js uses for each attribute.
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["displayName"] = "Display name", ["givenName"] = "First name", ["sn"] = "Last name", ["initials"] = "Initials", ["title"] = "Job title", ["department"] = "Department",
        ["company"] = "Company", ["manager"] = "Manager", ["telephoneNumber"] = "Phone", ["mobile"] = "Mobile", ["ipPhone"] = "IP phone", ["facsimileTelephoneNumber"] = "Fax",
        ["mail"] = "Email", ["proxyAddresses"] = "Email aliases", ["physicalDeliveryOfficeName"] = "Office", ["streetAddress"] = "Street", ["l"] = "City",
        ["st"] = "State or province", ["postalCode"] = "ZIP or postal code", ["co"] = "Country", ["c"] = "Country", ["countryCode"] = "Country", ["description"] = "Description",
        ["info"] = "Notes", ["userPrincipalName"] = "User principal name", ["sAMAccountName"] = "Pre-Windows 2000 logon name", ["userAccountControl"] = "Account",
        ["pwdLastSet"] = "Password last set", ["accountExpires"] = "Account expires", ["lockoutTime"] = "Locked out", ["employeeID"] = "Employee ID",
        ["employeeNumber"] = "Employee number", ["employeeType"] = "Employee type", ["homeDirectory"] = "Home folder", ["homeDrive"] = "Home drive",
        ["profilePath"] = "Profile path", ["scriptPath"] = "Logon script", ["wWWHomePage"] = "Web page", ["managedBy"] = "Managed by", ["groupType"] = "Group type",
        ["location"] = "Location", ["operatingSystem"] = "Operating system", ["operatingSystemVersion"] = "OS version", ["dNSHostName"] = "DNS host name",
        ["name"] = "Name", ["cn"] = "Name", ["ou"] = "Name", ["member"] = "Members", ["servicePrincipalName"] = "Service principal names",
        ["msDS-AllowedToDelegateTo"] = "Delegation targets", ["logonHours"] = "Logon hours", ["userWorkstations"] = "Log on to", ["nTSecurityDescriptor"] = "Permissions",
        ["gPLink"] = "Linked GPOs", ["gPOptions"] = "Block inheritance", ["isDeleted"] = "Deleted", ["primaryGroupID"] = "Primary group",
    };

    /// <summary>The events in words, then the metadata changes no event covers, newest first. ours: Rabbid Lab's own AD entries in its activity log.</summary>
    public static List<AuditRow> Rows(IReadOnlyList<AuditEntry> events, IReadOnlyList<AdChange> meta, IReadOnlyList<ActivityEntry> ours, string? service)
    {
        // Rabbid Lab's own changes, by the minute, so each change only looks at the few around it.
        var byMinute = ours.ToLookup(e => e.At.Ticks / TimeSpan.TicksPerMinute);
        ActivityEntry? Ours(DateTime at, string name) => Enumerable.Range(-3, 7).SelectMany(m => byMinute[(at.Ticks / TimeSpan.TicksPerMinute) + m])
            .Where(e => Math.Abs((e.At - at).TotalMinutes) < 3 && DirectoryExtrasApi.Mentions(e.Title, name)).MinBy(e => Math.Abs((e.At - at).Ticks));
        var rows = new List<AuditRow>();
        var mine = new Dictionary<string, ActivityEntry?>(StringComparer.Ordinal); // by operation
        foreach (var e in events)
        {
            var name = Ldap.Name(e.Dn);
            // The service account made it: Modified by is whoever made it in Rabbid Lab.
            if (!mine.TryGetValue(e.Op, out var app))
            {
                mine[e.Op] = app = service is not null && e.Account is { } a && Ldap.Account(a).Equals(service, StringComparison.OrdinalIgnoreCase) ? Ours(e.At, name) : null;
            }
            var (label, old, now) = Words(e);
            rows.Add(new AuditRow(e.Id, e.At, app?.By ?? e.ByName ?? (e.Account is { } who ? Ldap.Account(who) : null), e.Account, app is not null, e.Dn, name, e.Kind,
                Where(e.Dn), e.Guid, e.Attribute, label, old, now, e.Dc, e.Op, e.What, e.Event, app?.Change));
        }

        // The events about each object, by its GUID and its DN, to find the metadata changes they cover.
        var about = new Dictionary<string, List<AuditEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in events)
        {
            foreach (var key in new[] { e.Dn, e.Guid }.OfType<string>())
            {
                if (!about.TryGetValue(key, out var list))
                {
                    about[key] = list = [];
                }
                list.Add(e);
            }
        }
        bool Covered(AdChange c, string attribute, DateTime at, string what) =>
            new[] { c.Guid, c.Dn }.OfType<string>().SelectMany(k => about.TryGetValue(k, out var l) ? l : []).Any(e => (e.At - at).Duration() <= Slack
                && (attribute == AuditEntries.Whole ? e.Attribute == AuditEntries.Whole && e.What == what
                    : e.Attribute.Equals(attribute, StringComparison.OrdinalIgnoreCase) || (e.Attribute == AuditEntries.Moved && Renames.Contains(attribute))));
        foreach (var c in meta)
        {
            var key = c.Guid ?? c.Dn.ToLowerInvariant();
            if (c.What != "changed")
            {
                if (Covered(c, AuditEntries.Whole, c.At, c.What))
                {
                    continue;
                }
                var (old, now) = WholeWords(c.What, c.Where.Length > 0 ? c.Where : DomainOf(c.Dn));
                var app = Ours(c.At, c.Name);
                rows.Add(new AuditRow($"meta:{key}:{c.What}:{c.At.Ticks}", c.At, app?.By, null, app is not null, c.Dn, c.Name, c.Kind, c.Where, c.Guid,
                    AuditEntries.Whole, "Object", old, now, null, $"meta:{key}:{c.At:yyyyMMddHHmmss}", c.What, null, app?.Change));
                continue;
            }
            foreach (var a in c.Attributes)
            {
                if (Covered(c, a.Name, a.At, c.What))
                {
                    continue;
                }
                List<string>? now = a.Now is null ? null : [.. a.Now.Select(v => Show(a.Name, v))];
                // Members added or removed are known from their own metadata; any other old value isn't.
                List<string>? old = null;
                if (AuditEntries.Listed.ContainsKey(a.Name) && now is not null)
                {
                    old = a.Removed ? now : [];
                    now = a.Removed ? [] : now;
                }
                var app = Ours(a.At, c.Name);
                rows.Add(new AuditRow($"meta:{key}:{a.Name}:{a.At.Ticks}{(a.Removed ? ":out" : "")}", a.At, app?.By, null, app is not null, c.Dn, c.Name, c.Kind, c.Where, c.Guid,
                    a.Name, Label(a.Name, old, now), old, now, a.Dc, $"meta:{key}:{a.At:yyyyMMddHHmmss}", "changed", null, app?.Change));
            }
        }
        return [.. rows.OrderByDescending(r => r.At).ThenBy(r => r.Id, StringComparer.Ordinal)];
    }

    /// <summary>Each DC's state, with silent for one whose log is read but which recorded none of the changes made on it since.</summary>
    public static List<AuditDc> Dcs(IReadOnlyList<(string Name, AuditDcState State)> dcs, IReadOnlyList<AuditRow> rows) => [.. dcs.Select(d =>
    {
        var s = d.State;
        if (s.State != AuditDcState.Read)
        {
            return new AuditDc(d.Name, s.State, s.Problem);
        }
        var silent = s.FirstRead is { } first && s.LastRead is { } last && rows.Any(r => r.Event is null && string.Equals(r.Dc, d.Name, StringComparison.OrdinalIgnoreCase)
            && r.At >= first && r.At <= last - Margin);
        return new AuditDc(d.Name, silent ? "silent" : "recording", null);
    })];

    /// <summary>An attribute in the words the page uses; a value added or removed on its own says so.</summary>
    public static string Label(string attribute, List<string>? old, List<string>? now) =>
        AuditEntries.Listed.TryGetValue(attribute, out var one) && (old?.Count ?? 0) == 0 != ((now?.Count ?? 0) == 0) ? $"{one} {((now?.Count ?? 0) > 0 ? "added" : "removed")}"
        : Labels.TryGetValue(attribute, out var label) ? label
        : attribute.StartsWith("extensionAttribute", StringComparison.OrdinalIgnoreCase) && int.TryParse(attribute[18..], out var n) ? $"Custom attribute {n}"
        : attribute;

    // An event's label and values in words.
    private static (string Label, List<string> Old, List<string> New) Words(AuditEntry e)
    {
        switch (e.Attribute)
        {
            case AuditEntries.Whole:
                var (was, now) = WholeWords(e.What, Place(Ldap.Parent(e.Dn)));
                return ("Object", was, now);
            case AuditEntries.Moved:
                var from = e.Old.FirstOrDefault() ?? "";
                var to = e.New.FirstOrDefault() ?? e.Dn;
                var moved = !string.Equals(Ldap.Parent(from), Ldap.Parent(to), StringComparison.OrdinalIgnoreCase);
                var renamed = !string.Equals(Ldap.Name(from), Ldap.Name(to), StringComparison.OrdinalIgnoreCase);
                return moved && renamed ? ("Location", [Place(from)], [Place(to)])
                    : moved ? ("Location", [Place(Ldap.Parent(from))], [Place(Ldap.Parent(to))])
                    : ("Name", [Ldap.Name(from)], [Ldap.Name(to)]);
            default:
                List<string> old = [.. e.Old.Select(v => Show(e.Attribute, v))];
                List<string> added = [.. e.New.Select(v => Show(e.Attribute, v))];
                return (Label(e.Attribute, old, added), old, added);
        }
    }

    // Made, deleted or restored, as an old and a new value.
    private static (List<string> Old, List<string> New) WholeWords(string what, string place) => what switch
    {
        "made" => ([], [$"Created in {place}"]),
        "deleted" => ([$"In {place}"], ["Deleted"]),
        _ => (["Deleted"], [$"Restored to {place}"]),
    };

    /// <summary>One value in words: account options and group types by name, times as times, and a DN as its name.</summary>
    public static string Show(string attribute, string value)
    {
        if (DirectoryTools.Secret.Contains(attribute))
        {
            return "(hidden)";
        }
        var v = value.Trim();
        if (attribute.Equals("userAccountControl", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out var flags))
        {
            var named = new (int Bit, string Name)[] { (0x10, "locked out"), (0x20, "password not required"), (0x80, "reversible password encryption"),
                (0x10000, "password never expires"), (0x40000, "smart card required"), (0x80000, "trusted for delegation"), (0x100000, "sensitive and cannot be delegated"),
                (0x200000, "Kerberos DES encryption only"), (0x400000, "Kerberos preauthentication not required"), (0x1000000, "trusted to authenticate for delegation") };
            return string.Join(", ", named.Where(f => (flags & f.Bit) != 0).Select(f => f.Name).Prepend((flags & 0x2) != 0 ? "Disabled" : "Enabled"));
        }
        if (attribute.Equals("groupType", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out var type))
        {
            var scope = (type & 0xF) switch { 1 => "Builtin", 2 => "Global", 4 => "Domain local", 8 => "Universal", _ => "" };
            return string.Join(", ", new[] { type < 0 ? "Security" : "Distribution", scope }.Where(s => s.Length > 0));
        }
        if (DirectoryTools.FileTimes.Contains(attribute) && long.TryParse(v, out var ft))
        {
            return ft switch
            {
                0 when attribute.Equals("pwdLastSet", StringComparison.OrdinalIgnoreCase) => "Must change password at next sign-in",
                0 when attribute.Equals("lockoutTime", StringComparison.OrdinalIgnoreCase) => "Not locked out",
                0 or long.MaxValue => "Never",
                > 0 when ft < DateTime.MaxValue.ToFileTimeUtc() => $"{DateTime.FromFileTimeUtc(ft):yyyy-MM-dd HH:mm} UTC",
                _ => v,
            };
        }
        if (DateTime.TryParseExact(v, "yyyyMMddHHmmss.0Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at))
        {
            return $"{at:yyyy-MM-dd HH:mm} UTC";
        }
        if ((v.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) || v.StartsWith("OU=", StringComparison.OrdinalIgnoreCase)) && v.Contains(",DC=", StringComparison.OrdinalIgnoreCase))
        {
            return Ldap.Name(v);
        }
        return v.Length > 2000 ? v[..2000] + "…" : v;
    }

    // Where something is, readably: Staff / Sales, or the domain's name at the top.
    private static string Where(string dn) => Ldap.Path(Ldap.Parent(dn), BaseOf(dn));

    private static string Place(string dn) => Ldap.Path(dn, BaseOf(dn)) is { Length: > 0 } path ? path : DomainOf(dn);

    private static string BaseOf(string dn) => string.Join(',', Ldap.Split(dn).Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)));

    private static string DomainOf(string dn) => string.Join('.', Ldap.Split(dn).Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)).Select(p => p[3..]));
}

/// <summary>Reads a domain controller's Security log, signed in as an account. Tests stand in for it.</summary>
public interface ISecurityLog
{
    /// <summary>Up to most events' XML that match the query, oldest first. Throws SecurityLogProblem when the log can't be read.</summary>
    Task<IReadOnlyList<string>> Read(string host, AuditLogin login, string query, int most, CancellationToken stop);
}

/// <summary>Why a Security log couldn't be read: denied, unreachable, error or unsupported, and what to say.</summary>
public sealed class SecurityLogProblem(string state, string message) : Exception(message)
{
    public string State { get; } = state;
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSecurityLog : ISecurityLog
{
    public Task<IReadOnlyList<string>> Read(string host, AuditLogin login, string query, int most, CancellationToken stop) => Task.Run<IReadOnlyList<string>>(() =>
    {
        using var password = new System.Security.SecureString();
        foreach (var c in login.Password)
        {
            password.AppendChar(c);
        }
        var dc = SyncWhy.Short(host);
        try
        {
            using var session = new EventLogSession(host, login.Domain, login.User, password, SessionAuthentication.Default);
            using var reader = new EventLogReader(new EventLogQuery("Security", PathType.LogName, query) { Session = session });
            var list = new List<string>();
            while (list.Count < most && !stop.IsCancellationRequested && reader.ReadEvent(TimeSpan.FromSeconds(30)) is { } e)
            {
                using (e)
                {
                    list.Add(e.ToXml());
                }
            }
            return list;
        }
        catch (UnauthorizedAccessException)
        {
            throw new SecurityLogProblem("denied", $"Access denied. {login.User} cannot read the Security log on {dc}.");
        }
        catch (EventLogException e) when (e.Message.Contains("RPC", StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityLogProblem("unreachable", $"{dc} could not be reached. Check that it is online and that the firewall allows remote event log management.");
        }
        catch (EventLogException e)
        {
            throw new SecurityLogProblem("error", $"The Security log on {dc} could not be read: {e.Message.Trim().TrimEnd('.')}.");
        }
    }, stop);
}

/// <summary>In Docker or on Linux there's no event log to read.</summary>
public sealed class NoSecurityLog : ISecurityLog
{
    public Task<IReadOnlyList<string>> Read(string host, AuditLogin login, string query, int most, CancellationToken stop) =>
        throw new SecurityLogProblem("unsupported", $"Security logs are read only when {Product.Title} runs on Windows.");
}

/// <summary>The service account's sign-in and the domain's DCs, for reading their Security logs.</summary>
public sealed record AuditLogin(string Domain, string User, string Password, IReadOnlyList<SyncDc> Dcs);

/// <summary>What the audit asks of Active Directory. DirectoryTools does it; tests stand in for it.</summary>
public interface IAuditDirectory
{
    /// <summary>The sign-in and the DCs, or null without a service account (read-only mode has no password to read the logs with).</summary>
    Task<AuditLogin?> AuditLogin(CancellationToken stop);

    /// <summary>Display names by username, for Modified by.</summary>
    Task<Dictionary<string, string>> DisplayNames(IReadOnlyCollection<string> usernames, CancellationToken stop);

    /// <summary>The service account's username: what it changed was changed by someone in Rabbid Lab.</summary>
    string? ServiceAccount { get; }
}

public sealed partial class DirectoryTools : IAuditDirectory
{
    public string? ServiceAccount => Load() is { Account: { } account, Domain: not null, Scope: not null } ? account : null;

    public async Task<AuditLogin?> AuditLogin(CancellationToken stop)
    {
        var f = Load();
        if (f.Account is null || f.Domain is null || f.Scope is null || secrets.TryDecrypt(f.Password) is not { } password)
        {
            return null;
        }
        var (_, dcs) = await SyncDcs(stop);
        return new AuditLogin(f.Domain, f.Account, password, dcs);
    }

    public async Task<Dictionary<string, string>> DisplayNames(IReadOnlyCollection<string> usernames, CancellationToken stop)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (usernames.Count == 0)
        {
            return found;
        }
        using var ldap = await Open(stop);
        foreach (var batch in usernames.Chunk(40))
        {
            var filter = $"(|{string.Concat(batch.Select(u => $"(sAMAccountName={ActiveDirectory.Escape(u)})"))})";
            foreach (var r in await ldap.Find(ldap.BaseDn, filter, true, ["sAMAccountName", "displayName"], 100, stop))
            {
                if (r.Text("sAMAccountName") is { } sam && r.Text("displayName") is { Length: > 0 } name)
                {
                    found[sam] = name;
                }
            }
        }
        return found;
    }
}

/// <summary>Where each DC's Security log was read to, and how the last read went (ad-audit-state.json).</summary>
public sealed class AuditStateFile
{
    public Dictionary<string, AuditDcState> Dcs { get; set; } = [];
}

public sealed class AuditDcState
{
    public const string Read = "read";
    public const string Waiting = "waiting";

    public long Last { get; set; }
    public DateTime? LastAt { get; set; }
    public DateTime? FirstRead { get; set; }
    public DateTime? LastRead { get; set; }
    public DateTime? Checked { get; set; }
    public string State { get; set; } = Waiting;
    public string? Problem { get; set; }
    public int Fails { get; set; }
    public DateTime? NextTry { get; set; }
}

/// <summary>The changes read from Security logs: ad-audit.jsonl, one line each, kept in memory oldest first, for the days kept.</summary>
public sealed class AuditStore(DataFolder folder, TimeProvider clock, ILogger<AuditStore> log)
{
    /// <summary>The most kept, however many days: the oldest go first.</summary>
    public const int Most = 250_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string file = Path.Combine(folder.Path, "ad-audit.jsonl");
    private readonly Lock gate = new();
    private readonly HashSet<string> ids = new(StringComparer.Ordinal);
    private List<AuditEntry>? all;
    private DateTime pruned;

    /// <summary>Adds the ones not kept yet, and says how many that was.</summary>
    public int Add(IEnumerable<AuditEntry> entries)
    {
        lock (gate)
        {
            var list = Loaded();
            var news = entries.Where(e => ids.Add(e.Id)).ToList();
            if (news.Count == 0)
            {
                return 0;
            }
            try
            {
                File.AppendAllText(file, string.Concat(news.Select(e => JsonSerializer.Serialize(e, Json) + "\n")));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.LogWarning(e, "The AD audit could not be written");
            }
            var inOrder = (list.Count == 0 || news[0].At >= list[^1].At) && news.Zip(news.Skip(1)).All(p => p.First.At <= p.Second.At);
            list.AddRange(news);
            if (!inOrder)
            {
                list.Sort((a, b) => a.At.CompareTo(b.At));
            }
            return news.Count;
        }
    }

    /// <summary>Drops what's older than the days kept, once a day (now: straight away, after the days change).</summary>
    public void Prune(int days, bool now = false)
    {
        lock (gate)
        {
            var at = clock.GetUtcNow().UtcDateTime;
            if (!now && at - pruned < TimeSpan.FromDays(1))
            {
                return;
            }
            pruned = at;
            var list = Loaded();
            var cut = Math.Max(First(list, at.AddDays(-days)), list.Count - Most);
            if (cut <= 0)
            {
                return;
            }
            foreach (var e in list.Take(cut))
            {
                ids.Remove(e.Id);
            }
            list.RemoveRange(0, cut);
            try
            {
                var temp = file + ".tmp";
                File.WriteAllLines(temp, list.Select(e => JsonSerializer.Serialize(e, Json)));
                File.Move(temp, file, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.LogWarning(e, "The AD audit could not be pruned");
            }
        }
    }

    /// <summary>Everything since then, oldest first.</summary>
    public List<AuditEntry> Since(DateTime at)
    {
        lock (gate)
        {
            var list = Loaded();
            var from = First(list, at);
            return list.GetRange(from, list.Count - from);
        }
    }

    /// <summary>One object's changes since then: by its DN, and by its GUID, which a move or rename doesn't change.</summary>
    public List<AuditEntry> Of(string dn, string? guid, DateTime since)
    {
        lock (gate)
        {
            var list = Loaded();
            var from = First(list, since);
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (guid is not null)
            {
                guids.Add(guid);
            }
            for (var i = from; i < list.Count; i++)
            {
                if (list[i].Guid is { } g && string.Equals(list[i].Dn, dn, StringComparison.OrdinalIgnoreCase))
                {
                    guids.Add(g);
                }
            }
            return [.. list.Skip(from).Where(e => e.Guid is { } g ? guids.Contains(g) : string.Equals(e.Dn, dn, StringComparison.OrdinalIgnoreCase))];
        }
    }

    // The first one at or after then.
    private static int First(List<AuditEntry> list, DateTime at)
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (list[mid].At < at)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        return lo;
    }

    // Read once; a line cut off or zero-filled after a crash is skipped.
    private List<AuditEntry> Loaded()
    {
        if (all is not null)
        {
            return all;
        }
        all = [];
        try
        {
            if (File.Exists(file))
            {
                foreach (var line in File.ReadLines(file))
                {
                    var text = line.Trim('\0', ' ', '\r');
                    try
                    {
                        if (text.Length > 0 && JsonSerializer.Deserialize<AuditEntry>(text, Json) is { Id: not null } e && ids.Add(e.Id))
                        {
                            all.Add(e);
                        }
                    }
                    catch (JsonException)
                    {
                        // Skipped.
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(e, "The AD audit could not be read");
        }
        all.Sort((a, b) => a.At.CompareTo(b.At));
        return all;
    }
}

/// <summary>
/// Reads every writable DC's Security log every few minutes, from where it got to last time: up to 2,000 events a read,
/// five reads a round while they come back full. A DC that fails is tried again later and later, up to an hour.
/// </summary>
public sealed class AuditCollector(IAuditDirectory ad, ISecurityLog logs, AuditStore store, SettingsStore settings, DataFolder folder, TimeProvider clock,
    ILogger<AuditCollector> log) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(3);
    public const int Batch = 2000;
    private const int Batches = 5;
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5); // an operation's events are written within this
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(15); // Refresh reads again only after this
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(2); // one read of one DC
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<AuditStateFile> file = folder.Json<AuditStateFile>("ad-audit-state.json", Json);
    private readonly SemaphoreSlim one = new(1, 1);
    private readonly ConcurrentDictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase); // display names by username, "" when there's none
    private DateTime namesAt;
    private DateTime lastRound;

    /// <summary>
    /// Every event read from a DC's Security log, a batch at a time: the one place to listen for what DCs record. Alerts
    /// (failed sign-ins, lockouts, deletes) subscribe here, adding the event IDs they need to SecurityEvents.Watched.
    /// </summary>
    public event Action<IReadOnlyList<SecurityEvent>>? Seen;

    /// <summary>How many days changes are kept: 30, 90 or 365.</summary>
    public int Keep => Keeps(settings.Current.AuditDays);

    public static int Keeps(int days) => days is 30 or 90 or 365 ? days : 90;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await Round(stop);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogWarning(e, "Reading the domain controllers' Security logs failed");
                }
                await Task.Delay(Every, clock, stop);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    /// <summary>Each DC's state as last read, by name.</summary>
    public IReadOnlyList<(string Name, AuditDcState State)> Dcs() => [.. file.Read().Dcs.OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase).Select(d => (d.Key, d.Value))];

    /// <summary>One round of every DC. stale: only when the last one is more than a few seconds old (Refresh).</summary>
    public async Task Round(CancellationToken stop, bool stale = false)
    {
        await one.WaitAsync(stop);
        if (stale && Now - lastRound < Fresh)
        {
            one.Release();
            return;
        }
        try
        {
            var keep = Keep;
            store.Prune(keep);
            if (await ad.AuditLogin(stop) is not { } login)
            {
                return;
            }
            // A change is made, and recorded, on a writable DC: read-only ones have nothing to say.
            var dcs = login.Dcs.Where(d => !d.ReadOnly).ToList();
            var f = JsonSerializer.Deserialize<AuditStateFile>(JsonSerializer.Serialize(file.Read(), Json), Json) ?? new();
            f.Dcs = f.Dcs.Where(d => dcs.Any(x => x.Name.Equals(d.Key, StringComparison.OrdinalIgnoreCase))).ToDictionary(d => d.Key.ToUpperInvariant(), d => d.Value);
            foreach (var dc in dcs)
            {
                var s = f.Dcs.GetValueOrDefault(dc.Name) ?? new AuditDcState();
                f.Dcs[dc.Name] = s;
                if (s.NextTry > Now)
                {
                    continue;
                }
                try
                {
                    await ReadDc(dc, login, s, keep, stop);
                    s.State = AuditDcState.Read;
                    s.Problem = null;
                    s.Fails = 0;
                    s.NextTry = null;
                    s.FirstRead ??= Now;
                    s.LastRead = Now;
                }
                catch (SecurityLogProblem e)
                {
                    Failed(s, e.State, e.Message);
                }
                catch (TimeoutException)
                {
                    Failed(s, "unreachable", $"{dc.Name} did not answer within 2 minutes.");
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogWarning(e, "Reading the Security log on {Dc} failed", dc.Name);
                    Failed(s, "error", $"The Security log on {dc.Name} could not be read: {e.Message.Trim().TrimEnd('.')}.");
                }
                file.Save(f);
            }
            file.Save(f);
        }
        finally
        {
            lastRound = Now; // when it finished: Refresh straight after one reads nothing new
            one.Release();
        }
    }

    private void Failed(AuditDcState s, string state, string problem)
    {
        s.Fails++;
        s.State = state;
        s.Problem = problem;
        // 3, 6, 12, 24, 48 minutes, then every hour; without Windows, once an hour.
        s.NextTry = Now + (state == "unsupported" ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(Math.Min(60, 3 * Math.Pow(2, Math.Min(s.Fails - 1, 5)))));
    }

    private async Task ReadDc(SyncDc dc, AuditLogin login, AuditDcState s, int keep, CancellationToken stop)
    {
        for (var i = 0; i < Batches; i++)
        {
            var since = s.LastAt ?? Now.AddDays(-keep);
            var xml = await Ask(dc, login, SecurityEvents.Query(SecurityEvents.Watched, s.Last, since), Batch, stop);
            var events = xml.Select(x => SecurityEvents.Parse(x, dc.Name)).OfType<SecurityEvent>().OrderBy(e => e.Record).ToList();
            var take = Settled(events, xml.Count >= Batch);
            if (take.Count == 0)
            {
                // Nothing new for an hour: the log may have been cleared, and its numbers started again.
                if (s.Last > 0 && s.LastAt is { } lastAt && !(Now - s.Checked < TimeSpan.FromHours(1)))
                {
                    s.Checked = Now;
                    if ((await Ask(dc, login, SecurityEvents.Cleared(s.Last, lastAt), 1, stop)).Count > 0)
                    {
                        s.Last = 0;
                    }
                }
                return;
            }
            store.Add(await Named(AuditEntries.From(take), stop));
            Tell(take);
            s.Last = take[^1].Record;
            s.LastAt = take.Max(e => e.At);
            if (xml.Count < Batch || take.Count < events.Count)
            {
                return;
            }
        }
    }

    private async Task<IReadOnlyList<string>> Ask(SyncDc dc, AuditLogin login, string query, int most, CancellationToken stop) =>
        await logs.Read(dc.Host, login, query, most, stop).WaitAsync(Longest, stop);

    // An operation's last events may still be being written, or cut off by the end of a full batch: the last operation
    // waits for the next read, unless it's the whole of a full batch.
    private List<SecurityEvent> Settled(List<SecurityEvent> events, bool full)
    {
        if (events.Count == 0 || (!full && Now - events[^1].At >= Settle))
        {
            return events;
        }
        var last = AuditEntries.OpOf(events[^1]);
        var cut = events.FindLastIndex(e => AuditEntries.OpOf(e) != last) + 1;
        return cut > 0 ? events[..cut] : full ? events : [];
    }

    // Modified by in words: each account's display name, asked once a day.
    private async Task<List<AuditEntry>> Named(List<AuditEntry> entries, CancellationToken stop)
    {
        if (Now - namesAt > TimeSpan.FromDays(1))
        {
            names.Clear();
            namesAt = Now;
        }
        var wanted = entries.Select(e => e.Account).OfType<string>().Select(Ldap.Account).Where(u => !u.EndsWith('$') && !names.ContainsKey(u))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count > 0)
        {
            try
            {
                foreach (var (user, name) in await ad.DisplayNames(wanted, stop))
                {
                    names[user] = name;
                }
            }
            catch (Exception e) when (e is DirectoryProblem or ToolProblem or LdapRefused)
            {
                // Their usernames, then.
            }
            foreach (var user in wanted)
            {
                names.TryAdd(user, "");
            }
        }
        return [.. entries.Select(e => e.Account is { } a && names.TryGetValue(Ldap.Account(a), out var n) && n.Length > 0 ? e with { ByName = n } : e)];
    }

    private void Tell(IReadOnlyList<SecurityEvent> events)
    {
        try
        {
            Seen?.Invoke(events);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Something listening for Security log events failed");
        }
    }
}

public sealed record AuditKeepForm(int? Days);

public static class AuditApi
{
    /// <summary>The most rows the page or a History tab is sent: the newest.</summary>
    public const int Most = 2000;

    public static void AddAudit(this IServiceCollection services)
    {
        services.AddSingleton<ISecurityLog>(_ => OperatingSystem.IsWindows() ? new WindowsSecurityLog() : new NoSecurityLog());
        services.AddSingleton<IAuditDirectory>(s => s.GetRequiredService<DirectoryTools>());
        services.AddSingleton<AuditStore>();
        services.AddSingleton<AuditCollector>();
        services.AddHostedService(s => s.GetRequiredService<AuditCollector>());
    }

    public static void MapAuditApi(this WebApplication app)
    {
        // Every attribute change with its old and new value, who made it and on which DC, for the days kept, and how each DC
        // is recording. again=true reads the Security logs first (Refresh).
        app.MapGet("/api/ad/audit", (int? days, bool? again, HttpContext context, IDirectoryExtras tools, AuditStore store, AuditCollector collector, IAuditDirectory ad,
            ActivityLog log) => UsersApi.Try(context, Permissions.AdView, async () =>
        {
            var keep = collector.Keep;
            var span = Math.Clamp(days ?? keep, 1, keep);
            var (changes, more) = await tools.Changes(span, context.RequestAborted);
            if (again == true)
            {
                await collector.Round(context.RequestAborted, stale: true);
            }
            // The newest events are all that can make the newest rows.
            var events = store.Since(DateTime.UtcNow.AddDays(-span));
            var rows = AuditView.Rows(events.Count > Most ? events[^Most..] : events, changes, Ours(log), ad.ServiceAccount);
            var dcs = AuditView.Dcs(collector.Dcs(), rows);
            // account: the logs are read as the service account, so read-only mode reads none.
            return Results.Ok(new { rows = rows.Take(Most), dcs, keep, more = more || events.Count > Most || rows.Count > Most, windows = OperatingSystem.IsWindows(),
                account = ad.ServiceAccount is not null });
        }, change: false, whole: true));

        // One object's changes, for its History tab: inside the caller's roles, as the panel is.
        app.MapGet("/api/ad/history", (string? dn, HttpContext context, IDirectoryExtras tools, AuditStore store, AuditCollector collector, IAuditDirectory ad, ActivityLog log,
            ChangeJournal journal) => UsersApi.Try(context, Permissions.AdView, async () =>
        {
            if (string.IsNullOrWhiteSpace(dn))
            {
                return Results.BadRequest(new { error = "Select an object to show its history." });
            }
            var keep = collector.Keep;
            var now = await tools.ChangesOf(dn, keep, context.RequestAborted);
            var rows = AuditView.Rows(store.Of(dn.Trim(), now?.Guid, DateTime.UtcNow.AddDays(-keep)), now is null ? [] : [now], Ours(log), ad.ServiceAccount).Take(Most).ToList();
            // Whether each change made in Rabbid Lab can be undone by whoever asks, or was, as the Activity log says it.
            var changes = rows.Select(r => r.Change).OfType<string>().Distinct().Select(journal.Find).OfType<Change>().ToDictionary(c => c.Id, c => journal.Brief(context, c));
            return Results.Ok(new { rows, keep, changes });
        }, change: false));

        // How long changes are kept, for everyone.
        app.MapPost("/api/ad/audit/keep", (AuditKeepForm form, HttpContext context, SettingsStore settings, AuditStore store, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AdConnect) is { } refused)
            {
                return refused;
            }
            if (form.Days is not ({ } days and (30 or 90 or 365)))
            {
                return Results.BadRequest(new { error = "Select 30 days, 90 days or 1 year." });
            }
            settings.Change(s => s.AuditDays = days);
            store.Prune(days, now: true);
            log.Record(context, "users", "ad.audit.keep", $"Set the AD audit to keep changes for {(days == 365 ? "1 year" : $"{days} days")}");
            return Results.Ok(new { keep = days });
        });
    }

    // Rabbid Lab's own Active Directory changes in its activity log.
    private static List<ActivityEntry> Ours(ActivityLog log) => [.. log.Recent(1000).Where(e => e.Area == "users" && e.Action.StartsWith("ad.", StringComparison.Ordinal))];
}
