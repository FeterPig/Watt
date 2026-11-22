using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace IconGen
{
    internal sealed class IcoFrame
    {
        public int Size;
        public byte[] Bgra; // straight-alpha BGRA, Size * Size * 4 bytes

        public IcoFrame(int size, byte[] bgra)
        {
            Size = size;
            Bgra = bgra;
        }
    }

    /// <summary>
    /// Writes a classic Windows .ico container: 32 bpp BITMAPINFOHEADER frames for sizes &lt;= 64
    /// (with a correct AND mask for legacy consumers) and a PNG frame for 256x256.
    /// </summary>
    internal static class IcoWriter
    {
        public static void Write(string path, IList<IcoFrame> frames)
        {
            if (frames == null || frames.Count == 0)
            {
                throw new ArgumentException("No ICO frames supplied.", "frames");
            }

            var payloads = new List<byte[]>(frames.Count);
            foreach (IcoFrame f in frames)
            {
                if (f.Bgra.Length != f.Size * f.Size * 4)
                {
                    throw new ArgumentException("Frame " + f.Size + " has a wrong pixel buffer length.");
                }
                payloads.Add(f.Size >= 256 ? EncodePng(f.Size, f.Bgra) : EncodeBmp(f.Size, f.Bgra));
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (FileStream fs = File.Create(path))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((ushort)0);              // reserved
                bw.Write((ushort)1);              // type: icon
                bw.Write((ushort)frames.Count);

                int offset = 6 + 16 * frames.Count;
                for (int i = 0; i < frames.Count; i++)
                {
                    int s = frames[i].Size;
                    bw.Write((byte)(s >= 256 ? 0 : s)); // width (0 means 256)
                    bw.Write((byte)(s >= 256 ? 0 : s)); // height
                    bw.Write((byte)0);                  // color count
                    bw.Write((byte)0);                  // reserved
                    bw.Write((ushort)1);                // planes
                    bw.Write((ushort)32);               // bit count
                    bw.Write(payloads[i].Length);
                    bw.Write(offset);
                    offset += payloads[i].Length;
                }

                foreach (byte[] p in payloads)
                {
                    bw.Write(p);
                }
            }
        }

        private static byte[] EncodeBmp(int size, byte[] bgra)
        {
            int maskStride = ((size + 31) / 32) * 4;
            int pixelBytes = size * size * 4;
            int maskBytes = maskStride * size;

            using (var ms = new MemoryStream(40 + pixelBytes + maskBytes))
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(40);                    // biSize
                bw.Write(size);                  // biWidth
                bw.Write(size * 2);              // biHeight (XOR + AND)
                bw.Write((short)1);              // biPlanes
                bw.Write((short)32);             // biBitCount
                bw.Write(0);                     // biCompression BI_RGB
                bw.Write(pixelBytes + maskBytes);
                bw.Write(0);                     // biXPelsPerMeter
                bw.Write(0);                     // biYPelsPerMeter
                bw.Write(0);                     // biClrUsed
                bw.Write(0);                     // biClrImportant

                for (int y = size - 1; y >= 0; y--)
                {
                    bw.Write(bgra, y * size * 4, size * 4);
                }

                var row = new byte[maskStride];
                for (int y = size - 1; y >= 0; y--)
                {
                    Array.Clear(row, 0, row.Length);
                    for (int x = 0; x < size; x++)
                    {
                        if (bgra[(y * size + x) * 4 + 3] == 0)
                        {
                            row[x >> 3] |= (byte)(0x80 >> (x & 7));
                        }
                    }
                    bw.Write(row);
                }

                bw.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] EncodePng(int size, byte[] bgra)
        {
            BitmapSource bmp = Render.FromPixels(bgra, size, size);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var ms = new MemoryStream())
            {
                enc.Save(ms);
                return ms.ToArray();
            }
        }
    }
}
