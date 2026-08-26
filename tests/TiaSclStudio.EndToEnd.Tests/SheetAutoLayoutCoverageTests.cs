using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;
using Xunit;

namespace TiaSclStudio.App
{
    public sealed class SheetAutoLayoutCoverageTests
    {
        [Fact]
        public void MalformedGroupsAndWiresAreRejectedBeforeLayout()
        {
            var sheet = SheetWithNodes(2);
            sheet.Groups.Add(null);
            Assert.Throws<InvalidOperationException>(() => Layout(sheet));

            sheet = SheetWithNodes(2);
            sheet.Groups.Add(new DiagramGroup { MemberNodeIds = null });
            Assert.Throws<InvalidOperationException>(() => Layout(sheet));

            sheet = SheetWithNodes(2);
            var duplicate = new DiagramGroup("Duplicate", 0, 0, 300, 200);
            sheet.Groups.Add(duplicate);
            sheet.Groups.Add(new DiagramGroup("Duplicate", 0, 0, 300, 200) { Id = duplicate.Id });
            Assert.Throws<InvalidOperationException>(() => Layout(sheet));

            sheet = SheetWithNodes(2);
            var missing = new DiagramGroup("Missing", 0, 0, 300, 200);
            missing.MemberNodeIds.Add(Guid.NewGuid());
            sheet.Groups.Add(missing);
            Assert.Throws<InvalidOperationException>(() => Layout(sheet));

            sheet = SheetWithNodes(2);
            var first = new DiagramGroup("First", 0, 0, 300, 200);
            var second = new DiagramGroup("Second", 0, 0, 300, 200);
            first.MemberNodeIds.Add(sheet.Nodes[0].Id);
            second.MemberNodeIds.Add(sheet.Nodes[0].Id);
            sheet.Groups.Add(first);
            sheet.Groups.Add(second);
            Assert.Throws<InvalidOperationException>(() => Layout(sheet));

            sheet = SheetWithNodes(2);
            first = new DiagramGroup("First", 0, 0, 300, 200);
            second = new DiagramGroup("Second", 0, 0, 300, 200);
            first.ParentGroupId = second.Id;
            second.ParentGroupId = first.Id;
            sheet.Groups.Add(first);
            sheet.Groups.Add(second);
            Assert.Throws<InvalidOperationException>(() => Layout(sheet));

            sheet = SheetWithNodes(2);
            sheet.Wires.Add(null);
            Assert.Throws<InvalidOperationException>(() => Layout(sheet));
        }

        [Fact]
        public void DenseMainAndSpecialLanesWrapDeterministically()
        {
            var sheet = new CallSheet { Name = "FC_Dense", Width = 400, Height = 300 };
            var sink = Block("Sink");
            sheet.Nodes.Add(sink);
            for (var index = 0; index < 24; index++)
            {
                var source = Block("Source" + index);
                sheet.Nodes.Add(source);
                Connect(sheet, source, sink);
            }

            for (var index = 0; index < 24; index++)
            {
                sheet.Nodes.Add(new NoteNode { Title = "Note" + index, Text = "Note" });
            }

            var result = Layout(sheet);

            Assert.True(result.Changed);
            Assert.Equal(24, result.DisconnectedNodeCount);
            Assert.True(sheet.Nodes.Select(node => node.Y).Distinct().Count() > 2);
        }

        [Fact]
        public void EmptyGroupSubtreesWrapAndGroupedSecondPassIsStable()
        {
            var sheet = SheetWithNodes(2);
            for (var index = 0; index < 12; index++)
            {
                var parent = new DiagramGroup("Empty" + index, index * 20, 0, 700, 180);
                var child = new DiagramGroup("Child" + index, index * 20 + 20, 20, 300, 100)
                {
                    ParentGroupId = parent.Id
                };
                sheet.Groups.Add(parent);
                sheet.Groups.Add(child);
            }

            Layout(sheet);
            var first = Fingerprint(sheet);
            var second = Layout(sheet);

            Assert.False(second.Changed);
            Assert.Equal(first, Fingerprint(sheet));
            Assert.True(sheet.Groups.Select(group => group.Y).Distinct().Count() > 1);
        }

