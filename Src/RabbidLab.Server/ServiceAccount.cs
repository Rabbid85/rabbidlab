using System.Globalization;

namespace RabbidLab.Server;

/// <summary>How the service account's sign-in is doing: ok, or why it fails (rejected, locked, disabled), and when that was tested.</summary>
public sealed record ServiceHealth(string State, string? Text, DateTime At);

/// <summary>What Test sign-in found with the sign-in typed in the set-up window: who it is, whether a domain admin, how many OUs it read.</summary>
public sealed record SignInTest(string Account, bool DomainAdmin, int Ous);

/// <summary>A domain admin sign-in to fix the account with (used once), or the account's own new password when its owner changed it.</summary>
public sealed record RepairForm(string? Username, string? Password, string? AccountPassword);

/// <summary>Looking after the service account once it's set up: testing it, fixing it, and changing the password Rabbid Lab manages.</summary>
public interface IServiceAccount
{
    /// <summary>How its sign-in did the last time it was tested, or null when it hasn't been, or there's no account.</summary>
    ServiceHealth? Health { get; }

    /// <summary>Signs in with what was typed and changes nothing.</summary>
    Task<SignInTest> TestSignIn(SetupForm form, CancellationToken stop);

    /// <summary>Signs in as the service account now. Null when there's none; what was known when the domain doesn't answer.</summary>
    Task<ServiceHealth?> Check(CancellationToken stop);

    /// <summary>Fixes what stops it signing in (a new password, unlocked, or enabled), then tests it again.</summary>
    Task<ServiceHealth> Repair(RepairForm form, CancellationToken stop);

    /// <summary>A new random password for the domain admin Rabbid Lab made. When the next change is due.</summary>
    Task<DateTime> ChangePassword(CancellationToken stop);

    /// <summary>Whether that password is 30 days old.</summary>
    bool PasswordDue(DateTime now);
}

public sealed partial class DirectoryTools : IServiceAccount
{
    /// <summary>How long a password Rabbid Lab manages lasts.</summary>
    public static readonly TimeSpan PasswordLife = TimeSpan.FromDays(30);

    private const string DomainAdminsRid = "512";
    private const string InChain = "1.2.840.113556.1.4.1941"; // memberOf through groups in groups

    private ServiceHealth? health;

    public ServiceHealth? Health => health;

    private bool Broken => health is { State: not "ok" };

    /// <summary>What a change says while the service account can't sign in.</summary>
    public static string SignInFailedText => "Active Directory is read-only because the service account sign-in failed. Fix the service account in Settings, Active Directory.";

    public async Task<SignInTest> TestSignIn(SetupForm form, CancellationToken stop)
    {
        var domain = Clean(form.Domain) ?? Domain ?? throw new ToolProblem("Enter the domain name, for example amw.com.");
        var username = form.Username?.Trim() ?? "";
        using var ldap = await open(domain, await Host(domain, stop), username, form.Password ?? "", stop) ?? throw new ToolProblem("The username or password is incorrect.");
        var me = await Ldap.Bound(ldap, username, ["sAMAccountName"], stop) ?? throw new ToolProblem($"{Ldap.Account(username)} signed in, but the account was not found in {domain}.");
        var ous = (await PlacesIn(ldap, ldap.BaseDn, stop)).Count(p => p.Dn.StartsWith("OU=", StringComparison.OrdinalIgnoreCase));
        return new SignInTest(me.Text("sAMAccountName") ?? Ldap.Account(username), await IsDomainAdmin(ldap, me.Dn, stop), ous);
    }

    public async Task<ServiceHealth?> Check(CancellationToken stop)
    {
        var f = Load();
        if (f.Account is null || f.Domain is null || f.Scope is null)
        {
            health = null;
            return null;
        }
        var was = health;
        ServiceHealth now;
        try
        {
            // A sign-in of its own, not one of the connections kept open: those stay signed in after the account stops working.
            var password = secrets.TryDecrypt(f.Password);
            using var ldap = password is null ? null : await open(f.Domain, await Host(f.Domain, stop), $"{f.Account}@{f.Domain}", password, stop);
            now = ldap is not null ? new ServiceHealth("ok", null, DateTime.UtcNow) : await Why(f, stop);
        }
        catch (DirectoryProblem)
        {
            // The domain didn't answer, which says nothing about the account: what was known stays.
            return was;
        }
        health = now;
        if ((was?.State ?? "ok") != now.State)
        {
            pool?.Flush(); // the connections change hands: its own account, or this computer's
        }
        return now;
    }

