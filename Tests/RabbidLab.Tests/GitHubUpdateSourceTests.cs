using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RabbidLab.Server;
using RabbidLab.Updater;

namespace RabbidLab.Tests;

/// <summary>Where updates come from (Updates.cs): the newest release's manifest on GitHub, against a stand-in for GitHub.</summary>
public sealed class GitHubUpdateSourceTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rabbidlab-github-" + Guid.NewGuid().ToString("N"));
    private readonly Host host;

    public GitHubUpdateSourceTests()
    {
        Directory.CreateDirectory(folder);
        host = new Host(folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private const string Repository = "https://github.com/Rabbid85/rabbidlab";

    private GitHubUpdateSource Source(GitHub github, GitHubOptions? options = null) =>
        new(new Clients(github), options ?? new GitHubOptions { Repository = Repository }, host, NullLogger<GitHubUpdateSource>.Instance);

    [Fact]
    public async Task The_newest_release_s_manifest_is_read_from_GitHub_s_latest_link_and_only_its_own_downloads_are_fetched()
    {
        var github = new GitHub("""
            {"product":"Rabbid Lab","version":"9.1.0",
             "windows":{"version":"9.1.0","file":"RabbidLab-Setup-9.1.0.exe","size":42,"sha256":"ab12","url":"https://github.com/Rabbid85/rabbidlab/releases/download/v9.1.0/RabbidLab-Setup-9.1.0.exe","signature":"c2ln"},
             "docker":{"version":"9.0.5","file":"RabbidLab-Docker-9.0.5.zip","url":"https://github.com/Rabbid85/rabbidlab/releases/download/v9.0.5/RabbidLab-Docker-9.0.5.zip"}}
            """);
        var source = Source(github);

        await source.Check(CancellationToken.None);

        Assert.Equal(Product.Dev ? Repository + "/releases/download/dev-latest/release.json" : Repository + "/releases/latest/download/release.json", github.Asked.Single());
        Assert.Null(source.Problem);
        Assert.NotNull(source.CheckedAt);
        // Where it installs itself (a Windows service): the installer's offer.
        Assert.Equal(new UpdateOffer("9.1.0", 42, "ab12", "https://github.com/Rabbid85/rabbidlab/releases/download/v9.1.0/RabbidLab-Setup-9.1.0.exe", "c2ln"), source.Offer);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            source.Fetch(source.Offer! with { Url = "https://elsewhere.test/RabbidLab-Setup-9.1.0.exe" }, (_, _) => Task.CompletedTask, CancellationToken.None));
        Assert.Single(github.Asked); // never asked for

        // Where it doesn't (Docker): the Docker download's version, and nothing to fetch.
        host.CanInstall = false;
        await source.Check(CancellationToken.None);
        Assert.Equal(new UpdateOffer("9.0.5", 0, "", "", null), source.Offer);
    }

    [Fact]
    public async Task A_manifest_without_the_windows_part_offers_nothing_where_it_installs_itself()
    {
        var source = Source(new GitHub("""{"product":"Rabbid Lab","version":"9.1.0"}"""));

        await source.Check(CancellationToken.None);

        Assert.Null(source.Offer);
        Assert.Null(source.Problem);
    }

    [Fact]
    public async Task A_repository_with_no_release_yet_says_so_and_offers_nothing()
    {
        var source = Source(new GitHub(null, HttpStatusCode.NotFound));

        await source.Check(CancellationToken.None);

        Assert.Null(source.Offer);
        Assert.Equal("No release has been published on GitHub yet.", source.Problem);
        Assert.NotNull(source.CheckedAt);
    }

    [Fact]
    public async Task GitHub_that_can_t_be_reached_is_a_problem_to_show_not_an_offer()
    {
        var source = Source(new GitHub(null));

        await source.Check(CancellationToken.None);

        Assert.Null(source.Offer);
        Assert.StartsWith("GitHub couldn't be reached", source.Problem);
        Assert.NotNull(source.CheckedAt);
    }

    [Fact]
    public async Task GitHub_is_asked_again_after_12_hours_or_an_hour_after_it_got_no_answer()
    {
        var options = new GitHubOptions { Repository = Repository };
        var source = Source(new GitHub("""{"version":"1.0.0"}"""), options);
        Assert.Equal(DateTime.MinValue, GitHubChecks.Due(source, options)); // never asked: now

        await source.Check(CancellationToken.None);
        Assert.Equal(source.CheckedAt!.Value.AddHours(12), GitHubChecks.Due(source, options));

        var unreachable = Source(new GitHub(null), options);
        await unreachable.Check(CancellationToken.None);
        Assert.Equal(unreachable.CheckedAt!.Value.AddHours(1), GitHubChecks.Due(unreachable, options));
    }

    [Fact]
    public void The_manifest_links_are_the_repository_s_own()
    {
        var options = new GitHubOptions { Repository = Repository + "/" };
        Assert.Equal(Repository + "/releases/download/", options.Downloads);
        Assert.Equal(Product.Dev ? Repository + "/releases/download/dev-latest/release.json" : Repository + "/releases/latest/download/release.json", options.Manifest);
    }

    /// <summary>GitHub: answers every request with the JSON given (null: can't be reached, or the status given), and notes what was asked.</summary>
    private sealed class GitHub(string? json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Asked.Add(request.RequestUri!.ToString());
            if (json is null && status == HttpStatusCode.OK)
            {
                throw new HttpRequestException("No such host is known.");
            }
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json ?? "Not Found", Encoding.UTF8, json is null ? "text/html" : "application/json") });
        }
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Host(string folder) : IUpdateHost
    {
        public string Version => "0.5.0";

        public string Folder => folder;

        public bool CanInstall { get; set; } = true;

        public bool Automatic { get; set; } = true;

        public string ProductName { get; set; } = "Rabbid Lab";

        public void Record(UpdateAttempt attempt)
        {
        }
    }
}
