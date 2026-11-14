using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using RabbidLab.Client;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>
/// The certificates for added console addresses: the certificate authority that can only sign for the domain, what it
/// signs, the self-signed one for the rest, and the client knowing the server by its key at every address.
/// </summary>
public sealed class ConsoleCertificatesTests : IDisposable
{
    private const X509ChainStatusFlags NameLimits = X509ChainStatusFlags.HasNotPermittedNameConstraint | X509ChainStatusFlags.HasExcludedNameConstraint
        | X509ChainStatusFlags.InvalidNameConstraints | X509ChainStatusFlags.HasNotDefinedNameConstraint | X509ChainStatusFlags.HasNotSupportedNameConstraint;

    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-certs-" + Guid.NewGuid().ToString("N"));
    private readonly string keyName = "rabbidlab-test-" + Guid.NewGuid().ToString("N");
    private readonly X509Certificate2 own = Own("CN=Rabbid Lab test", "localhost");
    private readonly List<string> trusted = [];

    public ConsoleCertificatesTests() => Directory.CreateDirectory(data);

    public void Dispose()
    {
        own.Dispose();
        if (CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider))
        {
            using var key = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
            key.Delete();
        }
        try
        {
            Directory.Delete(data, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temp folder's own cleanup.
        }
    }

    // The store as the console uses it, but with a key of this user's and trust that is only noted.
    private StoreNamesCertificate Store(string? domain = "amw.com", TimeProvider? clock = null) =>
        new(() => own, data, () => domain, machine: false, cert => trusted.Add(cert.Thumbprint), thumbprint => trusted.Remove(thumbprint), clock ?? TimeProvider.System, keyName);

    [Fact]
    public void In_a_domain_the_full_names_are_signed_by_an_authority_and_the_short_ones_are_self_signed()
    {
        var store = Store();

        var made = store.Make(["lab", "lab.amw.com"]);

        Assert.Equal(["lab.amw.com"], made.SignedNames);
        Assert.Equal(["lab"], made.LocalNames);
        Assert.Equal(["amw.com", Environment.MachineName.ToLowerInvariant()], made.LimitedTo);
        Assert.Equal(made.Authority!.SubjectName.RawData, made.Signed!.IssuerName.RawData);
        Assert.Equal(X509ChainStatusFlags.NoError, Check(made.Signed, made.Authority));
        Assert.Equal([made.Authority.Thumbprint, made.Local!.Thumbprint], trusted);
        Assert.False(made.Authority.HasPrivateKey);
        Assert.True(store.Good(["lab", "lab.amw.com"]));
        Assert.False(store.Good(["lab", "lab.amw.com", "monitor.amw.com"]));
    }

    [Fact]
    public void Every_certificate_has_the_consoles_own_key_so_one_fingerprint_is_right_at_every_address()
    {
        var store = Store();
        store.Make(["lab", "lab.amw.com"]);
        var key = NameCerts.KeyFingerprint(own);

        var full = store.For("lab.amw.com")!;
        var bare = store.For("LAB")!;

        Assert.Equal(key, NameCerts.KeyFingerprint(full));
        Assert.Equal(key, NameCerts.KeyFingerprint(bare));
        Assert.True(full.HasPrivateKey && bare.HasPrivateKey);
        Assert.NotEqual(own.Thumbprint, full.Thumbprint);
        Assert.Null(store.For("localhost"));
        Assert.Null(store.For("other.amw.com"));
        // The client: the key's fingerprint, or the fingerprint of the very certificate it's shown, and nothing else.
        Assert.True(Pin.Matches(full.RawData, key));
        Assert.True(Pin.Matches(bare.RawData, key));
        Assert.True(Pin.Matches(own.RawData, key));
        Assert.True(Pin.Matches(own.RawData, ClientFiles.Fingerprint(own)));
        Assert.False(Pin.Matches(full.RawData, ClientFiles.Fingerprint(own)));
        using var stranger = Own("CN=Console addresses", "lab.amw.com");
        Assert.False(Pin.Matches(stranger.RawData, key));
        Assert.False(Pin.Matches([0x30, 0x03, 0x02, 0x01, 0x00], key));
    }

