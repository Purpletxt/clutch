using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace Clutch
{
    static class C
    {
        public static readonly Color Bg = Color.FromArgb(19, 18, 28);
        public static readonly Color Chrome = Color.FromArgb(12, 11, 19);
        public static readonly Color Panel = Color.FromArgb(25, 23, 37);
        public static readonly Color Card = Color.FromArgb(29, 27, 43);
        public static readonly Color CardHi = Color.FromArgb(37, 34, 54);
        public static readonly Color Control = Color.FromArgb(41, 38, 59);
        public static readonly Color ControlHi = Color.FromArgb(53, 49, 76);
        public static readonly Color Stroke = Color.FromArgb(47, 44, 67);
        public static readonly Color Text = Color.FromArgb(243, 241, 250);
        public static readonly Color Sub = Color.FromArgb(156, 150, 182);
        public static readonly Color Faint = Color.FromArgb(101, 96, 128);
        public static readonly Color Accent = Color.FromArgb(128, 56, 255);
        public static readonly Color AccentHi = Color.FromArgb(166, 108, 255);
        public static readonly Color AccentText = Color.White;
        public static readonly Color Live = Color.FromArgb(255, 70, 104);
        public static readonly Color Danger = Color.FromArgb(255, 92, 118);
        public static readonly Color Good = Color.FromArgb(70, 205, 130);
        public static readonly Color Warn = Color.FromArgb(245, 182, 66);
        public static readonly Color Gold = Color.FromArgb(255, 200, 70);
    }

    static class Ui
    {
        public static float Scale = 1f;
        public static string TextFont = "Segoe UI", SemiFont = "Segoe UI Semibold", BoldFont = "Segoe UI", DisplayFont = "Segoe UI Semibold", IconFont = "Segoe MDL2 Assets";
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public static void Init()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
            List<string> names = new List<string>();
            foreach (FontFamily f in new InstalledFontCollection().Families) names.Add(f.Name);
            Func<string, string> find = delegate (string prefix) { foreach (string n in names) if (n.StartsWith(prefix)) return n; return null; };
            IconFont = find("Segoe Fluent Icons") ?? "Segoe MDL2 Assets";
            TextFont = find("Segoe UI Variable Text") ?? "Segoe UI";
            SemiFont = find("Segoe UI Variable Text Semib") ?? "Segoe UI Semibold";
            DisplayFont = find("Segoe UI Variable Display Semib") ?? SemiFont;
            BoldFont = names.Contains("Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";
        }

        public static int S(float v) { return (int)Math.Round(v * Scale); }

        public static Font F(float pt, int weight)
        {
            string key = pt + "|" + weight + "|" + Scale; Font f;
            if (fonts.TryGetValue(key, out f)) return f;
            string fam = weight == 0 || weight == 5 ? TextFont : weight == 1 ? SemiFont : weight == 2 ? DisplayFont : weight == 3 ? IconFont : BoldFont;
            f = new Font(fam, pt * 96f / 72f * Scale, weight >= 4 ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            fonts[key] = f; return f;
        }
        public static Font Icon(float pt) { return F(pt, 3); }

        public static void DrawIcon(Graphics g, string glyph, float pt, Rectangle r, Color c)
        {
            if (string.IsNullOrEmpty(glyph)) return;
            float em = pt * 96f / 72f * Scale;
            if (glyph.StartsWith("rail:"))
            {
                int s = Math.Max(8, (int)Math.Round(em * 1.25f));
                g.DrawImage(RailIcons.Cached(glyph.Substring(5), s, c), r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s);
                return;
            }
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddString(glyph, Icon(pt).FontFamily, 0, em, PointF.Empty, StringFormat.GenericTypographic);
                RectangleF b = p.GetBounds();
                if (b.Width <= 0) return;
                using (Matrix m = new Matrix()) { m.Translate(r.X + (r.Width - b.Width) / 2f - b.X, r.Y + (r.Height - b.Height) / 2f - b.Y); p.Transform(m); }
                SmoothingMode old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush br = new SolidBrush(c)) g.FillPath(br, p);
                using (Pen pen = new Pen(c, Math.Max(1f, em * 0.06f))) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, p); }
                g.SmoothingMode = old;
            }
        }

        public static event Action ScaleChanged;
        public static bool HandleDpiChanged(Form f, ref Message m)
        {
            if (m.Msg != Native.WM_DPICHANGED) return false;
            int dpi = m.WParam.ToInt32() & 0xFFFF;
            Native.RECT r = (Native.RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
            float ns = dpi / 96f;
            if (Math.Abs(ns - Scale) > 0.01f)
            {
                Scale = ns;
                if (ScaleChanged != null) ScaleChanged();
            }
            f.Bounds = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            m.Result = IntPtr.Zero;
            return true;
        }

        public static Color Mix(Color a, Color b, float t)
        {
            if (t <= 0) return a; if (t >= 1) return b;
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
        public static Color FromHue(int h)
        {
            float s = 0.55f, v = 0.78f, c = v * s, x = c * (1 - Math.Abs((h / 60f) % 2 - 1)), m = v - c, r, g, b;
            if (h < 60) { r = c; g = x; b = 0; } else if (h < 120) { r = x; g = c; b = 0; } else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; } else if (h < 300) { r = x; g = 0; b = c; } else { r = c; g = 0; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }
        public static Color Alpha(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c); }

        public static void Prep(Graphics g) { g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float rad, Color fill)
        {
            using (GraphicsPath p = Round(r, rad)) using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
        }
        public static void FillAccent(Graphics g, RectangleF r, float rad, float lift, int alpha)
        {
            if (r.Width < 1 || r.Height < 1 || alpha <= 0) return;
            Color c = lift >= 0 ? Mix(C.Accent, Color.White, lift) : Mix(C.Accent, Color.Black, -lift);
            FillRound(g, r, rad, Alpha(c, alpha));
        }

        public static void StrokeRound(Graphics g, RectangleF r, float rad, Color c, float width)
        {
            using (GraphicsPath p = Round(r, rad)) using (Pen pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        public static void ImageRound(Graphics g, Image img, RectangleF r, float rad)
        {
            float sx = r.Width / img.Width, sy = r.Height / img.Height, s = Math.Max(sx, sy);
            float w = img.Width * s, h = img.Height * s;
            using (TextureBrush tb = new TextureBrush(img, WrapMode.Clamp))
            using (GraphicsPath p = Round(r, rad))
            {
                tb.ScaleTransform(s, s);
                tb.TranslateTransform((r.X + (r.Width - w) / 2) / s, (r.Y + (r.Height - h) / 2) / s, MatrixOrder.Prepend);
                g.FillPath(tb, p);
            }
        }

        const TextFormatFlags Base = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags extra) { TextRenderer.DrawText(g, s, f, r, c, Base | extra); }
        public static void TextLeft(Graphics g, string s, Font f, Rectangle r, Color c) { Text(g, s, f, r, c, TextFormatFlags.VerticalCenter | TextFormatFlags.Left); }
        public static void TextRight(Graphics g, string s, Font f, Rectangle r, Color c) { Text(g, s, f, r, c, TextFormatFlags.VerticalCenter | TextFormatFlags.Right); }
        public static void TextCenter(Graphics g, string s, Font f, Rectangle r, Color c) { Text(g, s, f, r, c, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter); }
        public static void Wrap(Graphics g, string s, Font f, Rectangle r, Color c)
        {
            TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top);
        }
        public static Size Measure(string s, Font f)
        {
            return TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
        public static int WrapHeight(string s, Font f, int width)
        {
            return TextRenderer.MeasureText(s, f, new Size(width, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        }

        public static void StyleTitleBar(Form f, Color caption)
        {
            int v = 1; Native.DwmSetWindowAttribute(f.Handle, 20, ref v, 4);
            v = 2; Native.DwmSetWindowAttribute(f.Handle, 33, ref v, 4);
            v = caption.R | (caption.G << 8) | (caption.B << 16); Native.DwmSetWindowAttribute(f.Handle, 35, ref v, 4);
            v = C.Text.R | (C.Text.G << 8) | (C.Text.B << 16); Native.DwmSetWindowAttribute(f.Handle, 36, ref v, 4);
        }

        static Bitmap logo; static bool logoTried;
        public static Bitmap Logo
        {
            get
            {
                if (!logoTried)
                {
                    logoTried = true;
                    Stream s = typeof(Ui).Assembly.GetManifestResourceStream("logo.png");
                    if (s != null) logo = new Bitmap(s);
                }
                return logo;
            }
        }

        public static void DrawLogo(Graphics g, Rectangle r)
        {
            if (Logo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(Logo, r); return;
            }
            using (GraphicsPath p = Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), r.Width * 0.28f))
            using (Pen pen = new Pen(C.Faint, Math.Max(1, S(1.2f))))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawPath(pen, p);
            }
        }

        public static System.Drawing.Icon MakeIcon(Color dot, int size)
        {
            using (Bitmap b = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    Prep(g); g.Clear(Color.Transparent);
                    if (Logo != null)
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        if (dot == C.Faint)
                            using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
                            {
                                float l = 0.33f;
                                ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new float[][] { new float[] { l, l, l, 0, 0 }, new float[] { l, l, l, 0, 0 }, new float[] { l, l, l, 0, 0 }, new float[] { 0, 0, 0, 0.75f, 0 }, new float[] { 0.25f, 0.25f, 0.28f, 0, 1 } }));
                                g.DrawImage(Logo, new Rectangle(0, 0, size, size), 0, 0, Logo.Width, Logo.Height, GraphicsUnit.Pixel, ia);
                            }
                        else g.DrawImage(Logo, 0, 0, size, size);
                    }
                    else
                    {
                        FillRound(g, new RectangleF(0, 0, size, size), size * 0.26f, C.Card);
                        float d = size * 0.46f;
                        using (SolidBrush br = new SolidBrush(dot)) g.FillEllipse(br, (size - d) / 2, (size - d) / 2, d, d);
                    }
                }
                IntPtr h = b.GetHicon();
                System.Drawing.Icon ic = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return ic;
            }
        }
    }

    class Anim
    {
        public float Value, Target; readonly float speed; readonly Control owner;
        static Timer timer; static readonly List<Anim> active = new List<Anim>();

        public Anim(Control owner, float v, float speed) { this.owner = owner; Value = Target = v; this.speed = speed; }

        public void To(float t)
        {
            Target = t; if (Math.Abs(Value - t) < 0.001f) return;
            if (!active.Contains(this)) active.Add(this);
            if (timer == null) { timer = new Timer(); timer.Interval = 15; timer.Tick += Tick; }
            timer.Enabled = true;
        }
        public void Snap(float v) { Value = Target = v; if (owner != null) owner.Invalidate(); }

        static void Tick(object s, EventArgs e)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Anim a = active[i]; float d = a.Target - a.Value;
                if (Math.Abs(d) < 0.004f) { a.Value = a.Target; active.RemoveAt(i); } else a.Value += d * a.speed;
                if (a.owner != null && !a.owner.IsDisposed) a.owner.Invalidate();
            }
            if (active.Count == 0) timer.Enabled = false;
        }
    }

    static class Glyph
    {
        public const string Play = "\uE768", Pause = "\uE769", Back = "\uE72B", Star = "rail:star-line", StarFill = "rail:star",
            Delete = "\uE74D", Rename = "\uE70F", Copy = "\uE8C8", Folder = "\uE838", Cut = "\uE8C6", Settings = "rail:settings", Video = "\uE714",
            Search = "\uE721", Add = "\uE710", Close = "\uE711", Mic = "\uE720", Volume = "\uE767", Game = "rail:games",
            Check = "\uE73E", Download = "\uE896", More = "\uE712", Info = "\uE946", Warning = "\uE7BA", Monitor = "\uE7F4",
            Film = "rail:clips", Keyboard = "\uE92E", Clock = "\uE823", Save = "\uE74E", Share = "\uE72D", Undo = "\uE7A7", Mute = "\uE74F",
            Down = "\uE70D", Up = "\uE70E";
    }
}
