using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// Group Policy against the stand-in for Active Directory (FakeLdap) and a stand-in for Group Policy Management: links in
/// the order Windows applies them, names, status, WMI filters, a domain admin's login when the service account isn't
/// allowed, and new GPOs, deletes and backups through Group Policy Management. No test reaches a domain.
/// </summary>
public sealed class GroupPolicyTests : IDisposable
{
    private const string Domain = "DC=amw,DC=com";
    private const string Lab = "OU=Lab," + Domain;
    private const string Policies = "CN=Policies,CN=System," + Domain;
    private const string Som = "CN=SOM,CN=WMIPolicy,CN=System," + Domain;
    private const string Ddp = "{31B2F340-016D-11D2-945F-00C04FB984F9}";
    private const string Firewall = "{AAAAAAAA-0000-0000-0000-000000000001}";
    private const string Drives = "{AAAAAAAA-0000-0000-0000-000000000002}";
    private const string Spare = "{AAAAAAAA-0000-0000-0000-000000000003}";
    private const string Win11 = "{FFFFFFFF-0000-0000-0000-000000000001}";
    private static readonly GpoLogin None = new(null, null);
    private static readonly CancellationToken Stop = CancellationToken.None;

    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-gpo-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;
    private readonly FakeLdap ad = new();
    private readonly FakeGpmc gpmc = new();
    private readonly DirectoryTools tools;

    public GroupPolicyTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
        var s = app.Services;
        tools = new DirectoryTools(s.GetRequiredService<DataFolder>(), s.GetRequiredService<SettingsStore>(), s.GetRequiredService<Accounts>(), app.Finder, ad.Open) { Gpmc = gpmc };
        static string Link(string id, int flags) => $"[{GpoLinks.PathOf(id, Domain)};{flags}]";
        ad.Rows.Find(r => r.Dn == Domain)!.Values["gPLink"] = [Link(Ddp, 0)];
        var lab = ad.Rows.Find(r => r.Dn == Lab)!.Values;
        lab["gPLink"] = [Link(Firewall, 0) + Link(Drives, 2)]; // Drives applies last: link order 1, enforced
        lab["gPOptions"] = ["1"];
        void Gpo(string id, string name, int flags, long version, string? wmi = null)
        {
            ad.Add($"CN={id},{Policies}", ("objectClass", "groupPolicyContainer"), ("cn", id), ("displayName", name), ("flags", flags.ToString()), ("versionNumber", version.ToString()),
                ("gPCMachineExtensionNames", "[{827D319E-6EAC-11D2-A4EA-00C04F79F83A}{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}]"), ("whenChanged", "20261110120000.0Z"));
            if (wmi is not null)
            {
                ad.Rows[^1].Values["gPCWQLFilter"] = [$"[amw.com;{wmi};0]"];
            }
        }
        Gpo(Ddp, "Default Domain Policy", 0, 13);
        Gpo(Firewall, "C-Firewall", 1, (2L << 16) | 5, Win11);
        Gpo(Drives, "U-Drive maps", 2, 108L << 16);
        Gpo(Spare, "Test - spare", 3, 0);
        gpmc.Made = (id, name) => Gpo(id, name, 0, 0);
        ad.Add($"CN={Win11},{Som}", ("objectClass", "msWMI-Som"), ("msWMI-ID", Win11), ("msWMI-Name", "Windows 11 only"),
            ("msWMI-Parm2", WmiQueries.Build(@"root\CIMv2", "SELECT * FROM Win32_OperatingSystem WHERE ProductType = 1")), ("msWMI-ChangeDate", "20260101120000.000000-000"));
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

    // The gPLink last written to a place, in link order.
    private List<GpoLinks.Entry> Written(string dn) => GpoLinks.Parse((string) ad.Mods.Last(m => m.Dn == dn && m.Mod.Attribute == "gPLink").Mod.Values[0]);

