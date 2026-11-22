using System;
using System.IO;
using System.Text;

namespace Watt.Services
{
    /// <summary>
    /// Best effort logging for the infrastructure services. Services must never take the
    /// app down because a diagnostic write failed, so every failure here is swallowed.
    /// Writes to the same watt-startup.log the App class uses so there is one log file.
    /// </summary>
    internal static class ServiceLog
    {
        private static readonly object Gate = new object();

        private static readonly string LogPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watt-startup.log");

        public static void Write(string message)
        {
            try
            {
                var line = DateTime.Now.ToString("HH:mm:ss.fff") + " [services] " + message + Environment.NewLine;
                lock (Gate)
                {
                    File.AppendAllText(LogPath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Diagnostics must never surface as an application failure.
            }
        }

        public static void Error(string context, Exception exception)
        {
            Write(context + " failed: " + exception.GetType().Name + ": " + exception.Message);
        }
    }
}
