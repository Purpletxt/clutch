using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Clutch
{
    static class GameList
    {
        class Entry { public string Suffix, Name; }
        static Dictionary<string, List<Entry>> map;
        static bool loading;
        static readonly object gate = new object();
        static string CacheFile { get { return Path.Combine(Paths.Local, "games-list.txt"); } }
        static readonly HashSet<string> Generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "game.exe", "launcher.exe", "client.exe", "start.exe", "app.exe", "main.exe", "java.exe", "javaw.exe", "python.exe", "pythonw.exe", "node.exe", "electron.exe", "unity.exe", "play.exe", "run.exe", "setup.exe" };

        public static bool Ready { get { return map != null; } }

        public static void Ensure()
        {
            lock (gate)
            {
                if (loading) return;
                bool stale = !File.Exists(CacheFile) || (DateTime.Now - File.GetLastWriteTime(CacheFile)).TotalDays > 7;
                if (map != null && !stale) return;
                loading = true;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (map == null && File.Exists(CacheFile)) map = Read(File.ReadAllLines(CacheFile));
                    if (!File.Exists(CacheFile) || (DateTime.Now - File.GetLastWriteTime(CacheFile)).TotalDays > 7)
                    {
                        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                        string json;
                        using (WebClient w = new WebClient()) { w.Headers[HttpRequestHeader.UserAgent] = "Clutch/" + AppInfo.Version; w.Encoding = Encoding.UTF8; json = w.DownloadString("https://discord.com/api/v9/applications/detectable"); }
                        List<string> lines = Parse(json);
                        if (lines.Count > 1000) { File.WriteAllLines(CacheFile, lines.ToArray(), Encoding.UTF8); map = Read(lines.ToArray()); }
                    }
                }
                catch (Exception ex) { Log.Info("Game list", ex.Message); }
                finally { lock (gate) loading = false; }
            });
        }

        static List<string> Parse(string json)
        {
            List<string> o = new List<string>();
            Regex exe = new Regex("\"is_launcher\": (true|false), \"name\": \"((?:[^\"\\\\]|\\\\.)*)\", \"os\": \"win32\"");
            int at = 0;
            while ((at = json.IndexOf("\"executables\": [", at, StringComparison.Ordinal)) >= 0)
            {
                int end = json.IndexOf(']', at); if (end < 0) break;
                string block = json.Substring(at, end - at);
                int id = json.IndexOf("\"id\": \"", end, StringComparison.Ordinal);
                int nm = id < 0 ? -1 : json.IndexOf("\"name\": \"", id, StringComparison.Ordinal);
                at = end;
                if (nm < 0) continue;
                int ns = nm + 9, ne = ns;
                while (ne < json.Length && (json[ne] != '"' || json[ne - 1] == '\\')) ne++;
                string name;
                try { name = Regex.Unescape(json.Substring(ns, ne - ns)); } catch { continue; }
                if (name.Length == 0 || name.IndexOf('\t') >= 0) continue;
                foreach (Match m in exe.Matches(block))
                {
                    if (m.Groups[1].Value == "true") continue;
                    string s = m.Groups[2].Value.Replace("\\/", "/").TrimStart('>').ToLowerInvariant();
                    if (s.Length == 0 || s.IndexOf('\t') >= 0) continue;
                    o.Add(s + "\t" + name);
                }
            }
            return o;
        }

        static Dictionary<string, List<Entry>> Read(string[] lines)
        {
            Dictionary<string, List<Entry>> d = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
            foreach (string l in lines)
            {
                int t = l.IndexOf('\t'); if (t <= 0) continue;
                Entry e = new Entry(); e.Suffix = l.Substring(0, t); e.Name = l.Substring(t + 1);
                string file = e.Suffix.Substring(e.Suffix.LastIndexOf('/') + 1);
                List<Entry> list; if (!d.TryGetValue(file, out list)) d[file] = list = new List<Entry>();
                list.Add(e);
            }
            return d;
        }

        public static string Match(Process p)
        {
            int id = p.Id;
            return Match(p.ProcessName + ".exe", delegate { return Games.ExePath(id); });
        }

        static string Match(string file, Func<string> fullPath)
        {
            Dictionary<string, List<Entry>> d = map;
            if (d == null) return null;
            List<Entry> list; if (!d.TryGetValue(file, out list)) return null;
            string path = null;
            foreach (Entry e in list)
            {
                if (e.Suffix.IndexOf('/') < 0) { if (!Generic.Contains(file)) return e.Name; continue; }
                if (path == null) path = (fullPath() ?? "").Replace('\\', '/').ToLowerInvariant();
                if (path.EndsWith("/" + e.Suffix)) return e.Name;
            }
            return null;
        }

#if DEV
        public static string Test(string jsonFile)
        {
            List<string> lines = Parse(File.ReadAllText(jsonFile));
            map = Read(lines.ToArray());
            StringBuilder r = new StringBuilder(lines.Count + " executables, " + map.Count + " file names\r\n");
            foreach (string path in new[] { @"C:\Games\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe", @"C:\Users\x\AppData\Local\Roblox\Versions\v1\RobloxPlayerBeta.exe",
                @"C:\Riot Games\VALORANT\live\ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Some\Random\game.exe", @"C:\Program Files (x86)\Minecraft\runtime\jre-x64\1.8.0_25\bin\javaw.exe", @"C:\Windows\System32\notepad.exe", @"D:\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe" })
            {
                string f = Path.GetFileName(path).ToLowerInvariant(), p = path;
                r.Append(Path.GetFileName(path) + " -> " + (Match(f, delegate { return p; }) ?? "(not a game)") + "\r\n");
            }
            return r.ToString();
        }
#endif
    }
}
