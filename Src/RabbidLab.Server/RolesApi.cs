namespace RabbidLab.Server;

/// <summary>
/// Roles and people: the roles with what they allow and who's in them, everyone who has signed in, and Rabbid Lab's own
/// accounts. Seeing it takes See roles; each change takes the permission for it, and a role can only be changed by someone
/// above it (Roles.cs).
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
                    permissions = r.Id == Roles.Admin ? [.. Permissions.All] : r.Permissions,
                    scope = r.Scope.Select(d => new { dn = d, path = Place(d) }),
                    mfa = r.Mfa,
                    members = r.Members.Select(m => new { kind = m.Kind, name = m.Name, display = m.Display, added = m.Added, addedBy = m.AddedBy }),
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
                me = new { username = me.Username, source = me.Source, roles = mine.Select(r => r.Id), can = roles.Allowed(me).Order() },
            });
        });

        app.MapPost("/api/roles", (RoleForm form, HttpContext context, Roles roles, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AccessRoles) is { } refused)
            {
                return refused;
            }
            var (role, problem) = roles.Create(form.Name, form.About, form.Color, form.Copy, Who.Of(context.User));
            if (role is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "access", "role.add", $"Made the role {role.Name}");
            return Results.Ok(new { id = role.Id });
        });

        // The order of the roles between Administrator and User, highest first.
        app.MapPost("/api/roles/order", (OrderForm form, HttpContext context, Roles roles, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AccessRoles) is { } refused)
            {
                return refused;
            }
            if (roles.Order(form.Ids ?? [], Who.Of(context.User)) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "access", "role.order", "Changed the order of the roles");
            return Results.Ok();
        });

        app.MapPost("/api/roles/{id}", (string id, RoleForm form, HttpContext context, Roles roles, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AccessRoles) is { } refused)
            {
                return refused;
            }
            var before = roles.Find(id);
            if (roles.Change(id, form.Name, form.About, form.Color, form.Permissions, Who.Of(context.User), form.Scope) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            var after = roles.Find(id) ?? before!;
            log.Record(context, "access", "role.change", $"Changed the role {after.Name}", detail: Changes(before!, after));
            return Results.Ok();
        });

        // Whether everyone in the role signs in with a code as well. Saved as it's switched, Administrator's too.
        app.MapPost("/api/roles/{id}/mfa", (string id, OnForm form, HttpContext context, Roles roles, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AccessRoles) is { } refused)
            {
                return refused;
            }
            if (roles.RequireMfa(id, form.On, Who.Of(context.User)) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "access", form.On ? "role.mfa.on" : "role.mfa.off", form.On ? $"Made MFA required for {RoleName(roles, id)}" : $"Stopped requiring MFA for {RoleName(roles, id)}");
            return Results.Ok();
        });

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
            IDomainFinder finder, ActivityLog log, CancellationToken stop) =>
        {
            if (Access.RefuseUnless(context, Permissions.AccessMembers) is { } refused)
            {
                return refused;
            }
            var name = form.Name?.Trim() ?? "";
            // A group or person that isn't in the domain would do nothing, so they're looked up first when the domain lets this computer look.
            if (form.Kind is Member.Group or Member.Person)
            {
                if (accounts.Directory is not { } d)
                {
                    return Results.BadRequest(new { error = "Active Directory isn't set up here, so add local accounts instead." });
                }
                try
                {
                    var found = await directory.Search(d.Domain, await finder.Host(d.Domain, d.Server, stop), name, stop);
                    if (found is not null && !found.Entries.Any(e => e.Kind == form.Kind && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return Results.BadRequest(new { error = $"There's no {(form.Kind == Member.Group ? "group" : "account")} called {name} in {d.Domain}." });
                    }
                }
                catch (DirectoryProblem)
                {
                    // Can't look now: taken as typed.
                }
            }
            if (form.Kind == Member.Everyone && accounts.Directory is null)
            {
                return Results.BadRequest(new { error = "Active Directory isn't set up here." });
            }
            if (roles.AddMember(id, form.Kind, name, form.Display, Who.Of(context.User)) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "access", "role.member.add", $"Put {Said(form)} in {RoleName(roles, id)}");
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
            log.Record(context, "access", "role.member.remove", $"Took {Said(form)} out of {RoleName(roles, id)}");
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
                    entries.Add(new { kind = Member.Everyone, name = "ad", display = "Everyone in Active Directory", description = $"Anyone who can sign in to {d.Domain} or a domain it trusts" });
                }
                try
                {
                    var found = await directory.Search(d.Domain, await finder.Host(d.Domain, d.Server, stop), text, stop);
                    if (found is null)
                    {
                        problem = $"This computer can't search {d.Domain}, so type the exact name of the group or account.";
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
        app.MapPost("/api/accounts", (AccountForm form, HttpContext context, Accounts accounts, Roles roles, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AccessLocal) is { } refused)
            {
                return refused;
            }
            var by = Who.Of(context.User);
            var wanted = form.Roles ?? [];
            if (wanted.Count > 0 && !Access.Can(context, Permissions.AccessMembers))
            {
                return Results.BadRequest(new { error = "Your roles don't let you put people in roles. Make the account without any." });
            }
            var managed = roles.Managed(by);
            if (wanted.FirstOrDefault(id => !managed.Contains(id)) is { } beyond)
            {
                return Results.BadRequest(new { error = $"You can't put people in {roles.Find(beyond)?.Name ?? "that role"}." });
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

        app.MapPost("/api/accounts/{username}/password", (string username, PasswordForm form, HttpContext context, Accounts accounts, Roles roles, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AccessLocal) is { } refused)
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
                return Results.BadRequest(new { error = "You can't remove the account you're signed in with." });
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
        static string Names(IEnumerable<string> keys) => string.Join(", ", keys.Select(Roles.PermissionName));
        var added = after.Permissions.Except(before.Permissions).ToList();
        var removed = before.Permissions.Except(after.Permissions).ToList();
        if (added.Count > 0)
        {
            said.Add($"Now allows: {Names(added)}.");
        }
        if (removed.Count > 0)
        {
            said.Add($"No longer allows: {Names(removed)}.");
        }
        if (before.Scope.Count != after.Scope.Count || before.Scope.Except(after.Scope, StringComparer.OrdinalIgnoreCase).Any())
        {
            said.Add(after.Scope.Count == 0 ? "Scope: all of Active Directory." : $"Scope: {string.Join(", ", after.Scope.Select(Place))}.");
        }
        if (before.Color != after.Color || before.About != after.About)
        {
            said.Add("New look or description.");
        }
        return said.Count > 0 ? string.Join(" ", said) : null;
    }
}

public sealed record RoleForm(string? Name, string? About, string? Color, string? Copy = null, List<string>? Permissions = null, List<string>? Scope = null);

public sealed record OrderForm(List<string>? Ids);

public sealed record MemberForm(string? Kind, string? Name, string? Display = null);

public sealed record AccountForm(string? Username, string? Password, List<string>? Roles);

public sealed record PasswordForm(string? Password);
