using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>An app on the Installs page: a winget package, installed for everyone on a computer, as SYSTEM.</summary>
public sealed class DeviceApp
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Publisher { get; set; }
    public string? About { get; set; }
    public string Category { get; set; } = DeviceApps.Other;

    /// <summary>The package's ID in the winget repository, such as Microsoft.VisualStudioCode.</summary>
    public string Winget { get; set; } = "";

    /// <summary>How its name starts under Installed apps, when that isn't its name here.</summary>
    public string? Shows { get; set; }

    public string? By { get; set; }
    public DateTime Changed { get; set; }

    /// <summary>One Rabbid Lab comes with (BuiltInApps): not kept in apps.json, and not changed or deleted.</summary>
    [JsonIgnore]
    public bool BuiltIn { get; set; }

    /// <summary>A built-in one's name under Installed apps, as a pattern.</summary>
    [JsonIgnore]
    public Regex? Pattern { get; set; }

    /// <summary>VS Code extensions added with it, for each person who uses the computer.</summary>
    [JsonIgnore]
    public IReadOnlyList<VsCodeExtension> Extensions { get; set; } = [];
}

/// <summary>A VS Code extension by its ID (publisher.name), and what people call it.</summary>
public sealed record VsCodeExtension(string Id, string Name);

public sealed class DeviceAppsFile
{
    public List<DeviceApp> Apps { get; set; } = [];
}

public sealed record AppForm(string? Id, string? Name, string? Winget, string? Publisher = null, string? About = null, string? Category = null, string? Shows = null);

/// <summary>
/// The apps on the Installs page: the built-in ones, and the ones added here by their winget ID, kept in apps.json in the
/// data folder. Installing one is a job like any other (DeviceJobs): a script the server writes around the ID, so nothing
/// typed here runs as code.
/// </summary>
public sealed partial class DeviceApps(DataFolder folder, TimeProvider clock)
{
    public const string Other = "Other";
    public static readonly IReadOnlyList<string> Categories = ["Browsers", "Developer tools", "Admin tools", "Utilities", "Office", "Media", "Runtimes", Other];
    public const int MaxApps = 300;
    public const int MaxName = 80;

    /// <summary>How long one app's install may run before it's stopped.</summary>
    public const int Minutes = 45;

    /// <summary>At most this many apps in one job.</summary>
    public const int MostAtOnce = Workflows.MaxSteps;

    // A winget package ID: two to eight parts with dots between, of letters, digits, + - and _.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9+_-]{0,31}(\.[A-Za-z0-9+_-]{1,32}){1,7}$")]
    private static partial Regex IdShape();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<DeviceAppsFile> file = folder.Json<DeviceAppsFile>("apps.json", Json);
    private readonly ConditionalWeakTable<DeviceInventory, Found> found = new();
    private int stamp; // goes up when the apps change, so what was found on each computer is looked for again

    private sealed record Found(int Stamp, Dictionary<string, DeviceProgram> Programs);

    public IReadOnlyList<DeviceApp> All()
    {
        lock (file.Gate)
        {
            return [.. file.Read().Apps, .. BuiltInApps.All];
        }
    }

    public DeviceApp? Find(string? id)
    {
        if (BuiltInApps.Find(id) is { } builtIn)
        {
            return builtIn;
        }
        lock (file.Gate)
        {
            return file.Read().Apps.FirstOrDefault(a => a.Id == id);
        }
    }

    /// <summary>Why it can't be a winget package ID, or null when it can.</summary>
    public static string? BadId(string? winget) =>
        string.IsNullOrWhiteSpace(winget) ? "Give the package's winget ID, such as Mozilla.Firefox."
        : winget.Trim().Length > 128 || !IdShape().IsMatch(winget.Trim()) ? "A winget ID looks like Publisher.App: letters, digits, + - and _, with dots between the parts."
        : null;

