using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using RabbidLab.Shared;
using RoleMember = RabbidLab.Server.Member;

namespace RabbidLab.Server;

/// <summary>Rabbid Lab's own Active Directory account, kept in directory-tools.json with its password encrypted.</summary>
public sealed class DirectoryToolsFile
{
    public string? Domain { get; set; }
    public string? Account { get; set; }
    public string? Password { get; set; }

    /// <summary>Where it may make and change accounts: the domain, or one OU.</summary>
    public string? Scope { get; set; }

    /// <summary>Where new people go unless someone picks another OU.</summary>
    public string? NewUsers { get; set; }

    /// <summary>Where new groups go; where new people go when not set.</summary>
    public string? NewGroups { get; set; }

    /// <summary>Where new computer accounts go; the domain's Computers container when not set.</summary>
    public string? NewComputers { get; set; }

    /// <summary>Whether Rabbid Lab made the account (and gave it its rights), rather than being given one.</summary>
    public bool Made { get; set; }
    public string? By { get; set; }
    public DateTime? At { get; set; }
}

/// <summary>What the tools are working with, for the page.</summary>
public sealed record DirectoryToolsState(string Domain, string Account, string Scope, string ScopeName, string NewUsers, string NewUsersName, bool Made, string? By, DateTime? At,
    string NewGroups = "", string NewGroupsName = "", string NewComputers = "", string NewComputersName = "");

/// <summary>An OU or container. Fixed says why nothing new can go in it (Builtin, Domain Controllers), or is null.</summary>
public sealed record AdOu(string Dn, string Name, string Path, string? Fixed = null);

public sealed record AdGroupRef(string Dn, string Name);

/// <summary>A group: its scope (Global, Domain local or Universal), whether it's a security group, and how many members it has.</summary>
public sealed record AdGroup(string Dn, string Name, string? Description, string Where, string Scope = "Global", bool Security = true, int Members = 0, bool MoreMembers = false,
    bool Protected = false, bool System = false, List<AdGroupRef>? Groups = null, string? Email = null, string? Notes = null);

/// <summary>Someone or something in a group: a person ("user"), a group, a computer, or "other" (a contact, say).</summary>
public sealed record AdMember(string Dn, string Name, string Kind, string? Username, string Where);

/// <summary>A computer's account in the domain.</summary>
public sealed record AdComputer(string Name, string Dn, string Where, string? Host, string? Os, string? OsVersion, string? Description, bool Enabled, bool Controller,
    DateTime? LastSignIn, DateTime? Created, List<AdGroupRef>? Groups = null, string? Location = null);

public sealed record NewAdGroup(string? Name, string? Scope, bool Security, string? Ou, string? Description);

public sealed record AdUser(string Username, string? Name, string? Upn, string? Email, string? Title, string? Description, string Dn, string Where,
    bool Enabled, bool Locked, bool NeverExpires, bool MustChange, bool Protected, bool Service, bool System, DateTime? LastSignIn, DateTime? PasswordSet, DateTime? Created,
    List<AdGroupRef> Groups, string? Department = null, string? Manager = null, DateTime? PasswordExpires = null, DateTime? AccountExpires = null, string? ManagerDn = null,
    string? First = null, string? Last = null, string? Office = null, string? Phone = null, string? Company = null,
    bool Sensitive = false, bool Smartcard = false, bool Reversible = false, bool DesOnly = false, bool NoPreauth = false);

/// <summary>The OUs to pick from while setting up, read with the admin sign-in given.</summary>
public sealed record SetupLook(string Domain, string BaseDn, List<AdOu> Places, string AccountOu, string Account);

public sealed record SetupForm(string? Domain, string? Username, string? Password, bool Existing, string? Account, string? Ou, string? Scope);

public sealed record NewAdUser(string? First, string? Last, string? Display, string? Username, string? Ou, string? Password, bool MustChange, bool Enabled, bool NeverExpires,
    string? Description, string? Email, string? Title, List<string>? Groups);

/// <summary>Something the tools couldn't do, said for the page, with the HTTP status to send it with.</summary>
public sealed class ToolProblem(string message, int status = StatusCodes.Status400BadRequest) : Exception(message)
{
    public int Status { get; } = status;
}

public interface IDirectoryTools
{
    /// <summary>The domain to suggest when setting up: the one people sign in with, or this computer's.</summary>
    string? Domain { get; }

    /// <summary>What it works with, or null until it's set up.</summary>
    DirectoryToolsState? State();

    Task<SetupLook> Look(string? domain, string username, string password, CancellationToken stop);

    /// <summary>Makes Rabbid Lab's account (or takes one it's given) and keeps it. A warning, when there's something to say.</summary>
    Task<string?> Connect(SetupForm form, string by, CancellationToken stop);

    void Disconnect();

    Task SetNewUsers(string dn, CancellationToken stop);

    /// <summary>Where New user (users), New group (groups) or New computer (computers) puts things unless another OU is picked.</summary>
    Task SetNewPlace(string kind, string dn, CancellationToken stop);

    Task<List<AdOu>> Places(CancellationToken stop);

    /// <summary>The people in scope. More when Active Directory had more than it sent.</summary>
    Task<(List<AdUser> Users, bool More)> Users(CancellationToken stop);

    Task<AdUser> User(string username, CancellationToken stop);

    Task<(List<AdGroup> Groups, bool More)> Groups(string? search, CancellationToken stop);

    /// <summary>The new account, and what didn't work (a group it couldn't join), if anything.</summary>
    Task<(AdUser User, string? Warning)> Create(NewAdUser form, string password, CancellationToken stop);

    Task Password(string username, string password, bool mustChange, CancellationToken stop);

    Task Unlock(string username, CancellationToken stop);

    Task Enable(string username, bool on, CancellationToken stop);

    /// <summary>What didn't work, if anything.</summary>
    Task<string?> ChangeGroups(string username, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken stop);

    Task Delete(string username, CancellationToken stop);

    /// <summary>The group's own members (not those of groups inside it), those who have it as primary group included.</summary>
    Task<(List<AdMember> Members, bool More)> Members(string group, CancellationToken stop);

    Task<AdGroup> CreateGroup(NewAdGroup form, CancellationToken stop);

    /// <summary>What didn't work, if anything.</summary>
    Task<string?> ChangeMembers(string group, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken stop);

    Task DeleteGroup(string group, CancellationToken stop);

    Task<(List<AdComputer> Computers, bool More)> Computers(CancellationToken stop);

    Task EnableComputer(string name, bool on, CancellationToken stop);

    Task DeleteComputer(string name, CancellationToken stop);

    /// <summary>People, groups and computers whose names match, to put in a group.</summary>
    Task<List<AdMember>> Find(string? search, CancellationToken stop);
}

/// <summary>Passwords Rabbid Lab makes: random, with every kind of character the usual rules want, and none that look alike.</summary>
public static class Passwords
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!#$%*+-=?@^_";

    public static string Make(int length = 16)
    {
        string[] sets = [Upper, Lower, Digits, Symbols];
        var all = string.Concat(sets);
        var chars = sets.Select(s => s[RandomNumberGenerator.GetInt32(s.Length)])
            .Concat(Enumerable.Range(0, Math.Max(0, length - sets.Length)).Select(_ => all[RandomNumberGenerator.GetInt32(all.Length)])).ToArray();
        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }
}

