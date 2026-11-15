using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RabbidLab.Shared;

/// <summary>
/// Wake-on-LAN: the magic packet (six 0xFF bytes, then the network card's MAC sixteen times) as a UDP broadcast to port
/// 9. Routers don't pass broadcasts on, so it has to go out from a computer on the same network as the one waking:
/// the server for its own networks, an online client for the rest.
/// </summary>
public static class WakeOnLan
{
    public const int Port = 9;

    /// <summary>At most this many network cards a computer, and MACs in one wake-up.</summary>
    public const int Most = 8;

    /// <summary>The MAC as six bytes, from AA:BB:CC:DD:EE:FF, AA-BB-CC-DD-EE-FF or twelve hex digits; null when it isn't one.</summary>
    public static byte[]? Mac(string? text)
    {
        var hex = new string((text ?? "").Where(ch => ch != ':' && ch != '-' && ch != '.' && !char.IsWhiteSpace(ch)).ToArray());
        if (hex.Length != 12 || !hex.All(Uri.IsHexDigit))
        {
            return null;
        }
        var mac = new byte[6];
        for (var i = 0; i < 6; i++)
        {
            mac[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        // All zeros or all ones is no card's address.
        return mac.All(b => b == 0) || mac.All(b => b == 0xFF) ? null : mac;
    }

    /// <summary>As AA:BB:CC:DD:EE:FF.</summary>
    public static string Text(byte[] mac) => string.Join(":", mac.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    public static byte[] Packet(byte[] mac)
    {
        var packet = new byte[102];
        for (var i = 0; i < 6; i++)
        {
            packet[i] = 0xFF;
        }
        for (var i = 1; i <= 16; i++)
        {
            Buffer.BlockCopy(mac, 0, packet, i * 6, 6);
        }
        return packet;
    }

    /// <summary>The network's broadcast address, such as 192.168.2.255 for 192.168.2.28/24; null when it isn't an IPv4 network.</summary>
    public static IPAddress? Broadcast(IPAddress address, int prefix)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || prefix is < 1 or > 30)
        {
            return null;
        }
        var bytes = address.GetAddressBytes();
        var host = uint.MaxValue >> prefix;
        var all = ((uint) bytes[0] << 24 | (uint) bytes[1] << 16 | (uint) bytes[2] << 8 | bytes[3]) | host;
        return new IPAddress(new[] { (byte) (all >> 24), (byte) (all >> 16), (byte) (all >> 8), (byte) all });
    }

    /// <summary>The network as people write it, such as 192.168.2.0/24.</summary>
    public static string Network(IPAddress address, int prefix)
    {
        if (Broadcast(address, prefix) is not { } all)
        {
            return address.ToString();
        }
        var b = all.GetAddressBytes();
        var host = uint.MaxValue >> prefix;
        var start = ((uint) b[0] << 24 | (uint) b[1] << 16 | (uint) b[2] << 8 | b[3]) & ~host;
        return $"{start >> 24}.{(start >> 16) & 255}.{(start >> 8) & 255}.{start & 255}/{prefix}";
    }

    /// <summary>Whether two IPv4 addresses are on the same network, by the first one's prefix length.</summary>
    public static bool SameNetwork(IPAddress a, IPAddress b, int prefix) =>
        Broadcast(a, prefix) is { } one && b.AddressFamily == AddressFamily.InterNetwork && one.Equals(Broadcast(b, prefix));

    /// <summary>This computer's IPv4 networks: an address and prefix length for each network card that's up.</summary>
    public static List<(IPAddress Address, int Prefix)> Networks()
    {
        var found = new List<(IPAddress, int)>();
        foreach (var card in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (card.OperationalStatus != OperationalStatus.Up || card.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }
            foreach (var a in card.GetIPProperties().UnicastAddresses)
            {
                if (a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal) && Prefix(a) is { } prefix)
                {
                    found.Add((a.Address, prefix));
                }
            }
        }
        return found;
    }

    /// <summary>The address's prefix length, from its mask.</summary>
    public static int? Prefix(UnicastIPAddressInformation a)
    {
        try
        {
            var mask = a.IPv4Mask?.GetAddressBytes();
            if (mask is not { Length: 4 })
            {
                return null;
            }
            var bits = mask.Sum(b => Convert.ToString(b, 2).Count(c => c == '1'));
            return bits is >= 1 and <= 30 ? bits : null;
        }
        catch (NotImplementedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sends the wake-up for each MAC out of this computer's address on a network: to that network's broadcast address and
    /// to 255.255.255.255, three times each. How many went out.
    /// </summary>
    public static int Send(IPAddress from, int prefix, IEnumerable<byte[]> macs)
    {
        var to = new List<IPAddress> { IPAddress.Broadcast };
        if (Broadcast(from, prefix) is { } own)
        {
            to.Add(own);
        }
        var sent = 0;
        using var udp = new UdpClient(new IPEndPoint(from, 0)) { EnableBroadcast = true };
        foreach (var packet in macs.Take(Most).Select(Packet))
        {
            for (var round = 0; round < 3; round++)
            {
                foreach (var address in to)
                {
                    try
                    {
                        udp.Send(packet, packet.Length, new IPEndPoint(address, Port));
                        sent++;
                    }
                    catch (SocketException)
                    {
                        // That way out is closed: the others are still tried.
                    }
                }
            }
        }
        return sent;
    }
}
