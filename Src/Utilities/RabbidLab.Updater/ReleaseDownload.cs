using System.Security.Cryptography;

namespace RabbidLab.Updater;

/// <summary>
/// Gets an offered release's installer into the Updates folder, exactly as signed: streamed to a .part file as it
/// arrives (not held in memory first), saying how much has arrived as it goes, then checked against the offer's size and
/// SHA-256 before it takes the installer's name. An earlier download that still matches is used as it is. It comes in
/// pieces over several connections at once when the source can (Pieces), else in one go.
/// </summary>
internal static class ReleaseDownload
{
    /// <summary>
    /// 2 MB pieces, six at a time. GitHub's file servers differ a lot from one connection to the next (the same file came
    /// at 135 KB/s on one and 14 MB/s on another), so one slow connection holds up a piece, not the whole download.
    /// </summary>
    internal const int PieceSize = 2 * 1024 * 1024;
    internal const int Connections = 6;

    /// <summary>A piece that fails this many times ends the download.</summary>
    private const int Tries = 4;
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
        if (!await Pieces(source, offer, part, options, progress, cancel))
        {
            await source.Fetch(offer, (body, token) => Save(body, part, offer, options, progress, token), cancel);
        }
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

    private sealed class Piece(long from, long to)
    {
        public long From { get; } = from;
        public long Length { get; } = to - from + 1;
        public long To => From + Length - 1;
        public long Got { get; set; } // the most any one try of it has written
        public int Running { get; set; }
        public int Failed { get; set; }
        public bool Done { get; set; }
        public DateTime Started { get; set; }
        public CancellationTokenSource? Stop { get; set; } // ends the tries still going once one has finished
    }

    /// <summary>
    /// The installer in pieces, several connections at once, each written where it belongs in the .part file. Once no
    /// piece is left to start, one that has been coming for RaceAfter is asked for again on a free connection, and
    /// whichever try finishes first counts (both write the same bytes). A piece that fails is tried again, Tries times at
    /// most. False when the source hands over only the whole file. The whole is still checked against the signed size and
    /// SHA-256 afterwards.
    /// </summary>
    private static async Task<bool> Pieces(IUpdateSource source, UpdateOffer offer, string part, UpdateOptions options, Action<long> progress, CancellationToken cancel)
    {
        if (offer.Size <= 0)
        {
            return false;
        }
        var count = (int) ((offer.Size + PieceSize - 1) / PieceSize);
        var pieces = Enumerable.Range(0, count).Select(i => new Piece((long) i * PieceSize, Math.Min(offer.Size, (long) (i + 1) * PieceSize) - 1)).ToList();
        using var whole = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        whole.CancelAfter(options.DownloadLimit);
        var gate = new Lock();
        var wholeOnly = false; // the source ignored a range
        Exception? failure = null;

        using (var handle = File.OpenHandle(part, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.Asynchronous, offer.Size))
        {
            async Task Connection()
            {
                var buffer = new byte[81920];
                while (true)
                {
                    Piece? p;
                    CancellationTokenSource stop;
                    lock (gate)
                    {
                        if (whole.IsCancellationRequested || failure is not null || wholeOnly || pieces.All(x => x.Done))
                        {
                            return;
                        }
                        var now = DateTime.UtcNow;
                        p = pieces.FirstOrDefault(x => !x.Done && x.Running == 0)
                            ?? pieces.Where(x => !x.Done && x.Running == 1 && now - x.Started >= options.RaceAfter).OrderBy(x => x.Started).FirstOrDefault();
                        if (p is not null)
                        {
                            if (p.Running == 0)
                            {
                                p.Stop?.Dispose();
                                p.Stop = CancellationTokenSource.CreateLinkedTokenSource(whole.Token);
                                p.Started = now;
                            }
                            p.Running++;
                        }
                        stop = p?.Stop ?? whole;
                    }
                    if (p is null)
                    {
                        try
                        {
                            await Task.Delay(200, whole.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        continue;
                    }
                    long mine = 0;
                    var finished = false;
                    Exception? why = null;
                    try
                    {
                        using var quiet = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                        var ranged = await source.FetchPiece(offer, p.From, p.To, async (body, token) =>
                        {
                            while (true)
                            {
                                quiet.CancelAfter(options.StallAfter);
                                var read = await body.ReadAsync(buffer, quiet.Token);
                                if (read == 0)
                                {
                                    break;
                                }
                                if (mine + read > p.Length)
                                {
                                    throw new InvalidDataException("the download is larger than the release signed with Rabbid Lab's release key");
                                }
                                await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), p.From + mine, quiet.Token);
                                mine += read;
                                lock (gate)
                                {
                                    if (mine > p.Got)
                                    {
                                        p.Got = mine;
                                        progress(pieces.Sum(x => x.Got));
                                    }
                                }
                            }
                        }, quiet.Token);
                        if (!ranged)
                        {
                            lock (gate)
                            {
                                wholeOnly = true;
                            }
                        }
                        else if (mine != p.Length)
                        {
                            throw new HttpRequestException($"a download segment ended after {mine:N0} of {p.Length:N0} bytes");
                        }
                        else
                        {
                            finished = true;
                        }
                    }
                    catch (Exception e) when (e is not OutOfMemoryException)
                    {
                        why = e;
                    }
                    lock (gate)
                    {
                        p.Running--;
                        if (finished && !p.Done)
                        {
                            p.Done = true;
                            p.Stop?.Cancel(); // the other try of it stops
                        }
                        // A try that failed by itself (not stopped because the other finished, or the download ending): again, a few times.
                        else if (why is not null && !p.Done && !whole.IsCancellationRequested && !wholeOnly && ++p.Failed >= Tries)
                        {
                            failure ??= why;
                        }
                    }
                }
            }
            await Task.WhenAll(Enumerable.Range(0, Math.Min(Connections, count)).Select(_ => Task.Run(Connection, CancellationToken.None)));
        }
        foreach (var p in pieces)
        {
            p.Stop?.Dispose();
        }
        cancel.ThrowIfCancellationRequested();
        if (wholeOnly)
        {
            return false;
        }
        if (failure is not null)
        {
            throw failure is HttpRequestException or InvalidDataException ? failure : new HttpRequestException($"the download failed: {failure.Message.TrimEnd('.')}", failure);
        }
        if (!pieces.All(p => p.Done))
        {
            var got = pieces.Sum(p => p.Got);
            throw new HttpRequestException($"the download did not finish within {options.DownloadLimit.TotalMinutes:0} minutes: {got / 1048576.0:0.0} MB of {offer.Size / 1048576.0:0.0}");
        }
        return true;
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
