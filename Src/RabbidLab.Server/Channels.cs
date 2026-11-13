using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>
/// A notification: somewhere alerts go, by its name. Email through Gmail, a mail server or Microsoft 365; a Discord,
/// Teams or Slack webhook; an ntfy topic; or any webhook.
/// </summary>
public sealed class Channel
{
    public string Id { get; set; } = "";

    /// <summary>email, discord, teams, slack, ntfy or webhook.</summary>
    public string Kind { get; set; } = "webhook";

    /// <summary>What it's picked by, such as Anthony or Ops team.</summary>
    public string Name { get; set; } = "";

    /// <summary>The webhook or topic address, stored encrypted: anyone who has it can post there.</summary>
    public string Url { get; set; } = "";

    /// <summary>For email: how it sends and to whom.</summary>
    public MailSettings? Mail { get; set; }

    /// <summary>From before alert rules: whether new sensors started with it. The rules started from it once.</summary>
    public bool OnForNew { get; set; }
    public DateTime Added { get; set; }
    public string? AddedBy { get; set; }

    /// <summary>The last time something was sent to it, whether it went, and why not.</summary>
    public DateTime? LastTried { get; set; }
    public DateTime? LastSent { get; set; }
    public string? LastProblem { get; set; }
}

public sealed class ChannelsFile
{
    public List<Channel> Channels { get; set; } = [];

    /// <summary>From before email was a notification like the rest: whether new sensors started with email.</summary>
    public bool EmailOnForNew { get; set; } = true;

    /// <summary>The one mail server from mail.json became the notification called Email.</summary>
    public bool MailMoved { get; set; }
}

/// <summary>A notification as the page sends it. An empty Url, password or secret keeps the stored one.</summary>
public sealed record ChannelForm(string? Kind, string? Name, string? Url, bool? OnForNew = null, MailForm? Mail = null);

/// <summary>A test of a notification: a saved one by id, a new one as typed, or a saved one with what's typed over it.</summary>
public sealed record ChannelTestForm(string? Id, ChannelForm? Form);

/// <summary>Sends an alert to one webhook or topic. The answer is why it didn't go, or null when it did.</summary>
public interface IChannelSender
{
    Task<string?> Send(string kind, string url, MailNote note, CancellationToken stop);
}

/// <summary>The notifications in channels.json. Email moved in from mail.json, under its old id "email", so sensors kept it.</summary>
public sealed class Channels(DataFolder folder, SettingsStore secrets, IMailSender mail, IChannelSender sender, TimeProvider clock)
{
    public const string Email = "email";
    public static readonly string[] Kinds = ["email", "discord", "teams", "slack", "ntfy", "webhook"];
    private const int Most = 30;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<ChannelsFile> file = folder.Json<ChannelsFile>("channels.json", Json);

    public static readonly Dictionary<string, string> KindNames = new()
    {
        ["email"] = "Email", ["discord"] = "Discord", ["teams"] = "Teams", ["slack"] = "Slack", ["ntfy"] = "ntfy", ["webhook"] = "Webhook",
    };

