using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace RabbidLab.Server;

/// <summary>One signed-in session: who, as what, how they signed in, from where, and when it was last heard from.</summary>
public sealed class Session
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";

    /// <summary>The Active Directory domain or computer they signed in with, and the groups in roles they were in: what their roles come from.</summary>
    public string? Domain { get; set; }
    public List<string> Groups { get; set; } = [];
    public bool Remembered { get; set; }
    public DateTimeOffset Started { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public DateTimeOffset? Expires { get; set; }
    public string? Address { get; set; }
    public string? Agent { get; set; }

    /// <summary>The sign-in itself, encrypted: what the cookie used to carry.</summary>
    public string Ticket { get; set; } = "";
}

/// <summary>
/// Sessions kept here instead of in the cookie, which carries only the session's id, so admins can see who is signed in
/// and sign any of them out. Saved to sessions.json with the sign-ins encrypted, so updating or restarting signs nobody out.
/// </summary>
public sealed class Sessions : ITicketStore, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(30); // last seen changes on every request
    private const string Asking = "rabbidlab.session";
    private readonly JsonFile<List<Session>> file;
    private readonly IDataProtector protector;
    private readonly TimeProvider clock;
    private readonly ILogger log;
    private readonly Timer saver;
    private readonly Dictionary<string, Session> sessions = [];
    private readonly Dictionary<string, AuthenticationTicket> tickets = [];
    private bool changed;

    private Lock gate => file.Gate;

    public Sessions(DataFolder folder, IDataProtectionProvider protection, TimeProvider clock)
    {
        file = folder.Json<List<Session>>("sessions.json", Json);
        protector = protection.CreateProtector("RabbidLab.Sessions");
        this.clock = clock;
        log = folder.Log;
        try
        {
            foreach (var s in file.Read())
            {
                sessions[s.Id] = s;
            }
        }
        catch (IOException)
        {
            // A file that can't be opened signs everyone out, nothing worse.
        }
        // Last seen is written here, off the requests that change it.
        saver = new Timer(_ => Flush(), null, SaveEvery, SaveEvery);
    }

    public void Dispose() => saver.Dispose();

    /// <summary>The id of the session making this request, once sign-in has read its cookie.</summary>
    public static string? Current(HttpContext context) => context.Items[Asking] as string;

    /// <summary>Everyone signed in now, the most recently seen first.</summary>
    public IReadOnlyList<Session> All()
    {
        lock (gate)
        {
            Prune();
            return [.. sessions.Values.OrderByDescending(s => s.LastSeen)];
        }
    }

    public Session? Find(string id)
    {
        lock (gate)
        {
            return sessions.GetValueOrDefault(id);
        }
    }

    /// <summary>Ends a session: its next request finds itself signed out.</summary>
    public bool End(string id)
    {
        lock (gate)
        {
            tickets.Remove(id);
            if (!sessions.Remove(id))
            {
                return false;
            }
            Save();
            return true;
        }
    }

    /// <summary>Writes what's changed, such as when Rabbid Lab stops.</summary>
    public void Flush()
    {
        try
        {
            lock (gate)
            {
                if (changed)
                {
                    Save();
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(e, "sessions.json could not be written");
        }
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket) => Task.FromResult(Store(ticket, null));

    public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken) => Task.FromResult(Store(ticket, null));

    public Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken) => Task.FromResult(Store(ticket, httpContext));

    public Task RenewAsync(string key, AuthenticationTicket ticket) => Renew(key, ticket);

    public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken) => Renew(key, ticket);

    public Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken) => Renew(key, ticket);

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => Task.FromResult(Retrieve(key, null));

    public Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken) => Task.FromResult(Retrieve(key, null));

    public Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken) => Task.FromResult(Retrieve(key, httpContext));

    public Task RemoveAsync(string key)
    {
        End(key);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken) => RemoveAsync(key);

    public Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken) => RemoveAsync(key);

    private string Store(AuthenticationTicket ticket, HttpContext? context)
    {
        var p = ticket.Principal;
        var now = clock.GetUtcNow();
        var s = new Session
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            Username = p.Identity?.Name ?? "",
            Name = p.FindFirstValue("name") ?? "",
            Source = p.FindFirstValue("source") ?? "",
            Domain = p.FindFirstValue("domain"),
            Groups = [.. p.FindAll("group").Select(c => c.Value)],
            Remembered = ticket.Properties.IsPersistent,
            Started = now,
            LastSeen = now,
            Expires = ticket.Properties.ExpiresUtc,
            Address = context is null ? null : ActivityLog.Address(context),
            Agent = context?.Request.Headers.UserAgent.ToString(),
            Ticket = Protect(ticket),
        };
        lock (gate)
        {
            Prune();
            sessions[s.Id] = s;
            tickets[s.Id] = ticket;
            Save();
        }
        if (context is not null)
        {
            context.Items[Asking] = s.Id;
        }
        return s.Id;
    }

    private AuthenticationTicket? Retrieve(string key, HttpContext? context)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(key, out var s))
            {
                return null;
            }
            if (!tickets.TryGetValue(key, out var ticket))
            {
                // Read back from the file after a restart; unreadable (its keys are gone) is signed out.
                if (Unprotect(s.Ticket) is not { } read)
                {
                    sessions.Remove(key);
                    Save();
                    return null;
                }
                ticket = tickets[key] = read;
            }
            s.LastSeen = clock.GetUtcNow();
            if (context is not null)
            {
                s.Address = ActivityLog.Address(context) ?? s.Address;
                context.Items[Asking] = key;
            }
            changed = true;
            return ticket;
        }
    }

    private Task Renew(string key, AuthenticationTicket ticket)
    {
        lock (gate)
        {
            // Ended meanwhile, by an admin or a sign-out elsewhere: it stays ended.
            if (sessions.TryGetValue(key, out var s))
            {
                s.Expires = ticket.Properties.ExpiresUtc;
                s.Remembered = ticket.Properties.IsPersistent;
                s.Ticket = Protect(ticket);
                tickets[key] = ticket;
                changed = true;
            }
        }
        return Task.CompletedTask;
    }

    private void Prune()
    {
        var now = clock.GetUtcNow();
        foreach (var gone in sessions.Values.Where(s => s.Expires < now).Select(s => s.Id).ToList())
        {
            sessions.Remove(gone);
            tickets.Remove(gone);
            changed = true;
        }
    }

    private void Save()
    {
        file.Save([.. sessions.Values]);
        changed = false;
    }

    private string Protect(AuthenticationTicket ticket) => Convert.ToBase64String(protector.Protect(TicketSerializer.Default.Serialize(ticket)));

    private AuthenticationTicket? Unprotect(string text)
    {
        try
        {
            return TicketSerializer.Default.Deserialize(protector.Unprotect(Convert.FromBase64String(text)));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The browser and system in a few words, such as Edge on Windows.</summary>
    public static string Device(string? agent)
    {
        if (string.IsNullOrWhiteSpace(agent))
        {
            return "Unknown";
        }
        if (agent.Contains("RabbidLab-Client", StringComparison.Ordinal))
        {
            return $"{Product.Title} client";
        }
        var browser = agent.Contains("Edg/") ? "Edge" : agent.Contains("OPR/") ? "Opera" : agent.Contains("Firefox/") ? "Firefox"
            : agent.Contains("Chrome/") ? "Chrome" : agent.Contains("Safari/") ? "Safari" : "A browser";
        var system = agent.Contains("Windows") ? "Windows" : agent.Contains("Android") ? "Android" : agent.Contains("iPhone") || agent.Contains("iPad") ? "iOS"
            : agent.Contains("Mac OS X") ? "macOS" : agent.Contains("Linux") ? "Linux" : null;
        return system is null ? browser : $"{browser} on {system}";
    }
}

public static class SessionsApi
{
    public static void MapSessionsApi(this WebApplication app)
    {
        app.Lifetime.ApplicationStopping.Register(() => app.Services.GetRequiredService<Sessions>().Flush());

        // Who is signed in: everyone, with See everyone signed in; otherwise their own sessions. Each with their roles, highest first.
        app.MapGet("/api/sessions", (HttpContext context, Sessions sessions, Roles roles) =>
        {
            var all = Access.Can(context, Permissions.SessionsView);
            var current = Sessions.Current(context);
            return sessions.All().Where(s => all || Mine(s, context)).Select(s => new
            {
                id = s.Id,
                username = s.Username,
                name = s.Name,
                roles = Roles.Brief(roles.Of(Who.Of(s))),
                source = s.Source,
                remembered = s.Remembered,
                started = s.Started,
                lastSeen = s.LastSeen,
                address = s.Address,
                device = Sessions.Device(s.Agent),
                current = s.Id == current,
            });
        });

        // Signs a session out: anyone their own, and others' with Sign other people out.
        app.MapPost("/api/sessions/{id}/signout", (string id, HttpContext context, Sessions sessions, ActivityLog log) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            if (sessions.Find(id) is not { } s)
            {
                return Results.NotFound(new { error = "That session has already ended." });
            }
            if (!Mine(s, context) && !Access.Can(context, Permissions.SessionsSignOut))
            {
                return Access.NotAllowed();
            }
            sessions.End(id);
            log.Record(context, "sign-in", "session.end", $"Signed out {s.Name} ({s.Username}) on {Sessions.Device(s.Agent)}");
            return Results.Ok(new { ended = id });
        });
    }

    /// <summary>Whether a session is the asker's own: the same name, signed in the same way to the same domain.</summary>
    private static bool Mine(Session s, HttpContext context)
    {
        var me = Who.Of(context.User);
        return s.Username == me.Username && s.Source == me.Source && s.Domain == me.Domain;
    }
}
