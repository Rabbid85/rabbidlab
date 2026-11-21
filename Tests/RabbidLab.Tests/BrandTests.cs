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
    public void The_sign_in_left_side_and_background_are_saved_and_empty_ones_put_day_and_squares_back()
    {
        var s = new ConsoleSettings();
        string?[] Look() { var b = JsonSerializer.SerializeToElement(Brand.Read(s)); return [b.GetProperty("side").GetString(), b.GetProperty("background").GetString()]; }

        var fresh = Look();
        Assert.Null(Brand.Problem(new BrandForm(null, null, null, Side: "clock", Background: "bubbles")));
        Brand.Apply(s, new BrandForm(null, null, null, Side: "clock", Background: "bubbles"));
        var set = Look();
        Brand.Apply(s, new BrandForm("Wiles Lab", null, null)); // another change leaves them be
        var kept = Look();
        Brand.Apply(s, new BrandForm(null, null, null, Side: "", Background: ""));

        Assert.Equal(["day", "squares"], fresh);
        Assert.Equal(["clock", "bubbles"], set);
        Assert.Equal(["clock", "bubbles"], kept);
        Assert.Equal(["day", "squares"], Look());
    }

    [Theory]
    [InlineData("flask", null)]
    [InlineData("Day", null)]
    [InlineData(null, "matrix")]
    [InlineData(null, "Squares")]
    [InlineData(null, "../tiles")]
    public void A_sign_in_side_or_background_not_on_the_list_is_refused(string? side, string? background) =>
        Assert.NotNull(Brand.Problem(new BrandForm(null, null, null, Side: side, Background: background)));

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
}