    // The file, with the old mail server moved in the first time it's read.
    private ChannelsFile Read()
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (!f.MailMoved)
            {
                var old = folder.Json<MailSettings>("mail.json", Json).Read();
                if (global::RabbidLab.Server.Mail.Ready(old) && f.Channels.All(c => c.Id != Email))
                {
                    if (old.Provider == "smtp" && string.Equals(old.Host, "smtp.gmail.com", StringComparison.OrdinalIgnoreCase))
                    {
                        old.Provider = "gmail";
                    }
                    f.Channels.Insert(0, new Channel { Id = Email, Kind = "email", Name = "Email", Mail = old, OnForNew = f.EmailOnForNew, Added = clock.GetUtcNow().UtcDateTime });
                }
                f.MailMoved = true;
                file.Save(f);
            }
            return f;
        }
    }

    /// <summary>The notifications for the page, the address shortened so it isn't shown whole. uses: what sends to one.</summary>
    public object View(Func<string, (int Sensors, int Links)> uses)
    {
        List<Channel> list;
        lock (file.Gate)
        {
            list = [.. Read().Channels];
        }
        // Counted outside the lock: the sensors take theirs, then ask here.
        return new
        {
            channels = list.Select(c => (c, n: uses(c.Id))).Select(x => new
            {
                x.c.Id, x.c.Kind, name = Label(x.c), where = x.c.Kind == "email" ? MailWhere(x.c.Mail) : Where(secrets.TryDecrypt(x.c.Url)),
                mail = x.c.Mail is null ? null : global::RabbidLab.Server.Mail.View(x.c.Mail), ready = Ready(x.c),
                sensors = x.n.Sensors, links = x.n.Links, x.c.LastTried, x.c.LastSent, x.c.LastProblem,
            }).ToList(),
        };
    }

    /// <summary>The ones new sensors used to start with, for starting the alert rules.</summary>
    public List<string> Defaults()
    {
        lock (file.Gate)
        {
            return [.. Read().Channels.Where(c => c.OnForNew && Ready(c)).Select(c => c.Id)];
        }
    }

    /// <summary>Whether it's here.</summary>
    public bool Known(string? id)
    {
        lock (file.Gate)
        {
            return Read().Channels.Any(c => c.Id == id);
        }
    }

    /// <summary>Whether an alert would go anywhere: it's here, and an email one has what it needs to send.</summary>
    public bool Ready(string id)
    {
        lock (file.Gate)
        {
            return Read().Channels.FirstOrDefault(c => c.Id == id) is { } c && Ready(c);
        }
    }

    private static bool Ready(Channel c) => c.Kind != "email" || c.Mail is { } m && global::RabbidLab.Server.Mail.Ready(m);

    /// <summary>What it's called, as the history and the alert rules show it.</summary>
    public string Name(string id)
    {
        lock (file.Gate)
        {
            return Read().Channels.FirstOrDefault(c => c.Id == id) is { } c ? Label(c) : "A notification that's gone";
        }
    }

    /// <summary>Its kind, or null when it's gone.</summary>
    public string? KindOf(string id)
    {
        lock (file.Gate)
        {
            return Read().Channels.FirstOrDefault(c => c.Id == id)?.Kind;
        }
    }

    // Its name; one from before names had to be given is called by its kind.
    private static string Label(Channel c) => c.Name.Length > 0 ? c.Name : KindNames.GetValueOrDefault(c.Kind, "Notification");

    public (Channel? Channel, string? Problem) Add(ChannelForm form, string by)
    {
        lock (file.Gate)
        {
            var f = Read();
            if (f.Channels.Count >= Most)
            {
                return (null, $"That's {Most} notifications, which is as many as {Product.Title} keeps.");
            }
            var (made, problem) = Build(form, null, f.Channels);
            if (made is null)
            {
                return (null, problem);
            }
            made.Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            made.Added = clock.GetUtcNow().UtcDateTime;
            made.AddedBy = by;
            f.Channels.Add(made);
            file.Save(f);
            return (made, null);
        }
    }

    public string? Change(string id, ChannelForm form)
    {
        lock (file.Gate)
        {
            var f = Read();
            var at = f.Channels.FindIndex(c => c.Id == id);
            if (at < 0)
            {
                return "That notification isn't here any more.";
            }
            var (next, problem) = Build(form, f.Channels[at], f.Channels);
            if (next is null)
            {
                return problem;
            }
            f.Channels[at] = next;
            file.Save(f);
            return null;
        }
    }

    // A notification from the form: the kind it is (a saved one keeps its kind), a name of its own, and where it sends.
    private (Channel? Channel, string? Problem) Build(ChannelForm form, Channel? old, List<Channel> all)
    {
        var kind = old?.Kind ?? (Kinds.Contains(form.Kind) ? form.Kind! : "");
        if (kind.Length == 0)
        {
            return (null, "Pick what kind of notification it is.");
        }
        var name = form.Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            return (null, "Give it a name, such as Anthony, Ops team or Lab alerts. It's what sensors and links pick it by.");
        }
        if (name.Length > 40)
        {
            return (null, "Keep the name to 40 characters.");
        }
        if (all.Any(c => c.Id != old?.Id && string.Equals(Label(c), name, StringComparison.OrdinalIgnoreCase)))
        {
            return (null, $"There's a notification called {name} already. Give this one another name.");
        }
        var next = new Channel
        {
            Id = old?.Id ?? "", Kind = kind, Name = name, Url = old?.Url ?? "", Mail = old?.Mail, OnForNew = old?.OnForNew ?? false,
            Added = old?.Added ?? default, AddedBy = old?.AddedBy, LastTried = old?.LastTried, LastSent = old?.LastSent, LastProblem = old?.LastProblem,
        };
        if (kind == "email")
        {
            var (settings, problem) = global::RabbidLab.Server.Mail.Read(form.Mail ?? new MailForm(null, null, null, null, null, null, null, null, null, null, null), old?.Mail, secrets);
            if (settings is null)
            {
                return (null, problem);
            }
            next.Mail = settings;
            return (next, null);
        }
        if (old is null || !string.IsNullOrWhiteSpace(form.Url))
        {
            if (Check(kind, form.Url) is { } bad)
            {
                return (null, bad);
            }
            next.Url = secrets.Encrypt(form.Url!.Trim());
        }
        return (next, null);
    }

    /// <summary>Takes one away; the sensors and rules that used it stop sending to it.</summary>
    public bool Remove(string id)
    {
        lock (file.Gate)
        {
            var f = Read();
            if (f.Channels.RemoveAll(c => c.Id == id) == 0)
            {
                return false;
            }
            file.Save(f);
            return true;
        }
    }

    /// <summary>Sends an alert to one notification by id. The answer is why it didn't go, or null when it went.</summary>
    public async Task<string?> Send(string id, MailNote note, CancellationToken stop)
    {
        Channel? c;
        lock (file.Gate)
        {
            c = Read().Channels.FirstOrDefault(x => x.Id == id);
        }
        if (c is null)
        {
            return "That notification isn't here any more.";
        }
        var problem = await Post(c, note, stop);
        lock (file.Gate)
        {
            if (Read().Channels.FirstOrDefault(x => x.Id == id) is { } kept)
            {
                kept.LastTried = clock.GetUtcNow().UtcDateTime;
                kept.LastProblem = problem;
                kept.LastSent = problem is null ? kept.LastTried : kept.LastSent;
                file.Save(file.Read());
            }
        }
        return problem;
    }

    private Task<string?> Post(Channel c, MailNote note, CancellationToken stop) =>
        c.Kind == "email" ? mail.Send(c.Mail ?? new MailSettings(), note, stop)
        : secrets.TryDecrypt(c.Url) is { } url ? sender.Send(c.Kind, url, note, stop)
        : Task.FromResult<string?>("Its address can't be read here any more. Type it again.");

    /// <summary>A test message: to a saved notification, to one as typed, or to a saved one with what's typed over it.</summary>
    public async Task<string?> Test(ChannelTestForm form, CancellationToken stop)
    {
        var note = new MailNote($"Test from {Product.Title}", "note", [("What", "A test message. Alerts will look like this.")],
            new AlertCard("test", "Alerts work here", $"This notification gets alerts from {Product.Title}.",
                [new("🔔 What you'll get", ["A message when something goes down, and one when it's back", "Reminders while it stays down, if its alert rule asks for them"])],
                "Sent from Settings, Notifications"));
        Channel? old;
        List<Channel> all;
        lock (file.Gate)
        {
            all = [.. Read().Channels];
            old = form.Id is null ? null : all.FirstOrDefault(c => c.Id == form.Id);
        }
        if (form.Id is not null && old is null)
        {
            return "That notification isn't here any more.";
        }
        if (form.Form is null)
        {
            return await Send(old!.Id, note, stop);
        }
        // As typed: a name isn't needed to try it.
        var typed = form.Form with { Name = string.IsNullOrWhiteSpace(form.Form.Name) ? "Test" : form.Form.Name };
        var (made, problem) = Build(typed, old, [.. all.Where(c => c.Id == old?.Id)]);
        return made is null ? problem : await Post(made, note, stop);
    }

    /// <summary>Why an address won't do for the kind, or null when it will.</summary>
    public static string? Check(string kind, string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 500 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || u.Host.Length == 0)
        {
            return kind == "ntfy" ? "Type the topic's address, such as https://ntfy.sh/lab-alerts." : "Paste the webhook address. It starts with https://.";
        }
        return kind switch
        {
            "discord" when !u.Host.EndsWith("discord.com", StringComparison.OrdinalIgnoreCase) && !u.Host.EndsWith("discordapp.com", StringComparison.OrdinalIgnoreCase)
                || !u.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal)
                => "That isn't a Discord webhook address. In Discord: channel settings, Integrations, Webhooks, New Webhook, Copy Webhook URL.",
            "slack" when !u.Host.Equals("hooks.slack.com", StringComparison.OrdinalIgnoreCase)
                => "That isn't a Slack webhook address. It starts with https://hooks.slack.com/.",
            "teams" when u.Scheme != "https"
                => "Teams gives an address starting with https:// when you add the Workflows webhook to a channel.",
            "ntfy" when u.AbsolutePath.Trim('/').Length == 0
                => "Add the topic to the address, such as https://ntfy.sh/lab-alerts.",
            _ => null,
        };
    }

    // The address shortened for the page: the host and the last few characters.
    private static string Where(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? $"{u.Host} · …{url![^Math.Min(4, url.Length)..]}" : "Address can't be read";

    // How an email one sends, in a few words: from alerts@gmail.com to anthony@amw.com and 1 more.
    private static string MailWhere(MailSettings? m)
    {
        if (m is null)
        {
            return "Not set up";
        }
        var to = m.To.Count == 0 ? "nobody yet" : m.To.Count == 1 ? m.To[0] : $"{m.To[0]} and {m.To.Count - 1} more";
        return m.Provider switch
        {
            "m365" => $"Microsoft 365 · from {m.From} to {to}",
            "gmail" => $"from {m.From} to {to}",
            _ => $"{m.Host}, port {m.Port}, {(m.Security == "ssl" ? "SSL/TLS" : m.Security == "none" ? "no encryption" : "STARTTLS")} · to {to}",
        };
    }
}

