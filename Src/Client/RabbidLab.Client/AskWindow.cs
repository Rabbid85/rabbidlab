using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RabbidLab.Client;

/// <summary>
/// A place for one picture in Ask IT for help: a screen's, named by where it is, or one chosen from this computer in its
/// place. Waiting while it's taken (no picture yet); Removed when it's left out, which leaves a + there.
/// </summary>
internal sealed class Shot : IDisposable
{
    public string Name = "";        // as shown and sent: the screen's name, or the chosen file's
    public string Place = "";       // the screen's name, which stays when a file takes its place
    public Screen? Screen;
    public byte[] Jpeg = Array.Empty<byte>();
    public Bitmap? Thumb;
    public bool Removed;
    public DateTime Came;           // when its picture came, to fade it in

    public bool Waiting => Thumb is null && !Removed;

    /// <summary>
    /// A place for each screen, left to right, by where it is ("Just say monitor 1, etc, monitor 2... or simply just
    /// something else?"): Your screen; the left and right screen; left, middle and right; more than three are numbered.
    /// Six at most, as many as a request takes.
    /// </summary>
    public static List<Shot> Screens()
    {
        var screens = Screen.AllScreens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).Take(6).ToList();
        string Name(int i) => screens.Count switch
        {
            1 => "Your screen",
            2 => i == 0 ? "Left screen" : "Right screen",
            3 => new[] { "Left screen", "Middle screen", "Right screen" }[i],
            _ => $"Screen {i + 1}",
        };
        return screens.Select((s, i) => new Shot { Name = Name(i), Place = Name(i), Screen = s }).ToList();
    }

    /// <summary>"the left screen", "your screen", "Screen 4": the place, in a sentence.</summary>
    public string Called => Place.StartsWith("Screen ", StringComparison.Ordinal) ? Place : $"the {Place.ToLowerInvariant()}";

    /// <summary>A screen's picture, taken on a thread of its own; null when it couldn't be read (a locked or secure desktop).</summary>
    public static (byte[] Jpeg, Bitmap Thumb)? Take(Screen screen)
    {
        var b = screen.Bounds;
        try
        {
            using var full = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(full))
            {
                g.CopyFromScreen(b.Location, Point.Empty, b.Size);
            }
            return Picture(full);
        }
        catch (Exception e) when (e is Win32Exception or ExternalException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>A picture from this computer; null when it isn't one Windows can open.</summary>
    public static (byte[] Jpeg, Bitmap Thumb)? Open(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var image = Image.FromStream(stream);
            using var copy = new Bitmap(image);
            return Picture(copy);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or ExternalException)
        {
            return null;
        }
    }

    // As sent: a JPEG 2560 pixels wide at most; and a small copy to show.
    private static (byte[] Jpeg, Bitmap Thumb) Picture(Bitmap full)
    {
        var scale = Math.Min(1f, 2560f / full.Width);
        using var sized = scale < 1f ? new Bitmap(full, (int) (full.Width * scale), (int) (full.Height * scale)) : (Bitmap) full.Clone();
        using var jpeg = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using (var quality = new EncoderParameters(1))
        {
            quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 82L);
            sized.Save(jpeg, codec, quality);
        }
        var thumb = new Bitmap(480, Math.Max(1, 480 * full.Height / full.Width));
        using (var g = Graphics.FromImage(thumb))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(sized, 0, 0, thumb.Width, thumb.Height);
        }
        return (jpeg.ToArray(), thumb);
    }

    public void Dispose() => Thumb?.Dispose();
}

