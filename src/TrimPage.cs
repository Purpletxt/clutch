using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Clutch
{
    class TrimPage : Page
    {
        public Clip Clip;
        public readonly VideoView Video = new VideoView();
        readonly Btn back = new Btn("Back", Glyph.Back, BtnStyle.Subtle), play = new Btn("", Glyph.Play, BtnStyle.Accent);
        readonly Btn setIn = new Btn("Set start", "\uE892", BtnStyle.Standard), setOut = new Btn("Set end", "\uE893", BtnStyle.Standard), reset = new Btn("", Glyph.Undo, BtnStyle.Subtle);
        readonly Lbl title = new Lbl("Trim & export", 16, 4, C.Text), clipName = new Lbl("", 10, 0, C.Sub), time = new Lbl("", 10, 5, C.Text), range = new Lbl("", 9.5f, 1, C.Sub);
        readonly Timeline line = new Timeline();
        readonly Card frame = new Card(), bar = new Card(), side = new Card();
        readonly Lbl hSize = new Lbl("Size", 11, 4, C.Text), hFormat = new Lbl("Format", 11, 4, C.Text), hAudio = new Lbl("Audio", 11, 4, C.Text), hName = new Lbl("File name", 11, 4, C.Text);
        readonly OptionTiles size = new OptionTiles(2), format = new OptionTiles(2);
        readonly Lbl lGame = new Lbl("Game", 9.5f, 0, C.Text), lMic = new Lbl("Microphone", 9.5f, 0, C.Text), lOther = new Lbl("Other apps", 9.5f, 0, C.Text), lPos = new Lbl("Position", 9.5f, 0, C.Text);
        readonly Lbl audioNote = new Lbl("", 8.5f, 0, C.Faint), note = new Lbl("", 8.5f, 0, C.Faint), status = new Lbl("", 9, 0, C.Sub);
        readonly Slider gameVol = new Slider(), micVol = new Slider(), otherVol = new Slider(), cropPos = new Slider();
        readonly Btn muteGame = new Btn("", Glyph.Volume, BtnStyle.Subtle), muteMic = new Btn("", Glyph.Volume, BtnStyle.Subtle), muteOther = new Btn("", Glyph.Volume, BtnStyle.Subtle);
        int gameBefore = 100, micBefore = 100, otherBefore = 100;
        readonly Field name = new Field();
        readonly Btn export = new Btn("Export", Glyph.Save, BtnStyle.Accent), copy = new Btn("Copy file", Glyph.Copy, BtnStyle.Standard), show = new Btn("Show", Glyph.Folder, BtnStyle.Standard);
        readonly ShadeBar shadeL = new ShadeBar(false), shadeR = new ShadeBar(true);
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        List<string> tracks = new List<string>(); List<double> keyframes = new List<double>();
        bool exporting; double exportProgress; string lastExport = "";
        static readonly int[] Sizes = { 0, 10, 50, 500 };
        int loadId;

        bool Vertical { get { return format.Index == 1; } }

        public TrimPage(MainForm m) : base(m)
        {
            back.Click += delegate { if (Clip != null && Main.Viewer.Clip == Clip) Main.Go(Main.Viewer); else Main.Go(Main.Clips); };
            play.IconPt = 12; play.Round = true;
            play.Click += delegate { if (!Video.Playing && (Video.Position >= line.Out - 0.05 || Video.Position < line.In - 0.05)) Video.Position = line.In; Video.TogglePlay(); };
            setIn.Click += delegate { line.In = Math.Max(0, Math.Min(Video.Position, line.Out - 0.5)); RangeChanged(); };
            setOut.Click += delegate { line.Out = Math.Min(line.Length, Math.Max(Video.Position, line.In + 0.5)); RangeChanged(); };
            setIn.Round = setOut.Round = true; setIn.IconPt = setOut.IconPt = 9; setIn.TextPt = setOut.TextPt = 9.5f; setIn.Weight = setOut.Weight = 4;
            reset.IconPt = 11; reset.Tip = "Reset to the whole clip";
            reset.Click += delegate { line.In = 0; line.Out = line.Length; RangeChanged(); };
            line.TrimMode = true;
            line.Seek += delegate (double t) { Video.Position = t; };
            line.RangeChanged += RangeChanged;
            frame.Fill = Color.Black; frame.Controls.Add(Video);

            size.Add(Glyph.Film, "Original", "Instant, no quality loss");
            size.Add(Glyph.Discord, "10 MB", "Discord");
            size.Add(Glyph.Discord, "50 MB", "Nitro Basic");
            size.Add(Glyph.Discord, "500 MB", "Nitro");
            size.Changed += delegate { UpdateNotes(); };
            format.Add(Glyph.Monitor, "Landscape", "As recorded");
            format.Add("\uE8EA", "Vertical", "TikTok, Shorts, Reels");
            format.Changed += delegate { UpdateNotes(); Shade(); };

            gameVol.Max = 200; gameVol.Value = 100; micVol.Max = 200; micVol.Value = 100; otherVol.Max = 200; otherVol.Value = 100;
            muteGame.IconPt = muteMic.IconPt = muteOther.IconPt = 10;
            muteGame.Click += delegate { Mute(gameVol, ref gameBefore); };
            muteMic.Click += delegate { Mute(micVol, ref micBefore); };
            muteOther.Click += delegate { Mute(otherVol, ref otherBefore); };
            gameVol.Changed += delegate { SyncMute(); }; micVol.Changed += delegate { SyncMute(); }; otherVol.Changed += delegate { SyncMute(); };
            cropPos.Value = 50; cropPos.Changed += delegate { Shade(); };
            foreach (ShadeBar p in new[] { shadeL, shadeR }) { p.Visible = false; frame.Controls.Add(p); }

            export.Click += delegate { Export(); };
            copy.Click += delegate { CopyExport(); };
            show.Click += delegate { if (File.Exists(lastExport)) try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + lastExport + "\""); } catch { } };
            audioNote.Wraps = true; note.Wraps = true; status.Wraps = true;
            foreach (Control c in new Control[] { hSize, size, hFormat, format, lPos, cropPos, hAudio, lGame, gameVol, muteGame, lMic, micVol, muteMic, lOther, otherVol, muteOther, audioNote, hName, name, note, export, status, copy, show }) side.Controls.Add(c);
            foreach (Control c in new Control[] { line, play, time, range, setIn, setOut, reset }) bar.Controls.Add(c);
            foreach (Control c in new Control[] { back, title, clipName, frame, bar, side }) Controls.Add(c);
            foreach (Control c in side.Controls) c.BackColor = C.Card;
            foreach (Control c in bar.Controls) c.BackColor = C.Card;
            timer.Interval = 50; timer.Tick += delegate { Tick(); };
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.Draw(e.Graphics, ClientSize, Point.Empty); }

        public void Open(Clip c)
        {
            Clip = c; loadId++;
            clipName.Text = c.Title;
            line.Length = Math.Max(0.5, c.Duration); line.In = 0; line.Out = line.Length; line.Position = 0;
            List<Bitmap> old = line.Strip; line.Strip = new List<Bitmap>();
            foreach (Bitmap b in old) if (b != null) b.Dispose();
            tracks = new List<string>(); keyframes = new List<double>();
            name.Box.Text = c.Title + " (trim)";
            size.Index = 0; format.Index = 0; gameVol.Value = 100; micVol.Value = 100; otherVol.Value = 100; SyncMute();
            status.Text = ""; lastExport = ""; copy.Visible = show.Visible = false; export.Progress = -1;
            Video.Open(c.Path, false);
#if DEV
            if (Program.Demo) { line.Strip = Demo.Strip(); tracks = new List<string>(new[] { "Game + Mic", "Game only", "Mic", "Other apps" }); line.In = 4.2; line.Out = Math.Min(line.Length, 41.6); line.Position = 12; }
            else
#endif
            {
                string path = c.Path; double dur = c.Duration; int id = loadId;
                int frames = Math.Max(8, Math.Min(18, (line.Width > 0 ? line.Width : S(800)) / S(96)));
                ThreadPool.QueueUserWorkItem(delegate { LoadTracks(path, id); });
                ThreadPool.QueueUserWorkItem(delegate { LoadStrip(path, dur, frames, id); });
            }
            UpdateNotes(); RangeChanged(); Relayout(); size.Invalidate(); format.Invalidate();
        }

        void LoadTracks(string path, int id)
        {
            List<string> t = FFmpeg.AudioTracks(path);
            Post(delegate { if (id == loadId) { tracks = t; UpdateNotes(); } });
            List<double> k = new List<double>();
            FFmpeg.Result r = FFmpeg.Run(FFmpeg.ProbeExe, "-v error -select_streams v:0 -show_entries packet=pts_time,flags -of csv=p=0 " + FFmpeg.Q(path), 20000, null);
            foreach (string l in r.Out.Split('\n'))
            {
                string[] p = l.Trim().Split(','); double v;
                if (p.Length >= 2 && p[1].StartsWith("K") && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) k.Add(v);
            }
            k.Sort();
            Post(delegate { if (id == loadId) keyframes = k; });
        }

        void LoadStrip(string path, double dur, int n, int id)
        {
            if (dur <= 0) dur = FFmpeg.Duration(path);
            string pattern = Path.Combine(Paths.Temp, "strip_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "_%02d.jpg");
            FFmpeg.Run(FFmpeg.Exe, "-hide_banner -loglevel error -threads 2 -skip_frame nokey -i " + FFmpeg.Q(path) + " -vf \"fps=" + FFmpeg.N(n / Math.Max(1, dur)) + ",scale=-2:72\" -frames:v " + n + " -q:v 6 " + FFmpeg.Q(pattern), 30000, null);
            List<Bitmap> strip = new List<Bitmap>();
            for (int i = 1; i <= n; i++)
            {
                string f = pattern.Replace("%02d", i.ToString("00"));
                if (File.Exists(f)) { Bitmap b = Library.LoadImage(f); if (b != null) strip.Add(b); try { File.Delete(f); } catch { } }
            }
            Post(delegate
            {
                if (id != loadId) { foreach (Bitmap b in strip) b.Dispose(); return; }
                List<Bitmap> old = line.Strip; line.Strip = strip; line.Invalidate();
                foreach (Bitmap b in old) if (b != null) b.Dispose();
            });
        }

        void CopyExport()
        {
            if (!File.Exists(lastExport)) return;
            StringCollection sc = new StringCollection(); sc.Add(lastExport); Clipboard.SetFileDropList(sc);
            Main.Toast.Show(Glyph.Check, "Copied. Paste it into Discord with Ctrl+V.", null, null, 3500);
        }

        void Post(Action a) { try { if (IsHandleCreated) BeginInvoke(a); } catch { } }

        bool Separate { get { return Track(tracks, "Mic") > 0 || Track(tracks, "Other apps") > 0; } }
        bool HasOther { get { return Track(tracks, "Other apps") > 0; } }
        bool HasMic { get { return Track(tracks, "Mic") > 0; } }

        void Mute(Slider s, ref int before)
        {
            if (s.Value > 0) { before = s.Value; s.Value = 0; } else s.Value = Math.Max(10, before);
            s.Invalidate(); SyncMute();
        }
        void SyncMute()
        {
            foreach (Btn b in new[] { muteGame, muteMic, muteOther })
            {
                string i = (b == muteGame ? gameVol : b == muteMic ? micVol : otherVol).Value == 0 ? Glyph.Mute : Glyph.Volume;
                if (b.Icon != i) { b.Icon = i; b.Invalidate(); }
            }
        }

        double KeepWidth { get { double ar = Video.Aspect > 0 ? Video.Aspect : 16 / 9.0; return Math.Min(1, (9 / 16.0) / ar); } }

        void Shade()
        {
            bool on = Vertical && Video.Width > 0;
            shadeL.Visible = shadeR.Visible = on;
            if (!on) return;
            int vw = Video.Width, keep = (int)(vw * KeepWidth), left = (int)((vw - keep) * cropPos.Value / 100.0);
            shadeL.SetBounds(Video.Left, Video.Top, left, Video.Height);
            shadeR.SetBounds(Video.Left + left + keep, Video.Top, vw - left - keep, Video.Height);
            shadeL.BringToFront(); shadeR.BringToFront();
        }

        int VerticalKbps()
        {
            double src = Clip != null && Clip.Duration > 0 ? Clip.Size * 8 / 1000.0 / Clip.Duration : 8000;
            return (int)Math.Max(4000, Math.Min(16000, src * KeepWidth * 1.6));
        }

        void UpdateNotes()
        {
            lMic.Visible = micVol.Visible = muteMic.Visible = HasMic;
            lOther.Visible = otherVol.Visible = muteOther.Visible = HasOther;
            muteGame.Visible = tracks.Count > 0;
            lGame.Text = HasOther ? "Game" : Separate ? "Desktop" : "Volume";
            if (tracks.Count == 0) audioNote.Text = Program.Demo ? "" : "This clip has no audio.";
            else if (!Separate) audioNote.Text = "One audio track.";
            else if (HasOther) audioNote.Text = "Game, other apps and mic are separate, so you can mix them however you like.";
            else audioNote.Text = "Desktop audio and mic are separate, so you can lower or mute either.";
            gameVol.Enabled = tracks.Count > 0;
            int i = Math.Max(0, size.Index);
            note.Text = Vertical ? "Vertical crops to 9:16" + (i == 0 ? " at 1080 \u00D7 1920" : " and fits " + Sizes[i] + " MB") + ". Drag Position to choose what stays in frame."
                : i == 0 ? "No quality loss. The start snaps to the nearest keyframe, within a second."
                : "Re-encoded with your " + Encoder.Label(EncoderName()) + " encoder to fit " + Sizes[i] + " MB, with one mixed audio track.";
            string n = name.Box.Text;
            if (i == 0 && n.EndsWith(" (Discord)")) name.Box.Text = n.Substring(0, n.Length - 10) + " (trim)";
            if (i > 0 && n.EndsWith(" (trim)")) name.Box.Text = n.Substring(0, n.Length - 7) + " (Discord)";
            ExportLabel();
            Relayout();
        }

        public static string EncoderName()
        {
            string e = Recorder.EncoderUsed.Length > 0 ? Recorder.EncoderUsed : Settings.Cur.ProbeResult;
            return e.Length > 0 ? e.Split(':')[0] : "x264";
        }

        void ExportLabel()
        {
            if (Clip == null || exporting) return;
            int i = Math.Max(0, size.Index);
            string est = i > 0 ? "\u2264 " + Sizes[i] + " MB"
                : Vertical ? "\u2248 " + Library.NiceSize((long)((VerticalKbps() + 192) * 125.0 * (line.Out - line.In)))
                : "\u2248 " + Library.NiceSize((long)(Clip.Size * (line.Out - line.In) / Math.Max(0.1, line.Length)));
            string t = "Export  \u00B7  " + est;
            if (export.Text != t) { export.Text = t; export.Invalidate(); }
        }

        public bool Key(Keys k)
        {
            switch (k)
            {
                case Keys.Space: play.PerformClick(); return true;
                case Keys.I: setIn.PerformClick(); return true;
                case Keys.O: setOut.PerformClick(); return true;
                case Keys.Left: Video.Position = Math.Max(0, Video.Position - 1); return true;
                case Keys.Right: Video.Position = Math.Min(line.Length, Video.Position + 1); return true;
                case Keys.Home: Video.Position = line.In; return true;
                case Keys.End: Video.Position = line.Out; return true;
                case Keys.Escape: back.PerformClick(); return true;
            }
            return false;
        }

        void RangeChanged()
        {
            string t = "Length " + (line.Out - line.In).ToString("0.0") + " s";
            if (line.In > 0.05 || line.Out < line.Length - 0.05) t += "  \u00B7  cut " + (line.Length - (line.Out - line.In)).ToString("0.0") + " s";
            range.Text = t; range.Invalidate();
            reset.Enabled = line.In > 0.05 || line.Out < line.Length - 0.05;
            line.Invalidate();
            ExportLabel();
        }

        static string Stamp(double s) { int m = (int)(s / 60); return m + ":" + (s - m * 60).ToString("00.0", CultureInfo.InvariantCulture); }

        public override void Relayout()
        {
            int pad = S(24), w = ClientSize.Width, h = ClientSize.Height, sw = S(300);
            back.SetBounds(pad - S(10), S(8), S(84), S(34));
            int tw = Ui.Measure(title.Text, Ui.F(14, 4)).Width + S(6);
            title.Pt = 14; title.SetBounds(back.Right + S(10), S(8), tw, S(34));
            clipName.SetBounds(title.Right + S(10), S(9), Math.Max(0, w - pad - sw - S(20) - title.Right - S(10)), S(32));
            int top = S(52), left = pad, right = w - pad - sw - S(16);
            int barH = S(146), vw = right - left, vh = h - top - barH - S(12) - S(16);
            double ar = Video.Aspect > 0 ? Video.Aspect : VideoView.DefaultAspect(); shownAspect = ar;
            int fw = Math.Min(vw, (int)(vh * ar)), fh = (int)(fw / ar);
            int spare = vh - fh;
            frame.SetBounds(left + (vw - fw) / 2, top + spare / 3, fw, fh);
            Video.SetBounds(S(3), S(3), fw - S(6), fh - S(6));

            bar.SetBounds(left, Math.Min(h - S(16) - barH, frame.Bottom + S(12)), vw, barH);
            int bw = bar.Width, bx = S(14);
            line.SetBounds(bx - S(8), S(8), bw - bx * 2 + S(16), S(88));
            int ry = S(100), rh = S(38);
            play.SetBounds(bx, ry, rh, rh);
            time.SetBounds(play.Right + S(12), ry, S(150), rh);
            setOut.Height = setIn.Height = rh; setIn.Width = setIn.PreferredWidth() + S(10); setOut.Width = setOut.PreferredWidth() + S(10);
            reset.SetBounds(bw - bx - rh, ry, rh, rh);
            setOut.Location = new Point(reset.Left - S(8) - setOut.Width, ry); setIn.Location = new Point(setOut.Left - S(8) - setIn.Width, ry);
            range.SetBounds(time.Right + S(8), ry, Math.Max(0, setIn.Left - time.Right - S(16)), rh); range.Center = true;

            side.SetBounds(w - pad - sw, top, sw, h - top - S(16));
            if (!LayoutSide(false)) LayoutSide(true);
            Shade();
        }

        bool LayoutSide(bool compact)
        {
            int x = S(18), cw = side.Width - S(36), y = S(16), gap = compact ? S(12) : S(18);
            size.ShowSub = format.ShowSub = !compact;
            hSize.SetBounds(x, y, cw, S(24)); y += S(30);
            size.SetBounds(x, y, cw, size.Needed); y += size.Needed + gap;
            hFormat.SetBounds(x, y, cw, S(24)); y += S(30);
            format.SetBounds(x, y, cw, format.Needed); y += format.Needed + S(6);
            lPos.Visible = cropPos.Visible = Vertical;
            if (Vertical) { lPos.SetBounds(x, y, S(80), S(30)); cropPos.SetBounds(x + S(84), y, cw - S(84), S(30)); y += S(34); }
            y += gap - S(6);
            hAudio.SetBounds(x, y, cw, S(24)); y += S(30);
            int mw = S(32);
            lGame.SetBounds(x, y, S(90), S(30)); gameVol.SetBounds(x + S(92), y, cw - S(92) - mw, S(30)); muteGame.SetBounds(x + cw - S(30), y, S(30), S(30)); y += S(34);
            if (micVol.Visible) { lMic.SetBounds(x, y, S(90), S(30)); micVol.SetBounds(x + S(92), y, cw - S(92) - mw, S(30)); muteMic.SetBounds(x + cw - S(30), y, S(30), S(30)); y += S(34); }
            if (otherVol.Visible) { lOther.SetBounds(x, y, S(90), S(30)); otherVol.SetBounds(x + S(92), y, cw - S(92) - mw, S(30)); muteOther.SetBounds(x + cw - S(30), y, S(30), S(30)); y += S(34); }
            audioNote.Visible = !compact && audioNote.Text.Length > 0;
            if (audioNote.Visible) { int ah = audioNote.Fit(cw); audioNote.SetBounds(x, y, cw, ah); y += ah; }
            y += gap;
            hName.SetBounds(x, y, cw, S(24)); y += S(30);
            name.SetBounds(x, y, cw, S(36)); y += S(36) + gap;
            int bottom = side.Height - S(16);
            int actH = copy.Visible ? S(36) + S(8) : 0;
            int sh = status.Text.Length > 0 ? Math.Max(S(18), status.Fit(cw)) + S(8) : 0;
            int exY = bottom - actH - sh - S(46);
            note.Visible = !compact;
            if (note.Visible) { int nh = note.Fit(cw); note.SetBounds(x, Math.Max(y, exY - nh - S(10)), cw, nh); if (note.Top + nh + S(10) > exY) note.Visible = false; }
            export.SetBounds(x, Math.Max(y, exY), cw, S(46));
            status.SetBounds(x, export.Bottom + S(8), cw, Math.Max(S(18), status.Fit(cw)));
            int ay = export.Bottom + S(8) + sh;
            copy.SetBounds(x, ay, (cw - S(8)) / 2, S(36)); show.SetBounds(copy.Right + S(8), ay, cw - copy.Width - S(8), S(36));
            return y <= exY || compact;
        }

        double shownAspect; string lastTime = ""; double lastPos = -1; bool lastPlaying;
        void Tick()
        {
            double va = Video.Aspect; if (va > 0 && Math.Abs(va - shownAspect) > 0.01) Relayout();
            Video.Poll();
            double len = Video.Length;
            if (len > 0 && Math.Abs(len - line.Length) > 0.05 && line.Out >= line.Length - 0.01) { line.Length = len; line.Out = len; RangeChanged(); }
            bool playing = Video.Playing;
            if (playing && Video.Position >= line.Out) { Video.Pause(); Video.Position = line.In; playing = false; }
            double pos = Program.Demo ? line.Position : Video.Position;
            if (Math.Abs(pos - lastPos) > 0.01) { lastPos = pos; line.Position = pos; line.Invalidate(); }
            if (playing != lastPlaying) { lastPlaying = playing; play.Icon = playing ? Glyph.Pause : Glyph.Play; play.Invalidate(); }
            string t = Stamp(pos) + "  /  " + Stamp(line.Length);
            if (t != lastTime) { lastTime = t; time.Text = t; }
            if (exporting)
            {
                string et = "Exporting\u2026  " + (int)(exportProgress * 100) + "%";
                if (export.Text != et) { export.Text = et; export.Progress = (float)exportProgress; export.Invalidate(); }
            }
        }

#if DEV
        public void ShotVertical() { format.Index = 1; cropPos.Value = 30; UpdateNotes(); Shade(); }
#endif
        public override void Entered() { lastPos = -1; timer.Start(); }
        public override void Leaving() { timer.Stop(); Video.Pause(); }

        void Export()
        {
            if (exporting || Clip == null) return;
            if (!FFmpeg.Ready) { Main.Toast.Show(Glyph.Warning, "Finish the FFmpeg setup first.", null, null, 4000); return; }
            Video.Pause();
            string src = Clip.Path, game = Clip.Game;
            bool vert = Vertical; double pos = cropPos.Value / 100.0; int vk = VerticalKbps();
            string file = name.Box.Text.Trim().Length > 0 ? name.Box.Text.Trim() : Clip.Title + " (trim)";
            if (vert && file.IndexOf("vertical", StringComparison.OrdinalIgnoreCase) < 0) file += " (vertical)";
            string outPath = Library.UniquePath(Path.Combine(Path.GetDirectoryName(src), Library.SafeName(file) + ".mp4"));
            double a = line.In, b = line.Out;
            int target = Sizes[Math.Max(0, size.Index)];
            if (target == 0 && !vert && keyframes.Count > 0) { double k = 0; foreach (double t in keyframes) if (t <= a + 0.02) k = t; a = k; }
            double gv = gameVol.Value / 100.0, mv = micVol.Value / 100.0, ov = otherVol.Value / 100.0;
            List<string> tr = tracks; string enc = EncoderName();
            exporting = true; exportProgress = 0; export.Progress = 0; copy.Visible = show.Visible = false; status.Text = ""; status.Fore = C.Sub; Relayout();
            size.Enabled = format.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null; long bytes = 0;
                try { err = RunExport(src, outPath, a, b, target, gv, mv, ov, tr, enc, vert, pos, vk, delegate (double p) { exportProgress = p; }); if (err == null) bytes = new FileInfo(outPath).Length; }
                catch (Exception ex) { err = ex.Message; Log.Error("Export", ex); }
                Post(delegate
                {
                    exporting = false; export.Progress = -1; size.Enabled = format.Enabled = true;
                    if (err == null)
                    {
                        lastExport = outPath;
                        status.Fore = C.Good; status.Text = "Saved \"" + Path.GetFileName(outPath) + "\" (" + Library.NiceSize(bytes) + ")";
                        copy.Visible = show.Visible = true;
                        Library.Add(outPath, game, b - a);
                        Main.Toast.Show(Glyph.Check, "Exported " + Library.NiceSize(bytes), "Copy file", CopyExport, 6000);
                    }
                    else { status.Fore = C.Warn; status.Text = err; }
                    ExportLabel(); Relayout(); status.Invalidate(); export.Invalidate();
                });
            });
        }

