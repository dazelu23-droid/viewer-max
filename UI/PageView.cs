using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PdfEditor.Core;

namespace PdfEditor.UI
{
    // The scrollable page canvas. It renders the current PDF page to a cached
    // bitmap (PDF content only) and paints annotations live on top, so edits show
    // instantly without re-rasterizing the PDF. All hit-testing and tool input
    // happens in PDF-point space; pixels-per-point is derived from the rendered
    // bitmap so it stays correct at any zoom.
    public sealed class PageView : Control
    {
        private DocumentModel _model;
        private PdfRenderer _renderer;
        private readonly ToolSettings _settings;

        private int _pageIndex;
        private double _zoom = 1.0;
        private Bitmap _pageBitmap;
        private float _ppp = 1.0f;

        private ToolType _tool = ToolType.Select;

        // Draft (in-progress) state
        private List<PointF> _draftInk;
        private ShapeAnnotation _draftShape;
        private bool _mouseDown;
        private PointF _lastPt;

        // Selection / move
        private Annotation _selected;
        private bool _dragging;
        private float _dragDx, _dragDy;

        // Eraser
        private readonly HashSet<Annotation> _erasePreview = new HashSet<Annotation>();
        private List<Annotation> _eraseBatch;

        // Pan
        private Point _panStartMouse;
        private Point _panStartScroll;

        // Inline text editing
        private TextBox _editor;
        private PointF _editorAnchorPt;

        public byte[] PendingImageBytes { get; set; }

        public event Action ViewChanged;
        public event Action<Annotation> SelectionChanged;

        public PageView(ToolSettings settings)
        {
            _settings = settings;
            DoubleBuffered = true;
            SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = true;
            BackColor = Color.FromArgb(82, 86, 89);
        }

        public int PageIndex => _pageIndex;
        public int PageCount => _model?.PageCount ?? 0;
        public double Zoom => _zoom;
        public Annotation Selected => _selected;

        public ToolType Tool
        {
            get => _tool;
            set
            {
                CommitEditor();
                _tool = value;
                _selected = null;
                UpdateCursor();
                Invalidate();
                SelectionChanged?.Invoke(null);
            }
        }

        public void SetDocument(DocumentModel model, PdfRenderer renderer)
        {
            if (_model != null)
            {
                _model.AnnotationsChanged -= OnModelAnnotationsChanged;
                _model.StructureChanged -= OnModelStructureChanged;
            }
            _model = model;
            _renderer = renderer;
            _pageIndex = 0;
            _selected = null;
            if (_model != null)
            {
                _model.AnnotationsChanged += OnModelAnnotationsChanged;
                _model.StructureChanged += OnModelStructureChanged;
            }
            RebuildPage();
        }

        private void OnModelAnnotationsChanged() => Invalidate();

        private void OnModelStructureChanged()
        {
            if (_pageIndex >= _model.PageCount) _pageIndex = Math.Max(0, _model.PageCount - 1);
            RebuildPage();
        }

        public void GoToPage(int index)
        {
            if (_model == null) return;
            index = Math.Max(0, Math.Min(_model.PageCount - 1, index));
            if (index == _pageIndex && _pageBitmap != null) return;
            CommitEditor();
            _pageIndex = index;
            _selected = null;
            RebuildPage();
            SelectionChanged?.Invoke(null);
        }

        public void SetZoom(double zoom)
        {
            zoom = Math.Max(0.1, Math.Min(6.0, zoom));
            if (Math.Abs(zoom - _zoom) < 0.001 && _pageBitmap != null) return;
            CommitEditor();
            _zoom = zoom;
            RebuildPage();
        }

        public void ZoomIn() => SetZoom(_zoom * 1.25);
        public void ZoomOut() => SetZoom(_zoom / 1.25);
        public void ActualSize() => SetZoom(1.0);

        public void FitWidth(int viewportWidth)
        {
            if (_model == null || _model.PageCount == 0) return;
            float wpt = _model.PageSizesPt[_pageIndex].Width;
            double targetPpp = (viewportWidth - 30) / wpt;
            SetZoom(targetPpp / (96.0 / 72.0));
        }

        private void RebuildPage()
        {
            if (_model == null || _model.PageCount == 0)
            {
                _pageBitmap?.Dispose();
                _pageBitmap = null;
                Size = new Size(10, 10);
                Invalidate();
                ViewChanged?.Invoke();
                return;
            }
            double requestedPpp = _zoom * (96.0 / 72.0);
            var old = _pageBitmap;
            _pageBitmap = _renderer.RenderPage(_model.BaseBytes, _pageIndex, requestedPpp);
            old?.Dispose();

            float wpt = _model.PageSizesPt[_pageIndex].Width;
            _ppp = wpt > 0 ? _pageBitmap.Width / wpt : (float)requestedPpp;

            Size = _pageBitmap.Size;
            CenterInParent();
            Invalidate();
            ViewChanged?.Invoke();
        }

