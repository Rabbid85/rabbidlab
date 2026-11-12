using System.Runtime.InteropServices;
using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>
/// The user tools: Active Directory accounts, made and changed as Rabbid Lab's own AD account (DirectoryTools.cs), and
/// Windows accounts on this server or on a computer in Seats, which its client changes when the server asks
/// (ClientCommands.cs). Each change takes its permission and goes in the activity log; passwords never do.
/// </summary>
public static class UsersApi
{
    /// <summary>The id the page uses for this server among the computers.</summary>
    public const string Server = "server";

    public static void AddUserTools(this IServiceCollection services)
    {
        services.AddSingleton<DirectoryTools>();
        services.AddSingleton<IDirectoryTools>(s => s.GetRequiredService<DirectoryTools>());
        services.AddSingleton<IDirectoryExtras>(s => s.GetRequiredService<DirectoryTools>());
        services.AddSingleton<ClientCommands>();
    }

    public static void MapUsersApi(this WebApplication app)
    {
        // --- Active Directory ----------------------------------------------------------------------------------------

        app.MapGet("/api/ad", (HttpContext context, IDirectoryTools tools) =>
            !Access.Can(context, Permissions.AdView) && !Access.Can(context, Permissions.AdConnect) ? Access.NotAllowed()
            : Results.Ok(new { domain = tools.Domain, state = tools.State() }));

        // Setting up, step one: the OUs to pick from, read with the admin sign-in given (which isn't kept).
        app.MapPost("/api/ad/look", (SetupForm form, HttpContext context, IDirectoryTools tools) => Try(context, Permissions.AdConnect, async () =>
        {
            var look = await tools.Look(form.Domain, form.Username ?? "", form.Password ?? "", context.RequestAborted);
            return Results.Ok(look);
        }));

        app.MapPost("/api/ad/connect", (SetupForm form, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdConnect, async () =>
        {
            var warning = await tools.Connect(form, ActivityLog.Who(context), context.RequestAborted);
            var state = tools.State()!;
            log.Record(context, "users", "ad.connect", form.Existing ? $"Set up {Product.Title}'s AD account as {state.Account}"
                : $"Made {state.Account} in {state.Domain} for {Product.Title}, able to change accounts in {state.ScopeName}", warning is null ? "ok" : "warning", warning);
            return Results.Ok(new { state, warning });
        }));

        app.MapPost("/api/ad/disconnect", (HttpContext context, IDirectoryTools tools, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AdConnect) is { } refused)
            {
                return refused;
            }
            var state = tools.State();
            tools.Disconnect();
            if (state is not null)
            {
                log.Record(context, "users", "ad.disconnect", $"Stopped using {state.Account} for Active Directory");
            }
            return Results.Ok(new { account = state?.Account, made = state?.Made == true });
        });

        app.MapPost("/api/ad/settings", (AdSettingsForm form, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdConnect, async () =>
        {
            await tools.SetNewUsers(form.NewUsers ?? "", context.RequestAborted);
            var state = tools.State()!;
            log.Record(context, "users", "ad.settings", $"New AD accounts go in {state.NewUsersName}");
            return Results.Ok(state);
        }));

        app.MapGet("/api/ad/places", (HttpContext context, IDirectoryTools tools) => Try(context, Permissions.AdView, async () =>
            Results.Ok(await tools.Places(context.RequestAborted)), change: false));

        app.MapGet("/api/ad/users", (HttpContext context, IDirectoryTools tools) => Try(context, Permissions.AdView, async () =>
        {
            var (users, more) = await tools.Users(context.RequestAborted);
            return Results.Ok(new { users, more });
        }, change: false));

        app.MapGet("/api/ad/users/{username}", (string username, HttpContext context, IDirectoryTools tools) => Try(context, Permissions.AdView, async () =>
            Results.Ok(await tools.User(username, context.RequestAborted)), change: false));

        // A plain list, as the page takes it; the X-More header says when Active Directory had more than it sent.
        app.MapGet("/api/ad/groups", (string? q, HttpContext context, IDirectoryTools tools) => Try(context, Permissions.AdView, async () =>
        {
            var (groups, more) = await tools.Groups(q, context.RequestAborted);
            More(context, more);
            return Results.Ok(groups);
        }, change: false));

        app.MapPost("/api/ad/users", (NewAdUser form, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdCreate, async () =>
        {
            var password = string.IsNullOrEmpty(form.Password) ? Passwords.Make() : form.Password;
            var (user, warning) = await tools.Create(form, password, context.RequestAborted);
            log.Record(context, "users", "ad.create", $"Made the AD account {user.Username} in {(user.Where.Length > 0 ? user.Where : tools.State()?.Domain)}",
                warning is null ? "ok" : "warning", warning);
            return Results.Ok(new { user, warning, password = string.IsNullOrEmpty(form.Password) ? password : null });
        }));

        app.MapPost("/api/ad/users/{username}/password", (string username, PasswordReset form, HttpContext context, IDirectoryTools tools, ActivityLog log) =>
            Try(context, Permissions.AdChange, async () =>
            {
                var password = string.IsNullOrEmpty(form.Password) ? Passwords.Make() : form.Password;
                await tools.Password(username, password, form.MustChange, context.RequestAborted);
                log.Record(context, "users", "ad.password", $"Set a new password for {username}" + (form.MustChange ? ", to be changed at their next sign-in" : ""));
                return Results.Ok(new { password = string.IsNullOrEmpty(form.Password) ? password : null });
            }));

        app.MapPost("/api/ad/users/{username}/unlock", (string username, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdChange, async () =>
        {
            await tools.Unlock(username, context.RequestAborted);
            log.Record(context, "users", "ad.unlock", $"Unlocked {username}");
            return Results.Ok();
        }));

        app.MapPost("/api/ad/users/{username}/enable", (string username, OnForm form, HttpContext context, IDirectoryTools tools, ActivityLog log) =>
            Try(context, Permissions.AdChange, async () =>
            {
                await tools.Enable(username, form.On, context.RequestAborted);
                log.Record(context, "users", form.On ? "ad.enable" : "ad.disable", form.On ? $"Turned {username} back on" : $"Turned {username} off");
                return Results.Ok();
            }));

        app.MapPost("/api/ad/users/{username}/groups", (string username, GroupsForm form, HttpContext context, IDirectoryTools tools, ActivityLog log) =>
            Try(context, Permissions.AdChange, async () =>
            {
                var add = form.Add ?? [];
                var remove = form.Remove ?? [];
                var warning = await tools.ChangeGroups(username, add, remove, context.RequestAborted);
                log.Record(context, "users", "ad.groups", $"Changed {username}'s groups" + Groups(add, remove, Ldap.Name), warning is null ? "ok" : "warning", warning);
                return Results.Ok(new { warning });
            }));

        app.MapPost("/api/ad/users/{username}/delete", (string username, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdDelete, async () =>
        {
            await tools.Delete(username, context.RequestAborted);
            log.Record(context, "users", "ad.delete", $"Deleted the AD account {username}");
            return Results.Ok();
        }));

        // --- Active Directory groups and computers -------------------------------------------------------------------

        app.MapGet("/api/ad/groups/members", (string dn, HttpContext context, IDirectoryTools tools) => Try(context, Permissions.AdView, async () =>
        {
            var (members, more) = await tools.Members(dn, context.RequestAborted);
            More(context, more);
            return Results.Ok(members);
        }, change: false));

        // People, groups and computers to put in a group.
        app.MapGet("/api/ad/find", (string? q, HttpContext context, IDirectoryTools tools) => Try(context, Permissions.AdView, async () =>
            Results.Ok(await tools.Find(q, context.RequestAborted)), change: false));

        app.MapPost("/api/ad/groups", (NewAdGroup form, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdCreate, async () =>
        {
            var group = await tools.CreateGroup(form, context.RequestAborted);
            log.Record(context, "users", "ad.group.create", $"Made the AD group {group.Name}");
            return Results.Ok(group);
        }));

        app.MapPost("/api/ad/groups/members", (GroupMembersForm form, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdChange, async () =>
        {
            var add = form.Add ?? [];
            var remove = form.Remove ?? [];
            var warning = await tools.ChangeMembers(form.Dn ?? "", add, remove, context.RequestAborted);
            log.Record(context, "users", "ad.group.members", $"Changed who's in {Ldap.Name(form.Dn ?? "")}" + Groups(add, remove, Ldap.Name).Replace(": into ", ": added ").Replace("out of", "took out"),
                warning is null ? "ok" : "warning", warning);
            return Results.Ok(new { warning });
        }));

        app.MapPost("/api/ad/groups/delete", (GroupMembersForm form, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdDelete, async () =>
        {
            await tools.DeleteGroup(form.Dn ?? "", context.RequestAborted);
            log.Record(context, "users", "ad.group.delete", $"Deleted the AD group {Ldap.Name(form.Dn ?? "")}");
            return Results.Ok();
        }));

        app.MapGet("/api/ad/computers", (HttpContext context, IDirectoryTools tools) => Try(context, Permissions.AdView, async () =>
        {
            var (computers, more) = await tools.Computers(context.RequestAborted);
            return Results.Ok(new { computers, more });
        }, change: false));

        app.MapPost("/api/ad/computers/{name}/enable", (string name, OnForm form, HttpContext context, IDirectoryTools tools, ActivityLog log) =>
            Try(context, Permissions.AdChange, async () =>
            {
                await tools.EnableComputer(name, form.On, context.RequestAborted);
                log.Record(context, "users", form.On ? "ad.computer.enable" : "ad.computer.disable", form.On ? $"Turned the computer {name} back on in AD" : $"Turned the computer {name} off in AD");
                return Results.Ok();
            }));

        app.MapPost("/api/ad/computers/{name}/delete", (string name, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdDelete, async () =>
        {
            await tools.DeleteComputer(name, context.RequestAborted);
            log.Record(context, "users", "ad.computer.delete", $"Deleted the computer {name} from AD");
            return Results.Ok();
        }));

        // --- Windows accounts, on this server or a computer in Seats -------------------------------------------------

        app.MapGet("/api/windows/computers", (HttpContext context, Clients clients, ClientCommands commands) =>
        {
            if (!Access.Can(context, Permissions.WindowsView))
            {
                return Access.NotAllowed();
            }
            var here = Normal(ActivityLog.Address(context));
            var list = new List<object>();
            if (OperatingSystem.IsWindows())
            {
                list.Add(new { id = Server, name = Environment.MachineName, server = true, os = RuntimeInformation.OSDescription, online = true, ready = true, why = (string?) null,
                    user = (string?) null, here = here is "127.0.0.1" or "::1", lastSeen = (DateTime?) null });
            }
            foreach (var c in clients.All().Where(c => c.Approved is not null))
            {
                var why = Why(c, clients, commands);
                list.Add(new { id = c.Id, name = c.Computer, server = false, os = c.Os, online = clients.IsOnline(c), ready = why is null, why, user = c.User,
                    here = here is not null && Normal(c.Address) == here, lastSeen = (DateTime?) c.LastSeen });
            }
            return Results.Ok(list);
        });

        app.MapGet("/api/windows/{id}/accounts", (string id, HttpContext context, Clients clients, ClientCommands commands, IWindowsAccounts windows) =>
            Run(context, id, Permissions.WindowsView, new AccountOrder { Action = AccountOrder.List }, clients, commands, windows, (_, answer) =>
                Results.Ok(new { accounts = answer.Accounts ?? [], groups = answer.Groups ?? [] })));

        app.MapPost("/api/windows/{id}/accounts", (string id, WindowsAccountForm form, HttpContext context, Clients clients, ClientCommands commands, IWindowsAccounts windows,
            ActivityLog log) =>
        {
            if (AccountOrder.BadName(form.Name) is { } bad)
            {
                return Task.FromResult(Results.BadRequest(new { error = bad }));
            }
            var password = string.IsNullOrEmpty(form.Password) ? Passwords.Make() : form.Password;
            var order = new AccountOrder
            {
                Action = AccountOrder.Create, Name = form.Name!.Trim(), FullName = form.FullName, Description = form.Description, NewPassword = password,
                MustChange = form.MustChange, NeverExpires = form.NeverExpires, Add = form.Groups ?? [],
            };
            return Run(context, id, Permissions.WindowsCreate, order, clients, commands, windows, (computer, answer) =>
            {
                log.Record(context, "users", "windows.create", $"Made the Windows account {order.Name} on {computer}" + Groups(order.Add, [], g => g),
                    answer.Warning is null ? "ok" : "warning", answer.Warning);
                return Results.Ok(new { warning = answer.Warning, password = string.IsNullOrEmpty(form.Password) ? password : null });
            });
        });

        app.MapPost("/api/windows/{id}/accounts/{name}/password", (string id, string name, PasswordReset form, HttpContext context, Clients clients, ClientCommands commands,
            IWindowsAccounts windows, ActivityLog log) =>
        {
            var password = string.IsNullOrEmpty(form.Password) ? Passwords.Make() : form.Password;
            return Run(context, id, Permissions.WindowsChange, new AccountOrder { Action = AccountOrder.Password, Name = name, NewPassword = password, MustChange = form.MustChange },
                clients, commands, windows, (computer, _) =>
                {
                    log.Record(context, "users", "windows.password", $"Set a new password for {name} on {computer}" + (form.MustChange ? ", to be changed at its next sign-in" : ""));
                    return Results.Ok(new { password = string.IsNullOrEmpty(form.Password) ? password : null });
                });
        });

        app.MapPost("/api/windows/{id}/accounts/{name}/enable", (string id, string name, OnForm form, HttpContext context, Clients clients, ClientCommands commands,
            IWindowsAccounts windows, ActivityLog log) =>
            Run(context, id, Permissions.WindowsChange, new AccountOrder { Action = form.On ? AccountOrder.Enable : AccountOrder.Disable, Name = name },
                clients, commands, windows, (computer, _) =>
                {
                    log.Record(context, "users", form.On ? "windows.enable" : "windows.disable", form.On ? $"Turned {name} back on on {computer}" : $"Turned {name} off on {computer}");
                    return Results.Ok();
                }));

        app.MapPost("/api/windows/{id}/accounts/{name}/groups", (string id, string name, GroupsForm form, HttpContext context, Clients clients, ClientCommands commands,
            IWindowsAccounts windows, ActivityLog log) =>
            Run(context, id, Permissions.WindowsChange, new AccountOrder { Action = AccountOrder.Groups, Name = name, Add = form.Add ?? [], Remove = form.Remove ?? [] },
                clients, commands, windows, (computer, answer) =>
                {
                    // Some groups may have changed and others not: logged either way, with what didn't work.
                    log.Record(context, "users", "windows.groups", $"Changed {name}'s groups on {computer}" + Groups(form.Add ?? [], form.Remove ?? [], g => g),
                        answer.Warning is null ? "ok" : "warning", answer.Warning);
                    return Results.Ok(new { warning = answer.Warning });
                }));

        app.MapPost("/api/windows/{id}/accounts/{name}/delete", (string id, string name, HttpContext context, Clients clients, ClientCommands commands,
            IWindowsAccounts windows, ActivityLog log) =>
            Run(context, id, Permissions.WindowsDelete, new AccountOrder { Action = AccountOrder.Delete, Name = name }, clients, commands, windows, (computer, _) =>
            {
                log.Record(context, "users", "windows.delete", $"Deleted the Windows account {name} on {computer}");
                return Results.Ok();
            }));
    }

    /// <summary>The header a plain list carries when Active Directory had more than it sent.</summary>
    public const string MoreHeader = "X-More";

    internal static void More(HttpContext context, bool more)
    {
        if (more)
        {
            context.Response.Headers[MoreHeader] = "true";
        }
    }

    /// <summary>Allowed, then done, with what went wrong said for the page. A change must also come from Rabbid Lab's own page.</summary>
    internal static async Task<IResult> Try(HttpContext context, string permission, Func<Task<IResult>> work, bool change = true)
    {
        if ((change ? Access.RefuseUnless(context, permission) : Access.Can(context, permission) ? null : Access.NotAllowed()) is { } refused)
        {
            return refused;
        }
        try
        {
            return await work();
        }
        catch (ToolProblem e)
        {
            return Results.Json(new { error = e.Message }, statusCode: e.Status);
        }
        catch (DirectoryProblem e)
        {
            return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (LdapRefused e)
        {
            return Results.Json(new { error = $"Active Directory said: {e.Message}." }, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>An order for a computer's accounts: run here for this server, or sent to the computer's client.</summary>
    private static async Task<IResult> Run(HttpContext context, string id, string permission, AccountOrder order, Clients clients, ClientCommands commands,
        IWindowsAccounts windows, Func<string, AccountAnswer, IResult> done)
    {
        if ((order.Action == AccountOrder.List ? (Access.Can(context, permission) ? null : Access.NotAllowed()) : Access.RefuseUnless(context, permission)) is { } refused)
        {
            return refused;
        }
        string computer;
        AccountAnswer? answer;
        if (id == Server)
        {
            computer = Environment.MachineName;
            answer = await windows.Do(order, context.RequestAborted);
        }
        else
        {
            if (clients.Find(id) is not { Approved: not null } c)
            {
                return Results.NotFound(new { error = "That computer isn't one of the seats any more." });
            }
            computer = c.Computer;
            if (Why(c, clients, commands) is { } why)
            {
                return Results.Json(new { error = $"{c.Computer}: {why}" }, statusCode: StatusCodes.Status409Conflict);
            }
            answer = await commands.Send(c.Id, order, context.RequestAborted);
            if (answer is null)
            {
                return Results.Json(new { error = $"{c.Computer} didn't answer in time. It may have just gone offline; try again in a minute." }, statusCode: StatusCodes.Status504GatewayTimeout);
            }
        }
        if (!answer.Ok)
        {
            return Results.BadRequest(new { error = answer.Error ?? $"{computer} couldn't do that." });
        }
        return done(computer, answer);
    }

    /// <summary>Why the computer can't take account changes now, or null when it can.</summary>
    private static string? Why(Client c, Clients clients, ClientCommands commands)
    {
        if (!clients.IsOnline(c))
        {
            return "It's offline.";
        }
        if (Version.TryParse(c.Version, out var v) && v < new Version(0, 4, 0))
        {
            return $"Its client is {c.Version}, which can't change accounts. It updates itself to {Product.Version} within a few minutes.";
        }
        return commands.Listening(c.Id) ? null : "Its client isn't listening for changes yet. It starts within a minute.";
    }

    private static string Groups(IReadOnlyList<string> add, IReadOnlyList<string> remove, Func<string, string> name) =>
        (add.Count > 0 ? $": into {string.Join(", ", add.Select(name))}" : "") + (remove.Count > 0 ? $"{(add.Count > 0 ? ";" : ":")} out of {string.Join(", ", remove.Select(name))}" : "");

    private static string? Normal(string? address) => Addresses.Normal(address);
}

public sealed record AdSettingsForm(string? NewUsers);

public sealed record PasswordReset(string? Password, bool MustChange);

public sealed record OnForm(bool On);

public sealed record GroupsForm(List<string>? Add, List<string>? Remove);

public sealed record GroupMembersForm(string? Dn, List<string>? Add, List<string>? Remove);

public sealed record WindowsAccountForm(string? Name, string? FullName, string? Description, string? Password, bool MustChange, bool NeverExpires, List<string>? Groups);
