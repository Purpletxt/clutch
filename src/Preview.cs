using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;

namespace Clutch
{
    static class HoverPreview
    {
        public const int Frames = 8;
        static readonly Dictionary<string, Bitmap> strips = new Dictionary<string, Bitmap>();
        static readonly HashSet<string> busy = new HashSet<string>(), failed = new HashSet<string>();
        static readonly List<string> order = new List<string>();

        public static string StripPath(Clip c) { string t = Library.ThumbPath(c); return t.Substring(0, t.Length - 4) + "_strip.jpg"; }

        public static Bitmap Get(Clip c, Action ready)
        {
            if (!FFmpeg.Ready) return null;
            string key = StripPath(c);
            lock (strips)
            {
                Bitmap b; if (strips.TryGetValue(key, out b)) return b;
                if (busy.Contains(key) || failed.Contains(key)) return null;
                busy.Add(key);
            }
            string clip = c.Path; double dur = c.Duration;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Bitmap made = null;
                try
                {
                    if (!File.Exists(key)) Make(clip, key, dur);
                    if (File.Exists(key)) made = Library.LoadImage(key);
                    if (made != null && made.Width < made.Height * 2) { made.Dispose(); made = null; }
                }
                catch (Exception ex) { Log.Error("Hover preview", ex); }
                lock (strips)
                {
                    busy.Remove(key);
                    if (made == null) { failed.Add(key); return; }
                    strips[key] = made; order.Add(key);
                    while (order.Count > 24) { string old = order[0]; order.RemoveAt(0); Bitmap ob; if (strips.TryGetValue(old, out ob)) { strips.Remove(old); ob.Dispose(); } }
                }
                if (ready != null) try { ready(); } catch { }
            });
            return null;
        }

        static void Make(string clip, string jpg, double dur)
        {
            if (dur <= 0.5) dur = 30;
            string rate = FFmpeg.N(Frames / dur);
            string vf = "fps=" + rate + ",scale=320:-2,tile=" + Frames + "x1";
            FFmpeg.Run(FFmpeg.Exe, "-y -hide_banner -loglevel error -skip_frame nokey -i " + FFmpeg.Q(clip) + " -an -vf " + vf + " -frames:v 1 -q:v 5 " + FFmpeg.Q(jpg), 20000, null);
            if (!File.Exists(jpg))
                FFmpeg.Run(FFmpeg.Exe, "-y -hide_banner -loglevel error -i " + FFmpeg.Q(clip) + " -an -vf " + vf + " -frames:v 1 -q:v 5 " + FFmpeg.Q(jpg), 30000, null);
        }

        public static void Forget(Clip c)
        {
            string key = StripPath(c);
            lock (strips) { Bitmap b; if (strips.TryGetValue(key, out b)) { strips.Remove(key); order.Remove(key); b.Dispose(); } failed.Remove(key); }
            try { if (File.Exists(key)) File.Delete(key); } catch { }
        }

        public static Rectangle FrameRect(Bitmap strip, int i)
        {
            int w = strip.Width / Frames; return new Rectangle(w * (i % Frames), 0, w, strip.Height);
        }
    }
}