        public void CenterInParent()
        {
            if (Parent == null || _pageBitmap == null) return;
            int x = Math.Max(0, (Parent.ClientSize.Width - Width) / 2);
            int y = Math.Max(0, (Parent.ClientSize.Height - Height) / 2);
            var scroll = Parent as ScrollableControl;
            Point ap = scroll?.AutoScrollPosition ?? Point.Empty;
            Location = new Point(x + ap.X, y + ap.Y);
        }

        private PointF ToPoint(int x, int y) => new PointF(x / _ppp, y / _ppp);
        private float TolPt(float px) => px / _ppp;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (_pageBitmap == null)
            {
                g.Clear(BackColor);
                return;
            }
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImageUnscaled(_pageBitmap, 0, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Committed annotations (skipping ones being erased this stroke)
            if (_model.Annotations.TryGetValue(_pageIndex, out var list))
            {
                foreach (var ann in list)
                {
                    if (_erasePreview.Contains(ann)) continue;
                    try { ann.DrawGdi(g, _ppp); } catch { }
                }
            }

            // Draft previews
            if (_draftInk != null && _draftInk.Count > 0)
            {
                var ink = new InkAnnotation
                {
                    PointsPt = _draftInk,
                    Color = _settings.Color,
                    Highlighter = _tool == ToolType.Highlighter,
                    WidthPt = _tool == ToolType.Highlighter ? _settings.HighlighterWidthPt : _settings.StrokeWidthPt
                };
                ink.DrawGdi(g, _ppp);
            }
            _draftShape?.DrawGdi(g, _ppp);

            // Selection chrome
            if (_selected != null && _tool == ToolType.Select)
            {
                var b = _selected.BoundsPt();
                var r = new RectangleF(b.X * _ppp, b.Y * _ppp, b.Width * _ppp, b.Height * _ppp);
                r.Inflate(3, 3);
                using var pen = new Pen(Color.FromArgb(23, 111, 209), 1.5f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (_model == null || _pageBitmap == null) return;
            _mouseDown = true;
            var pt = ToPoint(e.X, e.Y);
            _lastPt = pt;

            switch (_tool)
            {
                case ToolType.Pan:
                    _panStartMouse = Control.MousePosition;
                    var sc = Parent as ScrollableControl;
                    _panStartScroll = sc != null ? sc.AutoScrollPosition : Point.Empty;
                    break;

                case ToolType.Text:
                    BeginTextEdit(e.Location, pt);
                    break;

                case ToolType.Pen:
                case ToolType.Highlighter:
                    _draftInk = new List<PointF> { pt };
                    break;

                case ToolType.Rectangle:
                case ToolType.Ellipse:
                case ToolType.Line:
                case ToolType.Arrow:
                    _draftShape = new ShapeAnnotation
                    {
                        Kind = ShapeKindFor(_tool),
                        StartPt = pt,
                        EndPt = pt,
                        StrokeColor = _settings.Color,
                        WidthPt = _settings.StrokeWidthPt,
                        Filled = _settings.FillShapes,
                        FillColor = _settings.FillColor
                    };
                    break;

                case ToolType.Eraser:
                    _eraseBatch = new List<Annotation>();
                    EraseAt(pt);
                    break;

                case ToolType.PlaceImage:
                    PlaceImageAt(pt);
                    break;

                case ToolType.Select:
                    _selected = HitTopmost(pt);
                    _dragging = _selected != null;
                    _dragDx = _dragDy = 0;
                    SelectionChanged?.Invoke(_selected);
                    Invalidate();
                    break;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_mouseDown || _model == null) return;
            var pt = ToPoint(e.X, e.Y);

            switch (_tool)
            {
                case ToolType.Pan:
                    var sc = Parent as ScrollableControl;
                    if (sc != null)
                    {
                        Point now = Control.MousePosition;
                        int nx = -_panStartScroll.X - (now.X - _panStartMouse.X);
                        int ny = -_panStartScroll.Y - (now.Y - _panStartMouse.Y);
                        sc.AutoScrollPosition = new Point(nx, ny);
                    }
                    break;

                case ToolType.Pen:
                case ToolType.Highlighter:
                    if (_draftInk != null && Dist(pt, _draftInk[_draftInk.Count - 1]) > 1.2f)
                    {
                        _draftInk.Add(pt);
                        Invalidate();
                    }
                    break;

                case ToolType.Rectangle:
                case ToolType.Ellipse:
                case ToolType.Line:
                case ToolType.Arrow:
                    if (_draftShape != null) { _draftShape.EndPt = pt; Invalidate(); }
                    break;

                case ToolType.Eraser:
                    EraseAt(pt);
                    break;

                case ToolType.Select:
                    if (_dragging && _selected != null)
                    {
                        float dx = pt.X - _lastPt.X, dy = pt.Y - _lastPt.Y;
                        _selected.Translate(dx, dy);
                        _dragDx += dx; _dragDy += dy;
                        Invalidate();
                    }
                    break;
            }
            _lastPt = pt;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_mouseDown) return;
            _mouseDown = false;

            switch (_tool)
            {
                case ToolType.Pen:
                case ToolType.Highlighter:
                    if (_draftInk != null && _draftInk.Count >= 1)
                    {
                        var ink = new InkAnnotation
                        {
                            PointsPt = _draftInk,
                            Color = _settings.Color,
                            Highlighter = _tool == ToolType.Highlighter,
                            WidthPt = _tool == ToolType.Highlighter ? _settings.HighlighterWidthPt : _settings.StrokeWidthPt
                        };
                        _model.AddAnnotation(_pageIndex, ink);
                    }
                    _draftInk = null;
                    Invalidate();
                    break;

                case ToolType.Rectangle:
                case ToolType.Ellipse:
                case ToolType.Line:
                case ToolType.Arrow:
                    if (_draftShape != null && Dist(_draftShape.StartPt, _draftShape.EndPt) > 2f)
                        _model.AddAnnotation(_pageIndex, _draftShape);
                    _draftShape = null;
                    Invalidate();
                    break;

                case ToolType.Eraser:
                    _erasePreview.Clear();
                    if (_eraseBatch != null && _eraseBatch.Count > 0)
                        _model.RemoveAnnotations(_pageIndex, _eraseBatch);
                    _eraseBatch = null;
                    Invalidate();
                    break;

                case ToolType.Select:
                    if (_dragging && _selected != null && (Math.Abs(_dragDx) > 0.01 || Math.Abs(_dragDy) > 0.01))
                    {
                        var target = _selected;
                        float dx = _dragDx, dy = _dragDy;
                        // We applied the move live; revert so Undo/Redo drive it cleanly.
                        target.Translate(-dx, -dy);
                        _model.Undo.Push(new RelayCommand("Move",
                            () => { target.Translate(dx, dy); _model.IsDirty = true; Invalidate(); },
                            () => { target.Translate(-dx, -dy); _model.IsDirty = true; Invalidate(); }));
                    }
                    _dragging = false;
                    break;
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            // Double-click a text annotation with the Select tool to edit it.
            if (_tool == ToolType.Select && _model != null)
            {
                var pt = ToPoint(e.X, e.Y);
                if (HitTopmost(pt) is TextAnnotation ta)
                {
                    var list = _model.GetPageAnnotations(_pageIndex);
                    list.Remove(ta);
                    _settings.FontFamily = ta.FontFamily;
                    _settings.FontSizePt = ta.FontSizePt;
                    _settings.Color = ta.Color;
                    _settings.Bold = ta.Bold;
                    _settings.Italic = ta.Italic;
                    var loc = new Point((int)(ta.PositionPt.X * _ppp), (int)(ta.PositionPt.Y * _ppp));
                    BeginTextEdit(loc, ta.PositionPt);
                    _editor.Text = ta.Text;
                    _editor.SelectAll();
                    Invalidate();
                }
            }
        }

        private static ShapeKind ShapeKindFor(ToolType t) => t switch
        {
            ToolType.Rectangle => ShapeKind.Rectangle,
            ToolType.Ellipse => ShapeKind.Ellipse,
            ToolType.Line => ShapeKind.Line,
            ToolType.Arrow => ShapeKind.Arrow,
            _ => ShapeKind.Rectangle
        };

        private Annotation HitTopmost(PointF pt)
        {
            if (!_model.Annotations.TryGetValue(_pageIndex, out var list)) return null;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].HitTest(pt, TolPt(5))) return list[i];
            return null;
        }

