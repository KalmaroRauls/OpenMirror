using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace OpenMirror
{
    /// <summary>
    /// Win32 interop shared by the mirror host window, the key-mapping overlay and
    /// the input injector. Plain Win32 only - no third-party code.
    /// </summary>
    internal static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll")]
        internal static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        internal const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        internal static extern bool SetProcessDPIAware();

        internal const int GWL_STYLE = -16;
        internal const int GWL_EXSTYLE = -20;
        internal const int WS_CHILD = 0x40000000;
        internal const int WS_POPUP = unchecked((int)0x80000000);
        internal const int WS_CAPTION = 0x00C00000;
        internal const int WS_THICKFRAME = 0x00040000;
        internal const int WS_EX_LAYERED = 0x00080000;
        internal const int WS_EX_TRANSPARENT = 0x00000020;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const int SW_SHOW = 5;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        internal static RECT GetWindowBounds(IntPtr hwnd)
        {
            RECT r;
            if (GetWindowRect(hwnd, out r)) { return r; }
            return new RECT();
        }

        // ---- synthetic input (mouse -> touch via the mirror engine's mouse mode) --
        internal const int INPUT_MOUSE = 0;
        internal const uint MOUSEEVENTF_MOVE = 0x0001;
        internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
        internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        internal const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        internal const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        internal const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        internal const uint MOUSEEVENTF_WHEEL = 0x0800;
        internal const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        internal const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        internal const int SM_XVIRTUALSCREEN = 76;
        internal const int SM_YVIRTUALSCREEN = 77;
        internal const int SM_CXVIRTUALSCREEN = 78;
        internal const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public int type;
            public INPUTUNION u;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        /// <summary>Moves the cursor to an absolute virtual-screen pixel.</summary>
        internal static void MouseMoveAbsolute(int screenX, int screenY)
        {
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
            int vh = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));
            INPUT[] one = new INPUT[1];
            one[0].type = INPUT_MOUSE;
            one[0].u.mi.dx = (int)Math.Round((screenX - vx) * 65535.0 / (vw - 1));
            one[0].u.mi.dy = (int)Math.Round((screenY - vy) * 65535.0 / (vh - 1));
            one[0].u.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
            SendInput(1, one, Marshal.SizeOf(typeof(INPUT)));
        }

        internal static void MouseButton(uint flag)
        {
            INPUT[] one = new INPUT[1];
            one[0].type = INPUT_MOUSE;
            one[0].u.mi.dwFlags = flag;
            SendInput(1, one, Marshal.SizeOf(typeof(INPUT)));
        }

        internal static void MouseLeftDown() { MouseButton(MOUSEEVENTF_LEFTDOWN); }
        internal static void MouseLeftUp() { MouseButton(MOUSEEVENTF_LEFTUP); }

        // ---- global low-level keyboard hook ---------------------------------------
        internal delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr GetModuleHandle(string moduleName);

        internal const int WH_KEYBOARD_LL = 13;
        internal const int WM_KEYDOWN = 0x0100;
        internal const int WM_KEYUP = 0x0101;
        internal const int WM_SYSKEYDOWN = 0x0104;
        internal const int WM_SYSKEYUP = 0x0105;

        internal const int VK_SHIFT = 0x10;
        internal const int VK_MENU = 0x12;      // Alt - the viewer's shortcut modifier
        internal const int SCAN_ALT = 0x38;     // left Alt
        internal const int SCAN_SHIFT = 0x2A;   // left Shift

        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        internal static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOSIZE = 0x0001;

        private static IntPtr KeyLParam(int scanCode, bool isUp, bool altContext)
        {
            int value = (scanCode & 0xFF) << 16;
            if (altContext) { value |= 1 << 29; }
            if (isUp) { value |= unchecked((int)0xC0000000); }
            return (IntPtr)value;
        }

        /// <summary>
        /// Posts a MOD(+Shift)+key shortcut straight into the viewer window's message
        /// queue. Works whether the viewer is embedded (a child window) or floating, and
        /// does not depend on which window currently has focus. Used to drive the
        /// engine's own screen-off/screen-on shortcuts from the UI.
        /// </summary>
        internal static bool PostShortcut(IntPtr hwnd, bool alt, bool shift, int vk)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) { return false; }
            uint mapped = MapVirtualKey((uint)vk, 0);
            int scan = mapped == 0 ? 0 : (int)mapped;
            if (alt) { PostMessage(hwnd, WM_SYSKEYDOWN, (IntPtr)VK_MENU, KeyLParam(SCAN_ALT, false, true)); }
            if (shift) { PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_SHIFT, KeyLParam(SCAN_SHIFT, false, false)); }
            uint down = alt ? (uint)WM_SYSKEYDOWN : (uint)WM_KEYDOWN;
            uint up = alt ? (uint)WM_SYSKEYUP : (uint)WM_KEYUP;
            PostMessage(hwnd, down, (IntPtr)vk, KeyLParam(scan, false, alt));
            PostMessage(hwnd, up, (IntPtr)vk, KeyLParam(scan, true, alt));
            if (shift) { PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_SHIFT, KeyLParam(SCAN_SHIFT, true, false)); }
            if (alt) { PostMessage(hwnd, WM_SYSKEYUP, (IntPtr)VK_MENU, KeyLParam(SCAN_ALT, true, true)); }
            return true;
        }

        /// <summary>Gives a window of another process keyboard focus (hooks the queues).</summary>
        internal static void ForceFocus(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) { return; }
            uint target = GetWindowThreadProcessId(hwnd, IntPtr.Zero);
            uint current = GetCurrentThreadId();
            if (target == current) { SetFocus(hwnd); return; }
            if (AttachThreadInput(target, current, true))
            {
                SetFocus(hwnd);
                AttachThreadInput(target, current, false);
            }
        }

        /// <summary>
        /// Best effort: makes another process's top-level window foreground and focused, so
        /// shortcuts posted into it behave like real user input.
        /// </summary>
        internal static bool ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) { return false; }
            IntPtr root = GetAncestor(hwnd, GA_ROOT);
            if (root == IntPtr.Zero) { root = hwnd; }
            uint target = GetWindowThreadProcessId(root, IntPtr.Zero);
            uint current = GetCurrentThreadId();
            bool attached = false;
            if (target != current) { attached = AttachThreadInput(target, current, true); }
            ShowWindow(root, SW_SHOW);
            bool ok = SetForegroundWindow(root);
            SetFocus(attached ? root : hwnd);
            if (attached) { AttachThreadInput(target, current, false); }
            return ok;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // ---- console attachment (a GUI-subsystem exe has no console of its own) ----
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool AttachConsole(uint processId);

        internal const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
    }
}
