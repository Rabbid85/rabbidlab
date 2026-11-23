using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace RabbidLab.Server;

/// <summary>
/// Something that can happen and be alerted on: its area and name, the fixed settings it takes (computers, after, percent),
/// the fields its conditions can test, its usual severity, how Alert me says it about one user (null when it isn't about a
/// user), why it can't be alerted on yet (null when it can), where it's read from, the happening it counts (Of, when it's
/// a way of counting another) with a flag it needs (new, hours), and how it counts to start with.
/// </summary>
public sealed record AlertEvent(string Id, string Area, string Name, string[] Fixed, string[] Fields, string Severity, string? User = null, string? NotYet = null,
    string? Note = null, string? Of = null, string? Flag = null, int Times = 1, int Minutes = 10, string Per = "account", string Distinct = "");

/// <summary>Everything that can happen, in the order the Alerts page lists it.</summary>
public static class AlertEvents
{
    private const string Logs = "From the domain controllers' Security logs.";
    private const string Audit = "From the AD audit, so changes made with any tool count.";
    private const string Later = "Not built yet.";
    private static readonly string[] SignIn = ["account", "address", "computer", "reason", "hour", "day"];
    private static readonly string[] Change = ["object", "kind", "by", "hour", "day"];
    private static readonly string[] Device = ["computer", "hour", "day"];
    private static readonly string[] Lab = ["account", "hour", "day"];

    public static readonly AlertEvent[] All =
    [
        new("ad.signin.fail", "Domain sign-ins", "A domain sign-in fails", [], SignIn, "warning", "Has failed sign-ins", Note: $"{Logs} Wrong passwords and unknown accounts, over Kerberos and NTLM."),
        new("ad.spray", "Domain sign-ins", "Many accounts fail from one address", [], SignIn, "critical", Note: $"{Logs} Password spraying: one address trying many accounts.", Of: "ad.signin.fail", Times: 10, Minutes: 10, Per: "address", Distinct: "account"),
        new("ad.lockout", "Domain sign-ins", "An account is locked out", [], ["account", "computer", "hour", "day"], "warning", "Is locked out", Note: Logs),
        new("ad.signin.disabled", "Domain sign-ins", "Someone signs in with a disabled or expired account", [], SignIn, "warning", "Tries to sign in while disabled or expired", Note: Logs),
        new("ad.signin.many", "Domain sign-ins", "One account signs in on many computers", [], SignIn, "warning", "Signs in on many computers", Note: Logs, Of: "ad.signin", Times: 5, Minutes: 60, Distinct: "computer"),
        new("ad.signin.new", "Domain sign-ins", "Someone signs in on a computer they don't usually use", [], SignIn, "warning", "Signs in on a computer they don't usually use", Note: $"{Logs} Uses what was learned of each account.", Of: "ad.signin", Flag: "new"),
        new("ad.signin.hours", "Domain sign-ins", "Someone signs in outside their usual hours", [], SignIn, "info", "Signs in outside their usual hours", Note: $"{Logs} Uses what was learned of each account.", Of: "ad.signin", Flag: "hours"),
        new("ad.group.add", "Active Directory", "Someone is added to a group", [], [.. Change, "group"], "warning", "Is added to a group", Note: Audit),
        new("ad.group.remove", "Active Directory", "Someone is removed from a group", [], [.. Change, "group"], "info", "Is removed from a group", Note: Audit),
        new("ad.password", "Active Directory", "A password is reset in Rabbid Lab", [], ["object", "by", "hour", "day"], "info", "Has the password reset in Rabbid Lab", Note: "Resets made in Rabbid Lab. Windows doesn't log a reset made anywhere else."),
        new("ad.disable", "Active Directory", "An account is disabled or enabled", [], Change, "info", "Is disabled or enabled", Note: Audit),
        new("ad.delete", "Active Directory", "Something is deleted", [], Change, "warning", "Is deleted", Note: Audit, Per: "all"),
        new("ad.create", "Active Directory", "A user, group or computer is created", [], Change, "info", Note: Audit, Per: "all"),
        new("ad.gpo", "Active Directory", "A Group Policy object is changed in Rabbid Lab", [], ["by", "hour", "day"], "warning", Note: "Changes made in Rabbid Lab.", Per: "all"),
        new("ad.outside", "Active Directory", "Active Directory is changed outside Rabbid Lab", [], Change, "info", Note: "From the AD audit: changes by any account other than the service account.", Per: "all"),
        new("dev.offline", "Devices", "A computer stops checking in", ["computers", "after"], Device, "warning", Per: "computer"),
        new("dev.disk", "Devices", "A disk is nearly full", ["computers", "percent"], Device, "warning", Per: "computer"),
        new("dev.updates", "Devices", "A computer is missing updates", ["computers"], Device, "info", Per: "computer"),
        new("dev.admins", "Devices", "Local administrators change on a computer", [], Device, "warning", NotYet: $"Needs the last list kept to compare with. {Later}"),
        new("dev.new", "Devices", "A new computer connects", [], Device, "info", Per: "computer"),
        new("dev.uninstall", "Devices", "The client is uninstalled or removed", [], ["by", "hour", "day"], "warning", Per: "all"),
        new("net.new", "Network", "A new device joins the network", [], ["computer", "address", "hour", "day"], "info", Note: "From the network scan, after its first scan.", Per: "all"),
        new("lab.down", "Sensors and links", "A sensor or link goes down", [], [], "warning", NotYet: "Set on each sensor and link, with its alert type."),
        new("lab.cert", "Sensors and links", "A certificate is about to expire", [], [], "info", NotYet: "Set in Settings, Alert rules."),
        new("print.stuck", "Printers", "A print queue is stuck", [], [], "warning", NotYet: Later),
        new("print.toner", "Printers", "A printer is low on toner", [], [], "info", NotYet: $"Needs toner levels read over SNMP. {Later}"),
        new("apps.hand", "Connected apps", "Someone gets a role by hand in an app", [], [], "warning", Note: "A group or role an access rule gives, held by someone who is in none of the rule's Active Directory groups. Found at each sync.", Per: "all"),
        new("apps.sync", "Connected apps", "A connected app stops syncing", [], [], "warning", Note: "Each app is read every 15 minutes.", Per: "all"),
        new("signin.fail", "Rabbid Lab", "Sign-ins to Rabbid Lab fail", [], Lab, "warning", "Has failed sign-ins to Rabbid Lab"),
        new("signin.lock", "Rabbid Lab", "An account is locked out of Rabbid Lab", [], Lab, "warning", "Is locked out of Rabbid Lab"),
        new("rl.role", "Rabbid Lab", "A Rabbid Lab role or its members change", [], ["by", "hour", "day"], "critical", Per: "all"),
        new("rl.settings", "Rabbid Lab", "Rabbid Lab settings are changed", [], ["by", "hour", "day"], "info", Per: "all"),
        new("rl.update", "Rabbid Lab", "An update fails to install", [], [], "warning", Per: "all"),
        new("rl.svc", "Rabbid Lab", "The service account stops working", [], [], "critical", Note: "The server tests the Active Directory service account every 15 minutes.", Per: "all"),
        new("rl.flow", "Rabbid Lab", "A workflow or offboarding stage fails", [], ["object", "hour", "day"], "warning", "Has a workflow or offboarding stage fail", Per: "all"),
    ];

