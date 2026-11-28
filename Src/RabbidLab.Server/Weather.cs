using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>Where the lab is, for the weather on the banners: a US ZIP code. Empty: no weather.</summary>
public sealed class WeatherFile
{
    public string Zip { get; set; } = "";
}

public sealed record WeatherPlaceForm(string? Zip);

/// <summary>
/// What the sky is doing where the lab is, as the banners draw it. Kind: clear, rain, storm, snow, ice, wind or fog. Amount:
/// how hard it rains or snows (light, mid, heavy). Cover: how much cloud (none, few, half, most, over). Says: the weather
/// service's own words. Place: the town it is for. At: when the station read it.
/// </summary>
public sealed record WeatherReading(string Kind, string Amount, string Cover, double? TempC, double? WindKph, string Says, string Place, DateTime At);

/// <summary>An hour to come, from the forecast: when it starts, how warm, what the sky does (clear, cloud, rain, storm, snow, ice, wind or fog) and whether it is day.</summary>
public sealed record WeatherHour(DateTime At, double? TempC, string Kind, bool Day);

/// <summary>The one HTTP client the weather is asked with, so a test can stand in for the weather service.</summary>
public sealed class WeatherHttp(HttpMessageHandler? handler = null)
{
    // Asked only at the service's own address, never where an answer points; a short wait and a small answer at most.
    public HttpClient Client { get; } = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(30) })
    {
        BaseAddress = new Uri("https://api.weather.gov/"),
        Timeout = TimeSpan.FromSeconds(12),
        MaxResponseContentBufferSize = 4 * 1024 * 1024,
    };
}

