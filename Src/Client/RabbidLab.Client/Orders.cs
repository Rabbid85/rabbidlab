using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using RabbidLab.Shared;

namespace RabbidLab.Client;

/// <summary>
/// Listens for the server's account changes: one request to api/clients/next kept open, an order down it when someone
/// makes or changes a Windows account on this computer in Rabbid Lab, and how it went sent back to api/clients/done.
/// Only those account orders exist, and Group Policy can turn them off (Allow account changes).
/// </summary>
internal static class Orders
{
    public static async Task Listen(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var pause = TimeSpan.FromSeconds(15);
            try
            {
                var settings = Settings.Read();
                var me = Identity.Load();
                if (settings.Server is null || me is null || me.Server != settings.Server)
                {
                    pause = TimeSpan.FromSeconds(30); // not joined yet: the check-in round joins
                }
                else
                {
                    pause = await Serve(settings, me, stop).ConfigureAwait(false);
                }
            }
            catch (HttpRequestException)
            {
                // The server is away (updating, say): ask again shortly.
            }
            catch (TaskCanceledException) when (!stop.IsCancellationRequested)
            {
                // Held too long by something in between: ask again.
                pause = TimeSpan.FromSeconds(2);
            }
            catch (Exception) when (!stop.IsCancellationRequested)
            {
                pause = TimeSpan.FromMinutes(1);
            }
            try
            {
                await Task.Delay(pause, stop).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // Stopping.
            }
        }
    }

    /// <summary>Takes orders until the server stops giving them; how long to wait before asking again.</summary>
    private static async Task<TimeSpan> Serve(Settings settings, Identity me, CancellationToken stop)
    {
        using var api = new ServerApi(settings.Server!, settings.Fingerprint, TimeSpan.FromSeconds(80));
        var key = $"RabbidLab-Client {me.Id}:{me.Secret}";
        while (!stop.IsCancellationRequested)
        {
            // The server or its fingerprint changed (Group Policy, say): start again with the new one.
            var now = Settings.Read();
            if (now.Server != settings.Server || now.Fingerprint != settings.Fingerprint)
            {
                return TimeSpan.FromSeconds(1);
            }
            var asked = Stopwatch.StartNew();
            var (code, order) = await api.Post<AccountOrder>("api/clients/next", new Nothing(), key, stop).ConfigureAwait(false);
            if (code == HttpStatusCode.NoContent)
            {
                // The server holds the request open; an empty answer straight away isn't asked again at once.
                if (asked.Elapsed < Quick)
                {
                    await Task.Delay(Quick, stop).ConfigureAwait(false);
                }
                continue;
            }
            if (order?.Id is null || order.Action is null)
            {
                // A server older than this client has no orders to give; one that doesn't know this computer, it rejoins first.
                return code == HttpStatusCode.NotFound ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(30);
            }
            var answer = now.AccountChanges ? Do(order)
                : AccountAnswer.Failed(order.Id, $"Group Policy turns account changes off on {Machine.Name}.");
            answer.Id = order.Id;
            await Answer(api, answer, key, stop).ConfigureAwait(false);
        }
        return TimeSpan.Zero;
    }

    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(5);

    private static AccountAnswer Do(AccountOrder order)
    {
        try
        {
            return WindowsUsers.Do(order);
        }
        catch (Exception e)
        {
            // Whatever went wrong, the server hears about it.
            return AccountAnswer.Failed(order.Id, $"Windows on {Machine.Name} said: {e.Message.Trim().TrimEnd('.')}.");
        }
    }

    /// <summary>Sends how it went, up to three times: the server waits 40 seconds for it.</summary>
    private static async Task Answer(ServerApi api, AccountAnswer answer, string key, CancellationToken stop)
    {
        for (var attempt = 1; attempt <= 3 && !stop.IsCancellationRequested; attempt++)
        {
            try
            {
                var (code, _) = await api.Post<Nothing>("api/clients/done", answer, key, stop).ConfigureAwait(false);
                // Not found: the server stopped waiting for it, so sending again changes nothing.
                if ((int) code < 500)
                {
                    return;
                }
            }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !stop.IsCancellationRequested))
            {
                // Tried again below.
            }
            if (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), stop).ConfigureAwait(false);
            }
        }
    }
}

[DataContract]
internal sealed class Nothing
{
}
