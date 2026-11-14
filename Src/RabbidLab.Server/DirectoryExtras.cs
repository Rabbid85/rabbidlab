using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>
/// One domain setting beside what's recommended. Tone: good, warn, bad, or unknown when it couldn't be read. Edit: number
/// or switch when it can be changed here, with its Value (1 or 0 for a switch), Min, Max and Unit; How says how it's
/// changed otherwise.
/// </summary>
public sealed record AdSetting(string Key, string Area, string Name, string Now, string Recommended, string Tone, string About,
    string? Edit = null, long? Value = null, int? Min = null, int? Max = null, string? Unit = null, string? How = null)
{
    /// <summary>What the check found (up to 50), what fixes it, whose check it is, and the fix it can do here for what it found.</summary>
    public List<AdMember>? Found { get; init; }

    public string? Fix { get; init; }

    public string? Source { get; init; }

    public string? Act { get; init; }
}

/// <summary>Something that changed in Active Directory: made, changed or deleted, when, and which attributes (when AD says).</summary>
public sealed record AdChange(string Dn, string Name, string Kind, string Where, DateTime At, string What, List<string> Attributes, string? By = null);

public sealed record DeletedObject(string Dn, string Name, string Kind, string? WasIn, DateTime? Deleted, string? Username);

/// <summary>The forest's AD Recycle Bin: whether it's on, how long deleted things can come back (days), what's in it, and whether there's more than was sent.</summary>
public sealed record RecycleState(bool On, int Lifetime, List<DeletedObject> Items, string? Problem, bool More = false);

public sealed record AdPlaceItem(string Dn, string Name, string Kind, bool Opens, string? Description);

/// <summary>A place in the tree and what's directly in it.</summary>
public sealed record AdPlace(string Dn, string Name, string Path, string? Parent, List<AdPlaceItem> Items, bool More);

/// <summary>
/// One attribute: its values in words, its type from the schema, and whether it has a value (every attribute the object
/// may have is listed). Editable: the service account may write it, it's inside the scope, and it holds text, numbers,
/// dates or names that can be typed; Raw is then its values as stored, which Edit starts from. Single: it holds one value.
/// </summary>
public sealed record AdAttribute(string Name, List<string> Values, bool Hidden, string Type = "", bool Set = true, bool Editable = false, bool Single = false, List<string>? Raw = null);

public sealed record AdObject(string Dn, string Name, string Kind, List<AdAttribute> Attributes);

/// <summary>An attribute's new values; none clears it.</summary>
public sealed record AttributeForm(string? Dn, string? Name, List<string?>? Values);

/// <summary>Where to look while setting up, with the login typed there.</summary>
public sealed record LookBrowseForm(string? Domain, string? Username, string? Password, string? Dn);

/// <summary>An account found in AD while setting up: its username, display name, description, where it is, and whether it's turned on.</summary>
public sealed record AdAccountHit(string Username, string? Name, string? Description, string Where, bool Enabled);

/// <summary>Domain settings, the AD audit, the Recycle Bin and the attribute browser, read as Rabbid Lab's AD account.</summary>
public interface IDirectoryExtras
{
    Task<List<AdSetting>> DomainSettings(CancellationToken stop);

    /// <summary>Changes one domain setting, signed in as the domain admin given (not saved), and gives the settings as they are now.</summary>
    Task<List<AdSetting>> SetDomainSetting(string key, string value, string username, string password, CancellationToken stop);

    /// <summary>What changed in the last days. More when Active Directory had more than it sent, so some changes are missing.</summary>
    Task<(List<AdChange> Changes, bool More)> Changes(int days, CancellationToken stop);

    Task<RecycleState> RecycleBin(CancellationToken stop);

    Task<string> Restore(string dn, CancellationToken stop);

    /// <summary>Turns the Recycle Bin on for the whole forest, signed in as the Enterprise Admin given (credentials not saved). It can't be turned off again.</summary>
    Task EnableRecycleBin(string username, string password, CancellationToken stop);

    Task<AdPlace> Browse(string? dn, CancellationToken stop);

    /// <summary>Browse, signed in as the admin typed while setting up (credentials not saved): Pick an OU before there's a service account.</summary>
    Task<AdPlace> BrowseAs(string? domain, string username, string password, string? dn, CancellationToken stop);

    /// <summary>Accounts in AD to pick as the service account, read as this server's own computer account (no login needed on a domain-joined Windows server).</summary>
    Task<List<AdAccountHit>> LookAccounts(string? domain, string? q, string? place, CancellationToken stop);

    /// <summary>Every OU and container, read as this server, for Browse… while picking the service account.</summary>
    Task<List<AdOu>> LookPlaces(string? domain, CancellationToken stop);

    Task<AdObject> Attributes(string dn, CancellationToken stop);

    /// <summary>Anything in the domain by name, for the Attribute browser: users, groups, computers, OUs and containers.</summary>
    Task<List<AdMember>> FindAnything(string? search, CancellationToken stop);

    /// <summary>Writes an attribute (clears it with no values), as ADSI Edit would, and gives the object as it is now.</summary>
    Task<AdObject> SetAttribute(string dn, string name, List<string?> values, CancellationToken stop);
}

public sealed partial class DirectoryTools
{
    private const string RecycleFeature = "766ddcd8-acd0-445e-f3b9-a7f9b6744f2a";

    // Attributes whose values are secrets: shown as hidden, never sent.
    private static readonly HashSet<string> Secret = new(StringComparer.OrdinalIgnoreCase)
    {
        "unicodePwd", "dBCSPwd", "lmPwdHistory", "ntPwdHistory", "supplementalCredentials", "userPassword", "unixUserPassword", "ms-Mcs-AdmPwd", "msLAPS-Password",
        "msLAPS-EncryptedPassword", "msLAPS-EncryptedPasswordHistory", "msLAPS-EncryptedDSRMPassword", "msLAPS-EncryptedDSRMPasswordHistory", "msFVE-RecoveryPassword",
        "msFVE-KeyPackage", "msTPM-OwnerInformation", "msTPM-TpmInformationForComputer", "msDS-ManagedPassword", "trustAuthIncoming", "trustAuthOutgoing", "currentValue", "priorValue",
    };

