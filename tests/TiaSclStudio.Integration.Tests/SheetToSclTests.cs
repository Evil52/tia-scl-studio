using System;
using System.Linq;
using TiaSclStudio.Core.Generation;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Generation;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Integration.Tests
{
    /// <summary>
    /// One visual sheet, all the way to SCL. This is the path the product
    /// exists for, so the assertions are about the text an engineer will read
    /// in TIA Portal, not about intermediate objects.
    /// </summary>
    public sealed class SheetToSclTests
    {
        private static DiagramCompilationResult Compile(DiagramProject project)
        {
            return new DiagramSclGenerator().Generate(project.Plant, project.Sheets[0]);
        }

        private static string Issues(DiagramCompilationResult result)
        {
            return string.Join(
                "; ",
                result.Issues.Select(issue => issue.Code + " " + issue.Message));
        }

        [Fact]
        public void RejectsANullPlant()
        {
            Assert.Throws<ArgumentNullException>(
                () => new DiagramSclGenerator().Generate(null, ModelBuilder.Sheet()));
        }

        [Fact]
        public void ReportsAMissingSheetWithoutThrowing()
        {
            var result = new DiagramSclGenerator().Generate(ModelBuilder.Plant(), null);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Issues, issue => issue.Code == "DGM000");
            Assert.Equal(string.Empty, result.Scl);
        }

        // ---------------------------------------------------------------
        // The reference sheets
        // ---------------------------------------------------------------

        [Fact]
        public void CompilesAValveSheetIntoOneFunctionWithNamedArguments()
        {
            var result = Compile(DemoProjects.Valve());

            Assert.True(result.IsSuccess, Issues(result));

            var scl = Scl.Normalize(result.Scl);
            Assert.StartsWith("FUNCTION \"FC_CallUnits\" : Void\n", scl, StringComparison.Ordinal);
            Assert.EndsWith("END_FUNCTION\n", scl, StringComparison.Ordinal);
            Assert.Contains("Open := \"V01_Open_Cmd\"", scl, StringComparison.Ordinal);
            Assert.Contains("FbOpened := \"V01_FB_Opened\"", scl, StringComparison.Ordinal);
            Assert.Contains("Q => \"V01_Sol\"", scl, StringComparison.Ordinal);
            Assert.Contains("Fault => \"V01_Fault\"", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void AnUnwiredInputFallsBackToItsDeclaredDefault()
        {
            var result = Compile(DemoProjects.Valve());

            Assert.Contains("TravelTime := T#3s", result.Scl, StringComparison.Ordinal);
        }

        [Fact]
        public void AnUnwiredFbInputWithoutADefaultIsOmittedFromTheCall()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("FB_Optional", ModelBuilder.Input("Command"));
            project.Plant.Blocks.Add(block);
            var node = ModelBuilder.PlaceBlock(project.Sheets[0], block, "Optional01");
            node.Pins.Single().IsRequired = true;

            var result = Compile(project);

            Assert.True(result.IsSuccess, Issues(result));
            Assert.DoesNotContain("Command :=", result.Scl, StringComparison.Ordinal);
        }

        [Fact]
        public void AnUnwiredFcInputBlocksSclGeneration()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.Function("FC_Required", "Void", ModelBuilder.Input("Command"));
            project.Plant.Blocks.Add(block);
            ModelBuilder.PlaceBlock(project.Sheets[0], block, "Required01");

            var result = Compile(project);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Issues, issue => issue.Code == "DGM020");
            Assert.Equal(string.Empty, result.Scl);
        }

        [Fact]
        public void AGlobalDbUdtVariableCanFeedARequiredInOutPin()
        {
            var project = ModelBuilder.Project();
            var udt = ModelBuilder.Udt("Valve_Data", new TiaSclStudio.Core.Model.UdtMember("Open", "Bool"));
            var block = ModelBuilder.FunctionBlock(
                "FB_ValveUnit",
                ModelBuilder.InOut("HmiStr", "Valve_Data"));
            var variable = ModelBuilder.DbVariable("DB_Valve", "Data", "Valve_Data");
            project.Plant.DataTypes.Add(udt);
            project.Plant.Blocks.Add(block);
            project.Plant.DataBlockVariables.Add(variable);

            var terminal = ModelBuilder.PlaceDbVariable(
                project.Sheets[0],
                variable,
                TerminalDirection.Source);
            var call = ModelBuilder.PlaceBlock(project.Sheets[0], block, "Valve01");
            ModelBuilder.Connect(project.Sheets[0], terminal, "Data", call, "HmiStr");

            var result = Compile(project);

            Assert.True(result.IsSuccess, Issues(result));
            Assert.Contains(
                "HmiStr := \"DB_Valve\".\"Data\"",
                result.Scl,
                StringComparison.Ordinal);
            Assert.Contains(
                result.GeneratedSources,
                source => source.Kind == GeneratedSourceKind.GlobalDataBlocks &&
                    source.Content.Contains("Data : \"Valve_Data\";"));
        }

        [Fact]
        public void TheValveBundleCarriesTheBlockTheInstanceDbAndTheCallBlock()
        {
            var result = Compile(DemoProjects.Valve());

            Assert.Equal(
                new[] { "FB_Valve.scl", "90_Instances.scl", "99_FC_CallUnits.scl" },
                result.GeneratedSources.Select(source => source.FileName).ToArray());
        }

        /// <summary>
        /// An intermediate value with no tag on it has to land in VAR_TEMP, and
        /// the producer has to be called before the consumer reads it.
        /// </summary>
        [Fact]
        public void AValueChainedBetweenTwoBlocksBecomesATemporary()
        {
            var result = Compile(DemoProjects.Chain());

            Assert.True(result.IsSuccess, Issues(result));

            var scl = Scl.Normalize(result.Scl);
            var temporaries = Scl.Section(scl, "VAR_TEMP");
            Assert.Single(temporaries);
            Assert.EndsWith(": Int;", temporaries[0], StringComparison.Ordinal);

            var temporaryName = temporaries[0].Split(' ')[0];
            Assert.Contains("Value => #" + temporaryName, scl, StringComparison.Ordinal);
            Assert.Contains("Value := #" + temporaryName, scl, StringComparison.Ordinal);
            Assert.True(
                Scl.IndexOfLineContaining(scl, "\"P01\"(") < Scl.IndexOfLineContaining(scl, "\"C01\"("),
                "The producer is called after the consumer.");
        }

        [Fact]
        public void ALogicTreeIsInlinedIntoTheCallArgument()
        {
            var result = Compile(DemoProjects.LogicTree());

            Assert.True(result.IsSuccess, Issues(result));
            Assert.Contains(
                "Enable := (\"Cmd_Open\" AND (NOT \"Alarm\"))",
                result.Scl,
                StringComparison.Ordinal);
            Assert.Empty(Scl.Section(result.Scl, "VAR_TEMP"));
        }

        [Fact]
        public void AFunctionReturnIsAssignedToItsTag()
        {
            var result = Compile(DemoProjects.FunctionWithReturn());

            Assert.True(result.IsSuccess, Issues(result));
            Assert.Contains("\"Scaled_Value\" := \"FC_Scale\"(", result.Scl, StringComparison.Ordinal);
        }

        [Fact]
        public void AMultiInstanceSheetBecomesAFunctionBlockWithItsOwnDb()
        {
            var project = DemoProjects.Valve();
            DemoProjects.SingleBlockCall(project).UseMultiInstance = true;

            var result = Compile(project);

            Assert.True(result.IsSuccess, Issues(result));
            Assert.StartsWith("FUNCTION_BLOCK \"FC_CallUnits\"", result.Scl, StringComparison.Ordinal);
            Assert.Contains("V01 : \"FB_Valve\";", result.Scl, StringComparison.Ordinal);
            Assert.Contains("#V01(", result.Scl, StringComparison.Ordinal);
            Assert.Contains(
                "99z_FC_CallUnits_DB.scl",
                result.GeneratedSources.Select(source => source.FileName));
            Assert.DoesNotContain(
                "90_Instances.scl",
                result.GeneratedSources.Select(source => source.FileName));
        }

        [Fact]
        public void NotesBecomeCommentsAtTheTopOfTheBody()
        {
            var project = DemoProjects.Valve();
            ModelBuilder.PlaceNote(project.Sheets[0], "Участок подачи", 0.0, 10.0);
            ModelBuilder.PlaceNote(project.Sheets[0], "second note", 0.0, 20.0);

            var result = Compile(project);

            Assert.True(result.IsSuccess, Issues(result));
            var scl = Scl.Normalize(result.Scl);
            var begin = Scl.IndexOfLineContaining(scl, "BEGIN");
            Assert.Equal(begin + 1, Scl.IndexOfLineContaining(scl, "// Участок подачи"));
            Assert.True(
                Scl.IndexOfLineContaining(scl, "// second note") <
                Scl.IndexOfLineContaining(scl, "\"V01\"("));
        }

        [Fact]
        public void GeneratedSclUsesWindowsLineEndingsThroughout()
        {
            var scl = Compile(DemoProjects.LogicTree()).Scl;

            Assert.DoesNotContain("\n", scl.Replace("\r\n", string.Empty), StringComparison.Ordinal);
        }

        [Fact]
        public void CompilationIsDeterministic()
        {
            var project = DemoProjects.Chain();

            var first = Compile(project);
            var second = Compile(project);

            Assert.Equal(first.Scl, second.Scl);
            Assert.Equal(first.ExecutionOrder.ToArray(), second.ExecutionOrder.ToArray());
        }

        [Fact]
        public void CompilationDoesNotMutateTheModel()
        {
            var project = DemoProjects.Chain();
            var before = TiaSclStudio.Diagram.History.DiagramProjectSnapshot.Capture(project);

            Compile(project);

            Assert.False(
                TiaSclStudio.Diagram.History.DiagramProjectSnapshot.Capture(project)
                    .Restore()
                    .Sheets[0]
                    .Nodes.Count != before.Restore().Sheets[0].Nodes.Count,
                "Compilation changed the node count.");
            Assert.Equal(
                before.Restore().Plant.Blocks.Count,
                project.Plant.Blocks.Count);
        }

        // ---------------------------------------------------------------
        // Refusals
        // ---------------------------------------------------------------

        [Fact]
        public void RefusesToEmitAnythingWhenTheGraphHasACycle()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("In"), ModelBuilder.Output("Out"));
            project.Plant.Blocks.Add(block);
            var sheet = project.Sheets[0];
            var first = ModelBuilder.PlaceBlock(sheet, block, "A", 0.0, 0.0);
            var second = ModelBuilder.PlaceBlock(sheet, block, "B", 100.0, 0.0);
            ModelBuilder.Connect(sheet, first, "Out", second, "In");
            ModelBuilder.Connect(sheet, second, "Out", first, "In");

            var result = Compile(project);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Issues, issue => issue.Code == "DGM030");
            Assert.Equal(string.Empty, result.Scl);
            Assert.Empty(result.GeneratedSources);
        }

        [Fact]
        public void RefusesToEmitAnythingWhenAWireCrossesTypes()
        {
            var project = DemoProjects.Valve();
            Deformations.StalePinDataType(DemoProjects.SingleBlockCall(project), "Open", "Int");

            var result = Compile(project);

            Assert.False(result.IsSuccess);
            Assert.Equal(string.Empty, result.Scl);
        }

        [Fact]
        public void ReportsAPlacedBlockWhoseLibraryDefinitionIsGone()
        {
            var project = DemoProjects.Valve();
            DemoProjects.SingleBlockCall(project).BlockDefinitionId = Guid.NewGuid();

            var result = Compile(project);

            Assert.Contains(result.Issues, issue => issue.Code == "GEN001");
        }

        [Fact]
        public void ReportsAPinThatNoLongerMatchesItsInterfaceMember()
        {
            var project = DemoProjects.Valve();
            project.Plant.Blocks[0].Interface
                .Single(member => member.Name == "Open")
                .DataType = "Int";

            var result = Compile(project);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Issues, issue => issue.Code == "GEN004" || issue.Code == "DGM013");
        }

        [Fact]
        public void ReportsAnInstanceNameThatIsNotALegalSymbol()
        {
            var project = DemoProjects.Valve();
            DemoProjects.SingleBlockCall(project).InstanceName = "V 01";

            var result = Compile(project);

            Assert.Contains(result.Issues, issue => issue.Code == "GEN013");
        }

        [Fact]
        public void ReportsTwoBlockCallsSharingAnInstanceName()
        {
            var project = DemoProjects.Valve();
            var sheet = project.Sheets[0];
            var second = ModelBuilder.PlaceBlock(sheet, project.Plant.Blocks[0], "V01", 400.0, 400.0);

            var result = Compile(project);

            Assert.Contains(result.Issues, issue => issue.Code == "GEN011" && issue.NodeId == second.Id);
        }

        [Fact]
        public void ReportsASheetNameThatCollidesWithABlock()
        {
            var project = DemoProjects.Valve();
            project.Sheets[0].Name = "FB_Valve";

            var result = Compile(project);

            Assert.Contains(result.Issues, issue => issue.Code == "GEN012");
        }

        [Fact]
        public void ReportsAnInstanceNameThatCollidesWithATag()
        {
            var project = DemoProjects.Valve();
            DemoProjects.SingleBlockCall(project).InstanceName = "V01_Sol";

            var result = Compile(project);

            Assert.Contains(result.Issues, issue => issue.Code == "GEN014");
        }

        [Fact]
        public void ReportsALogicNodeWithAStalePinContract()
        {
            var project = DemoProjects.LogicTree();
            var logic = project.Sheets[0].Nodes.OfType<LogicNode>().First(node => node.Operation == LogicOperation.And);
            ModelBuilder.PinOf(logic, "In2").DataType = "Int";

            var result = Compile(project);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Issues, issue => issue.Code == "GEN024" || issue.Code == "DGM013");
        }

        [Fact]
        public void ReportsALogicOperationThatThisBuildDoesNotKnow()
        {
            var project = DemoProjects.LogicTree();
            Deformations.UndefinedLogicOperation(
                project.Sheets[0].Nodes.OfType<LogicNode>().First());

            var result = Compile(project);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Issues, issue => issue.Code == "GEN023");
        }

        [Fact]
        public void ReportsAConstantWithNoLiteral()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("In", "Int"));
            project.Plant.Blocks.Add(block);
            var sheet = project.Sheets[0];
            var call = ModelBuilder.PlaceBlock(sheet, block, "A01");
            var constant = ModelBuilder.PlaceConstant(sheet, "  ", "Int");
            ModelBuilder.Connect(sheet, ModelBuilder.OnlyPin(constant), ModelBuilder.PinOf(call, "In"));

            var result = Compile(project);

            Assert.Contains(result.Issues, issue => issue.Code == "GEN019");
        }

        [Fact]
        public void AConstantIsInlinedIntoTheCall()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("Delay", "Time"));
            project.Plant.Blocks.Add(block);
            var sheet = project.Sheets[0];
            var call = ModelBuilder.PlaceBlock(sheet, block, "A01");
            var constant = ModelBuilder.PlaceConstant(sheet, "T#5s", "Time");
            ModelBuilder.Connect(sheet, ModelBuilder.OnlyPin(constant), ModelBuilder.PinOf(call, "Delay"));

            var result = Compile(project);

            Assert.True(result.IsSuccess, Issues(result));
            Assert.Contains("Delay := T#5s", result.Scl, StringComparison.Ordinal);
        }

        [Fact]
        public void EveryDeformationOfAValidSheetIsReportedRatherThanThrown()
        {
            var deformations = new Action<CallSheet>[]
            {
                Deformations.NullNodeCollection,
                Deformations.NullWireCollection,
                Deformations.NullGroupCollection,
                Deformations.InjectNullNode,
                Deformations.InjectNullWire,
                Deformations.DuplicateNodeId,
                Deformations.DuplicatePinId,
                Deformations.DanglingWireSource,
                Deformations.DanglingWireTarget,
                sheet => sheet.Nodes[0].Pins = null,
                sheet => sheet.Nodes[0].Pins.Add(null),
                sheet => sheet.Id = Guid.Empty,
                sheet => sheet.Name = "not a name",
                sheet => sheet.Width = double.NaN
            };

            foreach (var deformation in deformations)
            {
                var project = DemoProjects.Valve();
                deformation(project.Sheets[0]);

                var result = new DiagramSclGenerator().Generate(project.Plant, project.Sheets[0]);

                Assert.False(result.IsSuccess, "A deformed sheet compiled successfully.");
                Assert.Equal(string.Empty, result.Scl);
                Assert.Empty(result.GeneratedSources);
            }
        }

        [Fact]
        public void ADeformedPlantIsReportedRatherThanThrown()
        {
            var deformations = new Action<DiagramProject>[]
            {
                project => project.Plant.Blocks = null,
                project => project.Plant.Tags = null,
                project => project.Plant.Blocks.Add(null),
                project => project.Plant.Tags.Add(null),
                project => project.Plant.Blocks[0].Interface = null,
                project => project.Plant.Blocks[0].Interface.Add(null)
            };

            foreach (var deformation in deformations)
            {
                var project = DemoProjects.Valve();
                deformation(project);

                var result = Record.Exception(
                    () => new DiagramSclGenerator().Generate(project.Plant, project.Sheets[0]));

                Assert.True(
                    result == null || result is NullReferenceException == false,
                    "A deformed plant produced " + result);
            }
        }
    }
}
