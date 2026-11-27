using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;

namespace RabbidLab.Server;

/// <summary>
/// Typing just Rabbid Lab's name in a browser (rabbidlab.home.lan) tries plain http on port 80. When Setup asks for it
/// (Redirect:Port in console.json) and nothing else on the machine has that port, Rabbid Lab listens there too and sends
/// every request on to its real address, like https://rabbidlab.home.lan:8420. Nothing else is served on it. When the
/// Windows HTTP service has port 80 (IIS and the like), Rabbid Lab shares it by name instead (RedirectByName).
/// </summary>
public static class Redirect
{
    public sealed record Plan(int Port, string Scheme, int To, bool Taken);

    /// <summary>Listens on the redirect port too, if console.json asks for it and the port is free.</summary>
    public static Plan? AddRedirectPort(this WebApplicationBuilder builder, int? to = null)
    {
        var port = builder.Configuration.GetValue<int>("Redirect:Port");
        var url = (builder.Configuration["Kestrel:Endpoints:Console:Url"] ?? "").Replace("://*", "://localhost").Replace("://+", "://localhost");
        if (port <= 0 || !Uri.TryCreate(url, UriKind.Absolute, out var console) || console.Port == port)
        {
            return null;
        }
        var held = Bind(port);
        // to: the console also answers on port 443 (ConsoleAlso.cs), so people are sent to the address with no port in it.
        var plan = new Plan(port, console.Scheme, to ?? console.Port, held is null);
        if (!plan.Taken)
        {
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenAnyIP(port));
            // Kestrel listens on the socket bound here, so no other program can take the port before it does.
            builder.Services.Configure<SocketTransportOptions>(sockets => sockets.CreateBoundListenSocket = endpoint =>
                endpoint is IPEndPoint ip && ip.Port == port && Interlocked.Exchange(ref held, null) is { } bound
                    ? bound : SocketTransportOptions.CreateDefaultBoundListenSocket(endpoint));
        }
        else if (OperatingSystem.IsWindows() && !Product.InContainer)
        {
            builder.Services.AddSingleton(services => new RedirectByName(plan, services.GetRequiredService<ConsoleNames>(), services.GetRequiredService<ILogger<RedirectByName>>()));
            builder.Services.AddHostedService(services => services.GetRequiredService<RedirectByName>());
        }
        return plan;
    }

    /// <summary>
    /// The port, bound the way Kestrel binds it, or null when another program has it. The listener table missed another
    /// console's port 80 redirect, and a failed bind stops Rabbid Lab altogether.
    /// </summary>
    internal static Socket? Bind(int port)
    {
        var dual = Socket.OSSupportsIPv6;
        var socket = new Socket(dual ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            if (dual)
            {
                socket.DualMode = true;
            }
            socket.Bind(new IPEndPoint(dual ? IPAddress.IPv6Any : IPAddress.Any, port));
            return socket;
        }
        catch (SocketException)
        {
            socket.Dispose();
            return null;
        }
    }

    /// <summary>Where a request on the redirect port goes: the console's address by the name it used.</summary>
    public static string Target(Plan plan, string host, string rest) =>
        $"{plan.Scheme}://{host}{(plan.To == (plan.Scheme == "https" ? 443 : 80) ? "" : ":" + plan.To)}{rest}";

    /// <summary>
    /// The names to ask a shared port 80 for: Setup's host name and the names added in Settings, short and full. Never the
    /// computer's own name, localhost or an IP address, which stay with the web server that has the port.
    /// </summary>
    public static List<string> OwnNames(string? extra, IEnumerable<string> added, string machine, string domain)
    {
        machine = machine.ToLowerInvariant();
        domain = domain.ToLowerInvariant();
        var list = new List<string>();
        void Add(string? name)
        {
            name = name?.Trim().TrimEnd('.').ToLowerInvariant();
            if (name is { Length: > 0 } && ConsoleNames.IsDnsName(name) && !IPAddress.TryParse(name, out _) && name != "localhost"
                && name != machine && name != $"{machine}.{domain}" && !list.Contains(name))
            {
                list.Add(name);
            }
        }
        if (extra is { Length: > 0 })
        {
            Add(extra);
            if (!extra.Contains('.') && domain.Length > 0)
            {
                Add($"{extra}.{domain}");
            }
        }
        foreach (var name in added)
        {
            Add(name);
        }
        return list;
    }

    /// <summary>Sends requests on the redirect port to Rabbid Lab's address, by the name they used.</summary>
    public static void UseRedirectPort(this WebApplication app, Plan? plan)
    {
        if (plan is null)
        {
            return;
        }
        if (plan.Taken)
        {
            if (app.Services.GetService<RedirectByName>() is null)
            {
                app.Logger.LogWarning("Port {Port} is already in use by another application on this machine, so the redirect to {Product} is not available. Enter the full address instead", plan.Port, Product.Name);
            }
            return;
        }
        app.Use(async (context, next) =>
        {
            if (context.Connection.LocalPort != plan.Port)
            {
                await next(context);
                return;
            }
            var request = context.Request;
            context.Response.Redirect(Target(plan, request.Host.Host, $"{request.PathBase}{request.Path}{request.QueryString}"));
        });
    }
}

