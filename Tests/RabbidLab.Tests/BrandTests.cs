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
    public void An_icon_over_256_KB_is_refused()
    {
        var big = "data:image/png;base64," + Convert.ToBase64String(new byte[Brand.IconBytes + 1]);

        Assert.NotNull(Brand.Problem(new BrandForm(null, big, null)));
    }
}
