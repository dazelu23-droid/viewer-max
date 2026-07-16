# PDF Editor MainForm UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `MainForm` shell (menus, toolbar, properties panel, status bar) and wire every command to the existing, verified core so the PDF editor is a complete, runnable Windows app.

**Architecture:** Hand-coded WinForms `MainForm` (no `.Designer.cs`), split into three `partial` files by responsibility: lifecycle (`MainForm.cs`), control construction (`MainForm.Build.cs`), and command handlers (`MainForm.Commands.cs`). It owns a long-lived `PdfRenderer`, the current `DocumentModel`, a shared `ToolSettings`, and the existing `PageView` canvas. The only new core code is `PdfIO.AppendBlankPage`.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), WinForms, PDFsharp 6.1.1, Docnet.Core 2.6.0. Build: `dotnet build` via the user-profile SDK at `%USERPROFILE%\dotnet\dotnet.exe`.

## Global Constraints

- Target `net8.0-windows`, `x64`, `WinExe`, `Nullable` disabled, `ImplicitUsings` disabled (matches `PdfEditor.csproj`).
- SDK is installed in the user profile only; PATH is not persisted across shell calls — always invoke the full path `$DOTNET` where `DOTNET="$USERPROFILE/dotnet/dotnet.exe"` (Git Bash: `$HOME/dotnet/dotnet.exe`).
- No icon asset files — Unicode glyphs + text labels only.
- No thumbnail strip; no OCR/form-fields.
- Every IO/print/export call wrapped so one failure shows a `MessageBox` instead of crashing.

---

## File Structure

- **Modify** `Core/PdfIO.cs` — add `AppendBlankPage`.
- **Modify** `Core/SelfTest.cs` — add an assertion for `AppendBlankPage`.
- **Replace** `UI/MainForm.cs` — lifecycle, fields, layout assembly, model load, title/status, keyboard routing, close-confirm.
- **Create** `UI/MainForm.Build.cs` — partial: `BuildMenus`, `BuildToolbar`, `BuildPropertiesPanel`, `BuildStatusBar`, and UI-sync helpers (`SelectTool`, `UpdateUndoEnabled`, `UpdateStatus`, `OnViewChanged`, `OnSelectionChanged`).
- **Create** `UI/MainForm.Commands.cs` — partial: New/Open/Save/SaveAs, Import, Export, Print, Insert (Text/Image/BlankPage), page nav, zoom, undo/redo, About, delete/deselect.

**Interfaces:**
- Consumes: `DocumentModel` (`NewBlank`, `Open(path)`, `SaveAs(path)`, `FilePath`, `IsDirty`, `Undo: UndoStack`, `PageCount`, `Annotations`, `StructureChanged`, `AnnotationsChanged`, `ReplaceBaseKeepingAnnotations(bytes)`); `PdfIO` (`AppendPdf`, `AppendImagePage`, `CreateBlank`, `LetterWidthPt`, `LetterHeightPt`, + new `AppendBlankPage`); `Compositor.ComposePage(renderer, model, pageIdx, scaling, out ppp)`; `PdfRenderer`; `PageView` (`Tool`, `Zoom`, `PageIndex`, `Selected`, `SetDocument`, `GoToPage`, `SetZoom`, `ZoomIn/Out/ActualSize`, `FitWidth(int)`, `DeleteSelected`, `ApplySettingsToSelection`, `PendingImageBytes`, `StatusText()`, events `ViewChanged`, `SelectionChanged`); `ToolSettings`; `ToolType`.
- Produces: a complete runnable `PdfEditor` WinExe.

---

### Task 1: AppendBlankPage (pure core, TDD)

**Files:**
- Modify: `Core/PdfIO.cs` (append method near `AppendImagePage`)
- Modify: `Core/SelfTest.cs:130` region (add assertion before the print-compose step)

**Interfaces:**
- Produces: `byte[] PdfIO.AppendBlankPage(byte[] baseBytes, double widthPt, double heightPt)` — appends one blank page of the given size to `baseBytes`, returns new bytes.

- [ ] **Step 1: Write the failing test (extend SelfTest)**

In `Core/SelfTest.cs`, after the erase assertion (around line 130) and before the print-compose step, add:

