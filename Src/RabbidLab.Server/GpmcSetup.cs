using System.Diagnostics;
using Microsoft.Win32;

namespace RabbidLab.Server;

/// <summary>
/// Installs Group Policy Management on this server with the one command that fits it, as SYSTEM: Windows Server adds
/// the GPMC feature, Windows 10 and 11 the RSAT capability. Never anything typed. It runs on its own while the page asks
/// how it's going, and the result goes in the activity log.
/// </summary>
public sealed class GpmcSetup(ActivityLog log)
{
    public sealed record View(bool Here, bool Can, string Command, bool Running, bool? Ok, string? Output, DateTime? Started, DateTime? Finished, string Machine);

    private readonly Lock gate = new();
    private Task? running;
    private bool? ok;
    private string? output;
    private DateTime? started;
    private DateTime? finished;

    /// <summary>Windows Server installs it as a feature; Windows 10 and 11 as an optional capability from Windows Update.</summary>
    public static string Command => IsServer()
        ? "Install-WindowsFeature -Name GPMC"
        : "Add-WindowsCapability -Online -Name Rsat.GroupPolicy.Management.Tools~~~~0.0.1.0";

    public static bool Can => OperatingSystem.IsWindows() && !Product.InContainer;

    public View Now(bool here)
    {
        lock (gate)
        {
            return new View(here, Can, Command, running is { IsCompleted: false }, ok, output, started, finished, Environment.MachineName);
        }
    }

    /// <summary>Starts it; false when it's already running.</summary>
    public bool Start(string by, string? from)
    {
        lock (gate)
        {
            if (running is { IsCompleted: false })
            {
                return false;
            }
            (ok, output, started, finished) = (null, null, DateTime.UtcNow, null);
            running = Task.Run(() => Run(by, from));
            return true;
        }
    }

    private async Task Run(string by, string? from)
    {
        var command = Command;
        bool done;
        string text;
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(start) ?? throw new InvalidOperationException("PowerShell didn't start.");
            var said = p.StandardOutput.ReadToEndAsync();
            var errors = p.StandardError.ReadToEndAsync();
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            try
            {
                await p.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                p.Kill(entireProcessTree: true);
                throw new TimeoutException("It was still running after 30 minutes, so it was stopped.");
            }
            var err = (await errors).Trim();
            text = $"{(await said).Trim()}\n{err}".Trim();
            done = p.ExitCode == 0 && err.Length == 0;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException or IOException)
        {
            text = e.Message;
            done = false;
        }
        lock (gate)
        {
            (ok, output, finished) = (done, text.Length > 4000 ? text[^4000..] : text, DateTime.UtcNow);
        }
        log.Record(by, from, "users", "gpo.gpmc", done ? "Installed Group Policy Management on this server" : "Couldn't install Group Policy Management on this server",
            done ? "ok" : "failed", $"{command}{(text.Length > 0 ? $": {(text.Length > 500 ? text[^500..] : text)}" : "")}");
    }

    private static bool IsServer()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return (key?.GetValue("InstallationType") as string)?.StartsWith("Server", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