/// <summary>
/// Port 80 belongs to the Windows HTTP service (HTTP.sys), which IIS and other web servers share it through. Rabbid Lab
/// asks it for just the console's own names, so http://helpdesk comes here and every other name stays where it was.
/// Each name is then tried from this computer, since a web server that took every name first still gets them.
/// </summary>
public sealed class RedirectByName(Redirect.Plan plan, ConsoleNames names, ILogger<RedirectByName> log) : BackgroundService
{
    /// <summary>A name asked for: whether a request for it reaches Rabbid Lab, or why not.</summary>
    public sealed record Claim(string Name, bool Works, string? Problem);

    private const string CheckPath = "/.rabbidlab-redirect";
    private const string CheckHeader = "X-Rabbid-Lab-Redirect";
    private readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
    private readonly Dictionary<string, HttpListener> open = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim wake = new(0);
    private volatile IReadOnlyList<Claim> claims = [];

    /// <summary>Each name asked for, as of the last look.</summary>
    public IReadOnlyList<Claim> Claims => claims;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        names.Changed += Poke;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                // A look that goes wrong is tried again at the next change, not the end of all of them.
                try
                {
                    await Sync(stop);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogWarning(e, "The names on port {Port} could not be looked at", plan.Port);
                }
                await wake.WaitAsync(stop);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
        finally
        {
            names.Changed -= Poke;
            foreach (var listener in open.Values)
            {
                listener.Close();
            }
            open.Clear();
        }
    }

    private void Poke() => wake.Release();

    /// <summary>Asks for names added since the last look, lets go of removed ones, and tries each.</summary>
    private async Task Sync(CancellationToken stop)
    {
        var domain = (IPGlobalProperties.GetIPGlobalProperties().DomainName ?? "").ToLowerInvariant();
        var wanted = Redirect.OwnNames(names.Install.Name(), names.All().SelectMany(n => new[] { n.Name, n.Full }), Environment.MachineName, domain);
        foreach (var gone in open.Keys.Except(wanted, StringComparer.OrdinalIgnoreCase).ToList())
        {
            open[gone].Close();
            open.Remove(gone);
        }
        var now = new List<Claim>();
        foreach (var name in wanted)
        {
            if (!open.ContainsKey(name) && Listen(name) is { } problem)
            {
                log.LogWarning("Port {Port} is shared with another web server, and http://{Name}/ can't be redirected to {Product}: {Problem}", plan.Port, name, Product.Name, problem);
                now.Add(new(name, false, problem));
                continue;
            }
            if (await Reaches(name, stop) == false)
            {
                var why = $"Another web server on this computer handles every name on port {plan.Port} first.";
                log.LogWarning("Port {Port} is shared with another web server, and http://{Name}/ can't be redirected to {Product}: {Problem}", plan.Port, name, Product.Name, why);
                now.Add(new(name, false, why));
                continue;
            }
            now.Add(new(name, true, null));
        }
        if (now.Any(c => c.Works))
        {
            log.LogInformation("Port {Port} is shared with another web server: {Names} redirect to {Product}", plan.Port, string.Join(", ", now.Where(c => c.Works).Select(c => $"http://{c.Name}/")), Product.Name);
        }
        claims = now;
    }

    /// <summary>Asks the Windows HTTP service for the name on the port; null when it's done, or why not.</summary>
    private string? Listen(string name)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://{name}:{plan.Port}/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException e)
        {
            listener.Close();
            return e.NativeErrorCode == 183 ? $"Another web server on this computer already serves http://{name}/." : e.Message;
        }
        open[name] = listener;
        _ = Serve(listener);
        return null;
    }

    private async Task Serve(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            var response = context.Response;
            try
            {
                var url = context.Request.Url!;
                if (url.AbsolutePath == CheckPath)
                {
                    response.StatusCode = 204;
                    response.Headers[CheckHeader] = token;
                }
                else
                {
                    var raw = context.Request.RawUrl;
                    response.Redirect(Redirect.Target(plan, url.Host, raw is { Length: > 0 } && raw[0] == '/' ? raw : url.PathAndQuery));
                }
                response.Close();
            }
            catch (Exception e) when (e is HttpListenerException or InvalidOperationException or ObjectDisposedException)
            {
                // The browser went away.
            }
        }
    }

    /// <summary>Whether a request for the name on this computer's port comes here; null when it couldn't be tried.</summary>
    private async Task<bool?> Reaches(string name, CancellationToken stop)
    {
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{plan.Port}{CheckPath}");
            request.Headers.Host = name;
            using var response = await http.SendAsync(request, stop);
            return response.Headers.TryGetValues(CheckHeader, out var said) && said.Contains(token);
        }
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !stop.IsCancellationRequested))
        {
            return null;
        }
    }
}
