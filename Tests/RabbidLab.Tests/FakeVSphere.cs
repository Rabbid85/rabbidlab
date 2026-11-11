using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RabbidLab.Tests;

/// <summary>A stand-in vCenter answering the few vSphere API calls a VMware sensor makes, picked by what the request asks.</summary>
internal sealed class FakeVSphere : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();

    public FakeVSphere(bool refuseLogin = false)
    {
        listener.Start();
        Port = ((IPEndPoint) listener.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                try
                {
                    await Serve(client.GetStream(), refuseLogin);
                }
                catch (IOException)
                {
                    // A connection dropped without a request: the next one is served.
                }
            }
        });
    }

    public int Port { get; }

    private static async Task Serve(NetworkStream stream, bool refuseLogin)
    {
        var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        if (string.IsNullOrEmpty(await reader.ReadLineAsync()))
        {
            return;
        }
        var length = 0;
        for (var line = await reader.ReadLineAsync(); !string.IsNullOrEmpty(line); line = await reader.ReadLineAsync())
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line[15..].Trim());
            }
        }
        var buffer = new char[length];
        var read = 0;
        while (read < length && await reader.ReadAsync(buffer.AsMemory(read)) is var n and > 0)
        {
            read += n;
        }
        var (code, body) = Answer(new string(buffer, 0, read), refuseLogin);
        var bytes = Encoding.UTF8.GetBytes(body);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {code}\r\nContent-Type: text/xml; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"));
        await stream.WriteAsync(bytes);
    }

    private static (string Code, string Body) Answer(string request, bool refuseLogin)
    {
        static string Envelope(string body) => "<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
            + $"<soapenv:Body>{body}</soapenv:Body></soapenv:Envelope>";
        static string Prop(string name, string value) => $"<propSet><name>{name}</name><val>{value}</val></propSet>";
        if (request.Contains("RetrieveServiceContent", StringComparison.Ordinal))
        {
            return ("200 OK", Envelope("<RetrieveServiceContentResponse xmlns=\"urn:vim25\"><returnval><rootFolder type=\"Folder\">group-d1</rootFolder>"
                + "<propertyCollector type=\"PropertyCollector\">propertyCollector</propertyCollector><viewManager type=\"ViewManager\">ViewManager</viewManager>"
                + "<about><fullName>VMware vCenter Server 8.0.3 build-24322831</fullName></about><sessionManager type=\"SessionManager\">SessionManager</sessionManager></returnval></RetrieveServiceContentResponse>"));
        }
        if (request.Contains("<urn:Login>", StringComparison.Ordinal))
        {
            return refuseLogin
                ? ("500 Internal Server Error", Envelope("<soapenv:Fault><faultcode>ServerFaultCode</faultcode><faultstring>Cannot complete login due to an incorrect user name or password.</faultstring>"
                    + "<detail><InvalidLoginFault xmlns=\"urn:vim25\" xsi:type=\"InvalidLogin\"></InvalidLoginFault></detail></soapenv:Fault>"))
                : ("200 OK", Envelope("<LoginResponse xmlns=\"urn:vim25\"><returnval><userName>monitor</userName></returnval></LoginResponse>"));
        }
        if (request.Contains("CreateContainerView", StringComparison.Ordinal))
        {
            return ("200 OK", Envelope($"<CreateContainerViewResponse xmlns=\"urn:vim25\"><returnval type=\"ContainerView\">session[1]{(request.Contains("HostSystem", StringComparison.Ordinal) ? "hosts" : "stores")}</returnval></CreateContainerViewResponse>"));
        }
        if (request.Contains("<urn:type>HostSystem</urn:type>", StringComparison.Ordinal))
        {
            return ("200 OK", Envelope("<RetrievePropertiesExResponse xmlns=\"urn:vim25\"><returnval>"
                + $"<objects><obj type=\"HostSystem\">host-1</obj>{Prop("name", "esx01")}{Prop("runtime.connectionState", "connected")}{Prop("overallStatus", "green")}</objects>"
                + $"<objects><obj type=\"HostSystem\">host-2</obj>{Prop("name", "esx02")}{Prop("runtime.connectionState", "notResponding")}{Prop("overallStatus", "gray")}</objects>"
                + "</returnval></RetrievePropertiesExResponse>"));
        }
        if (request.Contains("<urn:type>Datastore</urn:type>", StringComparison.Ordinal))
        {
            return ("200 OK", Envelope("<RetrievePropertiesExResponse xmlns=\"urn:vim25\"><returnval>"
                + $"<objects><obj type=\"Datastore\">ds-1</obj>{Prop("name", "datastore1")}{Prop("summary.capacity", "1000")}{Prop("summary.freeSpace", "180")}{Prop("summary.accessible", "true")}</objects>"
                + "</returnval></RetrievePropertiesExResponse>"));
        }
        if (request.Contains("triggeredAlarmState", StringComparison.Ordinal))
        {
            return ("200 OK", Envelope("<RetrievePropertiesExResponse xmlns=\"urn:vim25\"><returnval><objects><obj type=\"Folder\">group-d1</obj><propSet><name>triggeredAlarmState</name>"
                + "<val xsi:type=\"ArrayOfAlarmState\"><AlarmState><key>alarm-1</key><overallStatus>yellow</overallStatus></AlarmState></val></propSet></objects></returnval></RetrievePropertiesExResponse>"));
        }
        return ("200 OK", Envelope("<LogoutResponse xmlns=\"urn:vim25\"></LogoutResponse>"));
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
    }
}
