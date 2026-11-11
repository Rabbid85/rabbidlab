using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RabbidLab.Server;

/// <summary>
/// HTTPS to a device a sensor signs in to (an iDRAC, vCenter, a NAS). Credentials only go to a certificate Windows trusts,
/// or to the one pinned when the sensor was added; Check now (trying) reads any certificate so it can offer that pin.
/// </summary>
public sealed class DeviceHttp : IDisposable
{
    public DeviceHttp(string? pin, bool trying, TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, // a redirect could take the credentials somewhere else
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(6),
            SslOptions =
            {
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                {
                    Seen = certificate is null ? null : Fingerprint(certificate);
                    Trusted = errors == SslPolicyErrors.None;
                    return Trusted || (pin is not null ? Seen == pin : trying);
                },
            },
        };
        Http = new HttpClient(handler) { Timeout = timeout };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd($"RabbidLab/{Product.Version}");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        Pin = pin;
    }

    public HttpClient Http { get; }

    /// <summary>The fingerprint of the certificate the device sent, once it has.</summary>
    public string? Seen { get; private set; }

    /// <summary>Whether Windows trusts that certificate.</summary>
    public bool Trusted { get; private set; }

    private string? Pin { get; }

    /// <summary>The pin to offer in Check now: the certificate seen, when Windows doesn't trust it.</summary>
    public string? Offer => Trusted ? null : Seen;

    public static string Fingerprint(X509Certificate certificate) =>
        string.Join(':', Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())).Chunk(2).Select(c => new string(c)));

    /// <summary>The address to call: https:// in front of a host name, or the address as typed when it has its own scheme.</summary>
    public static string Root(string target) => target.Contains("://", StringComparison.Ordinal) ? target.TrimEnd('/') : $"https://{target}";

    /// <summary>What went wrong, in a few words, for the errors that have a plain answer; null for the rest.</summary>
    public string? Said(Exception e)
    {
        for (var inner = e; inner is not null; inner = inner.InnerException)
        {
            if (inner is AuthenticationException)
            {
                return Pin is not null && Seen is not null && Seen != Pin ? "The certificate changed. Open the sensor and run Check now to see the new one."
                    : "The certificate isn't trusted. Open the sensor and run Check now to trust it.";
            }
        }
        return e is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } ? "Sign-in failed: check the username and password"
            : e is HttpRequestException { StatusCode: { } code } ? $"HTTP {(int) code}" : null;
    }

    /// <summary>The certificate a device sends, for a sensor saved without Check now: null when Windows trusts it, so nothing is pinned.</summary>
    public static async Task<string?> Peek(string target, CancellationToken stop)
    {
        using var device = new DeviceHttp(null, trying: true, TimeSpan.FromSeconds(8));
        try
        {
            using var _ = await device.Http.GetAsync(Root(target), HttpCompletionOption.ResponseHeadersRead, stop);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            // Nothing to pin; the sensor says what's wrong when it's checked.
        }
        return device.Offer;
    }

    public void Dispose() => Http.Dispose();
}
