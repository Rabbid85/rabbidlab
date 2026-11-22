using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace RabbidLab.Server;

/// <summary>A security setting read from each domain controller: what's recommended, where it's read from, and the Group Policy setting that fixes it.</summary>
public sealed record DcSetting(string Key, string Name, string Severity, string Recommended, string ReadFrom, string Path, string Policy, string SetTo, string About);

/// <summary>A setting's value on one DC: its words, whether it's as recommended (null when it couldn't be read), and why it couldn't be.</summary>
public sealed record DcValue(string Value, bool? Ok, string? Problem = null);

/// <summary>What was last read from one DC.</summary>
public sealed class DcReading
{
    public string Name { get; set; } = "";
    public string? Site { get; set; }
    public DateTime At { get; set; }
    public Dictionary<string, DcValue> Values { get; set; } = [];
}

public sealed class DcReadingsFile
{
    public List<DcReading> Dcs { get; set; } = [];
}

/// <summary>Reads the settings from one DC. Tests stand in for it.</summary>
public interface IDcProbe
{
    Task<Dictionary<string, DcValue>> Read(SyncDc dc, AuditLogin login, CancellationToken stop);
}

/// <summary>
/// Hardening on each DC, for Domain health's On each DC tab. Each DC is read over the network as the service account and
/// kept in dc-settings.json: read when nothing has been yet, again on Re-check, and in the background once the last read
/// is 6 hours old. DCs are read 15 at a time.
/// </summary>
public sealed class DcSettings(DataFolder folder, IAuditDirectory directory, IDcProbe probe, TimeProvider clock, ILogger<DcSettings> log)
{
    public const string Gpo = "Default Domain Controllers Policy";
    private const string SecurityOptions = "Computer Configuration › Policies › Windows Settings › Security Settings › Local Policies › Security Options";

    public static readonly IReadOnlyList<DcSetting> All =
    [
        new("smb", "SMB signing", "High", "Required", "SMB negotiate response", SecurityOptions, "Microsoft network server: Digitally sign communications (always)", "Enabled",
            "Without SMB signing, SMB traffic to a domain controller can be relayed or changed, including Group Policy files from SYSVOL."),
        new("smb1", "SMBv1 server", "High", "Disabled", "SMB1 negotiate request", "Computer Configuration › Policies › Administrative Templates › MS Security Guide", "Configure SMB v1 server", "Disabled",
            "SMBv1 has no pre-authentication integrity and was used by WannaCry and NotPetya."),
        new("ldapsign", "LDAP server signing", "High", "Require signing", @"Registry: NTDS\Parameters\LDAPServerIntegrity", SecurityOptions, "Domain controller: LDAP server signing requirements",
            "Require signing", "Unsigned LDAP binds can be relayed to the domain controller."),
        new("cbt", "LDAP channel binding", "High", "Always", @"Registry: NTDS\Parameters\LdapEnforceChannelBinding", SecurityOptions, "Domain controller: LDAP server channel binding token requirements",
            "Always", "Channel binding ties LDAPS sign-ins to their TLS session, which blocks relay over LDAPS."),
        new("ntlm", "LAN Manager authentication level", "High", "Level 5", @"Registry: Lsa\LmCompatibilityLevel", SecurityOptions, "Network security: LAN Manager authentication level",
            "Send NTLMv2 response only. Refuse LM & NTLM", "Levels below 5 accept LM and NTLMv1 responses, which can be cracked offline."),
        new("spool", "Print Spooler service", "High", "Disabled", @"Registry: Services\Spooler\Start, and the spoolss pipe", "Computer Configuration › Policies › Windows Settings › Security Settings › System Services",
            "Print Spooler", "Disabled", "A running Print Spooler on a domain controller allows forced authentication (PrinterBug) and was the target of PrintNightmare."),
        new("tls", "TLS 1.0 and TLS 1.1", "Medium", "Disabled", @"Registry: SCHANNEL\Protocols", "Computer Configuration › Preferences › Windows Settings › Registry",
            @"SCHANNEL\Protocols\TLS 1.0\Server and TLS 1.1\Server: Enabled", "0", "TLS 1.0 and TLS 1.1 are deprecated. Supported Windows versions use TLS 1.2 or later."),
        new("kerb", "Kerberos encryption types", "Medium", "AES128, AES256", @"Registry: Kerberos\Parameters\SupportedEncryptionTypes", SecurityOptions,
            "Network security: Configure encryption types allowed for Kerberos", "AES128_HMAC_SHA1, AES256_HMAC_SHA1, Future encryption types",
            "RC4 service tickets can be cracked offline (Kerberoasting), and DES is not secure."),
        new("laps", "Windows LAPS for DSRM", "Medium", "Configured", @"Registry: Policies\LAPS", "Computer Configuration › Policies › Administrative Templates › System › LAPS",
            "Enable password backup for DSRM accounts", "Enabled", "Windows LAPS changes the Directory Services Restore Mode password on each DC and keeps it in Active Directory."),
        new("llmnr", "LLMNR", "Low", "Disabled", @"Registry: DNSClient\EnableMulticast", "Computer Configuration › Policies › Administrative Templates › Network › DNS Client",
            "Turn off multicast name resolution", "Enabled", "LLMNR answers can be spoofed on the local network to capture NTLM hashes."),
    ];

