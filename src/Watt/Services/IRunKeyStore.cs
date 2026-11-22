using System;
using System.Diagnostics;
using System.Reflection;

namespace Watt.Services
{
    /// <summary>
    /// Storage abstraction behind <see cref="StartupService"/>. The production
    /// implementation is <see cref="RegistryRunKeyStore"/> (HKCU Run key); tests can inject
    /// an in-memory implementation so the enable/read/disable/stale-path logic can be
    /// exercised without touching the real per-user Run key.
    /// </summary>
    public interface IRunKeyStore
    {
        /// <summary>Returns the string value, or null when the key/value is missing.</summary>
        string ReadValue(string runKeyPath, string valueName);

        /// <summary>Creates/overwrites the value. Returns true on success.</summary>
        bool WriteValue(string runKeyPath, string valueName, string value);

        /// <summary>Deletes the value. Returns true when it is gone afterwards.</summary>
        bool DeleteValue(string runKeyPath, string valueName);
    }
}
