using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>A drive on a computer: its letter, its size and what's free, in bytes.</summary>
public sealed class DeviceDisk
{
    public string Name { get; set; } = "";
    public long Total { get; set; }
    public long Free { get; set; }
}

/// <summary>A program installed on a computer, as Windows lists it under Installed apps.</summary>
public sealed class DeviceProgram
{
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public string? Installed { get; set; }
}

/// <summary>A printer on a computer: its kind (tcpip, lpr, ipp, wsd, share, universal, virtual or local) and address when Windows says.</summary>
public sealed class DevicePrinter
{
    public string Name { get; set; } = "";
    public string? Driver { get; set; }
    public string? Port { get; set; }
    public string Kind { get; set; } = "local";
    public string? Address { get; set; }
    public bool Shared { get; set; }
    public bool Offline { get; set; }
    public string? Location { get; set; }
}

/// <summary>A network card on a computer, for Wake-on-LAN: its MAC, wired or wifi, its IPv4 address and prefix when it had one.</summary>
public sealed class DeviceNic
{
    public string Name { get; set; } = "";
    public string Mac { get; set; } = "";
    public string Kind { get; set; } = "wired";
    public string? Ip { get; set; }
    public int Prefix { get; set; }
    public bool Up { get; set; }

    /// <summary>Windows lets it wake the computer; null when the computer couldn't say.</summary>
    public bool? Wake { get; set; }

    /// <summary>The card's own wake-from-shut-down switch is on; null when it has none, or the client is before 1.1.204.</summary>
    public bool? WakeOff { get; set; }
}

/// <summary>An update in Windows Update's history: its name, KB, when, and installed, failed, cancelled or pending.</summary>
public sealed class DeviceHistory
{
    public string Title { get; set; } = "";
    public string? Kb { get; set; }
    public DateTime? Date { get; set; }
    public string Result { get; set; } = "installed";
}

/// <summary>A Windows update: installed on the computer (with the day), or missing from it.</summary>
public sealed class DeviceUpdate
{
    public string Kb { get; set; } = "";
    public string? Title { get; set; }
    public string? Kind { get; set; }
    public string? Installed { get; set; }

    /// <summary>Antivirus definitions: listed, as Windows lists them, but they come several times a day, so they aren't counted as missing.</summary>
    public bool Definition { get; set; }
}

/// <summary>What Group Policy did on a computer: for the computer and the person signed in, its errors, and the local policy it ended up with.</summary>
public sealed class DeviceGroupPolicy
{
    public DateTime? Checked { get; set; }
    public DeviceGpSide? Computer { get; set; }
    public DeviceGpSide? User { get; set; }
    public List<DeviceGpError> Errors { get; set; } = [];
    public DeviceLocalPolicy? Local { get; set; }
}

/// <summary>One side's results: who, where it is in the domain, when Group Policy last finished and how long it took, what applied and what didn't.</summary>
public sealed class DeviceGpSide
{
    public string Who { get; set; } = "";
    public string? Som { get; set; }
    public DateTime? Refreshed { get; set; }
    public double Took { get; set; }
    public bool Slow { get; set; }
    public string? Dc { get; set; }
    public List<DeviceGpo> Applied { get; set; } = [];
    public List<DeviceGpo> Denied { get; set; } = [];
}

/// <summary>A GPO in a computer's results. Why it didn't apply: security, wmi, disabled, link or empty.</summary>
public sealed class DeviceGpo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Link { get; set; }
    public string? Why { get; set; }
}

public sealed class DeviceGpError
{
    public DateTime? When { get; set; }
    public string Source { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Warning { get; set; }
}

/// <summary>The local policy Windows uses now: password and lockout rules, firewall, Remote Desktop, UAC, SMBv1, sign-in auditing.</summary>
public sealed class DeviceLocalPolicy
{
    public int? MinLength { get; set; }
    public int? MaxAge { get; set; }
    public int? History { get; set; }
    public int? Lockout { get; set; }
    public int? LockoutMinutes { get; set; }
    public string? Firewall { get; set; }
    public string? Rdp { get; set; }
    public bool? Uac { get; set; }
    public bool? Smb1 { get; set; }
    public string? AuditLogon { get; set; }
}

/// <summary>
/// What a computer's client found on it: the hardware, the drives, the installed software, and the Windows updates
/// installed and missing. The client sends it when it changes, and every few hours.
/// </summary>
public sealed class DeviceInventory
{
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Serial { get; set; }
    public string? Processor { get; set; }

