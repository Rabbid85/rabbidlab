using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RabbidLab.Tests;

/// <summary>CHANGELOG.md and What's new (RELEASE_NOTES in console.js) say the same, newest first, starting with this version.</summary>
public sealed partial class ChangelogTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RabbidLab.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Can't find the repository.");
    }

    private static List<(string Version, List<string> Notes)> Changelog()
    {
        var list = new List<(string Version, List<string> Notes)>();
        foreach (var line in File.ReadAllLines(Path.Combine(Root(), "CHANGELOG.md")))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal)) list.Add((line[3..].Split(' ')[0], new List<string>()));
            else if (line.StartsWith("- ", StringComparison.Ordinal) && list.Count > 0) list[^1].Notes.Add(line[2..]);
        }
        return list;
    }

    private static List<(string Version, List<string> Notes)> WhatsNew()
    {
        var script = File.ReadAllText(Path.Combine(Root(), "Src", "RabbidLab.Server", "wwwroot", "Assets", "console.js"));
        return Entry().Matches(script)
            .Select(m => (m.Groups[1].Value, Note().Matches(m.Groups[2].Value).Select(n => n.Groups[1].Value).ToList()))
            .ToList();
    }

    [GeneratedRegex(@"\['(\d+\.\d+\.\d+)', \[(.*?)\]\]", RegexOptions.Singleline)]
    private static partial Regex Entry();

    [GeneratedRegex("'([^']*)'")]
    private static partial Regex Note();

    [Fact]
    public void The_changelog_says_what_whats_new_says()
    {
        var log = Changelog();
        var news = WhatsNew();
        Assert.NotEmpty(log);
        Assert.Equal(news.Select(x => x.Version), log.Select(x => x.Version));
        for (var i = 0; i < log.Count; i++) Assert.Equal(news[i].Notes, log[i].Notes);
    }

    [GeneratedRegex(@"^## (\d+\.\d+\.\d+) \((\d{4}-\d{2}-\d{2})\)\s*$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*'(\d+\.\d+\.\d+)': \['(\d{4}-\d{2}-\d{2})'(?:, '(feature|fix|update)', '([^']+)'(?:, '([\w.-]+\.(?:jpg|png|webp))')?)?\],\s*$", RegexOptions.Multiline)]
    private static partial Regex Info();

    [Fact]
    public void Every_version_has_its_date_and_the_newest_a_kind_and_title()
    {
        var dates = Heading().Matches(File.ReadAllText(Path.Combine(Root(), "CHANGELOG.md"))).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var script = File.ReadAllText(Path.Combine(Root(), "Src", "RabbidLab.Server", "wwwroot", "Assets", "console.js"));
        var info = Info().Matches(script).ToDictionary(m => m.Groups[1].Value, m => m);
        Assert.Equal(Changelog().Count, dates.Count);
        foreach (var (version, date) in dates)
        {
            Assert.True(info.ContainsKey(version), $"{version} has no line in RELEASE_INFO");
            Assert.Equal(date, info[version].Groups[2].Value);
        }
        var newest = info[Changelog()[0].Version];
        Assert.True(newest.Groups[3].Success && newest.Groups[4].Success, "The newest version needs a kind and a title in RELEASE_INFO");
    }

    [Fact]
    public void The_newest_notes_are_for_this_version()
    {
        var project = XDocument.Load(Path.Combine(Root(), "Src", "RabbidLab.Server", "RabbidLab.Server.csproj"));
        Assert.Equal(project.Descendants("Version").First().Value, Changelog()[0].Version);
    }
}
