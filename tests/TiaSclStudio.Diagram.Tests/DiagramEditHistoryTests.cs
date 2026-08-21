using System;
using System.Linq;
using TiaSclStudio.Diagram.History;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Diagram.Tests
{
    /// <summary>
    /// Undo and redo are the safety net an engineer relies on while editing a
    /// live plant program. A history that drops an entry, replays the wrong
    /// one, or keeps growing until the process runs out of memory is worse than
    /// no history at all, because the user trusts it.
    /// </summary>
    public sealed class DiagramEditHistoryTests
    {
        private static DiagramProject Edited(DiagramProject project, string plantName)
        {
            var copy = DiagramProjectSnapshot.Clone(project);
            copy.Plant.Name = plantName;
            return copy;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void RejectsANonPositiveCapacity(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DiagramEditHistory(capacity));
        }

        [Fact]
        public void StartsEmpty()
        {
            var history = new DiagramEditHistory();

            Assert.False(history.CanUndo);
            Assert.False(history.CanRedo);
            Assert.Equal(0, history.UndoCount);
            Assert.Equal(0, history.RedoCount);
            Assert.Equal(string.Empty, history.UndoDescription);
            Assert.Equal(string.Empty, history.RedoDescription);
        }

        [Fact]
        public void RejectsNullArguments()
        {
            var history = new DiagramEditHistory();
            var project = ModelBuilder.Project();

            Assert.Throws<ArgumentNullException>(() => history.Record(null, project, project));
            Assert.Throws<ArgumentNullException>(() => history.Record("edit", null, project));
            Assert.Throws<ArgumentNullException>(() => history.Record("edit", project, null));
        }

        [Fact]
        public void RefusesToUndoOrRedoWhenThereIsNothingToReplay()
        {
            var history = new DiagramEditHistory();

            Assert.Throws<InvalidOperationException>(() => history.Undo());
            Assert.Throws<InvalidOperationException>(() => history.Redo());
        }

        [Fact]
        public void IgnoresAnEditThatChangedNothing()
        {
            // Clicking a node and putting it back must not consume an undo slot,
            // or a real edit gets pushed out of a bounded history for free.
            var history = new DiagramEditHistory();
            var project = DemoProjects.Valve();

            Assert.False(history.Record("no-op", project, DiagramProjectSnapshot.Clone(project)));
            Assert.False(history.CanUndo);
        }

        [Fact]
        public void RecordsARealEdit()
        {
            var history = new DiagramEditHistory();
            var before = DemoProjects.Valve();

            Assert.True(history.Record("rename", before, Edited(before, "Renamed")));
            Assert.True(history.CanUndo);
            Assert.Equal("rename", history.UndoDescription);
            Assert.Equal("rename", history.NextUndoDescription);
        }

        [Fact]
        public void UndoRestoresTheStateFromBeforeTheEdit()
        {
            var history = new DiagramEditHistory();
            var before = DemoProjects.Valve();
            history.Record("rename", before, Edited(before, "Renamed"));

            var restored = history.Undo();

            Assert.Equal("ValveDemo", restored.Plant.Name);
            Assert.False(history.CanUndo);
            Assert.True(history.CanRedo);
        }

        [Fact]
        public void RedoReappliesTheEdit()
        {
            var history = new DiagramEditHistory();
            var before = DemoProjects.Valve();
            history.Record("rename", before, Edited(before, "Renamed"));
            history.Undo();

            var restored = history.Redo();

            Assert.Equal("Renamed", restored.Plant.Name);
            Assert.True(history.CanUndo);
            Assert.False(history.CanRedo);
        }

        [Fact]
        public void UndoAndRedoWalkALongChainInBothDirections()
        {
            var history = new DiagramEditHistory();
            var current = DemoProjects.Valve();
            for (var step = 1; step <= 5; step++)
            {
                var next = Edited(current, "Step" + step);
                history.Record("step " + step, current, next);
                current = next;
            }

            for (var step = 5; step >= 1; step--)
            {
                Assert.Equal(step == 1 ? "ValveDemo" : "Step" + (step - 1), history.Undo().Plant.Name);
            }

            for (var step = 1; step <= 5; step++)
            {
                Assert.Equal("Step" + step, history.Redo().Plant.Name);
            }
        }

        [Fact]
        public void ANewEditAfterAnUndoDiscardsTheRedoBranch()
        {
            var history = new DiagramEditHistory();
            var before = DemoProjects.Valve();
            var first = Edited(before, "First");
            history.Record("first", before, first);
            history.Undo();

            history.Record("second", before, Edited(before, "Second"));

            Assert.False(history.CanRedo);
            Assert.Equal("second", history.UndoDescription);
        }

        [Fact]
        public void TheOldestEntryIsDroppedWhenCapacityIsReached()
        {
            var history = new DiagramEditHistory(3);
            var current = DemoProjects.Valve();
            for (var step = 1; step <= 5; step++)
            {
                var next = Edited(current, "Step" + step);
                history.Record("step " + step, current, next);
                current = next;
            }

            Assert.Equal(3, history.UndoCount);
            Assert.Equal("step 5", history.UndoDescription);

            history.Undo();
            history.Undo();
            var oldest = history.Undo();

            Assert.Equal("Step2", oldest.Plant.Name);
            Assert.False(history.CanUndo);
        }

        [Fact]
        public void CapacityIsNeverExceededUnderSustainedEditing()
        {
            var history = new DiagramEditHistory(8);
            var current = DemoProjects.Valve();
            for (var step = 0; step < 200; step++)
            {
                var next = Edited(current, "Step" + step);
                history.Record("step " + step, current, next);
                current = next;
                Assert.True(history.UndoCount <= history.Capacity);
            }

            Assert.Equal(8, history.UndoCount);
        }

        [Fact]
        public void ClearDropsBothDirections()
        {
            var history = new DiagramEditHistory();
            var before = DemoProjects.Valve();
            history.Record("edit", before, Edited(before, "After"));
            history.Undo();

            history.Clear();

            Assert.False(history.CanUndo);
            Assert.False(history.CanRedo);
            Assert.Equal(0, history.UndoCount);
            Assert.Equal(0, history.RedoCount);
        }

        [Fact]
        public void ARestoredProjectIsIndependentOfTheOneThatWasRecorded()
        {
            var history = new DiagramEditHistory();
            var before = DemoProjects.Valve();
            history.Record("edit", before, Edited(before, "After"));

            var restored = history.Undo();
            restored.Plant.Name = "MutatedAfterUndo";

            Assert.Equal("ValveDemo", history.Redo().Plant.Name == "After" ? "ValveDemo" : "changed");
            Assert.Equal("ValveDemo", before.Plant.Name);
        }

        [Fact]
        public void UndoAfterRedoReturnsToTheSameStateEveryTime()
        {
            var history = new DiagramEditHistory();
            var before = DemoProjects.Valve();
            history.Record("edit", before, Edited(before, "After"));

            for (var attempt = 0; attempt < 10; attempt++)
            {
                Assert.Equal("ValveDemo", history.Undo().Plant.Name);
                Assert.Equal("After", history.Redo().Plant.Name);
            }
        }

        [Fact]
        public void AStructuralEditRoundTripsThroughUndoIntact()
        {
            var history = new DiagramEditHistory();
            var before = DemoProjects.Valve();
            var after = DiagramProjectSnapshot.Clone(before);
            var removed = after.Sheets[0].Nodes.OfType<TagNode>().First();
            after.Sheets[0].Wires.RemoveAll(wire =>
                wire.SourceNodeId == removed.Id || wire.TargetNodeId == removed.Id);
            after.Sheets[0].Nodes.Remove(removed);

            history.Record("delete tag", before, after);
            var restored = history.Undo();

            Assert.Equal(before.Sheets[0].Nodes.Count, restored.Sheets[0].Nodes.Count);
            Assert.Equal(before.Sheets[0].Wires.Count, restored.Sheets[0].Wires.Count);
            Assert.Contains(restored.Sheets[0].Nodes, node => node.Id == removed.Id);
        }
    }
}