    /// <summary>Memory, in bytes.</summary>
    public long Memory { get; set; }

    /// <summary>When Windows last started.</summary>
    public DateTime? Started { get; set; }

    /// <summary>Windows is waiting for a restart, to finish installing something.</summary>
    public bool RestartNeeded { get; set; }

    public List<DeviceDisk> Disks { get; set; } = [];
    public List<DeviceProgram> Software { get; set; } = [];
    public List<DeviceUpdate> Updates { get; set; } = [];
    public List<DeviceUpdate> Missing { get; set; } = [];

    /// <summary>When Windows Update last answered what's missing; null when it hasn't, so Missing says nothing.</summary>
    public DateTime? MissingChecked { get; set; }

    /// <summary>Everyone in the computer's Administrators group; null until a client from 1.1.183 on sends it.</summary>
    public List<WindowsMember>? Admins { get; set; }

    /// <summary>The computer's own accounts, without their groups: for Local admins' State and Last sign-in.</summary>
    public List<WindowsAccount>? Locals { get; set; }

    /// <summary>Its printers, and the printer drivers it has; null until a client from 1.1.190 on sends them.</summary>
    public List<DevicePrinter>? Printers { get; set; }
    public List<string>? PrinterDrivers { get; set; }

    /// <summary>When Admins was last read on the computer.</summary>
    public DateTime? AdminsChecked { get; set; }

    /// <summary>Its network cards; null until a client from 1.1.195 on sends them. Kept while it's off, which is when Wake needs them.</summary>
    public List<DeviceNic>? Nics { get; set; }

    /// <summary>What Windows Update installed, by name, newest first; null until a client from 1.1.203 on sends it.</summary>
    public List<DeviceHistory>? History { get; set; }

    /// <summary>Fast startup is on, which stops most computers waking after Shut down; null until a client from 1.1.204 on says.</summary>
    public bool? FastStartup { get; set; }

    /// <summary>What Group Policy did; null until a client from 1.1.211 on sends it. Kept when a report comes without it.</summary>
    public DeviceGroupPolicy? GroupPolicy { get; set; }

    /// <summary>Its security settings, for Domain health › On each computer; null until a client from 1.1.360 on sends them. Kept when a report comes without it.</summary>
    public DeviceHardening? Hardening { get; set; }

    /// <summary>When this server got it.</summary>
    public DateTime Received { get; set; }
}

/// <summary>
/// The security settings a client read on its own computer, as raw values (null when not set): SMB from a negotiate to
/// itself, the Print Spooler from the service, the rest from the registry. DcRules says what each means.
/// </summary>
public sealed class DeviceHardening
{
    public DateTime? Checked { get; set; }
    public bool? SmbSigning { get; set; }
    public bool? Smb1 { get; set; }
    public int? LmLevel { get; set; }
    public bool? SpoolerRunning { get; set; }
    public int? SpoolerStart { get; set; }
    public int? Tls10 { get; set; }
    public int? Tls11 { get; set; }
    public int? Etypes { get; set; }
    public int? LapsDirectory { get; set; }
    public int? LapsDsrm { get; set; }
    public int? LapsLegacy { get; set; }
    public int? Multicast { get; set; }
    public int? LdapIntegrity { get; set; }
    public int? ChannelBinding { get; set; }
}

/// <summary>
/// The inventory of every computer in Devices, one file each in the data folder's Inventory folder, and kept in memory
/// once read. Only a computer's own client can replace its inventory, and what it sends is cut to size.
/// </summary>
public sealed partial class Inventories(DataFolder folder, TimeProvider clock)
{
    public const int MaxPrograms = 3000;
    public const int MaxUpdates = 3000;
    public const int MaxText = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string dir = Path.Combine(folder.Path, "Inventory");
    private readonly ConcurrentDictionary<string, DeviceInventory?> known = new();

    /// <summary>The computer's inventory, or null when its client hasn't sent one.</summary>
    public DeviceInventory? Get(string id) => known.GetOrAdd(id, Read);