/// <summary>
/// Ask IT for help, in the client's look: a title it can't be sent without, more detail if they want, and a picture of
/// every screen (taken before this window opened) with a × to leave any out, or a tick when IT asks for that. Sent through
/// the service, which sends it on with this computer's identity (Pipe).
/// </summary>
internal sealed class AskWindow : Form
{
    private const float W = 580;
    private readonly Ink ink;
    private readonly string lab;
    private readonly string mode;
    private readonly List<Shot> shots;
    private readonly List<(string Label, string Value)> facts;
    private readonly Action<string, string> toast;
    private readonly TextBox title = new() { BorderStyle = BorderStyle.None, MaxLength = 80 };
    private readonly TextBox detail = new() { BorderStyle = BorderStyle.None, Multiline = true, MaxLength = 2000, AcceptsReturn = true };
    private readonly List<(RectangleF R, string Id, Action Click)> hits = new();
    private readonly System.Windows.Forms.Timer moving = new() { Interval = 50 }; // the bar while a picture is taken, and its fade in
    private RectangleF pictures; // where the pictures are drawn, to draw again while they move
    private float k = 1f;
    private string? hover, problem;
    private bool sending, sendWhenIn, tick;
    private int taking; // picture runs going on, with these windows kept out of them
    private Font? fHead, fH2, fText, fBtn, fSmall;
    private float bottom;

    public AskWindow(Ink ink, string lab, string mode, List<Shot> shots, List<(string, string)> facts, Action<string, string> toast)
    {
        (this.ink, this.lab, this.mode, this.shots, this.facts, this.toast) = (ink, lab, mode, shots, facts, toast);
        tick = mode == "all";
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Text = "Ask IT for help";
        BackColor = ink.Bg;
        KeyPreview = true;
        foreach (var box in new[] { title, detail })
        {
            box.BackColor = ink.Tile;
            box.ForeColor = ink.Text;
            box.Enter += (_, _) => Invalidate();
            box.Leave += (_, _) => Invalidate();
            Controls.Add(box);
        }
        title.TextChanged += (_, _) => { if (problem is not null) { problem = null; Invalidate(); } };
        // More detail's scroll bar only once there's more than fits, and dark in the dark look.
        detail.TextChanged += (_, _) => Overflow();
        detail.HandleCreated += (_, _) => SetWindowTheme(detail.Handle, ink.Light ? "Explorer" : "DarkMode_Explorer", null);
        ActiveControl = title;
        moving.Tick += (_, _) =>
        {
            var fading = shots.Any(s => s.Thumb is not null && DateTime.UtcNow - s.Came < Fade);
            if (!shots.Any(s => s.Waiting) && !fading)
            {
                moving.Stop();
            }
            Invalidate(Rectangle.Round(pictures));
        };
        Scale(DeviceDpi);
    }

    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(350);

    // The pictures are taken once this window is up ("Should open with 2 skeleton pictures, and they load when they can."),
    // each place saying so until its picture comes in. This window and the client's are kept out of them (Windows 10 2004 and
    // later); before that they're hidden for the moment the pictures take, as they were.
    private void Take(List<Shot> which)
    {
        if (which.Count == 0)
        {
            return;
        }
        moving.Start();
        var windows = new[] { Handle, Owner?.Handle ?? IntPtr.Zero }.Where(h => h != IntPtr.Zero).ToList();
        if (!windows.All(h => SetWindowDisplayAffinity(h, 0x11))) // WDA_EXCLUDEFROMCAPTURE
        {
            windows.ForEach(h => SetWindowDisplayAffinity(h, 0));
            var hidden = new Form?[] { this, Owner };
            Array.ForEach(hidden, f => { if (f is not null) { f.Opacity = 0; f.Update(); } });
            System.Threading.Thread.Sleep(120);
            foreach (var s in which)
            {
                Came(s, s.Screen is { } screen ? Shot.Take(screen) : null);
            }
            Array.ForEach(hidden, f => { if (f is not null) { f.Opacity = 1; } });
            return;
        }
        DwmFlush();
        taking++;
        Task.Run(() =>
        {
            foreach (var s in which)
            {
                var picture = s.Screen is { } screen ? Shot.Take(screen) : null;
                Later(() => Came(s, picture));
            }
            Later(() =>
            {
                if (--taking == 0)
                {
                    windows.ForEach(h => SetWindowDisplayAffinity(h, 0));
                }
            });
        });
    }

