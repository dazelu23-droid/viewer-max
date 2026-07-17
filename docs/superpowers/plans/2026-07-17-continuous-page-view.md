# Continuous Multi-Page View Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-page canvas with an editable, vertically scrolling view of every PDF page.

**Architecture:** Extract deterministic page layout and visibility calculations into a pure `PageLayout` class. Convert `PageView` into a fixed-page editable surface, then add `ContinuousDocumentView` as the document-level facade that lays out surfaces, virtualizes page bitmaps, tracks the active page, and preserves the API consumed by `MainForm`.

**Tech Stack:** C# 12, .NET 8 Windows Forms, System.Drawing, PDFsharp 6.1.1, Docnet.Core 2.6.0, existing self-test and GUI-test harnesses.

## Global Constraints

- Target `net8.0-windows`, x64, with no new NuGet dependencies.
- All pages use one shared zoom and a 20-pixel vertical gap.
- The active page is the page with the largest visible area; clicking a page activates it immediately.
- Render pages intersecting the viewport expanded vertically by one viewport height; unload all others.
- Saving, export, printing, undo/redo, menus, toolbar labels, and keyboard shortcuts retain existing behavior.
- A failed page render displays `Unable to render this page` without disabling other pages.

---

### Task 1: Deterministic Page Layout

**Files:**
- Create: `UI/PageLayout.cs`
- Modify/Test: `Core/SelfTest.cs`

**Interfaces:**
- Consumes: `IReadOnlyList<SizeF> pageSizesPt`, `double pixelsPerPoint`, `int gapPx`
- Produces: `PageLayout.Build(...) -> Rectangle[]`, `PageLayout.ActivePage(...) -> int`, and `PageLayout.VisiblePages(...) -> HashSet<int>`

- [ ] **Step 1: Write failing layout tests in `SelfTest.Run`**

```csharp
var bounds = UI.PageLayout.Build(
    new[] { new SizeF(100, 200), new SizeF(200, 100), new SizeF(100, 100) }, 2.0, 20);
Assert(bounds.Length == 3, "layout creates one bound per page");
Assert(bounds[0] == new Rectangle(100, 0, 200, 400), "layout centers first page");
Assert(bounds[1] == new Rectangle(0, 420, 400, 200), "layout stacks mixed-size second page");
Assert(bounds[2] == new Rectangle(100, 640, 200, 200), "layout stacks third page");
Assert(UI.PageLayout.ActivePage(bounds, new Rectangle(0, 390, 400, 180)) == 1,
    "active page uses largest visible area");
var visible = UI.PageLayout.VisiblePages(bounds, new Rectangle(0, 420, 400, 200), 200);
Assert(visible.SetEquals(new[] { 0, 1, 2 }), "render window includes one viewport margin");
```

- [ ] **Step 2: Build and run self-test; verify RED**

