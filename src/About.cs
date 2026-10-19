using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace Clutch
{
    class LicencesWindow : Window
    {
        readonly TextView view = new TextView();
        readonly Btn ffmpeg = new Btn("ffmpeg.org", Glyph.Share, BtnStyle.Standard), gpl = new Btn("GPLv3 text", Glyph.Share, BtnStyle.Standard),
                     close = new Btn("Close", "", BtnStyle.Subtle);

        public LicencesWindow() : base("Credits and licences", 600, 520)
        {
            view.Box.Text = Text_().Replace("\r\n", "\n").Replace("\n", "\r\n");
            ffmpeg.Click += delegate { Open("https://ffmpeg.org/"); };
            gpl.Click += delegate { Open("https://www.gnu.org/licenses/gpl-3.0.html"); };
            close.Click += delegate { Close(); };
            foreach (Control c in new Control[] { view, ffmpeg, gpl, close }) Controls.Add(c);
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
        }

        static void Open(string url) { try { Process.Start(url); } catch { } }

        static string Text_()
        {
            string t =
"FFmpeg\n" +
"Clutch records, trims and exports video with FFmpeg " + FFmpeg.PinnedVersion.Split('-')[0] + " (the \"essentials\" build by Gyan Doshi, " + FFmpeg.Source + "). " +
"FFmpeg isn't part of Clutch. It's downloaded from github.com/GyanD/codexffmpeg when you set Clutch up, checked against a SHA-256 checksum built into Clutch, " +
"and kept in %LOCALAPPDATA%\\Clutch\\ffmpeg. Clutch runs it as a separate program and doesn't change it.\n\n" +
"This FFmpeg build is licensed under the GNU General Public License, version 3 (GPLv3). Its source code is available from ffmpeg.org, " +
"and the build's configuration from gyan.dev. FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project.\n\n" +
"Game art and Discord\n" +
"Game icons come from Discord's public list of detectable games; banners from Steam and the Microsoft Store. They belong to their owners. " +
"Clutch isn't affiliated with or endorsed by Discord, Valve, Microsoft or any game publisher.\n\n";
            string lic = Path.Combine(FFmpeg.Dir, "LICENSE.txt");
            try { if (File.Exists(lic)) t += "FFmpeg build licence (from the download)\n" + new string('-', 40) + "\n" + File.ReadAllText(lic).Trim() + "\n"; } catch { }
            return t;
        }

        protected override void Relayout()
        {
            int x = S(28), w = S(600) - S(56), h = S(520);
            int by = h - S(26) - S(38);
            view.SetBounds(x, S(92), w, by - S(20) - S(92));
            ffmpeg.SetBounds(x, by, ffmpeg.PreferredWidth() + S(6), S(38));
            gpl.SetBounds(ffmpeg.Right + S(10), by, gpl.PreferredWidth() + S(6), S(38));
            close.SetBounds(x + w - S(90), by, S(90), S(38));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawHeader(e.Graphics, "Credits and licences", "Clutch " + AppInfo.Version);
        }
    }
}
