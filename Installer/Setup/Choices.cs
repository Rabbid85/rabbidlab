using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Win32;

namespace RabbidLab.Setup
{
    /// <summary>
    /// Everything Setup asks. It starts from the defaults, then the answers from the last time Setup ran here,
    /// then the answer file (RabbidLab.xml next to Setup, or the one named with /config=), then the command line.
    ///
    /// The answer file can hold everything, so a quiet install needs nothing else:
    ///   &lt;RabbidLab&gt;
    ///     &lt;Setup Quiet="yes" AcceptLicense="yes" /&gt;
    ///     &lt;Company Name="Home" /&gt;
    ///     &lt;Install Folder="C:\Program Files\Rabbid Lab" StartMenu="yes" Desktop="no" /&gt;
    ///     &lt;Web Port="8420" Https="yes" Name="lab.home.lan" AddToDns="no" Firewall="yes" Redirect="yes" CertificateSubject="" /&gt;
    ///   &lt;/RabbidLab&gt;
    /// </summary>
    public sealed class Choices
    {
        public const string ServiceName = Edition.Id;
        public const string SavedKey = @"SOFTWARE\Rabbid\" + Edition.Name;
        public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + Edition.Id;

        public string InstallDir = Edition.ProgramDir;
        public int Port = Edition.DefaultPort;
        public bool Https = true;
        public bool Firewall = true;

        /// <summary>
        /// Also answer on port 80 and send people on to the console's address, so typing just its name in a browser
        /// works. The console only takes port 80 when nothing else has it. Never Rabbid Lab Dev: port 80 is the release's.
        /// </summary>
        public bool Redirect = !Edition.Dev;

        /// <summary>Blank: Setup makes a certificate. Otherwise what the subject of one already on the server contains.</summary>
        public string CertificateSubject = "";

        /// <summary>A name of their own, like rabbidlab or lab.home.lan, pointed at this computer in DNS. Goes in the certificate.</summary>
        public string ExtraName = "";

        /// <summary>Setup adds the name of their own to the domain's DNS, as an alias of this computer.</summary>
        public bool AddToDns;

        /// <summary>The name of their own already points at this computer in DNS.</summary>
        public bool NameInDns;

        /// <summary>The name of their own in full: rabbidlab is rabbidlab.home.lan inside the home.lan domain.</summary>
        public string ExtraFullName => ExtraName.Length == 0 || ExtraName.Contains(".") || Domain.Length == 0 ? ExtraName : ExtraName + "." + Domain;

        public bool StartMenu = true;
        public bool Desktop = true;

        /// <summary>Exclude Rabbid Lab from Defender's attack surface reduction rules before starting it, where they'd block it.</summary>
        public bool AllowInDefender = true;

        /// <summary>The answer file, and what it asks for beyond Setup's own pages.</summary>
        public string ConfigFile;
        public bool ConfigNamed;
        public bool Quiet;
        public bool AcceptLicense;
        public string Company;

        /// <summary>The version already installed, when this is an upgrade.</summary>
        public string InstalledVersion;


        public static Choices Load(string[] args)
        {
            var c = new Choices();
            c.ReadInstalled();

            // The answer file: named with /config=, or RabbidLab.xml next to Setup.
            var named = args.Select(SplitArg).FirstOrDefault(a => a.Name == "config").Value;
            var file = !string.IsNullOrEmpty(named) ? Path.GetFullPath(named) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RabbidLab.xml");
            if (File.Exists(file))
            {
                c.ReadConfig(XDocument.Load(file).Root);
                c.ConfigFile = file;
                c.ConfigNamed = !string.IsNullOrEmpty(named);
            }
            else if (!string.IsNullOrEmpty(named))
            {
                throw new FileNotFoundException($"The answer file {file} does not exist.");
            }

            foreach (var arg in args)
            {
                var (name, value) = SplitArg(arg);
                switch (name)
                {
                    case "port": c.Port = int.TryParse(value, out var p) ? p : c.Port; break;
                    case "http": c.Https = false; break;
                    case "nofirewall": c.Firewall = false; break;
                    case "noredirect": c.Redirect = false; break;
                    case "certsubject": c.CertificateSubject = value; break;
                    case "name": c.ExtraName = value; break;
                    case "adddns": c.AddToDns = true; break;
                    case "dir": c.InstallDir = value; break;
                    case "nostartmenu": c.StartMenu = false; break;
                    case "nodesktop": c.Desktop = false; break;
                    case "desktop": c.Desktop = true; break;
                    case "nodefender": c.AllowInDefender = false; break;
                }
            }
            c.Redirect &= !Edition.Dev;
            // An update stays in the folder it's installed in, whatever the answer file or /dir= says.
            if (c.InstalledVersion != null && c.installedDir != null)
            {
                c.InstallDir = c.installedDir;
            }
            return c;
        }

