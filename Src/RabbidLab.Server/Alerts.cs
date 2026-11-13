using System.Globalization;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>One kind of alert as set in Settings, Alert rules: whether it's on, its times or limit, and the notifications it goes to.</summary>
public sealed class AlertSetting
{
    public bool On { get; set; } = true;

    /// <summary>Minutes down before the first alert, for the down rules.</summary>
    public int After { get; set; }

    /// <summary>Minutes between reminders while it lasts; 0 for one alert.</summary>
    public int Again { get; set; }

    /// <summary>Whether an all-clear goes when it's over.</summary>
    public bool Back { get; set; } = true;

    /// <summary>The rule's number: days before a certificate expires, % free, minutes quiet or failed sign-ins.</summary>
    public int Limit { get; set; }

    /// <summary>The notifications it goes to, by id.</summary>
    public List<string> To { get; set; } = [];
}

public sealed class AlertsFile
{
    public Dictionary<string, AlertSetting> Rules { get; set; } = [];
    public bool QuietOn { get; set; }

    /// <summary>Quiet hours, this server's local time, HH:mm.</summary>
    public string QuietFrom { get; set; } = "22:00";
    public string QuietTo { get; set; } = "07:00";

    /// <summary>A sensor or link going down still alerts during quiet hours.</summary>
    public bool DownInQuiet { get; set; } = true;

    /// <summary>The rules were started from what new sensors used to start with.</summary>
    public bool Seeded { get; set; }

    /// <summary>The update already told about as ready, and the failure already told about, so neither is said twice.</summary>
    public Dictionary<string, string> Told { get; set; } = [];

    /// <summary>The alert types sensors and watched links pick from, and the one new ones start with.</summary>
    public List<AlertType> Types { get; set; } = [];
    public string? StartType { get; set; }
}

/// <summary>
/// A named way to alert, such as Default or Critical, that sensors and watched links pick by name: how long down before
/// the alert, reminders while it stays down, the all-clear, and the notifications it goes to.
/// </summary>
public sealed class AlertType
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int After { get; set; } = 5;
    public int Again { get; set; }
    public bool Back { get; set; } = true;
    public List<string> To { get; set; } = [];
}

/// <summary>An alert type as the page sends it. Start makes it the one new sensors and links start with.</summary>
public sealed record AlertTypeForm(string? Name, int? After, int? Again, bool? Back, List<string>? To, bool? Start);

/// <summary>An alert that went, was held for quiet hours, or didn't go: sent, partial, failed or held.</summary>
public sealed record AlertEntry(DateTime At, string Rule, string Title, List<string> To, string Outcome, string? Why = null);

public sealed class AlertHistoryFile
{
    public List<AlertEntry> Entries { get; set; } = [];
}

/// <summary>A rule as the page sends it.</summary>
public sealed record AlertSettingForm(bool? On, int? After, int? Again, bool? Back, int? Limit, List<string>? To);

public sealed record QuietForm(bool? On, string? From, string? To, bool? DownStillGoes);

/// <summary>
/// The alert rules (alerts.json), quiet hours, and the history of every alert (alert-history.json). Alerts go out through
/// here, so each one is written down with where it went and whether it got there.
/// </summary>
public sealed class Alerts(DataFolder folder, Channels channels, TimeProvider clock)
{
    /// <summary>The rules, in the order the page shows them.</summary>
    public static readonly string[] Keys = ["sensor.down", "sensor.cert", "sensor.space", "sensor.hardware", "link.down", "seat.offline", "update.ready", "update.failed", "signin.failing"];
    public const string SensorDown = "sensor.down";
    public const string LinkDown = "link.down";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions Compact = new(JsonSerializerDefaults.Web);
    private readonly JsonFile<AlertsFile> file = folder.Json<AlertsFile>("alerts.json", Json);
    private readonly JsonFile<AlertHistoryFile> history = folder.Json<AlertHistoryFile>("alert-history.json", Compact);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // What each rule starts as.
    private static AlertSetting Start(string key) => key switch
    {
        "sensor.down" or "link.down" => new() { After = 5, Back = true },
        "sensor.cert" => new() { Limit = 21, Again = 10080, Back = false },
        "sensor.space" => new() { Limit = 10, Again = 1440, Back = false },
        "sensor.hardware" => new() { Again = 1440, Back = false },
        "seat.offline" => new() { On = false, Limit = 15, Back = true },
        "update.ready" => new() { On = false, Back = false },
        "signin.failing" => new() { On = false, Limit = 5, Back = false },
        _ => new() { Back = false },
    };

