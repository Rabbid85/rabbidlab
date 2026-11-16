using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace RabbidLab.Server;

/// <summary>The client deployed with Group Policy: the GPO Rabbid Lab made for it, and what it gives computers.</summary>
public sealed class ClientDeployFile
{
    public string? Gpo { get; set; }
    public string? Server { get; set; }
    public string? Version { get; set; }
    public string? By { get; set; }
    public DateTime? At { get; set; }
}

/// <summary>What the Deploy page shows: whether Group Policy Management is here, and the deployment if there is one.</summary>
public sealed record ClientDeployView(bool Gpmc, string? Domain, string? Gpo, string? Name, bool Linked, string? Server, string? Version, string? By, DateTime? At,
    string ClientPath, string ThisVersion);

/// <summary>Deploy: the address computers reach the server at, and a domain admin's login when Rabbid Lab's own account isn't allowed (never kept).</summary>
public sealed record ClientDeployForm(string? Server, string? Username = null, string? Password = null)
{
    public GpoLogin Login => new(Username, Password);
}

/// <summary>
/// One button puts the client on every computer: a GPO, Rabbid Lab Client, with the installer in its own SYSVOL folder,
/// a Group Policy Preferences task that installs it quietly as SYSTEM at the next policy refresh wherever it isn't yet,
/// and the client's file let past Defender's attack surface reduction rules until it's code-signed. Linked at the
/// domain, so every computer gets it. Computers still wait for approval in Devices.
/// </summary>
public sealed partial class DirectoryTools
{
    public const string ClientGpoName = "Rabbid Lab Client";

    /// <summary>Where the installer puts the client, which the task looks for and Defender lets run.</summary>
    public const string ClientExe = @"C:\Program Files\Rabbid Lab Client\RabbidLab-Client.exe";


    // Group Policy Preferences' own extensions: Scheduled Tasks and Registry, each with the tool that edits them.
    private static readonly (string Cse, string Tool)[] DeployExtensions =
    [
        ("{AADCED64-746C-4633-A97C-D61349046527}", "{CAB54552-DEEA-4691-817E-ED4A4D1AFC72}"),
        ("{B087BE9D-ED37-454F-AF9C-04291E351182}", "{BEE07A6A-EC9F-4659-B8C9-0B1937907C83}"),
    ];

    private readonly JsonFile<ClientDeployFile> deploy = folder.Json<ClientDeployFile>("client-deploy.json", Json);

    private ClientDeployFile Deployed()
    {
        lock (deploy.Gate)
        {
            return deploy.Read();
        }
    }

    public async Task<ClientDeployView> ClientDeploy(CancellationToken stop)
    {
        var d = Deployed();
        var f = Ready();
        string? name = null;
        var linked = false;
        if (d.Gpo is not null)
        {
            using var ldap = await Open(stop);
            name = (await ldap.Find($"CN={d.Gpo},CN=Policies,CN=System,{ldap.BaseDn}", "(objectClass=groupPolicyContainer)", false, ["displayName"], 1, stop)).FirstOrDefault()?.Text("displayName");
            var top = (await ldap.Find(ldap.BaseDn, "(objectClass=*)", false, ["gPLink"], 1, stop)).FirstOrDefault();
            linked = name is not null && GpoLinks.Parse(top?.Text("gPLink")).Any(e => string.Equals(e.Gpo, d.Gpo, StringComparison.OrdinalIgnoreCase) && (e.Flags & GpoLinks.Disabled) == 0);
        }
        return new ClientDeployView(Gpmc.Here, f.Domain, name is null ? null : d.Gpo, name, linked, d.Server, d.Version, d.By, d.At, ClientExe, Product.Version);
    }

