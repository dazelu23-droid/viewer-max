using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PdfEditor.Core;

namespace PdfEditor.UI
{
    // The application window: assembles the menu bar, toolbar, properties panel,
    // scroll host, and status bar; owns the renderer + document model; and routes
    // keyboard shortcuts. All tool interaction and painting live in PageView.
    public partial class MainForm : Form
    {
        private readonly PdfRenderer _renderer = new PdfRenderer();
        private DocumentModel _model;
        private readonly ToolSettings _settings = new ToolSettings();
        private readonly PageView _pageView;

        internal Panel _scrollHost;
        // Status labels (built/owned in Build partial, written here).
        internal ToolStripStatusLabel _statusPage, _statusTool, _statusZoom;

        // --- Test accessors (headless GUI verification via --guitest) ---
        internal DocumentModel TestModel => _model;
        internal PageView TestView => _pageView;
        internal int MenuCount => _menu != null ? _menu.Items.Count : 0;
        internal int ToolButtonCount => _toolButtons.Count;
        internal bool HasPropertiesPanel => _fontCombo != null && _fontSize != null && _strokeWidth != null && _btnColor != null;

        public MainForm(string[] args)
        {
            Text = "PDF Editor";
            Width = 1180; Height = 780;
            MinimumSize = new Size(760, 480);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(239, 239, 242);
            ShowIcon = false;

            BuildStatusBar();

            _scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(82, 86, 89),
                AutoScroll = true,
            };
            _pageView = new PageView(_settings);
            _scrollHost.Controls.Add(_pageView);
            _scrollHost.Resize += (s, e) => _pageView.CenterInParent();

            var props = BuildPropertiesPanel();

            BuildToolbar();
            BuildMenus();

            // Dock z-order: the LAST control added is laid out first and claims its
            // edge. So add the fill host first, then status/props/toolstrip, and the
            // menu last so it pins to the very top above the toolbar.
            Controls.Add(_scrollHost);
            Controls.Add(props);
            Controls.Add(_statusStrip);
            Controls.Add(_toolStrip);
            Controls.Add(_menu);

            _pageView.ViewChanged += () => UpdateStatus();
            _pageView.SelectionChanged += _ => OnSelectionChanged();
            UpdateUndoEnabled();

            DocumentModel initial;
            try
            {
                if (args != null && args.Length > 0 && File.Exists(args[0]))
                    initial = DocumentModel.Open(args[0]);
                else
                    initial = DocumentModel.NewBlank();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open " + (args != null && args.Length > 0 ? args[0] : "document") + ":\n" + ex.Message,
                    "PDF Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                initial = DocumentModel.NewBlank();
            }
            LoadModel(initial);

            Shown += (s, e) =>
            {
                // First fit, now that the window has a real client size.
                if (_model != null && _scrollHost.ClientSize.Width > 0)
                    _pageView.FitWidth(_scrollHost.ClientSize.Width);
            };
            FormClosing += MainForm_FormClosing;
        }

        internal void LoadModel(DocumentModel model)
        {
            if (_model != null)
            {
                _model.Undo.Changed -= UpdateUndoEnabled;
                _model.AnnotationsChanged -= OnModelChanged;
                _model.StructureChanged -= OnModelChanged;
            }
            _model = model;
            _model.Undo.Changed += UpdateUndoEnabled;
            _model.AnnotationsChanged += OnModelChanged;
            _model.StructureChanged += OnModelChanged;
            _pageView.SetDocument(_model, _renderer);
            UpdateUndoEnabled();
            UpdateTitle();
            UpdateStatus();
            // Fit once the host has its real client size. On the first load (called
            // from the constructor) the handle doesn't exist yet, so the Shown
            // handler below performs the initial fit instead.
            if (IsHandleCreated)
                BeginInvoke((Action)(() => _pageView.FitWidth(_scrollHost.ClientSize.Width)));
        }

        private void OnModelChanged()
        {
            UpdateTitle();          // refresh dirty marker
            _pageView.Invalidate();
        }

        internal void UpdateTitle()
        {
            if (_model == null) { Text = "PDF Editor"; return; }
            string name = Path.GetFileName(_model.FilePath ?? "Untitled");
            Text = "PDF Editor — " + name + (_model.IsDirty ? "  ●" : "");
        }

        internal void UpdateStatus()
        {
            if (_statusPage == null || _model == null) return;
            _statusPage.Text = _pageView.StatusText();
            _statusTool.Text = "Tool: " + ToolName(_pageView.Tool);
            _statusZoom.Text = ((int)Math.Round(_pageView.Zoom * 100)) + "%";
        }

        internal static string ToolName(ToolType t) => t switch
        {
            ToolType.Select => "Select", ToolType.Pan => "Hand", ToolType.Text => "Text",
            ToolType.Pen => "Pen", ToolType.Highlighter => "Highlighter",
            ToolType.Rectangle => "Rectangle", ToolType.Ellipse => "Ellipse",
            ToolType.Line => "Line", ToolType.Arrow => "Arrow",
            ToolType.Eraser => "Eraser", ToolType.PlaceImage => "Place Image",
            _ => t.ToString()
        };

        // Confirm before discarding an unsaved document. Returns true if safe to proceed.
        internal bool ConfirmDiscard()
        {
            if (_model == null || !_model.IsDirty) return true;
            var r = MessageBox.Show(this, "You have unsaved changes. Discard them?",
                "PDF Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            return r == DialogResult.Yes;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_model != null && _model.IsDirty && e.CloseReason != CloseReason.ApplicationExitCall)
            {
                var r = MessageBox.Show(this, "Save changes before closing?",
                    "PDF Editor", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) e.Cancel = true;
                else if (r == DialogResult.Yes)
                {
                    DoSave(string.IsNullOrEmpty(_model.FilePath) ? null : _model.FilePath);
                    if (_model.IsDirty) e.Cancel = true;   // save failed/aborted -> don't close
                }
            }
        }

        // Route plain-key shortcuts (Del, Esc, single-letter tools, page nav). Ctrl-combos
        // are handled by menu ShortcutKeys. We never hijack the inline text editor.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_model != null && !(ActiveControl is TextBox))
            {
                switch (keyData)
                {
                    case Keys.Escape: SelectTool(ToolType.Select); return true;
                    case Keys.Delete: _pageView.DeleteSelected(); return true;
                    case Keys.V: SelectTool(ToolType.Select); return true;
                    case Keys.H: SelectTool(ToolType.Pan); return true;
                    case Keys.T: SelectTool(ToolType.Text); return true;
                    case Keys.P: SelectTool(ToolType.Pen); return true;
                    case Keys.R: SelectTool(ToolType.Rectangle); return true;
                    case Keys.O: SelectTool(ToolType.Ellipse); return true;
                    case Keys.L: SelectTool(ToolType.Line); return true;
                    case Keys.A: SelectTool(ToolType.Arrow); return true;
                    case Keys.E: SelectTool(ToolType.Eraser); return true;
                    case Keys.Home: _pageView.GoToPage(0); return true;
                    case Keys.End: _pageView.GoToPage(int.MaxValue); return true;
                    case Keys.PageUp: _pageView.GoToPage(_pageView.PageIndex - 1); return true;
                    case Keys.PageDown: _pageView.GoToPage(_pageView.PageIndex + 1); return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
