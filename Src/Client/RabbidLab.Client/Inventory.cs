using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace RabbidLab.Client;

/// <summary>What this computer is and has, as the server is told: hardware, drives, software, Windows updates.</summary>
[DataContract]
internal sealed class InventoryReport
{
    [DataMember(Name = "manufacturer")] public string? Manufacturer { get; set; }
    [DataMember(Name = "model")] public string? Model { get; set; }
    [DataMember(Name = "serial")] public string? Serial { get; set; }
    [DataMember(Name = "processor")] public string? Processor { get; set; }
    [DataMember(Name = "memory")] public long Memory { get; set; }

    /// <summary>When Windows last started, as ISO 8601.</summary>
    [DataMember(Name = "started", EmitDefaultValue = false)] public string? Started { get; set; }
    [DataMember(Name = "restartNeeded")] public bool RestartNeeded { get; set; }
    [DataMember(Name = "disks")] public List<DiskReport> Disks { get; set; } = new();
    [DataMember(Name = "software")] public List<ProgramReport> Software { get; set; } = new();
    [DataMember(Name = "updates")] public List<UpdateReport> Updates { get; set; } = new();
    [DataMember(Name = "missing")] public List<UpdateReport> Missing { get; set; } = new();

    /// <summary>When Windows Update last answered what's missing, as ISO 8601; left out until it has.</summary>
    [DataMember(Name = "missingChecked", EmitDefaultValue = false)] public string? MissingChecked { get; set; }

    /// <summary>Everyone in Administrators; left out when Windows wouldn't say.</summary>
    [DataMember(Name = "admins", EmitDefaultValue = false)] public List<RabbidLab.Shared.WindowsMember>? Admins { get; set; }

    /// <summary>The computer's own accounts, without their groups.</summary>
    [DataMember(Name = "locals", EmitDefaultValue = false)] public List<RabbidLab.Shared.WindowsAccount>? Locals { get; set; }

    /// <summary>Its printers, and the printer drivers it has to add one with; left out when Windows wouldn't say.</summary>
    [DataMember(Name = "printers", EmitDefaultValue = false)] public List<PrinterReport>? Printers { get; set; }
    [DataMember(Name = "printerDrivers", EmitDefaultValue = false)] public List<string>? PrinterDrivers { get; set; }
}

[DataContract]
internal sealed class PrinterReport
{
    [DataMember(Name = "name")] public string Name { get; set; } = "";
    [DataMember(Name = "driver", EmitDefaultValue = false)] public string? Driver { get; set; }
    [DataMember(Name = "port", EmitDefaultValue = false)] public string? Port { get; set; }

    /// <summary>tcpip, lpr, ipp, wsd, share, universal, virtual or local.</summary>
    [DataMember(Name = "kind")] public string Kind { get; set; } = "local";

    /// <summary>Its IP address or name, or IPP address, when Windows says.</summary>
    [DataMember(Name = "address", EmitDefaultValue = false)] public string? Address { get; set; }
    [DataMember(Name = "shared")] public bool Shared { get; set; }
    [DataMember(Name = "offline")] public bool Offline { get; set; }
    [DataMember(Name = "location", EmitDefaultValue = false)] public string? Location { get; set; }
}

[DataContract]
internal sealed class DiskReport
{
    [DataMember(Name = "name")] public string Name { get; set; } = "";
    [DataMember(Name = "total")] public long Total { get; set; }
    [DataMember(Name = "free")] public long Free { get; set; }
}

[DataContract]
internal sealed class ProgramReport
{
    [DataMember(Name = "name")] public string Name { get; set; } = "";
    [DataMember(Name = "version", EmitDefaultValue = false)] public string? Version { get; set; }
    [DataMember(Name = "publisher", EmitDefaultValue = false)] public string? Publisher { get; set; }
    [DataMember(Name = "installed", EmitDefaultValue = false)] public string? Installed { get; set; }
}

[DataContract]
internal sealed class UpdateReport
{
    [DataMember(Name = "kb")] public string Kb { get; set; } = "";
    [DataMember(Name = "title", EmitDefaultValue = false)] public string? Title { get; set; }
    [DataMember(Name = "kind", EmitDefaultValue = false)] public string? Kind { get; set; }
    [DataMember(Name = "installed", EmitDefaultValue = false)] public string? Installed { get; set; }
}

