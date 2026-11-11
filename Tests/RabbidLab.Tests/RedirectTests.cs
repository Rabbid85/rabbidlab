using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>The port 80 redirect only listens where nothing else does, so a taken port never stops Rabbid Lab.</summary>
public sealed class RedirectTests
{
    private static Redirect.Plan? Plan(int port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Redirect:Port"] = port.ToString();
        builder.Configuration["Kestrel:Endpoints:Console:Url"] = "https://*:8420";
        return builder.AddRedirectPort();
    }

    [Fact]
    public void A_port_another_program_listens_on_is_taken()
    {
        // How another console's redirect listens: one IPv6 socket that takes IPv4 too.
        using var other = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp) { DualMode = true };
        other.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
        other.Listen();

        var plan = Plan(((IPEndPoint) other.LocalEndPoint!).Port);

        Assert.NotNull(plan);
        Assert.True(plan.Taken);
    }

    [Fact]
    public void A_free_port_is_used()
    {
        int port;
        using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            probe.Bind(new IPEndPoint(IPAddress.Any, 0));
            port = ((IPEndPoint) probe.LocalEndPoint!).Port;
        }

        var plan = Plan(port);

        Assert.NotNull(plan);
        Assert.False(plan.Taken);
    }

    [Fact]
    public async Task The_redirect_listens_on_the_port_it_found_free_and_sends_requests_on()
    {
        int port;
        using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            probe.Bind(new IPEndPoint(IPAddress.Any, 0));
            port = ((IPEndPoint) probe.LocalEndPoint!).Port;
        }
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Redirect:Port"] = port.ToString();
        builder.Configuration["Kestrel:Endpoints:Console:Url"] = "http://127.0.0.1:0";
        var plan = builder.AddRedirectPort();
        // The port is held from here on: nothing else can take it before the server listens on it.
        using (var other = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp) { DualMode = true })
        {
            Assert.Throws<SocketException>(() => other.Bind(new IPEndPoint(IPAddress.IPv6Any, port)));
        }
        await using var app = builder.Build();
        app.UseRedirectPort(plan);
        await app.StartAsync();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        var sent = await http.GetAsync($"http://127.0.0.1:{port}/links?x=1");

        Assert.Equal(HttpStatusCode.Redirect, sent.StatusCode);
        Assert.Equal("http://127.0.0.1:0/links?x=1", sent.Headers.Location?.OriginalString);
        await app.StopAsync();
    }
}
