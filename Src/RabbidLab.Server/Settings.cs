using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace RabbidLab.Server;

/// <summary>Everything Rabbid Lab's settings hold. Secrets, when there are some, are stored encrypted, never as plain text.</summary>
public sealed class ConsoleSettings
{
    /// <summary>Whether a new version installs by itself once nobody is using Rabbid Lab. Off: only when someone chooses Install now.</summary>
    public bool AutomaticUpdates { get; set; } = true;

    /// <summary>Rabbid Lab Dev: the Developer tools switch in Settings, Options, which shows the DEV buttons in Add a link and Add a sensor.</summary>
    public bool DevTools { get; set; }
}

/// <summary>Reads and writes settings.json in the data folder, and encrypts and decrypts the secrets in it.</summary>
public sealed class SettingsStore(DataFolder folder, IDataProtectionProvider protection)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly JsonFile<ConsoleSettings> file = folder.Json<ConsoleSettings>("settings.json", Json);
    private readonly IDataProtector protector = protection.CreateProtector("RabbidLab.Settings.Secrets");

    public ConsoleSettings Current => file.Read();

    /// <summary>Changes part of the settings and saves them, keeping everything else exactly as it was.</summary>
    public void Change(Action<ConsoleSettings> change)
    {
        lock (file.Gate)
        {
            var copy = JsonSerializer.Deserialize<ConsoleSettings>(JsonSerializer.Serialize(file.Read(), Json), Json) ?? new();
            change(copy);
            file.Save(copy);
        }
    }

    public string Encrypt(string plain) => protector.Protect(plain);

    /// <summary>A secret decrypted, or null when there's none or it can't be read here (the keys it was saved with are gone).</summary>
    public string? TryDecrypt(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
        {
            return null;
        }
        try
        {
            return protector.Unprotect(encrypted);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
