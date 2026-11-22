using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>The lab's own look: what a name, icon and accent may be, and how an empty one puts Rabbid Lab's own back.</summary>
public sealed class BrandTests
{
    private static readonly string Png = "data:image/png;base64," + Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

    [Fact]
    public void A_name_icon_and_accent_that_fit_are_saved_and_empty_ones_clear()
    {
        var s = new ConsoleSettings();

        Assert.Null(Brand.Problem(new BrandForm("  Wiles Lab  ", Png, "#3d7fe6")));
        Brand.Apply(s, new BrandForm("  Wiles Lab  ", Png, "#3d7fe6"));
        var set = (s.LabName, s.LabIcon, s.Accent);
        Brand.Apply(s, new BrandForm(null, "", null));
        var iconGone = (s.LabName, s.LabIcon, s.Accent);
        Brand.Apply(s, new BrandForm("", null, ""));

        Assert.Equal(("Wiles Lab", Png, "#3D7FE6"), set);
        Assert.Equal(("Wiles Lab", (string?) null, "#3D7FE6"), iconGone);
        Assert.Null(s.LabName);
        Assert.Null(s.Accent);
    }

    [Theory]
    [InlineData("A name far too long for the top bar of anyone's lab", null, null)]
    [InlineData("Two\nlines", null, null)]
    [InlineData(null, "data:image/gif;base64,R0lGOD", null)]
    [InlineData(null, "data:image/png;base64,not base64!", null)]
    [InlineData(null, null, "orange")]
    [InlineData(null, null, "#12345")]
    public void Anything_else_is_refused(string? name, string? icon, string? accent) =>
        Assert.NotNull(Brand.Problem(new BrandForm(name, icon, accent)));

    [Fact]
    public void A_tagline_keeps_its_tags_and_an_empty_one_puts_Lab_console_back()
    {
        var s = new ConsoleSettings();

        Assert.Null(Brand.Problem(new BrandForm(null, null, null, "  {name} on {hostname} · {role}  ")));
        Brand.Apply(s, new BrandForm(null, null, null, "  {name} on {hostname} · {role}  "));
        var set = s.LabTagline;
        Brand.Apply(s, new BrandForm(null, null, null, ""));

        Assert.Equal("{name} on {hostname} · {role}", set);
        Assert.Null(s.LabTagline);
    }

