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

    // The computer's domain for nltest, or a plain stop on a computer that isn't in one.
    private const string FindDomain = """
        $cs = Get-CimInstance Win32_ComputerSystem
        if (-not $cs.PartOfDomain) { "$env:COMPUTERNAME isn't in a domain (workgroup $($cs.Domain))."; exit 1 }
        $domain = $cs.Domain
        """;

    // Whether the computer's trust with the domain works, first as one line, then nltest's own answer.
    private const string SecureChannel = """
        $ok = Test-ComputerSecureChannel -ErrorAction SilentlyContinue
        "Secure channel to ${domain}: $(if ($ok) { 'healthy' } else { 'BROKEN' })"
        & nltest.exe "/sc_query:$domain"
        """;

    public static readonly IReadOnlyList<DeviceScript> All =
    [
        S("windows-update-sources", "Windows Update: compare update sources", Ps,
            "Use when the missing update count differs from the Windows Update page. Lists the update settings, each update service and what it offers, with the details that decide what is counted. Read-only.", """
            $ErrorActionPreference = 'Continue'
            "== $((Get-CimInstance Win32_OperatingSystem).Caption) $([Environment]::OSVersion.Version), as $([Security.Principal.WindowsIdentity]::GetCurrent().Name)"
            '== Settings'
            foreach ($key in 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate', 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU',
                'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings', 'HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Update') {
                $at = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
                if (-not $at) { "  $key : not there"; continue }
                "  $key"
                $at.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' } | ForEach-Object { "    $($_.Name) = $($_.Value)" }
            }
            '== Services'
            $services = @((New-Object -ComObject Microsoft.Update.ServiceManager).Services)
            foreach ($s in $services) { "  $($s.ServiceID) $($s.Name): default=$($s.IsDefaultAUService) managed=$($s.IsManaged) withAU=$($s.IsRegisteredWithAU) windows=$($s.OffersWindowsUpdates)" }
            $session = New-Object -ComObject Microsoft.Update.Session
            $session.ClientApplicationID = 'Rabbid Lab'
            function Show($name, $id, $selection, $online) {
                "== $name"
                try {
                    $searcher = $session.CreateUpdateSearcher()
                    if ($id) { $searcher.ServerSelection = 3; $searcher.ServiceID = $id } elseif ($selection) { $searcher.ServerSelection = $selection }
                    $searcher.Online = $online
                    $found = $searcher.Search('IsInstalled=0 and IsHidden=0').Updates
                    "  $($found.Count) update(s)"
                    foreach ($u in $found) {
                        $kinds = ($u.Categories | ForEach-Object { $_.Name }) -join '; '
                        "  - $($u.Title)"
                        "      type=$($u.Type) autoSelect=$($u.AutoSelectOnWebSites) browseOnly=$($u.BrowseOnly) mandatory=$($u.IsMandatory) autoSelection=$($u.AutoSelection) autoDownload=$($u.AutoDownload) downloaded=$($u.IsDownloaded) action=$($u.DeploymentAction) id=$($u.Identity.UpdateID)"
                        "      $kinds"
                    }
                } catch {
                    $e = $_.Exception
                    while ($e.InnerException) { $e = $e.InnerException }
                    "  didn't answer: 0x$('{0:X8}' -f $e.HResult) $($e.Message.Trim())"
                }
            }
            Show 'The usual service, asked online' $null 0 $true
            Show 'The usual service, from what Windows already has (no network)' $null 0 $false
            foreach ($s in $services) { Show "$($s.Name), by its id" $s.ServiceID 0 $true }
            $mu = '7971f918-a847-4430-9279-4a52d1efe18d'
            if (-not ($services | Where-Object { $_.ServiceID -eq $mu })) { Show 'Microsoft Update, by its id (not registered here)' $mu 0 $true }
            Show 'Windows Update itself, past any WSUS server' $null 2 $true
            "== Windows' own scan (Windows Server)"
            try {
                $scan = Invoke-CimMethod -Namespace root/Microsoft/Windows/WindowsUpdate -ClassName MSFT_WUOperations -MethodName ScanForUpdates -Arguments @{ SearchCriteria = 'IsInstalled=0' } -ErrorAction Stop
                "  $(@($scan.Updates).Count) update(s)"
                foreach ($u in $scan.Updates) { "  - $($u.Title)   KB$($u.KBArticleID) id=$($u.UpdateID) severity=$($u.MsrcSeverity)" }
            } catch { "  didn't answer: $($_.Exception.Message.Trim())" }
            """, 20),
        S("clear-temp", "Clear temp files", Ps, "Empties the Windows and user temp folders.", """
            Get-ChildItem "$env:windir\Temp", 'C:\Users\*\AppData\Local\Temp' -Force -ErrorAction SilentlyContinue |
                Get-ChildItem -Force -ErrorAction SilentlyContinue |
                Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
            'Temp folders emptied.'
            """),
        S("restart-service", "Restart a service", Ps, "Restarts the Print Spooler service. To restart another service, duplicate the script and change the service name.", """
            $name = 'Spooler'
            Restart-Service -Name $name -Force
            Get-Service -Name $name | Format-Table Name, Status -AutoSize
            """),
        S("clear-print-queue", "Clear a stuck print queue", Ps, "Stops the Print Spooler service, deletes the queued print jobs and starts the service again.", """
            Stop-Service -Name Spooler -Force
            $jobs = Get-ChildItem "$env:windir\System32\spool\PRINTERS" -File -ErrorAction SilentlyContinue
            $jobs | Remove-Item -Force -ErrorAction SilentlyContinue
            Start-Service -Name Spooler
            "Cleared $(@($jobs).Count) file(s) from the print queue. The spooler is $((Get-Service Spooler).Status)."
            """),
        S("flush-dns", "Flush DNS", Cmd, "Clears the DNS client cache.", "ipconfig /flushdns"),
        S("gpupdate", "Update Group Policy", Cmd, "Applies Group Policy now.", "gpupdate /force /wait:120", 5),
        S("network", "Network settings", Cmd, "Lists each network adapter with its IP addresses, gateway and DNS servers.", "ipconfig /all"),
        S("wake-on-lan", "Enable Wake-on-LAN", Ps, "Allows the wired network adapter to wake the computer from sleep and shutdown, and disables Fast startup, which prevents many adapters from waking a shut-down computer. Wake-on-LAN must also be enabled in the BIOS.", """
            $cards = Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.NdisPhysicalMedium -eq 14 }
            if (-not $cards) { 'No wired network card found.'; exit 1 }
            foreach ($c in $cards) {
                "$($c.Name) ($($c.InterfaceDescription)):"
                # The card's own switches, which each maker names its own way: wake on a magic packet, from sleep and from
                # shut down (Realtek: Shutdown Wake-On-Lan; Intel: ...from power off state; PME on some others).
                Get-NetAdapterAdvancedProperty -Name $c.Name -ErrorAction SilentlyContinue |
                    Where-Object { $_.DisplayName -match 'wake on magic packet|shutdown wake|wake from (power off|shutdown)|power off state|enable pme' -and $_.DisplayName -notmatch 'speed' } |
                    ForEach-Object {
                        $on = $_.ValidDisplayValues | Where-Object { $_ -match '^(Enabled|On)$' } | Select-Object -First 1
                        if (-not $on) { return }
                        if ($_.DisplayValue -eq $on) { "  $($_.DisplayName): already on"; return }
                        Set-NetAdapterAdvancedProperty -Name $c.Name -DisplayName $_.DisplayName -DisplayValue $on -NoRestart -ErrorAction SilentlyContinue
                        "  $($_.DisplayName): turned on"
                    }
                # Then Windows' Allow this device to wake the computer.
                Set-NetAdapterPowerManagement -Name $c.Name -WakeOnMagicPacket Enabled -NoRestart -ErrorAction SilentlyContinue
                & powercfg.exe /deviceenablewake "$($c.InterfaceDescription)" | Out-Null
                '  Allowed to wake the computer.'
            }
            Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name HiberbootEnabled -Value 0 -Type DWord
            'Fast startup is off.'
            'The BIOS has to allow it too: waking from the network on (often Power On By PCI-E, or Wake on LAN), and ErP or deep sleep off.'
            """),
        S("secure-channel", "Domain trust (secure channel)", Ps, "Tests the computer's secure channel with the domain, using Test-ComputerSecureChannel and nltest /sc_query. The job fails if the secure channel is broken. Read-only.", $$"""
            {{FindDomain}}
            {{SecureChannel}}
            if (-not $ok) { exit 1 }
            """),
        S("find-dc", "Domain controller in use", Ps, "Shows the domain controller and site this computer uses, from nltest /dsgetdc. Read-only.", $$"""
            {{FindDomain}}
            & nltest.exe "/dsgetdc:$domain"
            ''
            & nltest.exe /dsgetsite
            """),
        S("dc-list", "List domain controllers", Ps, "Lists each domain controller in the domain with its site and roles, from nltest /dclist. Read-only.", $$"""
            {{FindDomain}}
            & nltest.exe "/dclist:$domain"
            """),
        S("domain-trusts", "Domain trusts", Cmd, "Lists the domain's trust relationships, from nltest /domain_trusts /all_trusts. Read-only.", "nltest /domain_trusts /all_trusts"),
        S("whoami", "Script run-as account", Cmd, "Shows the account that scripts run as on this computer (SYSTEM), with its groups and privileges, from whoami. Read-only.", @"%SystemRoot%\System32\whoami.exe /user /groups /priv"),
        S("domain-report", "Domain report", Ps, "Runs all domain checks: secure channel, domain controller and site, domain controller list, trusts, and the script run-as account. Read-only.", $$"""
            {{FindDomain}}
            "== $env:COMPUTERNAME in $domain"
            ''
            '== Secure channel'
            {{SecureChannel}}
            ''
            '== Domain controller it uses'
            & nltest.exe "/dsgetdc:$domain"
            & nltest.exe /dsgetsite
            ''
            '== Every domain controller'
            & nltest.exe "/dclist:$domain"
            ''
            '== Trusts'
            & nltest.exe /domain_trusts /all_trusts
            ''
            '== Account scripts run as'
            & "$env:SystemRoot\System32\whoami.exe" /user /groups /priv
            if (-not $ok) { exit 1 }
            """, 10),
        S("signed-in", "Signed-in users", Cmd, "Lists users signed in locally or over Remote Desktop.", """
            quser 2>nul
            if errorlevel 1 echo Nobody is signed in.
            exit /b 0
            """),
        S("uptime", "Uptime and last restart", Ps, "Shows when Windows last started and whether a restart is pending.", """
            $os = Get-CimInstance Win32_OperatingSystem
            $up = (Get-Date) - $os.LastBootUpTime
            "Last restart: $($os.LastBootUpTime)"
            "Up for: $($up.Days) days, $($up.Hours) hours"
            $waiting = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired',
                'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending' | Where-Object { Test-Path $_ }
            if ($waiting) { 'Windows is waiting for a restart.' } else { 'No restart waiting.' }
            """),
        S("disk-space", "Disk space", Ps, "Lists each drive with its size and free space.", """
            Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' |
                Select-Object DeviceID, @{n='SizeGB';e={[math]::Round($_.Size / 1GB)}}, @{n='FreeGB';e={[math]::Round($_.FreeSpace / 1GB)}} |
                Format-Table -AutoSize
            """),
        S("disk-health", "Disk health", Ps, "Lists each physical disk and its health status.", """
            Get-PhysicalDisk | Select-Object FriendlyName, MediaType, HealthStatus, OperationalStatus, @{n='SizeGB';e={[math]::Round($_.Size / 1GB)}} |
                Format-Table -AutoSize
            """),
        S("local-admins", "Local administrators", Ps, "Lists the members of the local Administrators group.", """
            Get-LocalGroupMember -SID 'S-1-5-32-544' | Format-Table Name, ObjectClass, PrincipalSource -AutoSize
            """),
        S("recent-updates", "Updates in the last 30 days", Ps, "Lists Windows updates installed in the last 30 days.", """
            Get-HotFix | Where-Object { $_.InstalledOn -gt (Get-Date).AddDays(-30) } | Sort-Object InstalledOn -Descending |
                Format-Table HotFixID, Description, InstalledOn -AutoSize
            """),
        S("system-errors", "Errors in the System log", Ps, "Shows the last 20 errors in the System event log from the past 24 hours.", """
            Get-WinEvent -FilterHashtable @{ LogName = 'System'; Level = 1, 2; StartTime = (Get-Date).AddDays(-1) } -MaxEvents 20 -ErrorAction SilentlyContinue |
                Format-Table TimeCreated, ProviderName, Id, Message -Wrap
            """),
        S("defender-scan", "Defender quick scan", Ps, "Updates Microsoft Defender and runs a quick scan.", """
            Update-MpSignature
            Start-MpScan -ScanType QuickScan
            Get-MpComputerStatus | Format-List AntivirusSignatureLastUpdated, QuickScanEndTime
            """, 60),
        S("winget-install", "Install an app with winget", Ps, "Installs Google Chrome for all users. To install another app, duplicate the script and change the winget ID.", $"""
            $id = 'Google.Chrome'
            {FindWinget}
            & $winget.FullName install --id $id --exact --silent --scope machine --accept-package-agreements --accept-source-agreements
            exit $LASTEXITCODE
            """, 30),
        S("winget-uninstall", "Uninstall an app with winget", Ps, "Uninstalls an app by its winget ID. Duplicate the script and change the ID.", $"""
            $id = 'Valve.Steam'
            {FindWinget}
            & $winget.FullName uninstall --id $id --exact --silent --accept-source-agreements
            exit $LASTEXITCODE
            """, 30),
    ];

    public static DeviceScript? Find(string? id) => Is(id) ? All.FirstOrDefault(s => s.Id == id) : null;
}
