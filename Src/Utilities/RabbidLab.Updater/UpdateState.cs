namespace RabbidLab.Updater;

/// <summary>Where updating stands.</summary>
public enum UpdateStatus
{
    /// <summary>Nothing newer is offered (or what is offered isn't a genuine Rabbid Lab release).</summary>
    Current,

    /// <summary>A newer version is offered: it installs when someone chooses Install now, or by itself once it may.</summary>
    Available,

    /// <summary>A newer version installs once Rabbid Lab has finished starting, or unattended setup has completed.</summary>
    Waiting,

    /// <summary>The newer version is downloading (Downloaded bytes of Total so far).</summary>
    Downloading,

    /// <summary>The installer is running: it restarts Rabbid Lab as the new version.</summary>
    Installing,

    /// <summary>The newer version couldn't be downloaded or installed; the message says why and when it's tried again.</summary>
    Failed,
}

/// <summary>Where updating stands: this version, a newer one if there is one, the status and what to say about it.</summary>
public sealed record UpdateState(string Version, string? Available, UpdateStatus Status, string Message, long? Downloaded = null, long? Total = null);
