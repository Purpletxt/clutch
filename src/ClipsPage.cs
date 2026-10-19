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
        readonly Btn import = new Btn("Import", "\uE8B5", BtnStyle.Subtle);
        readonly Lbl title = new Lbl("Clips", 18, 4, C.Text), count = new Lbl("", 9.5f, 1, C.Faint);
        readonly Drop game = new Drop(), sort = new Drop();
        readonly Btn favs = new Btn("Favourites", Glyph.Star, BtnStyle.Subtle);
        readonly Field search = new Field();
        readonly SetupBanner banner;
        readonly ClipGrid grid;
        readonly Btn selFav = new Btn("Favourite", Glyph.Star, BtnStyle.Standard), selCopy = new Btn("Copy", Glyph.Copy, BtnStyle.Standard),
                     selDelete = new Btn("Delete", Glyph.Delete, BtnStyle.Danger), selClear = new Btn("Cancel", Glyph.Close, BtnStyle.Subtle);
        List<string> gameNames = new List<string>();
        static readonly string[] Sorts = { "Newest first", "Oldest first", "Longest first", "Biggest first", "Name A\u2013Z" };

        public ClipsPage(MainForm m) : base(m)
        {
            search.Icon = Glyph.Search; search.Placeholder = "Search"; search.Box.TabStop = false;
            search.Box.TextChanged += delegate { Refresh(); };
            import.Click += delegate { Import(); };
            favs.Click += delegate { ShowFavourites(!favs.On); Main.Rail.Invalidate(); };
            game.Changed += delegate { Refresh(); };
            sort.Items.AddRange(Sorts); sort.Index = 0; sort.Changed += delegate { Refresh(); };
            banner = new SetupBanner(m);
            grid = new ClipGrid(m);
            grid.SelectionChanged += delegate { Relayout(); };
            selFav.Click += delegate { List<Clip> l = Sel(); bool all = l.TrueForAll(delegate (Clip c) { return c.Favorite; }); foreach (Clip c in l) c.Favorite = !all; Library.Save(); grid.ClearSelection(); Refresh(); };
            selCopy.Click += delegate { Main.CopyClips(Sel()); };
            selDelete.Click += delegate { List<Clip> l = Sel(); grid.ClearSelection(); Main.DeleteClips(l); };
            selClear.Click += delegate { grid.ClearSelection(); };
            foreach (Control c in new Control[] { title, count, game, favs, sort, search, import, selFav, selCopy, selDelete, selClear, banner, grid }) Controls.Add(c);
        }

        public bool FavouritesOnly { get { return favs.On; } }
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
            favs.On = false; favs.Icon = Glyph.Star; search.Box.Text = "";
            Refresh();
            int i = gameNames.IndexOf(name); game.Index = i >= 0 ? i + 1 : 0;
            Refresh();
        }
        public void ShowFavourites(bool on) { favs.On = on; favs.Icon = on ? Glyph.StarFill : Glyph.Star; favs.Invalidate(); Refresh(); }

        public override void Relayout()
        {
            int pad = S(24), w = ClientSize.Width, y = S(18), h = S(34);
            int right = w - pad;
            bool selecting = grid.Selected.Count > 0;
            foreach (Control c in new Control[] { game, favs, sort, search, import }) c.Visible = !selecting;
            foreach (Control c in new Control[] { selFav, selCopy, selDelete, selClear }) c.Visible = selecting;
            if (selecting)
            {
                foreach (Btn b in new[] { selClear, selDelete, selCopy, selFav }) { b.Height = h; b.Width = b.PreferredWidth(); b.Location = new Point(right - b.Width, y); right = b.Left - S(8); }
                title.Text = grid.Selected.Count + " selected";
            }
            else title.Text = favs.On ? "Favourites" : "Clips";
            import.Height = h; import.Width = import.PreferredWidth(); import.Location = new Point(right - import.Width + S(6), y); right = import.Left - S(6);
            int sw = Math.Max(S(140), Math.Min(S(220), (w - S(560)) / 3));
            search.SetBounds(right - sw, y, sw, h); right = search.Left - S(8);
            sort.SetBounds(right - S(140), y, S(140), h); right = sort.Left - S(8);
            favs.Height = h; favs.Width = favs.PreferredWidth(); favs.Location = new Point(right - favs.Width, y); right = favs.Left - S(8);
            game.SetBounds(right - S(150), y, S(150), h); right = game.Left - S(16);
            int tw = Ui.Measure(title.Text, Ui.F(18, 4)).Width + S(4);
            title.SetBounds(pad, y - S(2), tw, h + S(4));
            count.SetBounds(title.Right + S(10), y + S(2), Math.Max(0, right - title.Right - S(10)), h);
            count.Visible = !selecting && count.Width > S(50);
            y += h + S(16);
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

            string q = search.Box.Text.Trim();
            List<Clip> list = new List<Clip>();
            foreach (Clip c in Library.Clips)
            {
                if (favs.On && !c.Favorite) continue;
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
            grid.Set(list, q.Length > 0 || favs.On || game.Index > 0, favs.On, sort.Index <= 1);
            int n = list.Count;
            count.Text = n == 1 ? "1 clip" : n + " clips";
            Relayout();
            banner.Invalidate(true); banner.Sync();
            game.Invalidate(); count.Invalidate();
        }

        public override void Entered() { Refresh(); }
        public void FreeMemory() { grid.FreeCache(null); }

        void Import()
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
