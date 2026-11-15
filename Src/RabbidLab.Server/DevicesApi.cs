namespace RabbidLab.Server;

/// <summary>
/// Doing things on the computers in Devices: the script library, running a script or an action on computers, the jobs
/// and their output, and workflows. Everything that puts code on a computer takes its own permission and a fresh
/// confirmation of who's asking (Confirm.cs), and is written to the activity log with the SHA-256 of what ran.
/// </summary>
public static class DevicesApi
{
    /// <summary>More computers than this in one run and the page has to send how many, typed by the person.</summary>
    public const int Brake = 10;

    /// <summary>How long a computer that's offline has to take a job someone ran by hand.</summary>
    public static readonly TimeSpan Life = TimeSpan.FromHours(24);

    private static readonly string[] Doing = [Permissions.SeatsActions, Permissions.ScriptsRun, Permissions.ScriptsEdit, Permissions.Workflows];

    public static void MapDevicesApi(this WebApplication app)
    {
        // Whether this session confirmed who it is lately, and what it's asked for when it hasn't.
        app.MapGet("/api/confirm", (HttpContext context, Confirmations confirm) =>
            Results.Ok(new { until = confirm.Until(context), method = confirm.Method(context.User) }));

        app.MapPost("/api/confirm", async (ConfirmForm form, HttpContext context, Confirmations confirm, ActivityLog log) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            if (await confirm.Confirm(context, form.Code, form.Password, context.RequestAborted) is { } problem)
            {
                log.Record(context, "access", "confirm.failed", "Didn't confirm who they are", "failed", problem);
                return Results.BadRequest(new { error = problem });
            }
            return Results.Ok(new { until = confirm.Until(context) });
        });

        // The script library, for whoever may run or write scripts, or put them in workflows.
        app.MapGet("/api/devices/scripts", (HttpContext context, DeviceScripts scripts, Workflows workflows) =>
            !Seeing(context, Permissions.ScriptsRun, Permissions.ScriptsEdit, Permissions.Workflows) ? Access.NotAllowed()
            : Results.Ok(scripts.All().Select(s => new
            {
                id = s.Id, name = s.Name, about = s.About, language = s.Language, text = s.Text, minutes = s.Minutes, by = s.By, changed = s.Changed,
                workflows = workflows.Using(s.Id).Select(w => w.Name),
            })));

        app.MapPost("/api/devices/scripts", (ScriptForm form, HttpContext context, DeviceScripts scripts, Confirmations confirm, ActivityLog log) =>
        {
            if ((Access.RefuseUnless(context, Permissions.ScriptsEdit) ?? confirm.Refuse(context)) is { } refused)
            {
                return refused;
            }
            var (script, problem) = scripts.Save(form.Id, form.Name, form.About, form.Language, form.Text, form.Minutes, ActivityLog.Who(context));
            if (script is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "clients", "script.save", $"{(string.IsNullOrEmpty(form.Id) ? "Added" : "Changed")} the script {script.Name}", "ok", $"SHA-256 {DeviceActions.Fingerprint([Workflows.Step(script)])}");
            return Results.Ok(new { id = script.Id });
        });

        app.MapPost("/api/devices/scripts/{id}/delete", (string id, HttpContext context, DeviceScripts scripts, Workflows workflows, Confirmations confirm, ActivityLog log) =>
        {
            if ((Access.RefuseUnless(context, Permissions.ScriptsEdit) ?? confirm.Refuse(context)) is { } refused)
            {
                return refused;
            }
            if (workflows.Using(id) is { Count: > 0 } used)
            {
                return Results.BadRequest(new { error = $"The workflow {used[0].Name} runs this script. Take it out of the workflow first." });
            }
            if (scripts.Delete(id) is not { } gone)
            {
                return Results.NotFound(new { error = "That script isn't here any more." });
            }
            log.Record(context, "clients", "script.delete", $"Deleted the script {gone.Name}");
            return Results.Ok();
        });

