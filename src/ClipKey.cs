using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Clutch
{
    static class ClipKey
    {
        static IntPtr hook;
        static Native.HookProc proc;
        static MainForm main;
        static volatile bool down, paused, fallbackOn;
        static System.Windows.Forms.Timer watch;
        static int lastPid = -1;
        static Thread thread;
        static ApplicationContext loop;
        static readonly bool selfElevated = IsElevated(Process.GetCurrentProcess().Id);

        public static void Start(MainForm m)
        {
            main = m; paused = false;
            if (Program.DevRun || !m.IsHandleCreated) return;
            if (thread == null) StartHookThread();
            if (watch == null) { watch = new System.Windows.Forms.Timer(); watch.Interval = 1000; watch.Tick += delegate { Tick(); }; watch.Start(); }
            SetFallback(false); lastPid = -1; CheckForeground();
        }

        static void StartHookThread()
        {
            ManualResetEvent ready = new ManualResetEvent(false);
            thread = new Thread(delegate ()
            {
                proc = Hook;
                hook = Native.SetWindowsHookEx(13, proc, Native.GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) Log.Error("Clip key", "Keyboard hook failed (" + Marshal.GetLastWin32Error() + "); using a regular hotkey instead.");
                loop = new ApplicationContext();
                ready.Set();
                Application.Run(loop);
                if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            });
            thread.IsBackground = true; thread.Name = "Clip key"; thread.Priority = ThreadPriority.Highest;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.WaitOne(3000);
        }

        public static void Pause() { paused = true; SetFallback(false); }

        public static void Stop()
        {
            if (watch != null) { watch.Stop(); watch.Dispose(); watch = null; }
            SetFallback(false);
            ApplicationContext l = loop;
            if (l != null && thread != null) { try { l.ExitThread(); } catch { } thread.Join(1000); }
            thread = null; loop = null;
        }

        static IntPtr Hook(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0 && !paused && main != null)
            {
                int msg = w.ToInt32(), vk = Marshal.ReadInt32(l);
                if (vk == Settings.Cur.HotkeyVk)
                {
                    bool isDown = msg == 0x100 || msg == 0x104;
                    if (isDown && !down)
                    {
                        if (!fallbackOn && HeldMods() == Settings.Cur.HotkeyMods)
                        {
                            down = true;
                            try { main.BeginInvoke((MethodInvoker)main.ClipKeyPressed); } catch { }
                        }
                    }
                    else if (!isDown) down = false;
                }
            }
            return Native.CallNextHookEx(hook, code, w, l);
        }

        static bool Held(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }
        static int HeldMods()
        {
            return (Held(0x12) ? 1 : 0) | (Held(0x11) ? 2 : 0) | (Held(0x10) ? 4 : 0) | (Held(0x5B) || Held(0x5C) ? 8 : 0);
        }

        static void Tick()
        {
            if (down && !Held(Settings.Cur.HotkeyVk)) down = false;
            CheckForeground();
        }

        static void CheckForeground()
        {
            if (paused || main == null) return;
            if (hook == IntPtr.Zero) { SetFallback(true); return; }
            int pid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            if (pid == lastPid) return;
            lastPid = pid;
            SetFallback(!selfElevated && pid != 0 && IsElevated(pid));
        }

        static void SetFallback(bool on)
        {
            if (main == null || !main.IsHandleCreated || on == fallbackOn) return;
            if (on) fallbackOn = Native.RegisterHotKey(main.Handle, 1, (uint)Settings.Cur.HotkeyMods | Native.MOD_NOREPEAT, (uint)Settings.Cur.HotkeyVk);
            else { Native.UnregisterHotKey(main.Handle, 1); fallbackOn = false; }
        }

        static bool IsElevated(int pid)
        {
            IntPtr h = Native.OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return true;
            try
            {
                IntPtr tok;
                if (!Native.OpenProcessToken(h, 8, out tok)) return true;
                try { int elev, len; return !Native.GetTokenInformation(tok, 20, out elev, 4, out len) || elev != 0; }
                finally { Native.CloseHandle(tok); }
            }
            finally { Native.CloseHandle(h); }
        }
    }
}
