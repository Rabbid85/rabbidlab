namespace RabbidLab.Server;

public sealed record AppKeysForm(List<string>? Keys);

public sealed record AppMatchForm(string? Match, bool? Create);

public sealed record AppPickForm(string? Account, string? User);

public sealed record AppKeepForm(string? Rule, string? App, string? Group, string? Account);

/// <summary>
/// Connected apps for the page. Seeing them takes View connected apps; connecting, rules, matching and applying take
/// Manage connected apps, and the ones that change an app or keep a secret take Confirm it's you as well.
/// </summary>
public static class ConnectedAppsApi
{
    public static void AddConnectedApps(this IServiceCollection services)
    {
        services.AddSingleton<AppHttp>();
        services.AddSingleton<IAppConnector, ScimConnector>();
        services.AddSingleton<IAppConnector, GoogleConnector>();
        services.AddSingleton<IAppConnector, ScriptConnector>();
        services.AddSingleton<ConnectedApps>();
        services.AddHostedService<ConnectedAppsWorker>();
    }

    private static IResult Refused(AppProblem e) => Results.BadRequest(new { error = e.Message });

    // Something that changes things: the permission, Confirm it's you when asked for, and an app's refusal said as an error.
    private static async Task<IResult> Changing(HttpContext context, bool confirm, Func<Task<IResult>> work)
    {
        if (Access.RefuseUnless(context, Permissions.AppsManage) is { } refused)
        {
            return refused;
        }
        if (confirm && context.RequestServices.GetRequiredService<Confirmations>().Refuse(context) is { } unconfirmed)
        {
            return unconfirmed;
        }
        try
        {
            return await work();
        }
        catch (AppProblem e)
        {
            return Refused(e);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return Results.BadRequest(new { error = e is TaskCanceledException ? "The app did not answer in time." : $"The app could not be reached: {e.Message}" });
        }
    }

