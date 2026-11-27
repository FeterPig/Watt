using System;
using System.Collections.Generic;
using System.IO;
using Watt.Battery;
using Watt.Plugins.BleGatt;
using Watt.Plugins.Support;

namespace Watt.Tests
{
    internal static class TestEnv
    {
        internal static string DataRoot { get; private set; }
        internal static string RepoRoot { get; private set; }

        internal static void Initialize()
        {
            RepoRoot = FindRepoRoot();
            // Keep test data inside the checkout: the Windows temp directory can be governed by
            // an unrelated sandbox policy, while the workspace is the writable root for the app.
            DataRoot = Path.Combine(RepoRoot, ".test-out", "test-data",
                "run-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataRoot);
            Environment.SetEnvironmentVariable(WattPaths.DataDirectoryVariable, DataRoot);
        }

        /// <summary>Fresh per-test state: the in-memory stores forget everything and the data
        /// files are removed, so tests never depend on execution order.</summary>
        internal static void Reset()
        {
            LastKnownStore.ResetForTests();
            BleBatteryGuard.ResetForTests();
            Delete(PathFor("last-known.json"));
            Delete(PathFor("devices.json"));
            Delete(PathFor("ble-battery-guard.txt"));
            Delete(PathFor("last-known.json.1"));
        }

        internal static string PathFor(string name)
        {
            return Path.Combine(DataRoot, name);
        }

        internal static string UniqueDirectory(string prefix)
        {
            var directory = Path.Combine(DataRoot, prefix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        internal static void Cleanup()
        {
            try
            {
                if (Directory.Exists(DataRoot)) Directory.Delete(DataRoot, true);
            }
            catch
            {
                // A temp directory is harmless.
            }
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "build.cmd")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "src")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new InvalidOperationException("Could not locate the Watt repository root from " +
                                                AppDomain.CurrentDomain.BaseDirectory);
        }

        private static void Delete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }
    }

    internal sealed class TestRunner
    {
        private readonly string _filter;
        private int _passed;
        private int _failed;

        internal TestRunner(string filter)
        {
            _filter = string.IsNullOrWhiteSpace(filter) ? null : filter;
        }

        internal void Run(string name, Action test)
        {
            if (_filter != null && name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) return;

            TestEnv.Reset();
            try
            {
                test();
                _passed++;
                Console.WriteLine("PASS  " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("FAIL  " + name);
                Console.WriteLine("      " + ex.GetType().Name + ": " + ex.Message);
                if (ex is TestFailure) return;
                Console.WriteLine(ex.StackTrace);
            }
        }

        internal int Finish()
        {
            Console.WriteLine();
            Console.WriteLine("tests run: " + (_passed + _failed) + "  passed: " + _passed + "  failed: " + _failed);
            return _failed == 0 ? 0 : 1;
        }
    }

    internal sealed class TestFailure : Exception
    {
        internal TestFailure(string message) : base(message)
        {
        }
    }

    internal static class Assert
    {
        internal static void True(bool condition, string message)
        {
            if (!condition) throw new TestFailure(message);
        }

        internal static void False(bool condition, string message)
        {
            if (condition) throw new TestFailure(message);
        }

        internal static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new TestFailure(message + " (expected: " + expected + ", actual: " + actual + ")");
        }

        internal static void NotNull(object value, string message)
        {
            if (value == null) throw new TestFailure(message + " (was null)");
        }

        internal static void Null(object value, string message)
        {
            if (value != null) throw new TestFailure(message + " (was: " + value + ")");
        }

        internal static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
        {
            var left = new List<T>(expected);
            var right = new List<T>(actual);
            if (left.Count != right.Count)
                throw new TestFailure(message + " (count expected " + left.Count + ", actual " + right.Count + ")");
            for (var i = 0; i < left.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                    throw new TestFailure(message + " (index " + i + ": expected " + left[i] + ", actual " + right[i] + ")");
            }
        }

        internal static void Throws<TException>(Action action, string message) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new TestFailure(message + " (threw " + ex.GetType().Name + " instead of " +
                                      typeof(TException).Name + ")");
            }

            throw new TestFailure(message + " (did not throw)");
        }
    }
}
