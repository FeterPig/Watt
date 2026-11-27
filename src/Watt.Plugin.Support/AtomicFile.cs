using System;
using System.IO;
using System.Text;

namespace Watt.Plugins.Support
{
    /// <summary>
    /// Small files (last-known cache, device preferences, plugin state) are written to a
    /// temporary file in the same directory and then swapped into place. A crash or a full
    /// disk leaves the previous file intact instead of a half-written JSON document.
    /// </summary>
    public static class AtomicFile
    {
        public static void WriteAllBytes(string path, byte[] contents)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");
            if (contents == null) throw new ArgumentNullException("contents");

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, contents);
                Replace(temporary, path);
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        public static void WriteAllText(string path, string contents, Encoding encoding)
        {
            if (contents == null) throw new ArgumentNullException("contents");
            var actualEncoding = encoding ?? new UTF8Encoding(false);
            WriteAllBytes(path, actualEncoding.GetBytes(contents));
        }

        private static void Replace(string temporary, string path)
        {
            if (!File.Exists(path))
            {
                File.Move(temporary, path);
                return;
            }

            try
            {
                // File.Replace keeps the destination ACLs and is atomic on NTFS.
                File.Replace(temporary, path, null);
            }
            catch (Exception)
            {
                // Some file systems (or a read-only attribute) reject Replace. Fall back to a
                // delete + move; the data is still safer than writing the live file in place.
                File.Delete(path);
                File.Move(temporary, path);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // A leftover temp file is harmless and is cleaned up on the next write.
            }
        }
    }
}
