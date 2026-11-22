using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace RabbidLab.Server;

/// <summary>What a change was made to: where it is now, its objectGUID when AD gave one, its name and kind (user, group, computer, ou, domain, other).</summary>
public sealed record ChangedObject(string Dn, string? Guid, string Name, string Kind);

/// <summary>
/// How a step is put back. Kind: set (Attribute back to Was, while it still holds Now), bit (a userAccountControl Bit back
/// to On), mustchange and cannotchange (that option back to On), groups (the object back into Add and out of Remove),
/// members (Add and Remove back on the group), place (back to Rdn under Parent, while it is still at Now), restore (from the Recycle Bin).
/// </summary>
public sealed record StepUndo(string Kind, string? Attribute = null, List<string>? Was = null, List<string>? Now = null, int Bit = 0, bool On = false,
    List<string>? Add = null, List<string>? Remove = null, string? Rdn = null, string? Parent = null);

/// <summary>One part of a change: its name, its values before and after as shown, and how it's put back.</summary>
public sealed record ChangeStep(string Label, List<string> Before, List<string> After, StepUndo? Undo = null);

/// <summary>One Active Directory change Rabbid Lab made: who, when, the permission it took, what it changed and how to put it back.</summary>
public sealed class Change
{
    public string Id { get; set; } = "";
    public DateTime At { get; set; }
    public string By { get; set; } = "";
    public string? From { get; set; }
    public string Permission { get; set; } = "";

    /// <summary>Whether its endpoint took Confirm it's you, so undoing it does too.</summary>
    public bool Confirm { get; set; }

    public ChangedObject Object { get; set; } = new("", null, "", "other");
    public string Title { get; set; } = "";
    public List<ChangeStep> Steps { get; set; } = [];

    /// <summary>Why it can't be undone, or null when it can.</summary>
    public string? Why { get; set; }

    /// <summary>For an undo: the change it put back.</summary>
    public string? Of { get; set; }

    public string? UndoneBy { get; set; }
    public DateTime? UndoneAt { get; set; }

    /// <summary>The change the undo was recorded as.</summary>
    public string? UndoneIn { get; set; }
}

public sealed record ChangeUndone(string By, DateTime At, string? Change);

/// <summary>A change as a list shows it, for the person asking: whether they can undo it now, and why not.</summary>
public sealed record ChangeBrief(string Id, bool Undo, string? Why, ChangeUndone? Undone, string Permission);

public sealed record StepView(string Label, IReadOnlyList<string> Before, IReadOnlyList<string> After);

/// <summary>A change with its steps, for its side panel and the History tab.</summary>
public sealed record ChangeView(string Id, DateTime At, string By, string? From, string Permission, string PermissionName, bool Confirm, ChangedObject Object,
    string Title, IReadOnlyList<StepView> Steps, bool Undo, string? Why, ChangeUndone? Undone, string? Of);

public sealed record UndoForm(List<string>? Ids);

/// <summary>
/// What one request changes in Active Directory. The directory tools note each step as they make it; the activity log
/// entry for the request keeps it in the journal and points at it.
/// </summary>
public sealed class ChangeNote(ChangeJournal journal, string permission, bool confirm)
{
    private static readonly AsyncLocal<ChangeNote?> Now = new();
    private readonly List<ChangeStep> steps = [];
    private ChangedObject? on;
    private string? why;
    private string? of;

    public static ChangeNote? Current => Now.Value;

    /// <summary>Set for the rest of this request, as Caller is.</summary>
    public static void Set(ChangeNote? note) => Now.Value = note;

    /// <summary>The change once the log kept it.</summary>
    public Change? Kept { get; private set; }

    public void On(ChangedObject o) => on ??= o;

    /// <summary>Where the object is once the change moved or renamed it.</summary>
    public void At(string dn, string? name = null)
    {
        if (on is not null)
        {
            on = on with { Dn = dn, Name = name ?? on.Name };
        }
    }

    public void Step(ChangeStep step) => steps.Add(step);

    public void Cannot(string reason) => why ??= reason;

    /// <summary>This request puts that change back; an undo itself isn't undone.</summary>
    public void Undoing(Change c)
    {
        of = c.Id;
        Cannot("This entry records an undo. To reverse it, make the change again.");
    }

