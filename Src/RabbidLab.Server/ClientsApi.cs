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
        app.MapPost("/api/clients/join", (JoinForm form, HttpContext context, Clients clients, JoinLimiter limiter, ActivityLog log) =>
        {
            var from = ActivityLog.Address(context);
            if (!limiter.Take(from))
            {
                return Results.Json(new { error = "Too many joins from this address. Wait a few minutes, then try again." }, statusCode: StatusCodes.Status429TooManyRequests);
            }
            var computer = form.Computer?.Trim() ?? "";
            if (computer.Length is 0 or > 64)
            {
                limiter.GiveBack(from); // not a join
                return Results.BadRequest(new { error = "Say which computer is joining." });
            }
            var (client, secret, problem) = clients.Join(computer, form.Os, form.Version, from);
            if (client is null)
            {
                return Results.Json(new { error = problem }, statusCode: StatusCodes.Status429TooManyRequests);
            }
            log.Record($"The client on {computer}", from, "clients", "client.join", client.Approved is null ? $"{computer} joined and is waiting for approval" : $"{computer} joined", "ok");
            return Results.Ok(new { id = client.Id, secret, approved = client.Approved is not null, version = Product.Version });
        });

        // The answer carries the check for the computer's uninstall code and the days without this server after which the
        // lock lets go, so both work there when this server can't be reached (the client's Removal.cs).
        app.MapPost("/api/clients/checkin", (CheckInForm form, HttpContext context, Clients clients, ActivityLog log) =>
        {
            var used = DateTime.TryParse(form.CodeUsed, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at.ToUniversalTime() : (DateTime?) null;
            if (Credentials(context) is not (var id, var secret)
                || clients.CheckIn(id, secret, form.Computer, form.Os, form.Version, form.User, ActivityLog.Address(context), form.Code, used, out var usedNow) is not { } c)
            {
                return Unknown();
            }
            if (usedNow)
            {
                log.Record($"The client on {c.Computer}", ActivityLog.Address(context), "clients", "client.code.used", $"The uninstall code was used on {c.Computer}", "ok");
            }
            return Results.Ok(new
            {
                approved = c.Approved is not null,
                version = Product.Version,
                removalUntil = clients.RemovalUntil(c)?.ToString("o"),
                uninstall = clients.TakeUninstall(c.Id),
                code = c.CodeCheck,
                unlockAfterDays = clients.UnlockAfterDays,
            });
        });

        // The client's service stopping: the computer shows as Offline at once, so the page open on it offers the client again.
        app.MapPost("/api/clients/bye", (HttpContext context, Clients clients) =>
            Credentials(context) is (var id, var secret) && clients.Stopping(id, secret) ? Results.Ok() : Unknown());

        // The client asking for its next order, held open until there is one (ClientCommands). Only computers let in get any.
        app.MapPost("/api/clients/next", async (HttpContext context, Clients clients, ClientCommands commands) =>
        {
            if (Caller(context, clients) is not { } c)
            {
                return Unknown();
            }
            if (c.Approved is null)
            {
                try
                {
                    await Task.Delay(ClientCommands.Hold, context.RequestAborted);
                }
                catch (TaskCanceledException)
                {
                    // It went.
                }
                return Results.NoContent();
            }
            return await commands.Next(c.Id, context.RequestAborted) is { } order ? Results.Ok(order) : Results.NoContent();
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
            })) : Access.NotAllowed());

        // The client's inventory of its computer (Inventory.cs): hardware, drives, software, Windows updates. Kept only for
        // a computer that's been approved.
        app.MapPost("/api/clients/inventory", (DeviceInventory inventory, HttpContext context, Clients clients, Inventories inventories) =>
        {
            if (Caller(context, clients) is not { } c)
            {
                return Unknown();
            }
            if (c.Approved is null)
            {
                return Results.Json(new { error = "This computer is waiting for approval." }, statusCode: StatusCodes.Status403Forbidden);
            }
            inventories.Set(c.Id, inventory);
            return Results.Ok();
        });

        // One computer's whole inventory, for its side panel; null when its client hasn't sent one yet.
        app.MapGet("/api/clients/{id}/inventory", (string id, HttpContext context, Clients clients, Inventories inventories) =>
            !Access.Can(context, Permissions.SeatsView) ? Access.NotAllowed()
            : clients.Find(id) is null ? Results.NotFound(new { error = "That client isn't here any more." })
            : Results.Ok(new { inventory = inventories.Get(id) }));

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

        app.MapPost("/api/clients/{id}/approve", (string id, HttpContext context, Clients clients, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsApprove) is { } refused)
            {
                return refused;
            }
            if (clients.Approve(id, context.User.FindFirst("name")?.Value ?? ActivityLog.Who(context)) is not { } c)
            {
                return Results.NotFound(new { error = "That client isn't here any more." });
            }
            log.Record(context, "clients", "client.approve", $"Approved {c.Computer}");
            return Results.Ok();
        });

        app.MapPost("/api/clients/{id}/remove", (string id, HttpContext context, Clients clients, ClientCommands commands, Inventories inventories, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsRemove) is { } refused)
            {
                return refused;
            }
            if (clients.Remove(id) is not { } c)
            {
                return Results.NotFound(new { error = "That client isn't here any more." });
            }
            commands.Forget(c.Id);
            inventories.Forget(c.Id);
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
                return Results.NotFound(new { error = "That client isn't here any more." });
            }
            log.Record(context, "clients", form.Allow ? form.Now ? "client.uninstall" : "client.removal.allow" : "client.removal.lock",
                form.Allow ? form.Now ? $"Ordered the client on {c.Computer} to uninstall itself" : $"Allowed uninstalling the client on {c.Computer} for an hour" : $"Locked uninstalling the client on {c.Computer} again");
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
                return Results.NotFound(new { error = "That client isn't here any more." });
            }
            log.Record(context, "clients", "client.code.show", $"Showed the uninstall code for {c.Computer}");
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

        // The client's MSI, for anyone: computers installing it, and clients updating themselves to this server's version.
        app.MapGet("/api/clients/download", () => File.Exists(ClientFiles.Msi)
            ? Results.File(ClientFiles.Msi, "application/x-msi", $"RabbidLab-Client-{Product.Version}.msi")
            : Results.NotFound(new { error = "This server has no client to hand out. Get it from the Rabbid Lab release on GitHub." }));

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
                log.Record(context, "clients", "client.settings", approve ? "New computers need approval" : "New computers connect without approval");
            }
            if (form.UnlockAfterDays is { } days)
            {
                clients.SetUnlockAfter(days);
                log.Record(context, "clients", "client.settings", days > 0 ? $"The uninstall lock is released after {days} days offline" : "The uninstall lock is no longer released for computers that are offline");
            }
            return Results.Ok();
        });
    }

    /// <summary>The id and secret the request carries, as Authorization: RabbidLab-Client id:secret.</summary>
    private static (string Id, string Secret)? Credentials(HttpContext context)
    {
        var said = context.Request.Headers.Authorization.ToString();
        var parts = said.StartsWith(Scheme + " ", StringComparison.Ordinal) ? said[(Scheme.Length + 1)..].Split(':', 2) : [];
        return parts.Length == 2 ? (parts[0], parts[1]) : null;
    }

    /// <summary>The client whose id and secret the request carries.</summary>
    private static Client? Caller(HttpContext context, Clients clients) =>
        Credentials(context) is (var id, var secret) ? clients.Verify(id, secret) : null;

    private static IResult Unknown() =>
        Results.Json(new { error = $"This computer isn't one of {Product.Title}'s clients. It will rejoin automatically." }, statusCode: StatusCodes.Status401Unauthorized);

    /// <summary>The few facts about a computer the list shows; the rest is asked for when the computer is opened.</summary>
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
        missing = i.MissingChecked is null ? (int?) null : i.Missing.Count,
        software = i.Software.Count,
        received = i.Received,
    };

    /// <summary>Whether a client runs an older version than this server.</summary>
    public static bool Behind(string? version) =>
        Version.TryParse(version, out var theirs) && Version.TryParse(Product.Version, out var ours) && theirs < ours;
}

public sealed record JoinForm(string? Computer, string? Os, string? Version);

public sealed record CheckInForm(string? Computer, string? Os, string? Version, string? User, string? Code = null, string? CodeUsed = null);

public sealed record ClientSettingsForm(bool? Approve = null, int? UnlockAfterDays = null);

/// <summary>Allow uninstalling the client for an hour (Allow), lock it again, or have the client uninstall itself (Now).</summary>
public sealed record RemovalForm(bool Allow, bool Now);

/// <summary>How many joins one address gets: 30 in ten minutes, so nobody can flood Devices. Kept apart from sign-ins.</summary>
public sealed class JoinLimiter(TimeProvider clock) : Limiter(30, clock);
