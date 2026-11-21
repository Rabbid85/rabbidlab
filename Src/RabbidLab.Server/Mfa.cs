using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>Someone's authenticator app: its secret (encrypted), since when, and the last code taken, so a code works once.</summary>
public sealed class MfaEntry
{
    public string Source { get; set; } = "";
    public string Username { get; set; } = "";
    public string Secret { get; set; } = "";
    public DateTime Since { get; set; }
    public long Used { get; set; }
}

/// <summary>A reset waiting to be used: the one-time code (only its hash) that setting MFA up again takes, and until when.</summary>
public sealed class MfaReset
{
    public string Source { get; set; } = "";
    public string Username { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTime Until { get; set; }
}

public sealed class MfaFile
{
    public List<MfaEntry> People { get; set; } = [];
    public List<MfaReset> Resets { get; set; } = [];
}

public enum MfaAnswer
{
    Right,
    Wrong,

    /// <summary>Too many wrong codes lately: nothing was checked.</summary>
    Wait,
}

/// <summary>
/// Multi-factor authentication: a code from an authenticator app (Totp.cs) after the password, required for everyone.
/// Secrets are kept in mfa.json in the data folder, encrypted. Five wrong codes for one person in ten minutes, then they
/// wait. A reset (Datto's way) leaves a one-time code the person needs, with their password, to set it up again.
/// </summary>
public sealed class Mfa(DataFolder folder, SettingsStore secrets, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly TimeSpan DraftLife = TimeSpan.FromMinutes(10);
    private readonly JsonFile<MfaFile> file = folder.Json<MfaFile>("mfa.json", Json);
    private readonly Limiter limiter = new(5, clock);
    // Secrets being set up from Settings, by person, until the first code confirms them. Kept encrypted, like mfa.json.
    private readonly Dictionary<string, (string Secret, DateTimeOffset Until)> drafts = [];

    private Lock gate => file.Gate;

    /// <summary>Since when this person has had it on, or null.</summary>
    public DateTime? Since(string source, string username)
    {
        lock (gate)
        {
            return Of(file.Read(), source, username)?.Since;
        }
    }

    public bool Has(Who who) => Since(who.Source, who.Username) is not null;

    /// <summary>A code for a sign-in or for turning it off. A code already taken, or one from before it, is wrong.</summary>
    public MfaAnswer Check(Who who, string? code)
    {
        var key = Key(who.Source, who.Username);
        if (!limiter.Take(key))
        {
            return MfaAnswer.Wait;
        }
        lock (gate)
        {
            var f = file.Read();
            if (Of(f, who.Source, who.Username) is { } entry && secrets.TryDecrypt(entry.Secret) is { } secret
                && Totp.Match(secret, code, clock.GetUtcNow()) is { } step && step > entry.Used)
            {
                entry.Used = step;
                file.Save(f);
                limiter.GiveBack(key);
                return MfaAnswer.Right;
            }
        }
        return MfaAnswer.Wrong;
    }

    /// <summary>A new secret to set up from Settings, kept until Confirm or ten minutes.</summary>
    public string Begin(Who who)
    {
        var secret = Totp.NewSecret();
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var old in drafts.Where(d => d.Value.Until < now).Select(d => d.Key).ToList())
            {
                drafts.Remove(old);
            }
            drafts[Key(who.Source, who.Username)] = (secrets.Encrypt(secret), now + DraftLife);
        }
        return secret;
    }

    /// <summary>Turns it on with the secret from Begin, once a code from the app shows the app has it. Null when there's nothing being set up.</summary>
    public MfaAnswer? Confirm(Who who, string? code)
    {
        string secret;
        lock (gate)
        {
            if (!drafts.TryGetValue(Key(who.Source, who.Username), out var draft) || draft.Until < clock.GetUtcNow()
                || secrets.TryDecrypt(draft.Secret) is not { } opened)
            {
                return null;
            }
            secret = opened;
        }
        var answer = Enable(who, secret, code);
        if (answer == MfaAnswer.Right)
        {
            lock (gate)
            {
                drafts.Remove(Key(who.Source, who.Username));
            }
        }
        return answer;
    }

    /// <summary>Turns it on with this secret when the code is one the secret gives now. Replaces what they had.</summary>
    public MfaAnswer Enable(Who who, string secret, string? code)
    {
        var key = Key(who.Source, who.Username);
        if (!limiter.Take(key))
        {
            return MfaAnswer.Wait;
        }
        if (Totp.Match(secret, code, clock.GetUtcNow()) is not { } step)
        {
            return MfaAnswer.Wrong;
        }
        limiter.GiveBack(key);
        lock (gate)
        {
            var f = file.Read();
            f.People.RemoveAll(e => Same(e, who.Source, who.Username));
            f.People.Add(new MfaEntry { Source = who.Source, Username = who.Username, Secret = secrets.Encrypt(secret), Since = clock.GetUtcNow().UtcDateTime, Used = step });
            file.Save(f);
        }
        return MfaAnswer.Right;
    }

    /// <summary>Takes it off one person. Whether they had it.</summary>
    public bool Remove(string source, string username)
    {
        lock (gate)
        {
            var f = file.Read();
            if (f.People.RemoveAll(e => Same(e, source, username)) == 0)
            {
                return false;
            }
            file.Save(f);
            return true;
        }
    }

    public static readonly TimeSpan ResetLife = TimeSpan.FromHours(24);
    private const string ResetLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O or 1/I to misread

    /// <summary>
    /// A lost phone: takes their app off and gives the one-time code that setting it up again takes, shown to whoever reset
    /// it once and kept only as a hash. Good once, for 24 hours. A newer reset replaces an older one.
    /// </summary>
    public string Reset(string source, string username)
    {
        var raw = RandomNumberGenerator.GetString(ResetLetters, 12);
        var code = $"{raw[..4]}-{raw[4..8]}-{raw[8..]}";
        lock (gate)
        {
            var f = file.Read();
            f.People.RemoveAll(e => Same(e, source, username));
            f.Resets.RemoveAll(r => Same(r, source, username) || r.Until < clock.GetUtcNow().UtcDateTime);
            f.Resets.Add(new MfaReset { Source = source, Username = username, Hash = HashOf(code), Until = (clock.GetUtcNow() + ResetLife).UtcDateTime });
            file.Save(f);
        }
        return code;
    }

    /// <summary>Whether setting MFA up for this person takes a reset code first.</summary>
    public bool ResetWaiting(Who who)
    {
        lock (gate)
        {
            return file.Read().Resets.Any(r => Same(r, who.Source, who.Username) && r.Until > clock.GetUtcNow().UtcDateTime);
        }
    }

    /// <summary>Whether this is their reset code. It counts against the same five tries as a wrong app code.</summary>
    public MfaAnswer CheckReset(Who who, string? code)
    {
        var key = Key(who.Source, who.Username);
        if (!limiter.Take(key))
        {
            return MfaAnswer.Wait;
        }
        var typed = HashOf((code ?? "").Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal));
        lock (gate)
        {
            if (file.Read().Resets.FirstOrDefault(r => Same(r, who.Source, who.Username) && r.Until > clock.GetUtcNow().UtcDateTime) is { } reset
                && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(reset.Hash), Convert.FromHexString(typed)))
            {
                limiter.GiveBack(key);
                return MfaAnswer.Right;
            }
        }
        return MfaAnswer.Wrong;
    }

    /// <summary>Uses up their reset code, once the new app is set up.</summary>
    public void ClearReset(Who who)
    {
        lock (gate)
        {
            var f = file.Read();
            if (f.Resets.RemoveAll(r => Same(r, who.Source, who.Username)) > 0)
            {
                file.Save(f);
            }
        }
    }

    /// <summary>Every account with this username, however it signs in, that has MFA set up (--reset-mfa).</summary>
    public List<(string Source, string Username)> WithName(string username)
    {
        lock (gate)
        {
            return [.. file.Read().People.Where(e => string.Equals(e.Username, username, StringComparison.OrdinalIgnoreCase)).Select(e => (e.Source, e.Username))];
        }
    }

    // Dashes and spaces don't count, so XXXX-XXXX-XXXX and XXXXXXXXXXXX are the same code.
    private static string HashOf(string code) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code.Replace("-", "", StringComparison.Ordinal))));

    private static bool Same(MfaReset r, string source, string username) => r.Source == source && string.Equals(r.Username, username, StringComparison.OrdinalIgnoreCase);

    private static MfaEntry? Of(MfaFile f, string source, string username) => f.People.FirstOrDefault(e => Same(e, source, username));

    private static bool Same(MfaEntry e, string source, string username) => e.Source == source && string.Equals(e.Username, username, StringComparison.OrdinalIgnoreCase);

    private static string Key(string source, string username) => $"{source}:{username.ToLowerInvariant()}";
}

