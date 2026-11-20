using System.Runtime.InteropServices;

namespace RabbidLab.Server;

/// <summary>What the page shows about this Rabbid Lab: which one, its version, the machine it runs on, and since when.</summary>
public sealed record StatusReport(string Product, string Version, bool Dev, string Server, string Platform, string Os, string DotNet, DateTime StartedAt,
    string Repository, string DataFolder);

public sealed class ServerStatus(DataFolder folder)
{
    private readonly DateTime started = DateTime.UtcNow;

    // Windows 11 Pro 25H2 rather than .NET's Microsoft Windows 10.0.26300.
    private readonly string os = OperatingSystem.IsWindows() ? RabbidLab.Shared.WindowsVersion.Name() : RuntimeInformation.OSDescription;

    public StatusReport Read() => new(Product.Name, Product.Version, Product.Dev, Environment.MachineName, Product.Platform,
        os, RuntimeInformation.FrameworkDescription, started, Product.Repository, folder.Path);
}

public static class StatusApi
{
    public static void MapStatusApi(this WebApplication app) => app.MapGet("/api/status", (ServerStatus status) => status.Read());
}
