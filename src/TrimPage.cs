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
        readonly Btn back = new Btn("Back", Glyph.Back, BtnStyle.Subtle), play = new Btn("", Glyph.Play, BtnStyle.Subtle);
        readonly Btn setIn = new Btn("Set start  (I)", "", BtnStyle.Standard), setOut = new Btn("Set end  (O)", "", BtnStyle.Standard);
        readonly Lbl title = new Lbl("Trim & export", 16, 4, C.Text), clipName = new Lbl("", 9.5f, 0, C.Sub), range = new Lbl("", 9.5f, 1, C.Sub);
        readonly Timeline line = new Timeline();
        readonly Card frame = new Card(), side = new Card();
        readonly Lbl hAudio = new Lbl("Audio", 11.5f, 4, C.Text), lGame = new Lbl("Game", 9.5f, 0, C.Text), lMic = new Lbl("Microphone", 9.5f, 0, C.Text), audioNote = new Lbl("", 9, 0, C.Sub);
        readonly Slider gameVol = new Slider(), micVol = new Slider();
        readonly Btn muteGame = new Btn("", Glyph.Volume, BtnStyle.Subtle), muteMic = new Btn("", Glyph.Volume, BtnStyle.Subtle);
        int gameBefore = 100, micBefore = 100;
        readonly Btn advanced = new Btn("Advanced", Glyph.Down, BtnStyle.Subtle);
        readonly Lbl lVertical = new Lbl("Vertical  9:16", 9.5f, 0, C.Text), lPos = new Lbl("Position", 9.5f, 0, C.Text);
        readonly Toggle vertical = new Toggle();
        readonly Slider cropPos = new Slider();
        readonly Panel shadeL = new Panel(), shadeR = new Panel();
        bool showAdvanced;
        readonly Lbl hExport = new Lbl("Export", 11.5f, 4, C.Text), lName = new Lbl("File name", 9.5f, 0, C.Text), lSize = new Lbl("Quality and size", 9.5f, 0, C.Text), sizeNote = new Lbl("", 9, 0, C.Sub);
        readonly Field name = new Field();
        readonly Drop size = new Drop();
        readonly Btn export = new Btn("Export", Glyph.Save, BtnStyle.Accent), copy = new Btn("Copy file", "", BtnStyle.Standard), show = new Btn("Show", Glyph.Folder, BtnStyle.Standard);
        readonly Lbl status = new Lbl("", 9, 0, C.Sub);
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        List<string> tracks = new List<string>(); List<double> keyframes = new List<double>();
        bool exporting; double exportProgress; string lastExport = "";
        static readonly int[] Sizes = { 0, 10, 50, 500 };

        public TrimPage(MainForm m) : base(m)
        {
            back.Click += delegate { if (Clip != null && Main.Viewer.Clip == Clip) Main.Go(Main.Viewer); else Main.Go(Main.Clips); };
            play.IconPt = 14;
            play.Click += delegate { if (!Video.Playing && (Video.Position >= line.Out - 0.05 || Video.Position < line.In - 0.05)) Video.Position = line.In; Video.TogglePlay(); };
            setIn.Click += delegate { line.In = Math.Min(Video.Position, line.Out - 0.5); line.In = Math.Max(0, line.In); RangeChanged(); };
            setOut.Click += delegate { line.Out = Math.Max(Video.Position, line.In + 0.5); line.Out = Math.Min(line.Length, line.Out); RangeChanged(); };
            line.TrimMode = true;
            line.Seek += delegate (double t) { Video.Position = t; };
            line.RangeChanged += RangeChanged;
            frame.Fill = Color.Black; frame.Controls.Add(Video);
            gameVol.Max = 200; gameVol.Value = 100; micVol.Max = 200; micVol.Value = 100;
            muteGame.IconPt = muteMic.IconPt = 10;
            muteGame.Click += delegate { Mute(gameVol, ref gameBefore); };
            muteMic.Click += delegate { Mute(micVol, ref micBefore); };
            gameVol.Changed += delegate { SyncMute(); }; micVol.Changed += delegate { SyncMute(); };
            advanced.TextPt = 9.5f; advanced.IconPt = 8;
            advanced.Click += delegate { showAdvanced = !showAdvanced || vertical.On; Relayout(); side.Invalidate(); };
            vertical.Changed += delegate { UpdateNotes(); Shade(); };
            cropPos.Value = 50; cropPos.Changed += delegate { Shade(); };
            foreach (Panel p in new[] { shadeL, shadeR }) { p.BackColor = Color.FromArgb(10, 9, 15); p.Visible = false; frame.Controls.Add(p); }
            shadeL.Paint += delegate (object o, PaintEventArgs e) { using (SolidBrush b = new SolidBrush(C.Accent)) e.Graphics.FillRectangle(b, shadeL.Width - S(2), 0, S(2), shadeL.Height); };
            shadeR.Paint += delegate (object o, PaintEventArgs e) { using (SolidBrush b = new SolidBrush(C.Accent)) e.Graphics.FillRectangle(b, 0, 0, S(2), shadeR.Height); };
            size.Items.AddRange(new[] { "Original quality (instant)", "Discord: under 10 MB", "Discord Nitro Basic: under 50 MB", "Discord Nitro: under 500 MB" });
            size.Changed += delegate { UpdateNotes(); };
            export.Click += delegate { Export(); };
            copy.Click += delegate { CopyExport(); };
            show.Click += delegate { if (File.Exists(lastExport)) try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + lastExport + "\""); } catch { } };
            audioNote.Wraps = true; sizeNote.Wraps = true; status.Wraps = true;
            foreach (Control c in new Control[] { hAudio, lGame, gameVol, muteGame, lMic, micVol, muteMic, audioNote, hExport, lName, name, lSize, size, advanced, lVertical, vertical, lPos, cropPos, sizeNote, export, status, copy, show }) side.Controls.Add(c);
            foreach (Control c in new Control[] { back, title, clipName, frame, play, line, range, setIn, setOut, side }) Controls.Add(c);
            timer.Interval = 80; timer.Tick += delegate { Tick(); };
        }

        public void Open(Clip c)
        {
            Clip = c;
            clipName.Text = c.Title;
            line.Length = Math.Max(0.5, c.Duration); line.In = 0; line.Out = line.Length; line.Position = 0;
            foreach (Bitmap b in line.Strip) if (b != null) b.Dispose();
            line.Strip = new List<Bitmap>(); tracks = new List<string>(); keyframes = new List<double>();
            name.Box.Text = c.Title + " (trim)";
            size.Index = 0; gameVol.Value = 100; micVol.Value = 100; SyncMute();
            status.Text = ""; lastExport = ""; copy.Visible = show.Visible = false;
            Video.Open(c.Path, false);
#if DEV
            if (Program.Demo) { line.Strip = Demo.Strip(); tracks = new List<string>(new[] { "Game + Mic", "Game", "Mic" }); line.In = 4.2; line.Out = Math.Min(line.Length, 41.6); line.Position = 12; }
            else
#endif
            {
                string path = c.Path; double dur = c.Duration;
                ThreadPool.QueueUserWorkItem(delegate { LoadDetails(path, dur); });
            }
            UpdateNotes(); RangeChanged(); Relayout();
        }

        void LoadDetails(string path, double dur)
        {
            List<string> t = FFmpeg.AudioTracks(path);
            List<double> k = new List<double>();
            FFmpeg.Result r = FFmpeg.Run(FFmpeg.ProbeExe, "-v error -select_streams v:0 -show_entries packet=pts_time,flags -of csv=p=0 " + FFmpeg.Q(path), 20000, null);
            foreach (string l in r.Out.Split('\n'))
            {
                string[] p = l.Trim().Split(','); double v;
                if (p.Length >= 2 && p[1].StartsWith("K") && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) k.Add(v);
            }
            k.Sort();
            Post(delegate { if (Clip != null && Clip.Path == path) { tracks = t; keyframes = k; UpdateNotes(); Relayout(); } });

            if (dur <= 0) dur = FFmpeg.Duration(path);
            string pattern = Path.Combine(Paths.Temp, "strip_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "_%02d.jpg");
            int n = 10;
            FFmpeg.Run(FFmpeg.Exe, "-hide_banner -loglevel error -skip_frame nokey -i " + FFmpeg.Q(path) + " -vf \"fps=" + FFmpeg.N(n / Math.Max(1, dur)) + ",scale=-2:96\" -frames:v " + n + " -q:v 5 " + FFmpeg.Q(pattern), 30000, null);
            List<Bitmap> strip = new List<Bitmap>();
            for (int i = 1; i <= n; i++)
            {
                string f = pattern.Replace("%02d", i.ToString("00"));
                if (File.Exists(f)) { Bitmap b = Library.LoadImage(f); if (b != null) strip.Add(b); try { File.Delete(f); } catch { } }
            }
            Post(delegate { if (Clip != null && Clip.Path == path) { line.Strip = strip; line.Invalidate(); } });
        }

        void CopyExport()
        {
            if (!File.Exists(lastExport)) return;
            StringCollection sc = new StringCollection(); sc.Add(lastExport); Clipboard.SetFileDropList(sc);
            Main.Toast.Show(Glyph.Check, "Copied. Paste it into Discord with Ctrl+V.", null, null, 3500);
        }

        void Post(Action a) { try { if (IsHandleCreated) BeginInvoke(a); } catch { } }

        bool Separate { get { return tracks.Count >= 3; } }

        void Mute(Slider s, ref int before)
        {
            if (s.Value > 0) { before = s.Value; s.Value = 0; } else s.Value = Math.Max(10, before);
            s.Invalidate(); SyncMute();
        }
        void SyncMute()
        {
            foreach (Btn b in new[] { muteGame, muteMic })
            {
                string i = (b == muteGame ? gameVol : micVol).Value == 0 ? Glyph.Mute : Glyph.Volume;
                if (b.Icon != i) { b.Icon = i; b.Invalidate(); }
            }
        }

        double KeepWidth { get { double ar = Video.Aspect > 0 ? Video.Aspect : 16 / 9.0; return Math.Min(1, (9 / 16.0) / ar); } }

        void Shade()
        {
            bool on = vertical.On && Video.Width > 0;
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
            lMic.Visible = micVol.Visible = muteMic.Visible = Separate;
            muteGame.Visible = tracks.Count > 0;
            lGame.Text = Separate ? "Game" : "Volume";
            if (tracks.Count == 0) audioNote.Text = Program.Demo ? "" : "This clip has no audio.";
            else if (!Separate) audioNote.Text = "This clip has one audio track.";
            else audioNote.Text = "Game and mic are on separate tracks, so you can lower or mute either one.";
            gameVol.Enabled = tracks.Count > 0;
            int i = Math.Max(0, size.Index);
            ExportLabel();
            if (vertical.On) showAdvanced = true;
            sizeNote.Text = vertical.On ? "For TikTok, Shorts and Reels" + (i == 0 ? " (1080 \u00D7 1920)" : ", under " + Sizes[i] + " MB") + ". Position picks what stays in frame."
                : i == 0 ? "No quality loss. The start snaps to the nearest keyframe (within 1 s)."
                : "Re-encoded with your " + Encoder.Label(EncoderName()) + " encoder to fit " + Sizes[i] + " MB. One mixed audio track.";
            string n = name.Box.Text;
            if (i == 0 && n.EndsWith(" (Discord)")) name.Box.Text = n.Substring(0, n.Length - 10) + " (trim)";
            if (i > 0 && n.EndsWith(" (trim)")) name.Box.Text = n.Substring(0, n.Length - 7) + " (Discord)";
            Relayout();
        }

        static string EncoderName()
        {
            string e = Recorder.EncoderUsed.Length > 0 ? Recorder.EncoderUsed : Settings.Cur.ProbeResult;
            return e.Length > 0 ? e.Split(':')[0] : "x264";
        }

        void ExportLabel()
        {
            if (Clip == null) return;
            int i = Math.Max(0, size.Index);
            string est = i > 0 ? "\u2264 " + Sizes[i] + " MB"
                : vertical.On ? "\u2248 " + Library.NiceSize((long)((VerticalKbps() + 192) * 125.0 * (line.Out - line.In)))
                : "\u2248 " + Library.NiceSize((long)(Clip.Size * (line.Out - line.In) / Math.Max(0.1, line.Length)));
            export.Text = "Export  \u00B7  " + est;
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
                case Keys.Escape: back.PerformClick(); return true;
            }
            return false;
        }

        void RangeChanged()
        {
            range.Text = "Start " + Stamp(line.In) + "   \u00B7   End " + Stamp(line.Out) + "   \u00B7   Length " + (line.Out - line.In).ToString("0.0") + " s";
            line.Invalidate();
            ExportLabel();
        }

        static string Stamp(double s) { int m = (int)(s / 60); return m + ":" + (s - m * 60).ToString("00.0", CultureInfo.InvariantCulture); }

        public override void Relayout()
        {
            int pad = S(28), w = ClientSize.Width, h = ClientSize.Height, sw = S(330);
            back.SetBounds(pad - S(10), S(14), S(90), S(34));
            title.SetBounds(pad, S(54), S(300), S(32));
            clipName.SetBounds(pad, S(86), w - pad * 2 - sw, S(22));
            int left = pad, right = w - pad - sw - S(24);
            int top = S(122), timelineH = S(78), bottomBar = S(46);
            int vh = h - top - timelineH - bottomBar - S(36), vw = right - left;
            double ar = Video.Aspect > 0 ? Video.Aspect : VideoView.DefaultAspect(); shownAspect = ar;
            int fw = Math.Min(vw, (int)(vh * ar)), fh = (int)(fw / ar);
            frame.SetBounds(left + (vw - fw) / 2, top, fw, fh);
            Video.SetBounds(S(4), S(4), fw - S(8), fh - S(8));
            int ly = frame.Bottom + S(18);
            line.SetBounds(left, ly, vw, timelineH);
            int by = line.Bottom + S(10);
            play.SetBounds(left - S(4), by, S(40), S(40));
            setOut.Height = setIn.Height = S(34); setIn.Width = setIn.PreferredWidth(); setOut.Width = setOut.PreferredWidth();
            setOut.Location = new Point(right - setOut.Width, by + S(3)); setIn.Location = new Point(setOut.Left - setIn.Width - S(8), by + S(3));
            range.SetBounds(play.Right + S(8), by, setIn.Left - play.Right - S(16), S(40));

            side.SetBounds(w - pad - sw, top, sw, h - top - S(24));
            if (!LayoutSide(false)) LayoutSide(true);
            Shade();
        }

        bool LayoutSide(bool compact)
        {
            int x = S(20), sw = side.Width, cw = sw - S(40), y = S(18);
            hAudio.SetBounds(x, y, cw, S(26)); y += S(34);
            int mw = muteGame.Visible ? S(34) : 0;
            lGame.SetBounds(x, y, S(90), S(30)); gameVol.SetBounds(x + S(92), y, cw - S(92) - mw, S(30)); muteGame.SetBounds(x + cw - S(30), y, S(30), S(30)); y += S(36);
            if (micVol.Visible) { lMic.SetBounds(x, y, S(90), S(30)); micVol.SetBounds(x + S(92), y, cw - S(92) - mw, S(30)); muteMic.SetBounds(x + cw - S(30), y, S(30), S(30)); y += S(36); }
            audioNote.Visible = !compact;
            if (!compact) { audioNote.SetBounds(x, y, cw, audioNote.Fit(cw)); y = audioNote.Bottom; }
            y += S(compact ? 12 : 24);
            hExport.SetBounds(x, y, cw, S(26)); y += S(34);
            lName.SetBounds(x, y, cw, S(20)); y += S(24);
            name.SetBounds(x, y, cw, S(34)); y += S(46);
            lSize.SetBounds(x, y, cw, S(20)); y += S(24);
            size.SetBounds(x, y, cw, S(34)); y += S(40);
            advanced.Icon = showAdvanced ? Glyph.Up : Glyph.Down;
            advanced.Width = advanced.PreferredWidth(); advanced.SetBounds(x - S(10), y, advanced.Width, S(28)); y += S(32);
            lVertical.Visible = vertical.Visible = showAdvanced; lPos.Visible = cropPos.Visible = showAdvanced && vertical.On;
            if (showAdvanced)
            {
                    lVertical.SetBounds(x, y, cw - S(60), S(30)); vertical.SetBounds(x + cw - S(50), y, S(50), S(30)); y += S(36);
                    if (cropPos.Visible) { lPos.SetBounds(x, y, S(90), S(30)); cropPos.SetBounds(x + S(92), y, cw - S(92), S(30)); y += S(36); }
            }
            y += S(4);
            sizeNote.Visible = !compact;
            if (!compact) { sizeNote.SetBounds(x, y, cw, sizeNote.Fit(cw)); y = sizeNote.Bottom; }
            y += S(compact ? 8 : 18);
            export.SetBounds(x, y, cw, S(40)); y += S(50);
            status.SetBounds(x, y, cw, Math.Max(S(20), status.Fit(cw))); y = status.Bottom + S(10);
            copy.SetBounds(x, y, (cw - S(8)) / 2, S(34)); show.SetBounds(copy.Right + S(8), y, cw - copy.Width - S(8), S(34));
            return (copy.Visible ? copy.Bottom : export.Bottom) <= side.Height - S(12);
        }

        double shownAspect;
        void Tick()
        {
            double va = Video.Aspect; if (va > 0 && Math.Abs(va - shownAspect) > 0.01) Relayout();
            Video.Poll();
            double len = Video.Length;
            if (len > 0 && Math.Abs(len - line.Length) > 0.05 && line.Out >= line.Length - 0.01) { line.Length = len; line.Out = len; RangeChanged(); }
            if (!Program.Demo) line.Position = Video.Position;
            if (Video.Playing && Video.Position >= line.Out) { Video.Pause(); Video.Position = line.In; }
            line.Invalidate();
            play.Icon = Video.Playing ? Glyph.Pause : Glyph.Play; play.Invalidate();
            if (exporting) { status.Text = "Exporting\u2026 " + (int)(exportProgress * 100) + "%"; status.Invalidate(); }
        }

