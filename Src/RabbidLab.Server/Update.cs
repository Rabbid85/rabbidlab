using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RabbidLab.Updater;

namespace RabbidLab.Server;

/// <summary>
/// Rabbid Lab around the update engine: the version running, the data folder, whether it can install itself (as a Windows
/// service, not in Docker), whether new versions install by themselves, and the activity log, which records the
/// installations the engine starts by itself (Install now records its own, as whoever chose it). Where the updates come
/// from is Updates.cs: GitHub.
/// </summary>
public sealed class ConsoleUpdateHost(SettingsStore store, DataFolder folder, ActivityLog activity) : IUpdateHost
{
    /// <summary>Who the activity log says installed an update that installed by itself.</summary>
    public const string AutomaticBy = Product.Name + " (automatic installation)";

    /// <summary>As a Windows service it installs its own updates (with Setup); in Docker it's updated with its new download.</summary>
    public static bool SelfInstalls => OperatingSystem.IsWindows() && !Product.InContainer;

    // A test build says so, so the release with its number counts as newer (UpdateEngine.Newer).
    public string Version => Product.Test ? $"{Product.Version}-test" : Product.Version;

    // Rabbid Lab Dev only installs releases signed as Rabbid Lab Dev, and the release only its own.
    public string ProductName => Product.Name;

    public string Folder => folder.Path;

    public bool CanInstall => SelfInstalls;

    public bool Automatic => store.Current.AutomaticUpdates;

    public void Record(UpdateAttempt attempt) =>
        activity.Record(AutomaticBy, null, "admin", "update.install", $"Install update: {Product.Name} {attempt.Version}", attempt.Problem is null ? "ok" : "failed", attempt.Problem);
}

/// <summary>
/// Runs the installer an update downloaded (InstallerRunner). When Windows won't let it run, it says what stopped it, as
/// Setup does for a blocked service: what Microsoft Defender, AppLocker and Windows Defender Application Control recorded
/// about it a moment later, and how to allow Rabbid Lab's updates there (its Updates folder, and the program the installer
/// puts in place, which a rule that blocks the one blocks after the update too).
/// </summary>
public sealed class ConsoleInstallerRunner(InstallerRunner runner) : IInstallerRunner
{
    /// <summary>The attack surface reduction rules that stop a program from running, by their IDs.</summary>
    private static readonly Dictionary<string, string> AsrRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["01443614-cd74-433a-b99e-2ecdc07bfc25"] = "Block executable files from running unless they meet a prevalence, age, or trusted list criterion",
        ["c1db55ab-c21a-4637-bb3f-a12568109d35"] = "Use advanced protection against ransomware",
        ["c0033c00-d16d-4114-a5a0-dc9b3a7d2ceb"] = "Block use of copied or impersonated system tools",
    };

    public void Run(string installer, string log)
    {
        try
        {
            runner.Run(installer, log);
        }
        // Access denied, a virus found or removed, blocked by policy, blocked by application control.
        catch (Win32Exception e) when (OperatingSystem.IsWindows() && e.NativeErrorCode is 5 or 225 or 226 or 1260 or 4551)
        {
            throw new InstallerBlockedException(Blocked(installer), e);
        }
    }

    [SupportedOSPlatform("windows")]
    private static string Blocked(string installer)
    {
        Thread.Sleep(1500); // what blocked it is written down a moment later
        var file = Path.GetFileName(installer);
        var updates = Path.GetDirectoryName(installer);
        var exe = Environment.ProcessPath;
        var allow = $"{updates} (where updates are downloaded) and {exe} (the program they install)";
        if (Recorded("Microsoft-Windows-Windows Defender/Operational", "EventID=1121", file) is { } asr)
        {
            var rule = Regex.Match(asr, @"\bID:\s*([0-9A-Fa-f-]{36})").Groups[1].Value;
            var said = $"Microsoft Defender blocked its installer ({installer}) with the attack surface reduction rule "
                + (AsrRules.TryGetValue(rule, out var name) ? $"“{name}”" : rule) + ", which stops programs few computers run yet, such as a new release.";
            return DefenderManagedBy() is { } by
                ? $"{said} These rules are set with {by}: add {allow} to its attack surface reduction exclusions, then select Try again."
                : $"{said} To let {Product.Name} update itself on this machine, run  Add-MpPreference -AttackSurfaceReductionOnlyExclusions '{updates}', '{exe}'  in PowerShell as an administrator, then select Try again.";
        }
        if (Recorded("Microsoft-Windows-Windows Defender/Operational", "EventID=1116 or EventID=1117", file) is not null)
        {
            return $"Microsoft Defender Antivirus reported its installer ({installer}) as a threat. It carries Rabbid Lab's release signature, so the detection is wrong: allow it in Windows Security > Virus & threat protection > Protection history, then select Try again.";
        }
        if (Recorded("Microsoft-Windows-AppLocker/EXE and DLL", "EventID=8004", file) is not null)
        {
            return $"AppLocker on this machine doesn't allow its installer ({installer}) to run. Allow {allow} in its executable rules, then select Try again.";
        }
        if (Recorded("Microsoft-Windows-CodeIntegrity/Operational", "EventID=3077 or EventID=3033 or EventID=3034", file) is not null)
        {
            return $"Windows Defender Application Control on this machine doesn't allow its installer ({installer}) to run. Allow {allow} in its policy, then select Try again.";
        }
        return $"Windows didn't let its installer ({installer}) run. Microsoft Defender, AppLocker and application control recorded no block, so other security software on this machine most likely stopped it: allow {allow} there, then select Try again.";
    }

    /// <summary>What a Windows log recorded about the file in the last two minutes, or null.</summary>
    [SupportedOSPlatform("windows")]
    private static string? Recorded(string logName, string ids, string file)
    {
        try
        {
            var query = new EventLogQuery(logName, PathType.LogName, $"*[System[({ids}) and TimeCreated[timediff(@SystemTime) <= 120000]]]") { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (var e = reader.ReadEvent(); e is not null; e = reader.ReadEvent())
            {
                using (e)
                {
                    string text;
                    try
                    {
                        text = e.FormatDescription() ?? "";
                    }
                    catch (EventLogException)
                    {
                        text = "";
                    }
                    if (text.Contains(file, StringComparison.OrdinalIgnoreCase))
                    {
                        return text;
                    }
                }
            }
        }
        catch (Exception e) when (e is EventLogException or UnauthorizedAccessException)
        {
            // That log isn't on this machine, or can't be read.
        }
        return null;
    }

    /// <summary>Who sets this machine's attack surface reduction rules, when it isn't the machine itself: Intune or Group Policy.</summary>
    [SupportedOSPlatform("windows")]
    private static string? DefenderManagedBy()
    {
        try
        {
            using var mdm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\PolicyManager\current\device\Defender");
            using var gpo = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR");
            using var policy = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender");
            if ((mdm?.GetValue("AttackSurfaceReductionRules") ?? mdm?.GetValue("AttackSurfaceReductionRules_ProviderSet")) is not null)
            {
                return "Intune";
            }
            if (Convert.ToInt32(gpo?.GetValue("ExploitGuard_ASR_Rules") ?? 0) == 1)
            {
                return "Group Policy";
            }
            // The machine's own exclusions don't count: whoever manages Defender adds them.
            return Convert.ToInt32(policy?.GetValue("DisableLocalAdminMerge") ?? 0) == 1 ? "your organization's Defender policy" : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}

/// <summary>Runs the update engine while Rabbid Lab runs.</summary>
public sealed class UpdateWorker(UpdateEngine engine) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stop) => engine.Run(stop);
}