```csharp
// 12. Append a blank page of a custom size
int beforeBlank = model.PageCount;
model.ReplaceBaseKeepingAnnotations(
    PdfIO.AppendBlankPage(model.BaseBytes, PdfIO.LetterWidthPt, PdfIO.LetterHeightPt));
Assert(model.PageCount == beforeBlank + 1, "append blank page added one page");
var blankSizes = PdfIO.ReadPageSizes(model.BaseBytes, out _);
Assert(Math.Abs(blankSizes[blankSizes.Count - 1].Width - 612) < 1
    && Math.Abs(blankSizes[blankSizes.Count - 1].Height - 792) < 1,
    "blank page is letter-sized");
```

- [ ] **Step 2: Run self-test, verify it fails**

Run: `$HOME/dotnet/dotnet.exe run -c Debug --project . -- --selftest` then read `%TEMP%\pdfeditor_selftest\result.txt`.
Expected: FAIL with `append blank page added one page` (method does not exist / compile error: `AppendBlankPage` not defined).

- [ ] **Step 3: Implement AppendBlankPage**

Add to `Core/PdfIO.cs` (after `AppendPdf`):

```csharp
// Appends a single blank page of the given size (points) to the base document.
public static byte[] AppendBlankPage(byte[] baseBytes, double widthPt, double heightPt)
{
    using var ms = new MemoryStream();
    ms.Write(baseBytes, 0, baseBytes.Length);
    ms.Position = 0;
    using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
    var page = doc.AddPage();
    page.Width = XUnit.FromPoint(widthPt);
    page.Height = XUnit.FromPoint(heightPt);
    using var outMs = new MemoryStream();
    doc.Save(outMs, false);
    return outMs.ToArray();
}
```

- [ ] **Step 4: Run self-test, verify it passes**

Run the same command; read the log.
Expected: `SELFTEST RESULT: ALL PASSED` (now 22 assertions, was 20).

- [ ] **Step 5: Commit**

```bash
git init -q 2>/dev/null; git add Core/PdfIO.cs Core/SelfTest.cs
git commit -m "feat(pdfio): AppendBlankPage for Insert>Blank Page"
```
(Repo is not yet git-initialized; `git init` is a no-op if it already exists.)

---

### Task 2: MainForm — lifecycle + layout + keyboard routing

**Files:**
- Replace: `UI/MainForm.cs`

**Interfaces:**
- Produces (fields used by the other partials): `_model`, `_renderer`, `_settings`, `_pageView`, `_scrollHost`, `_statusPage`, `_statusTool`, `_statusZoom`, `_miUndo`, `_miRedo`, `_toolButtons` (List), `_btnUndo`, `_btnRedo`, `_btnColorSwatch`; methods called by the other partials: `LoadModel(DocumentModel)`, `UpdateTitle()`, `UpdateStatus()`, `ConfirmDiscard()`, `SelectTool(ToolType)`, `UpdateUndoEnabled()`. Methods defined in the other partials and called here: `BuildMenus()`, `BuildToolbar()`, `BuildPropertiesPanel()` (returns `Control`), `BuildStatusBar()`.

- [ ] **Step 1: Write MainForm.cs shell**

Replace `UI/MainForm.cs` entirely with:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PdfEditor.Core;

namespace PdfEditor.UI
{
    // The application window: assembles the menu bar, toolbar, properties panel,
    // scroll host, and status bar, owns the renderer + document model, and routes
    // keyboard shortcuts. Tool interaction and painting live in PageView.
    public partial class MainForm : Form
    {
        private readonly PdfRenderer _renderer = new PdfRenderer();
        private DocumentModel _model;
        private readonly ToolSettings _settings = new ToolSettings();
        private readonly PageView _pageView;

        private Panel _scrollHost;
        // Status labels
        internal ToolStripStatusLabel _statusPage, _statusTool, _statusZoom;
        // Menu references (for enable state)
        internal ToolStripMenuItem _miUndo, _miRedo;
        // Toolbar references
        internal List<ToolStripButton> _toolButtons = new List<ToolStripButton>();
        internal ToolStripButton _btnUndo, _btnRedo, _btnColorSwatch;

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

            // Dock z-order: add in front-to-back order so each edge consumer
            // docks before the next. Menu (top) > Toolbar (top) > Props (right)
            // > Status (bottom) > Host (fill).
            Controls.Add(_menu);
            Controls.Add(_toolStrip);
            Controls.Add(props);
            Controls.Add(_statusStrip);
            Controls.Add(_scrollHost);

