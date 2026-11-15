using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Runtime.Serialization;

// Compiled into both the server (net10.0) and the client (net472): the same code makes and changes Windows accounts on
// the server itself and on each computer in Devices. The [DataMember] names are the JSON both sides send.
namespace RabbidLab.Shared;

/// <summary>One of a computer's own Windows accounts.</summary>
[DataContract]
public sealed class WindowsAccount
{
    [DataMember(Name = "name")] public string Name { get; set; } = "";
    [DataMember(Name = "fullName")] public string? FullName { get; set; }
    [DataMember(Name = "description")] public string? Description { get; set; }
    [DataMember(Name = "enabled")] public bool Enabled { get; set; }
    [DataMember(Name = "locked")] public bool Locked { get; set; }
    [DataMember(Name = "neverExpires")] public bool NeverExpires { get; set; }

    /// <summary>Administrator, Guest and the other accounts Windows makes itself.</summary>
    [DataMember(Name = "builtIn")] public bool BuiltIn { get; set; }
    [DataMember(Name = "groups")] public List<string>? Groups { get; set; }

    /// <summary>ISO 8601, UTC.</summary>
    [DataMember(Name = "lastSignIn")] public string? LastSignIn { get; set; }
    [DataMember(Name = "passwordSet")] public string? PasswordSet { get; set; }
}

/// <summary>One of a computer's own groups, such as Administrators.</summary>
[DataContract]
public sealed class WindowsGroup
{
    [DataMember(Name = "name")] public string Name { get; set; } = "";
    [DataMember(Name = "sid")] public string? Sid { get; set; }
    [DataMember(Name = "description")] public string? Description { get; set; }
}

/// <summary>One member of a computer's Administrators group, as Windows lists it.</summary>
[DataContract]
public sealed class WindowsMember
{
    /// <summary>The most members a list keeps.</summary>
    public const int Max = 500;

    /// <summary>DOMAIN\name, or the SID when Windows can't name it (a deleted account).</summary>
    [DataMember(Name = "name")] public string Name { get; set; } = "";
    [DataMember(Name = "sid")] public string? Sid { get; set; }

    /// <summary>local, localgroup, user, group, computer, wellknown or unknown.</summary>
    [DataMember(Name = "kind")] public string Kind { get; set; } = "unknown";
}

/// <summary>What the server asks a computer to do with its accounts.</summary>
[DataContract]
public sealed class AccountOrder
{
    public const string List = "list";
    public const string Create = "create";
    public const string Password = "password";
    public const string Enable = "enable";
    public const string Disable = "disable";
    public const string Groups = "groups";
    public const string Delete = "delete";

    /// <summary>Changes who's in Administrators: Add has names (DOMAIN\name or a local account), Remove has SIDs.</summary>
    public const string Admins = "admins";

    /// <summary>Reads for sensors, not changes: a service's state, and a drive's free space. Name is the service or the drive.</summary>
    public const string Service = "service";
    public const string Disk = "disk";

    /// <summary>Not a change either: jobs wait for this computer, and its client comes to take them (the client's Jobs.cs).</summary>
    public const string Jobs = "jobs";

    /// <summary>Reads too: a WMI query (Query, in Space), and the namespaces and classes in Space to pick from (the client's Wmi.cs).</summary>
    public const string Wmi = "wmi";
    public const string WmiClasses = "wmi-classes";

    [DataMember(Name = "id")] public string? Id { get; set; }
    [DataMember(Name = "action")] public string? Action { get; set; }
    [DataMember(Name = "space")] public string? Space { get; set; }
    [DataMember(Name = "query")] public string? Query { get; set; }
    [DataMember(Name = "name")] public string? Name { get; set; }
    [DataMember(Name = "fullName")] public string? FullName { get; set; }
    [DataMember(Name = "description")] public string? Description { get; set; }
    [DataMember(Name = "newPassword")] public string? NewPassword { get; set; }
    [DataMember(Name = "mustChange")] public bool MustChange { get; set; }
    [DataMember(Name = "neverExpires")] public bool NeverExpires { get; set; }
    [DataMember(Name = "add")] public List<string>? Add { get; set; }
    [DataMember(Name = "remove")] public List<string>? Remove { get; set; }

