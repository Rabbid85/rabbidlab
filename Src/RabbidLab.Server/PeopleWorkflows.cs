using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>A value asked for each time a workflow runs: its label, kind (text, choice, user, ou, date), whether it must be given, and a choice's options.</summary>
public sealed class FlowField
{
    public string Label { get; set; } = "";
    public string Kind { get; set; } = "text";
    public bool Required { get; set; }
    public List<string> Choices { get; set; } = [];
}

/// <summary>One step: its kind and what it was set to, with {Field} for a run-time value.</summary>
public sealed class FlowStep
{
    public string Kind { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = [];
}

public sealed class FlowStepResult
{
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public string State { get; set; } = "";
    public string Detail { get; set; } = "";
}

/// <summary>One run: by whom, on whom, how each step went, and the changes it made, so the run can be undone as one.</summary>
public sealed class FlowRun
{
    public string Id { get; set; } = "";
    public DateTime At { get; set; }
    public string By { get; set; } = "";
    public string? User { get; set; }
    public string? UserName { get; set; }
    public string State { get; set; } = "";
    public bool Created { get; set; }
    public List<FlowStepResult> Steps { get; set; } = [];
    public List<string> Changes { get; set; } = [];
}

/// <summary>A people workflow: its run-time fields and its steps, run by hand on a person (or making one).</summary>
public sealed class PeopleFlow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool On { get; set; } = true;
    public List<FlowField> Fields { get; set; } = [];
    public List<FlowStep> Steps { get; set; } = [];
    public string? By { get; set; }
    public DateTime Changed { get; set; }
    public List<FlowRun> Runs { get; set; } = [];
}

/// <summary>
/// A run saved for a date: the workflow, who it's for, its values, when, and who saved it (it runs as them, with their roles
/// as they are then). State is scheduled, running, done, failed or cancelled; Run is how it went.
/// </summary>
public sealed class PlannedRun
{
    public string Id { get; set; } = "";
    public string Flow { get; set; } = "";
    public string FlowName { get; set; } = "";
    public string? User { get; set; }

    /// <summary>Who it's for, as a list shows it: the user it runs on, or the name of the one it makes.</summary>
    public string Name { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = [];
    public DateTime When { get; set; }
    public OffboardWho By { get; set; } = new();
    public DateTime Made { get; set; }
    public string State { get; set; } = "scheduled";
    public FlowRun? Run { get; set; }
}

public sealed class PeopleFlowsFile
{
    public List<PeopleFlow> Flows { get; set; } = [];
    public List<PlannedRun> Planned { get; set; } = [];
}

public sealed record FlowForm(string? Id, string? Name, bool On, List<FlowField>? Fields, List<FlowStep>? Steps);

/// <summary>When: run then instead of now (UTC).</summary>
public sealed record FlowRunForm(string? User, Dictionary<string, string>? Values, DateTime? When = null);

/// <summary>
/// People workflows (people-workflows.json): steps for Active Directory people that run in order through the same
/// directory tools as a change made by hand, inside the runner's roles, and stop at the first failure. Each step that
/// changes Active Directory is its own change in the journal; a run keeps their ids so it can be undone as one.
/// </summary>
public sealed partial class PeopleWorkflows(DataFolder folder, TimeProvider clock)
{
    public const int MostRuns = 20;

    /// <summary>A step kind: its label, the permission it takes, whether that takes Confirm it's you, and the values it has.</summary>
    public sealed record Kind(string Key, string Label, string Permission, bool Confirm, IReadOnlyList<string> Values);

    public static readonly IReadOnlyList<Kind> Kinds =
    [
        new("create", "Create user", Permissions.AdCreate, true, ["name", "logon", "ou", "template"]),
        new("groups", "Add to groups", Permissions.AdGroups, true, ["groups"]),
        new("ungroup", "Remove from groups", Permissions.AdGroups, true, []),
        new("manager", "Set manager", Permissions.AdChange, false, ["manager"]),
        new("props", "Set properties", Permissions.AdChange, false, ["title", "department", "company", "office"]),
        new("move", "Move to OU", Permissions.AdChange, false, ["ou"]),
        new("enable", "Enable account", Permissions.AdEnable, false, []),
        new("disable", "Disable account", Permissions.AdEnable, false, []),
        new("password", "Reset password", Permissions.AdPassword, true, []),
        new("expire", "Set account expiration", Permissions.AdChange, false, ["date"]),
        new("home", "Create home folder", Permissions.AdChange, false, ["path", "drive"]),
        new("email", "Email a person", Permissions.AdView, false, ["to", "subject", "text"]),
    ];

