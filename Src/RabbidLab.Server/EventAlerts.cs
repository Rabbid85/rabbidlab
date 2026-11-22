using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace RabbidLab.Server;

/// <summary>
/// Something that can happen and be alerted on: its area, what it's called, the conditions it takes (count, group, what,
/// computers, after, percent), its usual severity, how Alert me says it about one user (null when it isn't about a user),
/// why it can't be alerted on yet (null when it can), and where it's read from.
/// </summary>
public sealed record AlertEvent(string Id, string Area, string Name, string[] Fields, string Severity, string? User = null, string? NotYet = null, string? Note = null);

/// <summary>Everything that can happen, in the order the Alerts page lists it.</summary>
public static class AlertEvents
{
    private const string Audit = "Read from the AD audit, so changes made with any tool count.";
    private const string Later = "Not built yet.";

    public static readonly AlertEvent[] All =
    [
        new("signin.fail", "Sign-ins", "Sign-ins to Rabbid Lab fail", ["count"], "warning", "Has failed sign-ins to Rabbid Lab"),
        new("signin.lock", "Sign-ins", "An account is locked out of Rabbid Lab", [], "warning", "Is locked out of Rabbid Lab"),
        new("signin.new", "Sign-ins", "Someone signs in from a new computer or address", [], "warning", NotYet: $"Needs sign-in events from the domain controllers and a learning period. {Later}"),
        new("signin.hours", "Sign-ins", "Someone signs in outside working hours", [], "info", NotYet: $"Needs working hours for each person. {Later}"),
        new("signin.many", "Sign-ins", "One account signs in to many computers", [], "warning", NotYet: $"Needs sign-in events from the domain controllers. {Later}"),
        new("signin.spray", "Sign-ins", "Many accounts fail from one address", [], "critical", NotYet: $"Needs sign-in events from the domain controllers. {Later}"),
        new("ad.group.add", "Active Directory", "Someone is added to a group", ["group"], "warning", "Is added to a group", Note: Audit),
        new("ad.group.remove", "Active Directory", "Someone is removed from a group", ["group"], "info", "Is removed from a group", Note: Audit),
        new("ad.password", "Active Directory", "A password is reset in Rabbid Lab", [], "info", "Has the password reset in Rabbid Lab", Note: "Resets made in Rabbid Lab. Windows doesn't log a reset made anywhere else."),
        new("ad.disable", "Active Directory", "An account is disabled or enabled", [], "info", "Is disabled or enabled", Note: Audit),
        new("ad.delete", "Active Directory", "Something is deleted", ["count", "what"], "warning", "Is deleted", Note: Audit),
        new("ad.create", "Active Directory", "A user, group or computer is created", ["what"], "info", Note: Audit),
        new("ad.gpo", "Active Directory", "A Group Policy object is changed in Rabbid Lab", [], "warning", Note: "Changes made in Rabbid Lab."),
        new("ad.outside", "Active Directory", "Active Directory is changed outside Rabbid Lab", ["what"], "info", Note: "From the AD audit: changes by any account other than the service account."),
        new("dev.offline", "Devices", "A computer stops checking in", ["computers", "after"], "warning"),
        new("dev.disk", "Devices", "A disk is nearly full", ["computers", "percent"], "warning"),
        new("dev.updates", "Devices", "A computer is missing updates", ["computers"], "info"),
        new("dev.admins", "Devices", "Local administrators change on a computer", [], "warning", NotYet: $"Needs the last list kept to compare with. {Later}"),
        new("dev.new", "Devices", "A new computer connects", [], "info"),
        new("dev.uninstall", "Devices", "The client is uninstalled or removed", [], "warning"),
        new("lab.down", "Sensors and links", "A sensor or link goes down", [], "warning", NotYet: "Set on each sensor and link, with its alert type."),
        new("lab.cert", "Sensors and links", "A certificate is about to expire", [], "info", NotYet: "Set in Settings, Alert rules."),
        new("print.stuck", "Printers", "A print queue is stuck", [], "warning", NotYet: Later),
        new("print.toner", "Printers", "A printer is low on toner", [], "info", NotYet: $"Needs toner levels read over SNMP. {Later}"),
        new("apps.hand", "Connected apps", "Someone gets a role by hand in an app", [], "warning", NotYet: "Comes with connected apps."),
        new("apps.sync", "Connected apps", "A connected app stops syncing", [], "warning", NotYet: "Comes with connected apps."),
        new("rl.role", "Rabbid Lab", "A Rabbid Lab role or its members change", [], "critical"),
        new("rl.settings", "Rabbid Lab", "Rabbid Lab settings are changed", [], "info"),
        new("rl.update", "Rabbid Lab", "An update fails to install", [], "warning"),
        new("rl.svc", "Rabbid Lab", "The service account stops working", [], "critical", NotYet: Later),
        new("rl.flow", "Rabbid Lab", "A workflow or offboarding stage fails", [], "warning", "Has a workflow or offboarding stage fail"),
    ];

