using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// Devices, Deploy the client: every computer in the domain with the client on it or not, and one computer's install log
/// read from that computer as an administrator of it (ClientDeployStatus.cs). The log lines are DC01's of 2026-11-27,
/// where the service did not start on .NET Framework 4.6.2.
/// </summary>
public sealed class ClientDeployStatusTests : IDisposable
{
    private const string Domain = "DC=amw,DC=com";
    private static readonly GpoLogin None = new(null, null);
    private static readonly CancellationToken Stop = CancellationToken.None;
    private static readonly string[] Failed =
    [
        "MSI (s) (04:E8) [23:21:30:789]: Executing op: ServiceInstall(Name=RabbidLabClient,DisplayName=Rabbid Lab Client,StartType=2)",
        "MSI (s) (04:E8) [23:21:32:609]: Executing op: ServiceControl(,Name=RabbidLabClient,Action=1,Wait=1,)",
        "MSI (s) (04:E8) [23:22:02:762]: Product: Rabbid Lab Client -- Error 1920. Service 'Rabbid Lab Client' (RabbidLabClient) failed to start.  Verify that you have sufficient privileges to start system services.",
        "",
        "Action ended 23:22:02: InstallFinalize. Return value 3.",
        "MSI (s) (04:E8) [23:22:02:933]: Product: Rabbid Lab Client -- Installation failed.",
        "MSI (s) (04:E8) [23:22:02:933]: Windows Installer installed the product. Product Name: Rabbid Lab Client. Product Version: 1.1.442. Product Language: 0. Manufacturer: Rabbid. Installation success or error status: 1603.",
    ];

    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-deploy-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;
    private readonly FakeLdap ad = new();
    private readonly FakeRemoteFiles remote = new();
    private readonly DirectoryTools tools;
    private readonly List<RabbidLab.Server.Client> clients =
    [
        new() { Id = "a", Computer = "pc01", Version = "1.1.444", Approved = DateTime.UtcNow, LastSeen = DateTime.UtcNow },
        new() { Id = "b", Computer = "PC02", Version = "1.1.444", LastSeen = DateTime.UtcNow.AddDays(-1) },
    ];

    public ClientDeployStatusTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
        var s = app.Services;
        tools = new DirectoryTools(s.GetRequiredService<DataFolder>(), s.GetRequiredService<SettingsStore>(), s.GetRequiredService<Accounts>(), app.Finder, ad.Open) { Remote = remote, Sysvol = new FakeSysvol(), Gpmc = new FakeGpmc() };
        void Computer(string name, int flags, string os) => ad.Add($"CN={name},OU=Lab,{Domain}", ("objectClass", "computer"), ("objectCategory", "computer"), ("cn", name), ("sAMAccountName", name + "$"),
            ("dNSHostName", name.ToLowerInvariant() + ".amw.com"), ("userAccountControl", flags.ToString()), ("operatingSystem", os));
        Computer("DC01", 0x82000, "Windows Server 2016 Standard");
        Computer("PC01", 0x1000, "Windows 11 Pro");
        Computer("PC02", 0x1000, "Windows 11 Pro");
        Computer("OLD01", 0x1002, "Windows 10 Pro"); // disabled
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

    private Task Connect() => tools.Connect(new SetupForm("amw.com", "svc-existing", "right", Existing: true, null, null, Domain), "admin", Stop);

