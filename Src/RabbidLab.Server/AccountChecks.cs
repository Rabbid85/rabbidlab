using System.Globalization;
using System.Security.Claims;

namespace RabbidLab.Server;

/// <summary>
/// Active Directory and Windows accounts are asked about again every few minutes while their sessions last. One that was
/// deleted or disabled, whose password was changed or reset, or that is in none of the roles any more is signed out, and a
/// change in its groups changes what the session may do. Rabbid Lab's own accounts don't need this: they're checked on
/// every request. When the domain or Windows can't be asked, the session goes on as it was.
/// </summary>
public sealed class AccountChecks(IDirectory directory, IWindowsAccounts windows, IDomainFinder finder, Accounts accounts, Roles roles, SignInLocks locks, TimeProvider clock)
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);
    private const int Most = 2000;

    /// <summary>The claim with the account's password-set time at sign-in, to tell a changed password by.</summary>
    public const string PasswordClaim = "pwd";

    /// <summary>Why the session ends (null when it goes on), and its groups now when they changed.</summary>
    public sealed record Verdict(string? Problem, IReadOnlyList<string>? Groups);

    private static readonly Verdict Fine = new(null, null);

    private sealed record Reading(bool Found, bool Enabled, bool Admin, string? PasswordSet, IReadOnlyList<string>? Groups);

    private readonly Lock gate = new();
    private readonly Dictionary<string, (DateTimeOffset At, Task<Reading?> Answer)> known = [];

    /// <summary>What to do with this session now. Asked of the domain or Windows at most every five minutes per account.</summary>
    public async Task<Verdict> Check(ClaimsPrincipal p, CancellationToken stop)
    {
        var who = Who.Of(p);
        if (who.Source is not (SignIn.Directory or SignIn.Windows))
        {
            return Fine;
        }
        IReadOnlyList<string> asked = who.Source == SignIn.Directory ? roles.Groups() : [];
        var key = string.Join('\n', [who.Source, who.Username, who.Domain ?? "", .. asked.Order(StringComparer.OrdinalIgnoreCase)]).ToLowerInvariant();
        var now = clock.GetUtcNow();
        Task<Reading?> answer;
        lock (gate)
        {
            if (!known.TryGetValue(key, out var last) || now - last.At >= Every)
            {
                if (known.Count >= Most)
                {
                    foreach (var old in known.Where(k => now - k.Value.At >= Every).Select(k => k.Key).ToList())
                    {
                        known.Remove(old);
                    }
                }
                known[key] = last = (now, Task.Run(() => Read(p, who, asked)));
            }
            answer = last.Answer;
        }
        Reading? r;
        try
        {
            r = await answer.WaitAsync(Wait, stop);
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
            return Fine;
        }
        return r is null ? Fine : Judge(p, who, r);
    }

    /// <summary>Forgets what was read, so the next request asks again (after a change made here, say).</summary>
    public void Forget()
    {
        lock (gate)
        {
            known.Clear();
        }
    }

    private async Task<Reading?> Read(ClaimsPrincipal p, Who who, IReadOnlyList<string> asked)
    {
        using var timeout = new CancellationTokenSource(Wait);
        try
        {
            if (who.Source == SignIn.Windows)
            {
                var w = await windows.Recheck(p.FindFirstValue("name") ?? "", timeout.Token);
                return w is null ? null : new Reading(w.Found, w.Enabled, w.Admin, w.PasswordSet?.ToString(CultureInfo.InvariantCulture), null);
            }
            if ((who.Domain ?? accounts.Directory?.Domain) is not { } domain)
            {
                return null;
            }
            // Accounts from another domain carry it in their name (awiles@lab.amw.com).
            var name = who.Username.EndsWith("@" + domain, StringComparison.OrdinalIgnoreCase) ? who.Username[..^(domain.Length + 1)] : who.Username;
            var host = await finder.Host(domain, p.FindFirstValue("server"), timeout.Token);
            var d = await directory.Recheck(domain, host, name, asked, timeout.Token);
            return d is null ? null : new Reading(d.Found, d.Enabled, true, d.PasswordSet?.ToString(CultureInfo.InvariantCulture), d.Groups);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null; // couldn't ask: the session goes on
        }
    }

    private Verdict Judge(ClaimsPrincipal p, Who who, Reading r)
    {
        var windowsAccount = who.Source == SignIn.Windows;
        if (!r.Found)
        {
            return new($"The account no longer exists in {(windowsAccount ? Environment.MachineName : who.Domain)}.", null);
        }
        if (!r.Enabled)
        {
            return new("The account was disabled.", null);
        }
        if (!r.Admin)
        {
            return new($"The account is no longer a member of the Administrators group on {Environment.MachineName}.", null);
        }
        if (p.FindFirstValue(PasswordClaim) is { } was && r.PasswordSet is { } set && was != set)
        {
            locks.ForgetBrowsers(SignInLocks.Key(who.Source, who.Username)); // the browsers remembered for it are strangers again
            return new("The account's password was changed or reset.", null);
        }
        if (r.Groups is not { } groups)
        {
            return Fine;
        }
        if (!roles.CanSignIn(who with { Groups = groups }))
        {
            return new($"The account is no longer assigned to any {Product.Title} role.", null);
        }
        return who.Groups.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(groups) ? Fine : new(null, groups);
    }
}
