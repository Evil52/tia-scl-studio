using System;
using System.Linq;
using System.Reflection;
using TiaSclStudio.Core.Importing;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.History;
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
        public void PublicBoundaryRejectsNullInputsAndProjectsWithoutAPlant()
        {
            var parsed = Parse("FUNCTION FC_A : Void", "BEGIN", "END_FUNCTION");
            var project = ModelBuilder.Project();

            Assert.Throws<ArgumentNullException>(() =>
                SclImportApplicationLogic.BuildPlan(null, parsed, false));
            Assert.Throws<ArgumentNullException>(() =>
                SclImportApplicationLogic.BuildPlan(project, null, false));
            project.Plant = null;
            Assert.Throws<ArgumentException>(() =>
                SclImportApplicationLogic.BuildPlan(project, parsed, false));

            Assert.Throws<ArgumentNullException>(() =>
                SclImportApplicationLogic.Apply(null, new SclLibraryImportPlan()));
            Assert.Throws<ArgumentNullException>(() =>
                SclImportApplicationLogic.Apply(ModelBuilder.Project(), null));
        }

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
        public void ImportedFbAcceptsSiemensTimerInstancesOnlyInItsStaticSection()
        {
            var project = ModelBuilder.Project();
            var parsed = Parse(
                "FUNCTION_BLOCK Unit",
                "VAR",
                "   sOffTimeout : TON_TIME;",
                "   sResetPulse : TP_TIME;",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            var plan = SclImportApplicationLogic.BuildPlan(project, parsed, false);

            Assert.True(plan.CanApply, Messages(plan));
            SclImportApplicationLogic.Apply(project, plan);
            var block = Assert.Single(project.Plant.Blocks);
            Assert.Equal("TON_TIME", block.Interface[0].DataType);
            Assert.Equal("TP_TIME", block.Interface[1].DataType);
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
        public void ReplacementPreviewReportsAndRemovesAnObsoleteWiredPin()
        {
            var project = ModelBuilder.Project();
            var existing = ModelBuilder.FunctionBlock("FB_Valve", ModelBuilder.Input("OldInput"));
            project.Plant.Blocks.Add(existing);
            var tag = ModelBuilder.Tag("Source", "Bool", "%I0.0");
            project.Plant.Tags.Add(tag);
            var tagNode = ModelBuilder.PlaceTag(
                project.Sheets[0],
                tag,
                TerminalDirection.Source,
                10,
                10);
            var blockNode = ModelBuilder.PlaceBlock(project.Sheets[0], existing, "Valve01");
            ModelBuilder.Connect(
                project.Sheets[0],
                ModelBuilder.OnlyPin(tagNode),
                ModelBuilder.PinOf(blockNode, "OldInput"));

            var plan = SclImportApplicationLogic.BuildPlan(
                project,
                Parse("FUNCTION_BLOCK FB_Valve", "BEGIN", "END_FUNCTION_BLOCK"),
                true);

            Assert.True(plan.CanApply, Messages(plan));
            Assert.Contains("1", Assert.Single(plan.Items).Detail);
            SclImportApplicationLogic.Apply(project, plan);
            Assert.Empty(project.Sheets[0].Wires);
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

        [Fact]
        public void PreviewRowsExposeEveryKindActionAndMessagePresentation()
        {
            var project = ModelBuilder.Project();
            var existingBlock = ModelBuilder.FunctionBlock("FB_Update");
            project.Plant.Blocks.Add(existingBlock);
            project.Plant.DataTypes.Add(ModelBuilder.Udt("ExistingType"));
            var parsed = Parse(
                "FUNCTION_BLOCK FB_Add",
                "BEGIN",
                "END_FUNCTION_BLOCK",
                "FUNCTION FC_Add : Void",
                "BEGIN",
                "END_FUNCTION",
                "TYPE ExistingType",
                "STRUCT",
                "END_STRUCT;",
                "END_TYPE",
                "FUNCTION_BLOCK FB_Update",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            var withoutReplacement = SclImportApplicationLogic.BuildPlan(project, parsed, false);
            var withReplacement = SclImportApplicationLogic.BuildPlan(project, parsed, true);

            Assert.Contains(withoutReplacement.Items, item =>
                item.Name == "FB_Add" && item.KindText == "FB" && item.ActionText == "Добавить" && item.SourceLine > 0);
            Assert.Contains(withoutReplacement.Items, item =>
                item.Name == "FC_Add" && item.KindText == "FC" && item.ActionText == "Добавить");
            Assert.Contains(withoutReplacement.Items, item =>
                item.Name == "ExistingType" && item.KindText == "UDT" && item.ActionText == "Пропустить");
            Assert.Contains(withReplacement.Items, item =>
                item.Name == "FB_Update" && item.ActionText == "Обновить");

            var rejected = new SclLibraryImportPlanItem(
                withoutReplacement.Items[0].Source,
                SclLibraryImportAction.Rejected,
                null);
            Assert.Equal("Заблокировано", rejected.ActionText);
            Assert.Equal(string.Empty, rejected.Detail);

            Assert.Contains("ОШИБКА · E · строка 7 · broken", new SclLibraryImportPlanMessage(
                SclImportDiagnosticSeverity.Error,
                "E",
                "broken",
                7).DisplayText);
            Assert.Equal(
                "ПРЕДУПРЕЖДЕНИЕ · W · note",
                new SclLibraryImportPlanMessage(
                    SclImportDiagnosticSeverity.Warning,
                    "W",
                    "note",
                    0).DisplayText);
        }

        [Fact]
        public void SameNameBlockKindChangeIsRejected()
        {
            var project = ModelBuilder.Project();
            project.Plant.Blocks.Add(ModelBuilder.FunctionBlock("Shared"));
            var plan = SclImportApplicationLogic.BuildPlan(
                project,
                Parse("FUNCTION Shared : Void", "BEGIN", "END_FUNCTION"),
                true);

            Assert.False(plan.CanApply);
            Assert.Contains(plan.Items, item =>
                item.Name == "Shared" && item.Action == SclLibraryImportAction.Rejected);
        }

        [Fact]
        public void UdtCollisionWithABlockAndExistingUdtSkipAreBothReported()
        {
            var project = ModelBuilder.Project();
            project.Plant.Blocks.Add(ModelBuilder.FunctionBlock("BlockedType"));
            project.Plant.DataTypes.Add(ModelBuilder.Udt("ExistingType"));
            var plan = SclImportApplicationLogic.BuildPlan(
                project,
                Parse(
                    "TYPE BlockedType", "STRUCT", "END_STRUCT;", "END_TYPE",
                    "TYPE ExistingType", "STRUCT", "END_STRUCT;", "END_TYPE"),
                false);

            Assert.Contains(plan.Items, item =>
                item.Name == "BlockedType" && item.Action == SclLibraryImportAction.Rejected);
            Assert.Contains(plan.Items, item =>
                item.Name == "ExistingType" && item.Action == SclLibraryImportAction.Skip);
        }

        [Theory]
        [InlineData("tag")]
        [InlineData("call")]
        [InlineData("sheet")]
        public void GlobalSymbolsBlockAnImportedObject(string owner)
        {
            var project = ModelBuilder.Project();
            const string name = "SharedImportName";
            if (owner == "tag")
            {
                project.Plant.Tags.Add(new TagDefinition { Name = name });
            }
            else if (owner == "call")
            {
                project.Plant.CallBlocks.Add(new CallBlockDefinition { Name = name });
            }
            else
            {
                project.Sheets.Add(new CallSheet { Name = name });
            }

            var plan = SclImportApplicationLogic.BuildPlan(
                project,
                Parse("FUNCTION_BLOCK " + name, "BEGIN", "END_FUNCTION_BLOCK"),
                false);

            Assert.Contains(plan.Items, item => item.Action == SclLibraryImportAction.Rejected);
        }

        [Fact]
        public void GeneratedDbCollisionsAndInvalidExistingInstancesRejectBlocksDuringUsageValidation()
        {
            var additionProject = ModelBuilder.Project();
            additionProject.Plant.CallBlocks.Add(new CallBlockDefinition
            {
                Name = "Calls",
                Kind = BlockKind.FunctionBlock
            });
            var addition = SclImportApplicationLogic.BuildPlan(
                additionProject,
                Parse("FUNCTION_BLOCK Calls_DB", "BEGIN", "END_FUNCTION_BLOCK"),
                false);
            Assert.Contains(addition.Items, item => item.Action == SclLibraryImportAction.Rejected);

            var updateProject = ModelBuilder.Project();
            var existing = ModelBuilder.FunctionBlock("FB_Existing");
            updateProject.Plant.Blocks.Add(existing);
            var node = ModelBuilder.PlaceBlock(updateProject.Sheets[0], existing, "ValidInstance");
            node.InstanceName = "bad-name";
            var update = SclImportApplicationLogic.BuildPlan(
                updateProject,
                Parse("FUNCTION_BLOCK FB_Existing", "BEGIN", "END_FUNCTION_BLOCK"),
                true);
            Assert.Contains(update.Items, item => item.Action == SclLibraryImportAction.Rejected);
        }

        [Fact]
        public void UnresolvedUdtMemberAndFunctionReturnTypesAreBothReported()
        {
            var project = ModelBuilder.Project();
            var plan = SclImportApplicationLogic.BuildPlan(
                project,
                Parse(
                    "TYPE Payload", "STRUCT", "   Value : MissingMemberType;", "END_STRUCT;", "END_TYPE",
                    "FUNCTION FC_BadReturn : MissingReturnType", "BEGIN", "END_FUNCTION"),
                false);

            Assert.False(plan.CanApply);
            Assert.Contains(plan.Messages, item =>
                item.Code == "SCL_IMPORT_TYPE_UNRESOLVED" && item.Message.Contains("Payload.Value"));
            Assert.Contains(plan.Messages, item =>
                item.Code == "SCL_IMPORT_TYPE_UNRESOLVED" && item.Message.Contains("FC_BadReturn.Return"));
        }

        [Fact]
        public void ProjectChangeAfterPreviewCanCreateANewCompilationFailure()
        {
            var project = ModelBuilder.Project();
            var existing = ModelBuilder.FunctionBlock("FB_LateUse");
            project.Plant.Blocks.Add(existing);
            var plan = SclImportApplicationLogic.BuildPlan(
                project,
                Parse(
                    "FUNCTION_BLOCK FB_LateUse",
                    "VAR_IN_OUT",
                    "   RequiredInput : DWord;",
                    "END_VAR",
                    "BEGIN",
                    "END_FUNCTION_BLOCK"),
                true);
            Assert.True(plan.CanApply, Messages(plan));
            ModelBuilder.PlaceBlock(project.Sheets[0], existing, "Late01");

            Assert.Throws<InvalidOperationException>(() =>
                SclImportApplicationLogic.Apply(project, plan));
            Assert.Empty(existing.Interface);
        }

        [Fact]
        public void ModifiedUpdateTargetsInvalidateThePreviewFingerprint()
        {
            var blockProject = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("FB_Changed");
            blockProject.Plant.Blocks.Add(block);
            var blockPlan = SclImportApplicationLogic.BuildPlan(
                blockProject,
                Parse("FUNCTION_BLOCK FB_Changed", "BEGIN", "END_FUNCTION_BLOCK"),
                true);
            block.Interface.Add(ModelBuilder.Input("ChangedAfterPreview"));
            Assert.Throws<InvalidOperationException>(() =>
                SclImportApplicationLogic.Apply(blockProject, blockPlan));

            var udtProject = ModelBuilder.Project();
            var udt = ModelBuilder.Udt("ChangedType", new UdtMember("Value", "Int"));
            udtProject.Plant.DataTypes.Add(udt);
            var udtPlan = SclImportApplicationLogic.BuildPlan(
                udtProject,
                Parse("TYPE ChangedType", "STRUCT", "   Value : Int;", "END_STRUCT;", "END_TYPE"),
                true);
            udt.Members.Add(new UdtMember("ChangedAfterPreview", "Bool"));
            Assert.Throws<InvalidOperationException>(() =>
                SclImportApplicationLogic.Apply(udtProject, udtPlan));
        }

        [Fact]
        public void PreviewedUdtAdditionDetectsALaterUdtCollision()
        {
            var project = ModelBuilder.Project();
            var plan = SclImportApplicationLogic.BuildPlan(
                project,
                Parse("TYPE AddedType", "STRUCT", "END_STRUCT;", "END_TYPE"),
                false);
            Assert.True(plan.CanApply, Messages(plan));
            project.Plant.DataTypes.Add(ModelBuilder.Udt("AddedType"));

            Assert.Throws<InvalidOperationException>(() =>
                SclImportApplicationLogic.Apply(project, plan));
        }

        [Fact]
        public void ApplyInitializesMissingDefinitionCollections()
        {
            var project = ModelBuilder.Project();
            project.Plant.Blocks = null;
            project.Plant.DataTypes = null;
            var plan = SclImportApplicationLogic.BuildPlan(
                project,
                Parse(
                    "TYPE Payload", "STRUCT", "END_STRUCT;", "END_TYPE",
                    "FUNCTION_BLOCK FB_New", "BEGIN", "END_FUNCTION_BLOCK"),
                false);

            Assert.True(plan.CanApply, Messages(plan));
            SclImportApplicationLogic.Apply(project, plan);

            Assert.Single(project.Plant.DataTypes);
            Assert.Single(project.Plant.Blocks);
        }

        [Fact]
        public void ClonePreflightDefencesRejectMissingTargetsAndNullSignaturesAreEmpty()
        {
            var applyClone = typeof(SclImportApplicationLogic).GetMethod(
                "ApplyDefinitionsToClone",
                BindingFlags.NonPublic | BindingFlags.Static);
            var blockSignature = typeof(SclImportApplicationLogic).GetMethod(
                "BlockSignature",
                BindingFlags.NonPublic | BindingFlags.Static);
            var dataTypeSignature = typeof(SclImportApplicationLogic).GetMethod(
                "DataTypeSignature",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.Equal(string.Empty, blockSignature.Invoke(null, new object[] { null }));
            Assert.Equal(string.Empty, dataTypeSignature.Invoke(null, new object[] { null }));

            var udtProject = ModelBuilder.Project();
            udtProject.Plant.DataTypes.Add(ModelBuilder.Udt("Payload"));
            var udtPlan = SclImportApplicationLogic.BuildPlan(
                udtProject,
                Parse("TYPE Payload", "STRUCT", "END_STRUCT;", "END_TYPE"),
                true);
            var udtClone = DiagramProjectSnapshot.Clone(udtProject);
            udtClone.Plant.DataTypes.Clear();
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() =>
                applyClone.Invoke(null, new object[] { udtClone, udtPlan })).InnerException);

            var blockProject = ModelBuilder.Project();
            blockProject.Plant.Blocks.Add(ModelBuilder.FunctionBlock("FB_Target"));
            var blockPlan = SclImportApplicationLogic.BuildPlan(
                blockProject,
                Parse("FUNCTION_BLOCK FB_Target", "BEGIN", "END_FUNCTION_BLOCK"),
                true);
            var blockClone = DiagramProjectSnapshot.Clone(blockProject);
            blockClone.Plant.Blocks.Clear();
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() =>
                applyClone.Invoke(null, new object[] { blockClone, blockPlan })).InnerException);
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
