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
        Task.Run(() =>
        {
            Guard.UsualPermissions();
            Guard.StartAgainWhenEnded();
        });
        stop = new CancellationTokenSource();
        var token = stop.Token;
        loop = Task.Run(() => Start(token));
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
        // The server hears the client has stopped (a restart, an update, an uninstall), so it shows Offline at once.
        Agent.Bye();
    }

    // Nothing is read from or written to the client's folder until it is the client's own (Folder.Secure): something
    // another account put there first is moved aside, and while it won't move the client does nothing but try again.
    private static async Task Start(CancellationToken stop)
    {
        var said = false;
        while (!Folder.Secure())
        {
            if (!said)
            {
                said = true;
                Journal.Write($"The Rabbid Lab Client is not running: {Folder.Data} was made by another account and cannot be moved aside. Delete or rename that folder; the client tries again every minute.");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stop).ConfigureAwait(false);
        }
        Jobs.Sweep();
        // Installed from a server's console and not connected yet: that server becomes this computer's.
        if (Settings.Adopt())
        {
            Journal.Write("The Rabbid Lab Client was installed from its server's console and is connecting to that server.");
        }
        // A new version since it last ran: written in what it did (Updated the client, or Installed).
        Logbook.Started(Agent.Version);
        // The tray icon asks it things through a pipe: Check in now, Ask IT for help (Pipe.cs).
        await Task.WhenAll(Task.Run(() => Run(stop)), Task.Run(() => Orders.Listen(stop)), Task.Run(() => Pipe.Listen(stop))).ConfigureAwait(false);
    }

    private static async Task Run(CancellationToken stop)
    {
        var agent = new Agent();
        while (!stop.IsCancellationRequested)
        {
            // The tray icon, for anyone signed in to Windows who hasn't got one.
            try
            {
                TrayStarter.Ensure();
            }
            catch (Exception)
            {
                // Tried again next round.
            }
            Status status;
            try
            {
                status = await agent.Round(stop).ConfigureAwait(false);
                // Not while a job runs: installing the new client stops this service, and the job with it.
                if (status.State is Status.Connected or Status.Waiting && !Jobs.Busy)
                {
                    await Updater.Follow(status, stop).ConfigureAwait(false);
                }
                // An approved computer tells the server what it is and has (Inventory.cs).
                if (status.State == Status.Connected)
                {
                    await Inventory.Follow(stop).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                Problems.Note("client", $"Check-in with the server failed: {e.Message}");
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
            // The window's ring counts down to the next round; Check in now (Pipe) starts it at once.
            var next = DateTime.UtcNow + Status.Every(status.State);
            try
            {
                status.Next = next;
                status.Save();
            }
            catch (IOException)
            {
                // The ring guesses.
            }
            // Between rounds the tray icon is still looked for every minute, for anyone who signed in meanwhile.
            while (!stop.IsCancellationRequested && next - DateTime.UtcNow is var left && left > TimeSpan.Zero)
            {
                if (await Pipe.Wait(left < TimeSpan.FromMinutes(1) ? left : TimeSpan.FromMinutes(1), stop).ConfigureAwait(false))
                {
                    break; // Check in now
                }
                try
                {
                    TrayStarter.Ensure();
                }
                catch (Exception)
                {
                    // Tried again in a minute.
                }
            }
        }
    }
}

/// <summary>
/// Keeps the client on the server's version: when the server is newer, downloads the client's MSI from it and installs
/// it quietly. Once a day at most for the same version, so a broken MSI doesn't install over and over. The MSI goes
/// in the Updates folder and its log in the Service folder, which only SYSTEM and administrators can open.
/// </summary>
internal static class Updater
{
    // When the next try may start, after one that didn't get the MSI running.
    private static DateTime notBefore = DateTime.MinValue;

    // The version whose failed install was already told to the server.
    private static string? told;

    // What Windows Installer's log says about how the last install ended, as ": Windows Installer error 1603".
    private static string InstallerSaid()
    {
        try
        {
            var log = File.ReadAllText(Folder.Kept("update.log")); // Unicode, which the byte order mark says
            var ended = System.Text.RegularExpressions.Regex.Matches(log, @"Installation success or error status: (\d+)");
            return ended.Count > 0 && ended[ended.Count - 1].Groups[1].Value is var code && code != "0" ? $": Windows Installer error {code}" : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    public static async Task Follow(Status status, CancellationToken stop)
    {
        if (!Version.TryParse(status.ServerVersion, out var server) || !Version.TryParse(Agent.Version, out var mine) || server <= mine)
        {
            return;
        }
        var tried = Folder.File("last-update.txt");
        if (File.Exists(tried) && File.ReadAllText(tried).Trim() == status.ServerVersion && DateTime.UtcNow - File.GetLastWriteTimeUtc(tried) < TimeSpan.FromDays(1))
        {
            // Started a while ago and this is still the old client: the install didn't take. Told once per version.
            if (told != status.ServerVersion && DateTime.UtcNow - File.GetLastWriteTimeUtc(tried) > TimeSpan.FromMinutes(10))
            {
                told = status.ServerVersion;
                Problems.Note("update", $"Client {status.ServerVersion} failed to install{InstallerSaid()}. The install is retried in 24 hours. See update.log in the client's Service folder on the computer for details.");
            }
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
            // A server without the MSI, or a download cut short: tried again in ten minutes.
            Problems.Note("update", $"Could not download client {status.ServerVersion} from the server.");
            return;
        }
        // msiexec carries on after this service stops for the upgrade, and starts the new one.
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        using var installer = Process.Start(new ProcessStartInfo(Path.Combine(system, "msiexec.exe"), $"/i \"{msi}\" /qn /norestart /l*v \"{Folder.Kept("update.log")}\"") { UseShellExecute = false, CreateNoWindow = true });
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
