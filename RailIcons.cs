using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Clutch
{
    static class RailIcons
    {
        public static void Draw(Graphics g, string kind, RectangleF r, Color ink, Color hole)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height), x = r.X + (r.Width - s) / 2, y = r.Y + (r.Height - s) / 2;
            Func<float, float, PointF> P = delegate (float u, float v) { return new PointF(x + u * s, y + v * s); };
            CompositingMode before = g.CompositingMode;
            Action holes = delegate { if (hole.A == 0) g.CompositingMode = CompositingMode.SourceCopy; };
            using (SolidBrush b = new SolidBrush(ink)) using (SolidBrush h = new SolidBrush(hole))
            switch (kind)
            {
                case "clips":
                    using (GraphicsPath p = Ui.Round(new RectangleF(x + 0.06f * s, y + 0.16f * s, 0.88f * s, 0.68f * s), 0.18f * s)) g.FillPath(b, p);
                    holes();
                    using (GraphicsPath t = new GraphicsPath())
                    {
                        t.AddPolygon(new[] { P(0.41f, 0.34f), P(0.41f, 0.66f), P(0.67f, 0.5f) });
                        using (Pen round = new Pen(hole, 0.07f * s)) { round.LineJoin = LineJoin.Round; g.DrawPath(round, t); }
                        g.FillPath(h, t);
                    }
                    break;

                case "star":
                    using (GraphicsPath st = new GraphicsPath())
                    {
                        PointF[] pts = new PointF[10];
                        for (int i = 0; i < 10; i++)
                        {
                            double a = -Math.PI / 2 + i * Math.PI / 5; float rad = i % 2 == 0 ? 0.44f : 0.2f;
                            pts[i] = P(0.5f + (float)(Math.Cos(a) * rad), 0.53f + (float)(Math.Sin(a) * rad));
                        }
                        st.AddPolygon(pts);
                        g.FillPath(b, st);
                        using (Pen round = new Pen(ink, 0.09f * s)) { round.LineJoin = LineJoin.Round; g.DrawPath(round, st); }
                    }
                    break;

                case "star-line":
                    using (GraphicsPath st = new GraphicsPath())
                    {
                        PointF[] pts = new PointF[10];
                        for (int i = 0; i < 10; i++)
                        {
                            double a = -Math.PI / 2 + i * Math.PI / 5; float rad = i % 2 == 0 ? 0.42f : 0.19f;
                            pts[i] = P(0.5f + (float)(Math.Cos(a) * rad), 0.53f + (float)(Math.Sin(a) * rad));
                        }
                        st.AddPolygon(pts);
                        using (Pen line = new Pen(ink, 0.085f * s)) { line.LineJoin = LineJoin.Round; g.DrawPath(line, st); }
                    }
                    break;

                case "games":
                    using (GraphicsPath pad = new GraphicsPath())
                    {
                        pad.FillMode = FillMode.Winding;
                        pad.AddPath(Ui.Round(new RectangleF(x + 0.05f * s, y + 0.24f * s, 0.9f * s, 0.4f * s), 0.2f * s), false);
                        pad.AddEllipse(x + 0.03f * s, y + 0.34f * s, 0.36f * s, 0.46f * s);
                        pad.AddEllipse(x + 0.61f * s, y + 0.34f * s, 0.36f * s, 0.46f * s);
                        g.FillPath(b, pad);
                    }
                    holes();
                    float c = 0.06f * s;
                    g.FillRectangle(h, x + 0.17f * s, y + 0.44f * s - c / 2, 0.2f * s, c);
                    g.FillRectangle(h, x + 0.27f * s - c / 2, y + 0.34f * s, c, 0.2f * s);
                    g.FillEllipse(h, x + 0.64f * s, y + 0.34f * s, 0.09f * s, 0.09f * s);
                    g.FillEllipse(h, x + 0.73f * s, y + 0.45f * s, 0.09f * s, 0.09f * s);
                    break;

                case "settings":
                    using (GraphicsPath gear = new GraphicsPath())
                    {
                        gear.FillMode = FillMode.Winding;
                        gear.AddEllipse(x + 0.17f * s, y + 0.17f * s, 0.66f * s, 0.66f * s);
                        for (int i = 0; i < 8; i++)
                            using (GraphicsPath tooth = Ui.Round(new RectangleF(x + 0.41f * s, y + 0.04f * s, 0.18f * s, 0.24f * s), 0.05f * s))
                            using (Matrix m = new Matrix())
                            {
                                m.RotateAt(i * 45, P(0.5f, 0.5f)); tooth.Transform(m);
                                gear.AddPath(tooth, false);
                            }
                        g.FillPath(b, gear);
                    }
                    holes();
                    g.FillEllipse(h, x + 0.35f * s, y + 0.35f * s, 0.3f * s, 0.3f * s);
                    break;

                case "discord":
                    using (SolidBrush db = new SolidBrush(Color.FromArgb(88, 101, 242)))
                    using (GraphicsPath d = new GraphicsPath())
                    {
                        d.AddBezier(P(0.27f, 0.17f), P(0.35f, 0.15f), P(0.40f, 0.19f), P(0.42f, 0.23f));
                        d.AddBezier(P(0.42f, 0.23f), P(0.47f, 0.22f), P(0.53f, 0.22f), P(0.58f, 0.23f));
                        d.AddBezier(P(0.58f, 0.23f), P(0.60f, 0.19f), P(0.65f, 0.15f), P(0.73f, 0.17f));
                        d.AddBezier(P(0.73f, 0.17f), P(0.88f, 0.25f), P(0.97f, 0.48f), P(0.95f, 0.70f));
                        d.AddBezier(P(0.95f, 0.70f), P(0.89f, 0.77f), P(0.82f, 0.81f), P(0.75f, 0.83f));
                        d.AddBezier(P(0.75f, 0.83f), P(0.72f, 0.79f), P(0.70f, 0.76f), P(0.68f, 0.73f));
                        d.AddBezier(P(0.68f, 0.73f), P(0.56f, 0.77f), P(0.44f, 0.77f), P(0.32f, 0.73f));
                        d.AddBezier(P(0.32f, 0.73f), P(0.30f, 0.76f), P(0.28f, 0.79f), P(0.25f, 0.83f));
                        d.AddBezier(P(0.25f, 0.83f), P(0.18f, 0.81f), P(0.11f, 0.77f), P(0.05f, 0.70f));
                        d.AddBezier(P(0.05f, 0.70f), P(0.03f, 0.48f), P(0.12f, 0.25f), P(0.27f, 0.17f));
                        d.CloseFigure();
                        g.FillPath(db, d);
                    }
                    holes();
                    g.FillEllipse(h, x + 0.285f * s, y + 0.43f * s, 0.13f * s, 0.15f * s);
                    g.FillEllipse(h, x + 0.585f * s, y + 0.43f * s, 0.13f * s, 0.15f * s);
                    break;
            }
            g.CompositingMode = before;
        }

        static readonly System.Collections.Generic.Dictionary<string, Bitmap> cache = new System.Collections.Generic.Dictionary<string, Bitmap>();
        public static Bitmap Cached(string kind, int size, Color ink)
        {
            string key = kind + "|" + size + "|" + ink.ToArgb();
            Bitmap bmp;
            if (cache.TryGetValue(key, out bmp)) return bmp;
            if (cache.Count > 96) { foreach (Bitmap old in cache.Values) old.Dispose(); cache.Clear(); }
            bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bmp)) { g.PixelOffsetMode = PixelOffsetMode.HighQuality; Draw(g, kind, new RectangleF(0, 0, size, size), ink, Color.Transparent); }
            cache[key] = bmp;
            return bmp;
        }
    }
}
