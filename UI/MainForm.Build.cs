using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PdfEditor.Core;

namespace PdfEditor.UI
{
    // Construction of the chrome (menus, toolbar, properties panel, status bar)
    // and the UI-sync helpers that keep controls in step with the canvas.
    partial class MainForm
    {
        internal MenuStrip _menu;
        internal ToolStrip _toolStrip;
        internal StatusStrip _statusStrip;

        // Menu items whose enabled state must update (undo/redo).
        internal ToolStripMenuItem _miUndo, _miRedo;

        // Toolbar references.
        internal List<ToolStripButton> _toolButtons = new List<ToolStripButton>();
        internal ToolStripButton _btnUndo, _btnRedo, _btnColorSwatch;

        // Properties-panel controls.
        internal ComboBox _fontCombo;
        internal NumericUpDown _fontSize, _strokeWidth, _highlighterWidth;
        internal Button _btnColor, _btnFillColor, _btnApplySel;
        internal CheckBox _fillCheck;
        internal readonly ToolTip _tips = new ToolTip();

        private static readonly Color Accent = Color.FromArgb(23, 111, 209);
        private static readonly Color ToggleOn = Color.FromArgb(196, 222, 255);

        // ---------- Status bar ----------
        private void BuildStatusBar()
        {
            _statusStrip = new StatusStrip { BackColor = Color.FromArgb(225, 225, 228) };
            _statusPage = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _statusTool = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _statusZoom = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(0, 3, 12, 2) };
            _statusStrip.Items.Add(_statusPage);
            _statusStrip.Items.Add(Sep());
            _statusStrip.Items.Add(_statusTool);
            _statusStrip.Items.Add(Sep());
            _statusStrip.Items.Add(_statusZoom);
        }

        private static ToolStripStatusLabel Sep() => new ToolStripStatusLabel("|") { ForeColor = Color.FromArgb(180, 180, 180) };

