using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Clutch
{
    class Row : Ctl
    {
        public string Title, Desc, Icon = ""; public Control Ctl1, Ctl2; public int RightW, Right2W; public bool Line = true;
        public int MinH, CtlH;
        public Func<Image> Picture;
        public Row(string title, string desc, Control right, int rightW) { Title = title; Desc = desc ?? ""; Ctl1 = right; RightW = rightW; if (right != null) Controls.Add(right); }
        public void Add2(Control c, int w) { Ctl2 = c; Right2W = w; Controls.Add(c); }
        int TextW { get { return Width - S(RightW + Right2W + 60) - S(36); } }
        public int Fit() { int h = S(22) + (Desc.Length > 0 ? Ui.WrapHeight(Desc, Ui.F(9, 0), Math.Max(S(100), TextW)) + S(4) : 0); return Math.Max(S(MinH > 0 ? MinH : 64), h + S(28)); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = Width - S(20);
            if (Ctl1 != null) { int h = CtlH > 0 ? S(CtlH) : Ctl1 is Toggle ? S(30) : S(34); Ctl1.SetBounds(x - S(RightW), (Height - h) / 2, S(RightW), h); x -= S(RightW) + S(10); }
            if (Ctl2 != null) { int h = S(34); Ctl2.SetBounds(x - S(Right2W), (Height - h) / 2, S(Right2W), h); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            int x = S(20);
            Image pic = Picture != null ? Picture() : null;
            if (pic != null) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(pic, x + S(2), (Height - S(20)) / 2, S(20), S(20)); }
            else if (Icon.Length > 0) Ui.DrawIcon(g, Icon, 13, new Rectangle(x, 0, S(24), Height), Enabled ? C.Sub : C.Faint);
            x += S(36);
            int dh = Desc.Length > 0 ? Ui.WrapHeight(Desc, Ui.F(9, 0), Math.Max(S(100), TextW)) : 0;
            int y = (Height - S(22) - (dh > 0 ? dh + S(2) : 0)) / 2;
            Ui.TextLeft(g, Title, Ui.F(10, 1), new Rectangle(x, y, TextW, S(22)), Enabled ? C.Text : C.Faint);
            if (dh > 0) Ui.Wrap(g, Desc, Ui.F(9, 0), new Rectangle(x, y + S(23), TextW, dh + S(2)), C.Sub);
            if (Line) using (Pen p = new Pen(Ui.Mix(C.Card, C.Text, 0.06f))) g.DrawLine(p, S(16), Height - 1, Width - S(16), Height - 1);
        }
    }

    class Meter : Ctl
    {
        public Func<float> Level;
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            float v = Level == null ? 0 : Math.Min(1f, (float)Math.Sqrt(Level()));
            int bars = 12, gap = S(2), bw = (Width - gap * (bars - 1)) / bars, h = S(10), y = (Height - h) / 2;
            for (int i = 0; i < bars; i++)
            {
                bool lit = v * bars > i;
                Color c = !lit ? C.Control : i >= bars - 2 ? C.Accent : i >= bars - 4 ? C.Warn : C.Good;
                Ui.FillRound(g, new RectangleF(i * (bw + gap), y, bw, h), S(1.5f), c);
            }
        }
    }

    class SettingsPage : Page
    {
        readonly Scroller scroll = new Scroller();
        readonly Seg tabs = new Seg(new[] { "Recording", "Audio", "Games", "Storage", "General" });
        static readonly int[] TabFirstCard = { 0, 2, 3, 4, 5 }, TabLastCard = { 1, 2, 3, 4, 6 };
        readonly Panel content = new Panel();
        readonly List<Card> cards = new List<Card>();
        readonly Dictionary<Card, List<Row>> rows = new Dictionary<Card, List<Row>>();
        readonly Dictionary<Card, Lbl> heads = new Dictionary<Card, Lbl>();
        bool loading;

        Toggle tEnabled, tDesktop, tMic, tGamesOnly, tStartup, tTray, tOverlay, tSound, tDiscord, tUpdate;
        OverlayPicker pOverlay;
        Seg sLength, sFps, sQuality, sRes; Drop dMonitor, dEncoder, dDesktop, dMic; Btn bHotkey, bFfmpeg, bFolder, bOpenFolder, bLog, bAddApp, bBrowse, bUpdate, bReport;
        Slider micVol; Meter mDesktop, mMic;
        Row rMemory, rRes, rEncoder, rFfmpeg, rFolder, rDesktopDev, rMicDev, rMicVol, rHotkey, rUpdate;
        Card gamesCard;
        List<AudioDevice> desktopDevs = new List<AudioDevice>(), micDevs = new List<AudioDevice>();
        static readonly int[] Lengths = { 30, 60, 120, 180 };
        static readonly string[] Encoders = { "auto", "nvenc", "amf", "qsv", "x264" };

        public SettingsPage(MainForm m) : base(m)
        {
            content.BackColor = C.Bg;
            scroll.Controls.Add(content); Controls.Add(scroll); Controls.Add(tabs);
            tabs.Changed += delegate { scroll.AutoScrollPosition = Point.Empty; Relayout(); };
            Settings s = Settings.Cur;

            Card c = NewCard("Replay buffer");
            tEnabled = new Toggle(); tEnabled.Changed += delegate { if (!loading) Main.SetEnabled(tEnabled.On); };
            AddRow(c, "Replay buffer", "Keeps the last moments of your screen in memory, ready to save. Turn it off to stop recording completely.", tEnabled, 60).Icon = Glyph.Video;
            sLength = new Seg(new[] { "30 s", "60 s", "2 min", "3 min" });
            sLength.Changed += delegate { if (loading) return; s.BufferSeconds = Lengths[sLength.Index]; Changed(true); };
            AddRow(c, "Clip length", "How much is saved when you press the clip key.", sLength, 300).Icon = Glyph.Clock;
            bHotkey = new Btn("", Glyph.Keyboard, BtnStyle.Standard);
            bHotkey.Click += delegate { StartHotkeyCapture(); };
            rHotkey = AddRow(c, "Clip key", "Works in any game, and the game still receives the key too. Click to change.", bHotkey, 170); rHotkey.Icon = Glyph.Keyboard;
            rMemory = AddRow(c, "Memory use", "", null, 0); rMemory.Icon = Glyph.Info;

            c = NewCard("Video");
            sFps = new Seg(new[] { "30 fps", "60 fps" });
            sFps.Changed += delegate { if (loading) return; s.Fps = sFps.Index == 0 ? 30 : 60; Changed(true); };
            AddRow(c, "Frame rate", "60 fps looks smoother. 30 fps halves the work and the file size.", sFps, 200);
            sQuality = new Seg(new[] { "Low", "Standard", "High" });
            sQuality.Changed += delegate { if (loading) return; s.Quality = sQuality.Index; Changed(true); };
            AddRow(c, "Quality", "Higher quality means bigger clips and more memory.", sQuality, 300);
            sRes = new Seg(new[] { "Native", "1080p", "720p" });
            sRes.Changed += delegate { if (loading) return; s.Resolution = sRes.Index; Changed(true); };
            rRes = AddRow(c, "Recording resolution", "", sRes, 300);
            dMonitor = new Drop();
            dMonitor.Changed += delegate { if (loading) return; s.Monitor = dMonitor.Index; Changed(true); };
            AddRow(c, "Screen", "Which monitor to record.", dMonitor, 300).Icon = Glyph.Monitor;
            dEncoder = new Drop(); dEncoder.Items.AddRange(new[] { "Automatic (recommended)", "NVIDIA NVENC", "AMD AMF", "Intel Quick Sync", "Software (CPU)" });
            dEncoder.Changed += delegate { if (loading) return; s.Encoder = Encoders[dEncoder.Index]; Changed(true); };
            rEncoder = AddRow(c, "Encoder", "", dEncoder, 300);

            c = NewCard("Audio");
            tDesktop = new Toggle(); tDesktop.Changed += delegate { if (loading) return; s.DesktopAudio = tDesktop.On; AudioChanged(); };
            Row r = AddRow(c, "Game and desktop audio", "Everything you hear: the game, Discord, music.", tDesktop, 60); r.Icon = Glyph.Volume;
            dDesktop = new Drop(); dDesktop.Changed += delegate { if (loading) return; s.DesktopDevice = dDesktop.Index <= 0 ? "" : desktopDevs[dDesktop.Index - 1].Id; AudioChanged(); };
            mDesktop = new Meter(); mDesktop.Level = delegate { return Recorder.Desktop.Level; };
            rDesktopDev = AddRow(c, "Output device", "", dDesktop, 300); rDesktopDev.Add2(mDesktop, 90);
            tMic = new Toggle(); tMic.Changed += delegate { if (loading) return; s.Mic = tMic.On; AudioChanged(); };
            r = AddRow(c, "Microphone", "Saved on its own track too, so you can lower or mute it when trimming.", tMic, 60); r.Icon = Glyph.Mic;
            dMic = new Drop(); dMic.Changed += delegate { if (loading) return; s.MicDevice = dMic.Index <= 0 ? "" : micDevs[dMic.Index - 1].Id; AudioChanged(); };
            mMic = new Meter(); mMic.Level = delegate { return Recorder.Mic.Level; };
            rMicDev = AddRow(c, "Input device", "", dMic, 300); rMicDev.Add2(mMic, 90);
            micVol = new Slider(); micVol.Max = 200;
            micVol.Changed += delegate { if (loading) return; s.MicVolume = micVol.Value; Settings.Save(); };
            rMicVol = AddRow(c, "Mic volume in clips", "How loud your mic is in the mixed track.", micVol, 300);

            gamesCard = NewCard("Auto-start with games");
            tGamesOnly = new Toggle(); tGamesOnly.Changed += delegate { if (loading) return; s.GamesOnly = tGamesOnly.On; Settings.Save(); Main.Sync(); };
            AddRow(gamesCard, "Only record while a game is running", "Clutch waits quietly and starts the buffer when one of the games below opens.", tGamesOnly, 60).Icon = Glyph.Game;

            c = NewCard("Clips and storage");
            bFolder = new Btn("Change", "", BtnStyle.Standard); bFolder.Click += delegate { PickFolder(); };
            bOpenFolder = new Btn("", Glyph.Folder, BtnStyle.Standard);
            bOpenFolder.Click += delegate { try { Directory.CreateDirectory(s.Folder); System.Diagnostics.Process.Start("explorer.exe", "\"" + s.Folder + "\""); } catch { } };
            rFolder = AddRow(c, "Clips folder", "", bFolder, 100); rFolder.Add2(bOpenFolder, 36);
            bFfmpeg = new Btn("", Glyph.Download, BtnStyle.Standard);
            bFfmpeg.Click += delegate { if (Main.Downloading) Main.CancelDownload(); else Main.StartDownload(); };
            rFfmpeg = AddRow(c, "FFmpeg", "", bFfmpeg, 170);

            c = NewCard("General");
            tStartup = new Toggle(); tStartup.Changed += delegate { if (!loading) Settings.StartWithWindows = tStartup.On; };
            AddRow(c, "Start with Windows", "Opens quietly in the tray when you sign in.", tStartup, 60);
            tTray = new Toggle(); tTray.Changed += delegate { if (loading) return; s.CloseToTray = tTray.On; Settings.Save(); };
            AddRow(c, "Keep running in the tray when closed", "So the clip key keeps working after you close the window.", tTray, 60);
            tOverlay = new Toggle(); tOverlay.Changed += delegate { if (loading) return; s.Overlay = tOverlay.On; Settings.Save(); pOverlay.Enabled = s.Overlay; };
            AddRow(c, "Show \"Clipped!\" over the game", "A small notice that slides in from the edge of the screen. It never appears in your clips.", tOverlay, 60);
            pOverlay = new OverlayPicker();
            pOverlay.Changed += delegate { SavedOverlay.Pop("Clipped!", "This is where it'll show", true, Recorder.MonitorBounds(s.Monitor)); };
            Row rPos = AddRow(c, "Notification position", "Drag the notice up or down the edge, or across to the other side. Let go to see it on your screen.", pOverlay, 190);
            rPos.MinH = 128; rPos.CtlH = 104;
            tSound = new Toggle(); tSound.Changed += delegate { if (loading) return; s.SaveSound = tSound.On; Settings.Save(); };
            AddRow(c, "Play a sound when a clip is saved", "", tSound, 60);
            tDiscord = new Toggle(); tDiscord.Changed += delegate { if (loading) return; s.DiscordStatus = tDiscord.On; Settings.Save(); Main.Sync(); };
            AddRow(c, "Show what you're clipping on Discord", "Your Discord status shows \"Clipping Roblox with Clutch\" with the game's icon while Clutch records one of your games.", tDiscord, 60);

            c = NewCard("About");
            bUpdate = new Btn("Check now", "", BtnStyle.Standard);
            bUpdate.Click += delegate { if (Updater.Available != null) Main.StartUpdate(); else Main.CheckForUpdate(true); Tick(); };
            rUpdate = AddRow(c, "Clutch " + AppInfo.Version, "", Updater.Configured ? bUpdate : null, 150); rUpdate.Icon = Glyph.Download;
            if (Updater.Configured)
            {
                tUpdate = new Toggle(); tUpdate.Changed += delegate { if (loading) return; s.AutoUpdate = tUpdate.On; Settings.Save(); };
                AddRow(c, "Check for updates automatically", "Clutch asks GitHub about twice a day and tells you before installing anything.", tUpdate, 60);
            }
            bReport = new Btn("Create report", "", BtnStyle.Standard);
            bReport.Click += delegate { using (ReportWindow w = new ReportWindow(Report.Build(null), false)) { w.StartPosition = FormStartPosition.CenterParent; w.ShowInTaskbar = false; w.ShowDialog(Main); } };
            bLog = new Btn("Error log", "", BtnStyle.Subtle);
            bLog.Click += delegate { try { if (!File.Exists(Log.FilePath)) File.WriteAllText(Log.FilePath, ""); System.Diagnostics.Process.Start("notepad.exe", "\"" + Log.FilePath + "\""); } catch { } };
            Row rep = AddRow(c, "Report a problem", "Puts your setup and recent errors in a short report, with tips for common problems. You copy it and send it yourself.", bReport, 130); rep.Add2(bLog, 90); rep.Icon = Glyph.Warning;
            Btn bLic = new Btn("Licences", "", BtnStyle.Standard);
            bLic.Click += delegate { using (LicencesWindow w = new LicencesWindow()) w.ShowDialog(Main); };
            AddRow(c, "Credits and licences", "Local only: no account, no ads, nothing uploaded. Records with FFmpeg (GPLv3), downloaded separately from " + FFmpeg.Source + ".", bLic, 110).Icon = Glyph.Info;
            if (Setup.RunningInstalled)
            {
                Btn bRemove = new Btn("Uninstall", "", BtnStyle.Danger);
                bRemove.Click += delegate { Main.StartUninstall(); };
                AddRow(c, "Uninstall Clutch", "Your clips are kept.", bRemove, 110).Icon = Glyph.Delete;
            }

            bAddApp = new Btn("Add running app", Glyph.Add, BtnStyle.Standard); bAddApp.Click += delegate { AddRunningMenu(); };
            bBrowse = new Btn("Browse\u2026", "", BtnStyle.Standard); bBrowse.Click += delegate { BrowseExe(); };
            BuildGames();
            Load();
        }

        Card NewCard(string title)
        {
            Card c = new Card(); cards.Add(c); rows[c] = new List<Row>(); content.Controls.Add(c);
            Lbl h = new Lbl(title, 13, 4, C.Text); heads[c] = h; content.Controls.Add(h);
            return c;
        }

        Row AddRow(Card c, string title, string desc, Control right, int w)
        {
            Row r = new Row(title, desc, right, w); r.BackColor = C.Card; rows[c].Add(r); c.Controls.Add(r); return r;
        }

        void BuildGames()
        {
            List<Row> list = rows[gamesCard];
            for (int i = list.Count - 1; i >= 1; i--) { list[i].Controls.Remove(bAddApp); list[i].Controls.Remove(bBrowse);
                gamesCard.Controls.Remove(list[i]); list[i].Dispose(); list.RemoveAt(i); }
            foreach (GameEntry g in Settings.Cur.Games)
            {
                GameEntry entry = g;
                Btn rm = new Btn("", Glyph.Close, BtnStyle.Subtle); rm.IconPt = 9;
                rm.Click += delegate { Settings.Cur.Games.Remove(entry); Settings.Save(); BuildGames(); Relayout(); Main.Sync(); };
                Row r = AddRow(gamesCard, entry.Name.Length > 0 ? entry.Name : entry.Exe, entry.Exe, rm, 34);
                r.Icon = Glyph.Game;
                r.Picture = delegate { return GameIcons.Get(entry.Name); };
            }
            if (Settings.Cur.Games.Count == 0)
            {
                Row r = AddRow(gamesCard, "No games yet", "Open your game, then use \"Add running app\". Or browse for its .exe file.", null, 0);
                r.Icon = Glyph.Info;
            }
            Row add = AddRow(gamesCard, "", "", bAddApp, 170); add.Add2(bBrowse, 100); add.Line = false;
        }

        void Load()
        {
            loading = true; Settings s = Settings.Cur;
            tEnabled.Set(s.Enabled);
            sLength.Set(Math.Max(0, Array.IndexOf(Lengths, s.BufferSeconds)));
            sFps.Set(s.Fps == 30 ? 0 : 1); sQuality.Set(s.Quality); sRes.Set(s.Resolution);
            dEncoder.Index = Math.Max(0, Array.IndexOf(Encoders, s.Encoder));
            tDesktop.Set(s.DesktopAudio); tMic.Set(s.Mic); micVol.Value = s.MicVolume;
            tGamesOnly.Set(s.GamesOnly);
            tStartup.Set(Settings.StartWithWindows); tTray.Set(s.CloseToTray); tOverlay.Set(s.Overlay); pOverlay.Load(); pOverlay.Enabled = s.Overlay; tSound.Set(s.SaveSound); tDiscord.Set(s.DiscordStatus); if (tUpdate != null) tUpdate.Set(s.AutoUpdate);
            dMonitor.Items.Clear();
            Screen[] all = Screen.AllScreens; int n = 1;
            foreach (Screen sc in all) if (sc.Primary) dMonitor.Items.Add("Screen " + n++ + " (main)  \u00B7  " + sc.Bounds.Width + " \u00D7 " + sc.Bounds.Height);
            foreach (Screen sc in all) if (!sc.Primary) dMonitor.Items.Add("Screen " + n++ + "  \u00B7  " + sc.Bounds.Width + " \u00D7 " + sc.Bounds.Height);
            dMonitor.Index = Math.Min(s.Monitor, dMonitor.Items.Count - 1);
            loading = false;
        }

        void LoadDevices()
        {
            loading = true;
            desktopDevs = AudioDevices.List(0); micDevs = AudioDevices.List(1);
            dDesktop.Items.Clear(); dDesktop.Items.Add("Windows default");
            foreach (AudioDevice d in desktopDevs) dDesktop.Items.Add(d.Name);
            dDesktop.Index = Math.Max(0, desktopDevs.FindIndex(delegate (AudioDevice d) { return d.Id == Settings.Cur.DesktopDevice; }) + 1);
            dMic.Items.Clear(); dMic.Items.Add("Windows default");
            foreach (AudioDevice d in micDevs) dMic.Items.Add(d.Name);
            dMic.Index = Math.Max(0, micDevs.FindIndex(delegate (AudioDevice d) { return d.Id == Settings.Cur.MicDevice; }) + 1);
            loading = false;
        }

        void Changed(bool restart)
        {
            Settings.Save(); Tick(); Main.Bar.Invalidate();
            if (restart) Main.RestartRecorder();
        }

        void AudioChanged()
        {
            Settings.Save(); Tick();
            Recorder.ApplyAudio();
        }

        public override void Entered() { LoadDevices(); Load(); Tick(); Relayout(); }
        string free = ""; DateTime freeAt;
        public void Reload() { Load(); Tick(); Invalidate(true); }

        public void Tick()
        {
            if (!Visible) return;
            Settings s = Settings.Cur;
            Rectangle b = Recorder.MonitorBounds(s.Monitor);
            Size o = Recorder.OutputSize();
            int kbps = o.IsEmpty ? Recorder.Bitrate(b.Width, b.Height, s.Fps, s.Quality) : Recorder.Bitrate(o.Width, o.Height, s.Fps, s.Quality);
            string encNow = (Recorder.EncoderUsed.Length > 0 ? Recorder.EncoderUsed : s.ProbeResult).Split(':')[0];
            SetDesc(rRes, o.IsEmpty ? (s.Resolution == 0 ? "Records at your screen's own size (" + b.Width + " \u00D7 " + b.Height + "). Sharpest, and the lightest on your PC." : "Your screen is already this size or smaller, so nothing changes.")
                : "Records at " + o.Width + " \u00D7 " + o.Height + ": smaller clips and less memory. " + (encNow == "qsv" ? "Your graphics shrinks the picture itself, so it costs almost nothing." : "Your graphics card can't shrink video on its own, so this uses about 1\u20132 CPU cores while recording."));
            long mb = (long)kbps * (s.BufferSeconds + 2) / 8 / 1000 + (s.DesktopAudio ? (s.BufferSeconds + 10) * 192 / 1000 : 0) + (s.Mic ? (s.BufferSeconds + 10) * 192 / 1000 : 0);
            SetDesc(rMemory, "About " + mb + " MB of RAM for " + s.BufferSeconds + " s at " + (kbps / 1000.0).ToString("0.#") + " Mbps. Nothing is written to disk until you save a clip.");
            string inUse = Recorder.EncoderUsed.Length > 0 ? Recorder.EncoderUsed : s.ProbeResult;
            string enc = "Your graphics: " + Encoder.GpuSummary() + "." + (inUse.Length > 0 ? " In use: " + Encoder.Label(inUse.Split(':')[0]) + (inUse.EndsWith(":1") ? " (frames stay on the GPU)." : ".") : "");
            if (inUse.Length > 0 && Encoder.UsesCpu(inUse.Split(':')[0], inUse.EndsWith(":1"), s.Resolution != 0))
                enc += " Each frame goes through your CPU, so for the smallest hit to game FPS use 30 fps.";
            SetDesc(rEncoder, enc);
            if ((DateTime.Now - freeAt).TotalSeconds > 10)
            try { freeAt = DateTime.Now; free = "  \u00B7  " + (new DriveInfo(Path.GetPathRoot(s.Folder)).AvailableFreeSpace >> 30) + " GB free"; } catch { }
            SetDesc(rFolder, s.Folder + free);
            string ff = Main.Downloading ? "Downloading\u2026 " + Main.DownloadPercent + "%" : FFmpeg.Ready ? "Installed" + (FFmpeg.Version.Length > 0 ? " (" + FFmpeg.Version + ")" : "") + " from " + FFmpeg.Source + "." : Main.DownloadError.Length > 0 ? Main.DownloadError : "Not installed yet. Needed for recording.";
            SetDesc(rFfmpeg, ff);
            Updater.Release up = Updater.Available;
            SetDesc(rUpdate, !Updater.Configured ? "Updates aren't switched on in this build." : Updater.Checking ? "Checking for updates\u2026"
                : up != null ? "Version " + up.Version + " is ready to install. Clutch closes, updates and opens again in a few seconds."
                : Updater.LastError.Length > 0 ? Updater.LastError
                : Updater.LastCheck == DateTime.MinValue ? "Not checked yet." : "You're on the latest version (checked " + Updater.LastCheck.ToString("HH:mm") + ").");
            string ut = up != null ? "Update to " + up.Version : "Check now";
            if (bUpdate.Text != ut) { bUpdate.Text = ut; bUpdate.Style = up != null ? BtnStyle.Accent : BtnStyle.Standard; bUpdate.Weight = up != null ? 5 : 1; bUpdate.Invalidate(); }
            string bt = Main.Downloading ? "Cancel" : FFmpeg.Ready ? "Re-download" : "Download";
            if (bFfmpeg.Text != bt) bFfmpeg.Text = bt;
            if (!Main.CapturingHotkey && bHotkey.Text != MainForm.HotkeyName()) bHotkey.Text = MainForm.HotkeyName();
            SetDesc(rDesktopDev, Recorder.Desktop.Running ? (Recorder.Desktop.Error.Length > 0 ? "Problem: " + Recorder.Desktop.Error : "Recording from " + Recorder.Desktop.DeviceName) : "");
            SetDesc(rMicDev, Recorder.Mic.Running ? (Recorder.Mic.Error.Length > 0 ? "Problem: " + Recorder.Mic.Error : "Recording from " + Recorder.Mic.DeviceName) : "");
            rDesktopDev.Enabled = s.DesktopAudio; rMicDev.Enabled = s.Mic; rMicVol.Enabled = s.Mic;
            if (Recorder.Desktop.Running) mDesktop.Invalidate();
            if (Recorder.Mic.Running) mMic.Invalidate();
        }

        void SetDesc(Row r, string d)
        {
            if (r.Desc == d) return;
            int before = r.Fit(); r.Desc = d;
            r.Invalidate(); if (r.Fit() != before) Relayout();
        }

        public override void Relayout()
        {
            int sb = SystemInformation.VerticalScrollBarWidth;
            int w = Math.Min(S(860), ClientSize.Width - S(56) - sb);
            int x = Math.Max(S(28), (ClientSize.Width - sb - w) / 2), y = S(8);
            tabs.SetBounds(x, S(18), Math.Min(w, S(560)), S(36));
            scroll.SetBounds(0, S(66), ClientSize.Width, ClientSize.Height - S(66));
            for (int ci = 0; ci < cards.Count; ci++)
            {
                Card c = cards[ci];
                bool show = ci >= TabFirstCard[tabs.Index] && ci <= TabLastCard[tabs.Index];
                c.Visible = heads[c].Visible = show;
                if (!show) continue;
                Lbl h = heads[c]; h.SetBounds(x + S(4), y, w, S(28)); y += S(36);
                int cy = S(4);
                List<Row> rl = rows[c];
                foreach (Row r in rl) { r.Line = r != rl[rl.Count - 1]; r.Width = w - S(8); int rh = r.Fit(); r.SetBounds(S(4), cy, w - S(8), rh); cy += rh; }
                c.SetBounds(x, y, w, cy + S(4)); y += cy + S(4) + S(28);
            }
            content.SetBounds(0, content.Top, ClientSize.Width - SystemInformation.VerticalScrollBarWidth, y + S(10));
        }

        public void ScrollTo(string which)
        {
            int t = which.EndsWith("audio") ? 1 : which.EndsWith("games") ? 2 : which.EndsWith("storage") ? 3 : which.EndsWith("general") ? 4 : 0;
            tabs.Index = t; scroll.AutoScrollPosition = Point.Empty; Relayout();
        }

        void StartHotkeyCapture()
        {
            Main.CapturingHotkey = true; Main.UnregisterHotkey();
            bHotkey.Text = "Press a key\u2026"; bHotkey.On = true; bHotkey.Invalidate();
            Main.Focus();
        }

        public void HotkeyKey(KeyEventArgs e)
        {
            Keys k = e.KeyCode;
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin) return;
            Main.CapturingHotkey = false; bHotkey.On = false;
            if (k != Keys.Escape)
            {
                int mods = (e.Alt ? 1 : 0) | (e.Control ? 2 : 0) | (e.Shift ? 4 : 0);
                Settings.Cur.HotkeyVk = (int)k; Settings.Cur.HotkeyMods = mods; Settings.Save();
            }
            Main.RegisterHotkey();
            bHotkey.Text = MainForm.HotkeyName(); bHotkey.Invalidate();
            Main.Bar.PerformLayout(); Main.Bar.Invalidate();
            Main.Welcome.Invalidate(true);
        }

        void AddRunningMenu() { ShowAddRunningMenu(bAddApp); }
        public void ShowAddRunningMenu(Control anchor)
        {
            ContextMenuStrip m = Menus.Make();
            List<Games.App> apps = Games.RunningApps();
            if (apps.Count == 0) { ToolStripMenuItem none = new ToolStripMenuItem("No apps with a window are open"); none.Enabled = false; m.Items.Add(none); }
            foreach (Games.App a in apps)
            {
                Games.App app = a;
                bool already = Settings.Cur.Games.Exists(delegate (GameEntry g) { return string.Equals(g.Exe, app.Exe, StringComparison.OrdinalIgnoreCase); });
                ToolStripMenuItem it = new ToolStripMenuItem(app.Name + "   (" + app.Exe + ")"); it.Enabled = !already;
                it.Click += delegate { GameIcons.Remember(app.Name, app.Path); AddGame(app.Exe, app.Name); };
                m.Items.Add(it);
            }
            m.MaximumSize = new Size(S(520), S(600));
            m.Show(anchor, new Point(0, anchor.Height + S(2)));
        }

        void BrowseExe()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "Programs (*.exe)|*.exe"; d.Title = "Choose the game's .exe file";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string name = Path.GetFileNameWithoutExtension(d.FileName);
                try { string desc = System.Diagnostics.FileVersionInfo.GetVersionInfo(d.FileName).FileDescription; if (!string.IsNullOrEmpty(desc) && desc.Length <= 40) name = desc.Trim(); } catch { }
                GameIcons.Remember(name, d.FileName);
                AddGame(Path.GetFileName(d.FileName), name);
            }
        }

        void AddGame(string exe, string name)
        {
            if (Settings.Cur.Games.Exists(delegate (GameEntry g) { return string.Equals(g.Exe, exe, StringComparison.OrdinalIgnoreCase); })) return;
            GameEntry e = new GameEntry(); e.Exe = exe; e.Name = name;
            Settings.Cur.Games.Add(e); Settings.Save();
            BuildGames(); Relayout();
            Main.Toast.Show(Glyph.Game, "Added " + name + ".", null, null, 3000);
            Main.Sync();
        }

        void PickFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Where should Clutch save your clips?"; d.SelectedPath = Settings.Cur.Folder;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Settings.Cur.ClipsFolder = d.SelectedPath; Settings.Save();
                Library.Rescan(); Tick();
            }
        }
    }
}
