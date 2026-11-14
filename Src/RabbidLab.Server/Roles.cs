using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>Who is in a role: an Active Directory group or person, a local account, or everyone in Active Directory.</summary>
public sealed class Member
{
    public const string Group = "group";
    public const string Person = "user";
    public const string Local = "local";
    public const string Everyone = "everyone";

    public string Kind { get; set; } = "";

    /// <summary>The group's name, the person's username, the local account's username, or "ad" for everyone.</summary>
    public string Name { get; set; } = "";

    /// <summary>A person's full name, when it's known.</summary>
    public string? Display { get; set; }
    public DateTime Added { get; set; }
    public string? AddedBy { get; set; }
}

/// <summary>A role: its name, colour and what it allows. Its place in the list is its rank, highest first.</summary>
public sealed class Role
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string About { get; set; } = "";
    public string Color { get; set; } = "";
    public List<string> Permissions { get; set; } = [];

    /// <summary>The OUs its Active Directory permissions cover, by DN. None means everywhere the service account reaches.</summary>
    public List<string> Scope { get; set; } = [];
    public List<Member> Members { get; set; } = [];
}

/// <summary>Someone who has signed in: who they are, and the groups in roles they were in then.</summary>
public sealed class Person
{
    public string Source { get; set; } = "";
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Domain { get; set; }
    public List<string> Groups { get; set; } = [];
    public DateTime LastSignIn { get; set; }
}

public sealed class RolesFile
{
    /// <summary>Which permissions the file was written with: an older one gets the permissions split out since (Roles.Upgrade).</summary>
    public int Version { get; set; }
    public List<Role> Roles { get; set; } = [];
    public List<Person> People { get; set; } = [];
}

/// <summary>Who is asking, the way roles see them: how they signed in, their name, and the groups in roles they're in.</summary>
public sealed record Who(string Source, string Username, string? Domain, IReadOnlyCollection<string> Groups)
{
    public static Who Of(ClaimsPrincipal p) =>
        new(p.FindFirstValue("source") ?? "", p.Identity?.Name ?? "", p.FindFirstValue("domain"), [.. p.FindAll("group").Select(c => c.Value)]);

    public static Who Of(Session s) => new(s.Source, s.Username, s.Domain, s.Groups);
}

/// <summary>
/// The roles, kept in roles.json in the data folder, highest first. Administrator is always at the top and allows everything;
/// User is always at the bottom and everyone signed in has it. Someone can be in any number of roles and gets everything
/// they allow. A role can only be changed by someone whose own highest role is above it, or by an administrator.
/// </summary>
public sealed partial class Roles(DataFolder folder, Accounts accounts)
{
    public const string Admin = "admin";
    public const string User = "user";
    private const int MostRoles = 50;
    private const int MostPeople = 2000;
    private const int MostPlaces = 20;
    private const int Current = 2; // 2: Change users, groups and computers became four permissions
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    // Watched: --add-admin writes it while the service runs.
    private readonly JsonFile<RolesFile> file = folder.Json<RolesFile>("roles.json", Json, watch: true);
    private RolesFile? seen; // the file as last read, already put in order

    private Lock gate => file.Gate;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Colour();

    /// <summary>A copy of everything, to show.</summary>
    public RolesFile Snapshot()
    {
        lock (gate)
        {
            return JsonSerializer.Deserialize<RolesFile>(JsonSerializer.Serialize(Fresh(), Json), Json)!;
        }
    }

    /// <summary>The roles someone has, highest first. Everyone signed in has User; Windows administrators are administrators.</summary>
    public IReadOnlyList<Role> Of(Who who)
    {
        lock (gate)
        {
            return Mine(Fresh(), who);
        }
    }

    /// <summary>Everything someone may do.</summary>
    public IReadOnlySet<string> Allowed(Who who)
    {
        lock (gate)
        {
            return Allowed(Fresh(), who);
        }
    }

    public bool Can(ClaimsPrincipal user, string permission) => user.Identity?.IsAuthenticated == true && Allowed(Who.Of(user)).Contains(permission);

    /// <summary>A copy of one role, or null.</summary>
    public Role? Find(string id)
    {
        lock (gate)
        {
            return Fresh().Roles.FirstOrDefault(x => x.Id == id) is { } r
                ? new Role { Id = r.Id, Name = r.Name, About = r.About, Color = r.Color, Permissions = [.. r.Permissions], Scope = [.. r.Scope], Members = [.. r.Members] } : null;
        }
    }