        // ---------- Menus ----------
        private void BuildMenus()
        {
            _menu = new MenuStrip { BackColor = Color.FromArgb(248, 248, 250), Padding = new Padding(4, 2, 4, 2) };

            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(Make("&New", Keys.Control | Keys.N, OnNew));
            file.DropDownItems.Add(Make("&Open…", Keys.Control | Keys.O, OnOpen));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Make("&Save", Keys.Control | Keys.S, OnSave));
            file.DropDownItems.Add(Make("Save &As…", Keys.Control | Keys.Shift | Keys.S, OnSaveAs));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Make("&Import…", OnImport));
            file.DropDownItems.Add(Make("&Export…", OnExport));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Make("&Print…", Keys.Control | Keys.P, OnPrint));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Make("E&xit", OnExit));

            var edit = new ToolStripMenuItem("&Edit");
            _miUndo = Make("&Undo", Keys.Control | Keys.Z, OnUndo);
            _miRedo = Make("&Redo", Keys.Control | Keys.Y, OnRedo);
            edit.DropDownItems.Add(_miUndo);
            edit.DropDownItems.Add(_miRedo);
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Display("&Delete", "Del", OnDelete));
            edit.DropDownItems.Add(Display("Deselect", "Esc", (s, e) => SelectTool(ToolType.Select)));

            var view = new ToolStripMenuItem("&View");
            view.DropDownItems.Add(Display("First Page", "Home", (s, e) => _pageView.GoToPage(0)));
            view.DropDownItems.Add(Display("Previous Page", "PgUp", (s, e) => _pageView.GoToPage(_pageView.PageIndex - 1)));
            view.DropDownItems.Add(Display("Next Page", "PgDn", (s, e) => _pageView.GoToPage(_pageView.PageIndex + 1)));
            view.DropDownItems.Add(Display("Last Page", "End", (s, e) => _pageView.GoToPage(int.MaxValue)));
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(Make("Zoom In", Keys.Control | Keys.Oemplus, (s, e) => _pageView.ZoomIn()));
            view.DropDownItems.Add(Make("Zoom Out", Keys.Control | Keys.OemMinus, (s, e) => _pageView.ZoomOut()));
            view.DropDownItems.Add(Make("Actual Size", Keys.Control | Keys.D0, (s, e) => _pageView.ActualSize()));
            view.DropDownItems.Add(Make("Fit Width", (s, e) => _pageView.FitWidth(_scrollHost.ClientSize.Width)));

            var insert = new ToolStripMenuItem("&Insert");
            insert.DropDownItems.Add(Make("Text", OnInsertText));
            insert.DropDownItems.Add(Make("Image…", OnInsertImage));
            insert.DropDownItems.Add(new ToolStripSeparator());
            insert.DropDownItems.Add(Make("Blank Page", OnInsertBlank));

            var draw = new ToolStripMenuItem("&Draw");
            draw.DropDownItems.Add(Display("Select", "V", (s, e) => SelectTool(ToolType.Select)));
            draw.DropDownItems.Add(Display("Hand", "H", (s, e) => SelectTool(ToolType.Pan)));
            draw.DropDownItems.Add(new ToolStripSeparator());
            draw.DropDownItems.Add(Display("Pen", "P", (s, e) => SelectTool(ToolType.Pen)));
            draw.DropDownItems.Add(Display("Highlighter", "U", (s, e) => SelectTool(ToolType.Highlighter)));
            draw.DropDownItems.Add(new ToolStripSeparator());
            draw.DropDownItems.Add(Display("Rectangle", "R", (s, e) => SelectTool(ToolType.Rectangle)));
            draw.DropDownItems.Add(Display("Ellipse", "O", (s, e) => SelectTool(ToolType.Ellipse)));
            draw.DropDownItems.Add(Display("Line", "L", (s, e) => SelectTool(ToolType.Line)));
            draw.DropDownItems.Add(Display("Arrow", "A", (s, e) => SelectTool(ToolType.Arrow)));
            draw.DropDownItems.Add(new ToolStripSeparator());
            draw.DropDownItems.Add(Display("Eraser", "E", (s, e) => SelectTool(ToolType.Eraser)));

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(Make("&About", OnAbout));

            _menu.Items.AddRange(new ToolStripItem[] { file, edit, view, insert, draw, help });
        }

        // Real Ctrl-key shortcut.
        private static ToolStripMenuItem Make(string text, Keys shortcut, EventHandler onClick)
        {
            var mi = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
            mi.Click += onClick;
            return mi;
        }

        // No shortcut.
        private static ToolStripMenuItem Make(string text, EventHandler onClick)
        {
            var mi = new ToolStripMenuItem(text);
            mi.Click += onClick;
            return mi;
        }

        // Plain key handled in ProcessCmdKey; show the hint only so it doesn't fire
        // while typing in the inline text editor.
        private static ToolStripMenuItem Display(string text, string displayShortcut, EventHandler onClick)
        {
            var mi = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = displayShortcut };
            mi.Click += onClick;
            return mi;
        }

        // ---------- Toolbar ----------
        private void BuildToolbar()
        {
            _toolStrip = new ToolStrip
            {
                BackColor = Color.FromArgb(248, 248, 250),
                GripStyle = ToolStripGripStyle.Hidden,
                Padding = new Padding(3, 2, 3, 2),
                Renderer = new FlatToolStripRenderer(),
            };

            AddTool("↖", "Select / move (V)", ToolType.Select);
            AddTool("✋", "Pan (H)", ToolType.Pan);
            _toolStrip.Items.Add(new ToolStripSeparator());
            AddTool("T", "Text (T)", ToolType.Text);
            AddTool("✎", "Pen (P)", ToolType.Pen);
            AddTool("━", "Highlighter (U)", ToolType.Highlighter);
            AddTool("▭", "Rectangle (R)", ToolType.Rectangle);
            AddTool("◯", "Ellipse (O)", ToolType.Ellipse);
            AddTool("╱", "Line (L)", ToolType.Line);
            AddTool("➤", "Arrow (A)", ToolType.Arrow);
            AddTool("⌫", "Eraser (E)", ToolType.Eraser);
            _toolStrip.Items.Add(new ToolStripSeparator());

            _btnUndo = ToolBtn("↶", "Undo (Ctrl+Z)", (s, e) => OnUndo(s, e));
            _btnRedo = ToolBtn("↷", "Redo (Ctrl+Y)", (s, e) => OnRedo(s, e));
            _toolStrip.Items.Add(_btnUndo);
            _toolStrip.Items.Add(_btnRedo);
            _toolStrip.Items.Add(new ToolStripSeparator());

            _btnColorSwatch = new ToolStripButton
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Text = "■",
                ToolTipText = "Color",
                Font = new Font("Segoe UI", 14F),
            };
            _btnColorSwatch.Click += (s, e) => PickColor();
            _toolStrip.Items.Add(_btnColorSwatch);
            _toolStrip.Items.Add(new ToolStripSeparator());

            _toolStrip.Items.Add(ToolBtn("－", "Zoom out (Ctrl+-)", (s, e) => _pageView.ZoomOut()));
            _toolStrip.Items.Add(ToolBtn("＋", "Zoom in (Ctrl++)", (s, e) => _pageView.ZoomIn()));
            _toolStrip.Items.Add(ToolBtn("▦", "Fit width", (s, e) => _pageView.FitWidth(_scrollHost.ClientSize.Width)));

            SyncToolButtons(ToolType.Select);
            SyncColorSwatch();
        }

        private void AddTool(string glyph, string tip, ToolType t)
        {
            var b = new ToolStripButton
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Text = glyph,
                ToolTipText = tip,
                Font = new Font("Segoe UI", 12F),
                CheckOnClick = true,
                Tag = t,
            };
            b.Click += (s, e) => SelectTool(t);
            _toolButtons.Add(b);
            _toolStrip.Items.Add(b);
        }

        private static ToolStripButton ToolBtn(string glyph, string tip, EventHandler onClick)
        {
            var b = new ToolStripButton
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Text = glyph,
                ToolTipText = tip,
                Font = new Font("Segoe UI", 12F),
            };
            b.Click += onClick;
            return b;
        }

        internal void SelectTool(ToolType t)
        {
            _pageView.Tool = t;
            SyncToolButtons(t);
            UpdateStatus();
        }

        private void SyncToolButtons(ToolType t)
        {
            foreach (var b in _toolButtons)
                b.Checked = (ToolType)b.Tag == t;
            _toolStrip.Invalidate();
        }

        internal void UpdateUndoEnabled()
        {
            bool canU = _model != null && _model.Undo.CanUndo;
            bool canR = _model != null && _model.Undo.CanRedo;
            if (_miUndo != null) _miUndo.Enabled = canU;
            if (_miRedo != null) _miRedo.Enabled = canR;
            if (_btnUndo != null) _btnUndo.Enabled = canU;
            if (_btnRedo != null) _btnRedo.Enabled = canR;
        }

        private void OnSelectionChanged()
        {
            UpdateStatus();
            if (_btnApplySel != null) _btnApplySel.Enabled = _pageView.Selected != null;
        }

        private void SyncColorSwatch()
        {
            if (_btnColorSwatch != null) _btnColorSwatch.ForeColor = _settings.Color;
        }

        // ---------- Properties panel ----------
        private Control BuildPropertiesPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 224,
                BackColor = Color.FromArgb(249, 249, 251),
                Padding = new Padding(10, 10, 10, 10),
            };
            var lblTitle = new Label
            {
                Text = "PROPERTIES",
                Dock = DockStyle.Top,
                ForeColor = Color.FromArgb(110, 110, 122),
                Height = 22,
            };

            // Text group
            var gText = new GroupBox { Text = "Text", Dock = DockStyle.Top, Height = 118, Padding = new Padding(6, 6, 6, 6) };
            _fontCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
            _fontCombo.Items.AddRange(new object[] { "Arial", "Times New Roman", "Courier New", "Segoe UI", "Calibri" });
            _fontCombo.SelectedItem = _settings.FontFamily;
            _fontCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_fontCombo.SelectedItem != null) _settings.FontFamily = (string)_fontCombo.SelectedItem;
                _pageView.ApplySettingsToSelection();
            };
            _fontSize = Num(_settings.FontSizePt, 6, 96, 1);
            _fontSize.ValueChanged += (s, e) => { _settings.FontSizePt = (double)_fontSize.Value; _pageView.ApplySettingsToSelection(); };
            var btnBold = Toggle("B", "Bold", new Font(Font, FontStyle.Bold), _settings.Bold, v => { _settings.Bold = v; _pageView.ApplySettingsToSelection(); });
            var btnItalic = Toggle("I", "Italic", new Font(Font, FontStyle.Italic), _settings.Italic, v => { _settings.Italic = v; _pageView.ApplySettingsToSelection(); });
            gText.Controls.Add(Row2(btnBold, btnItalic));
            gText.Controls.Add(Row("Size", _fontSize));
            gText.Controls.Add(_fontCombo);

            // Stroke & color group
            var gStroke = new GroupBox { Text = "Stroke & Color", Dock = DockStyle.Top, Height = 196, Padding = new Padding(6, 6, 6, 6) };
            _btnColor = new Button { Text = "Stroke color", Dock = DockStyle.Top, BackColor = _settings.Color, FlatStyle = FlatStyle.Flat, Height = 28 };
            _btnColor.Click += (s, e) => PickColor();
            _strokeWidth = Num(_settings.StrokeWidthPt, 0.5m, 40m, 0.5m);
            _strokeWidth.ValueChanged += (s, e) => { _settings.StrokeWidthPt = (double)_strokeWidth.Value; _pageView.ApplySettingsToSelection(); };
            _highlighterWidth = Num(_settings.HighlighterWidthPt, 4m, 40m, 1m);
            _highlighterWidth.ValueChanged += (s, e) => { _settings.HighlighterWidthPt = (double)_highlighterWidth.Value; };
            _fillCheck = new CheckBox { Text = "Fill shapes", Dock = DockStyle.Top, Checked = _settings.FillShapes, Height = 24 };
            _fillCheck.CheckedChanged += (s, e) => { _settings.FillShapes = _fillCheck.Checked; _pageView.ApplySettingsToSelection(); };
            _btnFillColor = new Button { Text = "Fill color", Dock = DockStyle.Top, BackColor = _settings.FillColor, FlatStyle = FlatStyle.Flat, Height = 28 };
            _btnFillColor.Click += (s, e) =>
            {
                using var dlg = new ColorDialog { Color = _settings.FillColor, FullOpen = true };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _settings.FillColor = dlg.Color;
                    _btnFillColor.BackColor = dlg.Color;
                    _pageView.ApplySettingsToSelection();
                }
            };
            _btnApplySel = new Button { Text = "Apply to selection", Dock = DockStyle.Top, Enabled = false, Height = 28, FlatStyle = FlatStyle.Flat };
            _btnApplySel.Click += (s, e) => _pageView.ApplySettingsToSelection();
            gStroke.Controls.Add(_btnApplySel);
            gStroke.Controls.Add(_btnFillColor);
            gStroke.Controls.Add(_fillCheck);
            gStroke.Controls.Add(Row("High", _highlighterWidth));
            gStroke.Controls.Add(Row("Width", _strokeWidth));
            gStroke.Controls.Add(_btnColor);

            // Dock=Top stacks last-added on top: add bottom group first.
            panel.Controls.Add(gStroke);
            panel.Controls.Add(gText);
            panel.Controls.Add(lblTitle);
            return panel;
        }

        private static NumericUpDown Num(double value, decimal min, decimal max, decimal step)
        {
            var n = new NumericUpDown
            {
                Minimum = min, Maximum = max, Increment = step,
                DecimalPlaces = step < 1 ? 1 : 0, Width = 72,
            };
            try { n.Value = (decimal)value; } catch { n.Value = min; }
            return n;
        }

        private static Panel Row(string labelText, Control input)
        {
            var p = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(0, 5, 0, 0) };
            var l = new Label { Text = labelText, Dock = DockStyle.Left, Width = 54, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(90, 90, 98) };
            input.Dock = DockStyle.Fill;
            p.Controls.Add(input);
            p.Controls.Add(l);
            return p;
        }

        private static Panel Row2(Control a, Control b)
        {
            var p = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(0, 5, 0, 0) };
            a.Dock = DockStyle.Left; a.Width = 44;
            b.Dock = DockStyle.Left; b.Width = 44;
            p.Controls.Add(b);
            p.Controls.Add(a);
            return p;
        }

        private Button Toggle(string text, string tip, Font f, bool initial, Action<bool> onChange)
        {
            bool on = initial;
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, Font = f, Width = 44, BackColor = on ? ToggleOn : SystemColors.Control };
            _tips.SetToolTip(b, tip);
            b.Click += (s, e) =>
            {
                on = !on;
                b.BackColor = on ? ToggleOn : SystemColors.Control;
                onChange(on);
            };
            return b;
        }

        private void PickColor()
        {
            using var dlg = new ColorDialog { Color = _settings.Color, FullOpen = true };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _settings.Color = dlg.Color;
                if (_btnColor != null) _btnColor.BackColor = dlg.Color;
                SyncColorSwatch();
                _pageView.ApplySettingsToSelection();
            }
        }
    }
}
