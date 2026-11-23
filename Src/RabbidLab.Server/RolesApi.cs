namespace RabbidLab.Server;

/// <summary>
/// Roles and people: the roles with their tiers, what they allow and who has them (each assignment with where it works,
/// until when and why), everyone who has signed in, and Rabbid Lab's own accounts. Seeing it takes See roles; each change
/// takes the permission for it, and a role can only be changed or given by someone in a higher tier (Roles.cs).
/// </summary>
public static class RolesApi
{
    public static void MapRolesApi(this WebApplication app)
    {
        app.MapGet("/api/roles", (HttpContext context, Roles roles, Accounts accounts, Mfa mfa) =>
        {
            if (!Access.Can(context, Permissions.AccessView))
            {
                return Access.NotAllowed();
            }
            var me = Who.Of(context.User);
            var f = roles.Snapshot();
            var mine = roles.Of(me);
            var managed = roles.Managed(me);
            return Results.Ok(new
            {
                catalog = Permissions.Shown(),
                scoped = Permissions.Scoped,
                roles = f.Roles.Select(r => new
                {
                    id = r.Id,
                    name = r.Name,
                    about = r.About,
                    color = r.Color,
                    tier = r.Tier,
                    permissions = r.Id == Roles.Admin ? [.. Permissions.All] : r.Permissions,
                    scope = r.Scope.Select(d => new { dn = d, path = Place(d) }),
                    mfa = r.Mfa,
                    members = r.Members.Select(m => new { kind = m.Kind, name = m.Name, display = m.Display, added = m.Added, addedBy = m.AddedBy,
                        scope = m.Scope.Select(d => new { dn = d, path = Place(d) }), ends = m.Ends, reason = m.Reason }),
                    builtIn = r.Id is Roles.Admin or Roles.User,
                    manage = managed.Contains(r.Id),
                }),
                people = f.People.OrderByDescending(p => p.LastSignIn).Select(p => new
                {
                    source = p.Source,
                    username = p.Username,
                    name = p.Name,
                    domain = p.Domain,
                    groups = p.Groups,
                    lastSignIn = p.LastSignIn,
                    mfa = mfa.Since(p.Source, p.Username),
                }),
                local = accounts.List().Select(a => new { username = a.Username, created = a.Created }),
                domain = accounts.Directory?.Domain,
                machine = Environment.MachineName,
                windowsAdmins = OperatingSystem.IsWindows(),
                me = new { username = me.Username, source = me.Source, roles = mine.Select(r => r.Id), tier = mine.Select(r => r.Tier).DefaultIfEmpty(2).Min(), can = roles.Allowed(me).Order() },
            });
        });

        app.MapPost("/api/roles", (RoleForm form, HttpContext context, Roles roles, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AccessRoles) is { } refused)
            {
                return refused;
            }
            var (role, problem) = roles.Create(form.Name, form.About, form.Color, form.Copy, Who.Of(context.User), form.Tier);
            if (role is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "access", "role.add", $"Created the role {role.Name} in tier {role.Tier}");
            return Results.Ok(new { id = role.Id });
        });

        app.MapPost("/api/roles/{id}", (string id, RoleForm form, HttpContext context, Roles roles, Confirmations confirm, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AccessRoles) is { } refused)
            {
                return refused;
            }
            var before = roles.Find(id);
            // Giving a role more takes Confirm it's you, a higher tier included; a name, a colour or taking something away doesn't.
            if (before is not null && ((form.Permissions ?? []).Except(before.Permissions).Any() || form.Tier < before.Tier) && confirm.Refuse(context) is { } unconfirmed)
            {
                return unconfirmed;
            }
            if (roles.Change(id, form.Name, form.About, form.Color, form.Permissions, Who.Of(context.User), form.Scope, form.Tier) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            var after = roles.Find(id) ?? before!;
            log.Record(context, "access", "role.change", $"Changed the role {after.Name}", detail: Changes(before!, after));
            return Results.Ok();
        });

        // MFA is required for everyone since 1.1.296, so there's no per-role switch any more; a page from before gets told so.
        app.MapPost("/api/roles/{id}/mfa", (string id, HttpContext context) =>
            Access.RefuseUnless(context, Permissions.AccessRoles) ?? Results.BadRequest(new { error = "MFA is required for everyone. Roles no longer have their own MFA setting." }));

        app.MapPost("/api/roles/{id}/delete", (string id, HttpContext context, Roles roles, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AccessRoles) is { } refused)
            {
                return refused;
            }
            var name = roles.Find(id)?.Name;
            if (roles.Delete(id, Who.Of(context.User)) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "access", "role.delete", $"Deleted the role {name}");
            return Results.Ok();
        });

        // A group or person from Active Directory, a local account, or everyone in Active Directory, into a role.
        app.MapPost("/api/roles/{id}/members", async (string id, MemberForm form, HttpContext context, Roles roles, Accounts accounts, IDirectory directory,
            IDomainFinder finder, Confirmations confirm, ActivityLog log, CancellationToken stop) =>
        {
            // Putting someone in a role gives them what it allows, so it takes Confirm it's you. Everyone has User anyway.
            if ((Access.RefuseUnless(context, Permissions.AccessMembers) ?? (id == Roles.User ? null : confirm.Refuse(context))) is { } refused)
            {
                return refused;
            }
            var name = form.Name?.Trim() ?? "";
            // A group or person that isn't in the domain would do nothing, so they're looked up first when the domain lets this computer look.
            if (form.Kind is Member.Group or Member.Person)
            {
                if (accounts.Directory is not { } d)
                {
                    return Results.BadRequest(new { error = "Active Directory is not configured. Add local accounts instead." });
                }
                try
                {
                    var found = await directory.Search(d.Domain, await finder.Host(d.Domain, d.Server, stop), name, stop);
                    if (found is not null && !found.Entries.Any(e => e.Kind == form.Kind && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return Results.BadRequest(new { error = $"The {(form.Kind == Member.Group ? "group" : "account")} {name} was not found in {d.Domain}." });
                    }
                }
                catch (DirectoryProblem)
                {
                    // Can't look now: taken as typed.
                }
            }
            if (form.Kind == Member.Everyone && accounts.Directory is null)
            {
                return Results.BadRequest(new { error = "Active Directory is not configured." });
            }
            if (roles.AddMember(id, form.Kind, name, form.Display, Who.Of(context.User), form.Scope, form.Ends, form.Reason) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "access", "role.member.add", $"Gave {RoleName(roles, id)} to {Said(form)}", detail: Terms(form));
            return Results.Ok();
        });

        // Where an assignment works, when it ends and why. Widening it takes Confirm it's you, as giving it did.
        app.MapPost("/api/roles/{id}/members/edit", (string id, MemberForm form, HttpContext context, Roles roles, Confirmations confirm, ActivityLog log) =>
        {
            if ((Access.RefuseUnless(context, Permissions.AccessMembers) ?? (id == Roles.User ? null : confirm.Refuse(context))) is { } refused)
            {
                return refused;
            }
            if (roles.EditMember(id, form.Kind, form.Name, Who.Of(context.User), form.Scope, form.Ends, form.Reason) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "access", "role.member.change", $"Changed {Said(form)}'s {RoleName(roles, id)} assignment", detail: Terms(form));
            return Results.Ok();
        });

        app.MapPost("/api/roles/{id}/members/remove", (string id, MemberForm form, HttpContext context, Roles roles, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AccessMembers) is { } refused)
            {
                return refused;
            }
            if (roles.RemoveMember(id, form.Kind, form.Name, Who.Of(context.User)) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "access", "role.member.remove", $"Removed {Said(form)} from {RoleName(roles, id)}");
            return Results.Ok();
        });

        // For Add people: groups and people in Active Directory, local accounts, and everyone, matching what's typed.
        app.MapGet("/api/roles/search", async (string? q, HttpContext context, Accounts accounts, IDirectory directory, IDomainFinder finder, CancellationToken stop) =>
        {
            if (!Access.Can(context, Permissions.AccessMembers))
            {
                return Access.NotAllowed();
            }
            var text = q?.Trim() ?? "";
            List<object> entries = [];
            var more = false;
            string? problem = null;
            if (accounts.Directory is { } d)
            {
                if (text.Length == 0 || "everyone".StartsWith(text, StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(new { kind = Member.Everyone, name = "ad", display = "Everyone in Active Directory", description = $"Any account that can sign in to {d.Domain} or a trusted domain" });
                }
                try
                {
                    var found = await directory.Search(d.Domain, await finder.Host(d.Domain, d.Server, stop), text, stop);
                    if (found is null)
                    {
                        problem = $"This computer cannot search {d.Domain}. Enter the exact name of the group or account.";
                    }
                    else
                    {
                        entries.AddRange(found.Entries.Take(40).Select(e => new { kind = e.Kind, name = e.Name, display = e.Display, description = e.Description }));
                        more = found.More || found.Entries.Count > 40;
                    }
                }
                catch (DirectoryProblem e)
                {
                    problem = e.Message;
                }
            }
            entries.AddRange(accounts.List().Where(a => a.Username.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Select(a => new { kind = Member.Local, name = a.Username, display = (string?) null, description = (string?) "Local account" }));
            return Results.Ok(new { entries, more, problem, domain = accounts.Directory?.Domain });
        });

        // Rabbid Lab's own accounts: everyone has User, and they can go in other roles too.
        app.MapPost("/api/accounts", (AccountForm form, HttpContext context, Accounts accounts, Roles roles, Confirmations confirm, ActivityLog log) =>
        {
            if ((Access.RefuseUnless(context, Permissions.AccessLocal) ?? confirm.Refuse(context)) is { } refused)
            {
                return refused;
            }
            var by = Who.Of(context.User);
            var wanted = form.Roles ?? [];
            if (wanted.Count > 0 && !Access.Can(context, Permissions.AccessMembers))
            {
                return Results.BadRequest(new { error = "Your roles do not allow you to assign roles. Create the account without roles." });
            }
            var managed = roles.Managed(by);
            if (wanted.FirstOrDefault(id => !managed.Contains(id)) is { } beyond)
            {
                return Results.BadRequest(new { error = $"You cannot add members to {roles.Find(beyond)?.Name ?? "that role"}." });
            }
            if (accounts.Create(form.Username, form.Password) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            var username = form.Username!.Trim();
            foreach (var id in wanted.Where(id => id != Roles.User))
            {
                roles.AddMember(id, Member.Local, username, null, by);
            }
            log.Record(context, "access", "account.add", $"Added the local account {username}");
            return Results.Ok();
        });

        app.MapPost("/api/accounts/{username}/password", (string username, PasswordForm form, HttpContext context, Accounts accounts, Roles roles, Confirmations confirm, SignInLocks locks, ActivityLog log) =>
        {
            if ((Access.RefuseUnless(context, Permissions.AccessLocal) ?? confirm.Refuse(context)) is { } refused)
            {
                return refused;
            }
            // Anyone's own password, and otherwise only accounts in roles they manage, so nobody takes over an account above them.
            if (!Self(context, username) && roles.Refuses(username, Who.Of(context.User)) is { } above)
            {
                return Results.BadRequest(new { error = above });
            }
            if (accounts.SetPassword(username, form.Password) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            locks.ForgetBrowsers(SignInLocks.Key(SignIn.Local, username)); // the browsers remembered for it are strangers again
            log.Record(context, "access", "account.password", $"Set a new password for {username}");
            return Results.Ok();
        });

        app.MapPost("/api/accounts/{username}/remove", (string username, HttpContext context, Accounts accounts, Roles roles, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AccessLocal) is { } refused)
            {
                return refused;
            }
            if (Self(context, username))
            {
                return Results.BadRequest(new { error = "You cannot remove the account you are signed in with." });
            }
            if (roles.Forget(username, Who.Of(context.User), () => accounts.Remove(username)) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "access", "account.remove", $"Removed the local account {username}");
            return Results.Ok();
        });
    }

    private static string Said(MemberForm form) => Roles.Words(form.Kind, form.Name?.Trim(), form.Display?.Trim());

    /// <summary>An assignment's terms in words, for the activity log: where, until when, and why.</summary>
    private static string Terms(MemberForm form) => string.Join(" ", new[]
    {
        form.Scope is { Count: > 0 } s ? $"Where: {string.Join(", ", s.Select(Place))}." : "Where: everywhere its role reaches.",
        form.Ends is { } e ? $"Ends: {e:yyyy-MM-dd}." : "Ends: never.",
        form.Reason is { Length: > 0 } r ? $"Reason: {r.Trim()}" : "",
    }.Where(x => x.Length > 0));

    private static string RoleName(Roles roles, string id) => roles.Find(id)?.Name ?? "a role";

    /// <summary>Whether the local account named is the one this request is signed in with.</summary>
    private static bool Self(HttpContext context, string username) =>
        context.User.FindFirst("source")?.Value == SignIn.Local && string.Equals(username, context.User.Identity?.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>An OU as people read it (Lab / People), or the domain's name for the domain itself.</summary>
    private static string Place(string dn)
    {
        var parts = Ldap.Split(dn);
        var domain = parts.Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)).ToList();
        return Ldap.Path(dn, string.Join(',', domain)) is { Length: > 0 } path ? path : string.Join('.', domain.Select(p => p[3..]));
    }

    /// <summary>What changed about a role, in words, for the activity log.</summary>
    private static string? Changes(Role before, Role after)
    {
        List<string> said = [];
        if (before.Name != after.Name)
        {
            said.Add($"Renamed from {before.Name}.");
        }
        if (before.Tier != after.Tier)
        {
            said.Add($"Moved from tier {before.Tier} to tier {after.Tier}.");
        }
        static string Names(IEnumerable<string> keys) => string.Join(", ", keys.Select(Roles.PermissionName));
        var added = after.Permissions.Except(before.Permissions).ToList();
        var removed = before.Permissions.Except(after.Permissions).ToList();
        if (added.Count > 0)
        {
            said.Add($"Permissions added: {Names(added)}.");
        }
        if (removed.Count > 0)
        {
            said.Add($"Permissions removed: {Names(removed)}.");
        }
        if (before.Scope.Count != after.Scope.Count || before.Scope.Except(after.Scope, StringComparer.OrdinalIgnoreCase).Any())
        {
            said.Add(after.Scope.Count == 0 ? "Scope: all of Active Directory." : $"Scope: {string.Join(", ", after.Scope.Select(Place))}.");
        }
        if (before.Color != after.Color || before.About != after.About)
        {
            said.Add("Colour or description changed.");
        }
        return said.Count > 0 ? string.Join(" ", said) : null;
    }
}

public sealed record RoleForm(string? Name, string? About, string? Color, string? Copy = null, List<string>? Permissions = null, List<string>? Scope = null, int? Tier = null);

/// <summary>An assignment: who, and for giving or changing it, where (OUs, none for everywhere), until when, and why.</summary>
public sealed record MemberForm(string? Kind, string? Name, string? Display = null, List<string>? Scope = null, DateTime? Ends = null, string? Reason = null);

public sealed record AccountForm(string? Username, string? Password, List<string>? Roles);

public sealed record PasswordForm(string? Password);
