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

}
