using System;
using System.IO;

namespace Watt.Plugins.Support
{
    /// <summary>
    /// Locations shared by the Watt host and the built-in plugins.
    ///
    /// Production always uses <c>%LOCALAPPDATA%\Watt</c>. The <c>WATT_DATA_DIR</c> environment
    /// variable is an explicit test/automation hook: it lets a diagnostic run or the automated
    /// test suite use a private cache directory instead of the signed-in user's real data. An
    /// invalid, relative or empty value is ignored so a bad variable can never break the app.
    /// </summary>
    public static class WattPaths
    {
        /// <summary>Environment variable that overrides the per-user Watt data directory.</summary>
        public const string DataDirectoryVariable = "WATT_DATA_DIR";

        private const string ProductFolderName = "Watt";

        /// <summary>
        /// Writable per-user data directory (cache, device preferences, plugin data).
        /// The directory is not created here; callers create it when they write.
        /// </summary>
        public static string DataDirectory
        {
            get
            {
                var configured = ReadOverride();
                if (configured != null) return configured;

                try
                {
                    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    if (!string.IsNullOrEmpty(local)) return Path.Combine(local, ProductFolderName);
                }
                catch
                {
                    // Fall through to a path relative to the executable so the app still runs.
                }

                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ProductFolderName);
            }
        }

        /// <summary>Full path of a file inside <see cref="DataDirectory"/>.</summary>
        public static string DataFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) throw new ArgumentNullException("fileName");
            return Path.Combine(DataDirectory, fileName);
        }

        private static string ReadOverride()
        {
            string raw;
            try
            {
                raw = Environment.GetEnvironmentVariable(DataDirectoryVariable);
            }
            catch
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                var value = raw.Trim();
                if (!Path.IsPathRooted(value)) return null;
                return Path.GetFullPath(value);
            }
            catch
            {
                return null;
            }
        }
    }
}