Run:

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build .\PdfEditor.csproj
& .\bin\Debug\net8.0-windows\win-x64\PdfEditor.exe --selftest
Get-Content "$env:TEMP\pdfeditor_selftest\result.txt"
```

Expected: build fails because `PageLayout` does not exist.

- [ ] **Step 3: Implement `UI/PageLayout.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace PdfEditor.UI
{
    internal static class PageLayout
    {
        internal static Rectangle[] Build(IReadOnlyList<SizeF> sizes, double ppp, int gap)
        {
            int maxWidth = sizes.Count == 0 ? 1 : sizes.Max(s => Math.Max(1, (int)Math.Round(s.Width * ppp)));
            var result = new Rectangle[sizes.Count];
            int y = 0;
            for (int i = 0; i < sizes.Count; i++)
            {
                int w = Math.Max(1, (int)Math.Round(sizes[i].Width * ppp));
                int h = Math.Max(1, (int)Math.Round(sizes[i].Height * ppp));
                result[i] = new Rectangle((maxWidth - w) / 2, y, w, h);
                y += h + gap;
            }
            return result;
        }

        internal static int ActivePage(IReadOnlyList<Rectangle> pages, Rectangle viewport)
        {
            int best = pages.Count == 0 ? -1 : 0, bestArea = -1;
            for (int i = 0; i < pages.Count; i++)
            {
                Rectangle hit = Rectangle.Intersect(pages[i], viewport);
                int area = hit.Width * hit.Height;
                if (area > bestArea) { best = i; bestArea = area; }
            }
            return best;
        }

        internal static HashSet<int> VisiblePages(IReadOnlyList<Rectangle> pages, Rectangle viewport, int margin)
        {
            var expanded = Rectangle.FromLTRB(viewport.Left, viewport.Top - margin,
                viewport.Right, viewport.Bottom + margin);
            return pages.Select((r, i) => (r, i)).Where(x => x.r.IntersectsWith(expanded))
                .Select(x => x.i).ToHashSet();
        }
    }
}
```

- [ ] **Step 4: Run self-test; verify GREEN**

Expected: build succeeds and every self-test assertion passes.

- [ ] **Step 5: Commit**

```powershell
git add UI/PageLayout.cs Core/SelfTest.cs
git commit -m "test: define continuous page layout behavior"
```

### Task 2: Fixed-Index Editable Page Surface

**Files:**
- Modify: `UI/PageView.cs`
- Modify/Test: `UI/GuiTest.cs`

**Interfaces:**
- Consumes: existing `DocumentModel`, `PdfRenderer`, `ToolSettings`, and fixed `int pageIndex`
- Produces: `PageView.PageIndex`, `SetDocument(model, renderer, pageIndex)`, `SetZoom(zoom)`, `LoadBitmap()`, `UnloadBitmap()`, `IsBitmapLoaded`, and existing editing operations

- [ ] **Step 1: Add a failing GUI assertion after inserting the blank page**

```csharp
var second = new PageView(new ToolSettings());
second.SetDocument(m, form.TestRenderer, 1);
Assert(second.PageIndex == 1, "page surface keeps its fixed page index");
second.LoadBitmap();
Assert(second.IsBitmapLoaded, "page surface loads its bitmap on demand");
second.UnloadBitmap();
Assert(!second.IsBitmapLoaded, "page surface unloads its bitmap on demand");
second.Dispose();
```

Expose `internal PdfRenderer TestRenderer => _renderer;` from `MainForm` for the real-renderer GUI test.

- [ ] **Step 2: Run GUI test; verify RED**

Expected: build fails because the indexed overload and bitmap lifecycle members do not exist.

- [ ] **Step 3: Make `PageView` a fixed page surface**

Change `SetDocument` to accept a page index and replace eager `RebuildPage()` with size-only layout plus explicit bitmap loading:

```csharp
public void SetDocument(DocumentModel model, PdfRenderer renderer, int pageIndex)
{
    DetachModel();
    _model = model;
    _renderer = renderer;
    _pageIndex = Math.Max(0, Math.Min(model.PageCount - 1, pageIndex));
    AttachModel();
    UpdateScaledSize();
}

public bool IsBitmapLoaded => _pageBitmap != null;

public void LoadBitmap()
{
    if (_pageBitmap != null || _model == null) return;
    try
    {
        _renderError = false;
        _pageBitmap = _renderer.RenderPage(_model.BaseBytes, _pageIndex, _zoom * 96.0 / 72.0);
        _ppp = _model.PageSizesPt[_pageIndex].Width > 0
            ? _pageBitmap.Width / _model.PageSizesPt[_pageIndex].Width
            : (float)(_zoom * 96.0 / 72.0);
    }
    catch { _renderError = true; }
    Invalidate();
}

public void UnloadBitmap()
{
    _pageBitmap?.Dispose();
    _pageBitmap = null;
    Invalidate();
}
```

`UpdateScaledSize` sets `Size` from `PageSizesPt[_pageIndex] * (_zoom * 96 / 72)` without rendering. Remove `GoToPage`, `FitWidth`, and centering responsibilities. Preserve all annotation operations unchanged and paint a white page plus centered `Unable to render this page` when `_renderError` is true.

- [ ] **Step 4: Run GUI and self-tests; verify GREEN**

Expected: both result logs report all passed.

- [ ] **Step 5: Commit**

```powershell
git add UI/PageView.cs UI/GuiTest.cs UI/MainForm.cs
git commit -m "refactor: make page canvas an indexed lazy surface"
```

### Task 3: Continuous Document Controller

**Files:**
- Create: `UI/ContinuousDocumentView.cs`
- Modify/Test: `UI/GuiTest.cs`

**Interfaces:**
- Consumes: `DocumentModel`, `PdfRenderer`, `ToolSettings`, and a parent `ScrollableControl`
- Produces the former document-level canvas API: `PageIndex`, `PageCount`, `Zoom`, `Selected`, `Tool`, `PendingImageBytes`, `SetDocument`, `GoToPage`, `SetZoom`, `ZoomIn`, `ZoomOut`, `ActualSize`, `FitWidth`, `DeleteSelected`, `ApplySettingsToSelection`, `StatusText`, `ViewChanged`, and `SelectionChanged`

- [ ] **Step 1: Add failing GUI assertions**

```csharp
Assert(form.TestView.PageSurfaceCount == 2, "continuous view creates one surface per page");
Assert(form.TestView.PageBounds[1].Top > form.TestView.PageBounds[0].Bottom,
    "continuous view stacks pages vertically");
