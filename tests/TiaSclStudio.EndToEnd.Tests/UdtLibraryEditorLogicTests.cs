using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.History;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.App
{
    public sealed class UdtLibraryEditorLogicTests
    {
        [Fact]
        public void RenameRewritesEveryModelAndDiagramReferenceAndKeepsStableIds()
        {
            var project = BuildReferencedProject();
            var original = project.Plant.DataTypes.Single(item => item.Name == "OldPayload");
            var originalUdtId = original.Id;
            var originalMemberId = original.Members[0].Id;
            var candidates = project.Plant.DataTypes
                .Select(UdtEditorWindow.CloneDataType)
                .ToList();
            candidates.Single(item => item.Id == originalUdtId).Name = "NewPayload";

            UdtLibraryEditorLogic.Apply(project, candidates);

            var renamed = project.Plant.DataTypes.Single(item => item.Id == originalUdtId);
            Assert.Equal("NewPayload", renamed.Name);
            Assert.Equal(originalMemberId, renamed.Members[0].Id);
            Assert.Equal(
                "Array[0..3] of \"NewPayload\"",
                project.Plant.DataTypes.Single(item => item.Name == "Envelope").Members[0].DataType);
            Assert.All(
                project.Plant.Blocks.SelectMany(item => item.Interface),
                item => Assert.Equal("\"NewPayload\"", item.DataType));
            Assert.Equal(
                "\"NewPayload\"",
                project.Plant.Blocks.Single(item => item.Name == "FC_ReturnPayload").ReturnType);
            Assert.All(
                project.Plant.CallBlocks.SelectMany(item => item.Interface),
                item => Assert.Equal("\"NewPayload\"", item.DataType));
            Assert.Equal(
                "\"NewPayload\"",
                project.Plant.CallBlocks.Single(item => item.Name == "FC_UsesPayload").ReturnType);
            Assert.Equal("\"NewPayload\"", project.Plant.Tags[0].DataType);
            Assert.All(
                project.Sheets.SelectMany(item => item.Nodes).SelectMany(item => item.Pins),
                item => Assert.Equal("\"NewPayload\"", item.DataType));
            Assert.Equal(
                "\"NewPayload\"",
                project.Sheets[0].Nodes.OfType<ConstantNode>().Single().DataType);
        }

        [Fact]
        public void DeletionUsageCensusIncludesUdtsBlocksCallBlocksTagsAndDiagramNodes()
        {
            var project = BuildReferencedProject();
            var target = project.Plant.DataTypes.Single(item => item.Name == "OldPayload");

            var usages = UdtLibraryEditorLogic.FindUsages(
                project,
                project.Plant.DataTypes,
                target.Id,
                new[] { target.Name });

            Assert.Contains(usages, item => item.Contains("FC_ReturnPayload"));
            Assert.True(usages.Count(item => item.Contains("FC_UsesPayload")) >= 2);

            Assert.Contains(usages, item => item.Contains("UDT «Envelope»"));
            Assert.Contains(usages, item => item.Contains("блок «FB_UsesPayload»"));
            Assert.Contains(usages, item => item.Contains("блок вызовов «FC_UsesPayload»"));
            Assert.Contains(usages, item => item.Contains("PLC-тег «PayloadTag»"));
            Assert.Contains(usages, item => item.Contains("лист «FC_Test»"));
        }

        [Fact]
        public void WholeUdtEditRoundTripsAsOneHistoryEntryWithStableIdentity()
        {
            var project = BuildReferencedProject();
            var before = DiagramProjectSnapshot.Clone(project);
            var target = project.Plant.DataTypes.Single(item => item.Name == "OldPayload");
            var targetId = target.Id;
            var memberId = target.Members[0].Id;
            var candidates = project.Plant.DataTypes
                .Select(UdtEditorWindow.CloneDataType)
                .ToList();
            candidates.Single(item => item.Id == targetId).Name = "NewPayload";
            UdtLibraryEditorLogic.Apply(project, candidates);

            var history = new DiagramEditHistory();
            Assert.True(history.Record("Изменение библиотеки UDT", before, project));
            Assert.Equal(1, history.UndoCount);

            var undone = history.Undo();
            var restoredOld = undone.Plant.DataTypes.Single(item => item.Id == targetId);
            Assert.Equal("OldPayload", restoredOld.Name);
            Assert.Equal(memberId, restoredOld.Members[0].Id);

            var redone = history.Redo();
            var restoredNew = redone.Plant.DataTypes.Single(item => item.Id == targetId);
            Assert.Equal("NewPayload", restoredNew.Name);
            Assert.Equal(memberId, restoredNew.Members[0].Id);
            Assert.Equal(1, history.UndoCount);
        }

        [Theory]
        [InlineData("FC_Test", false)]
        [InlineData("FC_Test_DB", true)]
        [InlineData("Use01", false)]
        public void FutureCatalogueRejectsDiagramWideGeneratedSymbolCollisions(
            string candidateName,
            bool useMultiInstance)
        {
            var project = BuildReferencedProject();
            project.Sheets[0].Nodes.OfType<BlockCallNode>().Single().UseMultiInstance = useMultiInstance;
            var candidates = project.Plant.DataTypes
                .Select(UdtEditorWindow.CloneDataType)
                .ToList();
            candidates.Add(ModelBuilder.Udt(candidateName, new UdtMember("Value", "Bool")));

            var messages = UdtLibraryEditorLogic.ValidateCandidates(project, candidates);

            Assert.Contains(
                messages,
                message => message.IndexOf(candidateName, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static DiagramProject BuildReferencedProject()
        {
            var project = ModelBuilder.Project("UdtProject", "FC_Test");
            var payload = ModelBuilder.Udt(
                "OldPayload",
                new UdtMember("Value", "Int"));
            var envelope = ModelBuilder.Udt(
                "Envelope",
                new UdtMember("Items", "Array[0..3] of OldPayload"));
            project.Plant.DataTypes.Add(payload);
            project.Plant.DataTypes.Add(envelope);

            var block = ModelBuilder.FunctionBlock(
                "FB_UsesPayload",
                ModelBuilder.Input("Input", "OldPayload"));
            project.Plant.Blocks.Add(block);
            project.Plant.Blocks.Add(new BlockDefinition
            {
                Name = "FC_ReturnPayload",
                Kind = BlockKind.Function,
                ReturnType = "oldpayload"
            });
            var callBlock = new CallBlockDefinition
            {
                Name = "FC_UsesPayload",
                Kind = BlockKind.Function,
                ReturnType = "\"OldPayload\""
            };
            callBlock.Interface.Add(ModelBuilder.Input("Input", "\"OldPayload\""));
            project.Plant.CallBlocks.Add(callBlock);

            project.Plant.Tags.Add(new TagDefinition
            {
                Name = "PayloadTag",
                DataType = "oldpayload",
                Address = "%M0.0"
            });

            var sheet = project.Sheets[0];
            ModelBuilder.PlaceBlock(sheet, block, "Use01");
            ModelBuilder.PlaceConstant(sheet, "DefaultPayload", "OldPayload");
            return project;
        }
    }
}
