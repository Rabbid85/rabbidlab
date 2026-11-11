using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using RabbidLab.Server;
using RabbidLab.Updater;

// Rabbid Lab: one console for a homelab, in a browser. It runs as a Windows service (installed by Setup, Installer\) or in
// Docker (Dockerfile), keeps everything in one data folder (DataFolder.cs), and updates itself from the releases of its
// GitHub repository (Updates.cs, Update.cs). People sign in with a local account or Active Directory (SignIn.cs).

// --health: is Rabbid Lab answering? For Docker's HEALTHCHECK (the image has no curl): 0 if it is, 1 if not.
if (args.Contains("--health"))
{
    var url = (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? $"http://+:{Product.DefaultPort}").Split(';')[0]
        .Replace("://+", "://127.0.0.1").Replace("://*", "://127.0.0.1").Replace("://0.0.0.0", "://127.0.0.1").Replace("://[::]", "://127.0.0.1");
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    try
    {
        using var answer = await http.GetAsync(url.TrimEnd('/') + "/api/health");
        return answer.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
    {
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = Product.Name); // nothing changes outside a Windows service
if (OperatingSystem.IsWindows() && Product.Dev)
{
    // Its own name in the Windows event log, so removing Rabbid Lab Dev leaves the release's alone.
#pragma warning disable CA1416 // only reached on Windows
    builder.Services.Configure<Microsoft.Extensions.Logging.EventLog.EventLogSettings>(settings => settings.SourceName = Product.Name);
#pragma warning restore CA1416
}

// Setup writes console.json next to the program: the port to listen on (on every network card, so other computers can
// reach it), and for HTTPS, which certificate in the machine's store to use. Without it, Rabbid Lab listens where --urls or
// ASPNETCORE_URLS says, or else over HTTP on its own port (Product.DefaultPort, Setup's first choice too).
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "console.json"), optional: true, reloadOnChange: false);
if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
    && !builder.Configuration.GetSection("Kestrel:Endpoints").Exists())
{
    builder.WebHost.UseUrls($"http://+:{Product.DefaultPort}");
}

// Settings, the activity log and the encryption keys live in the data folder. On Windows the keys that will encrypt saved
// secrets are themselves encrypted by Windows (DPAPI) for the account Rabbid Lab runs under, so a copy of the folder is no
// use elsewhere.
builder.Services.AddSingleton(services => DataFolder.Resolve(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<ILoggerFactory>().CreateLogger<DataFolder>()));
var keys = builder.Services.AddDataProtection().SetApplicationName(Product.Name);
if (OperatingSystem.IsWindows())
{
    keys.ProtectKeysWithDpapi();
}
builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(services => new ConfigureOptions<KeyManagementOptions>(options =>
    options.XmlRepository = new FileSystemXmlRepository(
        new DirectoryInfo(Path.Combine(services.GetRequiredService<DataFolder>().Path, "Keys")), services.GetRequiredService<ILoggerFactory>())));
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<ActivityLog>();
builder.Services.AddSingleton<ServerStatus>();
builder.Services.AddHttpClient();
// A background service that fails in a way it didn't expect is logged and left stopped: Rabbid Lab itself keeps serving.
builder.Services.Configure<HostOptions>(options => options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

// Updating itself: the releases on GitHub (Updates.cs), and the update engine (Src/Utilities/RabbidLab.Updater) with Rabbid
// Lab's side of it (Update.cs). A newer release is offered on the page and installed when someone chooses Install now, or
// by itself once nobody is using Rabbid Lab. It is downloaded, checked against Rabbid Lab's release signature and installed
// quietly; Setup restarts the service as the new version.
builder.AddUpdates();
builder.Services.AddSingleton<UpdateOptions>();
builder.Services.AddSingleton<InstallerRunner>();
builder.Services.AddSingleton<IInstallerRunner, ConsoleInstallerRunner>(); // says what stopped an installer Windows wouldn't run
builder.Services.AddSingleton<IUpdateHost, ConsoleUpdateHost>();
builder.Services.AddSingleton<UpdateEngine>();
builder.Services.AddHostedService<UpdateWorker>();

// Typing just the name: port 80 sends people on to Rabbid Lab's address (Redirect.cs), when Setup asked for it.
var redirect = builder.AddRedirectPort();

// Signing in: a local account or Active Directory, set up on the first visit (SignIn.cs).
builder.Services.AddSignIn();

// The user tools: Active Directory and Windows accounts (UsersApi.cs).
builder.Services.AddUserTools();
builder.Services.AddSingleton<Links>();

// Sensors: checks on a timer, and email when one stays down (Sensors.cs, Mail.cs).
builder.Services.AddSingleton<SensorLogins>();
builder.Services.AddSingleton<ISensorProbe, NetworkProbe>();
builder.Services.AddSingleton<Mail>();
builder.Services.AddSingleton<IMailSender>(services => services.GetRequiredService<Mail>());
builder.Services.AddSingleton<Sensors>();
builder.Services.AddHostedService<SensorWorker>();

var app = builder.Build();

// --add-admin: a local administrator for when nobody can sign in, then it exits.
if (app.AddAdminCommand(args) is int added)
{
    return added;
}

app.UseRedirectPort(redirect);
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; font-src 'self'; "
        + "connect-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'";
    headers["Referrer-Policy"] = "same-origin";
    headers.XContentTypeOptions = "nosniff";
    var path = context.Request.Path;
    if (path.StartsWithSegments("/api"))
    {
        headers.CacheControl = "no-store";
    }
    else if (path == "/" || path.Value?.EndsWith(".html", StringComparison.OrdinalIgnoreCase) == true)
    {
        headers.CacheControl = "no-cache"; // pages are checked each time, so an update shows straight away
    }
    await next();
});
app.UseSignIn();
// Someone working in Rabbid Lab: a new version waits for them, not installed by itself (UpdateEngine.InUse). Only a
// signed-in person counts, and not what the page asks by itself or the clients checking in (SignIn.Working).
app.Use(async (context, next) =>
{
    if (SignIn.Working(context))
    {
        context.RequestServices.GetRequiredService<UpdateEngine>().InUse();
    }
    await next(context);
});
// Rabbid Lab Dev: its icon (the release's, with a DEV band), and its DEV tag (edition-dev.css; the release's edition.css is empty).
if (Product.Dev)
{
    app.Use((context, next) =>
    {
        var path = context.Request.Path;
        if (path == "/favicon.svg")
        {
            context.Request.Path = "/favicon-dev.svg";
        }
        else if (path == "/Assets/edition.css")
        {
            context.Request.Path = "/Assets/edition-dev.css";
        }
        return next(context);
    });
}
app.UseDefaultFiles();
// The browser asks each time whether a page, script or style sheet has changed (a quick 304 when it hasn't), so the page a
// new version installs is the one people get, without a hard refresh.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});
app.MapGet("/api/health", () => Results.Ok(new { ok = true })); // open, for Docker's health check
app.MapSignIn();
app.MapStatusApi();
app.MapActivityApi();
app.MapSessionsApi();
app.MapRolesApi();
app.MapClientsApi();
app.MapUsersApi();
app.MapDirectoryExtrasApi();
app.MapLinksApi();
app.MapSensorsApi();
app.MapDevToolsApi();
app.MapBrandApi();
app.MapMailApi();
app.MapUpdateApi();

app.Run();
return 0;

/// <summary>Lets the tests start the app.</summary>
public partial class Program
{
}
