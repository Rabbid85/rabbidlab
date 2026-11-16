using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace RabbidLab.Server;

/// <summary>
/// A GPO's settings report (Group Policy Management's XML) as rows: where each setting is, its name and what it's set to,
/// for Administrative Templates, security settings, firewall rules, preferences and scripts, and anything else in a
/// plainer form. Plus who the GPO applies to and who can change it.
/// </summary>
public static partial class GpoReports
{
    private const int Most = 600;
    private const string Templates = "Administrative Templates";
    private const string Security = "Security Settings";

    public static GpoDetail Parse(string id, string xml)
    {
        XElement? root;
        try
        {
            root = XDocument.Parse(xml).Root;
        }
        catch (XmlException)
        {
            return new GpoDetail(id, [], [], [], null, null, null);
        }
        if (root is null)
        {
            return new GpoDetail(id, [], [], [], null, null, null);
        }
        var gpo = root.Name.LocalName == "GPO" ? root : root.Descendants().FirstOrDefault(e => e.Name.LocalName == "GPO") ?? root;
        var computer = Kid(gpo, "Computer");
        var user = Kid(gpo, "User");
        var sd = Kid(gpo, "SecurityDescriptor");
        return new GpoDetail(id, Side(computer), Side(user), Who(sd), Text(Kid(Kid(sd, "Owner"), "Name")) ?? Text(Kid(Kid(sd, "Owner"), "SID")),
            Number(Text(Kid(computer, "VersionSysvol"))), Number(Text(Kid(user, "VersionSysvol"))));
    }

    // --- Who it applies to, and who can change it ---------------------------------------------------------------------

    private static List<GpoTrustee> Who(XElement? sd)
    {
        var list = new List<GpoTrustee>();
        foreach (var p in Kids(Kid(sd, "Permissions"), "TrusteePermissions"))
        {
            var trustee = Kid(p, "Trustee");
            var name = Text(Kid(trustee, "Name")) ?? Text(Kid(trustee, "SID")) ?? "";
            if (name.Length == 0)
            {
                continue;
            }
            var access = Text(Kid(Kid(p, "Standard"), "GPOGroupedAccessEnum")) switch
            {
                "Apply Group Policy" => "apply",
                "Read" => "read",
                "Edit settings" => "edit",
                "Edit, delete, modify security" => "full",
                _ => "custom",
            };
            list.Add(new GpoTrustee(name, Text(Kid(trustee, "SID")), access, string.Equals(Text(Kid(Kid(p, "Type"), "PermissionType")), "Deny", StringComparison.OrdinalIgnoreCase),
                string.Equals(Text(Kid(p, "Inherited")), "true", StringComparison.OrdinalIgnoreCase)));
        }
        return list;
    }

    // --- Settings -----------------------------------------------------------------------------------------------------

    private static List<GpoSetting> Side(XElement? side)
    {
        var rows = new List<GpoSetting>();
        foreach (var data in Kids(side, "ExtensionData"))
        {
            foreach (var ext in Kids(data, "Extension"))
            {
                var type = ExtensionName(ext);
                foreach (var e in ext.Elements())
                {
                    if (rows.Count >= Most)
                    {
                        return rows;
                    }
                    Read(e, type, rows);
                }
            }
        }
        return rows;
    }

