using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace RabbidLab.Tests;

/// <summary>A stand-in SNMP v2c device on this computer: answers gets for the values it was given, to the right community only.</summary>
internal sealed class FakeSnmpAgent : IDisposable
{
    private readonly UdpClient udp = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly CancellationTokenSource stop = new();

    public FakeSnmpAgent(string community, Dictionary<string, ISnmpData> values)
    {
        Port = ((IPEndPoint) udp.Client.LocalEndPoint!).Port;
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                UdpReceiveResult got;
                try
                {
                    got = await udp.ReceiveAsync(stop.Token);
                }
                catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    return;
                }
                var asked = MessageFactory.ParseMessages(got.Buffer, new UserRegistry())[0];
                if (asked.Community().ToString() != community)
                {
                    continue; // a wrong community gets no answer, as on a real device
                }
                var answer = asked.Pdu().Variables.Select(v => new Variable(v.Id, values.TryGetValue(v.Id.ToString(), out var data) ? data : new NoSuchObject())).ToList();
                var reply = new ResponseMessage(asked.RequestId(), VersionCode.V2, new OctetString(community), ErrorCode.NoError, 0, answer).ToBytes();
                await udp.SendAsync(reply, got.RemoteEndPoint);
            }
        });
    }

    public int Port { get; }

    public void Dispose()
    {
        stop.Cancel();
        udp.Dispose();
    }
}
