using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace IconGen
{
    /// <summary>
    /// Access to "Segoe Fluent Icons" (C:\Windows\Fonts\SegoeIcons.ttf), the same font the
    /// Windows 11 shell uses for its own line-art glyphs.
    /// </summary>
    internal static class Fonts
    {
        public const string FluentFamily = "Segoe Fluent Icons";

        public static readonly Typeface Fluent =
            new Typeface(new FontFamily(FluentFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        public static char Char(string code)
        {
            return (char)Convert.ToInt32(code, 16);
        }

        public static FormattedText Glyph(string code, double size, Color color)
        {
            return new FormattedText(Char(code).ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Fluent, size, new SolidColorBrush(color), 1.0);
        }

        public static Geometry GlyphGeometry(string code, double emSize)
        {
            Geometry geo = Glyph(code, emSize, Colors.Black).BuildGeometry(new Point(0, 0));
            geo.Freeze();
            return geo;
        }

        /// <summary>Uniformly scales and centers a geometry's ink box inside <paramref name="target"/>.</summary>
        public static Geometry Fit(Geometry geo, Rect target)
        {
            Rect b = geo.Bounds;
            if (b.IsEmpty || b.Width <= 0.0001 || b.Height <= 0.0001)
            {
                return geo;
            }
            double s = Math.Min(target.Width / b.Width, target.Height / b.Height);
            var tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform(s, s));
            tg.Children.Add(new TranslateTransform(
                target.X + (target.Width - b.Width * s) / 2.0 - b.X * s,
                target.Y + (target.Height - b.Height * s) / 2.0 - b.Y * s));
            var fitted = PathGeometry.CreateFromGeometry(geo);
            fitted.Transform = tg;
            fitted.Freeze();
            return fitted;
        }

        public static bool FontAvailable
        {
            get
            {
                try
                {
                    GlyphTypeface gtf;
                    return Fluent.TryGetGlyphTypeface(out gtf);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool HasGlyph(string code)
        {
            try
            {
                GlyphTypeface gtf;
                if (!Fluent.TryGetGlyphTypeface(out gtf))
                {
                    return false;
                }
                return gtf.CharacterToGlyphMap.ContainsKey(Char(code));
            }
            catch
            {
                return false;
            }
        }
    }
}
