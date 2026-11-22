using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>Someone who accepted the Use at your own risk notice: which wording (its fingerprint), its version, and when.</summary>
public sealed class RiskNoticeEntry
{
    public string Source { get; set; } = "";
    public string Username { get; set; } = "";
    public string Wording { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime At { get; set; }
}

public sealed class RiskNoticeFile
{
    public List<RiskNoticeEntry> People { get; set; } = [];
}

public sealed record RiskNoticeSection(string Heading, string Text);

/// <summary>Where a sign-in stands while its notice is open: what Finish logs once it's accepted.</summary>
public sealed record NoticeStep(string Title, string? Detail);

public sealed record NoticeForm(string? Ticket, bool Accept);

/// <summary>
/// The Use at your own risk notice. Its words are notice.txt, which Setup shows and installs too. Every account accepts it
/// before its first session starts (SignIn.Finish); declining starts none. It's asked again only when the words change.
/// Who accepted is kept in risk-notice.json in the data folder.
/// </summary>
public sealed partial class RiskNotice(DataFolder folder, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<RiskNoticeFile> file = folder.Json<RiskNoticeFile>("risk-notice.json", Json);

    public static string Title { get; }
    public static string Version { get; }
    public static IReadOnlyList<RiskNoticeSection> Sections { get; }

    /// <summary>A fingerprint of the words: any change to them asks everyone again.</summary>
    public static string Wording { get; }

    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex Blank();

    static RiskNotice()
    {
        using var stream = typeof(RiskNotice).Assembly.GetManifestResourceStream("notice.txt") ?? throw new InvalidOperationException("notice.txt is missing from the build.");
        using var reader = new StreamReader(stream);
        (Title, Version, Sections) = Parse(reader.ReadToEnd());
        var words = string.Join("\n", new[] { Title, Version }.Concat(Sections.Select(s => s.Heading + "\n" + s.Text)));
        Wording = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(words)))[..16].ToLowerInvariant();
    }

    /// <summary>The title and version on the first two lines, then a section per paragraph: its heading line, then its text.</summary>
    public static (string Title, string Version, IReadOnlyList<RiskNoticeSection> Sections) Parse(string text)
    {
        var blocks = Blank().Split(text.Replace("\r\n", "\n").Trim()).Select(b => b.Split('\n').Select(l => l.Trim()).ToArray()).ToArray();
        var sections = blocks.Skip(1).Where(b => b.Length > 1).Select(b => new RiskNoticeSection(b[0], string.Join(" ", b.Skip(1)))).ToList();
        return (blocks[0][0], blocks[0].Length > 1 ? blocks[0][1] : "", sections);
    }

    /// <summary>What the sign-in page shows: the title, the version, the sections, and about how long it takes to read.</summary>
    public static object Shape() => new
    {
        title = Title,
        version = Version,
        wording = Wording, // sent back with the sign-in once this browser accepts

        minutes = Math.Max(1, (int) Math.Round(Sections.Sum(s => s.Text.Split(' ').Length) / 200.0)),
        sections = Sections.Select(s => new { heading = s.Heading, text = s.Text }),
    };

    public bool Accepted(Who who)
    {
        lock (file.Gate)
        {
            return file.Read().People.Any(p => Same(p, who) && p.Wording == Wording);
        }
    }

    public void Accept(Who who)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            f.People.RemoveAll(p => Same(p, who));
            f.People.Add(new RiskNoticeEntry { Source = who.Source, Username = who.Username, Wording = Wording, Version = Version, At = clock.GetUtcNow().UtcDateTime });
            file.Save(f);
        }
    }

    private static bool Same(RiskNoticeEntry p, Who who) => p.Source == who.Source && string.Equals(p.Username, who.Username, StringComparison.OrdinalIgnoreCase);
}
