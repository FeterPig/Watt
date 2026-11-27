using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Watt.Battery;
using Watt.Models;

namespace Watt.UI
{
    /// <summary>
    /// Borderless, per-pixel transparent window that hosts the panel and parks itself in the
    /// bottom-right corner of the working area (the Windows 11 tray corner). No chrome, no
    /// taskbar entry, no Alt+Tab entry: it behaves like a shell flyout.
    /// </summary>
    public sealed class FloatingWindow : Window
    {
        private const double ScreenInset = 10;
        private const double ShadowMargin = 22;
        private const double PanelCardWidth = 288;
        private const double PanelCardWidthWithShadow = PanelCardWidth + ShadowMargin * 2;

        private readonly PanelView _panel;
        private readonly TranslateTransform _slide = new TranslateTransform(0, 8);
        private bool _hiding;
        private int _animationGeneration;
        private DateTime _lastAutoHide = DateTime.MinValue;

        public FloatingWindow(PanelView panel)
        {
            if (panel == null) throw new ArgumentNullException("panel");
            _panel = panel;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowActivated = true;
            Title = "Watt";
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            _panel.Width = PanelCardWidthWithShadow;
            _panel.RenderTransform = _slide;
            Content = _panel;

            Deactivated += OnDeactivated;
            PreviewKeyDown += OnPreviewKeyDown;
            SourceInitialized += OnSourceInitialized;
        }

        public PanelView Panel { get { return _panel; } }

        public bool IsPanelVisible
        {
            get { return IsVisible && !_hiding; }
        }

        /// <summary>Toggle from the tray. A tray click first deactivates the window (click outside),
        /// so a deactivate-triggered hide must not be immediately undone by the same click.</summary>
        public void ToggleFromTray()
        {
            if (IsPanelVisible)
            {
                HidePanel();
                return;
            }

            if ((DateTime.UtcNow - _lastAutoHide).TotalMilliseconds < 350)
            {
                // This click is the one that closed the panel; keep it closed.
                return;
            }

            ShowPanel();
        }

        public void ShowPanel()
        {
            if (IsPanelVisible)
            {
                Activate();
                return;
            }

            // Bump the generation so a previously started hide animation cannot complete
            // and hide the panel we are showing right now.
            var generation = ++_animationGeneration;
            _hiding = false;
            ApplyLayoutLimits();
            BeginAnimation(OpacityProperty, null);
            _slide.BeginAnimation(TranslateTransform.YProperty, null);
            Opacity = 0;
            _slide.Y = 8;
            Show();
            UpdateLayout();
            PositionBottomRight();

            if (generation != _animationGeneration) return;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
            _slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });

            Activate();
        }

        public void HidePanel()
        {
            if (!IsVisible || _hiding) return;
            var generation = ++_animationGeneration;
            _hiding = true;

            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)) { EasingFunction = ease };
            fade.Completed += delegate
            {
                if (generation != _animationGeneration) return;
                Hide();
                _hiding = false;
                Opacity = 1;
                _slide.BeginAnimation(TranslateTransform.YProperty, null);
                _slide.Y = 8;
            };
            BeginAnimation(OpacityProperty, fade);
            _slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, 6, TimeSpan.FromMilliseconds(100)) { EasingFunction = ease });
        }

        /// <summary>Called on every battery refresh. Repositions if the panel grew or shrank.</summary>
        public void UpdateDevices(System.Collections.Generic.IReadOnlyList<PeripheralDevice> devices, bool scanning)
        {
            ApplyLayoutLimits();
            _panel.SetDevices(devices, scanning);
            if (!IsVisible) return;
            UpdateLayout();
            PositionBottomRight();
        }

        public void ApplyTheme(Theme theme)
        {
            ApplyLayoutLimits();
            _panel.ApplyTheme(theme);
            if (IsVisible)
            {
                UpdateLayout();
                PositionBottomRight();
            }
        }

        public void Dismiss()
        {
            if (IsVisible) HidePanel();
        }

        private void OnDeactivated(object sender, EventArgs e)
        {
            if (!IsVisible || _hiding) return;
            _lastAutoHide = DateTime.UtcNow;
            HidePanel();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HidePanel();
                e.Handled = true;
            }
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            var exStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
            exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            exStyle &= ~NativeMethods.WS_EX_APPWINDOW;
            NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, exStyle);
        }

        private void PositionBottomRight()
        {
            var work = SystemParameters.WorkArea;
            var width = ActualWidth > 0 ? ActualWidth : PanelCardWidthWithShadow;
            var height = ActualHeight > 0 ? ActualHeight : 0;

            // Keep the card ScreenInset above / right of the work-area corner; the transparent
            // shadow margin may hang slightly over the taskbar, which is fine for a flyout.
            Left = work.Right - ScreenInset - ShadowMargin - PanelCardWidth;
            Top = work.Bottom - ScreenInset + ShadowMargin - height;
        }

        /// <summary>Leaves room for the header, card padding and screen inset when the device
        /// list is long; the list itself scrolls instead of pushing the panel off screen.</summary>
        private void ApplyLayoutLimits()
        {
            var work = SystemParameters.WorkArea;
            // A headless session can report a zero-sized work area; leave the list unbounded
            // there so --snapshot still renders every row.
            _panel.SetMaxContentHeight(work.Height > 300 ? work.Height - 160 : double.PositiveInfinity);
        }

        private static class NativeMethods
        {
            public const int GWL_EXSTYLE = -20;
            public const int WS_EX_TOOLWINDOW = 0x00000080;
            public const int WS_EX_APPWINDOW = 0x00040000;

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        }
    }
}
