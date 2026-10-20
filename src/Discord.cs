using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Threading;

namespace Clutch
{
    static class DiscordPresence
    {
        public const string AppId = "1561549818202030120";

        static Thread worker;
        static readonly AutoResetEvent wake = new AutoResetEvent(false);
        static volatile bool quit;
        static string wantGame = "", wantExe = ""; static long wantStart;
        static readonly object gate = new object();

        public static void Update(string game, string exe, DateTime since)
        {
            if (AppId.Length == 0) return;
            bool show = Settings.Cur.DiscordStatus && game.Length > 0;
            lock (gate)
            {
                string g = show ? game : "", e = show ? exe : "";
                long s = show ? (long)(since.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalSeconds : 0;
                if (g == wantGame && e == wantExe && (s == wantStart || g.Length == 0)) return;
                wantGame = g; wantExe = e; wantStart = s;
            }
            if (worker == null && show) { worker = new Thread(Run); worker.IsBackground = true; worker.Name = "Discord"; worker.Start(); }
            wake.Set();
        }

        public static void Stop() { quit = true; wake.Set(); }

        static void Run()
        {
            NamedPipeClientStream pipe = null; string shown = null;
            while (!quit)
            {
                string game, exe; long start;
                lock (gate) { game = wantGame; exe = wantExe; start = wantStart; }
                string key = game + "|" + start;
                try
                {
                    if (pipe == null && game.Length > 0) { pipe = Connect(); shown = null; }
                    if (pipe != null && key != shown)
                    {
                        Send(pipe, 1, Activity(game, exe, start, !noName));
                        if (Read(pipe).Contains("\"evt\":\"ERROR\"") && !noName)
                        {
                            noName = true;
                            Send(pipe, 1, Activity(game, exe, start, false)); Read(pipe);
                        }
                        shown = key;
                    }
                }
                catch (Exception ex) { Log.Info("Discord", ex.Message); Close(ref pipe); }
                if (pipe != null && game.Length == 0) { Close(ref pipe); shown = null; }
                wake.WaitOne(pipe == null && game.Length > 0 ? 15000 : Timeout.Infinite);
            }
            Close(ref pipe);
        }

#if DEV
        public static string Ping(string appId)
        {
            for (int i = 0; i < 10; i++)
                using (NamedPipeClientStream p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    try { p.Connect(200); } catch { continue; }
                    Send(p, 0, "{\"v\":1,\"client_id\":\"" + appId + "\"}");
                    byte[] head = new byte[8]; if (!ReadAll(p, head)) return "pipe " + i + ": no reply";
                    byte[] body = new byte[BitConverter.ToInt32(head, 4)]; ReadAll(p, body);
                    return "pipe " + i + ", op " + BitConverter.ToInt32(head, 0) + ": " + Encoding.UTF8.GetString(body);
                }
            return "Discord isn't running";
        }
#endif

        static NamedPipeClientStream Connect()
        {
            for (int i = 0; i < 10; i++)
            {
                NamedPipeClientStream p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    p.Connect(200);
                    Send(p, 0, "{\"v\":1,\"client_id\":\"" + AppId + "\"}");
                    Read(p);
                    return p;
                }
                catch { p.Dispose(); }
            }
            return null;
        }

        static void Close(ref NamedPipeClientStream p) { if (p != null) { try { p.Dispose(); } catch { } p = null; } }

        static void Send(Stream s, int op, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            byte[] frame = new byte[8 + body.Length];
            BitConverter.GetBytes(op).CopyTo(frame, 0); BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
            body.CopyTo(frame, 8);
            s.Write(frame, 0, frame.Length); s.Flush();
        }

        static string Read(Stream s)
        {
            byte[] head = new byte[8];
            if (!ReadAll(s, head)) throw new IOException("Discord didn't answer");
            int len = BitConverter.ToInt32(head, 4);
            if (len <= 0 || len >= 1 << 20) return "";
            byte[] body = new byte[len]; ReadAll(s, body);
            string reply = Encoding.UTF8.GetString(body);
            if (BitConverter.ToInt32(head, 0) == 2 || reply.Contains("\"evt\":\"ERROR\"")) Log.Info("Discord", reply.Length > 300 ? reply.Substring(0, 300) : reply);
            return reply;
        }

        static bool ReadAll(Stream s, byte[] buf)
        {
            int got = 0;
            while (got < buf.Length)
            {
                System.Threading.Tasks.Task<int> t = s.ReadAsync(buf, got, buf.Length - got);
                if (!t.Wait(5000) || t.Result <= 0) return false;
                got += t.Result;
            }
            return true;
        }

        static bool noName;

        static string Activity(string game, string exe, long start, bool withName)
        {
            string icon = GameArt.Url(exe);
            StringBuilder a = new StringBuilder();
            a.Append("{\"cmd\":\"SET_ACTIVITY\",\"nonce\":\"").Append(Guid.NewGuid().ToString("N")).Append("\",\"args\":{\"pid\":").Append(Process.GetCurrentProcess().Id);
            a.Append(",\"activity\":{\"type\":0");
            if (withName) a.Append(",\"name\":").Append(J(game + " with Clutch"));
            a.Append(",\"details\":").Append(J("Clipping " + game + " with Clutch"));
            if (start >= 1) a.Append(",\"timestamps\":{\"start\":").Append(start).Append("}");
            a.Append(",\"assets\":{\"large_image\":").Append(J(icon ?? "clutch")).Append(",\"large_text\":").Append(J(game));
            if (icon != null) a.Append(",\"small_image\":\"clutch\",\"small_text\":\"Clutch\"");
            a.Append("},\"instance\":false}}}");
            return a.ToString();
        }

        static string J(string s)
        {
            StringBuilder b = new StringBuilder("\"");
            foreach (char c in s) { if (c == '"' || c == '\\') b.Append('\\').Append(c); else if (c < ' ') b.Append(' '); else b.Append(c); }
            return b.Append('"').ToString();
        }
    }
}
