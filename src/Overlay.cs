using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Clutch
{
    class SavedOverlay : Form
    {
        static SavedOverlay current;
        public static Form Current { get { return current; } }
        readonly Timer timer = new Timer();
        Bitmap card; IntPtr cardBits; Rectangle screen;
        int phase;
        DateTime phaseStart;
        const int InMs = 260, HoldMs = 2600, OutMs = 220;
        int hold = HoldMs;
        static int S(float v) { return Ui.S(v); }

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
            bool wasLeft = left;
            screen = scr; left = Settings.Cur.OverlaySide == 1; yFrac = Math.Max(0, Math.Min(1, Settings.Cur.OverlayY));
            if (wasLeft != left) showing = false;
            hold = holdMs > 0 ? holdMs : HoldMs;
            Draw(title, detail, ok, holdMs > 0);
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
                Place(1 - p * p * p);
                if (p >= 1) { phase = 0; timer.Stop(); Native.ShowWindow(Handle, 0); FreeCard(); }
            }
        }

        void Place(double amount)
        {
            if (card == null) return;
            int visible = Math.Max(1, (int)Math.Round(card.Width * amount));
            Native.POINT dst = new Native.POINT(left ? screen.Left : screen.Right - visible, CardTop), src = new Native.POINT(left ? card.Width - visible : 0, 0);
            Native.SIZE size = new Native.SIZE(visible, card.Height);
            Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION(); blend.Alpha = 255; blend.Format = 1;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero), memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr old = Native.SelectObject(memDc, cardBits);
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
            Native.SelectObject(memDc, old); Native.DeleteDC(memDc); Native.ReleaseDC(IntPtr.Zero, screenDc);
        }

        void Draw(string title, string detail, bool ok, bool wrap)
        {
            FreeCard();
            int h = S(70), logo = S(42), pad = S(16), tx = pad + logo + S(14), dh = S(22);
            using (Font ft = new Font(Ui.BoldFont, 13.5f * Ui.Scale, FontStyle.Bold, GraphicsUnit.Point))
            using (Font fd = new Font(Ui.TextFont, 9.5f * Ui.Scale, FontStyle.Regular, GraphicsUnit.Point))
            {
                int tw, w;
                using (Bitmap probe = new Bitmap(1, 1)) using (Graphics pg = Graphics.FromImage(probe))
                {
                    tw = (int)Math.Ceiling(Math.Max(pg.MeasureString(title, ft).Width, pg.MeasureString(detail, fd).Width));
                    w = Math.Min(S(wrap ? 460 : 420), Math.Max(S(230), tx + tw + S(28)));
                    if (wrap)
                    {
                        dh = (int)Math.Ceiling(Math.Min(pg.MeasureString(detail, fd, w - tx - S(16)).Height, fd.GetHeight(pg) * 4 + 2));
                        h = Math.Max(S(70), S(14) + S(30) + dh + S(14));
                    }
                }
                card = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(card))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);
                    using (GraphicsPath p = LeftRounded(new RectangleF(0, 0, w + S(20), h), S(16)))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(246, 20, 20, 22)))
                    {
                        if (left) using (Matrix mirror = new Matrix(-1, 0, 0, 1, w, 0)) p.Transform(mirror);
                        g.FillPath(b, p);
                    }
                    Rectangle lr = new Rectangle(pad, (h - logo) / 2, logo, logo);
                    if (ok && Ui.Logo != null) g.DrawImage(Ui.Logo, lr);
                    else Ui.DrawIcon(g, ok ? Glyph.Check : Glyph.Warning, 18, lr, ok ? C.AccentHi : C.Warn);
                    StringFormat sf = new StringFormat(StringFormatFlags.NoWrap); sf.Trimming = StringTrimming.EllipsisCharacter;
                    StringFormat wf = new StringFormat(); wf.Trimming = StringTrimming.EllipsisWord;
                    float ty = wrap ? (h - S(30) - dh) / 2f : h / 2f - S(27);
                    using (SolidBrush b = new SolidBrush(Color.White))
                        g.DrawString(title, ft, b, new RectangleF(tx, ty, w - tx - S(12), S(30)), sf);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(225, 225, 230)))
                        g.DrawString(detail, fd, b, new RectangleF(tx, wrap ? ty + S(30) : h / 2f + S(3), w - tx - S(16), dh), wrap ? wf : sf);
                }
            }
            cardBits = card.GetHbitmap(Color.FromArgb(0));
        }

        static GraphicsPath LeftRounded(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath(); float d = rad * 2;
            p.AddLine(r.Right, r.Top, r.Left + rad, r.Top);
            p.AddArc(r.Left, r.Top, d, d, 270, -90);
            p.AddLine(r.Left, r.Top + rad, r.Left, r.Bottom - rad);
            p.AddArc(r.Left, r.Bottom - d, d, d, 180, -90);
            p.AddLine(r.Left + rad, r.Bottom, r.Right, r.Bottom);
            return p;
        }

        public static Bitmap Sample(bool onLeft)
        {
            using (SavedOverlay o = new SavedOverlay())
            {
                o.left = onLeft;
                o.Draw("Clipped!", "Clipped Counter-Strike 2  \u00B7  60s", true, false);
                return new Bitmap(o.card);
            }
        }

        void FreeCard()
        {
            if (cardBits != IntPtr.Zero) { Native.DeleteObject(cardBits); cardBits = IntPtr.Zero; }
            if (card != null) { card.Dispose(); card = null; }
        }

        bool left; double yFrac;
        int CardTop { get { int m = S(28); return screen.Top + m + (int)Math.Round(Math.Max(0, screen.Height - card.Height - m * 2) * yFrac); } }