    public static AlertEvent? Get(string? id) => All.FirstOrDefault(e => e.Id == id);

    /// <summary>How often, by its number: each time, or so many times within so many minutes.</summary>
    public static readonly (int Times, int Minutes, string Words)[] Counts = [(1, 0, "Each time"), (3, 10, "3 times in 10 minutes"), (5, 10, "5 times in 10 minutes"), (10, 60, "10 times in an hour")];
    public static readonly int[] Afters = [5, 15, 30, 60];
    public static readonly int[] Percents = [80, 90, 95];
    public static readonly string[] Whats = ["", "user", "group", "computer", "ou"];
    public static readonly string[] Computers = ["", "servers", "workstations"];
    public static readonly string[] Severities = ["critical", "warning", "info"];
}

/// <summary>An alert someone added: what happens, its conditions, how serious, where it goes, and what else it may do.</summary>
public sealed class EventRule
{
    public string Id { get; set; } = "";
    public string Event { get; set; } = "";

    /// <summary>The number of an entry in AlertEvents.Counts.</summary>
    public int Count { get; set; }

    /// <summary>The group's name, for added to and removed from a group; empty for any group.</summary>
    public string Group { get; set; } = "";

    /// <summary>user, group, computer or ou; empty for anything.</summary>
    public string What { get; set; } = "";

    /// <summary>servers or workstations; empty for any computer.</summary>
    public string Computers { get; set; } = "";

    /// <summary>Minutes without checking in, for a computer that stops checking in.</summary>
    public int After { get; set; } = 15;

    /// <summary>How full a disk is, for a disk that's nearly full.</summary>
    public int Percent { get; set; } = 90;

    public string Severity { get; set; } = "warning";

    /// <summary>The alert types it goes through, by id; their notifications are where it's sent.</summary>
    public List<string> Types { get; set; } = [];

    /// <summary>Sent even in quiet hours.</summary>
    public bool Quiet { get; set; }
    public bool On { get; set; } = true;

    /// <summary>Only about this user (Alert me): their username and display name.</summary>
    public string? About { get; set; }
    public string? AboutName { get; set; }

    /// <summary>A people workflow to run on the user it's about, once someone approves it on the fired alert.</summary>
    public string? Workflow { get; set; }

    /// <summary>Where it was added: new (New alert), list (Everything that can happen) or user (Alert me).</summary>
    public string From { get; set; } = "new";
    public string By { get; set; } = "";
    public DateTime Made { get; set; }
}

public sealed class EventRulesFile
{
    public List<EventRule> Rules { get; set; } = [];
}

