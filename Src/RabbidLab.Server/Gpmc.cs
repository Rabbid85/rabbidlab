using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;
using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>Who Group Policy Management works as, against which domain controller.</summary>
public sealed record GpmcTarget(string Domain, string Host, string Username, string Password);

/// <summary>A backup in Group Policy Management's backup folder.</summary>
public sealed record GpmcBackup(string Id, string Gpo, string Name, DateTime At, string? Comment);

/// <summary>Active Directory or SYSVOL turned the account down.</summary>
public sealed class GpmcDenied(string message) : Exception(message);

/// <summary>Group Policy Management (gpmc.msc's own engine): what only it does right, SYSVOL and its permissions included.</summary>
public interface IGpmc
{
    /// <summary>Whether it's installed here.</summary>
    bool Here { get; }

    /// <summary>The GPO's settings report: XML, or HTML to save.</summary>
    Task<string> Report(GpmcTarget t, string gpo, bool html, CancellationToken stop);

    /// <summary>A new empty GPO's ID.</summary>
    Task<string> Create(GpmcTarget t, string name, CancellationToken stop);

    /// <summary>A copy's ID: the settings, not the links; the permissions a new GPO gets.</summary>
    Task<string> Copy(GpmcTarget t, string gpo, string name, CancellationToken stop);

    Task Delete(GpmcTarget t, string gpo, CancellationToken stop);

    Task<GpmcBackup> Backup(GpmcTarget t, string gpo, string folder, string comment, CancellationToken stop);

    /// <summary>The restored GPO's ID (a deleted one comes back with it).</summary>
    Task<string> Restore(GpmcTarget t, string folder, string backup, CancellationToken stop);

    /// <summary>Apply Group Policy for someone, or (apply false) only Read, so computers can still read it.</summary>
    Task SetApply(GpmcTarget t, string gpo, string trustee, bool apply, CancellationToken stop);

    /// <summary>The backups in the folder, read from their own files.</summary>
    IReadOnlyList<GpmcBackup> Backups(string folder);
}

/// <summary>Where Group Policy Management isn't (Docker, Linux): only the backups that are already there can be listed.</summary>
public sealed class NoGpmc : IGpmc
{
    public bool Here => false;

    public Task<string> Report(GpmcTarget t, string gpo, bool html, CancellationToken stop) => throw new NotSupportedException();

    public Task<string> Create(GpmcTarget t, string name, CancellationToken stop) => throw new NotSupportedException();

    public Task<string> Copy(GpmcTarget t, string gpo, string name, CancellationToken stop) => throw new NotSupportedException();

    public Task Delete(GpmcTarget t, string gpo, CancellationToken stop) => throw new NotSupportedException();

    public Task<GpmcBackup> Backup(GpmcTarget t, string gpo, string folder, string comment, CancellationToken stop) => throw new NotSupportedException();

    public Task<string> Restore(GpmcTarget t, string folder, string backup, CancellationToken stop) => throw new NotSupportedException();

    public Task SetApply(GpmcTarget t, string gpo, string trustee, bool apply, CancellationToken stop) => throw new NotSupportedException();

    public IReadOnlyList<GpmcBackup> Backups(string folder) => GpmcFiles.Backups(folder);
}

