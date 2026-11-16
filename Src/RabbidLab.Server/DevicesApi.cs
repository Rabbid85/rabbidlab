using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>
/// Doing things on the computers in Devices: the script library, running a script or an action on computers, the jobs
/// and their output, and workflows. Everything that puts code on a computer takes its own permission and a fresh
/// confirmation of who's asking (Confirm.cs), and is written to the activity log with the SHA-256 of what ran.
/// WMI queries are reads, so they take only their own permission and a line in the activity log.
/// </summary>
public static class DevicesApi
{
    /// <summary>More computers than this in one run and the page has to send how many, typed by the person.</summary>
    public const int Brake = 10;

    /// <summary>How long a computer that's offline has to take a job someone ran by hand.</summary>
    public static readonly TimeSpan Life = TimeSpan.FromHours(24);

    /// <summary>How long it has to take a lock, sign-out, shut down or message: those are for now, not tomorrow.</summary>
    public static readonly TimeSpan Soon = TimeSpan.FromMinutes(15);

    /// <summary>The first client that answers WMI queries.</summary>
    public static readonly Version WmiFrom = new(1, 1, 173);

    /// <summary>At most this many computers in one WMI query.</summary>
    public const int WmiMost = 50;

    private static readonly string[] Doing = [Permissions.SeatsActions, Permissions.ScriptsRun, Permissions.ScriptsEdit, Permissions.Workflows];