    [Fact]
    public void Links_read_in_the_order_they_apply_and_go_back_as_they_were()
    {
        var raw = $"[LDAP://cn={Firewall},cn=policies,cn=system,{Domain};0][LDAP://CN={Drives},CN=Policies,CN=System,{Domain};3][LDAP://cn={{11111111-2222-3333-4444-555555555555}},cn=policies,cn=system,DC=other,DC=net;1]";
        var links = GpoLinks.Parse(raw);

        Assert.Equal(["{11111111-2222-3333-4444-555555555555}", Drives, Firewall], links.Select(l => l.Gpo));
        Assert.Equal(3, links[1].Flags);
        Assert.Equal(raw, GpoLinks.Build(links));
        Assert.Equal(" ", GpoLinks.Build([]));
        Assert.Empty(GpoLinks.Parse(" "));
    }

    [Fact]
    public void Wmi_filter_queries_read_and_write_as_Active_Directory_keeps_them()
    {
        Assert.Equal(@"1;3;10;27;WQL;root\CIMv2;SELECT * FROM Win32_Battery;", WmiQueries.Build(@"root\CIMv2", "SELECT * FROM Win32_Battery"));
        var two = WmiQueries.Parse(@"2;3;10;27;WQL;root\CIMv2;SELECT * FROM Win32_Battery;3;10;8;WQL;root\CIMv2;SELECT 1;");
        Assert.Equal(new List<(string, string)> { (@"root\CIMv2", "SELECT * FROM Win32_Battery"), (@"root\CIMv2", "SELECT 1") }, two);
        Assert.Single(WmiQueries.Parse(@"1;3;10;12;WQL;root\CIMv2;SELECT a; b;"));
        Assert.Empty(WmiQueries.Parse("not a filter"));
        Assert.Equal(new DateTime(2021, 11, 25, 6, 29, 7, DateTimeKind.Utc), WmiQueries.Date("20211125062907.000000-000"));
    }

    [Fact]
    public async Task The_page_lists_GPOs_where_they_are_linked_and_WMI_filters()
    {
        await Connect();
        var gp = await tools.GroupPolicy(Stop);

        Assert.Equal(["C-Firewall", "Default Domain Policy", "Test - spare", "U-Drive maps"], gp.Gpos.Select(g => g.Name));
        var fw = gp.Gpos.Single(g => g.Id == Firewall);
        Assert.Equal((1, 5, 2, Win11), (fw.Status, fw.ComputerVersion, fw.UserVersion, fw.Wmi));
        Assert.True(gp.Places[0].Domain);
        var lab = gp.Places.Single(p => p.Dn == Lab);
        Assert.True(lab.Blocked);
        Assert.Equal("amw.com/Lab", lab.Path);
        Assert.Equal([(Drives, 1, true), (Firewall, 2, false)], lab.Links.Select(l => (l.Gpo, l.Order, l.Enforced)));
        Assert.Equal("SELECT * FROM Win32_OperatingSystem WHERE ProductType = 1", gp.Filters.Single().Query);
        Assert.True(gp.Gpmc);
    }

    [Fact]
    public async Task A_new_link_goes_last_and_links_move_turn_off_enforce_and_go()
    {
        await Connect();

        await tools.Link(Spare, Lab, new GpoLinkChange(Add: true, Enforced: true), None, Stop);
        Assert.Equal([(Drives, 2), (Firewall, 0), (Spare, 2)], Written(Lab).Select(e => (e.Gpo, e.Flags)));

        await tools.Link(Firewall, Lab, new GpoLinkChange(Order: 1), None, Stop);
        Assert.Equal([Firewall, Drives], Written(Lab).Select(e => e.Gpo));

        await tools.Link(Drives, Lab, new GpoLinkChange(Enabled: false, Enforced: false), None, Stop);
        Assert.Equal(GpoLinks.Disabled, Written(Lab).Single(e => e.Gpo == Drives).Flags);

        await tools.Link(Drives, Lab, new GpoLinkChange(Remove: true), None, Stop);
        Assert.Equal([Firewall], Written(Lab).Select(e => e.Gpo));

        Assert.Contains("already linked", (await Assert.ThrowsAsync<ToolProblem>(() => tools.Link(Drives, Lab, new GpoLinkChange(Add: true), None, Stop))).Message);
        Assert.Contains("no longer linked", (await Assert.ThrowsAsync<ToolProblem>(() => tools.Link(Spare, Lab, new GpoLinkChange(Remove: true), None, Stop))).Message);
        await Assert.ThrowsAsync<ToolProblem>(() => tools.Link(Spare, "OU=Elsewhere,DC=other,DC=net", new GpoLinkChange(Add: true), None, Stop));
    }

