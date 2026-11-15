using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>A printer to add, remove or print a test page on, from the Printers tab or Add a printer.</summary>
/// <param name="Action">add, remove or testpage.</param>
/// <param name="Way">How to add it: ipp (an IPP address), tcpip (a TCP/IP port and a driver), lpr, share (\\server\printer) or url (a device URL).</param>
public sealed record PrinterForm(string? Action, string? Name, string? Way = null, string? Address = null, int? Port = null, string? Queue = null, string? Url = null,
    string? Share = null, string? Driver = null, string? Inf = null);

/// <summary>
/// Printers on computers, done as built-in jobs so the client needs nothing new. Each is added for everyone on the
/// computer, as SYSTEM: by IPP address with Windows' own IPP driver, a TCP/IP or LPR port with a driver, a print server
/// share (a per-computer connection, so everyone who signs in gets it), or a device URL. Every value is checked here, then
/// goes into the script as a quoted string.
/// </summary>
public static partial class PrinterActions
{
    [GeneratedRegex(@"^([A-Za-z0-9]([A-Za-z0-9\-\.]{0,251}[A-Za-z0-9])?|\[?[0-9A-Fa-f:\.]{2,45}\]?)$")]
    private static partial Regex HostShape();

    [GeneratedRegex(@"^\\\\[^\\/:*?""<>|\s][^\\/:*?""<>|]{0,253}\\[^\\/:*?""<>|]{1,256}$")]
    private static partial Regex ShareShape();

    private static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    private static bool Plain(string? s, int most) => !string.IsNullOrWhiteSpace(s) && s.Trim().Length <= most && !s.Any(char.IsControl);

    /// <summary>The step and its title for the form, or why it can't be.</summary>
    public static (JobStep? Step, string Title, string? Problem) Make(PrinterForm? p)
    {
        if (p is null || p.Action is not ("add" or "remove" or "testpage"))
        {
            return (null, "", "Say what to do with the printer.");
        }
        var name = p.Name?.Trim() ?? "";
        if (!(p.Action == "add" && p.Way == "share") && !Plain(name, 200))
        {
            return (null, "", "Give the printer a name.");
        }
        return p.Action switch
        {
            "remove" => Done($"Remove the printer {name}", 5, $$"""
                $ErrorActionPreference = 'Stop'
                $name = {{Q(name)}}
                if ($name.StartsWith('\\')) {
                    Start-Process rundll32.exe -ArgumentList "printui.dll,PrintUIEntry /gd /n`"$name`"" -Wait
                    Restart-Service -Name Spooler -Force
                    "Took $name off this computer. It goes for each person the next time they sign in."
                    exit 0
                }
                if (-not (Get-Printer -Name $name -ErrorAction SilentlyContinue)) { "There's no printer called $name on this computer."; exit 1 }
                Remove-Printer -Name $name
                "Removed $name."
                """),
            "testpage" => Done($"Print a test page on {name}", 3, $$"""
                $ErrorActionPreference = 'Stop'
                $name = {{Q(name)}}
                $printer = Get-CimInstance Win32_Printer | Where-Object { $_.Name -eq $name } | Select-Object -First 1
                if (-not $printer) { "There's no printer called $name on this computer."; exit 1 }
                $result = Invoke-CimMethod -InputObject $printer -MethodName PrintTestPage
                if ($result.ReturnValue -ne 0) { "Windows didn't send it (error $($result.ReturnValue))."; exit 1 }
                "Sent a test page to $name."
                """),
            _ => Add(p, name),
        };
    }

