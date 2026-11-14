using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Novell.Directory.Ldap;
using Protocols = System.DirectoryServices.Protocols;

namespace RabbidLab.Server;

/// <summary>One entry found: its distinguished name and the attributes asked for (strings, or bytes for binary ones).</summary>
public sealed class LdapRow(string dn, Dictionary<string, object[]> values)
{
    public string Dn { get; } = dn;

    public string? Text(string attribute) => values.TryGetValue(attribute, out var v) && v.Length > 0 ? v[0] as string : null;

    public IEnumerable<string> Texts(string attribute) => values.TryGetValue(attribute, out var v) ? v.OfType<string>() : [];

    public byte[]? Bytes(string attribute) => values.TryGetValue(attribute, out var v) && v.Length > 0 ? v[0] as byte[] : null;

    public IEnumerable<byte[]> AllBytes(string attribute) => values.TryGetValue(attribute, out var v) ? v.OfType<byte[]>() : [];

    public long? Number(string attribute) => long.TryParse(Text(attribute), out var n) ? n : null;

    /// <summary>How many values there are, and whether Active Directory held some back (it sends big ones in ranges).</summary>
    public (int Count, bool More) Many(string attribute)
    {
        var ranged = values.Where(v => v.Key.StartsWith(attribute + ";range=", StringComparison.OrdinalIgnoreCase)).ToList();
        return ranged.Count > 0 ? (ranged.Sum(v => v.Value.Length), ranged.Any(v => !v.Key.EndsWith("-*", StringComparison.Ordinal)))
            : (values.TryGetValue(attribute, out var v) ? v.Length : 0, false);
    }
}

public enum LdapChange
{
    Add,
    Replace,
    Delete,
}

/// <summary>A change to one attribute. Values are strings or bytes; none with Delete takes all of them away.</summary>
public sealed record LdapMod(LdapChange Change, string Attribute, params object[] Values);

/// <summary>Active Directory refused a change: the LDAP result code, and what it said.</summary>
public sealed class LdapRefused(int code, string said) : Exception(said)
{
    public const int NoSuchAttribute = 16;
    public const int ConstraintViolation = 19;
    public const int AttributeExists = 20;
    public const int NoSuchObject = 32;
    public const int InsufficientRights = 50;
    public const int Unwilling = 53;
    public const int EntryExists = 68;

    public int Code { get; } = code;
}

/// <summary>
/// A connection to Active Directory signed in as one account, for reading and changing it. Windows: Kerberos or NTLM,
/// signed and sealed, so passwords can be set without a certificate. Docker and Linux: LDAPS, then StartTLS, then plain
/// LDAP, which can read and change everything except passwords.
/// </summary>
public interface ILdap : IDisposable
{
    string BaseDn { get; }

    /// <summary>Whether the connection is encrypted, which Active Directory needs before it sets a password.</summary>
    bool Encrypted { get; }

    /// <summary>Entries under root (or root itself when not subtree). None when root isn't there.</summary>
    Task<List<LdapRow>> Find(string root, string filter, bool subtree, string[] attributes, int limit, CancellationToken stop);

    Task Add(string dn, IReadOnlyDictionary<string, object[]> attributes, CancellationToken stop);

    Task Modify(string dn, IReadOnlyList<LdapMod> changes, CancellationToken stop);

    /// <summary>Deletes it, with anything under it (some accounts have containers of their own).</summary>
    Task Delete(string dn, CancellationToken stop);

    Task WriteDacl(string dn, byte[] descriptor, CancellationToken stop);

    /// <summary>Renames or moves it (ModifyDN): rdn is its new CN=…, parent where it goes.</summary>
    Task Rename(string dn, string rdn, string parent, CancellationToken stop);

    /// <summary>Like Find, but deleted objects too (the Show Deleted control), for the Recycle Bin.</summary>
    Task<List<LdapRow>> FindDeleted(string root, string filter, bool subtree, string[] attributes, int limit, CancellationToken stop);

    /// <summary>Brings a deleted object back as newDn: isDeleted removed and the name it had given again.</summary>
    Task Restore(string dn, string newDn, CancellationToken stop);

    /// <summary>Every attribute an object has, each value as bytes (some are binary), for the attribute browser.</summary>
    Task<Dictionary<string, byte[][]>?> ReadAll(string dn, CancellationToken stop);

