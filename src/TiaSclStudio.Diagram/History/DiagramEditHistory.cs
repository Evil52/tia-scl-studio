using System;
using System.Collections.Generic;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.Diagram.History
{
    /// <summary>
    /// Bounded undo/redo history for complete diagram project edits.
    /// </summary>
    public sealed class DiagramEditHistory
    {
        public const int DefaultCapacity = 64;

        private readonly int _capacity;
        private readonly List<HistoryEntry> _undoEntries;
        private readonly List<HistoryEntry> _redoEntries;

        public DiagramEditHistory()
            : this(DefaultCapacity)
        {
        }

        public DiagramEditHistory(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException("capacity", "History capacity must be greater than zero.");
            }

            _capacity = capacity;
            _undoEntries = new List<HistoryEntry>(capacity);
            _redoEntries = new List<HistoryEntry>(capacity);
        }

        public int Capacity
        {
            get { return _capacity; }
        }

        public int UndoCount
        {
            get { return _undoEntries.Count; }
        }

        public int RedoCount
        {
            get { return _redoEntries.Count; }
        }

        public bool CanUndo
        {
            get { return _undoEntries.Count != 0; }
        }

        public bool CanRedo
        {
            get { return _redoEntries.Count != 0; }
        }

        public string UndoDescription
        {
            get { return CanUndo ? _undoEntries[_undoEntries.Count - 1].Description : string.Empty; }
        }

        public string RedoDescription
        {
            get { return CanRedo ? _redoEntries[_redoEntries.Count - 1].Description : string.Empty; }
        }

        public string NextUndoDescription
        {
            get { return UndoDescription; }
        }

        public string NextRedoDescription
        {
            get { return RedoDescription; }
        }

        /// <summary>
        /// Records an edit. Returns false when the before and after states are identical.
        /// </summary>
        public bool Record(string description, DiagramProject before, DiagramProject after)
        {
            if (description == null)
            {
                throw new ArgumentNullException("description");
            }

            if (before == null)
            {
                throw new ArgumentNullException("before");
            }

            if (after == null)
            {
                throw new ArgumentNullException("after");
            }

            var beforeSnapshot = DiagramProjectSnapshot.Capture(before);
            var afterSnapshot = DiagramProjectSnapshot.Capture(after);

            if (beforeSnapshot.HasSameContent(afterSnapshot))
            {
                return false;
            }

            _redoEntries.Clear();
            _undoEntries.Add(new HistoryEntry(description, beforeSnapshot, afterSnapshot));
            if (_undoEntries.Count > _capacity)
            {
                _undoEntries.RemoveAt(0);
            }

            return true;
        }

        public DiagramProject Undo()
        {
            if (!CanUndo)
            {
                throw new InvalidOperationException("There is no diagram edit to undo.");
            }

            var lastIndex = _undoEntries.Count - 1;
            var entry = _undoEntries[lastIndex];
            _undoEntries.RemoveAt(lastIndex);
            _redoEntries.Add(entry);
            return entry.Before.Restore();
        }

        public DiagramProject Redo()
        {
            if (!CanRedo)
            {
                throw new InvalidOperationException("There is no diagram edit to redo.");
            }

            var lastIndex = _redoEntries.Count - 1;
            var entry = _redoEntries[lastIndex];
            _redoEntries.RemoveAt(lastIndex);
            _undoEntries.Add(entry);
            return entry.After.Restore();
        }

        public void Clear()
        {
            _undoEntries.Clear();
            _redoEntries.Clear();
        }

        private sealed class HistoryEntry
        {
            public HistoryEntry(
                string description,
                DiagramProjectSnapshot before,
                DiagramProjectSnapshot after)
            {
                Description = description;
                Before = before;
                After = after;
            }

            public string Description { get; private set; }

            public DiagramProjectSnapshot Before { get; private set; }

            public DiagramProjectSnapshot After { get; private set; }
        }
    }
}