    [Fact]
    public async Task Names_status_inheritance_and_WMI_filters_are_attributes_changed_in_place()
    {
        await Connect();
        await tools.RenameGpo(Spare, "Test - kept", None, Stop);
        await tools.SetGpoStatus(Ddp, computer: false, user: true, None, Stop);
        await tools.SetBlocked(Lab, false, None, Stop);
        await tools.SetGpoWmi(Ddp, Win11, None, Stop);
        await tools.SetGpoWmi(Firewall, "", None, Stop);

        string Value(string attribute) => (string) ad.Mods.Last(m => m.Mod.Attribute == attribute && m.Mod.Values.Length > 0).Mod.Values[0];
        Assert.Equal("Test - kept", Value("displayName"));
        Assert.Equal("2", Value("flags"));
        Assert.Equal("0", Value("gPOptions"));
        Assert.Equal($"[amw.com;{Win11};0]", Value("gPCWQLFilter"));
        Assert.Contains(ad.Mods, m => m.Mod.Attribute == "gPCWQLFilter" && m.Mod.Change == LdapChange.Delete && m.Dn.Contains(Firewall));
        Assert.Contains("GPO named default domain policy already exists", (await Assert.ThrowsAsync<ToolProblem>(() => tools.RenameGpo(Spare, "default domain policy", None, Stop))).Message);
        await Assert.ThrowsAsync<ToolProblem>(() => tools.SetBlocked(Domain, true, None, Stop));
        await Assert.ThrowsAsync<ToolProblem>(() => tools.SetGpoWmi(Ddp, "{00000000-0000-0000-0000-00000000000F}", None, Stop));
    }

    [Fact]
    public async Task WMI_filters_are_made_changed_and_only_deleted_when_nothing_uses_them()
    {
        await Connect();
        var id = await tools.SaveWmiFilter(null, "Laptops", "Anything with a battery", "SELECT *\r\n  FROM Win32_Battery", null, None, "awiles", Stop);
        var added = ad.Rows.Single(r => r.Dn == $"CN={id},{Som}");
        Assert.Equal(@"1;3;10;27;WQL;root\CIMv2;SELECT * FROM Win32_Battery;", added.Values["msWMI-Parm2"][0]);
        Assert.Equal("awiles", added.Values["msWMI-Author"][0]);

        await tools.SaveWmiFilter(Win11, "Windows 11", null, "SELECT * FROM Win32_OperatingSystem", null, None, "awiles", Stop);
        Assert.Contains(ad.Mods, m => m.Mod.Attribute == "msWMI-Name" && (string) m.Mod.Values[0] == "Windows 11");

        Assert.Contains("WMI filter named laptops already exists", (await Assert.ThrowsAsync<ToolProblem>(() => tools.SaveWmiFilter(null, "laptops", null, "SELECT 1 FROM X", null, None, "awiles", Stop))).Message);
        await Assert.ThrowsAsync<ToolProblem>(() => tools.SaveWmiFilter(null, "Bad", null, "DELETE FROM Win32_Battery", null, None, "awiles", Stop));
        await Assert.ThrowsAsync<ToolProblem>(() => tools.SaveWmiFilter(null, "Bad", null, "SELECT 1; SELECT 2", null, None, "awiles", Stop));
        Assert.Contains("C-Firewall uses this filter", (await Assert.ThrowsAsync<ToolProblem>(() => tools.DeleteWmiFilter(Win11, None, Stop))).Message);
        await tools.DeleteWmiFilter(id, None, Stop);
        Assert.DoesNotContain(ad.Rows, r => r.Dn == $"CN={id},{Som}");
    }