    private static readonly HashSet<string> FileTimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "pwdLastSet", "lastLogon", "lastLogonTimestamp", "badPasswordTime", "accountExpires", "lockoutTime", "lastLogoff", "msDS-LastSuccessfulInteractiveLogonTime",
        "msDS-LastFailedInteractiveLogonTime", "ms-Mcs-AdmPwdExpirationTime", "msLAPS-PasswordExpirationTime", "creationTime", "msDS-UserPasswordExpiryTimeComputed",
    };

    // Changes nobody made on purpose: sign-ins and replication housekeeping. The audit leaves objects out when these are all that changed.
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "lastLogonTimestamp", "dSCorePropagationData", "msDS-LastSuccessfulInteractiveLogonTime", "msDS-LastFailedInteractiveLogonTime", "msDS-FailedInteractiveLogonCount",
        "msDS-FailedInteractiveLogonCountAtLastSuccessfulLogon", "logonCount", "badPwdCount", "badPasswordTime", "lastLogon", "msDS-KeyCredentialLink", "msDS-SupportedEncryptionTypes",
    };

    private static readonly HashSet<string> Passwordy = new(StringComparer.OrdinalIgnoreCase)
    {
        "unicodePwd", "dBCSPwd", "lmPwdHistory", "ntPwdHistory", "supplementalCredentials", "pwdLastSet",
    };

    // --- Domain settings ---------------------------------------------------------------------------------------------

    public async Task<List<AdSetting>> DomainSettings(CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var account = Ready().Account;
        var list = new List<AdSetting>();
        var rootDse = (await ldap.Find("", "(objectClass=*)", false, ["configurationNamingContext", "schemaNamingContext", "domainFunctionality", "forestFunctionality", "rootDomainNamingContext"], 1, stop)).FirstOrDefault();
        var config = rootDse?.Text("configurationNamingContext");
        var head = (await ldap.Find(ldap.BaseDn, "(objectClass=*)", false, ["minPwdLength", "pwdHistoryLength", "maxPwdAge", "minPwdAge", "pwdProperties", "lockoutThreshold",
            "lockoutDuration", "lockOutObservationWindow", "ms-DS-MachineAccountQuota", "objectSid"], 1, stop)).FirstOrDefault();
        // Each with what it takes to change it (DomainEdits.cs): its value as a number for the ones changed here.
        void Add(string key, string area, string name, string now, string recommended, string tone, string about, long? value = null) =>
            list.Add(Editable(new AdSetting(key, area, name, now, recommended, tone, about), value));

        if (head is null)
        {
            Add("policy", "Passwords", "Password policy", "Couldn't be read", "", "unknown", $"{Product.Title}'s account couldn't read the domain's own object.");
            return list;
        }
        var minLength = (int) (head.Number("minPwdLength") ?? 0);
        Add("length", "Passwords", "Minimum password length", $"{minLength} characters", "14 or more", minLength >= 14 ? "good" : minLength >= 8 ? "warn" : "bad",
            "Longer passwords are the best protection against guessing. Set in the Default Domain Policy, Account Policies, Password Policy.", minLength);
        var properties = head.Number("pwdProperties") ?? 0;
        Add("complex", "Passwords", "Passwords must meet complexity rules", (properties & 1) != 0 ? "On" : "Off", "On", (properties & 1) != 0 ? "good" : "bad",
            "Needs three of: capitals, small letters, numbers and symbols.", properties & 1);
        Add("reversible", "Passwords", "Store passwords with reversible encryption", (properties & 16) != 0 ? "On" : "Off", "Off", (properties & 16) != 0 ? "bad" : "good",
            "On means passwords can be read back in plain text. Only very old apps need it.", (properties & 16) != 0 ? 1 : 0);
        var history = (int) (head.Number("pwdHistoryLength") ?? 0);
        Add("history", "Passwords", "Passwords remembered", history.ToString(CultureInfo.InvariantCulture), "24", history >= 24 ? "good" : history >= 12 ? "warn" : "bad",
            "Stops people going straight back to an old password.", history);
        var maxAge = Days(head.Number("maxPwdAge"));
        Add("maxage", "Passwords", "Maximum password age", maxAge is null ? "Never expires" : $"{maxAge} days", "365 days, or never with long passwords", maxAge is null or >= 60 ? "good" : "warn",
            "Forcing changes too often makes people pick weaker passwords.", maxAge);
        var minAge = Days(head.Number("minPwdAge")) ?? 0;
        Add("minage", "Passwords", "Minimum password age", $"{minAge} days", "1 day", minAge >= 1 ? "good" : "warn",
            "Without it, someone can change their password over and over to get back to the old one.", minAge);
        var threshold = (int) (head.Number("lockoutThreshold") ?? 0);
        Add("lockout", "Lockout", "Lock out after", threshold == 0 ? "Never locks out" : $"{threshold} wrong passwords", "5 to 10 wrong passwords",
            threshold == 0 ? "bad" : threshold <= 10 ? "good" : "warn", "Without a lockout, anyone can keep guessing passwords.", threshold);
        var duration = Minutes(head.Number("lockoutDuration"));
        Add("duration", "Lockout", "Locked out for", threshold == 0 ? "Doesn't apply" : duration is null ? "Until an admin unlocks" : $"{duration} minutes", "15 minutes or more",
            threshold == 0 || duration is null or >= 15 ? "good" : "warn", "How long a locked-out account stays locked.", duration);
        var quota = (int) (head.Number("ms-DS-MachineAccountQuota") ?? 10);
        Add("quota", "Domain", "Computers anyone can join to the domain", quota.ToString(CultureInfo.InvariantCulture), "0", quota == 0 ? "good" : "warn",
            "By default every user can join 10 computers. Set ms-DS-MachineAccountQuota to 0 so only admins can.", quota);
        var levels = new Dictionary<long, string> { [2] = "2003", [3] = "2008", [4] = "2008 R2", [5] = "2012", [6] = "2012 R2", [7] = "2016", [10] = "2025" };
        var domainLevel = rootDse?.Number("domainFunctionality");
        var forestLevel = rootDse?.Number("forestFunctionality");
        Add("dfl", "Domain", "Domain functional level", domainLevel is { } d ? $"Windows Server {levels.GetValueOrDefault(d, d.ToString(CultureInfo.InvariantCulture))}" : "Unknown",
            "Windows Server 2016 or later", domainLevel >= 7 ? "good" : domainLevel is null ? "unknown" : "warn", "Newer levels turn on newer security features.");
        Add("ffl", "Domain", "Forest functional level", forestLevel is { } f ? $"Windows Server {levels.GetValueOrDefault(f, f.ToString(CultureInfo.InvariantCulture))}" : "Unknown",
            "Windows Server 2016 or later", forestLevel >= 7 ? "good" : forestLevel is null ? "unknown" : "warn", "The Recycle Bin needs 2008 R2 or later.");

        // Accounts: Guest, and how long since krbtgt's password changed.
        if (head.Bytes("objectSid") is { } sid)
        {
            var domainSid = Sid(sid);
            var guest = (await ldap.Find(ldap.BaseDn, $"(objectSid={domainSid}-501)", true, ["userAccountControl"], 1, stop)).FirstOrDefault();
            var guestOn = guest is not null && ((guest.Number("userAccountControl") ?? 2) & Disabled) == 0;
            Add("guest", "Accounts", "Guest account", guest is null ? "Not found" : guestOn ? "On" : "Off", "Off", guestOn ? "bad" : "good", "Guest lets anyone in without a password.", guestOn ? 1 : 0);
            var krbtgt = (await ldap.Find(ldap.BaseDn, $"(objectSid={domainSid}-502)", true, ["pwdLastSet"], 1, stop)).FirstOrDefault();
            var set = FileTime(krbtgt?.Number("pwdLastSet"));
            var age = set is { } at ? (int) (DateTime.UtcNow - at).TotalDays : (int?) null;
            Add("krbtgt", "Accounts", "krbtgt password last changed", set is { } s ? $"{age} days ago ({s:yyyy-MM-dd})" : "Couldn't be read", "Within 180 days",
                age is null ? "unknown" : age <= 180 ? "good" : "warn", "krbtgt signs every Kerberos ticket. Change it twice, a day apart, to retire old tickets.");
            // Members, and anyone who has Domain Admins as primary group, which the member attribute leaves out.
            var admins = (await ldap.Find(ldap.BaseDn, $"(objectSid={domainSid}-512)", true, ["member"], 1, stop)).FirstOrDefault()?.Many("member").Count;
            admins += admins is null ? 0 : (await ldap.Find(ldap.BaseDn, "(primaryGroupID=512)", true, ["cn"], Limit, stop)).Count;
            Add("admins", "Accounts", "People in Domain Admins", admins?.ToString(CultureInfo.InvariantCulture) ?? "Couldn't be read", "5 or fewer", admins is null ? "unknown" : admins <= 5 ? "good" : "warn",
                "Every Domain Admin can do anything to the domain. Keep the list short.");
        }

        // Fine-grained password policies, which only some accounts can read.
        try
        {
            var psos = await ldap.FindChildren($"CN=Password Settings Container,CN=System,{ldap.BaseDn}", ["name"], 100, stop);
            Add("pso", "Passwords", "Fine-grained password policies", psos.Count == 0 ? "None" : string.Join(", ", psos.Select(p => p.Text("name") ?? Ldap.Name(p.Dn))), "As you need them", "good",
                "Stricter password rules for some groups, such as admins.");
        }
        catch (LdapRefused)
        {
            Add("pso", "Passwords", "Fine-grained password policies", "Couldn't be read", "As you need them", "unknown", $"{account} isn't allowed to read them, which is normal.");
        }

        if (config is not null)
        {
            var (on, tombstone, _) = await RecycleSettings(ldap, config, stop);
            Add("recycle", "Domain", "AD Recycle Bin", on ? "On" : "Off", "On", on ? "good" : "warn", "Lets a deleted user, group or computer come back as it was. Turn it on under Recycle Bin.");
            Add("tombstone", "Domain", "Tombstone lifetime", $"{tombstone} days", "180 days", tombstone >= 180 ? "good" : "warn", "How long Active Directory remembers deleted objects. Backups older than this can't be restored.", tombstone);
            var controllers = await ldap.Find(ldap.BaseDn, "(&(objectCategory=computer)(userAccountControl:1.2.840.113556.1.4.803:=8192))", true, ["cn", "operatingSystem"], 50, stop);
            var oldest = controllers.Select(c => c.Text("operatingSystem") ?? "").OrderBy(o => o, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? "";
            Add("dcs", "Domain controllers", "Domain controllers", $"{controllers.Count}: {string.Join(", ", controllers.Select(c => c.Text("cn")))}", "2 or more", controllers.Count >= 2 ? "good" : "warn",
                "With one domain controller, nobody can sign in while it's down.");
            var old = controllers.Any(c => Regex(c.Text("operatingSystem")));
            Add("dcos", "Domain controllers", "Oldest domain controller Windows", oldest.Length > 0 ? oldest : "Unknown", "Windows Server 2016 or later", old ? "warn" : "good",
                "Older Windows Server versions are out of support.");
            var schema = rootDse?.Text("schemaNamingContext");
            if (schema is not null)
            {
                var laps = await ldap.Find(schema, "(|(lDAPDisplayName=msLAPS-Password)(lDAPDisplayName=ms-Mcs-AdmPwd))", true, ["lDAPDisplayName"], 5, stop);
                var names = laps.Select(l => l.Text("lDAPDisplayName")).ToList();
                var lapsNow = names.Contains("msLAPS-Password") ? "Windows LAPS" : names.Contains("ms-Mcs-AdmPwd") ? "Legacy LAPS" : "Not set up";
                Add("laps", "Domain", "LAPS (local admin passwords)", lapsNow, "Windows LAPS", lapsNow == "Windows LAPS" ? "good" : lapsNow == "Legacy LAPS" ? "warn" : "bad",
                    "Gives every computer its own local admin password and changes it, so one leaked password doesn't open them all.");
            }
        }
        await HealthChecks(ldap, rootDse, config, head.Bytes("objectSid") is { } sidBytes ? Sid(sidBytes) : null, list, stop);
        return list;
    }

    /// <summary>Whether the forest's Recycle Bin is on, the tombstone lifetime, and how long deleted objects can come back (both in days).</summary>
    private static async Task<(bool On, int Tombstone, int Lifetime)> RecycleSettings(ILdap ldap, string config, CancellationToken stop)
    {
        var service = (await ldap.Find($"CN=Directory Service,CN=Windows NT,CN=Services,{config}", "(objectClass=*)", false, ["tombstoneLifetime", "msDS-DeletedObjectLifetime"], 1, stop)).FirstOrDefault();
        var feature = (await ldap.Find($"CN=Recycle Bin Feature,CN=Optional Features,CN=Directory Service,CN=Windows NT,CN=Services,{config}", "(objectClass=*)", false,
            ["msDS-EnabledFeatureBL"], 1, stop)).FirstOrDefault();
        var tombstone = (int) (service?.Number("tombstoneLifetime") ?? 180);
        return (feature?.Texts("msDS-EnabledFeatureBL").Any() == true, tombstone, (int) (service?.Number("msDS-DeletedObjectLifetime") ?? tombstone));
    }

    private static bool Regex(string? os) => os is not null && System.Text.RegularExpressions.Regex.IsMatch(os, @"200[038]|2012");

    /// <summary>Days from an interval Active Directory keeps as minus hundreds of nanoseconds; null for never.</summary>
    private static int? Days(long? interval) => interval is null or long.MinValue or 0 ? null : (int) (Math.Abs(interval.Value) / 864_000_000_000);

    private static int? Minutes(long? interval) => interval is null or long.MinValue ? null : (int) (Math.Abs(interval.Value) / 600_000_000);

    /// <summary>A SID as text, such as S-1-5-21-…</summary>
    public static string Sid(byte[] b)
    {
        var authority = 0L;
        for (var i = 2; i < 8; i++)
        {
            authority = (authority << 8) | b[i];
        }
        var s = new StringBuilder($"S-{b[0]}-{authority}");
        for (var i = 0; i < b[1]; i++)
        {
            s.Append('-').Append(BitConverter.ToUInt32(b, 8 + (i * 4)));
        }
        return s.ToString();
    }

    // --- The AD audit: what changed lately ---------------------------------------------------------------------------

    public async Task<(List<AdChange> Changes, bool More)> Changes(int days, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 90));
        var stamp = since.ToString("yyyyMMddHHmmss.0Z", CultureInfo.InvariantCulture);
        var kinds = "(|(&(objectCategory=person)(objectClass=user))(objectCategory=group)(objectCategory=computer)(objectClass=organizationalUnit))";
        var rows = await ldap.Find(ldap.BaseDn, $"(&(whenChanged>={stamp}){kinds})", true,
            ["objectClass", "cn", "ou", "displayName", "whenChanged", "whenCreated", "msDS-ReplAttributeMetaData"], Limit, stop);
        var more = rows.Count >= Limit;
        var changes = new List<AdChange>();
        foreach (var r in rows)
        {
            if (DnsPartition(r.Dn))
            {
                continue;
            }
            var changed = When(r.Text("whenChanged")) ?? DateTime.UtcNow;
            var made = When(r.Text("whenCreated"));
            var attributes = r.Texts("msDS-ReplAttributeMetaData").Select(Meta).Where(m => m.At >= since).ToList();
            var kind = KindOf(r);
            // A computer changes its own password every 30 days; a person's password change shows as just "password".
            var real = attributes.Where(a => !Noise.Contains(a.Name) && !(kind == "computer" && Passwordy.Contains(a.Name)))
                .Select(a => Passwordy.Contains(a.Name) ? "password" : a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
            // Only sign-ins and housekeeping since then: not a change anyone made. Groups change members in a way this doesn't list.
            if (attributes.Count > 0 && real.Count == 0 && !(made >= since))
            {
                continue;
            }
            if (attributes.Count == 0 && kind == "group" && !(made >= since))
            {
                real.Add("member");
            }
            // Brought back from the Recycle Bin: isDeleted went away. Something new: every attribute is new, so none are listed.
            var what = made >= since ? "made" : real.Contains("isDeleted", StringComparer.OrdinalIgnoreCase) ? "restored" : "changed";
            if (what != "changed")
            {
                real.Clear();
            }
            var name = kind == "user" ? r.Text("displayName") ?? r.Text("cn") : r.Text("cn") ?? r.Text("ou");
            changes.Add(new AdChange(r.Dn, name ?? Ldap.Name(r.Dn), kind, Ldap.Path(Ldap.Parent(r.Dn), ldap.BaseDn), changed, what, real));
        }
        try
        {
            var gone = await ldap.FindDeleted(ldap.BaseDn, $"(&(isDeleted=TRUE)(whenChanged>={stamp})(|(objectClass=user)(objectClass=group)(objectClass=organizationalUnit)))", true,
                ["objectClass", "msDS-LastKnownRDN", "lastKnownParent", "whenChanged"], 500, stop);
            more |= gone.Count >= 500;
            changes.AddRange(gone.Where(g => !g.Dn.StartsWith("CN=Deleted Objects,", StringComparison.OrdinalIgnoreCase) && !DnsPartition(g.Dn)).Select(g => new AdChange(g.Dn, g.Text("msDS-LastKnownRDN") ?? Ldap.Name(g.Dn),
                KindOf(g), g.Text("lastKnownParent") is { } p ? Ldap.Path(p, ldap.BaseDn) : "", When(g.Text("whenChanged")) ?? DateTime.UtcNow, "deleted", [])));
        }
        catch (LdapRefused)
        {
            // Deleted objects are for Domain Admins by default: the audit shows what it can.
        }
        return ([.. changes.OrderByDescending(c => c.At)], more);
    }

    /// <summary>The DNS partitions (DomainDnsZones, ForestDnsZones), which the domain's own searches can wander into.</summary>
    private static bool DnsPartition(string dn) =>
        dn.Contains(",DC=DomainDnsZones,", StringComparison.OrdinalIgnoreCase) || dn.Contains(",DC=ForestDnsZones,", StringComparison.OrdinalIgnoreCase);

    /// <summary>The attribute and when it last changed, from one msDS-ReplAttributeMetaData value. Read by hand: there are thousands, and parsing each as XML is slow.</summary>
    private static (string Name, DateTime At) Meta(string xml)
    {
        static string? Tag(string xml, string tag)
        {
            var from = xml.IndexOf($"<{tag}>", StringComparison.Ordinal);
            var to = from < 0 ? -1 : xml.IndexOf("</", from, StringComparison.Ordinal);
            return to < 0 ? null : xml[(from + tag.Length + 2)..to];
        }
        return (Tag(xml, "pszAttributeName") ?? "", DateTime.TryParse(Tag(xml, "ftimeLastOriginatingChange"), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : DateTime.MinValue);
    }

    private static DateTime? When(string? generalized) =>
        DateTime.TryParseExact(generalized, "yyyyMMddHHmmss.0Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    private static string KindOf(LdapRow r) => KindOf(r.Texts("objectClass").ToList());

    private static string KindOf(List<string> classes)
    {
        bool Is(string c) => classes.Contains(c, StringComparer.OrdinalIgnoreCase);
        return Is("computer") ? "computer" : Is("group") ? "group" : Is("user") ? "user" : Is("organizationalUnit") ? "ou"
            : Is("container") || Is("builtinDomain") || Is("lostAndFound") || Is("domainDNS") ? "container" : "other";
    }

    // --- The Recycle Bin ---------------------------------------------------------------------------------------------

    public async Task<RecycleState> RecycleBin(CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var cantLook = $"{Ready().Account} can't look in Deleted Objects. Only Domain Admins can by default.";
        var config = (await ldap.Find("", "(objectClass=*)", false, ["configurationNamingContext"], 1, stop)).FirstOrDefault()?.Text("configurationNamingContext") ?? "";
        var (on, _, lifetime) = await RecycleSettings(ldap, config, stop);
        var items = new List<DeletedObject>();
        var more = false;
        string? problem = null;
        try
        {
            var rows = await ldap.FindDeleted($"CN=Deleted Objects,{ldap.BaseDn}", "(&(isDeleted=TRUE)(!(isRecycled=TRUE))(|(objectClass=user)(objectClass=group)(objectClass=organizationalUnit)))", true,
                ["objectClass", "msDS-LastKnownRDN", "lastKnownParent", "whenChanged", "sAMAccountName"], Limit, stop);
            more = rows.Count >= Limit;
            items = [.. rows.Where(r => !r.Dn.StartsWith("CN=Deleted Objects,", StringComparison.OrdinalIgnoreCase)).Select(r => new DeletedObject(r.Dn,
                r.Text("msDS-LastKnownRDN") ?? Ldap.Name(r.Dn).Split('\n')[0], KindOf(r), r.Text("lastKnownParent") is { } p ? Ldap.Path(p, ldap.BaseDn) : null,
                When(r.Text("whenChanged")), r.Text("sAMAccountName"))).OrderByDescending(d => d.Deleted)];
            // Seeing nothing at all, not even the container, means it may not look.
            if (rows.Count == 0 && (await ldap.FindDeleted($"CN=Deleted Objects,{ldap.BaseDn}", "(objectClass=*)", false, ["cn"], 1, stop)).Count == 0)
            {
                problem = cantLook;
            }
        }
        catch (LdapRefused e)
        {
            problem = e.Code == LdapRefused.InsufficientRights ? cantLook : $"Active Directory said: {e.Message}.";
        }
        return new RecycleState(on, lifetime, items, problem, more);
    }

    public async Task<string> Restore(string dn, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var bin = $"CN=Deleted Objects,{ldap.BaseDn}";
        var at = dn?.Trim() ?? "";
        // Asked for by its own DN, so a full Recycle Bin can't hide it. The bin itself isn't something to restore.
        var row = (Ldap.Under(at, bin) && !Ldap.Under(bin, at)
                ? (await ldap.FindDeleted(at, "(isDeleted=TRUE)", false, ["objectClass", "msDS-LastKnownRDN", "lastKnownParent"], 1, stop)).FirstOrDefault() : null)
            ?? throw new ToolProblem("That isn't in the Recycle Bin any more.", StatusCodes.Status404NotFound);
        var name = row.Text("msDS-LastKnownRDN") ?? throw new ToolProblem("Active Directory didn't say what it was called.");
        var parent = row.Text("lastKnownParent") ?? throw new ToolProblem("Active Directory didn't say where it was.");
        if (parent.Contains("\\0ADEL:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolProblem($"Where it was ({Ldap.Name(parent)}) was deleted too. Restore that first.");
        }
        var newDn = $"{(KindOf(row) == "ou" ? "OU" : "CN")}={Ldap.Escape(name)},{parent}";
        InScope(newDn, f, ldap.BaseDn);
        try
        {
            await ldap.Restore(row.Dn, newDn, stop);
        }
        catch (LdapRefused e)
        {
            throw new ToolProblem(e.Code switch
            {
                LdapRefused.InsufficientRights => $"{f.Account} isn't allowed to restore deleted objects. That takes the Reanimate Tombstones right, which Domain Admins have.",
                LdapRefused.EntryExists => $"There's already something called {name} in {Ldap.Path(parent, ldap.BaseDn)}. Rename or move it first.",
                LdapRefused.NoSuchObject => $"{Ldap.Path(parent, ldap.BaseDn)} isn't there any more. Restore it first.",
                _ => $"Active Directory said: {e.Message}.",
            });
        }
        return newDn;
    }

    public async Task EnableRecycleBin(string username, string password, CancellationToken stop)
    {
        var f = Ready();
        using var ldap = await open(f.Domain!, await Host(f.Domain!, stop), username, password, stop) ?? throw new ToolProblem("That username or password is wrong.");
        var config = (await ldap.Find("", "(objectClass=*)", false, ["configurationNamingContext"], 1, stop)).FirstOrDefault()?.Text("configurationNamingContext")
            ?? throw new ToolProblem("Active Directory didn't say where its configuration is.");
        try
        {
            await ldap.Modify("", [new(LdapChange.Add, "enableOptionalFeature", $"CN=Partitions,{config}:{RecycleFeature}")], stop);
        }
        catch (LdapRefused e)
        {
            throw new ToolProblem(e.Code == LdapRefused.InsufficientRights ? $"{Ldap.Account(username)} isn't allowed to. It takes an Enterprise Admin." : e.Code == LdapRefused.AttributeExists
                ? "It's already on." : $"Active Directory said: {e.Message}. The forest has to be at Windows Server 2008 R2 or later.");
        }
    }

    // --- The attribute browser ---------------------------------------------------------------------------------------

    public async Task<List<AdMember>> FindAnything(string? search, CancellationToken stop)
    {
        var match = Match(search, "cn", "ou", "sAMAccountName", "displayName", "name");
        if (match.Length == 0)
        {
            return [];
        }
        using var ldap = await Open(stop);
        var rows = await ldap.Find(ldap.BaseDn, $"(&(|(objectClass=user)(objectClass=group)(objectClass=organizationalUnit)(objectClass=container)){match})", true,
            ["objectClass", "cn", "ou", "name", "sAMAccountName", "displayName"], 60, stop);
        return [.. rows.Select(r =>
        {
            var kind = KindOf(r);
            var sam = r.Text("sAMAccountName");
            var name = (kind == "user" ? r.Text("displayName") : null) ?? r.Text("cn") ?? r.Text("ou") ?? r.Text("name") ?? Ldap.Name(r.Dn);
            return new AdMember(r.Dn, name, kind, kind == "computer" ? sam?.TrimEnd('$') : sam, Ldap.Path(Ldap.Parent(r.Dn), ldap.BaseDn));
        }).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<AdPlace> Browse(string? dn, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        return await PlaceOf(ldap, dn, Ready().Domain!, stop);
    }

    public async Task<AdPlace> BrowseAs(string? domain, string username, string password, string? dn, CancellationToken stop)
    {
        var d = Clean(domain) ?? Domain ?? throw new ToolProblem("Which domain? Type its name, such as amw.com.");
        using var ldap = await open(d, await Host(d, stop), username, password, stop) ?? throw new ToolProblem("That username or password is wrong.");
        return await PlaceOf(ldap, dn, d, stop);
    }

    // Use one I have: the accounts to pick from, read as this server's own computer account. Typed: any account that
    // matches, across the domain. A place (Browse…): the accounts directly in that OU or container. Neither: the ones
    // that look like service accounts. Never krbtgt, Guest and the like, nor the mailboxes Exchange makes for itself.
    public async Task<List<AdAccountHit>> LookAccounts(string? domain, string? q, string? place, CancellationToken stop)
    {
        var d = Clean(domain) ?? Domain ?? throw new ToolProblem("Which domain? Type its name, such as amw.com.");
        var cant = $"This server can't read {d} on its own. Type the account's name instead.";
        using var ldap = await Ldap.OpenAnonymous(d, await Host(d, stop), stop) ?? throw new ToolProblem(cant);
        string[] wanted = ["objectClass", "sAMAccountName", "displayName", "description", "userAccountControl", "isCriticalSystemObject"];
        try
        {
            List<LdapRow> rows;
            if (!string.IsNullOrWhiteSpace(place) && string.IsNullOrWhiteSpace(q))
            {
                rows = [.. (await ldap.FindChildren(Inside(place, ldap.BaseDn), wanted, 1000, stop))
                    .Where(r => r.Texts("objectClass").Contains("user", StringComparer.OrdinalIgnoreCase) && !r.Texts("objectClass").Contains("computer", StringComparer.OrdinalIgnoreCase))];
            }
            else
            {
                var text = q?.Trim() ?? "";
                var match = text.Length == 0 ? "(|(sAMAccountName=svc*)(description=*service*))" : Match(text, "sAMAccountName", "displayName", "cn");
                rows = await ldap.Find(ldap.BaseDn, $"(&(objectCategory=person)(objectClass=user)(!(isCriticalSystemObject=TRUE))(!(sAMAccountName=$*))(!(sAMAccountName=SM_*))(!(sAMAccountName=HealthMailbox*)){match})", true, wanted, 50, stop);
            }
            return [.. rows.Where(r => Pickable(r))
                .Select(r => new AdAccountHit(r.Text("sAMAccountName")!, r.Text("displayName"), r.Text("description"), Ldap.Path(Ldap.Parent(r.Dn), ldap.BaseDn),
                    ((r.Number("userAccountControl") ?? 0) & 2) == 0))
                .OrderBy(h => h.Username, StringComparer.OrdinalIgnoreCase)];
        }
        catch (LdapRefused)
        {
            throw new ToolProblem(cant);
        }
    }

    private static bool Pickable(LdapRow r) => r.Text("sAMAccountName") is { Length: > 0 } sam && r.Text("isCriticalSystemObject") != "TRUE"
        && !sam.StartsWith('$') && !sam.StartsWith("SM_", StringComparison.OrdinalIgnoreCase) && !sam.StartsWith("HealthMailbox", StringComparison.OrdinalIgnoreCase);

    // Browse… for the service account: every OU and container, as Pick an OU lists them, read as this server.
    public async Task<List<AdOu>> LookPlaces(string? domain, CancellationToken stop)
    {
        var d = Clean(domain) ?? Domain ?? throw new ToolProblem("Which domain? Type its name, such as amw.com.");
        var cant = $"This server can't read {d} on its own. Type the account's name instead.";
        using var ldap = await Ldap.OpenAnonymous(d, await Host(d, stop), stop) ?? throw new ToolProblem(cant);
        try
        {
            return await PlacesIn(ldap, ldap.BaseDn, stop);
        }
        catch (LdapRefused)
        {
            throw new ToolProblem(cant);
        }
    }

    private static async Task<AdPlace> PlaceOf(ILdap ldap, string? dn, string domain, CancellationToken stop)
    {
        var at = string.IsNullOrWhiteSpace(dn) ? ldap.BaseDn : Inside(dn, ldap.BaseDn);
        var top = string.Equals(at, ldap.BaseDn, StringComparison.OrdinalIgnoreCase);
        var rows = await ldap.FindChildren(at, ["objectClass", "name", "description", "displayName"], Limit, stop);
        var items = rows.Select(r =>
        {
            var kind = KindOf(r);
            return new AdPlaceItem(r.Dn, r.Text("displayName") ?? r.Text("name") ?? Ldap.Name(r.Dn), kind, kind is "ou" or "container" or "other", r.Text("description"));
        }).OrderBy(i => i.Kind is "ou" or "container" ? 0 : i.Kind == "other" ? 1 : 2).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new AdPlace(at, top ? domain : Ldap.Name(at), Ldap.Path(at, ldap.BaseDn), top ? null : Ldap.Parent(at), items, rows.Count >= Limit);
    }

    public async Task<AdObject> Attributes(string dn, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        return await AttributesOf(ldap, Inside(dn, ldap.BaseDn), stop);
    }

    // Every attribute the object may have (allowedAttributes), set or not, each with its type; the ones the service
    // account may write (allowedAttributesEffective) and that can be typed are editable, inside the scope only.
    private async Task<AdObject> AttributesOf(ILdap ldap, string at, CancellationToken stop)
    {
        var all = await ldap.ReadAll(at, stop) ?? throw new ToolProblem("That isn't in Active Directory any more.", StatusCodes.Status404NotFound);
        var set = new Dictionary<string, byte[][]>(all, StringComparer.OrdinalIgnoreCase);
        var kind = KindOf(set.TryGetValue("objectClass", out var c) ? c.Select(v => Encoding.UTF8.GetString(v)).ToList() : []);
        var allowed = (await ldap.Find(at, "(objectClass=*)", false, ["allowedAttributes", "allowedAttributesEffective"], 1, stop)).FirstOrDefault();
        var may = allowed?.Texts("allowedAttributes").ToList() ?? [];
        var writable = new HashSet<string>(allowed?.Texts("allowedAttributesEffective") ?? [], StringComparer.OrdinalIgnoreCase);
        await Describe(ldap, set.Keys.Concat(may), stop);
        var inScope = Ldap.Under(at, Ready().Scope!);
        bool CanEdit(string name) => inScope && writable.Contains(name) && SchemaOf.TryGetValue(name, out var s) && Editable(name, s.Syntax);
        var rows = set.Select(a =>
        {
            var s = SchemaOf.GetValueOrDefault(a.Key);
            if (Secret.Contains(a.Key))
            {
                return new AdAttribute(a.Key, ["(hidden)"], true, TypeOf(s.Syntax));
            }
            var edit = CanEdit(a.Key);
            return new AdAttribute(a.Key, [.. a.Value.Select(v => Show(a.Key, v))], false, TypeOf(s.Syntax), true, edit, s.Single,
                edit ? [.. a.Value.Select(v => Encoding.UTF8.GetString(v))] : null);
        }).ToList();
        foreach (var name in may.Where(m => !set.ContainsKey(m)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var s = SchemaOf.GetValueOrDefault(name);
            rows.Add(new AdAttribute(name, [], Secret.Contains(name), TypeOf(s.Syntax), false, !Secret.Contains(name) && CanEdit(name), s.Single));
        }
        return new AdObject(at, Ldap.Name(at), kind == "container" ? "other" : kind, [.. rows.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)]);
    }

    public async Task<AdObject> SetAttribute(string dn, string name, List<string?> values, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var at = Inside(dn, ldap.BaseDn);
        InScope(at, f, ldap.BaseDn);
        if (!AttributeName().IsMatch(name))
        {
            throw new ToolProblem("That isn't an attribute name.");
        }
        await Describe(ldap, [name], stop);
        if (!SchemaOf.TryGetValue(name, out var schema))
        {
            throw new ToolProblem($"The schema has no attribute called {name}.");
        }
        if (!Editable(name, schema.Syntax))
        {
            throw new ToolProblem($"{name} can't be edited here: it's binary, a secret, or what makes the object what it is.");
        }
        var now = (await ldap.Find(at, "(objectClass=*)", false, ["allowedAttributesEffective", name], 1, stop)).FirstOrDefault()
            ?? throw new ToolProblem("That isn't in Active Directory any more.", StatusCodes.Status404NotFound);
        if (!now.Texts("allowedAttributesEffective").Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            throw new ToolProblem($"{f.Account} isn't allowed to change {name} on {Ldap.Name(at)}.");
        }
        var clean = Clean(name, schema, values, out var why) ?? throw new ToolProblem(why!);
        // Clearing what's already empty changes nothing; Active Directory would call it an error.
        if (clean.Count > 0 || now.Texts(name).Any())
        {
            try
            {
                await ldap.Modify(at, [clean.Count == 0 ? new LdapMod(LdapChange.Delete, name) : new LdapMod(LdapChange.Replace, name, [.. clean])], stop);
            }
            catch (LdapRefused e)
            {
                throw new ToolProblem(e.Code == LdapRefused.InsufficientRights ? $"{f.Account} isn't allowed to change {name} on {Ldap.Name(at)}."
                    : $"Active Directory refused it: {e.Message}.");
            }
        }
        return await AttributesOf(ldap, at, stop);
    }

    // --- The schema, for the attribute browser: each attribute's syntax and whether it holds one value -------------------

    // Read as they're needed, in batches, and kept: the schema seldom changes and is the same across the forest.
    private static readonly ConcurrentDictionary<string, (string Syntax, bool Single)> SchemaOf = new(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]{0,127}$")]
    private static partial Regex AttributeName();

    private static async Task Describe(ILdap ldap, IEnumerable<string> names, CancellationToken stop)
    {
        var wanted = names.Where(n => AttributeName().IsMatch(n) && !SchemaOf.ContainsKey(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0)
        {
            return;
        }
        var schemaDn = (await ldap.Find("", "(objectClass=*)", false, ["schemaNamingContext"], 1, stop)).FirstOrDefault()?.Text("schemaNamingContext");
        if (schemaDn is null)
        {
            return;
        }
        foreach (var batch in wanted.Chunk(80))
        {
            var filter = $"(&(objectClass=attributeSchema)(|{string.Concat(batch.Select(n => $"(lDAPDisplayName={n})"))}))";
            foreach (var r in await ldap.Find(schemaDn, filter, true, ["lDAPDisplayName", "attributeSyntax", "isSingleValued"], 1000, stop))
            {
                if (r.Text("lDAPDisplayName") is { } n && r.Text("attributeSyntax") is { } syntax)
                {
                    SchemaOf[n] = (syntax, string.Equals(r.Text("isSingleValued"), "TRUE", StringComparison.OrdinalIgnoreCase));
                }
            }
        }
    }

    /// <summary>The kind of value a schema syntax (2.5.5.x) holds, in words.</summary>
    private static string TypeOf(string? syntax) => syntax switch
    {
        "2.5.5.1" => "Distinguished name",
        "2.5.5.2" => "Object identifier",
        "2.5.5.3" or "2.5.5.4" or "2.5.5.5" or "2.5.5.12" => "String",
        "2.5.5.6" => "Numeric string",
        "2.5.5.7" or "2.5.5.14" => "Distinguished name with data",
        "2.5.5.8" => "Boolean",
        "2.5.5.9" => "Integer",
        "2.5.5.10" => "Octet string",
        "2.5.5.11" => "Date and time",
        "2.5.5.13" => "Presentation address",
        "2.5.5.15" => "Security descriptor",
        "2.5.5.16" => "Large integer",
        "2.5.5.17" => "SID",
        _ => "",
    };

    // Syntaxes whose values can be typed: names, object identifiers, text, numbers, true or false, and times.
    private static readonly HashSet<string> Typed = ["2.5.5.1", "2.5.5.2", "2.5.5.3", "2.5.5.4", "2.5.5.5", "2.5.5.6", "2.5.5.8", "2.5.5.9", "2.5.5.11", "2.5.5.12", "2.5.5.16"];

    // What makes an object what it is: changed only by making, moving or renaming it.
    private static readonly HashSet<string> Identity = new(StringComparer.OrdinalIgnoreCase) { "objectClass", "objectCategory", "distinguishedName", "name", "cn", "ou" };

    private static bool Editable(string name, string? syntax) => syntax is not null && Typed.Contains(syntax) && !Identity.Contains(name) && !Secret.Contains(name);

    /// <summary>Values typed for an attribute, as they're written: blanks left out, TRUE and FALSE, numbers checked. Null, with why, when they can't be.</summary>
    private static List<string>? Clean(string name, (string Syntax, bool Single) schema, IEnumerable<string?> typed, out string? why)
    {
        why = null;
        var values = new List<string>();
        foreach (var raw in typed)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }
            var text = schema.Syntax is "2.5.5.3" or "2.5.5.4" or "2.5.5.5" or "2.5.5.12" ? raw : raw.Trim();
            if (text.Length > 100_000)
            {
                why = $"A value of {name} can't be over 100,000 characters.";
                return null;
            }
            switch (schema.Syntax)
            {
                case "2.5.5.8":
                    if (!bool.TryParse(text, out var on))
                    {
                        why = $"{name} is TRUE or FALSE.";
                        return null;
                    }
                    text = on ? "TRUE" : "FALSE";
                    break;
                case "2.5.5.9":
                    if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
                    {
                        why = $"{name} is a whole number from -2147483648 to 2147483647.";
                        return null;
                    }
                    text = n.ToString(CultureInfo.InvariantCulture);
                    break;
                case "2.5.5.16":
                    if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var big))
                    {
                        why = $"{name} is a whole number from -9223372036854775808 to 9223372036854775807.";
                        return null;
                    }
                    text = big.ToString(CultureInfo.InvariantCulture);
                    break;
                case "2.5.5.6":
                    if (text.Any(ch => !char.IsAsciiDigit(ch) && ch != ' '))
                    {
                        why = $"{name} holds digits and spaces only.";
                        return null;
                    }
                    break;
                case "2.5.5.11":
                    if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d{12}(\d{2})?(\.\d{1,3})?Z$"))
                    {
                        why = $"{name} is a time in UTC, written as YYYYMMDDHHMMSS.0Z (for example 20261231170000.0Z).";
                        return null;
                    }
                    break;
            }
            if (text.Any(ch => char.IsControl(ch) && ch is not ('\r' or '\n' or '\t')))
            {
                why = $"A value of {name} has characters that can't be stored.";
                return null;
            }
            values.Add(text);
        }
        if (values.Count > 1000)
        {
            why = "At most 1,000 values can be set at once.";
            return null;
        }
        if (schema.Single && values.Count > 1)
        {
            why = $"{name} holds a single value.";
            return null;
        }
        if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
        {
            why = "The same value is there more than once.";
            return null;
        }
        return values;
    }

    /// <summary>One value in words: SIDs and GUIDs as text, dates as dates, flags by name, and binary as its size.</summary>
    private static string Show(string name, byte[] v)
    {
        if (name.Equals("objectSid", StringComparison.OrdinalIgnoreCase) || name.Equals("sIDHistory", StringComparison.OrdinalIgnoreCase) || name.Equals("securityIdentifier", StringComparison.OrdinalIgnoreCase))
        {
            return v.Length >= 8 ? Sid(v) : Convert.ToHexString(v);
        }
        if (v.Length == 16 && name.Contains("guid", StringComparison.OrdinalIgnoreCase))
        {
            return new Guid(v).ToString();
        }
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(v);
        }
        catch (DecoderFallbackException)
        {
            return $"(binary, {v.Length:N0} bytes)";
        }
        if (text.Any(ch => char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t'))
        {
            return $"(binary, {v.Length:N0} bytes)";
        }
        if (FileTimes.Contains(name) && long.TryParse(text, out var ft))
        {
            return ft is 0 or long.MaxValue ? $"{text} (never)" : ft > 0 && ft < DateTime.MaxValue.ToFileTimeUtc() ? $"{text} ({DateTime.FromFileTimeUtc(ft):yyyy-MM-dd HH:mm} UTC)" : text;
        }
        if (When(text) is { } at)
        {
            return $"{at:yyyy-MM-dd HH:mm:ss} UTC";
        }
        if (name.Equals("userAccountControl", StringComparison.OrdinalIgnoreCase) && int.TryParse(text, out var flags))
        {
            var named = new (int Bit, string Name)[] { (0x2, "disabled"), (0x10, "locked out"), (0x20, "no password needed"), (0x200, "normal account"), (0x1000, "workstation"),
                (0x2000, "domain controller"), (0x10000, "password never expires"), (0x40000, "smart card required"), (0x80000, "trusted for delegation"), (0x100000, "not delegated"),
                (0x400000, "no Kerberos pre-authentication") }.Where(f => (flags & f.Bit) != 0).Select(f => f.Name);
            return $"{text} ({string.Join(", ", named)})";
        }
        return text;
    }
}

public static class DirectoryExtrasApi
{
    public static void MapDirectoryExtrasApi(this WebApplication app)
    {
        app.MapGet("/api/ad/domain", (HttpContext context, IDirectoryExtras tools) => UsersApi.Try(context, Permissions.AdView, async () =>
            Results.Ok(await tools.DomainSettings(context.RequestAborted)), change: false));

        // One domain setting changed from its side panel, signed in as a domain admin for it (not saved).
        app.MapPost("/api/ad/domain/{key}", (string key, DomainSettingForm form, HttpContext context, IDirectoryExtras tools, ActivityLog log) => UsersApi.Try(context, Permissions.AdConnect, async () =>
        {
            var list = await tools.SetDomainSetting(key, form.Value ?? "", form.Username ?? "", form.Password ?? "", context.RequestAborted);
            var s = list.FirstOrDefault(x => x.Key == key);
            log.Record(context, "users", "ad.domain.set", $"Changed the domain setting {s?.Name ?? key} to {s?.Now ?? form.Value}");
            return Results.Ok(list);
        }));

        // What changed lately; who made it when Rabbid Lab made it.
        app.MapGet("/api/ad/audit", (int? days, HttpContext context, IDirectoryExtras tools, ActivityLog log) => UsersApi.Try(context, Permissions.AdView, async () =>
        {
            var (changes, more) = await tools.Changes(days ?? 30, context.RequestAborted);
            // Rabbid Lab's own AD changes, by the minute they happened, so each change only looks at the few around it.
            var ours = log.Recent(1000).Where(e => e.Area == "users" && e.Action.StartsWith("ad.", StringComparison.Ordinal)).ToLookup(e => e.At.Ticks / TimeSpan.TicksPerMinute);
            UsersApi.More(context, more);
            return Results.Ok(changes.Select(c => c with
            {
                By = Enumerable.Range(-3, 7).SelectMany(m => ours[(c.At.Ticks / TimeSpan.TicksPerMinute) + m])
                    .Where(e => Math.Abs((e.At - c.At).TotalMinutes) < 3 && Mentions(e.Title, c.Name)).MinBy(e => Math.Abs((e.At - c.At).Ticks))?.By,
            }));
        }, change: false));

        app.MapGet("/api/ad/recycle", (HttpContext context, IDirectoryExtras tools) => UsersApi.Try(context, Permissions.AdView, async () =>
            Results.Ok(await tools.RecycleBin(context.RequestAborted)), change: false));

        app.MapPost("/api/ad/recycle/restore", (DnForm form, HttpContext context, IDirectoryExtras tools, ActivityLog log) => UsersApi.Try(context, Permissions.AdChange, async () =>
        {
            var back = await tools.Restore(form.Dn ?? "", context.RequestAborted);
            log.Record(context, "users", "ad.restore", $"Restored {Ldap.Name(back)} from the AD Recycle Bin");
            return Results.Ok(new { dn = back });
        }));

        app.MapPost("/api/ad/recycle/enable", (SetupForm form, HttpContext context, IDirectoryExtras tools, ActivityLog log) => UsersApi.Try(context, Permissions.AdConnect, async () =>
        {
            await tools.EnableRecycleBin(form.Username ?? "", form.Password ?? "", context.RequestAborted);
            log.Record(context, "users", "ad.recycle.enable", "Turned on the AD Recycle Bin for the forest");
            return Results.Ok();
        }));

        app.MapGet("/api/ad/browse", (string? dn, HttpContext context, IDirectoryExtras tools) => UsersApi.Try(context, Permissions.AdView, async () =>
            Results.Ok(await tools.Browse(dn, context.RequestAborted)), change: false));

        app.MapGet("/api/ad/look/places", (string? domain, HttpContext context, IDirectoryExtras tools) => UsersApi.Try(context, Permissions.AdConnect, async () =>
            Results.Ok(await tools.LookPlaces(domain, context.RequestAborted)), change: false));

        app.MapGet("/api/ad/look/accounts", (string? domain, string? q, string? place, HttpContext context, IDirectoryExtras tools) => UsersApi.Try(context, Permissions.AdConnect, async () =>
            Results.Ok(await tools.LookAccounts(domain, q, place, context.RequestAborted)), change: false));

        app.MapPost("/api/ad/look/browse", (LookBrowseForm form, HttpContext context, IDirectoryExtras tools) => UsersApi.Try(context, Permissions.AdConnect, async () =>
            Results.Ok(await tools.BrowseAs(form.Domain, form.Username ?? "", form.Password ?? "", form.Dn, context.RequestAborted)), change: false));

        app.MapGet("/api/ad/object", (string dn, HttpContext context, IDirectoryExtras tools) => UsersApi.Try(context, Permissions.AdView, async () =>
            Results.Ok(await tools.Attributes(dn, context.RequestAborted)), change: false));

        // The Attribute browser's find: anything in the domain by name.
        app.MapGet("/api/ad/object/find", (string? q, HttpContext context, IDirectoryExtras tools) => UsersApi.Try(context, Permissions.AdView, async () =>
            Results.Ok(await tools.FindAnything(q, context.RequestAborted)), change: false));

        // Edit… and Clear value in the Attribute browser: one attribute at a time, in the Activity log with what it was set to.
        app.MapPost("/api/ad/object/attribute", (AttributeForm form, HttpContext context, IDirectoryExtras tools, ActivityLog log) => UsersApi.Try(context, Permissions.AdAttributes, async () =>
        {
            var values = form.Values ?? [];
            var o = await tools.SetAttribute(form.Dn ?? "", form.Name ?? "", values, context.RequestAborted);
            var shown = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            log.Record(context, "users", "ad.attribute", shown.Count == 0 ? $"Cleared {form.Name} on {o.Name}" : $"Set {form.Name} on {o.Name}",
                detail: shown.Count == 0 ? null : string.Join("; ", shown.Take(10)) + (shown.Count > 10 ? $" and {shown.Count - 10} more" : ""));
            return Results.Ok(o);
        }));
    }

    /// <summary>Whether an activity title names this object as a whole word, so a group called IT isn't matched by "edited".</summary>
    public static bool Mentions(string title, string name)
    {
        if (name.Length == 0)
        {
            return false;
        }
        for (var at = title.IndexOf(name, StringComparison.OrdinalIgnoreCase); at >= 0; at = title.IndexOf(name, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = at + name.Length;
            if ((at == 0 || !char.IsLetterOrDigit(title[at - 1])) && (end == title.Length || !char.IsLetterOrDigit(title[end])))
            {
                return true;
            }
        }
        return false;
    }
}

public sealed record DnForm(string? Dn);
