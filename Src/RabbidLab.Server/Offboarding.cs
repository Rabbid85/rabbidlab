using System.Globalization;
using System.Net;
using System.Text.Json;
using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>What an offboarding does, as its window chose. Stage 1 is the last day, stage 2 and 3 some days later.</summary>
public sealed class OffboardOptions
{
    public bool Disable { get; set; }
    public bool Password { get; set; }
    public bool SignOut { get; set; }
    public bool Hide { get; set; }
    public bool Manager { get; set; }

    /// <summary>Who the direct reports move to (a DN), or none to only clear their manager.</summary>
    public string? ReportsTo { get; set; }
    public string? ReportsToName { get; set; }
    public bool Contact { get; set; }
    public bool Note { get; set; }
    public string? NoteText { get; set; }
    public bool Email { get; set; }

    public bool Groups { get; set; }

    /// <summary>The groups (DNs) the person stays in.</summary>
    public List<string> Keep { get; set; } = [];
    public bool Move { get; set; }
    public string? MoveTo { get; set; }
    public string? MoveToName { get; set; }
    public bool Computers { get; set; }
    public bool Archive { get; set; }
    public string? ArchiveTo { get; set; }

    public bool Purge { get; set; }
    public bool Delete { get; set; }
}

/// <summary>How one step went: done, skipped (nothing to do) or failed, and what happened in words.</summary>
public sealed class OffboardResult
{
    public string Key { get; set; } = "";
    public string State { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class OffboardStage
{
    public int N { get; set; }
    public DateTime At { get; set; }

    /// <summary>scheduled, running, done, failed or cancelled.</summary>
    public string State { get; set; } = Offboarding.Scheduled;
    public DateTime? Ran { get; set; }
    public string? RanBy { get; set; }
    public bool Reminded { get; set; }
    public List<string> Steps { get; set; } = [];
    public List<OffboardResult> Results { get; set; } = [];
}

/// <summary>Who an offboarding runs as: the person who last saved it, with their roles as they are when each stage runs.</summary>
public sealed class OffboardWho
{
    public string Source { get; set; } = "";
    public string Username { get; set; } = "";
    public string? Domain { get; set; }
    public List<string> Groups { get; set; } = [];
    public string Name { get; set; } = "";

    public Who Who() => new(Source, Username, Domain, Groups);
}

/// <summary>One person's offboarding: what it does, when each stage runs, and how each went.</summary>
public sealed class OffboardPlan
{
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string Dn { get; set; } = "";
    public string? Sid { get; set; }
    public DateTime LastDay { get; set; }
    public int Days2 { get; set; } = 7;
    public int Days3 { get; set; } = 90;
    public OffboardOptions Options { get; set; } = new();
    public OffboardWho By { get; set; } = new();
    public DateTime Saved { get; set; }

    /// <summary>Where the home folder went in the archive share, for stage 3 to delete.</summary>
    public string? Archived { get; set; }
    public List<OffboardStage> Stages { get; set; } = [];
}

public sealed class OffboardFile
{
    public List<OffboardPlan> Plans { get; set; } = [];

    /// <summary>The last OU and archive share used, as the next window's defaults.</summary>
    public string? MoveTo { get; set; }
    public string? MoveToName { get; set; }
    public string? ArchiveTo { get; set; }
}

/// <summary>The window's answer: who, when (now, or yyyy-MM-ddTHH:mm on the server's clock), the days to stages 2 and 3, and what to do.</summary>
public sealed record OffboardForm(List<string>? Users, string? When, int Days2, int Days3, OffboardOptions? Options);

public sealed record OffboardLookForm(List<string>? Users);

/// <summary>
/// Offboarding in three stages, counted from the last day (offboarding.json). Each step goes through the same directory
/// tools as a change made by hand, as the person who scheduled it with their roles as they are then, so scope, rank, the
/// change journal and the activity log all hold. A failed stage holds the later ones until it's retried or cancelled.
/// </summary>
public sealed class Offboarding(DataFolder folder, TimeProvider clock, Roles roles, IDirectoryTools tools, IDirectoryActions actions, IOffboardDirectory directory,
    IHomeFolders homes, IPersonMail mail, ChangeJournal journal, ActivityLog log, Notes notes, Clients clients, DeviceJobs jobs, ILogger<Offboarding> logger)
{
    public const string Scheduled = "scheduled";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Skipped = "skipped";

    public static readonly int[] Days2Choices = [7, 14, 30];
    public static readonly int[] Days3Choices = [30, 90, 180];

    /// <summary>A step: its key, stage, what it's called, the permission it takes, and whether that permission's endpoint takes Confirm it's you.</summary>
    public sealed record Step(string Key, int Stage, string Label, string Permission, bool Confirm = false);

    public static readonly IReadOnlyList<Step> Steps =
    [
        new("disable", 1, "Disable the account", Permissions.AdEnable),
        new("password", 1, "Reset the password to a random value", Permissions.AdPassword, true),
        new("signout", 1, "Sign out of every computer", Permissions.SeatsActions),
        new("hide", 1, "Hide from address lists", Permissions.AdChange),
        new("manager", 1, "Clear the manager and reassign direct reports", Permissions.AdChange),
        new("contact", 1, "Remove the phone number and office", Permissions.AdChange),
        new("note", 1, "Add an offboarding note", Permissions.AdChange),
        new("email", 1, "Email the manager when it is done", Permissions.AdView),
        new("groups", 2, "Remove from groups", Permissions.AdGroups, true),
        new("move", 2, "Move to another OU", Permissions.AdChange),
        new("computers", 2, "Disable their computers", Permissions.AdEnable),
        new("archive", 2, "Move the home folder to the archive share", Permissions.AdChange),
        new("purge", 3, "Delete the archived home folder", Permissions.AdChange),
        new("delete", 3, "Delete the account", Permissions.AdDelete),
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<OffboardFile> file = folder.Json<OffboardFile>("offboarding.json", Json);
    private readonly SemaphoreSlim one = new(1, 1); // one stage runs at a time, by the worker or Run now

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private TimeZoneInfo Zone => clock.LocalTimeZone;

    public static bool On(OffboardOptions o, string key) => key switch
    {
        "disable" => o.Disable, "password" => o.Password, "signout" => o.SignOut, "hide" => o.Hide, "manager" => o.Manager, "contact" => o.Contact,
        "note" => o.Note, "email" => o.Email, "groups" => o.Groups, "move" => o.Move, "computers" => o.Computers, "archive" => o.Archive,
        "purge" => o.Purge, "delete" => o.Delete, _ => false,
    };

    public static string PermissionName(string key) => Permissions.Parts.SelectMany(p => p.Actions).FirstOrDefault(a => a.Key == key)?.Name ?? key;

    // --- Reading -------------------------------------------------------------------------------------------------------

    /// <summary>A copy, so the worker and the page can look while a stage saves.</summary>
    public OffboardFile Read()
    {
        lock (file.Gate)
        {
            return JsonSerializer.Deserialize<OffboardFile>(JsonSerializer.SerializeToUtf8Bytes(file.Read(), Json), Json)!;
        }
    }

    /// <summary>Now on this server's clock, as the window's calendar starts from it, and the time zone's name.</summary>
    public string LocalNow => TimeZoneInfo.ConvertTimeFromUtc(Now, Zone).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    public string ZoneName => Zone.IsDaylightSavingTime(Now) ? Zone.DaylightName : Zone.StandardName;

    public OffboardPlan? Find(string username) => Read().Plans.FirstOrDefault(p => Same(p.Username, username));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The stage that's next: scheduled, running or failed (which holds the rest). Null when it's all done or cancelled.</summary>
    public static OffboardStage? Next(OffboardPlan p) => p.Stages.FirstOrDefault(s => s.State is Scheduled or Running or Failed);

    private void Change(Action<OffboardFile> change)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            change(f);
            // Finished ones go 30 days after their last stage.
            f.Plans.RemoveAll(p => Next(p) is null && (p.Stages.Select(s => s.Ran ?? s.At).DefaultIfEmpty(p.Saved).Max()) < Now.AddDays(-30));
            file.Save(f);
        }
    }

    // --- Saving ----------------------------------------------------------------------------------------------------

    /// <summary>When stage 1 runs, from the window: now, or a time on this server's clock. Null when it's not a time to come.</summary>
    public DateTime? When(string? when)
    {
        if (string.IsNullOrWhiteSpace(when) || when.Trim() == "now")
        {
            return Now;
        }
        if (!DateTime.TryParseExact(when.Trim(), "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }
        var t = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(Zone.IsInvalidTime(t) ? t.AddHours(1) : t, Zone);
        return utc > Now.AddMinutes(-1) ? utc : null;
    }

    /// <summary>
    /// Saves an offboarding for one person: new, or in place of the stages of theirs still to run (stages that ran stay as
    /// they were, and a last day that has passed stays too).
    /// </summary>
    public OffboardPlan Save(AdUser user, DateTime lastDay, int days2, int days3, OffboardOptions o, Who who, string name)
    {
        OffboardPlan? saved = null;
        Change(f =>
        {
            var old = f.Plans.FirstOrDefault(p => Same(p.Username, user.Username));
            var ran = old?.Stages.Where(s => s.State == Done).ToList() ?? [];
            var day = ran.Any(s => s.N == 1) ? old!.LastDay : lastDay;
            var plan = new OffboardPlan
            {
                Username = user.Username, Name = user.Name ?? user.Username, Dn = user.Dn, Sid = user.More?.Sid, LastDay = day, Days2 = days2, Days3 = days3, Options = o,
                By = new OffboardWho { Source = who.Source, Username = who.Username, Domain = who.Domain, Groups = [.. who.Groups], Name = name }, Saved = Now,
                Archived = old?.Archived,
            };
            foreach (var n in new[] { 1, 2, 3 })
            {
                if (ran.FirstOrDefault(s => s.N == n) is { } kept)
                {
                    plan.Stages.Add(kept);
                    continue;
                }
                var keys = Steps.Where(s => s.Stage == n && On(o, s.Key)).Select(s => s.Key).ToList();
                if (keys.Count > 0)
                {
                    plan.Stages.Add(new OffboardStage { N = n, At = n == 1 ? day : day.AddDays(n == 2 ? days2 : days3), Steps = keys });
                }
            }
            f.Plans.RemoveAll(p => Same(p.Username, user.Username));
            f.Plans.Add(plan);
            if (o.Move && o.MoveTo is { Length: > 0 })
            {
                (f.MoveTo, f.MoveToName) = (o.MoveTo, o.MoveToName);
            }
            if (o.Archive && o.ArchiveTo is { Length: > 0 })
            {
                f.ArchiveTo = o.ArchiveTo;
            }
            saved = plan;
        });
        return saved!;
    }

    /// <summary>The stages still to run don't. Stages that ran are not undone. How many were cancelled.</summary>
    public int Cancel(string username)
    {
        var count = 0;
        Change(f =>
        {
            foreach (var s in f.Plans.FirstOrDefault(p => Same(p.Username, username))?.Stages.Where(s => s.State is Scheduled or Failed) ?? [])
            {
                s.State = Cancelled;
                count++;
            }
        });
        return count;
    }

    // --- Running ---------------------------------------------------------------------------------------------------

    /// <summary>One look: runs the stages that are due, and emails the day-before reminders. How many stages ran.</summary>
    public async Task<int> Tick(CancellationToken stop)
    {
        var ran = 0;
        foreach (var p in Read().Plans)
        {
            if (Next(p) is not { } s)
            {
                continue;
            }
            if (s.State == Scheduled && s.At <= Now)
            {
                await Run(p.Username, p.By.Who(), p.By.Name, stop);
                ran++;
            }
            else if (s.State == Scheduled && !s.Reminded && s.At - Now <= TimeSpan.FromDays(1) && s.At - Now > TimeSpan.FromMinutes(30))
            {
                await Remind(p, s, stop);
            }
        }
        return ran;
    }

    /// <summary>
    /// Runs the next stage now, as who: a scheduled one in full, or the steps of a failed one that didn't work. Null when
    /// nothing is left to run.
    /// </summary>
    public async Task<OffboardStage?> Run(string username, Who who, string by, CancellationToken stop)
    {
        await one.WaitAsync(stop);
        try
        {
            if (Find(username) is not { } plan || Next(plan) is not { } stage)
            {
                return null;
            }
            var retry = stage.State == Failed;
            var todo = Steps.Where(s => stage.Steps.Contains(s.Key) && (!retry || stage.Results.FirstOrDefault(r => r.Key == s.Key) is not { State: Done or Skipped })).ToList();
            Keep(username, stage.N, s => s.State = Running);
            var allowed = roles.Allowed(who);
            AdUser? user = null;
            try
            {
                user = await AsCaller(who, Permissions.AdView, () => tools.User(username, stop));
            }
            catch (ToolProblem)
            {
                // Gone already (deleted by hand): each step says so for itself.
            }
            var run = new Going(plan, stage, who, by, user?.ManagerDn ?? null, []);
            foreach (var step in todo)
            {
                OffboardResult r;
                if (!allowed.Contains(step.Permission))
                {
                    r = Bad(step, $"{by} no longer has the {PermissionName(step.Permission)} permission.");
                }
                else
                {
                    try
                    {
                        r = await Do(step, run, stop);
                    }
                    catch (ToolProblem e)
                    {
                        r = Bad(step, e.Message);
                    }
                    catch (DirectoryProblem e)
                    {
                        r = Bad(step, e.Message);
                    }
                    catch (LdapRefused e)
                    {
                        r = Bad(step, $"Active Directory returned an error: {e.Message}.");
                    }
                }
                run.Results.Add(r);
            }
            OffboardStage? done = null;
            Change(f =>
            {
                if (f.Plans.FirstOrDefault(p => Same(p.Username, username)) is not { } p || p.Stages.FirstOrDefault(s => s.N == stage.N) is not { } s)
                {
                    return;
                }
                foreach (var r in run.Results)
                {
                    s.Results.RemoveAll(x => x.Key == r.Key);
                    s.Results.Add(r);
                }
                s.Results = [.. s.Results.OrderBy(x => Steps.ToList().FindIndex(k => k.Key == x.Key))];
                s.State = s.Results.Any(x => x.State == Failed) ? Failed : Done;
                (s.Ran, s.RanBy) = (Now, by);
                p.Archived = run.Archived ?? p.Archived;
                done = s;
            });
            var failed = run.Results.Where(r => r.State == Failed).ToList();
            log.Record(by, null, "users", "ad.offboard.stage", $"{(retry ? "Retried" : "Ran")} offboarding stage {stage.N} of {plan.Stages.Count} for {username}",
                failed.Count == 0 ? "ok" : "failed", failed.Count == 0 ? null : string.Join(" ", failed.Select(f => $"{Label(f.Key)}: {f.Detail}")));
            return done;
        }
        finally
        {
            one.Release();
        }
    }

    // What one stage's steps share as they go: the manager before the stage cleared it, where the home folder went.
    private sealed record Going(OffboardPlan Plan, OffboardStage Stage, Who Who, string By, string? ManagerDn, List<OffboardResult> Results)
    {
        public string? Archived { get; set; }
    }

    private static OffboardResult Ok(Step s, string detail) => new() { Key = s.Key, State = Done, Detail = detail };

    private static OffboardResult None(Step s, string detail) => new() { Key = s.Key, State = Skipped, Detail = detail };

    private static OffboardResult Bad(Step s, string detail) => new() { Key = s.Key, State = Failed, Detail = detail };

    public static string Label(string key) => Steps.FirstOrDefault(s => s.Key == key)?.Label ?? key;

    private static UserEdit Only(string? description = null, string? manager = null) => new(null, null, null, description, null, null, null, null, null, null, manager);

    /// <summary>Work as who, with that permission, noted in the change journal when it changes Active Directory.</summary>
    private Task<T> AsCaller<T>(Who who, string permission, Func<Task<T>> work, bool note = false, bool confirm = false)
    {
        Caller.Set(new Caller(who, permission, roles));
        ChangeNote.Set(note ? new ChangeNote(journal, permission, confirm) : null);
        return work();
    }

    private async Task<OffboardResult> Do(Step step, Going run, CancellationToken stop)
    {
        var (plan, o, who, by, u) = (run.Plan, run.Plan.Options, run.Who, run.By, run.Plan.Username);
        var detail = $"Offboarding stage {run.Stage.N}";
        // Each change is its own entry in the activity log and the change journal, as if made by hand.
        async Task Logged(string permission, string action, string title, Func<Task> work, bool confirm = false, string? more = null)
        {
            await AsCaller(who, permission, async () => { await work(); return true; }, note: true, confirm: confirm);
            log.Record(by, null, "users", action, title, "ok", more is null ? detail : $"{detail}. {more}");
        }
        switch (step.Key)
        {
            case "disable":
                await Logged(step.Permission, "ad.disable", $"Disabled {u}", () => tools.Enable(u, false, stop));
                return Ok(step, "The account is disabled.");
            case "password":
                await Logged(step.Permission, "ad.password", $"Reset the password for {u}", () => tools.Password(u, Passwords.Make(), false, stop), confirm: true);
                return Ok(step, "The password was reset to a random value that was not shown or kept.");
            case "signout":
            {
                var on = clients.All().Where(c => c.Approved is not null && c.User is { } signed && Same(signed[(signed.LastIndexOf('\\') + 1)..], u)).ToList();
                if (on.Count == 0)
                {
                    return None(step, $"{plan.Name} is not signed in to any computer in Devices.");
                }
                jobs.Add(by, DeviceJobs.SignOut, $"Sign out {u}", [DeviceActions.SignOutUser(u)], on, TimeSpan.FromHours(1));
                log.Record(by, null, "clients", "jobs.signout", $"Signed {u} out of {string.Join(", ", on.Select(c => c.Computer))}", "ok", detail);
                return Ok(step, $"Signing out of {string.Join(", ", on.Select(c => c.Computer))}. The job is under Devices › Jobs.");
            }
            case "hide":
            {
                var hid = false;
                await AsCaller(who, step.Permission, async () => hid = await directory.HideFromAddressLists(u, stop), note: true);
                if (!hid)
                {
                    return None(step, "This domain has no Exchange attributes, so there are no address lists to hide from.");
                }
                log.Record(by, null, "users", "ad.edit", $"Edited properties of {u}", "ok", $"{detail}. Hid from address lists");
                return Ok(step, "Hidden from address lists.");
            }
            case "manager":
            {
                var user = await AsCaller(who, Permissions.AdView, () => tools.User(u, stop));
                var reports = await AsCaller(who, Permissions.AdView, () => directory.Reports(user.Dn, stop));
                var moved = new List<string>();
                if (o.ReportsTo is { Length: > 0 } to)
                {
                    foreach (var r in reports.Where(r => r.Username is not null))
                    {
                        await Logged(step.Permission, "ad.edit", $"Edited properties of {r.Username}", () => actions.Edit(r.Username!, Only(manager: to), stop), more: $"Manager set to {o.ReportsToName ?? Ldap.Name(to)}");
                        moved.Add(r.Name);
                    }
                }
                if (user.ManagerDn is { Length: > 0 })
                {
                    await Logged(step.Permission, "ad.edit", $"Edited properties of {u}", () => actions.Edit(u, Only(manager: ""), stop), more: "Cleared the manager");
                }
                return moved.Count > 0 ? Ok(step, $"{string.Join(", ", moved)} now {(moved.Count == 1 ? "reports" : "report")} to {o.ReportsToName ?? Ldap.Name(o.ReportsTo!)}.")
                    : user.ManagerDn is { Length: > 0 } ? Ok(step, "The manager is cleared. There were no direct reports to move.")
                    : None(step, "There was no manager to clear and no direct reports to move.");
            }
            case "contact":
            {
                var cleared = await AsCaller(who, step.Permission, () => directory.ClearContact(u, stop), note: true);
                if (cleared.Count == 0)
                {
                    return None(step, "No phone number, mobile number or office was set.");
                }
                log.Record(by, null, "users", "ad.edit", $"Edited properties of {u}", "ok", $"{detail}. Cleared {string.Join(", ", cleared)}");
                return Ok(step, $"Cleared the {string.Join(", ", cleared)}.");
            }
            case "note":
            {
                var text = Words.Blank(o.NoteText) ?? $"Offboarded {TimeZoneInfo.ConvertTimeFromUtc(Now, Zone).ToString("d MMM yyyy", CultureInfo.InvariantCulture)} by {by}.";
                var user = await AsCaller(who, Permissions.AdView, () => tools.User(u, stop));
                var description = $"{text} {user.Description}".Trim();
                await Logged(step.Permission, "ad.edit", $"Edited properties of {u}", () => actions.Edit(u, Only(description: description.Length > 1024 ? description[..1024] : description), stop),
                    more: "Added the offboarding note to the description");
                if (user.More?.Sid is { } sid)
                {
                    notes.Add($"ad:{sid}", WebUtility.HtmlEncode(text), by);
                }
                return Ok(step, $"Added “{text}” to the description and the Notes tab.");
            }
            case "email":
            {
                if (run.ManagerDn is not { Length: > 0 } boss)
                {
                    return None(step, $"{plan.Name} has no manager to email.");
                }
                var to = await AsCaller(who, Permissions.AdView, () => directory.MailAt(boss, stop));
                if (to is null)
                {
                    return None(step, $"{Ldap.Name(boss)} has no email address in Active Directory.");
                }
                var done = run.Results.Where(r => r.State == Done).Select(r => Label(r.Key)).ToList();
                var next = plan.Stages.FirstOrDefault(s => s.N > run.Stage.N && s.State == Scheduled);
                var said = await mail.Send(to, new MailNote($"{plan.Name} has been offboarded", "note",
                [
                    ("User", $"{plan.Name} ({u})"), ("Done", done.Count > 0 ? string.Join("; ", done) : "Nothing else was changed"),
                    ("Next", next is null ? "Nothing else is scheduled." : $"Stage {next.N} on {Shown(next.At)}"), ("By", by),
                ]), stop);
                return said is null ? Ok(step, $"Emailed {Ldap.Name(boss)} at {to}.") : Bad(step, $"Could not email {Ldap.Name(boss)}: {said}");
            }
            case "groups":
            {
                var user = await AsCaller(who, Permissions.AdView, () => tools.User(u, stop));
                var leave = user.Groups.Where(g => !o.Keep.Contains(g.Dn, StringComparer.OrdinalIgnoreCase)).ToList();
                if (leave.Count == 0)
                {
                    return None(step, o.Keep.Count > 0 ? "Only the groups to keep were left." : $"{plan.Name} is in no groups other than the primary group.");
                }
                string? warning = null;
                await AsCaller(who, step.Permission, async () => warning = await tools.ChangeGroups(u, [], [.. leave.Select(g => g.Dn)], stop), note: true, confirm: true);
                log.Record(by, null, "users", "ad.groups", $"Changed group membership for {u}: removed from {string.Join(", ", leave.Select(g => g.Name))}",
                    warning is null ? "ok" : "warning", warning is null ? detail : $"{detail}. {warning}");
                return warning is null ? Ok(step, $"Removed from {leave.Count} {(leave.Count == 1 ? "group" : "groups")}: {string.Join(", ", leave.Select(g => g.Name))}.") : Bad(step, warning);
            }
            case "move":
            {
                if (o.MoveTo is not { Length: > 0 } ou)
                {
                    return Bad(step, "No OU was selected to move to.");
                }
                var where = "";
                await Logged(step.Permission, "ad.move", $"Moved {u}", async () => where = await actions.Move(u, ou, stop));
                return Ok(step, $"Moved to {where}.");
            }
            case "computers":
            {
                var user = await AsCaller(who, Permissions.AdView, () => tools.User(u, stop));
                var theirs = await AsCaller(who, Permissions.AdView, () => directory.ManagedComputers(user.Dn, stop));
                if (theirs.Count == 0)
                {
                    return None(step, $"No computers have {plan.Name} as Managed by.");
                }
                foreach (var c in theirs)
                {
                    var name = (c.Username ?? c.Name).TrimEnd('$');
                    await Logged(step.Permission, "ad.computer.disable", $"Disabled the computer {name} in AD", () => tools.EnableComputer(name, false, stop));
                }
                return Ok(step, $"Disabled {string.Join(", ", theirs.Select(c => c.Name))}.");
            }
            case "archive":
            {
                var user = await AsCaller(who, Permissions.AdView, () => tools.User(u, stop));
                if (user.More?.HomeFolder is not { Length: > 0 } home)
                {
                    return None(step, $"{plan.Name} has no home folder set.");
                }
                if (o.ArchiveTo is not { Length: > 0 } into)
                {
                    return Bad(step, "No archive share was entered.");
                }
                run.Archived = await homes.Move(home, into, stop);
                log.Record(by, null, "users", "ad.home.archive", $"Moved the home folder of {u} to {run.Archived}", "ok", detail);
                return Ok(step, $"Moved {home} to {run.Archived}.");
            }
            case "purge":
            {
                if (plan.Archived is not { } path)
                {
                    return None(step, "The home folder was not moved to the archive share, so there is nothing to delete.");
                }
                await homes.Delete(path, stop);
                log.Record(by, null, "users", "ad.home.delete", $"Deleted the archived home folder of {u}", "ok", $"{detail}. {path}");
                return Ok(step, $"Deleted {path}.");
            }
            case "delete":
                await Logged(step.Permission, "ad.delete", $"Deleted the AD user {u}", () => tools.Delete(u, stop));
                return Ok(step, "The account was deleted. It can be restored from the Recycle Bin.");
            default:
                return Bad(step, "This step is not supported.");
        }
    }

    private void Keep(string username, int n, Action<OffboardStage> change) => Change(f =>
    {
        if (f.Plans.FirstOrDefault(p => Same(p.Username, username))?.Stages.FirstOrDefault(s => s.N == n) is { } s)
        {
            change(s);
        }
    });

    /// <summary>The day before a stage, an email to whoever scheduled it, when they signed in with Active Directory and have an address.</summary>
    private async Task Remind(OffboardPlan p, OffboardStage s, CancellationToken stop)
    {
        Keep(p.Username, s.N, x => x.Reminded = true);
        try
        {
            var to = p.By.Source == SignIn.Directory ? await AsCaller(p.By.Who(), Permissions.AdView, () => directory.MailOf(p.By.Username, stop)) : null;
            if (to is null)
            {
                return;
            }
            var said = await mail.Send(to, new MailNote($"Offboarding tomorrow: stage {s.N} for {p.Name}", "warn",
            [
                ("User", $"{p.Name} ({p.Username})"), ("When", Shown(s.At)), ("What it does", string.Join("; ", s.Steps.Select(Label))),
                ("To change it", "Open Active Directory > Users, select the user, then the Offboarding tab to edit or cancel it."),
            ]), stop);
            if (said is not null)
            {
                logger.LogWarning("The offboarding reminder for {User} could not be emailed: {Why}", p.Username, said);
            }
        }
        catch (Exception e) when (e is ToolProblem or DirectoryProblem or LdapRefused)
        {
            logger.LogWarning("The offboarding reminder for {User} could not be sent: {Why}", p.Username, e.Message);
        }
    }

    /// <summary>A time as people read it, on this server's clock: Friday, November 27 at 5:00 PM.</summary>
    public string Shown(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone).ToString("dddd, MMMM d 'at' h:mm tt", CultureInfo.InvariantCulture);

    // --- What the page gets ----------------------------------------------------------------------------------------

    /// <summary>What a step does for this person, as a line on the timeline.</summary>
    public static string Line(OffboardPlan p, string key)
    {
        var o = p.Options;
        return key switch
        {
            "manager" => o.ReportsTo is { Length: > 0 } ? $"Clear the manager and move direct reports to {o.ReportsToName ?? Ldap.Name(o.ReportsTo)}" : "Clear the manager",
            "note" => $"Add the note “{Words.Blank(o.NoteText) ?? "Offboarded (date) by (who)."}” to the description and the Notes tab",
            "groups" => o.Keep.Count > 0 ? $"Remove from groups, keeping {string.Join(", ", o.Keep.Select(Ldap.Name))}" : "Remove from every group except the primary group",
            "move" => $"Move to {o.MoveToName ?? (o.MoveTo is { } m ? Ldap.Name(m) : "another OU")}",
            "archive" => $"Move the home folder to {o.ArchiveTo}",
            "delete" => "Delete the account. It can be restored from the Recycle Bin.",
            _ => Label(key),
        };
    }

    /// <summary>The Users column's words for this person: Stage 2 on Nov 29, Stage 1 failed, Complete.</summary>
    public string Status(OffboardPlan p)
    {
        if (Next(p) is not { } s)
        {
            return p.Stages.Any(x => x.State == Cancelled) ? "Cancelled" : "Complete";
        }
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(s.At, DateTimeKind.Utc), Zone);
        return s.State switch
        {
            Failed => $"Stage {s.N} failed",
            Running => $"Stage {s.N} running",
            _ => $"Stage {s.N} on {local.ToString("MMM d", CultureInfo.InvariantCulture)}",
        };
    }

    public object Brief(OffboardPlan p) => new { username = p.Username, status = Status(p), state = Next(p)?.State ?? "done", next = Next(p) is { } s ? s.At : (DateTime?) null };

    public object View(OffboardPlan p, bool canChange) => new
    {
        username = p.Username, name = p.Name, dn = p.Dn, lastDay = p.LastDay, lastDayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(p.LastDay, DateTimeKind.Utc), Zone).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture), p.Days2, p.Days3, options = p.Options, by = p.By.Name, saved = p.Saved, status = Status(p),
        state = Next(p)?.State ?? "done", canChange,
        stages = p.Stages.Select(s => new
        {
            n = s.N, name = s.N == 1 ? "Last day" : $"{(s.N == 2 ? p.Days2 : p.Days3)} days later", at = s.At, when = Shown(s.At), state = s.State, ran = s.Ran, ranWhen = s.Ran is { } r ? Shown(r) : null, ranBy = s.RanBy,
            steps = s.Steps.Select(k => new { key = k, label = Label(k), line = Line(p, k), result = s.Results.FirstOrDefault(r => r.Key == k) }),
        }),
    };
}

