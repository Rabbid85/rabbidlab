using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>Reading JSON from a device's API, for the sensors that sign in.</summary>
internal static class JsonBits
{
    public static async Task<JsonElement> Get(HttpClient http, string url, CancellationToken stop) => await Read(await http.GetAsync(url, stop), stop);

    public static async Task<JsonElement> Post(HttpClient http, string url, Dictionary<string, string> form, CancellationToken stop) =>
        await Read(await http.PostAsync(url, new FormUrlEncodedContent(form), stop), stop);

    private static async Task<JsonElement> Read(HttpResponseMessage response, CancellationToken stop)
    {
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP {(int) response.StatusCode}", null, response.StatusCode);
            }
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(stop), cancellationToken: stop);
            return doc.RootElement.Clone();
        }
    }

    public static string? Text(JsonElement e, params string[] path) => At(e, path) is { ValueKind: JsonValueKind.String } x ? x.GetString() : null;

    // A number, or a string holding one (Synology sends sizes as strings).
    public static double? Number(JsonElement e, params string[] path) => At(e, path) switch
    {
        { ValueKind: JsonValueKind.Number } x => x.GetDouble(),
        { ValueKind: JsonValueKind.String } x when double.TryParse(x.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };

    public static IEnumerable<JsonElement> Items(JsonElement e, params string[] path) => At(e, path) is { ValueKind: JsonValueKind.Array } x ? x.EnumerateArray() : [];

    private static JsonElement? At(JsonElement e, string[] path)
    {
        foreach (var name in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out e))
            {
                return null;
            }
        }
        return e;
    }

    /// <summary>A check that signed in: down for anything watched that's bad, a warning for anything watched that needs a look.</summary>
    public static Probe Result(Sensor s, List<ProbePart> found, int ms, string title, string detail, string? pin)
    {
        var watched = found.Where(p => s.Watch is null || s.Watch.Contains(p.Key)).ToList();
        var worst = watched.FirstOrDefault(p => p.State == "bad") ?? watched.FirstOrDefault(p => p.State == "warn");
        return new Probe(worst?.State != "bad", ms, worst is null ? "All OK" : $"{worst.Name}: {worst.Now}", Warn: worst?.State == "warn", Detail: detail, Title: title, Parts: found, Pin: pin);
    }

    // The share of a pool or volume that's free, and the part that says so: Warn below 10 % · tank has 41 % free.
    public static ProbePart? Space(IEnumerable<(string Name, double? Size, double? Free)> spaces, int limit)
    {
        var known = spaces.Where(x => x.Size > 0 && x.Free is not null).Select(x => (x.Name, Percent: 100 * x.Free!.Value / x.Size!.Value)).ToList();
        if (known.Count == 0)
        {
            return null;
        }
        var low = known.MinBy(x => x.Percent);
        return new ProbePart("space", "Free space", $"Warn below {limit} % · {low.Name} has {low.Percent:0} % free", low.Percent < limit ? "warn" : "ok");
    }
}

/// <summary>A storage sensor's parts, by key.</summary>
public static class Storage
{
    public static readonly string[] Parts = ["pools", "disks", "space"];
}

/// <summary>TrueNAS over its REST API with an API key: pool health, the disks in them, and free space.</summary>
public static class TrueNas
{
    public static async Task<Probe> Check(Sensor s, Login login, bool trying, CancellationToken stop)
    {
        using var device = new DeviceHttp(s.Pin, trying, TimeSpan.FromSeconds(20));
        device.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Password);
        var root = DeviceHttp.Root(s.Target) + "/api/v2.0";
        var watch = Stopwatch.StartNew();
        try
        {
            var info = await JsonBits.Get(device.Http, root + "/system/info", stop);
            var pools = (await JsonBits.Get(device.Http, root + "/pool", stop)).EnumerateArray().ToList();
            var found = new List<ProbePart>();
            if (pools.Count > 0)
            {
                var sick = pools.Any(p => JsonBits.Text(p, "status") != "ONLINE" || (p.TryGetProperty("healthy", out var h) && h.ValueKind == JsonValueKind.False));
                found.Add(new ProbePart("pools", "Pool health", string.Join(" · ", pools.Select(p => $"{JsonBits.Text(p, "name")} {JsonBits.Text(p, "status")}")), sick ? "bad" : "ok"));
                var disks = pools.SelectMany(p => Disks(p.TryGetProperty("topology", out var t) ? t : default)).ToList();
                if (disks.Count > 0)
                {
                    var online = disks.Count(d => d == "ONLINE");
                    found.Add(new ProbePart("disks", "Disks", $"{online} of {disks.Count} online", online == disks.Count ? "ok" : "bad"));
                }
                if (JsonBits.Space(pools.Select(p => (JsonBits.Text(p, "name") ?? "pool", JsonBits.Number(p, "size"), JsonBits.Number(p, "free"))), s.WarnFree ?? 10) is { } space)
                {
                    found.Add(space);
                }
            }
            var version = JsonBits.Text(info, "version") ?? "TrueNAS";
            return JsonBits.Result(s, found, (int) watch.ElapsedMilliseconds, $"Signed in to {(version.StartsWith("TrueNAS", StringComparison.OrdinalIgnoreCase) ? version : $"TrueNAS {version}")}",
                $"{JsonBits.Text(info, "hostname")} · {pools.Count} pool{(pools.Count == 1 ? "" : "s")} · with the API key", device.Offer);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException && device.Said(e) is not null)
        {
            return new Probe(false, Note: e is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } ? "Sign-in failed: check the API key" : device.Said(e), Pin: device.Offer);
        }    }

    // The status of every disk in a pool's layout: the leaves of its data, cache, log and spare vdevs.
    private static IEnumerable<string> Disks(JsonElement topology)
    {
        if (topology.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }
        foreach (var group in topology.EnumerateObject().Where(g => g.Value.ValueKind == JsonValueKind.Array))
        {
            foreach (var status in Leaves(group.Value))
            {
                yield return status;
            }
        }
    }

    private static IEnumerable<string> Leaves(JsonElement vdevs)
    {
        foreach (var v in vdevs.EnumerateArray())
        {
            if (JsonBits.Text(v, "type") == "DISK")
            {
                yield return JsonBits.Text(v, "status") ?? "UNKNOWN";
            }
            else if (v.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                foreach (var status in Leaves(children))
                {
                    yield return status;
                }
            }
        }
    }
}