    /// <summary>What's directly inside an object (one level down).</summary>
    Task<List<LdapRow>> FindChildren(string dn, string[] attributes, int limit, CancellationToken stop);

    /// <summary>Who the server says is signed in (RFC 4532; Active Directory answers u:DOMAIN\name), or null when it won't say.</summary>
    Task<string?> WhoAmI(CancellationToken stop);
}

/// <summary>Signs in to a domain controller. DirectoryTools takes one so tests can stand in for Active Directory.</summary>
public delegate Task<ILdap?> LdapOpener(string domain, string host, string username, string password, CancellationToken stop);

public static class Ldap
{
    /// <summary>The attributes that come back as bytes.</summary>
    private static readonly HashSet<string> Binary = new(StringComparer.OrdinalIgnoreCase) { "objectSid", "objectGUID", "nTSecurityDescriptor", "tokenGroups", "dNSProperty" };

    private const string SdFlags = "1.2.840.113556.1.4.801";
    private const string TreeDelete = "1.2.840.113556.1.4.805";
    private const string ShowDeleted = "1.2.840.113556.1.4.417";
    private const string WhoAmIOid = "1.3.6.1.4.1.4203.1.11.3";

    /// <summary>The DACL only, as LDAP_SERVER_SD_FLAGS_OID takes it: SEQUENCE { INTEGER 4 }.</summary>
    private static readonly byte[] DaclOnly = [0x30, 0x03, 0x02, 0x01, 0x04];

    // A domain's NetBIOS name never changes, so it's read once per base DN.
    private static readonly ConcurrentDictionary<string, string> NetBiosNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Signed in as the account, or null when its password is wrong. DirectoryProblem when the domain can't be reached.</summary>
    public static async Task<ILdap?> Open(string domain, string host, string username, string password, CancellationToken stop)
    {
        var name = username.Trim();
        if (name.Length == 0 || password.Length == 0)
        {
            return null;
        }
        return OperatingSystem.IsWindows()
            ? await Task.Run(() => WindowsLdap.Open(domain, host, name, password), stop)
            : await ManagedLdap.Open(domain, host, name.Contains('\\') || name.Contains('@') ? name : $"{name}@{domain}", password, stop);
    }

    /// <summary>Connected without an account: as this computer (Windows) or anonymously (elsewhere). Null when this computer can't sign in there.</summary>
    public static async Task<ILdap?> OpenAnonymous(string domain, string host, CancellationToken stop) =>
        OperatingSystem.IsWindows()
            ? await Task.Run(() => WindowsLdap.Open(domain, host, "", null), stop)
            : await ManagedLdap.Open(domain, host, null, null, stop);

    /// <summary>Its security descriptor with only the DACL, or null when this account can't read it.</summary>
    public static async Task<byte[]?> ReadDacl(this ILdap ldap, string dn, CancellationToken stop) =>
        (await ldap.Find(dn, "(objectClass=*)", false, ["nTSecurityDescriptor"], 1, stop)).FirstOrDefault()?.Bytes("nTSecurityDescriptor");

    /// <summary>
    /// The account that signed in, found by who the server says it is, not by what was typed: OTHER\bob from a trusted
    /// domain isn't this domain's bob. Null when it isn't a person here. DirectoryProblem when it's another domain's account.
    /// </summary>
    public static async Task<LdapRow?> Bound(ILdap ldap, string typed, string[] attributes, CancellationToken stop)
    {
        async Task<LdapRow?> By(string attribute, string value) =>
            (await ldap.Find(ldap.BaseDn, $"(&(objectCategory=person)(objectClass=user)({attribute}={ActiveDirectory.Escape(value)}))", true, attributes, 1, stop)).FirstOrDefault();

        if (Who(await ldap.WhoAmI(stop)) is { } who && await NetBios(ldap, stop) is { } ours)
        {
            return string.Equals(who.Domain, ours, StringComparison.OrdinalIgnoreCase) ? await By("sAMAccountName", who.Name)
                : throw new DirectoryProblem($"{who.Domain}\\{who.Name} is an account in another domain.");
        }
        // The server didn't say: a name with @ is looked up as a UPN before its first part is tried as a username.
        var name = typed.Trim();
        if (name.Contains('@') && !name.Contains('\\') && await By("userPrincipalName", name) is { } byUpn)
        {
            return byUpn;
        }
        return await By("sAMAccountName", Account(name));
    }

