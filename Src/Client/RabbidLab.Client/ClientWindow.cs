using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RabbidLab.Client;

/// <summary>
/// The client's window, drawn as the Ink mock has it: the state in a panel down the left (the lab's mark and name, a ring
/// that counts down to the next check-in, Check in now, today's jobs and problems, Uninstall and the version), and at the
/// right the computer, then Details (how to reach IT, Ask IT for help, this computer, Copy for IT) and Activity (the last
/// day's check-ins as bars, then what the client did here). Never the server's address or version, an admin's name or a
/// script's. Light or dark as Windows' apps are; sizes are at 96 DPI and scaled for the screen it's on.
/// </summary>
internal sealed class ClientWindow : Form
{
    private const float W = 880, H = 600, Aside = 300;
    private readonly Action<string, string> toast;
    private readonly System.Windows.Forms.Timer tick = new() { Interval = 50 };
    private readonly ToolTip tip = new() { InitialDelay = 400 };
    private readonly List<(RectangleF R, string Id, Action? Click, string? Tip)> hits = new();
    private Status s = new();
    private Ink ink = Ink.Now(null);
    private float k = 1f;
    private string page = "details";
    private string? hover, press, tipFor;
    private bool asking;
    private DateTime? tickAt; // a check-in Check in now asked for just came: the button says so for a moment
    private RectangleF busy; // the button while it works or ticks, drawn again on the tick
    private AskWindow? asked; // Ask IT for help, while it's open
    private DateTime? doneAt; // the ring's finish when a check-in just came
    private RectangleF ring;
    private Font? fBrand, fTitle, fSub, fBtn, fNum, fSmall, fName, fMeta, fTab, fH3, fRow, fRing, fAxis;
    private string? ip;
    private (string? Maker, string? Model, string? Serial)? read; // read from Windows when the inventory hasn't said yet

    public ClientWindow(Action<string, string> toast)
    {
        this.toast = toast;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Rabbid Lab";
        ShowInTaskbar = true;
        KeyPreview = true;
        Scale(DeviceDpi);
        tick.Tick += (_, _) =>
        {
            Invalidate(Rectangle.Inflate(Rectangle.Round(ring), 4, 4));
            if (!busy.IsEmpty)
            {
                Invalidate(Rectangle.Inflate(Rectangle.Round(busy), 2, 2));
            }
        };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += Themed;
    }

    // --- Size and look ---------------------------------------------------------------------------------------------------

