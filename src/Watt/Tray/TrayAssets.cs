using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Watt.Tray
{
    /// <summary>
    /// Loads the generated tray icons that are embedded into the executable. Falls back to a
    /// simple runtime-drawn battery glyph if the assets are missing (e.g. during development).
    /// </summary>
    public static class TrayAssets
    {
        public static Icon LoadTrayIcon(bool darkTaskbar)
        {
            var icon = LoadEmbedded(darkTaskbar ? "Watt.Assets.tray-dark.ico" : "Watt.Assets.tray-light.ico");
            if (icon != null) return icon;
            return DrawFallback(darkTaskbar ? Color.White : Color.Black);
        }

        public static Icon LoadAppIcon()
        {
            return LoadEmbedded("Watt.Assets.Watt.ico") ?? LoadTrayIcon(false);
        }

        private static Icon LoadEmbedded(string resourceName)
        {
            try
            {
                var assembly = typeof(TrayAssets).Assembly;
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    return new Icon(stream);
                }
            }
            catch
            {
                return null;
            }
        }

        private static Icon DrawFallback(Color color)
        {
            using (var bitmap = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var pen = new Pen(color, 2f))
                    using (var cap = new SolidBrush(color))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        pen.LineJoin = LineJoin.Round;
                        g.DrawRectangle(pen, 6, 8, 19, 15);
                        g.FillRectangle(cap, 8, 15, 8, 2);
                        g.FillRectangle(cap, 26, 13, 2, 5);
                    }
                }

                var handle = bitmap.GetHicon();
                try
                {
                    using (var icon = Icon.FromHandle(handle))
                    {
                        return (Icon)icon.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern bool DestroyIcon(IntPtr hIcon);
        }
    }
}