#if DEV
        public void ShotVertical() { vertical.Set(true); cropPos.Value = 30; UpdateNotes(); Shade(); }
#endif
        public override void Entered() { timer.Start(); }
        public override void Leaving() { timer.Stop(); Video.Pause(); }

        void Export()
        {
            if (exporting || Clip == null) return;
            if (!FFmpeg.Ready) { Main.Toast.Show(Glyph.Warning, "Finish the FFmpeg setup first.", null, null, 4000); return; }
            Video.Pause();
            string src = Clip.Path, game = Clip.Game;
            bool vert = vertical.On; double pos = cropPos.Value / 100.0; int vk = VerticalKbps();
            string file = name.Box.Text.Length > 0 ? name.Box.Text : Clip.Title + " (trim)";
            if (vert && file.IndexOf("vertical", StringComparison.OrdinalIgnoreCase) < 0) file += " (vertical)";
            string outPath = Library.UniquePath(Path.Combine(Path.GetDirectoryName(src), Library.SafeName(file) + ".mp4"));
            double a = line.In, b = line.Out;
            int target = Sizes[Math.Max(0, size.Index)];
            if (target == 0 && !vert && keyframes.Count > 0) { double k = 0; foreach (double t in keyframes) if (t <= a + 0.02) k = t; a = k; }
            double gv = gameVol.Value / 100.0, mv = micVol.Value / 100.0;
            List<string> tr = tracks; string enc = EncoderName();
            exporting = true; exportProgress = 0; export.Enabled = false; copy.Visible = show.Visible = false; status.Fore = C.Sub; Relayout();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null; long bytes = 0;
                try { err = RunExport(src, outPath, a, b, target, gv, mv, tr, enc, vert, pos, vk); if (err == null) bytes = new FileInfo(outPath).Length; }
                catch (Exception ex) { err = ex.Message; Log.Error("Export", ex); }
                Post(delegate
                {
                    exporting = false; export.Enabled = true;
                    if (err == null)
                    {
                        lastExport = outPath;
                        status.Fore = C.Good; status.Text = "Saved \"" + Path.GetFileName(outPath) + "\" (" + Library.NiceSize(bytes) + ")";
                        copy.Visible = show.Visible = true;
                        Library.Add(outPath, game, b - a);
                        Main.Toast.Show(Glyph.Check, "Exported " + Library.NiceSize(bytes), "Copy file", CopyExport, 6000);
                    }
                    else { status.Fore = C.Warn; status.Text = err; }
                    Relayout(); status.Invalidate();
                });
            });
        }

