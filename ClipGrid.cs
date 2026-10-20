using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Clutch
{
    class ClipGrid : Ctl
    {
        class Cell { public bool Header; public string Title, Count; public int Index; public Rectangle R; }
        readonly MainForm main;
        List<Clip> list = new List<Clip>(); bool filtered, favMode, grouped;
        readonly List<Cell> cells = new List<Cell>();
        int layoutW = -1; float layoutScale; int contentH;
        int scroll, hot = -1, hotPart;
        int thumbH, cardH, anchor = -1;
        public readonly HashSet<Clip> Selected = new HashSet<Clip>();
        public event Action SelectionChanged;
        readonly Dictionary<Clip, KeyValuePair<Size, Bitmap>> scaled = new Dictionary<Clip, KeyValuePair<Size, Bitmap>>();
        readonly HashSet<Clip> drawn = new HashSet<Clip>();
        bool dragBar; int dragY, dragScroll;

        readonly Btn emptyImport = new Btn("Import videos", Glyph.Film, BtnStyle.Standard), emptyMedal = new Btn("Import from Medal", Glyph.Import, BtnStyle.Standard);

        public ClipGrid(MainForm m)
        {
            main = m;
            emptyImport.Click += delegate { main.Clips.Import(); };
            emptyMedal.Click += delegate { MedalImport.Start(main); };
            emptyImport.Visible = emptyMedal.Visible = false;
            Controls.Add(emptyImport); Controls.Add(emptyMedal);
        }

        bool Fresh { get { return list.Count == 0 && !filtered && !favMode; } }

        void PlaceEmpty()
        {
            bool show = Fresh;
            emptyImport.Visible = emptyMedal.Visible = show;
            if (!show) return;
            int top = EmptyTop(), h = S(36);
            emptyImport.Height = emptyMedal.Height = h;
            emptyImport.Width = emptyImport.PreferredWidth() + S(8); emptyMedal.Width = emptyMedal.PreferredWidth() + S(8);
            int w = emptyImport.Width + S(10) + emptyMedal.Width, x = (Width - w) / 2, y = top + S(372);
            emptyImport.Location = new Point(x, y); emptyMedal.Location = new Point(emptyImport.Right + S(10), y);
        }

        int EmptyTop() { return Math.Max(S(16), (Height - S(420)) / 2 - S(10)); }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); PlaceEmpty(); }

        public List<Clip> Items { get { return list; } }
#if DEV
        public void DemoHover(int i, int part) { hot = i; hotPart = part; Invalidate(); }
