using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Win32;

namespace RabbidLab.Server;

/// <summary>Settings, Console address: the addresses Setup's last page lists, the HTTPS certificate, the port and the firewall.</summary>
public static class ConsoleAddress
{
    /// <summary>An address; Name, on the first row of an added one, is what removing it takes. Dns says where its alias is.</summary>
    public sealed record Address(string Url, string What, string? Name = null, string? Dns = null);

    public sealed record CertificateInfo(string Subject, string Issuer, bool SelfSigned, DateTime Expires, string Fingerprint);

    /// <summary>Installed: Setup put it there, so running Setup again changes it. Firewall is null when Setup didn't say.</summary>
    public sealed record Report(string Scheme, int Port, List<Address> Addresses, CertificateInfo? Certificate, int? RedirectPort,
        bool RedirectTaken, bool? Firewall, bool Installed, bool Docker, bool CanAdd, string? Domain, CertificateInfo? NamesCertificate);

    public static void MapConsoleAddressApi(this WebApplication app, Redirect.Plan? redirect)
    {
        app.MapGet("/api/console", (IServer server, IConfiguration config, ConsoleNames names, INamesCertificate named) => Read(server, config, redirect, names, named));
        // The console's own certificate, or with ?names=1 the one for the added names.
        app.MapGet("/api/console/certificate", (IConfiguration config, INamesCertificate named, bool? names) => (names == true ? named.Current : ClientFiles.Certificate(config)) is { } cert
            ? Results.File(cert.RawData, "application/pkix-cert", $"{Product.Name}.cer")
            : Results.NotFound(new { error = "This console isn't using an HTTPS certificate." }));
    }

    private static Report Read(IServer server, IConfiguration config, Redirect.Plan? redirect, ConsoleNames names, INamesCertificate named)
    {
        // The console's own address, not port 80's redirect.
        var listening = (server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [])
            .Select(a => Uri.TryCreate(a.Replace("://+", "://localhost").Replace("://*", "://localhost"), UriKind.Absolute, out var u) ? u : null)
            .FirstOrDefault(u => u is not null && u.Port != redirect?.Port);
        var scheme = listening?.Scheme ?? "http";
        var port = listening?.Port ?? Product.DefaultPort;
        string Url(string host) => $"{scheme}://{host}:{port}";

        // The same list as Setup's last page (Choices.Urls). In Docker these are the container's, so none.
        var list = new List<Address>();
        if (!Product.InContainer)
        {
            var domain = (IPGlobalProperties.GetIPGlobalProperties().DomainName ?? "").ToLowerInvariant();
            // Names added in Settings first, then Setup's.
            foreach (var n in names.All())
            {
                var dns = n.Server is null ? null : $"an alias in DNS on {n.Server.Split('.')[0].ToUpperInvariant()}, pointing to this computer";
                list.Add(new(Url(n.Name), dns is null ? "Added here" : $"Added here · {dns}", n.Name, dns));
                if (n.Full != n.Name)
                {
                    list.Add(new(Url(n.Full), n.Zone is null ? "Its full name" : $"Its full name · in the {n.Zone} zone"));
                }
            }
            var extra = ExtraName() ?? "";
            if (extra.Length > 0)
            {
                list.Add(new(Url(extra), "Host name"));
                if (!extra.Contains('.') && domain.Length > 0)
                {
                    list.Add(new(Url($"{extra}.{domain}"), "Full host name"));
                }
            }
            var name = Environment.MachineName.ToLowerInvariant();
            list.Add(new(Url(name), "Computer name"));
            if (domain.Length > 0)
            {
                list.Add(new(Url($"{name}.{domain}"), "Full computer name"));
            }
            list.AddRange(Ips().Select(ip => new Address(Url(ip), "IP address")));
        }

        var cert = scheme == "https" ? ClientFiles.Certificate(config) : null;
        var info = Info(cert);
        var firewall = Saved("Firewall") is int f ? f == 1 : (bool?) null;
        var canAdd = OperatingSystem.IsWindows() && !Product.InContainer;
        return new Report(scheme, port, list, info, redirect?.Port, redirect?.Taken == true, firewall, Saved("Port") is not null, Product.InContainer,
            canAdd, names.Domain, scheme == "https" ? Info(named.Current) : null);
    }

    private static List<string> Ips() => ConsoleNames.Ips();

    private static CertificateInfo? Info(X509Certificate2? cert) => cert is null ? null : new CertificateInfo(cert.Subject, cert.GetNameInfo(X509NameType.SimpleName, forIssuer: true),
        cert.SubjectName.RawData.AsSpan().SequenceEqual(cert.IssuerName.RawData), cert.NotAfter.ToUniversalTime(), Convert.ToHexString(SHA256.HashData(cert.RawData)));

    /// <summary>The host name Setup was given, lower case, or null.</summary>
    public static string? ExtraName() => Saved("ExtraName") is string { Length: > 0 } s ? s.ToLowerInvariant() : null;

    /// <summary>What Setup saved about the install (HKLM\SOFTWARE\Rabbid\&lt;product&gt;); null off Windows or when it isn't there.</summary>
    private static object? Saved(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Rabbid\" + Product.Name);
            return key?.GetValue(name);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