/// <summary>
/// A sign-in whose password was right, waiting for its code (or for the app to be set up, with the secret to set up,
/// encrypted). Keys: what the name's wrong passwords and codes are counted under (SignInLocks), the account's own first.
/// </summary>
public sealed record PendingSignIn(ClaimsPrincipal Principal, string Name, string? Secret, DateTimeOffset Until, IReadOnlyList<string> Keys);

/// <summary>Sign-ins between the password and the code, by a ticket only the page that typed the password has. Ten minutes each.</summary>
public sealed class PendingSignIns(TimeProvider clock, SettingsStore secrets)
{
    /// <summary>The secret being set up, opened only to check the code with; null when there's none or it can't be opened.</summary>
    public string? Secret(PendingSignIn waiting) => waiting.Secret is null ? null : secrets.TryDecrypt(waiting.Secret);

    private static readonly TimeSpan Life = TimeSpan.FromMinutes(10);
    private const int Most = 1000;
    private readonly Lock gate = new();
    private readonly Dictionary<string, PendingSignIn> waiting = [];

    public string Add(ClaimsPrincipal principal, string name, string? secret, IReadOnlyList<string> keys)
    {
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var old in waiting.Where(w => w.Value.Until < now).Select(w => w.Key).ToList())
            {
                waiting.Remove(old);
            }
            // Each took a right password to make, so this many at once isn't people signing in: the oldest go.
            foreach (var old in waiting.OrderBy(w => w.Value.Until).Take(Math.Max(0, waiting.Count - Most + 1)).Select(w => w.Key).ToList())
            {
                waiting.Remove(old);
            }
            waiting[ticket] = new PendingSignIn(principal, name, secret is null ? null : secrets.Encrypt(secret), now + Life, keys);
        }
        return ticket;
    }

    public PendingSignIn? Find(string? ticket)
    {
        lock (gate)
        {
            return ticket is not null && waiting.TryGetValue(ticket, out var found) && found.Until >= clock.GetUtcNow() ? found : null;
        }
    }

    public void Remove(string ticket)
    {
        lock (gate)
        {
            waiting.Remove(ticket);
        }
    }
}

