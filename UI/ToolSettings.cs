using System.Drawing;

namespace PdfEditor.UI
{
    public enum ToolType
    {
        Select, Pan, Text, Pen, Highlighter,
        Rectangle, Ellipse, Line, Arrow, Eraser, PlaceImage
    }

    // Mutable, shared between the properties panel and the canvas: the panel
    // writes user choices here and the canvas reads them when creating the next
    // annotation.
    public sealed class ToolSettings
    {
        public Color Color = Color.Red;
        public double StrokeWidthPt = 2;
        public string FontFamily = "Arial";
        public double FontSizePt = 14;
        public bool Bold;
        public bool Italic;
        public bool FillShapes;
        public Color FillColor = Color.FromArgb(80, Color.Yellow);
        public double HighlighterWidthPt = 12;
    }
}
