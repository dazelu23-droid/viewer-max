using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using PdfSharp.Drawing;

namespace PdfEditor.Core
{
    // All annotation geometry is stored in PDF points (1/72 inch) with a
    // top-left origin and y increasing downward. That matches both GDI+ (when
    // scaled by pixels-per-point) and PdfSharp's XGraphics default space, so the
    // same numbers drive the on-screen preview and the flattened PDF output.
    public abstract class Annotation
    {
        public abstract void DrawGdi(Graphics g, float ppp);
        public abstract void DrawPdf(XGraphics gfx);
        public abstract bool HitTest(PointF ptPt, float tolPt);
        public abstract void Translate(float dxPt, float dyPt);
        public abstract RectangleF BoundsPt();

        protected static XColor ToXColor(Color c) => XColor.FromArgb(c.A, c.R, c.G, c.B);

        protected static bool SegmentHit(PointF a, PointF b, PointF p, float tol)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len2 = dx * dx + dy * dy;
            float t = len2 <= 0.0001f ? 0f : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = Math.Max(0f, Math.Min(1f, t));
            float cx = a.X + t * dx, cy = a.Y + t * dy;
            float ex = p.X - cx, ey = p.Y - cy;
            return ex * ex + ey * ey <= tol * tol;
        }
    }

    public sealed class TextAnnotation : Annotation
    {
        public PointF PositionPt;      // top-left of the text box
        public string Text = "";
        public string FontFamily = "Arial";
        public double FontSizePt = 14;
        public Color Color = Color.Black;
        public bool Bold;
        public bool Italic;

        private FontStyle GdiStyle =>
            (Bold ? FontStyle.Bold : 0) | (Italic ? FontStyle.Italic : 0);

        public override void DrawGdi(Graphics g, float ppp)
        {
            using var font = new Font(FontFamily, (float)(FontSizePt * ppp), GdiStyle, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color);
            g.DrawString(Text, font, brush, PositionPt.X * ppp, PositionPt.Y * ppp);
        }

        public override void DrawPdf(XGraphics gfx)
        {
            var style = (Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular)
                      | (Italic ? XFontStyleEx.Italic : XFontStyleEx.Regular);
            var font = new XFont(FontFamily, FontSizePt, style);
            var brush = new XSolidBrush(ToXColor(Color));
            // GDI DrawString at a point places the top of the text there; align
            // PdfSharp the same way so preview and output line up.
            gfx.DrawString(Text ?? "", font, brush,
                new XRect(PositionPt.X, PositionPt.Y, 10000, font.Height),
                XStringFormats.TopLeft);
        }

        public override bool HitTest(PointF p, float tol)
        {
            var b = BoundsPt();
            b.Inflate(tol, tol);
            return b.Contains(p);
        }

        public override void Translate(float dx, float dy)
            => PositionPt = new PointF(PositionPt.X + dx, PositionPt.Y + dy);

        public override RectangleF BoundsPt()
        {
            float w = Math.Max(20f, (Text?.Length ?? 1) * (float)FontSizePt * 0.55f);
            float h = (float)FontSizePt * 1.3f;
            return new RectangleF(PositionPt.X, PositionPt.Y, w, h);
        }
    }

    public sealed class InkAnnotation : Annotation
    {
        public List<PointF> PointsPt = new List<PointF>();
        public Color Color = Color.Red;
        public double WidthPt = 2;
        public bool Highlighter;

        private Color EffectiveColor =>
            Highlighter ? Color.FromArgb(110, Color) : Color;

        public override void DrawGdi(Graphics g, float ppp)
        {
            if (PointsPt.Count == 0) return;
            using var pen = new Pen(EffectiveColor, (float)(WidthPt * ppp))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            if (PointsPt.Count == 1)
            {
                float r = (float)(WidthPt * ppp) / 2f;
                using var b = new SolidBrush(EffectiveColor);
                g.FillEllipse(b, PointsPt[0].X * ppp - r, PointsPt[0].Y * ppp - r, r * 2, r * 2);
                return;
            }
            var pts = new PointF[PointsPt.Count];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new PointF(PointsPt[i].X * ppp, PointsPt[i].Y * ppp);
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawLines(pen, pts);
            g.SmoothingMode = old;
        }

        public override void DrawPdf(XGraphics gfx)
        {
            if (PointsPt.Count == 0) return;
            var pen = new XPen(ToXColor(EffectiveColor), WidthPt)
            {
                LineCap = XLineCap.Round,
                LineJoin = XLineJoin.Round
            };
            if (PointsPt.Count == 1)
            {
                double r = WidthPt / 2.0;
                gfx.DrawEllipse(new XSolidBrush(ToXColor(EffectiveColor)),
                    PointsPt[0].X - r, PointsPt[0].Y - r, r * 2, r * 2);
                return;
            }
            var pts = new XPoint[PointsPt.Count];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new XPoint(PointsPt[i].X, PointsPt[i].Y);
            gfx.DrawLines(pen, pts);
        }

        public override bool HitTest(PointF p, float tol)
        {
            float t = tol + (float)WidthPt / 2f;
            if (PointsPt.Count == 1)
            {
                float ex = p.X - PointsPt[0].X, ey = p.Y - PointsPt[0].Y;
                return ex * ex + ey * ey <= t * t;
            }
            for (int i = 1; i < PointsPt.Count; i++)
                if (SegmentHit(PointsPt[i - 1], PointsPt[i], p, t)) return true;
            return false;
        }

        public override void Translate(float dx, float dy)
        {
            for (int i = 0; i < PointsPt.Count; i++)
                PointsPt[i] = new PointF(PointsPt[i].X + dx, PointsPt[i].Y + dy);
        }

        public override RectangleF BoundsPt()
        {
            if (PointsPt.Count == 0) return RectangleF.Empty;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in PointsPt)
            {
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
            }
            float pad = (float)WidthPt / 2f;
            return RectangleF.FromLTRB(minX - pad, minY - pad, maxX + pad, maxY + pad);
        }
    }

    public enum ShapeKind { Rectangle, Ellipse, Line, Arrow }

    public sealed class ShapeAnnotation : Annotation
    {
        public ShapeKind Kind;
        public PointF StartPt;
        public PointF EndPt;
        public Color StrokeColor = Color.Red;
        public double WidthPt = 2;
        public bool Filled;
        public Color FillColor = Color.Empty;

        private RectangleF NormRect()
        {
            float x = Math.Min(StartPt.X, EndPt.X);
            float y = Math.Min(StartPt.Y, EndPt.Y);
            return new RectangleF(x, y, Math.Abs(EndPt.X - StartPt.X), Math.Abs(EndPt.Y - StartPt.Y));
        }

        public override void DrawGdi(Graphics g, float ppp)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(StrokeColor, (float)(WidthPt * ppp))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            var r = NormRect();
            var rp = new RectangleF(r.X * ppp, r.Y * ppp, r.Width * ppp, r.Height * ppp);
            switch (Kind)
            {
                case ShapeKind.Rectangle:
                    if (Filled && FillColor.A > 0)
                        using (var b = new SolidBrush(FillColor)) g.FillRectangle(b, rp);
                    g.DrawRectangle(pen, rp.X, rp.Y, rp.Width, rp.Height);
                    break;
                case ShapeKind.Ellipse:
                    if (Filled && FillColor.A > 0)
                        using (var b = new SolidBrush(FillColor)) g.FillEllipse(b, rp);
                    g.DrawEllipse(pen, rp);
                    break;
                case ShapeKind.Line:
                    g.DrawLine(pen, StartPt.X * ppp, StartPt.Y * ppp, EndPt.X * ppp, EndPt.Y * ppp);
                    break;
                case ShapeKind.Arrow:
                    DrawArrowGdi(g, pen, ppp);
                    break;
            }
            g.SmoothingMode = old;
        }

        private void DrawArrowGdi(Graphics g, Pen pen, float ppp)
        {
            PointF a = new PointF(StartPt.X * ppp, StartPt.Y * ppp);
            PointF b = new PointF(EndPt.X * ppp, EndPt.Y * ppp);
            g.DrawLine(pen, a, b);
            double ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
            double head = Math.Max(8, WidthPt * ppp * 3);
            double spread = Math.PI / 7;
            var p1 = new PointF(
                (float)(b.X - head * Math.Cos(ang - spread)),
                (float)(b.Y - head * Math.Sin(ang - spread)));
            var p2 = new PointF(
                (float)(b.X - head * Math.Cos(ang + spread)),
                (float)(b.Y - head * Math.Sin(ang + spread)));
            using var brush = new SolidBrush(pen.Color);
            g.FillPolygon(brush, new[] { b, p1, p2 });
        }

        public override void DrawPdf(XGraphics gfx)
        {
            var pen = new XPen(ToXColor(StrokeColor), WidthPt)
            {
                LineCap = XLineCap.Round,
                LineJoin = XLineJoin.Round
            };
            var r = NormRect();
            switch (Kind)
            {
                case ShapeKind.Rectangle:
                    if (Filled && FillColor.A > 0)
                        gfx.DrawRectangle(new XSolidBrush(ToXColor(FillColor)), r.X, r.Y, r.Width, r.Height);
                    gfx.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                    break;
                case ShapeKind.Ellipse:
                    if (Filled && FillColor.A > 0)
                        gfx.DrawEllipse(new XSolidBrush(ToXColor(FillColor)), r.X, r.Y, r.Width, r.Height);
                    gfx.DrawEllipse(pen, r.X, r.Y, r.Width, r.Height);
                    break;
                case ShapeKind.Line:
                    gfx.DrawLine(pen, StartPt.X, StartPt.Y, EndPt.X, EndPt.Y);
                    break;
                case ShapeKind.Arrow:
                    DrawArrowPdf(gfx, pen);
                    break;
            }
        }

        private void DrawArrowPdf(XGraphics gfx, XPen pen)
        {
            gfx.DrawLine(pen, StartPt.X, StartPt.Y, EndPt.X, EndPt.Y);
            double ang = Math.Atan2(EndPt.Y - StartPt.Y, EndPt.X - StartPt.X);
            double head = Math.Max(6, WidthPt * 3);
            double spread = Math.PI / 7;
            var b = new XPoint(EndPt.X, EndPt.Y);
            var p1 = new XPoint(EndPt.X - head * Math.Cos(ang - spread), EndPt.Y - head * Math.Sin(ang - spread));
            var p2 = new XPoint(EndPt.X - head * Math.Cos(ang + spread), EndPt.Y - head * Math.Sin(ang + spread));
            gfx.DrawPolygon(new XSolidBrush(ToXColor(StrokeColor)), new[] { b, p1, p2 }, XFillMode.Winding);
        }

        public override bool HitTest(PointF p, float tol)
        {
            float t = tol + (float)WidthPt / 2f;
            switch (Kind)
            {
                case ShapeKind.Line:
                case ShapeKind.Arrow:
                    return SegmentHit(StartPt, EndPt, p, t);
                default:
                    var r = NormRect();
                    r.Inflate(t, t);
                    var inner = NormRect();
                    inner.Inflate(-t, -t);
                    bool onBorder = r.Contains(p) && !inner.Contains(p);
                    return onBorder || (Filled && NormRect().Contains(p));
            }
        }

        public override void Translate(float dx, float dy)
        {
            StartPt = new PointF(StartPt.X + dx, StartPt.Y + dy);
            EndPt = new PointF(EndPt.X + dx, EndPt.Y + dy);
        }

        public override RectangleF BoundsPt()
        {
            var r = NormRect();
            float pad = (float)WidthPt / 2f;
            r.Inflate(pad, pad);
            return r;
        }
    }

    public sealed class ImageAnnotation : Annotation
    {
        public byte[] ImageBytes;
        public RectangleF RectPt;   // placement rectangle in points
        private Image _cached;

        private Image GetImage()
        {
            if (_cached == null && ImageBytes != null)
                _cached = Image.FromStream(new MemoryStream(ImageBytes));
            return _cached;
        }

        public override void DrawGdi(Graphics g, float ppp)
        {
            var img = GetImage();
            if (img == null) return;
            g.DrawImage(img, RectPt.X * ppp, RectPt.Y * ppp, RectPt.Width * ppp, RectPt.Height * ppp);
        }

        public override void DrawPdf(XGraphics gfx)
        {
            if (ImageBytes == null) return;
            // PdfSharp's importer calls MemoryStream.GetBuffer(), which throws on a
            // stream created from a byte[]. A default-constructed, then-written
            // stream exposes its buffer, so use that instead.
            using var ms = new MemoryStream();
            ms.Write(ImageBytes, 0, ImageBytes.Length);
            ms.Position = 0;
            var ximg = XImage.FromStream(ms);
            gfx.DrawImage(ximg, RectPt.X, RectPt.Y, RectPt.Width, RectPt.Height);
        }

        public override bool HitTest(PointF p, float tol)
        {
            var r = RectPt;
            r.Inflate(tol, tol);
            return r.Contains(p);
        }

        public override void Translate(float dx, float dy)
            => RectPt = new RectangleF(RectPt.X + dx, RectPt.Y + dy, RectPt.Width, RectPt.Height);

        public override RectangleF BoundsPt() => RectPt;
    }
}