    public static void MapDevicesApi(this WebApplication app)
    {
        MapWmi(app);

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
                id = s.Id, name = s.Name, about = s.About, language = s.Language, text = s.Text, minutes = s.Minutes, by = s.By, changed = s.Changed, builtIn = s.BuiltIn,
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
            if (BuiltInScripts.Is(id))
            {
                return Results.BadRequest(new { error = "Built-in scripts can't be deleted." });
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
                DeviceJobs.Restart or DeviceJobs.Updates or DeviceJobs.Printer or DeviceJobs.Check
                    or DeviceJobs.Lock or DeviceJobs.SignOut or DeviceJobs.Shutdown or DeviceJobs.Message or DeviceJobs.Client or DeviceJobs.GpUpdate => Permissions.SeatsActions,
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
            if (form.Kind == DeviceJobs.Printer)
            {
                var (made, named, problem) = PrinterActions.Make(form.Printer);
                if (made is null)
                {
                    return Results.BadRequest(new { error = problem });
                }
                (step, title) = (made, named);
            }
            else if (form.Kind == DeviceJobs.Restart)
            {
                (step, title) = (DeviceActions.Restart(), "Restart");
            }
            else if (form.Kind == DeviceJobs.Client)
            {
                (step, title) = (DeviceActions.UpdateClient(), "Update the client");
            }
            else if (form.Kind == DeviceJobs.GpUpdate)
            {
                (step, title) = (DeviceActions.GpUpdate(), "Run gpupdate");
            }
            else if (form.Kind == DeviceJobs.Lock)
            {
                (step, title) = (DeviceActions.Lock(), "Lock");
            }
            else if (form.Kind == DeviceJobs.SignOut)
            {
                (step, title) = (DeviceActions.SignOut(), "Sign everyone out");
            }
            else if (form.Kind == DeviceJobs.Shutdown)
            {
                (step, title) = (DeviceActions.Shutdown(), "Shut down");
            }
            else if (form.Kind == DeviceJobs.Message)
            {
                if (DeviceActions.MessageProblem(form.Text) is { } problem)
                {
                    return Results.BadRequest(new { error = problem });
                }
                (step, title) = (DeviceActions.Message(form.Text!), "Send a message");
            }
            else if (form.Kind == DeviceJobs.Check)
            {
                (step, title) = (DeviceActions.CheckUpdates(), "Check for updates");
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
            // Locking, signing out, shutting down and a message are for now: a computer that's offline longer doesn't get them later.
            var now = form.Kind is DeviceJobs.Lock or DeviceJobs.SignOut or DeviceJobs.Shutdown or DeviceJobs.Message or DeviceJobs.Client or DeviceJobs.GpUpdate;
            var job = jobs.Add(ActivityLog.Who(context), form.Kind!, title, [step], on, now ? Soon : Life, typed ? null : form.Script);
            var computers = Workflows.Computers(on);
            log.Record(context, "clients", "job.run", form.Kind switch
            {
                DeviceJobs.Printer or DeviceJobs.Check => $"{title} on {computers}",
                DeviceJobs.Script => $"Ran {(typed ? "a script typed in" : $"the script {title}")} on {computers}",
                DeviceJobs.Restart => $"Restarted {computers}",
                DeviceJobs.Lock => $"Locked {computers}",
                DeviceJobs.SignOut => $"Signed everyone out of {computers}",
                DeviceJobs.Shutdown => $"Shut down {computers}",
                DeviceJobs.Message => $"Sent a message to {computers}",
                DeviceJobs.Client => $"Updated the client on {computers}",
                DeviceJobs.GpUpdate => $"Ran gpupdate on {computers}",
                _ => $"Installed Windows updates on {computers}",
            }, "ok", $"Job {job.Id}, SHA-256 {DeviceActions.Fingerprint([step])}{(form.Kind == DeviceJobs.Message ? $", message: {form.Text!.Trim()}" : "")}");
            return Results.Ok(new { id = job.Id });
        });

        // Wake-on-LAN for the computers picked: a broadcast on the network, no code on any computer, so the permission and a line in the log.
        app.MapPost("/api/devices/wake", async (WakeForm form, HttpContext context, Clients clients, Waker waker, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsActions) is { } refused)
            {
                return refused;
            }
            List<Client> on = [.. (form.Clients ?? []).Distinct().Select(clients.Find).OfType<Client>().Where(c => c.Approved is not null)];
            if (on.Count == 0)
            {
                return Results.BadRequest(new { error = "Pick at least one approved computer." });
            }
            if (on.Count > Waker.Most)
            {
                return Results.BadRequest(new { error = $"Wake {Waker.Most} computers at a time at most." });
            }
            var results = await waker.Wake(on, context.RequestAborted);
            log.Record(context, "clients", "devices.wake", $"Sent a wake-up to {Workflows.Computers(on)}", results.Any(r => r.Problem is null) ? "ok" : "failed",
                string.Join("; ", results.Select(r => r.Problem is null ? $"{r.Computer} from {string.Join(" and ", r.Via)}" : $"{r.Computer}: {r.Problem}")));
            return Results.Ok(new { results = results.Select(r => new { client = r.Client, computer = r.Computer, sent = r.Problem is null, via = r.Via, problem = r.Problem }) });
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

    // WMI queries: a read on each computer picked, as SYSTEM, down its client's open request. The page gets the rows;
    // nothing is kept but the line in the activity log.
    private static void MapWmi(WebApplication app)
    {
        app.MapPost("/api/devices/wmi", async (WmiForm form, HttpContext context, Clients clients, ClientCommands commands, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsWmi) is { } refused)
            {
                return refused;
            }
            if ((WmiAsk.BadSpace(form.Space) ?? WmiAsk.BadQuery(form.Query)) is { } bad)
            {
                return Results.BadRequest(new { error = bad });
            }
            var ids = (form.Clients ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            if (ids.Count == 0)
            {
                return Results.BadRequest(new { error = "Pick the computers to ask." });
            }
            if (ids.Count > WmiMost)
            {
                return Results.BadRequest(new { error = $"Ask {WmiMost} computers at a time at most." });
            }
            var space = WmiAsk.Space(form.Space);
            var query = form.Query!.Trim();
            var found = ids.Select(id => (Id: id, Client: clients.Find(id))).ToList();
            log.Record(context, "clients", "devices.wmi", $"Ran a WMI query on {Few(found.Where(f => f.Client is not null).Select(f => f.Client!.Computer).ToList())}", "ok", $"{space}: {query}");
            var answers = await Task.WhenAll(found.Select(f => AskWmi(f.Client, new AccountOrder { Action = AccountOrder.Wmi, Space = space, Query = query }, commands, context.RequestAborted)));
            return Results.Ok(new
            {
                results = found.Zip(answers, (f, a) => new
                {
                    client = f.Id, computer = f.Client?.Computer ?? "", ok = a.Error is null, error = a.Error,
                    columns = a.Answer?.Columns ?? [], kinds = a.Answer?.Kinds ?? [], rows = a.Answer?.Rows ?? [], more = a.Answer?.More ?? false,
                }),
            });
        });

        // The namespaces and classes in a namespace on one computer, for the class list to pick from.
        app.MapPost("/api/devices/wmi/classes", async (WmiForm form, HttpContext context, Clients clients, ClientCommands commands) =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsWmi) is { } refused)
            {
                return refused;
            }
            if (WmiAsk.BadSpace(form.Space) is { } bad)
            {
                return Results.BadRequest(new { error = bad });
            }
            var space = WmiAsk.Space(form.Space);
            var (answer, error) = await AskWmi(clients.Find(form.Clients?.FirstOrDefault() ?? ""), new AccountOrder { Action = AccountOrder.WmiClasses, Space = space }, commands, context.RequestAborted);
            return error is not null ? Results.BadRequest(new { error }) : Results.Ok(new { space, spaces = answer!.Spaces ?? [], classes = answer.Classes ?? [] });
        });
    }

    // One computer's answer, or why there's none.
    private static async Task<(AccountAnswer? Answer, string? Error)> AskWmi(Client? c, AccountOrder order, ClientCommands commands, CancellationToken stop)
    {
        if (c is null)
        {
            return (null, "That computer isn't in Devices any more.");
        }
        if (c.Approved is null)
        {
            return (null, "It's waiting to be approved.");
        }
        if (!Version.TryParse(c.Version, out var v) || v < WmiFrom)
        {
            return (null, $"Its client is {c.Version ?? "too old"}. WMI queries need {WmiFrom} or later, which it updates to by itself.");
        }
        if (!commands.Listening(c.Id))
        {
            return (null, "Its client isn't connected right now.");
        }
        var answer = await commands.Send(c.Id, order, stop);
        if (answer is null)
        {
            return (null, $"It didn't answer within {ClientCommands.Answer.TotalSeconds:0} seconds.");
        }
        return answer.Ok ? (answer, null) : (null, answer.Error ?? "It couldn't read that.");
    }

    // GARAGE-PC, GARAGE-PC and OFFICE-PC, or 5 computers.
    private static string Few(List<string> names) => names.Count switch
    {
        0 => "no computers",
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => $"{names.Count} computers",
    };
}

/// <summary>A WMI query: which computers, the namespace (root\cimv2 when empty) and the query. The class list takes one computer.</summary>
public sealed record WmiForm(List<string>? Clients, string? Space, string? Query);

public sealed record ScriptForm(string? Id, string? Name, string? About, string? Language, string? Text, int? Minutes);

/// <summary>What to run and where: a library script by id, or one typed in (Language, Text), or an action. Count is the brake's answer.</summary>
public sealed record RunForm(string? Kind, List<string>? Clients, string? Script = null, string? Language = null, string? Text = null, int? Minutes = null, int? Count = null,
    PrinterForm? Printer = null);

public sealed record WorkflowOnForm(bool On);
