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

    public async Task<Status> Round(CancellationToken stop)
    {
        var settings = Settings.Read();
        var status = new Status { Server = settings.Server, ServerVersion = Status.Read()?.ServerVersion, User = Machine.Users() };
        var joined = false; // this computer has an identity on this server
        if (settings.Server is null)
        {
            status.State = Status.NoServer;
            Lock(status, joined: false, null);
            return status.Save();
        }
        using var api = new ServerApi(settings.Server, settings.Fingerprint);
        try
        {
            var me = Identity.Load();
            if (me is null || me.Server != settings.Server)
            {
                var (code, made) = await api.Post<JoinAnswer>("api/clients/join",
                    new JoinRequest { Computer = Machine.Name, Os = Machine.Os, Version = Version }, null, stop).ConfigureAwait(false);
                if (code == (HttpStatusCode) 429)
                {
                    status.State = Status.Refused;
                    status.Problem = "It's busy with other computers joining; this one tries again in a few minutes.";
                    return status.Save();
                }
                if (made is null)
                {
                    return Unreachable(status, joined, $"It answered {(int) code}.");
                }
                me = Identity.Save(made.Id, made.Secret, settings.Server);
            }
            joined = true;
            var (answered, answer) = await api.Post<CheckInAnswer>("api/clients/checkin",
                new CheckInRequest { Computer = Machine.Name, Os = Machine.Os, Version = Version, User = Machine.Users() },
                $"RabbidLab-Client {me.Id}:{me.Secret}", stop).ConfigureAwait(false);
            if (answered == HttpStatusCode.Unauthorized)
            {
                Identity.Forget();
                status.State = Status.Removed;
                Lock(status, joined: false, null);
                return status.Save();
            }
            if (answer is null)
            {
                return Unreachable(status, joined, $"It answered {(int) answered}.");
            }
            status.State = answer.Approved ? Status.Connected : Status.Waiting;
            status.ServerVersion = answer.Version;
            status.CheckedIn = DateTime.UtcNow;
            Lock(status, joined, Removal.Parse(answer.RemovalUntil));
            if (answer.Uninstall)
            {
                // Ordered from Seats: allowed for the few minutes it takes, then Windows Installer takes the client off.
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
            return Unreachable(status, joined, (e.InnerException ?? e).Message);
        }
        catch (TaskCanceledException) when (!stop.IsCancellationRequested)
        {
            return Unreachable(status, joined, "It didn't answer in time.");
        }
    }

    // What the server said about uninstalling, written where the installer reads it.
    private static void Lock(Status status, bool joined, DateTime? until)
    {
        try
        {
            status.Locked = Removal.Apply(joined, until);
            status.RemovalUntil = joined && !status.Locked ? until : null;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            // Not the service (--check-in by someone who isn't an administrator): the registry stays as it was.
        }
    }

    private static Status Unreachable(Status status, bool joined, string why)
    {
        status.State = Status.Unreachable;
        status.Problem = why;
        // The server can't be asked: what it allowed before stands until its time is up.
        Lock(status, joined, joined ? Removal.Until : null);
        return status.Save();
    }
}