/// <summary>
/// The weather for the banners (the banner's sky window, Current weather), from the US National Weather Service: free, no
/// account or key, and it reports what a station near the lab last read. An admin sets the lab's ZIP code; the ZIP's
/// middle (zips.tsv.gz, from the US Census Bureau's ZIP code areas) is where the service is asked about. Asked every 15
/// minutes while a ZIP is set; nothing about the lab is sent but those coordinates. The hours to come are the same service's
/// hourly forecast for the same grid square, asked for once an hour.
/// </summary>
public sealed partial class Weather(DataFolder folder, WeatherHttp http, ILogger<Weather> log)
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Stale = TimeSpan.FromHours(3); // a reading older than this is not shown
    private static readonly TimeSpan AheadEvery = TimeSpan.FromHours(1);
    private const int HoursAhead = 5;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Lazy<Dictionary<string, (double Lat, double Lon)>> Zips = new(LoadZips);
    private readonly JsonFile<WeatherFile> file = folder.Json<WeatherFile>("weather.json", Json);
    private readonly Lock gate = new();
    private WeatherReading? reading;
    private (string Zip, string Station, string Place, string Grid)? spot; // the station nearest the ZIP and its grid square, found once
    private IReadOnlyList<WeatherHour> ahead = [];
    private DateTime? aheadAt;
    private string? problem;
    private DateTime? checkedAt;

    [GeneratedRegex("^[0-9]{5}$")]
    private static partial Regex FiveDigits();

    [GeneratedRegex("^[A-Z]{3,4}$")]
    private static partial Regex Office();

    [GeneratedRegex("^[A-Z0-9]{3,8}$")]
    private static partial Regex StationId();

    public string Zip => file.Read().Zip;

    /// <summary>The middle of the lab's ZIP code area, to a hundredth of a degree: where the banners work the sun out for. Null with no ZIP code.</summary>
    public (double Lat, double Lon)? Where => Zips.Value.TryGetValue(Zip, out var at) ? (Math.Round(at.Lat, 2), Math.Round(at.Lon, 2)) : null;

    /// <summary>The last reading, unless it is too old to show. Null: none yet, or no ZIP.</summary>
    public WeatherReading? Reading
    {
        get
        {
            lock (gate)
            {
                return reading is not null && DateTime.UtcNow - reading.At < Stale ? reading : null;
            }
        }
    }

    /// <summary>The next few hours, from the forecast as last read: only while there is a reading to show beside them.</summary>
    public IReadOnlyList<WeatherHour> Ahead
    {
        get
        {
            lock (gate)
            {
                var now = DateTime.UtcNow;
                return reading is null || now - reading.At >= Stale ? [] : [.. ahead.Where(h => h.At > now).Take(HoursAhead)];
            }
        }
    }

    /// <summary>Why the last check gave nothing, in plain words; null when it worked.</summary>
    public string? Problem { get { lock (gate) { return problem; } } }

    public DateTime? CheckedAt { get { lock (gate) { return checkedAt; } } }

    /// <summary>The lab's ZIP code: five digits the Census Bureau has an area for, or empty for no weather.</summary>
    public void SetZip(string? zip)
    {
        var z = (zip ?? "").Trim();
        if (z.Length > 0 && !FiveDigits().IsMatch(z))
        {
            throw new ToolProblem("Enter a five-digit US ZIP code, or leave it blank for no weather.");
        }
        if (z.Length > 0 && !Zips.Value.ContainsKey(z))
        {
            throw new ToolProblem($"No area was found for ZIP code {z}. Try one nearby.");
        }
        lock (file.Gate)
        {
            file.Save(new WeatherFile { Zip = z });
        }
        lock (gate)
        {
            (reading, spot, problem, checkedAt, ahead, aheadAt) = (null, null, null, null, [], null);
        }
    }

    /// <summary>Asks the weather service once: finds the station nearest the ZIP the first time, then its latest reading.</summary>
    public async Task Check(CancellationToken stop)
    {
        var zip = Zip;
        if (zip.Length == 0 || !Zips.Value.TryGetValue(zip, out var at))
        {
            return;
        }
        try
        {
            (string Zip, string Station, string Place, string Grid)? known;
            lock (gate)
            {
                known = spot is { } s && s.Zip == zip ? s : null;
            }
            known ??= await Find(zip, at.Lat, at.Lon, stop);
            using var doc = await Get($"stations/{known.Value.Station}/observations/latest", stop);
            var read = Read(doc.RootElement, known.Value.Place);
            lock (gate)
            {
                // A ZIP changed while this was asking has its own check coming.
                if (Zip == zip)
                {
                    (spot, reading, problem, checkedAt) = (known, read, null, DateTime.UtcNow);
                }
            }
            await Forecast(zip, known.Value.Grid, stop);
        }
        // Whatever goes wrong with an answer from outside, the banner only goes without weather; the server carries on.
        catch (Exception e) when (e is not OperationCanceledException || !stop.IsCancellationRequested)
        {
            if (stop.IsCancellationRequested)
            {
                return;
            }
            log.LogWarning("The weather for {Zip} could not be read: {Why}", zip, e.Message);
            lock (gate)
            {
                (problem, checkedAt) = ("The National Weather Service did not answer. The banner shows no weather until it does.", DateTime.UtcNow);
            }
        }
    }

    // The hours to come for the grid square, once an hour. Without them the banner has its reading all the same.
    private async Task Forecast(string zip, string grid, CancellationToken stop)
    {
        lock (gate)
        {
            if (aheadAt is { } last && DateTime.UtcNow - last < AheadEvery)
            {
                return;
            }
        }
        try
        {
            using var doc = await Get($"gridpoints/{grid}/forecast/hourly", stop);
            var hours = Hours(doc.RootElement);
            lock (gate)
            {
                if (Zip == zip)
                {
                    (ahead, aheadAt) = (hours, DateTime.UtcNow);
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException || !stop.IsCancellationRequested)
        {
            if (!stop.IsCancellationRequested)
            {
                log.LogWarning("The hourly forecast for {Zip} could not be read: {Why}", zip, e.Message);
            }
        }
    }

    // The forecast office and grid square for the coordinates, then the stations for that square, nearest first.
    private async Task<(string Zip, string Station, string Place, string Grid)> Find(string zip, double lat, double lon, CancellationToken stop)
    {
        string office;
        int x;
        int y;
        string place;
        using (var point = await Get(string.Create(CultureInfo.InvariantCulture, $"points/{lat:0.####},{lon:0.####}"), stop))
        {
            var p = point.RootElement.GetProperty("properties");
            office = p.GetProperty("gridId").GetString() ?? "";
            (x, y) = (p.GetProperty("gridX").GetInt32(), p.GetProperty("gridY").GetInt32());
            var where = p.TryGetProperty("relativeLocation", out var rel) && rel.TryGetProperty("properties", out var rp) ? rp : default;
            var city = where.ValueKind == JsonValueKind.Object && where.TryGetProperty("city", out var c) ? c.GetString() : null;
            var state = where.ValueKind == JsonValueKind.Object && where.TryGetProperty("state", out var st) ? st.GetString() : null;
            place = Plain(string.IsNullOrWhiteSpace(city) ? zip : string.IsNullOrWhiteSpace(state) ? city! : $"{city}, {state}", 60);
        }
        // Only what the service's own addresses are made of goes into the next one.
        if (!Office().IsMatch(office) || x is < 0 or > 9999 || y is < 0 or > 9999)
        {
            throw new FormatException("The weather service gave a grid square that is not one.");
        }
        using var list = await Get($"gridpoints/{office}/{x},{y}/stations", stop);
        foreach (var f in list.RootElement.GetProperty("features").EnumerateArray())
        {
            var id = f.GetProperty("properties").GetProperty("stationIdentifier").GetString() ?? "";
            if (StationId().IsMatch(id))
            {
                return (zip, id, place, $"{office}/{x},{y}");
            }
        }
        throw new KeyNotFoundException("No weather station is near that ZIP code.");
    }

    private async Task<JsonDocument> Get(string path, CancellationToken stop)
    {
        using var ask = new HttpRequestMessage(HttpMethod.Get, path);
        // The service refuses a request that does not say what is asking.
        ask.Headers.TryAddWithoutValidation("User-Agent", $"RabbidLab/{Product.Version} (+https://github.com/Rabbid85/rabbidlab)");
        ask.Headers.TryAddWithoutValidation("Accept", "application/geo+json");
        using var answer = await http.Client.SendAsync(ask, stop);
        answer.EnsureSuccessStatusCode();
        await using var body = await answer.Content.ReadAsStreamAsync(stop);
        return await JsonDocument.ParseAsync(body, cancellationToken: stop);
    }

    /// <summary>A station's reading as the banners draw it. Everything in it is the service's, so it is checked and cut to size.</summary>
    public static WeatherReading Read(JsonElement observation, string place)
    {
        var p = observation.GetProperty("properties");
        var says = Plain(p.TryGetProperty("textDescription", out var t) ? t.GetString() : null, 60);
        var words = says.ToLowerInvariant();
        // What is falling, from its own list when it has one; its words otherwise.
        var falling = new List<(string What, string How)>();
        if (p.TryGetProperty("presentWeather", out var pw) && pw.ValueKind == JsonValueKind.Array)
        {
            foreach (var w in pw.EnumerateArray())
            {
                falling.Add(((w.TryGetProperty("weather", out var k) ? k.GetString() : null) ?? "", (w.TryGetProperty("intensity", out var i) ? i.GetString() : null) ?? ""));
            }
        }
        bool Has(params string[] any) => any.Any(a => words.Contains(a, StringComparison.Ordinal) || falling.Any(f => f.What.Contains(a.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)));
        var wind = Number(p, "windSpeed");
        var gust = Number(p, "windGust");
        var kind = Has("thunder") ? "storm"
            : Has("freezing", "ice pellets", "sleet") ? "ice"
            : Has("snow", "flurr", "blizzard") ? "snow"
            : Has("rain", "drizzle", "shower") ? "rain"
            : Has("fog", "mist", "haze") ? "fog"
            : wind >= 40 || gust >= 55 || Has("windy", "breezy") ? "wind"
            : "clear";
        var amount = words.Contains("heavy", StringComparison.Ordinal) || falling.Any(f => f.How == "heavy") ? "heavy"
            : words.Contains("light", StringComparison.Ordinal) || words.Contains("drizzle", StringComparison.Ordinal) || falling.Any(f => f.How == "light") ? "light"
            : "mid";
        var at = p.TryGetProperty("timestamp", out var ts) && ts.TryGetDateTimeOffset(out var when) ? when.UtcDateTime : DateTime.UtcNow;
        return new WeatherReading(kind, amount, Cover(p, words), Number(p, "temperature"), wind, says, place, at);
    }

    /// <summary>The hours of an hourly forecast, soonest first, as the banner draws them. An answer without any gives none.</summary>
    public static IReadOnlyList<WeatherHour> Hours(JsonElement forecast)
    {
        var hours = new List<WeatherHour>();
        if (!forecast.TryGetProperty("properties", out var p) || !p.TryGetProperty("periods", out var periods) || periods.ValueKind != JsonValueKind.Array)
        {
            return hours;
        }
        foreach (var period in periods.EnumerateArray().Take(48))
        {
            if (!period.TryGetProperty("startTime", out var start) || !start.TryGetDateTimeOffset(out var at))
            {
                continue;
            }
            double? temp = period.TryGetProperty("temperature", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
            var fahrenheit = !period.TryGetProperty("temperatureUnit", out var unit) || unit.GetString() != "C";
            var words = Plain(period.TryGetProperty("shortForecast", out var s) ? s.GetString() : null, 60).ToLowerInvariant();
            bool Has(params string[] any) => any.Any(a => words.Contains(a, StringComparison.Ordinal));
            var kind = Has("thunder") ? "storm" : Has("freezing", "ice pellets", "sleet") ? "ice" : Has("snow", "flurr", "blizzard") ? "snow" : Has("rain", "drizzle", "shower") ? "rain"
                : Has("fog", "mist", "haze") ? "fog" : Has("windy", "breezy") ? "wind" : Has("cloud", "overcast") ? "cloud" : "clear";
            var day = !period.TryGetProperty("isDaytime", out var light) || light.ValueKind != JsonValueKind.False;
            hours.Add(new WeatherHour(at.UtcDateTime, temp is { } v ? Math.Round(fahrenheit ? (v - 32) * 5 / 9 : v, 1) : null, kind, day));
        }
        return hours;
    }

    // How much cloud: the fullest layer the station saw, or its words when it lists none.
    private static string Cover(JsonElement p, string words)
    {
        var most = -1;
        if (p.TryGetProperty("cloudLayers", out var layers) && layers.ValueKind == JsonValueKind.Array)
        {
            foreach (var layer in layers.EnumerateArray())
            {
                var amount = layer.TryGetProperty("amount", out var a) ? a.GetString() : null;
                most = Math.Max(most, amount switch { "CLR" or "SKC" => 0, "FEW" => 1, "SCT" => 2, "BKN" => 3, "OVC" or "VV" => 4, _ => -1 });
            }
        }
        if (most < 0)
        {
            most = words.Contains("overcast", StringComparison.Ordinal) ? 4 : words.Contains("mostly cloudy", StringComparison.Ordinal) ? 3
                : words.Contains("partly", StringComparison.Ordinal) ? 2 : words.Contains("few", StringComparison.Ordinal) ? 1
                : words.Contains("cloud", StringComparison.Ordinal) ? 4 : 0;
        }
        return new[] { "none", "few", "half", "most", "over" }[most];
    }

    private static double? Number(JsonElement p, string name) =>
        p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("value", out var n) && n.ValueKind == JsonValueKind.Number
            && n.TryGetDouble(out var d) && double.IsFinite(d) ? Math.Round(d, 1) : null;

    // Letters, digits, spaces and plain marks only, cut to a length: what the service says is shown on a page.
    private static string Plain(string? text, int most)
    {
        var clean = new string((text ?? "").Where(c => char.IsLetterOrDigit(c) || " ,.'-/".Contains(c)).ToArray()).Trim();
        return clean.Length > most ? clean[..most] : clean;
    }

    private static Dictionary<string, (double, double)> LoadZips()
    {
        var map = new Dictionary<string, (double, double)>(34000);
        using var s = typeof(Weather).Assembly.GetManifestResourceStream("zips.tsv.gz");
        if (s is null)
        {
            return map;
        }
        using var gz = new GZipStream(s, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.ASCII);
        while (reader.ReadLine() is { } line)
        {
            var p = line.Split('\t');
            if (p.Length == 3 && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                map[p[0]] = (lat, lon);
            }
        }
        return map;
    }
}

/// <summary>Asks for the weather when the server starts and every 15 minutes after, while a ZIP code is set.</summary>
public sealed class WeatherWorker(Weather weather) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(Weather.Every);
        do
        {
            await weather.Check(stop);
        }
        while (await timer.WaitForNextTickAsync(stop));
    }
}