        [Fact]
        public void EveryNodeCategoryParticipatesInDisconnectedOrdering()
        {
            var sheet = new CallSheet { Name = "FC_Kinds", Width = 1180, Height = 700 };
            sheet.Nodes.Add(Tag("Source", TerminalDirection.Source));
            sheet.Nodes.Add(Tag("Sink", TerminalDirection.Sink));
            sheet.Nodes.Add(Db("DbSource", TerminalDirection.Source));
            sheet.Nodes.Add(Db("DbSink", TerminalDirection.Sink));
            sheet.Nodes.Add(Constant());
            sheet.Nodes.Add(Logic());
            sheet.Nodes.Add(Block("Call"));
            sheet.Nodes.Add(new NoteNode { Title = "Note", Text = "Note" });

            var result = Layout(sheet);

            Assert.Equal(sheet.Nodes.Count, result.DisconnectedNodeCount);
            Assert.Contains("8", result.Status);
        }

        [Fact]
        public void ExcessiveVisualSizeFailsTheSafeExtentGate()
        {
            var sheet = SheetWithNodes(1);

            Assert.Throws<InvalidOperationException>(() =>
                SheetAutoLayoutLogic.Apply(
                    sheet,
                    node => DenseNodePlacementLogic.MaximumSheetExtent,
                    node => 100));
        }

        [Fact]
        public void PrivateGeometryValidationAndRollbackDefencesFailClosed()
        {
            var nodeRecordType = Nested("NodeRecord");
            var groupPlanType = Nested("GroupPlan");
            var oldNodeType = Nested("OldNodeState");
            var oldLayoutType = Nested("OldLayoutState");
            var validate = Method("ValidatePlans");

            var badNode = New(nodeRecordType, Block("Bad"), 100.0, 80.0, 0, -1);
            nodeRecordType.GetProperty("X", Instance).SetValue(badNode, -1.0, null);
            var records = GenericList(nodeRecordType, badNode);
            var emptyGroups = GenericDictionary(groupPlanType);
            AssertInnerInvalidOperation(validate, records, emptyGroups, 1000.0, 1000.0);

            records = GenericList(nodeRecordType);
            var badPlan = New(groupPlanType, -1.0, 0.0, 100.0, 100.0);
            var groups = GenericDictionary(groupPlanType, Guid.NewGuid(), badPlan);
            AssertInnerInvalidOperation(validate, records, groups, 1000.0, 1000.0);

            var sheet = SheetWithNodes(1);
            var group = new DiagramGroup("Area", 50, 60, 300, 200);
            sheet.Groups.Add(group);
            var oldNode = New(oldNodeType, 11.0, 12.0, (int?)7);
            var oldGroup = New(groupPlanType, 21.0, 22.0, 333.0, 222.0);
            var oldNodes = GenericDictionary(oldNodeType, sheet.Nodes[0].Id, oldNode);
            var oldGroups = GenericDictionary(groupPlanType, group.Id, oldGroup);
            var oldState = New(oldLayoutType, 1234.0, 876.0, oldNodes, oldGroups);
            Method("RestoreOldState").Invoke(null, new[] { (object)sheet, oldState });

            Assert.Equal(11, sheet.Nodes[0].X);
            Assert.Equal(12, sheet.Nodes[0].Y);
            Assert.Equal(7, sheet.Nodes[0].ManualOrder);
            Assert.Equal(21, group.X);
            Assert.Equal(333, group.Width);
            Assert.Equal(1234, sheet.Width);
        }

        [Fact]
        public void PrivateLayoutHelpersCoverSingleLayerFallbackAndTransactionalRollback()
        {
            var nodeRecordType = Nested("NodeRecord");
            var groupRecordType = Nested("GroupRecord");
            var groupPlanType = Nested("GroupPlan");
            var oldNodeType = Nested("OldNodeState");
            var oldLayoutType = Nested("OldLayoutState");

            var record = New(nodeRecordType, Block("Only"), 100.0, 80.0, 0, -1);
            var layer = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(nodeRecordType));
            layer.Add(record);
            var layers = (IDictionary)Activator.CreateInstance(
                typeof(Dictionary<,>).MakeGenericType(typeof(int), layer.GetType()));
            layers.Add(0, layer);
            Method("ReduceCrossings").Invoke(null, new[] { (object)layers });

