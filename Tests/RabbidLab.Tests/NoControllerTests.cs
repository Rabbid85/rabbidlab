using RabbidLab.Server;
using Xunit;

namespace RabbidLab.Tests;

// The sign-in page's warning when no domain controller answers: the reason the server found, said plainly.
public class NoControllerTests
{
    [Fact]
    public void A_vpn_is_named_first_even_when_names_dont_resolve()
    {
        var w = NoController.Say("amw.com", "DESKTOP-1", "SPG-VPN", 2, "dc01.amw.com");
        Assert.Equal("VPN detected.", w.Title);
        Assert.Contains("(DESKTOP-1) is connected to a VPN (SPG-VPN) and can't reach a domain controller for amw.com.", w.Text);
        Assert.Contains("Disconnect from the VPN, or use a local account.", w.Text);
    }

    [Fact]
    public void A_vpn_with_names_that_resolve_still_says_vpn()
    {
        var w = NoController.Say("amw.com", "DESKTOP-1", "SPG-VPN", 2, null);
        Assert.Equal("VPN detected.", w.Title);
        Assert.DoesNotContain("DNS", w.Text);
    }

    [Fact]
    public void Without_a_vpn_it_tells_dns_from_no_route()
    {
        Assert.Contains("can't look up dc01.amw.com in DNS", NoController.Say("amw.com", "S", null, 2, "dc01.amw.com").Text);
        Assert.Contains("finds no domain controllers for amw.com in DNS", NoController.Say("amw.com", "S", null, 0, null).Text);
        var route = NoController.Say("amw.com", "S", null, 2, null);
        Assert.Equal("Domain controllers not reachable.", route.Title);
        Assert.Contains("(S)", route.Text);
    }
}
