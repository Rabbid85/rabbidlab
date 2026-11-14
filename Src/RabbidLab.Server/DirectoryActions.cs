using System.Globalization;
using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>What changes on a user: null leaves it as it is, empty clears it.</summary>
public sealed record UserEdit(string? First, string? Last, string? Display, string? Description, string? Office, string? Phone, string? Email,
    string? Title, string? Department, string? Company, string? Manager);

public sealed record FlagForm(string? Flag, bool On);

/// <summary>The last day the account works (yyyy-MM-dd), or none for never.</summary>
public sealed record ExpiryForm(string? Date);

public sealed record AdRenameForm(string? Name);

public sealed record AdMoveForm(string? Ou);

/// <summary>A group or computer by DN, and where it goes.</summary>
public sealed record ObjectMoveForm(string? Dn, string? Ou);

/// <summary>A group or computer by DN, and the groups to put it in or take it out of.</summary>
public sealed record ObjectGroupsForm(string? Dn, List<string>? Add, List<string>? Remove);

/// <summary>A group's or computer's details: null leaves it, empty clears it. Email and notes are a group's, location a computer's.</summary>
public sealed record ObjectEdit(string? Dn, string? Description, string? Email, string? Notes, string? Location);

/// <summary>A computer account made ahead of the computer joining (pre-staged): its name, OU and description.</summary>
public sealed record NewAdComputer(string? Name, string? Ou, string? Description);

/// <summary>The menus on Users, Groups and Computers: account options, expiry, properties, rename and move, one at a time.</summary>
public interface IDirectoryActions
{
    /// <summary>Where the group or computer is now, readably.</summary>
    Task<string> MoveObject(string dn, string ou, CancellationToken stop);

    /// <summary>What didn't work, if anything.</summary>
    Task<string?> ChangeMemberOf(string dn, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken stop);

    /// <summary>The fields that changed, by name.</summary>
    Task<List<string>> EditObject(ObjectEdit form, CancellationToken stop);

    /// <summary>A computer account, waiting for a computer of that name to join the domain.</summary>
    Task<AdComputer> CreateComputer(NewAdComputer form, CancellationToken stop);

    Task SetFlag(string username, string flag, bool on, CancellationToken stop);

    /// <summary>The account stops working after that day ends (server time), as ADUC's End of; null for never.</summary>
    Task SetExpiry(string username, DateOnly? last, CancellationToken stop);

    /// <summary>The fields that changed, by name, for the activity log.</summary>
    Task<List<string>> Edit(string username, UserEdit form, CancellationToken stop);

    Task Rename(string username, string name, CancellationToken stop);

    /// <summary>Where it is now, readably.</summary>
    Task<string> Move(string username, string ou, CancellationToken stop);
}

