using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Watt.Plugins.Support
{
    /// <summary>
    /// Reads the battery Windows exposes on classic-Bluetooth (BR/EDR) PnP device nodes.
    ///
    /// Some Bluetooth headsets (verified with EDIFIER W820NB by the VigilWatt project) publish
    /// their remaining charge as DEVPKEY_Device_BatteryLevel on the HFP gateway node, without
    /// exposing it through the AEP shell properties or a GATT battery service. This helper
    /// enumerates BTHENUM\* nodes, groups them by device MAC and returns the best battery value.
    ///
    /// Device-property key and grouping approach: VigilWatt (MIT, 漏 2026 evren),
    /// docs/dev/runtime-pitfalls.md 搂3 and src/VigilWatt.Core/Ble/ClassicDevNodeReader.cs.
    /// The implementation here is a small independent copy of that documented method.
    /// </summary>
    public static class HfpBatteryReader
    {
        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfAllClasses = 0x00000004;
        private const uint DevPropTypeByte = 0x03;
        private const uint DevPropTypeUInt16 = 0x05;
        private const uint DevPropTypeUInt32 = 0x07;

        private static readonly IntPtr InvalidHandle = new IntPtr(-1);
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);
        private static readonly object CacheLock = new object();
        private static Dictionary<string, int> _cache;
        private static DateTime _cacheAt = DateTime.MinValue;

        private static readonly Regex DevNodeMacRegex = new Regex(
            @"&(?:BLUETOOTHDEVICE_)?([0-9A-Fa-f]{12})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly DevPropKey BatteryKey = new DevPropKey
        {
            FmtId = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"),
            Pid = 2
        };

        /// <summary>MAC (12 hex, upper case) 鈫?battery percent. Never throws; empty on failure.</summary>
        public static IReadOnlyDictionary<string, int> ReadByMac()
        {
            lock (CacheLock)
            {
                if (_cache != null && DateTime.UtcNow - _cacheAt < CacheTtl) return _cache;
                try
                {
                    _cache = ReadUncached();
                }
                catch
                {
                    _cache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                }
                _cacheAt = DateTime.UtcNow;
                return _cache;
            }
        }

        private static Dictionary<string, int> ReadUncached()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
            if (set == IntPtr.Zero || set == InvalidHandle) return map;

            try
            {
                var info = new SpDevinfoData();
                info.Size = (uint)Marshal.SizeOf(typeof(SpDevinfoData));
                var instanceId = new StringBuilder(512);

                for (uint index = 0; SetupDiEnumDeviceInfo(set, index, ref info); index++)
                {
                    instanceId.Length = 0;
                    if (!SetupDiGetDeviceInstanceIdW(set, ref info, instanceId,
                            (uint)instanceId.Capacity, out _))
                        continue;

                    var id = instanceId.ToString();
                    if (id.IndexOf(@"BTHENUM\", StringComparison.OrdinalIgnoreCase) != 0) continue;

                    string mac = null;
                    foreach (Match match in DevNodeMacRegex.Matches(id))
                    {
                        mac = match.Groups[1].Value;
                    }
                    if (string.IsNullOrEmpty(mac)) continue;
                    mac = mac.ToUpperInvariant();

                    var percent = ReadBatteryPercent(set, ref info);
                    if (!percent.HasValue || percent.Value <= 0 || percent.Value > 100) continue;

                    int existing;
                    if (!map.TryGetValue(mac, out existing) || percent.Value < existing)
                    {
                        // The HFP gateway node is the one carrying the level. Prefer the smallest
                        // plausible value when several service nodes report something.
                        map[mac] = percent.Value;
                    }
                }
            }
            finally
            {
                try { SetupDiDestroyDeviceInfoList(set); } catch { }
            }

            return map;
        }

        private static int? ReadBatteryPercent(IntPtr set, ref SpDevinfoData info)
        {
            uint type;
            uint required;
            var key = BatteryKey;
            SetupDiGetDevicePropertyW(set, ref info, ref key, out type, null, 0, out required, 0);
            if (required == 0 || required > 64) return null;

            var buffer = new byte[required];
            if (!SetupDiGetDevicePropertyW(set, ref info, ref key, out type, buffer, (uint)buffer.Length,
                    out required, 0))
                return null;

            switch (type)
            {
                case DevPropTypeByte:
                    return buffer.Length >= 1 ? buffer[0] : (int?)null;
                case DevPropTypeUInt16:
                    return buffer.Length >= 2 ? (int)BitConverter.ToUInt16(buffer, 0) : (int?)null;
                case DevPropTypeUInt32:
                    return buffer.Length >= 4 ? (int)BitConverter.ToUInt32(buffer, 0) : (int?)null;
                default:
                    return null;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DevPropKey
        {
            public Guid FmtId;
            public uint Pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDevinfoData
        {
            public uint Size;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr parent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex,
            ref SpDevinfoData deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
            StringBuilder deviceInstanceId, uint deviceInstanceIdSize, out uint requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
            ref DevPropKey propertyKey, out uint propertyType, byte[] propertyBuffer, uint propertyBufferSize,
            out uint requiredSize, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
    }
}

