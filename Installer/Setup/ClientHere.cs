using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security;
using System.ServiceProcess;
using Microsoft.Win32;

namespace RabbidLab.Setup
{
    /// <summary>
    /// The Rabbid Lab client on this same computer (Install the client here puts it there). Removing the server with
    /// its data would leave it locked to a server that no longer exists, so Setup uninstalls it first (ClientPlan).
    /// </summary>
    internal static class ClientHere
    {
        // The client's own names, as in its installer and Settings.cs.
        private const string Key = @"SOFTWARE\Rabbid Lab\Client";
        private const string PolicyKey = @"SOFTWARE\Policies\Rabbid Lab\Client";
        private const string Service = "RabbidLabClient";
        private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Rabbid Lab Client");

        /// <summary>The client's product code, when it's installed here and connected to the server Setup removes; null otherwise.</summary>
        public static string Find()
        {
            try
            {
                var product = Product();
                return product != null && ClientPlan.Ours(Server(), Ports(), Here) ? product : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SecurityException)
            {
                return null;
            }
        }

        /// <summary>How many other computers have this server's client, and after how many days their lock lets go.</summary>
        public static (int Others, int Days) Others()
        {
            try
            {
                var file = Path.Combine(Engine.DataDir, "clients.json");
                return ClientPlan.Others(File.Exists(file) ? File.ReadAllBytes(file) : null, Environment.MachineName);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SecurityException)
            {
                return (0, ClientPlan.UsualDays);
            }
        }

        /// <summary>
        /// Takes the client off this computer. Its service is stopped first (running, it locks the uninstall again every
        /// minute), then what ties it to the server is cleared, so it's unlocked even if Windows Installer fails.
        /// Null when it's gone, otherwise what to tell the person. Never throws: removing the server goes on.
        /// </summary>
        public static string Uninstall(string product, Action<string> log)
        {
            try
            {
                Stop(log);
                Release();
                var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), $"/x {product} /qn /norestart") { UseShellExecute = false, CreateNoWindow = true };
                int code;
                using (var installer = Process.Start(info))
                {
                    if (!installer.WaitForExit(5 * 60 * 1000))
                    {
                        log("Windows Installer was still uninstalling the client after five minutes. Setup did not wait longer.");
                        return Unlocked("Windows Installer did not finish in five minutes");
                    }
                    code = installer.ExitCode;
                }
                log($"> msiexec.exe /x {product} /qn /norestart ({code})");
                // 0 is done, 3010 is done and wants a restart, 1605 is that it was gone already.
                if (code != 0 && code != 3010 && code != 1605)
                {
                    return Unlocked($"Windows Installer returned {code}");
                }
                Forget(log);
                return null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SecurityException || e is Win32Exception || e is InvalidOperationException)
            {
                log("Could not uninstall the Rabbid Lab client: " + e);
                return $"Setup could not uninstall the Rabbid Lab client from this computer ({e.Message.Trim().TrimEnd('.')}).";
            }
        }

        private static string Unlocked(string why) =>
            $"Setup could not uninstall the Rabbid Lab client from this computer ({why}). It is unlocked: uninstall it in Settings > Apps > Installed apps.";

        private static string Product()
        {
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var list = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
            {
                foreach (var name in list?.GetSubKeyNames() ?? new string[0])
                {
                    using (var entry = list.OpenSubKey(name))
                    {
                        if (name.StartsWith("{", StringComparison.Ordinal) && entry?.GetValue("DisplayName") as string == "Rabbid Lab Client" && entry.GetValue("WindowsInstaller") is int msi && msi == 1)
                        {
                            return name;
                        }
                    }
                }
            }
            return null;
        }

        // Group Policy first, as the client reads it.
        private static string Server()
        {
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                foreach (var path in new[] { PolicyKey, Key })
                {
                    using (var key = machine.OpenSubKey(path))
                    {
                        if (key?.GetValue("Server") is string server && server.Trim().Length > 0)
                        {
                            return server.Trim();
                        }
                    }
                }
            }
            return null;
        }

        // The console's port, and 443 when it answers there too (Settings > Console address).
        private static List<int> Ports()
        {
            using (var saved = Registry.LocalMachine.OpenSubKey(Choices.SavedKey))
            {
                var ports = new List<int> { saved?.GetValue("Port") is int port ? port : Edition.DefaultPort };
                if (saved?.GetValue("Also443") is int also && also == 1)
                {
                    ports.Add(443);
                }
                return ports;
            }
        }

        // Whether a name or address means this computer.
        private static bool Here(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            try
            {
                var mine = new HashSet<IPAddress>(NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address))
                {
                    IPAddress.Loopback, IPAddress.IPv6Loopback,
                };
                return Dns.GetHostAddresses(host).Any(mine.Contains);
            }
            catch (Exception e) when (e is SocketException || e is NetworkInformationException || e is ArgumentException)
            {
                return false;
            }
        }

        private static void Stop(Action<string> log)
        {
            try
            {
                using (var service = new ServiceController(Service))
                {
                    if (service.Status != ServiceControllerStatus.Stopped)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                        log("Stopped the client's service.");
                    }
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.ServiceProcess.TimeoutException || e is Win32Exception)
            {
                // No service, or it didn't stop: Windows Installer stops it too.
                log("The client's service was not stopped: " + e.Message);
            }
        }

        // The client as if it had never joined: nothing left that locks it, and the installer told it may go.
        private static void Release()
        {
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = machine.CreateSubKey(Key))
            {
                foreach (var name in new[] { "LastContact", "UninstallCode", "UnlockAfterDays", "CodeUnlockedUntil", "CodeUsed", "RemovalUntil" })
                {
                    key.DeleteValue(name, throwOnMissingValue: false);
                }
                key.SetValue("RemovalAllowed", "yes", RegistryValueKind.String);
            }
            var identity = Path.Combine(DataDir, "identity.dat");
            if (File.Exists(identity))
            {
                File.Delete(identity);
            }
        }

        // What the client's installer leaves for a reinstall (the server's address, its files): they point at a server that's gone.
        private static void Forget(Action<string> log)
        {
            try
            {
                using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    machine.DeleteSubKeyTree(Key, throwOnMissingSubKey: false);
                    bool empty;
                    using (var vendor = machine.OpenSubKey(@"SOFTWARE\Rabbid Lab"))
                    {
                        empty = vendor != null && vendor.SubKeyCount == 0 && vendor.ValueCount == 0;
                    }
                    if (empty)
                    {
                        machine.DeleteSubKey(@"SOFTWARE\Rabbid Lab", throwOnMissingSubKey: false);
                    }
                }
                if (Directory.Exists(DataDir))
                {
                    Directory.Delete(DataDir, recursive: true);
                }
                log("Uninstalled the Rabbid Lab client from this computer.");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SecurityException)
            {
                log("Uninstalled the Rabbid Lab client. Some of its leftovers could not be deleted: " + e.Message);
            }
        }
    }
}