    public static readonly string[] FieldKinds = ["text", "choice", "user", "ou", "date"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<PeopleFlowsFile> file = folder.Json<PeopleFlowsFile>("people-workflows.json", Json);

    [GeneratedRegex(@"\{([^{}]{1,64})\}")]
    private static partial Regex Token();

    public List<PeopleFlow> All()
    {
        lock (file.Gate)
        {
            return JsonSerializer.Deserialize<List<PeopleFlow>>(JsonSerializer.SerializeToUtf8Bytes(file.Read().Flows, Json), Json)!;
        }
    }

    public PeopleFlow? Find(string id) => All().FirstOrDefault(f => f.Id == id);

    /// <summary>Why the workflow can't be saved, or null.</summary>
    public static string? Bad(FlowForm form)
    {
        if (Words.Blank(form.Name) is not { Length: <= 80 })
        {
            return "Enter a name of up to 80 characters.";
        }
        var fields = form.Fields ?? [];
        if (fields.Count > 20 || (form.Steps ?? []).Count is 0 or > 30)
        {
            return "A workflow has 1 to 30 steps and up to 20 fields.";
        }
        if (fields.FirstOrDefault(f => Words.Blank(f.Label) is not { Length: <= 40 } || f.Label.Contains('{') || f.Label.Contains('}')) is not null)
        {
            return "Each field needs a name of up to 40 characters, without { or }.";
        }
        if (fields.GroupBy(f => f.Label.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            return "Each field needs its own name.";
        }
        if (fields.FirstOrDefault(f => !FieldKinds.Contains(f.Kind)) is { } odd)
        {
            return $"{odd.Label} is not a kind of field that can be asked for.";
        }
        if (fields.FirstOrDefault(f => f.Kind == "choice" && f.Choices.Count(c => !string.IsNullOrWhiteSpace(c)) == 0) is { } empty)
        {
            return $"Give {empty.Label} at least one choice.";
        }
        var steps = form.Steps!;
        if (steps.FirstOrDefault(s => Kinds.All(k => k.Key != s.Kind)) is { } unknown)
        {
            return $"{unknown.Kind} is not a step that can be run.";
        }
        if (steps.Skip(1).Any(s => s.Kind == "create"))
        {
            return "Create user can only be the first step.";
        }
        if (steps.SelectMany(s => s.Values.Values).Any(v => (v ?? "").Length > 1000))
        {
            return "A step's value can be up to 1,000 characters.";
        }
        var known = new HashSet<string>([.. fields.Select(f => f.Label.Trim()), "Logon name", "Name"], StringComparer.OrdinalIgnoreCase);
        if (steps.SelectMany(s => s.Values.Values).SelectMany(v => Token().Matches(v ?? "").Select(m => m.Groups[1].Value)).FirstOrDefault(t => !known.Contains(t)) is { } missing)
        {
            return $"{{{missing}}} is not a field of this workflow.";
        }
        if (steps[0].Kind == "create" && (Words.Blank(steps[0].Values.GetValueOrDefault("name")) is null || Words.Blank(steps[0].Values.GetValueOrDefault("logon")) is null))
        {
            return "Create user needs a name and a logon name.";
        }
        return null;
    }

    public PeopleFlow Save(FlowForm form, string by)
    {
        PeopleFlow? kept = null;
        lock (file.Gate)
        {
            var f = file.Read();
            var old = form.Id is { Length: > 0 } id ? f.Flows.FirstOrDefault(x => x.Id == id) : null;
            var flow = old ?? new PeopleFlow { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant() };
            flow.Name = form.Name!.Trim();
            flow.On = form.On;
            flow.Fields = [.. (form.Fields ?? []).Select(x => new FlowField { Label = x.Label.Trim(), Kind = x.Kind, Required = x.Required, Choices = [.. x.Choices.Select(c => c.Trim()).Where(c => c.Length > 0)] })];
            flow.Steps = [.. form.Steps!.Select(s => new FlowStep { Kind = s.Kind, Values = s.Values.Where(v => v.Value is not null).ToDictionary(v => v.Key, v => v.Value.Trim()) })];
            (flow.By, flow.Changed) = (by, clock.GetUtcNow().UtcDateTime);
            if (old is null)
            {
                f.Flows.Add(flow);
            }
            file.Save(f);
            kept = flow;
        }
        return kept!;
    }

    public bool Delete(string id)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            var gone = f.Flows.RemoveAll(x => x.Id == id) > 0;
            if (gone)
            {
                file.Save(f);
            }
            return gone;
        }
    }

