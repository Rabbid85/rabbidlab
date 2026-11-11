namespace RabbidLab.Updater;

/// <summary>An installation the engine started by itself: the installer started (Problem null), or why it couldn't.</summary>
public sealed record UpdateAttempt(string Version, string? Problem);

/// <summary>
/// The program around the engine (the console): the version running, where the engine keeps its files, whether it can
/// install itself (as a Windows service; not in Docker), whether a newer version may install by itself, and what it hears
/// about the installations the engine starts by itself.
/// </summary>
public interface IUpdateHost
{
    /// <summary>The product's name, which a release signature must cover: Rabbid Lab, or Rabbid Lab Dev.</summary>
    string ProductName { get; }

    /// <summary>The version running now. The engine never installs this version or an older one.</summary>
    string Version { get; }

    /// <summary>The data folder: the engine keeps Updates\ (downloads, Setup's logs, last-try.txt) and version.txt in it.</summary>
    string Folder { get; }

    /// <summary>Whether it can install a new version itself: as a Windows service, yes; in Docker, no.</summary>
    bool CanInstall { get; }

    /// <summary>Whether a newer version installs by itself once nobody is using the console. Not: only when chosen.</summary>
    bool Automatic { get; }

    /// <summary>An installation the engine started by itself (Install now reports through its own callback instead).</summary>
    void Record(UpdateAttempt attempt);
}