    /// <summary>Adds an app, or changes the one with this id.</summary>
    public (DeviceApp? App, string? Problem) Save(AppForm form, string by)
    {
        if (BuiltInApps.Is(form.Id))
        {
            return (null, "Built-in apps can't be changed.");
        }
        var name = form.Name?.Trim() ?? "";
        if (name.Length is 0 or > MaxName || name.Any(char.IsControl))
        {
            return (null, name.Length == 0 ? "Give the app a name." : $"A name is {MaxName} characters at most.");
        }
        if (BadId(form.Winget) is { } bad)
        {
            return (null, bad);
        }
        var winget = form.Winget!.Trim();
        var category = Categories.FirstOrDefault(c => string.Equals(c, form.Category?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Other;
        lock (file.Gate)
        {
            var f = file.Read();
            var others = f.Apps.Where(a => a.Id != form.Id).Concat(BuiltInApps.All).ToList();
            if (others.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) is not null)
            {
                return (null, $"There's already an app called {name}.");
            }
            if (others.FirstOrDefault(a => string.Equals(a.Winget, winget, StringComparison.OrdinalIgnoreCase)) is { } same)
            {
                return (null, $"{same.Name} already installs {same.Winget}.");
            }
            var app = f.Apps.FirstOrDefault(a => a.Id == form.Id);
            if (app is null)
            {
                if (!string.IsNullOrEmpty(form.Id))
                {
                    return (null, "That app isn't here any more.");
                }
                if (f.Apps.Count >= MaxApps)
                {
                    return (null, $"The page holds {MaxApps} apps of your own at most.");
                }
                app = new DeviceApp { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant() };
                f.Apps.Add(app);
            }
            (app.Name, app.Winget, app.Category) = (name, winget, category);
            (app.Publisher, app.About, app.Shows) = (Short(form.Publisher, 80), Short(form.About, 200), Short(form.Shows, 80));
            (app.By, app.Changed) = (by, clock.GetUtcNow().UtcDateTime);
            file.Save(f);
            Interlocked.Increment(ref stamp);
            return (app, null);
        }
    }

    public DeviceApp? Delete(string id)
    {
        lock (file.Gate)
        {
            var f = file.Read();
            if (f.Apps.FirstOrDefault(a => a.Id == id) is not { } app)
            {
                return null;
            }
            f.Apps.Remove(app);
            file.Save(f);
            Interlocked.Increment(ref stamp);
            return app;
        }
    }

    /// <summary>
    /// The apps a computer has, by app id, from the software its client listed. Worked out once for each inventory, and
    /// again when the apps change.
    /// </summary>
    public IReadOnlyDictionary<string, DeviceProgram> On(DeviceInventory? inventory)
    {
        if (inventory is null)
        {
            return new Dictionary<string, DeviceProgram>();
        }
        var now = Volatile.Read(ref stamp);
        if (found.TryGetValue(inventory, out var known) && known.Stamp == now)
        {
            return known.Programs;
        }
        var apps = All();
        Dictionary<string, DeviceProgram> has = [];
        foreach (var program in inventory.Software ?? [])
        {
            foreach (var app in apps)
            {
                if (!has.ContainsKey(app.Id) && Is(app, program.Name))
                {
                    has[app.Id] = program;
                }
            }
        }
        found.AddOrUpdate(inventory, new Found(now, has));
        return has;
    }

    // Whether a name under Installed apps is this app.
    private static bool Is(DeviceApp app, string name) => app.Pattern is { } pattern ? pattern.IsMatch(name)
        : name.StartsWith(string.IsNullOrWhiteSpace(app.Shows) ? app.Name : app.Shows, StringComparison.OrdinalIgnoreCase);

    private static string? Short(string? text, int most)
    {
        var t = string.Concat((text ?? "").Where(c => !char.IsControl(c))).Trim();
        return t.Length == 0 ? null : t.Length > most ? t[..most] : t;
    }

    private static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>Several apps by name: all of them when they're few, otherwise how many.</summary>
    public static string Names(IReadOnlyList<DeviceApp> apps) => apps.Count <= 3 ? string.Join(", ", apps.Select(a => a.Name)) : $"{apps.Count} apps";

    /// <summary>An app as a job's step: winget installs it for everyone, or brings it up to date, and what comes with it follows.</summary>
    public static JobStep Step(DeviceApp app) => new()
    {
        Title = $"Install {app.Name}", Language = DeviceScripts.PowerShell, Minutes = Minutes,
        Text = DeviceScripts.Clean(Install(app) + (app.Extensions.Count > 0 ? "\n" + AddExtensions(app.Extensions) : "")),
    };

    // winget, found where Windows keeps it (SYSTEM has none on its path), with the name and the ID as quoted strings.
    // Already there and up to date is fine, and so is a restart still to come; anything else fails the step with
    // winget's own code. Lines of the progress bar are left out of what it wrote.
    private static string Install(DeviceApp app) => $$"""
        $id = {{Q(app.Winget)}}
        $name = {{Q(app.Name)}}
        $apps = Join-Path $env:ProgramFiles 'WindowsApps'
        $winget = Get-ChildItem (Join-Path $apps 'Microsoft.DesktopAppInstaller_*_*__8wekyb3d8bbwe\winget.exe') -ErrorAction SilentlyContinue |
            Sort-Object { [version]($_.Directory.Name -replace '^Microsoft\.DesktopAppInstaller_([\d.]+)_.*$', '$1') } | Select-Object -Last 1
        if (-not $winget) { 'winget (App Installer) is not on this computer. Windows 10, Windows 11 and Windows Server 2025 come with it.'; exit 1 }
        # Run from there, winget wants the Visual C++ runtime that sits beside it.
        $arch = ($winget.Directory.Name -split '_')[2]
        $runtime = Get-ChildItem (Join-Path $apps "Microsoft.VCLibs.140.00.UWPDesktop_*_${arch}__8wekyb3d8bbwe") -Directory -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
        if ($runtime) { $env:Path = "$($runtime.FullName);$env:Path" }
        "Installing $name ($id) for everyone on this computer."
        $said = & $winget.FullName install --id $id --exact --source winget --scope machine --silent --accept-package-agreements --accept-source-agreements 2>&1
        $code = $LASTEXITCODE
        $said | ForEach-Object { "$_".Trim() } | Where-Object { $_ -match '[A-Za-z]{3}' }
        if ($code -eq 0) { "$name is installed." }
        elseif ($code -eq -1978335189 -or $code -eq -1978335135) { "$name is already installed, and winget has no newer version." }
        elseif ($code -eq -1978334967) { "$name is installed. Windows needs a restart to finish." }
        else {
            $why = switch ($code) {
                -1978335212 { "winget has no package with the ID $id." }
                -1978335216 { 'The package has no installer that installs for every user on this computer.' }
                -1978334975 { "$name is running. Close it, then install again." }
                -1978334974 { 'Another install is running on this computer. Try again in a few minutes.' }
                -1978334966 { 'Windows needs a restart before this can be installed.' }
                -1073741515 { 'winget could not start: the Visual C++ runtime it needs is missing on this computer.' }
                default { '' }
            }
            "winget ended with exit code $code (0x$('{0:X8}' -f $code)). $why".Trim()
            exit 1
        }
        """;

    // VS Code keeps extensions per person, so SYSTEM can't add one for them. A scheduled task runs VS Code's own
    // "code --install-extension" as each person: now for whoever is signed in, and for everyone else as they sign in.
    // Once per person (a mark in their own registry), so an extension someone removes later stays removed.
    private static string AddExtensions(IReadOnlyList<VsCodeExtension> extensions) => $$"""
        $cli = Join-Path $env:ProgramFiles 'Microsoft VS Code\bin\code.cmd'
        if (-not (Test-Path $cli)) { "VS Code is not under $env:ProgramFiles, so its extensions were not set up."; exit 1 }
        $extensions = @({{string.Join(", ", extensions.Select(e => Q(e.Id)))}})
        $key = 'HKCU\Software\Rabbid Lab\VS Code extensions'
        $each = foreach ($e in $extensions) { "(reg query `"$key`" /v $e >nul 2>&1 || (call `"$cli`" --install-extension $e >nul 2>&1 && reg add `"$key`" /v $e /d installed /f >nul))" }
        $task = 'Rabbid Lab VS Code extensions'
        try {
            $action = New-ScheduledTaskAction -Execute (Join-Path $env:windir 'System32\conhost.exe') -Argument ('--headless cmd.exe /d /c "' + ($each -join ' & ') + '"')
            $trigger = New-ScheduledTaskTrigger -AtLogOn
            $people = New-ScheduledTaskPrincipal -GroupId 'S-1-5-32-545' -RunLevel Limited
            $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances Parallel -ExecutionTimeLimit (New-TimeSpan -Minutes 30)
            Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Principal $people -Settings $settings -Force -ErrorAction Stop | Out-Null
        } catch {
            "The task that adds VS Code extensions for each person could not be made: $($_.Exception.Message.Trim())"
            exit 1
        }
        "Each person who signs in to this computer gets $($extensions -join ', ') in VS Code, once."
        if (Get-Process -Name explorer -ErrorAction SilentlyContinue) {
            Start-ScheduledTask -TaskName $task
            # Whoever is signed in gets it now: up to two minutes for that.
            $until = (Get-Date).AddMinutes(2)
            do { Start-Sleep -Seconds 3 } while ((Get-ScheduledTask -TaskName $task).State -eq 'Running' -and (Get-Date) -lt $until)
            foreach ($e in $extensions) {
                $has = @(Get-Item (Join-Path $env:SystemDrive "Users\*\.vscode\extensions\$e-*") -ErrorAction SilentlyContinue | Where-Object { $_.PSIsContainer } |
                    ForEach-Object { $_.Parent.Parent.Parent.Name } | Sort-Object -Unique)
                if ($has) { "$e is installed for $($has -join ', ')." } else { "$e is not installed for anyone yet. It installs when they sign in." }
            }
        } else { 'Nobody is signed in right now, so it installs when they sign in.' }
        """;
}

/// <summary>
/// The apps Rabbid Lab comes with: ones people put on computers all the time. Each ID was checked against the winget
/// repository, and each has an installer for every user (an .msi or a setup that installs to Program Files).
/// </summary>
public static class BuiltInApps
{
    public const string Prefix = "builtin-";

