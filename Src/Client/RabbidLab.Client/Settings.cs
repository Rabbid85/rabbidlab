using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

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

    public static Settings Read()
    {
        var s = new Settings();
        string? policyFingerprint = null, installedServer = null, installedFingerprint = null;
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using (var policy = machine.OpenSubKey(PolicyKey))
        {
            if (policy is not null)
            {
                s.PolicyServer = Text(policy, "Server")?.TrimEnd('/');
                policyFingerprint = CleanFingerprint(Text(policy, "CertificateFingerprint"));
                s.AccountChanges = policy.GetValue("AllowAccountChanges") is not int allowed || allowed != 0;
            }
        }
        using (var installed = machine.OpenSubKey(InstalledKey))
        {
            if (installed is not null)
            {
                installedServer = Text(installed, "Server")?.TrimEnd('/');
                installedFingerprint = CleanFingerprint(Text(installed, "CertificateFingerprint"));
            }
        }
        // Trying it out without installing: the same from environment variables.
        installedServer ??= Environment.GetEnvironmentVariable("RABBIDLAB_SERVER")?.TrimEnd('/');
        installedFingerprint ??= CleanFingerprint(Environment.GetEnvironmentVariable("RABBIDLAB_FINGERPRINT"));
        s.Server = s.PolicyServer ?? installedServer;
        // The installer's fingerprint belongs to the installer's server, not to another one Group Policy picks.
        var sameServer = s.PolicyServer is null || string.Equals(s.PolicyServer, installedServer, StringComparison.OrdinalIgnoreCase);
        s.Fingerprint = policyFingerprint ?? (sameServer ? installedFingerprint : null);
        return s;
    }

    /// <summary>A fingerprint as 64 capitals and digits, however it was typed; null when there's none.</summary>
    public static string? CleanFingerprint(string? value)
    {
        var clean = value?.Replace(":", "").Replace(" ", "").Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(clean) ? null : clean;
    }

    private static string? Text(RegistryKey key, string name) => key.GetValue(name) is string v && v.Trim().Length > 0 ? v.Trim() : null;
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

    public static string File(string name) => Path.Combine(Data, name);

    /// <summary>
    /// Makes the folders and sets their permissions: SYSTEM and Administrators full control, Users read, and Updates
    /// without Users. Run by the service (and --check-in), never by the tray icon.
    /// </summary>
    public static void Secure()
    {
        try
        {
            if (Override is not null)
            {
                Directory.CreateDirectory(Updates);
                return;
            }
            // Already locked: only SYSTEM and administrators could have written here, so everything in it stays.
            var locked = Directory.Exists(Data) && Locked(Data);
            Lock(Data, usersRead: true);
            if (!locked)
            {
                // The folder was open until now: files a standard user put there aren't the client's.
                foreach (var file in Directory.GetFiles(Data))
                {
                    if (!Ours(System.IO.File.GetAccessControl(file, AccessControlSections.Owner)))
                    {
                        System.IO.File.Delete(file);
                    }
                }
                if (Directory.Exists(Updates) && !Ours(Directory.GetAccessControl(Updates, AccessControlSections.Owner)))
                {
                    Directory.Delete(Updates, recursive: true);
                }
            }
            Lock(Updates, usersRead: false);
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
        }
    }

    private static void Lock(string path, bool usersRead)
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
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path, security);
        }
        // A folder someone else made first keeps them as its owner, who could change the permissions back.
        if (!Ours(Directory.GetAccessControl(path, AccessControlSections.Owner)))
        {
            security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        }
        Directory.SetAccessControl(path, security);
    }

    /// <summary>Whether the folder is ours and takes no permissions from the folder above it.</summary>
    private static bool Locked(string path)
    {
        var security = Directory.GetAccessControl(path, AccessControlSections.Owner | AccessControlSections.Access);
        return Ours(security) && security.AreAccessRulesProtected;
    }

    /// <summary>Whether SYSTEM or the Administrators group owns it.</summary>
    private static bool Ours(FileSystemSecurity security) =>
        security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner
        && (owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
}
