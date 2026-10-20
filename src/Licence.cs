using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Clutch
{
    static class Licence
    {
        const string PublicKey = "RUNTMSAAAAAO8mriuHhJLBVjOockR1efGCK28MuylKTlJxJUPtRsZnVe8DrqppILQ0H+UyfAlNMheB6g8MstP/4vKng9ahLw";
        const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        static readonly DateTime Epoch = new DateTime(2025, 1, 1);
        public enum State { Ok, Missing, Invalid, Expired, Revoked }

        static string KeyFile { get { return Path.Combine(Paths.Roaming, "invite.key"); } }
        static string RevokedFile { get { return Path.Combine(Paths.Roaming, "invite.off"); } }

        static byte[] pcHash;
        static byte[] PcHash
        {
            get
            {
                if (pcHash != null) return pcHash;
                string id = "";
                try { using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Cryptography")) id = (k.GetValue("MachineGuid") ?? "").ToString(); } catch { }
                if (id.Length == 0) id = Environment.MachineName;
                byte[] full; using (SHA256 sha = SHA256.Create()) full = sha.ComputeHash(Encoding.UTF8.GetBytes("Clutch|" + id.ToLowerInvariant()));
                pcHash = new byte[10]; Array.Copy(full, pcHash, 10);
                return pcHash;
            }
        }
        public static string PcCode { get { return Encode(PcHash, 4); } }

        public static string Saved { get { try { return File.Exists(KeyFile) ? File.ReadAllText(KeyFile).Trim() : ""; } catch { return ""; } } }
        public static DateTime Expires;
        public static int Serial;

        public static State Check() { return Check(Saved); }

        public static State Check(string key)
        {
            Expires = DateTime.MaxValue;
            if (string.IsNullOrEmpty(key)) return State.Missing;
            byte[] raw = Decode(key, 70);
            if (raw == null) return State.Invalid;
            byte[] msg = new byte[16]; Array.Copy(raw, msg, 6); PcHash.CopyTo(msg, 6);
            byte[] sig = new byte[64]; Array.Copy(raw, 6, sig, 0, 64);
            if (!Verify(msg, sig)) return State.Invalid;
            Serial = BitConverter.ToInt32(raw, 0);
            ushort days = BitConverter.ToUInt16(raw, 4);
            if (days > 0) { Expires = Epoch.AddDays(days); if (DateTime.Today > Expires) return State.Expired; }
            if (IsRevoked(Serial)) return State.Revoked;
            return State.Ok;
        }

#if DEV
        public static string TestKey;
#else
        const string TestKey = null;
#endif

        public static bool Verify(byte[] data, byte[] sig)
        {
            try
            {
                using (CngKey k = CngKey.Import(Convert.FromBase64String(TestKey ?? PublicKey), CngKeyBlobFormat.EccPublicBlob))
                using (ECDsaCng e = new ECDsaCng(k)) { e.HashAlgorithm = CngAlgorithm.Sha256; return e.VerifyData(data, sig); }
            }
            catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; return false; }
        }
        public static string LastError = "";
#if DEV
        public static string Explain(string key)
        {
            byte[] raw = Decode(key, 70);
            if (raw == null) return "decode failed, length " + key.Length;
            byte[] msg = new byte[16]; Array.Copy(raw, msg, 6); PcHash.CopyTo(msg, 6);
            byte[] sig = new byte[64]; Array.Copy(raw, 6, sig, 0, 64);
            return "pc bytes " + BitConverter.ToString(PcHash) + "\r\nhead " + BitConverter.ToString(raw, 0, 6) + "\r\nverify " + Verify(msg, sig) + " " + LastError;
        }
#endif

        public static bool Save(string key)
        {
            if (Check(key) != State.Ok) return false;
            try { File.WriteAllText(KeyFile, Encode(Decode(key, 70), 8)); return true; } catch { return false; }
        }

        public static void Forget() { try { File.Delete(KeyFile); } catch { } }

        static bool IsRevoked(int serial)
        {
            try { return File.Exists(RevokedFile) && Array.IndexOf(File.ReadAllLines(RevokedFile), serial.ToString("x8")) >= 0; } catch { return false; }
        }

        public static string RevokedUrl { get { return Updater.Repo.Length > 0 ? "https://raw.githubusercontent.com/" + Updater.Repo + "/main/revoked.txt" : ""; } }

        public static void CheckRevokedAsync(Control ui, Action revoked)
        {
            string url = RevokedUrl;
#if DEV
            string over = Environment.GetEnvironmentVariable("CLUTCH_REVOKED_FEED");
            if (!string.IsNullOrEmpty(over)) url = over;
#endif
            if (url.Length == 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string text;
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                    using (WebClient w = new WebClient()) { w.Headers[HttpRequestHeader.UserAgent] = "Clutch/" + AppInfo.Version; w.Encoding = Encoding.UTF8; text = w.DownloadString(url); }
                    List<string> serials = ParseRevoked(text);
                    if (serials == null) return;
                    File.WriteAllLines(RevokedFile, serials.ToArray());
                    if (Check() == State.Revoked) ui.BeginInvoke(revoked);
                }
                catch (Exception ex) { Log.Info("Invite check", ex.Message); }
            });
        }

        public static List<string> ParseRevoked(string text)
        {
            text = text.Replace("\r", "");
            int at = text.LastIndexOf("sig ");
            if (!text.StartsWith("clutch-revoked-1\n") || at < 0) return null;
            string body = text.Substring(0, at);
            byte[] sig; try { sig = Convert.FromBase64String(text.Substring(at + 4).Trim()); } catch { return null; }
            if (!Verify(Encoding.UTF8.GetBytes(body), sig)) return null;
            List<string> o = new List<string>();
            foreach (string l in body.Split('\n')) if (l.Length == 8) o.Add(l);
            return o;
        }

        public static string Encode(byte[] data, int group)
        {
            StringBuilder s = new StringBuilder(); int buffer = 0, bits = 0;
            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b; bits += 8;
                while (bits >= 5) { s.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
            }
            if (bits > 0) s.Append(Alphabet[(buffer << (5 - bits)) & 31]);
            StringBuilder o = new StringBuilder();
            for (int i = 0; i < s.Length; i++) { if (i > 0 && i % group == 0) o.Append('-'); o.Append(s[i]); }
            return o.ToString();
        }

        static byte[] Decode(string text, int bytes)
        {
            List<byte> o = new List<byte>(); int buffer = 0, bits = 0;
            foreach (char ch in text.ToUpperInvariant())
            {
                int v = Alphabet.IndexOf(ch);
                if (v < 0) { if (ch == '-' || char.IsWhiteSpace(ch)) continue; return null; }
                buffer = (buffer << 5) | v; bits += 5;
                if (bits >= 8) { o.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
            }
            return o.Count == bytes || o.Count == bytes + 1 ? o.GetRange(0, bytes).ToArray() : null;
        }
    }

    class ActivateWindow : Window
    {
        readonly Lbl step1 = new Lbl("1.  Send this PC code to the person who invited you", 10, 5, C.Text),
                     note = new Lbl("It's a scrambled code made just for Clutch. It contains nothing about you, your PC or your internet connection.", 9, 0, C.Sub),
                     step2 = new Lbl("2.  Paste the invite key they send back", 10, 5, C.Text),
                     status = new Lbl("", 9.5f, 1, C.Warn);
        readonly Field key = new Field();
        readonly Btn copy = new Btn("Copy", Glyph.Copy, BtnStyle.Standard), activate = new Btn("Activate", "", BtnStyle.Accent), quit = new Btn("Quit", "", BtnStyle.Subtle);
        readonly string reason;
        public bool Done;

        public ActivateWindow(Licence.State why) : base("Activate Clutch", 560, 410)
        {
            reason = why == Licence.State.Expired ? "Your invite has expired. Ask for a new key to keep using Clutch."
                   : why == Licence.State.Revoked ? "Your invite was turned off. Ask the person who invited you if you think that's a mistake."
                   : why == Licence.State.Invalid ? "Your saved invite key doesn't work on this PC. Paste a new one."
                   : "";
            status.Text = reason; note.Wraps = true; status.Wraps = true;
            key.Placeholder = "Invite key";
            copy.Click += delegate { try { Clipboard.SetText(Licence.PcCode); copy.Text = "Copied"; copy.Icon = Glyph.Check; copy.Invalidate(); } catch { } };
            activate.Click += delegate { Activate_(); };
            quit.Click += delegate { Close(); };
            key.Box.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Activate_(); } };
            foreach (Control c in new Control[] { step1, note, copy, step2, key, status, activate, quit }) Controls.Add(c);
        }

        void Activate_()
        {
            string k = key.Box.Text.Trim();
            Licence.State s = Licence.Check(k);
            if (s == Licence.State.Ok && Licence.Save(k)) { Done = true; Close(); return; }
            status.Fore = C.Warn;
            status.Text = s == Licence.State.Missing ? "Paste your invite key first."
                : s == Licence.State.Expired ? "That invite key has expired. Ask for a new one."
                : s == Licence.State.Revoked ? "That invite key has been turned off."
                : "That key doesn't work on this PC. Check you copied all of it, and that it was made for the PC code above.";
            Relayout(); status.Invalidate();
        }

        Rectangle CodeRect { get { return new Rectangle(S(28), S(126), S(560) - S(56) - S(110), S(46)); } }

        protected override void Relayout()
        {
            int x = S(28), w = S(560) - S(56);
            step1.SetBounds(x, S(94), w, S(24));
            copy.SetBounds(x + w - S(100), S(126), S(100), S(46));
            int nh = note.Fit(w); note.SetBounds(x, S(180), w, nh);
            step2.SetBounds(x, S(236), w, S(24));
            key.SetBounds(x, S(266), w, S(40));
            status.SetBounds(x, S(312), w, Math.Max(S(20), status.Fit(w)));
            quit.SetBounds(x + w - S(90), S(410) - S(26) - S(38), S(90), S(38));
            activate.SetBounds(quit.Left - S(10) - S(130), quit.Top, S(130), S(38));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            DrawHeader(g, "Activate Clutch", "Clutch is invite-only for now");
            Rectangle r = CodeRect;
            Ui.FillRound(g, r, S(8), C.Card);
            using (Font f = new Font("Consolas", 15 * Ui.Scale, FontStyle.Bold, GraphicsUnit.Point))
                TextRenderer.DrawText(g, Licence.PcCode, f, r, C.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Front(); key.Box.Focus(); }

        public static bool Run(Licence.State why)
        {
            using (ActivateWindow w = new ActivateWindow(why)) { Application.Run(w); return w.Done; }
        }
    }
}
