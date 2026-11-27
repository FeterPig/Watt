using System;
using System.Text;
using Watt.Models;

namespace Watt.Plugins.Support
{
    /// <summary>
    /// Best-effort mapping from a device name / Bluetooth class-of-device to the three categories
    /// the panel can draw. Name heuristics are primary because Bluetooth class-of-device is
    /// missing on BLE-only devices and frequently lies about audio endpoints.
    /// </summary>
    public static class DeviceClassifier
    {
        // Order matters: audio names are checked first because "G Pro X Headset" also contains a
        // mouse-model token, and "MX Keys" must be a keyboard while "MX Master" is a mouse.
        private static readonly string[] HeadphoneTokens =
        {
            "headphone", "headset", "earbud", "earphone", "ear buds", "earbuds", "airpods",
            "quietcomfort", "soundcore", "sennheiser", "wh-", "wf-", "ch-", "hifi", " hi-fi",
            "speaker", "soundbar", "buds", "zone wireless", "zone ", "qc3", "qc45", "qc ",
            "tune", "beats ", "jbl ", "bose ", "crusher", "pulse", "void", "arctis",
            "edifier", "razer kraken"
        };

        private static readonly string[] KeyboardTokens =
        {
            "keyboard", "keychron", "mx keys", " keys", "keypad", "numpad",
            "k380", "k400", "k480", "k580", "k650", "k780", "k800", "k850",
            "craft", "pop keys", "pop icon", "folio", "slim folio"
        };

        private static readonly string[] MouseTokens =
        {
            "mouse", "master", "anywhere", "mx ergo", "mx lift", "lift",
            "trackball", "trackpad", "m185", "m330", "m590", "m650", "m720", "m750",
            "m850", "g304", "g305", "g403", "g502", "g603", "g703", "g903", "g pro",
            "superlight", "basilisk", "orochi", "lift vertical"
        };

        public static DeviceKind Classify(string name, int? codMajor, int? codMinor)
        {
            var lower = (name ?? string.Empty).ToLowerInvariant();
            if (lower.Length > 0)
            {
                if (ContainsAny(lower, HeadphoneTokens)) return DeviceKind.Headphone;
                if (ContainsAny(lower, KeyboardTokens)) return DeviceKind.Keyboard;
                if (ContainsAny(lower, MouseTokens)) return DeviceKind.Mouse;
            }

            var fromCod = FromClassOfDevice(codMajor, codMinor);
            return fromCod ?? DeviceKind.Other;
        }

        /// <summary>Bluetooth class-of-device major/minor decoding (Bluetooth Assigned Numbers, baseband).</summary>
        public static DeviceKind? FromClassOfDevice(int? codMajor, int? codMinor)
        {
            if (!codMajor.HasValue) return null;

            switch (codMajor.Value)
            {
                case 0x04: // Audio/Video: headsets, headphones, speakers, portable audio
                    return DeviceKind.Headphone;
                case 0x05: // Peripheral: bits 7-6 of the minor field pick keyboard/pointing/combo
                {
                    var minor = codMinor ?? 0;
                    var pointing = (minor & 0x80) != 0;
                    var keyboard = (minor & 0x40) != 0;
                    if (keyboard) return DeviceKind.Keyboard;
                    if (pointing) return DeviceKind.Mouse;
                    return DeviceKind.Other;
                }
                case 0x07: // Wearable (watches, bands)
                    return DeviceKind.Other;
                default:
                    return null;
            }
        }

        /// <summary>True for the categories the panel shows above the fold.</summary>
        public static int SortRank(DeviceKind kind)
        {
            switch (kind)
            {
                case DeviceKind.Keyboard: return 0;
                case DeviceKind.Mouse: return 1;
                case DeviceKind.Headphone: return 2;
                default: return 3;
            }
        }

        public static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var builder = new StringBuilder(name.Length);
            var lastWasSpace = true;
            foreach (var raw in name.Trim())
            {
                var c = char.ToLowerInvariant(raw);
                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace) builder.Append(' ');
                    lastWasSpace = true;
                }
                else
                {
                    builder.Append(c);
                    lastWasSpace = false;
                }
            }
            return builder.ToString().Trim();
        }

        /// <summary>
        /// Normalizes a Bluetooth address to lowercase "aa:bb:cc:dd:ee:ff". Returns null for input
        /// that cannot be a 48-bit address.
        /// </summary>
        public static string NormalizeAddress(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var hex = new StringBuilder(12);
            foreach (var c in raw)
            {
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                    hex.Append(char.ToLowerInvariant(c));
            }

            if (hex.Length != 12) return null;

            var result = new StringBuilder(17);
            for (var i = 0; i < 12; i += 2)
            {
                if (i > 0) result.Append(':');
                result.Append(hex[i]).Append(hex[i + 1]);
            }
            return result.ToString();
        }

        /// <summary>Deterministic identity for a reading, used for merge keys and panel rows.
        /// Bluetooth readings share the "bt:&lt;mac&gt;" form so different plugins observing the
        /// same endpoint merge; plugins with non-Bluetooth devices pass their own stable prefix
        /// through <paramref name="fallbackPrefix"/>.</summary>
        public static string BuildId(string address, string name, string fallbackKey, string fallbackPrefix = null)
        {
            var normalized = NormalizeAddress(address);
            if (normalized != null) return "bt:" + normalized;

            var slug = Slug(name);
            if (slug.Length > 0)
            {
                return string.IsNullOrEmpty(fallbackPrefix) ? slug : fallbackPrefix + ":" + slug;
            }

            var fallback = fallbackKey ?? "unknown";
            return string.IsNullOrEmpty(fallbackPrefix) ? fallback : fallbackPrefix + ":" + fallback;
        }

        private static string Slug(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var builder = new StringBuilder(name.Length);
            foreach (var raw in name)
            {
                var c = char.ToLowerInvariant(raw);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) builder.Append(c);
                else if (c == ' ' || c == '-' || c == '_') builder.Append('_');
            }
            return builder.ToString().Trim('_');
        }

        private static bool ContainsAny(string haystack, string[] needles)
        {
            for (var i = 0; i < needles.Length; i++)
            {
                if (haystack.IndexOf(needles[i], StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        public static int ClampPercent(int value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }
    }
}

