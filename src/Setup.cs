using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Clutch
{
    static class Setup
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Clutch";
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Clutch"); } }
        public static string Exe { get { return Path.Combine(Dir, "Clutch.exe"); } }
        static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Clutch.lnk"); } }
        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Clutch.lnk"); } }

        public static bool IsInstalled { get { return File.Exists(Exe); } }
        public static bool RunningInstalled { get { return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase); } }
        public static bool IsSetupExe { get { return Path.GetFileName(Application.ExecutablePath).StartsWith("ClutchSetup", StringComparison.OrdinalIgnoreCase); } }
        public static string InstalledVersion
        {
            get { try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey)) return k == null ? "" : (k.GetValue("DisplayVersion") ?? "").ToString(); } catch { return ""; } }
        }

        public static void CloseRunning()
        {
            Native.PostMessage((IntPtr)0xFFFF, SingleInstance.QuitMessage, IntPtr.Zero, IntPtr.Zero);
            int me = Process.GetCurrentProcess().Id;
            for (int i = 0; i < 50; i++)
            {
                bool any = false;
                foreach (Process p in Process.GetProcessesByName("Clutch")) { if (p.Id != me) any = true; p.Dispose(); }
                if (!any) return;
                Thread.Sleep(100);
            }
            foreach (Process p in Process.GetProcessesByName("Clutch")) { try { if (p.Id != me) p.Kill(); } catch { } p.Dispose(); }
            Thread.Sleep(300);
        }

        public static void Install(bool desktop, bool startup, Action<string> step)
        {
            step("Closing Clutch if it's open\u2026");
            CloseRunning();
            step("Copying Clutch\u2026");
            Directory.CreateDirectory(Dir);
            if (!RunningInstalled)
            {
                string tmp = Exe + ".new";
                File.Copy(Application.ExecutablePath, tmp, true);
                if (File.Exists(Exe)) File.Delete(Exe);
                File.Move(tmp, Exe);
            }

            step("Adding shortcuts\u2026");
            Shortcut(StartMenuLink);
            if (desktop) Shortcut(DesktopLink); else if (File.Exists(DesktopLink)) File.Delete(DesktopLink);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (startup) k.SetValue("Clutch", "\"" + Exe + "\" --tray"); else k.DeleteValue("Clutch", false);
            }
            Register();
        }

        public static void Register()
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Clutch");
                k.SetValue("DisplayVersion", AppInfo.Version);
                k.SetValue("Publisher", "Clutch");
                k.SetValue("DisplayIcon", "\"" + Exe + "\",0");
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", "\"" + Exe + "\" --uninstall");
                k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(Exe).Length / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        static void Shortcut(string path)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic sh = Activator.CreateInstance(t);
            try
            {
                dynamic l = sh.CreateShortcut(path);
                l.TargetPath = Exe; l.WorkingDirectory = Dir; l.IconLocation = Exe + ",0"; l.Description = "Clip your best moments";
                l.Save();
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sh); }
        }

        public static void Uninstall(bool removeData, Action<string> step)
        {
            step("Closing Clutch\u2026");
            CloseRunning();
            step("Removing shortcuts\u2026");
            foreach (string l in new[] { StartMenuLink, DesktopLink }) try { if (File.Exists(l)) File.Delete(l); } catch { }
            try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) k.DeleteValue("Clutch", false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            if (removeData)
            {
                step("Removing settings and FFmpeg\u2026");
                foreach (string d in new[] { Paths.Roaming, Paths.Local }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
            }
            step("Removing Clutch\u2026");
            if (RunningInstalled) deleteOnExit = true;
            else
            {
                try { if (File.Exists(Exe)) File.Delete(Exe); } catch { }
                try { Directory.Delete(Dir, false); } catch { }
            }
        }

        static bool deleteOnExit;
        public static void FinishUninstall()
        {
            if (!deleteOnExit) return;
            string cmd = "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + Exe + "\" \"" + Path.Combine(Dir, "Clutch.old.exe") + "\" & rd \"" + Dir + "\"";
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmd); psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.WindowStyle = ProcessWindowStyle.Hidden;
            try { Process.Start(psi); } catch { }
        }

        public static void CleanOld()
        {
            string old = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Clutch.old.exe");
            if (!File.Exists(old)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < 20 && File.Exists(old); i++) { try { File.Delete(old); } catch { Thread.Sleep(500); } }
            });
        }
    }

    class SetupWindow : Window
    {
        enum Stage { Ask, Working, Done, Failed }
        readonly bool uninstall; Stage stage = Stage.Ask; string error = "", stepText = "";
        readonly Toggle tDesktop = new Toggle(), tStartup = new Toggle(), tData = new Toggle();
        readonly Btn go, cancel = new Btn("Cancel", "", BtnStyle.Subtle), open = new Btn("Open Clutch", "", BtnStyle.Accent);
        readonly Progress bar = new Progress();
        readonly string existing;

        public SetupWindow(bool uninstall) : base(uninstall ? "Uninstall Clutch" : "Clutch Setup", 520, uninstall ? 372 : 432)
        {
            this.uninstall = uninstall;
            existing = Setup.IsInstalled && !uninstall ? Setup.InstalledVersion : "";
            go = new Btn(uninstall ? "Uninstall" : existing.Length > 0 ? (existing == AppInfo.Version ? "Reinstall" : "Update") : "Install", "", uninstall ? BtnStyle.Danger : BtnStyle.Accent);
            go.Weight = 5;
            tDesktop.Set(!uninstall); tStartup.Set(!uninstall && (existing.Length == 0 || Settings.StartWithWindows));
            go.Click += delegate { Run(); };
            cancel.Click += delegate { Close(); };
            open.Click += delegate { try { Process.Start(Setup.Exe); } catch { } Close(); };
            foreach (Control c in new Control[] { tDesktop, tStartup, tData }) c.BackColor = C.Card;
            foreach (Control c in new Control[] { tDesktop, tStartup, tData, go, cancel, open, bar }) Controls.Add(c);
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape && stage != Stage.Working) Close(); };
            Show(Stage.Ask);
        }

        void Show(Stage s)
        {
            stage = s;
            tDesktop.Visible = tStartup.Visible = s == Stage.Ask && !uninstall;
            tData.Visible = s == Stage.Ask && uninstall;
            go.Visible = s == Stage.Ask || s == Stage.Failed;
            if (s == Stage.Failed) go.Text = "Try again";
            cancel.Visible = s != Stage.Working;
            cancel.Text = s == Stage.Ask || s == Stage.Failed ? "Cancel" : "Close";
            open.Visible = s == Stage.Done && !uninstall;
            bar.Visible = s == Stage.Working;
            bar.SetBusy(s == Stage.Working);
            ControlBox = s != Stage.Working;
            Relayout(); Invalidate();
        }

