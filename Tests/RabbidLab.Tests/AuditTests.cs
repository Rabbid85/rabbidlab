using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// The AD audit: Security log events made into changes with old and new values, kept for the days set, read from each DC
/// by the collector, and shown with replication metadata for what no log recorded. No test reads a real DC's log.
/// </summary>
public sealed class AuditTests : IDisposable
{
    private const string Greg = "CN=Greg Sutton,OU=Sales,OU=Staff,DC=amw,DC=com";
    private const string GregGuid = "4b7c2f6a-1d2e-4c3b-9a8f-7e6d5c4b3a21";
    private const string Op = "{8F2C1A6E-0D3B-4F6A-9B1E-2C7D5E4A3B21}";
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-audit-" + Guid.NewGuid().ToString("N"));

    public AuditTests() => Directory.CreateDirectory(data);

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

    // A 5136 as Event Viewer's XML view shows it: the title's old value taken away.
    private const string Sample = """
        <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
          <System>
            <Provider Name="Microsoft-Windows-Security-Auditing" Guid="{54849625-5478-4994-A5BA-3E3B0328C30D}" />
            <EventID>5136</EventID>
            <Version>0</Version>
            <Level>0</Level>
            <Task>14081</Task>
            <Opcode>0</Opcode>
            <Keywords>0x8020000000000000</Keywords>
            <TimeCreated SystemTime="2026-11-22T14:02:11.4567890Z" />
            <EventRecordID>1001</EventRecordID>
            <Correlation />
            <Execution ProcessID="696" ThreadID="1840" />
            <Channel>Security</Channel>
            <Computer>DC01.amw.com</Computer>
            <Security />
          </System>
          <EventData>
            <Data Name="OpCorrelationID">{8F2C1A6E-0D3B-4F6A-9B1E-2C7D5E4A3B21}</Data>
            <Data Name="AppCorrelationID">-</Data>
            <Data Name="SubjectUserSid">S-1-5-21-1-2-3-1104</Data>
            <Data Name="SubjectUserName">pnair</Data>
            <Data Name="SubjectDomainName">AMW</Data>
            <Data Name="SubjectLogonId">0x4a3f2</Data>
            <Data Name="DSName">amw.com</Data>
            <Data Name="DSType">%%14676</Data>
            <Data Name="ObjectDN">CN=Greg Sutton,OU=Sales,OU=Staff,DC=amw,DC=com</Data>
            <Data Name="ObjectGUID">{4B7C2F6A-1D2E-4C3B-9A8F-7E6D5C4B3A21}</Data>
            <Data Name="ObjectClass">user</Data>
            <Data Name="AttributeLDAPDisplayName">title</Data>
            <Data Name="AttributeSyntaxOID">2.5.5.12</Data>
            <Data Name="AttributeValue">Sales associate</Data>
            <Data Name="OperationType">%%14675</Data>
          </EventData>
        </Event>
        """;

    /// <summary>An event's XML with these data values.</summary>
    public static string Xml(int id, long record, DateTime at, params (string Name, string Value)[] values) =>
        $"""<Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event"><System><EventID>{id}</EventID><TimeCreated SystemTime="{at:yyyy-MM-ddTHH:mm:ss.fffffffZ}" /><EventRecordID>{record}</EventRecordID><Channel>Security</Channel></System><EventData>"""
        + string.Concat(values.Select(v => $"<Data Name=\"{v.Name}\">{System.Security.SecurityElement.Escape(v.Value)}</Data>")) + "</EventData></Event>";

    // One value of an attribute of Greg's taken away or added, by Priya, in the operation.
    private static string Value(long record, DateTime at, string attribute, string value, bool added, string op = Op, string dn = Greg) =>
        Xml(5136, record, at, ("OpCorrelationID", op), ("SubjectUserName", "pnair"), ("SubjectDomainName", "AMW"), ("ObjectDN", dn), ("ObjectGUID", $"{{{GregGuid}}}"),
            ("ObjectClass", "user"), ("AttributeLDAPDisplayName", attribute), ("AttributeValue", value), ("OperationType", added ? "%%14674" : "%%14675"));

    private static List<SecurityEvent> Parse(params string[] xml) => [.. xml.Select(x => SecurityEvents.Parse(x, "DC01")).OfType<SecurityEvent>()];

    [Fact]
    public void A_5136_is_read_from_its_XML()
    {
        var e = SecurityEvents.Parse(Sample, "DC01");

        Assert.NotNull(e);
        Assert.Equal(5136, e.Id);
        Assert.Equal(1001, e.Record);
        Assert.Equal(new DateTime(2026, 11, 22, 14, 2, 11, DateTimeKind.Utc), e.At.AddTicks(-(e.At.Ticks % TimeSpan.TicksPerSecond)));
        Assert.Equal(DateTimeKind.Utc, e.At.Kind);
        Assert.Equal("pnair", e.Get("SubjectUserName"));
        Assert.Equal(Greg, e.Get("ObjectDN"));
        Assert.Null(e.Get("AppCorrelationID")); // "-" is nothing
        Assert.Null(SecurityEvents.Parse("<Event><System>", "DC01"));
        Assert.Null(SecurityEvents.Parse("<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\"><System /></Event>", "DC01"));
    }