    [Theory]
    [InlineData("{password} is shown here")]
    [InlineData("Hi {UserName}")]
    [InlineData("A tagline that goes on and on far past what fits under the name")]
    [InlineData("Two\nlines")]
    public void A_tagline_with_an_unknown_tag_or_too_long_is_refused(string tagline) =>
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, tagline)));

    [Fact]
    public void The_sign_in_background_is_saved_and_an_empty_one_puts_squares_back()
    {
        var s = new ConsoleSettings();
        string Look() => JsonSerializer.SerializeToElement(Brand.Read(s)).GetProperty("background").GetString()!;

        var fresh = Look();
        Assert.Null(Brand.Problem(new BrandForm(null, null, null, Background: "bubbles")));
        Brand.Apply(s, new BrandForm(null, null, null, Background: "bubbles"));
        var set = Look();
        Brand.Apply(s, new BrandForm("Wiles Lab", null, null)); // another change leaves it be
        var kept = Look();
        Brand.Apply(s, new BrandForm(null, null, null, Background: ""));

        Assert.Equal("squares", fresh);
        Assert.Equal("bubbles", set);
        Assert.Equal("bubbles", kept);
        Assert.Equal("squares", Look());
    }

    [Fact]
    public void A_background_picked_before_it_left_the_list_shows_as_squares()
    {
        var s = new ConsoleSettings { SignInBackground = "rain" }; // Data rain, gone in 1.1.302

        Assert.Equal("squares", JsonSerializer.SerializeToElement(Brand.Read(s)).GetProperty("background").GetString());
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, Background: "rain")));
    }

    [Theory]
    [InlineData("matrix")]
    [InlineData("Squares")]
    [InlineData("../tiles")]
    public void A_sign_in_background_not_on_the_list_is_refused(string background) =>
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, Background: background)));

    [Fact]
    public async Task The_look_is_still_there_after_a_restart_such_as_an_update_and_the_sign_in_page_is_given_it()
    {
        var data = Path.Combine(Path.GetTempPath(), "rabbidlab-brand-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        try
        {
            using (var first = new TestApp(data))
            {
                var client = await first.SignedIn();
                var saved = await client.PostAsJsonAsync("/api/brand", new { accent = "#1FA3A8" });
                await client.PostAsJsonAsync("/api/brand", new { name = "Wiles Lab" });
                await client.PostAsJsonAsync("/api/update/settings", new { automatic = false }); // another setting saved after it
                Assert.True(saved.IsSuccessStatusCode);
            }
            using var second = new TestApp(data); // the same data folder, as after an update
            var signedIn = await (await second.SignedIn()).GetFromJsonAsync<JsonElement>("/api/brand");
            var signInPage = await second.Page().GetFromJsonAsync<JsonElement>("/api/signin");

            Assert.Equal("#1FA3A8", signedIn.GetProperty("accent").GetString());
            Assert.Equal("Wiles Lab", signedIn.GetProperty("name").GetString());
            Assert.Equal("#1FA3A8", signInPage.GetProperty("brand").GetProperty("accent").GetString());
            Assert.False(second.Services.GetRequiredService<SettingsStore>().Current.AutomaticUpdates);
        }
        finally
        {
            try { Directory.Delete(data, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void An_icon_over_256_KB_is_refused()
    {
        var big = "data:image/png;base64," + Convert.ToBase64String(new byte[Brand.IconBytes + 1]);

        Assert.NotNull(Brand.Problem(new BrandForm(null, big, null)));
    }

    [Fact]
    public void The_sign_in_card_starts_as_the_owner_picked_it_and_its_settings_are_saved()
    {
        var s = new ConsoleSettings();
        var start = JsonSerializer.SerializeToElement(Brand.Read(s));
        var quotes = new[] { new SignInQuote("  Ship it.  ", "  Anthony  "), new SignInQuote("Change one thing at a time.", " ") };
        var form = new BrandForm(null, null, null, Card: "split", Style: "frost", Glass: 40, Size: "largest", Quote: "day", QuoteKind: "own", Quotes: quotes);

        Assert.Null(Brand.Problem(form));
        Brand.Apply(s, form);
        var after = JsonSerializer.SerializeToElement(Brand.Read(s));

        Assert.Equal(("whole", "glass", 65, "larger", "visit", "motivational"), (start.GetProperty("card").GetString(), start.GetProperty("style").GetString(),
            start.GetProperty("glass").GetInt32(), start.GetProperty("size").GetString(), start.GetProperty("quote").GetString(), start.GetProperty("quoteKind").GetString()));
        Assert.Equal(("split", "frost", 40, "largest", "day", "own"), (after.GetProperty("card").GetString(), after.GetProperty("style").GetString(),
            after.GetProperty("glass").GetInt32(), after.GetProperty("size").GetString(), after.GetProperty("quote").GetString(), after.GetProperty("quoteKind").GetString()));
        Assert.Equal([new SignInQuote("Ship it.", "Anthony"), new SignInQuote("Change one thing at a time.", null)], s.SignInQuotes);
    }

    [Fact]
    public void Sign_in_settings_outside_the_lists_are_refused()
    {
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, Card: "round")));
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, Style: "neon")));
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, Glass: 10)));
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, Glass: 100)));
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, Size: "huge")));
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, Quote: "hourly")));
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, QuoteKind: "sad")));
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, Quotes: [new SignInQuote("Two\nlines", null)])));
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, Quotes: [new SignInQuote(new string('a', Brand.QuoteLength + 1), null)])));
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, Quotes: Enumerable.Repeat(new SignInQuote("Hi.", null), Brand.QuoteCount + 1).ToList())));
    }

    [Fact]
    public void The_labs_look_is_kept_as_sent_and_an_empty_one_clears_it()
    {
        var s = new ConsoleSettings();
        var look = JsonSerializer.SerializeToElement(new { theme = "slate", toast = new { type = "deck" } });

        Assert.Null(Brand.Problem(new BrandForm(null, null, null, Look: look)));
        Brand.Apply(s, new BrandForm(null, null, null, Look: look));
        var kept = JsonSerializer.SerializeToElement(Brand.Read(s)).GetProperty("look");
        Brand.Apply(s, new BrandForm(null, null, null, Look: JsonSerializer.SerializeToElement(new { })));

        Assert.Equal("slate", kept.GetProperty("theme").GetString());
        Assert.Equal("deck", kept.GetProperty("toast").GetProperty("type").GetString());
        Assert.Null(s.LabLook);
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, Look: JsonSerializer.SerializeToElement("not an object"))));
    }
}
