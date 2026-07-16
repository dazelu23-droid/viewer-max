using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PdfEditor.Core;

namespace PdfEditor.UI
{
    // Headless verification of the MainForm shell and its command wiring. Run with
    // `PdfEditor --guitest`. It constructs the real form (without showing it) and
    // drives the actual command handlers / tool selection so wiring bugs surface
    // without a human at the GUI.
    public static class GuiTest
    {
        private static int _fail;

        public static int Run()
        {
            _fail = 0;
            string tmp = Path.Combine(Path.GetTempPath(), "pdfeditor_guitest");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            MainForm form;
            try
            {
                form = new MainForm(new string[0]);
            }
            catch (Exception ex)
            {
                Console.WriteLine("GUITEST CRASHED constructing MainForm: " + ex);
                return 2;
            }

            // 1. Structural: chrome was built.
            Assert(form.MenuCount == 6, "menu bar has 6 top menus");
            Assert(form.ToolButtonCount == 10, "toolbar has 10 tool buttons");
            Assert(form.HasPropertiesPanel, "properties panel controls exist");

            // 2. Initial document is a single blank page.
            Assert(form.TestModel != null && form.TestModel.PageCount == 1, "starts on a 1-page blank doc");

            // 3. Tool selection routes through to the canvas.
            form.SelectTool(ToolType.Pen);
            Assert(form.TestView.Tool == ToolType.Pen, "SelectTool(Pen) sets the canvas tool");
            form.SelectTool(ToolType.Select);

            // 4. Add one of each annotation via the model, then export → PNGs.
            var m = form.TestModel;
            m.AddAnnotation(0, new TextAnnotation { PositionPt = new PointF(72, 72), Text = "GUI test", FontSizePt = 18, Color = Color.Black });
            m.AddAnnotation(0, new InkAnnotation { Color = Color.Red, WidthPt = 3, PointsPt = { new PointF(72, 150), new PointF(300, 200) } });
            m.AddAnnotation(0, new ShapeAnnotation { Kind = ShapeKind.Rectangle, StartPt = new PointF(72, 300), EndPt = new PointF(260, 400), StrokeColor = Color.Blue, WidthPt = 2 });

            var (ok, fail) = form.ExportToFolder(tmp);
            Assert(ok == 1 && fail == 0, "export wrote 1 PNG, none failed");
            string png = Path.Combine(tmp, "untitled.png");
            Assert(File.Exists(png) && new FileInfo(png).Length > 1000, "exported PNG exists and is non-trivial");

            // 5. Print-compose path produces one bitmap per page.
            var pages = form.ComposePrintPages();
            try
            {
                Assert(pages.Count == m.PageCount, "print-compose produced one bitmap per page");
                Assert(pages[0].Width > 700, "print raster is higher-res than screen");
            }
            finally { foreach (var b in pages) b.Dispose(); }

            // 6. Insert > Blank Page grows the document and export covers all pages.
            int before = m.PageCount;
            form.OnInsertBlank(null, EventArgs.Empty);
            Assert(m.PageCount == before + 1, "insert blank page added a page");
            var (ok2, _) = form.ExportToFolder(tmp);
            Assert(ok2 == m.PageCount, "export covers all pages after insert");

            // 7. Undo/redo through the model (what the menu handlers invoke).
            int annBefore = m.GetPageAnnotations(0).Count;
            m.Undo.Undo();
            Assert(m.GetPageAnnotations(0).Count == annBefore - 1, "undo removed the last annotation");
            m.Undo.Redo();
            Assert(m.GetPageAnnotations(0).Count == annBefore, "redo restored the annotation");

            form.Dispose();

            Console.WriteLine();
            Console.WriteLine(_fail == 0
                ? "GUITEST RESULT: ALL PASSED"
                : "GUITEST RESULT: " + _fail + " FAILURE(S)");
            Console.WriteLine("Artifacts in: " + tmp);
            return _fail == 0 ? 0 : 1;
        }

        private static void Assert(bool cond, string label)
        {
            Console.WriteLine((cond ? "  [PASS] " : "  [FAIL] ") + label);
            if (!cond) _fail++;
        }
    }
}
