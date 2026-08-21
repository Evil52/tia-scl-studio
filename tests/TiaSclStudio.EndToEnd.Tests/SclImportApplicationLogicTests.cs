using System;
using System.Linq;
using TiaSclStudio.Core.Importing;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.App
{
    /// <summary>
    /// Regression coverage for the import preview/commit boundary. Parsing may
    /// return useful preview rows alongside diagnostics, but no part of such a
    /// batch may reach the live project unless the complete plan is applicable.
    /// </summary>
    public sealed class SclImportApplicationLogicTests
    {
        [Fact]
        public void ANewBatchCanReferenceAUdtDeclaredLaterAndCanonicalizesEveryReference()
        {
            var project = ModelBuilder.Project();
            var parsed = Parse(
                "FUNCTION FC_UsePayload : payload",
                "VAR_INPUT",
                "   InputValue : Array[0..2] of PAYLOAD;",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION",
                "TYPE Payload",
                "STRUCT",
                "   Value : Int;",
                "END_STRUCT;",
                "END_TYPE");

            var plan = SclImportApplicationLogic.BuildPlan(project, parsed, false);

            Assert.True(plan.CanApply, Messages(plan));
            Assert.Equal(2, plan.AddCount);
            SclImportApplicationLogic.Apply(project, plan);

            var importedType = Assert.Single(project.Plant.DataTypes);
            Assert.Equal("Payload", importedType.Name);
            var importedBlock = Assert.Single(project.Plant.Blocks);
            Assert.Equal("\"Payload\"", importedBlock.ReturnType);
            Assert.Equal("Array[0..2] of \"Payload\"", Assert.Single(importedBlock.Interface).DataType);
            Assert.True(importedBlock.ImportedInterfaceOnly);
            Assert.Equal(string.Empty, importedBlock.SclBody);
        }

        [Fact]
        public void OneUnknownTypeBlocksTheWholeMixedBatchWithoutMutation()
        {
            var project = ModelBuilder.Project();
            var parsed = Parse(
                "TYPE Payload",
                "STRUCT",
                "   Value : Int;",
                "END_STRUCT;",
                "END_TYPE",
                "FUNCTION_BLOCK FB_Good",
                "VAR_INPUT",
                "   InputValue : Payload;",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK",
                "FUNCTION_BLOCK FB_Bad",
                "VAR_INPUT",
                "   InputValue : ThisTypeDoesNotExist;",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");
            Assert.False(parsed.HasErrors, ParserCodes(parsed));

            var plan = SclImportApplicationLogic.BuildPlan(project, parsed, false);

            Assert.False(plan.CanApply);
            Assert.Contains(plan.Messages, message => message.Code == "SCL_IMPORT_TYPE_UNRESOLVED");
            Assert.Throws<InvalidOperationException>(() => SclImportApplicationLogic.Apply(project, plan));
            Assert.Empty(project.Plant.DataTypes);
            Assert.Empty(project.Plant.Blocks);
        }

        [Fact]
        public void AParserErrorBlocksOtherwiseValidPreviewRowsWithoutMutation()
        {
            var project = ModelBuilder.Project();
            var parsed = Parse(
                "FUNCTION_BLOCK FB_Valid",
                "BEGIN",
                "END_FUNCTION_BLOCK",
                "FUNCTION_BLOCK FB_Unclosed",
                "BEGIN");
            Assert.True(parsed.HasErrors, ParserCodes(parsed));
            Assert.Contains(parsed.Items, item => item.Name == "FB_Valid");

            var plan = SclImportApplicationLogic.BuildPlan(project, parsed, false);

            Assert.False(plan.CanApply);
            Assert.Contains(plan.Messages, message => message.Code == "SCL_IMPORT_END_MISSING");
            Assert.Throws<InvalidOperationException>(() => SclImportApplicationLogic.Apply(project, plan));
            Assert.Empty(project.Plant.Blocks);
            Assert.Empty(project.Plant.DataTypes);
        }

        [Fact]
        public void ANewUdtDependencyCycleBlocksTheWholeBatch()
        {
            var project = ModelBuilder.Project();
            var parsed = Parse(
                "TYPE LeftType",
                "STRUCT",
                "   RightValue : RightType;",
                "END_STRUCT;",
                "END_TYPE",
                "TYPE RightType",
                "STRUCT",
                "   LeftValue : Array[0..1] of LeftType;",
                "END_STRUCT;",
                "END_TYPE");

            var plan = SclImportApplicationLogic.BuildPlan(project, parsed, false);

            Assert.False(plan.CanApply);
            Assert.Contains(plan.Messages, message => message.Code == "SCL_IMPORT_UDT_DEPENDENCY");
            Assert.Throws<InvalidOperationException>(() => SclImportApplicationLogic.Apply(project, plan));
            Assert.Empty(project.Plant.DataTypes);
        }

        [Fact]
        public void SameKindConflictIsSkippedUnlessReplacementWasExplicitlySelected()
        {
            var project = ModelBuilder.Project();
            var existing = ModelBuilder.FunctionBlock("FB_Valve", ModelBuilder.Input("Open"));
            project.Plant.Blocks.Add(existing);
            var parsed = Parse(
                "FUNCTION_BLOCK FB_Valve",
                "VAR_INPUT",
                "   Open : Bool;",
                "END_VAR",
                "VAR_OUTPUT",
                "   Ready : Bool;",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            var plan = SclImportApplicationLogic.BuildPlan(project, parsed, false);

            Assert.False(plan.CanApply);
            Assert.Equal(1, plan.SkipCount);
            Assert.Equal(0, plan.UpdateCount);
            Assert.Throws<InvalidOperationException>(() => SclImportApplicationLogic.Apply(project, plan));
            Assert.Same(existing, Assert.Single(project.Plant.Blocks));
            Assert.Single(existing.Interface);
        }

        [Fact]
        public void ExplicitBlockReplacementPreservesRootAndMatchingMemberIds()
        {
            var project = ModelBuilder.Project();
            var existing = ModelBuilder.FunctionBlock("FB_Valve", ModelBuilder.Input("Open"));
            existing.SclBody = "#Ready := #Open;";
            var blockId = existing.Id;
            var openId = existing.Interface[0].Id;
            project.Plant.Blocks.Add(existing);
            var parsed = Parse(
                "FUNCTION_BLOCK FB_Valve",
                "VERSION : 2.1",
                "VAR_INPUT",
                "   Open : Bool;",
                "END_VAR",
                "VAR_OUTPUT",
                "   Ready : Bool;",
                "END_VAR",
                "BEGIN",
                "   #Ready := #Open;",
                "END_FUNCTION_BLOCK");

            var plan = SclImportApplicationLogic.BuildPlan(project, parsed, true);

            Assert.True(plan.CanApply, Messages(plan));
            Assert.Equal(1, plan.UpdateCount);
            SclImportApplicationLogic.Apply(project, plan);

            var updated = Assert.Single(project.Plant.Blocks);
            Assert.Same(existing, updated);
            Assert.Equal(blockId, updated.Id);
            Assert.Equal(openId, updated.Interface.Single(member => member.Name == "Open").Id);
            Assert.Contains(updated.Interface, member => member.Name == "Ready");
            Assert.Equal("2.1", updated.Version);
            Assert.True(updated.ImportedInterfaceOnly);
            Assert.Equal(string.Empty, updated.SclBody);
        }

        [Fact]
        public void ExplicitUdtReplacementPreservesRootAndMatchingMemberIds()
        {
            var project = ModelBuilder.Project();
            var existing = ModelBuilder.Udt("Payload", new UdtMember("Value", "Int"));
            var typeId = existing.Id;
            var valueId = existing.Members[0].Id;
            project.Plant.DataTypes.Add(existing);
            var parsed = Parse(
                "TYPE Payload",
                "VERSION : 4.2",
                "STRUCT",
                "   Value : DInt;",
                "   Status : Word;",
                "END_STRUCT;",
                "END_TYPE");

            var plan = SclImportApplicationLogic.BuildPlan(project, parsed, true);

            Assert.True(plan.CanApply, Messages(plan));
            SclImportApplicationLogic.Apply(project, plan);

            var updated = Assert.Single(project.Plant.DataTypes);
            Assert.Same(existing, updated);
            Assert.Equal(typeId, updated.Id);
            Assert.Equal(valueId, updated.Members.Single(member => member.Name == "Value").Id);
            Assert.Equal("DInt", updated.Members.Single(member => member.Name == "Value").DataType);
            Assert.Contains(updated.Members, member => member.Name == "Status");
            Assert.Equal("4.2", updated.Version);
        }

        [Fact]
        public void AGlobalDifferentKindCollisionIsRejectedEvenWhenReplacementIsEnabled()
        {
            var project = ModelBuilder.Project();
            project.Plant.DataTypes.Add(ModelBuilder.Udt("SharedName", new UdtMember("Value", "Bool")));
            var parsed = Parse(
                "FUNCTION_BLOCK SharedName",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            var plan = SclImportApplicationLogic.BuildPlan(project, parsed, true);

            Assert.False(plan.CanApply);
            Assert.Contains(plan.Items, item => item.Action == SclLibraryImportAction.Rejected);
            Assert.Throws<InvalidOperationException>(() => SclImportApplicationLogic.Apply(project, plan));
            Assert.Empty(project.Plant.Blocks);
            Assert.Single(project.Plant.DataTypes);
        }

        [Fact]
        public void CompilationPreflightBlocksAReplacementThatWouldAddAnUnwiredRequiredPin()
        {
            var project = ModelBuilder.Project();
            var existing = ModelBuilder.FunctionBlock("FB_Valve");
            project.Plant.Blocks.Add(existing);
            var node = ModelBuilder.PlaceBlock(project.Sheets[0], existing, "V01");
            var parsed = Parse(
                "FUNCTION_BLOCK FB_Valve",
                "VAR_IN_OUT",
                "   Handle : DWord;",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            var plan = SclImportApplicationLogic.BuildPlan(project, parsed, true);

            Assert.False(plan.CanApply);
            Assert.Contains(
                plan.Messages,
                message => message.Code == "SCL_IMPORT_PREFLIGHT_DGM020");
            Assert.Throws<InvalidOperationException>(() => SclImportApplicationLogic.Apply(project, plan));
            Assert.Empty(existing.Interface);
            Assert.Empty(node.Pins);
        }

        [Fact]
        public void APreviewedAdditionCannotBeAppliedTwiceOrOverwriteALaterCollision()
        {
            var project = ModelBuilder.Project();
            var parsed = Parse(
                "FUNCTION_BLOCK FB_Once",
                "BEGIN",
                "END_FUNCTION_BLOCK");
            var plan = SclImportApplicationLogic.BuildPlan(project, parsed, false);
            Assert.True(plan.CanApply, Messages(plan));

            SclImportApplicationLogic.Apply(project, plan);

            Assert.Throws<InvalidOperationException>(() => SclImportApplicationLogic.Apply(project, plan));
            Assert.Single(project.Plant.Blocks);
            Assert.Equal("FB_Once", project.Plant.Blocks[0].Name);
        }

        private static SclLibraryImportResult Parse(params string[] lines)
        {
            return new SclLibrarySourceParser().Parse(string.Join("\r\n", lines));
        }

        private static string ParserCodes(SclLibraryImportResult result)
        {
            return string.Join(", ", result.Diagnostics.Select(item => item.Code));
        }

        private static string Messages(SclLibraryImportPlan plan)
        {
            return string.Join(", ", plan.Messages.Select(item => item.Code + ":" + item.Message));
        }
    }
}
