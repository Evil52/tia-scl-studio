using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Model;
using Xunit;

namespace TiaSclStudio.App
{
    /// <summary>
    /// The node-properties editor works on a detached copy and hands back a value
    /// object. Nothing reaches the project until <see cref="NodeEditingLogic.Apply"/>
    /// runs, and it refuses anything that would leave a wire, an instance DB or a
    /// PLC tag in a state the generator cannot emit.
    /// </summary>
    public sealed class NodeEditingLogicTests
    {
        [Fact]
        public void CloningABlockCallProducesAnIndependentCopyWithTheSameIdentity()
        {
            var fixture = new Fixture();
            var source = fixture.BlockCall;

            var clone = (BlockCallNode)NodeEditingLogic.CloneSupportedNode(source);

            Assert.NotSame(source, clone);
            Assert.Equal(source.Id, clone.Id);
            Assert.Equal(source.BlockDefinitionId, clone.BlockDefinitionId);
            Assert.Equal(source.InstanceName, clone.InstanceName);
            Assert.Equal(source.X, clone.X);
            Assert.Equal(source.Y, clone.Y);
            Assert.Equal(
                source.Pins.Select(pin => pin.Id).ToList(),
                clone.Pins.Select(pin => pin.Id).ToList());
            Assert.All(
                source.Pins.Zip(clone.Pins, (left, right) => new { left, right }),
                pair => Assert.NotSame(pair.left, pair.right));

            clone.InstanceName = "Changed";
            clone.Pins[0].DataType = "Int";

            Assert.NotEqual("Changed", source.InstanceName);
            Assert.NotEqual("Int", source.Pins[0].DataType);
        }

        [Fact]
        public void CloningAConstantAndATagKeepsTheirPayload()
        {
            var fixture = new Fixture();

            var constant = (ConstantNode)NodeEditingLogic.CloneSupportedNode(fixture.Constant);
            var tag = (TagNode)NodeEditingLogic.CloneSupportedNode(fixture.TagNode);

            Assert.Equal(fixture.Constant.Literal, constant.Literal);
            Assert.Equal(fixture.Constant.DataType, constant.DataType);
            Assert.Equal(fixture.TagNode.TagDefinitionId, tag.TagDefinitionId);
            Assert.Equal(fixture.TagNode.TagName, tag.TagName);
            Assert.Equal(fixture.TagNode.DataType, tag.DataType);
            Assert.Equal(fixture.TagNode.TerminalDirection, tag.TerminalDirection);
        }

        [Fact]
        public void TheEditorRefusesNodeKindsItCannotDescribe()
        {
            Assert.Throws<NotSupportedException>(() => NodeEditingLogic.CloneSupportedNode(
                DiagramNodeFactory.CreateLogic(LogicOperation.And, "Bool", 0.0, 0.0)));
            Assert.Throws<NotSupportedException>(() =>
                NodeEditingLogic.CloneSupportedNode(new NoteNode { Text = "note" }));
            Assert.Throws<ArgumentNullException>(() => NodeEditingLogic.CloneSupportedNode(null));
        }

        [Fact]
        public void CloningATagDefinitionCopiesEveryFieldAndNeverLeavesANull()
        {
            var source = new TagDefinition
            {
                Name = "Start",
                DataType = "Bool",
                Address = "%I0.0",
                Comment = null
            };

            var clone = NodeEditingLogic.CloneTag(source);

            Assert.NotSame(source, clone);
            Assert.Equal(source.Id, clone.Id);
            Assert.Equal("Start", clone.Name);
            Assert.Equal("Bool", clone.DataType);
            Assert.Equal("%I0.0", clone.Address);
            Assert.Equal(string.Empty, clone.Comment);
            Assert.Throws<ArgumentNullException>(() => NodeEditingLogic.CloneTag(null));
        }

