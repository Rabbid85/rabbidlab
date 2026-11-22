using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace RabbidLab.Server;

/// <summary>
/// Work done over the network as another account: the service account, or an admin typed in. Windows uses the login for
/// connections out (shares, print servers, SYSVOL), not on this computer. The one place Rabbid Lab does this.
/// </summary>
[SupportedOSPlatform("windows")]
public static class NetworkLogon
{
    /// <summary>Runs work as username (DOMAIN\user, user@domain, or a bare name). refused is said when Windows won't take the login.</summary>
    public static T Run<T>(string username, string password, Func<T> work, string refused)
    {
        var at = username.IndexOf('\\');
        var (user, domain) = at > 0 ? (username[(at + 1)..], username[..at]) : (username, (string?) null);
        if (!LogonUser(user, domain, password, NewCredentials, WinNt50, out var token))
        {
            throw new ToolProblem(refused);
        }
        using (token)
        {
            return WindowsIdentity.RunImpersonated(token, work);
        }
    }

    private const int NewCredentials = 9; // LOGON32_LOGON_NEW_CREDENTIALS: the login is used for connections out, not here
    private const int WinNt50 = 3;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LogonUserW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string user, string? domain, string password, int type, int provider, out SafeAccessTokenHandle token);
}