public static class UpdateApi
{
    /// <summary>
    /// Where updating stands: this version and any newer one, the status and what to say, when GitHub was last asked and why
    /// it didn't answer, the version this one updated from, whether new versions install by themselves, the download's
    /// progress, whether it can install itself here, and where it looks.
    /// </summary>
    public sealed record UpdateView(string Version, string? Available, string Status, string Message, DateTime? CheckedAt, string? UpdatedFrom, bool Automatic,
        long? Downloaded, long? Total, string? Problem, bool CanInstall, string Source);

    public sealed record UpdateSettingsForm(bool Automatic);

    /// <summary>The status as the page knows it: current, available, waiting, downloading, installing or failed.</summary>
    private static string Word(UpdateStatus status) => status.ToString().ToLowerInvariant();

    public static void MapUpdateApi(this WebApplication app)
    {
        static UpdateView View(UpdateState s, UpdateEngine updates, IUpdateSource source, IUpdateHost host, SettingsStore store, GitHubOptions github) =>
            new(s.Version, s.Available, Word(s.Status), s.Message, source.CheckedAt, updates.UpdatedFrom, store.Current.AutomaticUpdates, s.Downloaded, s.Total,
                source.Problem, host.CanInstall, github.Manifest);
        // Whether the sign-in page's Install now may start the update: never while someone signed in is working.
        static bool Here(UpdateState s, UpdateEngine updates, IUpdateHost host) =>
            host.CanInstall && host.Automatic && s.Status == UpdateStatus.Available && !updates.InUseNow;
        // What the sign-in page may know: versions and progress.
        static object Watched(UpdateState s, UpdateEngine updates, IUpdateSource source, IUpdateHost host) =>
            new { version = s.Version, available = s.Available, status = Word(s.Status), downloaded = s.Downloaded, total = s.Total,
                checking = updates.Checking, checkedAt = source.CheckedAt, canInstall = host.CanInstall, installHere = Here(s, updates, host) };
        // Install now, from either page: answers at once, and the activity log records how it went once that is known.
        static UpdateState Install(HttpContext context, UpdateEngine updates, ActivityLog activity, string title)
        {
            var by = ActivityLog.Who(context);
            var from = ActivityLog.Address(context);
            return updates.InstallNow(done =>
            {
                var ok = done.Status == UpdateStatus.Installing;
                activity.Record(by, from, "admin", "update.install", $"{title}: {Product.Name} {done.Available ?? done.Version}", ok ? "ok" : "failed", ok ? null : done.Message);
            });
        }
        // What GitHub last said, and any update under way. Opening the page asks GitHub nothing: the source asks when Rabbid
        // Lab starts and every 12 hours, and Check for updates asks at once.
        app.MapGet("/api/update", (HttpContext context, UpdateEngine updates, IUpdateSource source, IUpdateHost host, SettingsStore store, GitHubOptions github) =>
            Access.Can(context, Permissions.UpdatesView) ? Results.Ok(View(updates.State, updates, source, host, store, github)) : Access.NotAllowed());
        // The sign-in page, when it opens (opened=true) and every 30 seconds while it's open: it asks GitHub when opened if
        // the last check is over a minute old, and otherwise over 5 minutes old. With wait=true the answer comes once the
        // check is done, or an update is on its way, or eight seconds are up. Anyone may ask, so it says versions and
        // progress only: why an update failed stays behind sign-in.
        app.MapGet("/api/update/watch", async (bool? opened, bool? wait, UpdateEngine updates, IUpdateSource source, IUpdateHost host, CancellationToken cancel) =>
        {
            var s = wait == true ? await updates.Watch(opened == true, TimeSpan.FromSeconds(8), cancel) : updates.Watch(opened == true);
            return Watched(s, updates, source, host);
        });
        // Install now on the sign-in page: only while new versions install by themselves, so it starts sooner what would
        // install anyway once nobody is using the console, and nothing else.
        app.MapPost("/api/update/watch/install", (HttpContext context, UpdateEngine updates, IUpdateSource source, IUpdateHost host, ActivityLog activity) =>
        {
            if (Access.Refuse(context) is { } refused)
            {
                return refused;
            }
            if (!Here(updates.State, updates, host))
            {
                return Results.Json(new { error = "Sign in to install it." }, statusCode: StatusCodes.Status403Forbidden);
            }
            return Results.Ok(Watched(Install(context, updates, activity, "Install update from the sign-in page"), updates, source, host));
        });
        // Automatic installation on or off. Off: updates install only when chosen.
        app.MapPost("/api/update/settings", async Task<IResult> (UpdateSettingsForm form, HttpContext context, UpdateEngine updates, IUpdateSource source, IUpdateHost host,
            SettingsStore store, GitHubOptions github, ActivityLog activity, CancellationToken cancel) =>
        {
            if (Access.RefuseUnless(context, Permissions.UpdatesAuto) is { } refused)
            {
                return refused;
            }
            store.Change(s => s.AutomaticUpdates = form.Automatic);
            activity.Record(context, "admin", "update.settings", form.Automatic ? "Turned Update automatically on" : "Turned Update automatically off");
            return Results.Ok(View(await updates.TryNow(cancel), updates, source, host, store, github));
        });
        app.MapPost("/api/update/check", async Task<IResult> (HttpContext context, UpdateEngine updates, IUpdateSource source, IUpdateHost host, SettingsStore store,
            GitHubOptions github, CancellationToken cancel) =>
            Access.RefuseUnless(context, Permissions.UpdatesCheck) ?? Results.Ok(View(await updates.CheckNow(cancel), updates, source, host, store, github)));
        // Answers at once; the page then shows the download's progress from /api/update. The activity log records how it
        // went once that is known: installing, or why not. In Docker it can't install itself: it's updated with its download.
        app.MapPost("/api/update/install", (HttpContext context, UpdateEngine updates, IUpdateSource source, IUpdateHost host, SettingsStore store, GitHubOptions github,
            ActivityLog activity) =>
        {
            if (Access.RefuseUnless(context, Permissions.UpdatesInstall) is { } refused)
            {
                return refused;
            }
            if (!host.CanInstall)
            {
                return Results.Json(new { error = "In Docker, update with the new download from GitHub: docker compose up -d --build. Your data is kept." },
                    statusCode: StatusCodes.Status400BadRequest);
            }
            return Results.Ok(View(Install(context, updates, activity, "Install update"), updates, source, host, store, github));
        });
    }
}