/// <summary>An alert that went off: what happened, where it went, whether someone has looked, and a workflow waiting for approval.</summary>
public sealed class FiredAlert
{
    public string Id { get; set; } = "";
    public string Rule { get; set; } = "";
    public string Event { get; set; } = "";
    public DateTime At { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string Severity { get; set; } = "warning";
    public string? Subject { get; set; }
    public string? SubjectName { get; set; }

    /// <summary>new until someone acknowledges it, then ack.</summary>
    public string State { get; set; } = "new";
    public string? AckBy { get; set; }
    public DateTime? AckAt { get; set; }

    /// <summary>sent, partial, failed, held (for quiet hours) or nobody (no notifications on its alert types).</summary>
    public string Sent { get; set; } = "sent";
    public string? Why { get; set; }

    /// <summary>The workflow: waiting, approved or dismissed; who did it, and the run.</summary>
    public string? Approval { get; set; }
    public string? ApprovalBy { get; set; }
    public string? Run { get; set; }
}

public sealed class FiredFile
{
    public List<FiredAlert> Fired { get; set; } = [];
}

/// <summary>What happened, as the detectors hand it over: the event, its words, who or what it was about, and what the conditions look at.</summary>
public sealed record AlertFact(string Event, string Title, string Text, string? Subject = null, string? SubjectName = null, string? Group = null, string? What = null,
    string? Computer = null, string? Os = null);

public sealed record EventRuleForm(string? Event, int? Count, string? Group, string? What, string? Computers, int? After, int? Percent, string? Severity,
    List<string>? Types, bool? Quiet, bool? On, string? About, string? AboutName, string? Workflow, string? From);

public sealed record AckForm(List<string>? Ids);

public sealed record ApprovalForm(string? State, string? Run);

/// <summary>
/// The alerts people add in the Alerts section (alert-rules.json) and every one that went off (alerts-fired.json). What
/// happened comes in from the activity log, the AD audit and a look at the devices every 30 seconds (EventAlertWorker);
/// a rule that matches sends through its alert types' notifications, or holds it for quiet hours.
/// </summary>
public sealed class EventAlerts(DataFolder folder, Alerts alerts, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions Compact = new(JsonSerializerDefaults.Web);
    private readonly JsonFile<EventRulesFile> rules = folder.Json<EventRulesFile>("alert-rules.json", Json);
    private readonly JsonFile<FiredFile> fired = folder.Json<FiredFile>("alerts-fired.json", Compact);
    private readonly ConcurrentDictionary<string, List<DateTime>> counted = new(); // rule|subject: when each happened, for "so many times"
    private readonly ConcurrentDictionary<string, bool> told = new(); // rule|computer(|disk): already alerted, until it's over
    private bool seeded; // the first look at the devices only notes what's already so, so a restart doesn't alert it all again

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Something changed: a rule, or an alert went off or was acknowledged. The page reads again.</summary>
    public event Action? Changed;

    public List<EventRule> Rules()
    {
        lock (rules.Gate)
        {
            return rules.Read().Rules;
        }
    }

    public EventRule? Rule(string id) => Rules().FirstOrDefault(r => r.Id == id);

    public List<FiredAlert> Fired(int max = 300)
    {
        lock (fired.Gate)
        {
            return [.. fired.Read().Fired.OrderByDescending(f => f.At).Take(max)];
        }
    }

    /// <summary>Adds a rule (id null) or changes one, from what the page sent; the rule, or why not.</summary>
    public (EventRule? Rule, string? Problem) Save(string? id, EventRuleForm form, string by, IReadOnlyCollection<string> types, IReadOnlyCollection<string> flows)
    {
        lock (rules.Gate)
        {
            var file = rules.Read();
            var r = id is null ? new EventRule { Id = NewId(), By = by, Made = Now } : file.Rules.FirstOrDefault(x => x.Id == id);
            if (r is null)
            {
                return (null, "Alert not found. It may have been deleted.");
            }
            if (id is null && file.Rules.Count >= 300)
            {
                return (null, $"{Product.Title} supports up to 300 alerts.");
            }
            var ev = AlertEvents.Get(form.Event ?? r.Event);
            if (ev is null || ev.NotYet is not null)
            {
                return (null, ev is null ? "Select what happens." : $"{ev.Name} can't be alerted on yet. {ev.NotYet}");
            }
            if (form.Count is { } c && (c < 0 || c >= AlertEvents.Counts.Length) || form.After is { } a && !AlertEvents.Afters.Contains(a)
                || form.Percent is { } p && !AlertEvents.Percents.Contains(p) || form.What is { } w && !AlertEvents.Whats.Contains(w)
                || form.Computers is { } cs && !AlertEvents.Computers.Contains(cs) || form.Severity is { } s && !AlertEvents.Severities.Contains(s))
            {
                return (null, "Select one of the listed choices.");
            }
            if (form.Group is { Length: > 64 } || form.About is { Length: > 64 } || form.AboutName is { Length: > 128 })
            {
                return (null, "Names can be up to 64 characters.");
            }
            if (form.Workflow is { Length: > 0 } flow && !flows.Contains(flow))
            {
                return (null, "That workflow can't run here. Select one that runs on a user and is enabled.");
            }
            r.Event = ev.Id;
            r.Count = ev.Fields.Contains("count") ? form.Count ?? r.Count : 0;
            r.Group = ev.Fields.Contains("group") ? (form.Group ?? r.Group).Trim() : "";
            r.What = ev.Fields.Contains("what") ? form.What ?? r.What : "";
            r.Computers = ev.Fields.Contains("computers") ? form.Computers ?? r.Computers : "";
            r.After = form.After ?? r.After;
            r.Percent = form.Percent ?? r.Percent;
            r.Severity = form.Severity ?? (id is null ? ev.Severity : r.Severity);
            r.Types = form.Types is { } t ? [.. t.Where(types.Contains).Distinct()] : r.Types;
            r.Quiet = form.Quiet ?? r.Quiet;
            r.On = form.On ?? r.On;
            if (id is null)
            {
                r.About = ev.User is not null && form.About is { Length: > 0 } who ? who.Trim() : null;
                r.AboutName = r.About is null ? null : form.AboutName?.Trim();
                r.From = form.From is "list" or "user" ? form.From : "new";
            }
            r.Workflow = form.Workflow is null ? r.Workflow : form.Workflow.Length == 0 ? null : form.Workflow;
            if (id is null)
            {
                file.Rules.Add(r);
            }
            rules.Save(file);
            Changed?.Invoke();
            return (r, null);
        }
    }

    public bool Delete(string id)
    {
        lock (rules.Gate)
        {
            var file = rules.Read();
            if (file.Rules.RemoveAll(r => r.Id == id) == 0)
            {
                return false;
            }
            rules.Save(file);
        }
        foreach (var k in told.Keys.Where(k => k.StartsWith(id + "|", StringComparison.Ordinal)))
        {
            told.TryRemove(k, out _);
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Acknowledges these fired alerts, or every new one when ids is null; how many.</summary>
    public int Ack(IReadOnlyCollection<string>? ids, string by)
    {
        lock (fired.Gate)
        {
            var file = fired.Read();
            var n = 0;
            foreach (var f in file.Fired.Where(f => f.State == "new" && (ids is null || ids.Contains(f.Id))))
            {
                (f.State, f.AckBy, f.AckAt) = ("ack", by, Now);
                n++;
            }
            if (n > 0)
            {
                fired.Save(file);
                Changed?.Invoke();
            }
            return n;
        }
    }

    /// <summary>Approves (after the page ran the workflow) or dismisses the workflow waiting on a fired alert; why not.</summary>
    public string? Approve(string id, string state, string by, string? run)
    {
        if (state is not ("approved" or "dismissed"))
        {
            return "Select approve or dismiss.";
        }
        lock (fired.Gate)
        {
            var file = fired.Read();
            var f = file.Fired.FirstOrDefault(x => x.Id == id);
            if (f?.Approval != "waiting")
            {
                return f is null ? "Alert not found." : "This alert has no workflow waiting.";
            }
            (f.Approval, f.ApprovalBy, f.Run) = (state, by, run);
            if (f.State == "new")
            {
                (f.State, f.AckBy, f.AckAt) = ("ack", by, Now);
            }
            fired.Save(file);
        }
        Changed?.Invoke();
        return null;
    }

    /// <summary>Whether a rule is waiting for this: what happened, who or what it was about, and its conditions.</summary>
    public static bool Matches(EventRule r, AlertFact f) =>
        r.On && r.Event == f.Event
        && (r.About is null || Same(r.About, f.Subject) || Same(r.AboutName, f.SubjectName) || Same(r.About, f.SubjectName))
        && (r.Group.Length == 0 || Same(r.Group, f.Group))
        && (r.What.Length == 0 || r.What == f.What)
        && (r.Computers.Length == 0 || (r.Computers == "servers") == (f.Os?.Contains("Server", StringComparison.OrdinalIgnoreCase) == true));

    private static bool Same(string? a, string? b) => a is { Length: > 0 } && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Something happened: every rule waiting for it goes off, once its count is reached. How many went off.</summary>
    public async Task<int> Handle(AlertFact fact, CancellationToken stop)
    {
        var n = 0;
        foreach (var r in Rules().Where(r => Matches(r, fact)))
        {
            var (times, minutes, _) = AlertEvents.Counts[Math.Clamp(r.Count, 0, AlertEvents.Counts.Length - 1)];
            if (times > 1)
            {
                var seen = counted.GetOrAdd($"{r.Id}|{fact.Subject ?? fact.SubjectName}", _ => []);
                lock (seen)
                {
                    seen.Add(Now);
                    seen.RemoveAll(t => (Now - t).TotalMinutes > minutes);
                    if (seen.Count < times)
                    {
                        continue;
                    }
                    seen.Clear();
                }
            }
            await Fire(r, times > 1 ? fact with { Text = $"{AlertEvents.Counts[r.Count].Words}. {fact.Text}" } : fact, stop);
            n++;
        }
        return n;
    }

    private static string Tone(string severity) => severity switch { "critical" => "down", "info" => "note", _ => "warn" };

    private List<string> Channels(EventRule r) => [.. alerts.Types().Where(t => r.Types.Contains(t.Id)).SelectMany(t => t.To).Distinct()];

    private static MailNote Note(EventRule r, string title, string text, DateTime at)
    {
        var ev = AlertEvents.Get(r.Event);
        var facts = new List<(string, string)> { ("What happened", ev?.Name ?? r.Event), ("Severity", Word(r.Severity)) };
        if ((r.AboutName ?? r.About) is { } about)
        {
            facts.Add(("About", about));
        }
        return new MailNote(title, Tone(r.Severity), facts, new AlertCard(Tone(r.Severity), title, text, [new("🔔 Alert", [ev?.Name ?? r.Event, Word(r.Severity)])], Product.Lab, at));
    }

    private static string Word(string severity) => severity.Length == 0 ? severity : char.ToUpperInvariant(severity[0]) + severity[1..];

    /// <summary>A rule went off: it's written down, then sent through its alert types, or held for quiet hours.</summary>
    public async Task<FiredAlert> Fire(EventRule r, AlertFact fact, CancellationToken stop)
    {
        var f = new FiredAlert
        {
            Id = NewId(), Rule = r.Id, Event = r.Event, At = Now, Title = fact.Title, Text = fact.Text, Severity = r.Severity,
            Subject = fact.Subject, SubjectName = fact.SubjectName,
            Approval = r.Workflow is not null && (fact.Subject ?? fact.SubjectName) is not null ? "waiting" : null,
        };
        var to = Channels(r);
        if (to.Count == 0)
        {
            f.Sent = "nobody";
        }
        else if (!r.Quiet && alerts.Quiet(Now))
        {
            f.Sent = "held";
            alerts.Held("alert:" + r.Event, f.Title, to);
        }
        else
        {
            await Send(f, r, to, stop);
        }
        lock (fired.Gate)
        {
            var file = fired.Read();
            file.Fired.Add(f);
            file.Fired.RemoveAll(x => x.At < Now.AddDays(-90));
            if (file.Fired.Count > 1000)
            {
                file.Fired = [.. file.Fired.OrderByDescending(x => x.At).Take(1000)];
            }
            fired.Save(file);
        }
        Changed?.Invoke();
        return f;
    }

    private async Task Send(FiredAlert f, EventRule r, List<string> to, CancellationToken stop)
    {
        var (went, failed) = await alerts.Send("alert:" + r.Event, Note(r, f.Title, f.Text, f.At), to, stop);
        f.Sent = failed.Count == 0 ? "sent" : went.Count == 0 ? "failed" : "partial";
        f.Why = failed.Count == 0 ? null : string.Join(" · ", failed);
    }

    /// <summary>Sends what was held for quiet hours, once they're over.</summary>
    public async Task SendHeld(CancellationToken stop)
    {
        if (alerts.Quiet(Now))
        {
            return;
        }
        List<FiredAlert> held;
        lock (fired.Gate)
        {
            held = [.. fired.Read().Fired.Where(f => f.Sent == "held")];
        }
        if (held.Count == 0)
        {
            return;
        }
        foreach (var f in held)
        {
            if (Rule(f.Rule) is { } r && Channels(r) is { Count: > 0 } to)
            {
                await Send(f, r, to, stop);
            }
            else
            {
                f.Sent = "nobody";
            }
        }
        lock (fired.Gate)
        {
            var file = fired.Read();
            foreach (var f in held)
            {
                if (file.Fired.FirstOrDefault(x => x.Id == f.Id) is { } x)
                {
                    (x.Sent, x.Why) = (f.Sent, f.Why);
                }
            }
            fired.Save(file);
        }
        Changed?.Invoke();
    }

    /// <summary>Sends a test of a rule to its notifications; why it didn't go, or null.</summary>
    public async Task<string?> Test(string id, CancellationToken stop)
    {
        if (Rule(id) is not { } r)
        {
            return "Alert not found. It may have been deleted.";
        }
        var to = Channels(r);
        if (to.Count == 0)
        {
            return "This alert has no notifications to go to. Pick an alert type that has some.";
        }
        var name = AlertEvents.Get(r.Event)?.Name ?? r.Event;
        var (_, failed) = await alerts.Send("alert:test", Note(r, $"Test: {name}", $"This is a test of an alert from {Product.Title}.", Now), to, stop);
        return failed.Count == 0 ? null : string.Join(" · ", failed);
    }

    /// <summary>
    /// A look at the devices: a computer that stopped checking in, a disk that's nearly full, and updates missing, for each
    /// rule; each alerts once, and again only after it was over. An update that didn't install comes from the update engine.
    /// </summary>
    public async Task Poll(IReadOnlyList<Client> clients, Func<string, DeviceInventory?> inventory, CancellationToken stop)
    {
        var seen = new HashSet<string>();
        var first = !seeded;
        seeded = true;
        async Task Once(EventRule r, string key, bool now, AlertFact fact)
        {
            var k = $"{r.Id}|{key}";
            seen.Add(k);
            if (!now)
            {
                told.TryRemove(k, out _);
                return;
            }
            if (told.TryAdd(k, true) && !first && Matches(r, fact))
            {
                await Fire(r, fact, stop);
            }
        }
        foreach (var r in Rules().Where(r => r.On && r.Event is "dev.offline" or "dev.disk" or "dev.updates"))
        {
            foreach (var c in clients.Where(c => c.Approved is not null))
            {
                var name = c.Computer;
                if (r.Event == "dev.offline")
                {
                    var gone = (Now - c.LastSeen).TotalMinutes;
                    await Once(r, c.Id, gone >= r.After, new("dev.offline", $"{name} stopped checking in", $"Its client last checked in {Math.Round(gone)} minutes ago.", Computer: name, Os: c.Os));
                    continue;
                }
                if (inventory(c.Id) is not { } inv)
                {
                    continue;
                }
                if (r.Event == "dev.disk")
                {
                    foreach (var d in inv.Disks.Where(d => d.Total > 0))
                    {
                        var used = (int) Math.Round(100.0 * (d.Total - d.Free) / d.Total);
                        await Once(r, $"{c.Id}|{d.Name}", used >= r.Percent, new("dev.disk", $"{d.Name} on {name} is {used}% full", $"{Gb(d.Free)} free of {Gb(d.Total)}.", Computer: name, Os: c.Os));
                    }
                }
                else if (inv.MissingChecked is not null)
                {
                    var n = inv.Missing.Count(u => !u.Definition);
                    await Once(r, c.Id, n > 0, new("dev.updates", $"{name} is missing {n} update{(n == 1 ? "" : "s")}", string.Join(", ", inv.Missing.Where(u => !u.Definition).Take(5).Select(u => u.Kb)), Computer: name, Os: c.Os));
                }
            }
        }
        foreach (var k in told.Keys.Where(k => !seen.Contains(k)))
        {
            told.TryRemove(k, out _); // a computer or rule that's gone
        }
    }

    private static string Gb(long bytes) => $"{Math.Round(bytes / 1073741824.0)} GB";

    /// <summary>An update that didn't install, as the update engine says it: once per version and message.</summary>
    public async Task UpdateFailed(string version, string message, CancellationToken stop)
    {
        if (!told.TryAdd($"update|{version}|{message}", true))
        {
            return;
        }
        await Handle(new("rl.update", $"{Product.Title} {version} failed to install", message), stop);
    }

    private static string NewId() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    // --- What the activity log and the AD audit say happened -----------------------------------------------------------

    /// <summary>An activity log entry as something an alert can be about, or null when none is.</summary>
    public static AlertFact? FromActivity(ActivityEntry e)
    {
        string? After(string pattern) => Regex.Match(e.Title, pattern) is { Success: true } m ? m.Groups[1].Value : null;
        var by = $"By {e.By}.";
        return e.Action switch
        {
            "signin.failed" when e.By != SignIn.Unknown => new("signin.fail", $"{e.By} failed to sign in to {Product.Title}", $"From {e.From ?? "an unknown address"}.", e.By),
            "signin.locked" when e.By != SignIn.Unknown => new("signin.lock", $"{e.By} is locked out of {Product.Title}", $"Too many failed sign-ins, the last from {e.From ?? "an unknown address"}. Unlock it in Settings, Sign-in.", e.By),
            "ad.password" => new("ad.password", e.Title, by, After(@" for (\S+)")),
            "role.add" or "role.change" or "role.delete" or "role.member.add" or "role.member.remove" => new("rl.role", e.Title, by),
            "settings.idle" or "settings.lock" or "update.settings" or "ad.settings" or "client.settings" or "ad.domain.set" or "account.add-admin" or "ad.readonly.off" => new("rl.settings", e.Title, by),
            "workflow.failed" => new("rl.flow", e.Title, e.Detail ?? ""),
            "ad.workflow.run" or "ad.offboard.stage" when e.Result is "failed" or "warning" =>
                new("rl.flow", $"{e.Title} stopped at a failed step", e.Detail ?? by, After(@" for (\S+)$"), After(@" for (.+)$")),
            "client.join" => new("dev.new", e.Title, e.From is { } ip ? $"From {ip}." : "", Computer: After(@"^(\S+) connected")),
            "client.uninstall" or "client.remove" => new("dev.uninstall", e.Title, by),
            "gpo.change" or "gpo.new" or "gpo.delete" or "gpo.link" or "gpo.rename" or "gpo.restore" or "gpo.block" or "gpo.apply" or "gpo.wmifilter" when e.Result != "failed" => new("ad.gpo", e.Title, by),
            _ => null,
        };
    }

    private static readonly Dictionary<string, string> KindWord = new() { ["user"] = "user", ["group"] = "group", ["computer"] = "computer", ["ou"] = "OU" };

    /// <summary>Directory changes from the AD audit as things alerts can be about: group members, deletes, new objects, accounts turned off or on, and changes made outside Rabbid Lab.</summary>
    public static List<AlertFact> FromAudit(IReadOnlyList<AuditEntry> entries, string? service)
    {
        var list = new List<AlertFact>();
        var outside = new HashSet<string>();
        foreach (var e in entries)
        {
            var name = Ldap.Name(e.Dn);
            var kind = KindWord.GetValueOrDefault(e.Kind, e.Kind);
            var account = e.Account is { } a ? Ldap.Account(a) : null;
            var ours = service is not null && account is not null && account.Equals(service, StringComparison.OrdinalIgnoreCase);
            var by = ours ? $"By {Product.Title}, on {e.Dc}." : $"By {e.ByName ?? account ?? "an unknown account"}, on {e.Dc}.";
            if (e.Attribute.Equals("member", StringComparison.OrdinalIgnoreCase) && e.Kind == "group")
            {
                list.AddRange(e.New.Select(m => new AlertFact("ad.group.add", $"{Ldap.Name(m)} was added to {name}", by, SubjectName: Ldap.Name(m), Group: name, What: "group")));
                list.AddRange(e.Old.Select(m => new AlertFact("ad.group.remove", $"{Ldap.Name(m)} was removed from {name}", by, SubjectName: Ldap.Name(m), Group: name, What: "group")));
            }
            else if (e.What == "deleted")
            {
                list.Add(new("ad.delete", $"The {kind} {name} was deleted", by, SubjectName: name, What: e.Kind));
            }
            else if (e.What == "made")
            {
                list.Add(new("ad.create", $"The {kind} {name} was created", by, SubjectName: name, What: e.Kind));
            }
            else if (e.Attribute.Equals("userAccountControl", StringComparison.OrdinalIgnoreCase) && Off(e.Old) is { } was && Off(e.New) is { } now && was != now)
            {
                list.Add(new("ad.disable", $"The {kind} {name} was {(now ? "disabled" : "enabled")}", by, SubjectName: name, What: e.Kind));
            }
            if (!ours && account is not null && outside.Add(e.Op))
            {
                list.Add(new("ad.outside", $"{e.ByName ?? account} {e.What} the {kind} {name}", $"Outside {Product.Title}, on {e.Dc}.", SubjectName: name, What: e.Kind));
            }
        }
        return list;
    }

    // Whether userAccountControl says the account is off (bit 2), from the value written down; null when there's no number.
    private static bool? Off(List<string> values) => values.Count > 0 && int.TryParse(values[0], out var flags) ? (flags & 2) != 0 : null;
}

/// <summary>Feeds the alerts what happens: the activity log and the AD audit as they come, and the devices and updates every 30 seconds.</summary>
public sealed class EventAlertWorker(EventAlerts alerts, ActivityLog activity, AuditCollector audit, IAuditDirectory ad, Clients clients, Inventories inventories,
    RabbidLab.Updater.UpdateEngine updates, ILogger<EventAlertWorker> log) : BackgroundService
{
    private readonly Channel<AlertFact> queue = System.Threading.Channels.Channel.CreateUnbounded<AlertFact>();

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        activity.Recorded += e =>
        {
            if (EventAlerts.FromActivity(e) is { } f)
            {
                queue.Writer.TryWrite(f);
            }
        };
        audit.Seen += events =>
        {
            foreach (var f in EventAlerts.FromAudit(AuditEntries.From(events), ad.ServiceAccount))
            {
                queue.Writer.TryWrite(f);
            }
        };
        while (!stop.IsCancellationRequested)
        {
            using var turn = CancellationTokenSource.CreateLinkedTokenSource(stop);
            turn.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                while (await queue.Reader.WaitToReadAsync(turn.Token))
                {
                    while (queue.Reader.TryRead(out var f))
                    {
                        await Safely(() => alerts.Handle(f, stop));
                    }
                }
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested)
            {
                // The 30 seconds are up: the devices next.
            }
            await Safely(() => alerts.Poll(clients.All(), inventories.Get, stop));
            if (updates.State is { Status: RabbidLab.Updater.UpdateStatus.Failed, Available: { } version } s)
            {
                await Safely(() => alerts.UpdateFailed(version, s.Message ?? "", stop));
            }
            await Safely(() => alerts.SendHeld(stop));
        }
    }

    private async Task Safely(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "An alert could not be checked or sent");
        }
    }
}

