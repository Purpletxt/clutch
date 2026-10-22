using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Clutch
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        void RegisterEndpointNotificationCallback(IntPtr client);
        void UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        void GetCount(out int count);
        void Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        void OpenPropertyStore(int access, out IPropertyStore props);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out int state);
    }

    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid fmtid; public int pid; }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out int count);
        void GetAt(int index, out PropertyKey key);
        void GetValue(ref PropertyKey key, IntPtr pv);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        void GetBufferSize(out int frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out int padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long defaultPeriod, out long minPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr handle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);
        void ReleaseBuffer(int frames);
        void GetNextPacketSize(out int frames);
    }

    class AudioDevice { public string Id = "", Name = ""; }

    static class AudioDevices
    {
        public static List<AudioDevice> List(int flow)
        {
            List<AudioDevice> list = new List<AudioDevice>();
            try
            {
                IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                IMMDeviceCollection col; en.EnumAudioEndpoints(flow, 1, out col);
                int n; col.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IMMDevice d; col.Item(i, out d);
                    AudioDevice a = new AudioDevice(); d.GetId(out a.Id); a.Name = FriendlyName(d);
                    list.Add(a);
                    Marshal.ReleaseComObject(d);
                }
                Marshal.ReleaseComObject(col); Marshal.ReleaseComObject(en);
            }
            catch (Exception ex) { Log.Error("Audio devices", ex); }
            return list;
        }

        public static string FriendlyName(IMMDevice d)
        {
            IntPtr pv = Marshal.AllocCoTaskMem(32);
            try
            {
                for (int i = 0; i < 32; i++) Marshal.WriteByte(pv, i, 0);
                IPropertyStore ps; d.OpenPropertyStore(0, out ps);
                PropertyKey k = new PropertyKey(); k.fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"); k.pid = 14;
                ps.GetValue(ref k, pv);
                string name = Marshal.ReadInt16(pv) == 31 ? Marshal.PtrToStringUni(Marshal.ReadIntPtr(pv, 8)) : "";
                Native.PropVariantClear(pv);
                Marshal.ReleaseComObject(ps);
                return name ?? "";
            }
            catch { return ""; }
            finally { Marshal.FreeCoTaskMem(pv); }
        }
    }

    class AudioSource
    {
        public readonly bool Loopback; public readonly string Label;
        string deviceId; int seconds;
        Thread thread; volatile bool running;
        readonly object gate = new object();
        short[] ring; long written; int capacity; double t0 = -1; public int Rate;
        public volatile float Level;
        public volatile string Error = "";
        public volatile string DeviceName = "";

        public AudioSource(bool loopback, string label) { Loopback = loopback; Label = label; }

        public bool Running { get { return running; } }

        int targetPid; bool exclude;
        public int Pid { get { return targetPid; } }

        public void StartProcess(int pid, bool excludeTree, int bufferSeconds)
        {
            Stop();
            targetPid = pid; exclude = excludeTree; deviceId = ""; seconds = bufferSeconds + 10;
            running = true; Error = "";
            thread = new Thread(Loop); thread.IsBackground = true; thread.Name = "Audio " + Label;
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Start();
        }

        public bool Covers(double t) { lock (gate) return ring != null && t0 >= 0 && t0 <= t + 0.25; }

        public void Start(string id, int bufferSeconds)
        {
            Stop();
            targetPid = 0;
            deviceId = id ?? ""; seconds = bufferSeconds + 10;
            running = true; Error = "";
            thread = new Thread(Loop); thread.IsBackground = true; thread.Name = "Audio " + Label;
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            if (thread != null) { thread.Join(1500); thread = null; }
            lock (gate) { ring = null; written = 0; t0 = -1; Rate = 0; }
            Level = 0;
        }

        static double Now() { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; }

        void Loop()
        {
            while (running)
            {
                try { if (targetPid != 0) ProcessSession(); else CaptureSession(); }
                catch (Exception ex)
                {
                    Error = ex.Message; Level = 0;
                    Log.Info("Audio " + Label, ex.Message);
                }
                for (int i = 0; i < 20 && running; i++) Thread.Sleep(50);
            }
        }

        unsafe void CaptureSession()
        {
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
            IMMDevice dev = null; IAudioClient ac = null; IAudioCaptureClient cap = null;
            int flow = Loopback ? 0 : 1;
            try
            {
                if (deviceId == "") en.GetDefaultAudioEndpoint(flow, 0, out dev);
                else en.GetDevice(deviceId, out dev);
                string usedId; dev.GetId(out usedId);
                DeviceName = AudioDevices.FriendlyName(dev);

                Guid iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"); object o;
                dev.Activate(ref iidClient, 23, IntPtr.Zero, out o); ac = (IAudioClient)o;
                IntPtr fmt; ac.GetMixFormat(out fmt);
                int tag = Marshal.ReadInt16(fmt) & 0xFFFF, ch = Marshal.ReadInt16(fmt, 2), rate = Marshal.ReadInt32(fmt, 4);
                int block = Marshal.ReadInt16(fmt, 12), bits = Marshal.ReadInt16(fmt, 14);
                bool isFloat = tag == 3;
                if (tag == 0xFFFE) { byte[] g = new byte[16]; Marshal.Copy(fmt + 24, g, 0, 16); isFloat = new Guid(g) == new Guid("00000003-0000-0010-8000-00aa00389b71"); }
                try { ac.Initialize(0, Loopback ? 0x00020000 : 0, 2000000, 0, fmt, IntPtr.Zero); }
                finally { Marshal.FreeCoTaskMem(fmt); }
                Guid iidCap = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
                ac.GetService(ref iidCap, out o); cap = (IAudioCaptureClient)o;

                lock (gate)
                {
                    if (ring == null || Rate != rate) { capacity = rate * seconds; ring = new short[capacity * 2]; written = 0; t0 = -1; Rate = rate; }
                }
                ac.Start(); Error = "";
                short[] tmp = new short[0];
                double lastDefaultCheck = Now(); float peak = 0; double peakAt = Now();

                while (running)
                {
                    Thread.Sleep(10);
                    int packet; cap.GetNextPacketSize(out packet);
                    while (packet > 0 && running)
                    {
                        IntPtr data; int frames, flags; long devPos, qpc;
                        cap.GetBuffer(out data, out frames, out flags, out devPos, out qpc);
                        double t = (flags & 4) != 0 || qpc == 0 ? Now() : qpc / 1e7;
                        if (tmp.Length < frames * 2) tmp = new short[frames * 2];
                        bool silent = (flags & 2) != 0;
                        byte* p = (byte*)data;
                        for (int f = 0; f < frames; f++)
                        {
                            float l = 0, r = 0;
                            if (!silent)
                            {
                                byte* fp = p + f * block;
                                float s0 = Sample(fp, 0, bits, isFloat), s1 = ch > 1 ? Sample(fp, 1, bits, isFloat) : s0;
                                l = s0; r = s1;
                                if (ch >= 3) { float c = Sample(fp, 2, bits, isFloat) * 0.6f; l += c; r += c; }
                                if (ch >= 6) { l += Sample(fp, 4, bits, isFloat) * 0.5f; r += Sample(fp, 5, bits, isFloat) * 0.5f; }
                            }
                            float a = Math.Max(Math.Abs(l), Math.Abs(r)); if (a > peak) peak = a;
                            tmp[f * 2] = Clamp(l); tmp[f * 2 + 1] = Clamp(r);
                        }
                        cap.ReleaseBuffer(frames);
                        Put(tmp, frames, t);
                        cap.GetNextPacketSize(out packet);
                    }
                    double now = Now();
                    if (now - peakAt > 0.1) { Level = Math.Min(1f, peak); peak = 0; peakAt = now; }
                    if (deviceId == "" && now - lastDefaultCheck > 2)
                    {
                        lastDefaultCheck = now;
                        IMMDevice def; en.GetDefaultAudioEndpoint(flow, 0, out def);
                        string defId; def.GetId(out defId); Marshal.ReleaseComObject(def);
                        if (defId != usedId) break;
                    }
                }
                ac.Stop();
            }
            finally
            {
                if (cap != null) Marshal.ReleaseComObject(cap);
                if (ac != null) Marshal.ReleaseComObject(ac);
                if (dev != null) Marshal.ReleaseComObject(dev);
                Marshal.ReleaseComObject(en);
            }
        }

        void ProcessSession()
        {
            IAudioClient ac = ProcessLoopback.Activate(targetPid, exclude);
            IAudioCaptureClient cap = null; IntPtr fmt = IntPtr.Zero;
            AutoResetEvent ready = new AutoResetEvent(false);
            try
            {
                int rate = 48000;
                fmt = Marshal.AllocCoTaskMem(18);
                Marshal.WriteInt16(fmt, 0, 1); Marshal.WriteInt16(fmt, 2, 2); Marshal.WriteInt32(fmt, 4, rate); Marshal.WriteInt32(fmt, 8, rate * 4);
                Marshal.WriteInt16(fmt, 12, 4); Marshal.WriteInt16(fmt, 14, 16); Marshal.WriteInt16(fmt, 16, 0);
                ac.Initialize(0, 0x00020000 | 0x00040000 | 0x08000000 | unchecked((int)0x80000000), 2000000, 0, fmt, IntPtr.Zero);
                ac.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
                Guid iidCap = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"); object o;
                ac.GetService(ref iidCap, out o); cap = (IAudioCaptureClient)o;
                DeviceName = exclude ? "Other apps" : "Game";
                lock (gate)
                {
                    if (ring == null || Rate != rate) { capacity = rate * seconds; ring = new short[capacity * 2]; written = 0; t0 = -1; Rate = rate; }
                }
                ac.Start(); Error = "";
                short[] tmp = new short[0];
                float peak = 0; double peakAt = Now();
                while (running)
                {
                    ready.WaitOne(100);
                    int packet; cap.GetNextPacketSize(out packet);
                    while (packet > 0 && running)
                    {
                        IntPtr data; int frames, flags; long devPos, qpc;
                        cap.GetBuffer(out data, out frames, out flags, out devPos, out qpc);
                        double t = (flags & 4) != 0 || qpc == 0 ? Now() : qpc / 1e7;
                        if (tmp.Length < frames * 2) tmp = new short[frames * 2];
                        if ((flags & 2) != 0) Array.Clear(tmp, 0, frames * 2);
                        else Marshal.Copy(data, tmp, 0, frames * 2);
                        cap.ReleaseBuffer(frames);
                        for (int i = 0; i < frames * 2; i += 8) { float a = Math.Abs(tmp[i] / 32768f); if (a > peak) peak = a; }
                        Put(tmp, frames, t);
                        cap.GetNextPacketSize(out packet);
                    }
                    double now = Now();
                    if (now - peakAt > 0.1) { Level = Math.Min(1f, peak); peak = 0; peakAt = now; }
                }
                ac.Stop();
            }
            finally
            {
                if (cap != null) Marshal.ReleaseComObject(cap);
                Marshal.ReleaseComObject(ac);
                if (fmt != IntPtr.Zero) Marshal.FreeCoTaskMem(fmt);
                ready.Dispose();
            }
        }

        static unsafe float Sample(byte* frame, int c, int bits, bool isFloat)
        {
            if (isFloat) return ((float*)frame)[c];
            if (bits == 16) return ((short*)frame)[c] / 32768f;
            if (bits == 24) { byte* b = frame + c * 3; return ((b[0] << 8 | b[1] << 16 | b[2] << 24) >> 8) / 8388608f; }
            if (bits == 32) return ((int*)frame)[c] / 2147483648f;
            return 0;
        }

        static short Clamp(float v) { v *= 32767f; return (short)(v > 32767 ? 32767 : v < -32768 ? -32768 : v); }

        void Put(short[] src, int frames, double t)
        {
            lock (gate)
            {
                if (ring == null) return;
                if (t0 < 0) t0 = t - (double)written / Rate;
                long expected = (long)Math.Round((t - t0) * Rate);
                int skip = 0;
                if (expected > written + Rate / 50)
                {
                    long gap = Math.Min(expected - written, capacity);
                    for (long i = 0; i < gap; i++) { long slot = (written + i) % capacity; ring[slot * 2] = 0; ring[slot * 2 + 1] = 0; }
                    written = expected;
                }
                else if (expected < written - Rate / 16)
                    skip = (int)Math.Min(frames, written - expected);
                for (int f = skip; f < frames; f++)
                {
                    long slot = written % capacity;
                    ring[slot * 2] = src[f * 2]; ring[slot * 2 + 1] = src[f * 2 + 1];
                    written++;
                }
            }
        }

        public bool WriteWav(double start, double end, string path)
        {
            short[] buf; int rate;
            lock (gate)
            {
                if (ring == null || t0 < 0 || Rate == 0) return false;
                rate = Rate;
                long a = (long)Math.Round((start - t0) * rate), b = (long)Math.Round((end - t0) * rate);
                if (b <= a) return false;
                long n = b - a; buf = new short[n * 2];
                long oldest = Math.Max(0, written - capacity);
                for (long i = 0; i < n; i++)
                {
                    long f = a + i;
                    if (f < oldest || f >= written) continue;
                    long slot = f % capacity; buf[i * 2] = ring[slot * 2]; buf[i * 2 + 1] = ring[slot * 2 + 1];
                }
            }
            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                int bytes = buf.Length * 2;
                w.Write(0x46464952); w.Write(36 + bytes); w.Write(0x45564157); w.Write(0x20746D66);
                w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
                w.Write(0x61746164); w.Write(bytes);
                byte[] raw = new byte[bytes]; Buffer.BlockCopy(buf, 0, raw, 0, bytes); w.Write(raw);
            }
            return true;
        }
    }

    static class ProcessLoopback
    {
        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ICompletion { void ActivateCompleted(IOperation op); }

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IOperation { void GetActivateResult([MarshalAs(UnmanagedType.Error)] out int hr, [MarshalAs(UnmanagedType.IUnknown)] out object iface); }

        [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAgileObject { }

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, IntPtr parms, ICompletion handler, out IOperation op);

        class Handler : ICompletion, IAgileObject
        {
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public void ActivateCompleted(IOperation op) { try { Done.Set(); } catch { } }
        }

        static int build = -1;
        public static bool Supported
        {
            get
            {
                if (build < 0)
                {
                    build = 0;
                    try { using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) int.TryParse((k.GetValue("CurrentBuildNumber") ?? "0").ToString(), out build); } catch { }
                }
                return build >= 20348;
            }
        }

        public static IAudioClient Activate(int pid, bool exclude)
        {
            IntPtr p = Marshal.AllocHGlobal(12), pv = Marshal.AllocHGlobal(24);
            Handler h = new Handler();
            try
            {
                Marshal.WriteInt32(p, 0, 1); Marshal.WriteInt32(p, 4, pid); Marshal.WriteInt32(p, 8, exclude ? 1 : 0);
                for (int i = 0; i < 24; i++) Marshal.WriteByte(pv, i, 0);
                Marshal.WriteInt16(pv, 0, 65); Marshal.WriteInt32(pv, 8, 12); Marshal.WriteIntPtr(pv, 16, p);
                IOperation op;
                ActivateAudioInterfaceAsync(@"VAD\Process_Loopback", typeof(IAudioClient).GUID, pv, h, out op);
                if (!h.Done.WaitOne(5000)) throw new Exception("Game audio didn't start in time");
                int hr; object o; op.GetActivateResult(out hr, out o);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                Marshal.ReleaseComObject(op);
                return (IAudioClient)o;
            }
            finally { Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(pv); h.Done.Close(); }
        }
    }
}
