using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace RabbidLab.Client;

/// <summary>
/// How the last round went, written by the service to status.json for the tray icon to show. Anyone on the computer may
/// read it; it holds nothing secret.
/// </summary>
[DataContract]
internal sealed class Status
{
    public const string Starting = "starting";
    public const string NoServer = "no-server";
    public const string Refused = "refused";
    public const string Waiting = "waiting";
    public const string Connected = "connected";
    public const string Unreachable = "unreachable";
    public const string Removed = "removed";
    public const string IdentityUnreadable = "identity-unreadable";

    [DataMember(Name = "state")] public string State { get; set; } = Starting;
    [DataMember(Name = "server")] public string? Server { get; set; }
    [DataMember(Name = "computer")] public string Computer { get; set; } = Environment.MachineName;
    [DataMember(Name = "version")] public string Version { get; set; } = Agent.Version;
    [DataMember(Name = "serverVersion")] public string? ServerVersion { get; set; }
    [DataMember(Name = "checkedIn")] public DateTime? CheckedIn { get; set; }
    [DataMember(Name = "problem")] public string? Problem { get; set; }

    /// <summary>Who is signed in to Windows, as the server is told.</summary>
    [DataMember(Name = "user")] public string? User { get; set; }

    /// <summary>Whether uninstalling is locked until the server allows it, and until when it's allowed when it is (Removal).</summary>
    [DataMember(Name = "locked")] public bool Locked { get; set; }
    [DataMember(Name = "removalUntil")] public DateTime? RemovalUntil { get; set; }

    private static string Path => Folder.File("status.json");

    /// <summary>A few words for the tray icon and the console.</summary>
    public string Describe() => State switch
    {
        Connected => $"Connected to {Host}",
        Waiting => $"Waiting for an admin at {Host} to let this computer in",
        NoServer => "Not connected to a server",
        Refused => $"{Host} is busy, trying again shortly",
        Removed => $"{Host} removed this computer, joining again",
        Unreachable => $"Can't reach {Host}",
        IdentityUnreadable => "This computer's identity can't be read",
        _ => "Starting",
    };

    /// <summary>What to do about it, or what it means, in a sentence for the window.</summary>
    public string Hint() => State switch
    {
        Connected => "This computer shows under Seats in Rabbid Lab.",
        Waiting => "It shows under Seats as Waiting until someone lets it in.",
        NoServer => "Open Rabbid Lab in a browser on this computer and click Connect this computer, or click Connect to a server below.",
        Starting => "The Rabbid Lab Client service hasn't reported yet.",
        _ => Problem ?? "",
    };

    public string Host => Uri.TryCreate(Server, UriKind.Absolute, out var u) ? u.Host : Server ?? "the server";

    public Status Save()
    {
        var temp = Path + ".tmp";
        using (var file = File.Create(temp))
        {
            new DataContractJsonSerializer(typeof(Status)).WriteObject(file, this);
        }
        // The tray icon may be reading it this instant: a few tries, then the next round writes it.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(Path))
                {
                    File.Replace(temp, Path, null);
                }
                else
                {
                    File.Move(temp, Path);
                }
                return this;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
            catch (IOException)
            {
                return this;
            }
        }
    }

    public static Status? Read()
    {
        try
        {
            // Shared for writing and deleting, so the service can replace it while this reads.
            using var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return (Status?) new DataContractJsonSerializer(typeof(Status)).ReadObject(file);
        }
        catch (Exception e) when (e is IOException or SerializationException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
