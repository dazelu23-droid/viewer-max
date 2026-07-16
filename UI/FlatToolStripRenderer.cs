using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PdfEditor.UI
{
    // Flat renderer that draws its own button backgrounds for hover/checked states
    // instead of the themed blue, so the toolbar matches the app's quiet look and
    // the active tool reads clearly.
    internal sealed class FlatToolStripRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Hover = Color.FromArgb(235, 238, 244);
        private static readonly Color CheckedFill = Color.FromArgb(216, 232, 252);
        private static readonly Color CheckedEdge = Color.FromArgb(23, 111, 209);

        public FlatToolStripRenderer() { RoundedEdges = false; }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var btn = e.Item as ToolStripButton;
            if (btn == null) { base.OnRenderButtonBackground(e); return; }

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(1, 1, btn.Width - 3, btn.Height - 3);
            using var path = RoundRect(r, 4);

            if (btn.Checked)
            {
                using (var b = new SolidBrush(CheckedFill)) g.FillPath(b, path);
                using var pen = new Pen(CheckedEdge, 1.4f);
                g.DrawPath(pen, path);
            }
            else if (btn.Selected || btn.Pressed)
            {
                using var b = new SolidBrush(Hover);
                g.FillPath(b, path);
            }
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
