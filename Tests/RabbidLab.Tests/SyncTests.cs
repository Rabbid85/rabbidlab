using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Sync: reading replication metadata, where each change has got to, the words for it, and Entra Connect's answer.</summary>
public sealed class SyncTests
{
    private const string BaseDn = "DC=amw,DC=com";
    private static readonly DateTime Since = new(2026, 11, 21, 14, 0, 0, DateTimeKind.Utc);

    private static string Attr(string name, int version, string at, string dc) =>
        $"<DS_REPL_ATTR_META_DATA><pszAttributeName>{name}</pszAttributeName><dwVersion>{version}</dwVersion><ftimeLastOriginatingChange>{at}</ftimeLastOriginatingChange>"
        + $"<uuidLastOriginatingDsaInvocationID>x</uuidLastOriginatingDsaInvocationID><pszLastOriginatingDsaDN>CN=NTDS Settings,CN={dc},CN=Servers,CN=Lab-Site,CN=Sites,CN=Configuration,{BaseDn}</pszLastOriginatingDsaDN></DS_REPL_ATTR_META_DATA>";

    private static string Member(string who, int version, string at, bool gone = false) =>
        $"<DS_REPL_VALUE_META_DATA><pszAttributeName>member</pszAttributeName><pszObjectDn>CN={who},OU=Staff,{BaseDn}</pszObjectDn><cbData>0</cbData>"
        + $"<ftimeDeleted>{(gone ? at : "1601-01-01T00:00:00Z")}</ftimeDeleted><dwVersion>{version}</dwVersion><ftimeLastOriginatingChange>{at}</ftimeLastOriginatingChange>"
        + $"<pszLastOriginatingDsaDN>CN=NTDS Settings,CN=DC01,CN=Servers,CN=Lab-Site,CN=Sites,CN=Configuration,{BaseDn}</pszLastOriginatingDsaDN></DS_REPL_VALUE_META_DATA>";

    private static LdapRow Row(string kind, string[] attrs, string[]? values = null, string created = "20260101000000.0Z", Dictionary<string, string>? more = null)
    {
        var map = new Dictionary<string, object[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["objectClass"] = kind == "user" ? ["top", "person", "organizationalPerson", "user"] : kind == "computer" ? ["top", "user", "computer"] : ["top", "group"],
            ["cn"] = ["Alex Moreno"],
            ["displayName"] = ["Alex Moreno"],
            ["sAMAccountName"] = ["amoreno"],
            ["whenCreated"] = [created],
            ["msDS-ReplAttributeMetaData"] = attrs,
            ["msDS-ReplValueMetaData"] = values ?? [],
        };
        foreach (var (k, v) in more ?? [])
        {
            map[k] = [v];
        }
        return new LdapRow($"CN=Alex Moreno,OU=Staff,{BaseDn}", map);
    }

    [Fact]
    public void Metadata_is_read_with_where_it_was_made()
    {
        var a = SyncCompare.Attribute(Attr("title", 3, "2026-11-21T14:31:00Z", "DC02"));
        Assert.Equal(("title", 3), (a.Name, a.Version));
        Assert.Equal(new DateTime(2026, 11, 21, 14, 31, 0, DateTimeKind.Utc), a.At);
        Assert.Equal("DC02", SyncCompare.Server(a.Dsa));
        Assert.True(SyncCompare.Value(Member("Kiran Patel", 2, "2026-11-21T14:38:00Z", gone: true)).Deleted);
        Assert.False(SyncCompare.Value(Member("Kiran Patel", 1, "2026-11-21T14:38:00Z")).Deleted);
    }

    [Fact]
    public void A_dc_with_an_older_version_is_missing_the_change()
    {
        var newer = SyncCompare.Seen(Row("user", [Attr("title", 3, "2026-11-21T14:31:00Z", "DC02"), Attr("lastLogonTimestamp", 9, "2026-11-21T14:40:00Z", "DC01")],
            more: new() { ["title"] = "Senior analyst" }));
        var older = SyncCompare.Seen(Row("user", [Attr("title", 2, "2026-10-01T10:00:00Z", "DC01")]));
        var byDc = new Dictionary<string, SyncSeen?>(StringComparer.OrdinalIgnoreCase) { ["DC01"] = older, ["DC02"] = newer, ["DC03"] = null };
        var c = SyncCompare.Compare("id", byDc, ["DC01", "DC02", "DC03", "DC04"], Since, BaseDn)!;
        Assert.Equal("DC02", c.On);
        Assert.Equal("Title", c.What);
        Assert.Equal(["Title: Senior analyst"], c.Details);
        Assert.Equal(SyncCompare.Pending, c.Places["DC01"]);
        Assert.Equal(SyncCompare.Origin, c.Places["DC02"]);
        Assert.Equal(SyncCompare.Pending, c.Places["DC03"]); // doesn't have the object yet
        Assert.Equal(SyncCompare.Unknown, c.Places["DC04"]); // couldn't be read
    }

    [Fact]
    public void Sign_ins_and_a_computers_own_password_are_not_changes()
    {
        var signIn = SyncCompare.Seen(Row("user", [Attr("lastLogonTimestamp", 9, "2026-11-21T14:40:00Z", "DC01")]));
        Assert.Null(SyncCompare.Compare("a", new Dictionary<string, SyncSeen?> { ["DC01"] = signIn }, ["DC01"], Since, BaseDn));
        var machine = SyncCompare.Seen(Row("computer", [Attr("pwdLastSet", 4, "2026-11-21T14:40:00Z", "DC01"), Attr("unicodePwd", 4, "2026-11-21T14:40:00Z", "DC01")]));
        Assert.Null(SyncCompare.Compare("b", new Dictionary<string, SyncSeen?> { ["DC01"] = machine }, ["DC01"], Since, BaseDn));
        var person = SyncCompare.Seen(Row("user", [Attr("pwdLastSet", 4, "2026-11-21T14:36:00Z", "DC01"), Attr("unicodePwd", 4, "2026-11-21T14:36:00Z", "DC01")]));
        Assert.Equal("Password changed", SyncCompare.Compare("c", new Dictionary<string, SyncSeen?> { ["DC01"] = person }, ["DC01"], Since, BaseDn)!.What);
    }