    /// <summary>
    /// Makes the GPO (or brings Rabbid Lab's own up to date), writes the installer, the task and the Defender exclusion
    /// to it, raises its version so computers read it again, and links it at the domain. Nothing is installed until each
    /// computer refreshes its policy.
    /// </summary>
    public async Task<string> DeployClient(string server, string? fingerprint, byte[] msi, GpoLogin login, string by, CancellationToken stop)
    {
        if (!Uri.TryCreate(server.Trim(), UriKind.Absolute, out var address) || address.Scheme is not ("https" or "http"))
        {
            throw new ToolProblem("Pick the address computers reach this server at.");
        }
        var f = Ready();
        var (user, password) = SysvolLogin(f, login);
        var d = Deployed();
        string? id = null;
        if (d.Gpo is not null)
        {
            using var ldap = await Open(stop);
            id = (await ldap.Find($"CN={d.Gpo},CN=Policies,CN=System,{ldap.BaseDn}", "(objectClass=groupPolicyContainer)", false, ["cn"], 1, stop)).Count > 0 ? d.Gpo : null;
        }
        if (id is null)
        {
            using (var ldap = await Open(stop))
            {
                await Taken(ldap, ClientGpoName, null, stop);
            }
            id = await Gpm(login, t => Gpmc.Create(t, ClientGpoName, stop), stop);
        }
        var root = $@"\\{f.Domain}\SYSVOL\{f.Domain}\Policies\{id}";
        var msiPath = $@"{root}\Machine\Rabbid Lab\RabbidLab-Client.msi";
        var now = DateTime.Now;
        await Sysvol.Write(msiPath, msi, user, password, stop);
        await Sysvol.Write($@"{root}\Machine\Preferences\ScheduledTasks\ScheduledTasks.xml", GpoXml.InstallTask(msiPath, address.GetLeftPart(UriPartial.Authority), fingerprint, now), user, password, stop);
        await Sysvol.Write($@"{root}\Machine\Preferences\Registry\Registry.xml", GpoXml.AsrExclusion(ClientExe, now), user, password, stop);
        // GPT.INI and the GPO's version in Active Directory go up together, so every computer reads it again.
        await Change(login, async (ldap, _) =>
        {
            var dn = $"CN={id},CN=Policies,CN=System,{ldap.BaseDn}";
            var gpo = (await ldap.Find(dn, "(objectClass=groupPolicyContainer)", false, ["versionNumber", "gPCMachineExtensionNames"], 1, stop)).FirstOrDefault()
                ?? throw new ToolProblem("The GPO isn't in Active Directory yet. Try again in a minute.");
            byte[] ini;
            try
            {
                ini = await Sysvol.Read($@"{root}\GPT.INI", user, password, stop);
            }
            catch (ToolProblem)
            {
                ini = Encoding.ASCII.GetBytes("[General]\r\nVersion=0\r\n");
            }
            var (text, encoding) = GpoText.Decode(ini);
            var (bumped, version) = GpoText.BumpGpt(text, (int) (gpo.Number("versionNumber") ?? 0));
            await Sysvol.Write($@"{root}\GPT.INI", GpoText.Encode(bumped, encoding), user, password, stop);
            await ldap.Modify(dn,
            [
                new(LdapChange.Replace, "gPCMachineExtensionNames", GpoText.AddExtensions(gpo.Text("gPCMachineExtensionNames"), DeployExtensions)),
                new(LdapChange.Replace, "versionNumber", version.ToString(CultureInfo.InvariantCulture)),
            ], stop);
        }, stop);
        // Linked at the domain, so every computer gets it.
        using (var ldap = await Open(stop))
        {
            var top = (await ldap.Find(ldap.BaseDn, "(objectClass=*)", false, ["gPLink"], 1, stop)).FirstOrDefault();
            var link = GpoLinks.Parse(top?.Text("gPLink")).FirstOrDefault(e => string.Equals(e.Gpo, id, StringComparison.OrdinalIgnoreCase));
            if (link is null)
            {
                await Link(id, ldap.BaseDn, new GpoLinkChange(Add: true), login, stop);
            }
            else if ((link.Flags & GpoLinks.Disabled) != 0)
            {
                await Link(id, ldap.BaseDn, new GpoLinkChange(Enabled: true), login, stop);
            }
        }
        lock (deploy.Gate)
        {
            deploy.Save(new ClientDeployFile { Gpo = id, Server = address.GetLeftPart(UriPartial.Authority), Version = Product.Version, By = by, At = DateTime.UtcNow });
        }
        return id;
    }