public sealed partial class DirectoryTools : IDirectoryActions
{
    // The account options that are one bit of userAccountControl.
    private static readonly Dictionary<string, int> Bits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["noexpiry"] = DontExpire,
        ["sensitive"] = NotDelegated,
        ["smartcard"] = SmartcardRequired,
        ["reversible"] = ReversiblePassword,
        ["des"] = DesKeysOnly,
        ["preauth"] = NoPreauthNeeded,
        ["notreq"] = PasswordNotRequired,
    };

    public static bool KnownFlag(string flag) => Bits.ContainsKey(flag) || flag is "mustchange" or "cannotchange";

    private static readonly string[] ObjectAttributes = ["objectClass", "sAMAccountName", "userAccountControl", "groupType", "adminCount", "isCriticalSystemObject",
        "objectSid", "cn", "member", "description", "mail", "info", "location"];

    /// <summary>A user, group or computer in scope. Users go through Changeable; Active Directory refuses what may not be done.</summary>
    private static async Task<(LdapRow Row, string Kind)> ChangeableObject(ILdap ldap, DirectoryToolsFile f, string dn, CancellationToken stop)
    {
        var row = (await ldap.Find(Inside(dn, ldap.BaseDn), "(objectClass=*)", false, ObjectAttributes, 1, stop)).FirstOrDefault()
            ?? throw new ToolProblem("That isn't in Active Directory any more.", StatusCodes.Status404NotFound);
        InScope(row.Dn, f, ldap.BaseDn);
        var kind = KindOf(row);
        switch (kind)
        {
            case "group":
                return (await ChangeableGroup(ldap, f, row.Dn, stop), kind);
            case "computer":
                return (row, kind); // domain controllers too; only moving, disabling and deleting them is refused
            case "user":
                return (await Changeable(ldap, f, row.Text("sAMAccountName") ?? "", stop), kind);
            default:
                throw new ToolProblem("Only users, groups and computers can be changed here.");
        }
    }

    public async Task<string> MoveObject(string dn, string ou, CancellationToken stop)
    {
        if (string.IsNullOrWhiteSpace(ou))
        {
            throw new ToolProblem("Pick where to move it.");
        }
        using var ldap = await Open(stop);
        var f = Ready();
        var (row, kind) = await ChangeableObject(ldap, f, dn, stop);
        if (kind == "computer" && ((row.Number("userAccountControl") ?? 0) & 0x2000) != 0)
        {
            throw new ToolProblem($"{Ldap.Name(row.Dn)} is a domain controller. Domain controllers stay in Domain Controllers.");
        }
        var into = await Into(ldap, ou, f.Scope!, stop);
        InScope(into, f, ldap.BaseDn);
        if (!string.Equals(Ldap.Parent(row.Dn), into, StringComparison.OrdinalIgnoreCase))
        {
            await Relocate(ldap, f, row.Dn, Ldap.Split(row.Dn)[0], into, stop);
        }
        return Ldap.Path(into, ldap.BaseDn) is { Length: > 0 } path ? path : f.Domain!;
    }

    public async Task<string?> ChangeMemberOf(string dn, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var (row, _) = await ChangeableObject(ldap, f, dn, stop);
        // A group can't be put in itself.
        var a = await Join(ldap, f, row.Dn, add.Where(g => !string.Equals(g?.Trim(), row.Dn, StringComparison.OrdinalIgnoreCase)), true, stop);
        var b = await Join(ldap, f, row.Dn, remove, false, stop);
        return a is null ? b : b is null ? a : $"{a} {b}";
    }

    public async Task<AdComputer> CreateComputer(NewAdComputer form, CancellationToken stop)
    {
        var name = (form.Name ?? "").Trim().ToUpperInvariant();
        if (name.Length is 0 or > 15 || !System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Z0-9-]+$") || name.StartsWith('-') || name.All(char.IsAsciiDigit))
        {
            throw new ToolProblem("Computer names are 1 to 15 letters, digits and hyphens. They can't be all digits or start with a hyphen.");
        }
        using var ldap = await Open(stop);
        var f = Ready();
        var ou = await Into(ldap, form.Ou, NewComputersOf(f), stop);
        if (!Ldap.Under(ou, f.Scope!))
        {
            throw new ToolProblem($"{Product.Title}'s account can only make computers in {ScopeName(f.Scope!, ldap.BaseDn, f.Domain!)}.");
        }
        Reached(ou);
        if (await Taken(ldap, name + "$", stop))
        {
            throw new ToolProblem($"There's already a computer called {name} in {f.Domain}.");
        }
        var dn = $"CN={Ldap.Escape(name)},{ou}";
        var attributes = new Dictionary<string, object[]>
        {
            ["objectClass"] = ["computer"],
            ["sAMAccountName"] = [name + "$"],
            // A workstation account with no password yet, as ADUC makes one: the computer sets its own when it joins.
            ["userAccountControl"] = ["4128"],
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
            throw new ToolProblem(e.Code switch
            {
                LdapRefused.EntryExists => $"There's already something called {name} in {Ldap.Path(ou, ldap.BaseDn)}.",
                LdapRefused.InsufficientRights => $"{f.Account} isn't allowed to make computers in {Ldap.Path(ou, ldap.BaseDn)}.",
                _ => Said(e, f.Account!),
            });
        }
        var made = (await ldap.Find(dn, "(objectClass=*)", false, ComputerAttributes, 1, stop)).FirstOrDefault();
        return made is null ? throw new ToolProblem($"{name} was made, but couldn't be read back.") : Computer(made, ldap.BaseDn);
    }

    public async Task<List<string>> EditObject(ObjectEdit form, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var (row, kind) = await ChangeableObject(ldap, f, form.Dn ?? "", stop);
        (string Attribute, string Label, int Max, string? Value)[] fields = kind switch
        {
            "group" => [("description", "description", 1024, form.Description), ("mail", "email", 256, form.Email), ("info", "notes", 1024, form.Notes)],
            "computer" => [("description", "description", 1024, form.Description), ("location", "location", 1024, form.Location)],
            _ => throw new ToolProblem("Users are edited with Edit properties on Users."),
        };
        var changes = new List<LdapMod>();
        var changed = new List<string>();
        foreach (var (attribute, label, max, typed) in fields)
        {
            if (typed is null)
            {
                continue;
            }
            var value = attribute == "info" ? typed.Trim().Replace("\r\n", "\n").Replace("\n", "\r\n") : typed.Trim();
            if (value.Length > max)
            {
                throw new ToolProblem($"The {label} is {max} characters at most.");
            }
            if (value.Any(c => char.IsControl(c) && c is not ('\r' or '\n')))
            {
                throw new ToolProblem($"The {label} has characters that can't be stored.");
            }
            var was = row.Text(attribute) ?? "";
            if (string.Equals(was, value, StringComparison.Ordinal) || (value.Length == 0 && was.Length == 0))
            {
                continue;
            }
            changes.Add(value.Length == 0 ? new(LdapChange.Delete, attribute) : new(LdapChange.Replace, attribute, value));
            changed.Add(label);
        }
        if (changes.Count > 0)
        {
            await Change(ldap, row.Dn, changes, f, password: false, stop);
        }
        return changed;
    }

    public async Task SetFlag(string username, string flag, bool on, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var user = await Changeable(ldap, f, username, stop, signIn: true);
        var flags = (int) (user.Number("userAccountControl") ?? NormalAccount);
        if (Bits.TryGetValue(flag, out var bit))
        {
            var now = on ? flags | bit : flags & ~bit;
            if (now != flags)
            {
                await Change(ldap, user.Dn, [new(LdapChange.Replace, "userAccountControl", now.ToString(CultureInfo.InvariantCulture))], f, password: false, stop);
            }
            return;
        }
        switch (flag)
        {
            case "mustchange":
                if (on && (flags & DontExpire) != 0)
                {
                    throw new ToolProblem($"{username}'s password never expires, so it can't be made to change at the next sign-in. Set it to expire per policy first.");
                }
                // 0 makes them change it; -1 counts the password as set just now.
                await Change(ldap, user.Dn, [new(LdapChange.Replace, "pwdLastSet", on ? "0" : "-1")], f, password: false, stop);
                return;
            case "cannotchange":
                var dacl = await ldap.ReadDacl(user.Dn, stop) ?? throw new ToolProblem($"{f.Account} can't read the permissions on {username}.");
                try
                {
                    await ldap.WriteDacl(user.Dn, Dacl.SetCannotChangePassword(dacl, on), stop);
                }
                catch (LdapRefused e)
                {
                    throw new ToolProblem(e.Code == LdapRefused.InsufficientRights ? $"{f.Account} isn't allowed to change the permissions on {username}." : Said(e, f.Account!));
                }
                catch (FormatException e)
                {
                    throw new ToolProblem($"The permissions on {username} couldn't be changed: {e.Message}");
                }
                return;
            default:
                throw new ToolProblem("That isn't an account option.");
        }
    }

    public async Task SetExpiry(string username, DateOnly? last, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var user = await Changeable(ldap, f, username, stop, signIn: true);
        // ADUC's End of: midnight at the start of the next day, local time.
        var value = last is { } d ? d.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToFileTimeUtc().ToString(CultureInfo.InvariantCulture) : "0";
        await Change(ldap, user.Dn, [new(LdapChange.Replace, "accountExpires", value)], f, password: false, stop);
    }

    // The properties Edit properties shows, by the attribute each one is.
    private static readonly (string Field, string Attribute, string Label, int Max)[] EditFields =
    [
        ("First", "givenName", "first name", 64), ("Last", "sn", "last name", 64), ("Display", "displayName", "display name", 256),
        ("Description", "description", "description", 1024), ("Office", "physicalDeliveryOfficeName", "office", 128), ("Phone", "telephoneNumber", "phone", 64),
        ("Email", "mail", "email", 256), ("Title", "title", "job title", 128), ("Department", "department", "department", 64), ("Company", "company", "company", 64),
        ("Manager", "manager", "manager", 1024),
    ];

    public async Task<List<string>> Edit(string username, UserEdit form, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var user = await Changeable(ldap, f, username, stop);
        var given = new Dictionary<string, string?>
        {
            ["First"] = form.First, ["Last"] = form.Last, ["Display"] = form.Display, ["Description"] = form.Description, ["Office"] = form.Office,
            ["Phone"] = form.Phone, ["Email"] = form.Email, ["Title"] = form.Title, ["Department"] = form.Department, ["Company"] = form.Company,
            ["Manager"] = form.Manager,
        };
        var now = (await ldap.Find(user.Dn, "(objectClass=*)", false, [.. EditFields.Select(e => e.Attribute)], 1, stop)).FirstOrDefault() ?? user;
        var changes = new List<LdapMod>();
        var changed = new List<string>();
        foreach (var (field, attribute, label, max) in EditFields)
        {
            if (given[field] is not { } typed)
            {
                continue;
            }
            var value = typed.Trim();
            if (value.Length > max)
            {
                throw new ToolProblem($"The {label} is {max} characters at most.");
            }
            if (value.Any(char.IsControl))
            {
                throw new ToolProblem($"The {label} has characters that can't be stored.");
            }
            if (field == "Manager" && value.Length > 0)
            {
                var boss = (await ldap.Find(Inside(value, ldap.BaseDn), "(&(objectCategory=person)(objectClass=user))", false, ["distinguishedName"], 1, stop)).FirstOrDefault()
                    ?? throw new ToolProblem("That manager isn't in Active Directory any more.");
                value = boss.Dn;
            }
            var was = now.Text(attribute) ?? "";
            if (string.Equals(was, value, StringComparison.Ordinal))
            {
                continue;
            }
            if (value.Length == 0)
            {
                if (was.Length > 0)
                {
                    changes.Add(new(LdapChange.Delete, attribute));
                    changed.Add(label);
                }
                continue;
            }
            changes.Add(new(LdapChange.Replace, attribute, value));
            changed.Add(label);
        }
        if (changes.Count > 0)
        {
            await Change(ldap, user.Dn, changes, f, password: false, stop);
        }
        return changed;
    }

    public async Task Rename(string username, string name, CancellationToken stop)
    {
        var to = Words.Blank(name) ?? throw new ToolProblem("Type the new name.");
        if (to.Length > 64)
        {
            throw new ToolProblem("The name is 64 characters at most.");
        }
        using var ldap = await Open(stop);
        var f = Ready();
        var user = await Changeable(ldap, f, username, stop);
        var parent = Ldap.Parent(user.Dn);
        if (!string.Equals(Ldap.Name(user.Dn), to, StringComparison.Ordinal))
        {
            await Relocate(ldap, f, user.Dn, $"CN={Ldap.Escape(to)}", parent, stop);
        }
        // ADUC's Rename sets the display name to match.
        await Change(ldap, $"CN={Ldap.Escape(to)},{parent}", [new(LdapChange.Replace, "displayName", to)], f, password: false, stop);
    }

    public async Task<string> Move(string username, string ou, CancellationToken stop)
    {
        if (string.IsNullOrWhiteSpace(ou))
        {
            throw new ToolProblem("Pick where to move it.");
        }
        using var ldap = await Open(stop);
        var f = Ready();
        var user = await Changeable(ldap, f, username, stop);
        var into = await Into(ldap, ou, f.Scope!, stop);
        InScope(into, f, ldap.BaseDn);
        if (!string.Equals(Ldap.Parent(user.Dn), into, StringComparison.OrdinalIgnoreCase))
        {
            await Relocate(ldap, f, user.Dn, Ldap.Split(user.Dn)[0], into, stop);
        }
        return Ldap.Path(into, ldap.BaseDn) is { Length: > 0 } path ? path : f.Domain!;
    }

    private static async Task Relocate(ILdap ldap, DirectoryToolsFile f, string dn, string rdn, string parent, CancellationToken stop)
    {
        try
        {
            await ldap.Rename(dn, rdn, parent, stop);
        }
        catch (LdapRefused e)
        {
            throw new ToolProblem(e.Code switch
            {
                LdapRefused.EntryExists => $"There's already something called {Ldap.Name(rdn)} in {Ldap.Path(parent, ldap.BaseDn)}.",
                LdapRefused.InsufficientRights => $"{f.Account} isn't allowed to move or rename {Ldap.Name(dn)} there.",
                _ => Said(e, f.Account!),
            });
        }
    }
}

