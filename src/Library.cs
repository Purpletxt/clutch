using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Clutch
{
    class Clip
    {
        public string Path = "";
        public string Game = "";
        public DateTime Created;
        public double Duration;
        public long Size;
        public bool Favorite;
        public Bitmap Thumb;
        public bool ThumbTried, ThumbReady;
        public string Title { get { return System.IO.Path.GetFileNameWithoutExtension(Path); } }
    }

    class ClipMeta { public string Game = ""; public bool Favorite; public double Duration; }

    static class Library
    {
        public static readonly List<Clip> Clips = new List<Clip>();
        public static event Action Changed;
        static Dictionary<string, ClipMeta> meta = new Dictionary<string, ClipMeta>(StringComparer.OrdinalIgnoreCase);
        static System.Windows.Forms.Control ui;
        static string FilePath { get { return System.IO.Path.Combine(Paths.Roaming, "library.json"); } }

        public static void Attach(System.Windows.Forms.Control uiThread) { ui = uiThread; }

        static void Raise() { if (Changed != null) Changed(); }

        public static void Load()
        {
            meta = new Dictionary<string, ClipMeta>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                {
                    Dictionary<string, ClipMeta> m = new JavaScriptSerializer().Deserialize<Dictionary<string, ClipMeta>>(File.ReadAllText(FilePath));
                    if (m != null) foreach (KeyValuePair<string, ClipMeta> kv in m) meta[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex) { Log.Error("Library.Load", ex); }
            Rescan();
        }

        public static void Rescan()
        {
            Dictionary<string, Clip> old = new Dictionary<string, Clip>(StringComparer.OrdinalIgnoreCase);
            foreach (Clip c in Clips) old[c.Path] = c;
            Clips.Clear();
            try
            {
                string dir = Settings.Cur.Folder;
                if (Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir, "*.mp4"))
                    {
                        if (pendingPaths.Contains(f)) continue;
                        Clip c; if (!old.TryGetValue(f, out c)) c = FromFile(f);
                        Clips.Add(c);
                    }
            }
            catch (Exception ex) { Log.Error("Library.Rescan", ex); }
            Sort(); Raise(); QueueWork();
        }

        static Clip FromFile(string f)
        {
            Clip c = new Clip(); c.Path = f;
            FileInfo fi = new FileInfo(f); c.Created = fi.CreationTime; c.Size = fi.Length;
            c.ThumbReady = File.Exists(ThumbPath(c));
            ClipMeta m; if (meta.TryGetValue(System.IO.Path.GetFileName(f), out m)) { c.Game = Games.Clean(m.Game); c.Favorite = m.Favorite; c.Duration = m.Duration; }
            return c;
        }

        static void Sort() { Clips.Sort(delegate (Clip a, Clip b) { return b.Created.CompareTo(a.Created); }); }

        public static void Save()
        {
            if (Program.DevRun) return;
            Dictionary<string, ClipMeta> m = new Dictionary<string, ClipMeta>();
            foreach (Clip c in Clips) { ClipMeta x = new ClipMeta(); x.Game = c.Game; x.Favorite = c.Favorite; x.Duration = c.Duration; m[System.IO.Path.GetFileName(c.Path)] = x; }
            foreach (Pending p in pending) { ClipMeta x = new ClipMeta(); x.Game = p.Clip.Game; x.Favorite = p.Clip.Favorite; x.Duration = p.Clip.Duration; m[System.IO.Path.GetFileName(p.Clip.Path)] = x; }
            try { File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(m), Encoding.UTF8); }
            catch (Exception ex) { Log.Error("Library.Save", ex); }
        }

        public static Clip Add(string path, string game, double seconds)
        {
            Clip c = FromFile(path); c.Game = Games.Clean(game); c.Duration = seconds; c.Created = DateTime.Now;
            Clips.Insert(0, c); Save(); Raise(); QueueWork();
            return c;
        }

        public static void SetFavorite(Clip c, bool fav) { c.Favorite = fav; Save(); Raise(); }

        public static string SafeName(string s)
        {
            StringBuilder b = new StringBuilder();
            foreach (char ch in s.Trim()) b.Append(Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), ch) >= 0 ? '-' : ch);
            string r = b.ToString().Trim().TrimEnd('.');
            if (r.Length > 120) r = r.Substring(0, 120);
            return r.Length == 0 ? "Clip" : r;
        }

        public static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = System.IO.Path.GetDirectoryName(path), name = System.IO.Path.GetFileNameWithoutExtension(path), ext = System.IO.Path.GetExtension(path);
            for (int i = 2; ; i++) { string p = System.IO.Path.Combine(dir, name + " (" + i + ")" + ext); if (!File.Exists(p)) return p; }
        }

        public static string Rename(Clip c, string title)
        {
            string newPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(c.Path), SafeName(title) + ".mp4");
            if (string.Equals(newPath, c.Path, StringComparison.Ordinal)) return null;
            if (!string.Equals(newPath, c.Path, StringComparison.OrdinalIgnoreCase) && File.Exists(newPath)) return "There's already a clip with that name.";
            try
            {
                File.Move(c.Path, newPath);
                string oldThumb = ThumbPath(c);
                c.Path = newPath;
                try { if (File.Exists(oldThumb)) File.Move(oldThumb, ThumbPath(c)); } catch { }
            }
            catch (Exception ex) { return "Couldn't rename it: " + ex.Message; }
            Save(); Raise(); return null;
        }

        class Pending { public Clip Clip; public DateTime Due; public int Index; }
        static readonly List<Pending> pending = new List<Pending>();
        static readonly HashSet<string> pendingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static System.Windows.Forms.Timer deleteTimer;

        public static void Delete(Clip c)
        {
            Pending p = new Pending(); p.Clip = c; p.Due = DateTime.Now.AddSeconds(8); p.Index = Clips.IndexOf(c);
            Clips.Remove(c); pending.Add(p); pendingPaths.Add(c.Path);
            if (deleteTimer == null) { deleteTimer = new System.Windows.Forms.Timer(); deleteTimer.Interval = 1000; deleteTimer.Tick += delegate { Flush(false); }; }
            deleteTimer.Start();
            Raise();
        }

        public static bool Undo(Clip c)
        {
            Pending p = pending.Find(delegate (Pending x) { return x.Clip == c; });
            if (p == null) return false;
            pending.Remove(p); pendingPaths.Remove(c.Path);
            Clips.Insert(Math.Max(0, Math.Min(Clips.Count, p.Index)), c);
            Raise(); return true;
        }

        public static void Flush(bool all)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pending p = pending[i];
                if (!all && DateTime.Now < p.Due) continue;
                try
                {
                    if (File.Exists(p.Clip.Path))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p.Clip.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    try { string t = ThumbPath(p.Clip); if (File.Exists(t)) File.Delete(t); } catch { }
                    pending.RemoveAt(i); pendingPaths.Remove(p.Clip.Path);
                }
                catch (Exception ex)
                {
                    p.Due = DateTime.Now.AddSeconds(2);
                    if (all) { Log.Error("Delete clip", ex); pending.RemoveAt(i); }
                }
            }
            if (pending.Count == 0 && deleteTimer != null) deleteTimer.Stop();
            Save();
        }

        static readonly Queue<Clip> work = new Queue<Clip>();
        static Thread worker;

        public static string ThumbPath(Clip c)
        {
            return System.IO.Path.Combine(Paths.Thumbs, ((uint)c.Path.ToLowerInvariant().GetHashCode()).ToString("x8") + "_" + c.Size + ".jpg");
        }

        public static Bitmap LoadImage(string file)
        {
            try { using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(file))) using (Bitmap b = new Bitmap(ms)) return new Bitmap(b); }
            catch { return null; }
        }

        static void QueueWork()
        {
            lock (work)
            {
                foreach (Clip c in Clips) if (!c.ThumbTried || c.Duration <= 0) { c.ThumbTried = true; work.Enqueue(c); }
                if (work.Count > 0 && (worker == null || !worker.IsAlive)) { worker = new Thread(Work); worker.IsBackground = true; worker.Priority = ThreadPriority.BelowNormal; worker.Start(); }
            }
        }

        static void Work()
        {
            while (true)
            {
                Clip c;
                lock (work) { if (work.Count == 0) { worker = null; return; } c = work.Dequeue(); }
                try
                {
                    if (Program.Demo) continue;
                    bool changed = false;
                    if (c.Duration <= 0 && FFmpeg.Ready) { c.Duration = FFmpeg.Duration(c.Path); changed = true; }
                    string t = ThumbPath(c);
                    if (!File.Exists(t) && FFmpeg.Ready) FFmpeg.Thumbnail(c.Path, t, Math.Min(2, c.Duration / 2));
                    bool ready = File.Exists(t);
                    if (ready || changed)
                        Post(delegate { c.ThumbReady = ready; if (changed) Save(); Raise(); });
                }
                catch (Exception ex) { Log.Error("Thumbnail", ex); }
            }
        }

        public static void PostToUi(Action a) { Post(a); }

        static void Post(Action a)
        {
            if (ui == null || ui.IsDisposed) return;
            try { ui.BeginInvoke(a); } catch { }
        }

        public static string NiceSize(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.0") + " GB";
            if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0") + " MB";
            return Math.Max(1, bytes / 1024) + " KB";
        }

        public static string NiceTime(double s)
        {
            if (s <= 0) return "--:--";
            int t = (int)Math.Round(s); return (t / 60) + ":" + (t % 60).ToString("00");
        }

        public static string NiceDate(DateTime d)
        {
            if (d.Date == DateTime.Today) return "Today " + d.ToString("HH:mm");
            if (d.Date == DateTime.Today.AddDays(-1)) return "Yesterday " + d.ToString("HH:mm");
            return d.ToString(d.Year == DateTime.Today.Year ? "d MMM, HH:mm" : "d MMM yyyy");
        }
    }

    static class Games
    {
        public static string Running = "";
        public static string RunningExe = "";

        public static string Check()
        {
            string found = "";
            if (Settings.Cur.Games.Count == 0) { Running = RunningExe = ""; return ""; }
            string exe = "";
            HashSet<string> want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameEntry g in Settings.Cur.Games) want.Add(Path.GetFileNameWithoutExtension(g.Exe));
            foreach (Process p in Process.GetProcesses())
            {
                try { if (found == "" && want.Contains(p.ProcessName)) { found = NameFor(p.ProcessName); exe = p.ProcessName + ".exe"; GameIcons.Remember(found, ExePath(p.Id)); } }
                catch { }
                p.Dispose();
            }
            Running = found; RunningExe = exe; return found;
        }

        static string NameFor(string proc)
        {
            foreach (GameEntry g in Settings.Cur.Games)
                if (string.Equals(Path.GetFileNameWithoutExtension(g.Exe), proc, StringComparison.OrdinalIgnoreCase)) return Clean(g.Name.Length > 0 ? g.Name : proc);
            return proc;
        }

        public static string ForClip()
        {
            if (Running != "") return Running;
            try
            {
                int pid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                using (Process p = Process.GetProcessById(pid))
                {
                    if (p.Id == Process.GetCurrentProcess().Id || p.ProcessName == "explorer") return "";
                    string name = Friendly(p);
                    GameIcons.Remember(name, ExePath(p.Id));
                    return name;
                }
            }
            catch { return ""; }
        }

        public static string Clean(string name)
        {
            string n = (name ?? "").Trim();
            string[] tails = { " (64-bit)", " (32-bit)", " 64-bit", " x64", " Game Client", " Client", " Launcher", " Application" };
            for (bool again = true; again; )
            {
                again = false;
                foreach (string t in tails)
                    if (n.Length > t.Length + 1 && n.EndsWith(t, StringComparison.OrdinalIgnoreCase)) { n = n.Substring(0, n.Length - t.Length).Trim(); again = true; }
            }
            return n;
        }

        public static string Friendly(Process p)
        {
            string name = p.ProcessName;
            try
            {
                string d = p.MainModule.FileVersionInfo.FileDescription;
                if (!string.IsNullOrEmpty(d) && d.Length <= 40) name = d.Trim();
                name = Clean(name);
            }
            catch { }
            return name;
        }

        public static string ExePath(int pid)
        {
            IntPtr h = Native.OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return "";
            try { StringBuilder sb = new StringBuilder(1024); int n = sb.Capacity; return Native.QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : ""; }
            finally { Native.CloseHandle(h); }
        }

        public class App { public string Exe = "", Name = "", Title = "", Path = ""; }

        public static List<App> RunningApps()
        {
            List<App> list = new List<App>(); HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int me = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id != me && p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0 && seen.Add(p.ProcessName)
                        && p.ProcessName != "explorer" && p.ProcessName != "ApplicationFrameHost" && p.ProcessName != "TextInputHost" && p.ProcessName != "SystemSettings")
                    {
                        App a = new App(); a.Exe = p.ProcessName + ".exe"; a.Name = Friendly(p); a.Title = p.MainWindowTitle; a.Path = ExePath(p.Id); list.Add(a);
                    }
                }
                catch { }
                p.Dispose();
            }
            list.Sort(delegate (App a, App b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return list;
        }
    }

    static class GameIcons
    {
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        public static event Action Changed;

        static string Dir { get { return System.IO.Path.Combine(Paths.Local, "icons"); } }
        static string FileFor(string game)
        {
            StringBuilder b = new StringBuilder();
            foreach (char ch in game.ToLowerInvariant()) b.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            return System.IO.Path.Combine(Dir, b + "@64.png");
        }

        static Dictionary<string, string> exes;
        static string ExesFile { get { return System.IO.Path.Combine(Dir, "games.txt"); } }
        static void LoadExes()
        {
            if (exes != null) return;
            exes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(ExesFile)) foreach (string l in File.ReadAllLines(ExesFile)) { int t = l.IndexOf('\t'); if (t > 0) exes[l.Substring(0, t)] = l.Substring(t + 1); } } catch { }
        }
        static void NoteExe(string game, string exe)
        {
            lock (cache)
            {
                LoadExes(); string old;
                if (exes.TryGetValue(game, out old) && string.Equals(old, exe, StringComparison.OrdinalIgnoreCase)) return;
                exes[game] = exe;
                try { Directory.CreateDirectory(Dir); List<string> l = new List<string>(); foreach (KeyValuePair<string, string> kv in exes) l.Add(kv.Key + "\t" + kv.Value); File.WriteAllLines(ExesFile, l.ToArray()); } catch { }
            }
        }
        public static string ExeFor(string game)
        {
            game = Games.Clean(game);
            foreach (GameEntry g in Settings.Cur.Games) if (string.Equals(Games.Clean(g.Name), game, StringComparison.OrdinalIgnoreCase)) return g.Exe;
            lock (cache) { LoadExes(); string e; return exes.TryGetValue(game, out e) ? e : null; }
        }
        public static List<string> KnownExes() { lock (cache) { LoadExes(); List<string> l = new List<string>(); foreach (string e in exes.Values) l.Add(e.ToLowerInvariant()); return l; } }

        public static Bitmap Get(string game)
        {
            game = Games.Clean(game);
            if (string.IsNullOrEmpty(game)) return null;
            lock (cache)
            {
                Bitmap b;
                if (cache.TryGetValue(game, out b)) return b;
                string f = FileFor(game);
                b = File.Exists(f) ? Library.LoadImage(f) : null;
                cache[game] = b;
                return b;
            }
        }

        public static void Remember(string game, string exePath)
        {
            game = Games.Clean(game);
            if (!string.IsNullOrEmpty(game) && !string.IsNullOrEmpty(exePath)) NoteExe(game, System.IO.Path.GetFileName(exePath));
            if (string.IsNullOrEmpty(game) || string.IsNullOrEmpty(exePath) || Get(game) != null) return;
            try
            {
                Bitmap raw = null; IntPtr big, small;
                if (Native.SHDefExtractIcon(exePath, 0, 0, out big, out small, 64 | (16 << 16)) == 0 && big != IntPtr.Zero)
                {
                    using (Icon ic = Icon.FromHandle(big)) raw = ic.ToBitmap();
                    Native.DestroyIcon(big); if (small != IntPtr.Zero) Native.DestroyIcon(small);
                }
                if (raw == null) using (Icon ic = Icon.ExtractAssociatedIcon(exePath)) if (ic != null) raw = ic.ToBitmap();
                if (raw == null) return;
                Bitmap bmp = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(bmp)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(raw, 0, 0, 64, 64); }
                raw.Dispose();
                Directory.CreateDirectory(Dir);
                bmp.Save(FileFor(game), System.Drawing.Imaging.ImageFormat.Png);
                lock (cache) cache[game] = bmp;
                Action a = Changed;
                if (a != null) Library.PostToUi(a);
            }
            catch (Exception ex) { Log.Info("Game icon", game + ": " + ex.Message); }
        }
    }
}