    public void Set(string id, DeviceInventory inventory)
    {
        if (File(id) is null)
        {
            return;
        }
        Tidy(inventory);
        var now = clock.GetUtcNow().UtcDateTime;
        inventory.Received = now;
        // A client before 1.1.183, or one Windows wouldn't tell, sends no Administrators: the last list stays.
        if (inventory.Admins is not null)
        {
            inventory.AdminsChecked = now;
        }
        else if (Get(id) is { } before)
        {
            (inventory.Admins, inventory.Locals, inventory.AdminsChecked) = (before.Admins, before.Locals, before.AdminsChecked);
        }
        // The same for network cards: an older client, or one that couldn't read them, leaves the last ones.
        if (inventory.Nics is null && Get(id) is { } had)
        {
            inventory.Nics = had.Nics;
        }
        // A client that just started (after its own update, say) hasn't heard from Windows Update yet: the last answer stays.
        if (inventory.MissingChecked is null && Get(id) is { MissingChecked: not null } knew)
        {
            (inventory.Missing, inventory.MissingChecked) = (knew.Missing, knew.MissingChecked);
        }
        if (inventory.History is null && Get(id) is { History: not null } did)
        {
            inventory.History = did.History;
        }
        // Group Policy is read once a day on its own: a report from before then keeps the last results.
        if (inventory.GroupPolicy is null && Get(id) is { GroupPolicy: not null } gp)
        {
            inventory.GroupPolicy = gp.GroupPolicy;
        }
        // The same for the security settings: an older client sends none, and the last ones stay.
        if (inventory.Hardening is null && Get(id) is { Hardening: not null } hard)
        {
            inventory.Hardening = hard.Hardening;
        }
        Save(id, inventory);
    }

    /// <summary>Administrators as the computer just said, after a list or a change: kept with its inventory, when it has one.</summary>
    public void SetAdmins(string id, List<WindowsMember> admins, List<WindowsAccount>? locals)
    {
        if (Get(id) is not { } inventory)
        {
            return;
        }
        inventory.Admins = Members(admins);
        if (locals is not null)
        {
            inventory.Locals = Accounts(locals);
        }
        inventory.AdminsChecked = clock.GetUtcNow().UtcDateTime;
        Save(id, inventory);
    }

    /// <summary>
    /// The drives as the computer said at its check-in (every 5 minutes), so the console and the disk alerts don't wait for
    /// the daily inventory. Written only when a drive came or went or its use moved by a whole percent.
    /// </summary>
    public void SetDisks(string id, List<DeviceDisk> disks)
    {
        if (Get(id) is not { } inventory)
        {
            return;
        }
        var now = disks.Where(d => !string.IsNullOrWhiteSpace(d.Name) && d.Total > 0).Take(32)
            .Select(d => new DeviceDisk { Name = Cut(d.Name) ?? "", Total = d.Total, Free = Math.Clamp(d.Free, 0, d.Total) }).ToList();
        static string Use(IEnumerable<DeviceDisk> list) => string.Join('|', list.Select(d => $"{d.Name}:{d.Total}:{(d.Total - d.Free) * 100 / d.Total}"));
        var moved = Use(now) != Use(inventory.Disks.Where(d => d.Total > 0));
        inventory.Disks = now;
        if (moved)
        {
            Save(id, inventory);
        }
    }

    /// <summary>The client looked again and nothing had changed since what it last sent: that inventory is current as of now.</summary>
    public bool Same(string id)
    {
        if (Get(id) is not { } inventory)
        {
            return false;
        }
        inventory.Received = clock.GetUtcNow().UtcDateTime;
        Save(id, inventory);
        return true;
    }

    private void Save(string id, DeviceInventory inventory)
    {
        if (File(id) is not { } path)
        {
            return;
        }
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllBytes(path + ".tmp", JsonSerializer.SerializeToUtf8Bytes(inventory, Json));
        System.IO.File.Move(path + ".tmp", path, overwrite: true);
        known[id] = inventory;
    }

    /// <summary>The computer was removed from Devices: its inventory goes with it.</summary>
    public void Forget(string id)
    {
        known.TryRemove(id, out _);
        if (File(id) is { } path && System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
        }
    }

    private DeviceInventory? Read(string id)
    {
        try
        {
            var read = File(id) is { } path && System.IO.File.Exists(path) ? JsonSerializer.Deserialize<DeviceInventory>(System.IO.File.ReadAllBytes(path), Json) : null;
            // What an older client called a Group Policy error and isn't goes, without waiting for the computer to send again.
            read?.GroupPolicy?.Errors.RemoveAll(e => GpNoise(e.Text));
            return read;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // the client sends it again within hours
        }
    }