        private void EraseAt(PointF pt)
        {
            if (!_model.Annotations.TryGetValue(_pageIndex, out var list)) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var a = list[i];
                if (!_erasePreview.Contains(a) && a.HitTest(pt, TolPt(6)))
                {
                    _erasePreview.Add(a);
                    _eraseBatch.Add(a);
                    Invalidate();
                }
            }
        }

        private void PlaceImageAt(PointF pt)
        {
            if (PendingImageBytes == null) return;
            SizeF sz;
            try
            {
                using var ms = new System.IO.MemoryStream(PendingImageBytes);
                using var img = Image.FromStream(ms);
                sz = new SizeF(img.Width * 72f / 96f, img.Height * 72f / 96f);
            }
            catch { return; }

            // Clamp very large images to the page width.
            float maxW = _model.PageSizesPt[_pageIndex].Width - pt.X - 12;
            if (sz.Width > maxW && sz.Width > 0)
            {
                float scale = maxW / sz.Width;
                sz = new SizeF(sz.Width * scale, sz.Height * scale);
            }
            var ann = new ImageAnnotation
            {
                ImageBytes = PendingImageBytes,
                RectPt = new RectangleF(pt.X, pt.Y, sz.Width, sz.Height)
            };
            _model.AddAnnotation(_pageIndex, ann);
            PendingImageBytes = null;
            Tool = ToolType.Select;
        }