        // Runs a library script, a script typed in, a restart or Windows updates on the computers picked.
        app.MapPost("/api/devices/run", (RunForm form, HttpContext context, Clients clients, DeviceScripts scripts, DeviceJobs jobs, Confirmations confirm, ActivityLog log) =>
        {
            var typed = form.Kind == DeviceJobs.Script && string.IsNullOrEmpty(form.Script);
            var needs = form.Kind switch
            {
                DeviceJobs.Script => typed ? Permissions.ScriptsEdit : Permissions.ScriptsRun,
                DeviceJobs.Restart or DeviceJobs.Updates => Permissions.SeatsActions,
                _ => null,
            };
            if (needs is null)
            {
                return Results.BadRequest(new { error = "Say what to run." });
            }
            if ((Access.RefuseUnless(context, needs) ?? confirm.Refuse(context)) is { } refused)
            {
                return refused;
            }
            List<Client> on = [.. (form.Clients ?? []).Distinct().Select(clients.Find).OfType<Client>().Where(c => c.Approved is not null)];
            if (on.Count == 0)
            {
                return Results.BadRequest(new { error = "Pick at least one approved computer." });
            }
            if (on.Count > Brake && form.Count != on.Count)
            {
                return Results.BadRequest(new { error = $"That's {on.Count} computers. Type {on.Count} to run it on all of them.", count = on.Count });
            }
            JobStep step;
            string title;
            if (form.Kind == DeviceJobs.Restart)
            {
                (step, title) = (DeviceActions.Restart(), "Restart");
            }
            else if (form.Kind == DeviceJobs.Updates)
            {
                (step, title) = (DeviceActions.InstallUpdates(), "Install Windows updates");
            }
            else if (typed)
            {
                if (DeviceScripts.Problem(form.Language, form.Text) is { } problem)
                {
                    return Results.BadRequest(new { error = problem });
                }
                title = "A script typed in";
                step = new JobStep { Title = title, Language = form.Language!, Text = DeviceScripts.Clean(form.Text!), Minutes = DeviceScripts.Minutes(form.Minutes) };
            }
            else if (scripts.Find(form.Script) is { } script)
            {
                (step, title) = (Workflows.Step(script), script.Name);
            }
            else
            {
                return Results.NotFound(new { error = "That script isn't here any more." });
            }
            var job = jobs.Add(ActivityLog.Who(context), form.Kind!, title, [step], on, Life, typed ? null : form.Script);
            log.Record(context, "clients", "job.run", form.Kind == DeviceJobs.Script ? $"Ran {(typed ? "a script typed in" : $"the script {title}")} on {Workflows.Computers(on)}"
                : $"{(form.Kind == DeviceJobs.Restart ? "Restarted" : "Installed Windows updates on")} {Workflows.Computers(on)}", "ok", $"Job {job.Id}, SHA-256 {DeviceActions.Fingerprint([step])}");
            return Results.Ok(new { id = job.Id });
        });

        // The jobs, newest first: one line each, or with ?client= the ones for one computer.
        app.MapGet("/api/devices/jobs", (string? client, HttpContext context, DeviceJobs jobs) =>
            !Seeing(context, Doing) ? Access.NotAllowed() : Results.Ok(jobs.Read(all => all.Where(j => client is null || j.Targets.Any(t => t.Client == client)).Select(j => new
            {
                id = j.Id, at = j.At, by = j.By, kind = j.Kind, title = j.Title, workflow = j.Workflow, expires = j.Expires,
                computers = j.Targets.Count,
                names = j.Targets.Take(3).Select(t => t.Computer).ToList(),
                states = j.Targets.GroupBy(t => t.State).ToDictionary(g => g.Key, g => g.Count()),
                state = client is null ? null : j.Targets.First(t => t.Client == client).State,
            }).ToList())));

        // One job: exactly what ran, and how it went on each computer. A computer's output is asked for by itself.
        app.MapGet("/api/devices/jobs/{id}", (string id, HttpContext context, DeviceJobs jobs) =>
            !Seeing(context, Doing) ? Access.NotAllowed() : jobs.Read(all => all.FirstOrDefault(j => j.Id == id) is not { } j ? null : new
            {
                id = j.Id, at = j.At, by = j.By, kind = j.Kind, title = j.Title, workflow = j.Workflow, expires = j.Expires,
                steps = j.Steps.Select(s => new { title = s.Title, language = s.Language, text = s.Text, minutes = s.Minutes }).ToList(),
                targets = j.Targets.Select(t => new
                {
                    client = t.Client, computer = t.Computer, state = t.State, sent = t.Sent, finished = t.Finished, problem = t.Problem,
                    steps = t.Steps.Select(s => new { exit = s.Exit, seconds = s.Seconds, timedOut = s.TimedOut }).ToList(),
                }).ToList(),
            }) is { } job ? Results.Ok(job) : Results.NotFound(new { error = "That job isn't kept any more." }));

        app.MapGet("/api/devices/jobs/{id}/output/{client}", (string id, string client, HttpContext context, DeviceJobs jobs) =>
            !Seeing(context, Doing) ? Access.NotAllowed() : Results.Ok(new { output = jobs.Output(id, client) ?? [] }));

