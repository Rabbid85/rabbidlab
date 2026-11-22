using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>A domain setting's new value, and the domain admin it's changed as (not saved).</summary>
public sealed record DomainSettingForm(string? Value, string? Username, string? Password);

/// <summary>Files in SYSVOL, read and written as the admin typed (Group Policy keeps the password policy there).</summary>
public interface ISysvol
{
    Task<byte[]> Read(string path, string username, string password, CancellationToken stop);

    Task Write(string path, byte[] data, string username, string password, CancellationToken stop);
}

/// <summary>SYSVOL over the network, as the admin typed: Windows uses the login for this server's own connection only.</summary>
public sealed class WindowsSysvol : ISysvol
{
    public Task<byte[]> Read(string path, string username, string password, CancellationToken stop) => As(username, password, () => File.ReadAllBytes(path));

    public Task Write(string path, byte[] data, string username, string password, CancellationToken stop) => As(username, password, () =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); // a new GPO's folders aren't there yet
        File.WriteAllBytes(path, data);
        return true;
    });

    private static Task<T> As<T>(string username, string password, Func<T> work)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new ToolProblem($"Writing Group Policy files requires {Product.Title} running on Windows with access to SYSVOL.");
        }
        return Task.Run(() => Windows(username, password, work));
    }

    [SupportedOSPlatform("windows")]
    private static T Windows<T>(string username, string password, Func<T> work)
    {
        try
        {
            return NetworkLogon.Run(username, password, work, "Windows could not use those credentials to access SYSVOL.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new ToolProblem($"Access denied. {Ldap.Account(username)} does not have permission to change this GPO's files in SYSVOL. This requires a Domain Admin, or permissions delegated in Group Policy Management.");
        }
        catch (IOException e)
        {
            throw new ToolProblem($"Could not reach SYSVOL: {e.Message}");
        }
    }
}

/// <summary>The text of Group Policy's files: the security template's sections, and GPT.INI's version.</summary>
public static partial class GpoText
{
    /// <summary>Sets keys in one [section] of a security template (GptTmpl.inf): changed where they are, added at its end, the section added if missing.</summary>
    public static string SetInf(string text, string section, IReadOnlyDictionary<string, string> values)
    {
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        bool Head(string l) => l.TrimStart().StartsWith('[');
        var start = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            // After [Unicode] when there is one, else at the top.
            var unicode = lines.FindIndex(l => l.Trim().Equals("[Unicode]", StringComparison.OrdinalIgnoreCase));
            start = unicode < 0 ? 0 : lines.FindIndex(unicode + 1, Head) is var next and >= 0 ? next : lines.Count;
            lines.Insert(start, $"[{section}]");
        }
        var end = lines.FindIndex(start + 1, Head);
        if (end < 0)
        {
            end = lines.Count;
        }
        while (end > start + 1 && lines[end - 1].Trim().Length == 0)
        {
            end--;
        }
        foreach (var (key, value) in values)
        {
            var at = lines.FindIndex(start + 1, end - start - 1, l => l.Split('=')[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                lines[at] = $"{key} = {value}";
            }
            else
            {
                lines.Insert(end++, $"{key} = {value}");
            }
        }
        return string.Join(nl, lines);
    }

    /// <summary>GPT.INI with its version one more than both it and Active Directory had, so every computer applies it again.</summary>
    public static (string Text, int Version) BumpGpt(string text, int adVersion)
    {
        var m = Version().Match(text);
        var file = m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        var next = Math.Max(file, adVersion) + 1;
        var line = $"Version={next.ToString(CultureInfo.InvariantCulture)}";
        return (m.Success ? text[..m.Index] + line + text[(m.Index + m.Length)..] : $"{text.TrimEnd()}\r\n{line}\r\n", next);
    }

    /// <summary>A file's text and how it was written (UTF-16 with a mark, UTF-8, or the old code page), so it goes back the same way.</summary>
    public static (string Text, Encoding Encoding) Decode(byte[] data) =>
        data is [0xFF, 0xFE, ..] ? (Encoding.Unicode.GetString(data, 2, data.Length - 2), Encoding.Unicode)
        : data is [0xEF, 0xBB, 0xBF, ..] ? (Encoding.UTF8.GetString(data, 3, data.Length - 3), new UTF8Encoding(true))
        : (Encoding.Latin1.GetString(data), Encoding.Latin1);

