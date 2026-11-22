using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Watt.Models;

namespace Watt.UI
{
    public partial class PanelView : UserControl
    {
        private readonly ObservableCollection<DeviceRow> _rows = new ObservableCollection<DeviceRow>();
        private Theme _theme = Theme.Light;

        public PanelView()
        {
            InitializeComponent();
            DeviceList.ItemsSource = _rows;
            ApplyTheme(Theme.Light);
        }

        /// <summary>Outer window width in DIPs (card width + shadow breathing room).</summary>
        public const double PanelWidth = 288;
        public const double ShadowMargin = 22;

        public static readonly DependencyProperty SkeletonBrushProperty =
            DependencyProperty.Register("SkeletonBrush", typeof(Brush), typeof(PanelView),
                new PropertyMetadata(Theme.Light.Skeleton));

        public Brush SkeletonBrush
        {
            get { return (Brush)GetValue(SkeletonBrushProperty); }
            set { SetValue(SkeletonBrushProperty, value); }
        }

        public Theme CurrentTheme { get { return _theme; } }

        public void ApplyTheme(Theme theme)
        {
            if (theme == null) throw new ArgumentNullException("theme");
            _theme = theme;
            Card.Background = theme.Surface;
            Card.BorderBrush = theme.Border;
            EmptyText.Foreground = theme.TextSecondary;
            SkeletonBrush = theme.Skeleton;
            foreach (var row in _rows)
            {
                row.GlyphBrush = theme.Glyph;
                row.NameBrush = theme.TextPrimary;
                row.PercentBrush = row.IsLow ? theme.Low : theme.TextSecondary;
            }
            RebuildRows();
        }

        public void SetDevices(IReadOnlyList<PeripheralDevice> devices, bool scanning)
        {
            _rows.Clear();

            if (devices != null)
            {
                foreach (var device in devices)
                {
                    if (device == null || device.BatteryPercent == null) continue;
                    _rows.Add(new DeviceRow(device)
                    {
                        Glyph = Glyphs.ForKind(device.Kind),
                        Name = device.Name,
                        PercentText = device.BatteryPercent.Value + "%",
                        IsLow = device.BatteryPercent.Value <= 15,
                        GlyphBrush = _theme.Glyph,
                        NameBrush = _theme.TextPrimary,
                        PercentBrush = device.BatteryPercent.Value <= 15 ? _theme.Low : _theme.TextSecondary,
                        ChargingVisibility = device.IsCharging ? Visibility.Visible : Visibility.Collapsed
                    });
                }
            }

            var hasDevices = _rows.Count > 0;
            var showLoading = scanning && !hasDevices;
            var showEmpty = !scanning && !hasDevices;

            DeviceList.Visibility = hasDevices ? Visibility.Visible : Visibility.Collapsed;
            LoadingHost.Visibility = showLoading ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RebuildRows()
        {
            // Rebuild so theme brushes flow into every row without per-row change notification.
            var snapshot = new List<DeviceRow>(_rows);
            _rows.Clear();
            foreach (var row in snapshot)
            {
                row.GlyphBrush = _theme.Glyph;
                row.NameBrush = _theme.TextPrimary;
                row.PercentBrush = row.IsLow ? _theme.Low : _theme.TextSecondary;
                _rows.Add(row);
            }
        }
    }

    public sealed class DeviceRow
    {
        public DeviceRow(PeripheralDevice device)
        {
            if (device == null) throw new ArgumentNullException("device");
            Device = device;
            Name = device.Name;
            Glyph = Glyphs.ForKind(device.Kind);
            PercentText = device.BatteryPercent.HasValue ? device.BatteryPercent.Value + "%" : "--";
            IsLow = device.BatteryPercent.HasValue && device.BatteryPercent.Value <= 15;
            GlyphBrush = Theme.Light.Glyph;
            NameBrush = Theme.Light.TextPrimary;
            PercentBrush = Theme.Light.TextSecondary;
            ChargingVisibility = device.IsCharging ? Visibility.Visible : Visibility.Collapsed;
        }

        public PeripheralDevice Device { get; private set; }
        public string Glyph { get; set; }
        public string Name { get; set; }
        public string PercentText { get; set; }
        public bool IsLow { get; set; }
        public bool IsCharging { get { return ChargingVisibility == Visibility.Visible; } }
        public Brush GlyphBrush { get; set; }
        public Brush NameBrush { get; set; }
        public Brush PercentBrush { get; set; }
        public Visibility ChargingVisibility { get; set; }
    }
}
