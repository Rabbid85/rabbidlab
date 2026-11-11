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
    // Without Keep me signed in: an open console checks in every minute or less, so this ends soon after its last page closes.
    public static readonly TimeSpan Session = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Remembered = TimeSpan.FromDays(30);
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
        services.AddSingleton<IDirectory, ActiveDirectory>();
        services.AddSingleton<IWindowsAccounts, WindowsAccounts>();
        services.AddMemoryCache();
        services.AddSingleton<IDomainFinder, DomainFinder>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<Sessions>();
        services.AddSingleton<Clients>();
        services.AddSingleton<JoinLimiter>();
        // The cookie carries only the session's id; the session itself is kept by Sessions.
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<Sessions>((options, sessions) => options.SessionStore = sessions);
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
                var accounts = context.HttpContext.RequestServices.GetRequiredService<Accounts>();
                var roles = context.HttpContext.RequestServices.GetRequiredService<Roles>();
                var p = context.Principal;
                var stamp = p?.FindFirstValue("stamp");
                var current = p?.FindFirstValue("source") switch
                {
                    Directory => accounts.Directory?.Stamp,
                    Windows => Windows, // Windows checks the password; the session simply runs its course
                    _ => accounts.Find(p?.Identity?.Name)?.Stamp,
                };
                // Sessions without Keep me signed in used to last 12 hours; those end now.
                var old = !context.Properties.IsPersistent && context.Properties.ExpiresUtc - context.Properties.IssuedUtc > Session;
                // Staying signed in renews a session, but never past Longest for an account Rabbid Lab doesn't keep itself.
                if (p?.FindFirstValue("source") is Directory or Windows && Sessions.Current(context.HttpContext) is { } id
                    && context.HttpContext.RequestServices.GetRequiredService<Sessions>().Find(id) is { } session)
                {
                    old |= context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow() - session.Started > Longest;
                }
                if (stamp is null || stamp != current || old || p is null || !roles.CanSignIn(Who.Of(p)))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            };
        });
    }

    /// <summary>Who is asking, then the gate: until someone signs in, pages lead to setup or sign-in and the API says 401.</summary>
    public static void UseSignIn(this WebApplication app)
    {
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var signedIn = context.User.Identity?.IsAuthenticated == true;
            // What anyone can send is kept small: a client's answer may list a computer's accounts, the rest is a form.
            if (!signedIn && Open(path) && TooBig(context, path == "/api/clients/done" ? 1024 * 1024 : 64 * 1024))
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await context.Response.WriteAsJsonAsync(new { error = "That request is too large." });
                return;
            }
            if (signedIn || Open(path))
            {
                await next(context);
                return;
            }
            if (path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Sign in first." });
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
    private static readonly string[] ClientPaths = ["/api/clients/join", "/api/clients/checkin", "/api/clients/next", "/api/clients/done", "/api/clients/download"];

    // What an open page asks by itself on a timer.
    private static readonly string[] Polled = ["/api/update", "/api/clients", "/api/sessions", "/api/links/states", "/api/clients/here"];

    public static bool ForClients(PathString path) => ClientPaths.Any(p => path == p);

    /// <summary>Whether this request is a signed-in person working (not a poll, the health check or a client), so a new version waits.</summary>
    public static bool Working(HttpContext context)
    {
        var path = context.Request.Path;
        return context.User.Identity?.IsAuthenticated == true && path.StartsWithSegments("/api") && !ForClients(path)
            && !path.StartsWithSegments("/api/update/watch") && path != "/api/health"
            && !(HttpMethods.IsGet(context.Request.Method) && Polled.Any(p => path == p));
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
        app.MapGet("/api/signin", (HttpContext context, Accounts accounts, Roles roles) => Results.Ok(new
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
            return wanted != d.Domain && !(await Allowed(cache, directory, d.Domain, stop)).Contains(wanted)
                ? Results.BadRequest(new { error = NotTrusted(wanted, d.Domain) })
                : Results.Ok(new { controllers = await finder.Controllers(wanted, stop) });
        });

        // First run, a local administrator account.
        app.MapPost("/api/setup/local", async (HttpContext context, Accounts accounts, Roles roles, ActivityLog log, LocalSetupForm form) =>
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
            log.Record(account.Username, ActivityLog.Address(context), "sign-in", "setup.local", $"Set up {Product.Name} with a local account", "ok");
            var principal = Principal(account.Username, account.Username, Local, account.Stamp);
            await Start(context, principal, remember: false);
            roles.Seen(Who.Of(principal), account.Username);
            return Results.Ok(new { user = Me(principal, roles) });
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
            IDomainFinder finder, DirectorySetupForm form) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            var from = ActivityLog.Address(context);
            if (accounts.SetUp)
            {
                return Results.Conflict(new { error = Product.Name + " is already set up. Sign in instead." });
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
            var setupHost = domain;
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
                return Results.Json(new { error = "That username or password is wrong." }, statusCode: StatusCodes.Status401Unauthorized);
            }
            if (accounts.SetUpDirectory(domain, server) is { } problem)
            {
                return Results.Conflict(new { error = problem });
            }
            log.Record(user.Username, from, "sign-in", "setup.directory", $"Set up {Product.Name} with Active Directory ({domain}, {server ?? "automatic"}, anyone in the domain)", "ok");
            // Whoever sets it up is the administrator; everyone in the domain gets User.
            roles.SetUp(new Member { Kind = Member.Person, Name = user.Username, Display = user.Name }, new Member { Kind = Member.Everyone, Name = "ad" });
            var principal = Principal(user.Username, user.Name, Directory, accounts.Directory!.Stamp, domain, setupHost, user.Groups);
            await Start(context, principal, form.Remember);
            roles.Seen(Who.Of(principal), user.Name);
            return Results.Ok(new { user = Me(principal, roles) });
        }).AddEndpointFilter(Limited);

        // Rabbid Lab's own accounts for .\name or Local account, otherwise Active Directory when it's set up.
        app.MapPost("/api/signin", async (HttpContext context, Accounts accounts, Roles roles, ActivityLog log, IDirectory directory, IDomainFinder finder,
            IWindowsAccounts windows, IMemoryCache cache, SignInForm form) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            var from = ActivityLog.Address(context);
            var name = form.Username?.Trim() ?? "";
            if (name.Length > MaxUsername)
            {
                return Results.BadRequest(new { error = "That username is too long." });
            }
            // .\name is one of Rabbid Lab's own accounts, the way Windows uses .\ for the computer itself.
            var local = form.Local || name.StartsWith(@".\", StringComparison.Ordinal) || name.StartsWith("./", StringComparison.Ordinal);
            name = name.StartsWith(@".\", StringComparison.Ordinal) || name.StartsWith("./", StringComparison.Ordinal) ? name[2..] : name;
            var wrong = Results.Json(new { error = "That username or password is wrong." }, statusCode: StatusCodes.Status401Unauthorized);
            var settings = accounts.Directory;
            if (local || settings is null)
            {
                // Rabbid Lab's own accounts, then, on Windows, an administrator of this computer with its own Windows account.
                ClaimsPrincipal? principal = null;
                if (accounts.Check(name, form.Password) is { } account)
                {
                    principal = Principal(account.Username, account.Username, Local, account.Stamp);
                }
                else if (await windows.Administrator(name, form.Password ?? "", context.RequestAborted) is { } admin)
                {
                    principal = Principal($"{Environment.MachineName}\\{admin}", admin, Windows, Windows, Environment.MachineName);
                }
                if (principal is null)
                {
                    log.Record(name.Length > 0 ? name : "Unknown user", from, "sign-in", "signin.failed", "Sign-in failed", "failed");
                    return wrong;
                }
                await Start(context, principal, form.Remember);
                roles.Seen(Who.Of(principal), principal.FindFirstValue("name") ?? name);
                log.Record(principal.Identity!.Name!, from, "sign-in", "signin", "Signed in", "ok");
                return Results.Ok(new { user = Me(principal, roles) });
            }
            // Another domain only when the one set up shares its forest or trusts it, so nobody can send the check to a domain of their own.
            var domain = string.IsNullOrWhiteSpace(form.Domain) ? settings.Domain : Clean(form.Domain);
            if (domain != settings.Domain && !(await Allowed(cache, directory, settings.Domain, context.RequestAborted)).Contains(domain))
            {
                return Results.BadRequest(new { error = NotTrusted(domain, settings.Domain) });
            }
            // A domain controller picked for this sign-in must be one DNS lists for the domain, so nobody can send the password check elsewhere.
            var chosen = form.Server?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(chosen) && !(await finder.Controllers(domain, context.RequestAborted)).Any(c => c.Name == chosen))
            {
                return Results.BadRequest(new { error = $"{chosen} isn't one of {domain}'s domain controllers." });
            }
            DirectoryUser? user;
            var host = domain;
            try
            {
                // The controller set up for the domain unless another is picked; other domains start on Automatic.
                var server = !string.IsNullOrEmpty(chosen) ? chosen : domain == settings.Domain ? settings.Server : null;
                host = await finder.Host(domain, server, context.RequestAborted);
                user = await directory.SignIn(domain, host, roles.Groups(), name, form.Password ?? "", context.RequestAborted);
            }
            catch (DirectoryProblem e)
            {
                log.Record(name, from, "sign-in", "signin.failed", "Sign-in failed", "failed", e.Message);
                return Results.Json(new { error = $"Couldn't check your password with {domain}: {e.Message}" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            if (user is null)
            {
                log.Record(name.Length > 0 ? name : "Unknown user", from, "sign-in", "signin.failed", "Sign-in failed", "failed");
                return wrong;
            }
            // Accounts from another domain keep it in their name, so awiles@lab.amw.com isn't taken for amw.com's awiles.
            var username = domain == settings.Domain ? user.Username : $"{user.Username}@{domain}";
            // In at least one role, or no way in.
            var who = new Who(Directory, username, domain, user.Groups);
            if (!roles.CanSignIn(who))
            {
                log.Record(username, from, "sign-in", "signin.refused", "Sign-in refused", "failed", "Not in any role");
                return Results.Json(new { error = $"Your account isn't in any of {Product.Name}'s roles yet, so it can't sign in. Ask whoever runs {Product.Name} to add you." },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            var signedIn = Principal(username, user.Name, Directory, settings.Stamp, domain, host, user.Groups);
            await Start(context, signedIn, form.Remember);
            roles.Seen(who, user.Name);
            log.Record(username, from, "sign-in", "signin", "Signed in", "ok");
            return Results.Ok(new { user = Me(signedIn, roles) });
        }).AddEndpointFilter(Limited);

        app.MapPost("/api/signout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
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
    /// Who signed in and with what: the domain (or this computer), the domain controller that checked the password, and the
    /// groups in roles they're in. What their roles allow is worked out on each request, so role changes count at once.
    /// </summary>
    private static ClaimsPrincipal Principal(string username, string name, string source, string stamp, string? domain = null, string? server = null,
        IEnumerable<string>? groups = null)
    {
        List<Claim> claims = [new(ClaimTypes.Name, username), new("name", name), new("source", source), new("stamp", stamp)];
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
        return new
        {
            username = p.Identity?.Name,
            name = p.FindFirstValue("name"),
            source = p.FindFirstValue("source"),
            domain = p.FindFirstValue("domain"),
            server = p.FindFirstValue("server"),
            roles = Roles.Brief(roles.Of(who)),
            can = roles.Allowed(who).Order(),
        };
    }

    private static Task Start(HttpContext context, ClaimsPrincipal principal, bool remember) =>
        context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
        {
            IsPersistent = remember,
            ExpiresUtc = !remember ? null : context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow()
                + (principal.FindFirstValue("source") is Directory or Windows ? Longest : Remembered),
            AllowRefresh = true,
        });

    /// <summary>Takes one of the address's tries before a sign-in is checked, and gives it back unless the answer is a wrong password (401).</summary>
    private static async ValueTask<object?> Limited(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var limiter = invocation.HttpContext.RequestServices.GetRequiredService<SignInLimiter>();
        var from = ActivityLog.Address(invocation.HttpContext);
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
        return d.Length == 0 || !d.Contains('.') || Uri.CheckHostName(d) != UriHostNameType.Dns ? "Enter the domain's full name, such as home.lan." : null;
    }

    /// <summary>Why a domain controller's name won't do, or null. Empty is Automatic.</summary>
    private static string? ServerProblem(string? server) =>
        string.IsNullOrWhiteSpace(server) || Uri.CheckHostName(server.Trim()) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6
            ? null : "That isn't a domain controller's name.";

    /// <summary>The domains accounts may sign in from: the one set up, the rest of its forest and the domains it trusts. Asked every ten minutes.</summary>
    private static async Task<IReadOnlyList<string>> Allowed(IMemoryCache cache, IDirectory directory, string domain, CancellationToken stop) =>
        await cache.GetOrCreateAsync($"signin.domains:{domain}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await directory.Domains(domain, stop);
        }) ?? [domain];

    private static string NotTrusted(string domain, string setUp) =>
        $"{domain} isn't in {setUp}'s forest and {setUp} doesn't trust it, so its accounts can't sign in here.";

    private static IResult TooMany() =>
        Results.Json(new { error = "Too many wrong passwords from this computer. Wait a few minutes, then try again." }, statusCode: StatusCodes.Status429TooManyRequests);
}

public sealed record LocalSetupForm(string? Username, string? Password);

public sealed record DirectorySetupForm(string? Domain, string? Username, string? Password, bool Remember, string? Server = null);

public sealed record SignInForm(string? Username, string? Password, bool Remember, string? Server = null, bool Local = false, string? Domain = null);


/// <summary>Ten wrong passwords from one address in ten minutes, then it waits.</summary>
public sealed class SignInLimiter(TimeProvider clock) : Limiter(10, clock);

/// <summary>So many tries from one address in ten minutes: taken before the work, and given back when it doesn't count.</summary>
public class Limiter(int most, TimeProvider clock)
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
            var key = from ?? "";
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
            var key = from ?? "";
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
