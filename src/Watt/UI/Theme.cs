using System.Windows.Media;

namespace Watt.UI
{
    /// <summary>
    /// Two colour systems (light / dark) used by the panel and the tray icon.
    /// Everything is deliberately flat: one surface, one border, two text colours.
    /// </summary>
    public sealed class Theme
    {
        public static readonly Theme Light = new Theme(
            isDark: false,
            surface: "#FFFFFFFF",
            border: "#FFE7E7EA",
            textPrimary: "#FF17181A",
            textSecondary: "#FF7E7E86",
            glyph: "#FF1B1C1F",
            low: "#FFD9383C",
            skeleton: "#FFEFEFF2");

        public static readonly Theme Dark = new Theme(
            isDark: true,
            surface: "#FF1D1D20",
            border: "#FF303036",
            textPrimary: "#FFF4F4F6",
            textSecondary: "#FF9B9BA3",
            glyph: "#FFF2F2F4",
            low: "#FFFF6B6B",
            skeleton: "#FF2B2B2F");

        private Theme(bool isDark, string surface, string border, string textPrimary,
                      string textSecondary, string glyph, string low, string skeleton)
        {
            IsDark = isDark;
            Surface = Frozen(surface);
            Border = Frozen(border);
            TextPrimary = Frozen(textPrimary);
            TextSecondary = Frozen(textSecondary);
            Glyph = Frozen(glyph);
            Low = Frozen(low);
            Skeleton = Frozen(skeleton);
        }

        public bool IsDark { get; private set; }
        public Brush Surface { get; private set; }
        public Brush Border { get; private set; }
        public Brush TextPrimary { get; private set; }
        public Brush TextSecondary { get; private set; }
        public Brush Glyph { get; private set; }
        public Brush Low { get; private set; }
        public Brush Skeleton { get; private set; }

        private static Brush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}