    /// <summary>A rule as it's set, or as it starts.</summary>
    public AlertSetting Rule(string key)
    {
        lock (file.Gate)
        {
            var r = file.Read().Rules.GetValueOrDefault(key) ?? Start(key);
            return new AlertSetting { On = r.On, After = r.After, Again = r.Again, Back = r.Back, Limit = r.Limit, To = [.. r.To.Where(channels.Known)] };
        }
    }

    /// <summary>The rule's times as a sensor's: after, again, and back. Only for sensors from before alert types.</summary>
    public AlertRule Times(string key)
    {
        var r = Rule(key);
        return new AlertRule { Id = "follow", Name = "Follows its rule", Color = "#8A8FA8", After = r.On ? r.After : -1, Again = r.On ? r.Again : 0, Back = r.On && r.Back };
    }

    // --- Alert types: named ways to alert that sensors and watched links pick ----------------------------------------------

    // The first time: Default from how sensors went down, and Watched links too when links went down another way.
    private void EnsureTypes(AlertsFile f)
    {
        if (f.Types.Count > 0)
        {
            return;
        }
        AlertType From(string key, string name)
        {
            var r = f.Rules.GetValueOrDefault(key) ?? Start(key);
            return new AlertType { Id = NewId(), Name = name, After = Math.Max(0, r.After), Again = r.Again, Back = r.Back, To = [.. r.To] };
        }
        var plain = From(SensorDown, "Default");
        f.Types.Add(plain);
        var links = From(LinkDown, "Watched links");
        if (links.After != plain.After || links.Again != plain.Again || links.Back != plain.Back || !links.To.ToHashSet().SetEquals(plain.To))
        {
            f.Types.Add(links);
        }
        f.StartType = plain.Id;
        file.Save(f);
    }

    private static string NewId() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    private AlertType Copy(AlertType t) => new() { Id = t.Id, Name = t.Name, After = t.After, Again = t.Again, Back = t.Back, To = [.. t.To.Where(channels.Known)] };

    /// <summary>The alert types, in the order they were made.</summary>
    public List<AlertType> Types()
    {
        lock (file.Gate)
        {
            var f = file.Read();
            EnsureTypes(f);
            return [.. f.Types.Select(Copy)];
        }
    }

