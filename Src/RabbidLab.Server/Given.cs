using System.Security.Cryptography;
using System.Text.Json;

namespace RabbidLab.Server;

/// <summary>
/// One thing Rabbid Lab gave someone: the system it is in (ad), what kind of thing (account, group), what identifies it
/// there (a group's DN), how it reads, who gave it, when, and in which change. Took* once it was taken back.
/// </summary>
public sealed class GivenEntry
{
    public string Id { get; set; } = "";
    public string System { get; set; } = "ad";
    public string Kind { get; set; } = "";
    public string Key { get; set; } = "";
    public string What { get; set; } = "";
    public DateTime At { get; set; }
    public string By { get; set; } = "";
    public string How { get; set; } = "";
    public DateTime? TookAt { get; set; }
    public string? TookBy { get; set; }
    public string? TookHow { get; set; }
}

/// <summary>Someone and what they were given: by objectGUID once a change to them gave one, by DN until then.</summary>
public sealed class GivenPerson
{
    public string? Guid { get; set; }
    public string Dn { get; set; } = "";
    public string Name { get; set; } = "";
    public List<GivenEntry> Entries { get; set; } = [];
}

public sealed class GivenFile
{
    public List<GivenPerson> People { get; set; } = [];
}

/// <summary>
/// The record of what Rabbid Lab gave each person (given.json), kept for as long as they have it: the account it made
/// and the groups it put them in, by hand, in a workflow or any other way. It is written from the change journal, so
/// every Active Directory change counts without each one having to say so. Offboarding and the person's panel read it:
/// what was given can be taken back exactly, and anything else they have was given somewhere else.
/// </summary>
public sealed class GivenStore
{
    /// <summary>The most kept for one person; the oldest that were taken back go first.</summary>
    public const int Most = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonFile<GivenFile> file;

    public GivenStore(DataFolder folder, ChangeJournal journal)
    {
        file = folder.Json<GivenFile>("given.json", Json);
        journal.Added += Note;
    }

    /// <summary>What they were given, newest first, with what was taken back since.</summary>
    public IReadOnlyList<GivenEntry> For(string dn)
    {
        lock (file.Gate)
        {
            return [.. file.Read().People.Where(p => Same(p.Dn, dn)).SelectMany(p => p.Entries).OrderByDescending(e => e.At)];
        }
    }

    /// <summary>Reads a change the journal kept: an account made, groups joined or left, members added or removed, an account deleted.</summary>
    public void Note(Change c)
    {
        var o = c.Object;
        if (o.Kind is not ("user" or "group") || c.Steps.Count == 0)
        {
            return;
        }
        lock (file.Gate)
        {
            var f = file.Read();
            var changed = false;
            // Someone already on record follows a move or rename, which keeps the GUID and changes the DN.
            if (o.Kind == "user" && Find(f, o.Guid, o.Dn) is { } known && (!Same(known.Dn, o.Dn) || known.Name != o.Name))
            {
                (known.Dn, known.Name, changed) = (o.Dn, o.Name, true);
            }
            foreach (var s in c.Steps)
            {
                if (o.Kind == "user" && s.Label == "Account" && s.Before is ["None"])
                {
                    changed |= Give(Person(f, o.Guid, o.Dn, o.Name), "account", "", $"Account {s.After.FirstOrDefault() ?? o.Name}", c);
                }
                else if (o.Kind == "user" && s.Label == "Location" && s.After is ["Recycle Bin"] or ["Deleted permanently"])
                {
                    changed |= Find(f, o.Guid, o.Dn) is { } gone && gone.Entries.Count(e => Take(e, c)) > 0;
                }
                else if (o.Kind == "user" && s.Undo is { Kind: "groups" } groups)
                {
                    // The undo holds the reverse: what it would take away is what was added.
                    foreach (var dn in groups.Remove ?? [])
                    {
                        changed |= Give(Person(f, o.Guid, o.Dn, o.Name), "group", dn, $"Group {Ldap.Name(dn)}", c);
                    }
                    changed |= Find(f, o.Guid, o.Dn) is { } p && TakeGroups(p, groups.Add ?? [], c);
                }
                else if (o.Kind == "group" && s.Undo is { Kind: "members" } members)
                {
                    foreach (var dn in members.Remove ?? [])
                    {
                        changed |= Give(Person(f, null, dn, Ldap.Name(dn)), "group", o.Dn, $"Group {o.Name}", c);
                    }
                    foreach (var dn in members.Add ?? [])
                    {
                        changed |= Find(f, null, dn) is { } p && TakeGroups(p, [o.Dn], c);
                    }
                }
            }
            if (changed)
            {
                file.Save(f);
            }
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>By GUID first, then by DN. A record made from a group's member list has only the DN until a change to the person gives the GUID.</summary>
    private static GivenPerson? Find(GivenFile f, string? guid, string dn)
    {
        var byGuid = guid is null ? null : f.People.FirstOrDefault(p => p.Guid is { } g && Same(g, guid));
        var byDn = f.People.FirstOrDefault(p => p != byGuid && Same(p.Dn, dn) && (p.Guid is null || guid is null));
        if (byGuid is not null && byDn is { Guid: null })
        {
            byGuid.Entries.AddRange(byDn.Entries);
            f.People.Remove(byDn);
            return byGuid;
        }
        if (byGuid is null && byDn is not null && guid is not null)
        {
            byDn.Guid = guid;
        }
        return byGuid ?? byDn;
    }

    private static GivenPerson Person(GivenFile f, string? guid, string dn, string name)
    {
        if (Find(f, guid, dn) is { } p)
        {
            return p;
        }
        p = new GivenPerson { Guid = guid, Dn = dn, Name = name };
        f.People.Add(p);
        return p;
    }

    /// <summary>Whether it was added: not when they already have it from Rabbid Lab.</summary>
    private static bool Give(GivenPerson p, string kind, string key, string what, Change c)
    {
        if (p.Entries.Any(e => e.TookAt is null && e.Kind == kind && Same(e.Key, key)))
        {
            return false;
        }
        p.Entries.Add(new GivenEntry { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant(), Kind = kind, Key = key, What = what, At = c.At, By = c.By, How = c.Title });
        if (p.Entries.Count > Most && p.Entries.Where(e => e.TookAt is not null).MinBy(e => e.TookAt) is { } oldest)
        {
            p.Entries.Remove(oldest);
        }
        return true;
    }

    private static bool Take(GivenEntry e, Change c)
    {
        if (e.TookAt is not null)
        {
            return false;
        }
        (e.TookAt, e.TookBy, e.TookHow) = (c.At, c.By, c.Title);
        return true;
    }

    private static bool TakeGroups(GivenPerson p, IEnumerable<string> dns, Change c) =>
        dns.Count(dn => p.Entries.FirstOrDefault(e => e.TookAt is null && e.Kind == "group" && Same(e.Key, dn)) is { } e && Take(e, c)) > 0;
}

public static class GivenApi
{
    public static void MapGivenApi(this WebApplication app)
    {
        // The store listens to the journal from the start, not from the first time someone opens a panel.
        app.Services.GetRequiredService<GivenStore>();

        // The person's record. Reading them first keeps it inside the caller's part of Active Directory.
        app.MapGet("/api/ad/users/{username}/given", (string username, HttpContext context, IDirectoryTools tools, GivenStore given) =>
            UsersApi.Try(context, Permissions.AdView, async () =>
            {
                var user = await tools.User(username, context.RequestAborted);
                return Results.Ok(new { entries = given.For(user.Dn) });
            }, change: false));
    }
}
