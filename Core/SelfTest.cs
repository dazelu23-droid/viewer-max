using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace PdfEditor.Core
{
    // Headless end-to-end check of the editor's core so functionality can be
    // verified in an environment with no display. Run with `PdfEditor --selftest`.
    public static class SelfTest
    {
        private static int _fail;

        public static int Run()
        {
            _fail = 0;
            string tmp = Path.Combine(Path.GetTempPath(), "pdfeditor_selftest");
            Directory.CreateDirectory(tmp);

            WinFontResolver.Register();
            using var renderer = new PdfRenderer();

            // 1. New blank document
            var model = DocumentModel.NewBlank();
            Assert(model.PageCount == 1, "new doc has 1 page");
            Assert(Math.Abs(model.PageSizesPt[0].Width - 612) < 1, "letter width 612pt");
            Assert(Math.Abs(model.PageSizesPt[0].Height - 792) < 1, "letter height 792pt");

            // 2. Render base page
            var baseBmp = renderer.RenderPage(model.BaseBytes, 0, 1.0);
            Assert(baseBmp != null && baseBmp.Width > 100, "render produced a bitmap");
            Console.WriteLine($"   [info] page0 rendered {baseBmp.Width}x{baseBmp.Height}px at scaling 1.0");
            baseBmp.Dispose();

            // 3. Add one of every annotation type
            model.AddAnnotation(0, new TextAnnotation
            {
                PositionPt = new PointF(72, 72),
                Text = "Hello PDF Editor",
                FontFamily = "Arial",
                FontSizePt = 24,
                Color = Color.Black,
                Bold = true
            });
            model.AddAnnotation(0, new InkAnnotation
            {
                Color = Color.Red,
                WidthPt = 3,
                PointsPt = { new PointF(72, 150), new PointF(200, 180), new PointF(320, 140) }
            });
            model.AddAnnotation(0, new InkAnnotation
            {
                Color = Color.Yellow,
                WidthPt = 12,
                Highlighter = true,
                PointsPt = { new PointF(72, 230), new PointF(300, 230) }
            });
            model.AddAnnotation(0, new ShapeAnnotation
            {
                Kind = ShapeKind.Rectangle,
                StartPt = new PointF(72, 300),
                EndPt = new PointF(260, 400),
                StrokeColor = Color.Blue,
                WidthPt = 2
            });
            model.AddAnnotation(0, new ShapeAnnotation
            {
                Kind = ShapeKind.Arrow,
                StartPt = new PointF(300, 320),
                EndPt = new PointF(460, 420),
                StrokeColor = Color.Green,
                WidthPt = 3
            });

            byte[] pngBytes = MakeTestPng(120, 80, Color.MediumPurple);
            model.AddAnnotation(0, new ImageAnnotation
            {
                ImageBytes = pngBytes,
                RectPt = new RectangleF(320, 200, 120, 80)
            });
            Assert(model.GetPageAnnotations(0).Count == 6, "6 annotations added");

            // 4. Compose page and confirm annotations actually painted (non-white pixels)
            var composed = Compositor.ComposePage(renderer, model, 0, 1.0, out float ppp);
            Assert(ppp > 0.5, "derived pixels-per-point is sane");
            Assert(HasColoredPixels(composed), "composed page has non-white content");
            composed.Save(Path.Combine(tmp, "composed_page0.png"), ImageFormat.Png);
            composed.Dispose();

            // 5. Flatten to a real PDF and confirm it grew vs. the blank base
            string savedPath = Path.Combine(tmp, "saved.pdf");
            model.SaveAs(savedPath);
            Assert(File.Exists(savedPath), "saved.pdf written");
            Assert(!model.IsDirty, "dirty flag cleared after save");
            long savedLen = new FileInfo(savedPath).Length;
            Assert(savedLen > model.BaseBytes.Length, "flattened file larger than blank base");
            Console.WriteLine($"   [info] saved.pdf = {savedLen} bytes (base {model.BaseBytes.Length})");

            // 6. Reopen the saved PDF
            var reopened = DocumentModel.Open(savedPath);
            Assert(reopened.PageCount == 1, "reopened has 1 page");
            var reBmp = renderer.RenderPage(reopened.BaseBytes, 0, 1.0);
            Assert(HasColoredPixels(reBmp), "reopened page shows flattened content");
            reBmp.Dispose();

            // 7. Import an image as a new page
            string imgPath = Path.Combine(tmp, "import.png");
            File.WriteAllBytes(imgPath, MakeTestPng(300, 200, Color.Teal));
            model.ReplaceBaseKeepingAnnotations(PdfIO.AppendImagePage(model.BaseBytes, imgPath));
            Assert(model.PageCount == 2, "import image added a page");
            Assert(model.GetPageAnnotations(0).Count == 6, "page 0 annotations preserved after import");

            // 8. Import (append) another PDF's pages
            model.ReplaceBaseKeepingAnnotations(PdfIO.AppendPdf(model.BaseBytes, savedPath));
            Assert(model.PageCount == 3, "append pdf added a page");

            // 9. Undo / redo
            int before = model.GetPageAnnotations(0).Count;
            model.Undo.Undo();
            Assert(model.GetPageAnnotations(0).Count == before - 1, "undo removed last annotation");
            model.Undo.Redo();
            Assert(model.GetPageAnnotations(0).Count == before, "redo restored annotation");

            // 10. Erase (object eraser) removes a hit annotation
            var hit = new System.Collections.Generic.List<Annotation>();
            foreach (var a in model.GetPageAnnotations(0))
                if (a.HitTest(new PointF(72, 72), 6)) { hit.Add(a); break; }
            Assert(hit.Count == 1, "hit-test found the text annotation");
            model.RemoveAnnotations(0, hit);
            Assert(model.GetPageAnnotations(0).Count == before - 1, "erase removed one annotation");

            // 11. Append a blank page (Insert > Blank Page)
            int beforeBlank = model.PageCount;
            model.ReplaceBaseKeepingAnnotations(
                PdfIO.AppendBlankPage(model.BaseBytes, PdfIO.LetterWidthPt, PdfIO.LetterHeightPt));
            Assert(model.PageCount == beforeBlank + 1, "append blank page added one page");
            var blankSizes = PdfIO.ReadPageSizes(model.BaseBytes, out _);
            Assert(Math.Abs(blankSizes[blankSizes.Count - 1].Width - 612) < 1
                && Math.Abs(blankSizes[blankSizes.Count - 1].Height - 792) < 1,
                "blank page is letter-sized");

            // 12. Print-compose at higher resolution
            var printBmp = Compositor.ComposePage(renderer, model, 0, 150.0 / 72.0, out _);
            Assert(printBmp.Width > composedWidthGuard(), "print raster is higher-res");
            printBmp.Dispose();

            Console.WriteLine();
            Console.WriteLine(_fail == 0
                ? "SELFTEST RESULT: ALL PASSED"
                : $"SELFTEST RESULT: {_fail} FAILURE(S)");
            Console.WriteLine("Artifacts in: " + tmp);
            return _fail == 0 ? 0 : 1;
        }

        private static int composedWidthGuard() => 700; // > letter width at ppp 1

        private static void Assert(bool cond, string label)
        {
            Console.WriteLine((cond ? "  [PASS] " : "  [FAIL] ") + label);
            if (!cond) _fail++;
        }

        private static byte[] MakeTestPng(int w, int h, Color fill)
        {
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(fill);
                using var pen = new Pen(Color.White, 4);
                g.DrawRectangle(pen, 2, 2, w - 5, h - 5);
            }
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        private static bool HasColoredPixels(Bitmap bmp)
        {
            // Sample a grid looking for any pixel that isn't near-white.
            for (int y = 0; y < bmp.Height; y += 5)
                for (int x = 0; x < bmp.Width; x += 5)
                {
                    var c = bmp.GetPixel(x, y);
                    if (c.R < 240 || c.G < 240 || c.B < 240)
                        return true;
                }
            return false;
        }
    }
}
