using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Watt.Models;
using Watt.Plugins.Support;

namespace Watt.Tests
{
    internal static class ClassifierTests
    {
        internal static void Register(TestRunner runner)
        {
            runner.Run("classifier/names win over class-of-device", NameClassification);
            runner.Run("classifier/class-of-device fallback", ClassOfDevice);
            runner.Run("classifier/normalize name collapses whitespace and case", NormalizeName);
            runner.Run("classifier/normalize address accepts only 48-bit values", NormalizeAddress);
            runner.Run("classifier/build id uses normalized MAC or a stable slug", BuildId);
            runner.Run("classifier/sort rank and percent clamp", SortAndClamp);
        }

        private static void NameClassification()
        {
            var cases = new Dictionary<string, DeviceKind>
            {
                { "WH-1000XM5", DeviceKind.Headphone },
                { "EDIFIER Fit900NB", DeviceKind.Headphone },
                { "Soundcore Q45", DeviceKind.Headphone },
                { "MX Keys", DeviceKind.Keyboard },
                { "Keychron K8", DeviceKind.Keyboard },
                { "MX Master 3S", DeviceKind.Mouse },
                { "Logitech G502", DeviceKind.Mouse },
                { "Xbox Wireless Controller", DeviceKind.Other }
            };

            foreach (var pair in cases)
                Assert.Equal(pair.Value, DeviceClassifier.Classify(pair.Key, null, null), pair.Key);
        }

        private static void ClassOfDevice()
        {
            Assert.Equal(DeviceKind.Headphone, DeviceClassifier.FromClassOfDevice(0x04, 0), "audio major");
            Assert.Equal(DeviceKind.Keyboard, DeviceClassifier.Classify(null, 0x05, 0x40), "keyboard bit");
            Assert.Equal(DeviceKind.Mouse, DeviceClassifier.Classify(null, 0x05, 0x80), "pointing bit");
            Assert.Equal(DeviceKind.Other, DeviceClassifier.Classify(null, 0x05, 0x00), "other peripheral");
            Assert.Equal(DeviceKind.Other, DeviceClassifier.Classify(null, 0x07, 0), "wearable");
            Assert.Equal(null, DeviceClassifier.FromClassOfDevice(null, 0), "unknown major");
            Assert.Equal(null, DeviceClassifier.FromClassOfDevice(0x01, 0), "computer major is not mapped");
        }

        private static void NormalizeName()
        {
            Assert.Equal("mx keys", DeviceClassifier.NormalizeName("  MX   Keys  "), "trim and collapse");
            Assert.Equal("", DeviceClassifier.NormalizeName(null), "null name");
            Assert.Equal("", DeviceClassifier.NormalizeName("   "), "blank name");
        }

        private static void NormalizeAddress()
        {
            Assert.Equal("aa:bb:cc:dd:ee:ff", DeviceClassifier.NormalizeAddress("AA-BB-CC-DD-EE-FF"), "dashes");
            Assert.Equal("aa:bb:cc:dd:ee:ff", DeviceClassifier.NormalizeAddress("aabbccddeeff"), "bare hex");
            Assert.Equal(null, DeviceClassifier.NormalizeAddress("aa:bb"), "too short");
            Assert.Equal(null, DeviceClassifier.NormalizeAddress("aa:bb:cc:dd:ee:ff:00"), "too long");
            Assert.Equal(null, DeviceClassifier.NormalizeAddress(null), "null");
        }

        private static void BuildId()
        {
            Assert.Equal("bt:aa:bb:cc:dd:ee:ff",
                DeviceClassifier.BuildId("AA:BB:CC:DD:EE:FF", "Mouse", "fallback"), "MAC id");
            Assert.Equal("my_keyboard", DeviceClassifier.BuildId(null, "My Keyboard", "fallback"), "slug");
            Assert.Equal("vendor:fallback", DeviceClassifier.BuildId(null, null, "fallback", "vendor"),
                "fallback with prefix");
            Assert.Equal("unknown", DeviceClassifier.BuildId(null, null, null), "unknown fallback");
        }

        private static void SortAndClamp()
        {
            Assert.True(DeviceClassifier.SortRank(DeviceKind.Keyboard) < DeviceClassifier.SortRank(DeviceKind.Mouse),
                "keyboard before mouse");
            Assert.True(DeviceClassifier.SortRank(DeviceKind.Mouse) < DeviceClassifier.SortRank(DeviceKind.Headphone),
                "mouse before headphone");
            Assert.True(DeviceClassifier.SortRank(DeviceKind.Headphone) < DeviceClassifier.SortRank(DeviceKind.Other),
                "headphone before other");

            Assert.Equal(0, DeviceClassifier.ClampPercent(-1), "clamp low");
            Assert.Equal(100, DeviceClassifier.ClampPercent(101), "clamp high");
            Assert.Equal(42, DeviceClassifier.ClampPercent(42), "in range");
        }
    }

