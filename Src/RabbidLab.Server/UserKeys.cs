using System.Text.Json;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>Each person's keyboard shortcuts: the letter after G for each page, by the page's key.</summary>
public sealed class KeysFile
{
    public Dictionary<string, Dictionary<string, string>> People { get; set; } = [];
}

public sealed record KeysForm(Dictionary<string, string>? Keys);

/// <summary>
/// Keyboard shortcuts of a person's own, kept here in keys.json so they follow the person to any browser. A person with
/// none kept has the page's defaults; Reset puts those back. Only the letters are kept: the page knows what each key is.
/// </summary>
public sealed partial class UserKeys(DataFolder folder)
{
    public const int Most = 40;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<KeysFile> file = folder.Json<KeysFile>("keys.json", Json);

    [GeneratedRegex("^[a-z][a-z0-9-]{0,31}$")]
    private static partial Regex PageKey();

    // A local account and a domain account can share a name, so each is kept under where it signs in from too.
    public static string PersonOf(Who who) => $"{who.Source}:{who.Username}".ToLowerInvariant();

    public Dictionary<string, string> Of(string person)
    {
        lock (file.Gate)
        {
            return file.Read().People.TryGetValue(person, out var keys) ? new(keys) : [];
        }
    }

    /// <summary>The letters for this person: one letter or digit each, never the same one twice; "-" is no shortcut.</summary>
    public Dictionary<string, string> Set(string person, Dictionary<string, string>? keys)
    {
        var clean = new Dictionary<string, string>();
        foreach (var (page, key) in keys ?? [])
        {
            if (!PageKey().IsMatch(page ?? ""))
            {
                throw new ToolProblem("That is not a page that has a shortcut.");
            }
            var k = (key ?? "").Trim().ToLowerInvariant();
            if (k is "" or "-")
            {
                clean[page!] = "-";
                continue;
            }
            if (k.Length != 1 || !char.IsAsciiLetterOrDigit(k[0]))
            {
                throw new ToolProblem("Use one letter or digit for each page.");
            }
            if (clean.ContainsValue(k))
            {
                throw new ToolProblem($"G then {k.ToUpperInvariant()} is set for two pages. Give each page its own letter.");
            }
            clean[page!] = k;
        }
        if (clean.Count > Most)
        {
            throw new ToolProblem($"Up to {Most} shortcuts can be kept.");
        }
        lock (file.Gate)
        {
            var next = new KeysFile { People = new(file.Read().People) { [person] = clean } };
            file.Save(next);
        }
        return clean;
    }

    public void Reset(string person)
    {
        lock (file.Gate)
        {
            var people = new Dictionary<string, Dictionary<string, string>>(file.Read().People);
            if (people.Remove(person))
            {
                file.Save(new KeysFile { People = people });
            }
        }
    }
}

public static class UserKeysApi
{
    public static void MapUserKeys(this WebApplication app)
    {
        // Your own shortcuts, for anyone signed in.
        app.MapGet("/api/me/keys", (HttpContext context, UserKeys keys) => Results.Ok(new { keys = keys.Of(UserKeys.PersonOf(Who.Of(context.User))) }));

        app.MapPost("/api/me/keys", (KeysForm form, HttpContext context, UserKeys keys) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            try
            {
                return Results.Ok(new { keys = keys.Set(UserKeys.PersonOf(Who.Of(context.User)), form.Keys) });
            }
            catch (ToolProblem e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
        });

        app.MapPost("/api/me/keys/reset", (HttpContext context, UserKeys keys) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            keys.Reset(UserKeys.PersonOf(Who.Of(context.User)));
            return Results.Ok(new { keys = new Dictionary<string, string>() });
        });
    }
}
