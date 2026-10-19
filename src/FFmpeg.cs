using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Clutch
{
    static class FFmpeg
    {
        public const string PinnedVersion = "7.0.2-essentials_build";
        public const string ZipUrl = "https://github.com/GyanD/codexffmpeg/releases/download/7.0.2/ffmpeg-7.0.2-essentials_build.zip";
        public const string Sha256 = "d5308d30872b2739cf53169df61faba8639d39a19b20b91e611c177ef676f64c";
        public const long ZipSize = 91406388;
        public const string Source = "gyan.dev";

        public static string Dir { get { return Path.Combine(Paths.Local, "ffmpeg"); } }
        public static string Exe { get { return Path.Combine(Dir, "ffmpeg.exe"); } }
        public static string ProbeExe { get { return Path.Combine(Dir, "ffprobe.exe"); } }
        public static bool Ready { get { return File.Exists(Exe) && File.Exists(ProbeExe) && Version == PinnedVersion; } }

        public static string Version
        {
            get { try { string f = Path.Combine(Dir, "version.txt"); return File.Exists(f) ? File.ReadAllText(f).Trim() : ""; } catch { return ""; } }
        }

        public static void Download(Action<long, long> progress, Func<bool> cancelled)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            string expected = Sha256;
            string zip = Path.Combine(Paths.Local, "ffmpeg-download.zip");
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ZipUrl);
            req.UserAgent = "Clutch/" + AppInfo.Version;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream src = resp.GetResponseStream())
            using (FileStream dst = File.Create(zip))
            {
                long total = resp.ContentLength, done = 0; byte[] buf = new byte[1 << 16]; int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancelled()) { dst.Close(); File.Delete(zip); throw new OperationCanceledException(); }
                    dst.Write(buf, 0, n); done += n; progress(done, total);
                }
            }

            string actual;
            using (FileStream fs = File.OpenRead(zip)) using (SHA256 sha = SHA256.Create())
                actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            if (actual != expected)
            {
                File.Delete(zip);
                throw new Exception("The download didn't match the expected checksum, so it was thrown away. Please try again.");
            }

            string tmpDir = Dir + ".new";
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            Directory.CreateDirectory(tmpDir);
            string version = "";
            using (ZipArchive za = ZipFile.OpenRead(zip))
            {
                foreach (ZipArchiveEntry e in za.Entries)
                {
                    string name = e.FullName.Replace('\\', '/');
                    if (name.EndsWith("/bin/ffmpeg.exe") || name.EndsWith("/bin/ffprobe.exe"))
                        e.ExtractToFile(Path.Combine(tmpDir, e.Name), true);
                    if (name.EndsWith("/LICENSE") && name.Split('/').Length == 2) e.ExtractToFile(Path.Combine(tmpDir, "LICENSE.txt"), true);
                    if (version == "" && name.IndexOf('/') > 0) version = name.Substring(0, name.IndexOf('/'));
                }
            }
            File.Delete(zip);
            if (!File.Exists(Path.Combine(tmpDir, "ffmpeg.exe")) || !File.Exists(Path.Combine(tmpDir, "ffprobe.exe")))
                throw new Exception("The download didn't contain ffmpeg.exe and ffprobe.exe.");
            File.WriteAllText(Path.Combine(tmpDir, "version.txt"), version.Replace("ffmpeg-", ""));
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
            Directory.Move(tmpDir, Dir);
        }

        public class Result { public int Exit = -1; public string Out = "", Err = ""; public bool TimedOut; }

        public static ProcessStartInfo Info(string exe, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardError = true; psi.RedirectStandardOutput = true; psi.RedirectStandardInput = true;
            psi.WorkingDirectory = Paths.Temp;
            return psi;
        }

        public static Result Run(string exe, string args, int timeoutMs, Action<string> onLine)
        {
            Result r = new Result();
            StringBuilder err = new StringBuilder(), outp = new StringBuilder();
            using (Process p = new Process())
            {
                p.StartInfo = Info(exe, args);
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (err) { if (err.Length < 20000) err.AppendLine(e.Data); } };
                p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (outp) { if (outp.Length < 200000) outp.AppendLine(e.Data); }
                    if (onLine != null) try { onLine(e.Data); } catch { }
                };
                p.Start();
                Track(p);
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                p.BeginErrorReadLine(); p.BeginOutputReadLine();
                if (!p.WaitForExit(timeoutMs)) { r.TimedOut = true; try { p.Kill(); } catch { } p.WaitForExit(2000); }
                else p.WaitForExit();
                r.Exit = r.TimedOut ? -1 : p.ExitCode;
            }
            r.Err = err.ToString(); r.Out = outp.ToString();
            return r;
        }

        static IntPtr job;
        public static void Track(Process p)
        {
            try
            {
                if (job == IntPtr.Zero)
                {
                    IntPtr j = Native.CreateJobObject(IntPtr.Zero, null);
                    byte[] info = new byte[144]; info[17] = 0x20;
                    if (j != IntPtr.Zero && Native.SetInformationJobObject(j, 9, info, info.Length)) job = j;
                }
                if (job != IntPtr.Zero) Native.AssignProcessToJobObject(job, p.Handle);
            }
            catch { }
        }

        public static string Q(string path) { return "\"" + path + "\""; }
        public static string N(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        public static double Duration(string file)
        {
            if (!Ready) return 0;
            Result r = Run(ProbeExe, "-v error -show_entries format=duration -of csv=p=0 " + Q(file), 15000, null);
            double d; return double.TryParse(r.Out.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
        }

        public static List<string> AudioTracks(string file)
        {
            List<string> list = new List<string>();
            if (!Ready) return list;
            Result r = Run(ProbeExe, "-v error -select_streams a -show_entries stream=index:stream_tags=handler_name -of csv=p=0 " + Q(file), 15000, null);
            foreach (string line in r.Out.Split('\n'))
            {
                string l = line.Trim(); if (l.Length == 0) continue;
                int c = l.IndexOf(','); list.Add(c >= 0 ? l.Substring(c + 1) : "");
            }
            return list;
        }

        public static bool Thumbnail(string clip, string jpg, double at)
        {
            Result r = Run(Exe, "-y -hide_banner -loglevel error -ss " + N(at) + " -i " + Q(clip) + " -frames:v 1 -vf scale=480:-2 -q:v 4 " + Q(jpg), 20000, null);
            if (r.Exit != 0 || !File.Exists(jpg))
                r = Run(Exe, "-y -hide_banner -loglevel error -i " + Q(clip) + " -frames:v 1 -vf scale=480:-2 -q:v 4 " + Q(jpg), 20000, null);
            return File.Exists(jpg);
        }
    }
}
