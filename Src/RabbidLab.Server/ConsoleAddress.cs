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
    /// <summary>
    /// An address. Name is what removing it takes (a name added here, or Setup's host name), on its full name's row too; Dns
    /// says where its alias is. Hide is this computer's own name or IP, which comes off the list only: the console still answers there.
    /// </summary>
    public sealed record Address(string Url, string What, string? Name = null, string? Dns = null, string? Hide = null);

    public sealed record CertificateInfo(string Subject, string Issuer, bool SelfSigned, DateTime Expires, string Fingerprint);

    /// <summary>This install's certificate authority for added addresses: what it can sign for, and the file it downloads as.</summary>
    public sealed record AuthorityInfo(string Subject, DateTime Expires, IReadOnlyList<string> LimitedTo, string File);

    /// <summary>A certificate for added addresses, and which.</summary>
    public sealed record NamesInfo(DateTime Expires, IReadOnlyList<string> Names);

    /// <summary>
    /// Installed: Setup put it there, so running Setup again changes it. Firewall is null when Setup didn't say. Signed is
    /// the certificate from the authority, Local the self-signed one for the names it can't sign for (ConsoleCertificates.cs).
    /// </summary>
    public sealed record Report(string Scheme, int Port, List<Address> Addresses, CertificateInfo? Certificate, int? RedirectPort,
        bool RedirectTaken, bool? Firewall, bool Installed, bool Docker, bool CanAdd, string? Domain, AuthorityInfo? Authority, NamesInfo? Signed, NamesInfo? Local,
        IReadOnlyList<RedirectByName.Claim>? RedirectNames = null, List<Address>? Hidden = null);

    private static string AuthorityFile => $"{Product.Name} certificate authority.cer";

    public static void MapConsoleAddressApi(this WebApplication app, Redirect.Plan? redirect)
    {
        // Port 80 shared by name (Redirect.cs): which names come here.
        var byName = app.Services.GetService<RedirectByName>();
        app.MapGet("/api/console", (IServer server, IConfiguration config, ConsoleNames names, INamesCertificate named) => Read(server, config, redirect, names, named) with
        {
            RedirectNames = byName?.Claims,
        });
        // The console's own certificate; with ?authority=true the certificate authority for added addresses, and with
        // ?names=true the self-signed one for the added names the authority can't sign for.
        app.MapGet("/api/console/certificate", (IConfiguration config, INamesCertificate named, bool? names, bool? authority) =>
            (authority == true ? named.Now.Authority : names == true ? named.Now.Local : ClientFiles.Certificate(config)) is { } cert
                ? Results.File(cert.RawData, "application/pkix-cert", authority == true ? AuthorityFile : $"{Product.Name}.cer")
                : Results.NotFound(new { error = "This console doesn't have that certificate." }));
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
                    list.Add(new(Url(n.Full), n.Zone is null ? "Its full name" : $"Its full name · in the {n.Zone} zone", n.Name, dns));
                }
            }
            var extra = names.Install.Name() ?? "";
            var alias = names.Install.Alias();
            var setupDns = alias is null ? null : $"an alias in DNS on {alias.Value.Server.Split('.')[0].ToUpperInvariant()}, pointing to this computer";
            if (extra.Length > 0)
            {
                list.Add(new(Url(extra), setupDns is null ? "Host name" : $"Host name · {setupDns}", extra, setupDns));
                if (!extra.Contains('.') && domain.Length > 0)
                {
                    list.Add(new(Url($"{extra}.{domain}"), "Full host name", extra, setupDns));
                }
            }
            var name = Environment.MachineName.ToLowerInvariant();
            list.Add(new(Url(name), "Computer name", Hide: name));
            if (domain.Length > 0)
            {
                list.Add(new(Url($"{name}.{domain}"), "Full computer name", Hide: $"{name}.{domain}"));
            }
            list.AddRange(Ips().Select(ip => new Address(Url(ip), "IP address", Hide: ip)));
        }

        // This computer's own addresses taken off the list, for Show them.
        var hidden = names.Hidden();
        var off = list.Where(a => a.Hide is not null && hidden.Contains(a.Hide)).ToList();
        list.RemoveAll(off.Contains);

        var cert = scheme == "https" ? ClientFiles.Certificate(config) : null;
        var info = Info(cert);
        var firewall = Saved("Firewall") is int f ? f == 1 : (bool?) null;
        var canAdd = OperatingSystem.IsWindows() && !Product.InContainer;
        var now = scheme == "https" ? named.Now : NamesCertificates.None;
        return new Report(scheme, port, list, info, redirect?.Port, redirect?.Taken == true, firewall, Saved("Port") is not null, Product.InContainer,
            canAdd, names.Domain,
            now.Authority is null ? null : new AuthorityInfo(now.Authority.GetNameInfo(X509NameType.SimpleName, forIssuer: false), now.Authority.NotAfter.ToUniversalTime(), now.LimitedTo, AuthorityFile),
            now.Signed is null ? null : new NamesInfo(now.Signed.NotAfter.ToUniversalTime(), now.SignedNames),
            now.Local is null ? null : new NamesInfo(now.Local.NotAfter.ToUniversalTime(), now.LocalNames), Hidden: off);
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

/// <summary>Setup's host name for the console, and the DNS alias Setup added for it (name|zone|server|target).</summary>
public interface IInstallName
{
    string? Name();

    (string Name, string Zone, string Server, string Target)? Alias();

    /// <summary>Forgets the host name, so the console stops using it (Settings, Console address, Remove).</summary>
    void Forget();

    /// <summary>Forgets the alias once it's out of DNS, so uninstalling doesn't try again.</summary>
    void ForgetAlias();
}

/// <summary>Where Setup keeps them: HKLM\SOFTWARE\Rabbid\&lt;product&gt;. Nothing off Windows.</summary>
public sealed class RegistryInstallName : IInstallName
{
    private static string KeyPath => @"SOFTWARE\Rabbid\" + Product.Name;

    public string? Name() => ConsoleAddress.ExtraName();

    public (string Name, string Zone, string Server, string Target)? Alias()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
        return (key?.GetValue("DnsAlias") as string)?.Split('|') is [var name, var zone, var server, var target] ? (name, zone, server, target) : null;
    }

    public void Forget() => Write(key => key.SetValue("ExtraName", ""));

    public void ForgetAlias() => Write(key => key.DeleteValue("DnsAlias", throwOnMissingValue: false));

    private static void Write(Action<RegistryKey> change)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
        if (key is not null)
        {
            change(key);
        }
    }
}
