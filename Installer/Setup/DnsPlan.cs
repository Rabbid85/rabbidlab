#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace RabbidLab.Setup
{
    /// <summary>A DNS zone on the DNS server Setup asked: its name, whether Setup can write to it, and whether it's a reverse zone.</summary>
    public sealed class DnsZoneInfo
    {
        public string Name = "";

        /// <summary>Primary: the server holds a copy it can change (always so for zones kept in Active Directory).</summary>
        public bool Primary;
        public bool Reverse;
    }

    /// <summary>A record in DNS: its type (A, AAAA, CNAME, TXT...) and what it holds (an address, or the name it's an alias of).</summary>
    public sealed class DnsRecordInfo
    {
        public string Type = "";
        public string Data = "";
    }

    /// <summary>What Setup found about a name before adding it: the zones, what's at the name now, and this computer.</summary>
    public sealed class DnsFacts
    {
        /// <summary>The name in full, like rabbidlab.contoso.local.</summary>
        public string Name = "";

        /// <summary>The DNS server (a domain controller) Setup read all this from, and would add the name on.</summary>
        public string Server = "";

        public List<DnsZoneInfo> Zones = new List<DnsZoneInfo>();

        /// <summary>The records at the name now, in the zone it belongs to.</summary>
        public List<DnsRecordInfo> Records = new List<DnsRecordInfo>();

        /// <summary>This computer's full name, which the alias points at.</summary>
        public string Target = "";

        /// <summary>The addresses DNS has for this computer's full name (on the same DNS server).</summary>
        public List<string> TargetAddresses = new List<string>();

        /// <summary>This computer's own addresses.</summary>
        public List<string> MyAddresses = new List<string>();

        /// <summary>Another computer in Active Directory with this name (its full name), or null.</summary>
        public string ClashingComputer;
    }

    public enum DnsVerdict
    {
        /// <summary>Setup can add the alias.</summary>
        CanAdd,

        /// <summary>The name already leads to this computer: nothing to add.</summary>
        AlreadyHere,

        /// <summary>The name is in use for something else; Setup never changes it.</summary>
        Taken,

        /// <summary>Setup can't add it here: no zone for it, a read-only copy, the zone's own name, this computer's own name, or a computer's.</summary>
        CantAdd,

        /// <summary>This computer's own name isn't in DNS (or points elsewhere), so an alias of it wouldn't lead here.</summary>
        NoTarget,

        /// <summary>Whoever runs Setup can't change DNS (it needs DNS admin rights).</summary>
        NoRights,

        /// <summary>No domain controller's DNS server could be asked.</summary>
        Unreachable,
    }

    /// <summary>
    /// Whether Setup can add a name of their own to DNS as an alias (CNAME) of this computer, decided from what it read
    /// (DnsFacts) before changing anything. It never changes or removes a record that's already there.
    /// </summary>
    public static class DnsPlan
    {
        /// <summary>The zone a name belongs in: the longest forward zone that's the name itself or ends with "." and its name.</summary>
        public static DnsZoneInfo ZoneFor(string name, IEnumerable<DnsZoneInfo> zones)
        {
            name = Clean(name);
            return zones
                .Where(z => !z.Reverse && z.Name.Length > 0 && (name == Clean(z.Name) || name.EndsWith("." + Clean(z.Name), StringComparison.Ordinal)))
                .OrderByDescending(z => z.Name.Length)
                .FirstOrDefault();
        }

        public static DnsVerdict Decide(DnsFacts f, out string zone, out string message)
        {
            var name = Clean(f.Name);
            var target = Clean(f.Target);
            var found = ZoneFor(name, f.Zones);
            zone = found == null ? null : Clean(found.Name);

            if (name == target)
            {
                message = $"{name} is the name of this computer. No alias record is required.";
                return DnsVerdict.AlreadyHere;
            }

            // What's at the name now. An alias of this computer, or addresses that are all this computer's, is fine as it
            // is. Anything else belongs to something else, and an alias can't share its name with any other record.
            var records = f.Records ?? new List<DnsRecordInfo>();
            var aliases = records.Where(r => r.Type == "CNAME").ToList();
            var addresses = records.Where(r => r.Type == "A" || r.Type == "AAAA").ToList();
            if (aliases.Count > 0)
            {
                if (aliases.All(r => Clean(r.Data) == target) && records.Count == aliases.Count)
                {
                    message = $"An alias (CNAME) record for {name} that points to this computer already exists.";
                    return DnsVerdict.AlreadyHere;
                }
                var other = aliases.Select(r => Clean(r.Data)).First(d => d != target || records.Count != aliases.Count);
                message = $"{name} already exists in DNS as an alias (CNAME) of {other}. Setup does not modify existing records. Choose a different name, or update the record on the DNS server.";
                return DnsVerdict.Taken;
            }
            if (addresses.Count > 0)
            {
                var elsewhere = addresses.Where(r => !f.MyAddresses.Contains(r.Data.Trim())).Select(r => r.Data.Trim()).ToList();
                if (elsewhere.Count == 0 && records.Count == addresses.Count)
                {
                    message = $"A host (A or AAAA) record for {name} that points to this computer already exists.";
                    return DnsVerdict.AlreadyHere;
                }
                message = elsewhere.Count > 0
                    ? $"{name} already exists in DNS and resolves to {elsewhere[0]}. Setup does not modify existing records. Choose a different name, or update the record on the DNS server."
                    : $"{name} already has other records in DNS. Setup does not modify existing records. Choose a different name.";
                return DnsVerdict.Taken;
            }
            if (records.Count > 0)
            {
                message = $"{name} already has a record of type {records[0].Type} in DNS. An alias (CNAME) record cannot share its name with other records. Choose a different name.";
                return DnsVerdict.Taken;
            }

            if (f.ClashingComputer != null)
            {
                message = $"The computer account {f.ClashingComputer} in Active Directory uses this name, and an alias record would conflict with its host (A) record. Choose a different name.";
                return DnsVerdict.CantAdd;
            }
            if (found == null)
            {
                message = $"{f.Server} has no DNS zone that contains {name}. Create an alias (CNAME) record that points to {target} on the DNS server that hosts the zone for this name.";
                return DnsVerdict.CantAdd;
            }
            if (name == zone)
            {
                message = $"{name} is the name of a DNS zone and cannot be used as an alias. Choose a name within the zone, such as rabbidlab.{name}.";
                return DnsVerdict.CantAdd;
            }
            if (!found.Primary)
            {
                message = $"{f.Server} holds a read-only copy of the {zone} zone, so Setup cannot create records in it. Create the alias (CNAME) record for {name} on the DNS server that hosts the primary zone.";
                return DnsVerdict.CantAdd;
            }

            // The alias leads to this computer's own name, so that has to be in DNS and point here.
            var targetHere = (f.TargetAddresses ?? new List<string>()).Where(a => f.MyAddresses.Contains(a.Trim())).ToList();
            if ((f.TargetAddresses ?? new List<string>()).Count == 0)
            {
                message = $"This computer's host (A) record, {target}, is not registered in DNS, so an alias that points to it would not resolve. Run ipconfig /registerdns on this computer, wait one minute, and then try again.";
                return DnsVerdict.NoTarget;
            }
            if (targetHere.Count == 0)
            {
                message = $"This computer's host (A) record, {target}, resolves to {f.TargetAddresses[0]}, which is not an address of this computer. Correct the record (for example, by running ipconfig /registerdns), and then try again.";
                return DnsVerdict.NoTarget;
            }

            message = $"Setup can create an alias (CNAME) record for this name in the {zone} zone on {f.Server}.";
            return DnsVerdict.CanAdd;
        }

        public static string Clean(string name) => (name ?? "").Trim().TrimEnd('.').ToLowerInvariant();
    }
}
