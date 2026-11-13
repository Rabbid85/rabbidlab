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

/// <summary>The Users grid's menu: account options, expiry, properties, rename and move, one user at a time.</summary>
public interface IDirectoryActions
{
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
    };

    public static bool KnownFlag(string flag) => Bits.ContainsKey(flag) || flag is "mustchange" or "cannotchange";

    public async Task SetFlag(string username, string flag, bool on, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var user = await Changeable(ldap, f, username, stop);
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
        var user = await Changeable(ldap, f, username, stop);
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
    };

    public static void MapDirectoryActionsApi(this WebApplication app)
    {
        app.MapPost("/api/ad/users/{username}/flag", (string username, FlagForm form, HttpContext context, IDirectoryActions tools, ActivityLog log) =>
            UsersApi.Try(context, Permissions.AdChange, async () =>
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
