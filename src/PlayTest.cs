#if DEV
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Clutch
{
    static class PlayTest
    {
        public static void Run(string clip, string png, bool hiddenFirst)
        {
            Form f = new Form(); f.Text = "Clutch play test"; f.TopMost = true; f.StartPosition = FormStartPosition.CenterScreen;
            f.ClientSize = new Size(960, 540); f.BackColor = Color.Black;
            Panel host = new Panel(); host.Dock = DockStyle.Fill; f.Controls.Add(host);
            VideoView v = new VideoView(); v.Dock = DockStyle.Fill; host.Controls.Add(v);
            if (hiddenFirst) host.Visible = false;
            string log = Path.ChangeExtension(png, ".txt");
            f.Shown += delegate
            {
                v.Open(clip, true);
                if (hiddenFirst) host.Visible = true;
                Timer t = new Timer(); t.Interval = 4000;
                t.Tick += delegate
                {
                    t.Stop();
                    string info = "playing=" + v.Playing + " pos=" + v.Position.ToString("0.00") + " len=" + v.Length.ToString("0.00") + " " + v.Diagnostics();
                    File.WriteAllText(log, info);
                    Rectangle r = f.RectangleToScreen(f.ClientRectangle);
                    using (Bitmap b = new Bitmap(r.Width, r.Height))
                    {
                        using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                        b.Save(png, ImageFormat.Png);
                    }
                    v.Close(); f.Close();
                };
                t.Start();
            };
            Application.Run(f);
        }
    }
}

namespace Clutch
{
    static class ViewTest
    {
        public static string File_, Png;
        public static void Start(MainForm f)
        {
            f.TopMost = true;
            Clip c = new Clip(); c.Path = File_; c.Duration = 6; c.Created = DateTime.Now;
            f.OpenClip(c);
            Timer t = new Timer(); t.Interval = 3500; int step = 0;
            t.Tick += delegate
            {
                string log = Png.Replace(".png", ".txt");
                if (step == 3) { System.IO.File.AppendAllText(log, "back on clips: memory " + (System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 >> 20) + " MB" + Environment.NewLine); t.Stop(); f.Close(); return; }
                VideoView v = step == 1 ? f.Trim.Video : f.Viewer.Video;
                string tag = step == 0 ? "viewer" : step == 1 ? "trim" : "viewer-again";
                Rectangle r = v.RectangleToScreen(v.ClientRectangle);
                using (Bitmap b = new Bitmap(r.Width, r.Height))
                {
                    using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    b.Save(Png.Replace(".png", "-" + tag + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                System.IO.File.AppendAllText(log, tag + ": playing=" + v.Playing + " pos=" + v.Position.ToString("0.00") + " memory " + (System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 >> 20) + " MB " + v.Diagnostics() + Environment.NewLine);
                step++;
                if (step == 1) f.TrimClip(c);
                else if (step == 2) f.Go(f.Viewer);
                else f.Go(f.Clips);
            };
            t.Start();
        }
    }
}

#endif
