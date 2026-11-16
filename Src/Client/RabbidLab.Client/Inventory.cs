using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WakeOnLan = RabbidLab.Shared.WakeOnLan;

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

    /// <summary>Its network cards, for Wake-on-LAN; left out when Windows wouldn't say.</summary>
    [DataMember(Name = "nics", EmitDefaultValue = false)] public List<NicReport>? Nics { get; set; }

    /// <summary>What Windows Update installed, by name; left out until it's answered once.</summary>
    [DataMember(Name = "history", EmitDefaultValue = false)] public List<HistoryReport>? History { get; set; }

    /// <summary>Fast startup is on (Shut down isn't a real one, and many cards then don't wake it); left out when Windows wouldn't say.</summary>
    [DataMember(Name = "fastStartup", EmitDefaultValue = false)] public bool? FastStartup { get; set; }

    /// <summary>What Group Policy did here; left out until it's been read once.</summary>
    [DataMember(Name = "groupPolicy", EmitDefaultValue = false)] public GroupPolicyReport? GroupPolicy { get; set; }
}

[DataContract]
internal sealed class HistoryReport
{
    [DataMember(Name = "title")] public string Title { get; set; } = "";
    [DataMember(Name = "kb", EmitDefaultValue = false)] public string Kb { get; set; } = "";

    /// <summary>When, as ISO 8601.</summary>
    [DataMember(Name = "date")] public string Date { get; set; } = "";

    /// <summary>installed, failed, cancelled or pending.</summary>
    [DataMember(Name = "result")] public string Result { get; set; } = "installed";
}

[DataContract]
internal sealed class NicReport
{
    [DataMember(Name = "name")] public string Name { get; set; } = "";

    /// <summary>As AA:BB:CC:DD:EE:FF.</summary>
    [DataMember(Name = "mac")] public string Mac { get; set; } = "";

    /// <summary>wired or wifi.</summary>
    [DataMember(Name = "kind")] public string Kind { get; set; } = "wired";
    [DataMember(Name = "ip", EmitDefaultValue = false)] public string? Ip { get; set; }
    [DataMember(Name = "prefix", EmitDefaultValue = false)] public int Prefix { get; set; }
    [DataMember(Name = "up")] public bool Up { get; set; }

    /// <summary>Windows lets it wake the computer (powercfg's wake_armed list); left out when that couldn't be read.</summary>
    [DataMember(Name = "wake", EmitDefaultValue = false)] public bool? Wake { get; set; }

    /// <summary>The card's own wake-from-shut-down switch is on (Shutdown Wake-On-Lan and the like); left out when it has none.</summary>
    [DataMember(Name = "wakeOff", EmitDefaultValue = false)] public bool? WakeOff { get; set; }
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

    /// <summary>Antivirus definitions: listed as Windows lists them, but they come several times a day, so not counted as missing.</summary>
    [DataMember(Name = "definition", EmitDefaultValue = false)] public bool Definition { get; set; }
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
    private static List<HistoryReport>? history;
    private static List<string>? misses; // what WMI wouldn't read during this Gather, with why
    private static DateTime asked = DateTime.MinValue;
    private static Task? asking;
    private static readonly TimeSpan PolicyEvery = TimeSpan.FromHours(2);
    private static GroupPolicyReport? policy;
    private static DateTime policyAsked = DateTime.MinValue;
    private static Task? policyAsking;
    private static string? sentPolicy; // the Group Policy read that was last sent, by its time

    /// <summary>Something here just changed (a job ran): it's read and Windows Update is asked again at the next round, not when their time comes.</summary>
    public static void Soon()
    {
        looked = DateTime.MinValue;
        asked = DateTime.MinValue;
        policyAsked = DateTime.MinValue;
    }

