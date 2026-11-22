using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Security;
using System.ServiceProcess;
using Microsoft.Win32;

namespace RabbidLab.Client;

/// <summary>
/// This computer's security settings, for the server's Domain health › On each computer: raw values, null when not set.
/// The server (DcSettings.cs) says what each means, the same way as for a domain controller it reads over the network.
/// </summary>
[DataContract]
internal sealed class HardeningReport
{
    /// <summary>When they were read, as ISO 8601.</summary>
    [DataMember(Name = "checked")] public string Checked { get; set; } = "";
    [DataMember(Name = "smbSigning", EmitDefaultValue = false)] public bool? SmbSigning { get; set; }
    [DataMember(Name = "smb1", EmitDefaultValue = false)] public bool? Smb1 { get; set; }
    [DataMember(Name = "lmLevel", EmitDefaultValue = false)] public int? LmLevel { get; set; }
    [DataMember(Name = "spoolerRunning", EmitDefaultValue = false)] public bool? SpoolerRunning { get; set; }
    [DataMember(Name = "spoolerStart", EmitDefaultValue = false)] public int? SpoolerStart { get; set; }
    [DataMember(Name = "tls10", EmitDefaultValue = false)] public int? Tls10 { get; set; }
    [DataMember(Name = "tls11", EmitDefaultValue = false)] public int? Tls11 { get; set; }
    [DataMember(Name = "etypes", EmitDefaultValue = false)] public int? Etypes { get; set; }
    [DataMember(Name = "lapsDirectory", EmitDefaultValue = false)] public int? LapsDirectory { get; set; }
    [DataMember(Name = "lapsDsrm", EmitDefaultValue = false)] public int? LapsDsrm { get; set; }
    [DataMember(Name = "lapsLegacy", EmitDefaultValue = false)] public int? LapsLegacy { get; set; }
    [DataMember(Name = "multicast", EmitDefaultValue = false)] public int? Multicast { get; set; }
    [DataMember(Name = "ldapIntegrity", EmitDefaultValue = false)] public int? LdapIntegrity { get; set; }
    [DataMember(Name = "channelBinding", EmitDefaultValue = false)] public int? ChannelBinding { get; set; }
}

