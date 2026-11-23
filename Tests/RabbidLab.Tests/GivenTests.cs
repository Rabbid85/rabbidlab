using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>The record of what Rabbid Lab gave each person, written from the changes the journal keeps.</summary>
public sealed class GivenTests : IDisposable
{
    private const string Lab = "OU=Lab,DC=amw,DC=com";
    private const string Jo = "CN=Jo Reyes," + Lab;
    private const string Sales = "CN=Sales," + Lab;
    private const string Vpn = "CN=VPN users," + Lab;
    private const string Guid = "0f8fad5b-d9cb-469f-a165-70867728950e";

    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-given-" + System.Guid.NewGuid().ToString("N"));
    private readonly TestApp app;
    private readonly ChangeJournal journal;
    private readonly GivenStore given;

    public GivenTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
        journal = app.Services.GetRequiredService<ChangeJournal>();
        given = app.Services.GetRequiredService<GivenStore>();
    }

    public void Dispose()
    {
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

    private Change Changed(ChangedObject on, string title, params ChangeStep[] steps) =>
        journal.Add(new Change { By = "awiles", Permission = Permissions.AdGroups, Object = on, Title = title, Steps = [.. steps] });

    private static ChangedObject User(string dn = Jo, string? guid = Guid) => new(dn, guid, "Jo Reyes", "user");

    // The undo holds the reverse of what was done: Remove is what was added, Add what was removed.
    private static ChangeStep Groups(string[] added, string[] removed) => new("Member of", [], [], new StepUndo("groups", Add: [.. removed], Remove: [.. added]));

    private static ChangeStep Members(string[] added, string[] removed) => new("Members", [], [], new StepUndo("members", Add: [.. removed], Remove: [.. added]));

    [Fact]
    public void An_account_made_and_groups_joined_are_given_and_a_group_left_is_taken_back()
    {
        Changed(User(), "Created the user jreyes", new ChangeStep("Account", ["None"], ["jreyes in Lab"]));
        Changed(User(), "Added jreyes to 2 groups", Groups([Sales, Vpn], []));
        Changed(User(), "Added jreyes to Sales again", Groups([Sales], [])); // already theirs: not given twice
        var took = Changed(User(), "Removed jreyes from VPN users", Groups([], [Vpn]));

        var entries = given.For(Jo);

        Assert.Equal(3, entries.Count);
        var account = Assert.Single(entries, e => e.Kind == "account");
        Assert.Equal("Account jreyes in Lab", account.What);
        Assert.Equal("Created the user jreyes", account.How);
        Assert.Equal("awiles", account.By);
        var sales = Assert.Single(entries, e => e.Key == Sales);
        Assert.Equal("Group Sales", sales.What);
        Assert.Null(sales.TookAt);
        var vpn = Assert.Single(entries, e => e.Key == Vpn);
        Assert.Equal(took.At, vpn.TookAt);
        Assert.Equal("Removed jreyes from VPN users", vpn.TookHow);
    }

    [Fact]
    public void Members_added_on_a_group_count_for_each_person_and_join_their_own_record_once_their_GUID_is_known()
    {
        var group = new ChangedObject(Sales, null, "Sales", "group");
        Changed(group, "Added 2 members to Sales", Members([Jo, "CN=Pat Primary," + Lab], []));
        Changed(User(), "Added jreyes to VPN users", Groups([Vpn], []));
        Changed(group, "Removed Pat Primary from Sales", Members([], ["CN=Pat Primary," + Lab]));

        Assert.Equal([Vpn, Sales], given.For(Jo).Select(e => e.Key)); // newest first, in one record
        Assert.All(given.For(Jo), e => Assert.Null(e.TookAt));
        Assert.NotNull(Assert.Single(given.For("CN=Pat Primary," + Lab)).TookAt);
    }

    [Fact]
    public void The_record_follows_a_move_and_everything_is_taken_back_when_the_account_is_deleted()
    {
        const string moved = "CN=Jo Reyes,OU=Other,DC=amw,DC=com";
        Changed(User(), "Added jreyes to Sales", Groups([Sales], []));
        Changed(User(moved), "Moved jreyes to Other", new ChangeStep("Location", ["Lab"], ["Other"], new StepUndo("place", Rdn: "CN=Jo Reyes", Parent: Lab, Now: [moved])));
        var there = given.For(moved);
        var left = given.For(Jo);
        Changed(User(moved), "Deleted the user jreyes", new ChangeStep("Location", ["Other"], ["Recycle Bin"], new StepUndo("restore", Rdn: "CN=Jo Reyes", Parent: "OU=Other,DC=amw,DC=com")));

        Assert.Single(there);
        Assert.Empty(left);
        var gone = Assert.Single(given.For(moved));
        Assert.NotNull(gone.TookAt);
        Assert.Equal("Deleted the user jreyes", gone.TookHow);
    }

    [Fact]
    public void A_change_to_anything_else_or_one_that_gives_nothing_leaves_no_record()
    {
        Changed(new ChangedObject("CN=PC1," + Lab, null, "PC1", "computer"), "Added PC1 to Sales", Groups([Sales], []));
        Changed(User(), "Disabled jreyes", new ChangeStep("Account", ["Enabled"], ["Disabled"], new StepUndo("bit", "userAccountControl", Bit: 2, On: false)));
        Changed(User(), "Removed jreyes from Sales", Groups([], [Sales])); // never given by Rabbid Lab: nothing to take back

        Assert.Empty(given.For(Jo));
        Assert.Empty(given.For("CN=PC1," + Lab));
        Assert.False(File.Exists(Path.Combine(data, "given.json")));
    }
}
