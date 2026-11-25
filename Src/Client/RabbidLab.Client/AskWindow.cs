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

/// <summary>A picture of one screen for Ask IT for help: a JPEG to send, and a small copy to show.</summary>
internal sealed class Shot : IDisposable
{
    public string Name = "";
    public byte[] Jpeg = Array.Empty<byte>();
    public Bitmap Thumb = new(1, 1);

    /// <summary>Every screen, the main one first, each 2560 pixels wide at most.</summary>
    public static List<Shot> TakeAll()
    {
        var shots = new List<Shot>();
        var screens = Screen.AllScreens.OrderByDescending(s => s.Primary).ThenBy(s => s.Bounds.X).ToList();
        for (var i = 0; i < screens.Count; i++)
        {
            var b = screens[i].Bounds;
            try
            {
                using var full = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(full))
                {
                    g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                }
                var scale = Math.Min(1f, 2560f / b.Width);
                using var sized = scale < 1f ? new Bitmap(full, (int) (b.Width * scale), (int) (b.Height * scale)) : (Bitmap) full.Clone();
                using var jpeg = new MemoryStream();
                var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using (var quality = new EncoderParameters(1))
                {
                    quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 82L);
                    sized.Save(jpeg, codec, quality);
                }
                var thumb = new Bitmap(480, Math.Max(1, 480 * b.Height / b.Width));
                using (var g = Graphics.FromImage(thumb))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(sized, 0, 0, thumb.Width, thumb.Height);
                }
                shots.Add(new Shot { Name = $"Screen {i + 1}", Jpeg = jpeg.ToArray(), Thumb = thumb });
            }
            catch (Exception e) when (e is Win32Exception or ExternalException or ArgumentException or OutOfMemoryException)
            {
                // A screen that can't be read (a locked or secure desktop) is left out.
            }
        }
        return shots;
    }

    public void Dispose() => Thumb.Dispose();
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
    private readonly TextBox detail = new() { BorderStyle = BorderStyle.None, Multiline = true, MaxLength = 2000, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
    private readonly List<(RectangleF R, string Id, Action Click)> hits = new();
    private float k = 1f;
    private string? hover, problem;
    private bool sending, tick;
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
        Scale(DeviceDpi);
    }

    private RectangleF R(float x, float y, float w, float h) => new(x * k, y * k, w * k, h * k);

    private void Scale(int dpi)
    {
        k = dpi <= 0 ? 1f : dpi / 96f;
        Font F(string face, float px) => new(face, px * k, FontStyle.Regular, GraphicsUnit.Pixel);
        (fHead, fH2, fText, fBtn, fSmall) = (F("Segoe UI", 12.5f), F("Segoe UI Semibold", 21), F("Segoe UI", 13), F("Segoe UI Semibold", 13), F("Segoe UI", 12));
        title.Font = fText;
        detail.Font = fText;
        Layout();
    }

    // The fields where the drawing leaves room for them; the window as tall as what's in it.
    private new void Layout()
    {
        title.Bounds = Rectangle.Round(R(40, 157, W - 80, 20));
        detail.Bounds = Rectangle.Round(R(40, 240, W - 80, 96));
        var y = 360f;
        if (mode != "never")
        {
            y += 24 + (shots.Count == 0 ? 20 : (float) Math.Ceiling(shots.Count / 2.0) * (ThumbH + 30)) + (mode == "ask" ? 30 : 0);
        }
        bottom = y + 8;
        ClientSize = new Size((int) (W * k), (int) ((bottom + 38 + 26) * k));
    }

    private const float ThumbW = (W - 56 - 14) / 2;
    private static float ThumbH => ThumbW * 9 / 16;

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Scale(e.DeviceDpiNew);
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
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        title.Focus();
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
            var kept = mode == "ask" && !tick ? 0 : shots.Count;
            Say(g, shots.Count == 0 ? "Couldn't take any" : kept == 0 ? "Not added" : $"{kept} added", fSmall, ink.Muted, R(W - 228, y + 1, 200, 18), TextFormatFlags.Right);
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
            for (var i = 0; i < shots.Count; i++)
            {
                var x = 28 + i % 2 * (ThumbW + 14);
                var top = y + i / 2 * (ThumbH + 30);
                var r = R(x, top, ThumbW, ThumbH);
                using (var p = Ink.Round(r, 6 * k))
                {
                    var state = g.Save();
                    g.SetClip(p);
                    g.DrawImage(shots[i].Thumb, r);
                    if (mode == "ask" && !tick)
                    {
                        using var dim = new SolidBrush(Color.FromArgb(150, ink.Bg));
                        g.FillRectangle(dim, r);
                    }
                    g.Restore(state);
                    using var line = new Pen(ink.BtnLine, k);
                    g.DrawPath(line, p);
                }
                Say(g, shots[i].Name, fSmall, ink.Muted, R(x, top + ThumbH + 6, 200, 18));
                if (mode == "all")
                {
                    var xr = R(x + ThumbW - 30, top + 6, 24, 24);
                    using (var b = new SolidBrush(hover == "x" + i ? Color.FromArgb(0xC4, 0x2B, 0x1C) : Color.FromArgb(158, 0, 0, 0)))
                    {
                        g.FillEllipse(b, xr);
                    }
                    Glyphs.Draw(g, "close", new RectangleF(xr.X + 6 * k, xr.Y + 6 * k, 12 * k, 12 * k), Color.White, 2f);
                    var gone = shots[i];
                    hits.Add((xr, "x" + i, () => { shots.Remove(gone); gone.Dispose(); Layout(); Invalidate(); }));
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
        Pill(g, send, sending ? "Sending…" : "Send", "send", true);
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

    private void Send()
    {
        if (sending)
        {
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
            help.Pictures.AddRange(shots.Select(s => new HelpPictureData { Name = s.Name, Data = Convert.ToBase64String(s.Jpeg) }));
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