/// <summary>Posts alerts the way each service wants them.</summary>
public sealed class ChannelSender : IChannelSender
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<string?> Send(string kind, string url, MailNote note, CancellationToken stop)
    {
        try
        {
            HttpResponseMessage first;
            if (kind == "ntfy")
            {
                first = await Ntfy(url, note, stop);
            }
            else if (kind == "discord" && note.Card is { } card)
            {
                // Discord's newer layout (components); if Discord refuses it, the same card as an embed.
                first = await Http.PostAsJsonAsync(url + (url.Contains('?') ? "&" : "?") + "with_components=true", Discord(card), stop);
                if (first.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    first.Dispose();
                    first = await Http.PostAsJsonAsync(url, Body(kind, note), stop);
                }
            }
            else
            {
                first = await Http.PostAsJsonAsync(url, Body(kind, note), stop);
            }
            using var answer = first;
            if (answer.IsSuccessStatusCode)
            {
                return null;
            }
            var said = (await answer.Content.ReadAsStringAsync(stop)).Trim();
            return $"It answered {(int) answer.StatusCode} {answer.ReasonPhrase}{(said.Length > 0 ? $": {(said.Length > 160 ? said[..160] + "…" : said)}" : "")}";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return e is TaskCanceledException ? "It didn't answer in 15 seconds." : $"It couldn't be reached: {e.Message}";
        }
    }

    private static int Colour(string tone) => tone switch { "down" => 0xFF6B7D, "up" => 0x3DD68C, "warn" => 0xF2BE6B, _ => 0xE8542F };

    // The card's colour by its banner: red down, green back up, amber warning, blue-violet for a test.
    private static int CardColour(string banner) => banner switch { "up" => 0x23A55A, "warn" => 0xF0B232, "test" => 0x7B7FFF, _ => 0xF23F43 };

    /// <summary>The banner for a state, as a picture on GitHub: the same for every lab, so it says nothing about this one.</summary>
    public static string Banner(string state) =>
        $"https://raw.githubusercontent.com/Rabbid85/rabbidlab/{(Product.Dev ? "dev" : "main")}/Src/RabbidLab.Server/wwwroot/Assets/alerts/{state}.png";

    // The card's line with its time: Discord shows <t:…:t> in each reader's own time zone, others get this server's.
    private static string Line(AlertCard c, bool discord) => c.When is not { } when ? c.Line.Replace(" {when}", "").Replace("{when}", "")
        : c.Line.Replace("{when}", discord ? $"<t:{new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Utc)).ToUnixTimeSeconds()}:t>"
            : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(when, DateTimeKind.Utc), TimeZoneInfo.Local).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture));

    // Names can have * or _ in them, which Discord and Slack would read as formatting.
    private static string Plain(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"([\\*_~`|>])", @"\$1");

    // The sections as text: a bold emoji heading, then bullets.
    private static string Sections(AlertCard c, string bold, string bullet) =>
        string.Join("\n\n", c.Sections.Where(s => s.Items.Count > 0).Select(s => $"{bold}{s.Heading}{bold}\n" + string.Join("\n", s.Items.Select(i => $"{bullet}{(bold.Length > 0 ? Plain(i) : i)}"))));

    /// <summary>The card in Discord's newer layout: a frame in the state's colour, the banner, the title and line, a divider, the sections and the footer.</summary>
    public static object Discord(AlertCard c)
    {
        var parts = new List<object>
        {
            new { type = 12, items = new[] { new { media = new { url = Banner(c.Banner) }, description = c.Title } } },
            new { type = 10, content = Cut($"## {Plain(c.Title)}\n{Line(c, true)}", 1500) },
        };
        if (c.Sections.Any(s => s.Items.Count > 0))
        {
            parts.Add(new { type = 14, divider = true, spacing = 1 });
            parts.Add(new { type = 10, content = Cut(Sections(c, "**", "- "), 2200) });
        }
        parts.Add(new { type = 10, content = Cut($"-# {Plain(c.Footer)}", 200) });
        return new { username = Product.Title, flags = 32768, components = new object[] { new { type = 17, accent_color = CardColour(c.Banner), components = parts } } };
    }

    // What each service takes: Discord an embed, Slack blocks, Teams an Adaptive Card, a webhook plain JSON. With a card, laid out as one.
    public static object Body(string kind, MailNote note)
    {
        if (note.Card is { } card && kind is "discord" or "slack" or "teams")
        {
            return Laid(kind, card);
        }
        var facts = note.Facts.Where(f => f.Value.Length > 0).ToList();
        var by = $"Sent by {Product.Title}";
        return kind switch
        {
            "discord" => new
            {
                username = Product.Title,
                embeds = new[]
                {
                    new
                    {
                        title = Cut(note.Subject, 250),
                        color = Colour(note.Tone),
                        fields = facts.Take(20).Select(f => new { name = Cut(f.Label, 250), value = Cut(f.Value, 1000), inline = false }).ToArray(),
                        footer = new { text = by },
                        timestamp = DateTime.UtcNow.ToString("o"),
                    },
                },
            },
            "slack" => new
            {
                text = note.Subject,
                blocks = new object[]
                {
                    new { type = "header", text = new { type = "plain_text", text = Cut(note.Subject, 150) } },
                    new { type = "section", fields = facts.Take(10).Select(f => new { type = "mrkdwn", text = Cut($"*{f.Label}*\n{f.Value}", 2000) }).ToArray() },
                    new { type = "context", elements = new[] { new { type = "mrkdwn", text = by } } },
                },
            },
            "teams" => new
            {
                type = "message",
                attachments = new[]
                {
                    new
                    {
                        contentType = "application/vnd.microsoft.card.adaptive",
                        content = new Dictionary<string, object>
                        {
                            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
                            ["type"] = "AdaptiveCard",
                            ["version"] = "1.4",
                            ["body"] = new object[]
                            {
                                new { type = "TextBlock", text = note.Subject, weight = "Bolder", size = "Medium", wrap = true },
                                new { type = "FactSet", facts = facts.Select(f => new { title = f.Label, value = f.Value }).ToArray() },
                                new { type = "TextBlock", text = by, isSubtle = true, size = "Small" },
                            },
                        },
                    },
                },
            },
            _ => new
            {
                @event = note.Tone,
                subject = note.Subject,
                facts = facts.GroupBy(f => f.Label).ToDictionary(g => g.Key, g => g.First().Value),
                sentBy = Product.Title,
                at = DateTime.UtcNow.ToString("o"),
                card = note.Card is not { } c ? null : new
                {
                    state = c.Banner, title = c.Title, line = Line(c, false), banner = Banner(c.Banner),
                    sections = c.Sections.Select(s => new { heading = s.Heading, items = s.Items }).ToList(), footer = c.Footer,
                },
            },
        };
    }

    // The card as Discord's embed (when its newer layout is refused), Slack's blocks or a Teams Adaptive Card.
    private static object Laid(string kind, AlertCard c)
    {
        var line = Line(c, kind == "discord");
        switch (kind)
        {
            case "discord":
                return new
                {
                    username = Product.Title,
                    embeds = new[]
                    {
                        new
                        {
                            title = Cut(c.Title, 250),
                            description = Cut($"{line}\n\n{Sections(c, "**", "- ")}".Trim(), 4000),
                            color = CardColour(c.Banner),
                            image = new { url = Banner(c.Banner) },
                            footer = new { text = Cut(c.Footer, 200) },
                        },
                    },
                };
            case "slack":
                var blocks = new List<object>
                {
                    new { type = "image", image_url = Banner(c.Banner), alt_text = c.Title },
                    new { type = "header", text = new { type = "plain_text", text = Cut(c.Title, 150) } },
                    new { type = "section", text = new { type = "mrkdwn", text = Cut(line, 2900) } },
                };
                if (c.Sections.Any(s => s.Items.Count > 0))
                {
                    blocks.Add(new { type = "divider" });
                    blocks.Add(new { type = "section", text = new { type = "mrkdwn", text = Cut(Sections(c, "*", "• "), 2900) } });
                }
                blocks.Add(new { type = "context", elements = new[] { new { type = "mrkdwn", text = Cut(c.Footer, 300) } } });
                return new { text = c.Title, blocks };
            default:
                var body = new List<object>
                {
                    new { type = "Image", url = Banner(c.Banner), size = "Stretch", altText = c.Title },
                    new { type = "TextBlock", text = c.Title, weight = "Bolder", size = "Large", wrap = true },
                    new { type = "TextBlock", text = line, wrap = true, spacing = "Small" },
                };
                var first = true;
                foreach (var s in c.Sections.Where(s => s.Items.Count > 0))
                {
                    body.Add(new { type = "TextBlock", text = s.Heading, weight = "Bolder", wrap = true, spacing = "Medium", separator = first });
                    body.Add(new { type = "TextBlock", text = string.Join("\r", s.Items.Select(i => $"- {i}")), wrap = true, spacing = "Small" });
                    first = false;
                }
                body.Add(new { type = "TextBlock", text = c.Footer, isSubtle = true, size = "Small", wrap = true, spacing = "Medium" });
                return new
                {
                    type = "message",
                    attachments = new[]
                    {
                        new
                        {
                            contentType = "application/vnd.microsoft.card.adaptive",
                            content = new Dictionary<string, object>
                            {
                                ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
                                ["type"] = "AdaptiveCard",
                                ["version"] = "1.4",
                                ["msteams"] = new { width = "Full" },
                                ["body"] = body,
                            },
                        },
                    },
                };
        }
    }

    // ntfy's JSON publishing: the topic in the body, so names in any language arrive whole. With a card, its banner comes along.
    private static Task<HttpResponseMessage> Ntfy(string url, MailNote note, CancellationToken stop)
    {
        var u = new Uri(url);
        var server = u.GetLeftPart(UriPartial.Authority);
        var c = note.Card;
        return Http.PostAsJsonAsync(server, new
        {
            topic = u.AbsolutePath.Trim('/'),
            title = c?.Title ?? note.Subject,
            message = c is null ? string.Join("\n", note.Facts.Where(f => f.Value.Length > 0).Select(f => $"{f.Label}: {f.Value}"))
                : $"{Line(c, false)}\n\n{Sections(c, "", "• ")}".Trim(),
            priority = note.Tone == "down" ? 4 : 3,
            tags = new[] { (c?.Banner ?? note.Tone) switch { "down" or "many" => "rotating_light", "up" => "white_check_mark", "warn" => "warning", _ => "bell" } },
            attach = c is null ? null : Banner(c.Banner),
            filename = c is null ? null : $"{c.Banner}.png",
        }, stop);
    }

    private static string Cut(string s, int most) => s.Length <= most ? s : s[..(most - 1)] + "…";
}

