using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RabbidLab.Server;

/// <summary>
/// What Rabbid Lab hands out for its clients: the client's MSI and the Group Policy template, from the Client folder
/// next to the program (Installer\build.ps1 puts them there), and the fingerprint of its own HTTPS certificate.
/// </summary>
public static class ClientFiles
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Client");

    public static string Msi => Path.Combine(Folder, "RabbidLab-Client.msi");

    public static string Policy => Path.Combine(Folder, "RabbidLab-Client-Policy.zip");

    /// <summary>
    /// The installer's file name as this server hands it out. Over HTTPS it has this server in it: its address, its port
    /// and the fingerprint of its key when it has one. The client installed from it reads them back (Settings.FromName
    /// there) and connects here by itself, with nothing more to click. Plain when the address can't go in a file name.
    /// </summary>
    public static string DownloadName(string version, string scheme, HostString host, string? key) =>
        scheme == "https" && host.HasValue && System.Text.RegularExpressions.Regex.IsMatch(host.Host, "^[A-Za-z0-9.-]{1,253}$")
            ? $"RabbidLab-Client-{version}@{host.Host.ToLowerInvariant()}@{host.Port ?? 443}{(key is null ? "" : $"@{key}")}.msi"
            : $"RabbidLab-Client-{version}.msi";

    /// <summary>The SHA-256 fingerprint of the certificate Rabbid Lab serves HTTPS with; null over plain HTTP or when it can't be found.</summary>
    public static string? Fingerprint(IConfiguration config) => Certificate(config) is { } cert ? Fingerprint(cert) : null;

    public static string Fingerprint(X509Certificate2 cert) => Convert.ToHexString(SHA256.HashData(cert.RawData));

    /// <summary>
    /// The SHA-256 fingerprint of that certificate's public key, which is what clients are given: the certificates for
    /// added console addresses have the same key (ConsoleCertificates.cs), so it is right at every address.
    /// </summary>
    public static string? KeyFingerprint(IConfiguration config) => Certificate(config) is { } cert ? NameCerts.KeyFingerprint(cert) : null;

    /// <summary>
    /// The certificate Rabbid Lab serves HTTPS with, as console.json picks it (a subject in a certificate store); null over
    /// plain HTTP or when it can't be found.
    /// </summary>
    public static X509Certificate2? Certificate(IConfiguration config)
    {
        lock (Gate)
        {
            // Read once: Rabbid Lab serves the certificate it started with until it restarts.
            if (!read)
            {
                certificate = Read(config);
                read = true;
            }
            return certificate;
        }
    }

    private static readonly Lock Gate = new();
    private static X509Certificate2? certificate;
    private static bool read;

    private static X509Certificate2? Read(IConfiguration config)
    {
        foreach (var endpoint in config.GetSection("Kestrel:Endpoints").GetChildren())
        {
            var c = endpoint.GetSection("Certificate");
            if (string.IsNullOrEmpty(c["Subject"]) || !OperatingSystem.IsWindows())
            {
                continue;
            }
            try
            {
                using var store = new X509Store(c["Store"] ?? "My", Enum.TryParse<StoreLocation>(c["Location"], true, out var where) ? where : StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                var found = store.Certificates.Find(X509FindType.FindBySubjectName, c["Subject"]!, false).Cast<X509Certificate2>()
                    .Where(x => x.HasPrivateKey && x.NotAfter > DateTime.Now).OrderByDescending(x => x.NotAfter).FirstOrDefault();
                if (found is not null)
                {
                    return found;
                }
            }
            catch (CryptographicException)
            {
                // Not one to read: the next endpoint, or none.
            }
        }
        return null;
    }
}
