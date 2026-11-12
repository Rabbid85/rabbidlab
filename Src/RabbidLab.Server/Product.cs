using System.Reflection;

namespace RabbidLab.Server;

/// <summary>
/// What this is: its name, its version, where its code lives, and where it's running. And which Rabbid Lab it is: the
/// release, or Rabbid Lab Dev (built with -p:Dev=true), which installs beside the release with its own name, port (8421),
/// data folder and service, and looks exactly like the release but for a DEV tag beside the name and a DEV band on its icon.
/// </summary>
public static class Product
{
#if DEV
    public static readonly bool Dev = true;
    public const string Name = "Rabbid Lab Dev";
    public const int DefaultPort = 8421;
#else
    public static readonly bool Dev = false;
    public const string Name = "Rabbid Lab";
    public const int DefaultPort = 8420;
#endif

    /// <summary>The software's own name, where it names the software: versions, releases, signing.</summary>
    public const string Software = "Rabbid Lab";

    /// <summary>The name on screen: the lab's name when it has one (Settings, Appearance), else Rabbid Lab. Name stays for the service, data folder and keys.</summary>
    public static string Title => Brand.Named ?? Software;

    /// <summary>The repository on GitHub: where the code is, and where releases (and so updates) come from.</summary>
    public const string Repository = "https://github.com/Rabbid85/rabbidlab";

    /// <summary>The version, from the project's Version (without the build's commit).</summary>
    public static string Version { get; } = (typeof(Product).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>A test build made on a PC (not a release): the release with the same version is newer than it.</summary>
    public static bool Test { get; } = typeof(Product).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Any(a => a.Key == "TestBuild" && a.Value == "true");

    /// <summary>Running in a container: the .NET images say so.</summary>
    public static bool InContainer { get; } = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

    /// <summary>Where it runs, as people would say it.</summary>
    public static string Platform => InContainer ? "Docker" : OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "macOS";
}
