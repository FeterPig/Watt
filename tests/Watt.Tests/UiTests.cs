using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Watt.Models;
using Watt.UI;

namespace Watt.Tests
{
    internal static class UiTests
    {
        internal static void Register(TestRunner runner)
        {
            runner.Run("ui/rows render values, low battery and charging", RowsRender);
            runner.Run("ui/empty and loading states", EmptyAndLoading);
            runner.Run("ui/a scanning flag on the same snapshot does not rebuild rows", SameSnapshotNoRebuild);
            runner.Run("ui/edit mode renames through an event and keeps rows during a scan", EditRename);
            runner.Run("ui/delete button raises the device id", DeleteRaisesEvent);
            runner.Run("ui/refresh feedback disables the button while working", RefreshFeedback);
            runner.Run("ui/theme brushes reach every row", ThemeBrushes);
            runner.Run("ui/accessibility names are present on icon buttons", AccessibilityNames);
            runner.Run("ui/a long device list scrolls instead of growing past the screen", LongListIsBounded);
            runner.Run("ui/device row handles a null value and null input", DeviceRowBasics);
        }

        private static IReadOnlyList<PeripheralDevice> TwoDevices()
        {
            return new[]
            {
                new PeripheralDevice
                {
                    Id = "k1", Name = "Keyboard", Kind = DeviceKind.Keyboard, BatteryPercent = 86
                },
                new PeripheralDevice
                {
                    Id = "m1", Name = "Mouse", Kind = DeviceKind.Mouse, BatteryPercent = 9,
                    IsCharging = true
                },
                new PeripheralDevice
                {
                    Id = "h1", Name = "No value", Kind = DeviceKind.Headphone, BatteryPercent = null
                }
            };
        }

        private static PanelView CreateView(bool dark = false)
        {
            var view = new PanelView();
            view.ApplyTheme(dark ? Theme.Dark : Theme.Light);
            return view;
        }

        private static ItemsControl List(PanelView view)
        {
            return (ItemsControl)view.FindName("DeviceList");
        }

        private static List<DeviceRow> Rows(PanelView view)
        {
            return List(view).Items.Cast<DeviceRow>().ToList();
        }

