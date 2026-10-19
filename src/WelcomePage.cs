using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Clutch
{
    class WelcomePage : Page
    {
        int step;
        readonly Btn next = new Btn("Next", "", BtnStyle.Accent), back = new Btn("Back", "", BtnStyle.Subtle), skip = new Btn("Skip setup", "", BtnStyle.Subtle);
        readonly Btn download = new Btn("Download FFmpeg", Glyph.Download, BtnStyle.Standard), changeKey = new Btn("Change key", Glyph.Keyboard, BtnStyle.Standard),
                     addGame = new Btn("Add running game", Glyph.Add, BtnStyle.Standard);
        readonly Seg length = new Seg(new[] { "30 s", "60 s", "2 min", "3 min" });
        readonly Toggle gamesOnly = new Toggle();
        static readonly int[] Lengths = { 30, 60, 120, 180 };
        Rectangle box;

        public WelcomePage(MainForm m) : base(m)
        {
            next.Click += delegate { if (step == 2) Finish(); else Go(step + 1); };
            back.Click += delegate { Go(step - 1); };
            skip.Click += delegate { Finish(); };
            download.Click += delegate { Main.StartDownload(); Invalidate(true); };
            changeKey.Click += delegate { Main.CapturingHotkey = true; Main.UnregisterHotkey(); Invalidate(); };
            addGame.Click += delegate { Main.SettingsP.ShowAddRunningMenu(addGame); };
            length.Set(Math.Max(0, Array.IndexOf(Lengths, Settings.Cur.BufferSeconds)));
            length.Changed += delegate { Settings.Cur.BufferSeconds = Lengths[length.Index]; Settings.Save(); Main.RestartRecorder(); };
            gamesOnly.Set(Settings.Cur.GamesOnly);
            gamesOnly.Changed += delegate { Settings.Cur.GamesOnly = gamesOnly.On; Settings.Save(); Main.Sync(); };
            foreach (Control c in new Control[] { next, back, skip, download, changeKey, addGame, length, gamesOnly }) { c.BackColor = C.Card; Controls.Add(c); }
        }

        void Go(int s) { step = Math.Max(0, Math.Min(2, s)); Relayout(); Invalidate(true); }

        void Finish()
        {
            Main.CapturingHotkey = false; Main.RegisterHotkey();
            Settings.Cur.WelcomeDone = true; Settings.Save();
            Main.Go(Main.Clips);
        }

        public void Tick() { if (Visible) { download.Visible = step == 0 && !FFmpeg.Ready && !Main.Downloading; Invalidate(); } }

        public override void Relayout()
        {
            int w = S(560), h = S(430);
            box = new Rectangle((ClientSize.Width - w) / 2, Math.Max(S(20), (ClientSize.Height - h) / 2), w, h);
            int by = box.Bottom - S(60), bh = S(38);
            next.Text = step == 2 ? "Start clipping" : "Next";
            next.Height = bh; next.Width = Math.Max(S(120), next.PreferredWidth()); next.Location = new Point(box.Right - S(32) - next.Width, by);
            back.Height = bh; back.Width = back.PreferredWidth(); back.Location = new Point(next.Left - S(8) - back.Width, by); back.Visible = step > 0;
            skip.Height = bh; skip.Width = skip.PreferredWidth(); skip.Location = new Point(box.X + S(24), by); skip.Visible = step == 0;
            download.Visible = step == 0 && !FFmpeg.Ready && !Main.Downloading;
            download.Height = bh; download.Width = download.PreferredWidth() + S(8); download.Location = new Point(box.X + S(32), box.Y + S(262));
            changeKey.Visible = length.Visible = step == 1;
            changeKey.Height = S(34); changeKey.Width = changeKey.PreferredWidth() + S(6); changeKey.Location = new Point(box.X + S(170), box.Y + S(176));
            length.SetBounds(box.X + S(32), box.Y + S(268), S(320), S(36));
            gamesOnly.Visible = addGame.Visible = step == 2;
            gamesOnly.SetBounds(box.Right - S(32) - S(52), box.Y + S(176), S(52), S(30));
            addGame.Height = S(36); addGame.Width = addGame.PreferredWidth() + S(8); addGame.Location = new Point(box.X + S(32), box.Y + S(236));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics; Ui.Prep(g);
            Ui.FillRound(g, box, S(18), C.Card);
            Ui.StrokeRound(g, new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1), S(18), C.Stroke, 1);
            int x = box.X + S(32), tw = box.Width - S(64);
            Ui.DrawLogo(g, new Rectangle(x, box.Y + S(30), S(44), S(44)));
            string[] titles = { "Welcome to Clutch", "Your clip key", "Record only while gaming?" };
            Ui.TextLeft(g, titles[step], Ui.F(19, 4), new Rectangle(x, box.Y + S(88), tw, S(40)), C.Text);
            if (step == 0)
            {
                Ui.Wrap(g, "Clutch keeps the last moments of your game ready to save, with almost no effect on performance. No account, no ads, and everything stays on your PC.",
                    Ui.F(10.5f, 0), new Rectangle(x, box.Y + S(134), tw, S(70)), C.Sub);
                string ff = FFmpeg.Ready ? "\u2714  FFmpeg is ready." : Main.Downloading ? "Downloading FFmpeg\u2026 " + Main.DownloadPercent + "%" : Main.DownloadError.Length > 0 ? Main.DownloadError
                          : "Clutch records with FFmpeg, a free GPLv3 video tool (about 90 MB from gyan.dev, checked against a fingerprint built into Clutch).";
                Ui.Wrap(g, ff, Ui.F(10, FFmpeg.Ready ? 1 : 0), new Rectangle(x, box.Y + S(206), tw, S(50)), FFmpeg.Ready ? C.Good : C.Text);
                if (Main.Downloading)
                {
                    RectangleF bar = new RectangleF(x, box.Y + S(270), tw, S(6));
                    Ui.FillRound(g, bar, S(3), C.Control);
                    Ui.FillAccent(g, new RectangleF(bar.X, bar.Y, Math.Max(S(6), bar.Width * Main.DownloadPercent / 100f), bar.Height), S(3), 0, 255);
                }
            }
            else if (step == 1)
            {
                Ui.Wrap(g, "Press it in any game to save what just happened. The game still gets the key too.", Ui.F(10.5f, 0), new Rectangle(x, box.Y + S(134), tw, S(40)), C.Sub);
                string key = Main.CapturingHotkey ? "Press a key\u2026" : MainForm.HotkeyName();
                Font kf = Ui.F(14, 4); Rectangle cap = new Rectangle(x, box.Y + S(176), Math.Max(S(120), Ui.Measure(key, kf).Width + S(32)), S(44));
                Ui.FillRound(g, cap, S(10), C.ControlHi);
                Ui.FillRound(g, new Rectangle(cap.X, cap.Bottom - S(5), cap.Width, S(5)), S(3), C.Stroke);
                Ui.TextCenter(g, key, kf, new Rectangle(cap.X, cap.Y, cap.Width, cap.Height - S(4)), Main.CapturingHotkey ? C.AccentHi : C.Text);
                if (changeKey.Left < cap.Right + S(12)) changeKey.Left = cap.Right + S(12);
                Ui.TextLeft(g, "How much to save", Ui.F(10, 1), new Rectangle(x, box.Y + S(240), tw, S(22)), C.Text);
            }
            else
            {
                Ui.Wrap(g, "Clutch can wait in the tray and start recording only when one of your games opens. Otherwise it records whenever it's running.",
                    Ui.F(10.5f, 0), new Rectangle(x, box.Y + S(134), tw, S(44)), C.Sub);
                Ui.TextLeft(g, "Only record while a game is running", Ui.F(10.5f, 1), new Rectangle(x, box.Y + S(176), tw - S(70), S(30)), C.Text);
                List<string> names = new List<string>();
                foreach (GameEntry ge in Settings.Cur.Games) names.Add(ge.Name.Length > 0 ? ge.Name : ge.Exe);
                string list = names.Count == 0 ? "Open a game, then add it here. You can change this any time in Settings." : "Your games: " + string.Join(", ", names.ToArray());
                Ui.Wrap(g, list, Ui.F(9.5f, 0), new Rectangle(x, box.Y + S(284), tw, S(44)), names.Count == 0 ? C.Faint : C.Sub);
            }
            int dx = box.X + box.Width / 2 - S(25);
            for (int i = 0; i < 3; i++)
            {
                int dw = i == step ? S(22) : S(7);
                if (i == step) Ui.FillAccent(g, new RectangleF(dx, box.Bottom - S(44), dw, S(7)), S(3.5f), 0, 255);
                else Ui.FillRound(g, new RectangleF(dx, box.Bottom - S(44), dw, S(7)), S(3.5f), C.Control);
                dx += dw + S(7);
            }
        }
    }
}
