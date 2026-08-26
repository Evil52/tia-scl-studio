using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TiaSclStudio.App;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.EndToEnd.Tests
{
    public sealed class NodeCreationLogicCoverageTests
    {
        [Fact]
        public void CreationFactoriesNormalizeTextAndAllocateDistinctStableIds()
        {
            var constant = NodeCreationLogic.CreateConstant("  TRUE  ", " Bool ");
            var newTag = NodeCreationLogic.CreateNewTag(
                "  Input_1 ",
                " Bool ",
                " i00.0 ",
                null,
                TerminalDirection.Source);
            var tagId = Guid.NewGuid();
            var existing = NodeCreationLogic.CreateExistingTag(
                tagId,
                TerminalDirection.Sink);

            Assert.Equal("TRUE", constant.Literal);
            Assert.Equal("Bool", constant.DataType);
            Assert.Equal("Input_1", newTag.TagName);
            Assert.Equal("i00.0", newTag.TagAddress);
            Assert.Equal(string.Empty, newTag.TagComment);
            Assert.Equal(PlcCpuFamily.S71200, newTag.TargetCpuFamily);
            Assert.Equal(tagId, existing.TagDefinitionId);
            Assert.Null(existing.TargetCpuFamily);
            Assert.NotEqual(constant.NodeId, constant.PinId);
            Assert.NotEqual(newTag.NodeId, newTag.PinId);
            Assert.NotEqual(newTag.TagDefinitionId, newTag.NodeId);
        }

        [Fact]
        public void ValidateAndApplyRejectNullInputs()
        {
            var project = ModelBuilder.Project();
            var candidate = NodeCreationLogic.CreateConstant("TRUE", "Bool");

            Assert.Throws<ArgumentNullException>(() =>
                NodeCreationLogic.Validate(null, project.Sheets[0].Id, candidate));
            Assert.Throws<ArgumentNullException>(() =>
                NodeCreationLogic.Validate(project, project.Sheets[0].Id, null));
            Assert.Throws<ArgumentNullException>(() =>
                NodeCreationLogic.Apply(null, project.Sheets[0].Id, candidate));
            Assert.Throws<ArgumentNullException>(() =>
                NodeCreationLogic.Apply(project, project.Sheets[0].Id, null));
        }

        [Fact]
        public void SheetValidationRejectsEmptyMissingDuplicateAndNullNodeCollections()
        {
            var project = ModelBuilder.Project();
            var candidate = NodeCreationLogic.CreateConstant("TRUE", "Bool");

            Assert.Contains(
                NodeCreationLogic.Validate(project, Guid.Empty, candidate),
                error => error.Contains("not found"));
            Assert.Contains(
                NodeCreationLogic.Validate(project, Guid.NewGuid(), candidate),
                error => error.Contains("not found"));

            var duplicate = new CallSheet { Id = project.Sheets[0].Id, Name = "Duplicate" };
            project.Sheets.Add(duplicate);
            Assert.Contains(
                NodeCreationLogic.Validate(project, duplicate.Id, candidate),
                error => error.Contains("duplicated"));

            project.Sheets.Remove(duplicate);
            project.Sheets[0].Nodes = null;
            Assert.Contains(
                NodeCreationLogic.Validate(project, project.Sheets[0].Id, candidate),
                error => error.Contains("no node collection"));
        }

        [Fact]
        public void ConstantValidationRejectsEmptyVoidUnsafeAndCollidingIdentities()
        {
            var project = ModelBuilder.Project();
            var sheet = project.Sheets[0];
            var existingNode = DiagramNodeFactory.CreateConstant("FALSE", "Bool", 0.0, 0.0);
            sheet.Nodes.Add(existingNode);

            var cases = new[]
            {
                NodeCreationResult.ForConstant(Guid.Empty, Guid.NewGuid(), "TRUE", "Bool"),
                NodeCreationResult.ForConstant(Guid.NewGuid(), Guid.Empty, "TRUE", "Bool"),
                NodeCreationResult.ForConstant(existingNode.Id, Guid.NewGuid(), "TRUE", "Bool"),
                NodeCreationResult.ForConstant(Guid.NewGuid(), existingNode.Pins[0].Id, "TRUE", "Bool"),
                NodeCreationResult.ForConstant(Guid.NewGuid(), Guid.NewGuid(), "", "Bool"),
                NodeCreationResult.ForConstant(Guid.NewGuid(), Guid.NewGuid(), "TRUE; FALSE", "Bool"),
                NodeCreationResult.ForConstant(Guid.NewGuid(), Guid.NewGuid(), "TRUE", "Void"),
                NodeCreationResult.ForConstant(Guid.NewGuid(), Guid.NewGuid(), "TRUE", "")
            };

            Assert.All(
                cases,
                item => Assert.NotEmpty(NodeCreationLogic.Validate(project, sheet.Id, item)));

            var sameId = Guid.NewGuid();
            var same = NodeCreationResult.ForConstant(sameId, sameId, "TRUE", "Bool");
            Assert.Contains(
                NodeCreationLogic.Validate(project, sheet.Id, same),
                error => error.Contains("different stable ids"));

            var referencesTag = NodeCreationResult.ForConstant(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "TRUE",
                "Bool");
            typeof(NodeCreationResult).GetProperty("TagDefinitionId")
                .SetValue(referencesTag, Guid.NewGuid(), null);
            Assert.Contains(
                NodeCreationLogic.Validate(project, sheet.Id, referencesTag),
                error => error.Contains("must not reference"));
        }

        [Fact]
        public void UnsupportedCreationKindFailsClosed()
        {
            var project = ModelBuilder.Project();
            var candidate = NodeCreationLogic.CreateConstant("TRUE", "Bool");
            typeof(NodeCreationResult).GetProperty("Kind")
                .SetValue(candidate, (CallNodeKind)999, null);

            Assert.Contains(
                NodeCreationLogic.Validate(project, project.Sheets[0].Id, candidate),
                error => error.Contains("Only constant and tag-terminal"));
        }

        [Theory]
        [InlineData("value;other")]
        [InlineData("value//comment")]
        [InlineData("value(*comment")]
        [InlineData("value*)comment")]
        [InlineData("value\nother")]
        public void UnsafeConstantTextIsRejected(string literal)
        {
            Assert.False(NodeCreationLogic.IsSafeConstantLiteral(literal));
        }

        [Fact]
        public void XmlTextValidationRejectsInvalidCharacters()
        {
            Assert.True(NodeCreationLogic.IsValidXmlText(null));
            Assert.True(NodeCreationLogic.IsValidXmlText("обычный комментарий"));
            Assert.False(NodeCreationLogic.IsValidXmlText("bad\u0001text"));
        }

        [Fact]
        public void NewTagValidationRejectsMalformedIdentityFieldsDirectionAndCpu()
        {
            var project = ModelBuilder.Project();
            var sheetId = project.Sheets[0].Id;
            var commonId = Guid.NewGuid();
            var candidates = new[]
            {
                NodeCreationResult.ForNewTag(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty,
                    "Tag_A", "Bool", "%I0.0", "", TerminalDirection.Source, PlcCpuFamily.S71200),
                NodeCreationResult.ForNewTag(commonId, Guid.NewGuid(), commonId,
                    "Tag_A", "Bool", "%I0.0", "", TerminalDirection.Source, PlcCpuFamily.S71200),
                NodeCreationResult.ForNewTag(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    "1 bad", "Void", "", "bad\u0001", (TerminalDirection)999, PlcCpuFamily.S71200),
                NodeCreationResult.ForNewTag(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    "Tag_A", "Bool", "%I0.0", "", TerminalDirection.Source, (PlcCpuFamily)999),
                NodeCreationResult.ForNewTag(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    "Tag_A", "Real", "%MD8189", "", TerminalDirection.Source, PlcCpuFamily.S71200)
            };

            Assert.All(
                candidates,
                item => Assert.NotEmpty(NodeCreationLogic.Validate(project, sheetId, item)));
        }

        [Fact]
        public void NewTagValidationRejectsMissingPlantAndTagCollection()
        {
            var project = ModelBuilder.Project();
            var sheetId = project.Sheets[0].Id;
            var candidate = NewTag("Tag_A");
            project.Plant = null;
            Assert.Contains(
                NodeCreationLogic.Validate(project, sheetId, candidate),
                error => error.Contains("no PLC-tag collection"));

            project.Plant = new PlantProject { Tags = null };
            Assert.Contains(
                NodeCreationLogic.Validate(project, sheetId, candidate),
                error => error.Contains("no PLC-tag collection"));
        }

        [Fact]
        public void NewTagCpuMigrationValidatesEveryExistingAddress()
        {
            var project = ModelBuilder.Project();
            project.Plant.CpuFamily = PlcCpuFamily.S71500;
            project.Plant.Tags.Add(ModelBuilder.Tag("Far", "Real", "%MD10000"));

            var errors = NodeCreationLogic.Validate(
                project,
                project.Sheets[0].Id,
                NewTag("Near", PlcCpuFamily.S71200));

            Assert.Contains(errors, error => error.Contains("Far"));
        }

        [Theory]
        [InlineData("BlockName", "block")]
        [InlineData("TypeName", "data type")]
        [InlineData("TagName", "PLC tag")]
        [InlineData("CallName", "call block")]
        [InlineData("CallName_DB", "call-block DB")]
        [InlineData("UnitDb", "instance DB")]
        [InlineData("Calls", "call sheet")]
        [InlineData("Calls_DB", "sheet DB")]
        [InlineData("CanvasDb", "instance DB")]
        public void NewTagCannotCollideWithAnyGeneratedGlobalSymbol(
            string candidateName,
            string expectedOwner)
        {
            var project = SymbolRichProject();

            var errors = NodeCreationLogic.Validate(
                project,
                project.Sheets[0].Id,
                NewTag(candidateName));

            Assert.Contains(errors, error => error.Contains(expectedOwner));
        }

        [Fact]
        public void ExistingTagValidationRejectsMissingDuplicateAndIdentityCollision()
        {
            var project = ModelBuilder.Project();
            var tagId = Guid.NewGuid();
            var candidate = NodeCreationLogic.CreateExistingTag(
                tagId,
                TerminalDirection.Source);
            Assert.Contains(
                NodeCreationLogic.Validate(project, project.Sheets[0].Id, candidate),
                error => error.Contains("not found"));

            project.Plant.Tags.Add(new TagDefinition
            {
                Id = tagId,
                Name = "Tag_A",
                DataType = "Bool",
                Address = "%I0.0"
            });
            project.Plant.Tags.Add(new TagDefinition
            {
                Id = tagId,
                Name = "Tag_B",
                DataType = "Bool",
                Address = "%I0.1"
            });
            Assert.Contains(
                NodeCreationLogic.Validate(project, project.Sheets[0].Id, candidate),
                error => error.Contains("duplicated"));

            project.Plant.Tags.RemoveAt(1);
            project.Plant.Id = tagId;
            Assert.Contains(
                NodeCreationLogic.Validate(project, project.Sheets[0].Id, candidate),
                error => error.Contains("collides"));
        }

        [Fact]
        public void ExistingTagValidationChecksStoredFieldsDirectionCpuAndAddress()
        {
            var project = ModelBuilder.Project();
            var tag = new TagDefinition
            {
                Name = "1 bad",
                DataType = "Void",
                Address = string.Empty,
                Comment = "bad\u0001"
            };
            project.Plant.Tags.Add(tag);
            var candidate = NodeCreationResult.ForExistingTag(
                Guid.NewGuid(),
                Guid.NewGuid(),
                tag.Id,
                (TerminalDirection)999,
                (PlcCpuFamily)999);

            var errors = NodeCreationLogic.Validate(
                project,
                project.Sheets[0].Id,
                candidate);

            Assert.True(errors.Count >= 5, string.Join(Environment.NewLine, errors));
        }

        [Fact]
        public void ApplyCreatesConstantNewTagAndExistingTagWithStableIdentities()
        {
            var project = ModelBuilder.Project();
            var sheet = project.Sheets[0];
            var constant = NodeCreationLogic.CreateConstant("TRUE", "Bool");
            NodeCreationLogic.Apply(project, sheet.Id, constant);
            var constantNode = sheet.Nodes.OfType<ConstantNode>().Single();
            Assert.Equal(constant.NodeId, constantNode.Id);
            Assert.Equal(constant.PinId, constantNode.Pins.Single().Id);

            var newTag = NewTag("Input_A", PlcCpuFamily.S71500);
            NodeCreationLogic.Apply(project, sheet.Id, newTag);
            var definition = project.Plant.Tags.Single(tag => tag.Id == newTag.TagDefinitionId);
            Assert.Equal("%I0.0", definition.Address);
            Assert.Equal(PlcCpuFamily.S71500, project.Plant.CpuFamily);

            var existing = NodeCreationLogic.CreateExistingTag(
                definition.Id,
                TerminalDirection.Sink,
                PlcCpuFamily.S71200);
            NodeCreationLogic.Apply(project, sheet.Id, existing);
            var sink = sheet.Nodes.OfType<TagNode>()
                .Single(node => node.Id == existing.NodeId);
            Assert.Equal(PinDirection.Input, sink.Pins.Single().Direction);
            Assert.Equal(PlcCpuFamily.S71200, project.Plant.CpuFamily);
        }

        [Fact]
        public void ApplyIsAtomicWhenValidationFails()
        {
            var project = ModelBuilder.Project();
            var beforeTags = project.Plant.Tags.Count;
            var beforeNodes = project.Sheets[0].Nodes.Count;

            Assert.Throws<InvalidOperationException>(() =>
                NodeCreationLogic.Apply(
                    project,
                    project.Sheets[0].Id,
                    NodeCreationLogic.CreateConstant("", "Bool")));

            Assert.Equal(beforeTags, project.Plant.Tags.Count);
            Assert.Equal(beforeNodes, project.Sheets[0].Nodes.Count);
        }

        [Fact]
        public void IdentityAndSymbolHelpersCoverEveryPersistedKindAndFallbackLookup()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("FB_Fallback", ModelBuilder.Input("In"));
            var udt = ModelBuilder.Udt("Payload", new UdtMember("Value", "Bool"));
            udt.Members[0].Id = Guid.Empty;
            var callBlock = new CallBlockDefinition
            {
                Name = "FC_Calls",
                Kind = BlockKind.Function
            };
            callBlock.Interface.Add(ModelBuilder.Input("Enable"));
            var unit = new UnitDefinition
            {
                Name = "FallbackDb",
                BlockId = Guid.NewGuid(),
                BlockName = block.Name,
                UseMultiInstance = false
            };
            unit.Bindings.Add(new ParameterBinding
            {
                ParameterName = "In",
                Expression = "TRUE"
            });
            callBlock.Units.Add(unit);
            project.Plant.Blocks.Add(block);
            project.Plant.DataTypes.Add(udt);
            project.Plant.CallBlocks.Add(callBlock);
            project.Sheets[0].Groups.Add(new DiagramGroup());

            var collect = typeof(NodeCreationLogic).GetMethod(
                "CollectProjectIdentities",
                BindingFlags.NonPublic | BindingFlags.Static);
            var identities = (IDictionary<Guid, List<string>>)collect.Invoke(
                null,
                new object[] { project });
            Assert.Contains(callBlock.Interface[0].Id, identities.Keys);
            Assert.Contains(unit.Bindings[0].Id, identities.Keys);
            Assert.DoesNotContain(Guid.Empty, identities.Keys);

            var findOwner = typeof(NodeCreationLogic).GetMethod(
                "FindGlobalSymbolOwner",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.Null(findOwner.Invoke(null, new object[] { project, " " }));
            Assert.Contains(
                "instance DB",
                (string)findOwner.Invoke(null, new object[] { project, "FallbackDb" }));
        }

        private static NodeCreationResult NewTag(
            string name,
            PlcCpuFamily cpuFamily = PlcCpuFamily.S71200)
        {
            return NodeCreationLogic.CreateNewTag(
                name,
                "Bool",
                "%I0.0",
                string.Empty,
                TerminalDirection.Source,
                cpuFamily);
        }

        private static DiagramProject SymbolRichProject()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("BlockName");
            var multi = ModelBuilder.FunctionBlock("MultiBlock");
            project.Plant.Blocks.Add(block);
            project.Plant.Blocks.Add(multi);
            project.Plant.DataTypes.Add(new UdtDefinition { Name = "TypeName" });
            project.Plant.Tags.Add(ModelBuilder.Tag("TagName", "Bool", "%M0.0"));

            var callBlock = new CallBlockDefinition
            {
                Name = "CallName",
                Kind = BlockKind.FunctionBlock,
                InstanceDbName = string.Empty
            };
            callBlock.Units.Add(new UnitDefinition
            {
                Name = "UnitDb",
                BlockId = block.Id,
                BlockName = block.Name,
                UseMultiInstance = false
            });
            project.Plant.CallBlocks.Add(callBlock);

            var sheet = project.Sheets[0];
            sheet.Name = "Calls";
            var multiNode = DiagramNodeFactory.CreateBlockCall(multi, "Multi01", 0.0, 0.0);
            multiNode.UseMultiInstance = true;
            sheet.Nodes.Add(multiNode);
            sheet.Nodes.Add(DiagramNodeFactory.CreateBlockCall(block, "CanvasDb", 0.0, 0.0));
            return project;
        }
    }
}
