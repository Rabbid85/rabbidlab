using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>The Activity report emailed on a schedule (Settings, Reports). Off until someone turns it on.</summary>
public sealed class ReportSettings
{
    public bool On { get; set; }

    /// <summary>daily, weekly or monthly.</summary>
    public string Every { get; set; } = "weekly";

    /// <summary>Weekly: 1 Monday to 7 Sunday. Monthly: the day of the month, 1 to 28.</summary>
    public int Day { get; set; } = 1;

    /// <summary>The hour it goes, on this server's clock.</summary>
    public int Hour { get; set; } = 8;

    /// <summary>The days it covers, up to midnight before it goes: 1, 7 or 30.</summary>
    public int Covers { get; set; } = 7;

    /// <summary>The email notifications it goes to, by id.</summary>
    public List<string> To { get; set; } = [];

    /// <summary>When it last ran, or when its schedule was set, so runs before that aren't caught up. UTC.</summary>
    public DateTime? Ran { get; set; }

    /// <summary>Failed tries at the run that's due, and when the last one was.</summary>
    public int Tries { get; set; }
    public DateTime? Tried { get; set; }
}

/// <summary>A report that went, or didn't: sent, partial or failed. From and Until are UTC, Until not included.</summary>
public sealed record ReportSent(string Id, DateTime At, DateTime From, DateTime Until, int Entries, List<string> To, List<string> ToIds, string Outcome, string? Why, bool Test, string By);

public sealed class ReportsFile
{
    public ReportSettings Activity { get; set; } = new();
    public List<ReportSent> Sent { get; set; } = [];
}

/// <summary>The schedule as the page sends it. A test sends with To and Covers as they are on the page.</summary>
public sealed record ReportForm(bool? On, string? Every, int? Day, int? Hour, int? Covers, List<string>? To);

/// <summary>When a scheduled report goes, on this server's clock, and the days it covers.</summary>
public static class ReportSchedule
{
    public static readonly string[] Everys = ["daily", "weekly", "monthly"];
    public static readonly int[] Spans = [1, 7, 30];

    /// <summary>The latest run at or before this time.</summary>
    public static DateTime Last(ReportSettings s, DateTime local)
    {
        switch (s.Every)
        {
            case "daily":
                var d = local.Date.AddHours(s.Hour);
                return d <= local ? d : d.AddDays(-1);
            case "monthly":
                var m = new DateTime(local.Year, local.Month, s.Day).AddHours(s.Hour);
                return m <= local ? m : m.AddMonths(-1);
            default:
                var w = local.Date.AddDays(-((Iso(local.DayOfWeek) - s.Day + 7) % 7)).AddHours(s.Hour);
                return w <= local ? w : w.AddDays(-7);
        }
    }

    /// <summary>The first run after this time.</summary>
    public static DateTime Next(ReportSettings s, DateTime local)
    {
        var last = Last(s, local);
        return s.Every switch { "daily" => last.AddDays(1), "monthly" => last.AddMonths(1), _ => last.AddDays(7) };
    }

    /// <summary>The run that's due and hasn't gone: only the latest, however many were missed while the server was off.</summary>
    public static DateTime? Due(ReportSettings s, DateTime local, DateTime? ranLocal) =>
        s.On && s.To.Count > 0 && Last(s, local) is var last && (ranLocal is null || last > ranLocal) ? last : null;

    /// <summary>The days a run covers: from midnight Covers days before, up to midnight of the day it goes.</summary>
    public static (DateTime From, DateTime Until) Period(ReportSettings s, DateTime run) => (run.Date.AddDays(-s.Covers), run.Date);

    private static int Iso(DayOfWeek d) => d == DayOfWeek.Sunday ? 7 : (int) d;

    /// <summary>How it's set, in words: Weekly on Monday at 8:00 AM.</summary>
    public static string Words(ReportSettings s) => s.Every switch
    {
        "daily" => $"Daily at {Hour(s.Hour)}",
        "monthly" => $"Monthly on the {Ordinal(s.Day)} at {Hour(s.Hour)}",
        _ => $"Weekly on {(DayOfWeek) (s.Day % 7)} at {Hour(s.Hour)}",
    };

