using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>Console addresses added in Settings: the alias in DNS, the certificate of their own, and taking them away.</summary>
public sealed class ConsoleNamesTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-names-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public ConsoleNamesTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
    }

    public void Dispose()
    {
        app.Dispose();
        try
        {
            Directory.Delete(data, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temp folder's own cleanup.
        }
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static readonly object Account = new { username = @"AMW\awiles", password = "right" };

    [Fact]
    public async Task A_name_added_with_dns_gets_its_alias_and_a_certificate_and_is_listed()
    {
        var admin = await app.SignedIn();

        var added = await admin.PostAsJsonAsync("/api/console/names", new { name = "Lab", dns = true, username = @"AMW\awiles", password = "right" });
        var page = await admin.GetFromJsonAsync<JsonElement>("/api/console");
        var urls = page.GetProperty("addresses").EnumerateArray().Select(a => a.GetProperty("url").GetString()).ToList();

        added.EnsureSuccessStatusCode();
        Assert.Equal(["lab.amw.com"], app.Dns.Added);
        Assert.Equal(["lab", "lab.amw.com"], app.Named.Made.Single());
        Assert.Contains(urls, u => u!.Contains("//lab:"));
        Assert.Contains(urls, u => u!.Contains("//lab.amw.com:"));
        Assert.Equal("lab", page.GetProperty("addresses")[0].GetProperty("name").GetString());
        Assert.Contains("Alias added in DNS", await added.Content.ReadAsStringAsync());
        Assert.Equal("amw.com", page.GetProperty("domain").GetString());
    }

    [Fact]
    public async Task A_name_dns_has_for_something_else_is_not_added_or_changed()
    {
        var admin = await app.SignedIn();
        app.Dns.Records["nas.amw.com"] = [new() { Type = "A", Data = "192.0.2.50" }];

        var taken = await admin.PostAsJsonAsync("/api/console/names", new { name = "nas", dns = true, username = @"AMW\awiles", password = "right" });
        var check = await Json(await admin.PostAsJsonAsync("/api/console/names/check", new { name = "nas", username = @"AMW\awiles", password = "right" }));

        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Contains("192.0.2.50", await taken.Content.ReadAsStringAsync());
        Assert.False(check.GetProperty("ok").GetBoolean());
        Assert.Empty(app.Dns.Added);
        Assert.Empty(app.Named.Made);
    }

    [Fact]
    public async Task Dns_needs_an_account_and_a_domain_and_says_when_the_password_is_wrong()
    {
        var admin = await app.SignedIn();

        var noAccount = await admin.PostAsJsonAsync("/api/console/names", new { name = "lab", dns = true });
        var wrong = await admin.PostAsJsonAsync("/api/console/names", new { name = "lab", dns = true, username = @"AMW\awiles", password = "wrong" });
        app.Dns.Domain = null;
        var noDomain = await admin.PostAsJsonAsync("/api/console/names", new { name = "lab", dns = true, username = @"AMW\awiles", password = "right" });
        var onItsOwn = await admin.PostAsJsonAsync("/api/console/names", new { name = "lab.home.arpa" });

        Assert.Equal(HttpStatusCode.BadRequest, noAccount.StatusCode);
        Assert.Contains("password is wrong", await wrong.Content.ReadAsStringAsync());
        Assert.Contains("isn't in a domain", await noDomain.Content.ReadAsStringAsync());
        onItsOwn.EnsureSuccessStatusCode();
        Assert.Contains("doesn't answer yet", await onItsOwn.Content.ReadAsStringAsync());
        Assert.Equal(["lab.home.arpa"], app.Named.Made.Single());
    }

    [Fact]
    public async Task Removing_a_name_takes_its_alias_out_and_makes_the_certificate_again_without_it()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/console/names", new { name = "lab", dns = true, username = @"AMW\awiles", password = "right" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/console/names", new { name = "monitor" })).EnsureSuccessStatusCode();

        var noAccount = await admin.PostAsJsonAsync("/api/console/names/remove", new { name = "lab", dns = true });
        (await admin.PostAsJsonAsync("/api/console/names/remove", new { name = "lab", dns = true, username = @"AMW\awiles", password = "right" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/console/names/remove", new { name = "monitor" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.BadRequest, noAccount.StatusCode);
        Assert.Equal(["lab.amw.com in amw.com on dc01.amw.com to server1.amw.com"], app.Dns.Removed);
        Assert.Equal(["monitor", "monitor.amw.com"], app.Named.Made[^2]);
        Assert.Empty(app.Named.Made[^1]);
        Assert.Null(app.Named.Current);
    }

    [Theory]
    [InlineData("lab_1", "letters, numbers and hyphens")]
    [InlineData("-lab", "letters, numbers and hyphens")]
    [InlineData("localhost", "this computer's own names")]
    [InlineData("server1.amw.com", "this computer's own names")]
    public async Task Names_that_cant_be_used_are_refused(string name, string said)
    {
        var admin = await app.SignedIn();

        var refused = await admin.PostAsJsonAsync("/api/console/names", new { name });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(said, await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_name_is_added_once()
    {
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/console/names", new { name = "lab" })).EnsureSuccessStatusCode();

        var again = await admin.PostAsJsonAsync("/api/console/names", new { name = "lab.amw.com" });
        var look = await Json(await admin.PostAsJsonAsync("/api/console/names/look", new { name = "LAB" }));

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("in the list already", look.GetProperty("problem").GetString());
    }

    [Fact]
    public async Task The_console_shows_an_added_name_its_certificate_and_everyone_else_its_own()
    {
        using var own = Certificate("CN=Rabbid Lab test", "localhost");
        using var named = Certificate("CN=Console addresses", "lab.test");
        var pfx = Path.Combine(data, "own.pfx");
        await File.WriteAllBytesAsync(pfx, own.Export(X509ContentType.Pfx, "pw"));
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Console:Url"] = "https://127.0.0.1:0",
            ["Kestrel:Endpoints:Console:Certificate:Path"] = pfx,
            ["Kestrel:Endpoints:Console:Certificate:Password"] = "pw",
        });
        builder.WebHost.ServeNames(name => name == "lab.test" ? named : null);
        await using var server = builder.Build();
        server.MapGet("/", () => "ok");
        await server.StartAsync();
        var port = new Uri(server.Urls.First()).Port;

        Assert.Equal(named.Thumbprint, await Served(port, "lab.test"));
        Assert.Equal(own.Thumbprint, await Served(port, "localhost"));
        await server.StopAsync();
    }

    // A self-signed certificate for a name, with a key Windows can serve it with.
    private static X509Certificate2 Certificate(string subject, string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(name);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var made = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pfx, ""), "", X509KeyStorageFlags.Exportable);
    }

    // The certificate the console shows a browser that asks for this name.
    private static async Task<string> Served(int port, string name)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        await using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = name });
        return X509CertificateLoader.LoadCertificate(ssl.RemoteCertificate!.GetRawCertData()).Thumbprint;
    }
}
