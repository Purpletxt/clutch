using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Clutch
{
    class ClipsPage : Page
    {
        readonly Btn import = new Btn("Import", Glyph.Import, BtnStyle.Standard);
        readonly Lbl title = new Lbl("Clips", 24, 4, C.Text), count = new Lbl("", 10.5f, 1, C.Faint);
        readonly Drop game = new Drop(), sort = new Drop();
        bool favOnly;
        readonly SetupBanner banner;
        readonly ClipGrid grid;
        readonly GameChips chips = new GameChips();
        readonly Btn selFav = new Btn("Favourite", Glyph.Star, BtnStyle.Standard), selCopy = new Btn("Copy", Glyph.Copy, BtnStyle.Standard),
                     selDelete = new Btn("Delete", Glyph.Delete, BtnStyle.Danger), selClear = new Btn("Cancel", Glyph.Close, BtnStyle.Subtle);
        List<string> gameNames = new List<string>();
        static readonly string[] Sorts = { "Newest first", "Oldest first", "Longest first", "Biggest first", "Name A\u2013Z" };
        int lineY;
        readonly Btn fold = new Btn("", Glyph.Up, BtnStyle.Subtle);
        bool Folded { get { return favOnly ? Settings.Cur.FoldFavourites : Settings.Cur.FoldClips; } }

        public ClipsPage(MainForm m) : base(m)
        {
            fold.IconPt = 9; fold.Tip = "Show or hide the header";
            fold.Click += delegate { if (favOnly) Settings.Cur.FoldFavourites = !Settings.Cur.FoldFavourites; else Settings.Cur.FoldClips = !Settings.Cur.FoldClips; Settings.Save(); Relayout(); };
            import.Click += delegate { ImportMenu(import, new Point(0, import.Height + S(4))); };
            chips.Picked += delegate (string n) { game.Index = n == null ? 0 : gameNames.IndexOf(n) + 1; Refresh(); };
            chips.AddGame += delegate { Main.Go(Main.SettingsP); Main.SettingsP.ScrollTo("games"); };
            game.Changed += delegate { Refresh(); };
            sort.Items.AddRange(Sorts); sort.Index = 0; sort.Changed += delegate { Refresh(); };
            sort.IconAt = delegate (int i) { return "\uE8CB"; }; sort.Width = S(180);
            game.Width = S(170);
            banner = new SetupBanner(m);
            grid = new ClipGrid(m);
            grid.SelectionChanged += delegate { Relayout(); };
            selFav.Click += delegate { List<Clip> l = Sel(); bool all = l.TrueForAll(delegate (Clip c) { return c.Favorite; }); foreach (Clip c in l) c.Favorite = !all; Library.Save(); grid.ClearSelection(); Refresh(); };
            selCopy.Click += delegate { Main.CopyClips(Sel()); };
            selDelete.Click += delegate { List<Clip> l = Sel(); grid.ClearSelection(); Main.DeleteClips(l); };
            selClear.Click += delegate { grid.ClearSelection(); };
            foreach (Control c in new Control[] { title, count, fold, selFav, selCopy, selDelete, selClear, chips, banner, grid }) Controls.Add(c);
        }

        public Control[] Tools
        {
            get
            {
                game.Visible = false;
                return Settings.Cur.GameChips ? new Control[] { sort, import } : new Control[] { game, sort, import };
            }
        }

        public bool FavouritesOnly { get { return favOnly; } }
        List<Clip> Sel() { return new List<Clip>(grid.Selected); }
        public void SelectAll() { grid.SelectAll(); }
#if DEV
        public void DemoHover(int i, int part) { grid.DemoHover(i, part); }
#endif
        public bool ClearSelection() { bool had = grid.Selected.Count > 0; grid.ClearSelection(); return had; }
        public void DeleteSelected() { if (grid.Selected.Count == 0) return; List<Clip> l = Sel(); grid.ClearSelection(); Main.DeleteClips(l); }

        public Clip Neighbor(Clip c, int dir)
        {
            List<Clip> l = grid.Items.Contains(c) ? grid.Items : Library.Clips;
            int i = l.IndexOf(c) + dir;
            return i >= 0 && i < l.Count ? l[i] : null;
        }

        public void ShowGame(string name)
        {
            favOnly = false; Main.Bar.ClearSearch();
            Refresh();
            int i = gameNames.IndexOf(name); game.Index = i >= 0 ? i + 1 : 0;
            Refresh();
        }
        public void ShowFavourites(bool on) { favOnly = on; Refresh(); }
        public void LayoutChanged() { if (Main.Current == this) Main.Bar.SetTools(Tools); Refresh(); }

        protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.Draw(e.Graphics, ClientSize, Point.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (lineY > 0) using (Pen p = new Pen(Ui.Mix(C.Bg, C.Text, 0.07f))) e.Graphics.DrawLine(p, S(24), lineY, ClientSize.Width - S(24), lineY);
        }

        public override void Relayout()
        {
            int pad = S(24), w = ClientSize.Width, y = S(10), h = S(36);
            int right = w - pad;
            bool selecting = grid.Selected.Count > 0;
            foreach (Control c in new Control[] { selFav, selCopy, selDelete, selClear }) c.Visible = selecting;
            if (selecting)
            {
                foreach (Btn b in new[] { selClear, selDelete, selCopy, selFav }) { b.Height = h; b.Width = b.PreferredWidth(); b.Location = new Point(right - b.Width, y + S(6)); right = b.Left - S(8); }
                title.Text = grid.Selected.Count + " selected";
            }
            else title.Text = favOnly ? "Favourites" : "Clips";
            bool folded = Folded;
            float tp = folded ? 15 : 24; title.Pt = tp; count.Pt = folded ? 9.5f : 10.5f;
            int tw = Ui.Measure(title.Text, Ui.F(tp, 4)).Width + S(4), th = folded ? S(32) : S(48);
            title.SetBounds(pad - S(2), y, tw, th);
            int cw = Ui.Measure(count.Text, Ui.F(count.Pt, 1)).Width + S(6);
            count.SetBounds(title.Right + S(10), y + (folded ? S(2) : S(8)), cw, th - (folded ? S(2) : S(8)));
            count.Visible = !selecting;
            fold.Visible = !selecting; fold.Icon = folded ? Glyph.Down : Glyph.Up;
            fold.SetBounds((count.Visible ? count.Right : title.Right) + S(6), y + (th - S(30)) / 2 + (folded ? 0 : S(3)), S(30), S(30)); fold.Invalidate();
            y += th + (folded ? S(6) : S(14));
            chips.Visible = !folded && Settings.Cur.GameChips && gameNames.Count > 0;
            if (chips.Visible) { chips.SetBounds(pad, y, w - pad * 2, S(40)); y += S(40) + S(18); }
            lineY = y; y += S(18);
            bool setup = !FFmpeg.Ready; banner.Visible = setup;
            if (setup) { banner.SetBounds(pad, y, w - pad * 2, S(118)); y += S(118) + S(16); }
            grid.SetBounds(pad - S(4), y, w - pad * 2 + S(8), ClientSize.Height - y);
            Invalidate();
        }

        public new void Refresh()
        {
            List<string> names = new List<string>();
            foreach (Clip c in Library.Clips) if (c.Game.Length > 0 && !names.Contains(c.Game)) names.Add(c.Game);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            string chosen = game.Index > 0 && game.Index <= gameNames.Count ? gameNames[game.Index - 1] : null;
            gameNames = names;
            game.Items.Clear(); game.Items.Add("All games"); game.Items.AddRange(names);
            game.Index = chosen != null && names.Contains(chosen) ? names.IndexOf(chosen) + 1 : 0;
            Dictionary<string, int> per = new Dictionary<string, int>();
            foreach (Clip c in Library.Clips) if (c.Game.Length > 0) { int k; per.TryGetValue(c.Game, out k); per[c.Game] = k + 1; }
            List<string> byCount = new List<string>(names);
            byCount.Sort(delegate (string a, string b) { int d = per[b].CompareTo(per[a]); return d != 0 ? d : string.Compare(a, b, StringComparison.OrdinalIgnoreCase); });
            List<int> counts = new List<int>(); foreach (string gn in byCount) counts.Add(per[gn]);
            chips.Set(byCount, counts, Library.Clips.Count, game.Index > 0 ? names[game.Index - 1] : null);

            string q = Main.Bar.Query;
            List<Clip> list = new List<Clip>();
            foreach (Clip c in Library.Clips)
            {
                if (favOnly && !c.Favorite) continue;
                if (game.Index > 0 && c.Game != gameNames[game.Index - 1]) continue;
                if (q.Length > 0 && c.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && c.Game.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(c);
            }
            switch (sort.Index)
            {
                case 1: list.Sort(delegate (Clip a, Clip b) { return a.Created.CompareTo(b.Created); }); break;
                case 2: list.Sort(delegate (Clip a, Clip b) { return b.Duration.CompareTo(a.Duration); }); break;
                case 3: list.Sort(delegate (Clip a, Clip b) { return b.Size.CompareTo(a.Size); }); break;
                case 4: list.Sort(delegate (Clip a, Clip b) { return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase); }); break;
            }
            grid.Set(list, q.Length > 0 || favOnly || game.Index > 0, favOnly, sort.Index <= 1);
            int n = list.Count;
            count.Text = n == 1 ? "1 clip" : n + " clips";
            Relayout();
            banner.Invalidate(true); banner.Sync();
            game.Invalidate(); count.Invalidate();
        }

        public override void Entered() { Refresh(); }
        public void FreeMemory() { grid.FreeCache(null); grid.FreeBackdrop(); }

        public void ImportMenu(Control anchor, Point at)
        {
            ContextMenuStrip m = Menus.Make();
            Menus.Item(m, "Video files\u2026", Glyph.Film, delegate { Import(); });
            Menus.Item(m, "From Medal\u2026", Glyph.Import, delegate { MedalImport.Start(Main); });
            m.Show(anchor, at);
        }

        public void Import()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "MP4 videos (*.mp4)|*.mp4"; d.Multiselect = true; d.Title = "Import clips into Clutch";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                int n = 0;
                try
                {
                    Directory.CreateDirectory(Settings.Cur.Folder);
                    foreach (string f in d.FileNames)
                    {
                        string same = Path.Combine(Settings.Cur.Folder, Path.GetFileName(f));
                        if (string.Equals(Path.GetFullPath(f), Path.GetFullPath(same), StringComparison.OrdinalIgnoreCase)) continue;
                        File.Copy(f, Library.UniquePath(same)); n++;
                    }
                }
                catch (Exception ex) { Main.Toast.Show(Glyph.Warning, "Couldn't import: " + ex.Message, null, null, 5000); }
                Library.Rescan();
                if (n > 0) Main.Toast.Show(Glyph.Check, n == 1 ? "Imported 1 clip." : "Imported " + n + " clips.", null, null, 3000);
            }
        }
    }

    class SetupBanner : Ctl
    {
        readonly MainForm main; readonly Btn go, cancel;
        public SetupBanner(MainForm m)
        {
            main = m;
            go = new Btn("Download FFmpeg", Glyph.Download, BtnStyle.Accent);
            go.Click += delegate { main.StartDownload(); Sync(); };
            cancel = new Btn("Cancel", "", BtnStyle.Standard);
            cancel.Click += delegate { main.CancelDownload(); };
            Controls.Add(go); Controls.Add(cancel);
        }
        public void Sync() { go.Visible = !main.Downloading; cancel.Visible = main.Downloading; PerformLayout(); Invalidate(); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (go == null || cancel == null) return;
            go.Height = cancel.Height = S(36);
            go.Width = go.PreferredWidth() + S(8); cancel.Width = cancel.PreferredWidth() + S(12);
            go.Location = new Point(Width - go.Width - S(22), (Height - go.Height) / 2);
            cancel.Location = new Point(Width - cancel.Width - S(22), (Height - cancel.Height) / 2);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.FillRound(g, r, S(10), C.Card);
            Ui.StrokeRound(g, r, S(10), Ui.Mix(C.Stroke, C.Accent, 0.25f), 1);
            Rectangle ic = new Rectangle(S(22), (Height - S(48)) / 2, S(48), S(48));
            Ui.DrawIcon(g, Glyph.Download, 20, ic, C.Text);
            int tx = S(88), tw = Width - tx - S(240);
            Ui.TextLeft(g, "One-time setup: download FFmpeg", Ui.F(12, 4), new Rectangle(tx, S(22), tw, S(26)), C.Text);
            string body;
            if (main.Downloading) body = "Downloading from " + FFmpeg.Source + " and checking its fingerprint\u2026  " + main.DownloadPercent + "%";
            else if (main.DownloadError.Length > 0) body = main.DownloadError;
            else body = "Clutch records with FFmpeg, the free open-source (GPLv3) video tool. It's about 90 MB from " + FFmpeg.Source + " (an official FFmpeg build source), checked against a SHA-256 fingerprint built into Clutch, and kept in your AppData folder.";
            Ui.Wrap(g, body, Ui.F(9.5f, 0), new Rectangle(tx, S(52), tw, Height - S(60)), main.DownloadError.Length > 0 && !main.Downloading ? C.Warn : C.Sub);
            if (main.Downloading)
            {
                RectangleF bar = new RectangleF(tx, Height - S(22), tw, S(5));
                Ui.FillRound(g, bar, S(2.5f), C.Control);
                Ui.FillRound(g, new RectangleF(bar.X, bar.Y, Math.Max(S(5), bar.Width * main.DownloadPercent / 100f), bar.Height), S(2.5f), C.Accent);
                Invalidate();
            }
        }
    }
}