    [Fact]
    public void The_values_taken_away_and_added_in_one_operation_are_the_old_and_new_value()
    {
        var at = DateTime.UtcNow.AddMinutes(-20);
        var events = Parse(Sample.Replace("2026-11-22T14:02:11.4567890Z", $"{at:yyyy-MM-ddTHH:mm:ss.fffffffZ}"),
            Value(1002, at, "title", "Sales lead", added: true),
            Value(1003, at, "department", "Sales", added: false),
            Value(1004, at, "department", "Sales and marketing", added: true),
            Value(1005, at, "telephoneNumber", "555-0199", added: true),
            // Another operation on the same attribute is its own change.
            Value(1006, at.AddMinutes(1), "title", "Sales lead", added: false, op: "{11111111-2222-3333-4444-555555555555}"),
            Value(1007, at.AddMinutes(1), "title", "Sales manager", added: true, op: "{11111111-2222-3333-4444-555555555555}"));

        var entries = AuditEntries.From(events);

        Assert.Equal(4, entries.Count);
        var title = Assert.Single(entries, e => e.Attribute == "title" && e.Op == "8f2c1a6e-0d3b-4f6a-9b1e-2c7d5e4a3b21");
        Assert.Equal(["Sales associate"], title.Old);
        Assert.Equal(["Sales lead"], title.New);
        Assert.Equal("DC01", title.Dc);
        Assert.Equal(@"AMW\pnair", title.Account);
        Assert.Equal(GregGuid, title.Guid);
        Assert.Equal("user", title.Kind);
        Assert.Equal("changed", title.What);
        var department = Assert.Single(entries, e => e.Attribute == "department");
        Assert.Equal(["Sales"], department.Old);
        Assert.Equal(["Sales and marketing"], department.New);
        Assert.Empty(Assert.Single(entries, e => e.Attribute == "telephoneNumber").Old); // set where there was nothing
        var later = Assert.Single(entries, e => e.Op == "11111111-2222-3333-4444-555555555555");
        Assert.Equal(["Sales lead"], later.Old);
        Assert.Equal(["Sales manager"], later.New);
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct().Count());

