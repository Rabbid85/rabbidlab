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

    [Fact]
    public void The_newest_notes_are_for_this_version()
    {
        var project = XDocument.Load(Path.Combine(Root(), "Src", "RabbidLab.Server", "RabbidLab.Server.csproj"));
        Assert.Equal(project.Descendants("Version").First().Value, Changelog()[0].Version);
    }
}
