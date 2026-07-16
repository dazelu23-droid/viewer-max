using System;
using System.Collections.Generic;
using System.Drawing;

namespace PdfEditor.Core
{
    // The in-memory document: an immutable "base" PDF (rendered for display) plus
    // an editable overlay of annotations per page. Structural changes (new pages,
    // imports) swap the base bytes; everything the user draws lives in the overlay
    // until Save flattens it into a fresh PDF.
    public sealed class DocumentModel
    {
        public byte[] BaseBytes { get; private set; }
        public List<SizeF> PageSizesPt { get; private set; } = new List<SizeF>();
        public Dictionary<int, List<Annotation>> Annotations { get; } = new Dictionary<int, List<Annotation>>();
        public string FilePath { get; set; }
        public bool IsDirty { get; set; }
        public UndoStack Undo { get; } = new UndoStack();

        public int PageCount => PageSizesPt.Count;

        // Raised when page structure changes (open/new/import) and the whole view
        // must rebuild; distinct from annotation edits which just repaint.
        public event Action StructureChanged;
        public event Action AnnotationsChanged;

        public static DocumentModel NewBlank()
        {
            var bytes = PdfIO.CreateBlank();
            var m = new DocumentModel();
            m.SetBase(bytes);
            m.IsDirty = false;
            return m;
        }

        public static DocumentModel Open(string path)
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            var m = new DocumentModel { FilePath = path };
            m.SetBase(bytes);
            m.IsDirty = false;
            return m;
        }

        public void SetBase(byte[] bytes)
        {
            BaseBytes = bytes;
            PageSizesPt = PdfIO.ReadPageSizes(bytes, out _);
            StructureChanged?.Invoke();
        }

        // Replaces the base bytes while keeping existing annotations (used when
        // appending pages, which never renumbers earlier pages).
        public void ReplaceBaseKeepingAnnotations(byte[] bytes)
        {
            BaseBytes = bytes;
            PageSizesPt = PdfIO.ReadPageSizes(bytes, out _);
            IsDirty = true;
            StructureChanged?.Invoke();
        }

        public List<Annotation> GetPageAnnotations(int pageIndex)
        {
            if (!Annotations.TryGetValue(pageIndex, out var list))
            {
                list = new List<Annotation>();
                Annotations[pageIndex] = list;
            }
            return list;
        }

        public void AddAnnotation(int pageIndex, Annotation ann)
        {
            var list = GetPageAnnotations(pageIndex);
            Undo.Push(new RelayCommand("Add " + ann.GetType().Name,
                () => { if (!list.Contains(ann)) list.Add(ann); MarkChanged(); },
                () => { list.Remove(ann); MarkChanged(); }));
        }

        public void RemoveAnnotations(int pageIndex, List<Annotation> toRemove)
        {
            if (toRemove.Count == 0) return;
            var list = GetPageAnnotations(pageIndex);
            // Capture positions so undo restores original z-order.
            var removed = new List<(int idx, Annotation ann)>();
            foreach (var a in toRemove)
            {
                int idx = list.IndexOf(a);
                if (idx >= 0) removed.Add((idx, a));
            }
            removed.Sort((x, y) => x.idx.CompareTo(y.idx));

            Undo.Push(new RelayCommand("Erase",
                () => { foreach (var a in toRemove) list.Remove(a); MarkChanged(); },
                () =>
                {
                    foreach (var (idx, ann) in removed)
                    {
                        int at = Math.Min(idx, list.Count);
                        list.Insert(at, ann);
                    }
                    MarkChanged();
                }));
        }

        private void MarkChanged()
        {
            IsDirty = true;
            AnnotationsChanged?.Invoke();
        }

        public void SaveAs(string path)
        {
            PdfIO.SaveFlattened(BaseBytes, Annotations, path);
            FilePath = path;
            IsDirty = false;
        }
    }
}
