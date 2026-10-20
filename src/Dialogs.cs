using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Clutch
{
    class RenameDialog : Form
    {
        public static bool ShotMode;
        readonly Field box = new Field();
        readonly Btn ok = new Btn("Rename", "", BtnStyle.Accent), cancel = new Btn("Cancel", "", BtnStyle.Standard);
        readonly Lbl head = new Lbl("Rename clip", 13, 4, C.Text), hint = new Lbl("The file on disk is renamed too.", 9, 0, C.Sub);
        static int S(float v) { return Ui.S(v); }

        protected override void WndProc(ref Message m) { if (!Ui.HandleDpiChanged(this, ref m)) base.WndProc(ref m); }

        RenameDialog(string current)
        {
            Text = "Rename"; AutoScaleMode = AutoScaleMode.None; BackColor = C.Panel; Font = Ui.F(10, 0);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(S(440), S(196));
            box.Box.Text = current;
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            box.Box.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DialogResult = DialogResult.OK; Close(); }
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; Close(); }
            };
            foreach (Control c in new Control[] { head, hint, box, ok, cancel }) Controls.Add(c);
            Ui.ScaleChanged += Relayout;
            FormClosed += delegate { Ui.ScaleChanged -= Relayout; };
            Relayout();
        }

        void Relayout()
        {
            ClientSize = new Size(S(440), S(196));
            head.SetBounds(S(24), S(20), S(390), S(28));
            hint.SetBounds(S(24), S(50), S(390), S(20));
            box.SetBounds(S(24), S(80), S(392), S(36)); box.PerformLayout();
            cancel.SetBounds(S(416) - S(96), S(140), S(96), S(36));
            ok.SetBounds(cancel.Left - S(10) - S(110), S(140), S(110), S(36));
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.StyleTitleBar(this, C.Panel); }
        protected override void OnShown(EventArgs e) { base.OnShown(e); box.Box.Focus(); box.Box.SelectAll(); }

        public static string AskGame(IWin32Window owner, string current, List<string> known)
        {
            using (RenameDialog d = new RenameDialog(current))
            {
                d.Text = "Change game"; d.head.Text = "Change game"; d.ok.Text = "Save";
                d.hint.Text = "Which game this clip belongs to. Clips are grouped by it.";
                d.box.Placeholder = "Game name";
                AutoCompleteStringCollection list = new AutoCompleteStringCollection(); list.AddRange(known.ToArray());
                d.box.Box.AutoCompleteCustomSource = list; d.box.Box.AutoCompleteSource = AutoCompleteSource.CustomSource; d.box.Box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                return d.ShowDialog(owner) == DialogResult.OK ? d.box.Box.Text.Trim() : null;
            }
        }

        public static string Ask(IWin32Window owner, string current)
        {
            if (ShotMode) { RenameDialog s = new RenameDialog(current); s.StartPosition = FormStartPosition.CenterScreen; s.Show(owner); return null; }
            using (RenameDialog d = new RenameDialog(current))
                return d.ShowDialog(owner) == DialogResult.OK ? d.box.Box.Text.Trim() : null;
        }
    }

    class Window : Form
    {
        protected static int S(float v) { return Ui.S(v); }
        readonly int baseW, baseH;

        protected override void WndProc(ref Message m) { if (!Ui.HandleDpiChanged(this, ref m)) base.WndProc(ref m); }

        public Window(string title, int w, int h)
        {
            baseW = w; baseH = h;
            Text = title; AutoScaleMode = AutoScaleMode.None; BackColor = C.Bg; ForeColor = C.Text; Font = Ui.F(10, 0);
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true; KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            ClientSize = new Size(S(w), S(h));
            Action relayout = delegate { ClientSize = new Size(S(baseW), S(baseH)); Font = Ui.F(10, 0); Relayout(); Invalidate(true); };
            Ui.ScaleChanged += relayout;
            FormClosed += delegate { Ui.ScaleChanged -= relayout; };
        }

        protected virtual void Relayout() { }
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); Relayout(); }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); Ui.StyleTitleBar(this, C.Bg);
            try { float dpi = Native.GetDpiForWindow(Handle) / 96f; if (dpi > 0.5f && Math.Abs(dpi - Ui.Scale) > 0.01f) { Ui.Scale = dpi; ClientSize = new Size(S(baseW), S(baseH)); } } catch { }
        }

        protected void DrawHeader(Graphics g, string heading, string sub)
        {
            Ui.Prep(g);
            Ui.DrawLogo(g, new Rectangle(S(28), S(26), S(44), S(44)));
            Ui.TextLeft(g, heading, Ui.F(15, 4), new Rectangle(S(84), S(22), ClientSize.Width - S(110), S(30)), C.Text);
            if (!string.IsNullOrEmpty(sub)) Ui.TextLeft(g, sub, Ui.F(9.5f, 0), new Rectangle(S(84), S(50), ClientSize.Width - S(110), S(22)), C.Sub);
        }

        public void Front()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            TopMost = true; Activate(); Native.SetForegroundWindow(Handle); TopMost = false;
        }
    }

    class Progress : Ctl
    {
        float v; public bool Busy; float phase; readonly Timer spin = new Timer();
        public Progress() { spin.Interval = 16; spin.Tick += delegate { phase = (phase + 0.012f) % 1.4f; Invalidate(); }; }
        public float Value { get { return v; } set { v = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        public void SetBusy(bool b) { Busy = b; spin.Enabled = b && Visible; Invalidate(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); spin.Enabled = Busy && Visible; }
        protected override void Dispose(bool d) { if (d) spin.Dispose(); base.Dispose(d); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e); float r = Height / 2f;
            RectangleF full = new RectangleF(0, 0, Width, Height);
            Ui.FillRound(g, full, r, C.Control);
            if (Busy)
            {
                float x = (phase - 0.4f) * Width, w = Width * 0.4f;
                RectangleF seg = RectangleF.Intersect(full, new RectangleF(x, 0, w, Height));
                if (seg.Width > 1) Ui.FillRound(g, seg, r, C.Accent);
            }
            else if (v > 0) Ui.FillRound(g, new RectangleF(0, 0, Math.Max(Height, Width * v), Height), r, C.Accent);
        }
    }

    class TextView : Card
    {
        public readonly TextBox Box = new TextBox();
        public TextView()
        {
            Fill = C.Panel; BackColor = C.Panel;
            Box.Multiline = true; Box.ReadOnly = true; Box.ScrollBars = ScrollBars.Vertical; Box.BorderStyle = BorderStyle.None;
            Box.BackColor = C.Panel; Box.ForeColor = C.Sub; Box.WordWrap = true; Box.TabStop = false;
            Controls.Add(Box);
            Box.HandleCreated += delegate { Native.SetWindowTheme(Box.Handle, "DarkMode_Explorer", null); };
        }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); Box.Font = Ui.F(9, 0); Box.SetBounds(S(14), S(10), Width - S(18), Height - S(20)); }
        static int S(float v) { return Ui.S(v); }
    }
}