    public static bool Is(string? id) => id is not null && id.StartsWith(Prefix, StringComparison.Ordinal);

    private static DeviceApp A(string id, string name, string publisher, string category, string winget, string shows, string about, params VsCodeExtension[] extensions) => new()
    {
        Id = Prefix + id, Name = name, Publisher = publisher, Category = category, Winget = winget, About = about, BuiltIn = true, Extensions = extensions,
        Pattern = new Regex(shows, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        Changed = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc),
    };

    public static readonly IReadOnlyList<DeviceApp> All =
    [
        A("chrome", "Google Chrome", "Google", "Browsers", "Google.Chrome", "^Google Chrome$", "Web browser."),
        A("firefox", "Mozilla Firefox", "Mozilla", "Browsers", "Mozilla.Firefox", "^Mozilla Firefox", "Web browser."),
        A("vscode", "Visual Studio Code", "Microsoft", "Developer tools", "Microsoft.VisualStudioCode", "^Microsoft Visual Studio Code$",
            "Code editor, with the Claude Code extension added for each person who uses the computer.", new VsCodeExtension("anthropic.claude-code", "Claude Code")),
        A("git", "Git", "The Git Development Community", "Developer tools", "Git.Git", "^Git($| version )", "Version control, with Git Bash."),
        A("github-cli", "GitHub CLI", "GitHub", "Developer tools", "GitHub.cli", "^GitHub CLI", "GitHub from the command line (gh)."),
        A("nodejs", "Node.js LTS", "OpenJS Foundation", "Developer tools", "OpenJS.NodeJS.LTS", @"^Node\.js$", "JavaScript runtime, the long-term support version, with npm."),
        A("notepad-plus-plus", "Notepad++", "Notepad++ Team", "Developer tools", "Notepad++.Notepad++", @"^Notepad\+\+", "Text and code editor."),
        A("winmerge", "WinMerge", "WinMerge", "Developer tools", "WinMerge.WinMerge", "^WinMerge", "Compares and merges files and folders."),
        A("putty", "PuTTY", "Simon Tatham", "Admin tools", "PuTTY.PuTTY", "^PuTTY", "SSH and serial terminal."),
        A("winscp", "WinSCP", "Martin Prikryl", "Admin tools", "WinSCP.WinSCP", "^WinSCP", "Copies files over SFTP, SCP and FTP."),
        A("mremoteng", "mRemoteNG", "Next Generation Software", "Admin tools", "mRemoteNG.mRemoteNG", "^mRemoteNG", "Remote Desktop, SSH and VNC connections in tabs."),
        A("wireshark", "Wireshark", "Wireshark Foundation", "Admin tools", "WiresharkFoundation.Wireshark", "^Wireshark", "Captures and reads network traffic."),
        A("7zip", "7-Zip", "Igor Pavlov", "Utilities", "7zip.7zip", "^7-Zip", "Opens and makes zip, 7z and other archives."),
        A("everything", "Everything", "voidtools", "Utilities", "voidtools.Everything", "^Everything( |$)", "Finds files by name straight away."),
        A("windirstat", "WinDirStat", "The WinDirStat Team", "Utilities", "WinDirStat.WinDirStat", "^WinDirStat", "Shows what fills a drive."),
        A("treesize", "TreeSize Free", "JAM Software", "Utilities", "JAMSoftware.TreeSize.Free", "^TreeSize Free", "Folder sizes on a drive."),
        A("crystaldiskinfo", "CrystalDiskInfo", "Crystal Dew World", "Utilities", "CrystalDewWorld.CrystalDiskInfo", "^CrystalDiskInfo", "Drive health from SMART."),
        A("greenshot", "Greenshot", "Greenshot", "Utilities", "Greenshot.Greenshot", "^Greenshot", "Screenshots with notes drawn on them."),
        A("sharex", "ShareX", "ShareX Team", "Utilities", "ShareX.ShareX", "^ShareX", "Screenshots and screen recording."),
        A("keepassxc", "KeePassXC", "KeePassXC Team", "Utilities", "KeePassXCTeam.KeePassXC", "^KeePassXC", "Password manager that keeps its file on the computer."),
        A("acrobat-reader", "Adobe Acrobat Reader", "Adobe", "Office", "Adobe.Acrobat.Reader.64-bit", "^Adobe Acrobat", "Reads, signs and prints PDF files."),
        A("libreoffice", "LibreOffice", "The Document Foundation", "Office", "TheDocumentFoundation.LibreOffice", "^LibreOffice", "Documents, spreadsheets and slides."),
        A("thunderbird", "Mozilla Thunderbird", "Mozilla", "Office", "Mozilla.Thunderbird", "^Mozilla Thunderbird", "Mail and calendar."),
        A("zoom", "Zoom Workplace", "Zoom", "Office", "Zoom.Zoom", @"^Zoom( Workplace)?( ?\(|$)", "Meetings and chat."),
        A("vlc", "VLC media player", "VideoLAN", "Media", "VideoLAN.VLC", "^VLC media player", "Plays most video and audio files."),
        A("paint-net", "paint.net", "dotPDN", "Media", "dotPDN.PaintDotNet", @"^paint\.net", "Picture and photo editor."),
        A("vcredist-x64", "Visual C++ Redistributable (x64)", "Microsoft", "Runtimes", "Microsoft.VCRedist.2015+.x64", @"^Microsoft Visual C\+\+ (2015-20\d\d|v14) Redistributable \(x64\)",
            "The Visual C++ 2015 to 2022 runtime that many programs need."),
        A("dotnet-desktop-8", ".NET Desktop Runtime 8", "Microsoft", "Runtimes", "Microsoft.DotNet.DesktopRuntime.8", @"^Microsoft Windows Desktop Runtime - 8\.", "Runs Windows apps built on .NET 8."),
    ];

    public static DeviceApp? Find(string? id) => Is(id) ? All.FirstOrDefault(a => a.Id == id) : null;
}
