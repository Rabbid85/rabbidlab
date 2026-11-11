using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbidLab.Server;
using RabbidLab.Updater;

namespace RabbidLab.Tests;

/// <summary>
/// The update engine on its own (Src/Utilities/RabbidLab.Updater): no console, no license and no settings, only a
/// stand-in source of releases and a stand-in host. LicenseTests try it inside the console, with the license server.
/// </summary>
public sealed class UpdateEngineTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rabbidlab-engine-" + Guid.NewGuid().ToString("N"));
    private readonly RSA release = RSA.Create(2048);
    private readonly FakeRunner runner = new();
    private readonly Source source = new();
    private readonly Host host;

    public UpdateEngineTests()
    {
        Directory.CreateDirectory(folder);
        host = new Host(folder);
    }

    public void Dispose()
    {
        release.Dispose();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private UpdateEngine Engine(Action<UpdateOptions>? change = null)
    {
        var options = new UpdateOptions { SettleTime = TimeSpan.Zero, InstallWait = TimeSpan.Zero, ReleasePublicKey = release.ExportSubjectPublicKeyInfoPem() };
        change?.Invoke(options);
        return new UpdateEngine(options, source, host, runner, NullLogger<UpdateEngine>.Instance);
    }

    /// <summary>A release the stand-in source offers, signed with the test's own release key.</summary>
    private void Offer(string version, byte[] installer, string product = "Rabbid Lab")
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(installer));
        var signature = Convert.ToBase64String(release.SignData(Release.Message(version, sha, installer.Length, product), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        source.Offer = new UpdateOffer(version, installer.Length, sha, "https://source.test/" + version, signature);
        source.Installer = installer;
    }

    [Fact]
    public async Task A_release_signed_for_the_other_edition_is_never_installed()
    {
        // Rabbid Lab Dev's release, offered to Rabbid Lab: the same key signed it, for another product.
        Offer("3.2.0", "pretend installer"u8.ToArray(), "Rabbid Lab Dev");

        var asRelease = await Engine().TryNow(CancellationToken.None);
        host.ProductName = "Rabbid Lab Dev";
        var asDev = await Engine().TryNow(CancellationToken.None);

        Assert.Equal(UpdateStatus.Current, asRelease.Status);
        Assert.Contains("isn't signed by Rabbid Lab", asRelease.Message);
        Assert.Equal(UpdateStatus.Installing, asDev.Status);
        Assert.Single(runner.Runs);
    }

    [Fact]
    public async Task A_download_bigger_than_the_release_is_cut_off_and_nothing_is_run()
    {
        var installer = "pretend installer"u8.ToArray();
        Offer("3.2.0", installer);
        source.Installer = [.. installer, .. new byte[500_000]];

        var state = await Engine().TryNow(CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, state.Status);
        Assert.Contains("larger than the release", state.Message);
        Assert.Empty(runner.Runs);
        Assert.True(new FileInfo(Path.Combine(folder, "Updates", "RabbidLab-Setup-3.2.0.exe.part")).Length <= installer.Length);
    }

    [Fact]
    public async Task The_installer_cannot_be_changed_between_being_checked_and_being_run()
    {
        Offer("3.2.0", "pretend installer"u8.ToArray());
        var runs = new Swapper();

        var state = await new UpdateEngine(new UpdateOptions { SettleTime = TimeSpan.Zero, ReleasePublicKey = release.ExportSubjectPublicKeyInfoPem() }, source, host, runs,
            NullLogger<UpdateEngine>.Instance).TryNow(CancellationToken.None);

        Assert.Equal(UpdateStatus.Installing, state.Status);
        if (OperatingSystem.IsWindows())
        {
            Assert.True(runs.Refused); // Windows holds the file for the engine until the installer has started
        }
    }

    [Fact]
    public void Old_installers_and_their_logs_are_deleted_when_it_starts()
    {
        var updates = Path.Combine(folder, "Updates");
        Directory.CreateDirectory(updates);
        string[] kept = ["RabbidLab-Setup-3.1.0.exe", "update-3.1.0.log", "RabbidLab-Setup-3.1.4.exe", "RabbidLab-Setup-3.2.0.exe", "update-3.2.0.log", "last-try.txt", "notes.txt"];
        string[] gone = ["RabbidLab-Setup-3.0.0.exe", "update-3.0.0.log", "RabbidLab-Setup-2.9.0.exe", "RabbidLab-Setup-3.3.0.exe.part"];
        foreach (var name in kept.Concat(gone))
        {
            File.WriteAllText(Path.Combine(updates, name), name == "last-try.txt" ? "3.1.0" : "x");
        }

        Engine().Tidy();

        // This version's (3.1.4), the newest (3.2.0) and the last one tried (3.1.0) stay; so does anything that isn't an update's.
        Assert.Equal(kept.Order(), Directory.GetFiles(updates).Select(Path.GetFileName).Order());
    }

    /// <summary>A runner that tries to put another file in the installer's place, as something else on the machine might.</summary>
    private sealed class Swapper : IInstallerRunner
    {
        public bool Refused { get; private set; }

        public void Run(string installer, string log)
        {
            try
            {
                File.WriteAllText(installer, "something else");
            }
            catch (IOException)
            {
                Refused = true;
            }
        }
    }

    [Fact]
    public void A_test_build_is_older_than_the_release_with_its_number()
    {
        Assert.True(UpdateEngine.Newer("0.4.0", "0.4.0-test"));
        Assert.False(UpdateEngine.Newer("0.4.0", "0.4.0"));
        Assert.True(UpdateEngine.Newer("0.5.0", "0.4.0"));
        Assert.False(UpdateEngine.Newer("0.3.9", "0.4.0-test"));
        Assert.False(UpdateEngine.Newer("not a version", "0.4.0"));
    }

    [Fact]
    public async Task On_its_own_it_downloads_a_signed_release_checks_it_and_runs_its_installer()
    {
        var installer = "pretend installer"u8.ToArray();
        Offer("3.2.0", installer);

        var state = await Engine().TryNow(CancellationToken.None);

        Assert.Equal(UpdateStatus.Installing, state.Status);
        Assert.Equal("3.1.4", state.Version); // the host's version, never the engine library's own
        var run = Assert.Single(runner.Runs);
        Assert.Equal(Path.Combine(folder, "Updates", "RabbidLab-Setup-3.2.0.exe"), run.Installer);
        Assert.Equal(installer, File.ReadAllBytes(run.Installer));
        Assert.Equal(Path.Combine(folder, "Updates", "update-3.2.0.log"), run.Log);
        Assert.Equal(new UpdateAttempt("3.2.0", null), Assert.Single(host.Attempts)); // installed by itself: the host hears of it
    }

    [Fact]
    public async Task A_release_the_key_did_not_sign_is_never_downloaded_or_run()
    {
        Offer("3.2.0", "pretend installer"u8.ToArray());
        source.Offer = source.Offer! with { Signature = Convert.ToBase64String(new byte[256]) };

        var state = await Engine().TryNow(CancellationToken.None);

        Assert.Equal(UpdateStatus.Current, state.Status);
        Assert.Contains("isn't signed by Rabbid Lab", state.Message);
        Assert.Equal(0, source.Fetches);
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public async Task An_earlier_download_that_is_still_exactly_the_release_is_used_without_fetching_it_again()
    {
        var installer = "pretend installer"u8.ToArray();
        Offer("3.2.0", installer);
        Directory.CreateDirectory(Path.Combine(folder, "Updates"));
        File.WriteAllBytes(Path.Combine(folder, "Updates", "RabbidLab-Setup-3.2.0.exe"), installer);

        Assert.Equal(UpdateStatus.Installing, (await Engine().TryNow(CancellationToken.None)).Status);
        Assert.Equal(0, source.Fetches);
        Assert.Single(runner.Runs);
    }

    [Fact]
    public async Task A_download_that_stalls_ends_with_the_reason_and_nothing_is_run()
    {
        Offer("3.2.0", "pretend installer"u8.ToArray());
        source.Stall = true;

        var state = await Engine(o => o.StallAfter = TimeSpan.FromMilliseconds(200)).TryNow(CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, state.Status);
        Assert.Contains("the download stalled after", state.Message);
        Assert.Empty(runner.Runs);
        Assert.False(File.Exists(Path.Combine(folder, "Updates", "RabbidLab-Setup-3.2.0.exe")));
        Assert.Contains("stalled", Assert.Single(host.Attempts).Problem);
    }

    [Fact]
    public async Task An_installer_Windows_will_not_run_says_why_then_and_while_it_waits_to_try_again()
    {
        Offer("3.2.0", "pretend installer"u8.ToArray());
        const string why = "Microsoft Defender blocked its installer with an attack surface reduction rule.";
        runner.Refuse = new InstallerBlockedException(why, new System.ComponentModel.Win32Exception(5));
        var engine = Engine();

        var state = await engine.TryNow(CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, state.Status);
        Assert.Equal($"Version 3.2.0 didn't install. {why}", state.Message);
        Assert.Equal(new UpdateAttempt("3.2.0", why), Assert.Single(host.Attempts)); // not recorded as installed
        // The installation log says it, though Setup never ran to write it.
        Assert.Contains($"FAILED: {why}", File.ReadAllText(Path.Combine(folder, "Updates", "update-3.2.0.log")));
        // A minute later, by itself: not tried again yet, and still saying why; and after a restart too.
        var later = await engine.TryNow(CancellationToken.None);
        Assert.Equal(UpdateStatus.Failed, later.Status);
        Assert.Equal($"Version 3.2.0 didn't install. {why} Rabbid Lab tries again later.", later.Message);
        Assert.Equal(later.Message, (await Engine().TryNow(CancellationToken.None)).Message);
        Assert.Single(host.Attempts);
    }

    [Fact]
    public async Task An_installation_that_failed_says_what_Setup_wrote_in_its_log()
    {
        Offer("3.2.0", "pretend installer"u8.ToArray());
        var updates = Path.Combine(folder, "Updates");
        Directory.CreateDirectory(updates);
        File.WriteAllText(Path.Combine(updates, "last-try.txt"), "3.2.0");
        File.WriteAllText(Path.Combine(updates, "update-3.2.0.log"), "2026-10-28 14:01:00  --- Rabbid Lab 3.2.0 setup\r\n"
            + "2026-10-28 14:01:09  FAILED: System.InvalidOperationException: The Rabbid Lab service started but is not responding on port 8444.\r\n"
            + "   at RabbidLab.Setup.Engine.StartService()\r\n");

        var state = await Engine().TryNow(CancellationToken.None); // started again, as the old version

        Assert.Equal(UpdateStatus.Failed, state.Status);
        Assert.Equal("Version 3.2.0 didn't install. The Rabbid Lab service started but is not responding on port 8444. Rabbid Lab tries again later.", state.Message);
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public async Task In_Docker_a_newer_version_is_only_offered_with_how_to_update()
    {
        host.CanInstall = false;
        source.Offer = new UpdateOffer("3.2.0", 0, "", "", null);

        var state = await Engine().TryNow(CancellationToken.None);

        Assert.Equal(UpdateStatus.Available, state.Status);
        Assert.Contains("docker compose up -d --build", state.Message);
        Assert.Equal(0, source.Fetches);
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public void The_console_tells_the_engine_the_version_built_into_it()
    {
        // The project's Version (the one publish.sh releases) is what the engine compares offers with. The library's own
        // version (1.0.0) would make every server think it was newer than any release, and never update again.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Src", "RabbidLab.Server", "RabbidLab.Server.csproj")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        var project = File.ReadAllText(Path.Combine(root.FullName, "Src", "RabbidLab.Server", "RabbidLab.Server.csproj"));
        var version = Regex.Match(project, "<Version>(.+?)</Version>").Groups[1].Value;
        Assert.Equal(version, Product.Version);

        var data = Path.Combine(folder, "console");
        Directory.CreateDirectory(data);
        using var app = new TestApp(data);
        Assert.Equal(version, app.Services.GetRequiredService<UpdateEngine>().State.Version);
    }

    /// <summary>A source that offers whatever the test says and hands the installer over (or stalls).</summary>
    private sealed class Source : IUpdateSource
    {
        public UpdateOffer? Offer { get; set; }

        public byte[] Installer { get; set; } = [];

        public bool Stall { get; set; }

        public int Fetches { get; private set; }

        public DateTime? CheckedAt => DateTime.UtcNow;

        public string? Problem => null;

        public Task Check(CancellationToken cancel) => Task.CompletedTask;

        public async Task Fetch(UpdateOffer offer, Func<Stream, CancellationToken, Task> read, CancellationToken cancel)
        {
            Fetches++;
            await using Stream body = Stall ? new StalledStream() : new MemoryStream(Installer);
            await read(body, cancel);
        }
    }

    private sealed class Host(string folder) : IUpdateHost
    {
        public List<UpdateAttempt> Attempts { get; } = [];

        public string Version => "3.1.4";

        public string Folder => folder;

        public bool CanInstall { get; set; } = true;

        public bool Automatic => true;

        public string ProductName { get; set; } = "Rabbid Lab";

        public void Record(UpdateAttempt attempt) => Attempts.Add(attempt);
    }

    /// <summary>A download that never sends another byte.</summary>
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