/// <summary>
/// MFA for the person signed in (Settings, Account): whether it's on, setting it up and turning it off. And for others:
/// an admin clears someone's, so they set it up again. Requiring it is a role's own switch (RolesApi.cs).
/// </summary>
public static class MfaApi
{
    public const string WrongCode = "That code isn't right. Codes change every 30 seconds, so check the app and type the one it shows now.";
    public const string TooMany = "Too many wrong codes. Wait a few minutes, then try again.";
    private const string AlreadyOn = "MFA is already on. Turn it off first, with a code from your app, then set it up again.";

    /// <summary>What the app is asked to add: the secret, its QR code, and the account's name as the app will list it.</summary>
    public static object Setup(Who who, string secret) => new
    {
        secret,
        account = who.Username,
        qr = Totp.Qr(Totp.Uri(Product.Name, who.Username, secret)), // Rabbid Lab Dev by its own name, so the app keeps the two apart
    };

    public static void MapMfaApi(this WebApplication app)
    {
        // Theirs, and for anyone who manages roles, how many administrators have it: with one, a lost phone means the server.
        app.MapGet("/api/mfa", (HttpContext context, Mfa mfa, Roles roles) =>
        {
            var who = Who.Of(context.User);
            int? admins = Access.Can(context, Permissions.AccessRoles)
                ? roles.Snapshot().People.Count(p => mfa.Since(p.Source, p.Username) is not null
                    && roles.Of(new Who(p.Source, p.Username, p.Domain, p.Groups)).Any(r => r.Id == Roles.Admin))
                : null;
            return Results.Ok(new { since = mfa.Since(who.Source, who.Username), admins });
        });

        // Only while it's off: with it on, a new app would take the old one's place without a code from the old one.
        app.MapPost("/api/mfa/setup", (HttpContext context, Mfa mfa) =>
            Access.Refuse(context) ?? (mfa.Has(Who.Of(context.User)) ? Results.BadRequest(new { error = AlreadyOn })
                : Results.Ok(Setup(Who.Of(context.User), mfa.Begin(Who.Of(context.User))))));

        app.MapPost("/api/mfa/on", (CodeForm form, HttpContext context, Mfa mfa, ActivityLog log) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            if (mfa.Has(Who.Of(context.User)))
            {
                return Results.BadRequest(new { error = AlreadyOn });
            }
            switch (mfa.Confirm(Who.Of(context.User), form.Code))
            {
                case null:
                    return Results.BadRequest(new { error = "That took too long, so there's a new QR code. Scan it and try again.", again = true });
                case MfaAnswer.Wait:
                    return Results.Json(new { error = TooMany }, statusCode: StatusCodes.Status429TooManyRequests);
                case MfaAnswer.Wrong:
                    return Results.BadRequest(new { error = WrongCode });
                default:
                    log.Record(context, "access", "mfa.on", "Turned on MFA");
                    return Results.Ok();
            }
        });

