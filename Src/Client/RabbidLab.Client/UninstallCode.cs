using System;
using System.Security.Cryptography;
using System.Text;

namespace RabbidLab.Client;

/// <summary>
/// The uninstall code: each computer has its own, made by the server and shown to admins in Devices. Typed on the
/// computer, it unlocks uninstalling without the server. The computer is only given a check for it (a salt and the
/// SHA-256 of salt and code), so nothing on the computer says what the code is. The server compiles this file in too.
/// </summary>
internal static class UninstallCode
{
    // No I, L, O or U: nothing that reads as another letter or digit.
    private const string Letters = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Length = 20; // 100 bits

    /// <summary>A new code, at random.</summary>
    public static string New()
    {
        var random = new byte[Length];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(random);
        }
        var code = new StringBuilder(Length);
        foreach (var b in random)
        {
            code.Append(Letters[b & 31]);
        }
        return code.ToString();
    }

    /// <summary>As it's shown and read out: four groups of five.</summary>
    public static string Show(string code) => code.Length != Length ? code
        : $"{code.Substring(0, 5)}-{code.Substring(5, 5)}-{code.Substring(10, 5)}-{code.Substring(15, 5)}";

    /// <summary>As typed, without spaces and dashes, in capitals, and with O, I and L read as the digits they look like.</summary>
    public static string Clean(string? typed)
    {
        var code = new StringBuilder();
        foreach (var ch in (typed ?? "").ToUpperInvariant())
        {
            if (ch != '-' && !char.IsWhiteSpace(ch))
            {
                code.Append(ch == 'O' ? '0' : ch is 'I' or 'L' ? '1' : ch);
            }
        }
        return code.ToString();
    }

    /// <summary>The check a computer is given for a code: "salt:hash", both in hex.</summary>
    public static string Check(string code)
    {
        var salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }
        var hex = Hex(salt);
        return $"{hex}:{Hash(hex, code)}";
    }

    /// <summary>Whether what was typed is the code this check was made from.</summary>
    public static bool Matches(string? typed, string? check)
    {
        var parts = (check ?? "").Split(':');
        var code = Clean(typed);
        if (parts.Length != 2 || parts[0].Length != 32 || parts[1].Length != 64 || code.Length != Length)
        {
            return false;
        }
        string mine;
        try
        {
            mine = Hash(parts[0], code);
        }
        catch (FormatException)
        {
            return false;
        }
        var differs = 0;
        for (var i = 0; i < mine.Length; i++)
        {
            differs |= mine[i] ^ char.ToUpperInvariant(parts[1][i]);
        }
        return differs == 0;
    }

    private static string Hash(string saltHex, string code)
    {
        var salt = new byte[saltHex.Length / 2];
        for (var i = 0; i < salt.Length; i++)
        {
            salt[i] = Convert.ToByte(saltHex.Substring(i * 2, 2), 16);
        }
        var text = Encoding.ASCII.GetBytes(code);
        var all = new byte[salt.Length + text.Length];
        Buffer.BlockCopy(salt, 0, all, 0, salt.Length);
        Buffer.BlockCopy(text, 0, all, salt.Length, text.Length);
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(all));
    }

    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");
}