    public void Ran(string id, FlowRun run)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Flows.FirstOrDefault(x => x.Id == id) is { } flow)
            {
                flow.Runs.Insert(0, run);
                flow.Runs = [.. flow.Runs.Take(MostRuns)];
                file.Save(f);
            }
        }
    }

    // --- Runs saved for a date -------------------------------------------------------------------------------------

    /// <summary>The most runs that can wait at once.</summary>
    public const int MostPlanned = 200;

    /// <summary>How long a run that finished or was cancelled stays on the Lifecycle page.</summary>
    public static readonly TimeSpan KeepPlanned = TimeSpan.FromDays(30);

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Json), Json)!;

    public List<PlannedRun> Planned()
    {
        lock (file.Gate)
        {
            return Copy(file.Read().Planned);
        }
    }

    public PlannedRun Plan(PeopleFlow flow, string? user, string name, IReadOnlyDictionary<string, string> values, DateTime when, OffboardWho by)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            var now = clock.GetUtcNow().UtcDateTime;
            f.Planned.RemoveAll(p => p.State is "done" or "cancelled" && now - (p.Run?.At ?? p.When) > KeepPlanned);
            if (f.Planned.Count(p => p.State == "scheduled") >= MostPlanned)
            {
                throw new ToolProblem($"{MostPlanned} runs are already scheduled. Cancel one, or wait until one has run.");
            }
            var p = new PlannedRun
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(), Flow = flow.Id, FlowName = flow.Name, User = user, Name = name,
                Values = new Dictionary<string, string>(values), When = when, By = by, Made = now,
            };
            f.Planned.Add(p);
            file.Save(f);
            return Copy(p);
        }
    }

    /// <summary>The runs whose time has come, each marked running so it's taken once.</summary>
    public List<PlannedRun> Due()
    {
        lock (file.Gate)
        {
            var f = file.Read();
            var now = clock.GetUtcNow().UtcDateTime;
            var due = f.Planned.Where(p => p.State == "scheduled" && p.When <= now).ToList();
            if (due.Count == 0)
            {
                return [];
            }
            due.ForEach(p => p.State = "running");
            file.Save(f);
            return Copy(due);
        }
    }

    /// <summary>One run taken to run now, when it's still waiting or it failed; null when it isn't there to take.</summary>
    public PlannedRun? Claim(string id)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Planned.FirstOrDefault(p => p.Id == id && p.State is "scheduled" or "failed") is not { } p)
            {
                return null;
            }
            p.State = "running";
            file.Save(f);
            return Copy(p);
        }
    }

    public PlannedRun? Cancel(string id)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Planned.FirstOrDefault(p => p.Id == id && p.State is "scheduled" or "failed") is not { } p)
            {
                return null;
            }
            p.State = "cancelled";
            file.Save(f);
            return Copy(p);
        }
    }

    public void Finish(string id, FlowRun run)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Planned.FirstOrDefault(p => p.Id == id) is { } p)
            {
                (p.State, p.Run) = (run.State == "done" ? "done" : "failed", run);
                if (run.User is { } user)
                {
                    (p.User, p.Name) = (user, run.UserName ?? p.Name);
                }
                file.Save(f);
            }
        }
    }

    /// <summary>After a restart: a run that was under way didn't finish, and says so instead of running for ever.</summary>
    public void Recover()
    {
        lock (file.Gate)
        {
            var f = file.Read();
            var stuck = f.Planned.Where(p => p.State == "running").ToList();
            foreach (var p in stuck)
            {
                p.State = "failed";
                p.Run = new FlowRun
                {
                    Id = p.Id, At = clock.GetUtcNow().UtcDateTime, By = p.By.Name, User = p.User, UserName = p.Name, State = "failed",
                    Steps = [new FlowStepResult { Label = "Interrupted", State = "failed", Detail = "The server restarted while this was running. Check the user, then run it again or cancel it." }],
                };
            }
            if (stuck.Count > 0)
            {
                file.Save(f);
            }
        }
    }

    /// <summary>Who a run is for before it has run: the user it's on, or the name its first step gives the one it makes.</summary>
    public static string NameFor(PeopleFlow flow, string? user, IReadOnlyDictionary<string, string> values) =>
        user ?? (flow.Steps[0].Kind == "create" && Fill(flow.Steps[0].Values.GetValueOrDefault("name"), values) is { Length: > 0 } name ? name : "A new user");

    /// <summary>A step's value with each {Field} filled in from the run.</summary>
    public static string Fill(string? text, IReadOnlyDictionary<string, string> values) =>
        Token().Replace(text ?? "", m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : "").Trim();

    /// <summary>A step as a sentence, with its {Field} left in, for the list and the panel.</summary>
    public static string Sentence(FlowStep s)
    {
        string V(string k) => s.Values.GetValueOrDefault(k) ?? "";
        return s.Kind switch
        {
            "create" => $"Create {V("name")} ({V("logon")}){(V("ou").Length > 0 ? $" in {Ldap.Name(V("ou"))}" : "")}{(V("template").Length > 0 ? $", copying {V("template")}" : "")}",
            "groups" => V("groups").Length > 0 ? $"Add to {string.Join(", ", V("groups").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(g => Ldap.Name(g.Trim())))}" : "Add to the template user's groups",
            "ungroup" => "Remove from every group except the primary group",
            "manager" => $"Set the manager to {V("manager")}",
            "props" => $"Set {string.Join(", ", new[] { ("job title", V("title")), ("department", V("department")), ("company", V("company")), ("office", V("office")) }.Where(p => p.Item2.Length > 0).Select(p => $"{p.Item1} to {p.Item2}"))}",
            "move" => $"Move to {Ldap.Name(V("ou"))}",
            "enable" => "Enable the account",
            "disable" => "Disable the account",
            "password" => "Reset the password to a random value, changed at next sign-in",
            "expire" => $"Set the account to expire on {V("date")}",
            "home" => $"Create the home folder {V("path")}{(V("drive").Length > 0 ? $" as {V("drive")}" : "")}",
            "email" => $"Email {V("to")}: “{V("subject")}”",
            _ => s.Kind,
        };
    }

    public static Kind KindOf(string key) => Kinds.First(k => k.Key == key);
}