    /// <summary>The registry keys read, for Network access: Remotely accessible registry paths and subpaths when the service account isn't an admin on the DCs.</summary>
    public static readonly IReadOnlyList<string> RegistryPaths = [.. NetworkDcProbe.Keys.Select(k => k.Path).Distinct()];

    public static readonly TimeSpan Stale = TimeSpan.FromHours(6);
    private const int AtOnce = 15;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<DcReadingsFile> file = folder.Json<DcReadingsFile>("dc-settings.json", Json);
    private readonly SemaphoreSlim one = new(1, 1);
    private volatile bool reading;

    public bool Reading => reading;

    public IReadOnlyList<DcReading> Readings => file.Read().Dcs;

    /// <summary>Reads the DCs named (every DC when none are), 15 at a time, and keeps what each said. False without a service account.</summary>
    public async Task<bool> Read(IReadOnlyCollection<string>? only, CancellationToken stop)
    {
        var login = await directory.AuditLogin(stop);
        if (login is null)
        {
            return false;
        }
        await one.WaitAsync(stop);
        reading = true;
        try
        {
            var dcs = login.Dcs.Where(d => only is not { Count: > 0 } || only.Contains(d.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            var got = new ConcurrentBag<DcReading>();
            await Parallel.ForEachAsync(dcs, new ParallelOptions { MaxDegreeOfParallelism = AtOnce, CancellationToken = stop }, async (dc, token) =>
            {
                Dictionary<string, DcValue> values;
                try
                {
                    values = await probe.Read(dc, login, token);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogWarning(e, "Could not read the security settings on {Dc}", dc.Name);
                    values = Each(new DcValue("Could not be read", null, $"{dc.Name} could not be read: {e.Message.Trim().TrimEnd('.')}."));
                }
                got.Add(new DcReading { Name = dc.Name, Site = dc.Site, At = clock.GetUtcNow().UtcDateTime, Values = values });
            });
            lock (file.Gate)
            {
                // What was read replaces what was; DCs no longer in the domain go.
                var kept = file.Read().Dcs.Where(d => !got.Any(g => Same(g.Name, d.Name)) && login.Dcs.Any(x => Same(x.Name, d.Name)));
                file.Save(new DcReadingsFile { Dcs = [.. kept.Concat(got).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)] });
            }
            return true;
        }
        finally
        {
            reading = false;
            one.Release();
        }
    }

    /// <summary>
    /// What the page shows: each setting with its value on each DC, and when each DC was read. again reads them first (only
    /// the DCs in only, when given). With nothing read yet, or the last read 6 hours old, it reads in the background.
    /// </summary>
    public async Task<object> View(bool again, IReadOnlyCollection<string>? only, CancellationToken stop)
    {
        var login = await directory.AuditLogin(stop);
        if (login is not null && again)
        {
            await Read(only, stop);
        }
        var dcs = file.Read().Dcs.Where(d => login is null || login.Dcs.Any(x => Same(x.Name, d.Name))).ToList();
        var now = clock.GetUtcNow().UtcDateTime;
        if (login is not null && !reading && (dcs.Count < login.Dcs.Count || dcs.Any(d => now - d.At > Stale)))
        {
            reading = true; // said straight away, so the page knows to look again
            _ = Task.Run(async () =>
            {
                try
                {
                    await Read(null, CancellationToken.None);
                }
                catch (Exception e)
                {
                    reading = false;
                    log.LogWarning(e, "Could not read the domain controllers' security settings");
                }
            }, CancellationToken.None);
        }
        var unread = new DcValue("Not read yet", null);
        return new
        {
            settings = All.Select(s => new
            {
                s.Key, s.Name, s.Severity, s.Recommended, s.ReadFrom, gpo = Gpo, s.Path, s.Policy, s.SetTo, s.About,
                values = dcs.ToDictionary(d => d.Name, d => d.Values.GetValueOrDefault(s.Key) ?? unread, StringComparer.OrdinalIgnoreCase),
            }),
            dcs = dcs.Select(d => new { d.Name, d.Site, d.At }),
            reading,
            account = login is not null,
            user = login?.User,
            windows = OperatingSystem.IsWindows(),
            paths = RegistryPaths,
        };
    }

    /// <summary>The same value for every setting, for a DC that couldn't be read at all.</summary>
    public static Dictionary<string, DcValue> Each(DcValue value) => All.ToDictionary(s => s.Key, _ => value);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What each setting's raw value means: its words and whether it's as recommended. Missing means not set.</summary>
public static class DcRules
{
    public static int? Number(object? v) => v switch
    {
        int i => i,
        long l => (int) l,
        string s when int.TryParse(s, out var n) => n,
        _ => null,
    };

    public static DcValue SmbSigning(bool required) => required ? new("Required", true) : new("Not required", false);

    public static DcValue Smb1(bool answered) => answered ? new("Enabled", false) : new("Disabled", true);

    public static DcValue LdapSigning(object? v) => Number(v) switch
    {
        2 => new("Require signing", true),
        0 => new("Off", false),
        1 => new("None", false),
        _ => new("None (not set)", false),
    };

    public static DcValue ChannelBinding(object? v) => Number(v) switch
    {
        2 => new("Always", true),
        1 => new("When supported", false),
        0 => new("Never", false),
        _ => new("Never (not set)", false),
    };

    public static DcValue LmLevel(object? v) => Number(v) is { } n ? new($"Level {n}", n >= 5) : new("Level 3 (not set)", false);

    /// <summary>The spoolss pipe answers while the Print Spooler runs; its start type says whether it comes back after a restart.</summary>
    public static DcValue Spooler(bool running, object? start) => running ? new("Running", false)
        : Number(start) switch
        {
            4 => new("Disabled", true),
            2 => new("Stopped, starts with Windows", false),
            _ => new("Stopped", true),
        };

    public static DcValue Tls(object? tls10, object? tls11)
    {
        // Enabled 0 turns a protocol off for the server; anything else, or nothing, leaves it on.
        var on10 = Number(tls10) is not 0;
        var on11 = Number(tls11) is not 0;
        var set = tls10 is not null || tls11 is not null;
        var words = (on10, on11) switch
        {
            (false, false) => "Disabled",
            (true, false) => "TLS 1.0 enabled",
            (false, true) => "TLS 1.1 enabled",
            _ => set ? "Enabled" : "Enabled (not set)",
        };
        return new(words, !on10 && !on11);
    }

    public static DcValue Kerberos(object? v)
    {
        if (Number(v) is not { } n)
        {
            return new("RC4, AES128, AES256 (not set)", false);
        }
        var names = new List<string>();
        if ((n & 0x3) != 0)
        {
            names.Add("DES");
        }
        if ((n & 0x4) != 0)
        {
            names.Add("RC4");
        }
        if ((n & 0x8) != 0)
        {
            names.Add("AES128");
        }
        if ((n & 0x10) != 0)
        {
            names.Add("AES256");
        }
        return new(names.Count == 0 ? "None" : string.Join(", ", names), (n & 0x7) == 0 && (n & 0x18) != 0);
    }

    public static DcValue Laps(object? backupDirectory, object? dsrm) => Number(backupDirectory) == 2 && Number(dsrm) == 1 ? new("Configured", true)
        : Number(backupDirectory) == 2 ? new("Backs up to AD, not for DSRM", false) : new("Not configured", false);

    public static DcValue Llmnr(object? v) => Number(v) == 0 ? new("Disabled", true) : new(v is null ? "Enabled (not set)" : "Enabled", false);
}

/// <summary>An SMB negotiate, which a server answers before anyone signs in: whether it requires signing, and whether it still speaks SMBv1.</summary>
public static class SmbProbe
{
    /// <summary>An SMB2 NEGOTIATE offering 2.0.2 to 3.0.2, behind its 4-byte session header.</summary>
    public static byte[] Negotiate2()
    {
        ushort[] dialects = [0x0202, 0x0210, 0x0300, 0x0302];
        var b = new byte[4 + 64 + 36 + dialects.Length * 2];
        Session(b);
        var h = b.AsSpan(4);
        h[0] = 0xFE;
        "SMB"u8.CopyTo(h[1..]);
        BinaryPrimitives.WriteUInt16LittleEndian(h[4..], 64); // header size
        BinaryPrimitives.WriteUInt16LittleEndian(h[14..], 1); // credits asked for; command 0 is NEGOTIATE
        var n = h[64..];
        BinaryPrimitives.WriteUInt16LittleEndian(n, 36);
        BinaryPrimitives.WriteUInt16LittleEndian(n[2..], (ushort) dialects.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(n[4..], 1); // signing enabled, not required
        Guid.NewGuid().TryWriteBytes(n[12..]);
        for (var i = 0; i < dialects.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(n[(36 + i * 2)..], dialects[i]);
        }
        return b;
    }

    /// <summary>An SMB1 NEGOTIATE offering only NT LM 0.12. A server with SMBv1 off drops it.</summary>
    public static byte[] Negotiate1()
    {
        var dialect = Encoding.ASCII.GetBytes("\u0002NT LM 0.12\0");
        var b = new byte[4 + 32 + 3 + dialect.Length];
        Session(b);
        var h = b.AsSpan(4);
        h[0] = 0xFF;
        "SMB"u8.CopyTo(h[1..]);
        h[4] = 0x72; // NEGOTIATE
        h[9] = 0x18; // case-insensitive, canonical paths
        BinaryPrimitives.WriteUInt16LittleEndian(h[10..], 0x4001); // NT status codes, long names
        BinaryPrimitives.WriteUInt16LittleEndian(h[26..], 0xFEFF); // process id
        h[32] = 0; // no parameter words
        BinaryPrimitives.WriteUInt16LittleEndian(h[33..], (ushort) dialect.Length);
        dialect.CopyTo(h[35..]);
        return b;
    }

    /// <summary>Whether an SMB2 NEGOTIATE response (after the session header) says signing is required; null when it isn't one.</summary>
    public static bool? SigningRequired(byte[] reply)
    {
        if (reply.Length < 68 || reply[0] != 0xFE || !reply.AsSpan(1, 3).SequenceEqual("SMB"u8) || BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(8)) != 0)
        {
            return null;
        }
        return (BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(66)) & 0x2) != 0;
    }

    /// <summary>Whether a reply (after the session header) is an SMB1 NEGOTIATE response that took the dialect offered.</summary>
    public static bool Smb1Answered(byte[] reply) => reply.Length >= 35 && reply[0] == 0xFF && reply.AsSpan(1, 3).SequenceEqual("SMB"u8) && reply[4] == 0x72
        && BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(5)) == 0 && reply[32] >= 1 && BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(33)) != 0xFFFF;

    /// <summary>Sends a request to port 445 and reads one reply; empty when the server drops it. Throws when it can't connect.</summary>
    public static async Task<byte[]> Ask(string host, byte[] request, CancellationToken stop)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(stop);
        wait.CancelAfter(TimeSpan.FromSeconds(5));
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, 445, wait.Token);
        try
        {
            var s = tcp.GetStream();
            await s.WriteAsync(request, wait.Token);
            var head = new byte[4];
            if (!await Fill(s, head, wait.Token))
            {
                return [];
            }
            var length = (head[1] << 16) | (head[2] << 8) | head[3];
            if (length is <= 0 or > 65536)
            {
                return [];
            }
            var body = new byte[length];
            return await Fill(s, body, wait.Token) ? body : [];
        }
        catch (Exception e) when (e is IOException or SocketException || (e is OperationCanceledException && !stop.IsCancellationRequested))
        {
            return []; // dropped, reset or no answer
        }
    }

    private static void Session(byte[] b)
    {
        var length = b.Length - 4;
        b[1] = (byte) (length >> 16);
        b[2] = (byte) (length >> 8);
        b[3] = (byte) length;
    }

    private static async Task<bool> Fill(NetworkStream s, byte[] into, CancellationToken stop)
    {
        var at = 0;
        while (at < into.Length)
        {
            var n = await s.ReadAsync(into.AsMemory(at), stop);
            if (n == 0)
            {
                return false;
            }
            at += n;
        }
        return true;
    }
}

