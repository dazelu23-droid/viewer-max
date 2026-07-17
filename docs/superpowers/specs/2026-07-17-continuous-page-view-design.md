# Continuous Multi-Page View Design

## Goal

Display every PDF page in one vertically scrollable document while preserving the existing editing tools, annotations, zoom controls, status display, and page-navigation commands.

## Current Behavior

`MainForm` hosts one `PageView`. `PageView` owns a single page index and bitmap, and `GoToPage` replaces that bitmap. This architecture allows editing one page at a time but cannot display a continuous document.

## Chosen Approach

Introduce a continuous document control that vertically lays out one editable page surface per PDF page. Render only pages near the visible viewport and release bitmap resources for distant pages. This provides continuous scrolling without making large PDFs render every page at startup or retain every full-resolution bitmap in memory.

## Components

### ContinuousDocumentView

The new document-level control owns page layout, scrolling integration, shared zoom, active-page tracking, and render virtualization. It exposes the document-level API currently consumed by `MainForm`, including document loading, page navigation, zoom, active page, status text, tool selection, and selection commands.

It calculates each page's scaled bounds from `DocumentModel.PageSizesPt`, using a fixed visual gap between pages. When the host viewport changes, it loads bitmaps for pages intersecting the viewport plus a small margin and unloads distant bitmaps.

### Editable Page Surfaces

Each page surface represents one fixed page index and handles painting and pointer interaction for that page. Existing annotation hit-testing, drawing, erasing, selection, text editing, and image placement behavior moves from the single-page state model into these page-indexed surfaces. Only one annotation is selected across the document at a time.

### MainForm Integration

`MainForm` continues to own the scroll host, renderer, model, and settings. It hosts the continuous document control instead of a single-page control.

- Scrolling updates the active page to the page with the largest visible area.
- Clicking a page makes it active immediately.
- Home and End scroll to the first and last pages.
- Page Up and Page Down scroll to the previous or next page relative to the active page.
- Insert and import operations scroll to the first newly added page.
- Zoom changes preserve the active page near the viewport rather than jumping to the document top.
- Fit Width uses the available viewport width and applies one scale to all pages.

## Data Flow

On document load, the document view reads page sizes, creates lightweight page surfaces, calculates their bounds, and requests rendering for the initial viewport. Scroll and resize events update the active page and render window. Annotation edits continue to write directly to `DocumentModel` using each surface's page index, then invalidate only the affected page.

## Resource and Error Handling

Page bitmaps are owned and disposed by their page surfaces. Replacing a document, changing zoom, unloading an off-screen page, or disposing the view releases the corresponding bitmap. A render failure affects only that page: the surface displays `Unable to render this page` while the rest of the document remains usable. A later viewport or zoom refresh may retry rendering.

## Compatibility

Saving, export, printing, undo/redo, and the PDF core remain unchanged because they already operate on the document model by page index. Menus, toolbar controls, and keyboard shortcuts retain their existing commands and labels.

## Testing

Automated tests will cover:

- Vertical bounds for mixed-size pages at a shared zoom.
- Page lookup from viewport position and largest-visible-area active-page selection.
- Navigation targets for first, previous, next, and last page.
- The render window includes visible and nearby pages but excludes distant pages.
- Zoom relayout preserves the active-page anchor.
- Existing core self-tests and GUI tests continue to pass.

A GUI smoke test will load a multi-page document, verify multiple page surfaces exist in vertical order, scroll to a later page, confirm the active page changes, and verify annotation interaction targets the correct page index.

## Scope Boundaries

This change does not add thumbnails, two-page spreads, page reordering, independent per-page zoom, or a separate preview mode.
