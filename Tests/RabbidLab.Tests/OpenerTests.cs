using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>--open, what the shortcut runs: it only opens web addresses, and it knows when nothing is answering.</summary>
public sealed class OpenerTests
{
    [Theory]
    [InlineData("not an address")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("ftp://localhost/")]
    public async Task Anything_but_a_web_address_is_refused_and_nothing_is_opened(string address) =>
        Assert.Equal(1, await Opener.Open(address));

    [Fact]
    public async Task Nothing_listening_is_not_answering() =>
        Assert.False(await Opener.Answers(new Uri("http://127.0.0.1:9/"))); // the discard port: nothing serves it
}
