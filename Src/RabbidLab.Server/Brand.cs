using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>A change to the lab's look. Null leaves that part as it is; an empty string puts Rabbid Lab's own back.</summary>
public sealed record BrandForm(string? Name, string? Icon, string? Accent);

/// <summary>
/// The lab's own look, the same for everyone who signs in: its name in the top bar and the browser tab, its icon, and the
/// accent colour. Set under Settings, Appearance; nothing set means Rabbid Lab's own.
/// </summary>
public static partial class Brand
{
    /// <summary>The lab's name as last read from the settings, for messages (Product.Title); null when it has none.</summary>
    public static string? Named { get; internal set; }

    public const int NameLength = 40;
    public const int IconBytes = 256 * 1024;
    private static readonly string[] IconKinds = ["data:image/png;base64,", "data:image/svg+xml;base64,"];

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Hex();

    public static object Read(ConsoleSettings s) => new { name = s.LabName, icon = s.LabIcon, accent = s.Accent };

    /// <summary>Why a change can't be saved, or null when it can.</summary>
    public static string? Problem(BrandForm form)
    {
        if (form.Name is { Length: > 0 } name && (name.Trim().Length > NameLength || name.Any(char.IsControl)))
        {
            return $"Keep the name to {NameLength} characters, on one line.";
        }
        if (form.Icon is { Length: > 0 } icon)
        {
            var kind = IconKinds.FirstOrDefault(k => icon.StartsWith(k, StringComparison.Ordinal));
            if (kind is null)
            {
                return "The icon has to be a PNG or SVG file.";
            }
            var data = icon[kind.Length..];
            if (data.Length > IconBytes * 4 / 3 + 4 || !Convert.TryFromBase64String(data, new byte[data.Length], out var bytes))
            {
                return $"That icon isn't a PNG or SVG {Product.Title} can read, or it's over 256 KB.";
            }
            if (bytes > IconBytes)
            {
                return "Keep the icon under 256 KB.";
            }
        }
        if (form.Accent is { Length: > 0 } accent && !Hex().IsMatch(accent))
        {
            return "The accent has to be a colour such as #3D7FE6.";
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
            var what = new[] { form.Name is null ? null : "name", form.Icon is null ? null : "icon", form.Accent is null ? null : "accent colour" }.OfType<string>();
            log.Record(context, "admin", "brand", $"Changed the lab's {string.Join(" and ", what)}");
            return Results.Ok(Read(settings.Current));
        });
    }
}
