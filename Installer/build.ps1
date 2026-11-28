# Builds the Rabbid Lab installer, artifacts\RabbidLab-Setup-<version>.exe, and its uninstaller:
#   1. publishes the console with its own copy of .NET (so the computer needs nothing else installed),
#   2. zips it, and
#   3. builds Setup (Installer\Setup, a small WPF program on .NET Framework 4.7.2) with the zip inside it.
# -Stage Console does step 1 only, -Stage Setup steps 2 and 3 (from artifacts\publish), so the console can be
# code-signed in between, when there's something to sign it with. The default does all three.
# -Stage Client builds only the client program, and -KeepClient has -Stage Console put that one (signed by then) in the
# client's MSI instead of building it again.
# -Dev builds Rabbid Lab Dev instead (Installer\Setup\Edition.cs), which installs beside the release.
# -Preview builds artifacts\setup-preview\RabbidLab-Setup.exe instead: the same window, no administrator prompt and
# nothing inside it. Pictures of every page: artifacts\setup-preview\RabbidLab-Setup.exe --snapshot <folder>
param([switch]$Preview, [switch]$Dev, [switch]$Release, [ValidateSet('All', 'Console', 'Setup', 'Client')][string]$Stage = 'All', [switch]$KeepClient)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$server = Join-Path $root 'Src\RabbidLab.Server'
$setup = Join-Path $PSScriptRoot 'Setup\RabbidLab.Setup.csproj'
$artifacts = Join-Path $root 'artifacts'
$version = (Select-Xml -Path (Join-Path $server 'RabbidLab.Server.csproj') -XPath '//Version').Node.InnerText
# The dev build's switch, and what goes in its names (its own folders, so the two builds never mix).
$edition = if ($Dev) { '-p:Dev=true' } else { '-p:Dev=false' }
# A build made here is a test build unless it's a release (-Release, or on GitHub): updates know it isn't the release with its number.
$test = if ($Release -or $env:GITHUB_ACTIONS -eq 'true') { '-p:TestBuild=false' } else { '-p:TestBuild=true' }
$name = if ($Dev) { 'RabbidLab-Dev' } else { 'RabbidLab' }
$suffix = if ($Dev) { '-dev' } else { '' }
$publish = Join-Path $artifacts "publish$suffix"

if ($Preview) {
    dotnet build $setup --no-incremental -c Release -p:Preview=true $edition -p:Version=$version -o (Join-Path $artifacts "setup-preview$suffix")
    if ($LASTEXITCODE) { throw 'Building Setup failed.' }
    return
}

$client = Join-Path $root 'Src\Client'
$clientOut = Join-Path $artifacts "client$suffix"
if ($Stage -eq 'Client') {
    if (Test-Path $clientOut) { Remove-Item $clientOut -Recurse -Force }
    dotnet build (Join-Path $client 'RabbidLab.Client\RabbidLab.Client.csproj') --no-incremental -c Release -p:Version=$version -o $clientOut
    if ($LASTEXITCODE) { throw 'Building the client failed.' }
    "Client built to $clientOut"
    return
}

if ($Stage -ne 'Setup') {
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    # Restored first, then published without restoring again (the publish's own restore sometimes lost its win-x64 target).
    # -p:Version gives every file of ours the same version, the update library included.
    dotnet restore $server -r win-x64 -p:SelfContained=true $edition -p:Version=$version
    if ($LASTEXITCODE) { throw 'Restoring the console failed.' }
    foreach ($try in 1, 2) {
        dotnet publish $server -c Release -r win-x64 --self-contained --no-restore -o $publish -p:DebugType=None -p:DebugSymbols=false $edition $test -p:Version=$version
        if (-not $LASTEXITCODE) { break }
        if ($try -eq 2) { throw 'Publishing the console failed.' }
        'Publishing the console failed; restoring and trying once more.'
        dotnet restore $server -r win-x64 -p:SelfContained=true --force $edition -p:Version=$version
    }
    # The client for the computers that connect to this server (Src\Client): its MSI and the Group Policy template, inside
    # the console, which hands them out (Client\), and beside the installer for the release.
    if (-not $KeepClient) {
        if (Test-Path $clientOut) { Remove-Item $clientOut -Recurse -Force }
        dotnet build (Join-Path $client 'RabbidLab.Client\RabbidLab.Client.csproj') --no-incremental -c Release -p:Version=$version -o $clientOut
        if ($LASTEXITCODE) { throw 'Building the client failed.' }
    } elseif (-not (Test-Path (Join-Path $clientOut 'RabbidLab-Client.exe'))) {
        throw "No client to put in the MSI: build.ps1 -Stage Client first ($clientOut is missing)."
    }
    # The client's file version must be this release's. 1.1.444 shipped an MSI left over from a build by hand, whose
    # RabbidLab-Client.exe said 0.0.0.0: on a computer with 1.1.443, Windows Installer took the old version off and would
    # not put a lower one in its place, so the update removed the client. So the MSI is never one built before (its bin
    # and obj go first, and nothing is reused), and what is in it is read back before anything is published.
    $made = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $clientOut 'RabbidLab-Client.exe')).FileVersion
    if ($made -ne "$version.0") { throw "The client's file version is $made, not $version.0: it would not replace an older client. Nothing was published." }
    foreach ($old in 'bin', 'obj') { $p = Join-Path $client "Installer\$old"; if (Test-Path $p) { Remove-Item $p -Recurse -Force } }
    dotnet build (Join-Path $client 'Installer\RabbidLab.Client.Installer.wixproj') --no-incremental -c Release -p:Version=$version "-p:ClientDir=$clientOut" -o (Join-Path $clientOut 'msi')
    if ($LASTEXITCODE) { throw 'Building the client MSI failed.' }
    # Read back in a process of its own: when it ends nothing holds the MSI, which is copied and cleared away below.
    $reader = Join-Path $clientOut 'read-msi.ps1'
    Set-Content -Path $reader -Encoding ASCII -Value @'