    [Fact]
    public void Adding_a_name_keeps_the_authority_and_the_certificates_that_are_still_right()
    {
        var store = Store();
        var first = store.Make(["lab", "lab.amw.com"]);

        var second = store.Make(["lab", "lab.amw.com", "monitor.amw.com"]);
        var third = store.Make(["lab", "lab.amw.com", "monitor.amw.com"]);

        Assert.Equal(first.Authority!.Thumbprint, second.Authority!.Thumbprint);
        Assert.Equal(["lab.amw.com", "monitor.amw.com"], second.SignedNames);
        Assert.NotEqual(first.Signed!.Thumbprint, second.Signed!.Thumbprint);
        Assert.Equal(first.Local!.Thumbprint, second.Local!.Thumbprint);
        Assert.Equal(second.Signed.Thumbprint, third.Signed!.Thumbprint);
        Assert.Equal(X509ChainStatusFlags.NoError, Check(second.Signed, first.Authority));
    }

    [Fact]
    public void What_it_made_is_there_after_a_restart()
    {
        var made = Store().Make(["lab", "lab.amw.com"]);

        var again = Store();

        Assert.Equal(made.Signed!.Thumbprint, again.For("lab.amw.com")!.Thumbprint);
        Assert.Equal(made.Local!.Thumbprint, again.For("lab")!.Thumbprint);
        Assert.Equal(made.Authority!.Thumbprint, again.Now.Authority!.Thumbprint);
        Assert.Equal(["amw.com", Environment.MachineName.ToLowerInvariant()], again.Now.LimitedTo);
        Assert.True(again.Good(["lab", "lab.amw.com"]));
        Assert.Equal(made.Authority.Thumbprint, again.Make(["lab", "lab.amw.com", "nas.amw.com"]).Authority!.Thumbprint);
    }

    [Fact]
    public void With_no_domain_there_is_no_authority_and_one_self_signed_certificate()
    {
        var store = Store(domain: null);

        var made = store.Make(["lab", "lab.home.arpa"]);

        Assert.Null(made.Authority);
        Assert.Null(made.Signed);
        Assert.Equal(["lab", "lab.home.arpa"], made.LocalNames);
        Assert.Equal([made.Local!.Thumbprint], trusted);
        Assert.Equal(NameCerts.KeyFingerprint(own), NameCerts.KeyFingerprint(store.For("lab.home.arpa")!));
    }

    [Fact]
    public void Removing_the_last_name_takes_the_certificates_away_and_keeps_the_authority()
    {
        var store = Store();
        var made = store.Make(["lab", "lab.amw.com"]);

        var none = store.Make([]);

        Assert.Null(none.Signed);
        Assert.Null(none.Local);
        Assert.Null(store.For("lab.amw.com"));
        Assert.Equal([made.Authority!.Thumbprint], trusted);
        Assert.Equal(made.Authority.Thumbprint, none.Authority!.Thumbprint);
        Assert.False(File.Exists(Path.Combine(data, StoreNamesCertificate.SignedFile)));
        Assert.False(File.Exists(Path.Combine(data, StoreNamesCertificate.LocalFile)));
        Assert.True(store.Good([]));
    }

    [Fact]
    public void A_certificate_close_to_expiring_is_made_again_by_the_same_authority()
    {
        var made = Store().Make(["lab.amw.com"]);
        var later = new LaterClock(DateTimeOffset.UtcNow.AddDays(700));

        var store = Store(clock: later);

        Assert.False(store.Good(["lab.amw.com"]));
        var renewed = store.Make(["lab.amw.com"]);
        Assert.NotEqual(made.Signed!.Thumbprint, renewed.Signed!.Thumbprint);
        Assert.Equal(made.Authority!.Thumbprint, renewed.Authority!.Thumbprint);
        Assert.True(store.Good(["lab.amw.com"]));
    }

    [Fact]
    public void The_authority_cant_sign_for_a_name_outside_the_domain_or_for_an_ip_address()
    {
        using var key = RSA.Create(3072);
        using var authority = NameCerts.Authority("Test authority", key, ["amw.com", "server1"], DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));