        private static void RowsRender()
        {
            var view = CreateView();
            view.SetDevices(TwoDevices(), false);

            var rows = Rows(view);
            Assert.Equal(2, rows.Count, "null-value row is hidden");
            Assert.Equal("Keyboard", rows[0].Name, "first row");
            Assert.Equal("86%", rows[0].PercentText, "percent text");
            Assert.False(rows[0].IsLow, "86% is not low");
            Assert.Equal(Theme.Light.Glyph, rows[0].GlyphBrush, "glyph brush");
            Assert.Equal(Theme.Light.TextPrimary, rows[0].NameBrush, "name brush");
            Assert.Equal(Theme.Light.TextSecondary, rows[0].PercentBrush, "percent brush");

            Assert.Equal("9%", rows[1].PercentText, "low percent text");
            Assert.True(rows[1].IsLow, "9% is low");
            Assert.Equal(Theme.Light.Low, rows[1].PercentBrush, "low percent is red");
            Assert.Equal(Visibility.Visible, rows[1].ChargingVisibility, "charging bolt visible");

            Assert.Equal(Visibility.Visible, List(view).Visibility, "list visible");
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("EmptyText")).Visibility,
                "empty hidden");
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("LoadingHost")).Visibility,
                "loading hidden");
        }

        private static void EmptyAndLoading()
        {
            var view = CreateView();

            view.SetDevices(new PeripheralDevice[0], scanning: true);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("LoadingHost")).Visibility,
                "skeleton while scanning");
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("EmptyText")).Visibility,
                "empty hidden while scanning");
            Assert.Equal(Visibility.Collapsed, List(view).Visibility, "list hidden while scanning");

            view.SetDevices(new PeripheralDevice[0], scanning: false);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("LoadingHost")).Visibility,
                "skeleton hidden after scan");
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("EmptyText")).Visibility,
                "empty state visible");

            view.SetDevices(TwoDevices(), scanning: true);
            Assert.Equal(Visibility.Visible, List(view).Visibility, "device list visible during a scan");
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("LoadingHost")).Visibility,
                "no skeleton when rows exist");
        }

        private static void SameSnapshotNoRebuild()
        {
            var view = CreateView();
            var devices = TwoDevices();
            view.SetDevices(devices, scanning: false);

            var row = Rows(view)[0];
            view.SetDevices(devices, scanning: true);
            Assert.True(ReferenceEquals(row, Rows(view)[0]),
                "same snapshot keeps the existing row object");

            view.SetDevices(TwoDevices(), scanning: true);
            Assert.True(ReferenceEquals(row, Rows(view)[0]),
                "an equal snapshot in a new list does not rebuild rows");
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("LoadingHost")).Visibility,
                "no skeleton while a scan runs with rows present");
            Assert.Equal(Visibility.Visible, List(view).Visibility, "rows stay visible");

            view.SetDevices(new[]
            {
                new PeripheralDevice
                {
                    Id = "k1", Name = "Keyboard", Kind = DeviceKind.Keyboard, BatteryPercent = 85
                }
            }, scanning: false);
            Assert.False(ReferenceEquals(row, Rows(view)[0]), "a changed value rebuilds the row");
        }

        private static void EditRename()
        {
            var view = CreateView();
            view.SetDevices(TwoDevices(), false);
            var events = new List<DeviceActionEventArgs>();
            view.DeviceRenameRequested += (sender, args) => events.Add(args);

            var edit = (Button)view.FindName("EditButton");
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(view.IsEditing, "edit mode on");

            var rows = Rows(view);
            Assert.Equal(Visibility.Collapsed, rows[0].NameVisibility, "name text hidden");
            Assert.Equal(Visibility.Visible, rows[0].EditVisibility, "text box visible");

            rows[0].EditableName = "  New Name  ";
            Invoke(view, "CommitRow", rows[0]);
            Assert.Equal(1, events.Count, "rename raised");
            Assert.Equal("k1", events[0].DeviceId, "device id");
            Assert.Equal("New Name", events[0].Name, "trimmed name");
            Assert.Equal("New Name", rows[0].Name, "row updated");
            Assert.Equal("New Name", rows[0].Device.Name, "backing snapshot updated");

            // The same name does not raise a second event.
            Invoke(view, "CommitRow", rows[0]);
            Assert.Equal(1, events.Count, "no rename when the value did not change");

            // While editing, a background snapshot is buffered instead of replacing the rows.
            view.SetDevices(new[]
            {
                new PeripheralDevice { Id = "x", Name = "Only", Kind = DeviceKind.Other, BatteryPercent = 50 }
            }, false);
            Assert.Equal(2, Rows(view).Count, "rows are stable while editing");

            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(view.IsEditing, "edit mode off");
            Assert.Equal(1, Rows(view).Count, "buffered snapshot rendered after editing");
        }

        private static void DeleteRaisesEvent()
        {
            var view = CreateView();
            view.SetDevices(TwoDevices(), false);
            var events = new List<DeviceActionEventArgs>();
            view.DeviceDeleteRequested += (sender, args) => events.Add(args);

            var row = Rows(view)[1];
            var button = new Button { Tag = row };
            Invoke(view, "DeleteButton_Click", button, new RoutedEventArgs());

            Assert.Equal(1, events.Count, "delete raised");
            Assert.Equal("m1", events[0].DeviceId, "deleted device id");
        }

        private static void RefreshFeedback()
        {
            var view = CreateView();
            var button = (Button)view.FindName("RefreshButton");
            var feedback = (TextBlock)view.FindName("RefreshFeedback");

            view.SetRefreshing(true);
            Assert.False(button.IsEnabled, "button disabled while refreshing");
            Assert.Equal("刷新中…", feedback.Text, "refreshing feedback");
            Assert.Equal(Visibility.Visible, feedback.Visibility, "feedback visible");

            view.ShowRefreshResult(true);
            Assert.True(button.IsEnabled, "button re-enabled");
            Assert.Equal("已刷新", feedback.Text, "success feedback");

            view.ShowRefreshResult(false);
            Assert.Equal("刷新失败", feedback.Text, "failure feedback");
        }

        private static void ThemeBrushes()
        {
            var view = CreateView(dark: true);
            view.SetDevices(TwoDevices(), false);

            var card = (Border)view.FindName("Card");
            Assert.Equal(Theme.Dark.Surface, card.Background, "card surface");
            Assert.Equal(Theme.Dark.Border, card.BorderBrush, "card border");

            var row = Rows(view)[0];
            Assert.Equal(Theme.Dark.Glyph, row.GlyphBrush, "glyph");
            Assert.Equal(Theme.Dark.TextPrimary, row.NameBrush, "name");
            Assert.Equal(Theme.Dark.TextSecondary, row.PercentBrush, "percent");

            view.ApplyTheme(Theme.Light);
            row = Rows(view)[0];
            Assert.Equal(Theme.Light.TextPrimary, row.NameBrush, "switched back");
        }

        private static void AccessibilityNames()
        {
            var view = CreateView();
            Assert.Equal("修改设备名", AutomationProperties.GetName((Button)view.FindName("EditButton")),
                "edit button name");
            Assert.Equal("刷新设备列表", AutomationProperties.GetName((Button)view.FindName("RefreshButton")),
                "refresh button name");
        }

        private static void DeviceRowBasics()
        {
            Assert.Throws<ArgumentNullException>(() => new DeviceRow(null), "null device rejected");

            var row = new DeviceRow(new PeripheralDevice
            {
                Id = "d", Name = "N", Kind = DeviceKind.Other, BatteryPercent = null
            });
            Assert.Equal("--", row.PercentText, "unknown percent placeholder");
            Assert.False(row.IsLow, "unknown is not low");
            Assert.Equal(Visibility.Collapsed, row.ChargingVisibility, "not charging");
        }

        private static void LongListIsBounded()
        {
            var view = CreateView();
            view.Width = PanelView.PanelWidth + PanelView.ShadowMargin * 2;
            view.SetMaxContentHeight(120);

            var devices = new List<PeripheralDevice>();
            for (var i = 0; i < 20; i++)
            {
                devices.Add(new PeripheralDevice
                {
                    Id = "d" + i,
                    Name = "Device " + i.ToString("00"),
                    Kind = i % 2 == 0 ? DeviceKind.Keyboard : DeviceKind.Mouse,
                    BatteryPercent = 50
                });
            }

            view.SetDevices(devices, false);
            view.Measure(new Size(view.Width, double.PositiveInfinity));
            view.Arrange(new Rect(0, 0, view.Width, view.DesiredSize.Height));
            view.UpdateLayout();

            var scroll = (ScrollViewer)view.FindName("DeviceScroll");
            Assert.NotNull(scroll, "device list scroll host");
            Assert.True(scroll.ActualHeight <= 121,
                "scroll host is bounded (actual " + scroll.ActualHeight + ")");
            Assert.True(view.DesiredSize.Height < 400,
                "panel height stays reasonable (actual " + view.DesiredSize.Height + ")");
        }

        private static void Invoke(PanelView view, string method, params object[] args)
        {
            var info = typeof(PanelView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, "method " + method);
            try
            {
                info.Invoke(view, args);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }
    }
}