    [Fact]
    public async Task When_the_service_account_is_refused_a_domain_admins_login_is_asked_for_and_used()
    {
        await Connect();
        ad.Fail = what => what.StartsWith("modify", StringComparison.Ordinal) ? new LdapRefused(LdapRefused.InsufficientRights, "Insufficient access") : null;

        var asked = await Assert.ThrowsAsync<GpoNeedsLogin>(() => tools.SetBlocked(Lab, false, None, Stop));
        Assert.Contains("svc-existing does not have permission", asked.Message);
        var wrong = await Assert.ThrowsAsync<GpoNeedsLogin>(() => tools.SetBlocked(Lab, false, new GpoLogin(@"AMW\awiles", "wrong"), Stop));
        Assert.Contains("incorrect", wrong.Message);
        // A login that signs in but isn't allowed either is said plainly, not asked for again.
        var refused = await Assert.ThrowsAsync<ToolProblem>(() => tools.SetBlocked(Lab, false, new GpoLogin(@"AMW\awiles", "right"), Stop));
        Assert.Contains("awiles does not have permission", refused.Message);

        ad.Fail = null;
        gpmc.Deny = true;
        await Assert.ThrowsAsync<GpoNeedsLogin>(() => tools.NewGpo("C-New", null, null, None, "awiles", Stop));
        gpmc.Deny = false;
        await tools.NewGpo("C-New", null, null, new GpoLogin("awiles", "right"), "awiles", Stop);
        Assert.Equal("awiles@amw.com", gpmc.As);
    }

    [Fact]
    public async Task New_GPOs_copies_deletes_and_restores_go_through_Group_Policy_Management()
    {
        await Connect();
        var (id, warning) = await tools.NewGpo("C-Screen lock", null, Lab, None, "awiles", Stop);
        Assert.Null(warning);
        Assert.Equal($"create C-Screen lock", gpmc.Calls[0]);
        Assert.Equal(id, Written(Lab).Last().Gpo);
        Assert.Equal("svc-existing@amw.com", gpmc.As);

        await tools.NewGpo("C-Firewall 2", Firewall, null, None, "awiles", Stop);
        Assert.Equal($"copy {Firewall} as C-Firewall 2", gpmc.Calls[1]);
        Assert.Contains("GPO named c-firewall already exists", (await Assert.ThrowsAsync<ToolProblem>(() => tools.NewGpo("c-firewall", null, null, None, "awiles", Stop))).Message);

        // Kept first, then its links go, then the GPO.
        gpmc.Calls.Clear();
        await tools.DeleteGpo(Drives, None, "awiles", Stop);
        Assert.Equal([$"backup {Drives} Automatic backup before deletion", $"delete {Drives}"], gpmc.Calls);
        Assert.Equal([Firewall], Written(Lab).Select(e => e.Gpo));
        var kept = (await tools.GroupPolicy(Stop)).Backups.Single();
        Assert.Equal(("U-Drive maps", "awiles"), (kept.Name, kept.By));
        Assert.Contains("required by Windows", (await Assert.ThrowsAsync<ToolProblem>(() => tools.DeleteGpo(Ddp, None, "awiles", Stop))).Message);

        // Restoring keeps what it has now too, when it's still there.
        gpmc.Calls.Clear();
        await tools.RestoreGpo(kept.Id, None, "awiles", Stop);
        Assert.Equal([$"backup {Drives} Automatic backup before restoring the backup from {kept.At:yyyy-MM-dd HH:mm} UTC", $"restore {kept.Id}"], gpmc.Calls);

        await tools.SetGpoApply(Firewall, @"AMW\G-Workstations", false, None, Stop);
        Assert.Equal($@"apply {Firewall} AMW\G-Workstations False", gpmc.Calls[^1]);
    }

    [Fact]
    public async Task Without_Group_Policy_Management_its_parts_say_how_to_get_it_and_the_rest_still_works()
    {
        await Connect();
        tools.Gpmc = new NoGpmc();

        var refused = await Assert.ThrowsAsync<ToolProblem>(() => tools.NewGpo("C-New", null, null, None, "awiles", Stop));
        Assert.Equal(StatusCodes.Status501NotImplemented, refused.Status);
        Assert.Contains("Rsat.GroupPolicy.Management.Tools", refused.Message);
        await Assert.ThrowsAsync<ToolProblem>(() => tools.GpoDetail(Ddp, Stop));
        Assert.False((await tools.GroupPolicy(Stop)).Gpmc);
        await tools.Link(Spare, Lab, new GpoLinkChange(Add: true), None, Stop);
    }

