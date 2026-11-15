using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace RabbidLab.Client;

/// <summary>This computer as the server sees it: its name, its Windows, and who is signed in to it.</summary>
internal static class Machine
{
    public static string Name => Environment.MachineName;

    /// <summary>Such as Windows 11 Pro 24H2 (WindowsVersion, which the server uses for itself too).</summary>
    public static string Os => RabbidLab.Shared.WindowsVersion.Name();

    /// <summary>Who is signed in, at the screen or over Remote Desktop, as DOMAIN\name; null for nobody.</summary>
    public static string? Users()
    {
        var found = new List<string>();
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var list, out var count))
        {
            return null;
        }
        try
        {
            var size = Marshal.SizeOf(typeof(SessionInfo));
            for (var i = 0; i < count; i++)
            {
                var session = (SessionInfo) Marshal.PtrToStructure(new IntPtr(list.ToInt64() + (i * size)), typeof(SessionInfo));
                if (session.State != Active)
                {
                    continue;
                }
                var user = Ask(session.Id, UserName);
                if (!string.IsNullOrEmpty(user))
                {
                    var domain = Ask(session.Id, DomainName);
                    found.Add(string.IsNullOrEmpty(domain) ? user! : $"{domain}\\{user}");
                }
            }
        }
        finally
        {
            WTSFreeMemory(list);
        }
        return found.Count == 0 ? null : string.Join(", ", found.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string? Ask(int session, int what)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, session, what, out var buffer, out _))
        {
            return null;
        }
        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private const int Active = 0;
    private const int UserName = 5;
    private const int DomainName = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct SessionInfo
    {
        public int Id;
        public IntPtr StationName;
        public int State;
    }

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int session, int what, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