/// <summary>
/// The computer's inventory, for the server's Devices page. Every half hour the quick things are read (hardware, drives,
/// installed software and updates, whether a restart is waiting) and sent when something changed, or six hours passed.
/// What's missing from Windows Update takes Windows a while to answer, so it's asked every six hours, on a thread of
/// its own. Reading only: nothing on the computer is changed.
/// </summary>
internal static class Inventory
{
    private static readonly TimeSpan Look = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Resend = TimeSpan.FromHours(6);
    private static DateTime looked = DateTime.MinValue;
    private static DateTime sent = DateTime.MinValue;
    private static string? sentAs; // the fingerprint of what was last sent
    private static string? sentTo; // and to which server
    private static List<UpdateReport>? missing;
    private static DateTime? missingChecked;
    private static DateTime asked = DateTime.MinValue;
    private static Task? asking;

    /// <summary>Something here just changed (a job ran): it's read and Windows Update is asked again at the next round, not when their time comes.</summary>
    public static void Soon()
    {
        looked = DateTime.MinValue;
        asked = DateTime.MinValue;
    }

    /// <summary>Run by the service after each check-in that found this computer approved.</summary>
    public static async Task Follow(CancellationToken stop)
    {
        var now = DateTime.UtcNow;
        if ((asking is null || asking.IsCompleted) && now - asked > Resend)
        {
            asked = now;
            asking = Task.Run(AskWindowsUpdate);
        }
        var settings = Settings.Read();
        if (settings.Server is null || (now - looked < Look && sentTo == settings.Server && missingChecked?.ToString("o") == sentMissing))
        {
            return;
        }
        looked = now;
        var me = Identity.Load();
        if (me is null || me.Server != settings.Server)
        {
            return;
        }
        var report = Gather();
        var print = Print(report);
        if (print == sentAs && sentTo == settings.Server && now - sent < Resend)
        {
            return;
        }
        using var api = new ServerApi(settings.Server, settings.Fingerprint, TimeSpan.FromSeconds(60));
        var (code, _) = await api.Post<CheckInAnswer>("api/clients/inventory", report, $"RabbidLab-Client {me.Id}:{me.Secret}", stop).ConfigureAwait(false);
        if (code == HttpStatusCode.OK)
        {
            (sentAs, sentTo, sent, sentMissing) = (print, settings.Server, now, report.MissingChecked);
        }
    }

    private static string? sentMissing; // the Windows Update answer that was last sent, by its time

