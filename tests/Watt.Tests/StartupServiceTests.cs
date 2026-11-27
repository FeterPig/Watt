using System;
using System.Collections.Generic;
using Watt.Services;

namespace Watt.Tests
{
    internal static class StartupServiceTests
    {
        internal static void Register(TestRunner runner)
        {
            runner.Run("startup/enable writes a quoted command and reads it back", EnableDisable);
            runner.Run("startup/ensure enabled repairs stale and missing values", EnsureEnabled);
            runner.Run("startup/quote and normalize handle whitespace and quotes", QuoteRules);
            runner.Run("startup/store failures never escape", StoreFailures);
        }

        private sealed class MemoryStore : IRunKeyStore
        {
            internal readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
            internal bool RefuseWrites;
            internal bool Throw;

            public string ReadValue(string runKeyPath, string valueName)
            {
                if (Throw) throw new InvalidOperationException("read failed");
                string value;
                return Values.TryGetValue(runKeyPath + "\\" + valueName, out value) ? value : null;
            }

            public bool WriteValue(string runKeyPath, string valueName, string value)
            {
                if (Throw) throw new InvalidOperationException("write failed");
                if (RefuseWrites) return false;
                Values[runKeyPath + "\\" + valueName] = value;
                return true;
            }

            public bool DeleteValue(string runKeyPath, string valueName)
            {
                if (Throw) throw new InvalidOperationException("delete failed");
                return Values.Remove(runKeyPath + "\\" + valueName);
            }
        }

        private static StartupService Create(MemoryStore store, string exe = @"C:\Apps\Watt\Watt.exe")
        {
            return new StartupService("Software\\Watt.Tests\\Run", "Watt", () => exe, store);
        }

        private static void EnableDisable()
        {
            var store = new MemoryStore();
            var service = Create(store);

            Assert.False(service.IsEnabled, "initially disabled");
            service.Enable();
            Assert.True(service.IsEnabled, "enabled");
            Assert.Equal("\"C:\\Apps\\Watt\\Watt.exe\"", service.RegisteredCommand, "quoted command");
            Assert.Equal("\"C:\\Apps\\Watt\\Watt.exe\"", service.ExpectedCommand, "expected command");

            service.Disable();
            Assert.False(service.IsEnabled, "disabled");
        }

        private static void EnsureEnabled()
        {
            var store = new MemoryStore();
            var service = Create(store);

            Assert.True(service.EnsureEnabled(@"C:\Apps\Watt\Watt.exe"), "missing value written");
            Assert.False(service.EnsureEnabled(@"C:\Apps\Watt\Watt.exe"), "matching value not rewritten");

            store.Values["Software\\Watt.Tests\\Run\\Watt"] = "\"D:\\Old\\Watt.exe\"";
            Assert.True(service.EnsureEnabled(@"C:\Apps\Watt\Watt.exe"), "stale value repaired");
            Assert.Equal("\"C:\\Apps\\Watt\\Watt.exe\"", service.RegisteredCommand, "repaired command");
        }

        private static void QuoteRules()
        {
            Assert.Equal("\"C:\\Apps\\Watt\\Watt.exe\"", StartupService.Quote(" C:\\Apps\\Watt\\Watt.exe "),
                "whitespace trimmed");
            Assert.Equal("\"C:\\Apps\\Watt\\Watt.exe\"", StartupService.Quote("\"C:\\Apps\\Watt\\Watt.exe\""),
                "quotes not doubled");
            Assert.Equal(null, StartupService.Quote(null), "null path");
            Assert.Equal(null, StartupService.Quote("  "), "blank path becomes an empty quoted command?");
        }

        private static void StoreFailures()
        {
            var store = new MemoryStore { RefuseWrites = true };
            var service = Create(store);
            service.Enable();
            Assert.False(service.IsEnabled, "refused write leaves autostart off");

            var throwing = new MemoryStore { Throw = true };
            var service2 = Create(throwing);
            service2.Enable();
            service2.Disable();
            Assert.False(service2.IsEnabled, "read error is treated as disabled");
        }
    }
}
