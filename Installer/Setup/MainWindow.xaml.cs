using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace RabbidLab.Setup
{
    /// <summary>
    /// Setup's window. Installing, updating or repairing: one page that says which, with the actions Setup will take on this
    /// computer (Change installation settings opens the settings, each with its own page); then each action as it's taken;
    /// then where to open it. Removing: what to remove, then each action, then the result.
    /// </summary>
    public partial class MainWindow : Window
    {
        public enum Mode { Install, Uninstall }

        private sealed class Page
        {
            public string Key, Step, Next;
            public FrameworkElement Element;
            public Func<bool> Skip = () => false;
        }

        private readonly Mode mode;
        private readonly Choices choices;
        private readonly List<Page> pages;
        private int at;
        private SetupUpdate newer;      // a newer Rabbid Lab than this Setup, found as the window opened
        private bool newerTried;        // its download was tried (and didn't work), so Next installs this version
        private string nextWord = "Update"; // what the button that starts an update says: Update or Repair
        private bool working;
        private bool failed;
        private Engine engine;
        private string editing;         // the settings page opened with Change, which Done returns from
        private bool edited;            // a setting was changed with Change: the update uses what's on the pages

        public MainWindow(Mode mode, Choices choices)
        {
            InitializeComponent();
            if (Edition.Dev)
            {
                Title = "Rabbid Lab Dev Setup";
                WorkTitle.Text = "Installing Rabbid Lab Dev";
                RedirectBox.Visibility = Visibility.Collapsed; // port 80 is the release's
                DevLook.Window(this);
            }
            this.mode = mode;
            this.choices = choices;

            pages = mode == Mode.Install
                ? new List<Page>
                {
                    // New install: the license, one page per choice, then the summary. Update: straight to the summary,
                    // after the license only when it changed.
                    new Page { Key = "license", Step = "License", Next = "Next", Element = LicensePage, Skip = () => licenseAccepted },
                    new Page { Key = "options", Step = "Installation options", Next = "Next", Element = OptionsPage, Skip = () => Updating },
                    new Page { Key = "address", Step = "Console address", Next = "Next", Element = AddressPage, Skip = () => Updating },
                    new Page { Key = "security", Step = "Certificate", Next = "Next", Element = SecurityPage, Skip = () => Updating },
                    new Page { Key = "welcome", Step = "Ready", Next = "Next", Element = WelcomePage },
                    new Page { Key = "settings", Step = "Settings", Next = "Done", Element = SettingsPage, Skip = () => true },
                    new Page { Key = "work", Step = "Install", Next = "", Element = WorkPage },
                    new Page { Key = "done", Step = "", Next = "Finish", Element = DonePage },
                }
                : new List<Page>
                {
                    new Page { Key = "remove", Step = "Confirm", Next = "Remove", Element = RemovePage },
                    new Page { Key = "work", Step = "Remove", Next = "", Element = WorkPage },
                    new Page { Key = "done", Step = "", Next = "Close", Element = DonePage },
                };

            var upgrade = choices.InstalledVersion != null;
            if (mode == Mode.Uninstall)
            {
                Title = "Remove " + Edition.Name;
                WorkTitle.Text = "Removing " + Edition.Name;
                RemoveLead.Text = $"{Edition.Name} {choices.InstalledVersion ?? Engine.Version} is installed on this computer. Select what to remove, and then select Remove.";
                DataPathText.Text = $"Select this option if you plan to reinstall. Settings and saved credentials are retained in {Engine.DataDir} for the new installation.";
            }
            TitleText.Text = Title;
            // Tall enough for the most it shows: a new installation lists every action.
            Height = mode == Mode.Uninstall ? 520 : 560;

            // Welcome
            if (choices.Company != null)
            {
                PresetChip.Visibility = Visibility.Visible;
                PresetText.Text = $"This installation is preconfigured for {choices.Company} from an answer file. Review the actions below before you continue.";
            }
            if (mode == Mode.Install && !App.StartArgs.Contains("--snapshot"))
            {
                var from = App.StartArgs.Select(Choices.SplitArg).FirstOrDefault(a => a.Name == "updatedfrom").Value;
                if (!string.IsNullOrEmpty(from) && !upgrade)
                {
                    ShowNewer($"Setup downloaded the latest version, Rabbid Lab {Engine.Version}.");
                }
                else if (!App.StartArgs.Any(a => Choices.SplitArg(a).Name == SetupUpdate.NoCheck))
                {
                    LookForNewer();
                }
            }


            // License: the agreement this Setup carries. An installed copy with the same one already accepted it.
            var license = LicenseAgreement();
            // Paragraphs rewrapped to the box; the file keeps its own line breaks.
            LicenseText.Text = string.Join("\n\n", license.Replace("\r\n", "\n").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => string.Join(" ", p.Split('\n').Select(l => l.Trim()))));
            licenseAccepted = upgrade && File.Exists(System.IO.Path.Combine(choices.InstallDir, "LICENSE.txt"))
                && File.ReadAllText(System.IO.Path.Combine(choices.InstallDir, "LICENSE.txt")).Trim() == license;

            // Address and security
            PortBox.Text = choices.Port.ToString();
            NameBox.Text = choices.ExtraName;
            DnsBox.IsChecked = choices.AddToDns || choices.ConfigFile == null;
            FirewallBox.IsChecked = choices.Firewall;
            RedirectBox.IsChecked = choices.Redirect;
            FillCertificates();
            (choices.Https ? (choices.CertificateSubject.Length > 0 ? OwnCertChoice : MakeCertChoice) : HttpChoice).IsChecked = true;


            // Shortcuts
            FolderBox.Text = choices.InstallDir;
            if (upgrade)
            {
                // An update stays in the folder it's installed in.
                FolderBox.IsEnabled = FolderBrowse.IsEnabled = false;
                FolderBox.ToolTip = "The installation folder cannot be changed during an update.";
            }
            StartMenuBox.IsChecked = choices.StartMenu;
            DesktopBox.IsChecked = choices.Desktop;
            DefenderBox.IsChecked = choices.AllowInDefender;

            DescribeUpdate();
            Show(FirstShown());
            ContentRendered += (s, e) => sized = true;
            if (mode == Mode.Install && !App.StartArgs.Contains("--snapshot"))
            {
                if (!Edition.Dev)
                {
                    CheckRedirectPort();
                }
                CheckDefender();
            }
        }

        /// <summary>Set when this computer's Defender would block a new Rabbid Lab: the option shows, and the plan lists it.</summary>
        private bool defenderWouldBlock;

        private async void CheckDefender()
        {
            var dir = choices.InstallDir;
            defenderWouldBlock = await Task.Run(() => Engine.DefenderWouldBlock(dir));
            DefenderSection.Visibility = defenderWouldBlock ? Visibility.Visible : Visibility.Collapsed;
            if (pages[at].Key == "welcome")
            {
                DrawPlan(); // the summary was drawn before the answer came
            }
        }

        /// <summary>Port 80 is another program's: the redirect can't work there, so it's off and says who has it.</summary>
        private async void CheckRedirectPort() => ShowPortUser(await Task.Run(() => Engine.PortUser(80)));

        private void ShowPortUser(string user)
        {
            if (user == null)
            {
                return;
            }
            RedirectBox.IsChecked = false;
            RedirectBox.IsEnabled = false;
            RedirectNote.Text = $"Not available: port 80 is used by {user}.";
            RedirectNote.Visibility = Visibility.Visible;
        }

        // --- Moving between pages ----------------------------------------------------------------------------

        /// <summary>Updating or repairing: settings are kept, so the choice pages are skipped.</summary>
        private bool Updating => choices.InstalledVersion != null;

        /// <summary>The installed copy's LICENSE.txt is word for word this one: an update doesn't ask again.</summary>
        private bool licenseAccepted;

        /// <summary>The license agreement built into Setup (the repository's LICENSE).</summary>
        public static string LicenseAgreement()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LICENSE.txt"))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd().Trim();
            }
        }

        /// <summary>Updating with the settings it already has: nothing was changed with Change.</summary>
        private bool KeepSettings => mode == Mode.Install && choices.InstalledVersion != null && !edited;

        /// <summary>First page shown: Installation options for a new install, the summary for an update.</summary>
        private int FirstShown()
        {
            var first = 0;
            while (first < pages.Count - 1 && pages[first].Skip())
            {
                first++;
            }
            return first;
        }

        /// <summary>Step dots for a new install. Current page in the accent colour, done ones grey.</summary>
        private void ShowSteps()
        {
            StepDots.Children.Clear();
            var steps = pages.Where(p => !p.Skip() && p.Key != "work" && p.Key != "done").ToList();
            var current = steps.IndexOf(pages[at]);
            if (mode != Mode.Install || Updating || editing != null || current < 0)
            {
                StepDots.Visibility = Visibility.Collapsed;
                return;
            }
            for (var i = 0; i < steps.Count; i++)
            {
                StepDots.Children.Add(new Ellipse
                {
                    Width = i == current ? 10 : 8,
                    Height = i == current ? 10 : 8,
                    Margin = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Fill = (Brush) FindResource(i == current ? "Accent" : i < current ? "Muted" : "LineStrong"),
                    ToolTip = steps[i].Step,
                });
            }
            AutomationProperties.SetName(StepDots, $"Step {current + 1} of {steps.Count}: {steps[current].Step}");
            StepDots.Visibility = Visibility.Visible;
        }


        /// <summary>The next page that isn't skipped.</summary>
        private int NextShown(int from)
        {
            var next = from + 1;
            while (next < pages.Count - 1 && pages[next].Skip())
            {
                next++;
            }
            return next;
        }

        private void Show(int index)
        {
            at = index;
            var page = pages[at];
            foreach (var element in new FrameworkElement[] { LicensePage, WelcomePage, SettingsPage, AddressPage, SecurityPage, OptionsPage, WorkPage, DonePage, RemovePage })
            {
                element.Visibility = element == page.Element ? Visibility.Visible : Visibility.Collapsed;
            }
            // The button that starts says what it does: Install, Update or Repair. The settings say Done; a setting's page
            // says Done, and Cancel goes back without it; the last page opens Rabbid Lab. On the first page, Cancel closes.
            var startsWork = mode == Mode.Install && editing == null && page.Key != "work" && page.Key != "done" && page.Key != "settings" && pages[NextShown(at)].Key == "work";
            var succeeded = page.Key == "done" && mode == Mode.Install && !failed;
            NextButton.Content = startsWork ? (choices.InstalledVersion == null ? "Install" : nextWord) : succeeded ? "Open " + Edition.Name : page.Key == "done" ? "Close" : page.Next;
            NextButton.Visibility = page.Key == "work" ? Visibility.Hidden : Visibility.Visible;
            BackButton.Content = editing != null ? "Cancel" : succeeded ? "Close" : at == FirstShown() ? "Cancel" : "Back";
            BackButton.Visibility = page.Key == "work" || page.Key == "settings" || (page.Key == "done" && !succeeded) ? Visibility.Collapsed : Visibility.Visible;
            // New installs already went through every setting; only updates need this link.
            SettingsButton.Visibility = page.Key == "welcome" && mode == Mode.Install && Updating ? Visibility.Visible : Visibility.Collapsed;
            CloseButton.IsEnabled = page.Key != "work";
            LogButton.Visibility = page.Key == "done" && failed ? Visibility.Visible : Visibility.Collapsed;
            Problem.Text = "";
            NextButton.IsEnabled = page.Key != "license" || AgreeBox.IsChecked == true; // the agreement has to be accepted first
            if (page.Key == "address")
            {
                RefreshUrls();
            }
            if (page.Key == "welcome")
            {
                DrawPlan();
            }
            if (page.Key == "settings")
            {
                ShowSettings();
            }
            ShowSteps();
            FitHeight();
        }

        /// <summary>
        /// The first page: what Setup is about to do (Install, Update or Repair Rabbid Lab), a sentence about this
        /// server, and the actions it will take here, from the choices as they stand. An update or repair that keeps its
        /// settings lists the four it takes; otherwise every one, as Engine.Install takes them. Under an update or a
        /// repair: how long the console is unavailable, and what's new (or that it's the same version again).
        /// </summary>
        private void DrawPlan()
        {
            if (mode != Mode.Install || pages == null)
            {
                return;
            }
            var c = choices;
            var download = CanDownload();
            var target = download ? newer.Version : Engine.Version;
            var installed = c.InstalledVersion;
            var again = installed != null && installed == target;
            WelcomeTitle.Text = installed == null ? "Ready to install" : (again ? "Repair " : "Update ") + Edition.Name;
            WelcomeLead.Text = installed == null
                ? (download ? $"Setup will download version {target} and install it. " : "")
                    + "Review the actions below, and then select Install. To change a setting, select Back."
                : again ? $"Version {installed} is already installed on this computer. Repair reinstalls the program files and service. Review the actions below, and then select Repair."
                : $"Version {installed} is installed on this computer. " + (download ? $"Setup will download version {target} and update it. " : $"This package updates it to version {target}. ")
                    + "Review the actions below, and then select Update.";

            var url = c.Urls().First().Url;
            var files = again ? ("Replace", "Amber", "Program files", c.InstallDir) : ("Update", "Blue", "Program files", $"{installed}  →  {target}");
            var steps = new List<(string Verb, string Tone, string What, string Detail)>();
            if (installed != null)
            {
                steps.Add(("Stop", "Amber", "Service", Edition.Name));
                steps.Add(files);
            }
            if (KeepSettings)
            {
                steps.Add(("Start", "Blue", "Service", Edition.Name));
                steps.Add(("Verify", "Green", "Console", url));
            }
            else
            {
                if (installed == null)
                {
                    steps.Add(("Install", "Blue", "Program files", c.InstallDir));
                }
                if (c.Https)
                {
                    steps.Add(c.CertificateSubject.Length > 0 ? ("Use", "Blue", "HTTPS certificate", c.CertificateSubject) : ("Create", "Blue", "HTTPS certificate", "Self-signed certificate for this computer"));
                }
                var redirect = c.Redirect && c.Port != 80;
                steps.Add(("Configure", "Blue", "Console address", url + (redirect ? "  (port 80 redirects here)" : "") + (c.Https ? "" : "  (HTTP, unencrypted)")));
                if (c.Firewall)
                {
                    steps.Add(("Allow", "Blue", "Windows Firewall", $"Inbound TCP {(redirect ? $"{c.Port} and 80" : c.Port.ToString())}, Domain and Private profiles"));
                }
                if (c.AddToDns && c.ExtraName.Length > 0 && Choices.Domain.Length > 0)
                {
                    steps.Add(("Create", "Blue", "DNS record", $"{c.ExtraFullName}, an alias (CNAME) for this computer"));
                }
                steps.Add((installed == null ? "Install" : "Update", "Blue", "Windows service", $"{Edition.Name}, Automatic (Delayed Start)"));
                if (c.StartMenu || c.Desktop)
                {
                    steps.Add(("Create", "Blue", "Shortcuts", c.StartMenu && c.Desktop ? "Start menu and desktop" : c.StartMenu ? "Start menu" : "Desktop"));
                }
                if (c.AllowInDefender && defenderWouldBlock)
                {
                    steps.Add(("Allow", "Blue", "Microsoft Defender", $"Exclude {c.InstallDir} from attack surface reduction"));
                }
                steps.Add(("Start", "Green", "Service", "Then verify that the console responds"));
            }
            var footer = installed == null ? null : KeepSettings ? "No changes are made to the configuration or saved credentials." : "Saved credentials are preserved.";
            Plan.Children.Clear();
            Plan.Children.Add(PlanPanel(steps, footer));

            Facts.Children.Clear();
            if (installed != null)
            {
                Facts.Children.Add(Fact("Expected downtime", "Approximately 1 minute"));
                var notes = again || download ? new List<string>() : WhatsNew.Since(installed);
                Facts.Children.Add(again ? Fact("Installed version", $"{installed} (same as this package)")
                    : notes.Count == 0 ? Fact("Installed version", installed)
                    : Fact($"What’s new in {target}", notes[0] + (notes.Count > 1 ? $" (and {notes.Count - 1} more)" : ""), string.Join("\n", notes)));
            }
            Facts.Visibility = Facts.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            FitHeight();
        }

        /// <summary>
        /// Grows the window when the page showing holds more than its usual height (a note about a newer version, or every
        /// address and DNS note on the Console address page), so it never needs a scroll bar. It never shrinks (nothing jumps), and never goes past the screen.
        /// It goes by the page as the window has just laid it out (GrowToFit), once that layout is done: measuring the page on
        /// its own, at a width a little off the window's, left a sentence at the edge drawn both wrapped and not.
        /// </summary>
        private void FitHeight()
        {
            if (mode != Mode.Install || fitQueued)
            {
                return;
            }
            fitQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                fitQueued = false;
                GrowToFit();
            }));
        }

        private bool fitQueued;

        /// <summary>Set once the window is on screen: from then on it keeps its size, and a long page scrolls.</summary>
        private bool sized;

        /// <summary>After a layout: the window grows by as much as the page showing runs past it, before it's on screen.</summary>
        private void GrowToFit()
        {
            if (sized || mode != Mode.Install || pages == null || !(pages[at].Element is ScrollViewer page) || page.Visibility != Visibility.Visible)
            {
                return;
            }
            var over = page.ExtentHeight - page.ViewportHeight;
            if (over < 1)
            {
                return;
            }
            var height = Math.Min(Math.Ceiling(Height + over), SystemParameters.WorkArea.Height);
            if (height <= Height)
            {
                return;
            }
            if (IsLoaded)
            {
                Top = Math.Max(SystemParameters.WorkArea.Top, Top - (height - Height) / 2); // stays centred as it grows
            }
            Height = height;
        }

        /// <summary>
        /// The actions, as a numbered list under "Setup will perform the following actions on SERVER": the action in its
        /// color (amber for stopping or replacing, blue for the rest, green for the last), what it acts on, and the
        /// detail. A line at the foot says what's kept.
        /// </summary>
        private UIElement PlanPanel(IList<(string Verb, string Tone, string What, string Detail)> steps, string footer)
        {
            var list = new StackPanel();
            var heading = new TextBlock { FontSize = 12.5, Foreground = (Brush) FindResource("Muted") };
            heading.Inlines.Add(new Run("Setup will perform the following actions on "));
            heading.Inlines.Add(new Run(Environment.MachineName) { FontWeight = FontWeights.SemiBold, Foreground = (Brush) FindResource("Text") });
            list.Children.Add(new Border { BorderBrush = (Brush) FindResource("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 9, 16, 9), Child = heading });
            for (var i = 0; i < steps.Count; i++)
            {
                var (verb, tone, what, detail) = steps[i];
                var row = new Grid { Margin = new Thickness(16, 7, 16, 7) };
                foreach (var width in new[] { 32.0, 96, 160 })
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
                }
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = (Brush) FindResource("GhostBack"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush) FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                });
                row.Children.Add(Cell(verb, 1, (Brush) FindResource(tone), FontWeights.SemiBold));
                row.Children.Add(Cell(what, 2, (Brush) FindResource("Muted"), FontWeights.Normal));
                var last = Cell(detail, 3, (Brush) FindResource("Text"), FontWeights.Normal);
                last.ToolTip = detail;
                row.Children.Add(last);
                list.Children.Add(new Border { BorderBrush = (Brush) FindResource("RowSep"), BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0), Child = row });
            }
            if (footer != null)
            {
                var kept = new StackPanel { Orientation = Orientation.Horizontal };
                kept.Children.Add(Glyph(CheckIcon, (Brush) FindResource("Green"), 14));
                kept.Children.Add(new TextBlock { Text = footer, FontSize = 12.5, Foreground = (Brush) FindResource("Green"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                list.Children.Add(new Border { BorderBrush = (Brush) FindResource("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 9, 16, 9), Child = kept });
            }
            return new Border { Style = (Style) FindResource("Panel"), Child = list };
        }

        private static TextBlock Cell(string text, int column, Brush colour, FontWeight weight)
        {
            var cell = new TextBlock { Text = text, FontSize = 13, Foreground = colour, FontWeight = weight, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(cell, column);
            return cell;
        }

        /// <summary>A fact under the actions: what it is, and its value.</summary>
        private UIElement Fact(string name, string value, string more = null)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = (Brush) FindResource("Muted") });
            text.Children.Add(new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
            return new Border { CornerRadius = new CornerRadius(8), BorderBrush = (Brush) FindResource("Line"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(5, 0, 5, 0), Child = text, ToolTip = more ?? value };
        }

        /// <summary>
        /// Where to open it, on the last page: each address (it opens when clicked), what it is (a name not in DNS yet
        /// says so, in amber), and a copy button.
        /// </summary>
        private void ShowAddresses()
        {
            DoneUrlList.Children.Clear();
            var urls = choices.Urls().ToList();
            for (var i = 0; i < urls.Count; i++)
            {
                var (url, what) = urls[i];
                var row = new DockPanel { Margin = new Thickness(16, 4, 10, 4) };
                var copy = new Button { Style = (Style) FindResource("IconButton"), Content = Glyph(CopyIcon, (Brush) FindResource("Muted"), 14), ToolTip = "Copy" };
                copy.Click += (s, e) =>
                {
                    try
                    {
                        Clipboard.SetText(url);
                        copy.Content = Glyph(CheckIcon, (Brush) FindResource("Green"), 14);
                        copy.ToolTip = "Copied";
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        copy.ToolTip = "The clipboard is in use. Try again.";
                    }
                };
                AutomationProperties.SetName(copy, "Copy " + url);
                DockPanel.SetDock(copy, Dock.Right);
                row.Children.Add(copy);
                var dns = what.IndexOf(" (requires DNS record)", StringComparison.Ordinal);
                var label = new TextBlock { FontSize = 12, Foreground = (Brush) FindResource("Muted"), Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
                label.Inlines.Add(new Run(dns < 0 ? what : what.Substring(0, dns)));
                if (dns >= 0)
                {
                    label.Inlines.Add(new Run("  ·  requires a DNS record") { Foreground = (Brush) FindResource("Amber") });
                }
                DockPanel.SetDock(label, Dock.Right);
                row.Children.Add(label);
                var link = new TextBlock
                {
                    Text = url,
                    FontSize = 13,
                    Foreground = (Brush) FindResource("Blue"),
                    Cursor = Cursors.Hand,
                    ToolTip = "Open " + url,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                link.MouseEnter += (s, e) => link.TextDecorations = TextDecorations.Underline;
                link.MouseLeave += (s, e) => link.TextDecorations = null;
                link.MouseLeftButtonUp += (s, e) => { try { Process.Start(url); } catch { /* no browser set up; the address is on screen */ } };
                row.Children.Add(link);
                DoneUrlList.Children.Add(new Border { BorderBrush = (Brush) FindResource("RowSep"), BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0), Child = row });
            }
        }

        // Icons, drawn on a 24-unit square.
        private const string CheckIcon = "M5,12 L9.5,16.5 L19,7";
        private const string CopyIcon = "M10,8 H18 A2,2 0 0 1 20,10 V18 A2,2 0 0 1 18,20 H10 A2,2 0 0 1 8,18 V10 A2,2 0 0 1 10,8 Z M16,8 V6 A2,2 0 0 0 14,4 H6 A2,2 0 0 0 4,6 V14 A2,2 0 0 0 6,16 H8";

        private static UIElement Glyph(string data, Brush stroke, double size) => new Viewbox
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Canvas
            {
                Width = 24,
                Height = 24,
                Children =
                {
                    new Path { Data = Geometry.Parse(data), Stroke = stroke, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round },
                },
            },
        };

        /// <summary>
        /// The installation settings (Change installation settings): the console address, the certificate, the folder
        /// and the shortcuts, as they will be, each explained, with Change, which opens its page.
        /// </summary>
        private void ShowSettings()
        {
            SettingsList.Children.Clear();
            var c = choices;
            var verb = c.InstalledVersion == null ? "Install" : nextWord;
            SettingsLead.Text = $"These settings are used when you select {verb}. To change a setting, select Change.";
            var shortcuts = c.StartMenu && c.Desktop ? "Start menu and desktop" : c.StartMenu ? "Start menu" : c.Desktop ? "Desktop" : "None";
            var rows = new List<(string Name, string Value, string Sub, string Page)>
            {
                ("Console address", c.Urls().First().Url, $"Port {c.Port}." + (c.Redirect && c.Port != 80 ? " Requests to port 80 are redirected here." : "")
                    + (c.Firewall ? " Allowed through Windows Firewall." : " Windows Firewall is not changed."), "address"),
                ("HTTPS certificate", !c.Https ? "None (HTTP only)" : c.CertificateSubject.Length > 0 ? c.CertificateSubject : "Self-signed certificate created by Setup",
                    !c.Https ? "For testing only. Sign-ins are sent unencrypted." : c.CertificateSubject.Length > 0 ? "A certificate installed on this computer." : "Browsers on other computers show a security warning until the certificate is trusted.", "security"),
                ("Installation folder", c.InstallDir, "The Rabbid Lab program files and service.", "options"),
                ("Shortcuts", shortcuts, shortcuts == "None" ? "No shortcuts are created." : "Available to all users who sign in to this computer.", "options"),
            };
            for (var i = 0; i < rows.Count; i++)
            {
                var (name, value, sub, page) = rows[i];
                var row = new Grid { Margin = new Thickness(16, 11, 16, 11) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = name, FontSize = 12.5, Foreground = (Brush) FindResource("Muted"), Margin = new Thickness(0, 1, 0, 0) });
                var text = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
                text.Children.Add(new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = value });
                text.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = (Brush) FindResource("Muted"), Margin = new Thickness(0, 2, 0, 0) });
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                var change = new Button { Style = (Style) FindResource("LinkButton"), Content = "Change", Tag = page, VerticalAlignment = VerticalAlignment.Top };
                change.Click += Change_Click;
                AutomationProperties.SetName(change, "Change " + name.ToLowerInvariant());
                Grid.SetColumn(change, 2);
                row.Children.Add(change);
                SettingsList.Children.Add(new Border { BorderBrush = (Brush) FindResource("RowSep"), BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0), Child = row });
            }
        }

        /// <summary>Change: its page, with Done (which keeps what's there and comes back) and Cancel.</summary>
        private void Change_Click(object sender, RoutedEventArgs e)
        {
            if (moving)
            {
                return;
            }
            editing = (string) ((Button) sender).Tag;
            Show(pages.FindIndex(p => p.Key == editing));
        }

        /// <summary>Change installation settings: the settings, each with Change; Done comes back.</summary>
        private void ChangeSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!moving)
            {
                Show(pages.FindIndex(p => p.Key == "settings"));
            }
        }

        /// <summary>Set while Next is still at work (looking up a port or a name), so a second click does nothing.</summary>
        private bool moving;

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            if (moving || working)
            {
                return;
            }
            moving = true;
            try
            {
                await Next();
            }
            finally
            {
                moving = false;
            }
        }

        private async Task Next()
        {
            var page = pages[at];
            if (page.Key == "address" && int.TryParse(PortBox.Text.Trim(), out var typed) && typed >= 1 && typed <= 65535)
            {
                // Who has the port is asked off the window's thread: it can take Windows a few seconds to say.
                portUser = await Task.Run(() => Engine.PortUser(typed));
                portAsked = typed;
            }
            if (!Check(page.Key))
            {
                return;
            }
            if (editing != null)
            {
                edited = true;
                editing = null;
                Show(pages.FindIndex(p => p.Key == "settings"));
                return;
            }
            if (page.Key == "settings")
            {
                Show(pages.FindIndex(p => p.Key == "welcome"));
                return;
            }
            // Switch to a newer Setup on the first page, before any choices are made.
            if (at == FirstShown() && CanDownload())
            {
                await SwitchToNewer(); // if it took over, this window is closing; if not, it says why, and Next goes on
                return;
            }
            if (page.Key == "done")
            {
                if (mode == Mode.Install && !failed)
                {
                    try { Process.Start(choices.LocalUrl); } catch { /* no browser set up; the address is on screen */ }
                }
                Close();
                return;
            }
            var next = NextShown(at);
            if (pages[next].Key == "work" && !edited)
            {
                if (KeepSettings)
                {
                    choices.UseInstalledSettings();
                }
                // The Web address page usually checks this; not opened, it's looked up here for the addresses shown at the end.
                var name = choices.ExtraFullName;
                choices.NameInDns = name.Length > 0 && Choices.IsDnsName(name) && (await Task.Run(() => DnsAlias.Lookup(name))).Item1 == DnsAlias.Found.ThisServer;
            }
            Show(next);
            if (pages[next].Key == "work")
            {
                Run();
            }
        }

        /// <summary>Asks GitHub for a newer version in the background, and says so on the welcome page if there is one.</summary>
        private async void LookForNewer()
        {
            var found = await Task.Run(() => SetupUpdate.Latest());
            if (found == null || !found.IsNewer)
            {
                return;
            }
            newer = found;
            DescribeUpdate();
        }

        /// <summary>The newer version can be downloaded: there's one, and it hasn't been tried.</summary>
        private bool CanDownload() => newer != null && !newerTried;

        /// <summary>
        /// The welcome page says one thing everywhere: the version it installs, in place of which, in the title, the first
        /// choice and the button. That's the newer version GitHub offers when Setup can download it, otherwise this
        /// Setup's own; the same version as installed is a reinstall. The note above the title only says what the page
        /// doesn't: for a new installation, the one Next downloads.
        /// </summary>
        private void DescribeUpdate()
        {
            if (mode != Mode.Install)
            {
                return;
            }
            var upgrade = choices.InstalledVersion != null;
            var download = CanDownload();
            var target = download ? newer.Version : Engine.Version;
            if (upgrade)
            {
                var again = choices.InstalledVersion == target;
                nextWord = again ? "Repair" : "Update";
                WorkTitle.Text = (again ? "Repairing " : "Updating ") + Edition.Name;
            }
            DrawPlan();
            if (newer != null && !newerTried)
            {
                if (download && !upgrade)
                {
                    ShowNewer($"Rabbid Lab {newer.Version} is available. When you select Next, Setup downloads it and installs it instead of version {Engine.Version}.");
                }
                else
                {
                    NewerChip.Visibility = Visibility.Collapsed;
                    FolderNewerChip.Visibility = Visibility.Collapsed;
                }
            }
            if (pages != null && pages[at].Key == "welcome")
            {
                Show(at); // the button's name
            }
        }

        /// <summary>Shows the newer-version note on the first page and the summary.</summary>
        private void ShowNewer(string text)
        {
            NewerText.Text = text;
            NewerChip.Visibility = Visibility.Visible;
            FolderNewerText.Text = text;
            FolderNewerChip.Visibility = Visibility.Visible;
            FitHeight();
        }

        /// <summary>
        /// Downloads the newer version and runs it in place of this Setup, with the same command line, and closes. If it
        /// can't be downloaded, it says why, and the next Next installs this version.
        /// </summary>
        private async Task SwitchToNewer()
        {
            newerTried = true;
            NextButton.IsEnabled = false;
            ShowNewer($"Downloading Rabbid Lab {newer.Version}…");
            try
            {
                var setup = await Task.Run(() => newer.Download());
                var extra = new List<string> { "/updatedfrom=" + Engine.Version };
                SetupUpdate.Run(setup, App.StartArgs, choices, extra.ToArray());
                Application.Current.Shutdown(0);
            }
            catch (Exception ex) when (ex is IOException || ex is System.Net.WebException || ex is InvalidOperationException || ex is InvalidDataException
                || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
                NextButton.IsEnabled = true;
                ShowNewer($"Setup could not download version {newer.Version} ({ex.Message.TrimEnd('.')}). To install version {Engine.Version} instead, select {nextWord}. Rabbid Lab updates itself to the latest version shortly after it starts.");
                DescribeUpdate();
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (moving)
            {
                return;
            }
            if (editing != null)
            {
                editing = null; // Cancel: back to the settings, without what was typed there
                Show(pages.FindIndex(p => p.Key == "settings"));
                return;
            }
            if (at == FirstShown())
            {
                Close(); // Cancel, on the first page
                return;
            }
            if (pages[at].Key == "done")
            {
                Close();
                return;
            }
            var back = at - 1;
            while (back > FirstShown() && pages[back].Skip())
            {
                back--;
            }
            Show(back);
        }

        /// <summary>The port Next last asked about, and who has it (null when it's free or Rabbid Lab's own).</summary>
        private int portAsked;
        private string portUser;

        /// <summary>The host name as typed, without spaces or a dot at the end, in lower case.</summary>
        private string TypedName => NameBox.Text.Trim().TrimEnd('.').ToLowerInvariant();

        /// <summary>Checks a page's answers and saves them into the choices. False (with a reason on screen) to stay.</summary>
        private bool Check(string key)
        {
            string problem = null;
            switch (key)
            {
                case "welcome":
                    break;
                case "address":
                    var name = TypedName;
                    if (!int.TryParse(PortBox.Text.Trim(), out var port) || port < 1 || port > 65535)
                    {
                        problem = $"Enter a port number between 1 and 65535. The default port is {Edition.DefaultPort}.";
                    }
                    else if (port == portAsked && portUser is string user)
                    {
                        problem = $"Port {port} is in use by {user} on this computer. Enter a different port, such as 9443.";
                    }
                    else if (name.Length > 0 && (!Choices.IsDnsName(name) || (!name.Contains(".") && Choices.Domain.Length == 0)))
                    {
                        problem = "The host name is not valid. Enter a name such as rabbidlab or lab.home.lan, or leave the field blank.";
                    }
                    else
                    {
                        choices.Port = port;
                        choices.ExtraName = name;
                        choices.AddToDns = DnsBox.Visibility == Visibility.Visible && DnsBox.IsChecked == true;
                        choices.NameInDns = nameInDns;
                        choices.Firewall = FirewallBox.IsChecked == true;
                        choices.Redirect = RedirectBox.IsChecked == true;
                    }
                    break;
                case "security":
                    choices.Https = HttpChoice.IsChecked != true;
                    var picked = CertList.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
                    choices.CertificateSubject = OwnCertChoice.IsChecked == true && picked != null ? (string) picked.Tag : "";
                    if (OwnCertChoice.IsChecked == true && CertList.Children.Count == 0)
                    {
                        problem = "No suitable certificate is installed on this computer. Import a certificate, or select Create a self-signed certificate.";
                    }
                    else if (OwnCertChoice.IsChecked == true && picked == null)
                    {
                        problem = "Select a certificate.";
                    }
                    break;
                case "options":
                    var folder = FolderBox.Text.Trim();
                    if (folder.Length == 0 || !System.IO.Path.IsPathRooted(folder))
                    {
                        problem = "Enter the full path of the folder, such as C:\\Program Files\\Rabbid Lab.";
                    }
                    else if (!Updating && Engine.FolderProblem(folder) is string taken)
                    {
                        problem = taken;
                    }
                    else
                    {
                        choices.InstallDir = folder;
                        choices.StartMenu = StartMenuBox.IsChecked == true;
                        choices.AllowInDefender = DefenderBox.IsChecked == true;
                        choices.Desktop = DesktopBox.IsChecked == true;
                    }
                    break;
            }
            Problem.Text = problem ?? "";
            if (problem != null)
            {
                StepDots.Visibility = Visibility.Collapsed; // the message takes the foot
            }
            return problem == null;
        }

        // --- Doing it --------------------------------------------------------------------------------------

        private async void Run()
        {
            if (working)
            {
                return; // one installation at a time
            }
            working = true;
            WorkHeading.Text = $"Setup is performing the following actions on {Environment.MachineName}";
            WorkList.Children.Clear();
            engine = new Engine((what, progress) => Dispatcher.Invoke(() =>
            {
                WorkTask(what);
                WorkBar.Value = progress;
                WorkPercent.Text = $"{Math.Round(progress * 100)}%";
            }));
            var deleteData = EverythingChoice.IsChecked == true;
            Exception error = null;
            try
            {
                await Task.Run(() =>
                {
                    if (mode == Mode.Install)
                    {
                        engine.Install(choices);
                    }
                    else
                    {
                        engine.Uninstall(deleteData);
                    }
                });
            }
            catch (Exception e)
            {
                error = e;
            }
            working = false;
            failed = error != null;
            WorkTask(null);
            ShowResult(error);
            Show(pages.FindIndex(p => p.Key == "done"));
        }

        private string workingOn;

        /// <summary>
        /// What it's doing now, as a row under the ones it has done: a new step ticks off the one before and goes under it,
        /// in bold; null ticks off the last.
        /// </summary>
        private void WorkTask(string what)
        {
            what = what?.TrimEnd('.', ' ', '…');
            if (what == workingOn)
            {
                return;
            }
            if (WorkList.Children.Count > 0 && WorkList.Children[WorkList.Children.Count - 1] is DockPanel last)
            {
                var mark = (Border) last.Children[0];
                mark.Background = (Brush) FindResource("Green");
                mark.BorderThickness = new Thickness(0);
                mark.Child = new Path { Data = Geometry.Parse("M4,9 L7.5,12.5 L14,5.5"), Stroke = (Brush) FindResource("PanelSolid"), StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 18, Height = 18 };
                var text = (TextBlock) last.Children[1];
                text.FontWeight = FontWeights.Normal;
                text.Foreground = (Brush) FindResource("Muted");
            }
            workingOn = what;
            if (what == null)
            {
                return;
            }
            var row = new DockPanel { Margin = new Thickness(16, 6, 16, 6) };
            row.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(2), BorderBrush = (Brush) FindResource("Soft"), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = what, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            WorkList.Children.Add(row);
            row.Loaded += (s, e) => row.BringIntoView(); // the newest step stays in sight when the list outgrows the window
        }

        /// <summary>The service would not start: what stopped it, and how to fix it, step by step.</summary>
        private void ShowFix(StartFix fix)
        {
            shownFix = fix;
            DoneTitle.Text = fix.Title;
            DoneLead.Text = fix.Summary;
            FixPanel.Visibility = Visibility.Visible;
            FixSteps.Children.Clear();
            for (var i = 0; i < fix.Steps.Count; i++)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var number = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Background = (Brush) FindResource("Accent"),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                DockPanel.SetDock(number, Dock.Left);
                row.Children.Add(number);
                row.Children.Add(new TextBlock { Text = fix.Steps[i], FontSize = 13.5, LineHeight = 20, TextWrapping = TextWrapping.Wrap, Foreground = (Brush) FindResource("Text") });
                FixSteps.Children.Add(row);
            }
            AllowButton.Content = fix.AllowText ?? "Allow in Microsoft Defender";
            AllowButton.Visibility = fix.CanAllow ? Visibility.Visible : Visibility.Collapsed;
            AllowButton.IsEnabled = true;
            AllowResult.Visibility = Visibility.Collapsed;
            var copy = !string.IsNullOrEmpty(fix.CopyText);
            FixCopyLabel.Visibility = FixCopyBox.Visibility = copy ? Visibility.Visible : Visibility.Collapsed;
            FixCopyLabel.Text = fix.CopyLabel ?? "";
            FixCopyText.Text = fix.CopyText ?? "";
            FixCopyButton.Content = "Copy";
        }

        private void FixCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(FixCopyText.Text);
                FixCopyButton.Content = "Copied";
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                FixCopyText.Focus();
                FixCopyText.SelectAll(); // the clipboard is busy: selected, to copy with Ctrl+C
            }
        }

        /// <summary>Allow in Microsoft Defender: Setup adds the exclusion on this computer and starts Rabbid Lab.</summary>
        private async void Allow_Click(object sender, RoutedEventArgs e)
        {
            if (working)
            {
                return;
            }
            var retry = shownFix?.Retry == true;
            working = true; // the window stays open until the service has started, or hasn't
            AllowButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            AllowResult.Visibility = Visibility.Visible;
            AllowResult.Text = retry ? "Starting Rabbid Lab…" : "Excluding Rabbid Lab from the attack surface reduction rules and starting it…";
            try
            {
                await Task.Run(() =>
                {
                    if (retry)
                    {
                        engine.StartAgain(choices);
                    }
                    else
                    {
                        engine.AllowAndStart(choices);
                    }
                });
                failed = false;
                ShowResult(null);
            }
            catch (Exception ex)
            {
                // Every outcome gets the whole page: what went wrong and what to do, not a line under the button.
                failed = true;
                ShowResult(ex);
                if (FixPanel.Visibility == Visibility.Visible)
                {
                    AllowResult.Visibility = Visibility.Visible; // the same page again still shows the click did something
                    AllowResult.Text = $"Last tried at {DateTime.Now:t}.";
                }
            }
            working = false;
            Show(pages.FindIndex(p => p.Key == "done"));
        }

        /// <summary>The fix on the last page, for what Try again does.</summary>
        private StartFix shownFix;

        private void ShowResult(Exception error)
        {
            DoneMark.Visibility = error == null ? Visibility.Visible : Visibility.Collapsed;
            FailMark.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            DevTools.Visibility = mode == Mode.Install && ShowDevTools ? Visibility.Visible : Visibility.Collapsed;
            DoneUrls.Visibility = Visibility.Collapsed;
            if (mode == Mode.Uninstall)
            {
                DoneTitle.Text = error == null ? "Removal complete" : "Removal did not complete";
                DoneLead.Text = error == null
                    ? (EverythingChoice.IsChecked == true ? "Rabbid Lab was removed from this computer, including the program, service, settings, saved credentials, encryption keys, certificate, firewall rule, shortcuts, registry entries and logs." : $"The program, service, firewall rule, certificate and shortcuts were removed. Settings and saved credentials are retained in {Engine.DataDir} and are used if Rabbid Lab is reinstalled.")
                    : error.Message;
                // What Setup could not take out, and has to be removed by hand.
                foreach (var left in new[] { engine?.LeftoverMessage, engine?.DnsMessage }.Where(m => m != null))
                {
                    DoneLead.Text += " " + left;
                }
                DoneNext.Visibility = Visibility.Collapsed;
                return;
            }
            FixPanel.Visibility = Visibility.Collapsed;
            DoneNext.Visibility = Visibility.Visible;
            if (error is ServiceStartException notStarted)
            {
                ShowFix(notStarted.Fix);
                DoneNext.Visibility = Visibility.Collapsed;
                LogButton.Visibility = Visibility.Visible;
                return;
            }
            if (error != null)
            {
                DoneTitle.Text = "Setup did not complete";
                DoneLead.Text = error.Message;
                DoneNext.Text = "No data was lost. " + (engine?.ServiceLeftStopped == true ? $"The {Edition.Name} service is stopped, and Setup could not start it again. " : "")
                    + "Resolve the issue, and then run Setup again. For more information, view the setup log.";
                LogButton.Visibility = Visibility.Visible;
                return;
            }
            ShowAddresses();
            DoneUrls.Visibility = Visibility.Visible;
            if (choices.InstalledVersion != null)
            {
                // An update or a repair: it was set up already, so no first-time instructions.
                DoneTitle.Text = nextWord == "Repair" ? "Repair complete" : "Update complete";
                DoneLead.Text = $"{Edition.Name} {Engine.Version} is running on this computer. "
                    + (KeepSettings ? "Your configuration and saved credentials were preserved." : "The changed settings are applied, and saved credentials were preserved.");
                DoneNext.Text = "";
            }
            else
            {
                DoneTitle.Text = "Installation complete";
                DoneLead.Text = $"{Edition.Name} {Engine.Version} is installed and running on this computer.";
                DoneNext.Text = Edition.FirstSignIn;
                if (choices.Https && choices.CertificateSubject.Length == 0)
                {
                    DoneNext.Text += " Browsers on other computers display a certificate warning until the certificate is trusted.";
                }
            }
            if (engine?.DnsMessage != null)
            {
                DoneNext.Text += " " + engine.DnsMessage;
            }
        }

        // --- Developer options (Rabbid Lab Dev, or /devtools) -----------------------------------------------------

        private static bool ShowDevTools => Edition.Dev || App.StartArgs.Any(a => Choices.SplitArg(a).Name == "devtools");

        /// <summary>Removes the Defender exclusion, so the blocked screen can be tested again.</summary>
        private async void DevRemoveExclusion_Click(object sender, RoutedEventArgs e)
        {
            var dir = choices.InstallDir;
            DevResult.Visibility = Visibility.Visible;
            DevResult.Text = "Removing the exclusions…";
            try
            {
                await Task.Run(() => Engine.RemoveExclusions(dir));
                DevResult.Text = $"Removed the Defender exclusions for {dir}. Stop the service and run Setup again (Repair) to see the block.";
            }
            catch (Exception ex)
            {
                DevResult.Text = "Could not remove the exclusions: " + ex.Message;
            }
        }

        /// <summary>Shows the blocked screen as Setup shows it when Defender stops the service.</summary>
        private void DevShowBlocked_Click(object sender, RoutedEventArgs e)
        {
            var exe = System.IO.Path.Combine(choices.InstallDir, "RabbidLab.Server.exe");
            failed = true;
            ShowResult(new ServiceStartException(Engine.Fix("asr", "01443614-cd74-433a-b99e-2ecdc07bfc25", exe, null, null, true), null));
            Show(pages.FindIndex(p => p.Key == "done"));
        }

        // --- Bits of pages ---------------------------------------------------------------------------------------

        private void RefreshUrls()
        {
            if (UrlList == null)
            {
                return;
            }
            var probe = new Choices
            {
                Port = int.TryParse(PortBox.Text.Trim(), out var p) && p > 0 && p < 65536 ? p : choices.Port,
                ExtraName = TypedName,
                AddToDns = DnsBox.Visibility == Visibility.Visible && DnsBox.IsChecked == true,
                NameInDns = nameInDns,
                Https = HttpChoice.IsChecked != true,
            };
            UrlList.Children.Clear();
            // Three at most so the page fits: the name given, this computer's name, and an IP. The last page lists them all.
            var urls = probe.Urls();
            var shown = probe.ExtraName.Length > 0 ? urls.Take(1).Concat(urls.Where(u => u.What == "Computer name" || u.What == "IP address")) : urls;
            foreach (var (url, what) in shown.Take(3))
            {
                UrlList.Children.Add(UrlRow(url, what));
            }
            FitHeight();
        }


        /// <summary>An address on the Web address page: the address, and what it is (server name, IP address).</summary>
        private UIElement UrlRow(string url, string what)
        {
            var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var label = new TextBlock { Text = what, FontSize = 12, Foreground = (Brush) FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(label, Dock.Right);
            line.Children.Add(label);
            line.Children.Add(new TextBlock { Text = url, FontSize = 13, Foreground = (Brush) FindResource("Blue"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
            return line;
        }

        // --- Events --------------------------------------------------------------------------------------------

        private void Agree_Changed(object sender, RoutedEventArgs e)
        {
            if (pages[at].Key == "license")
            {
                NextButton.IsEnabled = AgreeBox.IsChecked == true;
            }
        }

        private void Address_Changed(object sender, TextChangedEventArgs e)
        {
            RefreshUrls();
            if (sender == NameBox)
            {
                CheckDnsSoon();
            }
        }

        private void Dns_Changed(object sender, RoutedEventArgs e) => RefreshUrls();

        private DispatcherTimer dnsTimer;
        private bool nameInDns;

        /// <summary>Half a second after the name stops changing, looks it up to see whether it needs adding to DNS.</summary>
        private void CheckDnsSoon()
        {
            if (dnsTimer == null)
            {
                dnsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                dnsTimer.Tick += async (s, e) =>
                {
                    dnsTimer.Stop();
                    await CheckDns();
                };
            }
            dnsTimer.Stop();
            dnsTimer.Start();
        }

        /// <summary>
        /// Offers to add the name of their own to DNS when it isn't there yet and it's in this computer's domain. Says so
        /// instead when it already points here, points somewhere else, or is in a zone Setup can't add to.
        /// </summary>
        private async Task CheckDns()
        {
            var probe = new Choices { ExtraName = TypedName };
            var name = probe.ExtraFullName;
            var found = name.Length > 0 && Choices.IsDnsName(name) ? await Task.Run(() => DnsAlias.Lookup(name)) : (DnsAlias.Found.Nothing, "");
            if (probe.ExtraName == TypedName) // not changed again while looking
            {
                ShowDns(probe, found.Item1, found.Item2);
            }
        }

        /// <summary>The same, straight away (for the pictures --snapshot takes).</summary>
        private void CheckDnsNow()
        {
            var probe = new Choices { ExtraName = TypedName };
            var (found, where) = probe.ExtraName.Length > 0 && Choices.IsDnsName(probe.ExtraFullName) ? DnsAlias.Lookup(probe.ExtraFullName) : (DnsAlias.Found.Nothing, "");
            ShowDns(probe, found, where);
        }

        private void ShowDns(Choices probe, DnsAlias.Found found, string where)
        {
            nameInDns = found == DnsAlias.Found.ThisServer;
            var name = probe.ExtraFullName;
            void Note(string text, string colour)
            {
                dnsChecks++;
                ShowDnsCheck("", "Muted");
                DnsBox.Visibility = Visibility.Collapsed;
                DnsNote.Text = text;
                DnsNote.Foreground = (Brush) FindResource(colour);
                DnsNote.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                RefreshUrls();
            }
            if (name.Length == 0 || !Choices.IsDnsName(name))
            {
                Note("", "Muted");
            }
            else if (found == DnsAlias.Found.ThisServer)
            {
                Note($"✓ {name} already resolves to this computer.", "Green");
            }
            else if (found == DnsAlias.Found.Elsewhere)
            {
                Note($"{name} already resolves to {where}, which is not this computer. Choose a different name, or update the record on the DNS server.", "Amber");
            }
            else if (Choices.Domain.Length == 0)
            {
                Note($"Create a host (A) or alias (CNAME) record for {name} that points to this computer.", "Muted");
            }
            else
            {
                DnsNote.Visibility = Visibility.Collapsed;
                DnsTitle.Text = $"Create a DNS alias (CNAME) record for {name}";
                DnsBox.Visibility = Visibility.Visible;
                RefreshUrls();
                if (!snapshotting)
                {
                    PreflightDns(name);
                }
            }
        }

        private int dnsChecks;
        private bool snapshotting;

        /// <summary>
        /// Asks the domain controller's DNS whether Setup can add the name (without changing anything), and says so under
        /// the tickbox: where it would go, or why it can't. A name that turns out to be taken, or a computer's, isn't
        /// offered at all; without DNS admin rights the tickbox is switched off.
        /// </summary>
        private async void PreflightDns(string name)
        {
            var mine = ++dnsChecks;
            DnsBox.IsEnabled = true;
            ShowDnsCheck("Checking whether Setup can create the DNS record…", "Muted");
            var (verdict, message) = await Task.Run(() => DnsAlias.Check(name));
            if (mine != dnsChecks || DnsBox.Visibility != Visibility.Visible)
            {
                return; // the name has changed since
            }
            switch (verdict)
            {
                case DnsVerdict.CanAdd:
                    ShowDnsCheck("✓ " + message, "Green");
                    break;
                case DnsVerdict.NoRights:
                case DnsVerdict.Unreachable:
                    DnsBox.IsChecked = false;
                    DnsBox.IsEnabled = false;
                    ShowDnsCheck(message, "Amber");
                    break;
                default:
                    DnsBox.IsChecked = false;
                    DnsBox.Visibility = Visibility.Collapsed;
                    ShowDnsCheck("", "Muted");
                    nameInDns = verdict == DnsVerdict.AlreadyHere;
                    DnsNote.Text = (verdict == DnsVerdict.AlreadyHere ? "✓ " : "") + message;
                    DnsNote.Foreground = (Brush) FindResource(verdict == DnsVerdict.AlreadyHere ? "Green" : "Amber");
                    DnsNote.Visibility = Visibility.Visible;
                    RefreshUrls();
                    break;
            }
        }

        private void ShowDnsCheck(string text, string colour)
        {
            DnsCheck.Text = text;
            DnsCheck.Foreground = (Brush) FindResource(colour);
            DnsCheck.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            FitHeight();
        }

        /// <summary>
        /// The certificates on this computer Rabbid Lab could use, as rows to pick from: in Local Computer > Personal,
        /// with their private key, valid now and good for a web server. Setup's own self-signed ones aren't listed.
        /// The one chosen before (by name) is picked again.
        /// </summary>
        private void FillCertificates()
        {
            CertList.Children.Clear();
            foreach (var cert in Choices.UsableCertificates())
            {
                var name = cert.GetNameInfo(X509NameType.SimpleName, false);
                var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, FontSize = 13.5 };
                text.Inlines.Add(new Run(name) { FontWeight = FontWeights.SemiBold });
                text.Inlines.Add(new Run($"   Issued by {cert.GetNameInfo(X509NameType.SimpleName, true)} · Expires {cert.NotAfter:MMM d, yyyy}") { Foreground = (Brush) FindResource("Muted"), FontSize = 12.5 });
                CertList.Children.Add(new RadioButton
                {
                    Style = (Style) FindResource("CertChoice"),
                    GroupName = "certificate",
                    Tag = name,
                    Content = text,
                    ToolTip = cert.Subject + "\nThumbprint: " + cert.Thumbprint,
                    IsChecked = choices.CertificateSubject.Length > 0 && cert.Subject.IndexOf(choices.CertificateSubject, StringComparison.OrdinalIgnoreCase) >= 0
                        && !CertList.Children.OfType<RadioButton>().Any(r => r.IsChecked == true),
                });
            }
            CertList.Visibility = CertList.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            CertNone.Visibility = CertList.Children.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Security_Changed(object sender, RoutedEventArgs e)
        {
            if (CertPicker != null)
            {
                CertPicker.Visibility = OwnCertChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
            RefreshUrls();
        }

        private void ChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Select the installation folder for Rabbid Lab.", SelectedPath = FolderBox.Text })
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    FolderBox.Text = dialog.SelectedPath.EndsWith(Edition.Name, StringComparison.OrdinalIgnoreCase)
                        ? dialog.SelectedPath
                        : System.IO.Path.Combine(dialog.SelectedPath, Edition.Name);
                }
            }
        }

        private void OpenLog_Click(object sender, RoutedEventArgs e)
        {
            var log = engine?.LogPath ?? Engine.InstallLog(choices.InstallDir);
            if (File.Exists(log))
            {
                Process.Start("notepad.exe", $"\"{log}\"");
            }
        }

        private void Minimise_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = working; // never leave halfway through installing
            base.OnClosing(e);
        }

        // --- Pictures of every page, for checking how they look (--snapshot <folder>) ---------------------------

        public void Snapshot(string folder)
        {
            snapshotting = true;
            Directory.CreateDirectory(folder);
            var root = (FrameworkElement) Content;
            Content = null;
            void Layout()
            {
                root.Measure(new Size(Width, Height));
                root.Arrange(new Rect(0, 0, Width, Height));
                root.UpdateLayout();
            }
            void Shoot(string name)
            {
                Layout();
                var before = Height;
                GrowToFit();
                if (Height != before)
                {
                    Layout();
                }
                var bitmap = new RenderTargetBitmap((int) Width * 2, (int) Height * 2, 192, 192, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(System.IO.Path.Combine(folder, name + ".png")))
                {
                    png.Save(file);
                }
                sized = true; // as on screen: only the first page sizes the window
            }
#if PREVIEW
            // /previewnewer=0.6.49: as if GitHub offered that version.
            var arg = new Func<string, string>(name => App.StartArgs.Select(Choices.SplitArg).FirstOrDefault(a => a.Name == name).Value);
            if (arg("previewcert") == "own")
            {
                OwnCertChoice.IsChecked = true; // /previewcert=own: the certificate list open
            }
            if (arg("previewdefender") == "yes")
            {
                defenderWouldBlock = true; // /previewdefender=yes: as on a computer whose Defender blocks new programs
                DefenderSection.Visibility = Visibility.Visible;
            }
            if (!string.IsNullOrEmpty(arg("previewport80")))
            {
                ShowPortUser(arg("previewport80")); // /previewport80=NorthStack AD: port 80 taken
            }
            if (!string.IsNullOrEmpty(arg("previewnewer")))
            {
                newer = new SetupUpdate { Version = arg("previewnewer") };
                DescribeUpdate();
            }
#endif
            for (var i = 0; i < pages.Count; i++)
            {
                if (pages[i].Skip())
                {
                    continue; // as the window goes: an update that keeps its settings skips them
                }
                if (pages[i].Key == "address")
                {
                    CheckDnsNow();
                }
                if (pages[i].Key == "work")
                {
                    Show(i);
                    WorkHeading.Text = $"Setup is performing the following actions on {Environment.MachineName}";
                    foreach (var step in new[] { "Checking this computer", "Stopping the Rabbid Lab service", "Copying program files" })
                    {
                        WorkTask(step);
                    }
                    WorkBar.Value = 0.46;
                    WorkPercent.Text = "46%";
                }
                else if (pages[i].Key == "done")
                {
#if PREVIEW
                    // /previewfail=asr|asr-intune|asr-gpo|asr-managed|asr-excluded|defender|applocker|wdac|unknown|error|stopped|silent|notallowed:
                    // the last page after Windows would not start the service, for each kind of block.
                    var kind = App.StartArgs.Select(Choices.SplitArg).FirstOrDefault(a => a.Name == "previewfail").Value;
                    if (kind != null)
                    {
                        failed = true;
                        var exe = System.IO.Path.Combine(choices.InstallDir, "RabbidLab.Server.exe");
                        var by = kind == "asr-intune" ? "Intune" : kind == "asr-gpo" ? "Group Policy" : null;
                        var said = kind == "stopped" ? "Failed to bind to address http://[::]:8420: address already in use."
                            : kind == "silent" ? "8420"
                            : kind == "notallowed" ? "Add-MpPreference : Operation failed with the following error: 0x800106ba."
                            : "The service did not respond to the start or control request in a timely fashion (error 1053)";
                        ShowResult(new ServiceStartException(Engine.Fix(kind.StartsWith("asr") ? "asr" : kind, "01443614-cd74-433a-b99e-2ecdc07bfc25", exe,
                            "3A7F2C9E41B05D86E2F4A9C1077B3D58C6E1F2A49B8D0E7C35A6F1B2C4D9E087", by, kind == "asr" || kind == "asr-excluded", said, kind == "asr-excluded"), null));
                        if (arg("previewtried") == "yes")
                        {
                            AllowResult.Visibility = Visibility.Visible; // /previewtried=yes: after a Try again that failed
                            AllowResult.Text = $"Last tried at {DateTime.Now:t}.";
                        }
                        Show(i);
                        Shoot($"{i + 1}-{pages[i].Key}");
                        continue;
                    }
#endif
                    ShowResult(null);
                    Show(i);
                }
                else
                {
                    Show(i);
                }
                Shoot($"{i + 1}-{pages[i].Key}");
            }
#if PREVIEW
            // /previewedit=yes: the settings, and the pages Change opens from them.
            if (mode == Mode.Install && arg("previewedit") == "yes")
            {
                Show(pages.FindIndex(p => p.Key == "settings"));
                Shoot("edit-settings");
                foreach (var key in new[] { "address", "security", "options" })
                {
                    editing = key;
                    Show(pages.FindIndex(p => p.Key == key));
                    if (key == "address")
                    {
                        CheckDnsNow();
                    }
                    Shoot("edit-" + key);
                }
                editing = null;
            }
#endif
        }
    }
}
