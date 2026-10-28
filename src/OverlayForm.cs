using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenMirror
{
    /// <summary>
    /// Click-through, always-on-top widget layer drawn over the mirror window so the
    /// mapped controls can be lined up with the game's own on-screen buttons.
    /// WS_EX_TRANSPARENT keeps the mouse flowing to the mirror underneath - the
    /// overlay never steals input.
    /// </summary>
    internal sealed class OverlayForm : Form
    {
        private Engine _engine;
        private KeyProfile _profile;
        private Timer _track;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Fuchsia;
            TransparencyKey = Color.Fuchsia;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(10, 10);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _track = new Timer();
            _track.Interval = 150;
            _track.Tick += delegate(object s, EventArgs e) { Follow(); };
            _track.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
                              Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public void Attach(Engine engine, KeyProfile profile)
        {
            _engine = engine;
            _profile = profile;
            Follow();
            Invalidate();
        }

        /// <summary>Glues the overlay to the mirror window rectangle.</summary>
        private void Follow()
        {
            IntPtr win = _engine == null ? IntPtr.Zero : _engine.WindowHandle();
            if (win == IntPtr.Zero || !Native.IsWindow(win))
            {
                if (Visible) { Hide(); }
                return;
            }
            Native.RECT r = Native.GetWindowBounds(win);
            if (r.Width < 40 || r.Height < 40)
            {
                if (Visible) { Hide(); }
                return;
            }
            if (Bounds.Left != r.Left || Bounds.Top != r.Top || Width != r.Width || Height != r.Height)
            {
                SetBounds(r.Left, r.Top, r.Width, r.Height);
            }
            if (!Visible) { Show(); }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_profile == null || _profile.Targets.Count == 0) { return; }

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int min = Math.Min(ClientSize.Width, ClientSize.Height);

            using (Pen pen = new Pen(Color.FromArgb(235, 90, 220, 130), 2f))
            using (Brush fill = new SolidBrush(Color.FromArgb(70, 40, 200, 110)))
            using (Brush text = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            using (Font font = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            {
                foreach (MapTarget t in _profile.Targets)
                {
                    int cx = (int)Math.Round(t.X * ClientSize.Width);
                    int cy = (int)Math.Round(t.Y * ClientSize.Height);
                    if (t.Kind == MapKind.Stick)
                    {
                        int radius = Math.Max(8, (int)Math.Round(t.Radius * min));
                        g.FillEllipse(fill, cx - radius, cy - radius, radius * 2, radius * 2);
                        g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
                        g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
                        string keys = VkNames.Name(t.KeyUp) + VkNames.Name(t.KeyDown) +
                                      VkNames.Name(t.KeyLeft) + VkNames.Name(t.KeyRight);
                        g.DrawString(t.Label + "  [" + keys + "]", font, text, cx - radius, cy + radius + 2);
                    }
                    else
                    {
                        int radius = 22;
                        g.FillEllipse(fill, cx - radius, cy - radius, radius * 2, radius * 2);
                        g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
                        g.DrawString(t.Label, font, text, cx - radius + 5, cy - 14);
                        g.DrawString("[" + VkNames.Name(t.KeyTap) + "]", font, text, cx - radius + 5, cy + 1);
                    }
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_track != null) { _track.Stop(); }
            base.OnFormClosing(e);
        }
    }
}