    // At the scale of the screen it's on, never bigger than that screen (a small screen at a large scale draws it smaller).
    // A new scale keeps the window's middle where it was, or puts its corner where Windows says when it was dragged.
    private void Scale(int dpi, Point? at = null)
    {
        var was = Bounds;
        var area = Screen.FromRectangle(at is { } p ? new Rectangle(p, was.Size) : was).WorkingArea;
        k = Math.Min(dpi <= 0 ? 1f : dpi / 96f, Math.Min(area.Width * 0.94f / W, area.Height * 0.94f / H));
        var size = new Size((int) Math.Round(W * k), (int) Math.Round(H * k));
        if (!IsHandleCreated)
        {
            ClientSize = size;
        }
        else if (at is { } corner)
        {
            Bounds = new Rectangle(corner, size);
        }
        else
        {
            var x = Math.Max(area.Left, Math.Min(was.X + (was.Width - size.Width) / 2, area.Right - size.Width));
            var y = Math.Max(area.Top, Math.Min(was.Y + (was.Height - size.Height) / 2, area.Bottom - size.Height));
            Bounds = new Rectangle(x, y, size.Width, size.Height);
        }
        Font F(string face, float px) => new(face, px * k, FontStyle.Regular, GraphicsUnit.Pixel);
        const string semi = "Segoe UI Semibold", plain = "Segoe UI";
        foreach (var f in new[] { fBrand, fTitle, fSub, fBtn, fNum, fSmall, fName, fMeta, fTab, fH3, fRow, fRing, fAxis })
        {
            f?.Dispose();
        }
        (fBrand, fTitle, fSub, fBtn, fNum, fSmall) = (F(semi, 15), F(semi, 21), F(plain, 13), F(semi, 13), F(semi, 19), F(plain, 12));
        (fName, fMeta, fTab, fH3, fRow, fRing, fAxis) = (F(semi, 18), F(plain, 12.5f), F(semi, 14), F(semi, 13), F(plain, 13), F(semi, 29), F(plain, 11.5f));
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Look.DpiChanged)
        {
            var (dpi, at) = Look.Moved(m);
            Scale(dpi, at);
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var round = 2; // Windows 11's rounded corners on a window without its own frame
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        }
        catch (Exception x) when (x is DllNotFoundException or EntryPointNotFoundException)
        {
            // Square corners before Windows 11.
        }
        // Made at the main screen's scale; opened on another one (where the mouse was), it's drawn at that one's.
        if (Look.Dpi(Handle) is var dpi and > 0 && dpi != DeviceDpi)
        {
            Scale(dpi);
        }
    }

    private void Themed(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Color)
        {
            BeginInvoke((Action) (() => { ink = Ink.Now(s.Lab?.Accent); Invalidate(); }));
        }
    }

    // --- What it shows ---------------------------------------------------------------------------------------------------

    /// <summary>The status as the service last wrote it.</summary>
    public void Draw(Status now)
    {
        if (now.CheckedIn != s.CheckedIn && s.CheckedIn is not null && now.State == Status.Connected)
        {
            doneAt = DateTime.UtcNow;
        }
        s = now;
        ink = Ink.Now(s.Lab?.Accent);
        BackColor = ink.Bg;
        Text = Lab;
        tick.Enabled = Visible && s.State is Status.Connected or Status.Unreachable or Status.Waiting;
        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        tick.Enabled = Visible && s.State is Status.Connected or Status.Unreachable or Status.Waiting;
        if (Visible)
        {
            ip = null;
            Hardware();
        }
    }

    private string Lab => s.Lab?.Name is { Length: > 0 } n ? n : "Rabbid Lab";

    private static string Tone(string state) => state switch
    {
        Status.Connected => "ok",
        Status.Waiting or Status.Starting or Status.Refused => "warn",
        Status.Unreachable or Status.IdentityUnreadable => "bad",
        _ => "off",
    };

    private string Title => Words(s);

    /// <summary>The state in a word or two, for the window, the tray icon's menu and its hint: never the server's name.</summary>
    public static string Words(Status s) => s.State switch
    {
        Status.Connected => "Connected",
        Status.Waiting => "Waiting for approval",
        Status.Unreachable => "Cannot reach the server",
        Status.NoServer => "Not connected",
        Status.Refused => "The server is busy",
        Status.Removed => "Removed",
        Status.IdentityUnreadable => "Cannot read its credentials",
        _ => "Starting",
    };

    private string[] Lead()
    {
        string At(DateTime? utc) => utc is { } t ? t.ToLocalTime().ToString("h:mm tt", CultureInfo.CurrentCulture) : "";
        return s.State switch
        {
            Status.Connected => new[] { $"Checked in at {At(s.CheckedIn)}.", "The ring counts down to the next." },
            Status.Waiting => new[] { $"{Lab} has this computer's request.", "An admin needs to approve it." },
            Status.Unreachable => new[] { s.LastOk is { } l ? $"No answer since {At(l)}." : "No answer from the server.", "The ring counts down to the next try." },
            Status.NoServer => new[] { $"Connect to a server here, or open {Lab} on this computer and click Connect this computer." },
            Status.Refused => new[] { "It tries again in a few minutes." },
            Status.Removed => new[] { "It was taken out of Devices. It reconnects by itself." },
            Status.IdentityUnreadable => new[] { "Reinstall the client, or contact your IT team." },
            _ => new[] { "Waiting for the Rabbid Lab Client service to start." },
        };
    }

    // --- Drawing ---------------------------------------------------------------------------------------------------------

    private RectangleF R(float x, float y, float w, float h) => new(x * k, y * k, w * k, h * k);

    private void Say(Graphics g, string text, Font? f, Color c, RectangleF r, TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top) =>
        TextRenderer.DrawText(g, text, f, Rectangle.Round(r), c, flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

    private float Measure(Graphics g, string text, Font? f, float width, bool wrap) =>
        TextRenderer.MeasureText(g, text, f, new Size((int) (width * k), int.MaxValue), (wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine) | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height / k;

    private float TextWidth(Graphics g, string text, Font? f) =>
        TextRenderer.MeasureText(g, text, f, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width / k;

    private void Hit(RectangleF r, string id, Action? click, string? tipText = null) => hits.Add((r, id, click, tipText));

    private void Button(Graphics g, RectangleF r, string label, string id, Action click, bool primary, string? glyph = null)
    {
        var on = hover == id;
        var down = press == id;
        using (var p = Ink.Round(r, 8 * k))
        {
            var back = primary ? (down ? Ink.Mix(ink.Accent, Color.Black, 0.12f) : on ? Ink.Mix(ink.Accent, Color.White, 0.14f) : ink.Accent) : on ? ink.Hover : ink.Btn;
            using var b = new SolidBrush(back);
            g.FillPath(b, p);
            if (!primary)
            {
                using var line = new Pen(ink.BtnLine, k);
                g.DrawPath(line, p);
            }
        }
        var fore = primary ? Color.White : ink.Head;
        if (glyph == "dots")
        {
            // While it works ("When clicked add animation to let user know something is fucking happening."): the words, then
            // three dots pulsing one after another, as in the ring while it waits for approval.
            var tw = TextWidth(g, label, fBtn) * k;
            var w = tw + 6 * k + 20 * k;
            var x = r.X + (r.Width - w) / 2;
            Say(g, label, fBtn, fore, new RectangleF(x, r.Y, tw + 4 * k, r.Height), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            var ms = DateTime.UtcNow.TimeOfDay.TotalMilliseconds;
            for (var i = 0; i < 3; i++)
            {
                var t = ((ms - i * 200) % 1400 + 1400) % 1400 / 1400;
                var o = t < 0.4 ? 0.22 + 0.78 * (t / 0.4) : t < 0.8 ? 1 - 0.78 * ((t - 0.4) / 0.4) : 0.22;
                using var dot = new SolidBrush(Color.FromArgb((int) (o * 255), fore));
                g.FillEllipse(dot, x + tw + 6 * k + i * 8 * k, r.Y + r.Height / 2 + 1 * k, 4 * k, 4 * k);
            }
        }
        else if (glyph is null)
        {
            Say(g, label, fBtn, fore, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        else
        {
            var w = TextWidth(g, label, fBtn) * k + 23 * k;
            var x = r.X + (r.Width - w) / 2;
            Glyphs.Draw(g, glyph, new RectangleF(x, r.Y + (r.Height - 15 * k) / 2, 15 * k, 15 * k), fore, 1.8f);
            Say(g, label, fBtn, fore, new RectangleF(x + 23 * k, r.Y, r.Width, r.Height), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        Hit(r, id, click);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ink.Smooth(g);
        hits.Clear();
        busy = RectangleF.Empty;
        g.Clear(ink.Bg);
        using (var aside = new SolidBrush(ink.Aside))
        {
            g.FillRectangle(aside, R(0, 0, Aside, H));
        }
        using (var line = new Pen(ink.Line, Math.Max(1, k)))
        {
            g.DrawLine(line, Aside * k, 0, Aside * k, H * k);
        }
        PaintAside(g);
        PaintMain(g);
        using var edge = new Pen(ink.Edge, Math.Max(1, k));
        g.DrawRectangle(edge, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    private void PaintAside(Graphics g)
    {
        // The lab: its mark and its name (two lines at most).
        Glyphs.Mark(g, R(24, 22, 28, 28), ink.AccentSoft);
        var nameH = Math.Min(Measure(g, Lab, fBrand, 214, wrap: true), 40);
        Say(g, Lab, fBrand, ink.Head, R(62, 36 - nameH / 2, 214, nameH + 2), TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

        // Today's jobs and problems over the foot: Jobs run at the left over Uninstall, Problems at the right over the version.
        using (var line = new Pen(ink.Line, Math.Max(1, k)))
        {
            g.DrawLine(line, 24 * k, 470 * k, 276 * k, 470 * k);
        }
        Say(g, "Today", fSmall, ink.Muted, R(24, 484, 120, 16));
        Say(g, s.JobsToday.ToString("N0", CultureInfo.CurrentCulture), fNum, ink.Head, R(24, 502, 120, 26));
        Say(g, "Jobs run", fSmall, ink.Muted, R(24, 528, 120, 16));
        Say(g, s.ProblemsToday.ToString("N0", CultureInfo.CurrentCulture), fNum, ink.Head, R(156, 502, 120, 26), TextFormatFlags.Right);
        Say(g, "Problems", fSmall, ink.Muted, R(156, 528, 120, 16), TextFormatFlags.Right);
        var un = R(18, 556, 104, 30);
        if (hover == "uninstall")
        {
            using var p = Ink.Round(un, 6 * k);
            using var b = new SolidBrush(ink.Btn);
            g.FillPath(b, p);
        }
        Glyphs.Draw(g, Removal.Allowed ? "trash" : "lock", R(25, 564, 14, 14), hover == "uninstall" ? ink.Head : ink.Soft, 2f);
        Say(g, "Uninstall", fBtn, hover == "uninstall" ? ink.Head : ink.Soft, R(47, 556, 80, 30), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        Hit(un, "uninstall", Uninstall);
        Say(g, $"{(s.Lab?.Name is { Length: > 0 } ? "Rabbid Lab" : "Version")} {Agent.Version}", fSmall, ink.Muted, R(126, 556, 150, 30), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        // The ring, the state and what to do, in the middle of the room between the lab and today.
        var lead = Lead();
        var leadH = lead.Sum(l => Measure(g, l, fSub, 240, wrap: true));
        var buttons = Buttons();
        var total = 112 + 18 + 28 + 6 + leadH + (buttons.Count > 0 ? 22 + buttons.Count * 38 + (buttons.Count - 1) * 8 : 0);
        var top = 62 + (470 - 62 - total) / 2;
        ring = R(94, top, 112, 112);
        PaintRing(g, ring);
        var y = top + 112 + 18;
        Say(g, Title, fTitle, ink.Head, R(20, y, 260, 28), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        y += 34;
        foreach (var l in lead)
        {
            var h = Measure(g, l, fSub, 240, wrap: true);
            Say(g, l, fSub, ink.Soft, R(30, y, 240, h + 2), TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
            y += h;
        }
        y += 22;
        foreach (var (label, id, click, primary) in buttons)
        {
            var at = R(24, y, 252, 38);
            if (asking && id is "checkin" or "retry")
            {
                busy = at;
                Button(g, at, id == "retry" ? "Trying" : "Checking in", id, click, primary, "dots");
            }
            else if (id == "checkin" && tickAt is { } t && DateTime.UtcNow - t < TimeSpan.FromMilliseconds(1600))
            {
                // Checked in: a tick for a moment, then Check in now again.
                busy = at;
                Button(g, at, "Checked in", id, click, primary, "check");
            }
            else
            {
                Button(g, at, label, id, click, primary);
            }
            y += 46;
        }
    }

    private List<(string Label, string Id, Action Click, bool Primary)> Buttons()
    {
        var list = new List<(string, string, Action, bool)>();
        switch (s.State)
        {
            case Status.Connected or Status.Waiting:
                list.Add(("Check in now", "checkin", CheckIn, true));
                break;
            case Status.Unreachable or Status.Refused:
                list.Add(("Try again now", "retry", CheckIn, true));
                list.Add(("Connect to another server", "connect", Connect, false));
                break;
            case Status.NoServer or Status.Removed:
                list.Add(("Connect to a server", "connect", Connect, true));
                break;
        }
        return list;
    }

    private void PaintRing(Graphics g, RectangleF box)
    {
        var u = box.Width / 120f;
        var c = new RectangleF(box.X + 10 * u, box.Y + 10 * u, 100 * u, 100 * u);
        var now = DateTime.UtcNow;
        var counting = s.State is Status.Connected or Status.Unreachable;
        using (var track = new Pen(ink.Ring, 3 * u))
        {
            if (!counting && s.State != Status.Waiting)
            {
                track.DashStyle = DashStyle.Dot;
                track.Width = 2.5f * u;
            }
            g.DrawEllipse(track, c);
        }
        if (counting)
        {
            // What's left to the next check-in as 4:59 (5 minutes connected, a minute offline), the arc filling over it.
            var every = Status.Every(s.State).TotalSeconds;
            var next = s.Next ?? (s.CheckedIn ?? now).AddSeconds(every);
            var left = Math.Max(0, Math.Min(every, (next - now).TotalSeconds));
            var colour = s.State == Status.Connected ? ink.Head : ink.Bad;
            var secs = (int) Math.Ceiling(left);
            var number = $"{secs / 60}:{secs % 60:00}";
            var alpha = 255;
            var sweep = (float) ((every - left) / every * 360);
            // A check-in just came: the arc finishes its sweep and fades, then starts again; the new number fades in.
            if (doneAt is { } d && (now - d).TotalMilliseconds < 700)
            {
                var ms = (now - d).TotalMilliseconds;
                sweep = 360;
                alpha = ms < 350 ? 255 : (int) (255 * (1 - (ms - 350) / 350));
            }
            else
            {
                doneAt = null;
            }
            if (sweep > 0.5f && alpha > 0)
            {
                using var arc = new Pen(Color.FromArgb(alpha, colour), 3 * u) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(arc, c, -90, sweep);
            }
            var numAlpha = doneAt is { } d2 ? (int) Math.Min(255, 255 * (now - d2).TotalMilliseconds / 450) : 255;
            Say(g, number, fRing, Color.FromArgb(numAlpha, ink.Head), box, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        else if (s.State == Status.Waiting)
        {
            // Waiting ("This is for the waiting for approval"): one white dot goes slowly round the ring, and three dots
            // pulse in the middle one after another, as when someone is typing.
            var ms = now.TimeOfDay.TotalMilliseconds;
            var a = (ms % 4000 / 4000 * 2 * Math.PI) - Math.PI / 2;
            var (cx, cy) = (box.X + 60 * u, box.Y + 60 * u);
            using (var sat = new SolidBrush(ink.Head))
            {
                g.FillEllipse(sat, (float) (cx + Math.Cos(a) * 50 * u - 4.5f * u), (float) (cy + Math.Sin(a) * 50 * u - 4.5f * u), 9 * u, 9 * u);
            }
            for (var i = 0; i < 3; i++)
            {
                // 0.22 to 1 and back over 1.4 s, each a fifth of a second after the one before.
                var t = ((ms - i * 200) % 1400 + 1400) % 1400 / 1400;
                var o = t < 0.4 ? 0.22 + 0.78 * (t / 0.4) : t < 0.8 ? 1 - 0.78 * ((t - 0.4) / 0.4) : 0.22;
                using var dot = new SolidBrush(Color.FromArgb((int) (o * 255), ink.Head));
                g.FillEllipse(dot, cx + (i - 1) * 12 * u - 3.6f * u, cy - 3.6f * u, 7.2f * u, 7.2f * u);
            }
        }
        else
        {
            var glyph = s.State switch { Status.NoServer or Status.Removed => "plug", Status.IdentityUnreadable => "x", _ => "clock" };
            Glyphs.Draw(g, glyph, new RectangleF(box.X + 39 * u, box.Y + 39 * u, 42 * u, 42 * u), ink.Tone(Tone(s.State)), 1.7f);
        }
    }

    private void PaintMain(Graphics g)
    {
        // The computer, and the window's own buttons.
        var tile = R(330, 18, 42, 42);
        using (var p = Ink.Round(tile, 11 * k))
        using (var b = new SolidBrush(ink.Tile))
        using (var line = new Pen(ink.Line, k))
        {
            g.FillPath(b, p);
            g.DrawPath(line, p);
        }
        Glyphs.Draw(g, "monitor", R(341, 29, 20, 20), ink.Soft, 2f);
        Say(g, Environment.MachineName, fName, ink.Head, R(386, 19, 380, 24), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        Say(g, $"{Machine.Os} · {Me}", fMeta, ink.Muted, R(386, 44, 380, 18), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        foreach (var (x, id, glyph) in new[] { (W - 82f, "min", "minus"), (W - 46f, "close", "close") })
        {
            var r = R(x, 14, 34, 30);
            if (hover == id)
            {
                using var p = Ink.Round(r, 6 * k);
                using var b = new SolidBrush(id == "close" ? Color.FromArgb(0xC4, 0x2B, 0x1C) : ink.Hover);
                g.FillPath(b, p);
            }
            Glyphs.Draw(g, glyph, new RectangleF(r.X + 10 * k, r.Y + 8 * k, 14 * k, 14 * k), hover == "close" && id == "close" ? Color.White : ink.Soft, 1.4f);
            Hit(r, id, id == "close" ? Hide : () => WindowState = FormWindowState.Minimized);
        }

        // Details first, then Activity with how many things it lists; a straight line in the accent under the open one.
        var rows = Rows();
        var x0 = 330f;
        foreach (var (id, label, glyph, count) in new[] { ("details", "Details", "info", ""), ("activity", "Activity", "history", rows.Count.ToString(CultureInfo.InvariantCulture)) })
        {
            var on = page == id;
            var colour = on || hover == "tab-" + id ? ink.Head : ink.Muted;
            var w = 24 + TextWidth(g, label, fTab) + (count.Length > 0 ? 8 + TextWidth(g, count, fRow) : 0);
            Glyphs.Draw(g, glyph, R(x0, 84, 16, 16), colour, 2f);
            Say(g, label, fTab, colour, R(x0 + 24, 82, 200, 20), TextFormatFlags.SingleLine);
            if (count.Length > 0)
            {
                Say(g, count, fRow, ink.Muted, R(x0 + w - TextWidth(g, count, fRow), 84, 40, 18), TextFormatFlags.SingleLine);
            }
            if (on)
            {
                using var bar = new SolidBrush(ink.AccentSoft);
                g.FillRectangle(bar, R(x0 - 1, 110, w + 2, 2));
            }
            var tabId = id;
            Hit(R(x0 - 6, 74, w + 12, 38), "tab-" + id, () => { page = tabId; Invalidate(); });
            x0 += w + 26;
        }
        using (var line = new Pen(ink.Line, Math.Max(1, k)))
        {
            g.DrawLine(line, Aside * k, 112 * k, W * k, 112 * k);
        }
        if (page == "details")
        {
            PaintDetails(g);
        }
        else
        {
            PaintActivity(g, rows);
        }
    }

    private static string Me => string.IsNullOrEmpty(Environment.UserDomainName) ? Environment.UserName : $"{Environment.UserDomainName}\\{Environment.UserName}";

    // --- Details ---------------------------------------------------------------------------------------------------------

    /// <summary>This computer, as Details lists it and Copy for IT copies it.</summary>
    public List<(string Label, string Value, string? Note)> Facts()
    {
        var (maker, model, serial) = (s.Maker, s.Model, s.Serial);
        if (model is null && read is { } r)
        {
            (maker, model, serial) = r;
        }
        var made = model is null ? "Not read yet" : maker is { Length: > 0 } m && !model.StartsWith(m, StringComparison.OrdinalIgnoreCase) && !m.StartsWith("System manufacturer", StringComparison.OrdinalIgnoreCase) ? $"{Tidy(m)} {model}" : model;
        var checkedIn = s.State == Status.Waiting ? "Waiting for approval" : s.CheckedIn is { } t
            ? (t.ToLocalTime().Date == DateTime.Today ? $"Today, {t.ToLocalTime():h:mm tt}" : t.ToLocalTime().ToString("ddd, MMM d, h:mm tt", CultureInfo.CurrentCulture)) : "Never";
        var current = s.State == Status.Connected && s.ServerVersion == Agent.Version ? "Up to date" : null;
        return new List<(string, string, string?)>
        {
            ("Computer name", Environment.MachineName, null), ("Signed-in user", Me, null), ("IP address", Ip(), null), ("Make and model", made, null),
            ("Serial number", serial is { Length: > 0 } ? serial : "Not read yet", null), ("Windows", Machine.Os, null), ("Client version", Agent.Version, current),
            ("Last check-in", checkedIn, null),
        };
    }

    // "Dell Inc." reads as Dell.
    private static string Tidy(string maker) => maker.Replace(" Inc.", "").Replace(", Inc.", "").Replace(" Corporation", "").Trim();

    private string Ip()
    {
        if (ip is not null)
        {
            return ip;
        }
        try
        {
            ip = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                .Select(n => n.GetIPProperties())
                .OrderByDescending(p => p.GatewayAddresses.Count > 0)
                .SelectMany(p => p.UnicastAddresses)
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "None";
        }
        catch (NetworkInformationException)
        {
            ip = "Unknown";
        }
        return ip;
    }

    // Make, model and serial number from Windows, when the inventory hasn't been sent yet.
    private void Hardware()
    {
        if (s.Model is not null || read is not null)
        {
            return;
        }
        Task.Run(() =>
        {
            string? maker = null, model = null, serial = null;
            try
            {
                using (var cs = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                {
                    foreach (var o in cs.Get())
                    {
                        (maker, model) = (o["Manufacturer"]?.ToString()?.Trim(), o["Model"]?.ToString()?.Trim());
                    }
                }
                using (var bios = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS"))
                {
                    foreach (var o in bios.Get())
                    {
                        serial = o["SerialNumber"]?.ToString()?.Trim();
                    }
                }
            }
            catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException)
            {
                // Said as Not read yet.
            }
            read = (maker, model, serial);
            try
            {
                BeginInvoke((Action) Invalidate);
            }
            catch (InvalidOperationException)
            {
                // Closed.
            }
        });
    }

    private void PaintDetails(Graphics g)
    {
        var help = s.Help is { On: true } && s.State is not (Status.NoServer or Status.Removed);
        var text = s.Help?.Text is { Length: > 0 } t ? t : "Ask your IT team, or send them a request from here.";
        var canAsk = s.State == Status.Connected;
        var textW = 520f - 36 - (canAsk ? 150 + 16 : 0);
        var textH = Measure(g, text, fRow, textW, wrap: true);
        var boxH = help ? Math.Max(84, 16 + 20 + 4 + textH + 16) : 0;
        var facts = Facts();
        var total = boxH + (help ? 24 : 0) + 30 + 8 + facts.Count * 31;
        var y = 134 + Math.Max(0, (600 - 134 - 24 - total) / 2);
        if (help)
        {
            var box = R(330, y, 520, boxH);
            using (var p = Ink.Round(box, 10 * k))
            using (var b = new SolidBrush(ink.Tile))
            using (var line = new Pen(ink.Line, k))
            {
                g.FillPath(b, p);
                g.DrawPath(line, p);
            }
            var top = y + (boxH - (20 + 4 + textH)) / 2;
            Say(g, "Need help?", fH3, ink.Head, R(348, top, textW, 20), TextFormatFlags.SingleLine);
            Say(g, text, fRow, ink.Soft, R(348, top + 24, textW, textH + 2), TextFormatFlags.WordBreak);
            if (canAsk)
            {
                Button(g, R(850 - 18 - 150, y + (boxH - 42) / 2, 150, 42), "Ask IT for help", "ask", Ask, primary: true, glyph: "message");
            }
            y += boxH + 24;
        }
        Say(g, "This computer", fH3, ink.Head, R(330, y + 5, 300, 20), TextFormatFlags.SingleLine);
        var copy = R(820, y, 30, 30);
        using (var p = Ink.Round(copy, 6 * k))
        using (var b = new SolidBrush(hover == "copy" ? ink.Hover : ink.Btn))
        using (var line = new Pen(ink.BtnLine, k))
        {
            g.FillPath(b, p);
            g.DrawPath(line, p);
        }
        Glyphs.Draw(g, "copy", R(827.5f, y + 7.5f, 15, 15), hover == "copy" ? ink.Head : ink.Soft, 2f);
        Hit(copy, "copy", CopyForIt, "Copy for IT");
        y += 38;
        using var hair = new Pen(ink.Line, Math.Max(1, k));
        for (var i = 0; i < facts.Count; i++)
        {
            var (label, value, note) = facts[i];
            Say(g, label, fRow, ink.Muted, R(330, y, 180, 31), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            Say(g, value, fRow, ink.Text, R(510, y, 340, 31), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (note is not null)
            {
                Say(g, note, fSmall, ink.Ok, R(510 + TextWidth(g, value, fRow) + 10, y, 120, 31), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            if (i < facts.Count - 1)
            {
                g.DrawLine(hair, 330 * k, (y + 31) * k, 850 * k, (y + 31) * k);
            }
            y += 31;
        }
    }

    // --- Activity --------------------------------------------------------------------------------------------------------

    // What it did, newest first: the last check-in, then what the service wrote down.
    private List<Act> Rows()
    {
        var list = new List<Act>();
        if (s.State == Status.Connected && s.CheckedIn is { } at)
        {
            list.Add(new Act { Icon = "check", Tone = "ok", Title = "Checked in", Detail = $"It checks in every {(Status.Every(s.State).TotalMinutes is 1 ? "minute" : $"{Status.Every(s.State).TotalMinutes:0} minutes")}.", At = at });
        }
        list.AddRange(s.Acts ?? new List<Act>());
        return list;
    }

    private void PaintActivity(Graphics g, List<Act> rows)
    {
        // At the top: the last day, a bar for each half hour.
        var y = 134f;
        var (said, bad) = s.State switch
        {
            Status.Waiting => ("None until it's approved", false),
            Status.NoServer => ("None, not connected", false),
            Status.Unreachable => (s.LastOk is { } l ? $"None since {l.ToLocalTime():h:mm tt}" : "None lately", true),
            _ => (s.FailedDay == 0 ? $"All {s.OkDay:N0} on time" : $"{s.OkDay:N0} of {s.OkDay + s.FailedDay:N0} on time", false),
        };
        Say(g, "Check-ins in the last 24 hours", fH3, ink.Head, R(330, y, 300, 20), TextFormatFlags.SingleLine);
        Say(g, said, fMeta, bad ? ink.Bad : ink.Muted, R(600, y + 1, 250, 20), TextFormatFlags.Right | TextFormatFlags.SingleLine);
        y += 32;
        var day = s.Day is { Count: 48 } d ? d : Enumerable.Repeat(-1, 48).ToList();
        var bw = (520f - 47 * 3) / 48;
        for (var i = 0; i < 48; i++)
        {
            var colour = day[i] switch { 2 => Color.FromArgb(184, ink.Ok), 1 => ink.Warn, 0 => Color.FromArgb(204, ink.Bad), _ => ink.None };
            using var p = Ink.Round(R(330 + i * (bw + 3), y, bw, 30), 2 * k);
            using var b = new SolidBrush(colour);
            g.FillPath(b, p);
        }
        y += 37;
        // Under it: the day's hours, where they fall, and Now.
        var start = DateTime.Now.AddHours(-24);
        Say(g, start.AddMinutes(30 - start.Minute).ToString("h tt", CultureInfo.CurrentCulture), fAxis, ink.Muted, R(330, y, 60, 16), TextFormatFlags.SingleLine);
        Say(g, "Now", fAxis, ink.Muted, R(790, y, 60, 16), TextFormatFlags.Right | TextFormatFlags.SingleLine);
        for (var h = 0; h < 24; h += 6)
        {
            var mark = DateTime.Today.AddHours(h);
            if (mark < start)
            {
                mark = mark.AddDays(1);
            }
            var at = (float) ((mark - start).TotalHours / 24);
            if (at is > 0.14f and < 0.86f) // never on top of the first hour or Now
            {
                Say(g, mark.ToString("h tt", CultureInfo.CurrentCulture), fAxis, ink.Muted, R(330 + at * 520 - 30, y, 60, 16), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            }
        }
        y += 40;

        // Then what it did, filling in under the bars as things happen.
        Say(g, $"What {Lab} did here", fH3, ink.Head, R(330, y, 400, 20), TextFormatFlags.SingleLine);
        y += 34;
        if (rows.Count == 0)
        {
            Say(g, "Nothing yet. What the client does here shows up as it happens.", fRow, ink.Muted, R(330, y, 520, 20), TextFormatFlags.SingleLine);
            return;
        }
        using var hair = new Pen(ink.Line, Math.Max(1, k));
        for (var i = 0; i < rows.Count && y + 44 <= 600 - 18; i++)
        {
            var a = rows[i];
            var dot = R(330, y, 30, 30);
            if (i < rows.Count - 1 && y + 52 + 44 <= 600 - 18)
            {
                g.DrawLine(hair, 345 * k, (y + 36) * k, 345 * k, (y + 48) * k);
            }
            using (var b = new SolidBrush(ink.Tile))
            using (var line = new Pen(ink.Line, k))
            {
                g.FillEllipse(b, dot);
                g.DrawEllipse(line, dot);
            }
            Glyphs.Draw(g, a.Icon, R(338, y + 8, 14, 14), ink.Tone(a.Tone), 2f);
            Say(g, a.Title, fH3, ink.Head, R(374, y + 1, 380, 19), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Say(g, a.Detail, fMeta, ink.Muted, R(374, y + 21, 400, 18), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            var when = a.At.ToLocalTime();
            Say(g, when.Date == DateTime.Today ? when.ToString("h:mm tt", CultureInfo.CurrentCulture) : when.ToString("MMM d", CultureInfo.CurrentCulture), fSmall, ink.Muted, R(760, y + 3, 90, 16), TextFormatFlags.Right | TextFormatFlags.SingleLine);
            y += 52;
        }
    }

    // --- Mouse and keys --------------------------------------------------------------------------------------------------

    private (RectangleF R, string Id, Action? Click, string? Tip)? HitAt(Point p) =>
        hits.LastOrDefault(h => h.R.Contains(p)) is var h && h.Id is not null ? h : null;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = HitAt(e.Location);
        Cursor = h is not null ? Cursors.Hand : Cursors.Default;
        if (h?.Id != hover)
        {
            hover = h?.Id;
            Invalidate();
        }
        if (h?.Tip is { } said && tipFor != h.Value.Id)
        {
            tipFor = h.Value.Id;
            tip.Show(said, this, e.X + (int) (12 * k), e.Y + (int) (18 * k), 1600);
        }
        else if (h?.Tip is null && tipFor is not null)
        {
            tipFor = null;
            tip.Hide(this);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = null;
        tipFor = null;
        tip.Hide(this);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        if (HitAt(e.Location) is { } h)
        {
            press = h.Id;
            Invalidate();
            return;
        }
        // Dragged by anywhere that isn't a button: the window has no title bar of its own.
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr) 2, IntPtr.Zero);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var h = HitAt(e.Location);
        var was = press;
        press = null;
        Invalidate();
        if (h is not null && h.Value.Id == was)
        {
            h.Value.Click?.Invoke();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keys)
    {
        if (keys == Keys.Escape)
        {
            Hide();
            return true;
        }
        if (keys is (Keys.Control | Keys.Tab) or (Keys.Control | Keys.Shift | Keys.Tab))
        {
            page = page == "details" ? "activity" : "details";
            Invalidate();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keys);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= Themed;
            tick.Dispose();
            tip.Dispose();
        }
        base.Dispose(disposing);
    }

    // --- What the buttons do ---------------------------------------------------------------------------------------------

    // Check in now (or Try again now): the service starts its round at once; said when the check-in comes.
    private void CheckIn()
    {
        if (asking)
        {
            return;
        }
        asking = true;
        Invalidate();
        var before = s.CheckedIn;
        var lab = Lab;
        Task.Run(() =>
        {
            var answer = Pipe.Ask(new PipeAsk { Do = "checkin" }, TimeSpan.FromSeconds(5));
            Status? after = null;
            if (answer.Ok)
            {
                for (var i = 0; i < 40; i++)
                {
                    System.Threading.Thread.Sleep(500);
                    after = Status.Read();
                    if (after is not null && (after.CheckedIn != before || (after.State == Status.Unreachable && after.Next is { } n && n > DateTime.UtcNow.AddSeconds(30))))
                    {
                        break;
                    }
                }
            }
            try
            {
                BeginInvoke((Action) (() =>
                {
                    asking = false;
                    if (after is not null)
                    {
                        Draw(after);
                    }
                    if (!answer.Ok)
                    {
                        toast(lab, answer.Error ?? "Could not check in.");
                    }
                    else if (after?.State == Status.Connected && after.CheckedIn != before)
                    {
                        tickAt = DateTime.UtcNow;
                        toast(lab, $"Checked in at {after.CheckedIn!.Value.ToLocalTime():h:mm tt}.");
                    }
                    else if (after?.State == Status.Unreachable)
                    {
                        toast(lab, "Still cannot reach the server. It tries again every minute.");
                    }
                    Invalidate();
                }));
            }
            catch (InvalidOperationException)
            {
                // Closed.
            }
        });
    }

    // Another server: the address isn't filled in (people need to know it, and it isn't shown here).
    private void Connect() => new ConnectWindow(new JoinLink { Server = "" }).Show();

    // Uninstalling goes through Windows Installer, which refuses unless the server has allowed it (Removal).
    private void Uninstall()
    {
        if (!Removal.Allowed)
        {
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
        if (MessageBox.Show(this, "Uninstall the Rabbid Lab client from this computer? It leaves Rabbid Lab and stops checking in.", Lab, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        if (!Removal.Uninstall(quiet: false))
        {
            MessageBox.Show(this, "Could not start the uninstall. Use Settings > Apps > Installed apps instead.", Lab, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopyForIt()
    {
        var text = string.Join(Environment.NewLine, Facts().Select(f => $"{f.Label}: {f.Value}{(f.Note is null ? "" : $" ({f.Note.ToLowerInvariant()})")}"));
        try
        {
            Clipboard.SetText(text);
            toast("Copied", "Paste it into your email or ticket to IT.");
        }
        catch (ExternalException)
        {
            toast("Couldn't copy", "Another program is using the clipboard. Try again.");
        }
    }

    // Ask IT for help: one at a time (another click brings it forward). It opens at once with a place for each screen, and
    // takes their pictures itself (AskWindow.Take), so this window never freezes for them.
    private void Ask()
    {
        if (asked is { IsDisposed: false })
        {
            asked.Activate();
            return;
        }
        var mode = s.Help?.Pictures ?? "all";
        var shots = mode == "never" ? new List<Shot>() : Shot.Screens();
        asked = new AskWindow(ink, Lab, mode, shots, Facts().Select(f => (f.Label, f.Value)).ToList(), toast) { Owner = this };
        asked.FormClosed += (_, _) => asked = null;
        asked.Show(this);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr w, IntPtr l);
}
