using System.Text;
using System.Text.Json;
using RabbidLab.Server;
using RabbidLab.Setup;

namespace RabbidLab.Tests;

/// <summary>What Setup does about computers with the client when the server is removed.</summary>
public class ClientPlanTests
{
    private static readonly int[] Dev = [8421];
    private static bool Here(string host) => host is "localhost" or "127.0.0.1" or "::1" or "LAB01" or "lab.home.lan";

    [Theory]
    [InlineData("https://localhost:8421", true)]
    [InlineData("https://LOCALHOST:8421/", true)]
    [InlineData("https://lab.home.lan:8421", true)]
    [InlineData("https://127.0.0.1:8421", true)]
    [InlineData("https://[::1]:8421", true)]
    [InlineData("https://localhost:8420", false)] // the other edition on the same computer
    [InlineData("https://other.home.lan:8421", false)] // a server somewhere else
    [InlineData("https://localhost", false)] // port 443, which this server doesn't answer on
    [InlineData("localhost:8421", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_client_here_is_this_servers_when_its_address_is_this_computer_on_the_servers_port(string? server, bool ours) =>
        Assert.Equal(ours, ClientPlan.Ours(server, Dev, Here));

    [Fact]
    public void Port_443_counts_when_the_console_answers_there_too() =>
        Assert.True(ClientPlan.Ours("https://lab.home.lan", [8420, 443], Here));

    [Fact]
    public void The_other_computers_are_counted_from_the_file_the_server_writes()
    {
        // Written the way Clients.cs writes it.
        var file = new ClientsFile { UnlockAfterDays = 14 };
        foreach (var name in new[] { "LAB01", "GARAGE-PC", "desk-02" })
        {
            file.Clients.Add(new RabbidLab.Server.Client { Id = name, Computer = name, Joined = DateTime.UtcNow, LastSeen = DateTime.UtcNow });
        }
        var json = JsonSerializer.SerializeToUtf8Bytes(file, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

        Assert.Equal((2, 14), ClientPlan.Others(json, "lab01"));
        Assert.Equal((3, 14), ClientPlan.Others(json, "SOMEWHERE-ELSE"));
        // With a byte order mark in front, as some editors save it.
        Assert.Equal((2, 14), ClientPlan.Others([0xEF, 0xBB, 0xBF, .. json], "LAB01"));
    }

    [Fact]
    public void No_file_an_empty_one_or_one_that_isnt_json_is_no_computers()
    {
        Assert.Equal((0, 30), ClientPlan.Others(null, "LAB01"));
        Assert.Equal((0, 30), ClientPlan.Others([], "LAB01"));
        Assert.Equal((0, 30), ClientPlan.Others(Encoding.UTF8.GetBytes("not json {"), "LAB01"));
        Assert.Equal((0, 30), ClientPlan.Others(Encoding.UTF8.GetBytes("{}"), "LAB01"));
    }

    [Fact]
    public void The_note_says_how_many_and_how_long_they_stay_locked()
    {
        Assert.Null(ClientPlan.OthersNote(0, 30));
        Assert.Contains("1 other computer,", ClientPlan.OthersNote(1, 30));
        Assert.Contains("3 other computers,", ClientPlan.OthersNote(3, 30));
        Assert.Contains("until 30 days after", ClientPlan.OthersNote(3, 30));
        Assert.Contains("never release", ClientPlan.OthersNote(3, 0));
    }
}
