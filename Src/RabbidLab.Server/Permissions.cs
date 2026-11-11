namespace RabbidLab.Server;

/// <summary>Everything a role can allow, part by part. Each part of Rabbid Lab lists the things it lets people do.</summary>
public static class Permissions
{
    public const string OverviewView = "overview.view";
    public const string SeatsView = "seats.view";
    public const string SeatsApprove = "seats.approve";
    public const string SeatsRemove = "seats.remove";
    public const string SeatsAdd = "seats.add";
    public const string SeatsSettings = "seats.settings";
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
    public const string AdView = "ad.view";
    public const string AdCreate = "ad.create";
    public const string AdChange = "ad.change";
    public const string AdDelete = "ad.delete";
    public const string AdConnect = "ad.connect";
    public const string WindowsView = "windows.view";
    public const string WindowsCreate = "windows.create";
    public const string WindowsChange = "windows.change";
    public const string WindowsDelete = "windows.delete";
    public const string LinksView = "links.view";
    public const string LinksEdit = "links.edit";
    public const string SensorsView = "sensors.view";
    public const string SensorsEdit = "sensors.edit";
    public const string SensorsAlerts = "sensors.alerts";

    public sealed record Action(string Key, string Name, string About);

    public sealed record Part(string Key, string Name, string Icon, IReadOnlyList<Action> Actions);

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
        new("seats", "Seats", "monitor",
        [
            new(SeatsView, "See computers", "Every computer with the client, whether it's online, and who's on it."),
            new(SeatsApprove, "Let new computers in", "Approve a computer that asked to join."),
            new(SeatsRemove, "Remove computers", "Take a computer off the list. It has to join again."),
            new(SeatsAdd, "Add computers", "Get the client installer, the Connect link and the Group Policy template."),
            new(SeatsSettings, "Change client settings", "Whether new computers wait for someone to let them in."),
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
            new(UpdatesInstall, "Install updates", "The server is offline for about a minute while one installs."),
            new(UpdatesAuto, "Turn Update automatically on or off", "Whether new versions install on their own."),
        ]),
        new("activity", "Activity log", "activity", [new(ActivityView, "See the activity log", "Sign-ins, changes and updates, and who did them.")]),
        new("appearance", "Appearance", "brush", [new(AppearanceBrand, "Change the lab's name, icon and accent colour", "What everyone sees: the name in the top bar, its icon and the accent colour.")]),
        new("access", "Roles and people", "shield",
        [
            new(AccessView, "See roles and who's in them", "The roles, what they allow, and everyone in them."),
            new(AccessMembers, "Add and remove people", "Put groups, people and local accounts in roles below their own."),
            new(AccessRoles, "Make and change roles", "Only roles below their own highest role."),
            new(AccessLocal, "Manage local accounts", "Add Rabbid Lab's own accounts, set their passwords and remove them."),
        ]),
        new("ad", "Active Directory", "users",
        [
            new(AdView, "See Active Directory", "Users, groups and computers in the domain, and who's in each group."),
            new(AdCreate, "Make users and groups", "New people, with a password and groups, and new groups."),
            new(AdChange, "Change users, groups and computers", "Set passwords, unlock, turn accounts and computers off and on, and change who's in groups."),
            new(AdDelete, "Delete users, groups and computers", "Gone from the domain for good."),
            new(AdConnect, "Set up Rabbid Lab's AD account", "Make the service account Rabbid Lab changes Active Directory with, or take it away."),
        ]),
        new("windows", "Windows accounts", "key",
        [
            new(WindowsView, "See Windows accounts", "The accounts on this server and on the computers in Seats."),
            new(WindowsCreate, "Make Windows accounts", "New accounts on this server or a computer in Seats, administrators included."),
            new(WindowsChange, "Change Windows accounts", "Set passwords, turn accounts off and on, and change their groups, such as Administrators."),
            new(WindowsDelete, "Delete Windows accounts", "Gone from that computer for good."),
        ]),
    ];

    public static readonly IReadOnlySet<string> All = Parts.SelectMany(p => p.Actions).Select(a => a.Key).ToHashSet();

    /// <summary>What the User role allows to begin with: what users could do before roles had permissions.</summary>
    public static readonly IReadOnlyList<string> Basic = [OverviewView, LinksView, UpdatesView, UpdatesCheck];
}
