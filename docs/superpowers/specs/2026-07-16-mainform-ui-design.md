# PDF Editor — MainForm UI & Command Wiring (Design Spec)

Date: 2026-07-16
Status: Design approved (pending spec review)

## Context

A C# WinForms PDF editor (`net8.0-windows`, WinExe). The **core pipeline is complete and
verified**: `PdfIO` (create/inspect/flatten/append), `PdfRenderer` (Docnet/PDFium raster),
`Compositor` (page → finished bitmap), `Annotations` (Text, Ink, Shape×4, Image — each with
GDI + PdfSharp draw paths), `DocumentModel` (base + per-page overlay, undo stack), and a
headless `SelfTest` that passes 20/20 assertions covering create, render, annotate, compose,
flatten, reopen, import, undo/redo, erase, and print-compose.

`UI/PageView.cs` is complete: the scrollable canvas renders the cached PDF page and paints
annotations live; it implements every tool's mouse handling (Select/move, Pan, Text inline
editor, Pen, Highlighter, Rectangle, Ellipse, Line, Arrow, Eraser, PlaceImage), selection,
double-click-to-edit text, zoom, page navigation, and the properties-panel integration hook
(`ApplySettingsToSelection`).

The **only remaining piece is `UI/MainForm.cs`**, currently a stub. It must provide the menu
bar, toolbar, right-side properties panel, and status bar described in the approved mockup,
and wire every command to the existing core + PageView APIs.

## Non-goals (YAGNI)

- No thumbnail/page strip on the left (navigate via menu + keys + status bar).
- No icon asset files — Unicode glyphs + text labels only.
- No rich-text or image-resize handles; image placement is fixed-size-on-drop (existing behavior).
- No PDF form-field or OCR support.

## Decisions (from brainstorming)

1. **Icons**: Unicode glyphs (Segoe/Unicode: ✎ pen, ▭ rect, ✕ eraser, ⤴ arrow, ✋ hand,
   “T” text, ↖ select, etc.) + text labels. No binary assets.
2. **Print + Export**: both fully wired. Print = real multi-page Windows printing. Export =
   every page → PNG into a chosen folder.
3. **Navigation**: no thumbnail strip.

## Architecture

`MainForm : Form`, hand-coded (no `.Designer.cs`), consistent with `PageView`. It owns:

| Field | Type | Lifetime |
|---|---|---|
| `_renderer` | `PdfRenderer` | App lifetime — Docnet `DocLib` is a process singleton. |
| `_model` | `DocumentModel` | Per document; `null` means no document. |
| `_settings` | `ToolSettings` | Shared between properties panel (writes) and `PageView` (reads). |
| `_pageView` | `PageView` | The canvas. |

### Layout (dock order, set bottom-up)

1. `StatusStrip` — Dock=Bottom.
2. Properties panel (`Panel`, fixed width ~210) — Dock=Right.
3. Scroll host panel (`Panel`, AutoScroll=true, dark `#525959`) — Dock=Fill. Contains `_pageView`.
4. `ToolStrip` — Dock=Top.
5. `MenuStrip` — Dock=Top.

## Menus (with shortcuts)

- **File**: New `Ctrl+N` · Open… `Ctrl+O` · ─ · Save `Ctrl+S` · Save As… `Ctrl+Shift+S` · ─ ·
  Import… · Export… · ─ · Print… `Ctrl+P` · ─ · Exit
- **Edit**: Undo `Ctrl+Z` · Redo `Ctrl+Y` · ─ · Delete `Del` · Deselect `Esc`
- **View**: First Page `Home` · Prev `PgUp` · Next `PgDn` · Last `End` · ─ · Zoom In `Ctrl++` ·
  Zoom Out `Ctrl+-` · Actual Size `Ctrl+0` · Fit Width
- **Insert**: Text · Image… · Blank Page
- **Draw**: Select `V` · Hand `H` · ─ · Pen `P` · Highlighter · ─ · Rectangle `R` · Ellipse `O` ·
  Line `L` · Arrow `A` · ─ · Eraser `E`
- **Help**: About

(Draw single-letter shortcuts and `Del`/`Esc` handled in `MainForm.OnKeyDown` / `ProcessCmdKey`,
since `PageView` has focus.)

## Toolbar

Mutually-exclusive **check** buttons for: Select, Hand, Text, Pen, Highlighter, Rectangle,
Ellipse, Line, Arrow, Eraser. Separator. Undo / Redo (disabled when stacks empty). Separator.
Color swatch button (back color = current color; opens `ColorDialog`). Separator. Zoom Out /
read-out `%` label / Zoom In / Fit. Tool buttons are checked/unchecked to mirror `_pageView.Tool`.

