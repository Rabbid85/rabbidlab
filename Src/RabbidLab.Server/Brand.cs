using System.Text.Json;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>A change to the lab's look. Null leaves that part as it is; an empty string puts Rabbid Lab's own back.</summary>
public sealed record BrandForm(string? Name, string? Icon, string? Accent, string? Tagline = null, string? Background = null,
    string? Card = null, string? Style = null, int? Glass = null, string? Size = null, string? Quote = null, string? QuoteKind = null,
    IReadOnlyList<SignInQuote>? Quotes = null, JsonElement? Look = null);

/// <summary>One of the lab's own quotes for the sign-in card, and who said it (none: no name under it).</summary>
public sealed record SignInQuote(string Text, string? By);

/// <summary>
/// The lab's own look, the same for everyone who signs in: its name in the top bar and the browser tab, its icon, and the
/// accent colour. Set under Settings, Appearance; nothing set means Rabbid Lab's own.
/// </summary>
public static partial class Brand
{
    /// <summary>The lab's name as last read from the settings, for messages (Product.Lab); null when it has none.</summary>
    public static string? Named { get; internal set; }

    public const int NameLength = 40;
    public const int TaglineLength = 60;
    public const int IconBytes = 256 * 1024;
    private static readonly string[] IconKinds = ["data:image/png;base64,", "data:image/svg+xml;base64,"];

    /// <summary>The tags a tagline may hold; the page fills them in (brand.js fill).</summary>
    public static readonly string[] Tags = ["name", "username", "hostname", "domain", "version", "role"];

    /// <summary>What shows behind the sign-in card (backdrop.js draws them); none is the page's plain colour.</summary>
    public static readonly string[] Backgrounds = ["none", "squares", "bubbles", "glass", "net", "fireflies", "fizz", "lava", "stars", "embers"];

    /// <summary>The sign-in card: the whole card in the style, the left side open on the background, or the left side in the style and the form solid.</summary>
    public static readonly string[] Cards = ["whole", "open", "split"];

    /// <summary>The style of the sign-in card: see-through glass (with an opacity; black is the one it starts with), nearly solid, or the console's card colour.</summary>
    public static readonly string[] Styles = ["glass", "frost", "black", "dark", "solid"];

    /// <summary>The size of the sign-in card: as it was, a little larger, or larger (the left side grows most).</summary>
    public static readonly string[] Sizes = ["now", "larger", "largest"];

    /// <summary>When the quote on the sign-in card changes, and which kind it is (own: only the lab's own quotes).</summary>
    public static readonly string[] QuoteModes = ["visit", "day", "off"];
    public static readonly string[] QuoteKinds = ["motivational", "funny", "wisdom", "mix", "own"];

    public const int GlassLeast = 20;
    public const int GlassMost = 95;
    public const int QuoteCount = 50;
    public const int QuoteLength = 200;
    public const int QuoteByLength = 60;
    public const int LookLength = 4096;

    private static string Pick(string[] from, string? value, string fallback) => from.Contains(value, StringComparer.Ordinal) ? value! : fallback;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Hex();

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex Tag();

    public static object Read(ConsoleSettings s) => new
    {
        name = s.LabName, icon = s.LabIcon, accent = s.Accent, tagline = s.LabTagline,
        // One picked before it left the list (such as Data rain or Ink) shows as Squares.
        background = Backgrounds.Contains(s.SignInBackground, StringComparer.Ordinal) ? s.SignInBackground : "squares",
        card = Pick(Cards, s.SignInCard, "whole"),
        style = Pick(Styles, s.SignInStyle, "black"),
        glass = Math.Clamp(s.SignInGlass ?? 52, GlassLeast, GlassMost),
        size = Pick(Sizes, s.SignInSize, "larger"),
        quote = Pick(QuoteModes, s.SignInQuote, "visit"),
        quoteKind = Pick(QuoteKinds, s.SignInQuoteKind, "motivational"),
        quotes = s.SignInQuotes ?? [],
        look = LookOf(s.LabLook),
    };

