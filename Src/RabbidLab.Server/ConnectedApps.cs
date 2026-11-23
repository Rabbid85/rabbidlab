using System.Security.Cryptography;
using System.Text.Json;
using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>An account in a connected app, as last read: Pick is the AD user someone matched it to by hand, or "-" for not a person.</summary>
public sealed class AppAccount
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Login { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime? LastSignIn { get; set; }
    public string? Pick { get; set; }
}

/// <summary>A group or role in a connected app, with the ids of the accounts in it.</summary>
public sealed class AppGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Group";
    public List<string> Members { get; set; } = [];
}

public sealed class AppLogLine
{
    public DateTime At { get; set; }
    public string By { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>done, skipped or failed.</summary>
    public string Result { get; set; } = "done";
}

/// <summary>
/// An app Rabbid Lab is connected to: what kind, how to reach it (its secret encrypted), how its accounts are matched to
/// Active Directory users (email, username or hand), whether Rabbid Lab may make accounts there, and what was last read.
/// </summary>
public sealed class ConnectedApp
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, string> Settings { get; set; } = [];
    public string? Secret { get; set; }
    public string Match { get; set; } = "email";
    public bool Create { get; set; } = true;
    public bool Paused { get; set; }
    public DateTime? LastSync { get; set; }

    /// <summary>Why the last sync failed, or null when it worked.</summary>
    public string? Problem { get; set; }
    public List<AppAccount> Accounts { get; set; } = [];
    public List<AppGroup> Groups { get; set; } = [];
    public List<AppLogLine> Log { get; set; } = [];
    public string By { get; set; } = "";
    public DateTime Made { get; set; }
}

public sealed class AccessTarget
{
    public string App { get; set; } = "";
    public string Group { get; set; } = "";
}

/// <summary>
/// An access rule: members of an Active Directory group get these groups and roles in the apps, one or several in each.
/// Mode auto gives them at each sync; hand waits for someone to apply them. Keep holds the ones given by hand that stay.
/// </summary>
public sealed class AccessRule
{
    public string Id { get; set; } = "";
    public string Ad { get; set; } = "";
    public string AdName { get; set; } = "";
    public List<AccessTarget> To { get; set; } = [];
    public string Mode { get; set; } = "auto";
    public List<string> Keep { get; set; } = [];
    public string By { get; set; } = "";
    public DateTime Made { get; set; }
}

public sealed class ConnectedAppsFile
{
    public List<ConnectedApp> Apps { get; set; } = [];
    public List<AccessRule> Rules { get; set; } = [];
}

/// <summary>Something a connected app refused or that went wrong reaching it, said for the page.</summary>
public sealed class AppProblem(string message) : Exception(message);

/// <summary>A setting a connector asks for: Secret ones are kept encrypted and never sent back to a browser.</summary>
public sealed record ConnectorField(string Key, string Label, string Hint = "", bool Secret = false, bool Long = false);

/// <summary>How to reach an app: its settings and its secret, decrypted for the call.</summary>
public sealed record AppLink(IReadOnlyDictionary<string, string> Settings, string? Secret)
{
    public string this[string key] => Settings.GetValueOrDefault(key)?.Trim() ?? "";
}

public sealed record AppSnapshot(List<AppAccount> Accounts, List<AppGroup> Groups);

/// <summary>Someone in Active Directory, as an app needs them to make an account.</summary>
public sealed record AppPerson(string Username, string Name, string Email, string First, string Last);

/// <summary>
/// One kind of app Rabbid Lab can connect to. Each kind is its own file. It reads the app's accounts and its groups or
/// roles, and makes the changes an access rule asks for.
/// </summary>
public interface IAppConnector
{
    string Kind { get; }
    string Name { get; }
    string About { get; }

    /// <summary>What an account and a group are called there: user, operator, login; group, role.</summary>
    string Account { get; }
    string Group { get; }
    IReadOnlyList<ConnectorField> Fields { get; }
    bool CanCreate { get; }

    /// <summary>Why these settings can't be used, or null.</summary>
    string? Bad(AppLink link);

    Task<AppSnapshot> Read(AppLink link, CancellationToken stop);

