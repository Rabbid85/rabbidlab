using System.Text.RegularExpressions;

namespace RabbidLab.Tests;

/// <summary>
/// What the Windows client must keep to, read from its source (it is .NET Framework, so it can't run here). Each was
/// found broken in the audit of 1.1.435 and is easy to put back by accident.
/// </summary>
public sealed partial class ClientGuardsTests
{
    private static string Client(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RabbidLab.slnx"))) dir = dir.Parent;
        var root = dir?.FullName ?? throw new InvalidOperationException("Can't find the repository.");
        return File.ReadAllText(Path.Combine(new[] { root, "Src", "Client" }.Concat(parts).ToArray()));
    }

    [Fact]
    public void The_installer_sets_no_permissions_on_the_data_folder()
    {
        // Permissions set on a path are set on wherever a link planted at that path leads: the service makes the folder.
        var wxs = Client("Installer", "Package.wxs");
        Assert.DoesNotContain("<PermissionEx", wxs);
        Assert.DoesNotContain("<CreateFolder", wxs);
    }

    [Fact]
    public void The_windows_never_name_the_server()
    {
        // Whoever sits at a managed computer isn't told where its server is.
        foreach (var file in new[] { "Connect.cs", "Unlock.cs", "ClientWindow.cs", "Tray.cs", "AskWindow.cs" })
        {
            var source = Client("RabbidLab.Client", file);
            Assert.False(Named().IsMatch(source), $"{file} puts the server's address in what it shows.");
        }
    }

    [Fact]
    public void What_only_the_service_reads_is_kept_where_only_it_can()
    {
        // The errors it has to tell, Group Policy's results and the installer's log aren't for everyone signed in.
        var all = string.Concat(new[] { "Problems.cs", "GroupPolicy.cs", "ClientService.cs" }.Select(f => Client("RabbidLab.Client", f)));
        Assert.DoesNotMatch(Open(), all);
        Assert.Contains("Folder.Kept(\"problems.json\")", all);
        Assert.Contains("Folder.Kept(\"update.log\")", all);
    }

    [Fact]
    public void The_pipe_hangs_up_on_silence_and_the_tray_checks_whose_it_is()
    {
        var pipe = Client("RabbidLab.Client", "Pipe.cs");
        Assert.Contains("new Timer(_ => HangUp(pipe)", pipe); // four silent programs held every place on it
        Assert.Contains("if (!Services(pipe))", pipe);         // a pipe another program made first is told nothing
    }

    [GeneratedRegex(@"\{(now|s|status)\.(Server|PolicyServer|Host|ServerVersion)\}|\{held\}|LockedNote\(")]
    private static partial Regex Named();

    [GeneratedRegex(@"Folder\.File\(""(problems\.json|update\.log)""\)|Path\.Combine\(Folder\.Data, \$""gp-")]
    private static partial Regex Open();
}
