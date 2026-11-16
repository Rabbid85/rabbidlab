using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>A GPO as the list shows it. Status is its flags: 0 on, 1 user settings off, 2 computer settings off, 3 both off.</summary>
public sealed record GpoView(string Id, string Name, int Status, int ComputerVersion, int UserVersion, string? Wmi, DateTime? Created, DateTime? Changed,
    bool HasComputer, bool HasUser);

/// <summary>The domain or an OU, the GPOs linked to it in the order they apply (1 wins), and whether it blocks inheritance.</summary>
public sealed record GpoPlaceView(string Dn, string Name, string Path, bool Domain, bool Blocked, List<GpoLinkView> Links);

public sealed record GpoLinkView(string Gpo, int Order, bool Enabled, bool Enforced);

public sealed record WmiFilterView(string Id, string Name, string? Description, string Space, string Query, int Queries, string? Author, DateTime? Changed);

public sealed record GpoBackupView(string Id, string Gpo, string Name, DateTime At, string? By, string? Comment);

/// <summary>Everything the Group Policy page lists. Gpmc: Group Policy Management is on this server (settings, delegation, new, copy, delete, backups).</summary>
public sealed record GroupPolicyView(string Domain, string Account, bool Gpmc, List<GpoView> Gpos, List<GpoPlaceView> Places, List<WmiFilterView> Filters, List<GpoBackupView> Backups);

/// <summary>One GPO's settings and delegation, from its settings report, with its versions in SYSVOL.</summary>
public sealed record GpoDetail(string Id, List<GpoSetting> Computer, List<GpoSetting> User, List<GpoTrustee> Who, string? Owner, int? SysvolComputer, int? SysvolUser);

public sealed record GpoSetting(string Area, string Name, string Value);

/// <summary>Someone a GPO's permissions name. Access: apply, read, edit, full or custom.</summary>
public sealed record GpoTrustee(string Name, string? Sid, string Access, bool Denied, bool Inherited);

/// <summary>A link to make or change. Order is the place it moves to, 1 first.</summary>
public sealed record GpoLinkChange(bool Add = false, bool Remove = false, bool? Enabled = null, bool? Enforced = null, int? Order = null);

/// <summary>A domain admin's login for one change, when Rabbid Lab's own account isn't allowed. Never kept.</summary>
public sealed record GpoLogin(string? Username, string? Password)
{
    public bool Given => !string.IsNullOrWhiteSpace(Username);
}

/// <summary>Rabbid Lab's own account isn't allowed to make this change: the page asks for a domain admin's login and tries again.</summary>
public sealed class GpoNeedsLogin(string message) : Exception(message);

public interface IGroupPolicy
{
    Task<GroupPolicyView> GroupPolicy(CancellationToken stop);

    Task<GpoDetail> GpoDetail(string id, CancellationToken stop);

    /// <summary>The settings report as Group Policy Management makes it, in HTML, and the GPO's name.</summary>
    Task<(string Html, string Name)> GpoReport(string id, CancellationToken stop);

    /// <summary>The new GPO's ID, and what didn't work (the link), if anything.</summary>
    Task<(string Id, string? Warning)> NewGpo(string name, string? from, string? link, GpoLogin login, string by, CancellationToken stop);

    Task RenameGpo(string id, string name, GpoLogin login, CancellationToken stop);

    Task SetGpoStatus(string id, bool computer, bool user, GpoLogin login, CancellationToken stop);

    Task SetGpoWmi(string id, string? filter, GpoLogin login, CancellationToken stop);

    Task SetGpoApply(string id, string trustee, bool apply, GpoLogin login, CancellationToken stop);

    Task DeleteGpo(string id, GpoLogin login, string by, CancellationToken stop);

    Task<GpoBackupView> BackupGpo(string id, string? comment, GpoLogin login, string by, CancellationToken stop);

    /// <summary>How many were backed up, and the ones that weren't.</summary>
    Task<(int Done, List<string> Failed)> BackupAll(string? comment, GpoLogin login, string by, CancellationToken stop);

    /// <summary>The restored GPO's ID.</summary>
    Task<string> RestoreGpo(string backup, GpoLogin login, string by, CancellationToken stop);

    Task Link(string gpo, string target, GpoLinkChange change, GpoLogin login, CancellationToken stop);

    Task SetBlocked(string target, bool blocked, GpoLogin login, CancellationToken stop);

    /// <summary>The filter's ID: a new one when id is empty.</summary>
    Task<string> SaveWmiFilter(string? id, string name, string? description, string query, string? space, GpoLogin login, string by, CancellationToken stop);

