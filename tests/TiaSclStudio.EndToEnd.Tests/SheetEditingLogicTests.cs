using System;
using System.Collections.Generic;
using System.Reflection;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Model;
using Xunit;

namespace TiaSclStudio.App
{
    public sealed class SheetEditingLogicTests
    {
        [Fact]
        public void PropertiesEditCommitsNameAndExtentTogether()
        {
            var project = BuildProject();
            var sheet = project.Sheets[0];
            var node = new TagNode
            {
                Title = "Input",
                X = 100.0,
                Y = 100.0
            };
            sheet.Nodes.Add(node);
            var group = new DiagramGroup("Area", 80.0, 80.0, 400.0, 300.0);
            sheet.Groups.Add(group);

            SheetEditingLogic.EditProperties(
                project,
                sheet,
                "FC_Renamed",
                1000.0,
                600.0);

            Assert.Equal("FC_Renamed", sheet.Name);
            Assert.Equal(1000.0, sheet.Width);
            Assert.Equal(600.0, sheet.Height);
            Assert.Equal(100.0, node.X);
            Assert.Equal(100.0, node.Y);
            Assert.Equal(400.0, group.Width);
            Assert.Equal(300.0, group.Height);
        }

        [Fact]
        public void NameCollisionRejectsTheWholePropertiesEdit()
        {
            var project = BuildProject();
            var sheet = project.Sheets[0];
            project.Sheets.Add(new CallSheet { Name = "FC_Taken" });

            Assert.Throws<InvalidOperationException>(() =>
                SheetEditingLogic.EditProperties(
                    project,
                    sheet,
                    "FC_Taken",
                    1000.0,
                    600.0));

            Assert.Equal("FC_Original", sheet.Name);
            Assert.Equal(1200.0, sheet.Width);
            Assert.Equal(800.0, sheet.Height);
        }

        public static IEnumerable<object[]> InvalidExtents()
        {
            yield return new object[] { double.NaN, 600.0 };
            yield return new object[] { 1000.0, double.NegativeInfinity };
            yield return new object[] { 899.0, 600.0 };
            yield return new object[] { 1000.0, 559.0 };
            yield return new object[] { 50001.0, 600.0 };
            yield return new object[] { 1000.0, 50001.0 };
        }

        [Theory]
        [MemberData(nameof(InvalidExtents))]
        public void InvalidExtentRejectsTheWholePropertiesEdit(double width, double height)
        {
            var project = BuildProject();
            var sheet = project.Sheets[0];

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SheetEditingLogic.EditProperties(
                    project,
                    sheet,
                    "FC_Renamed",
                    width,
                    height));

            Assert.Equal("FC_Original", sheet.Name);
            Assert.Equal(1200.0, sheet.Width);
            Assert.Equal(800.0, sheet.Height);
        }

        [Fact]
        public void ShrinkThatClipsAVisualNodeIsRejectedAtomically()
        {
            var project = BuildProject();
            var sheet = project.Sheets[0];
            var block = new BlockCallNode
            {
                Title = "FB_Test",
                X = 650.0,
                Y = 400.0
            };
            for (var index = 0; index < 4; index++)
            {
                block.Pins.Add(new Pin
                {
                    NodeId = block.Id,
                    Name = "In" + index,
                    Direction = PinDirection.Input,
                    DataType = "Bool",
                    Order = index
                });
            }

            sheet.Nodes.Add(block);

            Assert.Throws<InvalidOperationException>(() =>
                SheetEditingLogic.EditProperties(
                    project,
                    sheet,
                    "FC_Renamed",
                    900.0,
                    560.0));

            Assert.Equal("FC_Original", sheet.Name);
            Assert.Equal(1200.0, sheet.Width);
            Assert.Equal(800.0, sheet.Height);
        }

        [Fact]
        public void ShrinkThatClipsAGroupIsRejectedAtomically()
        {
            var project = BuildProject();
            var sheet = project.Sheets[0];
            sheet.Groups.Add(new DiagramGroup("Area", 850.0, 100.0, 300.0, 300.0));

            Assert.Throws<InvalidOperationException>(() =>
                SheetEditingLogic.EditProperties(
                    project,
                    sheet,
                    "FC_Renamed",
                    1000.0,
                    600.0));

            Assert.Equal("FC_Original", sheet.Name);
            Assert.Equal(1200.0, sheet.Width);
            Assert.Equal(800.0, sheet.Height);
        }

