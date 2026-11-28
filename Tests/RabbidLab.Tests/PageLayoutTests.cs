namespace RabbidLab.Tests;

/// <summary>What every page with a banner keeps to, read from the stylesheets and the page.</summary>
public sealed class PageLayoutTests
{
    private static string Web(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { ReleaseKeyTests.Root(), "Src", "RabbidLab.Server", "wwwroot" }.Concat(parts).ToArray()));

    [Fact]
    public void The_lists_on_Links_and_Sensors_start_and_end_where_their_banner_does()
    {
        // 1.1.433 took Overview's News block out with its styles, which these lists had borrowed their side margins from:
        // they ran to the very edge of the sidebar until 1.1.441. Their own rule now, by their own name.
        Assert.Contains(".sn-body { padding: 26px var(--side) 26px; }", Web("Assets", "sensors.css"));
        var page = Web("index.html");
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(page, "<section class=\"sn-body\">").Count);
        Assert.DoesNotContain("class=\"news ", page);
    }
}
