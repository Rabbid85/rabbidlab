namespace RabbidLab.Server;

/// <summary>Everything a Group Policy change can carry, and the domain admin's login when Rabbid Lab's own account isn't allowed (never kept).</summary>
public sealed record GpoForm(string? Name = null, string? From = null, string? Link = null, string? Gpo = null, string? Target = null, bool? Computer = null, bool? User = null,
    string? Filter = null, string? Trustee = null, bool? Apply = null, string? Comment = null, string? Backup = null, bool Add = false, bool Remove = false, bool? Enabled = null,
    bool? Enforced = null, int? Order = null, bool? Blocked = null, string? Id = null, string? Description = null, string? Query = null, string? Space = null,
    string? Username = null, string? Password = null)
{
    public GpoLogin Login => new(Username, Password);
}

/// <summary>
/// The Group Policy page: GPOs, where they're linked, WMI filters and backups. Seeing takes gpo.view; each change takes
/// its permission, a confirmed session, and goes in the activity log. A change Rabbid Lab's own account isn't allowed
/// to make answers login: true, and the page asks for a domain admin's login for it.
/// </summary>
public static class GroupPolicyApi
{
    public static void MapGroupPolicyApi(this WebApplication app)
    {
        app.MapGet("/api/gpo", (HttpContext context, IGroupPolicy gp) => Gpo(context, Permissions.GpoView, async () =>
            Results.Ok(await gp.GroupPolicy(context.RequestAborted)), change: false));

        app.MapGet("/api/gpo/{id}", (string id, HttpContext context, IGroupPolicy gp) => Gpo(context, Permissions.GpoView, async () =>
            Results.Ok(await gp.GpoDetail(id, context.RequestAborted)), change: false));

        // The settings report, as Group Policy Management saves it.
        app.MapGet("/api/gpo/{id}/report", (string id, HttpContext context, IGroupPolicy gp) => Gpo(context, Permissions.GpoView, async () =>
        {
            var (html, name) = await gp.GpoReport(id, context.RequestAborted);
            var file = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
            return Results.File(System.Text.Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", $"{file}.html");
        }, change: false));

        app.MapPost("/api/gpo", (GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoChange, async () =>
        {
            var (id, warning) = await gp.NewGpo(form.Name ?? "", form.From, form.Link, form.Login, ActivityLog.Who(context), context.RequestAborted);
            var name = form.Name?.Trim();
            log.Record(context, "users", "gpo.new", string.IsNullOrWhiteSpace(form.From) ? $"Made the GPO {name}" : $"Made the GPO {name} as a copy of {await gp.GpoName(form.From, context.RequestAborted)}",
                warning is null ? "ok" : "warning", warning);
            if (!string.IsNullOrWhiteSpace(form.Link) && warning is null)
            {
                log.Record(context, "users", "gpo.link", $"Linked {name} to {gp.PlaceName(form.Link)}");
            }
            return Results.Ok(new { id, warning });
        }));

        app.MapPost("/api/gpo/{id}/rename", (string id, GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoChange, async () =>
        {
            var was = await gp.GpoName(id, context.RequestAborted);
            await gp.RenameGpo(id, form.Name ?? "", form.Login, context.RequestAborted);
            log.Record(context, "users", "gpo.rename", $"Renamed the GPO {was} to {form.Name?.Trim()}");
            return Results.Ok(new { });
        }));

        app.MapPost("/api/gpo/{id}/status", (string id, GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoChange, async () =>
        {
            bool computer = form.Computer ?? true, user = form.User ?? true;
            await gp.SetGpoStatus(id, computer, user, form.Login, context.RequestAborted);
            log.Record(context, "users", "gpo.status", $"{await gp.GpoName(id, context.RequestAborted)}: " + (computer && user ? "all settings on" : computer ? "user settings off"
                : user ? "computer settings off" : "all settings off"));
            return Results.Ok(new { });
        }));

        app.MapPost("/api/gpo/{id}/wmi", (string id, GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoChange, async () =>
        {
            await gp.SetGpoWmi(id, form.Filter, form.Login, context.RequestAborted);
            log.Record(context, "users", "gpo.wmi", string.IsNullOrWhiteSpace(form.Filter) ? $"Took the WMI filter off {await gp.GpoName(id, context.RequestAborted)}"
                : $"{await gp.GpoName(id, context.RequestAborted)} uses a WMI filter now ({form.Filter})");
            return Results.Ok(new { });
        }));

        app.MapPost("/api/gpo/{id}/apply", (string id, GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoChange, async () =>
        {
            await gp.SetGpoApply(id, form.Trustee ?? "", form.Apply ?? true, form.Login, context.RequestAborted);
            log.Record(context, "users", "gpo.apply", form.Apply ?? true ? $"{await gp.GpoName(id, context.RequestAborted)} applies to {form.Trustee?.Trim()} now"
                : $"{await gp.GpoName(id, context.RequestAborted)} doesn't apply to {form.Trustee?.Trim()} any more");
            return Results.Ok(new { });
        }));

        app.MapPost("/api/gpo/{id}/delete", (string id, GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoDelete, async () =>
        {
            var name = await gp.GpoName(id, context.RequestAborted);
            await gp.DeleteGpo(id, form.Login, ActivityLog.Who(context), context.RequestAborted);
            log.Record(context, "users", "gpo.delete", $"Deleted the GPO {name}, and its links; a backup was kept first");
            return Results.Ok(new { });
        }));

        app.MapPost("/api/gpo/{id}/backup", (string id, GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoChange, async () =>
        {
            var b = await gp.BackupGpo(id, form.Comment, form.Login, ActivityLog.Who(context), context.RequestAborted);
            log.Record(context, "users", "gpo.backup", $"Backed up the GPO {b.Name}");
            return Results.Ok(b);
        }));

        app.MapPost("/api/gpo/backup-all", (GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoChange, async () =>
        {
            var (done, failed) = await gp.BackupAll(form.Comment, form.Login, ActivityLog.Who(context), context.RequestAborted);
            var warning = failed.Count > 0 ? $"Not backed up: {string.Join(", ", failed)}" : null;
            log.Record(context, "users", "gpo.backup", $"Backed up {done} GPOs", warning is null ? "ok" : "warning", warning);
            return Results.Ok(new { done, failed });
        }));

        app.MapPost("/api/gpo/restore", (GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoChange, async () =>
        {
            var id = await gp.RestoreGpo(form.Backup ?? "", form.Login, ActivityLog.Who(context), context.RequestAborted);
            log.Record(context, "users", "gpo.restore", $"Restored the GPO {await gp.GpoName(id, context.RequestAborted)} from a backup");
            return Results.Ok(new { id });
        }));

        app.MapPost("/api/gpo/link", (GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoLink, async () =>
        {
            var change = new GpoLinkChange(form.Add, form.Remove, form.Enabled, form.Enforced, form.Order);
            await gp.Link(form.Gpo ?? "", form.Target ?? "", change, form.Login, context.RequestAborted);
            var name = await gp.GpoName(form.Gpo ?? "", context.RequestAborted);
            var where = gp.PlaceName(form.Target ?? "");
            log.Record(context, "users", "gpo.link", form.Add ? $"Linked {name} to {where}{(form.Enforced == true ? ", enforced" : "")}" : form.Remove ? $"Unlinked {name} from {where}"
                : form.Order is { } o ? $"Moved {name}'s link on {where} to {o}" : form.Enabled is { } on ? $"Turned {name}'s link on {where} {(on ? "on" : "off")}"
                : $"{name}'s link on {where} is {(form.Enforced == true ? "enforced" : "not enforced")} now");
            return Results.Ok(new { });
        }));

        app.MapPost("/api/gpo/block", (GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoLink, async () =>
        {
            await gp.SetBlocked(form.Target ?? "", form.Blocked ?? true, form.Login, context.RequestAborted);
            log.Record(context, "users", "gpo.block", form.Blocked ?? true ? $"Blocked inheritance on {gp.PlaceName(form.Target ?? "")}" : $"{gp.PlaceName(form.Target ?? "")} inherits GPOs again");
            return Results.Ok(new { });
        }));

        app.MapPost("/api/gpo/wmi-filters", (GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoChange, async () =>
        {
            var id = await gp.SaveWmiFilter(form.Id, form.Name ?? "", form.Description, form.Query ?? "", form.Space, form.Login, ActivityLog.Who(context), context.RequestAborted);
            log.Record(context, "users", "gpo.wmifilter", string.IsNullOrWhiteSpace(form.Id) ? $"Made the WMI filter {form.Name?.Trim()}" : $"Changed the WMI filter {form.Name?.Trim()}", "ok", form.Query);
            return Results.Ok(new { id });
        }));


        // Devices, Deploy: the client on every computer through the Rabbid Lab Client GPO. Deploying takes adding computers
        // and changing Group Policy both, a confirmed session, and goes in the activity log.
        app.MapGet("/api/gpo/client", (HttpContext context, IGroupPolicy gp) => Gpo(context, Permissions.GpoView, async () =>
            Results.Ok(await gp.ClientDeploy(context.RequestAborted)), change: false));

        app.MapPost("/api/gpo/client", (ClientDeployForm form, HttpContext context, IGroupPolicy gp, IConfiguration config, ActivityLog log) => Gpo(context, Permissions.GpoChange, async () =>
        {
            if (Access.RefuseUnless(context, Permissions.SeatsAdd) is { } refused)
            {
                return refused;
            }
            if (!File.Exists(ClientFiles.Msi))
            {
                return Results.BadRequest(new { error = "The client installer isn't on this server. Run Setup again to put it back." });
            }
            var fingerprint = ClientFiles.Certificate(config) is { } cert ? ClientFiles.Fingerprint(cert) : null;
            var id = await gp.DeployClient(form.Server ?? "", fingerprint, await File.ReadAllBytesAsync(ClientFiles.Msi, context.RequestAborted), form.Login, ActivityLog.Who(context), context.RequestAborted);
            log.Record(context, "users", "gpo.client", $"Deployed the client {Product.Version} to every computer with the GPO {DirectoryTools.ClientGpoName}, pointed at {form.Server}");
            return Results.Ok(new { id });
        }));

        app.MapPost("/api/gpo/client/stop", (GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoLink, async () =>
        {
            await gp.StopClientDeploy(form.Login, context.RequestAborted);
            log.Record(context, "users", "gpo.client.stop", $"Stopped deploying the client: {DirectoryTools.ClientGpoName} is no longer linked to the domain");
            return Results.Ok(new { });
        }));
        app.MapPost("/api/gpo/wmi-filters/{id}/delete", (string id, GpoForm form, HttpContext context, IGroupPolicy gp, ActivityLog log) => Gpo(context, Permissions.GpoChange, async () =>
        {
            await gp.DeleteWmiFilter(id, form.Login, context.RequestAborted);
            log.Record(context, "users", "gpo.wmifilter", $"Deleted the WMI filter {form.Name?.Trim() ?? id}");
            return Results.Ok(new { });
        }));
    }

    // Allowed, confirmed for a change, and a refusal for rights said so the page asks for a login.
    private static Task<IResult> Gpo(HttpContext context, string permission, Func<Task<IResult>> work, bool change = true) =>
        UsersApi.Try(context, permission, async () =>
        {
            if (change && context.RequestServices.GetRequiredService<Confirmations>().Refuse(context) is { } refused)
            {
                return refused;
            }
            try
            {
                return await work();
            }
            catch (GpoNeedsLogin e)
            {
                return Results.Json(new { error = e.Message, login = true }, statusCode: StatusCodes.Status403Forbidden);
            }
        }, change);
}
