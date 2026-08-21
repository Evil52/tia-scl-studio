using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Diagram.Generation;
using TiaSclStudio.Diagram.History;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.App
{
    public sealed class GroupEditingLogicTests
    {
        [Fact]
        public void AddCreatesAValidNestedGroupWithoutChangingNodeGeometry()
        {
            DiagramGroup root;
            DiagramGroup child;
            TagNode rootNode;
            ConstantNode childNode;
            Wire wire;
            var project = BuildNestedProject(
                out root,
                out child,
                out rootNode,
                out childNode,
                out wire);
            var sheet = project.Sheets[0];
            var draft = GroupEditingLogic.CreateDraft(
                "Nested",
                string.Empty,
                110.0,
                120.0,
                250.0,
                150.0);

            GroupEditingLogic.Add(
                project,
                sheet.Id,
                draft,
                new[] { rootNode.Id });

            var added = sheet.Groups.Single(group => group.Id == draft.Id);
            Assert.Equal(root.Id, added.ParentGroupId);
            Assert.Equal(new[] { rootNode.Id }, added.MemberNodeIds);
            Assert.DoesNotContain(rootNode.Id, root.MemberNodeIds);
            Assert.Equal(130.0, rootNode.X);
            Assert.Equal(160.0, rootNode.Y);
        }

        [Fact]
        public void AddRejectsAChildOutsideItsParentBeforeChangingMembership()
        {
            DiagramGroup root;
            DiagramGroup child;
            TagNode rootNode;
            ConstantNode childNode;
            Wire wire;
            var project = BuildNestedProject(
                out root,
                out child,
                out rootNode,
                out childNode,
                out wire);
            var sheet = project.Sheets[0];
            var draft = GroupEditingLogic.CreateDraft(
                "Outside",
                string.Empty,
                100.0,
                100.0,
                650.0,
                200.0);
            var originalMembers = root.MemberNodeIds.ToArray();
            var originalGroupCount = sheet.Groups.Count;

            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Add(
                    project,
                    sheet.Id,
                    draft,
                    new[] { rootNode.Id }));

            Assert.Equal(originalMembers, root.MemberNodeIds);
            Assert.Equal(originalGroupCount, sheet.Groups.Count);
            Assert.DoesNotContain(sheet.Groups, group => group.Id == draft.Id);
        }

        [Fact]
        public void AddRejectsAGroupThatDoesNotContainItsSelectedMembersAtomically()
        {
            DiagramGroup root;
            DiagramGroup child;
            TagNode rootNode;
            ConstantNode childNode;
            Wire wire;
            var project = BuildNestedProject(
                out root,
                out child,
                out rootNode,
                out childNode,
                out wire);
            var sheet = project.Sheets[0];
            var draft = GroupEditingLogic.CreateDraft(
                "Misses member",
                string.Empty,
                400.0,
                200.0,
                200.0,
                150.0);
            var originalMembers = root.MemberNodeIds.ToArray();
            var originalGroupCount = sheet.Groups.Count;

            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Add(
                    project,
                    sheet.Id,
                    draft,
                    new[] { rootNode.Id }));

            Assert.Equal(originalMembers, root.MemberNodeIds);
            Assert.Equal(originalGroupCount, sheet.Groups.Count);
            Assert.DoesNotContain(sheet.Groups, group => group.Id == draft.Id);
        }

        [Fact]
        public void DraftGeometryHonorsTheSafeMaximumExtent()
        {
            var edge = GroupEditingLogic.CreateDraft(
                "Edge",
                string.Empty,
                49850.0,
                49904.0,
                150.0,
                96.0);

            Assert.Equal(50000.0, edge.X + edge.Width);
            Assert.Equal(50000.0, edge.Y + edge.Height);
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.CreateDraft(
                    "Outside X",
                    string.Empty,
                    49850.1,
                    100.0,
                    150.0,
                    96.0));
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.CreateDraft(
                    "Outside Y",
                    string.Empty,
                    100.0,
                    49904.1,
                    150.0,
                    96.0));
        }

        [Fact]
        public void AddAlsoRejectsRawDraftGeometryBeyondTheSafeMaximumAtomically()
        {
            var project = new DiagramProject();
            var sheet = new CallSheet
            {
                Name = "FC_Maximum",
                Width = 50000.0,
                Height = 50000.0
            };
            project.Sheets.Add(sheet);
            var draft = new DiagramGroup(
                "Outside",
                49900.0,
                100.0,
                150.0,
                96.0);

            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Add(
                    project,
                    sheet.Id,
                    draft,
                    Enumerable.Empty<Guid>()));

            Assert.Empty(sheet.Groups);
        }

        [Fact]
        public void GeometryEditMovesTheSubtreeResizesOnlyTheRootAndGrowsTheSheet()
        {
            DiagramGroup root;
            DiagramGroup child;
            TagNode rootNode;
            ConstantNode childNode;
            Wire wire;
            var project = BuildNestedProject(
                out root,
                out child,
                out rootNode,
                out childNode,
                out wire);
            var sheet = project.Sheets[0];
            var childWidth = child.Width;
            var childHeight = child.Height;
            var wireFingerprint = WireFingerprint(wire);

            GroupEditingLogic.EditGeometry(
                project,
                sheet.Id,
                root.Id,
                1300.0,
                900.0,
                700.0,
                500.0);

            Assert.Equal(1300.0, root.X);
            Assert.Equal(900.0, root.Y);
            Assert.Equal(700.0, root.Width);
            Assert.Equal(500.0, root.Height);
            Assert.Equal(1400.0, child.X);
            Assert.Equal(1000.0, child.Y);
            Assert.Equal(childWidth, child.Width);
            Assert.Equal(childHeight, child.Height);
            Assert.Equal(1330.0, rootNode.X);
            Assert.Equal(960.0, rootNode.Y);
            Assert.Equal(1420.0, childNode.X);
            Assert.Equal(1040.0, childNode.Y);
            Assert.True(sheet.Width >= 2000.0);
            Assert.True(sheet.Height >= 1400.0);
            Assert.Equal("Root", root.Title);
            Assert.Equal("Root comment", root.Comment);
            Assert.Equal(wireFingerprint, WireFingerprint(wire));
        }

        [Fact]
        public void ResizeBoundsChangesOnlyTheSelectedFrame()
        {
            DiagramGroup root;
            DiagramGroup child;
            TagNode rootNode;
            ConstantNode childNode;
            Wire wire;
            var project = BuildNestedProject(
                out root,
                out child,
                out rootNode,
                out childNode,
                out wire);
            var sheet = project.Sheets[0];

            GroupEditingLogic.ResizeBounds(
                project,
                sheet.Id,
                root.Id,
                80.0,
                80.0,
                720.0,
                500.0);

            Assert.Equal(80.0, root.X);
            Assert.Equal(80.0, root.Y);
            Assert.Equal(720.0, root.Width);
            Assert.Equal(500.0, root.Height);
            Assert.Equal(200.0, child.X);
            Assert.Equal(200.0, child.Y);
            Assert.Equal(130.0, rootNode.X);
            Assert.Equal(160.0, rootNode.Y);
            Assert.Equal(220.0, childNode.X);
            Assert.Equal(240.0, childNode.Y);
        }

        [Fact]
        public void GeometryRoundTripAndSingleHistoryEntryPreserveGeneratedScl()
        {
            var project = DemoProjects.Valve();
            var sheet = project.Sheets[0];
            var group = new DiagramGroup(
                "VisualArea",
                sheet.Width - 180.0,
                sheet.Height - 130.0,
                160.0,
                100.0);
            sheet.Groups.Add(group);
            var beforeProject = DiagramProjectSnapshot.Clone(project);
            var compiler = new DiagramProjectCompiler();
            var beforeCompilation = compiler.Compile(project);
            Assert.True(beforeCompilation.IsSuccess);
            var beforeSources = beforeCompilation.GeneratedSources
                .Select(source => source.FileName + "\u001f" + source.Content)
                .ToArray();

            GroupEditingLogic.ResizeBounds(
                project,
                sheet.Id,
                group.Id,
                group.X,
                group.Y,
                400.0,
                250.0);

            Assert.True(sheet.Width > beforeProject.Sheets[0].Width);
            Assert.True(sheet.Height > beforeProject.Sheets[0].Height);
            var reopened = DiagramProjectSnapshot.Clone(project);
            var reopenedGroup = reopened.Sheets[0].Groups.Single(item => item.Id == group.Id);
            Assert.Equal(400.0, reopenedGroup.Width);
            Assert.Equal(250.0, reopenedGroup.Height);
            Assert.Equal(sheet.Width, reopened.Sheets[0].Width);
            Assert.Equal(sheet.Height, reopened.Sheets[0].Height);

            var afterCompilation = compiler.Compile(reopened);
            Assert.True(afterCompilation.IsSuccess);
            Assert.Equal(
                beforeSources,
                afterCompilation.GeneratedSources
                    .Select(source => source.FileName + "\u001f" + source.Content)
                    .ToArray());

            var history = new DiagramEditHistory();
            Assert.True(history.Record("Resize visual area", beforeProject, project));
            Assert.Equal(1, history.UndoCount);
            var undone = history.Undo();
            Assert.Equal(160.0, undone.Sheets[0].Groups.Single(item => item.Id == group.Id).Width);
            var redone = history.Redo();
            Assert.Equal(400.0, redone.Sheets[0].Groups.Single(item => item.Id == group.Id).Width);
            Assert.Equal(sheet.Width, redone.Sheets[0].Width);
            Assert.Equal(sheet.Height, redone.Sheets[0].Height);
        }

        [Fact]
        public void ValidChildResizeRemainsInsideItsParent()
        {
            DiagramGroup root;
            DiagramGroup child;
            TagNode rootNode;
            ConstantNode childNode;
            Wire wire;
            var project = BuildNestedProject(
                out root,
                out child,
                out rootNode,
                out childNode,
                out wire);
            var sheet = project.Sheets[0];

            GroupEditingLogic.ResizeBounds(
                project,
                sheet.Id,
                child.Id,
                180.0,
                180.0,
                360.0,
                240.0);

            Assert.Equal(180.0, child.X);
            Assert.Equal(180.0, child.Y);
            Assert.Equal(360.0, child.Width);
            Assert.Equal(240.0, child.Height);
            Assert.Equal(100.0, root.X);
            Assert.Equal(100.0, root.Y);
            Assert.Equal(220.0, childNode.X);
            Assert.Equal(240.0, childNode.Y);
        }

        [Fact]
        public void ChildResizeOutsideItsParentIsRejectedAtomically()
        {
            DiagramGroup root;
            DiagramGroup child;
            TagNode rootNode;
            ConstantNode childNode;
            Wire wire;
            var project = BuildNestedProject(
                out root,
                out child,
                out rootNode,
                out childNode,
                out wire);
            var sheet = project.Sheets[0];
            var before = GeometryFingerprint(sheet);

            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.ResizeBounds(
                    project,
                    sheet.Id,
                    child.Id,
                    180.0,
                    180.0,
                    550.0,
                    240.0));

            Assert.Equal(before, GeometryFingerprint(sheet));
        }

        [Fact]
        public void ChildGeometryMoveOutsideItsParentIsRejectedAtomically()
        {
            DiagramGroup root;
            DiagramGroup child;
            TagNode rootNode;
            ConstantNode childNode;
            Wire wire;
            var project = BuildNestedProject(
                out root,
                out child,
                out rootNode,
                out childNode,
                out wire);
            var sheet = project.Sheets[0];
            var before = GeometryFingerprint(sheet);

            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.EditGeometry(
                    project,
                    sheet.Id,
                    child.Id,
                    500.0,
                    200.0,
                    child.Width,
                    child.Height));

            Assert.Equal(before, GeometryFingerprint(sheet));
        }

        [Fact]
        public void ResizeThatClipsADirectMemberIsRejectedAtomically()
        {
            DiagramGroup root;
            DiagramGroup child;
            TagNode rootNode;
            ConstantNode childNode;
            Wire wire;
            var project = BuildNestedProject(
                out root,
                out child,
                out rootNode,
                out childNode,
                out wire);
            var sheet = project.Sheets[0];
            var before = GeometryFingerprint(sheet);

            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.ResizeBounds(
                    project,
                    sheet.Id,
                    root.Id,
                    200.0,
                    100.0,
                    500.0,
                    400.0));

            Assert.Equal(before, GeometryFingerprint(sheet));
        }

        [Fact]
        public void ResizeThatClipsADescendantGroupIsRejectedAtomically()
        {
            DiagramGroup root;
            DiagramGroup child;
            TagNode rootNode;
            ConstantNode childNode;
            Wire wire;
            var project = BuildNestedProject(
                out root,
                out child,
                out rootNode,
                out childNode,
                out wire);
            var sheet = project.Sheets[0];
            child.X = 560.0;
            child.Width = 130.0;
            var before = GeometryFingerprint(sheet);

            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.ResizeBounds(
                    project,
                    sheet.Id,
                    root.Id,
                    100.0,
                    100.0,
                    580.0,
                    400.0));

            Assert.Equal(before, GeometryFingerprint(sheet));
        }

        public static IEnumerable<object[]> InvalidGeometryCandidates()
        {
            yield return new object[] { double.NaN, 100.0, 600.0, 400.0 };
            yield return new object[] { 100.0, double.PositiveInfinity, 600.0, 400.0 };
            yield return new object[] { -1.0, 100.0, 600.0, 400.0 };
            yield return new object[] { 100.0, -1.0, 600.0, 400.0 };
            yield return new object[] { 100.0, 100.0, 149.0, 400.0 };
            yield return new object[] { 100.0, 100.0, 600.0, 95.0 };
            yield return new object[] { 49900.0, 100.0, 150.0, 400.0 };
            yield return new object[] { 100.0, 49950.0, 600.0, 96.0 };
        }

        [Theory]
        [MemberData(nameof(InvalidGeometryCandidates))]
        public void InvalidGeometryIsRejectedWithoutPartialMutation(
            double x,
            double y,
            double width,
            double height)
        {
            DiagramGroup root;
            DiagramGroup child;
            TagNode rootNode;
            ConstantNode childNode;
            Wire wire;
            var project = BuildNestedProject(
                out root,
                out child,
                out rootNode,
                out childNode,
                out wire);
            var sheet = project.Sheets[0];
            var before = GeometryFingerprint(sheet);

            Assert.ThrowsAny<ArgumentException>(() =>
                GroupEditingLogic.EditGeometry(
                    project,
                    sheet.Id,
                    root.Id,
                    x,
                    y,
                    width,
                    height));

            Assert.Equal(before, GeometryFingerprint(sheet));
        }

        private static DiagramProject BuildNestedProject(
            out DiagramGroup root,
            out DiagramGroup child,
            out TagNode rootNode,
            out ConstantNode childNode,
            out Wire wire)
        {
            var project = new DiagramProject();
            var sheet = new CallSheet
            {
                Name = "FC_Geometry",
                Width = 1180.0,
                Height = 700.0
            };
            project.Sheets.Add(sheet);

            rootNode = new TagNode
            {
                Title = "Input",
                TagName = "Input",
                X = 130.0,
                Y = 160.0
            };
            childNode = new ConstantNode
            {
                Title = "Delay",
                Literal = "T#1s",
                X = 220.0,
                Y = 240.0
            };
            var outputPin = new Pin
            {
                NodeId = rootNode.Id,
                Name = "Value",
                Direction = PinDirection.Output,
                DataType = "Bool"
            };
            var inputPin = new Pin
            {
                NodeId = childNode.Id,
                Name = "Value",
                Direction = PinDirection.Input,
                DataType = "Bool"
            };
            rootNode.Pins.Add(outputPin);
            childNode.Pins.Add(inputPin);
            sheet.Nodes.Add(rootNode);
            sheet.Nodes.Add(childNode);

            root = new DiagramGroup("Root", 100.0, 100.0, 600.0, 400.0)
            {
                Comment = "Root comment"
            };
            root.MemberNodeIds.Add(rootNode.Id);
            child = new DiagramGroup("Child", 200.0, 200.0, 300.0, 200.0)
            {
                ParentGroupId = root.Id
            };
            child.MemberNodeIds.Add(childNode.Id);
            sheet.Groups.Add(root);
            sheet.Groups.Add(child);

            wire = new Wire
            {
                SourceNodeId = rootNode.Id,
                SourcePinId = outputPin.Id,
                TargetNodeId = childNode.Id,
                TargetPinId = inputPin.Id
            };
            sheet.Wires.Add(wire);
            return project;
        }

        private static string GeometryFingerprint(CallSheet sheet)
        {
            return string.Join(
                "|",
                new[] { sheet.Width, sheet.Height }
                    .Concat(sheet.Groups.SelectMany(group =>
                        new[] { group.X, group.Y, group.Width, group.Height }))
                    .Concat(sheet.Nodes.SelectMany(node => new[] { node.X, node.Y }))
                    .Select(value => value.ToString("R")));
        }

        private static string WireFingerprint(Wire wire)
        {
            return string.Join(
                "|",
                wire.Id,
                wire.SourceNodeId,
                wire.SourcePinId,
                wire.TargetNodeId,
                wire.TargetPinId);
        }
    }
}