        // Turning it off takes a code, so a browser left signed in can't do it.
        app.MapPost("/api/mfa/off", (CodeForm form, HttpContext context, Mfa mfa, ActivityLog log) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            var who = Who.Of(context.User);
            switch (mfa.Check(who, form.Code))
            {
                case MfaAnswer.Wait:
                    return Results.Json(new { error = TooMany }, statusCode: StatusCodes.Status429TooManyRequests);
                case MfaAnswer.Wrong:
                    log.Record(context, "access", "mfa.off.failed", "Typed a wrong MFA code", "failed");
                    return Results.BadRequest(new { error = WrongCode });
                default:
                    mfa.Remove(who.Source, who.Username);
                    log.Record(context, "access", "mfa.off", "Turned off MFA");
                    return Results.Ok();
            }
        });

        // Someone else's, for a lost phone (Datto's way): their app stops working, and the answer is a one-time code to give
        // them. At their next sign-in they need it, with their password, to set up a new app.
        app.MapPost("/api/mfa/reset", (MfaResetForm form, HttpContext context, Mfa mfa, Roles roles, Confirmations confirm, ActivityLog log) =>
        {
            if ((Access.RefuseUnless(context, Permissions.AccessMfa) ?? confirm.Refuse(context)) is { } refused)
            {
                return refused;
            }
            var person = roles.Snapshot().People.FirstOrDefault(p => p.Source == form.Source && string.Equals(p.Username, form.Username, StringComparison.OrdinalIgnoreCase));
            if (person is null || mfa.Since(person.Source, person.Username) is null)
            {
                return Results.BadRequest(new { error = "They don't have MFA set up." });
            }
            var me = Who.Of(context.User);
            if (person.Source == me.Source && string.Equals(person.Username, me.Username, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "Your own MFA is turned off in Settings, Account, with a code from your app." });
            }
            // Nobody above the one asking: with their password too, that would be their account.
            var above = roles.Outranking(Who.Of(context.User)).Select(r => r.Id).ToHashSet();
            if (roles.Of(new Who(person.Source, person.Username, person.Domain, person.Groups)).FirstOrDefault(r => above.Contains(r.Id)) is { } role)
            {
                return Results.Json(new { error = $"{person.Username} is in {role.Name}. Only someone above {role.Name} can reset their MFA." }, statusCode: StatusCodes.Status403Forbidden);
            }
            var code = mfa.Reset(person.Source, person.Username);
            log.Record(context, "access", "mfa.reset", $"Reset MFA for {person.Username}", "ok", "Gave a one-time code to set it up again");
            return Results.Ok(new { code, hours = (int) Mfa.ResetLife.TotalHours });
        });
    }

    /// <summary>
    /// --reset-mfa username: resets that account's MFA, for when its phone is gone and nobody else can, and prints the
    /// one-time code it needs to set up a new app. Run it on the machine Rabbid Lab is on, as an administrator (in Docker,
    /// with docker exec). The exit code, or null to run.
    /// </summary>
    public static int? ResetMfaCommand(this WebApplication app, string[] args)
    {
        var at = Array.IndexOf(args, "--reset-mfa");
        if (at < 0)
        {
            return null;
        }
        if (at + 1 >= args.Length || args[at + 1].StartsWith('-'))
        {
            Console.Error.WriteLine("Whose? --reset-mfa username");
            return 1;
        }
        var username = args[at + 1];
        var mfa = app.Services.GetRequiredService<Mfa>();
        var accounts = mfa.WithName(username);
        if (accounts.Count == 0)
        {
            Console.Error.WriteLine($"{username} doesn't have MFA set up.");
            return 1;
        }
        foreach (var (source, name) in accounts)
        {
            var code = mfa.Reset(source, name);
            app.Services.GetRequiredService<ActivityLog>().Record(name, null, "access", "mfa.reset", "Reset MFA from the command line", "ok");
            Console.WriteLine($"{name} ({source}): at the next sign-in to {Product.Name}, type the password, then this one-time code, then set up the new app: {code}");
        }
        Console.WriteLine($"Each code works once, for {(int) Mfa.ResetLife.TotalHours} hours.");
        return 0;
    }
}

public sealed record CodeForm(string? Code);

public sealed record MfaResetForm(string? Source, string? Username);
