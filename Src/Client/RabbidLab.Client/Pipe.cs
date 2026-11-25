using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RabbidLab.Client;

/// <summary>What the tray icon asks the service: check in now, or Ask IT for help (with the request).</summary>
[DataContract]
internal sealed class PipeAsk
{
    /// <summary>checkin or help.</summary>
    [DataMember(Name = "do")] public string Do { get; set; } = "";
    [DataMember(Name = "help", EmitDefaultValue = false)] public HelpRequest? Help { get; set; }
}

[DataContract]
internal sealed class PipeAnswer
{
    [DataMember(Name = "ok")] public bool Ok { get; set; }
    [DataMember(Name = "error", EmitDefaultValue = false)] public string? Error { get; set; }
}

/// <summary>
/// The tray icon (run as whoever is signed in) and the service (SYSTEM) talking on this computer: only the service holds
/// the computer's identity, so Check in now and Ask IT for help go through it. The pipe is open to people signed in here
/// and closed to the network. One message each way, its length first; 48 MB at most (the pictures of the screens).
/// </summary>
internal static class Pipe
{
    public const string Name = "RabbidLab.Client";
    private const int MostBytes = 48 * 1024 * 1024;

    // The service's wait for the next round, which Check in now cuts short.
    private static CancellationTokenSource? waiting;

    /// <summary>The service waits this long for its next round, or less when someone clicks Check in now.</summary>
    public static async Task Wait(TimeSpan wait, CancellationToken stop)
    {
        using var now = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Interlocked.Exchange(ref waiting, now);
        try
        {
            await Task.Delay(wait, now.Token).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            // Check in now, or stopping.
        }
        finally
        {
            Interlocked.CompareExchange(ref waiting, null, now);
        }
    }

    // --- The service's side ---------------------------------------------------------------------------------------------

    public static async Task Listen(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                var security = new PipeSecurity();
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 64 * 1024, 64 * 1024, security);
                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
                var mine = pipe;
                pipe = null;
                _ = Task.Run(() => Serve(mine, stop));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stop).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private static async Task Serve(NamedPipeServerStream pipe, CancellationToken stop)
    {
        using (pipe)
        {
            PipeAnswer answer;
            try
            {
                var ask = Read<PipeAsk>(pipe);
                // Who asked, as Windows says, not as the message says.
                string? who = null;
                try
                {
                    who = pipe.GetImpersonationUserName();
                }
                catch (IOException)
                {
                }
                answer = ask?.Do switch
                {
                    "checkin" => CheckInNow(),
                    "help" when ask.Help is not null => await Help(ask.Help, who, stop).ConfigureAwait(false),
                    _ => new PipeAnswer { Error = "Not understood." },
                };
            }
            catch (Exception e) when (e is IOException or SerializationException or InvalidDataException)
            {
                answer = new PipeAnswer { Error = "The request didn't come through. Try again." };
            }
            try
            {
                Write(pipe, answer);
            }
            catch (IOException)
            {
                // The window closed before the answer.
            }
        }
    }

    private static PipeAnswer CheckInNow()
    {
        try
        {
            waiting?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The round is under way already.
        }
        return new PipeAnswer { Ok = true };
    }

    // Ask IT for help: sent on to the server with this computer's identity, then written in what it did.
    private static async Task<PipeAnswer> Help(HelpRequest help, string? who, CancellationToken stop)
    {
        var settings = Settings.Read();
        var me = Identity.Load();
        if (settings.Server is null || me is null)
        {
            return new PipeAnswer { Error = "This computer isn't connected to a server, so the request can't be sent." };
        }
        // The account Windows says asked; the domain from the window when it's the same account.
        if (who is { Length: > 0 })
        {
            help.User = help.User is { } said && said.EndsWith("\\" + who, StringComparison.OrdinalIgnoreCase) ? said : who;
        }
        try
        {
            using var api = new ServerApi(settings.Server, settings.Fingerprint, TimeSpan.FromSeconds(90));
            var (code, error) = await api.PostSaying("api/clients/help", help, $"RabbidLab-Client {me.Id}:{me.Secret}", stop).ConfigureAwait(false);
            if (error is not null)
            {
                return new PipeAnswer { Error = code == System.Net.HttpStatusCode.Unauthorized ? "This computer isn't let into Rabbid Lab yet, so the request can't be sent." : error };
            }
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or IOException)
        {
            return new PipeAnswer { Error = "Couldn't reach the server, so the request wasn't sent. Try again in a few minutes, or contact your IT team another way." };
        }
        var n = help.Pictures.Count;
        Logbook.Add("message", "ok", "Asked IT for help", $"\"{Cut(help.Title)}\"{(n == 0 ? "" : n == 1 ? ", with a picture of the screen" : $", with pictures of {n} screens")}.");
        try
        {
            Status.Read()?.Save(); // shown in the window at once, not at the next round
        }
        catch (IOException)
        {
        }
        return new PipeAnswer { Ok = true };
    }

    private static string Cut(string text) => text.Length > 60 ? text.Substring(0, 57) + "…" : text;

    // --- The tray icon's side -------------------------------------------------------------------------------------------

    /// <summary>Asks the service and waits for its answer; an answer with an error when the service isn't there.</summary>
    public static PipeAnswer Ask(PipeAsk ask, TimeSpan wait)
    {
        var none = new PipeAnswer { Error = "The Rabbid Lab Client service isn't answering. Restart the computer, or contact your IT team." };
        try
        {
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            pipe.Connect(3000);
            // A pipe can't time out a read (setting ReadTimeout throws): the talk runs on its own, and closing the pipe ends it.
            var talk = Task.Run(() =>
            {
                Write(pipe, ask);
                return Read<PipeAnswer>(pipe);
            });
            if (!talk.Wait(wait))
            {
                return none;
            }
            return talk.Result ?? none;
        }
        catch (AggregateException e) when (e.InnerException is IOException or SerializationException or InvalidDataException or ObjectDisposedException)
        {
            return none;
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return none;
        }
    }

    // --- Messages: a length, then the JSON -------------------------------------------------------------------------------

    private static void Write<T>(Stream pipe, T message)
    {
        using var json = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(json, message);
        var length = BitConverter.GetBytes((int) json.Length);
        pipe.Write(length, 0, 4);
        json.Position = 0;
        json.CopyTo(pipe);
        pipe.Flush();
    }

    private static T? Read<T>(Stream pipe) where T : class
    {
        var length = new byte[4];
        Fill(pipe, length);
        var size = BitConverter.ToInt32(length, 0);
        if (size <= 0 || size > MostBytes)
        {
            throw new InvalidDataException("Too big.");
        }
        var body = new byte[size];
        Fill(pipe, body);
        using var json = new MemoryStream(body);
        return (T?) new DataContractJsonSerializer(typeof(T)).ReadObject(json);
    }

    private static void Fill(Stream pipe, byte[] into)
    {
        var at = 0;
        while (at < into.Length)
        {
            var n = pipe.Read(into, at, into.Length - at);
            if (n == 0)
            {
                throw new IOException("Closed.");
            }
            at += n;
        }
    }
}