    /// <summary>
    /// Where in Active Directory a permission reaches for someone: null is everywhere, else the OUs of their roles that
    /// allow it. One role without a scope that allows it is enough for everywhere.
    /// </summary>
    public IReadOnlyList<string>? Reach(Who who, string permission)
    {
        lock (gate)
        {
            return Reach(Fresh(), who, permission);
        }
    }

    /// <summary>
    /// The roles whose people and groups someone may not change in Active Directory: the ones they aren't in and aren't
    /// above. Resetting such a password, or joining such a group, would hand them the role. User never counts.
    /// </summary>
    public IReadOnlyList<Role> Outranking(Who by)
    {
        lock (gate)
        {
            var f = Fresh();
            var mine = Mine(f, by);
            if (mine.Any(r => r.Id == Admin))
            {
                return [];
            }
            return [.. f.Roles.Where((r, i) => r.Id != User && !mine.Contains(r) && !Manages(f, by, i)).Select(r => new Role { Id = r.Id, Name = r.Name, Members = [.. r.Members] })];
        }
    }

    /// <summary>Roles as the page lists them beside a person: id, name and colour.</summary>
    public static IEnumerable<object> Brief(IEnumerable<Role> roles) => roles.Select(r => new { id = r.Id, name = r.Name, color = r.Color });

    /// <summary>
    /// Whether someone may be signed in: Active Directory accounts must be in a role, User included. Local accounts and this
    /// computer's administrators always may.
    /// </summary>
    public bool CanSignIn(Who who)
    {
        if (who.Source != SignIn.Directory)
        {
            return true;
        }
        lock (gate)
        {
            return Fresh().Roles.Any(r => r.Members.Any(m => Matches(m, who)));
        }
    }