    private static void Read(XElement e, string type, List<GpoSetting> rows)
    {
        switch (e.Name.LocalName)
        {
            case "Policy":
                rows.Add(new($"{Templates} › {(Text(Kid(e, "Category")) ?? "").Replace("/", " › ", StringComparison.Ordinal)}".TrimEnd(' ', '›'), Text(Kid(e, "Name")) ?? "Policy", PolicyValue(e)));
                break;
            case "Account":
                rows.Add(Account(e));
                break;
            case "SecurityOptions":
                rows.Add(new($"{Security} › Local Policies › Security Options", SecurityOption(e), DisplayValue(e)));
                break;
            case "UserRightsAssignment":
                rows.Add(new($"{Security} › Local Policies › User Rights Assignment", Right(Text(Kid(e, "Name")) ?? ""), Members(e, "Member") is { Length: > 0 } m ? m : "Nobody"));
                break;
            case "Audit":
                rows.Add(new($"{Security} › Local Policies › Audit Policy", Words(Text(Kid(e, "Name")) ?? "Audit"), Audit(Text(Kid(e, "SuccessAttempts")), Text(Kid(e, "FailureAttempts")))));
                break;
            case "AuditSetting":
                rows.Add(new($"{Security} › Advanced Audit Policy", Text(Kid(e, "SubcategoryName")) ?? "Audit", Text(Kid(e, "SettingValue")) switch
                {
                    "1" => "Success",
                    "2" => "Failure",
                    "3" => "Success, Failure",
                    _ => "No auditing",
                }));
                break;
            case "RestrictedGroups":
                rows.Add(new($"{Security} › Restricted Groups", Text(Kid(Kid(e, "GroupName"), "Name")) ?? Text(Kid(Kid(e, "GroupName"), "SID")) ?? "Group",
                    string.Join("; ", new[] { Members(e, "Member") is { Length: > 0 } ms ? $"Members: {ms}" : null, Members(e, "Memberof") is { Length: > 0 } of ? $"Member of: {of}" : null }.OfType<string>()) is { Length: > 0 } v ? v : "Nobody"));
                break;
            case "SystemServices":
                rows.Add(new($"{Security} › System Services", Text(Kid(e, "Name")) ?? "Service", Text(Kid(e, "StartupMode")) ?? "Set"));
                break;
            case "EventLog":
                rows.Add(new($"{Security} › Event Log", $"{Text(Kid(e, "Log"))} log: {Words(Text(Kid(e, "Name")) ?? "")}".Trim(), Text(Kid(e, "SettingNumber")) ?? OnOff(Text(Kid(e, "SettingBoolean")))));
                break;
            case "Registry" when Kid(e, "Path") is not null:
            case "File" when Kid(e, "Path") is not null:
                rows.Add(new($"{Security} › {(e.Name.LocalName == "File" ? "File System" : "Registry")}", Text(Kid(e, "Path"))!, "Permissions set"));
                break;
            case "RegistrySetting":
                rows.Add(new($"{Templates} › Extra registry settings", Text(Kid(e, "KeyPath")) ?? "Key", Text(Kid(Kid(e, "Value"), "Name")) is { } n
                    ? $"{n} = {Text(Kid(Kid(e, "Value"), "Number")) ?? Text(Kid(Kid(e, "Value"), "String")) ?? ""}" : "Set"));
                break;
            case "InboundFirewallRules":
            case "OutboundFirewallRules":
                rows.Add(Firewall(e));
                break;
            case "DomainProfile":
            case "PrivateProfile":
            case "PublicProfile":
                if (Text(Kid(Kid(e, "EnableFirewall"), "Value")) is { } on)
                {
                    rows.Add(new("Windows Firewall › Profiles", $"{e.Name.LocalName.Replace("Profile", "", StringComparison.Ordinal)} profile: firewall", on == "true" ? "On" : "Off"));
                }
                break;
            case "GlobalSettings":
                break;
            case "Script":
                rows.Add(new($"Scripts › {Text(Kid(e, "Type")) ?? "Script"}", Text(Kid(e, "Command")) ?? "Script", Text(Kid(e, "Parameters")) is { Length: > 0 } p ? p : "No parameters"));
                break;
            case "MsiApplication":
                rows.Add(new("Software Installation", Text(Kid(e, "Name")) ?? "Package", $"{Text(Kid(e, "DeploymentType")) ?? "Assigned"}: {Text(Kid(e, "Path"))}"));
                break;
            default:
                Other(e, type, rows);
                break;
        }
    }

