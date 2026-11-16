using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RabbidLab.Client;

/// <summary>
/// Keeps the tray icon there: every round, the service starts the icon for anyone signed in to Windows who hasn't got
/// one, as that person and on their desktop. So it shows straight after an install or an update, and comes back if it
/// stops, without waiting for the next sign-in. A copy left running from before an update or a reinstall runs a
/// program file that has since been moved aside. It notices within seconds, starts the new one and takes its own icon
/// down as it goes, so it's left to do that: ending it here straight away left a dead icon in the tray after every
/// update. Only one that's still there a round later is ended (there is one icon for each person).
/// </summary>
internal static class TrayStarter
{
    // When each copy from before an update was first seen, by process id.
    private static readonly Dictionary<int, DateTime> old = new();
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(20);

    public static void Ensure()
    {
        if (Settings.Read().Quiet)
        {
            return; // nothing for people at the computer to see
        }
        string exe;
        string installed; // where Windows says this service's own program file is: the installed one
        using (var me = Process.GetCurrentProcess())
        {
            exe = me.MainModule.FileName;
            installed = Where(me.Id) ?? exe;
        }
        var name = Path.GetFileNameWithoutExtension(exe);
        var seen = new HashSet<int>();
        foreach (var session in SignedIn())
        {
            var there = false;
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    if (p.SessionId != session)
                    {
                        continue;
                    }
                    // The same file as the service's, or Windows won't say: it's left alone.
                    if (Where(p.Id) is not { } file || string.Equals(file, installed, StringComparison.OrdinalIgnoreCase))
                    {
                        there = true;
                        continue;
                    }
                    // From before an update: it gets a little while to start the new one and leave by itself.
                    seen.Add(p.Id);
                    if (!old.TryGetValue(p.Id, out var since))
                    {
                        old[p.Id] = since = DateTime.UtcNow;
                    }
                    if (DateTime.UtcNow - since < Grace)
                    {
                        there = true;
                        continue;
                    }
                    try
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                    {
                        // Gone already, or not to be ended: looked at again next round.
                    }
                }
            }
            if (!there)
            {
                Start(session, exe);
            }
        }
        // The ones that have gone are forgotten, so a later program with the same id starts fresh.
        foreach (var id in new List<int>(old.Keys))
        {
            if (!seen.Contains(id))
            {
                old.Remove(id);
            }
        }
    }

    // Where a running program's file is at this moment, as Windows has it: one whose file an installer moved aside, to
    // put a newer one in its place, shows up as somewhere else. Null when Windows won't say.
    private static string? Where(int id)
    {
        const int queryLimited = 0x1000;
        var process = OpenProcess(queryLimited, false, id);
        if (process == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            var path = new StringBuilder(1024);
            var size = path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    // The sessions someone is signed in to and using now.
    private static List<int> SignedIn()
    {
        var sessions = new List<int>();
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var list, out var count))
        {
            return sessions;
        }
        try
        {
            var size = Marshal.SizeOf(typeof(SessionInfo));
            for (var i = 0; i < count; i++)
            {
                var info = (SessionInfo) Marshal.PtrToStructure(IntPtr.Add(list, i * size), typeof(SessionInfo));
                if (info.State == 0 && info.Id != 0) // active, and not the services' own
                {
                    sessions.Add(info.Id);
                }
            }
        }
        finally
        {
            WTSFreeMemory(list);
        }
        return sessions;
    }

    // The icon, as whoever is signed in to that session, with their own environment, on their desktop.
    private static void Start(int session, string exe)
    {
        if (!WTSQueryUserToken(session, out var token))
        {
            return;
        }
        var environment = IntPtr.Zero;
        try
        {
            if (!CreateEnvironmentBlock(out environment, token, false))
            {
                environment = IntPtr.Zero;
            }
            var startup = new StartupInfo { Size = Marshal.SizeOf(typeof(StartupInfo)), Desktop = @"winsta0\default" };
            var line = new StringBuilder($"\"{exe}\" --tray");
            const int unicodeEnvironment = 0x400;
            if (CreateProcessAsUser(token, null, line, IntPtr.Zero, IntPtr.Zero, false, environment == IntPtr.Zero ? 0 : unicodeEnvironment,
                    environment, Path.GetDirectoryName(exe), ref startup, out var made))
            {
                CloseHandle(made.Thread);
                CloseHandle(made.Process);
            }
        }
        finally
        {
            if (environment != IntPtr.Zero)
            {
                DestroyEnvironmentBlock(environment);
            }
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SessionInfo
    {
        public int Id;
        public IntPtr Name;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2Size;
        public IntPtr Reserved2, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr list, out int count);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(int session, out IntPtr token);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessAsUser(IntPtr token, string? application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inherit, int flags, IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInfo made);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
}
