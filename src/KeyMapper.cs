using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace OpenMirror
{
    internal enum MapKind { Tap, Hold, Stick }

    /// <summary>
    /// One on-screen control. Positions are stored as fractions of the mirror
    /// window so a profile survives resolution changes and window resizing.
    /// </summary>
    internal sealed class MapTarget
    {
        public string Label = "control";
        public MapKind Kind = MapKind.Tap;
        public double X = 0.5;          // 0..1 of client width
        public double Y = 0.5;          // 0..1 of client height
        public double Radius = 0.08;    // stick deflection, fraction of min(w,h)
        public int TapMs = 50;
        public int KeyTap;              // Tap: the key; Hold: the key
        public int KeyUp;               // Stick directions
        public int KeyDown;
        public int KeyLeft;
        public int KeyRight;

        public bool UsesKey(int vk)
        {
            if (vk == 0) { return false; }
            if (Kind == MapKind.Stick)
            {
                return vk == KeyUp || vk == KeyDown || vk == KeyLeft || vk == KeyRight;
            }
            return vk == KeyTap;
        }

        public string Describe()
        {
            if (Kind == MapKind.Stick)
            {
                return Kind + " " + Label + " (" + VkNames.Name(KeyUp) + "/" + VkNames.Name(KeyDown) +
                       "/" + VkNames.Name(KeyLeft) + "/" + VkNames.Name(KeyRight) + ")";
            }
            return Kind + " " + Label + " <- " + VkNames.Name(KeyTap);
        }
    }

    /// <summary>A named set of controls plus its activation hotkey.</summary>
    internal sealed class KeyProfile
    {
        public string Name = "default";
        public string PackageHint = "";
        public int ToggleHotkey = 0x77;   // F8
        public bool Enabled = true;
        public List<MapTarget> Targets = new List<MapTarget>();

        public override string ToString() { return Name + "  (" + Targets.Count + " controls)"; }

        public List<MapTarget> Match(int vk)
        {
            List<MapTarget> hits = new List<MapTarget>();
            foreach (MapTarget t in Targets)
            {
                if (t.UsesKey(vk)) { hits.Add(t); }
            }
            return hits;
        }
    }

    /// <summary>Loads and saves key profiles as plain JSON (no external libraries).</summary>
    internal static class KeyMapStore
    {
        public static string ProfilesDirectory()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
            if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
            return dir;
        }

        public static KeyProfile Load(string path)
        {
            try
            {
                string text = File.ReadAllText(path);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                KeyProfile p = ser.Deserialize<KeyProfile>(text);
                if (p == null) { return DefaultProfile(); }
                if (p.Targets == null) { p.Targets = new List<MapTarget>(); }
                return p;
            }
            catch { return DefaultProfile(); }
        }

        public static void Save(KeyProfile profile, string path)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 8 * 1024 * 1024;
            File.WriteAllText(path, ser.Serialize(profile));
        }

        public static List<KeyProfile> LoadAll()
        {
            List<KeyProfile> profiles = new List<KeyProfile>();
            foreach (string file in Directory.GetFiles(ProfilesDirectory(), "*.json"))
            {
                KeyProfile p = Load(file);
                profiles.Add(p);
            }
            if (profiles.Count == 0)
            {
                KeyProfile d = DefaultProfile();
                Save(d, Path.Combine(ProfilesDirectory(), "default.json"));
                profiles.Add(d);
            }
            return profiles;
        }

        /// <summary>
        /// A usable starting point for a left-stick + fire layout, i.e. the two
        /// things every dual-stick shooter needs on day one.
        /// </summary>
        public static KeyProfile DefaultProfile()
        {
            KeyProfile p = new KeyProfile();
            p.Name = "shooter-default";
            MapTarget stick = new MapTarget();
            stick.Label = "left stick";
            stick.Kind = MapKind.Stick;
            stick.X = 0.22; stick.Y = 0.75; stick.Radius = 0.09;
            stick.KeyUp = 0x57;    // W
            stick.KeyDown = 0x53;  // S
            stick.KeyLeft = 0x41;  // A
            stick.KeyRight = 0x44; // D
            p.Targets.Add(stick);

            MapTarget fire = new MapTarget();
            fire.Label = "fire";
            fire.Kind = MapKind.Tap;
            fire.X = 0.85; fire.Y = 0.72; fire.TapMs = 60;
            fire.KeyTap = 0x4C; // L
            p.Targets.Add(fire);

            MapTarget hold = new MapTarget();
            hold.Label = "aim";
            hold.Kind = MapKind.Hold;
            hold.X = 0.72; hold.Y = 0.82;
            hold.KeyTap = 0x4B; // K
            p.Targets.Add(hold);
            return p;
        }
    }

    /// <summary>Human-readable names for the virtual keys we care about.</summary>
    internal static class VkNames
    {
        private static readonly Dictionary<int, string> Map = Build();

        private static Dictionary<int, string> Build()
        {
            Dictionary<int, string> m = new Dictionary<int, string>();
            for (int i = 0x30; i <= 0x39; i++) { m[i] = ((char)i).ToString(); }          // 0-9
            for (int i = 0x41; i <= 0x5A; i++) { m[i] = ((char)i).ToString(); }          // A-Z
            for (int i = 0x70; i <= 0x7B; i++) { m[i] = "F" + (i - 0x6F); }              // F1-F12
            m[0x20] = "Space"; m[0x0D] = "Enter"; m[0x1B] = "Esc"; m[0x09] = "Tab";
            m[0x10] = "Shift"; m[0x11] = "Ctrl"; m[0x12] = "Alt";
            m[0x25] = "Left"; m[0x26] = "Up"; m[0x27] = "Right"; m[0x28] = "Down";
            m[0x2C] = "PrintScreen"; m[0x2D] = "Insert"; m[0x2E] = "Delete";
            m[0xBA] = ";"; m[0xBB] = "="; m[0xBC] = ","; m[0xBD] = "-"; m[0xBE] = "."; m[0xBF] = "/";
            m[0xDB] = "["; m[0xDC] = "\\"; m[0xDD] = "]"; m[0xDE] = "'"; m[0xC0] = "`";
            return m;
        }

        public static string Name(int vk)
        {
            if (vk == 0) { return "(none)"; }
            string name;
            if (Map.TryGetValue(vk, out name)) { return name; }
            return "0x" + vk.ToString("X2");
        }
    }

    /// <summary>
    /// Turns keyboard input into touch input on the mirror window. Because the
    /// engine's mouse mode maps the Windows cursor onto a single touch pointer,
    /// v1 drives one touch at a time - a stick or a hold owns the pointer while
    /// it is active. True multi-touch needs the engine control protocol directly
    /// (see README, "Known limits").
    /// </summary>
    internal sealed class KeyMapper : IDisposable
    {
        public KeyProfile Profile;
        public bool Active;

        private readonly Action<string> _log;
        private IntPtr _hook = IntPtr.Zero;
        private Native.LowLevelKeyboardProc _callback;
        private System.Windows.Forms.Timer _timer;
        private readonly Dictionary<int, bool> _held = new Dictionary<int, bool>();
        private MapTarget _dragOwner;
        private double _dragDx, _dragDy;

        /// <summary>Supplies the mirror window the touches must land on.</summary>
        public Func<IntPtr> WindowProvider;

        public event EventHandler StateChanged;

        public KeyMapper(KeyProfile profile, Action<string> log)
        {
            Profile = profile == null ? KeyMapStore.DefaultProfile() : profile;
            _log = log;
        }

        public bool HookInstalled { get { return _hook != IntPtr.Zero; } }
        public string DragState
        {
            get { return _dragOwner == null ? "idle" : _dragOwner.Label; }
        }

        public void Install()
        {
            if (_hook != IntPtr.Zero) { return; }
            _callback = new Native.LowLevelKeyboardProc(HookProc);
            _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _callback, Native.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                _log("[mapper] hook install failed (error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
            }
            else
            {
                _log("[mapper] keyboard hook installed; toggle key = " + VkNames.Name(Profile.ToggleHotkey));
            }
        }

        public void Uninstall()
        {
            ReleaseDrag();
            if (_hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
                _log("[mapper] keyboard hook removed");
            }
        }

        public void Dispose() { Uninstall(); StopTimer(); }

        private void StartTimer()
        {
            if (_timer == null)
            {
                _timer = new System.Windows.Forms.Timer();
                _timer.Interval = 12;
                _timer.Tick += delegate(object s, EventArgs e) { Tick(); };
            }
            _timer.Start();
        }

        private void StopTimer()
        {
            if (_timer != null) { _timer.Stop(); }
        }

        public void SetActive(bool active)
        {
            Active = active;
            if (Active) { StartTimer(); } else { ReleaseDrag(); StopTimer(); }
            _log("[mapper] mapping " + (Active ? "ON" : "OFF"));
            if (StateChanged != null) { StateChanged(this, EventArgs.Empty); }
        }

        /// <summary>Optional supplier of the window to foreground before a touch.</summary>
        public Func<IntPtr> FocusProvider;

        private MapTarget _pendingHold;

        private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0) { return Native.CallNextHookEx(_hook, code, wParam, lParam); }
            int msg = wParam.ToInt32();
            bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
            bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
            if (!down && !up) { return Native.CallNextHookEx(_hook, code, wParam, lParam); }

            Native.KBDLLHOOKSTRUCT data = (Native.KBDLLHOOKSTRUCT)
                System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
            int vk = (int)data.vkCode;

            // The activation hotkey always wins, even while mapping is off.
            if (vk == Profile.ToggleHotkey && down)
            {
                SetActive(!Active);
                return (IntPtr)1;
            }
            if (!Active) { return Native.CallNextHookEx(_hook, code, wParam, lParam); }

            List<MapTarget> hits = Profile.Match(vk);
            if (hits.Count == 0) { return Native.CallNextHookEx(_hook, code, wParam, lParam); }

            // Keep the callback short: flag work, let the timer/thread pool do it.
            if (down)
            {
                if (!_held.ContainsKey(vk))
                {
                    _held[vk] = true;
                    foreach (MapTarget t in hits)
                    {
                        if (t.Kind == MapKind.Tap) { QueueTap(t); }
                        else if (t.Kind == MapKind.Hold) { _pendingHold = t; }
                    }
                }
            }
            else
            {
                _held.Remove(vk);
                foreach (MapTarget t in hits)
                {
                    if (t.Kind == MapKind.Hold)
                    {
                        if (_pendingHold == t) { _pendingHold = null; }
                        if (_dragOwner == t) { ReleaseDrag(); }
                    }
                }
            }
            return (IntPtr)1;   // swallow: the phone must not see the raw key
        }

        private bool IsHeld(int vk)
        {
            bool value;
            return vk != 0 && _held.TryGetValue(vk, out value) && value;
        }

        private bool AnyDirectionHeld()
        {
            foreach (MapTarget t in Profile.Targets)
            {
                if (t.Kind != MapKind.Stick) { continue; }
                if (IsHeld(t.KeyUp) || IsHeld(t.KeyDown) || IsHeld(t.KeyLeft) || IsHeld(t.KeyRight)) { return true; }
            }
            return false;
        }

        private void QueueTap(MapTarget t)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate(object state) { PerformTap(t); });
        }

        private IntPtr FocusHandle()
        {
            IntPtr h = FocusProvider == null ? IntPtr.Zero : FocusProvider();
            if (h == IntPtr.Zero && WindowProvider != null) { h = WindowProvider(); }
            return h;
        }

        /// <summary>Maps target fractions onto a screen pixel inside the mirror window.</summary>
        private bool TryPoint(MapTarget t, double dx, double dy, ref int screenX, ref int screenY)
        {
            IntPtr win = WindowProvider == null ? IntPtr.Zero : WindowProvider();
            if (win == IntPtr.Zero || !Native.IsWindow(win)) { return false; }
            Native.RECT r = Native.GetWindowBounds(win);
            if (r.Width <= 1 || r.Height <= 1) { return false; }
            double fx = t.X + dx;
            double fy = t.Y + dy;
            if (fx < 0) { fx = 0; }
            if (fx > 1) { fx = 1; }
            if (fy < 0) { fy = 0; }
            if (fy > 1) { fy = 1; }
            screenX = r.Left + (int)Math.Round(fx * r.Width);
            screenY = r.Top + (int)Math.Round(fy * r.Height);
            return true;
        }

        private void PerformTap(MapTarget t)
        {
            int x = 0; int y = 0;
            if (!TryPoint(t, 0, 0, ref x, ref y)) { _log("[mapper] tap skipped: mirror window not found"); return; }
            IntPtr focus = FocusHandle();
            if (focus != IntPtr.Zero) { Native.SetForegroundWindow(focus); }
            Native.MouseMoveAbsolute(x, y);
            System.Threading.Thread.Sleep(20);
            Native.MouseLeftDown();
            System.Threading.Thread.Sleep(t.TapMs <= 0 ? 50 : t.TapMs);
            Native.MouseLeftUp();
        }

        private void BeginDrag(MapTarget t, double dx, double dy)
        {
            if (_dragOwner != null && _dragOwner != t) { ReleaseDrag(); }
            int x = 0; int y = 0;
            if (!TryPoint(t, 0, 0, ref x, ref y))
            {
                _log("[mapper] drag skipped: mirror window not found");
                _pendingHold = null;
                return;
            }
            IntPtr focus = FocusHandle();
            if (focus != IntPtr.Zero) { Native.SetForegroundWindow(focus); }
            Native.MouseMoveAbsolute(x, y);
            System.Threading.Thread.Sleep(20);
            Native.MouseLeftDown();
            _dragOwner = t;
            _dragDx = dx;
            _dragDy = dy;
            MoveDrag();
        }

        private void MoveDrag()
        {
            if (_dragOwner == null) { return; }
            int x = 0; int y = 0;
            if (!TryPoint(_dragOwner, _dragDx, _dragDy, ref x, ref y)) { return; }
            Native.MouseMoveAbsolute(x, y);
        }

        private void ReleaseDrag()
        {
            if (_dragOwner == null) { return; }
            Native.MouseLeftUp();
            _log("[mapper] released " + _dragOwner.Label);
            _dragOwner = null;
            _dragDx = 0;
            _dragDy = 0;
        }

        /// <summary>Drives held sticks and pending holds. Runs on the UI timer.</summary>
        private void Tick()
        {
            if (!Active) { return; }

            if (_pendingHold != null)
            {
                MapTarget hold = _pendingHold;
                _pendingHold = null;
                if (IsHeld(hold.KeyTap)) { BeginDrag(hold, 0, 0); }
            }

            MapTarget stick = null;
            double sdx = 0; double sdy = 0;
            foreach (MapTarget t in Profile.Targets)
            {
                if (t.Kind != MapKind.Stick) { continue; }
                double lx = 0; double ly = 0;
                if (IsHeld(t.KeyLeft)) { lx -= 1; }
                if (IsHeld(t.KeyRight)) { lx += 1; }
                if (IsHeld(t.KeyUp)) { ly -= 1; }
                if (IsHeld(t.KeyDown)) { ly += 1; }
                if (lx == 0 && ly == 0)
                {
                    if (_dragOwner == t) { ReleaseDrag(); }
                    continue;
                }
                double len = Math.Sqrt(lx * lx + ly * ly);
                stick = t;
                sdx = lx / len * t.Radius;
                sdy = ly / len * t.Radius;
                break;
            }

            if (stick == null)
            {
                if (_dragOwner != null && _dragOwner.Kind == MapKind.Stick && !AnyDirectionHeld()) { ReleaseDrag(); }
                return;
            }
            if (_dragOwner == null) { BeginDrag(stick, sdx, sdy); }
            else if (_dragOwner == stick) { _dragDx = sdx; _dragDy = sdy; MoveDrag(); }
        }
    }
}
