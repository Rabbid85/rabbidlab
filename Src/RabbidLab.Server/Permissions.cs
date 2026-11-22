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
    public const string SeatsPrinters = "seats.printers";
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
        new("overview", "Overview", "overview", [new(OverviewView, "View the overview", "The Overview page: Rabbid Lab status and what's new.")]),
        new("links", "Links", "link",
        [
            new(LinksView, "View links", "The Links page and the status of each site."),
            new(LinksEdit, "Manage links", "Add, edit and remove links."),
        ]),
        new("sensors", "Sensors", "activity",
        [
            new(SensorsView, "View sensors", "The Sensors page: what is monitored, what is down, and history."),
            new(SensorsEdit, "Manage sensors", "Add, edit, pause and delete sensors and sensor groups."),
            new(SensorsAlerts, "Manage alerts and email", "Alert rules, the mail server and email recipients."),
        ]),
        new("seats", "Devices", "monitor",
        [
            new(SeatsView, "View computers", "Every computer with the client, its online status and the signed-in user."),
            new(SeatsWmi, "Query computers with WMI", "Read any WMI data on a computer: hardware, settings, services and security status. Read-only. Each query is recorded in the activity log."),
            new(SeatsApprove, "Approve new computers", "Approve computers that are pending approval."),
            new(SeatsRemove, "Remove computers", "Remove a computer from Devices. Its client must be approved again."),
            new(SeatsAdd, "Add computers", "Access the client installer, the Connect link and the Group Policy template."),
            new(SeatsSettings, "Change client settings", "Whether new computers require approval."),
            new(SeatsActions, "Restart, wake and update computers", "Restart computers, wake them with Wake-on-LAN, and install missing Windows updates. Restarts and updates appear under Jobs."),
            new(SeatsPrinters, "Manage print queues", "Pause, resume, restart and cancel print jobs on the print servers, and pause printing or cancel every job on a printer."),
            new(SeatsInstalls, "Install apps", "Install apps from the Installs page on computers for all users, as SYSTEM, and add apps by winget ID. Installs appear under Jobs."),
            new(ScriptsRun, "Run scripts", "Run library scripts on computers as SYSTEM and view the output under Jobs."),
            new(ScriptsEdit, "Manage scripts", "Add, edit and delete scripts, and run custom scripts. This permission can run any code on every computer."),
            new(Workflows, "Manage workflows", "Automated steps that run on computers when an event occurs or on a schedule."),
        ]),
        new("sessions", "Sessions", "signout",
        [
            new(SessionsView, "View all sessions", "Everyone can always view their own sessions."),
            new(SessionsSignOut, "Sign out other users", "End another user's session immediately."),
        ]),
        new("updates", "Updates", "updates",
        [
            new(UpdatesView, "View updates", "The installed version and whether an update is available."),
            new(UpdatesCheck, "Check for updates", "Check GitHub for updates now."),
            new(UpdatesInstall, "Install updates", "Rabbid Lab restarts while an update installs. This takes about a minute."),
            new(UpdatesAuto, "Enable or disable automatic updates", "Whether new versions install automatically."),
        ]),
        new("console", "Console address", "browser", [new(ConsoleAddresses, "Manage console addresses", "Host names for the console, their DNS aliases and their certificates.")]),
        new("activity", "Activity log", "activity", [new(ActivityView, "View the activity log", "Sign-ins, changes and updates, and who made them.")]),
        new("appearance", "Appearance", "brush", [new(AppearanceBrand, "Change the lab name, icon and accent colour", "Shown to everyone: the name in the top bar, its icon and the accent colour.")]),
        new("access", "Roles and people", "shield",
        [
            new(AccessView, "View roles and members", "Roles, their permissions and their members."),
            new(AccessMembers, "Manage role members", "Add groups, users and local accounts to roles below their own."),
            new(AccessRoles, "Manage roles", "Only roles below their own highest role."),
            new(AccessLocal, "Manage local accounts", "Create Rabbid Lab local accounts, set their passwords and remove them."),
            new(AccessMfa, "Reset MFA", "Clear another user's authenticator app, for example after a lost phone. Only for users in roles below their own."),
            new(AccessUnlock, "Unlock sign-ins", "Unlock usernames that are locked out after too many incorrect passwords, in Settings > Sign-in."),
        ]),
        new("ad", "Active Directory", "users",
        [
            new(AdView, "View Active Directory", "Users, groups and computers in the role's scope, and group members."),
            new(AdCreate, "Create users, groups and computers", "New users with a password and groups, new groups and new computer accounts."),
            new(AdPassword, "Reset passwords and unlock accounts", "Set a new password, require a password change at next sign-in, and unlock accounts."),
            new(AdEnable, "Enable and disable accounts", "Enable and disable users and computers."),
            new(AdGroups, "Change group membership", "Add and remove group members."),
            new(AdChange, "Edit properties and account options", "Details, account options and expiration, rename, move, and restore from the Recycle Bin."),
            new(AdDelete, "Delete users, groups and computers", "Permanently delete them from the domain."),
            new(AdConnect, "Set up the service account", "Create or remove the service account Rabbid Lab uses to make Active Directory changes."),
            new(AdReadOnly, "View in read-only mode", "Before the service account is set up, view Active Directory and Group Policy as read by this server, when read-only mode is enabled. Users who can set up the service account always can."),
            new(AdAttributes, "Edit attributes", "Change any attribute in the Attribute browser, as ADSI Edit does, within the service account's scope."),
        ]),
        new("gpo", "Group Policy", "policy",
        [
            new(GpoView, "View Group Policy", "All GPOs, their links, settings and security filtering, WMI filters, backups, and the results on each computer."),
            new(GpoLink, "Link GPOs", "Link and unlink GPOs, disable and enforce links, change link order, and block inheritance on an OU."),
            new(GpoChange, "Create and edit GPOs", "Create and copy GPOs, rename them, disable their settings, manage WMI filters and security filtering, back up and restore."),
            new(GpoDelete, "Delete GPOs", "Delete GPOs and their links from the domain. Each GPO is backed up first."),
        ]),
        new("windows", "Windows accounts", "key",
        [
            new(WindowsView, "View Windows accounts", "Local accounts on this server and on computers in Devices."),
            new(WindowsCreate, "Create Windows accounts", "Create accounts on this server or a computer in Devices, including administrators."),
            new(WindowsChange, "Edit Windows accounts", "Set passwords, enable and disable accounts, and change group membership, such as Administrators."),
            new(WindowsDelete, "Delete Windows accounts", "Permanently delete accounts from that computer."),
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
