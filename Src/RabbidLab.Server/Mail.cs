using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>How Rabbid Lab sends email and who gets its alerts, kept in mail.json. The password and client secret are stored encrypted.</summary>
public sealed class MailSettings
{
    /// <summary>smtp: any mail server, Gmail included. m365: Microsoft 365, through Microsoft Graph with an app registration.</summary>
    public string Provider { get; set; } = "smtp";
    public string? Host { get; set; }
    public int Port { get; set; } = 587;

    /// <summary>starttls, or none for a relay on the local network.</summary>
    public string Security { get; set; } = "starttls";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public List<string> To { get; set; } = [];
    public string? Tenant { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

/// <summary>The email settings as the page sends them. A password or secret left empty keeps the one that's stored.</summary>
public sealed record MailForm(string? Provider, string? Host, int? Port, string? Security, string? Username, string? Password, string? From,
    List<string>? To, string? Tenant, string? ClientId, string? ClientSecret);

/// <summary>
/// An alert to send: its subject, a tone for its colour (down, up, warn or note), and its facts as label and value. Card, when
/// there is one, is the same alert laid out for Discord, Slack, Teams and ntfy (ChannelSender).
/// </summary>
public sealed record MailNote(string Subject, string Tone, IReadOnlyList<(string Label, string Value)> Facts, AlertCard? Card = null);

/// <summary>
/// An alert laid out like release notes: a banner for its state (down, up, warn, test or many), a title, one line, short
/// sections with an emoji heading and bullets, and a footer. {when} in the line is When, which Discord shows in each
/// reader's own time zone.
/// </summary>
public sealed record AlertCard(string Banner, string Title, string Line, IReadOnlyList<AlertSection> Sections, string Footer, DateTime? When = null);

public sealed record AlertSection(string Heading, IReadOnlyList<string> Items);

public interface IMailSender
{
    /// <summary>Whether a mail server and at least one address to send to are set.</summary>
    bool Ready { get; }

    /// <summary>Sends to everyone on the alert list. The answer is why it couldn't, or null when it went.</summary>
    Task<string?> Send(MailNote note, CancellationToken stop);
}

public sealed partial class Mail(DataFolder folder, SettingsStore secrets, ILogger<Mail> log) : IMailSender
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HttpClient Graph = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly JsonFile<MailSettings> file = folder.Json<MailSettings>("mail.json", Json);

    public bool Ready
    {
        get
        {
            var s = file.Read();
            return s.To.Count > 0 && !string.IsNullOrEmpty(s.From) && (s.Provider == "m365"
                ? !string.IsNullOrEmpty(s.Tenant) && !string.IsNullOrEmpty(s.ClientId) && !string.IsNullOrEmpty(s.ClientSecret)
                : !string.IsNullOrEmpty(s.Host));
        }
    }

    /// <summary>The settings for the page: everything but the secrets, and whether each is stored.</summary>
    public object View()
    {
        var s = file.Read();
        return new { s.Provider, s.Host, s.Port, s.Security, s.Username, s.From, s.To, s.Tenant, s.ClientId,
            hasPassword = !string.IsNullOrEmpty(s.Password), hasSecret = !string.IsNullOrEmpty(s.ClientSecret), ready = Ready };
    }

    /// <summary>Saves the settings; the answer is why they can't be.</summary>
    public string? Save(MailForm form)
    {
        var provider = form.Provider == "m365" ? "m365" : "smtp";
        var from = form.From?.Trim() ?? "";
        if (!MailAddress.TryCreate(from, out _))
        {
            return "From has to be an email address, such as rabbidlab@example.com.";
        }
        var to = (form.To ?? []).Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (to.Count > 20 || to.Any(a => !MailAddress.TryCreate(a, out _)))
        {
            return to.Count > 20 ? "That's more than 20 addresses. Send to a group address instead." : $"{to.First(a => !MailAddress.TryCreate(a, out _))} isn't an email address.";
        }
        lock (file.Gate)
        {
            var old = file.Read();
            var next = new MailSettings { Provider = provider, From = from, To = to, Password = old.Password, ClientSecret = old.ClientSecret };
            if (provider == "m365")
            {
                next.Tenant = form.Tenant?.Trim();
                next.ClientId = form.ClientId?.Trim();
                if (next.Tenant is not { Length: > 0 } tenant || !TenantName().IsMatch(tenant) || !Guid.TryParse(next.ClientId, out _))
                {
                    return "Microsoft 365 needs the tenant (its ID, or a name such as contoso.onmicrosoft.com) and the application (client) ID of the app registration.";
                }
                if (!string.IsNullOrEmpty(form.ClientSecret))
                {
                    next.ClientSecret = secrets.Encrypt(form.ClientSecret);
                }
                if (string.IsNullOrEmpty(next.ClientSecret))
                {
                    return "Microsoft 365 needs the client secret of the app registration.";
                }
            }
            else
            {
                next.Host = form.Host?.Trim();
                next.Port = form.Port ?? 587;
                next.Security = form.Security == "none" ? "none" : "starttls";
                next.Username = string.IsNullOrWhiteSpace(form.Username) ? null : form.Username.Trim();
                if (next.Host is not { Length: > 0 and <= 200 } || next.Port is < 1 or > 65535)
                {
                    return "Type the mail server's name, such as smtp.gmail.com, and its port.";
                }
                if (next.Port == 465)
                {
                    return "Port 465 isn't supported. Mail servers that offer it offer 587 with STARTTLS too: use that.";
                }
                if (!string.IsNullOrEmpty(form.Password))
                {
                    next.Password = secrets.Encrypt(form.Password);
                }
                else if (next.Username is null)
                {
                    next.Password = null;
                }
            }
            file.Save(next);
            return null;
        }
    }

    public async Task<string?> Send(MailNote note, CancellationToken stop)
    {
        var s = file.Read();
        if (!Ready)
        {
            return "Email isn't set up yet.";
        }
        try
        {
            return s.Provider == "m365" ? await SendGraph(s, note, stop) : await SendSmtp(s, note, stop);
        }
        catch (Exception e) when (e is SmtpException or HttpRequestException or IOException or FormatException or InvalidOperationException
            || (e is OperationCanceledException && !stop.IsCancellationRequested))
        {
            log.LogWarning(e, "Email could not be sent");
            return Why(s, e);
        }
    }

    private async Task<string?> SendSmtp(MailSettings s, MailNote note, CancellationToken stop)
    {
        using var message = new MailMessage { From = new MailAddress(s.From!, Product.Title), Subject = note.Subject, Body = Text(note), BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(Html(note), Encoding.UTF8, "text/html"));
        foreach (var to in s.To)
        {
            message.To.Add(to);
        }
        using var client = new SmtpClient(s.Host!, s.Port) { EnableSsl = s.Security != "none", DeliveryMethod = SmtpDeliveryMethod.Network, Timeout = 20000 };
        if (s.Username is { Length: > 0 } user)
        {
            client.Credentials = new NetworkCredential(user, secrets.TryDecrypt(s.Password) ?? "");
        }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        await client.SendMailAsync(message, limit.Token);
        return null;
    }

    // Microsoft 365 without a mailbox password: an app registration with the Mail.Send application permission sends as the From mailbox.
    private async Task<string?> SendGraph(MailSettings s, MailNote note, CancellationToken stop)
    {
        using var asked = await Graph.PostAsync($"https://login.microsoftonline.com/{Uri.EscapeDataString(s.Tenant!)}/oauth2/v2.0/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = s.ClientId!,
            ["client_secret"] = secrets.TryDecrypt(s.ClientSecret) ?? "",
            ["scope"] = "https://graph.microsoft.com/.default",
            ["grant_type"] = "client_credentials",
        }), stop);
        var token = await asked.Content.ReadFromJsonAsync<JsonElement>(stop);
        if (!asked.IsSuccessStatusCode || !token.TryGetProperty("access_token", out var access))
        {
            var said = token.ValueKind == JsonValueKind.Object && token.TryGetProperty("error_description", out var d) ? d.GetString()?.Split('\n')[0].Trim() : null;
            return $"Microsoft 365 didn't sign the app in: {said ?? asked.StatusCode.ToString()}";
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(s.From!)}/sendMail")
        {
            Content = JsonContent.Create(new
            {
                message = new
                {
                    subject = note.Subject,
                    body = new { contentType = "HTML", content = Html(note) },
                    toRecipients = s.To.Select(a => new { emailAddress = new { address = a } }),
                },
                saveToSentItems = false,
            }),
        };
        request.Headers.Authorization = new("Bearer", access.GetString());
        using var sent = await Graph.SendAsync(request, stop);
        if (sent.IsSuccessStatusCode)
        {
            return null;
        }
        var error = await sent.Content.ReadFromJsonAsync<JsonElement>(stop);
        var message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m) ? m.GetString() : null;
        return sent.StatusCode == HttpStatusCode.Forbidden
            ? "Microsoft 365 refused: the app registration needs the Mail.Send application permission, with admin consent."
            : $"Microsoft 365 refused: {message ?? sent.StatusCode.ToString()}";
    }

    // What went wrong, in words that say what to do about it.
    private static string Why(MailSettings s, Exception e)
    {
        if (e is OperationCanceledException)
        {
            return $"{(s.Provider == "m365" ? "Microsoft 365" : s.Host)} didn't answer in time.";
        }
        var said = (e.InnerException?.Message ?? e.Message).Trim();
        if (e is SmtpException && (said.Contains("5.7.", StringComparison.Ordinal) || said.Contains("Authentication", StringComparison.OrdinalIgnoreCase)))
        {
            var gmail = s.Host?.Contains("gmail", StringComparison.OrdinalIgnoreCase) == true;
            return $"{s.Host} didn't accept the username and password." + (gmail ? " Gmail needs an app password (Google Account, Security, 2-Step Verification, App passwords), not your usual one." : "") + $" It said: {said}";
        }
        return $"{(s.Provider == "m365" ? "Microsoft 365" : s.Host)} couldn't be reached or refused the message: {said}";
    }

    private static string Text(MailNote note) =>
        note.Subject + "\r\n\r\n" + string.Join("\r\n", note.Facts.Select(f => $"{f.Label}: {f.Value}")) + $"\r\n\r\nSent by {Product.Title}.";

    private static string Html(MailNote note)
    {
        var (back, ink) = note.Tone switch { "down" => ("#FFE9EC", "#9B1C2E"), "up" => ("#E3F7EE", "#12683F"), "warn" => ("#FFF3D9", "#7A5200"), _ => ("#ECEEF6", "#1B1D29") };
        var rows = string.Concat(note.Facts.Select(f => $"<tr><td style=\"padding:4px 18px 4px 0;color:#6B7088;vertical-align:top\">{WebUtility.HtmlEncode(f.Label)}</td><td style=\"padding:4px 0\">{WebUtility.HtmlEncode(f.Value)}</td></tr>"));
        return "<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1B1D29;max-width:560px\">"
            + $"<div style=\"padding:14px 16px;border-radius:10px;background:{back};color:{ink};font-weight:700;font-size:16px\">{WebUtility.HtmlEncode(note.Subject)}</div>"
            + $"<table style=\"margin:16px 0;border-collapse:collapse;font-size:14px\">{rows}</table>"
            + $"<div style=\"color:#8A8FA8;font-size:12px\">Sent by {WebUtility.HtmlEncode(Product.Title)}.</div></div>";
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.-]{0,99}$")]
    private static partial Regex TenantName();
}

public static class MailApi
{
    public static void MapMailApi(this WebApplication app)
    {
        app.MapGet("/api/mail", (HttpContext context, Mail mail) =>
            !Access.Can(context, Permissions.SensorsAlerts) ? Access.NotAllowed() : Results.Ok(mail.View()));

        app.MapPost("/api/mail", (MailForm form, HttpContext context, Mail mail, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            if (mail.Save(form) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "mail.change", "Changed the email settings");
            return Results.Ok(mail.View());
        });

        app.MapPost("/api/mail/test", async (HttpContext context, Mail mail, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.SensorsAlerts) is { } refused)
            {
                return refused;
            }
            var note = new MailNote($"Test email from {Product.Title}", "note",
                [("Sent by", ActivityLog.Who(context)), ("Why", "Someone clicked Send a test email. If you can read this, alerts will reach you.")]);
            if (await mail.Send(note, context.RequestAborted) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "sensors", "mail.test", "Sent a test email");
            return Results.Ok(new { ok = true });
        });
    }
}
