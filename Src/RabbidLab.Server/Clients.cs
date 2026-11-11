using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>A computer with the Rabbid Lab client: what it said at its last check-in, and whether an admin let it in.</summary>
public sealed class Client
{
    public string Id { get; set; } = "";
    public string Computer { get; set; } = "";
    public string? Os { get; set; }
    public string? Version { get; set; }

    /// <summary>Who is signed in to Windows on it, as DOMAIN\name, or null for nobody.</summary>
    public string? User { get; set; }
    public string? Address { get; set; }
    public DateTime Joined { get; set; }
    public DateTime LastSeen { get; set; }

    /// <summary>Who let it in, and when; null while it waits for an admin.</summary>
    public string? ApprovedBy { get; set; }
    public DateTime? Approved { get; set; }

    /// <summary>SHA-256 of the client's secret, which only the client has.</summary>
    public string SecretHash { get; set; } = "";

    /// <summary>Until when the client may be uninstalled on its computer; null or past means it's locked.</summary>
    public DateTime? RemovalUntil { get; set; }

    /// <summary>The client is to uninstall itself at its next check-in.</summary>
    public bool Uninstall { get; set; }
}

public sealed class ClientsFile
{
    public List<Client> Clients { get; set; } = [];

    /// <summary>Whether computers that join wait for an admin before they count as seats.</summary>
    public bool Approve { get; set; } = true;
}

/// <summary>
/// The computers with the Rabbid Lab client, kept in clients.json in the data folder. A client joins with just the
/// server's address and gets its own id and secret, which it checks in with from then on. New ones wait to be let in.
/// </summary>
public sealed class Clients(DataFolder folder, TimeProvider clock)
{
    /// <summary>How long after its last check-in a client still counts as online. Clients check in every minute.</summary>
    public static readonly TimeSpan Online = TimeSpan.FromMinutes(3);

    /// <summary>How many computers may wait to be let in at once, so a flood of joins can't fill the list.</summary>
    public const int MaxWaiting = 200;

    /// <summary>How many computers the list holds in all.</summary>
    public const int MaxClients = 2000;

    /// <summary>The longest text a client may send for its operating system, version or user.</summary>
    public const int MaxText = 128;

    // A check-in that changes nothing but its time is written this often at most.
    private static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<ClientsFile> file = folder.Json<ClientsFile>("clients.json", Json);
    private DateTimeOffset written;
    private bool changed;

