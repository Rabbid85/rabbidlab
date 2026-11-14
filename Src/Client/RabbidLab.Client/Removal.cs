using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace RabbidLab.Client;

/// <summary>Why the client may be uninstalled now, or that it's locked.</summary>
internal enum Unlocked
{
    Locked,
    NoServer,
    ByServer,
    ByCode,
    Offline,
}

/// <summary>
/// Whether this client may be uninstalled. Once it has joined a server, uninstalling is locked. Three things unlock it:
/// an admin allows it in Rabbid Lab (Devices), for an hour at a time; the computer's uninstall code is typed here, for
/// an hour, with no server needed (UninstallCode); or the server hasn't been reached for as many days as the server
/// said (30 unless it's turned off there). The service writes the outcome to the registry, where the installer reads it
/// and refuses an uninstall that isn't allowed. A client with no server isn't locked.
/// </summary>
internal static class Removal
{
    private const string Yes = "yes";

    /// <summary>What the installer checks: set means uninstalling is allowed now.</summary>
    public static bool Allowed => Text("RemovalAllowed") == Yes;

    /// <summary>Until when the server allowed it, when it did.</summary>
    public static DateTime? Until => Parse(Text("RemovalUntil"));

    /// <summary>Until when the uninstall code allowed it, when it was typed.</summary>
    public static DateTime? CodeUntil => Parse(Text("CodeUnlockedUntil"));

    /// <summary>The check the server gave this computer for its uninstall code; null when it hasn't one yet.</summary>
    public static string? CodeCheck => Text("UninstallCode");

    /// <summary>When the code was last typed here, for the server to be told.</summary>
    public static string? CodeUsed => Text("CodeUsed");

    /// <summary>After how many days without the server the lock lets go; 0 when it never does.</summary>
    public static int OfflineDays => Read(key => key?.GetValue("UnlockAfterDays") is int days && days > 0 ? days : 0);

    /// <summary>This computer has checked in with a server, so it stays locked whatever becomes of its identity file.</summary>
    public static bool Enrolled => Text("LastContact") is not null;

    public static DateTime? Parse(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at.ToUniversalTime() : (DateTime?) null;

    /// <summary>
    /// Run by the service every round, with whether this computer belongs to a server and until when the server allowed
    /// uninstalling. Writes what the installer reads, and says why it's allowed, or that it's locked.
    /// </summary>
    public static Unlocked Apply(bool joined, DateTime? until)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.CreateSubKey(Settings.InstalledKey);
        var now = DateTime.UtcNow;
        var why = !joined ? Unlocked.NoServer
            : until > now ? Unlocked.ByServer
            : Parse(key.GetValue("CodeUnlockedUntil") as string) > now ? Unlocked.ByCode
            : key.GetValue("UnlockAfterDays") is int days && days > 0 && Parse(key.GetValue("LastContact") as string) is { } last && now - last > TimeSpan.FromDays(days) ? Unlocked.Offline
            : Unlocked.Locked;
        if (why == Unlocked.Locked)
        {
            key.DeleteValue("RemovalAllowed", throwOnMissingValue: false);
        }
        else
        {
            key.SetValue("RemovalAllowed", Yes, RegistryValueKind.String);
        }
        if (why == Unlocked.ByServer)
        {
            key.SetValue("RemovalUntil", until!.Value.ToString("o", CultureInfo.InvariantCulture), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue("RemovalUntil", throwOnMissingValue: false);
        }
        if (why != Unlocked.ByCode)
        {
            key.DeleteValue("CodeUnlockedUntil", throwOnMissingValue: false);
        }
        return why;
    }

    /// <summary>
    /// The server answered a check-in: when, the check for this computer's uninstall code, and after how many days
    /// without the server the lock lets go. Only what changed is written.
    /// </summary>
    public static void Reached(string? check, int? offlineDays)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.CreateSubKey(Settings.InstalledKey);
        key.SetValue("LastContact", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), RegistryValueKind.String);
        if (!string.IsNullOrEmpty(check) && key.GetValue("UninstallCode") as string != check)
        {
            key.SetValue("UninstallCode", check!, RegistryValueKind.String);
        }
        if (offlineDays is { } days && !(key.GetValue("UnlockAfterDays") is int had && had == days))
        {
            key.SetValue("UnlockAfterDays", Math.Max(0, days), RegistryValueKind.DWord);
        }
    }

    /// <summary>The server can't be reached: the days without it count from now, when nothing says since when.</summary>
    public static void Unreached()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.CreateSubKey(Settings.InstalledKey);
        if (key.GetValue("LastContact") is not string)
        {
            key.SetValue("LastContact", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), RegistryValueKind.String);
        }
    }

    /// <summary>This computer left its server (removed there, or it has none): nothing of the old one is kept.</summary>
    public static void Left()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.CreateSubKey(Settings.InstalledKey);
        foreach (var name in new[] { "LastContact", "UninstallCode", "UnlockAfterDays", "CodeUnlockedUntil", "CodeUsed" })
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// The uninstall code, typed on this computer by an administrator: when it's right, uninstalling is allowed for an
    /// hour from now, and that it was used is kept for the server. Until when, or null for a wrong code.
    /// </summary>
    public static DateTime? UnlockWith(string typed)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.CreateSubKey(Settings.InstalledKey);
        if (!UninstallCode.Matches(typed, key.GetValue("UninstallCode") as string))
        {
            return null;
        }
        var now = DateTime.UtcNow;
        var until = now.AddHours(1);
        key.SetValue("CodeUnlockedUntil", until.ToString("o", CultureInfo.InvariantCulture), RegistryValueKind.String);
        key.SetValue("CodeUsed", now.ToString("o", CultureInfo.InvariantCulture), RegistryValueKind.String);
        key.SetValue("RemovalAllowed", Yes, RegistryValueKind.String);
        return until;
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

    private static string? Text(string name) => Read(key => key?.GetValue(name) as string);

    // Anyone on the computer can read these; false, null or 0 when they can't be read.
    private static T Read<T>(Func<RegistryKey?, T> read)
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(Settings.InstalledKey);
            return read(key);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return read(null);
        }
    }
}
