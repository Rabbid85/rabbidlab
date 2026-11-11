using System.Security.AccessControl;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>The data folder and the files kept in it: saved safely, read from a backup copy when broken, and closed to other accounts.</summary>
public sealed class DataFolderTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-data-" + Guid.NewGuid().ToString("N"));

    public DataFolderTests() => Directory.CreateDirectory(data);

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

    public sealed class Thing
    {
        public string Name { get; set; } = "";
    }

    private string Path_(string name) => Path.Combine(data, name);

    private JsonFile<Thing> File_(bool watch = false) => new(Path_("thing.json"), Json, null, watch);

    [Fact]
    public void Saving_keeps_the_copy_before_as_a_backup()
    {
        var file = File_();
        Assert.Equal("", file.Read().Name); // nothing there yet: a new one
        file.Save(new Thing { Name = "first" });
        file.Save(new Thing { Name = "second" });

        Assert.Equal("second", File_().Read().Name);
        Assert.Contains("first", File.ReadAllText(Path_("thing.json.bak")));
        Assert.False(File.Exists(Path_("thing.json.tmp")));
    }

    [Theory]
    [InlineData("{ \"name\": \"sec")]
    [InlineData("")]
    [InlineData("null")]
    public void A_broken_or_empty_file_is_read_from_its_backup_copy(string broken)
    {
        var file = File_();
        file.Save(new Thing { Name = "first" });
        file.Save(new Thing { Name = "second" });
        File.WriteAllText(Path_("thing.json"), broken);

        var again = File_();
        Assert.Equal("first", again.Read().Name);
        Assert.False(again.Broken);
        // The broken file never becomes the backup copy.
        again.Save(new Thing { Name = "third" });
        Assert.Equal("third", File_().Read().Name);
        Assert.Contains("first", File.ReadAllText(Path_("thing.json.bak")));
    }

    [Fact]
    public void A_missing_file_is_read_from_its_backup_copy()
    {
        var file = File_();
        file.Save(new Thing { Name = "first" });
        file.Save(new Thing { Name = "second" });
        File.Delete(Path_("thing.json"));

        Assert.Equal("first", File_().Read().Name);
    }

    [Fact]
    public void With_no_copy_that_can_be_read_it_starts_empty_and_says_it_is_broken()
    {
        File.WriteAllText(Path_("thing.json"), "{ broken");
        File.WriteAllText(Path_("thing.json.bak"), "not json either");

        var file = File_();
        Assert.Equal("", file.Read().Name);
        Assert.True(file.Broken);
        file.Save(new Thing { Name = "mended" });

        Assert.False(file.Broken);
        Assert.Equal("mended", File_().Read().Name);
        Assert.Equal("{ broken", File.ReadAllText(Path_("thing.json.broken"))); // kept for whoever wants to mend it
    }

    [Fact]
    public async Task A_watched_file_is_read_again_when_something_else_writes_it()
    {
        var file = File_(watch: true);
        file.Save(new Thing { Name = "first" });
        Assert.Equal("first", file.Read().Name);

        File.WriteAllText(Path_("thing.json"), """{"name":"written elsewhere"}""");
        File.SetLastWriteTimeUtc(Path_("thing.json"), DateTime.UtcNow.AddSeconds(5));
        await Task.Delay(1100); // it looks once a second at most

        Assert.Equal("written elsewhere", file.Read().Name);
    }

    [Fact]
    public void A_data_folder_that_was_already_there_is_closed_to_other_accounts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        // Made the way any folder is: with whatever the folder above lets in.
        File.WriteAllText(Path_("accounts.json"), "{}");
        Assert.False(new DirectoryInfo(data).GetAccessControl().AreAccessRulesProtected);

        using var app = new TestApp(data);
        var folder = app.Services.GetRequiredService<DataFolder>();

        var rules = new DirectoryInfo(folder.Path).GetAccessControl();
        Assert.True(rules.AreAccessRulesProtected); // nothing comes down from the folder above any more
        Assert.Equal("{}", File.ReadAllText(Path_("accounts.json"))); // and Rabbid Lab itself still gets in
        File.WriteAllText(Path_("new.txt"), "x");
    }

    [Fact]
    public void A_full_activity_log_is_set_aside_and_the_newest_entries_are_still_read()
    {
        using var app = new TestApp(data);
        var log = app.Services.GetRequiredService<ActivityLog>();
        log.RotateAt = 4000; // about 28 entries

        for (var i = 1; i <= 100; i++)
        {
            log.Record("admin", null, "test", "test.entry", $"Entry {i}", "ok");
        }
        var recent = log.Recent(25);

        Assert.True(File.Exists(Path_("activity.1.jsonl")));
        Assert.True(new FileInfo(Path_("activity.jsonl")).Length < 4400);
        Assert.Equal(Enumerable.Range(0, 25).Select(i => $"Entry {100 - i}"), recent.Select(e => e.Title)); // newest first, across both files
    }
}
