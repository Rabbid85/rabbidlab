using System.Formats.Asn1;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RabbidLab.Server;

/// <summary>
/// Making the certificates for console addresses added in Settings: a certificate authority that can only sign for a
/// few names, what it signs, and a self-signed one for the names it can't sign for. All of them carry the public key of
/// the console's own certificate, so a client that knows the key's fingerprint knows the server at every address.
/// </summary>
public static class NameCerts
{
    private const string ServerAuth = "1.3.6.1.5.5.7.3.1";
    private const string NameConstraints = "2.5.29.30";

    /// <summary>The SHA-256 fingerprint of a certificate's public key (its SubjectPublicKeyInfo), as browsers show it.</summary>
    public static string KeyFingerprint(X509Certificate2 cert) => Convert.ToHexString(SHA256.HashData(cert.PublicKey.ExportSubjectPublicKeyInfo()));

    public static bool SameKey(X509Certificate2 a, X509Certificate2 b) =>
        a.PublicKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(b.PublicKey.ExportSubjectPublicKeyInfo());

    /// <summary>Whether a name is one of these, or under one of them: lab.amw.com is within amw.com, lab is not.</summary>
    public static bool Within(string name, IEnumerable<string> limits) =>
        limits.Any(l => name.Equals(l, StringComparison.OrdinalIgnoreCase) || name.EndsWith("." + l, StringComparison.OrdinalIgnoreCase));

    /// <summary>The DNS names in a certificate.</summary>
    public static List<string> Names(X509Certificate2 cert) =>
        [.. cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().SelectMany(e => e.EnumerateDnsNames())];

