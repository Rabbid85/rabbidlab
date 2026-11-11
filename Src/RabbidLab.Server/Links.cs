using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>A place in the lab: its address, what it is, the machine it's on, and its icon (a data: URL, so the page's rules allow it).</summary>
public sealed class Link
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>The IP address or host name to show, such as 192.168.2.23.</summary>
    public string? Address { get; set; }

    /// <summary>The heading it sits under, such as Network or Media; null for none.</summary>
    public string? Group { get; set; }
    public string? Icon { get; set; }

    /// <summary>Where it sits in its group once it has been dragged there; links nobody has placed follow, by name.</summary>
    public int? Place { get; set; }
    public DateTime Added { get; set; }
    public string? AddedBy { get; set; }

    /// <summary>A link to a Developer tools test address: up or down, played rather than checked, and gone at Expires.</summary>
    public string? Test { get; set; }
    public DateTime? Expires { get; set; }
}

public sealed class LinksFile
{
    public List<Link> Links { get; set; } = [];

    /// <summary>The groups in the order the page shows them. One can be empty.</summary>
    public List<string> Groups { get; set; } = [];

    /// <summary>Seconds between checks of each link.</summary>
    public int CheckEvery { get; set; } = 60;

    /// <summary>Seconds a link goes unanswered before it shows as down; 0 is the first failed check.</summary>
    public int DownAfter { get; set; } = 30;

    /// <summary>Whether Fill in writes a description for a product it recognises, such as TrueNAS or iDRAC (KnownSites).</summary>
    public bool Describe { get; set; } = true;

    /// <summary>Whether the links already here have been given those descriptions since Describe was last turned on.</summary>
    public bool Described { get; set; }
}

/// <summary>What a site says about itself, for filling in a new link.</summary>
public sealed record LinkLook(string? Name, string? Description, string? Address, string? Icon, string? Problem, string? IconFrom = null);

/// <summary>Whether a link's site answered its last check. Down only once it has gone unanswered, since Since, for the time set.</summary>
public sealed record LinkState(bool Up, bool Down, int? Ms, int? Code, DateTime? Since);

/// <summary>What the page gets: each link's state, in how many seconds to ask again, and Revision, which goes up when the links change by themselves.</summary>
public sealed record LinkStates(Dictionary<string, LinkState> States, int Next, int Revision);

/// <summary>
/// The Links page's links, kept in links.json in the data folder. Looking a site up reads its title, description and
/// icon the way a browser would, and its address from DNS. Homelab sites mostly have self-signed certificates, so these
/// reads take any certificate: they only read what the page shows anyone.
/// </summary>
public sealed class Links(DataFolder folder, TimeProvider clock)
{
    private const int MaxPage = 512 * 1024;
    private const int MaxIcon = 192 * 1024;
    private const string NoIcon = "none"; // a link shown with no icon at all; with none saved it shows its first letter
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const int ChecksAtOnce = 16;
    private static readonly TimeSpan LookFor = TimeSpan.FromSeconds(12); // a lookup in all: the page, its icon, every byte
    private static readonly HttpClient Http = MakeHttp(guarded: false); // whether the links answer
    private static readonly HttpClient Reader = MakeHttp(guarded: true); // reading a site someone typed
    private readonly JsonFile<LinksFile> file = folder.Json<LinksFile>("links.json", Json);
    private static readonly TimeSpan CheckFor = TimeSpan.FromSeconds(8); // one check of one link
    private static readonly int[] EveryChoices = [15, 30, 60, 300];
    private static readonly int[] DownChoices = [0, 15, 30, 60, 300];
    private readonly Dictionary<string, Checked> seen = []; // each link's last check, by id
    private Task? check; // the check under way, or the last one
    private Task? describing; // the links here being given descriptions of known products, or the last time they were
    private int revision; // goes up when that changes a link, so the page reads the links again

    private sealed record Checked(string Url, bool Up, int? Ms, int? Code, DateTime At, DateTime? Since);

    private Lock gate => file.Gate;

    public IReadOnlyList<Link> All()
    {
        lock (gate)
        {
            Sweep();
            return [.. file.Read().Links];
        }
    }

    private static List<string> Known(LinksFile f) => Grouping.Known(f.Groups, f.Links.Select(l => l.Group));

    // Test links past their time go. Called under the lock.
    private void Sweep()
    {
        var f = file.Read();
        var now = clock.GetUtcNow().UtcDateTime;
        if (f.Links.RemoveAll(l => l.Expires is { } end && end <= now) == 0)
        {
            return;
        }
        file.Save(f);
        revision++;
    }

    // A copy with another description: the links in the list are being read by other requests.
    private static Link WithDescription(Link l, string? description) =>
        new() { Id = l.Id, Name = l.Name, Url = l.Url, Description = description, Address = l.Address, Group = l.Group, Icon = l.Icon, Place = l.Place, Added = l.Added, AddedBy = l.AddedBy };

    public int Revision
    {
        get
        {
            lock (gate)
            {
                return revision;
            }
        }
    }

    /// <summary>Whether Fill in describes products it recognises.</summary>
    public bool Describes()
    {
        lock (gate)
        {
            return file.Read().Describe;
        }
    }