    /// <summary>Why the sign-in failed, read as this computer: disabled or expired, locked out, or (anything else) the password.</summary>
    private async Task<ServiceHealth> Why(DirectoryToolsFile f, CancellationToken stop)
    {
        var at = DateTime.UtcNow;
        var rejected = new ServiceHealth("rejected", $"{f.Account} sign-in failed: the password was rejected by {f.Domain}.", at);
        if (AsComputer is null)
        {
            return rejected;
        }
        try
        {
            using var read = await AsComputer(f.Domain!, await Host(f.Domain!, stop), stop);
            var row = read is null ? null : await Account(read, f, ["userAccountControl", "msDS-User-Account-Control-Computed", "lockoutTime", "accountExpires"], stop);
            if (row is null)
            {
                return rejected;
            }
            var computed = row.Number("msDS-User-Account-Control-Computed");
            if (((row.Number("userAccountControl") ?? NormalAccount) & Disabled) != 0 || FileTime(row.Number("accountExpires")) < at)
            {
                return new ServiceHealth("disabled", $"{f.Account} is disabled or expired in Active Directory.", at);
            }
            return (computed is { } c ? (c & LockedOut) != 0 : (row.Number("lockoutTime") ?? 0) > 0)
                ? new ServiceHealth("locked", $"{f.Account} is locked out in Active Directory after failed sign-ins.", at) : rejected;
        }
        catch (Exception e) when (e is DirectoryProblem or LdapRefused)
        {
            return rejected;
        }
    }

    public async Task<ServiceHealth> Repair(RepairForm form, CancellationToken stop)
    {
        var f = Load();
        if (f.Account is null || f.Domain is null || f.Scope is null)
        {
            throw new ToolProblem("The Active Directory service account is not configured. Configure it first.", StatusCodes.Status409Conflict);
        }
        // Tested again first, so the fix is for what's wrong now; nothing to fix when it signs in.
        var tested = await Check(stop);
        var state = tested?.State;
        if (state == "ok")
        {
            return tested!;
        }
        var host = await Host(f.Domain, stop);
        if (state == "rejected" && !f.Made)
        {
            // An account Rabbid Lab was given: its owner changed the password, and the new one is all that's needed.
            var given = form.AccountPassword ?? "";
            using var test = await open(f.Domain, host, $"{f.Account}@{f.Domain}", given, stop) ?? throw new ToolProblem($"{f.Domain} rejected that password for {f.Account}.");
            Save(f.With(x => x.Password = secrets.Encrypt(given)));
            return (await Check(stop))!;
        }
        var username = form.Username?.Trim() ?? "";
        var signedIn = Ldap.Account(username);
        using var ldap = await open(f.Domain, host, username, form.Password ?? "", stop) ?? throw new ToolProblem("The username or password is incorrect.");
        var row = await Account(ldap, f, ["userAccountControl"], stop)
            ?? throw new ToolProblem($"{f.Account} was not found in {f.Domain}. Disconnect the service account, then set up a new one.");
        try
        {
            if (state == "locked")
            {
                await ldap.Modify(row.Dn, [new(LdapChange.Replace, "lockoutTime", "0")], stop);
            }
            else if (state == "disabled")
            {
                var flags = (int) (row.Number("userAccountControl") ?? NormalAccount) & ~Disabled;
                await ldap.Modify(row.Dn, [new(LdapChange.Replace, "userAccountControl", flags.ToString(CultureInfo.InvariantCulture)), new(LdapChange.Replace, "accountExpires", "0")], stop);
            }
            else if (state == "rejected")
            {
                if (!ldap.Encrypted)
                {
                    throw new ToolProblem(NeedsLdaps);
                }
                var secret = Passwords.Make(64);
                await ldap.Modify(row.Dn, [new(LdapChange.Replace, "unicodePwd", Ldap.Password(secret))], stop);
                Save(f.With(x =>
                {
                    x.Password = secrets.Encrypt(secret);
                    x.PasswordSet = DateTime.UtcNow;
                }));
            }
        }
        catch (LdapRefused e)
        {
            throw new ToolProblem(e.Code == LdapRefused.InsufficientRights ? $"Access denied. {signedIn} does not have permission to change {f.Account}. Sign in as a domain admin."
                : Said(e, signedIn, password: state == "rejected"));
        }
        return (await Check(stop))!;
    }

