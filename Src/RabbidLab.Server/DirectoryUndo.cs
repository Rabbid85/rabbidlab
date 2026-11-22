using System.Globalization;

namespace RabbidLab.Server;

/// <summary>
/// The change journal's side of the directory tools: each write notes what it changed and how to put it back
/// (ChangeNote), and Undo puts a change back once it has checked nothing changed since.
/// </summary>
public sealed partial class DirectoryTools : IDirectoryUndo
{
    // What the account options are called on the page.
    private static readonly Dictionary<string, string> FlagNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["noexpiry"] = "Password never expires",
        ["sensitive"] = "Account is sensitive and cannot be delegated",
        ["smartcard"] = "Smart card required",
        ["reversible"] = "Reversible encryption",
        ["des"] = "DES encryption only",
        ["preauth"] = "No Kerberos preauthentication",
        ["notreq"] = "Password not required",
        ["mustchange"] = "Must change password at next sign-in",
        ["cannotchange"] = "Cannot change password",
    };

    private const string NoUndoUnlock = "Active Directory locks an account only after too many incorrect passwords, so an unlock cannot be undone.";
    private const string NoUndoPassword = "The previous password is not stored, so a password reset cannot be undone.";

    private static string NoUndoCreate(string kind) => $"Undoing a create would delete the {kind}. Delete the {kind} instead.";

    private static string NoUndoRestore(string kind) => $"Undoing a restore would delete the {kind} again. Delete the {kind} instead.";

    // --- Noting what changed ---------------------------------------------------------------------------------------

    private static ChangedObject Noted(LdapRow row, string kind) =>
        new(row.Dn, row.Bytes("objectGUID") is { Length: 16 } b ? new Guid(b).ToString() : null, row.Text("displayName") ?? row.Text("cn") ?? Ldap.Name(row.Dn), kind);

    private static void Note(LdapRow row, string kind, params ChangeStep[] steps) => Note(Noted(row, kind), steps);

    private static void Note(ChangedObject o, params ChangeStep[] steps)
    {
        if (ChangeNote.Current is not { } note || steps.Length == 0)
        {
            return;
        }
        note.On(o);
        foreach (var s in steps)
        {
            note.Step(s);
        }
    }

    /// <summary>A change that can't be undone, with why.</summary>
    private static void Cannot(ChangedObject o, string why, params ChangeStep[] steps)
    {
        if (ChangeNote.Current is not { } note)
        {
            return;
        }
        note.On(o);
        note.Cannot(why);
        foreach (var s in steps)
        {
            note.Step(s);
        }
    }

    private static List<string> Shown(string? value) => string.IsNullOrEmpty(value) ? [] : [value];

    private static string Capital(string label) => label.Length == 0 ? label : char.ToUpperInvariant(label[0]) + label[1..];

    /// <summary>One userAccountControl bit, when it changed: Disabled shows as Enabled or Disabled, the rest as Yes or No.</summary>
    private static void NoteBit(LdapRow row, string kind, string label, long was, long now, int bit)
    {
        if ((was & bit) == (now & bit))
        {
            return;
        }
        string Said(long flags) => bit == Disabled ? ((flags & bit) != 0 ? "Disabled" : "Enabled") : (flags & bit) != 0 ? "Yes" : "No";
        Note(row, kind, new ChangeStep(label, [Said(was)], [Said(now)], new StepUndo("bit", "userAccountControl", Bit: bit, On: (was & bit) != 0)));
    }

    /// <summary>An option shown as Yes or No that changed from was to now.</summary>
    private static void NoteOption(LdapRow row, string flag, bool was, bool now)
    {
        if (was != now)
        {
            Note(row, "user", new ChangeStep(FlagNames[flag], [was ? "Yes" : "No"], [now ? "Yes" : "No"], new StepUndo(flag, On: was)));
        }
    }

    /// <summary>An attribute set from was to now, shown as names for DNs and as dates for accountExpires.</summary>
    private static ChangeStep SetStep(string label, string attribute, List<string> was, List<string> now) =>
        new(label, [.. was.Select(v => ShowValue(attribute, v))], [.. now.Select(v => ShowValue(attribute, v))], new StepUndo("set", attribute, Was: was, Now: now));

    private static string ShowValue(string attribute, string value) => attribute.ToLowerInvariant() switch
    {
        "manager" or "managedby" => Ldap.Name(value),
        "accountexpires" => Never(value) ? "Never" : long.TryParse(value, out var t) && FileTime(t) is { } at ? Expiry(at) : value,
        _ => value,
    };

    private static bool Never(string? accountExpires) => accountExpires is null or "" or "0" or "9223372036854775807";

    // Set by Rabbid Lab or ADUC: midnight at the start of the next day, local time, shown as the end of the day before.
    private static string Expiry(DateTime at)
    {
        var local = at.ToLocalTime();
        return local.TimeOfDay == TimeSpan.Zero ? $"End of {local.AddDays(-1):yyyy-MM-dd}" : local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Membership before and after as names; a long list shows only what changed and how many others there are.</summary>
    private static (List<string> Before, List<string> After) Membership(IEnumerable<string> had, IReadOnlyList<string> added, IReadOnlyList<string> removed, string others)
    {
        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        var before = had.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var after = before.Where(d => !removed.Any(r => Same(r, d))).Concat(added.Where(a => !before.Any(b => Same(a, b)))).ToList();
        List<string> Names(IEnumerable<string> dns) => [.. dns.Select(Ldap.Name).Order(StringComparer.OrdinalIgnoreCase)];
        if (before.Count + added.Count <= 12)
        {
            return (Names(before), Names(after));
        }
        var kept = before.Count(d => !removed.Any(r => Same(r, d)));
        var tail = kept > 0 ? [$"and {kept} other {others}"] : new List<string>();
        return ([.. Names(removed), .. tail], [.. Names(added), .. tail]);
    }

    private static void NoteGroups(LdapRow row, string kind, List<string> added, List<string> removed)
    {
        if (added.Count + removed.Count == 0)
        {
            return;
        }
        var (before, after) = Membership(row.Texts("memberOf"), added, removed, "groups");
        Note(row, kind, new ChangeStep("Member of", before, after, new StepUndo("groups", Add: removed, Remove: added)));
    }

    private static void NoteMembers(LdapRow group, List<string> added, List<string> removed)
    {
        if (added.Count + removed.Count == 0)
        {
            return;
        }
        var (before, after) = Membership(group.TextsInRanges("member"), added, removed, "members");
        Note(group, "group", new ChangeStep("Members", before, after, new StepUndo("members", Add: removed, Remove: added)));
    }

    /// <summary>Moved or renamed: where it was, and where it is now.</summary>
    private static void NotePlace(LdapRow row, string kind, string label, string newDn, string before, string after)
    {
        if (ChangeNote.Current is null)
        {
            return;
        }
        Note(row, kind, new ChangeStep(label, [before], [after], new StepUndo("place", Rdn: Ldap.Split(row.Dn)[0], Parent: Ldap.Parent(row.Dn), Now: [newDn])));
        ChangeNote.Current.At(newDn);
    }

    private static string PathIn(string parent, string baseDn, string domain) => Ldap.Path(parent, baseDn) is { Length: > 0 } path ? path : domain;

    /// <summary>
    /// Before a delete: with the Recycle Bin on, the object can come back by its GUID; otherwise the delete is permanent.
    /// Read only for a change the journal keeps.
    /// </summary>
    private static async Task NoteDelete(ILdap ldap, DirectoryToolsFile f, LdapRow row, string kind, CancellationToken stop)
    {
        if (ChangeNote.Current is null)
        {
            return;
        }
        var config = (await ldap.Find("", "(objectClass=*)", false, ["configurationNamingContext"], 1, stop)).FirstOrDefault()?.Text("configurationNamingContext") ?? "";
        var (on, _, _) = await RecycleSettings(ldap, config, stop);
        var o = Noted(row, kind);
        var where = PathIn(Ldap.Parent(row.Dn), ldap.BaseDn, f.Domain!);
        if (on && o.Guid is not null)
        {
            Note(o, new ChangeStep("Location", [where], ["Recycle Bin"], new StepUndo("restore", Rdn: Ldap.Split(row.Dn)[0], Parent: Ldap.Parent(row.Dn))));
        }
        else
        {
            Cannot(o, $"The AD Recycle Bin is not enabled, so the deleted {kind} cannot be restored.", new ChangeStep("Location", [where], ["Deleted permanently"]));
        }
    }

    // --- Putting a change back ---------------------------------------------------------------------------------------

    public async Task<string> Undo(Change change, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        if (change.Steps.Any(s => s.Undo?.Kind == "restore"))
        {
            return await UndoDelete(ldap, f, change, stop);
        }
        var o = change.Object;
        string[] wanted = ["objectClass", "cn", "displayName", "sAMAccountName", "objectGUID", "userAccountControl", "pwdLastSet",
            .. change.Steps.Select(s => s.Undo?.Attribute).OfType<string>()];
        var row = await Locate(ldap, o, [.. wanted.Distinct(StringComparer.OrdinalIgnoreCase)], stop)
            ?? throw new ToolProblem($"{o.Name} no longer exists in Active Directory.", StatusCodes.Status404NotFound);
        InScope(row.Dn, f, ldap.BaseDn);
        await Ranked(ldap, row.Dn, stop);
        // Nothing is put back unless every step still holds what the change left.
        foreach (var s in change.Steps)
        {
            if (await Since(ldap, f, row, o.Name, s, stop) is { } changed)
            {
                throw new ToolProblem(changed, StatusCodes.Status409Conflict);
            }
        }
        ChangeNote.Current?.On(Noted(row, o.Kind));
        var dn = row.Dn;
        foreach (var s in Enumerable.Reverse(change.Steps))
        {
            dn = await Put(ldap, f, dn, s.Undo!, stop);
            ChangeNote.Current?.Step(new ChangeStep(s.Label, s.After, s.Before));
        }
        if (!string.Equals(dn, row.Dn, StringComparison.OrdinalIgnoreCase))
        {
            ChangeNote.Current?.At(dn, change.Steps.FirstOrDefault(s => s.Undo?.Kind == "place" && s.Label == "Name")?.Before.FirstOrDefault());
        }
        return Undid(change);
    }

    /// <summary>The object by its GUID when the change has one (it may have moved), else by where it was left.</summary>
    private static async Task<LdapRow?> Locate(ILdap ldap, ChangedObject o, string[] attributes, CancellationToken stop)
    {
        if (o.Guid is { } g && Guid.TryParse(g, out var id))
        {
            return (await ldap.Find(ldap.BaseDn, $"(objectGUID={GuidFilter(id)})", true, attributes, 1, stop)).FirstOrDefault();
        }
        return (await ldap.Find(Inside(o.Dn, ldap.BaseDn), "(objectClass=*)", false, attributes, 1, stop)).FirstOrDefault();
    }

    private static string GuidFilter(Guid id) => string.Concat(id.ToByteArray().Select(b => $"\\{b:x2}"));

    private static async Task<bool> IsMember(ILdap ldap, string group, string member, CancellationToken stop) =>
        (await ldap.Find(group, $"(member={ActiveDirectory.Escape(member)})", false, ["cn"], 1, stop)).Count > 0;

    private static bool SameValues(string attribute, IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        if (attribute.Equals("accountExpires", StringComparison.OrdinalIgnoreCase))
        {
            var (x, y) = (a.FirstOrDefault(), b.FirstOrDefault());
            return (Never(x) && Never(y)) || x == y;
        }
        return a.Count == b.Count && a.All(v => b.Contains(v, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>What changed on the object since the change, in a sentence; null when it's as the change left it.</summary>
    private static async Task<string?> Since(ILdap ldap, DirectoryToolsFile f, LdapRow row, string name, ChangeStep s, CancellationToken stop)
    {
        var u = s.Undo ?? throw new ToolProblem("This change cannot be undone.");
        var flags = row.Number("userAccountControl") ?? 0;
        switch (u.Kind)
        {
            case "set":
            {
                var now = row.Texts(u.Attribute!).ToList();
                if (SameValues(u.Attribute!, now, u.Now ?? []))
                {
                    return null;
                }
                return now.Count == 0 ? $"{name}'s {s.Label} was cleared since."
                    : $"{name}'s {s.Label} was changed again since. Undo would overwrite {string.Join(", ", now.Select(v => ShowValue(u.Attribute!, v)))}.";
            }
            case "bit":
                return ((flags & u.Bit) != 0) != u.On ? null
                    : u.Bit == Disabled ? $"{name} was {(u.On ? "disabled" : "enabled")} again since."
                    : $"{name}'s {s.Label} option was changed again since.";
            case "mustchange":
                return (row.Number("pwdLastSet") == 0) != u.On ? null : $"{name}'s {s.Label} option was changed again since.";
            case "cannotchange":
            {
                var dacl = await ldap.ReadDacl(row.Dn, stop) ?? throw new ToolProblem($"Access denied. {f.Account} cannot read the permissions on {name}.");
                return Dacl.CannotChangePassword(dacl) != u.On ? null : $"{name}'s {s.Label} option was changed again since.";
            }
            case "groups":
                foreach (var g in u.Remove ?? [])
                {
                    await ChangeableGroup(ldap, f, g, stop);
                    if (!await IsMember(ldap, g, row.Dn, stop))
                    {
                        return $"{name} was removed from {Ldap.Name(g)} since.";
                    }
                }
                foreach (var g in u.Add ?? [])
                {
                    await ChangeableGroup(ldap, f, g, stop);
                    if (await IsMember(ldap, g, row.Dn, stop))
                    {
                        return $"{name} was added to {Ldap.Name(g)} again since.";
                    }
                }
                return null;
            case "members":
                foreach (var m in u.Remove ?? [])
                {
                    if (!await IsMember(ldap, row.Dn, m, stop))
                    {
                        return $"{Ldap.Name(m)} was removed from {name} since.";
                    }
                }
                foreach (var m in u.Add ?? [])
                {
                    if (await IsMember(ldap, row.Dn, m, stop))
                    {
                        return $"{Ldap.Name(m)} was added to {name} again since.";
                    }
                }
                return null;
            case "place":
                if (!string.Equals(row.Dn, u.Now?.FirstOrDefault(), StringComparison.OrdinalIgnoreCase))
                {
                    return $"{name} was moved or renamed again since. It is now in {PathIn(Ldap.Parent(row.Dn), ldap.BaseDn, f.Domain!)}.";
                }
                InScope(u.Parent!, f, ldap.BaseDn);
                return null;
            default:
                throw new ToolProblem("This change cannot be undone.");
        }
    }

    /// <summary>One step put back; where the object is afterwards.</summary>
    private static async Task<string> Put(ILdap ldap, DirectoryToolsFile f, string dn, StepUndo u, CancellationToken stop)
    {
        switch (u.Kind)
        {
            case "set":
                if ((u.Was ?? []).Count > 0)
                {
                    await Change(ldap, dn, [new(LdapChange.Replace, u.Attribute!, [.. u.Was!])], f, password: false, stop);
                }
                else if ((u.Now ?? []).Count > 0)
                {
                    await Change(ldap, dn, [new(LdapChange.Delete, u.Attribute!)], f, password: false, stop);
                }
                return dn;
            case "bit":
            {
                var flags = (await ldap.Find(dn, "(objectClass=*)", false, ["userAccountControl"], 1, stop)).FirstOrDefault()?.Number("userAccountControl") ?? NormalAccount;
                flags = u.On ? flags | (long) u.Bit : flags & ~(long) u.Bit;
                await Change(ldap, dn, [new(LdapChange.Replace, "userAccountControl", flags.ToString(CultureInfo.InvariantCulture))], f, password: false, stop);
                return dn;
            }
            case "mustchange":
                await Change(ldap, dn, [new(LdapChange.Replace, "pwdLastSet", u.On ? "0" : "-1")], f, password: false, stop);
                return dn;
            case "cannotchange":
            {
                var dacl = await ldap.ReadDacl(dn, stop) ?? throw new ToolProblem($"Access denied. {f.Account} cannot read the permissions on {Ldap.Name(dn)}.");
                try
                {
                    await ldap.WriteDacl(dn, Dacl.SetCannotChangePassword(dacl, u.On), stop);
                }
                catch (LdapRefused e)
                {
                    throw new ToolProblem(e.Code == LdapRefused.InsufficientRights ? $"Access denied. {f.Account} does not have permission to change the permissions on {Ldap.Name(dn)}." : Said(e, f.Account!));
                }
                return dn;
            }
            case "groups":
            case "members":
            {
                var problems = new List<string>();
                foreach (var (list, add) in new[] { (u.Add ?? [], true), (u.Remove ?? [], false) })
                {
                    foreach (var x in list)
                    {
                        var (group, member) = u.Kind == "groups" ? (x, dn) : (dn, x);
                        if ((await SetMember(ldap, group, member, add, stop)).Refused is { } refused)
                        {
                            problems.Add(refused.Code == LdapRefused.InsufficientRights ? $"{f.Account} does not have permission to change {Ldap.Name(group)}" : $"{Ldap.Name(x)}: {refused.Message}");
                        }
                    }
                }
                return problems.Count == 0 ? dn : throw new ToolProblem($"Could not undo the whole change: {string.Join("; ", problems)}.");
            }
            case "place":
                await Relocate(ldap, f, dn, u.Rdn!, u.Parent!, stop);
                return $"{u.Rdn},{u.Parent}";
            default:
                throw new ToolProblem("This change cannot be undone.");
        }
    }

    /// <summary>A delete put back: the object from the Recycle Bin by its GUID, where it was.</summary>
    private async Task<string> UndoDelete(ILdap ldap, DirectoryToolsFile f, Change change, CancellationToken stop)
    {
        var o = change.Object;
        if (o.Guid is not { } g || !Guid.TryParse(g, out var id))
        {
            throw new ToolProblem($"{o.Name} cannot be found in the Recycle Bin.", StatusCodes.Status404NotFound);
        }
        var filter = $"(objectGUID={GuidFilter(id)})";
        var row = (await ldap.FindDeleted($"CN=Deleted Objects,{ldap.BaseDn}", $"(&(isDeleted=TRUE)(!(isRecycled=TRUE)){filter})", true, RestoreAttributes, 1, stop)).FirstOrDefault();
        if (row is null)
        {
            throw new ToolProblem((await ldap.Find(ldap.BaseDn, filter, true, ["cn"], 1, stop)).Count > 0
                ? $"{o.Name} was already restored from the Recycle Bin." : $"{o.Name} is no longer in the Recycle Bin.", StatusCodes.Status409Conflict);
        }
        var back = await RestoreRow(ldap, f, row, stop);
        Note(o with { Dn = back }, new ChangeStep("Location", ["Recycle Bin"], [PathIn(Ldap.Parent(back), ldap.BaseDn, f.Domain!)]));
        return $"{o.Name} was restored from the Recycle Bin.";
    }

    /// <summary>What an undo did, as the toast says it.</summary>
    private static string Undid(Change c)
    {
        var name = c.Object.Name;
        static string List(IEnumerable<string> dns, string noun)
        {
            var names = dns.Select(Ldap.Name).ToList();
            return names.Count <= 3 ? string.Join(", ", names) : $"{string.Join(", ", names.Take(2))} and {names.Count - 2} other {noun}";
        }
        if (c.Steps.Any(s => s.Undo?.Kind == "place" && s.Label == "Name"))
        {
            return $"{name} was renamed back to {c.Steps.First(s => s.Label == "Name").Before.FirstOrDefault()}.";
        }
        if (c.Steps.Count != 1)
        {
            return $"{c.Steps.Count} attributes of {name} were set back.";
        }
        var s = c.Steps[0];
        var u = s.Undo!;
        switch (u.Kind)
        {
            case "groups":
            {
                var back = (u.Add ?? []).Count > 0 ? $"added back to {List(u.Add!, "groups")}" : null;
                var again = (u.Remove ?? []).Count > 0 ? $"removed from {List(u.Remove!, "groups")} again" : null;
                return $"{name} was {string.Join(" and ", new[] { back, again }.OfType<string>())}.";
            }
            case "members":
            {
                var parts = new List<string>();
                if ((u.Add ?? []).Count > 0)
                {
                    parts.Add($"{List(u.Add!, "members")} {(u.Add!.Count == 1 ? "was" : "were")} added back to {name}.");
                }
                if ((u.Remove ?? []).Count > 0)
                {
                    parts.Add($"{List(u.Remove!, "members")} {(u.Remove!.Count == 1 ? "was" : "were")} removed from {name} again.");
                }
                return string.Join(" ", parts);
            }
            case "place":
                return $"{name} was moved back to {s.Before.FirstOrDefault()}.";
            case "bit" when u.Bit == Disabled:
                return $"{name} was {(u.On ? "disabled" : "enabled")} again.";
            case "bit" or "mustchange" or "cannotchange":
                return $"{name}'s {s.Label} option was set back to {s.Before.FirstOrDefault()}.";
            default:
                return s.Before.Count == 0 ? $"{name}'s {s.Label} was cleared." : $"{name}'s {s.Label} was set back to {string.Join(", ", s.Before)}.";
        }
    }
}
