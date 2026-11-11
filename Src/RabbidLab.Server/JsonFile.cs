using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>One JSON file in the data folder: kept in memory, saved atomically with a .bak, and read from the .bak when it's missing or broken.</summary>
public sealed class JsonFile<T>(string path, JsonSerializerOptions options, ILogger? log = null, bool watch = false) where T : class, new()
{
    private T? value;
    private DateTime stamp; // the file's write time when it was read or saved
    private long looked; // when the write time was last checked
    private bool good; // the file itself was read or written, so it's worth keeping as .bak

    public Lock Gate { get; } = new();

    /// <summary>Whether the file or its backup copy is on disk, readable or not.</summary>
    public bool Exists => File.Exists(path) || File.Exists(path + ".bak");

    /// <summary>Whether the file was there but neither it nor its backup copy could be read, so what Read gave is empty.</summary>
    public bool Broken { get; private set; }

    /// <summary>What the file holds. With watch, it's read again when something else wrote it, checked once a second at most.</summary>
    public T Read()
    {
        lock (Gate)
        {
            if (value is not null && (!watch || Environment.TickCount64 - looked < 1000))
            {
                return value;
            }
            looked = Environment.TickCount64;
            var written = File.GetLastWriteTimeUtc(path);
            if (value is null || written != stamp)
            {
                value = Load();
                stamp = written;
            }
            return value;
        }
    }

    public void Save(T next)
    {
        lock (Gate)
        {
            var temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, next, options);
                stream.Flush(flushToDisk: true);
            }
            if (Broken && File.Exists(path))
            {
                File.Copy(path, path + ".broken", overwrite: true); // kept for whoever wants to mend it
            }
            if (good && File.Exists(path))
            {
                try
                {
                    File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    File.Move(temp, path, overwrite: true); // no backup this time
                }
            }
            else
            {
                File.Move(temp, path, overwrite: true);
            }
            value = next;
            good = true;
            Broken = false;
            stamp = File.GetLastWriteTimeUtc(path);
        }
    }

    private T Load()
    {
        var name = Path.GetFileName(path);
        good = TryRead(path, out var main, out var why);
        Broken = false;
        if (good)
        {
            return main!;
        }
        if (TryRead(path + ".bak", out var kept, out var whyNot))
        {
            log?.LogWarning("{File} {Why}, so its backup copy ({Backup}) is used", name, why ?? "is missing", name + ".bak");
            return kept!;
        }
        Broken = (why ?? whyNot) is not null;
        if (Broken)
        {
            log?.LogError("{File} {Why} and has no usable backup copy, so nothing was read from it", name, why ?? "is missing");
        }
        return new T();
    }

    /// <summary>The file's contents, or why not (null when it just isn't there).</summary>
    private bool TryRead(string file, out T? read, out string? why)
    {
        read = null;
        why = null;
        if (!File.Exists(file))
        {
            return false;
        }
        try
        {
            var text = File.ReadAllText(file);
            read = string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<T>(text, options);
            why = read is null ? "is empty" : null;
        }
        catch (JsonException)
        {
            why = "can't be read";
        }
        return read is not null;
    }
}
