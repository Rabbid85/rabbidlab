using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbidLab.Server;
using RabbidLab.Shared;

namespace RabbidLab.Tests;

/// <summary>
/// The real DirectoryTools against a stand-in for Active Directory (FakeLdap): what it may change and what it refuses,
/// what it leaves behind when something stops it, and how it finds who signed in. No test reaches a domain.
/// </summary>
public sealed class DirectoryToolsTests : IDisposable
{
    private const string Lab = "OU=Lab,DC=amw,DC=com";
    private const string Other = "OU=Other,DC=amw,DC=com";

    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-adtools-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;
    private readonly FakeLdap ad = new();
    private readonly DirectoryTools tools;

    public DirectoryToolsTests()
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

    /// <summary>Set up with an account it was given (svc-existing), which Active Directory would let change anything, limited to the Lab OU.</summary>
    private Task<string?> Connect(string scope = Lab) =>
        tools.Connect(new SetupForm("amw.com", "svc-existing", "right", Existing: true, null, null, scope), "admin", CancellationToken.None);

    private static Task<ToolProblem> Refused(Func<Task> change) => Assert.ThrowsAsync<ToolProblem>(change);

    [Fact]
    public void Security_template_keys_change_where_they_are_or_join_their_section_and_the_policy_version_goes_up()
    {
        var inf = "[Unicode]\r\nUnicode=yes\r\n[System Access]\r\nMinimumPasswordLength = 7\r\nPasswordComplexity = 1\r\n\r\n[Version]\r\nsignature=\"$CHICAGO$\"\r\n";
        var after = GpoText.SetInf(inf, "System Access", new Dictionary<string, string> { ["MinimumPasswordLength"] = "14", ["LockoutBadCount"] = "5" });
        Assert.Contains("MinimumPasswordLength = 14\r\n", after);
        Assert.Contains("PasswordComplexity = 1", after);
        Assert.DoesNotContain("= 7", after);
        Assert.True(after.IndexOf("LockoutBadCount = 5", StringComparison.Ordinal) < after.IndexOf("[Version]", StringComparison.Ordinal));
        var none = GpoText.SetInf("[Unicode]\r\nUnicode=yes\r\n[Version]\r\nRevision=1\r\n", "System Access", new Dictionary<string, string> { ["PasswordHistorySize"] = "24" });
        Assert.Contains("[System Access]\r\nPasswordHistorySize = 24\r\n[Version]", none);
        var (ini, version) = GpoText.BumpGpt("[General]\r\nVersion=7\r\ndisplayName=New Group Policy Object\r\n", 9);
        Assert.Equal(10, version);
        Assert.Contains("Version=10\r\n", ini);
        var (text, encoding) = GpoText.Decode(GpoText.Encode("[Unicode]\r\n", Encoding.Unicode));
        Assert.Equal("[Unicode]\r\n", text);
        Assert.Equal(Encoding.Unicode, encoding);
    }

    [Fact]
    public async Task Domain_settings_change_as_the_admin_typed_and_the_password_policy_goes_through_the_default_domain_policy()
    {
        await Connect();
        const string gpo = "CN={31B2F340-016D-11D2-945F-00C04FB984F9},CN=Policies,CN=System,DC=amw,DC=com";
        const string path = @"\\amw.com\SysVol\amw.com\Policies\{31B2F340-016D-11D2-945F-00C04FB984F9}";
        const string template = path + @"\MACHINE\Microsoft\Windows NT\SecEdit\GptTmpl.inf";
        ad.Add(gpo, ("gPCFileSysPath", path), ("versionNumber", "3"));
        var files = new FakeSysvol();
        files.Files[template] = GpoText.Encode("[Unicode]\r\nUnicode=yes\r\n[System Access]\r\nMinimumPasswordLength = 7\r\n[Version]\r\nRevision=1\r\n", Encoding.Unicode);
        files.Files[path + @"\GPT.INI"] = Encoding.Latin1.GetBytes("[General]\r\nVersion=3\r\n");
        tools.Sysvol = files;
        var none = CancellationToken.None;

        await Refused(() => tools.SetDomainSetting("length", "14", "admin", "wrong", none));
        await Refused(() => tools.SetDomainSetting("length", "99", "admin", "right", none));
        await Refused(() => tools.SetDomainSetting("length", "14", "", "", none));
        await Refused(() => tools.SetDomainSetting("dfl", "7", "admin", "right", none));
        Assert.DoesNotContain(ad.Log, l => l.Contains("minPwdLength", StringComparison.Ordinal));

        var settings = await tools.SetDomainSetting("length", "14", "admin", "right", none);
        Assert.Contains("MinimumPasswordLength = 14", GpoText.Decode(files.Files[template]).Text);
        Assert.StartsWith("\uFEFF", Encoding.Unicode.GetString(files.Files[template])); // still UTF-16 with its mark, as Group Policy writes it
        Assert.Contains("Version=4", GpoText.Decode(files.Files[path + @"\GPT.INI"]).Text);
        Assert.Contains($"modify {gpo} versionNumber", ad.Log);
        Assert.Contains("modify DC=amw,DC=com minPwdLength", ad.Log);
        Assert.Contains(settings, s => s.Key == "length" && s.Edit == "number" && s.Max == 14);
        Assert.Contains(settings, s => s.Key == "dfl" && s.Edit is null && s.How is not null);

        await tools.SetDomainSetting("quota", "0", "admin", "right", none);
        Assert.Contains("modify DC=amw,DC=com ms-DS-MachineAccountQuota", ad.Log);
    }

