using System.Text;

namespace RabbidLab.Server;

/// <summary>
/// Developer tools, in Rabbid Lab Dev only: a switch in Settings, and while it's on, DEV buttons in Add a link and Add a
/// sensor that fill in test addresses (*.rabbidlab.test, a name that never resolves). Reading one plays a site that
/// answers, or one that doesn't; what's added with one goes away after 30 seconds, so the whole of adding can be watched.
/// </summary>
public static class DevTools
{
    public static readonly TimeSpan Lasts = TimeSpan.FromSeconds(30);
    private const string Domain = ".rabbidlab.test";

    public static bool On(SettingsStore settings) => Product.Dev && settings.Current.DevTools;

    /// <summary>Whether an address is a test one: up-07.rabbidlab.test answers, down-03.rabbidlab.test doesn't.</summary>
    public static bool IsTest(string? target) => Host(target)?.EndsWith(Domain, StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsDown(string? target) => Host(target)?.StartsWith("down", StringComparison.OrdinalIgnoreCase) == true;

    private static string? Host(string? target) =>
        string.IsNullOrWhiteSpace(target) ? null
        : Uri.TryCreate(target.Contains("://", StringComparison.Ordinal) ? target.Trim() : $"http://{target.Trim()}", UriKind.Absolute, out var u) ? u.Host : null;

    private static int Number(string? target) => int.TryParse(new string((Host(target) ?? "").TakeWhile(c => c != '.').Where(char.IsDigit).ToArray()), out var n) ? n : 1;

    /// <summary>What reading a test address finds, after a moment so the flask shows: a name, detail, IP and icon, or no answer.</summary>
    public static async Task<LinkLook> Look(string url, CancellationToken stop)
    {
        await Task.Delay(Random.Shared.Next(700, 1600), stop);
        var n = Number(url);
        var host = Host(url)!;
        return IsDown(url)
            ? new LinkLook(host.Split('.')[0], null, $"192.0.2.{n % 250 + 2}", null, $"{host} didn't answer, so fill it in yourself.")
            : new LinkLook($"Test {n:00}", "Test site from Developer tools", $"192.0.2.{n % 250 + 2}", Icon(n), null, "the test site");
    }

    /// <summary>What a check of a test target finds; a short wait in Check now, so the flask shows.</summary>
    public static async Task<Probe> Check(Sensor s, CancellationToken stop)
    {
        if (s.Trying)
        {
            await Task.Delay(700, stop);
        }
        return IsDown(s.Target) ? new Probe(false, Note: "No answer (a test)") : new Probe(true, 3 + Random.Shared.Next(40), "Answering (a test)");
    }

    // A test site's icon: a square in one of the console's colours with a T.
    private static string Icon(int n)
    {
        string[] colours = ["#FF6A45", "#FF8A3D", "#F2BE6B", "#5FD4A0", "#3CC7C7", "#6FA8FF", "#7C83FF", "#B07CE8", "#F06BB4"];
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 32 32\"><rect width=\"32\" height=\"32\" rx=\"7\" fill=\"{colours[n % colours.Length]}\"/>"
            + "<text x=\"16\" y=\"22\" font-family=\"Segoe UI, sans-serif\" font-size=\"17\" font-weight=\"700\" fill=\"#fff\" text-anchor=\"middle\">T</text></svg>";
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }

    public static void MapDevToolsApi(this WebApplication app)
    {
        if (!Product.Dev)
        {
            return; // nothing of this in a release
        }
        app.MapGet("/api/devtools", (HttpContext context, SettingsStore settings) =>
            Access.Refuse(context) ?? Results.Ok(new { on = settings.Current.DevTools }));

        app.MapPost("/api/devtools", (DevToolsForm form, HttpContext context, SettingsStore settings, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.UpdatesAuto) is { } refused)
            {
                return refused;
            }
            settings.Change(s => s.DevTools = form.On);
            log.Record(context, "admin", "devtools", form.On ? "Turned Developer tools on" : "Turned Developer tools off");
            return Results.Ok(new { on = settings.Current.DevTools });
        });
    }
}

public sealed record DevToolsForm(bool On);