        var rows = AuditView.Rows(entries, [], [], "svc-rabbidlab");
        var shown = Assert.Single(rows, r => r.Attribute == "title" && r.Old![0] == "Sales associate");
        Assert.Equal("Job title", shown.Label);
        Assert.Equal("Greg Sutton", shown.Name);
        Assert.Equal("Staff / Sales", shown.Where);
        Assert.Equal("pnair", shown.By); // no display name known yet: the username
        Assert.Equal(5136, shown.Event);
        Assert.Equal(later.Id, rows[0].Id); // newest first
    }

    [Fact]
    public void Members_added_and_removed_in_one_operation_are_listed_apart()
    {
        var at = DateTime.UtcNow.AddMinutes(-5);
        string Member(long record, string who, bool added) => Xml(5136, record, at, ("OpCorrelationID", Op), ("SubjectUserName", "tbecker"), ("SubjectDomainName", "AMW"),
            ("ObjectDN", "CN=VPN users,OU=Groups,DC=amw,DC=com"), ("ObjectGUID", "{aaaaaaaa-1111-2222-3333-444444444444}"), ("ObjectClass", "group"),
            ("AttributeLDAPDisplayName", "member"), ("AttributeValue", $"CN={who},OU=Staff,DC=amw,DC=com"), ("OperationType", added ? "%%14674" : "%%14675"));

        var entries = AuditEntries.From(Parse(Member(1, "Sam Holt", true), Member(2, "Nina Park", true), Member(3, "Old Kiosk", false)));
        var rows = AuditView.Rows(entries, [], [], null);

        var added = Assert.Single(rows, r => r.Label == "Member added");
        Assert.Equal(["Sam Holt", "Nina Park"], added.New);
        Assert.Empty(added.Old!);
        var removed = Assert.Single(rows, r => r.Label == "Member removed");
        Assert.Equal(["Old Kiosk"], removed.Old);
        Assert.Empty(removed.New!);
        Assert.All(rows, r => Assert.Equal("group", r.Kind));
        Assert.Single(rows.Select(r => r.Op).Distinct());
    }

    [Fact]
    public void Objects_created_moved_deleted_and_restored_are_one_change_each()
    {
        var at = DateTime.UtcNow.AddHours(-1);
        (string, string)[] Who(string op, string cls = "user") => [("OpCorrelationID", op), ("SubjectUserName", "lortiz"), ("SubjectDomainName", "AMW"), ("ObjectClass", cls)];
        var made = Xml(5137, 10, at, [.. Who("{00000000-0000-0000-0000-000000000001}", "computer"), ("ObjectDN", "CN=WS-FRONT-02,OU=Workstations,DC=amw,DC=com"),
            ("ObjectGUID", "{00000000-0000-0000-0000-0000000000aa}")]);
        var deleted = Xml(5141, 11, at, [.. Who("{00000000-0000-0000-0000-000000000002}"), ("ObjectDN", "CN=Old Kiosk,OU=Kiosks,DC=amw,DC=com"),
            ("ObjectGUID", "{00000000-0000-0000-0000-0000000000bb}"), ("TreeDelete", "%%1843")]);
        var moved = Xml(5139, 12, at, [.. Who("{00000000-0000-0000-0000-000000000003}"), ("OldObjectDN", "CN=Lena Ortiz,OU=Sales,OU=Staff,DC=amw,DC=com"),
            ("NewObjectDN", "CN=Lena Ortiz,OU=IT,OU=Staff,DC=amw,DC=com"), ("ObjectGUID", "{00000000-0000-0000-0000-0000000000cc}")]);
        var restored = Xml(5138, 13, at, [.. Who("{00000000-0000-0000-0000-000000000004}"), ("OldObjectDN", @"CN=Tom Becker\0ADEL:0000,CN=Deleted Objects,DC=amw,DC=com"),
            ("NewObjectDN", "CN=Tom Becker,OU=IT,OU=Staff,DC=amw,DC=com"), ("ObjectGUID", "{00000000-0000-0000-0000-0000000000dd}")]);
        // Into Deleted Objects is part of the delete; a GPO isn't something the audit lists.
        var binned = Xml(5139, 14, at, [.. Who("{00000000-0000-0000-0000-000000000002}"), ("OldObjectDN", "CN=Old Kiosk,OU=Kiosks,DC=amw,DC=com"),
            ("NewObjectDN", @"CN=Old Kiosk\0ADEL:bb,CN=Deleted Objects,DC=amw,DC=com"), ("ObjectGUID", "{00000000-0000-0000-0000-0000000000bb}")]);
        var policy = Xml(5136, 15, at, [.. Who("{00000000-0000-0000-0000-000000000005}", "groupPolicyContainer"), ("ObjectDN", "CN={31B2F340-016D-11D2-945F-00C04FB984F9},CN=Policies,CN=System,DC=amw,DC=com"),
            ("AttributeLDAPDisplayName", "versionNumber"), ("AttributeValue", "7"), ("OperationType", "%%14674")]);

        var rows = AuditView.Rows(AuditEntries.From(Parse(made, deleted, moved, restored, binned, policy)), [], [], null);

        Assert.Equal(4, rows.Count);
        var pc = Assert.Single(rows, r => r.What == "made");
        Assert.Equal(("Object", "computer", "WS-FRONT-02"), (pc.Label, pc.Kind, pc.Name));
        Assert.Equal(["Created in Workstations"], pc.New);
        Assert.Equal(5137, pc.Event);
        var gone = Assert.Single(rows, r => r.What == "deleted");
        Assert.Equal(["In Kiosks"], gone.Old);
        Assert.Equal(["Deleted"], gone.New);
        var lena = Assert.Single(rows, r => r.What == "moved");
        Assert.Equal("Location", lena.Label);
        Assert.Equal(["Staff / Sales"], lena.Old);
        Assert.Equal(["Staff / IT"], lena.New);
        Assert.Equal("CN=Lena Ortiz,OU=IT,OU=Staff,DC=amw,DC=com", lena.Dn);
        var back = Assert.Single(rows, r => r.What == "restored");
        Assert.Equal(["Restored to Staff / IT"], back.New);
        Assert.Equal("Tom Becker", back.Name);
    }

    [Fact]
    public void A_secret_s_value_is_never_kept()
    {
        var at = DateTime.UtcNow.AddMinutes(-5);
        var entry = Assert.Single(AuditEntries.From(Parse(Value(1, at, "ms-Mcs-AdmPwd", "Hunter2!", added: true))));

        Assert.Equal(["(hidden)"], entry.New);
        Assert.DoesNotContain("Hunter2!", System.Text.Json.JsonSerializer.Serialize(entry));
    }

    [Fact]
    public void Values_are_shown_in_words()
    {
        Assert.Equal("Disabled", AuditView.Show("userAccountControl", "514"));
        Assert.Equal("Enabled, password never expires", AuditView.Show("userAccountControl", "66048"));
        Assert.Equal("Security, Global", AuditView.Show("groupType", "-2147483646"));
        Assert.Equal("Distribution, Universal", AuditView.Show("groupType", "8"));
        Assert.Equal("Must change password at next sign-in", AuditView.Show("pwdLastSet", "0"));
        Assert.Equal("Never", AuditView.Show("accountExpires", "9223372036854775807"));
        Assert.Equal("2026-11-17 14:03 UTC", AuditView.Show("pwdLastSet", new DateTime(2026, 11, 17, 14, 3, 0, DateTimeKind.Utc).ToFileTimeUtc().ToString()));
        Assert.Equal("Lena Ortiz", AuditView.Show("manager", "CN=Lena Ortiz,OU=IT,OU=Staff,DC=amw,DC=com"));
        Assert.Equal("(hidden)", AuditView.Show("unicodePwd", "secret"));
        Assert.Equal("Job title", AuditView.Label("title", ["a"], ["b"]));
        Assert.Equal("Custom attribute 7", AuditView.Label("extensionAttribute7", [], ["x"]));
        Assert.Equal("msDS-cloudExtensionAttribute1", AuditView.Label("msDS-cloudExtensionAttribute1", [], ["x"]));
    }

    [Fact]
    public void Metadata_fills_in_what_no_event_recorded_and_leaves_out_what_one_did()
    {
        var at = DateTime.UtcNow.AddMinutes(-30);
        var events = AuditEntries.From(Parse(Value(1, at, "title", "Sales associate", added: false), Value(2, at, "title", "Sales lead", added: true)));
        var meta = new AdChange(Greg, "Greg Sutton", "user", "Staff / Sales", at, "changed",
        [
            new("title", at.AddSeconds(1), "DC01", ["Sales lead"]), // the same change the event says
            new("telephoneNumber", at.AddHours(-2), "DC02", ["555-0142"]), // made on a DC that recorded nothing
            new("logonHours", at.AddHours(-3), "DC02", null), // not read: its value isn't known
            new("member", at.AddHours(-4), "DC02", ["CN=Sam Holt,OU=Staff,DC=amw,DC=com"], Removed: true),
        ], GregGuid);
        var made = new AdChange("CN=Nina Park,OU=Finance,OU=Staff,DC=amw,DC=com", "Nina Park", "user", "Staff / Finance", at.AddDays(-1), "made", [], null);

        var rows = AuditView.Rows(events, [meta, made], [], null);

        var title = Assert.Single(rows, r => r.Attribute == "title");
        Assert.Equal(5136, title.Event);
        Assert.Equal(["Sales associate"], title.Old);
        var phone = Assert.Single(rows, r => r.Attribute == "telephoneNumber");
        Assert.Null(phone.Event);
        Assert.Null(phone.Old); // not recorded
        Assert.Equal(["555-0142"], phone.New);
        Assert.Equal(("DC02", "Phone"), (phone.Dc, phone.Label));
        Assert.Null(Assert.Single(rows, r => r.Attribute == "logonHours").New);
        var member = Assert.Single(rows, r => r.Attribute == "member");
        Assert.Equal(("Member removed", "Sam Holt"), (member.Label, member.Old![0]));
        var nina = Assert.Single(rows, r => r.Name == "Nina Park");
        Assert.Equal(["Created in Staff / Finance"], nina.New);
        Assert.Equal(rows.OrderByDescending(r => r.At).Select(r => r.Id), rows.Select(r => r.Id));
    }

    [Fact]
    public void A_change_the_service_account_made_says_who_made_it_in_Rabbid_Lab()
    {
        var at = DateTime.UtcNow.AddMinutes(-2);
        var entries = AuditEntries.From(Parse(
            Xml(5136, 1, at, ("OpCorrelationID", Op), ("SubjectUserName", "svc-rabbidlab"), ("SubjectDomainName", "AMW"), ("ObjectDN", Greg), ("ObjectGUID", GregGuid),
                ("ObjectClass", "user"), ("AttributeLDAPDisplayName", "userAccountControl"), ("AttributeValue", "512"), ("OperationType", "%%14675")),
            Xml(5136, 2, at, ("OpCorrelationID", Op), ("SubjectUserName", "svc-rabbidlab"), ("SubjectDomainName", "AMW"), ("ObjectDN", Greg), ("ObjectGUID", GregGuid),
                ("ObjectClass", "user"), ("AttributeLDAPDisplayName", "userAccountControl"), ("AttributeValue", "514"), ("OperationType", "%%14674"))));
        var ours = new List<ActivityEntry> { new(at.AddSeconds(2), "awiles", "10.0.0.5", "users", "ad.enable", "Disabled Greg Sutton", "ok", null) };

        var row = Assert.Single(AuditView.Rows(entries, [], ours, "svc-rabbidlab"));
        var theirs = Assert.Single(AuditView.Rows(entries, [], ours, "someone-else"));

        Assert.Equal(("awiles", true, @"AMW\svc-rabbidlab"), (row.By, row.App, row.Account));
        Assert.Equal(("Account", "Enabled", "Disabled"), (row.Label, row.Old![0], row.New![0]));
        Assert.Equal(("svc-rabbidlab", false), (theirs.By, theirs.App));
    }

    [Fact]
    public void A_DC_whose_log_is_read_but_records_nothing_is_silent()
    {
        var read = DateTime.UtcNow.AddHours(-6);
        AuditDcState State(string state = AuditDcState.Read) => new() { State = state, FirstRead = read, LastRead = DateTime.UtcNow, Problem = state == "denied" ? "Access denied." : null };
        AuditRow Row(string dc, DateTime at, int? ev) => new($"{dc}{at.Ticks}", at, null, null, false, Greg, "Greg Sutton", "user", "", null, "title", "Job title", null, ["x"], dc, "op", "changed", ev);
        var rows = new List<AuditRow>
        {
            Row("DC01", DateTime.UtcNow.AddHours(-1), 5136), // recorded
            Row("DC01", DateTime.UtcNow.AddDays(-3), null), // before its log was first read
            Row("DC02", DateTime.UtcNow.AddHours(-1), null), // after: it should have been in the log
        };

        var dcs = AuditView.Dcs([("DC01", State()), ("DC02", State()), ("DC03", State("denied")), ("DC04", new AuditDcState())], rows);

        Assert.Equal(["recording", "silent", "denied", "waiting"], dcs.Select(d => d.State));
        Assert.Equal("Access denied.", dcs[2].Problem);
    }

    [Fact]
    public void Changes_are_kept_for_the_days_set_and_survive_a_restart()
    {
        var folder = Folder();
        var clock = new FakeClock();
        var store = new AuditStore(folder, clock, NullLogger<AuditStore>.Instance);
        AuditEntry Entry(string id, int daysAgo) => new(id, clock.GetUtcNow().UtcDateTime.AddDays(-daysAgo), "DC01", 5136, "op" + id, "changed", Greg, GregGuid, "user", "title",
            ["a"], ["b"], @"AMW\pnair");

        var added = store.Add([Entry("a", 400), Entry("b", 100), Entry("c", 40), Entry("d", 10)]);
        var again = store.Add([Entry("d", 10), Entry("e", 1)]);
        store.Prune(90, now: true);
        var after90 = store.Since(DateTime.MinValue).Select(e => e.Id).ToList();
        store.Prune(30, now: true);
        var after30 = store.Since(DateTime.MinValue).Select(e => e.Id).ToList();
        var reopened = new AuditStore(folder, clock, NullLogger<AuditStore>.Instance).Since(DateTime.MinValue).Select(e => e.Id).ToList();
        File.AppendAllText(Path.Combine(folder.Path, "ad-audit.jsonl"), "\0\0\0\0\n{ not json\n");
        var mended = new AuditStore(folder, clock, NullLogger<AuditStore>.Instance).Since(DateTime.MinValue).Count;

        Assert.Equal(4, added);
        Assert.Equal(1, again); // d was there already
        Assert.Equal(["c", "d", "e"], after90);
        Assert.Equal(["d", "e"], after30);
        Assert.Equal(["d", "e"], reopened);
        Assert.Equal(2, mended); // a cut-off or zero-filled line is skipped
        Assert.Equal(90, AuditCollector.Keeps(45));
        Assert.Equal(365, AuditCollector.Keeps(365));
    }

    [Fact]
    public void One_object_s_history_follows_it_through_a_move()
    {
        var store = new AuditStore(Folder(), new FakeClock(), NullLogger<AuditStore>.Instance);
        var now = DateTime.UtcNow;
        AuditEntry Entry(string id, string dn, string? guid, int hoursAgo) => new(id, now.AddHours(-hoursAgo), "DC01", 5136, id, "changed", dn, guid, "user", "title", [], ["x"], null);
        store.Add([Entry("before", "CN=Greg Sutton,OU=Sales,DC=amw,DC=com", GregGuid, 5), Entry("after", Greg, GregGuid, 1), Entry("other", "CN=Lena Ortiz,OU=IT,DC=amw,DC=com", "11111111-1111-1111-1111-111111111111", 2),
            Entry("old", Greg, GregGuid, 24 * 200)]);

        var byDn = store.Of(Greg, null, now.AddDays(-90)).Select(e => e.Id);
        var byGuid = store.Of("CN=Someone,DC=amw,DC=com", GregGuid, now.AddDays(-90)).Select(e => e.Id);

        Assert.Equal(["before", "after"], byDn);
        Assert.Equal(["before", "after"], byGuid);
    }

    [Fact]
    public async Task The_collector_reads_each_DC_on_from_where_it_got_to_and_waits_longer_for_one_that_fails()
    {
        var folder = Folder();
        var clock = new FakeClock();
        var at = clock.GetUtcNow().UtcDateTime.AddMinutes(-10);
        var store = new AuditStore(folder, clock, NullLogger<AuditStore>.Instance);
        var directory = new FakeAuditDirectory
        {
            Login = new AuditLogin("amw.com", "svc-rabbidlab", "pass", [new SyncDc("DC01", "dc01.amw.com", "HQ", false), new SyncDc("DC02", "dc02.amw.com", "HQ", false),
                new SyncDc("RODC1", "rodc1.amw.com", "Branch", true)]),
            Names = { ["pnair"] = "Priya Nair" },
        };
        var logs = new FakeSecurityLog();
        logs.Logs["dc01.amw.com"] = [Value(1001, at, "title", "Sales associate", false), Value(1002, at, "title", "Sales lead", true),
            Xml(4624, 1003, at, ("TargetUserName", "pnair"))];
        logs.Fails["dc02.amw.com"] = new SecurityLogProblem("denied", "Access denied. svc-rabbidlab cannot read the Security log on DC02.");
        var collector = new AuditCollector(directory, logs, store, new SettingsStore(folder, new EphemeralDataProtectionProvider()), folder, clock, NullLogger<AuditCollector>.Instance);
        var heard = new List<SecurityEvent>();
        collector.Seen += heard.AddRange;

        await collector.Round(None);
        var first = logs.Asked.ToList();
        var states = collector.Dcs().ToDictionary(d => d.Name, d => d.State);
        logs.Logs["dc01.amw.com"].Add(Value(1010, at.AddMinutes(5), "department", "Sales", true, op: "{22222222-2222-2222-2222-222222222222}"));
        clock.Advance(TimeSpan.FromMinutes(2));
        logs.Asked.Clear();
        await collector.Round(None);
        var second = logs.Asked.ToList();

        var entry = Assert.Single(store.Since(DateTime.MinValue), e => e.Attribute == "title");
        Assert.Equal(["Sales associate"], entry.Old);
        Assert.Equal(["Sales lead"], entry.New);
        Assert.Equal("Priya Nair", entry.ByName);
        Assert.Contains(store.Since(DateTime.MinValue), e => e.Attribute == "department");
        Assert.Equal(["dc01.amw.com", "dc02.amw.com"], first.Select(a => a.Host)); // never the read-only DC
        Assert.Contains("EventID=5136", first[0].Query);
        Assert.Contains("TimeCreated[@SystemTime>=", first[0].Query); // the first time: everything in the days kept
        Assert.Equal("read", states["DC01"].State);
        Assert.Equal(1002, states["DC01"].Last); // the sign-in in between isn't a directory change
        Assert.Equal("denied", states["DC02"].State);
        Assert.NotNull(states["DC02"].NextTry);
        Assert.Equal(["dc01.amw.com"], second.Select(a => a.Host)); // DC02 waits its turn
        Assert.Contains("EventRecordID>1002", second[0].Query);
        Assert.Equal([1001L, 1002, 1010], heard.Select(e => e.Record)); // the hook hears every event read, a batch at a time
    }

    // A Kerberos event as a DC's log gives it.
    private static string Kerberos(int id, long record, DateTime at, params (string Name, string Value)[] data) =>
        $"<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>{id}</EventID><EventRecordID>{record}</EventRecordID><TimeCreated SystemTime='{at:O}'/></System><EventData>"
        + string.Concat(data.Select(d => $"<Data Name='{d.Name}'>{d.Value}</Data>")) + "</EventData></Event>";

    [Fact]
    public async Task Every_DC_is_asked_for_the_tickets_round_a_first_ticket()
    {
        var folder = Folder();
        var clock = new FakeClock();
        var at = clock.GetUtcNow().UtcDateTime.AddMinutes(-5);
        var logs = new FakeSecurityLog();
        // The first ticket was given by DC01; the ticket for the computer signed in on by DC02.
        logs.Logs["dc01.amw.com"] = [Kerberos(4768, 1, at, ("TargetUserName", "mwiles"), ("Status", "0x0"), ("IpAddress", "::ffff:192.168.2.186")),
            Kerberos(4768, 2, at, ("TargetUserName", "MEGAN-PC$"), ("Status", "0x0"), ("IpAddress", "::ffff:192.168.2.186"))];
        logs.Logs["dc02.amw.com"] = [Kerberos(4769, 7, at.AddSeconds(1), ("TargetUserName", "mwiles@AMW.COM"), ("ServiceName", "MEGAN-PC$"), ("Status", "0x0"), ("IpAddress", "::ffff:192.168.2.186")),
            Kerberos(4769, 8, at.AddSeconds(2), ("TargetUserName", "MEGAN-PC$@AMW.COM"), ("ServiceName", "DC02$"), ("Status", "0x0"), ("IpAddress", "::ffff:192.168.2.186"))];
        var collector = new AuditCollector(new FakeAuditDirectory { Login = new AuditLogin("amw.com", "svc", "pass", [new SyncDc("DC01", "dc01.amw.com", null, false), new SyncDc("DC02", "dc02.amw.com", null, false)]) },
            logs, new AuditStore(folder, clock, NullLogger<AuditStore>.Instance), new SettingsStore(folder, new EphemeralDataProtectionProvider()), folder, clock, NullLogger<AuditCollector>.Instance);
        var batches = new List<IReadOnlyList<SecurityEvent>>();
        collector.Seen += batches.Add;

        await collector.Round(None);

        // One question for the person's first ticket (none for the computer's own), to each DC, for that address and minute.
        var asked = logs.Asked.Where(a => a.Query.Contains("EventID=4769", StringComparison.Ordinal)).ToList();
        Assert.Equal(["dc01.amw.com", "dc02.amw.com"], asked.Select(a => a.Host));
        Assert.All(asked, a => Assert.Contains("Data[@Name='IpAddress']='::ffff:192.168.2.186'", a.Query));
        var fact = Assert.Single(batches.SelectMany(b => EventAlerts.FromSignIns(b, _ => null)));
        Assert.Equal("mwiles signed in from MEGAN-PC", fact.Title);
        Assert.Null(SecurityEvents.Tickets("' or '1'='1", at, at)); // only an address goes into the question
    }

    [Fact]
    public async Task An_operation_still_being_written_waits_for_the_next_read()
    {
        var folder = Folder();
        var clock = new FakeClock();
        var now = clock.GetUtcNow().UtcDateTime;
        var store = new AuditStore(folder, clock, NullLogger<AuditStore>.Instance);
        var logs = new FakeSecurityLog();
        logs.Logs["dc01.amw.com"] = [Value(1, now.AddMinutes(-1), "title", "Old", false, op: "{33333333-3333-3333-3333-333333333333}"),
            Value(2, now.AddMinutes(-1), "title", "New", true, op: "{33333333-3333-3333-3333-333333333333}"),
            Value(3, now.AddSeconds(-1), "department", "Sales", false)]; // its added value isn't written yet
        var collector = new AuditCollector(new FakeAuditDirectory { Login = new AuditLogin("amw.com", "svc", "pass", [new SyncDc("DC01", "dc01.amw.com", null, false)]) },
            logs, store, new SettingsStore(folder, new EphemeralDataProtectionProvider()), folder, clock, NullLogger<AuditCollector>.Instance);

        await collector.Round(None);
        var early = store.Since(DateTime.MinValue).Select(e => e.Attribute).ToList();
        logs.Logs["dc01.amw.com"].Add(Value(4, now.AddSeconds(-1), "department", "Sales and marketing", true));
        clock.Advance(TimeSpan.FromSeconds(30));
        await collector.Round(None);
        var department = Assert.Single(store.Since(DateTime.MinValue), e => e.Attribute == "department");

        Assert.Equal(["title"], early);
        Assert.Equal(["Sales"], department.Old);
        Assert.Equal(["Sales and marketing"], department.New);
    }

    [Fact]
    public async Task The_audit_and_History_show_events_with_metadata_for_the_rest()
    {
        var ad = new FakeLdap();
        using var app = new TestApp(data) { Ldap = ad };
        var tools = app.Services.GetRequiredService<DirectoryTools>();
        await tools.Connect(new SetupForm("amw.com", "svc-existing", "right", Existing: true, null, null, "DC=amw,DC=com"), "admin", None);
        var at = DateTime.UtcNow.AddMinutes(-1);
        var lab = "CN=Greg Sutton,OU=Lab,DC=amw,DC=com";
        ad.Add(lab, ("objectClass", "user"), ("objectCategory", "person"), ("sAMAccountName", "gsutton"), ("displayName", "Greg Sutton"), ("objectGUID", Guid.Parse(GregGuid).ToByteArray()),
            ("whenChanged", at.ToString("yyyyMMddHHmmss'.0Z'")), ("title", "Sales lead"), ("telephoneNumber", "555-0142"),
            ("msDS-ReplAttributeMetaData", Meta("title", at, "DC01")), ("msDS-ReplAttributeMetaData", Meta("telephoneNumber", at.AddMinutes(-30), "DC02")),
            ("msDS-ReplAttributeMetaData", Meta("lastLogonTimestamp", at, "DC01")));
        var store = app.Services.GetRequiredService<AuditStore>();
        store.Add(AuditEntries.From(Parse(
            Xml(5136, 1, at, ("OpCorrelationID", Op), ("SubjectUserName", "svc-existing"), ("SubjectDomainName", "AMW"), ("ObjectDN", lab), ("ObjectGUID", GregGuid),
                ("ObjectClass", "user"), ("AttributeLDAPDisplayName", "title"), ("AttributeValue", "Sales associate"), ("OperationType", "%%14675")),
            Xml(5136, 2, at, ("OpCorrelationID", Op), ("SubjectUserName", "svc-existing"), ("SubjectDomainName", "AMW"), ("ObjectDN", lab), ("ObjectGUID", GregGuid),
                ("ObjectClass", "user"), ("AttributeLDAPDisplayName", "title"), ("AttributeValue", "Sales lead"), ("OperationType", "%%14674")),
            // Before it moved to Lab, and someone else.
            Value(3, at.AddHours(-3), "department", "Sales", true, op: "{44444444-4444-4444-4444-444444444444}"),
            Value(4, at.AddHours(-2), "title", "x", true, op: "{55555555-5555-5555-5555-555555555555}", dn: "CN=Lena Ortiz,OU=IT,DC=amw,DC=com")
                .Replace(GregGuid, "66666666-6666-6666-6666-666666666666"))));
        app.Services.GetRequiredService<ActivityLog>().Record("admin", null, "users", "ad.edit", "Edited Greg Sutton", "ok");
        var admin = await app.SignedIn();

        var page = await admin.GetFromJsonAsync<JsonElement>("/api/ad/audit");
        var history = await admin.GetFromJsonAsync<JsonElement>($"/api/ad/history?dn={Uri.EscapeDataString(lab)}");
        var blank = await admin.GetAsync("/api/ad/history?dn=");

        var rows = page.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("dn").GetString() == lab).ToList();
        var title = Assert.Single(rows, r => r.GetProperty("attribute").GetString() == "title");
        Assert.Equal("admin", title.GetProperty("by").GetString()); // made in Rabbid Lab, by its service account
        Assert.True(title.GetProperty("app").GetBoolean());
        Assert.Equal("Sales associate", title.GetProperty("old")[0].GetString());
        Assert.Equal("Job title", title.GetProperty("label").GetString());
        var phone = Assert.Single(rows, r => r.GetProperty("attribute").GetString() == "telephoneNumber");
        Assert.Equal(JsonValueKind.Null, phone.GetProperty("old").ValueKind); // no event: not recorded
        Assert.Equal("555-0142", phone.GetProperty("new")[0].GetString());
        Assert.Equal("DC02", phone.GetProperty("dc").GetString());
        Assert.DoesNotContain(rows, r => r.GetProperty("attribute").GetString() == "lastLogonTimestamp");
        Assert.Equal(90, page.GetProperty("keep").GetInt32());
        Assert.True(page.GetProperty("account").GetBoolean()); // the logs are read as svc-existing
        var told = history.GetProperty("rows").EnumerateArray().Select(r => (r.GetProperty("attribute").GetString(), r.GetProperty("name").GetString())).ToList();
        Assert.Equal([("title", "Greg Sutton"), ("telephoneNumber", "Greg Sutton"), ("department", "Greg Sutton")], told);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    }

    [Fact]
    public async Task Only_someone_who_sets_up_the_service_account_changes_how_long_changes_are_kept()
    {
        using var app = new TestApp(data);
        var admin = await app.SignedIn();
        var role = (await (await admin.PostAsJsonAsync("/api/roles", new { name = "Viewer", color = "#5FD4A0" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        (await admin.PostAsJsonAsync($"/api/roles/{role}", new { name = "Viewer", color = "#5FD4A0", permissions = new[] { "ad.view" } })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "viewer1", password = "user password", roles = new[] { role } })).EnsureSuccessStatusCode();
        var viewer = await app.SignedIn("viewer1", "user password");

        var kept = await admin.PostAsJsonAsync("/api/ad/audit/keep", new { days = 365 });
        var odd = await admin.PostAsJsonAsync("/api/ad/audit/keep", new { days = 45 });
        var refused = await viewer.PostAsJsonAsync("/api/ad/audit/keep", new { days = 30 });

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(365, app.Services.GetRequiredService<SettingsStore>().Current.AuditDays);
        Assert.Equal(HttpStatusCode.BadRequest, odd.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains(app.Services.GetRequiredService<ActivityLog>().Recent(), e => e.Action == "ad.audit.keep" && e.Title.Contains("1 year"));
    }

    /// <summary>One msDS-ReplAttributeMetaData value: the attribute last changed then, on that DC.</summary>
    public static string Meta(string attribute, DateTime at, string dc) =>
        $"<DS_REPL_ATTR_META_DATA><pszAttributeName>{attribute}</pszAttributeName><dwVersion>2</dwVersion><ftimeLastOriginatingChange>{at:yyyy-MM-ddTHH:mm:ssZ}</ftimeLastOriginatingChange>"
        + $"<pszLastOriginatingDsaDN>CN=NTDS Settings,CN={dc},CN=Servers,CN=HQ,CN=Sites,CN=Configuration,DC=amw,DC=com</pszLastOriginatingDsaDN></DS_REPL_ATTR_META_DATA>";

    private DataFolder Folder() => DataFolder.Resolve(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = Path.Combine(data, Guid.NewGuid().ToString("N")) }).Build());
}