    public static AlertEvent? Get(string? id) => All.FirstOrDefault(e => e.Id == id);

    public static readonly int[] Afters = [5, 15, 30, 60];
    public static readonly int[] Percents = [80, 90, 95];
    public static readonly string[] Computers = ["", "servers", "workstations"];
    public static readonly string[] Severities = ["critical", "warning", "info"];
    public static readonly string[] Pers = ["account", "address", "computer", "group", "all"];
    public static readonly string[] Distincts = ["", "account", "address", "computer"];
    public static readonly string[] Ops = ["is", "isnot", "oneof", "noneof", "contains", "starts", "between"];

    public static readonly string[] Privileged = ["Domain Admins", "Enterprise Admins", "Schema Admins", "Administrators", "Account Operators", "Backup Operators"];

    /// <summary>The alert for a service account that stops working, which Settings, Active Directory opens.</summary>
    public const string ServiceAccountRule = "p-svc";

    /// <summary>The alerts there are to start with (the AD security alerts), each one changed or deleted like any other.</summary>
    public static List<EventRule> Presets(string? type, DateTime now)
    {
        EventRule P(string id, string name, string ev, Action<EventRule>? set = null)
        {
            var e = Get(ev)!;
            var r = new EventRule { Id = id, Name = name, Event = ev, Severity = e.Severity, Times = e.Times, Minutes = e.Minutes, Per = e.Per, Distinct = e.Distinct,
                Types = type is null ? [] : [type], From = "builtin", By = Product.Title, Made = now };
            set?.Invoke(r);
            return r;
        }
        return
        [
            P("p-fail", "Too many failed sign-ins", "ad.signin.fail", r => (r.Times, r.Minutes) = (5, 10)),
            P("p-lock", "Account locked out", "ad.lockout"),
            P("p-spray", "Password spraying", "ad.spray"),
            P("p-disabled", "Disabled or expired account", "ad.signin.disabled"),
            P("p-many", "Sign-ins to many computers", "ad.signin.many"),
            P("p-new", "Sign-in to an unusual computer", "ad.signin.new"),
            P("p-hours", "Sign-in outside usual hours", "ad.signin.hours"),
            P("p-delete", "Objects deleted", "ad.delete"),
            P("p-admins", "Added to a privileged group", "ad.group.add", r => { r.Severity = "critical"; r.Conditions = [new() { Field = "group", Op = "oneof", Value = string.Join(", ", Privileged) }]; }),
            P(ServiceAccountRule, "Service account stopped working", "rl.svc"),
        ];
    }
}

/// <summary>One condition of a rule: a field of what happened, how it's compared, and with what.</summary>
public sealed class RuleCondition
{
    public string Field { get; set; } = "";
    public string Op { get; set; } = "is";
    public string Value { get; set; } = "";
}

/// <summary>An alert: what happens, its conditions and how often, how serious, where it goes, and what else it may do.</summary>
public sealed class EventRule
{
    public string Id { get; set; } = "";
    public string Event { get; set; } = "";

    /// <summary>Its own name, such as Password spraying; empty for the name of what happens.</summary>
    public string Name { get; set; } = "";

    /// <summary>How many times (or different accounts, addresses or computers) within Minutes; 1 for each time.</summary>
    public int Times { get; set; } = 1;
    public int Minutes { get; set; } = 10;

    /// <summary>Counted for each account, address, computer or group, or for all of them together.</summary>
    public string Per { get; set; } = "account";

    /// <summary>Counting different accounts, addresses or computers rather than times; empty for times.</summary>
    public string Distinct { get; set; } = "";

