using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace RabbidLab.Tests;

/// <summary>
/// Every server address the Windows client calls is open to clients rather than behind sign-in: 1.1.405 shipped Ask IT for
/// help and the lab's icon behind it, so neither ever reached the server.
/// </summary>
public sealed partial class ClientPathsTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RabbidLab.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Can't find the repository.");
    }

    [Fact]
    public void EveryPathTheClientCallsIsOpenToClients()
    {
        var client = Path.Combine(Root(), "Src", "Client", "RabbidLab.Client");
        var called = Directory.GetFiles(client, "*.cs")
            .SelectMany(f => Called().Matches(File.ReadAllText(f)).Select(m => "/" + m.Groups[1].Value))
            .Distinct()
            .ToList();
        Assert.Contains("/api/clients/checkin", called); // the search found what the client calls
        Assert.All(called, p => Assert.True(RabbidLab.Server.SignIn.ForClients(new PathString(p)), $"{p} is behind sign-in, so the client can't reach it."));
    }

    [GeneratedRegex(@"""(api/clients/[a-z/\-]+)")]
    private static partial Regex Called();
}