public static class OffboardingApi
{
    private const int MostAtOnce = 50;

    public static void MapOffboardingApi(this WebApplication app)
    {
        // The Users column: everyone with an offboarding, inside what the person's roles show.
        app.MapGet("/api/offboarding", (HttpContext context, Offboarding off, Roles roles) =>
        {
            if (!Access.Can(context, Permissions.AdView))
            {
                return Access.NotAllowed();
            }
            var reach = roles.Reach(Who.Of(context.User), Permissions.AdView);
            return Results.Ok(off.Read().Plans.Where(p => Covers(reach, p.Dn)).Select(off.Brief));
        });

        // One person's offboarding for their Offboarding tab; anyone who can offboard them can change it.
        app.MapGet("/api/offboarding/{username}", (string username, HttpContext context, Offboarding off, Roles roles) =>
        {
            if (!Access.Can(context, Permissions.AdView))
            {
                return Access.NotAllowed();
            }
            var who = Who.Of(context.User);
            if (off.Find(username) is not { } p || !Covers(roles.Reach(who, Permissions.AdView), p.Dn))
            {
                return Results.Ok(new { plan = (object?) null });
            }
            return Results.Ok(new { plan = off.View(p, Access.Can(context, Permissions.AdEnable) && Covers(roles.Reach(who, Permissions.AdEnable), p.Dn)) });
        });

        // What the window shows for each person: groups, manager, direct reports, computers, home folder, and the defaults.
        app.MapPost("/api/offboarding/look", (OffboardLookForm form, HttpContext context, Offboarding off, IDirectoryTools tools, IOffboardDirectory directory, Clients clients) =>
            UsersApi.Try(context, Permissions.AdView, async () =>
            {
                var names = Names(form.Users);
                var stop = context.RequestAborted;
                var people = new List<object>();
                foreach (var name in names)
                {
                    var u = await tools.User(name, stop);
                    var signedIn = clients.All().Where(c => c.Approved is not null && c.User is { } s && string.Equals(s[(s.LastIndexOf('\\') + 1)..], u.Username, StringComparison.OrdinalIgnoreCase))
                        .Select(c => c.Computer);
                    people.Add(new
                    {
                        username = u.Username, name = u.Name ?? u.Username, dn = u.Dn, where = u.Where, enabled = u.Enabled, manager = u.Manager, managerDn = u.ManagerDn,
                        groups = u.Groups, reports = await directory.Reports(u.Dn, stop), computers = await directory.ManagedComputers(u.Dn, stop),
                        home = u.More?.HomeFolder, signedIn, plan = off.Find(u.Username) is { } p ? off.View(p, true) : null,
                    });
                }
                var f = off.Read();
                return Results.Ok(new
                {
                    people, exchange = await directory.HasExchange(stop), moveTo = f.MoveTo, moveToName = f.MoveToName, archiveTo = f.ArchiveTo,
                    days2 = Offboarding.Days2Choices, days3 = Offboarding.Days3Choices, now = off.LocalNow, zone = off.ZoneName,
                });
            }, change: false));

        // Start now or schedule, and edit: Confirm it's you once, and each chosen step's permission for each person.
        app.MapPost("/api/offboarding", (OffboardForm form, HttpContext context, Offboarding off, IDirectoryTools tools, Roles roles, ActivityLog log) =>
            UsersApi.Try(context, Permissions.AdEnable, async () =>
            {
                var names = Names(form.Users);
                var o = form.Options ?? throw new ToolProblem("Select what offboarding does.");
                if (!Offboarding.Days2Choices.Contains(form.Days2) || !Offboarding.Days3Choices.Contains(form.Days3))
                {
                    throw new ToolProblem("Select when stages 2 and 3 run.");
                }
                var chosen = Offboarding.Steps.Where(s => Offboarding.On(o, s.Key)).ToList();
                if (chosen.Count == 0)
                {
                    throw new ToolProblem("Select at least one change to make.");
                }
                if (chosen.Any(s => s.Stage == 2) && chosen.Any(s => s.Stage == 3) && form.Days3 <= form.Days2)
                {
                    throw new ToolProblem("Stage 3 must run after stage 2.");
                }
                if (o.Move && string.IsNullOrWhiteSpace(o.MoveTo))
                {
                    throw new ToolProblem("Select the OU to move to.");
                }
                if (o.Archive && !(o.ArchiveTo ?? "").Trim().StartsWith(@"\\", StringComparison.Ordinal))
                {
                    throw new ToolProblem("Enter the archive share, such as \\\\FS01\\Archive$.");
                }
                if ((o.NoteText ?? "").Length > 512)
                {
                    throw new ToolProblem("The note can be up to 512 characters.");
                }
                o.Keep = [.. o.Keep.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase)];
                var at = off.When(form.When) ?? throw new ToolProblem("Pick a time later than now.");
                var now = at <= context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime.AddMinutes(1);
                var who = Who.Of(context.User);
                var by = ActivityLog.Who(context);
                if (chosen.Select(s => s.Permission).Distinct().FirstOrDefault(p => !Access.Can(context, p)) is { } missing)
                {
                    return Results.Json(new { error = $"Access denied. Offboarding as chosen requires the {Offboarding.PermissionName(missing)} permission." }, statusCode: StatusCodes.Status403Forbidden);
                }
                var users = new List<AdUser>();
                foreach (var name in names)
                {
                    var u = await tools.User(name, context.RequestAborted);
                    if (chosen.Select(s => s.Permission).Distinct().FirstOrDefault(p => !Covers(roles.Reach(who, p), u.Dn)) is { } outside)
                    {
                        return Results.Json(new { error = $"Access denied. Your roles do not cover {u.Username} for {Offboarding.PermissionName(outside)}." }, statusCode: StatusCodes.Status403Forbidden);
                    }
                    users.Add(u);
                }
                foreach (var u in users)
                {
                    var was = off.Find(u.Username) is { } old && Offboarding.Next(old) is not null;
                    var plan = off.Save(u, at, form.Days2, form.Days3, o, who, by);
                    var first = Offboarding.Next(plan);
                    log.Record(context, "users", was ? "ad.offboard.edit" : "ad.offboard.schedule",
                        $"{(was ? "Changed the offboarding" : now ? "Started offboarding" : "Scheduled offboarding")} for {u.Username}",
                        detail: first is null ? "No stages are left to run" : now && first.N == 1 ? "Stage 1 runs now" : $"Stage {first.N} runs {off.Shown(first.At)}");
                }
                // Not tied to the page: closing it mustn't leave a stage half done.
                if (now)
                {
                    foreach (var u in users.Where(u => off.Find(u.Username) is { } p && Offboarding.Next(p) is { N: 1, State: Offboarding.Scheduled }))
                    {
                        await off.Run(u.Username, who, by, CancellationToken.None);
                    }
                }
                return Results.Ok(new { plans = users.Select(u => off.Find(u.Username)).OfType<OffboardPlan>().Select(p => off.View(p, true)) });
            }, confirm: true));