        [Fact]
        public void TheResultObjectsTrimEveryValueTheyCarry()
        {
            var fixture = new Fixture();

            var block = NodeEditingLogic.CreateBlockCallResult(fixture.BlockCall, "  Motor2  ", true);
            var constant = NodeEditingLogic.CreateConstantResult(fixture.Constant, "  5  ", "  Int  ");
            var tag = NodeEditingLogic.CreateTagResult(
                fixture.TagNode,
                new TagDefinition
                {
                    Name = "  Renamed  ",
                    DataType = "  Bool  ",
                    Address = "  %I0.1  ",
                    Comment = " keep me "
                });

            Assert.Equal("Motor2", block.InstanceName);
            Assert.True(block.UseMultiInstance);
            Assert.Equal(CallNodeKind.BlockCall, block.Kind);
            Assert.Equal("5", constant.Literal);
            Assert.Equal("Int", constant.DataType);
            Assert.Equal("Renamed", tag.TagName);
            Assert.Equal("Bool", tag.DataType);
            Assert.Equal("%I0.1", tag.TagAddress);
            Assert.Equal(" keep me ", tag.TagComment);
            Assert.Null(tag.TargetCpuFamily);
        }

        [Fact]
        public void TheResultFactoriesRejectNullArguments()
        {
            var fixture = new Fixture();

            Assert.Throws<ArgumentNullException>(() =>
                NodeEditingLogic.CreateBlockCallResult(null, "Motor2", false));
            Assert.Throws<ArgumentNullException>(() =>
                NodeEditingLogic.CreateConstantResult(null, "5", "Int"));
            Assert.Throws<ArgumentNullException>(() =>
                NodeEditingLogic.CreateTagResult(null, new TagDefinition()));
            Assert.Throws<ArgumentNullException>(() =>
                NodeEditingLogic.CreateTagResult(fixture.TagNode, null));
        }

        [Fact]
        public void ValidateRejectsNullArguments()
        {
            var fixture = new Fixture();

            Assert.Throws<ArgumentNullException>(() => NodeEditingLogic.ValidateCandidate(
                null,
                NodeEditingLogic.CreateBlockCallResult(fixture.BlockCall, "Motor1", false)));
            Assert.Throws<ArgumentNullException>(() =>
                NodeEditingLogic.ValidateCandidate(fixture.Project, null));
        }

        [Fact]
        public void AnUnchangedBlockCallValidatesCleanly()
        {
            var fixture = new Fixture();

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateBlockCallResult(fixture.BlockCall, "Motor1", false));

            Assert.Empty(errors);
        }

        [Fact]
        public void ANodeThatLeftTheProjectIsReported()
        {
            var fixture = new Fixture();
            var candidate = NodeEditingLogic.CreateBlockCallResult(fixture.BlockCall, "Motor2", false);
            fixture.Sheet.Nodes.Remove(fixture.BlockCall);

            var errors = NodeEditingLogic.ValidateCandidate(fixture.Project, candidate);

            Assert.Contains(errors, error => error.Contains("больше не найден"));
        }

        [Fact]
        public void ANodeIdThatAppearsOnTwoSheetsBlocksTheEdit()
        {
            var fixture = new Fixture();
            var second = new CallSheet { Name = "FC_Second" };
            var twin = DiagramNodeFactory.CreateBlockCall(fixture.FunctionBlock, "Motor9", 0.0, 0.0);
            twin.Id = fixture.BlockCall.Id;
            second.Nodes.Add(twin);
            fixture.Project.Sheets.Add(second);

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateBlockCallResult(fixture.BlockCall, "Motor2", false));

