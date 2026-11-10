using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Speech.Recognition;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Clutch
{
    static class VoiceAddon
    {
        public const string EngineUrl = "https://github.com/alphacep/vosk-api/releases/download/v0.3.45/vosk-win64-0.3.45.zip";
        public const string EngineSha = "f1dcc9cca460630f81ea8f71794f69c80bed6556d2a4e6237b5785e1d2dff34b";
        public const long EngineSize = 14882445;
        public const string ModelUrl = "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip";
        public const string ModelSha = "30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498";
        public const long ModelSize = 41205931;
        static readonly string[] Libs = { "libvosk.dll", "libstdc++-6.dll", "libwinpthread-1.dll", "libgcc_s_seh-1.dll" };

        public static string Dir
        {
            get
            {
#if DEV
                string o = Environment.GetEnvironmentVariable("CLUTCH_VOICE_DIR"); if (!string.IsNullOrEmpty(o)) return o;
#endif
                return Path.Combine(Paths.Local, "voice");
            }
        }
        public static string ModelDir { get { return Path.Combine(Dir, "model"); } }
        public static bool Ready
        {
            get
            {
                foreach (string l in Libs) if (!File.Exists(Path.Combine(Dir, l))) return false;
                return File.Exists(Path.Combine(ModelDir, "am", "final.mdl"));
            }
        }

        public static volatile bool Downloading;
        public static volatile int Percent;
        public static string Error = "";
        static volatile bool cancel;

        public static void Start(Control ui, Action done)
        {
            if (Downloading) return;
            Downloading = true; Percent = 0; Error = ""; cancel = false;
            Thread t = new Thread(delegate ()
            {
                string err = "";
                try { Download(); }
                catch (OperationCanceledException) { err = "Download cancelled."; }
                catch (Exception ex) { err = ex is WebException ? "Couldn't download the voice add-on. Check your internet connection and try again." : ex.Message; Log.Error("Voice add-on download", ex); }
                try { ui.BeginInvoke((MethodInvoker)delegate { Downloading = false; Error = err; if (done != null) done(); }); } catch { }
            });
            t.IsBackground = true; t.Start();
        }

        public static void Cancel() { cancel = true; }

        public static void Remove()
        {
            Voice.Stop();
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch (Exception ex) { Log.Info("Voice add-on", "Couldn't remove: " + ex.Message); }
        }

        static void Download()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            long total = EngineSize + ModelSize;
            string parent = Path.GetDirectoryName(Dir); Directory.CreateDirectory(parent);
            string engineZip = Path.Combine(parent, "voice-engine.zip"), modelZip = Path.Combine(parent, "voice-model.zip");
            try
            {
                Get(EngineUrl, EngineSha, engineZip, 0, total);
                Get(ModelUrl, ModelSha, modelZip, EngineSize, total);
                string tmp = Dir + ".new";
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(tmp);
                using (ZipArchive za = ZipFile.OpenRead(engineZip))
                    foreach (ZipArchiveEntry e in za.Entries)
                        if (Array.IndexOf(Libs, e.Name) >= 0) e.ExtractToFile(Path.Combine(tmp, e.Name), true);
                string modelTmp = Path.Combine(tmp, "model");
                using (ZipArchive za = ZipFile.OpenRead(modelZip))
                    foreach (ZipArchiveEntry e in za.Entries)
                    {
                        string name = e.FullName.Replace('\\', '/');
                        int cut = name.IndexOf('/'); if (cut < 0) continue;
                        string rel = name.Substring(cut + 1);
                        if (rel.Length == 0 || rel.EndsWith("/") || rel.Contains("..")) continue;
                        string dst = Path.Combine(modelTmp, rel.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        e.ExtractToFile(dst, true);
                    }
                File.WriteAllText(Path.Combine(tmp, "LICENSE.txt"), "Voice add-on: Vosk speech recognition 0.3.45 and the vosk-model-small-en-us-0.15 model by Alpha Cephei, Apache License 2.0. https://github.com/alphacep/vosk-api\r\nIncludes MinGW runtime libraries (GCC runtime library exception).");
                Voice.Stop();
                if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
                Directory.Move(tmp, Dir);
                if (!Ready) throw new Exception("The voice add-on didn't install properly. Please try again.");
            }
            finally
            {
                try { File.Delete(engineZip); } catch { }
                try { File.Delete(modelZip); } catch { }
            }
        }

        static void Get(string url, string sha, string file, long offset, long total)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Clutch/" + AppInfo.Version;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream src = resp.GetResponseStream())
            using (FileStream dst = File.Create(file))
            {
                long done = 0; byte[] buf = new byte[1 << 16]; int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancel) throw new OperationCanceledException();
                    dst.Write(buf, 0, n); done += n;
                    Percent = (int)Math.Min(100, (offset + done) * 100 / Math.Max(1, total));
                }
            }
            string actual;
            using (FileStream fs = File.OpenRead(file)) using (SHA256 s = SHA256.Create())
                actual = BitConverter.ToString(s.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            if (actual != sha) throw new Exception("The voice add-on download didn't match its expected fingerprint, so it was thrown away. Please try again.");
        }
    }

    static class Voice
    {
        public static string State = "";
        static SpeechRecognitionEngine basic;
        static Thread addon; static volatile bool addonRun;
        static string running = "", runningPhrase = "";
        static MainForm main;
        static DateTime last = DateTime.MinValue;

        public static string Normalize(string s)
        {
            string t = Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z' ]", " ");
            return Regex.Replace(t, "\\s+", " ").Trim();
        }

        public static string Phrase { get { string p = Normalize(Settings.Cur.VoicePhrase); return p.Length > 0 ? p : "clutch clip that"; } }

        public static void Sync(MainForm m)
        {
            main = m;
            bool want = Settings.Cur.VoiceClips && Recorder.Now == Recorder.State.Recording && !Program.DevRun;
            string mode = !want ? "" : VoiceAddon.Ready && !VoiceAddon.Downloading ? "addon" : "basic";
            if (mode == running && Phrase == runningPhrase) return;
            Stop();
            if (mode == "basic") StartBasic(Phrase, null);
            else if (mode == "addon") StartAddon(Phrase);
            else State = Settings.Cur.VoiceClips ? "Listens while Clutch is recording." : "";
        }

        public static void Stop()
        {
            SpeechRecognitionEngine b = basic; basic = null;
            if (b != null) { try { b.RecognizeAsyncCancel(); } catch { } try { b.Dispose(); } catch { } }
            addonRun = false;
            Thread a = addon; addon = null;
            if (a != null && a != Thread.CurrentThread) a.Join(1500);
            running = ""; runningPhrase = "";
        }

        static void Heard()
        {
            if ((DateTime.Now - last).TotalSeconds < 4) return;
            last = DateTime.Now;
            MainForm m = main; if (m == null) return;
            try { m.BeginInvoke((MethodInvoker)m.SaveClip); } catch { }
        }

        static RecognizerInfo English()
        {
            RecognizerInfo any = null;
            try
            {
                foreach (RecognizerInfo r in SpeechRecognitionEngine.InstalledRecognizers())
                {
                    if (r.Culture.TwoLetterISOLanguageName == "en") { if (r.Culture.Name == "en-US" || any == null || any.Culture.TwoLetterISOLanguageName != "en") any = r; }
                    else if (any == null) any = r;
                }
            }
            catch { }
            return any;
        }

        public static SpeechRecognitionEngine MakeBasic(string phrase, out string error)
        {
            error = "";
            RecognizerInfo info = English();
            if (info == null) { error = "Windows speech recognition isn't installed on this PC. Get the voice add-on below instead."; return null; }
            SpeechRecognitionEngine e = new SpeechRecognitionEngine(info);
            GrammarBuilder gb = new GrammarBuilder(phrase); gb.Culture = info.Culture;
            e.LoadGrammar(new Grammar(gb));
            return e;
        }

        static void StartBasic(string phrase, string why)
        {
            try
            {
                string err;
                SpeechRecognitionEngine e = MakeBasic(phrase, out err);
                if (e == null) { State = err; return; }
                e.SpeechRecognized += delegate (object s, SpeechRecognizedEventArgs a) { if (a.Result != null && a.Result.Confidence >= 0.6f) Heard(); };
                e.SetInputToDefaultAudioDevice();
                e.RecognizeAsync(RecognizeMode.Multiple);
                basic = e; running = "basic"; runningPhrase = phrase;
                State = why ?? "Listening with Windows speech recognition.";
            }
            catch (Exception ex) { State = "Voice clips couldn't start: " + ex.Message; Log.Info("Voice clips", ex.Message); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetDllDirectory(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string path);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern void vosk_set_log_level(int level);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr vosk_model_new(byte[] path);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern void vosk_model_free(IntPtr model);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern int vosk_model_find_word(IntPtr model, byte[] word);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr vosk_recognizer_new_grm(IntPtr model, float rate, byte[] grammar);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern int vosk_recognizer_accept_waveform(IntPtr rec, byte[] data, int length);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr vosk_recognizer_result(IntPtr rec);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr vosk_recognizer_final_result(IntPtr rec);
        [DllImport("libvosk.dll", CallingConvention = CallingConvention.Cdecl)] static extern void vosk_recognizer_free(IntPtr rec);

        static byte[] Z(string s) { return Encoding.UTF8.GetBytes(s + "\0"); }
        static bool loaded;

        public static bool LoadAddon()
        {
            if (loaded) return true;
            SetDllDirectory(VoiceAddon.Dir);
            foreach (string l in new[] { "libgcc_s_seh-1.dll", "libwinpthread-1.dll", "libstdc++-6.dll", "libvosk.dll" })
                if (LoadLibrary(Path.Combine(VoiceAddon.Dir, l)) == IntPtr.Zero) return false;
            vosk_set_log_level(-1);
            loaded = true; return true;
        }

        public class AddonRecognizer : IDisposable
        {
            IntPtr model, rec; public string Missing = "";
            public AddonRecognizer(string phrase)
            {
                if (!LoadAddon()) throw new Exception("The voice add-on couldn't load. Try removing and downloading it again.");
                model = vosk_model_new(Z(VoiceAddon.ModelDir));
                if (model == IntPtr.Zero) throw new Exception("The voice add-on's model couldn't load. Try removing and downloading it again.");
                foreach (string w in phrase.Split(' ')) if (vosk_model_find_word(model, Z(w)) < 0) Missing += (Missing.Length > 0 ? ", " : "") + w;
                if (Missing.Length > 0) return;
                rec = vosk_recognizer_new_grm(model, 16000f, Z("[\"" + phrase + "\", \"[unk]\"]"));
                if (rec == IntPtr.Zero) throw new Exception("The voice add-on couldn't start.");
            }
            public string Feed(byte[] pcm, int length)
            {
                if (rec == IntPtr.Zero) return null;
                return vosk_recognizer_accept_waveform(rec, pcm, length) != 0 ? Text(vosk_recognizer_result(rec)) : null;
            }
            public string Finish() { return rec == IntPtr.Zero ? null : Text(vosk_recognizer_final_result(rec)); }
            static string Text(IntPtr json)
            {
                string s = json == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(json);
                Match m = Regex.Match(s ?? "", "\"text\"\\s*:\\s*\"([^\"]*)\"");
                return m.Success ? m.Groups[1].Value.Trim() : "";
            }
            public void Dispose()
            {
                if (rec != IntPtr.Zero) { vosk_recognizer_free(rec); rec = IntPtr.Zero; }
                if (model != IntPtr.Zero) { vosk_model_free(model); model = IntPtr.Zero; }
            }
        }

        static void StartAddon(string phrase)
        {
            addonRun = true; running = "addon"; runningPhrase = phrase;
            State = "Starting the voice add-on\u2026";
            addon = new Thread(delegate () { AddonLoop(phrase); });
            addon.IsBackground = true; addon.Name = "Voice add-on"; addon.Priority = ThreadPriority.BelowNormal;
            addon.Start();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)] struct WaveFormat { public short Tag, Channels; public int Rate, Bytes; public short Align, Bits, Extra; }
        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, int device, ref WaveFormat fmt, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);

        static void AddonLoop(string phrase)
        {
            AddonRecognizer r = null; IntPtr wave = IntPtr.Zero;
            const int Count = 4, Size = 3200, Hdr = 48;
            IntPtr[] hdrs = new IntPtr[Count], data = new IntPtr[Count];
            try
            {
                r = new AddonRecognizer(phrase);
                if (r.Missing.Length > 0)
                {
                    string missing = r.Missing; r.Dispose(); r = null; addonRun = false;
                    string why = "The add-on doesn't know \"" + missing + "\", so it's using Windows speech instead. ";
                    MainForm m = main;
                    if (m != null) m.BeginInvoke((MethodInvoker)delegate { if (running == "addon" && runningPhrase == phrase) { running = ""; StartBasic(phrase, why); } });
                    return;
                }
                WaveFormat f = new WaveFormat(); f.Tag = 1; f.Channels = 1; f.Rate = 16000; f.Bits = 16; f.Align = 2; f.Bytes = 32000;
                int mm = waveInOpen(out wave, -1, ref f, IntPtr.Zero, IntPtr.Zero, 0);
                if (mm != 0) throw new Exception("No microphone is available (error " + mm + ").");
                for (int i = 0; i < Count; i++)
                {
                    data[i] = Marshal.AllocHGlobal(Size); hdrs[i] = Marshal.AllocHGlobal(Hdr);
                    for (int k = 0; k < Hdr; k++) Marshal.WriteByte(hdrs[i], k, 0);
                    Marshal.WriteIntPtr(hdrs[i], 0, data[i]); Marshal.WriteInt32(hdrs[i], 8, Size);
                    waveInPrepareHeader(wave, hdrs[i], Hdr); waveInAddBuffer(wave, hdrs[i], Hdr);
                }
                waveInStart(wave);
                State = "Listening with the voice add-on.";
                byte[] buf = new byte[Size]; int next = 0;
                while (addonRun)
                {
                    IntPtr h = hdrs[next];
                    if ((Marshal.ReadInt32(h, 24) & 1) == 0) { Thread.Sleep(20); continue; }
                    int got = Marshal.ReadInt32(h, 12);
                    if (got > 0) { Marshal.Copy(data[next], buf, 0, got); string t = r.Feed(buf, got); if (t != null && t == phrase) Heard(); }
                    Marshal.WriteInt32(h, 24, Marshal.ReadInt32(h, 24) & ~1);
                    waveInAddBuffer(wave, h, Hdr);
                    next = (next + 1) % Count;
                }
            }
            catch (Exception ex) { State = "Voice clips couldn't start: " + ex.Message; Log.Info("Voice add-on", ex.Message); }
            finally
            {
                if (wave != IntPtr.Zero) { waveInReset(wave); for (int i = 0; i < Count; i++) if (hdrs[i] != IntPtr.Zero) waveInUnprepareHeader(wave, hdrs[i], Hdr); waveInClose(wave); }
                for (int i = 0; i < Count; i++) { if (hdrs[i] != IntPtr.Zero) Marshal.FreeHGlobal(hdrs[i]); if (data[i] != IntPtr.Zero) Marshal.FreeHGlobal(data[i]); }
                if (r != null) r.Dispose();
            }
        }
    }
}
