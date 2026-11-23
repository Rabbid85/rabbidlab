using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RabbidLab.Server;

/// <summary>
/// Signing in, on one page (signin.html). The first visit sets Rabbid Lab up there: a local administrator account, or
/// Active Directory. After that the same form signs people in, with a cookie that scripts can't read and other sites don't
/// send. Changing a password or the directory settings signs out the sessions that used them.
/// </summary>
public static class SignIn
{
    // Release and Dev both run on localhost, and browsers don't keep cookies apart by port.
    public static readonly string Cookie = Product.Dev ? "rabbidlab-dev" : "rabbidlab";
    // Kept by a browser that has signed in: a token that says so (SignInLocks), nothing more. Scripts can't read it either.
    public static readonly string Known = Cookie + "-known";
    // Sessions aren't kept past the browser: an open console checks in every minute or less, so this ends soon after its last page closes.
    public static readonly TimeSpan Session = TimeSpan.FromMinutes(5);
    // Active Directory and Windows accounts sign in again this long after they did, so a change there counts by then.
    public static readonly TimeSpan Longest = TimeSpan.FromDays(7);
    public const int MaxUsername = 256;
    public const string Local = "local";
    public const string Directory = "ad";
    public const string Windows = "windows"; // an administrator of this computer, with its own Windows account