public static class ChannelsApi
{
    public static void MapChannelsApi(this WebApplication app)
    {
        // Seen by whoever may change alert settings.
        app.MapGet("/api/channels", (HttpContext context, Channels channels, Sensors sensors) =>
            !Access.Can(context, Permissions.SensorsAlerts) ? Access.NotAllowed() : Results.Ok(channels.View(sensors.Using)));

        app.MapPost("/api/channels", (ChannelForm form, HttpContext context, Channels channels, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var (made, problem) = channels.Add(form, ActivityLog.Who(context));
            if (made is null)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "channel.add", $"Added the notification {made.Name} ({Channels.KindNames[made.Kind]})");
            return Results.Ok(new { id = made.Id, now = channels.View(sensors.Using) });
        });

        app.MapPost("/api/channels/test", async (ChannelTestForm form, HttpContext context, Channels channels, CancellationToken stop) =>
            Access.RefuseUnless(context, Permissions.SensorsAlerts) ?? (await channels.Test(form, stop) is { } problem
                ? (IResult) Results.BadRequest(new { error = problem }) : Results.Ok(new { sent = true })));

        app.MapPost("/api/channels/{id}", (string id, ChannelForm form, HttpContext context, Channels channels, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            if (channels.Change(id, form) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "channel.change", $"Changed the notification {channels.Name(id)}");
            return Results.Ok(channels.View(sensors.Using));
        });

        app.MapPost("/api/channels/{id}/delete", (string id, HttpContext context, Channels channels, Alerts alerts, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var name = channels.Name(id);
            if (!channels.Remove(id))
            {
                return Results.NotFound(new { error = "That notification isn't here any more." });
            }
            alerts.Forget(id);
            log.Record(context, "sensors", "channel.delete", $"Deleted the notification {name}");
            return Results.Ok(channels.View(sensors.Using));
        });
    }
}
