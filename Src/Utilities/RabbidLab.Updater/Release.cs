using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RabbidLab.Updater;

/// <summary>
/// Rabbid Lab's release key. Every release is signed with it on the publisher's own PC (Tools/ReleaseSign); the private
/// key is never on GitHub or on any machine Rabbid Lab runs on. Rabbid Lab installs an update only when its signature
/// verifies against this public key, so control of the GitHub account is not enough to make installs run anything. Setup
/// checks the same signature (SetupUpdate.ReleaseKey; a test checks the two keys match).
/// </summary>
public static class Release
{
    public const string ReleaseKey = """
        -----BEGIN PUBLIC KEY-----
        MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEA1ZEOG02KEfROevDV2KTC
        g32ipp/XU6HOeb4J1Q5CN92cQ4kt5v3nkcC/SMnI2Su/A6eHoD4behtrvHg8h4ix
        fPglN7IF62AyITgA9WbSrLXdwV5VuytSNoUkwMjllx94keCsTsYcGOR94PMilYEI
        vxVzu7CrTOTF0au0iwjyC8oW6vixKIIi0IFmt7a+Rrn3hWWDfQdqZW1qZZP4DgaG
        lh659xf23SxT/qZKMdjYiBNBUmp+PkUKjtaZVwCXjTKdYu0MmUnF/D9p7TFTFOWp
        ko5Tuw8+jsgx7KzUcLpcCwyOwWPRlj5daNmCAY39zYF8ul7XJKqnOPO0J42eolCW
        rp06ppgVem23bT6b7NiRGoAvJ5iZIbbPNz3bYBeB6EZsDlK5Ev1prxVSa6gpy0wS
        TsVEL1DQGHCnH7HjBrfRyq9rPCjk8Ag4TqmV6LfnFt1F3Z7TqlZN6aWItqVBOO8h
        qCXcEUpGT81AbtQsUvMJABpRyZBBMAkM0NDlFXfgYNPBAgMBAAE=
        -----END PUBLIC KEY-----
        """;

    /// <summary>
    /// What a release signature covers: the product, the version, and the installer's SHA-256 and size. The product is in
    /// it so that no other product's signature, made with the same key, could pass for a Rabbid Lab release.
    /// </summary>
    public static byte[] Message(string version, string sha256, long size, string product = "Rabbid Lab") =>
        Encoding.UTF8.GetBytes($"{product} release\n{version}\n{sha256.ToLowerInvariant()}\n{size.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>Whether a release signature is genuine for this version and installer.</summary>
    public static bool Verify(string publicKeyPem, string version, string sha256, long size, string? signature, string product = "Rabbid Lab")
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(Message(version, sha256, size, product), Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }
}
