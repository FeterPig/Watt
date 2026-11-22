using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconGen
{
    /// <summary>
    /// Renders a comparison sheet of tray mark variants at real 16 px plus a 6x pixel zoom, on light
    /// and dark taskbar colours, so the 16 px quality can be judged visually.
    /// </summary>
    internal static class CandidateSheet
    {
        private static readonly Color LightBg = Render.Hex("FFF3F3F3");
        private static readonly Color DarkBg = Render.Hex("FF1C1C1E");
        private static readonly Color PageBg = Render.Hex("FFE9E9EC");
        private static readonly Color Ink = Render.Hex("FF1A1A1A");

        public static void Write(string previewDir)
        {
            Directory.CreateDirectory(previewDir);
            WriteTrayVariants(Path.Combine(previewDir, "art-candidates.png"));
            WriteAppTiles(Path.Combine(previewDir, "art-app-tiles.png"));
        }

        private static BitmapSource RenderMark(int size, Color ink, MarkVariant variant, int ss)
        {
            BitmapSource big = Render.Draw(size, size, ss, Art.Grid, Art.Grid,
                dc => Art.DrawTrayMark(dc, variant, ink));
            return Render.Downsample(big, ss);
        }

        private static void WriteTrayVariants(string path)
        {
            const int cellW = 214;
            const int cellH = 262;
            const int margin = 24;
            const int zoom = 6;
            var variants = Art.All;
            int w = margin * 2 + variants.Length * cellW;
            int h = margin * 2 + 56 + cellH;

            BitmapSource sheet = Render.DrawPixels(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(PageBg), null, new Rect(0, 0, w, h));
                Render.DrawLabel(dc, "Tray mark candidates - real 16 px on light/dark taskbars plus " + zoom + "x pixel zoom", margin, 12, 15, Ink, true);
                Render.DrawLabel(dc, "Top two rows: exact 16 px frames. Bottom: 6x nearest-neighbour zoom (no filtering) on #FFFFFF / #1C1C1E.",
                    margin, 32, 10, Render.Hex("FF6A6A70"));

                for (int i = 0; i < variants.Length; i++)
                {
                    MarkVariant v = variants[i];
                    int x = margin + i * cellW;
                    int y = margin + 56;

                    dc.DrawRoundedRectangle(new SolidColorBrush(Colors.White), null,
                        new Rect(x + 4, y + 2, cellW - 8, cellH - 10), 8, 8);
                    Render.DrawLabel(dc, Art.Name(v), x + 12, y + 8, 11, Ink, true);

                    BitmapSource light16 = RenderMark(16, Colors.Black, v, 8);
                    BitmapSource dark16 = RenderMark(16, Colors.White, v, 8);

                    // exact 16 px rows
                    dc.DrawRectangle(new SolidColorBrush(LightBg), null, new Rect(x + 12, y + 28, cellW - 24, 22));
                    dc.DrawImage(light16, new Rect(x + 12 + (cellW - 24 - 16) / 2.0, y + 28 + 3, 16, 16));
                    dc.DrawRectangle(new SolidColorBrush(DarkBg), null, new Rect(x + 12, y + 54, cellW - 24, 22));
                    dc.DrawImage(dark16, new Rect(x + 12 + (cellW - 24 - 16) / 2.0, y + 54 + 3, 16, 16));

                    // 6x zooms
                    double zy = y + 86;
                    foreach (bool dark in new[] { false, true })
                    {
                        BitmapSource z = Render.UpscaleNearest(dark ? dark16 : light16, zoom);
                        double zx = x + 12 + (dark ? 96 + 4 : 0);
                        dc.DrawRectangle(new SolidColorBrush(dark ? DarkBg : Colors.White), null,
                            new Rect(zx, zy, z.PixelWidth, z.PixelHeight));
                        dc.DrawImage(z, new Rect(zx, zy, z.PixelWidth, z.PixelHeight));
                    }
                    Render.DrawLabel(dc, "light", x + 12, zy + 96 + 6, 9, Render.Hex("FF8A8A90"));
                    Render.DrawLabel(dc, "dark", x + 12 + 100, zy + 96 + 6, 9, Render.Hex("FF8A8A90"));
                }
            });
            Render.SavePng(sheet, path);
        }

        private static void WriteAppTiles(string path)
        {
            var tiles = new[]
            {
                new[] { "FF0F6CBD", "Accent blue tile (white mark)" },
                new[] { "FF121316", "Black tile (white mark)" },
                new[] { "FFFFFFFF", "White tile (black mark)" },
                new[] { "FF2B2B2F", "Charcoal tile (white mark)" },
            };

            const int cellW = 280;
            const int cellH = 360;
            const int margin = 24;
            int w = margin * 2 + tiles.Length * cellW;
            int h = margin * 2 + 56 + cellH;

            BitmapSource sheet = Render.DrawPixels(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(PageBg), null, new Rect(0, 0, w, h));
                Render.DrawLabel(dc, "App icon candidates (Watt.ico) - 128 px on light/dark, then real 32/16 px", margin, 12, 15, Ink, true);

                for (int i = 0; i < tiles.Length; i++)
                {
                    Color tile = Render.Hex(tiles[i][0]);
                    Color ink = tile.R > 200 && tile.G > 200 && tile.B > 200 ? Colors.Black : Colors.White;
                    int x = margin + i * cellW;
                    int y = margin + 56;

                    dc.DrawRoundedRectangle(new SolidColorBrush(Colors.White), null,
                        new Rect(x + 4, y + 2, cellW - 8, cellH - 10), 8, 8);
                    Render.DrawLabel(dc, tiles[i][1], x + 14, y + 10, 10.5, Ink, true);

                    BitmapSource big = Render.Downsample(
                        Render.Draw(128, 128, 4, 256, 256, d => Art.DrawAppTile(d, tile, ink, Art.AppVariant, Art.AppMarkScale)), 4);
                    dc.DrawRectangle(new SolidColorBrush(LightBg), null, new Rect(x + 14, y + 34, 120, 128));
                    dc.DrawImage(big, new Rect(x + 14, y + 34, 128, 128));
                    dc.DrawRectangle(new SolidColorBrush(DarkBg), null, new Rect(x + 146, y + 34, 120, 128));
                    dc.DrawImage(big, new Rect(x + 146, y + 34, 128, 128));

                    foreach (int s in new[] { 32, 16 })
                    {
                        BitmapSource bmp = Render.Downsample(
                            Render.Draw(s, s, 4, 256, 256, d => Art.DrawAppTile(d, tile, ink, Art.AppVariant, Art.AppMarkScale)), 4);
                        double rowY = y + 176 + (s == 32 ? 0 : 74);
                        dc.DrawRectangle(new SolidColorBrush(LightBg), null, new Rect(x + 14, rowY, 120, s + 8));
                        dc.DrawImage(bmp, new Rect(x + 14 + (120 - s) / 2.0, rowY + 4, s, s));
                        dc.DrawRectangle(new SolidColorBrush(DarkBg), null, new Rect(x + 146, rowY, 120, s + 8));
                        dc.DrawImage(bmp, new Rect(x + 146 + (120 - s) / 2.0, rowY + 4, s, s));
                        Render.DrawLabel(dc, s + " px", x + 14, rowY + s + 10, 9, Render.Hex("FF8A8A90"));
                    }
                }
            });
            Render.SavePng(sheet, path);
        }
    }
}