    private static (JobStep?, string, string?) Add(PrinterForm p, string name)
    {
        const string already = """if (Get-Printer -Name $name -ErrorAction SilentlyContinue) { "$name is already on this computer."; exit 0 }""";
        switch (p.Way)
        {
            case "ipp":
            case "url":
            {
                var url = p.Url?.Trim() ?? "";
                if (p.Way == "ipp" && url.Length == 0 && Plain(p.Address, 255) && HostShape().IsMatch(p.Address!.Trim()))
                {
                    url = $"http://{p.Address.Trim()}:631/ipp/print";
                }
                if (url.Length > 500 || url.Any(char.IsControl) || !Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https" or "ipp" or "ipps"))
                {
                    return (null, "", p.Way == "ipp" ? "Give the printer's IP address or name, or its IPP address, such as http://192.168.3.20:631/ipp/print."
                        : "Give the printer's device address, such as http://192.168.3.20:80/WebServices/Device.");
                }
                return Done($"Add the printer {name}", 5, $$"""
                    $ErrorActionPreference = 'Stop'
                    $name = {{Q(name)}}
                    {{already}}
                    Add-Printer -Name $name {{(p.Way == "ipp" ? "-IppURL" : "-DeviceURL")}} {{Q(url)}}
                    "Added $name."
                    """);
            }
            case "tcpip":
            case "lpr":
            {
                var address = p.Address?.Trim() ?? "";
                if (!Plain(address, 255) || !HostShape().IsMatch(address))
                {
                    return (null, "", "Give the printer's IP address or name, such as 192.168.3.20.");
                }
                var driver = p.Driver?.Trim() ?? "";
                if (!Plain(driver, 200))
                {
                    return (null, "", "Pick the driver it uses.");
                }
                var inf = p.Inf?.Trim() ?? "";
                if (inf.Length > 0 && (!Plain(inf, 260) || !inf.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)))
                {
                    return (null, "", @"The driver file has to be an .inf, such as \\server\drivers\hp\hpcu250u.inf.");
                }
                string port;
                string makePort;
                if (p.Way == "tcpip")
                {
                    var number = p.Port is > 0 and < 65536 ? p.Port.Value : 9100;
                    port = number == 9100 ? $"IP_{address}" : $"IP_{address}_{number}";
                    makePort = $"Add-PrinterPort -Name {Q(port)} -PrinterHostAddress {Q(address)} -PortNumber {number}";
                }
                else
                {
                    var queue = p.Queue?.Trim() ?? "";
                    if (!Plain(queue, 100))
                    {
                        return (null, "", "Give the LPR queue name, such as lp or PASSTHRU.");
                    }
                    port = $"LPR_{address}_{queue}";
                    makePort = $"Add-PrinterPort -Name {Q(port)} -LprHostAddress {Q(address)} -LprQueueName {Q(queue)} -LprByteCounting";
                }
                // The driver is on the computer already, or comes from the .inf given first.
                var fromInf = inf.Length == 0 ? "" : $$"""
                    $said = & { $ErrorActionPreference = 'Continue'; pnputil.exe /add-driver {{Q(inf)}} /install 2>&1 | Out-String }
                    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 259 -and $LASTEXITCODE -ne 3010) { $said; "pnputil couldn't add the driver file (exit $LASTEXITCODE)."; exit 1 }
                    """;
                return Done($"Add the printer {name}", 10, $$"""
                    $ErrorActionPreference = 'Stop'
                    $name = {{Q(name)}}
                    $driver = {{Q(driver)}}
                    {{already}}
                    {{fromInf}}
                    if (-not (Get-PrinterDriver -Name $driver -ErrorAction SilentlyContinue)) { Add-PrinterDriver -Name $driver }
                    if (-not (Get-PrinterPort -Name {{Q(port)}} -ErrorAction SilentlyContinue)) { {{makePort}} }
                    Add-Printer -Name $name -DriverName $driver -PortName {{Q(port)}}
                    "Added $name with $driver."
                    """);
            }
            case "share":
            {
                var share = p.Share?.Trim() ?? "";
                if (!ShareShape().IsMatch(share))
                {
                    return (null, "", @"Give the shared printer as \\server\printer.");
                }
                return Done($"Add the printer {share}", 10, $$"""
                    $ErrorActionPreference = 'Stop'
                    $share = {{Q(share)}}
                    Start-Process rundll32.exe -ArgumentList "printui.dll,PrintUIEntry /ga /n`"$share`"" -Wait
                    Restart-Service -Name Spooler -Force
                    "Added $share for everyone on this computer. Each person gets it the next time they sign in."
                    """);
            }
            default:
                return (null, "", "Say how to add the printer.");
        }
    }

    private static (JobStep?, string, string?) Done(string title, int minutes, string text) =>
        (new JobStep { Title = title, Language = DeviceScripts.PowerShell, Minutes = minutes, Text = DeviceScripts.Clean(text) }, title, null);
}