            _pageView.ViewChanged += (s, e) => UpdateStatus();
            _pageView.SelectionChanged += (s, e) => OnSelectionChanged();
            UpdateUndoEnabled();

            // Load initial document: file passed on command line, else blank.
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
                MessageBox.Show(this, "Could not open " + (args?.Length > 0 ? args[0] : "document") + ":\n" + ex.Message,
                    "PDF Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                initial = DocumentModel.NewBlank();
            }
            LoadModel(initial);

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
            // Fit after the host has its real size.
            BeginInvoke((Action)(() => _pageView.FitWidth(_scrollHost.ClientSize.Width)));
        }

        private void OnModelChanged()
        {
            UpdateTitle();          // dirty marker
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
                else if (r == DialogResult.Yes) { DoSave(_model.FilePath); if (_model.IsDirty) e.Cancel = true; }
            }
        }

        // Route plain-key shortcuts (Del, Esc, single-letter tools, page nav, zoom).
        // Ctrl-combos are handled by menu ShortcutKeys. Never hijack the inline text editor.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!(ActiveControl is TextBox) && _model != null)
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
                    case Keys.Add:
                    case (Keys.Control | Keys.Oemplus): _pageView.ZoomIn(); return true;
                    case Keys.Subtract:
                    case (Keys.Control | Keys.OemMinus): _pageView.ZoomOut(); return true;
                    case (Keys.Control | Keys.D0): _pageView.ActualSize(); return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
