using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace RabbidLab.Updater;

/// <summary>
/// Rabbid Lab updating itself. The source (GitHub) says which version is newest, to every install:
/// A newer one installs by itself once nobody has used the console for a couple of minutes; while
/// someone is, it's offered, and installs when someone chooses Install now or once they've gone, so it never restarts
/// under anyone. Installing downloads it from the source, makes sure it's exactly the file Rabbid Lab's release signature
/// describes, and runs it quietly; the installer restarts the service as the new version. Never back to an older version,
/// and a version that failed to install isn't tried again by itself for six hours. Where it can't install itself (in
/// Docker), a newer version is only offered: it's updated with its new download (docker compose up -d --build).
/// The engine knows nothing of the console around it: the source says what's offered and hands it over, and the host
/// says which version is running, where files go, whether it can install itself and may do so by itself, and hears what
/// it did.
/// </summary>
public sealed class UpdateEngine(UpdateOptions options, IUpdateSource source, IUpdateHost host, IInstallerRunner runner, ILogger<UpdateEngine> log)
{
    private readonly string current = host.Version;
    private readonly string updates = Path.Combine(host.Folder, "Updates");
    private readonly DateTime started = DateTime.UtcNow;
    private readonly SemaphoreSlim gate = new(1, 1); // one decision at a time
    private readonly Lock installLock = new();
    private readonly HashSet<string> refused = []; // versions offered without a valid release signature (logged once each)
    private Task? install; // Install now, going on in the background
    private CancellationToken stopping = CancellationToken.None;
    private long lastUsed; // when someone last used the console (UTC ticks)
    private int checking; // 1 while a check a page asked for is under way
    private string? installingVersion; // the installer was started for this version, at installingAt
    private DateTime installingAt;
    private (string Version, string Why)? notStarted; // Windows wouldn't run this version's installer, and why

    /// <summary>Where updating stands now.</summary>
    public UpdateState State { get; private set; } = new(host.Version, null, UpdateStatus.Current, "Rabbid Lab is up to date.");

    /// <summary>
    /// The version this one replaced, when this is the first time it has run since updating itself ("earlier" when
    /// that version isn't known, for an update from before version.txt was kept). Null otherwise.
    /// </summary>
    public string? UpdatedFrom { get; } = Replaced(host.Folder, host.Version);

    /// <summary>
    /// Someone is using the console (a request from a signed-in person, other than the page's background polls). For
    /// InUseFor, a new version is offered and waits: it never installs under anyone.
    /// </summary>
    public void InUse() => Interlocked.Exchange(ref lastUsed, DateTime.UtcNow.Ticks);

    /// <summary>
    /// Whether anyone has used the console within InUseFor: a new version then waits. A session whose tab has gone (closed
    /// without signing out) doesn't count, however long it has left: nobody is there to work under.
    /// </summary>
    public bool InUseNow => DateTime.UtcNow.Ticks - Interlocked.Read(ref lastUsed) < options.InUseFor.Ticks;

    /// <summary>Whether a check a page asked for (Watch) is under way.</summary>
    public bool Checking => Volatile.Read(ref checking) == 1;

    private bool UnderWay => State.Status is UpdateStatus.Downloading or UpdateStatus.Installing;