public static class EventAlertsApi
{
    public static void AddEventAlerts(this IServiceCollection services)
    {
        services.AddSingleton<EventAlerts>();
        services.AddHostedService<EventAlertWorker>();
    }

    // The workflows an alert can run: enabled, and run on a user rather than making one.
    private static List<PeopleFlow> Runnable(PeopleWorkflows flows) => [.. flows.All().Where(f => f.On && f.Steps.FirstOrDefault()?.Kind != "create")];

    private static object View(EventAlerts ea, Alerts alerts, PeopleWorkflows flows) => new
    {
        events = AlertEvents.All.Select(e => new { e.Id, e.Area, e.Name, e.Fields, e.Severity, e.User, e.NotYet, e.Note }),
        counts = AlertEvents.Counts.Select(c => c.Words),
        rules = ea.Rules(),
        fired = ea.Fired(),
        types = alerts.Types().Select(t => new { t.Id, t.Name, to = t.To.Count }),
        start = alerts.StartType().Id, // what a new alert goes through, as new sensors do
        quiet = alerts.Quiet(DateTime.UtcNow),
        workflows = Runnable(flows).Select(f => new { f.Id, f.Name }),
    };

    public static void MapEventAlertsApi(this WebApplication app)
    {
        app.MapGet("/api/event-alerts", (HttpContext context, EventAlerts ea, Alerts alerts, PeopleWorkflows flows) =>
            !Access.Can(context, Permissions.SensorsAlerts) ? Access.NotAllowed() : Results.Ok(View(ea, alerts, flows)));

        IResult Saved(HttpContext context, string? id, EventRuleForm form, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, ActivityLog log)
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var (rule, problem) = ea.Save(id, form, ActivityLog.Who(context), [.. alerts.Types().Select(t => t.Id)], [.. Runnable(flows).Select(f => f.Id)]);
            if (rule is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            var name = AlertEvents.Get(rule.Event)?.Name ?? rule.Event;
            log.Record(context, "sensors", id is null ? "alerts.event.add" : "alerts.event.change", $"{(id is null ? "Added" : "Changed")} the alert {name}{((rule.AboutName ?? rule.About) is { } who ? $" about {who}" : "")}");
            return Results.Ok(new { id = rule.Id, now = View(ea, alerts, flows) });
        }
        app.MapPost("/api/event-alerts/rules", (EventRuleForm form, HttpContext context, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, ActivityLog log) =>
            Saved(context, null, form, ea, alerts, flows, log));
        app.MapPost("/api/event-alerts/rules/{id}", (string id, EventRuleForm form, HttpContext context, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, ActivityLog log) =>
            Saved(context, id, form, ea, alerts, flows, log));

        app.MapPost("/api/event-alerts/rules/{id}/delete", (string id, HttpContext context, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var name = ea.Rule(id) is { } r ? AlertEvents.Get(r.Event)?.Name ?? r.Event : null;
            if (name is null || !ea.Delete(id))
            {
                return Results.NotFound(new { error = "Alert not found. It may have been deleted." });
            }
            log.Record(context, "sensors", "alerts.event.delete", $"Deleted the alert {name}");
            return Results.Ok(new { now = View(ea, alerts, flows) });
        });

        app.MapPost("/api/event-alerts/rules/{id}/test", async (string id, HttpContext context, EventAlerts ea, CancellationToken stop) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            return await ea.Test(id, stop) is { } problem ? Results.BadRequest(new { error = problem }) : Results.Ok(new { sent = true });
        });

        app.MapPost("/api/event-alerts/ack", (AckForm form, HttpContext context, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var n = ea.Ack(form.Ids, ActivityLog.Who(context));
            if (n > 0)
            {
                log.Record(context, "sensors", "alerts.ack", $"Acknowledged {n} alert{(n == 1 ? "" : "s")}");
            }
            return Results.Ok(new { acknowledged = n, now = View(ea, alerts, flows) });
        });

        app.MapPost("/api/event-alerts/fired/{id}/approval", (string id, ApprovalForm form, HttpContext context, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            if (ea.Approve(id, form.State ?? "", ActivityLog.Who(context), form.Run) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "alerts.approval", form.State == "approved" ? "Approved the workflow on an alert" : "Dismissed the workflow on an alert");
            return Results.Ok(new { now = View(ea, alerts, flows) });
        });
    }
}
