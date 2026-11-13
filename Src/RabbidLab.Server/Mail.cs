using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using MailKit.Security;
using MimeKit;

namespace RabbidLab.Server;

/// <summary>
/// How an email notification sends: Gmail with an app password, any mail server, or Microsoft 365 through Graph. Kept in
/// the notification (Channels.cs). The password and client secret are stored encrypted.
/// </summary>
public sealed class MailSettings
{
    /// <summary>gmail, smtp (any mail server) or m365 (Microsoft Graph with an app registration).</summary>
    public string Provider { get; set; } = "smtp";
    public string? Host { get; set; }
    public int Port { get; set; } = 587;

    /// <summary>starttls, ssl (TLS from the start, port 465) or none for a relay on the local network.</summary>
    public string Security { get; set; } = "starttls";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public List<string> To { get; set; } = [];
    public string? Tenant { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

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
    /// <summary>Sends to the addresses in the settings. The answer is why it couldn't, or null when it went.</summary>
    Task<string?> Send(MailSettings settings, MailNote note, CancellationToken stop);
}

public sealed partial class Mail(SettingsStore secrets, ILogger<Mail> log) : IMailSender
{
    private static readonly HttpClient Graph = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Whether the settings can send: a sender, someone to send to, and a server or an app registration.</summary>
    public static bool Ready(MailSettings s) => s.To.Count > 0 && !string.IsNullOrEmpty(s.From) && (s.Provider == "m365"
        ? !string.IsNullOrEmpty(s.Tenant) && !string.IsNullOrEmpty(s.ClientId) && !string.IsNullOrEmpty(s.ClientSecret)
        : !string.IsNullOrEmpty(s.Host));

    /// <summary>The settings for the page: everything but the secrets, and whether each is stored.</summary>
    public static object View(MailSettings s) => new
    {
        s.Provider, s.Host, s.Port, s.Security, s.Username, s.From, s.To, s.Tenant, s.ClientId,
        hasPassword = !string.IsNullOrEmpty(s.Password), hasSecret = !string.IsNullOrEmpty(s.ClientSecret),
    };

    /// <summary>The settings from the form, the stored password and secret kept when left empty; or why they won't do.</summary>
    public static (MailSettings? Settings, string? Problem) Read(MailForm form, MailSettings? old, SettingsStore secrets)
    {
        var provider = form.Provider is "gmail" or "m365" ? form.Provider : "smtp";
        var to = (form.To ?? []).Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (to.Count == 0)
        {
            return (null, "Add at least one address under Send alerts to.");
        }
        if (to.Count > 20 || to.Any(a => !MailAddress.TryCreate(a, out _)))
        {
            return (null, to.Count > 20 ? "That's more than 20 addresses. Send to a group address instead." : $"{to.First(a => !MailAddress.TryCreate(a, out _))} isn't an email address.");
        }
        // A secret is kept only while it's for the same kind of sending.
        var same = old?.Provider == provider;
        var next = new MailSettings { Provider = provider, To = to, Password = same ? old!.Password : null, ClientSecret = same ? old!.ClientSecret : null };
        if (provider == "gmail")
        {
            var address = form.Username?.Trim() ?? "";
            if (!MailAddress.TryCreate(address, out _))
            {
                return (null, "Type the Gmail address alerts are sent from.");
            }
            (next.Host, next.Port, next.Security, next.Username, next.From) = ("smtp.gmail.com", 587, "starttls", address, address);
            if (!string.IsNullOrEmpty(form.Password))
            {
                next.Password = secrets.Encrypt(form.Password.Replace(" ", ""));
            }
            return string.IsNullOrEmpty(next.Password) ? (null, "Gmail needs an app password. Make one under Google Account, Security, 2-Step Verification, App passwords.") : (next, null);
        }
        next.From = form.From?.Trim() ?? "";
        if (!MailAddress.TryCreate(next.From, out _))
        {
            return (null, "From has to be an email address, such as rabbidlab@example.com.");
        }
        if (provider == "m365")
        {
            next.Tenant = form.Tenant?.Trim();
            next.ClientId = form.ClientId?.Trim();
            if (next.Tenant is not { Length: > 0 } tenant || !TenantName().IsMatch(tenant) || !Guid.TryParse(next.ClientId, out _))
            {
                return (null, "Microsoft 365 needs the tenant (its ID, or a name such as contoso.onmicrosoft.com) and the application (client) ID of the app registration.");
            }
            if (!string.IsNullOrEmpty(form.ClientSecret))
            {
                next.ClientSecret = secrets.Encrypt(form.ClientSecret);
            }
            return string.IsNullOrEmpty(next.ClientSecret) ? (null, "Microsoft 365 needs the client secret of the app registration.") : (next, null);
        }
        next.Host = form.Host?.Trim();
        next.Port = form.Port ?? 587;
        next.Security = form.Security is "ssl" or "none" ? form.Security : "starttls";
        next.Username = string.IsNullOrWhiteSpace(form.Username) ? null : form.Username.Trim();
        if (next.Host is not { Length: > 0 and <= 200 } || next.Port is < 1 or > 65535)
        {
            return (null, "Type the mail server's name, such as smtp.example.com, and its port.");
        }
        if (!string.IsNullOrEmpty(form.Password))
        {
            next.Password = secrets.Encrypt(form.Password);
        }
        else if (next.Username is null)
        {
            next.Password = null;
        }
        return (next, null);
    }