## Properties panel (right)

Two group boxes writing to `_settings` (the same object `PageView` reads):

- **Text**: FontFamily `ComboBox` (Arial, Times New Roman, Courier New, Segoe UI, Calibri);
  FontSize `NumericUpDown` 6–96; Bold + Italic toggle buttons.
- **Stroke & Color**: Color button (swatch + `ColorDialog`); Stroke width `NumericUpDown`
  0.5–40 (pen/shapes); Highlighter width `NumericUpDown` 4–40; Fill shapes `CheckBox` + fill
  color button. An “Apply to selection” button calls `_pageView.ApplySettingsToSelection()`.

The panel is always visible; values set defaults for the next annotation and, when a selection
exists, update it live.

## Status bar

Three labels: **Page** `Page {i+1} of {n}`, **Tool** `Tool: {name}`, **Zoom** `{pct}%`. Title
bar: `PdfEditor — {filename or "Untitled"}` + ` ●` when `_model.IsDirty`. Updated on
`PageView.ViewChanged`.

## Command wiring

| Command | Behavior |
|---|---|
| New | Confirm-discard if dirty → `DocumentModel.NewBlank()` → load. |
| Open | Confirm-discard if dirty → dialog → `DocumentModel.Open(path)` → load. |
| Save | If no path, route to Save As; else `model.SaveAs(path)`. |
| Save As | Dialog → `model.SaveAs`. |
| Import | Filter `*.pdf;*.png;*.jpg;…`; PDF → `PdfIO.AppendPdf`, image → `PdfIO.AppendImagePage`; `model.ReplaceBaseKeepingAnnotations`; jump to first new page. |
| Export | `FolderBrowserDialog`; for each page `Compositor.ComposePage` @ 150 DPI → PNG. |
| Print | `PrintDialog` + `PrintDocument`; `PrintPage` composites each page via `Compositor` at printer-resolution scaling (`e.MarginBounds` / page size in pt), multipage until done. |
| Undo / Redo | `model.Undo.Undo()/Redo()`; enabled state via `UndoStack.Changed`. |
| Delete | `_pageView.DeleteSelected()`. |
| Deselect / tool keys | `ProcessCmdKey` routes `Esc`, `Del`, `V/H/P/R/O/L/A/E`, arrows, `Home/End/PgUp/PgDn`, zoom. |
| Page nav / Zoom | `_pageView.GoToPage` / `ZoomIn/Out/ActualSize/FitWidth`. |
| Insert→Text | set tool Text. Insert→Image: open image → `PageView.PendingImageBytes` → tool PlaceImage. Insert→Blank Page: new `PdfIO.AppendBlankPage(base, wPt, hPt)` → `ReplaceBaseKeepingAnnotations`; jump to it. (Appends, so existing page indices/annotations stay valid.) |
| Closing | `FormClosing`: if dirty, `Save? (Yes/No/Cancel)`. |

**Load helper** (`LoadModel(model)`): dispose old model, set `_model`, `_pageView.SetDocument`,
`_renderer` reuse, reset title + status + undo enabled, `FitWidth` on first show.

## Error handling

- All file IO, `ColorDialog`, `PrintDocument`, and `Compositor` calls wrapped so one bad page
  or image never crashes the app; failures surface via `MessageBox`.
- `PdfSharp`/Docnet exceptions during import are reported with the file name.

## Testing

- **Headless (no display)**: extend `SelfTest` minimally only if a new pure-core path is added
  (e.g. blank-page append already covered conceptually). The core is already 20/20.
- **Build gate**: `dotnet build -c Debug` clean, `--selftest` still returns 0.
- **GUI smoke** (manual, since headless can't drive WinForms): launch, New, draw one of each
  annotation, Undo/Redo, Save, reopen, Print-preview, Export. Documented in the plan.

## Risks

- **DPI**: `ApplicationHighDpiMode=PerMonitorV2` — `PageView` already derives ppp from the
  rendered bitmap, so it is zoom-correct. Toolbar/status use logical px; fine.
- **Print scaling**: must map PDF points → printer hundredths-of-inch via `e.Graphics` page
  transform; `Compositor` gives a bitmap at a chosen ppp, drawn into `e.MarginBounds`.
- **Focus/shortcuts**: `PageView` holds focus; menu `ShortcutKeys` fire regardless, but
  single-letter tool keys need `ProcessCmdKey` on the form.
