using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SetupWindow = RabbidLab.Setup.MainWindow;

namespace RabbidLab.Setup
{
    /// <summary>
    /// RabbidLab-Setup.exe:
    ///   (nothing)                     install or update, in the window (RabbidLab.xml next to it fills it in)
    ///   /config=RabbidLab.xml         use that answer file; with Quiet="yes" in it, a quiet install
    ///   /quiet /acceptlicense [/port=8420] [/name=lab.home.lan] [/adddns] [/certsubject=...] [/http] [/nofirewall] [/noredirect]
    ///          [/dir=...] [/nostartmenu] [/desktop] [/log=file]     install or update without the window
    ///   /uninstall [/quiet] [/everything] [/log=file]    remove it; /everything leaves nothing behind
    ///   /service restart|stop|start                       the shortcut uses start when it finds the service stopped
    ///   --snapshot folder                                 pictures of every page, for checking how they look
    /// RabbidLab-Uninstall.exe (the same program built with -p:Uninstaller=true) only removes.
    /// Quiet exit codes: 0 done, 1 something failed (see the log), 2 the license was not accepted.
    /// A quiet install needs /acceptlicense or AcceptLicense="yes" in the answer file; the updater passes it.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>The command line Setup started with, handed on to a newer Setup (SetupUpdate.Run).</summary>
        public static string[] StartArgs = new string[0];

