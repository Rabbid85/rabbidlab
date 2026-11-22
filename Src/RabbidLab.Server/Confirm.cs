using System.Collections.Concurrent;
using System.Security.Claims;

namespace RabbidLab.Server;

/// <summary>
/// Asking who it is again before something that runs on computers as SYSTEM: a code from their authenticator app when
/// they have MFA on, otherwise their password. Good for ten minutes in the session it was given in, so a browser left
/// signed in can't run a script by itself. Five wrong tries in ten minutes, then they wait.
/// </summary>
public sealed class Confirmations(TimeProvider clock, Mfa mfa, Accounts accounts, IWindowsAccounts windows, IDirectory directory, IDomainFinder finder, Roles roles)
{
    public static readonly TimeSpan Life = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, DateTimeOffset> until = new();
    private readonly Limiter limiter = new(5, clock);

    /// <summary>What this person is asked for: "code" with MFA on, otherwise "password".</summary>
    public string Method(ClaimsPrincipal user) => mfa.Has(Who.Of(user)) ? "code" : "password";

    /// <summary>Until when this session's confirmation lasts, or null when it has none.</summary>
    public DateTimeOffset? Until(HttpContext context) =>
        Sessions.Current(context) is { } id && until.TryGetValue(id, out var t) && t > clock.GetUtcNow() ? t : null;

    /// <summary>The answer to send when this session hasn't confirmed lately (the page then asks), or null when it has.</summary>
    public IResult? Refuse(HttpContext context) => Until(context) is not null ? null
        : Results.Json(new { error = "Verify your identity to continue.", confirm = Method(context.User) }, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>Why it isn't confirmed, or null when it is.</summary>
    public async Task<string?> Confirm(HttpContext context, string? code, string? password, CancellationToken stop)
    {
        if (Sessions.Current(context) is not { } session)
        {
            return "Your session has ended. Sign in again, then try again.";
        }
        var who = Who.Of(context.User);
        if (mfa.Has(who))
        {
            switch (mfa.Check(who, code))
            {
                case MfaAnswer.Wait:
                    return MfaApi.TooMany;
                case MfaAnswer.Wrong:
                    return MfaApi.WrongCode;
            }
        }
        else
        {
            var key = $"{who.Source}\n{who.Username}".ToLowerInvariant();
            if (!limiter.Take(key))
            {
                return "Too many incorrect passwords. Wait a few minutes, then try again.";
            }
            if (await Password(context.User, who, password ?? "", stop) is { } problem)
            {
                return problem;
            }
            limiter.GiveBack(key);
        }
        var now = clock.GetUtcNow();
        foreach (var old in until.Where(u => u.Value < now).Select(u => u.Key).ToList())
        {
            until.TryRemove(old, out _);
        }
        until[session] = now + Life;
        return null;
    }

    // The same check as signing in, by where the account is from.
    private async Task<string?> Password(ClaimsPrincipal user, Who who, string password, CancellationToken stop)
    {
        const string wrong = "The password is incorrect.";
        if (password.Length == 0)
        {
            return "Enter your password.";
        }
        switch (who.Source)
        {
            case SignIn.Local:
                return accounts.Check(who.Username, password) is null ? wrong : null;
            case SignIn.Windows:
                return await windows.Administrator(user.FindFirstValue("name") ?? "", password, stop) is null ? wrong : null;
            default:
                if ((who.Domain ?? accounts.Directory?.Domain) is not { } domain)
                {
                    return "Your session has ended. Sign in again, then try again.";
                }
                // Accounts from another domain carry it in their name (awiles@lab.amw.com).
                var name = who.Username.EndsWith("@" + domain, StringComparison.OrdinalIgnoreCase) ? who.Username[..^(domain.Length + 1)] : who.Username;
                try
                {
                    var host = await finder.Host(domain, user.FindFirstValue("server"), stop);
                    return await directory.SignIn(domain, host, roles.Groups(), name, password, stop) is null ? wrong : null;
                }
                catch (DirectoryProblem e)
                {
                    return $"Could not verify your password with {domain}: {e.Message}";
                }
        }
    }
}

public sealed record ConfirmForm(string? Code = null, string? Password = null);