    // A picture came in (null: its screen couldn't be read, and its place is left with a +).
    private void Came(Shot s, (byte[] Jpeg, Bitmap Thumb)? picture)
    {
        if (picture is { } p)
        {
            s.Thumb?.Dispose();
            (s.Jpeg, s.Thumb, s.Came, s.Removed) = (p.Jpeg, p.Thumb, DateTime.UtcNow, false);
        }
        else
        {
            s.Removed = true;
        }
        moving.Start();
        Invalidate();
        if (sendWhenIn && !shots.Any(x => x.Waiting))
        {
            sendWhenIn = false;
            Send();
        }
    }

    private void Later(Action what)
    {
        try
        {
            BeginInvoke(what);
        }
        catch (InvalidOperationException)
        {
            // Closed.
        }
    }

    // The + where a picture was left out: that screen's picture again, or a picture from this computer in its place.
    private void Add(Shot s, RectangleF at)
    {
        var menu = new ContextMenuStrip { Renderer = new MenuLook(ink), ShowImageMargin = false };
        if (s.Screen is not null)
        {
            menu.Items.Add($"The picture of {s.Called} again", null, (_, _) =>
            {
                (s.Removed, s.Name) = (false, s.Place);
                Invalidate();
                Take(new List<Shot> { s });
            });
        }
        menu.Items.Add("A picture from this computer…", null, (_, _) => Choose(s));
        menu.Closed += (_, _) => BeginInvoke((Action) menu.Dispose);
        menu.Show(this, Point.Round(new PointF(at.X + at.Width / 2, at.Y + at.Height / 2)));
    }