    [Fact]
    public async Task Changes_stay_inside_the_scope_the_service_account_was_set_up_for()
    {
        await Connect();
        var none = CancellationToken.None;

        await tools.Password("jreyes", "New pass 1", false, none);
        await tools.Enable("jreyes", false, none);
        await tools.EnableComputer("PC1", false, none);
        var problems = new[]
        {
            await Refused(() => tools.Password("outsider", "New pass 1", false, none)),
            await Refused(() => tools.Unlock("outsider", none)),
            await Refused(() => tools.Enable("outsider", false, none)),
            await Refused(() => tools.ChangeGroups("outsider", [$"CN=Lab Staff,{Lab}"], [], none)),
            await Refused(() => tools.Delete("outsider", none)),
            await Refused(() => tools.EnableComputer("PC2", false, none)),
            await Refused(() => tools.DeleteComputer("PC2", none)),
            await Refused(() => tools.ChangeMembers($"CN=Elsewhere,{Other}", [$"CN=Jordan Reyes,{Lab}"], [], none)),
            await Refused(() => tools.DeleteGroup($"CN=Elsewhere,{Other}", none)),
        };
        var outsideGroup = await tools.ChangeGroups("jreyes", [$"CN=Elsewhere,{Other}", $"CN=Lab Staff,{Lab}"], [], none);
        var notListed = await Refused(() => tools.User("outsider", none));

        Assert.All(problems, p => Assert.Contains("outside the service account's scope (Lab)", p.Message));
        Assert.Contains("Elsewhere is outside the service account's scope", outsideGroup);
        Assert.Equal(StatusCodes.Status404NotFound, notListed.Status);
        Assert.DoesNotContain(ad.Log, l => l.Contains("Out Sider") || l.Contains("PC2") || l.Contains("Elsewhere"));
        Assert.Contains($"modify CN=Jordan Reyes,{Lab} unicodePwd", ad.Log);
        Assert.Contains($"modify CN=PC1,{Lab} userAccountControl", ad.Log);
        Assert.Contains($"modify CN=Lab Staff,{Lab} member", ad.Log); // the group in scope still got its member
    }

    [Fact]
    public async Task Protected_and_built_in_ones_go_to_AD_and_its_own_account_keeps_its_sign_in()
    {
        await Connect();
        var none = CancellationToken.None;

        // Protected and built-in accounts and groups: Active Directory decides, so the change is sent.
        await tools.Enable("boss", false, none);
        await tools.Password("krbtgt", "New pass 1", false, none);
        Assert.Null(await tools.ChangeMembers($"CN=Lab Operators,{Lab}", [$"CN=Jordan Reyes,{Lab}"], [], none));
        Assert.Null(await tools.ChangeGroups("jreyes", [$"CN=Lab Operators,{Lab}"], [], none));
        Assert.Contains($"modify CN=Boss,{Lab} userAccountControl", ad.Log);
        Assert.Contains($"modify CN=krbtgt,{Lab} unicodePwd", ad.Log);

        // Its own account: its groups and unlocking, yes; its password, sign-in settings and deleting it, no.
        Assert.Null(await tools.ChangeGroups("svc-existing", [$"CN=Lab Staff,{Lab}"], [], none));
        await tools.Unlock("svc-existing", none);
        var own = new[]
        {
            await Refused(() => tools.Password("svc-existing", "New pass 1", false, none)),
            await Refused(() => tools.Enable("svc-existing", false, none)),
            await Refused(() => tools.Delete("svc-existing", none)),
            await Refused(() => tools.SetFlag("svc-existing", "smartcard", true, none)),
            await Refused(() => tools.SetExpiry("svc-existing", new DateOnly(2030, 1, 1), none)),
        };
        Assert.All(own, p => Assert.Contains("own account", p.Message));

        // Domain controllers can't be deleted or moved; built-in and protected groups can't be deleted.
        Assert.Contains("domain controller", (await Refused(() => tools.DeleteComputer("DC9", none))).Message);
        Assert.Contains("domain controller", (await Refused(() => tools.MoveObject($"CN=DC9,{Lab}", $"OU=People,{Lab}", none))).Message);
        Assert.Contains("built-in or protected", (await Refused(() => tools.DeleteGroup($"CN=Lab Operators,{Lab}", none))).Message);
    }

    [Fact]
    public async Task OU_pickers_list_what_ADUC_shows_and_nothing_new_goes_in_Builtin()
    {
        const string domain = "DC=amw,DC=com";
        ad.Add($"CN=Builtin,{domain}", ("objectClass", "builtinDomain"), ("cn", "Builtin"));
        ad.Add($"CN=Computers,{domain}", ("objectClass", "container"), ("cn", "Computers"));
        ad.Add($"OU=Domain Controllers,{domain}", ("objectClass", "organizationalUnit"), ("ou", "Domain Controllers"));
        ad.Add($"CN=System,{domain}", ("objectClass", "container"), ("cn", "System"), ("showInAdvancedViewOnly", "TRUE"));
        ad.Add($"CN=PSPs,CN=System,{domain}", ("objectClass", "container"), ("cn", "PSPs"));
        await Connect(domain);

        var places = await tools.Places(CancellationToken.None);
        var dns = places.Select(p => p.Dn).ToList();
        Assert.Contains($"CN=Computers,{domain}", dns);
        Assert.Contains(Lab, dns);
        Assert.DoesNotContain($"CN=System,{domain}", dns);
        Assert.DoesNotContain($"CN=PSPs,CN=System,{domain}", dns);
        Assert.NotNull(places.Single(p => p.Dn == $"CN=Builtin,{domain}").Fixed);
        Assert.NotNull(places.Single(p => p.Dn == $"OU=Domain Controllers,{domain}").Fixed);
        Assert.Null(places.Single(p => p.Dn == $"CN=Computers,{domain}").Fixed);

        var refused = await Refused(() => tools.CreateGroup(new NewAdGroup("Lab Admins", "global", true, $"CN=Builtin,{domain}", null), CancellationToken.None));
        Assert.Contains("built-in groups", refused.Message);
    }