    // Ids are the server's own (hex); anything else gets no file.
    private string? File(string id) => id.Length is > 0 and <= 64 && id.All(char.IsAsciiLetterOrDigit) ? Path.Combine(dir, id + ".json") : null;

    private static string? Cut(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim() is { Length: > MaxText } t ? t[..MaxText] : text.Trim();

    // What a client sends is its own computer's, but still someone else's words: every text and list has a limit.
    private static void Tidy(DeviceInventory i)
    {
        (i.Manufacturer, i.Model, i.Serial, i.Processor) = (Cut(i.Manufacturer), Cut(i.Model), Cut(i.Serial), Cut(i.Processor));
        i.Memory = Math.Max(0, i.Memory);
        i.Disks = [.. (i.Disks ?? []).Where(d => !string.IsNullOrWhiteSpace(d.Name)).Take(32)];
        foreach (var d in i.Disks)
        {
            (d.Name, d.Total, d.Free) = (Cut(d.Name) ?? "", Math.Max(0, d.Total), Math.Max(0, d.Free));
        }
        i.Software = [.. (i.Software ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Name)).Take(MaxPrograms)];
        foreach (var p in i.Software)
        {
            (p.Name, p.Version, p.Publisher, p.Installed) = (Cut(p.Name) ?? "", Cut(p.Version), Cut(p.Publisher), Cut(p.Installed));
        }
        i.Updates = Updates(i.Updates);
        i.Missing = Updates(i.Missing);
        i.Admins = i.Admins is null ? null : Members(i.Admins);
        i.Locals = i.Locals is null ? null : Accounts(i.Locals);
        i.Printers = i.Printers is null ? null : [.. i.Printers.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Take(MaxPrinters).Select(p => new DevicePrinter
        {
            Name = Cut(p.Name) ?? "", Driver = Cut(p.Driver), Port = Cut(p.Port), Address = Cut(p.Address), Location = Cut(p.Location), Shared = p.Shared, Offline = p.Offline,
            Kind = PrinterKinds.Contains(p.Kind) ? p.Kind : "local",
        })];
        i.PrinterDrivers = i.PrinterDrivers is null ? null : [.. i.PrinterDrivers.Select(Cut).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxPrinters)];
        i.History = i.History is null ? null : [.. i.History.Where(h => !string.IsNullOrWhiteSpace(h.Title)).Take(MaxHistory).Select(h => new DeviceHistory
        {
            Title = Cut(h.Title) ?? "", Kb = Cut(h.Kb), Date = h.Date, Result = HistoryResults.Contains(h.Result) ? h.Result : "installed",
        })];
        i.Nics = i.Nics is null ? null : [.. i.Nics.Select(n => (Nic: n, Mac: WakeOnLan.Mac(n.Mac))).Where(n => n.Mac is not null).Take(WakeOnLan.Most).Select(n => new DeviceNic
        {
            Name = Cut(n.Nic.Name) ?? "", Mac = WakeOnLan.Text(n.Mac!), Kind = n.Nic.Kind == "wifi" ? "wifi" : "wired", Up = n.Nic.Up, Wake = n.Nic.Wake, WakeOff = n.Nic.WakeOff,
            Ip = System.Net.IPAddress.TryParse(n.Nic.Ip, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? ip.ToString() : null,
            Prefix = n.Nic.Prefix is >= 1 and <= 30 ? n.Nic.Prefix : 0,
        })];
        i.GroupPolicy = Policy(i.GroupPolicy);
    }

    public const int MaxGpos = 60;
    public static readonly string[] GpoWhy = ["security", "wmi", "disabled", "link", "empty"];