/// <summary>Runs a people workflow for the person asking, step by step, inside their roles.</summary>
public sealed class FlowRunner(IDirectoryTools tools, IDirectoryActions actions, IOffboardDirectory directory, IHomeFolders homes, IPersonMail mail, ChangeJournal journal,
    ActivityLog log, Roles roles, TimeProvider clock)
{
    public Task<FlowRun> Run(HttpContext context, PeopleFlow flow, string? target, IReadOnlyDictionary<string, string> given, CancellationToken stop) =>
        Run(Who.Of(context.User), ActivityLog.Who(context), ActivityLog.Address(context), flow, target, given, stop);

    /// <summary>
    /// A run saved for a date, as whoever saved it, with their roles as they are now. It fails without running when the
    /// workflow is gone or disabled, or they no longer hold a permission one of its steps takes.
    /// </summary>
    public async Task<FlowRun> Run(PlannedRun p, PeopleWorkflows flows, CancellationToken stop)
    {
        var who = p.By.Who();
        FlowRun Refused(string why) => new()
        {
            Id = p.Id, At = clock.GetUtcNow().UtcDateTime, By = p.By.Name, User = p.User, UserName = p.Name, State = "failed",
            Steps = [new FlowStepResult { Label = "Before the first step", State = "failed", Detail = why }],
        };
        if (flows.Find(p.Flow) is not { On: true } flow)
        {
            return Refused($"The workflow {p.FlowName} was deleted or disabled.");
        }
        var allowed = roles.Allowed(who);
        if (flow.Steps.Select(s => PeopleWorkflows.KindOf(s.Kind).Permission).Distinct().FirstOrDefault(x => !allowed.Contains(x)) is { } missing)
        {
            return Refused($"{p.By.Name} no longer has the {Offboarding.PermissionName(missing)} permission.");
        }
        try
        {
            return await Run(who, p.By.Name, null, flow, p.User, p.Values, stop);
        }
        catch (Exception e) when (e is ToolProblem or DirectoryProblem or LdapRefused)
        {
            return Refused(e.Message);
        }
    }

    private async Task<FlowRun> Run(Who who, string by, string? from, PeopleFlow flow, string? target, IReadOnlyDictionary<string, string> given, CancellationToken stop)
    {
        var values = new Dictionary<string, string>(given, StringComparer.OrdinalIgnoreCase);
        var run = new FlowRun { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(), At = clock.GetUtcNow().UtcDateTime, By = by };
        string? user = null;
        if (flow.Steps[0].Kind != "create")
        {
            var u = await As(who, Permissions.AdView, () => tools.User(target ?? throw new ToolProblem("Select the user to run the workflow on."), stop));
            (user, run.User, run.UserName) = (u.Username, u.Username, u.Name ?? u.Username);
            values["Logon name"] = u.Username;
            values["Name"] = u.Name ?? u.Username;
        }
        foreach (var step in flow.Steps)
        {
            var kind = PeopleWorkflows.KindOf(step.Kind);
            var result = new FlowStepResult { Kind = step.Kind, Label = kind.Label };
            try
            {
                result.Detail = await Do(step, kind, values, who, by, from, run, user, stop);
                user = run.User ?? user;
                result.State = "done";
            }
            catch (Exception e) when (e is ToolProblem or DirectoryProblem or LdapRefused)
            {
                result.State = "failed";
                result.Detail = e.Message;
            }
            run.Steps.Add(result);
            if (result.State == "failed")
            {
                break; // the rest don't run
            }
        }
        // The steps not reached.
        foreach (var step in flow.Steps.Skip(run.Steps.Count))
        {
            run.Steps.Add(new FlowStepResult { Kind = step.Kind, Label = PeopleWorkflows.KindOf(step.Kind).Label, State = "skipped", Detail = "Not run, because an earlier step failed." });
        }
        run.State = run.Steps.Any(s => s.State == "failed") ? "failed" : "done";
        log.Record(by, from, "users", "ad.workflow.run", $"Ran the workflow {flow.Name}{(run.UserName is { } n ? $" for {n}" : "")}", run.State == "done" ? "ok" : "failed",
            run.State == "done" ? $"{run.Steps.Count} steps" : string.Join(" ", run.Steps.Where(s => s.State == "failed").Select(s => $"{s.Label}: {s.Detail}")));
        return run;
    }

    // The person's roles for this permission, and the change journal, for as long as work runs.
    private Task<T> As<T>(Who who, string permission, Func<Task<T>> work, bool note = false, bool confirm = false)
    {
        Caller.Set(new Caller(who, permission, roles));
        ChangeNote.Set(note ? new ChangeNote(journal, permission, confirm) : null);
        return work();
    }

    private async Task<string> Do(FlowStep step, PeopleWorkflows.Kind kind, Dictionary<string, string> values, Who who, string by, string? from, FlowRun run, string? user, CancellationToken stop)
    {
        string V(string key) => PeopleWorkflows.Fill(step.Values.GetValueOrDefault(key), values);
        var u = user ?? (step.Kind == "create" ? "" : throw new ToolProblem("There is no user to run this step on."));
        // A change that Active Directory keeps: its own log entry and journal entry, as if made by hand.
        async Task Changed(string action, string title, Func<Task> work, string? detail = null)
        {
            await As(who, kind.Permission, async () => { await work(); return true; }, note: true, confirm: kind.Confirm);
            log.Record(by, from, "users", action, title, "ok", detail);
            if (ChangeNote.Current?.Kept is { } c)
            {
                run.Changes.Add(c.Id);
            }
        }
        // A user by logon name, or the DN of one, as a field gives it.
        async Task<AdUser> Person(string text) => await As(who, Permissions.AdView, () => tools.User(text.Contains('=') ? Ldap.Name(text) : text, stop));
        switch (step.Kind)
        {
            case "create":
            {
                var name = V("name");
                var logon = V("logon").Replace(" ", "").ToLowerInvariant();
                var (first, last) = name.Split(' ', 2) is [var a, var b] ? (a, b) : (name, "");
                AdUser? template = V("template") is { Length: > 0 } t ? await Person(t) : null;
                var ou = V("ou") is { Length: > 0 } o ? o : template is null ? null : Ldap.Parent(template.Dn);
                AdUser made = null!;
                await Changed("ad.create", $"Created the AD user {logon}", async () =>
                {
                    (made, _) = await tools.Create(new NewAdUser(first, last, name, logon, ou, null, true, true, false, null, null, template?.Title,
                        template?.Groups.Select(g => g.Dn).ToList()), Passwords.Make(), stop);
                });
                (run.User, run.UserName, run.Created) = (made.Username, made.Name ?? made.Username, true);
                values["Logon name"] = made.Username;
                values["Name"] = made.Name ?? made.Username;
                if (template is not null && (template.Department ?? template.Company ?? template.Office) is not null)
                {
                    await Changed("ad.edit", $"Edited properties of {made.Username}", () => actions.Edit(made.Username,
                        new UserEdit(null, null, null, null, template.Office, null, null, null, template.Department, template.Company, null), stop), "Copied from the template user");
                }
                return $"Created {made.Name} ({made.Username}) in {made.Where}{(template is null ? "" : $", with the groups of {template.Name}")}. The password is random and must be changed at next sign-in.";
            }
            case "groups":
            {
                var dns = V("groups").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (dns.Count == 0)
                {
                    return "No groups were given.";
                }
                string? warning = null;
                await Changed("ad.groups", $"Changed group membership for {u}: added to {string.Join(", ", dns.Select(Ldap.Name))}", async () => warning = await tools.ChangeGroups(u, dns, [], stop));
                return warning ?? $"Added to {string.Join(", ", dns.Select(Ldap.Name))}.";
            }
            case "ungroup":
            {
                var me = await Person(u);
                if (me.Groups.Count == 0)
                {
                    return "Not in any groups other than the primary group.";
                }
                await Changed("ad.groups", $"Changed group membership for {u}: removed from {string.Join(", ", me.Groups.Select(g => g.Name))}",
                    () => tools.ChangeGroups(u, [], [.. me.Groups.Select(g => g.Dn)], stop));
                return $"Removed from {me.Groups.Count} {(me.Groups.Count == 1 ? "group" : "groups")}.";
            }
            case "manager":
            {
                var boss = V("manager") is { Length: > 0 } m ? await Person(m) : null;
                await Changed("ad.edit", $"Edited properties of {u}", () => actions.Edit(u, new UserEdit(null, null, null, null, null, null, null, null, null, null, boss?.Dn ?? ""), stop),
                    boss is null ? "Cleared the manager" : $"Manager set to {boss.Name}");
                return boss is null ? "Cleared the manager." : $"The manager is {boss.Name}.";
            }
            case "props":
            {
                string? Or(string k) => V(k) is { Length: > 0 } x ? x : null;
                await Changed("ad.edit", $"Edited properties of {u}", () => actions.Edit(u, new UserEdit(null, null, null, null, Or("office"), null, null, Or("title"), Or("department"), Or("company"), null), stop));
                return "The properties are set.";
            }
            case "move":
            {
                var where = "";
                await Changed("ad.move", $"Moved {u}", async () => where = await actions.Move(u, V("ou"), stop));
                return $"Moved to {where}.";
            }
            case "enable":
            case "disable":
            {
                var on = step.Kind == "enable";
                await Changed(on ? "ad.enable" : "ad.disable", on ? $"Enabled {u}" : $"Disabled {u}", () => tools.Enable(u, on, stop));
                return on ? "The account is enabled." : "The account is disabled.";
            }
            case "password":
                await Changed("ad.password", $"Reset the password for {u} (must change at next sign-in)", () => tools.Password(u, Passwords.Make(), true, stop));
                return "The password was reset to a random value that was not shown or kept, and must be changed at next sign-in.";
            case "expire":
            {
                if (!DateOnly.TryParse(V("date"), CultureInfo.InvariantCulture, out var day))
                {
                    throw new ToolProblem($"“{V("date")}” is not a date. Use a date field, or a date such as 2026-12-31.");
                }
                await Changed("ad.expiry", $"Set {u}'s account to expire at the end of {day:yyyy-MM-dd}", () => actions.SetExpiry(u, day, stop));
                return $"The account expires at the end of {day:MMMM d, yyyy}.";
            }
            case "home":
            {
                var me = await Person(u);
                var path = V("path");
                if (me.More?.Sid is not { } sid)
                {
                    throw new ToolProblem("The user's SID could not be read, so the folder cannot be given to them.");
                }
                var made = await homes.Create(path, sid, stop);
                log.Record(by, from, "users", "ad.home.create", $"Created the home folder of {u}", "ok", made);
                return $"Created {made}. Set it as the home folder{(V("drive").Length > 0 ? $" on {V("drive")}" : "")} in the user's Profile tab.";
            }
            case "email":
            {
                var to = V("to");
                var address = to.Contains('@') ? to : (await As(who, Permissions.AdView, () => directory.MailOf(to.Contains('=') ? Ldap.Name(to) : to, stop)));
                if (address is null)
                {
                    throw new ToolProblem($"{to} has no email address in Active Directory.");
                }
                var said = await mail.Send(address, new MailNote(V("subject"), "note", [("Message", V("text") is { Length: > 0 } x ? x : V("subject"))]), stop);
                return said is null ? $"Emailed {address}." : throw new ToolProblem($"Could not email {address}: {said}");
            }
            default:
                throw new ToolProblem("This step is not supported.");
        }
    }
}