    /// <summary>Stops new installs: the GPO's link at the domain is turned off. Computers keep the client they have.</summary>
    public async Task StopClientDeploy(GpoLogin login, CancellationToken stop)
    {
        var d = Deployed();
        if (d.Gpo is null)
        {
            throw new ToolProblem("The client isn't deployed with Group Policy.");
        }
        using var ldap = await Open(stop);
        var top = (await ldap.Find(ldap.BaseDn, "(objectClass=*)", false, ["gPLink"], 1, stop)).FirstOrDefault();
        if (GpoLinks.Parse(top?.Text("gPLink")).Any(e => string.Equals(e.Gpo, d.Gpo, StringComparison.OrdinalIgnoreCase)))
        {
            await Link(d.Gpo, ldap.BaseDn, new GpoLinkChange(Remove: true), login, stop);
        }
    }

    /// <summary>Who SYSVOL is written as: the login given, or Rabbid Lab's own account.</summary>
    private (string User, string Password) SysvolLogin(DirectoryToolsFile f, GpoLogin login) =>
        login.Given ? (login.Username!.Trim(), login.Password ?? "")
        : f.ReadOnly ? throw new GpoNeedsLogin(ReadOnlyNeedsAdmin)
        : ($"{f.Account}@{f.Domain}", secrets.TryDecrypt(f.Password) ?? throw new ToolProblem($"{Product.Title}'s Active Directory password can't be read here any more. Set it up again.", StatusCodes.Status409Conflict));
}

/// <summary>Group Policy Preferences files, as Group Policy Management writes them.</summary>
public static class GpoXml
{
    /// <summary>
    /// An immediate task that runs once each policy refresh as SYSTEM: when the client isn't installed, msiexec installs it
    /// quietly from the GPO's folder, pointed at this server. Once it's there the task does nothing.
    /// </summary>
    public static byte[] InstallTask(string msi, string server, string? fingerprint, DateTime now)
    {
        var args = $"/c if not exist \"{DirectoryTools.ClientExe}\" msiexec.exe /i \"{msi}\" /qn /norestart /l*v \"%windir%\\Temp\\RabbidLab-Client-install.log\" SERVER={server}"
            + (string.IsNullOrEmpty(fingerprint) ? "" : $" FINGERPRINT={fingerprint}");
        const string name = "Install the Rabbid Lab client";
        var doc = new XElement("ScheduledTasks", new XAttribute("clsid", "{CC63F200-7309-4ba0-B154-A71CD118DBCC}"),
            new XElement("ImmediateTaskV2",
                new XAttribute("clsid", "{9756B581-76EC-4169-9AFC-0CA8D43ADB5F}"), new XAttribute("name", name), new XAttribute("image", "0"),
                new XAttribute("changed", Changed(now)), new XAttribute("uid", Uid()), new XAttribute("userContext", "0"), new XAttribute("removePolicy", "0"),
                new XElement("Properties", new XAttribute("action", "C"), new XAttribute("name", name), new XAttribute("runAs", @"NT AUTHORITY\System"), new XAttribute("logonType", "S4U"),
                    new XElement("Task", new XAttribute("version", "1.3"),
                        new XElement("RegistrationInfo", new XElement("Author", Product.Title), new XElement("Description", "Installs the Rabbid Lab client where it isn't installed yet.")),
                        new XElement("Principals", new XElement("Principal", new XAttribute("id", "Author"),
                            new XElement("UserId", @"NT AUTHORITY\System"), new XElement("LogonType", "S4U"), new XElement("RunLevel", "HighestAvailable"))),
                        new XElement("Settings",
                            new XElement("IdleSettings", new XElement("Duration", "PT10M"), new XElement("WaitTimeout", "PT1H"), new XElement("StopOnIdleEnd", "false"), new XElement("RestartOnIdle", "false")),
                            new XElement("MultipleInstancesPolicy", "IgnoreNew"), new XElement("DisallowStartIfOnBatteries", "false"), new XElement("StopIfGoingOnBatteries", "false"),
                            new XElement("AllowHardTerminate", "true"), new XElement("StartWhenAvailable", "true"), new XElement("RunOnlyIfNetworkAvailable", "false"),
                            new XElement("AllowStartOnDemand", "true"), new XElement("Enabled", "true"), new XElement("Hidden", "false"), new XElement("RunOnlyIfIdle", "false"),
                            new XElement("WakeToRun", "false"), new XElement("ExecutionTimeLimit", "PT1H"), new XElement("Priority", "7"), new XElement("DeleteExpiredTaskAfter", "PT0S")),
                        new XElement("Triggers", new XElement("TimeTrigger",
                            new XElement("StartBoundary", "%LocalTimeXmlEx%"), new XElement("EndBoundary", "%LocalTimeXmlEx%"), new XElement("Enabled", "true"))),
                        new XElement("Actions", new XAttribute("Context", "Author"),
                            new XElement("Exec", new XElement("Command", "cmd.exe"), new XElement("Arguments", args)))))));
        return Bytes(doc);
    }

