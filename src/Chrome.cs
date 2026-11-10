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

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84)
            {
                long lp = m.LParam.ToInt64();
                Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                if (p.Y < S(64) || (main.WindowState != FormWindowState.Maximized && p.Y < S(6))) { m.Result = (IntPtr)(-1); return; }
            }
            base.WndProc(ref m);
        }
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
            string m = Recorder.Now + "|" + (int)Recorder.SecondsHeld + "|" + Settings.Cur.BufferSeconds + "|" + Settings.Cur.Enabled + "|" + Games.Running + "|" + FFmpeg.Ready + "|" + main.DownloadPercent + "|" + Recorder.Struggling;
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
            if (h != hot) { hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; tip.SetToolTip(this, h == 99 ? tipText : null); Invalidate(); }
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

        readonly ToolTip tip = new ToolTip();
        string tipText = "";

        void DrawBuffer(Graphics g, Rectangle below)
        {
            int w = Width - S(20), h = S(66);
            Rectangle box = meter = new Rectangle(S(10), below.Y - h - S(12), w, h);
            bool rec = Recorder.Now == Recorder.State.Recording && Settings.Cur.Enabled && FFmpeg.Ready;
            int len = Settings.Cur.BufferSeconds;
            double held = rec ? Math.Min(len, Recorder.SecondsHeld) : 0;
            if (Program.Demo) { rec = true; held = Math.Min(len, 42); }
            bool slow = rec && Recorder.Struggling;
            string big; Color bigC = C.Text;
            if (rec) big = Secs(held);
            else if (!FFmpeg.Ready) { big = main.Downloading ? main.DownloadPercent + "%" : "Setup"; bigC = C.Warn; }
            else if (!Settings.Cur.Enabled) { big = "Off"; bigC = C.Faint; }
            else if (Settings.Cur.GamesOnly && Games.Running == "") { big = "Wait"; bigC = C.Sub; }
            else if (Recorder.Now == Recorder.State.Error) { big = "!"; bigC = C.Warn; }
            else { big = "\u2026"; bigC = C.Warn; }
            RectangleF rf = new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1);
            Ui.FillRound(g, rf, S(14), hot == 99 ? C.Control : C.Panel);
            Ui.StrokeRound(g, rf, S(14), slow ? Ui.Alpha(C.Warn, 110) : rec ? Ui.Alpha(C.Live, 90) : Ui.Mix(C.Panel, C.Text, 0.08f), 1);
            int top = box.Y + S(9), dd = S(8);
            Bitmap icon = rec && Games.Running.Length > 0 ? GameIcons.Get(Games.Running) : null;
            int rowW = dd + (icon != null ? S(6) + S(16) : 0), rx = box.X + (box.Width - rowW) / 2;
            Color dot = slow ? C.Warn : rec ? C.Live : Settings.Cur.Enabled && FFmpeg.Ready ? C.Warn : C.Faint;
            if (rec) using (SolidBrush halo = new SolidBrush(Ui.Alpha(dot, 70))) g.FillEllipse(halo, rx - S(3), top + S(4) - S(3), dd + S(6), dd + S(6));
            using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, rx, top + S(4), dd, dd);
            if (icon != null) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(icon, new Rectangle(rx + dd + S(6), top, S(16), S(16))); }
            Ui.TextCenter(g, big, Ui.F(12, 4), new Rectangle(box.X, top + S(17), box.Width, S(24)), bigC);
            RectangleF bar = new RectangleF(box.X + S(10), box.Bottom - S(11), box.Width - S(20), S(4));
            Ui.FillRound(g, bar, S(2), C.Control);
            double frac = len > 0 ? held / len : 0;
            if (frac > 0) Ui.FillAccent(g, new RectangleF(bar.X, bar.Y, Math.Max(S(4), bar.Width * (float)frac), bar.Height), S(2), 0, 255);
            string t = (slow ? Recorder.HealthNote + "\n\n" : "") + main.Bar.StatusText() + (rec ? "\nHolding " + Secs(held) + " of " + Secs(len) + ". Press " + MainForm.HotkeyName() + " to save it." : "") + "\nClick for options.";
            if (t != tipText) { tipText = t; if (hot == 99) tip.SetToolTip(this, t); }
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
