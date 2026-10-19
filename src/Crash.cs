using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Clutch
{
    static class Crash
    {
        static int handled;

        public static void Hook()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Fatal("Crash (UI)", e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception; if (ex != null) Fatal("Crash", ex);
            };
        }

        static void Fatal(string where, Exception ex)
        {
            Log.Error(where, ex);
            if (Program.DevRun) return;
            if (Interlocked.Exchange(ref handled, 1) == 1) return;
            try
            {
                string file = Path.Combine(Paths.Local, "crash-report.txt");
                File.WriteAllText(file, Report.Build(ex), Encoding.UTF8);
                Program.HideTray();
                Process.Start(Application.ExecutablePath, "--crashed \"" + file + "\"");
            }
            catch { }
            Environment.Exit(1);
        }
    }

    static class Report
    {
        public static string Build(Exception ex)
        {
            Settings s = Settings.Cur;
            StringBuilder b = new StringBuilder();
            b.AppendLine("Clutch " + AppInfo.Version + (Setup.IsInstalled ? "" : " (not installed)"));
            b.AppendLine(WindowsName());
            b.AppendLine("Graphics: " + Safe(delegate { return Encoder.GpuSummary(); }));
            string enc = Recorder.EncoderUsed.Length > 0 ? Recorder.EncoderUsed : s.ProbeResult;
            b.AppendLine("Encoder: " + (enc.Length > 0 ? Encoder.Label(enc.Split(':')[0]) + (enc.EndsWith(":1") ? " (zero-copy)" : "") : "not picked yet"));
            b.AppendLine("FFmpeg: " + (FFmpeg.Ready ? FFmpeg.Version : "not installed"));
            b.AppendLine("Recording: " + s.Fps + " fps, " + new[] { "low", "standard", "high" }[Math.Max(0, Math.Min(2, s.Quality))] + " quality, " + new[] { "native", "1080p", "720p" }[Math.Max(0, Math.Min(2, s.Resolution))] + ", " + s.BufferSeconds + " s buffer, " + (s.Enabled ? "on" : "off") + (s.GamesOnly ? ", games only" : ""));
            b.AppendLine("Audio: desktop " + (s.DesktopAudio ? "on" : "off") + ", mic " + (s.Mic ? "on" : "off"));
            b.AppendLine("Memory: " + (Process.GetCurrentProcess().WorkingSet64 >> 20) + " MB, up " + (int)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMinutes + " min");
            b.AppendLine();
            if (ex != null)
            {
                b.AppendLine("What happened:");
                for (Exception e = ex; e != null; e = e.InnerException) b.AppendLine(e.GetType().Name + ": " + e.Message);
                b.AppendLine(ex.StackTrace);
                b.AppendLine();
            }
            b.AppendLine("Recent log:");
            b.AppendLine(LogTail(30));
            return Scrub(b.ToString());
        }

        static string Safe(Func<string> f) { try { return f(); } catch { return "unknown"; } }

        static string WindowsName()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string name = (k.GetValue("ProductName") ?? "Windows").ToString(), ver = (k.GetValue("DisplayVersion") ?? "").ToString();
                    int build; int.TryParse((k.GetValue("CurrentBuild") ?? "0").ToString(), out build);
                    if (build >= 22000) name = name.Replace("Windows 10", "Windows 11");
                    return name + (ver.Length > 0 ? " " + ver : "") + " (build " + build + ")";
                }
            }
            catch { return Environment.OSVersion.VersionString; }
        }

        static string LogTail(int lines)
        {
            try
            {
                if (!File.Exists(Log.FilePath)) return "(empty)";
                string[] all = File.ReadAllLines(Log.FilePath);
                int from = Math.Max(0, all.Length - lines);
                return string.Join(Environment.NewLine, all, from, all.Length - from);
            }
            catch { return "(couldn't read the log)"; }
        }

        static string Scrub(string t)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 3) t = t.Replace(home, "%USERPROFILE%");
            string user = Environment.UserName;
            if (user.Length > 2) t = t.Replace("\\" + user + "\\", "\\%USERNAME%\\");
            return t;
        }

        static readonly string[][] Known =
        {
            new[] { "minimum required nvidia driver|driver does not support the required nvenc|nvenc api version", "Your NVIDIA driver is too old",
                    "Update it with the NVIDIA app or from nvidia.com, then restart Clutch. Until then, pick another encoder in Settings > Recording." },
            new[] { "887a0026|887a0027|access_lost", "Screen capture was interrupted",
                    "Windows pauses screen capture for the lock screen, admin prompts and games that switch display mode. Clutch restarts it by itself. If it keeps happening, play in borderless windowed mode." },
            new[] { "887a0004|dxgi_error_unsupported|duplicateoutput", "Windows wouldn't share the screen",
                    "This can happen on some laptops with two graphics chips. In Windows Settings > Display > Graphics, set Clutch to the same GPU as your monitor." },
            new[] { "not enough space on the disk|no space left on device|disk full", "Your drive is full",
                    "Free up some space, or move your clips to another drive in Settings > Storage." },
            new[] { "unauthorizedaccessexception|access to the path", "Clutch couldn't write a file",
                    "The clips folder may be read-only or protected by antivirus (Controlled folder access). Pick another folder in Settings > Storage." },
            new[] { "88890004|device_invalidated", "An audio device was unplugged",
                    "Pick your speakers or mic again in Settings > Audio." },
            new[] { "80070005", "Windows blocked the microphone or a file",
                    "If it's the mic, turn on Windows Settings > Privacy & security > Microphone > Let desktop apps access your microphone." },
            new[] { "outofmemoryexception|insufficient memory", "Your PC ran out of memory",
                    "Close other apps, or use a shorter clip length or lower quality in Settings > Recording." },
            new[] { "ffmpeg.exe|ffprobe.exe|checksum", "FFmpeg is missing or damaged",
                    "Download it again in Settings > Storage." },
            new[] { "webexception|couldn't reach|remote name could not be resolved", "Clutch couldn't reach the internet",
                    "Check your connection. Recording and clipping work offline; only downloads need it." },
        };

        public static List<string[]> Causes(string report, bool crashed)
        {
            List<string[]> found = new List<string[]>();
            StringBuilder use = new StringBuilder(); bool skip = false;
            DateTime now = DateTime.Now, at;
            foreach (string l in report.Split('\n'))
            {
                if (l.Length >= 19 && DateTime.TryParseExact(l.Substring(0, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out at))
                    skip = at < now.AddMinutes(crashed ? -15 : -24 * 60);
                if (!skip) use.AppendLine(l);
            }
            string t = use.ToString().ToLowerInvariant();
            foreach (string[] k in Known)
            {
                foreach (string p in k[0].Split('|'))
                    if (t.Contains(p)) { found.Add(new[] { k[1], k[2] }); break; }
                if (found.Count == 2) break;
            }
            return found;
        }
    }

    class ReportWindow : Window
    {
        readonly bool crashed; readonly string text;
        readonly List<Card> causes = new List<Card>();
        readonly Lbl hReport = new Lbl("Report", 11, 4, C.Text), note = new Lbl("", 9, 0, C.Sub);
        readonly TextView view = new TextView();
        readonly Btn copy = new Btn("Copy report", Glyph.Copy, BtnStyle.Accent), save = new Btn("Save as file", Glyph.Save, BtnStyle.Standard),
                     restart = new Btn("Restart Clutch", "", BtnStyle.Standard), close = new Btn("Close", "", BtnStyle.Subtle);

        public ReportWindow(string reportText, bool crashed) : base(crashed ? "Clutch crashed" : "Report a problem", 600, 0)
        {
            this.crashed = crashed; text = reportText;
            view.Box.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            note.Text = "Only what you see here is in the report. Nothing is sent automatically: copy it and send it to the Clutch developer.";
            note.Wraps = true;
            foreach (string[] c in Report.Causes(text, crashed)) causes.Add(CauseCard(c[0], c[1]));
            if (causes.Count == 0 && crashed) causes.Add(CauseCard("No known cause", "Sending the report helps get this fixed. Your clips and settings are safe."));
            copy.Click += delegate
            {
                try { Clipboard.SetText(text); copy.Text = "Copied"; copy.Icon = Glyph.Check; copy.Invalidate(); } catch { }
            };
            save.Click += delegate { SaveFile(); };
            restart.Click += delegate { try { Process.Start(Application.ExecutablePath); } catch { } Close(); };
            close.Click += delegate { Close(); };
            restart.Visible = crashed;
            foreach (Card c in causes) Controls.Add(c);
            foreach (Control c in new Control[] { hReport, note, view, copy, save, restart, close }) Controls.Add(c);
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        static Card CauseCard(string title, string fix)
        {
            Card c = new Card(); c.Fill = C.Card;
            Lbl t = new Lbl(title, 10, 5, C.Text), f = new Lbl(fix, 9, 0, C.Sub), i = new Lbl(Glyph.Warning, 12, 3, C.Warn); f.Wraps = true;
            c.Controls.Add(t); c.Controls.Add(f); c.Controls.Add(i);
            c.Tag = new Lbl[] { t, f, i };
            return c;
        }

        protected override void Relayout()
        {
            int x = S(28), w = S(600) - S(56), y = S(92);
            foreach (Card c in causes)
            {
                Lbl[] l = (Lbl[])c.Tag; int iw = w - S(52);
                l[0].SetBounds(S(44), S(12), iw, S(22)); l[2].SetBounds(S(14), S(12), S(24), S(22));
                int fh = l[1].Fit(iw); l[1].SetBounds(S(44), S(36), iw, fh);
                c.SetBounds(x, y, w, S(48) + fh); y = c.Bottom + S(10);
            }
            y += S(10);
            hReport.SetBounds(x, y, w, S(24)); y += S(28);
            int nh = note.Fit(w); note.SetBounds(x, y, w, nh); y += nh + S(10);
            view.SetBounds(x, y, w, S(190)); y = view.Bottom + S(20);
            copy.SetBounds(x, y, copy.PreferredWidth() + S(10), S(38));
            save.SetBounds(copy.Right + S(10), y, save.PreferredWidth() + S(6), S(38));
            close.SetBounds(x + w - S(90), y, S(90), S(38));
            restart.SetBounds(close.Left - S(10) - restart.PreferredWidth() - S(6), y, restart.PreferredWidth() + S(6), S(38));
            ClientSize = new Size(S(600), y + S(38) + S(26));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawHeader(e.Graphics, crashed ? "Clutch ran into a problem" : "Report a problem",
                crashed ? "Clutch had to close. Your clips and settings are safe." : "A summary of your setup and recent errors, to help fix it.");
        }

        void SaveFile()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.FileName = "Clutch report " + DateTime.Now.ToString("yyyy-MM-dd HHmm") + ".txt"; d.Filter = "Text file|*.txt";
                d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (d.ShowDialog(this) == DialogResult.OK) try { File.WriteAllText(d.FileName, text, Encoding.UTF8); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Clutch"); }
            }
        }

        public static void RunCrashed(string file)
        {
            string t = "";
            try { t = File.ReadAllText(file); } catch { t = "The crash report couldn't be read."; }
            ReportWindow w = new ReportWindow(t, true);
            w.Shown += delegate { w.Front(); };
            Application.Run(w);
        }
    }
}
