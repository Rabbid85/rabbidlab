using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RabbidLab.Updater;

namespace RabbidLab.Server;

/// <summary>Where Rabbid Lab's releases are, and how often it asks which is newest.</summary>
public sealed class GitHubOptions
{
    /// <summary>The repository on GitHub whose releases are Rabbid Lab's. A download it offers is under this address, or it isn't fetched.</summary>
    public string Repository { get; set; } = Product.Repository;

    /// <summary>How often it asks GitHub which version is newest, after asking when it starts.</summary>
    public TimeSpan CheckEvery { get; set; } = TimeSpan.FromHours(12);

    /// <summary>How soon it asks again after a check that got no answer (GitHub unreachable, say).</summary>
    public TimeSpan RetryEvery { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The newest release's manifest, release.json (publish.sh puts one in every release): GitHub's own link to the newest
    /// release's files, so no API, no token and no rate limit. Rabbid Lab Dev reads the dev-latest pre-release's instead.
    /// </summary>
    public string Manifest => Product.Dev ? Downloads + "dev-latest/release.json" : Repository.TrimEnd('/') + "/releases/latest/download/release.json";

    /// <summary>Where every release's files are: only a download under here is ever fetched.</summary>
    public string Downloads => Repository.TrimEnd('/') + "/releases/download/";
}

/// <summary>
/// release.json, as publish.sh and dev.sh write it: the version, the Windows installer (its file name, size, SHA-256,
/// download address and release signature) and the Docker download. Each part may be missing (a release without one).
/// </summary>
public sealed record ReleaseManifest(string? Product, string? Version, ReleaseFile? Windows, ReleaseFile? Docker);

public sealed record ReleaseFile(string? Version, string? File, long Size, string? Sha256, string? Url, string? Signature);

/// <summary>
/// Where Rabbid Lab's updates come from: the releases of its GitHub repository. It reads the newest release's manifest and
/// nothing else: no token, no name, nothing about this machine. Where it installs itself (a Windows service) the answer is
/// the newest installer, which GitHub then hands over; elsewhere (Docker), the newest Docker download's version.
/// </summary>
public sealed class GitHubUpdateSource(IHttpClientFactory http, GitHubOptions options, IUpdateHost host, ILogger<GitHubUpdateSource> log) : IUpdateSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public UpdateOffer? Offer { get; private set; }

    public DateTime? CheckedAt { get; private set; }

    public string? Problem { get; private set; }

    public async Task Check(CancellationToken cancel)
    {
        try
        {
            using var client = http.CreateClient("updates");
            var manifest = await client.GetFromJsonAsync<ReleaseManifest>(options.Manifest, Json, cancel);
            Offer = host.CanInstall
                ? manifest?.Windows is { } w ? new(w.Version ?? manifest.Version ?? "", w.Size, w.Sha256 ?? "", w.Url ?? "", w.Signature) : null
                : manifest?.Docker is { } d ? new(d.Version ?? manifest.Version ?? "", 0, "", "", null) : null;
            Problem = null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or NotSupportedException
            || (e is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            Offer = null;
            Problem = e switch
            {
                HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "No release has been published on GitHub yet.",
                TaskCanceledException => "GitHub didn't answer in time.",
                HttpRequestException { StatusCode: { } code } => $"GitHub answered HTTP {(int)code} instead of the newest release.",
                HttpRequestException => $"GitHub couldn't be reached ({e.Message.TrimEnd('.')}).",
                _ => "The newest release's manifest couldn't be read.",
            };
            log.LogWarning("Could not ask GitHub for the newest version: {Problem}", Problem);
        }
        finally
        {
            CheckedAt = DateTime.UtcNow;
        }
    }

    public async Task Fetch(UpdateOffer offer, Func<Stream, CancellationToken, Task> read, CancellationToken cancel)
    {
        if (!offer.Url.StartsWith(options.Downloads, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("the update is not one of the repository's own releases on GitHub");
        }
        using var client = http.CreateClient("updates");
        client.Timeout = TimeSpan.FromMinutes(15); // for the answer's headers; the engine limits the download itself
        using var response = await client.GetAsync(offer.Url, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GitHub returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        await using var body = await response.Content.ReadAsStreamAsync(cancel);
        await read(body, cancel);
    }
}

/// <summary>
/// Asks GitHub which version is newest when Rabbid Lab starts, every 12 hours, and an hour after a check that got no answer.
/// Check for updates and the page ask too (UpdateEngine), and the next check here is counted from whichever asked last. The
/// update engine decides what to do with the answer.
/// </summary>
public sealed class GitHubChecks(IUpdateSource source, GitHubOptions options, ILogger<GitHubChecks> log) : BackgroundService
{
    /// <summary>When it's next time to ask: now, when it never has; otherwise CheckEvery after the last check, or RetryEvery after one that got no answer.</summary>
    public static DateTime Due(IUpdateSource source, GitHubOptions options) =>
        source.CheckedAt is { } at ? at + (source.Problem is null ? options.CheckEvery : options.RetryEvery) : DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Yield(); // GitHub takes a moment to answer; don't hold up Rabbid Lab starting
        while (!stop.IsCancellationRequested)
        {
            if (DateTime.UtcNow >= Due(source, options))
            {
                try
                {
                    await source.Check(stop);
                }
                catch (Exception e) when (!stop.IsCancellationRequested)
                {
                    log.LogError(e, "Asking GitHub for the newest version failed");
                }
            }
            // Looked at again when it's due (a minute at the least), as anything else that asked meanwhile moves it on.
            var wait = Due(source, options) - DateTime.UtcNow;
            try
            {
                await Task.Delay(wait < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : wait, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

public static class UpdatesSetup
{
    /// <summary>Where updates come from: GitHub, asked when Rabbid Lab starts, every 12 hours, and with Check for updates.</summary>
    public static void AddUpdates(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddSingleton<GitHubOptions>();
        services.AddHttpClient("updates", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"RabbidLab/{Product.Version}");
        });
        services.AddSingleton<IUpdateSource, GitHubUpdateSource>();
        services.AddHostedService<GitHubChecks>();
    }
}
