using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Clutch
{
    class Page : Panel
    {
        protected readonly MainForm Main;
        public Page(MainForm m)
        {
            Main = m; BackColor = C.Bg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        protected static int S(float v) { return Ui.S(v); }
        public virtual void Relayout() { }
        public virtual void Entered() { }
        public virtual void Leaving() { }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }
    }

    partial class MainForm : Form
    {
        public readonly TopBar Bar;
        public readonly NavRail Rail;
        public ClipsPage Clips; public GamesPage GamesP; public ViewerPage Viewer; public TrimPage Trim; public SettingsPage SettingsP; public WelcomePage Welcome;
        Page current;
        public readonly Toast Toast = new Toast();
        NotifyIcon tray; TrayItem trayToggle, traySave, trayUpdate, trayGame;
        readonly System.Windows.Forms.Timer gameTimer = new System.Windows.Forms.Timer(), uiTimer = new System.Windows.Forms.Timer(), cleanTimer = new System.Windows.Forms.Timer();
        bool quitting, closing, saving, lastWant, checkingGames;
        int gameMisses;
        public bool CapturingHotkey;
        Icon iconOn, iconOff;

        public volatile bool Downloading; public volatile int DownloadPercent; public string DownloadError = "";
        volatile bool cancelDownload;

        static int S(float v) { return Ui.S(v); }

        public MainForm()
        {
            Text = "Clutch"; AutoScaleMode = AutoScaleMode.None; BackColor = C.Bg; ForeColor = C.Text;
            Font = Ui.F(10, 0); KeyPreview = true; DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(1180), S(760)); MinimumSize = new Size(S(880), S(580));
            iconOn = Ui.MakeIcon(C.Accent, 32); iconOff = Ui.MakeIcon(C.Faint, 32);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon = iconOn; }

            Bar = new TopBar(this); Controls.Add(Bar);
            Rail = new NavRail(this); Controls.Add(Rail);
            Clips = new ClipsPage(this); GamesP = new GamesPage(this); Viewer = new ViewerPage(this); Trim = new TrimPage(this); SettingsP = new SettingsPage(this); Welcome = new WelcomePage(this);
            foreach (Page p in new Page[] { Clips, GamesP, Viewer, Trim, SettingsP, Welcome }) { p.Visible = false; Controls.Add(p); }
            Controls.Add(Toast);
            Ui.ScaleChanged += delegate { Font = Ui.F(10, 0); PerformLayout(); foreach (Control c in Controls) { c.PerformLayout(); Page p = c as Page; if (p != null) p.Relayout(); } Invalidate(true); };

            Library.Attach(this);
            Library.Changed += delegate { Clips.Refresh(); if (current == GamesP) GamesP.Refresh(); Rail.Remeasure(); };
            GameIcons.Changed += delegate { Clips.Invalidate(true); GamesP.Invalidate(true); if (current == SettingsP) SettingsP.Reload(); };
            Recorder.Changed += delegate { try { BeginInvoke((MethodInvoker)StatusChanged); } catch { } };

#if DEV
            if (Program.Demo) Demo.Fill(); else
#endif
            Library.Load();
            Go(Clips);
            if (!Settings.Cur.WelcomeDone)
            {
                if (FFmpeg.Ready) { Settings.Cur.WelcomeDone = true; Settings.Save(); }
                else Go(Welcome);
            }

            gameTimer.Interval = 3000; gameTimer.Tick += delegate { CheckGames(); };
            uiTimer.Interval = 500; uiTimer.Tick += delegate { UiTick(); };
            uiTimer.Start();
            if (!Program.DevRun) MakeTray();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Bar == null || Rail == null) return;
            int rw = S(76), th = S(TopBar.Tall);
            Rail.SetBounds(0, 0, rw, ClientSize.Height);
            Bar.SetBounds(rw, 0, ClientSize.Width - rw, th);
            foreach (Control c in Controls) if (c is Page) c.SetBounds(rw, th, ClientSize.Width - rw, ClientSize.Height - th);
            if (Welcome != null) Welcome.SetBounds(0, 0, ClientSize.Width, ClientSize.Height);
            if (Toast.Visible) Toast.Location = new Point((ClientSize.Width - Toast.Width) / 2, ClientSize.Height - Toast.Height - S(24));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.StyleTitleBar(this, C.Chrome);
            try { float dpi = Native.GetDpiForWindow(Handle) / 96f; if (dpi > 0.5f && Math.Abs(dpi - Ui.Scale) > 0.01f) { Ui.Scale = dpi; ClientSize = new Size(S(1180), S(760)); } } catch { }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (Program.StartHidden && !IsHandleCreated) { CreateHandle(); value = false; OnStartup(); }
            base.SetVisibleCore(value);
        }

        bool started;
        void OnStartup()
        {
            if (started) return; started = true;
            RegisterHotkey();
            CheckGames();
            Sync();
            gameTimer.Start();
            cleanTimer.Interval = 20000;
            cleanTimer.Tick += delegate { cleanTimer.Interval = 3600000; CleanUp(); };
            cleanTimer.Start();
            StartUpdateChecks();
            if (Program.JustUpdated) Toast.Show(Glyph.Check, "Updated to Clutch " + AppInfo.Version, null, null, 5000);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            OnStartup();
#if DEV
            DevTools.Shown(this);
#endif
        }

        public void Go(Page p)
        {
            if (current == p) return;
            bool leftVideo = (current == Viewer || current == Trim) && p != Viewer && p != Trim;
            if (current != null) { current.Leaving(); current.Visible = false; }
            if (leftVideo) TrimSoon();
            Bar.SetTools(p == Clips ? Clips.Tools : null);
            current = p; p.Visible = true; p.Relayout(); p.Entered(); p.BringToFront();
            Toast.BringToFront();
            Bar.Invalidate();
            Rail.Invalidate();
        }
        public Page Current { get { return current; } }

        public void SearchChanged(string text)
        {
            if (text.Trim().Length > 0 && current != Clips) Go(Clips);
            Clips.Refresh();
        }

        protected override void WndProc(ref Message m)
        {
            if (Ui.HandleDpiChanged(this, ref m)) return;
            if (m.Msg == SingleInstance.ShowMessage && m.Msg != 0) { ShowWindowNow(); return; }
            if (m.Msg == SingleInstance.QuitMessage && m.Msg != 0) { Quit(); return; }
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == 1) { ClipKeyPressed(); return; }
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode; bool ctrl = (keyData & Keys.Control) != 0, alt = (keyData & Keys.Alt) != 0;
            Control f = ActiveControl; while (f is ContainerControl && ((ContainerControl)f).ActiveControl != null) f = ((ContainerControl)f).ActiveControl;
            if (ctrl && !alt && (k == Keys.K || k == Keys.F) && !CapturingHotkey && current != Welcome) { Bar.FocusSearch(); return true; }
            if (k == Keys.OemQuestion && !ctrl && !alt && !CapturingHotkey && !(f is TextBox) && current != Welcome) { ShortcutsWindow.Show_(this); return true; }
            if (!CapturingHotkey && !alt && !(f is TextBox))
            {
                bool handled = false;
                if (current == Viewer) handled = Viewer.Key(k, ctrl);
                else if (current == Trim) handled = Trim.Key(k);
                else if (current == Clips)
                {
                    if (ctrl && k == Keys.A) { Clips.SelectAll(); handled = true; }
                    else if (k == Keys.Escape) handled = Clips.ClearSelection();
                    else if (k == Keys.Delete) { Clips.DeleteSelected(); handled = true; }
                }
                if (handled) return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (CapturingHotkey) { SettingsP.HotkeyKey(e); e.Handled = true; e.SuppressKeyPress = true; return; }
            base.OnKeyDown(e);
        }

        public void RegisterHotkey() { ClipKey.Start(this); }
        public void UnregisterHotkey() { ClipKey.Pause(); }

        public static string HotkeyName() { return KeyName(Settings.Cur.HotkeyVk, Settings.Cur.HotkeyMods); }
        public static string KeyName(int vk, int mods)
        {
            string s = "";
            if ((mods & 2) != 0) s += "Ctrl+";
            if ((mods & 1) != 0) s += "Alt+";
            if ((mods & 4) != 0) s += "Shift+";
            if ((mods & 8) != 0) s += "Win+";
            Keys k = (Keys)vk; string n = k.ToString();
            if (k >= Keys.D0 && k <= Keys.D9) n = n.Substring(1);
            if (k == Keys.Oemtilde) n = "`"; if (k == Keys.Next) n = "PageDown"; if (k == Keys.Prior) n = "PageUp";
            return s + n;
        }

        void CheckGames()
        {
            if (checkingGames) return; checkingGames = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string before = Games.Running, beforeExe = Games.RunningExe; int beforePid = Games.RunningPid;
                try { Games.Check(); } catch { }
                if (Games.Running.Length > 0) gameMisses = 0;
                else if (before.Length > 0 && ++gameMisses < 2) { Games.Running = before; Games.RunningExe = beforeExe; Games.RunningPid = beforePid; }
                try { Games.UpdateCurrent(); } catch { }
                try { Recorder.SetGame(Games.RunningPid); } catch (Exception ex) { Log.Info("Game audio", ex.Message); }
                Rectangle? area = null; int pid = 0;
                try { area = Games.WindowArea(out pid); } catch { }
                if (!area.HasValue && areaPid != 0 && !Games.Alive(areaPid)) { area = Rectangle.Empty; pid = 0; }
                try { BeginInvoke((MethodInvoker)delegate { checkingGames = false; Sync(); Follow(area, pid); }); } catch { }
            });
        }

        Rectangle pendingArea = Rectangle.Empty; int pendingSeen, areaPid;
        void Follow(Rectangle? seen, int pid)
        {
            if (!seen.HasValue || Settings.Cur.CaptureMode != 0 || Recorder.RegionBroken) return;
            Rectangle a = seen.Value;
            if (Near(a, Recorder.Area)) areaPid = pid;
            if (Near(a, Recorder.Area)) { pendingSeen = 0; return; }
            if (Near(a, pendingArea)) pendingSeen++; else { pendingArea = a; pendingSeen = 1; }
            if (pendingSeen < 2) return;
            pendingSeen = 0;
            Recorder.Area = a; areaPid = pid;
            Recorder.Relaunch();
        }

        static bool Near(Rectangle a, Rectangle b)
        {
            if (a.IsEmpty || b.IsEmpty) return a.IsEmpty && b.IsEmpty;
            return Math.Abs(a.X - b.X) <= 8 && Math.Abs(a.Y - b.Y) <= 8 && Math.Abs(a.Width - b.Width) <= 8 && Math.Abs(a.Height - b.Height) <= 8;
        }

        public bool WantRecording { get { return FFmpeg.Ready && Settings.Cur.Enabled && (!Settings.Cur.GamesOnly || Games.Running != ""); } }

        public void Sync()
        {
            bool want = WantRecording;
            if (want != lastWant)
            {
                lastWant = want;
                if (want) Recorder.Start(); else Recorder.Stop();
            }
            StatusChanged();
        }

        public void RestartRecorder()
        {
            Recorder.EncoderUsed = "";
            if (lastWant) { Recorder.Stop(); Recorder.Start(); }
        }

        string discordGame = ""; DateTime discordSince;
        void StatusChanged()
        {
            Bar.Invalidate();
            bool rec = Recorder.Now == Recorder.State.Recording;
            if (tray != null && !IsDisposed && !closing)
            {
                tray.Icon = rec ? iconOn : iconOff;
                string tip = "Clutch - " + Bar.StatusText();
                tray.Text = tip.Length > 63 ? tip.Substring(0, 60) + "..." : tip;
                if (trayToggle != null) { trayToggle.Right = Settings.Cur.Enabled ? "On" : "Off"; trayToggle.RightColor = Settings.Cur.Enabled ? C.Good : C.Faint; }
            }
            string g = rec ? Games.Current : "";
            if (g != discordGame) { discordGame = g; discordSince = DateTime.Now; }
            DiscordPresence.Update(g, Games.CurrentExe, discordSince);
            if (current == SettingsP) SettingsP.Tick();
            if (current == Welcome) Welcome.Tick();
        }

        public void ShowStatusMenu(Control c, Point at)
        {
            Settings s = Settings.Cur; ContextMenuStrip m = Menus.Make();
            ToolStripMenuItem head = new ToolStripMenuItem(Bar.StatusText()); head.Enabled = false; m.Items.Add(head);
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem on = new ToolStripMenuItem("Replay buffer on"); on.Checked = s.Enabled; on.Click += delegate { SetEnabled(!s.Enabled); }; m.Items.Add(on);
            ToolStripMenuItem only = new ToolStripMenuItem("Only record while a game is running"); only.Checked = s.GamesOnly;
            only.Click += delegate { s.GamesOnly = !s.GamesOnly; Settings.Save(); CheckGames(); SettingsP.Reload(); };
            m.Items.Add(only);
            m.Items.Add(new ToolStripSeparator());
            Menus.Item(m, "Choose games\u2026", Glyph.Game, delegate { Go(SettingsP); SettingsP.ScrollTo("settings-games"); });
            m.Show(c, at);
        }

        public void SetEnabled(bool on)
        {
            Settings.Cur.Enabled = on; Settings.Save(); Sync();
            Bar.SyncToggle();
        }

        System.Windows.Forms.Timer holdTimer; long holdStart;

        public void ClipKeyPressed()
        {
            if (!Settings.Cur.HoldClip || Settings.Cur.HoldSeconds < 0.05) { SaveClip(); return; }
            if (holdTimer != null && holdTimer.Enabled) return;
            holdStart = System.Diagnostics.Stopwatch.GetTimestamp();
            if (holdTimer == null)
            {
                holdTimer = new System.Windows.Forms.Timer(); holdTimer.Interval = 30;
                holdTimer.Tick += delegate
                {
                    if ((Native.GetAsyncKeyState(Settings.Cur.HotkeyVk) & 0x8000) == 0) { holdTimer.Stop(); return; }
                    double held = (System.Diagnostics.Stopwatch.GetTimestamp() - holdStart) / (double)System.Diagnostics.Stopwatch.Frequency;
                    if (held >= Settings.Cur.HoldSeconds) { holdTimer.Stop(); SaveClip(); }
                };
            }
            holdTimer.Start();
        }

        public void SaveClip()
        {
            Rectangle screen = NoticeScreen();
            if (saving) { if (Settings.Cur.Overlay) SavedOverlay.Pop("Saving\u2026", "Your last clip is still being saved", true, screen); return; }
            if (Recorder.Now != Recorder.State.Recording)
            {
                string why = !FFmpeg.Ready ? "Finish the one-time setup in Clutch first" : !Settings.Cur.Enabled ? "The replay buffer is off" : Recorder.Now == Recorder.State.Starting ? "Still starting, try again in a moment" : Settings.Cur.GamesOnly && Games.Running == "" ? "Waiting for one of your games" : "Recording isn't running";
                if (Settings.Cur.Overlay) SavedOverlay.Pop("Couldn't save a clip", why, false, screen);
                else Toast.Show(Glyph.Warning, why, null, null, 4000);
                return;
            }
            saving = true;
            string game = Games.ForClip();
            int seconds = Settings.Cur.BufferSeconds;
            double held = Math.Min(seconds, Recorder.SecondsHeld);
            if (Settings.Cur.Overlay) SavedOverlay.Pop("Clipped!", "Clipped " + (game.Length > 0 ? game + "  \u00B7  " : "") + Math.Max(1, Math.Round(held)) + "s", true, screen);
            if (Settings.Cur.SaveSound) Sfx.Saved();
            ThreadPool.QueueUserWorkItem(delegate
            {
                Recorder.SaveResult r = Recorder.Save(seconds, game);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        saving = false;
                        if (r.Ok)
                        {
                            Clip c = Library.Add(r.Path, game, r.Seconds);
                            CleanUp();
                            Toast.Show(Glyph.Check, "Saved \"" + c.Title + "\"", "Open", delegate { OpenClip(c); }, 4000);
                        }
                        else
                        {
                            if (Settings.Cur.Overlay) SavedOverlay.Pop("Couldn't save the clip", r.Error, false, screen);
                            Toast.Show(Glyph.Warning, r.Error, null, null, 5000);
                        }
                    });
                }
                catch { saving = false; }
            });
        }

        public void OpenClip(Clip c) { ShowWindowNow(); Viewer.Open(c); Go(Viewer); }
        public void TrimClip(Clip c) { ShowWindowNow(); Trim.Open(c); Go(Trim); }

        public void DeleteClips(List<Clip> clips)
        {
            if (clips.Count == 0) return;
            foreach (Clip c in clips)
            {
                if (Viewer.Clip == c) { Viewer.Video.Close(); if (current == Viewer) Go(Clips); }
                if (Trim.Clip == c) { Trim.Video.Close(); if (current == Trim) Go(Clips); }
                Library.Delete(c);
            }
            string what = clips.Count == 1 ? "\"" + clips[0].Title + "\"" : clips.Count + " clips";
            Toast.Show(Glyph.Delete, "Deleted " + what, "Undo", delegate { for (int i = clips.Count - 1; i >= 0; i--) Library.Undo(clips[i]); }, 8000);
        }

        public void ToggleFavorite(Clip c)
        {
            Library.SetFavorite(c, !c.Favorite);
            if (current == Viewer) Viewer.Invalidate(true);
        }

        public void ChangeGame(Clip c)
        {
            string before = c.Game;
            string g = RenameDialog.AskGame(this, before, Library.KnownGames());
            if (g == null || g == before) return;
            Library.SetGame(c, g);
            if (current == GamesP) GamesP.Refresh();
            Toast.Show(Glyph.Game, g.Length > 0 ? "Moved to " + Games.Clean(g) : "Removed the game", "Undo", delegate { Library.SetGame(c, before); if (current == GamesP) GamesP.Refresh(); }, 8000);
        }

        public void CleanUp()
        {
            int n;
            long freed = Library.CleanUp(delegate (Clip c) { return (current == Viewer && Viewer.Clip == c) || (current == Trim && Trim.Clip == c); }, out n);
            if (n > 0) Toast.Show(Glyph.Delete, "Auto-clean deleted " + n + " old " + (n == 1 ? "clip" : "clips") + " (" + Library.NiceSize(freed) + ")", null, null, 5000);
        }

        public void RenameClip(Clip c)
        {
            string oldTitle = c.Title;
            string t = RenameDialog.Ask(this, oldTitle);
            if (t != null) ApplyRename(c, t);
        }

        public bool ApplyRename(Clip c, string t)
        {
            string oldTitle = c.Title;
            if (t == null || t.Trim().Length == 0 || t.Trim() == oldTitle) return true;
            t = t.Trim();
            bool open = Viewer.Clip == c && current == Viewer;
            double pos = open ? Viewer.Video.Position : 0;
            if (Viewer.Clip == c) Viewer.Video.Close();
            string err = Library.Rename(c, t);
            if (open) { Viewer.Open(c); if (pos > 0.2) Viewer.Video.Position = pos; }
            if (err != null) { Toast.Show(Glyph.Warning, err, null, null, 5000); return false; }
            Viewer.Invalidate(true);
            Toast.Show(Glyph.Rename, "Renamed to \"" + c.Title + "\"", "Undo", delegate
            {
                bool o2 = Viewer.Clip == c && current == Viewer;
                if (Viewer.Clip == c) Viewer.Video.Close();
                Library.Rename(c, oldTitle);
                if (o2) Viewer.Open(c);
                Viewer.Invalidate(true);
            }, 8000);
            return true;
        }

        public void OpenClipsFolder()
        {
            try { Directory.CreateDirectory(Settings.Cur.Folder); System.Diagnostics.Process.Start("explorer.exe", "\"" + Settings.Cur.Folder + "\""); } catch { }
        }

        public void CopyClips(List<Clip> clips)
        {
            try
            {
                System.Collections.Specialized.StringCollection sc = new System.Collections.Specialized.StringCollection();
                foreach (Clip c in clips) sc.Add(c.Path);
                Clipboard.SetFileDropList(sc);
                Toast.Show(Glyph.Check, (clips.Count == 1 ? "Copied" : "Copied " + clips.Count + " clips") + ". Paste into Discord with Ctrl+V.", null, null, 3500);
            }
            catch (Exception ex) { Log.Error("Copy clip", ex); }
        }

        public void ShowInFolder(Clip c)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + c.Path + "\""); } catch { }
        }

        public void StartDownload()
        {
            if (Downloading) return;
            Downloading = true; DownloadPercent = 0; DownloadError = ""; cancelDownload = false;
            Clips.Refresh(); SettingsP.Tick();
            Thread t = new Thread(delegate ()
            {
                string err = "";
                try { FFmpeg.Download(delegate (long done, long total) { DownloadPercent = total > 0 ? (int)(done * 100 / total) : 0; }, delegate { return cancelDownload; }); }
                catch (OperationCanceledException) { err = "Download cancelled."; }
                catch (Exception ex) { err = ex is System.Net.WebException ? "Couldn't reach " + FFmpeg.Source + ". Check your internet connection and try again." : ex.Message; Log.Error("FFmpeg download", ex); }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        Downloading = false; DownloadError = err;
                        if (err == "") { Toast.Show(Glyph.Check, "FFmpeg is ready. The replay buffer is starting.", null, null, 4000); Library.Rescan(); RestartRecorder(); Sync(); }
                        Clips.Refresh(); SettingsP.Tick(); SettingsP.Relayout();
                    });
                }
                catch { }
            });
            t.IsBackground = true; t.Start();
        }
        public void CancelDownload() { cancelDownload = true; }

