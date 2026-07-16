using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;

namespace PdfEditor.Core
{
    // Wraps Docnet/PDFium to rasterize existing PDF page content. Docnet's
    // DocLib is a process-wide singleton, so we hold one instance for the app's
    // lifetime rather than creating it per render.
    public sealed class PdfRenderer : IDisposable
    {
        private static readonly object Gate = new object();
        private readonly IDocLib _lib = DocLib.Instance;

        // Renders one page to a 32bpp bitmap composited over white. The caller
        // derives the true pixels-per-point from the returned width, which keeps
        // coordinate math independent of Docnet's scaling convention.
        public Bitmap RenderPage(byte[] pdfBytes, int pageIndex, double scaling)
        {
            if (scaling < 0.02) scaling = 0.02;
            lock (Gate)
            {
                using var docReader = _lib.GetDocReader(pdfBytes, new PageDimensions(scaling));
                using var pageReader = docReader.GetPageReader(pageIndex);
                int w = pageReader.GetPageWidth();
                int h = pageReader.GetPageHeight();
                if (w <= 0) w = 1;
                if (h <= 0) h = 1;
                byte[] raw = pageReader.GetImage();
                return BuildBitmap(raw, w, h);
            }
        }

        public int GetPageCount(byte[] pdfBytes)
        {
            lock (Gate)
            {
                using var docReader = _lib.GetDocReader(pdfBytes, new PageDimensions(1));
                return docReader.GetPageCount();
            }
        }

        private static Bitmap BuildBitmap(byte[] bgra, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int rowBytes = w * 4;
                if (bgra != null && bgra.Length >= rowBytes * h)
                {
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(bgra, y * rowBytes, data.Scan0 + y * stride, rowBytes);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            // PDFium leaves uncovered areas transparent; flatten onto white so the
            // page reads as paper rather than showing the app background through it.
            var white = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(white))
            {
                g.Clear(Color.White);
                g.DrawImageUnscaled(bmp, 0, 0);
            }
            bmp.Dispose();
            return white;
        }

        public void Dispose()
        {
            // DocLib.Instance is a shared singleton; disposing it here would break
            // other renders, so we intentionally leave it for process teardown.
        }
    }
}
