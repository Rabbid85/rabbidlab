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
/// stops, without waiting for the next sign-in.
/// </summary>
internal static class TrayStarter
{
    public static void Ensure()
    {
        string exe;
        using (var me = Process.GetCurrentProcess())
        {
            exe = me.MainModule.FileName;
        }
        var name = Path.GetFileNameWithoutExtension(exe);
        foreach (var session in SignedIn())
        {
            var there = false;
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    there |= p.SessionId == session;
                }
            }
            if (!there)
            {
                Start(session, exe);
            }
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
}
