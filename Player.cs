using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Clutch
{
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant { [FieldOffset(0)] public ushort Vt; [FieldOffset(8)] public long Value; }

    [StructLayout(LayoutKind.Sequential)]
    struct MfSize { public int W, H; }

    [ComImport, Guid("A714590A-58AF-430a-85BF-44F5EC838D85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFPMediaPlayer
    {
        [PreserveSig] int Play();
        [PreserveSig] int Pause();
        [PreserveSig] int Stop();
        [PreserveSig] int FrameStep();
        [PreserveSig] int SetPosition(ref Guid type, ref PropVariant value);
        [PreserveSig] int GetPosition(ref Guid type, out PropVariant value);
        [PreserveSig] int GetDuration(ref Guid type, out PropVariant value);
        [PreserveSig] int SetRate(float rate);
        [PreserveSig] int GetRate(out float rate);
        [PreserveSig] int GetSupportedRates(bool forward, out float slowest, out float fastest);
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int CreateMediaItemFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, bool sync, IntPtr userData, out IntPtr item);
        [PreserveSig] int CreateMediaItemFromObject(IntPtr obj, bool sync, IntPtr userData, out IntPtr item);
        [PreserveSig] int SetMediaItem(IntPtr item);
        [PreserveSig] int ClearMediaItem();
        [PreserveSig] int GetMediaItem(out IntPtr item);
        [PreserveSig] int GetVolume(out float volume);
        [PreserveSig] int SetVolume(float volume);
        [PreserveSig] int GetBalance(out float balance);
        [PreserveSig] int SetBalance(float balance);
        [PreserveSig] int GetMute(out bool mute);
        [PreserveSig] int SetMute(bool mute);
        [PreserveSig] int GetNativeVideoSize(out MfSize video, out MfSize aspect);
        [PreserveSig] int GetIdealVideoSize(out MfSize min, out MfSize max);
        [PreserveSig] int SetVideoSourceRect(IntPtr rect);
        [PreserveSig] int GetVideoSourceRect(IntPtr rect);
        [PreserveSig] int SetAspectRatioMode(int mode);
        [PreserveSig] int GetAspectRatioMode(out int mode);
        [PreserveSig] int GetVideoWindow(out IntPtr hwnd);
        [PreserveSig] int UpdateVideo();
        [PreserveSig] int SetBorderColor(int color);
        [PreserveSig] int GetBorderColor(out int color);
        [PreserveSig] int InsertEffect(IntPtr effect, bool optional);
        [PreserveSig] int RemoveEffect(IntPtr effect);
        [PreserveSig] int RemoveAllEffects();
        [PreserveSig] int Shutdown();
    }

    class MfPlayer : IDisposable
    {
        [DllImport("mfplay.dll", ExactSpelling = true)]
        static extern int MFPCreateMediaPlayer([MarshalAs(UnmanagedType.LPWStr)] string url, bool startPlayback, int options, IntPtr callback, IntPtr hwnd, out IMFPMediaPlayer player);
        [DllImport("mfplat.dll", ExactSpelling = true)] static extern int MFStartup(int version, int flags);

        public const int Empty = 0, Stopped = 1, Playing = 2, Paused = 3;
        static bool started;
        IMFPMediaPlayer p;
        public string Error = "";

        public static MfPlayer Create(IntPtr hwnd, out string error)
        {
            error = "";
            try
            {
                if (!started) { int hr0 = MFStartup(0x20070, 0); if (hr0 < 0) { error = "MFStartup 0x" + hr0.ToString("X8"); return null; } started = true; }
                IMFPMediaPlayer mp;
                int hr = MFPCreateMediaPlayer(null, false, 0, IntPtr.Zero, hwnd, out mp);
                if (hr < 0 || mp == null) { error = "MFPCreateMediaPlayer 0x" + hr.ToString("X8"); return null; }
                mp.SetBorderColor(0);
                MfPlayer m = new MfPlayer(); m.p = mp; return m;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return null; }
        }

        public bool Open(string file)
        {
            IntPtr item;
            int hr = p.CreateMediaItemFromURL(file, true, IntPtr.Zero, out item);
            if (hr < 0 || item == IntPtr.Zero) { Error = "open 0x" + hr.ToString("X8"); return false; }
            try { hr = p.SetMediaItem(item); } finally { Marshal.Release(item); }
            if (hr < 0) { Error = "set 0x" + hr.ToString("X8"); return false; }
            return true;
        }

        public int State { get { int s; return p != null && p.GetState(out s) >= 0 ? s : Empty; } }
        public void Play() { if (p != null) p.Play(); }
        public void Pause() { if (p != null) p.Pause(); }
        public void Stop() { if (p != null) p.Stop(); }
        public void Update() { if (p != null) p.UpdateVideo(); }

        public double Position
        {
            get { Guid g = Guid.Empty; PropVariant v; return p != null && p.GetPosition(ref g, out v) >= 0 ? v.Value / 1e7 : 0; }
            set { if (p == null) return; Guid g = Guid.Empty; PropVariant v = new PropVariant(); v.Vt = 20; v.Value = (long)(Math.Max(0, value) * 1e7); p.SetPosition(ref g, ref v); }
        }

        public double Duration { get { Guid g = Guid.Empty; PropVariant v; return p != null && p.GetDuration(ref g, out v) >= 0 ? v.Value / 1e7 : 0; } }

        public float Volume { set { if (p != null) p.SetVolume(Math.Max(0, Math.Min(1, value))); } }

        public Size VideoSize { get { MfSize s, a; return p != null && p.GetNativeVideoSize(out s, out a) >= 0 ? new Size(s.W, s.H) : Size.Empty; } }

        public void Dispose()
        {
            IMFPMediaPlayer x = p; p = null;
            if (x == null) return;
            try { x.Stop(); x.ClearMediaItem(); x.Shutdown(); } catch { }
            try { Marshal.FinalReleaseComObject(x); } catch { }
        }
    }

    class WmpHost : AxHost
    {
        public WmpHost() : base("6bf52a52-394a-11d3-b153-00c04f79faa6") { }
        public dynamic P { get { return GetOcx(); } }
    }

    class VideoView : Panel
    {
        static bool mfBroken, wmpBroken;
        MfPlayer mf; Panel surface;
        WmpHost host;
        string path = "";
        readonly Lbl fallback;
        public event Action Ended;
        int lastState; bool ended;
        int volume = 100;
        string pending; bool pendingPlay, pauseWhenPlaying, muteUntilPaused; double pendingPos;
        Form full;

        public VideoView()
        {
#if DEV
            string force = Environment.GetEnvironmentVariable("CLUTCH_PLAYER");
            if (force == "wmp") mfBroken = true;
            if (force == "none") mfBroken = wmpBroken = true;
#endif
            BackColor = Color.Black;
            fallback = new Lbl("", 10, 0, C.Sub); fallback.Center = true; fallback.Dock = DockStyle.Fill; fallback.Visible = false; fallback.BackColor = Color.Black;
            Controls.Add(fallback);
        }

        bool Ensure()
        {
            if (mf != null || host != null) return true;
            if (Program.ShotPage != null) return false;
            if (!mfBroken)
            {
                surface = new Panel(); surface.BackColor = Color.Black; surface.Dock = DockStyle.Fill;
                Controls.Add(surface); surface.CreateControl();
                surface.Resize += delegate { if (mf != null) mf.Update(); };
                surface.Paint += delegate { if (mf != null) mf.Update(); };
                surface.DoubleClick += delegate { FullScreen(); };
                string err;
                mf = MfPlayer.Create(surface.Handle, out err);
                if (mf != null) return true;
                Log.Info("Video player", "Media Foundation isn't available (" + err + "), trying Windows Media Player");
                mfBroken = true; Controls.Remove(surface); surface.Dispose(); surface = null;
            }
            if (!wmpBroken)
            {
                try
                {
                    host = new WmpHost();
                    ((System.ComponentModel.ISupportInitialize)host).BeginInit();
                    host.Dock = DockStyle.Fill;
                    Controls.Add(host);
                    ((System.ComponentModel.ISupportInitialize)host).EndInit();
                    dynamic p = host.P;
                    p.uiMode = "none"; p.stretchToFit = true; p.enableContextMenu = false;
                    p.settings.autoStart = false; p.settings.volume = volume;
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("Video player", ex); wmpBroken = true;
                    if (host != null) { try { Controls.Remove(host); host.Dispose(); } catch { } host = null; }
                }
            }
            fallback.Text = "Clutch can't play video on this PC, so clips open in your default video app.";
            fallback.Visible = true;
            return false;
        }

        public bool Available { get { return !(mfBroken && wmpBroken) && Program.ShotPage == null; } }

        public void Open(string file, bool autoplay)
        {
            path = file; pending = file; pendingPlay = autoplay; pendingPos = 0;
            if (Visible && IsHandleCreated) Load();
        }

        void Load()
        {
            if (pending == null) return;
            string file = pending; pending = null; ended = false;
            if (!Ensure()) return;
            try
            {
                if (mf != null)
                {
                    if (!mf.Open(file))
                    {
                        Log.Info("Video open", "Media Foundation couldn't open the clip (" + mf.Error + "), trying Windows Media Player");
                        ReleaseNow(); mfBroken = true; pending = file; Load(); return;
                    }
                    muteUntilPaused = !pendingPlay;
                    mf.Volume = muteUntilPaused ? 0 : volume / 100f;
                    applyPos = pendingPos; startWhenReady = true;
                    mf.Play();
                    pauseWhenPlaying = !pendingPlay;
                    return;
                }
                dynamic p = host.P; p.URL = file;
                if (pendingPos > 0) p.controls.currentPosition = pendingPos;
                p.controls.play();
                pauseWhenPlaying = !pendingPlay;
            }
            catch (Exception ex) { Log.Error("Video open", ex); }
        }
        double applyPos; bool startWhenReady;

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { if (pending != null && IsHandleCreated) BeginInvoke((MethodInvoker)Load); }
            else if (mf != null || host != null)
            {
                if (path.Length > 0 && pending == null) { pendingPos = Position; pendingPlay = false; pending = path; }
                if (IsHandleCreated) BeginInvoke((MethodInvoker)Release);
            }
        }

        void Release() { if (!Visible) ReleaseNow(); }

        void ReleaseNow()
        {
            if (mf != null) { mf.Dispose(); mf = null; }
            if (surface != null) { try { Controls.Remove(surface); surface.Dispose(); } catch { } surface = null; }
            if (host != null)
            {
                try { dynamic p = host.P; p.controls.stop(); p.close(); } catch { }
                try { Controls.Remove(host); host.Dispose(); } catch { }
                host = null;
            }
        }

        public void Close()
        {
            path = ""; pending = null; pauseWhenPlaying = false;
            ExitFull();
            if (mf != null) { mf.Stop(); return; }
            if (host == null) return;
            try { dynamic p = host.P; p.controls.stop(); p.close(); } catch { }
        }

        public bool Playing
        {
            get
            {
                if (mf != null) return mf.State == MfPlayer.Playing && !pauseWhenPlaying;
                try { return host != null && (int)host.P.playState == 3; } catch { return false; }
            }
        }

        public void Play()
        {
            if (mf != null) { if (ended || Length > 0 && Position >= Length - 0.05) { mf.Position = 0; ended = false; } mf.Volume = volume / 100f; muteUntilPaused = false; pauseWhenPlaying = false; mf.Play(); return; }
            try { if (host != null) host.P.controls.play(); } catch { }
        }

        public void Pause()
        {
            if (mf != null) { mf.Pause(); return; }
            try { if (host != null) host.P.controls.pause(); } catch { }
        }

        public void TogglePlay() { if (Playing) Pause(); else Play(); }

        public void FullScreen()
        {
            if (mf != null)
            {
                if (full != null) { ExitFull(); return; }
                full = new Form(); full.FormBorderStyle = FormBorderStyle.None; full.BackColor = Color.Black; full.ShowInTaskbar = false; full.KeyPreview = true;
                full.StartPosition = FormStartPosition.Manual; full.Bounds = Screen.FromControl(this).Bounds; full.TopMost = true;
                full.KeyDown += delegate (object s, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F) ExitFull();
                    else if (e.KeyCode == Keys.Space) TogglePlay();
                    else if (e.KeyCode == Keys.Left) Position = Position - 5;
                    else if (e.KeyCode == Keys.Right) Position = Position + 5;
                };
                full.FormClosed += delegate { full = null; };
                Controls.Remove(surface); full.Controls.Add(surface);
                full.Show(FindForm()); full.Activate();
                if (!Playing) Play();
                mf.Update();
                return;
            }
            try { if (host == null) return; if (!Playing) Play(); host.P.fullScreen = true; } catch { }
        }

        void ExitFull()
        {
            Form f = full; full = null;
            if (f == null) return;
            if (surface != null && !surface.IsDisposed) { f.Controls.Remove(surface); Controls.Add(surface); surface.BringToFront(); }
            f.Close(); f.Dispose();
            if (mf != null) mf.Update();
        }

        public double Position
        {
            get
            {
                if (mf != null) return pending != null ? pendingPos : mf.Position;
                try { return host == null || pending != null ? pendingPos : (double)host.P.controls.currentPosition; } catch { return 0; }
            }
            set
            {
                if (mf != null) { ended = false; double len = Length; mf.Position = len > 0 ? Math.Min(value, len - 0.05) : value; return; }
                try { if (host != null) host.P.controls.currentPosition = Math.Max(0, value); } catch { }
            }
        }

        public double Length
        {
            get
            {
                if (mf != null) return mf.Duration;
                try { return host == null || host.P.currentMedia == null ? 0 : (double)host.P.currentMedia.duration; } catch { return 0; }
            }
        }

        public int Volume
        {
            get { return volume; }
            set
            {
                volume = Math.Max(0, Math.Min(100, value));
                if (mf != null) { if (!muteUntilPaused) mf.Volume = volume / 100f; return; }
                try { if (host != null) host.P.settings.volume = volume; } catch { }
            }
        }

        public void Poll()
        {
            if (mf != null)
            {
                int s = mf.State;
                if (startWhenReady) { if (s == MfPlayer.Playing) startWhenReady = false; else if (s == MfPlayer.Stopped) mf.Play(); }
                if (s == MfPlayer.Playing && applyPos > 0) { mf.Position = applyPos; applyPos = 0; }
                if (pauseWhenPlaying && s == MfPlayer.Playing && applyPos == 0) { pauseWhenPlaying = false; mf.Pause(); }
                if (muteUntilPaused && s == MfPlayer.Paused) { muteUntilPaused = false; mf.Volume = volume / 100f; }
                double len = mf.Duration;
                if (!ended && len > 0 && s != MfPlayer.Playing && lastState == MfPlayer.Playing && mf.Position >= len - 0.25) { ended = true; if (Ended != null) Ended(); }
                lastState = s;
                return;
            }
            int st = 0; try { st = host == null ? 0 : (int)host.P.playState; } catch { }
            if (pauseWhenPlaying && st == 3) { pauseWhenPlaying = false; Pause(); }
            if (st == 8 && lastState != 8 && Ended != null) Ended();
            lastState = st;
        }

        public double Aspect
        {
            get
            {
                if (mf != null) { if (pending != null) return 0; Size z = mf.VideoSize; return z.Width > 0 && z.Height > 0 ? (double)z.Width / z.Height : 0; }
                try { if (host == null || pending != null) return 0; dynamic m = host.P.currentMedia; if (m == null) return 0; int w = m.imageSourceWidth, h = m.imageSourceHeight; return w > 0 && h > 0 ? (double)w / h : 0; }
                catch { return 0; }
            }
        }

        public static double DefaultAspect() { Rectangle b = Recorder.MonitorBounds(Settings.Cur.Monitor); return b.Height > 0 ? (double)b.Width / b.Height : 16 / 9.0; }

        public string Diagnostics()
        {
            if (mf != null) { Size z = mf.VideoSize; return "engine=MediaFoundation state=" + mf.State + " size=" + z.Width + "x" + z.Height + " len=" + mf.Duration.ToString("0.00"); }
            try { dynamic p = host.P; return "engine=WMP state=" + (int)p.playState + " size=" + (int)p.currentMedia.imageSourceWidth + "x" + (int)p.currentMedia.imageSourceHeight; }
            catch (Exception ex) { return "diag failed: " + ex.Message; }
        }

        public void OpenExternally() { if (path.Length > 0) try { System.Diagnostics.Process.Start(path); } catch { } }
    }

    class Timeline : Ctl
    {
        public double Length = 1, Position, In, Out = 1;
        public bool TrimMode;
        public List<Bitmap> Strip = new List<Bitmap>();
        public event Action<double> Seek;
        public event Action RangeChanged;
        int drag, hoverX = -1;

        public Timeline() { Cursor = Cursors.Hand; }

        int HandleW { get { return S(14); } }
        RectangleF Track { get { return TrimMode ? new RectangleF(S(18), S(28), Width - S(36), Height - S(32)) : new RectangleF(S(8), Height / 2f - S(2), Width - S(16), S(4)); } }
        float X(double t) { RectangleF r = Track; return r.X + (float)(Math.Max(0, Math.Min(1, t / Math.Max(0.001, Length))) * r.Width); }
        double T(int x) { RectangleF r = Track; return Math.Max(0, Math.Min(Length, (x - r.X) / Math.Max(1, r.Width) * Length)); }

        int HandleAt(int x)
        {
            float xi = X(In), xo = X(Out);
            if (x >= xi - HandleW - S(4) && x <= xi + S(4)) return 2;
            if (x >= xo - S(4) && x <= xo + HandleW + S(4)) return 3;
            return 0;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            drag = TrimMode ? Math.Max(1, HandleAt(e.X)) : 1;
            Capture = true; MoveTo(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag != 0) { MoveTo(e.X); return; }
            if (!TrimMode) return;
            Cursor = HandleAt(e.X) != 0 ? Cursors.SizeWE : Cursors.Hand;
            if (hoverX != e.X) { hoverX = e.X; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hoverX >= 0) { hoverX = -1; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = 0; Capture = false; Invalidate(); }

        void MoveTo(int x)
        {
            double t = T(x), minLen = Math.Min(0.5, Length / 4);
            if (drag == 1) { Position = t; if (Seek != null) Seek(t); }
            else if (drag == 2) { In = Math.Max(0, Math.Min(t, Out - minLen)); Position = In; if (RangeChanged != null) RangeChanged(); if (Seek != null) Seek(In); }
            else if (drag == 3) { Out = Math.Min(Length, Math.Max(t, In + minLen)); Position = Out; if (RangeChanged != null) RangeChanged(); if (Seek != null) Seek(Out); }
            hoverX = x;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e); RectangleF r = Track;
            if (!TrimMode)
            {
                float h = hover.Value;
                RectangleF tr = new RectangleF(r.X, r.Y - h, r.Width, r.Height + 2 * h);
                Ui.FillRound(g, tr, tr.Height / 2, C.Control);
                float px = X(Position);
                Ui.FillRound(g, new RectangleF(r.X, tr.Y, px - r.X, tr.Height), tr.Height / 2, C.Accent);
                float d = S(10) + S(4) * h;
                using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, px - d / 2, Height / 2f - d / 2, d, d);
                return;
            }
            float rad = S(10), hw = HandleW;
            DrawStrip(g, r);
            float xi = X(In), xo = X(Out);
            using (GraphicsPath clip = Ui.Round(r, rad))
            {
                Region before = g.Clip; g.SetClip(clip, CombineMode.Intersect);
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(200, 12, 11, 18)))
                {
                    g.FillRectangle(dim, r.X, r.Y, xi - r.X, r.Height);
                    g.FillRectangle(dim, xo, r.Y, r.Right - xo, r.Height);
                }
                g.Clip = before;
            }

            RectangleF outer = new RectangleF(xi - hw, r.Y - S(2), xo - xi + hw * 2, r.Height + S(4));
            RectangleF inner = new RectangleF(xi, r.Y + S(2), xo - xi, r.Height - S(4));
            Color frameC = drag >= 2 ? C.AccentHi : C.Accent;
            using (GraphicsPath fp = Ui.Round(outer, S(10)))
            using (GraphicsPath ip = Ui.Round(inner, S(4)))
            {
                fp.AddPath(ip, false); fp.FillMode = FillMode.Alternate;
                using (SolidBrush fb = new SolidBrush(frameC)) g.FillPath(fb, fp);
            }
            for (int k = 0; k < 2; k++)
            {
                float cx = k == 0 ? xi - hw / 2 : xo + hw / 2, cy = r.Y + r.Height / 2, a = S(4);
                using (Pen p = new Pen(Color.White, S(2))) { p.StartCap = p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round; float dir = k == 0 ? 1 : -1; g.DrawLines(p, new[] { new PointF(cx + dir * a / 2, cy - a), new PointF(cx - dir * a / 2, cy), new PointF(cx + dir * a / 2, cy + a) }); }
            }

            if (hoverX >= 0 && drag == 0 && hoverX >= r.X && hoverX <= r.Right)
                using (Pen hp = new Pen(Color.FromArgb(110, 255, 255, 255), 1)) g.DrawLine(hp, hoverX, r.Y, hoverX, r.Bottom);

            float ph = X(Position);
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillRectangle(sh, ph - S(2.5f), r.Y - S(4), S(5), r.Height + S(8));
            Ui.FillRound(g, new RectangleF(ph - S(1.5f), r.Y - S(4), S(3), r.Height + S(8)), S(1.5f), Color.White);

            double bt; float bx; bool accent = drag >= 2;
            if (drag == 2) { bt = In; bx = xi; }
            else if (drag == 3) { bt = Out; bx = xo; }
            else if (drag == 1) { bt = Position; bx = ph; }
            else if (hoverX >= r.X && hoverX <= r.Right) { bt = T(hoverX); bx = hoverX; }
            else { bt = Position; bx = ph; }
            Font lf = Ui.F(8.5f, 5); string s = Stamp(bt);
            int bw = Ui.Measure(s, lf).Width + S(14), bh = S(20);
            Rectangle br = new Rectangle((int)Math.Max(0, Math.Min(Width - bw, bx - bw / 2f)), 0, bw, bh);
            Ui.FillRound(g, br, bh / 2f, accent ? C.Accent : Color.White);
            Ui.TextCenter(g, s, lf, br, accent ? Color.White : Color.FromArgb(20, 18, 30));
        }

        static string Stamp(double s) { int m = (int)(s / 60); return m + ":" + (s - m * 60).ToString("00.0", System.Globalization.CultureInfo.InvariantCulture); }

        Bitmap stripCache; List<Bitmap> stripFor; Size stripSize;

        void DrawStrip(Graphics g, RectangleF r)
        {
            Size want = new Size((int)r.Width, (int)r.Height);
            if (want.Width < 2 || want.Height < 2) return;
            if (stripCache == null || stripFor != Strip || stripSize != want)
            {
                if (stripCache != null) stripCache.Dispose();
                stripFor = Strip; stripSize = want;
                stripCache = new Bitmap(want.Width, want.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (Graphics sg = Graphics.FromImage(stripCache))
                {
                    sg.SmoothingMode = SmoothingMode.AntiAlias; sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    using (GraphicsPath clip = Ui.Round(new RectangleF(0, 0, want.Width, want.Height), S(10)))
                    {
                        using (SolidBrush b = new SolidBrush(C.Control)) sg.FillPath(b, clip);
                        if (Strip.Count > 0)
                        {
                            float fw = want.Width / (float)Strip.Count;
                            for (int i = 0; i < Strip.Count; i++)
                            {
                                Bitmap bm = Strip[i]; if (bm == null) continue;
                                float s = Math.Max(fw / bm.Width, want.Height / (float)bm.Height), w = bm.Width * s, h = bm.Height * s;
                                RectangleF cell = new RectangleF(i * fw, 0, fw + 1, want.Height);
                                sg.SetClip(clip); sg.SetClip(cell, CombineMode.Intersect);
                                sg.DrawImage(bm, cell.X + (fw - w) / 2, (want.Height - h) / 2, w, h);
                            }
                        }
                    }
                }
            }
            g.DrawImageUnscaled(stripCache, (int)r.X, (int)r.Y);
        }
    }
}
