using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Clutch
{
    class Ctl : Control
    {
        protected Anim hover;
        public Ctl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            hover = new Anim(this, 0, 0.3f);
        }
        protected static int S(float v) { return Ui.S(v); }
        protected Color Bg { get { return BackColor; } }
        protected Graphics Begin(PaintEventArgs e) { Graphics g = e.Graphics; g.Clear(Bg); Ui.Prep(g); return g; }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (Enabled) hover.To(1); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover.To(0); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected MouseButtons downButton;
        protected override void OnMouseDown(MouseEventArgs e) { downButton = e.Button; base.OnMouseDown(e); }
        protected override void OnClick(EventArgs e) { if (downButton == MouseButtons.Left) base.OnClick(e); }
    }

    enum BtnStyle { Standard, Accent, Subtle, Danger }

    class Btn : Ctl
    {
        public BtnStyle Style; public string Icon = ""; public float TextPt = 10, IconPt = 11; public bool On; public string Tip;
        public bool Round = false;
        public int Weight = 1;
        readonly Anim press;
        public Btn(string text, string icon, BtnStyle style) { Text = text; Icon = icon ?? ""; Style = style; Cursor = Cursors.Hand; press = new Anim(this, 0, 0.4f); if (style == BtnStyle.Accent) Weight = 5; }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) press.To(1); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); press.To(0); }
        public void PerformClick() { downButton = MouseButtons.Left; OnClick(EventArgs.Empty); }

        public int PreferredWidth()
        {
            int w = S(28);
            if (Text.Length > 0) w += Ui.Measure(Text, Ui.F(TextPt, Weight)).Width;
            if (Icon.Length > 0) w += S(IconPt * 1.5f) + (Text.Length > 0 ? S(6) : 0);
            return Text.Length == 0 ? Height : w;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e); float h = hover.Value, p = press.Value;
            Color fill, fore = C.Text, border = Color.Empty;
            switch (Style)
            {
                case BtnStyle.Accent: fill = Ui.Mix(Ui.Mix(C.Accent, C.AccentHi, h), C.Accent, p * 0.6f); fore = C.AccentText; break;
                case BtnStyle.Subtle: fill = Ui.Mix(Bg, C.Text, (On ? 0.1f : 0) + h * 0.07f - p * 0.02f); if (!On) fore = Ui.Mix(C.Sub, C.Text, 0.6f + 0.4f * h); break;
                case BtnStyle.Danger: fill = Ui.Mix(C.Control, Color.FromArgb(120, 40, 50), h); break;
                default: fill = Ui.Mix(C.Control, C.ControlHi, h - p * 0.4f); border = Ui.Mix(C.Stroke, C.Text, 0.05f); break;
            }
            if (!Enabled) { fill = Ui.Mix(fill, Bg, 0.5f); fore = C.Faint; }
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Round ? Height / 2f : S(6);
            if (Style == BtnStyle.Accent && Enabled) Ui.FillAccent(g, r, rad, h * 0.12f - p * 0.1f, 255);
            else if (fill != Bg) Ui.FillRound(g, r, rad, fill);
            if (border != Color.Empty) Ui.StrokeRound(g, r, rad, border, 1);

            Font tf = Ui.F(TextPt, Weight);
            int tw = Text.Length > 0 ? Ui.Measure(Text, tf).Width : 0, iw = Icon.Length > 0 ? S(IconPt * 1.5f) : 0;
            int gap = tw > 0 && iw > 0 ? S(6) : 0, total = tw + iw + gap, x = (Width - total) / 2;
            if (iw > 0) { Ui.DrawIcon(g, Icon, IconPt * 0.92f, new Rectangle(x, 0, iw, Height), Style == BtnStyle.Subtle && On ? C.AccentHi : fore); x += iw + gap; }
            if (tw > 0) Ui.TextLeft(g, Text, tf, new Rectangle(x, 0, tw + S(2), Height - S(1)), fore);
        }
    }

    class Toggle : Ctl
    {
        bool on; readonly Anim knob;
        public event EventHandler Changed;
        public Toggle() { Cursor = Cursors.Hand; knob = new Anim(this, 0, 0.3f); }
        public bool On { get { return on; } set { if (on == value) return; on = value; knob.To(on ? 1 : 0); } }
        public void Set(bool v) { on = v; knob.Snap(v ? 1 : 0); }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (downButton != MouseButtons.Left || !Enabled) return;
            On = !On; if (Changed != null) Changed(this, EventArgs.Empty);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e); float k = knob.Value, h = hover.Value;
            int w = S(40), ht = S(20); RectangleF r = new RectangleF(Width - w - 0.5f, (Height - ht) / 2f, w, ht);
            Color off = Ui.Mix(C.Control, C.ControlHi, h), onC = Ui.Mix(C.Accent, C.AccentHi, h);
            if (!Enabled) { off = Ui.Mix(off, Bg, 0.5f); onC = Ui.Mix(onC, Bg, 0.6f); }
            Ui.FillRound(g, r, ht / 2f, off);
            if (Enabled) Ui.FillAccent(g, r, ht / 2f, h * 0.1f, (int)(255 * k)); else Ui.FillRound(g, r, ht / 2f, Ui.Alpha(onC, (int)(255 * k)));
            if (k < 0.99f) Ui.StrokeRound(g, r, ht / 2f, Ui.Alpha(C.Sub, (int)(255 * (1 - k))), 1);
            float d = S(12) + S(2) * h, cx = r.X + S(10) + (w - S(20)) * k;
            using (SolidBrush b = new SolidBrush(Ui.Mix(C.Sub, Color.White, k))) g.FillEllipse(b, cx - d / 2, r.Y + ht / 2f - d / 2, d, d);
        }
    }

    class Seg : Ctl
    {
        public string[] Items; int index, hot = -1; readonly Anim slide;
        public event EventHandler Changed;
        public Seg(string[] items) { Items = items; Cursor = Cursors.Hand; slide = new Anim(this, 0, 0.3f); }
        public int Index { get { return index; } set { index = Math.Max(0, Math.Min(Items.Length - 1, value)); slide.To(index); } }
        public void Set(int i) { index = Math.Max(0, Math.Min(Items.Length - 1, i)); slide.Snap(index); }
        int At(int x) { return Math.Max(0, Math.Min(Items.Length - 1, x * Items.Length / Math.Max(1, Width))); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = At(e.X); if (h != hot) { hot = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            int i = At(e.X); if (i == index) return;
            Index = i; if (Changed != null) Changed(this, EventArgs.Empty);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.FillRound(g, r, S(7), C.Control);
            float cw = (Width - S(6)) / (float)Items.Length;
            if (hot >= 0 && hot != index) Ui.FillRound(g, new RectangleF(S(3) + hot * cw, S(3), cw, Height - S(6) - 1), S(5), C.ControlHi);
            RectangleF sel = new RectangleF(S(3) + slide.Value * cw, S(3), cw, Height - S(6) - 1);
            Ui.FillAccent(g, sel, S(5), 0, 255);
            for (int i = 0; i < Items.Length; i++)
            {
                Rectangle tr = new Rectangle((int)(S(3) + i * cw), 0, (int)cw, Height - 1);
                float near = 1 - Math.Min(1, Math.Abs(slide.Value - i));
                Ui.TextCenter(g, Items[i], Ui.F(9.5f, 1), tr, Ui.Mix(C.Sub, C.AccentText, near));
            }
        }
    }

    class Drop : Ctl
    {
        public List<string> Items = new List<string>(); int index;
        public event EventHandler Changed;
        public string Placeholder = "";
        public Drop() { Cursor = Cursors.Hand; }
        public int Index { get { return index; } set { index = value; Invalidate(); } }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (downButton != MouseButtons.Left) return;
            ContextMenuStrip m = Menus.Make();
            for (int i = 0; i < Items.Count; i++)
            {
                int k = i;
                ToolStripMenuItem it = new ToolStripMenuItem(Items[i]);
                it.Checked = i == index;
                it.Click += delegate { index = k; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); };
                m.Items.Add(it);
            }
            m.MinimumSize = new Size(Width, 0);
            m.Show(this, new Point(0, Height + S(2)));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e); float h = hover.Value;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.FillRound(g, r, S(6), Ui.Mix(C.Control, C.ControlHi, h));
            Ui.StrokeRound(g, r, S(6), C.Stroke, 1);
            string t = index >= 0 && index < Items.Count ? Items[index] : Placeholder;
            Ui.TextLeft(g, t, Ui.F(9.5f, 0), new Rectangle(S(12), 0, Width - S(40), Height), C.Text);
            Ui.DrawIcon(g, "\uE70D", 8, new Rectangle(Width - S(30), 0, S(22), Height), C.Sub);
        }
    }

    class Slider : Ctl
    {
        public int Min, Max = 100, Value; bool drag;
        public event EventHandler Changed;
        public string Suffix = "%";
        public Slider() { Cursor = Cursors.Hand; }
        int TrackW { get { return Width - S(56); } }
        void SetFrom(int x)
        {
            int v = Min + (int)Math.Round((x - S(8)) / (float)Math.Max(1, TrackW - S(16)) * (Max - Min));
            v = Math.Max(Min, Math.Min(Max, v));
            if (v != Value) { Value = v; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); }
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left && Enabled) { drag = true; Capture = true; SetFrom(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (drag) SetFrom(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = false; Capture = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            int tw = TrackW, cy = Height / 2;
            float t = (Value - Min) / (float)Math.Max(1, Max - Min), x = S(8) + t * (tw - S(16));
            Ui.FillRound(g, new RectangleF(S(8), cy - S(2), tw - S(16), S(4)), S(2), C.Control);
            if (Enabled) Ui.FillAccent(g, new RectangleF(S(8), cy - S(2), x - S(8), S(4)), S(2), 0, 255); else Ui.FillRound(g, new RectangleF(S(8), cy - S(2), x - S(8), S(4)), S(2), C.Faint);
            float d = S(16);
            using (SolidBrush b = new SolidBrush(C.ControlHi)) g.FillEllipse(b, x - d / 2, cy - d / 2, d, d);
            float d2 = S(8) + S(2) * hover.Value;
            using (SolidBrush b = new SolidBrush(Enabled ? C.Accent : C.Faint)) g.FillEllipse(b, x - d2 / 2, cy - d2 / 2, d2, d2);
            Ui.TextRight(g, Value + Suffix, Ui.F(9.5f, 1), new Rectangle(tw, 0, Width - tw, Height), C.Sub);
        }
    }

    class Lbl : Ctl
    {
        public float Pt = 10; public int Weight; public Color Fore = C.Text; public bool Wraps; public bool Center;
        public Lbl(string text, float pt, int weight, Color fore) { Text = text; Pt = pt; Weight = weight; Fore = fore; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Bg);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            if (Wraps) Ui.Wrap(g, Text, Ui.F(Pt, Weight), r, Fore);
            else if (Center) Ui.TextCenter(g, Text, Ui.F(Pt, Weight), r, Fore);
            else Ui.TextLeft(g, Text, Ui.F(Pt, Weight), r, Fore);
        }
        public int Fit(int width) { return Wraps ? Ui.WrapHeight(Text, Ui.F(Pt, Weight), width) : Ui.Measure(Text, Ui.F(Pt, Weight)).Height; }
    }

    class Card : Panel
    {
        public Color Fill = C.Card;
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = C.Card;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : C.Bg); Ui.Prep(g);
            Ui.FillRound(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Ui.S(10), Fill);
            Ui.StrokeRound(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Ui.S(10), Ui.Mix(Fill, C.Text, 0.05f), 1);
        }
    }

    class Field : Ctl
    {
        public readonly TextBox Box = new TextBox(); public string Icon = ""; public string Placeholder = "";
        public Field()
        {
            Box.BorderStyle = BorderStyle.None; Box.BackColor = C.Control; Box.ForeColor = C.Text;
            Controls.Add(Box); Cursor = Cursors.IBeam;
            Box.GotFocus += delegate { Invalidate(); }; Box.LostFocus += delegate { Invalidate(); };
            Box.TextChanged += delegate { Invalidate(); };
            Box.HandleCreated += delegate { Native.SendMessageStr(Box.Handle, 0x1501, (IntPtr)1, Placeholder); };
        }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Box.Font = Ui.F(10, 0);
            int left = Icon.Length > 0 ? S(34) : S(12);
            Box.SetBounds(left, (Height - Box.PreferredHeight) / 2 + 1, Width - left - S(10), Box.PreferredHeight);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.FillRound(g, r, S(6), C.Control);
            bool focus = Box.Focused;
            Ui.StrokeRound(g, r, S(6), focus ? C.Accent : C.Stroke, 1);
            if (focus) using (Pen p = new Pen(C.Accent, S(2))) g.DrawLine(p, S(6), Height - S(1), Width - S(6), Height - S(1));
            if (Icon.Length > 0) Ui.DrawIcon(g, Icon, 10, new Rectangle(S(8), 0, S(22), Height), C.Sub);
        }
    }

    class Scroller : Panel
    {
        public Scroller() { AutoScroll = true; BackColor = C.Bg; SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.SetWindowTheme(Handle, "DarkMode_Explorer", null); }
        protected override Point ScrollToControl(Control activeControl) { return DisplayRectangle.Location; }
    }

    static class Menus
    {
        public static ContextMenuStrip Make() { return Make(true); }
        public static ContextMenuStrip Make(bool temporary)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new Renderer(); m.ShowImageMargin = false; m.ShowCheckMargin = false;
            m.Font = Ui.F(10, 0);
            m.Padding = new Padding(Ui.S(4), Ui.S(4), Ui.S(4), Ui.S(4));
            m.Opening += delegate
            {
                m.Font = Ui.F(10, 0);
                foreach (ToolStripItem it in m.Items) { it.ForeColor = C.Text; it.Padding = new Padding(Ui.S(8), Ui.S(6), Ui.S(8), Ui.S(6)); }
            };
            m.HandleCreated += delegate { int v = 2; Native.DwmSetWindowAttribute(m.Handle, 33, ref v, 4); };
            if (temporary) m.Closed += delegate { m.BeginInvoke((MethodInvoker)delegate { m.Dispose(); }); };
            return m;
        }

        public static ToolStripMenuItem Item(ContextMenuStrip m, string text, string glyph, EventHandler click)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text); it.Tag = glyph; it.Click += click; m.Items.Add(it); return it;
        }

        class Renderer : ToolStripRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(C.CardHi); }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (Pen p = new Pen(C.Stroke)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected || !e.Item.Enabled) return;
                Ui.Prep(e.Graphics);
                Ui.FillRound(e.Graphics, new RectangleF(Ui.S(2), 1, e.Item.Width - Ui.S(4), e.Item.Height - 2), Ui.S(5), C.ControlHi);
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                ToolStripMenuItem mi = e.Item as ToolStripMenuItem;
                string glyph = e.Item.Tag as string;
                bool danger = e.Text == "Delete";
                Color c = !e.Item.Enabled ? C.Faint : danger ? C.Danger : C.Text;
                int x = Ui.S(10);
                if (!string.IsNullOrEmpty(glyph))
                {
                    Ui.DrawIcon(e.Graphics, glyph, 10, new Rectangle(x, 0, Ui.S(18), e.Item.Height), danger ? C.Danger : C.Sub);
                    x += Ui.S(28);
                }
                else if (mi != null && mi.Checked)
                {
                    Ui.DrawIcon(e.Graphics, Glyph.Check, 9, new Rectangle(x, 0, Ui.S(18), e.Item.Height), C.Accent);
                    x += Ui.S(28);
                }
                else if (mi != null && mi.Owner != null && HasChecks(mi.Owner)) x += Ui.S(28);
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, new Rectangle(x, 0, e.Item.Width - x - Ui.S(8), e.Item.Height), c,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            static bool HasChecks(ToolStrip s) { foreach (ToolStripItem i in s.Items) { ToolStripMenuItem m = i as ToolStripMenuItem; if (m != null && m.Checked) return true; } return false; }
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (Pen p = new Pen(C.Stroke)) e.Graphics.DrawLine(p, Ui.S(8), y, e.Item.Width - Ui.S(8), y);
            }
        }
    }

    class Toast : Ctl
    {
        string icon = "", action = ""; Action onAction; readonly Timer timer = new Timer();
        Rectangle actionRect;
        public Toast()
        {
            Visible = false;
            timer.Tick += delegate { timer.Stop(); Visible = false; };
        }
        public void Show(string glyph, string text, string actionText, Action act, int ms)
        {
            icon = glyph ?? ""; Text = text; action = actionText ?? ""; onAction = act;
            Font f = Ui.F(10, 0);
            int w = S(48) + Ui.Measure(text, f).Width + (action.Length > 0 ? Ui.Measure(action, Ui.F(10, 1)).Width + S(36) : 0);
            Size = new Size(Math.Min(Parent.ClientSize.Width - S(40), w), S(44));
            Location = new Point((Parent.ClientSize.Width - Width) / 2, Parent.ClientSize.Height - Height - S(24));
            Visible = true; BringToFront(); Invalidate();
            timer.Stop(); timer.Interval = ms; timer.Start();
        }
        public void HideNow() { timer.Stop(); Visible = false; }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && action.Length > 0 && actionRect.Contains(e.Location)) { HideNow(); if (onAction != null) onAction(); }
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Cursor = actionRect.Contains(e.Location) && action.Length > 0 ? Cursors.Hand : Cursors.Default; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.FillRound(g, r, S(10), C.CardHi); Ui.StrokeRound(g, r, S(10), C.ControlHi, 1);
            Ui.DrawIcon(g, icon, 11, new Rectangle(S(12), 0, S(22), Height), C.Accent);
            int aw = action.Length > 0 ? Ui.Measure(action, Ui.F(10, 1)).Width + S(24) : 0;
            Ui.TextLeft(g, Text, Ui.F(10, 0), new Rectangle(S(42), 0, Width - S(42) - aw - S(10), Height), C.Text);
            if (aw > 0)
            {
                actionRect = new Rectangle(Width - aw - S(8), S(7), aw, Height - S(14));
                Ui.FillRound(g, actionRect, S(6), C.Control);
                Ui.TextCenter(g, action, Ui.F(10, 1), actionRect, C.AccentHi);
            }
        }
    }
}
