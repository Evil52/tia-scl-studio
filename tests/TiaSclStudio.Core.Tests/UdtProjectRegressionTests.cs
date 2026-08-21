using System;
using System.Linq;
using TiaSclStudio.Core.Generation;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Core.Tests
{
    /// <summary>
    /// Integration boundary between the shared PLC type catalogue, project
    /// validation and emitted SCL. A free-form editor or importer must not be
    /// able to bypass this boundary by writing an arbitrary type string.
    /// </summary>
    public sealed class UdtProjectRegressionTests
    {
        [Fact]
        public void UnknownTypesAreRejectedInEveryPersistedTypeContext()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.Function(
                "FC_Bad",
                "UnknownReturn",
                ModelBuilder.Input("InputValue", "UnknownInput")));
            project.DataTypes.Add(ModelBuilder.Udt(
                "Payload",
                new UdtMember("Nested", "UnknownMember")));
            project.Tags.Add(ModelBuilder.Tag("BadTag", "UnknownTag"));
            project.CallBlocks.Add(new CallBlockDefinition
            {
                Name = "FC_Calls",
                Kind = BlockKind.Function,
                ReturnType = "Void",
                Interface = { ModelBuilder.Output("OutputValue", "UnknownOutput") }
            });

            var result = new ProjectValidator().Validate(project);

            var unknownPaths = result.Errors
                .Where(issue => issue.Code == "UNKNOWN_DATA_TYPE")
                .Select(issue => issue.Path)
                .ToArray();
            Assert.Contains("Blocks[FC_Bad].ReturnType", unknownPaths);
            Assert.Contains("Blocks[FC_Bad].Interface[InputValue].DataType", unknownPaths);
            Assert.Contains("DataTypes[Payload].Members[Nested].DataType", unknownPaths);
            Assert.Contains("Tags[BadTag].DataType", unknownPaths);
            Assert.Contains("CallBlocks[FC_Calls].Interface[OutputValue].DataType", unknownPaths);
        }

        [Fact]
        public void ADeclaredUdtResolvesQuotedUnquotedCaseInsensitiveAndInsideArrays()
        {
            var project = ModelBuilder.Plant();
            project.DataTypes.Add(ModelBuilder.Udt(
                "ValvePayload",
                new UdtMember("State", "Bool")));
            project.Blocks.Add(ModelBuilder.Function(
                "FC_UsePayload",
                "valvepayload",
                ModelBuilder.Input("Quoted", "\"ValvePayload\""),
                ModelBuilder.Input("Legacy", "VALVEPAYLOAD"),
                ModelBuilder.Input("Many", "Array[0..3] of array[-1..1] of \"valvepayload\"")));

            var result = new ProjectValidator().Validate(project);

            Assert.DoesNotContain(result.Errors, issue => issue.Code == "UNKNOWN_DATA_TYPE");
            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        }

        [Fact]
        public void VoidIsAcceptedOnlyAsAFunctionReturnType()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.Function(
                "FC_Void",
                "void",
                ModelBuilder.Output("NotAValueType", "Void")));

            var result = new ProjectValidator().Validate(project);

            var unknown = Assert.Single(result.Errors, issue => issue.Code == "UNKNOWN_DATA_TYPE");
            Assert.Equal("Blocks[FC_Void].Interface[NotAValueType].DataType", unknown.Path);
            Assert.DoesNotContain(result.Errors, issue => issue.Path == "Blocks[FC_Void].ReturnType");
        }

        [Fact]
        public void ADirectSelfReferenceProducesOneCycleDiagnostic()
        {
            var project = ModelBuilder.Plant();
            project.DataTypes.Add(ModelBuilder.Udt(
                "Recursive",
                new UdtMember("Next", "Array[0..1] of \"Recursive\"")));

            var result = new ProjectValidator().Validate(project);

            var cycle = Assert.Single(result.Errors, issue => issue.Code == "UDT_REFERENCE_CYCLE");
            Assert.Equal("DataTypes", cycle.Path);
            Assert.DoesNotContain(result.Errors, issue => issue.Code == "UNKNOWN_DATA_TYPE");
        }

        [Fact]
        public void AnIndirectUdtCycleIsRejectedWithoutMisreportingKnownTypesAsUnknown()
        {
            var project = ModelBuilder.Plant();
            project.DataTypes.Add(ModelBuilder.Udt("LeftType", new UdtMember("Right", "\"RightType\"")));
            project.DataTypes.Add(ModelBuilder.Udt("RightType", new UdtMember("Left", "\"LeftType\"")));

            var result = new ProjectValidator().Validate(project);

            Assert.Single(result.Errors, issue => issue.Code == "UDT_REFERENCE_CYCLE");
            Assert.DoesNotContain(result.Errors, issue => issue.Code == "UNKNOWN_DATA_TYPE");
        }

        [Fact]
        public void AnUnresolvedUdtMemberIsReportedAsUnknownAndDoesNotAddAMisleadingCycle()
        {
            var project = ModelBuilder.Plant();
            project.DataTypes.Add(ModelBuilder.Udt("Envelope", new UdtMember("Payload", "\"MissingType\"")));

            var result = new ProjectValidator().Validate(project);

            Assert.Single(result.Errors, issue => issue.Code == "UNKNOWN_DATA_TYPE");
            Assert.DoesNotContain(result.Errors, issue => issue.Code == "UDT_REFERENCE_CYCLE");
        }

        [Fact]
        public void UdtGenerationIsDependencyFirstEvenWhenTheModelListIsReversed()
        {
            var leaf = ModelBuilder.Udt("LeafType", new UdtMember("Value", "Int"));
            var middle = ModelBuilder.Udt("MiddleType", new UdtMember("Leaf", "\"LeafType\""));
            var root = ModelBuilder.Udt(
                "RootType",
                new UdtMember("Items", "Array[0..2] of \"MiddleType\""));

            var source = new SclGenerator().GenerateUdts(new[] { root, middle, leaf });

            var leafIndex = source.IndexOf("TYPE \"LeafType\"", StringComparison.Ordinal);
            var middleIndex = source.IndexOf("TYPE \"MiddleType\"", StringComparison.Ordinal);
            var rootIndex = source.IndexOf("TYPE \"RootType\"", StringComparison.Ordinal);
            Assert.True(leafIndex >= 0, source);
            Assert.True(leafIndex < middleIndex, source);
            Assert.True(middleIndex < rootIndex, source);
        }

        [Fact]
        public void EveryGeneratorBoundaryCanonicalizesLegacyUdtReferences()
        {
            var state = ModelBuilder.Udt("MachineState", new UdtMember("Code", "Int"));
            var envelope = ModelBuilder.Udt(
                "Envelope",
                new UdtMember("Current", "machinestate"));
            var catalog = new[] { envelope, state };
            var block = ModelBuilder.Function(
                "FC_ReadState",
                "MACHINESTATE",
                ModelBuilder.Input("History", "Array[0..3] of machinestate"));
            var callBlock = new CallBlockDefinition
            {
                Name = "FC_CallState",
                Kind = BlockKind.Function,
                ReturnType = "MachineState",
                Interface = { ModelBuilder.Input("InputValue", "envelope") }
            };
            var generator = new SclGenerator();

            var udtSource = generator.GenerateUdts(catalog);
            var blockSource = generator.GenerateBlock(block, catalog);
            var callSource = generator.GenerateCallBlock(
                callBlock,
                new BlockDefinition[0],
                catalog);

            Assert.Contains("Current : \"MachineState\";", udtSource, StringComparison.Ordinal);
            Assert.Contains("FUNCTION \"FC_ReadState\" : \"MachineState\"", blockSource, StringComparison.Ordinal);
            Assert.Contains(
                "History : Array[0..3] of \"MachineState\";",
                blockSource,
                StringComparison.Ordinal);
            Assert.Contains("FUNCTION \"FC_CallState\" : \"MachineState\"", callSource, StringComparison.Ordinal);
            Assert.Contains("InputValue : \"Envelope\";", callSource, StringComparison.Ordinal);
        }

        [Fact]
        public void WholeProjectGenerationRefusesAnUnknownImportedInterfaceType()
        {
            var project = ModelBuilder.Plant();
            var imported = ModelBuilder.FunctionBlock(
                "FB_Imported",
                ModelBuilder.Input("Value", "AnythingAtAll"));
            imported.ImportedInterfaceOnly = true;
            project.Blocks.Add(imported);

            var exception = Assert.Throws<ModelValidationException>(
                () => new SclGenerator().GenerateProject(project));

            Assert.Contains("UNKNOWN_DATA_TYPE", exception.Message, StringComparison.Ordinal);
        }
    }
}
