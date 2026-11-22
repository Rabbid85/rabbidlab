using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>One step of a workflow: a script from the library, or one of the built-in actions.</summary>
public sealed class WorkflowStep
{
    public string Kind { get; set; } = Workflows.RunScript;

    /// <summary>The library script's id, for a script step.</summary>
    public string? Script { get; set; }

    /// <summary>The app's id on the Installs page, for an install step.</summary>
    public string? App { get; set; }
}

/// <summary>Steps that run on computers by themselves: when something happens or on a schedule, and only on computers that fit.</summary>
public sealed class Workflow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool On { get; set; }

    /// <summary>What starts it (Workflows.Approved and the rest).</summary>
    public string When { get; set; } = Workflows.Approved;

    /// <summary>On a schedule: the days of the week (0 is Sunday) and the time, by this server's clock.</summary>
    public List<int> Days { get; set; } = [];
    public string? Time { get; set; }

    /// <summary>Free space on C: below this many percent.</summary>
    public int Below { get; set; } = 10;

    /// <summary>Which computers it's for (Workflows.Always and the rest), and the words that takes.</summary>
    public string If { get; set; } = Workflows.Always;
    public string? IfText { get; set; }

    public List<WorkflowStep> Steps { get; set; } = [];
    public string? By { get; set; }
    public DateTime Changed { get; set; }
    public DateTime? LastRun { get; set; }
    public string? LastJob { get; set; }

    /// <summary>The scheduled time it last ran for, so one time runs once.</summary>
    public string? LastSlot { get; set; }
}

public sealed class WorkflowsFile
{
    public List<Workflow> Workflows { get; set; } = [];
}

/// <summary>What the page sends to add or change a workflow.</summary>
public sealed record WorkflowForm(string? Id, string? Name, bool On, string? When, List<int>? Days, string? Time, int? Below, string? If, string? IfText, List<WorkflowStep>? Steps);

/// <summary>
/// Workflows, kept in workflows.json. One starts when its trigger happens to a computer (approved, back online, updates
/// missing, a restart needed, C: running low) or at its scheduled time for every computer, and queues its steps as one
/// job (DeviceJobs), so they run in order on each computer and stop at the first that fails.
/// </summary>
public sealed class Workflows(DataFolder folder, TimeProvider clock, DeviceScripts scripts, DeviceApps apps, DeviceJobs jobs, Clients clients, ActivityLog log)
{
    public const string Approved = "approved";
    public const string Schedule = "schedule";
    public const string Online = "online";
    public const string NeedsUpdates = "updates";
    public const string NeedsRestart = "restart";
    public const string LowDisk = "disk";
    public static readonly IReadOnlyList<string> Triggers = [Approved, Schedule, Online, NeedsUpdates, NeedsRestart, LowDisk];

    public const string Always = "always";
    public const string NameLike = "name";
    public const string WindowsHas = "windows";
    public const string WindowsHasNot = "notwindows";
    public const string NobodySignedIn = "nobody";
    public static readonly IReadOnlyList<string> Conditions = [Always, NameLike, WindowsHas, WindowsHasNot, NobodySignedIn];

    public const string RunScript = "script";
    public const string InstallUpdates = "updates";
    public const string Restart = "restart";
    public const string RestartIfNeeded = "restart-if-needed";
    public const string InstallApp = "install";
    public static readonly IReadOnlyList<string> StepKinds = [RunScript, InstallUpdates, Restart, RestartIfNeeded, InstallApp];

    public const int MaxWorkflows = 100;
    public const int MaxSteps = 20;

    /// <summary>How long a computer that was offline has to take what a workflow queued for it.</summary>
    public static readonly TimeSpan EventLife = TimeSpan.FromHours(24);
    public static readonly TimeSpan ScheduleLife = TimeSpan.FromHours(4);

    /// <summary>The same workflow isn't started for the same computer again this soon, so a computer going up and down doesn't run it over and over.</summary>
    public static readonly TimeSpan Again = TimeSpan.FromMinutes(10);