        /// <summary>Where the installed copy is, from the registry; null when it isn't installed.</summary>
        private string installedDir;

        /// <summary>The settings the installed copy was set up with (Setup saves them in the registry), if it's installed.</summary>
        private void ReadInstalled()
        {
            using (var saved = Registry.LocalMachine.OpenSubKey(SavedKey))
            {
                if (saved == null)
                {
                    return;
                }
                installedDir = saved.GetValue("InstallDir") as string;
                InstallDir = installedDir ?? InstallDir;
                Port = saved.GetValue("Port") is int port ? port : Port;
                // Saved without them, it was installed by a Setup that didn't ask: Edition.EarlierHttps says what it did.
                Https = (saved.GetValue("Https") as int? ?? (Edition.EarlierHttps ? 1 : 0)) == 1;
                Firewall = (saved.GetValue("Firewall") as int? ?? 1) == 1;
                Redirect = (saved.GetValue("Redirect") as int? ?? (Edition.EarlierHttps ? 1 : 0)) == 1;
                CertificateSubject = saved.GetValue("CertificateSubject") as string ?? "";
                ExtraName = saved.GetValue("ExtraName") as string ?? "";
                StartMenu = (saved.GetValue("StartMenu") as int? ?? 1) == 1;
                Desktop = (saved.GetValue("Desktop") as int? ?? 1) == 1;
                InstalledVersion = saved.GetValue("Version") as string;
            }
        }

        /// <summary>
        /// An update that keeps the current settings (Setup's Update option): back to exactly what's installed, even
        /// where an answer file next to Setup says otherwise, and nothing added to DNS.
        /// </summary>
        public void UseInstalledSettings()
        {
            ReadInstalled();
            AddToDns = false;
        }

        private void ReadConfig(XElement root)
        {
            if (root == null || root.Name.LocalName != "RabbidLab")
            {
                throw new InvalidDataException("The answer file is not valid. Its root element must be <RabbidLab>.");
            }
            var setup = root.Element("Setup");
            if (setup != null)
            {
                Quiet = IsYes(Attr(setup, "Quiet"));
                AcceptLicense = IsYes(Attr(setup, "AcceptLicense"));
            }
            var company = Attr(root.Element("Company"), "Name");
            Company = company.Length > 0 ? company : null;
            var install = root.Element("Install");
            if (install != null)
            {
                InstallDir = Attr(install, "Folder").Length > 0 ? Attr(install, "Folder") : InstallDir;
                StartMenu = Attr(install, "StartMenu").Length > 0 ? IsYes(Attr(install, "StartMenu")) : StartMenu;
                Desktop = Attr(install, "Desktop").Length > 0 ? IsYes(Attr(install, "Desktop")) : Desktop;
            }
            var web = root.Element("Web");
            if (web != null)
            {
                Port = int.TryParse(Attr(web, "Port"), out var port) ? port : Port;
                Https = Attr(web, "Https").Length > 0 ? IsYes(Attr(web, "Https")) : Https;
                Firewall = Attr(web, "Firewall").Length > 0 ? IsYes(Attr(web, "Firewall")) : Firewall;
                Redirect = Attr(web, "Redirect").Length > 0 ? IsYes(Attr(web, "Redirect")) : Redirect;
                CertificateSubject = web.Attribute("CertificateSubject") != null ? Attr(web, "CertificateSubject") : CertificateSubject;
                ExtraName = web.Attribute("Name") != null ? Attr(web, "Name").ToLowerInvariant() : ExtraName;
                AddToDns = Attr(web, "AddToDns").Length > 0 ? IsYes(Attr(web, "AddToDns")) : AddToDns;
            }
        }

        public static string Attr(XElement element, string name) => element?.Attribute(name)?.Value.Trim() ?? "";

        public static bool IsYes(string value) =>
            new[] { "yes", "true", "1", "on" }.Contains(value.Trim().ToLowerInvariant());

        /// <summary>"/port=8443" or "-port:8443" → ("port", "8443"); "/http" → ("http", "").</summary>
        public static (string Name, string Value) SplitArg(string arg)
        {
            var a = arg.TrimStart('/', '-');
            var at = a.IndexOfAny(new[] { '=', ':' });
            return at < 0 ? (a.ToLowerInvariant(), "") : (a.Substring(0, at).ToLowerInvariant(), a.Substring(at + 1).Trim('"'));
        }