    /// <summary>The characters neither a Windows nor an Active Directory account or group name can have.</summary>
    public static readonly char[] NotInNames = "\"/\\[]:;|=,+*?<>@".ToCharArray();

    /// <summary>Why a name can't be a Windows account name, or null when it can.</summary>
    public static string? BadName(string? name)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0)
        {
            return "Give the account a name.";
        }
        if (n.Length > 20)
        {
            return "Windows account names are 20 characters at most.";
        }
        if (n.IndexOfAny(NotInNames) >= 0 || n.Trim('.', ' ').Length == 0)
        {
            return "Windows account names can't have \" / \\ [ ] : ; | = , + * ? < > or @ in them.";
        }
        return null;
    }
}

/// <summary>How it went: the accounts and groups after a list, or what went wrong.</summary>
[DataContract]
public sealed class AccountAnswer
{
    [DataMember(Name = "id")] public string? Id { get; set; }
    [DataMember(Name = "ok")] public bool Ok { get; set; }
    [DataMember(Name = "error")] public string? Error { get; set; }

    /// <summary>Done, but with something that didn't work, such as a group it couldn't join.</summary>
    [DataMember(Name = "warning")] public string? Warning { get; set; }
    [DataMember(Name = "accounts")] public List<WindowsAccount>? Accounts { get; set; }
    [DataMember(Name = "groups")] public List<WindowsGroup>? Groups { get; set; }

    /// <summary>Everyone in Administrators, domain members too; null from clients before 1.1.183.</summary>
    [DataMember(Name = "admins", EmitDefaultValue = false)] public List<WindowsMember>? Admins { get; set; }

    /// <summary>For a sensor: a service's state and start type and its names, or a drive's size and free space in bytes and its label.</summary>
    [DataMember(Name = "state")] public string? State { get; set; }
    [DataMember(Name = "startType")] public string? StartType { get; set; }
    [DataMember(Name = "detail")] public string? Detail { get; set; }
    [DataMember(Name = "total")] public long? Total { get; set; }
    [DataMember(Name = "free")] public long? Free { get; set; }

    /// <summary>For a WMI query: the columns, each one's kind (text, number, date, yes), the rows as text, and whether it had more than it sent.</summary>
    [DataMember(Name = "columns")] public List<string>? Columns { get; set; }
    [DataMember(Name = "kinds")] public List<string>? Kinds { get; set; }
    [DataMember(Name = "rows")] public List<List<string>>? Rows { get; set; }
    [DataMember(Name = "more")] public bool More { get; set; }

    /// <summary>For the WMI class list: the namespaces under the one asked about, and its classes.</summary>
    [DataMember(Name = "spaces")] public List<string>? Spaces { get; set; }
    [DataMember(Name = "classes")] public List<string>? Classes { get; set; }

    public static AccountAnswer Failed(string? id, string error) => new() { Id = id, Ok = false, Error = error };
}

/// <summary>What a WMI query from Devices may be: a read (SELECT, ASSOCIATORS OF or REFERENCES OF) in a namespace on the computer itself.</summary>
public static class WmiAsk
{
    public const string DefaultSpace = @"root\cimv2";
    public const int MaxQuery = 4000;