    // Preferences (an item with its Properties), a container of them, or anything else as plainly as it reads.
    private static void Other(XElement e, string type, List<GpoSetting> rows)
    {
        if (Kid(e, "Properties") is { } props)
        {
            var name = (string?) e.Attribute("name") ?? (string?) e.Attribute("status") ?? Words(e.Name.LocalName);
            var path = (string?) props.Attribute("path") ?? (string?) props.Attribute("targetPath") ?? (string?) props.Attribute("key");
            var shown = e.Name.LocalName == "Drive" && path is not null ? $"{name} {path}" : name;
            var bits = new List<string?> { Action((string?) props.Attribute("action")) };
            if (e.Name.LocalName != "Drive" && path is not null && !shown.Contains(path, StringComparison.OrdinalIgnoreCase))
            {
                bits.Add(path);
            }
            foreach (var a in new[] { "label", "value", "groupName", "userName", "newName", "startupType" })
            {
                if ((string?) props.Attribute(a) is { Length: > 0 } v)
                {
                    bits.Add(a == "label" ? $"label {v}" : v);
                }
            }
            rows.Add(new($"Preferences › {type}", shown, string.Join(", ", bits.OfType<string>().Where(b => b.Length > 0).Distinct())));
            return;
        }
        if (e.Elements().Any(k => Kid(k, "Properties") is not null))
        {
            foreach (var k in e.Elements())
            {
                Other(k, type, rows);
            }
            return;
        }
        if (e.Name.LocalName is "GPOSettingOrder" or "Filters" or "Blocked")
        {
            return;
        }
        var title = Text(Kid(e, "Name")) ?? Words(e.Name.LocalName);
        var value = string.Join(", ", e.Elements().Where(k => k.Name.LocalName != "Name" && !k.HasElements).Select(k => k.Value.Trim()).Where(v => v.Length > 0).Take(4));
        if (value.Length == 0)
        {
            value = string.Join(", ", e.Descendants().Where(k => !k.HasElements).Select(k => k.Value.Trim()).Where(v => v.Length > 0).Take(4));
        }
        rows.Add(new(type, title, Clip(value.Length > 0 ? value : "Set")));
    }

    private static string PolicyValue(XElement e)
    {
        var state = Text(Kid(e, "State")) ?? "Set";
        var parts = new List<string>();
        foreach (var o in e.Elements().Where(k => k.Name.LocalName is "CheckBox" or "DropDownList" or "Numeric" or "EditText" or "ListBox" or "MultiText" or "Text"))
        {
            var label = (Text(Kid(o, "Name")) ?? "").TrimEnd(':', ' ');
            var value = o.Name.LocalName switch
            {
                "CheckBox" => Text(Kid(o, "State")),
                "DropDownList" => Text(Kid(Kid(o, "Value"), "Name")) ?? Text(Kid(o, "Value")),
                "ListBox" => string.Join(", ", Kid(o, "Value")?.Descendants().Where(d => d.Name.LocalName == "Data").Select(d => d.Value.Trim()) ?? []),
                "MultiText" => string.Join(", ", o.Elements().Where(d => d.Name.LocalName == "Value").Select(d => d.Value.Trim())),
                _ => Text(Kid(o, "Value")),
            };
            if (o.Name.LocalName == "Text" || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            parts.Add(label.Length > 0 ? $"{label} {value}" : value!);
        }
        return Clip(parts.Count > 0 ? $"{state}: {string.Join("; ", parts)}" : state);
    }

    private static readonly Dictionary<string, (string Name, string? Unit)> Accounts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MinimumPasswordLength"] = ("Minimum password length", "characters"),
        ["MaximumPasswordAge"] = ("Maximum password age", "days"),
        ["MinimumPasswordAge"] = ("Minimum password age", "days"),
        ["PasswordHistorySize"] = ("Enforce password history", "passwords remembered"),
        ["PasswordComplexity"] = ("Password must meet complexity requirements", null),
        ["ClearTextPassword"] = ("Store passwords using reversible encryption", null),
        ["LockoutBadCount"] = ("Account lockout threshold", "invalid sign-in attempts"),
        ["LockoutDuration"] = ("Account lockout duration", "minutes"),
        ["ResetLockoutCount"] = ("Reset account lockout counter after", "minutes"),
        ["AllowAdministratorLockout"] = ("Allow Administrator account lockout", null),
        ["MaxTicketAge"] = ("Maximum lifetime for user ticket", "hours"),
        ["MaxRenewAge"] = ("Maximum lifetime for user ticket renewal", "days"),
        ["MaxServiceAge"] = ("Maximum lifetime for service ticket", "minutes"),
        ["MaxClockSkew"] = ("Maximum tolerance for computer clock synchronization", "minutes"),
        ["TicketValidateClient"] = ("Enforce user logon restrictions", null),
    };

    private static GpoSetting Account(XElement e)
    {
        var key = Text(Kid(e, "Name")) ?? "";
        var (name, unit) = Accounts.TryGetValue(key, out var known) ? known : (Words(key), null);
        var area = Text(Kid(e, "Type")) switch
        {
            "Password" => "Password Policy",
            "Account Lockout" => "Account Lockout Policy",
            "Kerberos" => "Kerberos Policy",
            var t => t ?? "Account Policies",
        };
        var value = Text(Kid(e, "SettingNumber")) is { } n ? unit is null ? n : $"{n} {unit}" : OnOff(Text(Kid(e, "SettingBoolean")));
        return new($"{Security} › Account Policies › {area}", name, value);
    }

