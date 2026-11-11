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
            log.Record($"The client on {computer}", from, "clients", "client.join", client.Approved is null ? $"{computer} joined and waits to be let in" : $"{computer} joined", "ok");
            return Results.Ok(new { id = client.Id, secret, approved = client.Approved is not null, version = Product.Version });
        });

        app.MapPost("/api/clients/checkin", (CheckInForm form, HttpContext context, Clients clients) =>
            Credentials(context) is (var id, var secret) && clients.CheckIn(id, secret, form.Computer, form.Os, form.Version, form.User, ActivityLog.Address(context)) is { } c
                ? Results.Ok(new { approved = c.Approved is not null, version = Product.Version, removalUntil = clients.RemovalUntil(c)?.ToString("o"), uninstall = clients.TakeUninstall(c.Id) }) : Unknown());

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

        // The computer this page is open on: whether it's a seat, and if not, what it needs to become one.
        app.MapGet("/api/clients/here", (HttpContext context, Clients clients, IConfiguration config) =>
        {
            var here = Addresses.Normal(ActivityLog.Address(context));
            var seat = here is null ? null : clients.All().FirstOrDefault(c => Addresses.Normal(c.Address) == here);
            return Results.Ok(new
            {
                seat = seat is null ? null : new { id = seat.Id, computer = seat.Computer, approved = seat.Approved is not null, online = clients.IsOnline(seat) },
                thisServer = Addresses.OnServer(context.Connection.RemoteIpAddress), // the page is open on the server itself: no seat to offer
                msi = File.Exists(ClientFiles.Msi),
                server = $"{context.Request.Scheme}://{context.Request.Host}",
                fingerprint = ClientFiles.Fingerprint(config),
                version = Product.Version,
            });
        });

        app.MapGet("/api/clients", (HttpContext context, Clients clients) =>
            Access.Can(context, Permissions.SeatsView) ? Results.Ok(clients.All().Select(c => new
            {
                id = c.Id,
                computer = c.Computer,
                os = c.Os,
                version = c.Version,
                user = c.User,
                address = c.Address,
                joined = c.Joined,
                lastSeen = c.LastSeen,
                approved = c.Approved,
                approvedBy = c.ApprovedBy,
                online = clients.IsOnline(c),
                behind = Behind(c.Version),
                removalUntil = clients.RemovalUntil(c),
                uninstalling = c.Uninstall,
            })) : Access.NotAllowed());

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
            log.Record(context, "clients", "client.approve", $"Let {c.Computer} in");
            return Results.Ok();
        });

        app.MapPost("/api/clients/{id}/remove", (string id, HttpContext context, Clients clients, ClientCommands commands, ActivityLog log) =>
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

        // Add computers: whether new ones wait, this server's certificate fingerprint, and which files it has.
        app.MapGet("/api/clients/settings", (HttpContext context, Clients clients, IConfiguration config) =>
        {
            if (!Access.Can(context, Permissions.SeatsAdd))
            {
                return Access.NotAllowed();
            }
            return Results.Ok(new
            {
                approve = clients.Waits,
                fingerprint = ClientFiles.Fingerprint(config),
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
            clients.SetApprove(form.Approve);
            log.Record(context, "clients", "client.settings", form.Approve ? "New computers wait for an admin" : "New computers join straight away");
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
        Results.Json(new { error = "This computer isn't one of Rabbid Lab's clients. It will rejoin automatically." }, statusCode: StatusCodes.Status401Unauthorized);

    /// <summary>Whether a client runs an older version than this server.</summary>
    public static bool Behind(string? version) =>
        Version.TryParse(version, out var theirs) && Version.TryParse(Product.Version, out var ours) && theirs < ours;
}

public sealed record JoinForm(string? Computer, string? Os, string? Version);

public sealed record CheckInForm(string? Computer, string? Os, string? Version, string? User);

public sealed record ClientSettingsForm(bool Approve);

/// <summary>Allow uninstalling the client for an hour (Allow), lock it again, or have the client uninstall itself (Now).</summary>
public sealed record RemovalForm(bool Allow, bool Now);

/// <summary>How many joins one address gets: 30 in ten minutes, so nobody can flood Seats. Kept apart from sign-ins.</summary>
public sealed class JoinLimiter(TimeProvider clock) : Limiter(30, clock);
