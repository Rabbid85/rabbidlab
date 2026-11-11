using System.Net;
using System.Net.NetworkInformation;

namespace RabbidLab.Server;

/// <summary>IP addresses as Rabbid Lab compares them, and which ones are this computer's own.</summary>
internal static class Addresses
{
    private static readonly Lock Gate = new();
    private static HashSet<string> own = [];
    private static long listed = long.MinValue;

    /// <summary>An address as IPv4 when it's IPv4 underneath, so the browser and the client on one computer match.</summary>
    public static string? Normal(string? address) => IPAddress.TryParse(address, out var ip) ? Normal(ip).ToString() : null;

    public static IPAddress Normal(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    /// <summary>Whether the address is this computer's own: loopback, or one of its network cards'.</summary>
    public static bool OnServer(IPAddress? ip)
    {
        if (ip is null)
        {
            return false;
        }
        var it = Normal(ip);
        return IPAddress.IsLoopback(it) || Own().Contains(it.ToString());
    }

    /// <summary>This computer's addresses, listed again once a minute at most.</summary>
    private static HashSet<string> Own()
    {
        lock (Gate)
        {
            if (listed != long.MinValue && Environment.TickCount64 - listed < 60_000)
            {
                return own;
            }
            try
            {
                own = [.. NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => Normal(a.Address).ToString())];
            }
            catch (NetworkInformationException)
            {
                own = [];
            }
            listed = Environment.TickCount64;
            return own;
        }
    }
}
