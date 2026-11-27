using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Watt.Models;

namespace Watt.UI
{
    public partial class PanelView : UserControl
    {
        private readonly ObservableCollection<DeviceRow> _rows = new ObservableCollection<DeviceRow>();
        private readonly DispatcherTimer _feedbackTimer;
        private Theme _theme = Theme.Light;
        private bool _editMode;
        private IReadOnlyList<PeripheralDevice> _lastDevices = new PeripheralDevice[0];
        private bool _lastScanning;

        public PanelView()
        {
            InitializeComponent();
            DeviceList.ItemsSource = _rows;
            var workAreaHeight = SystemParameters.WorkArea.Height;
            SetMaxContentHeight(workAreaHeight > 300 ? workAreaHeight - 160 : double.PositiveInfinity);
            _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
            _feedbackTimer.Tick += delegate
            {
                _feedbackTimer.Stop();
                RefreshFeedback.Visibility = Visibility.Collapsed;
            };
            ApplyTheme(Theme.Light);
        }

        /// <summary>
        /// Bounds the scrollable device list so a machine with many peripherals still shows a
        /// panel that fits on screen (the tray corner must not grow past the work area).
        /// </summary>
        public void SetMaxContentHeight(double maxHeight)
        {
            DeviceScroll.MaxHeight = Math.Max(120, maxHeight);
        }

        /// <summary>Raised when the user clicks the circular refresh button.</summary>
        public event EventHandler RefreshRequested;

        /// <summary>Raised when a device gets a new display name.</summary>
        public event EventHandler<DeviceActionEventArgs> DeviceRenameRequested;

        /// <summary>Raised when the user removes a device from the panel.</summary>
        public event EventHandler<DeviceActionEventArgs> DeviceDeleteRequested;

        /// <summary>Raised when edit mode turns on or off.</summary>
        public event EventHandler EditModeChanged;

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

        public bool IsEditing { get { return _editMode; } }

        public void ApplyTheme(Theme theme)
        {
            if (theme == null) throw new ArgumentNullException("theme");
            _theme = theme;
            Card.Background = theme.Surface;
            Card.BorderBrush = theme.Border;
            EmptyText.Foreground = theme.TextSecondary;
            RefreshFeedback.Foreground = theme.TextSecondary;
            RefreshButton.Foreground = theme.TextSecondary;
            EditButton.Foreground = _editMode ? theme.TextPrimary : theme.TextSecondary;
            Resources["RefreshHoverBrush"] = theme.Skeleton;
            Resources["RefreshPressedBrush"] = theme.IsDark
                ? new SolidColorBrush(Color.FromRgb(48, 48, 54))
                : new SolidColorBrush(Color.FromRgb(242, 242, 245));
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
            var previous = _lastDevices;
            _lastDevices = devices ?? new PeripheralDevice[0];
            _lastScanning = scanning;

            // While editing, keep the text boxes stable. The app refreshes this snapshot when
            // edit mode ends, so a background scan cannot interrupt a rename.
            if (_editMode) return;

            // BatteryService raises Changed at scan start and scan end, and the preference layer
            // returns a fresh list each time. Rebuild only when a displayed value actually
            // changed; otherwise an unchanged background scan would tear down every row (and
            // lose focus/scroll position) twice per minute.
            if (ReferenceEquals(previous, _lastDevices) || MatchesRows(_lastDevices))
            {
                UpdateEmptyState();
                return;
            }

            RenderDevices();
        }

        private bool MatchesRows(IReadOnlyList<PeripheralDevice> devices)
        {
            var visible = 0;
            if (devices != null)
            {
                foreach (var device in devices)
                {
                    if (device != null && device.BatteryPercent.HasValue) visible++;
                }
            }

            if (visible != _rows.Count) return false;

            var index = 0;
            if (devices != null)
            {
                foreach (var device in devices)
                {
                    if (device == null || !device.BatteryPercent.HasValue) continue;
                    var current = _rows[index++].Device;
                    if (!string.Equals(current.Id, device.Id, StringComparison.Ordinal)) return false;
                    if (!string.Equals(current.Name ?? string.Empty, device.Name ?? string.Empty,
                            StringComparison.Ordinal)) return false;
                    if (current.Kind != device.Kind) return false;
                    if (current.BatteryPercent != device.BatteryPercent) return false;
                    if (current.IsCharging != device.IsCharging) return false;
                }
            }

            return true;
        }