    /// <summary>An alert type by id, or null.</summary>
    public AlertType? Type(string? id)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            EnsureTypes(f);
            return f.Types.FirstOrDefault(t => t.Id == id) is { } t ? Copy(t) : null;
        }
    }

    /// <summary>The alert type new sensors and watched links start with.</summary>
    public AlertType StartType()
    {
        lock (file.Gate)
        {
            var f = file.Read();
            EnsureTypes(f);
            return Copy(f.Types.FirstOrDefault(t => t.Id == f.StartType) ?? f.Types[0]);
        }
    }

    /// <summary>An alert type's times as a sensor's.</summary>
    public static AlertRule TimesOf(AlertType t) => new() { Id = t.Id, Name = t.Name, Color = "#8A8FA8", After = t.After, Again = t.Again, Back = t.Back };

    /// <summary>Adds an alert type (id null) or changes one; the type, or why not.</summary>
    public (AlertType? Type, string? Problem) SaveType(string? id, AlertTypeForm form)
    {
        var name = form.Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            return (null, "Give it a name, such as Critical, Normal or Office hours. It's what sensors and links pick it by.");
        }
        if (name.Length > 40)
        {
            return (null, "Keep the name to 40 characters.");
        }
        if (form.After is not (0 or 1 or 5 or 10 or 30 or 60) || form.Again is not (0 or 60 or 360 or 1440))
        {
            return (null, "Pick one of the times offered.");
        }
        lock (file.Gate)
        {
            var f = file.Read();
            EnsureTypes(f);
            if (f.Types.Any(t => t.Id != id && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return (null, $"There's an alert type called {name} already. Give this one another name.");
            }
            var t = id is null ? new AlertType { Id = NewId() } : f.Types.FirstOrDefault(x => x.Id == id);
            if (t is null)
            {
                return (null, "That alert type isn't here any more.");
            }
            if (id is null && f.Types.Count >= 20)
            {
                return (null, "That's 20 alert types, which is as many as there's room for.");
            }
            (t.Name, t.After, t.Again, t.Back) = (name, form.After.Value, form.Again.Value, form.Back ?? true);
            t.To = [.. (form.To ?? []).Where(channels.Known).Distinct()];
            if (id is null)
            {
                f.Types.Add(t);
            }
            if (form.Start == true)
            {
                f.StartType = t.Id;
            }
            file.Save(f);
            return (Copy(t), null);
        }
    }

    /// <summary>Takes an alert type away; what used it moves to the one new sensors start with. That one's id, or why not.</summary>
    public (string? MovedTo, string? Problem) DeleteType(string id)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            EnsureTypes(f);
            if (f.Types.All(t => t.Id != id))
            {
                return (null, "That alert type isn't here any more.");
            }
            if (f.Types.Count == 1)
            {
                return (null, "It's the only alert type. Make another one first.");
            }
            f.Types.RemoveAll(t => t.Id == id);
            if (f.StartType == id || f.Types.All(t => t.Id != f.StartType))
            {
                f.StartType = f.Types[0].Id;
            }
            file.Save(f);
            return (f.StartType, null);
        }
    }

    /// <summary>
    /// The alert type with these times and notifications, made with this name (or one like it) when there isn't one yet.
    /// For sensors from before alert types.
    /// </summary>
    public string Match(int after, int again, bool back, IReadOnlyCollection<string> to, string name)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            EnsureTypes(f);
            var want = to.Where(channels.Known).ToHashSet();
            if (f.Types.FirstOrDefault(t => t.After == after && t.Again == again && t.Back == back && t.To.ToHashSet().SetEquals(want)) is { } same)
            {
                return same.Id;
            }
            var unique = name;
            for (var n = 2; f.Types.Any(t => string.Equals(t.Name, unique, StringComparison.OrdinalIgnoreCase)); n++)
            {
                unique = $"{name} {n}";
            }
            var made = new AlertType { Id = NewId(), Name = unique, After = after, Again = again, Back = back, To = [.. want] };
            f.Types.Add(made);
            file.Save(f);
            return made.Id;
        }
    }

    /// <summary>
    /// Starts the rules from how alerts used to be set: the times new sensors started with, for the down rules, and the
    /// notifications that were on for new sensors, for all of them. Once.
    /// </summary>
    public void Seed(AlertRule start, List<string> to)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Seeded)
            {
                return;
            }
            foreach (var key in Keys)
            {
                var r = f.Rules.GetValueOrDefault(key) ?? Start(key);
                if (key is SensorDown or LinkDown)
                {
                    (r.On, r.After, r.Again, r.Back) = (start.After >= 0, Math.Max(0, start.After), start.Again, start.Back);
                }
                r.To = [.. to];
                f.Rules[key] = r;
            }
            f.Seeded = true;
            file.Save(f);
        }
    }

    /// <summary>Changes a rule; the answer is why not.</summary>
    public string? Save(string key, AlertSettingForm form)
    {
        if (!Keys.Contains(key))
        {
            return "That isn't one of the alert rules.";
        }
        var r = Rule(key);
        r.On = form.On ?? r.On;
        if (key is SensorDown or LinkDown)
        {
            if (form.After is not (null or 0 or 1 or 5 or 10 or 30 or 60) || form.Again is not (null or 0 or 60 or 360 or 1440))
            {
                return "Pick one of the times offered.";
            }
            r.After = form.After ?? r.After;
            r.Again = form.Again ?? r.Again;
            r.Back = form.Back ?? r.Back;
        }
        else if (key is "sensor.cert" or "sensor.space" or "sensor.hardware")
        {
            int[] limits = key == "sensor.cert" ? [14, 21, 30, 60] : key == "sensor.space" ? [5, 10, 20] : [0];
            int[] agains = key == "sensor.cert" ? [0, 1440, 10080] : [0, 60, 360, 1440];
            if (form.Limit is { } limit && key != "sensor.hardware" && !limits.Contains(limit) || form.Again is { } again && !agains.Contains(again))
            {
                return "Pick one of the times offered.";
            }
            r.Limit = key == "sensor.hardware" ? 0 : form.Limit ?? r.Limit;
            r.Again = form.Again ?? r.Again;
        }
        else if (key == "seat.offline")
        {
            if (form.Limit is not (null or 5 or 15 or 30 or 60))
            {
                return "Pick one of the times offered.";
            }
            r.Limit = form.Limit ?? r.Limit;
            r.Back = form.Back ?? r.Back;
        }
        else if (key == "signin.failing")
        {
            if (form.Limit is not (null or 3 or 5 or 10))
            {
                return "Pick one of the numbers offered.";
            }
            r.Limit = form.Limit ?? r.Limit;
        }
        if (form.To is { } to)
        {
            r.To = [.. to.Where(channels.Known).Distinct()];
        }
        lock (file.Gate)
        {
            var f = file.Read();
            f.Rules[key] = r;
            file.Save(f);
        }
        return null;
    }

    /// <summary>Whether this (an update's version, a failure) hasn't been told about yet.</summary>
    public bool NotTold(string kind, string what)
    {
        lock (file.Gate)
        {
            return file.Read().Told.GetValueOrDefault(kind) != what;
        }
    }

    public void Told(string kind, string what)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            f.Told[kind] = what;
            file.Save(f);
        }
    }

    /// <summary>A notification that's gone comes off every rule.</summary>
    public void Forget(string channel)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Rules.Values.Count(r => r.To.Remove(channel)) + f.Types.Count(t => t.To.Remove(channel)) > 0)
            {
                file.Save(f);
            }
        }
    }

    public string? SetQuiet(QuietForm form)
    {
        static bool Time(string? t) => t is not null && TimeOnly.TryParseExact(t, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) && at.Minute % 30 == 0;
        if (form.From is not null && !Time(form.From) || form.To is not null && !Time(form.To))
        {
            return "Pick one of the times offered.";
        }
        lock (file.Gate)
        {
            var f = file.Read();
            f.QuietOn = form.On ?? f.QuietOn;
            f.QuietFrom = form.From ?? f.QuietFrom;
            f.QuietTo = form.To ?? f.QuietTo;
            f.DownInQuiet = form.DownStillGoes ?? f.DownInQuiet;
            file.Save(f);
            return null;
        }
    }

    /// <summary>Whether it's quiet hours now, on this server's clock.</summary>
    public bool Quiet(DateTime utc)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (!f.QuietOn || !TimeOnly.TryParse(f.QuietFrom, CultureInfo.InvariantCulture, out var from) || !TimeOnly.TryParse(f.QuietTo, CultureInfo.InvariantCulture, out var to) || from == to)
            {
                return false;
            }
            var now = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local));
            return from < to ? now >= from && now < to : now >= from || now < to;
        }
    }

    /// <summary>Whether an alert of this kind waits for quiet hours to end, now.</summary>
    public bool Holds(string key, DateTime utc)
    {
        bool down;
        lock (file.Gate)
        {
            down = file.Read().DownInQuiet && key is SensorDown or LinkDown;
        }
        return !down && Quiet(utc);
    }

    /// <summary>
    /// Sends an alert to its notifications and writes it down. What went (by name) and what didn't (with why), for the
    /// sensor's own history.
    /// </summary>
    public async Task<(List<string> Went, List<string> Failed)> Send(string key, MailNote note, IReadOnlyList<string> to, CancellationToken stop)
    {
        var went = new List<string>();
        var failed = new List<string>();
        foreach (var id in to.Distinct())
        {
            var problem = await channels.Send(id, note, stop);
            var name = channels.Name(id);
            if (problem is null)
            {
                went.Add(name);
            }
            else
            {
                failed.Add($"{name}: {problem}");
            }
        }
        Write(new AlertEntry(Now, key, note.Card?.Title ?? note.Subject, [.. to.Distinct().Select(channels.Name)],
            failed.Count == 0 ? "sent" : went.Count == 0 ? "failed" : "partial", failed.Count == 0 ? null : string.Join(" · ", failed)));
        return (went, failed);
    }

    /// <summary>An alert held back for quiet hours, written down once when it's first held.</summary>
    public void Held(string key, string title, IReadOnlyList<string> to) =>
        Write(new AlertEntry(Now, key, title, [.. to.Distinct().Select(channels.Name)], "held"));

    private void Write(AlertEntry entry)
    {
        lock (history.Gate)
        {
            var h = history.Read();
            h.Entries.Add(entry);
            var old = Now.AddDays(-90);
            h.Entries.RemoveAll(e => e.At < old);
            if (h.Entries.Count > 2000)
            {
                h.Entries.RemoveRange(0, h.Entries.Count - 2000);
            }
            history.Save(h);
        }
    }

    /// <summary>The history, newest first: the last days, only the ones that didn't go, or matching the words typed.</summary>
    public List<AlertEntry> History(int days, bool failed, string? find)
    {
        var since = Now.AddDays(-Math.Clamp(days, 1, 90));
        var words = find?.Trim() ?? "";
        lock (history.Gate)
        {
            return [.. history.Read().Entries.Where(e => e.At >= since && (!failed || e.Outcome is "failed" or "partial")
                    && (words.Length == 0 || e.Title.Contains(words, StringComparison.OrdinalIgnoreCase) || e.To.Any(t => t.Contains(words, StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(e => e.At).Take(500)];
        }
    }

    /// <summary>The rules and quiet hours for the page.</summary>
    public object View()
    {
        lock (file.Gate)
        {
            var f = file.Read();
            return new
            {
                rules = Keys.Where(k => k is not (SensorDown or LinkDown)).ToDictionary(k => k, k => Rule(k)),
                types = Types(),
                startType = StartType().Id,
                quiet = new { on = f.QuietOn, from = f.QuietFrom, to = f.QuietTo, downStillGoes = f.DownInQuiet, now = Quiet(Now) },
            };
        }
    }
}

/// <summary>
/// The alerts nothing else sends, checked every 30 seconds: a seat whose client went quiet (and its all-clear), an update
/// that's ready or didn't install, and sign-ins that keep failing for one account.
/// </summary>
public sealed class AlertWatch(Alerts alerts, Clients clients, RabbidLab.Updater.UpdateEngine updates, ActivityLog activity, TimeProvider clock, ILogger<AlertWatch> log) : BackgroundService
{
    private readonly Dictionary<string, DateTime> quietSeats = []; // seats told about, by id
    private readonly HashSet<string> held = []; // held for quiet hours, written down once
    private readonly System.Collections.Concurrent.ConcurrentQueue<(DateTime At, string Who, string? From)> failures = new();
    private readonly List<(DateTime At, string Who, string? From)> recent = [];
    private readonly Dictionary<string, DateTime> toldSignIns = [];

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        Listen();
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Round(stop);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Looking for alerts failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), clock, stop);
        }
    }

    /// <summary>Starts counting failed sign-ins from the activity log.</summary>
    public void Listen() => activity.Recorded += e =>
    {
        if (e.Action == "signin.failed")
        {
            failures.Enqueue((Now, e.By, e.From));
        }
    };

    /// <summary>One look at seats, updates and sign-ins.</summary>
    public async Task Round(CancellationToken stop)
    {
        await Seats(stop);
        await Update(stop);
        await SignIns(stop);
    }

    // Held for quiet hours: written down once, then it goes when they're over.
    private bool Hold(string key, string what, string title)
    {
        if (!alerts.Holds(key, Now))
        {
            held.Remove(what);
            return false;
        }
        if (held.Add(what))
        {
            alerts.Held(key, title, alerts.Rule(key).To);
        }
        return true;
    }

    private async Task Seats(CancellationToken stop)
    {
        var rule = alerts.Rule("seat.offline");
        var seats = clients.All().Where(c => c.Approved is not null).ToList();
        foreach (var gone in quietSeats.Keys.Where(id => seats.All(c => c.Id != id)).ToList())
        {
            quietSeats.Remove(gone);
        }
        if (!rule.On || rule.To.Count == 0)
        {
            quietSeats.Clear();
            return;
        }
        foreach (var c in seats)
        {
            var quiet = (Now - c.LastSeen).TotalMinutes >= rule.Limit;
            if (quiet && !quietSeats.ContainsKey(c.Id))
            {
                var title = $"{c.Computer} stopped checking in";
                if (Hold("seat.offline", "seat:" + c.Id, title))
                {
                    continue;
                }
                quietSeats[c.Id] = Now;
                await alerts.Send("seat.offline", new MailNote(title, "down", [("Computer", c.Computer), ("Last checked in", c.LastSeen.ToString("u"))],
                    new AlertCard("down", title, "Its client last checked in at {when}.",
                        [new("🔴 What happened", [$"Quiet for {Math.Round((Now - c.LastSeen).TotalMinutes)} minutes", c.User is { } who ? $"{who} was signed in" : "Nobody was signed in"]),
                            new("📍 Where", [c.Address ?? "Address unknown", c.Os ?? "Windows"])], Product.Lab, c.LastSeen)), rule.To, stop);
            }
            else if (!quiet && quietSeats.Remove(c.Id) && rule.Back)
            {
                var title = $"{c.Computer} is checking in again";
                await alerts.Send("seat.offline", new MailNote(title, "up", [("Computer", c.Computer)],
                    new AlertCard("up", title, "Its client checked in at {when}.", [new("🟢 Now", ["Checking in", c.Address ?? "Address unknown"])], Product.Lab, c.LastSeen)), rule.To, stop);
            }
        }
    }

    private async Task Update(CancellationToken stop)
    {
        var s = updates.State;
        if (s.Status is RabbidLab.Updater.UpdateStatus.Available or RabbidLab.Updater.UpdateStatus.Waiting && s.Available is { } ready
            && alerts.Rule("update.ready") is { On: true, To.Count: > 0 } r && alerts.NotTold("ready", ready))
        {
            var title = $"{Product.Title} {ready} is ready";
            if (!Hold("update.ready", "ready:" + ready, title))
            {
                alerts.Told("ready", ready);
                await alerts.Send("update.ready", new MailNote(title, "note", [("Now", s.Version), ("Ready", ready)],
                    new AlertCard("up", title, $"You have {s.Version}. Install it from Settings, Updates.", [new("🆕 What's new", [$"Version {ready}", "What's new in the console has the notes"])], Product.Lab)), r.To, stop);
            }
        }
        if (s.Status == RabbidLab.Updater.UpdateStatus.Failed && alerts.Rule("update.failed") is { On: true, To.Count: > 0 } f && alerts.NotTold("failed", $"{s.Available} {s.Message}"))
        {
            var title = $"{Product.Title} {s.Available} didn't install";
            if (!Hold("update.failed", "failed:" + s.Available, title))
            {
                alerts.Told("failed", $"{s.Available} {s.Message}");
                await alerts.Send("update.failed", new MailNote(title, "down", [("Still on", s.Version), ("What it said", s.Message)],
                    new AlertCard("down", title, $"It's still on {s.Version}.", [new("🔴 What it said", [s.Message])], Product.Lab)), f.To, stop);
            }
        }
    }

    private async Task SignIns(CancellationToken stop)
    {
        while (failures.TryDequeue(out var f))
        {
            recent.Add(f);
        }
        recent.RemoveAll(f => (Now - f.At).TotalMinutes > 10);
        foreach (var gone in toldSignIns.Where(t => (Now - t.Value).TotalMinutes > 10).Select(t => t.Key).ToList())
        {
            toldSignIns.Remove(gone);
        }
        var rule = alerts.Rule("signin.failing");
        if (!rule.On || rule.To.Count == 0)
        {
            return;
        }
        foreach (var group in recent.GroupBy(f => f.Who, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= rule.Limit && !toldSignIns.ContainsKey(g.Key)).ToList())
        {
            var title = $"{group.Count()} failed sign-ins for {group.Key}";
            if (Hold("signin.failing", "signin:" + group.Key, title))
            {
                continue;
            }
            toldSignIns[group.Key] = Now;
            var from = group.Select(f => f.From ?? "an unknown address").Distinct().ToList();
            await alerts.Send("signin.failing", new MailNote(title, "warn", [("Account", group.Key), ("From", string.Join(", ", from))],
                new AlertCard("warn", title, "In the 10 minutes up to {when}.", [new("🔐 From", [.. from.Take(8)])], Product.Lab, Now)), rule.To, stop);
        }
    }
}

