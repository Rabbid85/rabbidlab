namespace RabbidLab.Tests;

/// <summary>The small parts every page shares are written once (parts.js, console.js, grid.js), never again in a page's own script.</summary>
public sealed class SharedPartsTests
{
    private static readonly string Web = Path.Combine(ReleaseKeyTests.Root(), "Src", "RabbidLab.Server", "wwwroot");

    [Fact]
    public void No_page_script_has_its_own_copy_of_a_shared_part()
    {
        // 1.1.442 took 150 copies of these out of 26 scripts. A page takes them from RabbidLab: const { none, section } = R.
        string[] once =
        [
            "const none = (text) => `<p class=\"muted dw-empty\">", "const oops = (e) =>", "class=\"icon dw-close\" data-dw-close",
            "const strip = (pairs) => `<div class=\"dw-strip\">${pairs.map(([l, v]) => `<div><span class=\"l\">${esc(l)}", "const badge = (text, tone) => `<span class=\"ubadge",
            "const qa = (attr, icon, label) =>", "const muted = (", "const chip = (text, cls",
            "const section = (title, body, right = '') => `<section class=\"dw-sec\"><div class=\"dw-sec-head\"><h3>${title}</h3>${right}</div>${body}</section>`;",
            "(title, body) => `<section class=\"dw-sec\"><div class=\"dw-sec-head\"><h3>${title}</h3></div>${body}</section>`;",
            "boxes.indexOf(box)", // a list's ticks: grid.js tick and ticks
        ];
        string[] owners = ["parts.js", "console.js", "grid.js"];
        foreach (var file in Directory.GetFiles(Path.Combine(Web, "Assets"), "*.js").Where(f => !owners.Contains(Path.GetFileName(f))))
        {
            var text = File.ReadAllText(file);
            foreach (var part in once)
            {
                Assert.False(text.Contains(part, StringComparison.Ordinal), $"{Path.GetFileName(file)} has its own copy of: {part}");
            }
        }
    }

    [Fact]
    public void The_page_loads_the_shared_parts_right_after_the_console_script()
    {
        var page = File.ReadAllText(Path.Combine(Web, "index.html")).ReplaceLineEndings("\n");
        Assert.Contains("<script src=\"Assets/console.js\" defer></script>\n<script src=\"Assets/parts.js\" defer></script>\n", page);
        // Each name is on RabbidLab once: parts.js adds to what console.js made, and never replaces one of its own.
        var parts = File.ReadAllText(Path.Combine(Web, "Assets", "parts.js"));
        var added = System.Text.RegularExpressions.Regex.Match(parts, @"Object\.assign\(R, \{([^}]*)\}\)").Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var console = File.ReadAllText(Path.Combine(Web, "Assets", "console.js"));
        var given = System.Text.RegularExpressions.Regex.Match(console, @"window\.RabbidLab = \{([^\n]*)").Groups[1].Value;
        Assert.NotEmpty(added);
        Assert.All(added, name => Assert.DoesNotMatch($@"(?<![\w$]){System.Text.RegularExpressions.Regex.Escape(name)}(?![\w$:(])\s*,", given));
    }
}
