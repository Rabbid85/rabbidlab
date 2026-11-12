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

    /// <summary>Where the icon came from: a path on the site (or DuckDuckGo), or "own" for one someone uploaded. Null for one saved before this was kept.</summary>
    public string? IconFrom { get; set; }

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
public sealed record LinkLook(string? Name, string? Description, string? Address, string? Icon, string? Problem, string? IconFrom = null, bool Served = false);

/// <summary>Whether a link's site answered its last check, made At. Down only once it has gone unanswered, since Since, for the time set.</summary>
public sealed record LinkState(bool Up, bool Down, int? Ms, int? Code, DateTime? Since, DateTime? At, string? Why = null);

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
    private const int MaxIconFile = 512 * 1024; // read this far, so a big .ico can be cut down to one picture
    private static readonly Regex IconLinkTag = new(@"<link\b[^>]*\brel\s*=\s*[""']?[^""'>]*icon", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); // a page's head lists an icon
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

    private sealed record Checked(string Url, bool Up, int? Ms, int? Code, DateTime At, DateTime? Since, string? Why = null);

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
        new() { Id = l.Id, Name = l.Name, Url = l.Url, Description = description, Address = l.Address, Group = l.Group, Icon = l.Icon, IconFrom = l.IconFrom, Place = l.Place, Added = l.Added, AddedBy = l.AddedBy };

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
                var lan = await Ip(new Uri(l.Url).Host, cut.Token) is { } ip && OnLan(ip); // an internet site's page names all sorts of products
                var text = named ?? KnownSites.Describe(l.Name, l.Url, lan ? html : null);
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
                f.Links[i] = new Link { Id = l.Id, Name = l.Name, Url = l.Url, Description = l.Description, Address = l.Address, Group = to, Icon = l.Icon, IconFrom = l.IconFrom, Place = l.Place, Added = l.Added, AddedBy = l.AddedBy };
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
                // The same icon keeps where it came from; a new one says, or is someone's own when it doesn't.
                IconFrom = Blank(form.Icon) is not { } kept ? null : kept == old?.Icon ? old.IconFrom : Blank(form.IconFrom) ?? "own",
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
    /// Reads a link's site again, the way Fill in does, and fills in what it's missing: the icon where it shows its letter,
    /// a description where it has none or only Rabbid Lab's own, and its IP when that's on the lab's network. An icon it has stays (it may be one uploaded),
    /// and so do No icon, a typed description and the name. Null when the link is gone.
    /// </summary>
    public async Task<LinkRefreshed?> Refresh(string id, CancellationToken stop, bool guarded = true)
    {
        Link? was;
        bool describe;
        lock (gate)
        {
            var f = file.Read();
            was = f.Links.FirstOrDefault(l => l.Id == id);
            describe = f.Describe;
        }
        if (was is null)
        {
            return null;
        }
        if (was.Test is not null)
        {
            return new LinkRefreshed(was, [], null);
        }
        // An icon saved before Rabbid Lab kept where icons came from is checked against the ones the site serves, once.
        var unsure = was.Icon is { } saved && saved != NoIcon && was.IconFrom is null ? saved : null;
        var look = await Look(was.Url, stop, guarded, describe, served: unsure);
        lock (gate)
        {
            var f = file.Read();
            var at = f.Links.FindIndex(l => l.Id == id);
            if (at < 0)
            {
                return null;
            }
            var now = f.Links[at];
            List<string> changed = [];
            var icon = now.Icon;
            var from = now.IconFrom;
            if (now.Icon is null && look.Icon is { } found)
            {
                icon = found;
                from = look.IconFrom;
                changed.Add("icon");
            }
            else if (unsure is not null && now.Icon == unsure && look.Problem is null)
            {
                // One of the site's own (Plex's phone icon, from when that came first) gives way to the one its browser tab shows;
                // any other is someone's own, and stays.
                if (look.Served && look.Icon is { } tab && tab != now.Icon)
                {
                    icon = tab;
                    changed.Add("icon");
                }
                from = look.Served ? look.IconFrom : "own";
            }
            var description = now.Description;
            if (now.Description is null || KnownSites.IsOurs(now.Description))
            {
                // Its own name first, as the dialog would; one of ours that no longer fits (an old "UniFi" on Hulu) goes once the site has answered.
                var text = (describe ? KnownSites.Describe(now.Name, now.Url) : null) ?? Blank(look.Description);
                var next = text is not null ? (text.Length > 200 ? text[..200] : text) : look.Problem is null ? null : now.Description;
                if (next != now.Description)
                {
                    description = next;
                    changed.Add("description");
                }
            }
            var address = now.Address;
            if (now.Address is null && Blank(look.Address) is { } ip && OnLan(ip))
            {
                address = ip;
                changed.Add("IP");
            }
            // Not a link that was changed while its site was read; nothing to save when nothing changed, not even where its icon came from.
            if ((changed.Count == 0 && from == now.IconFrom) || now.Url != was.Url)
            {
                return new LinkRefreshed(now, [], changed.Count == 0 ? look.Problem : null);
            }
            var link = new Link
            {
                Id = now.Id, Added = now.Added, AddedBy = now.AddedBy, Name = now.Name, Url = now.Url, Group = now.Group, Place = now.Place,
                Icon = icon, IconFrom = from, Description = description, Address = address,
            };
            f.Links[at] = link;
            file.Save(f);
            revision++;
            return new LinkRefreshed(link, changed, null);
        }
    }

    /// <summary>
    /// The site's name, description, address and icon. Guarded (always, but in tests): not from this server or a
    /// link-local address. With describe, a product it recognises gets its description from KnownSites.
    /// </summary>
    public static async Task<LinkLook> Look(string? address, CancellationToken stop, bool guarded = true, bool describe = false, string? served = null)
    {
        if (Clean(address) is not { } url)
        {
            return new LinkLook(null, null, null, null, "That isn't an address.");
        }
        var uri = new Uri(url);
        using var cut = CancellationTokenSource.CreateLinkedTokenSource(stop);
        cut.CancelAfter(LookFor);
        var ip = await Ip(uri.Host, cut.Token);
        var lan = ip is not null && (OnLan(ip) || (IPAddress.TryParse(ip, out var self) && IPAddress.IsLoopback(self))); // the lab, or this computer
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
            var (icon, from, saw) = await Icon(http, html, where, cut.Token, same: served);
            var pages = html;
            // A front page with no name or icon of its own is a way in to the real one (vCenter's is): the page behind its first link is read.
            if (name is null && icon is null && FirstLink(html, where) is { } onward)
            {
                (html, where, ok) = await Page(http, onward, cut.Token);
                name = ok ? Named(html) : null;
                about ??= ok ? Meta(html, "description") : null;
                pages += html;
                (icon, from, saw) = await Icon(http, html, where, cut.Token, same: served);
            }
            // An internet site that turns readers away (behind a bot check) still has an icon at DuckDuckGo. Never one on the lab's network.
            if (icon is null && ip is not null && Public(ip) && Uri.CheckHostName(uri.Host) == UriHostNameType.Dns)
            {
                (icon, from, _) = await Icon(http, "", new Uri($"https://icons.duckduckgo.com/ip3/{uri.Host}.ico"), cut.Token, only: true);
                saw |= served is not null && icon == served;
            }
            // A page whose name a script fills in (ESXi, iDRAC, TrueNAS) goes by the machine's name in DNS, on the lab's network.
            name ??= lan ? await Machine(ip, cut.Token) : null;
            // A known product's own description beats the site's, which is often marketing (vCenter's) or missing.
            return new LinkLook(name, (describe ? KnownSites.Describe(name, url, lan ? pages : null) : null) ?? about, ip, icon, null, from, saw);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            var called = (lan ? await Machine(ip, stop) : null) ?? uri.Host;
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
                    f.Links[at] = new Link { Id = l.Id, Name = l.Name, Url = l.Url, Description = l.Description, Address = l.Address, Group = name, Icon = l.Icon, IconFrom = l.IconFrom, Place = place++, Added = l.Added, AddedBy = l.AddedBy };
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
                    states[l.Id] = test == "down" ? new LinkState(false, true, null, null, l.Added, now) : new LinkState(true, false, 12, 200, null, now);
                    continue;
                }
                if (seen.TryGetValue(l.Id, out var c) && c.Url == l.Url)
                {
                    states[l.Id] = new LinkState(c.Up, !c.Up && now - (c.Since ?? now) >= after, c.Ms, c.Code, c.Since, c.At, c.Why);
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

    /// <summary>Why a check got no answer, in words for the page: what the down card and the square say.</summary>
    private static string Why(Exception e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException)
        {
            switch (x)
            {
                case SocketException { SocketErrorCode: SocketError.ConnectionRefused }:
                    return "it refused the connection";
                case SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData }:
                    return "its name isn't in DNS";
                case SocketException { SocketErrorCode: SocketError.HostUnreachable or SocketError.NetworkUnreachable }:
                    return "it can't be reached from this server";
                case System.Security.Authentication.AuthenticationException:
                    return "the TLS handshake failed";
                case TimeoutException:
                    return "it didn't connect within 4 seconds";
            }
        }
        return e is OperationCanceledException ? $"no answer within {CheckFor.TotalSeconds:0} seconds" : "the connection failed";
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
                return (Link: l, Up: true, Ms: (int?) timer.ElapsedMilliseconds, Code: (int?) answer.StatusCode, Why: (string?) null);
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or UriFormatException or InvalidOperationException or NotSupportedException)
            {
                return (Link: l, Up: false, Ms: (int?) null, Code: (int?) null, Why: (string?) Why(e));
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
                seen[l.Id] = new Checked(l.Url, a.Up, a.Ms, a.Code, now, since, a.Why);
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

    /// <summary>
    /// The body, up to the limit; null when it's bigger. A page (upTo) is read as far as the limit, and no further than the
    /// end of its head once that has a title or an icon in it: Yahoo Finance's 1.5 MB page has all that in its first 80 KB.
    /// Anything else that says it's bigger isn't fetched at all.
    /// </summary>
    private static async Task<byte[]?> Read(HttpResponseMessage answer, int limit, CancellationToken stop, bool upTo = false)
    {
        var said = answer.Content.Headers.ContentLength;
        if (!upTo && said > limit)
        {
            return null;
        }
        await using var stream = await answer.Content.ReadAsStreamAsync(stop);
        using var into = new MemoryStream(said is > 0 ? (int) Math.Min(said.Value, limit) : 16384);
        var buffer = new byte[16384];
        int n;
        while ((n = await stream.ReadAsync(buffer, stop)) > 0)
        {
            if (into.Length + n > limit)
            {
                return upTo && into.Length > 0 ? into.ToArray() : null;
            }
            var from = (int) Math.Max(0, into.Length - 6);
            into.Write(buffer, 0, n);
            // The head has a name or an icon in it, read the way the lookup reads them (vCenter's title is only written by a script).
            if (upTo && Find(into.GetBuffer(), from, (int) into.Length, "</head") is var end and >= 0
                && System.Text.Encoding.UTF8.GetString(into.GetBuffer(), 0, end) is var head && (Named(head) is not null || IconLinkTag.IsMatch(head)))
            {
                return into.GetBuffer().AsSpan(0, end).ToArray();
            }
        }
        return into.ToArray();
    }

    /// <summary>Where some ASCII text starts in bytes from..to, any case; -1 when it isn't there.</summary>
    private static int Find(byte[] b, int from, int to, string text)
    {
        for (var i = from; i <= to - text.Length; i++)
        {
            var k = 0;
            while (k < text.Length && (b[i + k] | 0x20) == (text[k] | 0x20))
            {
                k++;
            }
            if (k == text.Length)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>A page's HTML, where it ended up, and whether the site was happy to give it. One that only sends the browser on (ESXi's front page, to /ui) is followed, twice at most.</summary>
    private static async Task<(string Html, Uri Where, bool Ok)> Page(HttpClient http, Uri at, CancellationToken stop)
    {
        for (var hop = 0; ; hop++)
        {
            using var answer = await http.GetAsync(at, HttpCompletionOption.ResponseHeadersRead, stop);
            var html = await Read(answer, MaxPage, stop, upTo: true) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : "";
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

    /// <summary>
    /// The site's best icon as a data: URL: the icon its page lists for the browser tab (an SVG, else the biggest), else its
    /// phone home-screen icon, else /favicon.ico and the other usual places; six tries at most. With only, just the picture
    /// at page. With same, the others are read too, to say whether that icon is one the site serves (Saw).
    /// </summary>
    private static async Task<(string? Icon, string? From, bool Saw)> Icon(HttpClient http, string html, Uri page, CancellationToken stop, bool only = false, string? same = null)
    {
        (string Icon, string From)? chosen = null;
        var saw = false;
        // The first that works is the one; when asked about another, the rest are read until it turns up.
        bool Found(string icon, string from)
        {
            chosen ??= (icon, from);
            saw |= icon == same;
            return same is null || saw;
        }
        List<string> listed = only ? [page.ToString()] : [.. Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase).Select(m => m.Value)
            .Select(tag => (Rel: Attr(tag, "rel")?.ToLowerInvariant() ?? "", Href: Attr(tag, "href"), Sizes: Attr(tag, "sizes"), Type: Attr(tag, "type")))
            .Where(l => l.Href is { Length: > 0 } && (l.Rel.Split(' ').Contains("icon") || l.Rel.Contains("apple-touch-icon")))
            .OrderByDescending(l => l.Rel.Split(' ').Contains("icon")) // what the browser tab shows; a phone's home-screen icon is often another design (Plex's is white on black)
            .ThenByDescending(l => l.Type == "image/svg+xml" || (l.Href ?? "").EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(l => int.TryParse(l.Sizes?.Split('x')[0], out var s) ? s : 0)
            .Select(l => l.Href!),
            // Where icons often are when the page doesn't say: an iDRAC 8 sends its front page on by script, and keeps its icon in /images.
            "/favicon.ico", "/favicon.png", "/images/favicon.png", "/images/favicon.ico"];
        var tries = listed.Distinct().Take(6).ToList();
        string[] guesses = ["/favicon.ico", "/favicon.png", "/images/favicon.png", "/images/favicon.ico"];
        for (var i = 0; i < tries.Count; i++)
        {
            var href = tries[i];
            // Asked about another icon, only the ones the page lists are compared: never a guess, once there's an icon.
            if (same is not null && chosen is not null && guesses.Contains(href))
            {
                break;
            }
            if (href.StartsWith("data:image/", StringComparison.Ordinal))
            {
                if (href.IndexOf(',') is var comma && (comma < 0 || comma == href.Length - 1))
                {
                    continue; // an empty one, as an iDRAC's error page lists
                }
                if (href.Length <= MaxIcon && Found(href, "the page itself"))
                {
                    break;
                }
                continue;
            }
            if (!Uri.TryCreate(page, href, out var at) || at.Scheme is not ("http" or "https"))
            {
                continue;
            }
            try
            {
                using var answer = await http.GetAsync(at, HttpCompletionOption.ResponseHeadersRead, stop);
                // A web page where an icon was asked for (Netflix sends /favicon.png to its 2 MB home page) isn't read at all.
                if (!answer.IsSuccessStatusCode || answer.Content.Headers.ContentType?.MediaType == "text/html"
                    || await Read(answer, MaxIconFile, stop) is not { Length: > 0 } bytes)
                {
                    continue;
                }
                var kind = Kind(bytes, answer.Content.Headers.ContentType?.MediaType);
                var (picture, type) = kind == "image/x-icon" && bytes.Length > MaxIcon ? OneIcon(bytes) : (bytes, kind);
                if (type is not null && picture.Length <= MaxIcon)
                {
                    if (Found($"data:{type};base64,{Convert.ToBase64String(picture)}", only ? "DuckDuckGo's icon service" : at.AbsolutePath))
                    {
                        break;
                    }
                    continue;
                }
                // A picture too big to keep (Pelican's one-picture .ico is 260 KB): the same one by the same name is often there as an SVG or PNG.
                if (kind is not null && !only && at.AbsolutePath.LastIndexOf('.') is var dot and > 0)
                {
                    tries.InsertRange(i + 1, new[] { ".svg", ".png" }.Select(e => at.AbsolutePath[..dot] + e).Where(p => !tries.Contains(p)));
                }
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
            {
                // The next one; out of time, the site goes without an icon.
            }
        }
        return (chosen?.Icon, chosen?.From, saw);
    }

    /// <summary>A big .ico (Pelican's holds every size in 260 KB) cut down to its biggest picture that fits: a PNG one as itself, otherwise an .ico of one.</summary>
    private static (byte[] Bytes, string? Type) OneIcon(byte[] b)
    {
        var count = b[4] | b[5] << 8;
        (int Entry, int Side, int At, int Size)? best = null;
        for (var i = 0; i < count && 6 + (i + 1) * 16 <= b.Length; i++)
        {
            var e = 6 + i * 16;
            var side = b[e] == 0 ? 256 : b[e];
            var size = (long) BitConverter.ToUInt32(b, e + 8);
            var at = (long) BitConverter.ToUInt32(b, e + 12);
            if (size > 0 && size <= MaxIcon - 22 && at + size <= b.Length && (best is null || side > best.Value.Side))
            {
                best = (e, side, (int) at, (int) size);
            }
        }
        if (best is not { } pick)
        {
            return (b, null);
        }
        var data = b.AsSpan(pick.At, pick.Size).ToArray();
        if (data.Length > 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            return (data, "image/png");
        }
        var one = new byte[22 + data.Length];
        one[2] = 1; // an icon, not a cursor
        one[4] = 1; // of one picture
        Array.Copy(b, pick.Entry, one, 6, 16);
        BitConverter.GetBytes(22u).CopyTo(one, 18);
        data.CopyTo(one, 22);
        return (one, "image/x-icon");
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

    /// <summary>Whether an address is on a private network, like the lab's: an internet site's IP says nothing worth showing.</summary>
    private static bool OnLan(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a))
        {
            return false;
        }
        if (a.AddressFamily != AddressFamily.InterNetwork)
        {
            return a.IsIPv6UniqueLocal;
        }
        var b = a.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and < 32) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] is >= 64 and < 128);
    }

    /// <summary>Whether an address is out on the internet: not the lab's network, this computer, or a link-local one. Only those are asked about elsewhere.</summary>
    private static bool Public(string ip) =>
        IPAddress.TryParse(ip, out var a) && !OnLan(ip) && !IPAddress.IsLoopback(a) && !a.IsIPv6LinkLocal && !a.IsIPv6SiteLocal
        && !(a.AddressFamily == AddressFamily.InterNetwork && a.GetAddressBytes() is [169, 254, ..] or [0, ..]);

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

public sealed record LinkForm(string? Name, string? Url, string? Description, string? Address, string? Group, string? Icon, int? Place = null, string? IconFrom = null);

public sealed record LinkArrangeForm(string? Group, List<string>? Ids);

public sealed record LinkChecksForm(int? Every, int? DownAfter, bool? Describe = null);

/// <summary>Links as a file: the links (no ids, so they are new wherever they land), the groups in order, and the check times.</summary>
public sealed record LinksExport(string? Kind, int? Version, DateTime? Exported, LinkChecksForm? Checks, List<string>? Groups, List<LinkForm>? Links)
{
    public const string Is = "rabbidlab-links";
}

/// <summary>What an import did: links and groups added, links left out as already here, links turned away, and whether the check times were set.</summary>
public sealed record LinksImported(int Links, int Groups, int Skipped, int Refused, bool Checks);

/// <summary>What Refresh filled in on a link (icon, description, IP), or why its site couldn't be read when it filled in nothing.</summary>
public sealed record LinkRefreshed(Link Link, List<string> Changed, string? Problem);


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

        // Fills in what a link is missing from its site: one link a call, so the page can show how far along it is.
        app.MapPost("/api/links/{id}/refresh", async (string id, HttpContext context, Links links, ActivityLog log) =>
        {
            if (Access.RefuseUnless(context, Permissions.LinksEdit) is { } refused)
            {
                return refused;
            }
            if (await links.Refresh(id, context.RequestAborted) is not { } done)
            {
                return Results.NotFound(new { error = "That link isn't here any more." });
            }
            var c = done.Changed;
            if (c.Count > 0)
            {
                log.Record(context, "links", "link.refresh", $"Filled in the {string.Join(", ", c.SkipLast(1))}{(c.Count > 1 ? " and " : "")}{c[^1]} of {done.Link.Name}");
            }
            return Results.Ok(new { link = done.Link, changed = c, problem = done.Problem });
        });

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
