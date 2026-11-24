using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace RabbidLab.Server;

/// <summary>Everything Rabbid Lab's settings hold. Secrets, when there are some, are stored encrypted, never as plain text.</summary>
public sealed class ConsoleSettings
{
    /// <summary>Whether a new version installs by itself once nobody is using Rabbid Lab. Off: only when someone chooses Install now.</summary>
    public bool AutomaticUpdates { get; set; } = true;

    /// <summary>Rabbid Lab Dev: the Developer tools switch in Settings, Developer, which shows the DEV buttons in Add a link and Add a sensor.</summary>
    public bool DevTools { get; set; }

    /// <summary>Rabbid Lab Dev only: the MFA step shows Skip MFA this time, on any computer (SignIn.MaySkipMfa).</summary>
    public bool DevSkipMfa { get; set; } = true;

    /// <summary>The lab's own name, icon (a PNG or SVG data URL) and accent colour, for everyone. Null: Rabbid Lab's own. A new
    /// install starts in purple; a settings file from before keeps what it has, null included, as its null is written out.</summary>
    public string? LabName { get; set; }
    public string? LabIcon { get; set; }
    public string? Accent { get; set; } = "#5900FF";

    /// <summary>The line under the name, with tags such as {username} filled in by the page. Null: "Lab console".</summary>
    public string? LabTagline { get; set; }

    /// <summary>What shows behind the sign-in card (Brand.Backgrounds). Null: squares.</summary>
    public string? SignInBackground { get; set; }

    /// <summary>The sign-in card's look (Brand.Cards, Brand.Styles, Brand.Sizes) and its glass opacity (20 to 95). Null: whole card, smoked glass, 65, a little larger.</summary>
    public string? SignInCard { get; set; }
    public string? SignInStyle { get; set; }
    public int? SignInGlass { get; set; }
    public string? SignInSize { get; set; }

    /// <summary>The quote on the sign-in card: when it changes (Brand.QuoteModes), which kind (Brand.QuoteKinds), and the lab's own. Null: a random motivational one each visit.</summary>
    public string? SignInQuote { get; set; }
    public string? SignInQuoteKind { get; set; }
    public List<SignInQuote>? SignInQuotes { get; set; }

    /// <summary>The lab's look for people who pick System in Settings, Appearance (theme, layout, style, toasts), as the console sends it. Null: Rabbid Lab's own.</summary>
    public string? LabLook { get; set; }

    /// <summary>Sign out when idle: minutes without a click, key press or scroll before a session ends (SignIn.IdleOut). 0 is never.</summary>
    public int IdleMinutes { get; set; } = 30;

    /// <summary>How many wrong passwords lock a name (SignInLocks): 3, 5 or 10.</summary>
    public int LockAfter { get; set; } = 10;

    /// <summary>How many days the AD audit keeps the changes read from Security logs (AdAudit.cs): 30, 90 or 365.</summary>
    public int AuditDays { get; set; } = 90;
}

/// <summary>Reads and writes settings.json in the data folder, and encrypts and decrypts the secrets in it.</summary>
public sealed class SettingsStore(DataFolder folder, IDataProtectionProvider protection)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly JsonFile<ConsoleSettings> file = folder.Json<ConsoleSettings>("settings.json", Json);
    private readonly IDataProtector protector = protection.CreateProtector("RabbidLab.Settings.Secrets");

    public ConsoleSettings Current => Seen(file.Read());

    // Every read keeps the lab's name where messages can get it.
    private static ConsoleSettings Seen(ConsoleSettings s)
    {
        Brand.Named = s.LabName;
        return s;
    }

    /// <summary>Changes part of the settings and saves them, keeping everything else exactly as it was.</summary>
    public void Change(Action<ConsoleSettings> change)
    {
        lock (file.Gate)
        {
            var copy = JsonSerializer.Deserialize<ConsoleSettings>(JsonSerializer.Serialize(file.Read(), Json), Json) ?? new();
            change(copy);
            file.Save(copy);
            Seen(copy);
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