    private static readonly Dictionary<string, string> AccessPolicies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ForceLogoffWhenHourExpire"] = "Network security: Force logoff when logon hours expire",
        ["LSAAnonymousNameLookup"] = "Network access: Allow anonymous SID/Name translation",
        ["EnableAdminAccount"] = "Accounts: Administrator account status",
        ["EnableGuestAccount"] = "Accounts: Guest account status",
        ["NewAdministratorName"] = "Accounts: Rename administrator account",
        ["NewGuestName"] = "Accounts: Rename guest account",
    };

    private static string SecurityOption(XElement e)
    {
        if (Text(Kid(Kid(e, "Display"), "Name")) is { Length: > 0 } shown)
        {
            return shown;
        }
        if (Text(Kid(e, "SystemAccessPolicyName")) is { } policy)
        {
            return AccessPolicies.GetValueOrDefault(policy) ?? Words(policy);
        }
        var key = Text(Kid(e, "KeyName")) ?? "Security option";
        return key[(key.LastIndexOf('\\') + 1)..];
    }

    private static string DisplayValue(XElement e)
    {
        var d = Kid(e, "Display");
        if (Text(Kid(d, "DisplayBoolean")) is { } b)
        {
            return OnOff(b);
        }
        if (Text(Kid(d, "DisplayNumber")) is { } n)
        {
            return Text(Kid(d, "Units")) is { Length: > 0 } u ? $"{n} {u}" : n;
        }
        if (Text(Kid(d, "DisplayString")) is { } s)
        {
            return s;
        }
        if (Kid(d, "DisplayStrings") is { } list)
        {
            return Clip(string.Join(", ", list.Elements().Select(v => v.Value.Trim()).Where(v => v.Length > 0)));
        }
        return Text(Kid(e, "SettingString")) ?? (Text(Kid(e, "SettingNumber")) is { } raw ? raw == "1" ? "Enabled" : raw == "0" ? "Disabled" : raw : "Set");
    }

    private static readonly Dictionary<string, string> Rights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SeInteractiveLogonRight"] = "Allow log on locally",
        ["SeDenyInteractiveLogonRight"] = "Deny log on locally",
        ["SeRemoteInteractiveLogonRight"] = "Allow log on through Remote Desktop Services",
        ["SeDenyRemoteInteractiveLogonRight"] = "Deny log on through Remote Desktop Services",
        ["SeNetworkLogonRight"] = "Access this computer from the network",
        ["SeDenyNetworkLogonRight"] = "Deny access to this computer from the network",
        ["SeBatchLogonRight"] = "Log on as a batch job",
        ["SeServiceLogonRight"] = "Log on as a service",
        ["SeBackupPrivilege"] = "Back up files and directories",
        ["SeRestorePrivilege"] = "Restore files and directories",
        ["SeShutdownPrivilege"] = "Shut down the system",
        ["SeRemoteShutdownPrivilege"] = "Force shutdown from a remote system",
        ["SeSystemtimePrivilege"] = "Change the system time",
        ["SeTimeZonePrivilege"] = "Change the time zone",
        ["SeDebugPrivilege"] = "Debug programs",
        ["SeTakeOwnershipPrivilege"] = "Take ownership of files or other objects",
        ["SeLoadDriverPrivilege"] = "Load and unload device drivers",
        ["SeSecurityPrivilege"] = "Manage auditing and security log",
        ["SeMachineAccountPrivilege"] = "Add workstations to domain",
        ["SeEnableDelegationPrivilege"] = "Enable computer and user accounts to be trusted for delegation",
        ["SeAssignPrimaryTokenPrivilege"] = "Replace a process level token",
        ["SeIncreaseQuotaPrivilege"] = "Adjust memory quotas for a process",
        ["SeAuditPrivilege"] = "Generate security audits",
        ["SeChangeNotifyPrivilege"] = "Bypass traverse checking",
        ["SeImpersonatePrivilege"] = "Impersonate a client after authentication",
        ["SeCreateGlobalPrivilege"] = "Create global objects",
        ["SeIncreaseBasePriorityPrivilege"] = "Increase scheduling priority",
        ["SeProfileSingleProcessPrivilege"] = "Profile single process",
        ["SeSystemProfilePrivilege"] = "Profile system performance",
        ["SeUndockPrivilege"] = "Remove computer from docking station",
        ["SeManageVolumePrivilege"] = "Perform volume maintenance tasks",
        ["SeIncreaseWorkingSetPrivilege"] = "Increase a process working set",
        ["SeCreatePagefilePrivilege"] = "Create a pagefile",
        ["SeSystemEnvironmentPrivilege"] = "Modify firmware environment values",
    };

    private static string Right(string key) => Rights.GetValueOrDefault(key) ?? Words(key.StartsWith("Se", StringComparison.Ordinal) ? key[2..] : key);

    private static string Members(XElement e, string kind) =>
        string.Join(", ", Kids(e, kind).Select(m => Text(Kid(m, "Name")) ?? Text(Kid(m, "SID"))).OfType<string>());

    private static string Audit(string? success, string? failure) =>
        (success == "true", failure == "true") switch
        {
            (true, true) => "Success, Failure",
            (true, false) => "Success",
            (false, true) => "Failure",
            _ => "No auditing",
        };

    private static GpoSetting Firewall(XElement e)
    {
        var name = Text(Kid(e, "Name")) ?? "Rule";
        if (name.StartsWith('@'))
        {
            var app = Path.GetFileName(Text(Kid(e, "App")) ?? "");
            var what = Text(Kid(e, "Svc")) ?? (app.Length > 0 ? app : "Windows");
            name = $"Built-in rule ({what})";
        }
        var protocol = Text(Kid(e, "Protocol")) switch
        {
            "6" => "TCP",
            "17" => "UDP",
            "1" => "ICMP",
            "58" => "ICMPv6",
            null => "",
            var p => $"protocol {p}",
        };
        var port = Text(Kid(e, "LPort")) is { } lp ? $" {lp}" : "";
        var from = Text(Kid(e, "RA4")) is { } ra ? $" from {ra}" : "";
        var profiles = Kids(e, "Profile").Select(p => p.Value.Trim()).ToList();
        var value = $"{Text(Kid(e, "Action")) ?? "Allow"} {protocol}{port}{from}".Replace("  ", " ", StringComparison.Ordinal).Trim()
            + (profiles.Count > 0 ? $" ({string.Join(", ", profiles)})" : "") + (Text(Kid(e, "Active")) == "false" ? ", off" : "");
        return new($"Windows Firewall › {(e.Name.LocalName.StartsWith("In", StringComparison.Ordinal) ? "Inbound" : "Outbound")} rules", name, value);
    }

    private static string? Action(string? a) => a switch
    {
        "C" => "Create",
        "R" => "Replace",
        "U" => "Update",
        "D" => "Delete",
        _ => null,
    };

    private static string ExtensionName(XElement ext)
    {
        var type = (string?) ext.Attribute(XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance")) ?? "";
        type = type[(type.IndexOf(':') + 1)..];
        return type switch
        {
            "SecuritySettings" => Security,
            "RegistrySettings" => Templates,
            "PublicKeySettings" => "Public Key Policies",
            "WindowsFirewallSettings" => "Windows Firewall",
            "DriveMapSettings" => "Drive Maps",
            "Scripts" or "ScriptSettings" => "Scripts",
            "SoftwareInstallationSettings" => "Software Installation",
            "" => "Settings",
            _ => Words(type.EndsWith("Settings", StringComparison.Ordinal) ? type[..^8] : type),
        };
    }

    // --- Small parts ------------------------------------------------------------------------------------------------

    private static XElement? Kid(XElement? e, string name) => e?.Elements().FirstOrDefault(k => k.Name.LocalName == name);

    private static IEnumerable<XElement> Kids(XElement? e, string name) => e?.Elements().Where(k => k.Name.LocalName == name) ?? [];

    private static string? Text(XElement? e) => e is null ? null : e.Value.Trim() is { Length: > 0 } v ? v : null;

    private static int? Number(string? s) => int.TryParse(s, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string OnOff(string? b) => b == "true" ? "Enabled" : b == "false" ? "Disabled" : b ?? "Set";

    private static string Clip(string s) => s.Length > 400 ? s[..400] + "…" : s;

    // MinimumPasswordLength -> Minimum password length.
    private static string Words(string s)
    {
        var w = Camel().Replace(s, " $1").Trim();
        return w.Length == 0 ? s : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
    }

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex Camel();
}