            Assert.Contains(errors, error => error.Contains("нескольких листах"));
        }

        [Fact]
        public void AKindThatNoLongerMatchesTheNodeBlocksTheEdit()
        {
            var fixture = new Fixture();
            var candidate = NodeEditingLogic.CreateConstantResult(fixture.Constant, "5", "Int");
            var constantId = fixture.Constant.Id;
            fixture.Sheet.Nodes.Remove(fixture.Constant);
            fixture.BlockCall.Id = constantId;

            var errors = NodeEditingLogic.ValidateCandidate(fixture.Project, candidate);

            Assert.Contains(errors, error => error.Contains("Тип редактируемого узла"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("1Motor")]
        [InlineData("Motor 2")]
        [InlineData("END_FUNCTION")]
        public void AnInstanceNameThatIsNotAnSclIdentifierIsReported(string name)
        {
            var fixture = new Fixture();

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateBlockCallResult(fixture.BlockCall, name, false));

            Assert.Contains(errors, error => error.Contains("Имя экземпляра"));
        }

        [Fact]
        public void MultiInstanceIsReportedAsUnavailableForAnFc()
        {
            var fixture = new Fixture();
            var call = DiagramNodeFactory.CreateBlockCall(fixture.Function, "Scale1", 500.0, 60.0);
            fixture.Sheet.Nodes.Add(call);

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateBlockCallResult(call, "Scale1", true));

            Assert.Contains(errors, error => error.Contains("FC не поддерживает multi-instance"));
        }

        [Fact]
        public void AnInstanceNameAlreadyUsedOnTheSameSheetIsReported()
        {
            var fixture = new Fixture();
            var second = DiagramNodeFactory.CreateBlockCall(fixture.FunctionBlock, "Motor2", 600.0, 60.0);
            fixture.Sheet.Nodes.Add(second);

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateBlockCallResult(fixture.BlockCall, "motor2", false));

            Assert.Contains(errors, error => error.Contains("уже используется"));
        }

        [Fact]
        public void AnInstanceDbNameThatCollidesWithTheLibraryIsReported()
        {
            var fixture = new Fixture();

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateBlockCallResult(fixture.BlockCall, "FB_Motor", false));

            Assert.Contains(errors, error => error.Contains("конфликтует"));
        }

        [Fact]
        public void ApplyingABlockCallEditWritesTheTrimmedNameAndTheInstanceMode()
        {
            var fixture = new Fixture();

            NodeEditingLogic.Apply(
                fixture.Project,
                NodeEditingLogic.CreateBlockCallResult(fixture.BlockCall, "  Motor7  ", true));

            Assert.Equal("Motor7", fixture.BlockCall.InstanceName);
            Assert.True(fixture.BlockCall.UseMultiInstance);
        }

        [Fact]
        public void AnFcCallCanNeverEndUpFlaggedAsMultiInstance()
        {
            var fixture = new Fixture();
            var call = DiagramNodeFactory.CreateBlockCall(fixture.Function, "Scale1", 500.0, 60.0);
            fixture.Sheet.Nodes.Add(call);

            NodeEditingLogic.Apply(
                fixture.Project,
                NodeEditingLogic.CreateBlockCallResult(call, "Scale2", false));

            Assert.Equal("Scale2", call.InstanceName);
            Assert.False(call.UseMultiInstance);
        }

        [Fact]
        public void ApplyRefusesToRunWhenValidationFoundAnything()
        {
            var fixture = new Fixture();
            var before = fixture.BlockCall.InstanceName;

            Assert.Throws<InvalidOperationException>(() => NodeEditingLogic.Apply(
                fixture.Project,
                NodeEditingLogic.CreateBlockCallResult(fixture.BlockCall, "1Motor", false)));

            Assert.Equal(before, fixture.BlockCall.InstanceName);
        }

        [Fact]
        public void ApplyRejectsNullArguments()
        {
            var fixture = new Fixture();

            Assert.Throws<ArgumentNullException>(() => NodeEditingLogic.Apply(
                null,
                NodeEditingLogic.CreateBlockCallResult(fixture.BlockCall, "Motor1", false)));
            Assert.Throws<ArgumentNullException>(() =>
                NodeEditingLogic.Apply(fixture.Project, null));
        }

        [Theory]
        [InlineData("", "Int", "не должно быть пустым")]
        [InlineData("5; DELETE", "Int", "однострочным")]
        [InlineData("5 // comment", "Int", "однострочным")]
        [InlineData("5", "", "тип данных")]
        [InlineData("5", "Void", "Void")]
        public void AConstantThatCannotBeEmittedIsReported(
            string literal,
            string dataType,
            string expected)
        {
            var fixture = new Fixture();

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateConstantResult(fixture.Constant, literal, dataType));

            Assert.Contains(errors, error => error.Contains(expected));
        }

        [Fact]
        public void ApplyingAConstantEditRewritesItsLiteralTypeTitleAndPin()
        {
            var fixture = new Fixture();

            NodeEditingLogic.Apply(
                fixture.Project,
                NodeEditingLogic.CreateConstantResult(fixture.Constant, " 42 ", " Int "));

            Assert.Equal("42", fixture.Constant.Literal);
            Assert.Equal("Int", fixture.Constant.DataType);
            Assert.Equal("42", fixture.Constant.Title);
            Assert.Equal("Int", fixture.Constant.Pins[0].DataType);
        }

        /// <summary>
        /// The constant feeds a Bool input, so retyping it to Int would leave a wire
        /// joining two incompatible ends.
        /// </summary>
        [Fact]
        public void RetypingAWiredConstantIntoAnIncompatibleTypeIsReported()
        {
            var fixture = new Fixture();
            fixture.WireConstantIntoTheCall();

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateConstantResult(fixture.Constant, "42", "Int"));

            Assert.Contains(errors, error => error.Contains("несовместим"));
            Assert.Throws<InvalidOperationException>(() => NodeEditingLogic.Apply(
                fixture.Project,
                NodeEditingLogic.CreateConstantResult(fixture.Constant, "42", "Int")));
            Assert.Equal("TRUE", fixture.Constant.Literal);
        }

        [Fact]
        public void ADamagedTerminalContractStopsTheConstantEdit()
        {
            var fixture = new Fixture();
            fixture.Constant.Pins.Add(new Pin
            {
                NodeId = fixture.Constant.Id,
                Name = "Extra",
                Direction = PinDirection.Output,
                Role = PinRole.Terminal,
                DataType = "Bool"
            });

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateConstantResult(fixture.Constant, "FALSE", "Bool"));

            Assert.Contains(errors, error => error.Contains("Контракт терминала"));
        }

        [Fact]
        public void AnUnchangedTagValidatesCleanly()
        {
            var fixture = new Fixture();

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateTagResult(fixture.TagNode, fixture.Tag));

            Assert.Empty(errors);
        }

        [Fact]
        public void ApplyingATagEditRenamesTheDefinitionAndEveryNodeBoundToIt()
        {
            var fixture = new Fixture();
            var secondNode = DiagramNodeFactory.CreateTag(
                fixture.Tag,
                TerminalDirection.Source,
                60.0,
                400.0);
            fixture.Sheet.Nodes.Add(secondNode);

            NodeEditingLogic.Apply(
                fixture.Project,
                NodeEditingLogic.CreateTagResult(
                    fixture.TagNode,
                    new TagDefinition
                    {
                        Id = fixture.Tag.Id,
                        Name = "Started",
                        DataType = "Bool",
                        Address = "i0.1",
                        Comment = "renamed"
                    }));

            Assert.Equal("Started", fixture.Tag.Name);
            Assert.Equal("renamed", fixture.Tag.Comment);
            Assert.Equal("%I0.1", fixture.Tag.Address);
            Assert.Equal("Started", fixture.TagNode.TagName);
            Assert.Equal("Started", fixture.TagNode.Title);
            Assert.Equal("Started", fixture.TagNode.Pins[0].Name);
            Assert.Equal("Started", secondNode.TagName);
            Assert.Equal("Started", secondNode.Pins[0].Name);
        }

        [Theory]
        [InlineData("", "Bool", "%I0.0", "Имя PLC-тега")]
        [InlineData("Start", "", "%I0.0", "тип данных")]
        [InlineData("Start", "Void", "%I0.0", "Void")]
        [InlineData("Start", "Bool", "", "адрес")]
        [InlineData("Start", "Bool", "%QW9999999", "Адрес PLC-тега")]
        public void ATagThatCannotBeEmittedIsReported(
            string name,
            string dataType,
            string address,
            string expected)
        {
            var fixture = new Fixture();

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateTagResult(
                    fixture.TagNode,
                    new TagDefinition
                    {
                        Id = fixture.Tag.Id,
                        Name = name,
                        DataType = dataType,
                        Address = address
                    }));

            Assert.Contains(errors, error => error.Contains(expected));
        }

        [Fact]
        public void ATagCommentThatXmlCannotCarryIsReported()
        {
            var fixture = new Fixture();

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateTagResult(
                    fixture.TagNode,
                    new TagDefinition
                    {
                        Id = fixture.Tag.Id,
                        Name = "Start",
                        DataType = "Bool",
                        Address = "%I0.0",
                        Comment = "bad\u0001comment"
                    }));

            Assert.Contains(errors, error => error.Contains("XML"));
        }

        [Fact]
        public void ATagNameThatCollidesWithTheLibraryIsReported()
        {
            var fixture = new Fixture();

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateTagResult(
                    fixture.TagNode,
                    new TagDefinition
                    {
                        Id = fixture.Tag.Id,
                        Name = "FB_Motor",
                        DataType = "Bool",
                        Address = "%I0.0"
                    }));

            Assert.Contains(errors, error => error.Contains("конфликтует"));
        }

        [Fact]
        public void ATerminalDirectionOutsideTheEnumIsReported()
        {
            var fixture = new Fixture();
            fixture.TagNode.TerminalDirection = (TerminalDirection)7;

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateTagResult(fixture.TagNode, fixture.Tag));

            Assert.Contains(errors, error => error.Contains("Направление"));
        }

        [Fact]
        public void SwitchingTheTargetCpuAlsoRewritesTheProjectCpuFamily()
        {
            var fixture = new Fixture();

            NodeEditingLogic.Apply(
                fixture.Project,
                NodeEditingLogic.CreateTagResult(
                    fixture.TagNode,
                    fixture.Tag,
                    PlcCpuFamily.S71500));

            Assert.Equal(PlcCpuFamily.S71500, fixture.Project.Plant.CpuFamily);
        }

        [Fact]
        public void ATargetCpuThatIsNotSupportedIsReported()
        {
            var fixture = new Fixture();

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateTagResult(
                    fixture.TagNode,
                    fixture.Tag,
                    PlcCpuFamily.Unknown));

            Assert.Contains(errors, error => error.Contains("S7-1200"));
        }

        [Fact]
        public void AnUnsupportedButInternallyConsistentNodeKindIsReported()
        {
            var fixture = new Fixture();
            var logic = DiagramNodeFactory.CreateLogic(LogicOperation.And, "Bool", 0.0, 0.0);
            fixture.Sheet.Nodes.Add(logic);

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                CreateRawResult(logic.Id, CallNodeKind.Logic));

            Assert.Single(errors);
        }

        [Fact]
        public void ABlockReferenceChangedAfterOpeningTheEditorIsReported()
        {
            var fixture = new Fixture();
            var candidate = NodeEditingLogic.CreateBlockCallResult(fixture.BlockCall, "Motor1", false);
            fixture.BlockCall.BlockDefinitionId = Guid.NewGuid();

            Assert.NotEmpty(NodeEditingLogic.ValidateCandidate(fixture.Project, candidate));
        }

        [Fact]
        public void MissingAndDuplicateBlockDefinitionsAreReported()
        {
            var missing = new Fixture();
            var missingCandidate = NodeEditingLogic.CreateBlockCallResult(
                missing.BlockCall,
                "Motor1",
                false);
            missing.Project.Plant.Blocks.Remove(missing.FunctionBlock);

            var duplicate = new Fixture();
            duplicate.Project.Plant.Blocks.Add(new BlockDefinition
            {
                Id = duplicate.FunctionBlock.Id,
                Name = "FB_Duplicate",
                Kind = BlockKind.FunctionBlock
            });

            Assert.NotEmpty(NodeEditingLogic.ValidateCandidate(missing.Project, missingCandidate));
            Assert.NotEmpty(NodeEditingLogic.ValidateCandidate(
                duplicate.Project,
                NodeEditingLogic.CreateBlockCallResult(duplicate.BlockCall, "Motor1", false)));
        }

        [Fact]
        public void MultiInstanceSheetDbMustBeAValidUniqueGlobalSymbol()
        {
            var invalid = new Fixture();
            invalid.Sheet.Name = new string('A', SclName.MaximumLength);

            var collision = new Fixture();
            collision.Project.Plant.Tags.Add(new TagDefinition
            {
                Name = "FC_Units_DB",
                DataType = "Bool",
                Address = "%M1.0"
            });

            Assert.NotEmpty(NodeEditingLogic.ValidateCandidate(
                invalid.Project,
                NodeEditingLogic.CreateBlockCallResult(invalid.BlockCall, "Motor1", true)));
            Assert.NotEmpty(NodeEditingLogic.ValidateCandidate(
                collision.Project,
                NodeEditingLogic.CreateBlockCallResult(collision.BlockCall, "Motor1", true)));
        }

        [Fact]
        public void ANodeOnlyPretendingToBeAConstantIsRejected()
        {
            var fixture = new Fixture();
            var fake = new PretendConstantNode { Id = fixture.Constant.Id };
            fixture.Sheet.Nodes[fixture.Sheet.Nodes.IndexOf(fixture.Constant)] = fake;

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                CreateRawResult(fake.Id, CallNodeKind.Constant, literal: "TRUE", dataType: "Bool"));

            Assert.Single(errors);
        }

        [Fact]
        public void ATagReferenceChangedAfterOpeningTheEditorIsReported()
        {
            var fixture = new Fixture();
            var candidate = NodeEditingLogic.CreateTagResult(fixture.TagNode, fixture.Tag);
            fixture.TagNode.TagDefinitionId = Guid.NewGuid();

            Assert.NotEmpty(NodeEditingLogic.ValidateCandidate(fixture.Project, candidate));
        }

        [Fact]
        public void MissingAndDuplicateTagDefinitionsAreReported()
        {
            var missing = new Fixture();
            var missingCandidate = NodeEditingLogic.CreateTagResult(missing.TagNode, missing.Tag);
            missing.Project.Plant.Tags.Remove(missing.Tag);

            var duplicate = new Fixture();
            duplicate.Project.Plant.Tags.Add(new TagDefinition
            {
                Id = duplicate.Tag.Id,
                Name = "StartDuplicate",
                DataType = "Bool",
                Address = "%I0.1"
            });

            Assert.NotEmpty(NodeEditingLogic.ValidateCandidate(missing.Project, missingCandidate));
            Assert.NotEmpty(NodeEditingLogic.ValidateCandidate(
                duplicate.Project,
                NodeEditingLogic.CreateTagResult(duplicate.TagNode, duplicate.Tag)));
        }

        [Fact]
        public void CpuMigrationAlsoValidatesEveryExistingTag()
        {
            var fixture = new Fixture();
            fixture.Project.Plant.Tags.Add(new TagDefinition
            {
                Name = "BrokenExisting",
                DataType = "Bool",
                Address = "not-an-address"
            });

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateTagResult(
                    fixture.TagNode,
                    fixture.Tag,
                    PlcCpuFamily.S71500));

            Assert.Contains(errors, error => error.Contains("BrokenExisting"));
        }

        [Fact]
        public void EmptyDuplicateAndProjectWideDuplicatePinIdsAreRejected()
        {
            var empty = new Fixture();
            empty.Constant.Pins[0].Id = Guid.Empty;

            var linkedDuplicate = new Fixture();
            var secondLinked = DiagramNodeFactory.CreateTag(
                linkedDuplicate.Tag,
                TerminalDirection.Source,
                0.0,
                0.0);
            secondLinked.Pins[0].Id = linkedDuplicate.TagNode.Pins[0].Id;
            linkedDuplicate.Sheet.Nodes.Add(secondLinked);

            var projectDuplicate = new Fixture();
            var unrelated = DiagramNodeFactory.CreateTag(
                new TagDefinition { Name = "Other", DataType = "Bool", Address = "%I0.1" },
                TerminalDirection.Source,
                0.0,
                0.0);
            unrelated.Pins[0].Id = projectDuplicate.Constant.Pins[0].Id;
            projectDuplicate.Sheet.Nodes.Add(unrelated);

            Assert.NotEmpty(NodeEditingLogic.ValidateCandidate(
                empty.Project,
                NodeEditingLogic.CreateConstantResult(empty.Constant, "TRUE", "Bool")));
            Assert.NotEmpty(NodeEditingLogic.ValidateCandidate(
                linkedDuplicate.Project,
                NodeEditingLogic.CreateTagResult(linkedDuplicate.TagNode, linkedDuplicate.Tag)));
            Assert.NotEmpty(NodeEditingLogic.ValidateCandidate(
                projectDuplicate.Project,
                NodeEditingLogic.CreateConstantResult(projectDuplicate.Constant, "TRUE", "Bool")));
        }

        [Fact]
        public void AWireWithAMissingOppositeEndBlocksRetyping()
        {
            var fixture = new Fixture();
            fixture.Sheet.Wires.Add(new Wire
            {
                SourceNodeId = fixture.Constant.Id,
                SourcePinId = fixture.Constant.Pins[0].Id,
                TargetNodeId = Guid.NewGuid(),
                TargetPinId = Guid.NewGuid()
            });

            var errors = NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateConstantResult(fixture.Constant, "TRUE", "Bool"));

            Assert.NotEmpty(errors);
        }

        [Theory]
        [InlineData("udt")]
        [InlineData("call-block")]
        [InlineData("call-block-db")]
        [InlineData("unit-db")]
        [InlineData("sheet-db")]
        public void TagRenameChecksEveryGlobalSymbolCategory(string category)
        {
            var fixture = new Fixture();
            string collisionName;

            switch (category)
            {
                case "udt":
                    collisionName = "Valve_Data";
                    fixture.Project.Plant.DataTypes.Add(new UdtDefinition { Name = collisionName });
                    break;
                case "call-block":
                    collisionName = "FC_Coordinator";
                    fixture.Project.Plant.CallBlocks.Add(new CallBlockDefinition { Name = collisionName });
                    break;
                case "call-block-db":
                    collisionName = "FB_Coordinator_DB";
                    fixture.Project.Plant.CallBlocks.Add(new CallBlockDefinition
                    {
                        Name = "FB_Coordinator",
                        Kind = BlockKind.FunctionBlock,
                        InstanceDbName = string.Empty
                    });
                    break;
                case "unit-db":
                    collisionName = "MotorFromCallBlock";
                    var callBlock = new CallBlockDefinition { Name = "FC_Coordinator" };
                    callBlock.Units.Add(new UnitDefinition
                    {
                        Name = collisionName,
                        BlockId = Guid.NewGuid(),
                        BlockName = fixture.FunctionBlock.Name,
                        UseMultiInstance = false
                    });
                    fixture.Project.Plant.CallBlocks.Add(callBlock);
                    break;
                default:
                    collisionName = "FC_Other_DB";
                    var otherSheet = new CallSheet { Name = "FC_Other" };
                    var call = DiagramNodeFactory.CreateBlockCall(
                        fixture.FunctionBlock,
                        "OtherMotor",
                        0.0,
                        0.0);
                    call.UseMultiInstance = true;
                    otherSheet.Nodes.Add(call);
                    fixture.Project.Sheets.Add(otherSheet);
                    break;
            }

            var candidateTag = new TagDefinition
            {
                Id = fixture.Tag.Id,
                Name = collisionName,
                DataType = "Bool",
                Address = "%I0.0"
            };

            Assert.NotEmpty(NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                NodeEditingLogic.CreateTagResult(fixture.TagNode, candidateTag)));
        }

        [Theory]
        [InlineData(" Motor")]
        [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        public void NameDiagnosticsTranslateWhitespaceAndLengthErrors(string name)
        {
            var fixture = new Fixture();

            Assert.NotEmpty(NodeEditingLogic.ValidateCandidate(
                fixture.Project,
                CreateRawResult(
                    fixture.BlockCall.Id,
                    CallNodeKind.BlockCall,
                    fixture.FunctionBlock.Id,
                    instanceName: name)));
        }

        [Fact]
        public void UnknownNameDiagnosticHasASafeFallback()
        {
            var method = typeof(NodeEditingLogic).GetMethod(
                "TranslateSclNameError",
                BindingFlags.NonPublic | BindingFlags.Static);

            var translated = (string)method.Invoke(null, new object[] { "future validation error" });

            Assert.False(string.IsNullOrWhiteSpace(translated));
        }

        [Fact]
        public void EmptyAffectedPinSetAndCompleteSymbolEnumerationAreSafe()
        {
            var fixture = new Fixture();
            var callBlock = new CallBlockDefinition { Name = "FC_Complete" };
            callBlock.Units.Add(new UnitDefinition
            {
                Name = "CompleteMotor",
                BlockId = fixture.FunctionBlock.Id,
                BlockName = fixture.FunctionBlock.Name
            });
            fixture.Project.Plant.CallBlocks.Add(callBlock);
            var errors = new List<string>();
            var wireValidation = typeof(NodeEditingLogic).GetMethod(
                "ValidateAffectedWireTypes",
                BindingFlags.NonPublic | BindingFlags.Static);
            wireValidation.Invoke(
                null,
                new object[] { fixture.Project, new Guid[0], "Bool", errors });

            var symbolEnumeration = typeof(NodeEditingLogic).GetMethod(
                "EnumerateGlobalSymbols",
                BindingFlags.NonPublic | BindingFlags.Static);
            var symbols = (System.Collections.IEnumerable)symbolEnumeration.Invoke(
                null,
                new object[] { fixture.Project, null, null, null, null });
            var count = 0;
            foreach (var symbol in symbols)
            {
                Assert.NotNull(symbol);
                count++;
            }

            Assert.Empty(errors);
            Assert.True(count > 0);
        }

        private static NodeEditResult CreateRawResult(
            Guid nodeId,
            CallNodeKind kind,
            Guid blockDefinitionId = default(Guid),
            string instanceName = "",
            string literal = "",
            string dataType = "")
        {
            return new NodeEditResult(
                nodeId,
                kind,
                blockDefinitionId,
                instanceName,
                false,
                literal,
                dataType,
                Guid.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                TerminalDirection.Source,
                null);
        }

        private sealed class PretendConstantNode : CallNode
        {
            public override CallNodeKind Kind { get { return CallNodeKind.Constant; } }
        }

        private sealed class Fixture
        {
            internal Fixture()
            {
                Project = new DiagramProject();
                Project.Plant.CpuFamily = PlcCpuFamily.S71200;

                FunctionBlock = new BlockDefinition
                {
                    Name = "FB_Motor",
                    Kind = BlockKind.FunctionBlock,
                    ReturnType = "Void"
                };
                FunctionBlock.Interface.Add(
                    new InterfaceMember("Enable", "Bool", InterfaceSection.Input));
                FunctionBlock.Interface.Add(
                    new InterfaceMember("Running", "Bool", InterfaceSection.Output));

                Function = new BlockDefinition
                {
                    Name = "FC_Scale",
                    Kind = BlockKind.Function,
                    ReturnType = "Void"
                };
                Function.Interface.Add(new InterfaceMember("In", "Int", InterfaceSection.Input));

                Project.Plant.Blocks.Add(FunctionBlock);
                Project.Plant.Blocks.Add(Function);

                Tag = new TagDefinition
                {
                    Name = "Start",
                    DataType = "Bool",
                    Address = "%I0.0",
                    Comment = string.Empty
                };
                Project.Plant.Tags.Add(Tag);

                Sheet = new CallSheet { Name = "FC_Units", Width = 1180.0, Height = 700.0 };
                Project.Sheets.Add(Sheet);

                BlockCall = DiagramNodeFactory.CreateBlockCall(FunctionBlock, "Motor1", 300.0, 60.0);
                Constant = DiagramNodeFactory.CreateConstant("TRUE", "Bool", 60.0, 60.0);
                TagNode = DiagramNodeFactory.CreateTag(Tag, TerminalDirection.Source, 60.0, 240.0);
                Sheet.Nodes.Add(BlockCall);
                Sheet.Nodes.Add(Constant);
                Sheet.Nodes.Add(TagNode);
            }

            internal DiagramProject Project { get; private set; }

            internal CallSheet Sheet { get; private set; }

            internal BlockDefinition FunctionBlock { get; private set; }

            internal BlockDefinition Function { get; private set; }

            internal TagDefinition Tag { get; private set; }

            internal BlockCallNode BlockCall { get; private set; }

            internal ConstantNode Constant { get; private set; }

            internal TagNode TagNode { get; private set; }

            internal void WireConstantIntoTheCall()
            {
                var target = BlockCall.Pins.First(pin => pin.Direction == PinDirection.Input);
                Sheet.Wires.Add(new Wire
                {
                    SourceNodeId = Constant.Id,
                    SourcePinId = Constant.Pins[0].Id,
                    TargetNodeId = BlockCall.Id,
                    TargetPinId = target.Id
                });
            }
        }
    }
}
