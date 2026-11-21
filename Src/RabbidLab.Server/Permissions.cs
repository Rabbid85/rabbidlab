namespace RabbidLab.Server;

/// <summary>Everything a role can allow, part by part. Each part of Rabbid Lab lists the things it lets people do.</summary>
public static class Permissions
{
    public const string OverviewView = "overview.view";
    public const string SeatsView = "seats.view";
    public const string SeatsWmi = "seats.wmi";
    public const string SeatsApprove = "seats.approve";
    public const string SeatsRemove = "seats.remove";
    public const string SeatsAdd = "seats.add";
    public const string SeatsSettings = "seats.settings";
    public const string SeatsActions = "seats.actions";
    public const string SeatsInstalls = "seats.installs";
    public const string ScriptsRun = "seats.scripts.run";
    public const string ScriptsEdit = "seats.scripts.edit";
    public const string Workflows = "seats.workflows";
    public const string SessionsView = "sessions.view";
    public const string SessionsSignOut = "sessions.signout";
    public const string UpdatesView = "updates.view";
    public const string UpdatesCheck = "updates.check";
    public const string UpdatesInstall = "updates.install";
    public const string UpdatesAuto = "updates.auto";
    public const string AppearanceBrand = "appearance.brand";
    public const string ActivityView = "activity.view";
    public const string AccessView = "access.view";
    public const string AccessMembers = "access.members";
    public const string AccessRoles = "access.roles";
    public const string AccessLocal = "access.local";
    public const string AccessMfa = "access.mfa";
    public const string AccessUnlock = "access.unlock";
    public const string AdView = "ad.view";
    public const string AdCreate = "ad.create";
    public const string AdChange = "ad.change";
    public const string AdPassword = "ad.password";
    public const string AdEnable = "ad.enable";
    public const string AdGroups = "ad.groups";
    public const string AdDelete = "ad.delete";
    public const string AdConnect = "ad.connect";
    public const string AdAttributes = "ad.attributes";
    public const string AdReadOnly = "ad.readonly";
    public const string GpoView = "gpo.view";
    public const string GpoLink = "gpo.link";
    public const string GpoChange = "gpo.change";
    public const string GpoDelete = "gpo.delete";
    public const string WindowsView = "windows.view";
    public const string WindowsCreate = "windows.create";
    public const string WindowsChange = "windows.change";
    public const string WindowsDelete = "windows.delete";
    public const string LinksView = "links.view";
    public const string LinksEdit = "links.edit";
    public const string SensorsView = "sensors.view";
    public const string SensorsEdit = "sensors.edit";
    public const string SensorsAlerts = "sensors.alerts";
    public const string ConsoleAddresses = "console.address";

    public sealed record Action(string Key, string Name, string About);

    public sealed record Part(string Key, string Name, string Icon, IReadOnlyList<Action> Actions);

    /// <summary>The catalog as the page shows it, with the lab's own name in place of Rabbid Lab.</summary>
    public static IReadOnlyList<Part> Shown() => Product.Title == Product.Software ? Parts
        : [.. Parts.Select(p => p with { Actions = [.. p.Actions.Select(a => a with { Name = a.Name.Replace(Product.Software, Product.Title), About = a.About.Replace(Product.Software, Product.Title) })] })];

