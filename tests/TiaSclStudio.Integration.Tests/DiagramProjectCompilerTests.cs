using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// The project compiler is what runs before an export. It has to see the
    /// whole program at once, because a symbol that is unique on one sheet can
    /// still collide with one on another sheet, and TIA would only find out
    /// half way through rewriting the customer's project.
    /// </summary>
    public sealed class DiagramProjectCompilerTests
    {
        private static DiagramProjectCompilationResult Compile(DiagramProject project)
        {
            return new DiagramProjectCompiler().Compile(project);
        }

        private static string Issues(DiagramProjectCompilationResult result)
        {
            return string.Join(
                "; ",
                result.Issues.Select(issue => issue.Code + " " + issue.Message));
        }

        private static bool HasCode(DiagramProjectCompilationResult result, string code)
        {
            return result.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal));
        }

        /// <summary>Merges a second sheet, with its own blocks and tags, into one project.</summary>
        private static DiagramProject TwoSheets()
        {
            var project = DemoProjects.Valve();
            var second = DemoProjects.Chain();

            project.Plant.Blocks.AddRange(second.Plant.Blocks);
            project.Plant.Tags.AddRange(second.Plant.Tags);
            project.Sheets.Add(second.Sheets[0]);
            return project;
        }

        [Fact]
        public void ReportsAMissingProjectWithoutThrowing()
        {
            var result = Compile(null);

            Assert.False(result.IsSuccess);
            Assert.True(HasCode(result, "DGP000"));
        }

        [Fact]
        public void ReportsAMissingSheetCollection()
        {
            var project = ModelBuilder.Project();
            project.Sheets = null;

            Assert.True(HasCode(Compile(project), "DGP001"));
        }

        [Fact]
        public void ReportsAProjectWithNoSheets()
        {
            var project = ModelBuilder.Project();
            project.Sheets.Clear();

            Assert.True(HasCode(Compile(project), "DGP011"));
        }

        [Fact]
        public void ReportsAMissingPlant()
        {
            var project = ModelBuilder.Project();
            project.Plant = null;

            Assert.True(HasCode(Compile(project), "DGP004"));
        }

        [Fact]
        public void ReportsANullSheetInTheCollection()
        {
            var project = DemoProjects.Valve();
            project.Sheets.Add(null);

            var result = Compile(project);

            Assert.False(result.IsSuccess);
            Assert.Empty(result.GeneratedSources);
        }

        [Fact]
        public void CompilesASingleSheetProject()
        {
            var result = Compile(DemoProjects.Valve());

            Assert.True(result.IsSuccess, Issues(result));
            Assert.Single(result.Sheets);
            Assert.Equal("FC_CallUnits", result.Sheets[0].SheetName);
            Assert.NotEmpty(result.GeneratedSources);
        }

        [Fact]
        public void CompilesTwoSheetsIntoOneBundle()
        {
            var result = Compile(TwoSheets());

            Assert.True(result.IsSuccess, Issues(result));
            Assert.Equal(2, result.Sheets.Count);
            Assert.Contains(result.GeneratedSources, source => source.FileName == "99_FC_CallUnits.scl");
            Assert.Contains(result.GeneratedSources, source => source.FileName == "99_FC_Chain.scl");
        }

        /// <summary>
        /// TIA resolves symbols as it imports, so a data type has to arrive
        /// before the block that uses it, and a block before its instance DB.
        /// </summary>
        [Fact]
        public void TheBundleIsOrderedByImportDependency()
        {
            var project = TwoSheets();
            project.Plant.DataTypes.Add(ModelBuilder.Udt("UDT_A", new UdtMember("X", "Bool")));

            var result = Compile(project);

            Assert.True(result.IsSuccess, Issues(result));

            var kinds = result.GeneratedSources.Select(source => Rank(source.Kind)).ToList();
            for (var index = 1; index < kinds.Count; index++)
            {
                Assert.True(
                    kinds[index - 1] <= kinds[index],
                    "The bundle is not in dependency order: " + string.Join(
                        ", ",
                        result.GeneratedSources.Select(source => source.FileName + "(" + source.Kind + ")")));
            }
        }

        [Fact]
        public void EverySourceInTheBundleHasAUniqueNameAndASequentialOrder()
        {
            var result = Compile(TwoSheets());

            var names = result.GeneratedSources.Select(source => source.FileName).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(
                Enumerable.Range(0, result.GeneratedSources.Count).ToArray(),
                result.GeneratedSources.Select(source => source.Order).ToArray());
        }

        /// <summary>
        /// Both sheets produce an instance-DB source. Without a per-sheet name
        /// they would land on the same file and one sheet's instances would be
        /// dropped from the import without any warning.
        /// </summary>
        [Fact]
        public void EachSheetGetsItsOwnInstanceSourceFile()
        {
            var result = Compile(TwoSheets());

            var instanceSources = result.GeneratedSources
                .Where(source => source.Kind == Core.Model.GeneratedSourceKind.InstanceDataBlocks)
                .ToList();

            Assert.Equal(2, instanceSources.Count);
            Assert.Equal(
                instanceSources.Count,
                instanceSources.Select(source => source.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void ReportsTwoSheetsWithTheSameName()
        {
            var project = TwoSheets();
            project.Sheets[1].Name = project.Sheets[0].Name;

            var result = Compile(project);

            Assert.True(HasCode(result, "DGP007"), Issues(result));
            Assert.Empty(result.GeneratedSources);
        }

        [Fact]
        public void ReportsAStableIdSharedByTwoSheets()
        {
            var project = TwoSheets();
            project.Sheets[1].Id = project.Sheets[0].Id;

            Assert.True(HasCode(Compile(project), "DGP006"));
        }

        [Fact]
        public void ReportsANodeIdReusedOnAnotherSheet()
        {
            var project = TwoSheets();
            project.Sheets[1].Nodes[0].Id = project.Sheets[0].Nodes[0].Id;

            var result = Compile(project);

            Assert.True(HasCode(result, "DGP006"), Issues(result));
            // Both sheets have to be flagged: the user cannot tell which one is
            // the intruder from one message alone.
            Assert.True(result.Sheets.All(sheet => !sheet.IsSuccess));
        }

        [Fact]
        public void ReportsAnIdSharedBetweenThePlantAndASheet()
        {
            var project = DemoProjects.Valve();
            project.Sheets[0].Nodes[0].Id = project.Plant.Blocks[0].Id;

            Assert.True(HasCode(Compile(project), "DGP006"));
        }

        /// <summary>
        /// Two sheets can each be internally consistent and still both name a
        /// global instance DB "V01". Only the project view can see that.
        /// </summary>
        [Fact]
        public void ReportsAnInstanceDbNameUsedOnTwoDifferentSheets()
        {
            var project = TwoSheets();
            project.Sheets[1].Nodes.OfType<BlockCallNode>().First().InstanceName = "V01";

            var result = Compile(project);

            Assert.True(HasCode(result, "DGP008"), Issues(result));
            Assert.Empty(result.GeneratedSources);
        }

        [Fact]
        public void ReportsASheetNameThatCollidesWithAPlantSymbol()
        {
            var project = DemoProjects.Valve();
            var second = ModelBuilder.Sheet("FB_Valve");
            project.Sheets.Add(second);

            var result = Compile(project);

            Assert.False(result.IsSuccess);
            Assert.Empty(result.GeneratedSources);
        }

        [Fact]
        public void ReportsADeformedPlantOnceRatherThanPerSheet()
        {
            var project = TwoSheets();
            project.Plant.Blocks.Add(null);

            var result = Compile(project);

            Assert.True(HasCode(result, "DGP005"));
            Assert.Equal(1, result.Issues.Count(issue => issue.Code == "DGP005"));
        }

        [Fact]
        public void PlantValidationIssuesAppearExactlyOnce()
        {
            var project = TwoSheets();
            project.Plant.Tags[0].Address = string.Empty;

            var result = Compile(project);

            Assert.Equal(
                1,
                result.Issues.Count(issue => issue.Code == "CORE_TAG_ADDRESS_REQUIRED"));
        }

        [Fact]
        public void NoSourcesAreEmittedWhileAnyErrorRemains()
        {
            var project = TwoSheets();
            Deformations.StalePinDataType(
                project.Sheets[0].Nodes.OfType<BlockCallNode>().First(),
                "Open",
                "Int");

            var result = Compile(project);

            Assert.False(result.IsSuccess);
            Assert.Empty(result.GeneratedSources);
            Assert.Empty(result.Sources);
        }

        [Fact]
        public void AWarningAloneStillProducesTheBundle()
        {
            var project = DemoProjects.Valve();
            project.Plant.DataTypes.Add(ModelBuilder.Udt("UDT_Empty"));

            var result = Compile(project);

            Assert.Contains(result.Issues, issue => issue.Severity == DiagramIssueSeverity.Warning);
            Assert.True(result.IsSuccess, Issues(result));
            Assert.NotEmpty(result.GeneratedSources);
        }

        [Fact]
        public void EveryIssueCanBePointedAtSomethingInTheUi()
        {
            var project = TwoSheets();
            project.Sheets[1].Nodes.OfType<BlockCallNode>().First().InstanceName = "V01";

            var result = Compile(project);

            Assert.All(result.Issues, issue =>
            {
                Assert.False(string.IsNullOrWhiteSpace(issue.Code));
                Assert.False(string.IsNullOrWhiteSpace(issue.Message));
                Assert.NotNull(issue.SheetName);
            });
        }

        [Fact]
        public void ResultCollectionsAreReadOnly()
        {
            var result = Compile(DemoProjects.Valve());

            Assert.Throws<NotSupportedException>(() => result.Issues.Add(null));
            Assert.Throws<NotSupportedException>(() => result.Sheets.Add(null));
            Assert.Throws<NotSupportedException>(() => result.GeneratedSources.Add(null));
        }

        [Fact]
        public void CompilationIsDeterministicAcrossRuns()
        {
            var project = TwoSheets();

            var first = Compile(project);
            var second = Compile(project);

            Assert.Equal(
                first.GeneratedSources.Select(source => source.FileName + " " + source.Content).ToArray(),
                second.GeneratedSources.Select(source => source.FileName + " " + source.Content).ToArray());
            Assert.Equal(
                first.Issues.Select(issue => issue.Code).ToArray(),
                second.Issues.Select(issue => issue.Code).ToArray());
        }

        [Fact]
        public void CompilationDoesNotMutateTheProject()
        {
            var project = TwoSheets();
            var before = TiaSclStudio.Diagram.History.DiagramProjectSnapshot.Capture(project);

            Compile(project);

            var after = TiaSclStudio.Diagram.History.DiagramProjectSnapshot.Capture(project);
            Assert.Equal(
                before.Restore().Sheets.Count,
                after.Restore().Sheets.Count);
            Assert.Equal(
                before.Restore().Plant.Blocks.Select(block => block.Id).ToArray(),
                after.Restore().Plant.Blocks.Select(block => block.Id).ToArray());
        }

        [Fact]
        public void EveryMalformedPlantCollectionFailsClosedWithoutThrowing()
        {
            var malformed = new List<DiagramProject>();

            var variables = DemoProjects.Valve();
            variables.Plant.DataBlockVariables = null;
            malformed.Add(variables);

            var nullUnit = DemoProjects.Valve();
            var callBlock = new CallBlockDefinition { Name = "FC_Extra" };
            callBlock.Units.Add(null);
            nullUnit.Plant.CallBlocks.Add(callBlock);
            malformed.Add(nullUnit);

            var nullBindings = DemoProjects.Valve();
            callBlock = new CallBlockDefinition { Name = "FC_Extra" };
            callBlock.Units.Add(new UnitDefinition
            {
                Name = "Unit",
                BlockId = nullBindings.Plant.Blocks[0].Id,
                BlockName = nullBindings.Plant.Blocks[0].Name,
                Bindings = null
            });
            nullBindings.Plant.CallBlocks.Add(callBlock);
            malformed.Add(nullBindings);

            var nullBinding = DemoProjects.Valve();
            callBlock = new CallBlockDefinition { Name = "FC_Extra" };
            var unit = new UnitDefinition("Unit", nullBinding.Plant.Blocks[0]);
            unit.Bindings.Add(null);
            callBlock.Units.Add(unit);
            nullBinding.Plant.CallBlocks.Add(callBlock);
            malformed.Add(nullBinding);

            foreach (var project in malformed)
            {
                var result = Compile(project);
                Assert.True(HasCode(result, "DGP005"), Issues(result));
                Assert.Empty(result.GeneratedSources);
            }
        }

        [Fact]
        public void EveryMalformedSheetShapeFailsClosedWithoutThrowing()
        {
            var malformed = new List<DiagramProject>();

            var nodes = DemoProjects.Valve();
            nodes.Sheets[0].Nodes = null;
            malformed.Add(nodes);

            var wires = DemoProjects.Valve();
            wires.Sheets[0].Wires = null;
            malformed.Add(wires);

            var nullNode = DemoProjects.Valve();
            nullNode.Sheets[0].Nodes.Add(null);
            malformed.Add(nullNode);

            var nullPins = DemoProjects.Valve();
            nullPins.Sheets[0].Nodes[0].Pins = null;
            malformed.Add(nullPins);

            var nullPin = DemoProjects.Valve();
            nullPin.Sheets[0].Nodes[0].Pins.Add(null);
            malformed.Add(nullPin);

            var nullWire = DemoProjects.Valve();
            nullWire.Sheets[0].Wires.Add(null);
            malformed.Add(nullWire);

            foreach (var project in malformed)
            {
                var result = Compile(project);
                Assert.Contains(result.Issues, issue => issue.Code == "DGP003");
                Assert.Empty(result.GeneratedSources);
            }
        }

        [Fact]
        public void StableIdsAreCheckedWithinOneSheetAndEmptyIdsRemainCoreDiagnostics()
        {
            var duplicate = DemoProjects.Valve();
            duplicate.Sheets[0].Nodes[0].Pins[0].Id = duplicate.Sheets[0].Nodes[0].Id;

            var empty = DemoProjects.Valve();
            empty.Sheets[0].Nodes[0].Id = Guid.Empty;

            Assert.True(HasCode(Compile(duplicate), "DGP006"));
            Assert.Contains(Compile(empty).Issues, issue => issue.Code == "DGM008");
        }

        [Fact]
        public void PlantIdentityAndSymbolRegistryIncludeDbVariablesCallBlocksAndUnits()
        {
            var project = DemoProjects.Valve();
            var udt = ModelBuilder.Udt("State_Data", new UdtMember("Value", "Bool"));
            project.Plant.DataTypes.Add(udt);
            project.Plant.DataBlockVariables.Add(
                ModelBuilder.DbVariable("State_DB", "State", udt.Name));

            var helper = ModelBuilder.FunctionBlock("FB_Helper");
            project.Plant.Blocks.Add(helper);
            var callBlock = new CallBlockDefinition
            {
                Name = "FB_Coordinator",
                Kind = BlockKind.FunctionBlock,
                ReturnType = "Void",
                InstanceDbName = string.Empty
            };
            callBlock.Interface.Add(new InterfaceMember(
                "Enabled",
                "Bool",
                InterfaceSection.Input));
            var unit = new UnitDefinition("Helper01", helper);
            unit.BlockId = Guid.NewGuid();
            unit.Bindings.Add(new ParameterBinding
            {
                ParameterName = "Unused",
                Expression = "TRUE"
            });
            callBlock.Units.Add(unit);
            project.Plant.CallBlocks.Add(callBlock);

            var result = Compile(project);

            Assert.NotNull(result);
            Assert.DoesNotContain(result.Issues, issue => issue.Code == "DGP005");
        }

        [Fact]
        public void EmptyPlantAndSheetSymbolsAreIgnoredByTheCrossProjectRegistries()
        {
            var project = DemoProjects.Valve();
            project.Plant.Tags.Add(new TagDefinition
            {
                Id = Guid.Empty,
                Name = string.Empty,
                DataType = "Bool",
                Address = "%M1.0"
            });
            project.Sheets.Add(new CallSheet { Name = string.Empty });

            var result = Compile(project);

            Assert.False(result.IsSuccess);
            Assert.Empty(result.GeneratedSources);
        }

        [Theory]
        [InlineData(true, "DGP009")]
        [InlineData(false, "DGP010")]
        public void SourceBundlerRejectsDuplicateAndConflictingFileNames(
            bool sameContent,
            string expectedCode)
        {
            var plant = ModelBuilder.Plant();
            var callBlock = new CallBlockDefinition
            {
                Name = "FC_Same",
                Kind = BlockKind.Function,
                ReturnType = "Void"
            };
            plant.CallBlocks.Add(callBlock);
            var plantSource = new SclGenerator().GenerateProject(plant)
                .Single(source => source.FileName == "99_FC_Same.scl");
            var sheet = new CallSheet { Name = "FC_Same" };
            var generated = new GeneratedSource(
                "99_FC_Same.scl",
                sameContent ? plantSource.Content : plantSource.Content + "// different\r\n",
                GeneratedSourceKind.CallBlock,
                0);
            var compilation = new DiagramCompilationResult(
                generated.Content,
                new DiagramIssue[0],
                new Guid[0],
                new[] { generated });
            var sheetResult = (DiagramSheetCompilationResult)Activator.CreateInstance(
                typeof(DiagramSheetCompilationResult),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object[]
                {
                    0,
                    (Guid?)sheet.Id,
                    sheet.Name,
                    compilation,
                    new DiagramProjectIssue[0]
                },
                null);
            var issues = new List<DiagramProjectIssue>();
            var buildBundle = typeof(DiagramProjectCompiler).GetMethod(
                "BuildSourceBundle",
                BindingFlags.NonPublic | BindingFlags.Static);

            var bundle = (IList<GeneratedSource>)buildBundle.Invoke(
                null,
                new object[]
                {
                    plant,
                    new List<CallSheet> { sheet },
                    new List<DiagramSheetCompilationResult> { sheetResult },
                    issues
                });

            Assert.Empty(bundle);
            Assert.Contains(issues, issue => issue.Code == expectedCode);
        }

        [Fact]
        public void CompilerFileNameHelpersCoverWindowsAndLengthBoundaries()
        {
            var compiler = typeof(DiagramProjectCompiler);
            var makeName = compiler.GetMethod(
                "MakeSafeFileName",
                BindingFlags.NonPublic | BindingFlags.Static);
            var makeToken = compiler.GetMethod(
                "MakeSafeFileToken",
                BindingFlags.NonPublic | BindingFlags.Static);
            var reserved = compiler.GetMethod(
                "IsReservedWindowsFileName",
                BindingFlags.NonPublic | BindingFlags.Static);
            var rank = compiler.GetMethod(
                "DependencyRank",
                BindingFlags.NonPublic | BindingFlags.Static);
            var visualName = compiler.GetMethod(
                "MakeVisualInstanceFileName",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.Equal("Fallback.scl", makeName.Invoke(null, new object[] { null, "Fallback" }));
            Assert.Equal("Fallback.scl", makeName.Invoke(null, new object[] { "C:\\folder\\", "Fallback" }));
            Assert.Equal("_CON", makeName.Invoke(null, new object[] { "CON", "Fallback" }));
            Assert.Equal("Fallback.scl", makeName.Invoke(null, new object[] { "...", "Fallback" }));
            var longName = (string)makeName.Invoke(
                null,
                new object[] { new string('A', 220) + ".scl", "Fallback" });
            Assert.True(longName.Length <= 180);

            Assert.Equal("Sheet", makeToken.Invoke(null, new object[] { null, "Sheet" }));
            Assert.Equal("A_B", makeToken.Invoke(null, new object[] { "A-B", "Sheet" }));
            Assert.StartsWith("90_Instances_001_Sheet", (string)visualName.Invoke(
                null,
                new object[] { 0, null }));

            Assert.False((bool)reserved.Invoke(null, new object[] { null }));
            Assert.True((bool)reserved.Invoke(null, new object[] { "AUX" }));
            Assert.True((bool)reserved.Invoke(null, new object[] { "COM1" }));
            Assert.False((bool)reserved.Invoke(null, new object[] { "COM0" }));
            Assert.Equal(int.MaxValue, rank.Invoke(
                null,
                new object[] { (GeneratedSourceKind)999 }));
            Assert.Equal(2, rank.Invoke(
                null,
                new object[] { GeneratedSourceKind.GlobalDataBlocks }));
            Assert.Equal(5, rank.Invoke(
                null,
                new object[] { GeneratedSourceKind.CallBlockInstanceDataBlock }));
        }

        private static int Rank(Core.Model.GeneratedSourceKind kind)
        {
            switch (kind)
            {
                case Core.Model.GeneratedSourceKind.DataTypes:
                    return 0;
                case Core.Model.GeneratedSourceKind.Block:
                    return 1;
                case Core.Model.GeneratedSourceKind.InstanceDataBlocks:
                    return 2;
                case Core.Model.GeneratedSourceKind.CallBlock:
                    return 3;
                default:
                    return 4;
            }
        }
    }
}
