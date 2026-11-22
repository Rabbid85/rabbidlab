using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>The Activity report: the Summary over the log, the schedule and its catch-up, the email, and Settings, Reports.</summary>
public sealed class ReportsTests : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-reports-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public ReportsTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
    }

    public void Dispose()
    {
        app.Dispose();
        try
        {
            Directory.Delete(data, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temp folder's own cleanup.
        }
    }

    private Reports Reports => app.Services.GetRequiredService<Reports>();

    private ActivityLog Log => app.Services.GetRequiredService<ActivityLog>();

    // An entry from some other time, written into the log as Record would have.
    private void Past(DateTime utc, string by, string area, string title, string result = "ok", string? detail = null) =>
        File.AppendAllText(Path.Combine(data, "activity.jsonl"), JsonSerializer.Serialize(new ActivityEntry(utc, by, null, area, "test", title, result, detail), Web) + "\n");

    private static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local);

    private static DateTime Utc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local);

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task The_summary_counts_sign_ins_changes_and_problems_by_user_and_by_area_for_the_same_filters_as_the_log()
    {
        var admin = await app.SignedIn();
        Log.Record("jsmith", "10.0.3.9", "sign-in", "signin", "Signed in", "ok");
        Log.Record("jsmith", "10.0.3.9", "sign-in", "signin.failed", "Sign-in failed", "failed");
        Log.Record("jsmith", null, "users", "ad.password", "Set a new password for Priya Raman", "ok");
        Log.Record("lbrooks", null, "links", "link.add", "Added the link Grafana", "ok");
        Log.Record("lbrooks", null, "clients", "client.approve", "Approved DESKTOP-01", "warning");
        Log.Record("lbrooks", null, "settings", "settings.change", "Changed a setting", "ok"); // no kind of its own: Updates and settings
        Past(new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc), "lbrooks", "links", "Added the link Proxmox");

        var all = await Summary(admin, "");
        var lbrooks = await Summary(admin, "by=lbrooks");
        var some = await Summary(admin, "by=lbrooks&kind=lab,seats");
        var bad = await Summary(admin, "by=jsmith&problems=true");
        var january = await Summary(admin, $"since={Uri.EscapeDataString("2026-01-01T00:00:00Z")}&until={Uri.EscapeDataString("2026-01-11T00:00:00Z")}");

        var jsmith = all.GetProperty("users").EnumerateArray().Single(u => u.GetProperty("name").GetString() == "jsmith");
        Assert.Equal((3, 2, 1, 1), (jsmith.GetProperty("entries").GetInt32(), jsmith.GetProperty("signIns").GetInt32(), jsmith.GetProperty("changes").GetInt32(), jsmith.GetProperty("problems").GetInt32()));
        Assert.Equal(all.GetProperty("total").GetInt32(), all.GetProperty("signIns").GetInt32() + all.GetProperty("changes").GetInt32());
        Assert.Equal((4, 0, 4, 1), (lbrooks.GetProperty("total").GetInt32(), lbrooks.GetProperty("signIns").GetInt32(), lbrooks.GetProperty("changes").GetInt32(), lbrooks.GetProperty("problems").GetInt32()));
        // By area, most first: two links, then the device and the setting.
        Assert.Equal(["lab", "seats", "admin"], lbrooks.GetProperty("areas").EnumerateArray().Select(a => a.GetProperty("kind").GetString()));
        Assert.Equal("Links and sensors", lbrooks.GetProperty("areas")[0].GetProperty("name").GetString());
        Assert.Equal(3, some.GetProperty("total").GetInt32());
        Assert.Equal((1, 1), (bad.GetProperty("total").GetInt32(), bad.GetProperty("signIns").GetInt32()));
        Assert.Equal(1, january.GetProperty("total").GetInt32());
        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>("/api/activity/find?kind=admin&by=lbrooks")).GetProperty("total").GetInt32()); // the log's filter counts it there too
    }

    [Fact]
    public async Task Only_people_who_can_see_the_activity_log_get_the_summary()
    {
        await app.SignedIn();
        app.Services.GetRequiredService<Accounts>().Add("mlopez", "user password", Roles.User);
        var user = app.Page();
        (await user.PostAsJsonAsync("/api/signin", new { username = "mlopez", password = "user password" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/activity/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/reports/activity", new { on = true })).StatusCode);
    }

    [Fact]
    public void A_schedule_runs_on_its_day_and_hour_and_catches_up_only_the_latest_run_it_missed()
    {
        var weekly = new ReportSettings { On = true, Every = "weekly", Day = 1, Hour = 8, Covers = 7, To = ["it"] };
        var sunday = new DateTime(2026, 11, 22, 15, 0, 0);

        Assert.Equal(new DateTime(2026, 11, 16, 8, 0, 0), ReportSchedule.Last(weekly, sunday));
        Assert.Equal(new DateTime(2026, 11, 23, 8, 0, 0), ReportSchedule.Next(weekly, sunday));
        Assert.Equal(new DateTime(2026, 11, 23, 8, 0, 0), ReportSchedule.Last(weekly, new DateTime(2026, 11, 23, 8, 0, 0)));
        Assert.Equal(new DateTime(2026, 11, 16, 8, 0, 0), ReportSchedule.Last(weekly, new DateTime(2026, 11, 23, 7, 59, 0)));
        Assert.Equal((new DateTime(2026, 11, 16), new DateTime(2026, 11, 23)), ReportSchedule.Period(weekly, new DateTime(2026, 11, 23, 8, 0, 0)));

        var sundays = new ReportSettings { On = true, Every = "weekly", Day = 7, Hour = 18, To = ["it"] };
        Assert.Equal(new DateTime(2026, 11, 15, 18, 0, 0), ReportSchedule.Last(sundays, sunday));
        Assert.Equal(new DateTime(2026, 11, 22, 18, 0, 0), ReportSchedule.Next(sundays, sunday));

        var daily = new ReportSettings { On = true, Every = "daily", Hour = 6, Covers = 1, To = ["it"] };
        Assert.Equal(new DateTime(2026, 11, 21, 6, 0, 0), ReportSchedule.Last(daily, new DateTime(2026, 11, 22, 5, 0, 0)));
        Assert.Equal(new DateTime(2026, 11, 22, 6, 0, 0), ReportSchedule.Next(daily, new DateTime(2026, 11, 22, 5, 0, 0)));

        var monthly = new ReportSettings { On = true, Every = "monthly", Day = 28, Hour = 23, Covers = 30, To = ["it"] };
        Assert.Equal(new DateTime(2026, 11, 28, 23, 0, 0), ReportSchedule.Last(monthly, new DateTime(2026, 12, 1)));
        Assert.Equal(new DateTime(2026, 12, 28, 23, 0, 0), ReportSchedule.Next(monthly, new DateTime(2026, 12, 1)));
        Assert.Equal(new DateTime(2027, 2, 28, 23, 0, 0), ReportSchedule.Last(monthly, new DateTime(2027, 3, 1)));

        // The server was off from Nov 10 to Nov 25: two Mondays went by, and only the latest goes.
        Assert.Equal(new DateTime(2026, 11, 23, 8, 0, 0), ReportSchedule.Due(weekly, new DateTime(2026, 11, 25, 10, 0, 0), new DateTime(2026, 11, 9, 8, 0, 5)));
        Assert.Null(ReportSchedule.Due(weekly, new DateTime(2026, 11, 25, 10, 0, 0), new DateTime(2026, 11, 23, 8, 0, 5)));
        Assert.Null(ReportSchedule.Due(new ReportSettings { Every = "weekly", Day = 1, Hour = 8, To = ["it"] }, sunday, null));
        Assert.Null(ReportSchedule.Due(new ReportSettings { On = true, Every = "weekly", Day = 1, Hour = 8 }, sunday, null));

        Assert.Equal("Weekly on Monday at 8:00 AM", ReportSchedule.Words(weekly));
        Assert.Equal("Monthly on the 28th at 11:00 PM", ReportSchedule.Words(monthly));
        Assert.Equal("Daily at 6:00 AM", ReportSchedule.Words(daily));
        Assert.Equal("Nov 16 to Nov 22, 2026", Server.Reports.Period(new DateTime(2026, 11, 16), new DateTime(2026, 11, 23)));
        Assert.Equal("Nov 22, 2026", Server.Reports.Period(new DateTime(2026, 11, 22), new DateTime(2026, 11, 23)));
        Assert.Equal("Dec 29, 2026 to Jan 4, 2027", Server.Reports.Period(new DateTime(2026, 12, 29), new DateTime(2027, 1, 5)));
    }

    [Fact]
    public async Task A_run_missed_while_the_server_was_off_goes_once_it_is_back_by_email_only_and_into_the_activity_log()
    {
        var admin = await app.SignedIn();
        var it = await TestApp.Notification(admin, "IT team");
        var discord = await TestApp.Notification(admin, "Lab chat", "discord", "https://discord.com/api/webhooks/1/abc");
        var hour = Local(app.Clock.GetUtcNow().UtcDateTime).AddHours(1).Hour;
        (await admin.PostAsJsonAsync("/api/reports/activity", new { on = true, every = "daily", hour, covers = 1, to = new[] { it, discord } })).EnsureSuccessStatusCode();

        Assert.Equal([it], Reports.Settings().To); // email only
        Assert.Null(await Reports.Tick(default)); // a run from before it was turned on never goes

        app.Clock.Advance(TimeSpan.FromDays(3));
        var run = ReportSchedule.Last(Reports.Settings(), Local(app.Clock.GetUtcNow().UtcDateTime));
        Past(Utc(run.Date.AddHours(-12)), "jsmith", "users", "Created user Nina Park");
        Past(Utc(run.Date.AddHours(-36)), "jsmith", "users", "Created user Old One");
        var sent = await Reports.Tick(default);
        var again = await Reports.Tick(default);

        Assert.NotNull(sent);
        Assert.Null(again);
        Assert.Equal(("sent", 1, Utc(run.Date.AddDays(-1)), Utc(run.Date)), (sent.Outcome, sent.Entries, sent.From, sent.Until));
        var mail = Assert.Single(app.Mail.Sent);
        Assert.Empty(app.Hooks.Sent);
        Assert.StartsWith("Activity report: ", mail.Subject);
        var csv = Encoding.UTF8.GetString(Assert.Single(mail.Files!).Bytes);
        Assert.Contains("Created user Nina Park", csv);
        Assert.DoesNotContain("Old One", csv);
        var logged = Log.Recent(5).First(e => e.Action == "report.send");
        Assert.Equal(($"{Product.Title} (scheduled report)", "Sent the Activity report to IT team", "ok"), (logged.By, logged.Title, logged.Result));
    }

    [Fact]
    public async Task A_report_that_cannot_be_sent_is_a_problem_in_the_log_and_is_tried_three_times()
    {
        var admin = await app.SignedIn();
        var it = await TestApp.Notification(admin, "IT team");
        // Two hours on, so the next day's run is still over an hour away after the three tries, whatever the time now.
        var hour = Local(app.Clock.GetUtcNow().UtcDateTime).AddHours(2).Hour;
        (await admin.PostAsJsonAsync("/api/reports/activity", new { on = true, every = "daily", hour, covers = 1, to = new[] { it } })).EnsureSuccessStatusCode();
        app.Mail.Fails = "Could not connect to smtp.amw.com: Connection refused";
        app.Clock.Advance(TimeSpan.FromDays(1));

        var tries = new List<string?>();
        tries.Add((await Reports.Tick(default))?.Outcome);
        tries.Add((await Reports.Tick(default))?.Outcome); // too soon
        for (var i = 0; i < 3; i++)
        {
            app.Clock.Advance(Server.Reports.Retry + TimeSpan.FromMinutes(1));
            tries.Add((await Reports.Tick(default))?.Outcome);
        }

        Assert.Equal(["failed", null, "failed", "failed", null], tries);
        var problem = Log.Recent(20).First(e => e.Action == "report.send");
        Assert.Equal(("Could not send the Activity report to IT team", "failed", "Could not connect to smtp.amw.com: Connection refused"), (problem.Title, problem.Result, problem.Detail));
        admin = await app.SignedIn(); // signed out while idle, as the clock went on
        Assert.Equal(3, (await admin.GetFromJsonAsync<JsonElement>("/api/reports")).GetProperty("sent").EnumerateArray().Count(r => r.GetProperty("outcome").GetString() == "failed"));
    }

    [Fact]
    public void The_email_has_the_summary_by_user_and_by_area_and_the_problems_with_every_entry_in_a_CSV_file()
    {
        var at = new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);
        ActivityEntry[] entries =
        [
            new(at, "jsmith", "10.0.3.9", "sign-in", "signin.failed", "Sign-in failed", "failed", "Wrong password"),
            new(at.AddMinutes(-5), "jsmith", "10.0.3.9", "sign-in", "signin", "Signed in", "ok", null),
            new(at.AddMinutes(-9), "lbrooks", null, "users", "ad.create", "Created user <Nina>", "warning", "Staff/Finance"),
        ];

        var note = ReportMail.Build(ActivityLog.Summary(entries), entries, "Nov 15 to Nov 21, 2026", TimeZoneInfo.Utc, test: false);

        Assert.Equal("Activity report: Nov 15 to Nov 21, 2026", note.Subject);
        Assert.Contains(("Entries", "3"), note.Facts);
        Assert.Contains(("Problems", "2"), note.Facts);
        Assert.Contains("Nov 15 to Nov 21, 2026. 3 entries: 2 sign-ins, 1 change, 2 problems.", note.Html);
        foreach (var part in new[] { "By user", "jsmith", "lbrooks", "By area", "Sign-ins", "People and accounts", "Warnings and failures", "Wrong password", "Nov 20, 3:00 PM", "Created user &lt;Nina&gt;" })
        {
            Assert.Contains(part, note.Html);
        }
        Assert.DoesNotContain("<Nina>", note.Html);
        var file = Assert.Single(note.Files!);
        Assert.Equal(("activity-report-nov-15-to-nov-21-2026.csv", "text/csv"), (file.Name, file.Type));
        Assert.Contains("Created user <Nina>", Encoding.UTF8.GetString(file.Bytes));
        Assert.Contains("Every entry is in the attached CSV file.", note.Html);
        Assert.EndsWith("(test)", ReportMail.Build(ActivityLog.Summary(entries), entries, "Nov 15 to Nov 21, 2026", TimeZoneInfo.Utc, test: true).Subject);
    }

    [Fact]
    public void A_very_big_CSV_file_keeps_the_newest_entries_and_says_so()
    {
        var at = new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);
        var entries = Enumerable.Range(0, 30000).Select(i => new ActivityEntry(at.AddSeconds(-i), "jsmith", null, "links", "link.check", $"Checked link {i}", "ok", new string('x', 120))).ToList();

        var note = ReportMail.Build(ActivityLog.Summary(entries), entries, "Nov 20, 2026", TimeZoneInfo.Utc, test: false);

        var csv = Encoding.UTF8.GetString(note.Files![0].Bytes);
        Assert.True(note.Files[0].Bytes.Length <= ReportMail.MostCsv);
        Assert.Contains("Checked link 0,", csv);
        Assert.DoesNotContain("Checked link 29999,", csv);
        Assert.Contains("The attached CSV file has the newest", note.Html);
    }

    [Fact]
    public async Task Settings_reports_is_off_until_turned_on_takes_only_listed_choices_and_saving_is_logged()
    {
        var admin = await app.SignedIn();
        var it = await TestApp.Notification(admin, "IT team");
        await TestApp.Notification(admin, "Lab chat", "discord", "https://discord.com/api/webhooks/1/abc");

        var first = await admin.GetFromJsonAsync<JsonElement>("/api/reports");
        var bad = new[]
        {
            await admin.PostAsJsonAsync("/api/reports/activity", new { on = true, every = "hourly" }),
            await admin.PostAsJsonAsync("/api/reports/activity", new { on = true, every = "monthly", day = 29 }),
            await admin.PostAsJsonAsync("/api/reports/activity", new { on = true, covers = 14 }),
            await admin.PostAsJsonAsync("/api/reports/activity", new { on = true, hour = 24 }),
        };
        var saved = await Json(await admin.PostAsJsonAsync("/api/reports/activity", new { on = true, every = "monthly", day = 1, hour = 7, covers = 30, to = new[] { it } }));

        var a = first.GetProperty("activity");
        Assert.False(a.GetProperty("on").GetBoolean());
        Assert.Equal(JsonValueKind.Null, a.GetProperty("next").ValueKind);
        Assert.Equal(["IT team"], first.GetProperty("emails").EnumerateArray().Select(e => e.GetProperty("name").GetString())); // email notifications only
        Assert.All(bad, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        var s = saved.GetProperty("activity");
        Assert.Equal((true, "monthly", 1, 7, 30), (s.GetProperty("on").GetBoolean(), s.GetProperty("every").GetString(), s.GetProperty("day").GetInt32(), s.GetProperty("hour").GetInt32(), s.GetProperty("covers").GetInt32()));
        Assert.Equal("Monthly on the 1st at 7:00 AM", s.GetProperty("words").GetString());
        Assert.EndsWith("at 7:00 AM", s.GetProperty("next").GetString());
        var logged = Log.Recent(5).First(e => e.Action == "report.settings");
        Assert.Equal(("Turned on the scheduled Activity report", "Monthly on the 1st at 7:00 AM, the previous 30 days, to IT team"), (logged.Title, logged.Detail));
    }

    [Fact]
    public async Task Changing_the_schedule_takes_managing_alerts_and_email_and_seeing_it_takes_the_activity_log()
    {
        var admin = await app.SignedIn();
        var made = await Json(await admin.PostAsJsonAsync("/api/roles", new { name = "Auditors", color = "#5FD4A0" }));
        var role = made.GetProperty("id").GetString()!;
        (await admin.PostAsJsonAsync($"/api/roles/{role}", new { name = "Auditors", color = "#5FD4A0", permissions = new[] { "activity.view" } })).EnsureSuccessStatusCode();
        app.Services.GetRequiredService<Accounts>().Add("audit", "audit password", Roles.User);
        (await admin.PostAsJsonAsync($"/api/roles/{role}/members", new { kind = "local", name = "audit" })).EnsureSuccessStatusCode();
        var auditor = app.Page();
        (await auditor.PostAsJsonAsync("/api/signin", new { username = "audit", password = "audit password" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await auditor.GetAsync("/api/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await auditor.GetAsync("/api/activity/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await auditor.PostAsJsonAsync("/api/reports/activity", new { on = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await auditor.PostAsJsonAsync("/api/reports/activity/test", new { })).StatusCode);
        Assert.False(Reports.Settings().On);
    }

    [Fact]
    public async Task A_test_report_goes_now_with_todays_entries_and_a_sent_one_can_go_again()
    {
        var admin = await app.SignedIn();
        var it = await TestApp.Notification(admin, "IT team");
        Log.Record("jsmith", null, "users", "ad.create", "Created user Nina Park", "ok");

        var none = await admin.PostAsJsonAsync("/api/reports/activity/test", new { to = Array.Empty<string>(), covers = 7 });
        var test = await Json(await admin.PostAsJsonAsync("/api/reports/activity/test", new { to = new[] { it }, covers = 7 }));
        var id = test.GetProperty("sent").GetProperty("id").GetString();
        var again = await admin.PostAsJsonAsync($"/api/reports/sent/{id}/again", new { });

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, app.Mail.Sent.Count);
        Assert.EndsWith("(test)", app.Mail.Sent[0].Subject);
        Assert.Contains("Created user Nina Park", Encoding.UTF8.GetString(app.Mail.Sent[0].Files![0].Bytes));
        Assert.Equal(["me@example.com"], app.Mail.With[0].To);
        Assert.Contains(Log.Recent(10), e => e.Title == "Sent a test Activity report to IT team" && e.By == "admin");
        var sent = (await admin.GetFromJsonAsync<JsonElement>("/api/reports")).GetProperty("sent");
        Assert.Equal(2, sent.GetArrayLength());
        Assert.All(sent.EnumerateArray(), r => Assert.Equal(("test", "sent"), (r.GetProperty("type").GetString(), r.GetProperty("outcome").GetString())));
        Assert.Equal(sent[1].GetProperty("entries").GetInt32() + 1, sent[0].GetProperty("entries").GetInt32()); // newest first, with the first one's own entry
        Assert.False(Reports.Settings().On); // a test never turns the schedule on
    }

    private static async Task<JsonElement> Summary(HttpClient client, string query) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/activity/summary?{query}");
}
