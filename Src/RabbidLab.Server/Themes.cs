using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

namespace RabbidLab.Server;

/// <summary>
/// The themes besides Dark (Light, Carbon, Mocha, Slate), worked out from the app's own style sheets the first time they're
/// asked for: every colour in them turned for each theme, served as one style sheet, /Assets/themes.css, scoped by
/// html[data-theme]. theme.js sets data-theme before the page draws; theme-fixes.css has the few things this gets wrong.
/// A new style sheet or colour is in every theme with nothing more to do. Dark is the style sheets as they are.
/// </summary>
public static partial class Themes
{
    public static readonly string[] Names = ["light", "carbon", "mocha", "slate"];

    /// <summary>The order the pages load the style sheets in, so a theme's rules overrule each other as the originals do.</summary>
    private static readonly string[] Order = ["console.css", "grid.css", "people.css", "users.css", "links.css", "sensors.css", "alerts.css",
        "wiki.css", "notes.css", "signin.css", "edition.css", "edition-dev.css"];

    /// <summary>Not turned: this one is made here, and the fixes are already written for the themes.</summary>
    private static readonly string[] Skip = ["themes.css", "theme-fixes.css"];

    // Tinted darks: the hue, how much of it in the dark layers and in the light text, and how far the dark layers are lifted.
    private static readonly Dictionary<string, (double Hue, double Dark, double Text, double Times, double Lift)> Tints = new()
    {
        ["carbon"] = (0, 0, 0, 1.28, 0.028),
        ["mocha"] = (26, 0.16, 0.12, 1.3, 0.032),
        ["slate"] = (214, 0.16, 0.07, 1.3, 0.03),
    };

