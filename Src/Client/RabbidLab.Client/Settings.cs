using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace RabbidLab.Client;

/// <summary>
/// Where the server is. Group Policy first (Software\Policies\Rabbid Lab\Client, from the Rabbid Lab template), then what
/// the MSI or Connect was given (SERVER and FINGERPRINT, kept in Software\Rabbid Lab\Client).
/// </summary>
internal sealed class Settings
{
    public const string PolicyKey = @"SOFTWARE\Policies\Rabbid Lab\Client";
    public const string InstalledKey = @"SOFTWARE\Rabbid Lab\Client";

    /// <summary>The server's address, such as https://rabbidlab.amw.com:8420, without a slash at the end.</summary>
    public string? Server { get; private set; }

    /// <summary>The SHA-256 fingerprint of the server's HTTPS certificate: when set, the only certificate the client accepts.</summary>
    public string? Fingerprint { get; private set; }

    /// <summary>The server Group Policy sets, which nothing on the computer can change; null when it doesn't.</summary>
    public string? PolicyServer { get; private set; }

    /// <summary>Whether the server may make and change this computer's Windows accounts. Group Policy can turn it off.</summary>
    public bool AccountChanges { get; private set; } = true;

    /// <summary>Whether the server may run scripts and actions on this computer (Jobs.cs). Group Policy can turn it off.</summary>
    public bool Scripts { get; private set; } = true;

    /// <summary>Nothing for people at the computer to see: no tray icon, no messages. Group Policy first, then the installer's QUIET=1.</summary>
    public bool Quiet { get; private set; }

    public static Settings Read()
    {
        var s = new Settings();
        string? policyFingerprint = null, installedServer = null, installedFingerprint = null;
        int? policyQuiet = null;
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using (var policy = machine.OpenSubKey(PolicyKey))
        {
            if (policy is not null)
            {
                s.PolicyServer = Text(policy, "Server")?.TrimEnd('/');
                policyFingerprint = CleanFingerprint(Text(policy, "CertificateFingerprint"));
                s.AccountChanges = policy.GetValue("AllowAccountChanges") is not int allowed || allowed != 0;
                s.Scripts = policy.GetValue("AllowScripts") is not int scripts || scripts != 0;
                policyQuiet = policy.GetValue("Quiet") as int?;
            }
        }
        using (var installed = machine.OpenSubKey(InstalledKey))
        {
            if (installed is not null)
            {
                installedServer = Text(installed, "Server")?.TrimEnd('/');
                installedFingerprint = CleanFingerprint(Text(installed, "CertificateFingerprint"));
                s.Quiet = policyQuiet is int q ? q != 0 : installed.GetValue("Quiet") is int iq && iq != 0;
            }
            else
            {
                s.Quiet = policyQuiet is int q && q != 0;
            }
        }
        // Trying it out without installing: the same from environment variables.
        installedServer ??= Environment.GetEnvironmentVariable("RABBIDLAB_SERVER")?.TrimEnd('/');
        installedFingerprint ??= CleanFingerprint(Environment.GetEnvironmentVariable("RABBIDLAB_FINGERPRINT"));
        s.Server = s.PolicyServer ?? installedServer;
        // The installer's fingerprint belongs to the installer's server, not to another one Group Policy picks.
        var sameServer = s.PolicyServer is null || string.Equals(s.PolicyServer, installedServer, StringComparison.OrdinalIgnoreCase);
        s.Fingerprint = policyFingerprint ?? (sameServer ? installedFingerprint : null);
        s.ours = policyFingerprint is null && s.Fingerprint is not null;
        s.policyPinned = policyFingerprint is not null;
        return s;
    }

    // Group Policy sets the fingerprint: only Group Policy changes it.
    private bool policyPinned;

