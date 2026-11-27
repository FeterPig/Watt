using System;

namespace Watt.Services
{
    /// <summary>
    /// Best effort logging for the infrastructure services. Services must never take the
    /// app down because a diagnostic write failed, so every failure here is swallowed.
    /// Writes through <see cref="LogFile"/> so there is one serialized log file.
    /// </summary>
    internal static class ServiceLog
    {
        public static void Write(string message)
        {
            LogFile.Write(DateTime.Now.ToString("HH:mm:ss.fff") + " [services] " + message);
        }

        public static void Error(string context, Exception exception)
        {
            Write(context + " failed: " + exception.GetType().Name + ": " + exception.Message);
        }
    }
}
