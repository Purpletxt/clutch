using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Clutch
{
    class WmpHost : AxHost
    {
        public WmpHost() : base("6bf52a52-394a-11d3-b153-00c04f79faa6") { }
        public dynamic P { get { return GetOcx(); } }
    }

    class VideoView : Panel
    {
        WmpHost host; bool failed; string path = "";
        readonly Lbl fallback;
        public event Action Ended;
        int lastState;

        public VideoView()
        {
            BackColor = Color.Black;
            fallback = new Lbl("", 10, 0, C.Sub); fallback.Center = true; fallback.Dock = DockStyle.Fill; fallback.Visible = false; fallback.BackColor = Color.Black;
            Controls.Add(fallback);
        }

        bool Ensure()
        {
            if (host != null) return true;
            if (failed || Program.ShotPage != null) return false;
            try
            {
                host = new WmpHost();
                ((System.ComponentModel.ISupportInitialize)host).BeginInit();
                host.Dock = DockStyle.Fill;
                Controls.Add(host);
                ((System.ComponentModel.ISupportInitialize)host).EndInit();
                dynamic p = host.P;
                p.uiMode = "none"; p.stretchToFit = true; p.enableContextMenu = false;
                p.settings.autoStart = false; p.settings.volume = 100;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Video player", ex); failed = true;
                if (host != null) { try { Controls.Remove(host); host.Dispose(); } catch { } host = null; }
                fallback.Text = "Windows Media Player isn't available, so clips open in your default video app.";
                fallback.Visible = true;
                return false;
            }
        }

        public bool Available { get { return !failed && Program.ShotPage == null; } }

        string pending; bool pendingPlay, pauseWhenPlaying; double pendingPos;

        public void Open(string file, bool autoplay)
        {
            path = file; pending = file; pendingPlay = autoplay; pendingPos = 0;
            if (Visible && IsHandleCreated) Load();
        }

        void Load()
        {
            if (pending == null) return;
            string file = pending; pending = null;
            if (!Ensure()) return;
            try
            {
                dynamic p = host.P; p.URL = file;
                if (pendingPos > 0) p.controls.currentPosition = pendingPos;
                p.controls.play();
                pauseWhenPlaying = !pendingPlay;
            }
            catch (Exception ex) { Log.Error("Video open", ex); }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { if (pending != null && IsHandleCreated) BeginInvoke((MethodInvoker)Load); }
            else if (host != null)
            {
                if (path.Length > 0 && pending == null) { pendingPos = Position; pendingPlay = false; pending = path; }
                if (IsHandleCreated) BeginInvoke((MethodInvoker)Release);
            }
        }

        void Release()
        {
            if (host == null || Visible) return;
            try { dynamic p = host.P; p.controls.stop(); p.close(); } catch { }
            try { Controls.Remove(host); host.Dispose(); } catch { }
            host = null;
        }

        public void Close()
        {
            path = ""; pending = null; pauseWhenPlaying = false;
            if (host == null) return;
            try { dynamic p = host.P; p.controls.stop(); p.close(); } catch { }
        }

        public bool Playing { get { try { return host != null && (int)host.P.playState == 3; } catch { return false; } } }

        public void Play() { try { if (host != null) host.P.controls.play(); } catch { } }
        public void Pause() { try { if (host != null) host.P.controls.pause(); } catch { } }
        public void TogglePlay() { if (Playing) Pause(); else Play(); }

        public void FullScreen() { try { if (host == null) return; if (!Playing) Play(); host.P.fullScreen = true; } catch { } }

        public double Position
        {
            get { try { return host == null || pending != null ? pendingPos : (double)host.P.controls.currentPosition; } catch { return 0; } }
            set { try { if (host != null) host.P.controls.currentPosition = Math.Max(0, value); } catch { } }
        }

        public double Length
        {
            get { try { return host == null || host.P.currentMedia == null ? 0 : (double)host.P.currentMedia.duration; } catch { return 0; } }
        }

        public int Volume
        {
            get { try { return host == null ? 100 : (int)host.P.settings.volume; } catch { return 100; } }
            set { try { if (host != null) host.P.settings.volume = value; } catch { } }
        }

        public void Poll()
        {
            int st = 0; try { st = host == null ? 0 : (int)host.P.playState; } catch { }
            if (pauseWhenPlaying && st == 3) { pauseWhenPlaying = false; Pause(); }
            if (st == 8 && lastState != 8 && Ended != null) Ended();
            lastState = st;
        }

        public double Aspect
        {
            get
            {
                try { if (host == null || pending != null) return 0; dynamic m = host.P.currentMedia; if (m == null) return 0; int w = m.imageSourceWidth, h = m.imageSourceHeight; return w > 0 && h > 0 ? (double)w / h : 0; }
                catch { return 0; }
            }
        }

        public static double DefaultAspect() { Rectangle b = Recorder.MonitorBounds(Settings.Cur.Monitor); return b.Height > 0 ? (double)b.Width / b.Height : 16 / 9.0; }

        public string Diagnostics()
        {
            try { dynamic p = host.P; return "state=" + (int)p.playState + " size=" + (int)p.currentMedia.imageSourceWidth + "x" + (int)p.currentMedia.imageSourceHeight + " status=" + (string)p.status + " version=" + (string)p.versionInfo; }
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
        int drag;

        public Timeline() { Cursor = Cursors.Hand; }

        RectangleF Track { get { return TrimMode ? new RectangleF(S(14), S(6), Width - S(28), Height - S(12)) : new RectangleF(S(8), Height / 2f - S(2), Width - S(16), S(4)); } }
        float X(double t) { RectangleF r = Track; return r.X + (float)(Math.Max(0, Math.Min(1, t / Math.Max(0.001, Length))) * r.Width); }
        double T(int x) { RectangleF r = Track; return Math.Max(0, Math.Min(Length, (x - r.X) / Math.Max(1, r.Width) * Length)); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            drag = 1;
            if (TrimMode)
            {
                if (Math.Abs(e.X - X(In)) < S(12)) drag = 2;
                else if (Math.Abs(e.X - X(Out)) < S(12)) drag = 3;
            }
            Capture = true; MoveTo(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag != 0) MoveTo(e.X);
            else if (TrimMode) Cursor = Math.Abs(e.X - X(In)) < S(12) || Math.Abs(e.X - X(Out)) < S(12) ? Cursors.SizeWE : Cursors.Hand;
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = 0; Capture = false; }

        void MoveTo(int x)
        {
            double t = T(x), minLen = Math.Min(0.5, Length / 4);
            if (drag == 1) { Position = t; if (Seek != null) Seek(t); }
            else if (drag == 2) { In = Math.Min(t, Out - minLen); In = Math.Max(0, In); Position = In; if (RangeChanged != null) RangeChanged(); if (Seek != null) Seek(In); }
            else if (drag == 3) { Out = Math.Max(t, In + minLen); Out = Math.Min(Length, Out); Position = Out; if (RangeChanged != null) RangeChanged(); if (Seek != null) Seek(Out); }
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
            using (GraphicsPath clip = Ui.Round(r, S(8)))
            {
                g.SetClip(clip);
                using (SolidBrush b = new SolidBrush(C.Control)) g.FillRectangle(b, r);
                if (Strip.Count > 0)
                {
                    float fw = r.Width / Strip.Count;
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    for (int i = 0; i < Strip.Count; i++)
                    {
                        Bitmap bm = Strip[i]; if (bm == null) continue;
                        float s = Math.Max(fw / bm.Width, r.Height / bm.Height), w = bm.Width * s, h = bm.Height * s;
                        RectangleF cell = new RectangleF(r.X + i * fw, r.Y, fw + 1, r.Height);
                        g.SetClip(clip); g.SetClip(cell, CombineMode.Intersect);
                        g.DrawImage(bm, cell.X + (fw - w) / 2, cell.Y + (r.Height - h) / 2, w, h);
                    }
                    g.SetClip(clip);
                }
                float xi = X(In), xo = X(Out);
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(170, 10, 10, 12)))
                {
                    g.FillRectangle(dim, r.X, r.Y, xi - r.X, r.Height);
                    g.FillRectangle(dim, xo, r.Y, r.Right - xo, r.Height);
                }
                g.ResetClip();
                RectangleF keep = new RectangleF(xi, r.Y, xo - xi, r.Height);
                using (Pen p = new Pen(C.Accent, S(2.5f))) g.DrawRectangle(p, keep.X, keep.Y + S(1), keep.Width, keep.Height - S(2));
                foreach (float hx in new[] { xi, xo })
                {
                    RectangleF hr = new RectangleF(hx - S(6), r.Y - S(2), S(12), r.Height + S(4));
                    Ui.FillRound(g, hr, S(4), C.Accent);
                    using (Pen p = new Pen(Color.FromArgb(200, 255, 255, 255), S(1.5f))) g.DrawLine(p, hx, hr.Y + hr.Height * 0.35f, hx, hr.Bottom - hr.Height * 0.35f);
                }
                float px = X(Position);
                Ui.FillRound(g, new RectangleF(px - S(1.5f), r.Y - S(5), S(3), r.Height + S(10)), S(1.5f), Color.White);
            }
        }
    }
}