param([string] $Msi)
$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($Msi, 0))
foreach ($ask in 'SELECT `Version` FROM `File` WHERE `File` = ''ClientExe''', 'SELECT `Value` FROM `Property` WHERE `Property` = ''ProductVersion''') {
    $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @($ask))
    [void]$view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
    $row = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    if ($row) { $row.GetType().InvokeMember('StringData', 'GetProperty', $null, $row, 1) } else { '' }
}
'@
    $read = @(powershell.exe -NoProfile -ExecutionPolicy Bypass -File $reader (Join-Path $clientOut 'msi\RabbidLab-Client.msi'))
    $inMsi = @{ exe = "$($read[0])"; product = "$($read[1])" }
    if ($inMsi.exe -ne "$version.0" -or $inMsi.product -ne $version) {
        throw "The client MSI holds RabbidLab-Client.exe $($inMsi.exe) as product $($inMsi.product), not ${version}: it would not replace an older client. Nothing was published."
    }
    "Client MSI: RabbidLab-Client.exe $($inMsi.exe), product $($inMsi.product)"
    $handout = Join-Path $publish 'Client'
    New-Item -ItemType Directory -Force $handout | Out-Null
    Copy-Item (Join-Path $clientOut 'msi\RabbidLab-Client.msi') (Join-Path $handout 'RabbidLab-Client.msi') -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $policyZip = Join-Path $handout 'RabbidLab-Client-Policy.zip'
    if (Test-Path $policyZip) { Remove-Item $policyZip -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $client 'Policy'), $policyZip)
    Copy-Item (Join-Path $handout 'RabbidLab-Client.msi') (Join-Path $artifacts "RabbidLab-Client-$version.msi") -Force
    Copy-Item $policyZip (Join-Path $artifacts "RabbidLab-Client-Policy-$version.zip") -Force
    Remove-Item $clientOut -Recurse -Force
    if ($Stage -eq 'Console') {
        "Console published to $publish"
        return
    }
}

if (-not (Test-Path $publish)) { throw "Nothing to package: build.ps1 -Stage Console first ($publish is missing)." }
# An open Setup window locks the installer, so the build can't replace it.
if (Get-Process | Where-Object { $_.Name -like "$name-Setup*" -or $_.Name -like "$name-Uninstall*" }) {
    throw 'Close any open Rabbid Lab Setup window, then build again.'
}

$payload = Join-Path $artifacts "payload$suffix.zip"
if (Test-Path $payload) { Remove-Item $payload -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publish, $payload, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$out = Join-Path $artifacts "setup$suffix"
dotnet build $setup --no-incremental -c Release $edition -p:Version=$version "-p:PayloadZip=$payload" -o $out
if ($LASTEXITCODE) { throw 'Building Setup failed.' }
# The uninstaller on its own: the same program without the console inside, which only removes.
$removerOut = Join-Path $artifacts "uninstaller$suffix"
dotnet build $setup --no-incremental -c Release -p:Uninstaller=true $edition -p:Version=$version -o $removerOut
if ($LASTEXITCODE) { throw 'Building the uninstaller failed.' }
$remover = Join-Path $artifacts "$name-Uninstall-$version.exe"
Copy-Item (Join-Path $removerOut "$name-Uninstall.exe") $remover -Force

$exe = Join-Path $artifacts "$name-Setup-$version.exe"
Copy-Item (Join-Path $out "$name-Setup.exe") $exe -Force

# Leave only this version's installer and uninstaller.
foreach ($leftover in $out, $removerOut, $publish, $payload) {
    if (Test-Path $leftover) { Remove-Item $leftover -Recurse -Force }
}
Get-ChildItem $artifacts -File | Where-Object { $_.Name -like "$name-Setup-*.exe" -or $_.Name -like "$name-Uninstall-*.exe" } |
    Where-Object { $_.FullName -ne $exe -and $_.FullName -ne $remover } | Remove-Item -Force
Get-ChildItem $artifacts -File | Where-Object { ($_.Name -like 'RabbidLab-Client-*.msi' -or $_.Name -like 'RabbidLab-Client-Policy-*.zip') -and $_.Name -notlike "*-$version.*" } | Remove-Item -Force
foreach ($f in $exe, $remover, (Join-Path $artifacts "RabbidLab-Client-$version.msi")) { '{0}  ({1:N1} MB)' -f $f, ((Get-Item $f).Length / 1MB) }
