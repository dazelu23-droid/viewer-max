using System;
using System.Collections.Generic;

namespace PdfEditor.Core
{
    // A minimal command-based undo/redo. Each edit knows how to apply and revert
    // itself, which keeps memory low compared to snapshotting the whole document.
    public interface IUndoableCommand
    {
        void Do();
        void Undo();
        string Label { get; }
    }

    public sealed class RelayCommand : IUndoableCommand
    {
        private readonly Action _do;
        private readonly Action _undo;
        public string Label { get; }
        public RelayCommand(string label, Action doIt, Action undoIt)
        {
            Label = label; _do = doIt; _undo = undoIt;
        }
        public void Do() => _do();
        public void Undo() => _undo();
    }

    public sealed class UndoStack
    {
        private readonly Stack<IUndoableCommand> _undo = new Stack<IUndoableCommand>();
        private readonly Stack<IUndoableCommand> _redo = new Stack<IUndoableCommand>();

        public event Action Changed;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        // Runs the command for the first time and records it for later undo.
        public void Push(IUndoableCommand cmd)
        {
            cmd.Do();
            _undo.Push(cmd);
            _redo.Clear();
            Changed?.Invoke();
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            var cmd = _undo.Pop();
            cmd.Undo();
            _redo.Push(cmd);
            Changed?.Invoke();
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            var cmd = _redo.Pop();
            cmd.Do();
            _undo.Push(cmd);
            Changed?.Invoke();
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            Changed?.Invoke();
        }
    }
}
