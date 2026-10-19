using System;
using System.Collections.Generic;
using System.Management;
using System.Text;

namespace Clutch
{
    class Gpu { public string Name = "", Vendor = ""; }

    static class Encoder
    {
        static List<Gpu> gpus;

        public static List<Gpu> Gpus
        {
            get
            {
                if (gpus != null) return gpus;
                List<Gpu> list = new List<Gpu>();
                try
                {
                    using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_VideoController"))
                    foreach (ManagementObject o in s.Get())
                    {
                        Gpu g = new Gpu();
                        g.Name = Convert.ToString(o["Name"]) ?? "";
                        string pnp = (Convert.ToString(o["PNPDeviceID"]) ?? "").ToUpperInvariant();
                        g.Vendor = pnp.Contains("VEN_10DE") ? "nvidia" : pnp.Contains("VEN_1002") ? "amd" : pnp.Contains("VEN_8086") ? "intel" : "other";
                        list.Add(g);
                    }
                }
                catch (Exception ex) { Log.Error("GPU detection", ex); }
                gpus = list; return list;
            }
        }

        public static string Label(string enc)
        {
            switch (enc)
            {
                case "nvenc": return "NVIDIA NVENC";
                case "amf": return "AMD AMF";
                case "qsv": return "Intel Quick Sync";
                case "x264": return "Software (CPU)";
            }
            return enc;
        }

        public static string GpuSummary()
        {
            List<string> names = new List<string>();
            foreach (Gpu g in Gpus) if (g.Vendor != "other") names.Add(g.Name);
            return names.Count == 0 ? "No hardware encoder found" : string.Join(", ", names.ToArray());
        }

        public static List<string> Candidates()
        {
            List<string> c = new List<string>();
            string forced = Settings.Cur.Encoder;
            if (forced != "auto") { c.Add(forced); if (forced != "x264") c.Add("x264"); return c; }
            foreach (string v in new[] { "nvidia", "amd", "intel" })
                foreach (Gpu g in Gpus) if (g.Vendor == v) { string e = v == "nvidia" ? "nvenc" : v == "amd" ? "amf" : "qsv"; if (!c.Contains(e)) c.Add(e); }
            c.Add("x264");
            return c;
        }

        public static string Args(string enc, bool zeroCopy, int fps, int kbps, System.Drawing.Size out_)
        {
            bool shrink = !out_.IsEmpty;
            string size = out_.Width + ":" + out_.Height;
            string cpuScale = shrink ? ",scale=" + size + ":flags=fast_bilinear" : "";
            int gop = fps;
            string rate = " -b:v " + kbps + "k -maxrate " + (kbps * 3 / 2) + "k -bufsize " + (kbps * 2) + "k -g " + gop + " -bf 0";
            switch (enc)
            {
                case "nvenc":
                    return (zeroCopy && !shrink ? "" : "-vf hwdownload,format=bgra" + cpuScale + (shrink ? ",format=nv12 " : " ")) + "-c:v h264_nvenc -preset p4 -rc vbr" + rate;
                case "amf":
                    return (zeroCopy && !shrink ? "" : "-vf hwdownload,format=bgra" + cpuScale + ",format=nv12 ") + "-c:v h264_amf -usage transcoding -quality speed -rc vbr_peak" + rate;
                case "qsv":
                    return (zeroCopy ? "-vf hwmap=derive_device=qsv,format=qsv" + (shrink ? ",scale_qsv=w=" + out_.Width + ":h=" + out_.Height : "") + " " : "-vf hwdownload,format=bgra" + cpuScale + ",format=nv12 ") + "-c:v h264_qsv -preset veryfast" + rate;
                default:
                    return "-vf hwdownload,format=bgra" + cpuScale + ",format=yuv420p -c:v libx264 -preset superfast -tune zerolatency" + rate;
            }
        }

        public static string FileArgs(string enc)
        {
            switch (enc)
            {
                case "nvenc": return "-c:v h264_nvenc -preset p5 -pix_fmt yuv420p";
                case "amf": return "-c:v h264_amf -quality balanced -pix_fmt nv12";
                case "qsv": return "-c:v h264_qsv -preset medium -pix_fmt nv12";
                default: return "-c:v libx264 -preset veryfast -pix_fmt yuv420p";
            }
        }

        public static string Key() { return GpuSummary() + "|" + FFmpeg.Version + "|" + Settings.Cur.Monitor + "|" + Settings.Cur.Encoder + "|" + Settings.Cur.Resolution; }

        public static string Choose(Action<string> status)
        {
            string key = Key();
            if (Settings.Cur.ProbeKey == key && Settings.Cur.ProbeResult != "") return Settings.Cur.ProbeResult;
            foreach (string enc in Candidates())
            {
                foreach (bool zc in enc == "x264" || (!Recorder.OutputSize().IsEmpty && enc != "qsv") ? new[] { false } : new[] { true, false })
                {
                    if (status != null) status("Testing " + Label(enc) + "\u2026");
                    string args = "-hide_banner -loglevel error -f lavfi -i ddagrab=output_idx=" + Settings.Cur.Monitor + ":framerate=30 -frames:v 10 "
                                + Args(enc, zc, 30, 4000, Recorder.OutputSize()) + " -f null -";
                    FFmpeg.Result r = FFmpeg.Run(FFmpeg.Exe, args, 20000, null);
                    if (r.Exit == 0)
                    {
                        string res = enc + ":" + (zc ? "1" : "0");
                        Settings.Cur.ProbeKey = key; Settings.Cur.ProbeResult = res; Settings.Save();
                        Log.Info("Encoder", "Picked " + res + " (" + GpuSummary() + ")");
                        return res;
                    }
                    Log.Info("Encoder", enc + (zc ? " (GPU frames)" : " (copied frames)") + " didn't work: " + Short(r.Err));
                }
            }
            return "";
        }

        public static string Short(string err)
        {
            string s = (err ?? "").Trim().Replace("\r", "");
            string[] lines = s.Split('\n');
            return lines.Length > 4 ? string.Join(" | ", lines, lines.Length - 4, 4) : s.Replace("\n", " | ");
        }
    }
}
