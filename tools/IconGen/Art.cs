using System;
using System.Windows;
using System.Windows.Media;

namespace IconGen
{
    internal enum MarkVariant
    {
        Ticks2,       // battery outline + terminal nub + two level ticks (first attempt, integer-centred strokes)
        Ticks2Crisp,  // same idea, geometry aligned to the 16 px pixel grid at 1.5 px stroke
        Ticks2Thin,   // crisp body with 1 px level ticks
        Ticks3Crisp,  // crisp body with three 1 px ticks
        LevelFill,    // crisp body with a solid level block (native Windows battery style)
        BoldCrisp,    // 2 px crisp outline, slightly larger battery
        Plain,        // crisp outline + nub, no accent
        Glyph850,     // Segoe Fluent Icons "Battery0" (E850) outline, scaled into the 16 px grid
    }

    /// <summary>
    /// The tray/app mark: a 16 x 16 unit design grid, pure black or pure white ink on transparency.
    /// "Crisp" variants put stroke edges on integer pixel boundaries at 16 px (a 1.5 px stroke centred
    /// on x.25 gives one fully black pixel + one half-covered pixel, the sharpest possible 1.5 px line).
    /// </summary>
    internal static class Art
    {
        public const double Grid = 16.0;

        // Picked after visual review of previews/art-candidates.png and the final tray previews.
        public static readonly MarkVariant TrayVariant = MarkVariant.Ticks2Crisp;
        public static readonly MarkVariant AppVariant = MarkVariant.Ticks2Crisp;

        // App tile geometry, in a 256 x 256 design grid.
        public const double TileInset = 16.0;
        public const double TileRadius = 52.0;
        public const double TileSize = 256.0 - 2.0 * TileInset;
        // How many 256-grid pixels one 16-grid unit becomes inside the app tile.
        public const double AppMarkScale = 10.6;

        public static readonly Color TileColor = Render.Hex("FF0F6CBD");     // Windows 11 accent blue tile
        public static readonly Color AppInk = Render.Hex("FFFFFFFF");        // white line art on the tile
        public static readonly Color TrayLightInk = Render.Hex("FF000000");  // pure black on light taskbars
        public static readonly Color TrayDarkInk = Render.Hex("FFFFFFFF");   // pure white on dark taskbars

        public static readonly MarkVariant[] All =
        {
            MarkVariant.Ticks2,
            MarkVariant.Ticks2Crisp,
            MarkVariant.Ticks2Thin,
            MarkVariant.Ticks3Crisp,
            MarkVariant.LevelFill,
            MarkVariant.BoldCrisp,
            MarkVariant.Plain,
            MarkVariant.Glyph850,
        };

        public static string Name(MarkVariant v)
        {
            switch (v)
            {
                case MarkVariant.Ticks2: return "Ticks2";
                case MarkVariant.Ticks2Crisp: return "Ticks2Crisp";
                case MarkVariant.Ticks2Thin: return "Ticks2Thin";
                case MarkVariant.Ticks3Crisp: return "Ticks3Crisp";
                case MarkVariant.LevelFill: return "LevelFill";
                case MarkVariant.BoldCrisp: return "BoldCrisp";
                case MarkVariant.Plain: return "Plain";
                case MarkVariant.Glyph850: return "GlyphE850";
                default: return v.ToString();
            }
        }

        public static Color TrayInk(bool dark)
        {
            return dark ? TrayDarkInk : TrayLightInk;
        }