    [Fact]
    public void New_objects_members_and_switches_are_said_plainly()
    {
        var made = SyncCompare.Seen(Row("user", [Attr("title", 1, "2026-11-21T14:38:00Z", "DC01")], created: "20261121143800.0Z", more: new() { ["title"] = "Payroll clerk" }));
        var c = SyncCompare.Compare("n", new Dictionary<string, SyncSeen?> { ["DC01"] = made }, ["DC01"], Since, BaseDn)!;
        Assert.Equal("New user", c.What);
        Assert.Contains("Where: Staff", c.Details);
        Assert.Contains("Title: Payroll clerk", c.Details);

        var group = SyncCompare.Seen(Row("group", [], [Member("Kiran Patel", 1, "2026-11-21T14:38:00Z"), Member("Old Hand", 1, "2026-01-01T00:00:00Z")]));
        var g = SyncCompare.Compare("g", new Dictionary<string, SyncSeen?> { ["DC01"] = group, ["DC02"] = SyncCompare.Seen(Row("group", [])) }, ["DC01", "DC02"], Since, BaseDn)!;
        Assert.Equal("Member added", g.What);
        Assert.Equal(["Added Kiran Patel"], g.Details);
        Assert.Equal(SyncCompare.Pending, g.Places["DC02"]);

        var off = SyncCompare.Seen(Row("user", [Attr("userAccountControl", 5, "2026-11-21T14:12:00Z", "DC01")], more: new() { ["userAccountControl"] = "514" }));
        Assert.Equal("Disabled", SyncCompare.Compare("d", new Dictionary<string, SyncSeen?> { ["DC01"] = off }, ["DC01"], Since, BaseDn)!.What);
        var two = SyncCompare.Seen(Row("user", [Attr("title", 3, "2026-11-21T14:31:00Z", "DC02"), Attr("manager", 2, "2026-11-21T14:31:00Z", "DC02")],
            more: new() { ["title"] = "Senior analyst", ["manager"] = "CN=Dana Lee,OU=Staff,DC=amw,DC=com" }));
        var t = SyncCompare.Compare("t", new Dictionary<string, SyncSeen?> { ["DC02"] = two }, ["DC02"], Since, BaseDn)!;
        Assert.Equal("Title and manager", t.What);
        Assert.Contains("Manager: Dana Lee", t.Details);
    }

    [Fact]
    public void A_guid_goes_into_a_filter_byte_by_byte()
    {
        var bytes = Enumerable.Range(1, 16).Select(i => (byte) i).ToArray();
        var id = SyncCompare.Id(bytes);
        Assert.Equal(new Guid(bytes).ToString(), id);
        Assert.Equal(@"\01\02\03\04\05\06\07\08\09\0a\0b\0c\0d\0e\0f\10", SyncCompare.Filter(id));
    }

    [Fact]
    public void Entra_connects_server_comes_from_its_account()
    {
        Assert.Equal("SYNC01", SyncEntra.Server("Account created by Microsoft Azure Active Directory Connect with installation identifier 123 running on computer sync01 configured to synchronize to tenant amw.onmicrosoft.com."));
        Assert.Null(SyncEntra.Server("Built-in account"));
    }

    [Fact]
    public void Entra_connects_answer_is_read()
    {
        var ok = SyncEntra.Read("noise\nRESULT {\"ok\":true,\"waited\":false,\"done\":true,\"next\":\"2026-11-21T15:30:00Z\",\"runs\":[{\"connector\":\"amw.com\",\"sent\":5},{\"connector\":\"amw.onmicrosoft.com - AAD\",\"sent\":3}]}");
        Assert.True(ok.Ok);
        Assert.Equal(3, ok.Sent);
        Assert.Equal(new DateTime(2026, 11, 21, 15, 30, 0, DateTimeKind.Utc), ok.Next);
        var missing = SyncEntra.Read("RESULT {\"ok\":false,\"error\":\"The specified module 'ADSync' was not loaded because no valid module file was found.\"}");
        Assert.Equal("Entra Connect isn't installed on that server.", missing.Error);
        Assert.False(SyncEntra.Read("nothing useful").Ok);
        Assert.Contains("Start-ADSyncSyncCycle -PolicyType Initial", SyncEntra.Script(full: true));
        Assert.Contains("Start-ADSyncSyncCycle -PolicyType Delta", SyncEntra.Script(full: false));
    }

    private sealed class Coded(int code) : Exception("RPC")
    {
        public int ErrorCode => code;
    }

    [Fact]
    public void Replication_errors_say_what_to_do()
    {
        Assert.Equal("RPC server unavailable", SyncWhy.Explain(new Coded(1722)).Said);
        Assert.Contains("Replication Synchronization", SyncWhy.Explain(new Coded(8453)).Advice);
        Assert.Equal("DC-BRANCH", SyncWhy.Short("dc-branch.amw.com"));
        Assert.Equal("Schema", SyncWhy.Part("CN=Schema,CN=Configuration,DC=amw,DC=com"));
        Assert.Equal("Domain", SyncWhy.Part("DC=amw,DC=com"));
    }
}