    private static byte[] Utf16(IEnumerable<string> lines, bool mark = true) => [.. mark ? Encoding.Unicode.GetPreamble() : [], .. Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")];

    private static RemoteFileEnd Whole(IEnumerable<string> lines)
    {
        var bytes = Utf16(lines);
        return new RemoteFileEnd(bytes, new DateTime(2026, 11, 28, 4, 22, 2, DateTimeKind.Utc), bytes.Length);
    }

    [Fact]
    public void A_failed_install_log_gives_the_result_the_error_and_its_last_lines()
    {
        var read = InstallLog.Parse(Whole(Failed), DateTime.UtcNow, "admin");

        Assert.Equal(1603, read.Status);
        Assert.Equal("1.1.442", read.Version);
        Assert.StartsWith("Error 1920. Service 'Rabbid Lab Client' (RabbidLabClient) failed to start.", read.Reason);
        Assert.Equal(6, read.Tail!.Split('\n').Length); // the empty line is left out
        Assert.EndsWith("Installation success or error status: 1603.", read.Tail);
        Assert.Contains("1.1.444", InstallLog.Advice(read));
    }

    [Fact]
    public void A_log_that_ended_well_has_no_error_and_a_computer_without_a_log_has_no_result()
    {
        var ok = InstallLog.Parse(Whole(["MSI (s) (04:E8) [23:22:02:762]: Product: Rabbid Lab Client -- Error 1920. An earlier try.",
            "MSI (s) (10:20) [00:01:02:003]: Product: Rabbid Lab Client -- Installation completed successfully.",
            "MSI (s) (10:20) [00:01:02:003]: Windows Installer installed the product. Product Name: Rabbid Lab Client. Product Version: 1.1.444. Product Language: 0. Manufacturer: Rabbid. Installation success or error status: 0."]), DateTime.UtcNow, "admin");
        var none = InstallLog.Parse(null, DateTime.UtcNow, "admin");

        Assert.Equal(0, ok.Status);
        Assert.Equal("1.1.444", ok.Version);
        Assert.Null(ok.Reason);
        Assert.Null(InstallLog.Advice(ok));
        Assert.Null(none.Written);
        Assert.Null(none.Status);
        Assert.Null(InstallLog.Advice(none));
    }

    [Fact]
    public void The_end_of_a_long_log_is_read_without_its_cut_first_line_and_the_net_check_says_what_to_install()
    {
        // As read from the middle of the file: no byte order mark, and the first line is part of one.
        var bytes = Utf16(["of a line that was cut", "MSI (s) (04:E8) [23:22:02:762]: Product: Rabbid Lab Client -- The Rabbid Lab client requires .NET Framework 4.6.2 or later. Install .NET Framework 4.8 on this computer, then run the installer again.",
            "MSI (s) (04:E8) [23:22:02:933]: Windows Installer installed the product. Product Name: Rabbid Lab Client. Product Version: 1.1.444. Product Language: 0. Manufacturer: Rabbid. Installation success or error status: 1603."], mark: false);
        var read = InstallLog.Parse(new RemoteFileEnd(bytes, DateTime.UtcNow, bytes.Length + 5000), DateTime.UtcNow, "admin");

        Assert.Equal(1603, read.Status);
        Assert.StartsWith("The Rabbid Lab client requires .NET Framework 4.6.2 or later.", read.Reason);
        Assert.DoesNotContain("was cut", read.Tail);
        Assert.Equal("Install .NET Framework 4.8 on this computer, then click Deploy again.", InstallLog.Advice(read));
    }

    [Fact]
    public async Task Every_enabled_computer_is_listed_with_the_client_on_it_or_not()
    {
        await Connect();

        // The stand-in directory has computers of its own; these four are this test's.
        var list = (await tools.DeployComputers(clients, c => c.Id == "a", Stop)).Where(c => c.Name is "DC01" or "PC01" or "PC02" or "OLD01").ToList();

        Assert.Equal(["DC01", "PC01", "PC02"], list.Select(c => c.Name)); // OLD01 is disabled
        Assert.Equal(["none", "installed", "pending"], list.Select(c => c.State));
        Assert.True(list[0].Controller);
        Assert.Equal("Windows Server 2016 Standard", list[0].Os);
        Assert.Equal("1.1.444", list[1].Version);
        Assert.True(list[1].Online);
        Assert.False(list[2].Online);
        Assert.Empty(remote.Asked); // nothing is asked of any computer
    }

    [Fact]
    public async Task Reading_a_log_connects_to_the_name_Active_Directory_has_as_the_service_account_and_keeps_the_result()
    {
        await Connect();
        remote.Files[@"\\dc01.amw.com\ADMIN$\Temp\RabbidLab-Client-install.log"] = Whole(Failed);

        var dc = (await tools.ReadInstallLog("dc01$", None, "admin", clients, _ => false, Stop)).Computer;
        var listed = (await tools.DeployComputers(clients, _ => false, Stop)).First(c => c.Name == "DC01");

        Assert.Equal(("svc-existing@amw.com", "right"), (remote.Asked[0].User, remote.Asked[0].Password));
        Assert.Equal("failed", dc.State);
        Assert.Equal(1603, dc.Log!.Status);
        Assert.Equal("admin", dc.Log.By);
        Assert.Contains("Deploy again", dc.Advice);
        Assert.Equal("failed", listed.State); // kept
        Assert.StartsWith("Error 1920.", listed.Log!.Reason);
    }

    [Fact]
    public async Task A_computer_with_no_log_says_so_and_one_not_in_Active_Directory_is_never_connected_to()
    {
        await Connect();

        var pc = (await tools.ReadInstallLog("PC02", None, "admin", [], _ => false, Stop)).Computer;
        var missing = await Assert.ThrowsAsync<ToolProblem>(() => tools.ReadInstallLog(@"evil.example\share", new GpoLogin(@"AMW\admin", "secret"), "admin", clients, _ => false, Stop));

        Assert.Equal("none", pc.State);
        Assert.NotNull(pc.Log);
        Assert.Null(pc.Log!.Written);
        Assert.Contains("no longer exists in Active Directory", missing.Message);
        Assert.Single(remote.Asked); // only PC02, by its own name
        Assert.Equal(@"\\pc02.amw.com\ADMIN$\Temp\RabbidLab-Client-install.log", remote.Asked[0].Path);
    }

    [Fact]
    public async Task When_the_service_account_is_not_an_administrator_there_a_login_is_asked_for_and_a_wrong_one_is_refused()
    {
        await Connect();
        remote.Denied = true;

        var ask = await Assert.ThrowsAsync<GpoNeedsLogin>(() => tools.ReadInstallLog("DC01", None, "admin", clients, _ => false, Stop));
        var refused = await Assert.ThrowsAsync<ToolProblem>(() => tools.ReadInstallLog("DC01", new GpoLogin(@"AMW\helpdesk", "pw"), "admin", clients, _ => false, Stop));
        remote.Denied = false;
        remote.Files[@"\\dc01.amw.com\ADMIN$\Temp\RabbidLab-Client-install.log"] = Whole(Failed);
        var read = (await tools.ReadInstallLog("DC01", new GpoLogin(@"AMW\admin", "secret"), "admin", clients, _ => false, Stop)).Computer;

        Assert.Contains("used once", ask.Message);
        Assert.Contains("not saved", ask.Message);
        Assert.Contains("is not an administrator on DC01", refused.Message);
        Assert.Equal((@"AMW\admin", "secret"), (remote.Asked[^1].User, remote.Asked[^1].Password));
        Assert.Equal("failed", read.State);
        Assert.DoesNotContain("secret", File.ReadAllText(Directory.GetFiles(data, "client-deploy.json", SearchOption.AllDirectories)[0])); // the login is not kept
    }

    [Fact]
    public async Task A_computer_that_is_off_says_what_it_needs()
    {
        await Connect();
        remote.Unreachable = true;

        var problem = await Assert.ThrowsAsync<ToolProblem>(() => tools.ReadInstallLog("PC01", None, "admin", clients, _ => false, Stop));

        Assert.StartsWith("Could not connect to PC01: The network path was not found.", problem.Message);
        Assert.Contains("File and Printer Sharing", problem.Message);
    }
}

/// <summary>Stands in for other computers' files: what was asked for, as whom.</summary>
public sealed class FakeRemoteFiles : IRemoteFiles
{
    public Dictionary<string, RemoteFileEnd> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Path, string User, string Password)> Asked { get; } = [];
    public bool Denied { get; set; }
    public bool Unreachable { get; set; }

    public Task<RemoteFileEnd?> ReadEnd(string path, int most, string username, string password, CancellationToken stop)
    {
        Asked.Add((path, username, password));
        return Denied ? throw new UnauthorizedAccessException() : Unreachable ? throw new IOException("The network path was not found.") : Task.FromResult(Files.GetValueOrDefault(path));
    }
}
