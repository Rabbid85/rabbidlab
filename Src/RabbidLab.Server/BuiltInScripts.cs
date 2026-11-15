namespace RabbidLab.Server;

/// <summary>
/// The scripts Rabbid Lab comes with: things people run on computers all the time. They're in the library for everyone
/// to run, start from or use in a workflow, but can't be changed or deleted; Duplicate makes one of your own to change.
/// </summary>
public static class BuiltInScripts
{
    public const string Prefix = "builtin-";

    public static bool Is(string? id) => id is not null && id.StartsWith(Prefix, StringComparison.Ordinal);

    private static DeviceScript S(string id, string name, string language, string about, string text, int minutes = DeviceScripts.UsualMinutes) => new()
    {
        Id = Prefix + id, Name = name, Language = language, About = about, Text = DeviceScripts.Clean(text), Minutes = minutes, BuiltIn = true,
        Changed = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc),
    };

    private const string Ps = DeviceScripts.PowerShell;
    private const string Cmd = DeviceScripts.Command;

    // SYSTEM has no winget on its path: the one Windows came with is under WindowsApps.
    private const string FindWinget = """
        $winget = Get-ChildItem "$env:ProgramFiles\WindowsApps\Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe\winget.exe" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime | Select-Object -Last 1
        if (-not $winget) { 'winget is not on this computer.'; exit 1 }
        """;

    public static readonly IReadOnlyList<DeviceScript> All =
    [
        S("clear-temp", "Clear temp files", Ps, "Empties the Windows and user temp folders.", """
            Get-ChildItem "$env:windir\Temp", 'C:\Users\*\AppData\Local\Temp' -Force -ErrorAction SilentlyContinue |
                Get-ChildItem -Force -ErrorAction SilentlyContinue |
                Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
            'Temp folders emptied.'
            """),
        S("restart-service", "Restart a service", Ps, "Restarts the print spooler. Duplicate it and change the name for another service.", """
            $name = 'Spooler'
            Restart-Service -Name $name -Force
            Get-Service -Name $name | Format-Table Name, Status -AutoSize
            """),
        S("clear-print-queue", "Clear a stuck print queue", Ps, "Stops the print spooler, deletes the jobs waiting in it, and starts it again.", """
            Stop-Service -Name Spooler -Force
            $jobs = Get-ChildItem "$env:windir\System32\spool\PRINTERS" -File -ErrorAction SilentlyContinue
            $jobs | Remove-Item -Force -ErrorAction SilentlyContinue
            Start-Service -Name Spooler
            "Cleared $(@($jobs).Count) file(s) from the print queue. The spooler is $((Get-Service Spooler).Status)."
            """),
        S("flush-dns", "Flush DNS", Cmd, "Empties the DNS cache.", "ipconfig /flushdns"),
        S("gpupdate", "Update Group Policy", Cmd, "Applies Group Policy now.", "gpupdate /force /wait:120", 5),
        S("network", "Network settings", Cmd, "Every network adapter with its addresses, gateway and DNS servers.", "ipconfig /all"),
        S("wake-on-lan", "Turn on Wake-on-LAN", Ps, "Lets the wired network card wake the computer, and turns off Fast startup, which stops many cards waking a computer that was shut down. The BIOS has to allow it too.", """
            $cards = Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.NdisPhysicalMedium -eq 14 }
            if (-not $cards) { 'No wired network card found.'; exit 1 }
            foreach ($c in $cards) {
                # The driver's own switch, where it has one, then Windows' Allow this device to wake the computer.
                Get-NetAdapterAdvancedProperty -Name $c.Name -ErrorAction SilentlyContinue |
                    Where-Object { $_.RegistryKeyword -in '*WakeOnMagicPacket', 'WakeOnMagicPacket' } |
                    ForEach-Object { Set-NetAdapterAdvancedProperty -Name $c.Name -RegistryKeyword $_.RegistryKeyword -RegistryValue 1 -NoRestart -ErrorAction SilentlyContinue }
                Set-NetAdapterPowerManagement -Name $c.Name -WakeOnMagicPacket Enabled -NoRestart -ErrorAction SilentlyContinue
                & powercfg.exe /deviceenablewake "$($c.InterfaceDescription)" | Out-Null
                "$($c.Name) ($($c.InterfaceDescription)) can wake this computer."
            }
            Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name HiberbootEnabled -Value 0 -Type DWord
            'Fast startup is off. Wake-on-LAN has to be on in the BIOS too.'
            """),
        S("signed-in", "Who is signed in", Cmd, "Everyone signed in at the screen or over Remote Desktop.", """
            quser 2>nul
            if errorlevel 1 echo Nobody is signed in.
            exit /b 0
            """),
        S("uptime", "Uptime and last restart", Ps, "When Windows last started, and whether it's waiting for a restart.", """
            $os = Get-CimInstance Win32_OperatingSystem
            $up = (Get-Date) - $os.LastBootUpTime
            "Last restart: $($os.LastBootUpTime)"
            "Up for: $($up.Days) days, $($up.Hours) hours"
            $waiting = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired',
                'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending' | Where-Object { Test-Path $_ }
            if ($waiting) { 'Windows is waiting for a restart.' } else { 'No restart waiting.' }
            """),
        S("disk-space", "Disk space", Ps, "Each drive with its size and free space.", """
            Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' |
                Select-Object DeviceID, @{n='SizeGB';e={[math]::Round($_.Size / 1GB)}}, @{n='FreeGB';e={[math]::Round($_.FreeSpace / 1GB)}} |
                Format-Table -AutoSize
            """),
        S("disk-health", "Disk health", Ps, "Each physical disk and what Windows says about its health.", """
            Get-PhysicalDisk | Select-Object FriendlyName, MediaType, HealthStatus, OperationalStatus, @{n='SizeGB';e={[math]::Round($_.Size / 1GB)}} |
                Format-Table -AutoSize
            """),
        S("local-admins", "Local administrators", Ps, "Who is in the Administrators group.", """
            Get-LocalGroupMember -SID 'S-1-5-32-544' | Format-Table Name, ObjectClass, PrincipalSource -AutoSize
            """),
        S("recent-updates", "Updates in the last 30 days", Ps, "The Windows updates installed in the last 30 days.", """
            Get-HotFix | Where-Object { $_.InstalledOn -gt (Get-Date).AddDays(-30) } | Sort-Object InstalledOn -Descending |
                Format-Table HotFixID, Description, InstalledOn -AutoSize
            """),
        S("system-errors", "Errors in the System log", Ps, "The last 20 errors in the System event log from the past day.", """
            Get-WinEvent -FilterHashtable @{ LogName = 'System'; Level = 1, 2; StartTime = (Get-Date).AddDays(-1) } -MaxEvents 20 -ErrorAction SilentlyContinue |
                Format-Table TimeCreated, ProviderName, Id, Message -Wrap
            """),
        S("defender-scan", "Defender quick scan", Ps, "Updates Microsoft Defender and runs a quick scan.", """
            Update-MpSignature
            Start-MpScan -ScanType QuickScan
            Get-MpComputerStatus | Format-List AntivirusSignatureLastUpdated, QuickScanEndTime
            """, 60),
        S("winget-install", "Install an app with winget", Ps, "Installs Google Chrome for everyone. Duplicate it and change the id for another app.", $"""
            $id = 'Google.Chrome'
            {FindWinget}
            & $winget.FullName install --id $id --exact --silent --scope machine --accept-package-agreements --accept-source-agreements
            exit $LASTEXITCODE
            """, 30),
        S("winget-uninstall", "Uninstall an app with winget", Ps, "Uninstalls an app by its winget id. Duplicate it and change the id.", $"""
            $id = 'Valve.Steam'
            {FindWinget}
            & $winget.FullName uninstall --id $id --exact --silent --accept-source-agreements
            exit $LASTEXITCODE
            """, 30),
    ];

    public static DeviceScript? Find(string? id) => Is(id) ? All.FirstOrDefault(s => s.Id == id) : null;
}
