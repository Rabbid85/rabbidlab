using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace RabbidLab.Server;

public sealed class IdentityFile
{
    /// <summary>The identity's private key as PKCS#8, encrypted with this server's data protection keys.</summary>
    public string Key { get; set; } = "";
    public DateTime Made { get; set; }
}

/// <summary>
/// This server's identity: a key of its own, apart from its HTTPS certificate, kept in identity.json with the rest of
/// its data. Clients learn its public half while they trust the server; when the certificate's key changes later, the
/// server signs the new key with this one and they take it without a visit to each computer (the client's Agent.cs).
/// </summary>
public sealed class ServerIdentity(DataFolder folder, IDataProtectionProvider protection)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<IdentityFile> file = folder.Json<IdentityFile>("identity.json", Json);
    private readonly IDataProtector protector = protection.CreateProtector("RabbidLab.ServerIdentity");
    private RSA? key;

    /// <summary>What a client signs over, so a signature for anything else is no use: the client's number and the key being vouched for.</summary>
    public static byte[] Message(string nonce, string tlsKey) => Encoding.UTF8.GetBytes($"rabbidlab-vouch|{nonce}|{tlsKey.ToUpperInvariant()}");

    private RSA Key()
    {
        lock (file.Gate)
        {
            if (key is not null)
            {
                return key;
            }
            var f = file.Read();
            var rsa = RSA.Create();
            if (f.Key.Length > 0)
            {
                rsa.ImportPkcs8PrivateKey(protector.Unprotect(Convert.FromBase64String(f.Key)), out _);
            }
            else
            {
                rsa.KeySize = 3072;
                file.Save(new IdentityFile { Key = Convert.ToBase64String(protector.Protect(rsa.ExportPkcs8PrivateKey())), Made = DateTime.UtcNow });
            }
            return key = rsa;
        }
    }

    /// <summary>The public half as clients keep it: modulus and exponent, base64.</summary>
    public (string Modulus, string Exponent) Public()
    {
        var p = Key().ExportParameters(false);
        return (Convert.ToBase64String(p.Modulus!), Convert.ToBase64String(p.Exponent!));
    }

    /// <summary>The signature over a client's number and the HTTPS key it was shown, with SHA-256 and PKCS#1 v1.5.</summary>
    public string Sign(string nonce, string tlsKey) =>
        Convert.ToBase64String(Key().SignData(Message(nonce, tlsKey), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
}