    public List<RuleCondition> Conditions { get; set; } = [];

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

    /// <summary>Where it came from: builtin (there to start with), new (New alert), list (Everything that can happen) or user (Alert me).</summary>
    public string From { get; set; } = "new";
    public string By { get; set; } = "";
    public DateTime Made { get; set; }
}

public sealed class EventRulesFile
{
    public List<EventRule> Rules { get; set; } = [];

    /// <summary>The alerts there are to start with were added, once.</summary>
    public bool Seeded { get; set; }

    /// <summary>The service account alert was added, once, for servers seeded before there was one.</summary>
    public bool ServiceAccount { get; set; }
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

/// <summary>
/// What happened, as the detectors hand it over: the happening, its words, the account or object it was about, and what
/// conditions look at (group, kind of object, computer and its Windows, address, why it failed, who made it), when it
/// happened, and flags from what was learned (new, hours).
/// </summary>
public sealed record AlertFact(string Event, string Title, string Text, string? Subject = null, string? SubjectName = null, string? Group = null, string? What = null,
    string? Computer = null, string? Os = null, string? Address = null, string? Reason = null, string? By = null, DateTime? At = null, string? Flags = null);

public sealed record EventRuleForm(string? Event = null, string? Name = null, int? Times = null, int? Minutes = null, string? Per = null, string? Distinct = null,
    List<RuleCondition>? Conditions = null, string? Computers = null, int? After = null, int? Percent = null, string? Severity = null, List<string>? Types = null,
    bool? Quiet = null, bool? On = null, string? About = null, string? AboutName = null, string? Workflow = null, string? From = null);

public sealed record AckForm(List<string>? Ids);

public sealed record ApprovalForm(string? State, string? Run);

public sealed record LearningForm(int? Days);

/// <summary>
/// The alerts (alert-rules.json) and every one that went off (alerts-fired.json). What happened comes in from the activity
/// log, the domain controllers' Security logs (directory changes and sign-ins), and a look at the devices every 30 seconds
/// (EventAlertWorker). A rule that matches sends through its alert types' notifications, or holds it for quiet hours.
/// Only what happened in the last 15 minutes can set one off: older events, as the first read of a log brings, only teach.
/// </summary>
public sealed class EventAlerts(DataFolder folder, Alerts alerts, TimeProvider clock)
{
    public static readonly TimeSpan Recent = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions Compact = new(JsonSerializerDefaults.Web);
    private readonly JsonFile<EventRulesFile> rules = folder.Json<EventRulesFile>("alert-rules.json", Json);
    private readonly JsonFile<FiredFile> fired = folder.Json<FiredFile>("alerts-fired.json", Compact);
    private readonly ConcurrentDictionary<string, List<(DateTime At, string Value)>> counted = new(); // rule|per: what was seen, for "so many in so many minutes"
    private readonly ConcurrentDictionary<string, bool> told = new(); // rule|computer(|disk): already alerted, until it's over
    private bool seeded; // the first look at the devices only notes what's already so, so a restart doesn't alert it all again

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Something changed: a rule, or an alert went off or was acknowledged. The page reads again.</summary>
    public event Action? Changed;

