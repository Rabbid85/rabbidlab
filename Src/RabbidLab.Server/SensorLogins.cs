using System.Security.Cryptography;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>Credentials saved for sensors, such as an iDRAC's read-only user. Secrets are stored encrypted and never sent to the page.</summary>
public sealed class SavedLogin
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>user (a username and password), key (an API key), community (SNMP v2c) or snmpv3; null in older files, read from the username.</summary>
    public string? Kind { get; set; }
    public string? Username { get; set; }

    /// <summary>Encrypted with the data protection keys (SettingsStore): the password, API key, community, or SNMPv3 auth password.</summary>
    public string Password { get; set; } = "";

    /// <summary>SNMPv3: SHA-256 or SHA-1, AES-128 or None, and the privacy password, encrypted.</summary>
    public string? Auth { get; set; }
    public string? Privacy { get; set; }
    public string? PrivacyPassword { get; set; }
    public DateTime Added { get; set; }
    public string? AddedBy { get; set; }

    public string KindNow => Kind ?? (Username is null ? "key" : "user");
}

public sealed class SavedLoginsFile
{
    public List<SavedLogin> Logins { get; set; } = [];
}

/// <summary>Credentials as typed in the dialog: for Check now, or to save with the sensor.</summary>
public sealed record LoginForm(string? Name, string? Username, string? Password, string? Kind = null, string? Auth = null, string? Privacy = null, string? PrivacyPassword = null);

/// <summary>Credentials ready to use, the secrets decrypted.</summary>
public sealed record Login(string? Username, string Password, string Kind = "user", string? Auth = null, string? Privacy = null, string? PrivacyPassword = null);

/// <summary>
/// The credentials sensors sign in with, kept in sensor-logins.json. One set serves every sensor that picks it, so the four
/// iDRACs share one read-only user. A set no sensor uses any more is deleted.
/// </summary>
public sealed class SensorLogins(DataFolder folder, SettingsStore secrets, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] Kinds = ["user", "key", "community", "snmpv3"];
    private readonly JsonFile<SavedLoginsFile> file = folder.Json<SavedLoginsFile>("sensor-logins.json", Json);

    private Lock gate => file.Gate;

    /// <summary>The saved credentials for the dialog: names, kinds and usernames, never the secrets.</summary>
    public IReadOnlyList<object> List(IEnumerable<Sensor> sensors)
    {
        var used = sensors.Where(s => s.Login is not null).GroupBy(s => s.Login!).ToDictionary(g => g.Key, g => g.Count());
        lock (gate)
        {
            return [.. file.Read().Logins.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).Select(l => new { l.Id, l.Name, kind = l.KindNow, l.Username, used = used.GetValueOrDefault(l.Id) })];
        }
    }

    /// <summary>The kind of a saved set, or null when it isn't there.</summary>
    public string? KindOf(string? id)
    {
        lock (gate)
        {
            return file.Read().Logins.FirstOrDefault(l => l.Id == id)?.KindNow;
        }
    }

    /// <summary>A saved set, decrypted; null when it's gone or the keys it was saved with are.</summary>
    public Login? Get(string? id)
    {
        SavedLogin? saved;
        lock (gate)
        {
            saved = file.Read().Logins.FirstOrDefault(l => l.Id == id);
        }
        return saved is not null && secrets.TryDecrypt(saved.Password) is { } password
            ? new Login(saved.Username, password, saved.KindNow, saved.Auth, saved.Privacy, secrets.TryDecrypt(saved.PrivacyPassword))
            : null;
    }

    /// <summary>Why typed credentials can't be used as the kind wanted, or null.</summary>
    public static string? Problem(LoginForm? form, string kind)
    {
        if (form is null || string.IsNullOrEmpty(form.Password) || (kind is "user" or "snmpv3" && string.IsNullOrWhiteSpace(form.Username)))
        {
            return kind switch
            {
                "key" => "Paste the API key, or pick a saved one.",
                "community" => "Type the community, or pick a saved one.",
                "snmpv3" => "Type the SNMPv3 username and auth password, or pick a saved user.",
                _ => "Type the username and password, or pick saved credentials.",
            };
        }
        if ((form.Username?.Length ?? 0) > 120 || form.Password.Length > 500 || (form.PrivacyPassword?.Length ?? 0) > 500 || (form.Name?.Length ?? 0) > 60)
        {
            return "That's too long: usernames are 120 characters at most, names 60.";
        }
        if (kind == "snmpv3" && (form.Password.Length < 8 || (form.Privacy != "None" && (form.PrivacyPassword?.Length ?? 0) < 8)))
        {
            return "SNMPv3 passwords are 8 characters at least, the privacy one too.";
        }
        return null;
    }

    /// <summary>Saves typed credentials of a kind and gives their id.</summary>
    public string Add(LoginForm form, string kind, string by)
    {
        lock (gate)
        {
            var f = file.Read();
            var v3 = kind == "snmpv3";
            var login = new SavedLogin
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Kind = Kinds.Contains(kind) ? kind : "user",
                Name = !string.IsNullOrWhiteSpace(form.Name) ? form.Name.Trim()
                    : string.IsNullOrWhiteSpace(form.Username) ? kind == "community" ? "SNMP community" : "API key" : form.Username.Trim(),
                Username = kind is "key" or "community" || string.IsNullOrWhiteSpace(form.Username) ? null : form.Username.Trim(),
                Password = secrets.Encrypt(form.Password!),
                Auth = v3 ? (form.Auth == "SHA-1" ? "SHA-1" : "SHA-256") : null,
                Privacy = v3 ? (form.Privacy == "None" ? "None" : "AES-128") : null,
                PrivacyPassword = v3 && form.Privacy != "None" && !string.IsNullOrEmpty(form.PrivacyPassword) ? secrets.Encrypt(form.PrivacyPassword) : null,
                Added = clock.GetUtcNow().UtcDateTime,
                AddedBy = by,
            };
            f.Logins.Add(login);
            file.Save(f);
            return login.Id;
        }
    }

    /// <summary>Deletes the sets no sensor uses.</summary>
    public void Prune(IEnumerable<Sensor> sensors)
    {
        var used = sensors.Select(s => s.Login).OfType<string>().ToHashSet();
        lock (gate)
        {
            var f = file.Read();
            if (f.Logins.RemoveAll(l => !used.Contains(l.Id)) > 0)
            {
                file.Save(f);
            }
        }
    }
}
