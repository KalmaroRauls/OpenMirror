using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OpenMirror
{
    /// <summary>
    /// The OpenMirror window: device picker, mirror surface, key-mapping controls and
    /// an activity log. Plain WinForms so it runs on a stock Windows install.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly string _engineRoot;
        private Engine _engine;
        private Adb _adb;
        private KeyMapper _mapper;
        private KeyProfile _profile;
        private OverlayForm _overlay;

        private ComboBox _deviceBox;
        private Button _refreshButton;
        private Button _connectButton;
        private Button _stopButton;
        private Button _shotButton;
        private Button _probeButton;
        private CheckBox _embedCheck;
        private CheckBox _audioCheck;
        private CheckBox _awakeCheck;
        private CheckBox _screenOffCheck;
        private CheckBox _topmostCheck;
        private ComboBox _sizeBox;
        private TextBox _bitrateBox;
        private Panel _videoPanel;
        private Label _statusLabel;
        private TextBox _logBox;
        private ComboBox _profileBox;
        private CheckBox _mapCheck;
        private CheckBox _overlayCheck;
        private ListBox _controlList;
        private Timer _deviceTimer;
        private Panel _topPanel;
        private Panel _sidePanel;
        private Button _popButton;
        private Button _compactButton;
        private bool _compact;
        private bool _fullscreen;
        private bool _reembedAfterRestore;
        private string _wirelessAddress = "";
        private DateTime _nextWirelessRetry = DateTime.MinValue;
        private bool _wirelessRetryRunning;

        public MainForm(string engineRoot)
        {
            _engineRoot = engineRoot;
            Text = "OpenMirror - vendor independent phone mirroring";
            Icon = AppIcon.Value;
            Width = 1220;
            Height = 780;
            MinimumSize = new Size(940, 620);
            StartPosition = FormStartPosition.CenterScreen;

            BuildUi();
            KeyPreview = true;
            _videoPanel.SizeChanged += delegate { RepositionEmbedded(); };
            Resize += delegate(object s, EventArgs e)
            {
                if (WindowState == FormWindowState.Minimized) { MinimizedEmbedded(); }
                else { RestoreEmbed(); RepositionEmbedded(); }
            };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.M) { ToggleCompact(); e.Handled = true; }
                else if (e.KeyCode == Keys.F11) { ToggleFullscreen(); e.Handled = true; }
                else if (e.KeyCode == Keys.Escape && _fullscreen) { LeaveFullscreen(); e.Handled = true; }
            };

            _wirelessAddress = WirelessStore.LoadAddress();
            LoadEngine();
            RefreshDevices();
            LoadProfiles();

            _deviceTimer = new Timer();
            _deviceTimer.Interval = 4000;
            _deviceTimer.Tick += delegate(object s, EventArgs e)
            {
                if (_engine == null || !_engine.Running) { RefreshDevices(); }
            };
            _deviceTimer.Start();

            FormClosing += delegate(object s, FormClosingEventArgs e) { Shutdown(); };
        }

        private void BuildUi()
        {
            BuildTop();
            BuildStatus();
            BuildSide();
            BuildVideo();
        }

        private void BuildTop()
        {
            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 128;
            Controls.Add(top);
            _topPanel = top;

            top.Controls.Add(Label("Device", 10, 2));
            _deviceBox = new ComboBox();
            _deviceBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _deviceBox.Location = new Point(10, 20);
            _deviceBox.Width = 330;
            top.Controls.Add(_deviceBox);

            _refreshButton = Button("Refresh", 348, 19, 70, top, delegate { RefreshDevices(); });
            _connectButton = Button("Connect", 422, 19, 84, top, delegate { Connect(); });
            _stopButton = Button("Stop", 510, 19, 60, top, delegate { Disconnect(); });
            _shotButton = Button("Screenshot", 574, 19, 90, top, delegate { TakeScreenshot(); });
            _probeButton = Button("Run checks", 668, 19, 90, top, delegate { RunChecks(); });

            top.Controls.Add(Label("Max size", 10, 48));
            _sizeBox = new ComboBox();
            _sizeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _sizeBox.Items.AddRange(new object[] { "native", "1920", "1600", "1280", "1024", "800" });
            _sizeBox.SelectedIndex = 3;
            _sizeBox.Location = new Point(72, 45);
            _sizeBox.Width = 80;
            top.Controls.Add(_sizeBox);

            top.Controls.Add(Label("Bitrate", 164, 48));
            _bitrateBox = new TextBox();
            _bitrateBox.Text = "8M";
            _bitrateBox.Location = new Point(208, 45);
            _bitrateBox.Width = 60;
            top.Controls.Add(_bitrateBox);

            _audioCheck = Check("Audio", 286, 47, top, true);
            _awakeCheck = Check("Keep awake", 350, 47, top, true);
            _screenOffCheck = Check("Phone screen off", 456, 47, top, false);
            _embedCheck = Check("Embed", 590, 47, top, true);
            _topmostCheck = Check("Always on top", 664, 47, top, false);

            // These two settings fight each other on the device, so wire them together.
            _awakeCheck.CheckedChanged += delegate { AwakeToggled(); };
            _screenOffCheck.CheckedChanged += delegate { ScreenOffToggled(); };
            _topmostCheck.CheckedChanged += delegate { ApplyTopmost(); };

            top.Controls.Add(Label("Window, screen and wireless", 10, 80));

            _popButton = Button("Pop out mirror", 10, 98, 110, top, delegate { PopOutMirror(); });
            _compactButton = Button("Compact view", 128, 98, 104, top, delegate { ToggleCompact(); });
            Button("Fullscreen (F11)", 240, 98, 116, top, delegate { ToggleFullscreen(); });
            Button("Wireless...", 364, 98, 90, top, delegate { OpenWireless(); });
            Button("Screen off", 462, 98, 84, top, delegate { SendScreenShortcut(false); });
            Button("Screen on", 554, 98, 84, top, delegate { SendScreenShortcut(true); });
        }

        private void BuildStatus()
        {
            _statusLabel = new Label();
            _statusLabel.Dock = DockStyle.Bottom;
            _statusLabel.Height = 24;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Text = "ready";
            Controls.Add(_statusLabel);
        }

        private void BuildSide()
        {
            Panel side = new Panel();
            side.Dock = DockStyle.Right;
            side.Width = 306;
            Controls.Add(side);
            _sidePanel = side;

            side.Controls.Add(Label("Key mapping", 8, 4));

            _mapCheck = new CheckBox();
            _mapCheck.Text = "Map keyboard to touch";
            _mapCheck.Location = new Point(8, 22);
            _mapCheck.Width = 290;
            _mapCheck.CheckedChanged += delegate { ApplyMapping(); };
            side.Controls.Add(_mapCheck);

            _profileBox = new ComboBox();
            _profileBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _profileBox.Location = new Point(8, 46);
            _profileBox.Width = 290;
            _profileBox.SelectedIndexChanged += delegate { SelectProfile(); };
            side.Controls.Add(_profileBox);

            _overlayCheck = new CheckBox();
            _overlayCheck.Text = "Show mapping overlay";
            _overlayCheck.Location = new Point(8, 72);
            _overlayCheck.Width = 290;
            _overlayCheck.CheckedChanged += delegate { ApplyOverlay(); };
            side.Controls.Add(_overlayCheck);

            Button save = new Button();
            save.Text = "Save profile";
            save.Location = new Point(8, 96);
            save.Width = 140;
            save.Click += delegate { SaveProfile(); };
            side.Controls.Add(save);

            Button reset = new Button();
            reset.Text = "Reset default";
            reset.Location = new Point(156, 96);
            reset.Width = 142;
            reset.Click += delegate { ResetProfile(); };
            side.Controls.Add(reset);

            _controlList = new ListBox();
            _controlList.Location = new Point(8, 128);
            _controlList.Size = new Size(290, 190);
            side.Controls.Add(_controlList);

            Label hint = new Label();
            hint.Text = "F8 toggles mapping while the mirror window is focused." + Environment.NewLine +
                        "One touch pointer is driven at a time (see README limits).";
            hint.Location = new Point(8, 324);
            hint.Size = new Size(290, 44);
            side.Controls.Add(hint);

            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.ReadOnly = true;
            _logBox.Font = new Font("Consolas", 8.25f);
            _logBox.Location = new Point(8, 372);
            _logBox.Size = new Size(290, 330);
            _logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            side.Controls.Add(_logBox);
        }

        private void BuildVideo()
        {
            _videoPanel = new Panel();
            _videoPanel.Dock = DockStyle.Fill;
            _videoPanel.BackColor = Color.Black;
            _videoPanel.Resize += delegate { RepositionEmbedded(); };
            Controls.Add(_videoPanel);
            _videoPanel.BringToFront();
        }

        private static Label Label(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.AutoSize = true;
            return l;
        }

        private static Button Button(string text, int x, int y, int width, Control parent, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Width = width;
            b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        private static CheckBox Check(string text, int x, int y, Control parent, bool value)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Location = new Point(x, y);
            c.AutoSize = true;
            c.Checked = value;
            parent.Controls.Add(c);
            return c;
        }

        private string ResolveAdbPath()
        {
            if (_engine != null && _engine.AdbExe.Length > 0 && File.Exists(_engine.AdbExe)) { return _engine.AdbExe; }
            string sdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Android\\Sdk\\platform-tools\\adb.exe");
            if (File.Exists(sdk)) { return sdk; }
            return "";
        }

        private void LoadEngine()
        {
            _engine = Engine.FromEngineRoot(_engineRoot, Log);
            _engine.Exited += delegate(object s, EventArgs e)
            {
                SetStatus("disconnected");
                if (_overlay != null) { _overlay.Hide(); }
            };
            _adb = new Adb(ResolveAdbPath(), Log);

            Log("[ui] engine root : " + _engineRoot);
            Log("[ui] engine exe  : " + (_engine.ScrcpyExe.Length == 0 ? "(missing - run setup-engine.ps1)" : _engine.ScrcpyExe));
            Log("[ui] engine build: " + _engine.Version());
            Log("[ui] payload     : " + (_engine.ServerPayloadPresent() ? "scrcpy-server present" : "MISSING"));
            Log("[ui] adb         : " + (_adb.AdbPath.Length == 0 ? "(not found)" : _adb.AdbPath));
            if (_engine.ScrcpyExe.Length == 0) { SetStatus("engine not installed - run setup-engine.ps1"); }
        }

        private void RefreshDevices()
        {
            if (_adb == null) { return; }
            string previous = SelectedSerial();
            List<AdbDevice> devices = _adb.Devices();
            if (devices.Count == 0 && Adb.ActivePort > 0)
            {
                // Nothing on the private port usually means a server elsewhere owns the phone's USB
                // handle - and that server may have gone since. Try the default port again instead
                // of staying blind for the rest of the session.
                _adb.RecheckDefaultPort();
                List<AdbDevice> retry = _adb.Devices();
                if (retry.Count > 0)
                {
                    devices = retry;
                    Log("[adb] the default port has it again - back on 5037");
                }
            }
            if (OnlineCount(devices) == 0 && !_engine.Running) { devices = ReconnectWireless(devices); }
            _deviceBox.Items.Clear();
            foreach (AdbDevice d in devices) { _deviceBox.Items.Add(d); }
            int selectIndex = PreferredIndex(devices, previous);
            if (selectIndex >= 0) { _deviceBox.SelectedIndex = selectIndex; }
            if (devices.Count == 0 && !_engine.Running)
            {
                SetStatus("no devices - connect USB or use wireless debugging");
                if (Adb.ActivePort > 0)
                {
                    // The usual cause for an empty list on the private port is not the phone at all:
                    // a server somewhere else reached it first and owns its USB handle. Say so.
                    Log("[adb] port " + Adb.ActivePort + " sees no devices even though this app is talking to adb fine: " +
                        "another adb server may still hold the phone's USB handle. Close other phone tools (or reboot), " +
                        "then press Refresh.");
                }
            }
        }

        /// <summary>How many of these devices are actually usable.</summary>
        private static int OnlineCount(List<AdbDevice> devices)
        {
            int count = 0;
            foreach (AdbDevice d in devices) { if (d.State == "device") { count++; } }
            return count;
        }

        /// <summary>
        /// Which entry to select: the previous choice while it is still online, else the Wi-Fi
        /// transport this app connected to, else the first usable device. Preferring the remembered
        /// Wi-Fi serial is what keeps a wireless session wireless when the cable is also plugged in.
        /// </summary>
        private int PreferredIndex(List<AdbDevice> devices, string previous)
        {
            int firstOnline = -1;
            for (int i = 0; i < devices.Count; i++)
            {
                AdbDevice d = devices[i];
                if (d.State != "device") { continue; }
                if (d.Serial == previous) { return i; }
                if (_wirelessAddress.Length > 0 && string.Equals(d.Serial, _wirelessAddress, StringComparison.OrdinalIgnoreCase)) { return i; }
                if (firstOnline < 0) { firstOnline = i; }
            }
            return firstOnline;
        }

        /// <summary>
        /// A Wi-Fi transport is a connection adb was told to make, not something it discovers, and it
        /// only lives while some adb server holds it: another adb install on the machine killing that
        /// server, or a tcpip restart, drops it. So re-issue the connect that worked last time rather
        /// than asking for the address again. Throttled, so an absent phone is retried, not hammered.
        /// </summary>
        private List<AdbDevice> ReconnectWireless(List<AdbDevice> devices)
        {
            if (_wirelessAddress.Length == 0 || _wirelessRetryRunning || DateTime.UtcNow < _nextWirelessRetry) { return devices; }
            _nextWirelessRetry = DateTime.UtcNow.AddSeconds(15);
            _wirelessRetryRunning = true;
            string address = _wirelessAddress;
            // Off the UI thread: adb sits on an address that does not answer for about twenty seconds,
            // and the window must not stop repainting while it does.
            System.Threading.ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                Adb.Result result = _adb.Connect(address);
                bool connected = Adb.ConnectSucceeded(result);
                string text = Adb.Preview(result.All);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _wirelessRetryRunning = false;
                        if (connected)
                        {
                            Log("[wireless] reconnected to " + address);
                            RefreshDevices();
                        }
                        else
                        {
                            Log("[wireless] " + address + " is not answering: " + text);
                        }
                    });
                }
                catch { _wirelessRetryRunning = false; }
            });
            return devices;
        }

        private string SelectedSerial()
        {
            AdbDevice d = _deviceBox.SelectedItem as AdbDevice;
            return d == null ? "" : d.Serial;
        }

        private void ApplyOptions()
        {
            MirrorOptions o = _engine.Options;
            o.BitRate = _bitrateBox.Text.Trim();
            o.MaxSize = _sizeBox.SelectedIndex <= 0 ? 0 : int.Parse((string)_sizeBox.SelectedItem);
            o.Audio = _audioCheck.Checked;
            // A screen-off request is pointless together with keep-awake: the device's
            // stay_on_while_plugged_in flag forces the panel straight back on.
            o.TurnScreenOff = _screenOffCheck.Checked;
            o.StayAwake = _awakeCheck.Checked && !o.TurnScreenOff;
            o.AlwaysOnTop = _topmostCheck.Checked;
            o.WindowTitle = "OpenMirror " + SelectedSerial();
            o.Record = "";
            o.NoWindow = false;
            o.TimeLimitSeconds = 0;
        }

        private void Connect()
        {
            if (_engine.Running) { Log("[ui] already mirroring"); return; }
            string serial = SelectedSerial();
            if (serial.Length == 0) { SetStatus("select a device first"); return; }
            if (_adb.AdbPath.Length == 0) { SetStatus("adb not found"); return; }
            if (!_adb.IsOnline(serial)) { SetStatus("device " + serial + " is not in the device state"); return; }

            ApplyOptions();
            string error = _engine.Start(serial);
            if (error.Length > 0) { Log("[ui] " + error); SetStatus(error); return; }
            SetStatus("mirroring " + serial);
            if (_embedCheck.Checked) { StartEmbed(); }
        }

        private void StartEmbed()
        {
            IntPtr parent = _videoPanel.Handle;
            Rectangle area = VideoArea();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                bool ok = _engine.Embed(parent, area);
                BeginInvoke((MethodInvoker)delegate
                {
                    Log(ok ? "[ui] mirror window embedded" : "[ui] embed failed - using the engine's own window");
                    if (ok) { RepositionEmbedded(); }
                });
            });
        }

        private Rectangle VideoArea()
        {
            return new Rectangle(0, 0, Math.Max(1, _videoPanel.ClientSize.Width), Math.Max(1, _videoPanel.ClientSize.Height));
        }

        private void RepositionEmbedded()
        {
            if (_engine != null && _engine.Embedded) { _engine.ResizeEmbedded(VideoArea()); }
        }

        private void Disconnect()
        {
            if (_engine != null) { _engine.Stop(); }
            SetStatus("stopped");
        }

        private void Shutdown()
        {
            if (_deviceTimer != null) { _deviceTimer.Stop(); }
            if (_mapper != null) { _mapper.Dispose(); }
            if (_overlay != null) { _overlay.Close(); }
            if (_engine != null) { _engine.Stop(); }
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { SetStatus(text); }); return; }
            _statusLabel.Text = text;
        }

        private void TakeScreenshot()
        {
            string serial = SelectedSerial();
            if (serial.Length == 0) { SetStatus("select a device first"); return; }
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
            if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
            string file = Path.Combine(dir, "shot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            SetStatus("taking screenshot...");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                Adb.Result r = _adb.Screenshot(serial, file);
                BeginInvoke((MethodInvoker)delegate
                {
                    if (File.Exists(file) && new FileInfo(file).Length > 1000)
                    {
                        Log("[ui] screenshot -> " + file);
                        SetStatus("screenshot saved");
                    }
                    else
                    {
                        Log("[ui] screenshot failed: " + r.StdErr);
                        SetStatus("screenshot failed");
                    }
                });
            });
        }

        private void RunChecks()
        {
            string serial = SelectedSerial();
            SetStatus("running checks...");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                List<string> lines = new List<string>();
                lines.Add("engine exe     : " + (_engine.ScrcpyExe.Length == 0 ? "MISSING" : _engine.ScrcpyExe));
                lines.Add("engine build   : " + _engine.Version());
                lines.Add("server payload : " + (_engine.ServerPayloadPresent() ? "present" : "MISSING"));
                lines.Add("adb            : " + (_adb.AdbPath.Length == 0 ? "MISSING" : _adb.Version()));
                List<AdbDevice> devices = _adb.Devices();
                lines.Add("devices        : " + devices.Count);
                foreach (AdbDevice d in devices) { lines.Add("   " + d.Label); }
                if (serial.Length > 0 && _adb.IsOnline(serial))
                {
                    lines.Add("model          : " + _adb.GetProp(serial, "ro.product.model"));
                    lines.Add("android        : " + _adb.GetProp(serial, "ro.build.version.release") +
                              "  sdk=" + _adb.GetProp(serial, "ro.build.version.sdk"));
                    lines.Add("screen         : " + _adb.Shell(serial, "wm size", 15000).All);
                    // Optional context only: some phones still have the old vendor client's app
                    // installed. OpenMirror neither needs nor uses it, but knowing it is there
                    // explains a stray daemon on the device when troubleshooting.
                    string legacy = _adb.Shell(serial, "pm path com.viper.wormhole", 15000).All;
                    lines.Add("legacy app     : " + (legacy.Length == 0 ? "not installed" : legacy));
                }
                else
                {
                    lines.Add("device not online; skipping device checks");
                }
                BeginInvoke((MethodInvoker)delegate
                {
                    foreach (string l in lines) { Log("[check] " + l); }
                    SetStatus("checks complete");
                });
            });
        }

        private void Log(string line)
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { Log(line); }); return; }
            if (_logBox == null) { return; }
            _logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
            if (_logBox.Lines.Length > 600)
            {
                List<string> keep = new List<string>(_logBox.Lines);
                keep.RemoveRange(0, 200);
                _logBox.Lines = keep.ToArray();
            }
        }

        private void LoadProfiles()
        {
            List<KeyProfile> profiles = KeyMapStore.LoadAll();
            _profileBox.Items.Clear();
            foreach (KeyProfile p in profiles) { _profileBox.Items.Add(p); }
            if (_profileBox.Items.Count > 0) { _profileBox.SelectedIndex = 0; }
        }

        private void SelectProfile()
        {
            KeyProfile p = _profileBox.SelectedItem as KeyProfile;
            if (p == null) { return; }
            _profile = p;
            RefreshControlList();
            if (_mapper != null) { _mapper.Profile = p; }
            Log("[mapper] profile " + p.Name + ": " + p.Targets.Count + " controls, toggle key " + VkNames.Name(p.ToggleHotkey));
        }

        private void RefreshControlList()
        {
            _controlList.Items.Clear();
            if (_profile == null) { return; }
            foreach (MapTarget t in _profile.Targets) { _controlList.Items.Add(t.Describe()); }
        }

        /// <summary>Panel the viewer is embedded into (diagnostics).</summary>
        public IntPtr VideoPanelHandle { get { return _videoPanel.Handle; } }

        /// <summary>Panel area the viewer should occupy (diagnostics).</summary>
        public Rectangle VideoBounds { get { return VideoArea(); } }

        /// <summary>Screen origin of the video panel's client area (diagnostics).</summary>
        public Point VideoOriginScreen() { return _videoPanel.PointToScreen(Point.Empty); }

        /// <summary>Diagnostic used by --layout: describes the laid-out regions.</summary>
        public string LayoutReport()
        {
            return "form client  : " + ClientSize.Width + " x " + ClientSize.Height + Environment.NewLine +
                   "video panel  : " + _videoPanel.Bounds.Width + " x " + _videoPanel.Bounds.Height +
                       " at " + _videoPanel.Bounds.X + "," + _videoPanel.Bounds.Y + Environment.NewLine +
                   "device box   : " + _deviceBox.Bounds + Environment.NewLine +
                   "profile box  : " + _profileBox.Bounds + Environment.NewLine +
                   "log box      : " + _logBox.Bounds + Environment.NewLine +
                   "control list : " + _controlList.Bounds + Environment.NewLine +
                   "status label : " + _statusLabel.Bounds + Environment.NewLine +
                   "profiles dir : " + KeyMapStore.ProfilesDirectory();
        }

        // ---- window modes, live screen control ---------------------------------

        /// <summary>Keep-awake and screen-off cannot coexist: the device wins the fight.</summary>
        private void AwakeToggled()
        {
            if (_awakeCheck.Checked && _screenOffCheck.Checked)
            {
                _screenOffCheck.Checked = false;
                Log("[ui] 'Phone screen off' cleared: keeping the device awake would force the panel back on");
            }
        }

        private void ScreenOffToggled()
        {
            if (_screenOffCheck.Checked && _awakeCheck.Checked)
            {
                _awakeCheck.Checked = false;
                Log("[ui] keep-awake cleared: stay-awake overrides a screen-off request on the device");
            }
            SetStatus(_screenOffCheck.Checked
                ? "panel will be switched off on Connect (use Screen off for now)"
                : "panel stays on");
        }

        /// <summary>
        /// Drives the engine's own screen-off/screen-on shortcut (MOD+o / MOD+Shift+o) by
        /// posting it into the viewer window, so the panel can be blanked while mirroring
        /// without reconnecting. The engine confirms it on stdout.
        /// </summary>
        private void SendScreenShortcut(bool turnOn)
        {
            IntPtr viewer = _engine == null ? IntPtr.Zero : _engine.WindowHandle();
            if (viewer == IntPtr.Zero) { SetStatus("connect first - screen shortcuts drive the running mirror"); return; }
            Native.PostShortcut(viewer, true, turnOn, (int)'O');
            Log(turnOn
                ? "[ui] asked the engine to turn the device panel ON"
                : "[ui] asked the engine to turn the device panel OFF (mirroring continues)");
            SetStatus(turnOn ? "turning the panel on" : "panel off - mirroring keeps working");
        }

        private void ApplyTopmost()
        {
            if (_engine == null) { return; }
            _engine.Options.AlwaysOnTop = _topmostCheck.Checked;
            _engine.SetViewerTopmost(_topmostCheck.Checked);
            Log(_topmostCheck.Checked ? "[ui] mirror window is now always on top" : "[ui] always-on-top off");
        }

        private void ToggleCompact() { SetCompact(!_compact); }

        /// <summary>Hides the control side panel so only the mirror is left in the window.</summary>
        private void SetCompact(bool on)
        {
            _compact = on;
            if (_sidePanel != null) { _sidePanel.Visible = !on; }
            if (_compactButton != null) { _compactButton.Text = on ? "Full view" : "Compact view"; }
            Log(on ? "[ui] compact view: side panel hidden (Ctrl+M brings it back)"
                   : "[ui] full view restored");
            RepositionEmbedded();
        }

        private void ToggleFullscreen()
        {
            if (_fullscreen) { LeaveFullscreen(); return; }
            _fullscreen = true;
            if (_topPanel != null) { _topPanel.Visible = false; }
            if (_sidePanel != null) { _sidePanel.Visible = false; }
            if (_statusLabel != null) { _statusLabel.Visible = false; }
            FormBorderStyle = FormBorderStyle.None;
            Bounds = Screen.FromControl(this).Bounds;
            Log("[ui] fullscreen - F11 or Esc comes back");
            RepositionEmbedded();
        }

        private void LeaveFullscreen()
        {
            if (!_fullscreen) { return; }
            _fullscreen = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            if (_topPanel != null) { _topPanel.Visible = true; }
            if (_sidePanel != null) { _sidePanel.Visible = !_compact; }
            if (_statusLabel != null) { _statusLabel.Visible = true; }
            Bounds = new Rectangle(Bounds.X + 40, Bounds.Y + 40, 1220, 780);
            Log("[ui] fullscreen left");
            RepositionEmbedded();
        }

        /// <summary>
        /// Moves the mirror between this window's panel and its own top-level window, so
        /// the phone screen can be watched while OpenMirror is minimised.
        /// </summary>
        private void PopOutMirror()
        {
            if (_engine == null || !_engine.Running) { SetStatus("connect first"); return; }
            if (_engine.Embedded)
            {
                _engine.Detach();
                if (_popButton != null) { _popButton.Text = "Dock mirror"; }
                SetStatus("mirror is its own window - OpenMirror can be minimised now");
            }
            else
            {
                StartEmbed();
                if (_popButton != null) { _popButton.Text = "Pop out mirror"; }
                SetStatus("mirror docked back into this window");
            }
        }

        /// <summary>
        /// An embedded mirror is a child window, so minimising would hide the phone too.
        /// Pop it out instead, and dock it again when the window comes back.
        /// </summary>
        private void MinimizedEmbedded()
        {
            if (_engine == null || !_engine.Embedded) { return; }
            _engine.Detach();
            _reembedAfterRestore = true;
            Log("[ui] mirror popped out so it stays visible while this window is minimised");
        }

        private void RestoreEmbed()
        {
            if (!_reembedAfterRestore) { return; }
            _reembedAfterRestore = false;
            if (_engine != null && _engine.Running)
            {
                StartEmbed();
                Log("[ui] mirror docked back in");
            }
        }

        private void OpenWireless()
        {
            WirelessForm dialog = new WirelessForm(_adb,
                delegate { return SelectedSerial(); },
                Log,
                delegate
                {
                    // The dialog saves whichever wireless address worked; take it from there, because
                    // that is what the refresh loop reconnects to when the transport disappears.
                    _wirelessAddress = WirelessStore.LoadAddress();
                    RefreshDevices();
                });
            dialog.Show(this);
        }

        private void ApplyMapping()
        {
            if (_mapCheck.Checked)
            {
                if (_mapper == null)
                {
                    if (_profile == null) { Log("[mapper] no profile loaded"); return; }
                    _mapper = new KeyMapper(_profile, Log);
                    _mapper.WindowProvider = delegate { return _engine.WindowHandle(); };
                    _mapper.FocusProvider = delegate { return Handle; };
                    _mapper.Install();
                }
                _mapper.SetActive(true);
                SetStatus("key mapping on - F8 toggles");
            }
            else if (_mapper != null)
            {
                _mapper.SetActive(false);
                SetStatus("key mapping off");
            }
        }

        private void ApplyOverlay()
        {
            if (!_overlayCheck.Checked)
            {
                if (_overlay != null) { _overlay.Hide(); }
                return;
            }
            if (_overlay == null || _overlay.IsDisposed)
            {
                _overlay = new OverlayForm();
                _overlay.Owner = this;
                _overlay.Show();
            }
            _overlay.Attach(_engine, _profile);
            _overlay.Show();
            Log("[overlay] widgets shown over the mirror");
        }

        private void SaveProfile()
        {
            if (_profile == null) { return; }
            string safe = "";
            foreach (char c in _profile.Name) { safe += char.IsLetterOrDigit(c) ? c.ToString() : "-"; }
            if (safe.Length == 0) { safe = "profile"; }
            string path = Path.Combine(KeyMapStore.ProfilesDirectory(), safe + ".json");
            KeyMapStore.Save(_profile, path);
            Log("[mapper] saved " + path);
            SetStatus("profile saved");
        }

        private void ResetProfile()
        {
            _profile = KeyMapStore.DefaultProfile();
            bool present = false;
            foreach (object item in _profileBox.Items)
            {
                KeyProfile existing = item as KeyProfile;
                if (existing != null && existing.Name == _profile.Name) { present = true; }
            }
            if (!present) { _profileBox.Items.Add(_profile); }
            _profileBox.SelectedItem = _profile;
            if (_mapper != null) { _mapper.Profile = _profile; }
            RefreshControlList();
            Log("[mapper] default profile restored (use Save profile to persist)");
        }
    }
}