/// <summary>
/// Making and changing Active Directory accounts as Rabbid Lab's own account. Someone signs in once with an account that
/// may make accounts (a domain admin); Rabbid Lab makes svc-rabbidlab with a long random password that nobody sees, gives
/// it the rights Delegate Control would give over users and groups in the domain or one OU, and keeps only that. Every
/// change is checked against that scope here too, since an account it was given may be allowed more.
/// </summary>
public sealed partial class DirectoryTools(DataFolder folder, SettingsStore secrets, Accounts accounts, IDomainFinder finder, LdapOpener? opener = null)
    : IDirectoryTools, IDirectoryExtras, IDisposable
{
    private const int NormalAccount = 0x200;
    private const int Disabled = 0x2;
    private const int DontExpire = 0x10000;
    private const int NotDelegated = 0x100000;
    private const int LockedOut = 0x10;
    private const int ReversiblePassword = 0x80;
    private const int SmartcardRequired = 0x40000;
    private const int DesKeysOnly = 0x200000;
    private const int PasswordNotRequired = 0x20;
    private const int NoPreauthNeeded = 0x400000;
    private const int Limit = 1000; // the most Active Directory sends without paging
    private const string NeedsLdaps = "Active Directory only sets passwords over an encrypted connection, and this one isn't. In Docker that's LDAPS or StartTLS: give the domain controllers a certificate this computer trusts.";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly string[] UserAttributes = ["sAMAccountName", "displayName", "userPrincipalName", "mail", "title", "description",
        "userAccountControl", "msDS-User-Account-Control-Computed", "lockoutTime", "lastLogonTimestamp", "pwdLastSet", "whenCreated", "memberOf", "adminCount", "isCriticalSystemObject",
        "department", "manager", "accountExpires", "msDS-UserPasswordExpiryTimeComputed", "givenName", "sn", "physicalDeliveryOfficeName", "telephoneNumber", "company"];

    private static readonly string[] GroupAttributes = ["cn", "sAMAccountName", "description", "groupType", "member", "adminCount", "isCriticalSystemObject", "objectSid", "memberOf", "mail", "info"];

    private static readonly string[] ComputerAttributes = ["cn", "dNSHostName", "operatingSystem", "operatingSystemVersion", "description", "userAccountControl",
        "lastLogonTimestamp", "whenCreated", "memberOf", "location"];

    private static readonly string[] MemberAttributes = ["objectClass", "sAMAccountName", "displayName", "cn"];

    // Domain Users, Domain Guests, Domain Computers, Domain Controllers and Read-only Domain Controllers: the groups Windows gives as primary group.
    private static readonly long[] PrimaryRids = [513, 514, 515, 516, 521];

    private readonly string file = System.IO.Path.Combine(folder.Path, "directory-tools.json");
    private readonly LdapOpener open = opener ?? Ldap.Open;
    private readonly Lock gate = new();
    private DirectoryToolsFile? loaded;
    private LdapPool? pool;

    public string? Domain => accounts.Directory?.Domain ?? ActiveDirectory.ThisDomain();

    public DirectoryToolsState? State()
    {
        var f = Load();
        if (f.Account is null || f.Domain is null || f.Scope is null)
        {
            return null;
        }
        var baseDn = BaseOf(f.Domain);
        var newUsers = NewUsersOf(f);
        var newGroups = NewGroupsOf(f);
        var newComputers = NewComputersOf(f);
        return new DirectoryToolsState(f.Domain, f.Account, f.Scope, ScopeName(f.Scope, baseDn, f.Domain), newUsers, ScopeName(newUsers, baseDn, f.Domain), f.Made, f.By, f.At,
            newGroups, ScopeName(newGroups, baseDn, f.Domain), newComputers, ScopeName(newComputers, baseDn, f.Domain));
    }

    public async Task<SetupLook> Look(string? domain, string username, string password, CancellationToken stop)
    {
        var d = Clean(domain) ?? Domain ?? throw new ToolProblem("Which domain? Type its name, such as amw.com.");
        using var ldap = await open(d, await Host(d, stop), username, password, stop) ?? throw new ToolProblem("That username or password is wrong.");
        var places = await PlacesIn(ldap, ldap.BaseDn, stop);
        var service = places.FirstOrDefault(p => p.Name.Equals("Service Accounts", StringComparison.OrdinalIgnoreCase));
        return new SetupLook(d, ldap.BaseDn, places, service?.Dn ?? $"CN=Users,{ldap.BaseDn}", "svc-rabbidlab");
    }

    public async Task<string?> Connect(SetupForm form, string by, CancellationToken stop)
    {
        var domain = Clean(form.Domain) ?? Domain ?? throw new ToolProblem("Which domain? Type its name, such as amw.com.");
        var host = await Host(domain, stop);
        var username = form.Username?.Trim() ?? "";
        var password = form.Password ?? "";
        var name = form.Account?.Trim() ?? "";
        if (!form.Existing && BadName(name) is { } bad)
        {
            throw new ToolProblem(bad);
        }
        using var ldap = await open(domain, host, username, password, stop) ?? throw new ToolProblem("That username or password is wrong.");
        var signedIn = Ldap.Account(username);
        if (form.Existing)
        {
            // Kept under the name Active Directory signed in, not the one typed.
            var me = await Ldap.Bound(ldap, username, ["sAMAccountName"], stop) ?? throw new ToolProblem($"{signedIn} signed in, but wasn't found in {domain}.");
            var sam = me.Text("sAMAccountName") ?? signedIn;
            var scope = await Place(ldap, form.Scope, ldap.BaseDn, stop);
            // Whether it may make users there: Active Directory works that out for whoever asks.
            var can = (await ldap.Find(scope, "(objectClass=*)", false, ["allowedChildClassesEffective"], 1, stop)).FirstOrDefault()?.Texts("allowedChildClassesEffective")
                .Contains("user", StringComparer.OrdinalIgnoreCase) == true;
            Save(new DirectoryToolsFile { Domain = domain, Account = sam, Password = secrets.Encrypt(password), Scope = scope, Made = false, By = by, At = DateTime.UtcNow });
            return can ? null : $"{sam} isn't allowed to make accounts in {ScopeName(scope, ldap.BaseDn, domain)}. {Product.Title} can still list them, but making them won't work until it is.";
        }

        if (!ldap.Encrypted)
        {
            throw new ToolProblem(NeedsLdaps);
        }
        if (await Taken(ldap, name, stop))
        {
            throw new ToolProblem($"There's already an account called {name} in {domain}. Pick another name, or choose Use an account I already have.");
        }
        var ou = await Into(ldap, form.Ou, $"CN=Users,{ldap.BaseDn}", stop);
        var where = await Place(ldap, form.Scope, ldap.BaseDn, stop);
        var dn = $"CN={Ldap.Escape(name)},{ou}";
        var secret = Passwords.Make(64);
        try
        {
            await ldap.Add(dn, new Dictionary<string, object[]>
            {
                ["objectClass"] = ["user"],
                ["sAMAccountName"] = [name],
                ["userPrincipalName"] = [$"{name}@{domain}"],
                ["displayName"] = [Product.Name],
                ["description"] = [$"{Product.Name} makes and changes accounts with this one. Made by {signedIn} on {DateTime.UtcNow:yyyy-MM-dd}."],
                ["userAccountControl"] = [(NormalAccount | Disabled).ToString(CultureInfo.InvariantCulture)],
            }, stop);
        }
        catch (LdapRefused e)
        {
            throw new ToolProblem(e.Code == LdapRefused.InsufficientRights ? $"{signedIn} isn't allowed to make accounts in {Ldap.Path(ou, ldap.BaseDn)}. Sign in with a domain admin." : Said(e, signedIn));
        }
        try
        {
            await ldap.Modify(dn, [new(LdapChange.Replace, "unicodePwd", Ldap.Password(secret))], stop);
            await ldap.Modify(dn, [new(LdapChange.Replace, "userAccountControl", (NormalAccount | DontExpire | NotDelegated).ToString(CultureInfo.InvariantCulture))], stop);
            var sid = (await ldap.Find(dn, "(objectClass=*)", false, ["objectSid"], 1, stop)).FirstOrDefault()?.Bytes("objectSid")
                ?? throw new ToolProblem($"{name} was made, but Active Directory didn't say its SID.");
            var dacl = await ldap.ReadDacl(where, stop) ?? throw new ToolProblem($"{signedIn} can't read who may change {ScopeName(where, ldap.BaseDn, domain)}. Sign in with a domain admin.");
            await ldap.WriteDacl(where, Dacl.Grant(dacl, Dacl.Delegation(sid)), stop);
            Save(new DirectoryToolsFile { Domain = domain, Account = name, Password = secrets.Encrypt(secret), Scope = where, Made = true, By = by, At = DateTime.UtcNow });
        }
        catch (Exception e)
        {
            // Half made is no use, whatever stopped it (a closed page included): it goes again, and nothing is kept.
            await Undo(ldap, dn);
            if (e is LdapRefused r)
            {
                throw new ToolProblem(r.Code == LdapRefused.InsufficientRights ? $"{signedIn} can't give {name} rights over {ScopeName(where, ldap.BaseDn, domain)}. Sign in with a domain admin." : Said(r, signedIn));
            }
            if (e is FormatException)
            {
                throw new ToolProblem($"The permissions on {ScopeName(where, ldap.BaseDn, domain)} couldn't be changed: {e.Message}");
            }
            throw;
        }
        return null;
    }

    public void Disconnect()
    {
        lock (gate)
        {
            File.Delete(file);
            loaded = new();
        }
        pool?.Flush();
    }

    public Task SetNewUsers(string dn, CancellationToken stop) => SetNewPlace("users", dn, stop);

    /// <summary>Where New user, New group or New computer puts things unless another OU is picked.</summary>
    public async Task SetNewPlace(string kind, string dn, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var place = await Into(ldap, dn, kind == "groups" ? NewGroupsOf(f) : kind == "computers" ? NewComputersOf(f) : NewUsersOf(f), stop);
        if (!Ldap.Under(place, f.Scope!))
        {
            throw new ToolProblem($"{Product.Title}'s account can only make {(kind == "groups" ? "groups" : kind == "computers" ? "computers" : "people")} in {ScopeName(f.Scope!, ldap.BaseDn, f.Domain!)}.");
        }
        lock (gate)
        {
            // Read again under the lock: a disconnect or new setup since then must not be undone by this save.
            var now = Load();
            if (now.Account != f.Account || now.Domain != f.Domain || now.Scope != f.Scope)
            {
                throw new ToolProblem($"{Product.Title}'s service account changed while this was saving. Try again.", StatusCodes.Status409Conflict);
            }
            Save(new DirectoryToolsFile
            {
                Domain = now.Domain, Account = now.Account, Password = now.Password, Scope = now.Scope, Made = now.Made, By = now.By, At = now.At,
                NewUsers = kind is "users" ? place : now.NewUsers, NewGroups = kind is "groups" ? place : now.NewGroups, NewComputers = kind is "computers" ? place : now.NewComputers,
            }, keepConnections: true);
        }
    }

    public async Task<List<AdOu>> Places(CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var places = new List<AdOu>();
        var top = Ready().Scope!;
        foreach (var root in Roots(top))
        {
            try
            {
                places.AddRange(await PlacesIn(ldap, root, stop));
            }
            catch (LdapRefused e) when (e.Code == LdapRefused.NoSuchObject && root != top)
            {
                // An OU in a role's scope that's gone from Active Directory.
            }
        }
        return places;
    }

    public async Task<(List<AdUser> Users, bool More)> Users(CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var rows = await Look(ldap, f.Scope!, "(&(objectCategory=person)(objectClass=user))", UserAttributes, Limit, stop);
        var users = rows.Select(r => Person(r, ldap.BaseDn, f.Account!)).OrderBy(u => u.Name ?? u.Username, StringComparer.OrdinalIgnoreCase).ToList();
        return (users, rows.Count >= Limit);
    }

    public async Task<AdUser> User(string username, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var row = await Find(ldap, f.Scope!, username, stop);
        Reached(row.Dn);
        return Person(row, ldap.BaseDn, f.Account!);
    }

    public async Task<(List<AdGroup> Groups, bool More)> Groups(string? search, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var rows = await Look(ldap, ldap.BaseDn, $"(&(objectCategory=group){Match(search, "cn", "sAMAccountName", "description")})", GroupAttributes, Limit, stop);
        // Whoever has a group as primary group isn't in its member attribute, so they're counted apart. Not for a search: pickers don't show counts.
        var primary = new Dictionary<long, int>();
        if (rows.Count > 0 && string.IsNullOrWhiteSpace(search))
        {
            var others = string.Concat(PrimaryRids.Select(rid => $"(!(primaryGroupID={rid}))"));
            foreach (var r in await ldap.Find(ldap.BaseDn, $"(&(primaryGroupID=*){others})", true, ["primaryGroupID"], Limit, stop))
            {
                if (r.Number("primaryGroupID") is { } rid)
                {
                    primary[rid] = primary.GetValueOrDefault(rid) + 1;
                }
            }
            foreach (var rid in rows.Select(Rid).OfType<long>().Where(PrimaryRids.Contains).Distinct())
            {
                primary[rid] = (await ldap.Find(ldap.BaseDn, $"(primaryGroupID={rid})", true, ["cn"], Limit, stop)).Count;
            }
        }
        var groups = rows.Select(r =>
        {
            var g = Group(r, ldap.BaseDn);
            return Rid(r) is { } rid && primary.TryGetValue(rid, out var n) && n > 0 ? g with { Members = g.Members + n, MoreMembers = g.MoreMembers || n >= Limit } : g;
        }).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return (groups, rows.Count >= Limit);
    }

    public async Task<(List<AdMember> Members, bool More)> Members(string group, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var dn = Inside(group, ldap.BaseDn);
        var found = (await ldap.Find(dn, "(objectClass=group)", false, ["objectSid"], 1, stop)).FirstOrDefault()
            ?? throw new ToolProblem("That group isn't in Active Directory any more.", StatusCodes.Status404NotFound);
        Reached(found.Dn);
        var member = $"(memberOf={ActiveDirectory.Escape(found.Dn)})";
        var rows = await ldap.Find(ldap.BaseDn, Rid(found) is { } rid ? $"(|{member}(primaryGroupID={rid}))" : member, true, MemberAttributes, Limit, stop);
        return ([.. rows.Select(r => Member(r, ldap.BaseDn)).OrderBy(m => m.Kind != "user").ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)], rows.Count >= Limit);
    }

    public async Task<AdGroup> CreateGroup(NewAdGroup form, CancellationToken stop)
    {
        var name = form.Name?.Trim() ?? "";
        if (name.Length is 0 or > 64 || name.IndexOfAny(AccountOrder.NotInNames) >= 0 || name.EndsWith('.'))
        {
            throw new ToolProblem("Group names are 64 characters at most, without \" / \\ [ ] : ; | = , + * ? < > or @.");
        }
        Unclaimed(name, group: true);
        using var ldap = await Open(stop);
        var f = Ready();
        var ou = await Into(ldap, form.Ou, NewGroupsOf(f), stop);
        if (!Ldap.Under(ou, f.Scope!))
        {
            throw new ToolProblem($"{Product.Title}'s account can only make groups in {ScopeName(f.Scope!, ldap.BaseDn, f.Domain!)}.");
        }
        Reached(ou);
        if (await Taken(ldap, name, stop))
        {
            throw new ToolProblem($"There's already something called {name} in {f.Domain}.");
        }
        // groupType: 2 global, 4 domain local, 8 universal; the top bit makes it a security group.
        var type = (form.Scope?.ToLowerInvariant()) switch { "universal" => 8, "local" => 4, _ => 2 } | (form.Security ? unchecked((int) 0x80000000) : 0);
        var dn = $"CN={Ldap.Escape(name)},{ou}";
        var attributes = new Dictionary<string, object[]>
        {
            ["objectClass"] = ["group"],
            ["sAMAccountName"] = [name],
            ["groupType"] = [type.ToString(CultureInfo.InvariantCulture)],
        };
        if (Words.Blank(form.Description) is { } about)
        {
            attributes["description"] = [about];
        }
        try
        {
            await ldap.Add(dn, attributes, stop);
        }
        catch (LdapRefused e)
        {
            throw new ToolProblem(e.Code == LdapRefused.EntryExists ? $"There's already something called {name} in {Ldap.Path(ou, ldap.BaseDn)}." : Said(e, f.Account!));
        }
        var made = (await ldap.Find(dn, "(objectClass=*)", false, GroupAttributes, 1, stop)).FirstOrDefault();
        return made is null ? throw new ToolProblem($"{name} was made, but couldn't be read back.") : Group(made, ldap.BaseDn);
    }

    public async Task<string?> ChangeMembers(string group, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var dn = (await ChangeableGroup(ldap, f, group, stop)).Dn;
        var problems = new List<string>();
        foreach (var (members, adding) in new[] { (add, true), (remove, false) })
        {
            foreach (var m in Listed(members))
            {
                if (await SetMember(ldap, dn, Inside(m, ldap.BaseDn), adding, stop) is { } refused)
                {
                    problems.Add(refused.Code == LdapRefused.InsufficientRights ? $"{f.Account} isn't allowed to change {Ldap.Name(dn)}" : $"{Ldap.Name(m)}: {refused.Message}");
                    break;
                }
            }
        }
        return problems.Count == 0 ? null : string.Join("; ", problems.Distinct()) + ".";
    }

    public async Task DeleteGroup(string group, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var found = await ChangeableGroup(ldap, f, group, stop);
        if (Group(found, ldap.BaseDn) is { Protected: true } or { System: true })
        {
            throw new ToolProblem($"{Ldap.Name(found.Dn)} is a built-in or protected Active Directory group, so it can't be deleted here.");
        }
        await Remove(ldap, found.Dn, f, stop);
    }

    public async Task<(List<AdComputer> Computers, bool More)> Computers(CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var rows = await Look(ldap, Ready().Scope!, "(objectCategory=computer)", ComputerAttributes, Limit, stop);
        return ([.. rows.Select(r => Computer(r, ldap.BaseDn)).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)], rows.Count >= Limit);
    }

    public async Task EnableComputer(string name, bool on, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var row = await ComputerRow(ldap, f, name, stop);
        var flags = (int) (row.Number("userAccountControl") ?? 0x1000);
        flags = on ? flags & ~Disabled : flags | Disabled;
        await Change(ldap, row.Dn, [new(LdapChange.Replace, "userAccountControl", flags.ToString(CultureInfo.InvariantCulture))], f, password: false, stop);
    }

    public async Task DeleteComputer(string name, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        await Remove(ldap, (await ComputerRow(ldap, f, name, stop)).Dn, f, stop);
    }

    public async Task<List<AdMember>> Find(string? search, CancellationToken stop)
    {
        var match = Match(search, "cn", "sAMAccountName", "displayName");
        if (match.Length == 0)
        {
            return [];
        }
        using var ldap = await Open(stop);
        var rows = await Look(ldap, ldap.BaseDn, $"(&(|(&(objectCategory=person)(objectClass=user))(objectCategory=group)(objectCategory=computer)){match})", MemberAttributes, 40, stop);
        return [.. rows.Select(r => Member(r, ldap.BaseDn)).OrderBy(m => m.Kind != "user").ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<(AdUser User, string? Warning)> Create(NewAdUser form, string password, CancellationToken stop)
    {
        var username = form.Username?.Trim() ?? "";
        if (BadName(username) is { } bad)
        {
            throw new ToolProblem(bad);
        }
        Unclaimed(username, group: false);
        var first = Words.Blank(form.First);
        var last = Words.Blank(form.Last);
        var display = Words.Blank(form.Display) ?? Words.Blank($"{first} {last}") ?? username;
        if (display.Length > 64)
        {
            throw new ToolProblem("The display name is 64 characters at most.");
        }
        using var ldap = await Open(stop);
        if (!ldap.Encrypted)
        {
            throw new ToolProblem(NeedsLdaps);
        }
        var f = Ready();
        var ou = await Into(ldap, form.Ou, NewUsersOf(f), stop);
        if (!Ldap.Under(ou, f.Scope!))
        {
            throw new ToolProblem($"{Product.Title}'s account can only make people in {ScopeName(f.Scope!, ldap.BaseDn, f.Domain!)}.");
        }
        Reached(ou);
        if (await Taken(ldap, username, stop))
        {
            throw new ToolProblem($"There's already an account called {username} in {f.Domain}.");
        }
        var dn = $"CN={Ldap.Escape(display)},{ou}";
        var attributes = new Dictionary<string, object[]>
        {
            ["objectClass"] = ["user"],
            ["sAMAccountName"] = [username],
            ["userPrincipalName"] = [$"{username}@{f.Domain}"],
            ["displayName"] = [display],
            ["userAccountControl"] = [(NormalAccount | Disabled).ToString(CultureInfo.InvariantCulture)],
        };
        foreach (var (attribute, value) in new[] { ("givenName", first), ("sn", last), ("description", Words.Blank(form.Description)), ("mail", Words.Blank(form.Email)), ("title", Words.Blank(form.Title)) })
        {
            if (value is not null)
            {
                attributes[attribute] = [value];
            }
        }
        try
        {
            await ldap.Add(dn, attributes, stop);
        }
        catch (LdapRefused e) when (e.Code == LdapRefused.EntryExists)
        {
            throw new ToolProblem($"There's already something called {display} in {Ldap.Path(ou, ldap.BaseDn)}. Change the display name.");
        }
        catch (LdapRefused e)
        {
            throw new ToolProblem(Said(e, f.Account!));
        }
        try
        {
            await ldap.Modify(dn, [new(LdapChange.Replace, "unicodePwd", Ldap.Password(password))], stop);
            var flags = NormalAccount | (form.Enabled ? 0 : Disabled) | (form.NeverExpires ? DontExpire : 0);
            List<LdapMod> rest = [new(LdapChange.Replace, "userAccountControl", flags.ToString(CultureInfo.InvariantCulture))];
            if (form.MustChange && !form.NeverExpires)
            {
                rest.Add(new(LdapChange.Replace, "pwdLastSet", "0"));
            }
            await ldap.Modify(dn, rest, stop);
        }
        catch (Exception e)
        {
            // Whatever stopped it (a closed page included), a disabled account with no password isn't left behind.
            await Undo(ldap, dn);
            if (e is LdapRefused r)
            {
                throw new ToolProblem(Said(r, f.Account!, password: true));
            }
            throw;
        }
        var warning = await Join(ldap, f, dn, form.Groups ?? [], true, stop);
        var made = (await ldap.Find(dn, "(objectClass=*)", false, UserAttributes, 1, stop)).FirstOrDefault();
        return (made is null ? throw new ToolProblem($"{username} was made, but couldn't be read back.") : Person(made, ldap.BaseDn, f.Account!),
            warning is null ? null : $"Made {username}, but {char.ToLowerInvariant(warning[0])}{warning[1..]}");
    }

    public async Task Password(string username, string password, bool mustChange, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        if (!ldap.Encrypted)
        {
            throw new ToolProblem(NeedsLdaps);
        }
        var f = Ready();
        var user = await Changeable(ldap, f, username, stop, signIn: true);
        List<LdapMod> changes = [new(LdapChange.Replace, "unicodePwd", Ldap.Password(password)), new(LdapChange.Replace, "lockoutTime", "0")];
        if (mustChange)
        {
            changes.Add(new(LdapChange.Replace, "pwdLastSet", "0"));
        }
        await Change(ldap, user.Dn, changes, f, password: true, stop);
    }

    public async Task Unlock(string username, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var user = await Changeable(ldap, f, username, stop);
        await Change(ldap, user.Dn, [new(LdapChange.Replace, "lockoutTime", "0")], f, password: false, stop);
    }

    public async Task Enable(string username, bool on, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var user = await Changeable(ldap, f, username, stop, signIn: true);
        var flags = (int) (user.Number("userAccountControl") ?? NormalAccount);
        flags = on ? flags & ~Disabled : flags | Disabled;
        await Change(ldap, user.Dn, [new(LdapChange.Replace, "userAccountControl", flags.ToString(CultureInfo.InvariantCulture))], f, password: false, stop);
    }

    public async Task<string?> ChangeGroups(string username, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var user = await Changeable(ldap, f, username, stop);
        var a = await Join(ldap, f, user.Dn, add, true, stop);
        var b = await Join(ldap, f, user.Dn, remove, false, stop);
        return a is null ? b : b is null ? a : $"{a} {b}";
    }

    public async Task Delete(string username, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        await Remove(ldap, (await Changeable(ldap, f, username, stop, signIn: true)).Dn, f, stop);
    }

    public void Dispose() => pool?.Dispose();

    // --- Helpers ---------------------------------------------------------------------------------------------------

    /// <summary>A connection signed in as Rabbid Lab's own account, lent from the few kept open. Disposing gives it back.</summary>
    private Task<ILdap> Open(CancellationToken stop)
    {
        lock (gate)
        {
            pool ??= new LdapPool(SignIn);
        }
        return pool.Take(stop);
    }

    private async Task<ILdap> SignIn(CancellationToken stop)
    {
        var f = Ready();
        var password = secrets.TryDecrypt(f.Password) ?? throw new ToolProblem($"{Product.Title}'s Active Directory password can't be read here any more. Set it up again.", StatusCodes.Status409Conflict);
        try
        {
            return await open(f.Domain!, await Host(f.Domain!, stop), $"{f.Account}@{f.Domain}", password, stop)
                ?? throw new ToolProblem($"{f.Domain} turned down {f.Account}'s password. Someone may have changed it or turned the account off. Set it up again.", StatusCodes.Status409Conflict);
        }
        catch (DirectoryProblem e)
        {
            throw new ToolProblem(e.Message, StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>What's set up, or the problem to send when nothing is.</summary>
    private DirectoryToolsFile Ready()
    {
        var f = Load();
        return f.Account is null || f.Domain is null || f.Scope is null
            ? throw new ToolProblem($"{Product.Title} doesn't have its Active Directory account yet. Set it up first.", StatusCodes.Status409Conflict)
            : f;
    }

    // The domain controller sign-in uses, when this is its domain; else the quickest that answers.
    private Task<string> Host(string domain, CancellationToken stop) =>
        finder.Host(domain, accounts.Directory is { } d && d.Domain == domain ? d.Server : null, stop);

    private static async Task<LdapRow> Find(ILdap ldap, string root, string username, CancellationToken stop) =>
        (await ldap.Find(root, $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={ActiveDirectory.Escape(username.Trim())}))", true, UserAttributes, 1, stop)).FirstOrDefault()
        ?? throw new ToolProblem($"There's no account called {username} any more.", StatusCodes.Status404NotFound);

    /// <summary>
    /// Found and in scope. Protected and built-in accounts go to Active Directory like any other, and it refuses what the
    /// service account may not do. Rabbid Lab's own account keeps its password and sign-in settings (signIn), or Rabbid
    /// Lab would lock itself out. Every change to a user goes through here.
    /// </summary>
    private static async Task<LdapRow> Changeable(ILdap ldap, DirectoryToolsFile f, string username, CancellationToken stop, bool signIn = false)
    {
        var row = await Find(ldap, ldap.BaseDn, username, stop);
        var user = Person(row, ldap.BaseDn, f.Account!);
        InScope(row.Dn, f, ldap.BaseDn);
        await Ranked(ldap, row.Dn, user.Username, null, group: false, stop);
        return signIn && user.Service
            ? throw new ToolProblem($"That's {Product.Title}'s own account. Its password and sign-in settings stay as they are, so {Product.Title} keeps working.")
            : row;
    }

    /// <summary>The group, when it's in scope; built-in ones too.</summary>
    private static async Task<LdapRow> ChangeableGroup(ILdap ldap, DirectoryToolsFile f, string group, CancellationToken stop)
    {
        var row = (await ldap.Find(Inside(group, ldap.BaseDn), "(objectClass=group)", false, GroupAttributes, 1, stop)).FirstOrDefault()
            ?? throw new ToolProblem("That group isn't in Active Directory any more.", StatusCodes.Status404NotFound);
        InScope(row.Dn, f, ldap.BaseDn);
        await Ranked(ldap, row.Dn, row.Text("sAMAccountName"), row.Text("cn") ?? Ldap.Name(row.Dn), group: true, stop);
        return row; // built-in ones too: Active Directory refuses what may not be done
    }

    /// <summary>The computer by name, when it's in scope and isn't a domain controller (those are only changed in Active Directory itself).</summary>
    private static async Task<LdapRow> ComputerRow(ILdap ldap, DirectoryToolsFile f, string name, CancellationToken stop)
    {
        var row = (await ldap.Find(ldap.BaseDn, $"(&(objectCategory=computer)(sAMAccountName={ActiveDirectory.Escape(name.Trim().TrimEnd('$'))}$))", true, ComputerAttributes, 1, stop)).FirstOrDefault()
            ?? throw new ToolProblem($"There's no computer called {name} in Active Directory any more.", StatusCodes.Status404NotFound);
        InScope(row.Dn, f, ldap.BaseDn);
        return ((row.Number("userAccountControl") ?? 0) & 0x2000) != 0
            ? throw new ToolProblem($"{name} is a domain controller. Change those in Active Directory itself.")
            : row;
    }

    /// <summary>Refused when it's outside the OU (or domain) the service account was set up for, or outside the caller's roles.</summary>
    private static void InScope(string dn, DirectoryToolsFile f, string baseDn)
    {
        if (!Ldap.Under(dn, f.Scope!))
        {
            throw new ToolProblem($"{Ldap.Name(dn)} is outside the service account's scope ({ScopeName(f.Scope!, baseDn, f.Domain!)}), so it can't be changed here.");
        }
        Reached(dn);
    }

    /// <summary>Refused when it's outside the OUs the caller's roles cover for what they're doing.</summary>
    private static void Reached(string dn)
    {
        if (Caller.Current?.Reach() is { } reach && !reach.Any(r => Ldap.Under(dn, r)))
        {
            throw new ToolProblem($"{Ldap.Name(dn)} is outside the scope of your roles.", StatusCodes.Status403Forbidden);
        }
    }

    /// <summary>Where the caller may look under a root: the root itself, or the OUs of their roles inside it.</summary>
    private static List<string> Roots(string top) => Caller.Current?.Reach() is not { } reach ? [top]
        : [.. reach.Select(r => Ldap.Under(r, top) ? r : Ldap.Under(top, r) ? top : null).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>A search of everything under a root, kept to what the caller's roles cover.</summary>
    private static async Task<List<LdapRow>> Look(ILdap ldap, string top, string filter, string[] attributes, int limit, CancellationToken stop)
    {
        var rows = new List<LdapRow>();
        foreach (var root in Roots(top))
        {
            try
            {
                rows.AddRange(await ldap.Find(root, filter, true, attributes, limit, stop));
            }
            catch (LdapRefused e) when (e.Code == LdapRefused.NoSuchObject && root != top)
            {
                // An OU in a role's scope that's gone from Active Directory.
            }
        }
        return rows;
    }

    /// <summary>
    /// Refused when the account or group is in a role the caller isn't in and isn't above: a new password for that
    /// account, or a new member of that group, would hand over the role. A group inside such a group counts too.
    /// </summary>
    private static async Task Ranked(ILdap ldap, string dn, string? sam, string? cn, bool group, CancellationToken stop)
    {
        if (Caller.Current?.Outranking() is not { Count: > 0 } above)
        {
            return;
        }
        static bool Same(string? a, string? b) => a is { Length: > 0 } && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        Role? Of(Func<RoleMember, bool> match) => above.FirstOrDefault(r => r.Members.Any(match));
        var role = group ? Of(m => m.Kind == RoleMember.Group && (Same(m.Name, cn) || Same(m.Name, sam)))
            : Of(m => m.Kind == RoleMember.Everyone || (m.Kind == RoleMember.Person && Same(m.Name, sam)));
        var wanted = above.SelectMany(r => r.Members).Where(m => m.Kind == RoleMember.Group).Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (role is null && wanted.Count > 0)
        {
            // tokenGroups is worked out on request from the entry itself: nested groups and the primary group count.
            var tokens = (await ldap.Find(dn, "(objectClass=*)", false, ["tokenGroups"], 1, stop)).FirstOrDefault()?.AllBytes("tokenGroups").ToList() ?? [];
            if (tokens.Count > 0)
            {
                foreach (var g in await ldap.Find(ldap.BaseDn, ActiveDirectory.GroupsFilter(wanted), true, ["objectSid", "cn", "sAMAccountName"], Limit, stop))
                {
                    if (g.Bytes("objectSid") is { } sid && tokens.Any(t => t.AsSpan().SequenceEqual(sid)))
                    {
                        role ??= Of(m => m.Kind == RoleMember.Group && (Same(m.Name, g.Text("cn")) || Same(m.Name, g.Text("sAMAccountName"))));
                    }
                }
            }
        }
        if (role is not null)
        {
            throw new ToolProblem(group
                ? $"{cn ?? Ldap.Name(dn)} puts people in the role {role.Name}. Only someone above {role.Name} can change it."
                : $"{sam ?? Ldap.Name(dn)} is in the role {role.Name}. Only someone above {role.Name} can change that account.", StatusCodes.Status403Forbidden);
        }
    }

    /// <summary>Ranked, for something only known by its DN: users and groups are checked, anything else passes.</summary>
    private static async Task Ranked(ILdap ldap, string dn, CancellationToken stop)
    {
        if (Caller.Current?.Outranking() is not { Count: > 0 })
        {
            return;
        }
        var row = (await ldap.Find(dn, "(objectClass=*)", false, ["objectClass", "sAMAccountName", "cn"], 1, stop)).FirstOrDefault();
        if (row is not null && KindOf(row) is var kind and ("user" or "group"))
        {
            await Ranked(ldap, dn, row.Text("sAMAccountName"), row.Text("cn") ?? Ldap.Name(dn), kind == "group", stop);
        }
    }

    /// <summary>
    /// Refused when a role above the caller names this account or group: making one by that name (after the old one was
    /// deleted, say) would put its maker in the role. Null checks both kinds.
    /// </summary>
    private static void Unclaimed(string name, bool? group)
    {
        if (Caller.Current?.Outranking() is not { Count: > 0 } above)
        {
            return;
        }
        var role = above.FirstOrDefault(r => r.Members.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)
            && ((m.Kind == RoleMember.Group && group != false) || (m.Kind == RoleMember.Person && group != true))));
        if (role is not null)
        {
            throw new ToolProblem($"The role {role.Name} names {name}. Only someone above {role.Name} can make or rename something to that.", StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<bool> Taken(ILdap ldap, string sam, CancellationToken stop) =>
        (await ldap.Find(ldap.BaseDn, $"(sAMAccountName={ActiveDirectory.Escape(sam)})", true, ["sAMAccountName"], 1, stop)).Count > 0;

    /// <summary>A filter matching the search anywhere in any of the attributes; empty without a search.</summary>
    private static string Match(string? search, params string[] attributes)
    {
        var s = ActiveDirectory.Escape(search?.Trim() ?? "");
        return s.Length == 0 ? "" : "(|" + string.Concat(attributes.Select(a => $"({a}=*{s}*)")) + ")";
    }

    private static IEnumerable<string> Listed(IEnumerable<string> names) => names.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);

    private static async Task Change(ILdap ldap, string dn, IReadOnlyList<LdapMod> changes, DirectoryToolsFile f, bool password, CancellationToken stop)
    {
        try
        {
            await ldap.Modify(dn, changes, stop);
        }
        catch (LdapRefused e)
        {
            throw new ToolProblem(Said(e, f.Account!, password));
        }
    }

    private static async Task Remove(ILdap ldap, string dn, DirectoryToolsFile f, CancellationToken stop)
    {
        try
        {
            await ldap.Delete(dn, stop);
        }
        catch (LdapRefused e)
        {
            throw new ToolProblem(Said(e, f.Account!));
        }
    }

    /// <summary>Takes away an account that was only half made. If that fails too it stays, turned off.</summary>
    private static async Task Undo(ILdap ldap, string dn)
    {
        try
        {
            await ldap.Delete(dn, CancellationToken.None);
        }
        catch (Exception e) when (e is LdapRefused or DirectoryProblem or ToolProblem)
        {
            // The first problem is the one to report.
        }
    }

    /// <summary>One member into a group or out of it. Null when done or already so, else Active Directory's refusal.</summary>
    private static async Task<LdapRefused?> SetMember(ILdap ldap, string group, string member, bool add, CancellationToken stop)
    {
        try
        {
            await ldap.Modify(group, [new(add ? LdapChange.Add : LdapChange.Delete, "member", member)], stop);
            return null;
        }
        catch (LdapRefused e) when (add ? e.Code is LdapRefused.AttributeExists or LdapRefused.EntryExists : e.Code is LdapRefused.NoSuchAttribute or LdapRefused.Unwilling)
        {
            return null; // already in it, or already out
        }
        catch (LdapRefused e)
        {
            return e;
        }
    }

    /// <summary>Into the groups (or out of them); what didn't work, or null. Each group must be in scope and not built-in.</summary>
    private static async Task<string?> Join(ILdap ldap, DirectoryToolsFile f, string userDn, IEnumerable<string> groups, bool add, CancellationToken stop)
    {
        var problems = new List<string>();
        foreach (var g in Listed(groups))
        {
            try
            {
                var group = await ChangeableGroup(ldap, f, g, stop);
                if (await SetMember(ldap, group.Dn, userDn, add, stop) is { } refused)
                {
                    problems.Add(refused.Code == LdapRefused.InsufficientRights ? $"it isn't allowed to change {Ldap.Name(g)}" : $"{Ldap.Name(g)} said: {refused.Message}");
                }
            }
            catch (ToolProblem e)
            {
                problems.Add(e.Message.TrimEnd('.'));
            }
        }
        if (problems.Count == 0)
        {
            return null;
        }
        var text = string.Join("; ", problems);
        return (add ? "Couldn't add to every group: " : "Couldn't take out of every group: ") + text + ".";
    }

    // Every OU and container Active Directory Users and Computers shows: not the advanced-only system ones, nor anything
    // inside System. The ones nothing new can be created in are listed too, with why, so the tree matches ADUC.
    private static async Task<List<AdOu>> PlacesIn(ILdap ldap, string root, CancellationToken stop)
    {
        var rows = await ldap.Find(root, "(&(|(objectClass=organizationalUnit)(objectClass=container)(objectClass=builtinDomain))(!(showInAdvancedViewOnly=TRUE)))",
            true, ["ou", "cn", "objectClass"], Limit, stop);
        return [.. rows.Where(r => !r.Dn.EndsWith($",CN=System,{ldap.BaseDn}", StringComparison.OrdinalIgnoreCase))
            .Select(r => new AdOu(r.Dn, r.Text("ou") ?? r.Text("cn") ?? Ldap.Name(r.Dn), Ldap.Path(r.Dn, ldap.BaseDn), Fixed(r, ldap.BaseDn)))
            .OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase)];
    }

    private static string? Fixed(LdapRow r, string baseDn) =>
        r.Texts("objectClass").Contains("builtinDomain", StringComparer.OrdinalIgnoreCase) ? "Active Directory keeps its built-in groups here. New objects can't go in it."
        : r.Dn.Equals($"CN=ForeignSecurityPrincipals,{baseDn}", StringComparison.OrdinalIgnoreCase) ? "Stand-ins for accounts from other domains. New objects can't go in it."
        : r.Dn.Equals($"OU=Domain Controllers,{baseDn}", StringComparison.OrdinalIgnoreCase) ? "Only domain controllers go here."
        : null;

    /// <summary>The OU picked (which must be there), or the fallback when none is.</summary>
    private static async Task<string> Place(ILdap ldap, string? dn, string fallback, CancellationToken stop)
    {
        var wanted = Words.Blank(dn) ?? fallback;
        if (!Ldap.Under(wanted, ldap.BaseDn))
        {
            throw new ToolProblem("That place isn't in this domain.");
        }
        var found = (await ldap.Find(wanted, "(objectClass=*)", false, ["distinguishedName"], 1, stop)).FirstOrDefault();
        return found?.Dn ?? throw new ToolProblem($"There's no {Ldap.Path(wanted, ldap.BaseDn)} in Active Directory any more.");
    }

    /// <summary>Like Place, for where something new is made: never Builtin, ForeignSecurityPrincipals or Domain Controllers.</summary>
    private static async Task<string> Into(ILdap ldap, string? dn, string fallback, CancellationToken stop)
    {
        var place = await Place(ldap, dn, fallback, stop);
        var row = (await ldap.Find(place, "(objectClass=*)", false, ["objectClass"], 1, stop)).FirstOrDefault();
        return row is not null && Fixed(row, ldap.BaseDn) is { } why ? throw new ToolProblem(why) : place;
    }

    private static AdUser Person(LdapRow r, string baseDn, string service)
    {
        var flags = r.Number("userAccountControl") ?? NormalAccount;
        var computed = r.Number("msDS-User-Account-Control-Computed");
        var sam = r.Text("sAMAccountName") ?? Ldap.Name(r.Dn);
        return new AdUser(sam, r.Text("displayName"), r.Text("userPrincipalName"), r.Text("mail"), r.Text("title"), r.Text("description"),
            r.Dn, Ldap.Path(Ldap.Parent(r.Dn), baseDn),
            Enabled: (flags & Disabled) == 0,
            Locked: computed is { } c ? (c & LockedOut) != 0 : (r.Number("lockoutTime") ?? 0) > 0,
            NeverExpires: (flags & DontExpire) != 0,
            MustChange: r.Number("pwdLastSet") == 0,
            Protected: r.Number("adminCount") == 1,
            Service: string.Equals(sam, service, StringComparison.OrdinalIgnoreCase),
            // krbtgt, Guest and the like, and the mailboxes Exchange makes for itself.
            System: r.Text("isCriticalSystemObject") == "TRUE" || sam.StartsWith('$') || sam.StartsWith("SM_", StringComparison.OrdinalIgnoreCase)
                || sam.StartsWith("HealthMailbox", StringComparison.OrdinalIgnoreCase),
            LastSignIn: FileTime(r.Number("lastLogonTimestamp")),
            PasswordSet: FileTime(r.Number("pwdLastSet")),
            Created: When(r.Text("whenCreated")),
            Groups: [.. r.Texts("memberOf").Select(g => new AdGroupRef(g, Ldap.Name(g))).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)],
            Department: r.Text("department"),
            Manager: r.Text("manager") is { } boss ? Ldap.Name(boss) : null,
            // Worked out by Active Directory from the domain's (or a fine-grained) policy; never for accounts that don't expire.
            PasswordExpires: (flags & DontExpire) == 0 ? FileTime(r.Number("msDS-UserPasswordExpiryTimeComputed")) : null,
            AccountExpires: FileTime(r.Number("accountExpires")),
            ManagerDn: r.Text("manager"),
            First: r.Text("givenName"), Last: r.Text("sn"), Office: r.Text("physicalDeliveryOfficeName"), Phone: r.Text("telephoneNumber"), Company: r.Text("company"),
            Sensitive: (flags & NotDelegated) != 0, Smartcard: (flags & SmartcardRequired) != 0, Reversible: (flags & ReversiblePassword) != 0,
            DesOnly: (flags & DesKeysOnly) != 0, NoPreauth: (flags & NoPreauthNeeded) != 0);
    }

    private static AdGroup Group(LdapRow r, string baseDn)
    {
        var type = r.Number("groupType") ?? 0;
        var (members, more) = r.Many("member");
        return new AdGroup(r.Dn, r.Text("cn") ?? Ldap.Name(r.Dn), r.Text("description"), Ldap.Path(Ldap.Parent(r.Dn), baseDn),
            Scope: (type & 8) != 0 ? "Universal" : (type & 4) != 0 ? "Domain local" : "Global",
            Security: type < 0,
            Members: members,
            MoreMembers: more,
            Protected: r.Number("adminCount") == 1,
            System: r.Text("isCriticalSystemObject") == "TRUE" || (type & 1) != 0,
            Groups: MemberOf(r), Email: r.Text("mail"), Notes: r.Text("info"));
    }

    /// <summary>The last part of its SID, which is what primaryGroupID holds. Null without an objectSid.</summary>
    private static long? Rid(LdapRow r) => r.Bytes("objectSid") is { Length: >= 12 } sid ? BitConverter.ToUInt32(sid, sid.Length - 4) : null;

    private static AdMember Member(LdapRow r, string baseDn)
    {
        var kind = KindOf(r) is var known and ("computer" or "group" or "user") ? known : "other";
        var sam = r.Text("sAMAccountName");
        var name = kind == "user" ? r.Text("displayName") ?? r.Text("cn") : r.Text("cn");
        return new AdMember(r.Dn, name ?? Ldap.Name(r.Dn), kind, kind == "computer" ? sam?.TrimEnd('$') : sam, Ldap.Path(Ldap.Parent(r.Dn), baseDn));
    }

    private static AdComputer Computer(LdapRow r, string baseDn)
    {
        var flags = r.Number("userAccountControl") ?? 0x1000;
        return new AdComputer(r.Text("cn") ?? Ldap.Name(r.Dn), r.Dn, Ldap.Path(Ldap.Parent(r.Dn), baseDn), r.Text("dNSHostName"), r.Text("operatingSystem"),
            r.Text("operatingSystemVersion"), r.Text("description"), Enabled: (flags & Disabled) == 0, Controller: (flags & 0x2000) != 0,
            LastSignIn: FileTime(r.Number("lastLogonTimestamp")),
            Created: When(r.Text("whenCreated")),
            Groups: MemberOf(r), Location: r.Text("location"));
    }

    /// <summary>The groups it is directly in, by name.</summary>
    private static List<AdGroupRef> MemberOf(LdapRow r) =>
        [.. r.Texts("memberOf").Select(g => new AdGroupRef(g, Ldap.Name(g))).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The DN, when it's in this domain.</summary>
    private static string Inside(string dn, string baseDn) =>
        Ldap.Under(dn?.Trim() ?? "", baseDn) ? dn!.Trim() : throw new ToolProblem("That isn't in this domain.");

    private static DateTime? FileTime(long? value) => value is > 0 and < long.MaxValue ? DateTime.FromFileTimeUtc(value.Value) : null;

    /// <summary>What Active Directory's refusal means, in words.</summary>
    private static string Said(LdapRefused e, string account, bool password = false) => e.Code switch
    {
        LdapRefused.InsufficientRights => $"{account} isn't allowed to do that there. Accounts of admins are protected by Active Directory itself; change those in Active Directory Users and Computers.",
        LdapRefused.ConstraintViolation or LdapRefused.Unwilling when password => "That password doesn't meet the domain's password rules (length, complexity or history). Try a longer one.",
        LdapRefused.NoSuchObject => "That isn't in Active Directory any more.",
        LdapRefused.EntryExists => "There's already something with that name there.",
        _ => $"Active Directory said: {e.Message}.",
    };

    /// <summary>Why a name can't be an account's username (sAMAccountName), or null when it can.</summary>
    public static string? BadName(string name) =>
        name.Length == 0 ? "Give the account a username."
        : name.Length > 20 ? "Usernames are 20 characters at most (Active Directory's sign-in name)."
        // The Windows account rules, and Active Directory's own: no spaces, no dot at the end.
        : AccountOrder.BadName(name) is not null || name.EndsWith('.') || name.Contains(' ') ? "Usernames can't have spaces or \" / \\ [ ] : ; | = , + * ? < > @ in them, or end with a dot."
        : null;

    private static string? Clean(string? domain) => Words.Blank(domain)?.TrimEnd('.').ToLowerInvariant();

    private static string BaseOf(string domain) => string.Join(',', domain.Split('.').Select(p => $"DC={p}"));

    private static string ScopeName(string dn, string baseDn, string domain) =>
        string.Equals(dn, baseDn, StringComparison.OrdinalIgnoreCase) ? $"all of {domain}" : Ldap.Path(dn, baseDn);

    private static string NewUsersOf(DirectoryToolsFile f) =>
        f.NewUsers ?? (string.Equals(f.Scope, BaseOf(f.Domain!), StringComparison.OrdinalIgnoreCase) ? $"CN=Users,{f.Scope}" : f.Scope!);

    private static string NewGroupsOf(DirectoryToolsFile f) => f.NewGroups ?? NewUsersOf(f);

    private static string NewComputersOf(DirectoryToolsFile f) =>
        f.NewComputers ?? (string.Equals(f.Scope, BaseOf(f.Domain!), StringComparison.OrdinalIgnoreCase) ? $"CN=Computers,{f.Scope}" : f.Scope!);

    // Read from disk once, then kept: only Save and Disconnect change it. Nobody changes the object handed out.
    private DirectoryToolsFile Load()
    {
        lock (gate)
        {
            return loaded ??= File.Exists(file) ? JsonSerializer.Deserialize<DirectoryToolsFile>(File.ReadAllText(file), Json) ?? new() : new();
        }
    }

    private void Save(DirectoryToolsFile f, bool keepConnections = false)
    {
        lock (gate)
        {
            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(f, Json));
            File.Move(temp, file, overwrite: true);
            loaded = f;
        }
        if (!keepConnections)
        {
            pool?.Flush(); // a new account or password: the connections signed in with the old one go
        }
    }
}

/// <summary>
/// A few signed-in connections kept open, so each request doesn't connect and bind again. One request has a connection
/// to itself at a time (the managed client isn't safe to share), so requests at once each get their own. A connection
/// idle for five minutes closes, one that failed isn't kept, and one that sat a while is checked before it's lent again.
/// </summary>
public sealed class LdapPool : IDisposable
{
    private const int Most = 4;
    private static readonly long Idle = (long) TimeSpan.FromMinutes(5).TotalMilliseconds;
    private static readonly long Life = (long) TimeSpan.FromMinutes(30).TotalMilliseconds;
    private static readonly long Trusted = (long) TimeSpan.FromSeconds(30).TotalMilliseconds;

    private readonly Func<CancellationToken, Task<ILdap>> open;
    private readonly Lock gate = new();
    private readonly List<Kept> kept = [];
    private readonly Timer sweep;
    private int generation;
    private bool disposed;

    private sealed record Kept(ILdap Ldap, long Opened, long Used, int Generation);

    public LdapPool(Func<CancellationToken, Task<ILdap>> open)
    {
        this.open = open;
        sweep = new Timer(_ => Close(TakeKept(k => Stale(k, Environment.TickCount64))), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>A connection for one request: one that's kept and still answers, else a new one. Dispose it to give it back.</summary>
    public async Task<ILdap> Take(CancellationToken stop)
    {
        while (true)
        {
            Kept? next = null;
            List<Kept> stale;
            lock (gate)
            {
                var now = Environment.TickCount64;
                stale = kept.Where(k => Stale(k, now)).ToList();
                kept.RemoveAll(stale.Contains);
                if (kept.Count > 0)
                {
                    next = kept[^1]; // the one used last is the likeliest to still be open
                    kept.RemoveAt(kept.Count - 1);
                }
            }
            Close(stale);
            if (next is null)
            {
                break;
            }
            try
            {
                if (Environment.TickCount64 - next.Used <= Trusted || await Alive(next.Ldap, stop))
                {
                    return new Lease(this, next.Ldap, next.Opened, next.Generation);
                }
            }
            catch
            {
                next.Ldap.Dispose();
                throw;
            }
            next.Ldap.Dispose(); // the domain controller dropped it: the next one, or a new sign-in
        }
        int current;
        lock (gate)
        {
            current = generation;
        }
        return new Lease(this, await open(stop), Environment.TickCount64, current);
    }

    /// <summary>Closes every kept connection, and those lent out as they come back: the account or its password changed.</summary>
    public void Flush()
    {
        List<Kept> all;
        lock (gate)
        {
            generation++;
            all = [.. kept];
            kept.Clear();
        }
        Close(all);
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
        }
        sweep.Dispose();
        Flush();
    }

    private void Return(ILdap ldap, long opened, int from, bool broken)
    {
        lock (gate)
        {
            if (!broken && !disposed && from == generation && kept.Count < Most)
            {
                kept.Add(new Kept(ldap, opened, Environment.TickCount64, from));
                return;
            }
        }
        ldap.Dispose();
    }

    private static bool Stale(Kept k, long now) => now - k.Used > Idle || now - k.Opened > Life;

    private List<Kept> TakeKept(Func<Kept, bool> which)
    {
        lock (gate)
        {
            var taken = kept.Where(which).ToList();
            kept.RemoveAll(taken.Contains);
            return taken;
        }
    }

    private static void Close(List<Kept> connections) => connections.ForEach(k => k.Ldap.Dispose());

    private static async Task<bool> Alive(ILdap ldap, CancellationToken stop)
    {
        try
        {
            return (await ldap.Find("", "(objectClass=*)", false, ["defaultNamingContext"], 1, stop)).Count > 0;
        }
        catch (Exception e) when (e is DirectoryProblem or LdapRefused)
        {
            return false;
        }
    }

    /// <summary>A kept connection while one request has it. Anything but a refusal from the directory means it isn't kept afterwards.</summary>
    private sealed class Lease(LdapPool pool, ILdap ldap, long opened, int generation) : ILdap
    {
        private bool broken;
        private int returned;

        public string BaseDn => ldap.BaseDn;

        public bool Encrypted => ldap.Encrypted;

        public Task<List<LdapRow>> Find(string root, string filter, bool subtree, string[] attributes, int limit, CancellationToken stop) =>
            Watch(ldap.Find(root, filter, subtree, attributes, limit, stop));

        public Task Add(string dn, IReadOnlyDictionary<string, object[]> attributes, CancellationToken stop) => Watch(ldap.Add(dn, attributes, stop));

        public Task Modify(string dn, IReadOnlyList<LdapMod> changes, CancellationToken stop) => Watch(ldap.Modify(dn, changes, stop));

        public Task Delete(string dn, CancellationToken stop) => Watch(ldap.Delete(dn, stop));

        public Task WriteDacl(string dn, byte[] descriptor, CancellationToken stop) => Watch(ldap.WriteDacl(dn, descriptor, stop));

        public Task Rename(string dn, string rdn, string parent, CancellationToken stop) => Watch(ldap.Rename(dn, rdn, parent, stop));

        public Task<List<LdapRow>> FindDeleted(string root, string filter, bool subtree, string[] attributes, int limit, CancellationToken stop) =>
            Watch(ldap.FindDeleted(root, filter, subtree, attributes, limit, stop));

        public Task Restore(string dn, string newDn, CancellationToken stop) => Watch(ldap.Restore(dn, newDn, stop));

        public Task<Dictionary<string, byte[][]>?> ReadAll(string dn, CancellationToken stop) => Watch(ldap.ReadAll(dn, stop));

        public Task<List<LdapRow>> FindChildren(string dn, string[] attributes, int limit, CancellationToken stop) => Watch(ldap.FindChildren(dn, attributes, limit, stop));

        public Task<string?> WhoAmI(CancellationToken stop) => Watch(ldap.WhoAmI(stop));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref returned, 1) == 0)
            {
                pool.Return(ldap, opened, generation, broken);
            }
        }

        private async Task<T> Watch<T>(Task<T> work)
        {
            try
            {
                return await work;
            }
            catch (Exception e) when (e is not LdapRefused)
            {
                broken = true;
                throw;
            }
        }

        private async Task Watch(Task work)
        {
            try
            {
                await work;
            }
            catch (Exception e) when (e is not LdapRefused)
            {
                broken = true;
                throw;
            }
        }
    }
}
