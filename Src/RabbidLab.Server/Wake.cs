using System.Net;
using System.Net.Sockets;
using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>Sends Wake-on-LAN from this server and says which networks it's on. Tests swap it for one that only notes what was sent.</summary>
public interface IWakeSender
{
    IReadOnlyList<(IPAddress Address, int Prefix)> Networks();

    /// <summary>How many packets went out.</summary>
    int Send(IPAddress from, int prefix, IReadOnlyList<byte[]> macs);
}

public sealed class WakeSender : IWakeSender
{
    public IReadOnlyList<(IPAddress Address, int Prefix)> Networks() => WakeOnLan.Networks();

    public int Send(IPAddress from, int prefix, IReadOnlyList<byte[]> macs) => WakeOnLan.Send(from, prefix, macs);
}

/// <summary>How one computer's wake-up went: who sent it, or why nobody could.</summary>
public sealed record WakeResult(string Client, string Computer, List<string> Via, string? Problem);

public sealed record WakeForm(List<string>? Clients);

/// <summary>
/// Wakes computers in Devices with Wake-on-LAN, from the network cards their clients last reported. One on a network
/// this server is on gets it from the server; one on another network from an online client there, because routers
/// don't pass broadcasts on. Nothing answers a magic packet: whether it wakes is up to its BIOS and network card,
/// and the page sees it come online.
/// </summary>
public sealed class Waker(Clients clients, Inventories inventories, ClientCommands commands, IWakeSender sender)
{
    /// <summary>The first client that reports its network cards and passes wake-ups on.</summary>
    public static readonly Version From = new(1, 1, 195);

    /// <summary>At most this many computers in one go.</summary>
    public const int Most = 50;

    private const int Relays = 2;
    private static readonly TimeSpan RelayWait = TimeSpan.FromSeconds(10);
    public const string Server = "this server";

    public async Task<List<WakeResult>> Wake(IReadOnlyList<Client> targets, CancellationToken stop)
    {
        IReadOnlyList<(IPAddress Address, int Prefix)> mine;
        try
        {
            mine = sender.Networks();
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
            mine = [];
        }
        return [.. await Task.WhenAll(targets.Select(c => One(c, mine, stop)))];
    }

    private async Task<WakeResult> One(Client c, IReadOnlyList<(IPAddress Address, int Prefix)> mine, CancellationToken stop)
    {
        var nics = inventories.Get(c.Id)?.Nics ?? [];
        List<byte[]> macs = [.. nics.Select(n => WakeOnLan.Mac(n.Mac)).OfType<byte[]>()];
        if (macs.Count == 0)
        {
            return new(c.Id, c.Computer, [], $"The network card of {c.Computer} is not known yet. Client {From} or later reports it a few minutes after it starts.");
        }
        // The networks it was on: its cards' addresses, and the address it last came to this server from.
        List<(IPAddress Address, int Prefix)> theirs = [.. nics.Where(n => n.Prefix > 0 && IPAddress.TryParse(n.Ip, out _)).Select(n => (IPAddress.Parse(n.Ip!), n.Prefix))];
        var seen = IPAddress.TryParse(c.Address, out var a) ? (a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a) : null;
        List<string> via = [];
        foreach (var (address, prefix) in mine.Where(m => theirs.Any(t => WakeOnLan.SameNetwork(m.Address, t.Address, m.Prefix)) || (seen is not null && WakeOnLan.SameNetwork(m.Address, seen, m.Prefix))))
        {
            try
            {
                if (sender.Send(address, prefix, macs) > 0 && !via.Contains(Server))
                {
                    via.Add(Server);
                }
            }
            catch (SocketException)
            {
                // Its other networks, and the clients, are still tried.
            }
        }
        // Its networks this server isn't on: up to two online clients there pass it on.
        var away = theirs.Where(t => !mine.Any(m => WakeOnLan.SameNetwork(m.Address, t.Address, m.Prefix))).ToList();
        if (away.Count > 0)
        {
            var relays = clients.All().Where(r => r.Id != c.Id && r.Approved is not null && clients.IsOnline(r) && commands.Listening(r.Id)
                    && Version.TryParse(r.Version, out var v) && v >= From
                    && (inventories.Get(r.Id)?.Nics ?? []).Any(n => IPAddress.TryParse(n.Ip, out var ip) && away.Any(t => WakeOnLan.SameNetwork(t.Address, ip, t.Prefix))))
                .Take(Relays).ToList();
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stop);
            wait.CancelAfter(RelayWait);
            var answers = await Task.WhenAll(relays.Select(r => commands.Send(r.Id, new AccountOrder { Action = AccountOrder.Wake, Macs = [.. macs.Select(WakeOnLan.Text)] }, wait.Token)));
            via.AddRange(relays.Zip(answers).Where(x => x.Second?.Ok == true).Select(x => x.First.Computer));
        }
        if (via.Count > 0)
        {
            return new(c.Id, c.Computer, via, null);
        }
        return new(c.Id, c.Computer, [], away.Count > 0
            ? $"{c.Computer} was last seen on {WakeOnLan.Network(away[0].Address, away[0].Prefix)}. This server is not on that network, and no online computer there has client {From} or later to relay the wake-up packet."
            : theirs.Count == 0 && seen is null ? $"The network of {c.Computer} is unknown." : "Could not send the wake-up packet on that network.");
    }
}
