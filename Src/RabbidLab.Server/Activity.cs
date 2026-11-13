using System.Text;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>One thing that happened: when, who did it and from where, what area and action, how it went, and any detail.</summary>
public sealed record ActivityEntry(DateTime At, string By, string? From, string Area, string Action, string Title, string Result, string? Detail);

/// <summary>
/// The activity log: every change made through Rabbid Lab and everything it did by itself (an update it installed, say),
/// one JSON line each in activity.jsonl in the data folder, and in the service log. A full file becomes activity.1.jsonl.
/// </summary>
public sealed class ActivityLog(DataFolder folder, ILogger<ActivityLog> log)
{
    /// <summary>Each entry as it's written: the alert watch counts failed sign-ins from it.</summary>
    public event Action<ActivityEntry>? Recorded;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string file = Path.Combine(folder.Path, "activity.jsonl");
    private readonly string older = Path.Combine(folder.Path, "activity.1.jsonl");
    private readonly Lock gate = new();

    /// <summary>How big activity.jsonl gets before it's set aside as activity.1.jsonl, replacing the one before.</summary>
    public long RotateAt { get; set; } = 5 * 1024 * 1024;

    /// <summary>Where a request came from, for the log.</summary>
    public static string? Address(HttpContext context) => context.Connection.RemoteIpAddress?.ToString();

    /// <summary>Who is asking: the person signed in, or else the address they're at.</summary>
    public static string Who(HttpContext context) => context.User.Identity?.Name is { Length: > 0 } name ? name : "Someone at " + (Address(context) ?? "an unknown address");

    public void Record(HttpContext context, string area, string action, string title, string result = "ok", string? detail = null) =>
        Record(Who(context), Address(context), area, action, title, result, detail);