    // root and names under it, never \\another-computer\root.
    private static readonly System.Text.RegularExpressions.Regex SpaceShape =
        new(@"^root(\\[A-Za-z0-9_]+)*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex Reads =
        new(@"^(select|associators\s+of|references\s+of)\s", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static string Space(string? space) => string.IsNullOrWhiteSpace(space) ? DefaultSpace : space!.Trim().Replace('/', '\\');

    /// <summary>Why it can't be the namespace, or null when it can.</summary>
    public static string? BadSpace(string? space)
    {
        var s = Space(space);
        return s.Length <= 200 && SpaceShape.IsMatch(s) ? null : @"Use a namespace on the computer itself, such as root\cimv2.";
    }

    /// <summary>Why it can't be asked, or null when it can.</summary>
    public static string? BadQuery(string? query)
    {
        var q = query?.Trim() ?? "";
        if (q.Length == 0)
        {
            return "Type a query, such as SELECT * FROM Win32_BIOS.";
        }
        if (q.Length > MaxQuery)
        {
            return $"A query can be {MaxQuery} characters at most.";
        }
        return Reads.IsMatch(q) ? null : "Only reads: a query starts with SELECT, ASSOCIATORS OF or REFERENCES OF.";
    }
}

/// <summary>What the client's sensors read on the computer itself: a service's state, or a drive's free space. Reads only.</summary>
#if NET
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
public static class SeatChecks
{
    public static bool Is(AccountOrder order) => order.Action == AccountOrder.Service || order.Action == AccountOrder.Disk;

    public static AccountAnswer Do(AccountOrder order)
    {
        var name = order.Name?.Trim() ?? "";
        try
        {
            if (order.Action == AccountOrder.Service)
            {
                var all = System.ServiceProcess.ServiceController.GetServices();
                try
                {
                    var found = all.FirstOrDefault(s => string.Equals(s.ServiceName, name, StringComparison.OrdinalIgnoreCase))
                        ?? all.FirstOrDefault(s => string.Equals(s.DisplayName, name, StringComparison.OrdinalIgnoreCase));
                    return found is null ? AccountAnswer.Failed(order.Id, $"There's no service called {name} on {Environment.MachineName}.")
                        : new AccountAnswer { Id = order.Id, Ok = true, State = found.Status.ToString(), StartType = found.StartType.ToString(), Detail = $"{found.DisplayName} ({found.ServiceName})" };
                }
                finally
                {
                    foreach (var s in all)
                    {
                        s.Dispose();
                    }
                }
            }
            var drive = new System.IO.DriveInfo(name.TrimEnd('\\', ':') + ":\\");
            return !drive.IsReady ? AccountAnswer.Failed(order.Id, $"{drive.Name.TrimEnd('\\')} isn't ready on {Environment.MachineName}.")
                : new AccountAnswer { Id = order.Id, Ok = true, Total = drive.TotalSize, Free = drive.AvailableFreeSpace, Detail = drive.VolumeLabel };
        }
        catch (Exception e) when (e is ArgumentException || e is System.IO.IOException || e is InvalidOperationException || e is UnauthorizedAccessException)
        {
            return AccountAnswer.Failed(order.Id, $"Windows on {Environment.MachineName} said: {e.Message.Trim().TrimEnd('.')}.");
        }
    }
}

/// <summary>This computer's Windows by name, the way the client reports it and the server shows its own.</summary>
#if NET
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
public static class WindowsVersion
{
    /// <summary>Such as Windows 11 Pro 24H2. Windows 11 still calls itself Windows 10 in the registry, so its build says which.</summary>
    public static string Name()
    {
        using var machine = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
        using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName") as string ?? "Windows";
        if (int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var build) && build >= 22000)
        {
            product = product.Replace("Windows 10", "Windows 11");
        }
        return key?.GetValue("DisplayVersion") is string display ? $"{product} {display}" : product;
    }
}

/// <summary>Text helpers the server and the client both use.</summary>
public static class Words
{
    /// <summary>Trimmed, or null when there's nothing in it.</summary>
    public static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();
}

/// <summary>
/// This computer's own accounts, through Windows' account API. It needs administrator rights, which the client service
/// (LocalSystem) and the installed server have.
/// </summary>
#if NET
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
public static class WindowsUsers
{
    private const string AdministratorsSid = "S-1-5-32-544";

