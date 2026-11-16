using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace RabbidLab.Server;

/// <summary>
/// Installs Group Policy Management on this server with the one command that fits it, as SYSTEM: Windows Server adds
/// the GPMC feature, Windows 10 and 11 the RSAT capability. Never anything typed. It runs on its own while the page asks
/// how it's going, and the result goes in the activity log.
/// </summary>
public sealed class GpmcSetup(ActivityLog log)
{
    /// <summary>Restart: Windows installed it and wants a restart before it works.</summary>
    public sealed record View(bool Here, bool Can, string Command, bool Running, bool? Ok, string? Output, DateTime? Started, DateTime? Finished, string Machine,
        int? Percent, bool Restart);

    /// <summary>How a run ended: whether Windows said it installed, whether it wants a restart, and what it wrote.</summary>
    public sealed record Ended(bool Ok, bool Restart, string Text);

    private readonly Lock gate = new();
    private Task? running;
    private bool? ok;
    private bool restart;
    private int? percent;
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
            var busy = running is { IsCompleted: false };
            return new View(here, Can, Command, busy, ok, output, started, finished, Environment.MachineName, busy ? percent : null, ok == true && restart && !here);
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
            (ok, restart, percent, output, started, finished) = (null, false, null, null, DateTime.UtcNow, null);
            running = Task.Run(() => Run(by, from));
            return true;
        }
    }

    private async Task Run(string by, string? from)
    {
        var command = Command;
        Ended ended;
        try
        {
            ended = await Install(command, p => { lock (gate) { percent = p; } }, TimeSpan.FromMinutes(30));
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException or IOException)
        {
            ended = new Ended(false, false, e.Message);
        }
        var text = ended.Text;
        lock (gate)
        {
            (ok, restart, output, finished) = (ended.Ok, ended.Restart, text.Length > 4000 ? text[^4000..] : text, DateTime.UtcNow);
        }
        var summary = !ended.Ok ? "Couldn't install Group Policy Management on this server"
            : ended.Restart ? "Installed Group Policy Management on this server: it needs a restart to finish"
            : "Installed Group Policy Management on this server";
        log.Record(by, from, "users", "gpo.gpmc", summary, ended.Ok ? "ok" : "failed", $"{command}{(text.Length > 0 ? $": {(text.Length > 500 ? text[^500..] : text)}" : "")}");
    }

    /// <summary>
    /// The command in a runspace of its own, so its progress can be read: a PCT line for each new percentage, then one
    /// RESULT line with whether it worked and whether Windows wants a restart. A warning is said, and isn't a failure.
    /// </summary>
    internal static string Script(string command) => $$"""
        $ErrorActionPreference = 'Stop'
        $ps = [powershell]::Create()
        [void]$ps.AddScript('{{command.Replace("'", "''")}}')
        $seen = -1
        $run = $ps.BeginInvoke()
        while (-not $run.IsCompleted) {
            Start-Sleep -Milliseconds 400
            $n = $ps.Streams.Progress.Count
            if ($n) {
                $p = $ps.Streams.Progress[$n - 1].PercentComplete
                if ($p -gt $seen -and $p -le 100) { $seen = $p; "PCT $p" }
            }
        }
        $r = @($ps.EndInvoke($run)) | Select-Object -Last 1
        foreach ($w in $ps.Streams.Warning) { "$w" }
        if ($ps.Streams.Error.Count) {
            foreach ($e in $ps.Streams.Error) { [Console]::Error.WriteLine("$e") }
            exit 1
        }
        $worked = $null -eq $r -or $null -eq $r.Success -or [bool]$r.Success
        $restart = $null -ne $r -and "$($r.RestartNeeded)" -in 'Yes', 'True'
        "RESULT $worked $restart"
        if (-not $worked) { exit 1 }
        """;

    /// <summary>Runs the command with Windows PowerShell and says how it ended; percent hears each new percentage.</summary>
    public static async Task<Ended> Install(string command, Action<int> percent, TimeSpan limit)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script(command)));
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(start) ?? throw new InvalidOperationException("PowerShell didn't start.");
        var said = new StringBuilder();
        bool? worked = null;
        var restart = false;
        var reading = Task.Run(async () =>
        {
            while (await p.StandardOutput.ReadLineAsync() is { } line)
            {
                var parts = line.Split(' ');
                if (parts is ["PCT", var n] && int.TryParse(n, out var at))
                {
                    percent(at);
                }
                else if (parts is ["RESULT", var w, var r])
                {
                    (worked, restart) = (w == "True", r == "True");
                }
                else if (line.Trim().Length > 0)
                {
                    said.AppendLine(line.Trim());
                }
            }
        });
        var errors = p.StandardError.ReadToEndAsync();
        using var stop = new CancellationTokenSource(limit);
        try
        {
            await p.WaitForExitAsync(stop.Token);
        }
        catch (OperationCanceledException)
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"It was still running after {limit.TotalMinutes:0} minutes, so it was stopped.");
        }
        await reading;
        var err = (await errors).Trim();
        var text = $"{said.ToString().Trim()}\n{err}".Trim();
        return new Ended(p.ExitCode == 0 && worked == true, restart, text);
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
