using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace OpenMirror
{
    /// <summary>
    /// OpenMirror entry point. The GUI is the default; the switches exist so the
    /// whole engine path can be verified without a human watching a window:
    ///
    ///   --probe              environment + device + engine report
    ///   --selftest           profile and mapper math self test (no device needed)
    ///   --headless N         mirror N seconds with no window (pipeline proof)
    ///   --screenshot FILE    one-shot adb screenshot (no engine needed)
    ///   --serial SERIAL      pick a device
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Native.SetProcessDPIAware();

            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            string serial = ArgValue(args, "--serial");
            string root = Engine.ResolveRoot(AppDomain.CurrentDomain.BaseDirectory);

            if (mode == "--probe" || mode == "--selftest" || mode == "--headless" ||
                mode == "--screenshot" || mode == "--layout" || mode == "--embedtest" ||
                mode == "--screentest")
            {
                StartTranscript();
            }

            Action<string> log = delegate(string line) { Console.WriteLine(line); };
            if (mode.Length > 0) { Console.WriteLine("argv: " + string.Join(" ", args)); }

            if (mode == "--probe") { return Probe(root, serial, log); }
            if (mode == "--selftest") { return SelfTest(); }
            if (mode == "--layout") { return LayoutCheck(root); }
            if (mode == "--embedtest") { return EmbedCheck(root, serial, ArgInt(args, 1, 4), log); }
            if (mode == "--screentest") { return ScreenTest(root, serial, ArgValue(args, "--mode"), log); }
            if (mode == "--headless") { return Headless(root, serial, ArgInt(args, 1, 6), log); }
            if (mode == "--screenshot") { return Screenshot(root, serial, args.Length > 1 ? args[1] : "screen.png", log); }

            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.Run(new MainForm(root));
            return 0;
        }

        private static string ArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) { return args[i + 1]; }
            }
            return "";
        }

        /// <summary>
        /// A GUI-subsystem exe owns no console, so CLI modes mirror all output into
        /// cli-transcript.txt next to the exe (see run-cli.ps1).
        /// </summary>
        private static void StartTranscript()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cli-transcript.txt");
                StreamWriter file = new StreamWriter(path, false);
                file.AutoFlush = true;
                Console.SetOut(new TeeWriter(Console.Out, file));
            }
            catch { }
        }

        private static int ArgInt(string[] args, int index, int fallback)
        {
            if (args.Length <= index) { return fallback; }
            int value;
            return int.TryParse(args[index], out value) ? value : fallback;
        }

        private static string FirstOnline(Adb adb)
        {
            foreach (AdbDevice d in adb.Devices())
            {
                if (d.State == "device") { return d.Serial; }
            }
            return "";
        }

        /// <summary>Finds adb: engine-bundled copy first, then the Android SDK.</summary>
        private static string ResolveAdb(Engine engine)
        {
            if (engine.AdbExe.Length > 0 && File.Exists(engine.AdbExe)) { return engine.AdbExe; }
            string sdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Android\\Sdk\\platform-tools\\adb.exe");
            if (File.Exists(sdk)) { return sdk; }
            string studio = @"C:\Program Files\Android\Android Studio\platform-tools\adb.exe";
            if (File.Exists(studio)) { return studio; }
            return "";
        }

        /// <summary>Reports everything needed to decide whether mirroring can work.</summary>
        private static int Probe(string engineRoot, string serial, Action<string> log)
        {
            Console.WriteLine("OpenMirror probe");
            Console.WriteLine("----------------");
            Engine engine = Engine.FromEngineRoot(engineRoot, log);
            Console.WriteLine("engine root     : " + engineRoot);
            Console.WriteLine("engine exe      : " + (engine.ScrcpyExe.Length == 0 ? "(not installed - run setup-engine.ps1)" : engine.ScrcpyExe));
            Console.WriteLine("engine version  : " + engine.Version());
            Console.WriteLine("server payload  : " + (engine.ServerPayloadPresent() ? "present" : "MISSING"));
            Console.WriteLine("engine adb      : " + (engine.AdbExe.Length == 0 ? "(none bundled)" : engine.AdbExe));

            string adbPath = ResolveAdb(engine);
            if (adbPath.Length == 0) { Console.WriteLine("adb             : NOT FOUND"); return 2; }
            Console.WriteLine("adb used        : " + adbPath);
            Adb adb = new Adb(adbPath, null);
            Console.WriteLine("adb version     : " + adb.Version());

            List<AdbDevice> devices = adb.Devices();
            Console.WriteLine("devices         : " + devices.Count);
            foreach (AdbDevice d in devices)
            {
                Console.WriteLine("   - " + d.Label);
                if (d.State == "device")
                {
                    Console.WriteLine("       model  = " + adb.GetProp(d.Serial, "ro.product.model"));
                    Console.WriteLine("       android= " + adb.GetProp(d.Serial, "ro.build.version.release") +
                                      "  sdk=" + adb.GetProp(d.Serial, "ro.build.version.sdk"));
                }
            }
            if (!string.IsNullOrEmpty(serial)) { Console.WriteLine("requested serial: " + serial + "  online=" + adb.IsOnline(serial)); }
            Console.WriteLine("");
            Console.WriteLine("verdict: " + ((engine.ScrcpyExe.Length > 0 && devices.Count > 0)
                ? "ready to mirror"
                : "not ready (needs engine and/or a connected device)"));
            return 0;
        }

        /// <summary>
        /// Starts a real mirror and reparents the viewer into the real form, then
        /// reports the geometry. This is the one GUI integration a headless test
        /// cannot otherwise see.
        /// </summary>
        private static int EmbedCheck(string root, string serial, int seconds, Action<string> log)
        {
            Console.WriteLine("OpenMirror embed check");
            Console.WriteLine("----------------------");
            Engine locator = Engine.FromEngineRoot(root, log);
            string adbPath = ResolveAdb(locator);
            if (adbPath.Length == 0) { Console.WriteLine("adb not found"); return 2; }
            Adb adb = new Adb(adbPath, log);
            if (string.IsNullOrEmpty(serial)) { serial = FirstOnline(adb); }
            if (string.IsNullOrEmpty(serial)) { Console.WriteLine("no online device"); return 3; }

            System.Windows.Forms.Application.EnableVisualStyles();
            MainForm form = new MainForm(root);
            form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            form.Location = new Point(-4000, 0);
            form.Show();
            System.Windows.Forms.Application.DoEvents();

            IntPtr host = form.VideoPanelHandle;
            Rectangle area = form.VideoBounds;
            Console.WriteLine("host panel   : 0x" + host.ToString("X") + "  area " + area.Width + "x" + area.Height);

            Engine engine = Engine.FromEngineRoot(root, log);
            engine.Options.MaxSize = 1024;
            engine.Options.TimeLimitSeconds = seconds;
            string error = engine.Start(serial);
            if (error.Length > 0)
            {
                Console.WriteLine("start failed: " + error);
                form.Close();
                return 4;
            }

            System.Threading.Thread.Sleep(1500);
            System.Windows.Forms.Application.DoEvents();

            bool embedded = engine.Embed(host, area);
            Console.WriteLine("embedded     : " + (embedded ? "yes" : "NO"));
            if (embedded)
            {
                IntPtr child = engine.ChildWindow;
                Native.RECT r = Native.GetWindowBounds(child);
                Point panelScreen = form.VideoOriginScreen();
                Console.WriteLine("viewer window: 0x" + child.ToString("X"));
                Console.WriteLine("viewer rect  : " + r.Width + "x" + r.Height + " at " + r.Left + "," + r.Top);
                Console.WriteLine("panel screen : " + panelScreen.X + "," + panelScreen.Y);
                Console.WriteLine(engine.IsChildOf(host)
                    ? "PASS: viewer is parented into our panel"
                    : "FAIL: viewer parent mismatch");
            }

            System.Threading.Thread.Sleep(Math.Max(1, seconds) * 1000);
            engine.Stop();
            form.Close();
            Console.WriteLine("");
            Console.WriteLine("EMBED CHECK DONE");
            return embedded ? 0 : 5;
        }

        /// <summary>
        /// Proves the screen-off feature end to end: starts a windowed mirror with the
        /// requested keep-awake combination, reads the device's physical display state
        /// from dumpsys, then drives the engine's own MOD+o / MOD+Shift+o shortcuts and
        /// reads the state again. --mode off = keep-awake disabled, --mode awake = enabled.
        /// </summary>
        private static int ScreenTest(string root, string serial, string mode, Action<string> log)
        {
            bool keepAwake = !string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine("OpenMirror screen test  (keep-awake=" + (keepAwake ? "on" : "off") + ")");
            Console.WriteLine("-------------------------------------------------------");

            Engine locator = Engine.FromEngineRoot(root, null);
            string adbPath = ResolveAdb(locator);
            if (adbPath.Length == 0) { Console.WriteLine("adb not found"); return 2; }
            Adb adb = new Adb(adbPath, null);
            if (string.IsNullOrEmpty(serial)) { serial = FirstOnline(adb); }
            if (string.IsNullOrEmpty(serial)) { Console.WriteLine("no online device"); return 3; }

            // Count the engine's own screen-off/on confirmations: those are produced by
            // the device side, so they prove a request arrived even when the OEM display
            // state reads oddly.
            int offEvents = 0;
            int onEvents = 0;
            Action<string> capture = delegate(string line)
            {
                if (line.IndexOf("display turned off") >= 0) { offEvents++; }
                if (line.IndexOf("display turned on") >= 0) { onEvents++; }
                Console.WriteLine(line);
            };

            int failures = 0;
            Console.WriteLine("panel now      : " + PanelState(adb, serial) + "  (logical display: " + DisplayState(adb, serial) + ")");
            Console.WriteLine("stay_on_while_plugged_in : " +
                adb.Shell(serial, "settings get global stay_on_while_plugged_in", 15000).All.Trim());

            Engine engine = Engine.FromEngineRoot(root, capture);
            engine.Options.MaxSize = 800;
            engine.Options.Audio = false;
            engine.Options.StayAwake = keepAwake;
            engine.Options.TurnScreenOff = true;
            string error = engine.Start(serial);
            if (error.Length > 0) { Console.WriteLine("start failed: " + error); return 4; }

            IntPtr viewer = engine.WaitForWindow(15000);
            Console.WriteLine("viewer window  : 0x" + viewer.ToString("X"));
            System.Threading.Thread.Sleep(5000);
            int saidOff = offEvents;
            Console.WriteLine("after start    : panel " + PanelState(adb, serial) +
                              "   (engine confirmed 'turned off' x" + saidOff + ")");
            if (keepAwake)
            {
                if (PanelState(adb, serial) != "OFF")
                {
                    Console.WriteLine("  REPRO  keep-awake beats --turn-screen-off - this is the reported bug");
                }
                else
                {
                    Console.WriteLine("  note   panel went off even with keep-awake on");
                }
            }
            else
            {
                failures += Check("--turn-screen-off holds the panel off", PanelState(adb, serial), "OFF");
            }

            Native.ForceForeground(viewer);
            Native.PostShortcut(viewer, true, false, (int)'O');
            System.Threading.Thread.Sleep(2500);
            failures += CheckCount("MOD+o reaches the engine (new 'turned off' event)", offEvents - saidOff);

            int beforeOn = onEvents;
            Native.PostShortcut(viewer, true, true, (int)'O');
            System.Threading.Thread.Sleep(2500);
            failures += CheckCount("MOD+Shift+o reaches the engine (new 'turned on' event)", onEvents - beforeOn);

            engine.Stop();
            Console.WriteLine("");
            Console.WriteLine(failures == 0 ? "SCREEN TEST PASS" : ("SCREEN TEST FAIL (" + failures + " step(s))"));
            return failures == 0 ? 0 : 5;
        }

        private static int Check(string label, string actual, string expected)
        {
            bool ok = actual == expected;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label + "  -> " + actual + " (expected " + expected + ")");
            return ok ? 0 : 1;
        }

        private static int CheckCount(string label, int actual)
        {
            bool ok = actual >= 1;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label + "  -> " + actual + " event(s)");
            return ok ? 0 : 1;
        }

        /// <summary>
        /// Reads the physical panel state. dumpsys' "Display State" tracks the logical
        /// display and keeps saying ON while the engine powers the panel down through
        /// SurfaceControl, so the verdicts use the brightness strategy instead: OxygenOS
        /// emits "ScreenOffBrightnessStrategy" only when the panel is really off.
        /// </summary>
        private static string PanelState(Adb adb, string serial)
        {
            string text = adb.Shell(serial, "dumpsys display | grep -o 'strat=[A-Za-z]*' | tail -1", 20000).All.Trim();
            if (text.IndexOf("ScreenOff", StringComparison.OrdinalIgnoreCase) >= 0) { return "OFF"; }
            if (text.Length == 0) { return "unknown"; }
            return "ON";
        }

        /// <summary>Reads the device's physical display state (ON/OFF) straight from dumpsys.</summary>
        private static string DisplayState(Adb adb, string serial)
        {
            string text = adb.Shell(serial, "dumpsys display | grep -m1 'Display State='", 20000).All;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("Display State="))
                {
                    return line.Substring("Display State=".Length).Trim();
                }
            }
            return "unknown";
        }

        /// <summary>Builds the real window off-screen and reports the laid-out regions.</summary>
        private static int LayoutCheck(string root)
        {
            Console.WriteLine("OpenMirror layout check");
            Console.WriteLine("-----------------------");
            MainForm form = new MainForm(root);
            form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-4000, 0);
            System.Windows.Forms.Application.EnableVisualStyles();
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            Console.WriteLine(form.LayoutReport());
            form.Close();

            // Also construct the wireless dialog for real, so its auto-detect path is proven.
            Engine engine = Engine.FromEngineRoot(root, null);
            string adbPath = ResolveAdb(engine);
            Adb adb = new Adb(adbPath, null);
            string serial = FirstOnline(adb);
            WirelessForm wireless = new WirelessForm(adb, delegate { return serial; }, delegate { }, null);
            wireless.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            wireless.Location = new System.Drawing.Point(-4000, 0);
            wireless.Show();
            System.Windows.Forms.Application.DoEvents();
            Console.WriteLine("wireless addr: " + wireless.AddressText + Environment.NewLine +
                              "wireless log : " + Adb.Preview(wireless.LogText));
            wireless.Close();

            Console.WriteLine("");
            Console.WriteLine("LAYOUT CHECK DONE");
            return 0;
        }

        /// <summary>Offline self test of the profile round trip and mapper math.</summary>
        private static int SelfTest()
        {
            int failures = 0;
            Console.WriteLine("OpenMirror self test");
            Console.WriteLine("--------------------");

            KeyProfile p = KeyMapStore.DefaultProfile();
            string path = Path.Combine(Path.GetTempPath(), "openmirror-selftest.json");
            KeyMapStore.Save(p, path);
            KeyProfile reloaded = KeyMapStore.Load(path);
            Console.WriteLine("profile round trip : " + p.Targets.Count + " -> " + reloaded.Targets.Count + " controls");
            if (reloaded.Targets.Count != p.Targets.Count) { failures++; }

            MapTarget stick = reloaded.Targets[0];
            Console.WriteLine("stick owns W       : " + (stick.UsesKey(0x57) ? "yes" : "no"));
            if (!stick.UsesKey(0x57)) { failures++; }
            Console.WriteLine("stick owns Z       : " + (stick.UsesKey(0x5A) ? "yes (WRONG)" : "no"));
            if (stick.UsesKey(0x5A)) { failures++; }

            MapTarget fire = reloaded.Targets[1];
            Console.WriteLine("fire control       : " + fire.Describe());
            Console.WriteLine("vk names           : " + VkNames.Name(0x57) + ", " + VkNames.Name(0x70) + ", " +
                                              VkNames.Name(0x0D) + ", " + VkNames.Name(0x99));
            if (VkNames.Name(0x57) != "W") { failures++; }
            if (VkNames.Name(0x70) != "F1") { failures++; }
            if (VkNames.Name(0x0D) != "Enter") { failures++; }

            int w = 1080; int h = 2400;
            int px = (int)Math.Round(fire.X * w);
            int py = (int)Math.Round(fire.Y * h);
            Console.WriteLine("fire fraction      : " + fire.X + "," + fire.Y + " -> " + px + "," + py + " px on " + w + "x" + h);
            if (px != 918 || py != 1728) { failures++; }

            double len = Math.Sqrt(2.0);
            double dx = (1.0 / len) * stick.Radius;
            double dy = (-1.0 / len) * stick.Radius;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            Console.WriteLine("stick deflection   : " + dist.ToString("0.####") + " (radius " + stick.Radius + ")");
            if (Math.Abs(dist - stick.Radius) > 0.0001) { failures++; }

            Console.WriteLine("");
            Console.WriteLine(failures == 0 ? "SELFTEST PASS" : ("SELFTEST FAIL (" + failures + ")"));
            try { File.Delete(path); } catch { }
            return failures == 0 ? 0 : 1;
        }

        /// <summary>Mirrors for N seconds with no window: the end-to-end pipeline proof.</summary>
        private static int Headless(string engineRoot, string serial, int seconds, Action<string> log)
        {
            Engine engine = Engine.FromEngineRoot(engineRoot, log);
            if (engine.ScrcpyExe.Length == 0) { Console.WriteLine("engine not installed"); return 2; }
            string adbPath = ResolveAdb(engine);
            if (adbPath.Length == 0) { Console.WriteLine("adb not found"); return 2; }
            Adb adb = new Adb(adbPath, log);
            if (string.IsNullOrEmpty(serial)) { serial = FirstOnline(adb); }
            if (string.IsNullOrEmpty(serial)) { Console.WriteLine("no online device"); return 3; }

            string record = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "headless-" + DateTime.Now.ToString("HHmmss") + ".mp4");
            engine.Options.NoWindow = true;
            engine.Options.Record = record;
            engine.Options.TimeLimitSeconds = seconds;
            engine.Options.Audio = false;
            string error = engine.Start(serial);
            if (error.Length > 0) { Console.WriteLine("start failed: " + error); return 4; }

            Console.WriteLine("mirroring " + serial + " for " + seconds + "s ...");
            DateTime deadline = DateTime.Now.AddSeconds(seconds + 45);
            while (engine.Running && DateTime.Now < deadline) { System.Threading.Thread.Sleep(250); }
            engine.Stop();

            if (File.Exists(record))
            {
                long size = new FileInfo(record).Length;
                Console.WriteLine("HEADLESS OK: " + size + " bytes -> " + record);
                return size > 10000 ? 0 : 5;
            }
            Console.WriteLine("HEADLESS FAILED: no recording produced");
            return 5;
        }

        private static int Screenshot(string engineRoot, string serial, string output, Action<string> log)
        {
            Engine engine = Engine.FromEngineRoot(engineRoot, log);
            string adbPath = ResolveAdb(engine);
            if (adbPath.Length == 0) { Console.WriteLine("adb not found"); return 2; }
            Adb adb = new Adb(adbPath, log);
            if (string.IsNullOrEmpty(serial)) { serial = FirstOnline(adb); }
            if (string.IsNullOrEmpty(serial)) { Console.WriteLine("no online device"); return 3; }
            Adb.Result r = adb.Screenshot(serial, output);
            if (File.Exists(output) && new FileInfo(output).Length > 1000)
            {
                Console.WriteLine("SCREENSHOT OK: " + output + " (" + new FileInfo(output).Length + " bytes)");
                return 0;
            }
            Console.WriteLine("SCREENSHOT FAILED: " + r.StdErr);
            return 5;
        }
    }

    /// <summary>Fans text output out to two writers (console plus transcript file).</summary>
    internal sealed class TeeWriter : TextWriter
    {
        private readonly TextWriter _first;
        private readonly TextWriter _second;

        public TeeWriter(TextWriter first, TextWriter second)
        {
            _first = first;
            _second = second;
        }

        public override System.Text.Encoding Encoding { get { return _first.Encoding; } }
        public override void Write(char value) { _first.Write(value); _second.Write(value); }
        public override void Write(string value) { _first.Write(value); _second.Write(value); }
        public override void WriteLine(string value) { _first.WriteLine(value); _second.WriteLine(value); }
        public override void WriteLine() { _first.WriteLine(); _second.WriteLine(); }
        public override void Flush() { _first.Flush(); _second.Flush(); }
    }
}
