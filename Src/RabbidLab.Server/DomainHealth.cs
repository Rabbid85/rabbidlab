using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace RabbidLab.Server;

/// <summary>
/// Domain health: the checks Microsoft Defender for Identity, PingCastle and Purple Knight make, the ones Active Directory
/// answers over LDAP as the service account. Each lists what it found (up to 50), why it matters and how it's fixed.
/// </summary>
public sealed partial class DirectoryTools
{
    private const string BitIs = "userAccountControl:1.2.840.113556.1.4.803:=";
    private const string People = "(objectCategory=person)(objectClass=user)";
    private const string Enabled = "(!(userAccountControl:1.2.840.113556.1.4.803:=2))";
    private const int FoundListed = 50;
    private const int TrustedForDelegation = 0x80000;
    private const int DomainController = 0x2000;

    // Where each check sits, and whose check it is.
    private static readonly Dictionary<string, (string Area, string Source)> Placed = new()
    {
        ["length"] = ("Passwords and lockout", "Microsoft password guidance"), ["complex"] = ("Passwords and lockout", "Default Domain Policy"),
        ["reversible"] = ("Passwords and lockout", "PingCastle"), ["history"] = ("Passwords and lockout", "CIS benchmark"), ["maxage"] = ("Passwords and lockout", "Microsoft password guidance"),
        ["minage"] = ("Passwords and lockout", "CIS benchmark"), ["lockout"] = ("Passwords and lockout", "CIS benchmark"), ["duration"] = ("Passwords and lockout", "CIS benchmark"),
        ["pso"] = ("Passwords and lockout", "Microsoft"), ["notreq"] = ("Passwords and lockout", "PingCastle S-PwdNotRequired"),
        ["neverexp"] = ("Passwords and lockout", "PingCastle S-PwdNeverExpires, Defender for Identity"), ["revaccounts"] = ("Passwords and lockout", "PingCastle S-Reversible"),
        ["admins"] = ("Privileged accounts", "PingCastle P-AdminNum"), ["builtin"] = ("Privileged accounts", "PingCastle P-AdminLogin"),
        ["kerbadmin"] = ("Privileged accounts", "PingCastle P-Kerberoasting, Defender for Identity"), ["sensitive"] = ("Privileged accounts", "PingCastle P-Delegated, Defender for Identity"),
        ["protected"] = ("Privileged accounts", "PingCastle P-ProtectedUsers"), ["schema"] = ("Privileged accounts", "PingCastle P-SchemaAdmin"),
        ["operators"] = ("Privileged accounts", "PingCastle P-OperatorsEmpty"), ["dnsadmins"] = ("Privileged accounts", "PingCastle P-DNSAdmin"),
        ["adminold"] = ("Privileged accounts", "PingCastle P-AdminPwdTooOld"), ["admininactive"] = ("Privileged accounts", "PingCastle P-Inactive"),
        ["krbtgt"] = ("Kerberos and delegation", "PingCastle A-Krbtgt, Defender for Identity"), ["unconstrained"] = ("Kerberos and delegation", "PingCastle P-UnconstrainedDelegation, Purple Knight"),
        ["nopreauth"] = ("Kerberos and delegation", "PingCastle S-NoPreAuth, Defender for Identity"), ["spnusers"] = ("Kerberos and delegation", "Defender for Identity, Purple Knight"),
        ["des"] = ("Kerberos and delegation", "PingCastle S-DesEnabled"),
        ["inactiveusers"] = ("Stale objects", "PingCastle S-Inactive, Defender for Identity"), ["inactivepcs"] = ("Stale objects", "PingCastle S-C-Inactive"),
        ["oldos"] = ("Stale objects", "PingCastle S-OS"), ["sidhistory"] = ("Stale objects", "PingCastle S-SIDHistory"),
        ["quota"] = ("Domain and forest", "PingCastle S-ADRegistration"), ["guest"] = ("Domain and forest", "Defender for Identity"), ["recycle"] = ("Domain and forest", "PingCastle P-RecycleBin"),
        ["tombstone"] = ("Domain and forest", "Microsoft"), ["dfl"] = ("Domain and forest", "PingCastle S-FunctionalLevel"), ["ffl"] = ("Domain and forest", "PingCastle S-FunctionalLevel"),
        ["laps"] = ("Domain and forest", "PingCastle A-LAPS-Not-Installed, Defender for Identity"), ["lapscover"] = ("Domain and forest", "PingCastle A-LAPS-Not-Installed"),
        ["backup"] = ("Domain and forest", "PingCastle A-BackupMetadata"),
        ["dcs"] = ("Domain controllers", "PingCastle A-NotEnoughDC"), ["dcos"] = ("Domain controllers", "PingCastle S-DC"), ["sysvol"] = ("Domain controllers", "PingCastle A-NTFRSOnSysvol"),
        ["subnets"] = ("Domain controllers", "PingCastle S-DC-SubnetMissing"),
        ["adcs"] = ("Certificates (AD CS)", "PingCastle, Defender for Identity"), ["esc1"] = ("Certificates (AD CS)", "PingCastle A-CertTempCustomSubject, Defender for Identity"),
        ["anypurpose"] = ("Certificates (AD CS)", "PingCastle A-CertTempAnyPurpose"),
        ["trusts"] = ("Trusts", "PingCastle T-*"),
        ["dnsupdate"] = ("DNS", "PingCastle A-DnsZoneUpdate"), ["dnsaging"] = ("DNS", "Microsoft DNS guidance"),
    };