        /// <summary>Draws the tray mark in the 16 x 16 design grid.</summary>
        public static void DrawTrayMark(DrawingContext dc, MarkVariant variant, Color ink)
        {
            var brush = new SolidColorBrush(ink);
            brush.Freeze();

            if (variant == MarkVariant.Glyph850)
            {
                Geometry geo = Fonts.Fit(Fonts.GlyphGeometry("E850", 32.0), new Rect(0.9, 2.9, 14.2, 10.2));
                dc.DrawGeometry(brush, null, geo);
                return;
            }

            switch (variant)
            {
                case MarkVariant.Ticks2:
                    DrawBattery(dc, brush, 1.5,
                        new Rect(1.95, 4.0, 10.8, 8.0), 1.8,
                        new Rect(13.05, 6.75, 1.7, 2.5), 0.55,
                        new[] { 5.8, 8.9 }, 1.5, 1.5);
                    return;

                case MarkVariant.Ticks2Crisp:
                    DrawBattery(dc, brush, 1.5,
                        new Rect(2.25, 4.25, 10.0, 7.5), 1.75,
                        new Rect(13.0, 6.75, 1.5, 2.5), 0.5,
                        new[] { 5.5, 8.5 }, 1.5, 1.25);
                    return;

                case MarkVariant.Ticks2Thin:
                    DrawBattery(dc, brush, 1.5,
                        new Rect(2.25, 4.25, 10.0, 7.5), 1.75,
                        new Rect(13.0, 6.75, 1.5, 2.5), 0.5,
                        new[] { 5.5, 8.5 }, 1.0, 1.5);
                    return;

                case MarkVariant.Ticks3Crisp:
                    DrawBattery(dc, brush, 1.5,
                        new Rect(2.25, 4.25, 10.0, 7.5), 1.75,
                        new Rect(13.0, 6.75, 1.5, 2.5), 0.5,
                        new[] { 4.5, 7.5, 10.5 }, 1.0, 1.5);
                    return;

                case MarkVariant.LevelFill:
                {
                    // Outline plus a solid level block, like the native Windows 11 battery icon.
                    var pen = CrispPen(brush, 1.5);
                    var body = new Rect(2.25, 4.25, 10.0, 7.5);
                    dc.DrawRoundedRectangle(null, pen, body, 1.75, 1.75);
                    dc.DrawRoundedRectangle(brush, null, new Rect(13.0, 6.75, 1.5, 2.5), 0.5, 0.5);
                    dc.DrawRoundedRectangle(brush, null, new Rect(4.0, 6.0, 3.0, 4.0), 0.4, 0.4);
                    return;
                }

                case MarkVariant.BoldCrisp:
                    DrawBattery(dc, brush, 2.0,
                        new Rect(2.0, 4.0, 10.0, 8.0), 2.0,
                        new Rect(13.0, 7.0, 2.0, 2.0), 0.6,
                        new[] { 5.5, 8.5 }, 1.0, 1.5);
                    return;

                case MarkVariant.Plain:
                    DrawBattery(dc, brush, 1.5,
                        new Rect(2.25, 4.25, 10.0, 7.5), 1.75,
                        new Rect(13.0, 6.75, 1.5, 2.5), 0.5,
                        null, 0, 0);
                    return;
            }
        }

        private static Pen CrispPen(Brush brush, double width)
        {
            return new Pen(brush, width)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
        }

        private static void DrawBattery(DrawingContext dc, Brush brush, double strokeWidth,
            Rect body, double bodyRadius, Rect nub, double nubRadius,
            double[] tickX, double tickStroke, double tickHalfShaft)
        {
            var pen = CrispPen(brush, strokeWidth);
            dc.DrawRoundedRectangle(null, pen, body, bodyRadius, bodyRadius);

            var nubBrush = brush;
            dc.DrawRoundedRectangle(nubBrush, null, nub, nubRadius, nubRadius);

            if (tickX == null || tickX.Length == 0)
            {
                return;
            }

            var tickPen = CrispPen(brush, tickStroke);
            foreach (double x in tickX)
            {
                dc.DrawLine(tickPen, new Point(x, 8.0 - tickHalfShaft), new Point(x, 8.0 + tickHalfShaft));
            }
        }

        /// <summary>Draws the 256 x 256 app tile icon (solid rounded-square tile + white mark).</summary>
        public static void DrawAppTile(DrawingContext dc, Color tile, Color ink, MarkVariant variant, double markScale)
        {
            var tileBrush = new SolidColorBrush(tile);
            tileBrush.Freeze();
            dc.DrawRoundedRectangle(tileBrush, null,
                new Rect(TileInset, TileInset, TileSize, TileSize), TileRadius, TileRadius);

            var tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform(markScale, markScale));
            tg.Children.Add(new TranslateTransform(128.0 - 8.0 * markScale, 128.0 - 8.0 * markScale));
            dc.PushTransform(tg);
            DrawTrayMark(dc, variant, ink);
            dc.Pop();
        }
    }
}