        // ---- Inline text editor ----
        private void BeginTextEdit(Point loc, PointF anchorPt)
        {
            CommitEditor();
            _editorAnchorPt = anchorPt;
            var style = (_settings.Bold ? FontStyle.Bold : 0) | (_settings.Italic ? FontStyle.Italic : 0);
            _editor = new TextBox
            {
                Multiline = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font(_settings.FontFamily, Math.Max(6f, (float)(_settings.FontSizePt * _ppp)), style, GraphicsUnit.Pixel),
                ForeColor = _settings.Color,
                Location = loc,
                Width = Math.Max(120, (int)(200)),
                Height = Math.Max(24, (int)(_settings.FontSizePt * _ppp * 1.6)),
                Text = ""
            };
            _editor.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; CommitEditor(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CancelEditor(); }
            };
            _editor.LostFocus += (s, e) => CommitEditor();
            Controls.Add(_editor);
            _editor.Focus();
        }

        private void CommitEditor()
        {
            if (_editor == null) return;
            var ed = _editor;
            _editor = null;
            string text = ed.Text;
            Controls.Remove(ed);
            ed.Dispose();
            if (!string.IsNullOrWhiteSpace(text))
            {
                _model.AddAnnotation(_pageIndex, new TextAnnotation
                {
                    PositionPt = _editorAnchorPt,
                    Text = text,
                    FontFamily = _settings.FontFamily,
                    FontSizePt = _settings.FontSizePt,
                    Color = _settings.Color,
                    Bold = _settings.Bold,
                    Italic = _settings.Italic
                });
            }
            Invalidate();
        }

        private void CancelEditor()
        {
            if (_editor == null) return;
            var ed = _editor;
            _editor = null;
            Controls.Remove(ed);
            ed.Dispose();
            Invalidate();
        }

        public void DeleteSelected()
        {
            if (_selected == null) return;
            _model.RemoveAnnotations(_pageIndex, new List<Annotation> { _selected });
            _selected = null;
            SelectionChanged?.Invoke(null);
            Invalidate();
        }

        // Applies current tool settings to the selected annotation (color/width/font).
        public void ApplySettingsToSelection()
        {
            if (_selected == null) return;
            switch (_selected)
            {
                case TextAnnotation t:
                    t.Color = _settings.Color; t.FontFamily = _settings.FontFamily;
                    t.FontSizePt = _settings.FontSizePt; t.Bold = _settings.Bold; t.Italic = _settings.Italic;
                    break;
                case InkAnnotation ink:
                    ink.Color = _settings.Color; ink.WidthPt = _settings.StrokeWidthPt;
                    break;
                case ShapeAnnotation sh:
                    sh.StrokeColor = _settings.Color; sh.WidthPt = _settings.StrokeWidthPt;
                    sh.Filled = _settings.FillShapes; sh.FillColor = _settings.FillColor;
                    break;
            }
            _model.IsDirty = true;
            Invalidate();
        }

        private void UpdateCursor()
        {
            switch (_tool)
            {
                case ToolType.Pan: Cursor = Cursors.Hand; break;
                case ToolType.Text: Cursor = Cursors.IBeam; break;
                case ToolType.Select: Cursor = Cursors.Default; break;
                case ToolType.Eraser: Cursor = Cursors.Cross; break;
                default: Cursor = Cursors.Cross; break;
            }
        }

        private static float Dist(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public string StatusText()
        {
            if (_model == null || _model.PageCount == 0) return "No document";
            return $"Page {_pageIndex + 1} of {_model.PageCount}    {(int)Math.Round(_zoom * 100)}%";
        }
    }
}
