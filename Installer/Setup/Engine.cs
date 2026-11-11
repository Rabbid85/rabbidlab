using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Win32;

namespace RabbidLab.Setup
{
    /// <summary>
    /// Installs, upgrades and removes Rabbid Lab: the program, the certificate, console.json, the Windows
    /// Firewall rule, the Windows service, the shortcuts the person asked for, and its entry in Settings > Apps.
    /// Each step reports what it's doing; everything is also written to Setup\install.log.
    /// </summary>
    /// <summary>How to get Rabbid Lab's service past what stopped it: for the last page of Setup.</summary>
    public sealed class StartFix
    {
        public string Title = "Rabbid Lab didn't start";
        public string Summary = "";
        public List<string> Steps = new List<string>();

        /// <summary>Something to copy (a command, or the program's hash), and what it is.</summary>
        public string CopyLabel;
        public string CopyText;

        /// <summary>Setup can add the Microsoft Defender exclusion itself and start the service (Allow in Microsoft Defender).</summary>
        public bool CanAllow;

        /// <summary>The button's words: Allow in Microsoft Defender, or Try again once the exclusion is in.</summary>
        public string AllowText;

        /// <summary>Try again only starts the service; Defender is left as it is.</summary>
        public bool Retry;
    }

    /// <summary>Rabbid Lab is installed, but Windows would not start its service; Fix says how to fix it.</summary>
    public sealed class ServiceStartException : InvalidOperationException
    {
        public StartFix Fix { get; }

        public ServiceStartException(StartFix fix, Exception inner) : base(fix.Summary, inner) => Fix = fix;
    }

    public sealed class Engine
    {
        public const string RuleName = Edition.Name;
        public const string CertSubject = Edition.CertSubject;
        public const string CertFriendlyName = Edition.Name + " console";
        public static readonly string DataDir = Edition.DataDir;
        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        /// <summary>What happened adding the name of their own to DNS (or taking it out), for the last page and the log. Null when not asked.</summary>
        public string DnsMessage { get; private set; }

        /// <summary>The alias Setup added (name|zone|domain controller|target), kept so removing everything takes it out.</summary>
        private string dnsAlias;

        /// <summary>An update failed and the service it stopped could not be started again: said on the last page.</summary>
        public bool ServiceLeftStopped { get; private set; }

        /// <summary>Removing: files Setup could not delete, for the last page. Null when it deleted them all.</summary>
        public string LeftoverMessage { get; private set; }

        /// <summary>The log of what this Engine last did: Setup\install.log, or uninstall.log in the data folder.</summary>
        public string LogPath { get; private set; }

        /// <summary>Setup's log of an installation.</summary>
        public static string InstallLog(string installDir) => Path.Combine(installDir, "Setup", "install.log");

        /// <summary>
        /// Why Setup won't install there, or null when it will: a folder that already has other files in it isn't
        /// Rabbid Lab's to fill, or to delete when Rabbid Lab is removed.
        /// </summary>
        public static string FolderProblem(string installDir)
        {
            try
            {
                if (!Directory.Exists(installDir) || File.Exists(Path.Combine(installDir, "RabbidLab.Server.exe")))
                {
                    return null;
                }
                // Setup's own folder may be there from an installation that didn't finish.
                var others = Directory.EnumerateFileSystemEntries(installDir).Any(e => !string.Equals(Path.GetFileName(e), "Setup", StringComparison.OrdinalIgnoreCase));
                return others ? $"The folder {installDir} already contains other files. Choose an empty folder or a new one." : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return $"Setup cannot use the folder {installDir}: {e.Message}";
            }
        }

        /// <summary>Where Setup moved itself to, when it was running from inside the folder it removed.</summary>
        public static string MovedSelf { get; private set; }

        private readonly Action<string, double> report;
#pragma warning disable CS0649 // only the preview build never writes a log
        private StreamWriter log;
#pragma warning restore CS0649

        /// <param name="report">What's happening now, and how far along (0 to 1).</param>
        public Engine(Action<string, double> report) => this.report = report ?? ((_, __) => { });

        // --- Installing -----------------------------------------------------------------------------------

        public void Install(Choices c)
        {
#if PREVIEW
            throw new InvalidOperationException("This is a preview build of Setup. It displays the setup pages only and does not install Rabbid Lab.");
#else
            // A new installation only: an update repairs the folder it's already in, whatever is missing from it.
            if (c.InstalledVersion == null && FolderProblem(c.InstallDir) is string problem)
            {
                throw new InvalidOperationException(problem);
            }
            Directory.CreateDirectory(Path.Combine(c.InstallDir, "Setup"));
            using (log = OpenLog(InstallLog(c.InstallDir)))
            {
                Log($"--- Rabbid Lab {Version} setup, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, as {Environment.UserDomainName}\\{Environment.UserName}");
                try
                {
                    Step("Stopping the Rabbid Lab service", 0.02);
                    StopService();

                    Step("Copying program files", 0.05);
                    CopyFiles(c);

                    if (c.Https)
                    {
                        Step("Configuring the HTTPS certificate", 0.72);
                        PrepareCertificate(c);
                    }

                    Step("Saving the web address configuration", 0.78);
                    WriteConsoleJson(c);

                    Step("Configuring the Windows Firewall rule", 0.81);
                    SetFirewall(c);

                    ReplaceOldAlias(c);
                    if (c.AddToDns && c.ExtraName.Length > 0 && Choices.Domain.Length > 0)
                    {
                        Step($"Creating the DNS alias (CNAME) record for {c.ExtraFullName}", 0.82);
                        AddToDns(c);
                    }

                    Step("Installing the Rabbid Lab service", 0.84);
                    InstallService(c);

                    Step("Creating shortcuts and registering the application", 0.88);
                    SetShortcuts(c);
                    Register(c);

                    if (c.AllowInDefender && DefenderWouldBlock(c.InstallDir))
                    {
                        Step("Allowing Rabbid Lab in Microsoft Defender", 0.89);
                        AllowBeforeStarting(c.InstallDir);
                    }

                    Step("Starting the Rabbid Lab service", 0.9);
                    StartService(c.Port, Path.Combine(c.InstallDir, "RabbidLab.Server.exe"), excluded: allowed);

                    Step("Completed", 1);
                    Log("Installation completed.");
                }
                catch (Exception e)
                {
                    Log("FAILED: " + e);
                    if (!(e is ServiceStartException))
                    {
                        StartAfterFailure();
                    }
                    throw;
                }
            }
#endif
        }

