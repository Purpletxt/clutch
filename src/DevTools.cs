#if DEV
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Clutch
{
    static class DevTools
    {
        public static bool Early(string[] args)
        {
            if (args.Length == 3 && args[0] == "--playtest") { Application.EnableVisualStyles(); Paths.Init(); Ui.Init(); PlayTest.Run(args[1], args[2], false); return true; }
            if (args.Length == 3 && args[0] == "--playtest-hidden") { Application.EnableVisualStyles(); Paths.Init(); Ui.Init(); PlayTest.Run(args[1], args[2], true); return true; }
            if (args.Length == 3 && args[0] == "--make-logo") { Branding.Make(args[1], args[2]); return true; }
            if (args.Length == 3 && args[0] == "--discordping") { System.IO.File.WriteAllText(args[2], DiscordPresence.Ping(args[1])); return true; }
            if (args.Length == 2 && args[0] == "--banner") { Paths.Init(); Settings.Load(); GameArt.FetchBanner(args[1]); return true; }
            if (args.Length == 3 && args[0] == "--gameart") { Paths.Init(); Settings.Load(); System.IO.File.WriteAllText(args[2], GameArt.Url(args[1]) ?? "(not found)"); return true; }
            if (args.Length == 3 && args[0] == "--winshot") { Paths.Init(); Settings.Load(); Ui.Init(); Application.EnableVisualStyles(); WinShot(args[1], Path.GetFullPath(args[2])); return true; }
            if (args.Length == 3 && args[0] == "--setuptest")
            {
                Paths.Init(); Settings.Load();
                Action<string> step = delegate (string s) { File.AppendAllText(args[2], s + Environment.NewLine); };
                try { if (args[1] == "install") Setup.Install(true, false, step); else { Setup.Uninstall(false, step); Setup.FinishUninstall(); } step("OK"); }
                catch (Exception ex) { step("FAILED: " + ex); }
                return true;
            }
            if (args.Length == 2 && args[0] == "--updatetest")
            {
                Paths.Init(); Settings.Load();
                Action<string> log = delegate (string s) { File.AppendAllText(args[1], s + Environment.NewLine); };
                try
                {
                    Updater.Release r = Updater.Fetch();
                    log(r == null ? "no release" : "found " + r.Version + " newer=" + Updater.Newer(r.Version) + " sha=" + r.Sha256 + " size=" + r.Size);
                    string f = Updater.Download(r, delegate { }, delegate { return false; });
                    log("downloaded and verified " + f);
                    Updater.Apply(f, "--whoami \"" + args[1] + "\"");
                    log("swapped");
                }
                catch (Exception ex) { log("FAILED: " + ex.Message); }
                return true;
            }
            if (args.Length == 2 && args[0] == "--whoami") { Paths.Init(); Setup.CleanOld(); System.Threading.Thread.Sleep(3000); File.AppendAllText(args[1], "started " + Application.ExecutablePath + " old-exe-left=" + File.Exists(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Clutch.old.exe")) + Environment.NewLine); return true; }
            if (args.Length == 3 && args[0] == "--verttest")
            {
                Paths.Init(); Settings.Load(); Ui.Init();
                TrimPage t = new TrimPage(null);
                double len = FFmpeg.Duration(args[1]), b = Math.Min(len, 8);
                string log = "";
                foreach (int mb in new[] { 0, 10 })
                {
                    string o = Path.Combine(args[2], "vertical-" + mb + ".mp4");
                    string err = t.TestExportVertical(args[1], o, 0, b, mb, 0.3);
                    log += mb + " MB: " + (err ?? "ok " + new FileInfo(o).Length + " bytes") + Environment.NewLine;
                    if (err == null) log += FFmpeg.Run(FFmpeg.ProbeExe, "-v error -select_streams v:0 -show_entries stream=width,height,codec_name -of csv=p=0 " + FFmpeg.Q(o), 10000, null).Out;
                }
                File.WriteAllText(Path.Combine(args[2], "vertical.txt"), log);
                return true;
            }
            if (args.Length == 3 && args[0] == "--keycheck") { Paths.Init(); File.WriteAllText(args[2], "pc " + Licence.PcCode + "\r\nresult " + Licence.Check(File.ReadAllText(args[1]).Trim()) + "\r\n" + Licence.Explain(File.ReadAllText(args[1]).Trim())); return true; }
            if (args.Length == 1 && args[0] == "--crashtest")
            {
                Paths.Init(); Settings.Load(); Ui.Init(); Crash.Hook(); Application.EnableVisualStyles();
                Form f = new Form(); f.Text = "Crash test"; f.Shown += delegate { throw new InvalidOperationException("Crash test: this is a pretend crash from --crashtest"); };
                Application.Run(f); return true;
            }
            return false;
        }

        static void WinShot(string kind, string png)
        {
            Form w;
            Updater.Release r = new Updater.Release(); r.Version = "1.1.0"; r.Notes = "- Vertical export for TikTok, Shorts and Reels\n- Mute buttons for game and mic when trimming\n- Crash reports with tips for common problems\n- Faster startup";
            if (kind == "setup" || kind == "setup-done") { SetupWindow s = new SetupWindow(false); if (kind == "setup-done") s.ShotStage(2); w = s; }
            else if (kind == "uninstall") w = new SetupWindow(true);
            else if (kind == "update" || kind == "update-failed") { UpdateWindow u = new UpdateWindow(r); u.Preview = kind == "update" ? 1 : 2; w = u; }
            else if (kind == "licences") w = new LicencesWindow();
            else if (kind == "activate") w = new ActivateWindow(Licence.State.Missing);
            else if (kind == "report") w = new ReportWindow(Report.Build(null), false);
            else w = new ReportWindow(File.Exists(Path.Combine(Paths.Local, "crash-report.txt")) ? File.ReadAllText(Path.Combine(Paths.Local, "crash-report.txt")) : Report.Build(new OutOfMemoryException()), true);
            w.StartPosition = FormStartPosition.CenterScreen;
            w.Shown += delegate
            {
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 1200;
                t.Tick += delegate
                {
                    t.Stop();
                    Rectangle b = w.Bounds;
                    using (Bitmap bmp = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(bmp)) { IntPtr hdc = g.GetHdc(); Native.PrintWindow(w.Handle, hdc, 2); g.ReleaseHdc(hdc); }
                        bmp.Save(png, ImageFormat.Png);
                    }
                    w.Hide(); Environment.Exit(0);
                };
                t.Start();
            };
            Application.Run(w);
        }

        public static void Parse(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--shot" && i + 2 < args.Length) { Program.ShotPage = args[i + 1]; Program.ShotFile = Path.GetFullPath(args[i + 2]); Program.DevRun = true; }
                if (args[i] == "--demo") { Program.Demo = true; Program.DevRun = true; }
                if (args[i] == "--viewtest" && i + 2 < args.Length) { ViewTest.File_ = Path.GetFullPath(args[i + 1]); ViewTest.Png = Path.GetFullPath(args[i + 2]); Program.DevRun = true; }
                if (args[i] == "--e2e" && i + 1 < args.Length) E2ETest.Folder = Path.GetFullPath(args[i + 1]);
                if (args[i] == "--selftest" || args[i] == "--download-ffmpeg") Program.DevRun = true;
            }
        }

        public static bool Late(string[] args)
        {
            if (args.Length == 2 && args[0] == "--download-ffmpeg") { SelfTest.Download(args[1]); return true; }
            if (args.Length == 2 && args[0] == "--selftest") { Program.SelfTesting = true; SelfTest.Run(Path.GetFullPath(args[1])); return true; }
            return false;
        }

        public static void Shown(MainForm f)
        {
            if (Program.ShotPage != null) Shot.Run(f);
            if (E2ETest.Folder != null) E2ETest.Start(f);
            if (ViewTest.File_ != null) ViewTest.Start(f);
        }
    }

    static class Shot
    {
        public static void Run(MainForm f)
        {
            string page = Program.ShotPage;
            if (page == "settings") f.Go(f.SettingsP);
            else if (page == "games") f.Go(f.GamesP);
            else if (page == "welcome") f.Go(f.Welcome);
            else if (page == "select") { f.Clips.SelectAll(); }
            else if (page == "empty" || page == "games-empty") { Library.Clips.Clear(); f.Clips.Refresh(); if (page == "games-empty") f.Go(f.GamesP); }

            else if (page.StartsWith("settings")) { f.Go(f.SettingsP); f.SettingsP.ScrollTo(page); }
            else if (page == "viewer" && Library.Clips.Count > 0) f.OpenClip(Library.Clips[0]);
            else if (page == "trim" && Library.Clips.Count > 0) f.TrimClip(Library.Clips[0]);
            else if (page == "trim-vertical" && Library.Clips.Count > 0) { f.TrimClip(Library.Clips[0]); f.Trim.ShotVertical(); }
            else if (page == "toast") f.Toast.Show(Glyph.Delete, "Deleted \"Valorant 2026-10-18 21-04-11\"", "Undo", null, 10000);
            else if (page == "overlay-left") { Settings.Cur.OverlaySide = 1; Settings.Cur.OverlayY = 0.5; page = Program.ShotPage = "overlay"; SavedOverlay.Pop("Clipped!", "Clipped Counter-Strike 2  \u00B7  60s", true, Screen.FromControl(f).WorkingArea); }
            else if (page == "overlay") { SavedOverlay.Pop("Clipped!", "Clipped Counter-Strike 2  \u00B7  60s", true, Screen.FromControl(f).WorkingArea); }
            else if (page == "rename") { f.BeginInvoke((MethodInvoker)delegate { RenameDialog.ShotMode = true; RenameDialog.Ask(f, "Valorant 2026-10-18 21-04-11"); }); }
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 1500;
            t.Tick += delegate
            {
                t.Stop();
                Form target = f;
                if (page == "overlay")
                {
                    Rectangle r = ((SavedOverlay)SavedOverlay.Current).ShotRect; r.Inflate(Ui.S(30), Ui.S(20));
                    using (Bitmap b = new Bitmap(r.Width, r.Height)) { using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size); b.Save(Program.ShotFile, ImageFormat.Png); }
                    f.Close(); return;
                }
                if (page == "rename") foreach (Form o in Application.OpenForms) if (o is RenameDialog) target = o;
                if (page == "hover") { f.Clips.DemoHover(1, 0); f.Refresh(); }
                Capture(target, Program.ShotFile);
                if (target != f) target.Close();
                f.Close();
            };
            t.Start();
        }

        static void Capture(Form f, string file)
        {
            Rectangle r = f.Bounds;
            using (Bitmap b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    IntPtr hdc = g.GetHdc();
                    Native.PrintWindow(f.Handle, hdc, 2);
                    g.ReleaseHdc(hdc);
                }
                b.Save(file, ImageFormat.Png);
            }
        }
    }

    static class Demo
    {
        static Bitmap Art(int seed, int w, int h)
        {
            Random r = new Random(seed);
            Bitmap b = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(b))
            {
                Ui.Prep(g);
                Color a = Color.FromArgb(r.Next(30, 120), r.Next(30, 120), r.Next(60, 160)), c2 = Color.FromArgb(r.Next(120, 255), r.Next(60, 200), r.Next(40, 140));
                using (LinearGradientBrush lg = new LinearGradientBrush(new Rectangle(0, 0, w, h), a, c2, (float)r.Next(0, 360))) g.FillRectangle(lg, 0, 0, w, h);
                for (int i = 0; i < 7; i++)
                {
                    int s = r.Next(h / 6, h / 2);
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(r.Next(25, 70), 255, 255, 255))) g.FillEllipse(sb, r.Next(-s / 2, w), r.Next(-s / 2, h), s, s);
                }
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillRectangle(sb, 0, h * 2 / 3, w, h / 3);
            }
            return b;
        }

        public static void Fill()
        {
            string[] games = { "Valorant", "Counter-Strike 2", "Fortnite", "Rocket League", "Apex Legends", "Minecraft", "Valorant", "Counter-Strike 2", "Overwatch 2", "Fortnite" };
            string[] names = { "Ace on Ascent", "1v4 clutch", "Victory Royale", "Ceiling shot", "Squad wipe", "Speedrun PB", "Operator flick", "Ninja defuse", "Team kill", "No-scope" };
            double[] dur = { 60, 30, 60, 42, 120, 60, 30, 60, 58, 37 };
            DateTime t = DateTime.Now.AddMinutes(-12);
            for (int i = 0; i < names.Length; i++)
            {
                Clip c = new Clip(); c.Path = @"C:\Demo\" + names[i] + ".mp4"; c.Game = games[i]; c.Duration = dur[i];
                c.Size = (long)(dur[i] * 1.25 * 1024 * 1024); c.Created = t; c.Favorite = i == 1 || i == 4; c.Thumb = Art(i * 7 + 3, 480, 270); c.ThumbTried = true;
                Library.Clips.Add(c);
                t = t.AddHours(-(3 + i * 5));
            }
        }

        public static List<Bitmap> Strip()
        {
            List<Bitmap> l = new List<Bitmap>();
            for (int i = 0; i < 10; i++) l.Add(Art(100 + i / 3, 170, 96));
            return l;
        }
    }
}
#endif
