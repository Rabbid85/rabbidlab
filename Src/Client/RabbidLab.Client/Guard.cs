using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;

namespace RabbidLab.Client;

/// <summary>
/// Keeps the client's service running while the computer is locked (Removal). Locked: only Windows itself (SYSTEM, which
/// the installer and the service run as) may stop, pause, reconfigure or delete the service; administrators at the
/// computer may still look at it and start it. Unlocked (allowed in Rabbid Lab, the uninstall code, too long without
/// the server, or no server): the service's permissions are Windows' usual ones again. And whenever its process is
/// ended without the service being stopped, Windows starts it again. An administrator who sets out to get round this
/// still can; it stops the ordinary ways (Services, sc stop, net stop, Task Manager).
/// </summary>
internal static class Guard
{
    // Windows' usual permissions for a service, and the same with administrators left only reading and starting.
    private const string Usual = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)";
    private const string Guarded = "D:(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCLCSWRPLOCRRC;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)";
    private static bool? guarded; // as it was last set by this process

    /// <summary>Run by the service whenever it works out whether the computer is locked. Does nothing when nothing changed.</summary>
    public static void Set(bool locked)
    {
        if (guarded == locked)
        {
            return;
        }
        var manager = IntPtr.Zero;
        var service = IntPtr.Zero;
        try
        {
            var descriptor = new RawSecurityDescriptor(locked ? Guarded : Usual);
            var bytes = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(bytes, 0);
            const int connect = 0x0001, readControl = 0x00020000, writeDac = 0x00040000, dacl = 0x4;
            manager = OpenSCManager(null, null, connect);
            service = manager == IntPtr.Zero ? IntPtr.Zero : OpenService(manager, ClientService.Name, readControl | writeDac);
            if (service != IntPtr.Zero && SetServiceObjectSecurity(service, dacl, bytes))
            {
                guarded = locked;
            }
        }
        catch (Exception e) when (e is ArgumentException or SystemException)
        {
            // Not the service (--check-in), or Windows refused: tried again at the next round.
        }
        finally
        {
            if (service != IntPtr.Zero)
            {
                CloseServiceHandle(service);
            }
            if (manager != IntPtr.Zero)
            {
                CloseServiceHandle(manager);
            }
        }
    }

    /// <summary>When the service's process ends without the service being stopped, Windows starts it again after five seconds. Set once as the service starts.</summary>
    public static void StartAgainWhenEnded()
    {
        try
        {
            var sc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
            using var set = Process.Start(new ProcessStartInfo(sc, $"failure {ClientService.Name} reset= 86400 actions= restart/5000/restart/5000/restart/30000") { UseShellExecute = false, CreateNoWindow = true });
            set?.WaitForExit(10000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // Left as it was.
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, int access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string name, int access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceObjectSecurity(IntPtr service, int information, byte[] descriptor);

    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