/// <summary>
/// Reads the security settings here, with the inventory: the registry, the Print Spooler service, and how this computer's
/// SMB server answers a negotiate on port 445 from itself (the same negotiate the server sends a domain controller).
/// Reading only: nothing is changed.
/// </summary>
internal static class Hardening
{
    public static HardeningReport Read()
    {
        const string lsa = @"SYSTEM\CurrentControlSet\Control\Lsa";
        const string ntds = @"SYSTEM\CurrentControlSet\Services\NTDS\Parameters";
        const string protocols = @"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols";
        const string laps = @"SOFTWARE\Microsoft\Policies\LAPS";
        var r = new HardeningReport
        {
            Checked = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            LmLevel = Number(lsa, "LmCompatibilityLevel"),
            SpoolerStart = Number(@"SYSTEM\CurrentControlSet\Services\Spooler", "Start"),
            Tls10 = Number(protocols + @"\TLS 1.0\Server", "Enabled"),
            Tls11 = Number(protocols + @"\TLS 1.1\Server", "Enabled"),
            Etypes = Number(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Kerberos\Parameters", "SupportedEncryptionTypes"),
            LapsDirectory = Number(laps, "BackupDirectory"),
            LapsDsrm = Number(laps, "ADBackupDSRMPassword"),
            LapsLegacy = Number(@"SOFTWARE\Policies\Microsoft Services\AdmPwd", "AdmPwdEnabled"),
            Multicast = Number(@"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast"),
            LdapIntegrity = Number(ntds, "LDAPServerIntegrity"),
            ChannelBinding = Number(ntds, "LdapEnforceChannelBinding"),
        };
        try
        {
            using var spooler = new ServiceController("Spooler");
            r.SpoolerRunning = spooler.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException)
        {
            r.SpoolerRunning = false; // not installed here
        }
        if (Ask(Negotiate2()) is { } two)
        {
            r.SmbSigning = SigningRequired(two);
            r.Smb1 = Ask(Negotiate1()) is { } one && Smb1Answered(one);
        }
        return r;
    }

    private static int? Number(string path, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue(name) switch
            {
                int i => i,
                long l => (int) l,
                string s when int.TryParse(s, out var n) => n,
                _ => null,
            };
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    // --- SMB: the same negotiates as the server's SmbProbe (DcSettings.cs), sent to this computer ----------------------------

    /// <summary>An SMB2 NEGOTIATE offering 2.0.2 to 3.0.2, behind its 4-byte session header.</summary>
    private static byte[] Negotiate2()
    {
        ushort[] dialects = { 0x0202, 0x0210, 0x0300, 0x0302 };
        var b = new byte[4 + 64 + 36 + dialects.Length * 2];
        Session(b);
        b[4] = 0xFE;
        b[5] = (byte) 'S';
        b[6] = (byte) 'M';
        b[7] = (byte) 'B';
        Write16(b, 4 + 4, 64); // header size
        Write16(b, 4 + 14, 1); // credits asked for; command 0 is NEGOTIATE
        var n = 4 + 64;
        Write16(b, n, 36);
        Write16(b, n + 2, (ushort) dialects.Length);
        Write16(b, n + 4, 1); // signing enabled, not required
        Array.Copy(Guid.NewGuid().ToByteArray(), 0, b, n + 12, 16);
        for (var i = 0; i < dialects.Length; i++)
        {
            Write16(b, n + 36 + i * 2, dialects[i]);
        }
        return b;
    }

    /// <summary>An SMB1 NEGOTIATE offering only NT LM 0.12. A computer with SMBv1 off drops it.</summary>
    private static byte[] Negotiate1()
    {
        var dialect = System.Text.Encoding.ASCII.GetBytes("\u0002NT LM 0.12\0");
        var b = new byte[4 + 32 + 3 + dialect.Length];
        Session(b);
        b[4] = 0xFF;
        b[5] = (byte) 'S';
        b[6] = (byte) 'M';
        b[7] = (byte) 'B';
        b[8] = 0x72; // NEGOTIATE
        b[4 + 9] = 0x18;
        Write16(b, 4 + 10, 0x4001);
        Write16(b, 4 + 26, 0xFEFF);
        Write16(b, 4 + 33, (ushort) dialect.Length);
        Array.Copy(dialect, 0, b, 4 + 35, dialect.Length);
        return b;
    }

    private static bool? SigningRequired(byte[] reply)
    {
        if (reply.Length < 68 || reply[0] != 0xFE || reply[1] != 'S' || reply[2] != 'M' || reply[3] != 'B' || BitConverter.ToUInt32(reply, 8) != 0)
        {
            return null;
        }
        return (BitConverter.ToUInt16(reply, 66) & 0x2) != 0;
    }

    private static bool Smb1Answered(byte[] reply) => reply.Length >= 35 && reply[0] == 0xFF && reply[1] == 'S' && reply[2] == 'M' && reply[3] == 'B' && reply[4] == 0x72
        && BitConverter.ToUInt32(reply, 5) == 0 && reply[32] >= 1 && BitConverter.ToUInt16(reply, 33) != 0xFFFF;

    /// <summary>One reply from this computer's port 445; empty when it drops the request, null when nothing answers there.</summary>
    private static byte[]? Ask(byte[] request)
    {
        using var tcp = new TcpClient();
        try
        {
            if (!tcp.ConnectAsync(IPAddress.Loopback, 445).Wait(3000))
            {
                return null;
            }
        }
        catch (AggregateException)
        {
            return null; // the Server service isn't running
        }
        try
        {
            tcp.ReceiveTimeout = 3000;
            tcp.SendTimeout = 3000;
            var s = tcp.GetStream();
            s.Write(request, 0, request.Length);
            var head = Fill(s, 4);
            if (head is null)
            {
                return Array.Empty<byte>();
            }
            var length = (head[1] << 16) | (head[2] << 8) | head[3];
            return length is <= 0 or > 65536 ? Array.Empty<byte>() : Fill(s, length) ?? Array.Empty<byte>();
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            return Array.Empty<byte>(); // dropped or reset
        }
    }

    private static byte[]? Fill(Stream s, int length)
    {
        var into = new byte[length];
        var at = 0;
        while (at < length)
        {
            var n = s.Read(into, at, length - at);
            if (n == 0)
            {
                return null;
            }
            at += n;
        }
        return into;
    }

    private static void Session(byte[] b)
    {
        var length = b.Length - 4;
        b[1] = (byte) (length >> 16);
        b[2] = (byte) (length >> 8);
        b[3] = (byte) length;
    }

    private static void Write16(byte[] b, int at, int value)
    {
        b[at] = (byte) value;
        b[at + 1] = (byte) (value >> 8);
    }
}