public static class DirectoryActionsApi
{
    private static readonly Dictionary<string, (string On, string Off)> FlagWords = new()
    {
        ["mustchange"] = ("Made {0} change their password at the next sign-in", "Cleared the password change requirement on {0}"),
        ["noexpiry"] = ("Set {0}'s password to never expire", "Set {0}'s password to expire per policy"),
        ["cannotchange"] = ("Stopped {0} from changing their password", "Let {0} change their password"),
        ["sensitive"] = ("Marked {0} as sensitive and cannot be delegated", "Allowed delegation for {0}"),
        ["smartcard"] = ("Required a smart card for {0}", "Stopped requiring a smart card for {0}"),
        ["reversible"] = ("Turned on reversible password encryption for {0}", "Turned off reversible password encryption for {0}"),
        ["des"] = ("Set {0} to use only Kerberos DES encryption types", "Stopped {0} using only Kerberos DES encryption types"),
        ["preauth"] = ("Turned off Kerberos preauthentication for {0}", "Turned on Kerberos preauthentication for {0}"),
        ["notreq"] = ("Let {0} have no password", "Required a password for {0}"),
    };

    public static void MapDirectoryActionsApi(this WebApplication app)
    {
        // New computer: a computer account made ahead of the computer joining.
        app.MapPost("/api/ad/computers", (NewAdComputer form, HttpContext context, IDirectoryActions tools, ActivityLog log) =>
            UsersApi.Try(context, Permissions.AdCreate, async () =>
            {
                var c = await tools.CreateComputer(form, context.RequestAborted);
                log.Record(context, "users", "ad.computer.create", $"Made the computer account {c.Name} in {(c.Where is { Length: > 0 } w ? w : "the domain")}");
                return Results.Ok(c);
            }));

        // Groups and computers, by DN: where they are, the groups they're in, and their details.
        app.MapPost("/api/ad/object/move", (ObjectMoveForm form, HttpContext context, IDirectoryActions tools, ActivityLog log) =>
            UsersApi.Try(context, Permissions.AdChange, async () =>
            {
                var where = await tools.MoveObject(form.Dn ?? "", form.Ou ?? "", context.RequestAborted);
                log.Record(context, "users", "ad.move", $"Moved {Ldap.Name(form.Dn ?? "")} to {where}");
                return Results.Ok(new { where });
            }));

        app.MapPost("/api/ad/object/groups", (ObjectGroupsForm form, HttpContext context, IDirectoryActions tools, ActivityLog log) =>
            UsersApi.Try(context, Permissions.AdGroups, async () =>
            {
                var add = form.Add ?? [];
                var remove = form.Remove ?? [];
                var warning = await tools.ChangeMemberOf(form.Dn ?? "", add, remove, context.RequestAborted);
                var said = string.Join("; ", new[] { add.Count > 0 ? "added to " + string.Join(", ", add.Select(Ldap.Name)) : null, remove.Count > 0 ? "taken out of " + string.Join(", ", remove.Select(Ldap.Name)) : null }.OfType<string>());
                log.Record(context, "users", "ad.memberof", $"Changed the groups {Ldap.Name(form.Dn ?? "")} is in", warning is null ? "ok" : "warning", said + (warning is null ? "" : $". {warning}"));
                return Results.Ok(new { warning });
            }));

        app.MapPost("/api/ad/object/edit", (ObjectEdit form, HttpContext context, IDirectoryActions tools, ActivityLog log) =>
            UsersApi.Try(context, Permissions.AdChange, async () =>
            {
                var changed = await tools.EditObject(form, context.RequestAborted);
                if (changed.Count > 0)
                {
                    log.Record(context, "users", "ad.edit", $"Edited {Ldap.Name(form.Dn ?? "")}'s properties", detail: "Changed " + string.Join(", ", changed));
                }
                return Results.Ok(new { changed });
            }));

        // Change password at next sign-in goes with resetting passwords; the other options with editing.
        app.MapPost("/api/ad/users/{username}/flag", (string username, FlagForm form, HttpContext context, IDirectoryActions tools, ActivityLog log) =>
            UsersApi.Try(context, string.Equals(form.Flag?.Trim(), "mustchange", StringComparison.OrdinalIgnoreCase) ? Permissions.AdPassword : Permissions.AdChange, async () =>
            {
                var flag = form.Flag?.Trim().ToLowerInvariant() ?? "";
                if (!DirectoryTools.KnownFlag(flag))
                {
                    throw new ToolProblem("That isn't an account option.");
                }
                await tools.SetFlag(username, flag, form.On, context.RequestAborted);
                var words = FlagWords[flag];
                log.Record(context, "users", $"ad.flag.{flag}.{(form.On ? "on" : "off")}", string.Format(CultureInfo.InvariantCulture, form.On ? words.On : words.Off, username));
                return Results.Ok();
            }));

        app.MapPost("/api/ad/users/{username}/expiry", (string username, ExpiryForm form, HttpContext context, IDirectoryActions tools, ActivityLog log) =>
            UsersApi.Try(context, Permissions.AdChange, async () =>
            {
                DateOnly? last = null;
                if (!string.IsNullOrWhiteSpace(form.Date))
                {
                    if (!DateOnly.TryParseExact(form.Date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    {
                        throw new ToolProblem("Pick the last day the account works.");
                    }
                    last = d;
                }
                await tools.SetExpiry(username, last, context.RequestAborted);
                log.Record(context, "users", last is null ? "ad.expiry.clear" : "ad.expiry",
                    last is { } day ? $"Set {username}'s account to expire at the end of {day:yyyy-MM-dd}" : $"Removed {username}'s account expiration");
                return Results.Ok();
            }));

        app.MapPost("/api/ad/users/{username}/edit", (string username, UserEdit form, HttpContext context, IDirectoryActions tools, ActivityLog log) =>
            UsersApi.Try(context, Permissions.AdChange, async () =>
            {
                var changed = await tools.Edit(username, form, context.RequestAborted);
                if (changed.Count > 0)
                {
                    log.Record(context, "users", "ad.edit", $"Edited {username}'s properties", detail: "Changed " + string.Join(", ", changed));
                }
                return Results.Ok(new { changed });
            }));

        app.MapPost("/api/ad/users/{username}/rename", (string username, AdRenameForm form, HttpContext context, IDirectoryActions tools, ActivityLog log) =>
            UsersApi.Try(context, Permissions.AdChange, async () =>
            {
                await tools.Rename(username, form.Name ?? "", context.RequestAborted);
                log.Record(context, "users", "ad.rename", $"Renamed {username} to {form.Name!.Trim()}");
                return Results.Ok();
            }));

        app.MapPost("/api/ad/users/{username}/move", (string username, AdMoveForm form, HttpContext context, IDirectoryActions tools, ActivityLog log) =>
            UsersApi.Try(context, Permissions.AdChange, async () =>
            {
                var where = await tools.Move(username, form.Ou ?? "", context.RequestAborted);
                log.Record(context, "users", "ad.move", $"Moved {username} to {where}");
                return Results.Ok(new { where });
            }));
    }
}
