using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace RabbidLab.Client;

/// <summary>
/// Whether this client may be uninstalled. Once it has joined a server, uninstalling is locked until someone allows it
/// in Rabbid Lab (Seats), for an hour at a time. The service writes what the server said to the registry, where the
/// installer reads it and refuses an uninstall that isn't allowed. A client with no server isn't locked.
/// </summary>
internal static class Removal
{
    private const string Yes = "yes";

    /// <summary>What the installer checks: set means uninstalling is allowed now.</summary>
    public static bool Allowed
    {
        get
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = machine.OpenSubKey(Settings.InstalledKey);
                return key?.GetValue("RemovalAllowed") as string == Yes;
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }
    }

    /// <summary>Until when the server allowed it, when it did.</summary>
    public static DateTime? Until
    {
        get
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(Settings.InstalledKey);
            return Parse(key?.GetValue("RemovalUntil") as string);
        }
    }

    public static DateTime? Parse(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at.ToUniversalTime() : (DateTime?) null;

    /// <summary>
    /// Run by the service every round. Joined to a server: allowed only until the time the server gave, if it gave one.
    /// Not joined: allowed. Whether uninstalling is locked now.
    /// </summary>
    public static bool Apply(bool joined, DateTime? until)
    {
        var allowed = !joined || until > DateTime.UtcNow;
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.CreateSubKey(Settings.InstalledKey);
        if (allowed)
        {
            key.SetValue("RemovalAllowed", Yes, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue("RemovalAllowed", throwOnMissingValue: false);
        }
        if (joined && until is { } at && allowed)
        {
            key.SetValue("RemovalUntil", at.ToString("o", CultureInfo.InvariantCulture), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue("RemovalUntil", throwOnMissingValue: false);
        }
        return !allowed;
    }

    /// <summary>The client's product code, from Windows' list of installed programs.</summary>
    public static string? ProductCode()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var list = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        if (list is null)
        {
            return null;
        }
        foreach (var name in list.GetSubKeyNames())
        {
            using var entry = list.OpenSubKey(name);
            if (name.StartsWith("{", StringComparison.Ordinal) && entry?.GetValue("DisplayName") as string == "Rabbid Lab Client" && entry.GetValue("WindowsInstaller") is int msi && msi == 1)
            {
                return name;
            }
        }
        return null;
    }

    /// <summary>Starts the uninstall: quietly when the server ordered it, with Windows' own prompts from the window. False when it couldn't start.</summary>
    public static bool Uninstall(bool quiet)
    {
        if (ProductCode() is not { } code)
        {
            return false;
        }
        try
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            using var installer = Process.Start(new ProcessStartInfo(Path.Combine(system, "msiexec.exe"), $"/x {code} {(quiet ? "/qn" : "/qb")} /norestart") { UseShellExecute = false, CreateNoWindow = quiet });
            return installer is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