        /// <summary>The × in a clearable text box: empties it and leaves the cursor in it.</summary>
        private void ClearBox_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.TemplatedParent is TextBox box)
            {
                box.Clear();
                box.Focus();
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var args = e.Args;
            StartArgs = args;
            bool Has(string name) => args.Any(a => Choices.SplitArg(a).Name == name);
            var quiet = Has("quiet") || Has("silent") || Has("q");

            var snapshot = Array.IndexOf(args, "--snapshot");
            if (snapshot >= 0 && snapshot + 1 < args.Length)
            {
                var snapshotRemoving = Has("uninstall");
                var snapshotChoices = Choices.Load(args);
#if PREVIEW
                // /previewinstalled=0.6.47: as if that version were installed (the update pages); none: as if it were not.
                var installed = args.Select(Choices.SplitArg).FirstOrDefault(a => a.Name == "previewinstalled").Value;
                if (!string.IsNullOrEmpty(installed))
                {
                    snapshotChoices.InstalledVersion = installed == "none" ? null : installed; // none: a new installation
                }
#endif
                new SetupWindow(snapshotRemoving ? SetupWindow.Mode.Uninstall : SetupWindow.Mode.Install, snapshotChoices).Snapshot(args[snapshot + 1]);
                Shutdown(0);
                return;
            }

            var service = Array.FindIndex(args, a => Choices.SplitArg(a).Name == "service");
            if (service >= 0)
            {
                var action = service + 1 < args.Length ? args[service + 1].ToLowerInvariant() : "restart";
                // The window shows straight away; stopping and starting can take a minute each.
                var doing = action == "stop" ? "Stopping" : action == "restart" ? "Restarting" : "Starting";
                Say($"{doing} the {Edition.Name} service…", () =>
                {
                    try
                    {
                        return Engine.ControlService(action);
                    }
                    catch (Exception ex)
                    {
                        return $"Unable to {action} the Rabbid Lab service: {ex.Message}\n\nFor more information, see Event Viewer > Windows Logs > Application.";
                    }
                });
                Shutdown(0);
                return;
            }

            var logArg = args.Select(Choices.SplitArg).FirstOrDefault(a => a.Name == "log").Value;
            var log = string.IsNullOrEmpty(logArg) ? Path.Combine(Path.GetTempPath(), "RabbidLab-Setup.log") : logArg;

            // When Setup removed the folder it was running from, it moved itself to the temporary folder; it deletes
            // that copy a moment after it closes.
            Exit += (_, __) =>
            {
                if (Engine.MovedSelf != null)
                {
                    CleanUpTempCopy(Engine.MovedSelf);
                }
            };

#if UNINSTALLER
            var removing = true; // RabbidLab-Uninstall.exe only removes
#else
            var removing = Has("uninstall");
#endif
            if (removing)
            {
#if UNINSTALLER
                var everything = !Has("keepdata"); // the uninstaller leaves nothing behind unless told to
#else
                var everything = Has("everything") || Has("deletedata");
#endif
                if (quiet)
                {
                    var remover = new Engine(null);
                    var removed = Quietly(log, everything ? "Removing Rabbid Lab and all associated data" : "Removing Rabbid Lab and retaining its settings",
                        () => remover.Uninstall(everything));
                    // What it could not take out, and has to be removed by hand.
                    foreach (var left in new[] { remover.LeftoverMessage, remover.DnsMessage }.Where(m => m != null))
                    {
                        Write(log, left);
                    }
                    Shutdown(removed);
                    return;
                }
                MainWindow = new SetupWindow(SetupWindow.Mode.Uninstall, new Choices());
                MainWindow.Show();
                return;
            }

            Choices choices;
            try
            {
                choices = Choices.Load(args);
            }
            catch (Exception ex) when (ex is IOException || ex is System.Xml.XmlException || ex is InvalidDataException)
            {
                // A broken or missing answer file: say so, quietly or not.
                Write(log, "Installation did not start. Setup cannot use the answer file. " + ex.Message);
                if (!quiet)
                {
                    Say("Setup cannot use the answer file. " + ex.Message + "\n\nCorrect the answer file, and then run Setup again.");
                }
                Shutdown(1);
                return;
            }

            // An answer file named with /config= can make it quiet itself.
            quiet = quiet || (choices.ConfigNamed && choices.Quiet);
            if (quiet)
            {
                // Quiet installs can't show the license, so it has to be accepted on the command line or in the answer file.
                if (!Has("acceptlicense") && !choices.AcceptLicense)
                {
                    Write(log, "Installation did not start. A quiet installation requires acceptance of the license agreement. Specify /acceptlicense, or set AcceptLicense=\"yes\" in the answer file. The license agreement is in LICENSE.txt.");
                    Shutdown(2);
                    return;
                }
                // Quiet installs can't ask about shortcuts, so a desktop shortcut only comes when asked for (/desktop, or
                // Desktop="yes"). An update keeps what it had.
                if (choices.InstalledVersion == null && !Has("desktop") && choices.ConfigFile == null)
                {
                    choices.Desktop = false;
                }
                // A newer Rabbid Lab than this Setup: it does the installation instead, and its exit code is ours.
                if (!Has(SetupUpdate.NoCheck))
                {
                    var newer = SetupUpdate.Latest();
                    if (newer != null && newer.IsNewer)
                    {
                        try
                        {
                            var setup = newer.Download();
                            Write(log, $"Version {newer.Version} is available. Setup downloaded and verified it, and is installing it in place of version {Engine.Version}.");
                            var child = SetupUpdate.Run(setup, args, choices);
                            child.WaitForExit();
                            Shutdown(child.ExitCode);
                            return;
                        }
                        catch (Exception ex) when (ex is IOException || ex is System.Net.WebException || ex is InvalidOperationException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
                        {
                            Write(log, $"Version {newer.Version} is available but could not be downloaded ({ex.Message.TrimEnd('.')}). Installing version {Engine.Version} instead. Rabbid Lab updates itself to the latest version shortly after it starts.");
                        }
                    }
                }
                var engine = new Engine(null);
                var code = Quietly(log, $"Installing Rabbid Lab {Engine.Version} in quiet mode on port {choices.Port}"
                    + (choices.ConfigFile != null ? $", using the answer file {choices.ConfigFile}" : ""), () => engine.Install(choices));
                if (code == 0 && engine.DnsMessage != null)
                {
                    Write(log, engine.DnsMessage);
                }
                if (code != 0 && engine.ServiceLeftStopped)
                {
                    Write(log, $"The {Edition.Name} service is stopped, and Setup could not start it again.");
                }
                Shutdown(code);
                return;
            }
            MainWindow = new SetupWindow(SetupWindow.Mode.Install, choices);
            MainWindow.Show();
        }

        private static int Quietly(string log, string what, Action action)
        {
            Write(log, what + "...");
            try
            {
                action();
                Write(log, "Completed successfully.");
                return 0;
            }
            catch (Exception ex)
            {
                Write(log, "FAILED: " + ex);
                return 1;
            }
        }

        private static void Write(string log, string line)
        {
            try
            {
                File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}\r\n");
            }
            catch (Exception)
            {
                // Nowhere to write it; the exit code still says what happened.
            }
        }

        /// <summary>The temporary copy that removed Rabbid Lab deletes itself a moment after it closes.</summary>
        private static void CleanUpTempCopy(string self)
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{self}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }

        /// <summary>A small window in Rabbid Lab's style with a message and OK, for /service.</summary>
        /// <remarks>With work: the message shows while it runs off the window's thread, then what it returns, and OK waits for it.</remarks>
        private void Say(string message, Func<string> work = null)
        {
            var ok = new Button { Content = "OK", Style = (Style) FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true };
            var said = new TextBlock { Text = message, FontSize = 14, Foreground = (Brush) FindResource("Muted"), LineHeight = 21, Margin = new Thickness(0, 0, 0, 20) };
            var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
            panel.Children.Add(new TextBlock { Text = Edition.Name, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
            panel.Children.Add(said);
            panel.Children.Add(ok);
            var window = new Window
            {
                Title = Edition.Name,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = (Brush) FindResource("PanelSolid"),
                Content = panel,
            };
            ok.Click += (_, __) => window.Close();
            if (work != null)
            {
                var busy = true;
                ok.IsEnabled = false;
                window.Closing += (_, e) => e.Cancel = busy; // not closed halfway through a restart
                window.ContentRendered += async (_, __) =>
                {
                    said.Text = await Task.Run(work);
                    busy = false;
                    ok.IsEnabled = true;
                };
            }
            window.ShowDialog();
        }
    }
}