    public static void MapConnectedAppsApi(this WebApplication app)
    {
        // Everything the page shows, in one read: the kinds there are, each app with how its accounts match, the rules, and the plan.
        app.MapGet("/api/apps", async (HttpContext context, ConnectedApps apps) =>
        {
            if (!Access.Can(context, Permissions.AppsView))
            {
                return Access.NotAllowed();
            }
            var f = apps.Read();
            var users = await apps.People(context.RequestAborted);
            var plan = apps.Plan(f, users);
            var names = users.ToDictionary(u => u.Username, u => u.Name, StringComparer.OrdinalIgnoreCase);
            return Results.Ok(new
            {
                canManage = Access.Can(context, Permissions.AppsManage),
                kinds = apps.Kinds.Select(k => new { kind = k.Kind, name = k.Name, about = k.About, account = k.Account, group = k.Group, canCreate = k.CanCreate, fields = k.Fields }),
                apps = f.Apps.Select(a =>
                {
                    var kind = apps.Connector(a.Kind);
                    var m = ConnectedApps.Match(a, users);
                    string State(AppAccount x) => m.UserOf.ContainsKey(x.Id) ? "matched" : x.Pick == "-" ? "np" : Claimed(a, x, users, m) ? "torn" : "none";
                    return new
                    {
                        id = a.Id, kind = a.Kind, kindName = kind?.Name ?? a.Kind, name = a.Name, settings = a.Settings, hasSecret = a.Secret is not null, match = a.Match, create = a.Create,
                        canCreate = kind?.CanCreate == true, paused = a.Paused, lastSync = a.LastSync, problem = a.Problem, account = kind?.Account ?? "account", group = kind?.Group ?? "group",
                        by = a.By, made = a.Made,
                        accounts = a.Accounts.Select(x => new
                        {
                            id = x.Id, name = x.Name, email = x.Email, login = x.Login, active = x.Active, lastSignIn = x.LastSignIn, state = State(x), byHand = x.Pick is { Length: > 1 },
                            user = m.UserOf.GetValueOrDefault(x.Id), userName = m.UserOf.TryGetValue(x.Id, out var u) ? names.GetValueOrDefault(u) : null,
                        }),
                        groups = a.Groups.Select(g => new { id = g.Id, name = g.Name, type = g.Type, members = g.Members, rules = f.Rules.Where(r => r.To.Any(t => t.App == a.Id && t.Group == g.Id)).Select(r => r.AdName) }),
                        log = a.Log.Take(60),
                    };
                }),
                rules = f.Rules.Select(r => new
                {
                    id = r.Id, ad = r.Ad, adName = r.AdName, mode = r.Mode, by = r.By, made = r.Made, people = users.Count(u => u.Enabled && u.Groups.Contains(r.Ad)),
                    to = r.To.Select(t => new { app = t.App, group = t.Group }),
                }),
                plan,
            });
        });

        // Connect an app or change its connection: it is read once first, and only saved when that works.
        app.MapPost("/api/apps", (ConnectedAppForm form, HttpContext context, ConnectedApps apps, ActivityLog log) => Changing(context, confirm: true, async () =>
        {
            var saved = await apps.Connect(form, ActivityLog.Who(context), context.RequestAborted);
            log.Record(context, "apps", form.Id is { Length: > 0 } ? "apps.edit" : "apps.connect", $"{(form.Id is { Length: > 0 } ? "Changed the connection to" : "Connected")} {saved.Name}",
                detail: $"{saved.Accounts.Count} accounts and {saved.Groups.Count} groups read.");
            return Results.Ok(new { id = saved.Id });
        }));

        app.MapPost("/api/apps/{id}/sync", (string id, HttpContext context, ConnectedApps apps) => Changing(context, confirm: false, async () =>
        {
            var ok = await apps.Sync(id, ActivityLog.Who(context), context.RequestAborted);
            var now = apps.Read().Apps.FirstOrDefault(a => a.Id == id);
            return now is null ? Results.NotFound(new { error = "This app is no longer connected." }) : Results.Ok(new { ok, problem = now.Problem });
        }));

        app.MapPost("/api/apps/{id}/settings", (string id, AppMatchForm form, HttpContext context, ConnectedApps apps, ActivityLog log) => Changing(context, confirm: false, () =>
        {
            if (form.Match is not null && !ConnectedApps.Matches.Contains(form.Match))
            {
                throw new AppProblem("Select how accounts are matched.");
            }
            var name = "";
            apps.Set(id, a =>
            {
                (a.Match, a.Create, name) = (form.Match ?? a.Match, form.Create ?? a.Create, a.Name);
            });
            log.Record(context, "apps", "apps.settings", $"Changed settings for {name}", detail: form.Match is { } m ? $"Accounts are matched by {m}." : form.Create == true ? "Accounts are made." : "Accounts are not made.");
            return Task.FromResult(Results.Ok(new { }));
        }));

        app.MapPost("/api/apps/{id}/pause", (string id, OnForm form, HttpContext context, ConnectedApps apps, ActivityLog log) => Changing(context, confirm: false, () =>
        {
            var name = "";
            apps.Set(id, a => (a.Paused, name) = (form.On, a.Name));
            log.Record(context, "apps", "apps.pause", $"{(form.On ? "Paused" : "Resumed")} sync for {name}");
            return Task.FromResult(Results.Ok(new { }));
        }));

        app.MapPost("/api/apps/{id}/pick", (string id, AppPickForm form, HttpContext context, ConnectedApps apps) => Changing(context, confirm: false, () =>
        {
            apps.Pick(id, form.Account ?? "", form.User, ActivityLog.Who(context));
            return Task.FromResult(Results.Ok(new { }));
        }));

        app.MapPost("/api/apps/{id}/delete", (string id, HttpContext context, ConnectedApps apps, ActivityLog log) => Changing(context, confirm: true, () =>
        {
            var gone = apps.Read().Apps.FirstOrDefault(a => a.Id == id);
            apps.Remove(id);
            if (gone is not null)
            {
                log.Record(context, "apps", "apps.remove", $"Removed the connection to {gone.Name}", detail: "Nothing was changed in the app. Its saved secret was deleted.");
            }
            return Task.FromResult(Results.Ok(new { }));
        }));

        // What a rule would do, before it is saved.
        app.MapPost("/api/apps/rules/preview", (AccessRuleForm form, HttpContext context, ConnectedApps apps) => Changing(context, confirm: false, async () =>
            Results.Ok(new { plan = await apps.Preview(form, context.RequestAborted) })));

        // Saves a rule; one that gives automatically gives what it owes straight away.
        app.MapPost("/api/apps/rules", (AccessRuleForm form, HttpContext context, ConnectedApps apps, ActivityLog log) => Changing(context, confirm: true, async () =>
        {
            var by = ActivityLog.Who(context);
            var rule = apps.SaveRule(form, by);
            log.Record(context, "apps", "apps.rule", $"Saved the access rule for {rule.AdName}", detail: $"{rule.To.Count} groups and roles, given {(rule.Mode == "auto" ? "automatically" : "after approval")}.");
            var (done, failed) = (0, 0);
            if (rule.Mode == "auto")
            {
                var keys = (await apps.Plan(context.RequestAborted)).Where(i => i.Rule == rule.Id && i.Kind is "create" or "add").Select(i => i.Key).ToList();
                (done, failed) = keys.Count > 0 ? await apps.Apply(keys, by, context.RequestAborted) : (0, 0);
            }
            return Results.Ok(new { id = rule.Id, done, failed });
        }));

        app.MapPost("/api/apps/rules/{id}/delete", (string id, HttpContext context, ConnectedApps apps, ActivityLog log) => Changing(context, confirm: false, () =>
        {
            if (apps.DeleteRule(id) is { } gone)
            {
                log.Record(context, "apps", "apps.rule.delete", $"Deleted the access rule for {gone.AdName}", detail: "Nobody lost access. The groups and roles no longer follow the Active Directory group.");
            }
            return Task.FromResult(Results.Ok(new { }));
        }));

        app.MapPost("/api/apps/keep", (AppKeepForm form, HttpContext context, ConnectedApps apps) => Changing(context, confirm: false, () =>
        {
            apps.Keep(form.Rule ?? "", form.App ?? "", form.Group ?? "", form.Account ?? "");
            return Task.FromResult(Results.Ok(new { }));
        }));

        // Review changes, Apply: the items picked from the plan as it stands now.
        app.MapPost("/api/apps/apply", (AppKeysForm form, HttpContext context, ConnectedApps apps, ActivityLog log) => Changing(context, confirm: true, async () =>
        {
            var keys = (form.Keys ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var (done, failed) = await apps.Apply(keys, ActivityLog.Who(context), context.RequestAborted);
            log.Record(context, "apps", "apps.apply", $"Applied {done} {(done == 1 ? "change" : "changes")} in connected apps", failed > 0 ? "warning" : "ok", failed > 0 ? $"{failed} failed. Each app's Activity says why." : null);
            return Results.Ok(new { done, failed });
        }));
    }

    // Whether another account claims the same user, which is why neither is matched.
    private static bool Claimed(ConnectedApp app, AppAccount x, IReadOnlyList<AppUser> users, AppMatches m)
    {
        var key = (app.Match == "username" ? x.Login : x.Email).ToLowerInvariant();
        var user = x.Pick is { Length: > 1 } p ? p : app.Match == "hand" || key.Length == 0 ? null : users.FirstOrDefault(u => (app.Match == "username" ? u.Username : u.Email).Equals(key, StringComparison.OrdinalIgnoreCase))?.Username;
        return user is not null && m.Torn.Contains(user);
    }
}