    /// <summary>The client's file excluded from attack surface reduction rules, the way the Defender policy for it writes the registry.</summary>
    public static byte[] AsrExclusion(string path, DateTime now)
    {
        XElement Item(string key, string name, string type, string value, string image) => new("Registry",
            new XAttribute("clsid", "{9CD4B2F4-923D-47f5-A062-E897DD1DAD50}"), new XAttribute("name", name), new XAttribute("status", name), new XAttribute("image", image),
            new XAttribute("changed", Changed(now)), new XAttribute("uid", Uid()),
            new XElement("Properties", new XAttribute("action", "U"), new XAttribute("displayDecimal", type == "REG_DWORD" ? "1" : "0"), new XAttribute("default", "0"),
                new XAttribute("hive", "HKEY_LOCAL_MACHINE"), new XAttribute("key", key), new XAttribute("name", name), new XAttribute("type", type), new XAttribute("value", value)));
        var doc = new XElement("RegistrySettings", new XAttribute("clsid", "{A3CCFC41-DFDB-43a5-8D26-0FE8B954DA51}"),
            Item(@"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR", "ExploitGuard_ASR_ASROnlyExclusions", "REG_DWORD", "00000001", "17"),
            Item(@"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR\ASROnlyExclusions", path, "REG_SZ", "0", "7"));
        return Bytes(doc);
    }

    private static string Changed(DateTime now) => now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Uid() => $"{{{Guid.NewGuid().ToString().ToUpperInvariant()}}}";

    private static byte[] Bytes(XElement root)
    {
        var text = new XDocument(new XDeclaration("1.0", "utf-8", null), root).Declaration + root.ToString(SaveOptions.DisableFormatting);
        return Encoding.UTF8.GetBytes(text);
    }
}

public static partial class GpoText
{
    private const string AllTools = "{00000000-0000-0000-0000-000000000000}";

    /// <summary>
    /// gPCMachineExtensionNames with these extensions added: one [{extension}{tool}...] group each, sorted as Group Policy
    /// Management keeps them, with the group of every tool ({00000000-...}) first.
    /// </summary>
    public static string AddExtensions(string? existing, IEnumerable<(string Cse, string Tool)> add)
    {
        var groups = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Put(string cse, string? tool)
        {
            if (!groups.TryGetValue(cse, out var tools))
            {
                groups[cse] = tools = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            if (tool is not null)
            {
                tools.Add(tool);
            }
        }
        foreach (System.Text.RegularExpressions.Match g in System.Text.RegularExpressions.Regex.Matches(existing ?? "", @"\[([^\]]*)\]"))
        {
            var ids = System.Text.RegularExpressions.Regex.Matches(g.Groups[1].Value, @"\{[0-9A-Fa-f-]{36}\}").Select(m => m.Value.ToUpperInvariant()).ToList();
            if (ids.Count > 0)
            {
                Put(ids[0], null);
                ids.Skip(1).ToList().ForEach(t => Put(ids[0], t));
            }
        }
        foreach (var (cse, tool) in add)
        {
            Put(cse.ToUpperInvariant(), tool.ToUpperInvariant());
            Put(AllTools, tool.ToUpperInvariant());
        }
        return string.Concat(groups.Select(g => $"[{g.Key}{string.Concat(g.Value)}]"));
    }
}