    /// <summary>Makes an account and gives its id.</summary>
    Task<string> Create(AppLink link, AppPerson who, CancellationToken stop);

    Task AddMember(AppLink link, AppGroup group, AppAccount account, CancellationToken stop);

    Task RemoveMember(AppLink link, AppGroup group, AppAccount account, CancellationToken stop);

    Task Disable(AppLink link, AppAccount account, CancellationToken stop);
}

/// <summary>An Active Directory user as the rules see them: the groups they are in directly, by DN.</summary>
public sealed record AppUser(string Username, string Name, string Email, bool Enabled, IReadOnlySet<string> Groups, string First, string Last);

/// <summary>
/// One difference between the rules and an app. Kind: create (an account to make), add (a group or role to give), drift
/// (one given by hand: the person is in no AD group whose rule gives it), blocked (can't be done, with why).
/// </summary>
public sealed record PlanItem(string Key, string Kind, string Rule, string RuleName, string App, string AppName, string Group, string GroupName, string User, string UserName,
    string? Account, string Text, string Why, bool Auto);

/// <summary>How an app's accounts match Active Directory users: by account id, and the users two accounts claim.</summary>
public sealed record AppMatches(IReadOnlyDictionary<string, string> UserOf, IReadOnlySet<string> Torn)
{
    public AppAccount? AccountOf(ConnectedApp app, string username) =>
        app.Accounts.FirstOrDefault(a => UserOf.TryGetValue(a.Id, out var u) && string.Equals(u, username, StringComparison.OrdinalIgnoreCase));
}

public sealed record ConnectedAppForm(string? Id, string? Kind, string? Name, Dictionary<string, string>? Settings, string? Secret, string? Match, bool? Create);

public sealed record AccessRuleForm(string? Id, string? Ad, string? AdName, List<AccessTarget>? To, string? Mode);