public static class PeopleWorkflowsApi
{
    public static void MapPeopleWorkflowsApi(this WebApplication app)
    {
        app.MapGet("/api/ad/workflows", (HttpContext context, PeopleWorkflows flows) => Access.Can(context, Permissions.AdView)
            ? Results.Ok(new
            {
                flows = flows.All().Select(f => new { f.Id, f.Name, f.On, f.Fields, f.By, f.Changed, steps = f.Steps.Select(s => new { s.Kind, s.Values, label = PeopleWorkflows.KindOf(s.Kind).Label, sentence = PeopleWorkflows.Sentence(s) }), f.Runs }),
                kinds = PeopleWorkflows.Kinds.Select(k => new { k.Key, k.Label, k.Values, k.Permission }), fieldKinds = PeopleWorkflows.FieldKinds,
            })
            : Access.NotAllowed());

        app.MapPost("/api/ad/workflows", (FlowForm form, HttpContext context, PeopleWorkflows flows, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AdWorkflows) is { } refused)
            {
                return refused;
            }
            if (PeopleWorkflows.Bad(form) is { } bad)
            {
                return Results.BadRequest(new { error = bad });
            }
            var made = form.Id is not { Length: > 0 };
            var flow = flows.Save(form, ActivityLog.Who(context));
            log.Record(context, "users", made ? "ad.workflow.create" : "ad.workflow.edit", $"{(made ? "Created" : "Edited")} the workflow {flow.Name}");
            return Results.Ok(new { id = flow.Id });
        });

        app.MapPost("/api/ad/workflows/{id}/delete", (string id, HttpContext context, PeopleWorkflows flows, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AdWorkflows) is { } refused)
            {
                return refused;
            }
            var flow = flows.Find(id);
            if (flow is null || !flows.Delete(id))
            {
                return Results.NotFound(new { error = "The workflow no longer exists." });
            }
            log.Record(context, "users", "ad.workflow.delete", $"Deleted the workflow {flow.Name}");
            return Results.Ok(new { });
        });

        // A run: each step's permission first, Confirm it's you when a step needs it, then the steps in order. With a
        // time that's still to come it is saved for then instead (the Lifecycle page lists it), and runs as whoever saved it.
        app.MapPost("/api/ad/workflows/{id}/run", async (string id, FlowRunForm form, HttpContext context, PeopleWorkflows flows, FlowRunner runner, IDirectoryTools tools,
            ActivityLog log, TimeProvider clock) =>
        {
            if (flows.Find(id) is not { } flow)
            {
                return Results.NotFound(new { error = "The workflow no longer exists." });
            }
            if (!flow.On)
            {
                return Results.BadRequest(new { error = $"{flow.Name} is disabled. Enable it to run it." });
            }
            var kinds = flow.Steps.Select(s => PeopleWorkflows.KindOf(s.Kind)).ToList();
            if (kinds.Select(k => k.Permission).Distinct().FirstOrDefault(p => !Access.Can(context, p)) is { } missing)
            {
                return Results.Json(new { error = $"Access denied. This workflow requires the {Offboarding.PermissionName(missing)} permission." }, statusCode: StatusCodes.Status403Forbidden);
            }
            var values = (form.Values ?? []).ToDictionary(v => v.Key.Trim(), v => (v.Value ?? "").Trim(), StringComparer.OrdinalIgnoreCase);
            if (flow.Fields.FirstOrDefault(f => f.Required && string.IsNullOrWhiteSpace(values.GetValueOrDefault(f.Label))) is { } empty)
            {
                return Results.BadRequest(new { error = $"Enter {empty.Label}." });
            }
            if (flow.Fields.FirstOrDefault(f => f.Kind == "choice" && values.GetValueOrDefault(f.Label) is { Length: > 0 } v && !f.Choices.Contains(v)) is { } odd)
            {
                return Results.BadRequest(new { error = $"Pick one of the choices for {odd.Label}." });
            }
            if (form.When?.ToUniversalTime() is { } when && when > clock.GetUtcNow().UtcDateTime.AddMinutes(1))
            {
                return await UsersApi.Try(context, Permissions.AdView, async () =>
                {
                    // The user it runs on is read now, so a name that isn't there is said today and not on the day.
                    var user = flow.Steps[0].Kind == "create" ? null
                        : (await tools.User(form.User?.Trim() is { Length: > 0 } u ? u : throw new ToolProblem("Select the user to run the workflow on."), context.RequestAborted));
                    var who = Who.Of(context.User);
                    var p = flows.Plan(flow, user?.Username, user is null ? PeopleWorkflows.NameFor(flow, null, values) : user.Name ?? user.Username, values, when,
                        new OffboardWho { Source = who.Source, Username = who.Username, Domain = who.Domain, Groups = [.. who.Groups], Name = ActivityLog.Who(context) });
                    log.Record(context, "users", "ad.workflow.plan", $"Scheduled the workflow {flow.Name} for {p.Name}", detail: $"Runs {when:yyyy-MM-dd HH:mm} UTC as {p.By.Name}.");
                    return Results.Ok(new { planned = p.Id, when = p.When, name = p.Name });
                }, change: false, confirm: kinds.Any(k => k.Confirm));
            }
            return await UsersApi.Try(context, Permissions.AdView, async () =>
            {
                var run = await runner.Run(context, flow, form.User?.Trim(), values, CancellationToken.None);
                flows.Ran(flow.Id, run);
                return Results.Ok(run);
            }, confirm: kinds.Any(k => k.Confirm));
        });
    }
}

/// <summary>Runs the workflow runs saved for a date once they're due, looking every 30 seconds.</summary>
public sealed class PlannedRunWorker(PeopleWorkflows flows, FlowRunner runner, TimeProvider clock, ILogger<PlannedRunWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        flows.Recover();
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), clock, stop);
            try
            {
                await Tick(stop);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "The scheduled workflow runs that are due could not be run");
            }
        }
    }

    public async Task<int> Tick(CancellationToken stop)
    {
        var due = flows.Due();
        foreach (var p in due)
        {
            var run = await runner.Run(p, flows, stop);
            flows.Finish(p.Id, run);
            // One that ran its steps is among the workflow's recent runs, where it can be undone as one.
            if (run.Steps.Any(s => s.Kind.Length > 0))
            {
                flows.Ran(p.Flow, run);
            }
        }
        return due.Count;
    }
}
