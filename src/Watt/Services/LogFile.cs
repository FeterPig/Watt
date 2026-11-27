using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Watt.Services
{
    /// <summary>
    /// The one writer for <c>watt-startup.log</c>. The WPF shell, the infrastructure services
    /// and plugin loggers can all log at the same time, and a diagnostic process may run while
    /// the tray app is writing, so appends are serialized with a per-session named mutex.
    /// The file is rotated once at 1 MB to keep a long-running tray app from growing it without
    /// bound; the previous file is kept as <c>watt-startup.log.1</c>.
    ///
    /// Logging is strictly best effort: a read-only install directory or a locked file must
    /// never turn a battery scan into an application failure.
    /// </summary>
    internal static class LogFile
    {
        private const long MaxBytes = 1024 * 1024;
        private const int MutexTimeoutMilliseconds = 500;
        private const string MutexName = @"Local\Watt.LogFile";

        private static readonly object Gate = new object();
        private static Mutex _mutex;
        private static bool _mutexUnavailable;

        internal static string Path
        {
            get
            {
                return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watt-startup.log");
            }
        }

        internal static void Write(string line)
        {
            if (line == null) return;

            try
            {
                var mutex = AcquireMutex();
                var mutexHeld = false;
                try
                {
                    if (mutex != null)
                    {
                        try
                        {
                            mutexHeld = mutex.WaitOne(MutexTimeoutMilliseconds);
                        }
                        catch (AbandonedMutexException)
                        {
                            mutexHeld = true;
                        }
                        catch
                        {
                            mutexHeld = false;
                        }
                    }

                    lock (Gate)
                    {
                        RotateIfNeeded();
                        File.AppendAllText(Path, line + Environment.NewLine, new UTF8Encoding(false));
                    }
                }
                finally
                {
                    if (mutexHeld)
                    {
                        try { mutex.ReleaseMutex(); } catch { }
                    }
                }
            }
            catch
            {
                // Diagnostics must never surface as an application failure.
            }
        }

        private static Mutex AcquireMutex()
        {
            if (Volatile.Read(ref _mutexUnavailable)) return null;

            lock (Gate)
            {
                if (_mutex != null || _mutexUnavailable) return _mutex;
                try
                {
                    _mutex = new Mutex(false, MutexName);
                    return _mutex;
                }
                catch
                {
                    // A named mutex can be unavailable in unusual sandboxes; local locking still
                    // protects this process, so keep logging.
                    _mutexUnavailable = true;
                    return null;
                }
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                var info = new FileInfo(Path);
                if (!info.Exists || info.Length <= MaxBytes) return;

                var previous = Path + ".1";
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(Path, previous);
            }
            catch
            {
                // Rotation is maintenance only; keep appending to the current file.
            }
        }
    }
}