    /// <summary>Looks at what the source offered every Every (a minute) until stopped, and decides each time.</summary>
    public async Task Run(CancellationToken stop)
    {
        stopping = stop;
        await Task.Yield();
        Tidy();
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await TryNow(stop);
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                log.LogError(e, "The automatic update failed");
            }
            try
            {
                await Task.Delay(options.Every, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Looks at what the source last offered: installs it, offers it or waits, as Decide says. Anything that looks while
    /// an update is downloading is told how far it has got, at once.
    /// </summary>
    public Task<UpdateState> TryNow(CancellationToken stop) => Attempt(stop, chosen: false);

    /// <summary>
    /// Check for updates (an administrator asking): asks the source straight away, unless anything asked it within
    /// AskAgainAfter, then decides. While an update downloads or installs, it says how far that has got.
    /// </summary>
    public async Task<UpdateState> CheckNow(CancellationToken stop)
    {
        if (UnderWay)
        {
            return State;
        }
        if (source.CheckedAt is not { } at || DateTime.UtcNow - at >= options.AskAgainAfter)
        {
            await source.Check(stop);
        }
        return await TryNow(stop);
    }

    /// <summary>
    /// A page is showing where updating stands (the sign-in page, before anyone signs in): opened, or asking again while
    /// it stays open. When the last check-in is older than CheckWhenOpened (opened) or CheckWhileOpen, the source is asked
    /// again in the background (one check at a time), and what it says is decided straight away: a newer version shows on
    /// the page within moments, and installs by itself when nobody is using the console. A version offered while someone
    /// was is installed as soon as a page asks after they have gone. Answers at once with what is known now.
    /// </summary>
    public UpdateState Watch(bool opened)
    {
        var due = opened ? options.CheckWhenOpened : options.CheckWhileOpen;
        var ask = source.CheckedAt is not { } at || DateTime.UtcNow - at >= due;
        var install = State.Status == UpdateStatus.Available && host.Automatic && !InUseNow;
        if (!UnderWay && (ask || install) && Interlocked.Exchange(ref checking, 1) == 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (ask)
                    {
                        await source.Check(stopping);
                    }
                    await TryNow(stopping);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogWarning(e, "The check for a newer version a page asked for could not be made");
                }
                catch (OperationCanceledException)
                {
                    // Stopping.
                }
                finally
                {
                    Interlocked.Exchange(ref checking, 0);
                }
            });
        }
        return State;
    }

    /// <summary>
    /// The same, answered once the check (the one it started, or one under way) is done, or an update is on its way, or
    /// the wait is up: the sign-in page shows its form only once it's known nothing is about to install under it.
    /// </summary>
    public async Task<UpdateState> Watch(bool opened, TimeSpan wait, CancellationToken cancel)
    {
        Watch(opened);
        var until = DateTime.UtcNow + wait;
        while (Checking && !UnderWay && DateTime.UtcNow < until)
        {
            await Task.Delay(100, cancel);
        }
        return State;
    }

    /// <summary>
    /// Install now: someone chose to install the newer version. It answers at once: the download and installation go on
    /// in the background, and State shows how far they have got (the console polls it). Finished hears how it went:
    /// installing, or why not. Chosen again while it is under way, it answers with where it is.
    /// </summary>
    public UpdateState InstallNow(Action<UpdateState>? finished = null)
    {
        lock (installLock)
        {
            if (install is { IsCompleted: false })
            {
                return State;
            }
            var offer = source.Offer;
            State = new(current, offer?.Version ?? State.Available, UpdateStatus.Downloading, "Getting the update ready.", 0, offer?.Size);
            install = Task.Run(async () =>
            {
                UpdateState done;
                try
                {
                    done = await Attempt(stopping, chosen: true);
                }
                catch (Exception e) when (!stopping.IsCancellationRequested)
                {
                    log.LogError(e, "The update could not be installed");
                    done = State = new(current, State.Available, UpdateStatus.Failed, $"The update didn't install ({e.Message.TrimEnd('.')}).");
                }
                finished?.Invoke(done);
            });
            return State;
        }
    }

    /// <summary>One decision at a time. Chosen (Install now) waits its turn; a regular look while one is under way doesn't.</summary>
    private async Task<UpdateState> Attempt(CancellationToken stop, bool chosen)
    {
        if (!await gate.WaitAsync(chosen ? Timeout.Infinite : 0, stop))
        {
            return State;
        }
        try
        {
            return State = await Decide(stop, chosen);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Whether the offered version is newer than this one. A test build ("1.2.3-test") is older than the release 1.2.3.</summary>
    public static bool Newer(string? offered, string current)
    {
        var test = current.EndsWith("-test", StringComparison.Ordinal);
        return Version.TryParse(offered, out var o) && Version.TryParse(test ? current[..^5] : current, out var mine) && (o > mine || (test && o == mine));
    }

    private async Task<UpdateState> Decide(CancellationToken stop, bool chosen)
    {
        var offer = source.Offer;
        if (offer is null || !Newer(offer.Version, current))
        {
            return Say(UpdateStatus.Current, null, current.EndsWith("-test", StringComparison.Ordinal)
                ? "This is a test build, and no release is newer." : "Rabbid Lab is up to date.");
        }
        var version = offer.Version;
        // Docker: nothing installs here, so there's nothing to check the signature of; the new download is on the website.
        if (!host.CanInstall)
        {
            return Say(UpdateStatus.Available, version, $"Version {version} is out. In Docker, update with docker compose up -d --build. Your data is kept.");
        }
        // Only a release signed with Rabbid Lab's offline release key is ever installed, whoever offers it.
        if (!Release.Verify(options.ReleasePublicKey, version, offer.Sha256, offer.Size, offer.Signature, host.ProductName))
        {
            if (refused.Add(version))
            {
                log.LogWarning("Rabbid Lab {Version} was offered without a valid Rabbid Lab release signature and will not be installed", version);
            }
            return Say(UpdateStatus.Current, null, $"Rabbid Lab is up to date. Version {version} on GitHub isn't signed by Rabbid Lab, so it won't be installed.");
        }
        // The installer is running: it restarts Rabbid Lab as the new version shortly.
        if (version == installingVersion && DateTime.UtcNow - installingAt < options.InstallWait)
        {
            return Installing(version);
        }
        // Automatic installation turned off: offered, installed only when someone chooses Install now.
        if (!chosen && !host.Automatic)
        {
            return Say(UpdateStatus.Available, version, $"Version {version} is ready to install. Installing takes about a minute, and Rabbid Lab is stopped while it does.");
        }
        // Someone is using the console: offered, never installed under anyone. Nobody has for a couple of minutes: installed.
        if (!chosen && InUseNow)
        {
            return Say(UpdateStatus.Available, version, $"Version {version} is ready. It installs automatically when nobody is using Rabbid Lab, or click Install now.");
        }
        if (!chosen && DateTime.UtcNow - started < options.SettleTime)
        {
            return Say(UpdateStatus.Waiting, version, $"Version {version} installs once Rabbid Lab has finished starting.");
        }
        var tried = Path.Combine(updates, "last-try.txt");
        if (!chosen && File.Exists(tried) && File.ReadAllText(tried).Trim() == version && DateTime.UtcNow - File.GetLastWriteTimeUtc(tried) < options.RetryAfter)
        {
            var why = notStarted is { } n && n.Version == version ? n.Why : LogSaid(version);
            return Say(UpdateStatus.Failed, version, why is null
                ? $"Version {version} didn't install. Rabbid Lab tries again later. The installation log is in {updates}."
                : $"Version {version} didn't install. {why} Rabbid Lab tries again later.");
        }
        Directory.CreateDirectory(updates);
        File.WriteAllText(tried, version);
        State = Say(UpdateStatus.Downloading, version, $"Downloading version {version}.") with { Downloaded = 0, Total = offer.Size };
        FileStream held;
        try
        {
            held = await ReleaseDownload.Get(source, offer, updates, options, got => State = State with { Downloaded = got }, stop);
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or IOException or InvalidOperationException
            || (e is TaskCanceledException && !stop.IsCancellationRequested))
        {
            log.LogWarning(e, "Could not download Rabbid Lab {Version}", version);
            var why = e.Message.TrimEnd('.');
            if (!chosen)
            {
                host.Record(new UpdateAttempt(version, $"The update couldn't be downloaded ({why})."));
            }
            return Say(UpdateStatus.Failed, version, $"Version {version} couldn't be downloaded ({why}). Rabbid Lab tries again later.");
        }
        // Held open until the installer has started, so the file that was checked is the file that runs.
        using var keep = held;
        var installer = held.Name;
        log.LogWarning("Updating Rabbid Lab from version {From} to {To}. The console will restart shortly.", current, version);
        var setupLog = Path.Combine(updates, $"update-{version}.log");
        try
        {
            runner.Run(installer, setupLog);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Windows wouldn't run it: security software, most often (the runner says which, and how to allow it). Said in
            // the service log, and in the installation log, as Setup would have, which Setup never got to write.
            var why = e is InstallerBlockedException ? e.Message : $"Its installer couldn't be started ({e.Message.TrimEnd('.')}).";
            log.LogError(e, "Rabbid Lab {Version} could not be installed. {Why}", version, why);
            try
            {
                File.AppendAllText(setupLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  FAILED: {why}{Environment.NewLine}");
            }
            catch (Exception io) when (io is IOException or UnauthorizedAccessException)
            {
                // The service log and the page still say it.
            }
            notStarted = (version, why);
            if (!chosen)
            {
                host.Record(new UpdateAttempt(version, why));
            }
            return Say(UpdateStatus.Failed, version, $"Version {version} didn't install. {why}");
        }
        if (!chosen)
        {
            host.Record(new UpdateAttempt(version, null));
        }
        installingVersion = version;
        installingAt = DateTime.UtcNow;
        return Installing(version);
    }

    private UpdateState Say(UpdateStatus status, string? available, string message) => new(current, available, status, message);

    /// <summary>
    /// Why the installation failed, from its log (update-version.log): the last FAILED line, which Setup writes when it stops,
    /// and the engine when Windows wouldn't run Setup at all. Null if there's none.
    /// </summary>
    private string? LogSaid(string version)
    {
        try
        {
            var failed = File.ReadLines(Path.Combine(updates, $"update-{version}.log")).LastOrDefault(l => l.Contains("FAILED: ", StringComparison.Ordinal));
            if (failed is null)
            {
                return null;
            }
            // FAILED: and the exception, as Setup writes it: its type first ("System.InvalidOperationException: ").
            var said = Regex.Replace(failed[(failed.IndexOf("FAILED: ", StringComparison.Ordinal) + 8)..], @"^[\w.]+Exception: ", "").Trim().TrimEnd('.');
            return said.Length == 0 ? null : $"{said}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Deletes old installers and their logs from Updates, keeping this version's, the newest and the last one tried.</summary>
    public void Tidy()
    {
        try
        {
            if (!Directory.Exists(updates))
            {
                return;
            }
            var files = Directory.GetFiles(updates).Select(f => (Path: f, Match: Regex.Match(Path.GetFileName(f), @"^(?:RabbidLab-Setup-|update-)(.+?)(\.exe\.part|\.exe|\.log)$")))
                .Where(f => f.Match.Success).Select(f => (f.Path, Version: f.Match.Groups[1].Value, Part: f.Match.Groups[2].Value == ".exe.part")).ToList();
            var tried = Path.Combine(updates, "last-try.txt");
            var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { current, current.EndsWith("-test", StringComparison.Ordinal) ? current[..^5] : current };
            if (File.Exists(tried))
            {
                kept.Add(File.ReadAllText(tried).Trim());
            }
            if (files.Where(f => !f.Part && Version.TryParse(f.Version, out _)).Select(f => f.Version).MaxBy(Version.Parse) is { } newest)
            {
                kept.Add(newest);
            }
            foreach (var f in files.Where(f => f.Part || !kept.Contains(f.Version)))
            {
                File.Delete(f.Path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Tried again the next time it starts.
        }
    }

    private UpdateState Installing(string version) =>
        Say(UpdateStatus.Installing, version, $"Installing version {version}. Rabbid Lab is stopped for about a minute.");

    /// <summary>Notes the version running now in version.txt, and says which one it replaced, if this is its first run.</summary>
    private static string? Replaced(string folder, string current)
    {
        var file = Path.Combine(folder, "version.txt");
        string? before = null;
        try
        {
            before = File.Exists(file) ? File.ReadAllText(file).Trim() : null;
            // Updated by a version that didn't keep version.txt yet: the update it installed is in last-try.txt.
            var tried = Path.Combine(folder, "Updates", "last-try.txt");
            if (before is null && File.Exists(tried) && File.ReadAllText(tried).Trim() == current)
            {
                before = "earlier";
            }
            File.WriteAllText(file, current);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not knowing only means the console doesn't say it was updated.
        }
        return before is not null && before != current ? before : null;
    }
}