    public static AccountAnswer Do(AccountOrder order)
    {
        var id = order.Id;
        try
        {
            using var machine = new PrincipalContext(ContextType.Machine);
            var name = order.Name?.Trim() ?? "";
            switch (order.Action)
            {
                case AccountOrder.List:
                    return new AccountAnswer { Id = id, Ok = true, Accounts = Accounts(machine), Groups = GroupsOf(machine), Admins = AdminsOrNone() };
                case AccountOrder.Create:
                    return Create(machine, order);
                case AccountOrder.Admins:
                    return ChangeAdmins(order);
            }
            if (name.Length == 0)
            {
                return AccountAnswer.Failed(id, "Which account?");
            }
            using var user = UserPrincipal.FindByIdentity(machine, IdentityType.SamAccountName, name);
            if (user is null)
            {
                return AccountAnswer.Failed(id, $"There's no account called {name} on {Environment.MachineName}.");
            }
            switch (order.Action)
            {
                case AccountOrder.Password:
                    if (string.IsNullOrEmpty(order.NewPassword))
                    {
                        return AccountAnswer.Failed(id, "Give a password.");
                    }
                    user.SetPassword(order.NewPassword);
                    if (user.IsAccountLockedOut())
                    {
                        user.UnlockAccount();
                    }
                    if (order.MustChange)
                    {
                        user.PasswordNeverExpires = false;
                        user.Save();
                        user.ExpirePasswordNow();
                    }
                    return new AccountAnswer { Id = id, Ok = true };
                case AccountOrder.Enable:
                case AccountOrder.Disable:
                    user.Enabled = order.Action == AccountOrder.Enable;
                    user.Save();
                    return new AccountAnswer { Id = id, Ok = true };
                case AccountOrder.Groups:
                    // Both always run, and it's done with a warning: a group that didn't work doesn't hide the ones that did.
                    var added = Join(machine, user, order.Add, true);
                    var removed = Join(machine, user, order.Remove, false);
                    return new AccountAnswer { Id = id, Ok = true, Warning = added is null ? removed : removed is null ? added : added + " " + removed };
                case AccountOrder.Delete:
                    if (BuiltIn(user))
                    {
                        return AccountAnswer.Failed(id, $"{user.SamAccountName} is a built-in Windows account. Disable it instead.");
                    }
                    user.Delete();
                    return new AccountAnswer { Id = id, Ok = true };
                default:
                    return AccountAnswer.Failed(id, $"This client doesn't know how to {order.Action}.");
            }
        }
        catch (PasswordException)
        {
            return AccountAnswer.Failed(id, $"That password doesn't meet {Environment.MachineName}'s password rules. Try a longer one, with capitals, numbers or symbols.");
        }
        catch (PrincipalExistsException)
        {
            return AccountAnswer.Failed(id, $"There's already an account called {order.Name?.Trim()} on {Environment.MachineName}.");
        }
        catch (UnauthorizedAccessException)
        {
            return AccountAnswer.Failed(id, $"Windows on {Environment.MachineName} said no: this needs an administrator, and it's running as {Environment.UserName}.");
        }
        catch (Exception e) when (e is PrincipalException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return AccountAnswer.Failed(id, $"Windows on {Environment.MachineName} said: {e.Message.Trim().TrimEnd('.')}.");
        }
    }

    private static AccountAnswer Create(PrincipalContext machine, AccountOrder order)
    {
        if (AccountOrder.BadName(order.Name) is { } bad)
        {
            return AccountAnswer.Failed(order.Id, bad);
        }
        if (string.IsNullOrEmpty(order.NewPassword))
        {
            return AccountAnswer.Failed(order.Id, "Give a password.");
        }
        var name = order.Name!.Trim();
        using var user = new UserPrincipal(machine)
        {
            Name = name,
            DisplayName = Words.Blank(order.FullName),
            Description = Words.Blank(order.Description),
            Enabled = true,
            PasswordNeverExpires = order.NeverExpires && !order.MustChange,
        };
        user.SetPassword(order.NewPassword);
        user.Save();
        if (order.MustChange)
        {
            user.ExpirePasswordNow();
        }
        var warning = Join(machine, user, order.Add, true);
        return new AccountAnswer { Id = order.Id, Ok = true, Warning = warning is null ? null : $"Made {name}, but {char.ToLowerInvariant(warning[0])}{warning.Substring(1)}" };
    }