    /// <summary>The days it covers, in words: the previous 7 days.</summary>
    public static string Covers(int days) => days == 1 ? "the previous day" : $"the previous {days} days";

    public static string Hour(int h) =>DateTime.MinValue.AddHours(h).ToString("h:mm tt", CultureInfo.InvariantCulture);

    private static string Ordinal(int n) => n + (n is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}

/// <summary>
/// The scheduled Activity report (reports.json): the schedule, sending it when it's due (ReportWorker), a test from the page,
/// and the reports that went. Each one goes through the email notifications it's set to, and into the Activity log.
/// </summary>
public sealed class Reports(DataFolder folder, ActivityLog activity, Channels channels, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<ReportsFile> file = folder.Json<ReportsFile>("reports.json", Json);
    private readonly SemaphoreSlim one = new(1, 1); // two looks at the schedule at once never send the same run twice

    /// <summary>How long after a run that couldn't be sent it's tried again, three tries in all.</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromMinutes(10);

    public static string ScheduledBy => Product.Title + " (scheduled report)";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private TimeZoneInfo Zone => clock.LocalTimeZone;

    /// <summary>The schedule, with only the email notifications that are still there.</summary>
    public ReportSettings Settings()
    {
        var emails = channels.Emails().Select(e => e.Id).ToHashSet();
        lock (file.Gate)
        {
            var s = file.Read().Activity;
            return new ReportSettings { On = s.On, Every = s.Every, Day = s.Day, Hour = s.Hour, Covers = s.Covers, To = [.. s.To.Where(emails.Contains)], Ran = s.Ran, Tries = s.Tries, Tried = s.Tried };
        }
    }

    /// <summary>Changes the schedule; the answer is why not. A new time starts from now, so it doesn't send for a time already gone.</summary>
    public string? Save(ReportForm form)
    {
        var s = Settings();
        var every = form.Every ?? s.Every;
        var day = form.Day ?? s.Day;
        var hour = form.Hour ?? s.Hour;
        var covers = form.Covers ?? s.Covers;
        if (!ReportSchedule.Everys.Contains(every))
        {
            return "Select how often to send the report.";
        }
        if (every == "weekly" && day is < 1 or > 7 || every == "monthly" && day is < 1 or > 28 || hour is < 0 or > 23 || !ReportSchedule.Spans.Contains(covers))
        {
            return "Select one of the listed options.";
        }
        var emails = channels.Emails().Select(e => e.Id).ToHashSet();
        var to = form.To is null ? s.To : [.. form.To.Where(emails.Contains).Distinct()];
        if (to.Count > 10)
        {
            return "Select up to 10 email notifications.";
        }
        lock (file.Gate)
        {
            var f = file.Read();
            var a = f.Activity;
            day = every == "daily" ? a.Day : day; // kept for when it's weekly or monthly again
            var timing = (form.On == true && !a.On) || every != a.Every || day != a.Day || hour != a.Hour;
            (a.On, a.Every, a.Day, a.Hour, a.Covers, a.To) = (form.On ?? a.On, every, day, hour, covers, to);
            if (timing)
            {
                (a.Ran, a.Tries, a.Tried) = (Now, 0, null);
            }
            file.Save(f);
        }
        return null;
    }

    /// <summary>The page's view: the schedule, the next run in words, the email notifications to pick from, and what went.</summary>
    public object View()
    {
        var s = Settings();
        var emails = channels.Emails();
        List<ReportSent> sent;
        lock (file.Gate)
        {
            sent = [.. Enumerable.Reverse(file.Read().Sent)]; // newest first: they are kept in the order they went
        }
        var next = s.On && s.To.Count > 0 ? ReportSchedule.Next(s, Local(Now)) : (DateTime?) null;
        return new
        {
            activity = new
            {
                s.On, s.Every, s.Day, s.Hour, s.Covers, s.To,
                next = next is { } n ? $"{n.ToString("dddd, MMM d", CultureInfo.InvariantCulture)} at {ReportSchedule.Hour(n.Hour)}" : null,
                words = ReportSchedule.Words(s),
            },
            emails,
            sent = sent.Select(r => new
            {
                r.Id, r.At, r.From, r.Until, period = Period(Local(r.From), Local(r.Until)), r.Entries, r.To, r.Outcome, r.Why, r.By,
                type = r.Test ? "test" : r.By == ScheduledBy ? "scheduled" : "again",
            }),
        };
    }

    /// <summary>One look at the schedule: sends the run that's due, if one is. What went, or null when nothing was due.</summary>
    public async Task<ReportSent?> Tick(CancellationToken stop)
    {
        await one.WaitAsync(stop);
        try
        {
            var s = Settings();
            if (ReportSchedule.Due(s, Local(Now), s.Ran is { } ran ? Local(ran) : null) is not { } run || s.Tries > 0 && s.Tried is { } t && Now - t < Retry)
            {
                return null;
            }
            var (from, until) = ReportSchedule.Period(s, run);
            ReportSent? sent = null;
            try
            {
                sent = await Send(s.To, Utc(from), Utc(until), false, ScheduledBy, null, stop);
            }
            finally
            {
                // Nothing went (or it broke): tried again later, three tries in all. Otherwise this run is done.
                lock (file.Gate)
                {
                    var f = file.Read();
                    var a = f.Activity;
                    if (sent?.Outcome is null or "failed" && a.Tries < 2)
                    {
                        (a.Tries, a.Tried) = (a.Tries + 1, Now);
                    }
                    else
                    {
                        (a.Ran, a.Tries, a.Tried) = (Now, 0, null);
                    }
                    file.Save(f);
                }
            }
            return sent;
        }
        finally
        {
            one.Release();
        }
    }

    /// <summary>A test from the page: the report up to now, over the days picked, to the notifications picked, saved or not.</summary>
    public Task<ReportSent> Test(IReadOnlyList<string> to, int covers, string by, string? from, CancellationToken stop)
    {
        var now = Local(Now);
        return Send(to, Utc(now.Date.AddDays(-covers)), null, true, by, from, stop);
    }

    /// <summary>A report that went before, for the same days (a test: up to now again), to the same notifications.</summary>
    public async Task<ReportSent?> Again(string id, string by, string? from, CancellationToken stop)
    {
        ReportSent? was;
        lock (file.Gate)
        {
            was = file.Read().Sent.FirstOrDefault(r => r.Id == id);
        }
        return was is null ? null : await Send(was.ToIds, was.From, was.Test ? null : was.Until, was.Test, by, from, stop);
    }

    /// <summary>
    /// Builds the report for the days given (no until: up to now) and sends it to each email notification, then writes it down
    /// twice: in the list here and in the Activity log.
    /// </summary>
    public async Task<ReportSent> Send(IReadOnlyList<string> to, DateTime fromUtc, DateTime? untilUtc, bool test, string by, string? address, CancellationToken stop)
    {
        var (summary, entries) = activity.Report(new ActivityFilter(null, null, null, fromUtc, false, int.MaxValue, untilUtc));
        var until = untilUtc ?? Now;
        var period = Period(Local(fromUtc), Local(until));
        var note = ReportMail.Build(summary, entries, period, Zone, test);
        var emails = channels.Emails().ToDictionary(e => e.Id);
        var went = new List<string>();
        var failed = new List<(string Name, string Why)>();
        foreach (var id in to.Distinct())
        {
            var name = emails.TryGetValue(id, out var e) ? e.Name : channels.Name(id);
            var problem = e is null ? "This is not an email notification, or it was deleted." : await channels.Send(id, note, stop);
            if (problem is null)
            {
                went.Add(name);
            }
            else
            {
                failed.Add((name, problem));
            }
        }
        var what = test ? "a test Activity report" : "the Activity report";
        if (went.Count > 0)
        {
            activity.Record(by, address, "admin", "report.send", $"Sent {what} to {And(went)}", "ok", $"{period}, {Count(summary.Total, "entry", "entries")}");
        }
        foreach (var (name, why) in failed)
        {
            activity.Record(by, address, "admin", "report.send", $"Could not send {what} to {name}", "failed", why);
        }
        var sent = new ReportSent(Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(), Now, fromUtc, until, summary.Total,
            [.. went, .. failed.Select(f => f.Name)], [.. to.Distinct()], failed.Count == 0 ? "sent" : went.Count == 0 ? "failed" : "partial",
            failed.Count == 0 ? null : string.Join(" · ", failed.Select(f => $"{f.Name}: {f.Why}")), test, by);
        lock (file.Gate)
        {
            var f = file.Read();
            f.Sent.Add(sent);
            f.Sent.RemoveAll(r => r.At < Now.AddDays(-365));
            if (f.Sent.Count > 100)
            {
                f.Sent.RemoveRange(0, f.Sent.Count - 100);
            }
            file.Save(f);
        }
        return sent;
    }

    private DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    // A time on this server's clock in UTC; one the clocks skip over (going forward) counts as an hour later.
    private DateTime Utc(DateTime local)
    {
        var t = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(Zone.IsInvalidTime(t) ? t.AddHours(1) : t, Zone);
    }

    /// <summary>The days covered, as people say them: Nov 15 to Nov 21, 2026. Until is not included.</summary>
    public static string Period(DateTime from, DateTime until)
    {
        var last = until > from ? until.AddTicks(-1) : from;
        var c = CultureInfo.InvariantCulture;
        return from.Date == last.Date ? last.ToString("MMM d, yyyy", c)
            : from.Year == last.Year ? $"{from.ToString("MMM d", c)} to {last.ToString("MMM d, yyyy", c)}"
            : $"{from.ToString("MMM d, yyyy", c)} to {last.ToString("MMM d, yyyy", c)}";
    }

    public static string Count(int n, string one, string many) => $"{n.ToString("N0", CultureInfo.InvariantCulture)} {(n == 1 ? one : many)}";

    public static string And(IReadOnlyList<string> names) => names.Count < 2 ? string.Concat(names) : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
}

/// <summary>The Activity report as an email: the summary, by user, by area and the latest problems, with every entry in a CSV file.</summary>
public static class ReportMail
{
    /// <summary>The most the CSV file can be. Microsoft 365 takes 3 MB attached to a message sent this way.</summary>
    public const int MostCsv = 2_500_000;