    [Fact]
    public async Task A_GPOs_settings_and_delegation_come_from_its_report()
    {
        await Connect();
        gpmc.Xml = Report;
        var d = await tools.GpoDetail(Ddp, Stop);

        Assert.Contains(d.Computer, s => s is { Name: "Minimum password length", Value: "14 characters", Area: "Security Settings › Account Policies › Password Policy" });
        Assert.Contains(d.Computer, s => s.Name == "Configure Automatic Updates" && s.Value == "Enabled: Configure automatic updating 4 - Auto download and schedule the install"
            && s.Area == "Administrative Templates › Windows Components › Windows Update");
        Assert.Contains(d.Computer, s => s.Area == "Security Settings › Restricted Groups" && s.Value == @"Member of: BUILTIN\Administrators");
        Assert.Contains(d.Computer, s => s.Area == "Windows Firewall › Inbound rules" && s.Name == "Built-in rule (dnscache)" && s.Value == "Allow UDP 5355 from LocalSubnet (Private)");
        Assert.Contains(d.User, s => s.Name == @"Y: \\nas\share" && s.Value == "Update, label Share" && s.Area == "Preferences › Drive Maps");
        Assert.Equal(["apply", "full"], d.Who.Select(w => w.Access));
        Assert.Equal((@"AMW\Domain Admins", 13, 0), (d.Owner, d.SysvolComputer, d.SysvolUser));
        Assert.Equal(1, gpmc.Reports);
        await tools.GpoDetail(Ddp, Stop);
        Assert.Equal(1, gpmc.Reports); // kept until the GPO changes
        Assert.Empty(GpoReports.Parse(Ddp, "not xml").Computer);
    }

    [Fact]
    public void Backups_are_read_from_Group_Policy_Managements_own_files()
    {
        var dir = Path.Combine(data, "backups", "{0D4E1111-2222-3333-4444-555555555555}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "bkupInfo.xml"), "<?xml version=\"1.0\" encoding=\"utf-8\"?><BackupInst xmlns=\"http://www.microsoft.com/GroupPolicy/GPOOperations/Manifest\">"
            + $"<GPOGuid><![CDATA[{Ddp}]]></GPOGuid><GPODomain><![CDATA[amw.com]]></GPODomain><BackupTime><![CDATA[2026-11-15T21:04:11]]></BackupTime>"
            + "<ID><![CDATA[{0d4e1111-2222-3333-4444-555555555555}]]></ID><Comment><![CDATA[Before it was deleted]]></Comment><GPODisplayName><![CDATA[Default Domain Policy]]></GPODisplayName></BackupInst>");
        Directory.CreateDirectory(Path.Combine(data, "backups", "not a backup"));

        var b = GpmcFiles.Backups(Path.Combine(data, "backups")).Single();
        Assert.Equal(("{0D4E1111-2222-3333-4444-555555555555}", Ddp, "Default Domain Policy", "Before it was deleted"), (b.Id, b.Gpo, b.Name, b.Comment));
        Assert.Equal(new DateTime(2026, 11, 15, 21, 4, 11, DateTimeKind.Utc), b.At);
    }