            var emptyGroup = new DiagramGroup("Fallback", 11, 12, 310, 210);
            var groupRecord = New(groupRecordType, emptyGroup);
            var groupRecords = GenericList(groupRecordType, groupRecord);
            var recordsById = (IDictionary)Activator.CreateInstance(
                typeof(Dictionary<,>).MakeGenericType(typeof(Guid), nodeRecordType));
            var plans = (IDictionary)Method("CalculateGroupPlans").Invoke(
                null,
                new object[] { groupRecords, recordsById, GenericDictionary(groupPlanType) });
            var fallbackPlan = plans[emptyGroup.Id];
            Assert.Equal(11.0, (double)groupPlanType.GetProperty("X", Instance).GetValue(fallbackPlan, null));

            var hasChanges = Method("HasChanges");
            var changedPlan = New(groupPlanType, 12.0, 12.0, 310.0, 210.0);
            var changedGroups = GenericDictionary(groupPlanType, emptyGroup.Id, changedPlan);
            var unchangedSheet = new CallSheet { Width = 1000, Height = 700 };
            unchangedSheet.Groups.Add(emptyGroup);
            Assert.True((bool)hasChanges.Invoke(
                null,
                new object[]
                {
                    unchangedSheet,
                    GenericList(nodeRecordType),
                    changedGroups,
                    new Dictionary<Guid, int>(),
                    1000.0,
                    700.0
                }));

            var transactionSheet = SheetWithNodes(2);
            transactionSheet.Nodes[0].ManualOrder = 0;
            transactionSheet.Nodes[1].ManualOrder = 1;
            var oldNodes = GenericDictionary(oldNodeType);
            oldNodes.Add(transactionSheet.Nodes[0].Id, New(oldNodeType, 0.0, 0.0, (int?)0));
            oldNodes.Add(transactionSheet.Nodes[1].Id, New(oldNodeType, 0.0, 0.0, (int?)1));
            var oldState = New(
                oldLayoutType,
                transactionSheet.Width,
                transactionSheet.Height,
                oldNodes,
                GenericDictionary(groupPlanType));
            Action reverseOrder = () =>
            {
                transactionSheet.Nodes[0].ManualOrder = 1;
                transactionSheet.Nodes[1].ManualOrder = 0;
            };
            AssertInnerInvalidOperation(
                Method("CommitPreservingExecutionOrder"),
                transactionSheet,
                transactionSheet.Nodes.Select(node => node.Id).ToList(),
                oldState,
                reverseOrder);
            Assert.Equal(0, transactionSheet.Nodes[0].ManualOrder);
            Assert.Equal(1, transactionSheet.Nodes[1].ManualOrder);
        }

        [Fact]
        public void InternalStableComparersCoverNullTiesAndBarycenterOrdering()
        {
            var nodeRecordType = Nested("NodeRecord");
            var comparerType = Nested("NodeRecordStableComparer");
            var comparer = comparerType.GetField("Instance", Static).GetValue(null);
            var compare = comparerType.GetMethod("Compare");
            var node = Block("Same");
            var first = New(nodeRecordType, node, 100.0, 80.0, 0, -1);
            var second = New(nodeRecordType, node, 100.0, 80.0, 1, -1);

            Assert.Equal(-1, compare.Invoke(comparer, new[] { null, first }));
            Assert.Equal(1, compare.Invoke(comparer, new[] { first, null }));
            Assert.True((int)compare.Invoke(comparer, new[] { first, second }) < 0);

            var scoreType = Nested("CrossingScore");
            var scoreCompare = scoreType.GetMethod("Compare", Static);
            var one = New(scoreType, first, (double?)1.0, 1);
            var two = New(scoreType, second, (double?)2.0, 0);
            var noneFirst = New(scoreType, first, (double?)null, 0);
            var noneSecond = New(scoreType, second, (double?)null, 1);
            Assert.True((int)scoreCompare.Invoke(null, new[] { one, two }) < 0);
            Assert.True((int)scoreCompare.Invoke(null, new[] { one, noneFirst }) < 0);
            Assert.True((int)scoreCompare.Invoke(null, new[] { noneFirst, one }) > 0);
            Assert.True((int)scoreCompare.Invoke(null, new[] { noneFirst, noneSecond }) < 0);
        }

