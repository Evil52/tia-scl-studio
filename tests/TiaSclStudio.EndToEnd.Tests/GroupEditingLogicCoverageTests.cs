using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.App
{
    public sealed class GroupEditingLogicCoverageTests
    {
        [Fact]
        public void AddRejectsNullEmptyDuplicateAndMissingSelections()
        {
            var project = ModelBuilder.Project();
            var sheet = project.Sheets[0];
            Assert.Throws<ArgumentNullException>(() =>
                GroupEditingLogic.Add(project, sheet.Id, null, new Guid[0]));

            var empty = GroupEditingLogic.CreateDraft("Empty", "", 0, 0, 200, 120);
            empty.Id = Guid.Empty;
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Add(project, sheet.Id, empty, new Guid[0]));

            var duplicate = GroupEditingLogic.CreateDraft("Duplicate", "", 0, 0, 200, 120);
            duplicate.Id = project.Plant.Id;
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Add(project, sheet.Id, duplicate, new Guid[0]));

            var missing = GroupEditingLogic.CreateDraft("Missing", "", 0, 0, 200, 120);
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Add(project, sheet.Id, missing, new[] { Guid.NewGuid() }));
        }

        [Fact]
        public void AddRejectsMembersWithDifferentOrAmbiguousOwners()
        {
            var project = ModelBuilder.Project();
            var sheet = project.Sheets[0];
            var first = ModelBuilder.PlaceNote(sheet, "A", 20, 20);
            var second = ModelBuilder.PlaceNote(sheet, "B", 300, 20);
            var firstGroup = new DiagramGroup("First", 0, 0, 250, 200);
            var secondGroup = new DiagramGroup("Second", 250, 0, 250, 200);
            firstGroup.MemberNodeIds.Add(first.Id);
            secondGroup.MemberNodeIds.Add(second.Id);
            sheet.Groups.Add(firstGroup);
            sheet.Groups.Add(secondGroup);
            var draft = GroupEditingLogic.CreateDraft("Mixed", "", 0, 0, 500, 200);

            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Add(project, sheet.Id, draft, new[] { first.Id, second.Id }));

            secondGroup.MemberNodeIds.Clear();
            secondGroup.Id = firstGroup.Id;
            secondGroup.MemberNodeIds.Add(first.Id);
            var ambiguous = GroupEditingLogic.CreateDraft("Ambiguous", "", 0, 0, 250, 200);
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Add(project, sheet.Id, ambiguous, new[] { first.Id }));
        }

        [Fact]
        public void EditUngroupRemoveAndMoveExerciseTheCompletePublicLifecycle()
        {
            var project = ModelBuilder.Project();
            var sheet = project.Sheets[0];
            var first = ModelBuilder.PlaceNote(sheet, "A", 30, 30);
            var second = ModelBuilder.PlaceNote(sheet, "B", 80, 80);
            var parent = new DiagramGroup("Parent", 0, 0, 500, 400);
            var child = new DiagramGroup("Child", 20, 20, 300, 250) { ParentGroupId = parent.Id };
            var grandchild = new DiagramGroup("Grandchild", 40, 40, 200, 150) { ParentGroupId = child.Id };
            child.MemberNodeIds.Add(first.Id);
            grandchild.MemberNodeIds.Add(second.Id);
            sheet.Groups.Add(parent);
            sheet.Groups.Add(child);
            sheet.Groups.Add(grandchild);

            GroupEditingLogic.Edit(project, sheet.Id, child.Id, "  Renamed  ", "comment");
            Assert.Equal("Renamed", child.Title);
            Assert.Equal("comment", child.Comment);

            GroupEditingLogic.Move(project, sheet.Id, child.Id, 10, 15);
            Assert.Equal(30, child.X);
            Assert.Equal(35, child.Y);
            Assert.Equal(40, first.X);
            Assert.Equal(45, first.Y);

            GroupEditingLogic.Ungroup(project, sheet.Id, child.Id);
            Assert.Equal(parent.Id, grandchild.ParentGroupId);
            Assert.Contains(first.Id, parent.MemberNodeIds);
            Assert.DoesNotContain(child, sheet.Groups);

            GroupEditingLogic.RemoveNodesFromGroups(sheet, new HashSet<Guid> { first.Id });
            Assert.DoesNotContain(first.Id, parent.MemberNodeIds);
            GroupEditingLogic.RemoveNodesFromGroups(null, new HashSet<Guid> { first.Id });
            GroupEditingLogic.RemoveNodesFromGroups(sheet, null);
            GroupEditingLogic.RemoveNodesFromGroups(sheet, new HashSet<Guid>());
        }

        [Fact]
        public void UngroupRejectsAnOrphanedParentAndMoveRejectsNonFiniteDeltas()
        {
            var project = ModelBuilder.Project();
            var sheet = project.Sheets[0];
            var orphan = new DiagramGroup("Orphan", 0, 0, 200, 120)
            {
                ParentGroupId = Guid.NewGuid()
            };
            sheet.Groups.Add(orphan);

            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Ungroup(project, sheet.Id, orphan.Id));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GroupEditingLogic.Move(project, sheet.Id, orphan.Id, double.NaN, 0));
            Assert.Throws<ArgumentNullException>(() =>
                GroupEditingLogic.GetSubtreeGroupIds(null, orphan.Id));
        }

        [Fact]
        public void GeometryRejectsDuplicateSubtreeIdsMissingMembersParentsAndInvalidSheetExtents()
        {
            var duplicateProject = ModelBuilder.Project();
            var duplicateSheet = duplicateProject.Sheets[0];
            var root = new DiagramGroup("Root", 0, 0, 400, 300);
            var duplicate = new DiagramGroup("Duplicate", 20, 20, 200, 120)
            {
                Id = root.Id,
                ParentGroupId = root.Id
            };
            duplicateSheet.Groups.Add(root);
            duplicateSheet.Groups.Add(duplicate);
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Move(duplicateProject, duplicateSheet.Id, root.Id, 1, 1));

            var memberProject = ModelBuilder.Project();
            var memberSheet = memberProject.Sheets[0];
            var withMissingMember = new DiagramGroup("Root", 0, 0, 400, 300);
            withMissingMember.MemberNodeIds.Add(Guid.NewGuid());
            memberSheet.Groups.Add(withMissingMember);
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Move(memberProject, memberSheet.Id, withMissingMember.Id, 1, 1));

            var parentProject = ModelBuilder.Project();
            var parentSheet = parentProject.Sheets[0];
            var parent = new DiagramGroup("Parent", 0, 0, 500, 400);
            var duplicateParent = new DiagramGroup("Duplicate parent", 0, 0, 500, 400) { Id = parent.Id };
            var child = new DiagramGroup("Child", 20, 20, 200, 120) { ParentGroupId = parent.Id };
            parentSheet.Groups.Add(parent);
            parentSheet.Groups.Add(duplicateParent);
            parentSheet.Groups.Add(child);
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Move(parentProject, parentSheet.Id, child.Id, 1, 1));

            var extentProject = ModelBuilder.Project();
            var extentSheet = extentProject.Sheets[0];
            var extentGroup = new DiagramGroup("Group", 0, 0, 200, 120);
            extentSheet.Groups.Add(extentGroup);
            extentSheet.Width = double.PositiveInfinity;
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Move(extentProject, extentSheet.Id, extentGroup.Id, 1, 1));

            var subtreeProject = ModelBuilder.Project();
            var subtreeSheet = subtreeProject.Sheets[0];
            var subtreeRoot = new DiagramGroup("Root", 0, 0, 500, 400);
            var firstChild = new DiagramGroup("First", 20, 20, 200, 120)
            {
                ParentGroupId = subtreeRoot.Id
            };
            var secondChild = new DiagramGroup("Second", 240, 20, 200, 120)
            {
                Id = firstChild.Id,
                ParentGroupId = subtreeRoot.Id
            };
            subtreeSheet.Groups.Add(subtreeRoot);
            subtreeSheet.Groups.Add(firstChild);
            subtreeSheet.Groups.Add(secondChild);
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Move(subtreeProject, subtreeSheet.Id, subtreeRoot.Id, 1, 1));

            var corruptProject = ModelBuilder.Project();
            var corruptSheet = corruptProject.Sheets[0];
            var safeRoot = new DiagramGroup("Safe root", 0, 0, 500, 400);
            var corrupt = new DiagramGroup("Corrupt child", -1, 0, 200, 120)
            {
                ParentGroupId = safeRoot.Id
            };
            corruptSheet.Groups.Add(safeRoot);
            corruptSheet.Groups.Add(corrupt);
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Move(corruptProject, corruptSheet.Id, safeRoot.Id, 0, 0));
        }

        [Fact]
        public void LookupOwnershipTextAndBoundsDefencesFailClosed()
        {
            var project = ModelBuilder.Project();
            var sheet = project.Sheets[0];
            Assert.Throws<ArgumentNullException>(() =>
                GroupEditingLogic.Add(null, sheet.Id, new DiagramGroup(), new Guid[0]));
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Edit(project, Guid.NewGuid(), Guid.NewGuid(), "A", ""));

            project.Sheets.Add(sheet);
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Edit(project, sheet.Id, Guid.NewGuid(), "A", ""));
            project.Sheets.RemoveAt(1);
            sheet.Groups = null;
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Edit(project, sheet.Id, Guid.NewGuid(), "A", ""));

            var textProject = ModelBuilder.Project();
            var textSheet = textProject.Sheets[0];
            var group = new DiagramGroup("Group", 0, 0, 200, 120);
            textSheet.Groups.Add(group);
            Assert.Throws<InvalidOperationException>(() => GroupEditingLogic.Edit(textProject, textSheet.Id, group.Id, " ", ""));
            Assert.Throws<InvalidOperationException>(() => GroupEditingLogic.Edit(textProject, textSheet.Id, group.Id, new string('A', GroupEditingLogic.MaximumTitleLength + 1), ""));
            Assert.Throws<InvalidOperationException>(() => GroupEditingLogic.Edit(textProject, textSheet.Id, group.Id, "A", new string('x', GroupEditingLogic.MaximumCommentLength + 1)));
            Assert.Throws<InvalidOperationException>(() => GroupEditingLogic.Edit(textProject, textSheet.Id, group.Id, "A\0", ""));

            var node = ModelBuilder.PlaceNote(textSheet, "Node", 20, 20);
            var firstOwner = new DiagramGroup("First", 0, 0, 200, 120);
            var secondOwner = new DiagramGroup("Second", 0, 0, 200, 120);
            firstOwner.MemberNodeIds.Add(node.Id);
            secondOwner.MemberNodeIds.Add(node.Id);
            textSheet.Groups.Add(firstOwner);
            textSheet.Groups.Add(secondOwner);
            var draft = GroupEditingLogic.CreateDraft("Draft", "", 0, 0, 200, 120);
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Add(textProject, textSheet.Id, draft, new[] { node.Id }));

            var outside = new DiagramGroup("Outside", 0, 0, 200, 120);
            textSheet.Width = 100;
            Assert.Throws<InvalidOperationException>(() =>
                GroupEditingLogic.Add(textProject, textSheet.Id, outside, new Guid[0]));
        }

        [Fact]
        public void ProjectIdentityEnumerationIncludesEveryPersistedObjectKindAndIgnoresNullCollections()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("In"));
            var udt = ModelBuilder.Udt("UdtA", new UdtMember("Value", "Bool"));
            var tag = ModelBuilder.Tag("TagA");
            var callBlock = new CallBlockDefinition { Name = "FC_Call" };
            callBlock.Interface.Add(ModelBuilder.Input("Command"));
            var unit = new UnitDefinition("UnitA", block);
            unit.Bindings.Add(new ParameterBinding { ParameterName = "In", Expression = "TRUE" });
            callBlock.Units.Add(unit);
            project.Plant.Blocks.Add(block);
            project.Plant.Blocks.Add(null);
            project.Plant.DataTypes.Add(udt);
            project.Plant.DataTypes.Add(null);
            project.Plant.Tags.Add(tag);
            project.Plant.Tags.Add(null);
            project.Plant.CallBlocks.Add(callBlock);
            project.Plant.CallBlocks.Add(null);
            var node = ModelBuilder.PlaceNote(project.Sheets[0], "note");
            node.Pins.Add(new Pin { NodeId = node.Id });
            node.Pins.Add(null);
            project.Sheets[0].Wires.Add(new Wire());
            project.Sheets[0].Wires.Add(null);
            project.Sheets[0].Groups.Add(new DiagramGroup());
            project.Sheets[0].Groups.Add(null);
            project.Sheets.Add(null);

            var method = typeof(GroupEditingLogic).GetMethod(
                "EnumerateProjectIds",
                BindingFlags.NonPublic | BindingFlags.Static);
            var ids = (ISet<Guid>)method.Invoke(null, new object[] { project });

            Assert.Contains(block.Interface[0].Id, ids);
            Assert.Contains(udt.Members[0].Id, ids);
            Assert.Contains(callBlock.Interface[0].Id, ids);
            Assert.Contains(unit.Bindings[0].Id, ids);
            Assert.DoesNotContain(Guid.Empty, ids);

            project.Plant = null;
            project.Sheets = null;
            Assert.Empty((ISet<Guid>)method.Invoke(null, new object[] { project }));
        }
    }
}