    /// <summary>Kept in the journal under the log's title, the first time; null when nothing was changed or it's kept already.</summary>
    public Change? Keep(string by, string? from, string title)
    {
        if (Kept is not null || on is null || (steps.Count == 0 && why is null))
        {
            return null;
        }
        var reason = why ?? (steps.Any(s => s.Undo is null) ? "This change cannot be undone." : null);
        Kept = journal.Add(new Change { By = by, From = from, Permission = permission, Confirm = confirm, Object = on, Title = title, Steps = [.. steps], Why = reason, Of = of });
        return Kept;
    }
}

/// <summary>
/// The change journal: every Active Directory change Rabbid Lab made in the last 30 days, with the values before and after,
/// one JSON line each in changes.jsonl in the data folder. A change marked undone is written again; the last line wins.
/// </summary>
public sealed class ChangeJournal(DataFolder folder, TimeProvider clock, ILogger<ChangeJournal> log)
{
    /// <summary>How long a change is kept and can be undone.</summary>
    public static readonly TimeSpan Life = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string file = Path.Combine(folder.Path, "changes.jsonl");
    private readonly Lock gate = new();
    private readonly HashSet<string> undoing = [];
    private Dictionary<string, Change>? all;
    private int lines;
    private DateTime newest; // each change is later than the one before, so newest first is always their order

    public DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Change Add(Change c)
    {
        lock (gate)
        {
            var known = Load();
            c.Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            c.At = newest = Now > newest ? Now : newest.AddTicks(1);
            known[c.Id] = c;
            if (!Prune())
            {
                Append(c);
            }
            return c;
        }
    }

    public Change? Find(string id)
    {
        lock (gate)
        {
            Prune();
            return Load().GetValueOrDefault(id);
        }
    }

    /// <summary>The changes to one object, newest first: by its DN, and by its GUID so the ones from before a move or rename come too.</summary>
    public IReadOnlyList<Change> For(string? dn, string? guid)
    {
        lock (gate)
        {
            Prune();
            var list = Load().Values;
            var guids = list.Where(c => dn is not null && string.Equals(c.Object.Dn, dn, StringComparison.OrdinalIgnoreCase)).Select(c => c.Object.Guid).OfType<string>()
                .Append(guid).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            return [.. list.Where(c => (dn is not null && string.Equals(c.Object.Dn, dn, StringComparison.OrdinalIgnoreCase)) || (c.Object.Guid is { } g && guids.Contains(g)))
                .OrderByDescending(c => c.At)];
        }
    }

    /// <summary>Taken while it's being undone, so two people can't undo it at once.</summary>
    public bool Claim(string id)
    {
        lock (gate)
        {
            return undoing.Add(id);
        }
    }

    public void Release(string id)
    {
        lock (gate)
        {
            undoing.Remove(id);
        }
    }

    public void Undone(string id, string by, string? inChange)
    {
        lock (gate)
        {
            if (Load().GetValueOrDefault(id) is not { } c)
            {
                return;
            }
            (c.UndoneBy, c.UndoneAt, c.UndoneIn) = (by, Now, inChange);
            Append(c);
        }
    }

    /// <summary>Why nobody can undo it now: it can't be, it was, or it's too old. Null when it can be.</summary>
    public string? Blocked(Change c) =>
        c.UndoneAt is not null ? $"This change was already undone by {c.UndoneBy}."
        : c.Why is { } why ? why
        : Now - c.At > Life ? "This change is more than 30 days old. Changes can be undone for 30 days."
        : null;

    /// <summary>Why this person can't undo it now, or null when they can. Their roles' scope is checked when they try.</summary>
    public string? WhyNot(HttpContext context, Change c) =>
        c.UndoneAt is not null ? null
        : Blocked(c) ?? (Access.Can(context, c.Permission) ? null : $"Undoing this change requires the {PermissionName(c.Permission)} permission.");

    public ChangeBrief Brief(HttpContext context, Change c)
    {
        var why = WhyNot(context, c);
        return new(c.Id, c.UndoneAt is null && why is null, why, Undone(c), c.Permission);
    }