    public async Task<DateTime> ChangePassword(CancellationToken stop)
    {
        var f = Load();
        if (!f.Rotates || f.Account is null || f.Domain is null)
        {
            throw new ToolProblem($"{Product.Title} does not manage this account's password.", StatusCodes.Status409Conflict);
        }
        var old = secrets.TryDecrypt(f.Password) ?? throw new ToolProblem("The service account password could not be decrypted on this server. Repair the service account.", StatusCodes.Status409Conflict);
        using var ldap = await open(f.Domain, await Host(f.Domain, stop), $"{f.Account}@{f.Domain}", old, stop)
            ?? throw new ToolProblem($"{f.Domain} rejected the password for {f.Account}. Repair the service account first.", StatusCodes.Status409Conflict);
        if (!ldap.Encrypted)
        {
            throw new ToolProblem(NeedsLdaps);
        }
        var row = await Account(ldap, f, ["sAMAccountName"], stop) ?? throw new ToolProblem($"{f.Account} was not found in {f.Domain}.");
        var secret = Passwords.Make(64);
        try
        {
            await ldap.Modify(row.Dn, [new(LdapChange.Replace, "unicodePwd", Ldap.Password(secret))], stop);
        }
        catch (LdapRefused e)
        {
            throw new ToolProblem(Said(e, f.Account, password: true));
        }
        var at = DateTime.UtcNow;
        // Kept straight away: until it is, only Active Directory knows the new password.
        Save(f.With(x =>
        {
            x.Password = secrets.Encrypt(secret);
            x.PasswordSet = at;
        }));
        return at + PasswordLife;
    }

    public bool PasswordDue(DateTime now) => Load() is { Rotates: true } f && (f.PasswordSet ?? f.At ?? now) + PasswordLife <= now;

    /// <summary>A newly set up account, kept: where new things go carries over from the one before where its scope still covers them, and it starts out working.</summary>
    private void Connected(DirectoryToolsFile next)
    {
        var was = Load();
        string? Keep(string? dn) => dn is not null && string.Equals(was.Domain, next.Domain, StringComparison.OrdinalIgnoreCase) && Ldap.Under(dn, next.Scope!) ? dn : null;
        next.NewUsers = Keep(was.NewUsers);
        next.NewGroups = Keep(was.NewGroups);
        next.NewComputers = Keep(was.NewComputers);
        health = new ServiceHealth("ok", null, DateTime.UtcNow);
        Save(next);
    }

    private static async Task<LdapRow?> Account(ILdap ldap, DirectoryToolsFile f, string[] attributes, CancellationToken stop) =>
        (await ldap.Find(ldap.BaseDn, $"(&(objectClass=user)(sAMAccountName={ActiveDirectory.Escape(f.Account!)}))", true, attributes, 1, stop)).FirstOrDefault();

    /// <summary>The Domain Admins group, found by its RID: its name can be translated or changed.</summary>
    private static async Task<LdapRow> DomainAdmins(ILdap ldap, CancellationToken stop)
    {
        var domain = SidText((await ldap.Find(ldap.BaseDn, "(objectClass=*)", false, ["objectSid"], 1, stop)).FirstOrDefault()?.Bytes("objectSid"));
        var group = domain is null ? null
            : (await ldap.Find(ldap.BaseDn, $"(&(objectClass=group)(objectSid={domain}-{DomainAdminsRid}))", true, ["cn"], 1, stop)).FirstOrDefault();
        return group ?? throw new ToolProblem("The Domain Admins group was not found.");
    }