        private static SheetAutoLayoutResult Layout(CallSheet sheet)
        {
            return SheetAutoLayoutLogic.Apply(sheet, Width, Height);
        }

        private static double Width(CallNode node)
        {
            return node is NoteNode ? 240 : 180;
        }

        private static double Height(CallNode node)
        {
            return node is NoteNode ? 100 : 120;
        }

        private static CallSheet SheetWithNodes(int count)
        {
            var sheet = new CallSheet { Name = "FC_Coverage", Width = 1180, Height = 700 };
            for (var index = 0; index < count; index++)
            {
                sheet.Nodes.Add(Block("Node" + index));
            }

            return sheet;
        }

        private static BlockCallNode Block(string title)
        {
            var node = new BlockCallNode { Title = title, InstanceName = title };
            node.Pins.Add(new Pin
            {
                NodeId = node.Id,
                Name = "In",
                Direction = PinDirection.Input,
                DataType = "Bool"
            });
            node.Pins.Add(new Pin
            {
                NodeId = node.Id,
                Name = "Out",
                Direction = PinDirection.Output,
                DataType = "Bool"
            });
            return node;
        }

        private static void Connect(CallSheet sheet, CallNode source, CallNode target)
        {
            sheet.Wires.Add(new Wire
            {
                SourceNodeId = source.Id,
                SourcePinId = source.Pins.Last().Id,
                TargetNodeId = target.Id,
                TargetPinId = target.Pins.First().Id
            });
        }

        private static TagNode Tag(string name, TerminalDirection direction)
        {
            var node = new TagNode
            {
                Title = name,
                TagName = name,
                DataType = "Bool",
                TerminalDirection = direction
            };
            node.Pins.Add(new Pin { NodeId = node.Id, DataType = "Bool" });
            return node;
        }

        private static DataBlockVariableNode Db(string name, TerminalDirection direction)
        {
            var node = new DataBlockVariableNode
            {
                Title = name,
                DataBlockName = name,
                VariableName = "Data",
                DataType = "Bool",
                TerminalDirection = direction
            };
            node.Pins.Add(new Pin { NodeId = node.Id, DataType = "Bool" });
            return node;
        }

        private static ConstantNode Constant()
        {
            var node = new ConstantNode { Title = "TRUE", Literal = "TRUE", DataType = "Bool" };
            node.Pins.Add(new Pin { NodeId = node.Id, DataType = "Bool" });
            return node;
        }

        private static LogicNode Logic()
        {
            return DiagramNodeFactory.CreateLogic(LogicOperation.And, "Bool", 0, 0);
        }

        private static string Fingerprint(CallSheet sheet)
        {
            return string.Join("|", sheet.Nodes.Select(node => node.X + ":" + node.Y)) + "#" +
                string.Join("|", sheet.Groups.Select(group =>
                    group.X + ":" + group.Y + ":" + group.Width + ":" + group.Height));
        }

        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static Type Nested(string name)
        {
            return typeof(SheetAutoLayoutLogic).GetNestedType(name, BindingFlags.NonPublic);
        }

        private static MethodInfo Method(string name)
        {
            return typeof(SheetAutoLayoutLogic).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        }

        private static object New(Type type, params object[] arguments)
        {
            return type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single()
                .Invoke(arguments);
        }

        private static IList GenericList(Type itemType, params object[] values)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
            foreach (var value in values) list.Add(value);
            return list;
        }

        private static IDictionary GenericDictionary(Type valueType)
        {
            return (IDictionary)Activator.CreateInstance(
                typeof(Dictionary<,>).MakeGenericType(typeof(Guid), valueType));
        }

        private static IDictionary GenericDictionary(Type valueType, Guid key, object value)
        {
            var dictionary = GenericDictionary(valueType);
            dictionary.Add(key, value);
            return dictionary;
        }

        private static void AssertInnerInvalidOperation(MethodInfo method, params object[] arguments)
        {
            var exception = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(null, arguments));
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }
    }
}
