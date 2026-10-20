using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Clutch
{
    static class Web
    {
        static readonly Dictionary<string, string> tags = new Dictionary<string, string>(), bodies = new Dictionary<string, string>();

        public static string Get(string url, string accept, out bool changed)
        {
            changed = true;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return File.ReadAllText(url);
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "Clutch/" + AppInfo.Version; r.Timeout = 15000; r.ReadWriteTimeout = 15000;
            r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (accept != null) r.Accept = accept;
            string tag; lock (tags) if (tags.TryGetValue(url, out tag)) r.Headers[HttpRequestHeader.IfNoneMatch] = tag;
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                using (StreamReader rd = new StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8))
                {
                    string body = rd.ReadToEnd();
                    string et = resp.Headers[HttpResponseHeader.ETag];
                    lock (tags) { if (!string.IsNullOrEmpty(et)) { tags[url] = et; bodies[url] = body; } }
                    return body;
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotModified)
                {
                    changed = false;
                    lock (tags) { string b; return bodies.TryGetValue(url, out b) ? b : ""; }
                }
                throw;
            }
        }
    }

    static class Updater
    {
        public const string Repo = "Purpletxt/clutch";
        public const string AssetName = "ClutchSetup.exe";

        public class Release { public string Version = "", Notes = "", Url = "", Sha256 = ""; public long Size; }
        public static Release Available;
        public static volatile bool Checking;
        public static string LastError = "";
        public static DateTime LastCheck = DateTime.MinValue;
        public static event Action Changed;

        static string Feed
        {
            get
            {
#if DEV
                string over = Environment.GetEnvironmentVariable("CLUTCH_UPDATE_FEED");
                if (!string.IsNullOrEmpty(over)) return over;
#endif
                return Repo.Length > 0 ? "https://api.github.com/repos/" + Repo + "/releases/latest" : "";
            }
        }
        public static bool Configured { get { return Feed.Length > 0; } }

        public static bool Newer(string v)
        {
            Version a, b;
            return Version.TryParse(v, out a) && Version.TryParse(AppInfo.Version, out b) && a > b;
        }

        public static void CheckAsync(Control ui, Action done)
        {
            if (!Configured || Checking) return;
            Checking = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Release r = null; string err = "";
                try { r = Fetch(); }
                catch (Exception ex) { err = ex is WebException ? "Couldn't reach GitHub. Check your internet connection." : ex.Message; Log.Info("Update check", ex.Message); }
                Action finish = delegate
                {
                    Checking = false; LastCheck = DateTime.Now; LastError = err;
                    if (err.Length == 0) Available = r != null && Newer(r.Version) && r.Version != Settings.Cur.UpdatedTo ? r : null;
                    if (Changed != null) Changed();
                    if (done != null) done();
                };
                try { ui.BeginInvoke(finish); } catch { Checking = false; }
            });
        }

        static Release last;

        public static Release Fetch()
        {
            bool changed;
            string json = Web.Get(Feed, "application/vnd.github+json", out changed);
            if (!changed) return last;
            last = Parse(json);
            return last;
        }

        static Release Parse(string json)
        {
            Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) return null;
            Release r = new Release();
            r.Version = Str(d, "tag_name").TrimStart('v', 'V');
            r.Notes = Str(d, "body").Replace("\r", "").Trim();
            IEnumerable assets = d.ContainsKey("assets") ? d["assets"] as IEnumerable : null;
            if (assets != null)
                foreach (object o in assets)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    if (a == null || !string.Equals(Str(a, "name"), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    r.Url = Str(a, "browser_download_url");
                    string dg = Str(a, "digest");
                    if (dg.StartsWith("sha256:")) r.Sha256 = dg.Substring(7).ToLowerInvariant();
                    long n; long.TryParse(Str(a, "size"), out n); r.Size = n;
                }
            return r.Url.Length > 0 ? r : null;
        }

        static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }

        public static string Download(Release r, Action<long, long> progress, Func<bool> cancelled)
        {
            if (r.Sha256.Length != 64) throw new Exception("This update has no checksum, so Clutch won't install it.");
            string file = Path.Combine(Paths.Local, "update.exe");
            WebRequest req = WebRequest.Create(r.Url);
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            HttpWebRequest http = req as HttpWebRequest; if (http != null) http.UserAgent = "Clutch/" + AppInfo.Version;
            using (WebResponse resp = req.GetResponse())
            using (Stream src = resp.GetResponseStream())
            using (FileStream dst = File.Create(file))
            {
                long total = resp.ContentLength > 0 ? resp.ContentLength : r.Size, done = 0; byte[] buf = new byte[1 << 16]; int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancelled()) { dst.Close(); File.Delete(file); throw new OperationCanceledException(); }
                    dst.Write(buf, 0, n); done += n; progress(done, total);
                }
            }
            string actual;
            using (FileStream fs = File.OpenRead(file)) using (SHA256 sha = SHA256.Create())
                actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            if (actual != r.Sha256) { File.Delete(file); throw new Exception("The download didn't match its checksum, so it was thrown away. Please try again."); }
            return file;
        }

        public static void Apply(string newExe) { Apply(newExe, "--updated"); }
        public static void Apply(string newExe, string args)
        {
            string target = Application.ExecutablePath, old = Path.Combine(Path.GetDirectoryName(target), "Clutch.old.exe");
            if (File.Exists(old)) File.Delete(old);
            File.Move(target, old);
            try { File.Move(newExe, target); }
            catch { File.Move(old, target); throw; }
            if (Setup.RunningInstalled) try { Setup.Register(); } catch { }
            SingleInstance.Release();
            Process.Start(target, args);
        }
    }

    class UpdateWindow : Window
    {
        readonly Updater.Release rel;
        readonly Progress bar = new Progress();
        readonly Card notes = new Card();
        readonly Lbl notesHead = new Lbl("What's new", 10, 5, C.Text), notesText = new Lbl("", 9.5f, 0, C.Sub);
        readonly Btn retry = new Btn("Try again", "", BtnStyle.Accent), cancel = new Btn("Cancel", "", BtnStyle.Subtle);
        string status = "Starting\u2026"; bool failed, done; volatile bool cancelled;

        public UpdateWindow(Updater.Release r) : base("Updating Clutch", 520, 0)
        {
            rel = r;
            List<string> lines = new List<string>();
            foreach (string l in r.Notes.Split('\n'))
            {
                string t = l.Trim().TrimStart('#').Trim();
                if (t.StartsWith("- ") || t.StartsWith("* ")) t = "\u2022  " + t.Substring(2);
                if (t.Length > 0 && lines.Count < 8) lines.Add(t.Replace("**", ""));
            }
            notesText.Text = lines.Count > 0 ? string.Join("\n", lines.ToArray()) : "Fixes and improvements.";
            notesText.Wraps = true; notesHead.BackColor = notesText.BackColor = C.Card;
            notes.Controls.Add(notesHead); notes.Controls.Add(notesText);
            retry.Visible = false;
            retry.Click += delegate { Start(); };
            cancel.Click += delegate { if (failed) { Relaunch(); Close(); } else cancelled = true; };
            foreach (Control c in new Control[] { bar, notes, retry, cancel }) Controls.Add(c);
            Shown += delegate
            {
                Front();
#if DEV
                if (Preview == 1) { status = "Downloading\u2026  1.6 MB of 4.1 MB"; bar.Value = 0.4f; Invalidate(); return; }
                if (Preview == 2) { failed = true; status = "Couldn't download the update. Check your internet connection."; Sync(); return; }
#endif
                Start();
            };
        }
#if DEV
        public int Preview;
#endif

        void Start()
        {
            failed = false; cancelled = false; status = "Downloading\u2026"; bar.Value = 0; Sync();
            Thread t = new Thread(delegate ()
            {
                string err = null;
                try
                {
                    string file = Updater.Download(rel, delegate (long d, long total)
                    {
                        Post(delegate { bar.Value = total > 0 ? (float)d / total : 0; status = "Downloading\u2026  " + Library.NiceSize(d) + (total > 0 ? " of " + Library.NiceSize(total) : ""); Invalidate(StatusRect); });
                    }, delegate { return cancelled; });
                    Post(delegate { status = "Installing\u2026"; bar.SetBusy(true); Invalidate(StatusRect); });
                    Settings.Cur.UpdatedTo = rel.Version; Settings.Save();
                    Updater.Apply(file);
                }
                catch (OperationCanceledException) { err = ""; }
                catch (Exception ex) { err = ex is WebException ? "Couldn't download the update. Check your internet connection." : ex.Message; Log.Error("Update", ex); }
                Post(delegate
                {
                    if (err == null) { done = true; Close(); return; }
                    if (err == "") { Relaunch(); Close(); return; }
                    failed = true; status = err; bar.SetBusy(false); Sync();
                });
            });
            t.IsBackground = true; t.Start();
        }

        static void Relaunch() { SingleInstance.Release(); try { Process.Start(Application.ExecutablePath); } catch { } }

        void Post(Action a) { try { if (IsHandleCreated) BeginInvoke(a); } catch { } }

        void Sync()
        {
            retry.Visible = failed;
            cancel.Text = failed ? "Open Clutch " + AppInfo.Version : "Cancel";
            bar.Visible = !failed;
            Relayout(); Invalidate();
        }

        Rectangle StatusRect { get { return new Rectangle(S(28), S(92), ClientSize.Width - S(56), S(24)); } }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!done && !failed && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; cancelled = true; return; }
            base.OnFormClosing(e);
        }

        protected override void Relayout()
        {
            int x = S(28), w = S(520) - S(56), y = S(124);
            bar.SetBounds(x, y, w, S(6)); y += S(28);
            int nh = notesText.Fit(w - S(36));
            notesHead.SetBounds(S(18), S(14), w - S(36), S(22)); notesText.SetBounds(S(18), S(40), w - S(36), nh);
            notes.SetBounds(x, y, w, nh + S(54)); y = notes.Bottom + S(22);
            int cw = cancel.PreferredWidth() + S(12);
            cancel.SetBounds(x + w - cw, y, cw, S(38));
            retry.SetBounds(cancel.Left - S(10) - S(120), y, S(120), S(38));
            ClientSize = new Size(S(520), y + S(38) + S(26));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawHeader(e.Graphics, "Updating Clutch", AppInfo.Version + "  \u2192  " + rel.Version);
            Ui.TextLeft(e.Graphics, status, Ui.F(9.5f, failed ? 1 : 0), StatusRect, failed ? C.Warn : C.Sub);
        }
    }
}
