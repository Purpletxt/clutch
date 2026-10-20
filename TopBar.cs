using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Clutch
{
    class TopBar : Ctl
    {
        readonly MainForm main;
        readonly Btn save;
        public readonly Field Search = new Field();
        Rectangle chipKey, chipLen, chipQuality; int hot = -1;
        readonly List<Control> tools = new List<Control>();
        public const int Row1 = 14, Row2 = 70, Tall = 122;

        public TopBar(MainForm m)
        {
            main = m; BackColor = C.Bg;
            save = new Btn("Save clip", Glyph.Film, BtnStyle.Accent); save.Round = true; save.TextPt = 10.5f;
            save.Click += delegate { main.SaveClip(); };
            Search.Box.TabStop = false; Search.Icon = Glyph.Search; Search.Placeholder = "Search clips or games\u2026"; Search.Keys = new[] { "Ctrl", "K" };
            Search.Box.TextChanged += delegate { main.SearchChanged(Search.Box.Text); };
            Search.Box.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Search.Box.Text = ""; main.Focus(); }
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; main.Focus(); }
            };
            Controls.Add(Search); Controls.Add(save);
        }

        public string Query { get { return Search.Box.Text.Trim(); } }
        public void ClearSearch() { if (Search.Box.Text.Length > 0) Search.Box.Text = ""; }
        public void FocusSearch() { Search.Box.Focus(); Search.Box.SelectAll(); }
        public void SyncToggle() { Invalidate(); }

        public void SetTools(Control[] list)
        {
            foreach (Control c in tools) c.Visible = false;
            tools.Clear();
            if (list != null) foreach (Control c in list) { if (c.Parent != this) Controls.Add(c); tools.Add(c); c.Visible = true; }
            PerformLayout(); Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (save == null) return;
            int h1 = S(44);
            save.Height = h1; save.Width = save.PreferredWidth() + S(12);
            save.Location = new Point(Width - save.Width - S(24), S(Row1));
            Search.SetBounds(S(24), S(Row1), Math.Max(S(200), Math.Min(S(680), save.Left - S(24) - S(40))), h1);
            int right = Width - S(24), y = S(Row2), h = S(40), left = PillsRight() + S(16);
            for (int i = tools.Count - 1; i >= 0; i--)
            {
                Control c = tools[i];
                Btn b = c as Btn; int w = b != null ? (b.Text.Length > 0 ? b.PreferredWidth() + S(8) : h + S(4)) : c.Width > 0 ? c.Width : S(160);
                c.SetBounds(right - w, y, w, h); right -= w + S(10);
            }
            if (right < left)
            {
                foreach (Control c in tools) { Btn b = c as Btn; if (b != null && b.Visible && b.Icon.Length > 0 && b.Text.Length > 0) { b.Tag = b.Text; b.Text = ""; PerformLayout(); return; } }
            }
            else if (right > left + S(130))
            {
                foreach (Control c in tools) { Btn b = c as Btn; if (b != null && b.Tag is string) { b.Text = (string)b.Tag; b.Tag = null; PerformLayout(); return; } }
            }
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
            int h = chipKey.Contains(e.Location) ? 1 : chipLen.Contains(e.Location) ? 3 : chipQuality.Contains(e.Location) ? 2 : -1;
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
                Menus.Item(m, "Save a clip now", Glyph.Film, delegate { main.SaveClip(); });
                Menus.Item(m, "Change clip key\u2026", Glyph.Keyboard, delegate { main.Go(main.SettingsP); main.SettingsP.ScrollTo("settings"); });
            }
            else if (chipLen.Contains(e.Location))
            {
                at = chipLen;
                foreach (int len in new[] { 30, 60, 120, 180 })
                {
                    int l = len;
                    Check(m, "Save the last " + (l < 120 ? l + " seconds" : (l / 60) + " minutes"), s.BufferSeconds == l, delegate { s.BufferSeconds = l; Settings.Save(); main.RestartRecorder(); main.SettingsP.Reload(); Invalidate(); });
                }
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

        int KeyW { get { return Math.Max(S(34), Ui.Measure(MainForm.HotkeyName(), Ui.F(9, 5)).Width + S(18)); } }
        int LenW { get { return Ui.Measure("Clip " + LengthName(Settings.Cur.BufferSeconds), Ui.F(10, 1)).Width + S(58); } }
        int QualW { get { return Ui.Measure(QualityText(), Ui.F(10, 1)).Width + S(86); } }
        int PillsRight() { return S(24) + KeyW + S(48) + S(10) + LenW + S(10) + QualW; }

        Rectangle Chip(Graphics g, int x, int w, int i)
        {
            Rectangle r = new Rectangle(x, S(Row2), w, S(40));
            RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            Ui.FillRound(g, rf, S(10), hot == i ? C.Control : C.Panel);
            Ui.StrokeRound(g, rf, S(10), Ui.Mix(C.Panel, C.Text, 0.07f), 1);
            Ui.DrawIcon(g, Glyph.Down, 8, new Rectangle(r.Right - S(32), r.Y, S(20), r.Height), C.Sub);
            return r;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            int x = S(24);
            Font f = Ui.F(10, 1);
            string key = MainForm.HotkeyName();
            chipKey = Chip(g, x, KeyW + S(48), 1);
            Rectangle cap = new Rectangle(x + S(9), chipKey.Y + S(7), KeyW, chipKey.Height - S(14));
            Ui.FillAccent(g, cap, S(6), 0, 80);
            Ui.TextCenter(g, key, Ui.F(9, 5), cap, C.Text);
            x = chipKey.Right + S(10);

            string len = "Clip " + LengthName(Settings.Cur.BufferSeconds);
            chipLen = Chip(g, x, LenW, 3);
            Ui.TextLeft(g, len, f, new Rectangle(x + S(18), chipLen.Y, LenW, chipLen.Height - S(1)), C.Text);
            x = chipLen.Right + S(10);

            string q = QualityText();
            chipQuality = Chip(g, x, QualW, 2);
            Ui.DrawIcon(g, Glyph.Monitor, 11, new Rectangle(x + S(14), chipQuality.Y, S(22), chipQuality.Height), C.Text);
            Ui.TextLeft(g, q, f, new Rectangle(x + S(44), chipQuality.Y, QualW, chipQuality.Height - S(1)), C.Text);
        }
    }
}
