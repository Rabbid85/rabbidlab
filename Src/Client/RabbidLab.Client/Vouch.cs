using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace RabbidLab.Client;

/// <summary>
/// The server's identity, and taking a changed HTTPS key on its word. While this computer trusts the server, each
/// check-in hands it the public half of the server's identity key, kept in the registry. When the server's key no longer
/// matches, the client sends a random number and the server signs it together with the key it serves now. A good
/// signature from the identity kept here, over the key this computer was actually shown, means the same server made a new
/// certificate: the new key is kept and the client carries on. Nobody posing as the server can make that signature.
/// </summary>
internal static class Vouch
{
    private const string Value = "ServerIdentity"; // modulus:exponent, base64, beside the server's address

    /// <summary>Keeps the identity the server handed over while trusted. The service writes it; the tray can't, and needn't.</summary>
    public static void Keep(IdentityKey? identity)
    {
        if (identity is null || identity.Modulus.Length == 0 || identity.Exponent.Length == 0)
        {
            return;
        }
        var text = $"{identity.Modulus}:{identity.Exponent}";
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var installed = machine.OpenSubKey(Settings.InstalledKey, writable: true);
            if (installed is not null && installed.GetValue(Value) as string != text)
            {
                installed.SetValue(Value, text);
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Not the service: it keeps it at its next round.
        }
    }

    /// <summary>The identity kept here, or null before the server has handed one over.</summary>
    public static (string Modulus, string Exponent)? Kept()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var installed = machine.OpenSubKey(Settings.InstalledKey);
        return installed?.GetValue(Value) is string text && text.Split(':') is { Length: 2 } parts && parts[0].Length > 0 && parts[1].Length > 0
            ? (parts[0], parts[1]) : null;
    }

    /// <summary>
    /// Asks the server to vouch for the key it shows now. That key's fingerprint when the server proved it holds the
    /// identity kept here, or null: no identity yet, another identity, a key other than the one shown, or a bad signature.
    /// </summary>
    public static async Task<string?> Ask(string server, CancellationToken stop)
    {
        if (Kept() is not { } id)
        {
            return null;
        }
        var bytes = new byte[32];
        using (var random = RandomNumberGenerator.Create())
        {
            random.GetBytes(bytes);
        }
        var nonce = Convert.ToBase64String(bytes);
        using var api = new ServerApi(server, null, TimeSpan.FromSeconds(15), looking: true);
        var (_, answer) = await api.Post<VouchAnswer>("api/clients/vouch", new VouchRequest { Nonce = nonce }, null, stop).ConfigureAwait(false);
        if (answer is null || api.Shown is null || answer.Modulus != id.Modulus || answer.Exponent != id.Exponent
            || !string.Equals(answer.Tls, api.Shown, StringComparison.OrdinalIgnoreCase) || !Pin.Vouched(id.Modulus, id.Exponent, nonce, answer.Tls, answer.Sig))
        {
            return null;
        }
        return api.Shown;
    }
}
