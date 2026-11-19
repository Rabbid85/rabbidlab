using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace RabbidLab.Server;

/// <summary>
/// HTTPS, or nothing secret over the network. A reverse proxy that does HTTPS for Rabbid Lab says so in X-Forwarded-Proto,
/// and that's believed only from proxies it trusts: this computer, and the addresses or networks in Proxy:Trusted
/// (console.json, or the Proxy__Trusted environment variable, such as 172.18.0.0/16). Over plain HTTP from another
/// computer nobody signs in and no session works, so no password or session cookie crosses the network unencrypted,
/// unless AllowPlainHttp is set to true. From this computer itself it always works: nothing leaves the machine.
/// </summary>
public static class PlainHttp
{
    public const string Refused = "This console is on plain HTTP, so your password and session would cross the network unencrypted. Open it with its https:// address.";

    /// <summary>X-Forwarded-Proto and X-Forwarded-For, from this computer and the proxies in Proxy:Trusted only.</summary>
    public static void AddProxies(this IServiceCollection services) =>
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, config) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var item in (config["Proxy:Trusted"] ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IPAddress.TryParse(item, out var one))
                {
                    options.KnownProxies.Add(one);
                }
                else if (System.Net.IPNetwork.TryParse(item, out var network))
                {
                    options.KnownIPNetworks.Add(network);
                }
            }
        });

    /// <summary>Whether this request is plain HTTP from another computer, where no password or session may go.</summary>
    public static bool Exposed(HttpContext context) => Over(context) && !context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("AllowPlainHttp");

    /// <summary>What the sign-in page says: "refused" over plain HTTP from another computer, "allowed" when AllowPlainHttp lets it, or null.</summary>
    public static string? Said(HttpContext context) => !Over(context) ? null : Exposed(context) ? "refused" : "allowed";

    // Plain HTTP, from another computer.
    private static bool Over(HttpContext context)
    {
        if (context.Request.IsHttps)
        {
            return false;
        }
        var from = context.Connection.RemoteIpAddress;
        if (from is null)
        {
            return false; // in the same process (tests), or a local socket
        }
        return !IPAddress.IsLoopback(from.IsIPv4MappedToIPv6 ? from.MapToIPv4() : from);
    }
}