    /// <summary>The rules; the first time, with the AD security alerts added to start with.</summary>
    public List<EventRule> Rules()
    {
        lock (rules.Gate)
        {
            var file = rules.Read();
            if (!file.Seeded)
            {
                file.Rules.InsertRange(0, AlertEvents.Presets(alerts.StartType().Id, Now));
                file.Seeded = file.ServiceAccount = true;
                rules.Save(file);
            }
            else if (!file.ServiceAccount)
            {
                if (!file.Rules.Any(r => r.Event == "rl.svc"))
                {
                    file.Rules.Add(AlertEvents.Presets(alerts.StartType().Id, Now).Single(r => r.Id == AlertEvents.ServiceAccountRule));
                }
                file.ServiceAccount = true;
                rules.Save(file);
            }
            return file.Rules;
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
        Rules(); // seeded first, so the starting alerts come before a new one
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
            if (form.Times is < 1 or > 1000 || form.Minutes is < 1 or > 1440)
            {
                return (null, "Enter how many times, from 1 to 1,000, within 1 to 1,440 minutes.");
            }
            if (form.After is { } a && !AlertEvents.Afters.Contains(a) || form.Percent is { } p && !AlertEvents.Percents.Contains(p)
                || form.Computers is { } cs && !AlertEvents.Computers.Contains(cs) || form.Severity is { } s && !AlertEvents.Severities.Contains(s)
                || form.Per is { } per && !AlertEvents.Pers.Contains(per) || form.Distinct is { } d && !AlertEvents.Distincts.Contains(d))
            {
                return (null, "Select one of the listed choices.");
            }
            if (form.Name is { Length: > 60 } || form.About is { Length: > 64 } || form.AboutName is { Length: > 128 })
            {
                return (null, "Names can be up to 60 characters.");
            }
            if (form.Conditions is { } cs2)
            {
                if (cs2.Count > 12)
                {
                    return (null, "An alert can have up to 12 conditions.");
                }
                if (cs2.FirstOrDefault(c => !ev.Fields.Contains(c.Field) || !AlertEvents.Ops.Contains(c.Op) || c.Value is null || c.Value.Length > 300) is { } bad)
                {
                    return (null, !ev.Fields.Contains(bad.Field) ? $"{ev.Name} has no {bad.Field} to test." : "Select how to compare, and enter up to 300 characters.");
                }
                if (cs2.FirstOrDefault(c => c.Value.Trim().Length == 0) is not null)
                {
                    return (null, "Enter a value for each condition, or remove it.");
                }
                if (cs2.FirstOrDefault(c => c.Op == "between" && Hours(c.Value) is null) is not null)
                {
                    return (null, "Enter hours as a range, such as 9-17.");
                }
            }
            if (form.Workflow is { Length: > 0 } flow && !flows.Contains(flow))
            {
                return (null, "That workflow can't run here. Select one that runs on a user and is enabled.");
            }
            var fresh = r.Event != ev.Id;
            r.Event = ev.Id;
            r.Name = form.Name is { } name ? name.Trim() : r.Name;
            r.Times = form.Times ?? (id is null || fresh ? ev.Times : r.Times);
            r.Minutes = form.Minutes ?? (id is null || fresh ? ev.Minutes : r.Minutes);
            r.Per = form.Per ?? (id is null || fresh ? ev.Per : r.Per);
            r.Distinct = form.Distinct ?? (id is null || fresh ? ev.Distinct : r.Distinct);
            r.Conditions = form.Conditions is { } c ? [.. c.Select(x => new RuleCondition { Field = x.Field, Op = x.Op, Value = x.Value.Trim() })] : fresh ? [] : r.Conditions;
            r.Computers = ev.Fixed.Contains("computers") ? form.Computers ?? r.Computers : "";
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
            foreach (var k in counted.Keys.Where(k => k.StartsWith(r.Id + "|", StringComparison.Ordinal)))
            {
                counted.TryRemove(k, out _); // counted afresh with the new numbers
            }
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
        foreach (var k in told.Keys.Concat(counted.Keys).Where(k => k.StartsWith(id + "|", StringComparison.Ordinal)))
        {
            told.TryRemove(k, out _);
            counted.TryRemove(k, out _);
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

    // --- Matching: what happened, who or what it was about, the fixed settings and the conditions --------------------

    /// <summary>Whether a rule is waiting for this.</summary>
    public static bool Matches(EventRule r, AlertFact f)
    {
        var ev = AlertEvents.Get(r.Event);
        return r.On && ev is not null && (ev.Of ?? ev.Id) == f.Event && (ev.Flag is null || f.Flags?.Split(',').Contains(ev.Flag) == true)
            && (r.About is null || Same(r.About, f.Subject) || Same(r.AboutName, f.SubjectName) || Same(r.About, f.SubjectName))
            && (r.Computers.Length == 0 || (r.Computers == "servers") == (f.Os?.Contains("Server", StringComparison.OrdinalIgnoreCase) == true))
            && r.Conditions.All(c => Test(c, f));
    }

    private static bool Same(string? a, string? b) => a is { Length: > 0 } && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>A field of what happened, as conditions read it: hour and day by this server's clock.</summary>
    public static string? Value(string field, AlertFact f)
    {
        var local = f.At is { } at ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(at, DateTimeKind.Utc), TimeZoneInfo.Local) : (DateTime?) null;
        return field switch
        {
            "account" => f.Subject ?? f.SubjectName,
            "address" => f.Address,
            "computer" => f.Computer,
            "group" => f.Group,
            "object" => f.SubjectName ?? f.Subject,
            "kind" => f.What,
            "by" => f.By,
            "reason" => f.Reason,
            "hour" => local?.Hour.ToString(CultureInfo.InvariantCulture),
            "day" => local?.DayOfWeek.ToString(),
            _ => null,
        };
    }

    /// <summary>9-17 as hours from 9 up to 17, or null when it isn't a range of hours.</summary>
    public static (int From, int To)? Hours(string text) =>
        Regex.Match(text, @"^\s*(\d{1,2})\s*-\s*(\d{1,2})\s*$") is { Success: true } m && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var a and <= 24
            && int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) is var b and <= 24 ? (a, b) : null;

    public static bool Test(RuleCondition c, AlertFact f)
    {
        var v = Value(c.Field, f) ?? "";
        var want = c.Value.Trim();
        var many = want.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        bool Eq(string x) => string.Equals(v, x, StringComparison.OrdinalIgnoreCase) || (c.Field == "day" && v.StartsWith(x, StringComparison.OrdinalIgnoreCase) && x.Length >= 3);
        return c.Op switch
        {
            "is" => Eq(want),
            "isnot" => !Eq(want),
            "oneof" => many.Any(Eq),
            "noneof" => !many.Any(Eq),
            "contains" => v.Contains(want, StringComparison.OrdinalIgnoreCase),
            "starts" => v.StartsWith(want, StringComparison.OrdinalIgnoreCase),
            "between" => Hours(want) is { } h && int.TryParse(v, out var hour) && (h.From <= h.To ? hour >= h.From && hour < h.To : hour >= h.From || hour < h.To),
            _ => true,
        };
    }

    // What "for each" and "different" count by.
    private static string Of(string by, AlertFact f) => by switch
    {
        "account" => (f.Subject ?? f.SubjectName ?? "").ToLowerInvariant(),
        "address" => f.Address ?? "",
        "computer" => (f.Computer ?? f.Address ?? "").ToLowerInvariant(),
        "group" => (f.Group ?? "").ToLowerInvariant(),
        _ => "",
    };

    private static readonly Dictionary<string, string> Plural = new() { ["account"] = "accounts", ["address"] = "addresses", ["computer"] = "computers" };

    /// <summary>Something happened: every rule waiting for it goes off once its count is reached. Too old to set one off: nothing. How many went off.</summary>
    public async Task<int> Handle(AlertFact fact, CancellationToken stop)
    {
        var at = fact.At ?? Now;
        if (Now - at > Recent)
        {
            return 0;
        }
        var n = 0;
        foreach (var r in Rules().Where(r => Matches(r, fact)))
        {
            var said = fact;
            if (r.Times > 1)
            {
                var seen = counted.GetOrAdd($"{r.Id}|{Of(r.Per, fact)}", _ => []);
                int got;
                lock (seen)
                {
                    seen.Add((at, r.Distinct.Length > 0 ? Of(r.Distinct, fact) : ""));
                    seen.RemoveAll(s => (at - s.At).TotalMinutes > r.Minutes);
                    got = r.Distinct.Length > 0 ? seen.Select(s => s.Value).Where(v => v.Length > 0).Distinct().Count() : seen.Count;
                    if (got < r.Times)
                    {
                        continue;
                    }
                    seen.Clear();
                }
                var counting = r.Distinct.Length > 0 ? $"{got} different {Plural.GetValueOrDefault(r.Distinct, r.Distinct)}" : $"{got} times";
                said = fact with { Text = $"{counting} in {r.Minutes} minutes. {fact.Text}" };
            }
            await Fire(r, said, stop);
            n++;
        }
        return n;
    }

    private static string Tone(string severity) => severity switch { "critical" => "down", "info" => "note", _ => "warn" };

    private List<string> Channels(EventRule r) => [.. alerts.Types().Where(t => r.Types.Contains(t.Id)).SelectMany(t => t.To).Distinct()];

    /// <summary>A rule's name: its own, or what happens.</summary>
    public static string NameOf(EventRule r) => r.Name.Length > 0 ? r.Name : AlertEvents.Get(r.Event)?.Name ?? r.Event;

    private static MailNote Note(EventRule r, string title, string text, DateTime at)
    {
        var facts = new List<(string, string)> { ("Alert", NameOf(r)), ("Severity", Word(r.Severity)) };
        if ((r.AboutName ?? r.About) is { } about)
        {
            facts.Add(("About", about));
        }
        return new MailNote(title, Tone(r.Severity), facts, new AlertCard(Tone(r.Severity), title, text, [new("🔔 Alert", [NameOf(r), Word(r.Severity)])], Product.Lab, at));
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
        var (_, failed) = await alerts.Send("alert:test", Note(r, $"Test: {NameOf(r)}", $"This is a test of an alert from {Product.Title}.", Now), to, stop);
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
                    await Once(r, c.Id, gone >= r.After, new("dev.offline", $"{name} stopped checking in", $"Its client last checked in {Math.Round(gone)} minutes ago.", Computer: name, Os: c.Os, At: Now));
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
                        await Once(r, $"{c.Id}|{d.Name}", used >= r.Percent, new("dev.disk", $"{d.Name} on {name} is {used}% full", $"{Gb(d.Free)} free of {Gb(d.Total)}.", Computer: name, Os: c.Os, At: Now));
                    }
                }
                else if (inv.MissingChecked is not null)
                {
                    var n = inv.Missing.Count(u => !u.Definition);
                    await Once(r, c.Id, n > 0, new("dev.updates", $"{name} is missing {n} update{(n == 1 ? "" : "s")}", string.Join(", ", inv.Missing.Where(u => !u.Definition).Take(5).Select(u => u.Kb)), Computer: name, Os: c.Os, At: Now));
                }
            }
        }
        foreach (var k in told.Keys.Where(k => !seen.Contains(k) && !k.StartsWith("update|", StringComparison.Ordinal)))
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

    // --- What the activity log and the domain controllers say happened ---------------------------------------------------

    /// <summary>An activity log entry as something an alert can be about, or null when none is.</summary>
    public static AlertFact? FromActivity(ActivityEntry e)
    {
        string? After(string pattern) => Regex.Match(e.Title, pattern) is { Success: true } m ? m.Groups[1].Value : null;
        var by = $"By {e.By}.";
        AlertFact Fact(string ev, string title, string text, string? subject = null, string? name = null, string? computer = null) =>
            new(ev, title, text, subject, name, Computer: computer, Address: e.From, By: e.By, At: e.At);
        return e.Action switch
        {
            "signin.failed" when e.By != SignIn.Unknown => Fact("signin.fail", $"{e.By} failed to sign in to {Product.Title}", $"From {e.From ?? "an unknown address"}.", e.By),
            "signin.locked" when e.By != SignIn.Unknown => Fact("signin.lock", $"{e.By} is locked out of {Product.Title}", $"Too many failed sign-ins, the last from {e.From ?? "an unknown address"}. Unlock it in Settings, Sign-in.", e.By),
            "ad.password" => Fact("ad.password", e.Title, by, After(@" for (\S+)")),
            "role.add" or "role.change" or "role.delete" or "role.member.add" or "role.member.remove" => Fact("rl.role", e.Title, by),
            "settings.idle" or "settings.lock" or "update.settings" or "ad.settings" or "client.settings" or "ad.domain.set" or "account.add-admin" or "ad.readonly.off" => Fact("rl.settings", e.Title, by),
            "workflow.failed" => Fact("rl.flow", e.Title, e.Detail ?? ""),
            "ad.service.failed" => Fact("rl.svc", e.Title, e.Detail ?? ""),
            "apps.hand" => Fact("apps.hand", e.Title, e.Detail ?? ""),
            "apps.sync.failed" => Fact("apps.sync", e.Title, e.Detail ?? ""),
            "ad.workflow.run" or "ad.offboard.stage" when e.Result is "failed" or "warning" =>
                Fact("rl.flow", $"{e.Title} stopped at a failed step", e.Detail ?? by, After(@" for (\S+)$"), After(@" for (.+)$")),
            "client.join" => Fact("dev.new", e.Title, e.From is { } ip ? $"From {ip}." : "", computer: After(@"^(\S+) connected")),
            "client.uninstall" or "client.remove" => Fact("dev.uninstall", e.Title, by),
            "network.new" => Fact("net.new", e.Title, e.Detail ?? "", computer: After(@"^(.+?) \(")),
            "gpo.change" or "gpo.new" or "gpo.delete" or "gpo.link" or "gpo.rename" or "gpo.restore" or "gpo.block" or "gpo.apply" or "gpo.wmifilter" when e.Result != "failed" => Fact("ad.gpo", e.Title, by),
            _ => null,
        };
    }

    private static readonly Dictionary<string, string> KindWord = new() { ["user"] = "user", ["group"] = "group", ["computer"] = "computer", ["ou"] = "OU" };

    /// <summary>Directory changes from the AD audit: group members, deletes, new objects, accounts turned off or on, and changes made outside Rabbid Lab.</summary>
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
            var who = ours ? Product.Title : e.ByName ?? account ?? "an unknown account";
            var by = $"By {who}, on {e.Dc}.";
            AlertFact Fact(string ev, string title, string? subject = null, string? group = null) => new(ev, title, by, SubjectName: subject ?? name, Group: group, What: e.Kind, By: who, At: e.At);
            if (e.Attribute.Equals("member", StringComparison.OrdinalIgnoreCase) && e.Kind == "group")
            {
                list.AddRange(e.New.Select(m => Fact("ad.group.add", $"{Ldap.Name(m)} was added to {name}", Ldap.Name(m), name)));
                list.AddRange(e.Old.Select(m => Fact("ad.group.remove", $"{Ldap.Name(m)} was removed from {name}", Ldap.Name(m), name)));
            }
            else if (e.What == "deleted")
            {
                list.Add(Fact("ad.delete", $"The {kind} {name} was deleted"));
            }
            else if (e.What == "made")
            {
                list.Add(Fact("ad.create", $"The {kind} {name} was created"));
            }
            else if (e.Attribute.Equals("userAccountControl", StringComparison.OrdinalIgnoreCase) && Off(e.Old) is { } was && Off(e.New) is { } now && was != now)
            {
                list.Add(Fact("ad.disable", $"The {kind} {name} was {(now ? "disabled" : "enabled")}"));
            }
            if (!ours && account is not null && outside.Add(e.Op))
            {
                list.Add(new("ad.outside", $"{who} {e.What} the {kind} {name}", $"Outside {Product.Title}, on {e.Dc}.", SubjectName: name, What: e.Kind, By: who, At: e.At));
            }
        }
        return list;
    }

    // Whether userAccountControl says the account is off (bit 2), from the value written down; null when there's no number.
    private static bool? Off(List<string> values) => values.Count > 0 && int.TryParse(values[0], out var flags) ? (flags & 2) != 0 : null;

    // Why a Kerberos (hex) or NTLM (NTSTATUS) sign-in failed, in words; null for codes that aren't a person's sign-in failing.
    private static readonly Dictionary<string, (string Event, string Why)> Failures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0x18"] = ("ad.signin.fail", "Wrong password"), ["0x6"] = ("ad.signin.fail", "No such account"), ["0x12"] = ("ad.signin.disabled", "The account is disabled, expired or locked out"),
        ["0x17"] = ("ad.signin.disabled", "The password has expired"), ["0xC000006A"] = ("ad.signin.fail", "Wrong password"), ["0xC0000064"] = ("ad.signin.fail", "No such account"),
        ["0xC0000072"] = ("ad.signin.disabled", "The account is disabled"), ["0xC0000193"] = ("ad.signin.disabled", "The account has expired"),
        ["0xC0000234"] = ("ad.signin.disabled", "The account is locked out"), ["0xC0000071"] = ("ad.signin.disabled", "The password has expired"),
    };

    private static string? Ip(string? address) => address is null ? null : address.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase) ? address[7..] : address is "::1" or "-" ? null : address;

    /// <summary>
    /// Sign-ins from the domain controllers' Security logs: lockouts (4740), Kerberos tickets that were or weren't given
    /// (4768), pre-authentication that failed (4771) and NTLM that failed (4776). Computer accounts are left out. A
    /// sign-in that worked is ad.signin, where name says which computer the address is.
    /// </summary>
    public static List<AlertFact> FromSignIns(IReadOnlyList<SecurityEvent> events, Func<string, string?> name)
    {
        var list = new List<AlertFact>();
        foreach (var e in events)
        {
            var user = e.Get("TargetUserName");
            if (user is null || user.EndsWith('$'))
            {
                continue;
            }
            var ip = Ip(e.Get("IpAddress"));
            var computer = e.Id == 4776 ? e.Get("Workstation") : ip is null ? null : name(ip) ?? ip;
            var from = computer ?? ip;
            var where = from is null ? "" : $" from {from}";
            if (e.Id == 4740)
            {
                var caller = e.Get("CallerComputerName");
                list.Add(new("ad.lockout", $"{user} was locked out", $"On {e.Dc}{(caller is null ? "" : $", after failed sign-ins from {caller}")}.", user, Computer: caller, At: e.At));
                continue;
            }
            var status = e.Get("Status") ?? "0x0";
            if (e.Id == 4768 && status == "0x0")
            {
                list.Add(new("ad.signin", $"{user} signed in{where}", $"On {e.Dc}.", user, Computer: computer, Address: ip, At: e.At));
                continue;
            }
            if (e.Id is 4768 or 4771 or 4776 && Failures.TryGetValue(status, out var f))
            {
                list.Add(new(f.Event, f.Event == "ad.signin.disabled" ? $"{user} tried to sign in while it can't{where}" : $"{user} failed to sign in{where}",
                    $"{f.Why}. On {e.Dc}.", user, Computer: computer, Address: ip, Reason: f.Why, At: e.At));
            }
        }
        return list;
    }
}

