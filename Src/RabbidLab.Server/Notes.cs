using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>A note someone left on a computer, an AD object or a GPO: a little formatted text, who wrote it and when.</summary>
public sealed class Note
{
    public string Id { get; set; } = "";

    /// <summary>What it's on: seat:{client id}, ad:{SID} or gpo:{GPO id}.</summary>
    public string On { get; set; } = "";

    /// <summary>The text, as the few HTML tags the editor makes (Notes.BadText).</summary>
    public string Text { get; set; } = "";

    public string By { get; set; } = "";
    public DateTime At { get; set; }
    public DateTime? Changed { get; set; }
}

public sealed class NotesFile
{
    public List<Note> Notes { get; set; } = [];
}

public sealed record NoteForm(string? On, string? Text, string? Name = null);

/// <summary>
/// The notes, kept in notes.json in the data folder. Anyone who can see a thing can read its notes and add one; people
/// change and delete their own, and whoever may change that kind of thing can delete anyone's.
/// </summary>
public sealed partial class Notes(DataFolder folder, TimeProvider clock)
{
    public const int MaxText = 10_000;
    public const int MaxOnOne = 300;
    public const int MaxAll = 20_000;

    // seat:, ad: or gpo:, then letters, digits and - { } . only.
    [GeneratedRegex(@"^(seat|ad|gpo):[A-Za-z0-9{}.-]{1,100}$")]
    private static partial Regex OnShape();

    // A tag: its name and what follows it inside the brackets.
    [GeneratedRegex(@"<(/?)([a-zA-Z0-9]+)([^<>]*)>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"^ href=""(https?://|mailto:)[^""<>]*""$")]
    private static partial Regex Href();

    private static readonly HashSet<string> Tags = ["b", "i", "u", "s", "code", "pre", "blockquote", "ul", "ol", "li", "br", "p", "div", "h3", "a"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<NotesFile> file = folder.Json<NotesFile>("notes.json", Json);

    public static bool Fits(string? on) => on is not null && OnShape().IsMatch(on);

    /// <summary>The permission that shows what a note is on, and the one that deletes anyone's notes there.</summary>
    public static (string See, string Tidy) PermissionsFor(string on) => on.Split(':')[0] switch
    {
        "seat" => (Permissions.SeatsView, Permissions.SeatsActions),
        "ad" => (Permissions.AdView, Permissions.AdChange),
        _ => (Permissions.GpoView, Permissions.GpoChange),
    };

    /// <summary>The activity log's area for what a note is on.</summary>
    public static string Area(string on) => on.StartsWith("seat:", StringComparison.Ordinal) ? "clients" : "users";

    /// <summary>Why the text can't be kept, or null when it can: only the editor's own tags, and links to web or mail addresses.</summary>
    public static string? BadText(string? text)
    {
        if (string.IsNullOrWhiteSpace(Regex.Replace(text ?? "", "<[^>]*>|&nbsp;", "")))
        {
            return "Enter the note text.";
        }
        if (text!.Length > MaxText)
        {
            return $"Notes can be up to {MaxText:N0} characters.";
        }
        if (text.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
        {
            return "The note contains characters that are not allowed.";
        }
        foreach (Match m in Tag().Matches(text))
        {
            var name = m.Groups[2].Value.ToLowerInvariant();
            var rest = m.Groups[3].Value;
            var ok = Tags.Contains(name) && (rest.Length == 0 || (name == "a" && m.Groups[1].Value.Length == 0 && Href().IsMatch(rest)));
            if (!ok)
            {
                return "The note contains formatting that is not supported.";
            }
        }
        // Outside the tags, < and > only as &lt; and &gt;.
        return Tag().Replace(text, "").IndexOfAny(['<', '>']) >= 0 ? "The note contains formatting that is not supported." : null;
    }

    public IReadOnlyList<Note> On(string on)
    {
        lock (file.Gate)
        {
            return [.. file.Read().Notes.Where(n => n.On == on).OrderByDescending(n => n.At)];
        }
    }

    /// <summary>How many notes each thing has, for the ones starting with prefix (seat:, ad: or gpo:).</summary>
    public IReadOnlyDictionary<string, int> Counts(string prefix)
    {
        lock (file.Gate)
        {
            return file.Read().Notes.Where(n => n.On.StartsWith(prefix, StringComparison.Ordinal)).GroupBy(n => n.On).ToDictionary(g => g.Key, g => g.Count());
        }
    }

    public Note? Find(string id)
    {
        lock (file.Gate)
        {
            return file.Read().Notes.FirstOrDefault(n => n.Id == id);
        }
    }

    public (Note? Note, string? Problem) Add(string on, string text, string by)
    {
        if (!Fits(on))
        {
            return (null, "Notes are not supported on this item.");
        }
        if (BadText(text) is { } bad)
        {
            return (null, bad);
        }
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Notes.Count(n => n.On == on) >= MaxOnOne)
            {
                return (null, $"This item has the maximum of {MaxOnOne} notes. Delete some first.");
            }
            if (f.Notes.Count >= MaxAll)
            {
                return (null, $"The maximum of {MaxAll:N0} notes has been reached. Delete some first.");
            }
            var note = new Note { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(), On = on, Text = text, By = by, At = clock.GetUtcNow().UtcDateTime };
            f.Notes.Add(note);
            file.Save(f);
            return (note, null);
        }
    }

    public (Note? Note, string? Problem) Change(string id, string text)
    {
        if (BadText(text) is { } bad)
        {
            return (null, bad);
        }
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Notes.FirstOrDefault(n => n.Id == id) is not { } note)
            {
                return (null, "Note not found. It may have been deleted.");
            }
            (note.Text, note.Changed) = (text, clock.GetUtcNow().UtcDateTime);
            file.Save(f);
            return (note, null);
        }
    }

    public Note? Delete(string id)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Notes.FirstOrDefault(n => n.Id == id) is not { } note)
            {
                return null;
            }
            f.Notes.Remove(note);
            file.Save(f);
            return note;
        }
    }
}

