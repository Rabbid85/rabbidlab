using System.Security.Cryptography;

namespace RabbidLab.Updater;

/// <summary>
/// Gets an offered release's installer into the Updates folder, exactly as signed: streamed to a .part file as it
/// arrives (not held in memory first), saying how much has arrived as it goes, then checked against the offer's size and
/// SHA-256 before it takes the installer's name. An earlier download that still matches is used as it is.
/// </summary>
internal static class ReleaseDownload
{
    /// <summary>The installer, held open so nothing can change it, once it's exactly the release; throws, with the reason, when it isn't.</summary>
    public static async Task<FileStream> Get(IUpdateSource source, UpdateOffer offer, string folder, UpdateOptions options, Action<long> progress,
        CancellationToken cancel)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"RabbidLab-Setup-{offer.Version}.exe");
        if (File.Exists(path) && await Held(path, offer, cancel) is { } earlier)
        {
            progress(offer.Size);
            return earlier;
        }
        var part = path + ".part";
        await source.Fetch(offer, (body, token) => Save(body, part, offer, options, progress, token), cancel);
        if (!await Matches(part, offer, cancel))
        {
            File.Delete(part);
            throw new InvalidDataException("the downloaded file does not match the release signed with Rabbid Lab's release key");
        }
        File.Move(part, path, overwrite: true);
        return await Held(path, offer, cancel)
            ?? throw new InvalidDataException("the downloaded file changed after it was checked against the release signed with Rabbid Lab's release key");
    }

    /// <summary>The file opened so others can only read it, when it's exactly the release offered; null when it isn't.</summary>
    private static async Task<FileStream?> Held(string path, UpdateOffer offer, CancellationToken cancel)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (await Matches(stream, offer, cancel))
            {
                return stream;
            }
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
        await stream.DisposeAsync();
        return null;
    }

    /// <summary>
    /// Writes the installer's bytes as they arrive. Each piece must arrive within StallAfter and the whole within
    /// DownloadLimit, so a download that stalls ends with its reason instead of holding every later update up.
    /// </summary>
    private static async Task Save(Stream body, string part, UpdateOffer offer, UpdateOptions options, Action<long> progress, CancellationToken cancel)
    {
        await using var file = File.Create(part);
        using var whole = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        whole.CancelAfter(options.DownloadLimit);
        using var piece = CancellationTokenSource.CreateLinkedTokenSource(whole.Token);
        var buffer = new byte[81920];
        long got = 0;
        try
        {
            while (true)
            {
                piece.CancelAfter(options.StallAfter);
                var read = await body.ReadAsync(buffer, piece.Token);
                if (read == 0)
                {
                    break;
                }
                got += read;
                if (got > offer.Size)
                {
                    throw new InvalidDataException("the download is larger than the release signed with Rabbid Lab's release key");
                }
                await file.WriteAsync(buffer.AsMemory(0, read), piece.Token);
                progress(got);
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new HttpRequestException($"the download stalled after {got / 1048576.0:0.0} MB of {offer.Size / 1048576.0:0.0}");
        }
    }

    /// <summary>Whether a file is exactly the release offered: its size and SHA-256.</summary>
    private static async Task<bool> Matches(string file, UpdateOffer offer, CancellationToken cancel)
    {
        await using var stream = File.OpenRead(file);
        return await Matches(stream, offer, cancel);
    }

    private static async Task<bool> Matches(FileStream stream, UpdateOffer offer, CancellationToken cancel) =>
        stream.Length == offer.Size
        && string.Equals(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancel)), offer.Sha256, StringComparison.OrdinalIgnoreCase);
}
