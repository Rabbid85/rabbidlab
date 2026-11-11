using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace RabbidLab.Client;

/// <summary>
/// The RabbidLabClient service: a round with the server every minute (every five while it turns joining down), the
/// server's version of the client installed whenever the server is newer, and the server's account changes (Orders.cs).
/// </summary>
internal sealed class ClientService : ServiceBase
{
    public const string Name = "RabbidLabClient";
    private CancellationTokenSource? stop;
    private Task? loop;

    public ClientService()
    {
        ServiceName = Name;
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        Folder.Secure();
        stop = new CancellationTokenSource();
        var token = stop.Token;
        loop = Task.WhenAll(Task.Run(() => Run(token)), Task.Run(() => Orders.Listen(token)));
    }

    protected override void OnStop() => Halt();

    protected override void OnShutdown() => Halt();

    private void Halt()
    {
        stop?.Cancel();
        try
        {
            loop?.Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // Stopping anyway.
        }
    }

    private static async Task Run(CancellationToken stop)
    {
        var agent = new Agent();
        while (!stop.IsCancellationRequested)
        {
            Status status;
            try
            {
                status = await agent.Round(stop).ConfigureAwait(false);
                if (status.State is Status.Connected or Status.Waiting)
                {
                    await Updater.Follow(status, stop).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                status = new Status { State = Status.Unreachable, Problem = e.Message };
                try
                {
                    status.Save();
                }
                catch (IOException)
                {
                    // Said next round.
                }
            }
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(status.State == Status.Refused ? 5 : 1), stop).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // Stopping.
            }
        }
    }
}

/// <summary>
/// Keeps the client on the server's version: when the server is newer, downloads the client's MSI from it and installs
/// it quietly. Once a day at most for the same version, so a broken MSI doesn't install over and over. The MSI goes
/// in the Updates folder, which only SYSTEM and administrators can write to.
/// </summary>
internal static class Updater
{
    // When the next try may start, after one that didn't get the MSI running.
    private static DateTime notBefore = DateTime.MinValue;

    public static async Task Follow(Status status, CancellationToken stop)
    {
        if (!Version.TryParse(status.ServerVersion, out var server) || !Version.TryParse(Agent.Version, out var mine) || server <= mine)
        {
            return;
        }
        var tried = Folder.File("last-update.txt");
        if (File.Exists(tried) && File.ReadAllText(tried).Trim() == status.ServerVersion && DateTime.UtcNow - File.GetLastWriteTimeUtc(tried) < TimeSpan.FromDays(1))
        {
            return;
        }
        var settings = Settings.Read();
        if (settings.Server is null || DateTime.UtcNow < notBefore)
        {
            return;
        }
        // If this try doesn't get as far as starting the MSI, the next is in ten minutes, not every round.
        notBefore = DateTime.UtcNow.AddMinutes(10);
        var msi = Path.Combine(Folder.Updates, $"RabbidLab-Client-{status.ServerVersion}.msi");
        Clear();
        bool downloaded;
        using (var api = new ServerApi(settings.Server, settings.Fingerprint))
        {
            try
            {
                downloaded = await api.Download("api/clients/download", msi, stop).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException or IOException || (e is TaskCanceledException && !stop.IsCancellationRequested))
            {
                downloaded = false;
            }
        }
        if (!downloaded)
        {
            return; // a server without the MSI, or a download cut short
        }
        // msiexec carries on after this service stops for the upgrade, and starts the new one.
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        using var installer = Process.Start(new ProcessStartInfo(Path.Combine(system, "msiexec.exe"), $"/i \"{msi}\" /qn /norestart /l*v \"{Folder.File("update.log")}\"") { UseShellExecute = false, CreateNoWindow = true });
        // Marked once it's started, so a download that failed is tried again and a broken MSI isn't run all day.
        File.WriteAllText(tried, status.ServerVersion);
    }

    /// <summary>Deletes the MSIs downloaded before, here and where older clients kept them.</summary>
    private static void Clear()
    {
        Directory.CreateDirectory(Folder.Updates);
        foreach (var old in Directory.GetFiles(Folder.Updates, "*.msi").Concat(Directory.GetFiles(Folder.Data, "RabbidLab-Client-*.msi")))
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // In use by an installation still running: deleted next time.
            }
        }
    }
}