    /// <summary>
    /// A certificate authority for server certificates only, and only for these DNS names and the names under them
    /// (name constraints, which Windows, Chrome, Edge and Firefox enforce on a trusted root). No IP addresses.
    /// </summary>
    public static X509Certificate2 Authority(string name, RSA key, IReadOnlyList<string> limitedTo, DateTimeOffset from, DateTimeOffset until)
    {
        var subject = new X500DistinguishedNameBuilder();
        subject.AddCommonName(name);
        var request = new CertificateRequest(subject.Build(), key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuth)], false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(new X509Extension(NameConstraints, Limits(limitedTo), critical: true));
        using var made = request.CreateSelfSigned(from, until);
        return X509CertificateLoader.LoadCertificate(made.RawData); // without the key, which stays where it is
    }

    /// <summary>A server certificate for the names, with the console's own public key, signed by the authority.</summary>
    public static X509Certificate2 Sign(X509Certificate2 authority, RSA authorityKey, X509Certificate2 own, string name, IReadOnlyList<string> names, DateTimeOffset from, DateTimeOffset until)
    {
        var subject = new X500DistinguishedNameBuilder();
        subject.AddCommonName(name);
        var request = new CertificateRequest(subject.Build(), own.PublicKey, HashAlgorithmName.SHA256);
        Leaf(request, names, own);
        if (authority.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault() is { } id)
        {
            request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(id));
        }
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] = (byte) ((serial[0] & 0x7F) | 0x01); // a positive number
        return request.Create(authority.SubjectName, X509SignatureGenerator.CreateForRSA(authorityKey, RSASignaturePadding.Pkcs1), from, until, serial);
    }

    /// <summary>A self-signed server certificate for the names, made with the console's own key.</summary>
    public static X509Certificate2 SelfSigned(X509Certificate2 own, string name, IReadOnlyList<string> names, DateTimeOffset from, DateTimeOffset until)
    {
        var subject = new X500DistinguishedNameBuilder();
        subject.AddCommonName(name);
        using var rsa = own.GetRSAPrivateKey();
        using var ec = rsa is null ? own.GetECDsaPrivateKey() : null;
        var request = rsa is not null ? new CertificateRequest(subject.Build(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            : new CertificateRequest(subject.Build(), ec ?? throw new CryptographicException("The console's certificate has no RSA or ECDSA key."), HashAlgorithmName.SHA256);
        Leaf(request, names, own);
        using var made = request.CreateSelfSigned(from, until);
        return X509CertificateLoader.LoadCertificate(made.RawData);
    }

    /// <summary>The certificate with the console's own private key, to serve it with; null when it has another key.</summary>
    public static X509Certificate2? WithKey(X509Certificate2 cert, X509Certificate2 own)
    {
        if (!SameKey(cert, own))
        {
            return null;
        }
        using var rsa = own.GetRSAPrivateKey();
        if (rsa is not null)
        {
            return cert.CopyWithPrivateKey(rsa);
        }
        using var ec = own.GetECDsaPrivateKey();
        return ec is null ? null : cert.CopyWithPrivateKey(ec);
    }

    /// <summary>The DNS names an authority made here is limited to; none when it isn't limited.</summary>
    public static List<string> LimitedTo(X509Certificate2 authority)
    {
        var names = new List<string>();
        if (authority.Extensions[NameConstraints] is not { } found)
        {
            return names;
        }
        try
        {
            var all = new AsnReader(found.RawData, AsnEncodingRules.DER).ReadSequence();
            var permitted = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
            var dns = new Asn1Tag(TagClass.ContextSpecific, 2);
            if (!all.HasData || !all.PeekTag().HasSameClassAndValue(permitted))
            {
                return names;
            }
            var list = all.ReadSequence(permitted);
            while (list.HasData)
            {
                var one = list.ReadSequence();
                if (one.PeekTag().HasSameClassAndValue(dns))
                {
                    names.Add(one.ReadCharacterString(UniversalTagNumber.IA5String, dns));
                }
            }
        }
        catch (AsnContentException)
        {
            names.Clear(); // not one made here
        }
        return names;
    }

    // What every server certificate here has: its names, server authentication only, and that it's not an authority.
    private static void Leaf(CertificateRequest request, IReadOnlyList<string> names, X509Certificate2 own)
    {
        var san = new SubjectAlternativeNameBuilder();
        foreach (var name in names)
        {
            san.AddDnsName(name);
        }
        var rsa = own.PublicKey.Oid.Value == "1.2.840.113549.1.1.1";
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuth)], false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(rsa ? X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment : X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
    }

    // Name constraints: only these DNS names and the names under them, and no IP address at all.
    private static byte[] Limits(IReadOnlyList<string> dns)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                foreach (var name in dns)
                {
                    using (w.PushSequence())
                    {
                        w.WriteCharacterString(UniversalTagNumber.IA5String, name, new Asn1Tag(TagClass.ContextSpecific, 2));
                    }
                }
            }
            using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1)))
            {
                foreach (var everyAddress in new[] { new byte[8], new byte[32] }) // 0.0.0.0/0 and ::/0
                {
                    using (w.PushSequence())
                    {
                        w.WriteOctetString(everyAddress, new Asn1Tag(TagClass.ContextSpecific, 7));
                    }
                }
            }
        }
        return w.Encode();
    }
}

/// <summary>
/// The certificates for the added names, as they are now. Signed is from this install's certificate authority, for the
/// names it may sign for; Local is self-signed and trusted on this computer only, for the rest.
/// </summary>
public sealed record NamesCertificates(X509Certificate2? Authority, IReadOnlyList<string> LimitedTo, X509Certificate2? Signed,
    IReadOnlyList<string> SignedNames, X509Certificate2? Local, IReadOnlyList<string> LocalNames)
{
    public static readonly NamesCertificates None = new(null, [], null, [], null, []);
}

/// <summary>
/// The certificates for the added names. The console's own certificate never changes, because computers in Devices know
/// it: an added name gets another, which the console shows only to a browser that asks for that name (SNI).
/// </summary>
public interface INamesCertificate
{
    /// <summary>Whether the console is served over HTTPS, so a name needs a certificate.</summary>
    bool Https { get; }

    /// <summary>The certificate to show a browser that asked for this name, or null for the console's own.</summary>
    X509Certificate2? For(string? name);

    NamesCertificates Now { get; }

    /// <summary>Whether what's there is right for these names, and has time left.</summary>
    bool Good(IReadOnlyList<string> names);