    public static MailNote Build(ActivitySummary s, IReadOnlyList<ActivityEntry> entries, string period, TimeZoneInfo zone, bool test)
    {
        var csv = ActivityApi.Csv(entries);
        var kept = entries.Count;
        while (csv.Length > MostCsv && kept > 0)
        {
            kept = (int) (kept * (MostCsv / (double) csv.Length) * 0.95);
            csv = ActivityApi.Csv(entries.Take(kept));
        }
        var name = $"activity-report-{period.Replace(",", "").Replace(' ', '-').ToLowerInvariant()}.csv";
        var subject = $"Activity report: {period}{(test ? " (test)" : "")}";
        return new MailNote(subject, "note",
            [("Period", period), ("Entries", s.Total.ToString("N0", CultureInfo.InvariantCulture)), ("Sign-ins", s.SignIns.ToString("N0", CultureInfo.InvariantCulture)),
                ("Changes", s.Changes.ToString("N0", CultureInfo.InvariantCulture)), ("Problems", s.Problems.ToString("N0", CultureInfo.InvariantCulture))],
            Html: Html(s, entries, period, zone, kept, test), Files: [new MailFile(name, "text/csv", csv)]);
    }

    /// <summary>The summary line: 40 entries: 15 sign-ins, 25 changes, 5 problems.</summary>
    public static string Line(ActivitySummary s) => s.Total == 0 ? "No entries."
        : $"{Reports.Count(s.Total, "entry", "entries")}: {Reports.Count(s.SignIns, "sign-in", "sign-ins")}, {Reports.Count(s.Changes, "change", "changes")}, {Reports.Count(s.Problems, "problem", "problems")}.";