    private const string Report = """
        <?xml version="1.0" encoding="utf-16"?>
        <GPO xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://www.microsoft.com/GroupPolicy/Settings">
          <Name>Default Domain Policy</Name>
          <Computer>
            <VersionDirectory>13</VersionDirectory><VersionSysvol>13</VersionSysvol><Enabled>true</Enabled>
            <ExtensionData>
              <Extension xmlns:q1="http://www.microsoft.com/GroupPolicy/Settings/Security" xsi:type="q1:SecuritySettings">
                <q1:Account><q1:Name>MinimumPasswordLength</q1:Name><q1:SettingNumber>14</q1:SettingNumber><q1:Type>Password</q1:Type></q1:Account>
                <q1:RestrictedGroups><q1:GroupName><SID xmlns="http://www.microsoft.com/GroupPolicy/Types">S-1-5-21-1-1198</SID><Name xmlns="http://www.microsoft.com/GroupPolicy/Types">AMW\L-SEC-LocalAdmins</Name></q1:GroupName>
                  <q1:Memberof><SID xmlns="http://www.microsoft.com/GroupPolicy/Types">S-1-5-32-544</SID><Name xmlns="http://www.microsoft.com/GroupPolicy/Types">BUILTIN\Administrators</Name></q1:Memberof></q1:RestrictedGroups>
              </Extension>
            </ExtensionData>
            <ExtensionData>
              <Extension xmlns:q2="http://www.microsoft.com/GroupPolicy/Settings/Registry" xsi:type="q2:RegistrySettings">
                <q2:Policy><q2:Name>Configure Automatic Updates</q2:Name><q2:State>Enabled</q2:State><q2:Explain>Long words.</q2:Explain><q2:Category>Windows Components/Windows Update</q2:Category>
                  <q2:DropDownList><q2:Name>Configure automatic updating:</q2:Name><q2:State>Enabled</q2:State><q2:Value><q2:Name>4 - Auto download and schedule the install</q2:Name></q2:Value></q2:DropDownList></q2:Policy>
              </Extension>
            </ExtensionData>
            <ExtensionData>
              <Extension xmlns:q3="http://www.microsoft.com/GroupPolicy/Settings/WindowsFirewall" xsi:type="q3:WindowsFirewallSettings">
                <q3:GlobalSettings><q3:PolicyVersion><q3:Value>544</q3:Value></q3:PolicyVersion></q3:GlobalSettings>
                <q3:InboundFirewallRules><q3:Action>Allow</q3:Action><q3:Name>@FirewallAPI.dll,-32801</q3:Name><q3:Dir>In</q3:Dir><q3:Svc>dnscache</q3:Svc><q3:Profile>Private</q3:Profile>
                  <q3:RA4>LocalSubnet</q3:RA4><q3:LPort>5355</q3:LPort><q3:Protocol>17</q3:Protocol><q3:Active>true</q3:Active></q3:InboundFirewallRules>
              </Extension>
            </ExtensionData>
          </Computer>
          <User>
            <VersionDirectory>0</VersionDirectory><VersionSysvol>0</VersionSysvol><Enabled>true</Enabled>
            <ExtensionData>
              <Extension xmlns:q1="http://www.microsoft.com/GroupPolicy/Settings/DriveMaps" xsi:type="q1:DriveMapSettings">
                <q1:DriveMapSettings clsid="{8FDDCC1A-0C3C-43cd-A6B4-71A6DF20DA8C}">
                  <q1:Drive clsid="{935D1B74-9CB8-4e3c-9914-7DD559B7A417}" name="Y:" status="Y:"><q1:GPOSettingOrder>1</q1:GPOSettingOrder>
                    <q1:Properties action="U" path="\\nas\share" label="Share" persistent="1" letter="Y" /></q1:Drive>
                </q1:DriveMapSettings>
              </Extension>
            </ExtensionData>
          </User>
          <SecurityDescriptor>
            <Owner xmlns="http://www.microsoft.com/GroupPolicy/Types/Security"><SID xmlns="http://www.microsoft.com/GroupPolicy/Types">S-1-5-21-1-512</SID><Name xmlns="http://www.microsoft.com/GroupPolicy/Types">AMW\Domain Admins</Name></Owner>
            <Permissions xmlns="http://www.microsoft.com/GroupPolicy/Types/Security">
              <TrusteePermissions><Trustee><SID xmlns="http://www.microsoft.com/GroupPolicy/Types">S-1-5-11</SID><Name xmlns="http://www.microsoft.com/GroupPolicy/Types">NT AUTHORITY\Authenticated Users</Name></Trustee>
                <Type xsi:type="PermissionType"><PermissionType>Allow</PermissionType></Type><Inherited>false</Inherited><Standard><GPOGroupedAccessEnum>Apply Group Policy</GPOGroupedAccessEnum></Standard></TrusteePermissions>
              <TrusteePermissions><Trustee><SID xmlns="http://www.microsoft.com/GroupPolicy/Types">S-1-5-21-1-512</SID><Name xmlns="http://www.microsoft.com/GroupPolicy/Types">AMW\Domain Admins</Name></Trustee>
                <Type xsi:type="PermissionType"><PermissionType>Allow</PermissionType></Type><Inherited>false</Inherited><Standard><GPOGroupedAccessEnum>Edit, delete, modify security</GPOGroupedAccessEnum></Standard></TrusteePermissions>
            </Permissions>
          </SecurityDescriptor>
        </GPO>
        """;

