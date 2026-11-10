using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Clutch
{
    static class NoticeSpot
    {
        public const int Right = 0, Left = 1, Top = 2, Bottom = 3;
        public static int Margin { get { return Ui.S(20); } }

        public static Point Place(Rectangle screen, Size card, int side, double along)
        {
            int m = Margin;
            along = Math.Max(0, Math.Min(1, along));
            if (side == Top || side == Bottom)
            {
                int x = screen.Left + m + (int)Math.Round(Math.Max(0, screen.Width - card.Width - m * 2) * along);
                return new Point(x, side == Top ? screen.Top + m : screen.Bottom - m - card.Height);
            }
            int y = screen.Top + m + (int)Math.Round(Math.Max(0, screen.Height - card.Height - m * 2) * along);
            return new Point(side == Left ? screen.Left + m : screen.Right - m - card.Width, y);
        }

        public static Point Offset(int side, int d)
        {
            return side == Left ? new Point(-d, 0) : side == Top ? new Point(0, -d) : side == Bottom ? new Point(0, d) : new Point(d, 0);
        }

        public static string Describe(int side, double along)
        {
            if (side == Top || side == Bottom)
                return (side == Top ? "Top" : "Bottom") + " of the screen, " + (along < 0.2 ? "on the left" : along > 0.8 ? "on the right" : along < 0.4 ? "left of centre" : along > 0.6 ? "right of centre" : "in the middle");
            return (side == Left ? "Left" : "Right") + " edge, " + (along < 0.2 ? "near the top" : along > 0.8 ? "near the bottom" : along < 0.4 ? "in the upper half" : along > 0.6 ? "in the lower half" : "in the middle");
        }
    }

    class SavedOverlay : Form
    {
        static SavedOverlay current;
        public static Form Current { get { return current; } }
        readonly Timer timer = new Timer();
        Bitmap card; IntPtr cardBits; Rectangle screen;
        int phase, side; double along;
        DateTime phaseStart;
        const int InMs = 280, HoldMs = 2600, OutMs = 220;
        int hold = HoldMs;
        static int S(float v) { return Ui.S(v); }
        static int Pad { get { return S(22); } }

        public SavedOverlay()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            timer.Tick += Step;
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8000000 | 0x8;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Program.ShotPage == null) try { Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE); } catch { }
        }

        public static void Pop(string title, string detail, bool ok, Rectangle screen) { Pop(title, detail, ok, screen, 0); }

        public static void Pop(string title, string detail, bool ok, Rectangle screen, int holdMs)
        {
            if (current == null || current.IsDisposed) current = new SavedOverlay();
            current.Show(title, detail, ok, screen, holdMs);
        }

        void Show(string title, string detail, bool ok, Rectangle scr, int holdMs)
        {
            bool showing = phase == 1 || phase == 2;
            int was = side;
            screen = scr; side = Settings.Cur.OverlaySide;
            along = side == NoticeSpot.Top || side == NoticeSpot.Bottom ? Settings.Cur.OverlayX : Settings.Cur.OverlayY;
            if (was != side) showing = false;
            hold = holdMs > 0 ? holdMs : HoldMs;
            FreeCard(); card = Render(title, detail, ok, holdMs > 0); cardBits = card.GetHbitmap(Color.FromArgb(0));
            if (!IsHandleCreated) CreateHandle();
            if (showing && phase == 2) { phaseStart = DateTime.Now; Place(1); }
            else { phase = 1; phaseStart = DateTime.Now; Place(0); }
            Native.ShowWindow(Handle, 4);
            TopMost = true;
            timer.Interval = 15; timer.Start();
        }

        void Step(object s, EventArgs e)
        {
            double t = (DateTime.Now - phaseStart).TotalMilliseconds;
            if (phase == 1)
            {
                double p = Math.Min(1, t / InMs);
                Place(1 - Math.Pow(1 - p, 3));
                if (p >= 1) { phase = 2; phaseStart = DateTime.Now; timer.Interval = hold; }
            }
            else if (phase == 2) { phase = 3; phaseStart = DateTime.Now; timer.Interval = 15; }
            else if (phase == 3)
            {
                double p = Math.Min(1, t / OutMs);
                Place(1 - p * p);
                if (p >= 1) { phase = 0; timer.Stop(); Native.ShowWindow(Handle, 0); FreeCard(); }
            }
        }

        Point Final { get { Point v = NoticeSpot.Place(screen, new Size(card.Width - Pad * 2, card.Height - Pad * 2), side, along); return new Point(v.X - Pad, v.Y - Pad); } }

        void Place(double amount)
        {
            if (card == null) return;
            Point f = Final, off = NoticeSpot.Offset(side, S(36));
            double back = 1 - amount;
            Native.POINT dst = new Native.POINT(f.X + (int)Math.Round(off.X * back), f.Y + (int)Math.Round(off.Y * back)), src = new Native.POINT(0, 0);
            Native.SIZE size = new Native.SIZE(card.Width, card.Height);
            Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION(); blend.Alpha = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(255 * amount))); blend.Format = 1;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero), memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr old = Native.SelectObject(memDc, cardBits);
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
            Native.SelectObject(memDc, old); Native.DeleteDC(memDc); Native.ReleaseDC(IntPtr.Zero, screenDc);
        }

        public static Bitmap Render(string title, string detail, bool ok, bool wrap)
        {
            int pad = Pad, h = S(74), icon = S(46), inner = S(14), tx = inner + icon + S(14), dh = S(22);
            using (Font ft = new Font(Ui.BoldFont, 13.5f * Ui.Scale, FontStyle.Bold, GraphicsUnit.Point))
            using (Font fd = new Font(Ui.TextFont, 9.5f * Ui.Scale, FontStyle.Regular, GraphicsUnit.Point))
            {
                int w;
                using (Bitmap probe = new Bitmap(1, 1)) using (Graphics pg = Graphics.FromImage(probe))
                {
                    int tw = (int)Math.Ceiling(Math.Max(pg.MeasureString(title, ft).Width, pg.MeasureString(detail, fd).Width));
                    w = Math.Min(S(wrap ? 460 : 420), Math.Max(S(250), tx + tw + S(26)));
                    if (wrap)
                    {
                        dh = (int)Math.Ceiling(Math.Min(pg.MeasureString(detail, fd, w - tx - S(18)).Height, fd.GetHeight(pg) * 4 + 2));
                        h = Math.Max(S(74), S(16) + S(30) + dh + S(16));
                    }
                }
                Bitmap bmp = new Bitmap(w + pad * 2, h + pad * 2, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);
                    float rad = S(18);
                    for (int i = 0; i < 9; i++)
                    {
                        float grow = pad * (i + 1) / 10f;
                        RectangleF sr = new RectangleF(pad - grow, pad - grow + S(5), w + grow * 2, h + grow * 2);
                        using (GraphicsPath sp = Ui.Round(sr, rad + grow)) using (SolidBrush sb = new SolidBrush(Color.FromArgb(9 - i, 0, 0, 0))) g.FillPath(sb, sp);
                    }
                    RectangleF cr = new RectangleF(pad, pad, w, h);
                    using (GraphicsPath p = Ui.Round(cr, rad))
                    {
                        using (LinearGradientBrush lg = new LinearGradientBrush(cr, Color.FromArgb(250, 32, 28, 48), Color.FromArgb(250, 20, 18, 30), 90f)) g.FillPath(lg, p);
                        Region keep = g.Clip; g.SetClip(p, CombineMode.Intersect);
                        RectangleF glow = new RectangleF(pad - S(30), pad - S(40), S(150), h + S(80));
                        using (GraphicsPath gp = new GraphicsPath())
                        {
                            gp.AddEllipse(glow);
                            using (PathGradientBrush pb = new PathGradientBrush(gp)) { pb.CenterColor = Color.FromArgb(ok ? 70 : 50, ok ? C.Accent : C.Warn); pb.SurroundColors = new[] { Color.FromArgb(0, C.Accent) }; g.FillPath(pb, gp); }
                        }
                        g.Clip = keep;
                        using (Pen edge = new Pen(Color.FromArgb(40, 255, 255, 255), 1)) g.DrawPath(edge, p);
                    }
                    Rectangle ir = new Rectangle(pad + inner, pad + (h - icon) / 2, icon, icon);
                    using (GraphicsPath ip = Ui.Round(ir, S(12)))
                    using (LinearGradientBrush ib = new LinearGradientBrush(ir, ok ? C.AccentHi : C.Warn, ok ? C.Accent : Color.FromArgb(200, 120, 30), 45f)) g.FillPath(ib, ip);
                    if (ok) Ui.DrawIcon(g, Glyph.Check, 16, ir, Color.White);
                    else Ui.DrawIcon(g, Glyph.Warning, 15, ir, Color.White);
                    StringFormat sf = new StringFormat(StringFormatFlags.NoWrap); sf.Trimming = StringTrimming.EllipsisCharacter;
                    StringFormat wf = new StringFormat(); wf.Trimming = StringTrimming.EllipsisWord;
                    float ty = wrap ? pad + (h - S(30) - dh) / 2f : pad + h / 2f - S(27);
                    using (SolidBrush b = new SolidBrush(Color.White))
                        g.DrawString(title, ft, b, new RectangleF(pad + tx, ty, w - tx - S(14), S(30)), sf);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(205, 200, 222)))
                        g.DrawString(detail, fd, b, new RectangleF(pad + tx, wrap ? ty + S(30) : pad + h / 2f + S(3), w - tx - S(18), dh), wrap ? wf : sf);
                }
                return bmp;
            }
        }

        public static Bitmap Sample() { return Render("Clipped!", "Clipped Counter-Strike 2  \u00B7  60s", true, false); }

        void FreeCard()
        {
            if (cardBits != IntPtr.Zero) { Native.DeleteObject(cardBits); cardBits = IntPtr.Zero; }
            if (card != null) { card.Dispose(); card = null; }
        }

