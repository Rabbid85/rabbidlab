using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>The console on a second HTTPS port (443 for real): when it listens, with which certificate, and who may turn it on.</summary>
public sealed class ConsoleAlsoTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-also-" + Guid.NewGuid().ToString("N"));

    public ConsoleAlsoTests() => Directory.CreateDirectory(data);

    public void Dispose()
    {
        try
        {
            Directory.Delete(data, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temp folder's own cleanup.
        }
    }

    // A port nothing has right now.
    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint) probe.LocalEndpoint).Port;
    }

    // A console like Setup's: one HTTPS endpoint in Kestrel's settings, with a certificate of its own.
    private WebApplicationBuilder Console(int port)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Rabbid Lab test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var pfx = Path.Combine(data, "console.pfx");
        File.WriteAllBytes(pfx, cert.Export(X509ContentType.Pfx, "test"));
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Console:Url"] = $"https://*:{port}",
            ["Kestrel:Endpoints:Console:Certificate:Path"] = pfx,
            ["Kestrel:Endpoints:Console:Certificate:Password"] = "test",
        });
        return builder;
    }

    private static HttpClient Trusting() => new(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true });

    [Fact]
    public async Task Turned_on_the_same_console_answers_on_the_second_port_with_its_own_certificate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var (main, second) = (FreePort(), FreePort());
        var builder = Console(main);
        var also = builder.PlanAlso(() => true, second);
        builder.AddAlso(also);
        builder.WebHost.ServeNames(_ => null);
        await using var app = builder.Build();
        app.MapGet("/where", (HttpContext context) => context.Connection.LocalPort.ToString());
        await app.StartAsync();
        using var http = Trusting();

        var first = await http.GetStringAsync($"https://localhost:{main}/where");
        var other = await http.GetStringAsync($"https://localhost:{second}/where");
        var listening = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;

        Assert.True(also is { Can: true, Wanted: true, Listening: true, Taken: false });
        Assert.Equal(main.ToString(), first);
        Assert.Equal(second.ToString(), other);
        Assert.Equal(2, listening.Count);
        await app.StopAsync();
    }

    [Fact]
    public async Task A_port_another_program_has_is_left_alone_and_the_console_still_starts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var main = FreePort();
        using var theirs = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp) { DualMode = true, ExclusiveAddressUse = true };
        theirs.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
        theirs.Listen();
        var taken = ((IPEndPoint) theirs.LocalEndPoint!).Port;
        var builder = Console(main);
        var also = builder.PlanAlso(() => true, taken);
        builder.AddAlso(also);
        await using var app = builder.Build();
        app.MapGet("/where", () => "here");
        await app.StartAsync();
        using var http = Trusting();

        Assert.True(also is { Wanted: true, Listening: false, Taken: true });
        Assert.Equal("here", await http.GetStringAsync($"https://localhost:{main}/where"));
        await app.StopAsync();
    }

    [Fact]
    public void Nothing_is_planned_until_someone_asks_or_where_it_makes_no_sense()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var asked = false;
        var off = Console(FreePort()).PlanAlso(() => false, FreePort());
        var plain = WebApplication.CreateBuilder().PlanAlso(() => asked = true); // no console.json: not Setup's HTTPS console

        Assert.True(off is { Can: true, Wanted: false, Listening: false });
        Assert.True(plain is { Can: false, Wanted: false, Listening: false });
        Assert.False(asked);
        Assert.True(ConsoleAlso.Fits("https", 8420));
        Assert.False(ConsoleAlso.Fits("https", 443)); // already there
        Assert.False(ConsoleAlso.Fits("http", 8420));
    }

    [Fact]
    public async Task Turning_it_on_takes_the_console_address_permission_and_an_installed_HTTPS_console()
    {
        using var app = new TestApp(data);
        var admin = await app.SignedIn();
        (await admin.PostAsJsonAsync("/api/accounts", new { username = "nobody1", password = "user password" })).EnsureSuccessStatusCode();
        var user = app.Page();
        (await user.PostAsJsonAsync("/api/signin", new { username = "nobody1", password = "user password" })).EnsureSuccessStatusCode();

        var refused = await user.PostAsJsonAsync("/api/console/443", new { on = true });
        var notHere = await admin.PostAsJsonAsync("/api/console/443", new { on = true }); // the test console is plain HTTP, not Setup's
        var report = await admin.GetFromJsonAsync<JsonElement>("/api/console");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, notHere.StatusCode);
        Assert.False(report.GetProperty("also").GetProperty("can").GetBoolean());
        Assert.Empty(app.AlsoPort.Asked);
    }
}
