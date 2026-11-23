namespace RabbidLab.Server;

/// <summary>
/// The Lifecycle page: what is dated. Workflow runs saved for a day (a new user set up for their start date), and
/// offboardings with their stages. The page adds the accounts about to expire from the Users list it already has.
/// A saved run can be run now, by someone who could run the workflow by hand, or cancelled.
/// </summary>
public static class LifecycleApi
{
    /// <summary>How far back what's finished still shows.</summary>
    public const int Days = 14;

    // Whoever saved the run, or anyone who manages workflows.
    private static bool Mine(HttpContext context, PlannedRun p)
    {
        var who = Who.Of(context.User);
        return Access.Can(context, Permissions.AdWorkflows) || (who.Source == p.By.Source && string.Equals(who.Username, p.By.Username, StringComparison.OrdinalIgnoreCase));
    }

    public static void MapLifecycleApi(this WebApplication app)
    {
        app.MapGet("/api/lifecycle", (HttpContext context, PeopleWorkflows flows, Offboarding off, Roles roles, TimeProvider clock) =>
        {
            if (!Access.Can(context, Permissions.AdView))
            {
                return Access.NotAllowed();
            }
            var who = Who.Of(context.User);
            var since = clock.GetUtcNow().UtcDateTime.AddDays(-Days);
            var all = flows.All();
            var planned = flows.Planned().Where(p => p.State is "scheduled" or "running" or "failed" || (p.Run?.At ?? p.When) > since).OrderBy(p => p.When).Select(p =>
            {
                var flow = all.FirstOrDefault(f => f.Id == p.Flow);
                var values = new Dictionary<string, string>(p.Values, StringComparer.OrdinalIgnoreCase) { ["Logon name"] = p.User ?? "", ["Name"] = p.Name };
                return new
                {
                    id = p.Id, flow = p.FlowName, name = p.Name, user = p.User, when = p.When, state = p.State, by = p.By.Name, made = p.Made, ran = p.Run?.At, canChange = Mine(context, p),
                    creates = flow?.Steps[0].Kind == "create",
                    // How each step went once it ran; until then, what each will do with the values given.
                    steps = p.Run is { } run ? run.Steps.Select(s => new { label = s.Label, text = s.Detail, state = s.State })
                        : (flow?.Steps ?? []).Select(s => new { label = PeopleWorkflows.KindOf(s.Kind).Label, text = PeopleWorkflows.Fill(PeopleWorkflows.Sentence(s), values), state = "" }),
                };
            });
            var see = roles.Reach(who, Permissions.AdView);
            var change = Access.Can(context, Permissions.AdEnable) ? roles.Reach(who, Permissions.AdEnable) : [];
            var leaving = off.Read().Plans.Where(p => OffboardingApi.Covers(see, p.Dn)
                    && (p.Stages.Any(s => s.State is Offboarding.Scheduled or Offboarding.Running or Offboarding.Failed) || p.Stages.Max(s => s.Ran ?? s.At) > since))
                .OrderBy(p => p.LastDay).Select(p => off.View(p, Access.Can(context, Permissions.AdEnable) && OffboardingApi.Covers(change, p.Dn)));
            return Results.Ok(new { planned, offboarding = leaving, days = Days });
        });

        app.MapPost("/api/lifecycle/planned/{id}/cancel", (string id, HttpContext context, PeopleWorkflows flows, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AdView) is { } refused)
            {
                return refused;
            }
            if (flows.Planned().FirstOrDefault(x => x.Id == id) is not { } p)
            {
                return Results.NotFound(new { error = "This scheduled run no longer exists." });
            }
            if (!Mine(context, p))
            {
                return Results.Json(new { error = $"Access denied. Only {p.By.Name}, or someone with the Manage people workflows permission, can cancel this run." }, statusCode: StatusCodes.Status403Forbidden);
            }
            if (flows.Cancel(id) is null)
            {
                return Results.Conflict(new { error = "This run has already run or was cancelled." });
            }
            log.Record(context, "users", "ad.workflow.cancel", $"Cancelled the scheduled workflow {p.FlowName} for {p.Name}");
            return Results.Ok(new { });
        });

        // Run now: as the person asking, with each step's permission and Confirm it's you, like a run by hand.
        app.MapPost("/api/lifecycle/planned/{id}/run", async (string id, HttpContext context, PeopleWorkflows flows, FlowRunner runner) =>
        {
            if (flows.Planned().FirstOrDefault(x => x.Id == id) is not { } p)
            {
                return Results.NotFound(new { error = "This scheduled run no longer exists." });
            }
            if (flows.Find(p.Flow) is not { On: true } flow)
            {
                return Results.BadRequest(new { error = $"The workflow {p.FlowName} was deleted or disabled." });
            }
            var kinds = flow.Steps.Select(s => PeopleWorkflows.KindOf(s.Kind)).ToList();
            if (kinds.Select(k => k.Permission).Distinct().FirstOrDefault(x => !Access.Can(context, x)) is { } missing)
            {
                return Results.Json(new { error = $"Access denied. This workflow requires the {Offboarding.PermissionName(missing)} permission." }, statusCode: StatusCodes.Status403Forbidden);
            }
            return await UsersApi.Try(context, Permissions.AdView, async () =>
            {
                var mine = flows.Claim(id) ?? throw new ToolProblem("This run has already run or was cancelled.", StatusCodes.Status409Conflict);
                FlowRun run;
                try
                {
                    run = await runner.Run(context, flow, mine.User, mine.Values, CancellationToken.None);
                }
                catch (Exception e) when (e is ToolProblem or DirectoryProblem or LdapRefused)
                {
                    // Stopped before its first step: it waits again, failed, with why.
                    flows.Finish(id, new FlowRun { Id = id, At = DateTime.UtcNow, By = ActivityLog.Who(context), User = mine.User, UserName = mine.Name, State = "failed",
                        Steps = [new FlowStepResult { Label = "Before the first step", State = "failed", Detail = e.Message }] });
                    throw;
                }
                flows.Finish(id, run);
                flows.Ran(flow.Id, run);
                return Results.Ok(run);
            }, confirm: kinds.Any(k => k.Confirm));
        });
    }
}