    public static readonly IReadOnlyList<Part> Parts =
    [
        new("overview", "Overview", "overview", [new(OverviewView, "See the overview", "The front page: how Rabbid Lab is doing and what's new.")]),
        new("links", "Links", "link",
        [
            new(LinksView, "See links", "The Links page, and whether each site is up."),
            new(LinksEdit, "Add and change links", "Put sites on Links, change them and take them off."),
        ]),
        new("sensors", "Sensors", "activity",
        [
            new(SensorsView, "See sensors", "The Sensors page: what's checked, what's down and its history."),
            new(SensorsEdit, "Add and change sensors", "Add sensors, change, pause and delete them, and their groups."),
            new(SensorsAlerts, "Change alerts and email", "The alert rules, the mail server and who gets the emails."),
        ]),
        new("seats", "Devices", "monitor",
        [
            new(SeatsView, "See computers", "Every computer with the client, whether it's online, and who's on it."),
            new(SeatsWmi, "Query computers with WMI", "Read anything Windows keeps in WMI on a computer: hardware, settings, services, security status. Read only, and each query is in the activity log."),
            new(SeatsApprove, "Approve new computers", "Approve a computer that's waiting to connect."),
            new(SeatsRemove, "Remove computers", "Take a computer off the list. It has to join again."),
            new(SeatsAdd, "Add computers", "Get the client installer, the Connect link and the Group Policy template."),
            new(SeatsSettings, "Change client settings", "Whether new computers need approval."),
            new(SeatsActions, "Restart, wake and update computers", "Restart a computer, wake one with Wake-on-LAN, and install its missing Windows updates. Restarts and updates are under Jobs."),
            new(SeatsInstalls, "Install apps", "Install apps from the Installs page on computers, for everyone on them, as SYSTEM, and add apps to the page by their winget ID. Installs are under Jobs."),
            new(ScriptsRun, "Run scripts", "Run a script from the library on computers, as SYSTEM, and see its output under Jobs."),
            new(ScriptsEdit, "Write scripts", "Add, change and delete scripts, and run one typed in. Whoever has this can run anything on every computer."),
            new(Workflows, "Manage workflows", "Steps that run on computers by themselves, when something happens or on a schedule."),
        ]),
        new("sessions", "Sessions", "signout",
        [
            new(SessionsView, "See everyone signed in", "Everyone can always see their own."),
            new(SessionsSignOut, "Sign other people out", "End someone else's session straight away."),
        ]),
        new("updates", "Updates", "updates",
        [
            new(UpdatesView, "See updates", "Which version is installed and whether a new one is out."),
            new(UpdatesCheck, "Check for updates", "Ask GitHub now instead of waiting."),
            new(UpdatesInstall, "Install updates", "Rabbid Lab is stopped for about a minute while one installs."),
            new(UpdatesAuto, "Turn Update automatically on or off", "Whether new versions install on their own."),
        ]),
        new("console", "Console address", "browser", [new(ConsoleAddresses, "Add and remove console addresses", "Names to open Rabbid Lab by, their aliases in DNS, and their certificate.")]),
        new("activity", "Activity log", "activity", [new(ActivityView, "See the activity log", "Sign-ins, changes and updates, and who did them.")]),
        new("appearance", "Appearance", "brush", [new(AppearanceBrand, "Change the lab's name, icon and accent colour", "What everyone sees: the name in the top bar, its icon and the accent colour.")]),
        new("access", "Roles and people", "shield",
        [
            new(AccessView, "See roles and who's in them", "The roles, what they allow, and everyone in them."),
            new(AccessMembers, "Add and remove people", "Put groups, people and local accounts in roles below their own."),
            new(AccessRoles, "Make and change roles", "Only roles below their own highest role."),
            new(AccessLocal, "Manage local accounts", "Add Rabbid Lab's own accounts, set their passwords and remove them."),
            new(AccessMfa, "Reset MFA", "Clear someone's authenticator app, for a lost phone. Only for people in roles below their own."),
            new(AccessUnlock, "Unlock sign-ins", "Unlock a name that locked after too many wrong passwords, in Settings, Sign-in."),
        ]),
        new("ad", "Active Directory", "users",
        [
            new(AdView, "See Active Directory", "Users, groups and computers in the role's scope, and who's in each group."),
            new(AdCreate, "Make users, groups and computers", "New people, with a password and groups, new groups and new computer accounts."),
            new(AdPassword, "Reset passwords and unlock accounts", "Set a new password, make someone change theirs at the next sign-in, and unlock."),
            new(AdEnable, "Turn accounts off and on", "Disable and enable users and computers."),
            new(AdGroups, "Change group membership", "Add and remove the members of groups."),
            new(AdChange, "Edit properties and account options", "Details, account options and expiry, rename, move, and restore from the Recycle Bin."),
            new(AdDelete, "Delete users, groups and computers", "Gone from the domain for good."),
            new(AdConnect, "Set up Rabbid Lab's AD account", "Make the service account Rabbid Lab changes Active Directory with, or take it away."),
            new(AdReadOnly, "See it while read-only", "Before the service account is set up, see Active Directory and Group Policy read as this server, when read-only is on. People who can set up the account always can."),
            new(AdAttributes, "Edit attributes", "Change any attribute in the Attribute browser, as ADSI Edit does, inside the service account's scope."),
        ]),
        new("gpo", "Group Policy", "policy",
        [
            new(GpoView, "See Group Policy", "Every GPO, where it's linked, its settings and who it applies to, WMI filters, backups, and what applied on each computer."),
            new(GpoLink, "Link GPOs", "Link and unlink GPOs, turn links off, enforce them, change their order, and block inheritance on an OU."),
            new(GpoChange, "Make and change GPOs", "New GPOs and copies, rename, turn settings off, WMI filters, who a GPO applies to, back up and restore."),
            new(GpoDelete, "Delete GPOs", "Gone from the domain, with their links. Each is backed up first."),
        ]),
        new("windows", "Windows accounts", "key",
        [
            new(WindowsView, "See Windows accounts", "The accounts on this server and on the computers in Devices."),
            new(WindowsCreate, "Make Windows accounts", "New accounts on this server or a computer in Devices, administrators included."),
            new(WindowsChange, "Change Windows accounts", "Set passwords, turn accounts off and on, and change their groups, such as Administrators."),
            new(WindowsDelete, "Delete Windows accounts", "Gone from that computer for good."),
        ]),
    ];

    /// <summary>The ones a role's scope narrows to its OUs. Setting up the service account isn't one: that's the whole domain's.</summary>
    public static readonly IReadOnlySet<string> Scoped = new HashSet<string> { AdView, AdCreate, AdPassword, AdEnable, AdGroups, AdChange, AdDelete, AdAttributes };

    /// <summary>What Change users, groups and computers was split into. Roles that had it keep all of it.</summary>
    public static readonly IReadOnlyList<string> WasChange = [AdPassword, AdEnable, AdGroups];

    /// <summary>Group Policy, for roles that could set up the AD account (the whole domain's) before it was here.</summary>
    public static readonly IReadOnlyList<string> Gpo = [GpoView, GpoLink, GpoChange, GpoDelete];

    public static readonly IReadOnlySet<string> All = Parts.SelectMany(p => p.Actions).Select(a => a.Key).ToHashSet();

    /// <summary>What the User role allows to begin with: what users could do before roles had permissions.</summary>
    public static readonly IReadOnlyList<string> Basic = [OverviewView, LinksView, UpdatesView, UpdatesCheck];
}
