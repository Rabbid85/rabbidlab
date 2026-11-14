using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using QRCoder;

namespace RabbidLab.Server;

/// <summary>
/// The codes an authenticator app shows (TOTP, RFC 6238): six digits from a shared secret and the time, new every 30
/// seconds. SHA-1, as every authenticator app expects.
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    private const int Period = 30;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"; // base32, as the apps take a typed key

    /// <summary>A new secret: 160 random bits, in base32.</summary>
    public static string NewSecret() => Encode(RandomNumberGenerator.GetBytes(20));

    /// <summary>Which 30 seconds a moment is in.</summary>
    public static long Step(DateTimeOffset at) => at.ToUnixTimeSeconds() / Period;

    public static string Code(string secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
#pragma warning disable CA5350 // TOTP is HMAC-SHA-1 by its standard, and that use of SHA-1 isn't broken
        HMACSHA1.HashData(Decode(secret), counter, hash);
#pragma warning restore CA5350
        var at = hash[19] & 0xF;
        var number = (BinaryPrimitives.ReadInt32BigEndian(hash[at..]) & 0x7FFFFFFF) % 1_000_000;
        return number.ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The step a typed code is for, or null when it's wrong. The step before and the one after count too, for a phone
    /// whose clock is a little off and for a code typed as it changes.
    /// </summary>
    public static long? Match(string secret, string? code, DateTimeOffset now)
    {
        var typed = Encoding.ASCII.GetBytes([.. (code ?? "").Where(char.IsAsciiDigit)]);
        if (typed.Length != Digits)
        {
            return null;
        }
        long? found = null;
        var step = Step(now);
        // Every step is compared, whichever matches, so how long it takes says nothing.
        foreach (var s in new[] { step, step - 1, step + 1 })
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, s)), typed))
            {
                found ??= s;
            }
        }
        return found;
    }

    /// <summary>What the QR code holds: the app adds an entry named for the issuer and the account.</summary>
    public static string Uri(string issuer, string account, string secret) =>
        $"otpauth://totp/{System.Uri.EscapeDataString(issuer)}:{System.Uri.EscapeDataString(account)}?secret={secret}&issuer={System.Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={Period}";

    /// <summary>The text as a QR code, as an SVG picture: black on white, with the blank border scanners need.</summary>
    public static string Qr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var rows = data.ModuleMatrix;
        var size = rows.Count;
        var dark = new StringBuilder();
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (!rows[y][x])
                {
                    continue;
                }
                var from = x;
                while (x + 1 < size && rows[y][x + 1])
                {
                    x++;
                }
                dark.Append(CultureInfo.InvariantCulture, $"M{from} {y}h{x - from + 1}v1h-{x - from + 1}z");
            }
        }
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {size} {size}\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"QR code\">"
            + $"<path fill=\"#fff\" d=\"M0 0h{size}v{size}H0z\"/><path fill=\"#000\" d=\"{dark}\"/></svg>";
    }

    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                text.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
        {
            text.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }
        return text.ToString();
    }

    public static byte[] Decode(string secret)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in secret.ToUpperInvariant())
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0)
            {
                continue; // spaces and padding
            }
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte) ((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return [.. bytes];
    }
}
