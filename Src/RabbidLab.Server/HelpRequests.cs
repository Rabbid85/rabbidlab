using System.Text;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>How Ask IT for help works on computers with the client (help-requests.json). Set under Settings, Help requests.</summary>
public sealed class HelpSettings
{
    /// <summary>Whether the client shows Ask IT for help, and the box with how to reach IT, under Details.</summary>
    public bool On { get; set; } = true;

    /// <summary>How to reach IT, shown to everyone above Ask IT for help. Empty: the box says only Need help?</summary>
    public string Text { get; set; } = "";

    /// <summary>The notifications (Channels) a request goes to.</summary>
    public List<string> To { get; set; } = [];

    /// <summary>Whether a request is written in Alert history too, so it isn't lost if nobody reads the email.</summary>
    public bool Alert { get; set; } = true;

    /// <summary>The subject, with {title}, {computer} and {user} filled in.</summary>
    public string Subject { get; set; } = HelpRequests.DefaultSubject;

    /// <summary>Pictures of the screens: all (every screen, the person can leave any out), ask (only if they tick it), never.</summary>
    public string Pictures { get; set; } = "all";

    /// <summary>Whether the computer's details (name, user, IP address, make and model, serial number, Windows) go with it.</summary>
    public bool Details { get; set; } = true;
}

public sealed record HelpSettingsForm(bool? On, string? Text, List<string>? To, bool? Alert, string? Subject, string? Pictures, bool? Details);

/// <summary>A picture of one screen, as the client sends it: a name and the PNG or JPEG as base64.</summary>
public sealed record HelpPicture(string? Name, string? Data);

/// <summary>What the computer says about itself, label and value, as the client shows them under Details.</summary>
public sealed record HelpFact(string? Label, string? Value);

/// <summary>Ask IT for help, from the client: the title (required), what they wrote, who, the pictures and the facts.</summary>
public sealed record HelpForm(string? Title, string? Detail, string? User, List<HelpPicture>? Pictures, List<HelpFact>? Facts);

