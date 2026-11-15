using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using RabbidLab.Shared;

namespace RabbidLab.Client;

/// <summary>
/// The server asking this computer to wake another one on its network (Wake-on-LAN), because the server isn't on that
/// network and broadcasts don't cross routers. Nothing on this computer changes, so Group Policy's account switch doesn't apply.
/// </summary>
internal static class Wake
{
    public static bool Is(AccountOrder order) => order.Action == AccountOrder.Wake;

    public static AccountAnswer Do(AccountOrder order)
    {
        var macs = (order.Macs ?? new List<string>()).Select(WakeOnLan.Mac).OfType<byte[]>().GroupBy(WakeOnLan.Text).Select(g => g.First()).Take(WakeOnLan.Most).ToList();
        if (macs.Count == 0)
        {
            return AccountAnswer.Failed(order.Id, "There was no network card to wake.");
        }
        var sent = 0;
        try
        {
            foreach (var (address, prefix) in WakeOnLan.Networks())
            {
                sent += WakeOnLan.Send(address, prefix, macs);
            }
        }
        catch (Exception e) when (e is SocketException or NetworkInformationException)
        {
            return AccountAnswer.Failed(order.Id, $"{Machine.Name} couldn't send it: {e.Message.Trim().TrimEnd('.')}.");
        }
        return sent > 0 ? new AccountAnswer { Id = order.Id, Ok = true } : AccountAnswer.Failed(order.Id, $"{Machine.Name} has no network to send it on.");
    }
}
