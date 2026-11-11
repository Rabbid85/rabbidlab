using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>
/// Server hardware over Redfish, the API iDRAC, iLO and Supermicro BMCs share: the system's own health, power supplies,
/// fans, temperatures and disks. Read-only; a user with the Read Only role is enough.
/// </summary>
public static class Redfish
{
    /// <summary>What a hardware sensor can watch, by key.</summary>
    public static readonly string[] Parts = ["health", "power", "fans", "temps", "disks"];

    public static async Task<Probe> Check(Sensor s, Login login, bool trying, CancellationToken stop)
    {
        using var device = new DeviceHttp(s.Pin, trying, TimeSpan.FromSeconds(30));
        var http = device.Http;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login.Username}:{login.Password}")));
        var root = DeviceHttp.Root(s.Target);
        var watch = Stopwatch.StartNew();
        try
        {
            var systems = await Get(http, root, "/redfish/v1/Systems", stop);
            if (Members(systems).FirstOrDefault() is not { } at)
            {
                return new Probe(false, Note: "No system in Redfish", Pin: device.Offer);
            }
            var system = await Get(http, root, at, stop);
            var chassisAt = LinkIn(system, "Links", "Chassis") ?? Members(await Get(http, root, "/redfish/v1/Chassis", stop)).FirstOrDefault();
            var chassis = chassisAt is null ? default : await Get(http, root, chassisAt, stop);
            var parts = await Task.WhenAll(
                Quietly(() => Power(http, root, chassis, stop)),
                Quietly(() => Thermal(http, root, chassis, stop)),
                Quietly(() => Disks(http, root, system, stop)));
            var manager = await Quietly(async () => LinkIn(system, "Links", "ManagedBy") is { } m ? new[] { await Get(http, root, m, stop) } : Array.Empty<JsonElement>());
            var found = new List<ProbePart>();
            if ((Text(system, "Status", "HealthRollup") ?? Text(system, "Status", "Health")) is { } overall)
            {
                found.Add(new ProbePart("health", "Overall health", overall, State(overall)));
            }
            found.AddRange(parts.SelectMany(p => p));
            var ms = (int) watch.ElapsedMilliseconds;
            var watched = found.Where(p => s.Watch is null || s.Watch.Contains(p.Key)).ToList();
            var worst = watched.FirstOrDefault(p => p.State == "bad") ?? watched.FirstOrDefault(p => p.State == "warn");
            var maker = Text(system, "Manufacturer") ?? "";
            var dell = maker.StartsWith("Dell", StringComparison.OrdinalIgnoreCase);
            var bmc = manager.Length > 0 && Text(manager[0], "Model") is { } model && model.Contains("iLO", StringComparison.OrdinalIgnoreCase) ? model
                : dell ? "the iDRAC" : "the BMC";
            var tag = dell ? Text(system, "SKU") is { } sku ? $"Service tag {sku}" : null : Text(system, "SerialNumber") is { } serial ? $"Serial {serial}" : null;
            var firmware = manager.Length > 0 && Text(manager[0], "FirmwareVersion") is { } fw ? $"firmware {fw}" : null;
            return new Probe(worst?.State != "bad", ms, worst is null ? "All OK" : $"{worst.Name}: {worst.Now}", Warn: worst?.State == "warn",
                Detail: string.Join(" · ", new[] { tag, firmware, $"as {login.Username}" }.OfType<string>()),
                Title: $"Signed in to {bmc} on a {Text(system, "Model") ?? "server"}", Parts: found, Pin: device.Offer);
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return new Probe(false, Note: "No Redfish there: is it an iDRAC, iLO or BMC address?", Pin: device.Offer);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException && device.Said(e) is not null)
        {
            return new Probe(false, Note: device.Said(e), Pin: device.Offer);
        }
    }

    private static async Task<IEnumerable<ProbePart>> Power(HttpClient http, string root, JsonElement chassis, CancellationToken stop)
    {
        IEnumerable<JsonElement> supplies;
        if (Link(chassis, "Power") is { } power)
        {
            supplies = Items(await Get(http, root, power, stop), "PowerSupplies");
        }
        else if (Link(chassis, "PowerSubsystem") is { } sub && Link(await Get(http, root, sub, stop), "PowerSupplies") is { } list)
        {
            supplies = await All(http, root, Members(await Get(http, root, list, stop)), stop);
        }
        else
        {
            return [];
        }
        return Count("power", "Power supplies", supplies) is { } part ? [part] : [];
    }

    private static async Task<IEnumerable<ProbePart>> Thermal(HttpClient http, string root, JsonElement chassis, CancellationToken stop)
    {
        if (Link(chassis, "Thermal") is not { } at)
        {
            return [];
        }
        var thermal = await Get(http, root, at, stop);
        var found = new List<ProbePart>();
        if (Count("fans", "Fans", Items(thermal, "Fans")) is { } fans)
        {
            found.Add(fans);
        }
        var temps = Present(Items(thermal, "Temperatures")).Where(t => t.TryGetProperty("ReadingCelsius", out var r) && r.ValueKind == JsonValueKind.Number).ToList();
        if (temps.Count > 0)
        {
            double Reading(JsonElement t) => t.GetProperty("ReadingCelsius").GetDouble();
            var inlet = temps.FirstOrDefault(t => (Text(t, "Name") ?? "").Contains("Inlet", StringComparison.OrdinalIgnoreCase));
            var exhaust = temps.FirstOrDefault(t => (Text(t, "Name") ?? "").Contains("Exhaust", StringComparison.OrdinalIgnoreCase));
            var hottest = temps.MaxBy(Reading);
            var now = inlet.ValueKind == JsonValueKind.Object
                ? $"Inlet {Reading(inlet):0} °C" + (exhaust.ValueKind == JsonValueKind.Object ? $" · exhaust {Reading(exhaust):0} °C" : "")
                : $"Highest {Reading(hottest):0} °C ({Text(hottest, "Name")})";
            var worst = temps.Select(t => State(Text(t, "Status", "Health"))).OrderBy(Rank).Last();
            found.Add(new ProbePart("temps", "Temperature", now, worst));
        }
        return found;
    }

    private static async Task<IEnumerable<ProbePart>> Disks(HttpClient http, string root, JsonElement system, CancellationToken stop)
    {
        if (Link(system, "Storage") is not { } at)
        {
            return [];
        }
        var controllers = await All(http, root, Members(await Get(http, root, at, stop)), stop);
        var drives = await All(http, root, controllers.SelectMany(c => Items(c, "Drives")).Select(d => Text(d, "@odata.id")).OfType<string>(), stop);
        return Count("disks", "Disks", drives) is { } part ? [part] : [];
    }

    // How many are OK, and the first that isn't: 1 of 2 OK · PS2 Critical.
    private static ProbePart? Count(string key, string name, IEnumerable<JsonElement> items)
    {
        var present = Present(items).ToList();
        if (present.Count == 0)
        {
            return null;
        }
        var states = present.Select(i => (Item: i, State: State(Text(i, "Status", "Health")))).ToList();
        var ok = states.Count(x => x.State == "ok");
        var worst = states.OrderBy(x => Rank(x.State)).Last();
        var now = $"{ok} of {present.Count} OK" + (worst.State == "ok" ? "" : $" · {Text(worst.Item, "Name") ?? Text(worst.Item, "MemberId") ?? "one"} {Text(worst.Item, "Status", "Health")}");
        return new ProbePart(key, name, now, worst.State);
    }

    private static IEnumerable<JsonElement> Present(IEnumerable<JsonElement> items) =>
        items.Where(i => Text(i, "Status", "State") is not ("Absent" or "Disabled" or "UnavailableOffline"));

    private static string State(string? health) => health switch { "Critical" => "bad", "Warning" => "warn", _ => "ok" };

    private static int Rank(string state) => state switch { "bad" => 2, "warn" => 1, _ => 0 };

    // A part that can't be read is left out, rather than failing the whole check; signing in has worked by then.
    private static async Task<T[]> Quietly<T>(Func<Task<T[]>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return [];
        }
    }

    private static async Task<ProbePart[]> Quietly(Func<Task<IEnumerable<ProbePart>>> read) => await Quietly(async () => (await read()).ToArray());

    private static async Task<JsonElement> Get(HttpClient http, string root, string path, CancellationToken stop)
    {
        using var response = await http.GetAsync(root + path, stop);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int) response.StatusCode} from {path}", null, response.StatusCode);
        }
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(stop), cancellationToken: stop);
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement[]> All(HttpClient http, string root, IEnumerable<string> paths, CancellationToken stop)
    {
        using var room = new SemaphoreSlim(6);
        return await Task.WhenAll(paths.Take(64).Select(async p =>
        {
            await room.WaitAsync(stop);
            try
            {
                return await Get(http, root, p, stop);
            }
            finally
            {
                room.Release();
            }
        }));
    }

    private static IEnumerable<string> Members(JsonElement collection) => Items(collection, "Members").Select(m => Text(m, "@odata.id")).OfType<string>();

    private static IEnumerable<JsonElement> Items(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : [];

    private static string? Link(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.Object ? Text(x, "@odata.id") : null;

    // The first link in a list inside an object: Links.Chassis[0].
    private static string? LinkIn(JsonElement e, string outer, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(outer, out var o) ? Items(o, name).Select(i => Text(i, "@odata.id")).FirstOrDefault(i => i is not null) : null;

    private static string? Text(JsonElement e, params string[] path)
    {
        foreach (var name in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out e))
            {
                return null;
            }
        }
        return e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }
}
