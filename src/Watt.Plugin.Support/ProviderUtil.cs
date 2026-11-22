using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Watt.Models;

namespace Watt.Plugins.Support
{
    /// <summary>
    /// Small exception-safe helpers shared by the providers. Everything here is defensive:
    /// a provider must degrade to "no reading" instead of throwing into the service.
    /// </summary>
    public static class ProviderUtil
    {
        public static readonly IReadOnlyList<PeripheralDevice> Empty = new PeripheralDevice[0];

        /// <summary>Fire-and-forget <c>Task.WhenAny</c> wrapper with a timeout; null on timeout.</summary>
        public static async Task<T> AwaitOrNullAsync<T>(Task<T> task, int timeoutMs) where T : class
        {
            if (task == null) return null;
            try
            {
                var completed = await Task.WhenAny(task, Task.Delay(timeoutMs)).ConfigureAwait(false);
                if (completed != task)
                {
                    Observe(task);
                    return null;
                }

                return await task.ConfigureAwait(false);
            }
            catch
            {
                Observe(task);
                return null;
            }
        }

        /// <summary>Attaches a continuation so a late failure never becomes an unobserved task exception.</summary>
        public static void Observe(Task task)
        {
            if (task == null) return;
            try
            {
                task.ContinueWith(
                    t => { var ignored = t.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch
            {
                // ignore
            }
        }

        public static int? TryGetInt(IReadOnlyDictionary<string, object> properties, string key)
        {
            object raw;
            if (properties == null || string.IsNullOrEmpty(key) || !properties.TryGetValue(key, out raw) || raw == null)
                return null;

            try
            {
                if (raw is byte) return (byte)raw;
                if (raw is sbyte) return (sbyte)raw;
                if (raw is ushort) return (ushort)raw;
                if (raw is short) return (short)raw;
                if (raw is int) return (int)raw;
                if (raw is uint)
                {
                    var value = (uint)raw;
                    return value <= int.MaxValue ? (int)value : (int?)null;
                }
                if (raw is long)
                {
                    var value = (long)raw;
                    return value >= int.MinValue && value <= int.MaxValue ? (int)value : (int?)null;
                }
                if (raw is bool) return ((bool)raw) ? 1 : 0;

                var text = raw as string;
                int parsed;
                if (text != null)
                    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                        ? (int?)parsed
                        : null;

                var convertible = raw as IConvertible;
                if (convertible != null) return convertible.ToInt32(CultureInfo.InvariantCulture);
            }
            catch
            {
                // fall through to null
            }

            return null;
        }

        public static bool TryGetBool(IReadOnlyDictionary<string, object> properties, string key, out bool value)
        {
            value = false;
            object raw;
            if (properties == null || string.IsNullOrEmpty(key) || !properties.TryGetValue(key, out raw) || raw == null)
                return false;

            try
            {
                if (raw is bool)
                {
                    value = (bool)raw;
                    return true;
                }

                var text = raw as string;
                if (text != null && bool.TryParse(text, out value)) return true;
            }
            catch
            {
                // fall through
            }

            return false;
        }

        public static string TryGetString(IReadOnlyDictionary<string, object> properties, string key)
        {
            object raw;
            if (properties == null || string.IsNullOrEmpty(key) || !properties.TryGetValue(key, out raw) || raw == null)
                return null;

            try
            {
                return raw as string;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Converts a 48-bit Bluetooth address into "aa:bb:cc:dd:ee:ff".</summary>
        public static string FormatBluetoothAddress(ulong address)
        {
            var bytes = new byte[6];
            for (var i = 0; i < 6; i++) bytes[i] = (byte)(address >> ((5 - i) * 8));
            return string.Format(CultureInfo.InvariantCulture, "{0:x2}:{1:x2}:{2:x2}:{3:x2}:{4:x2}:{5:x2}",
                bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5]);
        }
    }
}