/// <summary>Reading Group Policy Management's backup folders without it: each backup's bkupInfo.xml.</summary>
public static class GpmcFiles
{
    public static IReadOnlyList<GpmcBackup> Backups(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }
        var list = new List<GpmcBackup>();
        foreach (var dir in Directory.EnumerateDirectories(folder))
        {
            var info = Path.Combine(dir, "bkupInfo.xml");
            if (!File.Exists(info))
            {
                continue;
            }
            try
            {
                var x = XDocument.Load(info).Root;
                string? Of(string name) => x?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
                if (Of("ID") is not { Length: > 0 } id || Of("GPOGuid") is not { Length: > 0 } gpo)
                {
                    continue;
                }
                var at = DateTime.TryParse(Of("BackupTime"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
                    ? t : Directory.GetCreationTimeUtc(dir);
                list.Add(new GpmcBackup(id.ToUpperInvariant(), gpo.ToUpperInvariant(), Of("GPODisplayName") ?? gpo, at, Words.Blank(Of("Comment"))));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // A folder that isn't a readable backup is skipped.
            }
        }
        return list;
    }
}

/// <summary>
/// Group Policy Management's COM engine (GPMgmt.GPM), in this process. Each job runs on a thread of its own, signed in
/// for the network as the account given (as runas /netonly does), so Active Directory and SYSVOL see that account and
/// nothing else changes here.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGpmc : IGpmc
{
    private const int AccessDenied = unchecked((int) 0x80070005);
    private const int LdapRights = unchecked((int) 0x80072098); // ERROR_DS_INSUFF_ACCESS_RIGHTS
    private const int LogonFailure = unchecked((int) 0x8007052E);

    public bool Here => Type.GetTypeFromProgID("GPMgmt.GPM") is not null;

    public Task<string> Report(GpmcTarget t, string gpo, bool html, CancellationToken stop) => Run(t, (gpm, k, domain) =>
    {
        dynamic g = domain.GetGPO(gpo);
        dynamic result = g.GenerateReport(html ? k.ReportHTML : k.ReportXML);
        return (string) result.Result;
    });

    public Task<string> Create(GpmcTarget t, string name, CancellationToken stop) => Run(t, (gpm, k, domain) =>
    {
        dynamic g = domain.CreateGPO();
        g.DisplayName = name;
        return (string) g.ID;
    });

    public Task<string> Copy(GpmcTarget t, string gpo, string name, CancellationToken stop) => Run(t, (gpm, k, domain) =>
    {
        dynamic source = domain.GetGPO(gpo);
        dynamic result = source.CopyTo(0, domain);
        dynamic g = result.Result;
        g.DisplayName = name;
        return (string) g.ID;
    });

    public Task Delete(GpmcTarget t, string gpo, CancellationToken stop) => Run(t, (gpm, k, domain) =>
    {
        domain.GetGPO(gpo).Delete();
        return true;
    });

    public Task<GpmcBackup> Backup(GpmcTarget t, string gpo, string folder, string comment, CancellationToken stop) => Run(t, (gpm, k, domain) =>
    {
        dynamic g = domain.GetGPO(gpo);
        dynamic result = g.Backup(folder, comment);
        dynamic b = result.Result;
        return new GpmcBackup(((string) b.ID).ToUpperInvariant(), ((string) b.GPOID).ToUpperInvariant(), (string) b.GPODisplayName, ((DateTime) b.Timestamp).ToUniversalTime(),
            Words.Blank((string) b.Comment));
    });

    public Task<string> Restore(GpmcTarget t, string folder, string backup, CancellationToken stop) => Run(t, (gpm, k, domain) =>
    {
        dynamic dir = gpm.GetBackupDir(folder);
        dynamic b = dir.GetBackup(backup);
        dynamic result = domain.RestoreGPO(b, k.DoNotValidateDC);
        dynamic g = result.Result;
        return (string) g.ID;
    });

    public Task SetApply(GpmcTarget t, string gpo, string trustee, bool apply, CancellationToken stop) => Run(t, (gpm, k, domain) =>
    {
        dynamic g = domain.GetGPO(gpo);
        dynamic security = g.GetSecurityInfo();
        security.RemoveTrustee(trustee);
        security.Add(gpm.CreatePermission(trustee, apply ? k.PermGPOApply : k.PermGPORead, false));
        g.SetSecurityInfo(security);
        return true;
    });

    public IReadOnlyList<GpmcBackup> Backups(string folder) => GpmcFiles.Backups(folder);

    // On its own STA thread (the engine is apartment-threaded), as the account given.
    private static Task<T> Run<T>(GpmcTarget t, Func<dynamic, dynamic, dynamic, T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                T Go()
                {
                    dynamic gpm = Activator.CreateInstance(Type.GetTypeFromProgID("GPMgmt.GPM", true)!)!;
                    dynamic k = gpm.GetConstants();
                    dynamic d = gpm.GetDomain(t.Domain, t.Host, k.UseAnyDC);
                    try
                    {
                        return work(gpm, k, d);
                    }
                    finally
                    {
                        // Let go here, on its own thread, rather than whenever the collector gets to them.
                        foreach (object o in new object[] { d, k, gpm })
                        {
                            if (Marshal.IsComObject(o))
                            {
                                Marshal.FinalReleaseComObject(o);
                            }
                        }
                    }
                }
                // No login: as this computer, for reading before the service account is set up.
                if (t.Username.Length == 0)
                {
                    done.SetResult(Go());
                    return;
                }
                var at = t.Username.IndexOf('\\');
                var (user, domain) = at > 0 ? (t.Username[(at + 1)..], t.Username[..at]) : (t.Username, (string?) null);
                if (!LogonUser(user, domain, t.Password, NewCredentials, WinNt50, out var token))
                {
                    throw new ToolProblem("Windows couldn't use that login.");
                }
                using (token)
                {
                    done.SetResult(WindowsIdentity.RunImpersonated(token, Go));
                }
            }
            catch (COMException e) when (e.HResult is AccessDenied or LdapRights)
            {
                done.SetException(new GpmcDenied(e.Message));
            }
            catch (COMException e) when (e.HResult == LogonFailure)
            {
                done.SetException(new ToolProblem("Windows turned the login down: the username or password is wrong."));
            }
            catch (COMException e)
            {
                done.SetException(new ToolProblem($"Group Policy Management said: {e.Message.Trim()}"));
            }
            catch (UnauthorizedAccessException e)
            {
                done.SetException(new GpmcDenied(e.Message));
            }
            catch (Exception e)
            {
                done.SetException(e is ToolProblem ? e : new ToolProblem($"Group Policy Management couldn't do it: {e.Message}"));
            }
        }) { IsBackground = true, Name = "Group Policy" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private const int NewCredentials = 9; // LOGON32_LOGON_NEW_CREDENTIALS: the login is used for connections out, not here
    private const int WinNt50 = 3;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LogonUserW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string user, string? domain, string password, int type, int provider, out SafeAccessTokenHandle token);
}