#if DEV
        public Rectangle ShotRect { get { return card == null ? Rectangle.Empty : new Rectangle(left ? screen.Left : screen.Right - card.Width, CardTop, card.Width, card.Height); } }
#endif

        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); FreeCard(); } base.Dispose(disposing); }
    }

    class OverlayEditor : Form
    {
        Bitmap backdrop, cardL, cardR;
        readonly Rectangle screen;
        bool left; double y; bool drag; int grabDy;
        readonly Btn save = new Btn("Save", "", BtnStyle.Accent), cancel = new Btn("Cancel", "", BtnStyle.Standard);
        public bool Saved;
#if DEV
        public static bool NoBackdrop;
#endif
        static int S(float v) { return Ui.S(v); }

        public OverlayEditor(Rectangle scr)
        {
            screen = scr; left = Settings.Cur.OverlaySide == 1; y = Settings.Cur.OverlayY;
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
            cardL = SavedOverlay.Sample(true); cardR = SavedOverlay.Sample(false);
            save.Click += delegate { Settings.Cur.OverlaySide = left ? 1 : 0; Settings.Cur.OverlayY = Math.Round(y, 3); Settings.Save(); Saved = true; Close(); };
            cancel.Click += delegate { Close(); };
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Enter) save.PerformClick();
                else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) { y = Math.Max(0, Math.Min(1, y + (e.KeyCode == Keys.Up ? -0.02 : 0.02))); Invalidate(); }
                else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) { left = e.KeyCode == Keys.Left; Invalidate(); }
            };
            Controls.Add(save); Controls.Add(cancel);
            int bw = S(110), bh = S(40), cx = scr.Width / 2;
            cancel.SetBounds(cx + S(6), scr.Height / 2 + S(44), bw, bh); save.SetBounds(cx - S(6) - bw, scr.Height / 2 + S(44), bw, bh);
            foreach (Btn b in new[] { save, cancel }) b.BackColor = Color.FromArgb(20, 19, 28);
        }

        Bitmap Card { get { return left ? cardL : cardR; } }
        int Margin_ { get { return S(28); } }

        Rectangle CardRect
        {
            get
            {
                Bitmap c = Card; int m = Margin_;
                int top = m + (int)Math.Round(Math.Max(0, screen.Height - c.Height - m * 2) * y);
                return new Rectangle(left ? 0 : screen.Width - c.Width, top, c.Width, c.Height);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Rectangle r = CardRect;
            drag = true; grabDy = r.Contains(e.Location) ? e.Y - r.Y : r.Height / 2;
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
            int m = Margin_, h = Card.Height;
            left = p.X < screen.Width / 2;
            y = Math.Max(0, Math.Min(1, (p.Y - grabDy - m) / (double)Math.Max(1, screen.Height - h - m * 2)));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (backdrop != null) g.DrawImageUnscaled(backdrop, 0, 0); else g.Clear(Color.FromArgb(14, 13, 20));
            Rectangle r = CardRect;
            using (Pen guide = new Pen(Color.FromArgb(90, 255, 255, 255), 1) { DashStyle = DashStyle.Dash })
            {
                int gx = left ? S(2) : screen.Width - S(3);
                g.DrawLine(guide, gx, Margin_, gx, screen.Height - Margin_);
            }
            g.DrawImageUnscaled(Card, r.X, r.Y);
            Rectangle tr = new Rectangle(0, screen.Height / 2 - S(60), screen.Width, S(34));
            Ui.TextCenter(g, "Drag the notice to where you want it", Ui.F(16, 4), tr, Color.White);
            Ui.TextCenter(g, "Either side of the screen, any height. Arrow keys work too. Enter saves, Esc cancels.", Ui.F(10, 0), new Rectangle(0, tr.Bottom + S(4), screen.Width, S(24)), Color.FromArgb(205, 205, 215));
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (backdrop != null) backdrop.Dispose(); if (cardL != null) cardL.Dispose(); if (cardR != null) cardR.Dispose(); }
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