form.TestView.GoToPage(1);
Assert(form.TestView.PageIndex == 1, "navigation activates requested page");
```

- [ ] **Step 2: Run GUI test; verify RED**

Expected: build fails because `ContinuousDocumentView` and its test properties do not exist.

- [ ] **Step 3: Implement `ContinuousDocumentView`**

Create a `Control` that maintains `List<PageView> _pages`, `Rectangle[] _bounds`, one shared tool/zoom/selection, and the API above. `Relayout()` calls `PageLayout.Build`, positions each surface, and sets the document control size. `RefreshViewport()` converts the scroll host's `AutoScrollPosition` into document coordinates, calls `ActivePage` and `VisiblePages`, loads included surfaces, unloads excluded surfaces, and emits `ViewChanged` only when active page changes. `GoToPage` clamps the index and sets the host's `AutoScrollPosition` to the target page top. `SetZoom` records the active page's viewport-relative offset, relayouts, restores that offset, then refreshes virtualization. `FitWidth` chooses the zoom from the widest PDF page and `(viewportWidth - 30)`.

Forward `Tool` and settings commands to all surfaces. When a surface receives focus or selection, make it active, clear selection on other surfaces, and forward `SelectionChanged`. Transfer `PendingImageBytes` to the clicked surface and clear it when placement completes. Dispose every surface and bitmap when replacing the document or disposing the controller.

- [ ] **Step 4: Run GUI and self-tests; verify GREEN**

Expected: all assertions pass, including two vertically ordered surfaces and page navigation.

- [ ] **Step 5: Commit**

```powershell
git add UI/ContinuousDocumentView.cs UI/GuiTest.cs
git commit -m "feat: add virtualized continuous document view"
```

### Task 4: Main Window Integration and End-to-End Verification

**Files:**
- Modify: `UI/MainForm.cs`
- Modify: `UI/MainForm.Build.cs`
- Modify: `UI/MainForm.Commands.cs`
- Modify/Test: `UI/GuiTest.cs`

**Interfaces:**
- Consumes: the document-level API from `ContinuousDocumentView`
- Produces: unchanged menus, toolbar, status, shortcuts, import/insert navigation, and test hooks with `internal ContinuousDocumentView TestView`

- [ ] **Step 1: Change the GUI test's expected test-view type and add viewport refresh coverage**

```csharp
form.TestView.GoToPage(0);
Assert(form.TestView.StatusText().StartsWith("Page 1 of 2"), "status reflects active visible page");
form.TestView.GoToPage(1);
Assert(form.TestView.StatusText().StartsWith("Page 2 of 2"), "status follows later page");
```

- [ ] **Step 2: Build to verify RED**

Expected: type mismatch while `MainForm` still declares `PageView`.

- [ ] **Step 3: Replace the hosted canvas**

Change the field and construction:

```csharp
private readonly ContinuousDocumentView _pageView;
internal ContinuousDocumentView TestView => _pageView;

_pageView = new ContinuousDocumentView(_settings, _scrollHost);
_scrollHost.Controls.Add(_pageView);
_scrollHost.Scroll += (s, e) => _pageView.RefreshViewport();
_scrollHost.Resize += (s, e) => _pageView.RefreshViewport();
```

Remove the old `CenterInParent` resize call. Keep all command call sites unchanged because the controller preserves their API. Ensure `LoadModel` calls `SetDocument`, `Shown` calls `FitWidth`, import/insert calls `GoToPage`, and status reads the active `PageIndex`.

- [ ] **Step 4: Run full automated verification**

```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet build .\PdfEditor.csproj
& .\bin\Debug\net8.0-windows\win-x64\PdfEditor.exe --selftest
Get-Content "$env:TEMP\pdfeditor_selftest\result.txt"
& .\bin\Debug\net8.0-windows\win-x64\PdfEditor.exe --guitest
Get-Content "$env:TEMP\pdfeditor_guitest_log\result.txt"
```

Expected: build has zero warnings/errors and both logs report all passed.

- [ ] **Step 5: Perform a visible smoke test**

Launch the app, insert two blank pages, verify all three pages appear vertically, scroll between them, draw on pages 1 and 3, change zoom, and verify the pages remain centered and editable.

- [ ] **Step 6: Commit**

```powershell
git add UI/MainForm.cs UI/MainForm.Build.cs UI/MainForm.Commands.cs UI/GuiTest.cs
git commit -m "feat: show and edit all PDF pages continuously"
```
