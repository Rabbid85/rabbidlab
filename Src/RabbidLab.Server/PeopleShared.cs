namespace RabbidLab.Server;

/// <summary>
/// Home folders on a file share, reached as the service account: made for a new person, moved to an archive share when
/// they leave, and deleted later. Offboarding and people workflows both use it.
/// </summary>
public interface IHomeFolders
{
    /// <summary>Makes the folder and gives the person Full control of it, as ADUC's Home folder box does. The answer is the folder made.</summary>
    Task<string> Create(string path, string username, CancellationToken stop);

    /// <summary>Moves the folder into another folder (an archive share). The answer is where it is now.</summary>
    Task<string> Move(string path, string intoFolder, CancellationToken stop);

    /// <summary>Deletes the folder and everything in it. This cannot be undone.</summary>
    Task Delete(string path, CancellationToken stop);
}

/// <summary>An email to one person (a manager, the person who scheduled something), sent with the mail settings in Settings.</summary>
public interface IPersonMail
{
    /// <summary>Why it couldn't be sent, or null when it went.</summary>
    Task<string?> Send(string to, string subject, string text, CancellationToken stop);
}

// Until the real ones are built: each says so, plainly.
internal sealed class NoHomeFolders : IHomeFolders
{
    private const string Not = "Home folders cannot be changed by Rabbid Lab yet.";

    public Task<string> Create(string path, string username, CancellationToken stop) => throw new ToolProblem(Not);

    public Task<string> Move(string path, string intoFolder, CancellationToken stop) => throw new ToolProblem(Not);

    public Task Delete(string path, CancellationToken stop) => throw new ToolProblem(Not);
}

internal sealed class NoPersonMail : IPersonMail
{
    public Task<string?> Send(string to, string subject, string text, CancellationToken stop) => Task.FromResult<string?>("Email to one person is not available yet.");
}