```

- [ ] **Step 2: Temporarily stub the other partials so it compiles**

Create `UI/MainForm.Build.cs` and `UI/MainForm.Commands.cs` as minimal stubs exposing the members referenced above (`_menu`, `_toolStrip`, `_statusStrip`, `BuildMenus/BuildToolbar/BuildStatusBar/BuildPropertiesPanel/SelectTool/UpdateUndoEnabled/OnSelectionChanged/DoSave`). These are filled in Tasks 3 and 4. (If executing inline, fill them fully in the same pass instead of stubbing.)

- [ ] **Step 3: Build, verify it compiles**

Run: `$HOME/dotnet/dotnet.exe build -c Debug`
Expected: `Build succeeded. 0 Error(s)`.

---

### Task 3: MainForm.Build.cs — control construction + UI sync

**Files:**
- Create: `UI/MainForm.Build.cs`

**Interfaces:**
- Produces: `_menu`, `_toolStrip`, `_statusStrip` fields; methods `BuildMenus()`, `BuildToolbar()`, `BuildStatusBar()`, `BuildPropertiesPanel()`, `SelectTool(ToolType)`, `UpdateUndoEnabled()`, `OnSelectionChanged()`. Calls command handlers defined in Task 4 (`OnNew`, `OnOpen`, `OnSave`, `OnSaveAs`, `OnImport`, `OnExport`, `OnPrint`, `OnExit`, `OnUndo`, `OnRedo`, `OnDelete`, `OnInsertText`, `OnInsertImage`, `OnInsertBlank`, nav/zoom handlers, `OnAbout`).

- [ ] **Step 1: Write the build partial**

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PdfEditor.Core;

namespace PdfEditor.UI
{
    partial class MainForm
    {
        internal MenuStrip _menu;
        internal ToolStrip _toolStrip;
        internal StatusStrip _statusStrip;

        // Properties-panel controls (shared with Commands)
        internal ComboBox _fontCombo;
        internal NumericUpDown _fontSize, _strokeWidth, _highlighterWidth;
        internal Button _btnColor, _btnFillColor, _btnApplySel;
        internal CheckBox _fillCheck;
        internal ToolTip _tips = new ToolToolTip();

        // ---- Status bar ----
        private void BuildStatusBar()
        {
            _statusStrip = new StatusStrip { BackColor = Color.FromArgb(225, 225, 228) };
            _statusPage = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _statusTool = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _statusZoom = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleRight };
            _statusStrip.Items.Add(_statusPage);
            _statusStrip.Items.Add(new ToolStripStatusLabel("|") { Spring = false });
            _statusStrip.Items.Add(_statusTool);
            _statusStrip.Items.Add(new ToolStripStatusLabel("|") { Spring = false });
            _statusStrip.Items.Add(_statusZoom);
        }

        // ---- Menus ----
        private void BuildMenus()
        {
            _menu = new MenuStrip { BackColor = Color.FromArgb(248, 248, 248) };

            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(Make("&New", Keys.Control | Keys.N, OnNew));
            file.DropDownItems.Add(Make("&Open…", Keys.Control | Keys.O, OnOpen));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Make("&Save", Keys.Control | Keys.S, OnSave));
            file.DropDownItems.Add(Make("Save &As…", Keys.Control | Keys.Shift | Keys.S, OnSaveAs));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Make("&Import…", Keys.None, OnImport));
            file.DropDownItems.Add(Make("&Export…", Keys.None, OnExport));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Make("&Print…", Keys.Control | Keys.P, OnPrint));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Make("E&xit", Keys.None, OnExit));

            var edit = new ToolStripMenuItem("&Edit");
            _miUndo = Make("&Undo", Keys.Control | Keys.Z, OnUndo);
            _miRedo = Make("&Redo", Keys.Control | Keys.Y, OnRedo);
            edit.DropDownItems.Add(_miUndo);
            edit.DropDownItems.Add(_miRedo);
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Make("&Delete", Keys.Delete, OnDelete));
            edit.DropDownItems.Add(Make("Deselect", Keys.Escape, (s, e) => SelectTool(ToolType.Select)));

            var view = new ToolStripMenuItem("&View");
            view.DropDownItems.Add(Make("First Page", Keys.Home, (s, e) => _pageView.GoToPage(0)));
            view.DropDownItems.Add(Make("Previous Page", Keys.PageUp, (s, e) => _pageView.GoToPage(_pageView.PageIndex - 1)));
            view.DropDownItems.Add(Make("Next Page", Keys.PageDown, (s, e) => _pageView.GoToPage(_pageView.PageIndex + 1)));
            view.DropDownItems.Add(Make("Last Page", Keys.End, (s, e) => _pageView.GoToPage(int.MaxValue)));
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(Make("Zoom In", Keys.Control | Keys.Oemplus, (s, e) => _pageView.ZoomIn()));
            view.DropDownItems.Add(Make("Zoom Out", Keys.Control | Keys.OemMinus, (s, e) => _pageView.ZoomOut()));
            view.DropDownItems.Add(Make("Actual Size", Keys.Control | Keys.D0, (s, e) => _pageView.ActualSize()));
            view.DropDownItems.Add(Make("Fit Width", Keys.None, (s, e) => _pageView.FitWidth(_scrollHost.ClientSize.Width)));

            var insert = new ToolStripMenuItem("&Insert");
            insert.DropDownItems.Add(Make("Text", Keys.None, OnInsertText));
            insert.DropDownItems.Add(Make("Image…", Keys.None, OnInsertImage));
            insert.DropDownItems.Add(new ToolStripSeparator());
            insert.DropDownItems.Add(Make("Blank Page", Keys.None, OnInsertBlank));

            var draw = new ToolStripMenuItem("&Draw");
            draw.DropDownItems.Add(Make("Select", Keys.V, (s, e) => SelectTool(ToolType.Select)));
            draw.DropDownItems.Add(Make("Hand", Keys.H, (s, e) => SelectTool(ToolType.Pan)));
            draw.DropDownItems.Add(new ToolStripSeparator());
            draw.DropDownItems.Add(Make("Pen", Keys.P, (s, e) => SelectTool(ToolType.Pen)));
            draw.DropDownItems.Add(Make("Highlighter", Keys.None, (s, e) => SelectTool(ToolType.Highlighter)));
            draw.DropDownItems.Add(new ToolStripSeparator());
            draw.DropDownItems.Add(Make("Rectangle", Keys.R, (s, e) => SelectTool(ToolType.Rectangle)));
            draw.DropDownItems.Add(Make("Ellipse", Keys.O, (s, e) => SelectTool(ToolType.Ellipse)));
            draw.DropDownItems.Add(Make("Line", Keys.L, (s, e) => SelectTool(ToolType.Line)));
            draw.DropDownItems.Add(Make("Arrow", Keys.A, (s, e) => SelectTool(ToolType.Arrow)));
            draw.DropDownItems.Add(new ToolStripSeparator());
            draw.DropDownItems.Add(Make("Eraser", Keys.E, (s, e) => SelectTool(ToolType.Eraser)));

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(Make("&About", Keys.None, OnAbout));

            _menu.Items.AddRange(new ToolStripItem[] { file, edit, view, insert, draw, help });
        }

        private static ToolStripMenuItem Make(string text, Keys shortcut, EventHandler onClick)
        {
            var mi = new ToolStripMenuItem(text);
            if (shortcut != Keys.None) mi.ShortcutKeys = shortcut;
            mi.Click += onClick;
            return mi;
        }

        // ---- Toolbar ----
        private void BuildToolbar()
        {
            _toolStrip = new ToolStrip { BackColor = Color.FromArgb(248, 248, 248), GripStyle = ToolStripGripStyle.Hidden };

            AddTool("↖\nSelect", "Select / move (V)", ToolType.Select);
            AddTool("✋\nHand", "Pan (H)", ToolType.Pan);
            _toolStrip.Items.Add(new ToolStripSeparator());
            AddTool("T\nText", "Text (T)", ToolType.Text);
            AddTool("✎\nPen", "Pen (P)", ToolType.Pen);
            AddTool("▮\nHigh", "Highlighter", ToolType.Highlighter);
            AddTool("▭\nRect", "Rectangle (R)", ToolType.Rectangle);
            AddTool("◯\nEllipse", "Ellipse (O)", ToolType.Ellipse);
            AddTool("╱\nLine", "Line (L)", ToolType.Line);
            AddTool("➤\nArrow", "Arrow (A)", ToolType.Arrow);
            AddTool("⌫\nErase", "Eraser (E)", ToolType.Eraser);
            _toolStrip.Items.Add(new ToolStripSeparator());

            _btnUndo = ToolBtn("↶", "Undo (Ctrl+Z)", (s, e) => OnUndo(s, e));
            _btnRedo = ToolBtn("↷", "Redo (Ctrl+Y)", (s, e) => OnRedo(s, e));
            _toolStrip.Items.Add(_btnUndo);
            _toolStrip.Items.Add(_btnRedo);
            _toolStrip.Items.Add(new ToolStripSeparator());

            _btnColorSwatch = new ToolStripButton { DisplayStyle = ToolStripItemDisplayStyle.Text, Text = "■", ToolTipText = "Color" };
            _btnColorSwatch.Click += (s, e) => PickColor();
            _toolStrip.Items.Add(_btnColorSwatch);
            _toolStrip.Items.Add(new ToolStripSeparator());

            _toolStrip.Items.Add(ToolBtn("－", "Zoom out", (s, e) => _pageView.ZoomOut()));
            _toolStrip.Items.Add(ToolBtn("＋", "Zoom in", (s, e) => _pageView.ZoomIn()));
            _toolStrip.Items.Add(ToolBtn("▦", "Fit width", (s, e) => _pageView.FitWidth(_scrollHost.ClientSize.Width)));

            SyncToolButtons(ToolType.Select);
            SyncColorSwatch();
        }

        private void AddTool(string label, string tip, ToolType t)
        {
            var b = new ToolStripButton
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Text = label,
                ToolTipText = tip,
                CheckOnClick = true,
                Tag = t,
            };
            b.Click += (s, e) => SelectTool(t);
            _toolButtons.Add(b);
            _toolStrip.Items.Add(b);
        }

        private static ToolStripButton ToolBtn(string glyph, string tip, EventHandler onClick)
        {
            var b = new ToolStripButton { DisplayStyle = ToolStripItemDisplayStyle.Text, Text = glyph, ToolTipText = tip };
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
            foreach (var b in _toolButtons) b.Checked = (ToolType)b.Tag == t;
        }

        internal void UpdateUndoEnabled()
        {
            bool canU = _model?.Undo.CanUndo ?? false;
            bool canR = _model?.Undo.CanRedo ?? false;
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

        // ---- Properties panel ----
        private Control BuildPropertiesPanel()
        {
            var panel = new Panel { Dock = DockStyle.Right, Width = 218, BackColor = Color.FromArgb(249, 249, 251), Padding = new Padding(10) };

            var lblTitle = new Label { Text = "PROPERTIES", Dock = DockStyle.Top, ForeColor = Color.FromArgb(110, 110, 120), Height = 22 };

            // Text group
            var gText = new GroupBox { Text = "Text", Dock = DockStyle.Top, Height = 110, Top = 0 };
            _fontCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Top = 0 };
            _fontCombo.Items.AddRange(new object[] { "Arial", "Times New Roman", "Courier New", "Segoe UI", "Calibri" });
            _fontCombo.SelectedItem = _settings.FontFamily;
            _fontCombo.SelectedIndexChanged += (s, e) => { _settings.FontFamily = (string)_fontCombo.SelectedItem; _pageView.ApplySettingsToSelection(); };
            _fontSize = Num(_settings.FontSizePt, 6, 96, 1);
            _fontSize.ValueChanged += (s, e) => { _settings.FontSizePt = (double)_fontSize.Value; _pageView.ApplySettingsToSelection(); };
            var fontSizeRow = Row("Size", _fontSize);
            var btnBold = Toggle("B", "Bold", new Font(Font, FontStyle.Bold), _settings.Bold, v => { _settings.Bold = v; _pageView.ApplySettingsToSelection(); });
            var btnItalic = Toggle("I", "Italic", new Font(Font, FontStyle.Italic), _settings.Italic, v => { _settings.Italic = v; _pageView.ApplySettingsToSelection(); });
            var styleRow = Row2(btnBold, btnItalic);
            gText.Controls.AddRange(new Control[] { styleRow, fontSizeRow, _fontCombo });

            // Stroke & color group
            var gStroke = new GroupBox { Text = "Stroke & Color", Dock = DockStyle.Top, Height = 190 };
            _btnColor = new Button { Text = "Color", Dock = DockStyle.Top, BackColor = _settings.Color, FlatStyle = FlatStyle.Flat };
            _btnColor.Click += (s, e) => PickColor();
            _strokeWidth = Num(_settings.StrokeWidthPt, 0.5m, 40m, 0.5m);
            _strokeWidth.ValueChanged += (s, e) => { _settings.StrokeWidthPt = (double)_strokeWidth.Value; _pageView.ApplySettingsToSelection(); };
            var strokeRow = Row("Width", _strokeWidth);
            _highlighterWidth = Num(_settings.HighlighterWidthPt, 4m, 40m, 1m);
            _highlighterWidth.ValueChanged += (s, e) => { _settings.HighlighterWidthPt = (double)_highlighterWidth.Value; };
            var highRow = Row("High", _highlighterWidth);
            _fillCheck = new CheckBox { Text = "Fill shapes", Dock = DockStyle.Top, Checked = _settings.FillShapes };
            _fillCheck.CheckedChanged += (s, e) => { _settings.FillShapes = _fillCheck.Checked; _pageView.ApplySettingsToSelection(); };
            _btnFillColor = new Button { Text = "Fill color", Dock = DockStyle.Top, BackColor = _settings.FillColor, FlatStyle = FlatStyle.Flat };
            _btnFillColor.Click += (s, e) =>
            {
                using var dlg = new ColorDialog { Color = _settings.FillColor, FullOpen = true };
                if (dlg.ShowDialog(this) == DialogResult.OK) { _settings.FillColor = dlg.Color; _btnFillColor.BackColor = dlg.Color; }
            };
            _btnApplySel = new Button { Text = "Apply to selection", Dock = DockStyle.Top, Enabled = false };
            _btnApplySel.Click += (s, e) => _pageView.ApplySettingsToSelection();
            gStroke.Controls.AddRange(new Control[] { _btnApplySel, _btnFillColor, _fillCheck, highRow, strokeRow, _btnColor });

            // Stack groups top-down with manual tops (reverse add order = Dock.Top stacking)
            panel.Controls.Add(gStroke);
            panel.Controls.Add(gText);
            panel.Controls.Add(lblTitle);
            return panel;
        }

        private static NumericUpDown Num(double value, decimal min, decimal max, decimal step)
        {
            var n = new NumericUpDown { Minimum = min, Maximum = max, Increment = step, DecimalPlaces = (step < 1 ? 1 : 0), Width = 70, Top = 0 };
            try { n.Value = (decimal)value; } catch { }
            return n;
        }

        private static Panel Row(string labelText, Control input)
        {
            var p = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(0, 4, 0, 0) };
            var l = new Label { Text = labelText, Dock = DockStyle.Left, Width = 56, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(90, 90, 95) };
            input.Dock = DockStyle.Fill;
            p.Controls.Add(input); p.Controls.Add(l);
            return p;
        }

        private static Panel Row2(Control a, Control b)
        {
            var p = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(0, 4, 0, 0) };
            a.Dock = DockStyle.Left; a.Width = 40; b.Dock = DockStyle.Left; b.Width = 40;
            p.Controls.Add(b); p.Controls.Add(a);
            return p;
        }

        private Button Toggle(string text, string tip, Font f, bool initial, Action<bool> onChange)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, Font = f, Width = 40, BackColor = initial ? Color.FromArgb(200, 220, 255) : SystemColors.Control };
            _tips.SetToolTip(b, tip);
            b.Click += (s, e) => { bool on = b.BackColor != Color.FromArgb(200, 220, 255); b.BackColor = on ? Color.FromArgb(200, 220, 255) : SystemColors.Control; onChange(on); };
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
```