/// <summary>What was learned of each account from its sign-ins: the computers it uses and the hours it signs in.</summary>
public sealed class LearnedAccount
{
    public DateTime First { get; set; }
    public Dictionary<string, DateTime> Places { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int[] Hours { get; set; } = new int[24];
}

public sealed class LearningFile
{
    /// <summary>How many days of sign-ins come before an account's unusual sign-ins alert: 7, 14 or 30.</summary>
    public int Days { get; set; } = 14;
    public Dictionary<string, LearnedAccount> Accounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The computers and hours each account usually signs in from (signin-learning.json), learned from its sign-ins. A
/// sign-in is unusual once the account has been learned for the learning period: on a computer it hasn't used in that
/// time, or at an hour that has had fewer than one in fifty of its sign-ins. Kept in memory and saved every few minutes.
/// </summary>
public sealed class SignInLearning(DataFolder folder, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly JsonFile<LearningFile> file = folder.Json<LearningFile>("signin-learning.json", Json);
    private readonly Lock gate = new();
    private LearningFile? now;
    private DateTime saved;

    private LearningFile Now() => now ??= file.Read();

    /// <summary>Learns a sign-in, and says what was unusual about it before it was learned: new (computer), hours.</summary>
    public string? See(string account, string place, DateTime at)
    {
        lock (gate)
        {
            var f = Now();
            if (!f.Accounts.TryGetValue(account, out var a))
            {
                f.Accounts[account] = a = new LearnedAccount { First = at };
            }
            a.First = at < a.First ? at : a.First;
            var learned = at - a.First >= TimeSpan.FromDays(f.Days);
            var hour = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(at, DateTimeKind.Utc), TimeZoneInfo.Local).Hour;
            var total = a.Hours.Sum();
            var flags = new List<string>();
            if (learned && (!a.Places.TryGetValue(place, out var last) || at - last > TimeSpan.FromDays(f.Days)))
            {
                flags.Add("new");
            }
            if (learned && total >= 20 && a.Hours[hour] * 50 < total)
            {
                flags.Add("hours");
            }
            a.Places[place] = a.Places.TryGetValue(place, out var was) && was > at ? was : at;
            a.Hours[hour]++;
            return flags.Count > 0 ? string.Join(',', flags) : null;
        }
    }

