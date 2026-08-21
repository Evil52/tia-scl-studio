using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;
using Xunit;

namespace TiaSclStudio.App
{
    /// <summary>
    /// Auto-layout rewrites coordinates only. It must never invent or drop a node, a
    /// wire or a group, never change the order in which the sheet executes, and never
    /// leave the sheet half-arranged when a precondition turns out to be violated.
    /// </summary>
    public sealed class SheetAutoLayoutLogicTests
    {
        [Fact]
        public void NullArgumentsAreRejected()
        {
            var sheet = new CallSheet();

            Assert.Throws<ArgumentNullException>(() =>
                SheetAutoLayoutLogic.Apply(null, Width, Height));
            Assert.Throws<ArgumentNullException>(() =>
                SheetAutoLayoutLogic.Apply(sheet, null, Height));
            Assert.Throws<ArgumentNullException>(() =>
                SheetAutoLayoutLogic.Apply(sheet, Width, null));
        }

        [Fact]
        public void AnEmptySheetReportsThereIsNothingToArrange()
        {
            var sheet = new CallSheet { Name = "FC_Empty", Width = 1180.0, Height = 700.0 };

            var result = Layout(sheet);

            Assert.False(result.Changed);
            Assert.Equal(0, result.NodeCount);
            Assert.Equal(0, result.GroupCount);
            Assert.True(result.ExecutionOrderPreserved);
            Assert.False(result.SheetExpanded);
            Assert.Equal(1180.0, sheet.Width);
            Assert.Equal(700.0, sheet.Height);
            Assert.NotEmpty(result.Status);
        }

        [Fact]
        public void ASheetWithAMissingCollectionIsRefused()
        {
            Assert.Throws<InvalidOperationException>(() =>
                Layout(new CallSheet { Nodes = null }));
            Assert.Throws<InvalidOperationException>(() =>
                Layout(new CallSheet { Wires = null }));
            Assert.Throws<InvalidOperationException>(() =>
                Layout(new CallSheet { Groups = null }));
        }

        [Theory]
        [InlineData(0.0, 700.0)]
        [InlineData(1180.0, -1.0)]
        [InlineData(double.NaN, 700.0)]
        [InlineData(1180.0, double.PositiveInfinity)]
        public void ASheetWithoutFinitePositiveDimensionsIsRefused(double width, double height)
        {
            var sheet = new CallSheet { Width = width, Height = height };

            Assert.Throws<InvalidOperationException>(() => Layout(sheet));
        }

        [Fact]
        public void ADuplicatedNodeIdIsRefusedBeforeAnythingMoves()
        {
            var sheet = BuildChainSheet(3);
            sheet.Nodes[2].Id = sheet.Nodes[1].Id;
            var before = Fingerprint(sheet);

            Assert.Throws<InvalidOperationException>(() => Layout(sheet));

            Assert.Equal(before, Fingerprint(sheet));
        }

        [Fact]
        public void ANullItemInACollectionIsRefusedBeforeAnythingMoves()
        {
            var sheet = BuildChainSheet(2);
            sheet.Nodes.Add(null);
            var before = Fingerprint(sheet);

            Assert.Throws<InvalidOperationException>(() => Layout(sheet));

            Assert.Equal(before, Fingerprint(sheet));
        }

        [Fact]
        public void AMeasuredSizeThatIsNotUsableIsRefusedBeforeAnythingMoves()
        {
            var sheet = BuildChainSheet(2);
            var before = Fingerprint(sheet);

            Assert.Throws<InvalidOperationException>(() =>
                SheetAutoLayoutLogic.Apply(sheet, node => 0.0, Height));
            Assert.Throws<InvalidOperationException>(() =>
                SheetAutoLayoutLogic.Apply(sheet, Width, node => double.NaN));

            Assert.Equal(before, Fingerprint(sheet));
        }

        [Fact]
        public void AGroupWithUnusableGeometryIsRefusedBeforeAnythingMoves()
        {
            var sheet = BuildChainSheet(2);
            var group = new DiagramGroup("Area", 40.0, 40.0, 400.0, 300.0);
            group.MemberNodeIds.Add(sheet.Nodes[0].Id);
            sheet.Groups.Add(group);
            group.Width = 0.0;
            var before = Fingerprint(sheet);

            Assert.Throws<InvalidOperationException>(() => Layout(sheet));

            Assert.Equal(before, Fingerprint(sheet));
        }