    [Fact]
    public async Task The_attribute_browser_edits_as_ADSI_Edit_does_inside_the_scope_only()
    {
        const string schema = "CN=Schema,CN=Configuration,DC=amw,DC=com";
        ad.Rows.Single(r => r.Dn == "").Values["schemaNamingContext"] = [schema];
        ad.Add($"CN=Description,{schema}", ("objectClass", "attributeSchema"), ("lDAPDisplayName", "description"), ("attributeSyntax", "2.5.5.12"), ("isSingleValued", "FALSE"));
        ad.Add($"CN=Employee-Number,{schema}", ("objectClass", "attributeSchema"), ("lDAPDisplayName", "employeeNumber"), ("attributeSyntax", "2.5.5.12"), ("isSingleValued", "TRUE"));
        ad.Add($"CN=Object-Sid,{schema}", ("objectClass", "attributeSchema"), ("lDAPDisplayName", "objectSid"), ("attributeSyntax", "2.5.5.17"), ("isSingleValued", "TRUE"));
        var casey = $"CN=Casey Holt,{Lab}";
        ad.Add(casey, ("objectClass", "user"), ("description", "Old"), ("allowedAttributes", "description"), ("allowedAttributes", "employeeNumber"), ("allowedAttributes", "objectSid"),
            ("allowedAttributesEffective", "description"), ("allowedAttributesEffective", "employeeNumber"));
        ad.Add($"CN=Far Away,{Other}", ("objectClass", "user"), ("allowedAttributesEffective", "description"));
        await Connect();
        var none = CancellationToken.None;

        // Every attribute it may have is listed, set or not, with its type; only typed ones the account may write are editable.
        var o = await tools.Attributes(casey, none);
        var description = o.Attributes.Single(a => a.Name == "description");
        Assert.Equal("String", description.Type);
        Assert.True(description.Set);
        Assert.True(description.Editable);
        Assert.Equal(["Old"], description.Raw);
        Assert.True(o.Attributes.Single(a => a.Name == "employeeNumber") is { Set: false, Editable: true, Single: true });
        Assert.False(o.Attributes.Single(a => a.Name == "objectSid").Editable);

        await tools.SetAttribute(casey, "employeeNumber", ["1042"], none);
        Assert.Contains($"modify {casey} employeeNumber", ad.Log);
        Assert.Contains("single value", (await Refused(() => tools.SetAttribute(casey, "employeeNumber", ["1", "2"], none))).Message);
        Assert.Contains("binary", (await Refused(() => tools.SetAttribute(casey, "objectSid", ["S-1-5-21-1"], none))).Message);
        Assert.Contains("outside", (await Refused(() => tools.SetAttribute($"CN=Far Away,{Other}", "description", ["x"], none))).Message);

        // Clearing what's already empty asks Active Directory for nothing.
        ad.Log.Clear();
        await tools.SetAttribute(casey, "employeeNumber", [], none);
        Assert.DoesNotContain($"modify {casey} employeeNumber", ad.Log);
    }

    [Fact]
    public async Task The_users_grid_menu_changes_options_expiry_properties_name_and_place_inside_the_scope_only()
    {
        await Connect();
        var none = CancellationToken.None;
        var jordan = $"CN=Jordan Reyes,{Lab}";

        await tools.SetFlag("jreyes", "smartcard", true, none);
        await tools.SetFlag("jreyes", "mustchange", true, none);
        await tools.SetExpiry("jreyes", new DateOnly(2026, 12, 31), none);
        var changed = await tools.Edit("jreyes", new UserEdit(null, null, null, "Field tech", null, null, null, "Tech", null, null, null), none);
        Assert.Equal(["description", "job title"], changed);
        Assert.Contains($"modify {jordan} userAccountControl", ad.Log);
        Assert.Contains($"modify {jordan} pwdLastSet", ad.Log);
        Assert.Contains($"modify {jordan} accountExpires", ad.Log);
        Assert.Contains($"modify {jordan} description", ad.Log);

        // A password that never expires can't also be made to change.
        ad.Rows.Single(r => r.Dn == jordan).Values["userAccountControl"] = ["66048"];
        Assert.Contains("never expires", (await Refused(() => tools.SetFlag("jreyes", "mustchange", true, none))).Message);

        // Moved and renamed inside the scope; never out of it, and never a protected account.
        Assert.Equal("Lab / People", await tools.Move("jreyes", $"OU=People,{Lab}", none));
        Assert.Contains($"rename {jordan} -> CN=Jordan Reyes,OU=People,{Lab}", ad.Log);
        await tools.Rename("jreyes", "Jordan Reyes-Hall", none);
        Assert.Contains($"rename CN=Jordan Reyes,OU=People,{Lab} -> CN=Jordan Reyes-Hall,OU=People,{Lab}", ad.Log);
        Assert.Contains("outside", (await Refused(() => tools.Move("jreyes", Other, none))).Message);
        Assert.Contains("outside", (await Refused(() => tools.SetFlag("outsider", "sensitive", true, none))).Message);
        Assert.Contains("own account", (await Refused(() => tools.SetExpiry("svc-existing", null, none))).Message);
    }

