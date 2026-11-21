using System.Globalization;
using System.Net;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>The themes made from the style sheets (Themes.cs): what each does to a colour, and themes.css as the pages get it.</summary>
public sealed class ThemesTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-themes-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public ThemesTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
    }

    public void Dispose()
    {
        app.Dispose();
        try
        {
            Directory.Delete(data, recursive: true);
        }
        catch (IOException)
        {
            // Still in use; the temp folder is cleaned up later.
        }
    }

    private static (int R, int G, int B) Rgb(string hex) =>
        (int.Parse(hex[1..3], NumberStyles.HexNumber), int.Parse(hex[3..5], NumberStyles.HexNumber), int.Parse(hex[5..7], NumberStyles.HexNumber));

    [Fact]
    public void Light_turns_the_dark_page_light_and_the_light_text_dark()
    {
        var page = Rgb(Themes.Turn("#0B0C14", "light"));
        var text = Rgb(Themes.Turn("#F2F3FA", "light"));

        Assert.True(page.R > 225 && page.G > 225 && page.B > 225, $"page {page}");
        Assert.True(text.R < 60 && text.G < 60 && text.B < 60, $"text {text}");
    }

    [Theory]
    [InlineData("carbon")]
    [InlineData("mocha")]
    [InlineData("slate")]
    [InlineData("light")]
    public void A_colour_like_the_accent_keeps_its_hue(string theme) =>
        Assert.Equal("#4C00FF", Themes.Turn("#4C00FF", theme));

    [Fact]
    public void Carbon_has_no_tint_and_mocha_is_warmer_than_slate()
    {
        var carbon = Rgb(Themes.Turn("#11131D", "carbon"));
        var mocha = Rgb(Themes.Turn("#11131D", "mocha"));
        var slate = Rgb(Themes.Turn("#11131D", "slate"));

        Assert.True(carbon.R == carbon.G && carbon.G == carbon.B, $"carbon {carbon}");
        Assert.True(mocha.R > mocha.B, $"mocha {mocha}");
        Assert.True(slate.B > slate.R, $"slate {slate}");
    }

    [Fact]
    public void A_theme_rule_sits_under_its_name_and_outranks_the_original_by_one_step()
    {
        Assert.Equal(":root[data-theme=\"light\"]", Themes.Scope(":root", "light"));
        Assert.Equal("html[data-theme=\"slate\"].auth", Themes.Scope("html.auth", "slate"));
        Assert.Equal("[data-theme=\"mocha\"] .rail a:hover", Themes.Scope(".rail a:hover", "mocha"));
    }

    [Fact]
    public void Built_rules_keep_variables_media_and_white_on_the_accent_and_leave_keyframes_and_pictures_alone()
    {
        var css = Themes.Build([
            ":root { --night: #0B0C14; --gap: 12px; }\n"
            + ".a:not(.b, .c) { color: #F2F3FA; padding: 4px; }\n"
            + ".on { color: var(--accent); }\n"
            + "button.go { background: var(--ember); color: #fff; }\n"
            + ".icon { background-image: url(\"data:image/svg+xml,%3Csvg fill='%23000'%3E\"); }\n"
            + "@media (max-width: 600px) { .a { border-color: #2A2D3E; } }\n"
            + "@keyframes spin { to { color: #fff; } }\n",
        ]);
        var light = string.Join('\n', css.Split('\n').Where(l => l.Contains("\"light\"") || l.StartsWith("@media", StringComparison.Ordinal) || l == "}"));

        Assert.Contains(":root[data-theme=\"light\"] { --night: #", light);
        Assert.Contains("--gap: 12px", light); // a variable is written again whatever it holds
        Assert.Contains("[data-theme=\"light\"] .a:not(.b, .c) { color: #", light);
        Assert.DoesNotContain("padding", light);
        Assert.Contains("[data-theme=\"light\"] .on { color: var(--accent); }", light);
        Assert.Contains("[data-theme=\"light\"] button.go { background: var(--ember); color: #fff; }", light);
        Assert.Contains("%23000", light);
        Assert.Contains("@media (max-width: 600px) {", light);
        Assert.DoesNotContain("spin", css);
        Assert.Equal(Themes.Names.Length, css.Split(":root[data-theme=").Length - 1);
    }

    [Fact]
    public async Task The_pages_get_themes_css_signed_in_or_not_and_a_304_while_it_hasnt_changed()
    {
        var page = app.RawPage();

        var first = await page.GetAsync("/Assets/themes.css");
        var css = await first.Content.ReadAsStringAsync();
        var again = new HttpRequestMessage(HttpMethod.Get, "/Assets/themes.css");
        again.Headers.TryAddWithoutValidation("If-None-Match", first.Headers.ETag?.Tag);
        var second = await page.SendAsync(again);
        var squeezed = new HttpRequestMessage(HttpMethod.Get, "/Assets/themes.css");
        squeezed.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
        var gz = await page.SendAsync(squeezed);
        var gzBytes = await gz.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("gzip", gz.Content.Headers.ContentEncoding);
        Assert.True(gzBytes.Length < css.Length / 4, $"{gzBytes.Length} of {css.Length}");
        Assert.Equal("text/css", first.Content.Headers.ContentType?.MediaType);
        foreach (var theme in Themes.Names)
        {
            Assert.Contains($":root[data-theme=\"{theme}\"]", css);
            Assert.Contains($"[data-theme=\"{theme}\"] .signin-card", css); // the sign-in page's style sheet is in it too
        }
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }
}
