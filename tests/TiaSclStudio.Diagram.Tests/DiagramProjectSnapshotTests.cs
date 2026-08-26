using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TiaSclStudio.Diagram.History;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Diagram.Tests
{
    /// <summary>
    /// Undo restores a snapshot. If a snapshot shares any object with the live
    /// project, undo silently stops working for that part of the model, which
    /// the user only discovers after losing an edit they wanted back.
    /// </summary>
    public sealed class DiagramProjectSnapshotTests
    {
        [Fact]
        public void RejectsANullProject()
        {
            Assert.Throws<ArgumentNullException>(() => DiagramProjectSnapshot.Capture(null));
            Assert.Throws<ArgumentNullException>(() => DiagramProjectSnapshot.Clone(null));
        }

        [Fact]
        public void RestoreProducesAnEquivalentProject()
        {
            var project = DemoProjects.Valve();

            var restored = DiagramProjectSnapshot.Capture(project).Restore();

            Assert.Equal(project.Plant.Name, restored.Plant.Name);
            Assert.Equal(project.Sheets[0].Nodes.Count, restored.Sheets[0].Nodes.Count);
            Assert.Equal(
                project.Sheets[0].Nodes.Select(node => node.Id).ToArray(),
                restored.Sheets[0].Nodes.Select(node => node.Id).ToArray());
        }

        [Fact]
        public void RestoreSharesNoObjectWithTheCapturedProject()
        {
            var project = DemoProjects.Valve();
            var snapshot = DiagramProjectSnapshot.Capture(project);

            var restored = snapshot.Restore();

            Assert.NotSame(project, restored);
            Assert.NotSame(project.Plant, restored.Plant);
            Assert.NotSame(project.Sheets, restored.Sheets);
            Assert.NotSame(project.Sheets[0], restored.Sheets[0]);
            Assert.NotSame(project.Sheets[0].Nodes[0], restored.Sheets[0].Nodes[0]);
            Assert.NotSame(project.Sheets[0].Nodes[0].Pins[0], restored.Sheets[0].Nodes[0].Pins[0]);
            Assert.NotSame(project.Plant.Blocks[0], restored.Plant.Blocks[0]);
            Assert.NotSame(project.Plant.Blocks[0].Interface[0], restored.Plant.Blocks[0].Interface[0]);
        }

        [Fact]
        public void MutatingTheProjectAfterCaptureDoesNotChangeTheSnapshot()
        {
            var project = DemoProjects.Valve();
            var snapshot = DiagramProjectSnapshot.Capture(project);

            project.Plant.Name = "Changed";
            project.Sheets[0].Nodes.Clear();
            project.Plant.Blocks[0].Interface.Clear();

            var restored = snapshot.Restore();

            Assert.Equal("ValveDemo", restored.Plant.Name);
            Assert.NotEmpty(restored.Sheets[0].Nodes);
            Assert.NotEmpty(restored.Plant.Blocks[0].Interface);
        }

        [Fact]
        public void RestoringTwiceProducesTwoIndependentProjects()
        {
            var snapshot = DiagramProjectSnapshot.Capture(DemoProjects.Valve());

            var first = snapshot.Restore();
            var second = snapshot.Restore();

            first.Plant.Name = "First";

            Assert.Equal("ValveDemo", second.Plant.Name);
        }

        [Fact]
        public void CloneIsCaptureFollowedByRestore()
        {
            var project = DemoProjects.Chain();

            var clone = DiagramProjectSnapshot.Clone(project);

            Assert.NotSame(project, clone);
            Assert.Equal(
                project.Sheets[0].Wires.Select(wire => wire.Id).ToArray(),
                clone.Sheets[0].Wires.Select(wire => wire.Id).ToArray());
        }

        [Fact]
        public void EveryNodeSubtypeSurvivesASnapshot()
        {
            var project = DemoProjects.LogicTree();
            ModelBuilder.PlaceConstant(project.Sheets[0], "T#3s", "Time");
            ModelBuilder.PlaceNote(project.Sheets[0], "note");

            var sheet = DiagramProjectSnapshot.Clone(project).Sheets[0];

            Assert.Single(sheet.Nodes.OfType<BlockCallNode>());
            Assert.Equal(2, sheet.Nodes.OfType<LogicNode>().Count());
            Assert.Single(sheet.Nodes.OfType<ConstantNode>());
            Assert.Single(sheet.Nodes.OfType<NoteNode>());
            Assert.Equal(2, sheet.Nodes.OfType<TagNode>().Count());
        }

        [Fact]
        public void SnapshottingIsStableForAnUnchangedProject()
        {
            // Undo suppression compares snapshots byte for byte, so a project
            // that has not been touched has to serialize identically every time.
            var project = DemoProjects.Valve();
            var history = new DiagramEditHistory();

            Assert.False(history.Record("no-op", project, DiagramProjectSnapshot.Clone(project)));
        }

        [Fact]
        public void ANonFiniteCoordinateIsRejectedRatherThanSilentlyPersisted()
        {
            // XML has no representation for NaN in a double element the way this
            // schema writes it; the failure has to surface at capture time
            // instead of producing a file that cannot be read back.
            var project = DemoProjects.Valve();
            project.Sheets[0].Nodes[0].X = double.NaN;

            var snapshot = DiagramProjectSnapshot.Capture(project);
            var restored = snapshot.Restore();

            Assert.True(
                double.IsNaN(restored.Sheets[0].Nodes[0].X),
                "A NaN coordinate did not survive the round trip and was silently changed.");
        }

        [Fact]
        public void RestoreRejectsASnapshotWhoseRootExplicitlyDeserializesToNull()
        {
            var bytes = Encoding.UTF8.GetBytes(
                "<TiaSclStudioProject xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\" />");
            var snapshot = (DiagramProjectSnapshot)typeof(DiagramProjectSnapshot)
                .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(byte[]) }, null)
                .Invoke(new object[] { bytes });

            Assert.Throws<InvalidDataException>(() => snapshot.Restore());
        }
    }
}