    /// <summary>Saves what was learned, at most every few minutes unless now; computers not used for 90 days are forgotten.</summary>
    public void Save(bool force = false)
    {
        lock (gate)
        {
            if (now is null || (!force && clock.GetUtcNow().UtcDateTime - saved < TimeSpan.FromMinutes(5)))
            {
                return;
            }
            var old = clock.GetUtcNow().UtcDateTime.AddDays(-90);
            foreach (var a in now.Accounts.Values)
            {
                foreach (var p in a.Places.Where(p => p.Value < old).Select(p => p.Key).ToList())
                {
                    a.Places.Remove(p);
                }
            }
            file.Save(now);
            saved = clock.GetUtcNow().UtcDateTime;
        }
    }

    public int Days
    {
        get { lock (gate) { return Now().Days; } }
    }

    public void SetDays(int days)
    {
        lock (gate)
        {
            Now().Days = days;
        }
        Save(force: true);
    }

    /// <summary>For the page: the learning period, how many accounts are known, how many are learned, and since when.</summary>
    public object View()
    {
        lock (gate)
        {
            var f = Now();
            var at = clock.GetUtcNow().UtcDateTime;
            return new
            {
                days = f.Days,
                accounts = f.Accounts.Count,
                learned = f.Accounts.Values.Count(a => at - a.First >= TimeSpan.FromDays(f.Days)),
                since = f.Accounts.Count == 0 ? (DateTime?) null : f.Accounts.Values.Min(a => a.First),
            };
        }
    }
}

/// <summary>Feeds the alerts what happens: the activity log and the Security logs as they come, and the devices and updates every 30 seconds.</summary>
public sealed class EventAlertWorker(EventAlerts alerts, SignInLearning learning, ActivityLog activity, AuditCollector audit, IAuditDirectory ad, Clients clients,
    Inventories inventories, RabbidLab.Updater.UpdateEngine updates, ILogger<EventAlertWorker> log) : BackgroundService
{
    private readonly Channel<AlertFact> queue = System.Threading.Channels.Channel.CreateUnbounded<AlertFact>();

    // A computer's name for an address: the client there, else the address itself.
    private string? Place(string ip) => clients.All().FirstOrDefault(c => c.Address == ip)?.Computer;

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
            // A sign-in that worked is learned first; what was unusual about it rides along as its flags.
            foreach (var f in EventAlerts.FromSignIns(events, Place))
            {
                queue.Writer.TryWrite(f.Event == "ad.signin" && f.Subject is { } who && f.At is { } at ? f with { Flags = learning.See(who, f.Computer ?? f.Address ?? "", at) } : f);
            }
            learning.Save();
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
        learning.Save(force: true);
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
        services.AddSingleton<SignInLearning>();
        services.AddHostedService<EventAlertWorker>();
    }