    public async Task<string?> Send(MailSettings s, MailNote note, CancellationToken stop)
    {
        if (!Ready(s))
        {
            return "Its mail settings aren't finished. Edit it and fill them in.";
        }
        try
        {
            return s.Provider == "m365" ? await SendGraph(s, note, stop) : await SendSmtp(s, note, stop);
        }
        catch (Exception e) when (e is MailKit.ServiceNotConnectedException or MailKit.ProtocolException or MailKit.CommandException or AuthenticationException
            or System.Net.Sockets.SocketException or HttpRequestException or IOException or FormatException or InvalidOperationException or ParseException
            || (e is OperationCanceledException && !stop.IsCancellationRequested))
        {
            log.LogWarning(e, "Email could not be sent");
            return Why(s, e);
        }
    }

    private async Task<string?> SendSmtp(MailSettings s, MailNote note, CancellationToken stop)
    {
        var message = new MimeMessage { Subject = note.Subject };
        message.From.Add(new MailboxAddress(Product.Title, s.From));
        message.To.AddRange(s.To.Select(a => MailboxAddress.Parse(a)));
        message.Body = new BodyBuilder { TextBody = Text(note), HtmlBody = Html(note) }.ToMessageBody();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = 20000 };
        var security = s.Security switch { "ssl" => SecureSocketOptions.SslOnConnect, "none" => SecureSocketOptions.None, _ => SecureSocketOptions.StartTls };
        await client.ConnectAsync(s.Host!, s.Port, security, limit.Token);
        if (s.Username is { Length: > 0 } user)
        {
            await client.AuthenticateAsync(user, secrets.TryDecrypt(s.Password) ?? "", limit.Token);
        }
        await client.SendAsync(new MailKit.SendRequest(message), limit.Token);
        await client.DisconnectAsync(true, limit.Token);
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
        var server = s.Provider == "m365" ? "Microsoft 365" : s.Provider == "gmail" ? "Gmail" : s.Host;
        if (e is OperationCanceledException)
        {
            return $"{server} didn't answer in time.";
        }
        var said = (e.InnerException?.Message ?? e.Message).Trim();
        if (e is AuthenticationException)
        {
            return $"{server} didn't accept the username and password." + (s.Provider == "gmail" ? " Gmail needs an app password, not your usual one." : "") + $" It said: {said}";
        }
        if (e is SslHandshakeException)
        {
            return $"{server} and {Product.Title} couldn't agree on encryption. Check the port: 587 is STARTTLS, 465 is SSL/TLS. It said: {said}";
        }
        return $"{server} couldn't be reached or refused the message: {said}";
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

/// <summary>An email notification's settings as the page sends them. A password or secret left empty keeps the one that's stored.</summary>
public sealed record MailForm(string? Provider, string? Host, int? Port, string? Security, string? Username, string? Password, string? From,
    List<string>? To, string? Tenant, string? ClientId, string? ClientSecret);