    Task DeleteWmiFilter(string id, GpoLogin login, CancellationToken stop);

    /// <summary>A GPO's name for the activity log, or its ID when it can't be read.</summary>
    Task<string> GpoName(string id, CancellationToken stop);

    /// <summary>Where a DN is, readably: amw.com/AMW/Computers.</summary>
    string PlaceName(string dn);
}

/// <summary>The gPLink attribute: [LDAP://cn={GUID},cn=policies,...;flags] for each link, the one that applies last (link order 1) at the end.</summary>
public static partial class GpoLinks
{
    public const int Disabled = 1;
    public const int Enforced = 2;

    /// <summary>One link as written (Raw keeps a link to another domain's GPO as it was), its GPO's ID and its flags.</summary>
    public sealed record Entry(string Raw, string? Gpo, int Flags);

    /// <summary>The links in link order: 1 (applies last, wins) first.</summary>
    public static List<Entry> Parse(string? gPLink)
    {
        var list = new List<Entry>();
        foreach (Match m in Item().Matches(gPLink ?? ""))
        {
            var path = m.Groups[1].Value;
            var id = Guid().Match(path);
            list.Add(new Entry(path, id.Success ? id.Value.ToUpperInvariant() : null, int.TryParse(m.Groups[2].Value, CultureInfo.InvariantCulture, out var f) ? f : 0));
        }
        list.Reverse();
        return list;
    }

    /// <summary>The attribute for these links (in link order), or a single space for none, as Group Policy Management leaves it.</summary>
    public static string Build(IEnumerable<Entry> inOrder)
    {
        var s = string.Concat(inOrder.Reverse().Select(e => $"[{e.Raw};{e.Flags.ToString(CultureInfo.InvariantCulture)}]"));
        return s.Length == 0 ? " " : s;
    }

    public static string PathOf(string gpo, string baseDn) => $"LDAP://cn={gpo},cn=policies,cn=system,{baseDn}";

