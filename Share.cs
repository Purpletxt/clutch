using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Clutch
{
    static class FolderPicker
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] class FileOpenDialogCo { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr specs);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName(out IntPtr name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem item);
            void GetDisplayName(uint sigdn, out IntPtr name);
            void GetAttributes(uint mask, out uint attrs);
            void Compare(IShellItem item, uint hint, out int order);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        public static string Pick(IWin32Window owner, string title, string start)
        {
            IFileDialog d;
            try { d = (IFileDialog)new FileOpenDialogCo(); }
            catch { return Old(owner, title, start); }
            try
            {
                uint o; d.GetOptions(out o); d.SetOptions(o | 0x20 | 0x40 | 0x800);
                d.SetTitle(title); d.SetOkButtonLabel("Select folder");
                string from = start;
                while (!string.IsNullOrEmpty(from) && !Directory.Exists(from)) from = Path.GetDirectoryName(from);
                if (!string.IsNullOrEmpty(from))
                    try { Guid iid = typeof(IShellItem).GUID; IShellItem si; SHCreateItemFromParsingName(from, IntPtr.Zero, ref iid, out si); d.SetFolder(si); } catch { }
                if (d.Show(owner != null ? owner.Handle : IntPtr.Zero) != 0) return null;
                IShellItem r; d.GetResult(out r);
                IntPtr p; r.GetDisplayName(0x80058000, out p);
                try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
            }
            catch (Exception ex) { Log.Info("Folder picker", ex.Message); return null; }
            finally { Marshal.ReleaseComObject(d); }
        }

        static string Old(IWin32Window owner, string title, string start)
        {
            using (FolderBrowserDialog f = new FolderBrowserDialog())
            {
                f.Description = title; if (Directory.Exists(start)) f.SelectedPath = start;
                return f.ShowDialog(owner) == DialogResult.OK ? f.SelectedPath : null;
            }
        }
    }

    class ChoiceDialog : Form
    {
        static int S(float v) { return Ui.S(v); }
        readonly Lbl head, body;
        readonly List<Btn> buttons = new List<Btn>();
        readonly string icon;
        int picked = -1;

        protected override void WndProc(ref Message m) { if (!Ui.HandleDpiChanged(this, ref m)) base.WndProc(ref m); }

        ChoiceDialog(string title, string message, string glyph, string[] choices)
        {
            Text = title; AutoScaleMode = AutoScaleMode.None; BackColor = C.Panel; Font = Ui.F(10, 0);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; KeyPreview = true;
            icon = glyph ?? "";
            head = new Lbl(title, 13, 4, C.Text); body = new Lbl(message, 9.5f, 0, C.Sub); body.Wraps = true;
            Controls.Add(head); Controls.Add(body);
            for (int i = 0; i < choices.Length; i++)
            {
                int k = i;
                Btn b = new Btn(choices[i], "", i == 0 ? BtnStyle.Accent : BtnStyle.Standard);
                b.Click += delegate { picked = k; Close(); };
                buttons.Add(b); Controls.Add(b);
            }
            KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); } };
            Ui.ScaleChanged += Relayout;
            FormClosed += delegate { Ui.ScaleChanged -= Relayout; };
            Relayout();
        }

        void Relayout()
        {
            int w = S(480), x = S(24) + (icon.Length > 0 ? S(46) : 0), tw = w - x - S(24);
            head.SetBounds(x, S(22), tw, S(28));
            int bh = body.Fit(tw); body.SetBounds(x, S(56), tw, bh);
            int y = body.Bottom + S(24), right = w - S(24);
            for (int i = buttons.Count - 1; i >= 0; i--)
            {
                Btn b = buttons[i]; b.Height = S(36);
                b.Width = Math.Max(S(96), b.PreferredWidth() + S(8));
                b.Location = new Point(right - b.Width, y); right = b.Left - S(10);
            }
            ClientSize = new Size(w, y + S(36) + S(22));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (icon.Length == 0) return;
            Ui.Prep(e.Graphics);
            Rectangle r = new Rectangle(S(24), S(22), S(32), S(32));
            Ui.FillRound(e.Graphics, r, S(8), Ui.Mix(C.Panel, C.Text, 0.07f));
            Ui.DrawIcon(e.Graphics, icon, 12, r, C.Text);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.StyleTitleBar(this, C.Panel); }
        protected override void OnShown(EventArgs e) { base.OnShown(e); if (buttons.Count > 0) buttons[0].Focus(); }

        public static int Ask(IWin32Window owner, string title, string message, string glyph, params string[] choices)
        {
            using (ChoiceDialog d = new ChoiceDialog(title, message, glyph, choices))
            {
                if (owner == null) d.StartPosition = FormStartPosition.CenterScreen;
                d.ShowDialog(owner);
                return d.picked;
            }
        }
    }

    static class DiscordFit
    {
        public static readonly int[] Sizes = { 10, 50, 500 };
        static readonly string[] Plans = { "Discord free", "Nitro Basic", "Nitro" };
        static bool busy; static double progress;

        public static void Menu(MainForm m, Clip c, Control anchor, Point at)
        {
            ContextMenuStrip menu = Menus.Make();
            ToolStripMenuItem head = new ToolStripMenuItem("Fit for Discord"); head.Enabled = false; head.Tag = Glyph.Discord; menu.Items.Add(head);
            for (int i = 0; i < Sizes.Length; i++)
            {
                int k = i;
                ToolStripMenuItem it = new ToolStripMenuItem("Under " + Sizes[i] + " MB  \u00B7  " + Plans[i]);
                it.Checked = Settings.Cur.DiscordMB == Sizes[i];
                it.Click += delegate { Start(m, c, Sizes[k]); };
                menu.Items.Add(it);
            }
            menu.Show(anchor, at);
        }

        public static void Start(MainForm m, Clip c, int mb)
        {
            Settings.Cur.DiscordMB = mb; Settings.Save();
            if (busy) { m.Toast.Show(Glyph.Discord, "Already fitting a clip for Discord. One at a time.", null, null, 3500); return; }
            if (c == null || !File.Exists(c.Path)) return;
            long limit = mb * 1000L * 1000L;
            if (c.Size <= limit) { Copy(m, c.Path, "Already under " + mb + " MB, so it's copied as it is. Paste it into Discord with Ctrl+V.", null); return; }
            if (!FFmpeg.Ready) { m.Toast.Show(Glyph.Warning, "Finish the FFmpeg setup first.", null, null, 4000); return; }
            busy = true; progress = 0;
            string src = c.Path, game = c.Game, enc = TrimPage.EncoderName();
            double known = c.Duration, mic = Settings.Cur.MicVolume / 100.0;
            string outPath = Library.UniquePath(Path.Combine(Path.GetDirectoryName(src), Library.SafeName(c.Title + " (Discord)") + ".mp4"));
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 400;
            EventHandler show = delegate { m.Toast.Show(Glyph.Discord, "Fitting for Discord\u2026  " + (int)(progress * 100) + "%", null, null, 2500); };
            t.Tick += show; t.Start(); show(null, EventArgs.Empty);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null; double dur = known; long bytes = 0;
                try
                {
                    if (dur <= 0) dur = FFmpeg.Duration(src);
                    err = TrimPage.RunExport(src, outPath, 0, dur, mb, 1, mic, FFmpeg.AudioTracks(src), enc, false, 0.5, 0, delegate (double p) { progress = p; });
                    if (err == null) bytes = new FileInfo(outPath).Length;
                }
                catch (Exception ex) { err = "Couldn't fit it for Discord. Details are in the error log."; Log.Error("Fit for Discord", ex); }
                Post(m, delegate
                {
                    busy = false; t.Stop(); t.Dispose();
                    if (err != null)
                    {
                        bool longer = err.Contains("shorter");
                        m.Toast.Show(Glyph.Warning, longer ? "Too long to fit in " + mb + " MB. Trim it shorter first." : err, longer ? "Trim" : null, longer ? (Action)delegate { m.TrimClip(c); } : null, 7000);
                        return;
                    }
                    Library.Add(outPath, game, dur);
                    Copy(m, outPath, "Fitted to " + Library.NiceSize(bytes) + " and copied. Paste it into Discord with Ctrl+V.", outPath);
                });
            });
        }

        static void Copy(MainForm m, string path, string message, string reveal)
        {
            try
            {
                StringCollection sc = new StringCollection(); sc.Add(path); Clipboard.SetFileDropList(sc);
                m.Toast.Show(Glyph.Discord, message, reveal != null ? "Show" : null, reveal != null ? (Action)delegate { try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + reveal + "\""); } catch { } } : null, 8000);
            }
            catch (Exception ex) { Log.Error("Copy clip", ex); }
        }

        public static void Post(Control ui, Action a) { try { if (!ui.IsDisposed) ui.BeginInvoke(a); } catch { } }
    }

    static class MedalImport
    {
        static bool busy, stop;
        static readonly string[] Generic = { "clips", "medal", "recordings", "full sessions", "bookmarks", "trimmed", "edited", "videos", "uploads", "local", "exports" };
        static readonly string[] Skip = { "screenshots", "thumbnails", "thumbs", "cache", "temp", "tmp", "logs" };

        public static void Start(MainForm m)
        {
            if (busy) { m.Toast.Show(Glyph.Import, "Already importing from Medal.", null, null, 3000); return; }
            string root;
            Cursor.Current = Cursors.WaitCursor;
            try { root = Find(); } finally { Cursor.Current = Cursors.Default; }
            if (root == null)
            {
                if (ChoiceDialog.Ask(m, "Import from Medal", "Clutch couldn't find Medal's clips on this PC. If you moved Medal's folder, choose it and Clutch will bring the clips in, sorted by game.", Glyph.Import, "Choose folder", "Cancel") != 0) return;
                root = FolderPicker.Pick(m, "Choose Medal's clips folder", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
                if (root == null) return;
            }
            string clutch = Settings.Cur.Folder;
            if (Inside(clutch, root) || Inside(root, clutch)) { m.Toast.Show(Glyph.Warning, "That's your Clutch clips folder, so there's nothing to import.", null, null, 5000); return; }
            List<string> files;
            Cursor.Current = Cursors.WaitCursor;
            try { files = Scan(root, 50000); } finally { Cursor.Current = Cursors.Default; }
            if (files.Count == 0) { m.Toast.Show(Glyph.Info, "No clips found in " + root + ".", null, null, 5000); return; }
            long total = 0; foreach (string f in files) try { total += new FileInfo(f).Length; } catch { }
            string size = Library.NiceSize(total);
            int pick = ChoiceDialog.Ask(m, "Import from Medal",
                "Found " + (files.Count == 1 ? "1 clip" : files.Count + " clips") + " (" + size + ") in " + root + ". Clutch keeps each clip's game and date.\n\n"
                + "Copy leaves Medal's clips where they are and uses " + size + " more space. Move takes them out of Medal's folder and uses no extra space.",
                Glyph.Import, "Copy", "Move", "Cancel");
            if (pick != 0 && pick != 1) return;
            bool move = pick == 1;
            bool sameDrive = string.Equals(Path.GetPathRoot(Path.GetFullPath(root)), Path.GetPathRoot(Path.GetFullPath(clutch)), StringComparison.OrdinalIgnoreCase);
            if (!move || !sameDrive)
                try
                {
                    long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(clutch))).AvailableFreeSpace;
                    if (free < total + (1L << 30)) { m.Toast.Show(Glyph.Warning, "Not enough free space: this needs " + size + " and there's " + Library.NiceSize(free) + " free.", null, null, 7000); return; }
                }
                catch { }
            Run(m, root, clutch, files, move);
        }

        static void Run(MainForm m, string root, string clutch, List<string> files, bool move)
        {
            busy = true; stop = false;
            int handled = 0, count = files.Count;
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 400;
            EventHandler show = delegate { m.Toast.Show(Glyph.Import, (move ? "Moving" : "Copying") + " clips from Medal\u2026  " + handled + " of " + count, "Stop", delegate { stop = true; }, 2500); };
            t.Tick += show; t.Start(); show(null, EventArgs.Empty);
            ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, string> games = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int done = 0, already = 0, failed = 0;
                try { Directory.CreateDirectory(clutch); } catch { }
                foreach (string f in files)
                {
                    if (stop) break;
                    try
                    {
                        FileInfo fi = new FileInfo(f);
                        string dst = Path.Combine(clutch, fi.Name);
                        if (File.Exists(dst) && new FileInfo(dst).Length == fi.Length) already++;
                        else
                        {
                            dst = Library.UniquePath(dst);
                            DateTime when = fi.CreationTime < fi.LastWriteTime ? fi.CreationTime : fi.LastWriteTime;
                            Transfer(f, dst, move);
                            try { File.SetCreationTime(dst, when); File.SetLastWriteTime(dst, when); } catch { }
                            games[Path.GetFileName(dst)] = GameFor(root, f);
                            done++;
                        }
                    }
                    catch (Exception ex) { failed++; Log.Info("Medal import", Path.GetFileName(f) + ": " + ex.Message); }
                    handled++;
                }
                DiscordFit.Post(m, delegate
                {
                    busy = false; t.Stop(); t.Dispose();
                    Library.Remember(games, null);
                    string msg = done == 0 && already > 0 ? "Those clips are already in Clutch."
                        : (move ? "Moved " : "Imported ") + (done == 1 ? "1 clip" : done + " clips") + " from Medal." + (already > 0 ? " " + already + " were already here." : "");
                    if (failed > 0) msg += " " + failed + " couldn't be imported; the error log says why.";
                    if (stop) msg = "Stopped. " + msg;
                    m.Toast.Show(failed > 0 ? Glyph.Warning : Glyph.Check, msg, null, null, 7000);
                });
            });
        }

        public static void Transfer(string src, string dst, bool move)
        {
            bool sameDrive = string.Equals(Path.GetPathRoot(src), Path.GetPathRoot(dst), StringComparison.OrdinalIgnoreCase);
            if (move && sameDrive) { File.Move(src, dst); return; }
            string part = dst + ".part";
            try { File.Copy(src, part, true); File.Move(part, dst); }
            catch { try { File.Delete(part); } catch { } throw; }
            if (move) File.Delete(src);
        }

        static bool Inside(string dir, string parent)
        {
            try
            {
                string a = Path.GetFullPath(dir).TrimEnd('\\') + "\\", b = Path.GetFullPath(parent).TrimEnd('\\') + "\\";
                return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static string GameFor(string root, string file)
        {
            string top = Path.GetFullPath(root).TrimEnd('\\'), dir = Path.GetDirectoryName(file);
            while (!string.IsNullOrEmpty(dir) && dir.Length > top.Length)
            {
                string n = Path.GetFileName(dir);
                if (Array.IndexOf(Generic, n.ToLowerInvariant()) < 0) return Games.Clean(n);
                dir = Path.GetDirectoryName(dir);
            }
            return "";
        }

        public static List<string> Scan(string root, int limit)
        {
            List<string> o = new List<string>();
            Stack<string> dirs = new Stack<string>(); dirs.Push(root);
            while (dirs.Count > 0 && o.Count < limit)
            {
                string d = dirs.Pop();
                try { foreach (string f in Directory.GetFiles(d, "*.mp4")) { if (f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) o.Add(f); if (o.Count >= limit) break; } } catch { }
                try
                {
                    foreach (string s in Directory.GetDirectories(d))
                    {
                        string n = Path.GetFileName(s).ToLowerInvariant();
                        if (n.StartsWith(".") || Array.IndexOf(Skip, n) >= 0) continue;
                        if ((new DirectoryInfo(s).Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        dirs.Push(s);
                    }
                }
                catch { }
            }
            return o;
        }

        static string Find()
        {
            List<string> spots = new List<string>();
            string roam = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Regex key = new Regex("\"(?:clip|clips|recording|recordings|save|output|video|videos|media)(?:folder|directory|dir|path|location)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)+)\"", RegexOptions.IgnoreCase);
            foreach (string app in new[] { Path.Combine(roam, "Medal"), Path.Combine(local, "Medal") })
            {
                if (!Directory.Exists(app)) continue;
                List<string> json = new List<string>();
                try { json.AddRange(Directory.GetFiles(app, "*.json")); } catch { }
                try { foreach (string s in Directory.GetDirectories(app)) try { json.AddRange(Directory.GetFiles(s, "*.json")); } catch { } } catch { }
                foreach (string j in json)
                    try
                    {
                        if (new FileInfo(j).Length > 4 * 1024 * 1024) continue;
                        foreach (Match mt in key.Matches(File.ReadAllText(j)))
                        {
                            string p = mt.Groups[1].Value.Replace("\\\\", "\\").Replace("\\/", "/");
                            if (p.Length > 3 && !spots.Contains(p)) spots.Add(p);
                        }
                    }
                    catch { }
            }
            spots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Medal"));
            spots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Medal"));
            try { foreach (DriveInfo d in DriveInfo.GetDrives()) if (d.DriveType == DriveType.Fixed && d.IsReady) spots.Add(Path.Combine(d.RootDirectory.FullName, "Medal")); } catch { }
            foreach (string s in spots)
                try { if (Directory.Exists(s) && Scan(s, 1).Count > 0) return s; } catch { }
            return null;
        }
    }

    static class ClipFolder
    {
        static bool busy;

        public static string Clean(string text)
        {
            string p = Environment.ExpandEnvironmentVariables((text ?? "").Trim().Trim('"').Trim());
            if (p.Length == 0) return null;
            try { if (!Path.IsPathRooted(p) || p.StartsWith("\\") && !p.StartsWith("\\\\")) return null; p = Path.GetFullPath(p); }
            catch { return null; }
            return p.Length > 3 ? p.TrimEnd('\\') : p;
        }

        public static bool Change(MainForm m, string text)
        {
            if (busy) { m.Toast.Show(Glyph.Folder, "Still moving your clips. Try again when it's done.", null, null, 4000); return false; }
            string path = Clean(text);
            if (path == null) { m.Toast.Show(Glyph.Warning, "Paste a full folder path, like D:\\Clips.", null, null, 5000); return false; }
            string old = Settings.Cur.Folder;
            if (string.Equals(Path.GetFullPath(old).TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
            if (File.Exists(path)) { m.Toast.Show(Glyph.Warning, "That's a file, not a folder.", null, null, 5000); return false; }
            try { Directory.CreateDirectory(path); string probe = Path.Combine(path, ".clutch-test"); File.WriteAllText(probe, ""); File.Delete(probe); }
            catch (Exception ex) { m.Toast.Show(Glyph.Warning, "Clutch can't save clips there: " + ex.Message, null, null, 7000); return false; }

            Library.Flush(true); Library.Save();
            List<Clip> clips = new List<Clip>();
            foreach (Clip c in Library.Clips) if (File.Exists(c.Path)) clips.Add(c);
            bool move = false;
            if (clips.Count > 0)
            {
                long total = 0; foreach (Clip c in clips) total += c.Size;
                int pick = ChoiceDialog.Ask(m, "Move your clips too?",
                    "You have " + (clips.Count == 1 ? "1 clip" : clips.Count + " clips") + " (" + Library.NiceSize(total) + ") in " + old + ".\n\nMove them to " + path + " so they stay in Clutch, or leave them where they are and only save new clips there.",
                    Glyph.Folder, "Move clips", "Leave them", "Cancel");
                if (pick != 0 && pick != 1) return false;
                move = pick == 0;
            }
            Settings.Cur.ClipsFolder = path; Settings.Save();
            if (!move) { Library.Rescan(); m.Toast.Show(Glyph.Folder, "New clips will save to " + path + ".", null, null, 5000); return true; }

            if (m.Viewer.Visible || m.Trim.Visible) m.Go(m.Clips);
            m.Viewer.Video.Close(); m.Trim.Video.Close();
            List<KeyValuePair<string, long>> items = new List<KeyValuePair<string, long>>();
            foreach (Clip c in clips) items.Add(new KeyValuePair<string, long>(c.Path, c.Size));
            busy = true;
            int handled = 0, count = items.Count;
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 400;
            EventHandler show = delegate { m.Toast.Show(Glyph.Folder, "Moving clips\u2026  " + handled + " of " + count, null, null, 2500); };
            t.Tick += show; t.Start(); show(null, EventArgs.Empty);
            ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, string> renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int moved = 0, failed = 0;
                foreach (KeyValuePair<string, long> it in items)
                {
                    try
                    {
                        string name = Path.GetFileName(it.Key), dst = Library.UniquePath(Path.Combine(path, name));
                        MedalImport.Transfer(it.Key, dst, true);
                        if (!string.Equals(Path.GetFileName(dst), name, StringComparison.OrdinalIgnoreCase)) renamed[name] = Path.GetFileName(dst);
                        try { string a = Library.ThumbPath(it.Key, it.Value), b = Library.ThumbPath(dst, it.Value); if (File.Exists(a) && !File.Exists(b)) File.Move(a, b); } catch { }
                        moved++;
                    }
                    catch (Exception ex) { failed++; Log.Info("Move clips", Path.GetFileName(it.Key) + ": " + ex.Message); }
                    handled++;
                }
                DiscordFit.Post(m, delegate
                {
                    busy = false; t.Stop(); t.Dispose();
                    Library.Remember(null, renamed);
                    string msg = "Moved " + (moved == 1 ? "1 clip" : moved + " clips") + " to " + path + ".";
                    if (failed > 0) msg += " " + failed + " couldn't be moved (maybe open in another app) and stayed in the old folder.";
                    m.Toast.Show(failed > 0 ? Glyph.Warning : Glyph.Check, msg, null, null, 7000);
                    m.SettingsP.Tick();
                });
            });
            return true;
        }
    }

    class GameChips : Ctl
    {
        List<string> names = new List<string>(); List<int> counts = new List<int>(); int total;
        string selected;
        public event Action<string> Picked;
        public event Action AddGame;
        readonly List<KeyValuePair<Rectangle, string>> hits = new List<KeyValuePair<Rectangle, string>>();
        readonly List<string> overflow = new List<string>();
        Rectangle more = Rectangle.Empty; int hot = -2;
        const string AllKey = "\u0001all";

        public GameChips() { Cursor = Cursors.Hand; }

        public void Set(List<string> gameNames, List<int> gameCounts, int all, string current)
        {
            names = gameNames; counts = gameCounts; total = all; selected = current;
            Invalidate();
        }

        int ChipW(string text, string count, bool badge)
        {
            return S(14) + (badge ? S(22) : 0) + Ui.Measure(text, Ui.F(9.5f, 1)).Width + S(8) + Ui.Measure(count, Ui.F(9, 0)).Width + S(14);
        }

        void Build()
        {
            hits.Clear(); overflow.Clear(); more = Rectangle.Empty;
            int h = Height - S(2), gap = S(8), x = 0;
            int allW = ChipW("All", total.ToString(), false);
            hits.Add(new KeyValuePair<Rectangle, string>(new Rectangle(x, 0, allW, h), AllKey)); x += allW + gap;
            int moreW = h;
            List<int> shown = new List<int>(); int used = x;
            for (int i = 0; i < names.Count; i++)
            {
                int w = ChipW(names[i], counts[i].ToString(), true);
                if (used + w <= Width - moreW - gap) { shown.Add(i); used += w + gap; }
                else { for (int r = i; r < names.Count; r++) overflow.Add(names[r]); break; }
            }
            if (selected != null && overflow.Contains(selected) && shown.Count > 0)
            {
                int si = names.IndexOf(selected);
                overflow.Remove(selected); overflow.Insert(0, names[shown[shown.Count - 1]]);
                shown[shown.Count - 1] = si;
            }
            foreach (int i in shown)
            {
                int w = ChipW(names[i], counts[i].ToString(), true);
                hits.Add(new KeyValuePair<Rectangle, string>(new Rectangle(x, 0, w, h), names[i])); x += w + gap;
            }
            more = new Rectangle(x, 0, moreW, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e); Build();
            for (int k = 0; k < hits.Count; k++)
            {
                Rectangle r = hits[k].Key; string key = hits[k].Value;
                bool all = key == AllKey, on = all ? selected == null : key == selected;
                RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
                if (on) Ui.FillAccent(g, rf, rf.Height / 2, hot == k ? 0.08f : 0, 255);
                else { Ui.FillRound(g, rf, rf.Height / 2, hot == k ? C.Control : C.Panel); Ui.StrokeRound(g, rf, rf.Height / 2, Ui.Mix(C.Panel, C.Text, 0.08f), 1); }
                int x = r.X + S(14);
                if (!all) { ClipGrid.GameBadge(g, key, new Rectangle(x, r.Y + (r.Height - S(16)) / 2, S(16), S(16))); x += S(22); }
                string label = all ? "All" : key, num = (all ? total : counts[names.IndexOf(key)]).ToString();
                int tw = Ui.Measure(label, Ui.F(9.5f, 1)).Width;
                Ui.TextLeft(g, label, Ui.F(9.5f, 1), new Rectangle(x, r.Y, tw + S(2), r.Height - S(1)), on ? C.AccentText : C.Text);
                x += tw + S(8);
                Ui.TextLeft(g, num, Ui.F(9, 0), new Rectangle(x, r.Y, r.Right - x, r.Height - S(1)), on ? Ui.Alpha(C.AccentText, 190) : C.Faint);
            }
            if (!more.IsEmpty)
            {
                RectangleF rf = new RectangleF(more.X + 0.5f, more.Y + 0.5f, more.Width - 1, more.Height - 1);
                Ui.FillRound(g, rf, rf.Height / 2, hot == -1 ? C.Control : C.Panel);
                Ui.StrokeRound(g, rf, rf.Height / 2, Ui.Mix(C.Panel, C.Text, 0.08f), 1);
                Ui.DrawIcon(g, Glyph.Add, 10, more, overflow.Count > 0 ? C.AccentHi : C.Text);
            }
        }

        int HitAt(Point p)
        {
            for (int k = 0; k < hits.Count; k++) if (hits[k].Key.Contains(p)) return k;
            return more.Contains(p) ? -1 : -2;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = HitAt(e.Location); if (h != hot) { hot = h; Invalidate(); } Cursor = h == -2 ? Cursors.Default : Cursors.Hand; }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -2; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            int h = HitAt(e.Location);
            if (h >= 0) { string key = hits[h].Value; Pick(key == AllKey ? null : key); }
            else if (h == -1)
            {
                ContextMenuStrip m = Menus.Make();
                foreach (string n in overflow)
                {
                    string name = n;
                    ToolStripMenuItem it = new ToolStripMenuItem(name + "   " + counts[names.IndexOf(name)]); it.Checked = name == selected;
                    it.Click += delegate { Pick(name); };
                    m.Items.Add(it);
                }
                if (overflow.Count > 0) m.Items.Add(new ToolStripSeparator());
                Menus.Item(m, "Add a game\u2026", Glyph.Game, delegate { if (AddGame != null) AddGame(); });
                m.MaximumSize = new Size(S(420), S(560));
                m.Show(this, new Point(more.X, more.Bottom + S(4)));
            }
        }

        void Pick(string name) { selected = name; Invalidate(); if (Picked != null) Picked(name); }
    }


    static class Backdrop
    {
        static Bitmap bmp;

        public static void Free() { if (bmp != null) { bmp.Dispose(); bmp = null; } }

        public static void Draw(Graphics g, Size page, Point offset)
        {
            if (page.Width < 2 || page.Height < 2) return;
            if (bmp == null || bmp.Width != page.Width || bmp.Height != page.Height) { Free(); bmp = Make(page.Width, page.Height); }
            g.DrawImageUnscaled(bmp, offset.X, offset.Y);
        }

        static Bitmap Make(int w, int h)
        {
            Bitmap b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(C.Bg); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Glow(g, w * 0.92f, h * 0.95f, w * 0.7f, h * 0.8f, Color.FromArgb(44, C.Accent));
                Glow(g, w * 0.02f, h * 1.05f, w * 0.45f, h * 0.5f, Color.FromArgb(22, 80, 90, 255));
                int s = Math.Min(Ui.S(380), Math.Min(w, h) * 9 / 20);
                using (Bitmap logo = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                {
                    using (Graphics lg = Graphics.FromImage(logo)) { Ui.Prep(lg); Ui.DrawLogo(lg, new Rectangle(0, 0, s, s)); }
                    System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix(); cm.Matrix33 = 0.045f;
                    using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
                    {
                        ia.SetColorMatrix(cm);
                        g.DrawImage(logo, new Rectangle(w - s - Ui.S(60), h - s - Ui.S(50), s, s), 0, 0, s, s, GraphicsUnit.Pixel, ia);
                    }
                }
            }
            Dither(b);
            return b;
        }

        static unsafe void Dither(Bitmap b)
        {
            Rectangle all = new Rectangle(0, 0, b.Width, b.Height);
            System.Drawing.Imaging.BitmapData d = b.LockBits(all, System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                uint seed = 2463534242;
                for (int y = 0; y < b.Height; y++)
                {
                    byte* row = (byte*)d.Scan0 + y * d.Stride;
                    for (int x = 0; x < b.Width; x++)
                    {
                        seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
                        int n = (int)(seed % 3) - 1;
                        if (n == 0) continue;
                        byte* px = row + x * 4;
                        for (int k = 0; k < 3; k++) { int v = px[k] + n; px[k] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v); }
                    }
                }
            }
            finally { b.UnlockBits(d); }
        }

        static void Glow(Graphics g, float cx, float cy, float rx, float ry, Color c)
        {
            using (System.Drawing.Drawing2D.GraphicsPath p = new System.Drawing.Drawing2D.GraphicsPath())
            {
                p.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
                using (System.Drawing.Drawing2D.PathGradientBrush br = new System.Drawing.Drawing2D.PathGradientBrush(p))
                {
                    br.CenterColor = c; br.SurroundColors = new[] { Color.FromArgb(0, c) };
                    System.Drawing.Drawing2D.Blend bl = new System.Drawing.Drawing2D.Blend();
                    bl.Positions = new[] { 0f, 0.25f, 0.55f, 0.8f, 1f };
                    bl.Factors = new[] { 0f, 0.08f, 0.35f, 0.75f, 1f };
                    br.Blend = bl;
                    g.FillPath(br, p);
                }
            }
        }
    }

    class OptionTiles : Ctl
    {
        public class Item { public string Icon, Title, Sub; }
        public readonly List<Item> Items = new List<Item>();
        public int Columns = 2, Index;
        public bool ShowSub = true;
        public event Action Changed;
        int hot = -1;

        public OptionTiles(int columns) { Columns = columns; Cursor = Cursors.Hand; }
        public void Add(string icon, string title, string sub) { Item i = new Item(); i.Icon = icon; i.Title = title; i.Sub = sub; Items.Add(i); }
        public int Rows { get { return (Items.Count + Columns - 1) / Columns; } }
        public int TileH { get { return ShowSub ? S(58) : S(42); } }
        public int Needed { get { return Rows * TileH + (Rows - 1) * S(8); } }

        Rectangle Tile(int i)
        {
            int gap = S(8), w = (Width - gap * (Columns - 1)) / Columns;
            return new Rectangle((i % Columns) * (w + gap), (i / Columns) * (TileH + gap), w, TileH);
        }

        int At(Point p) { for (int i = 0; i < Items.Count; i++) if (Tile(i).Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = At(e.Location); if (h != hot) { hot = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            int i = At(e.Location); if (i < 0 || i == Index) return;
            Index = i; Invalidate(); if (Changed != null) Changed();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            for (int i = 0; i < Items.Count; i++)
            {
                Rectangle r = Tile(i); Item it = Items[i]; bool on = i == Index;
                RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
                Ui.FillRound(g, rf, S(10), on ? Ui.Mix(C.Panel, C.Accent, 0.16f) : hot == i ? C.Control : C.Panel);
                Ui.StrokeRound(g, rf, S(10), on ? C.Accent : Ui.Mix(C.Panel, C.Text, 0.08f), on ? S(1.5f) : 1);
                int x = r.X + S(12);
                Ui.DrawIcon(g, it.Icon, 11, new Rectangle(x, r.Y, S(22), r.Height), on ? C.AccentHi : C.Sub);
                x += S(30);
                if (ShowSub && it.Sub.Length > 0)
                {
                    Ui.TextLeft(g, it.Title, Ui.F(10, 4), new Rectangle(x, r.Y + S(9), r.Right - x - S(6), S(22)), C.Text);
                    Ui.TextLeft(g, it.Sub, Ui.F(8.5f, 0), new Rectangle(x, r.Y + S(30), r.Right - x - S(6), S(18)), C.Sub);
                }
                else Ui.TextLeft(g, it.Title, Ui.F(10, 4), new Rectangle(x, r.Y, r.Right - x - S(6), r.Height), C.Text);
            }
        }
    }

    class ShadeBar : Control
    {
        readonly bool right;
        public ShadeBar(bool rightSide)
        {
            right = rightSide;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(14, 12, 22);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(BackColor);
            using (Pen p = new Pen(Color.FromArgb(14, 255, 255, 255), 1))
                for (int x = -Height; x < Width; x += Ui.S(12)) g.DrawLine(p, x, Height, x + Height, 0);
            int w = Ui.S(2);
            using (SolidBrush b = new SolidBrush(C.Accent)) g.FillRectangle(b, right ? 0 : Width - w, 0, w, Height);
        }
    }
}