    /// <summary>DOMAIN and name from WhoAmI's u:DOMAIN\name, or null when it's something else.</summary>
    public static (string Domain, string Name)? Who(string? authzId)
    {
        var id = authzId?.Trim() ?? "";
        id = id.StartsWith("u:", StringComparison.OrdinalIgnoreCase) ? id[2..] : id;
        var slash = id.IndexOf('\\');
        return slash > 0 && slash < id.Length - 1 ? (id[..slash], id[(slash + 1)..]) : null;
    }

    /// <summary>The domain's NetBIOS name (AMW for amw.com), from its crossRef in the configuration partition. Null when it can't be read.</summary>
    private static async Task<string?> NetBios(ILdap ldap, CancellationToken stop)
    {
        if (NetBiosNames.TryGetValue(ldap.BaseDn, out var known))
        {
            return known;
        }
        try
        {
            var config = (await ldap.Find("", "(objectClass=*)", false, ["configurationNamingContext"], 1, stop)).FirstOrDefault()?.Text("configurationNamingContext");
            var name = config is null ? null
                : (await ldap.Find($"CN=Partitions,{config}", $"(&(objectClass=crossRef)(nCName={ActiveDirectory.Escape(ldap.BaseDn)}))", true, ["nETBIOSName"], 1, stop))
                    .FirstOrDefault()?.Text("nETBIOSName");
            if (!string.IsNullOrWhiteSpace(name))
            {
                NetBiosNames[ldap.BaseDn] = name;
                return name;
            }
        }
        catch (LdapRefused)
        {
            // Not allowed to read it: the caller falls back to the name typed.
        }
        return null;
    }

    /// <summary>The account part of DOMAIN\name or name@domain.</summary>
    public static string Account(string name)
    {
        var account = name[(name.LastIndexOf('\\') + 1)..];
        return account.Contains('@') ? account[..account.IndexOf('@')] : account;
    }

    /// <summary>The CN of a distinguished name: Anthony Wiles for CN=Anthony Wiles,OU=Lab,DC=amw,DC=com.</summary>
    public static string Name(string dn)
    {
        var first = Split(dn).FirstOrDefault() ?? dn;
        var eq = first.IndexOf('=');
        return Unescape(eq < 0 ? first : first[(eq + 1)..]);
    }

    /// <summary>Where it is, readably: Lab / People for CN=x,OU=People,OU=Lab,DC=amw,DC=com. The domain itself is empty.</summary>
    public static string Path(string dn, string baseDn)
    {
        var inside = dn.EndsWith(baseDn, StringComparison.OrdinalIgnoreCase) ? dn[..^baseDn.Length].TrimEnd(',') : dn;
        return string.Join(" / ", Split(inside).Select(p => Unescape(p[(p.IndexOf('=') + 1)..])).Reverse());
    }

