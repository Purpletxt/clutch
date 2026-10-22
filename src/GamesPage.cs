using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace Clutch
{
    class GamesPage : Page
    {
        readonly Lbl title = new Lbl("Games", 24, 4, C.Text), count = new Lbl("", 10.5f, 1, C.Faint);
        readonly Btn manage = new Btn("Auto-start settings", Glyph.Settings, BtnStyle.Standard);
        readonly GameGrid grid;

        public GamesPage(MainForm m) : base(m)
        {
            manage.Click += delegate { Main.Go(Main.SettingsP); Main.SettingsP.ScrollTo("settings-games"); };
            grid = new GameGrid(m);
            foreach (Control c in new Control[] { title, count, manage, grid }) Controls.Add(c);
        }

        public override void Relayout()
        {
            int pad = S(24), w = ClientSize.Width, y = S(10), h = S(36);
            title.SetBounds(pad - S(2), y, Ui.Measure("Games", Ui.F(24, 4)).Width + S(4), S(48));
            manage.Height = h; manage.Width = manage.PreferredWidth() + S(6); manage.Location = new Point(w - pad - manage.Width, y + S(6));
            count.SetBounds(title.Right + S(12), y + S(8), Math.Max(0, manage.Left - title.Right - S(20)), S(40));
            y += S(48) + S(18);
            grid.SetBounds(pad - S(4), y, w - pad * 2 + S(8), ClientSize.Height - y);
        }

        public new void Refresh()
        {
            grid.Build();
            int n = grid.Count;
            count.Text = n == 1 ? "1 game" : n + " games";
            count.Invalidate(); grid.Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.Draw(e.Graphics, ClientSize, Point.Empty); }
        public override void Entered() { Refresh(); Relayout(); }
        public override void Leaving() { grid.FreeBanners(); }
    }

    class GameGrid : Ctl
    {
        class Game { public string Name, Exe; public int Clips; public double Seconds; public DateTime Last; public bool AutoStart; }
        readonly MainForm main;
        readonly List<Game> games = new List<Game>();
        readonly Dictionary<string, Bitmap> banners = new Dictionary<string, Bitmap>();
        Size bannerSize;
        int scroll, hot = -1, cols = 1, tileW, tileH, bannerH, gap;
        static readonly Queue<string> fetchQueue = new Queue<string>(); static Thread fetcher;

        public GameGrid(MainForm m) { main = m; }
        public int Count { get { return games.Count; } }

        public void Build()
        {
            games.Clear();
            Dictionary<string, Game> by = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
            foreach (Clip c in Library.Clips)
            {
                if (c.Game.Length == 0) continue;
                Game g; if (!by.TryGetValue(c.Game, out g)) { g = new Game(); g.Name = c.Game; by[c.Game] = g; games.Add(g); }
                g.Clips++; g.Seconds += Math.Max(0, c.Duration); if (c.Created > g.Last) g.Last = c.Created;
            }
            foreach (GameEntry e in Settings.Cur.Games)
            {
                string n = Games.Clean(e.Name.Length > 0 ? e.Name : System.IO.Path.GetFileNameWithoutExtension(e.Exe));
                Game g; if (!by.TryGetValue(n, out g)) { g = new Game(); g.Name = n; by[n] = g; games.Add(g); }
                g.AutoStart = true;
            }
            foreach (Game g in games) g.Exe = GameIcons.ExeFor(g.Name);
            games.Sort(delegate (Game a, Game b) { int c = b.Last.CompareTo(a.Last); return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            hot = -1; Clamp();
            foreach (Game g in games) { bool tried; if (g.Exe != null && GameArt.BannerFile(g.Exe, out tried) == null && !tried) Fetch(g.Exe); }
        }

        void Fetch(string exe)
        {
            if (Program.DevRun && !Program.Demo) return;
            lock (fetchQueue)
            {
                if (fetchQueue.Contains(exe)) return;
                fetchQueue.Enqueue(exe);
                if (fetcher != null && fetcher.IsAlive) return;
                fetcher = new Thread(delegate ()
                {
                    while (true)
                    {
                        string next; lock (fetchQueue) { if (fetchQueue.Count == 0) { fetcher = null; return; } next = fetchQueue.Peek(); }
                        GameArt.FetchBanner(next);
                        lock (fetchQueue) fetchQueue.Dequeue();
                        try { BeginInvoke((MethodInvoker)delegate { FreeBanners(); Invalidate(); }); } catch { }
                    }
                });
                fetcher.IsBackground = true; fetcher.Priority = ThreadPriority.BelowNormal; fetcher.Start();
            }
        }

        public void FreeBanners() { foreach (Bitmap b in banners.Values) if (b != null) b.Dispose(); banners.Clear(); }

        Bitmap Banner(Game g, Size s)
        {
            if (s != bannerSize) { FreeBanners(); bannerSize = s; }
            Bitmap b;
            if (banners.TryGetValue(g.Name, out b)) return b;
            bool tried; string f = g.Exe == null ? null : GameArt.BannerFile(g.Exe, out tried);
            Bitmap src = f != null ? Library.LoadImage(f) : null;
            if (src != null)
            {
                b = new Bitmap(s.Width, s.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (Graphics gr = Graphics.FromImage(b))
                {
                    gr.InterpolationMode = InterpolationMode.HighQualityBicubic; gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float sc = Math.Max(s.Width / (float)src.Width, s.Height / (float)src.Height);
                    gr.DrawImage(src, (s.Width - src.Width * sc) / 2, (s.Height - src.Height * sc) / 2, src.Width * sc, src.Height * sc);
                }
                src.Dispose();
            }
            banners[g.Name] = b;
            return b;
        }

        void Measure()
        {
            gap = S(18);
            int avail = Width - S(14);
            cols = Math.Max(1, (avail + gap) / (S(300) + gap));
            tileW = (avail - gap * (cols - 1)) / cols; tileH = bannerH = tileW * 9 / 16;
        }
        int ContentH { get { Measure(); return (games.Count + cols - 1) / cols * (tileH + gap) + S(20); } }
        void Clamp() { scroll = Math.Max(0, Math.Min(scroll, ContentH - Height)); }
        Rectangle TileRect(int i) { return new Rectangle(S(4) + i % cols * (tileW + gap), S(2) + i / cols * (tileH + gap) - scroll, tileW, tileH); }
        int HitTest(Point p) { Measure(); for (int i = 0; i < games.Count; i++) if (TileRect(i).Contains(p)) return i; return -1; }

        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); scroll -= e.Delta * S(100) / 120; Clamp(); hot = HitTest(PointToClient(Cursor.Position)); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = HitTest(e.Location); if (h != hot) { hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Clamp(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = HitTest(e.Location);
            if (i < 0 || e.Button != MouseButtons.Left) return;
            main.Clips.ShowGame(games[i].Name); main.Go(main.Clips);
        }

        static string Footage(double s)
        {
            int t = (int)Math.Round(s);
            return t < 60 ? t + " s" : t < 3600 ? (t / 60) + " min" : (t / 3600) + " h " + (t % 3600 / 60) + " min";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e); if (Parent != null) Backdrop.Draw(g, Parent.ClientSize, new Point(-Left, -Top)); Measure();
            if (games.Count == 0) { Empty(g); return; }
            for (int i = 0; i < games.Count; i++)
            {
                Rectangle r = TileRect(i);
                if (r.Bottom < 0) continue; if (r.Top > Height) break;
                DrawTile(g, games[i], r, i == hot);
            }
        }

        void DrawTile(Graphics g, Game gm, Rectangle r, bool h)
        {
            float rad = S(14);
            Bitmap b = Banner(gm, r.Size);
            using (GraphicsPath p = Ui.Round(r, rad))
            {
                if (b != null) using (TextureBrush tb = new TextureBrush(b, WrapMode.Clamp)) { tb.TranslateTransform(r.X, r.Y); g.FillPath(tb, p); }
                else
                {
                    int hash = 0; foreach (char ch in gm.Name) hash = unchecked(hash * 31 + ch);
                    Color hue = Ui.FromHue((hash & 0x7fffffff) % 360);
                    using (LinearGradientBrush lg = new LinearGradientBrush(r, Ui.Mix(hue, C.Card, 0.35f), Ui.Mix(hue, C.Card, 0.85f), 35f)) g.FillPath(lg, p);
                    Bitmap icon = GameIcons.Get(gm.Name);
                    if (icon != null)
                    {
                        int s = (int)(r.Height * 1.05);
                        using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
                        {
                            ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.16f });
                            Region before = g.Clip; g.SetClip(p, CombineMode.Intersect); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(icon, new Rectangle(r.Right - s + S(20), r.Y - S(20), s, s), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                            g.Clip = before;
                        }
                    }
                }
                Region keep = g.Clip; g.SetClip(p, CombineMode.Intersect);
                int botH = Math.Min(S(110), r.Height * 3 / 5);
                using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(r.X, r.Bottom - botH - 1, r.Width, botH + 2), Color.FromArgb(0, 8, 7, 14), Color.FromArgb(225, 8, 7, 14), 90f))
                    g.FillRectangle(lg, r.X, r.Bottom - botH, r.Width, botH);
                if (h) using (SolidBrush dark = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(dark, r);
                g.Clip = keep;
            }
            Ui.StrokeRound(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), rad, Color.FromArgb(h ? 60 : 26, 255, 255, 255), 1);

            if (gm.AutoStart)
            {
                string badge = "Auto-start"; int bw = Ui.Measure(badge, Ui.F(8.5f, 5)).Width + S(22);
                Rectangle pill = new Rectangle(r.X + S(12), r.Y + S(12), bw, S(26));
                Ui.FillRound(g, pill, S(13), Color.FromArgb(200, 14, 12, 22));
                Ui.TextCenter(g, badge, Ui.F(8.5f, 5), pill, C.AccentHi);
            }
            if (h)
            {
                string v = "View clips"; int vw = Ui.Measure(v, Ui.F(9, 5)).Width + S(40);
                Rectangle vp = new Rectangle(r.Right - S(12) - vw, r.Y + S(12), vw, S(30));
                Ui.FillRound(g, vp, S(15), C.Accent);
                Ui.TextLeft(g, v, Ui.F(9, 5), new Rectangle(vp.X + S(14), vp.Y, vw, vp.Height), Color.White);
                Ui.DrawIcon(g, "", 8, new Rectangle(vp.Right - S(24), vp.Y, S(16), vp.Height), Color.White);
            }

            Rectangle ic = new Rectangle(r.X + S(16), r.Bottom - S(16) - S(44), S(44), S(44));
            Bitmap gi = GameIcons.Get(gm.Name);
            if (gi != null) { Ui.FillRound(g, ic, S(10), Color.FromArgb(160, 14, 12, 22)); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(gi, Rectangle.Inflate(ic, -S(6), -S(6))); }
            else ClipGrid.GameBadge(g, gm.Name, ic);
            int tx = ic.Right + S(14), tw = r.Right - tx - S(16);
            Ui.TextLeft(g, gm.Name, Ui.F(13, 4), new Rectangle(tx, ic.Y - S(2), tw, S(26)), Color.White);
            string sub = gm.Clips == 0 ? (gm.AutoStart ? "Recording starts when it opens" : "No clips yet")
                : (gm.Clips == 1 ? "1 clip" : gm.Clips + " clips") + "  ·  " + Footage(gm.Seconds) + "  ·  " + Library.NiceDate(gm.Last);
            Ui.TextLeft(g, sub, Ui.F(9, 0), new Rectangle(tx, ic.Y + S(24), tw, S(20)), Color.FromArgb(200, 255, 255, 255));
        }

        void Empty(Graphics g)
        {
            int w = S(460), x = (Width - w) / 2, y = Math.Max(S(30), Height / 2 - S(150));
            Rectangle ic = new Rectangle((Width - S(64)) / 2, y + S(10), S(64), S(64));
            RailIcons.Draw(g, "games", ic, C.Text, BackColor);
            Ui.TextCenter(g, "No games yet", Ui.F(15, 4), new Rectangle(x, ic.Bottom + S(18), w, S(32)), C.Text);
            TextRenderer.DrawText(g, "Clips you save are grouped by game here. Add your games under Auto-start settings to record only while they run.",
                Ui.F(10, 0), new Rectangle(x, ic.Bottom + S(56), w, S(60)), C.Sub, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
