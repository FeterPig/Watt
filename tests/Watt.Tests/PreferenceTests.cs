using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Watt.Models;
using Watt.Settings;

namespace Watt.Tests
{
    internal static class PreferenceTests
    {
        internal static void Register(TestRunner runner)
        {
            runner.Run("preferences/apply renames, hides and does not mutate input", ApplyAndClone);
            runner.Run("preferences/rename trims, limits length and removes overrides", RenameRules);
            runner.Run("preferences/hide, restore and hidden count persist", HideRestorePersist);
            runner.Run("preferences/corrupt preferences file is tolerated", CorruptPreferences);
            runner.Run("preferences/control characters in names are replaced", ControlCharacters);
        }

        private static IReadOnlyList<PeripheralDevice> Snapshot()
        {
            return new[]
            {
                new PeripheralDevice
                {
                    Id = "bt:01", Name = "Original Keyboard", Kind = DeviceKind.Keyboard,
                    BatteryPercent = 80, SourceId = "aep"
                },
                new PeripheralDevice
                {
                    Id = "bt:02", Name = "Original Mouse", Kind = DeviceKind.Mouse,
                    BatteryPercent = 50, SourceId = "aep"
                }
            };
        }

        private static void ApplyAndClone()
        {
            var path = Path.Combine(TestEnv.UniqueDirectory("prefs"), "devices.json");
            var preferences = new DevicePreferences(path, null);

            preferences.Rename("bt:01", "My Keyboard");
            preferences.Hide("bt:02");

            var input = Snapshot();
            var applied = preferences.Apply(input);

            Assert.Equal(1, applied.Count, "hidden device removed");
            Assert.Equal("My Keyboard", applied[0].Name, "custom name applied");
            Assert.Equal("Original Keyboard", input[0].Name, "input snapshot was not mutated");
            Assert.Equal("Original Mouse", input[1].Name, "second input snapshot was not mutated");

            // Callers can mutate the returned copy without affecting the service snapshot.
            applied[0].Name = "Changed";
            Assert.Equal("Original Keyboard", input[0].Name, "returned copy is independent");
        }

        private static void RenameRules()
        {
            var path = Path.Combine(TestEnv.UniqueDirectory("prefs"), "devices.json");
            var preferences = new DevicePreferences(path, null);

            preferences.Rename("d1", "  A name  ");
            Assert.Equal("A name", preferences.Apply(new[]
            {
                new PeripheralDevice { Id = "d1", Name = "Original", Kind = DeviceKind.Other, BatteryPercent = 1 }
            })[0].Name, "trimmed name applied");

            var longName = new string('x', 60);
            preferences.Rename("d1", longName);
            var applied = preferences.Apply(new[]
            {
                new PeripheralDevice { Id = "d1", Name = "Original", Kind = DeviceKind.Other, BatteryPercent = 1 }
            })[0];
            Assert.Equal(40, applied.Name.Length, "names are limited to 40 characters");

            preferences.Rename("d1", "");
            applied = preferences.Apply(new[]
            {
                new PeripheralDevice { Id = "d1", Name = "Original", Kind = DeviceKind.Other, BatteryPercent = 1 }
            })[0];
            Assert.Equal("Original", applied.Name, "empty rename removes the override");
        }

        private static void HideRestorePersist()
        {
            var path = Path.Combine(TestEnv.UniqueDirectory("prefs"), "devices.json");
            var preferences = new DevicePreferences(path, null);
            preferences.Rename("bt:01", "Named");
            preferences.Hide("bt:01");
            preferences.Hide("bt:02");
            Assert.Equal(2, preferences.HiddenCount, "two hidden devices");

            var reloaded = new DevicePreferences(path, null);
            Assert.Equal(2, reloaded.HiddenCount, "hidden count reloaded");
            Assert.Equal(0, reloaded.Apply(Snapshot()).Count, "all snapshots hidden");

            Assert.True(reloaded.RestoreHidden(), "restore reports a change");
            Assert.Equal(0, reloaded.HiddenCount, "nothing hidden after restore");
            Assert.Equal(2, reloaded.Apply(Snapshot()).Count, "devices visible again");
            Assert.False(reloaded.RestoreHidden(), "second restore is a no-op");

            var afterRestore = new DevicePreferences(path, null);
            Assert.Equal(0, afterRestore.HiddenCount, "restore persisted");
            Assert.Equal("Named", afterRestore.Apply(Snapshot())[0].Name, "name survived restore");
        }

        private static void CorruptPreferences()
        {
            var directory = TestEnv.UniqueDirectory("prefs");
            var path = Path.Combine(directory, "devices.json");
            File.WriteAllText(path, "{ broken");

            var preferences = new DevicePreferences(path, null);
            var applied = preferences.Apply(Snapshot());
            Assert.Equal(2, applied.Count, "corrupt file ignored, devices still returned");

            preferences.Rename("bt:01", "Recovered");
            var reloaded = new DevicePreferences(path, null);
            Assert.Equal("Recovered", reloaded.Apply(Snapshot())[0].Name, "preferences recover after the next save");
        }

        private static void ControlCharacters()
        {
            var path = Path.Combine(TestEnv.UniqueDirectory("prefs"), "devices.json");
            var preferences = new DevicePreferences(path, null);
            preferences.Rename("d1", "Line\r\nBreak\tTab");

            var name = preferences.Apply(new[]
            {
                new PeripheralDevice { Id = "d1", Name = "Original", Kind = DeviceKind.Other, BatteryPercent = 1 }
            })[0].Name;
            Assert.False(name.Contains("\r"), "carriage return removed");
            Assert.False(name.Contains("\n"), "newline removed");
            Assert.False(name.Contains("\t"), "tab removed");
        }
    }
}
