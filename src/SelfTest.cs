#if DEV
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Clutch
{
    static class SelfTest
    {
        static StringBuilder report = new StringBuilder();
        static string reportPath;
        static void R(string s) { report.AppendLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s); try { File.WriteAllText(reportPath, report.ToString()); } catch { } }

        public static void Download(string reportFile)
        {
            reportPath = reportFile;
            Stopwatch sw = Stopwatch.StartNew(); int last = -1;
            try
            {
                FFmpeg.Download(delegate (long d, long t) { int p = t > 0 ? (int)(d * 100 / t) : 0; if (p / 10 != last) { last = p / 10; R("progress " + p + "% of " + t); } }, delegate { return false; });
                R("OK in " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s. version=" + FFmpeg.Version + " ready=" + FFmpeg.Ready);
            }
            catch (Exception ex) { R("FAILED: " + ex); }
        }

        static Form flash;

        public static void Run(string folder)
        {
            reportPath = Path.Combine(folder, "report.txt");
            Directory.CreateDirectory(folder);
            Settings.Cur.ClipsFolder = folder; Settings.Cur.Mic = false; Settings.Cur.DesktopAudio = true; Settings.Cur.BufferSeconds = 30;
            Screen prim = Screen.PrimaryScreen;
            flash = new Form();
            flash.FormBorderStyle = FormBorderStyle.None; flash.StartPosition = FormStartPosition.Manual; flash.TopMost = true; flash.ShowInTaskbar = false;
            flash.BackColor = Color.Black; flash.Bounds = new Rectangle(prim.Bounds.X + prim.Bounds.Width / 2 - 150, prim.Bounds.Y + prim.Bounds.Height / 2 - 150, 300, 300);
            flash.Shown += delegate { Thread t = new Thread(Work); t.IsBackground = true; t.Start(); };
            Application.Run(flash);
        }

        static void UI(Action a) { try { flash.Invoke(a); } catch { } }

        static void Work()
        {
            try
            {
                R("GPUs: " + Encoder.GpuSummary());
                foreach (Gpu g in Encoder.Gpus) R("  adapter: " + g.Name + " [" + g.Vendor + "]");
                Stopwatch sw = Stopwatch.StartNew();
                Settings.Cur.ProbeKey = "";
                string choice = Encoder.Choose(delegate (string s) { R("  " + s); });
                R("Encoder choice: " + choice + " (probe took " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s)");
                Recorder.EncoderUsed = choice;

                Recorder.Start();
                for (int i = 0; i < 100 && Recorder.Now != Recorder.State.Recording; i++) Thread.Sleep(100);
                R("Recorder state: " + Recorder.Now + " / " + Recorder.Status);
                if (Recorder.Now != Recorder.State.Recording) { R("FAILED to start"); Finish(); return; }

                Process ff = null;
                foreach (Process p in Process.GetProcessesByName("ffmpeg")) { try { if (p.MainModule.FileName.StartsWith(FFmpeg.Dir, StringComparison.OrdinalIgnoreCase)) ff = p; } catch { } }
                Process me = Process.GetCurrentProcess();
                TimeSpan ff0 = ff != null ? ff.TotalProcessorTime : TimeSpan.Zero, me0 = me.TotalProcessorTime; Stopwatch cpu = Stopwatch.StartNew();
                Thread.Sleep(3000);

                SoundPlayer beep = new SoundPlayer(Tone());
                beep.Load();
                List<double> marks = new List<double>();
                for (int k = 0; k < 3; k++)
                {
                    UI(delegate { flash.BackColor = Color.White; flash.Refresh(); beep.Play(); });
                    marks.Add(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
                    Thread.Sleep(250);
                    UI(delegate { flash.BackColor = Color.Black; flash.Refresh(); });
                    Thread.Sleep(1750);
                }
                Thread.Sleep(3000);
                double el = cpu.Elapsed.TotalSeconds; ff.Refresh(); me.Refresh();
                double ffCpu = ff != null ? (ff.TotalProcessorTime - ff0).TotalSeconds / el / Environment.ProcessorCount * 100 : -1;
                double meCpu = (me.TotalProcessorTime - me0).TotalSeconds / el / Environment.ProcessorCount * 100;
                R("CPU (share of whole PC): ffmpeg " + ffCpu.ToString("0.0") + "%, Clutch " + meCpu.ToString("0.0") + "%  over " + el.ToString("0.0") + " s");
                if (ff != null) R("ffmpeg memory: " + (ff.WorkingSet64 >> 20) + " MB; Clutch memory: " + (me.WorkingSet64 >> 20) + " MB; buffer holds " + (Recorder.BytesHeld >> 20) + " MB / " + Recorder.SecondsHeld.ToString("0.0") + " s of video");

                sw = Stopwatch.StartNew();
                Recorder.SaveResult res = Recorder.Save(12, "Self test");
                R("Save: ok=" + res.Ok + " in " + sw.ElapsedMilliseconds + " ms, " + res.Seconds.ToString("0.00") + " s, " + res.Path + " " + res.Error);
                if (!res.Ok) { Recorder.Stop(); Finish(); return; }

                string clip = res.Path;
                R("Probe: duration " + FFmpeg.Duration(clip).ToString("0.00") + " s, size " + (new FileInfo(clip).Length >> 10) + " KB, audio tracks [" + string.Join(", ", FFmpeg.AudioTracks(clip).ToArray()) + "]");
                FFmpeg.Result info = FFmpeg.Run(FFmpeg.ProbeExe, "-v error -show_entries stream=codec_name,width,height,avg_frame_rate,sample_rate,channels -of compact " + FFmpeg.Q(clip), 20000, null);
                R("Streams:\n" + info.Out.Trim());

                SyncCheck(clip, marks);
                Recorder.Stop();

                string thumb = Path.Combine(Path.GetDirectoryName(clip), "thumb.jpg");
                R("Thumbnail: " + FFmpeg.Thumbnail(clip, thumb, 1));

                TrimPage tp = null;
                UI(delegate { tp = new TrimPage(null); });
                foreach (int mb in new[] { 0, 10 })
                {
                    string outp = Path.Combine(Path.GetDirectoryName(clip), "export_" + mb + ".mp4");
                    sw = Stopwatch.StartNew();
                    string err = tp.TestExport(clip, outp, 2.3, 8.0, mb, 1.0, 0.5);
                    R("Export " + (mb == 0 ? "original" : mb + " MB") + ": " + (err ?? "ok") + " in " + sw.ElapsedMilliseconds + " ms" + (File.Exists(outp) ? ", " + (new FileInfo(outp).Length >> 10) + " KB, " + FFmpeg.Duration(outp).ToString("0.00") + " s" : ""));
                }
            }
            catch (Exception ex) { R("EXCEPTION " + ex); }
            Finish();
        }

        static void Finish() { R("done"); UI(delegate { flash.Close(); }); }

        static MemoryStream Tone()
        {
            int rate = 48000, n = rate / 10;
            MemoryStream m = new MemoryStream(); BinaryWriter w = new BinaryWriter(m);
            w.Write(0x46464952); w.Write(36 + n * 2); w.Write(0x45564157); w.Write(0x20746D66);
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(0x61746164); w.Write(n * 2);
            for (int i = 0; i < n; i++) w.Write((short)(Math.Sin(2 * Math.PI * 1000 * i / rate) * 0.5 * 32767));
            m.Position = 0; return m;
        }

        static void SyncCheck(string clip, List<double> marks)
        {
            string wav = Path.Combine(Paths.Temp, "ring.wav");
            foreach (double m in marks)
            {
                if (!Recorder.Desktop.WriteWav(m - 0.05, m + 0.6, wav)) continue;
                byte[] w = File.ReadAllBytes(wav); int rate = BitConverter.ToInt32(w, 24);
                for (int i = 44; i + 3 < w.Length; i += 4)
                    if (Math.Abs((int)BitConverter.ToInt16(w, i)) > 6000) { R("  beep reached the speakers " + (((i - 44) / 4.0 / rate - 0.05) * 1000).ToString("0") + " ms after it was played"); break; }
            }
            try { File.Delete(wav); } catch { }
            Rectangle mon = Recorder.MonitorBounds(0), f = flash.Bounds;
            int cx = f.X - mon.X + 100, cy = f.Y - mon.Y + 100;
            string raw = Path.Combine(Paths.Temp, "luma.raw"), pcm = Path.Combine(Paths.Temp, "audio.raw");
            FFmpeg.Run(FFmpeg.Exe, "-y -v error -i " + FFmpeg.Q(clip) + " -map 0:v:0 -vf \"crop=100:100:" + cx + ":" + cy + ",scale=1:1,format=gray\" -fps_mode passthrough -f rawvideo " + FFmpeg.Q(raw), 60000, null);
            FFmpeg.Result ts = FFmpeg.Run(FFmpeg.ProbeExe, "-v error -select_streams v:0 -show_entries frame=pts_time -of csv=p=0 " + FFmpeg.Q(clip), 60000, null);
            FFmpeg.Run(FFmpeg.Exe, "-y -v error -i " + FFmpeg.Q(clip) + " -map 0:a:0 -ac 1 -ar 8000 -f s16le " + FFmpeg.Q(pcm), 60000, null);
            byte[] luma = File.Exists(raw) ? File.ReadAllBytes(raw) : new byte[0];
            List<double> times = new List<double>();
            foreach (string l in ts.Out.Split('\n')) { double v; if (double.TryParse(l.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) times.Add(v); }
            List<double> vOn = new List<double>();
            for (int i = 1; i < luma.Length && i < times.Count; i++) if (luma[i] > 128 && luma[i - 1] <= 128) vOn.Add(times[i] - times[0]);
            List<double> aOn = new List<double>();
            if (File.Exists(pcm))
            {
                byte[] a = File.ReadAllBytes(pcm); int n = a.Length / 2; double lastOn = -10;
                for (int i = 0; i < n; i++)
                {
                    short s = BitConverter.ToInt16(a, i * 2);
                    if (Math.Abs(s) > 6000 && i / 8000.0 - lastOn > 1) { aOn.Add(i / 8000.0); lastOn = i / 8000.0; }
                }
            }
            R("Flash seen in video at: " + Join(vOn) + "  (frames: " + luma.Length + ")");
            R("Beep heard in audio at: " + Join(aOn));
            for (int i = 0; i < Math.Min(vOn.Count, aOn.Count); i++) R("  marker " + (i + 1) + ": audio minus video = " + ((aOn[i] - vOn[i]) * 1000).ToString("0") + " ms");
            for (int i = 0; i < Math.Min(vOn.Count, marks.Count); i++) R("  marker " + (i + 1) + ": flash shows in the clip " + ((Recorder.LastClipStart + vOn[i] - marks[i]) * 1000).ToString("0") + " ms after it was drawn");
            for (int i = 0; i < Math.Min(aOn.Count, marks.Count); i++) R("  marker " + (i + 1) + ": beep shows in the clip " + ((Recorder.LastClipStart + aOn[i] - marks[i]) * 1000).ToString("0") + " ms after it was played");
            try { File.Delete(raw); File.Delete(pcm); } catch { }
        }

        static string Join(List<double> l) { List<string> s = new List<string>(); foreach (double d in l) s.Add(d.ToString("0.000")); return string.Join(", ", s.ToArray()); }
    }
}

#endif