    /// <summary>Everything quick to read, with what Windows Update last said is missing.</summary>
    public static InventoryReport Gather()
    {
        var r = new InventoryReport();
        Wmi("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem", o =>
        {
            r.Manufacturer = Text(o["Manufacturer"]);
            r.Model = Text(o["Model"]);
            r.Memory = o["TotalPhysicalMemory"] is null ? 0 : Convert.ToInt64(o["TotalPhysicalMemory"], CultureInfo.InvariantCulture);
        });
        Wmi("SELECT SerialNumber FROM Win32_BIOS", o => r.Serial = Text(o["SerialNumber"]));
        var processors = new List<string>();
        Wmi("SELECT Name FROM Win32_Processor", o => processors.Add(Text(o["Name"]) ?? "Processor"));
        r.Processor = processors.Count == 0 ? null : processors.Count == 1 ? processors[0] : $"{processors.Count} × {processors[0]}";
        Wmi("SELECT LastBootUpTime FROM Win32_OperatingSystem", o =>
        {
            if (o["LastBootUpTime"] is string at)
            {
                r.Started = ManagementDateTimeConverter.ToDateTime(at).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            }
        });
        Wmi("SELECT HotFixID, Description, InstalledOn FROM Win32_QuickFixEngineering", o =>
        {
            if (Text(o["HotFixID"]) is { } kb)
            {
                r.Updates.Add(new UpdateReport { Kb = kb, Kind = Text(o["Description"]), Installed = Day(Text(o["InstalledOn"])) });
            }
        });
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType == DriveType.Fixed && d.IsReady)
                {
                    r.Disks.Add(new DiskReport { Name = d.Name.TrimEnd('\\'), Total = d.TotalSize, Free = d.TotalFreeSpace });
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The drives read so far are sent.
        }
        r.Software = Programs();
        try
        {
            r.Admins = RabbidLab.Shared.WindowsUsers.Admins();
            r.Locals = RabbidLab.Shared.WindowsUsers.Locals();
        }
        catch (Exception)
        {
            // Sent without it: the server keeps the last list it had.
            r.Admins = null;
            r.Locals = null;
        }
        Printers(r);
        r.RestartNeeded = Exists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
            || Exists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
        if (missingChecked is { } at2 && missing is not null)
        {
            r.Missing = missing;
            r.MissingChecked = at2.ToString("o", CultureInfo.InvariantCulture);
        }
        return r;
    }

    // The printers, what kind each is (from its port and driver), and the drivers there are to add one with.
    private static void Printers(InventoryReport r)
    {
        var ports = new Dictionary<string, (string Host, int Protocol, string? Queue)>(StringComparer.OrdinalIgnoreCase);
        Wmi("SELECT Name, HostAddress, Protocol, Queue FROM Win32_TCPIPPrinterPort", o =>
        {
            if (Text(o["Name"]) is { } name)
            {
                ports[name] = (Text(o["HostAddress"]) ?? "", o["Protocol"] is null ? 1 : Convert.ToInt32(o["Protocol"], CultureInfo.InvariantCulture), Text(o["Queue"]));
            }
        });
        var printers = new List<PrinterReport>();
        Wmi("SELECT Name, DriverName, PortName, Shared, WorkOffline, Location FROM Win32_Printer", o =>
        {
            if (Text(o["Name"]) is not { } name)
            {
                return;
            }
            var p = new PrinterReport { Name = name, Driver = Text(o["DriverName"]), Port = Text(o["PortName"]), Location = Text(o["Location"]),
                Shared = o["Shared"] is true, Offline = o["WorkOffline"] is true };
            var port = p.Port ?? "";
            var driver = p.Driver ?? "";
            if (driver.IndexOf("Universal Print", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                p.Kind = "universal";
            }
            else if (ports.TryGetValue(port, out var tcp))
            {
                (p.Kind, p.Address) = (tcp.Protocol == 2 ? "lpr" : "tcpip", tcp.Host);
            }
            else if (name.StartsWith(@"\\", StringComparison.Ordinal))
            {
                p.Kind = "share";
            }
            else if (port.StartsWith("WSD", StringComparison.OrdinalIgnoreCase))
            {
                p.Kind = "wsd";
            }
            else if (port.IndexOf("://", StringComparison.Ordinal) > 0 || port.StartsWith("IPP", StringComparison.OrdinalIgnoreCase))
            {
                (p.Kind, p.Address) = ("ipp", port.IndexOf("://", StringComparison.Ordinal) > 0 ? port : null);
            }
            else if (port.EndsWith(":", StringComparison.Ordinal) || port.Length == 0 || driver.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0
                || driver.IndexOf("OneNote", StringComparison.OrdinalIgnoreCase) >= 0 || driver.IndexOf("XPS", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                p.Kind = "virtual"; // Print to PDF, OneNote, XPS, Fax and the like
            }
            printers.Add(p);
        });
        var drivers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        // Win32_PrinterDriver names read "HP Universal Printing PCL 6,3,Windows x64".
        Wmi("SELECT Name FROM Win32_PrinterDriver", o =>
        {
            if (Text(o["Name"]) is { } full)
            {
                drivers.Add(full.Split(',')[0].Trim());
            }
        });
        r.Printers = printers.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        r.PrinterDrivers = drivers.ToList();
    }

    // Installed apps as Windows lists them: for the computer (64 and 32-bit), and for each person whose profile is loaded.
    private static List<ProgramReport> Programs()
    {
        var found = new Dictionary<string, ProgramReport>(StringComparer.OrdinalIgnoreCase);
        void Read(RegistryKey? list)
        {
            if (list is null)
            {
                return;
            }
            foreach (var name in list.GetSubKeyNames())
            {
                try
                {
                    using var k = list.OpenSubKey(name);
                    if (k?.GetValue("DisplayName") is not string shown || shown.Trim().Length == 0 || (k.GetValue("SystemComponent") is int part && part == 1)
                        || k.GetValue("ParentKeyName") is string || (k.GetValue("ReleaseType") is string release && release.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        continue; // a part of Windows or of another program, or an update to one
                    }
                    var p = new ProgramReport { Name = shown.Trim(), Version = k.GetValue("DisplayVersion") as string, Publisher = k.GetValue("Publisher") as string, Installed = Day(k.GetValue("InstallDate") as string) };
                    found[$"{p.Name}\n{p.Version}"] = p;
                }
                catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // That one can't be read: the rest are listed.
                }
            }
        }
        const string where = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        try
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var list = machine.OpenSubKey(where);
                Read(list);
            }
            using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default);
            foreach (var sid in users.GetSubKeyNames().Where(s => s.StartsWith("S-1-5-21-", StringComparison.Ordinal) && !s.EndsWith("_Classes", StringComparison.Ordinal)))
            {
                using var list = users.OpenSubKey(sid + "\\" + where);
                Read(list);
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // What was read is sent.
        }
        return found.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Windows Update's id for the Definition Updates kind, the same in every language.
    private const string DefinitionUpdates = "e0789628-ce08-4437-be74-2495b842f43b";

    // Asks Windows Update what this computer is missing. It goes to Microsoft (or the WSUS server the computer uses),
    // and can take minutes. When it doesn't answer, what it said last time stands.
    private static void AskWindowsUpdate()
    {
        try
        {
            var type = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (type is null)
            {
                return;
            }
            dynamic session = Activator.CreateInstance(type);
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software'");
            var list = new List<UpdateReport>();
            foreach (dynamic update in result.Updates)
            {
                var kb = "";
                foreach (dynamic id in update.KBArticleIDs)
                {
                    kb = "KB" + (string) id;
                    break;
                }
                // An update is in several categories: its kind (Security Updates, Updates...) and the products it's for.
                string? kind = null;
                var definitions = false;
                foreach (dynamic category in update.Categories)
                {
                    if (kind is null || string.Equals((string) category.Type, "UpdateClassification", StringComparison.OrdinalIgnoreCase))
                    {
                        kind = (string) category.Name;
                    }
                    definitions |= string.Equals((string) category.CategoryID, DefinitionUpdates, StringComparison.OrdinalIgnoreCase);
                }
                if (definitions)
                {
                    continue; // antivirus definitions come out several times a day and install themselves
                }
                list.Add(new UpdateReport { Kb = kb, Title = (string) update.Title, Kind = kind });
            }
            missing = list;
            missingChecked = DateTime.UtcNow;
        }
        catch (Exception)
        {
            // Windows Update is off, unreachable or busy: asked again in six hours.
        }
    }

    // What makes one inventory different from the next, without the things that change by themselves all day: free space
    // counts to the nearest hundredth of the drive.
    private static string Print(InventoryReport r)
    {
        var all = new StringBuilder().Append(r.Manufacturer).Append('|').Append(r.Model).Append('|').Append(r.Serial).Append('|').Append(r.Processor).Append('|').Append(r.Memory)
            .Append('|').Append(r.Started).Append('|').Append(r.RestartNeeded).Append('|').Append(r.MissingChecked);
        foreach (var d in r.Disks)
        {
            all.Append('|').Append(d.Name).Append(':').Append(d.Total).Append(':').Append(d.Total > 0 ? d.Free * 100 / d.Total : 0);
        }
        foreach (var p in r.Software)
        {
            all.Append('|').Append(p.Name).Append(':').Append(p.Version);
        }
        foreach (var u in r.Updates.Concat(r.Missing))
        {
            all.Append('|').Append(u.Kb).Append(':').Append(u.Title);
        }
        foreach (var a in r.Admins ?? new List<RabbidLab.Shared.WindowsMember>())
        {
            all.Append("|a:").Append(a.Sid ?? a.Name);
        }
        foreach (var l in r.Locals ?? new List<RabbidLab.Shared.WindowsAccount>())
        {
            all.Append("|l:").Append(l.Name).Append(':').Append(l.Enabled).Append(':').Append(l.NeverExpires);
        }
        foreach (var p in r.Printers ?? new List<PrinterReport>())
        {
            all.Append("|p:").Append(p.Name).Append(':').Append(p.Port).Append(':').Append(p.Driver).Append(':').Append(p.Offline);
        }
        all.Append("|d:").Append(string.Join(",", r.PrinterDrivers ?? new List<string>()));
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(all.ToString())));
    }

    private static void Wmi(string query, Action<ManagementBaseObject> each)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (var found in searcher.Get())
            {
                using (found)
                {
                    each(found);
                }
            }
        }
        catch (Exception e) when (e is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidCastException or FormatException)
        {
            // Windows wouldn't say: that part is left out.
        }
    }

    private static string? Text(object? value) => value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } text ? text : null;

    private static bool Exists(string key)
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var found = machine.OpenSubKey(key);
            return found is not null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    // A day as Windows writes it in these places (20261114, or 11/14/2026), as 2026-11-14; anything else as it is.
    private static string? Day(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        return DateTime.TryParseExact(text!.Trim(), new[] { "yyyyMMdd", "M/d/yyyy", "MM/dd/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : text.Trim();
    }
}