#if DEV
        public ContextMenuStrip DevTrayMenu() { if (tray == null) MakeTray(); return tray.ContextMenuStrip; }
#endif
        void MakeTray()
        {
            tray = new NotifyIcon(); tray.Icon = iconOff; tray.Text = "Clutch"; tray.Visible = true;
            ContextMenuStrip m = TrayMenu.Make();
            TrayMenu.Add(m, "Open Clutch", "\uE80F", ShowWindowNow);
            traySave = TrayMenu.Add(m, "Save clip", "\uE714", SaveClip);
            TrayMenu.Add(m, "Clips folder", Glyph.Folder, OpenClipsFolder);
            TrayMenu.Add(m, "Settings", "\uE713", delegate { ShowWindowNow(); Go(SettingsP); });
            TrayMenu.Line(m);
            trayToggle = TrayMenu.Add(m, "Replay buffer", "", delegate { SetEnabled(!Settings.Cur.Enabled); });
            trayGame = TrayMenu.Add(m, "Game", "", ShowWindowNow);
            trayUpdate = TrayMenu.Add(m, "Update available", "", StartUpdate); trayUpdate.Visible = false; trayUpdate.Badge = true; trayUpdate.RightColor = C.Accent;
            TrayMenu.Line(m);
            TrayMenu.Add(m, "Quit Clutch", "", Quit);
            m.Opening += delegate
            {
                traySave.Right = HotkeyName();
                bool on = Settings.Cur.Enabled, rec = Recorder.Now == Recorder.State.Recording;
                trayToggle.Right = !on ? "Off" : rec ? "On" : "Starting"; trayToggle.RightColor = !on ? C.Faint : rec ? C.Good : C.Warn;
                trayGame.Right = Games.Running.Length > 0 ? Games.Running : "None open"; trayGame.RightColor = Games.Running.Length > 0 ? C.Text : C.Faint;
                Updater.Release r = Updater.Available;
                trayUpdate.Visible = r != null; if (r != null) trayUpdate.Right = r.Version;
            };
            tray.ContextMenuStrip = m;
            tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowWindowNow(); };
        }

        string lastStatus = "";
        void UiTick()
        {
            if (!Visible || WindowState == FormWindowState.Minimized) return;
            string st = Bar.StatusText() + "|" + Settings.Cur.BufferSeconds + "|" + Settings.Cur.Fps + "|" + HotkeyName();
            if (st != lastStatus) { lastStatus = st; Bar.Invalidate(); }
            Rail.Tick();
            if (current == SettingsP) SettingsP.Tick();
        }

        void FreeMemory() { Clips.FreeMemory(); TrimMemory(); }

        static void TrimMemory()
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            try { Native.SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1)); } catch { }
        }

        void TrimSoon()
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 1200;
            t.Tick += delegate { t.Stop(); t.Dispose(); if (current != Viewer && current != Trim) TrimMemory(); };
            t.Start();
        }

        public void ShowWindowNow()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate(); Native.SetForegroundWindow(Handle);
        }

        public void Quit() { quitting = true; Close(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!quitting && !Program.DevRun && Settings.Cur.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; Hide();
                FreeMemory();
                if (current == Viewer) Viewer.Video.Pause();
                if (current == Trim) Trim.Video.Pause();
                if (!Settings.Cur.TrayHintShown && tray != null)
                {
                    Settings.Cur.TrayHintShown = true; Settings.Save();
                    tray.ShowBalloonTip(4000, "Clutch is still running", "The replay buffer keeps going in the tray. Right-click the icon to quit.", ToolTipIcon.None);
                }
                return;
            }
            base.OnFormClosing(e);
            closing = true;
            DiscordPresence.Stop();
            ClipKey.Stop();
            Recorder.Stop();
            Viewer.Video.Close(); Trim.Video.Close();
            Library.Flush(true);
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
        }

        public void HideTray() { if (tray != null) tray.Visible = false; }

        readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer(), newsTimer = new System.Windows.Forms.Timer();
        void StartUpdateChecks()
        {
            if (!Updater.Configured || Program.DevRun) return;
            updateTimer.Interval = 15000;
            updateTimer.Tick += delegate
            {
                updateTimer.Interval = 5 * 60 * 1000;
                if (Settings.Cur.AutoUpdate) CheckForUpdate(false);
            };
            updateTimer.Start();
            newsTimer.Interval = 12000;
            newsTimer.Tick += delegate
            {
                newsTimer.Interval = 5 * 60 * 1000;
                Licence.CheckRevokedAsync(this, delegate { Program.InviteRevoked = true; Quit(); });
                Announce.CheckAsync(this, ShowAnnouncement);
            };
            newsTimer.Start();
        }

        public void CheckForUpdate(bool manual)
        {
            string before = Updater.Available != null ? Updater.Available.Version : "";
            Updater.CheckAsync(this, delegate
            {
                Updater.Release r = Updater.Available;
                if (r != null && (manual || r.Version != before)) Toast.Show(Glyph.Download, "Clutch " + r.Version + " is available", "Update", StartUpdate, manual ? 6000 : 10000);
                if (r != null && !manual && r.Version != before && Settings.Cur.Overlay && (!Visible || WindowState == FormWindowState.Minimized || Form.ActiveForm != this))
                    SavedOverlay.Pop("Update available", "Clutch " + r.Version + " is ready. Open Clutch to update.", true, NoticeScreen());
                else if (manual && Updater.LastError.Length == 0) Toast.Show(Glyph.Check, "You're on the latest version", null, null, 3000);
                if (trayUpdate != null) { trayUpdate.Visible = r != null; if (r != null) trayUpdate.Right = r.Version; }
            });
        }

        public void StartUpdate()
        {
            if (Updater.Available == null) return;
            if (saving) { Toast.Show(Glyph.Clock, "Saving a clip, update in a moment", null, null, 3000); return; }
            Program.PendingUpdate = Updater.Available; Quit();
        }

        public void StartUninstall() { Program.PendingUninstall = true; Quit(); }

        void ShowAnnouncement()
        {
            if (Announce.Id.Length == 0 || Announce.Id == Settings.Cur.ShownAnnouncement) return;
            Settings.Cur.ShownAnnouncement = Announce.Id; Settings.Save();
            string msg = Announce.Message.Replace("\r", "").Replace("\n", " ");
            SavedOverlay.Pop(Announce.Title.Length > 0 ? Announce.Title : "Announcement", msg, true, NoticeScreen(), Math.Min(12000, 8000 + msg.Length * 30));
        }

        public void EditNoticePosition()
        {
            Rectangle scr = NoticeScreen();
            using (OverlayEditor ed = new OverlayEditor(scr))
            {
                ed.ShowDialog(this);
                if (ed.Saved) SavedOverlay.Pop("Clipped!", "This is where it'll show", true, scr);
            }
        }

        static Rectangle NoticeScreen()
        {
            int gm = Recorder.Captured.IsEmpty ? -1 : Recorder.MonitorFor(Recorder.Captured);
            return Recorder.MonitorBounds(gm >= 0 ? gm : Settings.Cur.Monitor);
        }
    }
}
