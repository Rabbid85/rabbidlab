using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RabbidLab.Client;

/// <summary>
/// One round with the server: joins when this computer hasn't yet (or the server changed, or removed it), then checks
/// in, saying the computer, its Windows, this version and who is signed in. Writes how it went to status.json.
/// </summary>
internal sealed class Agent
{
    public static string Version { get; } = typeof(Agent).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    public Task<Status> Round(CancellationToken stop) => Round(stop, vouched: false);

    // vouched: this round follows the server vouching for a new key, so a second refusal isn't asked about again.
    private async Task<Status> Round(CancellationToken stop, bool vouched)
    {
        var settings = Settings.Read();
        var status = new Status { Server = settings.Server ?? settings.Plain, ServerVersion = Status.Read()?.ServerVersion, User = Machine.Users() };
        var joined = false; // this computer has an identity on this server
        if (settings.Plain is not null)
        {
            // An http:// address: nothing is sent to it, and a computer that was locked stays locked.
            return Unreachable(status, Removal.Enrolled, Settings.PlainNote);
        }
        if (settings.Server is null)
        {
            status.State = Status.NoServer;
            Leave();
            Lock(status, joined: false, null);
            return status.Save();
        }
        // A computer that has checked in with a server stays locked when the server can't be reached or joined, whatever
        // became of its identity: only the server, the uninstall code or the days without the server unlock it.
        var held = Removal.Enrolled;
        using var api = new ServerApi(settings.Server, settings.Fingerprint);
        try
        {
            var me = Identity.Load();
            held |= me is not null;
            if (me is null || me.Server != settings.Server)
            {
                var (code, made) = await api.Post<JoinAnswer>("api/clients/join",
                    new JoinRequest { Computer = Machine.Name, Os = Machine.Os, Version = Version }, null, stop).ConfigureAwait(false);
                if (code == (HttpStatusCode) 429)
                {
                    status.State = Status.Refused;
                    status.Problem = "The server is busy with other connection requests. This computer will retry in a few minutes.";
                    return status.Save();
                }
                if (made is null)
                {
                    return Unreachable(status, held, $"The server returned an error (HTTP {(int) code}).");
                }
                me = Identity.Save(made.Id, made.Secret, settings.Server);
            }
            joined = held = true;
            var check = Removal.CodeCheck;
            var problems = Problems.Waiting();
            var (answered, answer) = await api.Post<CheckInAnswer>("api/clients/checkin",
                new CheckInRequest
                {
                    Computer = Machine.Name, Os = Machine.Os, Version = Version, User = Machine.Users(),
                    Code = check?.Substring(check.IndexOf(':') + 1), CodeUsed = Removal.CodeUsed, Problems = problems,
                    Disks = Inventory.Drives(),
                },
                me.Key, stop).ConfigureAwait(false);
            if (answered == HttpStatusCode.OK && problems is not null)
            {
                Problems.Sent(problems);
            }
            if (answered == HttpStatusCode.Unauthorized)
            {
                Identity.Forget();
                status.State = Status.Removed;
                Leave();
                Lock(status, joined: false, null);
                return status.Save();
            }
            if (answer is null)
            {
                return Unreachable(status, held, $"The server returned an error (HTTP {(int) answered}).");
            }
            settings.KeepKey(api.Key);
            Vouch.Keep(answer.Identity); // trusted now, so its identity is too: it vouches for a changed key later
            status.State = answer.Approved ? Status.Connected : Status.Waiting;
            status.ServerVersion = answer.Version;
            status.CheckedIn = DateTime.UtcNow;
            Reached(answer);
            // What the window shows: a check-in this minute, the lab's look and Ask IT for help, and the lab's own icon
            // when it changed (lab-icon.png, which the tray icon reads).
            Logbook.Round(reached: true);
            Logbook.From(answer.Lab, answer.Help);
            await KeepIcon(api, me, answer.Lab, stop).ConfigureAwait(false);
            Lock(status, joined, Removal.Parse(answer.RemovalUntil));
            if (answer.Uninstall)
            {
                // Ordered from Devices: allowed for the few minutes it takes, then Windows Installer takes the client off.
                Lock(status, joined, DateTime.UtcNow.AddMinutes(10));
                status.Save();
                Removal.Uninstall(quiet: true);
            }
            return status.Save();
        }
        catch (IdentityUnreadableException e)
        {
            // Not joined again over it: that would cut off the identity the service has.
            status.State = Status.IdentityUnreadable;
            status.Problem = e.Message;
            return status.Save();
        }
        catch (HttpRequestException e)
        {
            // Another certificate key: the server vouches for it with the identity kept here, and it's taken without a visit.
            if (api.Untrusted && !vouched && await Vouch.Ask(settings.Server, stop).ConfigureAwait(false) is { } key && settings.Repin(key))
            {
                return await Round(stop, vouched: true).ConfigureAwait(false);
            }
            // It answered, but not with the certificate this computer accepts: said plainly, not as Windows words it.
            var why = !api.Untrusted ? (e.InnerException ?? e).Message
                : settings.Fingerprint is null ? "Windows doesn't trust the server's certificate."
                : "The server's certificate does not match the fingerprint stored on this computer. If the certificate was replaced on purpose, an administrator can connect this computer again: RabbidLab-Client.exe --join <address> <fingerprint>.";
            return Unreachable(status, held, why);
        }
        catch (TaskCanceledException) when (!stop.IsCancellationRequested)
        {
            return Unreachable(status, held, "The connection to the server timed out.");
        }
    }