    /// <summary>Turns describing known products on or off. Off takes the descriptions it wrote off the links; typed ones stay.</summary>
    public void SetDescribe(bool on)
    {
        lock (gate)
        {
            var f = file.Read();
            if (f.Describe == on)
            {
                return;
            }
            f.Describe = on;
            f.Described = false;
            if (!on)
            {
                for (var i = 0; i < f.Links.Count; i++)
                {
                    if (KnownSites.IsOurs(f.Links[i].Description))
                    {
                        f.Links[i] = WithDescription(f.Links[i], null);
                    }
                }
                revision++;
            }
            file.Save(f);
        }
    }

    /// <summary>Gives the links here with no description one when they're a known product, once each time describing is turned on. The pass under way, or a new one.</summary>
    public Task Describing()
    {
        lock (gate)
        {
            var f = file.Read();
            if (f.Describe && !f.Described && (describing is null || describing.IsCompleted))
            {
                describing = Task.Run(DescribeAll);
            }
            return describing ?? Task.CompletedTask;
        }
    }

    // A link with no description gets the known one; so does one whose description is only the site's own (vCenter's
    // marketing line, ESXi's "ESXi Host Client"), which Fill in copied. One typed by hand stays.
    private async Task DescribeAll()
    {
        List<Link> links;
        lock (gate)
        {
            links = [.. file.Read().Links.Where(l => l.Description is null || IsWeb(l.Url))];
        }
        using var room = new SemaphoreSlim(ChecksAtOnce);
        var found = await Task.WhenAll(links.Select(async l =>
        {
            // The name or host name is usually enough; otherwise the page is read, as Fill in does.
            var named = KnownSites.Describe(l.Name, l.Url);
            if ((named is not null && l.Description is null) || !IsWeb(l.Url))
            {
                return (l.Id, l.Url, Was: l.Description, Text: named);
            }
            await room.WaitAsync();
            try
            {
                using var cut = new CancellationTokenSource(LookFor);
                var (html, _, _) = await Page(Http, new Uri(l.Url), cut.Token);
                var text = named ?? KnownSites.Describe(l.Name, l.Url, html);
                return (l.Id, l.Url, Was: l.Description, Text: l.Description is null || FromSite(l.Description, Meta(html, "description")) ? text : null);
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or UriFormatException or InvalidOperationException or NotSupportedException)
            {
                return (l.Id, l.Url, Was: l.Description, Text: l.Description is null ? named : null);
            }
            finally
            {
                room.Release();
            }
        }));
        lock (gate)
        {
            var f = file.Read();
            if (!f.Describe)
            {
                return; // turned off while the pages were read
            }
            var changed = false;
            foreach (var (id, url, was, text) in found)
            {
                var at = f.Links.FindIndex(l => l.Id == id);
                // Not one changed in the meantime.
                if (text is not null && at >= 0 && f.Links[at].Description == was && f.Links[at].Url == url)
                {
                    f.Links[at] = WithDescription(f.Links[at], text);
                    changed = true;
                }
            }
            f.Described = true;
            file.Save(f);
            if (changed)
            {
                revision++;
            }
        }
    }

