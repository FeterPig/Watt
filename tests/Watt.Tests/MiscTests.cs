using System;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Forms;
using Watt.Models;
using Watt.Tray;
using Watt.UI;

namespace Watt.Tests
{
    internal static class MiscTests
    {
        internal static void Register(TestRunner runner)
        {
            runner.Run("misc/theme brushes are frozen and glyphs map to kinds", ThemeAndGlyphs);
            runner.Run("misc/floating window wraps the panel without showing", FloatingWindowWrapper);
            runner.Run("misc/tray menu exposes the restore-hidden entry", TrayMenu);
        }

        private static void ThemeAndGlyphs()
        {
            Assert.True(Theme.Light.Surface.IsFrozen, "light surface frozen");
            Assert.True(Theme.Dark.Surface.IsFrozen, "dark surface frozen");
            Assert.False(Theme.Light.IsDark, "light theme flag");
            Assert.True(Theme.Dark.IsDark, "dark theme flag");

            Assert.Equal(Glyphs.Keyboard, Glyphs.ForKind(DeviceKind.Keyboard), "keyboard glyph");
            Assert.Equal(Glyphs.Mouse, Glyphs.ForKind(DeviceKind.Mouse), "mouse glyph");
            Assert.Equal(Glyphs.Headphone, Glyphs.ForKind(DeviceKind.Headphone), "headphone glyph");
            Assert.Equal(Glyphs.Other, Glyphs.ForKind(DeviceKind.Other), "other glyph");
        }

        private static void FloatingWindowWrapper()
        {
            var panel = new PanelView();
            panel.ApplyTheme(Theme.Light);
            var window = new FloatingWindow(panel);
            Assert.True(ReferenceEquals(panel, window.Panel), "panel is the window content");
            Assert.False(window.IsPanelVisible, "not visible before showing");

            window.ApplyTheme(Theme.Dark);
            Assert.Equal(Theme.Dark, panel.CurrentTheme, "theme forwarded to the panel");

            window.UpdateDevices(new[]
            {
                new PeripheralDevice
                {
                    Id = "d", Name = "Device", Kind = DeviceKind.Keyboard, BatteryPercent = 50
                }
            }, false);
            Assert.Equal("Device", ((DeviceRow)((ItemsControl)panel.FindName("DeviceList")).Items[0]).Name,
                "devices forwarded to the panel");

            window.Dismiss();
            Assert.False(window.IsPanelVisible, "dismiss on a hidden window is a no-op");
        }

        private static void TrayMenu()
        {
            using (var tray = new TrayController())
            {
                var field = typeof(TrayController).GetField("_menu",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(field, "menu field");
                var menu = (ContextMenuStrip)field.GetValue(tray);
                Assert.NotNull(menu, "menu created");

                Assert.Equal(5, menu.Items.Count, "open, autostart, restore, separator, exit");
                var restore = menu.Items.OfType<ToolStripMenuItem>()
                    .Single(item => item.Text == "恢复已隐藏设备");
                Assert.False(restore.Enabled, "restore disabled with nothing hidden");

                tray.SetRestoreHiddenEnabled(true);
                Assert.True(restore.Enabled, "restore enabled on request");

                tray.SetAutostart(true);
                var autostart = menu.Items.OfType<ToolStripMenuItem>()
                    .Single(item => item.Text == "开机自启");
                Assert.True(autostart.Checked, "autostart checkbox state");

                tray.SetTheme(true);
                tray.SetTheme(false);
                Assert.NotNull(tray.CurrentIcon, "theme sets an icon");
            }
        }
    }
}
