using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;

namespace RabbidLab.Client;

/// <summary>
/// The service's own settings in Windows. An administrator at the computer can stop it, as with Datto RMM's agent:
/// the lock is on uninstalling (Removal), not on the service. Versions 1.1.160 and 1.1.161 took stopping away from
/// administrators while the computer was locked; that is put back here. And when the service's process ends without
/// the service being stopped (it crashed, or was ended in Task Manager), Windows starts it again.
/// </summary>
internal static class Guard
{
    // Windows' usual permissions for a service.
    private const string Usual = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)";

    /// <summary>Puts the service's permissions back to Windows' usual ones. Run once as the service starts.</summary>
    public static void UsualPermissions()
    {
        var manager = IntPtr.Zero;
        var service = IntPtr.Zero;
        try
        {
            var descriptor = new RawSecurityDescriptor(Usual);
            var bytes = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(bytes, 0);
            const int connect = 0x0001, readControl = 0x00020000, writeDac = 0x00040000, dacl = 0x4;
            manager = OpenSCManager(null, null, connect);
            service = manager == IntPtr.Zero ? IntPtr.Zero : OpenService(manager, ClientService.Name, readControl | writeDac);
            if (service != IntPtr.Zero)
            {
                SetServiceObjectSecurity(service, dacl, bytes);
            }
        }
        catch (Exception e) when (e is ArgumentException or SystemException)
        {
            // Left as they are.
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