    private static string Html(ActivitySummary s, IReadOnlyList<ActivityEntry> entries, string period, TimeZoneInfo zone, int kept, bool test)
    {
        static string E(string? v) => WebUtility.HtmlEncode(v ?? "");
        static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
        const string Th = "padding:6px 14px 6px 0;border-bottom:1px solid #1B1D29;text-align:left;font-size:12px;white-space:nowrap";
        const string Td = "padding:5px 14px 5px 0;border-bottom:1px solid #E3E5EC;font-size:13px;vertical-align:top";
        const string Red = ";color:#B4232F;font-weight:700";
        string Table(string[] heads, IEnumerable<string[]> rows, int bad = -1) =>
            $"<table style=\"width:100%;border-collapse:collapse;margin:6px 0 4px\"><tr>{string.Concat(heads.Select(h => $"<th style=\"{Th}\">{E(h)}</th>"))}</tr>"
            + string.Concat(rows.Select(r => "<tr>" + string.Concat(r.Select((c, i) => $"<td style=\"{Td}{(i == bad && c != "0" ? Red : "")}\">{E(c)}</td>")) + "</tr>")) + "</table>";
        static string H(string t) => $"<h2 style=\"margin:22px 0 4px;font-size:15px;color:#111\">{E(t)}</h2>";
        var c = CultureInfo.InvariantCulture;
        string When(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone).ToString("MMM d, h:mm tt", c);
        var cell = "padding:8px 14px;border:1px solid #C9CCD6";
        var strip = "<table style=\"border-collapse:collapse;margin:4px 0 6px\"><tr>"
            + string.Concat(new[] { ("Entries", s.Total), ("Sign-ins", s.SignIns), ("Changes", s.Changes), ("Problems", s.Problems) }.Select(x =>
                $"<td style=\"{cell}\"><div style=\"color:#5B6070;font-size:12px\">{x.Item1}</div><div style=\"font-size:22px;font-weight:700{(x.Item1 == "Problems" && x.Item2 > 0 ? ";color:#B4232F" : "")}\">{N(x.Item2)}</div></td>"))
            + "</tr></table>";
        var problems = entries.Where(e => e.Result != "ok").Take(10).ToList();
        var more = s.Problems > problems.Count ? $"<p style=\"margin:4px 0 0;color:#5B6070;font-size:12px\">The newest {N(problems.Count)} of {N(s.Problems)} are shown. The rest are in the attached file.</p>" : "";
        var attached = kept < entries.Count
            ? $"The attached CSV file has the newest {N(kept)} of {N(entries.Count)} entries. Export the rest from Settings &gt; Activity log."
            : "Every entry is in the attached CSV file.";
        return "<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1B1D29;max-width:680px\">"
            + $"<div style=\"color:#6B7088;font-size:12px\">{E(Product.Lab)}{(test ? " · Test report" : "")}</div>"
            + "<h1 style=\"margin:4px 0 2px;font-size:22px;color:#111\">Activity report</h1>"
            + $"<p style=\"margin:0 0 12px;color:#444955\">{E(period)}. {E(Line(s))}</p>"
            + strip
            + H("By user") + (s.Users.Count == 0 ? "<p>No entries.</p>" : Table(["User", "Entries", "Sign-ins", "Changes", "Problems"], s.Users.Select(u => new[] { u.Name, N(u.Entries), N(u.SignIns), N(u.Changes), N(u.Problems) }), 4))
            + H("By area") + (s.Areas.Count == 0 ? "<p>No entries.</p>" : Table(["Area", "Entries", "Problems"], s.Areas.Select(a => new[] { a.Name, N(a.Entries), N(a.Problems) }), 2))
            + H("Warnings and failures") + (problems.Count == 0 ? "<p style=\"margin:4px 0\">None.</p>"
                : Table(["When", "User", "Activity", "Result", "Details"], problems.Select(e => new[] { When(e.At), e.By, e.Title, e.Result == "warning" ? "Warning" : "Failed", e.Detail ?? "" })) + more)
            + $"<p style=\"margin:22px 0 4px;color:#5B6070;font-size:12px\">{attached} Times are in the server's time zone. To change this report, go to Settings &gt; Reports in {E(Product.Title)}.</p>"
            + $"<p style=\"margin:0;color:#8A8FA8;font-size:12px\">Sent by {E(Product.Lab)}.</p></div>";
    }
}