Note: `ToolToolTip` is a one-line alias — add `internal class ToolToolTip : ToolTip { public ToolToolTip() {} }` at the bottom of this file, or just change the field type to `ToolTip`. (Use plain `ToolTip` to avoid the alias.)

- [ ] **Step 2: Build, verify it compiles**

Run: `$HOME/dotnet/dotnet.exe build -c Debug`
Expected: `Build succeeded. 0 Error(s)`.

---

### Task 4: MainForm.Commands.cs — all command handlers

**Files:**
- Create: `UI/MainForm.Commands.cs`

**Interfaces:**
- Produces: `OnNew`, `OnOpen`, `OnSave`, `OnSaveAs`, `OnImport`, `OnExport`, `OnPrint`, `OnExit`, `OnUndo`, `OnRedo`, `OnDelete`, `OnInsertText`, `OnInsertImage`, `OnInsertBlank`, `OnAbout`; helper `DoSave(string)`. Consumes everything from Tasks 2 & 3.

- [ ] **Step 1: Write the commands partial**

```csharp
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
            catch (Exception ex) { MessageBox.Show(this, "Could not open file:\n" + ex.Message, "Open", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void OnSave(object s, EventArgs e)
        {
            if (_model == null) return;
            if (string.IsNullOrEmpty(_model.FilePath)) OnSaveAs(s, e);
            else DoSave(_model.FilePath);
        }

        private void OnSaveAs(object s, EventArgs e)
        {
            if (_model == null) return;
            using var dlg = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = Path.GetFileName(_model.FilePath ?? "Untitled.pdf") };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            DoSave(dlg.FileName);
        }

        internal void DoSave(string path)
        {
            if (_model == null) return;
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
                Multiselect = true
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
            catch (Exception ex) { MessageBox.Show(this, "Import failed:\n" + ex.Message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            _pageView.GoToPage(start);
        }

        private void OnExport(object s, EventArgs e)
        {
            if (_model == null) return;
            using var dlg = new FolderBrowserDialog { Description = "Choose a folder to export page images into" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            string baseName = Path.GetFileNameWithoutExtension(_model.FilePath ?? "untitled");
            int ok = 0, fail = 0;
            for (int i = 0; i < _model.PageCount; i++)
            {
                try
                {
                    using var bmp = Compositor.ComposePage(_renderer, _model, i, 150.0 / 72.0, out _);
                    string pad = _model.PageCount > 1 ? "_" + i.ToString("D2") : "";
                    bmp.Save(Path.Combine(dlg.SelectedPath, baseName + pad + ".png"), ImageFormat.Png);
                    ok++;
                }
                catch { fail++; }
            }
            MessageBox.Show(this, $"Exported {ok} page(s){(fail > 0 ? $"; {fail} failed." : ".")}\n{dlg.SelectedPath}",
                "Export", MessageBoxButtons.OK, fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void OnPrint(object s, EventArgs e)
        {
            if (_model == null) return;
            var pages = new List<Bitmap>();
            try
            {
                for (int i = 0; i < _model.PageCount; i++)
                    pages.Add(Compositor.ComposePage(_renderer, _model, i, 150.0 / 72.0, out _));
            }
            catch (Exception ex)
            {
                foreach (var b in pages) b.Dispose();
                MessageBox.Show(this, "Print prepare failed:\n" + ex.Message, "Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                ev.Graphics.DrawImage(bmp, x, y, w, h);
                index++;
                ev.HasMorePages = index < pages.Count;
            };
            pd.EndPrint += (sender, ev) => { foreach (var b in pages) b.Dispose(); };

            try
            {
                using var dlg = new PrintDialog { UseEXDialog = true, Document = pd, AllowSomePages = false };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    pd.Print();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Print failed:\n" + ex.Message, "Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnExit(object s, EventArgs e) => Close();

        private void OnUndo(object s, EventArgs e) { _model?.Undo.Undo(); }
        private void OnRedo(object s, EventArgs e) { _model?.Undo.Redo(); }
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
            catch (Exception ex) { MessageBox.Show(this, "Could not load image:\n" + ex.Message, "Insert", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void OnInsertBlank(object s, EventArgs e)
        {
            if (_model == null) return;
            int at = _model.PageCount;
            try
            {
                _model.ReplaceBaseKeepingAnnotations(
                    PdfIO.AppendBlankPage(_model.BaseBytes, PdfIO.LetterWidthPt, PdfIO.LetterHeightPt));
                _pageView.GoToPage(at);
            }
            catch (Exception ex) { MessageBox.Show(this, "Insert page failed:\n" + ex.Message, "Insert", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void OnAbout(object s, EventArgs e)
        {
            MessageBox.Show(this,
                "PDF Editor\n\nA small WinForms PDF editor: annotate, draw, import, print, and export.\n" +
                "Built on PDFsharp + PDFium (Docnet).",
                "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
```

