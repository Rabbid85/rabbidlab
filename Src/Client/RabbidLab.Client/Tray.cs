using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

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

    public Tray(bool open)
    {
        RegisterApplicationRestart("--tray", 0);
        var menu = new ContextMenuStrip();
        menu.Items.Add(heading);
        menu.Items.Add(new ToolStripSeparator());
        var show = new ToolStripMenuItem("Open", null, (_, _) => Show());
        show.Font = new Font(show.Font, FontStyle.Bold);
        menu.Items.Add(show);
        menu.Items.Add(new ToolStripMenuItem("Open Rabbid Lab in the browser", null, (_, _) => Browse()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Close the icon", null, (_, _) => Quit()));
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
        var now = Status.Read() ?? new Status();
        heading.Text = now.Describe();
        var tip = $"Rabbid Lab: {now.Describe()}";
        icon.Text = tip.Length > 63 ? tip.Substring(0, 60) + "…" : tip;
        // Let in: said once, when it happens.
        if (last?.State == Status.Waiting && now.State == Status.Connected)
        {
            icon.ShowBalloonTip(5000, "Rabbid Lab", $"This computer is connected to {now.Host}.", ToolTipIcon.Info);
        }
        last = now;
        if (window is { IsDisposed: false, Visible: true })
        {
            window.Draw(now);
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
        facts.SuspendLayout();
        facts.Controls.Clear();
        facts.RowStyles.Clear();
        facts.RowCount = 0;
        Fact("Server", s.Server ?? "Not set");
        Fact("This computer", s.Computer);
        Fact("Signed in", string.IsNullOrEmpty(s.User) ? Environment.UserName : s.User!);
        Fact("Last check-in", s.CheckedIn is { } at ? at.ToLocalTime().ToString("g") : "Not yet");
        Fact("Client version", s.Version);
        Fact("Server version", s.ServerVersion ?? "Not known");
        Fact("Uninstalling", !joined ? "Allowed: not connected to a server"
            : s.Locked ? $"Locked by {s.Host}"
            : s.RemovalUntil is { } until ? $"Allowed until {until.ToLocalTime():t}" : "Allowed");
        facts.ResumeLayout();
    }

    private void Fact(string label, string value)
    {
        facts.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var gap = (int) Math.Round(4 * fit);
        facts.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Look.Muted, Margin = new Padding(0, gap, 0, gap) });
        facts.Controls.Add(new Label { Text = value, AutoSize = true, ForeColor = Look.Ink, Margin = new Padding(0, gap, 0, gap), MaximumSize = new Size((int) Math.Round(250 * fit), 0) });
    }

    // Uninstalling goes through Windows Installer, which refuses unless the server has allowed it (Removal).
    private void Uninstall()
    {
        if (!Removal.Allowed)
        {
            MessageBox.Show(this, $"Uninstalling is locked by {shown.Host}.\n\nAn admin has to allow it in Rabbid Lab first: Seats, {shown.Computer}, Uninstall.",
                "Rabbid Lab Client", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, "Uninstall the Rabbid Lab client from this computer?", "Rabbid Lab Client", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        if (!Removal.Uninstall(quiet: false))
        {
            MessageBox.Show(this, "Windows couldn't start the uninstall. Use Settings, Apps, Installed apps.", "Rabbid Lab Client", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        return b;
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
}
