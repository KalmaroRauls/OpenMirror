using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace OpenMirror
{
    /// <summary>
    /// The application icon. It is compiled into the assembly by build.ps1
    /// (/win32icon for Explorer and the taskbar, /resource as app.ico for the windows),
    /// so the exe stays a single self-contained file.
    /// </summary>
    internal static class AppIcon
    {
        private const string ResourceName = "app.ico";
        private static Icon _cached;

        public static Icon Value
        {
            get
            {
                if (_cached == null) { _cached = Load(); }
                return _cached;
            }
        }

        private static Icon Load()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string[] names = asm.GetManifestResourceNames();
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].EndsWith(ResourceName, StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream s = asm.GetManifestResourceStream(names[i]))
                        {
                            // Ask for a larger frame than the title bar needs: Windows scales
                            // a sharp 64 px source down better than a 16 px one up.
                            if (s != null) { return new Icon(s, new Size(64, 64)); }
                        }
                    }
                }
            }
            catch { }

            try
            {
                // Not built with the resource (e.g. compiled by hand): look next to the exe.
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ResourceName);
                if (File.Exists(path)) { return new Icon(path); }
            }
            catch { }

            try
            {
                string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"assets\icon.ico");
                if (File.Exists(assets)) { return new Icon(assets); }
            }
            catch { }

            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            return SystemIcons.Application;
        }
    }
}
