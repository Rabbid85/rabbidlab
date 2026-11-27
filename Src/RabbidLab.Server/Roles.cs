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

    /// <summary>The OUs this assignment's Active Directory tasks cover, by DN. None means the role's own, or everywhere.</summary>
    public List<string> Scope { get; set; } = [];

    /// <summary>When the assignment stops counting; null for never.</summary>
    public DateTime? Ends { get; set; }

    /// <summary>Why it was given, as the giver wrote it.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// A role: its name, colour and what it allows, and its tier: 0 is Administrator alone, 1 changes the roles below it and
/// the people who have them, 2 uses the lab. Only someone in a higher tier changes a role or gives it.
/// </summary>
public sealed class Role
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string About { get; set; } = "";
    public string Color { get; set; } = "";
    public int Tier { get; set; } = 1;
    public List<string> Permissions { get; set; } = [];

    /// <summary>
    /// The OUs its Active Directory permissions cover by default, by DN, for assignments without their own. None means
    /// everywhere the service account reaches. Since 1.1.372 the scope is set on each assignment.
    /// </summary>
    public List<string> Scope { get; set; } = [];

    /// <summary>Everyone in it signs in with a code from an authenticator app as well as their password (Mfa.cs).</summary>
    public bool Mfa { get; set; }
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
/// The roles, kept in roles.json in the data folder, by tier. Administrator is the one tier 0 role and allows everything;
/// User is tier 2 and everyone signed in has it. Someone can be in any number of roles and gets everything they allow,
/// through each assignment until it ends, in the OUs it covers. A role can only be changed or given by someone in a higher
/// tier, and the same goes for the passwords and MFA of the people who have it.
/// </summary>
public sealed partial class Roles(DataFolder folder, Accounts accounts, TimeProvider clock)
{
    public const string Admin = "admin";
    public const string User = "user";
    private const int MostRoles = 50;
    private const int MostPeople = 2000;
    private const int MostPlaces = 20;
    // 2: Change users, groups and computers became four permissions; 3: Group Policy; 4: Install apps; 5: Network;
    // 6: tiers in place of the order, and the scope on each assignment.
    private const int Current = 6;
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
                ? new Role { Id = r.Id, Name = r.Name, About = r.About, Color = r.Color, Tier = r.Tier, Permissions = [.. r.Permissions], Scope = [.. r.Scope], Mfa = r.Mfa, Members = [.. r.Members] } : null;
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
    /// The roles whose people and groups someone may not change in Active Directory: every role in their tier or above,
    /// their own included. Resetting such a password, or joining such a group, would hand them the role. User never counts.
    /// Only assignments that haven't ended are listed.
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
            var tier = TierOf(mine);
            return [.. f.Roles.Where(r => r.Id != User && r.Tier <= tier).Select(r => new Role { Id = r.Id, Name = r.Name, Tier = r.Tier, Members = [.. r.Members.Where(Active)] })];
        }
    }

    /// <summary>MFA is required for everyone (since 1.1.296, like Datto RMM and NinjaOne): every sign-in takes a code.</summary>
    public bool NeedsMfa(Who who) => true;

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
            return Fresh().Roles.Any(r => r.Members.Any(m => Active(m) && Matches(m, who)));
        }
    }

    /// <summary>The groups in roles: the ones a sign-in checks the person against.</summary>
    public IReadOnlyList<string> Groups()
    {
        lock (gate)
        {
            return [.. Fresh().Roles.SelectMany(r => r.Members).Where(m => m.Kind == Member.Group && Active(m)).Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>The roles someone may change: every role for an administrator, otherwise those in a lower tier than their own.</summary>
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
            return Above(Fresh(), username, by) is { } role ? $"{username} has the {role.Name} role. Only someone in a higher tier than {role.Name} can change this account." : null;
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

    /// <summary>
    /// A new role in a tier below its maker's (tier 1 by default for an administrator), starting with what another role
    /// allows (only what its maker may allow) or nothing.
    /// </summary>
    public (Role? Role, string? Problem) Create(string? name, string? about, string? color, string? copy, Who by, int? tier = null)
    {
        lock (gate)
        {
            var f = Fresh();
            if (f.Roles.Count >= MostRoles)
            {
                return (null, $"The limit of {MostRoles} roles has been reached. Delete a role first.");
            }
            var mine = TierOf(Mine(f, by));
            var t = tier ?? Math.Min(2, mine + 1);
            if (mine >= 2)
            {
                return (null, "Your roles do not allow you to create roles.");
            }
            if (t is not (1 or 2) || t <= mine)
            {
                return (null, mine == 0 ? "A new role goes in tier 1 or tier 2. Administrator is the only tier 0 role." : "You can create roles only in a lower tier than your own.");
            }
            var role = new Role { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant(), Color = "#6FA8FF", Tier = t };
            if (Describe(f, role, name, about, color) is { } problem)
            {
                return (null, problem);
            }
            if (copy is not null && f.Roles.FirstOrDefault(r => r.Id == copy) is { } from)
            {
                var can = Allowed(f, by);
                // Its scope comes along, and what the maker's own roles don't cover there is left out.
                role.Scope = from.Id == Admin ? [] : [.. from.Scope];
                role.Permissions = [.. (from.Id == Admin ? Permissions.All : (IEnumerable<string>) from.Permissions).Where(can.Contains)
                    .Where(p => !Permissions.Scoped.Contains(p) || Covers(Reach(f, by, p), role.Scope)).Order()];
            }
            f.Roles.Add(role);
            Arrange(f);
            Save(f);
            return (role, null);
        }
    }

    /// <summary>
    /// A role's name, what it's for, its colour, what it allows, its default scope in Active Directory and its tier.
    /// Administrator can't be changed, nor User's name or tier; a tier only goes to one below the changer's own.
    /// </summary>
    public string? Change(string id, string? name, string? about, string? color, IReadOnlyCollection<string>? permissions, Who by, IReadOnlyCollection<string>? scope = null, int? tier = null)
    {
        lock (gate)
        {
            var f = Fresh();
            var index = f.Roles.FindIndex(r => r.Id == id);
            if (index < 0)
            {
                return "The role no longer exists.";
            }
            var role = f.Roles[index];
            if (role.Id == Admin)
            {
                return "The Administrator role always has every permission and cannot be changed. Only its members can be changed.";
            }
            if (!Manages(f, by, index))
            {
                return $"Only someone in a higher tier than {role.Name} can change this role.";
            }
            if (role.Id == User && name is not null && name.Trim() != role.Name)
            {
                return "The User role cannot be renamed. It applies to everyone who signs in.";
            }
            if (tier is { } t && t != role.Tier)
            {
                if (role.Id == User)
                {
                    return "The User role is always tier 2.";
                }
                if (t is not (1 or 2))
                {
                    return "A role goes in tier 1 or tier 2. Administrator is the only tier 0 role.";
                }
                if (t <= TierOf(Mine(f, by)))
                {
                    return "You can put a role only in a lower tier than your own.";
                }
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
                    return $"The permission {unknown} does not exist.";
                }
                // Nobody gives or takes away what they can't do themselves.
                var mine = Allowed(f, by);
                var changed = permissions.Except(role.Permissions).Concat(role.Permissions.Except(permissions));
                if (changed.FirstOrDefault(p => !mine.Contains(p)) is { } beyond)
                {
                    return $"You can only grant permissions your own roles have. Your roles do not allow you to {PermissionName(beyond).ToLowerInvariant()}.";
                }
                allows = [.. permissions.Distinct().Order()];
            }
            var places = role.Scope;
            if (scope is not null)
            {
                places = Tidy(scope);
                if (places.FirstOrDefault(d => !IsDn(d)) is { } not)
                {
                    return $"{not} is not an OU in Active Directory.";
                }
                if (places.Count > MostPlaces)
                {
                    return $"A role's scope can include up to {MostPlaces} OUs.";
                }
            }
            // Nor a wider scope: what the role does in Active Directory stays inside what their own roles cover.
            var moved = places.Count != role.Scope.Count || places.Except(role.Scope, StringComparer.OrdinalIgnoreCase).Any();
            if (allows.Where(Permissions.Scoped.Contains).Any(p => (moved || !role.Permissions.Contains(p)) && !Covers(Reach(f, by, p), places)))
            {
                return "This role's scope cannot go beyond the scope of your own roles in Active Directory.";
            }
            role.Permissions = allows;
            role.Scope = places;
            role.Name = copy.Name;
            role.About = copy.About;
            role.Color = copy.Color;
            role.Tier = tier ?? role.Tier;
            Arrange(f);
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
                return "The role no longer exists.";
            }
            if (id is Admin or User)
            {
                return $"{f.Roles[index].Name} is a built-in role and cannot be deleted.";
            }
            if (!Manages(f, by, index))
            {
                return $"Only someone in a higher tier than {f.Roles[index].Name} can delete this role.";
            }
            f.Roles.RemoveAt(index);
            Save(f);
            return null;
        }
    }

    /// <summary>
    /// Gives a role: to an AD group or person, a local account, or everyone in AD; where in Active Directory its tasks
    /// work (OUs, or none for everywhere), until when (null for never), and why. The scope can't reach beyond the giver's
    /// own for the role's Active Directory permissions.
    /// </summary>
    public string? AddMember(string id, string? kind, string? name, string? display, Who by, IReadOnlyCollection<string>? scope = null, DateTime? ends = null, string? reason = null)
    {
        lock (gate)
        {
            var f = Fresh();
            var index = f.Roles.FindIndex(r => r.Id == id);
            if (index < 0)
            {
                return "The role no longer exists.";
            }
            var role = f.Roles[index];
            if (!Manages(f, by, index))
            {
                return $"Only someone in a higher tier than {role.Name} can give this role.";
            }
            var n = kind == Member.Everyone ? "ad" : name?.Trim() ?? "";
            if (kind is not (Member.Group or Member.Person or Member.Local or Member.Everyone) || n.Length is 0 or > 256)
            {
                return "Select a group, user or local account to add.";
            }
            if (kind == Member.Local && accounts.Find(n) is null)
            {
                return $"The local account {n} does not exist.";
            }
            if (role.Members.Any(m => m.Kind == kind && Same(m.Name, n)))
            {
                var words = Words(kind, n);
                return $"{(kind == Member.Person ? words : char.ToUpperInvariant(words[0]) + words[1..])} is already a member of {role.Name}.";
            }
            var m = new Member { Kind = kind, Name = n, Display = display?.Trim() is { Length: > 0 } d ? d : null, Added = Now, AddedBy = by.Username };
            if (Assign(f, role, m, by, scope ?? [], ends, reason) is { } problem)
            {
                return problem;
            }
            role.Members.Add(m);
            Save(f);
            return null;
        }
    }

    /// <summary>Changes where an assignment works, when it ends and why; the same checks as giving it.</summary>
    public string? EditMember(string id, string? kind, string? name, Who by, IReadOnlyCollection<string>? scope, DateTime? ends, string? reason)
    {
        lock (gate)
        {
            var f = Fresh();
            var index = f.Roles.FindIndex(r => r.Id == id);
            if (index < 0)
            {
                return "The role no longer exists.";
            }
            var role = f.Roles[index];
            if (!Manages(f, by, index))
            {
                return $"Only someone in a higher tier than {role.Name} can change who has this role.";
            }
            if (role.Members.FirstOrDefault(x => x.Kind == kind && Same(x.Name, name)) is not { } m)
            {
                return "This assignment no longer exists.";
            }
            var copy = new Member { Kind = m.Kind, Name = m.Name };
            if (Assign(f, role, copy, by, scope ?? [], ends, reason) is { } problem)
            {
                return problem;
            }
            if (role.Id == Admin && copy.Ends is not null && role.Members.Count(x => x != m && Active(x) && x.Ends is null) == 0)
            {
                return "The Administrator role must keep someone without an end date.";
            }
            (m.Scope, m.Ends, m.Reason) = (copy.Scope, copy.Ends, copy.Reason);
            Save(f);
            return null;
        }
    }

    // Checks and sets an assignment's scope, end and reason: OUs only, inside the giver's own reach for the role's
    // Active Directory permissions (none is everywhere, so a giver limited to some OUs must name them), an end in the
    // next five years. Why not, or null.
    private string? Assign(RolesFile f, Role role, Member m, Who by, IReadOnlyCollection<string> scope, DateTime? ends, string? reason)
    {
        var places = Tidy(scope);
        if (places.FirstOrDefault(d => !IsDn(d)) is { } not)
        {
            return $"{not} is not an OU in Active Directory.";
        }
        if (places.Count > MostPlaces)
        {
            return $"An assignment can cover up to {MostPlaces} OUs.";
        }
        var effective = places.Count > 0 ? places : role.Scope;
        if (role.Id != Admin && role.Permissions.Where(Permissions.Scoped.Contains).Any(p => !Covers(Reach(f, by, p), effective)))
        {
            return "This assignment can't reach further in Active Directory than your own roles do. Select OUs inside your own.";
        }
        if (ends is { } e)
        {
            var when = DateTime.SpecifyKind(e, DateTimeKind.Utc);
            if (when <= Now || when > Now.AddYears(5))
            {
                return "Select an end date in the next five years.";
            }
            ends = when;
        }
        var why = reason?.Trim() ?? "";
        if (why.Length > 300)
        {
            return "The reason can be up to 300 characters.";
        }
        (m.Scope, m.Ends, m.Reason) = (places, ends, why.Length > 0 ? why : null);
        return null;
    }

    public string? RemoveMember(string id, string? kind, string? name, Who by)
    {
        lock (gate)
        {
            var f = Fresh();
            var index = f.Roles.FindIndex(r => r.Id == id);
            if (index < 0)
            {
                return "The role no longer exists.";
            }
            var role = f.Roles[index];
            if (!Manages(f, by, index))
            {
                return $"Only someone in a higher tier than {role.Name} can take this role away.";
            }
            var member = role.Members.FirstOrDefault(m => m.Kind == kind && Same(m.Name, name));
            if (member is null)
            {
                return "This member is no longer in the role.";
            }
            if (role.Id == Admin && role.Members.Count == 1)
            {
                return "The Administrator role must have at least one member. Add another member first.";
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
                return $"{username} has the {above.Name} role. Only someone in a higher tier than {above.Name} can remove this account.";
            }
            var admins = f.Roles[0].Members;
            if (admins.Count == 1 && admins[0].Kind == Member.Local && Same(admins[0].Name, username))
            {
                return $"{username} is the only member of the Administrator role. Add another member first.";
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
    private Role? Above(RolesFile f, string username, Who by) =>
        f.Roles.Where((r, i) => r.Members.Any(m => m.Kind == Member.Local && Same(m.Name, username) && Active(m)) && !Manages(f, by, i)).FirstOrDefault();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Whether an assignment still counts: it has no end, or its end hasn't come.</summary>
    private bool Active(Member m) => m.Ends is not { } e || e > Now;

    /// <summary>The highest tier among roles (the lowest number); 2 with none but User.</summary>
    private static int TierOf(IEnumerable<Role> roles) => roles.Select(r => r.Tier).DefaultIfEmpty(2).Min();

    private List<Role> Mine(RolesFile f, Who who)
    {
        if (who.Source.Length == 0)
        {
            return [];
        }
        return [.. f.Roles.Where(r => r.Id == User || (r.Id == Admin && who.Source == SignIn.Windows) || r.Members.Any(m => Active(m) && Matches(m, who)))];
    }

    private IReadOnlySet<string> Allowed(RolesFile f, Who who)
    {
        var mine = Mine(f, who);
        return mine.Any(r => r.Id == Admin) ? Permissions.All : mine.SelectMany(r => r.Permissions).ToHashSet();
    }

    // Each assignment of a role that allows it reaches its own OUs (or its role's when it has none); one that reaches
    // everywhere makes it everywhere. User reaches what its role says.
    private IReadOnlyList<string>? Reach(RolesFile f, Who who, string permission)
    {
        var mine = Mine(f, who);
        if (mine.Any(r => r.Id == Admin))
        {
            return null;
        }
        var scopes = mine.Where(r => r.Permissions.Contains(permission))
            .SelectMany(r => r.Id == User ? [r.Scope] : r.Members.Where(m => Active(m) && Matches(m, who)).Select(m => m.Scope.Count > 0 ? m.Scope : r.Scope)).ToList();
        return scopes.Any(s => s.Count == 0) ? null : Tidy(scopes.SelectMany(s => s));
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

    // An administrator manages every role; anyone else only roles in a lower tier than their own highest.
    private bool Manages(RolesFile f, Who by, int index)
    {
        var mine = Mine(f, by);
        return mine.Any(r => r.Id == Admin) || TierOf(mine) < f.Roles[index].Tier;
    }

    // Administrator first, then the roles by tier and name, User last.
    private static void Arrange(RolesFile f)
    {
        var admin = f.Roles.First(r => r.Id == Admin);
        var user = f.Roles.First(r => r.Id == User);
        f.Roles = [admin, .. f.Roles.Where(r => r.Id is not (Admin or User)).OrderBy(r => r.Tier).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase), user];
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
            return "Enter a role name of up to 40 characters.";
        }
        if (f.Roles.Any(r => r.Id != role.Id && Same(r.Name, n)))
        {
            return $"A role named {n} already exists.";
        }
        var a = about?.Trim() ?? "";
        if (a.Length > 160)
        {
            return "The description can be up to 160 characters.";
        }
        var c = color?.Trim() ?? role.Color;
        if (!Colour().IsMatch(c))
        {
            return "Select one of the colours.";
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
            new Role { Id = Admin, Name = "Administrator", About = "All permissions, including access management. The only tier 0 role.", Color = "#FF6A45", Tier = 0 },
            new Role { Id = User, Name = "User", About = "Assigned to everyone who signs in. Always tier 2.", Color = "#8A8FA8", Tier = 2, Permissions = [.. Permissions.Basic] },
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

    /// <summary>Administrator first in tier 0, User last in tier 2, and every other role in tier 1 or 2, whatever the file says.</summary>
    private static void Repair(RolesFile f)
    {
        var defaults = Defaults();
        var admin = f.Roles.FirstOrDefault(r => r.Id == Admin) ?? defaults.Roles[0];
        var user = f.Roles.FirstOrDefault(r => r.Id == User) ?? defaults.Roles[1];
        (admin.Tier, user.Tier) = (0, 2);
        f.Roles = [admin, .. f.Roles.Where(r => r.Id is not (Admin or User)), user];
        foreach (var r in f.Roles.Where(r => r.Id is not (Admin or User) && r.Tier is not (1 or 2)))
        {
            r.Tier = 1;
        }
        Arrange(f);
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
        // Group Policy is the whole domain's: all of it for roles that set up the AD account, seeing it for those that see all of AD.
        foreach (var r in f.Roles.Where(_ => f.Version < 3))
        {
            var add = r.Permissions.Contains(Permissions.AdConnect) ? Permissions.Gpo
                : r.Permissions.Contains(Permissions.AdView) && r.Scope.Count == 0 ? [Permissions.GpoView] : [];
            r.Permissions = [.. r.Permissions.Union(add).Order()];
        }
        // Install apps came after: roles that run scripts on computers could install an app that way already.
        foreach (var r in f.Roles.Where(r => f.Version < 4 && (r.Permissions.Contains(Permissions.ScriptsRun) || r.Permissions.Contains(Permissions.ScriptsEdit))))
        {
            r.Permissions = [.. r.Permissions.Union([Permissions.SeatsInstalls]).Order()];
        }
        // Network came after: roles that see computers see it, and roles that change Devices' settings can scan and set it up.
        foreach (var r in f.Roles.Where(_ => f.Version < 5))
        {
            var add = new List<string>();
            if (r.Permissions.Contains(Permissions.SeatsView))
            {
                add.Add(Permissions.NetworkView);
            }
            if (r.Permissions.Contains(Permissions.SeatsSettings))
            {
                add.AddRange([Permissions.NetworkView, Permissions.NetworkScan, Permissions.NetworkSettings]);
            }
            r.Permissions = [.. r.Permissions.Union(add).Order()];
        }
        // Tiers came after: every role between Administrator and User is tier 1 (Repair), and a role's scope moves onto
        // each of its assignments, where it's set from now on.
        foreach (var r in f.Roles.Where(r => f.Version < 6 && r.Id is not (Admin or User) && r.Scope.Count > 0))
        {
            foreach (var m in r.Members.Where(m => m.Scope.Count == 0))
            {
                m.Scope = [.. r.Scope];
            }
            r.Scope = [];
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
