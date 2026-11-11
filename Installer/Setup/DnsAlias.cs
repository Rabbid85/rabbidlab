using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace RabbidLab.Setup
{
    /// <summary>
    /// The name of their own, in the domain's DNS: an alias (CNAME) of this computer, so it follows the server if its IP
    /// address changes, and Windows sign-in works by that name without registering anything more (browsers ask for
    /// the server's own name). Setup adds it on a domain controller's DNS server over WMI, as whoever runs Setup,
    /// which needs DNS admin rights (Domain Admins have them).
    ///
    /// Before changing anything it reads what's there (Read) and decides with DnsPlan: it only ever adds a new alias,
    /// never changes or removes a record that's already there, and refuses a name a computer in Active Directory
    /// has. After adding, it checks the record is on the DNS server and that this computer can look the name up.
    /// Removing everything takes the alias out again, but only if Setup added it, and only while it still points here.
    /// </summary>
    public static class DnsAlias
    {
        public enum Found { Nothing, ThisServer, Elsewhere }

        /// <summary>Where the name leads now, as this computer looks it up: nowhere, here, or somewhere else (and where).</summary>
        public static (Found Found, string Where) Lookup(string name)
        {
            try
            {
                var entry = Dns.GetHostEntry(name);
                var mine = new HashSet<string>(Choices.Addresses()) { "127.0.0.1", "::1" };
                if (string.Equals(entry.HostName, Choices.FullName, StringComparison.OrdinalIgnoreCase) || entry.AddressList.Any(a => mine.Contains(a.ToString())))
                {
                    return (Found.ThisServer, entry.HostName);
                }
                var ip = entry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                return (Found.Elsewhere, ip?.ToString() ?? entry.HostName);
            }
            catch (SocketException)
            {
                return (Found.Nothing, "");
            }
            catch (ArgumentException)
            {
                return (Found.Nothing, "");
            }
        }

        /// <summary>
        /// Whether Setup can add the name, without changing anything: for the Web address page, as the name is typed.
        /// NoRights and Unreachable come from here rather than DnsPlan, since they're about reaching DNS at all.
        /// </summary>
        public static (DnsVerdict Verdict, string Message) Check(string name)
        {
            try
            {
                var verdict = DnsPlan.Decide(Read(name), out _, out var message);
                return (verdict, message);
            }
            catch (Exception e) when (IsNoRights(e))
            {
                return (DnsVerdict.NoRights, "Your account does not have permission to create DNS records. This requires DNS administrator permissions, such as membership in Domain Admins. Create the record on the DNS server manually, or run Setup as a domain administrator.");
            }
            catch (Exception e) when (e is DnsProblem || e is ManagementException || e is COMException || e is ActiveDirectoryObjectNotFoundException
                || e is ActiveDirectoryOperationException)
            {
                return (DnsVerdict.Unreachable, $"Setup could not query DNS: {Explain(e)}. To use this name, create an alias (CNAME) record for it on the DNS server.");
            }
        }

        /// <summary>
        /// Adds the alias on a domain controller's DNS server, after checking it's safe to (see DnsPlan). Created is false
        /// when the name already led here, so removing Rabbid Lab leaves it alone. Throws, saying why, when it can't.
        /// Afterwards it checks the record is there, and that this computer can look the name up.
        /// </summary>
        public static string Add(string name, out string server, out string zone, out bool created)
        {
            var facts = Read(name);
            server = facts.Server;
            created = false;
            var verdict = DnsPlan.Decide(facts, out zone, out var message);
            if (verdict == DnsVerdict.AlreadyHere)
            {
                return message;
            }
            if (verdict != DnsVerdict.CanAdd)
            {
                throw new DnsProblem(message);
            }

            var scope = Connect(server);
            using (var type = new ManagementClass(scope, new ManagementPath("MicrosoftDNS_CNAMEType"), null))
            {
                var data = type.GetMethodParameters("CreateInstanceFromPropertyData");
                data["DnsServerName"] = server;
                data["ContainerName"] = zone;
                data["OwnerName"] = facts.Name;
                data["PrimaryName"] = facts.Target + ".";
                type.InvokeMethod("CreateInstanceFromPropertyData", data, null);
            }
            created = true;

            var records = Records(scope, zone, facts.Name);
            if (!records.Any(r => r.Type == "CNAME" && DnsPlan.Clean(r.Data) == facts.Target))
            {
                throw new DnsProblem($"{server} accepted the record for {facts.Name}, but the record does not appear in the {zone} zone. Verify the zone on {server}");
            }

            // While the name was typed, this computer looked it up and remembered "not found" for a while. Forget that,
            // then look again: this computer's DNS server may be another domain controller, which gets the new record
            // when Active Directory copies it across.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                Forget(facts.Name);
                if (Lookup(facts.Name).Found == Found.ThisServer)
                {
                    return $"Created the alias (CNAME) record {facts.Name}, which points to {facts.Target}, on {server}. The name resolves from this computer.";
                }
                Thread.Sleep(2000);
            }
            return $"Created the alias (CNAME) record {facts.Name}, which points to {facts.Target}, on {server}. The name does not resolve from this computer yet. "
                + "The record reaches the other domain controllers through Active Directory replication, typically within a few minutes.";
        }

        /// <summary>
        /// Takes out the alias Setup added: only a CNAME for that name that still points at that server, on the DNS
        /// server it was added on, or another domain controller if that one's gone. Says what it did.
        /// </summary>
        public static string Remove(string name, string zone, string server, string target)
        {
            name = DnsPlan.Clean(name);
            target = DnsPlan.Clean(target);
            Exception last = null;
            foreach (var dns in new[] { server }.Concat(DnsServers().Where(s => !string.Equals(s, server, StringComparison.OrdinalIgnoreCase))))
            {
                ManagementScope scope;
                try
                {
                    scope = Connect(dns);
                }
                catch (Exception e) when (!IsNoRights(e) && (e is COMException || e is ManagementException))
                {
                    last = e;
                    continue;
                }
                var mine = RecordObjects(scope, zone, name).Where(r => PointsAt(r, target)).ToList();
                if (mine.Count == 0)
                {
                    var now = Records(scope, zone, name);
                    return now.Count == 0
                        ? $"The DNS record for {name} no longer exists."
                        : $"The DNS record for {name} no longer points to this computer. Setup did not modify it.";
                }
                foreach (var record in mine)
                {
                    record.Delete();
                }
                if (RecordObjects(scope, zone, name).Any(r => PointsAt(r, target)))
                {
                    throw new DnsProblem($"the alias (CNAME) record for {name} is still present on {dns} after deletion");
                }
                Forget(name);
                return $"Removed the alias (CNAME) record {name} from {dns}.";
            }
            throw last ?? new DnsProblem("no DNS server on a domain controller could be reached");
        }

        /// <summary>A sentence for why it couldn't, without the WMI jargon where there's something plainer to say.</summary>
        public static string Explain(Exception e)
        {
            if (e is DnsProblem)
            {
                return e.Message.Trim().TrimEnd('.');
            }
            if (IsNoRights(e))
            {
                return "the account running Setup does not have DNS administrator permissions, such as membership in Domain Admins";
            }
            if (e is ActiveDirectoryObjectNotFoundException)
            {
                return "no domain controller responded";
            }
            if (e is COMException)
            {
                return "the DNS server on the domain controller could not be reached (" + e.Message.Trim().TrimEnd('.') + ")";
            }
            return e.Message.Trim().TrimEnd('.');
        }

        /// <summary>A reason Setup won't, or couldn't, add the name, in plain words.</summary>
        public sealed class DnsProblem : Exception
        {
            public DnsProblem(string message) : base(message) { }
        }

        // --- Reading DNS and Active Directory ---------------------------------------------------------------

        /// <summary>
        /// What's in DNS around the name, read from the nearest domain controller that runs DNS: its zones, what's at the
        /// name now, what this computer's own name points at, and whether a computer in Active Directory has the name.
        /// </summary>
        public static DnsFacts Read(string name)
        {
            name = DnsPlan.Clean(name);
            var target = DnsPlan.Clean(Choices.FullName);
            var skipped = new List<string>();
            foreach (var server in DnsServers())
            {
                ManagementScope scope;
                try
                {
                    scope = Connect(server);
                }
                catch (ManagementException e) when (e.ErrorCode == ManagementStatus.InvalidNamespace)
                {
                    skipped.Add($"{server} does not have the DNS Server role installed");
                    continue;
                }
                catch (Exception e) when (!IsNoRights(e) && (e is COMException || e is ManagementException))
                {
                    skipped.Add($"{server} did not respond");
                    continue;
                }

                var facts = new DnsFacts
                {
                    Name = name,
                    Server = server.ToLowerInvariant(),
                    Target = target,
                    MyAddresses = Choices.Addresses(),
                    Zones = Zones(scope),
                };
                var zone = DnsPlan.ZoneFor(name, facts.Zones);
                if (zone != null)
                {
                    facts.Records = Records(scope, DnsPlan.Clean(zone.Name), name);
                }
                var targetZone = DnsPlan.ZoneFor(target, facts.Zones);
                facts.TargetAddresses = targetZone == null
                    ? Dns.GetHostAddresses(target).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToList()
                    : Records(scope, DnsPlan.Clean(targetZone.Name), target).Where(r => r.Type == "A").Select(r => r.Data.Trim()).ToList();
                facts.ClashingComputer = ComputerNamed(name, target);
                return facts;
            }
            throw new DnsProblem(skipped.Count == 0 ? "no domain controller responded" : string.Join(", ", skipped));
        }

        /// <summary>This domain's domain controllers, the nearest first.</summary>
        private static List<string> DnsServers()
        {
            var servers = new List<string>();
            using (var domain = Domain.GetComputerDomain())
            {
                try
                {
                    using (var nearest = domain.FindDomainController())
                    {
                        servers.Add(nearest.Name);
                    }
                }
                catch (ActiveDirectoryObjectNotFoundException)
                {
                    // None answered just now; try them all below.
                }
                foreach (DomainController dc in domain.DomainControllers)
                {
                    using (dc)
                    {
                        if (!servers.Contains(dc.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            servers.Add(dc.Name);
                        }
                    }
                }
            }
            return servers;
        }

        private static ManagementScope Connect(string server)
        {
            var scope = new ManagementScope($@"\\{server}\root\MicrosoftDNS", new ConnectionOptions
            {
                Impersonation = ImpersonationLevel.Impersonate,
                Authentication = AuthenticationLevel.PacketPrivacy,
                Timeout = TimeSpan.FromSeconds(30),
            });
            scope.Connect();
            return scope;
        }

        private static List<DnsZoneInfo> Zones(ManagementScope scope)
        {
            using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name, ZoneType, Reverse FROM MicrosoftDNS_Zone")))
            {
                return searcher.Get().Cast<ManagementObject>().Select(z => new DnsZoneInfo
                {
                    Name = (string) z["Name"] ?? "",
                    Primary = Convert.ToUInt32(z["ZoneType"] ?? 0u) == 1,
                    Reverse = z["Reverse"] is bool reverse && reverse,
                }).ToList();
            }
        }

        /// <summary>Every record at the name in the zone, as type and data. Names are checked DNS names, so they're safe in the query.</summary>
        private static List<DnsRecordInfo> Records(ManagementScope scope, string zone, string name) =>
            RecordObjects(scope, zone, name).Select(r => new DnsRecordInfo
            {
                Type = ((string) r["__CLASS"] ?? "").Replace("MicrosoftDNS_", "").Replace("Type", "").ToUpperInvariant(),
                Data = (string) r["RecordData"] ?? "",
            }).ToList();

        private static List<ManagementObject> RecordObjects(ManagementScope scope, string zone, string name)
        {
            if (!Choices.IsDnsName(zone) || !Choices.IsDnsName(name))
            {
                throw new DnsProblem($"{name} is not a valid DNS name");
            }
            var query = new ObjectQuery($"SELECT * FROM MicrosoftDNS_ResourceRecord WHERE ContainerName = '{zone}' AND OwnerName = '{name}'");
            using (var searcher = new ManagementObjectSearcher(scope, query))
            {
                return searcher.Get().Cast<ManagementObject>().ToList();
            }
        }

        private static bool PointsAt(ManagementObject record, string target) =>
            (string) record["__CLASS"] == "MicrosoftDNS_CNAMEType" && DnsPlan.Clean((string) record["RecordData"]) == DnsPlan.Clean(target);

        /// <summary>
        /// Another computer in Active Directory with this name, whose own DNS record would clash with the alias (even if
        /// it's switched off and its record has gone for now). This computer doesn't count.
        /// </summary>
        private static string ComputerNamed(string name, string target)
        {
            var label = name.Split('.')[0];
            var inDomain = name == label + "." + DnsPlan.Clean(Choices.Domain);
            try
            {
                using (var root = new DirectoryEntry("LDAP://" + Choices.Domain))
                using (var searcher = new DirectorySearcher(root,
                    $"(&(objectCategory=computer)(|(dNSHostName={name}){(inDomain ? $"(sAMAccountName={label}$)" : "")}))",
                    new[] { "dNSHostName", "name" }))
                {
                    searcher.SizeLimit = 5;
                    using (var found = searcher.FindAll())
                    {
                        foreach (SearchResult r in found)
                        {
                            var dns = r.Properties["dNSHostName"].Count > 0 ? DnsPlan.Clean(r.Properties["dNSHostName"][0] as string) : "";
                            if (dns != target)
                            {
                                return r.Properties["name"].Count > 0 ? (string) r.Properties["name"][0] : name;
                            }
                        }
                    }
                }
            }
            catch (COMException)
            {
                // Couldn't search the domain; DNS itself still shows any record that's there.
            }
            return null;
        }

        private static bool IsNoRights(Exception e) =>
            e is UnauthorizedAccessException || (e is ManagementException m && m.ErrorCode == ManagementStatus.AccessDenied);

        /// <summary>Makes this computer forget what it remembered about the name, so the next lookup asks DNS again.</summary>
        private static void Forget(string name)
        {
            try
            {
                DnsFlushResolverCacheEntry(name);
            }
            catch (EntryPointNotFoundException)
            {
                // Older Windows: the remembered answer runs out by itself in a few minutes.
            }
        }

        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCacheEntry_W", CharSet = CharSet.Unicode)]
        private static extern bool DnsFlushResolverCacheEntry(string name);
    }
}
