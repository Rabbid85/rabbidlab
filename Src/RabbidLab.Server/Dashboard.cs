using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>One card on a person's dashboard: what it is, how many columns and rows it takes, and a name of their own for it.</summary>
public sealed record DashCard(string? Type, int W, int H, string? Title = null);

public sealed class DashboardsFile
{
    public Dictionary<string, List<DashCard>> People { get; set; } = [];
}

public sealed record DashboardForm(List<DashCard>? Cards);

/// <summary>
/// Overview's dashboard as each person arranged it, kept here in dashboards.json so it follows them to any browser: which
/// cards show, in what order and at what size. A person with none kept has the page's own arrangement; Reset puts that
/// back. Only the arrangement is kept: the page knows what each card is, and reads what it shows with the person's roles.
/// </summary>
public sealed class UserDashboards(DataFolder folder)
{
    public const int LongestTitle = 40;
    public static readonly IReadOnlySet<string> Types = new HashSet<string> { "news", "machine", "sensors", "links", "devices", "alerts", "activity", "network" };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<DashboardsFile> file = folder.Json<DashboardsFile>("dashboards.json", Json);

    public List<DashCard>? Of(string person)
    {
        lock (file.Gate)
        {
            return file.Read().People.TryGetValue(person, out var cards) ? [.. cards] : null;
        }
    }

    /// <summary>The cards for this person: each a card there is, once, one to four columns wide and one or two rows tall.</summary>
    public List<DashCard> Set(string person, List<DashCard>? cards)
    {
        var clean = new List<DashCard>();
        foreach (var card in cards ?? [])
        {
            if (card?.Type is not { } type || !Types.Contains(type))
            {
                throw new ToolProblem("That is not a card the dashboard has.");
            }
            if (clean.Any(c => c.Type == type))
            {
                throw new ToolProblem("A card can be on the dashboard once.");
            }
            if (card.W is < 1 or > 4 || card.H is < 1 or > 2)
            {
                throw new ToolProblem("A card is one to four columns wide and one or two rows tall.");
            }
            var title = card.Title?.Trim();
            if (title?.Length > LongestTitle)
            {
                throw new ToolProblem($"A card's name can be up to {LongestTitle} characters.");
            }
            clean.Add(new DashCard(type, card.W, card.H, string.IsNullOrEmpty(title) ? null : title));
        }
        lock (file.Gate)
        {
            file.Save(new DashboardsFile { People = new(file.Read().People) { [person] = clean } });
        }
        return clean;
    }

    public void Reset(string person)
    {
        lock (file.Gate)
        {
            var people = new Dictionary<string, List<DashCard>>(file.Read().People);
            if (people.Remove(person))
            {
                file.Save(new DashboardsFile { People = people });
            }
        }
    }
}

public static class DashboardApi
{
    public static void MapDashboard(this WebApplication app)
    {
        // Your own dashboard, for anyone signed in. No cards kept (null) is the page's own arrangement.
        app.MapGet("/api/me/dashboard", (HttpContext context, UserDashboards boards) => Results.Ok(new { cards = boards.Of(UserKeys.PersonOf(Who.Of(context.User))) }));

        app.MapPost("/api/me/dashboard", (DashboardForm form, HttpContext context, UserDashboards boards) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            try
            {
                return Results.Ok(new { cards = boards.Set(UserKeys.PersonOf(Who.Of(context.User)), form.Cards) });
            }
            catch (ToolProblem e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
        });

        app.MapPost("/api/me/dashboard/reset", (HttpContext context, UserDashboards boards) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            boards.Reset(UserKeys.PersonOf(Who.Of(context.User)));
            return Results.Ok(new { cards = (List<DashCard>?) null });
        });

        // What the Alerts and Network cards show, in a few lines: their pages' own reads carry every rule and every device,
        // too much to ask for each minute. Each part only for someone whose roles show that page.
        app.MapGet("/api/overview/cards", (HttpContext context, EventAlerts alerts, NetworkScan network) => Results.Ok(new
        {
            alerts = Access.Can(context, Permissions.SensorsAlerts) ? AlertsNow(alerts) : null,
            network = Access.Can(context, Permissions.NetworkView) ? network.Brief() : null,
        }));
    }

    // Alerts nobody has acknowledged: how many, and the newest five.
    private static object AlertsNow(EventAlerts alerts)
    {
        var fresh = alerts.Fired().Where(f => f.State == "new").OrderByDescending(f => f.At).ToList();
        return new { fresh = fresh.Count, latest = fresh.Take(5).Select(f => new { f.Title, f.At, f.Severity }) };
    }
}