/// <summary>/api/notes: read, add, change and delete notes, each change in the activity log.</summary>
public static class NotesApi
{
    public static void MapNotesApi(this WebApplication app)
    {
        // The notes on one thing, newest first, each saying whether this person may change or delete it.
        app.MapGet("/api/notes", (string? on, HttpContext context, Notes notes) =>
        {
            if (!Notes.Fits(on))
            {
                return Results.BadRequest(new { error = "Notes are not supported on this item." });
            }
            // A read: the role decides, as for every list (the header is only for changes).
            var (see, tidy) = Notes.PermissionsFor(on!);
            if (!Access.Can(context, see))
            {
                return Access.NotAllowed();
            }
            var me = ActivityLog.Who(context);
            var all = Access.Can(context, tidy);
            return Results.Ok(notes.On(on!).Select(n => new { id = n.Id, text = n.Text, by = n.By, at = n.At, changed = n.Changed, mine = Same(n.By, me), canDelete = all || Same(n.By, me) }));
        });

        // How many notes each thing of one kind has, for the tab counts and the lists.
        app.MapGet("/api/notes/counts", (string? kind, HttpContext context, Notes notes) =>
        {
            if (kind is not ("seat" or "ad" or "gpo"))
            {
                return Results.BadRequest(new { error = "Specify seat, ad or gpo." });
            }
            return !Access.Can(context, Notes.PermissionsFor(kind + ":x").See) ? Access.NotAllowed() : Results.Ok(notes.Counts(kind + ":"));
        });

        app.MapPost("/api/notes", (NoteForm form, HttpContext context, Notes notes, ActivityLog log) =>
        {
            if (!Notes.Fits(form.On))
            {
                return Results.BadRequest(new { error = "Notes are not supported on this item." });
            }
            if (Access.RefuseUnless(context, Notes.PermissionsFor(form.On!).See) is { } refused)
            {
                return refused;
            }
            var (note, problem) = notes.Add(form.On!, form.Text ?? "", ActivityLog.Who(context));
            if (note is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, Notes.Area(note.On), "note.add", $"Added a note on {Named(form.Name, note.On)}");
            return Results.Ok(new { id = note.Id });
        });

        // Only its writer changes a note.
        app.MapPost("/api/notes/{id}", (string id, NoteForm form, HttpContext context, Notes notes, ActivityLog log) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            if (notes.Find(id) is not { } was)
            {
                return Results.NotFound(new { error = "Note not found. It may have been deleted." });
            }
            if (!Access.Can(context, Notes.PermissionsFor(was.On).See) || !Same(was.By, ActivityLog.Who(context)))
            {
                return Results.Json(new { error = "Only the author of a note can edit it." }, statusCode: StatusCodes.Status403Forbidden);
            }
            var (note, problem) = notes.Change(id, form.Text ?? "");
            if (note is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, Notes.Area(note.On), "note.change", $"Changed a note on {Named(form.Name, note.On)}");
            return Results.Ok();
        });

        // Its writer deletes a note, and so does whoever may change that kind of thing.
        app.MapPost("/api/notes/{id}/delete", (string id, NoteForm? form, HttpContext context, Notes notes, ActivityLog log) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            if (notes.Find(id) is not { } was)
            {
                return Results.NotFound(new { error = "Note not found. It may have been deleted." });
            }
            var (see, tidy) = Notes.PermissionsFor(was.On);
            if (!Access.Can(context, see) || !(Same(was.By, ActivityLog.Who(context)) || Access.Can(context, tidy)))
            {
                return Results.Json(new { error = "Only the author, or a user with permission to edit the item, can delete a note." }, statusCode: StatusCodes.Status403Forbidden);
            }
            notes.Delete(id);
            log.Record(context, Notes.Area(was.On), "note.delete", $"Deleted {(Same(was.By, ActivityLog.Who(context)) ? "their" : $"{was.By}'s")} note on {Named(form?.Name, was.On)}");
            return Results.Ok();
        });
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // The name the page gave, for the log, or what it's on when it gave none.
    private static string Named(string? name, string on) =>
        name?.Trim() is { Length: > 0 and <= 120 } n && !n.Any(char.IsControl) ? n : on;
}
