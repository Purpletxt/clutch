using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Clutch
{
    class Chunk { public long Pts; public readonly List<byte[]> Pages = new List<byte[]>(); public int Length; }

    static class Recorder
    {
        public enum State { Off, Starting, Recording, Error }

        public static volatile State Now = State.Off;
        public static volatile string Status = "";
        public static string EncoderUsed = "";
        public static double LastClipStart;
        public static event Action Changed;
        public static readonly AudioSource Desktop = new AudioSource(true, "desktop");
        public static readonly AudioSource Mic = new AudioSource(false, "mic");
        public static readonly AudioSource Game = new AudioSource(true, "game"), Other = new AudioSource(true, "other");
        static int gamePid;

        public static bool SplitActive { get { return Game.Running && Other.Running; } }

        public static void SetGame(int pid)
        {
            Settings s = Settings.Cur;
            bool want = wantRunning && s.DesktopAudio && s.SplitGameAudio && pid > 0 && ProcessLoopback.Supported;
            if (!want) { if (Game.Running) Game.Stop(); if (Other.Running) Other.Stop(); gamePid = 0; return; }
            if (pid == gamePid && Game.Running && Other.Running) return;
            gamePid = pid;
            Game.StartProcess(pid, false, s.BufferSeconds); Other.StartProcess(pid, true, s.BufferSeconds);
        }

        static Process proc;
        static Thread reader, starter;
        static volatile bool wantRunning;
        static readonly object gate = new object();
        static readonly List<Chunk> chunks = new List<Chunk>();
        static byte[] pat, pmt;
        static long lastPts = -1, wrapAdd;
        static double minOffset = double.MaxValue;
        static int keepSeconds = 62, fps = 60;
        static long bytesHeld;
        const int PageSize = 188 * 348;
        static readonly Stack<byte[]> pool = new Stack<byte[]>();
        static int saveHold;
        static int restarts;
        static DateTime launchedAt;
        static readonly System.Text.StringBuilder errTail = new System.Text.StringBuilder();

        public static long BytesHeld { get { return bytesHeld; } }
        public static double SecondsHeld
        {
            get { lock (gate) return chunks.Count == 0 || lastPts < 0 ? 0 : (lastPts - chunks[0].Pts) / 90000.0; }
        }

        static void Fire(State s, string status)
        {
            Now = s; Status = status;
            Action a = Changed; if (a != null) try { a(); } catch { }
        }

        static double Clock() { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; }

        public static void Start()
        {
            if (Program.DevRun && !Program.SelfTesting) return;
            wantRunning = true;
            if (starter != null && starter.IsAlive) return;
            if (proc != null) return;
            restarts = 0;
            Fire(State.Starting, "Starting\u2026");
            starter = new Thread(StartWorker); starter.IsBackground = true; starter.Start();
        }

        public static void Stop()
        {
            wantRunning = false;
            KillProcess();
            Desktop.Stop(); Mic.Stop(); Game.Stop(); Other.Stop(); gamePid = 0;
            lock (gate) { chunks.Clear(); pool.Clear(); bytesHeld = 0; lastPts = -1; }
            Fire(State.Off, "Off");
        }

        public static void ApplyAudio()
        {
            if (!wantRunning) return;
            Settings s = Settings.Cur;
            if (s.DesktopAudio) Desktop.Start(s.DesktopDevice, s.BufferSeconds); else Desktop.Stop();
            if (s.Mic) Mic.Start(s.MicDevice, s.BufferSeconds); else Mic.Stop();
            gamePid = 0; SetGame(Games.RunningPid);
        }

        static void KillProcess()
        {
            Process p = proc; proc = null;
            if (p != null)
            {
                try { p.StandardInput.Write("q"); p.StandardInput.Flush(); } catch { }
                try { if (!p.WaitForExit(1500)) p.Kill(); } catch { }
                try { p.Dispose(); } catch { }
            }
            Thread r = reader; reader = null;
            if (r != null && r != Thread.CurrentThread) r.Join(1500);
        }

        static void StartWorker()
        {
            try
            {
                Settings s = Settings.Cur;
                if (!FFmpeg.Ready) { Fire(State.Error, "FFmpeg isn't installed yet"); return; }
                string choice = EncoderUsed;
                if (choice == "" || Settings.Cur.ProbeKey != Encoder.Key()) choice = Encoder.Choose(delegate (string st) { Fire(State.Starting, st); });
                if (!wantRunning) return;
                if (choice == "") { Fire(State.Error, "Screen capture didn't start. See the error log."); return; }
                EncoderUsed = choice;

                if (s.DesktopAudio) Desktop.Start(s.DesktopDevice, s.BufferSeconds); else Desktop.Stop();
                if (s.Mic) Mic.Start(s.MicDevice, s.BufferSeconds); else Mic.Stop();
                gamePid = 0; SetGame(Games.RunningPid);
                Launch();
            }
            catch (Exception ex) { Log.Error("Recorder start", ex); Fire(State.Error, ex.Message); }
        }

        static void Launch()
        {
            Settings s = Settings.Cur;
            string[] parts = EncoderUsed.Split(':');
            fps = s.Fps; keepSeconds = s.BufferSeconds + 2;
            int mon; Rectangle b, cap; CaptureRect(out mon, out b, out cap);
            Captured = cap;
            Size outSize = OutputSize(); Size src = outSize.IsEmpty ? cap.Size : outSize;
            int kbps = Bitrate(src.Width, src.Height, fps, s.Quality);
            string region = cap == b ? "" : ":offset_x=" + (cap.X - b.X) + ":offset_y=" + (cap.Y - b.Y) + ":video_size=" + cap.Width + "x" + cap.Height;
            usedRegion = region.Length > 0;
            string args = "-hide_banner -loglevel error -nostats -f lavfi -i ddagrab=output_idx=" + mon + region + ":framerate=" + fps + ":draw_mouse=1 "
                        + Encoder.Args(parts[0], parts.Length > 1 && parts[1] == "1", fps, kbps, outSize)
                        + " -flags +global_header -bsf:v dump_extra=freq=keyframe -an -f mpegts -mpegts_start_pid 256 -mpegts_pmt_start_pid 4096 -muxdelay 0 -muxpreload 0 -flush_packets 1 pipe:1";
            lock (gate) { foreach (Chunk c in chunks) Recycle(c); chunks.Clear(); bytesHeld = 0; lastPts = -1; wrapAdd = 0; minOffset = double.MaxValue; pat = pmt = null; }
            lock (errTail) errTail.Length = 0;

            Process p = new Process();
            p.StartInfo = FFmpeg.Info(FFmpeg.Exe, args);
            p.ErrorDataReceived += delegate (object o, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (errTail) { errTail.AppendLine(e.Data); if (errTail.Length > 4000) errTail.Remove(0, errTail.Length - 4000); }
            };
            p.Start();
            FFmpeg.Track(p);
            try { p.PriorityClass = Encoder.UsesCpu(parts[0], parts.Length > 1 && parts[1] == "1", !outSize.IsEmpty) ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal; } catch { }
            launchedAt = DateTime.Now;
            p.BeginErrorReadLine();
            proc = p;
            reader = new Thread(delegate () { ReadLoop(p); }); reader.IsBackground = true; reader.Name = "TS reader"; reader.Start();
            Fire(State.Starting, "Starting\u2026");
        }

        public static Rectangle MonitorBounds(int index)
        {
            Screen[] all = Screen.AllScreens;
            List<Screen> ordered = new List<Screen>();
            foreach (Screen sc in all) if (sc.Primary) ordered.Add(sc);
            foreach (Screen sc in all) if (!sc.Primary) ordered.Add(sc);
            return ordered[Math.Max(0, Math.Min(ordered.Count - 1, index))].Bounds;
        }

        public static Rectangle Area = Rectangle.Empty, Captured = Rectangle.Empty;
        public static bool RegionBroken;
        static bool usedRegion;

        static int MonitorCount { get { return Screen.AllScreens.Length; } }

        public static int MonitorFor(Rectangle r)
        {
            Point c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            for (int i = 0; i < MonitorCount; i++) if (MonitorBounds(i).Contains(c)) return i;
            return -1;
        }

        static void CaptureRect(out int mon, out Rectangle bounds, out Rectangle cap)
        {
            mon = Settings.Cur.Monitor; bounds = MonitorBounds(mon); cap = bounds;
            if (Settings.Cur.CaptureMode != 0 || Area.IsEmpty || RegionBroken) return;
            int m = MonitorFor(Area); if (m < 0) return;
            Rectangle b = MonitorBounds(m), a = Rectangle.Intersect(Area, b);
            a.Width &= ~1; a.Height &= ~1;
            if (a.Width < 160 || a.Height < 120) return;
            mon = m; bounds = b; cap = a;
        }

#if DEV
        public static string DescribeCapture() { int m; Rectangle b, c; CaptureRect(out m, out b, out c); return "monitor " + m + " " + b + " capture " + c + (c == b ? " (whole monitor)" : " offset " + (c.X - b.X) + "," + (c.Y - b.Y)); }
#endif

        public static void Relaunch()
        {
            if (!wantRunning || proc == null || EncoderUsed == "") return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                lock (relaunchGate)
                {
                    if (!wantRunning || proc == null) return;
                    KillProcess();
                    try { if (wantRunning) Launch(); } catch (Exception ex) { Log.Error("Recorder relaunch", ex); }
                }
            });
        }
        static readonly object relaunchGate = new object();

        public static Size OutputSize()
        {
            int h = Settings.Cur.Resolution == 1 ? 1080 : Settings.Cur.Resolution == 2 ? 720 : 0;
            Rectangle b = Captured.IsEmpty ? MonitorBounds(Settings.Cur.Monitor) : Captured;
            if (h == 0 || b.Height <= h) return Size.Empty;
            int w = (int)Math.Round(b.Width * (double)h / b.Height / 2) * 2;
            return new Size(w, h);
        }

        public static int Bitrate(int w, int h, int fps, int quality)
        {
            double bpp = quality == 0 ? 0.04 : quality == 2 ? 0.1 : 0.06;
            return (int)Math.Max(2500, Math.Min(80000, w * (double)h * fps * bpp / 1000));
        }

        static void ReadLoop(Process p)
        {
            Stream s;
            try { s = p.StandardOutput.BaseStream; } catch { return; }
            byte[] buf = new byte[188 * 512]; int have = 0;
            Chunk cur = null;
            bool announced = false;
            try
            {
                while (true)
                {
                    int n = s.Read(buf, have, buf.Length - have);
                    if (n <= 0) break;
                    have += n;
                    int pos = 0;
                    double arrival = Clock();
                    while (have - pos >= 188)
                    {
                        if (buf[pos] != 0x47) { pos++; continue; }
                        Packet(buf, pos, arrival, ref cur);
                        pos += 188;
                    }
                    if (pos > 0) { Buffer.BlockCopy(buf, pos, buf, 0, have - pos); have -= pos; }
                    if (!announced && lastPts >= 0) { announced = true; Fire(State.Recording, "Recording"); }
                }
            }
            catch (Exception ex) { if (proc == p) Log.Error("TS reader", ex); }

            if (proc == p && wantRunning)
            {
                string tail; lock (errTail) tail = errTail.ToString();
                Log.Error("Recorder", "FFmpeg stopped (capturing " + Captured.Width + "x" + Captured.Height + " at " + Captured.X + "," + Captured.Y + " of monitor " + MonitorBounds(Settings.Cur.Monitor) + "): " + Encoder.Short(tail));
                proc = null;
                try { p.Dispose(); } catch { }
                if (!RegionBroken && usedRegion && (DateTime.Now - launchedAt).TotalSeconds < 10)
                {
                    RegionBroken = true; Area = Rectangle.Empty;
                    Log.Info("Recorder", "Game window capture failed on this setup (area " + Captured + "), recording the whole screen instead");
                }
                if ((DateTime.Now - launchedAt).TotalSeconds > 60) restarts = 0;
                if (restarts++ < 5)
                {
                    Fire(State.Starting, "Reconnecting\u2026");
                    Thread.Sleep(2000 * restarts * restarts);
                    if (wantRunning && proc == null) { if (restarts == 3) EncoderUsed = ""; try { if (EncoderUsed == "") StartWorker(); else Launch(); } catch (Exception ex) { Log.Error("Recorder restart", ex); } }
                }
                else Fire(State.Error, "Screen capture keeps stopping. See the error log.");
            }
        }

        static void Packet(byte[] b, int o, double arrival, ref Chunk cur)
        {
            int pid = ((b[o + 1] & 0x1F) << 8) | b[o + 2];
            bool start = (b[o + 1] & 0x40) != 0;
            int afc = (b[o + 3] >> 4) & 3;
            int payload = o + 4;
            bool rai = false;
            if ((afc & 2) != 0)
            {
                int alen = b[o + 4];
                if (alen > 0) rai = (b[o + 5] & 0x40) != 0;
                payload += 1 + alen;
            }
            if (pid == 0) { pat = Copy(b, o); return; }
            if (pid == 4096) { pmt = Copy(b, o); return; }
            if (pid == 256 && start && (afc & 1) != 0 && payload + 14 <= o + 188 && b[payload] == 0 && b[payload + 1] == 0 && b[payload + 2] == 1)
            {
                if ((b[payload + 7] & 0x80) != 0)
                {
                    int q = payload + 9;
                    long pts = ((long)((b[q] >> 1) & 7) << 30) | ((long)b[q + 1] << 22) | ((long)(b[q + 2] >> 1) << 15) | ((long)b[q + 3] << 7) | ((long)b[q + 4] >> 1);
                    pts += wrapAdd;
                    if (lastPts >= 0 && pts < lastPts - (1L << 32)) { wrapAdd += 1L << 33; pts += 1L << 33; }
                    double off = arrival - pts / 90000.0;
                    if (off < minOffset) minOffset = off;
                    if (rai)
                    {
                        lock (gate)
                        {
                            if (cur != null) Trim();
                            cur = new Chunk(); cur.Pts = pts;
                            chunks.Add(cur);
                        }
                    }
                    lastPts = pts;
                }
            }
            if (cur == null) return;
            lock (gate)
            {
                int at = cur.Length % PageSize;
                if (at == 0) cur.Pages.Add(pool.Count > 0 ? pool.Pop() : new byte[PageSize]);
                Buffer.BlockCopy(b, o, cur.Pages[cur.Pages.Count - 1], at, 188); cur.Length += 188; bytesHeld += 188;
            }
        }

        static byte[] Copy(byte[] b, int o) { byte[] c = new byte[188]; Buffer.BlockCopy(b, o, c, 0, 188); return c; }

        static void Trim()
        {
            if (saveHold > 0) return;
            while (chunks.Count > 2 && lastPts - chunks[1].Pts >= keepSeconds * 90000L)
            {
                bytesHeld -= chunks[0].Length; Recycle(chunks[0]); chunks.RemoveAt(0);
            }
        }

        static void Recycle(Chunk c)
        {
            foreach (byte[] p in c.Pages) pool.Push(p);
            c.Pages.Clear();
        }

        public class SaveResult { public bool Ok; public string Path, Error; public double Seconds; }

        static string Details(FFmpeg.Result r, long tsBytes, int pieces, double seconds)
        {
            string err = Encoder.Short(r.Err);
            return "exit " + (r.TimedOut ? "timed out" : r.Exit + " (0x" + r.Exit.ToString("X8") + ")") + ", " + (tsBytes >> 20) + " MB in " + pieces + " pieces, "
                + seconds.ToString("0.0") + " s, encoder " + (EncoderUsed.Length > 0 ? EncoderUsed : "none") + (err.Length > 0 ? ": " + err : ", ffmpeg printed nothing");
        }

        public static SaveResult Save(int seconds, string game)
        {
            SaveResult res = new SaveResult();
            List<Chunk> pick = new List<Chunk>(); byte[] patC, pmtC; long startPts, endPts; double offset;
            lock (gate)
            {
                if (chunks.Count == 0 || lastPts < 0 || pat == null || pmt == null) { res.Error = "The replay buffer is still empty. Try again in a second."; return res; }
                long cutoff = lastPts - seconds * 90000L;
                int first = 0;
                for (int i = 0; i < chunks.Count; i++) if (chunks[i].Pts <= cutoff) first = i;
                for (int i = first; i < chunks.Count; i++) pick.Add(chunks[i]);
                saveHold++;
                startPts = chunks[first].Pts; endPts = lastPts + 90000L / Math.Max(1, fps);
                patC = pat; pmtC = pmt; offset = minOffset;
            }
            string ts = Paths.NewTemp(".ts"), wavD = null, wavM = null, wavG = null, wavO = null;
            bool released = false;
            try
            {
                using (FileStream f = new FileStream(ts, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    f.Write(patC, 0, 188); f.Write(pmtC, 0, 188);
                    foreach (Chunk c in pick)
                    {
                        int len; byte[][] pages; lock (gate) { len = c.Length; pages = c.Pages.ToArray(); }
                        for (int i = 0; i < pages.Length && len > 0; i++) { int n = Math.Min(PageSize, len); f.Write(pages[i], 0, n); len -= n; }
                    }
                    released = true; lock (gate) saveHold--;
                }
                double lat = 1.0 / Math.Max(1, fps);
                double a = offset + startPts / 90000.0 - lat, b = offset + endPts / 90000.0 - lat;
                res.Seconds = (endPts - startPts) / 90000.0;
                LastClipStart = a;
                if (Desktop.Running) { wavD = Paths.NewTemp(".wav"); if (!Desktop.WriteWav(a, b, wavD)) wavD = null; }
                if (Mic.Running) { wavM = Paths.NewTemp(".wav"); if (!Mic.WriteWav(a, b, wavM)) wavM = null; }
                if (wavD != null && SplitActive && Game.Covers(a) && Other.Covers(a))
                {
                    wavG = Paths.NewTemp(".wav"); wavO = Paths.NewTemp(".wav");
                    if (!Game.WriteWav(a, b, wavG) || !Other.WriteWav(a, b, wavO)) { try { File.Delete(wavG); File.Delete(wavO); } catch { } wavG = wavO = null; }
                }

                Directory.CreateDirectory(Settings.Cur.Folder);
                string name = Library.SafeName((string.IsNullOrEmpty(game) ? "Clip" : game) + " " + DateTime.Now.ToString("d MMM HH.mm"));
                string outPath = Library.UniquePath(Path.Combine(Settings.Cur.Folder, name + ".mp4"));
                string part = outPath + ".part";

                string args = "-y -hide_banner -loglevel warning -fflags +genpts -i " + FFmpeg.Q(ts);
                int ni = 1, iD = -1, iM = -1, iG = -1, iO = -1;
                if (wavD != null) { args += " -i " + FFmpeg.Q(wavD); iD = ni++; }
                if (wavM != null) { args += " -i " + FFmpeg.Q(wavM); iM = ni++; }
                if (wavG != null) { args += " -i " + FFmpeg.Q(wavG); iG = ni++; }
                if (wavO != null) { args += " -i " + FFmpeg.Q(wavO); iO = ni++; }
                args += " -map 0:v:0 -c:v copy";
                bool split = iG > 0 && iO > 0;
                List<string> maps = new List<string>(), names = new List<string>();
                if (iD > 0 && iM > 0)
                {
                    args += " -filter_complex \"[" + iM + ":a]volume=" + FFmpeg.N(Settings.Cur.MicVolume / 100.0) + "[mv];[" + iD + ":a][mv]amix=inputs=2:duration=longest:normalize=0[mix]\"";
                    maps.Add("[mix]"); names.Add("Game + Mic");
                }
                else if (iD > 0) { maps.Add(iD + ":a"); names.Add("Game"); }
                else if (iM > 0) { maps.Add(iM + ":a"); names.Add("Mic"); }
                if (split || (iD > 0 && iM > 0))
                {
                    maps.Add((split ? iG : iD) + ":a"); names.Add(split ? "Game only" : "Game");
                    if (iM > 0) { maps.Add(iM + ":a"); names.Add("Mic"); }
                    if (split) { maps.Add(iO + ":a"); names.Add("Other apps"); }
                }
                if (maps.Count > 0)
                {
                    foreach (string m in maps) args += " -map " + m;
                    args += " -c:a aac -b:a 192k";
                    for (int i = 0; i < names.Count; i++) args += " -metadata:s:a:" + i + " handler_name=\"" + names[i] + "\" -disposition:a:" + i + (i == 0 ? " default" : " 0");
                }
                args += " -metadata comment=\"Saved with Clutch\" -movflags +faststart -f mp4 " + FFmpeg.Q(part);

                long tsBytes = new FileInfo(ts).Length;
                int timeout = (int)Math.Min(600000, 60000 + tsBytes / 1000);
                FFmpeg.Result r = FFmpeg.Run(FFmpeg.Exe, args, timeout, null);
                if (r.Exit != 0 || !File.Exists(part))
                {
                    Log.Error("Save clip", Details(r, tsBytes, pick.Count, res.Seconds));
                    try { File.Delete(part); } catch { }
                    string enc = (EncoderUsed.Length > 0 ? EncoderUsed : "x264").Split(':')[0];
                    int kbps = Math.Max(4000, (int)(tsBytes * 8 / 1000 / Math.Max(1, res.Seconds)));
                    string again = args.Replace(" -c:v copy", " " + Encoder.FileArgs(enc) + " -b:v " + kbps + "k");
                    r = FFmpeg.Run(FFmpeg.Exe, again, Math.Max(timeout, 300000), null);
                    if ((r.Exit != 0 || !File.Exists(part)) && enc != "x264")
                    {
                        Log.Error("Save clip", "re-encode with " + enc + " failed. " + Details(r, tsBytes, pick.Count, res.Seconds));
                        try { File.Delete(part); } catch { }
                        r = FFmpeg.Run(FFmpeg.Exe, again.Replace(Encoder.FileArgs(enc), Encoder.FileArgs("x264")), Math.Max(timeout, 300000), null);
                    }
                    if (r.Exit != 0 || !File.Exists(part))
                    {
                        Log.Error("Save clip", "re-encode failed too. " + Details(r, tsBytes, pick.Count, res.Seconds));
                        res.Error = "Couldn't save the clip. Settings > General > Report a problem has the details.";
                        try { File.Delete(part); } catch { }
                        return res;
                    }
                    Log.Info("Save clip", "saved by re-encoding instead of copying");
                }
                File.Move(part, outPath);
                res.Ok = true; res.Path = outPath;
            }
            catch (Exception ex) { res.Error = ex.Message; Log.Error("Save clip", ex); }
            finally
            {
                if (!released) lock (gate) saveHold--;
                foreach (string f in new[] { ts, wavD, wavM, wavG, wavO }) if (f != null) try { File.Delete(f); } catch { }
            }
            return res;
        }
    }
}
