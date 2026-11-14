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
                list.Add(new AdSetting(key, "", name, "Couldn't be read", "", "unknown", $"{Ready().Account} isn't allowed to read what this check needs."));
            }
        }
        var accounts = ("account", "accounts");
        var computers = ("computer", "computers");

        // Passwords and lockout: accounts the policy doesn't reach.
        await Try("notreq", "Accounts that don't need a password", async () => Found("notreq", "Accounts that don't need a password",
            await Hits($"(&{People}{Enabled}({BitIs}{PasswordNotRequired}))"), accounts, "None",
            "PASSWD_NOTREQD lets an account have an empty password, whatever the policy says.", "Clear \"password not required\" on each one.", "notreq-off"));
        await Try("neverexp", "Passwords that never expire", async () => Found("neverexp", "Passwords that never expire",
            await Hits($"(&{People}{Enabled}({BitIs}{DontExpire}))"), accounts, "Only service accounts with long random passwords",
            "A password that never changes stays useful to whoever learns it.", "Turn off Password never expires, or give the account a long random password.", "noexpiry-off", "warn"));
        await Try("revaccounts", "Accounts storing reversible passwords", async () => Found("revaccounts", "Accounts storing reversible passwords",
            await Hits($"(&(objectClass=user)({BitIs}{ReversiblePassword}))"), accounts, "None", "Reversible encryption is as good as keeping the password in plain text.",
            "Turn off reversible encryption, then reset the password.", "reversible-off"));

        // Privileged accounts.
        var adminFilter = $"(&{People}{Enabled}(adminCount=1)(!(sAMAccountName=krbtgt)))";
        if (domainSid is not null)
        {
            await Try("builtin", "Built-in Administrator signed in lately", async () =>
            {
                var admin = (await ldap.Find(ldap.BaseDn, $"(objectSid={domainSid}-500)", true, ["lastLogonTimestamp", "userAccountControl"], 1, stop)).FirstOrDefault();
                var last = FileTime(admin?.Number("lastLogonTimestamp"));
                var days = last is { } l ? (int) (now - l).TotalDays : (int?) null;
                list.Add(new AdSetting("builtin", "", "Built-in Administrator signed in lately", days is null ? "Never" : days == 0 ? "Today" : $"{days} days ago", "Not used: each admin has their own account",
                    days is not null && days <= 30 ? "warn" : "good", "Everyone knows its name and its SID never changes, so it's the first account anyone tries.")
                { Fix = days is not null && days <= 30 ? "Give each admin their own account and stop using Administrator." : null });
            });
        }
        await Try("kerbadmin", "Admins with a service name (Kerberoastable)", async () => Found("kerbadmin", "Admins with a service name (Kerberoastable)",
            await Hits($"(&{adminFilter}(servicePrincipalName=*))"), accounts, "None",
            "Anyone signed in can ask for a ticket for it and crack the password offline.", "Take the service name off, or run the service as a group Managed Service Account."));
        await Try("sensitive", "Admins that can be delegated", async () => Found("sensitive", "Admins that can be delegated",
            await Hits($"(&{adminFilter}(!({BitIs}{NotDelegated})))"), accounts, "Sensitive and cannot be delegated",
            "A server trusted for delegation can act as them anywhere.", "Turn on \"Account is sensitive and cannot be delegated\".", "sensitive-on", "warn"));
        if (domainSid is not null)
        {
            await Try("protected", "Admins outside Protected Users", async () =>
            {
                var group = (await ldap.Find(ldap.BaseDn, $"(objectSid={domainSid}-525)", true, ["cn"], 1, stop)).FirstOrDefault();
                if (group is null)
                {
                    list.Add(new AdSetting("protected", "", "Admins outside Protected Users", "There's no Protected Users group", "All admins in Protected Users", "warn",
                        "Protected Users came with Windows Server 2012 R2. Raise the domain to get it.") { Fix = "Raise the domain functional level." });
                    return;
                }
                Found("protected", "Admins outside Protected Users", await Hits($"(&{adminFilter}(!(memberOf={ActiveDirectory.Escape(group.Dn)})))"), accounts, "All admins in Protected Users",
                    "Protected Users stops NTLM, DES, RC4 and delegation for its members.", "Add the admins to Protected Users. Try one first: they can't use NTLM after.", tone: "warn");
            });
            await Try("schema", "Schema Admins has members", async () =>
            {
                var group = (await ldap.Find(ldap.BaseDn, $"(objectSid={domainSid}-518)", true, ["cn"], 1, stop)).FirstOrDefault();
                Found("schema", "Schema Admins has members", group is null ? [] : await Hits($"(memberOf={ActiveDirectory.Escape(group.Dn)})"), ("member", "members"), "Empty until you need it",
                    "Schema changes can't be undone. Add someone only for the change, then take them out.", "Take everyone out of Schema Admins.", tone: "warn");
            });
        }
        await Try("operators", "Operator groups have members", async () =>
        {
            var groups = await ldap.Find(ldap.BaseDn, "(&(objectClass=group)(|(objectSid=S-1-5-32-548)(objectSid=S-1-5-32-549)(objectSid=S-1-5-32-550)(objectSid=S-1-5-32-551)))", true, ["cn"], 10, stop);
            var hits = groups.Count == 0 ? [] : await Hits("(|" + string.Concat(groups.Select(g => $"(memberOf={ActiveDirectory.Escape(g.Dn)})")) + ")");
            Found("operators", "Operator groups have members", hits, ("member", "members"), "Empty",
                "Account, Server, Print and Backup Operators can sign in to domain controllers, and Backup Operators can copy the AD database.", "Give the rights needed by delegation instead.", tone: "warn");
        });
        await Try("dnsadmins", "DnsAdmins has members", async () =>
        {
            var group = (await ldap.Find(ldap.BaseDn, "(&(objectClass=group)(sAMAccountName=DnsAdmins))", true, ["cn"], 1, stop)).FirstOrDefault();
            Found("dnsadmins", "DnsAdmins has members", group is null ? [] : await Hits($"(memberOf={ActiveDirectory.Escape(group.Dn)})"), ("member", "members"), "Empty",
                "DnsAdmins can make the DNS service on domain controllers load any DLL, which runs as SYSTEM.", "Take them out, and change DNS as a Domain Admin instead.", tone: "info");
        });
        await Try("adminold", "Admin passwords older than 3 years", async () => Found("adminold", "Admin passwords older than 3 years",
            await Hits($"(&{adminFilter}(pwdLastSet<={FileTimeFilter(now.AddYears(-3))}))"), accounts, "Changed at least every 3 years",
            "An old admin password has had a long time to leak.", "Set a new password for each one.", tone: "warn"));
        await Try("admininactive", "Admins not signed in for 90 days", async () => Found("admininactive", "Admins not signed in for 90 days",
            await Hits($"(&{adminFilter}(lastLogonTimestamp<={FileTimeFilter(now.AddDays(-90))}))"), accounts, "Turned off",
            "An admin account nobody uses is one nobody notices being used.", "Turn them off.", "disable", "warn"));

        // Kerberos and delegation.
        await Try("unconstrained", "Unconstrained delegation", async () => Found("unconstrained", "Unconstrained delegation",
            await Hits($"(&(objectClass=user)({BitIs}{TrustedForDelegation})(!({BitIs}{DomainController})))"), ("account", "accounts"), "Only domain controllers",
            "Anyone signing in to it leaves a ticket that can be reused anywhere in the domain.", "Switch it to constrained or resource-based delegation."));
        await Try("nopreauth", "Accounts without Kerberos preauthentication", async () => Found("nopreauth", "Accounts without Kerberos preauthentication",
            await Hits($"(&{People}({BitIs}{NoPreauthNeeded}))"), accounts, "None",
            "Anyone can ask for a reply encrypted with its password and crack it offline (AS-REP roasting).", "Turn preauthentication back on.", "preauth-on"));
        await Try("spnusers", "User accounts with a service name", async () => Found("spnusers", "User accounts with a service name",
            await Hits($"(&{People}{Enabled}(servicePrincipalName=*)(!(adminCount=1))(!(sAMAccountName=krbtgt)))"), accounts, "Group Managed Service Accounts",
            "Their tickets can be cracked offline (Kerberoasting), so the password has to be long.", "Move the service to a group Managed Service Account, or give it a 25+ character password.", tone: "warn"));
        await Try("des", "Accounts using DES", async () => Found("des", "Accounts using DES",
            await Hits($"(&(objectClass=user)({BitIs}{DesKeysOnly}))"), accounts, "None", "DES was broken long ago.", "Turn off \"Use only Kerberos DES encryption types\".", "des-off"));

        // Stale objects.
        var day180 = now.AddDays(-180);
        var day90 = now.AddDays(-90);
        await Try("inactiveusers", "Users not signed in for 180 days", async () => Found("inactiveusers", "Users not signed in for 180 days",
            await Hits($"(&{People}{Enabled}(|(lastLogonTimestamp<={FileTimeFilter(day180)})(&(!(lastLogonTimestamp=*))(whenCreated<={StampFilter(day180)}))))"), accounts, "Turned off, then deleted",
            "Old accounts are a way in nobody is watching.", "Turn them off, and delete them after a while.", "disable", "warn"));
        await Try("inactivepcs", "Computers not seen for 90 days", async () => Found("inactivepcs", "Computers not seen for 90 days",
            await Hits($"(&(objectCategory=computer){Enabled}(!({BitIs}{DomainController}))(|(lastLogonTimestamp<={FileTimeFilter(day90)})(&(!(lastLogonTimestamp=*))(whenCreated<={StampFilter(day90)}))))"),
            computers, "Turned off, then deleted", "A computer account is a password too.", "Turn them off, and delete them after a while.", "disable", "warn"));
        await Try("oldos", "Computers on unsupported Windows", async () => Found("oldos", "Computers on unsupported Windows",
            await Hits($"(&(objectCategory=computer){Enabled}(|(operatingSystem=*XP*)(operatingSystem=*Vista*)(operatingSystem=*Windows 7*)(operatingSystem=*Windows 8*)(operatingSystem=Windows 10*)"
                + "(operatingSystem=*2000*)(operatingSystem=*2003*)(operatingSystem=*2008*)(operatingSystem=*2012*)))"), computers, "None",
            "No more security fixes.", "Upgrade or retire them."));
        await Try("sidhistory", "Leftover SID history", async () => Found("sidhistory", "Leftover SID history",
            await Hits("(sIDHistory=*)"), accounts, "None", "An old SID can carry rights from a domain that's gone, and can hide an admin.", "Clear SID history once a migration is done.", tone: "warn"));

        // Domain and forest: how many computers have a LAPS password, and when the domain was last backed up.
        await Try("lapscover", "Computers without a LAPS password", async () =>
        {
            var all = await ldap.Find(ldap.BaseDn, $"(&(objectCategory=computer){Enabled}(!({BitIs}{DomainController})))", true, ["cn"], Limit, stop);
            var without = await Hits($"(&(objectCategory=computer){Enabled}(!({BitIs}{DomainController}))(!(msLAPS-PasswordExpirationTime=*))(!(ms-Mcs-AdmPwdExpirationTime=*)))");
            list.Add(new AdSetting("lapscover", "", "Computers without a LAPS password", without.Count == 0 ? "None" : $"{without.Count} of {all.Count}", "Every computer",
                without.Count == 0 ? "good" : without.Count == all.Count ? "bad" : "warn", "Without it, one local admin password opens every computer.")
            { Found = [.. without.Take(FoundListed)], Fix = without.Count == 0 ? null : "Turn on Windows LAPS for them with a Group Policy." });
        });
        await Try("backup", "Last AD backup", async () =>
        {
            var meta = (await ldap.Find(ldap.BaseDn, "(objectClass=*)", false, ["msDS-ReplAttributeMetaData"], 1, stop)).FirstOrDefault()?.Texts("msDS-ReplAttributeMetaData").Select(Meta)
                .FirstOrDefault(m => m.Name.Equals("dSASignature", StringComparison.OrdinalIgnoreCase));
            var at = meta is { At.Year: > 1601 } m ? m.At : (DateTime?) null;
            var days = at is { } a ? (int) (now - a).TotalDays : (int?) null;
            list.Add(new AdSetting("backup", "", "Last AD backup", days is null ? "Never" : days == 0 ? "Today" : $"{days} days ago", "Within 7 days",
                days is null ? "bad" : days <= 7 ? "good" : "warn", "A domain with no recent backup can't come back from ransomware or a bad change.")
            { Fix = days is null or > 7 ? "Back up the system state of a domain controller every day (Windows Server Backup works)." : null });
        });

        // Domain controllers: SYSVOL replication and subnets.
        await Try("sysvol", "SYSVOL replication", async () =>
        {
            var frs = (await ldap.Find($"CN=File Replication Service,CN=System,{ldap.BaseDn}", "(objectClass=*)", false, ["cn"], 1, stop)).Count > 0;
            var flags = (await ldap.Find($"CN=DFSR-GlobalSettings,CN=System,{ldap.BaseDn}", "(objectClass=*)", false, ["msDFSR-Flags"], 1, stop)).FirstOrDefault()?.Number("msDFSR-Flags");
            var old = frs && flags != 48;
            list.Add(new AdSetting("sysvol", "", "SYSVOL replication", old ? "FRS (the old one)" : "DFS Replication", "DFS Replication", old ? "bad" : "good",
                "FRS is gone from new Windows Server, and a domain still on it can't add one.") { Fix = old ? "Move SYSVOL to DFS Replication with dfsrmig." : null });
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
                        outside.Add(Member(dc, ldap.BaseDn) with { Where = $"{string.Join(", ", addresses)} has no subnet" });
                    }
                }
                Found("subnets", "Domain controllers in an AD subnet", outside, ("controller", "controllers"), "All in a subnet",
                    "Without a subnet, computers may sign in at the wrong site and DCs may not replicate the best way.", "Add the subnet in Active Directory Sites and Services.", tone: "warn");
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
                Found("esc1", "Templates that let the requester choose the name", [.. templates.Where(t => Open(t) && ((t.Number("msPKI-Certificate-Name-Flag") ?? 0) & 1) != 0
                    && (ekus(t).Count == 0 || ekus(t).Any(e => signIn.Contains(e)))).Select(T)], ("template", "templates"), "None for sign-in certificates",
                    "If ordinary users can enroll, anyone can get a certificate as a Domain Admin (ESC1).", "Turn off \"Supply in the request\", or require CA manager approval. Check who can enroll.", tone: "warn");
                Found("anypurpose", "Any-purpose templates", [.. templates.Where(t => Open(t) && (ekus(t).Count == 0 || ekus(t).Contains("2.5.29.37.0"))).Select(T)], ("template", "templates"), "None",
                    "A certificate for any purpose can sign in as whoever it names.", "Give the template only the purposes it needs, or require approval.", tone: "warn");
            });
        }

        // Trusts.
        await Try("trusts", "Trusts", async () =>
        {
            var trusts = await ldap.Find($"CN=System,{ldap.BaseDn}", "(objectClass=trustedDomain)", true, ["cn", "trustAttributes", "trustDirection", "whenChanged"], 50, stop);
            var risky = trusts.Where(t => ((t.Number("trustAttributes") ?? 0) & 0xC) == 0 || When(t.Text("whenChanged")) is { } w && (now - w).TotalDays > 45).ToList();
            list.Add(new AdSetting("trusts", "", "Trusts", trusts.Count == 0 ? "None" : Many(trusts.Count, "trust", "trusts"), "Only the ones you need, with SID filtering", risky.Count == 0 ? "good" : "warn",
                "Each trust is a way in from another domain; one not used in 45 days or without SID filtering is worth a look.")
            {
                Found = [.. trusts.Select(t => new AdMember(t.Dn, t.Text("cn") ?? Ldap.Name(t.Dn), "trust", null,
                    (((t.Number("trustAttributes") ?? 0) & 0xC) == 0 ? "no SID filtering" : "SID filtering on") + (When(t.Text("whenChanged")) is { } w && (now - w).TotalDays > 45 ? ", not used lately" : "")))],
                Fix = risky.Count == 0 ? null : "Remove trusts you don't need, and turn on SID filtering (netdom trust /quarantine).",
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
            var open = zones.Where(z => ZoneProperty(z, 0x02) == 1).Select(z => Z(z, "Takes nonsecure and secure updates")).ToList();
            var noAging = zones.Where(z => ZoneProperty(z, 0x10) is null or 0).Select(z => Z(z, "Aging off")).ToList();
            Found("dnsupdate", "DNS zones taking insecure updates", open, ("zone", "zones"), "Secure only", "Anyone on the network can change their records, even the domain controllers' own.",
                "Set dynamic updates to \"Secure only\" in DNS Manager.");
            Found("dnsaging", "DNS zones without aging", zones.Count == 0 ? [] : noAging, ("zone", "zones"), "Aging on, with scavenging on one DNS server",
                "Old records pile up and point at the wrong computers.", "Turn on aging for the zone, and scavenging on one DNS server.", tone: "warn");
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
