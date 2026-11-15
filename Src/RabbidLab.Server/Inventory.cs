using System.Collections.Concurrent;
using System.Text.Json;
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

/// <summary>A Windows update: installed on the computer (with the day), or missing from it.</summary>
public sealed class DeviceUpdate
{
    public string Kb { get; set; } = "";
    public string? Title { get; set; }
    public string? Kind { get; set; }
    public string? Installed { get; set; }
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

    /// <summary>When Admins was last read on the computer.</summary>
    public DateTime? AdminsChecked { get; set; }

    /// <summary>When this server got it.</summary>
    public DateTime Received { get; set; }
}

/// <summary>
/// The inventory of every computer in Devices, one file each in the data folder's Inventory folder, and kept in memory
/// once read. Only a computer's own client can replace its inventory, and what it sends is cut to size.
/// </summary>
public sealed class Inventories(DataFolder folder, TimeProvider clock)
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
            return File(id) is { } path && System.IO.File.Exists(path) ? JsonSerializer.Deserialize<DeviceInventory>(System.IO.File.ReadAllBytes(path), Json) : null;
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
    }

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
