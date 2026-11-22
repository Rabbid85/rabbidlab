using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Finding the print servers: which computers Active Directory says to ask, what's kept of their answers, and one list from both.</summary>
public sealed class PrintServersTests : IDisposable
{
    private const string Lab = "OU=Lab,DC=amw,DC=com";

    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-prints-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;
    private readonly FakeLdap ad = new();
    private readonly DirectoryTools tools;
    private readonly FakePrintServerProbe probe = new();
    private readonly FakeClock clock = new();

    public PrintServersTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
        var services = app.Services;
        tools = new DirectoryTools(services.GetRequiredService<DataFolder>(), services.GetRequiredService<SettingsStore>(), services.GetRequiredService<Accounts>(), app.Finder, ad.Open);
    }

    public void Dispose()
    {
        tools.Dispose();
        app.Dispose();
        try
        {
            Directory.Delete(data, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temp folder's own cleanup.
        }
    }

    private static string FileTime(int daysAgo) => DateTime.UtcNow.AddDays(-daysAgo).ToFileTimeUtc().ToString(CultureInfo.InvariantCulture);

    private void Computer(string name, string os, int seenDaysAgo = 1, bool disabled = false) =>
        ad.Add($"CN={name},{Lab}", ("objectClass", "computer"), ("objectCategory", "computer"), ("name", name), ("dNSHostName", $"{name.ToLowerInvariant()}.amw.com"),
            ("operatingSystem", os), ("userAccountControl", disabled ? "4098" : "4096"), ("lastLogonTimestamp", FileTime(seenDaysAgo)));

    private static SharedPrinter Shared(string server, string name, string? driver = null, bool published = false) =>
        new(name, server, $@"\\{server.ToLowerInvariant()}.amw.com\{name}", driver, null, null, null, published);

    private static Task<List<AdServer>> Two() => Task.FromResult<List<AdServer>>([new("PRINT01", "print01.amw.com"), new("FILE01", "file01.amw.com")]);

    [Fact]
    public async Task The_servers_asked_are_the_enabled_Windows_Servers_seen_lately_and_any_computer_that_published_a_printer()
    {
        await tools.Connect(new SetupForm("amw.com", "svc-existing", "right", Existing: true, null, null, Lab), "admin", CancellationToken.None);
        Computer("PRINT01", "Windows Server 2022 Standard");
        Computer("OLD01", "Windows Server 2012 R2 Standard", seenDaysAgo: 200);
        Computer("OFF01", "Windows Server 2019 Standard", disabled: true);
        Computer("DESK01", "Windows 11 Pro");
        Computer("DESK02", "Windows 11 Pro");
        ad.Add($"CN=DESK02-Label,CN=DESK02,{Lab}", ("objectClass", "printQueue"), ("objectCategory", "printQueue"), ("printerName", "Label"),
            ("shortServerName", "DESK02"), ("serverName", "desk02.amw.com"), ("uNCName", @"\\desk02.amw.com\Label"));

        var servers = await tools.PrintServerCandidates(CancellationToken.None);

        Assert.Equal(["DESK02", "PRINT01"], servers.Select(s => s.Name));
        Assert.Equal("print01.amw.com", servers.Single(s => s.Name == "PRINT01").Host);
    }

    [Fact]
    public async Task Every_printer_the_servers_share_is_listed_and_only_print_servers_and_the_ones_that_could_not_be_asked_show()
    {
        probe.Answers["PRINT01"] = new([Shared("PRINT01", "Front-Office"), Shared("PRINT01", "Warehouse")]);
        probe.Answers["DOWN01"] = new([], "No response on port 445 (File and Printer Sharing).");
        var search = new PrintServers(probe, clock);
        static Task<List<AdServer>> Three() => Task.FromResult<List<AdServer>>([new("PRINT01", "print01.amw.com"), new("FILE01", "file01.amw.com"), new("DOWN01", "down01.amw.com")]);

        var scan = await search.Read(Three, again: false, CancellationToken.None);

        Assert.False(scan.Scanning);
        Assert.Equal(["Front-Office", "Warehouse"], scan.Printers.Select(p => p.Name));
        Assert.Equal(["PRINT01", "DOWN01"], scan.Servers.Select(s => s.Server)); // FILE01 answered and shares none: not a print server
        Assert.Equal(2, scan.Servers[0].Printers);
        Assert.Equal("No response on port 445 (File and Printer Sharing).", scan.Servers[1].Problem);
    }

    [Fact]
    public async Task The_servers_are_asked_once_and_again_only_when_asked_to_a_minute_later_or_after_six_hours()
    {
        var search = new PrintServers(probe, clock);

        await search.Read(Two, again: false, CancellationToken.None);
        await search.Read(Two, again: false, CancellationToken.None);
        await search.Read(Two, again: true, CancellationToken.None); // Refresh straight away: still fresh enough
        Assert.Equal(2, probe.Asked.Count);

        clock.Advance(TimeSpan.FromMinutes(2));
        await search.Read(Two, again: true, CancellationToken.None);
        Assert.Equal(4, probe.Asked.Count);

        clock.Advance(TimeSpan.FromHours(7));
        await search.Read(Two, again: false, CancellationToken.None);
        Assert.Equal(6, probe.Asked.Count);
    }

    [Fact]
    public async Task A_server_Active_Directory_no_longer_names_drops_out_with_its_printers()
    {
        probe.Answers["PRINT01"] = new([Shared("PRINT01", "Front-Office")]);
        probe.Answers["FILE01"] = new([Shared("FILE01", "Scans")]);
        var search = new PrintServers(probe, clock);
        await search.Read(Two, again: false, CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(7));
        var scan = await search.Read(() => Task.FromResult<List<AdServer>>([new("PRINT01", "print01.amw.com")]), again: false, CancellationToken.None);

        Assert.Equal(["Front-Office"], scan.Printers.Select(p => p.Name));
    }

    [Fact]
    public void A_printer_in_Active_Directory_and_on_its_server_is_one_row()
    {
        var published = new List<AdPrinter>
        {
            new("Front-Office", "PRINT01", @"\\print01.amw.com\Front-Office", "HP Universal Printing PCL 6", "Front office", null, null),
            new("Gone", "PRINT09", @"\\print09.amw.com\Gone", null, null, null, null),
        };
        var found = new List<SharedPrinter> { Shared("PRINT01", "Front-Office", published: true), Shared("PRINT01", "Warehouse", "ZDesigner ZT411") };

        var rows = PrintServers.Together(published, Scan(found, "PRINT01"));

        Assert.Equal(["Front-Office", "Gone", "Warehouse"], rows.Select(r => r.Name));
        Assert.True(rows[0] is { Published: true, Found: true, Location: "Front office", Driver: "HP Universal Printing PCL 6" });
        Assert.True(rows[1] is { Published: true, Found: false }); // still in Active Directory, but its server wasn't asked
        Assert.True(rows[2] is { Published: false, Found: true, Driver: "ZDesigner ZT411" });
    }

    private static PrintScan Scan(List<SharedPrinter> found, params string[] answered) =>
        new(found, [], DateTime.UtcNow, false, new HashSet<string>(answered, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void What_looks_wrong_with_a_printer_is_said_in_plain_words()
    {
        var published = new List<AdPrinter>
        {
            new("Front-Office", "PRINT01", @"\\print01.amw.com\Front-Office", null, null, null, null),
            new("Old-Plotter", "PRINT01", @"\\print01.amw.com\Old-Plotter", null, null, null, "IP_10.1.2.40"),
            new("Elsewhere", "PRINT09", @"\\print09.amw.com\Elsewhere", null, null, null, null),
        };
        var found = new List<SharedPrinter>
        {
            Shared("PRINT01", "Front-Office", published: true) with { Ip = "10.1.2.30", Status = "Offline", Jobs = 4 },
            Shared("PRINT01", "Front-Office-Colour") with { Ip = "10.1.2.30" },
            Shared("PRINT02", "Lab") with { Ip = "10.1.2.31" },
        };

        var rows = PrintServers.Together(published, Scan(found, "PRINT01", "PRINT02")).ToDictionary(r => r.Name);

        Assert.Equal(["The queue reports: Offline."], rows["Front-Office"].Problems);
        Assert.Equal(4, rows["Front-Office"].Jobs);
        // The second queue at one address, and the one printer its server left out of Active Directory.
        Assert.Equal(["Same IP address as Front-Office on PRINT01. This is likely one printer shared twice.",
            "Shared but not published in Active Directory, so it does not appear when users search for printers."], rows["Front-Office-Colour"].Problems);
        // PRINT02 lists nothing in Active Directory at all: not listing is how it's run, not a problem.
        Assert.Empty(rows["Lab"].Problems);
        // PRINT01 answered and doesn't share it any more; PRINT09 wasn't asked, so nothing is said.
        Assert.Equal(["Published in Active Directory, but PRINT01 no longer shares it. The directory listing is stale."], rows["Old-Plotter"].Problems);
        Assert.Equal("10.1.2.40", rows["Old-Plotter"].Ip);
        Assert.Empty(rows["Elsewhere"].Problems);
    }

    [Theory]
    [InlineData("IP_10.1.2.30", "10.1.2.30")]
    [InlineData("10.1.2.30_1", "10.1.2.30")]
    [InlineData("192.168.003.020", "192.168.3.20")]
    [InlineData("WSD-6d91f834-df0c-4210-b724-4409dea449c5", null)]
    [InlineData("IP_999.1.2.3", null)]
    [InlineData("printer7.amw.com", null)]
    [InlineData(null, null)]
    public void A_printers_address_is_read_from_its_ports_name(string? port, string? ip) => Assert.Equal(ip, PrintServers.IpIn(port));

    [Theory]
    [InlineData(0u, 0u, null)]
    [InlineData(0x400u, 0u, null)] // printing: busy, not broken
    [InlineData(0x80u, 0u, "Offline")]
    [InlineData(0x8u | 0x2u, 0u, "Error")]
    [InlineData(0x20000u, 0u, "Toner low")]
    [InlineData(0x1u, 0u, "Paused")]
    [InlineData(0u, 0x400u, "Offline (Use Printer Offline is on)")]
    public void A_queues_status_is_said_only_when_something_is_wrong(uint status, uint attributes, string? text) =>
        Assert.Equal(text, PrintServers.QueueStatus(status, attributes));

    /// <summary>
    /// The real Windows calls, against this computer only: its own spooler lists its printers (a struct read wrong would
    /// give junk names), and its own shares can be listed. Nothing on the network is asked.
    /// </summary>
    [Fact]
    public void The_Windows_calls_read_this_computers_own_printers_and_shares()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var here = new AdServer(Environment.MachineName, Environment.MachineName);
        List<SharedPrinter> printers;
        try
        {
            printers = WindowsPrintServerProbe.Printers(here, shared: false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return; // the spooler is stopped here: nothing to read
        }
        Assert.All(printers, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.DoesNotContain('\0', p.Name);
            Assert.False(p.Name.StartsWith(@"\\", StringComparison.Ordinal));
        });
        try
        {
            Assert.All(WindowsPrintServerProbe.PrintShares(Environment.MachineName), s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The Server service is off here.
        }
    }
}