    [Fact]
    public async Task Deploying_the_client_makes_its_GPO_with_the_installer_task_and_exclusion_and_links_it_at_the_domain()
    {
        await Connect();
        var files = new FakeSysvol();
        tools.Sysvol = files;
        // The stand-in records changes without making them: the domain's links as last written.
        void Linked() => ad.Rows.Find(r => r.Dn == Domain)!.Values["gPLink"] = [(string) ad.Mods.Last(m => m.Dn == Domain && m.Mod.Attribute == "gPLink").Mod.Values[0]];

        var id = await tools.DeployClient("https://helpdesk.amw.com:8420/", "ABCD12", [1, 2, 3], None, "admin", Stop);
        Linked();
        var root = $@"\\amw.com\SYSVOL\amw.com\Policies\{id}";
        var dn = $"CN={id},{Policies}";
        var task = System.Text.Encoding.UTF8.GetString(files.Files[$@"{root}\Machine\Preferences\ScheduledTasks\ScheduledTasks.xml"]);
        var reg = System.Text.Encoding.UTF8.GetString(files.Files[$@"{root}\Machine\Preferences\Registry\Registry.xml"]);
        var deployed = await tools.ClientDeploy(Stop);
        var again = await tools.DeployClient("https://helpdesk.amw.com:8420", "ABCD12", [4, 5], None, "admin", Stop);
        await tools.StopClientDeploy(None, Stop);
        Linked();
        var stopped = await tools.ClientDeploy(Stop);

        Assert.Contains($@"{root}\GPT.INI", files.Files.Keys);
        Assert.Contains($@"if not exist ""{DirectoryTools.ClientExe}"" msiexec.exe /i ""{root}\Machine\Rabbid Lab\RabbidLab-Client.msi"" /qn", task);
        Assert.Contains("/qn /norestart", task);
        Assert.Contains("SERVER=https://helpdesk.amw.com:8420 FINGERPRINT=ABCD12", task);
        Assert.DoesNotContain("QUIET", task); // the tray icon always shows on a deployed computer
        Assert.Contains(@"runAs=""NT AUTHORITY\System""", task);
        Assert.Contains("ASROnlyExclusions", reg);
        Assert.Contains(DirectoryTools.ClientExe, reg);
        Assert.Contains("[{00000000-0000-0000-0000-000000000000}{BEE07A6A-EC9F-4659-B8C9-0B1937907C83}{CAB54552-DEEA-4691-817E-ED4A4D1AFC72}]",
            (string) ad.Mods.Last(m => m.Dn == dn && m.Mod.Attribute == "gPCMachineExtensionNames").Mod.Values[0]);
        Assert.Equal(["1", "2"], ad.Mods.Where(m => m.Dn == dn && m.Mod.Attribute == "versionNumber").Select(m => (string) m.Mod.Values[0]));
        Assert.Equal(id, again); // the same GPO, brought up to date
        // Each deployment's task has a name of its own, so one left on a computer never stops the next from being made.
        Assert.Matches(@"name=""Install the Rabbid Lab client \d{8}-\d{6}""", task);
        Assert.Equal([4, 5], files.Files[$@"{root}\Machine\Rabbid Lab\RabbidLab-Client.msi"]);
        Assert.True(deployed.Linked);
        Assert.Equal(DirectoryTools.ClientGpoName, deployed.Name);
        Assert.Equal("https://helpdesk.amw.com:8420", deployed.Server);
        Assert.False(stopped.Linked);
        Assert.Equal(id, stopped.Gpo); // still there, just not linked
    }

