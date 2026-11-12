using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>Somewhere alerts go besides email: a Discord, Teams or Slack webhook, an ntfy topic, or any webhook.</summary>
public sealed class Channel
{
    public string Id { get; set; } = "";

    /// <summary>discord, teams, slack, ntfy or webhook.</summary>
    public string Kind { get; set; } = "webhook";
    public string Name { get; set; } = "";

    /// <summary>The webhook or topic address, stored encrypted: anyone who has it can post to the channel.</summary>
    public string Url { get; set; } = "";

    /// <summary>Whether new sensors start with it ticked.</summary>
    public bool OnForNew { get; set; }
    public DateTime Added { get; set; }
    public string? AddedBy { get; set; }
}

public sealed class ChannelsFile
{
    public List<Channel> Channels { get; set; } = [];

    /// <summary>Whether new sensors start with email ticked.</summary>
    public bool EmailOnForNew { get; set; } = true;
}

/// <summary>A channel as the page sends it. An empty Url keeps the stored one.</summary>
public sealed record ChannelForm(string? Kind, string? Name, string? Url, bool? OnForNew);

/// <summary>Sends an alert to one channel. The answer is why it didn't go, or null when it did.</summary>
public interface IChannelSender
{
    Task<string?> Send(string kind, string url, MailNote note, CancellationToken stop);
}

/// <summary>The channels in channels.json, and email, which is the mail settings (Mail.cs) under the name "email".</summary>
public sealed class Channels(DataFolder folder, SettingsStore secrets, IMailSender mail, IChannelSender sender)
{
    public const string Email = "email";
    public static readonly string[] Kinds = ["discord", "teams", "slack", "ntfy", "webhook"];
    private const int Most = 20;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<ChannelsFile> file = folder.Json<ChannelsFile>("channels.json", Json);

    private static readonly Dictionary<string, string> KindNames = new()
    {
        ["discord"] = "Discord", ["teams"] = "Teams", ["slack"] = "Slack", ["ntfy"] = "ntfy", ["webhook"] = "Webhook",
    };

    /// <summary>The channels for the page, the address shortened so it isn't shown whole. uses: how many sensors use one.</summary>
    public object View(Func<string, int> uses)
    {
        var f = file.Read();
        return new
        {
            email = new { id = Email, ready = mail.Ready, onForNew = f.EmailOnForNew, uses = uses(Email) },
            channels = f.Channels.Select(c => new { c.Id, c.Kind, c.Name, where = Where(secrets.TryDecrypt(c.Url)), c.OnForNew, uses = uses(c.Id) }).ToList(),
        };
    }

    /// <summary>The channels a new sensor starts with.</summary>
    public List<string> Defaults()
    {
        var f = file.Read();
        return [.. (f.EmailOnForNew ? [Email] : Array.Empty<string>()), .. f.Channels.Where(c => c.OnForNew).Select(c => c.Id)];
    }

    /// <summary>Whether a sensor can send to it: email, or a channel that's here.</summary>
    public bool Known(string? id) => id == Email || file.Read().Channels.Any(c => c.Id == id);

    /// <summary>Whether an alert would go anywhere: email set up, or the channel here.</summary>
    public bool Ready(string id) => id == Email ? mail.Ready : file.Read().Channels.Any(c => c.Id == id);

    /// <summary>What a channel is called in a sensor's history: Email, or Discord #homelab.</summary>
    public string Name(string id) => id == Email ? "Email" : file.Read().Channels.FirstOrDefault(c => c.Id == id) is { } c ? $"{KindNames[c.Kind]} {c.Name}".Trim() : "A channel";