    private static string Many(int n, string one, string many) => $"{n.ToString("N0", CultureInfo.InvariantCulture)}{(n >= Limit ? "+" : "")} {(n == 1 ? one : many)}";

    private static string FileTimeFilter(DateTime when) => when.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture);

    private static string StampFilter(DateTime when) => when.ToString("yyyyMMddHHmmss.0Z", CultureInfo.InvariantCulture);

    /// <summary>The checks beyond the domain's own settings, added to list; then every check gets its area and source.</summary>
    private async Task HealthChecks(ILdap ldap, LdapRow? rootDse, string? config, string? domainSid, List<AdSetting> list, CancellationToken stop)
    {
        var now = DateTime.UtcNow;
        async Task<List<AdMember>> Hits(string filter, string? root = null) =>
            [.. (await ldap.Find(root ?? ldap.BaseDn, filter, true, MemberAttributes, Limit, stop)).Where(r => !DnsPartition(r.Dn)).Select(r => Member(r, ldap.BaseDn))];
        void Found(string key, string name, List<AdMember> hits, (string One, string Many) noun, string rec, string why, string fix, string? act = null, string tone = "bad") =>
            list.Add(new AdSetting(key, "", name, hits.Count == 0 ? "None" : Many(hits.Count, noun.One, noun.Many), rec, hits.Count == 0 ? "good" : tone, why)
            { Found = [.. hits.Take(FoundListed)], Fix = hits.Count == 0 ? null : fix, Act = hits.Count == 0 ? null : act });
        // A check that couldn't be read says so, rather than the whole page failing.
        async Task Try(string key, string name, Func<Task> check)
        {
            try
            {
                await check();
            }
            catch (LdapRefused)
            {
                list.Add(new AdSetting(key, "", name, "Could not be read", "", "unknown", $"{Ready().Account} does not have permission to read the data this check needs."));
            }
        }
        var accounts = ("account", "accounts");
        var computers = ("computer", "computers");

        // Passwords and lockout: accounts the policy doesn't reach.
        await Try("notreq", "Accounts that do not require a password", async () => Found("notreq", "Accounts that do not require a password",
            await Hits($"(&{People}{Enabled}({BitIs}{PasswordNotRequired}))"), accounts, "None",
            "PASSWD_NOTREQD allows an empty password regardless of the password policy.", "Clear \"Password not required\" on each account.", "notreq-off"));
        await Try("neverexp", "Passwords that never expire", async () => Found("neverexp", "Passwords that never expire",
            await Hits($"(&{People}{Enabled}({BitIs}{DontExpire}))"), accounts, "Only service accounts with long random passwords",
            "A password that never changes stays valid for anyone who obtains it.", "Clear \"Password never expires\", or set a long random password.", "noexpiry-off", "warn"));
        await Try("revaccounts", "Accounts with reversible password encryption", async () => Found("revaccounts", "Accounts with reversible password encryption",
            await Hits($"(&(objectClass=user)({BitIs}{ReversiblePassword}))"), accounts, "None", "Reversible encryption is equivalent to storing the password in plain text.",
            "Disable reversible encryption, then reset the password.", "reversible-off"));

        // Privileged accounts.
        var adminFilter = $"(&{People}{Enabled}(adminCount=1)(!(sAMAccountName=krbtgt)))";
        if (domainSid is not null)
        {
            await Try("builtin", "Built-in Administrator used recently", async () =>
            {
                var admin = (await ldap.Find(ldap.BaseDn, $"(objectSid={domainSid}-500)", true, ["lastLogonTimestamp", "userAccountControl"], 1, stop)).FirstOrDefault();
                var last = FileTime(admin?.Number("lastLogonTimestamp"));
                var days = last is { } l ? (int) (now - l).TotalDays : (int?) null;
                list.Add(new AdSetting("builtin", "", "Built-in Administrator used recently", days is null ? "Never" : days == 0 ? "Today" : $"{days} days ago", "Not used. Each admin has a personal account",
                    days is not null && days <= 30 ? "warn" : "good", "Its name and SID are well known, so it is the first account attackers try.")
                { Fix = days is not null && days <= 30 ? "Give each admin a personal account and stop using the built-in Administrator." : null });
            });
        }
        await Try("kerbadmin", "Admins with an SPN (Kerberoastable)", async () => Found("kerbadmin", "Admins with an SPN (Kerberoastable)",
            await Hits($"(&{adminFilter}(servicePrincipalName=*))"), accounts, "None",
            "Any signed-in user can request a service ticket for the account and crack its password offline.", "Remove the SPN, or run the service as a group Managed Service Account."));
        await Try("sensitive", "Admins that can be delegated", async () => Found("sensitive", "Admins that can be delegated",
            await Hits($"(&{adminFilter}(!({BitIs}{NotDelegated})))"), accounts, "Sensitive and cannot be delegated",
            "A server trusted for delegation can impersonate them anywhere.", "Enable \"Account is sensitive and cannot be delegated\".", "sensitive-on", "warn"));
        if (domainSid is not null)
        {
            await Try("protected", "Admins not in Protected Users", async () =>
            {
                var group = (await ldap.Find(ldap.BaseDn, $"(objectSid={domainSid}-525)", true, ["cn"], 1, stop)).FirstOrDefault();
                if (group is null)
                {
                    list.Add(new AdSetting("protected", "", "Admins not in Protected Users", "Protected Users group not found", "All admins in Protected Users", "warn",
                        "Protected Users requires the Windows Server 2012 R2 domain functional level.") { Fix = "Raise the domain functional level." });
                    return;
                }
                Found("protected", "Admins not in Protected Users", await Hits($"(&{adminFilter}(!(memberOf={ActiveDirectory.Escape(group.Dn)})))"), accounts, "All admins in Protected Users",
                    "Protected Users blocks NTLM, DES, RC4 and delegation for its members.", "Add the admins to Protected Users. Test with one account first, because members cannot use NTLM.", tone: "warn");
            });
            await Try("schema", "Schema Admins has members", async () =>
            {
                var group = (await ldap.Find(ldap.BaseDn, $"(objectSid={domainSid}-518)", true, ["cn"], 1, stop)).FirstOrDefault();
                Found("schema", "Schema Admins has members", group is null ? [] : await Hits($"(memberOf={ActiveDirectory.Escape(group.Dn)})"), ("member", "members"), "Empty until needed",
                    "Schema changes cannot be undone. Add a member only for the change, then remove them.", "Remove all members from Schema Admins.", tone: "warn");
            });
        }
        await Try("operators", "Operator groups have members", async () =>
        {
            var groups = await ldap.Find(ldap.BaseDn, "(&(objectClass=group)(|(objectSid=S-1-5-32-548)(objectSid=S-1-5-32-549)(objectSid=S-1-5-32-550)(objectSid=S-1-5-32-551)))", true, ["cn"], 10, stop);
            var hits = groups.Count == 0 ? [] : await Hits("(|" + string.Concat(groups.Select(g => $"(memberOf={ActiveDirectory.Escape(g.Dn)})")) + ")");
            Found("operators", "Operator groups have members", hits, ("member", "members"), "Empty",
                "Account, Server, Print and Backup Operators can sign in to domain controllers, and Backup Operators can copy the AD database.", "Delegate only the permissions needed instead.", tone: "warn");
        });
        await Try("dnsadmins", "DnsAdmins has members", async () =>
        {
            var group = (await ldap.Find(ldap.BaseDn, "(&(objectClass=group)(sAMAccountName=DnsAdmins))", true, ["cn"], 1, stop)).FirstOrDefault();
            Found("dnsadmins", "DnsAdmins has members", group is null ? [] : await Hits($"(memberOf={ActiveDirectory.Escape(group.Dn)})"), ("member", "members"), "Empty",
                "Members of DnsAdmins can make the DNS service on domain controllers load any DLL, which runs as SYSTEM.", "Remove the members and manage DNS as a Domain Admin instead.", tone: "info");
        });
        await Try("adminold", "Admin passwords older than 3 years", async () => Found("adminold", "Admin passwords older than 3 years",
            await Hits($"(&{adminFilter}(pwdLastSet<={FileTimeFilter(now.AddYears(-3))}))"), accounts, "Changed at least every 3 years",
            "An old admin password has had years to leak.", "Reset the password for each account.", tone: "warn"));
        await Try("admininactive", "Admins not signed in for 90 days", async () => Found("admininactive", "Admins not signed in for 90 days",
            await Hits($"(&{adminFilter}(lastLogonTimestamp<={FileTimeFilter(now.AddDays(-90))}))"), accounts, "Disabled",
            "Misuse of an unused admin account is unlikely to be noticed.", "Disable them.", "disable", "warn"));

        // Kerberos and delegation.
        await Try("unconstrained", "Unconstrained delegation", async () => Found("unconstrained", "Unconstrained delegation",
            await Hits($"(&(objectClass=user)({BitIs}{TrustedForDelegation})(!({BitIs}{DomainController})))"), ("account", "accounts"), "Only domain controllers",
            "Every user who signs in to it leaves a ticket that can be reused anywhere in the domain.", "Change it to constrained or resource-based delegation."));
        await Try("nopreauth", "Accounts without Kerberos preauthentication", async () => Found("nopreauth", "Accounts without Kerberos preauthentication",
            await Hits($"(&{People}({BitIs}{NoPreauthNeeded}))"), accounts, "None",
            "Anyone can request a reply encrypted with the account's password and crack it offline (AS-REP roasting).", "Require Kerberos preauthentication.", "preauth-on"));
        await Try("spnusers", "User accounts with an SPN", async () => Found("spnusers", "User accounts with an SPN",
            await Hits($"(&{People}{Enabled}(servicePrincipalName=*)(!(adminCount=1))(!(sAMAccountName=krbtgt)))"), accounts, "Group Managed Service Accounts",
            "Their tickets can be cracked offline (Kerberoasting), so the password must be long.", "Move the service to a group Managed Service Account, or set a password of 25 or more characters.", tone: "warn"));
        await Try("des", "Accounts using DES", async () => Found("des", "Accounts using DES",
            await Hits($"(&(objectClass=user)({BitIs}{DesKeysOnly}))"), accounts, "None", "DES encryption is obsolete and easily broken.", "Clear \"Use only Kerberos DES encryption types\".", "des-off"));

        // Stale objects.
        var day180 = now.AddDays(-180);
        var day90 = now.AddDays(-90);
        await Try("inactiveusers", "Users not signed in for 180 days", async () => Found("inactiveusers", "Users not signed in for 180 days",
            await Hits($"(&{People}{Enabled}(|(lastLogonTimestamp<={FileTimeFilter(day180)})(&(!(lastLogonTimestamp=*))(whenCreated<={StampFilter(day180)}))))"), accounts, "Disabled, then deleted",
            "Unused accounts are an unmonitored way in.", "Disable them, then delete them after a waiting period.", "disable", "warn"));
        await Try("inactivepcs", "Computers inactive for 90 days", async () => Found("inactivepcs", "Computers inactive for 90 days",
            await Hits($"(&(objectCategory=computer){Enabled}(!({BitIs}{DomainController}))(|(lastLogonTimestamp<={FileTimeFilter(day90)})(&(!(lastLogonTimestamp=*))(whenCreated<={StampFilter(day90)}))))"),
            computers, "Disabled, then deleted", "Each computer account has a password that can be misused.", "Disable them, then delete them after a waiting period.", "disable", "warn"));
        await Try("oldos", "Computers running unsupported Windows", async () => Found("oldos", "Computers running unsupported Windows",
            await Hits($"(&(objectCategory=computer){Enabled}(|(operatingSystem=*XP*)(operatingSystem=*Vista*)(operatingSystem=*Windows 7*)(operatingSystem=*Windows 8*)(operatingSystem=Windows 10*)"
                + "(operatingSystem=*2000*)(operatingSystem=*2003*)(operatingSystem=*2008*)(operatingSystem=*2012*)))"), computers, "None",
            "These versions no longer receive security updates.", "Upgrade or retire them."));
        await Try("sidhistory", "Accounts with SID history", async () => Found("sidhistory", "Accounts with SID history",
            await Hits("(sIDHistory=*)"), accounts, "None", "An old SID can carry permissions from a former domain and can hide admin rights.", "Clear SID history after the migration is complete.", tone: "warn"));

        // Domain and forest: how many computers have a LAPS password, and when the domain was last backed up.
        await Try("lapscover", "Computers without a LAPS password", async () =>
        {
            var all = await ldap.Find(ldap.BaseDn, $"(&(objectCategory=computer){Enabled}(!({BitIs}{DomainController})))", true, ["cn"], Limit, stop);
            var without = await Hits($"(&(objectCategory=computer){Enabled}(!({BitIs}{DomainController}))(!(msLAPS-PasswordExpirationTime=*))(!(ms-Mcs-AdmPwdExpirationTime=*)))");
            list.Add(new AdSetting("lapscover", "", "Computers without a LAPS password", without.Count == 0 ? "None" : $"{without.Count} of {all.Count}", "All computers",
                without.Count == 0 ? "good" : without.Count == all.Count ? "bad" : "warn", "Without LAPS, one local admin password can unlock every computer.")
            { Found = [.. without.Take(FoundListed)], Fix = without.Count == 0 ? null : "Enable Windows LAPS for these computers with a Group Policy." });
        });
        await Try("backup", "Last AD backup", async () =>
        {
            var meta = (await ldap.Find(ldap.BaseDn, "(objectClass=*)", false, ["msDS-ReplAttributeMetaData"], 1, stop)).FirstOrDefault()?.Texts("msDS-ReplAttributeMetaData").Select(Meta)
                .FirstOrDefault(m => m.Name.Equals("dSASignature", StringComparison.OrdinalIgnoreCase));
            var at = meta is { At.Year: > 1601 } m ? m.At : (DateTime?) null;
            var days = at is { } a ? (int) (now - a).TotalDays : (int?) null;
            list.Add(new AdSetting("backup", "", "Last AD backup", days is null ? "Never" : days == 0 ? "Today" : $"{days} days ago", "Within 7 days",
                days is null ? "bad" : days <= 7 ? "good" : "warn", "Without a recent backup, the domain cannot be recovered from ransomware or a bad change.")
            { Fix = days is null or > 7 ? "Back up the system state of a domain controller daily (for example, with Windows Server Backup)." : null });
        });

        // Domain controllers: SYSVOL replication and subnets.
        await Try("sysvol", "SYSVOL replication", async () =>
        {
            var frs = (await ldap.Find($"CN=File Replication Service,CN=System,{ldap.BaseDn}", "(objectClass=*)", false, ["cn"], 1, stop)).Count > 0;
            var flags = (await ldap.Find($"CN=DFSR-GlobalSettings,CN=System,{ldap.BaseDn}", "(objectClass=*)", false, ["msDFSR-Flags"], 1, stop)).FirstOrDefault()?.Number("msDFSR-Flags");
            var old = frs && flags != 48;
            list.Add(new AdSetting("sysvol", "", "SYSVOL replication", old ? "FRS (deprecated)" : "DFS Replication", "DFS Replication", old ? "bad" : "good",
                "FRS is removed from current Windows Server versions, and a domain that still uses it cannot add new domain controllers.") { Fix = old ? "Migrate SYSVOL to DFS Replication with dfsrmig." : null });
        });
        if (config is not null)
        {
            await Try("subnets", "Domain controllers in an AD subnet", async () =>
            {
                var nets = (await ldap.Find($"CN=Subnets,CN=Sites,{config}", "(objectClass=subnet)", true, ["cn"], Limit, stop)).Select(s => s.Text("cn") ?? Ldap.Name(s.Dn)).ToList();
                var dcs = await ldap.Find(ldap.BaseDn, $"(&(objectCategory=computer)({BitIs}{DomainController}))", true, ["cn", "dNSHostName", "objectClass", "sAMAccountName"], 50, stop);
                var outside = new List<AdMember>();
                foreach (var dc in dcs)
                {
                    var addresses = dc.Text("dNSHostName") is { } host ? await Addresses(host, stop) : [];
                    if (addresses.Count > 0 && !addresses.Any(ip => nets.Any(n => InSubnet(ip, n))))
                    {
                        outside.Add(Member(dc, ldap.BaseDn) with { Where = $"No subnet defined for {string.Join(", ", addresses)}" });
                    }
                }
                Found("subnets", "Domain controllers in an AD subnet", outside, ("controller", "controllers"), "All in a defined subnet",
                    "Without a subnet, clients may authenticate at the wrong site and replication may not be optimal.", "Add the subnet in Active Directory Sites and Services.", tone: "warn");
            });

            // Certificates (AD CS): templates a requester can abuse, on a CA that issues them.
            await Try("adcs", "Certificates (AD CS)", async () =>
            {
                var services = await ldap.Find($"CN=Enrollment Services,CN=Public Key Services,CN=Services,{config}", "(objectClass=pKIEnrollmentService)", true, ["cn", "certificateTemplates"], 50, stop);
                if (services.Count == 0)
                {
                    list.Add(new AdSetting("adcs", "", "Certificates (AD CS)", "Not installed", "", "info", "No certificate authority is published in Active Directory, so there are no templates to check."));
                    return;
                }
                var published = services.SelectMany(s => s.Texts("certificateTemplates").Select(t => (Template: t, Ca: s.Text("cn") ?? ""))).ToLookup(t => t.Template, t => t.Ca, StringComparer.OrdinalIgnoreCase);
                var templates = await ldap.Find($"CN=Certificate Templates,CN=Public Key Services,CN=Services,{config}", "(objectClass=pKICertificateTemplate)", true,
                    ["cn", "msPKI-Certificate-Name-Flag", "pKIExtendedKeyUsage", "msPKI-Enrollment-Flag", "msPKI-RA-Signature"], Limit, stop);
                AdMember T(LdapRow t) => new(t.Dn, t.Text("cn") ?? Ldap.Name(t.Dn), "cert", null, $"Issued by {string.Join(", ", published[t.Text("cn") ?? ""])}");
                bool Open(LdapRow t) => published.Contains(t.Text("cn") ?? "") && ((t.Number("msPKI-Enrollment-Flag") ?? 0) & 2) == 0 && (t.Number("msPKI-RA-Signature") ?? 0) == 0;
                string[] signIn = ["1.3.6.1.5.5.7.3.2", "1.3.6.1.5.2.3.4", "1.3.6.1.4.1.311.20.2.2", "2.5.29.37.0"];
                var ekus = (LdapRow t) => t.Texts("pKIExtendedKeyUsage").ToList();
                list.Add(new AdSetting("adcs", "", "Certificates (AD CS)", $"{services.Count} {(services.Count == 1 ? "certificate authority" : "certificate authorities")}", "", "good",
                    "Certificate templates are checked below."));
                Found("esc1", "Templates where the requester supplies the subject", [.. templates.Where(t => Open(t) && ((t.Number("msPKI-Certificate-Name-Flag") ?? 0) & 1) != 0
                    && (ekus(t).Count == 0 || ekus(t).Any(e => signIn.Contains(e)))).Select(T)], ("template", "templates"), "None for sign-in certificates",
                    "If ordinary users can enroll, any user can obtain a certificate as a Domain Admin (ESC1).", "Clear \"Supply in the request\", or require CA certificate manager approval. Review enrollment permissions.", tone: "warn");
                Found("anypurpose", "Any-purpose templates", [.. templates.Where(t => Open(t) && (ekus(t).Count == 0 || ekus(t).Contains("2.5.29.37.0"))).Select(T)], ("template", "templates"), "None",
                    "An any-purpose certificate can be used to sign in as the account it names.", "Limit the template to the purposes it needs, or require approval.", tone: "warn");
            });
        }

        // Trusts.
        await Try("trusts", "Trusts", async () =>
        {
            var trusts = await ldap.Find($"CN=System,{ldap.BaseDn}", "(objectClass=trustedDomain)", true, ["cn", "trustAttributes", "trustDirection", "whenChanged"], 50, stop);
            var risky = trusts.Where(t => ((t.Number("trustAttributes") ?? 0) & 0xC) == 0 || When(t.Text("whenChanged")) is { } w && (now - w).TotalDays > 45).ToList();
            list.Add(new AdSetting("trusts", "", "Trusts", trusts.Count == 0 ? "None" : Many(trusts.Count, "trust", "trusts"), "Only required trusts, with SID filtering", risky.Count == 0 ? "good" : "warn",
                "Each trust is a path in from another domain. Review trusts unused for 45 days or without SID filtering.")
            {
                Found = [.. trusts.Select(t => new AdMember(t.Dn, t.Text("cn") ?? Ldap.Name(t.Dn), "trust", null,
                    (((t.Number("trustAttributes") ?? 0) & 0xC) == 0 ? "SID filtering disabled" : "SID filtering enabled") + (When(t.Text("whenChanged")) is { } w && (now - w).TotalDays > 45 ? "; unused for 45+ days" : "")))],
                Fix = risky.Count == 0 ? null : "Remove unneeded trusts and enable SID filtering (netdom trust /quarantine).",
            });
        });

        // DNS: the zones kept in Active Directory, from their own properties.
        await Try("dnsupdate", "DNS zones", async () =>
        {
            var forest = rootDse?.Text("rootDomainNamingContext") ?? ldap.BaseDn;
            var zones = new List<LdapRow>();
            foreach (var root in new[] { $"DC=DomainDnsZones,{ldap.BaseDn}", $"DC=ForestDnsZones,{forest}" })
            {
                try
                {
                    zones.AddRange(await ldap.Find(root, "(objectClass=dnsZone)", true, ["dc", "dNSProperty"], 500, stop));
                }
                catch (LdapRefused)
                {
                    // That partition isn't on this DC, or can't be read.
                }
            }
            zones = [.. zones.Where(z => z.Text("dc") is { } n && n != "RootDNSServers" && !n.StartsWith("..", StringComparison.Ordinal))];
            AdMember Z(LdapRow z, string where) => new(z.Dn, z.Text("dc") ?? Ldap.Name(z.Dn), "zone", null, where);
            var open = zones.Where(z => ZoneProperty(z, 0x02) == 1).Select(z => Z(z, "Nonsecure and secure updates allowed")).ToList();
            var noAging = zones.Where(z => ZoneProperty(z, 0x10) is null or 0).Select(z => Z(z, "Aging disabled")).ToList();
            Found("dnsupdate", "DNS zones allowing nonsecure updates", open, ("zone", "zones"), "Secure only", "Anyone on the network can change records in these zones, including domain controller records.",
                "Set dynamic updates to \"Secure only\" in DNS Manager.");
            Found("dnsaging", "DNS zones without aging", zones.Count == 0 ? [] : noAging, ("zone", "zones"), "Aging enabled, with scavenging on one DNS server",
                "Stale records accumulate and resolve to the wrong computers.", "Enable aging on the zone and scavenging on one DNS server.", tone: "warn");
        });

        // Where each one sits and whose check it is, area by area.
        for (var i = 0; i < list.Count; i++)
        {
            if (Placed.TryGetValue(list[i].Key, out var p))
            {
                list[i] = list[i] with { Area = p.Area, Source = p.Source };
            }
        }
        var sorted = list.OrderBy(s => Array.IndexOf(Areas, s.Area) is var at and >= 0 ? at : Areas.Length).ToList();
        list.Clear();
        list.AddRange(sorted);
    }

    private static readonly string[] Areas = ["Passwords and lockout", "Privileged accounts", "Kerberos and delegation", "Stale objects", "Domain and forest", "Domain controllers",
        "Certificates (AD CS)", "Trusts", "DNS"];

    /// <summary>A DNS zone property from dNSProperty (DNS_RPC_PROPERTY: lengths, flag, version, id, then the data), as a number; null when it isn't there.</summary>
    private static long? ZoneProperty(LdapRow zone, int id)
    {
        foreach (var b in zone.AllBytes("dNSProperty"))
        {
            if (b.Length >= 21 && BitConverter.ToInt32(b, 16) == id)
            {
                var length = BitConverter.ToInt32(b, 0);
                return length >= 4 && b.Length >= 24 ? BitConverter.ToUInt32(b, 20) : length >= 1 ? b[20] : null;
            }
        }
        return null;
    }

    private static async Task<List<string>> Addresses(string host, CancellationToken stop)
    {
        try
        {
            return [.. (await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, stop)).Select(a => a.ToString())];
        }
        catch (SocketException)
        {
            return [];
        }
    }

    /// <summary>Whether an IPv4 address is in a subnet written as 192.168.2.0/24.</summary>
    private static bool InSubnet(string address, string subnet)
    {
        var slash = subnet.IndexOf('/');
        if (slash < 0 || !IPAddress.TryParse(address, out var ip) || !IPAddress.TryParse(subnet[..slash], out var net) || !int.TryParse(subnet[(slash + 1)..], out var bits)
            || ip.AddressFamily != AddressFamily.InterNetwork || net.AddressFamily != AddressFamily.InterNetwork || bits is < 0 or > 32)
        {
            return false;
        }
        var mask = bits == 0 ? 0u : uint.MaxValue << (32 - bits);
        uint Value(IPAddress a) => (uint) IPAddress.NetworkToHostOrder(BitConverter.ToInt32(a.GetAddressBytes(), 0));
        return (Value(ip) & mask) == (Value(net) & mask);
    }
}
