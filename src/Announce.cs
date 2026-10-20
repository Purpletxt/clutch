using System;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Clutch
{
    static class Announce
    {
        public static string Id = "", Title = "", Message = "";

        static string Url
        {
            get
            {
#if DEV
                string over = Environment.GetEnvironmentVariable("CLUTCH_ANNOUNCE_FEED");
                if (!string.IsNullOrEmpty(over)) return over;
#endif
                return Updater.Repo.Length > 0 ? "https://raw.githubusercontent.com/" + Updater.Repo + "/main/announcement.txt" : "";
            }
        }

        public static void CheckAsync(Control ui, Action changed)
        {
            string url = Url;
            if (url.Length == 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string text;
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                    using (WebClient w = new WebClient()) { w.Headers[HttpRequestHeader.UserAgent] = "Clutch/" + AppInfo.Version; w.Encoding = Encoding.UTF8; text = w.DownloadString(url); }
                }
                catch (WebException ex)
                {
                    HttpWebResponse r = ex.Response as HttpWebResponse;
                    if (r == null || r.StatusCode != HttpStatusCode.NotFound) { Log.Info("Announcement", ex.Message); return; }
                    text = "";
                }
                catch (Exception ex) { Log.Info("Announcement", ex.Message); return; }
                string id = "", title = "", message = "";
                if (text.Length > 0 && !Parse(text, out id, out title, out message)) return;
                try { ui.BeginInvoke((MethodInvoker)delegate { bool diff = id != Id; Id = id; Title = title; Message = message; if (diff && changed != null) changed(); }); } catch { }
            });
        }

        public static bool Parse(string text, out string id, out string title, out string message)
        {
            id = title = message = "";
            text = text.Replace("\r", "");
            int at = text.LastIndexOf("sig ", StringComparison.Ordinal);
            if (!text.StartsWith("clutch-announce-1\n") || at < 0) return false;
            string body = text.Substring(0, at);
            byte[] sig; try { sig = Convert.FromBase64String(text.Substring(at + 4).Trim()); } catch { return false; }
            if (!Licence.Verify(Encoding.UTF8.GetBytes(body), sig)) return false;
            string expires = "";
            foreach (string l in body.Split('\n'))
            {
                int sp = l.IndexOf(' '); if (sp < 0) continue;
                string k = l.Substring(0, sp), v = l.Substring(sp + 1).Trim();
                try
                {
                    if (k == "id") id = v;
                    else if (k == "expires") expires = v;
                    else if (k == "title") title = Encoding.UTF8.GetString(Convert.FromBase64String(v));
                    else if (k == "message") message = Encoding.UTF8.GetString(Convert.FromBase64String(v));
                }
                catch { return false; }
            }
            DateTime end;
            if (expires.Length > 0 && expires != "never" && DateTime.TryParseExact(expires, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out end) && DateTime.Today > end) id = "";
            if (title.Length == 0 && message.Length == 0) id = "";
            if (id.Length == 0) title = message = "";
            return true;
        }
    }

}