    // The workflows an alert can run: enabled, and run on a user rather than making one.
    private static List<PeopleFlow> Runnable(PeopleWorkflows flows) => [.. flows.All().Where(f => f.On && f.Steps.FirstOrDefault()?.Kind != "create")];

    private static object View(EventAlerts ea, Alerts alerts, PeopleWorkflows flows, SignInLearning learning, AuditCollector audit) => new
    {
        events = AlertEvents.All.Select(e => new { e.Id, e.Area, e.Name, e.Fixed, e.Fields, e.Severity, e.User, e.NotYet, e.Note, e.Times, e.Minutes, e.Per, e.Distinct }),
        rules = ea.Rules(),
        fired = ea.Fired(),
        types = alerts.Types().Select(t => new { t.Id, t.Name, to = t.To.Count }),
        start = alerts.StartType().Id, // what a new alert goes through, as new sensors do
        quiet = alerts.Quiet(DateTime.UtcNow),
        workflows = Runnable(flows).Select(f => new { f.Id, f.Name }),
        privileged = AlertEvents.Privileged,
        learning = learning.View(),
        dcs = audit.Dcs().Select(d => new { name = d.Name, state = d.State.State, problem = d.State.Problem, read = d.State.LastRead, last = d.State.LastAt }),
    };

    public static void MapEventAlertsApi(this WebApplication app)
    {
        app.MapGet("/api/event-alerts", (HttpContext context, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, SignInLearning learning, AuditCollector audit) =>
            !Access.Can(context, Permissions.SensorsAlerts) ? Access.NotAllowed() : Results.Ok(View(ea, alerts, flows, learning, audit)));

        IResult Saved(HttpContext context, string? id, EventRuleForm form, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, ActivityLog log, SignInLearning learning, AuditCollector audit)
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
            log.Record(context, "sensors", id is null ? "alerts.event.add" : "alerts.event.change", $"{(id is null ? "Added" : "Changed")} the alert {EventAlerts.NameOf(rule)}{((rule.AboutName ?? rule.About) is { } who ? $" about {who}" : "")}");
            return Results.Ok(new { id = rule.Id, now = View(ea, alerts, flows, learning, audit) });
        }
        app.MapPost("/api/event-alerts/rules", (EventRuleForm form, HttpContext context, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, ActivityLog log, SignInLearning learning, AuditCollector audit) =>
            Saved(context, null, form, ea, alerts, flows, log, learning, audit));
        app.MapPost("/api/event-alerts/rules/{id}", (string id, EventRuleForm form, HttpContext context, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, ActivityLog log, SignInLearning learning, AuditCollector audit) =>
            Saved(context, id, form, ea, alerts, flows, log, learning, audit));

