using Microsoft.Win32;

namespace Watt.Services
{
    /// <summary>
    /// Production <see cref="IRunKeyStore"/> backed by the current user's registry.
    /// All exceptions are allowed to bubble to <see cref="StartupService"/>, which is
    /// responsible for swallowing them.
    /// </summary>
    internal sealed class RegistryRunKeyStore : IRunKeyStore
    {
        public string ReadValue(string runKeyPath, string valueName)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(runKeyPath, false))
            {
                if (key == null) return null;
                return key.GetValue(valueName) as string;
            }
        }

        public bool WriteValue(string runKeyPath, string valueName, string value)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(runKeyPath))
            {
                if (key == null) return false;
                key.SetValue(valueName, value, RegistryValueKind.String);
                return true;
            }
        }

        public bool DeleteValue(string runKeyPath, string valueName)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(runKeyPath, true))
            {
                if (key == null) return true;
                key.DeleteValue(valueName, false);
                return true;
            }
        }
    }
}
