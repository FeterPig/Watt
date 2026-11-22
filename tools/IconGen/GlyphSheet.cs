using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconGen
{
    internal sealed class GlyphCandidate
    {
        public string Code;
        public string Name;
        public string Category;
        public bool Zoom;

        public GlyphCandidate(string code, string name, string category, bool zoom = false)
        {
            Code = code;
            Name = name;
            Category = category;
            Zoom = zoom;
        }
    }

    /// <summary>
    /// Renders contact sheets of candidate Segoe Fluent Icons code points: a large 48 px sheet to
    /// identify the artwork, and a magnified 20 px sheet (the panel's real font size) on the exact
    /// light/dark panel colours to check crispness.
    /// </summary>
    internal static class GlyphSheet
    {
        private static readonly Color PanelLight = Render.Hex("FFFFFFFF");
        private static readonly Color PanelDark = Render.Hex("FF1D1D20");
        private static readonly Color PageBg = Render.Hex("FFE9E9EC");

        public static readonly List<GlyphCandidate> Candidates = new List<GlyphCandidate>
        {
            // Keyboards
            new GlyphCandidate("E92E", "KeyboardStandard", "Keyboard", true),
            new GlyphCandidate("E765", "KeyboardClassic", "Keyboard", true),
            new GlyphCandidate("E763", "KeyboardLeftHanded", "Keyboard"),
            new GlyphCandidate("E764", "KeyboardRightHanded", "Keyboard"),
            new GlyphCandidate("E766", "KeyboardSplit", "Keyboard"),
            new GlyphCandidate("E961", "Input", "Keyboard"),
            // Mice
            new GlyphCandidate("E962", "Mouse", "Mouse", true),
            new GlyphCandidate("E7C4", "? (probe)", "Mouse"),
            // Headphones / headsets
            new GlyphCandidate("E7F6", "Headphone", "Headphone", true),
            new GlyphCandidate("E95B", "Headset", "Headphone", true),
            new GlyphCandidate("E95C", "? (probe)", "Headphone"),
            new GlyphCandidate("ED30", "Headphone0", "Headphone", true),
            new GlyphCandidate("ED31", "Headphone1", "Headphone"),
            new GlyphCandidate("ED32", "Headphone2", "Headphone"),
            new GlyphCandidate("ED33", "Headphone3", "Headphone"),
            new GlyphCandidate("F27C", "EarbudSingle", "Headphone"),
            // Generic peripheral / other
            new GlyphCandidate("E772", "Devices", "Other", true),
            new GlyphCandidate("E7F4", "? (probe)", "Other", true),
            new GlyphCandidate("E975", "? (probe)", "Other", true),
            new GlyphCandidate("E70C", "RememberedDevice", "Other"),
            new GlyphCandidate("EB3B", "GenericApp", "Other"),
            // Charging bolt
            new GlyphCandidate("E945", "LightningBolt", "Bolt", true),
            new GlyphCandidate("E83D", "? (probe)", "Bolt", true),
            new GlyphCandidate("EA93", "BatteryCharging10", "Bolt"),
            new GlyphCandidate("EBAB", "MobBatteryCharging0", "Bolt"),
            new GlyphCandidate("EB1A", "BatteryUnknownCharging", "Bolt"),
            // Bluetooth
            new GlyphCandidate("E702", "Bluetooth", "Bluetooth", true),
            new GlyphCandidate("E703", "Connect", "Bluetooth", true),
            new GlyphCandidate("E704", "InternetSharing", "Bluetooth"),
            // Battery
            new GlyphCandidate("E850", "Battery0", "Battery", true),
            new GlyphCandidate("E851", "Battery1", "Battery"),
            new GlyphCandidate("E855", "Battery5", "Battery"),
            new GlyphCandidate("E859", "Battery9", "Battery"),
            new GlyphCandidate("E83F", "Battery10", "Battery", true),
            new GlyphCandidate("E85A", "BatteryCharging0", "Battery", true),
            new GlyphCandidate("E996", "BatteryUnknown", "Battery", true),
            new GlyphCandidate("EA94", "BatterySaver9", "Battery"),
            new GlyphCandidate("EA95", "BatterySaver10", "Battery"),
            new GlyphCandidate("F432", "BatterySaver", "Battery"),
            new GlyphCandidate("F5F2", "VerticalBattery0", "Battery"),
            new GlyphCandidate("F5FD", "VerticalBatteryCharging0", "Battery"),
            new GlyphCandidate("E7E8", "? (probe, task-listed)", "Battery", true),
        };

        public static void Write(string previewDir)
        {
            Directory.CreateDirectory(previewDir);
            WriteLargeSheet(Path.Combine(previewDir, "glyphs-large.png"));
            WriteZoomSheet(Path.Combine(previewDir, "glyphs-zoom-20px.png"));
            WriteChosenSheet(Path.Combine(previewDir, "glyphs-chosen-20px.png"));
        }

        // Final constants - keep in sync with watt/src/Watt/UI/Glyphs.cs after review.
        private static readonly string[][] Chosen =
        {
            new[] { "Keyboard", "E92E" },
            new[] { "Mouse", "E962" },
            new[] { "Headphone", "E7F6" },
            new[] { "Other", "E772" },
            new[] { "Bolt", "E945" },
            new[] { "Bluetooth", "E702" },
            new[] { "Battery", "E83F" },
        };

        private static void WriteLargeSheet(string path)
        {
            const int cols = 7;
            const int cellW = 150;
            const int cellH = 94;
            const int margin = 24;
            int rows = (Candidates.Count + cols - 1) / cols;
            int w = margin * 2 + cols * cellW;
            int h = margin * 2 + 52 + rows * cellH;

            BitmapSource sheet = Render.DrawPixels(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(Colors.White), null, new Rect(0, 0, w, h));
                Render.DrawLabel(dc, "Segoe Fluent Icons candidates at 48 px (black on white)", margin, 12, 15,
                    Render.Hex("FF1A1A1A"), true);
                Render.DrawLabel(dc, "Rendered with WPF FormattedText, family \"Segoe Fluent Icons\" from C:\\Windows\\Fonts\\SegoeIcons.ttf",
                    margin, 32, 10, Render.Hex("FF6A6A70"));

                for (int i = 0; i < Candidates.Count; i++)
                {
                    GlyphCandidate c = Candidates[i];
                    int x = margin + (i % cols) * cellW;
                    int y = margin + 52 + (i / cols) * cellH;

                    FormattedText ft = Fonts.Glyph(c.Code, 48, Render.Hex("FF000000"));
                    ft.TextAlignment = TextAlignment.Center;
                    ft.MaxTextWidth = cellW;
                    dc.DrawText(ft, new Point(x, y));

                    Render.DrawLabel(dc, c.Code + "  " + c.Name, x, y + 66, 10,
                        Render.Hex("FF2A2A30"), true, cellW, TextAlignment.Center);
                    Render.DrawLabel(dc, c.Category + (Fonts.HasGlyph(c.Code) ? "" : "  MISSING?"), x, y + 79, 9,
                        Render.Hex("FF8A8A90"), false, cellW, TextAlignment.Center);
                }
            });
            Render.SavePng(sheet, path);
        }

        /// <summary>Renders one 20 px glyph into a small transparent bitmap (exactly like the panel).</summary>
        private static BitmapSource GlyphBitmap(string code, Color color)
        {
            const int boxW = 30;
            const int boxH = 34;
            return Render.DrawPixels(boxW, boxH, dc =>
            {
                FormattedText ft = Fonts.Glyph(code, 20.0, color);
                ft.TextAlignment = TextAlignment.Center;
                ft.MaxTextWidth = boxW;
                dc.DrawText(ft, new Point(0, (boxH - ft.Height) / 2.0));
            });
        }

        private static void WriteZoomSheet(string path)
        {
            var list = new List<GlyphCandidate>();
            foreach (GlyphCandidate c in Candidates)
            {
                if (c.Zoom && Fonts.HasGlyph(c.Code))
                {
                    list.Add(c);
                }
            }
            if (list.Count == 0)
            {
                return;
            }

            const int cols = 5;
            const int cellW = 248;
            const int cellH = 178;
            const int margin = 24;
            const int zoom = 4;
            int rows = (list.Count + cols - 1) / cols;
            int w = margin * 2 + cols * cellW;
            int h = margin * 2 + 52 + rows * cellH;

            BitmapSource sheet = Render.DrawPixels(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(PageBg), null, new Rect(0, 0, w, h));
                Render.DrawLabel(dc, "Panel glyphs at the real 20 px, 4x pixel zoom, on the panel light (#FFFFFF) and dark (#1D1D20) surfaces",
                    margin, 12, 15, Render.Hex("FF1A1A1A"), true);
                Render.DrawLabel(dc, "These are exact-size WPF renders (Ideal formatting, grayscale AA) matching PanelView.xaml's TextBlock FontSize=20.",
                    margin, 32, 10, Render.Hex("FF6A6A70"));

                for (int i = 0; i < list.Count; i++)
                {
                    GlyphCandidate c = list[i];
                    int x = margin + (i % cols) * cellW;
                    int y = margin + 52 + (i / cols) * cellH;

                    BitmapSource lightBmp = GlyphBitmap(c.Code, Render.Hex("FF1B1C1F"));
                    BitmapSource darkBmp = GlyphBitmap(c.Code, Render.Hex("FFF2F2F4"));

                    int zw = lightBmp.PixelWidth * zoom;
                    int zh = lightBmp.PixelHeight * zoom;

                    dc.DrawRoundedRectangle(new SolidColorBrush(Colors.White), null, new Rect(x + 4, y + 2, cellW - 8, cellH - 10), 8, 8);
                    // light zoom
                    dc.DrawRectangle(new SolidColorBrush(PanelLight), null, new Rect(x + 14, y + 28, zw, zh));
                    dc.DrawImage(Render.UpscaleNearest(lightBmp, zoom), new Rect(x + 14, y + 28, zw, zh));
                    // dark zoom
                    dc.DrawRectangle(new SolidColorBrush(PanelDark), null, new Rect(x + 22 + zw, y + 28, zw, zh));
                    dc.DrawImage(Render.UpscaleNearest(darkBmp, zoom), new Rect(x + 22 + zw, y + 28, zw, zh));

                    Render.DrawLabel(dc, c.Code + "  " + c.Name, x + 14, y + 28 + zh + 8, 10,
                        Render.Hex("FF2A2A30"), true);
                    Render.DrawLabel(dc, c.Category, x + 14, y + 28 + zh + 22, 9, Render.Hex("FF8A8A90"));
                }
            });
            Render.SavePng(sheet, path);
        }

        private static void WriteChosenSheet(string path)
        {
            const int cols = 7;
            const int cellW = 192;
            const int cellH = 268;
            const int margin = 24;
            const int zoom = 3;
            int w = margin * 2 + cols * cellW;
            int h = margin * 2 + 62 + cellH;

            BitmapSource sheet = Render.DrawPixels(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(PageBg), null, new Rect(0, 0, w, h));
                Render.DrawLabel(dc, "Chosen panel glyphs (watt/src/Watt/UI/Glyphs.cs) - exact 20 px render, " + zoom + "x zoom", margin, 12, 15,
                    Render.Hex("FF1A1A1A"), true);
                Render.DrawLabel(dc, "Light card is the panel's #FFFFFF surface with the #1B1C1F glyph brush; dark card is #1D1D20 with #F2F2F4.",
                    margin, 32, 10, Render.Hex("FF6A6A70"));

                for (int i = 0; i < Chosen.Length; i++)
                {
                    int x = margin + i * cellW;
                    int y = margin + 62;
                    string name = Chosen[i][0];
                    string code = Chosen[i][1];
                    BitmapSource lightBmp = GlyphBitmap(code, Render.Hex("FF1B1C1F"));
                    BitmapSource darkBmp = GlyphBitmap(code, Render.Hex("FFF2F2F4"));
                    int zw = lightBmp.PixelWidth * zoom;
                    int zh = lightBmp.PixelHeight * zoom;

                    dc.DrawRoundedRectangle(new SolidColorBrush(Colors.White), null,
                        new Rect(x + 4, y, cellW - 8, cellH - 8), 8, 8);
                    Render.DrawLabel(dc, name, x + 12, y + 10, 11, Render.Hex("FF2A2A30"), true);

                    double zx = x + (cellW - zw) / 2.0;

                    dc.DrawRectangle(new SolidColorBrush(PanelLight), null, new Rect(x + 10, y + 30, cellW - 20, 108));
                    dc.DrawImage(Render.UpscaleNearest(lightBmp, zoom), new Rect(zx, y + 33, zw, zh));

                    dc.DrawRectangle(new SolidColorBrush(PanelDark), null, new Rect(x + 10, y + 142, cellW - 20, 108));
                    dc.DrawImage(Render.UpscaleNearest(darkBmp, zoom), new Rect(zx, y + 145, zw, zh));

                    Render.DrawLabel(dc, "U+" + code + "  HasGlyph: " + (Fonts.HasGlyph(code) ? "yes" : "NO"),
                        x + 12, y + cellH - 24, 9,
                        Fonts.HasGlyph(code) ? Render.Hex("FF2E7D32") : Render.Hex("FFB00020"));
                }
            });
            Render.SavePng(sheet, path);
        }
    }
}