    private void Choose(Shot s)
    {
        using var pick = new OpenFileDialog { Title = "Add a picture", Filter = "Pictures (*.png, *.jpg, *.bmp, *.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Multiselect = false };
        if (pick.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        var path = pick.FileName;
        (s.Removed, s.Name) = (false, Path.GetFileName(path));
        s.Thumb?.Dispose();
        s.Thumb = null;
        Invalidate();
        moving.Start();
        Task.Run(() =>
        {
            var picture = Shot.Open(path);
            Later(() =>
            {
                if (picture is null)
                {
                    (s.Removed, s.Name) = (true, s.Place);
                    problem = "That file isn't a picture Windows can open.";
                }
                Came(s, picture);
            });
        });
    }

    private RectangleF R(float x, float y, float w, float h) => new(x * k, y * k, w * k, h * k);

    // At the scale of the screen it's on, never bigger than that screen; where Windows says when it was dragged to another.
    private void Scale(int dpi, Point? at = null)
    {
        var area = Screen.FromRectangle(at is { } p ? new Rectangle(p, Size) : Bounds).WorkingArea;
        k = Math.Min(dpi <= 0 ? 1f : dpi / 96f, Math.Min(area.Width * 0.94f / W, area.Height * 0.94f / Tall()));
        var old = new[] { fHead, fH2, fText, fBtn, fSmall };
        Font F(string face, float px) => new(face, px * k, FontStyle.Regular, GraphicsUnit.Pixel);
        (fHead, fH2, fText, fBtn, fSmall) = (F("Segoe UI", 12.5f), F("Segoe UI Semibold", 21), F("Segoe UI", 13), F("Segoe UI Semibold", 13), F("Segoe UI", 12));
        title.Font = fText;
        detail.Font = fText;
        foreach (var f in old)
        {
            f?.Dispose();
        }
        Layout();
        if (at is { } corner)
        {
            Location = corner;
        }
        Invalidate();
    }

    // How tall the window is at 96 DPI: the fields, the pictures in rows of two, then Cancel and Send.
    private float Tall()
    {
        var y = 360f;
        if (mode != "never")
        {
            y += 24 + (shots.Count == 0 ? 20 : (float) Math.Ceiling(shots.Count / 2.0) * (ThumbH + 30)) + (mode == "ask" ? 30 : 0);
        }
        return y + 8 + 38 + 26;
    }

    // The fields where the drawing leaves room for them; the window as tall as what's in it.
    private new void Layout()
    {
        title.Bounds = Rectangle.Round(R(40, 157, W - 80, 20));
        detail.Bounds = Rectangle.Round(R(40, 240, W - 80, 96));
        bottom = Tall() - 38 - 26;
        ClientSize = new Size((int) (W * k), (int) (Tall() * k));
        Overflow();
    }

    private void Overflow()
    {
        if (!detail.IsHandleCreated)
        {
            return; // empty until it's on screen
        }
        var lines = detail.GetLineFromCharIndex(detail.TextLength) + 1;
        var want = lines * detail.Font.Height > detail.ClientSize.Height ? ScrollBars.Vertical : ScrollBars.None;
        if (detail.ScrollBars != want)
        {
            detail.ScrollBars = want;
        }
    }

    private const float ThumbW = (W - 56 - 14) / 2;
    private static float ThumbH => ThumbW * 9 / 16;

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
            var round = 2;
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        }
        catch (Exception x) when (x is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        SendMessage(title.Handle, 0x1501, (IntPtr) 1, "A few words, like: The printer won't print"); // the hint in the empty box
        // At the scale of the client window's screen, in the middle of it (a window that isn't a dialog isn't put there).
        if (Look.Dpi(Handle) is var dpi and > 0 && dpi != DeviceDpi)
        {
            Scale(dpi);
        }
        var over = Owner is { } o ? o.Bounds : Screen.FromHandle(Handle).WorkingArea;
        var area = Screen.FromRectangle(over).WorkingArea;
        Location = new Point(
            Math.Max(area.Left, Math.Min(over.X + (over.Width - Width) / 2, area.Right - Width)),
            Math.Max(area.Top, Math.Min(over.Y + (over.Height - Height) / 2, area.Bottom - Height)));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        title.Focus();
        if (mode != "never")
        {
            Take(shots.ToList());
        }
    }

    private void Say(Graphics g, string text, Font? f, Color c, RectangleF r, TextFormatFlags flags = TextFormatFlags.Left) =>
        TextRenderer.DrawText(g, text, f, Rectangle.Round(r), c, flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

    private void Frame(Graphics g, RectangleF r, bool focused, bool bad)
    {
        using var p = Ink.Round(r, 8 * k);
        using var b = new SolidBrush(ink.Tile);
        using var line = new Pen(bad ? ink.Bad : focused ? ink.Accent : ink.BtnLine, k);
        g.FillPath(b, p);
        g.DrawPath(line, p);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ink.Smooth(g);
        hits.Clear();
        g.Clear(ink.Bg);
        Glyphs.Mark(g, R(18, 15, 16, 16), ink.AccentSoft);
        Say(g, "Ask IT for help", fHead, ink.Soft, R(42, 14, 300, 18));
        var close = R(W - 46, 8, 34, 30);
        if (hover == "close")
        {
            using var p = Ink.Round(close, 6 * k);
            using var b = new SolidBrush(Color.FromArgb(0xC4, 0x2B, 0x1C));
            g.FillPath(b, p);
        }
        Glyphs.Draw(g, "close", new RectangleF(close.X + 10 * k, close.Y + 8 * k, 14 * k, 14 * k), hover == "close" ? Color.White : ink.Soft, 1.4f);
        hits.Add((close, "close", Close));

        Say(g, "Ask IT for help", fH2, ink.Head, R(28, 48, 400, 30));
        Say(g, "Your IT team gets this with this computer's details.", fText, ink.Muted, R(28, 88, W - 56, 20));
        Say(g, "Title", fText, ink.Soft, R(28, 122, 200, 20));
        Frame(g, R(28, 146, W - 56, 40), title.Focused, problem is not null);
        if (problem is not null)
        {
            Say(g, problem, fSmall, ink.Bad, R(28, 192, W - 56, 18));
        }
        Say(g, "More detail", fText, ink.Soft, R(28, 212, 100, 20));
        Say(g, "(if you want)", fSmall, ink.Muted, R(28 + TextRenderer.MeasureText(g, "More detail ", fText, Size.Empty, TextFormatFlags.NoPadding).Width / k, 213, 120, 20));
        Frame(g, R(28, 232, W - 56, 112), detail.Focused, false);

        var y = 360f;
        if (mode != "never")
        {
            Say(g, "Pictures of your screens", fText, ink.Soft, R(28, y, 300, 20));
            var kept = mode == "ask" && !tick ? 0 : shots.Count(s => s.Thumb is not null && !s.Removed);
            Say(g, shots.Count == 0 ? "Couldn't take any" : shots.Any(s => s.Waiting) ? "Taking them…" : kept == 0 ? "Not added" : $"{kept} added", fSmall, ink.Muted, R(W - 228, y + 1, 200, 18), TextFormatFlags.Right);
            y += 24;
            if (mode == "ask")
            {
                var box = R(28, y + 2, 16, 16);
                using (var p = Ink.Round(box, 4 * k))
                using (var b = new SolidBrush(tick ? ink.Accent : ink.Tile))
                using (var line = new Pen(tick ? ink.Accent : ink.BtnLine, k))
                {
                    g.FillPath(b, p);
                    g.DrawPath(line, p);
                }
                if (tick)
                {
                    Glyphs.Draw(g, "check", new RectangleF(box.X + 1 * k, box.Y + 1 * k, 14 * k, 14 * k), Color.White, 2.4f);
                }
                Say(g, "Add pictures of my screens", fText, ink.Soft, R(52, y, 300, 20));
                hits.Add((R(24, y - 2, 260, 24), "tick", () => { tick = !tick; Invalidate(); }));
                y += 30;
            }
            pictures = R(28, y, W - 56, (float) Math.Ceiling(shots.Count / 2.0) * (ThumbH + 30));
            for (var i = 0; i < shots.Count; i++)
            {
                var s = shots[i];
                var x = 28 + i % 2 * (ThumbW + 14);
                var top = y + i / 2 * (ThumbH + 30);
                var r = R(x, top, ThumbW, ThumbH);
                if (s.Removed)
                {
                    PaintAdd(g, s, r, "add" + i);
                    Say(g, "Add a picture", fSmall, ink.Muted, R(x, top + ThumbH + 6, 200, 18));
                    continue;
                }
                using (var p = Ink.Round(r, 6 * k))
                {
                    var state = g.Save();
                    g.SetClip(p);
                    if (s.Thumb is { } thumb)
                    {
                        Fading(g, thumb, r, s.Came);
                        if (mode == "ask" && !tick)
                        {
                            using var dim = new SolidBrush(Color.FromArgb(150, ink.Bg));
                            g.FillRectangle(dim, r);
                        }
                    }
                    else
                    {
                        PaintTaking(g, r);
                    }
                    g.Restore(state);
                    using var line = new Pen(ink.BtnLine, k);
                    g.DrawPath(line, p);
                }
                Say(g, s.Name, fSmall, ink.Muted, R(x, top + ThumbH + 6, ThumbW, 18), TextFormatFlags.EndEllipsis);
                if (mode == "all" && s.Thumb is not null)
                {
                    var xr = R(x + ThumbW - 30, top + 6, 24, 24);
                    using (var b = new SolidBrush(hover == "x" + i ? Color.FromArgb(0xC4, 0x2B, 0x1C) : Color.FromArgb(158, 0, 0, 0)))
                    {
                        g.FillEllipse(b, xr);
                    }
                    Glyphs.Draw(g, "close", new RectangleF(xr.X + 6 * k, xr.Y + 6 * k, 12 * k, 12 * k), Color.White, 2f);
                    hits.Add((xr, "x" + i, () => { s.Removed = true; (s.Name, s.Jpeg) = (s.Place, Array.Empty<byte>()); s.Thumb?.Dispose(); s.Thumb = null; Invalidate(); }));
                }
            }
            if (shots.Count == 0)
            {
                Say(g, "No pictures: the screens couldn't be read.", fSmall, ink.Muted, R(28, y, 400, 18));
            }
        }

        // Cancel and Send at the foot, at the right.
        var send = R(W - 28 - 84, bottom, 84, 38);
        var cancel = R(W - 28 - 84 - 8 - 90, bottom, 90, 38);
        Pill(g, cancel, "Cancel", "cancel", false);
        Pill(g, send, sending || sendWhenIn ? "Sending…" : "Send", "send", true);
        hits.Add((cancel, "cancel", Close));
        hits.Add((send, "send", Send));
        using var edge = new Pen(ink.Edge, Math.Max(1, k));
        g.DrawRectangle(edge, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    private void Pill(Graphics g, RectangleF r, string label, string id, bool primary)
    {
        using (var p = Ink.Round(r, 8 * k))
        {
            var back = primary ? (hover == id ? Ink.Mix(ink.Accent, Color.White, 0.14f) : ink.Accent) : hover == id ? ink.Hover : ink.Btn;
            using var b = new SolidBrush(back);
            g.FillPath(b, p);
            if (!primary)
            {
                using var line = new Pen(ink.BtnLine, k);
                g.DrawPath(line, p);
            }
        }
        Say(g, label, fBtn, primary ? Color.White : ink.Head, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    // While its picture is taken ("I want this one."): Taking the picture… in the middle, a thin bar in the accent running
    // along the foot.
    private void PaintTaking(Graphics g, RectangleF r)
    {
        using (var b = new SolidBrush(ink.Tile))
        {
            g.FillRectangle(b, r);
        }
        Say(g, "Taking the picture…", fSmall, ink.Muted, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        var track = new RectangleF(r.X, r.Bottom - 3 * k, r.Width, 3 * k);
        using (var b = new SolidBrush(Color.FromArgb(16, ink.Head)))
        {
            g.FillRectangle(b, track);
        }
        var at = DateTime.UtcNow.TimeOfDay.TotalMilliseconds % 1200 / 1200; // across in 1.2 s, again and again
        var w = r.Width * 0.35f;
        using (var b = new SolidBrush(ink.Accent))
        {
            g.FillRectangle(b, r.X - w + (float) at * (r.Width + w), track.Y, w, track.Height);
        }
    }

    // A picture coming in fades up over a third of a second.
    private static void Fading(Graphics g, Image thumb, RectangleF r, DateTime came)
    {
        var a = (float) Math.Min(1, (DateTime.UtcNow - came).TotalMilliseconds / Fade.TotalMilliseconds);
        if (a >= 1)
        {
            g.DrawImage(thumb, r);
            return;
        }
        using var look = new ImageAttributes();
        look.SetColorMatrix(new ColorMatrix { Matrix33 = a });
        g.DrawImage(thumb, Rectangle.Round(r), 0, 0, thumb.Width, thumb.Height, GraphicsUnit.Pixel, look);
    }

    // The + left where a picture was left out ("This is cool as fuck."): a dashed box, the accent when the mouse is on it.
    private void PaintAdd(Graphics g, Shot s, RectangleF r, string id)
    {
        var on = hover == id;
        using (var p = Ink.Round(r, 6 * k))
        {
            if (on)
            {
                using var b = new SolidBrush(Color.FromArgb(20, ink.Accent));
                g.FillPath(b, p);
            }
            using var line = new Pen(on ? ink.Accent : ink.BtnLine, k) { DashStyle = DashStyle.Dash };
            g.DrawPath(line, p);
        }
        var (cx, cy, half) = (r.X + r.Width / 2, r.Y + r.Height / 2, 11 * k);
        using (var plus = new Pen(on ? ink.Head : ink.Soft, 1.8f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(plus, cx - half, cy, cx + half, cy);
            g.DrawLine(plus, cx, cy - half, cx, cy + half);
        }
        hits.Add((r, id, () => Add(s, r)));
    }

    private void Send()
    {
        if (sending)
        {
            return;
        }
        // Pictures still being taken: it's sent as soon as they're in.
        if (shots.Any(s => s.Waiting) && (mode == "all" || (mode == "ask" && tick)) && title.Text.Trim().Length > 0)
        {
            sendWhenIn = true;
            Invalidate();
            return;
        }
        var t = title.Text.Trim();
        if (t.Length == 0)
        {
            problem = "Add a title so IT knows what it's about.";
            title.Focus();
            Invalidate();
            return;
        }
        sending = true;
        title.Enabled = detail.Enabled = false;
        Invalidate();
        var help = new HelpRequest
        {
            Title = t, Detail = detail.Text.Trim(),
            User = string.IsNullOrEmpty(Environment.UserDomainName) ? Environment.UserName : $"{Environment.UserDomainName}\\{Environment.UserName}",
        };
        if (mode == "all" || (mode == "ask" && tick))
        {
            help.Pictures.AddRange(shots.Where(s => !s.Removed && s.Thumb is not null).Select(s => new HelpPictureData { Name = s.Name, Data = Convert.ToBase64String(s.Jpeg) }));
        }
        help.Facts.AddRange(facts.Select(f => new HelpFactData { Label = f.Label, Value = f.Value }));
        Task.Run(() =>
        {
            var answer = Pipe.Ask(new PipeAsk { Do = "help", Help = help }, TimeSpan.FromSeconds(100));
            try
            {
                BeginInvoke((Action) (() =>
                {
                    sending = false;
                    if (answer.Ok)
                    {
                        toast("Sent to your IT team", "They'll get back to you.");
                        Close();
                        return;
                    }
                    title.Enabled = detail.Enabled = true;
                    problem = answer.Error ?? "It couldn't be sent. Try again.";
                    Invalidate();
                }));
            }
            catch (InvalidOperationException)
            {
                // Closed.
            }
        });
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = hits.LastOrDefault(x => x.R.Contains(e.Location));
        Cursor = h.Id is not null ? Cursors.Hand : Cursors.Default;
        if (h.Id != hover)
        {
            hover = h.Id;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var h = hits.LastOrDefault(x => x.R.Contains(e.Location));
        if (e.Button == MouseButtons.Left && h.Id is not null)
        {
            h.Click();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && !hits.Any(x => x.R.Contains(e.Location)))
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr) 2, IntPtr.Zero);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keys)
    {
        if (keys == Keys.Escape && !sending)
        {
            Close();
            return true;
        }
        if (keys == Keys.Enter && ActiveControl == title)
        {
            Send();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keys);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        moving.Dispose();
        // Closed while pictures were being taken: the client's window shows in captures again now, not when they're done.
        if (taking > 0 && Owner is { IsDisposed: false } owner)
        {
            SetWindowDisplayAffinity(owner.Handle, 0);
        }
        foreach (var s in shots)
        {
            s.Dispose();
        }
        base.OnFormClosed(e);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr w, IntPtr l);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr w, string l);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string app, string? idList);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}

/// <summary>The client's own notification at the foot of the screen by the tray, in its colours, not Windows' grey.</summary>
internal sealed class Toast : Form
{
    private static Toast? shown;
    private readonly Ink ink;
    private readonly string title, text;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 4200 };
    private readonly float k;

    private Toast(Ink ink, string title, string text)
    {
        (this.ink, this.title, this.text) = (ink, title, text);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = ink.Aside;
        k = DeviceDpi / 96f;
        ClientSize = new Size((int) (360 * k), (int) (76 * k));
        var area = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(area.Right - Width - (int) (12 * k), area.Bottom - Height - (int) (12 * k));
        timer.Tick += (_, _) => Fade();
        Click += (_, _) => Close();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x80 | 0x08000000; // a tool window that doesn't take the focus
            return p;
        }
    }

    public static void Show(Ink ink, string title, string text)
    {
        shown?.Close();
        shown = new Toast(ink, title, text);
        shown.Show();
        shown.timer.Start();
    }

    private async void Fade()
    {
        timer.Stop();
        for (var o = 1.0; o > 0; o -= 0.1)
        {
            Opacity = o;
            await Task.Delay(30);
        }
        Close();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var round = 2;
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        }
        catch (Exception x) when (x is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ink.Smooth(g);
        g.Clear(ink.Aside);
        Glyphs.Mark(g, new RectangleF(16 * k, 16 * k, 24 * k, 24 * k), ink.AccentSoft);
        using var head = new Font("Segoe UI Semibold", 13 * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var body = new Font("Segoe UI", 13 * k, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, title, head, Rectangle.Round(new RectangleF(52 * k, 14 * k, 296 * k, 20 * k)), ink.Head, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, text, body, Rectangle.Round(new RectangleF(52 * k, 35 * k, 296 * k, 36 * k)), ink.Soft, TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using var edge = new Pen(ink.Edge, Math.Max(1, k));
        g.DrawRectangle(edge, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Dispose();
        if (shown == this)
        {
            shown = null;
        }
        base.OnFormClosed(e);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

/// <summary>The tray icon's menu in the client's colours.</summary>
internal sealed class MenuLook : ToolStripProfessionalRenderer
{
    private readonly Ink ink;

    public MenuLook(Ink ink) : base(new Colours(ink)) => this.ink = ink;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? ink.Head : ink.Muted;
        base.OnRenderItemText(e);
    }

    private sealed class Colours : ProfessionalColorTable
    {
        private readonly Ink ink;

        public Colours(Ink ink) => this.ink = ink;

        public override Color ToolStripDropDownBackground => ink.Aside;
        public override Color ImageMarginGradientBegin => ink.Aside;
        public override Color ImageMarginGradientMiddle => ink.Aside;
        public override Color ImageMarginGradientEnd => ink.Aside;
        public override Color MenuBorder => ink.Edge;
        public override Color MenuItemBorder => ink.Hover;
        public override Color MenuItemSelected => ink.Hover;
        public override Color SeparatorDark => ink.Line;
        public override Color SeparatorLight => ink.Line;
    }
}

/// <summary>
/// The tray icon: the lab's mark with a dot for the state at its foot. A new one is made when either changes; the one it
/// replaces is let go once the tray and the window use the new one (Release).
/// </summary>
internal static class TrayMark
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    private static IntPtr current, stale;

    public static Icon Make(Ink ink, string tone)
    {
        var size = Math.Max(16, SystemInformation.SmallIconSize.Width) * 2;
        using var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        {
            Ink.Smooth(g);
            g.Clear(Color.Transparent);
            Glyphs.Mark(g, new RectangleF(0, 0, size, size), ink.AccentSoft);
            var d = size * 0.42f;
            var r = new RectangleF(size - d, size - d, d, d);
            using var ring = new SolidBrush(Color.FromArgb(0x1C, 0x1E, 0x26));
            g.FillEllipse(ring, r);
            using var dot = new SolidBrush(ink.Tone(tone));
            g.FillEllipse(dot, RectangleF.Inflate(r, -size * 0.06f, -size * 0.06f));
        }
        var handle = b.GetHicon();
        if (stale != IntPtr.Zero)
        {
            DestroyIcon(stale);
        }
        (stale, current) = (current, handle);
        return Icon.FromHandle(handle);
    }

    /// <summary>The icon before the last one, once nothing shows it any more.</summary>
    public static void Release()
    {
        if (stale != IntPtr.Zero)
        {
            DestroyIcon(stale);
            stale = IntPtr.Zero;
        }
    }
}