        // --- This computer ------------------------------------------------------------------------------------

        public static string ShortName => Environment.MachineName.ToLowerInvariant();

        /// <summary>server.domain, or just the server name when it isn't in a domain.</summary>
        public static string FullName => Domain.Length == 0 ? ShortName : (ShortName + "." + Domain).ToLowerInvariant();

        public static string Domain
        {
            get
            {
                lock (Network)
                {
                    AskWindows();
                    return domain;
                }
            }
        }

        /// <summary>This computer's IPv4 addresses on networks that are up, leaving out loopback and self-assigned ones.</summary>
        public static List<string> Addresses()
        {
            lock (Network)
            {
                AskWindows();
                return addresses.ToList();
            }
        }

        // The pages redraw their addresses on every keystroke: Windows is asked for them at most every few seconds.
        private static readonly object Network = new object();
        private static DateTime askedAt;
        private static string domain;
        private static List<string> addresses;

        private static void AskWindows()
        {
            if (addresses != null && DateTime.UtcNow - askedAt < TimeSpan.FromSeconds(5))
            {
                return;
            }
            domain = IPGlobalProperties.GetIPGlobalProperties().DomainName ?? "";
            addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254."))
                .Select(a => a.ToString())
                .Distinct()
                .ToList();
            askedAt = DateTime.UtcNow;
        }

        /// <summary>The addresses that will work, each with what it is: [url, what].</summary>
        public List<(string Url, string What)> Urls()
        {
            var scheme = Https ? "https" : "http";
            var list = new List<(string, string)>();
            if (ExtraName.Length > 0)
            {
                var what = AddToDns || NameInDns ? "Host name" : "Host name (requires DNS record)";
                list.Add(($"{scheme}://{ExtraName}:{Port}", what));
                if (ExtraFullName != ExtraName)
                {
                    list.Add(($"{scheme}://{ExtraFullName}:{Port}", AddToDns || NameInDns ? "Full host name" : "Full host name (requires DNS record)"));
                }
            }
            list.Add(($"{scheme}://{ShortName}:{Port}", "Computer name"));
            if (!string.Equals(FullName, ShortName, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(($"{scheme}://{FullName}:{Port}", "Full computer name"));
            }
            foreach (var ip in Addresses())
            {
                list.Add(($"{scheme}://{ip}:{Port}", "IP address"));
            }
            return list;
        }

        public string LocalUrl => $"{(Https ? "https" : "http")}://localhost:{Port}/";

        public static bool ServiceInstalled
        {
            get
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + ServiceName))
                {
                    return key != null;
                }
            }
        }

        /// <summary>A certificate with its private key, still valid, whose subject contains the text, in Local Computer > Personal.</summary>
        public static X509Certificate2 FindCertificate(string subject)
        {
            using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadOnly);
                return store.Certificates.Cast<X509Certificate2>()
                    .Where(c => c.HasPrivateKey && c.NotAfter > DateTime.Now && c.Subject.IndexOf(subject, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(c => c.NotAfter)
                    .FirstOrDefault();
            }
        }

        /// <summary>
        /// Certificates Rabbid Lab could use: in Local Computer > Personal, with their private key, valid now, and
        /// good for a web server (no usage list, or one with server authentication). Not the ones Setup makes.
        /// </summary>
        public static List<X509Certificate2> UsableCertificates()
        {
            using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadOnly);
                return store.Certificates.Cast<X509Certificate2>()
                    .Where(c => c.HasPrivateKey && c.NotBefore <= DateTime.Now && c.NotAfter > DateTime.Now
                        && !string.Equals(c.Subject, Engine.CertSubject, StringComparison.OrdinalIgnoreCase)
                        && c.Extensions.OfType<X509EnhancedKeyUsageExtension>().All(u => u.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.1")))
                    .OrderBy(c => c.GetNameInfo(X509NameType.SimpleName, false), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        /// <summary>Letters, digits, dots and hyphens: a DNS name like ad.contoso.local.</summary>
        public static bool IsDnsName(string name) =>
            name.Length <= 253 && name.Split('.').All(part => part.Length > 0 && part.Length <= 63
                && part.All(ch => char.IsLetterOrDigit(ch) || ch == '-') && part[0] != '-' && part[part.Length - 1] != '-');
    }
}