    [Fact]
    public async Task Groups_and_computers_move_join_groups_and_take_edits_inside_the_scope_only()
    {
        await Connect();
        var none = CancellationToken.None;
        var staff = $"CN=Lab Staff,{Lab}";
        var pc1 = $"CN=PC1,{Lab}";

        Assert.Equal("Lab / People", await tools.MoveObject(pc1, $"OU=People,{Lab}", none));
        Assert.Contains($"rename {pc1} -> CN=PC1,OU=People,{Lab}", ad.Log);
        Assert.Null(await tools.ChangeMemberOf($"CN=PC1,OU=People,{Lab}", [staff], [], none));
        Assert.Contains($"modify {staff} member", ad.Log);
        Assert.Equal(["description", "notes"], await tools.EditObject(new ObjectEdit(staff, "Lab people", null, "Ask Anthony", null), none));
        Assert.Contains($"modify {staff} description", ad.Log);
        // A group isn't put in itself; email and location only go on what has them.
        ad.Log.Clear();
        Assert.Null(await tools.ChangeMemberOf(staff, [staff], [], none));
        Assert.DoesNotContain(ad.Log, l => l.StartsWith("modify", StringComparison.Ordinal));
        Assert.Equal(["location"], await tools.EditObject(new ObjectEdit($"CN=PC1,OU=People,{Lab}", null, "x@amw.com", null, "Rack 2"), none));

        Assert.Equal("Lab / People", await tools.MoveObject($"CN=Lab Operators,{Lab}", $"OU=People,{Lab}", none));
        Assert.Null(await tools.ChangeMemberOf($"CN=DC9,{Lab}", [staff], [], none));
        Assert.Contains("outside", (await Refused(() => tools.EditObject(new ObjectEdit($"CN=Elsewhere,{Other}", "x", null, null, null), none))).Message);
        Assert.Contains("outside", (await Refused(() => tools.MoveObject($"CN=PC1,OU=People,{Lab}", Other, none))).Message);
    }

    [Fact]
    public async Task Users_groups_and_computers_keep_their_own_default_OU_and_new_computers_go_in_theirs()
    {
        await Connect();
        var none = CancellationToken.None;

        await tools.SetNewPlace("groups", $"OU=People,{Lab}", none);
        var s = tools.State()!;
        Assert.Equal(Lab, s.NewUsers);
        Assert.Equal($"OU=People,{Lab}", s.NewGroups);
        Assert.Equal(Lab, s.NewComputers);

        var pc = await tools.CreateComputer(new NewAdComputer("ws-042", null, "Front desk"), none);
        Assert.Equal("WS-042", pc.Name);
        Assert.Contains($"add CN=WS-042,{Lab}", ad.Log);
        Assert.Contains("already a computer called PC1", (await Refused(() => tools.CreateComputer(new NewAdComputer("PC1", null, null), none))).Message);
        Assert.Contains("1 to 15", (await Refused(() => tools.CreateComputer(new NewAdComputer("a-name-far-too-long", null, null), none))).Message);
        Assert.Contains("can only make computers", (await Refused(() => tools.CreateComputer(new NewAdComputer("WS-043", Other, null), none))).Message);
    }

    [Fact]
    public async Task Lists_say_when_Active_Directory_had_more_than_it_sent()
    {
        await Connect("DC=amw,DC=com");
        var none = CancellationToken.None;
        var few = await tools.Groups(null, none);
        for (var i = 0; i < 1000; i++)
        {
            ad.Add($"CN=Bulk {i:0000},OU=Bulk,DC=amw,DC=com", ("objectClass", "group"), ("objectCategory", "group"), ("cn", $"Bulk {i:0000}"), ("member", $"CN=Jordan Reyes,{Lab}"));
            ad.Add($"CN=Temp {i:0000},OU=Bulk,DC=amw,DC=com", ("objectClass", "user"), ("objectCategory", "person"), ("sAMAccountName", $"temp{i:0000}"),
                ("memberOf", $"CN=Lab Staff,{Lab}"), ("whenChanged", DateTime.UtcNow.ToString("yyyyMMddHHmmss'.0Z'")));
            ad.Deleted.Add(FakeLdap.Entry($@"CN=Gone {i:0000}\0ADEL:{i},CN=Deleted Objects,DC=amw,DC=com", ("objectClass", "user"), ("isDeleted", "TRUE"), ("msDS-LastKnownRDN", $"Gone {i:0000}")));
        }

        var groups = await tools.Groups(null, none);
        var users = await tools.Users(none);
        var members = await tools.Members($"CN=Lab Staff,{Lab}", none);
        var changes = await tools.Changes(30, none);
        var bin = await tools.RecycleBin(none);

        Assert.False(few.More);
        Assert.True(groups.More);
        Assert.Equal(1000, groups.Groups.Count);
        Assert.True(users.More);
        Assert.True(members.More);
        Assert.True(changes.More);
        Assert.True(bin.More);
    }

    [Fact]
    public async Task A_group_s_members_include_those_who_have_it_as_primary_group()
    {
        await Connect("DC=amw,DC=com");
        var none = CancellationToken.None;

        var (members, _) = await tools.Members($"CN=Lab Staff,{Lab}", none);
        var staff = (await tools.Groups(null, none)).Groups.Single(g => g.Name == "Lab Staff");
        var missing = await Refused(() => tools.Members($"CN=Nobody,{Lab}", none));

        Assert.Contains(members, m => m.Username == "pat"); // primary group only: not in the group's member attribute
        Assert.Equal(1, staff.Members);
        Assert.Equal(StatusCodes.Status404NotFound, missing.Status);
    }