    /// <summary>The changes a page of the activity log points at, by id.</summary>
    public IReadOnlyDictionary<string, ChangeBrief> Briefs(HttpContext context, IEnumerable<ActivityEntry> entries)
    {
        var ids = entries.Select(e => e.Change).OfType<string>().Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, ChangeBrief>();
        }
        lock (gate)
        {
            Prune();
            var known = Load();
            return ids.Select(known.GetValueOrDefault).OfType<Change>().ToDictionary(c => c.Id, c => Brief(context, c));
        }
    }

    public ChangeView View(HttpContext context, Change c)
    {
        var why = WhyNot(context, c);
        return new ChangeView(c.Id, c.At, c.By, c.From, c.Permission, PermissionName(c.Permission), c.Confirm, c.Object, c.Title,
            [.. c.Steps.Select(s => new StepView(s.Label, s.Before, s.After))], c.UndoneAt is null && why is null, why, Undone(c), c.Of);
    }

    /// <summary>
    /// Whether this person may read it: anyone who can view the activity log, or who can view Active Directory where the
    /// object is.
    /// </summary>
    public static bool MaySee(HttpContext context, Change c)
    {
        if (Access.Can(context, Permissions.ActivityView))
        {
            return true;
        }
        if (!Access.Can(context, Permissions.AdView))
        {
            return false;
        }
        var reach = context.RequestServices.GetRequiredService<Roles>().Reach(Who.Of(context.User), Permissions.AdView);
        return reach is null || reach.Any(r => Ldap.Under(c.Object.Dn, r));
    }

    public static string PermissionName(string key) =>
        Permissions.Parts.SelectMany(p => p.Actions).FirstOrDefault(a => a.Key == key)?.Name ?? key;

    private static ChangeUndone? Undone(Change c) => c.UndoneAt is { } at ? new(c.UndoneBy ?? "", at, c.UndoneIn) : null;

    private Dictionary<string, Change> Load()
    {
        if (all is not null)
        {
            return all;
        }
        all = [];
        lines = 0;
        try
        {
            if (File.Exists(file))
            {
                foreach (var line in File.ReadLines(file))
                {
                    lines++;
                    try
                    {
                        if (line.Length > 0 && JsonSerializer.Deserialize<Change>(line, Json) is { Id.Length: > 0 } c)
                        {
                            all[c.Id] = c;
                            newest = c.At > newest ? c.At : newest;
                        }
                    }
                    catch (JsonException)
                    {
                        // A line cut off part-way through is skipped.
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(e, "The change journal could not be read");
        }
        Prune();
        return all;
    }

    // Dropped a day after they can no longer be undone, so a panel opened then still says why. The file is written again
    // when that happens, or when it's mostly lines written over since.
    private bool Prune()
    {
        var known = all ?? Load();
        var cut = Now - Life - TimeSpan.FromDays(1);
        var old = known.Values.Where(c => c.At < cut).Select(c => c.Id).ToList();
        foreach (var id in old)
        {
            known.Remove(id);
        }
        if (old.Count == 0 && lines <= (2 * known.Count) + 200)
        {
            return false;
        }
        try
        {
            var temp = file + ".tmp";
            File.WriteAllLines(temp, known.Values.OrderBy(c => c.At).Select(c => JsonSerializer.Serialize(c, Json)));
            File.Move(temp, file, overwrite: true);
            lines = known.Count;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(e, "The change journal could not be written");
        }
        return true;
    }

    private void Append(Change c)
    {
        try
        {
            File.AppendAllText(file, JsonSerializer.Serialize(c, Json) + "\n");
            lines++;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(e, "The change journal could not be written");
        }
    }
}

/// <summary>Putting a change back in Active Directory (DirectoryUndo.cs).</summary>
public interface IDirectoryUndo
{
    /// <summary>Checks the object still has what the change left, puts it back, and says what it did in a sentence.</summary>
    Task<string> Undo(Change change, CancellationToken stop);
}

/// <summary>/api/changes: one change, an object's changes, and undo, one at a time or several.</summary>
public static class ChangesApi
{
    private const string Gone = "This change is no longer in the change history. Changes are kept for 30 days.";

    public static void MapChangesApi(this WebApplication app)
    {
        app.MapGet("/api/changes/{id}", (string id, HttpContext context, ChangeJournal journal) =>
            journal.Find(id) is not { } c ? Results.NotFound(new { error = Gone })
            : !ChangeJournal.MaySee(context, c) ? Access.NotAllowed()
            : Results.Ok(journal.View(context, c)));

        // One object's changes, newest first, for its History tab.
        app.MapGet("/api/changes", (string? dn, string? guid, HttpContext context, ChangeJournal journal) =>
        {
            if (string.IsNullOrWhiteSpace(dn) && string.IsNullOrWhiteSpace(guid))
            {
                return Results.BadRequest(new { error = "Specify the object's distinguished name." });
            }
            if (!Access.Can(context, Permissions.ActivityView) && !Access.Can(context, Permissions.AdView))
            {
                return Access.NotAllowed();
            }
            return Results.Ok(journal.For(dn?.Trim(), guid?.Trim()).Where(c => ChangeJournal.MaySee(context, c)).Select(c => journal.View(context, c)));
        });

        app.MapPost("/api/changes/{id}/undo", (string id, HttpContext context, ChangeJournal journal, IDirectoryUndo tools, ActivityLog log) =>
            Undo(context, journal, tools, log, id, null));

        // Several at once, newest first so later changes to an object are put back before earlier ones. Each is answered on
        // its own; Confirm it's you is asked once, before any, when one of them needs it.
        app.MapPost("/api/changes/undo", async (UndoForm form, HttpContext context, ChangeJournal journal, IDirectoryUndo tools, ActivityLog log, Confirmations confirm) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            var ids = (form.Ids ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct().ToList();
            if (ids.Count is 0 or > 200)
            {
                return Results.BadRequest(new { error = "Select between 1 and 200 changes to undo." });
            }
            if (ids.Any(i => journal.Find(i) is { Confirm: true } c && journal.Blocked(c) is null) && confirm.Refuse(context) is { } unconfirmed)
            {
                return unconfirmed;
            }
            var results = new List<object>();
            var done = 0;
            string? said = null;
            foreach (var id in ids.OrderByDescending(i => journal.Find(i)?.At ?? DateTime.MinValue))
            {
                string? mine = null;
                var answer = await Undo(context, journal, tools, log, id, s => mine = s);
                if (mine is not null)
                {
                    done++;
                    said ??= mine;
                    results.Add(new { id, ok = true, said = mine });
                }
                else
                {
                    results.Add(new { id, ok = false, error = ErrorOf(answer) });
                }
            }
            return Results.Ok(new { results, done, said = done == 1 ? said : done > 1 ? $"{done} changes were undone." : null });
        });
    }

    private static async Task<IResult> Undo(HttpContext context, ChangeJournal journal, IDirectoryUndo tools, ActivityLog log, string id, Action<string>? done)
    {
        if (journal.Find(id) is not { } c)
        {
            return Results.NotFound(new { error = Gone });
        }
        if (journal.Blocked(c) is { } why)
        {
            return Results.Json(new { error = why }, statusCode: StatusCodes.Status409Conflict);
        }
        // The same permission as the change, inside the same scope, and Confirm it's you when the change took it.
        return await UsersApi.Try(context, c.Permission, async () =>
        {
            if (!journal.Claim(c.Id))
            {
                throw new ToolProblem("This change is already being undone.", StatusCodes.Status409Conflict);
            }
            try
            {
                ChangeNote.Current?.Undoing(c);
                var said = await tools.Undo(c, context.RequestAborted);
                log.Record(context, "users", "ad.undo", $"Undid: {c.Title}", detail: said);
                journal.Undone(c.Id, ActivityLog.Who(context), ChangeNote.Current?.Kept?.Id);
                done?.Invoke(said);
                return Results.Ok(new { said, undid = journal.View(context, c) });
            }
            finally
            {
                journal.Release(c.Id);
            }
        }, confirm: c.Confirm);
    }

    private static string ErrorOf(IResult answer)
    {
        if (answer is IValueHttpResult { Value: { } v } && JsonSerializer.SerializeToElement(v) is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty("error", out var e))
        {
            return e.GetString() ?? "";
        }
        return "The change could not be undone.";
    }

    /// <summary>An AD change's answer with the change it made: X-Change, and change in its JSON when it's an object.</summary>
    internal static IResult With(HttpContext context, IResult answer, ChangeBrief brief)
    {
        context.Response.Headers["X-Change"] = brief.Id;
        if (answer is IStatusCodeHttpResult { StatusCode: { } code } && code != StatusCodes.Status200OK)
        {
            return answer;
        }
        var options = context.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var node = answer switch
        {
            IValueHttpResult { Value: { } v } => JsonSerializer.SerializeToNode(v, v.GetType(), options),
            Microsoft.AspNetCore.Http.HttpResults.Ok => new JsonObject(),
            _ => null,
        };
        if (node is not JsonObject o)
        {
            return answer;
        }
        o["change"] = JsonSerializer.SerializeToNode(brief, options);
        return Results.Json(o, options);
    }
}
