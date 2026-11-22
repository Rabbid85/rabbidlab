using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>
/// The user tools: Active Directory accounts, made and changed as Rabbid Lab's own AD account (DirectoryTools.cs), and
/// Windows accounts on this server or on a computer in Devices, which its client changes when the server asks
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
        services.AddSingleton<IDirectoryActions>(s => s.GetRequiredService<DirectoryTools>());
        services.AddSingleton<IGroupPolicy>(s => s.GetRequiredService<DirectoryTools>());
        services.AddSingleton<GpmcSetup>();
        services.AddSingleton<IPrintServerProbe>(_ => OperatingSystem.IsWindows() ? new WindowsPrintServerProbe() : new NoPrintServerProbe());
        services.AddSingleton<PrintServers>();
        services.AddSingleton<IPrinterSnmp, PrinterSnmp>();
        services.AddSingleton<PrinterDevices>();
        services.AddSingleton<ClientCommands>();
        services.AddSingleton<IAdminGroups, AdminGroups>();
    }

    public static void MapUsersApi(this WebApplication app)
    {
        // --- Active Directory ----------------------------------------------------------------------------------------

        // scoped: this person's roles only show part of Active Directory, so the pages about the whole domain aren't theirs.
        // reading and canRead: until the service account is set up, read-only as this computer, which only people who can
        // set up the account may turn on, and only they and roles allowed to see it while read-only may see.
        app.MapGet("/api/ad", (HttpContext context, IDirectoryTools tools, Roles roles) =>
        {
            if (!Access.Can(context, Permissions.AdView) && !Access.Can(context, Permissions.AdConnect))
            {
                return Access.NotAllowed();
            }
            var state = tools.State();
            var admin = state is null && Access.Can(context, Permissions.AdConnect);
            var reader = admin || (state is null && Access.Can(context, Permissions.AdReadOnly));
            return Results.Ok(new { domain = tools.Domain, state, reading = reader ? tools.Reading() : null, canRead = admin && tools.CanReadHere,
                scoped = Access.Can(context, Permissions.AdView) && roles.Reach(Who.Of(context.User), Permissions.AdView) is not null });
        });

        // Not now on the setup card: read-only as this computer for the whole server, confirmed and logged. Off again in Settings.
        app.MapPost("/api/ad/read-only", (ReadOnlyForm form, HttpContext context, IDirectoryTools tools, Confirmations confirm, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AdConnect) is { } refused)
            {
                return refused;
            }
            if (form.On && confirm.Refuse(context) is { } unconfirmed)
            {
                return unconfirmed;
            }
            if (form.On && !tools.CanReadHere)
            {
                return Results.BadRequest(new { error = "This server cannot read Active Directory with its computer account. Configure the service account instead." });
            }
            tools.ReadHere(form.On, ActivityLog.Who(context));
            log.Record(context, "users", form.On ? "ad.readonly.on" : "ad.readonly.off", form.On
                ? $"Enabled read-only Active Directory access as {Environment.MachineName} for admins who can configure the service account"
                : "Disabled read-only Active Directory access");
            return Results.Ok(new { });
        });

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
            log.Record(context, "users", "ad.connect", form.Existing ? $"Configured the AD service account as {state.Account}"
                : $"Created the service account {state.Account} in {state.Domain} with delegated permissions on {state.ScopeName}", warning is null ? "ok" : "warning", warning);
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
                log.Record(context, "users", "ad.disconnect", $"Disconnected the AD service account {state.Account}");
            }
            return Results.Ok(new { account = state?.Account, made = state?.Made == true });
        });

        app.MapPost("/api/ad/settings", (AdSettingsForm form, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdConnect, async () =>
        {
            // One at a time, as each is changed: only the ones given are set.
            foreach (var (kind, dn) in new[] { ("users", form.NewUsers), ("groups", form.NewGroups), ("computers", form.NewComputers) }.Where(p => p.Item2 is not null))
            {
                await tools.SetNewPlace(kind, dn!, context.RequestAborted);
            }
            var state = tools.State()!;
            log.Record(context, "users", "ad.settings", form.NewGroups is not null ? $"Set the default OU for new AD groups to {state.NewGroupsName}"
                : form.NewComputers is not null ? $"Set the default OU for new AD computers to {state.NewComputersName}" : $"Set the default OU for new AD users to {state.NewUsersName}");
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
            log.Record(context, "users", "ad.create", $"Created the AD user {user.Username} in {(user.Where.Length > 0 ? user.Where : tools.State()?.Domain)}",
                warning is null ? "ok" : "warning", warning);
            return Results.Ok(new { user, warning, password = string.IsNullOrEmpty(form.Password) ? password : null });
        }, confirm: true));

        app.MapPost("/api/ad/users/{username}/password", (string username, PasswordReset form, HttpContext context, IDirectoryTools tools, ActivityLog log) =>
            Try(context, Permissions.AdPassword, async () =>
            {
                var password = string.IsNullOrEmpty(form.Password) ? Passwords.Make() : form.Password;
                await tools.Password(username, password, form.MustChange, context.RequestAborted);
                log.Record(context, "users", "ad.password", $"Reset the password for {username}" + (form.MustChange ? " (must change at next sign-in)" : ""));
                return Results.Ok(new { password = string.IsNullOrEmpty(form.Password) ? password : null });
            }, confirm: true));

        app.MapPost("/api/ad/users/{username}/unlock", (string username, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdPassword, async () =>
        {
            await tools.Unlock(username, context.RequestAborted);
            log.Record(context, "users", "ad.unlock", $"Unlocked {username}");
            return Results.Ok();
        }));

        app.MapPost("/api/ad/users/{username}/enable", (string username, OnForm form, HttpContext context, IDirectoryTools tools, ActivityLog log) =>
            Try(context, Permissions.AdEnable, async () =>
            {
                await tools.Enable(username, form.On, context.RequestAborted);
                log.Record(context, "users", form.On ? "ad.enable" : "ad.disable", form.On ? $"Enabled {username}" : $"Disabled {username}");
                return Results.Ok();
            }));

        app.MapPost("/api/ad/users/{username}/groups", (string username, GroupsForm form, HttpContext context, IDirectoryTools tools, ActivityLog log) =>
            Try(context, Permissions.AdGroups, async () =>
            {
                var add = form.Add ?? [];
                var remove = form.Remove ?? [];
                var warning = await tools.ChangeGroups(username, add, remove, context.RequestAborted);
                log.Record(context, "users", "ad.groups", $"Changed group membership for {username}" + Groups(add, remove, Ldap.Name), warning is null ? "ok" : "warning", warning);
                return Results.Ok(new { warning });
            }, confirm: true));

        app.MapPost("/api/ad/users/{username}/delete", (string username, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdDelete, async () =>
        {
            await tools.Delete(username, context.RequestAborted);
            log.Record(context, "users", "ad.delete", $"Deleted the AD user {username}");
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
            log.Record(context, "users", "ad.group.create", $"Created the AD group {group.Name}");
            return Results.Ok(group);
        }));

        app.MapPost("/api/ad/groups/members", (GroupMembersForm form, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdGroups, async () =>
        {
            var add = form.Add ?? [];
            var remove = form.Remove ?? [];
            var warning = await tools.ChangeMembers(form.Dn ?? "", add, remove, context.RequestAborted);
            log.Record(context, "users", "ad.group.members", $"Changed members of {Ldap.Name(form.Dn ?? "")}" + Groups(add, remove, Ldap.Name).Replace(": added to ", ": added ").Replace("removed from", "removed"),
                warning is null ? "ok" : "warning", warning);
            return Results.Ok(new { warning });
        }, confirm: true));

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
            Try(context, Permissions.AdEnable, async () =>
            {
                await tools.EnableComputer(name, form.On, context.RequestAborted);
                log.Record(context, "users", form.On ? "ad.computer.enable" : "ad.computer.disable", form.On ? $"Enabled the computer {name} in AD" : $"Disabled the computer {name} in AD");
                return Results.Ok();
            }));

        app.MapPost("/api/ad/computers/{name}/delete", (string name, HttpContext context, IDirectoryTools tools, ActivityLog log) => Try(context, Permissions.AdDelete, async () =>
        {
            await tools.DeleteComputer(name, context.RequestAborted);
            log.Record(context, "users", "ad.computer.delete", $"Deleted the computer {name} from AD");
            return Results.Ok();
        }));

        // --- Windows accounts, on this server or a computer in Devices -------------------------------------------------

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
                list.Add(new { id = Server, name = Environment.MachineName, server = true, os = WindowsVersion.Name(), online = true, ready = true, why = (string?) null,
                    user = (string?) null, here = here is "127.0.0.1" or "::1", lastSeen = (DateTime?) null, version = (string?) Product.Version });
            }
            foreach (var c in clients.All().Where(c => c.Approved is not null))
            {
                var why = Why(c, clients, commands);
                list.Add(new { id = c.Id, name = c.Computer, server = false, os = c.Os, online = clients.IsOnline(c), ready = why is null, why, user = c.User,
                    here = here is not null && Normal(c.Address) == here, lastSeen = (DateTime?) c.LastSeen, version = c.Version });
            }
            return Results.Ok(list);
        });

        // A computer's accounts and its Administrators group; the group is kept with its inventory for Local admins.
        app.MapGet("/api/windows/{id}/accounts", (string id, HttpContext context, Clients clients, ClientCommands commands, IWindowsAccounts windows, Inventories inventories) =>
            Run(context, id, Permissions.WindowsView, new AccountOrder { Action = AccountOrder.List }, clients, commands, windows, (_, answer) =>
            {
                if (id != Server && answer.Admins is { } admins)
                {
                    inventories.SetAdmins(id, admins, answer.Accounts);
                }
                return Results.Ok(new { accounts = answer.Accounts ?? [], groups = answer.Groups ?? [], admins = answer.Admins });
            }));

        // Who's in Administrators: names in (DOMAIN\name, or a local account), SIDs out.
        app.MapPost("/api/windows/{id}/admins", async (string id, AdminsForm form, HttpContext context, Clients clients, ClientCommands commands, IWindowsAccounts windows,
            Inventories inventories, ActivityLog log) =>
        {
            var add = Clean(form.Add);
            var remove = Clean(form.Remove);
            if (add.Count + remove.Count is 0 or > MaxAdminChanges)
            {
                return Results.BadRequest(new { error = $"Add or remove between 1 and {MaxAdminChanges} members at a time." });
            }
            if (add.FirstOrDefault(n => n.Length > 256 || n.Any(char.IsControl) || n.Count(c => c == '\\') > 1 || n.StartsWith('\\') || n.EndsWith('\\')) is { } bad)
            {
                return Results.BadRequest(new { error = $"{bad} is not a valid account name. Use DOMAIN\\name for a domain user or group, or the name of a local account." });
            }
            if (remove.Any(s => !SidShape.IsMatch(s)))
            {
                return Results.BadRequest(new { error = "Specify the members to remove by SID." });
            }
            if (id != Server && clients.Find(id) is { } c && Version.TryParse(c.Version, out var v) && v < AdminsFrom)
            {
                return Results.Json(new { error = $"The client on {c.Computer} is version {c.Version}, which cannot change the Administrators group. It updates to {Product.Version} automatically within a few minutes." },
                    statusCode: StatusCodes.Status409Conflict);
            }
            // Who's being taken out, by name, for the activity log.
            var before = id == Server ? (await windows.ReadAdmins(context.RequestAborted))?.Admins : inventories.Get(id)?.Admins;
            var gone = remove.Select(s => before?.FirstOrDefault(m => string.Equals(m.Sid, s, StringComparison.OrdinalIgnoreCase))?.Name ?? s).ToList();
            return await Run(context, id, Permissions.WindowsChange, new AccountOrder { Action = AccountOrder.Admins, Add = add, Remove = remove }, clients, commands, windows,
                (computer, answer) =>
                {
                    if (id != Server && answer.Admins is { } admins)
                    {
                        inventories.SetAdmins(id, admins, null);
                    }
                    var said = string.Join("; ", new[] { add.Count > 0 ? $"added {string.Join(", ", add)}" : null, gone.Count > 0 ? $"removed {string.Join(", ", gone)}" : null }.Where(x => x is not null));
                    log.Record(context, "users", "windows.admins", $"Changed Administrators group membership on {computer}: {said}", answer.Warning is null ? "ok" : "warning", answer.Warning);
                    return Results.Ok(new { warning = answer.Warning, admins = answer.Admins });
                }, confirm: true);
        });

        // Local admins on Devices: each computer's Administrators group as last read (this server's, read now), and the
        // people in the domain groups among them. Groups is null when Active Directory can't be read from here.
        app.MapGet("/api/devices/admins", async (HttpContext context, Clients clients, Inventories inventories, IWindowsAccounts windows, IAdminGroups groups, TimeProvider clock) =>
        {
            if (!Access.Can(context, Permissions.WindowsView))
            {
                return Access.NotAllowed();
            }
            var stop = context.RequestAborted;
            var computers = new List<object>();
            var all = new List<WindowsMember>();
            if (await windows.ReadAdmins(stop) is { } mine)
            {
                computers.Add(new { id = Server, computer = Environment.MachineName, server = true, online = true, version = Product.Version, @checked = (DateTime?) clock.GetUtcNow().UtcDateTime,
                    admins = (List<WindowsMember>?) mine.Admins, locals = (List<WindowsAccount>?) mine.Locals });
                all.AddRange(mine.Admins);
            }
            foreach (var c in clients.All().Where(c => c.Approved is not null))
            {
                var inventory = inventories.Get(c.Id);
                computers.Add(new { id = c.Id, computer = c.Computer, server = false, online = clients.IsOnline(c), version = c.Version, @checked = inventory?.AdminsChecked,
                    admins = inventory?.Admins, locals = inventory?.Locals });
                all.AddRange(inventory?.Admins ?? []);
            }
            var sids = all.Where(m => m.Kind == "group" && m.Sid is not null).Select(m => m.Sid!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var people = sids.Count == 0 ? new Dictionary<string, AdminGroup>() : await groups.People(sids, stop);
            return Results.Ok(new { computers, groups = people });
        });

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
                log.Record(context, "users", "windows.create", $"Created the local account {order.Name} on {computer}" + Groups(order.Add, [], g => g),
                    answer.Warning is null ? "ok" : "warning", answer.Warning);
                return Results.Ok(new { warning = answer.Warning, password = string.IsNullOrEmpty(form.Password) ? password : null });
            }, confirm: true);
        });

        app.MapPost("/api/windows/{id}/accounts/{name}/password", (string id, string name, PasswordReset form, HttpContext context, Clients clients, ClientCommands commands,
            IWindowsAccounts windows, ActivityLog log) =>
        {
            var password = string.IsNullOrEmpty(form.Password) ? Passwords.Make() : form.Password;
            return Run(context, id, Permissions.WindowsChange, new AccountOrder { Action = AccountOrder.Password, Name = name, NewPassword = password, MustChange = form.MustChange },
                clients, commands, windows, (computer, _) =>
                {
                    log.Record(context, "users", "windows.password", $"Reset the password for {name} on {computer}" + (form.MustChange ? " (must change at next sign-in)" : ""));
                    return Results.Ok(new { password = string.IsNullOrEmpty(form.Password) ? password : null });
                }, confirm: true);
        });

        app.MapPost("/api/windows/{id}/accounts/{name}/enable", (string id, string name, OnForm form, HttpContext context, Clients clients, ClientCommands commands,
            IWindowsAccounts windows, ActivityLog log) =>
            Run(context, id, Permissions.WindowsChange, new AccountOrder { Action = form.On ? AccountOrder.Enable : AccountOrder.Disable, Name = name },
                clients, commands, windows, (computer, _) =>
                {
                    log.Record(context, "users", form.On ? "windows.enable" : "windows.disable", form.On ? $"Enabled {name} on {computer}" : $"Disabled {name} on {computer}");
                    return Results.Ok();
                }));

        app.MapPost("/api/windows/{id}/accounts/{name}/groups", (string id, string name, GroupsForm form, HttpContext context, Clients clients, ClientCommands commands,
            IWindowsAccounts windows, ActivityLog log) =>
            Run(context, id, Permissions.WindowsChange, new AccountOrder { Action = AccountOrder.Groups, Name = name, Add = form.Add ?? [], Remove = form.Remove ?? [] },
                clients, commands, windows, (computer, answer) =>
                {
                    // Some groups may have changed and others not: logged either way, with what didn't work.
                    log.Record(context, "users", "windows.groups", $"Changed group membership for {name} on {computer}" + Groups(form.Add ?? [], form.Remove ?? [], g => g),
                        answer.Warning is null ? "ok" : "warning", answer.Warning);
                    return Results.Ok(new { warning = answer.Warning });
                }, confirm: true));

        app.MapPost("/api/windows/{id}/accounts/{name}/delete", (string id, string name, HttpContext context, Clients clients, ClientCommands commands,
            IWindowsAccounts windows, ActivityLog log) =>
            Run(context, id, Permissions.WindowsDelete, new AccountOrder { Action = AccountOrder.Delete, Name = name }, clients, commands, windows, (computer, _) =>
            {
                log.Record(context, "users", "windows.delete", $"Deleted the local account {name} on {computer}");
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

    /// <summary>
    /// Allowed, then done, with what went wrong said for the page. A change must also come from Rabbid Lab's own page.
    /// The tools are told who's asking (Caller), so they stay inside the scope of that person's roles; a page about the
    /// whole domain (whole) is only for roles that cover all of it.
    /// </summary>
    internal static async Task<IResult> Try(HttpContext context, string permission, Func<Task<IResult>> work, bool change = true, bool whole = false, bool confirm = false)
    {
        if ((change ? Access.RefuseUnless(context, permission) : Access.Can(context, permission) ? null : Access.NotAllowed()) is { } refused)
        {
            return refused;
        }
        // Passwords, new accounts and group changes take Confirm it's you, so a session someone else got hold of can't take over accounts.
        if (confirm && context.RequestServices.GetRequiredService<Confirmations>().Refuse(context) is { } unconfirmed)
        {
            return unconfirmed;
        }
        var caller = new Caller(Who.Of(context.User), permission, context.RequestServices.GetRequiredService<Roles>());
        if (whole && Permissions.Scoped.Contains(permission) && caller.Reach() is not null)
        {
            return Results.Json(new { error = "Access denied. Your roles cover only part of Active Directory, and this action requires a role that covers the entire directory." }, statusCode: StatusCodes.Status403Forbidden);
        }
        Caller.Set(caller);
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
            return Results.Json(new { error = $"Active Directory returned an error: {e.Message}." }, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>An order for a computer's accounts: run here for this server, or sent to the computer's client.</summary>
    private static async Task<IResult> Run(HttpContext context, string id, string permission, AccountOrder order, Clients clients, ClientCommands commands,
        IWindowsAccounts windows, Func<string, AccountAnswer, IResult> done, bool confirm = false)
    {
        if ((order.Action == AccountOrder.List ? (Access.Can(context, permission) ? null : Access.NotAllowed()) : Access.RefuseUnless(context, permission)) is { } refused)
        {
            return refused;
        }
        if (confirm && context.RequestServices.GetRequiredService<Confirmations>().Refuse(context) is { } unconfirmed)
        {
            return unconfirmed;
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
                return Results.NotFound(new { error = "The computer no longer exists in Devices." });
            }
            computer = c.Computer;
            if (Why(c, clients, commands) is { } why)
            {
                return Results.Json(new { error = $"{c.Computer}: {why}" }, statusCode: StatusCodes.Status409Conflict);
            }
            answer = await commands.Send(c.Id, order, context.RequestAborted);
            if (answer is null)
            {
                return Results.Json(new { error = $"{c.Computer} did not respond in time. It may have gone offline. Try again in a minute." }, statusCode: StatusCodes.Status504GatewayTimeout);
            }
        }
        if (!answer.Ok)
        {
            return Results.BadRequest(new { error = answer.Error ?? $"{computer} could not complete the operation." });
        }
        return done(computer, answer);
    }

    /// <summary>Why the computer can't take account changes now, or null when it can.</summary>
    private static string? Why(Client c, Clients clients, ClientCommands commands)
    {
        if (!clients.IsOnline(c))
        {
            return "The computer is offline.";
        }
        if (Version.TryParse(c.Version, out var v) && v < new Version(0, 4, 0))
        {
            return $"The client is version {c.Version}, which cannot manage local accounts. It updates to {Product.Version} automatically within a few minutes.";
        }
        return commands.Listening(c.Id) ? null : "The client is not ready to receive changes yet. Try again in a minute.";
    }

    /// <summary>The first client that reads and changes Administrators.</summary>
    public static readonly Version AdminsFrom = new(1, 1, 183);

    private const int MaxAdminChanges = 20;
    private static readonly System.Text.RegularExpressions.Regex SidShape = new(@"^S-1-\d+(-\d+){1,14}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static List<string> Clean(List<string>? list) =>
        [.. (list ?? []).Select(n => n?.Trim() ?? "").Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string Groups(IReadOnlyList<string> add, IReadOnlyList<string> remove, Func<string, string> name) =>
        (add.Count > 0 ? $": added to {string.Join(", ", add.Select(name))}" : "") + (remove.Count > 0 ? $"{(add.Count > 0 ? ";" : ":")} removed from {string.Join(", ", remove.Select(name))}" : "");

    private static string? Normal(string? address) => Addresses.Normal(address);
}

public sealed record AdSettingsForm(string? NewUsers, string? NewGroups = null, string? NewComputers = null);

public sealed record PasswordReset(string? Password, bool MustChange);

public sealed record OnForm(bool On);

public sealed record GroupsForm(List<string>? Add, List<string>? Remove);

public sealed record AdminsForm(List<string>? Add, List<string>? Remove);

public sealed record GroupMembersForm(string? Dn, List<string>? Add, List<string>? Remove);

public sealed record WindowsAccountForm(string? Name, string? FullName, string? Description, string? Password, bool MustChange, bool NeverExpires, List<string>? Groups);