        private void CopyFiles(Choices c)
        {
            using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            {
                if (payload == null)
                {
                    throw new InvalidOperationException("This copy of Setup does not contain the Rabbid Lab program files, and might be incomplete or damaged. Download Setup again from the Rabbid Lab releases on GitHub.");
                }
                using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
                {
                    var files = zip.Entries.Where(e => e.Name.Length > 0).ToList();
                    var root = Path.GetFullPath(c.InstallDir) + Path.DirectorySeparatorChar;
                    for (var i = 0; i < files.Count; i++)
                    {
                        var target = Path.GetFullPath(Path.Combine(c.InstallDir, files[i].FullName));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // never write outside the install folder
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        Retry(() => files[i].ExtractToFile(target, overwrite: true));
                        if (i % 10 == 0)
                        {
                            report("Copying program files", 0.05 + 0.65 * i / files.Count);
                        }
                    }
                    Log($"Copied {files.Count} files to {c.InstallDir}.");
                }
            }


            // A copy of Setup stays behind: it removes Rabbid Lab later, and restarts, stops and starts the service.
            var self = Assembly.GetExecutingAssembly().Location;
            var kept = Path.Combine(c.InstallDir, "Setup", "RabbidLab-Setup.exe");
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(kept), StringComparison.OrdinalIgnoreCase))
            {
                Retry(() => File.Copy(self, kept, overwrite: true));
            }
        }

        /// <summary>
        /// HTTPS: a certificate Setup makes (for the server's names, its IP addresses and their own name), trusted on
        /// the server itself; or one already on the server. A certificate Setup made before is kept while it still
        /// covers every name and has more than 30 days left.
        /// </summary>
        private void PrepareCertificate(Choices c)
        {
            if (c.CertificateSubject.Length > 0)
            {
                var found = Choices.FindCertificate(c.CertificateSubject)
                    ?? throw new InvalidOperationException($"Setup could not find a valid certificate with a private key whose subject contains \"{c.CertificateSubject}\" in the Local Computer > Personal certificate store. Import the certificate with its private key, or select a different certificate.");
                Log($"Using the certificate {found.Subject} ({found.Thumbprint}).");
                return;
            }

            var names = new List<string> { Choices.FullName, Choices.ShortName, "localhost" };
            if (c.ExtraName.Length > 0)
            {
                names.Insert(0, c.ExtraFullName.ToLowerInvariant());
                names.Insert(0, c.ExtraName.ToLowerInvariant());
            }
            names = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var ips = Choices.Addresses();

            using (var my = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            using (var trusted = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
            {
                my.Open(OpenFlags.ReadWrite);
                trusted.Open(OpenFlags.ReadWrite);
                var mine = my.Certificates.Cast<X509Certificate2>().Where(x => x.Subject == CertSubject && x.FriendlyName == CertFriendlyName).ToList();
                var keep = mine.FirstOrDefault(x => x.NotAfter > DateTime.Now.AddDays(30) && Covers(x, names, ips));
                if (keep != null)
                {
                    Log($"Keeping the existing certificate created by Setup ({keep.Thumbprint}).");
                    return;
                }
                foreach (var old in mine)
                {
                    DeletePrivateKey(old);
                    my.Remove(old);
                    foreach (var t in trusted.Certificates.Find(X509FindType.FindByThumbprint, old.Thumbprint, false))
                    {
                        trusted.Remove(t);
                    }
                }

                using (var rsa = RSA.Create(2048))
                {
                    var request = new CertificateRequest(CertSubject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    var san = new SubjectAlternativeNameBuilder();
                    names.ForEach(san.AddDnsName);
                    ips.ForEach(ip => san.AddIpAddress(System.Net.IPAddress.Parse(ip)));
                    request.CertificateExtensions.Add(san.Build());
                    request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
                    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                    using (var made = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5)))
                    {
                        // Into the computer's store with its private key, which only administrators and SYSTEM can read.
                        var stored = new X509Certificate2(made.Export(X509ContentType.Pfx, ""), "",
                            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet) { FriendlyName = CertFriendlyName };
                        my.Add(stored);
                        trusted.Add(new X509Certificate2(made.RawData));
                        Log($"Created a certificate for {string.Join(", ", names.Concat(ips))} ({stored.Thumbprint}) and trusted it on this computer.");
                    }
                }
            }
        }

        private static bool Covers(X509Certificate2 cert, List<string> names, List<string> ips)
        {
            var san = cert.Extensions.Cast<X509Extension>().FirstOrDefault(e => e.Oid.Value == "2.5.29.17");
            if (san == null)
            {
                return false;
            }
            // "DNS Name=rabbidlab, IP Address=192.168.2.19": each value whole, so rabbidlab isn't found in rabbidlab2.
            var covered = new HashSet<string>(san.Format(false).Split(',', '\r', '\n').Select(entry => entry.Substring(entry.IndexOf('=') + 1).Trim()), StringComparer.OrdinalIgnoreCase);
            return names.All(covered.Contains) && ips.All(covered.Contains);
        }

        /// <summary>
        /// console.json next to the console: listen on every network card on the port, and which certificate. With
        /// Redirect, also port 80, which only sends people on to that address.
        /// </summary>
        private void WriteConsoleJson(Choices c)
        {
            string Json(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            var url = $"{(c.Https ? "https" : "http")}://*:{c.Port}";
            var cert = !c.Https ? "" : ",\r\n        \"Certificate\": { \"Subject\": " + Json(c.CertificateSubject.Length > 0 ? c.CertificateSubject : CertSubject.Substring(3))
                + ", \"Store\": \"My\", \"Location\": \"LocalMachine\", \"AllowInvalid\": true }";
            var redirect = c.Redirect && c.Port != 80 ? ",\r\n  \"Redirect\": { \"Port\": 80 }" : "";
            var json = "{\r\n  \"Kestrel\": {\r\n    \"Endpoints\": {\r\n      \"Console\": {\r\n        \"Url\": " + Json(url) + cert + "\r\n      }\r\n    }\r\n  }" + redirect + "\r\n}\r\n";
            File.WriteAllText(Path.Combine(c.InstallDir, "console.json"), json, new UTF8Encoding(false));
            Log($"The console listens on {url}" + (redirect.Length > 0 ? ", and on port 80 to redirect to it." : "."));
        }

        private void SetFirewall(Choices c)
        {
            Run("netsh.exe", $"advfirewall firewall delete rule name=\"{RuleName}\"", okCodes: new[] { 0, 1 });
            if (c.Firewall)
            {
                var exe = Path.Combine(c.InstallDir, "RabbidLab.Server.exe");
                var ports = c.Redirect && c.Port != 80 ? $"{c.Port},80" : c.Port.ToString();
                Run("netsh.exe", $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={ports} "
                    + $"program=\"{exe}\" profile=domain,private enable=yes description=\"Allows other computers to connect to the Rabbid Lab console.\"");
                Log($"Windows Firewall allows inbound TCP port {ports} on domain and private networks.");
            }
            else
            {
                Log("Windows Firewall was not changed.");
            }
        }

        private void InstallService(Choices c)
        {
            var exe = Path.Combine(c.InstallDir, "RabbidLab.Server.exe");
            var bin = $"binPath= \"\\\"{exe}\\\"\"";
            if (Choices.ServiceInstalled)
            {
                Run("sc.exe", $"config {Choices.ServiceName} {bin} start= delayed-auto DisplayName= \"{Edition.Name} Server\"");
            }
            else
            {
                Run("sc.exe", $"create {Choices.ServiceName} {bin} start= delayed-auto DisplayName= \"{Edition.Name} Server\"");
            }
            Run("sc.exe", $"description {Choices.ServiceName} \"Hosts the {Edition.Name} web console: one page for a homelab, in a browser.\"");
            Run("sc.exe", $"failure {Choices.ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/300000");
            Log("The Rabbid Lab service runs as Local System, starts automatically with Windows (delayed start), and restarts if it stops.");
        }

        private void StartService(int port, string exe, bool excluded = false)
        {
            var since = DateTime.UtcNow;
            using (var service = new ServiceController(Choices.ServiceName))
            {
                // Only start it when it's stopped. Starting one that's already starting fails with "already running".
                if (service.Status == ServiceControllerStatus.Stopped)
                {
                    try
                    {
                        service.Start();
                    }
                    catch (InvalidOperationException e) when (e.InnerException is System.ComponentModel.Win32Exception w)
                    {
                        if (w.NativeErrorCode != 1056) // 1056: something else started it a moment ago, which is fine
                        {
                            throw new ServiceStartException(StartProblem(w, exe, excluded), e);
                        }
                    }
                }
                // Wait for it to answer on its port. If it stops in the meantime it crashed, so say why straight away.
                var until = DateTime.UtcNow.AddSeconds(90);
                while (DateTime.UtcNow < until)
                {
                    service.Refresh();
                    if (service.Status == ServiceControllerStatus.Stopped)
                    {
                        throw new ServiceStartException(Stopped(exe, since), null);
                    }
                    if (service.Status == ServiceControllerStatus.Running && Answers(port))
                    {
                        Log("The service is running and responding.");
                        return;
                    }
                    Thread.Sleep(500);
                }
            }
            Log($"The service is running but nothing answers on port {port}.");
            throw new ServiceStartException(Fix("silent", null, exe, null, null, true, port.ToString()), null);
        }

        /// <summary>Whether something answers on the port on this computer within two seconds.</summary>
        private static bool Answers(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    return client.ConnectAsync("localhost", port).Wait(2000);
                }
            }
            catch (Exception e) when (e is SocketException || (e is AggregateException a && a.InnerException is SocketException))
            {
                return false;
            }
        }

        /// <summary>The service started and then stopped: the fix, with the error it stopped on.</summary>
        private StartFix Stopped(string exe, DateTime since)
        {
            string reason = null;
            for (var i = 0; i < 8 && reason == null; i++)
            {
                Thread.Sleep(500); // .NET writes the error a moment after the program ends
                reason = CrashReason(Path.GetFileName(exe), since);
            }
            Log("The service started and then stopped" + (reason != null ? ": " + reason : ". Windows recorded no reason."));
            return Fix("stopped", null, exe, null, null, true, reason);
        }

        /// <summary>The unhandled error .NET Runtime recorded for the program since then, or null.</summary>
        private static string CrashReason(string file, DateTime since)
        {
            string reason = null;
            var within = (long) (DateTime.UtcNow - since).TotalMilliseconds + 5000;
            Events("Application", $"*[System[Provider[@Name='.NET Runtime'] and EventID=1026 and TimeCreated[timediff(@SystemTime) <= {within}]]]", newestFirst: true, take: e =>
            {
                var text = e.Properties.Count > 0 ? e.Properties[0].Value as string ?? "" : "";
                if (text.IndexOf(file, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
                // "Exception Info: System.IO.IOException: Failed to bind to address ..." without the type name.
                var said = System.Text.RegularExpressions.Regex.Match(text, @"Exception Info: (?:[\w.`]+: )?(.+)");
                reason = said.Success ? said.Groups[1].Value.Trim() : "an unhandled error";
                return true;
            });
            return reason;
        }

        /// <summary>Each event the query finds in a Windows log, until one is taken. False when the log can't be read.</summary>
        private static bool Events(string logName, string query, bool newestFirst, Func<System.Diagnostics.Eventing.Reader.EventRecord, bool> take)
        {
            try
            {
                var asked = new System.Diagnostics.Eventing.Reader.EventLogQuery(logName, System.Diagnostics.Eventing.Reader.PathType.LogName, query) { ReverseDirection = newestFirst };
                using (var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(asked))
                {
                    for (var e = reader.ReadEvent(); e != null; e = reader.ReadEvent())
                    {
                        using (e)
                        {
                            if (take(e))
                            {
                                break;
                            }
                        }
                    }
                }
                return true;
            }
            catch (Exception ex) when (ex is System.Diagnostics.Eventing.Reader.EventLogException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Who else has the port: a service's display name, or a program's name. Null when the port is free or Rabbid Lab
        /// itself has it. Found by binding to it, then asking Windows which process listens there.
        /// </summary>
        public static string PortUser(int port)
        {
            var dual = Socket.OSSupportsIPv6;
            try
            {
                using (var socket = new Socket(dual ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    if (dual)
                    {
                        socket.DualMode = true;
                    }
                    socket.Bind(new IPEndPoint(dual ? IPAddress.IPv6Any : IPAddress.Any, port));
                    return null;
                }
            }
            catch (SocketException)
            {
                // Taken. By whom comes next.
            }
            try
            {
                using (var listeners = new System.Management.ManagementObjectSearcher(@"root\StandardCimv2", $"SELECT OwningProcess FROM MSFT_NetTCPConnection WHERE LocalPort = {port} AND State = 2"))
                {
                    foreach (System.Management.ManagementBaseObject listener in listeners.Get())
                    {
                        var pid = Convert.ToUInt32(listener["OwningProcess"]);
                        using (var services = new System.Management.ManagementObjectSearcher($"SELECT Name, DisplayName FROM Win32_Service WHERE ProcessId = {pid}"))
                        {
                            foreach (System.Management.ManagementBaseObject service in services.Get())
                            {
                                return string.Equals((string) service["Name"], Choices.ServiceName, StringComparison.OrdinalIgnoreCase) ? null : (string) service["DisplayName"];
                            }
                        }
                        // Process 4 is Windows itself: HTTP.sys, which IIS and other web servers listen through.
                        return pid == 4 ? "the Windows HTTP service (HTTP.sys)" : Process.GetProcessById((int) pid).ProcessName;
                    }
                }
            }
            catch (Exception e) when (e is System.Management.ManagementException || e is System.Runtime.InteropServices.COMException || e is ArgumentException || e is InvalidOperationException || e is UnauthorizedAccessException)
            {
                // Can't tell who.
            }
            return "another program";
        }

        /// <summary>
        /// Why Windows would not start the service, and how to fix it. "Access is denied" means Windows would not run the
        /// program: what blocked it is looked for in what Microsoft Defender, AppLocker and Windows Defender Application Control
        /// recorded in the last 15 minutes (written to the log too). Nothing recorded there is most likely other security
        /// software, such as endpoint protection, which keeps its own records. Excluded: Setup has just excluded it from
        /// Microsoft Defender's attack surface reduction rules on this computer, and that wasn't enough.
        /// </summary>
        private StartFix StartProblem(System.ComponentModel.Win32Exception w, string exe, bool excluded)
        {
            if (w.NativeErrorCode != 5)
            {
                return Fix("error", null, exe, null, null, true, $"{w.Message.TrimEnd('.')} (error {w.NativeErrorCode})");
            }
            Thread.Sleep(1500); // what blocked it is written down a moment later
            var found = Blocked(Path.GetFileName(exe));
            foreach (var (_, text) in found)
            {
                Log("Recorded by Windows: " + text);
            }
            var (source, said) = found.FirstOrDefault(x => x.Source != "scm");
            var rule = said == null ? null : System.Text.RegularExpressions.Regex.Match(said, @"\bID:\s*([0-9A-Fa-f-]{36})").Groups[1].Value;
            var (managedBy, localCounts) = DefenderManagement();
            Log($"Microsoft Defender attack surface reduction is managed by {managedBy ?? "this computer"}; exclusions made on the server {(localCounts ? "count" : "do not count")}.");
            // The program's hash only goes on the pages that show it. Reading it after an attack surface reduction block counts as another block.
            var kind = source ?? "unknown";
            return Fix(kind, rule, exe, kind == "asr" ? null : Hash(exe), managedBy, localCounts, null, excluded);
        }

        private static string Hash(string file)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(file))
                {
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Who sets this computer's attack surface reduction rules ("Intune", "Group Policy", or null for the server itself), and
        /// whether exclusions made on the server still count (not when the policy turns off local merging).
        /// </summary>
        private static (string By, bool LocalCounts) DefenderManagement()
        {
            string by = null;
            var localCounts = true;
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var mdm = hklm.OpenSubKey(@"SOFTWARE\Microsoft\PolicyManager\current\device\Defender"))
                using (var gpo = hklm.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR"))
                using (var policy = hklm.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender"))
                {
                    if ((mdm?.GetValue("AttackSurfaceReductionRules") ?? mdm?.GetValue("AttackSurfaceReductionRules_ProviderSet")) != null)
                    {
                        by = "Intune";
                    }
                    else if (gpo != null && Convert.ToInt32(gpo.GetValue("ExploitGuard_ASR_Rules") ?? 0) == 1)
                    {
                        by = "Group Policy";
                    }
                    if (Convert.ToInt32(policy?.GetValue("DisableLocalAdminMerge") ?? 0) == 1 || Convert.ToInt32(mdm?.GetValue("DisableLocalAdminMerge") ?? 0) == 1)
                    {
                        localCounts = false;
                    }
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException || e is UnauthorizedAccessException || e is IOException)
            {
                // Can't tell: the steps for a server that sets its own.
            }
            return (by, localCounts);
        }

        /// <summary>The attack surface reduction rules by their IDs, for saying which one blocked it.</summary>
        private static readonly Dictionary<string, string> AsrRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["01443614-cd74-433a-b99e-2ecdc07bfc25"] = "Block executable files from running unless they meet a prevalence, age, or trusted list criterion",
            ["c1db55ab-c21a-4637-bb3f-a12568109d35"] = "Use advanced protection against ransomware",
            ["d1e49aac-8f56-4280-b9ba-993a6d77406c"] = "Block process creations originating from PSExec and WMI commands",
            ["e6db77e5-3df2-4cf1-b95a-636979351e5b"] = "Block persistence through WMI event subscription",
            ["c0033c00-d16d-4114-a5a0-dc9b3a7d2ceb"] = "Block use of copied or impersonated system tools",
            ["a8f5898e-1dc8-49a9-9878-85004b8a61e6"] = "Block Webshell creation for Servers",
            ["9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2"] = "Block credential stealing from the Windows local security authority subsystem",
            ["56a863a9-875e-4185-98a7-b882c64b5ce5"] = "Block abuse of exploited vulnerable signed drivers",
        };

        private const string StartIt = "start " + Edition.Name + " from the Start menu (" + Edition.Name + " > Start " + Edition.Name + "), or run Start-Service " + Edition.Id + " in PowerShell.";
        private const string AdminShell = "Run in PowerShell as administrator";

        /// <summary>
        /// The fix, by what blocked it: Microsoft Defender's attack surface reduction (asr), its antivirus (defender), AppLocker,
        /// Windows Defender Application Control (wdac), a crash after starting (stopped), no answer (silent), a refused
        /// exclusion (notallowed), something that recorded nothing (unknown), or another error.
        /// </summary>
        public static StartFix Fix(string source, string rule, string exe, string hash, string managedBy, bool localCounts, string error = null, bool excluded = false)
        {
            var fix = new StartFix();
            var folder = Path.GetDirectoryName(exe);
            var allowCommand = $"Add-MpPreference -AttackSurfaceReductionOnlyExclusions {string.Join(", ", ExclusionPaths(folder).Select(p => "\"" + p + "\""))}; Start-Service {Edition.Id}";
            void HashToCopy()
            {
                if (hash != null)
                {
                    fix.CopyLabel = "SHA-256 hash of RabbidLab.Server.exe";
                    fix.CopyText = hash;
                }
            }
            void Copy(string command)
            {
                fix.CopyLabel = AdminShell;
                fix.CopyText = command;
            }
            void TryAgain()
            {
                fix.CanAllow = true;
                fix.AllowText = "Try again";
                fix.Retry = true;
            }
            switch (source)
            {
                case "asr":
                    var name = rule != null && AsrRules.TryGetValue(rule, out var n) ? n : null;
                    if (excluded && localCounts)
                    {
                        // Excluded, and local exclusions count here: Defender just hadn't applied it yet.
                        fix.Title = "Rabbid Lab didn't start";
                        fix.Summary = "Setup added the Defender exclusion, but Defender still blocked the service. New exclusions can take a few seconds to apply.";
                        TryAgain();
                        fix.Steps.Add("Select Try again.");
                        fix.Steps.Add("If it still won't start, restart the computer, then " + StartIt);
                        break;
                    }
                    fix.Title = "Microsoft Defender blocked Rabbid Lab";
                    fix.Summary = excluded
                        ? "Setup added an exclusion, but Defender still blocked Rabbid Lab. This computer ignores exclusions made on it."
                        : "Rabbid Lab installed fine, but Microsoft Defender won't let it run. "
                            + (string.Equals(rule, "01443614-cd74-433a-b99e-2ecdc07bfc25", StringComparison.OrdinalIgnoreCase)
                                ? "One of its attack surface reduction rules blocks programs that few computers have run yet, and a new install counts as one."
                                : name != null ? $"Its attack surface reduction rule “{name}” stopped the service." : "One of its attack surface reduction rules stopped the service.");
                    if (localCounts)
                    {
                        fix.CanAllow = true;
                        fix.Steps.Add("Select Allow in Microsoft Defender. Setup adds an exclusion for Rabbid Lab on this computer only, then starts it.");
                        fix.Steps.Add("Or run the command below yourself. It does the same thing.");
                        if (managedBy != null)
                        {
                            fix.Steps.Add($"This computer gets its Defender rules from {managedBy}. To allow Rabbid Lab on your other computers too, add {folder} to the attack surface reduction exclusions in {managedBy}.");
                        }
                        Copy(allowCommand);
                    }
                    else if (managedBy == "Intune")
                    {
                        fix.Summary += " Its Defender rules come from Intune, so the exclusion has to be added there.";
                        fix.Steps.Add("In the Intune admin center, go to Endpoint security > Attack surface reduction and open the policy for this computer.");
                        fix.Steps.Add($"Add {folder} under Attack Surface Reduction Only Exclusions, then save.");
                        fix.Steps.Add("Sync this computer: Settings > Accounts > Access work or school > your account > Info > Sync.");
                        fix.Steps.Add("Then " + StartIt);
                    }
                    else if (managedBy == "Group Policy")
                    {
                        fix.Summary += " Its Defender rules come from Group Policy, so the exclusion has to be added there.";
                        fix.Steps.Add("In Group Policy Management, edit the GPO that sets this computer's attack surface reduction rules.");
                        fix.Steps.Add("Go to Computer Configuration > Policies > Administrative Templates > Windows Components > Microsoft Defender Antivirus > Microsoft Defender Exploit Guard > Attack Surface Reduction, and open Exclude files and paths from Attack Surface Reduction Rules.");
                        fix.Steps.Add($"Choose Enabled, then Show. Add {folder} as the value name with 0 as the value, and select OK twice.");
                        fix.Steps.Add("Run the command below on this computer. It applies the policy and starts Rabbid Lab.");
                        Copy("gpupdate /force; Start-Service " + Edition.Id);
                    }
                    else
                    {
                        fix.Summary += " Its Defender rules are managed centrally, so the exclusion has to be added there.";
                        fix.Steps.Add($"Ask whoever manages Defender for your organization to exclude {folder} from the attack surface reduction rules.");
                        fix.Steps.Add("Once that reaches this computer, " + StartIt);
                    }
                    break;
                case "defender":
                    fix.Title = "Microsoft Defender flagged Rabbid Lab";
                    fix.Summary = "Defender Antivirus thinks RabbidLab.Server.exe is a threat and blocked it. If you got Setup from the Rabbid Lab releases on GitHub, that's a false positive.";
                    fix.Steps.Add("Open Windows Security > Virus & threat protection > Protection history, pick the entry for RabbidLab.Server.exe, and choose Actions > Allow on device.");
                    fix.Steps.Add("Run Setup again to put back anything Defender removed.");
                    fix.Steps.Add("Report the false positive at microsoft.com/wdsi/filesubmission so it doesn't happen on other computers.");
                    HashToCopy();
                    break;
                case "applocker":
                    fix.Title = "AppLocker blocked Rabbid Lab";
                    fix.Summary = $"AppLocker on this computer doesn't allow {exe} to run.";
                    fix.Steps.Add("Open the AppLocker policy for this computer: Local Security Policy, or its GPO in Group Policy Management.");
                    fix.Steps.Add("Go to Security Settings > Application Control Policies > AppLocker > Executable Rules and choose Create New Rule.");
                    fix.Steps.Add($"Choose Allow and Everyone, then Path. Enter {folder}\\* and choose Create.");
                    fix.Steps.Add("Run the command below on this computer. It applies the policy and starts Rabbid Lab.");
                    Copy("gpupdate /force; Start-Service " + Edition.Id);
                    break;
                case "wdac":
                    fix.Title = "Application Control blocked Rabbid Lab";
                    fix.Summary = $"This computer's Windows Defender Application Control policy doesn't allow {exe} to run.";
                    fix.Steps.Add($"Ask whoever manages Application Control for your organization to allow Rabbid Lab by its folder, {folder}, or by the hash below.");
                    fix.Steps.Add("Once the new policy is on this computer, " + StartIt);
                    HashToCopy();
                    break;
                case "stopped":
                    fix.Title = "Rabbid Lab didn't start";
                    fix.Summary = error != null ? $"The service started, then crashed with this error: {error}" : "The service started, then stopped. Windows didn't record why.";
                    TryAgain();
                    var inUse = System.Text.RegularExpressions.Regex.Match(error ?? "", @":(\d+): address already in use");
                    if (inUse.Success)
                    {
                        fix.Steps.Add($"Another program is already using port {inUse.Groups[1].Value}. Close it, or run Setup again and pick a different port.");
                    }
                    fix.Steps.Add("Select Try again.");
                    fix.Steps.Add("If it keeps stopping, check the setup log, or look for .NET Runtime errors in Event Viewer > Windows Logs > Application.");
                    break;
                case "silent":
                    fix.Title = "Rabbid Lab isn't responding";
                    fix.Summary = $"The service is running, but nothing answers on port {error}.";
                    TryAgain();
                    fix.Steps.Add("Select Try again.");
                    fix.Steps.Add("If it still doesn't answer, restart the service with the command below.");
                    Copy("Restart-Service " + Edition.Id);
                    break;
                case "notallowed":
                    fix.Title = "Couldn't add the Defender exclusion";
                    fix.Summary = $"Microsoft Defender refused the exclusion: {error}";
                    fix.CanAllow = true;
                    fix.AllowText = "Try again";
                    fix.Steps.Add("Select Try again.");
                    fix.Steps.Add("Or run the command below yourself. It does the same thing.");
                    Copy(allowCommand);
                    break;
                case "error":
                    fix.Summary = $"Windows couldn't start the service: {error}.";
                    fix.Steps.Add("Check Event Viewer > Windows Logs > System for Service Control Manager errors about Rabbid Lab.");
                    fix.Steps.Add("Fix what it says, then " + StartIt);
                    break;
                default:
                    fix.Title = "Something blocked Rabbid Lab";
                    fix.Summary = $"Windows wasn't allowed to run {exe}, but Defender, AppLocker and Application Control didn't log a block. Other security software, like an endpoint protection product, most likely stopped it.";
                    fix.Steps.Add("In that product's console, such as CrowdStrike Falcon, SentinelOne, Sophos or Trend Micro, find the block for RabbidLab.Server.exe on this computer.");
                    fix.Steps.Add($"Allow it by its path, {exe}, or by the hash below.");
                    fix.Steps.Add("Then " + StartIt);
                    HashToCopy();
                    break;
            }
            return fix;
        }

        /// <summary>
        /// Allow in Microsoft Defender: excludes Rabbid Lab from the attack surface reduction rules on this computer (the
        /// person chose to), and starts it. Still blocked: a ServiceStartException with the steps for where Defender is managed.
        /// </summary>
        public void AllowAndStart(Choices c)
        {
#if PREVIEW
            throw new InvalidOperationException("This is a preview build of Setup. It does not change Microsoft Defender.");
#else
            using (log = OpenLog(InstallLog(c.InstallDir)))
            {
                var exe = Path.Combine(c.InstallDir, "RabbidLab.Server.exe");
                Log($"--- Allow in Microsoft Defender, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, as {Environment.UserDomainName}\\{Environment.UserName}");
                try
                {
                    try
                    {
                        AddExclusions(c.InstallDir);
                    }
                    catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
                    {
                        throw new ServiceStartException(Fix("notallowed", null, exe, null, null, true, e.Message.Trim()), e);
                    }
                    StartWhenAllowed(TimeSpan.FromSeconds(20));
                    StartService(c.Port, exe, excluded: true);
                    Log("Installation completed.");
                }
                catch (Exception e)
                {
                    Log("FAILED: " + e);
                    throw;
                }
            }
#endif
        }

#if !PREVIEW
        /// <summary>Set once Allow in Microsoft Defender has added the exclusion.</summary>
        private bool allowed;

        /// <summary>Excludes the Rabbid Lab folder and its Updates folder from the attack surface reduction rules on this computer.</summary>
        private void AddExclusions(string installDir)
        {
            var paths = ExclusionPaths(installDir);
            Run(PowerShell, $"-NoProfile -NonInteractive -Command \"Add-MpPreference -AttackSurfaceReductionOnlyExclusions {string.Join(", ", paths.Select(p => "'" + p + "'"))}\"");
            allowed = true;
            Log($"Excluded {string.Join(" and ", paths)} from the attack surface reduction rules of Microsoft Defender on this computer.");
        }

        /// <summary>
        /// Allow in Microsoft Defender before the service first starts, so it's never blocked. Waits for Defender to report
        /// the exclusion in effect (event 5007): starting a moment before that gets it blocked anyway.
        /// </summary>
        private void AllowBeforeStarting(string installDir)
        {
            var since = DateTime.UtcNow;
            try
            {
                AddExclusions(installDir);
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            {
                Log("Couldn't add the Microsoft Defender exclusion, so the service may be blocked: " + e.Message.Trim());
                return;
            }
            var until = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < until && !DefenderReported(installDir.TrimEnd('\\'), since))
            {
                Thread.Sleep(300);
            }
        }

        /// <summary>Whether Defender has logged the exclusion for the folder since then.</summary>
        private static bool DefenderReported(string folder, DateTime since)
        {
            var reported = false;
            var within = (long) (DateTime.UtcNow - since).TotalMilliseconds + 2000;
            var readable = Events("Microsoft-Windows-Windows Defender/Operational", $"*[System[EventID=5007 and TimeCreated[timediff(@SystemTime) <= {within}]]]", newestFirst: false,
                take: e => reported = (e.FormatDescription() ?? "").IndexOf(@"ASROnlyExclusions\" + folder, StringComparison.OrdinalIgnoreCase) >= 0);
            return reported || !readable; // can't watch for it: go ahead
        }
#endif

        /// <summary>
        /// Whether Defender would block a new Rabbid Lab: this computer has attack surface reduction rules (from Intune, Group
        /// Policy or set here), exclusions made here count, and the folder isn't excluded yet. The rules themselves can't
        /// always be read (Intune's aren't shown), so having any is enough: the exclusion only touches those rules.
        /// </summary>
        public static bool DefenderWouldBlock(string installDir)
        {
            var (managedBy, localCounts) = DefenderManagement();
            if (!localCounts)
            {
                return false;
            }
            var (rules, excluded) = DefenderPreferences();
            var folder = installDir.TrimEnd('\\');
            return (managedBy != null || rules > 0) && !excluded.Any(x => string.Equals(x.TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>How many attack surface reduction rules are set on this computer, and the folders already excluded from them.</summary>
        private static (int Rules, string[] Excluded) DefenderPreferences()
        {
            const string script = "$p = Get-MpPreference; @($p.AttackSurfaceReductionRules_Ids | Where-Object { $_ }).Count; "
                + "@($p.AttackSurfaceReductionOnlyExclusions | Where-Object { $_ -and $_ -notlike 'N/A*' }) -join '|'";
            try
            {
                var lines = Launch(PowerShell, $"-NoProfile -NonInteractive -Command \"{script}\"").Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var rules = lines.Length > 0 && int.TryParse(lines[0].Trim(), out var n) ? n : 0;
                var excluded = lines.Length > 1 ? lines[1].Split('|') : new string[0];
                return (rules, excluded);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                return (0, new string[0]); // no PowerShell or no Defender: nothing to do
            }
        }

        /// <summary>Try again: starts the service and waits until it answers.</summary>
        public void StartAgain(Choices c)
        {
#if PREVIEW
            throw new InvalidOperationException("This is a preview build of Setup. It does not start the service.");
#else
            using (log = OpenLog(InstallLog(c.InstallDir)))
            {
                Log($"--- Try again, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                try
                {
                    if (allowed)
                    {
                        StartWhenAllowed(TimeSpan.FromSeconds(10));
                    }
                    StartService(c.Port, Path.Combine(c.InstallDir, "RabbidLab.Server.exe"), excluded: allowed);
                    Log("Installation completed.");
                }
                catch (Exception e)
                {
                    Log("FAILED: " + e);
                    throw;
                }
            }
#endif
        }

        /// <summary>Tries to start the service every two seconds, quietly, until it starts or the time is up.</summary>
        private static void StartWhenAllowed(TimeSpan forUpTo)
        {
            var until = DateTime.UtcNow + forUpTo;
            while (DateTime.UtcNow < until)
            {
                using (var service = new ServiceController(Choices.ServiceName))
                {
                    if (service.Status != ServiceControllerStatus.Stopped)
                    {
                        return; // running or on its way
                    }
                    try
                    {
                        service.Start();
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        Thread.Sleep(2000);
                    }
                }
            }
        }

        /// <summary>What Allow in Microsoft Defender excludes: the program folder (the service, Setup's copy, the shortcuts) and where updates download.</summary>
        public static string[] ExclusionPaths(string installDir) => new[] { installDir.TrimEnd('\\'), Path.Combine(DataDir, "Updates") };

        /// <summary>Takes out the attack surface reduction exclusions that Allow in Microsoft Defender adds (and the single-program one earlier builds added).</summary>
        public static void RemoveExclusions(string installDir)
        {
            var paths = ExclusionPaths(installDir).Concat(new[] { Path.Combine(installDir, "RabbidLab.Server.exe") });
#if PREVIEW
            throw new InvalidOperationException("This is a preview build of Setup. It does not change Microsoft Defender.");
#else
            var (code, _, error) = Launch(PowerShell, $"-NoProfile -NonInteractive -Command \"Remove-MpPreference -AttackSurfaceReductionOnlyExclusions {string.Join(", ", paths.Select(p => "'" + p + "'"))}\"");
            if (code != 0)
            {
                throw new InvalidOperationException(error.Trim());
            }
#endif
        }

        /// <summary>What Windows recorded about the program being blocked in the last 15 minutes: where, and what it said.</summary>
        private static List<(string Source, string Text)> Blocked(string file)
        {
            var found = new List<(string Source, string Text)>();
            // A log that isn't on this computer, or can't be read, has nothing to add.
            void Look(string logName, string ids, string mentions, Func<int, string> source) =>
                Events(logName, $"*[System[({ids}) and TimeCreated[timediff(@SystemTime) <= 900000]]]", newestFirst: true, take: e =>
                {
                    string text;
                    try
                    {
                        text = e.FormatDescription() ?? "";
                    }
                    catch (System.Diagnostics.Eventing.Reader.EventLogException)
                    {
                        text = "";
                    }
                    if (text.IndexOf(mentions, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var said = string.Join(" ", text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                        found.Add((source(e.Id), $"{logName}, event {e.Id}: {(said.Length > 500 ? said.Substring(0, 500) + "…" : said)}"));
                    }
                    return false;
                });
            Look("Microsoft-Windows-Windows Defender/Operational", "EventID=1121 or EventID=1116 or EventID=1117", file, id => id == 1121 ? "asr" : "defender");
            Look("Microsoft-Windows-AppLocker/EXE and DLL", "EventID=8004", file, _ => "applocker");
            Look("Microsoft-Windows-CodeIntegrity/Operational", "EventID=3077 or EventID=3033 or EventID=3034", file, _ => "wdac");
            Look("System", "EventID=7000 or EventID=7009 or EventID=7023", Edition.Name, _ => "scm");
            return found;
        }

        private void SetShortcuts(Choices c)
        {
            var menu = Edition.StartMenuDir;
            var desktop = Edition.DesktopShortcut;
            var icon = IconFile(c.InstallDir);
            var setup = Path.Combine(c.InstallDir, "Setup", "RabbidLab-Setup.exe");

            if (Directory.Exists(menu))
            {
                Directory.Delete(menu, recursive: true);
            }
            if (c.StartMenu)
            {
                Directory.CreateDirectory(menu);
                WriteUrl(Path.Combine(menu, Edition.Name + ".url"), c.LocalUrl, icon);
                WriteLink(Path.Combine(menu, $"Restart {Edition.Name}.lnk"), setup, "/service restart", icon, $"Restart the {Edition.Name} service");
                WriteLink(Path.Combine(menu, $"Stop {Edition.Name}.lnk"), setup, "/service stop", icon, $"Stop the {Edition.Name} service");
                WriteLink(Path.Combine(menu, $"Start {Edition.Name}.lnk"), setup, "/service start", icon, $"Start the {Edition.Name} service");
                Log("Created Start menu shortcuts to open the console and to restart, stop and start the service.");
            }
            if (File.Exists(desktop))
            {
                File.Delete(desktop);
            }
            if (c.Desktop)
            {
                WriteUrl(desktop, c.LocalUrl, icon);
                Log("Created a desktop shortcut.");
            }
            // The shortcuts' icon is the program's, at the same path as before: without this, Explorer keeps showing the
            // icon it cached for that path, the old version's, after an update that changed it.
            SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>The shortcuts' icon: the icon file, which Defender never blocks, or the program in older builds without one.</summary>
        private static string IconFile(string installDir)
        {
            var icon = Path.Combine(installDir, "RabbidLab.ico");
            return File.Exists(icon) ? icon : Path.Combine(installDir, "RabbidLab.Server.exe");
        }

        private const int ShcneAssocChanged = 0x08000000;

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        private static void WriteUrl(string path, string url, string icon) =>
            File.WriteAllText(path, $"[InternetShortcut]\r\nURL={url}\r\nIconFile={icon}\r\nIconIndex=0\r\n", Encoding.ASCII);

        private static void WriteLink(string path, string target, string args, string icon, string description)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = target;
            link.Arguments = args;
            link.Description = description;
            link.WorkingDirectory = Path.GetDirectoryName(target);
            if (icon != null)
            {
                link.IconLocation = icon + ",0";
            }
            link.Save();
        }

        /// <summary>Settings > Apps, and the answers for next time.</summary>
        /// <summary>
        /// The name of their own into the domain's DNS, as an alias of this computer. If it can't, Setup carries on and
        /// says so at the end: Rabbid Lab works by the server's own name meanwhile.
        /// </summary>
        private void AddToDns(Choices c)
        {
            var name = c.ExtraFullName.ToLowerInvariant();
            try
            {
                DnsMessage = DnsAlias.Add(name, out var server, out var zone, out var created);
                if (created)
                {
                    dnsAlias = string.Join("|", name, zone, server, Choices.FullName.ToLowerInvariant());
                }
                Log(DnsMessage + (created ? $" (zone {zone} on {server})" : ""));
            }
            catch (Exception e)
            {
                DnsMessage = $"Setup could not create the DNS record for {name}: {DnsAlias.Explain(e)}. Create an alias (CNAME) record for {name} that points to {Choices.FullName} on the DNS server.";
                Log(DnsMessage + " " + e);
            }
        }

        /// <summary>
        /// When the name of their own has changed (or gone) since Setup added the old one to DNS, takes the old alias out,
        /// so it doesn't lead here for ever. Only the one Setup added, and only while it still points at this computer.
        /// </summary>
        private void ReplaceOldAlias(Choices c)
        {
            string saved;
            using (var key = Registry.LocalMachine.OpenSubKey(Choices.SavedKey))
            {
                saved = key?.GetValue("DnsAlias") as string;
            }
            var part = saved?.Split('|');
            if (part == null || part.Length != 4 || part[0] == c.ExtraFullName.ToLowerInvariant())
            {
                return;
            }
            Step($"Removing the previous DNS alias record for {part[0]}", 0.82);
            try
            {
                Log(DnsAlias.Remove(part[0], part[1], part[2], part[3]));
                oldAliasGone = true;
            }
            catch (Exception e)
            {
                Log($"Setup could not remove the previous DNS alias record for {part[0]} ({DnsAlias.Explain(e)}). Remove the alias (CNAME) record on the DNS server manually. " + e);
            }
        }

        private bool oldAliasGone;

        private void Register(Choices c)
        {
            var setup = Path.Combine(c.InstallDir, "Setup", "RabbidLab-Setup.exe");
            using (var key = Registry.LocalMachine.CreateSubKey(Choices.UninstallKey))
            {
                key.SetValue("DisplayName", $"{Edition.Name} Server"); // beside Rabbid Lab Client in Settings > Apps
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "Rabbid");
                key.SetValue("DisplayIcon", IconFile(c.InstallDir));
                key.SetValue("InstallLocation", c.InstallDir);
                key.SetValue("UninstallString", $"\"{setup}\" /uninstall");
                key.SetValue("QuietUninstallString", $"\"{setup}\" /uninstall /quiet");
                key.SetValue("URLInfoAbout", Edition.Repository);
                key.SetValue("HelpLink", Edition.Repository + "/issues");
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("EstimatedSize", (int) (FolderSize(c.InstallDir) / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            using (var key = Registry.LocalMachine.CreateSubKey(Choices.SavedKey))
            {
                key.SetValue("Version", Version);
                key.SetValue("InstallDir", c.InstallDir);
                key.SetValue("Port", c.Port, RegistryValueKind.DWord);
                key.SetValue("Https", c.Https ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("Firewall", c.Firewall ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("Redirect", c.Redirect ? 1 : 0, RegistryValueKind.DWord);
                if (dnsAlias != null)
                {
                    key.SetValue("DnsAlias", dnsAlias);
                }
                else if (oldAliasGone)
                {
                    key.DeleteValue("DnsAlias", throwOnMissingValue: false);
                }
                key.SetValue("CertificateSubject", c.CertificateSubject);
                key.SetValue("ExtraName", c.ExtraName);
                key.SetValue("StartMenu", c.StartMenu ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("Desktop", c.Desktop ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        /// <summary>A folder only administrators and SYSTEM can open, as the console makes its own data folder.</summary>
        private static void CreateLockedFolder(string path)
        {
            if (Directory.Exists(path))
            {
                return;
            }
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var sid in new[] { new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            {
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            }
            Directory.CreateDirectory(path, security);
        }

        // --- Removing --------------------------------------------------------------------------------------

        /// <summary>
        /// Removes Rabbid Lab: the service, program, firewall rule, the certificate Setup made (and its private
        /// key), the shortcuts and its entries in Settings > Apps and the registry. With everything, also its
        /// settings, saved logins and encryption keys (%ProgramData%\Rabbid Lab), its Event Log registrations,
        /// Setup's logs and leftover temporary copies: nothing of it is left on the server.
        /// </summary>
        public void Uninstall(bool everything)
        {
#if PREVIEW
            throw new InvalidOperationException("This is a preview build of Setup. It does not remove Rabbid Lab.");
#else
            // The log goes in the data folder: the program folder, where install.log is, gets deleted.
            var logFile = Path.Combine(DataDir, "uninstall.log");
            try
            {
                CreateLockedFolder(DataDir);
                log = OpenLog(logFile);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // No log, then: removing carries on, and the last page says how it went.
            }
            try
            {
                Log($"--- Rabbid Lab {Version} removal, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, as {Environment.UserDomainName}\\{Environment.UserName}");
                Remove(everything);
                Log("Removal completed.");
            }
            catch (Exception e)
            {
                if (log == null)
                {
                    // Closed to delete the data folder, which then would not go: the reason still gets written.
                    try
                    {
                        log = OpenLog(logFile);
                    }
                    catch (Exception again) when (again is IOException || again is UnauthorizedAccessException)
                    {
                        // The last page still says it.
                    }
                }
                Log("FAILED: " + e);
                throw;
            }
            finally
            {
                log?.Dispose();
                log = null;
            }
#endif
        }

#if !PREVIEW
        private void Remove(bool everything)
        {
            string dir;
            string alias;
            using (var saved = Registry.LocalMachine.OpenSubKey(Choices.SavedKey))
            {
                dir = saved?.GetValue("InstallDir") as string ?? Edition.ProgramDir;
                alias = saved?.GetValue("DnsAlias") as string;
            }

            if (everything && alias != null && alias.Split('|').Length == 4)
            {
                // The DNS alias Setup added (never one that was there before).
                var part = alias.Split('|');
                Step($"Removing the DNS alias record for {part[0]}", 0.01);
                try
                {
                    Log(DnsAlias.Remove(part[0], part[1], part[2], part[3]));
                }
                catch (Exception e)
                {
                    DnsMessage = $"Setup could not remove the DNS alias record for {part[0]} ({DnsAlias.Explain(e)}). Remove the alias (CNAME) record on the DNS server manually.";
                    Log(DnsMessage + " " + e);
                }
            }

            Step("Stopping the Rabbid Lab service", 0.05);
            StopService();
            foreach (var p in Process.GetProcessesByName("RabbidLab.Server"))
            {
                // A copy still running from the install folder would keep its files.
                try
                {
                    if (p.MainModule.FileName.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                    {
                        p.Kill();
                        p.WaitForExit(10000);
                    }
                }
                catch (Exception)
                {
                    // Not ours to look at, or already gone.
                }
            }

            Step("Removing the Rabbid Lab service", 0.15);
            if (Choices.ServiceInstalled)
            {
                Run("sc.exe", $"delete {Choices.ServiceName}", okCodes: new[] { 0, 1072 });
            }

            try
            {
                RemoveExclusions(dir);
                Log("Removed the Microsoft Defender exclusions for Rabbid Lab, if there were any.");
            }
            catch (Exception e)
            {
                Log("Could not remove the Microsoft Defender exclusion: " + e.Message);
            }

            Step("Removing the Windows Firewall rule", 0.25);
            Run("netsh.exe", $"advfirewall firewall delete rule name=\"{RuleName}\"", okCodes: new[] { 0, 1 });

            Step("Removing the certificate created by Setup", 0.35);
            RemoveOwnCertificates();

            Step("Removing shortcuts", 0.45);
            var menu = Edition.StartMenuDir;
            if (Directory.Exists(menu))
            {
                Directory.Delete(menu, recursive: true);
            }
            var desktop = Edition.DesktopShortcut;
            if (File.Exists(desktop))
            {
                File.Delete(desktop);
            }

            Step("Removing program files", 0.55);
            if (Directory.Exists(dir))
            {
                // Setup's own copy is in that folder, and Windows won't delete a running program, but it will
                // move one. It goes to the temporary folder and deletes itself there once it closes.
                var self = Assembly.GetExecutingAssembly().Location;
                if (self.StartsWith(Path.GetFullPath(dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) && File.Exists(self))
                {
                    MovedSelf = Path.Combine(Path.GetTempPath(), $"RabbidLab-Remove-{Guid.NewGuid():N}.exe");
                    File.Move(self, MovedSelf);
                }
                RemoveProgramFiles(dir);
            }

            Step("Removing registry entries", 0.75);
            Registry.LocalMachine.DeleteSubKeyTree(Choices.UninstallKey, throwOnMissingSubKey: false);
            Registry.LocalMachine.DeleteSubKeyTree(Choices.SavedKey, throwOnMissingSubKey: false);
            using (var vendor = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Rabbid"))
            {
                var empty = vendor != null && vendor.SubKeyCount == 0 && vendor.ValueCount == 0;
                vendor?.Close();
                if (empty)
                {
                    Registry.LocalMachine.DeleteSubKey(@"SOFTWARE\Rabbid", throwOnMissingSubKey: false);
                }
            }

            if (everything)
            {
                Step("Deleting settings, saved credentials and encryption keys", 0.82);
                // The log is in that folder, so it is closed first and goes with it.
                log?.Dispose();
                log = null;
                if (Directory.Exists(DataDir))
                {
                    Retry(() => Directory.Delete(DataDir, recursive: true));
                }

                Step("Removing event log sources and temporary files", 0.92);
                // The Windows Event Log registrations the console made for its messages (the entries already
                // written stay in the log, as Windows keeps them).
                foreach (var source in Edition.Dev ? new[] { Edition.Name } : new[] { "RabbidLab.Server", "Rabbid Lab", "RabbidLab" })
                {
                    Registry.LocalMachine.DeleteSubKeyTree(@"SYSTEM\CurrentControlSet\Services\EventLog\Application\" + source, throwOnMissingSubKey: false);
                }
                // Setup's own logs, and copies of Setup left from earlier removals.
                var self = Assembly.GetExecutingAssembly().Location;
                foreach (var temp in new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") }.Distinct())
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(temp, "RabbidLab-*"))
                        {
                            if (!string.Equals(file, self, StringComparison.OrdinalIgnoreCase))
                            {
                                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Can't look in that folder; nothing of ours there to worry about.
                    }
                }
            }
            Step("Completed", 1);
        }

        /// <summary>
        /// The program folder. All of it when the program is in it; otherwise only the files Setup put there, so a folder
        /// that holds other things keeps them. Files that will not go are reported, and removing carries on.
        /// </summary>
        private void RemoveProgramFiles(string dir)
        {
            try
            {
                if (File.Exists(Path.Combine(dir, "RabbidLab.Server.exe")))
                {
                    Retry(() => Directory.Delete(dir, recursive: true));
                    return;
                }
                var files = new List<string> { Path.Combine(dir, "console.json"), InstallLog(dir), Path.Combine(dir, "Setup", "RabbidLab-Setup.exe") };
                using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
                {
                    if (payload != null)
                    {
                        using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
                        {
                            files.AddRange(zip.Entries.Where(e => e.Name.Length > 0).Select(e => Path.GetFullPath(Path.Combine(dir, e.FullName))));
                        }
                    }
                }
                var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files.Select(Path.GetFullPath).Where(f => f.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                {
                    if (File.Exists(file))
                    {
                        Retry(() => File.Delete(file));
                    }
                    // Every folder between the file and the program folder: the ones left empty go below.
                    for (var folder = Path.GetDirectoryName(file); folder != null && folder.Length >= root.Length; folder = Path.GetDirectoryName(folder))
                    {
                        folders.Add(folder);
                    }
                }
                // Deepest first, then the program folder itself.
                foreach (var folder in folders.OrderByDescending(f => f.Length).Concat(new[] { dir }))
                {
                    if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    {
                        Directory.Delete(folder);
                    }
                }
                if (Directory.Exists(dir))
                {
                    LeftoverMessage = $"The folder {dir} contains files that are not part of Rabbid Lab, so Setup did not delete it.";
                    Log(LeftoverMessage);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                LeftoverMessage = $"Some files in {dir} could not be deleted ({e.Message.Trim().TrimEnd('.')}). Delete the folder manually.";
                Log(LeftoverMessage + " " + e);
            }
        }
#endif

        /// <summary>The certificates Setup made, their private keys, and the copies trusted on this computer.</summary>
        private static void RemoveOwnCertificates()
        {
            using (var my = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            using (var trusted = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
            {
                my.Open(OpenFlags.ReadWrite);
                trusted.Open(OpenFlags.ReadWrite);
                foreach (var cert in my.Certificates.Cast<X509Certificate2>().Where(x => x.Subject == CertSubject && x.FriendlyName == CertFriendlyName).ToList())
                {
                    DeletePrivateKey(cert);
                    foreach (var t in trusted.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false))
                    {
                        trusted.Remove(t);
                    }
                    my.Remove(cert);
                }
            }
        }

        /// <summary>Taking a certificate out of the store leaves its private key file behind; this deletes the key.</summary>
        private static void DeletePrivateKey(X509Certificate2 cert)
        {
            try
            {
                using (var key = cert.GetRSAPrivateKey())
                {
                    if (key is RSACng cng)
                    {
                        cng.Key.Delete();
                    }
                    else if (key is RSACryptoServiceProvider csp)
                    {
                        csp.PersistKeyInCsp = false;
                        csp.Clear();
                    }
                }
            }
            catch (CryptographicException)
            {
                // Already gone, or not readable: removing the certificate still takes it out of use.
            }
        }

        // --- The service, from the Start menu shortcuts ------------------------------------------------------

        /// <summary>restart, stop or start. Returns what happened, in a sentence.</summary>
        public static string ControlService(string action)
        {
            if (!Choices.ServiceInstalled)
            {
                return "Rabbid Lab isn't installed on this computer. Run Rabbid Lab Setup to install it.";
            }
            using (var service = new ServiceController(Choices.ServiceName))
            {
                if (action == "stop" || action == "restart")
                {
                    if (service.Status != ServiceControllerStatus.Stopped)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
                    }
                    if (action == "stop")
                    {
                        return "Rabbid Lab is stopped. It starts again when you start it from the Start menu or restart the computer.";
                    }
                }
                if (service.Status == ServiceControllerStatus.Stopped)
                {
                    service.Start();
                }
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
                return action == "restart" ? "Rabbid Lab restarted. Give it a few seconds before you open it." : "Rabbid Lab is running.";
            }
        }


        // --- Helpers -----------------------------------------------------------------------------------------

        private void StopService()
        {
            if (!Choices.ServiceInstalled)
            {
                return;
            }
            using (var service = new ServiceController(Choices.ServiceName))
            {
                if (service.Status != ServiceControllerStatus.Stopped)
                {
                    stoppedService = true;
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
                    Log("Stopped the service.");
                }
            }
        }

        /// <summary>Set when Setup stopped a running service, so a failed update knows to start it again.</summary>
        private bool stoppedService;

        /// <summary>An update that failed: the service Setup stopped is started again, so the console isn't left down.</summary>
        private void StartAfterFailure()
        {
            if (!stoppedService)
            {
                return;
            }
            try
            {
                using (var service = new ServiceController(Choices.ServiceName))
                {
                    if (service.Status == ServiceControllerStatus.Stopped)
                    {
                        service.Start();
                    }
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    Log("Started the service again.");
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.ServiceProcess.TimeoutException || e is System.ComponentModel.Win32Exception)
            {
                ServiceLeftStopped = true;
                Log("The service is stopped, and Setup could not start it again: " + e.Message);
            }
        }

        private StreamWriter OpenLog(string path)
        {
            LogPath = path;
            return new StreamWriter(path, append: true, Encoding.UTF8);
        }

        private void Step(string what, double progress)
        {
            report(what, progress);
            Log(what + "...");
        }

        private void Log(string line)
        {
            log?.WriteLine(line);
            log?.Flush();
        }

        private void Run(string exe, string args, int[] okCodes = null)
        {
            var (code, output, error) = Launch(exe, args);
            var said = (output + error).Trim();
            Log($"> {exe} {args} ({code}) {said}");
            if (!(okCodes ?? new[] { 0 }).Contains(code))
            {
                throw new InvalidOperationException($"The command {exe} {args.Split(' ')[0]} failed with exit code {code}. {said}");
            }
        }

        private const string PowerShell = @"WindowsPowerShell\v1.0\powershell.exe";

        /// <summary>Runs a program from System32 without a window and waits for it: its exit code, and what it wrote.</summary>
        private static (int Code, string Output, string Error) Launch(string exe, string args)
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, exe), args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var p = Process.Start(info))
            {
                // Errors are read alongside, so a program that fills one pipe isn't left waiting on the other.
                var error = p.StandardError.ReadToEndAsync();
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return (p.ExitCode, output, error.Result);
            }
        }

        /// <summary>A file that's briefly in use (the service letting go of it) gets a few more tries.</summary>
        private static void Retry(Action action)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException) when (attempt < 10)
                {
                    Thread.Sleep(500);
                }
                catch (UnauthorizedAccessException) when (attempt < 10)
                {
                    Thread.Sleep(500);
                }
            }
        }

        private static long FolderSize(string dir) =>
            new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
    }
}