    /// <summary>Run by the service after each check-in that found this computer approved.</summary>
    public static async Task Follow(CancellationToken stop)
    {
        var now = DateTime.UtcNow;
        if (now - asked > Resend)
        {
            // Windows Update can hang on a question for good. One still unanswered after six hours is given up on and said,
            // so the answer on the server doesn't quietly go stale.
            if (asking is { IsCompleted: false })
            {
                Problems.Note("windows-update", "Windows Update was asked what's missing six hours ago and never answered. Asking again.");
            }
            asked = now;
            asking = Task.Run(AskWindowsUpdate);
        }
        // gpresult takes a few seconds, so Group Policy is read on a thread of its own and sent once it's in.
        if ((policyAsking is null || policyAsking.IsCompleted) && now - policyAsked > PolicyEvery)
        {
            policyAsked = now;
            policyAsking = Task.Run(() =>
            {
                try
                {
                    policy = GroupPolicy.Read();
                }
                catch (Exception e)
                {
                    Problems.Note("group policy", $"Couldn't read Group Policy: {e.Message}");
                }
            });
        }
        var settings = Settings.Read();
        if (settings.Server is null || (now - looked < Look && sentTo == settings.Server && missingChecked?.ToString("o") == sentMissing && policy?.Checked == sentPolicy))
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
            (sentAs, sentTo, sent, sentMissing, sentPolicy) = (print, settings.Server, now, report.MissingChecked, report.GroupPolicy?.Checked);
        }
        else
        {
            Problems.Note("inventory", $"The server didn't take the inventory (HTTP {(int) code}).");
        }
    }

    private static string? sentMissing; // the Windows Update answer that was last sent, by its time

    /// <summary>Everything quick to read, with what Windows Update last said is missing.</summary>
    public static InventoryReport Gather()
    {
        misses = new List<string>();
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
        Nics(r);
        r.FastStartup = FastStartup();
        r.RestartNeeded = Exists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
            || Exists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
        if (missingChecked is { } at2 && missing is not null)
        {
            r.Missing = missing;
            r.MissingChecked = at2.ToString("o", CultureInfo.InvariantCulture);
        }
        r.History = history;
        r.GroupPolicy = policy;
        if (misses.Count > 0)
        {
            Problems.Note("inventory", $"Couldn't read {misses.Count} part(s) of the inventory. {misses[0]}");
        }
        misses = null;
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
    // The network cards in the computer (not VPN, Hyper-V or Bluetooth ones), wired first: what Wake-on-LAN needs.
    private static void Nics(InventoryReport r)
    {
        // The cards Windows calls hardware, as Get-NetAdapter -Physical does, by interface index: 14 is Ethernet, 9 Wi-Fi.
        var hardware = new Dictionary<int, int>();
        var asked = false;
        Wmi(@"root\StandardCimv2", "SELECT InterfaceIndex, HardwareInterface, NdisPhysicalMedium FROM MSFT_NetAdapter", o =>
        {
            asked = true;
            if (o["HardwareInterface"] is true && o["InterfaceIndex"] is { } index)
            {
                hardware[Convert.ToInt32(index, CultureInfo.InvariantCulture)] = o["NdisPhysicalMedium"] is { } m ? Convert.ToInt32(m, CultureInfo.InvariantCulture) : 0;
            }
        });
        var armed = WakeArmed();
        var cards = new List<NicReport>();
        try
        {
            foreach (var card in NetworkInterface.GetAllNetworkInterfaces())
            {
                var props = card.GetIPProperties();
                int index;
                try
                {
                    index = props.GetIPv4Properties()?.Index ?? -1;
                }
                catch (NetworkInformationException)
                {
                    index = -1;
                }
                var medium = hardware.TryGetValue(index, out var m2) ? m2 : -1;
                var wifi = medium == 9 || card.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                var wired = medium is 0 or 14 || (!asked && card.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet);
                if ((asked && medium < 0) || !(wifi || wired) || Virtual(card.Description))
                {
                    continue;
                }
                if (WakeOnLan.Mac(card.GetPhysicalAddress().ToString()) is not { } mac)
                {
                    continue;
                }
                var v4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal));
                cards.Add(new NicReport
                {
                    Name = card.Name, Mac = WakeOnLan.Text(mac), Kind = wifi ? "wifi" : "wired", Ip = v4?.Address.ToString(), Prefix = v4 is null ? 0 : WakeOnLan.Prefix(v4) ?? 0,
                    Up = card.OperationalStatus == OperationalStatus.Up, Wake = armed?.Contains(card.Description), WakeOff = wifi ? null : WakesFromOff(card.Name),
                });
            }
        }
        catch (NetworkInformationException)
        {
            r.Nics = null; // the server keeps the cards it had
            return;
        }
        r.Nics = cards.OrderBy(c => c.Kind == "wifi").ThenBy(c => !c.Up).Take(WakeOnLan.Most).ToList();
    }

    // The card's own switch for waking from shut down, which each maker names its own way; null when it has none or
    // Windows wouldn't say. Read only: the built-in script Turn on Wake-on-LAN changes it.
    private static bool? WakesFromOff(string name)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\StandardCimv2",
                $"SELECT DisplayName, DisplayValue FROM MSFT_NetAdapterAdvancedPropertySettingData WHERE Name = '{name.Replace("\\", "\\\\").Replace("'", "\\'")}'");
            foreach (var found in searcher.Get())
            {
                using (found)
                {
                    var label = Text(found["DisplayName"]) ?? "";
                    if (System.Text.RegularExpressions.Regex.IsMatch(label, "shutdown wake|wake from (power off|shutdown)|power off state", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                        && label.IndexOf("speed", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return System.Text.RegularExpressions.Regex.IsMatch(Text(found["DisplayValue"]) ?? "", "^(enabled|on)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }
                }
            }
            return null;
        }
        catch (Exception e) when (e is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Fast startup: on when Windows has it switched on and hibernation, which it needs, isn't off.
    private static bool? FastStartup()
    {
        try
        {
            using var power = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
            using var hibernate = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            if (power?.GetValue("HiberbootEnabled") is not int boot)
            {
                return null;
            }
            return boot == 1 && (hibernate?.GetValue("HibernateEnabled") is not int h || h != 0);
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool Virtual(string? description) =>
        new[] { "virtual", "hyper-v", "vpn", "tap-", "bluetooth", "loopback", "miniport", "vmware", "virtualbox" }.Any(w => (description ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

    // The devices Windows lets wake the computer (Allow this device to wake the computer), by name; null when powercfg couldn't say.
    private static HashSet<string>? WakeArmed()
    {
        try
        {
            using var run = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), "/devicequery wake_armed")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            });
            if (run is null)
            {
                return null;
            }
            var said = run.StandardOutput.ReadToEndAsync();
            if (!run.WaitForExit(10000) || !said.Wait(1000) || run.ExitCode != 0)
            {
                return null;
            }
            return new HashSet<string>(said.Result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

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

    // And for Upgrades: a new version of Windows.
    private const string Upgrades = "3689bdc8-b205-4af4-8d4a-a63924c5e9d5";

    // The Microsoft Update service: Windows Update with Microsoft's other products.
    private const string MicrosoftUpdate = "7971f918-a847-4430-9279-4a52d1efe18d";

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
            // Windows keeps several update services (Windows Update, Microsoft Update, the one drivers come from) and its
            // own Update page puts their answers together. Asking only the usual one gave a server one update where its
            // page showed five, and each registered one still missed three. So each is asked, and Microsoft Update by its id,
            // and Windows Server's own scan (the one sconfig uses), and an update counts once (DeviceJobs.FindUpdates does the same).
            var services = new List<object>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Type.GetTypeFromProgID("Microsoft.Update.ServiceManager") is { } managerType)
                {
                    dynamic manager = Activator.CreateInstance(managerType);
                    foreach (dynamic service in manager.Services)
                    {
                        services.Add(service);
                        ids.Add((string) service.ServiceID);
                    }
                }
            }
            catch (Exception)
            {
                // The services can't be listed here: the usual one is still asked.
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<UpdateReport>();
            var answered = 0;
            Exception? failed = null;
            var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Ask(string? service)
            {
                dynamic searcher = session.CreateUpdateSearcher();
                if (service is not null)
                {
                    searcher.ServerSelection = 3; // another service, named by its id
                    searcher.ServiceID = service;
                }
                // Everything that isn't installed, as the Update page asks: software, and drivers too.
                dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0");
                foreach (dynamic update in result.Updates)
                {
                    // Seen by name too, so Windows' own scan below doesn't count it a second time.
                    titles.Add((string) update.Title);
                    // Windows marks each update as one it puts in itself (1) or an optional one (4), and its page keeps the
                    // optional ones apart under Optional updates: not missing. AutoSelectOnWebSites isn't that mark: a
                    // driver on the main list had it off, and was left out.
                    if (!seen.Add((string) update.Identity.UpdateID) || (int) update.DeploymentAction == 4)
                    {
                        continue;
                    }
                    var kb = "";
                    foreach (dynamic id in update.KBArticleIDs)
                    {
                        kb = "KB" + (string) id;
                        break;
                    }
                    // An update is in several categories: its kind (Security Updates, Updates...) and the products it's for.
                    string? kind = null;
                    var (definitions, upgrade) = (false, false);
                    foreach (dynamic category in update.Categories)
                    {
                        if (kind is null || string.Equals((string) category.Type, "UpdateClassification", StringComparison.OrdinalIgnoreCase))
                        {
                            kind = (string) category.Name;
                        }
                        definitions |= string.Equals((string) category.CategoryID, DefinitionUpdates, StringComparison.OrdinalIgnoreCase);
                        upgrade |= string.Equals((string) category.CategoryID, Upgrades, StringComparison.OrdinalIgnoreCase);
                    }
                    if (upgrade)
                    {
                        continue; // a new version of Windows is its own decision, not a missing update
                    }
                    // Antivirus definitions come out several times a day: listed, as Windows lists them, and marked so they aren't counted.
                    list.Add(new UpdateReport { Kb = kb, Title = (string) update.Title, Kind = kind, Definition = definitions });
                }
                answered++;
            }
            // The usual service, each registered one, and Microsoft Update whether it's registered or not.
            var sources = new List<string?> { null };
            sources.AddRange(ids);
            if (!ids.Contains(MicrosoftUpdate))
            {
                sources.Add(MicrosoftUpdate);
            }
            foreach (var source in sources)
            {
                try
                {
                    Ask(source);
                }
                catch (Exception e)
                {
                    failed ??= e; // one service that won't answer doesn't lose the others
                }
            }
            // Windows Server's own scan, where the computer has it: what it lists that none of those did. It gives a title
            // and a KB number and no more, so definitions are told by their name.
            try
            {
                using var scan = new ManagementClass(@"root\Microsoft\Windows\WindowsUpdate", "MSFT_WUOperations", null);
                using var ask = scan.GetMethodParameters("ScanForUpdates");
                ask["SearchCriteria"] = "IsInstalled=0";
                using var found = scan.InvokeMethod("ScanForUpdates", ask, new InvokeMethodOptions { Timeout = TimeSpan.FromMinutes(15) });
                foreach (var one in found?["Updates"] as ManagementBaseObject[] ?? new ManagementBaseObject[0])
                {
                    using (one)
                    {
                        var title = one["Title"] as string;
                        var id = one["UpdateID"] as string;
                        if (string.IsNullOrWhiteSpace(title) || (id is not null && !seen.Add(id)) || !titles.Add(title!))
                        {
                            continue;
                        }
                        var number = one["KBArticleID"] as string;
                        list.Add(new UpdateReport
                        {
                            Kb = string.IsNullOrEmpty(number) ? "" : "KB" + number, Title = title,
                            Definition = title!.IndexOf("Security Intelligence Update", StringComparison.OrdinalIgnoreCase) >= 0
                                || title.IndexOf("Definition Update", StringComparison.OrdinalIgnoreCase) >= 0,
                        });
                    }
                }
                answered++;
            }
            catch (Exception e) when (e is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidCastException)
            {
                // Not on this edition of Windows (it's Windows Server's), or it wouldn't answer: the services' answers stand.
            }
            if (answered == 0)
            {
                throw failed ?? new InvalidOperationException("Windows Update has no service to ask.");
            }
            missing = list.OrderBy(u => u.Definition).ThenBy(u => u.Title, StringComparer.OrdinalIgnoreCase).ToList();
            missingChecked = DateTime.UtcNow;
            history = History(session.CreateUpdateSearcher());
        }
        catch (Exception e)
        {
            // Windows Update is off, unreachable or busy: the server is told, and it's asked again in half an hour, not six.
            Problems.Note("windows-update", $"Windows Update didn't answer: {e.Message.Trim()} (0x{e.HResult:X8})");
            asked = DateTime.UtcNow - Resend + TimeSpan.FromMinutes(30);
        }
    }

    // What Windows Update installed, by name (Installed has only KB numbers): newest first, the latest try of each
    // update, without antivirus definitions, which come several times a day.
    private static List<HistoryReport>? History(dynamic searcher)
    {
        try
        {
            var total = (int) searcher.GetTotalHistoryCount();
            if (total <= 0)
            {
                return new List<HistoryReport>();
            }
            var all = new List<HistoryReport>();
            foreach (dynamic entry in searcher.QueryHistory(0, Math.Min(total, 300)))
            {
                var title = Text((object) entry.Title);
                if (title is null || (int) entry.Operation != 1 || title.IndexOf("Security Intelligence Update", StringComparison.OrdinalIgnoreCase) >= 0
                    || title.IndexOf("Definition Update", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                var result = (int) entry.ResultCode;
                all.Add(new HistoryReport
                {
                    Title = title, Kb = System.Text.RegularExpressions.Regex.Match(title, @"KB\d{6,8}").Value,
                    Date = ((DateTime) entry.Date).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    Result = result is 2 or 3 ? "installed" : result == 4 ? "failed" : result == 5 ? "cancelled" : "pending",
                });
            }
            return all.OrderByDescending(h => h.Date, StringComparer.Ordinal).GroupBy(h => h.Kb.Length > 0 ? h.Kb : h.Title).Select(g => g.First()).Take(100).ToList();
        }
        catch (Exception)
        {
            return null; // the last history sent stays
        }
    }

    // What makes one inventory different from the next, without the things that change by themselves all day: free space
    // counts to the nearest hundredth of the drive.
    private static string Print(InventoryReport r)
    {
        var all = new StringBuilder().Append(r.Manufacturer).Append('|').Append(r.Model).Append('|').Append(r.Serial).Append('|').Append(r.Processor).Append('|').Append(r.Memory).Append('|').Append(r.FastStartup)
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
        foreach (var n in r.Nics ?? new List<NicReport>())
        {
            all.Append("|n:").Append(n.Mac).Append(':').Append(n.Ip).Append(':').Append(n.Up).Append(':').Append(n.Wake).Append(':').Append(n.WakeOff);
        }
        foreach (var h in r.History ?? new List<HistoryReport>())
        {
            all.Append("|h:").Append(h.Title).Append(':').Append(h.Result);
        }
        all.Append("|g:").Append(r.GroupPolicy?.Checked);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(all.ToString())));
    }

    private static void Wmi(string query, Action<ManagementBaseObject> each) => Wmi(@"root\cimv2", query, each);

    private static void Wmi(string space, string query, Action<ManagementBaseObject> each)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(space, query);
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
            // Windows wouldn't say: that part is left out, and the server hears what couldn't be read.
            var from = System.Text.RegularExpressions.Regex.Match(query, @"FROM\s+(\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
            misses?.Add($"{from}: {e.Message.Trim()}");
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
