using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RabbidLab.Client;

/// <summary>
/// The tray icon for whoever is signed in. A click opens the client's window: whether this computer is connected, to
/// which server, and the details. It starts at sign-in, and Windows starts it again after the client updates.
/// </summary>
internal sealed class Tray : ApplicationContext
{
    /// <summary>Set by a second copy (the Start menu shortcut) to have the one that's running open its window.</summary>
    public const string ShowSignal = @"Local\RabbidLab.Client.Show";

    private readonly NotifyIcon icon;
    private readonly ToolStripMenuItem heading = new() { Enabled = false };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 5000 };
    private readonly Control pump = new(); // a handle on this thread, for the signal to come back to
    private readonly EventWaitHandle signal = new(false, EventResetMode.AutoReset, ShowSignal);
    private StatusWindow? window;
    private Status? last;
    private int shownTries; // how often the taskbar switch was looked for; -1 once it's settled
    private readonly DateTime written = Written(); // this program's file as it was when the icon started

    public Tray(bool open)
    {
        RegisterApplicationRestart("--tray", 0);
        var menu = new ContextMenuStrip();
        menu.Items.Add(heading);
        menu.Items.Add(new ToolStripSeparator());
        var show = new ToolStripMenuItem("Open", null, (_, _) => Show());
        show.Font = new Font(show.Font, FontStyle.Bold);
        menu.Items.Add(show);
        menu.Items.Add(new ToolStripMenuItem("Open Rabbid Lab in browser", null, (_, _) => Browse()));
        icon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Show();
            }
        };
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        Refresh();
        pump.CreateControl();
        ThreadPool.RegisterWaitForSingleObject(signal, (_, _) =>
        {
            try
            {
                pump.BeginInvoke((Action) Show);
            }
            catch (InvalidOperationException)
            {
                // Closing.
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        if (open)
        {
            Show();
        }
    }

    private void Refresh()
    {
        // An update replaced this program, or it was uninstalled, while the icon was up: this icon goes, and after an
        // update the new program's takes its place. The service starts one too, for anyone signed in without (TrayStarter).
        if (Written() != written)
        {
            try
            {
                if (File.Exists(Application.ExecutablePath))
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--tray") { UseShellExecute = false })?.Dispose();
                }
            }
            catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException)
            {
                // The service starts it within a minute.
            }
            Quit();
            return;
        }
        LeaveToWindows();
        var now = Status.Read() ?? new Status();
        heading.Text = now.Describe();
        var tip = $"Rabbid Lab: {now.Describe()}";
        icon.Text = tip.Length > 63 ? tip.Substring(0, 60) + "…" : tip;
        // Approved: said once, when it happens.
        if (last?.State == Status.Waiting && now.State == Status.Connected)
        {
            icon.ShowBalloonTip(5000, "Rabbid Lab", $"This computer was approved and is now connected to {now.Host}.", ToolTipIcon.Info);
        }
        last = now;
        if (window is { IsDisposed: false, Visible: true })
        {
            window.Draw(now);
        }
    }

    // Versions 1.1.155 to 1.1.161 turned on this icon's switch under Settings, Personalization, Taskbar, Other system
    // tray icons, which put it on the taskbar itself. Where it sits is Windows' and the person's choice: the switch is
    // put back once, so the icon is under the arrow with the others, and it's never touched again after that.
    private void LeaveToWindows()
    {
        if (shownTries < 0)
        {
            return;
        }
        try
        {
            using var mine = Registry.CurrentUser.CreateSubKey(@"Software\Rabbid Lab\Client");
            using var all = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (all is null || mine.GetValue("TaskbarIcon") is string)
            {
                shownTries = -1; // not Windows 11, or done before
                return;
            }
            var exe = Application.ExecutablePath;
            var tail = "\\" + Path.Combine(Path.GetFileName(Path.GetDirectoryName(exe) ?? ""), Path.GetFileName(exe));
            var found = false;
            var changed = false;
            foreach (var name in all.GetSubKeyNames())
            {
                using var one = all.OpenSubKey(name, writable: true);
                if (one?.GetValue("ExecutablePath") is not string path || !path.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                found = true;
                if (one.GetValue("IsPromoted") is int on && on == 1)
                {
                    one.SetValue("IsPromoted", 0, RegistryValueKind.DWord);
                    changed = true;
                }
            }
            // Windows lists the icon a moment after it first shows: looked for a few times, then left.
            if (!found && shownTries++ < 12)
            {
                return;
            }
            mine.SetValue("TaskbarIcon", "left to Windows", RegistryValueKind.String);
            shownTries = -1;
            if (changed)
            {
                // Windows reads the switch when an icon is added, so the icon is added again.
                icon.Visible = false;
                icon.Visible = true;
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            shownTries = -1;
        }
    }

    // When this program's file was last written; the earliest time there is when the file is gone or can't be looked at.
    private static DateTime Written()
    {
        try
        {
            var exe = Application.ExecutablePath;
            return File.Exists(exe) ? File.GetLastWriteTimeUtc(exe) : DateTime.MinValue;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private void Browse()
    {
        var server = Settings.Read().Server;
        if (server is null)
        {
            Show();
            return;
        }
        Process.Start(new ProcessStartInfo(server) { UseShellExecute = true });
    }

    private void Show()
    {
        if (window is null || window.IsDisposed)
        {
            window = new StatusWindow(Browse);
        }
        window.Draw(Status.Read() ?? last ?? new Status());
        window.Show();
        if (window.WindowState == FormWindowState.Minimized)
        {
            window.WindowState = FormWindowState.Normal;
        }
        window.Activate();
    }

    private void Quit()
    {
        timer.Stop();
        icon.Visible = false;
        ExitThread();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string commandLine, int flags);
}

/// <summary>
/// The client's window: connected or not, what to do about it, the details as a list, and the buttons. Dark, like
/// Rabbid Lab. Sizes are given at 96 DPI and Windows scales them, so nothing is cut off on a scaled screen.
/// </summary>
internal sealed class StatusWindow : Form
{
    private readonly Label state = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 11.5f), Margin = new Padding(0, 14, 0, 0) };
    private readonly Label hint = new() { AutoSize = true, ForeColor = Look.Muted, Margin = new Padding(0, 4, 0, 0), MaximumSize = new Size(416, 0) };
    private readonly TableLayoutPanel facts = new()
    {
        ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
        BackColor = Look.Field, Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 16, 0, 0),
    };
    private readonly List<Label> said = new(); // each fact's value, made once (Draw)
    private float madeFor; // the screen scale those were made for
    private readonly Button connect = Look.Button("Connect to a server", primary: false);
    private readonly Button remove = Look.Button("Uninstall", primary: false);
    private Status shown = new();
    private float fit = 1f; // how much the window was scaled for its screen

    public StatusWindow(Action browse)
    {
        SuspendLayout();
        // Sizes are written for 96 DPI. Look.Fit makes them the screen's when the window first shows.
        AutoScaleMode = AutoScaleMode.None;
        Text = "Rabbid Lab Client";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Look.Night;
        ForeColor = Look.Ink;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(460, 412);
        Padding = new Padding(22, 20, 22, 20);

        var title = new Label { Text = "Rabbid Lab Client", AutoSize = true, Font = new Font("Segoe UI", 16f, FontStyle.Bold), ForeColor = Color.White, Margin = new Padding(0) };
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var open = Look.Button("Open Rabbid Lab", primary: true);
        open.Click += (_, _) => browse();
        connect.Click += (_, _) => new ConnectWindow(new JoinLink { Server = Settings.Read().Server ?? "" }).Show();
        remove.Click += (_, _) => Uninstall();
        var right = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Right };
        right.Controls.Add(open);
        right.Controls.Add(connect);
        var buttons = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        remove.Anchor = AnchorStyles.Left;
        buttons.Controls.Add(remove, 0, 0);
        buttons.Controls.Add(right, 1, 0);

        // Top to bottom: the title, how it is, what to do, the details, then the buttons kept at the foot.
        var page = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0) };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var row in new Control[] { title, state, hint, facts })
        {
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.Controls.Add(row);
        }
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(new Panel { Height = 1, Margin = new Padding(0) });
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(buttons);
        Controls.Add(page);
        ResumeLayout(false);
        PerformLayout();
    }

    public void Draw(Status s)
    {
        shown = s;
        var tone = s.State switch
        {
            Status.Connected => Look.Green,
            Status.Waiting or Status.Starting => Look.Amber,
            _ => Look.Red,
        };
        state.Text = $"●  {s.Describe()}";
        state.ForeColor = tone;
        hint.Text = s.Hint();
        connect.Visible = s.State is Status.NoServer or Status.Unreachable;
        var joined = s.State is Status.Connected or Status.Waiting or Status.Unreachable or Status.Refused;
        var rows = new (string Label, string Value)[]
        {
            ("Server", s.Server ?? "Not set"),
            ("Computer name", s.Computer),
            ("Signed-in user", string.IsNullOrEmpty(s.User) ? Environment.UserName : s.User!),
            ("Last check-in", s.CheckedIn is { } at ? at.ToLocalTime().ToString("g") : "Never"),
            ("Client version", s.Version),
            ("Server version", s.ServerVersion ?? "Unknown"),
            ("Uninstall", !joined ? "Allowed (not connected to a server)"
                : s.Locked ? $"Locked by {s.Host}"
                : s.Unlocked == Status.ByOffline ? $"Allowed (server not reached for {Removal.OfflineDays} days)"
                : s.RemovalUntil is { } until ? $"Allowed until {until.ToLocalTime():t}{(s.Unlocked == Status.ByCode ? " (uninstall code)" : "")}" : "Allowed"),
        };
        // The rows are made once, and again only when the window learns its screen's scale (OnLoad). After that only a
        // value that changed is set: clearing and making them all again every five seconds flashed the whole box, and
        // left the old labels behind.
        if (said.Count != rows.Length || madeFor != fit)
        {
            facts.SuspendLayout();
            while (facts.Controls.Count > 0)
            {
                facts.Controls[0].Dispose(); // which takes it out of the table too
            }
            facts.RowStyles.Clear();
            facts.RowCount = 0;
            said.Clear();
            madeFor = fit;
            var gap = (int) Math.Round(4 * fit);
            foreach (var (label, value) in rows)
            {
                facts.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                facts.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Look.Muted, Margin = new Padding(0, gap, 0, gap) });
                var text = new Label { Text = value, AutoSize = true, ForeColor = Look.Ink, Margin = new Padding(0, gap, 0, gap), MaximumSize = new Size((int) Math.Round(250 * fit), 0) };
                facts.Controls.Add(text);
                said.Add(text);
            }
            facts.ResumeLayout();
            return;
        }
        for (var i = 0; i < rows.Length; i++)
        {
            if (said[i].Text != rows[i].Value)
            {
                said[i].Text = rows[i].Value;
            }
        }
    }

    // Uninstalling goes through Windows Installer, which refuses unless the server has allowed it (Removal).
    private void Uninstall()
    {
        if (!Removal.Allowed)
        {
            // Locked: the uninstall code, asked for by this program run as an administrator, which can then unlock it (Unlock.cs).
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--unlock") { Verb = "runas", UseShellExecute = true })?.Dispose();
            }
            catch (Win32Exception)
            {
                // No administrator said yes: it stays locked.
            }
            return;
        }
        if (MessageBox.Show(this, "Uninstall the Rabbid Lab client from this computer?", "Rabbid Lab Client", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        if (!Removal.Uninstall(quiet: false))
        {
            MessageBox.Show(this, "The uninstall didn't start. Use Settings > Apps > Installed apps instead.", "Rabbid Lab Client", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        fit = Look.Fit(this);
        Draw(shown);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Look.DarkTitle(Handle);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing only hides it; the icon stays.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}

/// <summary>Rabbid Lab's colours and buttons, for the client's two windows.</summary>
internal static class Look
{
    public static readonly Color Night = Color.FromArgb(0x11, 0x13, 0x1D);
    public static readonly Color Field = Color.FromArgb(0x1B, 0x1E, 0x2B);
    public static readonly Color Line = Color.FromArgb(0x2A, 0x2D, 0x3E);
    public static readonly Color Ink = Color.FromArgb(0xE6, 0xE8, 0xF2);
    public static readonly Color Muted = Color.FromArgb(0x8A, 0x8F, 0xA8);
    public static readonly Color Amber = Color.FromArgb(0xFF, 0x8A, 0x3D);
    public static readonly Color Red = Color.FromArgb(0xFF, 0x6B, 0x7D);
    public static readonly Color Green = Color.FromArgb(0x5F, 0xD4, 0xA0);
    public static readonly Color Accent = Color.FromArgb(0xE8, 0x54, 0x2F);

    /// <summary>A flat button as wide as its words: ember for the main one, a quiet outline for the rest.</summary>
    public static Button Button(string text, bool primary)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 36),
            Padding = new Padding(10, 0, 10, 0),
            Margin = new Padding(8, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Field,
            ForeColor = primary ? Color.White : Ink,
            Font = new Font("Segoe UI Semibold", 9.75f),
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = primary ? 0 : 1;
        b.FlatAppearance.BorderColor = Line;
        if (primary)
        {
            // Greyed while it can't be clicked.
            b.EnabledChanged += (_, _) => b.BackColor = b.Enabled ? Accent : Line;
        }
        return b;
    }

    /// <summary>A text box in the window's colours, in a thin frame that turns ember while it has the cursor.</summary>
    public static Panel Box(TextBox box)
    {
        box.BorderStyle = BorderStyle.None;
        box.BackColor = Field;
        box.ForeColor = Ink;
        box.Dock = DockStyle.Fill;
        var inside = new Panel { Dock = DockStyle.Fill, BackColor = Field, Padding = new Padding(9, 7, 9, 0) };
        inside.Controls.Add(box);
        var frame = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Line, Padding = new Padding(1) };
        frame.Controls.Add(inside);
        box.Enter += (_, _) => frame.BackColor = Accent;
        box.Leave += (_, _) => frame.BackColor = Line;
        return frame;
    }

    /// <summary>Windows' administrator shield on one of these buttons, for what asks for an administrator.</summary>
    public static void Shield(Button button)
    {
        try
        {
            var info = new StockIcon { Size = Marshal.SizeOf(typeof(StockIcon)) };
            if (SHGetStockIconInfo(77, 0x101, ref info) != 0 || info.Icon == IntPtr.Zero) // the shield, small
            {
                return;
            }
            using (var icon = Icon.FromHandle(info.Icon))
            {
                button.Image = icon.ToBitmap();
            }
            DestroyIcon(info.Icon);
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Text = " " + button.Text; // a little room between the shield and the word
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // No shield: the button works the same.
        }
    }

    /// <summary>
    /// Scales a window written for 96 DPI to the screen it opened on, once, and says by how much. Text is already drawn
    /// at the screen's size; this makes the window and everything in it match, so nothing is cut off.
    /// </summary>
    public static float Fit(Form window)
    {
        float dpi;
        try
        {
            dpi = GetDpiForWindow(window.Handle);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            using var screen = window.CreateGraphics();
            dpi = screen.DpiX;
        }
        var by = dpi <= 0 ? 1f : dpi / 96f;
        if (Math.Abs(by - 1f) > 0.01f)
        {
            window.Scale(new SizeF(by, by));
            var area = Screen.FromControl(window).WorkingArea;
            window.Location = new Point(area.Left + (area.Width - window.Width) / 2, area.Top + (area.Height - window.Height) / 2);
        }
        return by;
    }

    /// <summary>The window's own title bar dark too, where Windows can (10 and 11).</summary>
    public static void DarkTitle(IntPtr window)
    {
        try
        {
            var on = 1;
            DwmSetWindowAttribute(window, 20, ref on, sizeof(int));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // An older Windows: the title bar stays as it is.
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StockIcon
    {
        public int Size;
        public IntPtr Icon;
        public int SystemIndex;
        public int Index;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Path;
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetStockIconInfo(int id, int flags, ref StockIcon info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
