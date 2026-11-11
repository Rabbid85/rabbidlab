using System.Diagnostics;

namespace RabbidLab.Updater;

/// <summary>Starts Rabbid Lab's own installer, quietly, to update in place.</summary>
public interface IInstallerRunner
{
    void Run(string installer, string log);
}

/// <summary>
/// Runs the installer with /quiet /acceptlicense: it keeps every earlier answer (they're in the registry), stops
/// this service, replaces the program and starts the service again, as the new version. /noupdatecheck: it installs
/// itself, the version that was downloaded and checked, without asking GitHub for a newer one first.
/// </summary>
public sealed class InstallerRunner : IInstallerRunner
{
    public void Run(string installer, string log)
    {
        var start = new ProcessStartInfo(installer) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/quiet");
        start.ArgumentList.Add("/acceptlicense");
        start.ArgumentList.Add("/noupdatecheck");
        start.ArgumentList.Add("/log=" + log);
        Process.Start(start);
    }
}

/// <summary>Windows wouldn't run the installer. The message says what stopped it and how to allow it, in sentences.</summary>
public sealed class InstallerBlockedException(string message, Exception inner) : Exception(message, inner);
