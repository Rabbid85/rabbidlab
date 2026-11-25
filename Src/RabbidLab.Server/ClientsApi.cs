using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>
/// The Rabbid Lab client's side (joining with the server's address, then checking in with its own id and secret) and
/// the admins' side (the clients, and whether new ones wait to be let in).
/// </summary>
public static class ClientsApi
{
    /// <summary>The scheme a client signs its check-ins with: Authorization: RabbidLab-Client id:secret.</summary>
    public const string Scheme = "RabbidLab-Client";

    public static void MapClientsApi(this WebApplication app)
    {
        app.Lifetime.ApplicationStopping.Register(() => app.Services.GetRequiredService<Clients>().Flush());

        // Anyone can join; new computers wait to be let in. Joins are limited per address so nobody can flood the list.
        app.MapPost("/api/clients/join", (JoinForm form, HttpContext context, Clients clients, JoinLimiter limiter, ActivityLog log, Workflows workflows) =>
        {
            var from = ActivityLog.Address(context);
            if (!limiter.Take(from))
            {
                return Results.Json(new { error = "Too many connection attempts from this address. Wait a few minutes and try again." }, statusCode: StatusCodes.Status429TooManyRequests);
            }
            var computer = form.Computer?.Trim() ?? "";
            if (computer.Length is 0 or > 64)
            {
                limiter.GiveBack(from); // not a join
                return Results.BadRequest(new { error = "Computer name is required." });
            }
            // This server's own client (Install the client here) is approved straight away: it joins from this computer, by its name.
            var self = Addresses.OnServer(context.Connection.RemoteIpAddress) && string.Equals(computer, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
            var (client, secret, problem) = clients.Join(computer, form.Os, form.Version, from, self);
            if (client is null)
            {
                return Results.Json(new { error = problem }, statusCode: StatusCodes.Status429TooManyRequests);
            }
            log.Record($"The client on {computer}", from, "clients", "client.join", client.Approved is null ? $"{computer} connected and is pending approval" : $"{computer} connected", "ok");
            workflows.Happened(Workflows.Approved, client); // only when computers join without approval
            return Results.Ok(new { id = client.Id, secret, approved = client.Approved is not null, version = Product.Version });
        });

        // The answer carries the check for the computer's uninstall code and the days without this server after which the
        // lock lets go, so both work there when this server can't be reached (the client's Removal.cs).
        // The server vouching for the HTTPS key it serves now, signed with its identity key, so a client whose stored
        // fingerprint no longer matches can take the new one without anyone visiting it. The client's own random number
        // goes into the signature, so an old answer can't be played back; the key in it must be the one the client saw.
        app.MapPost("/api/clients/vouch", (VouchForm form, IConfiguration config, ServerIdentity identity) =>
        {
            var nonce = form.Nonce?.Trim() ?? "";
            if (nonce.Length is < 32 or > 128 || ClientFiles.KeyFingerprint(config) is not { } tls)
            {
                return Results.BadRequest(new { error = "No vouch for this request." });
            }
            var (modulus, exponent) = identity.Public();
            return Results.Ok(new { modulus, exponent, tls, sig = identity.Sign(nonce, tls) });
        });

        app.MapPost("/api/clients/checkin", (CheckInForm form, HttpContext context, Clients clients, ActivityLog log, Workflows workflows, ServerIdentity identity, SettingsStore settings, HelpRequests help) =>
        {
            // Away until now: its client had stopped, or it hadn't checked in for a while.
            var away = Credentials(context) is (var asking, _) && clients.Find(asking) is { Approved: not null } known && !clients.IsOnline(known);
            var used = DateTime.TryParse(form.CodeUsed, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at.ToUniversalTime() : (DateTime?) null;
            if (Credentials(context) is not (var id, var secret)
                || clients.CheckIn(id, secret, form.Computer, form.Os, form.Version, form.User, ActivityLog.Address(context), form.Code, used, out var usedNow) is not { } c)
            {
                return Unknown();
            }
            if (usedNow)
            {
                log.Record($"The client on {c.Computer}", ActivityLog.Address(context), "clients", "client.code.used", $"Uninstall code used on {c.Computer}", "ok");
            }
            if (away)
            {
                workflows.CameOnline(c);
            }
            // Errors its client ran into since its last check-in that got through.
            if (form.Problems is { Count: > 0 } problems && c.Approved is not null)
            {
                clients.AddProblems(c.Id, problems);
            }
            // The identity's public half, which the client keeps while it trusts this server (ServerIdentity).
            var (modulus, exponent) = identity.Public();
            return Results.Ok(new
            {
                approved = c.Approved is not null,
                version = Product.Version,
                removalUntil = clients.RemovalUntil(c)?.ToString("o"),
                uninstall = clients.TakeUninstall(c.Id),
                code = c.CodeCheck,
                unlockAfterDays = clients.UnlockAfterDays,
                identity = new { modulus, exponent },
                // The lab's own name, accent and icon (Settings, Appearance), which the client's window wears too; the icon
                // by its hash, fetched from lab-icon when it changes.
                lab = Lab(settings.Current),
                // Ask IT for help: on or off, how to reach IT, pictures of the screens (HelpRequests).
                help = help.ForClient(),
            });
        });

        // The lab's icon for the client's window and tray icon, as Settings, Appearance has it (a PNG or SVG data URI).
        app.MapGet("/api/clients/lab-icon", (HttpContext context, Clients clients, SettingsStore settings) =>
            Caller(context, clients) is null ? Unknown() : Results.Ok(new { icon = settings.Current.LabIcon }));

        // Ask IT for help, from the client on a computer that's been let in (HelpRequests): sent on to the notifications picked.
        app.MapPost("/api/clients/help", async (HelpForm form, HttpContext context, Clients clients, HelpRequests help, ActivityLog log, CancellationToken stop) =>
        {
            if (Caller(context, clients) is not { Approved: not null } c)
            {
                return Unknown();
            }
            var problem = await help.Send(c, form, stop);
            var title = (form.Title ?? "").Trim();
            log.Record($"The client on {c.Computer}", ActivityLog.Address(context), "clients", "client.help",
                $"Help request from {c.Computer}: {(title.Length > 60 ? title[..60] + "…" : title)}", problem is null ? "ok" : "failed", problem);
            return problem is null ? Results.Ok() : Results.Json(new { error = problem }, statusCode: StatusCodes.Status400BadRequest);
        });

        // Settings, Help requests.
        app.MapGet("/api/help-requests", (HttpContext context, HelpRequests help) =>
            Access.Can(context, Permissions.SeatsSettings) ? Results.Ok(help.View()) : Access.NotAllowed());

        app.MapPost("/api/help-requests", (HelpSettingsForm form, HttpContext context, HelpRequests help, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsSettings) is { } refused)
            {
                return refused;
            }
            if (help.Save(form) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "clients", "client.help.settings", "Changed the help request settings");
            return Results.Ok(help.View());
        });

        app.MapPost("/api/help-requests/test", async (HttpContext context, HelpRequests help, CancellationToken stop) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsSettings) is { } refused)
            {
                return refused;
            }
            var problem = await help.Test(context.User.Identity?.Name ?? "Someone", stop);
            return problem is null ? Results.Ok() : Results.BadRequest(new { error = problem });
        });

        // The client's service stopping: the computer shows as Offline at once, so the page open on it offers the client again.
        app.MapPost("/api/clients/bye", (HttpContext context, Clients clients) =>
            Credentials(context) is (var id, var secret) && clients.Stopping(id, secret) ? Results.Ok() : Unknown());

        // The client asking for its next order, held open until there is one (ClientCommands). Only computers let in get any.
        app.MapPost("/api/clients/next", async (HttpContext context, Clients clients, ClientCommands commands, DeviceJobs jobs, IHostApplicationLifetime life) =>
        {
            if (Caller(context, clients) is not { } c)
            {
                return Unknown();
            }
            // Let go when this server stops: a held request would keep it from stopping, for up to half a minute.
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, life.ApplicationStopping);
            if (c.Approved is null)
            {
                try
                {
                    await Task.Delay(ClientCommands.Hold, stop.Token);
                }
                catch (TaskCanceledException)
                {
                    // It went.
                }
                return Results.NoContent();
            }
            // A client that asks with ?jobs knows the order that says jobs wait for it (1.1.168 and later).
            Func<bool>? waiting = context.Request.Query.ContainsKey("jobs") ? () => jobs.Waits(c.Id) : null;
            return await commands.Next(c.Id, stop.Token, waiting, context.Request.IsHttps) is { } order ? Results.Ok(order) : Results.NoContent();
        });

        app.MapPost("/api/clients/done", (AccountAnswer answer, HttpContext context, Clients clients, ClientCommands commands) =>
            Caller(context, clients) is not { } c ? Unknown() : commands.Done(c.Id, answer) ? Results.Ok() : Results.NotFound());

        // The computer this page is open on: whether it's in Devices, and if not, what it needs to get there.
        app.MapGet("/api/clients/here", (HttpContext context, Clients clients, IConfiguration config, INamesCertificate named) =>
        {
            var here = Addresses.Normal(ActivityLog.Address(context));
            // The one checking in, when the address is listed more than once (an old entry left Offline, say).
            var seat = here is null ? null : clients.All().Where(c => Addresses.Normal(c.Address) == here).OrderByDescending(clients.IsOnline).ThenByDescending(c => c.LastSeen).FirstOrDefault();
            return Results.Ok(new
            {
                seat = seat is null ? null : new { id = seat.Id, computer = seat.Computer, approved = seat.Approved is not null, online = clients.IsOnline(seat) },
                thisServer = Addresses.OnServer(context.Connection.RemoteIpAddress), // the page is open on the server itself: nothing to connect
                msi = File.Exists(ClientFiles.Msi),
                server = $"{context.Request.Scheme}://{context.Request.Host}",
                // The key's fingerprint is the one a client keeps. The certificate's, of the one this address shows, is for clients before 1.1.150.
                fingerprint = named.For(context.Request.Host.Host) is { } shown ? ClientFiles.Fingerprint(shown) : ClientFiles.Fingerprint(config),
                key = ClientFiles.KeyFingerprint(config),
                version = Product.Version,
            });
        });

        app.MapGet("/api/clients", (HttpContext context, Clients clients, Inventories inventories) =>
            Access.Can(context, Permissions.SeatsView) ? Results.Ok(clients.All().Select(c => new
            {
                id = c.Id,
                computer = c.Computer,
                os = c.Os,
                version = c.Version,
                user = c.User,
                address = Addresses.Normal(c.Address) ?? c.Address,
                joined = c.Joined,
                lastSeen = c.LastSeen,
                approved = c.Approved,
                approvedBy = c.ApprovedBy,
                online = clients.IsOnline(c),
                behind = Behind(c.Version),
                removalUntil = clients.RemovalUntil(c),
                uninstalling = c.Uninstall,
                inventory = Summary(inventories.Get(c.Id)),
                thisServer = string.Equals(c.Computer, Environment.MachineName, StringComparison.OrdinalIgnoreCase), // this server, with the client on it too
                problems = (c.Problems ?? []).Select(p => new { area = p.Area, message = p.Message, first = p.First, at = p.At, count = p.Count }),
            })) : Access.NotAllowed());

        // The computer's errors were looked at: cleared from its list, for whoever may act on computers.
        app.MapPost("/api/clients/{id}/problems/clear", (string id, HttpContext context, Clients clients, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsActions) is { } refused)
            {
                return refused;
            }
            if (clients.ClearProblems(id) is not { } c)
            {
                return Results.NotFound(new { error = "That computer is no longer in Devices." });
            }
            log.Record(context, "clients", "client.problems.clear", $"Cleared the errors on {c.Computer}");
            return Results.Ok();
        });

        // The client's inventory of its computer (Inventory.cs): hardware, drives, software, Windows updates. Kept only for
        // a computer that's been approved.
        app.MapPost("/api/clients/inventory", (DeviceInventory inventory, HttpContext context, Clients clients, Inventories inventories, Workflows workflows) =>
        {
            if (Caller(context, clients) is not { } c)
            {
                return Unknown();
            }
            if (c.Approved is null)
            {
                return Results.Json(new { error = "This computer is pending approval." }, statusCode: StatusCodes.Status403Forbidden);
            }
            var before = inventories.Get(c.Id);
            inventories.Set(c.Id, inventory);
            workflows.InventoryChanged(c, before, inventory);
            return Results.Ok();
        });

        // One computer's whole inventory, for its side panel; null when its client hasn't sent one yet.
        // Its Administrators and accounts only for whoever may see Windows accounts.
        app.MapGet("/api/clients/{id}/inventory", (string id, HttpContext context, Clients clients, Inventories inventories) =>
            !Access.Can(context, Permissions.SeatsView) ? Access.NotAllowed()
            : clients.Find(id) is null ? Results.NotFound(new { error = "That computer is no longer in Devices." })
            : Results.Ok(new { inventory = Access.Can(context, Permissions.WindowsView) ? (object?) inventories.Get(id) : WithoutAccounts(inventories.Get(id)) }));

        // Software across the approved computers: a row for each program and publisher, with the computers that have it.
        app.MapGet("/api/devices/software", (HttpContext context, Clients clients, Inventories inventories) =>
            !Access.Can(context, Permissions.SeatsView) ? Access.NotAllowed() : Results.Ok(clients.All().Where(c => c.Approved is not null)
                .SelectMany(c => (inventories.Get(c.Id)?.Software ?? []).Select(p => (Client: c, Program: p)))
                .GroupBy(x => $"{x.Program.Name}\n{x.Program.Publisher}".ToUpperInvariant())
                .Select(g => new
                {
                    name = g.First().Program.Name,
                    publisher = g.First().Program.Publisher,
                    versions = g.Select(x => x.Program.Version ?? "").Where(v => v.Length > 0).Distinct().OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList(),
                    computers = g.Select(x => new { id = x.Client.Id, computer = x.Client.Computer, version = x.Program.Version, installed = x.Program.Installed })
                        .DistinctBy(x => x.id).OrderBy(x => x.computer, StringComparer.OrdinalIgnoreCase).ToList(),
                })
                .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase)));

        // Printers across the approved computers: a row for each printer (by name and address), with the computers that have it.
        // Drivers is every printer driver each computer has, for Add a printer.
        app.MapGet("/api/devices/printers", (HttpContext context, Clients clients, Inventories inventories) =>
        {
            if (!Access.Can(context, Permissions.SeatsView))
            {
                return Access.NotAllowed();
            }
            var all = clients.All().Where(c => c.Approved is not null).Select(c => (Client: c, Inventory: inventories.Get(c.Id))).ToList();
            return Results.Ok(new
            {
                printers = all.SelectMany(x => (x.Inventory?.Printers ?? []).Select(p => (x.Client, Printer: p)))
                    .GroupBy(x => $"{x.Printer.Name}\n{x.Printer.Address}".ToUpperInvariant())
                    .Select(g => new
                    {
                        name = g.First().Printer.Name,
                        kind = g.First().Printer.Kind,
                        address = g.First().Printer.Address,
                        // The printer's own address where a computer's port or location gives it away (PrinterDevices.cs).
                        ip = g.Select(x => PrinterDevices.IpOf(x.Printer)).FirstOrDefault(a => a is not null),
                        drivers = g.Select(x => x.Printer.Driver ?? "").Where(d => d.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        computers = g.Select(x => new { id = x.Client.Id, computer = x.Client.Computer, driver = x.Printer.Driver, port = x.Printer.Port, offline = x.Printer.Offline, shared = x.Printer.Shared })
                            .DistinctBy(x => x.id).OrderBy(x => x.computer, StringComparer.OrdinalIgnoreCase).ToList(),
                    })
                    .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase).ToList(),
                computers = all.Select(x => new { id = x.Client.Id, computer = x.Client.Computer, read = x.Inventory?.Printers is not null, drivers = x.Inventory?.PrinterDrivers ?? [] }).ToList(),
            });
        });

        // What the printers say about themselves over SNMP: model, serial number, pages printed and supply levels, for each
        // address a computer's printer or a server's queue points at. Kept six hours; again=true asks them again now.
        // The servers' queues only for someone who may see Active Directory, as on the list itself.
        app.MapGet("/api/devices/printers/supplies", async (bool? again, HttpContext context, Clients clients, Inventories inventories, PrintServers servers, PrinterDevices devices) =>
        {
            if (!Access.Can(context, Permissions.SeatsView))
            {
                return Access.NotAllowed();
            }
            var own = clients.All().Where(c => c.Approved is not null).SelectMany(c => inventories.Get(c.Id)?.Printers ?? []).Select(PrinterDevices.IpOf).OfType<string>();
            var queues = Access.Can(context, Permissions.AdView) ? servers.Addresses().Where(PrinterDevices.Private) : [];
            var (said, asking) = await devices.Read(own.Concat(queues), again == true, context.RequestAborted);
            return Results.Ok(new { devices = said, asking });
        });

        // Windows updates across the approved computers: a row for each update, with the computers missing it and the ones that have it.
        app.MapGet("/api/devices/updates", (HttpContext context, Clients clients, Inventories inventories) =>
        {
            if (!Access.Can(context, Permissions.SeatsView))
            {
                return Access.NotAllowed();
            }
            var all = clients.All().Where(c => c.Approved is not null).Select(c => (Client: c, Inventory: inventories.Get(c.Id))).Where(x => x.Inventory is not null).ToList();
            var seen = all.SelectMany(x => x.Inventory!.Missing.Select(u => (x.Client, Update: u, Missing: true)).Concat(x.Inventory.Updates.Select(u => (x.Client, Update: u, Missing: false))));
            return Results.Ok(seen.GroupBy(x => (x.Update.Kb.Length > 0 ? x.Update.Kb : x.Update.Title ?? "").ToUpperInvariant())
                .Select(g => new
                {
                    kb = g.First().Update.Kb,
                    title = g.Select(x => x.Update.Title).FirstOrDefault(t => !string.IsNullOrEmpty(t)),
                    kind = g.Select(x => x.Update.Kind).FirstOrDefault(k => !string.IsNullOrEmpty(k)),
                    missing = g.Where(x => x.Missing).Select(x => new { id = x.Client.Id, computer = x.Client.Computer }).DistinctBy(x => x.id).OrderBy(x => x.computer, StringComparer.OrdinalIgnoreCase).ToList(),
                    installed = g.Where(x => !x.Missing).Select(x => new { id = x.Client.Id, computer = x.Client.Computer, on = x.Update.Installed }).DistinctBy(x => x.id).OrderBy(x => x.computer, StringComparer.OrdinalIgnoreCase).ToList(),
                })
                .OrderByDescending(x => x.missing.Count).ThenByDescending(x => x.kb, StringComparer.OrdinalIgnoreCase));
        });

        app.MapPost("/api/clients/{id}/approve", (string id, HttpContext context, Clients clients, ActivityLog log, Workflows workflows) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsApprove) is { } refused)
            {
                return refused;
            }
            if (clients.Approve(id, context.User.FindFirst("name")?.Value ?? ActivityLog.Who(context)) is not { } c)
            {
                return Results.NotFound(new { error = "That computer is no longer in Devices." });
            }
            log.Record(context, "clients", "client.approve", $"Approved {c.Computer}");
            workflows.Happened(Workflows.Approved, c);
            return Results.Ok();
        });

        app.MapPost("/api/clients/{id}/remove", (string id, HttpContext context, Clients clients, ClientCommands commands, Inventories inventories, DeviceJobs jobs, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsRemove) is { } refused)
            {
                return refused;
            }
            if (clients.Remove(id) is not { } c)
            {
                return Results.NotFound(new { error = "That computer is no longer in Devices." });
            }
            commands.Forget(c.Id);
            inventories.Forget(c.Id);
            jobs.Forget(c.Id);
            log.Record(context, "clients", "client.remove", $"Removed {c.Computer}");
            return Results.Ok();
        });

        // Uninstalling the client is locked on its computer until it's allowed here, for an hour; with now, the client
        // takes itself off at its next check-in, within a minute.
        app.MapPost("/api/clients/{id}/removal", (string id, RemovalForm form, HttpContext context, Clients clients, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsRemove) is { } refused)
            {
                return refused;
            }
            if (clients.AllowRemoval(id, form.Allow, form.Allow && form.Now) is not { } c)
            {
                return Results.NotFound(new { error = "That computer is no longer in Devices." });
            }
            log.Record(context, "clients", form.Allow ? form.Now ? "client.uninstall" : "client.removal.allow" : "client.removal.lock",
                form.Allow ? form.Now ? $"Sent an uninstall command to the client on {c.Computer}" : $"Allowed uninstalling the client on {c.Computer} for one hour" : $"Locked uninstalling the client on {c.Computer}");
            return Results.Ok(new { removalUntil = clients.RemovalUntil(c), uninstalling = c.Uninstall });
        });

        // The computer's uninstall code, for whoever may remove computers. Showing it is in the activity log.
        app.MapPost("/api/clients/{id}/code", (string id, HttpContext context, Clients clients, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsRemove) is { } refused)
            {
                return refused;
            }
            if (clients.Reveal(id) is not var (c, code))
            {
                return Results.NotFound(new { error = "That computer is no longer in Devices." });
            }
            log.Record(context, "clients", "client.code.show", $"Viewed the uninstall code for {c.Computer}");
            return Results.Ok(new { code, held = c.CodeHeld });
        });

        // Add computers: whether new ones wait, this server's public key fingerprint, and which files it has.
        app.MapGet("/api/clients/settings", (HttpContext context, Clients clients, IConfiguration config) =>
        {
            if (!Access.Can(context, Permissions.SeatsAdd))
            {
                return Access.NotAllowed();
            }
            return Results.Ok(new
            {
                approve = clients.Waits,
                unlockAfterDays = clients.UnlockAfterDays,
                fingerprint = ClientFiles.KeyFingerprint(config),
                msi = File.Exists(ClientFiles.Msi),
                policy = File.Exists(ClientFiles.Policy),
                version = Product.Version,
            });
        });

        // Install the client here: this server puts the client on itself, so it's in Devices with everything the others have.
        app.MapPost("/api/clients/install-here", (HttpContext context, Clients clients, IConfiguration config, IHereClient here, ActivityLog log, ILogger<HereClient> logger) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsAdd) is { } refused)
            {
                return refused;
            }
            if (!OperatingSystem.IsWindows() || Product.InContainer)
            {
                return Results.BadRequest(new { error = "The client requires Windows. This server is not running Windows." });
            }
            if (clients.All().Any(c => string.Equals(c.Computer, Environment.MachineName, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.Conflict(new { error = $"The client is already installed on {Environment.MachineName}. It is listed under Devices." });
            }
            if (!File.Exists(ClientFiles.Msi))
            {
                return Results.NotFound(new { error = "The client installer is not on this server. Download it from the Rabbid Lab release on GitHub." });
            }
            // The client talks to this server on the same port the page uses; its key fingerprint makes the name not matter.
            var server = $"https://localhost:{context.Request.Host.Port ?? 443}";
            var who = ActivityLog.Who(context);
            var from = ActivityLog.Address(context);
            log.Record(context, "clients", "client.install-here", $"Started installing the client on this server ({Environment.MachineName})");
            _ = Task.Run(async () =>
            {
                var code = await here.Install(server, ClientFiles.KeyFingerprint(config), CancellationToken.None);
                // 0 is done, 3010 is done and wants a restart.
                var ok = code is 0 or 3010;
                log.Record(who, from, "clients", "client.install-here", ok ? $"Installed the client on this server ({Environment.MachineName})"
                    : $"Could not install the client on {Environment.MachineName}: msiexec {(code is null ? "did not start" : $"exited with code {code}")}. See the install log in the Logs folder of the data folder.", ok ? "ok" : "failed");
                if (!ok)
                {
                    logger.LogWarning("The client didn't install on this server: msiexec ended with {Code}", code);
                }
            });
            return Results.Accepted();
        });

        // The client's MSI, for anyone: computers installing it, and clients updating themselves to this server's version.
        app.MapGet("/api/clients/download", () => File.Exists(ClientFiles.Msi)
            ? Results.File(ClientFiles.Msi, "application/x-msi", $"RabbidLab-Client-{Product.Version}.msi")
            : Results.NotFound(new { error = "The client installer is not on this server. Download it from the Rabbid Lab release on GitHub." }));

        // The Group Policy template, for admins.
        app.MapGet("/api/clients/policy", (HttpContext context) =>
            !Access.Can(context, Permissions.SeatsAdd) ? Access.NotAllowed()
            : File.Exists(ClientFiles.Policy) ? Results.File(ClientFiles.Policy, "application/zip", $"RabbidLab-Client-Policy-{Product.Version}.zip")
            : Results.NotFound());

        app.MapPost("/api/clients/settings", (ClientSettingsForm form, HttpContext context, Clients clients, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsSettings) is { } refused)
            {
                return refused;
            }
            // Each switch sends its own setting, so the other stays as it is.
            if (form.Approve is { } approve)
            {
                clients.SetApprove(approve);
                log.Record(context, "clients", "client.settings", approve ? "Enabled approval for new computers" : "Disabled approval for new computers");
            }
            if (form.UnlockAfterDays is { } days)
            {
                clients.SetUnlockAfter(days);
                log.Record(context, "clients", "client.settings", days > 0 ? $"Set the uninstall lock to release after {days} days offline" : "Set the uninstall lock to never release for offline computers");
            }
            return Results.Ok();
        });
    }

    /// <summary>The lab's look for the client: its name (null for Rabbid Lab's own), accent, and its icon's hash (null for Rabbid Lab's own).</summary>
    private static object Lab(ConsoleSettings s) => new
    {
        name = string.IsNullOrWhiteSpace(s.LabName) ? null : s.LabName,
        accent = s.Accent,
        icon = string.IsNullOrEmpty(s.LabIcon) ? null : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s.LabIcon)))[..16],
    };

    /// <summary>The id and secret the request carries, as Authorization: RabbidLab-Client id:secret.</summary>
    private static (string Id, string Secret)? Credentials(HttpContext context)
    {
        var said = context.Request.Headers.Authorization.ToString();
        var parts = said.StartsWith(Scheme + " ", StringComparison.Ordinal) ? said[(Scheme.Length + 1)..].Split(':', 2) : [];
        return parts.Length == 2 ? (parts[0], parts[1]) : null;
    }

    /// <summary>The client whose id and secret the request carries.</summary>
    internal static Client? Caller(HttpContext context, Clients clients) =>
        Credentials(context) is (var id, var secret) ? clients.Verify(id, secret) : null;

    internal static IResult Unknown() =>
        Results.Json(new { error = $"This computer is not registered with {Product.Title}. The client will reconnect automatically." }, statusCode: StatusCodes.Status401Unauthorized);

    /// <summary>The few facts about a computer the list shows; the rest is asked for when the computer is opened.</summary>
    private static System.Text.Json.Nodes.JsonNode? WithoutAccounts(DeviceInventory? i)
    {
        if (System.Text.Json.JsonSerializer.SerializeToNode(i, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) is not System.Text.Json.Nodes.JsonObject o)
        {
            return null;
        }
        o.Remove("admins");
        o.Remove("locals");
        o.Remove("adminsChecked");
        return o;
    }

    private static object? Summary(DeviceInventory? i) => i is null ? null : new
    {
        manufacturer = i.Manufacturer,
        model = i.Model,
        serial = i.Serial,
        processor = i.Processor,
        memory = i.Memory,
        started = i.Started,
        restartNeeded = i.RestartNeeded,
        // Free space on the Windows drive, as a percentage.
        disk = i.Disks.FirstOrDefault(d => d.Name.StartsWith('C') || d.Name.StartsWith('c')) is { Total: > 0 } c ? (int?) Math.Round(100.0 * c.Free / c.Total) : null,
        // How many updates are missing; null until Windows Update has answered once.
        // Antivirus definitions are listed but not counted: they come several times a day.
        missing = i.MissingChecked is null ? (int?) null : i.Missing.Count(u => !u.Definition),
        // When Windows Update last answered, and the newest update it has installed.
        missingChecked = i.MissingChecked,
        lastUpdate = i.Updates.Where(u => !string.IsNullOrEmpty(u.Installed)).OrderByDescending(u => u.Installed, StringComparer.Ordinal).Select(u => new { kb = u.Kb, installed = u.Installed }).FirstOrDefault(),
        software = i.Software.Count,
        // Its first network card's MAC (wired first), for Wake.
        mac = i.Nics?.FirstOrDefault()?.Mac,
        // Group Policy, for the Group Policy list and for running gpupdate where a GPO applies.
        gp = i.GroupPolicy is not { } gp ? null : new
        {
            refreshed = gp.Computer?.Refreshed,
            took = gp.Computer?.Took,
            som = gp.Computer?.Som,
            computer = gp.Computer?.Applied.Count,
            user = gp.User?.Applied.Count,
            who = gp.User?.Who,
            errors = gp.Errors.Count(e => !e.Warning),
            warnings = gp.Errors.Count(e => e.Warning),
            gpos = (gp.Computer?.Applied ?? []).Concat(gp.Computer?.Denied ?? []).Concat(gp.User?.Applied ?? []).Concat(gp.User?.Denied ?? []).Select(g => g.Id).Distinct(),
            minLength = gp.Local?.MinLength,
            firewall = gp.Local?.Firewall,
            rdp = gp.Local?.Rdp,
        },
        received = i.Received,
    };

    /// <summary>Whether a client runs an older version than this server.</summary>
    public static bool Behind(string? version) =>
        Version.TryParse(version, out var theirs) && Version.TryParse(Product.Version, out var ours) && theirs < ours;
}

public sealed record JoinForm(string? Computer, string? Os, string? Version);

/// <summary>A client asking the server to vouch for its HTTPS key: its own random number, base64 or hex, 32 to 128 characters.</summary>
public sealed record VouchForm(string? Nonce);

public sealed record CheckInForm(string? Computer, string? Os, string? Version, string? User, string? Code = null, string? CodeUsed = null, List<ProblemForm>? Problems = null);

public sealed record ClientSettingsForm(bool? Approve = null, int? UnlockAfterDays = null);

/// <summary>Allow uninstalling the client for an hour (Allow), lock it again, or have the client uninstall itself (Now).</summary>
public sealed record RemovalForm(bool Allow, bool Now);

/// <summary>How many joins one address gets: 30 in ten minutes, so nobody can flood Devices. Kept apart from sign-ins.</summary>
public sealed class JoinLimiter(TimeProvider clock) : Limiter(30, clock, byAddress: true);
