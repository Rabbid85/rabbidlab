using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>A change to the lab's look. Null leaves that part as it is; an empty string puts Rabbid Lab's own back.</summary>
public sealed record BrandForm(string? Name, string? Icon, string? Accent, string? Tagline = null, string? Background = null);

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

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Hex();

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex Tag();

    public static object Read(ConsoleSettings s) => new
    {
        name = s.LabName, icon = s.LabIcon, accent = s.Accent, tagline = s.LabTagline,
        // One picked before it left the list (such as Data rain or Ink) shows as Squares.
        background = Backgrounds.Contains(s.SignInBackground, StringComparer.Ordinal) ? s.SignInBackground : "squares",
    };

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
                form.Background is null ? null : "sign-in background" }.OfType<string>();
            log.Record(context, "admin", "brand", $"Changed the lab's {string.Join(" and ", what)}");
            return Results.Ok(Read(settings.Current));
        });
    }
}