    private static bool IsWeb(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    // The site's own description, maybe cut to the 200 characters a description can have.
    private static bool FromSite(string description, string? meta) =>
        meta is not null && (description == meta || (description.Length >= 100 && meta.StartsWith(description, StringComparison.Ordinal)));

    public IReadOnlyList<string> Groups()
    {
        lock (gate)
        {
            Sweep();
            return Known(file.Read());
        }
    }

    /// <summary>A new, empty group; the answer is why it can't be made.</summary>
    public string? AddGroup(string? name)
    {
        lock (gate)
        {
            var f = file.Read();
            var known = Known(f);
            if (Grouping.Add(known, name) is { } problem)
            {
                return problem;
            }
            f.Groups = known;
            file.Save(f);
            return null;
        }
    }

    /// <summary>Renames a group and takes its links with it; the answer is why it can't be.</summary>
    public string? RenameGroup(string? name, string? to)
    {
        lock (gate)
        {
            var f = file.Read();
            var known = Known(f);
            if (Grouping.Rename(known, name, to, out var old) is { } problem)
            {
                return problem;
            }
            f.Groups = known;
            Regroup(f, old, Grouping.Clean(to));
            file.Save(f);
            return null;
        }
    }

    /// <summary>Deletes a group and keeps its links, with no group. How many links that was, or null when the group isn't there.</summary>
    public int? DeleteGroup(string? name)
    {
        lock (gate)
        {
            var f = file.Read();
            var known = Known(f);
            if (!Grouping.Remove(known, name, out var old))
            {
                return null;
            }
            f.Groups = known;
            var moved = Regroup(f, old, null);
            file.Save(f);
            return moved;
        }
    }

    /// <summary>Puts the groups in this order. A group left out keeps its place after the ones named.</summary>
    public void OrderGroups(IReadOnlyList<string>? names)
    {
        lock (gate)
        {
            var f = file.Read();
            f.Groups = Grouping.Order(Known(f), names);
            file.Save(f);
        }
    }

    // A new object for each link that moves: the ones in the list are being read by other requests.
    private static int Regroup(LinksFile f, string from, string? to)
    {
        var moved = 0;
        for (var i = 0; i < f.Links.Count; i++)
        {
            var l = f.Links[i];
            if (Grouping.Same.Equals(l.Group, from))
            {
                f.Links[i] = new Link { Id = l.Id, Name = l.Name, Url = l.Url, Description = l.Description, Address = l.Address, Group = to, Icon = l.Icon, Place = l.Place, Added = l.Added, AddedBy = l.AddedBy };
                moved++;
            }
        }
        return moved;
    }

    /// <summary>The link saved (new when id is null), or why it can't be. Testing: Developer tools are on, so a test address makes a test link.</summary>
    public (Link? Link, string? Problem) Save(string? id, LinkForm form, string by, bool testing = false)
    {
        if (Clean(form.Url) is not { } url)
        {
            return (null, "That isn't an address. Links start with http:// or https:// (or another scheme, such as ssh://).");
        }
        var name = Blank(form.Name) ?? new Uri(url).Host;
        if (name.Length > 60 || (form.Description?.Length ?? 0) > 200 || (form.Group?.Length ?? 0) > 40 || (form.Address?.Length ?? 0) > 100)
        {
            return (null, "That's too long: names are 60 characters at most, groups 40, and descriptions 200.");
        }
        if (form.Icon is { Length: > 0 } icon && icon != NoIcon && (!icon.StartsWith("data:image/", StringComparison.Ordinal) || icon.Length > MaxIcon * 4 / 3 + 64))
        {
            return (null, "The icon has to be a picture of 192 KB at most.");
        }
        lock (gate)
        {
            var f = file.Read();
            var at = id is null ? -1 : f.Links.FindIndex(l => l.Id == id);
            if (id is not null && at < 0)
            {
                return (null, "That link isn't here any more.");
            }
            if (at < 0 && f.Links.Count >= 500)
            {
                return (null, "That's 500 links, which is as many as Rabbid Lab keeps.");
            }
            var old = at < 0 ? null : f.Links[at];
            // A group typed for the first time is made, at the end. One that's there keeps its own spelling.
            var known = Known(f);
            var (group, full) = Grouping.Join(known, form.Group);
            if (full is not null)
            {
                return (null, full);
            }
            f.Groups = known;
            // A new object each time: the ones in the list are being read by other requests.
            var link = new Link
            {
                Id = old?.Id ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Added = old?.Added ?? clock.GetUtcNow().UtcDateTime,
                AddedBy = old is null ? by : old.AddedBy,
                Name = name,
                Url = url,
                Description = Blank(form.Description),
                Address = Blank(form.Address),
                Group = group,
                Icon = Blank(form.Icon),
                Place = form.Place ?? old?.Place,
            };
            if (testing && DevTools.IsTest(url))
            {
                link.Test = DevTools.IsDown(url) ? "down" : "up";
                link.Expires = clock.GetUtcNow().UtcDateTime + DevTools.Lasts;
            }
            if (at < 0)
            {
                f.Links.Add(link);
            }
            else
            {
                f.Links[at] = link;
            }
            file.Save(f);
            return (link, null);
        }
    }

    public Link? Delete(string id)
    {
        lock (gate)
        {
            var f = file.Read();
            var link = f.Links.FirstOrDefault(l => l.Id == id);
            if (link is not null)
            {
                f.Links.Remove(link);
                file.Save(f);
                seen.Remove(id);
            }
            return link;
        }
    }

    /// <summary>Everything Links keeps, as one file to save: the links and groups in their order, and the check times.</summary>
    public LinksExport Export()
    {
        lock (gate)
        {
            var f = file.Read();
            return new LinksExport(LinksExport.Is, 1, clock.GetUtcNow().UtcDateTime, new LinkChecksForm(Every(f), After(f)), Known(f),
                [.. f.Links.Select(l => new LinkForm(l.Name, l.Url, l.Description, l.Address, l.Group, l.Icon, l.Place))]);
        }
    }

    /// <summary>
    /// Brings an exported file in beside what's here: its groups that aren't here, its links whose address isn't here
    /// already, and its check times. Null when the file isn't a Links export.
    /// </summary>
    public LinksImported? Import(LinksExport data, string by)
    {
        if (data.Kind != LinksExport.Is || (data.Links is null && data.Groups is null))
        {
            return null;
        }
        var groups = Groups().Count;
        foreach (var g in data.Groups ?? [])
        {
            AddGroup(g); // one that's here already stays as it is
        }
        var have = All().Select(l => l.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0, skipped = 0, refused = 0;
        foreach (var l in data.Links ?? [])
        {
            if (Clean(l.Url) is { } url && !have.Add(url))
            {
                skipped++;
            }
            else if (Save(null, l, by).Link is null)
            {
                refused++;
            }
            else
            {
                added++;
            }
        }
        var checks = data.Checks is { } c && SetChecks(c.Every, c.DownAfter) is null;
        return new LinksImported(added, Groups().Count - groups, skipped, refused, checks);
    }

    /// <summary>
    /// The site's name, description, address and icon. Guarded (always, but in tests): not from this server or a
    /// link-local address. With describe, a product it recognises gets its description from KnownSites.
    /// </summary>
    public static async Task<LinkLook> Look(string? address, CancellationToken stop, bool guarded = true, bool describe = false)
    {
        if (Clean(address) is not { } url)
        {
            return new LinkLook(null, null, null, null, "That isn't an address.");
        }
        var uri = new Uri(url);
        using var cut = CancellationTokenSource.CreateLinkedTokenSource(stop);
        cut.CancelAfter(LookFor);
        var ip = await Ip(uri.Host, cut.Token);
        if (uri.Scheme is not ("http" or "https"))
        {
            return new LinkLook(uri.Host, describe ? KnownSites.Describe(uri.Host, url) : null, ip, null, null);
        }
        try
        {
            var http = guarded ? Reader : Http;
            var (html, where, ok) = await Page(http, uri, cut.Token);
            var name = ok ? Named(html) : null; // an error page's title ("Not Found") is no name
            var about = ok ? Meta(html, "description") : null;
            var (icon, from) = await Icon(http, html, where, cut.Token);
            var pages = html;
            // A front page with no name or icon of its own is a way in to the real one (vCenter's is): the page behind its first link is read.
            if (name is null && icon is null && FirstLink(html, where) is { } onward)
            {
                (html, where, ok) = await Page(http, onward, cut.Token);
                name = ok ? Named(html) : null;
                about ??= ok ? Meta(html, "description") : null;
                pages += html;
                (icon, from) = await Icon(http, html, where, cut.Token);
            }
            // A page whose name a script fills in (ESXi, iDRAC, TrueNAS) goes by the machine's name in DNS.
            name ??= await Machine(ip, cut.Token);
            // A known product's own description beats the site's, which is often marketing (vCenter's) or missing.
            return new LinkLook(name, (describe ? KnownSites.Describe(name, url, pages) : null) ?? about, ip, icon, null, from);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            var called = await Machine(ip, stop) ?? uri.Host;
            return new LinkLook(called, describe ? KnownSites.Describe(called, url) : null, ip, null, IsRefusal(e)
                ? $"{uri.Host} is on this server or a link-local address, which {Product.Name} doesn't read, so fill it in yourself."
                : $"{uri.Host} didn't answer, so fill it in yourself.");
        }
    }

    /// <summary>Whether a lookup must not read from this address: this server itself, or a link-local one.</summary>
    public static bool Refused(IPAddress address)
    {
        var ip = Addresses.Normal(address);
        return ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal
            || (ip.AddressFamily == AddressFamily.InterNetwork && ip.GetAddressBytes() is [169, 254, ..]) || Addresses.OnServer(ip);
    }

    private static int Every(LinksFile f) => Math.Clamp(f.CheckEvery, EveryChoices[0], EveryChoices[^1]);

    private static int After(LinksFile f) => Math.Clamp(f.DownAfter, 0, DownChoices[^1]);

    // A link that didn't answer is asked again sooner than the others, until it answers or counts as down.
    private static int Retry(LinksFile f) => Math.Min(Every(f), Math.Clamp(After(f) / 3, 5, 30));

    /// <summary>How often each link is checked, and how long one goes unanswered before it shows as down, in seconds.</summary>
    public (int Every, int DownAfter) Checks()
    {
        lock (gate)
        {
            var f = file.Read();
            return (Every(f), After(f));
        }
    }

    /// <summary>Puts the links given in a group (null for none), in the order given; the answer is why it can't be.</summary>
    public string? Arrange(string? group, IReadOnlyList<string>? ids)
    {
        lock (gate)
        {
            var f = file.Read();
            var known = Known(f);
            var (name, problem) = Grouping.Join(known, group);
            if (problem is not null)
            {
                return problem;
            }
            f.Groups = known;
            var place = 0;
            foreach (var id in ids ?? [])
            {
                var at = f.Links.FindIndex(l => l.Id == id);
                if (at >= 0)
                {
                    var l = f.Links[at];
                    f.Links[at] = new Link { Id = l.Id, Name = l.Name, Url = l.Url, Description = l.Description, Address = l.Address, Group = name, Icon = l.Icon, Place = place++, Added = l.Added, AddedBy = l.AddedBy };
                }
            }
            file.Save(f);
            return null;
        }
    }

    /// <summary>Sets either or both; the answer is why it can't be.</summary>
    public string? SetChecks(int? every, int? downAfter)
    {
        if ((every is { } e && !EveryChoices.Contains(e)) || (downAfter is { } d && !DownChoices.Contains(d)) || (every is null && downAfter is null))
        {
            return "Pick one of the times offered.";
        }
        lock (gate)
        {
            var f = file.Read();
            f.CheckEvery = every ?? Every(f);
            f.DownAfter = downAfter ?? After(f);
            file.Save(f);
            return null;
        }
    }

    /// <summary>
    /// Whether each http(s) link answers (any answer counts: a sign-in page is up). Links are checked when someone asks and
    /// their last check is older than the time set; one that didn't answer shows as down once that has lasted long enough.
    /// </summary>
    public async Task<LinkStates> States(CancellationToken stop)
    {
        _ = Describing(); // once after describing is turned on, in the background; the page reads the links again when it changes one
        Task running;
        lock (gate)
        {
            Sweep();
            // One check for everyone asking: the one under way, or a new one of the links that are due.
            if (check is null || check.IsCompleted)
            {
                var due = Due(file.Read(), clock.GetUtcNow().UtcDateTime);
                check = due.Count > 0 ? Task.Run(() => Check(due)) : Task.CompletedTask;
            }
            running = check;
        }
        await running.WaitAsync(stop);
        lock (gate)
        {
            var f = file.Read();
            var now = clock.GetUtcNow().UtcDateTime;
            var after = TimeSpan.FromSeconds(After(f));
            var states = new Dictionary<string, LinkState>();
            foreach (var l in f.Links)
            {
                if (l.Test is { } test)
                {
                    states[l.Id] = test == "down" ? new LinkState(false, true, null, null, l.Added) : new LinkState(true, false, 12, 200, null);
                    continue;
                }
                if (seen.TryGetValue(l.Id, out var c) && c.Url == l.Url)
                {
                    states[l.Id] = new LinkState(c.Up, !c.Up && now - (c.Since ?? now) >= after, c.Ms, c.Code, c.Since);
                }
            }
            var waiting = states.Values.Any(s => !s.Up && !s.Down);
            return new LinkStates(states, waiting ? Retry(f) : Math.Min(Every(f), 60), revision);
        }
    }

    private List<Link> Due(LinksFile f, DateTime now)
    {
        var every = TimeSpan.FromSeconds(Every(f));
        var retry = TimeSpan.FromSeconds(Retry(f));
        var after = TimeSpan.FromSeconds(After(f));
        return [.. f.Links.Where(l =>
        {
            if (!l.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !l.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || l.Test is not null)
            {
                return false;
            }
            if (!seen.TryGetValue(l.Id, out var c) || c.Url != l.Url)
            {
                return true;
            }
            var waiting = !c.Up && now - (c.Since ?? now) < after;
            return now - c.At >= (waiting ? retry : every);
        })];
    }

    private async Task Check(List<Link> links)
    {
        using var room = new SemaphoreSlim(ChecksAtOnce);
        var answers = await Task.WhenAll(links.Select(async l =>
        {
            await room.WaitAsync();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var cut = new CancellationTokenSource(CheckFor);
                using var answer = await Http.GetAsync(l.Url, HttpCompletionOption.ResponseHeadersRead, cut.Token);
                return (Link: l, Up: true, Ms: (int?) timer.ElapsedMilliseconds, Code: (int?) answer.StatusCode);
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or UriFormatException or InvalidOperationException or NotSupportedException)
            {
                return (Link: l, Up: false, Ms: (int?) null, Code: (int?) null);
            }
            finally
            {
                room.Release();
            }
        }));
        lock (gate)
        {
            var f = file.Read();
            var now = clock.GetUtcNow().UtcDateTime;
            // Unanswered checks only count as one stretch when nothing longer than this lies between them.
            var gap = TimeSpan.FromSeconds(Every(f) + 60);
            foreach (var a in answers)
            {
                var l = a.Link;
                var since = a.Up ? (DateTime?) null
                    : seen.TryGetValue(l.Id, out var old) && old.Url == l.Url && !old.Up && now - old.At <= gap ? old.Since : now;
                seen[l.Id] = new Checked(l.Url, a.Up, a.Ms, a.Code, now, since);
            }
            var kept = f.Links.Select(l => l.Id).ToHashSet();
            foreach (var id in seen.Keys.Where(id => !kept.Contains(id)).ToList())
            {
                seen.Remove(id);
            }
        }
    }

    // --- Reading a site ------------------------------------------------------------------------------------------

    /// <summary>A connection to an address a lookup doesn't read was asked for.</summary>
    private sealed class RefusedSite(string host) : IOException($"{host} is on this server or a link-local address");

    private static bool IsRefusal(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
        {
            if (e is RefusedSite)
            {
                return true;
            }
        }
        return false;
    }

    private static HttpClient MakeHttp(bool guarded)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All, // an iDRAC only has its pages gzipped, and answers 404 to a reader that doesn't ask for that
            ConnectTimeout = TimeSpan.FromSeconds(4),
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
        };
        if (guarded)
        {
            // Every connection is checked, so a redirect or a DNS name can't lead back to this server.
            handler.ConnectCallback = async (context, stop) =>
            {
                var host = context.DnsEndPoint.Host;
                var found = await Dns.GetHostAddressesAsync(host, stop);
                var allowed = found.Where(a => !Refused(a)).ToArray();
                if (allowed.Length == 0)
                {
                    throw found.Length > 0 ? new RefusedSite(host) : new SocketException((int) SocketError.HostNotFound);
                }
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, stop);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        }
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"RabbidLab/{Product.Version}");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/html,image/*;q=0.9,*/*;q=0.8");
        return http;
    }

    /// <summary>The body, up to the limit; null when it's bigger.</summary>
    private static async Task<byte[]?> Read(HttpResponseMessage answer, int limit, CancellationToken stop)
    {
        await using var stream = await answer.Content.ReadAsStreamAsync(stop);
        using var into = new MemoryStream();
        var buffer = new byte[16384];
        int n;
        while ((n = await stream.ReadAsync(buffer, stop)) > 0)
        {
            if (into.Length + n > limit)
            {
                return into.Length > 0 && limit == MaxPage ? into.ToArray() : null; // a page is read as far as the limit
            }
            into.Write(buffer, 0, n);
        }
        return into.ToArray();
    }

    /// <summary>A page's HTML, where it ended up, and whether the site was happy to give it. One that only sends the browser on (ESXi's front page, to /ui) is followed, twice at most.</summary>
    private static async Task<(string Html, Uri Where, bool Ok)> Page(HttpClient http, Uri at, CancellationToken stop)
    {
        for (var hop = 0; ; hop++)
        {
            using var answer = await http.GetAsync(at, HttpCompletionOption.ResponseHeadersRead, stop);
            var html = await Read(answer, MaxPage, stop) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : "";
            var where = answer.RequestMessage?.RequestUri ?? at;
            if (hop < 2 && Refresh(html, where) is { } next)
            {
                at = next;
                continue;
            }
            return (html, where, answer.IsSuccessStatusCode);
        }
    }

    // Where a meta refresh sends the browser: <meta http-equiv="refresh" content="0;URL='/ui'">.
    private static Uri? Refresh(string html, Uri page)
    {
        foreach (Match m in Regex.Matches(html, @"<meta\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (string.Equals(Attr(m.Value, "http-equiv"), "refresh", StringComparison.OrdinalIgnoreCase)
                && Regex.Match(Attr(m.Value, "content") ?? "", @"url\s*=\s*['""]?([^'"";>\s]+)", RegexOptions.IgnoreCase) is { Success: true } to
                && Uri.TryCreate(page, to.Groups[1].Value, out var next) && next.Scheme is "http" or "https" && next != page)
            {
                return next;
            }
        }
        return null;
    }

    // The first link on the page to another page of the same site.
    private static Uri? FirstLink(string html, Uri page)
    {
        foreach (Match m in Regex.Matches(html, @"<a\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (Attr(m.Value, "href") is { Length: > 0 } href && !href.StartsWith('#') && Uri.TryCreate(page, href, out var next) && next.Scheme is "http" or "https"
                && next.Host == page.Host && next.Port == page.Port && next.AbsolutePath != page.AbsolutePath)
            {
                return next;
            }
        }
        return null;
    }

    private static string? Named(string html) => Meta(html, "application-name") ?? Meta(html, "og:site_name") ?? Title(html);

    /// <summary>The machine's own name from reverse DNS, without its domain: esxi01 for 192.168.2.21. Null when DNS has none, and for this computer.</summary>
    private static async Task<string?> Machine(string? ip, CancellationToken stop)
    {
        if (!IPAddress.TryParse(ip, out var address) || IPAddress.IsLoopback(address))
        {
            return null;
        }
        try
        {
            using var cut = CancellationTokenSource.CreateLinkedTokenSource(stop);
            cut.CancelAfter(TimeSpan.FromSeconds(2));
            var name = (await Dns.GetHostEntryAsync(address.ToString(), cut.Token)).HostName;
            return IPAddress.TryParse(name, out _) || name.Length == 0 ? null : name.Split('.')[0];
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or ArgumentException)
        {
            return null;
        }
    }

    // A title a script fills in (vCenter's is " + ID_VC_Welcome + ") is no name.
    private static string? Title(string html) =>
        Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline) is { Success: true } m
        && Words(m.Groups[1].Value) is { } title && !Regex.IsMatch(title, @"[""']\s*\+|\+\s*[""']|\{\{|\$\{|<%") ? title : null;

    private static string? Meta(string html, string name)
    {
        foreach (Match m in Regex.Matches(html, @"<meta\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = m.Value;
            if (string.Equals(Attr(tag, "name") ?? Attr(tag, "property"), name, StringComparison.OrdinalIgnoreCase) && Attr(tag, "content") is { } content)
            {
                return Words(content);
            }
        }
        return null;
    }

    private static string? Attr(string tag, string name) =>
        Regex.Match(tag, $@"\b{name}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase) is { Success: true } m
            ? WebUtility.HtmlDecode(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value) : null;

    private static string? Words(string text)
    {
        var s = Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
        return s.Length == 0 ? null : s.Length > 200 ? s[..200] : s;
    }

    /// <summary>The site's best icon as a data: URL: apple-touch-icon, else the biggest icon it lists, else /favicon.ico.</summary>
    private static async Task<(string? Icon, string? From)> Icon(HttpClient http, string html, Uri page, CancellationToken stop)
    {
        var listed = Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase).Select(m => m.Value)
            .Select(tag => (Rel: Attr(tag, "rel")?.ToLowerInvariant() ?? "", Href: Attr(tag, "href"), Sizes: Attr(tag, "sizes"), Type: Attr(tag, "type")))
            .Where(l => l.Href is { Length: > 0 } && (l.Rel.Split(' ').Contains("icon") || l.Rel.Contains("apple-touch-icon")))
            .OrderByDescending(l => l.Rel.Contains("apple-touch-icon"))
            .ThenByDescending(l => l.Type == "image/svg+xml" || (l.Href ?? "").EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(l => int.TryParse(l.Sizes?.Split('x')[0], out var s) ? s : 0)
            .Select(l => l.Href!).ToList();
        listed.Add("/favicon.ico");
        foreach (var href in listed.Distinct().Take(4))
        {
            if (href.StartsWith("data:image/", StringComparison.Ordinal))
            {
                return href.Length <= MaxIcon ? (href, "the page itself") : (null, null);
            }
            if (!Uri.TryCreate(page, href, out var at) || at.Scheme is not ("http" or "https"))
            {
                continue;
            }
            try
            {
                using var answer = await http.GetAsync(at, HttpCompletionOption.ResponseHeadersRead, stop);
                if (!answer.IsSuccessStatusCode || await Read(answer, MaxIcon, stop) is not { Length: > 0 } bytes)
                {
                    continue;
                }
                var type = Kind(bytes, answer.Content.Headers.ContentType?.MediaType);
                if (type is not null)
                {
                    return ($"data:{type};base64,{Convert.ToBase64String(bytes)}", at.AbsolutePath);
                }
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
            {
                // The next one; out of time, the site goes without an icon.
            }
        }
        return (null, null);
    }

    /// <summary>What kind of picture the bytes are, by their first bytes (servers often say the wrong type); null when they aren't one.</summary>
    private static string? Kind(byte[] b, string? said)
    {
        if (b.Length > 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
        {
            return "image/png";
        }
        if (b.Length > 4 && b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0)
        {
            // Only one a browser can draw: it lists a picture, and that picture is all there.
            return b.Length >= 22 && (b[4] | b[5] << 8) > 0 && BitConverter.ToUInt32(b, 14) > 0
                && (ulong) BitConverter.ToUInt32(b, 18) + BitConverter.ToUInt32(b, 14) <= (ulong) b.Length ? "image/x-icon" : null;
        }
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8)
        {
            return "image/jpeg";
        }
        if (b.Length > 4 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F')
        {
            return "image/gif";
        }
        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
        {
            return "image/webp";
        }
        // An SVG has an svg element near its start, whatever the server calls it.
        var start = System.Text.Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, 4096)).TrimStart();
        return start.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || (start.StartsWith('<') && start.Contains("<svg", StringComparison.OrdinalIgnoreCase)
            && (said == "image/svg+xml" || start.StartsWith("<?xml", StringComparison.Ordinal))) ? "image/svg+xml" : null;
    }

    /// <summary>The host's IPv4 address (or IPv6 when that's all it has); the host itself when it's already an address.</summary>
    private static async Task<string?> Ip(string host, CancellationToken stop)
    {
        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip))
        {
            return ip.ToString();
        }
        try
        {
            using var cut = CancellationTokenSource.CreateLinkedTokenSource(stop);
            cut.CancelAfter(TimeSpan.FromSeconds(3));
            var found = await Dns.GetHostAddressesAsync(host, cut.Token);
            return (found.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? found.FirstOrDefault())?.ToString();
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The address as a URL, with http:// added when it has no scheme; null when it can't be one.</summary>
    public static string? Clean(string? address)
    {
        var a = address?.Trim() ?? "";
        if (a.Length == 0 || a.Length > 2000)
        {
            return null;
        }
        if (!Regex.IsMatch(a, "^[a-zA-Z][a-zA-Z0-9+.-]*://"))
        {
            a = "http://" + a;
        }
        return Uri.TryCreate(a, UriKind.Absolute, out var u) && u.Host.Length > 0 && u.Scheme is not ("javascript" or "data" or "file" or "vbscript") ? u.ToString() : null;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed record LinkForm(string? Name, string? Url, string? Description, string? Address, string? Group, string? Icon, int? Place = null);

public sealed record LinkArrangeForm(string? Group, List<string>? Ids);

public sealed record LinkChecksForm(int? Every, int? DownAfter, bool? Describe = null);

/// <summary>Links as a file: the links (no ids, so they are new wherever they land), the groups in order, and the check times.</summary>
public sealed record LinksExport(string? Kind, int? Version, DateTime? Exported, LinkChecksForm? Checks, List<string>? Groups, List<LinkForm>? Links)
{
    public const string Is = "rabbidlab-links";
}

/// <summary>What an import did: links and groups added, links left out as already here, links turned away, and whether the check times were set.</summary>
public sealed record LinksImported(int Links, int Groups, int Skipped, int Refused, bool Checks);


public static class LinksApi
{
    private static readonly JsonSerializerOptions FileJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void MapLinksApi(this WebApplication app)
    {
        app.MapGet("/api/links", (HttpContext context, Links links) =>
            !Access.Can(context, Permissions.LinksView) ? Access.NotAllowed()
            : Results.Ok(Listed(links)));

        app.MapPost("/api/links/groups", (GroupForm form, HttpContext context, Links links, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.LinksEdit) is { } refused)
            {
                return refused;
            }
            if (links.AddGroup(form.Name) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "links", "link.group.add", $"Made the group {form.Name!.Trim()} on Links");
            return Results.Ok(Listed(links));
        });

        app.MapPost("/api/links/groups/rename", (GroupForm form, HttpContext context, Links links, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.LinksEdit) is { } refused)
            {
                return refused;
            }
            if (links.RenameGroup(form.Name, form.To) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "links", "link.group.rename", $"Renamed the group {form.Name!.Trim()} to {form.To!.Trim()} on Links");
            return Results.Ok(Listed(links));
        });

        app.MapPost("/api/links/groups/delete", (GroupForm form, HttpContext context, Links links, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.LinksEdit) is { } refused)
            {
                return refused;
            }
            if (links.DeleteGroup(form.Name) is not { } moved)
            {
                return Results.NotFound(new { error = "That group isn't here any more." });
            }
            log.Record(context, "links", "link.group.delete", $"Deleted the group {form.Name!.Trim()} from Links" + (moved > 0 ? $", which had {moved} link{(moved == 1 ? "" : "s")}" : ""));
            return Results.Ok(Listed(links));
        });

        app.MapPost("/api/links/groups/order", (GroupForm form, HttpContext context, Links links, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.LinksEdit) is { } refused)
            {
                return refused;
            }
            links.OrderGroups(form.Names);
            log.Record(context, "links", "link.group.order", "Reordered the groups on Links");
            return Results.Ok(Listed(links));
        });

        app.MapGet("/api/links/states", async (HttpContext context, Links links) =>
            !Access.Can(context, Permissions.LinksView) ? Access.NotAllowed() : Results.Ok(await links.States(context.RequestAborted)));

        app.MapPost("/api/links/checks", async (LinkChecksForm form, HttpContext context, Links links, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.LinksEdit) is { } refused)
            {
                return refused;
            }
            // Describing known products: turned on, the links here get theirs before the answer; off, they lose them.
            if (form.Describe is { } describe)
            {
                links.SetDescribe(describe);
                log.Record(context, "links", "link.describe", $"Turned {(describe ? "on" : "off")} auto-filled descriptions on Links");
                if (describe)
                {
                    await links.Describing().WaitAsync(context.RequestAborted);
                }
                if (form.Every is null && form.DownAfter is null)
                {
                    return Results.Ok(Listed(links));
                }
            }
            if (links.SetChecks(form.Every, form.DownAfter) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            var (every, after) = links.Checks();
            log.Record(context, "links", "link.checks", $"Set Links to check every {Span(every)} and show a link as down {(after == 0 ? "the first time it doesn't answer" : $"after {Span(after)} with no answer")}");
            return Results.Ok(Listed(links));
        });

        // A link dragged to another place: the group it lands in, and that group's links in their new order.
        app.MapPost("/api/links/arrange", (LinkArrangeForm form, HttpContext context, Links links, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.LinksEdit) is { } refused)
            {
                return refused;
            }
            if (links.Arrange(form.Group, form.Ids) is { } problem)
            {
                return Results.BadRequest(new { error = problem });
            }
            log.Record(context, "links", "link.arrange", string.IsNullOrWhiteSpace(form.Group) ? "Arranged the links with no group on Links" : $"Arranged the links in {form.Group.Trim()} on Links");
            return Results.Ok(Listed(links));
        });

        // Everything on Links as a file to keep, and a file like it brought in beside what's here.
        app.MapGet("/api/links/export", (HttpContext context, Links links, ActivityLog log, TimeProvider clock) =>
        {
            if (!Access.Can(context, Permissions.LinksEdit))
            {
                return Access.NotAllowed();
            }
            log.Record(context, "links", "link.export", "Exported Links to a file");
            return Results.File(JsonSerializer.SerializeToUtf8Bytes(links.Export(), FileJson), "application/json", $"rabbidlab-links-{clock.GetLocalNow():yyyy-MM-dd}.json");
        });

        app.MapPost("/api/links/import", (LinksExport file, HttpContext context, Links links, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.LinksEdit) is { } refused)
            {
                return refused;
            }
            if (links.Import(file, ActivityLog.Who(context)) is not { } done)
            {
                return Results.BadRequest(new { error = "That file isn't a Links export from Rabbid Lab." });
            }
            log.Record(context, "links", "link.import", $"Imported {done.Links} link{(done.Links == 1 ? "" : "s")} and {done.Groups} group{(done.Groups == 1 ? "" : "s")} to Links from a file");
            return Results.Ok(new { done.Links, done.Groups, done.Skipped, done.Refused, done.Checks, now = Listed(links) });
        });

        // A new link's name, description, address and icon, read from the site.
        app.MapPost("/api/links/look", async (LinkForm form, HttpContext context, Links links, SettingsStore settings) =>
            Access.RefuseUnless(context, Permissions.LinksEdit) is { } refused ? refused
            : Results.Ok(DevTools.On(settings) && DevTools.IsTest(form.Url) ? await DevTools.Look(form.Url!, context.RequestAborted)
                : await Links.Look(form.Url, context.RequestAborted, describe: links.Describes())));

        app.MapPost("/api/links", (LinkForm form, HttpContext context, Links links, ActivityLog log, SettingsStore settings) => Save(null, form, context, links, log, settings));

        app.MapPost("/api/links/{id}", (string id, LinkForm form, HttpContext context, Links links, ActivityLog log, SettingsStore settings) => Save(id, form, context, links, log, settings));

        app.MapPost("/api/links/{id}/delete", (string id, HttpContext context, Links links, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.LinksEdit) is { } refused)
            {
                return refused;
            }
            if (links.Delete(id) is not { } gone)
            {
                return Results.NotFound(new { error = "That link isn't here any more." });
            }
            log.Record(context, "links", "link.delete", $"Took {gone.Name} off Links");
            return Results.Ok();
        });
    }

    // The groups in their order, and the links in it too: by group, then by name, the ones with no group last.
    internal static object Listed(Links links)
    {
        var groups = links.Groups();
        var (every, downAfter) = links.Checks();
        var place = groups.Select((g, i) => (g, i)).ToDictionary(p => p.g, p => p.i, StringComparer.OrdinalIgnoreCase);
        return new
        {
            links = links.All().OrderBy(l => l.Group is { } g && place.TryGetValue(g, out var at) ? at : int.MaxValue).ThenBy(l => l.Place ?? int.MaxValue).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase),
            groups,
            checks = new { every, downAfter, describe = links.Describes() },
            revision = links.Revision,
        };
    }

    private static string Span(int seconds) => seconds < 60 ? $"{seconds} seconds" : seconds == 60 ? "minute" : $"{seconds / 60} minutes";

    private static IResult Save(string? id, LinkForm form, HttpContext context, Links links, ActivityLog log, SettingsStore settings)
    {
        if (Access.RefuseUnless(context, Permissions.LinksEdit) is { } refused)
        {
            return refused;
        }
        var (link, problem) = links.Save(id, form, ActivityLog.Who(context), DevTools.On(settings));
        if (link is null)
        {
            return Results.BadRequest(new { error = problem });
        }
        log.Record(context, "links", id is null ? "link.add" : "link.change", id is null ? $"Put {link.Name} on Links" : $"Changed {link.Name} on Links");
        return Results.Ok(link);
    }
}