    /// <summary>
    /// The service is stopping: the server is told, so the computer shows as Offline at once and not three minutes
    /// later. Three seconds at most, and nothing is done about an answer that doesn't come.
    /// </summary>
    public static void Bye()
    {
        try
        {
            var settings = Settings.Read();
            var me = Identity.Load();
            if (settings.Server is null || me is null || me.Server != settings.Server)
            {
                return;
            }
            using var api = new ServerApi(settings.Server, settings.Fingerprint, TimeSpan.FromSeconds(3));
            api.Post<CheckInAnswer>("api/clients/bye", new JoinRequest { Computer = Machine.Name }, me.Key, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // It shows Offline after three minutes without a check-in instead.
        }
    }

    // Whether uninstalling is locked, and why not when it isn't, written where the installer reads it.
    private static void Lock(Status status, bool joined, DateTime? until)
    {
        try
        {
            var why = Removal.Apply(joined, until);
            status.Locked = why == Unlocked.Locked;
            status.RemovalUntil = why == Unlocked.ByServer ? until : why == Unlocked.ByCode ? Removal.CodeUntil : null;
            status.Unlocked = why == Unlocked.ByCode ? Status.ByCode : why == Unlocked.Offline ? Status.ByOffline : null;
        }
        catch (Exception e) when (NotTheService(e))
        {
            // The registry stays as it was.
        }
    }


    // The lab's own icon, fetched when its hash changes: a PNG is kept as lab-icon.png; an SVG, which a Windows window
    // can't draw, or none means the window draws its own mark.
    private static async Task KeepIcon(ServerApi api, Identity me, LabLook? lab, CancellationToken stop)
    {
        try
        {
            if (Logbook.HasIcon(lab?.Icon))
            {
                return;
            }
            if (lab?.Icon is null)
            {
                System.IO.File.Delete(Logbook.IconFile); // no error when it isn't there
                Logbook.IconSaved(null);
                return;
            }
            var (code, got) = await api.Get<LabIcon>("api/clients/lab-icon", me.Key, stop).ConfigureAwait(false);
            if (code != System.Net.HttpStatusCode.OK)
            {
                return;
            }
            const string png = "data:image/png;base64,";
            if (got?.Icon is { } icon && icon.StartsWith(png, StringComparison.Ordinal))
            {
                var bytes = Convert.FromBase64String(icon.Substring(png.Length));
                if (bytes.Length <= 256 * 1024)
                {
                    System.IO.File.WriteAllBytes(Logbook.IconFile, bytes);
                }
            }
            else
            {
                System.IO.File.Delete(Logbook.IconFile);
            }
            Logbook.IconSaved(lab.Icon);
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or System.IO.IOException or UnauthorizedAccessException or FormatException or TaskCanceledException)
        {
            // Tried again at the next check-in.
        }
    }
    // The check-in was answered: when, and what the server says about the uninstall code and the days without it.
    private static void Reached(CheckInAnswer answer)
    {
        try
        {
            Removal.Reached(answer.Code, answer.UnlockAfterDays);
        }
        catch (Exception e) when (NotTheService(e))
        {
        }
    }

    private static void Leave()
    {
        try
        {
            Removal.Left();
        }
        catch (Exception e) when (NotTheService(e))
        {
        }
    }

    // --check-in by someone who isn't an administrator can't write the registry.
    private static bool NotTheService(Exception e) => e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException;

    private static Status Unreachable(Status status, bool held, string why)
    {
        status.State = Status.Unreachable;
        Logbook.Round(reached: false);
        status.Problem = why;
        // Told to the server once it's reached again, so it knows why the computer was away.
        Problems.Note("connection", $"Could not reach the server: {why}");
        try
        {
            if (held)
            {
                Removal.Unreached();
            }
        }
        catch (Exception e) when (NotTheService(e))
        {
        }
        // The server can't be asked: what it allowed before stands until its time is up.
        Lock(status, held, held ? Removal.Until : null);
        return status.Save();
    }
}