    public IReadOnlyList<Client> All()
    {
        lock (file.Gate)
        {
            return [.. file.Read().Clients.OrderBy(c => c.Computer, StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>Whether new computers wait to be let in.</summary>
    public bool Waits
    {
        get
        {
            lock (file.Gate)
            {
                return file.Read().Approve;
            }
        }
    }

    public void SetApprove(bool approve)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            f.Approve = approve;
            Save(f);
        }
    }

    /// <summary>A computer joining: its id and secret, or why the list has no room for it.</summary>
    public (Client? Client, string? Secret, string? Problem) Join(string computer, string? os, string? version, string? address)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            // The same computer joining again while it waits (it lost its secret, say) replaces its old entry.
            f.Clients.RemoveAll(c => c.Approved is null && string.Equals(c.Computer, computer, StringComparison.OrdinalIgnoreCase) && c.Address == address);
            if (f.Approve && f.Clients.Count(c => c.Approved is null) >= MaxWaiting)
            {
                return (null, null, $"{MaxWaiting} computers are already waiting to be let in. Let some in or remove them first.");
            }
            if (f.Clients.Count >= MaxClients)
            {
                return (null, null, $"The list already has {MaxClients} computers. Remove some first.");
            }
            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var now = clock.GetUtcNow().UtcDateTime;
            var client = new Client
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(),
                Computer = computer,
                Os = Cut(os),
                Version = Cut(version),
                Address = address,
                Joined = now,
                LastSeen = now,
                Approved = f.Approve ? null : now,
                ApprovedBy = f.Approve ? null : "joining straight away",
                SecretHash = Hash(secret),
            };
            f.Clients.Add(client);
            Save(f);
            return (client, secret, null);
        }
    }

    /// <summary>A check-in: what the client says now. Null when the id or secret is wrong, or the client was removed.</summary>
    public Client? CheckIn(string id, string secret, string? computer, string? os, string? version, string? user, string? address)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (Known(f, id, secret) is not { } c)
            {
                return null;
            }
            var before = (c.Computer, c.Os, c.Version, c.User, c.Address);
            c.Computer = string.IsNullOrWhiteSpace(computer) || computer.Trim().Length > 64 ? c.Computer : computer.Trim();
            c.Os = Cut(os) ?? c.Os;
            c.Version = Cut(version) ?? c.Version;
            c.User = string.IsNullOrWhiteSpace(user) ? null : Cut(user.Trim());
            c.Address = address ?? c.Address;
            c.LastSeen = clock.GetUtcNow().UtcDateTime;
            changed = true;
            if (before != (c.Computer, c.Os, c.Version, c.User, c.Address) || clock.GetUtcNow() - written > SaveEvery)
            {
                Save(f);
            }
            return c;
        }
    }

    /// <summary>The client, when the id and secret are its own; nothing is changed.</summary>
    public Client? Verify(string id, string secret)
    {
        lock (file.Gate)
        {
            return Known(file.Read(), id, secret);
        }
    }

    public Client? Find(string id)
    {
        lock (file.Gate)
        {
            return file.Read().Clients.FirstOrDefault(x => x.Id == id);
        }
    }

    public Client? Approve(string id, string by)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            var c = f.Clients.FirstOrDefault(x => x.Id == id);
            if (c is null)
            {
                return null;
            }
            c.Approved ??= clock.GetUtcNow().UtcDateTime;
            c.ApprovedBy ??= by;
            Save(f);
            return c;
        }
    }

    /// <summary>Removes a client: its secret stops working, and it has to join again and wait for approval.</summary>
    public Client? Remove(string id)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            var c = f.Clients.FirstOrDefault(x => x.Id == id);
            if (c is not null)
            {
                f.Clients.Remove(c);
                Save(f);
            }
            return c;
        }
    }

    /// <summary>How long an allowed uninstall stays allowed.</summary>
    public static readonly TimeSpan RemovalWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// Allows the client to be uninstalled on its computer for the next hour, or locks it again. With now, the client
    /// is also told to uninstall itself at its next check-in. A client is locked unless this says otherwise.
    /// </summary>
    public Client? AllowRemoval(string id, bool allow, bool now = false)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            var c = f.Clients.FirstOrDefault(x => x.Id == id);
            if (c is null)
            {
                return null;
            }
            c.RemovalUntil = allow ? clock.GetUtcNow().UtcDateTime + RemovalWindow : null;
            c.Uninstall = allow && now;
            Save(f);
            return c;
        }
    }

    /// <summary>Until when the client may be uninstalled, when that's still ahead.</summary>
    public DateTime? RemovalUntil(Client c) => c.RemovalUntil is { } until && until > clock.GetUtcNow().UtcDateTime ? until : null;

    /// <summary>Whether the client was ordered to uninstall itself. Said once: if it didn't work, it's ordered again.</summary>
    public bool TakeUninstall(string id)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            var c = f.Clients.FirstOrDefault(x => x.Id == id);
            if (c is null || !c.Uninstall)
            {
                return false;
            }
            c.Uninstall = false;
            Save(f);
            return RemovalUntil(c) is not null;
        }
    }

    /// <summary>Writes the check-in times still waiting to be written, such as when Rabbid Lab stops.</summary>
    public void Flush()
    {
        lock (file.Gate)
        {
            if (changed)
            {
                Save(file.Read());
            }
        }
    }

    public bool IsOnline(Client c) => clock.GetUtcNow().UtcDateTime - c.LastSeen < Online;

    /// <summary>The client with this id, when the secret is its own.</summary>
    private static Client? Known(ClientsFile f, string id, string secret) =>
        f.Clients.FirstOrDefault(x => x.Id == id) is { } c
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(secret)), Encoding.ASCII.GetBytes(c.SecretHash)) ? c : null;

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static string? Cut(string? text) => text is { Length: > MaxText } ? text[..MaxText] : text;

    private void Save(ClientsFile f)
    {
        file.Save(f);
        written = clock.GetUtcNow();
        changed = false;
    }
}