    /// <summary>
    /// Keeps a new key the server vouched for with its identity (Vouch), in place of the one stored, or of none when
    /// Windows was trusted to check the certificate. Not over a fingerprint from Group Policy. Whether it was kept.
    /// </summary>
    public bool Repin(string key)
    {
        if (policyPinned)
        {
            return false;
        }
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var installed = machine.OpenSubKey(InstalledKey, writable: true);
            if (installed is null)
            {
                return false;
            }
            installed.SetValue("CertificateFingerprint", key);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false; // not the service
        }
    }

    // The fingerprint is the installer's or Connect's, so the client may replace it (not Group Policy's).
    private bool ours;

    /// <summary>
    /// Keeps the fingerprint of the server's key in place of its certificate's, once the certificate was accepted, so
    /// the server can make its certificate again. Whether it changed anything.
    /// </summary>
    public bool KeepKey(string? key)
    {
        if (!ours || key is null || string.Equals(key, Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var installed = machine.OpenSubKey(InstalledKey, writable: true);
            if (installed?.GetValue("CertificateFingerprint") is not string)
            {
                return false; // from an environment variable, trying it out
            }
            installed.SetValue("CertificateFingerprint", key);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false; // not the service: it does this at its next round
        }
    }

    /// <summary>A fingerprint as 64 capitals and digits, however it was typed; null when there's none.</summary>
    public static string? CleanFingerprint(string? value)
    {
        var clean = value?.Replace(":", "").Replace(" ", "").Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(clean) ? null : clean;
    }

    private static string? Text(RegistryKey key, string name) => key.GetValue(name) is string v && v.Trim().Length > 0 ? v.Trim() : null;
}

/// <summary>Something another account put where one of the client's folders goes couldn't be moved aside.</summary>
internal sealed class NotOursException : IOException
{
    public NotOursException(string path, Exception? inner) : base($"{path} was made by another account and can't be moved aside.", inner)
    {
    }
}

/// <summary>
/// Where the client keeps its files: %ProgramData%\Rabbid Lab Client, or RABBIDLAB_CLIENT_DATA when trying it out.
/// Only SYSTEM and administrators can change it; everyone can read it (the tray icon reads status.json).
/// </summary>
internal static class Folder
{
    private static readonly string? Override = Environment.GetEnvironmentVariable("RABBIDLAB_CLIENT_DATA");

    /// <summary>Run from a folder of your own, without installing: its permissions are left alone.</summary>
    public static bool TryingOut => Override is not null;

    public static string Data { get; } = Override
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Rabbid Lab Client");

    /// <summary>Downloaded updates, which only SYSTEM and administrators can open.</summary>
    public static string Updates => Path.Combine(Data, "Updates");

    /// <summary>The scripts of jobs while they run, which only SYSTEM and administrators can open.</summary>
    public static string Jobs => Path.Combine(Data, "Jobs");

    /// <summary>What only the service reads: the errors it has to tell, Group Policy's results, the installer's log.</summary>
    public static string Service => Path.Combine(Data, "Service");

    public static string File(string name) => Path.Combine(Data, name);

    /// <summary>A file of the service's own, which only SYSTEM and administrators can open.</summary>
    public static string Kept(string name) => Path.Combine(Service, name);

    /// <summary>
    /// Makes the folders and sets their permissions: SYSTEM and Administrators full control, Users read, and Updates,
    /// Jobs and Service without Users. Run by the service (and --check-in), never by the tray icon. False when something
    /// another account put there is in the way and won't move: nothing may be read from or written to the folder then.
    /// </summary>
    public static bool Secure()
    {
        try
        {
            if (Override is not null)
            {
                foreach (var folder in new[] { Updates, Jobs, Service })
                {
                    Directory.CreateDirectory(folder);
                }
                return true;
            }
            // Already locked: only SYSTEM and administrators could have written here, so everything in it stays.
            if (!Make(Data, usersRead: true))
            {
                // The folder was open until now: files and links a standard user put there aren't the client's.
                foreach (var file in Directory.GetFiles(Data))
                {
                    if (Linked(System.IO.File.GetAttributes(file)) || !Ours(System.IO.File.GetAccessControl(file, AccessControlSections.Owner)))
                    {
                        System.IO.File.Delete(file);
                    }
                }
            }
            foreach (var folder in new[] { Updates, Jobs, Service })
            {
                Make(folder, usersRead: false);
            }
            // A script left by a job that was cut short (the computer restarted, say).
            foreach (var left in Directory.GetFiles(Jobs))
            {
                System.IO.File.Delete(left);
            }
            Gather();
            return true;
        }
        catch (NotOursException)
        {
            return false;
        }
        catch (Exception)
        {
            // Not an administrator, or Windows refused: the service starts anyway, and needs the folder to be there.
            try
            {
                Directory.CreateDirectory(Updates);
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                // Whatever writes there next says why it can't.
            }
            return true;
        }
    }