/// <summary>
/// Ask IT for help from the Rabbid Lab client: its settings, and a request sent on through the notifications picked (an
/// email gets the pictures of the screens attached), written in Alert history when that's on. A computer sends ten an hour
/// at most, so a stuck button or a script can't flood the help desk.
/// </summary>
public sealed class HelpRequests(DataFolder folder, Alerts alerts, Channels channels, TimeProvider clock)
{
    public const string DefaultSubject = "Help request: {title} on {computer}";
    public const int TitleLength = 80;
    public const int DetailLength = 2000;
    public const int TextLength = 400;
    public const int SubjectLength = 160;
    public const int MostPictures = 6;
    public const int PictureBytes = 6 * 1024 * 1024;
    public const int FactLength = 200;
    public const int PerHour = 10;
    public static readonly string[] PictureModes = ["all", "ask", "never"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<HelpSettings> file = folder.Json<HelpSettings>("help-requests.json", Json);
    private readonly Dictionary<string, Queue<DateTimeOffset>> sent = [];

    public HelpSettings Read()
    {
        lock (file.Gate)
        {
            return file.Read();
        }
    }

    /// <summary>What the client needs to show it: on or off, how to reach IT, and whether it may send pictures.</summary>
    public object ForClient()
    {
        var s = Read();
        return new { on = s.On, text = s.Text, pictures = s.Pictures };
    }

    /// <summary>The settings for the page, with the notifications there are to pick from.</summary>
    public object View()
    {
        var s = Read();
        var all = channels.Picks().Select(c => new { id = c.Id, name = c.Name, kind = c.Kind, where = c.Where }).ToList();
        return new
        {
            on = s.On, text = s.Text, to = s.To.Where(channels.Known).ToList(), alert = s.Alert, subject = s.Subject, pictures = s.Pictures, details = s.Details,
            notifications = all, textLength = TextLength, subjectLength = SubjectLength,
        };
    }

    /// <summary>Saves what the page sent; null leaves that part as it is. Says what's wrong instead of saving.</summary>
    public string? Save(HelpSettingsForm form)
    {
        if (form.Text is { Length: > TextLength })
        {
            return $"How to reach IT is {TextLength} characters at most.";
        }
        if (form.Subject is { } subject && (string.IsNullOrWhiteSpace(subject) || subject.Length > SubjectLength))
        {
            return $"The subject needs some words, {SubjectLength} characters at most.";
        }
        if (form.Pictures is { } pictures && !PictureModes.Contains(pictures, StringComparer.Ordinal))
        {
            return "Pictures of the screens is all, ask or never.";
        }
        if (form.To is { } to && to.FirstOrDefault(id => !channels.Known(id)) is { } unknown)
        {
            return "That notification is gone. Open Help requests again and pick another.";
        }
        lock (file.Gate)
        {
            var s = file.Read();
            s.On = form.On ?? s.On;
            s.Text = form.Text?.Trim() ?? s.Text;
            s.To = form.To is { } list ? [.. list.Distinct()] : s.To;
            s.Alert = form.Alert ?? s.Alert;
            s.Subject = form.Subject?.Trim() ?? s.Subject;
            s.Pictures = form.Pictures ?? s.Pictures;
            s.Details = form.Details ?? s.Details;
            file.Save(s);
        }
        return null;
    }

    /// <summary>
    /// A request from a computer, sent on. Null when it went; otherwise why not, in words for the person who asked. The
    /// computer's own name comes from Devices, not from what it sends.
    /// </summary>
    public async Task<string?> Send(Client from, HelpForm form, CancellationToken stop)
    {
        var s = Read();
        if (!s.On)
        {
            return "Ask IT for help is turned off. Contact your IT team another way.";
        }
        var title = OneLine(form.Title, TitleLength);
        if (title.Length == 0)
        {
            return "Add a title so IT knows what it's about.";
        }
        if (s.To.Where(channels.Known).ToList() is not { Count: > 0 } to)
        {
            return "Your IT team hasn't set up where help requests go yet. Contact them another way.";
        }
        if (!Allowed(from.Id))
        {
            return "This computer has sent a lot of requests in the last hour. Try again later, or contact your IT team another way.";
        }
        var user = OneLine(form.User, Clients.MaxText) is { Length: > 0 } u ? u : from.User ?? "Someone";
        var detail = (form.Detail ?? "").Trim();
        detail = detail.Length > DetailLength ? detail[..DetailLength] : detail;
        var facts = new List<(string Label, string Value)> { ("Title", title), ("Asked by", user), ("Computer", from.Computer) };
        if (detail.Length > 0)
        {
            facts.Add(("What they wrote", detail));
        }
        if (s.Details)
        {
            foreach (var f in form.Facts ?? [])
            {
                if (OneLine(f.Label, 40) is { Length: > 0 } label && OneLine(f.Value, FactLength) is { Length: > 0 } value && label != "Computer name")
                {
                    facts.Add((label, value));
                }
            }
        }
        var files = s.Pictures == "never" ? [] : Pictures(form.Pictures);
        if (files.Count > 0)
        {
            facts.Add(("Pictures", files.Count == 1 ? "One screen, attached" : $"{files.Count} screens, attached"));
        }
        var note = new MailNote(Subject(s.Subject, title, from.Computer, user), "warn", facts, Files: files);
        var failed = new List<string>();
        if (s.Alert)
        {
            (_, failed) = await alerts.Send("help.request", note, to, stop);
        }
        else
        {
            foreach (var id in to)
            {
                if (await channels.Send(id, note, stop) is { } problem)
                {
                    failed.Add(problem);
                }
            }
        }
        return failed.Count == to.Count ? "Your request couldn't be sent to your IT team. Try again in a few minutes, or contact them another way." : null;
    }

    /// <summary>A test request, as one from this server's own name, to check it reaches the help desk.</summary>
    public async Task<string?> Test(string by, CancellationToken stop)
    {
        var s = Read();
        if (s.To.Where(channels.Known).ToList() is not { Count: > 0 } to)
        {
            return "Pick where help requests go first.";
        }
        var facts = new List<(string Label, string Value)> { ("Title", "A test help request"), ("Asked by", by), ("Computer", Environment.MachineName), ("What they wrote", "Sent from Settings, Help requests, to check it reaches you.") };
        var note = new MailNote(Subject(s.Subject, "A test help request", Environment.MachineName, by), "test", facts);
        var failed = new List<string>();
        foreach (var id in to)
        {
            if (await channels.Send(id, note, stop) is { } problem)
            {
                failed.Add($"{channels.Name(id)}: {problem}");
            }
        }
        return failed.Count == 0 ? null : string.Join(" · ", failed);
    }

    // Ten an hour for each computer.
    private bool Allowed(string id)
    {
        lock (sent)
        {
            var now = clock.GetUtcNow();
            var times = sent.TryGetValue(id, out var q) ? q : sent[id] = new Queue<DateTimeOffset>();
            while (times.Count > 0 && now - times.Peek() > TimeSpan.FromHours(1))
            {
                times.Dequeue();
            }
            if (times.Count >= PerHour)
            {
                return false;
            }
            times.Enqueue(now);
            return true;
        }
    }

    // The pictures that are what they say: PNG or JPEG, not too big, six at most.
    private static List<MailFile> Pictures(List<HelpPicture>? pictures)
    {
        var files = new List<MailFile>();
        foreach (var (p, i) in (pictures ?? []).Take(MostPictures).Select((p, i) => (p, i)))
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(p.Data ?? "");
            }
            catch (FormatException)
            {
                continue;
            }
            var png = bytes is [0x89, 0x50, 0x4E, 0x47, ..];
            var jpeg = bytes is [0xFF, 0xD8, 0xFF, ..];
            if (bytes.Length > PictureBytes || !(png || jpeg))
            {
                continue;
            }
            files.Add(new MailFile($"Screen {i + 1}.{(png ? "png" : "jpg")}", png ? "image/png" : "image/jpeg", bytes));
        }
        return files;
    }

    private static string Subject(string template, string title, string computer, string user)
    {
        var text = (string.IsNullOrWhiteSpace(template) ? DefaultSubject : template).Replace("{title}", title).Replace("{computer}", computer).Replace("{user}", user);
        return OneLine(text, 200);
    }

    // One line of plain text: no line breaks or control characters (a subject line can't be split), cut to length.
    private static string OneLine(string? text, int most)
    {
        var b = new StringBuilder();
        foreach (var ch in (text ?? "").Trim())
        {
            b.Append(char.IsControl(ch) ? ' ' : ch);
        }
        var s = b.ToString().Trim();
        return s.Length > most ? s[..most].TrimEnd() : s;
    }
}