        [Fact]
        public void AGroupPointingAtAMissingParentIsRefused()
        {
            var sheet = BuildChainSheet(2);
            var group = new DiagramGroup("Area", 40.0, 40.0, 400.0, 300.0)
            {
                ParentGroupId = Guid.NewGuid()
            };
            group.MemberNodeIds.Add(sheet.Nodes[0].Id);
            sheet.Groups.Add(group);

            Assert.Throws<InvalidOperationException>(() => Layout(sheet));
        }

        [Fact]
        public void LayoutNeitherCreatesNorRemovesAnythingOnTheSheet()
        {
            var sheet = BuildChainSheet(5);
            var nodeIds = sheet.Nodes.Select(node => node.Id).ToList();
            var wireIds = sheet.Wires.Select(wire => wire.Id).ToList();
            var pinIds = sheet.Nodes.SelectMany(node => node.Pins).Select(pin => pin.Id).ToList();

            Layout(sheet);

            Assert.Equal(nodeIds, sheet.Nodes.Select(node => node.Id).ToList());
            Assert.Equal(wireIds, sheet.Wires.Select(wire => wire.Id).ToList());
            Assert.Equal(pinIds, sheet.Nodes.SelectMany(node => node.Pins).Select(pin => pin.Id).ToList());
        }

        [Fact]
        public void TheExecutableOrderIsTheSameBeforeAndAfter()
        {
            var sheet = BuildChainSheet(6);
            var before = TopologicalSorter.Sort(sheet).OrderedNodeIds.ToList();

            var result = Layout(sheet);

            Assert.True(result.ExecutionOrderPreserved);
            Assert.Equal(before, TopologicalSorter.Sort(sheet).OrderedNodeIds.ToList());
        }

        [Fact]
        public void ASecondRunOnAnAlreadyArrangedSheetChangesNothing()
        {
            var sheet = BuildChainSheet(6);

            var first = Layout(sheet);
            var arranged = Fingerprint(sheet);
            var second = Layout(sheet);

            Assert.True(first.Changed);
            Assert.False(second.Changed);
            Assert.Equal(arranged, Fingerprint(sheet));
        }

        [Fact]
        public void TwoIdenticalSheetsAreArrangedIdentically()
        {
            var ids = Enumerable.Range(0, 5).Select(index => Guid.NewGuid()).ToList();

            var first = BuildChainSheet(5, ids);
            var second = BuildChainSheet(5, ids);
            Layout(first);
            Layout(second);

            Assert.Equal(Fingerprint(first), Fingerprint(second));
        }

        [Fact]
        public void ArrangingFromDifferentStartingCoordinatesReachesTheSameLayout()
        {
            var ids = Enumerable.Range(0, 4).Select(index => Guid.NewGuid()).ToList();
            var first = BuildChainSheet(4, ids);
            var second = BuildChainSheet(4, ids);
            foreach (var node in second.Nodes)
            {
                node.X += 137.0;
                node.Y += 211.0;
            }

            Layout(first);
            Layout(second);

            Assert.Equal(Fingerprint(first), Fingerprint(second));
        }

        [Fact]
        public void EveryNodeEndsInsideThePositiveCanvas()
        {
            var sheet = BuildChainSheet(7);

            var result = Layout(sheet);

            Assert.All(sheet.Nodes, node =>
            {
                Assert.True(node.X >= 0.0);
                Assert.True(node.Y >= 0.0);
                Assert.True(node.X + SheetEditingLogic.GetNodeWidth(node) <= result.Width);
                Assert.True(node.Y + SheetEditingLogic.GetNodeHeight(node) <= result.Height);
            });
        }

