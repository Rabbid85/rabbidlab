using System.Diagnostics;

namespace RabbidLab.Server;

/// <summary>Puts the Rabbid Lab client on this server itself, so it's in Devices like the other computers.</summary>
public interface IHereClient
{
    /// <summary>Runs the client's MSI quietly, pointed at this server; msiexec's exit code, or null when it couldn't start.</summary>
    Task<int?> Install(string server, string? fingerprint, CancellationToken stop);
}

/// <summary>
/// msiexec, quietly, with this server's own address and key fingerprint, the same as the install line in Add computers.
/// Its log goes in the data folder. The client then joins, and is approved straight away because it joins from here.
/// </summary>
public sealed class HereClient(DataFolder folder, ILogger<HereClient> log) : IHereClient
{
    public async Task<int?> Install(string server, string? fingerprint, CancellationToken stop)
    {
        var logs = Path.Combine(folder.Path, "Logs");
        Directory.CreateDirectory(logs);
        var start = new ProcessStartInfo("msiexec.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "/i", ClientFiles.Msi, "/qn", "/norestart", $"SERVER={server}", "/l*v", Path.Combine(logs, "client-install.log") })
        {
            start.ArgumentList.Add(a);
        }
        if (!string.IsNullOrEmpty(fingerprint))
        {
            start.ArgumentList.Add($"FINGERPRINT={fingerprint}");
        }
        try
        {
            using var p = Process.Start(start);
            if (p is null)
            {
                return null;
            }
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
            limit.CancelAfter(TimeSpan.FromMinutes(5));
            await p.WaitForExitAsync(limit.Token);
            return p.ExitCode;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            log.LogWarning("Couldn't install the client on this server: {Problem}", e.Message);
            return null;
        }
    }
}
