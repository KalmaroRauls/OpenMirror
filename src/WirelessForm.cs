using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenMirror
{
    /// <summary>
    /// Wireless adb helper: plug in USB once to flip the device into TCP mode, then work
    /// over Wi-Fi. Android 11+ pairing (with its 6-digit code) is supported too.
    /// </summary>
    internal sealed class WirelessForm : Form
    {
        private readonly Adb _adb;
        private readonly Func<string> _serialProvider;
        private readonly Action<string> _log;
        private readonly Action _onChanged;
        private TextBox _hostBox;
        private TextBox _portBox;
        private TextBox _pairBox;
        private TextBox _codeBox;
        private TextBox _logBox;
        private Label _status;

        public WirelessForm(Adb adb, Func<string> serialProvider, Action<string> log, Action onChanged)
        {
            _adb = adb;
            _serialProvider = serialProvider;
            _log = log;
            _onChanged = onChanged;

            Text = "OpenMirror - wireless debugging";
            Icon = AppIcon.Value;
            Width = 560;
            Height = 430;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;

            Label info = new Label();
            info.AutoSize = true;
            info.Location = new Point(12, 10);
            info.Text = "Step 1 needs the USB cable once, to switch the phone to TCP/IP mode.\n" +
                        "Afterwards both devices just need to be on the same Wi-Fi network.";
            Controls.Add(info);

            Controls.Add(LabelAt("Phone address", 12, 62));
            _hostBox = new TextBox();
            _hostBox.Location = new Point(118, 58);
            _hostBox.Width = 150;
            Controls.Add(_hostBox);

            Controls.Add(LabelAt("port", 276, 62));
            _portBox = new TextBox();
            _portBox.Text = "5555";
            _portBox.Location = new Point(308, 58);
            _portBox.Width = 56;
            Controls.Add(_portBox);

            Btn("Detect from device", 374, 57, 156, delegate { Detect(); });

            Btn("1. Enable TCP/IP (USB)", 12, 96, 170, delegate { Step("tcpip"); });
            Btn("2. Connect over Wi-Fi", 190, 96, 170, delegate { Step("connect"); });
            Btn("Disconnect", 368, 96, 162, delegate { Step("disconnect"); });

            Btn("Back to USB mode", 12, 130, 170, delegate { Step("usb"); });

            Controls.Add(LabelAt("Android 11+ pairing", 12, 170));
            _pairBox = new TextBox();
            _pairBox.Location = new Point(148, 166);
            _pairBox.Width = 150;
            Controls.Add(_pairBox);
            Controls.Add(LabelAt("code", 308, 170));
            _codeBox = new TextBox();
            _codeBox.Location = new Point(350, 166);
            _codeBox.Width = 74;
            Controls.Add(_codeBox);
            Btn("Pair", 432, 165, 98, delegate { Step("pair"); });

            _status = new Label();
            _status.AutoSize = true;
            _status.Location = new Point(12, 196);
            _status.Text = "ready";
            Controls.Add(_status);

            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.Location = new Point(12, 220);
            _logBox.Size = new Size(518, 160);
            Controls.Add(_logBox);

            Detect();
        }

        private void Step(string action)
        {
            string serial = _serialProvider();
            string host = _hostBox.Text.Trim();
            string port = _portBox.Text.Trim();
            string target = host.Length == 0 ? "" : (host + ":" + port);
            Adb.Result result = null;

            if (action == "tcpip")
            {
                if (serial.Length == 0) { Say("select a USB device in the main window first"); return; }
                int portNumber;
                if (!int.TryParse(port, out portNumber)) { portNumber = 5555; }
                Say("switching " + serial + " to TCP mode on port " + portNumber + " ...");
                result = _adb.EnableTcpip(serial, portNumber);
            }
            else if (action == "connect")
            {
                if (target.Length == 0) { Say("enter the phone's address first"); return; }
                Say("connecting to " + target + " ...");
                result = _adb.Connect(target);
            }
            else if (action == "disconnect")
            {
                if (target.Length == 0) { Say("enter the phone's address first"); return; }
                result = _adb.Disconnect(target);
            }
            else if (action == "usb")
            {
                if (serial.Length == 0) { Say("enter the device serial of the USB connection"); return; }
                Say("restoring USB mode on " + serial + " ...");
                result = _adb.Run(Adb.DeviceFlag(serial) + " usb", 20000);
            }
            else if (action == "pair")
            {
                if (_pairBox.Text.Trim().Length == 0) { Say("enter the ip:port from the phone's pairing dialog"); return; }
                Say("pairing with " + _pairBox.Text.Trim() + " ...");
                result = _adb.Pair(_pairBox.Text.Trim(), _codeBox.Text.Trim());
            }

            if (result != null)
            {
                Say(result.All.Length == 0 ? "(no output)" : Adb.Preview(result.All));
                _status.Text = result.Ok ? "ok" : "failed (exit " + result.ExitCode + ")";
                _log("[wireless] " + action + " -> " + Adb.Preview(result.All));
            }
            if (_onChanged != null) { _onChanged(); }
        }

        /// <summary>Reads the phone's Wi-Fi address so it does not have to be hunted down.</summary>
        private void Detect()
        {
            string serial = _serialProvider();
            if (serial.Length == 0) { Say("no device selected yet - plug in USB or connect first"); return; }
            string route = _adb.Shell(serial, "ip route", 15000).All;
            string found = "";
            string fallback = "";
            foreach (string raw in route.Split('\n'))
            {
                string line = raw.Trim();
                int at = line.IndexOf("src ");
                if (at < 0) { continue; }
                string rest = line.Substring(at + 4).Trim();
                int space = rest.IndexOf(' ');
                if (space > 0) { rest = rest.Substring(0, space); }
                if (!(rest.StartsWith("10.") || rest.StartsWith("192.168.") || rest.StartsWith("172."))) { continue; }
                // Prefer the Wi-Fi interface; VPN/virtual interfaces answer first otherwise.
                if (line.IndexOf("wlan") >= 0) { found = rest; }
                else if (fallback.Length == 0) { fallback = rest; }
            }
            if (found.Length == 0) { found = fallback; }
            if (found.Length > 0)
            {
                _hostBox.Text = found;
                Say("device Wi-Fi address: " + found + ":" + _portBox.Text);
            }
            else
            {
                Say("could not read a Wi-Fi address; find it in Settings > About phone > status");
            }
        }

        private void Say(string line)
        {
            _logBox.AppendText(line + Environment.NewLine);
        }

        /// <summary>Diagnostic (used by --layout): the address the box ended up with.</summary>
        public string AddressText { get { return _hostBox.Text; } }

        /// <summary>Diagnostic (used by --layout): what the dialog reported.</summary>
        public string LogText { get { return _logBox.Text; } }

        private static Label LabelAt(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Location = new Point(x, y);
            return label;
        }

        private Button Btn(string text, int x, int y, int width, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Width = width;
            b.Click += onClick;
            Controls.Add(b);
            return b;
        }
    }
}
