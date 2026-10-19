#if DEV
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Clutch
{
    static class Branding
    {
        public static void Make(string src, string folder)
        {
            using (Bitmap s = new Bitmap(src))
            {
                Rectangle b = Bounds(s);
                int side = (int)(Math.Max(b.Width, b.Height) * 1.08);
                using (Bitmap sq = new Bitmap(side, side, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(sq))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(s, new Rectangle((side - b.Width) / 2, (side - b.Height) / 2, b.Width, b.Height), b, GraphicsUnit.Pixel);
                    }
                    using (Bitmap logo = Scale(sq, 256)) logo.Save(Path.Combine(folder, "logo.png"), ImageFormat.Png);
                    using (Bitmap tile = Tile(sq, 512)) tile.Save(Path.Combine(folder, "icon-preview.png"), ImageFormat.Png);
                    WriteIcon(sq, Path.Combine(folder, "icon.ico"));
                }
            }
        }

        static Rectangle Bounds(Bitmap s)
        {
            int minX = s.Width, minY = s.Height, maxX = 0, maxY = 0;
            BitmapData d = s.LockBits(new Rectangle(0, 0, s.Width, s.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] px = new byte[d.Stride * s.Height];
            System.Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length);
            s.UnlockBits(d);
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                    if (px[y * d.Stride + x * 4 + 3] > 12) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
            return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }

        static Bitmap Scale(Bitmap src, int n)
        {
            Bitmap b = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, n, n);
            }
            return b;
        }

        public static Bitmap Tile(Bitmap logo, int n)
        {
            Bitmap b = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                RectangleF r = new RectangleF(n * 0.03f, n * 0.03f, n * 0.94f, n * 0.94f);
                using (GraphicsPath p = Ui.Round(r, n * 0.22f))
                {
                    using (LinearGradientBrush lg = new LinearGradientBrush(r, Color.FromArgb(34, 26, 62), Color.FromArgb(14, 12, 26), 60f)) g.FillPath(lg, p);
                    using (Pen pen = new Pen(Color.FromArgb(90, 150, 90, 255), Math.Max(1f, n / 128f))) g.DrawPath(pen, p);
                }
                float ls = n * 0.68f;
                g.DrawImage(logo, (n - ls) / 2, (n - ls) / 2, ls, ls);
            }
            return b;
        }

        static void WriteIcon(Bitmap logo, string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            List<byte[]> images = new List<byte[]>();
            foreach (int n in sizes)
                using (Bitmap bmp = Scale(logo, n))
                using (MemoryStream ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); images.Add(ms.ToArray()); }
            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte s = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(s); w.Write(s); w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length); w.Write(offset); offset += images[i].Length;
                }
                foreach (byte[] img in images) w.Write(img);
            }
        }
    }
}

#endif