#if DEV
        public string TestExport(string src, string outPath, double a, double b, int targetMB, double gv, double mv)
        {
            return RunExport(src, outPath, a, b, targetMB, gv, mv, FFmpeg.AudioTracks(src), EncoderName(), false, 0.5, 0);
        }
        public string TestExportVertical(string src, string outPath, double a, double b, int targetMB, double pos)
        {
            return RunExport(src, outPath, a, b, targetMB, 1, 1, FFmpeg.AudioTracks(src), EncoderName(), true, pos, 8000);
        }
#endif

        string RunExport(string src, string outPath, double a, double b, int targetMB, double gv, double mv, List<string> tr, string enc, bool vert, double pos, int vertKbps)
        {
            double dur = b - a;
            bool sep = tr.Count >= 3;
            string part = outPath + ".part";
            Action<string> progress = delegate (string l)
            {
                if (l.StartsWith("out_time_us=")) { long us; if (long.TryParse(l.Substring(12), out us)) exportProgress = Math.Max(0, Math.Min(1, us / 1e6 / dur)); }
            };
            string input = "-y -hide_banner -loglevel error -nostats -progress pipe:1 -ss " + FFmpeg.N(a) + " -i " + FFmpeg.Q(src) + " -t " + FFmpeg.N(dur);
            string mix = sep ? "[0:a:1]volume=" + FFmpeg.N(gv) + "[g];[0:a:2]volume=" + FFmpeg.N(mv) + "[m];[g][m]amix=inputs=2:duration=longest:normalize=0[mix]"
                             : tr.Count > 0 ? "[0:a:0]volume=" + FFmpeg.N(gv) + "[mix]" : "";
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
                if (sep) args += " -map 0:a:1 -map 0:a:2 -metadata:s:a:0 handler_name=\"Game + Mic\" -metadata:s:a:1 handler_name=Game -metadata:s:a:2 handler_name=Mic -disposition:a:1 0 -disposition:a:2 0";
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
