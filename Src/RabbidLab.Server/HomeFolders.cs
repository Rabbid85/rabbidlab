using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RabbidLab.Server;

/// <summary>The service account's sign-in, for work on other servers (shares, print servers) through NetworkLogon.</summary>
public interface IServiceLogin
{
    /// <summary>DOMAIN\account and its password, or null when the service account isn't set up.</summary>
    (string Username, string Password)? Login();
}

public sealed partial class DirectoryTools : IServiceLogin
{
    public (string Username, string Password)? Login() =>
        Load() is { Account: { } account, Domain: { } domain, Scope: not null } f && secrets.TryDecrypt(f.Password) is { } password ? ($"{domain}\\{account}", password) : null;
}

/// <summary>
/// Home folders on file shares, as the service account (NetworkLogon), so the share's own permissions decide. Only UNC
/// paths with a folder below the share are touched, never a share itself.
/// </summary>
public sealed class HomeFolders(IServiceLogin service) : IHomeFolders
{
    public Task<string> Create(string path, string sid, CancellationToken stop) => As(path, "create", () =>
    {
        var dir = Directory.CreateDirectory(path);
        var acl = dir.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        dir.SetAccessControl(acl);
        return dir.FullName;
    });

    public Task<string> Move(string path, string intoFolder, CancellationToken stop)
    {
        Check(intoFolder, share: true);
        return As(path, "move", () =>
        {
            if (!Directory.Exists(path))
            {
                throw new ToolProblem($"The folder {path} does not exist.");
            }
            Directory.CreateDirectory(intoFolder);
            var name = Path.GetFileName(path.TrimEnd('\\'));
            var to = Path.Combine(intoFolder, name);
            for (var n = 2; Directory.Exists(to) || File.Exists(to); n++)
            {
                to = Path.Combine(intoFolder, $"{name} ({n})");
            }
            // One share: a rename. Another share or server: a copy, then the original goes.
            if (string.Equals(Share(path), Share(to), StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(path, to);
            }
            else
            {
                Copy(new DirectoryInfo(path), to, stop);
                Directory.Delete(path, true);
            }
            return to;
        });
    }

    public Task Delete(string path, CancellationToken stop) => As(path, "delete", () =>
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
        return true;
    });

    private static void Copy(DirectoryInfo from, string to, CancellationToken stop)
    {
        var made = Directory.CreateDirectory(to);
        made.SetAccessControl(from.GetAccessControl());
        foreach (var f in from.EnumerateFiles())
        {
            stop.ThrowIfCancellationRequested();
            f.CopyTo(Path.Combine(to, f.Name));
        }
        foreach (var d in from.EnumerateDirectories())
        {
            Copy(d, Path.Combine(to, d.Name), stop);
        }
    }

    // \\server\share, the part of a UNC path a rename can't leave.
    private static string Share(string path) => string.Join('\\', path.TrimStart('\\').Split('\\').Take(2));

    /// <summary>A UNC path with a folder below the share (or the share itself, for where things go).</summary>
    private static void Check(string path, bool share = false)
    {
        var parts = (path ?? "").StartsWith(@"\\", StringComparison.Ordinal) ? path!.TrimStart('\\').TrimEnd('\\').Split('\\') : [];
        if (parts.Length < (share ? 2 : 3) || parts.Any(p => p.Length == 0 || p is "." or ".."))
        {
            throw new ToolProblem(share ? $"{path} is not a share. Enter a path such as \\\\FS01\\Archive$." : $"{path} is not a folder on a share, such as \\\\FS01\\Home$\\gsutton.");
        }
    }

    private Task<T> As<T>(string path, string doing, Func<T> work)
    {
        Check(path);
        if (!OperatingSystem.IsWindows())
        {
            throw new ToolProblem($"Home folders can only be changed when {Product.Title} runs on Windows.");
        }
        var (user, password) = service.Login() ?? throw new ToolProblem("The Active Directory service account is not configured. Configure it first.", StatusCodes.Status409Conflict);
        return Task.Run(() => Windows(user, password, path, doing, work));
    }

    [SupportedOSPlatform("windows")]
    private static T Windows<T>(string user, string password, string path, string doing, Func<T> work)
    {
        try
        {
            return NetworkLogon.Run(user, password, work, $"Windows could not sign in as {user} to reach {path}.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new ToolProblem($"Access denied. {user} does not have permission to {doing} {path}. Give it Modify on the share and its folders.");
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            throw new ToolProblem($"Could not {doing} {path}: {e.Message}");
        }
    }
}
