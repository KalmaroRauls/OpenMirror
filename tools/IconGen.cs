using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace OpenMirrorIconTool
{
    /// <summary>
    /// Draws the OpenMirror mark - a thick blue ring whose top-right segment has been lifted
    /// out and tinted dark, leaving a clean gap ("the open mirror") - and writes everything
    /// the app needs:
    ///
    ///   IconGen.exe assets\icon.ico assets\logo.png assets\wordmark.png
    ///
    /// The mark is pure geometry, so it can be regenerated at any size, and the small icon
    /// sizes are supersampled before downscaling so they stay crisp in the taskbar.
    /// </summary>
    internal static class IconGen
    {
        private static readonly Color Ring = Color.FromArgb(255, 88, 170, 246);
        // The lifted piece is dark, but not so dark that it vanishes into a dark taskbar.
        // Change this one value (and re-run MakeIcon.ps1) to tune it.
        private static readonly Color Lift = Color.FromArgb(255, 54, 60, 70);

        /// <summary>Every size Windows asks for, so no scaling is left to the shell.</summary>
        private static readonly int[] IconSizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

        private static int Main(string[] args)
        {
            string icoPath = args.Length > 0 ? args[0] : "icon.ico";
            string logoPath = args.Length > 1 ? args[1] : "logo.png";
            string wordPath = args.Length > 2 ? args[2] : "";
            string previewPath = args.Length > 3 ? args[3] : "";

            List<int> sizes = new List<int>();
            List<byte[]> blobs = new List<byte[]>();
            for (int i = 0; i < IconSizes.Length; i++)
            {
                using (Bitmap bmp = RenderScaled(IconSizes[i]))
                {
                    sizes.Add(IconSizes[i]);
                    // Everything up to 128 px travels as a classic bitmap, because
                    // System.Drawing's Icon class (what the app itself uses) cannot read PNG
                    // entries. Only the 256 px frame is PNG - the usual convention, and it
                    // keeps the file small.
                    blobs.Add(IconSizes[i] >= 256 ? ToPng(bmp) : ToDib(bmp));
                }
            }
            WriteIco(icoPath, sizes, blobs);
            Console.WriteLine("icon    : " + Path.GetFullPath(icoPath) + "  (" + sizes.Count + " sizes)");

            using (Bitmap logo = RenderScaled(512)) { logo.Save(logoPath, ImageFormat.Png); }
            Console.WriteLine("logo    : " + Path.GetFullPath(logoPath));

            if (wordPath.Length > 0)
            {
                WriteWordmark(wordPath);
                Console.WriteLine("wordmark: " + Path.GetFullPath(wordPath));
            }

            if (previewPath.Length > 0)
            {
                WritePreview(previewPath);
                Console.WriteLine("preview : " + Path.GetFullPath(previewPath));
            }
            return 0;
        }

        /// <summary>Rows of the mark, drawn on a transparent square.</summary>
        private static Bitmap Render(int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float s = size;
                float cx = s * 0.5f;
                float cy = s * 0.5f;
                float outer = s * 0.44f;         // blue ring, outer radius
                float hole = outer * 0.36f;      // ring is a good two thirds of the radius thick
                // A hairline gap is invisible at 16 px, so the tiny sizes get a slightly
                // wider one - but not so wide that the ring starts to look chopped.
                float gapRatio = size <= 20 ? 0.10f : (size <= 32 ? 0.075f : 0.055f);
                float gap = outer * gapRatio;
                float reach = outer * 1.04f;     // the flap sits a touch proud of the ring

                using (Brush ringBrush = new SolidBrush(Ring))
                using (Brush liftBrush = new SolidBrush(Lift))
                {
                    // Angles are GDI+: 0 deg = 3 o'clock and positive sweeps clockwise, so
                    // this is the ring from 3 o'clock round through 6 and 9 back up to 12.
                    using (GraphicsPath ring = Wedge(cx, cy, outer, hole, 0f, 270f))
                    {
                        g.FillPath(ringBrush, ring);
                    }

                    // The lifted piece: where the ring's top-right fragment was, a rounded
                    // square has swung outward - that is the "open" in OpenMirror.
                    using (GraphicsPath flap = RoundedRect(cx + gap, cy - reach,
                                                           reach - gap, reach - gap, outer * 0.21f))
                    {
                        g.FillPath(liftBrush, flap);
                    }

                    // Punch the hole back out with a hairline of clearance. SourceCopy makes
                    // those pixels genuinely transparent instead of painting white on them.
                    float erase = hole + gap;
                    using (GraphicsPath mirror = new GraphicsPath())
                    {
                        mirror.AddEllipse(cx - erase, cy - erase, erase * 2f, erase * 2f);
                        using (Brush clear = new SolidBrush(Color.Transparent))
                        {
                            g.CompositingMode = CompositingMode.SourceCopy;
                            g.FillPath(clear, mirror);
                            g.CompositingMode = CompositingMode.SourceOver;
                        }
                    }
                }
            }
            return bmp;
        }

        /// <summary>A ring sector: outer arc forward, inner arc back, closed.</summary>
        private static GraphicsPath Wedge(float cx, float cy, float outer, float inner, float startDeg, float sweepDeg)
        {
            GraphicsPath path = new GraphicsPath();
            RectangleF outerBox = new RectangleF(cx - outer, cy - outer, outer * 2f, outer * 2f);
            RectangleF innerBox = new RectangleF(cx - inner, cy - inner, inner * 2f, inner * 2f);
            path.AddArc(outerBox, startDeg, sweepDeg);
            path.AddArc(innerBox, startDeg + sweepDeg, -sweepDeg);
            path.CloseFigure();
            return path;
        }

        /// <summary>A rectangle with all four corners rounded.</summary>
        private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            GraphicsPath path = new GraphicsPath();
            float d = r * 2f;
            path.AddArc(x, y, d, d, 180f, 90f);                       // top left
            path.AddArc(x + w - d, y, d, d, 270f, 90f);               // top right
            path.AddArc(x + w - d, y + h - d, d, d, 0f, 90f);         // bottom right
            path.AddArc(x, y + h - d, d, d, 90f, 90f);                // bottom left
            path.CloseFigure();
            return path;
        }

        /// <summary>Draws big, then averages down: small sizes keep clean edges.</summary>
        private static Bitmap RenderScaled(int size)
        {
            int factor = size <= 24 ? 8 : (size <= 48 ? 6 : (size <= 96 ? 4 : 2));
            using (Bitmap big = Render(size * factor))
            {
                Bitmap small = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.Clear(Color.Transparent);
                    g.DrawImage(big, new Rectangle(0, 0, size, size));
                }
                return small;
            }
        }

        /// <summary>32bpp bottom-up bitmap + AND mask, the classic .ico entry.</summary>
        private static byte[] ToDib(Bitmap bmp)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            int stride = w * 4;
            int maskStride = ((w + 31) / 32) * 4;
            int maskBytes = maskStride * h;
            byte[] data = new byte[40 + stride * h + maskBytes];

            PutInt(data, 0, 40);                 // BITMAPINFOHEADER
            PutInt(data, 4, w);
            PutInt(data, 8, h * 2);              // XOR + AND mask are stacked
            PutShort(data, 12, 1);               // planes
            PutShort(data, 14, 32);              // bits per pixel
            PutInt(data, 16, 0);                 // BI_RGB
            PutInt(data, 20, stride * h + maskBytes);

            for (int y = 0; y < h; y++)
            {
                int srcY = h - 1 - y;            // bitmaps are written bottom-up
                int row = 40 + y * stride;
                int maskRow = 40 + stride * h + y * maskStride;
                for (int x = 0; x < w; x++)
                {
                    Color c = bmp.GetPixel(x, srcY);
                    int o = row + x * 4;
                    data[o] = c.B;
                    data[o + 1] = c.G;
                    data[o + 2] = c.R;
                    data[o + 3] = c.A;
                    if (c.A == 0) { data[maskRow + (x >> 3)] |= (byte)(0x80 >> (x & 7)); }
                }
            }
            return data;
        }

        private static byte[] ToPng(Bitmap bmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private static void WriteIco(string path, List<int> sizes, List<byte[]> blobs)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (dir.Length > 0 && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((ushort)0);                          // reserved
                w.Write((ushort)1);                          // 1 = icon
                w.Write((ushort)sizes.Count);
                int offset = 6 + 16 * sizes.Count;
                for (int i = 0; i < sizes.Count; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);   // 0 stands for 256
                    w.Write(dim);
                    w.Write(dim);
                    w.Write((byte)0);                        // colours in palette
                    w.Write((byte)0);                        // reserved
                    w.Write((ushort)1);                      // planes
                    w.Write((ushort)32);                     // bits per pixel
                    w.Write(blobs[i].Length);
                    w.Write(offset);
                    offset += blobs[i].Length;
                }
                for (int i = 0; i < blobs.Count; i++) { w.Write(blobs[i]); }
            }
        }

        /// <summary>Mark plus the wordmark, for the README header.</summary>
        private static void WriteWordmark(string path)
        {
            const float markSize = 256f;
            const float margin = 40f;
            const float spacing = 30f;
            const float fontSize = 104f;

            using (Font heavy = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font light = new Font("Segoe UI", fontSize, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                float open, mirror, textHeight;
                using (Bitmap probe = new Bitmap(1, 1))
                using (Graphics pg = Graphics.FromImage(probe))
                {
                    // GenericTypographic measures the glyphs themselves: MeasureString's
                    // default padding would leave a phantom space in the middle of the name.
                    SizeF first = pg.MeasureString("Open", heavy, new PointF(0f, 0f), StringFormat.GenericTypographic);
                    SizeF second = pg.MeasureString("Mirror", light, new PointF(0f, 0f), StringFormat.GenericTypographic);
                    open = first.Width - fontSize * 0.02f;      // a hair of kern
                    mirror = second.Width;
                    textHeight = Math.Max(first.Height, second.Height);
                }

                int width = (int)Math.Ceiling(margin * 2f + markSize + spacing + open + mirror);
                int height = (int)Math.Ceiling(margin * 2f + Math.Max(markSize, textHeight));
                using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    using (Bitmap mark = RenderScaled(256))
                    {
                        g.DrawImage(mark, new RectangleF(margin, (height - markSize) / 2f, markSize, markSize));
                    }
                    using (Brush ink = new SolidBrush(Lift))
                    {
                        float top = (height - textHeight) / 2f + textHeight * 0.06f;
                        float x = margin + markSize + spacing;
                        g.DrawString("Open", heavy, ink, x, top, StringFormat.GenericTypographic);
                        g.DrawString("Mirror", light, ink, x + open, top, StringFormat.GenericTypographic);
                    }
                    bmp.Save(path, ImageFormat.Png);
                }
            }
        }

        /// <summary>
        /// Every icon size strip on a light and a dark card: how the mark survives at
        /// 16 px is the thing most worth eyeballing.
        /// </summary>
        private static void WritePreview(string path)
        {
            int[] strip = new int[] { 16, 20, 24, 32, 48, 64, 128, 256 };
            const int cellHeight = 288;
            int width = 16;
            for (int i = 0; i < strip.Length; i++) { width += Math.Max(strip[i], 48) + 16; }
            int height = cellHeight * 2 + 16;

            using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Brush light = new SolidBrush(Color.FromArgb(255, 243, 243, 243)))
                using (Brush dark = new SolidBrush(Color.FromArgb(255, 32, 32, 32)))
                {
                    g.FillRectangle(light, new Rectangle(0, 0, width, cellHeight));
                    g.FillRectangle(dark, new Rectangle(0, cellHeight, width, cellHeight));
                }

                int x = 16;
                for (int i = 0; i < strip.Length; i++)
                {
                    int cell = Math.Max(strip[i], 48);
                    using (Bitmap icon = RenderScaled(strip[i]))
                    {
                        int top = 8 + (cellHeight - 16 - strip[i]) / 2;
                        g.DrawImage(icon, new Rectangle(x + (cell - strip[i]) / 2, top, strip[i], strip[i]));
                        g.DrawImage(icon, new Rectangle(x + (cell - strip[i]) / 2, cellHeight + top, strip[i], strip[i]));
                    }
                    x += cell + 16;
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private static void PutInt(byte[] data, int at, int value)
        {
            data[at] = (byte)(value & 0xFF);
            data[at + 1] = (byte)((value >> 8) & 0xFF);
            data[at + 2] = (byte)((value >> 16) & 0xFF);
            data[at + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void PutShort(byte[] data, int at, int value)
        {
            data[at] = (byte)(value & 0xFF);
            data[at + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
}
