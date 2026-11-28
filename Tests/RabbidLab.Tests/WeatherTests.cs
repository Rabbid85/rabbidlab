using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>The weather on the banners: the lab's ZIP code, what a station's reading becomes, and asking the weather service.</summary>
public sealed class WeatherTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-weather-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(data, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temp folder's own cleanup.
        }
    }

    private Weather Make(FakeWeatherService service) =>
        new(DataFolder.Resolve(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = data }).Build()), new WeatherHttp(service), NullLogger<Weather>.Instance);

    private static WeatherReading Read(string json) => Weather.Read(JsonDocument.Parse(json).RootElement, "Chicago, IL");

    private static string Observation(string says, string layers = "", string present = "", string wind = "null", string gust = "null", string temp = "6") =>
        """{"properties":{"timestamp":"2026-11-26T01:30:00+00:00","textDescription":"@says","presentWeather":[@present],"temperature":{"value":@temp},"windSpeed":{"value":@wind},"windGust":{"value":@gust},"cloudLayers":[@layers]}}"""
            .Replace("@says", says).Replace("@present", present).Replace("@temp", temp).Replace("@wind", wind).Replace("@gust", gust).Replace("@layers", layers);

    [Fact]
    public void A_zip_code_must_be_five_digits_that_the_census_has_an_area_for()
    {
        var weather = Make(new FakeWeatherService());

        Assert.Contains("five-digit", Assert.Throws<ToolProblem>(() => weather.SetZip("6060")).Message);
        Assert.Contains("five-digit", Assert.Throws<ToolProblem>(() => weather.SetZip("60601; drop")).Message);
        Assert.Contains("No area", Assert.Throws<ToolProblem>(() => weather.SetZip("00000")).Message);
        weather.SetZip(" 60601 ");
        Assert.Equal("60601", weather.Zip);
        weather.SetZip("");
        Assert.Equal("", weather.Zip);
    }

    [Fact]
    public void The_place_the_banners_work_the_sun_out_for_is_the_zip_codes_and_no_finer()
    {
        var weather = Make(new FakeWeatherService());
        Assert.Null(weather.Where);

        // Chicago's Loop, to a hundredth of a degree (about a kilometre): enough for sunrise and sunset to the minute.
        weather.SetZip("60601");
        var (lat, lon) = weather.Where!.Value;
        Assert.InRange(lat, 41.8, 41.95);
        Assert.InRange(lon, -87.7, -87.55);
        Assert.Equal(Math.Round(lat, 2), lat);
        Assert.Equal(Math.Round(lon, 2), lon);
    }

    [Fact]
    public void The_banner_goes_by_the_sun_and_not_by_fixed_hours()
    {
        // Dusk was 5:00 to 7:30 PM by the clock all year, so in winter the banner stayed light long after dark.
        var scene = File.ReadAllText(Path.Combine(ReleaseKeyTests.Root(), "Src", "RabbidLab.Server", "wwwroot", "Assets", "scene.js"));
        Assert.DoesNotContain("getHours()", scene);
        Assert.Contains("timeAt(Date.now(), Sky?.place?.() ?? guess())", scene);
    }

    [Fact]
    public void A_reading_says_what_is_falling_how_hard_and_how_much_cloud()
    {
        var cloudy = Read(Observation("Mostly Cloudy", """{"amount":"FEW"},{"amount":"BKN"}"""));
        Assert.Equal(("clear", "most", 6.0), (cloudy.Kind, cloudy.Cover, cloudy.TempC));
        Assert.Equal(new DateTime(2026, 11, 26, 1, 30, 0, DateTimeKind.Utc), cloudy.At);

        Assert.Equal(("rain", "light", "over"), Kind(Read(Observation("Light Rain", """{"amount":"OVC"}"""))));
        Assert.Equal(("rain", "mid", "over"), Kind(Read(Observation("Rain", """{"amount":"OVC"}"""))));
        Assert.Equal(("rain", "heavy", "over"), Kind(Read(Observation("Heavy Rain and Fog", """{"amount":"VV"}"""))));
        Assert.Equal(("storm", "heavy", "most"), Kind(Read(Observation("Thunderstorm Heavy Rain", """{"amount":"BKN"}"""))));
        Assert.Equal(("snow", "light", "over"), Kind(Read(Observation("Light Snow", """{"amount":"OVC"}"""))));
        Assert.Equal(("ice", "mid", "over"), Kind(Read(Observation("Freezing Rain", """{"amount":"OVC"}"""))));
        Assert.Equal(("fog", "mid", "none"), Kind(Read(Observation("Fog/Mist", """{"amount":"CLR"}"""))));
        // Its list of what is falling counts when its words don't say.
        Assert.Equal(("rain", "heavy", "half"), Kind(Read(Observation("Unknown", """{"amount":"SCT"}""", """{"weather":"rain","intensity":"heavy"}"""))));
        // Wind by speed or gust, in km/h, when nothing is falling.
        Assert.Equal("wind", Read(Observation("Clear", """{"amount":"CLR"}""", wind: "44")).Kind);
        Assert.Equal("wind", Read(Observation("Fair", gust: "60")).Kind);
        Assert.Equal("clear", Read(Observation("Fair", gust: "42.5")).Kind);
        // No cloud layers listed: its words say how much.
        Assert.Equal("half", Read(Observation("Partly Cloudy")).Cover);
        Assert.Equal("few", Read(Observation("A Few Clouds")).Cover);
        Assert.Equal("none", Read(Observation("Fair")).Cover);
    }

    private static (string, string, string) Kind(WeatherReading r) => (r.Kind, r.Amount, r.Cover);

    [Fact]
    public void What_the_service_says_is_cut_to_plain_words()
    {
        var r = Read(Observation("<img src=x onerror=alert(1)> Rain " + new string('a', 200)));

        Assert.DoesNotContain('<', r.Says);
        Assert.DoesNotContain('=', r.Says);
        Assert.True(r.Says.Length <= 60);
        Assert.Equal("rain", r.Kind);
    }

    [Fact]
    public async Task The_nearest_station_is_found_once_and_then_only_its_reading_is_asked_for()
    {
        var service = new FakeWeatherService();
        var weather = Make(service);
        weather.SetZip("60601");

        await weather.Check(CancellationToken.None);
        await weather.Check(CancellationToken.None);

        // The hours to come are asked for with the first reading, and not again within the hour.
        Assert.Equal(["/points/41.885,-87.622", "/gridpoints/LOT/76,73/stations", "/stations/KMDW/observations/latest", "/gridpoints/LOT/76,73/forecast/hourly", "/stations/KMDW/observations/latest"], service.Asked);
        Assert.All(service.Agents, a => Assert.StartsWith("RabbidLab/", a));
        Assert.Equal(("clear", "most", "Chicago, IL"), (weather.Reading!.Kind, weather.Reading.Cover, weather.Reading.Place));
        Assert.Null(weather.Problem);
    }

    [Fact]
    public void The_hours_to_come_are_read_from_the_hourly_forecast()
    {
        using var doc = JsonDocument.Parse("""{"properties":{"periods":[{"startTime":"2026-11-27T15:00:00-05:00","temperature":50,"temperatureUnit":"F","shortForecast":"Chance Rain Showers","isDaytime":true},{"startTime":"2026-11-27T16:00:00-05:00","temperature":48,"temperatureUnit":"F","shortForecast":"Mostly Cloudy","isDaytime":true},{"startTime":"2026-11-27T22:00:00-05:00","temperature":41,"temperatureUnit":"F","shortForecast":"Clear","isDaytime":false},{"startTime":"never","temperature":1}]}}""");
        var hours = Weather.Hours(doc.RootElement);

        // An hour with no time of its own is left out; Fahrenheit is kept as Celsius, as the reading is.
        Assert.Equal(3, hours.Count);
        Assert.Equal(("rain", 10.0, true), (hours[0].Kind, hours[0].TempC, hours[0].Day));
        Assert.Equal(new DateTime(2026, 11, 27, 20, 0, 0, DateTimeKind.Utc), hours[0].At);
        Assert.Equal("cloud", hours[1].Kind);
        Assert.Equal(("clear", 5.0, false), (hours[2].Kind, hours[2].TempC, hours[2].Day));

        using var none = JsonDocument.Parse("""{"properties":{}}""");
        Assert.Empty(Weather.Hours(none.RootElement));
    }

    [Fact]
    public async Task Without_a_zip_code_nothing_is_asked()
    {
        var service = new FakeWeatherService();
        var weather = Make(service);

        await weather.Check(CancellationToken.None);

        Assert.Empty(service.Asked);
        Assert.Null(weather.Reading);
    }

    [Fact]
    public async Task A_service_that_does_not_answer_leaves_no_reading_and_says_why()
    {
        var service = new FakeWeatherService { Down = true };
        var weather = Make(service);
        weather.SetZip("60601");

        await weather.Check(CancellationToken.None);

        Assert.Null(weather.Reading);
        Assert.Contains("did not answer", weather.Problem);
    }

    [Fact]
    public async Task A_grid_square_that_is_not_one_is_never_asked_about()
    {
        var service = new FakeWeatherService { Office = "../../evil" };
        var weather = Make(service);
        weather.SetZip("60601");

        await weather.Check(CancellationToken.None);

        Assert.Equal(["/points/41.885,-87.622"], service.Asked);
        Assert.Null(weather.Reading);
        Assert.NotNull(weather.Problem);
    }
}