#if DEV
        public string TestExport(string src, string outPath, double a, double b, int targetMB, double gv, double mv)
        {
            return RunExport(src, outPath, a, b, targetMB, gv, mv, 1, FFmpeg.AudioTracks(src), EncoderName(), false, 0.5, 0, null);
        }
        public string TestExportVertical(string src, string outPath, double a, double b, int targetMB, double pos)
        {
            return RunExport(src, outPath, a, b, targetMB, 1, 1, 1, FFmpeg.AudioTracks(src), EncoderName(), true, pos, 8000, null);
        }
#endif

        public static int Track(List<string> tr, params string[] names)
        {
            foreach (string n in names) for (int i = 1; i < tr.Count; i++) if (tr[i] == n) return i;
            return -1;
        }

        public static string RunExport(string src, string outPath, double a, double b, int targetMB, double gv, double mv, double ov, List<string> tr, string enc, bool vert, double pos, int vertKbps, Action<double> onProgress)
        {
            double dur = b - a;
            int gi = Track(tr, "Game only", "Game"), mi = Track(tr, "Mic"), oi = Track(tr, "Other apps");
            List<string> parts = new List<string>(), labels = new List<string>();
            if (gi > 0) { parts.Add("[0:a:" + gi + "]volume=" + FFmpeg.N(gv) + "[g]"); labels.Add("[g]"); }
            if (mi > 0) { parts.Add("[0:a:" + mi + "]volume=" + FFmpeg.N(mv) + "[m]"); labels.Add("[m]"); }
            if (oi > 0) { parts.Add("[0:a:" + oi + "]volume=" + FFmpeg.N(ov) + "[o]"); labels.Add("[o]"); }
            bool sep = parts.Count >= 2;
            string part = outPath + ".part";
            Action<string> progress = delegate (string l)
            {
                if (l.StartsWith("out_time_us=")) { long us; if (long.TryParse(l.Substring(12), out us) && onProgress != null) onProgress(Math.Max(0, Math.Min(1, us / 1e6 / dur))); }
            };
            string input = "-y -hide_banner -loglevel error -nostats -progress pipe:1 -ss " + FFmpeg.N(a) + " -i " + FFmpeg.Q(src) + " -t " + FFmpeg.N(dur);
            string mix = sep ? string.Join(";", parts.ToArray()) + ";" + string.Join("", labels.ToArray()) + "amix=inputs=" + parts.Count + ":duration=longest:normalize=0[mix]"
                       : parts.Count == 1 ? parts[0].Substring(0, parts[0].Length - 3) + "[mix]"
                       : tr.Count > 0 ? "[0:a:0]volume=" + FFmpeg.N(gv) + "[mix]" : "";
            string keep = "";
            if (sep)
            {
                for (int i = 1; i < tr.Count; i++) keep += " -map 0:a:" + i;
                for (int i = 0; i < tr.Count; i++) keep += " -metadata:s:a:" + i + " handler_name=\"" + (tr[i].Length > 0 ? tr[i] : "Audio") + "\" -disposition:a:" + i + (i == 0 ? " default" : " 0");
            }
            Func<int, string> crop = delegate (int h) { return "crop='min(iw,trunc(ih*9/32)*2)':ih:'(iw-ow)*" + FFmpeg.N(pos) + "':0,scale=" + (h * 9 / 16 / 2 * 2) + ":" + h + ","; };
            if (targetMB == 0 && vert)
            {
                string args = input + " -map 0:v:0 -vf \"" + crop(1920) + "fps='min(60,source_fps)'\" " + Encoder.FileArgs(enc) + " -b:v " + vertKbps + "k -maxrate " + (vertKbps * 3 / 2) + "k -bufsize " + (vertKbps * 2) + "k";
                if (mix.Length > 0) args += " -filter_complex \"" + mix + "\" -map [mix] -c:a aac -b:a 192k -ac 2";
                args += " -movflags +faststart -f mp4 " + FFmpeg.Q(part);
                FFmpeg.Result r = FFmpeg.Run(FFmpeg.Exe, args, 1800000, progress);
                if (r.Exit != 0 && enc != "x264") { Log.Info("Export", enc + " failed, retrying on the CPU: " + Encoder.Short(r.Err)); r = FFmpeg.Run(FFmpeg.Exe, args.Replace(Encoder.FileArgs(enc), Encoder.FileArgs("x264")), 1800000, progress); }
                if (r.Exit != 0) { Log.Error("Export", Encoder.Short(r.Err)); try { File.Delete(part); } catch { } return "Export failed. Details are in the error log."; }
            }
            else if (targetMB == 0)
            {
                string args = input + " -map 0:v:0 -c:v copy";
                if (mix.Length > 0) args += " -filter_complex \"" + mix + "\" -map [mix]";
                if (sep) args += keep;
                if (mix.Length > 0) args += " -c:a aac -b:a 192k";
                args += " -avoid_negative_ts make_zero -movflags +faststart -f mp4 " + FFmpeg.Q(part);
                FFmpeg.Result r = FFmpeg.Run(FFmpeg.Exe, args, 600000, progress);
                if (r.Exit != 0) { Log.Error("Export", Encoder.Short(r.Err)); try { File.Delete(part); } catch { } return "Export failed. Details are in the error log."; }
            }
            else
            {
                long limit = targetMB * 1000L * 1000L;
                double factor = 0.92;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    int audioK = targetMB <= 10 ? 96 : 160;
                    int videoK = (int)(limit * 8 * factor / dur / 1000) - (mix.Length > 0 ? audioK : 0) - 16;
                    if (videoK < 120) return "This part is too long to fit in " + targetMB + " MB. Trim it shorter and try again.";
                    int maxH = videoK < 700 ? 480 : videoK < 1500 ? 720 : videoK < 4500 ? 1080 : 1440;
                    int fpsCap = videoK < 2500 ? 30 : 60;
                    string vf = (vert ? crop(maxH <= 480 ? 854 : maxH <= 720 ? 1280 : 1920) : "scale=-2:'min(" + maxH + ",ih)',") + "fps='min(" + fpsCap + ",source_fps)'";
                    string args = input + " -map 0:v:0 -vf \"" + vf + "\" " + Encoder.FileArgs(enc) + " -b:v " + videoK + "k -maxrate " + (videoK * 5 / 4) + "k -bufsize " + videoK + "k";
                    if (mix.Length > 0) args += " -filter_complex \"" + mix + "\" -map [mix] -c:a aac -b:a " + audioK + "k -ac 2";
                    args += " -movflags +faststart -f mp4 " + FFmpeg.Q(part);
                    FFmpeg.Result r = FFmpeg.Run(FFmpeg.Exe, args, 1800000, progress);
                    if (r.Exit != 0 && enc != "x264") { Log.Info("Export", enc + " failed, retrying on the CPU: " + Encoder.Short(r.Err)); enc = "x264"; attempt--; continue; }
                    if (r.Exit != 0) { Log.Error("Export", Encoder.Short(r.Err)); try { File.Delete(part); } catch { } return "Export failed. Details are in the error log."; }
                    long got = new FileInfo(part).Length;
                    if (got <= limit) break;
                    if (attempt == 2) { try { File.Delete(part); } catch { } return "Couldn't get it under " + targetMB + " MB. Trim it shorter and try again."; }
                    factor *= 0.92 * limit / got;
                }
            }
            if (File.Exists(outPath)) File.Delete(outPath);
            File.Move(part, outPath);
            return null;
        }
    }
}
