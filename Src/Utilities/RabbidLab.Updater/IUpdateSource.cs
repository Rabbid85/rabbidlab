namespace RabbidLab.Updater;

/// <summary>
/// A release of Rabbid Lab offered to this server: its version, and the size and SHA-256 of its installer, with
/// Rabbid Lab's release signature over all three (Release.cs). Url: where the source hands the installer over.
/// </summary>
public sealed record UpdateOffer(string Version, long Size, string Sha256, string Url, string? Signature);

/// <summary>
/// Where releases come from (for Rabbid Lab, the license server, which offers them to every server, licensed or not):
/// the newest one last offered, and the installer itself, handed over with whatever the source needs to see.
/// </summary>
public interface IUpdateSource
{
    /// <summary>The newest release the source last offered, or null.</summary>
    UpdateOffer? Offer { get; }

    /// <summary>When the source was last asked, whatever it answered; null when it never has been.</summary>
    DateTime? CheckedAt { get; }

    /// <summary>Why the last check didn't get an answer, or null when it did.</summary>
    string? Problem { get; }

    /// <summary>Asks the source now which release is newest.</summary>
    Task Check(CancellationToken cancel);

    /// <summary>
    /// Opens an offered release's installer and hands its bytes to read as they arrive. When the source won't hand it
    /// over, throws HttpRequestException, InvalidDataException or InvalidOperationException with the reason, which the
    /// engine says (to the administrator who chose Install now, and in the log).
    /// </summary>
    Task Fetch(UpdateOffer offer, Func<Stream, CancellationToken, Task> read, CancellationToken cancel);
}
