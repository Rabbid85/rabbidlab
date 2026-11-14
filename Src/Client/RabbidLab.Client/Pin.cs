using System;
using System.Security.Cryptography;

namespace RabbidLab.Client;

/// <summary>
/// How the client knows its server: the SHA-256 fingerprint of the server's HTTPS certificate, or of that certificate's
/// public key. The key's is the one the server hands out: it stays the same when the server makes its certificate again,
/// and when it answers another of its addresses with another certificate.
/// </summary>
internal static class Pin
{
    public static string OfCertificate(byte[] certificate) => Hash(certificate);

    /// <summary>The fingerprint of the public key in a certificate (its SubjectPublicKeyInfo); null when it can't be read.</summary>
    public static string? OfKey(byte[] certificate) => PublicKeyInfo(certificate) is { } key ? Hash(key) : null;

    /// <summary>Whether the certificate, or its key, is the one with this fingerprint.</summary>
    public static bool Matches(byte[] certificate, string fingerprint) =>
        string.Equals(OfCertificate(certificate), fingerprint, StringComparison.OrdinalIgnoreCase)
        || string.Equals(OfKey(certificate), fingerprint, StringComparison.OrdinalIgnoreCase);

    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
    }

    // The public key is in the certificate's first part, after the version, serial number, algorithm, issuer, dates and subject.
    private static byte[]? PublicKeyInfo(byte[] der)
    {
        if (!Read(der, 0, out var at, out _) || !Read(der, at, out at, out var partEnd))
        {
            return null;
        }
        if (at < partEnd && der[at] == 0xA0 && !Read(der, at, out _, out at))
        {
            return null;
        }
        for (var skipped = 0; skipped < 5; skipped++)
        {
            if (!Read(der, at, out _, out at))
            {
                return null;
            }
        }
        if (at >= partEnd || der[at] != 0x30 || !Read(der, at, out _, out var end) || end > partEnd)
        {
            return null;
        }
        var key = new byte[end - at];
        Buffer.BlockCopy(der, at, key, 0, key.Length);
        return key;
    }

    // One DER value starting at 'from': where its content starts and where it ends. False when the bytes aren't DER.
    private static bool Read(byte[] der, int from, out int content, out int end)
    {
        content = end = 0;
        if (from < 0 || from + 2 > der.Length)
        {
            return false;
        }
        var at = from + 1;
        long length = der[at++];
        if (length >= 0x80)
        {
            var count = (int) length & 0x7F;
            if (count is 0 or > 4 || at + count > der.Length)
            {
                return false;
            }
            length = 0;
            for (var i = 0; i < count; i++)
            {
                length = (length << 8) | der[at++];
            }
        }
        if (at + length > der.Length)
        {
            return false;
        }
        content = at;
        end = at + (int) length;
        return true;
    }
}
