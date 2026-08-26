using System;
using System.Linq;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.App
{
    public sealed class BlockLibraryEditorLogicCoverageTests
    {
        [Fact]
        public void ValidationRejectsNullBoundaries()
        {
            Assert.Equal("project", Assert.Throws<ArgumentNullException>(() =>
                BlockLibraryEditorLogic.ValidateCandidateUsage(
                    null,
                    ModelBuilder.FunctionBlock("FB_Test"))).ParamName);
            Assert.Equal("candidate", Assert.Throws<ArgumentNullException>(() =>
                BlockLibraryEditorLogic.ValidateCandidateUsage(
                    ModelBuilder.Project(),
                    null)).ParamName);
        }

        [Fact]
        public void GeneratedSheetAndCallBlockDbNamesParticipateInCollisions()
        {
            var project = ModelBuilder.Project("Project", "FC_Sheet");
            var other = ModelBuilder.FunctionBlock("FB_Other");
            project.Plant.Blocks.Add(other);
            project.Sheets[0].Nodes.Add(new BlockCallNode
            {
                BlockDefinitionId = other.Id,
                UseMultiInstance = true
            });
            project.Plant.CallBlocks.Add(new CallBlockDefinition
            {
                Name = "FB_Calls",
                Kind = BlockKind.FunctionBlock,
                InstanceDbName = string.Empty
            });

            Assert.NotEmpty(BlockLibraryEditorLogic.ValidateCandidateUsage(
                project,
                ModelBuilder.FunctionBlock("FC_Sheet_DB")));
            Assert.NotEmpty(BlockLibraryEditorLogic.ValidateCandidateUsage(
                project,
                ModelBuilder.FunctionBlock("FB_Calls_DB")));

            project.Plant.CallBlocks[0].InstanceDbName = "CustomCallsDb";
            Assert.NotEmpty(BlockLibraryEditorLogic.ValidateCandidateUsage(
                project,
                ModelBuilder.FunctionBlock("CustomCallsDb")));
        }

        [Fact]
        public void LegacyCallUnitsAndPlacedInstancesAreValidatedForCandidateFb()
        {
            var project = ModelBuilder.Project();
            var candidate = ModelBuilder.FunctionBlock("FB_Candidate");
            var other = ModelBuilder.FunctionBlock("FB_Other");
            project.Plant.Blocks.Add(null);
            project.Plant.Blocks.Add(candidate);
            project.Plant.Blocks.Add(other);
            project.Plant.Tags.Add(new TagDefinition { Name = "TakenDb" });
            var calls = new CallBlockDefinition { Name = "FC_Calls" };
            calls.Units.Add(null);
            calls.Units.Add(new UnitDefinition
            {
                Name = "bad-name",
                BlockId = Guid.NewGuid(),
                BlockName = candidate.Name
            });
            calls.Units.Add(new UnitDefinition
            {
                Name = "TakenDb",
                BlockId = candidate.Id,
                BlockName = candidate.Name
            });
            calls.Units.Add(new UnitDefinition
            {
                Name = "FreeDb",
                BlockId = candidate.Id,
                BlockName = candidate.Name
            });
            calls.Units.Add(new UnitDefinition
            {
                Name = "OtherUnitDb",
                BlockId = Guid.NewGuid(),
                BlockName = other.Name
            });
            calls.Units.Add(new UnitDefinition
            {
                Name = "IgnoredMulti",
                BlockId = candidate.Id,
                UseMultiInstance = true
            });
            project.Plant.CallBlocks.Add(calls);
            project.Sheets[0].Nodes.Add(new BlockCallNode
            {
                BlockDefinitionId = other.Id,
                InstanceName = "OtherPlacedDb"
            });
            project.Sheets[0].Nodes.Add(new BlockCallNode
            {
                BlockDefinitionId = candidate.Id,
                InstanceName = "bad placed name"
            });
            project.Sheets[0].Nodes.Add(new BlockCallNode
            {
                BlockDefinitionId = candidate.Id,
                InstanceName = "OtherPlacedDb"
            });

            var errors = BlockLibraryEditorLogic.ValidateCandidateUsage(project, candidate);

            Assert.Contains(errors, item => item.Contains("bad-name"));
            Assert.Contains(errors, item => item.Contains("TakenDb"));
            Assert.Contains(errors, item => item.Contains("bad placed name"));
            Assert.Contains(errors, item => item.Contains("OtherPlacedDb"));
        }

        [Fact]
        public void FunctionCandidateDoesNotCreateInstanceDbErrors()
        {
            var project = ModelBuilder.Project();
            var candidate = new BlockDefinition
            {
                Name = "FC_Candidate",
                Kind = BlockKind.Function,
                ReturnType = "Void"
            };
            project.Plant.Blocks.Add(candidate);
            project.Sheets[0].Nodes.Add(new BlockCallNode
            {
                BlockDefinitionId = candidate.Id,
                InstanceName = "bad name"
            });

            Assert.Empty(BlockLibraryEditorLogic.ValidateCandidateUsage(project, candidate));
        }

        [Fact]
        public void SynchronizationPreservesCompatiblePinsAndRemovesOnlyInvalidWires()
        {
            var project = ModelBuilder.Project();
            var sheet = project.Sheets[0];
            var candidate = new BlockDefinition
            {
                Name = "FC_Updated",
                Kind = BlockKind.Function,
                ReturnType = "Int",
                Version = "2.0",
                Description = "updated",
                SclBody = "#Return := 1;"
            };
            var input = ModelBuilder.Input("In", "Bool");
            var output = ModelBuilder.Output("Out", "Int");
            var inOut = ModelBuilder.InOut("Handle", "DWord");
            candidate.Interface.Add(input);
            candidate.Interface.Add(output);
            candidate.Interface.Add(inOut);

            var blockNode = new BlockCallNode
            {
                BlockDefinitionId = candidate.Id,
                InstanceName = "OldInstance",
                UseMultiInstance = true,
                Title = "old"
            };
            var existingInput = Pin(blockNode, "In", PinDirection.Input, "Bool", PinRole.Parameter, input.Id);
            var existingReturn = Pin(blockNode, "Return", PinDirection.Output, "Int", PinRole.FunctionReturn, Guid.Empty);
            blockNode.Pins.Add(existingInput);
            blockNode.Pins.Add(existingReturn);

            var sourceTag = new TagNode { Title = "source" };
            var sourcePin = Pin(sourceTag, "Value", PinDirection.Output, "Bool", PinRole.Terminal, Guid.Empty);
            sourceTag.Pins.Add(sourcePin);
            var constant = new ConstantNode { Title = "constant" };
            var constantPin = Pin(constant, "Value", PinDirection.Output, "DWord", PinRole.Terminal, Guid.Empty);
            constant.Pins.Add(constantPin);
            var sinkTag = new TagNode { Title = "sink" };
            var sinkPin = Pin(sinkTag, "Value", PinDirection.Input, "Bool", PinRole.Terminal, Guid.Empty);
            sinkTag.Pins.Add(sinkPin);
            sheet.Nodes.Add(blockNode);
            sheet.Nodes.Add(sourceTag);
            sheet.Nodes.Add(constant);
            sheet.Nodes.Add(sinkTag);

            var unrelated = Wire(sourceTag, sourcePin, sinkTag, sinkPin);
            var validInput = Wire(sourceTag, sourcePin, blockNode, existingInput);
            var dangling = new Wire
            {
                SourceNodeId = blockNode.Id,
                SourcePinId = existingReturn.Id,
                TargetNodeId = Guid.NewGuid(),
                TargetPinId = Guid.NewGuid()
            };
            var staleOutput = new Pin
            {
                Id = Guid.NewGuid(),
                NodeId = blockNode.Id,
                MemberId = output.Id,
                Name = "Out",
                Direction = PinDirection.Output,
                Role = PinRole.Parameter,
                DataType = "Bool"
            };
            blockNode.Pins.Add(staleOutput);
            var incompatibleOutput = Wire(blockNode, staleOutput, sinkTag, sinkPin);
            var staleHandle = new Pin
            {
                Id = Guid.NewGuid(),
                NodeId = blockNode.Id,
                MemberId = inOut.Id,
                Name = "Handle",
                Direction = PinDirection.Input,
                Role = PinRole.Parameter,
                DataType = "DWord",
                IsRequired = true
            };
            blockNode.Pins.Add(staleHandle);
            var nonWritableRequired = Wire(constant, constantPin, blockNode, staleHandle);
            sheet.Wires.Add(unrelated);
            sheet.Wires.Add(validInput);
            sheet.Wires.Add(dangling);
            sheet.Wires.Add(incompatibleOutput);
            sheet.Wires.Add(nonWritableRequired);

            var plan = BlockLibraryEditorLogic.BuildSynchronizationPlan(project, candidate);

            Assert.Single(plan.NodeStates);
            Assert.Equal(3, plan.WireRemovals.Count);
            var futurePins = plan.NodeStates[0].Pins;
            Assert.Equal(existingInput.Id, futurePins.Single(item => item.Name == "In").Id);
            Assert.Equal(existingReturn.Id, futurePins.Single(item => item.Role == PinRole.FunctionReturn).Id);
            Assert.Equal("Int", futurePins.Single(item => item.Name == "Out").DataType);

            BlockLibraryEditorLogic.ApplySynchronization(candidate, candidate, plan);

            Assert.Equal("FC_Updated", blockNode.Title);
            Assert.False(blockNode.UseMultiInstance);
            Assert.Contains(unrelated, sheet.Wires);
            Assert.Contains(validInput, sheet.Wires);
            Assert.DoesNotContain(dangling, sheet.Wires);
            Assert.DoesNotContain(incompatibleOutput, sheet.Wires);
            Assert.DoesNotContain(nonWritableRequired, sheet.Wires);
        }

        [Fact]
        public void SynchronizationRejectsNullBoundariesAndHandlesEmptySheets()
        {
            var project = ModelBuilder.Project();
            var candidate = ModelBuilder.FunctionBlock("FB_Test");

            Assert.Equal("project", Assert.Throws<ArgumentNullException>(() =>
                BlockLibraryEditorLogic.BuildSynchronizationPlan(null, candidate)).ParamName);
            Assert.Equal("candidate", Assert.Throws<ArgumentNullException>(() =>
                BlockLibraryEditorLogic.BuildSynchronizationPlan(project, null)).ParamName);
            Assert.Equal("target", Assert.Throws<ArgumentNullException>(() =>
                BlockLibraryEditorLogic.ApplySynchronization(
                    null,
                    candidate,
                    new BlockLibraryEditorLogic.BlockSynchronizationPlan())).ParamName);
            Assert.Equal("candidate", Assert.Throws<ArgumentNullException>(() =>
                BlockLibraryEditorLogic.ApplySynchronization(
                    candidate,
                    null,
                    new BlockLibraryEditorLogic.BlockSynchronizationPlan())).ParamName);
            Assert.Equal("plan", Assert.Throws<ArgumentNullException>(() =>
                BlockLibraryEditorLogic.ApplySynchronization(candidate, candidate, null)).ParamName);

            project.Sheets.Add(new CallSheet { Nodes = null, Wires = null });
            Assert.Empty(BlockLibraryEditorLogic.BuildSynchronizationPlan(project, candidate).NodeStates);
        }

        [Fact]
        public void SynchronizationPlanValueObjectsRetainTheirInputs()
        {
            var sheet = new CallSheet();
            var wire = new Wire();
            var removal = new BlockLibraryEditorLogic.WireRemoval(sheet, wire);

            Assert.Same(sheet, removal.Sheet);
            Assert.Same(wire, removal.Wire);
        }

        private static Pin Pin(
            CallNode node,
            string name,
            PinDirection direction,
            string dataType,
            PinRole role,
            Guid memberId)
        {
            return new Pin
            {
                Id = Guid.NewGuid(),
                NodeId = node.Id,
                MemberId = memberId,
                Name = name,
                Direction = direction,
                Role = role,
                DataType = dataType
            };
        }

        private static Wire Wire(CallNode source, Pin sourcePin, CallNode target, Pin targetPin)
        {
            return new Wire
            {
                SourceNodeId = source.Id,
                SourcePinId = sourcePin.Id,
                TargetNodeId = target.Id,
                TargetPinId = targetPin.Id
            };
        }
    }
}
