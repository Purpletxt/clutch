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

        public ClipGrid(MainForm m) { main = m; }

        public List<Clip> Items { get { return list; } }
#if DEV
        public void DemoHover(int i, int part) { hot = i; hotPart = part; Invalidate(); }
#endif

        public void Set(List<Clip> l, bool isFiltered, bool favourites, bool byDate)
        {
            list = l; filtered = isFiltered; favMode = favourites; grouped = byDate; hot = -1;
            Selected.IntersectWith(l);
            layoutW = -1; Clamp(); Invalidate();
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
            int gap = S(16), avail = Width - S(18);
            int cols = Math.Max(1, (avail + gap) / (S(250) + gap));
            int cardW = (avail - gap * (cols - 1)) / cols; thumbH = cardW * 9 / 16; cardH = thumbH + S(64);
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

        Rectangle Thumb(Rectangle r) { return new Rectangle(r.X, r.Y, r.Width, thumbH); }
        Rectangle StarRect(Rectangle r) { return new Rectangle(r.Right - S(40), r.Y + S(8), S(32), S(32)); }
        Rectangle SelectRect(Rectangle r) { return new Rectangle(r.X + S(8), r.Y + S(8), S(26), S(26)); }
        Rectangle CopyRect(Rectangle r) { return new Rectangle(r.X + S(8), r.Y + thumbH - S(38), Ui.Measure("Copy", Ui.F(9, 5)).Width + S(38), S(30)); }
        Rectangle TrimRect(Rectangle r) { Rectangle c = CopyRect(r); return new Rectangle(c.Right + S(6), c.Y, S(30), S(30)); }
        Rectangle MoreRect(Rectangle r) { Rectangle t = TrimRect(r); return new Rectangle(t.Right + S(6), t.Y, S(30), S(30)); }

        int HitTest(Point p, out int part)
        {
            part = 0; Build();
            foreach (Cell c in cells)
            {
                if (c.Header) continue;
                Rectangle r = OnScreen(c.R);
                if (r.Bottom < 0 || r.Top > Height || !r.Contains(p)) continue;
                part = StarRect(r).Contains(p) ? 1 : SelectRect(r).Contains(p) ? 5 : MoreRect(r).Contains(p) ? 2
                     : CopyRect(r).Contains(p) ? 3 : TrimRect(r).Contains(p) ? 4 : Thumb(r).Contains(p) ? 0 : 6;
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
            Menus.Item(m, "Copy file (for Discord)", Glyph.Copy, delegate { main.CopyClips(new List<Clip> { c }); });
            Menus.Item(m, c.Favorite ? "Remove from favourites" : "Add to favourites", c.Favorite ? Glyph.StarFill : Glyph.Star, delegate { main.ToggleFavorite(c); });
            Menus.Item(m, "Rename", Glyph.Rename, delegate { main.RenameClip(c); });
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
            Graphics g = Begin(e); Build(); drawn.Clear();
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
                else DrawCard(g, list[c.Index], r, c.Index == hot);
            }
            Rectangle bar = BarRect();
            if (!bar.IsEmpty) Ui.FillRound(g, bar, S(2.5f), dragBar ? C.Sub : C.ControlHi);
            if (scaled.Count > drawn.Count + 24) FreeCache(drawn);
        }

        static GraphicsPath TopRound(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath(); float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddLine(r.Right, r.Bottom, r.X, r.Bottom); p.CloseFigure(); return p;
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

        void Pill(Graphics g, Rectangle r, string icon, string text, bool hover)
        {
            Ui.FillRound(g, r, r.Height / 2f, Color.FromArgb(hover ? 240 : 185, Glass));
            if (text == null) { Ui.DrawIcon(g, icon, 10, r, Color.White); return; }
            Ui.DrawIcon(g, icon, 9.5f, new Rectangle(r.X + S(9), r.Y, S(18), r.Height), Color.White);
            Ui.TextLeft(g, text, Ui.F(9, 5), new Rectangle(r.X + S(30), r.Y, r.Width - S(30), r.Height - S(1)), Color.White);
        }

        void DrawCard(Graphics g, Clip c, Rectangle r, bool isHot)
        {
            float rad = S(10); bool sel = Selected.Contains(c), selMode = Selected.Count > 0;
            Ui.FillRound(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), rad, isHot || sel ? C.CardHi : C.Card);
            Rectangle tr = Thumb(r);
            Bitmap b = Scaled(c, tr.Size);
            using (GraphicsPath p = TopRound(tr, rad))
            {
                if (b != null) using (TextureBrush tb = new TextureBrush(b, WrapMode.Clamp)) { tb.TranslateTransform(tr.X, tr.Y); g.FillPath(tb, p); }
                else
                {
                    using (LinearGradientBrush lg = new LinearGradientBrush(tr, C.Control, C.Panel, 50f)) g.FillPath(lg, p);
                    int d = S(30); RailIcons.Draw(g, "clips", new Rectangle(tr.X + (tr.Width - d) / 2, tr.Y + (tr.Height - d) / 2, d, d), C.Faint, C.Panel);
                }
                if (isHot && !selMode)
                {
                    Rectangle shade = new Rectangle(tr.X, tr.Bottom - S(64), tr.Width, S(64));
                    using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(shade.X, shade.Y - 1, shade.Width, shade.Height + 1), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(150, 0, 0, 0), 90f))
                        g.FillRectangle(lg, shade);
                    if (hotPart == 0)
                    {
                        int d = S(46); Rectangle pc = new Rectangle(tr.X + (tr.Width - d) / 2, tr.Y + (tr.Height - d) / 2 - S(8), d, d);
                        Ui.FillAccent(g, pc, d / 2f, 0, 235);
                        Ui.DrawIcon(g, Glyph.Play, 14, new Rectangle(pc.X + S(2), pc.Y, d, d), Color.White);
                    }
                }
            }
            if (sel) Ui.StrokeRound(g, new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), rad, C.Accent, S(2));
            string dur = Library.NiceTime(c.Duration); Font bf = Ui.F(8.5f, 5); int bw = Ui.Measure(dur, bf).Width + S(14);
            Rectangle badge = new Rectangle(tr.Right - bw - S(8), tr.Bottom - S(30), bw, S(22));
            Ui.FillRound(g, badge, S(6), Color.FromArgb(200, Glass));
            Ui.TextCenter(g, dur, bf, badge, Color.White);
            if (isHot && !selMode)
            {
                Pill(g, CopyRect(r), Glyph.Copy, "Copy", hotPart == 3);
                Pill(g, TrimRect(r), Glyph.Cut, null, hotPart == 4);
                Pill(g, MoreRect(r), Glyph.More, null, hotPart == 2);
            }
            if ((c.Favorite || isHot) && !selMode)
            {
                Rectangle sr = StarRect(r);
                Ui.FillRound(g, sr, sr.Width / 2f, Color.FromArgb(isHot && hotPart == 1 ? 240 : 185, Glass));
                Ui.DrawIcon(g, c.Favorite ? Glyph.StarFill : Glyph.Star, 10.5f, new Rectangle(sr.X, sr.Y + S(1), sr.Width, sr.Height), c.Favorite ? C.Gold : Color.White);
            }
            if (sel || selMode || isHot)
            {
                Rectangle sc = SelectRect(r);
                if (sel) { Ui.FillAccent(g, sc, sc.Width / 2f, 0, 255); Ui.DrawIcon(g, Glyph.Check, 9, sc, Color.White); }
                else
                {
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(isHot && hotPart == 5 ? 220 : 140, Glass))) g.FillEllipse(sb, sc);
                    using (Pen pen = new Pen(Color.FromArgb(220, 255, 255, 255), S(1.5f))) g.DrawEllipse(pen, sc.X + 1, sc.Y + 1, sc.Width - 2, sc.Height - 2);
                }
            }
            int ty = r.Y + thumbH + S(10);
            Ui.TextLeft(g, c.Title, Ui.F(10.5f, 4), new Rectangle(r.X + S(12), ty, r.Width - S(24), S(22)), C.Text);
            int mx = r.X + S(12), my = ty + S(25);
            if (c.Game.Length > 0) { GameBadge(g, c.Game, new Rectangle(mx, my + S(1), S(16), S(16))); mx += S(22); }
            string sub = (c.Game.Length > 0 ? c.Game + "  \u00B7  " : "") + Library.NiceSize(c.Size) + "  \u00B7  " + Library.NiceDate(c.Created);
            Ui.TextLeft(g, sub, Ui.F(8.5f, 0), new Rectangle(mx, my, r.Right - mx - S(10), S(18)), C.Sub);
        }

        void Empty(Graphics g)
        {
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
    }
}