#endif

        public void Set(List<Clip> l, bool isFiltered, bool favourites, bool byDate)
        {
            list = l; filtered = isFiltered; favMode = favourites; grouped = byDate; hot = -1;
            Selected.IntersectWith(l);
            layoutW = -1; Clamp(); PlaceEmpty(); Invalidate();
        }

        static string Group(DateTime d)
        {
            DateTime t = DateTime.Today;
            if (d >= t) return "Today";
            if (d >= t.AddDays(-1)) return "Yesterday";
            if (d >= t.AddDays(-(((int)t.DayOfWeek + 6) % 7))) return "Earlier this week";
            if (d.Year == t.Year && d.Month == t.Month) return "Earlier this month";
            return d.ToString("MMMM yyyy");
        }

        void Build()
        {
            if (layoutW == Width && layoutScale == Ui.Scale) return;
            layoutW = Width; layoutScale = Ui.Scale; cells.Clear();
            int gap = S(18), avail = Width - S(18), cols, cardW;
            cols = Math.Max(1, (avail + gap) / (S(260) + gap)); cardW = (avail - gap * (cols - 1)) / cols; thumbH = cardW * 9 / 16; cardH = thumbH;
            int y = S(2), col = 0; string group = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (grouped)
                {
                    string gname = Group(list[i].Created);
                    if (gname != group)
                    {
                        if (col > 0) { y += cardH + gap; col = 0; }
                        group = gname; int n = 0;
                        for (int k = i; k < list.Count && Group(list[k].Created) == gname; k++) n++;
                        Cell h = new Cell(); h.Header = true; h.Title = gname; h.Count = n == 1 ? "1 clip" : n + " clips";
                        h.R = new Rectangle(S(4), y, avail, S(32)); cells.Add(h); y += S(40);
                    }
                }
                Cell c = new Cell(); c.Index = i; c.R = new Rectangle(S(4) + col * (cardW + gap), y, cardW, cardH); cells.Add(c);
                if (++col == cols) { col = 0; y += cardH + gap; }
            }
            if (col > 0) y += cardH + gap;
            contentH = y + S(16);
        }

        int ContentH { get { Build(); return contentH; } }
        void Clamp() { scroll = Math.Max(0, Math.Min(scroll, ContentH - Height)); }
        Rectangle OnScreen(Rectangle r) { r.Y -= scroll; return r; }

        Rectangle GameRect(Rectangle r) { return new Rectangle(r.X + S(12), r.Y + S(12), S(30), S(30)); }
        Rectangle StarRect(Rectangle r) { return new Rectangle(r.Right - S(88), r.Y + S(12), S(34), S(30)); }
        Rectangle SelectRect(Rectangle r) { return new Rectangle(r.X + S(14), r.Y + S(14), S(26), S(26)); }
        Rectangle MoreRect(Rectangle r) { return new Rectangle(r.Right - S(50), r.Y + S(12), S(38), S(30)); }

        int HitTest(Point p, out int part)
        {
            part = 0; Build();
            foreach (Cell c in cells)
            {
                if (c.Header) continue;
                Rectangle r = OnScreen(c.R);
                if (r.Bottom < 0 || r.Top > Height || !r.Contains(p)) continue;
                part = StarRect(r).Contains(p) ? 1 : SelectRect(r).Contains(p) ? 5 : MoreRect(r).Contains(p) ? 2 : 0;
                return c.Index;
            }
            return -1;
        }

        Rectangle BarRect()
        {
            int ch = ContentH; if (ch <= Height) return Rectangle.Empty;
            int h = Math.Max(S(40), Height * Height / ch), y = (int)((Height - h) * (scroll / (float)(ch - Height)));
            return new Rectangle(Width - S(7), y, S(5), h);
        }

        void UpdateHot(Point p)
        {
            int part, h = HitTest(p, out part);
            if (h != hot || part != hotPart) { hot = h; hotPart = part; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); scroll -= e.Delta * S(120) / 120; Clamp(); hot = -2; UpdateHot(PointToClient(Cursor.Position)); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragBar) { int ch = ContentH; Rectangle b = BarRect(); scroll = dragScroll + (int)((e.Y - dragY) * (ch - Height) / (float)Math.Max(1, Height - b.Height)); Clamp(); Invalidate(); return; }
            UpdateHot(e.Location);
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            Rectangle b = BarRect();
            if (e.Button == MouseButtons.Left && !b.IsEmpty && e.X >= Width - S(12)) { dragBar = true; dragY = e.Y; dragScroll = scroll; Capture = true; }
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (dragBar) { dragBar = false; Capture = false; } }
        protected override void OnResize(EventArgs e) { base.OnResize(e); layoutW = -1; Clamp(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int part, i = HitTest(e.Location, out part);
            if (i < 0) return;
            Clip c = list[i];
            if (e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && part == 2 && Selected.Count == 0)) { ShowMenu(c, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            bool ctrl = (ModifierKeys & Keys.Control) != 0, shift = (ModifierKeys & Keys.Shift) != 0;
            if (shift && anchor >= 0 && anchor < list.Count) { SelectRange(anchor, i); return; }
            if (ctrl || part == 5 || Selected.Count > 0) { Toggle(c); anchor = i; return; }
            switch (part)
            {
                case 1: main.ToggleFavorite(c); break;
                case 3: main.CopyClips(new List<Clip> { c }); break;
                case 4: main.TrimClip(c); break;
                default: main.OpenClip(c); break;
            }
        }

        void Toggle(Clip c) { if (!Selected.Remove(c)) Selected.Add(c); Changed(); }
        void SelectRange(int a, int b) { for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) Selected.Add(list[i]); Changed(); }
        public void SelectAll() { Selected.UnionWith(list); Changed(); }
        public void ClearSelection() { if (Selected.Count == 0) return; Selected.Clear(); anchor = -1; Changed(); }
        void Changed() { Invalidate(); if (SelectionChanged != null) SelectionChanged(); }

        void ShowMenu(Clip c, Point at)
        {
            ContextMenuStrip m = Menus.Make();
            Menus.Item(m, "Play", Glyph.Play, delegate { main.OpenClip(c); });
            Menus.Item(m, "Trim and export", Glyph.Cut, delegate { main.TrimClip(c); });
            Menus.Item(m, "Copy file", Glyph.Copy, delegate { main.CopyClips(new List<Clip> { c }); });
            Menus.Item(m, "Fit for Discord\u2026", Glyph.Discord, delegate { DiscordFit.Menu(main, c, this, at); });
            Menus.Item(m, c.Favorite ? "Remove from favourites" : "Add to favourites", c.Favorite ? Glyph.StarFill : Glyph.Star, delegate { main.ToggleFavorite(c); });
            Menus.Item(m, "Rename", Glyph.Rename, delegate { main.RenameClip(c); });
            Menus.Item(m, "Change game\u2026", Glyph.Game, delegate { main.ChangeGame(c); });
            Menus.Item(m, "Select", Glyph.Check, delegate { Toggle(c); anchor = list.IndexOf(c); });
            Menus.Item(m, "Show in folder", Glyph.Folder, delegate { main.ShowInFolder(c); });
            m.Items.Add(new ToolStripSeparator());
            Menus.Item(m, "Delete", Glyph.Delete, delegate { main.DeleteClips(new List<Clip> { c }); });
            m.Show(this, at);
        }

        Bitmap Scaled(Clip c, Size s)
        {
            if ((c.Thumb == null && !c.ThumbReady) || s.Width < 1 || s.Height < 1) return null;
            drawn.Add(c);
            KeyValuePair<Size, Bitmap> kv;
            if (scaled.TryGetValue(c, out kv) && kv.Key == s) return kv.Value;
            if (kv.Value != null) kv.Value.Dispose();
            Bitmap src = c.Thumb ?? Library.LoadImage(Library.ThumbPath(c));
            if (src == null) { c.ThumbReady = false; return null; }
            Bitmap b = new Bitmap(s.Width, s.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float sc = Math.Max(s.Width / (float)src.Width, s.Height / (float)src.Height);
                g.DrawImage(src, (s.Width - src.Width * sc) / 2, (s.Height - src.Height * sc) / 2, src.Width * sc, src.Height * sc);
            }
            if (src != c.Thumb) src.Dispose();
            scaled[c] = new KeyValuePair<Size, Bitmap>(s, b);
            return b;
        }

        public void FreeCache(HashSet<Clip> keep)
        {
            List<Clip> gone = new List<Clip>();
            foreach (KeyValuePair<Clip, KeyValuePair<Size, Bitmap>> kv in scaled) if (keep == null || !keep.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (Clip c in gone) { scaled[c].Value.Dispose(); scaled.Remove(c); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e); if (Parent != null) Backdrop.Draw(g, Parent.ClientSize, new Point(-Left, -Top)); Build(); drawn.Clear();
            if (list.Count == 0) { Empty(g); return; }
            foreach (Cell c in cells)
            {
                Rectangle r = OnScreen(c.R);
                if (r.Bottom < 0 || r.Top > Height) continue;
                if (c.Header)
                {
                    Font hf = Ui.F(11.5f, 4); int tw = Ui.Measure(c.Title, hf).Width;
                    Ui.TextLeft(g, c.Title, hf, new Rectangle(r.X + S(2), r.Y, tw + S(4), r.Height), C.Text);
                    Ui.TextLeft(g, c.Count, Ui.F(9, 1), new Rectangle(r.X + tw + S(14), r.Y + S(1), S(140), r.Height), C.Faint);
                }
                else DrawTile(g, list[c.Index], r, c.Index == hot);
            }
            Rectangle bar = BarRect();
            if (!bar.IsEmpty) Ui.FillRound(g, bar, S(2.5f), dragBar ? C.Sub : C.ControlHi);
            if (scaled.Count > drawn.Count + 24) FreeCache(drawn);
        }

        public static void GameBadge(Graphics g, string game, Rectangle r)
        {
            Bitmap icon = GameIcons.Get(game);
            if (icon != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(icon, r); return; }
            int h = 0; foreach (char ch in game) h = unchecked(h * 31 + ch);
            Ui.FillRound(g, r, r.Height * 0.25f, Ui.FromHue((h & 0x7fffffff) % 360));
            Ui.TextCenter(g, game.Substring(0, 1).ToUpperInvariant(), Ui.F(Math.Max(7.5f, r.Height / Ui.Scale * 0.45f), 4), r, Color.White);
        }

        static readonly Color Glass = Color.FromArgb(14, 12, 22);

        public void FreeBackdrop() { Backdrop.Free(); }

        void DrawTile(Graphics g, Clip c, Rectangle r, bool isHot)
        {
            float rad = S(14); bool sel = Selected.Contains(c), selMode = Selected.Count > 0;
            Bitmap b = Scaled(c, r.Size);
            using (GraphicsPath p = Ui.Round(r, rad))
            {
                if (b != null) using (TextureBrush tb = new TextureBrush(b, WrapMode.Clamp)) { tb.TranslateTransform(r.X, r.Y); g.FillPath(tb, p); }
                else
                {
                    using (LinearGradientBrush lg = new LinearGradientBrush(r, C.Control, C.Panel, 50f)) g.FillPath(lg, p);
                    int d = Math.Min(S(36), r.Height / 3); RailIcons.Draw(g, "clips", new Rectangle(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d), C.Faint, C.Panel);
                }
                Region before = g.Clip; g.SetClip(p, CombineMode.Intersect);
                int topH = Math.Min(S(70), r.Height / 3), botH = Math.Min(S(96), r.Height / 2);
                using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, topH + 2), Color.FromArgb(isHot ? 150 : 120, 8, 7, 14), Color.FromArgb(0, 8, 7, 14), 90f))
                    g.FillRectangle(lg, r.X, r.Y, r.Width, topH);
                using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(r.X, r.Bottom - botH - 1, r.Width, botH + 2), Color.FromArgb(0, 8, 7, 14), Color.FromArgb(220, 8, 7, 14), 90f))
                    g.FillRectangle(lg, r.X, r.Bottom - botH, r.Width, botH);
                if (isHot && !selMode) using (SolidBrush sb = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillRectangle(sb, r);
                g.Clip = before;
            }
            RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            if (sel) Ui.StrokeRound(g, new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), rad, C.Accent, S(2.5f));
            else Ui.StrokeRound(g, rf, rad, Color.FromArgb(isHot ? 60 : 26, 255, 255, 255), 1);

            if (isHot && !selMode)
            {
                float tw = S(22), th = S(26), cx = r.X + r.Width / 2f + S(2), cy = r.Y + r.Height / 2f - S(6);
                PointF[] tri = { new PointF(cx - tw / 2, cy - th / 2), new PointF(cx - tw / 2, cy + th / 2), new PointF(cx + tw / 2, cy) };
                using (GraphicsPath tp = new GraphicsPath())
                {
                    tp.AddPolygon(tri);
                    using (Pen jp = new Pen(Color.White, S(4))) { jp.LineJoin = LineJoin.Round; g.DrawPath(jp, tp); }
                    using (SolidBrush wb = new SolidBrush(Color.White)) g.FillPath(wb, tp);
                }
            }

            Rectangle gr = GameRect(r);
            bool showGame = !isHot && !selMode;
            if (showGame && c.Game.Length > 0)
            {
                using (GraphicsPath gp = Ui.Round(gr, S(7)))
                {
                    Region before = g.Clip; g.SetClip(gp, CombineMode.Intersect);
                    GameBadge(g, c.Game, gr);
                    g.Clip = before;
                }
            }
            else if (showGame)
            {
                Ui.FillRound(g, gr, S(7), Color.FromArgb(190, Glass));
                RailIcons.Draw(g, "clips", new Rectangle(gr.X + S(7), gr.Y + S(7), gr.Width - S(14), gr.Height - S(14)), Color.FromArgb(220, 255, 255, 255), Color.FromArgb(190, Glass));
            }

            if (!selMode)
            {
                Rectangle sr = StarRect(r);
                if (isHot && hotPart == 1) Ui.FillRound(g, sr, S(8), Color.FromArgb(150, Glass));
                Ui.DrawIcon(g, c.Favorite ? Glyph.StarFill : Glyph.Star, 13, new Rectangle(sr.X, sr.Y + S(1), sr.Width, sr.Height), c.Favorite ? C.Gold : Color.White);
                Rectangle mr = MoreRect(r);
                if (isHot && hotPart == 2) Ui.FillRound(g, mr, S(8), Color.FromArgb(150, Glass));
                using (SolidBrush wb = new SolidBrush(Color.White))
                {
                    int dd = S(6), cy = mr.Y + mr.Height / 2 - dd / 2, sx = mr.X + (mr.Width - (dd * 3 + S(4) * 2)) / 2;
                    for (int i = 0; i < 3; i++) g.FillEllipse(wb, sx + i * (dd + S(4)), cy, dd, dd);
                }
            }
            if (sel || selMode || isHot)
            {
                Rectangle sc = SelectRect(r);
                if (sel) { Ui.FillAccent(g, sc, sc.Width / 2f, 0, 255); Ui.DrawIcon(g, Glyph.Check, 9, sc, Color.White); }
                else
                {
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(isHot && hotPart == 5 ? 220 : 120, Glass))) g.FillEllipse(sb, sc);
                    using (Pen pen = new Pen(Color.FromArgb(220, 255, 255, 255), S(1.5f))) g.DrawEllipse(pen, sc.X + 1, sc.Y + 1, sc.Width - 2, sc.Height - 2);
                }
            }

            string dur = Library.NiceTime(c.Duration); Font df = Ui.F(10, 5);
            int dw = Ui.Measure(dur, df).Width + S(4);
            Rectangle dr = new Rectangle(r.Right - S(16) - dw, r.Bottom - S(42), dw, S(28));
            Ui.TextRight(g, dur, df, dr, Color.White);
            Ui.TextLeft(g, c.Title, Ui.F(12.5f, 4), new Rectangle(r.X + S(16), r.Bottom - S(44), dr.X - S(12) - r.X - S(16), S(30)), Color.White);
        }

        void Empty(Graphics g)
        {
            if (Fresh) { Welcome(g); return; }
            int w = S(460), x = (Width - w) / 2, y = Math.Max(S(30), Height / 2 - S(150));
            Rectangle ic = new Rectangle((Width - S(64)) / 2, y + S(10), S(64), S(64));
            if (filtered && !favMode) Ui.DrawIcon(g, Glyph.Search, 26, ic, C.Text);
            else RailIcons.Draw(g, favMode ? "star" : "clips", ic, C.Text, BackColor);
            string t = favMode ? "No favourites yet" : filtered ? "Nothing matches" : "No clips yet";
            string b = favMode ? "Tap the star on a clip to keep your best moments here."
                : filtered ? "Try a different search or filter."
                : "Clutch is keeping the last " + Settings.Cur.BufferSeconds + " seconds ready. Press " + MainForm.HotkeyName() + " in a game and the clip lands here, instantly.";
            Ui.TextCenter(g, t, Ui.F(15, 4), new Rectangle(x, ic.Bottom + S(18), w, S(32)), C.Text);
            TextRenderer.DrawText(g, b, Ui.F(10, 0), new Rectangle(x, ic.Bottom + S(56), w, S(60)), C.Sub, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }

        void Welcome(Graphics g)
        {
            int top = EmptyTop(), cx = Width / 2;
            Color halo = Ui.Mix(BackColor, C.Accent, 0.08f), inner = Ui.Mix(BackColor, C.Accent, 0.18f);
            using (SolidBrush b = new SolidBrush(halo)) g.FillEllipse(b, cx - S(56), top, S(112), S(112));
            using (SolidBrush b = new SolidBrush(inner)) g.FillEllipse(b, cx - S(40), top + S(16), S(80), S(80));
            RailIcons.Draw(g, "clips", new Rectangle(cx - S(20), top + S(36), S(40), S(40)), C.AccentHi, inner);

            Ui.TextCenter(g, "No clips yet", Ui.F(16, 4), new Rectangle(0, top + S(126), Width, S(34)), C.Text);

            string key = MainForm.HotkeyName(), a = "Press", b2 = "in any game to save the moment.";
            Font f = Ui.F(10.5f, 0), kf = Ui.F(9.5f, 5);
            int aw = Ui.Measure(a, f).Width, bw = Ui.Measure(b2, f).Width, kw = Ui.Measure(key, kf).Width + S(20), gap = S(8);
            int lx = cx - (aw + gap + kw + gap + bw) / 2, ly = top + S(168), lh = S(30);
            Ui.TextLeft(g, a, f, new Rectangle(lx, ly, aw + S(2), lh), C.Sub); lx += aw + gap;
            Rectangle cap = new Rectangle(lx, ly + S(2), kw, lh - S(4));
            Ui.FillRound(g, new RectangleF(cap.X, cap.Y + S(2), cap.Width, cap.Height), S(6), Ui.Mix(C.Control, Color.Black, 0.35f));
            Ui.FillRound(g, cap, S(6), C.ControlHi);
            Ui.StrokeRound(g, new RectangleF(cap.X + 0.5f, cap.Y + 0.5f, cap.Width - 1, cap.Height - 1), S(6), Ui.Mix(C.Stroke, C.Text, 0.12f), 1);
            Ui.TextCenter(g, key, kf, cap, C.Text); lx += kw + gap;
            Ui.TextLeft(g, b2, f, new Rectangle(lx, ly, bw + S(4), lh), C.Sub);

            int secs = Settings.Cur.BufferSeconds;
            string span = secs < 120 ? secs + " seconds" : (secs / 60) + " minutes";
            string[] icons = { Glyph.Game, Glyph.Keyboard, Glyph.Discord };
            string[] heads = { "Play a game", "Press " + key, "Share it" };
            string[] subs = { "Clutch keeps the last " + span + " ready, in memory.", "It saves instantly, with no lag in your game.", "Trim it, or fit it for Discord in one click." };
            int cgap = S(12), cw = Math.Min(S(176), (Width - S(32) - cgap * 2) / 3), ch = S(124);
            int x0 = cx - (cw * 3 + cgap * 2) / 2, cy = top + S(224);
            for (int i = 0; i < 3; i++)
            {
                Rectangle r = new Rectangle(x0 + i * (cw + cgap), cy, cw, ch);
                Ui.FillRound(g, r, S(10), C.Card);
                Ui.StrokeRound(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), S(10), Ui.Mix(C.Card, C.Text, 0.06f), 1);
                Rectangle ib = new Rectangle(r.X + S(14), r.Y + S(14), S(30), S(30));
                Ui.FillRound(g, ib, S(8), Ui.Mix(C.Card, C.Text, 0.07f));
                Ui.DrawIcon(g, icons[i], 11, ib, C.Text);
                Ui.TextCenter(g, (i + 1).ToString(), Ui.F(9, 5), new Rectangle(r.Right - S(34), r.Y + S(14), S(20), S(30)), C.Faint);
                Ui.TextLeft(g, heads[i], Ui.F(10, 4), new Rectangle(r.X + S(14), r.Y + S(52), r.Width - S(24), S(22)), C.Text);
                Ui.Wrap(g, subs[i], Ui.F(9, 0), new Rectangle(r.X + S(14), r.Y + S(76), r.Width - S(24), ch - S(80)), C.Sub);
            }
        }
    }
}
