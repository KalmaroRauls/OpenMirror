using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace OpenMirror
{
    /// <summary>A device as reported by `adb devices -l`.</summary>
    internal sealed class AdbDevice
    {
        public string Serial = "";
        public string State = "";
        public string Model = "";
        public string Product = "";
        public bool Wireless;
        public int TransportId;

        public string Label
        {
            get
            {
                string name = Model.Length > 0 ? Model : Serial;
                string transport = Wireless ? "wireless" : "usb";
                return name + "   [" + transport + " | " + Serial + " | " + State + "]";
            }
        }

        public override string ToString() { return Label; }
    }

    /// <summary>
    /// Dependency-free adb wrapper. Always uses an explicit adb.exe path so a stray
    /// vendor adb server (the old Wormhole install ships a forked "wdb.exe") can
    /// never hijack our connection.
    /// </summary>
    internal sealed class Adb
    {
        public string AdbPath;
        private readonly Action<string> _log;
        private bool _switched;

        /// <summary>
        /// Server port we ended up using; 0 means adb's default (5037). When a foreign or
        /// wedged adb server owns 5037 - typically the abandoned Wormhole service running
        /// an old adb - every call hangs, so we fall back to our own server on a private
        /// port (the viewer is pointed at the same port via ADB_SERVER_SOCKET).
        /// </summary>
        public static int ActivePort;
        public const int PrivatePort = 5057;
        private static DateTime _nextRecheck = DateTime.MinValue;

        public Adb(string adbPath, Action<string> log)
        {
            AdbPath = adbPath;
            _log = log;
        }

        /// <summary>Arguments that pin adb to our own server, once we had to switch.</summary>
        public string AdbFlag() { return ActivePort > 0 ? ("-P " + ActivePort + " ") : ""; }

        /// <summary>
        /// Gives the default port another chance after we stepped aside to the private one: the
        /// server that made us move may be gone, and a phone that came free meanwhile should be
        /// picked up without restarting the app. Throttled, because a culprit that refuses to die
        /// would otherwise be retried on every refresh.
        /// </summary>
        public void RecheckDefaultPort()
        {
            if (DateTime.UtcNow < _nextRecheck) { return; }
            _nextRecheck = DateTime.UtcNow.AddSeconds(30);
            _switched = false;
            ActivePort = 0;
        }

        internal sealed class Result
        {
            public int ExitCode;
            public string StdOut = "";
            public string StdErr = "";
            public bool Ok { get { return ExitCode == 0; } }
            public string All { get { return (StdOut + StdErr).Trim(); } }
        }

        /// <summary>
        /// True when the failure is an out-of-date server on the default port. adb kills such a
        /// server and starts its own in its place, so the port itself is fine - which matters,
        /// because only the server that reached the phone first owns its USB handle.
        /// </summary>
        private static bool StaleServer(Result result)
        {
            return result.All.IndexOf("doesn't match this client", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Runs adb and captures text output. Never throws. If the first attempt fails in a
        /// way that points at an unusable default adb server, the command is retried once:
        /// on the default port when the server there is merely out of date, otherwise on a
        /// private port.
        /// </summary>
        public Result Run(string arguments, int timeoutMs)
        {
            Result result = RunOnce(arguments, timeoutMs);
            if (!_switched && ServerLooksBroken(result))
            {
                // An out-of-date server on 5037 is worth taking over rather than dodging. Our own
                // client replaces it, keeps the port, and keeps the device visible; stepping aside
                // to a private port leaves a phone that another adb already holds invisible to us.
                if (StaleServer(result))
                {
                    Result reclaimed = RunOnce(arguments, timeoutMs);
                    if (reclaimed.ExitCode == 0)
                    {
                        _switched = true;
                        if (_log != null)
                        {
                            _log("[adb] took port 5037 over from an out-of-date adb server and kept using it");
                        }
                        return reclaimed;
                    }
                    if (_log != null) { _log("[adb] could not take port 5037 over: " + Preview(reclaimed.All)); }
                }
                _switched = true;
                ActivePort = PrivatePort;
                if (_log != null)
                {
                    _log("[adb] default server (5037) is not usable - switching to a private server on port " + PrivatePort);
                    _log("[adb] usual cause: the abandoned Wormhole service holding an old adb on that port");
                }
                Result retry = RunOnce(arguments, timeoutMs);
                if (retry.ExitCode == 0 || retry.All.Length > 0) { result = retry; }
            }
            return result;
        }

        /// <summary>True when the failure looks like a dead foreign server, not a missing device.</summary>
        private static bool ServerLooksBroken(Result result)
        {
            if (result.ExitCode == 0) { return false; }
            string text = result.All;
            if (text.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            if (text.IndexOf("doesn't match this client", StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            if (text.IndexOf("cannot connect to daemon", StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            // A server that resets the connection mid-request is just as unusable, and that is what
            // two adb installs on one machine look like: each one kills the other's server on the
            // default port, and every kill takes all established transports with it.
            if (text.IndexOf("protocol fault", StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            if (text.IndexOf("connection reset", StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            return false;
        }

        private Result RunOnce(string arguments, int timeoutMs)
        {
            Result result = new Result();
            string full = AdbFlag() + arguments;
            ProcessStartInfo psi = new ProcessStartInfo(AdbPath, full);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            try
            {
                Process p = Process.Start(psi);
                if (p == null)
                {
                    result.ExitCode = -3;
                    result.StdErr = "could not start " + AdbPath;
                    return result;
                }
                string outText = "";
                string errText = "";
                // Read both pipes on dedicated threads. The async event API can race with
                // Kill()/Dispose() when adb is wedged (we saw an access violation), and a
                // StringBuilder shared with a reader thread corrupts, so the readers collect
                // plain strings: reference assignment is atomic.
                System.Threading.Thread outThread = new System.Threading.Thread(
                    delegate() { try { outText = p.StandardOutput.ReadToEnd(); } catch { } });
                System.Threading.Thread errThread = new System.Threading.Thread(
                    delegate() { try { errText = p.StandardError.ReadToEnd(); } catch { } });
                outThread.IsBackground = true;
                errThread.IsBackground = true;
                outThread.Start();
                errThread.Start();

                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    try { p.WaitForExit(5000); } catch { }
                    outThread.Join(2000);
                    errThread.Join(2000);
                    try { p.Dispose(); } catch { }
                    result.ExitCode = -1;
                    result.StdOut = outText == null ? "" : outText;
                    result.StdErr = "timed out after " + timeoutMs + " ms: adb " + full;
                    return result;
                }

                outThread.Join(3000);
                errThread.Join(3000);
                result.StdOut = outText == null ? "" : outText;
                result.StdErr = errText == null ? "" : errText;
                result.ExitCode = p.ExitCode;
                try { p.Dispose(); } catch { }
            }
            catch (Exception ex)
            {
                result.ExitCode = -2;
                result.StdErr = ex.Message;
            }
            if (_log != null && result.All.Length > 0)
            {
                _log("adb " + full + "  ->  " + Preview(result.All));
            }
            return result;
        }

        /// <summary>
        /// One-line, length-capped preview. Some dumps (dumpsys display) are tens of
        /// thousands of characters on a single logical line and would flood the UI log.
        /// </summary>
        public static string Preview(string text)
        {
            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            while (flat.IndexOf("  ") >= 0) { flat = flat.Replace("  ", " "); }
            if (flat.Length > 240) { flat = flat.Substring(0, 240) + " ...[" + text.Length + " chars]"; }
            return flat;
        }

        public Result Run(string arguments) { return Run(arguments, 20000); }

        /// <summary>Runs a command that emits binary stdout straight into a file.</summary>
        public Result RunRaw(string arguments, int timeoutMs, string outputFile)
        {
            Result result = new Result();
            ProcessStartInfo psi = new ProcessStartInfo(AdbPath, AdbFlag() + arguments);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            try
            {
                using (Process p = Process.Start(psi))
                {
                    using (FileStream fs = File.Create(outputFile))
                    {
                        p.StandardOutput.BaseStream.CopyTo(fs);
                    }
                    result.StdErr = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } result.ExitCode = -1; }
                    else { result.ExitCode = p.ExitCode; }
                }
            }
            catch (Exception ex)
            {
                result.ExitCode = -2;
                result.StdErr = ex.Message;
            }
            return result;
        }

        public void StartServer() { Run("start-server", 30000); }

        public void KillServer() { Run("kill-server", 15000); }

        public string Version()
        {
            return Run("version", 15000).All.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        /// <summary>Parses `adb devices -l` into structured devices.</summary>
        public List<AdbDevice> Devices()
        {
            List<AdbDevice> list = new List<AdbDevice>();
            Result r = Run("devices -l", 15000);
            foreach (string raw in r.All.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) { continue; }
                if (line.StartsWith("List of devices")) { continue; }
                if (line.StartsWith("*")) { continue; }
                string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) { continue; }
                AdbDevice d = new AdbDevice();
                d.Serial = parts[0];
                d.State = parts[1];
                for (int i = 2; i < parts.Length; i++)
                {
                    string p = parts[i];
                    int eq = p.IndexOf(':');
                    if (eq <= 0) { continue; }
                    string key = p.Substring(0, eq);
                    string val = p.Substring(eq + 1);
                    if (key == "model") { d.Model = val; }
                    else if (key == "product") { d.Product = val; }
                    else if (key == "transport_id") { int.TryParse(val, out d.TransportId); }
                }
                d.Wireless = d.Serial.IndexOf(':') > 0;
                list.Add(d);
            }
            return list;
        }

        public AdbDevice Find(string serial)
        {
            foreach (AdbDevice d in Devices())
            {
                if (string.Equals(d.Serial, serial, StringComparison.OrdinalIgnoreCase)) { return d; }
            }
            return null;
        }

        /// <summary>True when the serial exists and is in the "device" state.</summary>
        public bool IsOnline(string serial)
        {
            if (string.IsNullOrEmpty(serial)) { return false; }
            AdbDevice d = Find(serial);
            return d != null && d.State == "device";
        }

        // ---- wireless adb ------------------------------------------------------
        /// <summary>Step 1: make an already-USB-connected device listen on TCP.</summary>
        public Result EnableTcpip(string serial, int port)
        {
            return Run(DeviceFlag(serial) + " tcpip " + port, 30000);
        }

        /// <summary>Step 2: connect to a device that is already listening.</summary>
        public Result Connect(string hostPort) { return Run("connect " + hostPort, 30000); }

        public Result Disconnect(string hostPort) { return Run("disconnect " + hostPort, 20000); }

        /// <summary>Android 11+ wireless debugging pairing.</summary>
        public Result Pair(string hostPort, string code) { return Run("pair " + hostPort + " " + code, 60000); }

        /// <summary>
        /// Connects, retrying while the phone finishes switching to TCP mode. `adb tcpip` restarts
        /// adbd, so a connect issued straight afterwards can be refused for a second or two.
        /// </summary>
        public Result ConnectRetry(string hostPort, int attempts, int delayMs)
        {
            Result last = null;
            for (int i = 0; i < attempts; i++)
            {
                last = Connect(hostPort);
                if (ConnectSucceeded(last)) { return last; }
                if (i < attempts - 1) { System.Threading.Thread.Sleep(delayMs); }
            }
            return last;
        }

        /// <summary>
        /// True when `adb connect` actually established the transport. adb reports that in its text
        /// ("connected to ...", "failed to connect to ...") rather than in its exit code.
        /// </summary>
        public static bool ConnectSucceeded(Result result)
        {
            if (result == null) { return false; }
            string text = result.All;
            if (text.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0) { return false; }
            if (text.IndexOf("cannot", StringComparison.OrdinalIgnoreCase) >= 0) { return false; }
            if (text.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0) { return false; }
            return text.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- device queries ----------------------------------------------------
        public string GetProp(string serial, string prop)
        {
            return Run(DeviceFlag(serial) + " shell getprop " + prop, 15000).All.Trim();
        }

        public Result Shell(string serial, string command, int timeoutMs)
        {
            return Run(DeviceFlag(serial) + " shell " + command, timeoutMs);
        }

        /// <summary>One-shot PNG grab; works with or without the mirror engine.</summary>
        public Result Screenshot(string serial, string outputPng)
        {
            return RunRaw(DeviceFlag(serial) + " exec-out screencap -p", 30000, outputPng);
        }

        public Result Push(string serial, string local, string remote, int timeoutMs)
        {
            return Run(DeviceFlag(serial) + " push \"" + local + "\" \"" + remote + "\"", timeoutMs);
        }

        public static string DeviceFlag(string serial)
        {
            return string.IsNullOrEmpty(serial) ? "" : ("-s " + serial);
        }
    }
}
