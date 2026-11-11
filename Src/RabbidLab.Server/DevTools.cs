namespace RabbidLab.Server;

/// <summary>
/// Developer tools, in Rabbid Lab Dev only: a switch in Settings, and while it's on, test buttons on Links and Sensors that
/// add a link, a list of links or a sensor that plays its part (answering, or down) and goes away after 30 seconds.
/// </summary>
public static class DevTools
{
    public static readonly TimeSpan Lasts = TimeSpan.FromSeconds(30);

    public static bool On(SettingsStore settings) => Product.Dev && settings.Current.DevTools;

    public static void MapDevToolsApi(this WebApplication app)
    {
        if (!Product.Dev)
        {
            return; // nothing of this in a release
        }
        app.MapGet("/api/devtools", (HttpContext context, SettingsStore settings) =>
            Access.Refuse(context) ?? Results.Ok(new { on = settings.Current.DevTools }));

        app.MapPost("/api/devtools", (DevToolsForm form, HttpContext context, SettingsStore settings, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.UpdatesAuto) is { } refused)
            {
                return refused;
            }
            settings.Change(s => s.DevTools = form.On);
            log.Record(context, "admin", "devtools", form.On ? "Turned Developer tools on" : "Turned Developer tools off");
            return Results.Ok(new { on = settings.Current.DevTools });
        });

        app.MapPost("/api/links/test", (TestForm form, HttpContext context, Links links, SettingsStore settings) =>
        {
            if (Access.RefuseUnless(context, Permissions.LinksEdit) is { } refused)
            {
                return refused;
            }
            if (!On(settings))
            {
                return Results.BadRequest(new { error = "Turn on Developer tools in Settings first." });
            }
            links.AddTests(form.Kind, ActivityLog.Who(context));
            return Results.Ok(LinksApi.Listed(links));
        });

        app.MapPost("/api/sensors/test", (TestForm form, HttpContext context, Sensors sensors, SettingsStore settings) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsEdit) is { } refused)
            {
                return refused;
            }
            if (!On(settings))
            {
                return Results.BadRequest(new { error = "Turn on Developer tools in Settings first." });
            }
            sensors.AddTest(form.Kind == "down", ActivityLog.Who(context));
            return Results.Ok(sensors.View());
        });
    }
}

public sealed record DevToolsForm(bool On);

/// <summary>up, down, or list (twelve links, every fourth one down).</summary>
public sealed record TestForm(string? Kind);
