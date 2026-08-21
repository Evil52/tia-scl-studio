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
    /// ProjectValidator is the only thing standing between a half-edited model
    /// and a generator that emits text into a customer's PLC project. These
    /// tests care about two properties: it never throws on a deformed model,
    /// and it never reports a model as valid when generation would go wrong.
    /// </summary>
    public sealed class ProjectValidatorTests
    {
        private static ValidationResult Validate(PlantProject project)
        {
            return new ProjectValidator().Validate(project);
        }

        private static bool HasCode(ValidationResult result, string code)
        {
            return result.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal));
        }

        private static string Codes(ValidationResult result)
        {
            return string.Join(", ", result.Issues.Select(issue => issue.Code));
        }

        [Fact]
        public void RejectsNullProject()
        {
            Assert.Throws<ArgumentNullException>(() => Validate(null));
        }

        [Fact]
        public void AcceptsAFreshEmptyProject()
        {
            var result = Validate(new PlantProject());

            Assert.True(result.IsValid, Codes(result));
            Assert.Empty(result.Issues);
        }

        // ---------------------------------------------------------------
        // Deformed collections. A crashed session or a hand-edited project
        // file can leave holes behind. Validation must survive them, and it
        // must report them: a model with a hole in it cannot be generated
        // from, so calling it valid would only move the failure downstream
        // into the generator as a NullReferenceException.
        // ---------------------------------------------------------------

        [Fact]
        public void ReportsMissingCollectionsInsteadOfPassingThemOn()
        {
            var project = new PlantProject
            {
                Blocks = null,
                DataTypes = null,
                Tags = null,
                CallBlocks = null
            };

            var result = Validate(project);

            Assert.Equal(4, result.Issues.Count(issue => issue.Code == "MISSING_COLLECTION"));
            Assert.False(result.IsValid);
        }

        [Fact]
        public void ReportsNullItemsInsideCollections()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(null);
            project.DataTypes.Add(null);
            project.Tags.Add(null);
            project.CallBlocks.Add(null);

            var result = Validate(project);

            Assert.Equal(4, result.Issues.Count(issue => issue.Code == "NULL_MODEL_ITEM"));
            Assert.False(result.IsValid);
        }

        [Fact]
        public void ReportsNullInterfaceMembersAndUnits()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("In"));
            block.Interface.Add(null);
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            callBlock.Units.Add(null);
            callBlock.Interface.Add(null);
            project.CallBlocks.Add(callBlock);

            var result = Validate(project);

            Assert.Equal(3, result.Issues.Count(issue => issue.Code == "NULL_MODEL_ITEM"));
        }

        [Fact]
        public void ReportsANullBindingInsideAUnit()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("In"));
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            var unit = new UnitDefinition("A01", block);
            unit.Bindings.Add(null);
            callBlock.Units.Add(unit);
            project.CallBlocks.Add(callBlock);

            Assert.True(HasCode(Validate(project), "NULL_MODEL_ITEM"), Codes(Validate(project)));
        }

        [Fact]
        public void ADeformedCollectionIsReportedRatherThanThrown()
        {
            var deformations = new Action<PlantProject>[]
            {
                project => project.Blocks = null,
                project => project.DataTypes = null,
                project => project.Tags = null,
                project => project.CallBlocks = null,
                project => project.Blocks.Add(null),
                project => project.Tags.Add(null),
                project => project.Blocks[0].Interface = null,
                project => project.Blocks[0].Interface.Add(null)
            };

            foreach (var deformation in deformations)
            {
                var project = ModelBuilder.Plant();
                project.Blocks.Add(ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("In")));
                deformation(project);

                var result = Validate(project);

                Assert.False(result.IsValid, "A deformed model was reported as valid.");
            }
        }

        [Fact]
        public void ValidationDoesNotMutateTheModel()
        {
            var project = DemoProjects.Valve().Plant;
            var block = project.Blocks[0];
            var originalName = block.Name;
            var originalMemberCount = block.Interface.Count;
            var originalId = block.Id;

            Validate(project);
            Validate(project);

            Assert.Equal(originalName, block.Name);
            Assert.Equal(originalMemberCount, block.Interface.Count);
            Assert.Equal(originalId, block.Id);
        }

        [Fact]
        public void ValidationIsRepeatableForTheSameModel()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.FunctionBlock("IF"));

            var first = Codes(Validate(project));
            var second = Codes(Validate(project));

            Assert.Equal(first, second);
        }

        // ---------------------------------------------------------------
        // Stable identity
        // ---------------------------------------------------------------

        [Fact]
        public void ReportsEmptyIdentity()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_A");
            block.Id = Guid.Empty;
            project.Blocks.Add(block);

            var result = Validate(project);

            Assert.True(HasCode(result, "EMPTY_ID"), Codes(result));
            Assert.False(result.IsValid);
        }

        [Fact]
        public void ReportsAnIdShredBetweenDifferentEntityKinds()
        {
            // A copy/paste that reused a Guid across a block and a tag would
            // otherwise silently break every ID-based reference in the project.
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_A");
            var tag = ModelBuilder.Tag("Tag_A");
            tag.Id = block.Id;
            project.Blocks.Add(block);
            project.Tags.Add(tag);

            var result = Validate(project);

            Assert.True(HasCode(result, "DUPLICATE_ID"), Codes(result));
        }

        [Fact]
        public void ReportsAnIdSharedBetweenAnInterfaceMemberAndItsBlock()
        {
            var project = ModelBuilder.Plant();
            var member = ModelBuilder.Input("In");
            var block = ModelBuilder.FunctionBlock("FB_A", member);
            member.Id = block.Id;
            project.Blocks.Add(block);

            var result = Validate(project);

            Assert.True(HasCode(result, "DUPLICATE_ID"), Codes(result));
        }

        // ---------------------------------------------------------------
        // Names and symbols
        // ---------------------------------------------------------------

        [Theory]
        [InlineData("FB Valve")]
        [InlineData("IF")]
        [InlineData("1Valve")]
        [InlineData("")]
        public void ReportsInvalidBlockNames(string name)
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.FunctionBlock(name));

            var result = Validate(project);

            Assert.True(HasCode(result, "INVALID_NAME"), Codes(result));
        }

        [Fact]
        public void ReportsDuplicateBlockNamesCaseInsensitively()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.FunctionBlock("FB_Valve"));
            project.Blocks.Add(ModelBuilder.FunctionBlock("fb_valve"));

            var result = Validate(project);

            Assert.True(HasCode(result, "DUPLICATE_NAME"), Codes(result));
        }

        [Fact]
        public void ReportsACollisionBetweenABlockAndATag()
        {
            // Both become global symbols in the PLC, so one of them would win
            // silently at import time.
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.FunctionBlock("Shared"));
            project.Tags.Add(ModelBuilder.Tag("shared"));

            var result = Validate(project);

            Assert.True(HasCode(result, "NAME_COLLISION"), Codes(result));
        }

        [Fact]
        public void ReportsACollisionBetweenAGlobalInstanceDbAndABlock()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_Valve");
            var clash = ModelBuilder.FunctionBlock("V01");
            project.Blocks.Add(block);
            project.Blocks.Add(clash);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            callBlock.Units.Add(new UnitDefinition("V01", block));
            project.CallBlocks.Add(callBlock);

            var result = Validate(project);

            Assert.True(HasCode(result, "NAME_COLLISION"), Codes(result));
        }

        [Fact]
        public void ReportsACollisionBetweenTheDefaultCallBlockDbAndABlock()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.FunctionBlock("FC_Calls_DB"));
            project.CallBlocks.Add(new CallBlockDefinition
            {
                Name = "FC_Calls",
                Kind = BlockKind.FunctionBlock
            });

            var result = Validate(project);

            Assert.True(HasCode(result, "NAME_COLLISION"), Codes(result));
        }

        [Fact]
        public void MultiInstanceNamesDoNotConsumeAGlobalSymbol()
        {
            // A multi-instance lives inside the parent FB, so the same name may
            // exist twice in different call blocks without colliding globally.
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_Valve");
            project.Blocks.Add(block);

            foreach (var callBlockName in new[] { "FB_CallsA", "FB_CallsB" })
            {
                var callBlock = new CallBlockDefinition
                {
                    Name = callBlockName,
                    Kind = BlockKind.FunctionBlock
                };
                callBlock.Units.Add(new UnitDefinition("V01", block) { UseMultiInstance = true });
                project.CallBlocks.Add(callBlock);
            }

            var result = Validate(project);

            Assert.False(HasCode(result, "NAME_COLLISION"), Codes(result));
        }

        [Fact]
        public void ReportsAMultiInstanceThatShadowsACallBlockInterfaceMember()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_Valve");
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FB_Calls", Kind = BlockKind.FunctionBlock };
            callBlock.Interface.Add(ModelBuilder.Input("V01"));
            callBlock.Units.Add(new UnitDefinition("V01", block) { UseMultiInstance = true });
            project.CallBlocks.Add(callBlock);

            var result = Validate(project);

            Assert.True(HasCode(result, "LOCAL_NAME_COLLISION"), Codes(result));
        }

        // ---------------------------------------------------------------
        // Versions
        // ---------------------------------------------------------------

        [Theory]
        [InlineData("0.1")]
        [InlineData("12.34")]
        [InlineData(" 1.0 ")]
        public void AcceptsNumericMajorMinorVersions(string version)
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_A");
            block.Version = version;
            project.Blocks.Add(block);

            Assert.False(HasCode(Validate(project), "INVALID_VERSION"));
        }

        [Theory]
        [InlineData("1")]
        [InlineData("1.2.3")]
        [InlineData("v1.0")]
        [InlineData("1,0")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("1.0-beta")]
        public void RejectsOtherVersionForms(string version)
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_A");
            block.Version = version;
            project.Blocks.Add(block);

            Assert.True(HasCode(Validate(project), "INVALID_VERSION"));
        }

        /// <summary>
        /// .NET anchors "$" before a trailing newline, so "0.1\n" satisfies the
        /// version pattern. That is only safe because the validator and the
        /// generator agree to trim it. If they ever disagree, a newline reaches
        /// the VERSION header and splits the block declaration in two.
        /// </summary>
        [Fact]
        public void AVersionWithATrailingNewlineIsAcceptedAndThenNormalizedAway()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_A");
            block.Version = "0.1\n";
            project.Blocks.Add(block);

            Assert.False(HasCode(Validate(project), "INVALID_VERSION"));

            var scl = new SclGenerator().GenerateProject(project)
                .Single(source => source.FileName == "FB_A.scl").Content;

            Assert.Contains("VERSION : 0.1\r\n", scl, StringComparison.Ordinal);
            Assert.Equal(1, Scl.CountOccurrences(scl, "VERSION"));
        }

        [Fact]
        public void RejectsAVersionThatHidesAnInteriorNewline()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_A");
            block.Version = "0.1\nFAMILY : 'Injected'";
            project.Blocks.Add(block);

            Assert.True(HasCode(Validate(project), "INVALID_VERSION"));
        }

        // ---------------------------------------------------------------
        // Free text that is copied verbatim into one SCL line
        // ---------------------------------------------------------------

        [Theory]
        [InlineData("Bool;\r\nEND_VAR")]
        [InlineData("Bool\nInjected : Int")]
        [InlineData("Bo\tol")]
        [InlineData("Bool ")]
        public void ReportsAControlCharacterInADataType(string dataType)
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("In", dataType)));

            Assert.True(HasCode(Validate(project), "CONTROL_CHARACTER"), Codes(Validate(project)));
        }

        [Fact]
        public void ReportsAControlCharacterInAnInitialValue()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.FunctionBlock(
                "FB_A",
                ModelBuilder.Input("In", "Bool", "TRUE;\r\nEND_VAR\r\nVAR_TEMP\r\nInjected : Bool")));

            Assert.True(HasCode(Validate(project), "CONTROL_CHARACTER"));
        }

        [Fact]
        public void ReportsAControlCharacterInAUdtMemberInitialValue()
        {
            var project = ModelBuilder.Plant();
            var member = new UdtMember("X", "Bool") { InitialValue = "TRUE\r\nEND_STRUCT;" };
            project.DataTypes.Add(ModelBuilder.Udt("UDT_A", member));

            Assert.True(HasCode(Validate(project), "CONTROL_CHARACTER"));
        }

        [Fact]
        public void ReportsAControlCharacterInAResultTarget()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.Function("FC_Scale", "Real");
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            callBlock.Units.Add(new UnitDefinition("S01", block) { ResultTarget = "\"A\";\r\n   \"Injected\"()" });
            project.CallBlocks.Add(callBlock);

            Assert.True(HasCode(Validate(project), "CONTROL_CHARACTER"));
        }

        [Fact]
        public void ReportsAControlCharacterInABindingExpression()
        {
            var project = ModelBuilder.Plant();
            var member = ModelBuilder.Input("In");
            var block = ModelBuilder.FunctionBlock("FB_A", member);
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            var unit = new UnitDefinition("A01", block);
            unit.Bindings.Add(new ParameterBinding(member, "TRUE);\r\n   \"Injected\"("));
            callBlock.Units.Add(unit);
            project.CallBlocks.Add(callBlock);

            Assert.True(HasCode(Validate(project), "CONTROL_CHARACTER"));
        }

        [Fact]
        public void ReportsAControlCharacterInAFunctionReturnType()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.Function("FC_A", "Real\r\nVAR_INPUT\r\nInjected : Bool;\r\nEND_VAR"));

            Assert.True(HasCode(Validate(project), "CONTROL_CHARACTER"));
        }

        [Fact]
        public void OrdinarySingleLineValuesAreNotFlaggedAsControlCharacters()
        {
            var project = ModelBuilder.Plant();
            var member = ModelBuilder.Input("In", "Array[0..9] of Bool", "T#3s");
            member.Comment = "multi\r\nline comments stay allowed";
            project.Blocks.Add(ModelBuilder.FunctionBlock("FB_A", member));

            Assert.False(HasCode(Validate(project), "CONTROL_CHARACTER"), Codes(Validate(project)));
        }

        // ---------------------------------------------------------------
        // Block contract
        // ---------------------------------------------------------------

        [Fact]
        public void ReportsAStaticMemberOnAFunction()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.Function("FC_A", "Void", ModelBuilder.Static("State")));

            Assert.True(HasCode(Validate(project), "FC_STATIC_NOT_ALLOWED"));
        }

        [Fact]
        public void ReportsAFunctionWithoutAReturnType()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.Function("FC_A", "   "));

            Assert.True(HasCode(Validate(project), "RETURN_TYPE_REQUIRED"));
        }

        [Fact]
        public void WarnsAboutAReturnTypeOnAFunctionBlock()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_A");
            block.ReturnType = "Int";
            project.Blocks.Add(block);

            var result = Validate(project);

            Assert.True(HasCode(result, "FB_RETURN_TYPE_IGNORED"), Codes(result));
            Assert.True(result.IsValid, "A cosmetic mismatch must not block generation.");
        }

        [Fact]
        public void ReportsAnUndefinedBlockKind()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_A");
            block.Kind = (BlockKind)42;
            project.Blocks.Add(block);

            Assert.True(HasCode(Validate(project), "INVALID_BLOCK_KIND"));
        }

        [Fact]
        public void ReportsAnUndefinedInterfaceSection()
        {
            var project = ModelBuilder.Plant();
            var member = ModelBuilder.Input("In");
            member.Section = (InterfaceSection)99;
            project.Blocks.Add(ModelBuilder.FunctionBlock("FB_A", member));

            Assert.True(HasCode(Validate(project), "INVALID_INTERFACE_SECTION"));
        }

        [Fact]
        public void ReportsAMissingDataType()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("In", "  ")));

            Assert.True(HasCode(Validate(project), "DATA_TYPE_REQUIRED"));
        }

        [Fact]
        public void ReportsDuplicateInterfaceMemberNamesAcrossSections()
        {
            // Sections do not create separate namespaces in SCL.
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.FunctionBlock(
                "FB_A",
                ModelBuilder.Input("Value"),
                ModelBuilder.Output("value")));

            Assert.True(HasCode(Validate(project), "DUPLICATE_NAME"));
        }

        // ---------------------------------------------------------------
        // Tags and data types
        // ---------------------------------------------------------------

        [Fact]
        public void ReportsATagWithoutAnAddress()
        {
            var project = ModelBuilder.Plant();
            project.Tags.Add(new TagDefinition { Name = "Tag_A", DataType = "Bool", Address = " " });

            Assert.True(HasCode(Validate(project), "TAG_ADDRESS_REQUIRED"));
        }

        [Fact]
        public void WarnsAboutAnEmptyDataType()
        {
            var project = ModelBuilder.Plant();
            project.DataTypes.Add(ModelBuilder.Udt("UDT_A"));

            var result = Validate(project);

            Assert.True(HasCode(result, "EMPTY_UDT"), Codes(result));
            Assert.True(result.IsValid);
        }

        // ---------------------------------------------------------------
        // Unit references and bindings
        // ---------------------------------------------------------------

        [Fact]
        public void ReportsAUnitPointingAtAMissingBlock()
        {
            var project = ModelBuilder.Plant();
            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            callBlock.Units.Add(new UnitDefinition
            {
                Name = "V01",
                BlockId = Guid.NewGuid(),
                BlockName = "FB_Missing"
            });
            project.CallBlocks.Add(callBlock);

            Assert.True(HasCode(Validate(project), "BLOCK_REFERENCE_NOT_FOUND"));
        }

        [Fact]
        public void WarnsWhenAStaleBlockIdFallsBackToTheName()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_Valve");
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            var unit = new UnitDefinition("V01", block);
            unit.BlockId = Guid.NewGuid();
            callBlock.Units.Add(unit);
            project.CallBlocks.Add(callBlock);

            var result = Validate(project);

            Assert.True(HasCode(result, "STALE_BLOCK_ID"), Codes(result));
            Assert.True(result.IsValid, "A recoverable reference must not block generation.");
        }

        [Fact]
        public void WarnsWhenALegacyUnitHasNoBlockIdAtAll()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_Valve");
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            callBlock.Units.Add(new UnitDefinition { Name = "V01", BlockName = "FB_Valve" });
            project.CallBlocks.Add(callBlock);

            Assert.True(HasCode(Validate(project), "LEGACY_BLOCK_REFERENCE"));
        }

        [Fact]
        public void ReportsAMultiInstanceOnAFunction()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.Function("FC_Scale", "Void");
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FB_Calls", Kind = BlockKind.FunctionBlock };
            callBlock.Units.Add(new UnitDefinition("S01", block) { UseMultiInstance = true });
            project.CallBlocks.Add(callBlock);

            Assert.True(HasCode(Validate(project), "FC_MULTI_INSTANCE"));
        }

        [Fact]
        public void ReportsAMultiInstanceInsideAFunctionCallBlock()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_Valve");
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            callBlock.Units.Add(new UnitDefinition("V01", block) { UseMultiInstance = true });
            project.CallBlocks.Add(callBlock);

            Assert.True(HasCode(Validate(project), "MULTI_INSTANCE_REQUIRES_FB"));
        }

        [Fact]
        public void ReportsAnUnboundInOutParameter()
        {
            var project = BuildProjectWithBoundUnit(ModelBuilder.InOut("Handle"), bindExpression: null);

            Assert.True(HasCode(Validate(project), "INOUT_BINDING_REQUIRED"));
        }

        [Fact]
        public void ReportsAWhitespaceOnlyInOutBinding()
        {
            var project = BuildProjectWithBoundUnit(ModelBuilder.InOut("Handle"), bindExpression: "   ");

            Assert.True(HasCode(Validate(project), "INOUT_BINDING_REQUIRED"));
        }

        [Fact]
        public void ReportsABindingToANonCallableSection()
        {
            var project = BuildProjectWithBoundUnit(ModelBuilder.Temp("Scratch"), bindExpression: "X");

            Assert.True(HasCode(Validate(project), "PARAMETER_NOT_CALLABLE"));
        }

        [Fact]
        public void ReportsTwoBindingsForTheSameParameter()
        {
            var project = ModelBuilder.Plant();
            var member = ModelBuilder.Input("In");
            var block = ModelBuilder.FunctionBlock("FB_A", member);
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            var unit = new UnitDefinition("A01", block);
            unit.Bindings.Add(new ParameterBinding(member, "First"));
            unit.Bindings.Add(new ParameterBinding(member, "Second"));
            callBlock.Units.Add(unit);
            project.CallBlocks.Add(callBlock);

            Assert.True(HasCode(Validate(project), "DUPLICATE_BINDING"));
        }

        [Fact]
        public void ReportsABindingToAParameterThatNoLongerExists()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("In"));
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            var unit = new UnitDefinition("A01", block);
            unit.Bindings.Add(new ParameterBinding
            {
                ParameterId = Guid.NewGuid(),
                ParameterName = "Removed",
                Expression = "X"
            });
            callBlock.Units.Add(unit);
            project.CallBlocks.Add(callBlock);

            Assert.True(HasCode(Validate(project), "PARAMETER_REFERENCE_NOT_FOUND"));
        }

        [Fact]
        public void ReportsANonVoidFunctionUnitWithoutAResultTarget()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.Function("FC_Scale", "Real");
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            callBlock.Units.Add(new UnitDefinition("S01", block));
            project.CallBlocks.Add(callBlock);

            Assert.True(HasCode(Validate(project), "FC_RESULT_TARGET_REQUIRED"));
        }

        [Fact]
        public void WarnsAboutAResultTargetOnACallWithNoReturnValue()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_Valve");
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            callBlock.Units.Add(new UnitDefinition("V01", block) { ResultTarget = "Somewhere" });
            project.CallBlocks.Add(callBlock);

            var result = Validate(project);

            Assert.True(HasCode(result, "RESULT_TARGET_IGNORED"), Codes(result));
            Assert.True(result.IsValid);
        }

        [Fact]
        public void WarnsAboutDuplicateManualOrder()
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_Valve");
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            callBlock.Units.Add(new UnitDefinition("V01", block) { ManualOrder = 1 });
            callBlock.Units.Add(new UnitDefinition("V02", block) { ManualOrder = 1 });
            project.CallBlocks.Add(callBlock);

            var result = Validate(project);

            Assert.True(HasCode(result, "DUPLICATE_MANUAL_ORDER"), Codes(result));
            Assert.True(result.IsValid);
        }

        [Fact]
        public void WarnsAboutAnInstanceDbNameOnAFunctionCallBlock()
        {
            var project = ModelBuilder.Plant();
            project.CallBlocks.Add(new CallBlockDefinition
            {
                Name = "FC_Calls",
                Kind = BlockKind.Function,
                InstanceDbName = "FC_Calls_DB"
            });

            Assert.True(HasCode(Validate(project), "FC_INSTANCE_DB_IGNORED"));
        }

        // ---------------------------------------------------------------
        // Result surface
        // ---------------------------------------------------------------

        [Fact]
        public void IssuesAreExposedAsAReadOnlyView()
        {
            var result = Validate(ModelBuilder.Plant());

            Assert.Throws<NotSupportedException>(() =>
                result.Issues.Add(new ValidationIssue(ValidationSeverity.Error, "X", "m", "p")));
        }

        [Fact]
        public void WarningsAloneKeepTheResultValid()
        {
            var project = ModelBuilder.Plant();
            project.DataTypes.Add(ModelBuilder.Udt("UDT_A"));

            var result = Validate(project);

            Assert.True(result.IsValid);
            Assert.NotEmpty(result.Warnings);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidateOrThrowCarriesEveryErrorInTheMessage()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.FunctionBlock("IF"));
            project.Tags.Add(new TagDefinition { Name = "Tag_A", DataType = "Bool", Address = string.Empty });

            var exception = Assert.Throws<ModelValidationException>(
                () => new ProjectValidator().ValidateOrThrow(project));

            Assert.Contains("INVALID_NAME", exception.Message, StringComparison.Ordinal);
            Assert.Contains("TAG_ADDRESS_REQUIRED", exception.Message, StringComparison.Ordinal);
            Assert.Equal(2, exception.Result.Errors.Count());
        }

        [Fact]
        public void ValidateOrThrowStaysSilentOnAValidProject()
        {
            new ProjectValidator().ValidateOrThrow(DemoProjects.Valve().Plant);
        }

        [Fact]
        public void ModelValidationExceptionRejectsANullResult()
        {
            Assert.Throws<ArgumentNullException>(() => new ModelValidationException(null));
        }

        [Fact]
        public void EveryIssueCarriesALocatablePath()
        {
            var project = ModelBuilder.Plant();
            project.Blocks.Add(ModelBuilder.FunctionBlock("IF", ModelBuilder.Input("VAR")));

            var result = Validate(project);

            Assert.NotEmpty(result.Issues);
            foreach (var issue in result.Issues)
            {
                Assert.False(string.IsNullOrWhiteSpace(issue.Path), issue.Code + " has no path.");
                Assert.False(string.IsNullOrWhiteSpace(issue.Message), issue.Code + " has no message.");
            }
        }

        private static PlantProject BuildProjectWithBoundUnit(InterfaceMember member, string bindExpression)
        {
            var project = ModelBuilder.Plant();
            var block = ModelBuilder.FunctionBlock("FB_A", member);
            project.Blocks.Add(block);

            var callBlock = new CallBlockDefinition { Name = "FC_Calls", Kind = BlockKind.Function };
            var unit = new UnitDefinition("A01", block);
            if (bindExpression != null)
            {
                unit.Bindings.Add(new ParameterBinding(member, bindExpression));
            }

            callBlock.Units.Add(unit);
            project.CallBlocks.Add(callBlock);
            return project;
        }
    }
}