    [Fact]
    public async Task One_sign_in_serves_many_requests_until_the_account_changes()
    {
        await Connect();
        var none = CancellationToken.None;
        var before = ad.Opens;

        await tools.Users(none);
        await tools.Computers(none);
        await tools.User("jreyes", none);
        var reused = ad.Opens - before;
        tools.Disconnect();
        var gone = await Refused(() => tools.Users(none));
        await Connect();
        await tools.Users(none);

        Assert.Equal(1, reused);
        Assert.Equal(StatusCodes.Status409Conflict, gone.Status);
        Assert.Equal(before + 3, ad.Opens); // the setup's own sign-in, then one for the new account
    }

    [Fact]
    public async Task A_connection_that_failed_is_not_used_again()
    {
        await Connect();
        var none = CancellationToken.None;
        await tools.Users(none);
        var before = ad.Opens;

        ad.Fail = op => op.StartsWith("find", StringComparison.Ordinal) ? new DirectoryProblem("Active Directory couldn't be searched: the server is down.") : null;
        await Assert.ThrowsAsync<DirectoryProblem>(() => tools.Users(none));
        ad.Fail = null;
        await tools.Users(none);

        Assert.Equal(before + 1, ad.Opens);
    }

    [Fact]
    public async Task An_account_that_was_only_half_made_is_taken_away_again_whatever_stopped_it()
    {
        await Connect();
        ad.Fail = op => op.Contains("unicodePwd") ? new OperationCanceledException() : null;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.Create(new NewAdUser("New", "Person", null, "nperson", null, null, false, true, false, null, null, null, null), "Pass word 1", CancellationToken.None));