/// <summary>Synology DSM over its web API: volume health, disks and free space. DSM only gives storage details to an administrator.</summary>
public static class Synology
{
    public static async Task<Probe> Check(Sensor s, Login login, bool trying, CancellationToken stop)
    {
        using var device = new DeviceHttp(s.Pin, trying, TimeSpan.FromSeconds(20));
        // DSM listens on 5001 for HTTPS unless told otherwise.
        var root = DeviceHttp.Root(s.Target.Contains(':', StringComparison.Ordinal) ? s.Target : $"{s.Target}:5001");
        var watch = Stopwatch.StartNew();
        try
        {
            var auth = await JsonBits.Post(device.Http, root + "/webapi/auth.cgi", new()
            {
                ["api"] = "SYNO.API.Auth", ["version"] = "6", ["method"] = "login", ["account"] = login.Username ?? "", ["passwd"] = login.Password, ["session"] = "RabbidLab", ["format"] = "sid",
            }, stop);
            if (JsonBits.Text(auth, "data", "sid") is not { } sid)
            {
                return new Probe(false, Note: JsonBits.Number(auth, "error", "code") switch
                {
                    400 or 401 => "Sign-in failed: check the username and password",
                    402 => "That account isn't allowed to use DSM",
                    403 or 404 or 406 => "That account has 2-step verification on. Use one without it.",
                    407 => "DSM has blocked this server's IP address",
                    var code => $"DSM refused the sign-in (error {code})",
                }, Pin: device.Offer);
            }
            try
            {
                var info = await JsonBits.Get(device.Http, $"{root}/webapi/entry.cgi?api=SYNO.DSM.Info&version=2&method=getinfo&_sid={Uri.EscapeDataString(sid)}", stop);
                var storage = await JsonBits.Get(device.Http, $"{root}/webapi/entry.cgi?api=SYNO.Storage.CGI.Storage&version=1&method=load_info&_sid={Uri.EscapeDataString(sid)}", stop);
                if (storage.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
                {
                    return new Probe(false, Note: "DSM didn't give the storage details: the account has to be in the administrators group", Pin: device.Offer);
                }
                var volumes = JsonBits.Items(storage, "data", "volumes").ToList();
                var disks = JsonBits.Items(storage, "data", "disks").ToList();
                string Called(JsonElement v) => JsonBits.Text(v, "display_name") ?? JsonBits.Text(v, "id") ?? "volume";
                var found = new List<ProbePart>();
                if (volumes.Count > 0)
                {
                    var sick = volumes.Any(v => JsonBits.Text(v, "status") != "normal");
                    found.Add(new ProbePart("pools", "Volume health", string.Join(" · ", volumes.Select(v => $"{Called(v)} {JsonBits.Text(v, "status")}")), sick ? "bad" : "ok"));
                }
                if (disks.Count > 0)
                {
                    var good = disks.Count(d => JsonBits.Text(d, "status") is "normal" or "initialized" && JsonBits.Text(d, "smart_status") is null or "normal" or "safe");
                    found.Add(new ProbePart("disks", "Disks", $"{good} of {disks.Count} normal", good == disks.Count ? "ok" : "bad"));
                }
                if (JsonBits.Space(volumes.Select(v => (Called(v), JsonBits.Number(v, "size", "total"), JsonBits.Number(v, "size", "total") - JsonBits.Number(v, "size", "used"))), s.WarnFree ?? 10) is { } space)
                {
                    found.Add(space);
                }
                var model = JsonBits.Text(info, "data", "model") ?? "Synology";
                return JsonBits.Result(s, found, (int) watch.ElapsedMilliseconds, $"Signed in to {JsonBits.Text(info, "data", "version_string") ?? "DSM"} on a {model}",
                    $"{volumes.Count} volume{(volumes.Count == 1 ? "" : "s")} · {disks.Count} disks · as {login.Username}", device.Offer);
            }
            finally
            {
                // Signed out, so DSM doesn't keep a session for every check.
                _ = device.Http.GetAsync($"{root}/webapi/auth.cgi?api=SYNO.API.Auth&version=6&method=logout&session=RabbidLab&_sid={Uri.EscapeDataString(sid)}", CancellationToken.None)
                    .ContinueWith(t => t.Exception, TaskScheduler.Default);
            }
        }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException && device.Said(e) is not null)
        {
            return new Probe(false, Note: device.Said(e), Pin: device.Offer);
        }
    }
}
