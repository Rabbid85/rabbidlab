using System.Security.Cryptography;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>Credentials saved for sensors, such as an iDRAC's read-only user. The password is stored encrypted and never sent to the page.</summary>
public sealed class SavedLogin
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Username { get; set; }

    /// <summary>Encrypted with the data protection keys (SettingsStore).</summary>
    public string Password { get; set; } = "";
    public DateTime Added { get; set; }
    public string? AddedBy { get; set; }
}

public sealed class SavedLoginsFile
{
    public List<SavedLogin> Logins { get; set; } = [];
}

/// <summary>Credentials as typed in the dialog: for Check now, or to save with the sensor.</summary>
public sealed record LoginForm(string? Name, string? Username, string? Password);

/// <summary>Credentials ready to use, the password decrypted.</summary>
public sealed record Login(string? Username, string Password);

/// <summary>
/// The credentials sensors sign in with, kept in sensor-logins.json. One set serves every sensor that picks it, so the four
/// iDRACs share one read-only user. A set no sensor uses any more is deleted.
/// </summary>
public sealed class SensorLogins(DataFolder folder, SettingsStore secrets, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<SavedLoginsFile> file = folder.Json<SavedLoginsFile>("sensor-logins.json", Json);

    private Lock gate => file.Gate;

    /// <summary>The saved credentials for the dialog: names and usernames, never the passwords.</summary>
    public IReadOnlyList<object> List(IEnumerable<Sensor> sensors)
    {
        var used = sensors.Where(s => s.Login is not null).GroupBy(s => s.Login!).ToDictionary(g => g.Key, g => g.Count());
        lock (gate)
        {
            return [.. file.Read().Logins.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).Select(l => new { l.Id, l.Name, l.Username, used = used.GetValueOrDefault(l.Id) })];
        }
    }

    public bool Has(string? id)
    {
        lock (gate)
        {
            return id is not null && file.Read().Logins.Any(l => l.Id == id);
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
        return saved is not null && secrets.TryDecrypt(saved.Password) is { } password ? new Login(saved.Username, password) : null;
    }

    /// <summary>Why typed credentials can't be used, or null.</summary>
    public static string? Problem(LoginForm? form) =>
        form is null || string.IsNullOrWhiteSpace(form.Username) || string.IsNullOrEmpty(form.Password) ? "Type the username and password, or pick saved credentials."
        : form.Username.Length > 120 || form.Password.Length > 500 || (form.Name?.Length ?? 0) > 60 ? "That's too long: usernames are 120 characters at most, names 60."
        : null;

    /// <summary>Saves typed credentials and gives their id.</summary>
    public string Add(LoginForm form, string by)
    {
        lock (gate)
        {
            var f = file.Read();
            var login = new SavedLogin
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Name = string.IsNullOrWhiteSpace(form.Name) ? form.Username!.Trim() : form.Name.Trim(),
                Username = form.Username!.Trim(),
                Password = secrets.Encrypt(form.Password!),
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
