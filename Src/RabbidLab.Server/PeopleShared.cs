using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>
/// Home folders on a file share, reached as the service account: made for a new person, moved to an archive share when
/// they leave, and deleted later. Offboarding and people workflows both use it.
/// </summary>
public interface IHomeFolders
{
    /// <summary>Makes the folder and gives the person (by SID) Full control of it, as ADUC's Home folder box does. The answer is the folder made.</summary>
    Task<string> Create(string path, string sid, CancellationToken stop);

    /// <summary>Moves the folder into another folder (an archive share). The answer is where it is now.</summary>
    Task<string> Move(string path, string intoFolder, CancellationToken stop);

    /// <summary>Deletes the folder and everything in it. This cannot be undone.</summary>
    Task Delete(string path, CancellationToken stop);
}

/// <summary>An email to one person (a manager, the person who scheduled something), sent with the mail settings in Settings.</summary>
public interface IPersonMail
{
    /// <summary>Why it couldn't be sent, or null when it went.</summary>
    Task<string?> Send(string to, MailNote note, CancellationToken stop);
}

/// <summary>Through the first email notification in Settings > Notifications that can send (Channels.SendTo).</summary>
public sealed class PersonMail(Channels channels) : IPersonMail
{
    public Task<string?> Send(string to, MailNote note, CancellationToken stop) =>
        Words.Blank(to) is { } address && address.Contains('@') ? channels.SendTo(address, note, stop) : Task.FromResult<string?>("There is no email address to send to.");
}