public static class WeatherApi
{
    private static object View(Weather weather, bool canSet) => new
    {
        zip = weather.Zip,
        lat = weather.Where?.Lat,
        lon = weather.Where?.Lon,
        reading = weather.Reading,
        ahead = weather.Ahead,
        problem = canSet ? weather.Problem : null,
        checkedAt = weather.CheckedAt,
        every = (int)Weather.Every.TotalMinutes,
        canSet,
    };

    public static void MapWeather(this WebApplication app)
    {
        // The reading, for anyone signed in; whoever may change the lab's look also sees why a check failed.
        app.MapGet("/api/weather", (HttpContext context, Weather weather) => Results.Ok(View(weather, Access.Can(context, Permissions.AppearanceBrand))));

        // The lab's ZIP code, from whoever may change the lab's look. It is checked at once, so the page has a reading to show.
        app.MapPost("/api/settings/weather", async (WeatherPlaceForm form, HttpContext context, Weather weather, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AppearanceBrand) is { } refused)
            {
                return refused;
            }
            try
            {
                weather.SetZip(form.Zip);
                log.Record(context, "admin", "brand", weather.Zip.Length > 0 ? $"Set the ZIP code for the banner weather to {weather.Zip}" : "Turned the banner weather off for the lab");
            }
            catch (ToolProblem e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
            await weather.Check(context.RequestAborted);
            return Results.Ok(View(weather, true));
        });
    }
}
