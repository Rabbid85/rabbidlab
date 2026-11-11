using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;

namespace RabbidLab.Server;

/// <summary>
/// Typing just Rabbid Lab's name in a browser (rabbidlab.home.lan) tries plain http on port 80. When Setup asks for it
/// (Redirect:Port in console.json) and nothing else on the machine has that port, Rabbid Lab listens there too and sends
/// every request on to its real address, like https://rabbidlab.home.lan:8420. Nothing else is served on it.
/// </summary>
public static class Redirect
{
    public sealed record Plan(int Port, string Scheme, int To, bool Taken);

    /// <summary>Listens on the redirect port too, if console.json asks for it and the port is free.</summary>
    public static Plan? AddRedirectPort(this WebApplicationBuilder builder)
    {
        var port = builder.Configuration.GetValue<int>("Redirect:Port");
        var url = (builder.Configuration["Kestrel:Endpoints:Console:Url"] ?? "").Replace("://*", "://localhost").Replace("://+", "://localhost");
        if (port <= 0 || !Uri.TryCreate(url, UriKind.Absolute, out var console) || console.Port == port)
        {
            return null;
        }
        var held = Bind(port);
        var taken = held is null;
        if (!taken)
        {
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenAnyIP(port));
            // Kestrel listens on the socket bound here, so no other program can take the port before it does.
            builder.Services.Configure<SocketTransportOptions>(sockets => sockets.CreateBoundListenSocket = endpoint =>
                endpoint is IPEndPoint ip && ip.Port == port && Interlocked.Exchange(ref held, null) is { } bound
                    ? bound : SocketTransportOptions.CreateDefaultBoundListenSocket(endpoint));
        }
        return new Plan(port, console.Scheme, console.Port, taken);
    }

    /// <summary>
    /// The port, bound the way Kestrel binds it, or null when another program has it. The listener table missed another
    /// console's port 80 redirect, and a failed bind stops Rabbid Lab altogether.
    /// </summary>
    private static Socket? Bind(int port)
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

    /// <summary>Sends requests on the redirect port to Rabbid Lab's address, by the name they used.</summary>
    public static void UseRedirectPort(this WebApplication app, Plan? plan)
    {
        if (plan is null)
        {
            return;
        }
        if (plan.Taken)
        {
            app.Logger.LogWarning("Port {Port} is already in use by another application on this machine, so the redirect to {Product} is not available. Enter the full address instead", plan.Port, Product.Name);
            return;
        }
        var port = plan.To == (plan.Scheme == "https" ? 443 : 80) ? "" : ":" + plan.To;
        app.Use(async (context, next) =>
        {
            if (context.Connection.LocalPort != plan.Port)
            {
                await next(context);
                return;
            }
            var request = context.Request;
            context.Response.Redirect($"{plan.Scheme}://{request.Host.Host}{port}{request.PathBase}{request.Path}{request.QueryString}");
        });
    }
}
