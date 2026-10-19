using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Clutch
{
    static class AppInfo
    {
        public const string Name = "Clutch";
        public const string Version = "1.0.2";
    }

    static class Program
    {
        public static bool DevRun;
        public static string ShotPage, ShotFile;
        public static bool Demo, StartHidden, SelfTesting, JustUpdated, PendingUninstall, InviteRevoked;
        public static Updater.Release PendingUpdate;
        static MainForm Main_;

        public static void HideTray() { try { if (Main_ != null) Main_.HideTray(); } catch { } }

        [STAThread]
        static void Main(string[] args)
        {
            try { if (!Native.SetProcessDpiAwarenessContext((IntPtr)(-4))) Native.SetProcessDPIAware(); }
            catch { try { Native.SetProcessDPIAware(); } catch { } }

#if DEV
            if (DevTools.Early(args)) return;
            DevTools.Parse(args);
#endif
            if (Array.IndexOf(args, "--tray") >= 0) StartHidden = true;

            int ci = Array.IndexOf(args, "--crashed");
            bool install = Array.IndexOf(args, "--install") >= 0 || (Setup.IsSetupExe && !DevRun);
            bool uninstall = Array.IndexOf(args, "--uninstall") >= 0;
            if (ci >= 0 || install || uninstall)
            {
                Paths.Init(); Settings.Load(); Ui.Init();
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                if (ci >= 0) ReportWindow.RunCrashed(ci + 1 < args.Length ? args[ci + 1] : "");
                else Application.Run(new SetupWindow(uninstall));
                Setup.FinishUninstall();
                return;
            }

            if (!DevRun && !SingleInstance.Claim()) { SingleInstance.WakeOther(); return; }

            Crash.Hook();
            Paths.Init();
            Settings.Load();
            Ui.Init();
#if DEV
            if (DevTools.Late(args)) return;
#endif
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Setup.CleanOld();
            JustUpdated = Array.IndexOf(args, "--updated") >= 0;
            if (!DevRun)
            {
                Licence.State st = Licence.Check();
                if (st != Licence.State.Ok && !ActivateWindow.Run(st)) { SingleInstance.Release(); return; }
            }
            Main_ = new MainForm();
            Application.Run(Main_);
            Main_ = null;
            if (PendingUpdate != null) Application.Run(new UpdateWindow(PendingUpdate));
            else if (PendingUninstall) { Application.Run(new SetupWindow(true)); Setup.FinishUninstall(); }
            else if (InviteRevoked && ActivateWindow.Run(Licence.State.Revoked)) { SingleInstance.Release(); try { System.Diagnostics.Process.Start(Application.ExecutablePath); } catch { } }
            SingleInstance.Release();
        }
    }

    static class Paths
    {
        public static string Roaming, Local, Temp, Thumbs;

        public static void Init()
        {
            Roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clutch");
            Local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clutch");
            Temp = Path.Combine(Local, "temp");
            Thumbs = Path.Combine(Local, "thumbs");
            foreach (string d in new[] { Roaming, Local, Temp, Thumbs }) try { Directory.CreateDirectory(d); } catch { }
            try { foreach (string f in Directory.GetFiles(Temp)) File.Delete(f); } catch { }
        }

        public static string DefaultClips()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Clutch");
        }

        public static string NewTemp(string ext)
        {
            return Path.Combine(Temp, Guid.NewGuid().ToString("N").Substring(0, 12) + ext);
        }
    }

    static class Log
    {
        static readonly object gate = new object();
        public static string FilePath { get { return Path.Combine(Paths.Roaming ?? Path.GetTempPath(), "errors.log"); } }

        public static void Error(string where, Exception ex)
        {
            Write("ERROR", where, ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
        }
        public static void Error(string where, string message) { Write("ERROR", where, message); }
        public static void Info(string where, string message) { Write("INFO", where, message); }

        static void Write(string kind, string where, string text)
        {
            try
            {
                lock (gate)
                {
                    string p = FilePath;
                    if (File.Exists(p) && new FileInfo(p).Length > 512 * 1024)
                    {
                        string[] lines = File.ReadAllLines(p);
                        File.WriteAllLines(p, lines.Length > 2000 ? new List<string>(lines).GetRange(lines.Length - 2000, 2000).ToArray() : lines);
                    }
                    File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  [" + kind + "] " + where + ": " + (text ?? "").Replace("\r", "").Trim() + "\r\n");
                }
            }
            catch { }
        }
    }

    class GameEntry { public string Exe = ""; public string Name = ""; }

    class Settings
    {
        public static Settings Cur = new Settings();

        public bool Enabled = true;
        public bool GamesOnly = false;
        public List<GameEntry> Games = new List<GameEntry>();
        public int BufferSeconds = 60;
        public int Fps = 60;
        public int Quality = 1;
        public int Resolution = 0;
        public int Monitor = 0;
        public int CaptureMode = 0;
        public string Encoder = "auto";
        public int HotkeyVk = 0x77;
        public int HotkeyMods = 0;
        public bool DesktopAudio = true;
        public string DesktopDevice = "";
        public bool Mic = false;
        public string MicDevice = "";
        public int MicVolume = 100;
        public string ClipsFolder = "";
        public bool SaveSound = true;
        public int OverlaySide = 0;
        public double OverlayY = 0;
        public bool Overlay = true;
        public bool CloseToTray = true;
        public bool TrayHintShown = false;
        public bool WelcomeDone = false;
        public bool DiscordStatus = true;
        public bool AutoUpdate = true;
        public string ProbeKey = "";
        public string ProbeResult = "";

        [ScriptIgnore] public string Folder { get { return string.IsNullOrEmpty(ClipsFolder) ? Paths.DefaultClips() : ClipsFolder; } }

        static string FilePath { get { return Path.Combine(Paths.Roaming, "settings.json"); } }

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    Settings s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath));
                    if (s != null) Cur = s;
                }
            }
            catch (Exception ex) { Log.Error("Settings.Load", ex); }
            if (Cur.Games == null) Cur.Games = new List<GameEntry>();
            foreach (GameEntry g in Cur.Games) g.Name = Clutch.Games.Clean(g.Name);
            if (Cur.BufferSeconds < 5) Cur.BufferSeconds = 60;
            if (Cur.BufferSeconds > 600) Cur.BufferSeconds = 600;
            if (Cur.Fps != 30 && Cur.Fps != 60) Cur.Fps = 60;
            if (Cur.OverlaySide != 1) Cur.OverlaySide = 0;
            Cur.OverlayY = Math.Max(0, Math.Min(1, Cur.OverlayY));
        }

        public static void Save()
        {
            if (Program.DevRun) return;
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(Cur), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Error("Settings.Save", ex); }
        }

        public static bool StartWithWindows
        {
            get
            {
                try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return k != null && k.GetValue("Clutch") != null; }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    {
                        if (value) k.SetValue("Clutch", "\"" + Application.ExecutablePath + "\" --tray");
                        else k.DeleteValue("Clutch", false);
                    }
                }
                catch (Exception ex) { Log.Error("StartWithWindows", ex); }
            }
        }
    }

    static class SingleInstance
    {
        static Mutex mutex;
        static int showMessage, quitMessage;
        public static int ShowMessage { get { if (showMessage == 0) showMessage = Native.RegisterWindowMessage("Clutch.ShowWindow"); return showMessage; } }
        public static int QuitMessage { get { if (quitMessage == 0) quitMessage = Native.RegisterWindowMessage("Clutch.Quit"); return quitMessage; } }

        public static bool Claim()
        {
            bool created;
            mutex = new Mutex(true, "Local\\Clutch.SingleInstance", out created);
            return created;
        }

        public static void WakeOther()
        {
            Native.AllowSetForegroundWindow(-1);
            Native.PostMessage((IntPtr)0xFFFF, ShowMessage, IntPtr.Zero, IntPtr.Zero);
        }

        public static void Release()
        {
            if (mutex == null) return;
            try { mutex.ReleaseMutex(); } catch { }
            try { mutex.Close(); } catch { }
            mutex = null;
        }
    }
}
