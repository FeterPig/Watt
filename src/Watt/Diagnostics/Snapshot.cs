using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Watt.Models;
using Watt.UI;

namespace Watt.Diagnostics
{
    /// <summary>
    /// Renders the panel to PNG files without showing a window. Used for design review
    /// and for verifying layout on machines that cannot run the tray app interactively.
    /// </summary>
    public static class Snapshot
    {
        public static void Render(string directory)
        {
            Directory.CreateDirectory(directory);

            RenderTo(Path.Combine(directory, "panel-light-3.png"), Theme.Light, DemoData.Three(), scanning: false);
            RenderTo(Path.Combine(directory, "panel-dark-3.png"), Theme.Dark, DemoData.Three(), scanning: false);

            RenderTo(Path.Combine(directory, "panel-light-1.png"), Theme.Light, DemoData.Two(), scanning: false);
            RenderTo(Path.Combine(directory, "panel-light-low.png"), Theme.Light, DemoData.OneLow(), scanning: false);
            RenderTo(Path.Combine(directory, "panel-light-empty.png"), Theme.Light, DemoData.Empty(), scanning: false);
            RenderTo(Path.Combine(directory, "panel-dark-empty.png"), Theme.Dark, DemoData.Empty(), scanning: false);
            RenderTo(Path.Combine(directory, "panel-light-loading.png"), Theme.Light, DemoData.Empty(), scanning: true);
            RenderTo(Path.Combine(directory, "panel-dark-loading.png"), Theme.Dark, DemoData.Empty(), scanning: true);
        }

        private static void RenderTo(string filePath, Theme theme, IReadOnlyList<PeripheralDevice> devices, bool scanning)
        {
            var view = new PanelView();
            view.ApplyTheme(theme);
            view.SetDevices(devices, scanning);

            const double scale = 2.0;
            double width = PanelView.PanelWidth + PanelView.ShadowMargin * 2;
            view.Width = width;
            view.Measure(new Size(width, double.PositiveInfinity));
            view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
            view.UpdateLayout();

            int pixelWidth = (int)Math.Ceiling(view.ActualWidth * scale);
            int pixelHeight = (int)Math.Ceiling(view.ActualHeight * scale);
            var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
            bitmap.Render(view);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(filePath))
            {
                encoder.Save(stream);
            }
        }
    }
}
