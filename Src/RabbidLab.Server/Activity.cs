using System.Text;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>One thing that happened: when, who did it and from where, what area and action, how it went, any detail, and the AD change it made (Changes.cs).</summary>
public sealed record ActivityEntry(DateTime At, string By, string? From, string Area, string Action, string Title, string Result, string? Detail, string? Change = null);

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
    public static string? Address(HttpContext context) => context.Connection.RemoteIpAddress is { } ip ? Addresses.Normal(ip).ToString() : null;

    /// <summary>Who is asking: the person signed in, or else the address they're at.</summary>
    public static string Who(HttpContext context) => context.User.Identity?.Name is { Length: > 0 } name ? name : "Unknown user from " + (Address(context) ?? "an unknown address");

    public void Record(HttpContext context, string area, string action, string title, string result = "ok", string? detail = null) =>
        Record(Who(context), Address(context), area, action, title, result, detail);

    public void Record(string by, string? from, string area, string action, string title, string result, string? detail = null)
    {
        // An Active Directory change this request made goes in the change journal under this title.
        var change = ChangeNote.Current?.Keep(by, from, title)?.Id;
        var entry = new ActivityEntry(DateTime.UtcNow, by, from, area, action, title, result, detail, change);
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

    /// <summary>What each kind is called on the page and in reports.</summary>
    public static readonly IReadOnlyDictionary<string, string> KindNames = new Dictionary<string, string>
    {
        ["signins"] = "Sign-ins", ["accounts"] = "People and accounts", ["lab"] = "Links and sensors", ["seats"] = "Devices", ["admin"] = "Updates and settings",
    };

    /// <summary>The kind an entry's area belongs to, for its icon and its filter.</summary>
    public static string KindOf(string area) => Kinds.FirstOrDefault(k => k.Value.Contains(area)).Key ?? "admin";

    /// <summary>
    /// The entries that match, newest first: of some kinds, by one person, with some words in them, between two times, only the
    /// ones that didn't go well. Count says how many to hand back; Total is how many matched, Problems how many of those failed.
    /// </summary>
    public ActivityPage Find(ActivityFilter filter)
    {
        var inRange = All().Where(e => InRange(filter, e)).ToList();
        var matched = inRange.Where(Matches(filter)).ToList();
        return new ActivityPage([.. matched.Take(filter.Count)], matched.Count, matched.Count(e => e.Result != "ok"),
            [.. inRange.Select(e => e.By).Distinct().Order(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>The counts for what matches, by user and by area, in one pass over the log.</summary>
    public ActivitySummary Summarize(ActivityFilter filter)
    {
        var matches = Matches(filter);
        return Summary(All().Where(e => InRange(filter, e) && matches(e)));
    }

    /// <summary>Every entry that matches, newest first, with its summary: for a report.</summary>
    public (ActivitySummary Summary, IReadOnlyList<ActivityEntry> Entries) Report(ActivityFilter filter)
    {
        var matches = Matches(filter);
        var list = All().Where(e => InRange(filter, e) && matches(e)).ToList();
        return (Summary(list), list);
    }

    /// <summary>Entries counted: sign-ins (the sign-in area), changes (the rest) and problems, by user and by kind.</summary>
    public static ActivitySummary Summary(IEnumerable<ActivityEntry> entries)
    {
        var users = new Dictionary<string, int[]>(); // entries, sign-ins, problems; by name as the log's User filter matches it
        var kinds = new Dictionary<string, int[]>(); // entries, problems
        int total = 0, signIns = 0, problems = 0;
        foreach (var e in entries)
        {
            var signIn = e.Area == "sign-in";
            var bad = e.Result != "ok";
            total++;
            signIns += signIn ? 1 : 0;
            problems += bad ? 1 : 0;
            var u = users.TryGetValue(e.By, out var had) ? had : users[e.By] = new int[3];
            u[0]++;
            u[1] += signIn ? 1 : 0;
            u[2] += bad ? 1 : 0;
            var kind = KindOf(e.Area);
            var k = kinds.TryGetValue(kind, out var was) ? was : kinds[kind] = new int[2];
            k[0]++;
            k[1] += bad ? 1 : 0;
        }
        return new ActivitySummary(total, signIns, total - signIns, problems,
            [.. users.Select(u => new ActivityUserCount(u.Key, u.Value[0], u.Value[1], u.Value[0] - u.Value[1], u.Value[2])).OrderByDescending(u => u.Entries).ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase)],
            [.. Kinds.Keys.Where(kinds.ContainsKey).Select(k => new ActivityKindCount(k, KindNames[k], kinds[k][0], kinds[k][1])).OrderByDescending(k => k.Entries)]);
    }

    // Both files, newest first.
    private List<ActivityEntry> All()
    {
        try
        {
            lock (gate)
            {
                return [.. Read(file), .. Read(older)];
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(e, "The activity log could not be read");
            return [];
        }
    }

    private static bool InRange(ActivityFilter f, ActivityEntry e) => (f.Since is null || e.At >= f.Since) && (f.Until is null || e.At < f.Until);

    // The kinds (one, or several with commas), the person, problems only and the words.
    private static Func<ActivityEntry, bool> Matches(ActivityFilter filter)
    {
        var picked = (filter.Kind ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(Kinds.ContainsKey).ToList();
        var areas = picked.Count == 0 ? null : picked.SelectMany(k => Kinds[k]).ToHashSet();
        var others = picked.Contains("admin"); // admin also has every area no other kind has
        var words = filter.Find?.Trim() is { Length: > 0 } q ? q : null;
        return e => (areas is null || areas.Contains(e.Area) || others && KindOf(e.Area) == "admin") && (filter.By is null || e.By == filter.By)
            && (!filter.Problems || e.Result != "ok")
            && (words is null || $"{e.Title} {e.By} {e.From} {e.Detail}".Contains(words, StringComparison.OrdinalIgnoreCase));
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

/// <summary>What Settings, Activity log asks for. Since: how far back (none: all of it). Until: up to, not including.</summary>
public sealed record ActivityFilter(string? Kind, string? By, string? Find, DateTime? Since, bool Problems, int Count, DateTime? Until = null);

/// <summary>One page of the activity log, with how many matched in all, who appears in the time asked about, and the changes its entries point at.</summary>
public sealed record ActivityPage(IReadOnlyList<ActivityEntry> Entries, int Total, int Problems, IReadOnlyList<string> People,
    IReadOnlyDictionary<string, ChangeBrief>? Changes = null);

/// <summary>How many entries matched, and of them sign-ins, changes (the rest) and problems; then by user and by kind, most first.</summary>
public sealed record ActivitySummary(int Total, int SignIns, int Changes, int Problems, IReadOnlyList<ActivityUserCount> Users, IReadOnlyList<ActivityKindCount> Areas);

public sealed record ActivityUserCount(string Name, int Entries, int SignIns, int Changes, int Problems);

public sealed record ActivityKindCount(string Kind, string Name, int Entries, int Problems);

public static class ActivityApi
{
    public static void MapActivityApi(this WebApplication app)
    {
        app.MapGet("/api/activity", (int? count, HttpContext context, ActivityLog activity) =>
            Access.Can(context, Permissions.ActivityView) ? Results.Ok(activity.Recent(Math.Clamp(count ?? 100, 1, 1000))) : Access.NotAllowed());

        app.MapGet("/api/activity/find", (string? kind, string? by, string? q, DateTime? since, DateTime? until, bool? problems, int? count, HttpContext context, ActivityLog activity, ChangeJournal changes) =>
            Access.Can(context, Permissions.ActivityView)
                ? Results.Ok(activity.Find(Filter(kind, by, q, since, until, problems, Math.Clamp(count ?? 50, 1, 5000))) is var page ? page with { Changes = changes.Briefs(context, page.Entries) } : null)
                : Access.NotAllowed());

        // The Summary over the log: the same filters, counted by user and by area.
        app.MapGet("/api/activity/summary", (string? kind, string? by, string? q, DateTime? since, DateTime? until, bool? problems, HttpContext context, ActivityLog activity) =>
            Access.Can(context, Permissions.ActivityView) ? Results.Ok(activity.Summarize(Filter(kind, by, q, since, until, problems, 0))) : Access.NotAllowed());

        // The same, as a CSV file to open in Excel.
        app.MapGet("/api/activity/export", (string? kind, string? by, string? q, DateTime? since, DateTime? until, bool? problems, HttpContext context, ActivityLog activity) =>
            Access.Can(context, Permissions.ActivityView)
                ? Results.File(Csv(activity.Find(Filter(kind, by, q, since, until, problems, int.MaxValue)).Entries), "text/csv", "rabbidlab-activity.csv")
                : Access.NotAllowed());
    }

    /// <summary>Entries as a CSV file for Excel, with its byte order mark.</summary>
    public static byte[] Csv(IEnumerable<ActivityEntry> entries)
    {
        var csv = new StringBuilder("Time (UTC),User,From,Area,Action,Result,Details\r\n");
        foreach (var e in entries)
        {
            csv.Append(string.Join(',', new[] { e.At.ToString("yyyy-MM-dd HH:mm:ss"), e.By, e.From ?? "", e.Area, e.Title, e.Result, e.Detail ?? "" }.Select(Cell))).Append("\r\n");
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static ActivityFilter Filter(string? kind, string? by, string? q, DateTime? since, DateTime? until, bool? problems, int count) =>
        new(kind is { Length: > 0 } ? kind : null, by is { Length: > 0 } ? by : null, q, since?.ToUniversalTime(), problems == true, count, until?.ToUniversalTime());

    // A CSV cell: quoted when it has a comma, quote or line in it, and never starting a formula in Excel.
    private static string Cell(string value)
    {
        var v = value.Length > 0 && "=+-@".Contains(value[0]) ? "'" + value : value;
        return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }
}