    internal static class SupportTests
    {
        internal static void Register(TestRunner runner)
        {
            runner.Run("support/provider conversions", ProviderConversions);
            runner.Run("support/await or null handles success, failure and timeout", AwaitOrNull);
            runner.Run("support/bluetooth address formatting", AddressFormatting);
            runner.Run("support/hid enumeration never throws", HidEnumeration);
        }

        private static void ProviderConversions()
        {
            var values = new Dictionary<string, object>
            {
                { "byte", (byte)7 },
                { "sbyte", (sbyte)-8 },
                { "ushort", (ushort)9 },
                { "short", (short)-10 },
                { "int", 11 },
                { "uint", (uint)12 },
                { "uintbig", uint.MaxValue },
                { "long", 13L },
                { "bool", true },
                { "string", "14" },
                { "badstring", "nope" },
                { "null", null }
            };

            Assert.Equal(7, ProviderUtil.TryGetInt(values, "byte"), "byte");
            Assert.Equal(-8, ProviderUtil.TryGetInt(values, "sbyte"), "sbyte");
            Assert.Equal(9, ProviderUtil.TryGetInt(values, "ushort"), "ushort");
            Assert.Equal(-10, ProviderUtil.TryGetInt(values, "short"), "short");
            Assert.Equal(11, ProviderUtil.TryGetInt(values, "int"), "int");
            Assert.Equal(12, ProviderUtil.TryGetInt(values, "uint"), "uint");
            Assert.Equal(null, ProviderUtil.TryGetInt(values, "uintbig"), "uint overflow");
            Assert.Equal(13, ProviderUtil.TryGetInt(values, "long"), "long");
            Assert.Equal(1, ProviderUtil.TryGetInt(values, "bool"), "bool");
            Assert.Equal(14, ProviderUtil.TryGetInt(values, "string"), "numeric string");
            Assert.Equal(null, ProviderUtil.TryGetInt(values, "badstring"), "bad string");
            Assert.Equal(null, ProviderUtil.TryGetInt(values, "missing"), "missing");
            Assert.Equal(null, ProviderUtil.TryGetInt(null, "int"), "null dictionary");

            bool flag;
            Assert.True(ProviderUtil.TryGetBool(values, "bool", out flag) && flag, "bool");
            var boolString = new Dictionary<string, object> { { "s", "true" } };
            Assert.True(ProviderUtil.TryGetBool(boolString, "s", out flag) && flag, "bool string");
            Assert.False(ProviderUtil.TryGetBool(values, "int", out flag), "int is not a bool");
            Assert.Equal("value", ProviderUtil.TryGetString(new Dictionary<string, object> { { "s", "value" } }, "s"),
                "string");
            Assert.Equal(null, ProviderUtil.TryGetString(values, "int"), "non-string");
        }

        private static void AwaitOrNull()
        {
            var value = ProviderUtil.AwaitOrNullAsync(Task.FromResult("ok"), 1000).GetAwaiter().GetResult();
            Assert.Equal("ok", value, "completed task");

            var slow = ProviderUtil.AwaitOrNullAsync(new TaskCompletionSource<string>().Task, 50)
                .GetAwaiter().GetResult();
            Assert.Null(slow, "timeout returns null");

            var faulted = ProviderUtil.AwaitOrNullAsync(Func(), 1000).GetAwaiter().GetResult();
            Assert.Null(faulted, "fault returns null");
        }

        private static async Task<string> Func()
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }

        private static void AddressFormatting()
        {
            Assert.Equal("11:22:33:44:55:66", ProviderUtil.FormatBluetoothAddress(0x112233445566UL), "address");
            Assert.Equal("00:00:00:00:00:01", ProviderUtil.FormatBluetoothAddress(1), "leading zeroes");
        }

        private static void HidEnumeration()
        {
            var collections = HidNative.EnumerateAllPresentCollections();
            Assert.NotNull(collections, "list");
            foreach (var info in collections)
            {
                Assert.NotNull(info.DevicePath, "device path");
                Assert.True(info.InputReportByteLength >= 0, "non-negative input length");
            }
        }
    }
}