/// <summary>
/// Connected apps (connected-apps.json): the apps, what was last read from each, and the access rules that say which
/// Active Directory group gets which groups and roles there. Every difference between the rules and the apps is worked
/// out here as a plan, and nothing changes in an app except by applying items of that plan: at a sync for rules that give
/// automatically, or when someone reviews and applies them.
/// </summary>
public sealed class ConnectedApps(DataFolder folder, SettingsStore secrets, IEnumerable<IAppConnector> connectors, IDirectoryTools tools, TimeProvider clock, ActivityLog log)
{
    public const int MostLog = 200;
    public static readonly string[] Matches = ["email", "username", "hand"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<ConnectedAppsFile> file = folder.Json<ConnectedAppsFile>("connected-apps.json", Json);
    private readonly Dictionary<string, IAppConnector> kinds = connectors.ToDictionary(c => c.Kind);
    private readonly SemaphoreSlim working = new(1, 1); // one sync or apply at a time: they read and change the same apps
    private (DateTime At, List<AppUser> Users)? people;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public IReadOnlyCollection<IAppConnector> Kinds => kinds.Values;

    public IAppConnector? Connector(string kind) => kinds.GetValueOrDefault(kind);

    /// <summary>Something changed: the page reads again.</summary>
    public event Action? Changed;

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Json), Json)!;

    public ConnectedAppsFile Read()
    {
        lock (file.Gate)
        {
            return Copy(file.Read());
        }
    }

    private void Change(Action<ConnectedAppsFile> change)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            change(f);
            file.Save(f);
        }
        Changed?.Invoke();
    }

    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();

    private AppLink Link(ConnectedApp app) => new(app.Settings, secrets.TryDecrypt(app.Secret));

    private void Said(ConnectedApp app, string by, string text, string result = "done")
    {
        app.Log.Insert(0, new AppLogLine { At = Now, By = by, Text = text, Result = result });
        if (app.Log.Count > MostLog)
        {
            app.Log.RemoveRange(MostLog, app.Log.Count - MostLog);
        }
    }

    // --- Active Directory's side -----------------------------------------------------------------------------------

    /// <summary>The people the rules are about, read at most once a minute. None while Active Directory can't be read.</summary>
    public async Task<List<AppUser>> People(CancellationToken stop, bool fresh = false)
    {
        if (!fresh && people is { } kept && Now - kept.At < TimeSpan.FromMinutes(1))
        {
            return kept.Users;
        }
        try
        {
            Caller.Set(null); // the whole of what the service account manages, whoever asks: a rule is about a group, not a role
            var (users, _) = await tools.Users(stop);
            var list = users.Where(u => !u.System && !u.Service).Select(u => new AppUser(u.Username, u.Name ?? u.Username, u.Email ?? "", u.Enabled,
                u.Groups.Select(g => g.Dn).ToHashSet(StringComparer.OrdinalIgnoreCase), u.First ?? "", u.Last ?? "")).ToList();
            people = (Now, list);
            return list;
        }
        catch (Exception e) when (e is ToolProblem or DirectoryProblem or LdapRefused)
        {
            return people?.Users ?? [];
        }
    }

    // --- Matching ----------------------------------------------------------------------------------------------------

    /// <summary>Each account's AD user: the one picked by hand, else by email or username. A user two accounts claim gets neither until one is picked.</summary>
    public static AppMatches Match(ConnectedApp app, IReadOnlyList<AppUser> users)
    {
        var byKey = users.GroupBy(u => (app.Match == "username" ? u.Username : u.Email).ToLowerInvariant()).Where(g => g.Key.Length > 0).ToDictionary(g => g.Key, g => g.First().Username);
        var names = users.Select(u => u.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var claims = new List<(string Account, string User)>();
        foreach (var a in app.Accounts)
        {
            var key = (app.Match == "username" ? a.Login : a.Email).ToLowerInvariant();
            var user = a.Pick == "-" ? null : a.Pick is { Length: > 0 } picked ? (names.Contains(picked) ? picked : null)
                : app.Match == "hand" || key.Length == 0 ? null : byKey.GetValueOrDefault(key);
            if (user is not null)
            {
                claims.Add((a.Id, user));
            }
        }
        var torn = claims.GroupBy(c => c.User, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new AppMatches(claims.Where(c => !torn.Contains(c.User)).ToDictionary(c => c.Account, c => c.User), torn);
    }

    // --- The plan ----------------------------------------------------------------------------------------------------

    /// <summary>Every difference between the rules and the apps, as they were last read.</summary>
    public List<PlanItem> Plan(ConnectedAppsFile f, IReadOnlyList<AppUser> users)
    {
        var items = new List<PlanItem>();
        var making = new HashSet<string>();
        var matches = f.Apps.ToDictionary(a => a.Id, a => Match(a, users));
        var byName = users.ToDictionary(u => u.Username, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in f.Rules)
        {
            var members = users.Where(u => u.Enabled && u.Groups.Contains(rule.Ad)).ToList();
            foreach (var t in rule.To)
            {
                if (f.Apps.FirstOrDefault(a => a.Id == t.App) is not { } app || app.Groups.FirstOrDefault(g => g.Id == t.Group) is not { } group || !kinds.TryGetValue(app.Kind, out var kind))
                {
                    continue;
                }
                var match = matches[app.Id];
                PlanItem Item(string what, AppUser u, AppAccount? a, string text, string why) =>
                    new($"{what}|{app.Id}|{group.Id}|{u.Username}".ToLowerInvariant(), what, rule.Id, rule.AdName, app.Id, app.Name, group.Id, group.Name, u.Username, u.Name, a?.Id, text, why, rule.Mode == "auto");
                foreach (var u in members)
                {
                    var from = $"From the rule for {rule.AdName}{(rule.Mode == "auto" ? "" : ", which waits for approval")}.";
                    if (match.Torn.Contains(u.Username))
                    {
                        items.Add(Item("blocked", u, null, $"{u.Name} matches two {kind.Account}s", "Pick one under Accounts."));
                    }
                    else if (match.AccountOf(app, u.Username) is { } account)
                    {
                        if (!group.Members.Contains(account.Id))
                        {
                            items.Add(Item("add", u, account, $"Give {u.Name} {group.Name}", from));
                        }
                    }
                    else if (!app.Create || !kind.CanCreate)
                    {
                        items.Add(Item("blocked", u, null, $"{u.Name} has no {kind.Account}", $"{app.Name} is set not to make them. Make one in the app, or allow it under Provisioning."));
                    }
                    else
                    {
                        // One account however many groups it then joins.
                        if (making.Add($"{app.Id}|{u.Username}".ToLowerInvariant()))
                        {
                            items.Add(Item("create", u, null, $"Make {An(kind.Account)} {kind.Account} for {u.Name}", from) with { Key = $"create|{app.Id}||{u.Username}".ToLowerInvariant(), Group = "", GroupName = "" });
                        }
                        items.Add(Item("add", u, null, $"Give {u.Name} {group.Name}", from));
                    }
                }
            }
        }
        // Given by hand: someone in a group a rule gives, who is in none of the AD groups whose rules give it.
        foreach (var target in f.Rules.SelectMany(r => r.To.Select(t => (Rule: r, To: t))).GroupBy(x => (x.To.App, x.To.Group)))
        {
            if (f.Apps.FirstOrDefault(a => a.Id == target.Key.App) is not { } app || app.Groups.FirstOrDefault(g => g.Id == target.Key.Group) is not { } group)
            {
                continue;
            }
            var rules = target.Select(x => x.Rule).ToList();
            foreach (var id in group.Members)
            {
                if (!matches[app.Id].UserOf.TryGetValue(id, out var username) || !byName.TryGetValue(username, out var u))
                {
                    continue; // nobody Rabbid Lab knows: not its to judge
                }
                if ((u.Enabled && rules.Any(r => u.Groups.Contains(r.Ad))) || rules.Any(r => r.Keep.Contains($"{app.Id}|{group.Id}|{id}")))
                {
                    continue;
                }
                var rule = rules[0];
                items.Add(new($"drift|{app.Id}|{group.Id}|{u.Username}".ToLowerInvariant(), "drift", rule.Id, rule.AdName, app.Id, app.Name, group.Id, group.Name, u.Username, u.Name, id,
                    $"Take {group.Name} from {u.Name}", $"Not in {string.Join(" or ", rules.Select(r => r.AdName))}{(u.Enabled ? "" : ", and disabled in Active Directory")}. Given by hand.", false));
            }
        }
        return items;
    }

    private static string An(string word) => word.Length > 0 && "aeio".Contains(char.ToLowerInvariant(word[0])) ? "an" : "a";

    public async Task<List<PlanItem>> Plan(CancellationToken stop) => Plan(Read(), await People(stop));

    /// <summary>What a rule would do before it's saved: the plan with it in place of the one it edits.</summary>
    public async Task<List<PlanItem>> Preview(AccessRuleForm form, CancellationToken stop)
    {
        var f = Read();
        var rule = Shaped(f, form, "preview");
        f.Rules.RemoveAll(r => r.Id == form.Id || string.Equals(r.Ad, rule.Ad, StringComparison.OrdinalIgnoreCase));
        f.Rules.Add(rule);
        return [.. Plan(f, await People(stop)).Where(i => i.Rule == rule.Id)];
    }

    // --- Apps --------------------------------------------------------------------------------------------------------

    /// <summary>Connects an app, or changes how one is reached. It is read once first, so settings that don't work aren't saved.</summary>
    public async Task<ConnectedApp> Connect(ConnectedAppForm form, string by, CancellationToken stop)
    {
        var old = form.Id is { Length: > 0 } id ? Read().Apps.FirstOrDefault(a => a.Id == id) ?? throw new AppProblem("This app is no longer connected.") : null;
        var kind = kinds.GetValueOrDefault(old?.Kind ?? form.Kind ?? "") ?? throw new AppProblem("Select the kind of app to connect.");
        var name = Words.Blank(form.Name) is { Length: <= 60 } n ? n : throw new AppProblem("Enter a name of up to 60 characters.");
        var settings = kind.Fields.Where(x => !x.Secret).ToDictionary(x => x.Key, x => (form.Settings?.GetValueOrDefault(x.Key) ?? "").Trim());
        // A secret left empty while editing keeps the one saved.
        var secret = form.Secret is { Length: > 0 } s ? secrets.Encrypt(s) : old?.Secret;
        var link = new AppLink(settings, secrets.TryDecrypt(secret));
        if (kind.Bad(link) is { } bad)
        {
            throw new AppProblem(bad);
        }
        var read = await kind.Read(link, stop);
        var app = old ?? new ConnectedApp { Id = NewId(), Kind = kind.Kind, By = by, Made = Now };
        Change(f =>
        {
            var kept = f.Apps.FirstOrDefault(a => a.Id == app.Id);
            if (kept is null)
            {
                f.Apps.Add(kept = app);
            }
            (kept.Name, kept.Settings, kept.Secret) = (name, settings, secret);
            kept.Match = RabbidLab.Server.ConnectedApps.Matches.Contains(form.Match) ? form.Match! : kept.Match;
            kept.Create = (form.Create ?? kept.Create) && kind.CanCreate;
            Taken(kept, read);
            Said(kept, by, old is null ? $"Connected {name}: read {read.Accounts.Count} {kind.Account}s and {read.Groups.Count} {kind.Group}s" : "Changed the connection");
            app = Copy(kept);
        });
        return app;
    }

    // What was read replaces what was kept; a match made by hand stays with its account.
    private void Taken(ConnectedApp app, AppSnapshot read)
    {
        var picks = app.Accounts.Where(a => a.Pick is not null).ToDictionary(a => a.Id, a => a.Pick);
        read.Accounts.ForEach(a => a.Pick = picks.GetValueOrDefault(a.Id));
        (app.Accounts, app.Groups, app.LastSync, app.Problem) = (read.Accounts, read.Groups, Now, null);
    }

    public void Remove(string id) => Change(f =>
    {
        f.Apps.RemoveAll(a => a.Id == id);
        f.Rules.ForEach(r => r.To.RemoveAll(t => t.App == id));
        f.Rules.RemoveAll(r => r.To.Count == 0);
    });

    public void Set(string id, Action<ConnectedApp> change) => Change(f =>
    {
        if (f.Apps.FirstOrDefault(a => a.Id == id) is { } app)
        {
            change(app);
        }
    });

    /// <summary>Matches an account to a user by hand ("-" for not a person), or back to automatic with null.</summary>
    public void Pick(string id, string account, string? user, string by) => Set(id, app =>
    {
        if (app.Accounts.FirstOrDefault(a => a.Id == account) is { } a)
        {
            a.Pick = Words.Blank(user);
            Said(app, by, a.Pick is null ? $"{a.Id} is matched automatically again" : a.Pick == "-" ? $"Marked {a.Id} as not a person" : $"Matched {a.Id} to {a.Pick} by hand");
        }
    });

    /// <summary>Reads one app again. Whether it worked; why not is kept on the app.</summary>
    public async Task<bool> Sync(string id, string by, CancellationToken stop)
    {
        await working.WaitAsync(stop);
        try
        {
            return await SyncNow(id, by, stop);
        }
        finally
        {
            working.Release();
        }
    }

    private async Task<bool> SyncNow(string id, string by, CancellationToken stop)
    {
        if (Read().Apps.FirstOrDefault(a => a.Id == id) is not { } app || !kinds.TryGetValue(app.Kind, out var kind))
        {
            return false;
        }
        AppSnapshot? read = null;
        string? problem = null;
        try
        {
            read = await kind.Read(Link(app), stop);
        }
        catch (Exception e) when (e is AppProblem or HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            problem = e is TaskCanceledException ? $"{app.Name} did not answer in time." : e.Message;
        }
        var first = false;
        Set(id, kept =>
        {
            if (read is not null)
            {
                Taken(kept, read);
            }
            else
            {
                first = kept.Problem is null;
                kept.Problem = problem;
                Said(kept, by, $"Sync failed: {problem}", "failed");
            }
        });
        if (first)
        {
            // Once when it starts failing, which the alert for an app that stops syncing goes off on.
            log.Record(by, null, "apps", "apps.sync.failed", $"{app.Name} stopped syncing", "failed", problem);
        }
        return read is not null;
    }

    // --- Rules -------------------------------------------------------------------------------------------------------

    private AccessRule Shaped(ConnectedAppsFile f, AccessRuleForm form, string by)
    {
        var ad = Words.Blank(form.Ad) ?? throw new AppProblem("Select the Active Directory group.");
        var to = (form.To ?? []).Where(t => f.Apps.Any(a => a.Id == t.App && a.Groups.Any(g => g.Id == t.Group)))
            .GroupBy(t => (t.App, t.Group)).Select(g => new AccessTarget { App = g.Key.App, Group = g.Key.Group }).ToList();
        if (to.Count == 0)
        {
            throw new AppProblem("Select at least one group or role to give.");
        }
        return new AccessRule { Id = form.Id is { Length: > 0 } id ? id : NewId(), Ad = ad, AdName = Words.Blank(form.AdName) ?? Ldap.Name(ad), To = to, Mode = form.Mode == "hand" ? "hand" : "auto", By = by, Made = Now };
    }

    /// <summary>Saves a rule: one for each Active Directory group, so saving one for a group that has a rule changes that rule.</summary>
    public AccessRule SaveRule(AccessRuleForm form, string by)
    {
        AccessRule saved = null!;
        Change(f =>
        {
            var rule = Shaped(f, form, by);
            var old = f.Rules.FirstOrDefault(r => r.Id == form.Id) ?? f.Rules.FirstOrDefault(r => string.Equals(r.Ad, rule.Ad, StringComparison.OrdinalIgnoreCase));
            if (old is not null)
            {
                (rule.Id, rule.Keep) = (old.Id, old.Keep);
                f.Rules[f.Rules.IndexOf(old)] = rule;
            }
            else
            {
                f.Rules.Add(rule);
            }
            foreach (var app in f.Apps.Where(a => rule.To.Any(t => t.App == a.Id)))
            {
                var names = rule.To.Where(t => t.App == app.Id).Select(t => app.Groups.First(g => g.Id == t.Group).Name);
                Said(app, by, $"Access rule: members of {rule.AdName} get {string.Join(", ", names)}");
            }
            saved = Copy(rule);
        });
        return saved;
    }

    public AccessRule? DeleteRule(string id)
    {
        AccessRule? gone = null;
        Change(f =>
        {
            gone = f.Rules.FirstOrDefault(r => r.Id == id);
            f.Rules.RemoveAll(r => r.Id == id);
        });
        return gone;
    }

    /// <summary>Keeps something given by hand: it stops showing as a difference.</summary>
    public void Keep(string rule, string app, string group, string account) => Change(f =>
    {
        if (f.Rules.FirstOrDefault(r => r.Id == rule) is { } r && !r.Keep.Contains($"{app}|{group}|{account}"))
        {
            r.Keep.Add($"{app}|{group}|{account}");
        }
    });

    // --- Applying ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Makes the changes picked from the plan, as it stands now: accounts first, then groups and roles, then what's taken
    /// away. Each is a line in its app's activity. An account left in nothing is disabled. Then each app touched is read again.
    /// </summary>
    public async Task<(int Done, int Failed)> Apply(IReadOnlyCollection<string> keys, string by, CancellationToken stop, bool onlyAuto = false)
    {
        await working.WaitAsync(stop);
        try
        {
            var f = Read();
            var users = await People(stop);
            var order = new Dictionary<string, int> { ["create"] = 0, ["add"] = 1, ["drift"] = 2 };
            var items = Plan(f, users).Where(i => order.ContainsKey(i.Kind) && (onlyAuto ? i.Auto : keys.Contains(i.Key))).OrderBy(i => order[i.Kind]).ToList();
            var (done, failed) = (0, 0);
            var said = new List<(string App, string Text, string Result)>();
            foreach (var i in items)
            {
                var app = f.Apps.First(a => a.Id == i.App);
                var kind = kinds[app.Kind];
                var link = Link(app);
                var u = users.First(x => string.Equals(x.Username, i.User, StringComparison.OrdinalIgnoreCase));
                try
                {
                    if (i.Kind == "create")
                    {
                        var id = await kind.Create(link, new AppPerson(u.Username, u.Name, u.Email, u.First, u.Last), stop);
                        // Theirs from now on, whatever it was named: the groups that follow find it by this match.
                        app.Accounts.Add(new AppAccount { Id = id, Name = u.Name, Email = u.Email, Login = u.Username, Pick = u.Username });
                        said.Add((app.Id, $"Made {kind.Account} {id} for {u.Name}, from the rule for {i.RuleName}", "done"));
                    }
                    else
                    {
                        var group = app.Groups.First(g => g.Id == i.Group);
                        var account = app.Accounts.FirstOrDefault(a => a.Id == i.Account) ?? Match(app, users).AccountOf(app, u.Username)
                            ?? throw new AppProblem($"{u.Name} has no {kind.Account} in {app.Name}.");
                        if (i.Kind == "add")
                        {
                            await kind.AddMember(link, group, account, stop);
                            group.Members.Add(account.Id);
                            said.Add((app.Id, $"Added {account.Id} to {group.Name}, from the rule for {i.RuleName}", "done"));
                        }
                        else
                        {
                            await kind.RemoveMember(link, group, account, stop);
                            group.Members.Remove(account.Id);
                            said.Add((app.Id, $"Removed {account.Id} from {group.Name}: not in {i.RuleName}", "done"));
                            if (!app.Groups.Any(g => g.Members.Contains(account.Id)))
                            {
                                await kind.Disable(link, account, stop);
                                said.Add((app.Id, $"Disabled {account.Id}: it has no {kind.Group}s left", "done"));
                            }
                        }
                    }
                    done++;
                }
                catch (Exception e) when (e is AppProblem or HttpRequestException or TaskCanceledException or JsonException or IOException)
                {
                    failed++;
                    said.Add((app.Id, $"{i.Text} failed: {(e is TaskCanceledException ? $"{app.Name} did not answer in time." : e.Message)}", "failed"));
                }
            }
            if (said.Count > 0)
            {
                Change(now =>
                {
                    foreach (var line in said)
                    {
                        if (now.Apps.FirstOrDefault(a => a.Id == line.App) is { } app)
                        {
                            Said(app, by, line.Text, line.Result);
                        }
                    }
                    // A new account keeps its match until the next read brings it back under the same id.
                    foreach (var app in f.Apps)
                    {
                        if (now.Apps.FirstOrDefault(a => a.Id == app.Id) is { } kept)
                        {
                            kept.Accounts.AddRange(app.Accounts.Where(a => kept.Accounts.All(k => k.Id != a.Id)));
                        }
                    }
                });
                foreach (var id in items.Select(i => i.App).Distinct())
                {
                    await SyncNow(id, by, stop);
                }
            }
            return (done, failed);
        }
        finally
        {
            working.Release();
        }
    }
}

/// <summary>
/// Reads every connected app every 15 minutes and gives what the rules that give automatically are owed. A role given by
/// hand is only reported: the first time one shows, the activity log says so, which its alert goes off on.
/// </summary>
public sealed class ConnectedAppsWorker(ConnectedApps apps, ActivityLog log, TimeProvider clock, ILogger<ConnectedAppsWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(15);
    private HashSet<string> drift = [];
    private bool looked;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(looked ? Every : TimeSpan.FromSeconds(40), clock, stop);
            try
            {
                await Tick(stop);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Connected apps could not be synced");
            }
        }
    }

    public async Task Tick(CancellationToken stop)
    {
        var f = apps.Read();
        if (f.Apps.Count == 0)
        {
            looked = true;
            return;
        }
        await apps.People(stop, fresh: true);
        foreach (var app in f.Apps.Where(a => !a.Paused))
        {
            await apps.Sync(app.Id, Product.Title, stop);
        }
        if (f.Rules.Any(r => r.Mode == "auto"))
        {
            await apps.Apply([], Product.Title, stop, onlyAuto: true);
        }
        // What is given by hand now that wasn't the last time: said once. The first look after a start only notes what's there.
        var plan = await apps.Plan(stop);
        var now = plan.Where(i => i.Kind == "drift").ToList();
        foreach (var i in now.Where(i => looked && !drift.Contains(i.Key)))
        {
            log.Record(Product.Title, null, "apps", "apps.hand", $"{i.UserName} has {i.GroupName} in {i.AppName} without a rule", "warning", i.Why);
        }
        drift = [.. now.Select(i => i.Key)];
        looked = true;
    }
}
