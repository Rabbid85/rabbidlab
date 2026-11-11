using System.Diagnostics;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace RabbidLab.Server;

/// <summary>
/// vCenter or a standalone ESXi host over the vSphere API (SOAP, which both answer the same way): whether the hosts are
/// connected and well, datastore free space, and triggered alarms. A user with the Read-only role is enough.
/// </summary>
public static class VMware
{
    public static readonly string[] Parts = ["hosts", "space", "alarms"];
    private static readonly XNamespace Vim = "urn:vim25";

    public static async Task<Probe> Check(Sensor s, Login login, bool trying, CancellationToken stop)
    {
        using var device = new DeviceHttp(s.Pin, trying, TimeSpan.FromSeconds(30));
        var sdk = DeviceHttp.Root(s.Target) + "/sdk";
        var watch = Stopwatch.StartNew();
        try
        {
            var content = await Call(device.Http, sdk, "<urn:RetrieveServiceContent><urn:_this type=\"ServiceInstance\">ServiceInstance</urn:_this></urn:RetrieveServiceContent>", stop);
            string Ref(string name) => content.Descendants(Vim + name).FirstOrDefault()?.Value ?? "";
            var collector = Ref("propertyCollector");
            var sessions = Ref("sessionManager");
            var root = Ref("rootFolder");
            var views = Ref("viewManager");
            var product = content.Descendants(Vim + "about").Elements(Vim + "fullName").FirstOrDefault()?.Value ?? "vSphere";
            try
            {
                await Call(device.Http, sdk, $"<urn:Login><urn:_this type=\"SessionManager\">{X(sessions)}</urn:_this><urn:userName>{X(login.Username)}</urn:userName><urn:password>{X(login.Password)}</urn:password></urn:Login>", stop);
            }
            catch (SoapFault f) when (f.Code == "InvalidLogin")
            {
                return new Probe(false, Note: "Sign-in failed: check the username and password", Pin: device.Offer);
            }
            try
            {
                var hosts = await Objects(device.Http, sdk, collector, views, root, "HostSystem", ["name", "runtime.connectionState", "overallStatus"], stop);
                var stores = await Objects(device.Http, sdk, collector, views, root, "Datastore", ["name", "summary.capacity", "summary.freeSpace", "summary.accessible"], stop);
                var alarms = await Call(device.Http, sdk, $"<urn:RetrievePropertiesEx><urn:_this type=\"PropertyCollector\">{X(collector)}</urn:_this><urn:specSet><urn:propSet><urn:type>Folder</urn:type>"
                    + $"<urn:pathSet>triggeredAlarmState</urn:pathSet></urn:propSet><urn:objectSet><urn:obj type=\"Folder\">{X(root)}</urn:obj></urn:objectSet></urn:specSet><urn:options/></urn:RetrievePropertiesEx>", stop);
                var found = new List<ProbePart>();
                if (hosts.Count > 0)
                {
                    var connected = hosts.Count(h => h.GetValueOrDefault("runtime.connectionState") == "connected");
                    var red = hosts.FirstOrDefault(h => h.GetValueOrDefault("overallStatus") == "red");
                    var yellow = hosts.FirstOrDefault(h => h.GetValueOrDefault("overallStatus") == "yellow");
                    var off = hosts.FirstOrDefault(h => h.GetValueOrDefault("runtime.connectionState") != "connected");
                    var said = off is not null ? $" · {off.GetValueOrDefault("name")} {off.GetValueOrDefault("runtime.connectionState")}"
                        : red is not null ? $" · {red.GetValueOrDefault("name")} has a critical alarm" : yellow is not null ? $" · {yellow.GetValueOrDefault("name")} has a warning" : "";
                    found.Add(new ProbePart("hosts", "Hosts connected", $"{connected} of {hosts.Count}{said}", off is not null || red is not null ? "bad" : yellow is not null ? "warn" : "ok"));
                }
                double? Number(Dictionary<string, string> d, string name) => double.TryParse(d.GetValueOrDefault(name), System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
                if (JsonBits.Space(stores.Where(d => d.GetValueOrDefault("summary.accessible") != "false").Select(d => (d.GetValueOrDefault("name") ?? "datastore", Number(d, "summary.capacity"), Number(d, "summary.freeSpace"))), s.WarnFree ?? 10) is { } space)
                {
                    found.Add(space with { Name = "Datastore free space" });
                }
                var states = alarms.Descendants(Vim + "overallStatus").Select(e => e.Value).ToList();
                var critical = states.Count(x => x == "red");
                var warning = states.Count(x => x == "yellow");
                found.Add(new ProbePart("alarms", "Triggered alarms", critical + warning == 0 ? "None"
                    : string.Join(", ", new[] { critical > 0 ? $"{critical} critical" : null, warning > 0 ? $"{warning} warning" : null }.OfType<string>()), critical > 0 ? "bad" : warning > 0 ? "warn" : "ok"));
                return JsonBits.Result(s, found, (int) watch.ElapsedMilliseconds, $"Signed in to {product}",
                    $"{hosts.Count} host{(hosts.Count == 1 ? "" : "s")} · {stores.Count} datastore{(stores.Count == 1 ? "" : "s")} · as {login.Username}", device.Offer);
            }
            finally
            {
                // Signed out, so a session isn't left behind for every check.
                await Quietly(() => Call(device.Http, sdk, $"<urn:Logout><urn:_this type=\"SessionManager\">{X(sessions)}</urn:_this></urn:Logout>", CancellationToken.None));
            }
        }
        catch (SoapFault f)
        {
            return new Probe(false, Note: $"vSphere said: {f.Message}", Pin: device.Offer);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or System.Xml.XmlException && device.Said(e) is not null)
        {
            return new Probe(false, Note: device.Said(e), Pin: device.Offer);
        }
    }

    // Every object of a type under the root, through a container view, with the properties asked for.
    private static async Task<List<Dictionary<string, string>>> Objects(HttpClient http, string sdk, string collector, string views, string root, string type, string[] paths, CancellationToken stop)
    {
        var view = (await Call(http, sdk, $"<urn:CreateContainerView><urn:_this type=\"ViewManager\">{X(views)}</urn:_this><urn:container type=\"Folder\">{X(root)}</urn:container>"
            + $"<urn:type>{type}</urn:type><urn:recursive>true</urn:recursive></urn:CreateContainerView>", stop)).Descendants(Vim + "returnval").First().Value;
        var answer = await Call(http, sdk, $"<urn:RetrievePropertiesEx><urn:_this type=\"PropertyCollector\">{X(collector)}</urn:_this><urn:specSet><urn:propSet><urn:type>{type}</urn:type>"
            + string.Concat(paths.Select(p => $"<urn:pathSet>{p}</urn:pathSet>")) + $"</urn:propSet><urn:objectSet><urn:obj type=\"ContainerView\">{X(view)}</urn:obj><urn:skip>true</urn:skip>"
            + "<urn:selectSet xsi:type=\"urn:TraversalSpec\"><urn:name>view</urn:name><urn:type>ContainerView</urn:type><urn:path>view</urn:path><urn:skip>false</urn:skip></urn:selectSet>"
            + "</urn:objectSet></urn:specSet><urn:options/></urn:RetrievePropertiesEx>", stop);
        return [.. answer.Descendants(Vim + "objects").Select(o => o.Elements(Vim + "propSet")
            .ToDictionary(p => p.Element(Vim + "name")?.Value ?? "", p => p.Element(Vim + "val")?.Value ?? ""))];
    }

    private static async Task<XDocument> Call(HttpClient http, string sdk, string body, CancellationToken stop)
    {
        var envelope = "<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:urn=\"urn:vim25\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
            + $"<soapenv:Body>{body}</soapenv:Body></soapenv:Envelope>";
        using var request = new HttpRequestMessage(HttpMethod.Post, sdk) { Content = new StringContent(envelope, Encoding.UTF8, "text/xml") };
        request.Headers.Add("SOAPAction", "urn:vim25/6.7");
        using var response = await http.SendAsync(request, stop);
        var text = await response.Content.ReadAsStringAsync(stop);
        var doc = text.TrimStart().StartsWith('<') ? XDocument.Parse(text) : null;
        if (doc?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault") is { } fault)
        {
            var detail = fault.Descendants().FirstOrDefault(e => e.Parent?.Name.LocalName == "detail")?.Name.LocalName.Replace("Fault", "", StringComparison.Ordinal) ?? "";
            throw new SoapFault(detail, fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring")?.Value ?? "a fault");
        }
        if (!response.IsSuccessStatusCode || doc is null)
        {
            throw new HttpRequestException($"HTTP {(int) response.StatusCode}", null, response.StatusCode);
        }
        return doc;
    }

    // A sign-out that doesn't answer within a few seconds is left to time out on the server.
    internal static async Task Quietly(Func<Task> call)
    {
        try
        {
            await call().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TimeoutException or OperationCanceledException or SoapFault or System.Xml.XmlException)
        {
            // Signing out is a courtesy; the check has its answer.
        }
    }

    private static string X(string? text) => SecurityElement.Escape(text ?? "");

    private sealed class SoapFault(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
