using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace RabbidLab.Client;

/// <summary>The identity file is there but this account can't read it, so the client doesn't join again over it.</summary>
internal sealed class IdentityUnreadableException : Exception
{
    public IdentityUnreadableException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// The id and secret this computer got when it joined, and the server they're for. Encrypted by Windows (DPAPI) for
/// this computer, in a file only SYSTEM and administrators can open.
/// </summary>
internal sealed class Identity
{
    public string Id { get; private set; } = "";
    public string Secret { get; private set; } = "";
    public string Server { get; private set; } = "";

    private static string Path => Folder.File("identity.dat");

    // Files written since the computer-wide encryption start with this.
    private static readonly byte[] Mark = Encoding.ASCII.GetBytes("RLI2");

    /// <summary>The identity, or null when this computer has none. Throws when the file is there but can't be read.</summary>
    public static Identity? Load()
    {
        byte[] file;
        try
        {
            file = File.ReadAllBytes(Path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw Unreadable(e);
        }
        var marked = file.Length > Mark.Length && file.Take(Mark.Length).SequenceEqual(Mark);
        string[] parts;
        try
        {
            // Without the mark it's an older file: encrypted for the account that wrote it (the service's).
            var text = marked
                ? ProtectedData.Unprotect(file.Skip(Mark.Length).ToArray(), null, DataProtectionScope.LocalMachine)
                : ProtectedData.Unprotect(file, null, DataProtectionScope.CurrentUser);
            parts = Encoding.UTF8.GetString(text).Split('\n');
        }
        catch (CryptographicException e)
        {
            throw Unreadable(e);
        }
        if (parts.Length != 3)
        {
            throw Unreadable(new InvalidDataException("It doesn't hold an id, a secret and a server."));
        }
        return marked ? new Identity { Id = parts[0], Secret = parts[1], Server = parts[2] } : Save(parts[0], parts[1], parts[2]);
    }

    public static Identity Save(string id, string secret, string server)
    {
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes($"{id}\n{secret}\n{server}"), null, DataProtectionScope.LocalMachine);
        // Made with its permissions already on it: SYSTEM and Administrators only.
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        }
        var temp = Path + ".tmp";
        File.Delete(temp);
        // Trying it out in a folder of your own (RABBIDLAB_CLIENT_DATA): the folder's permissions as they are.
        using (var file = Folder.TryingOut
            ? new FileStream(temp, FileMode.CreateNew, FileAccess.Write)
            : new FileStream(temp, FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security))
        {
            file.Write(Mark, 0, Mark.Length);
            file.Write(data, 0, data.Length);
        }
        File.Delete(Path);
        File.Move(temp, Path);
        return new Identity { Id = id, Secret = secret, Server = server };
    }

    /// <summary>Forgets it, such as when the server removed this computer: the next round joins again.</summary>
    public static void Forget()
    {
        try
        {
            File.Delete(Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Tried again next round.
        }
    }

    private static IdentityUnreadableException Unreadable(Exception e) => new(
        $"{Path} can't be read by this account ({e.Message.Trim().TrimEnd('.')}). The Rabbid Lab Client service reads it as SYSTEM. To join again, an administrator deletes that file.", e);
}