    public void Record(string by, string? from, string area, string action, string title, string result, string? detail = null)
    {
        var entry = new ActivityEntry(DateTime.UtcNow, by, from, area, action, title, result, detail);
        log.LogInformation("{Title} ({Action}) by {By}: {Result}{Detail}", title, action, by, result, detail is null ? "" : " - " + detail);
        try
        {
            lock (gate)
            {
                if (new FileInfo(file) is { Exists: true } now && now.Length >= RotateAt)
                {
                    File.Move(file, older, overwrite: true);
                }
                File.AppendAllText(file, JsonSerializer.Serialize(entry, Json) + "\n");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(e, "The activity log could not be written");
        }
        Recorded?.Invoke(entry);
    }

    /// <summary>The newest entries, newest first.</summary>
    public IReadOnlyList<ActivityEntry> Recent(int count = 100)
    {
        try
        {
            List<string> lines;
            lock (gate)
            {
                lines = Tail(file, count);
                if (lines.Count < count)
                {
                    lines.AddRange(Tail(older, count - lines.Count));
                }
            }
            return lines.Select(line => { try { return JsonSerializer.Deserialize<ActivityEntry>(line, Json); } catch (JsonException) { return null; } })
                .OfType<ActivityEntry>().ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(e, "The activity log could not be read");
            return [];
        }
    }

    /// <summary>The kinds of activity Settings, Activity log filters by, and the areas each covers.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Kinds = new Dictionary<string, string[]>
    {
        ["signins"] = ["sign-in"],
        ["accounts"] = ["access", "users"],
        ["lab"] = ["links", "sensors"],
        ["seats"] = ["clients"],
        ["admin"] = ["admin"],
    };

    /// <summary>The kind an entry's area belongs to, for its icon and its filter.</summary>
    public static string KindOf(string area) => Kinds.FirstOrDefault(k => k.Value.Contains(area)).Key ?? "admin";

    /// <summary>
    /// The entries that match, newest first: of one kind, by one person, with some words in them, since a time, only the
    /// ones that didn't go well. Count says how many to hand back; Total is how many matched, Problems how many of those failed.
    /// </summary>
    public ActivityPage Find(ActivityFilter filter)
    {
        List<ActivityEntry> all;
        try
        {
            lock (gate)
            {
                all = [.. Read(file), .. Read(older)];
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(e, "The activity log could not be read");
            all = [];
        }
        var areas = filter.Kind is { } k && Kinds.TryGetValue(k, out var a) ? a : null;
        var words = filter.Find?.Trim() is { Length: > 0 } q ? q : null;
        var inRange = all.Where(e => filter.Since is null || e.At >= filter.Since).ToList();
        var matched = inRange.Where(e => (areas is null || areas.Contains(e.Area)) && (filter.By is null || e.By == filter.By)
                && (!filter.Problems || e.Result != "ok")
                && (words is null || $"{e.Title} {e.By} {e.From} {e.Detail}".Contains(words, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return new ActivityPage([.. matched.Take(filter.Count)], matched.Count, matched.Count(e => e.Result != "ok"),
            [.. inRange.Select(e => e.By).Distinct().Order(StringComparer.OrdinalIgnoreCase)]);
    }

    // Every entry in a file, newest first.
    private static IEnumerable<ActivityEntry> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var list = new List<ActivityEntry>();
        while (reader.ReadLine() is { } line)
        {
            try
            {
                if (line.Length > 0 && JsonSerializer.Deserialize<ActivityEntry>(line, Json) is { } entry)
                {
                    list.Add(entry);
                }
            }
            catch (JsonException)
            {
                // A line cut off part-way through is skipped.
            }
        }
        list.Reverse();
        return list;
    }

    /// <summary>The last lines of a file, newest first, reading only as much of its end as they take.</summary>
    private static List<string> Tail(string path, int count)
    {
        if (!File.Exists(path))
        {
            return [];
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        for (long size = 256 * 1024; ; size *= 4)
        {
            var start = Math.Max(0, stream.Length - size);
            var bytes = new byte[stream.Length - start];
            stream.Position = start;
            stream.ReadExactly(bytes);
            var lines = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (start > 0 && lines.Count > 0)
            {
                lines.RemoveAt(0); // cut off part-way through
            }
            if (lines.Count >= count || start == 0)
            {
                lines.Reverse();
                return lines.Count > count ? lines[..count] : lines;
            }
        }
    }
}

/// <summary>What Settings, Activity log asks for. Since: how far back (none: all of it).</summary>
public sealed record ActivityFilter(string? Kind, string? By, string? Find, DateTime? Since, bool Problems, int Count);

/// <summary>One page of the activity log, with how many matched in all and who appears in the time asked about.</summary>
public sealed record ActivityPage(IReadOnlyList<ActivityEntry> Entries, int Total, int Problems, IReadOnlyList<string> People);

public static class ActivityApi
{
    public static void MapActivityApi(this WebApplication app)
    {
        app.MapGet("/api/activity", (int? count, HttpContext context, ActivityLog activity) =>
            Access.Can(context, Permissions.ActivityView) ? Results.Ok(activity.Recent(Math.Clamp(count ?? 100, 1, 1000))) : Access.NotAllowed());

        app.MapGet("/api/activity/find", (string? kind, string? by, string? q, DateTime? since, bool? problems, int? count, HttpContext context, ActivityLog activity) =>
            Access.Can(context, Permissions.ActivityView)
                ? Results.Ok(activity.Find(Filter(kind, by, q, since, problems, Math.Clamp(count ?? 50, 1, 5000))))
                : Access.NotAllowed());

        // The same, as a CSV file to open in Excel.
        app.MapGet("/api/activity/export", (string? kind, string? by, string? q, DateTime? since, bool? problems, HttpContext context, ActivityLog activity) =>
        {
            if (!Access.Can(context, Permissions.ActivityView))
            {
                return Access.NotAllowed();
            }
            var page = activity.Find(Filter(kind, by, q, since, problems, int.MaxValue));
            var csv = new StringBuilder("When (UTC),Who,From,Area,What,Result,Detail\r\n");
            foreach (var e in page.Entries)
            {
                csv.Append(string.Join(',', new[] { e.At.ToString("yyyy-MM-dd HH:mm:ss"), e.By, e.From ?? "", e.Area, e.Title, e.Result, e.Detail ?? "" }.Select(Cell))).Append("\r\n");
            }
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", "rabbidlab-activity.csv");
        });
    }

    private static ActivityFilter Filter(string? kind, string? by, string? q, DateTime? since, bool? problems, int count) =>
        new(kind is { Length: > 0 } ? kind : null, by is { Length: > 0 } ? by : null, q, since?.ToUniversalTime(), problems == true, count);

    // A CSV cell: quoted when it has a comma, quote or line in it, and never starting a formula in Excel.
    private static string Cell(string value)
    {
        var v = value.Length > 0 && "=+-@".Contains(value[0]) ? "'" + value : value;
        return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }
}