    // Light: the darkest layers go light grey (the page), panels and boxes white, lines mid grey, light text dark.
    private static readonly (double From, double To)[] Light =
        [(0, 0.94), (0.065, 0.945), (0.085, 1), (0.11, 1), (0.13, 0.955), (0.17, 0.89), (0.22, 0.83), (0.3, 0.76), (0.55, 0.47), (0.8, 0.26), (1, 0.1)];

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"url\([^)]*\)|#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})\b|rgba?\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*(?:,\s*[\d.]+\s*)?\)")]
    private static partial Regex Colour();

    /// <summary>The properties a theme writes again under its own name, colour or not, so a rule that sets one with a variable
    /// still wins over another rule's turned colour as it did before.</summary>
    [GeneratedRegex(@"^(--|color$|background(-color|-image)?$|border(-(top|right|bottom|left|block|inline)(-(start|end))?)?(-color)?$|outline(-color)?$|box-shadow$|text-shadow$|fill$|stroke$|caret-color$|accent-color$|text-decoration(-color)?$|column-rule(-color)?$|color-scheme$)")]
    private static partial Regex Painted();

    private static readonly object Gate = new();
    private static (string Css, string Tag)? made;
    private static byte[]? zipped;

    /// <summary>themes.css gzipped, for a browser that takes it: a tenth of the size.</summary>
    private static byte[] Zipped(string css)
    {
        lock (Gate)
        {
            if (zipped is null)
            {
                using var o = new MemoryStream();
                using (var gz = new System.IO.Compression.GZipStream(o, System.IO.Compression.CompressionLevel.Optimal))
                {
                    gz.Write(Encoding.UTF8.GetBytes(css));
                }
                zipped = o.ToArray();
            }
            return zipped;
        }
    }

    /// <summary>themes.css: made once from the style sheets in Assets.</summary>
    public static (string Css, string Tag) Sheet(IFileProvider web)
    {
        lock (Gate)
        {
            if (made is { } ready)
            {
                return ready;
            }
            var files = web.GetDirectoryContents("Assets").Where(f => !f.IsDirectory && f.Name.EndsWith(".css", StringComparison.OrdinalIgnoreCase) && !Skip.Contains(f.Name))
                .OrderBy(f => Array.IndexOf(Order, f.Name) is var i && i >= 0 ? i : Order.Length).ThenBy(f => f.Name, StringComparer.Ordinal);
            var sheets = files.Select(f =>
            {
                using var reader = new StreamReader(f.CreateReadStream());
                return reader.ReadToEnd();
            }).ToList();
            var css = Build(sheets);
            made = (css, $"\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(css)))[..16]}\"");
            return made.Value;
        }
    }

    /// <summary>Every theme's rules for these style sheets, in this order.</summary>
    public static string Build(IReadOnlyList<string> sheets)
    {
        var o = new StringBuilder("/* Made by Themes.cs from the style sheets in Assets; theme.js picks one with data-theme on <html>. */\n");
        foreach (var theme in Names)
        {
            foreach (var sheet in sheets)
            {
                Rules(o, theme, Comment().Replace(sheet, ""), 0, sheet.Length);
            }
        }
        return o.ToString();
    }

    // Walks the rules between from and to: a style rule gets its theme's copy, @media and @supports are walked into, and the
    // rest (@keyframes, @font-face) are left alone.
    private static void Rules(StringBuilder o, string theme, string css, int from, int to)
    {
        var at = from;
        to = Math.Min(to, css.Length);
        while (at < to)
        {
            var open = css.IndexOf('{', at);
            if (open < 0 || open >= to)
            {
                return;
            }
            var close = Matching(css, open);
            var head = css[at..open].Trim();
            if (head.StartsWith("@media", StringComparison.Ordinal) || head.StartsWith("@supports", StringComparison.Ordinal))
            {
                var inner = new StringBuilder();
                Rules(inner, theme, css, open + 1, close);
                if (inner.Length > 0)
                {
                    o.Append(head).Append(" {\n").Append(inner).Append("}\n");
                }
            }
            else if (!head.StartsWith('@') && head.Length > 0)
            {
                Rule(o, theme, head, css[(open + 1)..close]);
            }
            at = close + 1;
        }
    }

    private static int Matching(string css, int open)
    {
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}' && --depth == 0)
            {
                return i;
            }
        }
        return css.Length - 1;
    }

    private static void Rule(StringBuilder o, string theme, string selectors, string body)
    {
        // White on the accent (Sign in, the tab that's on) stays white in every theme.
        var onAccent = Regex.IsMatch(body, @"background(-color)?\s*:[^;]*var\(--(accent|ember)");
        var kept = new List<string>();
        foreach (var part in body.Split(';'))
        {
            var colon = part.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }
            var name = part[..colon].Trim();
            var value = part[(colon + 1)..].Trim();
            if (!Painted().IsMatch(name))
            {
                continue;
            }
            if (name == "color-scheme")
            {
                if (theme == "light")
                {
                    kept.Add($"{name}: {value.Replace("dark", "light")}");
                }
                continue;
            }
            kept.Add($"{name}: {Turn(value, theme, onAccent)}");
        }
        if (kept.Count == 0)
        {
            return;
        }
        var scoped = string.Join(", ", Split(selectors).Select(s => Scope(s.Trim(), theme)));
        o.Append(scoped).Append(" { ").Append(string.Join("; ", kept)).Append("; }\n");
    }

    /// <summary>A selector list split at its own commas, not the ones inside :is(), :not() or :has().</summary>
    private static List<string> Split(string selectors)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < selectors.Length; i++)
        {
            var c = selectors[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                parts.Add(selectors[start..i]);
                start = i + 1;
            }
        }
        parts.Add(selectors[start..]);
        return parts;
    }

    /// <summary>A selector for one theme only: under html[data-theme], so it outranks the original by the same step each time.</summary>
    public static string Scope(string selector, string theme)
    {
        var tag = $"[data-theme=\"{theme}\"]";
        if (selector.StartsWith(":root", StringComparison.Ordinal))
        {
            return $":root{tag}{selector[5..]}";
        }
        if (selector == "html" || selector.StartsWith("html ", StringComparison.Ordinal) || selector.StartsWith("html.", StringComparison.Ordinal) || selector.StartsWith("html[", StringComparison.Ordinal))
        {
            return $"html{tag}{selector[4..]}";
        }
        return $"{tag} {selector}";
    }

    /// <summary>Every colour in a value turned for the theme; url(…) left as it is.</summary>
    public static string Turn(string value, string theme, bool onAccent = false) => Colour().Replace(value, m =>
    {
        var v = m.Value;
        if (v.StartsWith("url(", StringComparison.Ordinal))
        {
            return v;
        }
        if (onAccent && Regex.IsMatch(v, "^#(fff|ffffff|f2f3fa)$", RegexOptions.IgnoreCase))
        {
            return v;
        }
        double r, g, b, a = 1;
        if (v.StartsWith('#'))
        {
            var x = v[1..];
            if (x.Length == 3)
            {
                x = string.Concat(x.Select(c => $"{c}{c}"));
            }
            r = Convert.ToInt32(x[..2], 16);
            g = Convert.ToInt32(x[2..4], 16);
            b = Convert.ToInt32(x[4..6], 16);
        }
        else
        {
            var n = Regex.Matches(v, @"[\d.]+").Select(d => double.Parse(d.Value, CultureInfo.InvariantCulture)).ToArray();
            (r, g, b) = (n[0], n[1], n[2]);
            if (n.Length > 3)
            {
                a = n[3];
            }
        }
        return Format(Map(r, g, b, a, theme));
    });

    private static (double R, double G, double B, double A) Map(double r, double g, double b, double a, string theme)
    {
        var (h, s, l) = ToHsl(r, g, b);
        var spread = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
        var neutral = s < 0.3 || spread < 36;
        if (theme == "light")
        {
            if (a < 1 && r > 225 && g > 225 && b > 225)
            {
                return (14, 16, 32, Math.Round(a * 0.9, 3)); // a white wash: a dark one
            }
            if (a < 1 && l < 0.08)
            {
                return a >= 0.3 ? (255, 255, 255, Math.Round(Math.Min(0.85, a + 0.15), 3)) : (20, 24, 44, Math.Round(a * 0.28, 3)); // dark glass: white glass
            }
            if (neutral)
            {
                return With(FromHsl(h, s * 0.55, Along(l, Light)), a);
            }
            return l > 0.62 ? With(FromHsl(h, Math.Min(1, s * 1.05), l * 0.68), a) : (r, g, b, a); // pastel text: darker
        }
        if (Tints.TryGetValue(theme, out var t) && neutral)
        {
            return l < 0.3 ? With(FromHsl(t.Hue / 360, t.Dark, l * t.Times + t.Lift), a) : With(FromHsl(t.Hue / 360, t.Text, l), a);
        }
        return (r, g, b, a);
    }

    private static (double, double, double, double) With((double R, double G, double B) c, double a) => (c.R, c.G, c.B, a);

    private static double Along(double x, (double From, double To)[] points)
    {
        if (x <= points[0].From)
        {
            return points[0].To;
        }
        for (var i = 1; i < points.Length; i++)
        {
            if (x <= points[i].From)
            {
                var (x0, y0) = points[i - 1];
                var (x1, y1) = points[i];
                return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
            }
        }
        return points[^1].To;
    }

    private static string Format((double R, double G, double B, double A) c)
    {
        int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 255));
        return c.A < 1
            ? $"rgba({Byte(c.R)}, {Byte(c.G)}, {Byte(c.B)}, {c.A.ToString(CultureInfo.InvariantCulture)})"
            : $"#{Byte(c.R):X2}{Byte(c.G):X2}{Byte(c.B):X2}";
    }

    private static (double H, double S, double L) ToHsl(double r, double g, double b)
    {
        r /= 255;
        g /= 255;
        b /= 255;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min)
        {
            return (0, 0, l);
        }
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    private static (double R, double G, double B) FromHsl(double h, double s, double l)
    {
        if (s == 0)
        {
            return (l * 255, l * 255, l * 255);
        }
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        double Hue(double t)
        {
            if (t < 0)
            {
                t += 1;
            }
            if (t > 1)
            {
                t -= 1;
            }
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }
        return (Hue(h + 1.0 / 3) * 255, Hue(h) * 255, Hue(h - 1.0 / 3) * 255);
    }

    public static void MapThemesApi(this WebApplication app)
    {
        // Open like the rest of Assets (the sign-in page wears the theme too); a 304 while it hasn't changed.
        app.MapGet("/Assets/themes.css", (HttpContext context) =>
        {
            var (css, tag) = Sheet(app.Environment.WebRootFileProvider);
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.ETag = tag;
            context.Response.Headers.Vary = "Accept-Encoding";
            if (context.Request.Headers.IfNoneMatch == tag)
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }
            if (context.Request.Headers.AcceptEncoding.ToString().Contains("gzip", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.ContentEncoding = "gzip";
                return Results.Bytes(Zipped(css), "text/css; charset=utf-8");
            }
            return Results.Text(css, "text/css; charset=utf-8");
        });
    }
}