    public static void AddSignIn(this IServiceCollection services)
    {
        services.AddSingleton<Accounts>();
        services.AddSingleton<Roles>();
        services.AddSingleton<SignInLimiter>();
        services.AddSingleton<SignInLocks>();
        services.AddSingleton<SignInFlood>();
        services.AddSingleton<Mfa>();
        services.AddSingleton<PendingSignIns>();
        services.AddSingleton<RiskNotice>();
        services.AddSingleton<AccountChecks>();
        services.AddSingleton<IDirectory, ActiveDirectory>();
        services.AddSingleton<IWindowsAccounts, WindowsAccounts>();
        services.AddMemoryCache();
        services.AddSingleton<IDomainFinder, DomainFinder>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<Sessions>();
        services.AddSingleton<Confirmations>();
        // The cookie carries only the session's id; the session itself is kept by Sessions.
        // When the console is HTTPS, the cookie says so too: a browser never sends it over plain HTTP.
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<Sessions, IConfiguration>((options, sessions, config) =>
            {
                options.SessionStore = sessions;
                if (HttpsOnly(config))
                {
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                }
            });
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = Cookie;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = Session;
            options.SlidingExpiration = true;
            // A session ends when its password or the directory settings change, or when an Active Directory account is in no role any more.
            options.Events.OnValidatePrincipal = async context =>
            {
                var http = context.HttpContext;
                async Task SignOut()
                {
                    context.RejectPrincipal();
                    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
                if (context.Principal is not { } p)
                {
                    await SignOut();
                    return;
                }
                var accounts = http.RequestServices.GetRequiredService<Accounts>();
                var roles = http.RequestServices.GetRequiredService<Roles>();
                var sessions = http.RequestServices.GetRequiredService<Sessions>();
                var id = Sessions.Current(http);
                var who = Who.Of(p);
                var source = p.FindFirstValue("source");
                var mfa = p.FindFirstValue("mfa");
                var stamp = p.FindFirstValue("stamp");
                var current = source switch
                {
                    Directory => accounts.Directory?.Stamp,
                    Windows => Windows, // Windows checks the password; the session simply runs its course
                    _ => accounts.Find(p.Identity?.Name)?.Stamp,
                };
                // A session kept past the browser (Keep me signed in, gone since 1.1.172), or one from when they lasted 12 hours, ends now.
                var old = context.Properties.IsPersistent || context.Properties.ExpiresUtc - context.Properties.IssuedUtc > Session;
                // Staying signed in renews a session, but never past Longest for an account Rabbid Lab doesn't keep itself.
                if (source is Directory or Windows && id is not null && sessions.Find(id) is { } session)
                {
                    old |= http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow() - session.Started > Longest;
                }
                // Every sign-in takes a code (Roles.NeedsMfa): a session without one ends, and signing in again asks for it.
                var noCode = mfa is null && roles.NeedsMfa(who) || mfa == Skipped && !MaySkipMfa(http);
                // Its last console tab closed a few seconds ago (Sessions.Tab): whoever opens it again signs in.
                var closed = id is not null && sessions.Closed(id);
                // The Use at your own risk notice changed, or this session is from before there was one: signing in again asks.
                var notice = !http.RequestServices.GetRequiredService<RiskNotice>().Accepted(who);
                if (stamp is null || stamp != current || old || noCode || notice || !roles.CanSignIn(who) || PlainHttp.Exposed(http) || closed || IdleOut(http, p, sessions, id))
                {
                    await SignOut();
                    return;
                }
                // Active Directory and Windows accounts: asked again every few minutes (AccountChecks).
                var verdict = await http.RequestServices.GetRequiredService<AccountChecks>().Check(p, http.RequestAborted);
                if (verdict.Problem is { } problem)
                {
                    if (id is not null && sessions.End(id))
                    {
                        http.RequestServices.GetRequiredService<ActivityLog>().Record(p.Identity?.Name ?? "", ActivityLog.Address(http), "sign-in",
                            "signout.account", "Signed out after an account check", "ok", problem);
                    }
                    await SignOut();
                    return;
                }
                // Its groups changed but it can still sign in: what it may do follows them from now on.
                if (verdict.Groups is { } groups)
                {
                    var claims = p.Claims.Where(c => c.Type != "group").Select(c => new Claim(c.Type, c.Value)).Concat(groups.Select(g => new Claim("group", g)));
                    context.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
                    context.ShouldRenew = true;
                }
            };
        });
    }

    /// <summary>Whether the console only answers over HTTPS (Setup's console.json, or --urls with https only).</summary>
    public static bool HttpsOnly(IConfiguration config)
    {
        var urls = config["Kestrel:Endpoints:Console:Url"] ?? config["urls"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "";
        var all = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return all.Length > 0 && all.All(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether an answer carries HSTS, so the browser only ever uses HTTPS for this name: HTTPS on port 443, by a name.
    /// Not on another port, where a browser sent from http://name would land on a port 443 with nothing on it, and not
    /// for localhost or an IP address.
    /// </summary>
    public static bool Hsts(HttpRequest request)
    {
        var host = request.Host.Host;
        return request.IsHttps && request.Host.Port is null or 443 && Uri.CheckHostName(host) == UriHostNameType.Dns
            && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    // Sign out when idle: the choices, in minutes (0 is never), and the slack the page's once-a-minute report needs.
    public static readonly int[] IdleChoices = [0, 15, 30, 60, 120, 240, 480];
    // How many wrong passwords lock a name: the choices in Settings, Sign-in.
    public static readonly int[] LockChoices = [3, 5, 10];
    private static readonly TimeSpan IdleSlack = TimeSpan.FromMinutes(1);

    public static string IdleWords(int minutes) => minutes % 60 == 0 ? (minutes == 60 ? "1 hour" : $"{minutes / 60} hours") : $"{minutes} minutes";

    /// <summary>
    /// Whether this session went longer than Sign out when idle allows without its person doing anything. The page signs
    /// itself out first; this catches a page that couldn't, such as a sleeping laptop or a tab the browser froze.
    /// </summary>
    private static bool IdleOut(HttpContext context, ClaimsPrincipal p, Sessions sessions, string? id)
    {
        var minutes = context.RequestServices.GetRequiredService<SettingsStore>().Current.IdleMinutes;
        if (minutes <= 0 || id is null || sessions.Find(id) is not { } s
            || context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow() - s.Active <= TimeSpan.FromMinutes(minutes) + IdleSlack)
        {
            return false;
        }
        // Logged once, by whichever request ends it.
        if (sessions.End(id))
        {
            context.RequestServices.GetRequiredService<ActivityLog>().Record(p.Identity?.Name ?? "", ActivityLog.Address(context), "sign-in", "signout.idle",
                $"Signed out after {IdleWords(minutes)} of inactivity", "ok");
        }
        return true;
    }

    /// <summary>Who is asking, then the gate: until someone signs in, pages lead to setup or sign-in and the API says 401.</summary>
    public static void UseSignIn(this WebApplication app)
    {
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var signedIn = context.User.Identity?.IsAuthenticated == true;
            var open = !signedIn && Open(path);
            // The stopper for a script hammering the sign-in (SignInFlood): past its limit, turned away before anything is read.
            // Only requests with the page's own header count: without it they're refused anyway, and another site, which
            // can't send it, can't use up a visitor's requests.
            var signing = HttpMethods.IsPost(context.Request.Method) && (path.StartsWithSegments("/api/signin") || path.StartsWithSegments("/api/setup"));
            if (signing && context.Request.Headers.ContainsKey(Access.Header) && !OnServer(context)
                && !context.RequestServices.GetRequiredService<SignInFlood>().Let(ActivityLog.Address(context), out var justOut))
            {
                if (justOut)
                {
                    context.RequestServices.GetRequiredService<ActivityLog>().Record(ActivityLog.Who(context), ActivityLog.Address(context), "sign-in", "signin.flood",
                        "Sign-in requests blocked for 15 minutes", "failed", $"More than {SignInFlood.PerMinute * 10} requests in one minute from this address");
                }
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = "60";
                await context.Response.WriteAsJsonAsync(new { error = "Too many sign-in requests from this computer. Wait a minute, then try again.", wait = 60 });
                return;
            }
            // What anyone can send is kept small: a client's answer may list a computer's accounts, the rest is a form.
            if (open && TooBig(context, path == "/api/clients/done" || path == "/api/clients/inventory" ? 2 * 1024 * 1024 : 64 * 1024))
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await context.Response.WriteAsJsonAsync(new { error = "The request is too large." });
                return;
            }
            // Plain HTTP from another computer: no password goes in (PlainHttp.cs). Sessions are refused in OnValidatePrincipal.
            if ((signing || (HttpMethods.IsPost(context.Request.Method) && (path == "/api/confirm" || path == "/api/me/password"))) && PlainHttp.Exposed(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = PlainHttp.Refused, plain = true });
                return;
            }
            if (signedIn || open)
            {
                await next(context);
                return;
            }
            if (path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Sign-in required." });
                return;
            }
            context.Response.Redirect("/signin.html"); // setting up and signing in are the same page
        });
    }

    /// <summary>What anyone may open: the sign-in and setup pages with what they use, the health check, and where updating stands.</summary>
    private static bool Open(PathString path) =>
        path == "/signin.html" || path.StartsWithSegments("/Assets") || path.StartsWithSegments("/favicon.svg")
        || path.StartsWithSegments("/favicon-dev.svg") || path == "/api/health" || path.StartsWithSegments("/api/signin")
        || path.StartsWithSegments("/api/setup") || path == "/api/signout" || path.StartsWithSegments("/api/update/watch") || ForClients(path);

    // The Rabbid Lab client's own endpoints: open (they carry the client's id and secret), and never someone working.
    private static readonly HashSet<string> ClientPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/clients/join", "/api/clients/checkin", "/api/clients/vouch", "/api/clients/next", "/api/clients/done", "/api/clients/bye", "/api/clients/inventory", "/api/clients/download",
        "/api/clients/jobs/take", "/api/clients/jobs/done",
    };

    // What an open page asks by itself on a timer.
    private static readonly HashSet<string> Polled = new(StringComparer.OrdinalIgnoreCase) { "/api/update", "/api/clients", "/api/sessions", "/api/links/states", "/api/clients/here" };

    public static bool ForClients(PathString path) => ClientPaths.Contains(path.Value ?? "");

    /// <summary>Whether this request is a signed-in person working (not a poll, the health check or a client), so a new version waits.</summary>
    public static bool Working(HttpContext context)
    {
        var path = context.Request.Path;
        return context.User.Identity?.IsAuthenticated == true && path.StartsWithSegments("/api") && !ForClients(path)
            && !path.StartsWithSegments("/api/update/watch") && path != "/api/health"
            && !(HttpMethods.IsGet(context.Request.Method) && Polled.Contains(path.Value ?? ""));
    }

    /// <summary>Whether the request says its body is over the limit; the server cuts off one that doesn't say.</summary>
    private static bool TooBig(HttpContext context, long limit)
    {
        if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } body)
        {
            body.MaxRequestBodySize = limit;
        }
        return context.Request.ContentLength > limit;
    }

    public static void MapSignIn(this WebApplication app)
    {
        // What the sign-in and setup pages need: whether it's set up, how people sign in, and who is signed in.
        app.MapGet("/api/signin", (HttpContext context, Accounts accounts, Roles roles, SettingsStore settings) => Results.Ok(new
        {
            product = Product.Name,
            version = Product.Version,
            dev = Product.Dev,
            platform = Product.Platform,
            machine = Environment.MachineName,
            setUp = accounts.SetUp,
            domain = accounts.Directory?.Domain,
            server = accounts.Directory?.Server,
            thisDomain = accounts.SetUp ? null : ActiveDirectory.ThisDomain(),
            user = context.User.Identity?.IsAuthenticated == true ? Me(context.User, roles) : null,
            brand = Brand.Read(settings.Current), // the lab's own name, icon and accent, so the sign-in page wears them too
            idle = settings.Current.IdleMinutes, // Sign out when idle, so the page can warn before it happens
            plain = PlainHttp.Said(context), // plain HTTP from another computer: refused, or allowed by AllowPlainHttp
            notice = RiskNotice.Shape(), // the Use at your own risk notice, shown as the page opens until this browser accepts it
        }));

        // The sign-in page's Change: the domains accounts may sign in from, and a domain's controllers and which ones answer now.
        app.MapGet("/api/signin/domains", async (Accounts accounts, IDirectory directory, IMemoryCache cache, CancellationToken stop) =>
            accounts.Directory is { } d ? Results.Ok(new { domains = await Allowed(cache, directory, d.Domain, stop) }) : Results.NotFound());
        app.MapGet("/api/signin/controllers", async (string? domain, Accounts accounts, IDirectory directory, IDomainFinder finder, IMemoryCache cache, CancellationToken stop) =>
        {
            if (accounts.Directory is not { } d)
            {
                return Results.NotFound();
            }
            var wanted = string.IsNullOrWhiteSpace(domain) ? d.Domain : Clean(domain);
            if (wanted != d.Domain && !(await Allowed(cache, directory, d.Domain, stop)).Contains(wanted))
            {
                return Results.BadRequest(new { error = NotTrusted(wanted, d.Domain) });
            }
            // None answering: why, when the server can tell (a VPN, names it can't look up, or no route), for the page's warning.
            var found = await finder.Controllers(wanted, stop);
            return Results.Ok(new { controllers = found, why = await finder.WhyNone(wanted, found, stop) });
        });

        // First run, a local administrator account.
        app.MapPost("/api/setup/local", (HttpContext context, Accounts accounts, Roles roles, ActivityLog log, Mfa mfa, PendingSignIns pending, LocalSetupForm form) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            var (account, problem) = accounts.SetUpLocal(form.Username, form.Password);
            if (account is null)
            {
                return accounts.SetUp ? Results.Conflict(new { error = problem }) : Results.BadRequest(new { error = problem });
            }
            roles.SetUp(new Member { Kind = Member.Local, Name = account.Username }, null);
            log.Record(account.Username, ActivityLog.Address(context), "sign-in", "setup.local", $"Set up {Product.Title} with a local account", "ok");
            // MFA is required for everyone, the person setting it up too: they scan the code before they're signed in.
            return SetUpMfa(Principal(account.Username, account.Username, Local, account.Stamp), account.Username, mfa, pending, MaySkipMfa(context));
        });

        // First run, the Domain and Domain controller boxes: the domains this machine can find, and a domain's controllers.
        app.MapGet("/api/setup/directory/domains", async (Accounts accounts, IDomainFinder finder, CancellationToken stop) =>
            accounts.SetUp ? Results.NotFound() : Results.Ok(new { domains = await finder.Domains(stop), thisDomain = ActiveDirectory.ThisDomain() }));
        app.MapGet("/api/setup/directory/controllers", async (string? domain, Accounts accounts, IDomainFinder finder, CancellationToken stop) =>
            accounts.SetUp ? Results.NotFound()
            : DomainProblem(domain) is { } problem ? Results.BadRequest(new { error = problem })
            : Results.Ok(new { controllers = await finder.Controllers(Clean(domain), stop) }));

        // First run, Active Directory: the person setting it up signs in, and becomes the administrator.
        app.MapPost("/api/setup/directory", async (HttpContext context, Accounts accounts, Roles roles, ActivityLog log, IDirectory directory,
            IDomainFinder finder, Mfa mfa, PendingSignIns pending, DirectorySetupForm form) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            var from = ActivityLog.Address(context);
            if (accounts.SetUp)
            {
                return Results.Conflict(new { error = Product.Title + " is already set up. Sign in instead." });
            }
            var domain = Clean(form.Domain);
            if ((DomainProblem(domain) ?? ServerProblem(form.Server)) is { } badDomain)
            {
                return Results.BadRequest(new { error = badDomain });
            }
            var server = string.IsNullOrWhiteSpace(form.Server) ? null : form.Server.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(form.Username) || form.Username.Length > MaxUsername || string.IsNullOrEmpty(form.Password))
            {
                return Results.BadRequest(new { error = "Enter your domain username and password." });
            }
            DirectoryUser? user;
            string setupHost;
            try
            {
                setupHost = await finder.Host(domain, server, context.RequestAborted);
                user = await directory.SignIn(domain, setupHost, [], form.Username, form.Password, context.RequestAborted);
            }
            catch (DirectoryProblem e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
            if (user is null)
            {
                return Results.Json(new { error = "The username or password is incorrect." }, statusCode: StatusCodes.Status401Unauthorized);
            }
            if (accounts.SetUpDirectory(domain, server) is { } problem)
            {
                return Results.Conflict(new { error = problem });
            }
            log.Record(user.Username, from, "sign-in", "setup.directory", $"Set up {Product.Title} with Active Directory ({domain}, domain controller: {server ?? "Automatic"}, all domain users have the User role)", "ok");
            // Whoever sets it up is the administrator; everyone in the domain gets User.
            roles.SetUp(new Member { Kind = Member.Person, Name = user.Username, Display = user.Name }, new Member { Kind = Member.Everyone, Name = "ad" });
            var principal = Principal(user.Username, user.Name, Directory, accounts.Directory!.Stamp, domain, setupHost, user.Groups, Stamp(user.PasswordSet));
            // MFA is required for everyone, the person setting it up too: they scan the code before they're signed in.
            return SetUpMfa(principal, user.Name, mfa, pending, MaySkipMfa(context));
        }).AddEndpointFilter(Limited);

        // Rabbid Lab's own accounts for .\name or Local account, otherwise Active Directory when it's set up. Wrong
        // passwords are counted for the name typed (SignInLocks): each makes the next try wait longer, and enough lock it.
        app.MapPost("/api/signin", async (HttpContext context, Accounts accounts, Roles roles, ActivityLog log, IDirectory directory, IDomainFinder finder,
            IWindowsAccounts windows, IMemoryCache cache, Mfa mfa, PendingSignIns pending, SignInLocks locks, SettingsStore store, SignInForm form) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            var from = ActivityLog.Address(context);
            var name = form.Username?.Trim() ?? "";
            if (name.Length > MaxUsername)
            {
                return Results.BadRequest(new { error = "The username is too long." });
            }
            // .\name is one of Rabbid Lab's own accounts, the way Windows uses .\ for the computer itself.
            var dotted = name.StartsWith(@".\", StringComparison.Ordinal) || name.StartsWith("./", StringComparison.Ordinal);
            name = dotted ? name[2..] : name;
            var settings = accounts.Directory;
            var own = form.Local || dotted || settings is null; // Rabbid Lab's own accounts and this computer's, not the domain's
            var key = name.Length == 0 ? null : SignInLocks.Key(own ? Local : Directory, name);
            // Too soon after a wrong password, or locked: said before any password is looked at, and the same for a name that isn't
            // an account. A try that may go ahead takes the name's turn until this request is answered, so tries sent together
            // wait for each other and each is counted before the next is checked.
            var asker = key is null ? default : Asking(context, locks, key);
            LockAnswer standing = default;
            using var turn = key is null ? null : locks.Begin(key, asker, out standing);
            // From here a try is seen through and counted even if whoever sent it hangs up.
            var stop = CancellationToken.None;
            // A locked name sent with a code is an administrator's way back in: the code is checked first, then the password.
            var unlocking = standing.State == LockState.Locked && !string.IsNullOrWhiteSpace(form.Code);
            if (standing.State == LockState.Locked && !unlocking)
            {
                return LockedOut(name);
            }
            if (standing.Seconds > 0)
            {
                return TooSoon(standing.Seconds, unlocking);
            }
            var most = LockChoices.Contains(store.Current.LockAfter) ? store.Current.LockAfter : SignInLocks.Most; // Settings, Sign-in: 3, 5 or 10
            // Unlocking: whose code it is, once it's right. Without the app nothing else is looked at, and no password goes to the domain.
            Who? proven = null;
            bool Coded(Who asked)
            {
                proven = mfa.Check(asked, form.Code) == MfaAnswer.Right ? asked : null;
                return proven is not null;
            }
            // A wrong password (unlocking, a wrong code too): logged, counted for the name, and answered with the wait before the next
            // try. Unlocking with the code right and the password wrong counts for more: whoever sent it has the authenticator.
            IResult Failed(string by, bool known, string? detail = null)
            {
                log.Record(by, from, "sign-in", "signin.failed", "Sign-in failed", "failed", detail);
                if (key is null)
                {
                    return Results.Json(new { error = WrongText }, statusCode: StatusCodes.Status401Unauthorized);
                }
                var now = locks.Wrong(key, from, asker, known, most, heavy: unlocking && proven is not null);
                if (now.Just)
                {
                    log.Record(by, from, "sign-in", "signin.locked", "Locked out", "failed", "Too many incorrect passwords");
                }
                var locked = now.State == LockState.Locked;
                return Results.Json(new { error = unlocking ? WrongWithCode : locked ? LockedText(name) : WrongText, wait = now.Seconds, locked }, statusCode: StatusCodes.Status401Unauthorized);
            }
            // The password was right. With MFA the page gets a ticket to send its code with, and nobody is signed in yet;
            // someone who hasn't set it up gets the secret to set it up with first, and after a reset also needs its code.
            async Task<IResult> Enter(ClaimsPrincipal principal, string shown)
            {
                var who = Who.Of(principal);
                // Counted under the account's own name too, so another spelling of it gets around neither its lock nor its count.
                var counted = SignInLocks.Key(who.Source, who.Username);
                if (counted != key && locks.Check(counted, Asking(context, locks, counted)).State != LockState.Open)
                {
                    // Typed another way than the account is named, and the account is locked or waiting: answered like a wrong
                    // password, so the other spelling can't be used to find out whether a password is right.
                    return Failed(who.Username, true, "Correct password, but the account is locked out or delayed under its own username");
                }
                string[] keys = key is null || key == counted ? [counted] : [counted, key];
                if (unlocking)
                {
                    // Password and code were both right. Only an administrator unlocks itself this way, and only with its own code.
                    if (proven is null || proven.Source != who.Source || !string.Equals(proven.Username, who.Username, StringComparison.OrdinalIgnoreCase)
                        || !roles.CanSignIn(who) || !roles.Of(who).Any(r => r.Id == Roles.Admin))
                    {
                        return Failed(who.Username, true, "Locked out. The code does not belong to this administrator.");
                    }
                    locks.Unlock(key!);
                    ((ClaimsIdentity) principal.Identity!).AddClaim(new Claim("mfa", "1"));
                    log.Record(who.Username, from, "sign-in", "signin.unlock", "Unlocked their own sign-in", "ok", "Using the password and the authenticator code");
                    return await Finish(context, roles, log, locks, principal, shown, keys, notice: form.Notice);
                }
                if (mfa.Has(who))
                {
                    return Results.Ok(new { mfa = "code", ticket = pending.Add(principal, shown, null, keys), skip = MaySkipMfa(context) });
                }
                if (roles.NeedsMfa(who))
                {
                    return SetUpMfa(principal, shown, mfa, pending, MaySkipMfa(context), keys);
                }
                return await Finish(context, roles, log, locks, principal, shown, keys, notice: form.Notice);
            }
            if (own)
            {
                var mine = accounts.Find(name);
                if (unlocking && !Coded(mine is not null ? new Who(Local, mine.Username, null, [])
                    : new Who(Windows, $"{Environment.MachineName}\\{Ldap.Account(name)}", Environment.MachineName, [])))
                {
                    return Failed(mine is not null ? name : Unknown, mine is not null, "Locked out. The code is incorrect.");
                }
                // Rabbid Lab's own accounts, then, on Windows, an administrator of this computer with its own Windows account.
                // A name with a domain in it (AMW\name, name@domain) is this computer's only when the domain is this computer:
                // any other is never sent to Windows from here, where it would be checked with the domain behind the domain's lock.
                var computer = Environment.MachineName + @"\";
                var onThisComputer = name.StartsWith(computer, StringComparison.OrdinalIgnoreCase) ? name[computer.Length..]
                    : name.Contains('\\') || name.Contains('@') ? "" : name;
                ClaimsPrincipal? principal = null;
                if (accounts.Check(name, form.Password) is { } account)
                {
                    principal = Principal(account.Username, account.Username, Local, account.Stamp);
                }
                else if (await windows.Administrator(onThisComputer, form.Password ?? "", stop) is { } admin)
                {
                    var now = await windows.Recheck(admin, stop);
                    principal = Principal($"{Environment.MachineName}\\{admin}", admin, Windows, Windows, Environment.MachineName, password: Stamp(now?.PasswordSet));
                }
                if (principal is null)
                {
                    // Under its name only when that's an account here: what was typed may be a password in the wrong box.
                    // Windows is asked either way, so a name that is an account here answers no sooner than one that isn't.
                    var onWindows = await windows.Recheck(onThisComputer, stop) is { Found: true };
                    var known = mine is not null || onWindows;
                    return Failed(known ? name : Unknown, known);
                }
                return await Enter(principal, principal.FindFirstValue("name") ?? name);
            }
            // Another domain only when the one set up shares its forest or trusts it, so nobody can send the check to a domain of their own.
            var domain = string.IsNullOrWhiteSpace(form.Domain) ? settings!.Domain : Clean(form.Domain);
            if (domain != settings!.Domain && !(await Allowed(cache, directory, settings.Domain, context.RequestAborted)).Contains(domain))
            {
                return Results.BadRequest(new { error = NotTrusted(domain, settings.Domain) });
            }
            // A domain controller picked for this sign-in must be one DNS lists for the domain, so nobody can send the password check elsewhere.
            var chosen = form.Server?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(chosen) && !(await finder.Controllers(domain, context.RequestAborted)).Any(c => c.Name == chosen))
            {
                return Results.BadRequest(new { error = $"{chosen} is not a domain controller in {domain}." });
            }
            // The account's name for the log when it has signed in here before, otherwise Unknown user.
            var logged = KnownIn(roles, name, domain, settings.Domain);
            var sam = Ldap.Account(name);
            if (unlocking && !Coded(new Who(Directory, domain == settings.Domain ? sam : $"{sam}@{domain}", domain, [])))
            {
                return Failed(logged, logged != Unknown, "Locked out. The code is incorrect.");
            }
            DirectoryUser? user;
            string host;
            try
            {
                // The controller set up for the domain unless another is picked; other domains start on Automatic.
                var server = !string.IsNullOrEmpty(chosen) ? chosen : domain == settings.Domain ? settings.Server : null;
                host = await finder.Host(domain, server, stop);
                // Every wrong password here is a failed logon in the domain too: locked here one short of where the domain locks the account itself.
                most = await Most(cache, directory, domain, most, stop);
                user = await directory.SignIn(domain, host, roles.Groups(), name, form.Password ?? "", stop);
            }
            catch (DirectoryProblem e)
            {
                log.Record(logged, from, "sign-in", "signin.failed", "Sign-in failed", "failed", e.Message);
                return Results.Json(new { error = $"Could not verify your password with {domain}: {e.Message}" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            if (user is null)
            {
                // Whether it's an account in the domain (asked as this computer, for every wrong password alike): one that is gets
                // kept by name when it locks, so it shows in Settings and can be unlocked.
                var found = await directory.Recheck(domain, host, sam, [], stop) is { Found: true };
                return Failed(logged, found || logged != Unknown);
            }
            // Accounts from another domain keep it in their name, so awiles@lab.amw.com isn't taken for amw.com's awiles.
            var username = domain == settings.Domain ? user.Username : $"{user.Username}@{domain}";
            // In at least one role, or no way in.
            var who = new Who(Directory, username, domain, user.Groups);
            if (!roles.CanSignIn(who))
            {
                log.Record(username, from, "sign-in", "signin.refused", "Sign-in denied", "failed", "Not assigned to any role");
                return Results.Json(new { error = $"Your account is not assigned to a role in {Product.Title}. Ask an administrator to add you to a role." },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            return await Enter(Principal(username, user.Name, Directory, settings.Stamp, domain, host, user.Groups, Stamp(user.PasswordSet)), user.Name);
        }).AddEndpointFilter(Limited);

        // The second step: the code from the authenticator app, with the ticket the password step gave. When the app is
        // being set up, the first code also turns MFA on.
        app.MapPost("/api/signin/mfa", async (HttpContext context, Roles roles, ActivityLog log, Mfa mfa, PendingSignIns pending, SignInLocks locks, SettingsStore store, MfaSignInForm form) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            // A notice's ticket is past the code already: it's only good at /api/signin/notice.
            if (pending.Find(form.Ticket) is not { Notice: null } waiting)
            {
                return Results.BadRequest(new { error = "The sign-in timed out. Sign in again.", again = true });
            }
            var from = ActivityLog.Address(context);
            var who = Who.Of(waiting.Principal);
            var username = waiting.Principal.Identity!.Name!;
            // Locked since the password was typed: back to the password step, which says so.
            if (waiting.Keys.Any(k => locks.Check(k, Asking(context, locks, k)).State == LockState.Locked))
            {
                pending.Remove(form.Ticket!);
                return Results.BadRequest(new { error = "Sign in again.", again = true });
            }
            // A wrong code counts for the name like a wrong password, so a right password can't be followed by endless codes.
            IResult WrongCode(string error, string detail)
            {
                log.Record(username, from, "sign-in", "signin.failed", "Sign-in failed", "failed", detail);
                if (waiting.Keys.Count == 0 || !locks.Wrong(waiting.Keys[0], from, Asking(context, locks, waiting.Keys[0]), known: true, most: store.Current.LockAfter).Just)
                {
                    return Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized);
                }
                pending.Remove(form.Ticket!);
                log.Record(username, from, "sign-in", "signin.locked", "Locked out", "failed", "Too many incorrect passwords and codes");
                return Results.Json(new { error = LockedText(username), again = true }, statusCode: StatusCodes.Status401Unauthorized);
            }
            // Skip MFA this time: Rabbid Lab Dev, while Settings, Developer allows it. Nothing is set up.
            if (form.Skip)
            {
                if (!MaySkipMfa(context))
                {
                    return Results.Json(new { error = "MFA cannot be skipped." }, statusCode: StatusCodes.Status403Forbidden);
                }
                pending.Remove(form.Ticket!);
                if (!roles.CanSignIn(who))
                {
                    return Results.BadRequest(new { error = "Sign in again.", again = true });
                }
                ((ClaimsIdentity) waiting.Principal.Identity).AddClaim(new Claim("mfa", Skipped));
                return await Finish(context, roles, log, locks, waiting.Principal, waiting.Name, waiting.Keys, "Signed in without MFA", $"{Product.Name}, using Skip MFA this time", form.Notice);
            }
            // Set up some other way since the password was typed: this ticket's secret doesn't take that one's place.
            if (waiting.Secret is not null && mfa.Has(who))
            {
                pending.Remove(form.Ticket!);
                return Results.BadRequest(new { error = "MFA was just set up for this account. Sign in again.", again = true });
            }
            if (waiting.Secret is not null && pending.Secret(waiting) is null)
            {
                pending.Remove(form.Ticket!);
                return Results.BadRequest(new { error = "The sign-in timed out. Sign in again.", again = true });
            }
            // After a reset, a new app takes the one-time code too, so a password alone can't put someone else's app on it.
            var afterReset = waiting.Secret is not null && mfa.ResetWaiting(who);
            if (afterReset)
            {
                switch (mfa.CheckReset(who, form.Reset))
                {
                    case MfaAnswer.Wait:
                        return Results.Json(new { error = MfaApi.TooMany }, statusCode: StatusCodes.Status429TooManyRequests);
                    case MfaAnswer.Wrong:
                        return WrongCode("The one-time code is incorrect. Ask the administrator who reset your MFA for the code.", "Incorrect one-time code after an MFA reset");
                }
            }
            switch (waiting.Secret is null ? mfa.Check(who, form.Code) : mfa.Enable(who, pending.Secret(waiting)!, form.Code))
            {
                case MfaAnswer.Wait:
                    return Results.Json(new { error = MfaApi.TooMany }, statusCode: StatusCodes.Status429TooManyRequests);
                case MfaAnswer.Wrong:
                    return WrongCode(MfaApi.WrongCode, "Incorrect MFA code");
            }
            pending.Remove(form.Ticket!);
            if (afterReset)
            {
                mfa.ClearReset(who);
            }
            if (!roles.CanSignIn(who))
            {
                return Results.BadRequest(new { error = "Sign in again.", again = true });
            }
            ((ClaimsIdentity) waiting.Principal.Identity).AddClaim(new Claim("mfa", "1"));
            if (waiting.Secret is not null)
            {
                log.Record(username, from, "access", "mfa.on", "Enabled MFA", "ok", afterReset ? "At sign-in, using the one-time code from an MFA reset" : "At sign-in");
            }
            return await Finish(context, roles, log, locks, waiting.Principal, waiting.Name, waiting.Keys, notice: form.Notice);
        });

        // The last step for an account that hasn't accepted the Use at your own risk notice: accept, and the session starts;
        // decline, and none does, so the console never opens. Only a ticket from Finish, which comes after the password and code.
        app.MapPost("/api/signin/notice", async (HttpContext context, Roles roles, ActivityLog log, PendingSignIns pending, SignInLocks locks, RiskNotice notice, NoticeForm form) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            if (pending.Find(form.Ticket) is not { Notice: { } step } waiting)
            {
                return Results.BadRequest(new { error = "The sign-in timed out. Sign in again.", again = true });
            }
            pending.Remove(form.Ticket!);
            var who = Who.Of(waiting.Principal);
            var username = waiting.Principal.Identity!.Name!;
            var from = ActivityLog.Address(context);
            // Locked, or out of every role, since the password was typed: back to the password step, which says why.
            if (waiting.Keys.Any(k => locks.Check(k, Asking(context, locks, k)).State == LockState.Locked) || !roles.CanSignIn(who))
            {
                return Results.BadRequest(new { error = "Sign in again.", again = true });
            }
            if (!form.Accept)
            {
                log.Record(username, from, "sign-in", "notice.declined", "Declined the Use at your own risk notice", "ok", $"Notice version {RiskNotice.Version}. The console was not opened.");
                return Results.Ok(new { declined = true });
            }
            notice.Accept(who);
            log.Record(username, from, "sign-in", "notice.accepted", "Accepted the Use at your own risk notice", "ok", $"Notice version {RiskNotice.Version}");
            return await Finish(context, roles, log, locks, waiting.Principal, waiting.Name, waiting.Keys, step.Title, step.Detail);
        });

        app.MapPost("/api/signout",async (HttpContext context, bool? idle, SettingsStore settings, ActivityLog log) =>
        {
            // From the console's own page only, so another site can't sign anyone out.
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            // The page signing itself out after Sign out when idle.
            if (idle == true && context.User.Identity?.IsAuthenticated == true && settings.Current.IdleMinutes > 0)
            {
                log.Record(context, "sign-in", "signout.idle", $"Signed out after {IdleWords(settings.Current.IdleMinutes)} of inactivity");
            }
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        // Sign out when idle, for everyone: how long without a click, key press or scroll before a session ends.
        app.MapGet("/api/settings/signin", (HttpContext context, SettingsStore settings) =>
            Access.Can(context, Permissions.AccessRoles) ? Results.Ok(new { idle = settings.Current.IdleMinutes, lockAfter = settings.Current.LockAfter }) : Access.NotAllowed());
        app.MapPost("/api/settings/signin", (HttpContext context, SignInSettingsForm form, SettingsStore settings, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AccessRoles) is { } refused)
            {
                return refused;
            }
            if (form.Idle is { } idle)
            {
                if (!IdleChoices.Contains(idle))
                {
                    return Results.BadRequest(new { error = "Select Never, 15 or 30 minutes, or 1, 2, 4 or 8 hours." });
                }
                settings.Change(s => s.IdleMinutes = idle);
                log.Record(context, "admin", "settings.idle", idle == 0 ? "Disabled Sign out when idle" : $"Set Sign out when idle to {IdleWords(idle)}");
            }
            if (form.LockAfter is { } after)
            {
                if (!LockChoices.Contains(after))
                {
                    return Results.BadRequest(new { error = "Select 3, 5 or 10 incorrect passwords." });
                }
                settings.Change(s => s.LockAfter = after);
                log.Record(context, "admin", "settings.lock", $"Set the sign-in lockout threshold to {after} incorrect passwords");
            }
            return Results.Ok(new { idle = settings.Current.IdleMinutes, lockAfter = settings.Current.LockAfter });
        });

        // Settings, Sign-in: the accounts locked after too many wrong passwords, and unlocking one. Names that aren't
        // accounts here are only counted: they may be passwords typed in the wrong box.
        app.MapGet("/api/settings/signin/locks", (HttpContext context, SignInLocks locks) =>
        {
            if (!Access.Can(context, Permissions.AccessUnlock))
            {
                return Access.NotAllowed();
            }
            var (locked, others) = locks.Locked();
            return Results.Ok(new { locked = locked.Select(e => new { key = e.Id, name = e.Name, wrong = e.Wrong, since = e.Locked, from = e.From }), others });
        });
        app.MapPost("/api/settings/signin/locks/unlock", (HttpContext context, UnlockForm form, SignInLocks locks, Confirmations confirm, ActivityLog log) =>
        {
            if ((Access.RefuseUnless(context, Permissions.AccessUnlock) ?? confirm.Refuse(context)) is { } refused)
            {
                return refused;
            }
            if (locks.Locked().Accounts.FirstOrDefault(e => e.Id == form.Key) is not { } entry || !locks.Unlock(entry.Id))
            {
                return Results.NotFound(new { error = "This username is no longer locked out." });
            }
            log.Record(context, "access", "signin.unlock", $"Unlocked sign-in for {entry.Name}");
            return Results.Ok();
        });

        // Your own password, for Rabbid Lab accounts only. Needs the current one; wrong ones count against sign-in's limit.
        app.MapPost("/api/me/password", (HttpContext context, Accounts accounts, ActivityLog log, SignInLimiter limiter, SignInLocks locks, MyPasswordForm form) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            var username = context.User.Identity?.Name;
            if (context.User.FindFirstValue("source") != Local || username is null)
            {
                return Results.BadRequest(new { error = $"Only {Product.Title} local accounts can change their password here. Change Active Directory and Windows passwords in Windows." });
            }
            var from = ActivityLog.Address(context);
            if (!limiter.Take(from))
            {
                return TooMany();
            }
            if (accounts.Check(username, form.Current) is null)
            {
                log.Record(context, "access", "account.password.failed", "Entered an incorrect current password", "failed");
                return Results.BadRequest(new { error = "Your current password is incorrect." });
            }
            limiter.GiveBack(from);
            // Every session of the account ends, this one too, so the new password is the only way back in.
            if (accounts.SetPassword(username, form.Password) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            locks.ForgetBrowsers(SignInLocks.Key(Local, username)); // a new password: the browsers remembered for it are strangers again
            log.Record(context, "access", "account.password", "Changed their own password");
            return Results.Ok();
        });
    }

    /// <summary>
    /// --add-admin [username]: a local administrator with a new password, printed once, for when nobody can sign in. Run it
    /// on the machine Rabbid Lab is on, as an administrator (in Docker, with docker exec). The exit code, or null to run.
    /// </summary>
    public static int? AddAdminCommand(this WebApplication app, string[] args)
    {
        var at = Array.IndexOf(args, "--add-admin");
        if (at < 0)
        {
            return null;
        }
        var username = at + 1 < args.Length && !args[at + 1].StartsWith('-') ? args[at + 1] : "admin";
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(15)).Replace('+', 'x').Replace('/', 'y');
        if (Accounts.Problem(username, password) is { } problem)
        {
            Console.Error.WriteLine(problem);
            return 1;
        }
        app.Services.GetRequiredService<Accounts>().AddAdmin(username, password);
        app.Services.GetRequiredService<Roles>().Grant(Roles.Admin, new Member { Kind = Member.Local, Name = username });
        app.Services.GetRequiredService<ActivityLog>().Record(username, null, "sign-in", "account.add-admin", "Added an administrator from the command line", "ok");
        Console.WriteLine($"{username} can sign in to {Product.Name} with the password {password}");
        return 0;
    }

    /// <summary>
    /// --unlock name: unlocks a name locked after too many wrong passwords, for when no administrator can sign in to do
    /// it. Run it on the machine Rabbid Lab is on, as an administrator. The exit code, or null to run.
    /// </summary>
    public static int? UnlockCommand(this WebApplication app, string[] args)
    {
        var at = Array.IndexOf(args, "--unlock");
        if (at < 0)
        {
            return null;
        }
        if (at + 1 >= args.Length || args[at + 1].StartsWith('-'))
        {
            Console.Error.WriteLine("Specify a username: --unlock <username>");
            return 1;
        }
        var name = args[at + 1];
        // The name may be one of Rabbid Lab's or this computer's, or the domain's: whichever is locked. The file is changed
        // as it is (SignInLocks.UnlockInFile), and the running server sees that.
        if (SignInLocks.UnlockInFile(app.Services.GetRequiredService<DataFolder>(), [SignInLocks.Key(Local, name), SignInLocks.Key(Directory, name)]) == 0)
        {
            Console.WriteLine($"{name} is not locked out.");
            return 0;
        }
        app.Services.GetRequiredService<ActivityLog>().Record(name, null, "sign-in", "signin.unlock", "Unlocked from the command line", "ok");
        Console.WriteLine($"{name} can sign in to {Product.Name} again.");
        return 0;
    }

    /// <summary>
    /// The MFA step for someone who hasn't set it up: a ticket and the secret for their app. After a reset (reset: true) the
    /// page also asks for the one-time code whoever reset it was given. skip: the page may offer Skip MFA this time.
    /// </summary>
    private static IResult SetUpMfa(ClaimsPrincipal principal, string shown, Mfa mfa, PendingSignIns pending, bool skip, IReadOnlyList<string>? keys = null)
    {
        var who = Who.Of(principal);
        var secret = Totp.NewSecret();
        return Results.Ok(new { mfa = "setup", ticket = pending.Add(principal, shown, secret, keys ?? []), setup = MfaApi.Setup(who, secret), reset = mfa.ResetWaiting(who), skip });
    }

    /// <summary>The claim a session gets when its sign-in skipped MFA: it lasts only while skipping is still allowed for it.</summary>
    public const string Skipped = "skipped";

    /// <summary>
    /// Whether the MFA step may be skipped: only in Rabbid Lab Dev, and only while Settings, Developer allows it. From any
    /// computer since 1.1.379, at the user's word, so testing from other PCs doesn't need a phone. The release never skips.
    /// </summary>
    public static bool MaySkipMfa(HttpContext context) =>
        SkipsMfa(Product.Dev, context.RequestServices.GetRequiredService<SettingsStore>().Current.DevSkipMfa);

    /// <summary>The rule behind MaySkipMfa.</summary>
    public static bool SkipsMfa(bool dev, bool allowed) => dev && allowed;

    /// <summary>
    /// Who signed in and with what: the domain (or this computer), the domain controller that checked the password, and the
    /// groups in roles they're in. What their roles allow is worked out on each request, so role changes count at once.
    /// </summary>
    private static ClaimsPrincipal Principal(string username, string name, string source, string stamp, string? domain = null, string? server = null,
        IEnumerable<string>? groups = null, string? password = null)
    {
        List<Claim> claims = [new(ClaimTypes.Name, username), new("name", name), new("source", source), new("stamp", stamp)];
        if (password is not null)
        {
            claims.Add(new Claim(AccountChecks.PasswordClaim, password)); // when the password was set, not the password
        }
        if (domain is not null)
        {
            claims.Add(new Claim("domain", domain));
        }
        if (server is not null)
        {
            claims.Add(new Claim("server", server));
        }
        claims.AddRange((groups ?? []).Select(g => new Claim("group", g)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>Who's signed in, their roles highest first, and everything those allow.</summary>
    private static object Me(ClaimsPrincipal p, Roles roles)
    {
        var who = Who.Of(p);
        var can = roles.Allowed(who);
        return new
        {
            username = p.Identity?.Name,
            name = p.FindFirstValue("name"),
            source = p.FindFirstValue("source"),
            domain = p.FindFirstValue("domain"),
            server = p.FindFirstValue("server"),
            roles = Roles.Brief(roles.Of(who)),
            can = can.Order(),
            // Their roles show only part of Active Directory, so the pages about the whole domain aren't theirs.
            adScoped = can.Contains(Permissions.AdView) && roles.Reach(who, Permissions.AdView) is not null,
        };
    }

    /// <summary>Who a failed sign-in is logged under when what was typed isn't an account here.</summary>
    public const string Unknown = "Unknown user";

    /// <summary>
    /// A failed domain sign-in's name for the log: the account when it has signed in here before, otherwise Unknown user,
    /// so a password typed into the username box never lands in the log.
    /// </summary>
    private static string KnownIn(Roles roles, string typed, string domain, string setUp)
    {
        var sam = Ldap.Account(typed);
        if (sam.Length == 0)
        {
            return Unknown;
        }
        var username = domain == setUp ? sam : $"{sam}@{domain}";
        return roles.Snapshot().People.FirstOrDefault(p => p.Source == Directory && string.Equals(p.Username, username, StringComparison.OrdinalIgnoreCase)) is { } person
            ? person.Username : Unknown;
    }

    // Never kept past the browser, whatever a request asks.
    private static Task Start(HttpContext context, ClaimsPrincipal principal) =>
        context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });

    /// <summary>
    /// The end of every sign-in: the session starts, the name's wrong passwords are forgotten (keys: what they were
    /// counted under), the person is noted as seen, and it's logged.
    /// </summary>
    private static async Task<IResult> Finish(HttpContext context, Roles roles, ActivityLog log, SignInLocks locks, ClaimsPrincipal principal, string shown,
        IReadOnlyList<string> keys, string title = "Signed in", string? detail = null, string? notice = null)
    {
        var notices = context.RequestServices.GetRequiredService<RiskNotice>();
        var who = Who.Of(principal);
        if (!notices.Accepted(who))
        {
            // Accepted on the sign-in page as it opened, before the password: kept for this account now.
            if (notice == RiskNotice.Wording)
            {
                notices.Accept(who);
                log.Record(principal.Identity!.Name!, ActivityLog.Address(context), "sign-in", "notice.accepted", "Accepted the Use at your own risk notice", "ok",
                    $"Notice version {RiskNotice.Version}, on the sign-in page");
            }
            // Not accepted at all: no session, only a ticket to accept it with (/api/signin/notice).
            else
            {
                var ticket = context.RequestServices.GetRequiredService<PendingSignIns>().Add(principal, shown, null, keys, new NoticeStep(title, detail));
                return Results.Ok(new { notice = RiskNotice.Shape(), ticket });
            }
        }
        await Start(context, principal);
        foreach (var key in keys)
        {
            locks.Right(key, Asking(context, locks, key));
        }
        // This browser has now signed in as the account: its wrong passwords for it are counted apart from everyone else's.
        if (keys.Count > 0 && !OnServer(context))
        {
            context.Response.Cookies.Append(Known, locks.Remember(context.Request.Cookies[Known], keys[0]), new CookieOptions
                { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, MaxAge = TimeSpan.FromDays(SignInLocks.BrowserDays), IsEssential = true });
        }
        roles.Seen(Who.Of(principal), shown);
        log.Record(principal.Identity!.Name!, ActivityLog.Address(context), "sign-in", "signin", title, "ok", detail);
        return Results.Ok(new { user = Me(principal, roles) });
    }

    private static string? Stamp(long? passwordSet) => passwordSet?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Takes one of the address's tries before a sign-in is checked, and gives it back unless the answer is a wrong password (401).</summary>
    private static async ValueTask<object?> Limited(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var limiter = invocation.HttpContext.RequestServices.GetRequiredService<SignInLimiter>();
        // A browser signing in as the account it has signed in as before is counted by itself, so wrong passwords from its
        // neighbours on the network don't stop it. For any other name it is counted with its address, like everyone.
        var http = invocation.HttpContext;
        var locks = http.RequestServices.GetRequiredService<SignInLocks>();
        var token = http.Request.Cookies[Known];
        var typed = invocation.Arguments.OfType<SignInForm>().FirstOrDefault()?.Username?.Trim();
        var browser = string.IsNullOrEmpty(typed) || typed.Length > MaxUsername ? null
            : locks.Browser(token, SignInLocks.Key(Local, typed)) ?? locks.Browser(token, SignInLocks.Key(Directory, typed));
        var from = browser is not null ? "browser:" + browser : ActivityLog.Address(http);
        // The server itself is the way back in when everything else is locked: its tries are slowed by name, never stopped here.
        if (OnServer(http))
        {
            return await next(invocation);
        }
        if (!limiter.Take(from))
        {
            return TooMany();
        }
        var wrong = false;
        try
        {
            var answer = await next(invocation);
            wrong = answer is IStatusCodeHttpResult { StatusCode: StatusCodes.Status401Unauthorized };
            return answer;
        }
        finally
        {
            if (!wrong)
            {
                limiter.GiveBack(from);
            }
        }
    }

    private static string Clean(string? domain) => domain?.Trim().TrimEnd('.').ToLowerInvariant() ?? "";

    /// <summary>Why a domain name won't do, or null.</summary>
    private static string? DomainProblem(string? domain)
    {
        var d = Clean(domain);
        return d.Length == 0 || !d.Contains('.') || Uri.CheckHostName(d) != UriHostNameType.Dns ? "Enter the fully qualified domain name, such as home.lan." : null;
    }

    /// <summary>Why a domain controller's name won't do, or null. Empty is Automatic.</summary>
    private static string? ServerProblem(string? server) =>
        string.IsNullOrWhiteSpace(server) || Uri.CheckHostName(server.Trim()) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6
            ? null : "That is not a valid domain controller name.";

    /// <summary>The domains accounts may sign in from: the one set up, the rest of its forest and the domains it trusts. Asked every ten minutes.</summary>
    private static async Task<IReadOnlyList<string>> Allowed(IMemoryCache cache, IDirectory directory, string domain, CancellationToken stop) =>
        await cache.GetOrCreateAsync($"signin.domains:{domain}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await directory.Domains(domain, stop);
        }) ?? [domain];

    private static string NotTrusted(string domain, string setUp) =>
        $"{domain} is not in the {setUp} forest and {setUp} has no trust with it. Accounts from {domain} cannot sign in.";

    private const string WrongText = "The username or password is incorrect.";
    private const string WrongWithCode = "The username, password or code is incorrect.";

    /// <summary>Said for any name, an account or not. Without a number: the domain's own may be lower than Rabbid Lab's.</summary>
    private static string LockedText(string name) => $"{name} is locked out after too many incorrect passwords. An administrator must unlock it.";

    /// <summary>A locked name: nothing is checked.</summary>
    private static IResult LockedOut(string name) => Results.Json(new { error = LockedText(name), locked = true }, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>Too soon after a wrong password: nothing is checked, and the page counts the seconds down.</summary>
    private static IResult TooSoon(int seconds, bool locked) => Results.Json(new
    {
        error = $"Too many incorrect passwords. Try again in {(seconds < 60 ? $"{seconds} second{(seconds == 1 ? "" : "s")}" : $"{(seconds + 59) / 60} minute{(seconds <= 60 ? "" : "s")}")}.",
        wait = seconds,
        locked,
    }, statusCode: StatusCodes.Status429TooManyRequests);

    /// <summary>
    /// Whether the request comes from the server itself (loopback, after a trusted proxy's X-Forwarded-For): its wrong
    /// passwords are counted apart and never lock a name, so whoever can get to the server can always get in.
    /// </summary>
    private static bool OnServer(HttpContext context) => context.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(Addresses.Normal(ip));

    /// <summary>Whose count a try for this name goes on: the server itself and a browser that has signed in as it before have their own.</summary>
    private static Asker Asking(HttpContext context, SignInLocks locks, string key) =>
        OnServer(context) ? new(Here: true) : new(locks.Browser(context.Request.Cookies[Known], key));

    private const int UnknownDomain = 4; // a domain that won't say where it locks: one short of the usual five

    /// <summary>
    /// How many wrong passwords lock a domain account here: Rabbid Lab's own number, or one short of where the domain's
    /// policy locks accounts itself when that comes sooner, so wrong passwords typed here stop before the domain locks the
    /// account in Windows. A stricter policy set on the account alone isn't read. Asked every ten minutes.
    /// </summary>
    private static async Task<int> Most(IMemoryCache cache, IDirectory directory, string domain, int own, CancellationToken stop)
    {
        var threshold = await cache.GetOrCreateAsync($"signin.lockout:{domain}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await directory.LockoutThreshold(domain, stop);
        });
        return threshold switch
        {
            > 0 => Math.Clamp(threshold.Value - 1, 1, own),
            0 => own, // the domain never locks an account
            _ => Math.Min(own, UnknownDomain),
        };
    }

    private static IResult TooMany() =>
        Results.Json(new { error = "Too many incorrect passwords from this computer. Wait a few minutes, then try again." }, statusCode: StatusCodes.Status429TooManyRequests);
}