        // Takes a job back from the computers that haven't taken it yet.
        app.MapPost("/api/devices/jobs/{id}/cancel", (string id, HttpContext context, DeviceJobs jobs, ActivityLog log) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            if (!Seeing(context, Doing))
            {
                return Access.NotAllowed();
            }
            var cancelled = jobs.Cancel(id);
            if (cancelled > 0)
            {
                log.Record(context, "clients", "job.cancel", $"Cancelled a job for {cancelled} computer{(cancelled == 1 ? "" : "s")} that hadn't taken it", "ok", $"Job {id}");
            }
            return Results.Ok(new { cancelled });
        });

        app.MapGet("/api/devices/workflows", (HttpContext context, Workflows workflows) =>
            !Seeing(context, Permissions.Workflows) ? Access.NotAllowed() : Results.Ok(workflows.All().Select(w => new
            {
                id = w.Id, name = w.Name, on = w.On, when = w.When, days = w.Days, time = w.Time, below = w.Below, @if = w.If, ifText = w.IfText,
                steps = w.Steps.Select(s => new { kind = s.Kind, script = s.Script }), by = w.By, changed = w.Changed, lastRun = w.LastRun, lastJob = w.LastJob,
            })));

        // A workflow can only do what the person saving it may do by hand.
        app.MapPost("/api/devices/workflows", (WorkflowForm form, HttpContext context, Workflows workflows, Confirmations confirm, ActivityLog log) =>
        {
            if ((Access.RefuseUnless(context, Permissions.Workflows) ?? confirm.Refuse(context)) is { } refused)
            {
                return refused;
            }
            if ((form.Steps ?? []).Any(s => !Access.Can(context, s.Kind == Workflows.RunScript ? Permissions.ScriptsRun : Permissions.SeatsActions)))
            {
                return Results.Json(new { error = "Your roles don't let you do one of these steps by hand, so you can't put it in a workflow." }, statusCode: StatusCodes.Status403Forbidden);
            }
            var (w, problem) = workflows.Save(form, ActivityLog.Who(context));
            if (w is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "clients", "workflow.save", $"{(string.IsNullOrEmpty(form.Id) ? "Added" : "Changed")} the workflow {w.Name}", "ok", w.On ? "Enabled" : "Disabled");
            return Results.Ok(new { id = w.Id });
        });

        app.MapPost("/api/devices/workflows/{id}/on", (string id, WorkflowOnForm form, HttpContext context, Workflows workflows, Confirmations confirm, ActivityLog log) =>
        {
            // Turning one off is never held up.
            if ((Access.RefuseUnless(context, Permissions.Workflows) ?? (form.On ? confirm.Refuse(context) : null)) is { } refused)
            {
                return refused;
            }
            if (workflows.Enable(id, form.On) is not { } w)
            {
                return Results.NotFound(new { error = "That workflow isn't here any more." });
            }
            log.Record(context, "clients", "workflow.on", $"{(form.On ? "Enabled" : "Disabled")} the workflow {w.Name}");
            return Results.Ok();
        });

        app.MapPost("/api/devices/workflows/{id}/run", (string id, HttpContext context, Workflows workflows, Confirmations confirm) =>
        {
            if ((Access.RefuseUnless(context, Permissions.Workflows) ?? confirm.Refuse(context)) is { } refused)
            {
                return refused;
            }
            if (workflows.Find(id) is not { } w)
            {
                return Results.NotFound(new { error = "That workflow isn't here any more." });
            }
            return workflows.RunNow(w, ActivityLog.Who(context)) is { } job ? Results.Ok(new { id = job.Id, computers = job.Targets.Count })
                : Results.BadRequest(new { error = "It didn't run: no approved computer fits it, or one of its scripts was deleted." });
        });

        app.MapPost("/api/devices/workflows/{id}/delete", (string id, HttpContext context, Workflows workflows, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.Workflows) is { } refused)
            {
                return refused;
            }
            if (workflows.Delete(id) is not { } gone)
            {
                return Results.NotFound(new { error = "That workflow isn't here any more." });
            }
            log.Record(context, "clients", "workflow.delete", $"Deleted the workflow {gone.Name}");
            return Results.Ok();
        });

        // The client's side. A client told jobs wait for it (ClientCommands) takes them here, each once, and answers below.
        app.MapPost("/api/clients/jobs/take", (HttpContext context, Clients clients, DeviceJobs jobs) =>
            ClientsApi.Caller(context, clients) is not { Approved: not null } c ? ClientsApi.Unknown()
            : Results.Ok(new { jobs = jobs.Take(c.Id).Select(j => new { id = j.Id, steps = j.Steps.Select(s => new { language = s.Language, text = s.Text, minutes = s.Minutes }) }) }));

        app.MapPost("/api/clients/jobs/done", (JobReport report, HttpContext context, Clients clients, DeviceJobs jobs) =>
            ClientsApi.Caller(context, clients) is not { } c ? ClientsApi.Unknown() : jobs.Finish(c.Id, report) ? Results.Ok() : Results.NotFound());
    }

    private static bool Seeing(HttpContext context, params string[] any) => any.Any(p => Access.Can(context, p));
}

public sealed record ScriptForm(string? Id, string? Name, string? About, string? Language, string? Text, int? Minutes);

/// <summary>What to run and where: a library script by id, or one typed in (Language, Text), or an action. Count is the brake's answer.</summary>
public sealed record RunForm(string? Kind, List<string>? Clients, string? Script = null, string? Language = null, string? Text = null, int? Minutes = null, int? Count = null);

public sealed record WorkflowOnForm(bool On);
