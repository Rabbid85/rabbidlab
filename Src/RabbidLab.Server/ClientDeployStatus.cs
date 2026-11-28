using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>
/// What a computer's install log said when it was last read from that computer (Devices, Deploy the client). The server
/// fetches it, as an administrator of that computer; a computer without the client never sends anything here.
/// </summary>
public sealed class InstallLogRead
{
    public DateTime Read { get; set; }
    public string By { get; set; } = "";

    /// <summary>When the installer last wrote its log; null when the computer has none, so the installer has not run.</summary>
    public DateTime? Written { get; set; }

    /// <summary>Windows Installer's result: 0 installed, 1603 failed, and so on; null when the log does not say.</summary>
    public int? Status { get; set; }
    public string? Version { get; set; }

    /// <summary>The error Windows Installer gave, in its own words.</summary>
    public string? Reason { get; set; }

    /// <summary>The last lines of the log.</summary>
    public string? Tail { get; set; }
}

/// <summary>
/// A computer in Active Directory and the client on it. State: installed, pending (waits for approval), failed (no client,
/// and its install log says the install failed) or none.
/// </summary>
public sealed record DeployComputer(string Name, string? Os, bool Controller, DateTime? LastSignIn, string State, string? Version, DateTime? Seen, bool Online,
    InstallLogRead? Log, string? Advice);

/// <summary>A computer after its install log was read, and the text that was read (the server keeps only its last lines).</summary>
public sealed record InstallLogAnswer(DeployComputer Computer, string? Text);

/// <summary>The computer whose install log is read, and an administrator's login for it when Rabbid Lab's own account isn't one (never kept).</summary>
public sealed record InstallLogForm(string? Computer, string? Username = null, string? Password = null)
{
    public GpoLogin Login => new(Username, Password);
}

/// <summary>The end of a file on another computer, and when it was last written.</summary>
public sealed record RemoteFileEnd(byte[] Bytes, DateTime Written, long Length);

/// <summary>Files on other computers, read over the network as the login given.</summary>
public interface IRemoteFiles
{
    /// <summary>The last bytes of a file, at most this many; null when the file is not there.</summary>
    Task<RemoteFileEnd?> ReadEnd(string path, int most, string username, string password, CancellationToken stop);
}

/// <summary>Over the computer's administrative share. Windows uses the login for this server's own connection only.</summary>
public sealed class WindowsRemoteFiles : IRemoteFiles
{
    public Task<RemoteFileEnd?> ReadEnd(string path, int most, string username, string password, CancellationToken stop) => !OperatingSystem.IsWindows()
        ? throw new ToolProblem($"Reading a file on another computer requires {Product.Title} running on Windows.")
        : Task.Run(() => Windows(path, most, username, password), stop);

    [SupportedOSPlatform("windows")]
    private static RemoteFileEnd? Windows(string path, int most, string username, string password) =>
        NetworkLogon.Run(username, password, () =>
        {
            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                // From an even place, so two-byte text read from the middle stays in step.
                var from = Math.Max(0, file.Length - most);
                from += from % 2;
                file.Seek(from, SeekOrigin.Begin);
                var bytes = new byte[(int) (file.Length - from)];
                var got = 0;
                while (got < bytes.Length && file.Read(bytes, got, bytes.Length - got) is > 0 and var n)
                {
                    got += n;
                }
                return new RemoteFileEnd(got == bytes.Length ? bytes : bytes[..got], File.GetLastWriteTimeUtc(path), file.Length);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
        }, "Windows could not use those credentials.");
}

/// <summary>Windows Installer's log of a client install, as the deploy GPO has it written (msiexec /l*v), read for what went wrong.</summary>
public static partial class InstallLog
{
    /// <summary>Where the deploy GPO's task has msiexec write it, under the computer's Windows folder.</summary>
    public const string File = @"Temp\RabbidLab-Client-install.log";

    /// <summary>How much of the end of the log is read. A whole log is about 100 KB.</summary>
    public const int Most = 256 * 1024;
    private const int TailLines = 40;
    private const int LineMost = 300;

