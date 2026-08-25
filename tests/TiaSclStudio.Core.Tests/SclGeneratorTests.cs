using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiaSclStudio.Core.Generation;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Core.Tests
{
    /// <summary>
    /// The generator produces text that TIA Portal compiles without a second
    /// opinion. A malformed line here surfaces as a compiler error on the
    /// customer's machine at best, and as silently wrong logic at worst.
    /// </summary>
    public sealed class SclGeneratorTests
    {
        private static SclGenerator Generator()
        {
            return new SclGenerator();
        }

        // ---------------------------------------------------------------
        // Argument guards
        // ---------------------------------------------------------------

        [Fact]
        public void RejectsANullValidator()
        {
            Assert.Throws<ArgumentNullException>(() => new SclGenerator(null));
        }

        [Fact]
        public void RejectsNullArguments()
        {
            var generator = Generator();

            Assert.Throws<ArgumentNullException>(() => generator.GenerateBlock(null));
            Assert.Throws<ArgumentNullException>(() => generator.GenerateUdt(null));
            Assert.Throws<ArgumentNullException>(() => generator.GenerateUdts(null));
            Assert.Throws<ArgumentNullException>(() => generator.GenerateInstanceDbs(null));
            Assert.Throws<ArgumentNullException>(() => generator.GenerateGlobalDataBlocks(null));
            Assert.Throws<ArgumentNullException>(() => generator.GenerateProject(null));
            Assert.Throws<ArgumentNullException>(() => generator.GenerateCallBlock(null, new BlockDefinition[0]));
            Assert.Throws<ArgumentNullException>(() => generator.GenerateCallBlockInstanceDb(null));
            Assert.Throws<ArgumentNullException>(
                () => generator.GenerateCallBlock(new CallBlockDefinition(), (IEnumerable<BlockDefinition>)null));
        }

        [Fact]
        public void GeneratesOwnedGlobalDbVariablesAfterTheirUdtSource()
        {
            var project = ModelBuilder.Plant();
            project.DataTypes.Add(ModelBuilder.Udt("Valve_Data", new UdtMember("Open", "Bool")));
            project.DataBlockVariables.Add(
                ModelBuilder.DbVariable("DB_Valve", "Data", "Valve_Data"));

            var sources = Generator().GenerateProject(project);
            var dbSource = sources.Single(source => source.Kind == GeneratedSourceKind.GlobalDataBlocks);

            Assert.Equal("10_GlobalDataBlocks.scl", dbSource.FileName);
            Assert.True(
                sources.Single(source => source.Kind == GeneratedSourceKind.DataTypes).Order < dbSource.Order);
            Assert.Contains("DATA_BLOCK \"DB_Valve\"", dbSource.Content, StringComparison.Ordinal);
            Assert.Contains("Data : \"Valve_Data\";", dbSource.Content, StringComparison.Ordinal);
        }

        [Fact]
        public void DoesNotGenerateAReferenceOnlyTiaDataBlock()
        {
            var project = ModelBuilder.Plant();
            project.DataTypes.Add(ModelBuilder.Udt("Valve_Data"));
            project.DataBlockVariables.Add(
                ModelBuilder.DbVariable("Existing_DB", "Data", "Valve_Data", false));

            Assert.DoesNotContain(
                Generator().GenerateProject(project),
                source => source.Kind == GeneratedSourceKind.GlobalDataBlocks);
        }

        // ---------------------------------------------------------------
        // Block shape
        // ---------------------------------------------------------------

        [Fact]
        public void EmitsAFunctionBlockHeaderAndFooter()
        {
            var block = ModelBuilder.FunctionBlock("FB_Valve", ModelBuilder.Input("Open"));

            var scl = Scl.Normalize(Generator().GenerateBlock(block));

            Assert.StartsWith("FUNCTION_BLOCK \"FB_Valve\"\n", scl, StringComparison.Ordinal);
            Assert.Contains("{ S7_Optimized_Access := 'TRUE' }\n", scl, StringComparison.Ordinal);
            Assert.Contains("VERSION : 0.1\n", scl, StringComparison.Ordinal);
            Assert.EndsWith("END_FUNCTION_BLOCK\n", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void EmitsAFunctionHeaderWithItsReturnType()
        {
            var block = ModelBuilder.Function("FC_Scale", "Real", ModelBuilder.Input("Raw", "Int"));

            var scl = Scl.Normalize(Generator().GenerateBlock(block));

            Assert.StartsWith("FUNCTION \"FC_Scale\" : Real\n", scl, StringComparison.Ordinal);
            Assert.EndsWith("END_FUNCTION\n", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void NormalizesAMissingReturnTypeToVoid()
        {
            var block = ModelBuilder.Function("FC_A", "   ");

            Assert.Contains("\"FC_A\" : Void", Generator().GenerateBlock(block), StringComparison.Ordinal);
        }

        [Fact]
        public void NormalizesAMissingVersionToTheDefault()
        {
            var block = ModelBuilder.FunctionBlock("FB_A");
            block.Version = null;

            Assert.Contains("VERSION : 0.1", Generator().GenerateBlock(block), StringComparison.Ordinal);
        }

        [Fact]
        public void EmitsOptimizedAccessFalseWhenItIsTurnedOff()
        {
            var block = ModelBuilder.FunctionBlock("FB_A");
            block.OptimizedAccess = false;

            Assert.Contains(
                "{ S7_Optimized_Access := 'FALSE' }",
                Generator().GenerateBlock(block),
                StringComparison.Ordinal);
        }

        [Fact]
        public void EmitsInterfaceSectionsInTheOrderTiaExpects()
        {
            var block = ModelBuilder.FunctionBlock(
                "FB_A",
                ModelBuilder.Temp("T"),
                new InterfaceMember("C", "Int", InterfaceSection.Constant),
                ModelBuilder.Static("S"),
                ModelBuilder.InOut("IO"),
                ModelBuilder.Output("O"),
                ModelBuilder.Input("I"));

            var lines = Scl.Lines(Generator().GenerateBlock(block));
            var order = new[] { "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR", "VAR_TEMP", "VAR CONSTANT" }
                .Select(header => lines
                    .Select((line, index) => new { Line = line.Trim(), Index = index })
                    .Where(item => string.Equals(item.Line, header, StringComparison.Ordinal))
                    .Select(item => item.Index)
                    .DefaultIfEmpty(-1)
                    .First())
                .ToList();

            Assert.DoesNotContain(-1, order);
            for (var index = 1; index < order.Count; index++)
            {
                Assert.True(
                    order[index - 1] < order[index],
                    "Interface sections are emitted out of order: " + string.Join(",", order));
            }
        }

        [Fact]
        public void OmitsEmptySections()
        {
            var scl = Generator().GenerateBlock(ModelBuilder.FunctionBlock("FB_A"));

            Assert.DoesNotContain("VAR_INPUT", scl, StringComparison.Ordinal);
            Assert.DoesNotContain("END_VAR", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void EmitsInitialValuesAndCommentsOnDeclarations()
        {
            var member = ModelBuilder.Input("TravelTime", "Time", "T#3s");
            member.Comment = "opening time";
            var block = ModelBuilder.FunctionBlock("FB_A", member);

            var declaration = Scl.Section(Generator().GenerateBlock(block), "VAR_INPUT").Single();

            Assert.Equal("TravelTime : Time := T#3s; // opening time", declaration);
        }

        [Fact]
        public void FoldsAMultiLineCommentOntoOneDeclarationLine()
        {
            // A raw newline here would terminate the declaration early and make
            // the rest of the interface unparseable.
            var member = ModelBuilder.Input("Open");
            member.Comment = "first\r\nsecond\rthird\nfourth";
            var block = ModelBuilder.FunctionBlock("FB_A", member);

            var declaration = Scl.Section(Generator().GenerateBlock(block), "VAR_INPUT").Single();

            Assert.StartsWith("Open : Bool; // ", declaration, StringComparison.Ordinal);
            foreach (var word in new[] { "first", "second", "third", "fourth" })
            {
                Assert.Contains(word, declaration, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void FoldsATabInACommentIntoTheSameDeclarationLine()
        {
            var member = ModelBuilder.Input("Open");
            member.Comment = "before\tafter";
            var block = ModelBuilder.FunctionBlock("FB_A", member);

            var section = Scl.Section(Generator().GenerateBlock(block), "VAR_INPUT");

            Assert.Single(section);
            Assert.DoesNotContain("\t", section[0], StringComparison.Ordinal);
        }

        [Fact]
        public void EmitsTheBlockDescriptionAsALeadingCommentBlock()
        {
            var block = ModelBuilder.FunctionBlock("FB_A");
            block.Description = "line one\r\nline two";

            var lines = Scl.Lines(Generator().GenerateBlock(block));

            Assert.Equal("// line one", lines[0]);
            Assert.Equal("// line two", lines[1]);
        }

        [Fact]
        public void IndentsTheBodyAndNormalizesItsLineEndings()
        {
            var block = ModelBuilder.FunctionBlock("FB_A");
            block.SclBody = "\n#Q := #Open;\r\nIF #Open THEN\r  ;\nEND_IF;\n\n";

            var scl = Scl.Normalize(Generator().GenerateBlock(block));
            var body = scl.Substring(scl.IndexOf("BEGIN\n", StringComparison.Ordinal) + "BEGIN\n".Length);

            Assert.Equal(
                "   #Q := #Open;\n   IF #Open THEN\n     ;\n   END_IF;\nEND_FUNCTION_BLOCK\n",
                body);
        }

        [Fact]
        public void EmitsAnEmptyBodyWithoutABlankLine()
        {
            var block = ModelBuilder.FunctionBlock("FB_A");
            block.SclBody = "   \r\n  ";

            var scl = Scl.Normalize(Generator().GenerateBlock(block));

            Assert.Contains("BEGIN\nEND_FUNCTION_BLOCK\n", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void DoublesQuotesInsideAGlobalSymbol()
        {
            // Validation rejects such names, but the low-level entry point is
            // public, and a broken quote would swallow the rest of the source.
            var block = ModelBuilder.FunctionBlock("FB\"Valve");

            Assert.Contains(
                "FUNCTION_BLOCK \"FB\"\"Valve\"",
                Generator().GenerateBlock(block),
                StringComparison.Ordinal);
        }

        [Fact]
        public void EveryGeneratedLineUsesWindowsLineEndings()
        {
            // TIA Portal's external-source importer is line-ending sensitive.
            var block = ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("Open"));
            block.Description = "note";
            block.SclBody = "#Open := TRUE;";

            var scl = Generator().GenerateBlock(block);

            Assert.DoesNotContain("\n", scl.Replace("\r\n", string.Empty), StringComparison.Ordinal);
            Assert.DoesNotContain("\r", scl.Replace("\r\n", string.Empty), StringComparison.Ordinal);
        }

        // ---------------------------------------------------------------
        // UDTs
        // ---------------------------------------------------------------

        [Fact]
        public void EmitsAUdtStruct()
        {
            var udt = ModelBuilder.Udt(
                "UDT_Valve",
                new UdtMember("Open", "Bool"),
                new UdtMember("Time", "Time"));

            var scl = Scl.Normalize(Generator().GenerateUdt(udt));

            Assert.Contains("TYPE \"UDT_Valve\"\n", scl, StringComparison.Ordinal);
            Assert.Contains("   STRUCT\n", scl, StringComparison.Ordinal);
            Assert.Contains("      Open : Bool;\n", scl, StringComparison.Ordinal);
            Assert.Contains("   END_STRUCT;\n", scl, StringComparison.Ordinal);
            Assert.EndsWith("END_TYPE\n", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void SkipsNullUdtMembers()
        {
            var udt = ModelBuilder.Udt("UDT_A", new UdtMember("Open", "Bool"));
            udt.Members.Add(null);

            var members = Scl.Lines(Generator().GenerateUdt(udt))
                .Select(line => line.Trim())
                .Where(line => line.EndsWith(";", StringComparison.Ordinal) &&
                    line.Contains(" : ") &&
                    !line.StartsWith("END_STRUCT", StringComparison.Ordinal))
                .ToList();

            Assert.Equal(new[] { "Open : Bool;" }, members.ToArray());
        }

        [Fact]
        public void JoinsSeveralUdtsAndDropsEmptyOnes()
        {
            var scl = Generator().GenerateUdts(new[]
            {
                ModelBuilder.Udt("UDT_A", new UdtMember("X", "Bool")),
                ModelBuilder.Udt("UDT_B", new UdtMember("Y", "Bool"))
            });

            Assert.Equal(2, Scl.CountOccurrences(scl, "END_TYPE"));
        }

        // ---------------------------------------------------------------
        // Instance data blocks
        // ---------------------------------------------------------------

        [Fact]
        public void EmitsAGlobalInstanceDataBlock()
        {
            var block = ModelBuilder.FunctionBlock("FB_Valve");
            var unit = new UnitDefinition("V01", block) { Comment = "first valve" };

            var scl = Scl.Normalize(Generator().GenerateInstanceDb(unit, block));

            Assert.Contains("// first valve\n", scl, StringComparison.Ordinal);
            Assert.Contains("DATA_BLOCK \"V01\"\n", scl, StringComparison.Ordinal);
            Assert.Contains("NON_RETAIN\n\"FB_Valve\"\n", scl, StringComparison.Ordinal);
            Assert.EndsWith("END_DATA_BLOCK\n", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void RefusesAnInstanceDataBlockForAFunction()
        {
            var block = ModelBuilder.Function("FC_Scale", "Void");
            var unit = new UnitDefinition("S01", block);

            Assert.Throws<InvalidOperationException>(() => Generator().GenerateInstanceDb(unit, block));
        }

        [Fact]
        public void RefusesAGlobalDataBlockForAMultiInstance()
        {
            var block = ModelBuilder.FunctionBlock("FB_Valve");
            var unit = new UnitDefinition("V01", block) { UseMultiInstance = true };

            Assert.Throws<InvalidOperationException>(() => Generator().GenerateInstanceDb(unit, block));
        }

        [Fact]
        public void GeneratesOneInstanceDataBlockPerGlobalFunctionBlockUnit()
        {
            var project = BuildCallProject();
            var block = project.Blocks[0];
            var callBlock = project.CallBlocks[0];
            callBlock.Units.Add(new UnitDefinition("V02", block));
            callBlock.Units.Add(new UnitDefinition("V03", block) { UseMultiInstance = true });
            callBlock.Kind = BlockKind.FunctionBlock;

            var scl = Generator().GenerateInstanceDbs(project);

            Assert.Equal(2, Scl.CountOccurrences(scl, "END_DATA_BLOCK"));
            Assert.Contains("DATA_BLOCK \"V01\"", scl, StringComparison.Ordinal);
            Assert.Contains("DATA_BLOCK \"V02\"", scl, StringComparison.Ordinal);
            Assert.DoesNotContain("DATA_BLOCK \"V03\"", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void DefaultsTheCallBlockDbNameFromTheCallBlockName()
        {
            var callBlock = new CallBlockDefinition { Name = "FB_Calls", Kind = BlockKind.FunctionBlock };

            Assert.Contains(
                "DATA_BLOCK \"FB_Calls_DB\"",
                Generator().GenerateCallBlockInstanceDb(callBlock),
                StringComparison.Ordinal);
        }

        [Fact]
        public void HonoursAnExplicitCallBlockDbName()
        {
            var callBlock = new CallBlockDefinition
            {
                Name = "FB_Calls",
                Kind = BlockKind.FunctionBlock,
                InstanceDbName = "  Custom_DB  "
            };

            Assert.Contains(
                "DATA_BLOCK \"Custom_DB\"",
                Generator().GenerateCallBlockInstanceDb(callBlock),
                StringComparison.Ordinal);
        }

        [Fact]
        public void RefusesACallBlockDataBlockForAFunction()
        {
            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };

            Assert.Throws<InvalidOperationException>(() => Generator().GenerateCallBlockInstanceDb(callBlock));
        }

        // ---------------------------------------------------------------
        // Call blocks
        // ---------------------------------------------------------------

        [Fact]
        public void EmitsInputsWithAssignAndOutputsWithArrow()
        {
            var project = BuildCallProject();
            var block = project.Blocks[0];
            var unit = project.CallBlocks[0].Units[0];
            unit.Bindings.Add(new ParameterBinding(Member(block, "Open"), "\"Cmd\""));
            unit.Bindings.Add(new ParameterBinding(Member(block, "Q"), "\"Sol\""));

            var scl = Generator().GenerateCallBlock(project.CallBlocks[0], project);

            Assert.Contains("Open := \"Cmd\"", scl, StringComparison.Ordinal);
            Assert.Contains("Q => \"Sol\"", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void FallsBackToTheDeclaredInitialValueForAnUnboundInput()
        {
            var project = BuildCallProject();
            Member(project.Blocks[0], "Open").InitialValue = "TRUE";

            var scl = Generator().GenerateCallBlock(project.CallBlocks[0], project);

            Assert.Contains("Open := TRUE", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void OmitsAnUnboundInputThatHasNoDefault()
        {
            var project = BuildCallProject();

            var scl = Generator().GenerateCallBlock(project.CallBlocks[0], project);

            Assert.Contains("\"V01\"();", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void RefusesToEmitACallWithAnUnboundInOutParameter()
        {
            var project = BuildCallProject();
            project.Blocks[0].Interface.Add(ModelBuilder.InOut("Handle"));

            var exception = Assert.Throws<InvalidOperationException>(
                () => Generator().GenerateCallBlock(project.CallBlocks[0], project));

            Assert.Contains("Handle", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void RefusesToEmitANonVoidFunctionCallWithoutAResultTarget()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.Function("FC_Scale", "Real");
            project.Blocks.Add(block);
            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            callBlock.Units.Add(new UnitDefinition("S01", block));
            project.CallBlocks.Add(callBlock);

            Assert.Throws<InvalidOperationException>(() => Generator().GenerateCallBlock(callBlock, project));
        }

        [Fact]
        public void CallsAFunctionByItsBlockNameAndAssignsItsResult()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.Function("FC_Scale", "Real", ModelBuilder.Input("Raw", "Int"));
            project.Blocks.Add(block);
            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            callBlock.Units.Add(new UnitDefinition("S01", block) { ResultTarget = "  \"Scaled\"  " });
            project.CallBlocks.Add(callBlock);

            var scl = Generator().GenerateCallBlock(callBlock, project);

            Assert.Contains("\"Scaled\" := \"FC_Scale\"(", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void CallsAMultiInstanceThroughItsLocalName()
        {
            var project = BuildCallProject();
            project.CallBlocks[0].Kind = BlockKind.FunctionBlock;
            project.CallBlocks[0].Units[0].UseMultiInstance = true;

            var scl = Generator().GenerateCallBlock(project.CallBlocks[0], project);

            Assert.Contains("#V01(", scl, StringComparison.Ordinal);
            Assert.Contains("V01 : \"FB_Valve\";", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void OrdersUnitsByManualOrderThenByListPosition()
        {
            var project = BuildCallProject();
            var block = project.Blocks[0];
            var callBlock = project.CallBlocks[0];
            callBlock.Units.Clear();
            callBlock.Units.Add(new UnitDefinition("V_NoOrder1", block));
            callBlock.Units.Add(new UnitDefinition("V_Second", block) { ManualOrder = 2 });
            callBlock.Units.Add(new UnitDefinition("V_NoOrder2", block));
            callBlock.Units.Add(new UnitDefinition("V_First", block) { ManualOrder = 1 });

            var scl = Generator().GenerateCallBlock(callBlock, project);
            var order = new[] { "V_First", "V_Second", "V_NoOrder1", "V_NoOrder2" }
                .Select(name => Scl.IndexOfLineContaining(scl, "\"" + name + "\"("))
                .ToList();

            Assert.DoesNotContain(-1, order);
            for (var index = 1; index < order.Count; index++)
            {
                Assert.True(order[index - 1] < order[index], "Call order is wrong: " + string.Join(",", order));
            }
        }

        [Fact]
        public void NegativeManualOrderStillRunsBeforeUnorderedUnits()
        {
            var project = BuildCallProject();
            var block = project.Blocks[0];
            var callBlock = project.CallBlocks[0];
            callBlock.Units.Clear();
            callBlock.Units.Add(new UnitDefinition("V_Plain", block));
            callBlock.Units.Add(new UnitDefinition("V_Negative", block) { ManualOrder = -5 });

            var scl = Generator().GenerateCallBlock(callBlock, project);

            Assert.True(
                Scl.IndexOfLineContaining(scl, "\"V_Negative\"(") <
                Scl.IndexOfLineContaining(scl, "\"V_Plain\"("));
        }

        [Fact]
        public void ResolvesAStaleBlockIdByName()
        {
            var project = BuildCallProject();
            project.CallBlocks[0].Units[0].BlockId = Guid.NewGuid();

            var scl = Generator().GenerateCallBlock(project.CallBlocks[0], project);

            Assert.Contains("\"V01\"(", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void FailsLoudlyWhenAUnitReferencesNoKnownBlock()
        {
            var project = BuildCallProject();
            project.CallBlocks[0].Units[0].BlockId = Guid.NewGuid();
            project.CallBlocks[0].Units[0].BlockName = "FB_Gone";

            var exception = Assert.Throws<InvalidOperationException>(
                () => Generator().GenerateCallBlock(project.CallBlocks[0], project));

            Assert.Contains("V01", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FoldsAMultiLineUnitCommentOntoTheCallHeaderLine()
        {
            var project = BuildCallProject();
            project.CallBlocks[0].Units[0].Comment = "first\nsecond";

            var scl = Scl.Normalize(Generator().GenerateCallBlock(project.CallBlocks[0], project));

            Assert.Contains("// ---- V01 : FB_Valve - first second ----\n", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void AlignsContinuationArgumentsUnderTheOpeningParenthesis()
        {
            var project = BuildCallProject();
            var block = project.Blocks[0];
            var unit = project.CallBlocks[0].Units[0];
            unit.Bindings.Add(new ParameterBinding(Member(block, "Open"), "\"A\""));
            unit.Bindings.Add(new ParameterBinding(Member(block, "Q"), "\"B\""));

            var lines = Scl.Lines(Generator().GenerateCallBlock(project.CallBlocks[0], project));
            var first = lines.Single(line => line.Contains("Open := \"A\""));
            var second = lines.Single(line => line.Contains("Q => \"B\""));

            Assert.Equal(
                first.IndexOf("Open", StringComparison.Ordinal),
                second.IndexOf("Q =>", StringComparison.Ordinal));
        }

        // ---------------------------------------------------------------
        // Whole-project generation
        // ---------------------------------------------------------------

        [Fact]
        public void RefusesToGenerateAnInvalidProject()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.FunctionBlock("IF"));

            var exception = Assert.Throws<ModelValidationException>(() => Generator().GenerateProject(project));

            Assert.Contains("INVALID_NAME", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void EmitsSourcesInTiaDependencyOrder()
        {
            var project = BuildCallProject();
            project.DataTypes.Add(ModelBuilder.Udt("UDT_A", new UdtMember("X", "Bool")));
            project.CallBlocks[0].Kind = BlockKind.FunctionBlock;

            var sources = Generator().GenerateProject(project);

            Assert.Equal(
                new[] { "00_Types.scl", "FB_Valve.scl", "90_Instances.scl", "99_FC_Calls.scl", "99z_FC_Calls_DB.scl" },
                sources.Select(source => source.FileName).ToArray());
            Assert.Equal(
                Enumerable.Range(0, sources.Count).ToArray(),
                sources.Select(source => source.Order).ToArray());
        }

        [Fact]
        public void OmitsTheTypesSourceWhenThereAreNoDataTypes()
        {
            var sources = Generator().GenerateProject(BuildCallProject());

            Assert.DoesNotContain("00_Types.scl", sources.Select(source => source.FileName));
        }

        [Fact]
        public void OmitsTheInstanceSourceWhenEveryCallIsAMultiInstance()
        {
            var project = BuildCallProject();
            project.CallBlocks[0].Kind = BlockKind.FunctionBlock;
            project.CallBlocks[0].Units[0].UseMultiInstance = true;

            var sources = Generator().GenerateProject(project);

            Assert.DoesNotContain("90_Instances.scl", sources.Select(source => source.FileName));
        }

        [Fact]
        public void SkipsBlocksWhoseBodyIsOwnedByTheTiaProject()
        {
            var project = BuildCallProject();
            project.Blocks[0].ImportedInterfaceOnly = true;

            var sources = Generator().GenerateProject(project);

            Assert.DoesNotContain("FB_Valve.scl", sources.Select(source => source.FileName));
            Assert.Contains("90_Instances.scl", sources.Select(source => source.FileName));
        }

        [Fact]
        public void GenerationIsDeterministic()
        {
            var project = BuildCallProject();
            project.DataTypes.Add(ModelBuilder.Udt("UDT_A", new UdtMember("X", "Bool")));

            var first = Generator().GenerateProject(project);
            var second = Generator().GenerateProject(project);

            Assert.Equal(
                first.Select(source => source.FileName + " " + source.Content).ToArray(),
                second.Select(source => source.FileName + " " + source.Content).ToArray());
        }

        /// <summary>
        /// Two sources sharing a file name would silently overwrite each other
        /// when the bundle is written, and TIA would import a program that is
        /// one block short. Block names that sit as close as the rules allow to
        /// the generator's own reserved names must still come out distinct.
        /// </summary>
        [Fact]
        public void GeneratedFileNamesStayUniqueForNamesThatCrowdTheReservedOnes()
        {
            var project = ModelBuilder.Plant();
            foreach (var name in new[]
            {
                "_00_Types", "Types", "_90_Instances", "Instances",
                "_99_FC_Calls", "_99z_FC_Calls_DB", "FC_Calls_DB_x"
            })
            {
                project.Blocks.Add(ModelBuilder.FunctionBlock(name));
            }

            project.DataTypes.Add(ModelBuilder.Udt("UDT_A", new UdtMember("X", "Bool")));
            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.FunctionBlock };
            callBlock.Units.Add(new UnitDefinition("V01", project.Blocks[0]));
            project.CallBlocks.Add(callBlock);

            var fileNames = Generator().GenerateProject(project)
                .Select(source => source.FileName)
                .ToList();

            Assert.Equal(
                fileNames.Count,
                fileNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        /// <summary>
        /// The reason the previous property holds is structural: every file name
        /// the generator invents for itself starts with a digit, and a block
        /// name that starts with a digit is not a legal SCL identifier. If
        /// either half of that ever changes, silent overwrites become possible.
        /// </summary>
        [Fact]
        public void EveryReservedSourceNameStartsWithADigitThatSclNamesCannot()
        {
            var project = BuildCallProject();
            project.DataTypes.Add(ModelBuilder.Udt("UDT_A", new UdtMember("X", "Bool")));
            project.CallBlocks[0].Kind = BlockKind.FunctionBlock;

            var blockNames = new[] { project.Blocks[0].Name };
            var reserved = Generator().GenerateProject(project)
                .Select(source => source.FileName)
                .Where(name => !blockNames.Any(block =>
                    string.Equals(name, block + ".scl", StringComparison.Ordinal)))
                .ToList();

            Assert.NotEmpty(reserved);
            foreach (var name in reserved)
            {
                Assert.True(char.IsDigit(name[0]), name + " does not start with a digit.");
                Assert.False(
                    SclName.IsValid(Path.GetFileNameWithoutExtension(name)),
                    Path.GetFileNameWithoutExtension(name) + " is a legal block name and could collide.");
            }
        }

        /// <summary>
        /// InitialValue is copied verbatim into a declaration and into unbound
        /// call arguments. A newline inside it ends the declaration early, so
        /// the rest of the interface becomes free-standing garbage that TIA
        /// either rejects or, worse, misreads.
        /// </summary>
        [Fact]
        public void RefusesAnInitialValueThatWouldBreakOutOfItsDeclaration()
        {
            var project = BuildCallProject();
            Member(project.Blocks[0], "Open").InitialValue = "TRUE;\r\nEND_VAR\r\nVAR_TEMP\r\nInjected : Bool";

            var declarationSurvivedIntact = false;
            try
            {
                var sources = Generator().GenerateProject(project);
                var blockSource = sources.Single(source => source.FileName == "FB_Valve.scl").Content;
                declarationSurvivedIntact = Scl.CountOccurrences(blockSource, "END_VAR") ==
                    Scl.CountOccurrences(blockSource, "VAR_INPUT") +
                    Scl.CountOccurrences(blockSource, "VAR_OUTPUT");
            }
            catch (ModelValidationException)
            {
                declarationSurvivedIntact = true;
            }

            Assert.True(
                declarationSurvivedIntact,
                "A multi-line initial value escaped its declaration and rewrote the interface.");
        }

        [Fact]
        public void RefusesAResultTargetThatWouldBreakOutOfItsCall()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.Function("FC_Scale", "Real");
            project.Blocks.Add(block);
            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            callBlock.Units.Add(new UnitDefinition("S01", block)
            {
                ResultTarget = "\"A\";\r\n   \"Injected\"()"
            });
            project.CallBlocks.Add(callBlock);

            var callSurvivedIntact = false;
            try
            {
                var scl = Generator().GenerateProject(project)
                    .Single(source => source.FileName == "99_FC_Calls.scl").Content;
                callSurvivedIntact = !scl.Contains("\"Injected\"()");
            }
            catch (ModelValidationException)
            {
                callSurvivedIntact = true;
            }

            Assert.True(callSurvivedIntact, "A multi-line result target injected an extra statement.");
        }

        private static PlantProject BuildCallProject()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock(
                "FB_Valve",
                ModelBuilder.Input("Open"),
                ModelBuilder.Output("Q"));
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            callBlock.Units.Add(new UnitDefinition("V01", block));
            project.CallBlocks.Add(callBlock);
            return project;
        }

        private static InterfaceMember Member(BlockDefinition block, string name)
        {
            return block.Interface.Single(member => string.Equals(member.Name, name, StringComparison.Ordinal));
        }
    }
}