#if DEV
        public Rectangle ShotRect { get { if (card == null) return Rectangle.Empty; Point f = Final; return new Rectangle(f.X, f.Y, card.Width, card.Height); } }
#endif

        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); FreeCard(); } base.Dispose(disposing); }
    }

    class OverlayEditor : Form
    {
        Bitmap backdrop, sample;
        readonly Rectangle screen;
        int side; double along; bool drag; Point grab;
        readonly Btn save = new Btn("Save", "", BtnStyle.Accent), cancel = new Btn("Cancel", "", BtnStyle.Standard);
        public bool Saved;
#if DEV
        public static bool NoBackdrop;
#endif
        static int S(float v) { return Ui.S(v); }
        static int Pad { get { return S(22); } }

        public OverlayEditor(Rectangle scr)
        {
            screen = scr; side = Settings.Cur.OverlaySide;
            along = side == NoticeSpot.Top || side == NoticeSpot.Bottom ? Settings.Cur.OverlayX : Settings.Cur.OverlayY;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; Bounds = scr; TopMost = true; ShowInTaskbar = false;
            BackColor = Color.Black; DoubleBuffered = true; KeyPreview = true; Cursor = Cursors.Default;
            try
            {
#if DEV
                if (NoBackdrop) throw new Exception();
#endif
                backdrop = new Bitmap(scr.Width, scr.Height);
                using (Graphics g = Graphics.FromImage(backdrop))
                {
                    g.CopyFromScreen(scr.Location, Point.Empty, scr.Size);
                    using (SolidBrush dim = new SolidBrush(Color.FromArgb(150, 8, 8, 12))) g.FillRectangle(dim, 0, 0, scr.Width, scr.Height);
                }
            }
            catch { backdrop = null; }
            sample = SavedOverlay.Sample();
            save.Click += delegate
            {
                Settings.Cur.OverlaySide = side;
                if (side == NoticeSpot.Top || side == NoticeSpot.Bottom) Settings.Cur.OverlayX = Math.Round(along, 3); else Settings.Cur.OverlayY = Math.Round(along, 3);
                Settings.Save(); Saved = true; Close();
            };
            cancel.Click += delegate { Close(); };
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Enter) save.PerformClick();
                else if (e.KeyCode == Keys.Up) { side = NoticeSpot.Top; along = 0.5; Invalidate(); }
                else if (e.KeyCode == Keys.Down) { side = NoticeSpot.Bottom; along = 0.5; Invalidate(); }
                else if (e.KeyCode == Keys.Left) { side = NoticeSpot.Left; along = 0.1; Invalidate(); }
                else if (e.KeyCode == Keys.Right) { side = NoticeSpot.Right; along = 0.1; Invalidate(); }
            };
            Controls.Add(save); Controls.Add(cancel);
            int bw = S(110), bh = S(40), cx = scr.Width / 2;
            cancel.SetBounds(cx + S(6), scr.Height / 2 + S(44), bw, bh); save.SetBounds(cx - S(6) - bw, scr.Height / 2 + S(44), bw, bh);
            foreach (Btn b in new[] { save, cancel }) b.BackColor = Color.FromArgb(20, 19, 28);
        }

        Size CardSize { get { return new Size(sample.Width - Pad * 2, sample.Height - Pad * 2); } }

        Rectangle CardRect
        {
            get
            {
                Point v = NoticeSpot.Place(new Rectangle(0, 0, screen.Width, screen.Height), CardSize, side, along);
                return new Rectangle(v, CardSize);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Rectangle r = CardRect;
            drag = true; grab = r.Contains(e.Location) ? new Point(e.X - r.X, e.Y - r.Y) : new Point(r.Width / 2, r.Height / 2);
            MoveTo(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag) MoveTo(e.Location);
            else Cursor = CardRect.Contains(e.Location) ? Cursors.SizeAll : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = false; }

        void MoveTo(Point p)
        {
            Size c = CardSize; int m = NoticeSpot.Margin;
            int x = p.X - grab.X, y = p.Y - grab.Y, cx = x + c.Width / 2, cy = y + c.Height / 2;
            double dl = cx / (double)screen.Width, dr = 1 - dl, dt = cy / (double)screen.Height, db = 1 - dt;
            double best = Math.Min(Math.Min(dl, dr), Math.Min(dt, db));
            side = best == dt ? NoticeSpot.Top : best == db ? NoticeSpot.Bottom : best == dl ? NoticeSpot.Left : NoticeSpot.Right;
            if (side == NoticeSpot.Top || side == NoticeSpot.Bottom) along = Math.Max(0, Math.Min(1, (x - m) / (double)Math.Max(1, screen.Width - c.Width - m * 2)));
            else along = Math.Max(0, Math.Min(1, (y - m) / (double)Math.Max(1, screen.Height - c.Height - m * 2)));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; Ui.Prep(g);
            if (backdrop != null) g.DrawImageUnscaled(backdrop, 0, 0); else g.Clear(Color.FromArgb(14, 13, 20));
            int m = NoticeSpot.Margin / 2;
            using (Pen guide = new Pen(Color.FromArgb(60, 255, 255, 255), 1) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(guide, m, m, screen.Width - m * 2 - 1, screen.Height - m * 2 - 1);
            using (Pen on = new Pen(Color.FromArgb(220, C.AccentHi), S(3)))
            {
                if (side == NoticeSpot.Top) g.DrawLine(on, m, m, screen.Width - m, m);
                else if (side == NoticeSpot.Bottom) g.DrawLine(on, m, screen.Height - m, screen.Width - m, screen.Height - m);
                else if (side == NoticeSpot.Left) g.DrawLine(on, m, m, m, screen.Height - m);
                else g.DrawLine(on, screen.Width - m, m, screen.Width - m, screen.Height - m);
            }
            Rectangle r = CardRect;
            g.DrawImageUnscaled(sample, r.X - Pad, r.Y - Pad);
            Rectangle tr = new Rectangle(0, screen.Height / 2 - S(60), screen.Width, S(34));
            Ui.TextCenter(g, "Drag the notice to where you want it", Ui.F(16, 4), tr, Color.White);
            Ui.TextCenter(g, "Any edge of the screen: left, right, top or bottom. Arrow keys pick an edge. Enter saves, Esc cancels.", Ui.F(10, 0), new Rectangle(0, tr.Bottom + S(4), screen.Width, S(24)), Color.FromArgb(205, 205, 215));
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (backdrop != null) backdrop.Dispose(); if (sample != null) sample.Dispose(); }
            base.Dispose(disposing);
        }
    }

    static class Sfx
    {
        static System.Media.SoundPlayer player;
        public static void Saved()
        {
            try
            {
                if (player == null)
                {
                    int rate = 44100, n = rate * 380 / 1000;
                    System.IO.MemoryStream m = new System.IO.MemoryStream(); System.IO.BinaryWriter w = new System.IO.BinaryWriter(m);
                    w.Write(0x46464952); w.Write(36 + n * 2); w.Write(0x45564157); w.Write(0x20746D66);
                    w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                    w.Write(0x61746164); w.Write(n * 2);
                    double thump = 0, a = 0, b = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double t = (double)i / rate;
                        thump += 2 * Math.PI * (80 + 110 * Math.Exp(-t / 0.025)) / rate;
                        double v = Math.Sin(thump) * Math.Exp(-t / 0.035) * 0.55;
                        a += 2 * Math.PI * 1318.5 / rate; b += 2 * Math.PI * 1975.5 / rate;
                        v += Note(a, t) * 0.32;
                        if (t > 0.055) v += Note(b, t - 0.055) * 0.30;
                        double fade = i > n - rate * 0.02 ? (n - i) / (rate * 0.02) : 1;
                        w.Write((short)(Math.Max(-1, Math.Min(1, v * 0.42 * fade)) * 32767));
                    }
                    m.Position = 0; player = new System.Media.SoundPlayer(m); player.Load();
                }
                player.Play();
            }
            catch { }
        }

        static double Note(double ph, double t)
        {
            double attack = Math.Min(1, t / 0.004);
            return (Math.Sin(ph) + 0.18 * Math.Sin(ph * 2)) * attack * Math.Exp(-t / 0.09);
        }
    }
}