    /// <summary>After this server starts, every computer comes back at once: that isn't each of them coming online.</summary>
    public static readonly TimeSpan Settling = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<WorkflowsFile> file = folder.Json<WorkflowsFile>("workflows.json", Json);
    private readonly Dictionary<(string Workflow, string Client), DateTime> started = [];
    private readonly DateTime since = clock.GetUtcNow().UtcDateTime;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public IReadOnlyList<Workflow> All()
    {
        lock (file.Gate)
        {
            return [.. file.Read().Workflows.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)];
        }
    }

    public Workflow? Find(string? id)
    {
        lock (file.Gate)
        {
            return file.Read().Workflows.FirstOrDefault(w => w.Id == id);
        }
    }

    /// <summary>The workflows a library script is a step of.</summary>
    public IReadOnlyList<Workflow> Using(string script) => [.. All().Where(w => w.Steps.Any(s => s.Kind == RunScript && s.Script == script))];

    /// <summary>The workflows that install an app from the Installs page.</summary>
    public IReadOnlyList<Workflow> Installing(string app) => [.. All().Where(w => w.Steps.Any(s => s.Kind == InstallApp && s.App == app))];

    /// <summary>Adds a workflow, or changes the one with this id.</summary>
    public (Workflow? Workflow, string? Problem) Save(WorkflowForm form, string by)
    {
        var name = form.Name?.Trim() ?? "";
        if (name.Length is 0 or > 80)
        {
            return (null, name.Length == 0 ? "Workflow name is required." : "Names can be up to 80 characters.");
        }
        if (!Triggers.Contains(form.When))
        {
            return (null, "Select a trigger.");
        }
        var days = (form.Days ?? []).Where(d => d is >= 0 and <= 6).Distinct().Order().ToList();
        if (form.When == Schedule && (days.Count == 0 || !TimeOnly.TryParseExact(form.Time, "HH:mm", out _)))
        {
            return (null, days.Count == 0 ? "Select at least one day." : "Enter the time as HH:mm, for example 02:00.");
        }
        if (!Conditions.Contains(form.If ?? Always))
        {
            return (null, "Select which computers the workflow applies to.");
        }
        var words = form.IfText?.Trim() ?? "";
        if (form.If is (NameLike or WindowsHas or WindowsHasNot) && words.Length is 0 or > 100)
        {
            return (null, form.If == NameLike ? "Enter a computer name pattern, for example SHOP-* or *-LT." : "Enter text to match in the Windows edition, for example Server.");
        }
        var steps = form.Steps ?? [];
        if (steps.Count is 0 or > MaxSteps)
        {
            return (null, steps.Count == 0 ? "Add at least one step." : $"Workflows can have up to {MaxSteps} steps.");
        }
        foreach (var step in steps)
        {
            if (!StepKinds.Contains(step.Kind) || (step.Kind == RunScript && scripts.Find(step.Script) is null))
            {
                return (null, step.Kind == RunScript ? "Select a script for each Run script step." : "One of the steps is not supported in a workflow.");
            }
            if (step.Kind == InstallApp && apps.Find(step.App) is null)
            {
                return (null, "Select an app for each Install an app step.");
            }
        }
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Workflows.Any(w => w.Id != form.Id && string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return (null, $"A workflow named {name} already exists.");
            }
            var w = f.Workflows.FirstOrDefault(x => x.Id == form.Id);
            if (w is null)
            {
                if (!string.IsNullOrEmpty(form.Id))
                {
                    return (null, "That workflow no longer exists.");
                }
                if (f.Workflows.Count >= MaxWorkflows)
                {
                    return (null, $"You can have up to {MaxWorkflows} workflows.");
                }
                w = new Workflow { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant() };
                f.Workflows.Add(w);
            }
            (w.Name, w.On, w.When, w.Days, w.Time) = (name, form.On, form.When!, days, form.When == Schedule ? form.Time : null);
            (w.Below, w.If, w.IfText) = (Math.Clamp(form.Below ?? 10, 1, 90), form.If ?? Always, words.Length > 0 ? words : null);
            w.Steps = [.. steps.Select(s => new WorkflowStep { Kind = s.Kind, Script = s.Kind == RunScript ? s.Script : null, App = s.Kind == InstallApp ? s.App : null })];
            (w.By, w.Changed) = (by, Now);
            file.Save(f);
            return (w, null);
        }
    }

    public Workflow? Enable(string id, bool on) => Change(id, w => w.On = on);

    public Workflow? Delete(string id)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Workflows.FirstOrDefault(w => w.Id == id) is not { } gone)
            {
                return null;
            }
            f.Workflows.Remove(gone);
            file.Save(f);
            return gone;
        }
    }

    /// <summary>Whether a workflow is for this computer.</summary>
    public static bool Fits(Workflow w, Client c) => w.If switch
    {
        NameLike => Regex.IsMatch(c.Computer, "^" + Regex.Escape(w.IfText ?? "").Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
        WindowsHas => (c.Os ?? "").Contains(w.IfText ?? "", StringComparison.OrdinalIgnoreCase),
        WindowsHasNot => !(c.Os ?? "").Contains(w.IfText ?? "", StringComparison.OrdinalIgnoreCase),
        NobodySignedIn => string.IsNullOrWhiteSpace(c.User),
        _ => true,
    };

    /// <summary>Something happened to a computer: the workflows that start on it, and fit it, run on it.</summary>
    public void Happened(string trigger, Client c, Func<Workflow, bool>? which = null)
    {
        if (c.Approved is null)
        {
            return;
        }
        foreach (var w in All().Where(w => w.On && w.When == trigger && (which?.Invoke(w) ?? true) && Fits(w, c)))
        {
            lock (started)
            {
                if (started.TryGetValue((w.Id, c.Id), out var last) && Now - last < Again)
                {
                    continue;
                }
                started[(w.Id, c.Id)] = Now;
            }
            Run(w, [c], EventLife);
        }
    }

    /// <summary>A computer checked in after being away. Not counted while everything comes back after this server started.</summary>
    public void CameOnline(Client c)
    {
        if (Now - since >= Settling)
        {
            Happened(Online, c);
        }
    }

    /// <summary>A computer sent its inventory: what's newly wrong with it since the one before starts its workflows.</summary>
    public void InventoryChanged(Client c, DeviceInventory? before, DeviceInventory after)
    {
        if (after.MissingChecked is not null && after.Missing.Any(u => !u.Definition) && before?.Missing.Any(u => !u.Definition) != true)
        {
            Happened(NeedsUpdates, c);
        }
        if (after.RestartNeeded && before?.RestartNeeded != true)
        {
            Happened(NeedsRestart, c);
        }
        if (FreeOnC(after) is { } free)
        {
            var was = before is null ? null : FreeOnC(before);
            Happened(LowDisk, c, w => free < w.Below && (was is null || was >= w.Below));
        }
    }

    /// <summary>Free space on the Windows drive, as a percentage; null when the computer didn't say.</summary>
    public static double? FreeOnC(DeviceInventory i) =>
        i.Disks.FirstOrDefault(d => d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase)) is { Total: > 0 } c ? 100.0 * c.Free / c.Total : null;

    /// <summary>The scheduled workflows whose time has come, each once. Called every half minute.</summary>
    public void Tick()
    {
        var now = clock.GetLocalNow();
        foreach (var w in All().Where(w => w.On && w.When == Schedule && w.Days.Contains((int) now.DayOfWeek)))
        {
            if (!TimeOnly.TryParseExact(w.Time, "HH:mm", out var at))
            {
                continue;
            }
            // A time this server slept through by more than a few minutes isn't made up for later in the day.
            var late = now.TimeOfDay - at.ToTimeSpan();
            var slot = $"{now:yyyy-MM-dd}T{w.Time}";
            if (late < TimeSpan.Zero || late > TimeSpan.FromMinutes(5) || w.LastSlot == slot)
            {
                continue;
            }
            Change(w.Id, x => x.LastSlot = slot);
            Run(w, [.. clients.All().Where(c => c.Approved is not null && Fits(w, c))], ScheduleLife);
        }
    }

    /// <summary>Runs it now on every approved computer it's for. Null when there are none, or a step's script is gone.</summary>
    public DeviceJob? RunNow(Workflow w, string by) => Run(w, [.. clients.All().Where(c => c.Approved is not null && Fits(w, c))], EventLife, by);

    private DeviceJob? Run(Workflow w, IReadOnlyList<Client> on, TimeSpan life, string? by = null)
    {
        if (on.Count == 0)
        {
            return null;
        }
        List<JobStep> steps = [];
        foreach (var step in w.Steps)
        {
            var script = step.Kind == RunScript ? scripts.Find(step.Script) : null;
            var app = step.Kind == InstallApp ? apps.Find(step.App) : null;
            if ((step.Kind == RunScript && script is null) || (step.Kind == InstallApp && app is null))
            {
                log.Record("Workflow", null, "clients", "workflow.failed", $"The workflow {w.Name} did not run because one of its {(step.Kind == InstallApp ? "apps" : "scripts")} was deleted", "failed");
                return null;
            }
            steps.Add(step.Kind switch
            {
                InstallApp => DeviceApps.Step(app!),
                InstallUpdates => DeviceActions.InstallUpdates(),
                Restart => DeviceActions.Restart(),
                RestartIfNeeded => DeviceActions.RestartIfNeeded(),
                _ => Step(script!),
            });
        }
        var job = jobs.Add(by ?? "Workflow", DeviceJobs.Workflow, w.Name, steps, on, life, workflow: w.Id);
        Change(w.Id, x => (x.LastRun, x.LastJob) = (Now, job.Id));
        log.Record(by ?? "Workflow", null, "clients", "workflow.run", $"The workflow {w.Name} started on {Computers(on)}", "ok", $"Job {job.Id}, SHA-256 {DeviceActions.Fingerprint(steps)}");
        return job;
    }

    /// <summary>A library script as a job's step.</summary>
    public static JobStep Step(DeviceScript s) => new() { Title = s.Name, Language = s.Language, Text = s.Text, Minutes = s.Minutes };

    /// <summary>The computers by name when they're few, otherwise how many.</summary>
    public static string Computers(IReadOnlyList<Client> on) => on.Count <= 3 ? string.Join(", ", on.Select(c => c.Computer)) : $"{on.Count} computers";

    private Workflow? Change(string id, Action<Workflow> change)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Workflows.FirstOrDefault(w => w.Id == id) is not { } w)
            {
                return null;
            }
            change(w);
            file.Save(f);
            return w;
        }
    }
}

/// <summary>Every half minute: scheduled workflows whose time has come, and jobs that are overdue.</summary>
public sealed class WorkflowWorker(Workflows workflows, DeviceJobs jobs, TimeProvider clock, ILogger<WorkflowWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                jobs.Sweep();
                workflows.Tick();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Running workflows failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), clock, stop);
        }
    }
}
