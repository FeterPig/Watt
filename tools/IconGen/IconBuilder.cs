using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconGen
{
    /// <summary>
    /// Writes watt/assets/tray-light.ico, tray-dark.ico and Watt.ico and the review previews.
    /// Frames are rendered from the vector mark at each target size using 4x/8x supersampling,
    /// so 16 px gets the same sharpness as larger frames.
    /// </summary>
    internal static class IconBuilder
    {
        private static readonly int[] TraySizes = { 16, 20, 24, 32, 40, 48 };
        private static readonly int[] AppSizes = { 16, 20, 24, 32, 40, 48, 64, 256 };

        private static readonly Color LightBg = Render.Hex("FFF3F3F3");
        private static readonly Color DarkBg = Render.Hex("FF1C1C1E");
        private static readonly Color PageBg = Render.Hex("FFE9E9EC");
        private static readonly Color Ink = Render.Hex("FF1A1A1A");

        public static void Write(string assetsDir, string previewDir)
        {
            Directory.CreateDirectory(assetsDir);
            Directory.CreateDirectory(previewDir);

            if (!Fonts.FontAvailable)
            {
                Console.WriteLine("warning: 'Segoe Fluent Icons' not resolvable by WPF; Glyph variants may not render.");
            }

            string lightIco = Path.Combine(assetsDir, "tray-light.ico");
            string darkIco = Path.Combine(assetsDir, "tray-dark.ico");
            string appIco = Path.Combine(assetsDir, "Watt.ico");

            WriteTrayIcon(lightIco, Art.TrayLightInk);
            WriteTrayIcon(darkIco, Art.TrayDarkInk);
            WriteAppIcon(appIco);

            VerifyIco(lightIco, TraySizes);
            VerifyIco(darkIco, TraySizes);
            VerifyIco(appIco, AppSizes);

            WriteTraySizePreview(Path.Combine(previewDir, "tray-mark-sizes.png"));
            WriteTrayTaskbarPreview(Path.Combine(previewDir, "tray-taskbar-actual.png"));
            WriteTrayMagnifiedPreview(Path.Combine(previewDir, "tray-mark-magnified.png"));
            WriteFramesPreview(Path.Combine(previewDir, "tray-ico-frames.png"), lightIco, darkIco, "tray-light.ico", "tray-dark.ico", Art.TrayLightInk, Art.TrayDarkInk);
            WriteAppPreview(previewDir, appIco);
        }

        private static void WriteTrayIcon(string path, Color ink)
        {
            var frames = new List<IcoFrame>();
            foreach (int size in TraySizes)
            {
                byte[] px = MaskPixels(size, ink);
                AuditTrayPixels(Path.GetFileName(path) + " " + size + " px", px, ink);
                frames.Add(new IcoFrame(size, px));
            }
            IcoWriter.Write(path, frames);
            Console.WriteLine("wrote " + path + " (" + string.Join(", ", TraySizes) + " px)");
        }

        /// <summary>
        /// Quality bar: the tray line art must be pure black (#000000) or pure white (#FFFFFF) on a
        /// transparent background, with only antialiasing in the alpha channel - never grey ink.
        /// </summary>
        private static void AuditTrayPixels(string label, byte[] px, Color ink)
        {
            int opaque = 0;
            int transparent = 0;
            int badInk = 0;
            int minAlpha = 255;
            for (int i = 0; i < px.Length; i += 4)
            {
                byte a = px[i + 3];
                if (a == 0)
                {
                    transparent++;
                    continue;
                }
                opaque++;
                if (a < minAlpha)
                {
                    minAlpha = a;
                }
                if (px[i] != ink.B || px[i + 1] != ink.G || px[i + 2] != ink.R)
                {
                    badInk++;
                }
            }
            Console.WriteLine("audit " + label + ": " + opaque + " ink px, " + transparent
                + " transparent px, non-pure ink = " + badInk + " (colour #" + ink.R.ToString("X2")
                + ink.G.ToString("X2") + ink.B.ToString("X2") + ", min alpha " + minAlpha + ")");
        }

        private static void WriteAppIcon(string path)
        {
            var frames = new List<IcoFrame>();
            foreach (int size in AppSizes)
            {
                frames.Add(new IcoFrame(size, AppPixels(size)));
            }
            IcoWriter.Write(path, frames);
            Console.WriteLine("wrote " + path + " (" + string.Join(", ", AppSizes) + " px)");
        }

        private static byte[] MaskPixels(int size, Color ink)
        {
            int ss = size >= 64 ? 4 : 8;
            BitmapSource big = Render.Draw(size, size, ss, Art.Grid, Art.Grid,
                dc => Art.DrawTrayMark(dc, Art.TrayVariant, ink));
            return Render.GetPixels(Render.Downsample(big, ss));
        }

        private static byte[] AppPixels(int size)
        {
            int ss = size >= 128 ? 4 : 8;
            BitmapSource big = Render.Draw(size, size, ss, 256, 256,
                dc => Art.DrawAppTile(dc, Art.TileColor, Art.AppInk, Art.AppVariant, Art.AppMarkScale));
            return Render.GetPixels(Render.Downsample(big, ss));
        }

        /// <summary>Decodes the written .ico and checks the frame list/pixels round-trip.</summary>
        private static List<BitmapSource> VerifyIco(string path, int[] expectedSizes)
        {
            var decoder = new IconBitmapDecoder(new Uri(path, UriKind.Absolute),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frames = new List<BitmapSource>();
            foreach (BitmapFrame f in decoder.Frames)
            {
                frames.Add(f);
            }

            var seen = new List<int>();
            foreach (BitmapSource f in frames)
            {
                seen.Add(f.PixelWidth);
            }
            // Windows' icon decoder may reorder frames (largest first); compare as a set.
            var expectedSorted = new List<int>(expectedSizes);
            expectedSorted.Sort();
            var seenSorted = new List<int>(seen);
            seenSorted.Sort();
            bool ok = expectedSorted.Count == seenSorted.Count;
            if (ok)
            {
                for (int i = 0; i < expectedSorted.Count; i++)
                {
                    if (expectedSorted[i] != seenSorted[i])
                    {
                        ok = false;
                    }
                }
            }
            Console.WriteLine("verify " + Path.GetFileName(path) + ": " + frames.Count + " frames ["
                + string.Join(", ", seen) + "] " + (ok ? "all expected sizes OK" : "MISMATCH"));
            return frames;
        }

        private static BitmapSource Bgra(BitmapSource src)
        {
            if (src.Format == PixelFormats.Bgra32)
            {
                return src;
            }
            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            conv.Freeze();
            return conv;
        }

        private static void DrawIcon(DrawingContext dc, BitmapSource bmp, double x, double y)
        {
            dc.DrawImage(bmp, new Rect(x, y, bmp.PixelWidth, bmp.PixelHeight));
        }

        private static void DrawSwatch(DrawingContext dc, Color bg, double x, double y, double w, double h)
        {
            dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(x, y, w, h));
        }

        private static void WriteTraySizePreview(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128 };
            const int margin = 24;
            const int rowH = 150;
            int w = margin * 2 + 8 * 130;
            int h = margin * 2 + 56 + rowH * 2;

            BitmapSource sheet = Render.DrawPixels(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(PageBg), null, new Rect(0, 0, w, h));
                Render.DrawLabel(dc, "Tray mark, all generated sizes, light (#F3F3F3) and dark (#1C1C1E) taskbars", margin, 12, 15, Ink, true);
                Render.DrawLabel(dc, "Black ink for light taskbars (tray-light.ico), white ink for dark taskbars (tray-dark.ico).", margin, 32, 10,
                    Render.Hex("FF6A6A70"));

                for (int row = 0; row < 2; row++)
                {
                    bool dark = row == 1;
                    Color bg = dark ? DarkBg : LightBg;
                    Color ink = dark ? Colors.White : Colors.Black;
                    int y = margin + 56 + row * rowH;
                    dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(margin, y, w - margin * 2, rowH - 12));

                    for (int i = 0; i < sizes.Length; i++)
                    {
                        int s = sizes[i];
                        int x = margin + i * 130;
                        BitmapSource bmp = Render.FromPixels(MaskPixels(s, ink), s, s);
                        DrawIcon(dc, bmp, x + (130 - s) / 2.0, y + (rowH - 12 - s) / 2.0);
                        Render.DrawLabel(dc, s + " px", x, y + rowH - 26, 9, dark ? Render.Hex("FFB0B0B8") : Render.Hex("FF6A6A70"));
                    }
                }
            });
            Render.SavePng(sheet, path);
        }

        private static void WriteTrayTaskbarPreview(string path)
        {
            int[] sizes = { 16, 20, 24 };
            const int margin = 24;
            const int band = 46;
            int w = margin * 2 + 8 * 46 + 60;
            int h = margin * 2 + 52 + sizes.Length * 2 * (band + 10);

            BitmapSource sheet = Render.DrawPixels(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(PageBg), null, new Rect(0, 0, w, h));
                Render.DrawLabel(dc, "Taskbar simulation - every icon at its real pixel size (no scaling in this preview)", margin, 12, 15, Ink, true);
                Render.DrawLabel(dc, "Top of each pair: light taskbar with tray-light.ico. Bottom: dark taskbar with tray-dark.ico.", margin, 32, 10,
                    Render.Hex("FF6A6A70"));

                int y = margin + 52;
                foreach (int s in sizes)
                {
                    foreach (bool dark in new[] { false, true })
                    {
                        Color bg = dark ? DarkBg : LightBg;
                        Color ink = dark ? Colors.White : Colors.Black;
                        dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(margin, y, w - margin * 2, band));
                        BitmapSource bmp = Render.FromPixels(MaskPixels(s, ink), s, s);
                        for (int k = 0; k < 8; k++)
                        {
                            DrawIcon(dc, bmp, margin + 12 + k * 46 + (46 - s) / 2.0, y + (band - s) / 2.0);
                        }
                        Render.DrawLabel(dc, s + " px " + (dark ? "dark" : "light"), w - margin - 62, y + 16, 9,
                            dark ? Render.Hex("FFB0B0B8") : Render.Hex("FF6A6A70"));
                        y += band + 10;
                    }
                }
            });
            Render.SavePng(sheet, path);
        }

        private static void WriteTrayMagnifiedPreview(string path)
        {
            int[] sizes = { 16, 24, 32 };
            const int margin = 24;
            const int zoom = 6;
            int cellW = 32 * zoom + 40;
            int w = margin * 2 + sizes.Length * cellW;
            int h = margin * 2 + 56 + 2 * (32 * zoom + 44);

            BitmapSource sheet = Render.DrawPixels(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(PageBg), null, new Rect(0, 0, w, h));
                Render.DrawLabel(dc, "Tray mark pixel zoom (" + zoom + "x nearest neighbour): 16 px must stay readable", margin, 12, 15, Ink, true);
                Render.DrawLabel(dc, "No scaling filters in this view - it is the exact bitmap in the .ico, magnified.", margin, 32, 10,
                    Render.Hex("FF6A6A70"));

                for (int row = 0; row < 2; row++)
                {
                    bool dark = row == 1;
                    Color bg = dark ? DarkBg : LightBg;
                    Color ink = dark ? Colors.White : Colors.Black;
                    int y = margin + 56 + row * (32 * zoom + 44);
                    for (int i = 0; i < sizes.Length; i++)
                    {
                        int s = sizes[i];
                        int x = margin + i * cellW;
                        BitmapSource bmp = Render.FromPixels(MaskPixels(s, ink), s, s);
                        BitmapSource zoomed = Render.UpscaleNearest(bmp, zoom);
                        dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(x, y, zoomed.PixelWidth, zoomed.PixelHeight));
                        DrawIcon(dc, zoomed, x, y);
                        Render.DrawLabel(dc, s + " px " + (dark ? "on dark" : "on light"), x, y + zoomed.PixelHeight + 8, 9,
                            dark ? Render.Hex("FFB0B0B8") : Render.Hex("FF6A6A70"));
                    }
                }
            });
            Render.SavePng(sheet, path);
        }

        private static void WriteFramesPreview(string path, string lightIco, string darkIco, string lightName, string darkName,
            Color lightInk, Color darkInk)
        {
            const int margin = 24;
            const int cellW = 380;
            const int cellH = 120;
            int w = margin * 2 + 2 * cellW;
            int h = margin * 2 + 52 + 3 * cellH;

            var lightFrames = ReadFrames(lightIco);
            var darkFrames = ReadFrames(darkIco);

            BitmapSource sheet = Render.DrawPixels(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(PageBg), null, new Rect(0, 0, w, h));
                Render.DrawLabel(dc, "Frames decoded back from the generated .ico files (round-trip check)", margin, 12, 15, Ink, true);

                // Rows: 16, 24, 48 for both icons.
                int[] show = { 16, 24, 48 };
                for (int r = 0; r < show.Length; r++)
                {
                    int s = show[r];
                    int y = margin + 52 + r * cellH;
                    Render.DrawLabel(dc, s + " px", margin, y + 8, 11, Ink, true);

                    // light icon frames on a light card
                    dc.DrawRectangle(new SolidColorBrush(Colors.White), null, new Rect(margin + 50, y, cellW - 60, cellH - 12));
                    int x = margin + 50 + 12;
                    foreach (BitmapSource f in lightFrames)
                    {
                        if (f.PixelWidth == s)
                        {
                            DrawIcon(dc, f, x, y + (cellH - 12 - s) / 2.0);
                        }
                    }
                    BitmapSource lightZoom = Render.UpscaleNearest(FindFrame(lightFrames, s), 2);
                    DrawIcon(dc, lightZoom, margin + 50 + 12 + s + 12, y + (cellH - 12 - lightZoom.PixelHeight) / 2.0);

                    // dark icon frames on a dark card
                    dc.DrawRectangle(new SolidColorBrush(DarkBg), null, new Rect(margin + 50 + cellW, y, cellW - 60, cellH - 12));
                    int x2 = margin + 50 + cellW + 12;
                    foreach (BitmapSource f in darkFrames)
                    {
                        if (f.PixelWidth == s)
                        {
                            DrawIcon(dc, f, x2, y + (cellH - 12 - s) / 2.0);
                        }
                    }
                    BitmapSource darkZoom = Render.UpscaleNearest(FindFrame(darkFrames, s), 2);
                    DrawIcon(dc, darkZoom, x2 + s + 12, y + (cellH - 12 - darkZoom.PixelHeight) / 2.0);
                }
            });
            Render.SavePng(sheet, path);
        }

        private static List<BitmapSource> ReadFrames(string path)
        {
            var decoder = new IconBitmapDecoder(new Uri(path, UriKind.Absolute),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frames = new List<BitmapSource>();
            foreach (BitmapFrame f in decoder.Frames)
            {
                frames.Add(f);
            }
            return frames;
        }

        private static BitmapSource FindFrame(List<BitmapSource> frames, int size)
        {
            foreach (BitmapSource f in frames)
            {
                if (f.PixelWidth == size)
                {
                    return f;
                }
            }
            return frames[0];
        }

        private static void WriteAppPreview(string previewDir, string appIco)
        {
            var frames = ReadFrames(appIco);

            // 256 px master PNG for docs.
            Render.SavePng(FindFrame(frames, 256), Path.Combine(previewDir, "Watt-256.png"));

            const int margin = 24;
            int w = 900;
            int h = 760;
            BitmapSource sheet = Render.DrawPixels(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(PageBg), null, new Rect(0, 0, w, h));
                Render.DrawLabel(dc, "Watt.ico (app / executable icon), decoded from the generated file", margin, 12, 15, Ink, true);

                // 256 hero on light + dark
                BitmapSource hero = FindFrame(frames, 256);
                dc.DrawRectangle(new SolidColorBrush(Colors.White), null, new Rect(margin, 46, 300, 300));
                DrawIcon(dc, hero, margin + 22, 46 + 22);
                dc.DrawRectangle(new SolidColorBrush(DarkBg), null, new Rect(margin + 320, 46, 300, 300));
                DrawIcon(dc, hero, margin + 320 + 22, 46 + 22);
                Render.DrawLabel(dc, "256 px on light / dark", margin, 352, 10, Render.Hex("FF6A6A70"));

                // actual size row
                int y = 400;
                Render.DrawLabel(dc, "Actual pixel sizes on light and dark (Explorer / Start / taskbar sizes)", margin, y, 12, Ink, true);
                y += 22;
                int[] sizes = { 64, 48, 40, 32, 24, 20, 16 };
                dc.DrawRectangle(new SolidColorBrush(LightBg), null, new Rect(margin, y, w - margin * 2, 76));
                int x = margin + 16;
                foreach (int s in sizes)
                {
                    BitmapSource f = FindFrame(frames, s);
                    DrawIcon(dc, f, x + (80 - s) / 2.0, y + (76 - s) / 2.0);
                    Render.DrawLabel(dc, s.ToString(), x, y + 76 + 4, 9, Render.Hex("FF6A6A70"));
                    x += 108;
                }
                y += 100;
                dc.DrawRectangle(new SolidColorBrush(DarkBg), null, new Rect(margin, y, w - margin * 2, 76));
                x = margin + 16;
                foreach (int s in sizes)
                {
                    BitmapSource f = FindFrame(frames, s);
                    DrawIcon(dc, f, x + (80 - s) / 2.0, y + (76 - s) / 2.0);
                    Render.DrawLabel(dc, s.ToString(), x, y + 76 + 4, 9, Render.Hex("FFB0B0B8"));
                    x += 108;
                }

                // 16 px zoom
                y += 104;
                BitmapSource zoom16 = Render.UpscaleNearest(FindFrame(frames, 16), 8);
                dc.DrawRectangle(new SolidColorBrush(LightBg), null, new Rect(margin, y, zoom16.PixelWidth, zoom16.PixelHeight));
                DrawIcon(dc, zoom16, margin, y);
                dc.DrawRectangle(new SolidColorBrush(DarkBg), null, new Rect(margin + zoom16.PixelWidth + 16, y, zoom16.PixelWidth, zoom16.PixelHeight));
                DrawIcon(dc, zoom16, margin + zoom16.PixelWidth + 16, y);
                Render.DrawLabel(dc, "16 px at 8x (light / dark)", margin, y + zoom16.PixelHeight + 8, 10, Render.Hex("FF6A6A70"));
            });
            Render.SavePng(sheet, Path.Combine(previewDir, "Watt-ico-preview.png"));
        }
    }
}