    /// <summary>
    /// Ends with a real folder of SYSTEM's or the administrators' at this path, with the client's permissions on it;
    /// whether it was that already. Anything else there (a link to somewhere else, a file, a folder another account
    /// made) is moved aside by renaming it, so nothing it leads to or holds is touched: setting permissions through a
    /// link would set them on wherever it leads. The folder is held open meanwhile, so it can't be swapped for a link
    /// between being looked at and being changed.
    /// </summary>
    private static bool Make(string path, bool usersRead)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags all = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
        }
        if (usersRead)
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, all, PropagationFlags.None, AccessControlType.Allow));
        }
        var made = false;
        for (var tries = 0; tries < 4; tries++)
        {
            using (var held = Hold(path, out var attributes, out var there))
            {
                if (held is not null && !Linked(attributes) && (attributes & FileAttributes.Directory) != 0)
                {
                    var now = Directory.GetAccessControl(path, AccessControlSections.Owner | AccessControlSections.Access);
                    if (Ours(now))
                    {
                        Directory.SetAccessControl(path, security);
                        return !made && now.AreAccessRulesProtected;
                    }
                }
                else if (!there)
                {
                    // Made with its permissions already on it; looked at again, in case something got there first.
                    Directory.CreateDirectory(path, security);
                    made = true;
                    continue;
                }
            }
            try
            {
                Directory.Move(path, $"{path}.not-ours-{Guid.NewGuid():N}"); // a file or a folder, and a link itself, not where it leads
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new NotOursException(path, e);
            }
        }
        throw new NotOursException(path, null);
    }

    // Opens whatever is at the path, a link itself and not where it leads, so that it can't be renamed or deleted while
    // it's held. Null when it can't be opened: there says whether that's because nothing is there.
    private static SafeFileHandle? Hold(string path, out FileAttributes attributes, out bool there)
    {
        const uint readAttributes = 0x80, backupSemantics = 0x02000000, openLink = 0x00200000;
        attributes = 0;
        var handle = CreateFile(path, readAttributes, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, backupSemantics | openLink, IntPtr.Zero);
        var error = Marshal.GetLastWin32Error();
        there = !handle.IsInvalid || error is not (2 or 3); // not "file not found" or "path not found"
        if (!handle.IsInvalid && GetFileInformationByHandle(handle, out var facts))
        {
            attributes = (FileAttributes) facts.Attributes;
            return handle;
        }
        handle.Dispose();
        return null;
    }

    // Files everyone signed in could read until 1.1.435 are the service's own now (Kept): the errors still to tell go
    // with it, and the rest are written again there.
    private static void Gather()
    {
        foreach (var name in new[] { "problems.json", "update.log", "gp-computer.xml", "gp-user.xml" })
        {
            var old = File(name);
            if (!System.IO.File.Exists(old))
            {
                continue;
            }
            try
            {
                if (name == "problems.json" && !System.IO.File.Exists(Kept(name)))
                {
                    System.IO.File.Copy(old, Kept(name)); // a copy takes the Service folder's permissions; a moved file keeps its own
                }
                System.IO.File.Delete(old);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Still open: the installer writes its log while it starts this service. It goes the next time.
            }
        }
    }

    private static bool Linked(FileAttributes attributes) => (attributes & FileAttributes.ReparsePoint) != 0;

    /// <summary>Whether SYSTEM or the Administrators group owns it.</summary>
    private static bool Ours(FileSystemSecurity security) =>
        security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner
        && (owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));

    [StructLayout(LayoutKind.Sequential)]
    private struct FileFacts
    {
        public uint Attributes, Created1, Created2, Read1, Read2, Written1, Written2, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileFacts facts);
}
