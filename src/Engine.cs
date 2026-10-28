using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;

namespace OpenMirror
{
    /// <summary>User-facing mirroring options (Wormhole-like subset, engine-agnostic).</summary>
    internal sealed class MirrorOptions
    {
        public string BitRate = "8M";
        public int MaxSize = 1280;
        public int MaxFps = 0;
        public string VideoCodec = "";
        public bool Audio = true;
        public string AudioCodec = "";
        public bool StayAwake = true;
        public bool TurnScreenOff = false;
        public bool AlwaysOnTop = false;
        public bool Fullscreen = false;
        public string Record = "";
        public bool NoWindow = false;
        public int TimeLimitSeconds = 0;
        public string WindowTitle = "OpenMirror";

        public MirrorOptions Clone()
        {
            MirrorOptions c = new MirrorOptions();
            c.BitRate = BitRate; c.MaxSize = MaxSize; c.MaxFps = MaxFps;
            c.VideoCodec = VideoCodec; c.Audio = Audio; c.AudioCodec = AudioCodec;
            c.StayAwake = StayAwake; c.TurnScreenOff = TurnScreenOff;
            c.AlwaysOnTop = AlwaysOnTop; c.Fullscreen = Fullscreen;
            c.Record = Record; c.NoWindow = NoWindow;
            c.TimeLimitSeconds = TimeLimitSeconds; c.WindowTitle = WindowTitle;
            return c;
        }
    }

    /// <summary>
    /// Owns the device-side payload and the local viewer process. The viewer is
    /// launched with an explicit ADB path so no other product's adb server (the
    /// abandoned Wormhole install ships a forked wdb.exe) can interfere.
    /// </summary>
    internal sealed class Engine
    {
        public string ScrcpyExe = "";
        public string EngineDir = "";
        public string AdbExe = "";
        public MirrorOptions Options = new MirrorOptions();

        private readonly Action<string> _log;
        private Process _proc;
        private IntPtr _childWindow = IntPtr.Zero;
        private Native.RECT _floatRect;   // viewer geometry before it was embedded

        public Engine(Action<string> log) { _log = log; }

        public bool Running { get { return _proc != null && !_proc.HasExited; } }
        public IntPtr ChildWindow { get { return _childWindow; } }
        public int ProcessId { get { return _proc == null ? 0 : _proc.Id; } }
        public event EventHandler Exited;

        /// <summary>Locates the engine from an engine root (.\engine) or any directory.</summary>
        public static Engine FromEngineRoot(string root, Action<string> log)
        {
            Engine e = new Engine(log);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) { return e; }
            FileInfo[] exes = new DirectoryInfo(root).GetFiles("scrcpy.exe", SearchOption.AllDirectories);
            if (exes.Length == 0) { return e; }
            e.ScrcpyExe = exes[0].FullName;
            e.EngineDir = exes[0].DirectoryName;
            string adb = Path.Combine(e.EngineDir, "adb.exe");
            if (File.Exists(adb)) { e.AdbExe = adb; }
            return e;
        }