- [ ] **Step 2: Build, verify it compiles**

Run: `$HOME/dotnet/dotnet.exe build -c Debug`
Expected: `Build succeeded. 0 Error(s)`.

---

### Task 5: Verification

**Files:** none.

- [ ] **Step 1: Clean build + self-test regression**

Run:
```bash
$HOME/dotnet/dotnet.exe build -c Debug
$HOME/dotnet/dotnet.exe run -c Debug --project . -- --selftest
# then read %TEMP%\pdfeditor_selftest\result.txt
```
Expected: build 0 errors; self-test `ALL PASSED` (22 assertions).

- [ ] **Step 2: Launch smoke (manual)**

Run: `$HOME/dotnet/dotnet.exe run -c Debug --project .`
Manually confirm:
- Window shows menu bar (File/Edit/View/Insert/Draw/Help), toolbar, dark canvas with a centered blank page, right properties panel, status bar.
- Draw one of each: text, pen, highlighter, rectangle, ellipse, line, arrow; erase one; select & move one; delete one.
- Undo / Redo work and update toolbar/menu enabled state.
- Zoom in/out/fit/actual; page nav (add a blank page, then Home/End/PgUp/PgDn).
- Save, reopen the saved file (annotations flattened in).
- Import an image page; Insert→Image then click to place; Print (to a printer or PDF) ; Export to a folder and open a PNG.
- Close with unsaved changes → save prompt.