    /// <summary>Makes the certificates for these names, keeping the ones that are still right; with no names they're taken away.</summary>
    NamesCertificates Make(IReadOnlyList<string> names);
}

/// <summary>
/// On Windows. In a domain, the names in that domain get one certificate signed by a certificate authority made for this
/// install, which can sign only for the domain and this computer's name: trust the authority once on another computer
/// and every such name works there, whatever is added later. Its key is in the computer's key store, and can't be
/// exported. The other names (short ones, other domains, no domain at all) get one self-signed certificate, trusted on
/// this computer. The certificates themselves are files in the data folder; their private key is the console's own.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StoreNamesCertificate : INamesCertificate
{
    public const string AuthorityFile = "console-ca.cer";
    public const string SignedFile = "console-names.cer";
    public const string LocalFile = "console-names-local.cer";

    /// <summary>The authority's key in the computer's key store. Setup deletes it by this name when uninstalling.</summary>
    public static string KeyName => $"{Product.Name} console addresses";
    private readonly string keyName;

    // What 1.1.149 and older called the one certificate they kept in the computer's store.
    private static string Friendly => $"{Product.Name} console addresses";
    private static readonly TimeSpan Early = TimeSpan.FromDays(60); // made again this long before they expire

    private readonly Func<X509Certificate2?> own;
    private readonly string folder;
    private readonly Func<string?> domain;
    private readonly bool machine;
    private readonly Action<X509Certificate2> trust;
    private readonly Action<string> distrust;
    private readonly TimeProvider clock;
    private readonly Lock gate = new();
    private volatile Held? held;

    // What's served: the certificates with their key, and which one each name gets.
    private sealed record Held(NamesCertificates Shown, Dictionary<string, X509Certificate2> ByName);

    public StoreNamesCertificate(IConfiguration config, string folder, Func<string?> domain)
        : this(() => ClientFiles.Certificate(config), folder, domain, machine: true, TrustHere, DistrustHere, TimeProvider.System)
    {
    }

    /// <summary>For tests: a key of the user's own, and trust that is only noted.</summary>
    public StoreNamesCertificate(Func<X509Certificate2?> own, string folder, Func<string?> domain, bool machine,
        Action<X509Certificate2> trust, Action<string> distrust, TimeProvider clock, string? keyName = null)
    {
        this.keyName = keyName ?? KeyName;
        (this.own, this.folder, this.domain, this.machine, this.trust, this.distrust, this.clock) = (own, folder, domain, machine, trust, distrust, clock);
    }

    public bool Https => own() is not null;

    public NamesCertificates Now => Current().Shown;

    public X509Certificate2? For(string? name) =>
        name is not null && Current().ByName.TryGetValue(name.TrimEnd('.'), out var cert) ? cert : null;

    public bool Good(IReadOnlyList<string> names)
    {
        lock (gate)
        {
            if (own() is not { } mine)
            {
                return true;
            }
            var now = Current().Shown;
            var limits = now.Authority is null ? Wanted() : now.LimitedTo;
            var signed = names.Where(n => NameCerts.Within(n, limits)).ToList();
            var at = clock.GetUtcNow();
            return Fits(now.Signed, signed, mine, at) && Fits(now.Local, [.. names.Except(signed)], mine, at) && !Older();
        }
    }

    public NamesCertificates Make(IReadOnlyList<string> names)
    {
        lock (gate)
        {
            if (own() is not { } mine)
            {
                held = Hold(NamesCertificates.None);
                return NamesCertificates.None;
            }
            var now = Current().Shown;
            var at = clock.GetUtcNow();
            var authority = now.Authority;
            RSA? key = null;
            try
            {
                // The authority there is, while its key is here and it has time left; otherwise a new one, in a domain.
                key = authority is null ? null : AuthorityKey(authority);
                if (key is null || authority!.NotAfter < at.Add(Early))
                {
                    key?.Dispose();
                    (key, authority) = (null, null);
                }
                var limits = authority is null ? Wanted() : NameCerts.LimitedTo(authority);
                List<string> signedNames = [.. names.Where(n => NameCerts.Within(n, limits))];
                List<string> localNames = [.. names.Except(signedNames)];

                var signed = now.Signed;
                if (signedNames.Count == 0)
                {
                    signed = null;
                    Delete(SignedFile);
                }
                else if (!Fits(signed, signedNames, mine, at) || (authority is not null && !signed!.IssuerName.RawData.AsSpan().SequenceEqual(authority.SubjectName.RawData)))
                {
                    if (authority is null)
                    {
                        (authority, key) = NewAuthority(limits, at, now.Authority);
                    }
                    var until = at.AddYears(2) < authority.NotAfter ? at.AddYears(2) : authority.NotAfter;
                    using var made = NameCerts.Sign(authority, key!, mine, "Console addresses", signedNames, at.AddDays(-1), until);
                    Save(SignedFile, made.RawData);
                    signed = NameCerts.WithKey(made, mine);
                }

                var local = now.Local;
                if (localNames.Count == 0 || !Fits(local, localNames, mine, at))
                {
                    if (local is not null)
                    {
                        distrust(local.Thumbprint);
                    }
                    local = null;
                    Delete(LocalFile);
                    if (localNames.Count > 0)
                    {
                        using var made = NameCerts.SelfSigned(mine, "Console addresses (this computer)", localNames, at.AddDays(-1), at.AddYears(5));
                        Save(LocalFile, made.RawData);
                        trust(made);
                        local = NameCerts.WithKey(made, mine);
                    }
                }
                RemoveOlder(mine);
                var shown = new NamesCertificates(authority ?? now.Authority, authority is null ? now.LimitedTo : limits, signed, signed is null ? [] : signedNames, local, local is null ? [] : localNames);
                held = Hold(shown);
                return shown;
            }
            finally
            {
                key?.Dispose();
            }
        }
    }

    private Held Current()
    {
        if (held is { } have)
        {
            return have;
        }
        lock (gate)
        {
            return held ??= Hold(Load());
        }
    }

    // From the data folder: each certificate that is still the console's key and has time left.
    private NamesCertificates Load()
    {
        if (own() is not { } mine)
        {
            return NamesCertificates.None;
        }
        var authority = Read(AuthorityFile);
        var signed = Pair(Read(SignedFile), mine);
        var local = Pair(Read(LocalFile), mine);
        return new NamesCertificates(authority, authority is null ? [] : NameCerts.LimitedTo(authority), signed, signed is null ? [] : NameCerts.Names(signed),
            local, local is null ? [] : NameCerts.Names(local));
    }

    private X509Certificate2? Pair(X509Certificate2? cert, X509Certificate2 mine)
    {
        try
        {
            return cert is null || cert.NotAfter < clock.GetLocalNow().DateTime ? null : NameCerts.WithKey(cert, mine);
        }
        catch (CryptographicException)
        {
            return null; // made again by whoever asks whether it's good
        }
    }

    private static Held Hold(NamesCertificates shown)
    {
        var byName = new Dictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase);
        foreach (var (cert, names) in new[] { (shown.Local, shown.LocalNames), (shown.Signed, shown.SignedNames) })
        {
            foreach (var name in names)
            {
                if (cert is not null)
                {
                    byName[name] = cert;
                }
            }
        }
        return new Held(shown, byName);
    }

    // Whether the certificate is for exactly these names, with the console's key and time left; none is right for no names.
    private static bool Fits(X509Certificate2? cert, List<string> names, X509Certificate2 mine, DateTimeOffset at) =>
        cert is null ? names.Count == 0
            : names.Count > 0 && NameCerts.SameKey(cert, mine) && cert.NotAfter > at.Add(Early).LocalDateTime
                && new HashSet<string>(NameCerts.Names(cert), StringComparer.OrdinalIgnoreCase).SetEquals(names);

    // What a new authority would be limited to: the domain and this computer's own name. None outside a domain, so no authority.
    private List<string> Wanted() => domain() is { Length: > 0 } d ? [d.ToLowerInvariant(), Environment.MachineName.ToLowerInvariant()] : [];

    // A new authority with a new key, trusted on this computer in place of the last one.
    private (X509Certificate2 Authority, RSA Key) NewAuthority(List<string> limits, DateTimeOffset at, X509Certificate2? last)
    {
        var options = machine ? CngKeyOpenOptions.MachineKey : CngKeyOpenOptions.None;
        if (CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, options))
        {
            using var stale = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, options);
            stale.Delete();
        }
        var creation = new CngKeyCreationParameters
        {
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            KeyCreationOptions = machine ? CngKeyCreationOptions.MachineKey : CngKeyCreationOptions.None,
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Signing,
        };
        creation.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(3072), CngPropertyOptions.None));
        var key = new RSACng(CngKey.Create(CngAlgorithm.Rsa, keyName, creation));
        try
        {
            var authority = NameCerts.Authority($"{Product.Name} certificate authority ({Environment.MachineName})", key, limits, at.AddDays(-1), at.AddYears(10));
            Save(AuthorityFile, authority.RawData);
            if (last is not null)
            {
                distrust(last.Thumbprint);
            }
            trust(authority);
            return (authority, key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    // The authority's key from the computer's key store, when it's the one in the certificate; null when it's gone.
    private RSA? AuthorityKey(X509Certificate2 authority)
    {
        var options = machine ? CngKeyOpenOptions.MachineKey : CngKeyOpenOptions.None;
        try
        {
            if (!CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, options))
            {
                return null;
            }
            var key = new RSACng(CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, options));
            if (key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(authority.PublicKey.ExportSubjectPublicKeyInfo()))
            {
                return key;
            }
            key.Dispose();
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    // 1.1.149 and older kept one self-signed certificate with a key of its own in the computer's store.
    private bool Older()
    {
        if (!machine)
        {
            return false;
        }
        using var my = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        my.Open(OpenFlags.ReadOnly);
        return my.Certificates.Any(x => x.FriendlyName == Friendly);
    }

    // Taken out, with its trust and its key. Never a key the console's own certificate has.
    private void RemoveOlder(X509Certificate2 mine)
    {
        if (!machine)
        {
            return;
        }
        using var my = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        my.Open(OpenFlags.ReadWrite);
        foreach (var old in my.Certificates.Where(x => x.FriendlyName == Friendly).ToList())
        {
            distrust(old.Thumbprint);
            if (!NameCerts.SameKey(old, mine))
            {
                DeletePrivateKey(old);
            }
            my.Remove(old);
        }
    }

    private static void DeletePrivateKey(X509Certificate2 cert)
    {
        try
        {
            using var key = cert.GetRSAPrivateKey();
            if (key is RSACng cng)
            {
                cng.Key.Delete();
            }
        }
        catch (CryptographicException)
        {
            // Already gone, or not readable: taking the certificate out of the store still stops it being used.
        }
    }

    private static void TrustHere(X509Certificate2 cert)
    {
        using var root = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        root.Open(OpenFlags.ReadWrite);
        root.Add(X509CertificateLoader.LoadCertificate(cert.RawData));
    }

    private static void DistrustHere(string thumbprint)
    {
        using var root = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        root.Open(OpenFlags.ReadWrite);
        foreach (var trusted in root.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false))
        {
            root.Remove(trusted);
        }
    }

    private X509Certificate2? Read(string name)
    {
        try
        {
            var path = Path.Combine(folder, name);
            return File.Exists(path) ? X509CertificateLoader.LoadCertificate(File.ReadAllBytes(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null;
        }
    }

    private void Save(string name, byte[] certificate)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path + ".tmp", certificate);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private void Delete(string name) => File.Delete(Path.Combine(folder, name));
}

/// <summary>Off Windows (Docker): no certificate store, so no certificate for added names.</summary>
public sealed class NoNamesCertificate : INamesCertificate
{
    public bool Https => false;

    public NamesCertificates Now => NamesCertificates.None;

    public X509Certificate2? For(string? name) => null;

    public bool Good(IReadOnlyList<string> names) => true;

    public NamesCertificates Make(IReadOnlyList<string> names) => NamesCertificates.None;
}
