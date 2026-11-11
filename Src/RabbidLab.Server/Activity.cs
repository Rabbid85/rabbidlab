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

public static class ActivityApi
{
    public static void MapActivityApi(this WebApplication app) =>
        app.MapGet("/api/activity", (int? count, HttpContext context, ActivityLog activity) =>
            Access.Can(context, Permissions.ActivityView) ? Results.Ok(activity.Recent(Math.Clamp(count ?? 100, 1, 1000))) : Access.NotAllowed());
}