/// <summary>Sends the scheduled Activity report when it's due, looking once a minute; a run missed while the server was off goes when it's back.</summary>
public sealed class ReportWorker(Reports reports, TimeProvider clock, ILogger<ReportWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), clock, stop);
            try
            {
                await reports.Tick(stop);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "The scheduled Activity report could not be sent");
            }
        }
    }
}

public static class ReportsApi
{
    public static void MapReportsApi(this WebApplication app)
    {
        // Seen by whoever can see the Activity log; changed and sent by whoever can also manage alerts and email.
        app.MapGet("/api/reports", (HttpContext context, Reports reports) =>
            Access.Can(context, Permissions.ActivityView) ? Results.Ok(reports.View()) : Access.NotAllowed());

        app.MapPost("/api/reports/activity", (ReportForm form, HttpContext context, Reports reports, Channels channels, ActivityLog log) =>
        {
            if (Refused(context) is { } refused)
            {
                return refused;
            }
            var was = reports.Settings();
            if (reports.Save(form) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            var s = reports.Settings();
            var to = Reports.And([.. s.To.Select(channels.Name)]);
            log.Record(context, "admin", "report.settings", !s.On ? (was.On ? "Turned off the scheduled Activity report" : "Changed the scheduled Activity report")
                : was.On ? "Changed the scheduled Activity report" : "Turned on the scheduled Activity report",
                "ok", $"{ReportSchedule.Words(s)}, {ReportSchedule.Covers(s.Covers)}, to {(to.Length > 0 ? to : "no notifications yet")}");
            return Results.Ok(reports.View());
        });

        app.MapPost("/api/reports/activity/test", async (ReportForm form, HttpContext context, Reports reports, CancellationToken stop) =>
        {
            if (Refused(context) is { } refused)
            {
                return refused;
            }
            var to = form.To ?? reports.Settings().To;
            if (to.Count == 0)
            {
                return Results.BadRequest(new { error = "Select at least one email notification under Send to." });
            }
            var covers = form.Covers is { } c && ReportSchedule.Spans.Contains(c) ? c : reports.Settings().Covers;
            var sent = await reports.Test(to, covers, ActivityLog.Who(context), ActivityLog.Address(context), stop);
            return sent.Outcome == "failed" ? Results.BadRequest(new { error = sent.Why, now = reports.View() }) : Results.Ok(new { sent, now = reports.View() });
        });

        app.MapPost("/api/reports/sent/{id}/again", async (string id, HttpContext context, Reports reports, CancellationToken stop) =>
        {
            if (Refused(context) is { } refused)
            {
                return refused;
            }
            var sent = await reports.Again(id, ActivityLog.Who(context), ActivityLog.Address(context), stop);
            return sent is null ? Results.NotFound(new { error = "Report not found. It may have been removed from the list." })
                : sent.Outcome == "failed" ? Results.BadRequest(new { error = sent.Why, now = reports.View() }) : Results.Ok(new { sent, now = reports.View() });
        });
    }

    private static IResult? Refused(HttpContext context) =>
        Access.RefuseUnless(context, Permissions.SensorsAlerts) ?? (Access.Can(context, Permissions.ActivityView) ? null : Access.NotAllowed());
}
