using System;
using System.Collections.Generic;
using System.Drawing;

namespace PdfEditor.Core
{
    // Produces a finished raster of a page: the PDF content underneath and all
    // annotations painted on top with GDI+. Shared by the on-screen page view,
    // the thumbnail strip, and printing so they always look identical.
    public static class Compositor
    {
        public static Bitmap ComposePage(PdfRenderer renderer, DocumentModel model,
            int pageIndex, double requestedScaling, out float actualPpp)
        {
            Bitmap page = renderer.RenderPage(model.BaseBytes, pageIndex, requestedScaling);

            // Derive true pixels-per-point from what PDFium actually produced,
            // so annotation placement never depends on the renderer's scaling
            // convention.
            float pageWidthPt = model.PageSizesPt[pageIndex].Width;
            actualPpp = pageWidthPt > 0 ? page.Width / pageWidthPt : (float)requestedScaling;

            if (model.Annotations.TryGetValue(pageIndex, out var list) && list.Count > 0)
            {
                using var g = Graphics.FromImage(page);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                foreach (var ann in list)
                {
                    try { ann.DrawGdi(g, actualPpp); }
                    catch { /* never let one bad annotation break the whole page */ }
                }
            }
            return page;
        }
    }
}
