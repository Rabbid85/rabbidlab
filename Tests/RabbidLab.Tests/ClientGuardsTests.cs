using System.Text.RegularExpressions;

namespace RabbidLab.Tests;

/// <summary>
/// What the Windows client must keep to, read from its source (it is .NET Framework, so it can't run here). Each was
/// found broken in the audit of 1.1.435 and is easy to put back by accident.
/// </summary>
public sealed partial class ClientGuardsTests
{
    private static string ClientPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RabbidLab.slnx"))) dir = dir.Parent;
        var root = dir?.FullName ?? throw new InvalidOperationException("Can't find the repository.");
        return Path.Combine(new[] { root, "Src", "Client" }.Concat(parts).ToArray());
    }

    private static string Client(params string[] parts) => File.ReadAllText(ClientPath(parts));

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

    [Fact]
    public void The_client_connects_only_over_https()
    {
        // Over http there is no certificate to tell the server by, and its scripts run here as SYSTEM.
        var connect = Client("RabbidLab.Client", "Connect.cs");
        Assert.DoesNotContain("u.Scheme != \"http\"", connect);
        Assert.Contains("u.Scheme != \"https\"", connect);
        Assert.Contains("if (s.Server is not null && !Https(s.Server))", Client("RabbidLab.Client", "Settings.cs"));
        Assert.Contains("if (settings.Plain is not null)", Client("RabbidLab.Client", "Agent.cs"));
    }

    [Fact]
    public void No_link_or_web_page_can_start_the_client()
    {
        // As Datto's agent: the installer knows its server, and nothing a web page can open hands the client one.
        Assert.DoesNotContain("URL Protocol", Client("Installer", "Package.wxs"));
        Assert.DoesNotContain("rabbidlab:", Client("RabbidLab.Client", "Program.cs"));
        Assert.DoesNotContain("Parse(", Client("RabbidLab.Client", "Connect.cs"));
        Assert.DoesNotContain("click Connect this computer", Client("RabbidLab.Client", "Agent.cs"));
    }

    [Fact]
    public void The_installer_from_the_console_knows_its_server_and_never_moves_a_connected_computer()
    {
        // The installer leaves its own file name; the service takes a server from it only where there is none.
        Assert.Contains("Name=\"Package\" Type=\"string\" Value=\"[OriginalDatabase]\"", Client("Installer", "Package.wxs"));
        Assert.Contains("if (Settings.Adopt())", Client("RabbidLab.Client", "ClientService.cs"));
        var settings = Client("RabbidLab.Client", "Settings.cs");
        Assert.Contains("if (Text(installed, \"Server\") is not null || (policy is not null && Text(policy, \"Server\") is not null) || FromName(package) is not { } named)", settings);

        // The name the server gives its download is one the client reads back: the same pattern, taken from the client's source.
        var pattern = new Regex(NamedIn().Match(settings).Groups[1].Value);
        var key = new string('A', 62) + "1F";
        var m = pattern.Match(RabbidLab.Server.ClientFiles.DownloadName("1.1.439", "https", new Microsoft.AspNetCore.Http.HostString("RabbidLab.amw.com", 8421), key).Replace(".msi", " (1).msi")); // as a browser names a second download
        Assert.True(m.Success);
        Assert.Equal(("rabbidlab.amw.com", "8421", key), (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value));
        // The usual port is written too, and a server with no key of its own (behind a proxy) gives none.
        m = pattern.Match(RabbidLab.Server.ClientFiles.DownloadName("1.1.439", "https", new Microsoft.AspNetCore.Http.HostString("lab.example.com"), null));
        Assert.Equal(("lab.example.com", "443", false), (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Success));
        // Never over http, and never an address that can't be a file name: those get the plain name, which names no server.
        Assert.Equal("RabbidLab-Client-1.1.439.msi", RabbidLab.Server.ClientFiles.DownloadName("1.1.439", "http", new Microsoft.AspNetCore.Http.HostString("lab.example.com", 8420), key));
        Assert.Equal("RabbidLab-Client-1.1.439.msi", RabbidLab.Server.ClientFiles.DownloadName("1.1.439", "https", new Microsoft.AspNetCore.Http.HostString("[::1]", 8421), key));
        Assert.False(pattern.IsMatch("RabbidLab-Client-1.1.439.msi"));
        // A fingerprint that is not whole names no server at all.
        Assert.False(pattern.IsMatch("RabbidLab-Client-1.1.439@lab.example.com@8421@ABCDEF.msi"));
    }

    [GeneratedRegex("public const string Named = @\"([^\"]+)\";")]
    private static partial Regex NamedIn();

    [Fact]
    public void The_client_that_ships_reads_nothing_from_the_environment()
    {
        // Its folder and its server come from the installer and Group Policy only: the try-out variables are for debug builds.
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(ClientPath("RabbidLab.Client", "Settings.cs"))!, "*.cs"))
        {
            var debug = false;
            foreach (var line in File.ReadLines(file))
            {
                var code = line.Trim();
                if (code.StartsWith("#if DEBUG")) debug = true;
                else if (code.StartsWith("#else") || code.StartsWith("#endif")) debug = false;
                else if (code.Contains("GetEnvironmentVariable(")) Assert.True(debug, $"{Path.GetFileName(file)} reads the environment in a release build: {code}");
            }
        }
    }

    [Fact]
    public void Scripts_left_behind_are_deleted_when_the_service_starts()
    {
        Assert.Contains("Jobs.Sweep();", Client("RabbidLab.Client", "ClientService.cs"));
    }

    [GeneratedRegex(@"\{(now|s|status)\.(Server|PolicyServer|Host|ServerVersion)\}|\{held\}|LockedNote\(")]
    private static partial Regex Named();

    [GeneratedRegex(@"Folder\.File\(""(problems\.json|update\.log)""\)|Path\.Combine\(Folder\.Data, \$""gp-")]
    private static partial Regex Open();
}
