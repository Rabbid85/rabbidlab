<#
.SYNOPSIS
Takes the Rabbid Lab client off a computer when its server is gone.

.DESCRIPTION
The client can only be uninstalled when its Rabbid Lab server allows it (Devices, Uninstall now) or with the
computer's uninstall code. If the server was removed first, nothing can allow it any more. This script clears the
lock and uninstalls the client. It needs administrator rights on the computer, and it leaves a line in the
computer's Application event log.

If the server still exists, use Uninstall now in Devices instead.

Three ways to run it:
  - On the computer itself, in PowerShell started as administrator, with no parameters.
  - From one computer for many, with -ComputerName (PowerShell remoting has to be on there).
  - As a Group Policy startup script, which also gets computers that are off now, the next time they start.
    A computer without the client is left alone, so the policy can be linked to all of them.

If Rabbid Lab's own "Rabbid Lab Client" policy is still in Group Policy Management, delete it first. It installs the
client again on every computer that doesn't have it.

.PARAMETER ComputerName
Computers to do it on from here. The ones that don't answer are listed as Not reached.

.PARAMETER Check
Changes nothing. Only says whether the client is installed, whether it's locked and which server it points at.

.PARAMETER Report
A folder that gets one small text file per computer saying how it went, such as a share computers can write to.
For the startup script, to see which computers are done. Not used with -ComputerName, which lists the results itself.

.EXAMPLE
.\Remove-RabbidLabClient.ps1

.EXAMPLE
.\Remove-RabbidLabClient.ps1 -ComputerName (Get-Content .\computers.txt)

.EXAMPLE
.\Remove-RabbidLabClient.ps1 -ComputerName PC01, PC02 -Check
#>
[CmdletBinding()]
param(
    [string[]]$ComputerName,
    [switch]$Check,
    [string]$Report
)