        using var inside = NameCerts.Sign(authority, key, own, "Console addresses", ["lab.amw.com", "server1"], DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30));
        using var outside = NameCerts.Sign(authority, key, own, "Console addresses", ["lab.amw.com", "login.example.com"], DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30));
        using var lookalike = NameCerts.Sign(authority, key, own, "Console addresses", ["notamw.com"], DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30));
        using var address = SignedForAddress(authority, key);

        Assert.Equal(["amw.com", "server1"], NameCerts.LimitedTo(authority));
        Assert.Equal(X509ChainStatusFlags.NoError, Check(inside, authority));
        Assert.NotEqual(X509ChainStatusFlags.NoError, Check(outside, authority) & NameLimits);
        Assert.NotEqual(X509ChainStatusFlags.NoError, Check(lookalike, authority) & NameLimits);
        Assert.NotEqual(X509ChainStatusFlags.NoError, Check(address, authority) & NameLimits);
        Assert.True(NameCerts.Within("lab.amw.com", ["amw.com"]));
        Assert.False(NameCerts.Within("notamw.com", ["amw.com"]));
        Assert.False(NameCerts.Within("lab", ["amw.com"]));
    }

    [Fact]
    public void The_clients_key_fingerprint_is_the_servers_for_rsa_and_ecdsa_keys()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var curve = new CertificateRequest("CN=ec", ec, HashAlgorithmName.SHA256).CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));

        Assert.Equal(NameCerts.KeyFingerprint(own), Pin.OfKey(own.RawData));
        Assert.Equal(NameCerts.KeyFingerprint(curve), Pin.OfKey(curve.RawData));
        Assert.Equal(ClientFiles.Fingerprint(own), Pin.OfCertificate(own.RawData));
        Assert.Null(Pin.OfKey([1, 2, 3]));
        Assert.Null(Pin.OfKey([]));
    }

    [Fact]
    public async Task The_console_shows_each_added_name_its_certificate_and_a_client_knows_them_all_by_the_key()
    {
        var store = Store();
        var made = store.Make(["lab", "lab.amw.com"]);
        var pfx = Path.Combine(data, "own.pfx");
        await File.WriteAllBytesAsync(pfx, own.Export(X509ContentType.Pfx, "pw"));
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Console:Url"] = "https://127.0.0.1:0",
            ["Kestrel:Endpoints:Console:Certificate:Path"] = pfx,
            ["Kestrel:Endpoints:Console:Certificate:Password"] = "pw",
        });
        builder.WebHost.ServeNames(store.For);
        await using var server = builder.Build();
        server.MapGet("/", () => "ok");
        await server.StartAsync();
        var port = new Uri(server.Urls.First()).Port;
        var key = NameCerts.KeyFingerprint(own);

        var full = await Served(port, "lab.amw.com");
        var bare = await Served(port, "lab");
        var mine = await Served(port, "localhost");
        await server.StopAsync();

        Assert.Equal(made.Signed!.Thumbprint, full.Thumbprint);
        Assert.Equal(made.Local!.Thumbprint, bare.Thumbprint);
        Assert.Equal(own.Thumbprint, mine.Thumbprint);
        Assert.All(new[] { full, bare, mine }, shown => Assert.True(Pin.Matches(shown.RawData, key)));
        Assert.Equal(X509ChainStatusFlags.NoError, Check(full, made.Authority!));
    }

    // What Windows makes of a certificate when it trusts only this authority.
    private static X509ChainStatusFlags Check(X509Certificate2 cert, X509Certificate2 authority)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(authority);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.Build(cert);
        return chain.ChainStatus.Aggregate(X509ChainStatusFlags.NoError, (all, s) => all | s.Status);
    }

    // A certificate for an IP address, signed with the authority's key, as someone who stole the key might make.
    private X509Certificate2 SignedForAddress(X509Certificate2 authority, RSA key)
    {
        var request = new CertificateRequest(new X500DistinguishedName("CN=Console addresses"), own.PublicKey, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Parse("192.168.2.1"));
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        return request.Create(authority.SubjectName, X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1), DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30), [0x01, 0x02, 0x03]);
    }

    // A self-signed certificate like the console's own, with a key Windows can serve it with.
    private static X509Certificate2 Own(string subject, string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(name);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var made = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));
        return X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pfx, ""), "", X509KeyStorageFlags.Exportable);
    }

    // The certificate the console shows a browser that asks for this name.
    private static async Task<X509Certificate2> Served(int port, string name)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        await using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = name });
        return X509CertificateLoader.LoadCertificate(ssl.RemoteCertificate!.GetRawCertData());
    }

    private sealed class LaterClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
