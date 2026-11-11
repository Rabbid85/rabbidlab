using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace RabbidLab.Server;

/// <summary>
/// The folder where Rabbid Lab keeps everything it saves: its settings, the activity log, the encryption keys and the
/// updates it downloads. DataPath in the configuration (or the RABBIDLAB_DATA environment variable) says where; otherwise
/// /data in a container (mounted as a volume), %ProgramData%\Rabbid Lab on Windows, and /var/lib/rabbidlab elsewhere. Only
/// the account it runs as (and on Windows, administrators and SYSTEM) can open it, whether Rabbid Lab made it or found it.
/// </summary>
public sealed class DataFolder
{
    public string Path { get; }

    /// <summary>Where the files kept here say what went wrong with them.</summary>
    public ILogger Log { get; }

    private DataFolder(string path, ILogger log)
    {
        Path = path;
        Log = log;
    }

    /// <summary>One of the JSON files kept here.</summary>
    public JsonFile<T> Json<T>(string name, JsonSerializerOptions options, bool watch = false) where T : class, new() =>
        new(System.IO.Path.Combine(Path, name), options, Log, watch);

    public static DataFolder Resolve(IConfiguration config, ILogger? log = null)
    {
        log ??= NullLogger.Instance;
        var path = config["DataPath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Environment.GetEnvironmentVariable("RABBIDLAB_DATA");
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Product.InContainer ? "/data"
                : OperatingSystem.IsWindows() ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Product.Name)
                : "/var/lib/rabbidlab";
        }
        if (!Directory.Exists(path))
        {
            Create(path);
        }
        else
        {
            Secure(path, log);
        }
        return new DataFolder(path, log);
    }

    private static void Create(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            new DirectoryInfo(path).Create(Locked(Me()));
            return;
        }
        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>A folder that was already there gets the same permissions as one Rabbid Lab makes, when it doesn't have them.</summary>
    private static void Secure(string path, ILogger log)
    {
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                // A link to somewhere else: whatever it points at isn't Rabbid Lab's to lock.
                log.LogWarning("The data folder {Path} is a link to another folder, so its permissions were left as they are. Check that only administrators can open it", path);
            }
            else if (OperatingSystem.IsWindows())
            {
                if (!IsLocked(path))
                {
                    Relock(path);
                    log.LogWarning("The data folder {Path} was open to other accounts. Its permissions were reset: only administrators, SYSTEM and the account {Product} runs as can open it", path, Product.Name);
                }
            }
            else if ((File.GetUnixFileMode(path) & ~(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute)) != 0)
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or InvalidOperationException or IdentityNotMappedException)
        {
            log.LogWarning(e, "The permissions of the data folder {Path} could not be checked or reset", path);
        }
    }

    [SupportedOSPlatform("windows")]
    private static DirectorySecurity Locked(SecurityIdentifier me)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), me })
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }
        return security;
    }

    /// <summary>Whether the folder inherits nothing, and only trusted accounts own it or are allowed in.</summary>
    [SupportedOSPlatform("windows")]
    private static bool IsLocked(string path)
    {
        var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        return security.AreAccessRulesProtected && Trusted(security.GetOwner(typeof(SecurityIdentifier)))
            && security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                .All(r => r.AccessControlType == AccessControlType.Deny || Trusted(r.IdentityReference));
    }

    /// <summary>Administrators, SYSTEM, the service accounts, and the account Rabbid Lab runs as.</summary>
    [SupportedOSPlatform("windows")]
    private static bool Trusted(IdentityReference? who) =>
        who is SecurityIdentifier sid && (sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) || sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
            || sid.IsWellKnown(WellKnownSidType.LocalServiceSid) || sid.IsWellKnown(WellKnownSidType.NetworkServiceSid)
            || sid.Value.StartsWith("S-1-5-80-", StringComparison.Ordinal) || sid == Me());

    /// <summary>The account Rabbid Lab runs as.</summary>
    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier Me()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!;
    }

    /// <summary>Locks the folder, and makes everything in it take the folder's permissions and a trusted owner.</summary>
    [SupportedOSPlatform("windows")]
    private static void Relock(string path)
    {
        var me = Me();
        var folder = new DirectoryInfo(path);
        if (!Trusted(folder.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))))
        {
            var owned = new DirectorySecurity();
            owned.SetOwner(me);
            folder.SetAccessControl(owned);
        }
        folder.SetAccessControl(Locked(me));
        // Links to other folders are left alone, so nothing outside the data folder is touched.
        var inside = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var entry in folder.EnumerateFileSystemInfos("*", inside))
        {
            if (entry is DirectoryInfo d)
            {
                if (!Trusted(d.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))))
                {
                    var owned = new DirectorySecurity();
                    owned.SetOwner(me);
                    d.SetAccessControl(owned);
                }
                var inherit = new DirectorySecurity();
                inherit.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                d.SetAccessControl(inherit);
            }
            else if (entry is FileInfo f)
            {
                if (!Trusted(f.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))))
                {
                    var owned = new FileSecurity();
                    owned.SetOwner(me);
                    f.SetAccessControl(owned);
                }
                var inherit = new FileSecurity();
                inherit.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                f.SetAccessControl(inherit);
            }
        }
    }
}
