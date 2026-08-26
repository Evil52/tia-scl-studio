using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Generation;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Diagram.Tests
{
    public sealed class DiagramSclGeneratorCoverageTests
    {
        private static IList<DiagramIssue> ValidateGenerationInputs(PlantProject project, CallSheet sheet)
        {
            var definitions = project.Blocks
                .GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var tags = project.Tags
                .GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var issues = new List<DiagramIssue>();
            typeof(DiagramSclGenerator)
                .GetMethod("ValidateGenerationInputs", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { project, sheet, definitions, tags, issues });
            return issues;
        }

        private static bool HasCode(IEnumerable<DiagramIssue> issues, string code)
        {
            return issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal));
        }

        private static Pin AddNoteOutput(NoteNode note, string dataType)
        {
            var pin = new Pin
            {
                NodeId = note.Id,
                Name = "Out",
                DataType = dataType,
                Direction = PinDirection.Output,
                Role = PinRole.Terminal
            };
            note.Pins.Add(pin);
            return pin;
        }

        [Fact]
        public void GlobalSymbolDiscoveryIncludesCallBlocksDatabasesAndLegacyUnitFallbacks()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("FB_Unit");
            project.Plant.Blocks.Add(block);
            var callBlock = new CallBlockDefinition
            {
                Name = "FB_Controller",
                Kind = BlockKind.FunctionBlock,
                InstanceDbName = " "
            };
            callBlock.Units.Add(new UnitDefinition
            {
                Name = "Unit01",
                BlockId = Guid.NewGuid(),
                BlockName = block.Name,
                UseMultiInstance = false
            });
            project.Plant.CallBlocks.Add(callBlock);
            project.Sheets[0].Name = "Unit01";

            var issues = ValidateGenerationInputs(project.Plant, project.Sheets[0]);

            Assert.True(HasCode(issues, "GEN012"));
        }

        [Fact]
        public void DefensiveGenerationValidationReportsEveryStaleBlockContract()
        {
            var project = ModelBuilder.Project();
            var function = ModelBuilder.Function("FC_Value", "Int", ModelBuilder.Input("In"));
            var inOutBlock = ModelBuilder.FunctionBlock("FB_InOut", ModelBuilder.InOut("Data"));
            project.Plant.Blocks.Add(function);
            project.Plant.Blocks.Add(inOutBlock);
            var sheet = project.Sheets[0];

            var missingDefinition = ModelBuilder.PlaceBlock(sheet, function, "Missing");
            missingDefinition.BlockDefinitionId = Guid.NewGuid();

            var multiFunction = ModelBuilder.PlaceBlock(sheet, function, "MultiFc");
            multiFunction.UseMultiInstance = true;

            var staleMember = ModelBuilder.PlaceBlock(sheet, function, "StaleMember");
            staleMember.Pins.First(pin => pin.Role == PinRole.Parameter).MemberId = Guid.NewGuid();

            var missingPin = ModelBuilder.PlaceBlock(sheet, function, "MissingPin");
            missingPin.Pins.RemoveAll(pin => pin.Role == PinRole.Parameter);

            var staleReturnContract = ModelBuilder.PlaceBlock(sheet, function, "StaleReturnContract");
            staleReturnContract.Pins.RemoveAll(pin => pin.Role == PinRole.FunctionReturn);

            var staleReturnPin = ModelBuilder.PlaceBlock(sheet, function, "StaleReturnPin");
            staleReturnPin.Pins.Single(pin => pin.Role == PinRole.FunctionReturn).Direction = PinDirection.Input;

            var foreignRole = ModelBuilder.PlaceBlock(sheet, function, "ForeignRole");
            foreignRole.Pins.Add(new Pin
            {
                NodeId = foreignRole.Id,
                Name = "Foreign",
                DataType = "Bool",
                Direction = PinDirection.Input,
                Role = PinRole.Terminal
            });

            ModelBuilder.PlaceBlock(sheet, inOutBlock, "InOut");

            var issues = ValidateGenerationInputs(project.Plant, sheet);

            foreach (var code in new[] { "GEN001", "GEN002", "GEN003", "GEN006", "GEN007", "GEN015", "GEN016", "GEN017" })
            {
                Assert.True(HasCode(issues, code), "Missing diagnostic " + code);
            }
        }

        [Fact]
        public void DefensiveGenerationValidationReportsEveryStaleTerminalContract()
        {
            var project = ModelBuilder.Project();
            var validTag = ModelBuilder.Tag("ValidTag", "Bool", "%M0.0");
            project.Plant.Tags.Add(validTag);
            var sheet = project.Sheets[0];

            var missingTag = ModelBuilder.PlaceTag(sheet, validTag, TerminalDirection.Source);
            missingTag.TagDefinitionId = Guid.NewGuid();

            var staleTag = ModelBuilder.PlaceTag(sheet, validTag, TerminalDirection.Source);
            staleTag.DataType = "Int";

            var staleTagPin = ModelBuilder.PlaceTag(sheet, validTag, TerminalDirection.Source);
            staleTagPin.Pins[0].Role = PinRole.Parameter;

            var dbDefinition = ModelBuilder.DbVariable("DB_Data", "Value", "Bool");
            project.Plant.DataBlockVariables.Add(dbDefinition);
            var missingDb = ModelBuilder.PlaceDbVariable(sheet, dbDefinition, TerminalDirection.Source);
            missingDb.DefinitionId = Guid.NewGuid();

            var staleDb = ModelBuilder.PlaceDbVariable(sheet, dbDefinition, TerminalDirection.Source);
            staleDb.VariableName = "OldValue";

            var staleDbPin = ModelBuilder.PlaceDbVariable(sheet, dbDefinition, TerminalDirection.Source);
            staleDbPin.Pins[0].Role = PinRole.Parameter;

            var logic = ModelBuilder.PlaceLogic(sheet, LogicOperation.And);
            logic.Pins[0].DataType = "Int";

            var issues = ValidateGenerationInputs(project.Plant, sheet);

            foreach (var code in new[] { "GEN008", "GEN009", "GEN018", "GEN024", "GEN026", "GEN027", "GEN028" })
            {
                Assert.True(HasCode(issues, code), "Missing diagnostic " + code);
            }
        }

        [Fact]
        public void DefensiveGenerationValidationReportsInvalidAndCollidingCallSheetDatabaseNames()
        {
            var invalidProject = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("FB_A");
            invalidProject.Plant.Blocks.Add(block);
            ModelBuilder.PlaceBlock(invalidProject.Sheets[0], block, "A", useMultiInstance: true);
            invalidProject.Sheets[0].Name = "A" + new string('x', SclName.MaximumLength - 1);

            Assert.True(HasCode(
                ValidateGenerationInputs(invalidProject.Plant, invalidProject.Sheets[0]),
                "GEN021"));

            var collisionProject = ModelBuilder.Project();
            collisionProject.Plant.Blocks.Add(block);
            ModelBuilder.PlaceBlock(collisionProject.Sheets[0], block, "B", useMultiInstance: true);
            collisionProject.Plant.Tags.Add(ModelBuilder.Tag(collisionProject.Sheets[0].Name + "_DB"));

            Assert.True(HasCode(
                ValidateGenerationInputs(collisionProject.Plant, collisionProject.Sheets[0]),
                "GEN022"));
        }

        [Fact]
        public void BlockOutputFanOutUsesOneTemporaryAndEmitsEveryAssignment()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("FB_Source", ModelBuilder.Output("Value"));
            var firstTag = ModelBuilder.Tag("First", "Bool", "%M0.0");
            var secondTag = ModelBuilder.Tag("Second", "Bool", "%M0.1");
            project.Plant.Blocks.Add(block);
            project.Plant.Tags.Add(firstTag);
            project.Plant.Tags.Add(secondTag);
            var call = ModelBuilder.PlaceBlock(project.Sheets[0], block, "Source01");
            var first = ModelBuilder.PlaceTag(project.Sheets[0], firstTag, TerminalDirection.Sink);
            var second = ModelBuilder.PlaceTag(project.Sheets[0], secondTag, TerminalDirection.Sink);
            ModelBuilder.Connect(project.Sheets[0], call, "Value", first, "First");
            ModelBuilder.Connect(project.Sheets[0], call, "Value", second, "Second");

            var result = new DiagramSclGenerator().Generate(project.Plant, project.Sheets[0]);

            Assert.True(result.IsSuccess);
            Assert.Contains("Value => #tmp_Source01_Value", result.Scl, StringComparison.Ordinal);
            Assert.Contains("\"First\" := #tmp_Source01_Value;", result.Scl, StringComparison.Ordinal);
            Assert.Contains("\"Second\" := #tmp_Source01_Value;", result.Scl, StringComparison.Ordinal);
        }

        [Fact]
        public void LogicOutputSupportsDirectAndFanOutAssignments()
        {
            var direct = CreateLogicAssignmentProject(1);
            var directResult = new DiagramSclGenerator().Generate(direct.Plant, direct.Sheets[0]);
            Assert.True(directResult.IsSuccess);
            Assert.Contains("\"Sink0\" := (\"Left\" AND \"Right\");", directResult.Scl, StringComparison.Ordinal);

            var fanOut = CreateLogicAssignmentProject(2);
            var fanOutResult = new DiagramSclGenerator().Generate(fanOut.Plant, fanOut.Sheets[0]);
            Assert.True(fanOutResult.IsSuccess);
            Assert.Contains("#tmp_logic_And_Result := (\"Left\" AND \"Right\");", fanOutResult.Scl, StringComparison.Ordinal);
            Assert.Contains("\"Sink0\" := #tmp_logic_And_Result;", fanOutResult.Scl, StringComparison.Ordinal);
            Assert.Contains("\"Sink1\" := #tmp_logic_And_Result;", fanOutResult.Scl, StringComparison.Ordinal);
        }

        [Fact]
        public void UnresolvableNoteSourcesAreRejectedDuringPlanningAndInvocation()
        {
            var logicProject = CreateLogicAssignmentProject(1);
            var logic = logicProject.Sheets[0].Nodes.OfType<LogicNode>().Single();
            var original = logicProject.Sheets[0].Wires.Single(item =>
                item.TargetPinId == ModelBuilder.PinOf(logic, "In1").Id);
            logicProject.Sheets[0].Wires.Remove(original);
            var note = ModelBuilder.PlaceNote(logicProject.Sheets[0], "source");
            ModelBuilder.Connect(logicProject.Sheets[0], AddNoteOutput(note, "Bool"), ModelBuilder.PinOf(logic, "In1"));
            var spectatorBlock = ModelBuilder.FunctionBlock("FB_Spectator");
            logicProject.Plant.Blocks.Add(spectatorBlock);
            ModelBuilder.PlaceBlock(logicProject.Sheets[0], spectatorBlock, "Spectator01");

            var planningResult = new DiagramSclGenerator().Generate(logicProject.Plant, logicProject.Sheets[0]);
            Assert.False(planningResult.IsSuccess);
            Assert.Contains(planningResult.Issues, issue => issue.Code == "GEN025");

            var callProject = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("FB_Target", ModelBuilder.Input("In"));
            callProject.Plant.Blocks.Add(block);
            var call = ModelBuilder.PlaceBlock(callProject.Sheets[0], block, "Target01");
            var callNote = ModelBuilder.PlaceNote(callProject.Sheets[0], "source");
            ModelBuilder.Connect(callProject.Sheets[0], AddNoteOutput(callNote, "Bool"), ModelBuilder.PinOf(call, "In"));

            var invocationResult = new DiagramSclGenerator().Generate(callProject.Plant, callProject.Sheets[0]);
            Assert.False(invocationResult.IsSuccess);
            Assert.Contains(invocationResult.Issues, issue => issue.Code == "GEN020");
        }

        [Fact]
        public void PrivateExpressionDefencesReportMissingInputsUnknownOperationsAndUnknownSources()
        {
            var project = CreateLogicAssignmentProject(1);
            var sheet = project.Sheets[0];
            var logic = sheet.Nodes.OfType<LogicNode>().Single();
            sheet.Wires.RemoveAll(item => item.TargetPinId == ModelBuilder.PinOf(logic, "In1").Id);
            var issues = new List<DiagramIssue>();
            var outputPlans = CreateOutputPlanDictionary();

            var missing = InvokeBuildLogicExpression(project, logic, outputPlans, issues);
            Assert.Equal(string.Empty, missing);
            Assert.True(HasCode(issues, "GEN025"));

            var complete = CreateLogicAssignmentProject(1);
            var unknown = complete.Sheets[0].Nodes.OfType<LogicNode>().Single();
            unknown.Operation = (LogicOperation)999;
            issues.Clear();
            var unsupported = InvokeBuildLogicExpression(complete, unknown, outputPlans, issues);
            Assert.Equal(string.Empty, unsupported);
            Assert.True(HasCode(issues, "GEN023"));

            var note = ModelBuilder.PlaceNote(complete.Sheets[0], "source");
            var sourcePin = AddNoteOutput(note, "Bool");
            var targetPin = ModelBuilder.PinOf(unknown, "In1");
            var wire = new Wire
            {
                SourceNodeId = note.Id,
                SourcePinId = sourcePin.Id,
                TargetNodeId = unknown.Id,
                TargetPinId = targetPin.Id
            };
            issues.Clear();
            var unresolved = InvokeResolveSourceExpression(complete, wire, outputPlans, issues);
            Assert.Equal(string.Empty, unresolved);
            Assert.True(HasCode(issues, "GEN999"));
        }

        [Fact]
        public void TemporaryAndIdentifierHelpersCoverCollisionsBlankNamesAndLeadingDigits()
        {
            var generator = typeof(DiagramSclGenerator);
            var sanitize = generator.GetMethod("SanitizeIdentifier", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.Equal("value", sanitize.Invoke(null, new object[] { null }));
            Assert.Equal("_1_bad", sanitize.Invoke(null, new object[] { "1 bad" }));

            var unique = generator.GetMethod("MakeUniqueTemporaryName", BindingFlags.NonPublic | BindingFlags.Static);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name" };
            Assert.Equal("name_2", unique.Invoke(null, new object[] { "name", used }));
        }

        private static DiagramProject CreateLogicAssignmentProject(int sinkCount)
        {
            var project = ModelBuilder.Project();
            var leftDefinition = ModelBuilder.Tag("Left", "Bool", "%M0.0");
            var rightDefinition = ModelBuilder.Tag("Right", "Bool", "%M0.1");
            project.Plant.Tags.Add(leftDefinition);
            project.Plant.Tags.Add(rightDefinition);
            var left = ModelBuilder.PlaceTag(project.Sheets[0], leftDefinition, TerminalDirection.Source);
            var right = ModelBuilder.PlaceTag(project.Sheets[0], rightDefinition, TerminalDirection.Source);
            var logic = ModelBuilder.PlaceLogic(project.Sheets[0], LogicOperation.And);
            ModelBuilder.Connect(project.Sheets[0], left, "Left", logic, "In1");
            ModelBuilder.Connect(project.Sheets[0], right, "Right", logic, "In2");
            for (var index = 0; index < sinkCount; index++)
            {
                var definition = ModelBuilder.Tag("Sink" + index, "Bool", "%M0." + (index + 2));
                project.Plant.Tags.Add(definition);
                var sink = ModelBuilder.PlaceTag(project.Sheets[0], definition, TerminalDirection.Sink);
                ModelBuilder.Connect(project.Sheets[0], logic, "Result", sink, definition.Name);
            }

            return project;
        }

        private static object CreateOutputPlanDictionary()
        {
            var outputPlan = typeof(DiagramSclGenerator)
                .GetNestedType("OutputPlan", BindingFlags.NonPublic);
            return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(Guid), outputPlan));
        }

        private static string InvokeBuildLogicExpression(
            DiagramProject project,
            LogicNode logic,
            object outputPlans,
            IList<DiagramIssue> issues)
        {
            var sheet = project.Sheets[0];
            var nodes = sheet.Nodes.GroupBy(node => node.Id).ToDictionary(group => group.Key, group => group.First());
            var pins = sheet.Nodes.SelectMany(node => node.Pins).GroupBy(pin => pin.Id).ToDictionary(group => group.Key, group => group.First());
            var tags = project.Plant.Tags.GroupBy(tag => tag.Id).ToDictionary(group => group.Key, group => group.First());
            return (string)typeof(DiagramSclGenerator)
                .GetMethod("BuildLogicExpression", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { sheet, logic, nodes, pins, tags, outputPlans, issues });
        }

        private static string InvokeResolveSourceExpression(
            DiagramProject project,
            Wire wire,
            object outputPlans,
            IList<DiagramIssue> issues)
        {
            var sheet = project.Sheets[0];
            var nodes = sheet.Nodes.GroupBy(node => node.Id).ToDictionary(group => group.Key, group => group.First());
            var pins = sheet.Nodes.SelectMany(node => node.Pins).GroupBy(pin => pin.Id).ToDictionary(group => group.Key, group => group.First());
            var tags = project.Plant.Tags.GroupBy(tag => tag.Id).ToDictionary(group => group.Key, group => group.First());
            return (string)typeof(DiagramSclGenerator)
                .GetMethod("ResolveSourceExpression", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { wire, nodes, pins, tags, outputPlans, issues, "GEN999" });
        }
    }
}
