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
    /// The SHA-256 fingerprint of the certificate Rabbid Lab serves HTTPS with, as console.json picks it (a subject in a
    /// certificate store); null over plain HTTP or when it can't be found.
    /// </summary>
    public static string? Fingerprint(IConfiguration config)
    {
        lock (Gate)
        {
            // Read once: Rabbid Lab serves the certificate it started with until it restarts.
            if (!read)
            {
                fingerprint = Read(config);
                read = true;
            }
            return fingerprint;
        }
    }

    private static readonly Lock Gate = new();
    private static string? fingerprint;
    private static bool read;

    private static string? Read(IConfiguration config)
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
                    return Convert.ToHexString(SHA256.HashData(found.RawData));
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
