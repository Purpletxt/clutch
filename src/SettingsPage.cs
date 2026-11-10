using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Clutch
{
    class Row : Ctl
    {
        public string Title, Desc, Tip = "", Icon = ""; public Control Ctl1, Ctl2; public int RightW, Right2W; public bool Line = true;
        public bool Hidden;
        public Func<Image> Picture;
        public Row(string title, string desc, Control right, int rightW) { Title = title; Desc = desc ?? ""; Ctl1 = right; RightW = rightW; if (right != null) Controls.Add(right); }
        public void Add2(Control c, int w) { Ctl2 = c; Right2W = w; Controls.Add(c); }
        static readonly ToolTip tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 350 };
        public void SetTip(string t)
        {
            if ((t ?? "") == Tip) return;
            Tip = t ?? ""; string w = Tip.Length > 0 ? WrapTip(Tip) : null;
            tips.SetToolTip(this, w); if (Ctl1 != null && !(Ctl1 is Field)) tips.SetToolTip(Ctl1, w); Invalidate();
        }
        static string WrapTip(string s)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder(); int col = 0;
            foreach (string word in s.Split(' '))
            {
                if (col > 0 && col + word.Length > 60) { b.Append('\n'); col = 0; } else if (col > 0) { b.Append(' '); col++; }
                b.Append(word); col += word.Length;
            }
            return b.ToString();
        }
        int TextW { get { return Width - S(RightW + Right2W + 60) - S(36); } }
        public int Fit() { int h = S(22) + (Desc.Length > 0 ? Ui.WrapHeight(Desc, Ui.F(9, 0), Math.Max(S(100), TextW)) + S(4) : 0); return Math.Max(S(64), h + S(28)); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = Width - S(20);
            if (Ctl1 != null) { int h = Ctl1 is Toggle ? S(30) : S(34); Ctl1.SetBounds(x - S(RightW), (Height - h) / 2, S(RightW), h); x -= S(RightW) + S(10); }
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
            if (Tip.Length > 0)
            {
                int tw = TextRenderer.MeasureText(g, Title, Ui.F(10, 1), new Size(TextW, S(22)), TextFormatFlags.NoPadding).Width;
                if (tw + S(20) < TextW) Ui.DrawIcon(g, Glyph.Info, 8.5f, new Rectangle(x + tw + S(4), y, S(16), S(22)), C.Faint);
            }
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
        readonly BackdropScroller scroll = new BackdropScroller();
        readonly PillTabs tabs = new PillTabs(new[] { "Recording", "Audio", "Games", "Storage", "General" }, new[] { Glyph.Video, Glyph.Volume, Glyph.Game, Glyph.Folder, Glyph.Settings });
        readonly Lbl pageHead = new Lbl("Settings", 24, 4, C.Text), pageSub = new Lbl("Changes save straight away.", 10.5f, 1, C.Faint);
        static readonly int[] TabFirstCard = { 0, 2, 3, 4, 5 }, TabLastCard = { 1, 2, 3, 4, 6 };
        readonly BackdropPanel content = new BackdropPanel();
        readonly List<Card> cards = new List<Card>();
        readonly Dictionary<Card, List<Row>> rows = new Dictionary<Card, List<Row>>();
        readonly Dictionary<Card, Lbl> heads = new Dictionary<Card, Lbl>();
        bool loading;

        Toggle tPreview, tVoice, tSwitcher, tHold, tSplit, tEnabled, tDesktop, tMic, tGamesOnly, tStartup, tTray, tOverlay, tSound, tDiscord, tUpdate, tAdvanced, tDetect, tClean;
        Seg sCleanDays, sCleanSize; Row rScreen, rCleanDays, rCleanSize, rFps;
        static readonly int[] CleanDays = { 7, 14, 30, 90, 0 }, CleanSizes = { 10, 25, 50, 100, 0 };
        Btn bPosition; Row rPosition;
        Seg sShare; Seg sLayout; Field fFolder, fPhrase; Btn bUseFolder, bAddon;
        Seg sLength, sFps, sQuality, sRes, sCapture; Drop dMonitor, dEncoder, dDesktop, dMic; Btn bHotkey, bFfmpeg, bFolder, bOpenFolder, bLog, bAddApp, bBrowse, bUpdate, bReport;
        Slider micVol, sHold; Meter mDesktop, mMic;
        Row rVoice, rPhrase, rAddon, rHold, rSplit, rMemory, rRes, rEncoder, rFfmpeg, rFolder, rDesktopDev, rMicDev, rMicVol, rHotkey, rUpdate;
        Card gamesCard;
        List<AudioDevice> desktopDevs = new List<AudioDevice>(), micDevs = new List<AudioDevice>();
        static readonly int[] Lengths = { 30, 60, 120, 180 };
        static readonly string[] Encoders = { "auto", "nvenc", "amf", "qsv", "x264" };

        public SettingsPage(MainForm m) : base(m)
        {
            content.Page = this; scroll.Page = this;
            pageHead.BackColor = pageSub.BackColor = Color.Transparent;
            scroll.Controls.Add(content); Controls.Add(scroll); Controls.Add(tabs); Controls.Add(pageHead); Controls.Add(pageSub);
            tabs.Changed += delegate { scroll.AutoScrollPosition = Point.Empty; Relayout(); };
            Settings s = Settings.Cur;

            Card c = NewCard("Replay buffer");
            tEnabled = new Toggle(); tEnabled.Changed += delegate { if (!loading) Main.SetEnabled(tEnabled.On); };
            AddRow(c, "Replay buffer", "Keeps the last moments ready to save.", tEnabled, 60, "Keeps the last moments of your screen in memory, ready to save. Turn it off to stop recording completely.").Icon = Glyph.Video;
            sLength = new Seg(new[] { "30 s", "60 s", "2 min", "3 min" });
            sLength.Changed += delegate { if (loading) return; s.BufferSeconds = Lengths[sLength.Index]; Changed(true); };
            AddRow(c, "Clip length", "How much is saved when you press the clip key.", sLength, 300).Icon = Glyph.Clock;
            bHotkey = new Btn("", Glyph.Keyboard, BtnStyle.Standard);
            bHotkey.Click += delegate { StartHotkeyCapture(); };
            rHotkey = AddRow(c, "Clip key", "Click to change.", bHotkey, 170, "Works in any game, and the game still receives the key too."); rHotkey.Icon = Glyph.Keyboard;
            tHold = new Toggle(); tHold.Changed += delegate { if (loading) return; s.HoldClip = tHold.On; Settings.Save(); HoldRows(); Relayout(); };
            AddRow(c, "Hold to clip", "Ignore quick accidental taps.", tHold, 60, "Only saves when you hold the clip key for a moment, so a quick accidental tap doesn't make a clip.").Icon = Glyph.Clock;
            sHold = new Slider(); sHold.Min = 2; sHold.Max = 30; sHold.Format = delegate (int v) { return (v / 10.0).ToString("0.0") + " s"; };
            sHold.Changed += delegate { if (loading) return; s.HoldSeconds = sHold.Value / 10.0; Settings.Save(); };
            rHold = AddRow(c, "Hold for", "How long to hold the clip key before a clip is saved.", sHold, 300);
            tVoice = new Toggle(); tVoice.Changed += delegate { if (loading) return; s.VoiceClips = tVoice.On; Settings.Save(); VoiceRows(); Voice.Sync(Main); Tick(); Relayout(); };
            rVoice = AddRow(c, "Voice clips", "", tVoice, 60); rVoice.Icon = Glyph.Mic;
            fPhrase = new Field(); fPhrase.Placeholder = "Clutch, clip that";
            fPhrase.Box.KeyDown += delegate (object o, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SavePhrase(); Main.Focus(); } };
            fPhrase.Box.LostFocus += delegate { SavePhrase(); };
            rPhrase = AddRow(c, "Voice phrase", "Short, distinct phrases work best.", fPhrase, 260);
            bAddon = new Btn("Download", Glyph.Download, BtnStyle.Standard);
            bAddon.Click += delegate
            {
                if (VoiceAddon.Downloading) VoiceAddon.Cancel();
                else if (VoiceAddon.Ready) { VoiceAddon.Remove(); Voice.Sync(Main); Main.Toast.Show(Glyph.Delete, "Voice add-on removed. Voice clips use Windows speech recognition.", null, null, 4000); }
                else Main.StartVoiceDownload();
                Tick();
            };
            rAddon = AddRow(c, "Voice add-on (more accurate)", "", bAddon, 140); rAddon.Icon = Glyph.Download;
            rMemory = AddRow(c, "Memory use", "", null, 0); rMemory.Icon = Glyph.Info;

            c = NewCard("Video");
            sFps = new Seg(new[] { "30 fps", "60 fps" });
            sFps.Changed += delegate { if (loading) return; s.Fps = sFps.Index == 0 ? 30 : 60; Changed(true); };
            rFps = AddRow(c, "Frame rate", "60 fps looks smoother. 30 fps halves the work and the file size.", sFps, 200);
            sQuality = new Seg(new[] { "Low", "Standard", "High" });
            sQuality.Changed += delegate { if (loading) return; s.Quality = sQuality.Index; Changed(true); };
            AddRow(c, "Quality", "Higher quality means bigger clips and more memory.", sQuality, 300);
            sRes = new Seg(new[] { "Native", "1080p", "720p" });
            sRes.Changed += delegate { if (loading) return; s.Resolution = sRes.Index; Changed(true); };
            rRes = AddRow(c, "Recording resolution", "", sRes, 300);
            dMonitor = new Drop();
            dMonitor.Changed += delegate { if (loading) return; s.Monitor = dMonitor.Index; Changed(true); };
            sCapture = new Seg(new[] { "Game window", "Whole screen" });
            sCapture.Changed += delegate { if (loading) return; s.CaptureMode = sCapture.Index; Settings.Save(); Recorder.Area = Rectangle.Empty; Recorder.Relaunch(); };
            AddRow(c, "What to record", "Just your game, or your whole screen.", sCapture, 300, "Game window records just your game, on whichever screen it's on, and follows it if you move it. Whole screen records your whole screen.").Icon = Glyph.Game;
            rScreen = AddRow(c, "Screen", "Used with Whole screen, or when no game is open.", dMonitor, 300); rScreen.Icon = Glyph.Monitor;
            dEncoder = new Drop(); dEncoder.Items.AddRange(new[] { "Automatic (recommended)", "NVIDIA NVENC", "AMD AMF", "Intel Quick Sync", "Software (CPU)" });
            dEncoder.Changed += delegate { if (loading) return; s.Encoder = Encoders[dEncoder.Index]; Changed(true); };
            rEncoder = AddRow(c, "Encoder", "", dEncoder, 300);
            tAdvanced = new Toggle(); tAdvanced.Changed += delegate { if (loading) return; s.ShowAdvanced = tAdvanced.On; Settings.Save(); ShowAdvanced(); Relayout(); };
            AddRow(c, "Show advanced settings", "Encoder, resolution and screen.", tAdvanced, 60, "Encoder, recording resolution and which screen to record. Most people never need to change these.").Icon = Glyph.Settings;

            c = NewCard("Audio");
            tDesktop = new Toggle(); tDesktop.Changed += delegate { if (loading) return; s.DesktopAudio = tDesktop.On; AudioChanged(); };
            Row r = AddRow(c, "Game and desktop audio", "Everything you hear: the game, Discord, music.", tDesktop, 60); r.Icon = Glyph.Volume;
            dDesktop = new Drop(); dDesktop.Changed += delegate { if (loading) return; s.DesktopDevice = dDesktop.Index <= 0 ? "" : desktopDevs[dDesktop.Index - 1].Id; AudioChanged(); };
            mDesktop = new Meter(); mDesktop.Level = delegate { return Recorder.Desktop.Level; };
            rDesktopDev = AddRow(c, "Output device", "", dDesktop, 300); rDesktopDev.Add2(mDesktop, 90);
            tSplit = new Toggle(); tSplit.Changed += delegate { if (loading) return; s.SplitGameAudio = tSplit.On; AudioChanged(); };
            rSplit = AddRow(c, "Record game audio separately", ProcessLoopback.Supported ? "Mix game, apps and mic when trimming." : "Needs Windows 11, or Windows 10 build 20348 or newer.", tSplit, 60, ProcessLoopback.Supported ? "Saves the game, your other apps (Discord, music) and your mic on separate tracks, so you can mix them when trimming." : null); rSplit.Icon = Glyph.Game;
            tMic = new Toggle(); tMic.Changed += delegate { if (loading) return; s.Mic = tMic.On; AudioChanged(); };
            r = AddRow(c, "Microphone", "Your voice, on its own track.", tMic, 60, "Saved on its own track too, so you can lower or mute it when trimming."); r.Icon = Glyph.Mic;
            dMic = new Drop(); dMic.Changed += delegate { if (loading) return; s.MicDevice = dMic.Index <= 0 ? "" : micDevs[dMic.Index - 1].Id; AudioChanged(); };
            mMic = new Meter(); mMic.Level = delegate { return Recorder.Mic.Level; };
            rMicDev = AddRow(c, "Input device", "", dMic, 300); rMicDev.Add2(mMic, 90);
            micVol = new Slider(); micVol.Max = 200;
            micVol.Changed += delegate { if (loading) return; s.MicVolume = micVol.Value; Settings.Save(); };
            rMicVol = AddRow(c, "Mic volume in clips", "How loud your mic is in the mixed track.", micVol, 300);

            gamesCard = NewCard("Auto-start with games");
            tGamesOnly = new Toggle(); tGamesOnly.Changed += delegate { if (loading) return; s.GamesOnly = tGamesOnly.On; Settings.Save(); Main.Sync(); };
            AddRow(gamesCard, "Only record while a game is running", "Starts the buffer when a game opens.", tGamesOnly, 60, "Clutch waits quietly and starts the buffer when a game opens.").Icon = Glyph.Game;
            tDetect = new Toggle(); tDetect.Changed += delegate { if (loading) return; s.AutoDetectGames = tDetect.On; Settings.Save(); Main.Sync(); };
            AddRow(gamesCard, "Detect games automatically", "Recognises thousands of games for you.", tDetect, 60, "Recognises thousands of games using Discord's public game list, so you don't have to add them. Games you add below always count.").Icon = Glyph.Search;

            c = NewCard("Clips and storage");
            bFolder = new Btn("Browse\u2026", "", BtnStyle.Standard); bFolder.Click += delegate { PickFolder(); };
            bOpenFolder = new Btn("", Glyph.Folder, BtnStyle.Standard);
            bOpenFolder.Click += delegate { try { Directory.CreateDirectory(s.Folder); System.Diagnostics.Process.Start("explorer.exe", "\"" + s.Folder + "\""); } catch { } };
            rFolder = AddRow(c, "Clips folder", "", bFolder, 100); rFolder.Add2(bOpenFolder, 36);
            rFolder.Icon = Glyph.Folder;
            fFolder = new Field(); fFolder.Placeholder = "Paste a folder path, like D:\\Clips"; fFolder.Box.Text = s.Folder;
            fFolder.Box.KeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UseTyped(); }
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; fFolder.Box.Text = Settings.Cur.Folder; }
            };
            bUseFolder = new Btn("Use", "", BtnStyle.Standard); bUseFolder.Click += delegate { UseTyped(); };
            Row rPath = AddRow(c, "Or paste a location", "Paste any folder path and press Enter.", bUseFolder, 70); rPath.Add2(fFolder, 340);
            bFfmpeg = new Btn("", Glyph.Download, BtnStyle.Standard);
            bFfmpeg.Click += delegate { if (Main.Downloading) Main.CancelDownload(); else Main.StartDownload(); };
            rFfmpeg = AddRow(c, "FFmpeg", "", bFfmpeg, 170);
            tClean = new Toggle();
            tClean.Changed += delegate
            {
                if (loading) return; s.AutoClean = tClean.On; Settings.Save(); CleanRows(); Relayout();
                if (s.AutoClean) Main.Toast.Show(Glyph.StarFill, "Auto-clean is on. Favourited clips are never deleted.", null, null, 6000);
            };
            AddRow(c, "Clean up old clips automatically", "Favourites are never deleted.", tClean, 60, "Deletes your oldest clips for good when they pass the limits below. Favourited clips are never deleted, so star anything you want to keep.").Icon = Glyph.Delete;
            sCleanDays = new Seg(new[] { "7 days", "14 days", "30 days", "90 days", "Never" });
            sCleanDays.Changed += delegate { if (loading) return; s.CleanDays = CleanDays[sCleanDays.Index]; Settings.Save(); };
            rCleanDays = AddRow(c, "Delete clips older than", "", sCleanDays, 360);
            sCleanSize = new Seg(new[] { "10 GB", "25 GB", "50 GB", "100 GB", "No limit" });
            sCleanSize.Changed += delegate { if (loading) return; s.CleanGB = CleanSizes[sCleanSize.Index]; Settings.Save(); };
            rCleanSize = AddRow(c, "Keep clips under", "The oldest go first.", sCleanSize, 360, "When your clips pass this size, the oldest are deleted first.");

            c = NewCard("General");
            tStartup = new Toggle(); tStartup.Changed += delegate { if (!loading) Settings.StartWithWindows = tStartup.On; };
            AddRow(c, "Start with Windows", "Opens quietly in the tray when you sign in.", tStartup, 60);
            tTray = new Toggle(); tTray.Changed += delegate { if (loading) return; s.CloseToTray = tTray.On; Settings.Save(); };
            AddRow(c, "Keep running in the tray when closed", "The clip key keeps working.", tTray, 60, "So the clip key keeps working after you close the window.");
            tOverlay = new Toggle(); tOverlay.Changed += delegate { if (loading) return; s.Overlay = tOverlay.On; Settings.Save(); bPosition.Enabled = s.Overlay; };
            AddRow(c, "Show \"Clipped!\" over the game", "Never appears in your clips.", tOverlay, 60, "A small notice that slides in from the edge of the screen. It never appears in your clips.");
            bPosition = new Btn("Move on screen", "", BtnStyle.Standard);
            bPosition.Click += delegate { Main.EditNoticePosition(); Tick(); };
            rPosition = AddRow(c, "Notification position", "", bPosition, 160);
            tSound = new Toggle(); tSound.Changed += delegate { if (loading) return; s.SaveSound = tSound.On; Settings.Save(); };
            AddRow(c, "Play a sound when a clip is saved", "", tSound, 60);
            tDiscord = new Toggle(); tDiscord.Changed += delegate { if (loading) return; s.DiscordStatus = tDiscord.On; Settings.Save(); Main.Sync(); };
            AddRow(c, "Show what you're clipping on Discord", "Shows the game in your Discord status.", tDiscord, 60, "Your Discord status shows \"Clipping Roblox with Clutch\" with the game's icon while Clutch records one of your games.").Icon = Glyph.Discord;
            sShare = new Seg(new[] { "10 MB", "50 MB", "500 MB" });
            sShare.Changed += delegate { if (loading) return; s.DiscordMB = DiscordFit.Sizes[sShare.Index]; Settings.Save(); };
            AddRow(c, "Discord upload size", "10 MB works for everyone.", sShare, 300, "Share on Discord shrinks clips to fit this. 10 MB works for everyone; 50 MB and 500 MB need Nitro.").Icon = Glyph.Discord;
            tSwitcher = new Toggle(); tSwitcher.Changed += delegate { if (loading) return; s.DiscordSwitcher = tSwitcher.On; Settings.Save(); };
            AddRow(c, "Open Discord when sharing", "Pick who to send it to, then Ctrl+V.", tSwitcher, 60, "Brings Discord to the front and opens its search, so you just pick who to send it to and press Ctrl+V.").Icon = Glyph.Search;
            Btn bKeys = new Btn("Show", Glyph.Keyboard, BtnStyle.Standard); bKeys.Click += delegate { ShortcutsWindow.Show_(Main); };
            AddRow(c, "Keyboard shortcuts", "Or press ? anywhere.", bKeys, 110).Icon = Glyph.Keyboard;
            sLayout = new Seg(new[] { "Game chips", "Compact" });
            sLayout.Changed += delegate { if (loading) return; s.GameChips = sLayout.Index == 0; Settings.Save(); Main.Clips.LayoutChanged(); };
            tPreview = new Toggle(); tPreview.Changed += delegate { if (loading) return; s.HoverPreviews = tPreview.On; Settings.Save(); };
            AddRow(c, "Moving previews on hover", "Clips play a quick preview when you rest on them.", tPreview, 60, "Clutch makes a few small frames from a clip the first time you hover over it, and keeps them with the thumbnails. Turn it off to keep things as light as possible.").Icon = Glyph.Play;
            AddRow(c, "Library layout", "How games show above your clips.", sLayout, 220, "Game chips puts a button for each game above your clips, so you can filter with one click. Compact keeps games in a small menu.").Icon = Glyph.Film;

            c = NewCard("About");
            bUpdate = new Btn("Check now", "", BtnStyle.Standard);
            bUpdate.Click += delegate { if (Updater.Available != null) Main.StartUpdate(); else Main.CheckForUpdate(true); Tick(); };
            rUpdate = AddRow(c, "Clutch " + AppInfo.Version, "", Updater.Configured ? bUpdate : null, 150); rUpdate.Icon = Glyph.Download;
            if (Updater.Configured)
            {
                tUpdate = new Toggle(); tUpdate.Changed += delegate { if (loading) return; s.AutoUpdate = tUpdate.On; Settings.Save(); };
                AddRow(c, "Check for updates automatically", "Always asks before installing.", tUpdate, 60, "Clutch quietly checks every few minutes, using almost no data, and asks before installing anything.");
            }
            bReport = new Btn("Create report", "", BtnStyle.Standard);
            bReport.Click += delegate { using (ReportWindow w = new ReportWindow(Report.Build(null), false)) { w.StartPosition = FormStartPosition.CenterParent; w.ShowInTaskbar = false; w.ShowDialog(Main); } };
            bLog = new Btn("Error log", "", BtnStyle.Subtle);
            bLog.Click += delegate { try { if (!File.Exists(Log.FilePath)) File.WriteAllText(Log.FilePath, ""); System.Diagnostics.Process.Start("notepad.exe", "\"" + Log.FilePath + "\""); } catch { } };
            Row rep = AddRow(c, "Report a problem", "A short report you send yourself.", bReport, 130, "Puts your setup and recent errors in a short report, with tips for common problems. You copy it and send it yourself."); rep.Add2(bLog, 90); rep.Icon = Glyph.Warning;
            Btn bLic = new Btn("Licences", "", BtnStyle.Standard);
            bLic.Click += delegate { using (LicencesWindow w = new LicencesWindow()) w.ShowDialog(Main); };
            AddRow(c, "Credits and licences", "No account, no ads, nothing uploaded.", bLic, 110, "Local only: no account, no ads, nothing uploaded. Records with FFmpeg (GPLv3), downloaded separately from " + FFmpeg.Source + ".").Icon = Glyph.Info;
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
            Lbl h = new Lbl(title, 13, 4, C.Text); h.BackColor = Color.Transparent; heads[c] = h; content.Controls.Add(h);
            return c;
        }

        Row AddRow(Card c, string title, string desc, Control right, int w) { return AddRow(c, title, desc, right, w, null); }
        Row AddRow(Card c, string title, string desc, Control right, int w, string tip)
        {
            Row r = new Row(title, desc, right, w); r.BackColor = C.Card; rows[c].Add(r); c.Controls.Add(r);
            if (tip != null) r.SetTip(tip);
            return r;
        }

        void BuildGames()
        {
            List<Row> list = rows[gamesCard];
            for (int i = list.Count - 1; i >= 2; i--) { list[i].Controls.Remove(bAddApp); list[i].Controls.Remove(bBrowse);
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
            sFps.Set(s.Fps == 30 ? 0 : 1); sQuality.Set(s.Quality); sRes.Set(s.Resolution); sCapture.Set(s.CaptureMode);
            dEncoder.Index = Math.Max(0, Array.IndexOf(Encoders, s.Encoder));
            tDesktop.Set(s.DesktopAudio); tVoice.Set(s.VoiceClips); fPhrase.Box.Text = s.VoicePhrase; VoiceRows(); tHold.Set(s.HoldClip); sHold.Value = Math.Max(2, Math.Min(30, (int)Math.Round(s.HoldSeconds * 10))); HoldRows(); tSplit.Set(s.SplitGameAudio && ProcessLoopback.Supported); tSplit.Enabled = ProcessLoopback.Supported && s.DesktopAudio; tMic.Set(s.Mic); micVol.Value = s.MicVolume;
            tGamesOnly.Set(s.GamesOnly);
            tAdvanced.Set(s.ShowAdvanced); tDetect.Set(s.AutoDetectGames); tClean.Set(s.AutoClean);
            sCleanDays.Set(Array.IndexOf(CleanDays, s.CleanDays) < 0 ? 2 : Array.IndexOf(CleanDays, s.CleanDays));
            sCleanSize.Set(Array.IndexOf(CleanSizes, s.CleanGB) < 0 ? 2 : Array.IndexOf(CleanSizes, s.CleanGB));
            ShowAdvanced(); CleanRows();
            tStartup.Set(Settings.StartWithWindows); tTray.Set(s.CloseToTray); tOverlay.Set(s.Overlay); bPosition.Enabled = s.Overlay; tSound.Set(s.SaveSound); tDiscord.Set(s.DiscordStatus); sShare.Set(Math.Max(0, Array.IndexOf(DiscordFit.Sizes, s.DiscordMB))); tSwitcher.Set(s.DiscordSwitcher); sLayout.Set(s.GameChips ? 0 : 1); tPreview.Set(s.HoverPreviews); if (tUpdate != null) tUpdate.Set(s.AutoUpdate);
            dMonitor.Items.Clear();
            Screen[] all = Screen.AllScreens; int n = 1;
            foreach (Screen sc in all) if (sc.Primary) dMonitor.Items.Add("Screen " + n++ + " (main)  \u00B7  " + sc.Bounds.Width + " \u00D7 " + sc.Bounds.Height);
            foreach (Screen sc in all) if (!sc.Primary) dMonitor.Items.Add("Screen " + n++ + "  \u00B7  " + sc.Bounds.Width + " \u00D7 " + sc.Bounds.Height);
            dMonitor.Index = Math.Min(s.Monitor, dMonitor.Items.Count - 1);
            loading = false;
        }

        void ShowAdvanced() { bool a = Settings.Cur.ShowAdvanced; rRes.Hidden = rScreen.Hidden = rEncoder.Hidden = !a; }
        void HoldRows() { rHold.Hidden = !Settings.Cur.HoldClip; }
        void VoiceRows() { rPhrase.Hidden = !Settings.Cur.VoiceClips; }

        void SavePhrase()
        {
            string p = fPhrase.Box.Text.Trim();
            if (Voice.Normalize(p).Length == 0) { p = "Clutch, clip that"; fPhrase.Box.Text = p; }
            if (p == Settings.Cur.VoicePhrase) return;
            Settings.Cur.VoicePhrase = p; Settings.Save(); Voice.Sync(Main); Tick();
        }
        void CleanRows() { rCleanDays.Hidden = rCleanSize.Hidden = !Settings.Cur.AutoClean; }

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
            bool viaCpu = inUse.Length > 0 && Encoder.UsesCpu(inUse.Split(':')[0], inUse.EndsWith(":1"), s.Resolution != 0);
            SetDesc(rFps, "60 fps looks smoother. 30 fps halves the work and the file size." + (viaCpu ? " Your graphics card sends each frame through the CPU, so 30 fps has the smallest effect on game FPS." : ""));
            if (viaCpu)
                enc += " Each frame goes through your CPU, so for the smallest hit to game FPS use 30 fps.";
            SetDesc(rEncoder, enc);
            if ((DateTime.Now - freeAt).TotalSeconds > 10)
            try { freeAt = DateTime.Now; free = "  \u00B7  " + (new DriveInfo(Path.GetPathRoot(s.Folder)).AvailableFreeSpace >> 30) + " GB free"; } catch { }
            SetDesc(rFolder, s.Folder + free);
            if (!fFolder.Box.Focused && fFolder.Box.Text != s.Folder) fFolder.Box.Text = s.Folder;
            string ff = Main.Downloading ? "Downloading\u2026 " + Main.DownloadPercent + "%" : FFmpeg.Ready ? "Installed" + (FFmpeg.Version.Length > 0 ? " (" + FFmpeg.Version + ")" : "") + " from " + FFmpeg.Source + "." : Main.DownloadError.Length > 0 ? Main.DownloadError : "Not installed yet. Needed for recording.";
            SetDesc(rFfmpeg, ff);
            SetDesc(rPosition, NoticeSpot.Describe(s.OverlaySide, s.OverlaySide >= 2 ? s.OverlayX : s.OverlayY) + ". Drag it to any edge of your actual screen.");
            Updater.Release up = Updater.Available;
            string said = "\u201C" + (s.VoicePhrase.Trim().Length > 0 ? s.VoicePhrase.Trim() : "Clutch, clip that") + "\u201D";
            string how = VoiceAddon.Ready ? "Say " + said + " to save a clip."
                : "Say " + said + " to save a clip. Windows speech can miss you in loud games; the add-on below is more accurate.";
            rVoice.SetTip(VoiceAddon.Ready ? "Uses the voice add-on, which works offline and handles noisy games well."
                : "Uses Windows\u2019 built-in speech recognition, which may not hear you properly in loud games or with some accents. The voice add-on below is more accurate.");
            SetDesc(rVoice, how + (s.VoiceClips && Voice.State.Length > 0 ? "\nNow: " + Voice.State : ""));
            SetDesc(rAddon, VoiceAddon.Downloading ? "Downloading the voice add-on\u2026 " + VoiceAddon.Percent + "%"
                : VoiceAddon.Ready ? "Installed. Remove it to free about 70 MB."
                : (VoiceAddon.Error.Length > 0 ? VoiceAddon.Error + " " : "") + "Optional, about 56 MB. More accurate, and works offline.");
            rAddon.SetTip(VoiceAddon.Ready ? "Voice clips use it instead of Windows speech recognition."
                : "An offline speech engine (Vosk) that\u2019s more accurate than Windows\u2019 built-in one, especially in noisy games. Nothing you say ever leaves your PC.");
            string at = VoiceAddon.Downloading ? "Cancel" : VoiceAddon.Ready ? "Remove" : "Download";
            if (bAddon.Text != at) { bAddon.Text = at; bAddon.Icon = at == "Download" ? Glyph.Download : at == "Remove" ? Glyph.Delete : ""; bAddon.Invalidate(); }
            SetDesc(rUpdate, !Updater.Configured ? "Updates aren't switched on in this build." : Updater.Checking ? "Checking for updates\u2026"
                : up != null ? "Version " + up.Version + " is ready to install. Clutch closes, updates and opens again in a few seconds."
                : Updater.LastError.Length > 0 ? Updater.LastError
                : Updater.LastCheck == DateTime.MinValue ? "Not checked yet." : "You're on the latest version (checked " + Updater.LastCheck.ToString("HH:mm") + ").");
            string ut = up != null ? "Update to " + up.Version : "Check now";
            if (bUpdate.Text != ut) { bUpdate.Text = ut; bUpdate.Style = up != null ? BtnStyle.Accent : BtnStyle.Standard; bUpdate.Weight = up != null ? 5 : 1; bUpdate.Invalidate(); }
            string bt = Main.Downloading ? "Cancel" : FFmpeg.Ready ? "Re-download" : "Download";
            if (bFfmpeg.Text != bt) bFfmpeg.Text = bt;
            if (!Main.CapturingHotkey && bHotkey.Text != MainForm.HotkeyName()) bHotkey.Text = MainForm.HotkeyName();
            SetDesc(rDesktopDev, Recorder.Desktop.Running ? (Recorder.Desktop.Error.Length > 0 ? "Problem: " + Recorder.Desktop.Error : "Recording from " + Recorder.Desktop.DeviceName) : Idle());
            SetDesc(rMicDev, Recorder.Mic.Running ? (Recorder.Mic.Error.Length > 0 ? "Problem: " + Recorder.Mic.Error : "Recording from " + Recorder.Mic.DeviceName) : Idle());
            rDesktopDev.Enabled = s.DesktopAudio; rMicDev.Enabled = s.Mic; rMicVol.Enabled = s.Mic;
            if (Recorder.Desktop.Running) mDesktop.Invalidate();
            if (Recorder.Mic.Running) mMic.Invalidate();
        }

        static string Idle()
        {
            Settings s = Settings.Cur;
            if (!s.Enabled) return "The level bar shows sound while recording. The replay buffer is off.";
            if (s.GamesOnly && Games.Running == "") return "The level bar shows sound while recording. Clutch is waiting for one of your games to open.";
            return "The level bar shows sound while recording.";
        }

        void SetDesc(Row r, string d)
        {
            if (r.Desc == d) return;
            int before = r.Fit(); r.Desc = d;
            r.Invalidate(); if (r.Fit() != before) Relayout();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.Draw(e.Graphics, ClientSize, Point.Empty); }

        public override void Relayout()
        {
            int sb = SystemInformation.VerticalScrollBarWidth;
            int w = Math.Min(S(860), ClientSize.Width - S(56) - sb);
            int x = Math.Max(S(28), (ClientSize.Width - sb - w) / 2), y = S(8);
            int hw = Ui.Measure(pageHead.Text, Ui.F(24, 4)).Width + S(4);
            pageHead.SetBounds(x - S(2), S(10), hw, S(48)); pageSub.SetBounds(pageHead.Right + S(12), S(18), Math.Max(0, w - hw - S(12)), S(40));
            tabs.SetBounds(x, S(70), w, S(40));
            scroll.SetBounds(0, S(124), ClientSize.Width, ClientSize.Height - S(124));
            for (int ci = 0; ci < cards.Count; ci++)
            {
                Card c = cards[ci];
                bool show = ci >= TabFirstCard[tabs.Index] && ci <= TabLastCard[tabs.Index];
                c.Visible = heads[c].Visible = show;
                if (!show) continue;
                Lbl h = heads[c]; h.SetBounds(x + S(4), y, w, S(28)); y += S(36);
                int cy = S(4);
                List<Row> rl = rows[c];
                Row lastShown = null; foreach (Row r in rl) if (!r.Hidden) lastShown = r;
                foreach (Row r in rl)
                {
                    r.Visible = !r.Hidden; if (r.Hidden) continue;
                    r.Line = r != lastShown; r.Width = w - S(8); int rh = r.Fit(); r.SetBounds(S(4), cy, w - S(8), rh); cy += rh;
                }
                c.SetBounds(x, y, w, cy + S(4)); y += cy + S(4) + S(28);
            }
            content.SetBounds(0, content.Top, ClientSize.Width - SystemInformation.VerticalScrollBarWidth, y + S(10));
        }

        static readonly string[] TabNames = { "Recording", "Audio", "Games", "Storage", "General" };

        public class Hit { public string Title, Where; public Row Row; public int Tab; }

        public List<Hit> Find(string query)
        {
            List<Hit> o = new List<Hit>(), weak = new List<Hit>();
            string q = query.Trim();
            if (q.Length < 2) return o;
            for (int ci = 0; ci < cards.Count; ci++)
            {
                int tab = 0; for (int t = 0; t < TabFirstCard.Length; t++) if (ci >= TabFirstCard[t] && ci <= TabLastCard[t]) tab = t;
                foreach (Row r in rows[cards[ci]])
                {
                    if (r.Title.Length == 0 || r.Hidden) continue;
                    Hit h = new Hit(); h.Title = r.Title; h.Row = r; h.Tab = tab; h.Where = TabNames[tab];
                    if (r.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) o.Add(h);
                    else if (q.Length >= 3 && (r.Desc + " " + r.Tip).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) weak.Add(h);
                }
            }
            o.AddRange(weak);
            return o.Count > 5 ? o.GetRange(0, 5) : o;
        }

        public void Reveal(Hit h)
        {
            tabs.Index = h.Tab; Relayout();
            int y = h.Row.Top + h.Row.Parent.Top - S(16);
            scroll.PerformLayout(); scroll.AutoScrollPosition = new Point(0, Math.Max(0, y)); scroll.Invalidate(true);
            BeginInvoke((Action)delegate { scroll.AutoScrollPosition = new Point(0, Math.Max(0, y)); scroll.Invalidate(true); });
            Color was = h.Row.BackColor; Row row = h.Row;
            row.BackColor = Ui.Mix(C.Card, C.Accent, 0.25f); row.Invalidate(true);
            Timer t = new Timer(); t.Interval = 1400;
            t.Tick += delegate { t.Stop(); t.Dispose(); row.BackColor = was; row.Invalidate(true); };
            t.Start();
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
            string p = FolderPicker.Pick(FindForm(), "Where should Clutch save your clips?", Settings.Cur.Folder);
            if (p == null) return;
            ClipFolder.Change(Main, p);
            fFolder.Box.Text = Settings.Cur.Folder; Tick();
        }

        void UseTyped()
        {
            if (ClipFolder.Change(Main, fFolder.Box.Text)) { fFolder.Box.Text = Settings.Cur.Folder; Main.Focus(); }
            Tick();
        }
    }
}