    /// <summary>Whether the account is in Domain Admins, through other groups too.</summary>
    private static async Task<bool> IsDomainAdmin(ILdap ldap, string dn, CancellationToken stop)
    {
        var admins = await DomainAdmins(ldap, stop);
        return (await ldap.Find(dn, $"(memberOf:{InChain}:={ActiveDirectory.Escape(admins.Dn)})", false, ["cn"], 1, stop)).Count > 0;
    }
}

/// <summary>
/// Tests the service account every 15 minutes. When its sign-in starts failing it says so in the activity log, which the
/// alert for a service account that stops working goes off on. It also changes the password of the domain admin Rabbid
/// Lab made, every 30 days.
/// </summary>
public sealed class ServiceAccountWatch(IServiceAccount account, ActivityLog log, TimeProvider clock, ILogger<ServiceAccountWatch> logger) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), clock, stop); // after the server is up
            using var timer = new PeriodicTimer(Every, clock);
            do
            {
                await Look(stop);
            }
            while (await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException)
        {
            // The server is stopping.
        }
    }

    public async Task Look(CancellationToken stop)
    {
        try
        {
            var was = account.Health?.State;
            var now = await account.Check(stop);
            if (now is null)
            {
                return;
            }
            if (now.State != "ok" && was != now.State)
            {
                log.Record(Product.Title, null, "users", "ad.service.failed", "The Active Directory service account sign-in failed", "failed", $"{now.Text} Active Directory pages are read-only until it is fixed in Settings, Active Directory.");
            }
            else if (now.State == "ok" && was is not null and not "ok")
            {
                log.Record(Product.Title, null, "users", "ad.service.ok", "The Active Directory service account signs in again", "ok");
            }
            if (now.State == "ok" && account.PasswordDue(clock.GetUtcNow().UtcDateTime))
            {
                var next = await account.ChangePassword(stop);
                log.Record(Product.Title, null, "users", "ad.service.password", "Changed the Active Directory service account password", "ok", $"Changed every 30 days. Next change {next:yyyy-MM-dd}.");
            }
        }
        catch (Exception e) when (e is ToolProblem or DirectoryProblem or LdapRefused)
        {
            logger.LogWarning("The service account check failed: {Message}", e.Message);
        }
    }
}

public static class ServiceAccountApi
{
    public static void MapServiceAccountApi(this WebApplication app)
    {
        // Test sign-in in the set-up window: signs in with what was typed and changes nothing.
        app.MapPost("/api/ad/test", (SetupForm form, HttpContext context, IServiceAccount account) => UsersApi.Try(context, Permissions.AdConnect, async () =>
            Results.Ok(await account.TestSignIn(form, context.RequestAborted)), change: false));

        // Test on the Settings card: signs in as the service account now.
        app.MapPost("/api/ad/service/test", (HttpContext context, IServiceAccount account, IDirectoryTools tools) => UsersApi.Try(context, Permissions.AdConnect, async () =>
        {
            var health = await account.Check(context.RequestAborted);
            return Results.Ok(new { health, state = tools.State() });
        }, change: false));

        app.MapPost("/api/ad/service/repair", (RepairForm form, HttpContext context, IServiceAccount account, IDirectoryTools tools, ActivityLog log) =>
            UsersApi.Try(context, Permissions.AdConnect, async () =>
            {
                var was = account.Health?.State;
                var health = await account.Repair(form, context.RequestAborted);
                var name = tools.State()?.Account;
                log.Record(context, "users", "ad.service.repair", was switch
                {
                    "locked" => $"Unlocked the AD service account {name}",
                    "disabled" => $"Enabled the AD service account {name}",
                    _ => $"Repaired the AD service account {name} with a new password",
                }, health.State == "ok" ? "ok" : "warning", health.Text);
                return Results.Ok(new { health, state = tools.State() });
            }, confirm: true));

        app.MapPost("/api/ad/service/password", (HttpContext context, IServiceAccount account, IDirectoryTools tools, ActivityLog log) =>
            UsersApi.Try(context, Permissions.AdConnect, async () =>
            {
                var next = await account.ChangePassword(context.RequestAborted);
                log.Record(context, "users", "ad.service.password", $"Changed the AD service account password for {tools.State()?.Account}");
                return Results.Ok(new { next, state = tools.State() });
            }, confirm: true));
    }
}