        public string Version()
        {
            if (ScrcpyExe.Length == 0 || !File.Exists(ScrcpyExe)) { return "(engine missing)"; }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(ScrcpyExe, "--version");
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    string outText = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    string first = outText.Replace("\r", "").Split('\n')[0].Trim();
                    return first.Length > 0 ? first : "(unknown)";
                }
            }
            catch (Exception ex) { return "(error: " + ex.Message + ")"; }
        }

        public bool ServerPayloadPresent()
        {
            return EngineDir.Length > 0 && File.Exists(Path.Combine(EngineDir, "scrcpy-server"));
        }

        private static string Quote(string value)
        {
            if (value == null) { return "\"\""; }
            if (value.IndexOf(' ') < 0) { return value; }
            return "\"" + value + "\"";
        }

        /// <summary>Builds the engine command line from the current options.</summary>
        public string BuildArgs(string serial)
        {
            MirrorOptions o = Options;
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(serial)) { sb.Append("-s ").Append(Quote(serial)).Append(' '); }
            sb.Append("--window-title=").Append(Quote(string.IsNullOrEmpty(o.WindowTitle) ? "OpenMirror" : o.WindowTitle)).Append(' ');
            if (!string.IsNullOrEmpty(o.BitRate)) { sb.Append("-b ").Append(o.BitRate).Append(' '); }
            if (o.MaxSize > 0) { sb.Append("-m ").Append(o.MaxSize).Append(' '); }
            if (o.MaxFps > 0) { sb.Append("--max-fps=").Append(o.MaxFps).Append(' '); }
            if (!string.IsNullOrEmpty(o.VideoCodec)) { sb.Append("--video-codec=").Append(o.VideoCodec).Append(' '); }
            if (!o.Audio) { sb.Append("--no-audio "); }
            if (!string.IsNullOrEmpty(o.AudioCodec)) { sb.Append("--audio-codec=").Append(o.AudioCodec).Append(' '); }
            if (o.StayAwake && !o.NoWindow) { sb.Append("--stay-awake "); }
            if (o.TurnScreenOff && !o.NoWindow) { sb.Append("--turn-screen-off "); }
            if (o.AlwaysOnTop) { sb.Append("--always-on-top "); }
            if (o.Fullscreen) { sb.Append("--fullscreen "); }
            if (!string.IsNullOrEmpty(o.Record)) { sb.Append("--record=").Append(Quote(o.Record)).Append(' '); }
            if (o.NoWindow) { sb.Append("--no-playback --no-control "); }
            if (o.TimeLimitSeconds > 0) { sb.Append("--time-limit=").Append(o.TimeLimitSeconds).Append(' '); }
            return sb.ToString().Trim();
        }

        /// <summary>Starts the viewer for a device. Empty string means success.</summary>
        public string Start(string serial)
        {
            if (Running) { return "already running"; }
            if (!File.Exists(ScrcpyExe)) { return "engine not installed: " + ScrcpyExe; }

            string args = BuildArgs(serial);
            _log("[engine] " + ScrcpyExe + " " + args);

            ProcessStartInfo psi = new ProcessStartInfo(ScrcpyExe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = EngineDir;
            if (!string.IsNullOrEmpty(AdbExe) && File.Exists(AdbExe))
            {
                psi.EnvironmentVariables["ADB"] = AdbExe;
            }
            if (Adb.ActivePort > 0)
            {
                // Keep the viewer on the same adb server this app uses (see Adb.Run).
                psi.EnvironmentVariables["ADB_SERVER_SOCKET"] = "tcp:127.0.0.1:" + Adb.ActivePort;
                _log("[engine] viewer will use the adb server on port " + Adb.ActivePort);
            }
            try
            {
                _proc = new Process();
                _proc.StartInfo = psi;
                _proc.EnableRaisingEvents = true;
                _proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                { if (e.Data != null && e.Data.Trim().Length > 0) { _log("[engine] " + e.Data); } };
                _proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                { if (e.Data != null && e.Data.Trim().Length > 0) { _log("[engine] " + e.Data); } };
                _proc.Exited += delegate(object s, EventArgs e)
                {
                    _childWindow = IntPtr.Zero;
                    _log("[engine] exited");
                    if (Exited != null) { Exited(this, EventArgs.Empty); }
                };
                if (!_proc.Start()) { return "engine failed to start"; }
                _proc.BeginOutputReadLine();
                _proc.BeginErrorReadLine();
                return "";
            }
            catch (Exception ex)
            {
                return "engine start failed: " + ex.Message;
            }
        }

        public void Stop()
        {
            Process p = _proc;
            if (p == null) { return; }
            try
            {
                if (!p.HasExited)
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(5000))
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                }
            }
            catch { }
            _childWindow = IntPtr.Zero;
            _proc = null;
        }

        /// <summary>Waits for the viewer window (engines create it after connecting).</summary>
        public IntPtr WaitForWindow(int timeoutMs)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                Process p = _proc;
                if (p != null)
                {
                    try
                    {
                        p.Refresh();
                        if (p.HasExited) { return IntPtr.Zero; }
                        if (p.MainWindowHandle != IntPtr.Zero) { return p.MainWindowHandle; }
                    }
                    catch { }
                }
                System.Threading.Thread.Sleep(200);
            }
            return IntPtr.Zero;
        }

        /// <summary>Reparents the viewer window into our own panel (embedded mode).</summary>
        public bool Embed(IntPtr parent, Rectangle area)
        {
            IntPtr child = WaitForWindow(20000);
            if (child == IntPtr.Zero) { return false; }
            _floatRect = Native.GetWindowBounds(child);   // remember where it lived
            int style = Native.GetWindowLong(child, Native.GWL_STYLE);
            style = (style & ~Native.WS_POPUP & ~Native.WS_CAPTION & ~Native.WS_THICKFRAME) | Native.WS_CHILD;
            Native.SetWindowLong(child, Native.GWL_STYLE, style);
            Native.SetParent(child, parent);
            Native.MoveWindow(child, area.X, area.Y, area.Width, area.Height, true);
            Native.ShowWindow(child, Native.SW_SHOW);
            _childWindow = child;
            return true;
        }

        /// <summary>
        /// Pops the viewer back out to a normal top-level window, restoring the size and
        /// position it had before it was embedded (clamped so it cannot land off-screen).
        /// </summary>
        public void Detach()
        {
            IntPtr child = _childWindow;
            if (child == IntPtr.Zero || !Native.IsWindow(child)) { return; }
            Native.SetParent(child, IntPtr.Zero);
            Native.SetWindowLong(child, Native.GWL_STYLE, Native.WS_POPUP | Native.WS_CAPTION | Native.WS_THICKFRAME | 0x10000000);
            _childWindow = IntPtr.Zero;

            int width = _floatRect.Width > 200 ? _floatRect.Width : 480;
            int height = _floatRect.Height > 200 ? _floatRect.Height : 800;
            int x = ClampToScreen(_floatRect.Left, 0);
            int y = ClampToScreen(_floatRect.Top, 0);
            Native.MoveWindow(child, x, y, width, height, true);
            Native.ShowWindow(child, Native.SW_SHOW);
            SetViewerTopmost(Options.AlwaysOnTop);
            Native.ForceForeground(child);
            _log("[engine] viewer popped out to its own window");
        }

        /// <summary>Applies or clears always-on-top on the viewer window.</summary>
        public void SetViewerTopmost(bool on)
        {
            IntPtr win = WindowHandle();
            if (win == IntPtr.Zero) { return; }
            Native.SetWindowPos(win, on ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        /// <summary>True when the viewer is a normal top-level window rather than a child.</summary>
        public bool Floating { get { return Running && _childWindow == IntPtr.Zero; } }

        private static int ClampToScreen(int value, int fallback)
        {
            int min = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            int max = min + Math.Max(200, Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN) - 160);
            if (value < min || value > max) { return fallback; }
            return value;
        }

        /// <summary>Fills an area with the embedded viewer (call on panel resize).</summary>
        public void ResizeEmbedded(Rectangle area)
        {
            IntPtr child = _childWindow;
            if (child != IntPtr.Zero && Native.IsWindow(child))
            {
                Native.MoveWindow(child, area.X, area.Y, area.Width, area.Height, true);
            }
        }

        public bool Embedded
        {
            get { return _childWindow != IntPtr.Zero && Native.IsWindow(_childWindow); }
        }

        /// <summary>True when the viewer window is currently parented to the given window.</summary>
        public bool IsChildOf(IntPtr parent)
        {
            if (_childWindow == IntPtr.Zero || parent == IntPtr.Zero) { return false; }
            return Native.GetParent(_childWindow) == parent;
        }

        /// <summary>The window touches must land on: embedded child, or the engine's own.</summary>
        public IntPtr WindowHandle()
        {
            if (Embedded) { return _childWindow; }
            Process p = _proc;
            if (p == null) { return IntPtr.Zero; }
            try
            {
                p.Refresh();
                if (p.HasExited) { return IntPtr.Zero; }
                return p.MainWindowHandle;
            }
            catch { return IntPtr.Zero; }
        }

        /// <summary>
        /// Finds the engine directory. Order: OPENMIRROR_ENGINE, &lt;dir&gt;\engine,
        /// &lt;parent&gt;\engine. Keeps the app working when the folder is moved or a
        /// shortcut starts the exe from somewhere else.
        /// </summary>
        public static string ResolveRoot(string baseDirectory)
        {
            List<string> candidates = new List<string>();
            string env = Environment.GetEnvironmentVariable("OPENMIRROR_ENGINE");
            if (!string.IsNullOrEmpty(env)) { candidates.Add(env); }
            candidates.Add(Path.Combine(baseDirectory, "engine"));
            try
            {
                DirectoryInfo info = new DirectoryInfo(baseDirectory);
                if (info.Parent != null) { candidates.Add(Path.Combine(info.Parent.FullName, "engine")); }
            }
            catch { }
            foreach (string candidate in candidates)
            {
                if (!Directory.Exists(candidate)) { continue; }
                if (new DirectoryInfo(candidate).GetFiles("scrcpy.exe", SearchOption.AllDirectories).Length > 0)
                {
                    return candidate;
                }
            }
            return Path.Combine(baseDirectory, "engine");
        }
    }
}