    /// <summary>Puts the account in the groups (or takes it out); what went wrong, or null.</summary>
    private static string? Join(PrincipalContext machine, UserPrincipal user, List<string>? groups, bool add)
    {
        var problems = new List<string>();
        foreach (var g in (groups ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var group = GroupPrincipal.FindByIdentity(machine, g.Trim());
                if (group is null)
                {
                    problems.Add($"there's no group called {g}");
                    continue;
                }
                Member(group, user, add);
            }
            catch (Exception e) when (e is PrincipalException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                problems.Add($"{g} said: {e.Message.Trim().TrimEnd('.')}");
            }
        }
        if (problems.Count == 0)
        {
            return null;
        }
        var text = string.Join("; ", problems);
        return char.ToUpperInvariant(text[0]) + text.Substring(1) + ".";
    }

    /// <summary>Into the group or out of it; already so is fine.</summary>
    private static void Member(GroupPrincipal group, UserPrincipal user, bool add)
    {
        try
        {
            var member = group.Members.Contains(user);
            if (add && !member)
            {
                group.Members.Add(user);
                group.Save();
            }
            else if (!add && member)
            {
                group.Members.Remove(user);
                group.Save();
            }
        }
        catch (Exception e) when (e is PrincipalOperationException or NoMatchingPrincipalException or System.Runtime.InteropServices.COMException)
        {
            // Contains lists the members, which fails when one can't be resolved (a deleted domain account): change it without listing.
            Direct(group, user, add);
        }
    }

    // ERROR_MEMBER_IN_ALIAS and ERROR_MEMBER_NOT_IN_ALIAS, as HRESULTs.
    private const uint AlreadyIn = 0x80070562;
    private const uint AlreadyOut = 0x80070561;

    /// <summary>Adds or removes by SID on the group's own directory entry, as the account API does underneath.</summary>
    private static void Direct(GroupPrincipal group, UserPrincipal user, bool add)
    {
        var entry = (System.DirectoryServices.DirectoryEntry) group.GetUnderlyingObject();
        try
        {
            entry.Invoke(add ? "Add" : "Remove", "WinNT://" + user.Sid.Value);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or System.Reflection.TargetInvocationException)
        {
            var cause = e.InnerException ?? e;
            if (cause is System.Runtime.InteropServices.COMException com && unchecked((uint) com.ErrorCode) == (add ? AlreadyIn : AlreadyOut))
            {
                return;
            }
            throw new PrincipalOperationException(cause.Message, cause);
        }
    }

    /// <summary>This computer's accounts without their groups (quicker), for the inventory.</summary>
    public static List<WindowsAccount> Locals()
    {
        using var machine = new PrincipalContext(ContextType.Machine);
        return Accounts(machine, false);
    }