        [Fact]
        public void NoTwoNodesOverlapAfterALayout()
        {
            var sheet = BuildChainSheet(8);
            sheet.Nodes.Add(CreateNote("Standalone remark", 10.0, 10.0));

            Layout(sheet);

            var boxes = sheet.Nodes
                .Select(node => new
                {
                    node.X,
                    node.Y,
                    W = SheetEditingLogic.GetNodeWidth(node),
                    H = SheetEditingLogic.GetNodeHeight(node)
                })
                .ToList();
            for (var i = 0; i < boxes.Count; i++)
            {
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i];
                    var b = boxes[j];
                    Assert.False(
                        a.X < b.X + b.W && a.X + a.W > b.X &&
                        a.Y < b.Y + b.H && a.Y + a.H > b.Y,
                        "Auto-layout left two nodes overlapping.");
                }
            }
        }

        [Fact]
        public void TheSheetGrowsToFitButIsNeverShrunk()
        {
            var small = BuildChainSheet(8);
            small.Width = 400.0;
            small.Height = 300.0;

            var grown = Layout(small);

            Assert.True(grown.SheetExpanded);
            Assert.True(small.Width >= 400.0);
            Assert.True(small.Height >= 300.0);

            var roomy = BuildChainSheet(2);
            roomy.Width = 6000.0;
            roomy.Height = 5000.0;

            var kept = Layout(roomy);

            Assert.False(kept.SheetExpanded);
            Assert.Equal(6000.0, roomy.Width);
            Assert.Equal(5000.0, roomy.Height);
        }

        [Fact]
        public void NotesAreArrangedAsDisconnectedContent()
        {
            var sheet = BuildChainSheet(2);
            sheet.Nodes.Add(CreateNote("A remark", 0.0, 0.0));

            var result = Layout(sheet);

            Assert.Equal(3, result.NodeCount);
            Assert.Equal(1, result.DisconnectedNodeCount);
            Assert.Equal(0, result.CyclicNodeCount);
        }

        [Fact]
        public void AnUnwiredBlockCallCountsAsDisconnected()
        {
            var sheet = BuildChainSheet(2);
            sheet.Nodes.Add(CreateBlock("FB_Orphan", 0.0, 0.0));

            var result = Layout(sheet);

            Assert.Equal(1, result.DisconnectedNodeCount);
        }

        [Fact]
        public void NodesOnACycleAreCountedAndStillArranged()
        {
            var sheet = BuildChainSheet(2);
            Connect(sheet, sheet.Nodes[1], sheet.Nodes[0]);

            var result = Layout(sheet);

            Assert.Equal(2, result.CyclicNodeCount);
            Assert.Equal(2, result.NodeCount);
            Assert.True(result.ExecutionOrderPreserved);
        }

        [Fact]
        public void AGroupFrameEndsUpAroundItsMemberNodes()
        {
            var sheet = BuildChainSheet(4);
            var group = new DiagramGroup("Area", 20.0, 20.0, 400.0, 300.0);
            group.MemberNodeIds.Add(sheet.Nodes[0].Id);
            group.MemberNodeIds.Add(sheet.Nodes[1].Id);
            sheet.Groups.Add(group);

            var result = Layout(sheet);

            Assert.Equal(1, result.GroupCount);
            foreach (var memberId in group.MemberNodeIds)
            {
                var node = sheet.Nodes.Single(item => item.Id == memberId);
                Assert.True(node.X >= group.X);
                Assert.True(node.Y >= group.Y);
                Assert.True(
                    node.X + SheetEditingLogic.GetNodeWidth(node) <= group.X + group.Width);
                Assert.True(
                    node.Y + SheetEditingLogic.GetNodeHeight(node) <= group.Y + group.Height);
            }
        }

        [Fact]
        public void ANestedGroupStaysInsideItsParentFrame()
        {
            var sheet = BuildChainSheet(4);
            var parent = new DiagramGroup("Outer", 20.0, 20.0, 600.0, 400.0);
            var child = new DiagramGroup("Inner", 40.0, 40.0, 300.0, 200.0)
            {
                ParentGroupId = parent.Id
            };
            parent.MemberNodeIds.Add(sheet.Nodes[0].Id);
            child.MemberNodeIds.Add(sheet.Nodes[1].Id);
            sheet.Groups.Add(parent);
            sheet.Groups.Add(child);

            var result = Layout(sheet);

            Assert.Equal(2, result.GroupCount);
            Assert.True(child.X >= parent.X);
            Assert.True(child.Y >= parent.Y);
            Assert.True(child.X + child.Width <= parent.X + parent.Width);
            Assert.True(child.Y + child.Height <= parent.Y + parent.Height);
        }

        [Fact]
        public void AnEmptyGroupKeepsAUsableFrameOfItsOwn()
        {
            var sheet = BuildChainSheet(3);
            var group = new DiagramGroup("Reserved", 30.0, 30.0, 200.0, 150.0);
            sheet.Groups.Add(group);

            var result = Layout(sheet);

            Assert.Equal(1, result.GroupCount);
            Assert.True(group.Width > 0.0);
            Assert.True(group.Height > 0.0);
            Assert.True(group.X >= 0.0);
            Assert.True(group.Y >= 0.0);
            Assert.True(group.X + group.Width <= sheet.Width);
            Assert.True(group.Y + group.Height <= sheet.Height);
        }

        [Fact]
        public void TheReportedExtentIsTheExtentActuallyWrittenToTheSheet()
        {
            var sheet = BuildChainSheet(6);

            var result = Layout(sheet);

            Assert.Equal(sheet.Width, result.Width);
            Assert.Equal(sheet.Height, result.Height);
        }

        private static SheetAutoLayoutResult Layout(CallSheet sheet)
        {
            return SheetAutoLayoutLogic.Apply(sheet, Width, Height);
        }

        private static double Width(CallNode node)
        {
            return SheetEditingLogic.GetNodeWidth(node);
        }

        private static double Height(CallNode node)
        {
            return SheetEditingLogic.GetNodeHeight(node);
        }

        private static CallSheet BuildChainSheet(int length)
        {
            return BuildChainSheet(length, null);
        }

        /// <summary>
        /// A single chain of block calls: node 0 feeds node 1, node 1 feeds node 2 and
        /// so on, which gives a graph with exactly one topological order.
        /// </summary>
        private static CallSheet BuildChainSheet(int length, IList<Guid> ids)
        {
            var sheet = new CallSheet { Name = "FC_Layout", Width = 1180.0, Height = 700.0 };
            for (var index = 0; index < length; index++)
            {
                var node = CreateBlock(
                    "FB_Step" + index.ToString("00"),
                    60.0 + index * 37.0,
                    50.0 + index * 23.0);
                if (ids != null)
                {
                    node.Id = ids[index];
                    foreach (var pin in node.Pins)
                    {
                        pin.NodeId = node.Id;
                    }
                }

                sheet.Nodes.Add(node);
                if (index > 0)
                {
                    Connect(sheet, sheet.Nodes[index - 1], sheet.Nodes[index]);
                }
            }

            return sheet;
        }

        private static BlockCallNode CreateBlock(string title, double x, double y)
        {
            var node = new BlockCallNode
            {
                Title = title,
                InstanceName = title,
                X = x,
                Y = y
            };
            node.Pins.Add(new Pin
            {
                NodeId = node.Id,
                Name = "In",
                Direction = PinDirection.Input,
                DataType = "Bool",
                Order = 0
            });
            node.Pins.Add(new Pin
            {
                NodeId = node.Id,
                Name = "Out",
                Direction = PinDirection.Output,
                DataType = "Bool",
                Order = 1
            });
            return node;
        }

        private static NoteNode CreateNote(string text, double x, double y)
        {
            return new NoteNode { Text = text, Title = text, X = x, Y = y };
        }

        private static void Connect(CallSheet sheet, CallNode source, CallNode target)
        {
            var sourcePin = source.Pins.First(pin => pin.Direction == PinDirection.Output);
            var targetPin = target.Pins.First(pin => pin.Direction == PinDirection.Input);
            sheet.Wires.Add(new Wire
            {
                SourceNodeId = source.Id,
                SourcePinId = sourcePin.Id,
                TargetNodeId = target.Id,
                TargetPinId = targetPin.Id
            });
        }

        private static string Fingerprint(CallSheet sheet)
        {
            var nodes = string.Join(
                "|",
                sheet.Nodes.Select(node => node == null
                    ? "<null>"
                    : node.Id + ":" + node.X + ":" + node.Y + ":" + node.ManualOrder));
            var groups = string.Join(
                "|",
                sheet.Groups.Select(group => group.Id + ":" + group.X + ":" + group.Y +
                    ":" + group.Width + ":" + group.Height));
            return sheet.Width + "x" + sheet.Height + "#" + nodes + "#" + groups;
        }
    }
}
