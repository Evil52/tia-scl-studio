using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Diagram.Tests
{
    /// <summary>
    /// The sort decides the order in which blocks execute inside the generated
    /// call block. A wrong order produces SCL that compiles and then behaves
    /// incorrectly on the PLC, which is the most expensive class of defect this
    /// project can ship.
    /// </summary>
    public sealed class TopologicalSorterTests
    {
        [Fact]
        public void RejectsANullSheet()
        {
            Assert.Throws<ArgumentNullException>(() => TopologicalSorter.Sort(null));
        }

        [Fact]
        public void AnEmptySheetSortsToNothing()
        {
            var result = TopologicalSorter.Sort(ModelBuilder.Sheet());

            Assert.Empty(result.OrderedNodeIds);
            Assert.False(result.HasCycle);
        }

        [Fact]
        public void OnlyExecutableNodesTakePartInTheOrder()
        {
            var project = DemoProjects.Valve();
            var sheet = project.Sheets[0];
            ModelBuilder.PlaceNote(sheet, "note");
            ModelBuilder.PlaceConstant(sheet, "TRUE", "Bool");

            var result = TopologicalSorter.Sort(sheet);

            Assert.Equal(
                sheet.Nodes.OfType<BlockCallNode>().Select(node => node.Id).OrderBy(id => id).ToArray(),
                result.OrderedNodeIds.OrderBy(id => id).ToArray());
        }

        [Fact]
        public void ProducerRunsBeforeConsumer()
        {
            var project = DemoProjects.Chain();
            var sheet = project.Sheets[0];
            var producer = sheet.Nodes.OfType<BlockCallNode>().Single(node => node.InstanceName == "P01");
            var consumer = sheet.Nodes.OfType<BlockCallNode>().Single(node => node.InstanceName == "C01");

            var order = TopologicalSorter.Sort(sheet).OrderedNodeIds;

            Assert.True(order.IndexOf(producer.Id) < order.IndexOf(consumer.Id));
        }

        [Fact]
        public void ManualOrderWinsOverGeometryForIndependentNodes()
        {
            var block = ModelBuilder.FunctionBlock("FB_A");
            var sheet = ModelBuilder.Sheet();
            var right = ModelBuilder.PlaceBlock(sheet, block, "R", 900.0, 0.0);
            var left = ModelBuilder.PlaceBlock(sheet, block, "L", 10.0, 0.0);
            right.ManualOrder = 1;

            var order = TopologicalSorter.Sort(sheet).OrderedNodeIds;

            Assert.Equal(new[] { right.Id, left.Id }, order.ToArray());
        }

        [Fact]
        public void ANodeWithManualOrderSortsBeforeAnOtherwiseEarlierAutomaticNode()
        {
            var block = ModelBuilder.FunctionBlock("FB_A");
            var sheet = ModelBuilder.Sheet();
            var automatic = ModelBuilder.PlaceBlock(sheet, block, "Automatic", 0.0, 0.0);
            var manual = ModelBuilder.PlaceBlock(sheet, block, "Manual", 100.0, 0.0);
            manual.ManualOrder = 10;

            var order = TopologicalSorter.Sort(sheet).OrderedNodeIds;

            Assert.Equal(new[] { manual.Id, automatic.Id }, order.ToArray());
        }

        [Fact]
        public void GeometryBreaksTiesLeftToRightThenTopToBottom()
        {
            var block = ModelBuilder.FunctionBlock("FB_A");
            var sheet = ModelBuilder.Sheet();
            var bottomRight = ModelBuilder.PlaceBlock(sheet, block, "BR", 200.0, 200.0);
            var topRight = ModelBuilder.PlaceBlock(sheet, block, "TR", 200.0, 10.0);
            var left = ModelBuilder.PlaceBlock(sheet, block, "L", 10.0, 500.0);

            var order = TopologicalSorter.Sort(sheet).OrderedNodeIds;

            Assert.Equal(new[] { left.Id, topRight.Id, bottomRight.Id }, order.ToArray());
        }

        [Fact]
        public void TheOrderIsStableAcrossRepeatedRuns()
        {
            var sheet = DemoProjects.LogicTree().Sheets[0];

            var first = TopologicalSorter.Sort(sheet).OrderedNodeIds.ToArray();
            for (var attempt = 0; attempt < 20; attempt++)
            {
                Assert.Equal(first, TopologicalSorter.Sort(sheet).OrderedNodeIds.ToArray());
            }
        }

        // ---------------------------------------------------------------
        // Cycles
        // ---------------------------------------------------------------

        [Fact]
        public void ReportsATwoNodeCycleAndLeavesItOutOfTheOrder()
        {
            var input = ModelBuilder.Input("In");
            var output = ModelBuilder.Output("Out");
            var block = ModelBuilder.FunctionBlock("FB_A", input, output);
            var sheet = ModelBuilder.Sheet();
            var first = ModelBuilder.PlaceBlock(sheet, block, "A", 0.0, 0.0);
            var second = ModelBuilder.PlaceBlock(sheet, block, "B", 100.0, 0.0);
            ModelBuilder.Connect(sheet, first, "Out", second, "In");
            ModelBuilder.Connect(sheet, second, "Out", first, "In");

            var result = TopologicalSorter.Sort(sheet);

            Assert.True(result.HasCycle);
            Assert.Equal(
                new[] { first.Id, second.Id }.OrderBy(id => id).ToArray(),
                result.CyclicNodeIds.OrderBy(id => id).ToArray());
            Assert.Empty(result.OrderedNodeIds);
        }

        [Fact]
        public void ReportsASelfLoop()
        {
            var input = ModelBuilder.Input("In");
            var output = ModelBuilder.Output("Out");
            var block = ModelBuilder.FunctionBlock("FB_A", input, output);
            var sheet = ModelBuilder.Sheet();
            var node = ModelBuilder.PlaceBlock(sheet, block, "A");
            ModelBuilder.Connect(sheet, node, "Out", node, "In");

            var result = TopologicalSorter.Sort(sheet);

            Assert.Equal(new[] { node.Id }, result.CyclicNodeIds.ToArray());
        }

        /// <summary>
        /// Only the nodes inside the cycle may be blamed. Marking the healthy
        /// blocks downstream of it as cyclic would send the user hunting through
        /// the wrong part of the sheet.
        /// </summary>
        [Fact]
        public void ANodeThatMerelyFeedsOffACycleIsNotReportedAsCyclic()
        {
            var input = ModelBuilder.Input("In");
            var output = ModelBuilder.Output("Out");
            var block = ModelBuilder.FunctionBlock("FB_A", input, output);
            var sheet = ModelBuilder.Sheet();
            var first = ModelBuilder.PlaceBlock(sheet, block, "A", 0.0, 0.0);
            var second = ModelBuilder.PlaceBlock(sheet, block, "B", 100.0, 0.0);
            var downstream = ModelBuilder.PlaceBlock(sheet, block, "C", 200.0, 0.0);
            ModelBuilder.Connect(sheet, first, "Out", second, "In");
            ModelBuilder.Connect(sheet, second, "Out", first, "In");
            ModelBuilder.Connect(sheet, second, "Out", downstream, "In");

            var result = TopologicalSorter.Sort(sheet);

            Assert.DoesNotContain(downstream.Id, result.CyclicNodeIds);
            Assert.Contains(first.Id, result.CyclicNodeIds);
            Assert.Contains(second.Id, result.CyclicNodeIds);
        }

        [Fact]
        public void ReportsEveryMemberOfTwoIndependentCycles()
        {
            var input = ModelBuilder.Input("In");
            var output = ModelBuilder.Output("Out");
            var block = ModelBuilder.FunctionBlock("FB_A", input, output);
            var sheet = ModelBuilder.Sheet();
            var nodes = new List<BlockCallNode>();
            for (var index = 0; index < 4; index++)
            {
                nodes.Add(ModelBuilder.PlaceBlock(sheet, block, "N" + index, index * 100.0, 0.0));
            }

            ModelBuilder.Connect(sheet, nodes[0], "Out", nodes[1], "In");
            ModelBuilder.Connect(sheet, nodes[1], "Out", nodes[0], "In");
            ModelBuilder.Connect(sheet, nodes[2], "Out", nodes[3], "In");
            ModelBuilder.Connect(sheet, nodes[3], "Out", nodes[2], "In");

            var result = TopologicalSorter.Sort(sheet);

            Assert.Equal(4, result.CyclicNodeIds.Count);
        }

        [Fact]
        public void CyclicNodesAreReportedInTheSameDeterministicOrder()
        {
            var sheet = Deformations.DeepCycle(12);

            var first = TopologicalSorter.Sort(sheet).CyclicNodeIds.ToArray();
            var second = TopologicalSorter.Sort(sheet).CyclicNodeIds.ToArray();

            Assert.Equal(first, second);
        }

        // ---------------------------------------------------------------
        // Scale
        // ---------------------------------------------------------------

        /// <summary>
        /// A recursive depth-first walk needs one call frame per edge of the
        /// longest path. On a sheet with a long call chain that exhausts the
        /// thread stack, and a stack overflow on Windows is not an exception the
        /// editor can catch and report: the process is torn down. The walk has
        /// to run on an explicit stack, so a deep chain must merely be slow.
        /// </summary>
        [Fact]
        public void SortsAVeryDeepChainWithoutExhaustingTheCallStack()
        {
            List<BlockCallNode> nodes;
            var sheet = Deformations.DeepChain(50000, out nodes);

            var result = TopologicalSorter.Sort(sheet);

            Assert.False(result.HasCycle);
            Assert.Equal(50000, result.OrderedNodeIds.Count);
            Assert.Equal(nodes[0].Id, result.OrderedNodeIds[0]);
            Assert.Equal(nodes[nodes.Count - 1].Id, result.OrderedNodeIds[result.OrderedNodeIds.Count - 1]);
        }

        [Fact]
        public void DetectsAVeryLongCycleWithoutExhaustingTheCallStack()
        {
            var sheet = Deformations.DeepCycle(50000);

            var result = TopologicalSorter.Sort(sheet);

            Assert.True(result.HasCycle);
            Assert.Equal(50000, result.CyclicNodeIds.Count);
            Assert.Empty(result.OrderedNodeIds);
        }

        /// <summary>
        /// A dense sheet must not degrade into quadratic work. This is a coarse
        /// ceiling, not a benchmark: it exists to catch an accidental nested
        /// scan over the wire list, which is the shape of mistake that turns a
        /// one-second compile into a minute-long freeze in the editor.
        /// </summary>
        [Fact]
        public void SortingStaysWellUnderASecondForAThousandNodes()
        {
            List<BlockCallNode> nodes;
            var sheet = Deformations.DeepChain(1000, out nodes);

            var stopwatch = Stopwatch.StartNew();
            TopologicalSorter.Sort(sheet);
            stopwatch.Stop();

            Assert.True(
                stopwatch.ElapsedMilliseconds < 2000,
                "Sorting 1000 chained nodes took " + stopwatch.ElapsedMilliseconds + " ms.");
        }

        // ---------------------------------------------------------------
        // Deformed input
        // ---------------------------------------------------------------

        [Fact]
        public void SurvivesAMissingNodeCollection()
        {
            var sheet = ModelBuilder.Sheet();
            Deformations.NullNodeCollection(sheet);

            var result = TopologicalSorter.Sort(sheet);

            Assert.Empty(result.OrderedNodeIds);
        }

        [Fact]
        public void SurvivesAMissingWireCollection()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            Deformations.NullWireCollection(sheet);

            var result = TopologicalSorter.Sort(sheet);

            Assert.Single(result.OrderedNodeIds);
        }

        [Fact]
        public void SurvivesANullNodeInTheCollection()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            Deformations.InjectNullNode(sheet);

            var result = TopologicalSorter.Sort(sheet);

            Assert.Single(result.OrderedNodeIds);
        }

        [Fact]
        public void SurvivesANullWireInTheCollection()
        {
            var sheet = DemoProjects.Chain().Sheets[0];
            Deformations.InjectNullWire(sheet);

            var result = TopologicalSorter.Sort(sheet);

            Assert.Equal(2, result.OrderedNodeIds.Count);
        }

        /// <summary>
        /// Duplicate ids are a validation error reported elsewhere. Sorting is
        /// reached from public entry points that have not necessarily run that
        /// check yet, so it has to collapse the duplicate rather than throw an
        /// ArgumentException from inside a dictionary build.
        /// </summary>
        [Fact]
        public void SurvivesADuplicateNodeId()
        {
            var block = ModelBuilder.FunctionBlock("FB_A");
            var sheet = ModelBuilder.Sheet();
            ModelBuilder.PlaceBlock(sheet, block, "A", 0.0, 0.0);
            ModelBuilder.PlaceBlock(sheet, block, "B", 100.0, 0.0);
            Deformations.DuplicateNodeId(sheet);

            var result = TopologicalSorter.Sort(sheet);

            Assert.Single(result.OrderedNodeIds);
        }

        [Fact]
        public void SurvivesANodeWithAnEmptyId()
        {
            var block = ModelBuilder.FunctionBlock("FB_A");
            var sheet = ModelBuilder.Sheet();
            var node = ModelBuilder.PlaceBlock(sheet, block, "A");
            node.Id = Guid.Empty;

            var result = TopologicalSorter.Sort(sheet);

            Assert.Empty(result.OrderedNodeIds);
        }

        [Fact]
        public void IgnoresAWireWhoseEndpointsDoNotExist()
        {
            var sheet = DemoProjects.Chain().Sheets[0];
            Deformations.DanglingWireSource(sheet);

            var result = TopologicalSorter.Sort(sheet);

            Assert.Equal(2, result.OrderedNodeIds.Count);
            Assert.False(result.HasCycle);
        }

        [Fact]
        public void ParallelWiresBetweenTheSamePairAreCountedOnce()
        {
            // Two outputs of A feeding two inputs of B create two edges between
            // the same node pair. Counting both would leave B's in-degree stuck
            // above zero and silently drop it from the order.
            var block = ModelBuilder.FunctionBlock(
                "FB_A",
                ModelBuilder.Input("In1"),
                ModelBuilder.Input("In2"),
                ModelBuilder.Output("Out1"),
                ModelBuilder.Output("Out2"));
            var sheet = ModelBuilder.Sheet();
            var first = ModelBuilder.PlaceBlock(sheet, block, "A", 0.0, 0.0);
            var second = ModelBuilder.PlaceBlock(sheet, block, "B", 100.0, 0.0);
            ModelBuilder.Connect(sheet, first, "Out1", second, "In1");
            ModelBuilder.Connect(sheet, first, "Out2", second, "In2");

            var result = TopologicalSorter.Sort(sheet);

            Assert.Equal(new[] { first.Id, second.Id }, result.OrderedNodeIds.ToArray());
        }
    }
}