- [ ] **Step 3: Commit**

```bash
git add UI/ Core/ docs/
git commit -m "feat(ui): full MainForm shell with menus, toolbar, properties, status, print & export"
```

---

## Self-Review

- **Spec coverage:** Menu bar (all 6 menus + File submenu) → Task 3 `BuildMenus`. Toolbar (tools, undo/redo, color, zoom) → Task 3 `BuildToolbar`. Properties panel (font/size/color/stroke) → Task 3 `BuildPropertiesPanel`. Status bar (page/tool/zoom) → Task 2 + Task 3. Wiring (New/Open/Save/SaveAs/Import/Export/Print/Insert/Undo/Redo/Delete/nav/zoom) → Task 4. `AppendBlankPage` → Task 1. Keyboard shortcuts → Task 2 `ProcessCmdKey`. Close-confirm + dirty title → Task 2. All spec sections mapped.
- **Placeholder scan:** none; every step has runnable code or an exact command.
- **Type consistency:** `LoadModel`, `UpdateTitle`, `UpdateStatus`, `ConfirmDiscard`, `SelectTool`, `UpdateUndoEnabled`, `DoSave`, `OnSelectionChanged`, `_pageView`, `_settings`, `_model`, `_renderer`, `_scrollHost`, status/menu/toolbar fields — names match across all three partials. `PdfIO.AppendBlankPage`, `PdfIO.LetterWidthPt/Height` exist. `PageView` members (`CenterInParent`, `FitWidth`, `GoToPage`, `ZoomIn/Out/ActualSize`, `DeleteSelected`, `ApplySettingsToSelection`, `PendingImageBytes`, `StatusText`, `Tool`, `Zoom`, `PageIndex`, `Selected`, `SetDocument`, `ViewChanged`, `SelectionChanged`) match the existing file. `Compositor.ComposePage` signature matches.
- One known minor: `ProcessCmdKey` also handles `Keys.Control|Keys.Oemplus/OemMinus/D0` which duplicate the View-menu shortcuts — harmless (menu shortcuts are processed first; this is a fallback for keyboards where `Oemplus` doesn't map to `Add`).