# Everything done on one computer. Sent as it is to the others with -ComputerName.
$work = {
    param([bool]$Check, [string]$Report)
    $ErrorActionPreference = 'Stop'
    $keyPath = 'SOFTWARE\Rabbid Lab\Client'
    $data = Join-Path $env:ProgramData 'Rabbid Lab Client'
    $out = [pscustomobject]@{ Computer = $env:COMPUTERNAME; Result = ''; Server = ''; Detail = '' }
    try {
        # The 64-bit registry, whichever PowerShell this is.
        $hklm = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)

        # The client in Windows' list of installed programs.
        $product = $null
        $list = $hklm.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')
        foreach ($name in $list.GetSubKeyNames()) {
            if ($name -notlike '{*') { continue }
            $entry = $list.OpenSubKey($name)
            if ($entry.GetValue('DisplayName') -eq 'Rabbid Lab Client' -and $entry.GetValue('WindowsInstaller') -eq 1) { $product = $name }
            $entry.Close()
        }
        $list.Close()

        $locked = $false
        $key = $hklm.OpenSubKey($keyPath)
        if ($key) {
            $out.Server = [string]$key.GetValue('Server')
            $locked = $key.GetValue('RemovalAllowed') -ne 'yes'
            $key.Close()
        }

        if ($Check) {
            $out.Result = if (-not $product) { 'Not installed' } elseif ($locked) { 'Installed, locked' } else { 'Installed, not locked' }
            return $out
        }

        $me = [Security.Principal.WindowsIdentity]::GetCurrent()
        if (-not ([Security.Principal.WindowsPrincipal]$me).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'This needs administrator rights. Start PowerShell with Run as administrator.'
        }

        if ($product) {
            # The service first: while it runs, it locks the uninstall again every minute.
            $service = Get-Service RabbidLabClient -ErrorAction SilentlyContinue
            if ($service) {
                if ($service.Status -eq 'StartPending') {
                    try { $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(30)) } catch { }
                    $service.Refresh()
                }
                if ($service.Status -ne 'Stopped') {
                    Stop-Service -InputObject $service -Force
                    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
                }
            }
            # The tray icons too, so nothing holds the program's file open.
            Get-Process RabbidLab-Client -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

            # What ties the client to its server goes, then the mark its installer looks for is set.
            $key = $hklm.CreateSubKey($keyPath)
            foreach ($name in 'LastContact', 'UninstallCode', 'UnlockAfterDays', 'CodeUnlockedUntil', 'CodeUsed', 'RemovalUntil') { $key.DeleteValue($name, $false) }
            $key.SetValue('RemovalAllowed', 'yes', [Microsoft.Win32.RegistryValueKind]::String)
            $key.Close()
            Remove-Item (Join-Path $data 'identity.dat') -Force -ErrorAction SilentlyContinue

            $msi = Start-Process (Join-Path $env:SystemRoot 'System32\msiexec.exe') -ArgumentList "/x $product /qn /norestart" -Wait -PassThru
            # 0 is done, 3010 is done and wants a restart, 1605 is that it was gone already.
            if ($msi.ExitCode -notin 0, 3010, 1605) {
                throw "Windows Installer returned $($msi.ExitCode). The client is unlocked now: uninstall it from Installed apps."
            }
            $out.Result = 'Removed'
            if ($msi.ExitCode -eq 3010) { $out.Detail = 'Windows wants a restart to finish' }
            try {
                if (-not [Diagnostics.EventLog]::SourceExists('Rabbid Lab Client')) { [Diagnostics.EventLog]::CreateEventSource('Rabbid Lab Client', 'Application') }
                [Diagnostics.EventLog]::WriteEntry('Rabbid Lab Client', "The Rabbid Lab client was removed from this computer with Remove-RabbidLabClient.ps1 by $($me.Name). It was connected to $($out.Server).", 'Warning', 4101)
            }
            catch { }
        }
        else {
            $out.Result = 'Not installed'
        }

        # What the client's installer keeps for a reinstall: it points at the server that's gone.
        $hklm.DeleteSubKeyTree($keyPath, $false)
        $vendor = $hklm.OpenSubKey('SOFTWARE\Rabbid Lab')
        if ($vendor) {
            $empty = $vendor.SubKeyCount -eq 0 -and $vendor.ValueCount -eq 0
            $vendor.Close()
            if ($empty) { $hklm.DeleteSubKey('SOFTWARE\Rabbid Lab', $false) }
        }
        Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
    }
    catch {
        $out.Result = 'Failed'
        $out.Detail = "$($_.Exception.Message)".Trim()
    }

    if ($Report) {
        # One file per computer. A computer that never had the client doesn't write over what an earlier run said.
        try {
            $file = Join-Path $Report "$env:COMPUTERNAME.txt"
            if ($out.Result -ne 'Not installed' -or -not (Test-Path $file)) {
                "$(Get-Date -Format s)`t$($out.Result)`t$($out.Detail)" | Set-Content $file -Encoding UTF8
            }
        }
        catch { }
    }
    $out
}

if (-not $ComputerName) {
    & $work $Check.IsPresent $Report
    return
}

$failed = @()
$results = @(Invoke-Command -ComputerName $ComputerName -ScriptBlock $work -ArgumentList $Check.IsPresent, '' -ErrorAction SilentlyContinue -ErrorVariable failed)
$results | Select-Object Computer, Result, Server, Detail
foreach ($name in $ComputerName | Where-Object { $_ -notin $results.PSComputerName }) {
    # Off, or PowerShell remoting isn't on there: the first line of what Windows said.
    $why = $failed | Where-Object { "$($_.TargetObject)" -eq $name } | Select-Object -First 1
    $detail = if ($why) { ("$($why.Exception.Message)" -split "`r?`n")[0].Trim() } else { 'No answer' }
    [pscustomobject]@{ Computer = $name; Result = 'Not reached'; Server = ''; Detail = $detail }
}
