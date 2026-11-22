using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Hardening on each DC: what each raw value means, how SMB answers are read, and what's kept between reads.</summary>
public sealed class DcSettingsTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-dcs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(data, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temp folder's own cleanup.
        }
    }

    [Fact]
    public void Registry_values_become_words_and_whether_they_are_as_recommended()
    {
        Assert.Equal(new DcValue("Require signing", true), DcRules.LdapSigning(2));
        Assert.Equal(new DcValue("None (not set)", false), DcRules.LdapSigning(null));
        Assert.Equal(new DcValue("Always", true), DcRules.ChannelBinding(2));
        Assert.Equal(new DcValue("When supported", false), DcRules.ChannelBinding(1));
        Assert.Equal(new DcValue("Level 5", true), DcRules.LmLevel(5));
        Assert.Equal(new DcValue("Level 3 (not set)", false), DcRules.LmLevel(null));
        Assert.Equal(new DcValue("Disabled", true), DcRules.Tls(0, 0));
        Assert.Equal(new DcValue("TLS 1.1 enabled", false), DcRules.Tls(0, null));
        Assert.Equal(new DcValue("Enabled (not set)", false), DcRules.Tls(null, null));
        Assert.Equal(new DcValue("AES128, AES256", true), DcRules.Kerberos(0x18));
        Assert.Equal(new DcValue("AES128, AES256", true), DcRules.Kerberos(0x7FFFFFF8)); // with future encryption types
        Assert.Equal(new DcValue("RC4, AES128, AES256", false), DcRules.Kerberos(0x1C));
        Assert.Equal(new DcValue("RC4, AES128, AES256 (not set)", false), DcRules.Kerberos(null));
        Assert.Equal(new DcValue("Configured", true), DcRules.Laps(2, 1));
        Assert.Equal(new DcValue("Backs up to AD, not for DSRM", false), DcRules.Laps(2, null));
        Assert.Equal(new DcValue("Not configured", false), DcRules.Laps(null, null));
        Assert.Equal(new DcValue("Disabled", true), DcRules.Llmnr(0));
        Assert.Equal(new DcValue("Enabled (not set)", false), DcRules.Llmnr(null));
        Assert.Equal(new DcValue("Running", false), DcRules.Spooler(true, 4));
        Assert.Equal(new DcValue("Disabled", true), DcRules.Spooler(false, 4));
        Assert.Equal(new DcValue("Stopped, starts with Windows", false), DcRules.Spooler(false, 2));
    }

    [Fact]
    public void Smb_answers_say_whether_signing_is_required_and_smb1_is_on()
    {
        var request = SmbProbe.Negotiate2();
        Assert.Equal(request.Length - 4, (request[1] << 16) | (request[2] << 8) | request[3]);
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(4 + 64 + 2))); // four dialects

        static byte[] Reply(ushort mode, uint status = 0)
        {
            var b = new byte[64 + 65];
            b[0] = 0xFE;
            "SMB"u8.CopyTo(b.AsSpan(1));
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), status);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(64), 65);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(66), mode);
            return b;
        }
        Assert.True(SmbProbe.SigningRequired(Reply(0x3)));
        Assert.False(SmbProbe.SigningRequired(Reply(0x1)));
        Assert.Null(SmbProbe.SigningRequired(Reply(0x3, 0xC0000022)));
        Assert.Null(SmbProbe.SigningRequired([]));

        // How a Windows Server DC with SMBv1 on answered NT LM 0.12: 17 words, dialect 0.
        var smb1 = Convert.FromHexString("FF534D4272000000009801400000000000000000000000000000FFFE000000001100000F320001000441000000000100" + new string('0', 94));
        Assert.True(SmbProbe.Smb1Answered(smb1));
        Assert.False(SmbProbe.Smb1Answered([]));
        Assert.False(SmbProbe.Smb1Answered(Reply(0x3)));
    }

    [Fact]
    public async Task Each_dc_is_read_and_kept_until_it_is_read_again()
    {
        var dc1 = new SyncDc("DC01", "dc01.amw.com", "HQ", false);
        var dc2 = new SyncDc("DC02", "dc02.amw.com", "HQ", false);
        var ad = new FakeAuditDirectory { Login = new AuditLogin("amw.com", "svc-rabbidlab", "pass", [dc1, dc2]) };
        var probe = new FakeDcProbe();
        probe.Said["DC01"] = new() { ["spool"] = DcRules.Spooler(true, 2), ["smb"] = DcRules.SmbSigning(true) };
        probe.Said["DC02"] = new() { ["spool"] = DcRules.Spooler(false, 4), ["smb"] = DcRules.SmbSigning(true) };
        var clock = new FakeClock();
        var settings = new DcSettings(Folder(), ad, probe, clock, NullLogger<DcSettings>.Instance);

        var first = Json(await settings.View(again: true, null, CancellationToken.None));
        probe.Said["DC01"]["spool"] = DcRules.Spooler(false, 4);
        probe.Said["DC02"]["spool"] = DcRules.Spooler(true, 2);
        clock.Advance(TimeSpan.FromMinutes(5));
        var one = Json(await settings.View(again: true, ["DC01"], CancellationToken.None));

        var spool = Setting(first, "spool");
        Assert.Equal("Running", spool.GetProperty("values").GetProperty("DC01").GetProperty("value").GetString());
        Assert.False(spool.GetProperty("values").GetProperty("DC01").GetProperty("ok").GetBoolean());
        Assert.True(spool.GetProperty("values").GetProperty("DC02").GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Setting(first, "kerb").GetProperty("values").GetProperty("DC01").GetProperty("ok").ValueKind); // not said: not read
        Assert.Equal(DcSettings.All.Count, first.GetProperty("settings").GetArrayLength());
        Assert.Equal("Disabled", Setting(one, "spool").GetProperty("values").GetProperty("DC01").GetProperty("value").GetString()); // read again
        Assert.Equal("Disabled", Setting(one, "spool").GetProperty("values").GetProperty("DC02").GetProperty("value").GetString()); // kept from the first read
        Assert.Equal(["DC01", "DC02"], probe.Asked.Take(2).Order());
        Assert.Equal("DC01", probe.Asked[^1]);
        Assert.True(one.GetProperty("account").GetBoolean());
        Assert.Contains(@"SYSTEM\CurrentControlSet\Services\NTDS\Parameters", one.GetProperty("paths").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task A_dc_that_cannot_be_read_says_why_and_one_gone_from_the_domain_is_dropped()
    {
        var dc1 = new SyncDc("DC01", "dc01.amw.com", "HQ", false);
        var dc2 = new SyncDc("DC02", "dc02.amw.com", "Branch", false);
        var ad = new FakeAuditDirectory { Login = new AuditLogin("amw.com", "svc-rabbidlab", "pass", [dc1, dc2]) };
        var probe = new FakeDcProbe();
        probe.Said["DC01"] = new() { ["smb"] = DcRules.SmbSigning(false) };
        probe.Fails.Add("DC02");
        var settings = new DcSettings(Folder(), ad, probe, new FakeClock(), NullLogger<DcSettings>.Instance);

        var read = Json(await settings.View(again: true, null, CancellationToken.None));
        ad.Login = ad.Login with { Dcs = [dc1] };
        var after = Json(await settings.View(again: true, null, CancellationToken.None));
        ad.Login = null;
        var none = Json(await settings.View(again: true, null, CancellationToken.None));

        var smb = Setting(read, "smb").GetProperty("values");
        Assert.Equal("Not required", smb.GetProperty("DC01").GetProperty("value").GetString());
        Assert.Contains("DC02 could not be read", smb.GetProperty("DC02").GetProperty("problem").GetString());
        Assert.Equal("Branch", read.GetProperty("dcs").EnumerateArray().Single(d => d.GetProperty("name").GetString() == "DC02").GetProperty("site").GetString());
        Assert.Equal(["DC01"], after.GetProperty("dcs").EnumerateArray().Select(d => d.GetProperty("name").GetString()));
        Assert.False(none.GetProperty("account").GetBoolean());
    }

    private static JsonElement Json(object view) => JsonSerializer.SerializeToElement(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static JsonElement Setting(JsonElement view, string key) => view.GetProperty("settings").EnumerateArray().Single(s => s.GetProperty("key").GetString() == key);

    private DataFolder Folder() => DataFolder.Resolve(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = Path.Combine(data, Guid.NewGuid().ToString("N")) }).Build());
}

/// <summary>What each DC says when read, and the ones whose read fails.</summary>
public sealed class FakeDcProbe : IDcProbe
{
    public Dictionary<string, Dictionary<string, DcValue>> Said { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Fails { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Asked { get; } = [];

    public Task<Dictionary<string, DcValue>> Read(SyncDc dc, AuditLogin login, CancellationToken stop)
    {
        lock (Asked)
        {
            Asked.Add(dc.Name);
        }
        if (Fails.Contains(dc.Name))
        {
            throw new IOException("The network path was not found.");
        }
        return Task.FromResult(new Dictionary<string, DcValue>(Said.GetValueOrDefault(dc.Name) ?? []));
    }
}