    private static DeviceGroupPolicy? Policy(DeviceGroupPolicy? p)
    {
        if (p is null)
        {
            return null;
        }
        static List<DeviceGpo> Gpos(List<DeviceGpo>? list, bool denied) =>
            [.. (list ?? []).Where(g => !string.IsNullOrWhiteSpace(g.Id)).Take(MaxGpos).Select(g => new DeviceGpo
            {
                Id = Cut(g.Id)!.ToUpperInvariant(), Name = Cut(g.Name) ?? Cut(g.Id)!, Link = Cut(g.Link), Why = denied ? GpoWhy.Contains(g.Why) ? g.Why : "empty" : null,
            })];
        static DeviceGpSide? Side(DeviceGpSide? s) => s is null ? null : new DeviceGpSide
        {
            Who = Cut(s.Who) ?? "", Som = Cut(s.Som), Refreshed = s.Refreshed, Took = double.IsFinite(s.Took) ? Math.Clamp(s.Took, 0, 86400) : 0, Slow = s.Slow, Dc = Cut(s.Dc),
            Applied = Gpos(s.Applied, false), Denied = Gpos(s.Denied, true),
        };
        static int? Small(int? n) => n is >= 0 and <= 100_000 ? n : null;
        return new DeviceGroupPolicy
        {
            Checked = p.Checked,
            Computer = Side(p.Computer),
            User = Side(p.User),
            Errors = [.. (p.Errors ?? []).Where(e => !string.IsNullOrWhiteSpace(e.Text) && !GpNoise(e.Text)).Take(10).Select(e => new DeviceGpError
            {
                When = e.When, Source = Cut(e.Source) ?? "Group Policy", Text = e.Text.Trim() is { Length: > 400 } t ? t[..400] : e.Text.Trim(), Warning = e.Warning,
            })],
            Local = p.Local is not { } l ? null : new DeviceLocalPolicy
            {
                MinLength = Small(l.MinLength), MaxAge = Small(l.MaxAge), History = Small(l.History), Lockout = Small(l.Lockout), LockoutMinutes = Small(l.LockoutMinutes),
                Firewall = Cut(l.Firewall), Rdp = Cut(l.Rdp), Uac = l.Uac, Smb1 = l.Smb1, AuditLogon = Cut(l.AuditLogon),
            },
        };
    }

    /// <summary>
    /// Not a Group Policy error, though a client before 1.1.236 sends it as one: Windows couldn't measure the link's speed and
    /// carried on, or a part of Group Policy "completed" with nothing saying what went wrong.
    /// </summary>
    public static bool GpNoise(string? text) => text is not null && (text.StartsWith("Group Policy bandwidth estimation failed", StringComparison.OrdinalIgnoreCase)
        || GpCompleted().IsMatch(text.Trim()));

    [GeneratedRegex(@"^Completed .+ Extension Processing in \d+ milliseconds\.$", RegexOptions.IgnoreCase)]
    private static partial Regex GpCompleted();

    public const int MaxPrinters = 300;
    public const int MaxHistory = 100;
    public static readonly string[] HistoryResults = ["installed", "failed", "cancelled", "pending"];
    public static readonly string[] PrinterKinds = ["tcpip", "lpr", "ipp", "wsd", "share", "universal", "virtual", "local"];

    public static readonly string[] MemberKinds = ["local", "localgroup", "user", "group", "computer", "wellknown", "unknown"];

    private static List<WindowsMember> Members(List<WindowsMember> list) =>
        [.. list.Where(m => !string.IsNullOrWhiteSpace(m.Name)).Take(WindowsMember.Max).Select(m => new WindowsMember
        {
            Name = Cut(m.Name) ?? "",
            Sid = Cut(m.Sid),
            Kind = MemberKinds.Contains(m.Kind) ? m.Kind : "unknown",
        })];

    private static List<WindowsAccount> Accounts(List<WindowsAccount> list) =>
        [.. list.Where(a => !string.IsNullOrWhiteSpace(a.Name)).Take(WindowsMember.Max).Select(a => new WindowsAccount
        {
            Name = Cut(a.Name) ?? "",
            FullName = Cut(a.FullName),
            Description = Cut(a.Description),
            Enabled = a.Enabled,
            Locked = a.Locked,
            NeverExpires = a.NeverExpires,
            BuiltIn = a.BuiltIn,
            LastSignIn = Cut(a.LastSignIn),
            PasswordSet = Cut(a.PasswordSet),
        })];

    private static List<DeviceUpdate> Updates(List<DeviceUpdate>? list)
    {
        List<DeviceUpdate> kept = [.. (list ?? []).Where(u => !string.IsNullOrWhiteSpace(u.Kb) || !string.IsNullOrWhiteSpace(u.Title)).Take(MaxUpdates)];
        foreach (var u in kept)
        {
            (u.Kb, u.Title, u.Kind, u.Installed) = (Cut(u.Kb) ?? "", Cut(u.Title), Cut(u.Kind), Cut(u.Installed));
        }
        return kept;
    }
}