        /// <summary>Disables the refresh button and shows transient "refreshing" feedback.</summary>
        public void SetRefreshing(bool refreshing)
        {
            RefreshButton.IsEnabled = !refreshing;
            if (refreshing) ShowFeedback("刷新中…", false);
        }

        /// <summary>Re-enables the button and shows a short result message.</summary>
        public void ShowRefreshResult(bool success)
        {
            RefreshButton.IsEnabled = true;
            ShowFeedback(success ? "已刷新" : "刷新失败", true);
        }

        private void RenderDevices()
        {
            // A background scan refreshes the same rows every 60 seconds. Keep the user's scroll
            // position when the rebuilt list is still tall enough instead of jumping to the top.
            var scrollOffset = DeviceScroll == null ? 0 : DeviceScroll.VerticalOffset;

            _rows.Clear();

            foreach (var device in _lastDevices)
            {
                if (device == null || device.BatteryPercent == null) continue;
                var row = new DeviceRow(device)
                {
                    Glyph = Glyphs.ForKind(device.Kind),
                    Name = device.Name,
                    PercentText = device.BatteryPercent.Value + "%",
                    IsLow = device.BatteryPercent.Value <= 15,
                    GlyphBrush = _theme.Glyph,
                    NameBrush = _theme.TextPrimary,
                    PercentBrush = device.BatteryPercent.Value <= 15 ? _theme.Low : _theme.TextSecondary,
                    ChargingVisibility = device.IsCharging ? Visibility.Visible : Visibility.Collapsed
                };
                _rows.Add(row);
            }

            if (scrollOffset > 0 && DeviceScroll != null)
            {
                DeviceScroll.ScrollToVerticalOffset(scrollOffset);
                if (!Dispatcher.HasShutdownStarted)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                    {
                        DeviceScroll.ScrollToVerticalOffset(scrollOffset);
                    }));
                }
            }

            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            var hasDevices = _rows.Count > 0;
            var showLoading = _lastScanning && !hasDevices;
            var showEmpty = !_lastScanning && !hasDevices;

            DeviceList.Visibility = hasDevices ? Visibility.Visible : Visibility.Collapsed;
            LoadingHost.Visibility = showLoading ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowFeedback(string text, bool autoHide)
        {
            _feedbackTimer.Stop();
            RefreshFeedback.Text = text;
            RefreshFeedback.Visibility = Visibility.Visible;
            if (autoHide) _feedbackTimer.Start();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            var handler = RefreshRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (_editMode)
            {
                EndEditing();
            }
            else
            {
                BeginEditing();
            }
        }

        private void BeginEditing()
        {
            if (_rows.Count == 0) return;
            _editMode = true;
            EditButton.Foreground = _theme.TextPrimary;
            foreach (var row in _rows) row.BeginEdit();
            RaiseEditModeChanged();
        }

        private void EndEditing()
        {
            if (!_editMode) return;
            foreach (var row in _rows)
            {
                CommitRow(row);
                row.EndEdit();
            }
            _editMode = false;
            EditButton.Foreground = _theme.TextSecondary;

            // The app refreshes the snapshot when edit mode ends, but a standalone preview (for
            // example --panel) has no such listener: render the snapshot we buffered while
            // editing so rows that changed in the background are not left stale.
            RenderDevices();
            RaiseEditModeChanged();
        }

        private void RaiseEditModeChanged()
        {
            var handler = EditModeChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void CommitRow(DeviceRow row)
        {
            if (row == null) return;
            var name = (row.EditableName ?? string.Empty).Trim();
            if (name.Length == 0 || string.Equals(name, row.Name, StringComparison.Ordinal))
            {
                row.EditableName = row.Name;
                return;
            }

            row.Name = name;
            row.EditableName = name;
            row.Device.Name = name;
            var handler = DeviceRenameRequested;
            if (handler != null)
            {
                handler(this, new DeviceActionEventArgs(row.Device.Id, name));
            }
        }

        private void NameBox_KeyDown(object sender, KeyEventArgs e)
        {
            var box = sender as TextBox;
            var row = box == null ? null : box.DataContext as DeviceRow;
            if (row == null) return;

            if (e.Key == Key.Enter)
            {
                CommitRow(row);
                e.Handled = true;
                Keyboard.ClearFocus();
            }
            else if (e.Key == Key.Escape)
            {
                row.EditableName = row.Name;
                e.Handled = true;
            }
        }

        private void NameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            var row = box == null ? null : box.DataContext as DeviceRow;
            if (row == null) return;
            CommitRow(row);
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var row = button == null ? null : button.Tag as DeviceRow;
            if (row == null) return;

            if (_editMode) EndEditing();
            var handler = DeviceDeleteRequested;
            if (handler != null) handler(this, new DeviceActionEventArgs(row.Device.Id, row.Name));
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

    public sealed class DeviceActionEventArgs : EventArgs
    {
        public DeviceActionEventArgs(string deviceId, string name)
        {
            DeviceId = deviceId;
            Name = name;
        }

        public string DeviceId { get; private set; }
        public string Name { get; private set; }
    }

    public sealed class DeviceRow : INotifyPropertyChanged
    {
        private string _name;
        private string _editableName;
        private bool _isEditing;

        public DeviceRow(PeripheralDevice device)
        {
            if (device == null) throw new ArgumentNullException("device");
            Device = device;
            _name = device.Name;
            _editableName = device.Name;
            Glyph = Glyphs.ForKind(device.Kind);
            PercentText = device.BatteryPercent.HasValue ? device.BatteryPercent.Value + "%" : "--";
            IsLow = device.BatteryPercent.HasValue && device.BatteryPercent.Value <= 15;
            GlyphBrush = Theme.Light.Glyph;
            NameBrush = Theme.Light.TextPrimary;
            PercentBrush = Theme.Light.TextSecondary;
            ChargingVisibility = device.IsCharging ? Visibility.Visible : Visibility.Collapsed;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public PeripheralDevice Device { get; private set; }
        public string Glyph { get; set; }

        public string Name
        {
            get { return _name; }
            set
            {
                if (string.Equals(_name, value, StringComparison.Ordinal)) return;
                _name = value;
                OnPropertyChanged("Name");
            }
        }

        public string EditableName
        {
            get { return _editableName; }
            set
            {
                if (string.Equals(_editableName, value, StringComparison.Ordinal)) return;
                _editableName = value;
                OnPropertyChanged("EditableName");
            }
        }

        public bool IsEditing
        {
            get { return _isEditing; }
            private set
            {
                if (_isEditing == value) return;
                _isEditing = value;
                OnPropertyChanged("IsEditing");
                OnPropertyChanged("NameVisibility");
                OnPropertyChanged("EditVisibility");
            }
        }

        public Visibility NameVisibility
        {
            get { return _isEditing ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility EditVisibility
        {
            get { return _isEditing ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string PercentText { get; set; }
        public bool IsLow { get; set; }
        public bool IsCharging { get { return ChargingVisibility == Visibility.Visible; } }
        public Brush GlyphBrush { get; set; }
        public Brush NameBrush { get; set; }
        public Brush PercentBrush { get; set; }
        public Visibility ChargingVisibility { get; set; }

        public void BeginEdit()
        {
            EditableName = Name;
            IsEditing = true;
        }

        public void EndEdit()
        {
            IsEditing = false;
        }

        private void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
