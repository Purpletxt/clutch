using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Clutch
{
    class ViewerPage : Page
    {
        public Clip Clip;
        public readonly VideoView Video = new VideoView();
        readonly Btn back = new Btn("Clips", Glyph.Back, BtnStyle.Subtle);
        readonly Btn prev = new Btn("", "\uE76B", BtnStyle.Subtle), next = new Btn("", "\uE76C", BtnStyle.Subtle);
        readonly Btn fav = new Btn("", Glyph.Star, BtnStyle.Standard), rename = new Btn("", Glyph.Rename, BtnStyle.Standard),
                     folder = new Btn("", Glyph.Folder, BtnStyle.Standard), delete = new Btn("", Glyph.Delete, BtnStyle.Standard),
                     trim = new Btn("Trim & export", Glyph.Cut, BtnStyle.Accent), play = new Btn("", Glyph.Play, BtnStyle.Subtle),
                     full = new Btn("", "\uE740", BtnStyle.Subtle), external = new Btn("Open in video app", Glyph.Share, BtnStyle.Standard);
        readonly Lbl title = new Lbl("", 16, 4, C.Text), meta = new Lbl("", 9.5f, 0, C.Sub), time = new Lbl("", 9.5f, 1, C.Sub);
        readonly Btn vol = new Btn("", Glyph.Volume, BtnStyle.Subtle); int unmuted = 100;
        readonly Slider volume = new Slider();
        readonly Timeline seek = new Timeline();
        readonly Card frame = new Card();
        readonly Timer timer = new Timer();

        public ViewerPage(MainForm m) : base(m)
        {
            back.Click += delegate { Main.Go(Main.Clips); };
            prev.Click += delegate { Step(-1); };
            next.Click += delegate { Step(1); };
            fav.Click += delegate { if (Clip != null) { Main.ToggleFavorite(Clip); Sync(); } };
            rename.Click += delegate { if (Clip != null) { Main.RenameClip(Clip); Sync(); } };
            folder.Click += delegate { if (Clip != null) Main.ShowInFolder(Clip); };
            delete.Click += delegate { DeleteAndNext(); };
            trim.Click += delegate { if (Clip != null) { Video.Pause(); Main.TrimClip(Clip); } };
            play.Click += delegate { Video.TogglePlay(); };
            full.Click += delegate { Video.FullScreen(); };
            external.Click += delegate { Video.Pause(); Video.OpenExternally(); };
            play.IconPt = 14; full.IconPt = 12; prev.IconPt = next.IconPt = 12;
            volume.Value = 100; volume.Changed += delegate { Video.Volume = volume.Value; SyncVol(); };
            vol.IconPt = 12;
            vol.Click += delegate { if (volume.Value > 0) { unmuted = volume.Value; volume.Value = 0; } else volume.Value = Math.Max(10, unmuted); volume.Invalidate(); Video.Volume = volume.Value; SyncVol(); };
            seek.Seek += delegate (double t) { Video.Position = t; };
            Video.Ended += delegate { Video.Position = 0; };
            frame.Fill = Color.Black; frame.Padding = new Padding(1);
            frame.Controls.Add(Video);
            foreach (Control c in new Control[] { back, title, meta, prev, next, fav, rename, folder, delete, trim, frame, play, time, seek, vol, volume, full, external }) Controls.Add(c);
            timer.Interval = 100; timer.Tick += delegate { Tick(); };
        }

        public void Open(Clip c)
        {
            Clip = c; Sync();
            seek.Length = Math.Max(0.1, c.Duration); seek.Position = 0;
            Video.Open(c.Path, true); Video.Volume = volume.Value;
            external.Visible = !Video.Available;
            Relayout();
        }

        void SyncVol() { string i = volume.Value == 0 ? Glyph.Mute : Glyph.Volume; if (vol.Icon != i) { vol.Icon = i; vol.Invalidate(); } }

        void Sync()
        {
            if (Clip == null) return;
            title.Text = Clip.Title;
            meta.Text = (Clip.Game.Length > 0 ? Clip.Game + "  \u00B7  " : "") + Library.NiceDate(Clip.Created) + "  \u00B7  " + Library.NiceTime(Clip.Duration) + "  \u00B7  " + Library.NiceSize(Clip.Size);
            fav.Icon = Clip.Favorite ? Glyph.StarFill : Glyph.Star; fav.Invalidate();
            prev.Enabled = Main.Clips.Neighbor(Clip, -1) != null; next.Enabled = Main.Clips.Neighbor(Clip, 1) != null;
        }

        void Step(int dir)
        {
            if (Clip == null) return;
            Clip n = Main.Clips.Neighbor(Clip, dir);
            if (n != null) Open(n);
        }

        void DeleteAndNext()
        {
            if (Clip == null) return;
            Clip gone = Clip, n = Main.Clips.Neighbor(gone, 1) ?? Main.Clips.Neighbor(gone, -1);
            Video.Close();
            Main.DeleteClips(new List<Clip> { gone });
            if (n != null) { Main.Go(this); Open(n); }
        }

        public bool Key(Keys k, bool ctrl)
        {
            switch (k)
            {
                case Keys.Space: Video.TogglePlay(); return true;
                case Keys.Left: if (ctrl) Step(-1); else Video.Position = Math.Max(0, Video.Position - 5); return true;
                case Keys.Right: if (ctrl) Step(1); else Video.Position = Math.Min(seek.Length, Video.Position + 5); return true;
                case Keys.F: Video.FullScreen(); return true;
                case Keys.Delete: DeleteAndNext(); return true;
                case Keys.Escape: Main.Go(Main.Clips); return true;
            }
            return false;
        }

        public override void Relayout()
        {
            int pad = S(28), w = ClientSize.Width, h = ClientSize.Height, bh = S(36);
            back.SetBounds(pad - S(10), S(14), S(96), S(34));
            int x = w - pad;
            trim.Height = bh; trim.Width = trim.PreferredWidth() + S(8); trim.Location = new Point(x - trim.Width, S(62)); x = trim.Left - S(10);
            foreach (Btn b in new[] { delete, folder, rename, fav }) { b.SetBounds(x - bh, S(62), bh, bh); x -= bh + S(8); }
            x -= S(10);
            foreach (Btn b in new[] { next, prev }) { b.SetBounds(x - bh, S(62), bh, bh); x -= bh + S(2); }
            title.SetBounds(pad, S(54), Math.Max(S(100), x - pad - S(10)), S(32));
            meta.SetBounds(pad, S(86), Math.Max(S(100), x - pad - S(10)), S(22));
            int top = S(122), bottom = h - S(70);
            int vw = w - pad * 2, vh = bottom - top;
            double ar = Video.Aspect > 0 ? Video.Aspect : VideoView.DefaultAspect(); shownAspect = ar;
            int fw = Math.Min(vw, (int)(vh * ar)), fh = (int)(fw / ar);
            frame.SetBounds((w - fw) / 2, top + (vh - fh) / 2, fw, fh);
            Video.SetBounds(S(4), S(4), fw - S(8), fh - S(8));
            int ty = h - S(54), right = Math.Max(frame.Right, S(400));
            play.SetBounds(frame.Left - S(4), ty, S(40), S(40));
            full.SetBounds(right - S(36), ty, S(40), S(40));
            volume.SetBounds(full.Left - S(130), ty, S(124), S(40));
            vol.SetBounds(volume.Left - S(38), ty, S(36), S(40));
            time.SetBounds(vol.Left - S(108), ty, S(104), S(40)); time.Center = true;
            seek.SetBounds(play.Right + S(8), ty, time.Left - play.Right - S(12), S(40));
            external.Height = S(34); external.Width = external.PreferredWidth() + S(8);
            external.Location = new Point(frame.Left + (frame.Width - external.Width) / 2, frame.Top + frame.Height / 2 + S(30));
            external.BringToFront();
        }

        double shownAspect;
        void Tick()
        {
            double va = Video.Aspect; if (va > 0 && Math.Abs(va - shownAspect) > 0.01) Relayout();
            Video.Poll();
            double len = Video.Length; if (len > 0) seek.Length = len;
            seek.Position = Video.Position; seek.Invalidate();
            string pi = Video.Playing ? Glyph.Pause : Glyph.Play;
            if (play.Icon != pi) { play.Icon = pi; play.Invalidate(); }
            string t = Library.NiceTime(seek.Position <= 0 ? 0.01 : seek.Position) + " / " + Library.NiceTime(seek.Length);
            if (time.Text != t) time.Text = t;
        }

        public override void Entered() { timer.Start(); Sync(); }
        public override void Leaving() { timer.Stop(); Video.Pause(); }
    }
}