        [Fact]
        public void CreateSheetAppendsASheetWithTheDefaultCanvas()
        {
            var project = BuildProject();

            var sheet = SheetEditingLogic.CreateSheet(project, "FC_Second");

            Assert.Same(sheet, project.Sheets[1]);
            Assert.Equal("FC_Second", sheet.Name);
            Assert.Equal(SheetEditingLogic.DefaultSheetWidth, sheet.Width);
            Assert.Equal(SheetEditingLogic.DefaultSheetHeight, sheet.Height);
            Assert.Equal(1.0, sheet.Zoom);
        }

        [Fact]
        public void CreateSheetMaterializesAMissingSheetCollection()
        {
            var project = new DiagramProject { Sheets = null };

            var sheet = SheetEditingLogic.CreateSheet(project, "FC_First");

            Assert.Single(project.Sheets);
            Assert.Same(sheet, project.Sheets[0]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" FC_Leading")]
        [InlineData("1_Digit")]
        [InlineData("FC Space")]
        [InlineData("END_FUNCTION")]
        public void CreateSheetRejectsANameThatIsNotAnSclIdentifier(string name)
        {
            var project = BuildProject();

            Assert.Throws<InvalidOperationException>(() =>
                SheetEditingLogic.CreateSheet(project, name));
            Assert.Single(project.Sheets);
        }

        [Fact]
        public void CreateSheetRejectsANameAlreadyTakenByAnotherSheetIgnoringCase()
        {
            var project = BuildProject();

            Assert.Throws<InvalidOperationException>(() =>
                SheetEditingLogic.CreateSheet(project, "fc_original"));
            Assert.Single(project.Sheets);
        }