    public static byte[] Encode(string text, Encoding encoding) => [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    [GeneratedRegex(@"^Version=(\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex Version();
}

public sealed partial class DirectoryTools
{
    private const string DefaultDomainPolicy = "{31B2F340-016D-11D2-945F-00C04FB984F9}";
    private const long Day = 864_000_000_000;
    private const long Minute = 600_000_000;

    /// <summary>SYSVOL, where the Default Domain Policy's password and lockout rules are kept. Tests give their own.</summary>
    public ISysvol Sysvol { get; set; } = new WindowsSysvol();

    // What can be changed here: a number from Min to Max (in Unit), or on and off. The rest say how they're changed.
    private sealed record EditSpec(string Kind, int Min = 0, int Max = 1, string Unit = "");

    private static readonly Dictionary<string, EditSpec> Edits = new()
    {
        ["length"] = new("number", 0, 14, "characters"),
        ["complex"] = new("switch"),
        ["reversible"] = new("switch"),
        ["history"] = new("number", 0, 24, "passwords"),
        ["maxage"] = new("number", 1, 999, "days"),
        ["minage"] = new("number", 0, 998, "days"),
        ["lockout"] = new("number", 0, 999, "invalid attempts"),
        ["duration"] = new("number", 1, 99_999, "minutes"),
        ["quota"] = new("number", 0, 100, "computers"),
        ["guest"] = new("switch"),
        ["tombstone"] = new("number", 60, 3650, "days"),
    };

    private static readonly Dictionary<string, string> How = new()
    {
        ["dfl"] = "Raise it in Active Directory Domains and Trusts. Functional levels cannot be lowered, so this setting is not changed here.",
        ["ffl"] = "Raise it in Active Directory Domains and Trusts. Functional levels cannot be lowered, so this setting is not changed here.",
        ["krbtgt"] = "Reset it with Microsoft's New-KrbtgtKeys script, twice and a day apart, so tickets signed with the old key expire first.",
        ["admins"] = "Open Domain Admins under Groups to review and remove members.",
        ["pso"] = "Create them in Active Directory Administrative Center, under System > Password Settings Container.",
        ["recycle"] = "Enable it from the Recycle Bin page. Once enabled, it cannot be disabled.",
        ["dcs"] = "Add a domain controller by promoting another Windows Server.",
        ["dcos"] = "Upgrade by promoting a newer Windows Server, then demoting the old one.",
        ["laps"] = "Configure it with Update-LapsADSchema and a Group Policy for Windows LAPS.",
    };

    /// <summary>A setting with what it takes to change it: its value as a number (1 or 0 for on and off), or how it's changed instead.</summary>
    private static AdSetting Editable(AdSetting s, long? value) => Edits.TryGetValue(s.Key, out var e)
        ? s with { Edit = e.Kind, Value = value, Min = e.Min, Max = e.Max, Unit = e.Unit }
        : s with { How = How.GetValueOrDefault(s.Key) };

    public async Task<List<AdSetting>> SetDomainSetting(string key, string value, string username, string password, CancellationToken stop)
    {
        var f = Ready();
        var spec = Edits.GetValueOrDefault(key) ?? throw new ToolProblem("This setting cannot be changed here.");
        var v = (value ?? "").Trim().ToLowerInvariant();
        long n = spec.Kind == "switch"
            ? v is "true" or "on" or "1" ? 1 : v is "false" or "off" or "0" ? 0 : throw new ToolProblem("Enter on or off.")
            : long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) && num >= spec.Min && num <= spec.Max ? num
            : throw new ToolProblem($"Enter a number from {spec.Min} to {spec.Max}.");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            throw new ToolProblem("Sign in as a domain admin to change this setting. These credentials are used once and are not stored.");
        }
        using var ldap = await open(f.Domain!, await Host(f.Domain!, stop), username.Trim(), password, stop) ?? throw new ToolProblem("The username or password is incorrect.");
        try
        {
            switch (key)
            {
                case "quota":
                    await ldap.Modify(ldap.BaseDn, [new(LdapChange.Replace, "ms-DS-MachineAccountQuota", n.ToString(CultureInfo.InvariantCulture))], stop);
                    break;
                case "guest":
                {
                    var sid = (await ldap.Find(ldap.BaseDn, "(objectClass=*)", false, ["objectSid"], 1, stop)).FirstOrDefault()?.Bytes("objectSid")
                        ?? throw new ToolProblem("Active Directory did not return the domain SID.");
                    var guest = (await ldap.Find(ldap.BaseDn, $"(objectSid={Sid(sid)}-501)", true, ["userAccountControl"], 1, stop)).FirstOrDefault()
                        ?? throw new ToolProblem("The Guest account was not found.");
                    var flags = guest.Number("userAccountControl") ?? (NormalAccount | Disabled);
                    flags = n == 1 ? flags & ~Disabled : flags | Disabled;
                    await ldap.Modify(guest.Dn, [new(LdapChange.Replace, "userAccountControl", flags.ToString(CultureInfo.InvariantCulture))], stop);
                    break;
                }
                case "tombstone":
                {
                    var config = (await ldap.Find("", "(objectClass=*)", false, ["configurationNamingContext"], 1, stop)).FirstOrDefault()?.Text("configurationNamingContext")
                        ?? throw new ToolProblem("Active Directory did not return its configuration partition.");
                    await ldap.Modify($"CN=Directory Service,CN=Windows NT,CN=Services,{config}", [new(LdapChange.Replace, "tombstoneLifetime", n.ToString(CultureInfo.InvariantCulture))], stop);
                    break;
                }
                default:
                    await SetPolicy(ldap, key, n, username.Trim(), password, stop);
                    break;
            }
        }
        catch (LdapRefused e)
        {
            throw new ToolProblem(e.Code == LdapRefused.InsufficientRights
                ? $"Access denied. {Ldap.Account(username)} does not have permission to change this setting. {(key == "tombstone" ? "This requires an Enterprise Admin." : "This requires a Domain Admin.")}"
                : $"Active Directory returned an error: {e.Message}");
        }
        return await DomainSettings(stop);
    }

    // The password and lockout rules, as Group Policy Management changes them: in the Default Domain Policy's security
    // template in SYSVOL, with the policy's version raised so every domain controller applies it. The domain's own
    // object is set too, so the new rule counts (and shows here) straight away rather than at the next refresh.
    private async Task SetPolicy(ILdap ldap, string key, long n, string username, string password, CancellationToken stop)
    {
        var gpoDn = $"CN={DefaultDomainPolicy},CN=Policies,CN=System,{ldap.BaseDn}";
        var gpo = (await ldap.Find(gpoDn, "(objectClass=*)", false, ["gPCFileSysPath", "versionNumber"], 1, stop)).FirstOrDefault()
            ?? throw new ToolProblem("The Default Domain Policy was not found, so the password policy cannot be changed here.");
        var path = gpo.Text("gPCFileSysPath") ?? throw new ToolProblem("The Default Domain Policy has no file system path (gPCFileSysPath).");
        var now = (await ldap.Find(ldap.BaseDn, "(objectClass=*)", false, ["pwdProperties", "maxPwdAge", "minPwdAge", "lockoutDuration", "lockOutObservationWindow"], 1, stop)).FirstOrDefault()
            ?? throw new ToolProblem("Could not read the domain object.");
        var inf = new Dictionary<string, string>();
        var mods = new List<LdapMod>();
        void Set(string infKey, long infValue, string attribute, long attributeValue)
        {
            inf[infKey] = infValue.ToString(CultureInfo.InvariantCulture);
            mods.Add(new(LdapChange.Replace, attribute, attributeValue.ToString(CultureInfo.InvariantCulture)));
        }
        var properties = now.Number("pwdProperties") ?? 0;
        var maxAge = Days(now.Number("maxPwdAge"));
        var minAge = Days(now.Number("minPwdAge")) ?? 0;
        var window = Minutes(now.Number("lockOutObservationWindow"));
        switch (key)
        {
            case "length": Set("MinimumPasswordLength", n, "minPwdLength", n); break;
            case "complex": Set("PasswordComplexity", n, "pwdProperties", n == 1 ? properties | 1 : properties & ~1L); break;
            case "reversible": Set("ClearTextPassword", n, "pwdProperties", n == 1 ? properties | 16 : properties & ~16L); break;
            case "history": Set("PasswordHistorySize", n, "pwdHistoryLength", n); break;
            case "maxage":
                if (n <= minAge)
                {
                    throw new ToolProblem($"The maximum password age must be longer than the minimum password age ({minAge} days).");
                }
                Set("MaximumPasswordAge", n, "maxPwdAge", -n * Day);
                break;
            case "minage":
                if (maxAge is { } most && n >= most)
                {
                    throw new ToolProblem($"The minimum password age must be shorter than the maximum password age ({most} days).");
                }
                Set("MinimumPasswordAge", n, "minPwdAge", -n * Day);
                break;
            case "lockout":
                Set("LockoutBadCount", n, "lockoutThreshold", n);
                // Turning lockout on needs how long it lasts and when the count resets: 15 minutes each when they aren't set.
                if (n > 0 && now.Number("lockoutDuration") is null or 0)
                {
                    Set("LockoutDuration", 15, "lockoutDuration", -15 * Minute);
                }
                if (n > 0 && now.Number("lockOutObservationWindow") is null or 0)
                {
                    Set("ResetLockoutCount", 15, "lockOutObservationWindow", -15 * Minute);
                }
                break;
            case "duration":
                Set("LockoutDuration", n, "lockoutDuration", -n * Minute);
                // The count can't reset after the lockout ends.
                if (window is { } w && w > n)
                {
                    Set("ResetLockoutCount", n, "lockOutObservationWindow", -n * Minute);
                }
                break;
            default:
                throw new ToolProblem("This setting cannot be changed here.");
        }
        var template = $@"{path}\MACHINE\Microsoft\Windows NT\SecEdit\GptTmpl.inf";
        var ini = $@"{path}\GPT.INI";
        var (text, encoding) = GpoText.Decode(await Sysvol.Read(template, username, password, stop));
        var (iniText, iniEncoding) = GpoText.Decode(await Sysvol.Read(ini, username, password, stop));
        var (bumped, version) = GpoText.BumpGpt(iniText, (int) (gpo.Number("versionNumber") ?? 0));
        await Sysvol.Write(template, GpoText.Encode(GpoText.SetInf(text, "System Access", inf), encoding), username, password, stop);
        await Sysvol.Write(ini, GpoText.Encode(bumped, iniEncoding), username, password, stop);
        await ldap.Modify(gpoDn, [new(LdapChange.Replace, "versionNumber", version.ToString(CultureInfo.InvariantCulture))], stop);
        await ldap.Modify(ldap.BaseDn, mods, stop);
    }
}
