using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfEditor.Core
{
    // Structural PDF operations built on PdfSharp: create, inspect, flatten
    // annotations, and append pages. Rendering for the screen is handled
    // separately by PdfRenderer (PDFium); this side owns anything that writes.
    public static class PdfIO
    {
        public const double LetterWidthPt = 612;   // 8.5in
        public const double LetterHeightPt = 792;  // 11in

        public static byte[] CreateBlank(double wPt = LetterWidthPt, double hPt = LetterHeightPt)
        {
            using var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(wPt);
            page.Height = XUnit.FromPoint(hPt);
            using var ms = new MemoryStream();
            doc.Save(ms, false);
            return ms.ToArray();
        }

        public static List<SizeF> ReadPageSizes(byte[] bytes, out int count)
        {
            var sizes = new List<SizeF>();
            using var ms = new MemoryStream(bytes);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            count = doc.PageCount;
            foreach (var page in doc.Pages)
            {
                double w = page.Width.Point;
                double h = page.Height.Point;
                int rot = ((page.Rotate % 360) + 360) % 360;
                if (rot == 90 || rot == 270)
                    (w, h) = (h, w); // report the visual (post-rotation) size
                sizes.Add(new SizeF((float)w, (float)h));
            }
            return sizes;
        }

        // Writes a copy of the base PDF with every annotation drawn onto its page.
        public static void SaveFlattened(byte[] baseBytes,
            IReadOnlyDictionary<int, List<Annotation>> annotations, string outPath)
        {
            using var ms = new MemoryStream();
            ms.Write(baseBytes, 0, baseBytes.Length);
            ms.Position = 0;
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

            for (int i = 0; i < doc.PageCount; i++)
            {
                if (!annotations.TryGetValue(i, out var list) || list.Count == 0)
                    continue;
                var page = doc.Pages[i];
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                foreach (var ann in list)
                    ann.DrawPdf(gfx);
            }
            doc.Save(outPath);
        }

        public static byte[] SaveFlattenedToBytes(byte[] baseBytes,
            IReadOnlyDictionary<int, List<Annotation>> annotations)
        {
            using var ms = new MemoryStream();
            ms.Write(baseBytes, 0, baseBytes.Length);
            ms.Position = 0;
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
            for (int i = 0; i < doc.PageCount; i++)
            {
                if (!annotations.TryGetValue(i, out var list) || list.Count == 0)
                    continue;
                var page = doc.Pages[i];
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                foreach (var ann in list)
                    ann.DrawPdf(gfx);
            }
            using var outMs = new MemoryStream();
            doc.Save(outMs, false);
            return outMs.ToArray();
        }

        // Adds a new page sized to an image and draws the image full-bleed.
        public static byte[] AppendImagePage(byte[] baseBytes, string imagePath)
        {
            using var ms = new MemoryStream();
            ms.Write(baseBytes, 0, baseBytes.Length);
            ms.Position = 0;
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

            using (var img = Image.FromFile(imagePath))
            {
                // Map image pixels to points assuming 96 DPI screen images.
                double wPt = img.Width * 72.0 / 96.0;
                double hPt = img.Height * 72.0 / 96.0;
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(wPt);
                page.Height = XUnit.FromPoint(hPt);
                using var gfx = XGraphics.FromPdfPage(page);
                var ximg = XImage.FromFile(imagePath);
                gfx.DrawImage(ximg, 0, 0, wPt, hPt);
            }

            using var outMs = new MemoryStream();
            doc.Save(outMs, false);
            return outMs.ToArray();
        }

        // Appends all pages from another PDF onto the base document.
        public static byte[] AppendPdf(byte[] baseBytes, string otherPath)
        {
            using var ms = new MemoryStream();
            ms.Write(baseBytes, 0, baseBytes.Length);
            ms.Position = 0;
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
            using (var other = PdfReader.Open(otherPath, PdfDocumentOpenMode.Import))
            {
                for (int i = 0; i < other.PageCount; i++)
                    doc.AddPage(other.Pages[i]);
            }
            using var outMs = new MemoryStream();
            doc.Save(outMs, false);
            return outMs.ToArray();
        }

        // Appends a single blank page of the given size (points) to the base document.
        public static byte[] AppendBlankPage(byte[] baseBytes, double widthPt, double heightPt)
        {
            using var ms = new MemoryStream();
            ms.Write(baseBytes, 0, baseBytes.Length);
            ms.Position = 0;
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(widthPt);
            page.Height = XUnit.FromPoint(heightPt);
            using var outMs = new MemoryStream();
            doc.Save(outMs, false);
            return outMs.ToArray();
        }
    }
}
