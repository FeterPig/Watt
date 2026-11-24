using System;
using System.Drawing;
using System.Windows.Forms;

namespace Watt.Tray
{
    /// <summary>
    /// Owns the notification-area icon and its minimal native menu. Left click toggles the
    /// floating panel; right click offers only the actions a tray app actually needs:
    /// open the plugin folder, autostart and quit. All visual styling lives here so no
    /// second-level menu ever leaks into the floating panel.
    /// </summary>
    public sealed class TrayController : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _autostartItem;
        private bool _disposed;

        public TrayController()
        {
            var openPluginsItem = new ToolStripMenuItem("打开插件目录");
            openPluginsItem.Click += delegate { Raise(OpenPluginFolderRequested); };

            _autostartItem = new ToolStripMenuItem("开机自启");
            _autostartItem.Click += delegate { Raise(AutostartToggleRequested); };

            var exitItem = new ToolStripMenuItem("退出 Watt");
            exitItem.Click += delegate { Raise(ExitRequested); };

            _menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = true,
                Padding = new Padding(0, 4, 0, 4),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
                Renderer = new FlatMenuRenderer()
            };
            _menu.Items.Add(openPluginsItem);
            _menu.Items.Add(_autostartItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon
            {
                ContextMenuStrip = _menu,
                Visible = false,
                Text = "Watt"
            };
            _notifyIcon.MouseClick += OnMouseClick;
        }

        public event EventHandler Clicked;
        public event EventHandler ExitRequested;
        public event EventHandler AutostartToggleRequested;
        public event EventHandler OpenPluginFolderRequested;

        public Icon CurrentIcon { get; private set; }

        public void Show(Icon icon, bool autostartEnabled)
        {
            ApplyIcon(icon);
            _autostartItem.Checked = autostartEnabled;
            _notifyIcon.Visible = true;
        }

        public void ApplyIcon(Icon icon)
        {
            if (icon == null) return;
            CurrentIcon = icon;
            _notifyIcon.Icon = icon;
        }

        /// <summary>
        /// Re-adds the notification icon. Explorer sometimes does not create the
        /// NotifyIconSettings entry until an icon is registered again (for example after an
        /// in-place upgrade); re-registering lets the tray promotion retry find its entry.
        /// </summary>
        public void ReRegister()
        {
            if (_disposed) return;
            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Visible = true;
            }
            catch
            {
                // The icon may be gone during an Explorer restart; the next retry handles it.
            }
        }

        public void SetAutostart(bool enabled)
        {
            _autostartItem.Checked = enabled;
        }

        public void SetTooltip(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _notifyIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        public void SetTheme(bool taskbarLight)
        {
            ApplyIcon(TrayAssets.LoadTrayIcon(!taskbarLight));
            _menu.Renderer = new FlatMenuRenderer();
            _menu.BackColor = taskbarLight ? Color.FromArgb(252, 252, 253) : Color.FromArgb(32, 32, 36);
            _menu.ForeColor = taskbarLight ? Color.FromArgb(23, 24, 26) : Color.FromArgb(244, 244, 246);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
        }

        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Raise(Clicked);
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>Flat, monochrome menu that follows the taskbar theme instead of the
        /// dated gradient defaults of ToolStripProfessionalRenderer.</summary>
        private sealed class FlatMenuRenderer : ToolStripRenderer
        {
            private static readonly Color LightHover = Color.FromArgb(242, 242, 245);
            private static readonly Color DarkHover = Color.FromArgb(48, 48, 54);

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var brush = new SolidBrush(e.ToolStrip.BackColor))
                {
                    e.Graphics.FillRectangle(brush, e.AffectedBounds);
                }
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected) return;
                var dark = e.ToolStrip.BackColor.GetBrightness() < 0.5f;
                using (var brush = new SolidBrush(dark ? DarkHover : LightHover))
                {
                    e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
                }
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.ToolStrip.ForeColor;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                var dark = e.ToolStrip.BackColor.GetBrightness() < 0.5f;
                using (var pen = new Pen(dark ? Color.FromArgb(60, 60, 66) : Color.FromArgb(230, 230, 234)))
                {
                    e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
                }
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                // Draw our own checkmark so it stays visible on both light and dark menus.
                var rect = e.ImageRectangle;
                if (rect.IsEmpty)
                {
                    rect = new Rectangle(4, (e.Item.Height - 16) / 2, 16, 16);
                }

                using (var pen = new Pen(e.ToolStrip.ForeColor, 1.6f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    e.Graphics.DrawLines(pen, new[]
                    {
                        new PointF(rect.Left + rect.Width * 0.18f, rect.Top + rect.Height * 0.52f),
                        new PointF(rect.Left + rect.Width * 0.42f, rect.Top + rect.Height * 0.76f),
                        new PointF(rect.Left + rect.Width * 0.82f, rect.Top + rect.Height * 0.26f)
                    });
                }
            }
        }
    }
}
