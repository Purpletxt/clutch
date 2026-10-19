#if DEV
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Clutch
{
    static class E2ETest
    {
        public static string Folder;
        static MainForm f; static string log;
        static void R(string s) { File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\r\n"); }

        static void After(int ms, Action a)
        {
            Timer t = new Timer(); t.Interval = ms;
            t.Tick += delegate { t.Stop(); t.Dispose(); try { a(); } catch (Exception ex) { R("EXCEPTION " + ex); } };
            t.Start();
        }

        static void Shot(string name)
        {
            Rectangle r = f.Bounds;
            using (Bitmap b = new Bitmap(r.Width, r.Height))
            {
                using (Graphics g = Graphics.FromImage(b)) { IntPtr h = g.GetHdc(); Native.PrintWindow(f.Handle, h, 2); g.ReleaseHdc(h); }
                b.Save(Path.Combine(Folder, name + ".png"), ImageFormat.Png);
            }
        }

        public static void Start(MainForm form)
        {
            f = form; Directory.CreateDirectory(Folder); log = Path.Combine(Folder, "e2e.txt");
            R("start; recorder " + Recorder.Now + " / " + Recorder.Status + "; clips " + Library.Clips.Count);
            WaitRecording(0);
        }

        static void WaitRecording(int tries)
        {
            if (Recorder.Now == Recorder.State.Recording) { R("recording; " + f.Bar.StatusText()); After(7000, Save); return; }
            if (tries > 60) { R("FAILED: never started: " + Recorder.Status); f.Quit(); return; }
            After(500, delegate { WaitRecording(tries + 1); });
        }

        static void Save()
        {
            int before = Library.Clips.Count;
            Native.PostMessage(f.Handle, Native.WM_HOTKEY, (IntPtr)1, IntPtr.Zero);
            After(4000, delegate
            {
                R("clips before " + before + ", after " + Library.Clips.Count);
                if (Library.Clips.Count > before) { Clip c = Library.Clips[0]; R("new clip: " + c.Path + " " + c.Duration.ToString("0.0") + " s, thumb " + c.ThumbReady); }
                Shot("1-library");
                if (Library.Clips.Count > before) { f.OpenClip(Library.Clips[0]); After(3000, Player); }
                else Games();
            });
        }

        static void Player()
        {
            R("player: playing " + f.Viewer.Video.Playing + ", position " + f.Viewer.Video.Position.ToString("0.00") + ", length " + f.Viewer.Video.Length.ToString("0.00"));
            Shot("2-player");
            f.Viewer.Video.Pause();
            f.TrimClip(f.Viewer.Clip);
            After(4000, delegate { Shot("3-trim"); f.Go(f.Clips); Games(); });
        }

        static Process ping;
        static void Games()
        {
            Settings s = Settings.Cur;
            bool oldOnly = s.GamesOnly; System.Collections.Generic.List<GameEntry> oldGames = s.Games;
            s.Games = new System.Collections.Generic.List<GameEntry>(); GameEntry g = new GameEntry(); g.Exe = "PING.EXE"; g.Name = "Ping test"; s.Games.Add(g);
            s.GamesOnly = true; f.Sync();
            After(4500, delegate
            {
                R("games-only, no game running: want=" + f.WantRecording + " recorder=" + Recorder.Now + " status='" + f.Bar.StatusText() + "'");
                ProcessStartInfo psi = new ProcessStartInfo("ping.exe", "-n 14 127.0.0.1"); psi.CreateNoWindow = true; psi.UseShellExecute = false;
                ping = Process.Start(psi);
                After(9000, delegate
                {
                    R("ping running: game='" + Clutch.Games.Running + "' want=" + f.WantRecording + " recorder=" + Recorder.Now + " status='" + f.Bar.StatusText() + "'");
                    Shot("4-game-running");
                    try { ping.Kill(); } catch { }
                    After(4500, delegate
                    {
                        R("ping closed: game='" + Clutch.Games.Running + "' want=" + f.WantRecording + " recorder=" + Recorder.Now);
                        s.Games = oldGames; s.GamesOnly = oldOnly; Settings.Save(); f.Sync();
                        R("done");
                        f.Quit();
                    });
                });
            });
        }
    }
}

#endif
