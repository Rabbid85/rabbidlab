using Microsoft.Extensions.Caching.Memory;

namespace RabbidLab.Server;

/// <summary>Someone a domain group makes an administrator: their username, display name, and whether they're enabled.</summary>
public sealed record AdminPerson(string Account, string? Name, bool Enabled);

/// <summary>A domain group's people, nested groups opened up; More when it had more than were read.</summary>
public sealed record AdminGroup(List<AdminPerson> People, bool More);

public interface IAdminGroups
{
    /// <summary>The people in each domain group asked about, by SID; null when Active Directory can't be read from here.</summary>
    Task<Dictionary<string, AdminGroup>?> People(IReadOnlyCollection<string> sids, CancellationToken stop);
}

/// <summary>
/// Opens up the domain groups in computers' Administrators groups, for Local admins' People: read as this server's own
/// computer account, so no service account is needed. Each group is kept five minutes.
/// </summary>
public sealed class AdminGroups(IDirectoryTools tools, IDomainFinder finder, IMemoryCache cache, ILogger<AdminGroups> log) : IAdminGroups
{
    public const int MaxPeople = 500;
    public const int MaxGroups = 50;

    public async Task<Dictionary<string, AdminGroup>?> People(IReadOnlyCollection<string> sids, CancellationToken stop)
    {
        var found = new Dictionary<string, AdminGroup>(StringComparer.OrdinalIgnoreCase);
        var wanted = new List<string>();
        foreach (var sid in sids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (cache.TryGetValue(Key(sid), out AdminGroup? kept) && kept is not null)
            {
                found[sid] = kept;
            }
            else
            {
                wanted.Add(sid);
            }
        }
        if (wanted.Count == 0)
        {
            return found;
        }
        if (tools.Domain is not { } domain)
        {
            return found.Count > 0 ? found : null;
        }
        try
        {
            using var ldap = await Ldap.OpenAnonymous(domain, await finder.Host(domain, null, stop), stop);
            if (ldap is null)
            {
                return found.Count > 0 ? found : null;
            }
            foreach (var sid in wanted.Take(MaxGroups))
            {
                var group = (await ldap.Find(ldap.BaseDn, $"(&(objectClass=group)(objectSid={ActiveDirectory.Escape(sid)}))", true, ["cn"], 1, stop)).FirstOrDefault();
                if (group is null)
                {
                    continue; // another domain's group, or one that's gone
                }
                // Members through nested groups too, and those who have it as their primary group (Domain Users).
                var rid = sid[(sid.LastIndexOf('-') + 1)..];
                var rows = await ldap.Find(ldap.BaseDn,
                    $"(&(objectCategory=person)(objectClass=user)(|(memberOf:1.2.840.113556.1.4.1941:={ActiveDirectory.Escape(group.Dn)})(primaryGroupID={ActiveDirectory.Escape(rid)})))",
                    true, ["sAMAccountName", "displayName", "userAccountControl"], MaxPeople + 1, stop);
                var people = rows.Where(r => r.Text("sAMAccountName") is { Length: > 0 }).Take(MaxPeople)
                    .Select(r => new AdminPerson(r.Text("sAMAccountName")!, r.Text("displayName"), ((r.Number("userAccountControl") ?? 0) & 2) == 0))
                    .OrderBy(p => p.Name ?? p.Account, StringComparer.OrdinalIgnoreCase).ToList();
                var g = new AdminGroup(people, rows.Count > MaxPeople);
                cache.Set(Key(sid), g, TimeSpan.FromMinutes(5));
                found[sid] = g;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogInformation("Couldn't read the domain groups in Administrators from {Domain}: {Problem}", domain, e.Message);
            return found.Count > 0 ? found : null;
        }
        return found;
    }

    private static string Key(string sid) => "admin-group:" + sid.ToUpperInvariant();
}
