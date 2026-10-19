using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Clutch
{
    class NavRail : Ctl
    {
        readonly MainForm main;
        class Item { public string Icon, Label; public Rectangle R; public Action Click; public Func<bool> Active; }
        readonly List<Item> items = new List<Item>();
        int hot = -1;
        Rectangle meter;

        public NavRail(MainForm m)
        {
            main = m; BackColor = C.Chrome;
            Add("clips", "Clips", delegate { main.Clips.ShowFavourites(false); main.Go(main.Clips); },
                delegate { return (main.Current == main.Clips && !main.Clips.FavouritesOnly) || main.Current == main.Viewer || main.Current == main.Trim; });
            Add("star", "Favourites", delegate { main.Clips.ShowFavourites(true); main.Go(main.Clips); },
                delegate { return main.Current == main.Clips && main.Clips.FavouritesOnly; });
            Add("games", "Games", delegate { main.Go(main.GamesP); }, delegate { return main.Current == main.GamesP; });
            Add("settings", "Settings", delegate { main.Go(main.SettingsP); }, delegate { return main.Current == main.SettingsP; });
        }

        void Add(string icon, string label, Action click, Func<bool> active)
        {
            Item i = new Item(); i.Icon = icon; i.Label = label; i.Click = click; i.Active = active; items.Add(i);
        }

        public void Remeasure() { Invalidate(); }

        string lastMeter = "";
        public void Tick()
        {
            string m = Recorder.Now + "|" + (int)Recorder.SecondsHeld + "|" + Settings.Cur.BufferSeconds + "|" + Settings.Cur.Enabled + "|" + Games.Running + "|" + FFmpeg.Ready + "|" + main.DownloadPercent;
            if (m != lastMeter) { lastMeter = m; Invalidate(); }
        }

        void Place()
        {
            int y = S(70), h = S(58);
            for (int i = 0; i < items.Count; i++)
            {
                if (i == items.Count - 1) items[i].R = new Rectangle(S(8), Height - S(14) - h, Width - S(16), h);
                else { items[i].R = new Rectangle(S(8), y, Width - S(16), h); y += h + S(4); }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); Place();
            int h = items.FindIndex(delegate (Item i) { return i.R.Contains(e.Location); });
            if (h < 0 && meter.Contains(e.Location)) h = 99;
            if (h != hot) { hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            Place();
            foreach (Item i in items) if (i.R.Contains(e.Location)) { i.Click(); Invalidate(); return; }
            if (meter.Contains(e.Location)) main.ShowStatusMenu(this, new Point(meter.Right + S(6), meter.Y));
        }

        static string Secs(double s) { int t = (int)s; return t < 60 ? t + "s" : (t / 60) + ":" + (t % 60).ToString("00"); }

        void DrawBuffer(Graphics g, Rectangle below)
        {
            int w = Width - S(20), h = S(78);
            Rectangle box = meter = new Rectangle(S(10), below.Y - h - S(12), w, h);
            Ui.FillRound(g, box, S(10), hot == 99 ? C.Control : C.Panel);
            bool rec = Recorder.Now == Recorder.State.Recording && Settings.Cur.Enabled && FFmpeg.Ready;
            int len = Settings.Cur.BufferSeconds;
            double held = rec ? Math.Min(len, Recorder.SecondsHeld) : 0;
            if (Program.Demo) { rec = true; held = Math.Min(len, 42); }
            string big, small; Color bigC = C.Text;
            string label = rec ? "READY" : "REPLAY";
            if (rec) { big = Secs(held); small = "of " + Secs(len); }
            else if (!FFmpeg.Ready) { big = main.Downloading ? main.DownloadPercent + "%" : "SETUP"; small = main.Downloading ? "downloading" : "needed"; bigC = C.Warn; }
            else if (!Settings.Cur.Enabled) { big = "OFF"; small = "buffer"; bigC = C.Faint; }
            else if (Settings.Cur.GamesOnly && Games.Running == "") { big = "WAIT"; small = "for a game"; bigC = C.Sub; }
            else if (Recorder.Now == Recorder.State.Error) { big = "!"; small = "see log"; bigC = C.Warn; }
            else { big = "..."; small = "starting"; bigC = C.Warn; }
            Color dot = rec ? C.Live : C.Faint;
            int dd = S(6), lx = box.X + S(9), ly = box.Y + S(10);
            if (rec)
            {
                float pulse = 0.7f;
                using (SolidBrush halo = new SolidBrush(Ui.Alpha(C.Live, (int)(80 * pulse)))) g.FillEllipse(halo, lx - S(3), ly - S(3), dd + S(6), dd + S(6));
            }
            using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, lx, ly, dd, dd);
            Ui.TextLeft(g, label, Ui.F(6.5f, 1), new Rectangle(lx + S(10), ly - S(5), box.Width, S(16)), C.Sub);
            Ui.TextCenter(g, big, Ui.F(13, 4), new Rectangle(box.X, box.Y + S(20), box.Width, S(26)), bigC);
            Ui.TextCenter(g, small, Ui.F(7, 0), new Rectangle(box.X, box.Y + S(44), box.Width, S(14)), C.Faint);
            int n = 4, gap = S(3), sx = box.X + S(8), sw = (box.Width - S(16) - gap * (n - 1)) / n, sy = box.Bottom - S(13);
            double frac = len > 0 ? held / len : 0;
            for (int i = 0; i < n; i++)
            {
                RectangleF seg = new RectangleF(sx + i * (sw + gap), sy, sw, S(5));
                Ui.FillRound(g, seg, S(2), C.Control);
                double part = Math.Max(0, Math.Min(1, frac * n - i));
                if (part > 0) Ui.FillAccent(g, new RectangleF(seg.X, seg.Y, Math.Max(S(2), seg.Width * (float)part), seg.Height), S(2), 0, 255);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e); Place();
            int ls = S(34);
            Ui.DrawLogo(g, new Rectangle((Width - ls) / 2, S(16), ls, ls));
            for (int i = 0; i < items.Count; i++)
            {
                Item it = items[i]; bool act = it.Active(), h = hot == i;
                Color back = act ? C.Panel : h ? Ui.Mix(C.Chrome, C.Panel, 0.6f) : C.Chrome;
                if (act || h) Ui.FillRound(g, it.R, S(10), back);
                if (act) Ui.FillAccent(g, new RectangleF(0, it.R.Y + S(16), S(3), it.R.Height - S(32)), S(1.5f), 0, 255);
                Color ink = act || h ? Color.White : Ui.Mix(C.Sub, Color.White, 0.55f);
                float isz = S(22);
                RailIcons.Draw(g, it.Icon, new RectangleF(it.R.X + (it.R.Width - isz) / 2, it.R.Y + S(9), isz, isz), ink, back);
                Ui.TextCenter(g, it.Label, Ui.F(7.5f, 5), new Rectangle(it.R.X - S(4), it.R.Y + S(35), it.R.Width + S(8), S(16)), ink);
            }
            DrawBuffer(g, items[items.Count - 1].R);
            using (Pen p = new Pen(C.Panel)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);
        }
    }

    class TopBar : Ctl
    {
        readonly MainForm main;
        readonly Btn save;
        Rectangle chipKey, chipQuality; int hot = -1;

        public TopBar(MainForm m)
        {
            main = m; BackColor = C.Chrome;
            save = new Btn("Save clip", Glyph.Film, BtnStyle.Accent); save.Round = true;
            save.Click += delegate { main.SaveClip(); };
            Controls.Add(save);
        }
        public void SyncToggle() { Invalidate(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (save == null) return;
            save.Height = S(36); save.Width = save.PreferredWidth() + S(6);
            save.Location = new Point(Width - save.Width - S(18), (Height - save.Height) / 2);
        }

        public string StatusText()
        {
            Settings s = Settings.Cur;
            if (!FFmpeg.Ready) return main.Downloading ? "Setting up\u2026 " + main.DownloadPercent + "%" : "Setup needed";
            if (!s.Enabled) return "Replay buffer off";
            if (s.GamesOnly && Games.Running == "") return s.Games.Count == 0 ? "Pick a game to record" : "Start your game to record";
            switch (Recorder.Now)
            {
                case Recorder.State.Recording: return Games.Running != "" ? "Recording \u00B7 " + Games.Running : "Recording";
                case Recorder.State.Starting: return Recorder.Status;
                case Recorder.State.Error: return Recorder.Status;
            }
            return "Replay buffer off";
        }

        static string LengthName(int s) { return s < 120 ? s + "s" : (s / 60) + " min"; }

        string QualityText()
        {
            Size o = Recorder.OutputSize();
            int h = o.IsEmpty ? Recorder.MonitorBounds(Settings.Cur.Monitor).Height : o.Height;
            return h + "p  " + Settings.Cur.Fps + "fps";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = chipKey.Contains(e.Location) ? 1 : chipQuality.Contains(e.Location) ? 2 : -1;
            if (h != hot) { hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            Settings s = Settings.Cur;
            ContextMenuStrip m = Menus.Make(); Rectangle at;
            if (chipKey.Contains(e.Location))
            {
                at = chipKey;
                foreach (int len in new[] { 30, 60, 120, 180 })
                {
                    int l = len;
                    Check(m, "Save the last " + (l < 120 ? l + " seconds" : (l / 60) + " minutes"), s.BufferSeconds == l, delegate { s.BufferSeconds = l; Settings.Save(); main.RestartRecorder(); main.SettingsP.Reload(); Invalidate(); });
                }
                m.Items.Add(new ToolStripSeparator());
                Menus.Item(m, "Change clip key\u2026", Glyph.Keyboard, delegate { main.Go(main.SettingsP); main.SettingsP.ScrollTo("settings"); });
            }
            else if (chipQuality.Contains(e.Location))
            {
                at = chipQuality;
                Check(m, "60 fps", s.Fps == 60, delegate { s.Fps = 60; Changed(); });
                Check(m, "30 fps", s.Fps == 30, delegate { s.Fps = 30; Changed(); });
                m.Items.Add(new ToolStripSeparator());
                string[] q = { "Low quality", "Standard quality", "High quality" };
                for (int i = 2; i >= 0; i--) { int k = i; Check(m, q[i], s.Quality == i, delegate { s.Quality = k; Changed(); }); }
                m.Items.Add(new ToolStripSeparator());
                string[] res = { "Native resolution", "1080p (uses more CPU)", "720p (uses more CPU)" };
                for (int i = 0; i < 3; i++) { int k = i; Check(m, res[i], s.Resolution == i, delegate { s.Resolution = k; Changed(); }); }
            }
            else { m.Dispose(); return; }
            m.Show(this, new Point(at.X, at.Bottom + S(4)));
        }

        void Changed() { Settings.Save(); main.RestartRecorder(); main.SettingsP.Reload(); Invalidate(); }

        static void Check(ContextMenuStrip m, string text, bool on, Action a)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text); it.Checked = on; it.Click += delegate { a(); }; m.Items.Add(it);
        }

        Rectangle Chip(Graphics g, int x, int w, int i)
        {
            int h = S(34); Rectangle r = new Rectangle(x, (Height - h) / 2, w, h);
            Ui.FillRound(g, r, S(8), hot == i ? C.Control : C.Panel);
            Ui.DrawIcon(g, "\uE70D", 7, new Rectangle(r.Right - S(24), r.Y, S(16), r.Height), C.Sub);
            return r;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            int x = S(16); Font f = Ui.F(9.5f, 1); int w;

            string key = MainForm.HotkeyName(), len = "Clip " + LengthName(Settings.Cur.BufferSeconds);
            Font kf = Ui.F(8.5f, 1);
            int kw = Math.Max(S(28), Ui.Measure(key, kf).Width + S(14));
            w = S(10) + kw + S(10) + Ui.Measure(len, f).Width + S(34);
            chipKey = Chip(g, x, w, 1);
            Rectangle cap = new Rectangle(x + S(8), chipKey.Y + S(6), kw, chipKey.Height - S(12));
            Ui.FillRound(g, cap, S(5), C.ControlHi);
            Ui.FillRound(g, new Rectangle(cap.X, cap.Bottom - S(3), cap.Width, S(3)), S(2), C.Stroke);
            Ui.TextCenter(g, key, kf, new Rectangle(cap.X, cap.Y, cap.Width, cap.Height - S(2)), C.Text);
            Ui.TextLeft(g, len, f, new Rectangle(cap.Right + S(10), chipKey.Y, w, chipKey.Height - S(1)), C.Text);
            x = chipKey.Right + S(8);

            string q = QualityText();
            w = Ui.Measure(q, f).Width + S(58);
            chipQuality = Chip(g, x, w, 2);
            Ui.DrawIcon(g, Glyph.Monitor, 10, new Rectangle(x + S(10), chipQuality.Y, S(18), chipQuality.Height), C.Text);
            Ui.TextLeft(g, q, f, new Rectangle(x + S(34), chipQuality.Y, w, chipQuality.Height - S(1)), C.Text);

            using (Pen p = new Pen(C.Panel)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }
}