    [GeneratedRegex(@"\[(LDAP://[^;\]]+);(\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex Item();

    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
    public static partial Regex Guid();
}

/// <summary>A WMI filter's queries as Active Directory keeps them (msWMI-Parm2): 1;3;10;66;WQL;root\CIMv2;SELECT ...;</summary>
public static class WmiQueries
{
    public const string DefaultSpace = @"root\CIMv2";

    /// <summary>Each query's namespace and text; none when it can't be read.</summary>
    public static List<(string Space, string Query)> Parse(string? parm2)
    {
        var list = new List<(string, string)>();
        var s = parm2 ?? "";
        var at = 0;
        string? Field()
        {
            var end = s.IndexOf(';', at);
            if (end < 0)
            {
                return null;
            }
            var v = s[at..end];
            at = end + 1;
            return v;
        }
        string? Fixed(int length)
        {
            if (at + length > s.Length)
            {
                return null;
            }
            var v = s.Substring(at, length);
            at += length + 1; // and its ;
            return v;
        }
        if (!int.TryParse(Field(), CultureInfo.InvariantCulture, out var count))
        {
            return list;
        }
        for (var i = 0; i < count; i++)
        {
            if (!int.TryParse(Field(), CultureInfo.InvariantCulture, out var langLength) || !int.TryParse(Field(), CultureInfo.InvariantCulture, out var spaceLength)
                || !int.TryParse(Field(), CultureInfo.InvariantCulture, out var queryLength) || Fixed(langLength) is null
                || Fixed(spaceLength) is not { } space || Fixed(queryLength) is not { } query)
            {
                break;
            }
            list.Add((space, query));
        }
        return list;
    }

    public static string Build(string space, string query) =>
        string.Create(CultureInfo.InvariantCulture, $"1;3;{space.Length};{query.Length};WQL;{space};{query};");

    /// <summary>When WMI wrote it: 20211125062907.000000-000.</summary>
    public static DateTime? Date(string? value) =>
        value is { Length: >= 14 } && DateTime.TryParseExact(value[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    public static string Date(DateTime at) => at.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ".000000-000";
}

/// <summary>
/// Group Policy, as Rabbid Lab's own AD account (or a domain admin's login for one change when it isn't allowed). Links,
/// names, status and WMI filters are attributes in Active Directory, changed here directly as Group Policy Management
/// changes them. New GPOs, copies, deletes, backups, restores, settings reports and who a GPO applies to go through
/// Group Policy Management (Gpmc) on this server, so SYSVOL and its permissions stay as Windows expects.
/// </summary>
public sealed partial class DirectoryTools : IGroupPolicy
{
    /// <summary>Group Policy Management on this server. Tests give their own.</summary>
    public IGpmc Gpmc { get; set; } = OperatingSystem.IsWindows() ? new WindowsGpmc() : new NoGpmc();

    private static readonly string[] GpoAttributes = ["cn", "displayName", "flags", "versionNumber", "gPCWQLFilter", "whenCreated", "whenChanged",
        "gPCMachineExtensionNames", "gPCUserExtensionNames"];

    private static readonly string[] FilterAttributes = ["msWMI-ID", "msWMI-Name", "msWMI-Parm1", "msWMI-Parm2", "msWMI-Author", "msWMI-ChangeDate"];

    // The two GPOs every domain has, which Windows needs.
    private static readonly HashSet<string> BuiltInGpos = new(StringComparer.OrdinalIgnoreCase) { "{31B2F340-016D-11D2-945F-00C04FB984F9}", "{6AC1786C-016F-11D2-945F-00C04FB984F9}" };

    private const string NoGpmcHere = "This needs Group Policy Management on this server. On Windows 10 or 11, run in PowerShell as administrator: "
        + "Add-WindowsCapability -Online -Name Rsat.GroupPolicy.Management.Tools~~~~0.0.1.0. On Windows Server: Install-WindowsFeature GPMC.";

    private readonly Dictionary<string, (string Key, GpoDetail Detail)> details = [];

    private string BackupFolder => System.IO.Path.Combine(folder.Path, "gpo-backups");

    private string BackupIndex => System.IO.Path.Combine(folder.Path, "gpo-backups.json");

    private static string Policies(string baseDn) => $"CN=Policies,CN=System,{baseDn}";

    private static string Som(string baseDn) => $"CN=SOM,CN=WMIPolicy,CN=System,{baseDn}";

    public async Task<GroupPolicyView> GroupPolicy(CancellationToken stop)
    {
        var f = Ready();
        using var ldap = await Open(stop);
        var b = ldap.BaseDn;
        var gpos = (await ldap.Find(Policies(b), "(objectClass=groupPolicyContainer)", true, GpoAttributes, Limit, stop)).Select(Gpo).OfType<GpoView>()
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var places = new List<GpoPlaceView>();
        if ((await ldap.Find(b, "(objectClass=*)", false, ["gPLink", "gPOptions"], 1, stop)).FirstOrDefault() is { } root)
        {
            places.Add(Place(root, b, f.Domain!, true));
        }
        places.AddRange((await ldap.Find(b, "(objectClass=organizationalUnit)", true, ["gPLink", "gPOptions"], Limit, stop))
            .Select(r => Place(r, b, f.Domain!, false)).OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase));
        var filters = (await ldap.Find(Som(b), "(objectClass=msWMI-Som)", true, FilterAttributes, Limit, stop)).Select(Filter).OfType<WmiFilterView>()
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new GroupPolicyView(f.Domain!, f.Account!, Gpmc.Here, gpos, places, filters, Backups());
    }

    private static GpoView? Gpo(LdapRow r)
    {
        if (r.Text("cn") is not { } cn || !GpoLinks.Guid().IsMatch(cn))
        {
            return null;
        }
        var version = r.Number("versionNumber") ?? 0;
        return new GpoView(cn.ToUpperInvariant(), r.Text("displayName") ?? cn, (int) ((r.Number("flags") ?? 0) & 3), (int) (version & 0xFFFF), (int) ((version >> 16) & 0xFFFF),
            GpoLinks.Guid().Match(r.Text("gPCWQLFilter") ?? "") is { Success: true } w ? w.Value.ToUpperInvariant() : null,
            When(r.Text("whenCreated")), When(r.Text("whenChanged")),
            !string.IsNullOrWhiteSpace(r.Text("gPCMachineExtensionNames")), !string.IsNullOrWhiteSpace(r.Text("gPCUserExtensionNames")));
    }

    private static GpoPlaceView Place(LdapRow r, string baseDn, string domain, bool isDomain)
    {
        var path = Ldap.Path(r.Dn, baseDn).Replace(" / ", "/", StringComparison.Ordinal);
        var links = GpoLinks.Parse(r.Text("gPLink")).Select((e, i) => (e, i)).Where(x => x.e.Gpo is not null)
            .Select(x => new GpoLinkView(x.e.Gpo!, x.i + 1, (x.e.Flags & GpoLinks.Disabled) == 0, (x.e.Flags & GpoLinks.Enforced) != 0)).ToList();
        return new GpoPlaceView(r.Dn, isDomain ? domain : Ldap.Name(r.Dn), isDomain ? domain : $"{domain}/{path}", isDomain, ((r.Number("gPOptions") ?? 0) & 1) != 0, links);
    }

    private static WmiFilterView? Filter(LdapRow r)
    {
        if (r.Text("msWMI-ID") is not { } id)
        {
            return null;
        }
        var queries = WmiQueries.Parse(r.Text("msWMI-Parm2"));
        return new WmiFilterView(id.ToUpperInvariant(), r.Text("msWMI-Name") ?? id, Words.Blank(r.Text("msWMI-Parm1")), queries.FirstOrDefault().Space ?? WmiQueries.DefaultSpace,
            queries.FirstOrDefault().Query ?? "", queries.Count, Words.Blank(r.Text("msWMI-Author")), WmiQueries.Date(r.Text("msWMI-ChangeDate")));
    }

    // --- Settings, delegation and the report: Group Policy Management, as Rabbid Lab's own account -----------------

    public async Task<GpoDetail> GpoDetail(string id, CancellationToken stop)
    {
        var g = await FindGpo(id, stop);
        var key = $"{g.ComputerVersion}.{g.UserVersion}.{g.Changed?.Ticks}";
        lock (details)
        {
            if (details.TryGetValue(g.Id, out var had) && had.Key == key)
            {
                return had.Detail;
            }
        }
        var xml = await Gpm(new GpoLogin(null, null), t => Gpmc.Report(t, g.Id, false, stop), stop, reads: true);
        var detail = GpoReports.Parse(g.Id, xml);
        lock (details)
        {
            details[g.Id] = (key, detail);
        }
        return detail;
    }

    public async Task<(string Html, string Name)> GpoReport(string id, CancellationToken stop)
    {
        var g = await FindGpo(id, stop);
        return (await Gpm(new GpoLogin(null, null), t => Gpmc.Report(t, g.Id, true, stop), stop, reads: true), g.Name);
    }

    private async Task<GpoView> FindGpo(string id, CancellationToken stop)
    {
        var guid = GuidOf(id);
        using var ldap = await Open(stop);
        return (await ldap.Find($"CN={guid},{Policies(ldap.BaseDn)}", "(objectClass=groupPolicyContainer)", false, GpoAttributes, 1, stop)).Select(Gpo).OfType<GpoView>().FirstOrDefault()
            ?? throw new ToolProblem("That GPO isn't in the domain any more.", StatusCodes.Status404NotFound);
    }

    public async Task<string> GpoName(string id, CancellationToken stop)
    {
        try
        {
            return (await FindGpo(id, stop)).Name;
        }
        catch (ToolProblem)
        {
            return id;
        }
    }

    public string PlaceName(string dn)
    {
        var domain = Load().Domain ?? "";
        var path = Ldap.Path(dn ?? "", BaseOf(domain)).Replace(" / ", "/", StringComparison.Ordinal);
        return path.Length == 0 ? domain : $"{domain}/{path}";
    }

    private static string GuidOf(string? id) =>
        id is not null && GpoLinks.Guid().Match(id.Trim()) is { Success: true } m && m.Length == id.Trim().Length ? m.Value.ToUpperInvariant()
        : throw new ToolProblem("That isn't a GPO's ID.");

    // --- Changes in Active Directory ------------------------------------------------------------------------------

    public Task RenameGpo(string id, string name, GpoLogin login, CancellationToken stop) => Change(login, async (ldap, f) =>
    {
        var n = GpoName(name);
        var g = await GpoRow(ldap, id, stop);
        await Taken(ldap, n, g.Dn, stop);
        await ldap.Modify(g.Dn, [new(LdapChange.Replace, "displayName", n)], stop);
    }, stop);

    public Task SetGpoStatus(string id, bool computer, bool user, GpoLogin login, CancellationToken stop) => Change(login, async (ldap, f) =>
    {
        var g = await GpoRow(ldap, id, stop);
        var flags = (user ? 0 : 1) | (computer ? 0 : 2);
        await ldap.Modify(g.Dn, [new(LdapChange.Replace, "flags", flags.ToString(CultureInfo.InvariantCulture))], stop);
    }, stop);

    public Task SetGpoWmi(string id, string? filter, GpoLogin login, CancellationToken stop) => Change(login, async (ldap, f) =>
    {
        var g = await GpoRow(ldap, id, stop);
        if (string.IsNullOrWhiteSpace(filter))
        {
            if (g.Text("gPCWQLFilter") is not null)
            {
                await ldap.Modify(g.Dn, [new(LdapChange.Delete, "gPCWQLFilter")], stop);
            }
            return;
        }
        var w = GuidOf(filter);
        if ((await ldap.Find(Som(ldap.BaseDn), $"(&(objectClass=msWMI-Som)(msWMI-ID={ActiveDirectory.Escape(w)}))", true, ["msWMI-ID"], 1, stop)).Count == 0)
        {
            throw new ToolProblem("That WMI filter isn't in the domain any more.");
        }
        await ldap.Modify(g.Dn, [new(LdapChange.Replace, "gPCWQLFilter", $"[{f.Domain};{w};0]")], stop);
    }, stop);

    public Task Link(string gpo, string target, GpoLinkChange change, GpoLogin login, CancellationToken stop) => Change(login, async (ldap, f) =>
    {
        var id = GuidOf(gpo);
        if (change.Add)
        {
            await GpoRow(ldap, id, stop);
        }
        var t = await Target(ldap, target, stop);
        var links = GpoLinks.Parse(t.Text("gPLink"));
        var at = links.FindIndex(e => string.Equals(e.Gpo, id, StringComparison.OrdinalIgnoreCase));
        if (change.Add)
        {
            if (at >= 0)
            {
                throw new ToolProblem("It's already linked there.");
            }
            // A new link goes last, as Group Policy Management adds it: it applies first, so the ones there already win.
            links.Add(new GpoLinks.Entry(GpoLinks.PathOf(id, ldap.BaseDn), id, change.Enforced == true ? GpoLinks.Enforced : 0));
        }
        else
        {
            if (at < 0)
            {
                throw new ToolProblem("It isn't linked there any more.");
            }
            var e = links[at];
            links.RemoveAt(at);
            if (!change.Remove)
            {
                var flags = e.Flags;
                if (change.Enabled is { } on)
                {
                    flags = on ? flags & ~GpoLinks.Disabled : flags | GpoLinks.Disabled;
                }
                if (change.Enforced is { } enforced)
                {
                    flags = enforced ? flags | GpoLinks.Enforced : flags & ~GpoLinks.Enforced;
                }
                links.Insert(Math.Clamp((change.Order ?? at + 1) - 1, 0, links.Count), e with { Flags = flags });
            }
        }
        await ldap.Modify(t.Dn, [new(LdapChange.Replace, "gPLink", GpoLinks.Build(links))], stop);
    }, stop);

    public Task SetBlocked(string target, bool blocked, GpoLogin login, CancellationToken stop) => Change(login, async (ldap, f) =>
    {
        var t = await Target(ldap, target, stop);
        if (string.Equals(t.Dn, ldap.BaseDn, StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolProblem("The domain has nothing above it to block.");
        }
        await ldap.Modify(t.Dn, [new(LdapChange.Replace, "gPOptions", blocked ? "1" : "0")], stop);
    }, stop);

    public Task<string> SaveWmiFilter(string? id, string name, string? description, string query, string? space, GpoLogin login, string by, CancellationToken stop) => Change(login, async (ldap, f) =>
    {
        var n = Words.Blank(name) ?? throw new ToolProblem("Give the filter a name.");
        var q = System.Text.RegularExpressions.Regex.Replace(query ?? "", @"\s+", " ").Trim();
        if (!q.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase) || q.Length > 4000 || q.Contains(';', StringComparison.Ordinal))
        {
            throw new ToolProblem("The query is one WQL SELECT, such as SELECT * FROM Win32_OperatingSystem WHERE ProductType = 1.");
        }
        var ns = Words.Blank(space) ?? WmiQueries.DefaultSpace;
        if (!System.Text.RegularExpressions.Regex.IsMatch(ns, @"^root(\\[A-Za-z0-9_]+)*$"))
        {
            throw new ToolProblem(@"The namespace looks like root\CIMv2.");
        }
        var d = Words.Blank(description);
        var som = Som(ldap.BaseDn);
        var same = await ldap.Find(som, $"(&(objectClass=msWMI-Som)(msWMI-Name={ActiveDirectory.Escape(n)}))", true, ["msWMI-ID"], 2, stop);
        var now = WmiQueries.Date(DateTime.UtcNow);
        if (string.IsNullOrWhiteSpace(id))
        {
            if (same.Count > 0)
            {
                throw new ToolProblem($"There's already a WMI filter called {n}.");
            }
            var made = $"{{{System.Guid.NewGuid().ToString().ToUpperInvariant()}}}";
            var attributes = new Dictionary<string, object[]>
            {
                ["objectClass"] = ["msWMI-Som"],
                ["msWMI-ID"] = [made],
                ["msWMI-Name"] = [n],
                ["msWMI-Parm2"] = [WmiQueries.Build(ns, q)],
                ["msWMI-Author"] = [by],
                ["msWMI-ChangeDate"] = [now],
                ["msWMI-CreationDate"] = [now],
                ["showInAdvancedViewOnly"] = ["TRUE"],
            };
            if (d is not null)
            {
                attributes["msWMI-Parm1"] = [d];
            }
            await ldap.Add($"CN={made},{som}", attributes, stop);
            return made;
        }
        var w = GuidOf(id);
        var row = (await ldap.Find(som, $"(&(objectClass=msWMI-Som)(msWMI-ID={ActiveDirectory.Escape(w)}))", true, FilterAttributes, 1, stop)).FirstOrDefault()
            ?? throw new ToolProblem("That WMI filter isn't in the domain any more.", StatusCodes.Status404NotFound);
        if (same.Any(s => !string.Equals(s.Text("msWMI-ID"), w, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ToolProblem($"There's already a WMI filter called {n}.");
        }
        List<LdapMod> mods = [new(LdapChange.Replace, "msWMI-Name", n), new(LdapChange.Replace, "msWMI-Parm2", WmiQueries.Build(ns, q)), new(LdapChange.Replace, "msWMI-ChangeDate", now)];
        if (d is not null)
        {
            mods.Add(new(LdapChange.Replace, "msWMI-Parm1", d));
        }
        else if (row.Text("msWMI-Parm1") is not null)
        {
            mods.Add(new(LdapChange.Delete, "msWMI-Parm1"));
        }
        await ldap.Modify(row.Dn, mods, stop);
        return w;
    }, stop);

    public Task DeleteWmiFilter(string id, GpoLogin login, CancellationToken stop) => Change(login, async (ldap, f) =>
    {
        var w = GuidOf(id);
        var row = (await ldap.Find(Som(ldap.BaseDn), $"(&(objectClass=msWMI-Som)(msWMI-ID={ActiveDirectory.Escape(w)}))", true, ["msWMI-Name"], 1, stop)).FirstOrDefault()
            ?? throw new ToolProblem("That WMI filter isn't in the domain any more.", StatusCodes.Status404NotFound);
        var users = await ldap.Find(Policies(ldap.BaseDn), $"(&(objectClass=groupPolicyContainer)(gPCWQLFilter=*{ActiveDirectory.Escape(w)}*))", true, ["displayName"], 50, stop);
        if (users.Count > 0)
        {
            throw new ToolProblem($"{string.Join(", ", users.Select(u => u.Text("displayName")))} {(users.Count == 1 ? "uses" : "use")} it. Take it off {(users.Count == 1 ? "that GPO" : "those GPOs")} first.");
        }
        await ldap.Delete(row.Dn, stop);
        return true;
    }, stop);

    private static string GpoName(string? name)
    {
        var n = Words.Blank(name) ?? throw new ToolProblem("Give the GPO a name.");
        return n.Length > 200 ? throw new ToolProblem("That name is too long.") : n;
    }

    private static async Task Taken(ILdap ldap, string name, string? except, CancellationToken stop)
    {
        var same = await ldap.Find(Policies(ldap.BaseDn), $"(&(objectClass=groupPolicyContainer)(displayName={ActiveDirectory.Escape(name)}))", true, ["cn"], 2, stop);
        if (same.Any(s => !string.Equals(s.Dn, except, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ToolProblem($"There's already a GPO called {name}.");
        }
    }

    private static async Task<LdapRow> GpoRow(ILdap ldap, string id, CancellationToken stop) =>
        (await ldap.Find($"CN={GuidOf(id)},{Policies(ldap.BaseDn)}", "(objectClass=groupPolicyContainer)", false, GpoAttributes, 1, stop)).FirstOrDefault()
        ?? throw new ToolProblem("That GPO isn't in the domain any more.", StatusCodes.Status404NotFound);

    // The domain itself, or an OU in it.
    private static async Task<LdapRow> Target(ILdap ldap, string? dn, CancellationToken stop)
    {
        var d = Words.Blank(dn) ?? throw new ToolProblem("Pick where to link it.");
        if (!d.EndsWith(ldap.BaseDn, StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolProblem("That isn't in this domain.");
        }
        return (await ldap.Find(d, "(|(objectClass=organizationalUnit)(objectClass=domainDNS))", false, ["gPLink", "gPOptions"], 1, stop)).FirstOrDefault()
            ?? throw new ToolProblem("That OU isn't there any more.", StatusCodes.Status404NotFound);
    }

    // As Rabbid Lab's own account, or the login given; refused for rights with no login given, the page asks for one.
    private async Task<T> Change<T>(GpoLogin login, Func<ILdap, DirectoryToolsFile, Task<T>> work, CancellationToken stop)
    {
        var f = Ready();
        if (f.ReadOnly && !login.Given)
        {
            throw new GpoNeedsLogin(ReadOnlyNeedsAdmin);
        }
        using var ldap = login.Given ? await AsLogin(f, login, stop) : await Open(stop);
        try
        {
            return await work(ldap, f);
        }
        catch (LdapRefused e) when (e.Code == LdapRefused.InsufficientRights)
        {
            throw login.Given ? new ToolProblem($"{Ldap.Account(login.Username!)} isn't allowed to do that. It takes a Domain Admin, or rights given in Group Policy Management.")
                : new GpoNeedsLogin(NeedsAdmin(f));
        }
        catch (LdapRefused e)
        {
            throw new ToolProblem($"Active Directory said: {e.Message}");
        }
    }

    private Task Change(GpoLogin login, Func<ILdap, DirectoryToolsFile, Task> work, CancellationToken stop) =>
        Change(login, async (ldap, f) =>
        {
            await work(ldap, f);
            return true;
        }, stop);

    private static string ReadOnlyNeedsAdmin => $"{Product.Title} is only reading Active Directory until its service account is set up. Sign in as a domain admin to make this change.";

    private static string NeedsAdmin(DirectoryToolsFile f) =>
        $"{f.Account} isn't allowed to change Group Policy. Sign in as a domain admin to make this change.";

    private async Task<ILdap> AsLogin(DirectoryToolsFile f, GpoLogin login, CancellationToken stop)
    {
        try
        {
            return await open(f.Domain!, await Host(f.Domain!, stop), login.Username!.Trim(), login.Password ?? "", stop)
                ?? throw new GpoNeedsLogin("That username or password is wrong.");
        }
        catch (DirectoryProblem e)
        {
            throw new ToolProblem(e.Message, StatusCodes.Status503ServiceUnavailable);
        }
    }

    // --- Changes through Group Policy Management -----------------------------------------------------------------

    public async Task<(string Id, string? Warning)> NewGpo(string name, string? from, string? link, GpoLogin login, string by, CancellationToken stop)
    {
        var n = GpoName(name);
        using (var ldap = await Open(stop))
        {
            await Taken(ldap, n, null, stop);
        }
        var source = string.IsNullOrWhiteSpace(from) ? null : GuidOf(from);
        var id = await Gpm(login, t => source is null ? Gpmc.Create(t, n, stop) : Gpmc.Copy(t, source, n, stop), stop);
        if (string.IsNullOrWhiteSpace(link))
        {
            return (id, null);
        }
        try
        {
            await Link(id, link, new GpoLinkChange(Add: true), login, stop);
            return (id, null);
        }
        catch (Exception e) when (e is ToolProblem or GpoNeedsLogin)
        {
            return (id, $"It was made, but couldn't be linked: {e.Message}");
        }
    }

    public async Task DeleteGpo(string id, GpoLogin login, string by, CancellationToken stop)
    {
        var g = await FindGpo(id, stop);
        if (BuiltInGpos.Contains(g.Id))
        {
            throw new ToolProblem($"Windows needs the {g.Name}. Change its settings instead.");
        }
        // Kept first, so it can come back; then its links, then the GPO.
        await BackupGpo(g.Id, "Before it was deleted", login, by, stop);
        await Change(login, async (ldap, f) =>
        {
            foreach (var t in await ldap.Find(ldap.BaseDn, $"(gPLink=*{ActiveDirectory.Escape(g.Id)}*)", true, ["gPLink"], Limit, stop))
            {
                var links = GpoLinks.Parse(t.Text("gPLink")).Where(e => !string.Equals(e.Gpo, g.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                await ldap.Modify(t.Dn, [new(LdapChange.Replace, "gPLink", GpoLinks.Build(links))], stop);
            }
        }, stop);
        await Gpm(login, async t =>
        {
            await Gpmc.Delete(t, g.Id, stop);
            return true;
        }, stop);
        lock (details)
        {
            details.Remove(g.Id);
        }
    }

    public async Task SetGpoApply(string id, string trustee, bool apply, GpoLogin login, CancellationToken stop)
    {
        var g = await FindGpo(id, stop);
        var who = Words.Blank(trustee) ?? throw new ToolProblem("Say who: a group, user or computer, such as AMW\\G-Workstations.");
        await Gpm(login, async t =>
        {
            await Gpmc.SetApply(t, g.Id, who, apply, stop);
            return true;
        }, stop);
        lock (details)
        {
            details.Remove(g.Id);
        }
    }

    public async Task<GpoBackupView> BackupGpo(string id, string? comment, GpoLogin login, string by, CancellationToken stop)
    {
        var g = await FindGpo(id, stop);
        Directory.CreateDirectory(BackupFolder);
        var b = await Gpm(login, t => Gpmc.Backup(t, g.Id, BackupFolder, Words.Blank(comment) ?? "", stop), stop);
        Keep(b.Id, by);
        return new GpoBackupView(b.Id, b.Gpo, b.Name, b.At, by, b.Comment);
    }

    public async Task<(int Done, List<string> Failed)> BackupAll(string? comment, GpoLogin login, string by, CancellationToken stop)
    {
        var all = (await GroupPolicy(stop)).Gpos;
        var failed = new List<string>();
        foreach (var g in all)
        {
            try
            {
                await BackupGpo(g.Id, comment, login, by, stop);
            }
            catch (ToolProblem)
            {
                failed.Add(g.Name);
            }
        }
        return (all.Count - failed.Count, failed);
    }

    public async Task<string> RestoreGpo(string backup, GpoLogin login, string by, CancellationToken stop)
    {
        var b = Backups().FirstOrDefault(x => string.Equals(x.Id, backup?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ToolProblem("That backup isn't here any more.", StatusCodes.Status404NotFound);
        // What it has now is kept too, when it's still there.
        try
        {
            await BackupGpo(b.Gpo, $"Before restoring the backup from {b.At:yyyy-MM-dd HH:mm} UTC", login, by, stop);
        }
        catch (ToolProblem e) when (e.Status == StatusCodes.Status404NotFound)
        {
            // Deleted since: it comes back with its ID.
        }
        var id = await Gpm(login, t => Gpmc.Restore(t, BackupFolder, b.Id, stop), stop);
        lock (details)
        {
            details.Remove(b.Gpo);
        }
        return id;
    }

    // As Rabbid Lab's own account, or the login given (checked first, since Windows only tries it once it's used).
    private async Task<T> Gpm<T>(GpoLogin login, Func<GpmcTarget, Task<T>> work, CancellationToken stop, bool reads = false)
    {
        var f = Ready();
        if (!Gpmc.Here)
        {
            throw new ToolProblem(NoGpmcHere, StatusCodes.Status501NotImplemented);
        }
        var host = await Host(f.Domain!, stop);
        GpmcTarget target;
        if (login.Given)
        {
            using (await AsLogin(f, login, stop))
            {
            }
            var u = login.Username!.Trim();
            target = new GpmcTarget(f.Domain!, host, u.Contains('\\') || u.Contains('@') ? u : $"{u}@{f.Domain}", login.Password ?? "");
        }
        else if (f.ReadOnly)
        {
            // Reports are read as this computer; changes take a login.
            target = reads ? new GpmcTarget(f.Domain!, host, "", "") : throw new GpoNeedsLogin(ReadOnlyNeedsAdmin);
        }
        else
        {
            target = new GpmcTarget(f.Domain!, host, $"{f.Account}@{f.Domain}", secrets.TryDecrypt(f.Password)
                ?? throw new ToolProblem($"{Product.Title}'s Active Directory password can't be read here any more. Set it up again.", StatusCodes.Status409Conflict));
        }
        try
        {
            return await work(target);
        }
        catch (GpmcDenied)
        {
            throw login.Given ? new ToolProblem($"{Ldap.Account(login.Username!)} isn't allowed to do that. It takes a Domain Admin, or rights given in Group Policy Management.")
                : new GpoNeedsLogin(NeedsAdmin(f));
        }
    }

    // --- Backups: Group Policy Management's own folders, and who made each -----------------------------------------

    private List<GpoBackupView> Backups()
    {
        var by = Index();
        return [.. Gpmc.Backups(BackupFolder).Select(b => new GpoBackupView(b.Id, b.Gpo, b.Name, b.At, by.GetValueOrDefault(b.Id), b.Comment)).OrderByDescending(b => b.At)];
    }

    private Dictionary<string, string> Index()
    {
        try
        {
            return File.Exists(BackupIndex) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(BackupIndex)) ?? [] : [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Keep(string backup, string by)
    {
        lock (details)
        {
            var index = Index();
            index[backup] = by;
            File.WriteAllText(BackupIndex, JsonSerializer.Serialize(index), Encoding.UTF8);
        }
    }
}