/// <summary>The audit's view of Active Directory: a sign-in and DCs to read, and display names.</summary>
public sealed class FakeAuditDirectory : IAuditDirectory
{
    public AuditLogin? Login { get; set; }

    public Dictionary<string, string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? ServiceAccount => Login?.User;

    public Task<AuditLogin?> AuditLogin(CancellationToken stop) => Task.FromResult(Login);

    public Task<Dictionary<string, string>> DisplayNames(IReadOnlyCollection<string> usernames, CancellationToken stop) =>
        Task.FromResult(usernames.Where(Names.ContainsKey).ToDictionary(u => u, u => Names[u], StringComparer.OrdinalIgnoreCase));
}

/// <summary>Each DC's Security log as XML, oldest first: what a read gets, past the record number it asks after.</summary>
public sealed partial class FakeSecurityLog : ISecurityLog
{
    public Dictionary<string, List<string>> Logs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, SecurityLogProblem> Fails { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Host, string Query)> Asked { get; } = [];

    public Task<IReadOnlyList<string>> Read(string host, AuditLogin login, string query, int most, CancellationToken stop)
    {
        lock (Asked)
        {
            Asked.Add((host, query));
        }
        if (Fails.TryGetValue(host, out var problem))
        {
            return Task.FromException<IReadOnlyList<string>>(problem);
        }
        if (query.Contains("EventRecordID<=", StringComparison.Ordinal))
        {
            return Task.FromResult<IReadOnlyList<string>>([]); // never cleared
        }
        var after = After().Match(query) is { Success: true } m ? long.Parse(m.Groups[1].Value) : 0;
        var ids = Ids().Matches(query).Select(x => x.Groups[1].Value).ToHashSet();
        IReadOnlyList<string> found = [.. (Logs.GetValueOrDefault(host) ?? []).Where(x => long.Parse(Record().Match(x).Groups[1].Value) > after
            && ids.Contains(EventId().Match(x).Groups[1].Value)).Take(most)];
        return Task.FromResult(found);
    }

    [GeneratedRegex(@"EventRecordID>(\d+)")]
    private static partial Regex After();

    [GeneratedRegex(@"EventID=(\d+)")]
    private static partial Regex Ids();

    [GeneratedRegex(@"<EventRecordID>(\d+)</EventRecordID>")]
    private static partial Regex Record();

    [GeneratedRegex(@"<EventID>(\d+)</EventID>")]
    private static partial Regex EventId();
}