    /// <summary>The groups in roles: the ones a sign-in checks the person against.</summary>
    public IReadOnlyList<string> Groups()
    {
        lock (gate)
        {
            return [.. Fresh().Roles.SelectMany(r => r.Members).Where(m => m.Kind == Member.Group).Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>The roles someone may change: every role for an administrator, otherwise those below their own highest.</summary>
    public IReadOnlySet<string> Managed(Who by)
    {
        lock (gate)
        {
            var f = Fresh();
            return f.Roles.Where((_, i) => Manages(f, by, i)).Select(r => r.Id).ToHashSet();
        }
    }

    /// <summary>Why someone can't change this local account (it's in a role they don't manage), or null.</summary>
    public string? Refuses(string username, Who by)
    {
        lock (gate)
        {
            return Above(Fresh(), username, by) is { } role ? $"{username} is in {role.Name}. Only someone above {role.Name} can change that account." : null;
        }
    }

    /// <summary>A sign-in, so the people list knows who they are and which groups they were in.</summary>
    public void Seen(Who who, string name)
    {
        lock (gate)
        {
            var f = Fresh();
            var p = f.People.FirstOrDefault(x => x.Source == who.Source && Same(x.Username, who.Username));
            if (p is null)
            {
                p = new Person { Source = who.Source, Username = who.Username };
                f.People.Add(p);
            }
            p.Name = name;
            p.Domain = who.Domain;
            p.Groups = [.. who.Groups];
            p.LastSignIn = DateTime.UtcNow;
            if (f.People.Count > MostPeople)
            {
                f.People = [.. f.People.OrderByDescending(x => x.LastSignIn).Take(MostPeople)];
            }
            Save(f);
        }
    }

    /// <summary>A new Rabbid Lab: whoever set it up is the administrator, and User is everyone in Active Directory when that's how people sign in.</summary>
    public void SetUp(Member admin, Member? everyone)
    {
        lock (gate)
        {
            var f = Defaults();
            admin.Added = DateTime.UtcNow;
            admin.AddedBy = "Setup";
            f.Roles[0].Members.Add(admin);
            if (everyone is not null)
            {
                everyone.Added = DateTime.UtcNow;
                everyone.AddedBy = "Setup";
                f.Roles[^1].Members.Add(everyone);
            }
            Save(f);
        }
    }

    /// <summary>Someone put in a role by Rabbid Lab itself, such as by --add-admin: no checks, and nothing when they're in it already.</summary>
    public void Grant(string id, Member member)
    {
        lock (gate)
        {
            var f = Fresh();
            if (f.Roles.FirstOrDefault(r => r.Id == id) is not { } role || role.Members.Any(m => m.Kind == member.Kind && Same(m.Name, member.Name)))
            {
                return;
            }
            member.Added = DateTime.UtcNow;
            member.AddedBy ??= Product.Title;
            role.Members.Add(member);
            Save(f);
        }
    }

    /// <summary>A new role just above User, starting with what another role allows (only what its maker may allow) or nothing.</summary>
    public (Role? Role, string? Problem) Create(string? name, string? about, string? color, string? copy, Who by)
    {
        lock (gate)
        {
            var f = Fresh();
            if (f.Roles.Count >= MostRoles)
            {
                return (null, $"That's {MostRoles} roles already. Delete one first.");
            }
            if (!Manages(f, by, f.Roles.Count - 1))
            {
                return (null, "Your roles don't let you make roles.");
            }
            var role = new Role { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant(), Color = "#6FA8FF" };
            if (Describe(f, role, name, about, color) is { } problem)
            {
                return (null, problem);
            }
            if (copy is not null && f.Roles.FirstOrDefault(r => r.Id == copy) is { } from)
            {
                var mine = Allowed(f, by);
                // Its scope comes along, and what the maker's own roles don't cover there is left out.
                role.Scope = from.Id == Admin ? [] : [.. from.Scope];
                role.Permissions = [.. (from.Id == Admin ? Permissions.All : (IEnumerable<string>) from.Permissions).Where(mine.Contains)
                    .Where(p => !Permissions.Scoped.Contains(p) || Covers(Reach(f, by, p), role.Scope)).Order()];
            }
            f.Roles.Insert(f.Roles.Count - 1, role);
            Save(f);
            return (role, null);
        }
    }

    /// <summary>A role's name, what it's for, its colour, what it allows and its scope in Active Directory. Administrator can't be changed, nor User's name.</summary>
    public string? Change(string id, string? name, string? about, string? color, IReadOnlyCollection<string>? permissions, Who by, IReadOnlyCollection<string>? scope = null)
    {
        lock (gate)
        {
            var f = Fresh();
            var index = f.Roles.FindIndex(r => r.Id == id);
            if (index < 0)
            {
                return "That role isn't there any more.";
            }
            var role = f.Roles[index];
            if (role.Id == Admin)
            {
                return "Administrator always allows everything, so it can't be changed. Only who's in it can.";
            }
            if (!Manages(f, by, index))
            {
                return $"Only someone above {role.Name} can change it.";
            }
            if (role.Id == User && name is not null && name.Trim() != role.Name)
            {
                return "User keeps its name: it's everyone who signs in.";
            }
            var copy = new Role { Id = role.Id, Name = role.Name, About = role.About, Color = role.Color };
            if (Describe(f, copy, name ?? role.Name, about ?? role.About, color ?? role.Color) is { } problem)
            {
                return problem;
            }
            var allows = role.Permissions;
            if (permissions is not null)
            {
                if (permissions.FirstOrDefault(p => !Permissions.All.Contains(p)) is { } unknown)
                {
                    return $"There's no permission called {unknown}.";
                }
                // Nobody gives or takes away what they can't do themselves.
                var mine = Allowed(f, by);
                var changed = permissions.Except(role.Permissions).Concat(role.Permissions.Except(permissions));
                if (changed.FirstOrDefault(p => !mine.Contains(p)) is { } beyond)
                {
                    return $"You can only change what your own roles allow, and they don't allow {PermissionName(beyond).ToLowerInvariant()}.";
                }
                allows = [.. permissions.Distinct().Order()];
            }
            var places = role.Scope;
            if (scope is not null)
            {
                places = Tidy(scope);
                if (places.FirstOrDefault(d => !IsDn(d)) is { } not)
                {
                    return $"{not} isn't an OU in Active Directory.";
                }
                if (places.Count > MostPlaces)
                {
                    return $"A role's scope is {MostPlaces} OUs at most.";
                }
            }
            // Nor a wider scope: what the role does in Active Directory stays inside what their own roles cover.
            var moved = places.Count != role.Scope.Count || places.Except(role.Scope, StringComparer.OrdinalIgnoreCase).Any();
            if (allows.Where(Permissions.Scoped.Contains).Any(p => (moved || !role.Permissions.Contains(p)) && !Covers(Reach(f, by, p), places)))
            {
                return "This role's scope can only cover what your own roles cover in Active Directory.";
            }
            role.Permissions = allows;
            role.Scope = places;
            role.Name = copy.Name;
            role.About = copy.About;
            role.Color = copy.Color;
            Save(f);
            return null;
        }
    }

    public string? Delete(string id, Who by)
    {
        lock (gate)
        {
            var f = Fresh();
            var index = f.Roles.FindIndex(r => r.Id == id);
            if (index < 0)
            {
                return "That role isn't there any more.";
            }
            if (id is Admin or User)
            {
                return $"{f.Roles[index].Name} is built in, so it can't be deleted.";
            }
            if (!Manages(f, by, index))
            {
                return $"Only someone above {f.Roles[index].Name} can delete it.";
            }
            f.Roles.RemoveAt(index);
            Save(f);
            return null;
        }
    }

    /// <summary>A new order for the roles between Administrator and User. Roles someone can't change keep their places.</summary>
    public string? Order(IReadOnlyList<string> ids, Who by)
    {
        lock (gate)
        {
            var f = Fresh();
            var middle = f.Roles.Skip(1).Take(f.Roles.Count - 2).ToList();
            if (ids.Count != middle.Count || !ids.Order().SequenceEqual(middle.Select(r => r.Id).Order()))
            {
                return "The roles changed since this page loaded. Try again.";
            }
            List<Role> order = [f.Roles[0], .. ids.Select(id => middle.First(r => r.Id == id)), f.Roles[^1]];
            // Roles at or above someone's own highest one stay where they are, and nothing moves above them.
            for (var i = 0; i < f.Roles.Count; i++)
            {
                if (!Manages(f, by, i) && order.IndexOf(f.Roles[i]) != i)
                {
                    return $"Only someone above {f.Roles[i].Name} can move it, or move a role above it.";
                }
            }
            f.Roles = order;
            Save(f);
            return null;
        }
    }

    public string? AddMember(string id, string? kind, string? name, string? display, Who by)
    {
        lock (gate)
        {
            var f = Fresh();
            var index = f.Roles.FindIndex(r => r.Id == id);
            if (index < 0)
            {
                return "That role isn't there any more.";
            }
            var role = f.Roles[index];
            if (!Manages(f, by, index))
            {
                return $"Only someone above {role.Name} can add people to it.";
            }
            var n = kind == Member.Everyone ? "ad" : name?.Trim() ?? "";
            if (kind is not (Member.Group or Member.Person or Member.Local or Member.Everyone) || n.Length is 0 or > 256)
            {
                return "Pick a group, a person or a local account to add.";
            }
            if (kind == Member.Local && accounts.Find(n) is null)
            {
                return $"There's no local account called {n}.";
            }
            if (role.Members.Any(m => m.Kind == kind && Same(m.Name, n)))
            {
                var words = Words(kind, n);
                return $"{(kind == Member.Person ? words : char.ToUpperInvariant(words[0]) + words[1..])} is already in {role.Name}.";
            }
            role.Members.Add(new Member { Kind = kind, Name = n, Display = display?.Trim() is { Length: > 0 } d ? d : null, Added = DateTime.UtcNow, AddedBy = by.Username });
            Save(f);
            return null;
        }
    }

    public string? RemoveMember(string id, string? kind, string? name, Who by)
    {
        lock (gate)
        {
            var f = Fresh();
            var index = f.Roles.FindIndex(r => r.Id == id);
            if (index < 0)
            {
                return "That role isn't there any more.";
            }
            var role = f.Roles[index];
            if (!Manages(f, by, index))
            {
                return $"Only someone above {role.Name} can take people out of it.";
            }
            var member = role.Members.FirstOrDefault(m => m.Kind == kind && Same(m.Name, name));
            if (member is null)
            {
                return "They're not in that role any more.";
            }
            if (role.Id == Admin && role.Members.Count == 1)
            {
                return "Administrator needs someone in it. Add someone else first.";
            }
            role.Members.Remove(member);
            Save(f);
            return null;
        }
    }

    /// <summary>Removes a local account (remove does that, and says why not) unless it's the only administrator, and takes it out of every role.</summary>
    public string? Forget(string username, Who by, Func<string?> remove)
    {
        lock (gate)
        {
            var f = Fresh();
            if (Above(f, username, by) is { } above)
            {
                return $"{username} is in {above.Name}. Only someone above {above.Name} can remove that account.";
            }
            var admins = f.Roles[0].Members;
            if (admins.Count == 1 && admins[0].Kind == Member.Local && Same(admins[0].Name, username))
            {
                return $"{username} is the only one in Administrator. Add someone else to it first.";
            }
            if (remove() is { } problem)
            {
                return problem;
            }
            foreach (var r in f.Roles)
            {
                r.Members.RemoveAll(m => m.Kind == Member.Local && Same(m.Name, username));
            }
            f.People.RemoveAll(p => p.Source == SignIn.Local && Same(p.Username, username));
            Save(f);
            return null;
        }
    }

    /// <summary>The words for a member, such as "the group Help Desk"; a person by name.</summary>
    public static string Words(string? kind, string? name, string? display = null) => kind switch
    {
        Member.Group => $"the group {name}",
        Member.Local => $"the local account {name}",
        Member.Everyone => "everyone in Active Directory",
        _ => display is { Length: > 0 } ? $"{display} ({name})" : name ?? "",
    };

    /// <summary>A permission's name as the page shows it, or its key.</summary>
    public static string PermissionName(string key) => Permissions.Shown().SelectMany(p => p.Actions).FirstOrDefault(a => a.Key == key)?.Name ?? key;

    /// <summary>The first role this local account is in that someone doesn't manage, or null.</summary>
    private static Role? Above(RolesFile f, string username, Who by) =>
        f.Roles.Where((r, i) => r.Members.Any(m => m.Kind == Member.Local && Same(m.Name, username)) && !Manages(f, by, i)).FirstOrDefault();

    private static List<Role> Mine(RolesFile f, Who who)
    {
        if (who.Source.Length == 0)
        {
            return [];
        }
        return [.. f.Roles.Where(r => r.Id == User || (r.Id == Admin && who.Source == SignIn.Windows) || r.Members.Any(m => Matches(m, who)))];
    }

    private static IReadOnlySet<string> Allowed(RolesFile f, Who who)
    {
        var mine = Mine(f, who);
        return mine.Any(r => r.Id == Admin) ? Permissions.All : mine.SelectMany(r => r.Permissions).ToHashSet();
    }

    private static IReadOnlyList<string>? Reach(RolesFile f, Who who, string permission)
    {
        var mine = Mine(f, who);
        if (mine.Any(r => r.Id == Admin))
        {
            return null;
        }
        var giving = mine.Where(r => r.Permissions.Contains(permission)).ToList();
        return giving.Any(r => r.Scope.Count == 0) ? null : Tidy(giving.SelectMany(r => r.Scope));
    }

    /// <summary>Whether a reach (null is everywhere) takes in every OU of a scope (none is everywhere).</summary>
    private static bool Covers(IReadOnlyList<string>? reach, IReadOnlyCollection<string> scope) =>
        reach is null || (scope.Count > 0 && scope.All(s => reach.Any(r => Ldap.Under(s, r))));

    /// <summary>OUs without repeats, and without the ones inside another on the list.</summary>
    private static List<string> Tidy(IEnumerable<string> dns)
    {
        var all = dns.Select(d => d?.Trim() ?? "").Where(d => d.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return [.. all.Where(d => !all.Any(o => !Same(o, d) && Ldap.Under(d, o)))];
    }

    private static bool IsDn(string dn) =>
        dn.Length <= 1024 && Ldap.Split(dn) is { Count: > 0 } parts && parts.All(p => p.IndexOf('=') > 0) && parts[^1].StartsWith("DC=", StringComparison.OrdinalIgnoreCase);

    private static bool Manages(RolesFile f, Who by, int index)
    {
        var mine = Mine(f, by);
        return mine.Any(r => r.Id == Admin) || mine.Any(r => f.Roles.IndexOf(r) < index);
    }

    private static bool Matches(Member m, Who who) => m.Kind switch
    {
        Member.Everyone => who.Source == SignIn.Directory,
        Member.Group => who.Source == SignIn.Directory && who.Groups.Any(g => Same(g, m.Name)),
        Member.Person => who.Source == SignIn.Directory && Same(m.Name, who.Username),
        Member.Local => who.Source == SignIn.Local && Same(m.Name, who.Username),
        _ => false,
    };

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Checks and sets a role's name, what it's for and its colour. Why not, or null.</summary>
    private static string? Describe(RolesFile f, Role role, string? name, string? about, string? color)
    {
        var n = name?.Trim() ?? "";
        if (n.Length is 0 or > 40)
        {
            return "Give the role a name of up to 40 characters.";
        }
        if (f.Roles.Any(r => r.Id != role.Id && Same(r.Name, n)))
        {
            return $"There's already a role called {n}.";
        }
        var a = about?.Trim() ?? "";
        if (a.Length > 160)
        {
            return "Keep what it's for to 160 characters.";
        }
        var c = color?.Trim() ?? role.Color;
        if (!Colour().IsMatch(c))
        {
            return "Pick one of the colours.";
        }
        role.Name = n;
        role.About = a;
        role.Color = c.ToUpperInvariant();
        return null;
    }

    private static RolesFile Defaults() => new()
    {
        Version = Current,
        Roles =
        [
            new Role { Id = Admin, Name = "Administrator", About = "Everything, including who gets in. Always at the top.", Color = "#FF6A45" },
            new Role { Id = User, Name = "User", About = "Everyone who signs in has it. Always at the bottom.", Color = "#8A8FA8", Permissions = [.. Permissions.Basic] },
        ],
    };

    /// <summary>
    /// Roles made from the accounts: local admins and the admins group in Administrator, the users group (or everyone) in
    /// User. Only for a Rabbid Lab from before roles (legacy) does no admins group mean everyone is an administrator.
    /// </summary>
    private RolesFile Rebuild(bool legacy)
    {
        var f = Defaults();
        var now = DateTime.UtcNow;
        Member Add(Role r, string kind, string name)
        {
            var m = new Member { Kind = kind, Name = name, Added = now, AddedBy = Product.Title };
            r.Members.Add(m);
            return m;
        }
        foreach (var a in accounts.List().Where(a => a.Role != User))
        {
            Add(f.Roles[0], Member.Local, a.Username);
        }
        if (accounts.Directory is { } d)
        {
            if (d.AdminGroup.Trim().Length > 0)
            {
                Add(f.Roles[0], Member.Group, d.AdminGroup.Trim());
            }
            else if (legacy)
            {
                Add(f.Roles[0], Member.Everyone, "ad");
            }
            if (d.Group.Trim().Length > 0)
            {
                Add(f.Roles[^1], Member.Group, d.Group.Trim());
            }
            else
            {
                Add(f.Roles[^1], Member.Everyone, "ad");
            }
        }
        return f;
    }

    /// <summary>The roles as they are now. A file with no roles in it is made again from the accounts.</summary>
    private RolesFile Fresh()
    {
        var f = file.Read();
        if (f.Roles.Count == 0)
        {
            // Only a Rabbid Lab that never kept roles takes them from its old settings; a lost file makes nobody an administrator.
            var legacy = !accounts.HasRoles && !file.Exists;
            f.Roles = Rebuild(legacy).Roles;
            f.Version = Current;
            if (accounts.SetUp)
            {
                if (!legacy)
                {
                    folder.Log.LogWarning("roles.json was missing or unreadable, so the built-in roles were made again from the accounts. Check who is in each role; add an administrator with --add-admin if nobody is");
                }
                Save(f);
            }
        }
        else if (!ReferenceEquals(f, seen))
        {
            Repair(f);
            if (Upgrade(f) && accounts.SetUp)
            {
                file.Save(f);
            }
            if (accounts.SetUp)
            {
                accounts.NoteRoles();
            }
        }
        seen = f;
        return f;
    }

    /// <summary>Administrator first and User last, whatever the file says.</summary>
    private static void Repair(RolesFile f)
    {
        var defaults = Defaults();
        var admin = f.Roles.FirstOrDefault(r => r.Id == Admin) ?? defaults.Roles[0];
        var user = f.Roles.FirstOrDefault(r => r.Id == User) ?? defaults.Roles[1];
        f.Roles = [admin, .. f.Roles.Where(r => r.Id is not (Admin or User)), user];
    }

    /// <summary>A file from before a permission was split gets its parts, so nobody loses what they could do. Whether anything changed.</summary>
    private static bool Upgrade(RolesFile f)
    {
        if (f.Version >= Current)
        {
            return false;
        }
        foreach (var r in f.Roles.Where(r => r.Permissions.Contains(Permissions.AdChange)))
        {
            r.Permissions = [.. r.Permissions.Union(Permissions.WasChange).Order()];
        }
        f.Version = Current;
        return true;
    }

    private void Save(RolesFile f)
    {
        file.Save(f);
        seen = f;
        accounts.NoteRoles();
    }
}