    private static List<WindowsAccount> Accounts(PrincipalContext machine, bool withGroups = true)
    {
        var found = new List<WindowsAccount>();
        using var filter = new UserPrincipal(machine);
        using var search = new PrincipalSearcher(filter);
        using var all = search.FindAll();
        foreach (var p in all)
        {
            using var u = (UserPrincipal) p;
            List<string>? groups = withGroups ? new List<string>() : null;
            try
            {
                if (groups is not null)
                {
                    using var memberOf = u.GetGroups();
                    foreach (var g in memberOf)
                    {
                        using (g)
                        {
                            if (!string.IsNullOrEmpty(g.Name))
                            {
                                groups.Add(g.Name);
                            }
                        }
                    }
                }
            }
            catch (PrincipalOperationException)
            {
                groups = new List<string>();
            }
            found.Add(new WindowsAccount
            {
                Name = u.SamAccountName ?? u.Name ?? "",
                FullName = Words.Blank(u.DisplayName),
                Description = Words.Blank(u.Description),
                Enabled = u.Enabled ?? true,
                Locked = Safe(u.IsAccountLockedOut),
                NeverExpires = u.PasswordNeverExpires,
                BuiltIn = BuiltIn(u),
                Groups = groups,
                LastSignIn = Iso(u.LastLogon),
                PasswordSet = Iso(u.LastPasswordSet),
            });
        }
        return found.OrderBy(a => a.BuiltIn).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<WindowsGroup> GroupsOf(PrincipalContext machine)
    {
        var found = new List<WindowsGroup>();
        using var filter = new GroupPrincipal(machine);
        using var search = new PrincipalSearcher(filter);
        using var all = search.FindAll();
        foreach (var p in all)
        {
            using var g = (GroupPrincipal) p;
            found.Add(new WindowsGroup { Name = g.Name ?? "", Sid = g.Sid?.Value, Description = Words.Blank(g.Description) });
        }
        // Administrators first, then the rest by name.
        return found.Where(g => g.Name.Length > 0).OrderBy(g => g.Sid != AdministratorsSid).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Administrator (500), Guest (501), DefaultAccount (503) and WDAGUtilityAccount (504).</summary>
    private static bool BuiltIn(UserPrincipal u)
    {
        var sid = u.Sid?.Value;
        var rid = sid is null ? "" : sid.Substring(sid.LastIndexOf('-') + 1);
        return rid is "500" or "501" or "503" or "504";
    }

    private static bool Safe(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is PrincipalException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    // The account API gives local times for these.
    private static string? Iso(DateTime? at) => at is { } t && t.Year > 1970
        ? (t.Kind == DateTimeKind.Utc ? t : DateTime.SpecifyKind(t, DateTimeKind.Local).ToUniversalTime()).ToString("o") : null;

    // --- Administrators, domain members included ---------------------------------------------------------------------
    // Read and changed with the NetLocalGroup calls: a deleted account in the group shows as its SID instead of failing the list.

    private const int MoreData = 234;
    private const int MemberNotIn = 1377; // ERROR_MEMBER_NOT_IN_ALIAS
    private const int MemberIn = 1378; // ERROR_MEMBER_IN_ALIAS

    /// <summary>Everyone in this computer's Administrators group.</summary>
    public static List<WindowsMember> Admins()
    {
        var group = AdminsName();
        var found = new List<WindowsMember>();
        var resume = System.IntPtr.Zero;
        int status;
        do
        {
            status = NetLocalGroupGetMembers(null, group, 2, out var buffer, -1, out var read, out _, ref resume);
            if (status != 0 && status != MoreData)
            {
                throw new System.ComponentModel.Win32Exception(status);
            }
            try
            {
                var size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemberInfo2));
                for (var i = 0; i < read && found.Count < WindowsMember.Max; i++)
                {
                    var m = (MemberInfo2) System.Runtime.InteropServices.Marshal.PtrToStructure(new System.IntPtr(buffer.ToInt64() + (long) i * size), typeof(MemberInfo2))!;
                    found.Add(Member(m));
                }
            }
            finally
            {
                if (buffer != System.IntPtr.Zero)
                {
                    NetApiBufferFree(buffer);
                }
            }
        }
        while (status == MoreData && found.Count < WindowsMember.Max);
        return found;
    }

    // The list is extra: when Windows won't give it, the accounts still come back.
    private static List<WindowsMember>? AdminsOrNone()
    {
        try
        {
            return Admins();
        }
        catch (SystemException)
        {
            return null;
        }
    }

    private static WindowsMember Member(MemberInfo2 m)
    {
        var sid = m.Sid == System.IntPtr.Zero ? null : new System.Security.Principal.SecurityIdentifier(m.Sid).Value;
        var name = string.IsNullOrEmpty(m.DomainAndName) ? sid ?? "" : m.DomainAndName!;
        var slash = name.IndexOf('\\');
        var local = slash > 0 && string.Equals(name.Substring(0, slash), Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        // A domain local group in AD is an alias (4) too: only this computer's and BUILTIN's are local groups.
        var builtIn = sid is not null && sid.StartsWith("S-1-5-32-", StringComparison.Ordinal);
        var kind = m.SidUsage switch
        {
            1 => local ? "local" : "user",
            2 or 4 => local || builtIn ? "localgroup" : "group",
            5 => "wellknown",
            9 => "computer",
            _ => "unknown",
        };
        return new WindowsMember { Name = name, Sid = sid, Kind = kind };
    }

    // Administrators by its SID, so it's found in any language.
    private static string AdminsName()
    {
        var full = new System.Security.Principal.SecurityIdentifier(AdministratorsSid).Translate(typeof(System.Security.Principal.NTAccount)).Value;
        return full.Substring(full.IndexOf('\\') + 1);
    }

    /// <summary>Adds the names in Add and takes out the SIDs in Remove; each is tried, and what didn't work is the warning.</summary>
    private static AccountAnswer ChangeAdmins(AccountOrder order)
    {
        var group = AdminsName();
        var problems = new List<string>();
        var done = 0;
        foreach (var name in (order.Add ?? new List<string>()).Select(n => n?.Trim() ?? "").Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var info = new MemberInfo3 { DomainAndName = name };
            var status = NetLocalGroupAddMembers(null, group, 3, ref info, 1);
            if (status is 0 or MemberIn)
            {
                done++;
            }
            else
            {
                problems.Add($"{name}: {Said(status)}");
            }
        }
        foreach (var text in (order.Remove ?? new List<string>()).Select(n => n?.Trim() ?? "").Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            System.Security.Principal.SecurityIdentifier sid;
            try
            {
                sid = new System.Security.Principal.SecurityIdentifier(text);
            }
            catch (ArgumentException)
            {
                problems.Add($"{text}: that isn't a SID");
                continue;
            }
            var bytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(bytes, 0);
            var pinned = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                var info = new MemberInfo0 { Sid = pinned.AddrOfPinnedObject() };
                var status = NetLocalGroupDelMembers(null, group, 0, ref info, 1);
                if (status is 0 or MemberNotIn)
                {
                    done++;
                }
                else
                {
                    problems.Add($"{Named(sid)}: {Said(status)}");
                }
            }
            finally
            {
                pinned.Free();
            }
        }
        var said = problems.Count == 0 ? null : string.Join("; ", problems) + ".";
        return done == 0 && said is not null ? AccountAnswer.Failed(order.Id, $"Windows on {Environment.MachineName} said no. {said}")
            : new AccountAnswer { Id = order.Id, Ok = true, Warning = said, Admins = AdminsOrNone() };
    }

    private static string Named(System.Security.Principal.SecurityIdentifier sid)
    {
        try
        {
            return sid.Translate(typeof(System.Security.Principal.NTAccount)).Value;
        }
        catch (System.Security.Principal.IdentityNotMappedException)
        {
            return sid.Value;
        }
    }

    private static string Said(int status) => status switch
    {
        5 => "access denied",
        1387 => "there's no account by that name",
        1388 => "that kind of account can't go in a local group",
        1371 => "Windows won't change that one", // a special account or group
        _ => new System.ComponentModel.Win32Exception(status).Message.Trim().TrimEnd('.'),
    };

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct MemberInfo2
    {
        public System.IntPtr Sid;
        public int SidUsage;
        public string? DomainAndName;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct MemberInfo3
    {
        public string DomainAndName;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MemberInfo0
    {
        public System.IntPtr Sid;
    }

    [System.Runtime.InteropServices.DllImport("netapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int NetLocalGroupGetMembers(string? server, string group, int level, out System.IntPtr buffer, int max, out int read, out int total, ref System.IntPtr resume);

    [System.Runtime.InteropServices.DllImport("netapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int NetLocalGroupAddMembers(string? server, string group, int level, ref MemberInfo3 info, int count);

    [System.Runtime.InteropServices.DllImport("netapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int NetLocalGroupDelMembers(string? server, string group, int level, ref MemberInfo0 info, int count);

    [System.Runtime.InteropServices.DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(System.IntPtr buffer);
}
