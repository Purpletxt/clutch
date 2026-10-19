using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Clutch
{
    static class GameArt
    {
        class Info { public string Icon = "", AppId = "", Cover = "", Steam = "", Xbox = ""; public bool Complete; }
        static Dictionary<string, Info> map;
        static bool fetching; static DateTime lastFetch = DateTime.MinValue;
        static readonly object sync = new object();
        static string FilePath { get { return Path.Combine(Paths.Local, "discord-art.txt"); } }
        static string BannerDir { get { return Path.Combine(Paths.Local, "banners"); } }

        public static string Url(string exe)
        {
            Info i = Get(exe, true);
            return i != null && i.Icon.Length > 0 ? i.Icon : null;
        }

        static Info Get(string exe, bool lookup)
        {
            exe = (exe ?? "").ToLowerInvariant();
            if (exe.Length == 0) return null;
            Info i;
            lock (sync) { if (map == null) Load(); if (map.TryGetValue(exe, out i) && i.Complete) return i; }
            if (!lookup) return i;
            Lookup(exe);
            lock (sync) { map.TryGetValue(exe, out i); return i; }
        }

        static void Load()
        {
            map = new Dictionary<string, Info>();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string l in File.ReadAllLines(FilePath))
                {
                    string[] f = l.Split('\t'); if (f.Length < 2) continue;
                    Info i = new Info(); i.Icon = f[1];
                    if (f.Length >= 6) { i.AppId = f[2]; i.Cover = f[3]; i.Steam = f[4]; i.Xbox = f[5]; i.Complete = true; }
                    map[f[0]] = i;
                }
            }
            catch { }
        }

        static void SaveMap()
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, Info> kv in map) lines.Add(kv.Key + "\t" + kv.Value.Icon + "\t" + kv.Value.AppId + "\t" + kv.Value.Cover + "\t" + kv.Value.Steam + "\t" + kv.Value.Xbox);
            try { File.WriteAllLines(FilePath, lines.ToArray()); } catch { }
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            WebClient wc = new WebClient(); wc.Headers[HttpRequestHeader.UserAgent] = "Clutch/" + AppInfo.Version; wc.Encoding = Encoding.UTF8;
            return wc;
        }

        static void Lookup(string exe)
        {
            lock (sync) { if (fetching || (DateTime.Now - lastFetch).TotalMinutes < 30) return; fetching = true; lastFetch = DateTime.Now; }
            try
            {
                string list; using (WebClient wc = Client()) list = wc.DownloadString("https://discord.com/api/v9/applications/detectable");
                List<string> wanted = new List<string> { exe };
                foreach (GameEntry g in Settings.Cur.Games) wanted.Add(g.Exe.ToLowerInvariant());
                foreach (string e in GameIcons.KnownExes()) wanted.Add(e);
                lock (sync)
                {
                    foreach (string e in wanted) { Info old; if (!map.TryGetValue(e, out old) || !old.Complete) map[e] = Find(list, e); }
                    SaveMap();
                }
            }
            catch (Exception ex) { Log.Info("Game art", ex.Message); }
            finally { lock (sync) fetching = false; }
        }

        static Info Find(string list, string exe)
        {
            Info info = new Info(); info.Complete = true;
            foreach (string pattern in new[] { "\"name\": \"" + exe + "\"", "/" + exe + "\"" })
            {
                int at = list.IndexOf(pattern, StringComparison.Ordinal);
                if (at < 0) continue;
                int start = list.LastIndexOf("{\"aliases\":", at, StringComparison.Ordinal);
                int end = list.IndexOf("{\"aliases\":", at, StringComparison.Ordinal); if (end < 0) end = list.Length;
                if (start < 0) continue;
                string obj = list.Substring(start, end - start);
                int iconAt = obj.IndexOf("\"icon_hash\": \"", StringComparison.Ordinal);
                info.AppId = Value(obj, "\"id\": \"", Math.Max(0, iconAt)) ?? "";
                string hash = Value(obj, "\"icon_hash\": \"", 0);
                if (hash != null && info.AppId.Length > 0) info.Icon = "https://cdn.discordapp.com/app-icons/" + info.AppId + "/" + hash + ".png?size=256";
                info.Cover = Value(obj, "\"cover_image_hash\": \"", 0) ?? "";
                Match steam = Regex.Match(obj, "\"distributor\": \"steam\", \"id\": \"(\\d+)\"");
                if (steam.Success) info.Steam = steam.Groups[1].Value;
                Match xbox = Regex.Match(obj, "\"distributor\": \"xbox\", \"id\": \"(\\w+)\"");
                if (xbox.Success) info.Xbox = xbox.Groups[1].Value;
                return info;
            }
            return info;
        }

        static string Value(string s, string key, int from)
        {
            int i = s.IndexOf(key, from, StringComparison.Ordinal); if (i < 0) return null;
            i += key.Length; int j = s.IndexOf('"', i);
            return j > i ? s.Substring(i, j - i) : null;
        }

        static string Safe(string exe) { StringBuilder b = new StringBuilder(); foreach (char c in exe.ToLowerInvariant()) b.Append(char.IsLetterOrDigit(c) ? c : '_'); return b.ToString(); }

        public static string BannerFile(string exe, out bool tried)
        {
            string f = Path.Combine(BannerDir, Safe(exe) + ".jpg");
            tried = File.Exists(f) || File.Exists(Path.ChangeExtension(f, ".none"));
            return File.Exists(f) ? f : null;
        }

        public static void FetchBanner(string exe)
        {
            Directory.CreateDirectory(BannerDir);
            string f = Path.Combine(BannerDir, Safe(exe) + ".jpg");
            Info i = Get(exe, true);
            Bitmap art = null; bool soften = false;
            try
            {
                if (i != null && i.Steam.Length > 0)
                    art = Download("https://cdn.cloudflare.steamstatic.com/steam/apps/" + i.Steam + "/library_hero.jpg")
                       ?? Download("https://cdn.cloudflare.steamstatic.com/steam/apps/" + i.Steam + "/header.jpg");
                if (art == null && i != null && i.Xbox.Length > 0) art = Download(StoreHero(i.Xbox));
                if (art == null && i != null && i.Cover.Length > 0 && i.AppId.Length > 0)
                { art = Download("https://cdn.discordapp.com/app-icons/" + i.AppId + "/" + i.Cover + ".jpg?size=512"); soften = art != null; }
                if (art == null) { File.WriteAllText(Path.ChangeExtension(f, ".none"), ""); return; }
                using (Bitmap small = Shrink(art, 720, 300, soften)) SaveJpeg(small, f, 82);
            }
            catch (Exception ex) { Log.Info("Game banner", exe + ": " + ex.Message); }
            finally { if (art != null) art.Dispose(); }
        }

        static string StoreHero(string productId)
        {
            try
            {
                string json; using (WebClient wc = Client()) json = wc.DownloadString("https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds=" + productId + "&market=US&languages=en-us");
                foreach (string purpose in new[] { "SuperHeroArt", "Hero", "BoxArt" })
                {
                    Match m = Regex.Match(json, "\"ImagePurpose\":\"" + purpose + "\"[^{}]*?\"Uri\":\"([^\"]+)\"");
                    if (m.Success) return "https:" + m.Groups[1].Value + "?w=720&h=405&q=80&format=jpg";
                }
            }
            catch { }
            return null;
        }

        static Bitmap Download(string url)
        {
            if (url == null) return null;
            try { using (WebClient wc = Client()) using (MemoryStream ms = new MemoryStream(wc.DownloadData(url))) using (Bitmap b = new Bitmap(ms)) return new Bitmap(b); }
            catch { return null; }
        }

        static Bitmap Shrink(Bitmap src, int w, int h, bool soften)
        {
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float s = Math.Max(w / (float)src.Width, h / (float)src.Height);
                if (soften)
                {
                    using (Bitmap tiny = new Bitmap(Math.Max(1, w / 12), Math.Max(1, h / 12)))
                    {
                        using (Graphics t = Graphics.FromImage(tiny)) { t.InterpolationMode = InterpolationMode.HighQualityBilinear; float ts = s / 12; t.DrawImage(src, (tiny.Width - src.Width * ts) / 2, (tiny.Height - src.Height * ts) / 2, src.Width * ts, src.Height * ts); }
                        g.DrawImage(tiny, -w / 24, -h / 24, w + w / 12, h + h / 12);
                    }
                    using (SolidBrush dark = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(dark, 0, 0, w, h);
                    float cs = h * 0.9f;
                    g.DrawImage(src, (w - cs) / 2, (h - cs) / 2, cs, cs);
                }
                else g.DrawImage(src, (w - src.Width * s) / 2, (h - src.Height * s) / 2, src.Width * s, src.Height * s);
            }
            return dst;
        }

        static void SaveJpeg(Bitmap b, string path, long quality)
        {
            ImageCodecInfo jpg = Array.Find(ImageCodecInfo.GetImageEncoders(), delegate (ImageCodecInfo c) { return c.MimeType == "image/jpeg"; });
            using (EncoderParameters p = new EncoderParameters(1))
            {
                p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                string tmp = path + ".tmp"; b.Save(tmp, jpg, p);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
        }
    }
}