        // The next stage now, as the person asking: a scheduled one in full, or what didn't work in a failed one.
        app.MapPost("/api/offboarding/{username}/run", (string username, HttpContext context, Offboarding off, Roles roles) =>
            UsersApi.Try(context, Permissions.AdEnable, async () =>
            {
                if (off.Find(username) is not { } p || Offboarding.Next(p) is null)
                {
                    throw new ToolProblem("No offboarding stage is left to run.", StatusCodes.Status404NotFound);
                }
                if (!Covers(roles.Reach(Who.Of(context.User), Permissions.AdEnable), p.Dn))
                {
                    return Access.NotAllowed();
                }
                await off.Run(username, Who.Of(context.User), ActivityLog.Who(context), CancellationToken.None);
                return Results.Ok(new { plan = off.View(off.Find(username)!, true) });
            }, confirm: true));

        app.MapPost("/api/offboarding/{username}/cancel", (string username, HttpContext context, Offboarding off, Roles roles, ActivityLog log) =>
            UsersApi.Try(context, Permissions.AdEnable, () =>
            {
                if (off.Find(username) is not { } p || !Covers(roles.Reach(Who.Of(context.User), Permissions.AdEnable), p.Dn))
                {
                    return Task.FromResult(Access.NotAllowed());
                }
                var count = off.Cancel(username);
                if (count > 0)
                {
                    log.Record(context, "users", "ad.offboard.cancel", $"Cancelled the rest of the offboarding for {username}", detail: $"{count} {(count == 1 ? "stage" : "stages")} will not run");
                }
                return Task.FromResult(Results.Ok(new { cancelled = count, plan = off.View(off.Find(username)!, true) }));
            }));
    }

    private static List<string> Names(List<string>? users)
    {
        var names = (users ?? []).Select(n => n?.Trim() ?? "").Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return names.Count is > 0 and <= MostAtOnce ? names : throw new ToolProblem($"Select between 1 and {MostAtOnce} users.");
    }

    private static bool Covers(IReadOnlyList<string>? reach, string dn) => reach is null || reach.Any(ou => Ldap.Under(dn, ou));
}

/// <summary>Runs the offboarding stages that are due, looking every 30 seconds.</summary>
public sealed class OffboardWorker(Offboarding offboarding, TimeProvider clock, ILogger<OffboardWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), clock, stop);
            try
            {
                await offboarding.Tick(stop);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Offboarding could not run the stages that are due");
            }
        }
    }
}