    /// <summary>The DN's parts, split on commas that aren't escaped.</summary>
    public static List<string> Split(string dn)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i < dn.Length; i++)
        {
            if (dn[i] == '\\')
            {
                i++;
            }
            else if (dn[i] == ',')
            {
                parts.Add(dn[start..i].Trim());
                start = i + 1;
            }
        }
        if (start < dn.Length)
        {
            parts.Add(dn[start..].Trim());
        }
        return parts;
    }

    /// <summary>The parent of a DN.</summary>
    public static string Parent(string dn) => string.Join(',', Split(dn).Skip(1));

    /// <summary>Whether a DN is this one or under it, compared part by part so an escaped comma can't fake a parent.</summary>
    public static bool Under(string dn, string root)
    {
        var parts = Split(dn);
        var roots = Split(root);
        return roots.Count > 0 && parts.Count >= roots.Count && parts.Skip(parts.Count - roots.Count).SequenceEqual(roots, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A value as a DN takes it (RFC 4514).</summary>
    public static string Escape(string value)
    {
        var s = string.Concat(value.Select(c => c is ',' or '+' or '"' or '\\' or '<' or '>' or ';' or '=' ? "\\" + c : c.ToString()));
        if (s.StartsWith('#') || s.StartsWith(' '))
        {
            s = "\\" + s;
        }
        return s.EndsWith(' ') ? s[..^1] + "\\ " : s;
    }

    private static string Unescape(string value)
    {
        var b = new System.Text.StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                if (i + 2 < value.Length && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
                {
                    b.Append((char) Convert.ToInt32(value.Substring(i + 1, 2), 16));
                    i += 2;
                    continue;
                }
                i++;
            }
            b.Append(value[i]);
        }
        return b.ToString();
    }

    /// <summary>A password as Active Directory takes it in unicodePwd: in quotes, UTF-16.</summary>
    public static byte[] Password(string password) => System.Text.Encoding.Unicode.GetBytes("\"" + password + "\"");

    private static IEnumerable<Exception> Chain(Exception e)
    {
        for (var x = e; x is not null; x = x.InnerException)
        {
            yield return x;
        }
    }

    /// <summary>The most useful words about what went wrong: the directory's own, else the network's.</summary>
    private static string Said(Exception e) =>
        (e is LdapException { LdapErrorMessage: { Length: > 0 } said } ? said
            : Chain(e).LastOrDefault(x => x is not LdapException)?.Message ?? e.Message).Trim().TrimEnd('\0', '.');

    // --- Windows: System.DirectoryServices.Protocols with Negotiate ------------------------------------------------

    private sealed class WindowsLdap(Protocols.LdapConnection ldap, string baseDn) : ILdap
    {
        public string BaseDn { get; } = baseDn;

        public bool Encrypted => true;

        /// <summary>Signed in as the account, or as this computer when there's no name. Null when that sign-in doesn't work.</summary>
        public static ILdap? Open(string domain, string host, string name, string? password)
        {
            // The domain itself lets Windows find a domain controller; a chosen one is used by name.
            var identifier = string.Equals(host, domain, StringComparison.OrdinalIgnoreCase)
                ? new Protocols.LdapDirectoryIdentifier(domain, 389, false, false) : new Protocols.LdapDirectoryIdentifier(host, 389, true, false);
            var ldap = name.Length == 0 ? new Protocols.LdapConnection(identifier)
                : new Protocols.LdapConnection(identifier,
                    name.Contains('\\') ? new NetworkCredential(Account(name), password, name[..name.IndexOf('\\')])
                    : name.Contains('@') ? new NetworkCredential(name, password)
                    : new NetworkCredential(name, password, domain));
            var keep = false;
            try
            {
                ldap.AuthType = Protocols.AuthType.Negotiate;
                ldap.Timeout = TimeSpan.FromSeconds(30);
                ldap.SessionOptions.ProtocolVersion = 3;
                ldap.SessionOptions.Signing = true;
                ldap.SessionOptions.Sealing = true;
                ldap.SessionOptions.ReferralChasing = Protocols.ReferralChasingOptions.None;
                ldap.Bind();
                var root = (Protocols.SearchResponse) ldap.SendRequest(new Protocols.SearchRequest("", "(objectClass=*)", Protocols.SearchScope.Base, "defaultNamingContext"));
                var baseDn = root.Entries.Count > 0 && root.Entries[0].Attributes.Contains("defaultNamingContext") ? root.Entries[0].Attributes["defaultNamingContext"][0] as string : null;
                var open = new WindowsLdap(ldap, baseDn ?? throw new DirectoryProblem($"{domain} didn't say where its directory starts."));
                keep = true;
                return open;
            }
            catch (Protocols.LdapException e) when (e.ErrorCode == 81)
            {
                throw new DirectoryProblem($"{host} couldn't be reached. Check the name.");
            }
            catch (Protocols.LdapException e) when (e.ErrorCode == 49 || name.Length == 0)
            {
                return null; // wrong password, or this computer can't sign in there
            }
            catch (Protocols.LdapException e)
            {
                throw new DirectoryProblem($"{domain} refused the sign-in: {e.Message.TrimEnd('.')}.");
            }
            catch (Protocols.DirectoryException e)
            {
                throw new DirectoryProblem($"{domain} couldn't be read: {e.Message.TrimEnd('.')}.");
            }
            finally
            {
                if (!keep)
                {
                    ldap.Dispose();
                }
            }
        }

        public Task<List<LdapRow>> Find(string root, string filter, bool subtree, string[] attributes, int limit, CancellationToken stop) =>
            Task.Run(() => Search(root, filter, subtree ? Protocols.SearchScope.Subtree : Protocols.SearchScope.Base, attributes, limit,
                attributes.Contains("nTSecurityDescriptor", StringComparer.OrdinalIgnoreCase) ? [new Protocols.SecurityDescriptorFlagControl(Protocols.SecurityMasks.Dacl)] : []), stop);

        public Task<List<LdapRow>> FindDeleted(string root, string filter, bool subtree, string[] attributes, int limit, CancellationToken stop) =>
            Task.Run(() => Search(root, filter, subtree ? Protocols.SearchScope.Subtree : Protocols.SearchScope.Base, attributes, limit, new Protocols.ShowDeletedControl()), stop);

        public Task<List<LdapRow>> FindChildren(string dn, string[] attributes, int limit, CancellationToken stop) =>
            Task.Run(() => Search(dn, "(objectClass=*)", Protocols.SearchScope.OneLevel, attributes, limit), stop);

        public Task Add(string dn, IReadOnlyDictionary<string, object[]> attributes, CancellationToken stop) =>
            Send(new Protocols.AddRequest(dn, [.. attributes.Select(a => new Protocols.DirectoryAttribute(a.Key, a.Value))]), stop);

        public Task Modify(string dn, IReadOnlyList<LdapMod> changes, CancellationToken stop) =>
            Send(new Protocols.ModifyRequest(dn, [.. changes.Select(Change)]), stop);

        public Task Delete(string dn, CancellationToken stop)
        {
            var request = new Protocols.DeleteRequest(dn);
            request.Controls.Add(new Protocols.TreeDeleteControl());
            return Send(request, stop);
        }

        public Task WriteDacl(string dn, byte[] descriptor, CancellationToken stop)
        {
            var request = new Protocols.ModifyRequest(dn, Protocols.DirectoryAttributeOperation.Replace, "nTSecurityDescriptor", descriptor);
            request.Controls.Add(new Protocols.SecurityDescriptorFlagControl(Protocols.SecurityMasks.Dacl));
            return Send(request, stop);
        }

        public Task Rename(string dn, string rdn, string parent, CancellationToken stop) =>
            Send(new Protocols.ModifyDNRequest(dn, parent, rdn) { DeleteOldRdn = true }, stop);

        public Task Restore(string dn, string newDn, CancellationToken stop)
        {
            var request = new Protocols.ModifyRequest(dn,
                new Protocols.DirectoryAttributeModification { Name = "isDeleted", Operation = Protocols.DirectoryAttributeOperation.Delete },
                Change(new LdapMod(LdapChange.Replace, "distinguishedName", newDn)));
            request.Controls.Add(new Protocols.ShowDeletedControl());
            return Send(request, stop);
        }

        public Task<Dictionary<string, byte[][]>?> ReadAll(string dn, CancellationToken stop) => Task.Run(() =>
        {
            if (Ask(new Protocols.SearchRequest(dn, "(objectClass=*)", Protocols.SearchScope.Base, "*"), "read") is not Protocols.SearchResponse { Entries.Count: > 0 } response)
            {
                return null;
            }
            var e = response.Entries[0];
            var all = new Dictionary<string, byte[][]>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in e.Attributes.AttributeNames)
            {
                // The collection keeps names in lower case; the attribute itself has them as Active Directory spells them.
                all[e.Attributes[name].Name ?? name] = e.Attributes[name].GetValues(typeof(byte[])).Cast<byte[]>().ToArray();
            }
            return all;
        }, stop);

        public Task<string?> WhoAmI(CancellationToken stop) => Task.Run(() =>
        {
            try
            {
                return Ask(new Protocols.ExtendedRequest(WhoAmIOid), "asked") is Protocols.ExtendedResponse { ResponseValue: { Length: > 0 } value }
                    ? System.Text.Encoding.UTF8.GetString(value) : null;
            }
            catch (LdapRefused)
            {
                return null; // the server doesn't answer WhoAmI
            }
        }, stop);

        private List<LdapRow> Search(string root, string filter, Protocols.SearchScope scope, string[] attributes, int limit, params Protocols.DirectoryControl[] controls)
        {
            var request = new Protocols.SearchRequest(root, filter, scope, attributes) { SizeLimit = limit };
            request.Controls.AddRange(controls);
            if (Ask(request, "searched") is not Protocols.SearchResponse response)
            {
                return [];
            }
            return [.. response.Entries.Cast<Protocols.SearchResultEntry>().Select(e =>
            {
                var values = new Dictionary<string, object[]>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in e.Attributes.AttributeNames)
                {
                    values[name] = Binary.Contains(name) ? e.Attributes[name].GetValues(typeof(byte[])) : e.Attributes[name].GetValues(typeof(string));
                }
                return new LdapRow(e.DistinguishedName, values);
            })];
        }

        /// <summary>One request and its answer. A search cut short at the size limit gives what it has; one whose root isn't there gives null.</summary>
        private Protocols.DirectoryResponse? Ask(Protocols.DirectoryRequest request, string doing)
        {
            try
            {
                return ldap.SendRequest(request);
            }
            catch (Protocols.DirectoryOperationException e) when (request is Protocols.SearchRequest && e.Response is Protocols.SearchResponse { ResultCode: Protocols.ResultCode.SizeLimitExceeded } partial)
            {
                return partial;
            }
            catch (Protocols.DirectoryOperationException e) when (request is Protocols.SearchRequest && e.Response?.ResultCode == Protocols.ResultCode.NoSuchObject)
            {
                return null;
            }
            catch (Protocols.DirectoryOperationException e)
            {
                throw new LdapRefused((int) (e.Response?.ResultCode ?? Protocols.ResultCode.Other),
                    (e.Response?.ErrorMessage is { Length: > 0 } said ? said : e.Message).Trim().TrimEnd('\0', '.'));
            }
            catch (Protocols.DirectoryException e)
            {
                throw new DirectoryProblem($"Active Directory couldn't be {doing}: {e.Message.TrimEnd('.')}.");
            }
        }

        private Task Send(Protocols.DirectoryRequest request, CancellationToken stop) => Task.Run(() => { Ask(request, "changed"); }, stop);

        private static Protocols.DirectoryAttributeModification Change(LdapMod m)
        {
            var change = new Protocols.DirectoryAttributeModification
            {
                Name = m.Attribute,
                Operation = m.Change switch
                {
                    LdapChange.Add => Protocols.DirectoryAttributeOperation.Add,
                    LdapChange.Delete => Protocols.DirectoryAttributeOperation.Delete,
                    _ => Protocols.DirectoryAttributeOperation.Replace,
                },
            };
            foreach (var v in m.Values)
            {
                if (v is byte[] bytes)
                {
                    change.Add(bytes);
                }
                else
                {
                    change.Add(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!);
                }
            }
            return change;
        }

        public void Dispose() => ldap.Dispose();
    }

    // --- Docker and Linux: the managed client ----------------------------------------------------------------------

    private sealed class ManagedLdap(LdapConnection ldap, string baseDn, bool secure) : ILdap
    {
        private enum Transport
        {
            Ldaps,
            StartTls,
            Plain,
        }

        public string BaseDn { get; } = baseDn;

        public bool Encrypted { get; } = secure;

        /// <summary>Connected over LDAPS, else StartTLS, else plain LDAP, and signed in when there's an account. Null when the password is wrong.</summary>
        public static async Task<ILdap?> Open(string domain, string host, string? bindAs, string? password, CancellationToken stop)
        {
            string? problem = null;
            foreach (var transport in new[] { Transport.Ldaps, Transport.StartTls, Transport.Plain })
            {
                var options = new LdapConnectionOptions();
                if (transport == Transport.Ldaps)
                {
                    options.UseSsl();
                }
                var connection = new LdapConnection(options) { ConnectionTimeout = 5000 };
                var keep = false;
                try
                {
                    await connection.ConnectAsync(host, transport == Transport.Ldaps ? 636 : 389, stop);
                    if (transport == Transport.StartTls)
                    {
                        await connection.StartTlsAsync(stop);
                    }
                    if (bindAs is not null)
                    {
                        await connection.BindAsync(bindAs, password ?? "", stop);
                    }
                    var root = await connection.ReadAsync("", ["defaultNamingContext"], stop);
                    var baseDn = root.GetAttributeSet().Values.FirstOrDefault(a => a.Name.Equals("defaultNamingContext", StringComparison.OrdinalIgnoreCase))?.StringValue
                        ?? throw new DirectoryProblem($"{domain} didn't say where its directory starts.");
                    var open = new ManagedLdap(connection, baseDn, transport != Transport.Plain);
                    keep = true;
                    return open;
                }
                catch (LdapException e) when (e.ResultCode == LdapException.InvalidCredentials)
                {
                    return null;
                }
                // StartTLS is only an attempt on the way to plain LDAP, so whatever stops it moves on to that.
                catch (Exception e) when (e is LdapException or SocketException or IOException or System.Security.Authentication.AuthenticationException or TimeoutException
                    || (transport == Transport.StartTls && e is not (OperationCanceledException or DirectoryProblem)))
                {
                    if (Chain(e).OfType<SocketException>().Any(x => x.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData))
                    {
                        throw new DirectoryProblem($"{host} wasn't found. Check the name.");
                    }
                    problem = e is LdapException { ResultCode: LdapException.StrongAuthRequired or LdapException.ConfidentialityRequired }
                        ? $"{host} only accepts sign-ins over an encrypted connection, and LDAPS and StartTLS didn't work. Give the domain controllers a certificate this computer trusts."
                        : $"{host} couldn't be reached: {Said(e)}.";
                }
                finally
                {
                    if (!keep)
                    {
                        connection.Dispose();
                    }
                }
            }
            throw new DirectoryProblem(problem ?? $"{host} couldn't be reached.");
        }

        public Task<List<LdapRow>> Find(string root, string filter, bool subtree, string[] attributes, int limit, CancellationToken stop) =>
            Search(root, subtree ? LdapConnection.ScopeSub : LdapConnection.ScopeBase, filter, attributes, limit,
                attributes.Contains("nTSecurityDescriptor", StringComparer.OrdinalIgnoreCase) ? new LdapControl(SdFlags, true, DaclOnly) : null, stop);

        public Task<List<LdapRow>> FindDeleted(string root, string filter, bool subtree, string[] attributes, int limit, CancellationToken stop) =>
            Search(root, subtree ? LdapConnection.ScopeSub : LdapConnection.ScopeBase, filter, attributes, limit, new LdapControl(ShowDeleted, true, null), stop);

        public Task<List<LdapRow>> FindChildren(string dn, string[] attributes, int limit, CancellationToken stop) =>
            Search(dn, LdapConnection.ScopeOne, "(objectClass=*)", attributes, limit, null, stop);

        public async Task Add(string dn, IReadOnlyDictionary<string, object[]> attributes, CancellationToken stop)
        {
            var set = new LdapAttributeSet();
            foreach (var (name, values) in attributes)
            {
                set.Add(Attribute(name, values));
            }
            await Run(() => ldap.AddAsync(new LdapEntry(dn, set), stop));
        }

        public Task Modify(string dn, IReadOnlyList<LdapMod> changes, CancellationToken stop) =>
            Run(() => ldap.ModifyAsync(dn, [.. changes.Select(m => new LdapModification(
                m.Change switch { LdapChange.Add => LdapModification.Add, LdapChange.Delete => LdapModification.Delete, _ => LdapModification.Replace },
                Attribute(m.Attribute, m.Values)))], stop));

        public Task Delete(string dn, CancellationToken stop)
        {
            var constraints = new LdapConstraints();
            constraints.SetControls(new LdapControl(TreeDelete, true, null));
            return Run(() => ldap.DeleteAsync(dn, constraints, stop));
        }

        public Task WriteDacl(string dn, byte[] descriptor, CancellationToken stop)
        {
            var constraints = new LdapConstraints();
            constraints.SetControls(new LdapControl(SdFlags, true, DaclOnly));
            return Run(() => ldap.ModifyAsync(dn, new LdapModification(LdapModification.Replace, new LdapAttribute("nTSecurityDescriptor", descriptor)), constraints, stop));
        }

        public Task Rename(string dn, string rdn, string parent, CancellationToken stop) =>
            Run(() => ldap.RenameAsync(dn, rdn, parent, true, stop));

        public Task Restore(string dn, string newDn, CancellationToken stop)
        {
            var constraints = new LdapConstraints();
            constraints.SetControls(new LdapControl(ShowDeleted, true, null));
            return Run(() => ldap.ModifyAsync(dn, [new LdapModification(LdapModification.Delete, new LdapAttribute("isDeleted")),
                new LdapModification(LdapModification.Replace, new LdapAttribute("distinguishedName", newDn))], constraints, stop));
        }

        public async Task<Dictionary<string, byte[][]>?> ReadAll(string dn, CancellationToken stop)
        {
            try
            {
                var results = await ldap.SearchAsync(dn, LdapConnection.ScopeBase, "(objectClass=*)", ["*"], false, stop);
                if (!await results.HasMoreAsync(stop))
                {
                    return null;
                }
                var e = await results.NextAsync(stop);
                return e.GetAttributeSet().Values.ToDictionary(a => a.Name, a => a.ByteValueArray, StringComparer.OrdinalIgnoreCase);
            }
            catch (LdapException e) when (e.ResultCode == LdapException.NoSuchObject)
            {
                return null;
            }
            catch (Exception e) when (Failed(e) is { } failed)
            {
                throw failed;
            }
        }

        public async Task<string?> WhoAmI(CancellationToken stop)
        {
            try
            {
                var answer = await ldap.ExtendedOperationAsync(new LdapExtendedOperation(WhoAmIOid, null), stop);
                return answer?.Value is { Length: > 0 } value ? System.Text.Encoding.UTF8.GetString(value) : null;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // No answer is fine (the caller goes by the name typed); a lost connection isn't.
                return Failed(e) is DirectoryProblem gone ? throw gone : null;
            }
        }

        private async Task<List<LdapRow>> Search(string root, int scope, string filter, string[] attributes, int limit, LdapControl? control, CancellationToken stop)
        {
            var constraints = new LdapSearchConstraints { MaxResults = limit, ReferralFollowing = false };
            if (control is not null)
            {
                constraints.SetControls(control);
            }
            var rows = new List<LdapRow>();
            try
            {
                var results = await ldap.SearchAsync(root, scope, filter, attributes, false, constraints, stop);
                while (await results.HasMoreAsync(stop))
                {
                    try
                    {
                        var e = await results.NextAsync(stop);
                        var values = new Dictionary<string, object[]>(StringComparer.OrdinalIgnoreCase);
                        foreach (var a in e.GetAttributeSet().Values)
                        {
                            values[a.Name] = Binary.Contains(a.Name) ? a.ByteValueArray.Cast<object>().ToArray() : a.StringValueArray.Cast<object>().ToArray();
                        }
                        rows.Add(new LdapRow(e.Dn, values));
                    }
                    catch (LdapReferralException)
                    {
                        // Another domain's part of the forest.
                    }
                    catch (LdapException e) when (e.ResultCode == LdapException.SizeLimitExceeded)
                    {
                        break;
                    }
                }
            }
            catch (LdapException e) when (e.ResultCode == LdapException.NoSuchObject)
            {
                return [];
            }
            catch (Exception e) when (Failed(e) is { } failed)
            {
                throw failed;
            }
            return rows;
        }

        private static LdapAttribute Attribute(string name, object[] values)
        {
            var a = new LdapAttribute(name);
            foreach (var v in values)
            {
                if (v is byte[] bytes)
                {
                    a.AddValue(bytes);
                }
                else
                {
                    a.AddValue(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!);
                }
            }
            return a;
        }

        private static async Task Run(Func<Task> change)
        {
            try
            {
                await change();
            }
            catch (Exception e) when (Failed(e) is { } failed)
            {
                throw failed;
            }
        }

        /// <summary>What went wrong, as the rest of Rabbid Lab takes it: refused by the directory, or the connection gone. Null for anything else.</summary>
        private static Exception? Failed(Exception e) => e switch
        {
            LdapException { ResultCode: LdapException.ServerDown or LdapException.ConnectError or LdapException.LdapTimeout } => new DirectoryProblem($"Active Directory couldn't be reached: {Said(e)}."),
            LdapException x => new LdapRefused(x.ResultCode, (x.LdapErrorMessage is { Length: > 0 } said ? said : x.Message).Trim().TrimEnd('\0', '.')),
            IOException or SocketException or ObjectDisposedException => new DirectoryProblem($"Active Directory couldn't be reached: {Said(e)}."),
            _ => null,
        };

        public void Dispose() => ldap.Dispose();
    }
}