        [Fact]
        public void CreateSheetRejectsANullProject()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SheetEditingLogic.CreateSheet(null, "FC_Second"));
        }

        [Fact]
        public void TheDefaultNameSkipsPastEveryNameAlreadyInUse()
        {
            var project = new DiagramProject();

            Assert.Equal("FC_CallUnits", SheetEditingLogic.CreateUniqueDefaultName(project));

            SheetEditingLogic.CreateSheet(project, "FC_CallUnits");
            Assert.Equal("FC_CallUnits_02", SheetEditingLogic.CreateUniqueDefaultName(project));

            SheetEditingLogic.CreateSheet(project, "FC_CallUnits_02");
            Assert.Equal("FC_CallUnits_03", SheetEditingLogic.CreateUniqueDefaultName(project));
        }

        [Fact]
        public void TheDefaultNameAlsoAvoidsNamesOwnedByThePlantLibrary()
        {
            var project = new DiagramProject();
            project.Plant.Blocks.Add(new BlockDefinition { Name = "FC_CallUnits" });

            Assert.Equal("FC_CallUnits_02", SheetEditingLogic.CreateUniqueDefaultName(project));
        }

        [Fact]
        public void RenameSheetKeepsEverythingElseAboutTheSheet()
        {
            var project = BuildProject();
            var sheet = project.Sheets[0];

            SheetEditingLogic.RenameSheet(project, sheet, "FC_Renamed");

            Assert.Equal("FC_Renamed", sheet.Name);
            Assert.Equal(1200.0, sheet.Width);
            Assert.Equal(800.0, sheet.Height);
        }

        [Fact]
        public void RenamingASheetToItsOwnNameIsNotACollision()
        {
            var project = BuildProject();
            var sheet = project.Sheets[0];

            SheetEditingLogic.RenameSheet(project, sheet, "FC_Original");

            Assert.Equal("FC_Original", sheet.Name);
        }

        [Fact]
        public void RenameSheetRejectsASheetThatIsNotInTheProject()
        {
            var project = BuildProject();
            var foreign = new CallSheet { Name = "FC_Foreign" };

            Assert.Throws<InvalidOperationException>(() =>
                SheetEditingLogic.RenameSheet(project, foreign, "FC_Renamed"));
            Assert.Equal("FC_Foreign", foreign.Name);
        }

        [Fact]
        public void RenameSheetRejectsNullArguments()
        {
            var project = BuildProject();

            Assert.Throws<ArgumentNullException>(() =>
                SheetEditingLogic.RenameSheet(null, project.Sheets[0], "FC_Renamed"));
            Assert.Throws<ArgumentNullException>(() =>
                SheetEditingLogic.RenameSheet(project, null, "FC_Renamed"));
        }

        [Fact]
        public void DeletingASheetSelectsTheOneThatTookItsPlace()
        {
            var project = BuildProject();
            var second = SheetEditingLogic.CreateSheet(project, "FC_Second");
            var third = SheetEditingLogic.CreateSheet(project, "FC_Third");

            var selected = SheetEditingLogic.DeleteSheet(project, second);

            Assert.Same(third, selected);
            Assert.Equal(2, project.Sheets.Count);
            Assert.DoesNotContain(second, project.Sheets);
        }

        [Fact]
        public void DeletingTheLastSheetSelectsTheNewLastOne()
        {
            var project = BuildProject();
            var first = project.Sheets[0];
            var second = SheetEditingLogic.CreateSheet(project, "FC_Second");

            var selected = SheetEditingLogic.DeleteSheet(project, second);

            Assert.Same(first, selected);
        }

        [Fact]
        public void TheProjectAlwaysKeepsAtLeastOneSheet()
        {
            var project = BuildProject();

            Assert.Throws<InvalidOperationException>(() =>
                SheetEditingLogic.DeleteSheet(project, project.Sheets[0]));
            Assert.Single(project.Sheets);
        }

        [Fact]
        public void DeleteSheetRejectsASheetThatIsNotInTheProject()
        {
            var project = BuildProject();
            SheetEditingLogic.CreateSheet(project, "FC_Second");

            Assert.Throws<InvalidOperationException>(() =>
                SheetEditingLogic.DeleteSheet(project, new CallSheet { Name = "FC_Foreign" }));
            Assert.Equal(2, project.Sheets.Count);
        }

        [Fact]
        public void ANameOwnedByThePlantLibraryIsReportedWithItsOwner()
        {
            var project = BuildProject();
            project.Plant.Blocks.Add(new BlockDefinition { Name = "FB_Motor" });
            project.Plant.DataTypes.Add(new UdtDefinition { Name = "UDT_Motor" });
            project.Plant.Tags.Add(new TagDefinition { Name = "Start" });
            project.Plant.CallBlocks.Add(new CallBlockDefinition
            {
                Name = "FC_Units",
                Kind = BlockKind.FunctionBlock,
                InstanceDbName = "FC_Units_DB"
            });

            AssertNameIsTaken(project, "FB_Motor");
            AssertNameIsTaken(project, "UDT_Motor");
            AssertNameIsTaken(project, "Start");
            AssertNameIsTaken(project, "FC_Units");
            AssertNameIsTaken(project, "FC_Units_DB");
        }

        [Fact]
        public void AnInstanceDbOfANonMultiInstanceCallIsReserved()
        {
            var project = BuildProject();
            var definition = new BlockDefinition { Name = "FB_Motor", Kind = BlockKind.FunctionBlock };
            project.Plant.Blocks.Add(definition);
            project.Sheets[0].Nodes.Add(new BlockCallNode
            {
                BlockDefinitionId = definition.Id,
                InstanceName = "Motor1",
                Title = "Motor1"
            });

            AssertNameIsTaken(project, "Motor1");
        }

        [Fact]
        public void ANullProjectMakesEveryNameInvalid()
        {
            string error;

            Assert.False(SheetEditingLogic.TryValidateName(null, null, "FC_Anything", out error));
            Assert.NotEmpty(error);
        }

        /// <summary>
        /// A sheet that holds a multi-instance call is generated as an FB with its own
        /// instance DB, so the name has to leave "&lt;name&gt;_DB" free as well.
        /// </summary>
        [Fact]
        public void ASheetWithAMultiInstanceCallAlsoReservesItsDbName()
        {
            var project = BuildProject();
            var sheet = project.Sheets[0];
            var definition = new BlockDefinition { Name = "FB_Motor", Kind = BlockKind.FunctionBlock };
            project.Plant.Blocks.Add(definition);
            sheet.Nodes.Add(new BlockCallNode
            {
                BlockDefinitionId = definition.Id,
                InstanceName = "Motor1",
                Title = "Motor1",
                UseMultiInstance = true
            });
            project.Plant.Tags.Add(new TagDefinition { Name = "FC_Units_DB" });

            string error;
            Assert.False(SheetEditingLogic.TryValidateName(project, sheet, "FC_Units", out error));
            Assert.Contains("_DB", error, StringComparison.Ordinal);
            Assert.True(SheetEditingLogic.TryValidateName(project, sheet, "FC_Free", out error));
        }

        [Fact]
        public void ASheetWithoutAMultiInstanceCallDoesNotReserveADbName()
        {
            var project = BuildProject();
            var sheet = project.Sheets[0];
            project.Plant.Tags.Add(new TagDefinition { Name = "FC_Units_DB" });

            string error;

            Assert.True(SheetEditingLogic.TryValidateName(project, sheet, "FC_Units", out error));
        }

        [Fact]
        public void AnotherSheetWithAMultiInstanceCallReservesItsOwnDbName()
        {
            var project = BuildProject();
            var definition = new BlockDefinition { Name = "FB_Motor", Kind = BlockKind.FunctionBlock };
            project.Plant.Blocks.Add(definition);
            var other = SheetEditingLogic.CreateSheet(project, "FC_Other");
            other.Nodes.Add(new BlockCallNode
            {
                BlockDefinitionId = definition.Id,
                InstanceName = "Motor1",
                Title = "Motor1",
                UseMultiInstance = true
            });

            AssertNameIsTaken(project, "FC_Other_DB");
        }

        [Fact]
        public void AnExtentThatAlreadyFitsIsLeftExactlyAsItIs()
        {
            Assert.Equal(1180.0, SheetEditingLogic.GrowExtentToFit(1180.0, 900.0));
            Assert.Equal(1180.0, SheetEditingLogic.GrowExtentToFit(1180.0, 1180.0));
        }

        [Fact]
        public void AnExtentThatHasToGrowIsRoundedUpToAWholeStep()
        {
            Assert.Equal(1280.0, SheetEditingLogic.GrowExtentToFit(1180.0, 1181.0));
            Assert.Equal(1280.0, SheetEditingLogic.GrowExtentToFit(1180.0, 1280.0));
            Assert.Equal(1536.0, SheetEditingLogic.GrowExtentToFit(1180.0, 1281.0));
        }

        [Fact]
        public void GrowthIsCappedAtTheSafeMaximumExtent()
        {
            Assert.Equal(
                SheetEditingLogic.MaximumSheetExtent,
                SheetEditingLogic.GrowExtentToFit(1180.0, SheetEditingLogic.MaximumSheetExtent));
        }

        public static IEnumerable<object[]> UnusableExtents()
        {
            yield return new object[] { double.NaN, 1000.0 };
            yield return new object[] { 1180.0, double.PositiveInfinity };
            yield return new object[] { 0.0, 1000.0 };
            yield return new object[] { -1.0, 1000.0 };
            yield return new object[] { 50001.0, 1000.0 };
            yield return new object[] { 1180.0, -1.0 };
            yield return new object[] { 1180.0, 50001.0 };
        }

        [Theory]
        [MemberData(nameof(UnusableExtents))]
        public void AnUnusableExtentIsRefusedRatherThanClamped(double current, double required)
        {
            Assert.Throws<InvalidOperationException>(() =>
                SheetEditingLogic.GrowExtentToFit(current, required));
        }

        [Fact]
        public void EachNodeKindHasItsOwnVisualWidth()
        {
            Assert.Equal(272.0, SheetEditingLogic.GetNodeWidth(new BlockCallNode()));
            Assert.Equal(128.0, SheetEditingLogic.GetNodeWidth(new ConstantNode()));
            Assert.Equal(154.0, SheetEditingLogic.GetNodeWidth(new TagNode()));
            Assert.Equal(224.0, SheetEditingLogic.GetNodeWidth(new LogicNode()));
            Assert.Equal(238.0, SheetEditingLogic.GetNodeWidth(new NoteNode()));
        }

        [Fact]
        public void TheHeightOfACallGrowsWithTheBusierSideOfItsPins()
        {
            var node = new BlockCallNode();

            Assert.Equal(72.0, SheetEditingLogic.GetNodeHeight(node));

            AddPins(node, PinDirection.Input, 3);
            AddPins(node, PinDirection.Output, 1);

            Assert.Equal(72.0 + 3 * 32.0, SheetEditingLogic.GetNodeHeight(node));
        }

        [Fact]
        public void TheHeightOfALogicNodeGrowsWithTheBusierSideOfItsPins()
        {
            var node = new LogicNode();
            AddPins(node, PinDirection.Input, 2);
            AddPins(node, PinDirection.Output, 1);

            Assert.Equal(57.0 + 2 * 32.0, SheetEditingLogic.GetNodeHeight(node));
        }

        [Fact]
        public void PinlessNodeKindsHaveAFixedHeight()
        {
            Assert.Equal(112.0, SheetEditingLogic.GetNodeHeight(new NoteNode()));
            Assert.Equal(44.0, SheetEditingLogic.GetNodeHeight(new TagNode()));
            Assert.Equal(44.0, SheetEditingLogic.GetNodeHeight(new ConstantNode()));
        }

        [Fact]
        public void MeasuringANullNodeIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => SheetEditingLogic.GetNodeWidth(null));
            Assert.Throws<ArgumentNullException>(() => SheetEditingLogic.GetNodeHeight(null));
        }

        [Fact]
        public void RenameRejectsAnInvalidIdentifierWithoutChangingTheSheet()
        {
            var project = BuildProject();
            var sheet = project.Sheets[0];

            Assert.Throws<InvalidOperationException>(() =>
                SheetEditingLogic.RenameSheet(project, sheet, "bad-name"));

            Assert.Equal("FC_Original", sheet.Name);
        }

        [Fact]
        public void UniqueDefaultNameRejectsANullProject()
        {
            Assert.Equal("project", Assert.Throws<ArgumentNullException>(() =>
                SheetEditingLogic.CreateUniqueDefaultName(null)).ParamName);
        }

        [Fact]
        public void MultiInstanceSheetNameAlsoValidatesTheGeneratedDbIdentifier()
        {
            var project = BuildProject();
            var block = new BlockDefinition
            {
                Name = "FB_Test",
                Kind = BlockKind.FunctionBlock
            };
            project.Plant.Blocks.Add(block);
            project.Sheets[0].Nodes.Add(new BlockCallNode
            {
                BlockDefinitionId = block.Id,
                UseMultiInstance = true
            });
            var maximumLengthName = "F" + new string('x', SclName.MaximumLength - 1);
            string error;

            Assert.False(SheetEditingLogic.TryValidateName(
                project,
                project.Sheets[0],
                maximumLengthName,
                out error));
            Assert.Contains("DB", error, StringComparison.Ordinal);
        }

        [Fact]
        public void ReservedSymbolsResolveLegacyCallUnitsByBlockName()
        {
            var project = BuildProject();
            var block = new BlockDefinition
            {
                Name = "FB_Legacy",
                Kind = BlockKind.FunctionBlock
            };
            project.Plant.Blocks.Add(null);
            project.Plant.Blocks.Add(block);
            var calls = new CallBlockDefinition { Name = "FC_LegacyCalls" };
            calls.Units.Add(null);
            calls.Units.Add(new UnitDefinition
            {
                Name = "IgnoredMulti",
                BlockId = block.Id,
                BlockName = block.Name,
                UseMultiInstance = true
            });
            calls.Units.Add(new UnitDefinition
            {
                Name = "LegacyInstanceDb",
                BlockId = Guid.NewGuid(),
                BlockName = block.Name
            });
            project.Plant.CallBlocks.Add(calls);

            AssertNameIsTaken(project, "LegacyInstanceDb");
        }

        [Fact]
        public void EveryNodeKindIncludingDefensiveFallbackHasAWidth()
        {
            Assert.Equal(210.0, SheetEditingLogic.GetNodeWidth(new DataBlockVariableNode()));
            Assert.Equal(220.0, SheetEditingLogic.GetNodeWidth(new UnknownNode()));
        }

        [Fact]
        public void UnknownSclNameErrorHasADefensiveTranslation()
        {
            var translate = typeof(SheetEditingLogic).GetMethod(
                "TranslateSclNameError",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.Equal(
                "недопустимый SCL-идентификатор.",
                translate.Invoke(null, new object[] { "unexpected validator message" }));
        }

        private static void AssertNameIsTaken(DiagramProject project, string name)
        {
            string error;

            Assert.False(
                SheetEditingLogic.TryValidateName(project, project.Sheets[0], name, out error),
                "'" + name + "' should be reserved.");
            Assert.Contains("занято", error, StringComparison.Ordinal);
        }

        private static void AddPins(CallNode node, PinDirection direction, int count)
        {
            for (var index = 0; index < count; index++)
            {
                node.Pins.Add(new Pin
                {
                    NodeId = node.Id,
                    Name = direction + index.ToString(),
                    Direction = direction,
                    DataType = "Bool",
                    Order = node.Pins.Count
                });
            }
        }

        private static DiagramProject BuildProject()
        {
            var project = new DiagramProject();
            project.Sheets.Add(new CallSheet
            {
                Name = "FC_Original",
                Width = 1200.0,
                Height = 800.0
            });
            return project;
        }

        private sealed class UnknownNode : CallNode
        {
            public override CallNodeKind Kind { get { return (CallNodeKind)999; } }
        }
    }
}
