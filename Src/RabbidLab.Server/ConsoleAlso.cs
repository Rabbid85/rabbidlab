using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Win32;

namespace RabbidLab.Server;

/// <summary>
/// The console on port 443 as well as its own port, so https://name opens it with no port typed. Its own port stays: the
/// computers in Devices are set to it, and so are bookmarks. It's the same console with the same certificates, on a second
/// HTTPS port. Turned on in Settings, Console address; kept beside Setup's choices, so an update keeps it; started with
/// Rabbid Lab, which is why turning it on or off restarts the service. Only where Setup installed an HTTPS console on
/// Windows. Another program holding port 443 is left alone, and the page says so.
/// </summary>
public static class ConsoleAlso
{
    public const int Port = 443;

    /// <summary>Wanted: someone turned it on. Listening: Rabbid Lab has the port. Taken: wanted, and another program has it.</summary>
    public sealed record Plan(bool Can, bool Wanted, bool Listening, bool Taken, int Port = ConsoleAlso.Port)
    {
        internal Socket? Held { get; init; }
    }

    /// <summary>Whether a second port makes sense here: an HTTPS console on another port.</summary>
    public static bool Fits(string? scheme, int port, int also = Port) => scheme == "https" && port != also;

    /// <summary>
    /// Decides at start: nothing unless it fits and was asked for; then the port is taken hold of, the way Kestrel binds
    /// it, so no other program gets in between and a failed bind can't stop Rabbid Lab from starting. port: 443, but for
    /// a test, which can't count on having that.
    /// </summary>
    public static Plan PlanAlso(this WebApplicationBuilder builder, Func<bool> wanted, int port = Port)
    {
        var url = (builder.Configuration["Kestrel:Endpoints:Console:Url"] ?? "").Replace("://*", "://localhost").Replace("://+", "://localhost");
        var can = !Product.InContainer && OperatingSystem.IsWindows() && Uri.TryCreate(url, UriKind.Absolute, out var console) && Fits(console.Scheme, console.Port, port);
        if (!can || !wanted())
        {
            return new Plan(can, false, false, false, port);
        }
        var held = Redirect.Bind(port);
        return new Plan(true, true, held is not null, held is null, port) { Held = held };
    }

    /// <summary>
    /// Listens there: a second endpoint in Kestrel's own settings with the console's certificate, and the socket taken hold
    /// of handed over when Kestrel asks for it. Called after the redirect port's, whose socket goes the same way.
    /// </summary>
    public static void AddAlso(this WebApplicationBuilder builder, Plan plan)
    {
        if (!plan.Listening)
        {
            return;
        }
        var port = plan.Port;
        var settings = new Dictionary<string, string?> { ["Kestrel:Endpoints:Web:Url"] = $"https://*:{port}" };
        foreach (var part in builder.Configuration.GetSection("Kestrel:Endpoints:Console:Certificate").AsEnumerable(makePathsRelative: true))
        {
            if (part.Value is not null)
            {
                settings[$"Kestrel:Endpoints:Web:Certificate:{part.Key}"] = part.Value;
            }
        }
        builder.Configuration.AddInMemoryCollection(settings);
        var held = plan.Held;
        builder.Services.Configure<SocketTransportOptions>(sockets =>
        {
            var before = sockets.CreateBoundListenSocket;
            sockets.CreateBoundListenSocket = endpoint => endpoint is IPEndPoint ip && ip.Port == port && Interlocked.Exchange(ref held, null) is { } bound ? bound : before(endpoint);
        });
    }

    public sealed record AlsoForm(bool On);

    public static void MapConsoleAlsoApi(this WebApplication app, Plan plan)
    {
        // Turning port 443 on or off: saved, the firewall rule made or taken away, and Rabbid Lab restarts to start or stop
        // listening. The page waits for it to come back.
        app.MapPost("/api/console/443", (AlsoForm form, HttpContext context, IAlsoPort saved, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.ConsoleAddresses) is { } refused)
            {
                return refused;
            }
            if (!plan.Can)
            {
                return Results.Json(new { error = "Port 443 is for an HTTPS console that Setup installed on Windows." }, statusCode: StatusCodes.Status409Conflict);
            }
            saved.Want(form.On);
            log.Record(context, "settings", form.On ? "console.443.on" : "console.443.off", form.On
                ? $"Turned on port {Port} for the console, as well as its own port"
                : $"Turned off port {Port} for the console");
            var restarting = saved.Restart();
            return Results.Ok(new { on = form.On, restarting });
        });
    }
}

/// <summary>Whether port 443 was asked for, kept where Setup keeps its choices, with the firewall rule and the restart it takes.</summary>
public interface IAlsoPort
{
    bool Wanted();

    /// <summary>Saves the choice, and makes or removes the firewall rule when Setup made the console's.</summary>
    void Want(bool on);

    /// <summary>Restarts Rabbid Lab's service in a moment, so the answer gets out first. False when it isn't running as a service.</summary>
    bool Restart();
}

/// <summary>On Windows: HKLM\SOFTWARE\Rabbid\&lt;product&gt;, netsh for the rule, and the service restarted from a process of its own.</summary>
public sealed class InstalledAlsoPort(ILogger<InstalledAlsoPort> log) : IAlsoPort
{
    private static string KeyPath => @"SOFTWARE\Rabbid\" + Product.Name;

    private static string Rule => $"{Product.Name} (port {ConsoleAlso.Port})";

    public bool Wanted() => OperatingSystem.IsWindows() && Read("Also443") is int n && n == 1;

    /// <summary>Read before there's a logger or any service: Program.cs decides the ports first.</summary>
    public static bool WantedNow() => OperatingSystem.IsWindows() && Read("Also443") is int n && n == 1;

    public void Want(bool on)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using (var key = Registry.LocalMachine.CreateSubKey(KeyPath))
        {
            key.SetValue("Also443", on ? 1 : 0, RegistryValueKind.DWord);
        }
        // The rule only where Setup made one for the console: someone who said no firewall changes meant it.
        if (Read("Firewall") is not int f || f != 1)
        {
            return;
        }
        Netsh($"advfirewall firewall delete rule name=\"{Rule}\"");
        if (on && Environment.ProcessPath is { } exe)
        {
            Netsh($"advfirewall firewall add rule name=\"{Rule}\" dir=in action=allow protocol=TCP localport={ConsoleAlso.Port} program=\"{exe}\" "
                + "profile=domain,private enable=yes description=\"Allows other computers to open the Rabbid Lab console on port 443.\"");
        }
    }

    public bool Restart()
    {
        if (!OperatingSystem.IsWindows() || !WindowsServiceHelpers.IsWindowsService())
        {
            return false;
        }
        try
        {
            // Its own process, which outlives the service it stops. Two seconds first, for the answer to reach the page.
            var name = Product.Name.Replace("'", "''");
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 2; Restart-Service -Name '{name}' -Force\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
            })?.Dispose();
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log.LogWarning(e, "The service could not be restarted for port {Port}. Restart {Product} by hand", ConsoleAlso.Port, Product.Name);
            return false;
        }
    }

    private void Netsh(string arguments)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netsh.exe", arguments) { UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(15000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log.LogWarning(e, "Windows Firewall could not be changed for port {Port}", ConsoleAlso.Port);
        }
    }

    private static object? Read(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            return key?.GetValue(name);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
