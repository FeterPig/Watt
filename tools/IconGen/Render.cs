using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconGen
{
    /// <summary>
    /// Supersampled WPF rendering helpers. Everything is rendered with
    /// <see cref="RenderTargetBitmap"/> at <c>size * ss</c> pixels and then box-filtered
    /// down, which gives clean antialiasing for 16 px icons and exact pixel control.
    /// </summary>
    internal static class Render
    {
        public static readonly Typeface UiFace = new Typeface("Segoe UI");
        public static readonly Typeface UiSemiboldFace =
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        /// <summary>
        /// Draws <paramref name="draw"/> (in design units) into a bitmap of
        /// <paramref name="outW"/> x <paramref name="outH"/> logical pixels at <paramref name="ss"/>x supersampling.
        /// </summary>
        public static BitmapSource Draw(double outW, double outH, int ss, double designW, double designH, Action<DrawingContext> draw)
        {
            int pw = (int)Math.Round(outW * ss);
            int ph = (int)Math.Round(outH * ss);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform((double)pw / designW, (double)ph / designH));
                draw(dc);
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>Renders in a 1 DIP = 1 pixel space, no supersampling.</summary>
        public static BitmapSource DrawPixels(double w, double h, Action<DrawingContext> draw)
        {
            return Draw(w, h, 1, w, h, draw);
        }

        /// <summary>Renders a solid background then the supplied drawing on top.</summary>
        public static BitmapSource OnBackground(double w, double h, Color bg, Action<DrawingContext> draw)
        {
            return Draw(w, h, 1, w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, w, h));
                draw(dc);
            });
        }

        public static BitmapSource FromPixels(byte[] bgra, int w, int h)
        {
            BitmapSource bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
            bmp.Freeze();
            return bmp;
        }

        public static byte[] GetPixels(BitmapSource src)
        {
            int stride = src.PixelWidth * 4;
            var px = new byte[stride * src.PixelHeight];
            src.CopyPixels(px, stride, 0);
            return px;
        }

        /// <summary>Box-filters a <paramref name="ss"/>x supersampled Pbgra32 bitmap down to straight-alpha Bgra32.</summary>
        public static BitmapSource Downsample(BitmapSource src, int ss)
        {
            if (ss <= 1)
            {
                var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                conv.Freeze();
                return conv;
            }

            int w = src.PixelWidth / ss;
            int h = src.PixelHeight / ss;
            byte[] sp = GetPixels(src);
            int sstride = src.PixelWidth * 4;
            byte[] dp = new byte[w * h * 4];
            int n = ss * ss;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sa = 0, sb = 0, sg = 0, sr = 0;
                    for (int j = 0; j < ss; j++)
                    {
                        int off = (y * ss + j) * sstride + (x * ss) * 4;
                        for (int i = 0; i < ss; i++)
                        {
                            sb += sp[off];
                            sg += sp[off + 1];
                            sr += sp[off + 2];
                            sa += sp[off + 3];
                            off += 4;
                        }
                    }
                    if (sa == 0)
                    {
                        continue;
                    }
                    int di = (y * w + x) * 4;
                    // Premultiplied -> straight:  straight = sum(premul) / sum(alpha)
                    dp[di] = (byte)Math.Min(255, (sb * 255 + sa / 2) / sa);
                    dp[di + 1] = (byte)Math.Min(255, (sg * 255 + sa / 2) / sa);
                    dp[di + 2] = (byte)Math.Min(255, (sr * 255 + sa / 2) / sa);
                    dp[di + 3] = (byte)Math.Min(255, (sa + n / 2) / n);
                }
            }
            return FromPixels(dp, w, h);
        }

        /// <summary>Copies a bitmap with each pixel replicated <paramref name="f"/> times (pixel-art zoom).</summary>
        public static BitmapSource UpscaleNearest(BitmapSource src, int f)
        {
            int w = src.PixelWidth * f;
            int h = src.PixelHeight * f;
            byte[] sp = GetPixels(src);
            int sw = src.PixelWidth;
            byte[] dp = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int sy = y / f;
                for (int x = 0; x < w; x++)
                {
                    int si = (sy * sw + x / f) * 4;
                    int di = (y * w + x) * 4;
                    dp[di] = sp[si];
                    dp[di + 1] = sp[si + 1];
                    dp[di + 2] = sp[si + 2];
                    dp[di + 3] = sp[si + 3];
                }
            }
            return FromPixels(dp, w, h);
        }

        public static void SavePng(BitmapSource bmp, string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            BitmapSource src = bmp;
            if (bmp.Format != PixelFormats.Bgra32)
            {
                var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                conv.Freeze();
                src = conv;
            }
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using (FileStream fs = File.Create(path))
            {
                enc.Save(fs);
            }
        }

        public static FormattedText Label(string text, double size, Color color, bool semibold = false)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                semibold ? UiSemiboldFace : UiFace, size, new SolidColorBrush(color), 1.0);
        }

        public static void DrawLabel(DrawingContext dc, string text, double x, double y, double size, Color color,
            bool semibold = false, double maxWidth = 0, TextAlignment align = TextAlignment.Left)
        {
            var ft = Label(text, size, color, semibold);
            if (maxWidth > 0)
            {
                ft.MaxTextWidth = maxWidth;
                ft.TextAlignment = align;
            }
            dc.DrawText(ft, new Point(x, y));
        }

        public static Color Hex(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                hex = "FF" + hex;
            }
            uint v = Convert.ToUInt32(hex, 16);
            return Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        public static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush(Hex(hex));
            b.Freeze();
            return b;
        }
    }
}
