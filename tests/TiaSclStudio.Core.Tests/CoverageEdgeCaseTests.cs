using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TiaSclStudio.Core.Generation;
using TiaSclStudio.Core.Importing;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Core.Tests
{
    public sealed class CoverageEdgeCaseTests
    {
        [Fact]
        public void GeneratorPublicOverloadsRejectEveryNullDependency()
        {
            var generator = new SclGenerator();
            var block = ModelBuilder.FunctionBlock("FB_Test");
            var unit = new UnitDefinition("Test", block);
            var callBlock = new CallBlockDefinition { Name = "FC_Calls" };

            Assert.Equal("dataTypes", Assert.Throws<ArgumentNullException>(() =>
                generator.GenerateBlock(block, null)).ParamName);
            Assert.Equal("unit", Assert.Throws<ArgumentNullException>(() =>
                generator.GenerateInstanceDb(null, block)).ParamName);
            Assert.Equal("block", Assert.Throws<ArgumentNullException>(() =>
                generator.GenerateInstanceDb(unit, null)).ParamName);
            Assert.Equal("dataTypes", Assert.Throws<ArgumentNullException>(() =>
                generator.GenerateCallBlock(
                    callBlock,
                    new BlockDefinition[0],
                    null)).ParamName);
            Assert.Equal("project", Assert.Throws<ArgumentNullException>(() =>
                generator.GenerateCallBlock(callBlock, (PlantProject)null)).ParamName);
        }

        [Fact]
        public void GlobalDbGeneratorSeparatesSeveralOwnedBlocks()
        {
            var project = ModelBuilder.Plant();
            project.DataTypes.Add(ModelBuilder.Udt("Valve_Data"));
            project.DataBlockVariables.Add(ModelBuilder.DbVariable("DB_A", "Data", "Valve_Data"));
            project.DataBlockVariables.Add(ModelBuilder.DbVariable("DB_B", "Data", "Valve_Data"));

            var scl = new SclGenerator().GenerateGlobalDataBlocks(project);

            Assert.Contains("END_DATA_BLOCK\r\n\r\nDATA_BLOCK", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratorSkipsNullBindingsBeforeNameFallback()
        {
            var block = ModelBuilder.FunctionBlock("FB_Test", ModelBuilder.Input("Enable"));
            var unit = new UnitDefinition("Test", block);
            unit.Bindings.Add(null);
            unit.Bindings.Add(new ParameterBinding
            {
                ParameterName = "Enable",
                Expression = "TRUE"
            });
            var callBlock = new CallBlockDefinition { Name = "FC_Calls" };
            callBlock.Units.Add(unit);

            var scl = new SclGenerator().GenerateCallBlock(callBlock, new[] { block });

            Assert.Contains("Enable := TRUE", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void ModelBindingGuardsRejectNullTargets()
        {
            Assert.Equal("parameter", Assert.Throws<ArgumentNullException>(() =>
                new ParameterBinding(null, "TRUE")).ParamName);
            Assert.Equal("parameter", Assert.Throws<ArgumentNullException>(() =>
                new ParameterBinding().BindTo(null)).ParamName);
            Assert.Equal("block", Assert.Throws<ArgumentNullException>(() =>
                new UnitDefinition("Unit", null)).ParamName);
            Assert.Equal("block", Assert.Throws<ArgumentNullException>(() =>
                new UnitDefinition().BindTo(null)).ParamName);
        }

        [Fact]
        public void SingleLineAndXmlGuardsHandleEmptyAndSurrogateEdges()
        {
            Assert.Equal(string.Empty, SclText.SingleLine(null));
            Assert.Equal(string.Empty, SclText.SingleLine(string.Empty));
            Assert.True(SclText.HasNonPersistableCharacters("\uD800"));
            Assert.True(SclText.HasNonPersistableCharacters("\uD800x"));
            Assert.True(SclText.HasNonPersistableCharacters("\uDC00"));
            Assert.False(SclText.HasNonPersistableCharacters("\uD83D\uDE80"));
        }

        [Fact]
        public void PlcTypesRejectOverflowAndInvalidSystemAndUdtNames()
        {
            string canonical;
            Guid udtId;
            Assert.False(PlcDataTypes.TryResolve(
                "String[999999999999999999999999999999]",
                new UdtDefinition[0],
                false,
                out canonical,
                out udtId));
            Assert.False(PlcDataTypes.TryGetReferencedUdt(
                "Bool",
                new UdtDefinition[0],
                out var referenced));
            Assert.Null(referenced);
            Assert.False(PlcDataTypes.TryResolveSystemBlockInstanceType(null, out canonical));
            Assert.False(PlcDataTypes.TryResolveSystemBlockInstanceType("TON_TIME\n", out canonical));
            Assert.Throws<ArgumentException>(() => PlcDataTypes.QuoteUdtName("bad-name"));
            Assert.False(PlcDataTypes.ReferencesUdt("Valve_Data", "bad-name"));
            Assert.False(PlcDataTypes.ReferencesUdt(null, "Valve_Data"));
        }

        [Fact]
        public void UdtRewriteRejectsInvalidTargetAndPreservesInvalidArrayBounds()
        {
            Assert.Throws<ArgumentException>(() =>
                PlcDataTypes.RewriteUdtReference("OldType", "OldType", "bad-name"));
            Assert.Equal(
                "Array[4..2] of OldType",
                PlcDataTypes.RewriteUdtReference(
                    "Array[4..2] of OldType",
                    "OldType",
                    "NewType"));
            Assert.Equal(string.Empty, PlcDataTypes.RewriteUdtReference(null, "OldType", "NewType"));
        }

        [Fact]
        public void ValidatorReportsCpuAndDbOwnershipAndTypeProblems()
        {
            var project = ModelBuilder.Plant();
            project.CpuFamily = (PlcCpuFamily)999;
            project.DataBlockVariables.Add(ModelBuilder.DbVariable("DB_State", "Data", "Bool", true));
            project.DataBlockVariables.Add(ModelBuilder.DbVariable("db_state", "data", "Bool", false));

            var result = new ProjectValidator().Validate(project);

            Assert.Contains(result.Issues, issue => issue.Code == "INVALID_CPU_FAMILY");
            Assert.Contains(result.Issues, issue => issue.Code == "DB_OWNERSHIP_MIXED");
            Assert.Contains(result.Issues, issue => issue.Code == "DUPLICATE_DB_VARIABLE");
            Assert.Equal(2, result.Issues.Count(issue => issue.Code == "DB_VARIABLE_UDT_REQUIRED"));
        }

        [Fact]
        public void ValidatorCoversLegacyStaleAndDuplicateParameterReferences()
        {
            var legacyParameter = ModelBuilder.Input("Legacy");
            legacyParameter.Id = Guid.Empty;
            var stableParameter = ModelBuilder.Input("Stable");
            var inOut = ModelBuilder.InOut("Shared");
            inOut.Id = Guid.Empty;
            var block = ModelBuilder.FunctionBlock(
                "FB_Test",
                legacyParameter,
                stableParameter,
                inOut);
            var unit = new UnitDefinition("Test", block);
            var duplicateId = Guid.NewGuid();
            unit.Bindings.Add(new ParameterBinding
            {
                Id = duplicateId,
                ParameterName = "Legacy",
                Expression = "TRUE"
            });
            unit.Bindings.Add(new ParameterBinding
            {
                Id = duplicateId,
                ParameterName = "Legacy",
                Expression = "FALSE"
            });
            unit.Bindings.Add(new ParameterBinding
            {
                ParameterId = Guid.NewGuid(),
                ParameterName = "Stable",
                Expression = "TRUE"
            });
            unit.Bindings.Add(new ParameterBinding
            {
                ParameterName = "Shared",
                Expression = "Writable"
            });
            var calls = new CallBlockDefinition { Name = "FC_Calls" };
            calls.Units.Add(unit);
            var project = ModelBuilder.Plant();
            project.Blocks.Add(block);
            project.CallBlocks.Add(calls);

            var result = new ProjectValidator().Validate(project);
            var codes = result.Issues.Select(issue => issue.Code).ToArray();

            Assert.Contains("LEGACY_PARAMETER_REFERENCE", codes);
            Assert.Contains("STALE_PARAMETER_ID", codes);
            Assert.Contains("DUPLICATE_BINDING", codes);
            Assert.Contains("DUPLICATE_ID", codes);
            Assert.DoesNotContain("INOUT_BINDING_REQUIRED", codes);
        }

        [Fact]
        public void MissingBlockStillValidatesBindingIdentities()
        {
            var missing = new UnitDefinition
            {
                Name = "Missing",
                BlockId = Guid.NewGuid(),
                BlockName = "FB_Missing"
            };
            missing.Bindings.Add(new ParameterBinding
            {
                Id = Guid.Empty,
                ParameterName = "Enable",
                Expression = "TRUE"
            });
            var calls = new CallBlockDefinition { Name = "FC_Calls" };
            calls.Units.Add(missing);
            var project = ModelBuilder.Plant();
            project.CallBlocks.Add(calls);

            var result = new ProjectValidator().Validate(project);

            Assert.Contains(result.Issues, issue => issue.Code == "BLOCK_REFERENCE_NOT_FOUND");
            Assert.Contains(result.Issues, issue => issue.Code == "EMPTY_ID");
        }

        [Fact]
        public void InternalDefensiveGuardsRejectImpossibleParserAndGeneratorInputs()
        {
            var resolveOperand = typeof(PlcAddressing).GetMethod(
                "TryResolveOperand",
                BindingFlags.NonPublic | BindingFlags.Static);
            var operandArguments = new object[]
            {
                "X",
                "Bool",
                PlcSignalKind.DigitalInput,
                string.Empty,
                string.Empty
            };
            Assert.False((bool)resolveOperand.Invoke(null, operandArguments));
            Assert.False(string.IsNullOrWhiteSpace((string)operandArguments[4]));

            var resolveBlock = typeof(SclGenerator).GetMethod(
                "ResolveBlock",
                BindingFlags.NonPublic | BindingFlags.Static);
            var blockError = Assert.Throws<TargetInvocationException>(() =>
                resolveBlock.Invoke(null, new object[] { new BlockDefinition[0], null }));
            Assert.IsType<ArgumentNullException>(blockError.InnerException);

            var import = new SclLibraryImportResult();
            var addItem = typeof(SclLibraryImportResult).GetMethod(
                "AddItem",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var importError = Assert.Throws<TargetInvocationException>(() =>
                addItem.Invoke(import, new object[] { null }));
            Assert.IsType<ArgumentNullException>(importError.InnerException);
        }

        [Fact]
        public void UdtDependencyWalkerDefendsAgainstAnInconsistentDependencyMap()
        {
            var root = ModelBuilder.Udt("Root");
            var missingId = Guid.NewGuid();
            var dependencies = new Dictionary<Guid, HashSet<Guid>>
            {
                { root.Id, new HashSet<Guid> { missingId } }
            };
            var originalIndex = new Dictionary<Guid, int> { { root.Id, 0 } };
            var visit = typeof(PlcDataTypes).GetMethod(
                "Visit",
                BindingFlags.NonPublic | BindingFlags.Static);

            var error = Assert.Throws<TargetInvocationException>(() => visit.Invoke(
                null,
                new object[]
                {
                    root,
                    new List<UdtDefinition> { root },
                    dependencies,
                    originalIndex,
                    new Dictionary<Guid, int>(),
                    new List<UdtDefinition>()
                }));

            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }
}