    // The lab's look as the console sent it; one that no longer reads is none.
    private static JsonElement? LookOf(string? json)
    {
        if (json is null)
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Why a change can't be saved, or null when it can.</summary>
    public static string? Problem(BrandForm form)
    {
        if (form.Name is { Length: > 0 } name && (name.Trim().Length > NameLength || name.Any(char.IsControl)))
        {
            return $"The name can be up to {NameLength} characters on one line.";
        }
        if (form.Icon is { Length: > 0 } icon)
        {
            var kind = IconKinds.FirstOrDefault(k => icon.StartsWith(k, StringComparison.Ordinal));
            if (kind is null)
            {
                return "The icon must be a PNG or SVG file.";
            }
            var data = icon[kind.Length..];
            if (data.Length > IconBytes * 4 / 3 + 4 || !Convert.TryFromBase64String(data, new byte[data.Length], out var bytes))
            {
                return $"The icon is not a PNG or SVG file {Product.Title} can read, or it is larger than 256 KB.";
            }
            if (bytes > IconBytes)
            {
                return "The icon must be smaller than 256 KB.";
            }
        }
        if (form.Accent is { Length: > 0 } accent && !Hex().IsMatch(accent))
        {
            return "The accent colour must be a hex colour, such as #3D7FE6.";
        }
        if (form.Tagline is { Length: > 0 } tagline)
        {
            if (tagline.Trim().Length > TaglineLength || tagline.Any(char.IsControl))
            {
                return $"The tagline can be up to {TaglineLength} characters on one line.";
            }
            if (Tag().Matches(tagline).Select(m => m.Groups[1].Value).FirstOrDefault(t => !Tags.Contains(t, StringComparer.Ordinal)) is { } unknown)
            {
                return $"{{{unknown}}} is not a valid tag. Use {string.Join(", ", Tags.Select(t => $"{{{t}}}"))}.";
            }
        }
        if (form.Background is { Length: > 0 } background && !Backgrounds.Contains(background, StringComparer.Ordinal))
        {
            return "Select a sign-in background from the list.";
        }
        if (form.Card is { Length: > 0 } card && !Cards.Contains(card, StringComparer.Ordinal))
        {
            return "Select a sign-in card from the list.";
        }
        if (form.Style is { Length: > 0 } style && !Styles.Contains(style, StringComparer.Ordinal))
        {
            return "Select a sign-in card style from the list.";
        }
        if (form.Glass is { } glass && (glass < GlassLeast || glass > GlassMost))
        {
            return $"The glass opacity must be between {GlassLeast}% and {GlassMost}%.";
        }
        if (form.Size is { Length: > 0 } size && !Sizes.Contains(size, StringComparer.Ordinal))
        {
            return "Select a sign-in card size from the list.";
        }
        if (form.Quote is { Length: > 0 } quote && !QuoteModes.Contains(quote, StringComparer.Ordinal))
        {
            return "Select when the quote changes from the list.";
        }
        if (form.QuoteKind is { Length: > 0 } quoteKind && !QuoteKinds.Contains(quoteKind, StringComparer.Ordinal))
        {
            return "Select a kind of quote from the list.";
        }
        if (form.Quotes is { } quotes)
        {
            if (quotes.Count > QuoteCount)
            {
                return $"The lab can have up to {QuoteCount} quotes of its own.";
            }
            foreach (var q in quotes)
            {
                if (q is null || string.IsNullOrWhiteSpace(q.Text) || q.Text.Trim().Length > QuoteLength || q.Text.Any(char.IsControl))
                {
                    return $"Each quote must be on one line and up to {QuoteLength} characters.";
                }
                if (q.By is { } by && (by.Trim().Length > QuoteByLength || by.Any(char.IsControl)))
                {
                    return $"The name under a quote can be up to {QuoteByLength} characters.";
                }
            }
        }
        if (form.Look is { } look && (look.ValueKind != JsonValueKind.Object || look.GetRawText().Length > LookLength))
        {
            return "The lab's look could not be saved. Reload the page and try again.";
        }
        return null;
    }

    /// <summary>Applies a change that has passed Problem: empty strings clear back to Rabbid Lab's own.</summary>
    public static void Apply(ConsoleSettings s, BrandForm form)
    {
        if (form.Name is not null)
        {
            s.LabName = form.Name.Trim() is { Length: > 0 } n ? n : null;
        }
        if (form.Icon is not null)
        {
            s.LabIcon = form.Icon.Length > 0 ? form.Icon : null;
        }
        if (form.Accent is not null)
        {
            s.Accent = form.Accent.Length > 0 ? form.Accent.ToUpperInvariant() : null;
        }
        if (form.Tagline is not null)
        {
            s.LabTagline = form.Tagline.Trim() is { Length: > 0 } t ? t : null;
        }
        if (form.Background is not null)
        {
            s.SignInBackground = form.Background.Length > 0 ? form.Background : null;
        }
        if (form.Card is not null)
        {
            s.SignInCard = form.Card.Length > 0 ? form.Card : null;
        }
        if (form.Style is not null)
        {
            s.SignInStyle = form.Style.Length > 0 ? form.Style : null;
        }
        if (form.Glass is { } glass)
        {
            s.SignInGlass = glass;
        }
        if (form.Size is not null)
        {
            s.SignInSize = form.Size.Length > 0 ? form.Size : null;
        }
        if (form.Quote is not null)
        {
            s.SignInQuote = form.Quote.Length > 0 ? form.Quote : null;
        }
        if (form.QuoteKind is not null)
        {
            s.SignInQuoteKind = form.QuoteKind.Length > 0 ? form.QuoteKind : null;
        }
        if (form.Quotes is { } quotes)
        {
            s.SignInQuotes = quotes.Count > 0 ? quotes.Select(q => new SignInQuote(q.Text.Trim(), q.By?.Trim() is { Length: > 0 } by ? by : null)).ToList() : null;
        }
        if (form.Look is { } look)
        {
            // An empty object puts Rabbid Lab's own back.
            s.LabLook = look.EnumerateObject().Any() ? look.GetRawText() : null;
        }
    }

    public static void MapBrandApi(this WebApplication app)
    {
        // A read: anyone signed in, with no header asked for (Access.Refuse is for changes).
        app.MapGet("/api/brand", (SettingsStore settings) => Results.Ok(Read(settings.Current)));

        app.MapPost("/api/brand", (BrandForm form, HttpContext context, SettingsStore settings, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.AppearanceBrand) is { } refused)
            {
                return refused;
            }
            if (Problem(form) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            settings.Change(s => Apply(s, form));
            var what = new[] { form.Name is null ? null : "name", form.Icon is null ? null : "icon", form.Accent is null ? null : "accent colour", form.Tagline is null ? null : "tagline",
                form.Background is null ? null : "sign-in background", form.Card is null ? null : "sign-in card", form.Style is null ? null : "sign-in card style",
                form.Glass is null ? null : "sign-in glass opacity", form.Size is null ? null : "sign-in card size", form.Quote is null ? null : "sign-in quote",
                form.QuoteKind is null ? null : "kind of quote", form.Quotes is null ? null : "own quotes", form.Look is null ? null : "default look" }.OfType<string>();
            log.Record(context, "admin", "brand", $"Changed the lab's {string.Join(" and ", what)}");
            return Results.Ok(Read(settings.Current));
        });
    }
}