#if DEV
        public void ShotStage(int s) { Show((Stage)s); }
#endif

        void Run()
        {
            bool desktop = tDesktop.On, startup = tStartup.On, data = tData.On;
            Show(Stage.Working);
            Thread t = new Thread(delegate ()
            {
                string err = null;
                Action<string> step = delegate (string st) { Post(delegate { stepText = st; Invalidate(); }); };
                try
                {
                    if (uninstall) Setup.Uninstall(data, step); else Setup.Install(desktop, startup, step);
                    Thread.Sleep(400);
                }
                catch (Exception ex) { err = ex.Message; Log.Error(uninstall ? "Uninstall" : "Install", ex); }
                Post(delegate { error = err ?? ""; Show(err == null ? Stage.Done : Stage.Failed); if (err == null && !uninstall) open.Focus(); });
            });
            t.IsBackground = true; t.SetApartmentState(ApartmentState.STA); t.Start();
        }

        void Post(Action a) { try { if (IsHandleCreated) BeginInvoke(a); } catch { } }

        protected override void OnFormClosing(FormClosingEventArgs e) { if (stage == Stage.Working) e.Cancel = true; base.OnFormClosing(e); }

        Rectangle OptionsCard { get { return new Rectangle(S(28), S(196), S(520) - S(56), uninstall ? S(64) : S(112)); } }

        protected override void Relayout()
        {
            int w = S(520), h = S(uninstall ? 372 : 432), bh = S(40);
            Rectangle card = OptionsCard;
            tDesktop.SetBounds(card.Right - S(66), card.Y + S(12), S(50), S(30));
            tStartup.SetBounds(card.Right - S(66), card.Y + S(68), S(50), S(30));
            tData.SetBounds(card.Right - S(66), card.Y + S(17), S(50), S(30));
            bar.SetBounds(S(28), S(250), w - S(56), S(6));
            int y = h - S(28) - bh;
            cancel.SetBounds(w - S(28) - S(96), y, S(96), bh);
            go.SetBounds(cancel.Left - S(10) - S(140), y, S(140), bh);
            open.SetBounds(cancel.Left - S(10) - S(150), y, S(150), bh);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            string head, sub;
            if (uninstall) { head = stage == Stage.Done ? "Clutch was removed" : "Uninstall Clutch"; sub = "Version " + AppInfo.Version; }
            else
            {
                head = stage == Stage.Done ? "Clutch is ready" : existing.Length > 0 ? (existing == AppInfo.Version ? "Reinstall Clutch" : "Update Clutch") : "Install Clutch";
                sub = existing.Length > 0 && existing != AppInfo.Version ? "Version " + existing + "  \u2192  " + AppInfo.Version : "Version " + AppInfo.Version + "  \u00B7  " + Library.NiceSize(SafeSize());
            }
            DrawHeader(g, head, sub);
            int x = S(28), w = ClientSize.Width - S(56);
            Font body = Ui.F(9.5f, 0);
            if (stage == Stage.Ask || stage == Stage.Failed)
            {
                string[] lines = uninstall
                    ? new[] { "Your clips stay in your Videos\\Clutch folder.", "Clutch is closed first if it's running." }
                    : new[] { "Installs for you only, no admin needed.", "No account, no ads, nothing uploaded.", "Shows up in Start, and in Installed apps to remove it." };
                int y = S(96);
                foreach (string l in lines)
                {
                    Ui.DrawIcon(g, Glyph.Check, 9, new Rectangle(x, y, S(18), S(22)), C.AccentHi);
                    Ui.TextLeft(g, l, body, new Rectangle(x + S(26), y, w - S(26), S(22)), C.Sub);
                    y += S(26);
                }
                Rectangle card = OptionsCard;
                Ui.FillRound(g, card, S(10), C.Card);
                if (uninstall)
                {
                    Ui.TextLeft(g, "Also remove settings and FFmpeg", Ui.F(10, 1), new Rectangle(card.X + S(18), card.Y + S(10), card.Width - S(100), S(24)), C.Text);
                    Ui.TextLeft(g, "Frees about " + DataSize() + ". Clutch will need setting up again.", Ui.F(9, 0), new Rectangle(card.X + S(18), card.Y + S(32), card.Width - S(100), S(22)), C.Sub);
                }
                else
                {
                    Ui.TextLeft(g, "Desktop shortcut", Ui.F(10, 1), new Rectangle(card.X + S(18), card.Y + S(14), card.Width - S(100), S(26)), C.Text);
                    using (Pen p = new Pen(Ui.Mix(C.Card, C.Text, 0.06f))) g.DrawLine(p, card.X + S(16), card.Y + S(56), card.Right - S(16), card.Y + S(56));
                    Ui.TextLeft(g, "Start with Windows", Ui.F(10, 1), new Rectangle(card.X + S(18), card.Y + S(62), card.Width - S(100), S(24)), C.Text);
                    Ui.TextLeft(g, "Opens quietly in the tray, so the clip key always works.", Ui.F(9, 0), new Rectangle(card.X + S(18), card.Y + S(84), card.Width - S(100), S(20)), C.Sub);
                }
                if (stage == Stage.Failed)
                    Ui.Wrap(g, (uninstall ? "Couldn't uninstall: " : "Couldn't install: ") + error, Ui.F(9, 0), new Rectangle(x, card.Bottom + S(10), w, S(40)), C.Warn);
                else
                    Ui.TextLeft(g, uninstall ? "" : "Installs to " + Shorten(Setup.Dir), Ui.F(9, 0), new Rectangle(x, card.Bottom + S(10), w, S(22)), C.Faint);
            }
            else if (stage == Stage.Working)
                Ui.TextLeft(g, stepText, body, new Rectangle(x, S(210), w, S(28)), C.Sub);
            else
            {
                Rectangle ring = new Rectangle(x, S(110), S(44), S(44));
                Ui.DrawIcon(g, Glyph.Check, 20, ring, C.Good);
                string done = uninstall ? "Thanks for trying Clutch." : "Press " + MainForm.HotkeyName() + " in any game to save the last moments. Clutch finishes setting up the first time it opens.";
                Ui.Wrap(g, done, body, new Rectangle(x + S(60), S(112), w - S(60), S(60)), C.Sub);
            }
        }

        static long SafeSize() { try { return new FileInfo(Application.ExecutablePath).Length; } catch { return 0; } }

        static string DataSize()
        {
            long n = 0;
            foreach (string d in new[] { Paths.Roaming, Paths.Local })
                try { if (Directory.Exists(d)) foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories)) n += new FileInfo(f).Length; } catch { }
            return Library.NiceSize(n);
        }

        static string Shorten(string p)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return p.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "%LOCALAPPDATA%" + p.Substring(home.Length) : p;
        }
    }
}
