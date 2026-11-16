using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Xml.Linq;
using Microsoft.Win32;

namespace RabbidLab.Client;

/// <summary>What Group Policy did on this computer: for the computer and whoever is signed in, its errors, and the local policy it ended up with.</summary>
[DataContract]
internal sealed class GroupPolicyReport
{
    /// <summary>When this was read, as ISO 8601.</summary>
    [DataMember(Name = "checked")] public string Checked { get; set; } = "";
    [DataMember(Name = "computer", EmitDefaultValue = false)] public GpSideReport? Computer { get; set; }
    [DataMember(Name = "user", EmitDefaultValue = false)] public GpSideReport? User { get; set; }
    [DataMember(Name = "errors")] public List<GpErrorReport> Errors { get; set; } = new();
    [DataMember(Name = "local", EmitDefaultValue = false)] public LocalPolicyReport? Local { get; set; }
}

[DataContract]
internal sealed class GpSideReport
{
    /// <summary>AMW\GARAGE-PC$ for the computer, AMW\mwiles for a user.</summary>
    [DataMember(Name = "who")] public string Who { get; set; } = "";

    /// <summary>Where it is in the domain: amw.com/AMW/Computers.</summary>
    [DataMember(Name = "som", EmitDefaultValue = false)] public string? Som { get; set; }

    /// <summary>When Group Policy last finished, as ISO 8601.</summary>
    [DataMember(Name = "refreshed", EmitDefaultValue = false)] public string? Refreshed { get; set; }
    [DataMember(Name = "took", EmitDefaultValue = false)] public double Took { get; set; }
    [DataMember(Name = "slow")] public bool Slow { get; set; }
    [DataMember(Name = "dc", EmitDefaultValue = false)] public string? Dc { get; set; }
    [DataMember(Name = "applied")] public List<GpGpoReport> Applied { get; set; } = new();
    [DataMember(Name = "denied")] public List<GpGpoReport> Denied { get; set; } = new();
}

[DataContract]
internal sealed class GpGpoReport
{
    [DataMember(Name = "id")] public string Id { get; set; } = "";
    [DataMember(Name = "name")] public string Name { get; set; } = "";

    /// <summary>Where it's linked: amw.com/AMW/Computers.</summary>
    [DataMember(Name = "link", EmitDefaultValue = false)] public string? Link { get; set; }

    /// <summary>Why it didn't apply: security, wmi, disabled, link, empty, invalid.</summary>
    [DataMember(Name = "why", EmitDefaultValue = false)] public string? Why { get; set; }
}

[DataContract]
internal sealed class GpErrorReport
{
    [DataMember(Name = "when")] public string When { get; set; } = "";
    [DataMember(Name = "source")] public string Source { get; set; } = "";
    [DataMember(Name = "text")] public string Text { get; set; } = "";
    [DataMember(Name = "warning")] public bool Warning { get; set; }
}

[DataContract]
internal sealed class LocalPolicyReport
{
    [DataMember(Name = "minLength", EmitDefaultValue = false)] public int? MinLength { get; set; }

    /// <summary>Days; 0 for never.</summary>
    [DataMember(Name = "maxAge", EmitDefaultValue = false)] public int? MaxAge { get; set; }
    [DataMember(Name = "history", EmitDefaultValue = false)] public int? History { get; set; }

    /// <summary>Wrong passwords before lockout; 0 for never.</summary>
    [DataMember(Name = "lockout", EmitDefaultValue = false)] public int? Lockout { get; set; }
    [DataMember(Name = "lockoutMinutes", EmitDefaultValue = false)] public int? LockoutMinutes { get; set; }

    /// <summary>On, Off, or which profiles are on.</summary>
    [DataMember(Name = "firewall", EmitDefaultValue = false)] public string? Firewall { get; set; }

    /// <summary>Off, On, or On with NLA.</summary>
    [DataMember(Name = "rdp", EmitDefaultValue = false)] public string? Rdp { get; set; }
    [DataMember(Name = "uac", EmitDefaultValue = false)] public bool? Uac { get; set; }
    [DataMember(Name = "smb1", EmitDefaultValue = false)] public bool? Smb1 { get; set; }