/// <summary>Stands in for api.weather.gov: Chicago's forecast office, two stations and a mostly cloudy reading.</summary>
public sealed class FakeWeatherService : HttpMessageHandler
{
    public List<string> Asked { get; } = [];
    public List<string> Agents { get; } = [];
    public bool Down { get; set; }
    public string Office { get; set; } = "LOT";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Asked.Add(path);
        Agents.Add(request.Headers.UserAgent.ToString());
        if (Down)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
        var json = path.StartsWith("/points/", StringComparison.Ordinal)
            ? """{"properties":{"gridId":"@office","gridX":76,"gridY":73,"relativeLocation":{"properties":{"city":"Chicago","state":"IL"}}}}""".Replace("@office", Office)
            : path.EndsWith("/stations", StringComparison.Ordinal)
                ? """{"features":[{"properties":{"stationIdentifier":"KMDW"}},{"properties":{"stationIdentifier":"KORD"}}]}"""
                : """{"properties":{"timestamp":"@now","textDescription":"Mostly Cloudy","presentWeather":[],"temperature":{"value":6},"windSpeed":{"value":null},"windGust":{"value":42.5},"cloudLayers":[{"amount":"BKN"}]}}""".Replace("@now", DateTime.UtcNow.ToString("O"));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/geo+json") });
    }
}