        Assert.Contains($"add CN=New Person,{Lab}", ad.Log);
        Assert.Contains($"delete CN=New Person,{Lab}", ad.Log);
        Assert.DoesNotContain(ad.Rows, r => r.Dn == $"CN=New Person,{Lab}");
    }

    [Fact]
    public async Task Restoring_asks_for_the_object_itself_and_only_puts_it_back_in_scope()
    {
        await Connect();
        var none = CancellationToken.None;
        var mine = $@"CN=Old User\0ADEL:1,CN=Deleted Objects,DC=amw,DC=com";
        var theirs = $@"CN=Other User\0ADEL:2,CN=Deleted Objects,DC=amw,DC=com";
        for (var i = 0; i < 1500; i++)
        {
            ad.Deleted.Add(FakeLdap.Entry($@"CN=Noise {i}\0ADEL:n{i},CN=Deleted Objects,DC=amw,DC=com", ("objectClass", "computer"), ("isDeleted", "TRUE")));
        }
        ad.Deleted.Add(FakeLdap.Entry(mine, ("objectClass", "user"), ("isDeleted", "TRUE"), ("msDS-LastKnownRDN", "Old User"), ("lastKnownParent", Lab)));
        ad.Deleted.Add(FakeLdap.Entry(theirs, ("objectClass", "user"), ("isDeleted", "TRUE"), ("msDS-LastKnownRDN", "Other User"), ("lastKnownParent", Other)));

        var back = await tools.Restore(mine, none);
        var outside = await Refused(() => tools.Restore(theirs, none));
        var notThere = await Refused(() => tools.Restore("CN=Deleted Objects,DC=amw,DC=com", none));
        var elsewhere = await Refused(() => tools.Restore($"CN=Jordan Reyes,{Lab}", none));

        Assert.Equal($"CN=Old User,{Lab}", back);
        Assert.Contains($"restore {mine} -> CN=Old User,{Lab}", ad.Log);
        Assert.Contains("outside the service account's scope", outside.Message);
        Assert.Equal(StatusCodes.Status404NotFound, notThere.Status);
        Assert.Equal(StatusCodes.Status404NotFound, elsewhere.Status);
    }

    [Fact]
    public async Task Who_signed_in_is_who_Active_Directory_says_not_who_was_typed()
    {
        var none = CancellationToken.None;
        string[] attributes = ["sAMAccountName"];

        // AMW\awiles for amw.com: the NetBIOS name, which is what people type.
        ad.Who = @"u:AMW\awiles";
        var typedNetBios = await Ldap.Bound(ad, @"AMW\awiles", attributes, none);
        // A UPN whose first part is someone else's username: it's the account that signed in, not the lookalike.
        ad.Who = @"u:AMW\john.smith";
        var typedUpn = await Ldap.Bound(ad, "jsmith@amw.com", attributes, none);
        // An account from a trusted domain, with the same name as one here.
        ad.Who = @"u:OTHER\awiles";
        var otherDomain = await Assert.ThrowsAsync<DirectoryProblem>(() => Ldap.Bound(ad, @"OTHER\awiles", attributes, none));
        // No WhoAmI: the UPN is looked up before the username in it.
        ad.Who = null;
        var noWhoAmI = await Ldap.Bound(ad, "jsmith@amw.com", attributes, none);
        var plain = await Ldap.Bound(ad, "awiles", attributes, none);

        Assert.Equal("awiles", typedNetBios?.Text("sAMAccountName"));
        Assert.Equal("john.smith", typedUpn?.Text("sAMAccountName"));
        Assert.Contains(@"OTHER\awiles is an account in another domain", otherDomain.Message);
        Assert.Equal("john.smith", noWhoAmI?.Text("sAMAccountName"));
        Assert.Equal("awiles", plain?.Text("sAMAccountName"));
    }

    [Fact]
    public async Task Setup_keeps_the_account_that_signed_in_and_reads_files_from_before()
    {
        ad.Who = @"u:AMW\svc-existing";
        await tools.Connect(new SetupForm("amw.com", "svc-existing@amw.com", "right", Existing: true, null, null, Lab), "admin", CancellationToken.None);
        var state = tools.State()!;
        var file = Path.Combine(data, "directory-tools.json");
        var saved = File.ReadAllText(file);

        // A file from before these went: unknown properties are skipped, not an error.
        File.WriteAllText(file, saved.Replace("{", "{ \"server\": \"dc01.amw.com\", \"accountDn\": \"CN=svc-existing,OU=Lab,DC=amw,DC=com\",", StringComparison.Ordinal));
        using var again = new DirectoryTools(app.Services.GetRequiredService<DataFolder>(), app.Services.GetRequiredService<SettingsStore>(), app.Services.GetRequiredService<Accounts>(), app.Finder, ad.Open);

        Assert.Equal("svc-existing", state.Account);
        Assert.Equal("Lab", state.ScopeName);
        Assert.Equal("admin", state.By);
        Assert.Equal("svc-existing", again.State()!.Account);
        Assert.Equal(2, (await again.Users(CancellationToken.None)).Users.Count(u => u.Username is "jreyes" or "boss"));
    }

    [Fact]
    public async Task New_users_can_only_be_pointed_at_a_place_in_scope_and_a_disconnect_in_between_is_not_undone()
    {
        await Connect();
        var none = CancellationToken.None;

        await tools.SetNewUsers($"OU=People,{Lab}", none);
        var outside = await Refused(() => tools.SetNewUsers(Other, none));
        ad.Fail = op =>
        {
            if (op.StartsWith($"find OU=People,{Lab}", StringComparison.Ordinal))
            {
                tools.Disconnect(); // someone disconnects while the OU is being checked
            }
            return null;
        };
        var raced = await Refused(() => tools.SetNewUsers($"OU=People,{Lab}", none));

        Assert.Contains("can only make people in Lab", outside.Message);
        Assert.Equal(StatusCodes.Status409Conflict, raced.Status);
        Assert.Null(tools.State());
        Assert.False(File.Exists(Path.Combine(data, "directory-tools.json")));
    }

    [Fact]
    public void A_parent_is_matched_part_by_part()
    {
        Assert.True(Ldap.Under($"CN=x,{Lab}", Lab));
        Assert.True(Ldap.Under("cn=x,ou=lab,dc=amw,dc=com", Lab));
        Assert.False(Ldap.Under(@"CN=x\,OU=Lab,DC=amw,DC=com", Lab)); // one part named "x,OU=Lab", straight under the domain
        Assert.False(Ldap.Under("<GUID=0f3c5a3e-1111-2222-3333-444455556666>", "DC=amw,DC=com"));
        Assert.Equal(("AMW", "awiles"), Ldap.Who(@"u:AMW\awiles"));
        Assert.Null(Ldap.Who("u:awiles@amw.com"));
        Assert.Null(Ldap.Who(""));
    }

    [Fact]
    public async Task The_host_for_a_domain_is_the_one_chosen_else_the_quickest_that_answers_else_the_domain()
    {
        IDomainFinder finder = app.Finder;

        Assert.Equal("dc02.amw.com", await finder.Host("amw.com", " DC02.amw.com ", CancellationToken.None));
        Assert.Equal("dc01.amw.com", await finder.Host("amw.com", null, CancellationToken.None));
        Assert.Equal("nowhere.example", await finder.Host("nowhere.example", "", CancellationToken.None));
    }

    [Fact]
    public void The_audit_names_who_only_when_the_log_names_the_object_as_a_whole_word()
    {
        Assert.True(DirectoryExtrasApi.Mentions("Made the AD group IT", "IT"));
        Assert.True(DirectoryExtrasApi.Mentions("Changed who's in Lab Staff: added jreyes", "lab staff"));
        Assert.False(DirectoryExtrasApi.Mentions("Deleted the AD account smith", "IT"));
        Assert.False(DirectoryExtrasApi.Mentions("Made the AD group ITs", "IT"));
        Assert.False(DirectoryExtrasApi.Mentions("anything", ""));
    }

    [Fact]
    public void The_client_and_the_server_use_the_same_names_for_account_orders_and_answers()
    {
        var web = app.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        var order = new AccountOrder
        {
            Id = "7", Action = AccountOrder.Create, Name = "kiosk", FullName = "Front desk", Description = "The kiosk", NewPassword = "Pass word 1", MustChange = true, NeverExpires = true,
            Add = ["Users"], Remove = ["Guests"],
        };
        var answer = new AccountAnswer
        {
            Id = "7", Ok = true, Error = "e", Warning = "w",
            Accounts = [new() { Name = "kiosk", FullName = "Front desk", Description = "d", Enabled = true, Locked = true, NeverExpires = true, BuiltIn = true, Groups = ["Users"], LastSignIn = "2026-01-02T03:04:05Z", PasswordSet = "2026-01-01T00:00:00Z" }],
            Groups = [new() { Name = "Administrators", Sid = "S-1-5-32-544", Description = "Admins" }],
        };

        // The server writes an order with its own JSON options and the client (DataContractJsonSerializer, by [DataMember] name) reads it; answers go the other way.
        var orderRead = AsClient<AccountOrder>(JsonSerializer.Serialize(order, web));
        var answerRead = JsonSerializer.Deserialize<AccountAnswer>(FromClient(answer), web)!;

        Assert.Equal(JsonSerializer.Serialize(order, web), JsonSerializer.Serialize(orderRead, web));
        Assert.Equal(JsonSerializer.Serialize(answer, web), JsonSerializer.Serialize(answerRead, web));
        foreach (var type in new[] { typeof(AccountOrder), typeof(AccountAnswer), typeof(WindowsAccount), typeof(WindowsGroup) })
        {
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // A property without [DataMember] never reaches the client, and one named otherwise is lost on the way.
                Assert.Equal(web.PropertyNamingPolicy!.ConvertName(p.Name), p.GetCustomAttribute<DataMemberAttribute>()?.Name);
            }
        }
    }

    private static T AsClient<T>(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return (T) new DataContractJsonSerializer(typeof(T)).ReadObject(stream)!;
    }

    private static string FromClient<T>(T value)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>
