using System.Security.Cryptography;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>One of Rabbid Lab's own accounts. The password is kept as a salted PBKDF2 hash, never as itself.</summary>
public sealed class LocalAccount
{
    public string Username { get; set; } = "";
    public string Hash { get; set; } = "";

    /// <summary>What it was made as, admin or user, read when roles.json has to be made. Accounts from before roles were admins.</summary>
    public string Role { get; set; } = Roles.Admin;

    /// <summary>Changes with the password, which signs out every session of the account.</summary>
    public string Stamp { get; set; } = "";
    public DateTime Created { get; set; }
}

/// <summary>Active Directory sign-in: the domain, its domain controller, and the group whose members may sign in.</summary>
public sealed class DirectorySignIn
{
    public string Domain { get; set; } = "";

    /// <summary>The domain controller to use, or null for Automatic: the quickest that answers at each sign-in.</summary>
    public string? Server { get; set; }

    /// <summary>Before roles.json, whose members could sign in (empty: anyone in the domain). Read once, to make the roles.</summary>
    public string Group { get; set; } = "";

    /// <summary>Before roles.json, whose members were admins (empty: everyone who signed in). Read once, to make the roles.</summary>
    public string AdminGroup { get; set; } = "";
    public string Stamp { get; set; } = "";
}

public sealed class AccountsFile
{
    public List<LocalAccount> Local { get; set; } = [];
    public DirectorySignIn? Directory { get; set; }

    /// <summary>Roles have been kept for this Rabbid Lab, so a lost roles.json is never rebuilt the way one from before roles is.</summary>
    public bool HasRoles { get; set; }
}

/// <summary>
/// Who may sign in, kept in accounts.json in the data folder: Rabbid Lab's own accounts, and Active Directory once it's
/// set up. Until one of them is, Rabbid Lab isn't set up and signin.html sets it up.
/// </summary>
public sealed class Accounts(DataFolder folder)
{
    private const int Iterations = 210_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    // Watched: --add-admin writes it while the service runs.
    private readonly JsonFile<AccountsFile> file = folder.Json<AccountsFile>("accounts.json", Json, watch: true);

    public bool SetUp
    {
        get
        {
            lock (file.Gate)
            {
                // An unreadable accounts.json is no reason to offer setup to whoever asks first.
                return IsSetUp(file.Read()) || file.Broken;
            }
        }
    }

    public DirectorySignIn? Directory
    {
        get
        {
            lock (file.Gate)
            {
                return file.Read().Directory;
            }
        }
    }

    /// <summary>Whether roles have been kept here before (see AccountsFile.HasRoles).</summary>
    public bool HasRoles
    {
        get
        {
            lock (file.Gate)
            {
                return file.Read().HasRoles;
            }
        }
    }

    /// <summary>Notes that roles are kept now.</summary>
    public void NoteRoles()
    {
        lock (file.Gate)
        {
            var a = file.Read();
            // Never written over an accounts.json that couldn't be read: that would open setup again.
            if (!a.HasRoles && !file.Broken)
            {
                a.HasRoles = true;
                file.Save(a);
            }
        }
    }

    public LocalAccount? Find(string? username)
    {
        lock (file.Gate)
        {
            return username is null ? null : Named(file.Read(), username.Trim());
        }
    }

    /// <summary>Why a new username or password won't do, or null when they're fine.</summary>
    public static string? Problem(string? username, string? password) =>
        string.IsNullOrWhiteSpace(username) ? "Enter a username."
        : username.Trim().Length > 64 || !username.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
            ? "Use only letters, numbers, dots, dashes and underscores in the username."
        : string.IsNullOrEmpty(password) || password.Length < 8 ? "Use a password of at least 8 characters."
        : null;

    /// <summary>The first account, while Rabbid Lab isn't set up. Null and the reason when it can't be made.</summary>
    public (LocalAccount? Account, string? Problem) SetUpLocal(string? username, string? password)
    {
        lock (file.Gate)
        {
            var a = file.Read();
            if (Taken(a) is { } taken)
            {
                return (null, taken);
            }
            if (Problem(username, password) is { } problem)
            {
                return (null, problem);
            }
            var account = New(username!.Trim(), password!);
            a.Local.Add(account);
            a.HasRoles = true;
            file.Save(a);
            return (account, null);
        }
    }