    public static InstallLogRead Parse(RemoteFileEnd? file, DateTime now, string by)
    {
        var read = new InstallLogRead { Read = now, By = by };
        if (file is null)
        {
            return read;
        }
        read.Written = file.Written;
        var lines = Decode(file.Bytes).Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0).ToList();
        if (file.Length > file.Bytes.Length && lines.Count > 0)
        {
            lines.RemoveAt(0); // read from the middle of the file: the first line is part of one
        }
        for (var i = lines.Count - 1; i >= 0 && (read.Status is null || read.Reason is null); i--)
        {
            if (read.Status is null && StatusLine().Match(lines[i]) is { Success: true } s)
            {
                read.Status = int.Parse(s.Groups["status"].Value, System.Globalization.CultureInfo.InvariantCulture);
                read.Version = s.Groups["version"].Success ? s.Groups["version"].Value : null;
            }
            // The installer's own error, not its closing "Installation failed." or "completed successfully".
            else if (read.Reason is null && ProductLine().Match(lines[i]) is { Success: true } p && !p.Groups["text"].Value.StartsWith("Installation ", StringComparison.OrdinalIgnoreCase)
                && !p.Groups["text"].Value.StartsWith("Configuration ", StringComparison.OrdinalIgnoreCase))
            {
                read.Reason = Cut(p.Groups["text"].Value, 400);
            }
        }
        if (read.Status == 0)
        {
            read.Reason = null; // an error from an earlier part of a log that ended well
        }
        read.Tail = string.Join('\n', lines.Skip(Math.Max(0, lines.Count - TailLines)).Select(l => Cut(l, LineMost)));
        return read;
    }

    /// <summary>What to do about a failed install, in a sentence, for the results this client is known to give.</summary>
    public static string? Advice(InstallLogRead? log) => log switch
    {
        null or { Written: null } or { Status: 0 } => null,
        { Reason: { } r } when r.Contains(".NET Framework", StringComparison.OrdinalIgnoreCase) => "Install .NET Framework 4.8 on this computer, then click Deploy again.",
        { Reason: { } r } when r.Contains("Error 1920", StringComparison.OrdinalIgnoreCase) =>
            $"The client service did not start. Clients before version 1.1.444 require .NET Framework 4.7.2, which Windows Server 2016 does not have. Update {Product.Title}, then click Deploy again.",
        { Status: 1618 } => "Another installation was running on this computer. Click Deploy again to retry.",
        { Status: 1619 or 1620 } => "This computer could not open the installer in SYSVOL. Check that it can reach the domain's SYSVOL share, then click Deploy again.",
        { Status: 1625 } => "A policy on this computer blocks the installation.",
        { Status: not null } => "After you fix the cause, click Deploy again. A computer only retries when the GPO changes.",
        _ => "The log does not show a result. The installation may still be running, or it was interrupted.",
    };

    // Windows Installer writes its verbose log as UTF-16; a tail read from the middle has no byte order mark.
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        var sample = Math.Min(bytes.Length, 400);
        var zeros = 0;
        for (var i = 1; i < sample; i += 2)
        {
            zeros += bytes[i] == 0 ? 1 : 0;
        }
        return sample >= 2 && zeros * 4 > sample ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
    }

    private static string Cut(string text, int most) => text.Length > most ? text[..most] + "…" : text;

    [GeneratedRegex(@"(?:Product Version: (?<version>[\d.]+?)\.? .*)?Installation success or error status: (?<status>\d+)")]
    private static partial Regex StatusLine();

    [GeneratedRegex(@"Product: [^-]+ -- (?<text>.+)$")]
    private static partial Regex ProductLine();
}

public sealed partial class DirectoryTools
{
    private const int MostLogs = 300;

    /// <summary>Other computers' files, read as an administrator of theirs (the install log). Replaced in tests.</summary>
    public IRemoteFiles Remote { get; set; } = new WindowsRemoteFiles();