        app.MapPost("/api/event-alerts/rules/{id}/delete", (string id, HttpContext context, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, ActivityLog log, SignInLearning learning, AuditCollector audit) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var name = ea.Rule(id) is { } r ? EventAlerts.NameOf(r) : null;
            if (name is null || !ea.Delete(id))
            {
                return Results.NotFound(new { error = "Alert not found. It may have been deleted." });
            }
            log.Record(context, "sensors", "alerts.event.delete", $"Deleted the alert {name}");
            return Results.Ok(new { now = View(ea, alerts, flows, learning, audit) });
        });

        app.MapPost("/api/event-alerts/rules/{id}/test", async (string id, HttpContext context, EventAlerts ea, CancellationToken stop) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            return await ea.Test(id, stop) is { } problem ? Results.BadRequest(new { error = problem }) : Results.Ok(new { sent = true });
        });

        app.MapPost("/api/event-alerts/ack", (AckForm form, HttpContext context, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, ActivityLog log, SignInLearning learning, AuditCollector audit) =>
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
            return Results.Ok(new { acknowledged = n, now = View(ea, alerts, flows, learning, audit) });
        });

        app.MapPost("/api/event-alerts/fired/{id}/approval", (string id, ApprovalForm form, HttpContext context, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, ActivityLog log, SignInLearning learning, AuditCollector audit) =>
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
            return Results.Ok(new { now = View(ea, alerts, flows, learning, audit) });
        });

        // The learning period for unusual sign-ins: 7, 14 or 30 days.
        app.MapPost("/api/event-alerts/learning", (LearningForm form, HttpContext context, EventAlerts ea, Alerts alerts, PeopleWorkflows flows, ActivityLog log, SignInLearning learning, AuditCollector audit) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            if (form.Days is not (7 or 14 or 30))
            {
                return Results.BadRequest(new { error = "Select 7, 14 or 30 days." });
            }
            learning.SetDays(form.Days.Value);
            log.Record(context, "sensors", "alerts.learning", $"Set the learning period for unusual sign-ins to {form.Days} days");
            return Results.Ok(new { now = View(ea, alerts, flows, learning, audit) });
        });
    }
}