    /// <summary>Active Directory as the way to sign in, while Rabbid Lab isn't set up. The reason when it can't be.</summary>
    public string? SetUpDirectory(string domain, string? server)
    {
        lock (file.Gate)
        {
            var a = file.Read();
            if (Taken(a) is { } taken)
            {
                return taken;
            }
            a.Directory = new DirectorySignIn { Domain = domain, Server = server, Stamp = NewStamp() };
            a.HasRoles = true;
            file.Save(a);
            return null;
        }
    }

    /// <summary>An administrator with this password: a new account, or a new password for one that's there (--add-admin).</summary>
    public void AddAdmin(string username, string password) => Add(username, password, Roles.Admin);

    /// <summary>A local account with this password and role: new, or replacing the one with that name and ending its sessions.</summary>
    public void Add(string username, string password, string role)
    {
        lock (file.Gate)
        {
            var a = file.Read();
            a.Local.RemoveAll(x => Same(x.Username, username));
            var account = New(username, password);
            account.Role = role;
            a.Local.Add(account);
            file.Save(a);
        }
    }

    /// <summary>The local accounts, without their password hashes.</summary>
    public IReadOnlyList<(string Username, string Role, DateTime Created)> List()
    {
        lock (file.Gate)
        {
            return [.. file.Read().Local.OrderBy(x => x.Username, StringComparer.OrdinalIgnoreCase).Select(x => (x.Username, x.Role, x.Created))];
        }
    }

    /// <summary>A new local account, in no role but User. Why it can't be made, or null.</summary>
    public string? Create(string? username, string? password)
    {
        lock (file.Gate)
        {
            var a = file.Read();
            if (Problem(username, password) is { } problem)
            {
                return problem;
            }
            if (Named(a, username!.Trim()) is not null)
            {
                return $"There's already an account called {username!.Trim()}.";
            }
            var account = New(username!.Trim(), password!);
            account.Role = Roles.User;
            a.Local.Add(account);
            file.Save(a);
            return null;
        }
    }

    /// <summary>A new password for a local account; its sessions end. Why not, or null.</summary>
    public string? SetPassword(string username, string? password) =>
        password is null || password.Length < 8 ? "Use a password of at least 8 characters." : Change(username, x =>
        {
            x.Hash = Hash(password);
            x.Stamp = NewStamp();
        });

    /// <summary>Removes a local account; its sessions end. Why not, or null. Roles.Forget says whether it may go.</summary>
    public string? Remove(string username) => Change(username, null);

    /// <summary>Changes one account, or removes it when there's no change. Why not, or null.</summary>
    private string? Change(string username, Action<LocalAccount>? change)
    {
        lock (file.Gate)
        {
            var a = file.Read();
            var account = Named(a, username);
            if (account is null)
            {
                return $"There's no account called {username}.";
            }
            if (change is null)
            {
                a.Local.Remove(account);
            }
            else
            {
                change(account);
            }
            file.Save(a);
            return null;
        }
    }

    /// <summary>The account when the password is its own, otherwise null.</summary>
    public LocalAccount? Check(string? username, string? password)
    {
        var account = Find(username);
        // An unknown username takes as long as a wrong password, so timing doesn't tell which usernames exist.
        var ok = Verify(password ?? "", account?.Hash ?? DummyHash);
        return account is not null && ok ? account : null;
    }

    private static bool IsSetUp(AccountsFile a) => a.Local.Count > 0 || a.Directory is not null;

    /// <summary>Why setup isn't open (it's done, or accounts.json can't be read), or null when it is.</summary>
    private string? Taken(AccountsFile a) =>
        IsSetUp(a) ? Product.Title + " is already set up. Sign in instead."
        : file.Broken ? $"accounts.json can't be read, so {Product.Title} can't be set up again from here. Restore the file from a backup, or add an administrator with --add-admin."
        : null;

    private static LocalAccount? Named(AccountsFile a, string username) => a.Local.FirstOrDefault(x => Same(x.Username, username));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static LocalAccount New(string username, string password) =>
        new() { Username = username, Hash = Hash(password), Stamp = NewStamp(), Created = DateTime.UtcNow };

    private static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static readonly string DummyHash = Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)));

    private static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool Verify(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[2]), iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