    [Fact]
    public void The_GPO_s_extension_list_keeps_what_it_had_and_stays_sorted()
    {
        var names = GpoText.AddExtensions("[{827D319E-6EAC-11D2-A4EA-00C04F79F83A}{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}]",
            [("{AADCED64-746C-4633-A97C-D61349046527}", "{CAB54552-DEEA-4691-817E-ED4A4D1AFC72}")]);

        Assert.Equal("[{00000000-0000-0000-0000-000000000000}{CAB54552-DEEA-4691-817E-ED4A4D1AFC72}][{827D319E-6EAC-11D2-A4EA-00C04F79F83A}{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}]"
            + "[{AADCED64-746C-4633-A97C-D61349046527}{CAB54552-DEEA-4691-817E-ED4A4D1AFC72}]", names);
    }
}

/// <summary>Stands in for Group Policy Management: keeps what it was asked to do, as whom, and backups in memory.</summary>
public sealed class FakeGpmc : IGpmc
{
    private readonly List<GpmcBackup> kept = [];

    public List<string> Calls { get; } = [];

    /// <summary>Who the last job ran as.</summary>
    public string? As { get; private set; }

    /// <summary>Refuse everything, as Active Directory does an account without rights.</summary>
    public bool Deny { get; set; }

    public string Xml { get; set; } = "<GPO/>";

    public int Reports { get; private set; }

    public bool Here => true;

    private Task<T> Do<T>(GpmcTarget t, string call, Func<T> result)
    {
        As = t.Username;
        if (Deny)
        {
            return Task.FromException<T>(new GpmcDenied("Access is denied."));
        }
        Calls.Add(call);
        return Task.FromResult(result());
    }

    public Task<string> Report(GpmcTarget t, string gpo, bool html, CancellationToken stop) => Do(t, $"report {gpo}", () =>
    {
        Reports++;
        return Xml;
    });

    /// <summary>A GPO it made, for the stand-in Active Directory to have too.</summary>
    public Action<string, string>? Made { get; set; }

    public Task<string> Create(GpmcTarget t, string name, CancellationToken stop) => Do(t, $"create {name}", () =>
    {
        Made?.Invoke("{CCCCCCCC-0000-0000-0000-000000000001}", name);
        return "{CCCCCCCC-0000-0000-0000-000000000001}";
    });

    public Task<string> Copy(GpmcTarget t, string gpo, string name, CancellationToken stop) => Do(t, $"copy {gpo} as {name}", () =>
    {
        Made?.Invoke("{CCCCCCCC-0000-0000-0000-000000000002}", name);
        return "{CCCCCCCC-0000-0000-0000-000000000002}";
    });

    public Task Delete(GpmcTarget t, string gpo, CancellationToken stop) => Do(t, $"delete {gpo}", () => true);

    public Task<GpmcBackup> Backup(GpmcTarget t, string gpo, string folder, string comment, CancellationToken stop) => Do(t, $"backup {gpo} {comment}", () =>
    {
        var b = new GpmcBackup($"{{BBBBBBBB-0000-0000-0000-{kept.Count + 1:D12}}}", gpo, gpo == "{AAAAAAAA-0000-0000-0000-000000000002}" ? "U-Drive maps" : gpo, new DateTime(2026, 11, 15, 21, 4, 0, DateTimeKind.Utc), comment);
        kept.Add(b);
        return b;
    });

    public Task<string> Restore(GpmcTarget t, string folder, string backup, CancellationToken stop) => Do(t, $"restore {backup}", () => kept.First(b => b.Id == backup).Gpo);

    public Task SetApply(GpmcTarget t, string gpo, string trustee, bool apply, CancellationToken stop) => Do(t, $"apply {gpo} {trustee} {apply}", () => true);

    public IReadOnlyList<GpmcBackup> Backups(string folder) => kept;
}