/// <summary>
/// Reads a DC over the network: SMB signing and SMBv1 from how it answers on port 445 (no sign-in needed), then, signed in
/// as the service account, whether its spoolss pipe answers and the registry values the rest come from.
/// </summary>
public sealed class NetworkDcProbe : IDcProbe
{
    public sealed record Key(string Path, string Value);

    private static readonly Key LdapIntegrity = new(@"SYSTEM\CurrentControlSet\Services\NTDS\Parameters", "LDAPServerIntegrity");
    private static readonly Key ChannelBinding = new(@"SYSTEM\CurrentControlSet\Services\NTDS\Parameters", "LdapEnforceChannelBinding");
    private static readonly Key LmLevel = new(@"SYSTEM\CurrentControlSet\Control\Lsa", "LmCompatibilityLevel");
    private static readonly Key SpoolerStart = new(@"SYSTEM\CurrentControlSet\Services\Spooler", "Start");
    private static readonly Key Tls10 = new(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.0\Server", "Enabled");
    private static readonly Key Tls11 = new(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.1\Server", "Enabled");
    private static readonly Key Etypes = new(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Kerberos\Parameters", "SupportedEncryptionTypes");
    private static readonly Key LapsDirectory = new(@"SOFTWARE\Microsoft\Policies\LAPS", "BackupDirectory");
    private static readonly Key LapsDsrm = new(@"SOFTWARE\Microsoft\Policies\LAPS", "ADBackupDSRMPassword");
    private static readonly Key Multicast = new(@"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast");

    public static readonly IReadOnlyList<Key> Keys = [LdapIntegrity, ChannelBinding, LmLevel, SpoolerStart, Tls10, Tls11, Etypes, LapsDirectory, LapsDsrm, Multicast];

    public async Task<Dictionary<string, DcValue>> Read(SyncDc dc, AuditLogin login, CancellationToken stop)
    {
        byte[] smb2;
        try
        {
            smb2 = await SmbProbe.Ask(dc.Host, SmbProbe.Negotiate2(), stop);
        }
        catch (Exception e) when (e is SocketException || (e is OperationCanceledException && !stop.IsCancellationRequested))
        {
            return DcSettings.Each(new DcValue("Could not be read", null, $"{dc.Name} could not be reached on port 445."));
        }
        var values = new Dictionary<string, DcValue>
        {
            ["smb"] = SmbProbe.SigningRequired(smb2) is { } required ? DcRules.SmbSigning(required) : new DcValue("Could not be read", null, $"{dc.Name} did not answer an SMB2 negotiate."),
            ["smb1"] = DcRules.Smb1(SmbProbe.Smb1Answered(await SmbProbe.Ask(dc.Host, SmbProbe.Negotiate1(), stop))),
        };
        if (!OperatingSystem.IsWindows())
        {
            var why = new DcValue("Could not be read", null, $"The rest is read only when {Product.Title} runs on Windows.");
            foreach (var s in DcSettings.All.Where(s => !values.ContainsKey(s.Key)))
            {
                values[s.Key] = why;
            }
            return values;
        }
        var rest = await Task.Run(() => OperatingSystem.IsWindows() ? FromWindows(dc, login) : [], stop);
        foreach (var (key, value) in rest)
        {
            values[key] = value;
        }
        return values;
    }

    [SupportedOSPlatform("windows")]
    private static Dictionary<string, DcValue> FromWindows(SyncDc dc, AuditLogin login) => NetworkLogon.Run($@"{login.Domain}\{login.User}", login.Password, () =>
    {
        var running = SpoolerAnswers(dc.Host);
        var raw = new Dictionary<Key, object?>();
        string? problem = null;
        try
        {
            using var hklm = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, dc.Host);
            foreach (var k in Keys)
            {
                try
                {
                    using var key = hklm.OpenSubKey(k.Path);
                    raw[k] = key?.GetValue(k.Value);
                }
                catch (Exception e) when (e is SecurityException or UnauthorizedAccessException)
                {
                    problem ??= $"Access denied. {login.User} cannot read the registry on {dc.Name}.";
                }
            }
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException)
        {
            problem = $"Access denied. {login.User} cannot read the registry on {dc.Name}.";
        }
        catch (IOException)
        {
            problem = $"The registry on {dc.Name} could not be read. Check that the Remote Registry service is running.";
        }
        DcValue From(Func<DcValue> rule, params Key[] keys) => keys.All(raw.ContainsKey) ? rule() : new DcValue("Could not be read", null, problem);
        object? V(Key k) => raw.GetValueOrDefault(k);
        return new Dictionary<string, DcValue>
        {
            ["ldapsign"] = From(() => DcRules.LdapSigning(V(LdapIntegrity)), LdapIntegrity),
            ["cbt"] = From(() => DcRules.ChannelBinding(V(ChannelBinding)), ChannelBinding),
            ["ntlm"] = From(() => DcRules.LmLevel(V(LmLevel)), LmLevel),
            // The pipe says whether it runs even when the registry can't be read.
            ["spool"] = running ? DcRules.Spooler(true, null) : From(() => DcRules.Spooler(false, V(SpoolerStart)), SpoolerStart),
            ["tls"] = From(() => DcRules.Tls(V(Tls10), V(Tls11)), Tls10, Tls11),
            ["kerb"] = From(() => DcRules.Kerberos(V(Etypes)), Etypes),
            ["laps"] = From(() => DcRules.Laps(V(LapsDirectory), V(LapsDsrm)), LapsDirectory, LapsDsrm),
            ["llmnr"] = From(() => DcRules.Llmnr(V(Multicast)), Multicast),
        };
    }, $"Windows did not accept the sign-in for {login.User}.");

    /// <summary>Whether the DC's spoolss pipe answers, which it does while the Print Spooler runs. Access denied still means it's there.</summary>
    [SupportedOSPlatform("windows")]
    private static bool SpoolerAnswers(string host)
    {
        using var pipe = new NamedPipeClientStream(host, "spoolss", PipeDirection.InOut);
        try
        {
            pipe.Connect(3000);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException)
        {
            return false;
        }
    }
}

public static class DcSettingsApi
{
    public static void AddDcSettings(this IServiceCollection services)
    {
        services.AddSingleton<IDcProbe, NetworkDcProbe>();
        services.AddSingleton<DcSettings>();
    }

    public static void MapDcSettingsApi(this WebApplication app)
    {
        // Domain health › On each DC: every setting's value on each DC. again=true reads them again first (Re-check), only
        // the DCs in dc (comma-separated) when it's given.
        app.MapGet("/api/ad/dcs", (bool? again, string? dc, HttpContext context, DcSettings settings) => UsersApi.Try(context, Permissions.AdView, async () =>
            Results.Ok(await settings.View(again == true, dc?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), context.RequestAborted)),
            change: false, whole: true));
    }
}