public static class AlertsApi
{
    public static void MapAlertsApi(this WebApplication app)
    {
        app.MapGet("/api/alerts", (HttpContext context, Alerts alerts, Sensors sensors) =>
            !Access.Can(context, Permissions.SensorsAlerts) ? Access.NotAllowed() : Results.Ok(new { now = alerts.View(), counts = sensors.RuleCounts(), gather = sensors.Gathering() }));

        app.MapPost("/api/alerts/rules/{key}", (string key, AlertSettingForm form, HttpContext context, Alerts alerts, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            if (alerts.Save(key, form) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "alerts.rule", $"Changed the alert rule {key}");
            return Results.Ok(new { now = alerts.View(), counts = sensors.RuleCounts(), gather = sensors.Gathering() });
        });

        // Alert types: named ways to alert that sensors and watched links pick.
        app.MapPost("/api/alerts/types", (AlertTypeForm form, HttpContext context, Alerts alerts, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var (made, problem) = alerts.SaveType(null, form);
            if (made is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "alerts.type.add", $"Added the alert type {made.Name}");
            return Results.Ok(new { id = made.Id, now = alerts.View(), counts = sensors.RuleCounts(), gather = sensors.Gathering() });
        });

        app.MapPost("/api/alerts/types/{id}", (string id, AlertTypeForm form, HttpContext context, Alerts alerts, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var (saved, problem) = alerts.SaveType(id, form);
            if (saved is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "alerts.type.change", $"Changed the alert type {saved.Name}");
            return Results.Ok(new { id, now = alerts.View(), counts = sensors.RuleCounts(), gather = sensors.Gathering() });
        });

        // Deleting a type moves what used it to the one new sensors start with.
        app.MapPost("/api/alerts/types/{id}/delete", (string id, HttpContext context, Alerts alerts, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var name = alerts.Type(id)?.Name ?? "an alert type";
            var (movedTo, problem) = alerts.DeleteType(id);
            if (movedTo is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            var moved = sensors.Retype(id, movedTo);
            log.Record(context, "sensors", "alerts.type.delete", $"Deleted the alert type {name}; {moved} moved to {alerts.Type(movedTo)?.Name}");
            return Results.Ok(new { moved, now = alerts.View(), counts = sensors.RuleCounts(), gather = sensors.Gathering() });
        });

        app.MapPost("/api/alerts/quiet", (QuietForm form, HttpContext context, Alerts alerts, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            if (alerts.SetQuiet(form) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "alerts.quiet", "Changed the quiet hours");
            return Results.Ok(new { now = alerts.View(), counts = sensors.RuleCounts(), gather = sensors.Gathering() });
        });

        app.MapGet("/api/alerts/history", (int? days, bool? failed, string? find, HttpContext context, Alerts alerts) =>
            !Access.Can(context, Permissions.SensorsAlerts) ? Access.NotAllowed() : Results.Ok(alerts.History(days ?? 7, failed == true, find)));
    }
}
