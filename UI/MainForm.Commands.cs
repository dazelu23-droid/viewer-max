using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Windows.Forms;
using PdfEditor.Core;

namespace PdfEditor.UI
{
    // Every menu/toolbar command. Each wraps its core call so a failure surfaces a
    // message box instead of crashing the app.
    partial class MainForm
    {
        private void OnNew(object s, EventArgs e)
        {
            if (!ConfirmDiscard()) return;
            LoadModel(DocumentModel.NewBlank());
        }

        private void OnOpen(object s, EventArgs e)
        {
            if (!ConfirmDiscard()) return;
            using var dlg = new OpenFileDialog { Filter = "PDF|*.pdf|All files|*.*" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try { LoadModel(DocumentModel.Open(dlg.FileName)); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open file:\n" + ex.Message, "Open", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnSave(object s, EventArgs e)
        {
            if (_model == null) return;
            DoSave(string.IsNullOrEmpty(_model.FilePath) ? null : _model.FilePath);
        }

        private void OnSaveAs(object s, EventArgs e) => DoSave(null);

        // path == null => prompt with Save As dialog.
        internal void DoSave(string path)
        {
            if (_model == null) return;
            if (string.IsNullOrEmpty(path))
            {
                using var dlg = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = Path.GetFileName(_model.FilePath ?? "Untitled.pdf") };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }
            try
            {
                _model.SaveAs(path);
                UpdateTitle();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Save failed:\n" + ex.Message, "Save", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnImport(object s, EventArgs e)
        {
            if (_model == null) return;
            using var dlg = new OpenFileDialog
            {
                Filter = "Supported|*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|PDF|*.pdf|Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff",
                Multiselect = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            int start = _model.PageCount;
            try
            {
                foreach (var f in dlg.FileNames)
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".pdf")
                        _model.ReplaceBaseKeepingAnnotations(PdfIO.AppendPdf(_model.BaseBytes, f));
                    else
                        _model.ReplaceBaseKeepingAnnotations(PdfIO.AppendImagePage(_model.BaseBytes, f));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Import failed:\n" + ex.Message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            _pageView.GoToPage(start);
        }

        private void OnExport(object s, EventArgs e)
        {
            if (_model == null) return;
            using var dlg = new FolderBrowserDialog { Description = "Choose a folder to export page images into" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var (ok, fail) = ExportToFolder(dlg.SelectedPath);
            MessageBox.Show(this,
                "Exported " + ok + " page(s)" + (fail > 0 ? "; " + fail + " failed." : ".") + "\n" + dlg.SelectedPath,
                "Export", MessageBoxButtons.OK, fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        // Renders every page to a PNG in `folder`. Returns (ok, fail). Testable without a dialog.
        internal (int ok, int fail) ExportToFolder(string folder)
        {
            int ok = 0, fail = 0;
            string baseName = Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(_model.FilePath) ? "untitled" : _model.FilePath);
            for (int i = 0; i < _model.PageCount; i++)
            {
                try
                {
                    using var bmp = Compositor.ComposePage(_renderer, _model, i, 150.0 / 72.0, out _);
                    string pad = _model.PageCount > 1 ? "_" + i.ToString("D2") : "";
                    bmp.Save(Path.Combine(folder, baseName + pad + ".png"), ImageFormat.Png);
                    ok++;
                }
                catch { fail++; }
            }
            return (ok, fail);
        }

        // Composes every page at print resolution (150 DPI). Shared by Print and the GUI test.
        internal List<Bitmap> ComposePrintPages()
        {
            var pages = new List<Bitmap>(_model.PageCount);
            for (int i = 0; i < _model.PageCount; i++)
                pages.Add(Compositor.ComposePage(_renderer, _model, i, 150.0 / 72.0, out _));
            return pages;
        }

        private void OnPrint(object s, EventArgs e)
        {
            if (_model == null) return;

            List<Bitmap> pages;
            try { pages = ComposePrintPages(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Print preparation failed:\n" + ex.Message, "Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int index = 0;
            var pd = new PrintDocument();
            pd.DocumentName = Path.GetFileName(_model.FilePath ?? "document");
            pd.PrintPage += (sender, ev) =>
            {
                var bmp = pages[index];
                var mb = ev.MarginBounds;
                double scale = Math.Min((double)mb.Width / bmp.Width, (double)mb.Height / bmp.Height);
                int w = (int)(bmp.Width * scale), h = (int)(bmp.Height * scale);
                int x = mb.Left + (mb.Width - w) / 2, y = mb.Top + (mb.Height - h) / 2;
                ev.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                ev.Graphics.DrawImage(bmp, x, y, w, h);
                index++;
                ev.HasMorePages = index < pages.Count;
            };
            pd.EndPrint += (sender, ev) => { foreach (var b in pages) b.Dispose(); };

            try
            {
                using var dlg = new PrintDialog { UseEXDialog = true, Document = pd, AllowSomePages = false, AllowCurrentPage = false };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    pd.Print();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Print failed:\n" + ex.Message, "Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                foreach (var b in pages) b.Dispose();
            }
        }

        private void OnExit(object s, EventArgs e) => Close();

        private void OnUndo(object s, EventArgs e) => _model?.Undo.Undo();
        private void OnRedo(object s, EventArgs e) => _model?.Undo.Redo();
        private void OnDelete(object s, EventArgs e) => _pageView.DeleteSelected();

        private void OnInsertText(object s, EventArgs e) => SelectTool(ToolType.Text);

        private void OnInsertImage(object s, EventArgs e)
        {
            if (_model == null) return;
            using var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                _pageView.PendingImageBytes = File.ReadAllBytes(dlg.FileName);
                SelectTool(ToolType.PlaceImage);
                MessageBox.Show(this, "Click on the page to place the image.", "Place Image", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not load image:\n" + ex.Message, "Insert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal void OnInsertBlank(object s, EventArgs e)
        {
            if (_model == null) return;
            int at = _model.PageCount;
            try
            {
                _model.ReplaceBaseKeepingAnnotations(
                    PdfIO.AppendBlankPage(_model.BaseBytes, PdfIO.LetterWidthPt, PdfIO.LetterHeightPt));
                _pageView.GoToPage(at);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Insert page failed:\n" + ex.Message, "Insert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnAbout(object s, EventArgs e)
        {
            MessageBox.Show(this,
                "PDF Editor\n\n" +
                "A small WinForms PDF editor: annotate, draw, import, print, and export.\n" +
                "Built on PDFsharp + PDFium (Docnet).",
                "About PDF Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
