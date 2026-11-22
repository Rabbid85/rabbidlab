using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>The Active Directory reads and writes offboarding needs that the Users page has no button for.</summary>
public interface IOffboardDirectory
{
    /// <summary>Whether the schema has Exchange's attributes (msExchHideFromAddressLists).</summary>
    Task<bool> HasExchange(CancellationToken stop);

    /// <summary>Sets msExchHideFromAddressLists to TRUE. False when the schema has no Exchange attributes, so nothing changed.</summary>
    Task<bool> HideFromAddressLists(string username, CancellationToken stop);

    /// <summary>Clears telephoneNumber, mobile and physicalDeliveryOfficeName; the ones that were set, by name.</summary>
    Task<List<string>> ClearContact(string username, CancellationToken stop);

    /// <summary>The people whose manager is this DN.</summary>
    Task<List<AdMember>> Reports(string dn, CancellationToken stop);

    /// <summary>The computer accounts whose managedBy is this DN.</summary>
    Task<List<AdMember>> ManagedComputers(string dn, CancellationToken stop);

    /// <summary>The mail of the account with this logon name, anywhere in the domain, or null.</summary>
    Task<string?> MailOf(string username, CancellationToken stop);

    /// <summary>The mail of the object at this DN, or null.</summary>
    Task<string?> MailAt(string dn, CancellationToken stop);
}

public sealed partial class DirectoryTools : IOffboardDirectory
{
    private const string HideAttribute = "msExchHideFromAddressLists";

    public async Task<bool> HasExchange(CancellationToken stop)
    {
        using var ldap = await Open(stop);
        return await Exchange(ldap, stop);
    }

    private static async Task<bool> Exchange(ILdap ldap, CancellationToken stop)
    {
        var root = (await ldap.Find("", "(objectClass=*)", false, ["schemaNamingContext", "configurationNamingContext"], 1, stop)).FirstOrDefault();
        var schema = root?.Text("schemaNamingContext") ?? (root?.Text("configurationNamingContext") is { } config ? $"CN=Schema,{config}" : null);
        if (schema is null)
        {
            return false;
        }
        try
        {
            return (await ldap.Find(schema, $"(lDAPDisplayName={HideAttribute})", true, ["lDAPDisplayName"], 1, stop)).Count > 0;
        }
        catch (LdapRefused e) when (e.Code == LdapRefused.NoSuchObject)
        {
            return false;
        }
    }

    public async Task<bool> HideFromAddressLists(string username, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        if (!await Exchange(ldap, stop))
        {
            return false;
        }
        var user = await Changeable(ldap, f, username, stop);
        var was = (await ldap.Find(user.Dn, "(objectClass=*)", false, [HideAttribute], 1, stop)).FirstOrDefault()?.Text(HideAttribute);
        if (!string.Equals(was, "TRUE", StringComparison.OrdinalIgnoreCase))
        {
            await Change(ldap, user.Dn, [new(LdapChange.Replace, HideAttribute, "TRUE")], f, password: false, stop);
            Note(user, "user", SetStep("Hide from address lists", HideAttribute, Shown(was), ["TRUE"]));
        }
        return true;
    }

    public async Task<List<string>> ClearContact(string username, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var user = await Changeable(ldap, f, username, stop);
        (string Attribute, string Label)[] fields = [("telephoneNumber", "Phone"), ("mobile", "Mobile"), ("physicalDeliveryOfficeName", "Office")];
        var set = fields.Where(x => user.Text(x.Attribute) is { Length: > 0 }).ToList();
        if (set.Count > 0)
        {
            await Change(ldap, user.Dn, [.. set.Select(x => new LdapMod(LdapChange.Delete, x.Attribute))], f, password: false, stop);
            Note(user, "user", [.. set.Select(x => SetStep(x.Label, x.Attribute, [user.Text(x.Attribute)!], []))]);
        }
        return [.. set.Select(x => x.Label.ToLowerInvariant())];
    }

    public async Task<List<AdMember>> Reports(string dn, CancellationToken stop) =>
        await Around(dn, "(&(objectCategory=person)(objectClass=user)(manager={0}))", stop);

    public async Task<List<AdMember>> ManagedComputers(string dn, CancellationToken stop) =>
        await Around(dn, "(&(objectCategory=computer)(managedBy={0}))", stop);

    // What points at a DN, inside what the caller's roles cover.
    private async Task<List<AdMember>> Around(string dn, string filter, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        var f = Ready();
        var rows = await Look(ldap, f.Scope!, string.Format(System.Globalization.CultureInfo.InvariantCulture, filter, ActiveDirectory.Escape(Inside(dn, ldap.BaseDn))), MemberAttributes, Limit, stop);
        return [.. rows.Select(r => Member(r, ldap.BaseDn)).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<string?> MailOf(string username, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        Ready();
        var row = (await ldap.Find(ldap.BaseDn, $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={ActiveDirectory.Escape(username.Trim())}))", true, ["mail"], 1, stop)).FirstOrDefault();
        return Words.Blank(row?.Text("mail"));
    }

    public async Task<string?> MailAt(string dn, CancellationToken stop)
    {
        using var ldap = await Open(stop);
        Ready();
        var row = (await ldap.Find(Inside(dn, ldap.BaseDn), "(objectClass=*)", false, ["mail"], 1, stop)).FirstOrDefault();
        return Words.Blank(row?.Text("mail"));
    }
}