/// Active Directory as these tests see it: amw.com (NetBIOS name AMW) with a Lab OU and an Other OU, kept in memory.
/// It answers the filters DirectoryTools sends, and notes every change it's asked for instead of making it anywhere.
/// </summary>
/// <summary>SYSVOL as a dictionary of files; it checks nothing about who signs in.</summary>
public sealed class FakeSysvol : ISysvol
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<byte[]> Read(string path, string username, string password, CancellationToken stop) =>
        Files.TryGetValue(path, out var data) ? Task.FromResult(data) : Task.FromException<byte[]>(new ToolProblem($"No file at {path}"));

    public Task Write(string path, byte[] data, string username, string password, CancellationToken stop)
    {
        Files[path] = data;
        return Task.CompletedTask;
    }
}

public sealed class FakeLdap : ILdap
{
    private const string Domain = "DC=amw,DC=com";
    private const string Lab = "OU=Lab," + Domain;
    private const string Other = "OU=Other," + Domain;

    public sealed record Item(string Dn, Dictionary<string, object[]> Values);

    public List<Item> Rows { get; } = [];

    /// <summary>What's in Deleted Objects, which only FindDeleted sees.</summary>
    public List<Item> Deleted { get; } = [];

    /// <summary>Every change asked for: "modify dn attribute", "add dn", "delete dn", "restore dn -> newDn".</summary>
    public List<string> Log { get; } = [];

    /// <summary>What WhoAmI answers; null for a server that doesn't.</summary>
    public string? Who { get; set; } = @"u:AMW\svc-existing";

    /// <summary>Given "find root filter", "modify dn attribute" and so on: what to throw instead of doing it, or null.</summary>
    public Func<string, Exception?>? Fail { get; set; }

    /// <summary>How many times someone signed in.</summary>
    public int Opens { get; private set; }

    public string BaseDn => Domain;

    public bool Encrypted => true;

    public FakeLdap()
    {
        byte[] Sid(int rid) => [1, 5, 0, 0, 0, 0, 0, 5, 21, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, .. BitConverter.GetBytes(rid)];
        Add("", ("configurationNamingContext", "CN=Configuration," + Domain), ("defaultNamingContext", Domain));
        Add("CN=AMW,CN=Partitions,CN=Configuration," + Domain, ("objectClass", "crossRef"), ("nCName", Domain), ("nETBIOSName", "AMW"));
        Add(Domain, ("objectClass", "domainDNS"));
        Add(Lab, ("objectClass", "organizationalUnit"), ("ou", "Lab"), ("allowedChildClassesEffective", "user"));
        Add("OU=People," + Lab, ("objectClass", "organizationalUnit"), ("ou", "People"));
        Add(Other, ("objectClass", "organizationalUnit"), ("ou", "Other"));
        void Person(string sam, string name, string ou, params (string, object)[] more) =>
            Add($"CN={name},{ou}", [("objectClass", "user"), ("objectCategory", "person"), ("sAMAccountName", sam), ("displayName", name), ("userAccountControl", "512"), .. more]);
        Person("jreyes", "Jordan Reyes", Lab);
        Person("pat", "Pat Primary", Lab, ("primaryGroupID", "1105"));
        Person("boss", "Boss", Lab, ("adminCount", "1"));
        Person("krbtgt", "krbtgt", Lab, ("isCriticalSystemObject", "TRUE"));
        Person("svc-existing", "svc-existing", Lab);
        Person("outsider", "Out Sider", Other);
        Person("awiles", "Anthony Wiles", Lab);
        Person("john.smith", "John Smith", Lab, ("userPrincipalName", "jsmith@amw.com"));
        Person("jsmith", "Jane Smith", Lab, ("userPrincipalName", "jane.smith@amw.com"));
        void Group(string name, string ou, int rid, params (string, object)[] more) =>
            Add($"CN={name},{ou}", [("objectClass", "group"), ("objectCategory", "group"), ("cn", name), ("sAMAccountName", name), ("groupType", "-2147483646"), ("objectSid", Sid(rid)), .. more]);
        Group("Lab Staff", Lab, 1105);
        Group("Lab Operators", Lab, 1106, ("isCriticalSystemObject", "TRUE"));
        Group("Elsewhere", Other, 1107);
        void Computer(string name, string ou, int flags) =>
            Add($"CN={name},{ou}", ("objectClass", "computer"), ("objectCategory", "computer"), ("cn", name), ("sAMAccountName", name + "$"), ("userAccountControl", flags.ToString()));
        Computer("PC1", Lab, 0x1000);
        Computer("PC2", Other, 0x1000);
        Computer("DC9", Lab, 0x2000);
    }

    /// <summary>Signs in when the password is "right".</summary>
    public Task<ILdap?> Open(string domain, string host, string username, string password, CancellationToken stop)
    {
        if (password != "right")
        {
            return Task.FromResult<ILdap?>(null);
        }
        Opens++;
        return Task.FromResult<ILdap?>(this);
    }

