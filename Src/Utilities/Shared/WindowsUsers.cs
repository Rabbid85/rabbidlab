using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Runtime.Serialization;

// Compiled into both the server (net10.0) and the client (net472): the same code makes and changes Windows accounts on
// the server itself and on each computer in Seats. The [DataMember] names are the JSON both sides send.
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

    [DataMember(Name = "id")] public string? Id { get; set; }
    [DataMember(Name = "action")] public string? Action { get; set; }
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

    public static AccountAnswer Failed(string? id, string error) => new() { Id = id, Ok = false, Error = error };
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
                    return new AccountAnswer { Id = id, Ok = true, Accounts = Accounts(machine), Groups = GroupsOf(machine) };
                case AccountOrder.Create:
                    return Create(machine, order);
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
                        return AccountAnswer.Failed(id, $"{user.SamAccountName} is one of Windows' own accounts. Turn it off instead.");
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

    private static List<WindowsAccount> Accounts(PrincipalContext machine)
    {
        var found = new List<WindowsAccount>();
        using var filter = new UserPrincipal(machine);
        using var search = new PrincipalSearcher(filter);
        using var all = search.FindAll();
        foreach (var p in all)
        {
            using var u = (UserPrincipal) p;
            var groups = new List<string>();
            try
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
}
