using RabbidLab.Setup;

namespace RabbidLab.Tests;

/// <summary>
/// Setup's decision about adding a name of their own to DNS (DnsPlan), from what it read first. It may only ever add
/// a new alias of this server; anything already at the name, or a computer's name, is left alone.
/// </summary>
public sealed class DnsPlanTests
{
    private const string Server = "srv01.contoso.local";

    private static DnsFacts Facts(string name = "rabbidlab.contoso.local", params DnsRecordInfo[] records) => new()
    {
        Name = name,
        Server = "dc01.contoso.local",
        Target = Server,
        Zones =
        [
            new() { Name = "contoso.local", Primary = true },
            new() { Name = "_msdcs.contoso.local", Primary = true },
            new() { Name = "2.168.192.in-addr.arpa", Primary = true, Reverse = true },
        ],
        Records = [.. records],
        TargetAddresses = ["192.168.2.28"],
        MyAddresses = ["192.168.2.28"],
    };

    private static DnsRecordInfo Record(string type, string data) => new() { Type = type, Data = data };

    private static (DnsVerdict Verdict, string? Zone, string Message) Decide(DnsFacts facts)
    {
        var verdict = DnsPlan.Decide(facts, out var zone, out var message);
        return (verdict, zone, message);
    }

    [Fact]
    public void A_new_name_in_the_domain_can_be_added_in_its_zone()
    {
        var (verdict, zone, message) = Decide(Facts());
        Assert.Equal(DnsVerdict.CanAdd, verdict);
        Assert.Equal("contoso.local", zone);
        Assert.Contains("dc01.contoso.local", message);
    }

    [Fact]
    public void The_zone_is_the_closest_one_that_holds_the_name()
    {
        var facts = Facts("ad.it.contoso.local");
        facts.Zones.Add(new() { Name = "it.contoso.local", Primary = true });
        Assert.Equal("it.contoso.local", Decide(facts).Zone);
        Assert.Null(DnsPlan.ZoneFor("rabbidlab.fabrikam.com", facts.Zones));
        Assert.Null(DnsPlan.ZoneFor("notcontoso.local", facts.Zones)); // a zone's name only counts after a dot
    }

    [Fact]
    public void An_alias_of_this_server_is_already_there_and_is_not_added_again()
    {
        var (verdict, _, _) = Decide(Facts(records: Record("CNAME", "SRV01.contoso.local.")));
        Assert.Equal(DnsVerdict.AlreadyHere, verdict);
    }

    [Fact]
    public void An_address_record_for_this_server_counts_as_already_there()
    {
        Assert.Equal(DnsVerdict.AlreadyHere, Decide(Facts(records: Record("A", "192.168.2.28"))).Verdict);
    }

    [Theory]
    [InlineData("CNAME", "web01.contoso.local.", "web01.contoso.local")]
    [InlineData("A", "192.168.2.50", "192.168.2.50")]
    [InlineData("TXT", "\"v=spf1 -all\"", "TXT")]
    [InlineData("MX", "[10] mail.contoso.local.", "MX")]
    public void A_name_already_used_for_anything_else_is_left_alone(string type, string data, string said)
    {
        var (verdict, _, message) = Decide(Facts(records: Record(type, data)));
        Assert.Equal(DnsVerdict.Taken, verdict);
        Assert.Contains(said, message);
    }

    [Fact]
    public void A_name_pointing_here_and_somewhere_else_too_is_left_alone()
    {
        Assert.Equal(DnsVerdict.Taken, Decide(Facts(records: [Record("A", "192.168.2.28"), Record("A", "192.168.2.50")])).Verdict);
    }

    [Fact]
    public void A_computer_s_name_is_refused_even_when_it_has_no_record_right_now()
    {
        var facts = Facts("fs01.contoso.local");
        facts.ClashingComputer = "FS01";
        var (verdict, _, message) = Decide(facts);
        Assert.Equal(DnsVerdict.CantAdd, verdict);
        Assert.Contains("FS01", message);
    }

    [Fact]
    public void This_server_s_own_name_is_never_made_an_alias_of_itself()
    {
        Assert.Equal(DnsVerdict.AlreadyHere, Decide(Facts(Server)).Verdict);
    }

    [Fact]
    public void A_name_outside_every_zone_on_the_DNS_server_is_not_added()
    {
        var (verdict, zone, message) = Decide(Facts("rabbidlab.fabrikam.com"));
        Assert.Equal(DnsVerdict.CantAdd, verdict);
        Assert.Null(zone);
        Assert.Contains("no DNS zone", message);
    }

    [Fact]
    public void The_zone_s_own_name_is_not_made_an_alias()
    {
        Assert.Equal(DnsVerdict.CantAdd, Decide(Facts("contoso.local")).Verdict);
    }

    [Fact]
    public void A_read_only_copy_of_the_zone_is_not_written_to()
    {
        var facts = Facts();
        facts.Zones[0].Primary = false;
        var (verdict, _, message) = Decide(facts);
        Assert.Equal(DnsVerdict.CantAdd, verdict);
        Assert.Contains("read-only copy", message);
    }

    [Fact]
    public void An_alias_needs_this_server_s_own_name_in_DNS_pointing_here()
    {
        var missing = Facts();
        missing.TargetAddresses = [];
        Assert.Equal(DnsVerdict.NoTarget, Decide(missing).Verdict);

        var stale = Facts();
        stale.TargetAddresses = ["192.168.2.99"];
        var (verdict, _, message) = Decide(stale);
        Assert.Equal(DnsVerdict.NoTarget, verdict);
        Assert.Contains("192.168.2.99", message);
    }

    [Fact]
    public void Names_are_compared_without_case_or_the_final_dot()
    {
        var facts = Facts("RabbidLab.Contoso.Local.");
        facts.Target = "SRV01.CONTOSO.LOCAL";
        Assert.Equal(DnsVerdict.CanAdd, Decide(facts).Verdict);
    }
}