public sealed record LocalSetupForm(string? Username, string? Password);

public sealed record MyPasswordForm(string? Current, string? Password);

public sealed record DirectorySetupForm(string? Domain, string? Username, string? Password, string? Server = null);

/// <summary>Code: the authenticator code, sent with the password only to unlock a locked administrator.</summary>
// Notice: the wording of the Use at your own risk notice this browser accepted on the sign-in page, if it did.
public sealed record SignInForm(string? Username, string? Password, string? Server = null, bool Local = false, string? Domain = null, string? Code = null, string? Notice = null);

public sealed record UnlockForm(string? Key);

public sealed record MfaSignInForm(string? Ticket, string? Code, string? Reset = null, bool Skip = false, string? Notice = null);

public sealed record SignInSettingsForm(int? Idle = null, int? LockAfter = null);


/// <summary>Ten wrong passwords from one address in ten minutes, then it waits.</summary>
public sealed class SignInLimiter(TimeProvider clock) : Limiter(10, clock, byAddress: true);

/// <summary>So many tries from one address in ten minutes: taken before the work, and given back when it doesn't count.</summary>
public class Limiter(int most, TimeProvider clock, bool byAddress = false)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private readonly Lock gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> tries = [];
    private DateTimeOffset swept;

    /// <summary>How many addresses it's keeping count for.</summary>
    public int Tracked
    {
        get
        {
            lock (gate)
            {
                return tries.Count;
            }
        }
    }

    /// <summary>Takes one of the address's tries; false when it has none left.</summary>
    public bool Take(string? from)
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            Sweep(now);
            var key = Group(from);
            if (!tries.TryGetValue(key, out var taken))
            {
                tries[key] = taken = [];
            }
            taken.RemoveAll(t => now - t > Window);
            if (taken.Count >= most)
            {
                return false;
            }
            taken.Add(now);
            return true;
        }
    }

    /// <summary>Gives back the try just taken.</summary>
    public void GiveBack(string? from)
    {
        lock (gate)
        {
            var key = Group(from);
            if (tries.TryGetValue(key, out var taken) && taken.Count > 0)
            {
                taken.RemoveAt(taken.Count - 1);
                if (taken.Count == 0)
                {
                    tries.Remove(key);
                }
            }
        }
    }

    /// <summary>What a try is counted under: with byAddress, an IPv6 address by its /64 (Addresses.Network).</summary>
    private string Group(string? from) => byAddress ? Addresses.Network(from) : from ?? "";

    /// <summary>Once a minute, forgets the addresses whose tries have all run out.</summary>
    private void Sweep(DateTimeOffset now)
    {
        if (now - swept < TimeSpan.FromMinutes(1))
        {
            return;
        }
        swept = now;
        foreach (var (key, taken) in tries.ToList())
        {
            taken.RemoveAll(t => now - t > Window);
            if (taken.Count == 0)
            {
                tries.Remove(key);
            }
        }
    }
}