    public static Item Entry(string dn, params (string Name, object Value)[] attributes) =>
        new(dn, attributes.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(a => a.Value).ToArray(), StringComparer.OrdinalIgnoreCase));

    public void Add(string dn, params (string Name, object Value)[] attributes) => Rows.Add(Entry(dn, attributes));

    public Task<List<LdapRow>> Find(string root, string filter, bool subtree, string[] attributes, int limit, CancellationToken stop) =>
        Do($"find {root} {filter}", () => Search(Rows, root, filter, subtree, limit));

    public Task<List<LdapRow>> FindDeleted(string root, string filter, bool subtree, string[] attributes, int limit, CancellationToken stop) =>
        Do($"find-deleted {root} {filter}", () => Search(Deleted, root, filter, subtree, limit));

    public Task<List<LdapRow>> FindChildren(string dn, string[] attributes, int limit, CancellationToken stop) =>
        Do($"children {dn}", () => Rows.Where(r => r.Dn.Length > 0 && Same(Ldap.Parent(r.Dn), dn)).Take(limit).Select(r => new LdapRow(r.Dn, r.Values)).ToList());

    public Task Add(string dn, IReadOnlyDictionary<string, object[]> attributes, CancellationToken stop) => Do($"add {dn}", () =>
    {
        var values = new Dictionary<string, object[]>(attributes, StringComparer.OrdinalIgnoreCase);
        if (values.TryGetValue("objectClass", out var c) && c.Contains("user"))
        {
            values["objectCategory"] = ["person"];
        }
        Rows.Add(new Item(dn, values));
        return true;
    });

    public async Task Modify(string dn, IReadOnlyList<LdapMod> changes, CancellationToken stop)
    {
        foreach (var m in changes)
        {
            await Do($"modify {dn} {m.Attribute}", () => true);
        }
    }

    public Task Delete(string dn, CancellationToken stop) => Do($"delete {dn}", () => Rows.RemoveAll(r => Same(r.Dn, dn)));

    public Task WriteDacl(string dn, byte[] descriptor, CancellationToken stop) => Do($"dacl {dn}", () => true);

    public Task Rename(string dn, string rdn, string parent, CancellationToken stop) => Do($"rename {dn} -> {rdn},{parent}", () =>
    {
        var at = Rows.FindIndex(r => Same(r.Dn, dn));
        if (at >= 0)
        {
            Rows[at] = Rows[at] with { Dn = $"{rdn},{parent}" };
        }
        return true;
    });

    public Task Restore(string dn, string newDn, CancellationToken stop) => Do($"restore {dn} -> {newDn}", () => true);

    // Everything stored on it as bytes, as a "*" read gives; not the constructed allowed* ones, which only come when asked for.
    public Task<Dictionary<string, byte[][]>?> ReadAll(string dn, CancellationToken stop) => Task.FromResult(Rows.FirstOrDefault(r => Same(r.Dn, dn)) is { } r
        ? r.Values.Where(kv => !kv.Key.StartsWith("allowed", StringComparison.OrdinalIgnoreCase)).ToDictionary(kv => kv.Key,
            kv => kv.Value.Select(v => v as byte[] ?? Encoding.UTF8.GetBytes(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "")).ToArray(), StringComparer.OrdinalIgnoreCase)
        : null);

    public Task<string?> WhoAmI(CancellationToken stop) => Task.FromResult(Who);

    public void Dispose()
    {
        // The one connection is handed out again: nothing to close.
    }

    private Task<T> Do<T>(string what, Func<T> work)
    {
        if (!what.StartsWith("find", StringComparison.Ordinal) && !what.StartsWith("children", StringComparison.Ordinal))
        {
            Log.Add(what);
        }
        return Fail?.Invoke(what) is { } problem ? Task.FromException<T>(problem) : Task.FromResult(work());
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static List<LdapRow> Search(List<Item> rows, string root, string filter, bool subtree, int limit) =>
        [.. rows.Where(r => (subtree ? r.Dn.Length > 0 && Ldap.Under(r.Dn, root) : Same(r.Dn, root)) && Matches(filter, r)).Take(limit).Select(r => new LdapRow(r.Dn, r.Values))];

    private static bool Matches(string filter, Item item)
    {
        var at = 0;
        return Part(filter, ref at, item);
    }

    // One (…) of an LDAP filter: and, or, not, or a single test.
    private static bool Part(string f, ref int i, Item item)
    {
        i++; // (
        bool result;
        if (f[i] is '&' or '|')
        {
            var and = f[i++] == '&';
            result = and;
            while (f[i] == '(')
            {
                var one = Part(f, ref i, item);
                result = and ? result && one : result || one;
            }
        }
        else if (f[i] == '!')
        {
            i++;
            result = !Part(f, ref i, item);
        }
        else
        {
            var end = f.IndexOf(')', i);
            result = Test(f[i..end], item);
            i = end;
        }
        i++; // )
        return result;
    }

    private static bool Test(string test, Item item)
    {
        var eq = test.IndexOf('=');
        var name = test[..eq];
        if (name.EndsWith('>') || name.EndsWith('<') || name.Contains(':'))
        {
            return true; // ranges and bit tests: every row passes
        }
        var values = item.Values.TryGetValue(name, out var v) ? v.OfType<string>().ToList() : [];
        var wanted = test[(eq + 1)..];
        if (name.Equals("objectClass", StringComparison.OrdinalIgnoreCase) && wanted == "*")
        {
            return true;
        }
        var pattern = "^" + string.Join(".*", wanted.Split('*').Select(p => Regex.Escape(Regex.Replace(p, @"\\([0-9a-fA-F]{2})", m => ((char) Convert.ToInt32(m.Groups[1].Value, 16)).ToString())))) + "$";
        return values.Any(x => Regex.IsMatch(x, pattern, RegexOptions.IgnoreCase));
    }
}
