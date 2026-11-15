using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace RabbidLab.Client;

/// <summary>
/// Talking to the server over HTTPS. With a fingerprint from Group Policy or the installer, its certificate or that
/// certificate's key must be exactly that one (Pin; how a self-signed certificate is trusted); without one, a
/// certificate Windows trusts.
/// </summary>
internal sealed class ServerApi : IDisposable
{
    private readonly HttpClient http;

    /// <summary>The server answered with a certificate this computer doesn't accept.</summary>
    public bool Untrusted { get; private set; }

    /// <summary>The key fingerprint of the certificate the server showed and this computer accepted.</summary>
    public string? Key { get; private set; }

    public ServerApi(string server, string? fingerprint, TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            {
                var ok = fingerprint is null ? errors == SslPolicyErrors.None : certificate is not null && Pin.Matches(certificate.RawData, fingerprint);
                Untrusted = !ok;
                Key = ok && certificate is not null ? Pin.OfKey(certificate.RawData) : null;
                return ok;
            },
        };
        http = new HttpClient(handler) { BaseAddress = new Uri(server + "/"), Timeout = timeout ?? TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"RabbidLab-Client/{Agent.Version}");
    }

    /// <summary>Sends the body as JSON and reads the answer, or null when it isn't one.</summary>
    public async Task<(HttpStatusCode Code, T? Answer)> Post<T>(string path, object body, string? authorization, CancellationToken stop) where T : class
    {
        using var json = new MemoryStream();
        new DataContractJsonSerializer(body.GetType()).WriteObject(json, body);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(json.ToArray()) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }
        using var response = await http.SendAsync(request, stop).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return (response.StatusCode, null);
        }
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            return (response.StatusCode, (T?) new DataContractJsonSerializer(typeof(T)).ReadObject(stream));
        }
        catch (SerializationException)
        {
            return (response.StatusCode, null);
        }
    }

    /// <summary>Downloads a file to the path given.</summary>
    public async Task<bool> Download(string path, string to, CancellationToken stop)
    {
        using var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, stop).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }
        using var file = File.Create(to);
        await (await response.Content.ReadAsStreamAsync().ConfigureAwait(false)).CopyToAsync(file).ConfigureAwait(false);
        return true;
    }

    public void Dispose() => http.Dispose();
}

[DataContract]
internal sealed class JoinRequest
{
    [DataMember(Name = "computer")] public string Computer { get; set; } = "";
    [DataMember(Name = "os")] public string? Os { get; set; }
    [DataMember(Name = "version")] public string? Version { get; set; }
}

[DataContract]
internal sealed class JoinAnswer
{
    [DataMember(Name = "id")] public string Id { get; set; } = "";
    [DataMember(Name = "secret")] public string Secret { get; set; } = "";
    [DataMember(Name = "approved")] public bool Approved { get; set; }
    [DataMember(Name = "version")] public string? Version { get; set; }
}

[DataContract]
internal sealed class CheckInRequest
{
    [DataMember(Name = "computer")] public string Computer { get; set; } = "";
    [DataMember(Name = "os")] public string? Os { get; set; }
    [DataMember(Name = "version")] public string? Version { get; set; }
    [DataMember(Name = "user")] public string? User { get; set; }

    /// <summary>The hash in the uninstall code's check this computer holds, so the server knows whether its code works here.</summary>
    [DataMember(Name = "code", EmitDefaultValue = false)] public string? Code { get; set; }

    /// <summary>When the uninstall code was last typed on this computer, as ISO 8601.</summary>
    [DataMember(Name = "codeUsed", EmitDefaultValue = false)] public string? CodeUsed { get; set; }

    /// <summary>Errors waiting to be told (Problems.cs); left out when there are none.</summary>
    [DataMember(Name = "problems", EmitDefaultValue = false)] public System.Collections.Generic.List<ProblemReport>? Problems { get; set; }
}

[DataContract]
internal sealed class CheckInAnswer
{
    [DataMember(Name = "approved")] public bool Approved { get; set; }
    [DataMember(Name = "version")] public string? Version { get; set; }

    /// <summary>Until when the server allows uninstalling, as ISO 8601; null while it's locked.</summary>
    [DataMember(Name = "removalUntil")] public string? RemovalUntil { get; set; }

    /// <summary>The server ordered the client to uninstall itself.</summary>
    [DataMember(Name = "uninstall")] public bool Uninstall { get; set; }

    /// <summary>The check for this computer's uninstall code (UninstallCode): "salt:hash".</summary>
    [DataMember(Name = "code")] public string? Code { get; set; }

    /// <summary>After how many days without the server the uninstall lock lets go; 0 for never.</summary>
    [DataMember(Name = "unlockAfterDays")] public int? UnlockAfterDays { get; set; }
}