    /// <summary>
    /// Every enabled computer in the domain, which is what the deploy GPO applies to, with the client on it if it has
    /// one and what its install log said if someone read it. Nothing is asked of any computer here.
    /// </summary>
    public async Task<List<DeployComputer>> DeployComputers(IReadOnlyList<Client> clients, Func<Client, bool> online, CancellationToken stop)
    {
        List<LdapRow> rows;
        using (var ldap = await Open(stop))
        {
            rows = await ldap.Find(ldap.BaseDn, "(objectCategory=computer)", true, ["cn", "operatingSystem", "lastLogonTimestamp", "userAccountControl"], Limit, stop);
        }
        var logs = Deployed().Logs;
        var byName = clients.GroupBy(c => c.Computer, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.LastSeen).First(), StringComparer.OrdinalIgnoreCase);
        return [.. rows.Select(r =>
        {
            var name = r.Text("cn") ?? Ldap.Name(r.Dn);
            var flags = r.Number("userAccountControl") ?? 0x1000;
            if ((flags & Disabled) != 0)
            {
                return null;
            }
            var client = byName.GetValueOrDefault(name);
            var log = logs?.GetValueOrDefault(name.ToUpperInvariant());
            var state = client is { Approved: not null } ? "installed" : client is not null ? "pending" : log is { Status: not null and not 0 } ? "failed" : "none";
            return new DeployComputer(name, r.Text("operatingSystem"), (flags & 0x2000) != 0, FileTime(r.Number("lastLogonTimestamp")), state, client?.Version, client?.LastSeen,
                client is not null && online(client), log, client is null ? InstallLog.Advice(log) : null);
        }).OfType<DeployComputer>().OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Reads the end of the client's install log from a computer's administrative share and keeps what it says. As
    /// Rabbid Lab's own account, or the login given when that account is not an administrator there; the login is used
    /// for this one connection and never kept. Only a computer Active Directory has is connected to, by the name
    /// Active Directory has for it, so a login is never sent to a name someone typed.
    /// </summary>
    public async Task<InstallLogAnswer> ReadInstallLog(string computer, GpoLogin login, string by, IReadOnlyList<Client> clients, Func<Client, bool> online, CancellationToken stop)
    {
        var f = Ready();
        var asked = computer.Trim().TrimEnd('$');
        if (asked.Length == 0)
        {
            throw new ToolProblem("Select a computer.");
        }
        string name;
        string host;
        using (var ldap = await Open(stop))
        {
            var row = (await ldap.Find(ldap.BaseDn, $"(&(objectCategory=computer)(sAMAccountName={ActiveDirectory.Escape(asked)}$))", true, ["cn", "dNSHostName"], 1, stop)).FirstOrDefault()
                ?? throw new ToolProblem($"The computer {asked} no longer exists in Active Directory.", StatusCodes.Status404NotFound);
            name = row.Text("cn") ?? asked;
            host = row.Text("dNSHostName") is { Length: > 0 } dns ? dns : name;
        }
        const string needsAdmin = "Enter the login of an account that is an administrator on this computer, such as a domain admin. It is used once to read the log and is not saved.";
        if (!login.Given && f.ReadOnly)
        {
            throw new GpoNeedsLogin(needsAdmin);
        }
        var (user, password) = SysvolLogin(f, login);
        RemoteFileEnd? file;
        try
        {
            file = await Remote.ReadEnd($@"\\{host}\ADMIN$\{InstallLog.File}", InstallLog.Most, user, password, stop);
        }
        catch (UnauthorizedAccessException) when (!login.Given)
        {
            throw new GpoNeedsLogin($"{Product.Title}'s service account is not an administrator on {name}. {needsAdmin}");
        }
        catch (UnauthorizedAccessException)
        {
            throw new ToolProblem($"Access denied. {Ldap.Account(user)} is not an administrator on {name}.", StatusCodes.Status403Forbidden);
        }
        catch (IOException e)
        {
            throw new ToolProblem($"Could not connect to {name}: {e.Message.TrimEnd('.', ' ')}. The computer must be turned on, with File and Printer Sharing allowed through its firewall.");
        }
        var read = InstallLog.Parse(file, DateTime.UtcNow, by);
        lock (deploy.Gate)
        {
            var d = deploy.Read();
            d.Logs ??= [];
            d.Logs[name.ToUpperInvariant()] = read;
            foreach (var old in d.Logs.OrderByDescending(l => l.Value.Read).Skip(MostLogs).Select(l => l.Key).ToList())
            {
                d.Logs.Remove(old);
            }
            deploy.Save(d);
        }
        var now = (await DeployComputers(clients, online, stop)).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ToolProblem($"The computer {name} is disabled in Active Directory.");
        return new InstallLogAnswer(now, file is null ? null : InstallLog.Decode(file.Bytes));
    }
}
