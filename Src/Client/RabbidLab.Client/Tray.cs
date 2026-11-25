using System;
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
/// The tray icon for whoever is signed in: the lab's mark with a dot for the state. A click opens the client's window
/// (ClientWindow); its menu says how it is and opens it. It starts at sign-in, and Windows starts it again after the
/// client updates. Never Open Rabbid Lab in the browser: people need to know the console's address themselves.
/// </summary>
internal sealed class Tray : ApplicationContext
{
    /// <summary>Set by a second copy (the Start menu shortcut) to have the one that's running open its window.</summary>
    public const string ShowSignal = @"Local\RabbidLab.Client.Show";

    private readonly NotifyIcon icon;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem heading = new() { Enabled = false };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 5000 };
    private readonly Control pump = new(); // a handle on this thread, for the signal to come back to
    private readonly EventWaitHandle signal = new(false, EventResetMode.AutoReset, ShowSignal);
    private ClientWindow? window;
    private Status? last;
    private Ink ink = Ink.Now(null);
    private string? drawn; // what the tray icon was last made for
    private int shownTries; // how often the taskbar switch was looked for; -1 once it's settled
    private readonly DateTime written = Written(); // this program's file as it was when the icon started

    public Tray(bool open)
    {
        RegisterApplicationRestart("--tray", 0);
        menu.Items.Add(heading);
        menu.Items.Add(new ToolStripSeparator());
        var show = new ToolStripMenuItem("Open", null, (_, _) => Show());
        show.Font = new Font(show.Font, FontStyle.Bold);
        menu.Items.Add(show);
        menu.Renderer = new MenuLook(ink);
        icon = new NotifyIcon
        {
            Icon = TrayMark.Make(ink, "off"),
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
        // An update asks this program to close before it replaces its file, the way signing out does. Leaving at once
        // takes the icon down with it. Waiting to be stopped left a dead icon in the tray after every update, which only
        // went when the mouse passed over it.
        SystemEvents.SessionEnding += Leaving;
        SystemEvents.SessionEnded += Left;
        SystemEvents.UserPreferenceChanged += Themed;
        Application.ApplicationExit += (_, _) => icon.Visible = false;
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
        var lab = now.Lab?.Name is { Length: > 0 } n ? n : "Rabbid Lab";
        ink = Ink.Now(now.Lab?.Accent);
        Look.Accent = ink.Accent;
        heading.Text = ClientWindow.Words(now);
        var tip = $"{lab}: {ClientWindow.Words(now)}";
        icon.Text = tip.Length > 63 ? tip.Substring(0, 60) + "…" : tip;
        Mark(now);
        // Approved: said once, when it happens.
        if (last?.State == Status.Waiting && now.State == Status.Connected)
        {
            Toast.Show(ink, lab, "This computer was approved and is now connected.");
        }
        last = now;
        if (window is { IsDisposed: false, Visible: true })
        {
            window.Draw(now);
        }
    }

    // The tray icon (and the window's) made again when the state, the accent, the light or the lab's icon changed.
    private void Mark(Status now)
    {
        var tone = now.State switch
        {
            Status.Connected => "ok",
            Status.Waiting or Status.Starting or Status.Refused => "warn",
            Status.Unreachable or Status.IdentityUnreadable => "bad",
            _ => "off",
        };
        var own = File.Exists(Logbook.IconFile) ? File.GetLastWriteTimeUtc(Logbook.IconFile).Ticks : 0;
        var key = $"{tone}|{ink.Accent.ToArgb()}|{ink.Light}|{own}";
        if (key == drawn)
        {
            return;
        }
        drawn = key;
        menu.Renderer = new MenuLook(ink);
        var made = TrayMark.Make(ink, tone);
        icon.Icon = made;
        if (window is { IsDisposed: false })
        {
            window.Icon = made;
        }
        TrayMark.Release();
    }

    private void Themed(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            drawn = null;
            pump.BeginInvoke((Action) Refresh);
        }
    }

    private void Say(string title, string text) => Toast.Show(ink, title, text);

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

    private void Show()
    {
        if (window is null || window.IsDisposed)
        {
            window = new ClientWindow(Say) { Icon = icon.Icon };
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
        SystemEvents.SessionEnding -= Leaving;
        SystemEvents.SessionEnded -= Left;
        SystemEvents.UserPreferenceChanged -= Themed;
        icon.Visible = false;
        ExitThread();
    }

    private void Leaving(object? sender, SessionEndingEventArgs e) => Quit();

    private void Left(object? sender, SessionEndedEventArgs e) => Quit();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string commandLine, int flags);
}

/// <summary>The colours and buttons of the Connect and Unlock windows (the main window draws its own, ClientWindow).</summary>
internal static class Look
{
    // Ink, as the client's window (Ink.cs): the Connect and Unlock windows wear it too, with the lab's accent on the main button.
    public static readonly Color Night = Color.FromArgb(0x0F, 0x0F, 0x12);
    public static readonly Color Field = Color.FromArgb(0x16, 0x16, 0x19);
    public static readonly Color Line = Color.FromArgb(0x27, 0x27, 0x2A);
    public static readonly Color Ink = Color.FromArgb(0xEC, 0xEC, 0xEF);
    public static readonly Color Muted = Color.FromArgb(0xA1, 0xA1, 0xAA);
    public static readonly Color Amber = Color.FromArgb(0xF2, 0xBE, 0x6B);
    public static readonly Color Red = Color.FromArgb(0xFF, 0x6B, 0x7D);
    public static readonly Color Green = Color.FromArgb(0x5F, 0xD4, 0xA0);
    public static Color Accent { get; set; } = Color.FromArgb(0x59, 0x00, 0xFF);

    /// <summary>A flat button as wide as its words: the lab's accent for the main one, a quiet outline for the rest.</summary>
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

    /// <summary>A text box in the window's colours, in a thin frame that turns the accent while it has the cursor.</summary>
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

    /// <summary>
    /// WM_DPICHANGED: a window moved to a screen with another scale. Forms on .NET Framework never pass it on without an
    /// App.config switch, so the drawn windows (ClientWindow, AskWindow) read it themselves.
    /// </summary>
    public const int DpiChanged = 0x02E0;

    /// <summary>The scale of the screen a window is on, in DPI; 0 where Windows can't say.</summary>
    public static int Dpi(IntPtr window)
    {
        try
        {
            return (int) GetDpiForWindow(window);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    /// <summary>What WM_DPICHANGED says: the new DPI, and where Windows puts the window's corner.</summary>
    public static (int Dpi, Point At) Moved(Message m)
    {
        var to = Marshal.PtrToStructure<Rect>(m.LParam);
        return ((int) ((long) m.WParam & 0xFFFF), new Point(to.Left, to.Top));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
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