    /// <summary>Sign-in auditing: Success, Failure, Success and Failure, or No auditing.</summary>
    [DataMember(Name = "auditLogon", EmitDefaultValue = false)] public string? AuditLogon { get; set; }
}

/// <summary>
/// Group Policy's results, read with gpresult (as SYSTEM, for the computer and for each person signed in), its errors and
/// warnings from the event logs since it last ran, and the local policy Windows uses now. Reading only.
/// </summary>
internal static class GroupPolicy
{
    private const string Infrastructure = "{00000000-0000-0000-0000-000000000000}";
    private const int Most = 60;

    public static GroupPolicyReport Read()
    {
        var r = new GroupPolicyReport { Checked = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) };
        r.Computer = Side("computer", null);
        var user = (Machine.Users() ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim())
            .FirstOrDefault(u => u.Contains('\\') && !u.StartsWith(Environment.MachineName + "\\", StringComparison.OrdinalIgnoreCase));
        if (user is not null)
        {
            r.User = Side("user", user);
        }
        if (r.Computer is not null && Dc() is { } dc)
        {
            r.Computer.Dc = dc;
        }
        var since = new[] { r.Computer?.Refreshed, r.User?.Refreshed }.Select(Iso).Where(d => d is not null).Select(d => d!.Value).DefaultIfEmpty(DateTime.UtcNow.AddHours(-2)).Max();
        r.Errors = Errors(since.AddMinutes(-10));
        r.Local = Local();
        return r;
    }

    // One side's results from gpresult's XML.
    private static GpSideReport? Side(string scope, string? user)
    {
        var file = Path.Combine(Folder.Data, $"gp-{scope}.xml");
        try
        {
            var args = user is null ? $"/scope computer /x \"{file}\" /f" : $"/user \"{user}\" /scope user /x \"{file}\" /f";
            if (!Run(Tool("gpresult.exe"), args, TimeSpan.FromSeconds(90)) || !File.Exists(file))
            {
                // A person who signed in without Group Policy data (a local account, say) has none to read: only the computer's is a problem.
                if (user is null)
                {
                    Problems.Note("group policy", "gpresult couldn't read the computer's Group Policy results.");
                }
                return null;
            }
            var root = XDocument.Load(file).Root;
            var results = Kid(root, user is null ? "ComputerResults" : "UserResults");
            if (results is null)
            {
                return null;
            }
            var side = new GpSideReport { Who = Text(Kid(results, "Name")) ?? user ?? Environment.MachineName, Som = Text(Kid(results, "SOM")), Slow = Text(Kid(results, "SlowLink")) == "true" };
            var exts = Kids(results, "ExtensionStatus").ToList();
            var infra = exts.FirstOrDefault(e => string.Equals(Text(Kid(e, "Identifier")), Infrastructure, StringComparison.OrdinalIgnoreCase));
            if (Time(Text(Kid(infra, "BeginTime"))) is { } begin)
            {
                var ends = exts.Select(e => (Begin: Time(Text(Kid(e, "BeginTime"))), End: Time(Text(Kid(e, "EndTime"))))).Where(x => x.Begin >= begin && x.End is not null).Select(x => x.End!.Value);
                var end = ends.DefaultIfEmpty(begin).Max();
                side.Refreshed = end.ToString("o", CultureInfo.InvariantCulture);
                side.Took = Math.Round(Math.Max(0, (end - begin).TotalSeconds), 1);
            }
            foreach (var g in Kids(results, "GPO"))
            {
                var id = Text(Kid(Kid(g, "Path"), "Identifier")) ?? "";
                if (id.Length == 0 || id == "LocalGPO" && Text(Kid(Kid(g, "Link"), "AppliedOrder")) == "0")
                {
                    continue; // the computer's own Local Group Policy, when it sets nothing
                }
                var link = Kid(g, "Link");
                var report = new GpGpoReport { Id = id.ToUpperInvariant(), Name = Text(Kid(g, "Name")) ?? id, Link = Text(Kid(link, "SOMPath")) };
                if (Text(Kid(link, "AppliedOrder")) is { } order && order != "0")
                {
                    side.Applied.Add(report);
                    continue;
                }
                report.Why = Text(Kid(g, "AccessDenied")) == "true" ? "security"
                    : Text(Kid(g, "FilterAllowed")) == "false" && Text(Kid(g, "IsValid")) != "false" ? "wmi"
                    : Text(Kid(link, "Enabled")) == "false" ? "link"
                    : Text(Kid(g, "Enabled")) == "false" ? "disabled"
                    : Text(Kid(g, "IsValid")) == "false" ? "empty"
                    : "empty";
                side.Denied.Add(report);
            }
            side.Applied = side.Applied.Take(Most).ToList();
            side.Denied = side.Denied.Take(Most).ToList();
            return side;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Problems.Note("group policy", $"Couldn't read Group Policy's results for the {scope}: {e.Message}");
            return null;
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Overwritten next time.
            }
        }
    }

    // Errors and warnings Group Policy and its preference parts wrote since it last ran, newest first, each said once.
    private static List<GpErrorReport> Errors(DateTime since)
    {
        var found = new List<GpErrorReport>();
        var ms = (long) Math.Max(60000, Math.Min((DateTime.UtcNow - since).TotalMilliseconds, 86400000));
        var query = $"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= {ms}]]]";
        void From(string log, Func<string, bool> mine)
        {
            try
            {
                using var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, query) { ReverseDirection = true });
                var read = 0;
                for (var e = reader.ReadEvent(); e is not null && read < 300; e = reader.ReadEvent(), read++)
                {
                    using (e)
                    {
                        var source = e.ProviderName ?? "";
                        if (!mine(source))
                        {
                            continue;
                        }
                        string text;
                        try
                        {
                            text = e.FormatDescription() ?? $"Event {e.Id}";
                        }
                        catch (EventLogException)
                        {
                            text = $"Event {e.Id}";
                        }
                        text = string.Join(" ", text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
                        found.Add(new GpErrorReport
                        {
                            When = (e.TimeCreated ?? DateTime.Now).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                            Source = source.StartsWith("Group Policy ", StringComparison.Ordinal) ? source.Substring(13) : "Group Policy",
                            Text = text.Length > 400 ? text.Substring(0, 400) + "…" : text,
                            Warning = e.Level == 3,
                        });
                    }
                }
            }
            catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
            {
                // That log can't be read here: the other still is.
            }
        }
        From("Microsoft-Windows-GroupPolicy/Operational", _ => true);
        From("Application", s => s.StartsWith("Group Policy", StringComparison.OrdinalIgnoreCase));
        return found.GroupBy(f => f.Text).Select(g => g.First()).OrderByDescending(f => f.When, StringComparer.Ordinal).Take(10).ToList();
    }

    // --- The local policy Windows uses now ------------------------------------------------------------------------

    private static LocalPolicyReport Local()
    {
        var l = new LocalPolicyReport();
        if (NetUserModalsGet(null, 0, out var zero) == 0)
        {
            try
            {
                var m = (Modals0) Marshal.PtrToStructure(zero, typeof(Modals0));
                l.MinLength = m.MinLength;
                l.MaxAge = m.MaxAge == uint.MaxValue ? 0 : (int) (m.MaxAge / 86400);
                l.History = m.History;
            }
            finally
            {
                NetApiBufferFree(zero);
            }
        }
        if (NetUserModalsGet(null, 3, out var three) == 0)
        {
            try
            {
                var m = (Modals3) Marshal.PtrToStructure(three, typeof(Modals3));
                l.Lockout = m.Threshold;
                l.LockoutMinutes = m.Duration == uint.MaxValue ? 0 : (int) (m.Duration / 60);
            }
            finally
            {
                NetApiBufferFree(three);
            }
        }
        // Each profile: what Group Policy says when it says, else the computer's own setting.
        var profiles = new[] { ("Domain", "DomainProfile", "DomainProfile"), ("Private", "PrivateProfile", "StandardProfile"), ("Public", "PublicProfile", "PublicProfile") };
        var on = new List<string>();
        var known = 0;
        foreach (var (name, policy, local) in profiles)
        {
            var v = Dword($@"SOFTWARE\Policies\Microsoft\WindowsFirewall\{policy}", "EnableFirewall")
                ?? Dword($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{local}", "EnableFirewall");
            if (v is null)
            {
                continue;
            }
            known++;
            if (v == 1)
            {
                on.Add(name);
            }
        }
        l.Firewall = known == 0 ? null : on.Count == known ? "On" : on.Count == 0 ? "Off" : $"On for {string.Join(", ", on)}";
        var deny = Dword(@"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services", "fDenyTSConnections") ?? Dword(@"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections");
        var nla = Dword(@"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services", "UserAuthentication") ?? Dword(@"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp", "UserAuthentication");
        l.Rdp = deny is null ? null : deny == 1 ? "Off" : nla == 1 ? "On, with NLA" : "On";
        l.Uac = Dword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA") is { } lua ? lua != 0 : null;
        try
        {
            using var s = new ManagementObjectSearcher(new ManagementScope(@"root\Microsoft\Windows\SMB"), new ObjectQuery("SELECT EnableSMB1Protocol FROM MSFT_SmbServerConfiguration"));
            foreach (ManagementObject o in s.Get())
            {
                using (o)
                {
                    l.Smb1 = o["EnableSMB1Protocol"] is bool b ? b : null;
                }
            }
        }
        catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException)
        {
            // Older Windows: left out.
        }
        l.AuditLogon = AuditLogon();
        return l;
    }

    // The Logon subcategory, as auditpol says it in its CSV.
    private static string? AuditLogon()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Tool("auditpol.exe"), "/get /subcategory:{0CCE9215-69AE-11D9-BED3-505054503030} /r")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
            if (p is null)
            {
                return null;
            }
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(15000))
            {
                p.Kill();
                return null;
            }
            var line = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault();
            var cells = line?.Split(',');
            return cells is { Length: >= 5 } ? cells[4].Trim() switch
            {
                "Success and Failure" => "Success and Failure",
                "Success" => "Success",
                "Failure" => "Failure",
                _ => "No auditing",
            } : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    // --- Small parts ------------------------------------------------------------------------------------------------

    private static string? Dc()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\History");
        return key?.GetValue("DCName") is string dc && dc.Trim('\\').Length > 0 ? dc.Trim('\\') : null;
    }

    private static int? Dword(string path, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key?.GetValue(name) is int v ? v : null;
    }

    private static bool Run(string tool, string args, TimeSpan most)
    {
        using var p = Process.Start(new ProcessStartInfo(tool, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
        if (p is null)
        {
            return false;
        }
        p.OutputDataReceived += (_, _) => { };
        p.ErrorDataReceived += (_, _) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit((int) most.TotalMilliseconds))
        {
            try
            {
                p.Kill();
            }
            catch (InvalidOperationException)
            {
                // Finished just then.
            }
            return false;
        }
        return p.ExitCode == 0;
    }

    // The 64-bit tool from a 32-bit process too.
    private static string Tool(string name)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var native = Path.Combine(windows, "Sysnative", name);
        return File.Exists(native) ? native : Path.Combine(windows, "System32", name);
    }

    private static XElement? Kid(XElement? e, string name) => e?.Elements().FirstOrDefault(k => k.Name.LocalName == name);

    private static IEnumerable<XElement> Kids(XElement? e, string name) => e?.Elements().Where(k => k.Name.LocalName == name) ?? Enumerable.Empty<XElement>();

    private static string? Text(XElement? e) => e is null ? null : e.Value.Trim() is { Length: > 0 } v ? v : null;

    // gpresult's times are UTC without saying so.
    private static DateTime? Time(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) && t.Year > 2000 ? t : null;

    private static DateTime? Iso(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct Modals0
    {
        public int MinLength;
        public uint MaxAge;
        public uint MinAge;
        public uint ForceLogoff;
        public int History;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Modals3
    {
        public uint Duration;
        public uint Window;
        public int Threshold;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserModalsGet(string? server, int level, out IntPtr buffer);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
