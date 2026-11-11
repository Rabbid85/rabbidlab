using System.Text.RegularExpressions;

namespace RabbidLab.Server;

/// <summary>Homelab products Links recognises from a site's name, host name or page, with the description it writes for each.</summary>
public static class KnownSites
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);

    private sealed record Site(string Description, Regex Name, Regex Page);

    private static Site S(string description, string name, string? page = null) =>
        new(description, new Regex(name, Options, Limit), new Regex(page ?? name, Options, Limit));

    // The first that matches wins, so dashboards (whose pages name other products) and the more specific ones come first.
    private static readonly Site[] Sites =
    [
        S("Homarr dashboard", "homarr"),
        S("Heimdall dashboard", "heimdall"),
        S("Organizr dashboard", "organizr"),
        S("Dashy dashboard", @"\bdashy\b"),
        S("Dell iDRAC out-of-band management", "idrac", "idrac|integrated dell remote access"),
        S("HPE iLO out-of-band management", @"\bilo\d?\b|integrated lights.out", @"integrated lights.out|hpe ilo\b"),
        S("Supermicro IPMI out-of-band management", @"supermicro|\bipmi\b", "supermicro"),
        S("VMware vCenter Server", @"vcenter|vsphere|\bvcsa\b", "vcenter|vsphere"),
        S("VMware ESXi host", @"esxi|\besx\d*\b|vmware host client", @"esxi\b|esxuiapp"),
        S("Proxmox Backup Server", @"proxmox backup|\bpbs\d*\b", "proxmox backup server"),
        S("Proxmox VE host", @"proxmox|\bpve\d*\b", "proxmox"),
        S("Xen Orchestra virtualization management", "xen orchestra|xcp-ng"),
        S("Windows Admin Center", "windows admin center"),
        S("TrueNAS storage", "truenas|freenas", @"truenas|freenas|<ix-root\b"),
        S("Unraid server", "unraid"),
        S("OpenMediaVault NAS", @"openmediavault|\bomv\d*\b", "openmediavault"),
        S("Synology NAS", @"synology|diskstation|\bdsm\b", "synology"),
        S("QNAP NAS", @"qnap|\bqu?ts\b", "qnap"),
        S("UniFi network console", "unifi"),
        S("TP-Link Omada controller", "omada"),
        S("pfSense firewall", "pfsense"),
        S("OPNsense firewall", "opnsense"),
        S("FortiGate firewall", "fortigate|fortinet"),
        S("MikroTik router", "mikrotik|routeros", "mikrotik|routeros|webfig"),
        S("Ubiquiti EdgeRouter", "edgerouter|edgeos", "edgeos"),
        S("Pi-hole DNS filtering", "pi-?hole", "pi-hole"),
        S("AdGuard Home DNS filtering", "adguard"),
        S("Technitium DNS server", "technitium"),
        S("Home Assistant home automation", @"home ?assistant|\bhass\b", "home assistant|home-assistant"),
        S("Node-RED automation", "node-red"),
        S("Plex media server", @"\bplex\b"),
        S("Jellyfin media server", "jellyfin"),
        S("Emby media server", @"\bemby\b"),
        S("Portainer container management", "portainer"),
        S("Grafana dashboards", "grafana"),
        S("Prometheus metrics", "prometheus"),
        S("Zabbix monitoring", "zabbix"),
        S("Uptime Kuma monitoring", "uptime[ -]?kuma"),
        S("Nginx Proxy Manager reverse proxy", "nginx proxy manager"),
        S("Traefik reverse proxy", "traefik"),
        S("Nextcloud file sync and share", "nextcloud"),
        S("Gitea Git server", "gitea"),
        S("Forgejo Git server", "forgejo"),
        S("GitLab", "gitlab"),
        S("Vaultwarden password manager", "vaultwarden"),
        S("Bitwarden password manager", "bitwarden"),
        S("Cockpit server management", "cockpit"),
        S("Webmin server management", "webmin"),
        S("Veeam backup", "veeam"),
        S("HP printer", "laserjet|officejet|deskjet", "laserjet|officejet|deskjet|hp embedded web server"),
    ];

    private static readonly HashSet<string> Ours = [.. Sites.Select(s => s.Description)];

    /// <summary>The description for the product a site is, from its name and host name first, then its page; null when it isn't one of these.</summary>
    public static string? Describe(string? name, string? url, string? html = null)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
        var called = $"{name} {host}";
        foreach (var s in Sites)
        {
            if (Matches(s.Name, called))
            {
                return s.Description;
            }
        }
        if (!string.IsNullOrEmpty(html))
        {
            foreach (var s in Sites)
            {
                if (Matches(s.Page, html))
                {
                    return s.Description;
                }
            }
        }
        return null;
    }

    /// <summary>Whether a description is one written here, so turning the feature off takes it away again.</summary>
    public static bool IsOurs(string? description) => description is not null && Ours.Contains(description);

    private static bool Matches(Regex r, string text)
    {
        try
        {
            return r.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
