using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using Microsoft.Win32;

namespace RabbidLab.Client;

/// <summary>
/// The look of the client's windows ("Ink"): near black and neutral greys with hairlines, or white with the same greys when
/// Windows' apps are light; colour for the state, and the lab's own accent for the main button.
/// </summary>
internal sealed class Ink
{
    public bool Light;
    public Color Bg, Aside, Line, Text, Head, Soft, Muted, Btn, BtnLine, Hover, Tile, None, Edge, Ring, Ok, Warn, Bad, Off, Accent, AccentSoft;

    private static Color C(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

    public static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb(255, (int) (a.R + (b.R - a.R) * t), (int) (a.G + (b.G - a.G) * t), (int) (a.B + (b.B - a.B) * t));

    /// <summary>The colours for now: light or dark as Windows' apps are, with the lab's accent (Rabbid Lab's purple when none).</summary>
    public static Ink Now(string? accent)
    {
        var light = WindowsLight();
        var a = Parse(accent) ?? C(0x5900FF);
        return light
            ? new Ink
            {
                Light = true, Bg = C(0xFFFFFF), Aside = C(0xFAFAFA), Line = C(0xE7E7EA), Text = C(0x18181B), Head = C(0x09090B), Soft = C(0x52525B), Muted = C(0x71717A),
                Btn = C(0xFFFFFF), BtnLine = C(0xE4E4E7), Hover = C(0xF4F4F5), Tile = C(0xFAFAFA), None = C(0xECECEF), Edge = C(0xE1E1E5), Ring = C(0xE7E7EA),
                Ok = C(0x16935A), Warn = C(0xB7791F), Bad = C(0xD23A50), Off = C(0x6B7085), Accent = a, AccentSoft = a,
            }
            : new Ink
            {
                Bg = C(0x0A0A0C), Aside = C(0x0F0F12), Line = C(0x1D1D21), Text = C(0xECECEF), Head = C(0xFAFAFA), Soft = C(0xA1A1AA), Muted = C(0x71717A),
                Btn = C(0x161619), BtnLine = C(0x27272A), Hover = C(0x1E1E22), Tile = C(0x121215), None = C(0x1C1C20), Edge = C(0x26262B), Ring = C(0x222226),
                Ok = C(0x5FD4A0), Warn = C(0xF2BE6B), Bad = C(0xFF6B7D), Off = C(0x8A8FA8), Accent = a, AccentSoft = Mix(a, Color.White, 0.35f),
            };
    }

    public Color Tone(string? tone) => tone switch { "ok" => Ok, "warn" => Warn, "bad" => Bad, _ => Off };

    public static Color? Parse(string? hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#')
        {
            return null;
        }
        try
        {
            return C(Convert.ToInt32(hex.Substring(1), 16));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Whether Windows' apps are in light mode (Settings, Personalization, Colors).</summary>
    public static bool WindowsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int on && on == 1;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            p.AddRectangle(r);
            return p;
        }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }
}

/// <summary>
/// The little drawings the windows use, as the console's icons are (strokes on a 24-unit square), and the lab's mark: the
/// console's own R on the dark tile with the dot in the accent, or the lab's own icon.
/// </summary>
internal static class Glyphs
{
    public static void Draw(Graphics g, string name, RectangleF box, Color colour, float width = 2f)
    {
        var k = box.Width / 24f;
        using var pen = new Pen(colour, width * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(colour);
        PointF P(float x, float y) => new(box.X + x * k, box.Y + y * k);
        void Lines(params float[] xy)
        {
            var pts = new PointF[xy.Length / 2];
            for (var i = 0; i < pts.Length; i++)
            {
                pts[i] = P(xy[i * 2], xy[i * 2 + 1]);
            }
            g.DrawLines(pen, pts);
        }
        void Circle(float cx, float cy, float r) => g.DrawEllipse(pen, box.X + (cx - r) * k, box.Y + (cy - r) * k, 2 * r * k, 2 * r * k);
        void Dot(float cx, float cy, float r) => g.FillEllipse(fill, box.X + (cx - r) * k, box.Y + (cy - r) * k, 2 * r * k, 2 * r * k);
        void Box(float x, float y, float w, float h, float r)
        {
            using var p = Ink.Round(new RectangleF(box.X + x * k, box.Y + y * k, w * k, h * k), r * k);
            g.DrawPath(pen, p);
        }
        void Arc(float cx, float cy, float r, float start, float sweep) => g.DrawArc(pen, box.X + (cx - r) * k, box.Y + (cy - r) * k, 2 * r * k, 2 * r * k, start, sweep);
        switch (name)
        {
            case "check": Lines(6.5f, 12.5f, 10f, 16f, 17.5f, 8.5f); break;
            case "x": Lines(7.5f, 7.5f, 16.5f, 16.5f); Lines(16.5f, 7.5f, 7.5f, 16.5f); break;
            case "close": Lines(6f, 6f, 18f, 18f); Lines(18f, 6f, 6f, 18f); break;
            case "minus": Lines(6f, 12f, 18f, 12f); break;
            case "clock": Circle(12, 12, 8); Lines(12, 8, 12, 12, 14.6f, 13.6f); break;
            case "plug":
                Lines(9, 4, 9, 8); Lines(15, 4, 15, 8);
                using (var p = new GraphicsPath())
                {
                    p.AddLine(P(7, 8), P(17, 8));
                    p.AddLine(P(17, 8), P(17, 11));
                    p.AddArc(box.X + 7 * k, box.Y + 6 * k, 10 * k, 10 * k, 0, 180);
                    p.AddLine(P(7, 11), P(7, 8));
                    g.DrawPath(pen, p);
                }
                Lines(12, 16, 12, 20); break;
            case "monitor": Box(3, 4, 18, 12, 2); Lines(8, 20, 16, 20); Lines(12, 16, 12, 20); break;
            case "info": Circle(12, 12, 9); Lines(12, 11, 12, 16.5f); Dot(12, 7.8f, 1.2f); break;
            case "history": Arc(12, 12, 8.5f, 200, 290); Lines(3.2f, 6.5f, 4f, 9.4f, 7f, 8.9f); Lines(12, 8, 12, 12, 15, 14); break;
            case "download": Lines(12, 4, 12, 15); Lines(7.5f, 10.5f, 12, 15, 16.5f, 10.5f); Lines(5, 19.5f, 19, 19.5f); break;
            case "code": Lines(9, 7, 4, 12, 9, 17); Lines(15, 7, 20, 12, 15, 17); break;
            case "shield":
                using (var p = new GraphicsPath())
                {
                    p.AddLine(P(12, 3), P(19, 6));
                    p.AddLine(P(19, 6), P(19, 11));
                    p.AddBezier(P(19, 11), P(19, 15.5f), P(16, 19), P(12, 21));
                    p.AddBezier(P(12, 21), P(8, 19), P(5, 15.5f), P(5, 11));
                    p.AddLine(P(5, 11), P(5, 6));
                    p.CloseFigure();
                    g.DrawPath(pen, p);
                }
                break;
            case "alert":
                using (var p = new GraphicsPath())
                {
                    p.AddLines(new[] { P(12, 4), P(21, 19.5f), P(3, 19.5f) });
                    p.CloseFigure();
                    g.DrawPath(pen, p);
                }
                Lines(12, 9.5f, 12, 13.5f); Dot(12, 16.4f, 1.1f); break;
            case "message": Box(4, 5, 16, 11, 3); Lines(8, 16, 8, 19.5f, 12, 16); Lines(8, 9.5f, 16, 9.5f); Lines(8, 12.5f, 13, 12.5f); break;
            case "copy": Box(9, 9, 11, 11, 2); Lines(15, 9, 15, 5.5f, 4.5f, 5.5f, 4.5f, 15, 9, 15); break;
            case "lock": Box(6, 11, 12, 9, 2); Arc(12, 8, 3, 180, 180); Lines(9, 8, 9, 11); Lines(15, 8, 15, 11); break;
            case "trash": Lines(4, 7, 20, 7); Lines(6, 7, 7, 20, 17, 20, 18, 7); Lines(9, 7, 9, 4, 15, 4, 15, 7); break;
        }
    }

    private static Bitmap? labIcon;
    private static DateTime labIconAt;

    /// <summary>The lab's own icon (lab-icon.png, kept by the service), or null for Rabbid Lab's own mark.</summary>
    public static Bitmap? LabIcon()
    {
        try
        {
            var file = Logbook.IconFile;
            if (!File.Exists(file))
            {
                labIcon?.Dispose();
                labIcon = null;
                return null;
            }
            var at = File.GetLastWriteTimeUtc(file);
            if (labIcon is null || at != labIconAt)
            {
                using var read = new MemoryStream(File.ReadAllBytes(file));
                using var image = Image.FromStream(read);
                labIcon?.Dispose();
                labIcon = new Bitmap(image);
                labIconAt = at;
            }
            return labIcon;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>The lab's mark in a square: its own icon, or the console's R on the dark tile with the dot in the accent.</summary>
    public static void Mark(Graphics g, RectangleF r, Color accent)
    {
        if (LabIcon() is { } own)
        {
            g.DrawImage(own, r);
            return;
        }
        var k = r.Width / 64f;
        using (var tile = Ink.Round(r, 15 * k))
        using (var dark = new SolidBrush(Color.FromArgb(0x0B, 0x0C, 0x14)))
        {
            g.FillPath(dark, tile);
        }
        // The R, as index.html's data-brand-mark draws it: translate(10 14) scale(.9).
        PointF P(float x, float y) => new(r.X + (10 + x * 0.9f) * k, r.Y + (14 + y * 0.9f) * k);
        float S(float v) => v * 0.9f * k;
        using var white = new SolidBrush(Color.FromArgb(0xF2, 0xF3, 0xFA));
        g.FillRectangle(white, P(0, 0).X, P(0, 0).Y, S(11), S(40));
        using (var bowl = new GraphicsPath(FillMode.Alternate))
        {
            bowl.AddLine(P(0, 0), P(18, 0));
            bowl.AddArc(P(5, 0).X, P(5, 0).Y, S(26), S(26), 270, 180);
            bowl.AddLine(P(18, 26), P(0, 26));
            bowl.CloseFigure();
            bowl.AddLine(P(11, 9), P(11, 17));
            bowl.AddLine(P(11, 17), P(18, 17));
            bowl.AddArc(P(14, 9).X, P(14, 9).Y, S(8), S(8), 90, -180);
            bowl.CloseFigure();
            g.FillPath(white, bowl);
        }
        g.FillPolygon(white, new[] { P(13, 22), P(24, 22), P(33, 40), P(22, 40) });
        using var dot = Ink.Round(new RectangleF(r.X + 44 * k, r.Y + 39 * k, 11 * k, 11 * k), 1.5f * k);
        using var brush = new SolidBrush(accent);
        g.FillPath(brush, dot);
    }
}