    public (Channel? Channel, string? Problem) Add(ChannelForm form, string by)
    {
        var kind = Kinds.Contains(form.Kind) ? form.Kind! : "";
        if (kind.Length == 0)
        {
            return (null, "Pick what kind of channel it is.");
        }
        if (Check(kind, form.Url) is { } bad)
        {
            return (null, bad);
        }
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Channels.Count >= Most)
            {
                return (null, $"That's {Most} channels, which is as many as {Product.Title} keeps.");
            }
            var channel = new Channel
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Kind = kind,
                Name = Named(form.Name, kind, form.Url!),
                Url = secrets.Encrypt(form.Url!.Trim()),
                OnForNew = form.OnForNew ?? true,
                Added = DateTime.UtcNow,
                AddedBy = by,
            };
            f.Channels.Add(channel);
            file.Save(f);
            return (channel, null);
        }
    }

    public string? Change(string id, ChannelForm form)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Channels.FirstOrDefault(c => c.Id == id) is not { } c)
            {
                return "That channel isn't here any more.";
            }
            if (!string.IsNullOrWhiteSpace(form.Url))
            {
                if (Check(c.Kind, form.Url) is { } bad)
                {
                    return bad;
                }
                c.Url = secrets.Encrypt(form.Url.Trim());
            }
            if (form.Name is not null)
            {
                c.Name = Named(form.Name, c.Kind, secrets.TryDecrypt(c.Url) ?? "");
            }
            if (form.OnForNew is { } on)
            {
                c.OnForNew = on;
            }
            file.Save(f);
            return null;
        }
    }

    /// <summary>Takes a channel away; the sensors that used it stop sending to it (Sensors drops unknown ids).</summary>
    public bool Remove(string id)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Channels.RemoveAll(c => c.Id == id) == 0)
            {
                return false;
            }
            file.Save(f);
            return true;
        }
    }

    public void EmailOnForNew(bool on)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            f.EmailOnForNew = on;
            file.Save(f);
        }
    }

    /// <summary>Sends an alert to one channel by id. The answer is why it didn't go, or null when it went.</summary>
    public Task<string?> Send(string id, MailNote note, CancellationToken stop)
    {
        if (id == Email)
        {
            return mail.Send(note, stop);
        }
        var c = file.Read().Channels.FirstOrDefault(x => x.Id == id);
        return c is null ? Task.FromResult<string?>("That channel isn't here any more.")
            : secrets.TryDecrypt(c.Url) is { } url ? sender.Send(c.Kind, url, note, stop)
            : Task.FromResult<string?>("Its address can't be read here any more. Type it again.");
    }

    /// <summary>A test message, to a channel being set up (form) or one that's saved (id).</summary>
    public async Task<string?> Test(string? id, ChannelForm? form, CancellationToken stop)
    {
        var note = new MailNote($"Test from {Product.Title}", "note", [("What", "A test message. Sensor alerts will look like this.")]);
        if (id is not null)
        {
            return Known(id) ? await Send(id, note, stop) : "That channel isn't here any more.";
        }
        var kind = Kinds.Contains(form?.Kind) ? form!.Kind! : "";
        return kind.Length == 0 ? "Pick what kind of channel it is." : Check(kind, form!.Url) ?? await sender.Send(kind, form.Url!.Trim(), note, stop);
    }

    /// <summary>Why an address won't do for the kind of channel, or null when it will.</summary>
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

    private static string Named(string? name, string kind, string url)
    {
        var typed = name?.Trim() ?? "";
        if (typed.Length > 0)
        {
            return typed.Length > 40 ? typed[..40] : typed;
        }
        // An ntfy channel is known by its topic; the others by their kind until someone names them.
        return kind == "ntfy" && Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.AbsolutePath.Trim('/') : "";
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
            using var answer = kind == "ntfy" ? await Ntfy(url, note, stop) : await Http.PostAsJsonAsync(url, Body(kind, note), stop);
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

    // What each service takes: Discord an embed, Slack blocks, Teams an Adaptive Card, a webhook plain JSON.
    public static object Body(string kind, MailNote note)
    {
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
            },
        };
    }

    // ntfy's JSON publishing: the topic in the body, so names in any language arrive whole.
    private static Task<HttpResponseMessage> Ntfy(string url, MailNote note, CancellationToken stop)
    {
        var u = new Uri(url);
        var server = u.GetLeftPart(UriPartial.Authority);
        return Http.PostAsJsonAsync(server, new
        {
            topic = u.AbsolutePath.Trim('/'),
            title = note.Subject,
            message = string.Join("\n", note.Facts.Where(f => f.Value.Length > 0).Select(f => $"{f.Label}: {f.Value}")),
            priority = note.Tone == "down" ? 4 : 3,
            tags = new[] { note.Tone switch { "down" => "rotating_light", "up" => "white_check_mark", "warn" => "warning", _ => "test_tube" } },
        }, stop);
    }

    private static string Cut(string s, int most) => s.Length <= most ? s : s[..(most - 1)] + "…";
}

public static class ChannelsApi
{
    public static void MapChannelsApi(this WebApplication app)
    {
        // Seen by whoever may change alert settings, as the mail settings are.
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
            log.Record(context, "sensors", "channel.add", $"Added the {made.Kind} channel {made.Name}".TrimEnd());
            return Results.Ok(new { id = made.Id, now = channels.View(sensors.Using) });
        });

        app.MapPost("/api/channels/email", (OnForNewForm form, HttpContext context, Channels channels, Sensors sensors) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            channels.EmailOnForNew(form.OnForNew);
            return Results.Ok(channels.View(sensors.Using));
        });

        app.MapPost("/api/channels/test", async (ChannelForm form, HttpContext context, Channels channels, CancellationToken stop) =>
            Access.RefuseUnless(context, Permissions.SensorsAlerts) ?? (await channels.Test(null, form, stop) is { } problem
                ? (IResult) Results.BadRequest(new { error = problem }) : Results.Ok(new { sent = true })));

        app.MapPost("/api/channels/{id}/test", async (string id, HttpContext context, Channels channels, CancellationToken stop) =>
            Access.RefuseUnless(context, Permissions.SensorsAlerts) ?? (await channels.Test(id, null, stop) is { } problem
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
            log.Record(context, "sensors", "channel.change", $"Changed the channel {channels.Name(id)}");
            return Results.Ok(channels.View(sensors.Using));
        });

        app.MapPost("/api/channels/{id}/delete", (string id, HttpContext context, Channels channels, Sensors sensors, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var name = channels.Name(id);
            if (!channels.Remove(id))
            {
                return Results.NotFound(new { error = "That channel isn't here any more." });
            }
            log.Record(context, "sensors", "channel.delete", $"Deleted the channel {name}");
            return Results.Ok(channels.View(sensors.Using));
        });
    }
}

public sealed record OnForNewForm(bool OnForNew);
