using System;
using System.Linq;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Diagram.Tests
{
    /// <summary>
    /// Everything the editor lets a user build has to be either valid or
    /// reported with a code the UI can point at. A deformed sheet reaching this
    /// class must never produce an exception, because the validator runs on
    /// every keystroke that changes the diagram.
    /// </summary>
    public sealed class DiagramValidatorTests
    {
        private static DiagramValidationResult Validate(CallSheet sheet)
        {
            return new DiagramValidator().Validate(sheet);
        }

        private static bool HasCode(DiagramValidationResult result, string code)
        {
            return result.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal));
        }

        private static string Codes(DiagramValidationResult result)
        {
            return string.Join(", ", result.Issues.Select(issue => issue.Code));
        }

        [Fact]
        public void ReportsAMissingSheetInsteadOfThrowing()
        {
            var result = Validate(null);

            Assert.True(HasCode(result, "DGM000"));
            Assert.False(result.IsValid);
        }

        [Fact]
        public void AcceptsTheReferenceValveSheet()
        {
            var result = Validate(DemoProjects.Valve().Sheets[0]);

            Assert.True(result.IsValid, Codes(result));
        }

        [Fact]
        public void AcceptsTheReferenceLogicSheet()
        {
            var result = Validate(DemoProjects.LogicTree().Sheets[0]);

            Assert.True(result.IsValid, Codes(result));
        }

        // ---------------------------------------------------------------
        // Deformed shape. Each of these used to be a NullReferenceException.
        // ---------------------------------------------------------------

        [Fact]
        public void ReportsAMissingNodeCollection()
        {
            var sheet = ModelBuilder.Sheet();
            Deformations.NullNodeCollection(sheet);

            var result = Validate(sheet);

            Assert.True(HasCode(result, "DGM060"), Codes(result));
            Assert.False(result.IsValid);
        }

        [Fact]
        public void ReportsAMissingWireCollection()
        {
            var sheet = ModelBuilder.Sheet();
            Deformations.NullWireCollection(sheet);

            Assert.True(HasCode(Validate(sheet), "DGM064"));
        }

        [Fact]
        public void ReportsANullNodeInTheCollection()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            Deformations.InjectNullNode(sheet);

            Assert.True(HasCode(Validate(sheet), "DGM061"));
        }

        [Fact]
        public void ReportsANullWireInTheCollection()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            Deformations.InjectNullWire(sheet);

            Assert.True(HasCode(Validate(sheet), "DGM065"));
        }

        [Fact]
        public void ReportsAMissingPinCollection()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            sheet.Nodes[0].Pins = null;

            Assert.True(HasCode(Validate(sheet), "DGM062"));
        }

        [Fact]
        public void ReportsANullPinInTheCollection()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            sheet.Nodes[0].Pins.Add(null);

            Assert.True(HasCode(Validate(sheet), "DGM063"));
        }

        [Fact]
        public void ReportsAMissingGroupCollection()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            Deformations.NullGroupCollection(sheet);

            Assert.True(HasCode(Validate(sheet), "DGM040"));
        }

        [Fact]
        public void EveryDeformationProducesACodeAndNotAnException()
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
                sheet => sheet.Name = null,
                sheet => sheet.Nodes[0].Id = Guid.Empty,
                sheet => sheet.Wires[0].Id = Guid.Empty
            };

            foreach (var deformation in deformations)
            {
                var sheet = DemoProjects.Valve().Sheets[0];
                deformation(sheet);

                var result = Validate(sheet);

                Assert.False(result.IsValid, "A deformed sheet was reported as valid.");
                Assert.All(result.Issues, issue => Assert.False(string.IsNullOrWhiteSpace(issue.Code)));
                Assert.All(result.Issues, issue => Assert.False(string.IsNullOrWhiteSpace(issue.Message)));
            }
        }

        // ---------------------------------------------------------------
        // Identity
        // ---------------------------------------------------------------

        [Fact]
        public void ReportsAnInvalidSheetName()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            sheet.Name = "FC Call Units";

            Assert.True(HasCode(Validate(sheet), "DGM006"));
        }

        [Fact]
        public void ReportsAnEmptySheetId()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            sheet.Id = Guid.Empty;

            Assert.True(HasCode(Validate(sheet), "DGM007"));
        }

        [Fact]
        public void ReportsDuplicateNodeIds()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            Deformations.DuplicateNodeId(sheet);

            Assert.True(HasCode(Validate(sheet), "DGM001"));
        }

        [Fact]
        public void ReportsDuplicatePinIds()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            Deformations.DuplicatePinId(sheet);

            Assert.True(HasCode(Validate(sheet), "DGM002"));
        }

        [Fact]
        public void ReportsDuplicateWireIds()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            sheet.Wires[1].Id = sheet.Wires[0].Id;

            Assert.True(HasCode(Validate(sheet), "DGM003"));
        }

        [Fact]
        public void ReportsAnIdSharedBetweenANodeAndAWire()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            sheet.Wires[0].Id = sheet.Nodes[0].Id;

            Assert.True(HasCode(Validate(sheet), "DGM017"));
        }

        [Fact]
        public void ReportsAPinThatPointsAtTheWrongOwner()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            Deformations.OrphanPinOwner(DemoProjects.SingleBlockCall(DemoProjects.Valve()), "Open");
            var call = sheet.Nodes.OfType<BlockCallNode>().Single();
            Deformations.OrphanPinOwner(call, "Open");

            Assert.True(HasCode(Validate(sheet), "DGM004"));
        }

        /// <summary>
        /// Cycle detection runs a graph walk that assumes unique ids. Running it
        /// on a sheet whose identity is already broken would report noise, so it
        /// must be skipped until the ids are fixed.
        /// </summary>
        [Fact]
        public void SkipsCycleDetectionWhileIdentityIsStillBroken()
        {
            var sheet = Deformations.DeepCycle(4);
            Deformations.DuplicateNodeId(sheet);

            var result = Validate(sheet);

            Assert.True(HasCode(result, "DGM001"));
            Assert.False(HasCode(result, "DGM030"));
        }

        [Fact]
        public void ReportsACycleOnceIdentityIsSound()
        {
            var result = Validate(Deformations.DeepCycle(4));

            Assert.True(HasCode(result, "DGM030"), Codes(result));
            Assert.Equal(4, result.Issues.Count(issue => issue.Code == "DGM030"));
        }

        // ---------------------------------------------------------------
        // Wires
        // ---------------------------------------------------------------

        [Fact]
        public void ReportsAWireWithAMissingSource()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            Deformations.DanglingWireSource(sheet);

            Assert.True(HasCode(Validate(sheet), "DGM010"));
        }

        [Fact]
        public void ReportsAWireWithAMissingTarget()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            Deformations.DanglingWireTarget(sheet);

            Assert.True(HasCode(Validate(sheet), "DGM011"));
        }

        [Fact]
        public void ReportsAWireWhoseSourcePinBelongsToAnotherNode()
        {
            var project = DemoProjects.Valve();
            var sheet = project.Sheets[0];
            var call = sheet.Nodes.OfType<BlockCallNode>().Single();
            sheet.Wires[0].SourceNodeId = call.Id;

            Assert.True(HasCode(Validate(sheet), "DGM010"));
        }

        [Fact]
        public void ReportsAWireDrawnFromAnInputToAnOutput()
        {
            var project = DemoProjects.Valve();
            var sheet = project.Sheets[0];
            var call = sheet.Nodes.OfType<BlockCallNode>().Single();
            var tag = sheet.Nodes.OfType<TagNode>().First(node => node.TerminalDirection == TerminalDirection.Sink);
            var wire = ModelBuilder.Connect(sheet, ModelBuilder.OnlyPin(tag), ModelBuilder.PinOf(call, "Open"));

            var result = Validate(sheet);

            Assert.True(HasCode(result, "DGM012"), Codes(result));
            Assert.Contains(result.Issues, issue => issue.WireId == wire.Id);
        }

        [Fact]
        public void ReportsATypeMismatchAcrossAWire()
        {
            var project = DemoProjects.Valve();
            var sheet = project.Sheets[0];
            var call = sheet.Nodes.OfType<BlockCallNode>().Single();
            Deformations.StalePinDataType(call, "Open", "Int");

            Assert.True(HasCode(Validate(sheet), "DGM013"));
        }

        [Fact]
        public void ReportsASecondWireIntoTheSameInputPin()
        {
            var project = DemoProjects.Valve();
            var sheet = project.Sheets[0];
            var call = sheet.Nodes.OfType<BlockCallNode>().Single();
            var extraTag = ModelBuilder.Tag("Extra", "Bool", "%I9.0");
            project.Plant.Tags.Add(extraTag);
            var extraNode = ModelBuilder.PlaceTag(sheet, extraTag, TerminalDirection.Source, 0.0, 500.0);
            ModelBuilder.Connect(sheet, ModelBuilder.OnlyPin(extraNode), ModelBuilder.PinOf(call, "Open"));

            Assert.True(HasCode(Validate(sheet), "DGM014"));
        }

        [Fact]
        public void AllowsOneOutputToFanOutToSeveralInputs()
        {
            var project = DemoProjects.Valve();
            var sheet = project.Sheets[0];
            var call = sheet.Nodes.OfType<BlockCallNode>().Single();
            var extraTag = ModelBuilder.Tag("Extra_Sink", "Bool", "%Q9.0");
            project.Plant.Tags.Add(extraTag);
            var extraNode = ModelBuilder.PlaceTag(sheet, extraTag, TerminalDirection.Sink, 900.0, 500.0);
            ModelBuilder.Connect(sheet, ModelBuilder.PinOf(call, "Q"), ModelBuilder.OnlyPin(extraNode));

            var result = Validate(sheet);

            Assert.True(result.IsValid, Codes(result));
        }

        [Fact]
        public void ReportsARequiredInOutPinThatIsNotConnected()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("FB_A", ModelBuilder.InOut("Handle"));
            project.Plant.Blocks.Add(block);
            ModelBuilder.PlaceBlock(project.Sheets[0], block, "A01");

            Assert.True(HasCode(Validate(project.Sheets[0]), "DGM020"));
        }

        [Fact]
        public void ProjectAwareValidationIgnoresAStaleRequiredFlagOnAnFbInput()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("Command"));
            project.Plant.Blocks.Add(block);
            var node = ModelBuilder.PlaceBlock(project.Sheets[0], block, "A01");
            node.Pins.Single().IsRequired = true;

            var result = new DiagramValidator().Validate(project.Plant, project.Sheets[0]);

            Assert.False(HasCode(result, "DGM020"), Codes(result));
        }

        [Fact]
        public void ProjectAwareValidationRequiresAnFcInput()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.Function("FC_A", "Void", ModelBuilder.Input("Command"));
            project.Plant.Blocks.Add(block);
            ModelBuilder.PlaceBlock(project.Sheets[0], block, "A01");

            var result = new DiagramValidator().Validate(project.Plant, project.Sheets[0]);

            Assert.True(HasCode(result, "DGM020"), Codes(result));
        }

        [Fact]
        public void ProjectAwareValidationDoesNotTreatAnFcInputAsInOut()
        {
            var project = ModelBuilder.Project();
            var block = ModelBuilder.Function("FC_A", "Void", ModelBuilder.Input("Command"));
            project.Plant.Blocks.Add(block);
            var call = ModelBuilder.PlaceBlock(project.Sheets[0], block, "A01");
            var constant = ModelBuilder.PlaceConstant(project.Sheets[0], "TRUE", "Bool");
            ModelBuilder.Connect(
                project.Sheets[0],
                ModelBuilder.OnlyPin(constant),
                ModelBuilder.PinOf(call, "Command"));

            var result = new DiagramValidator().Validate(project.Plant, project.Sheets[0]);

            Assert.False(HasCode(result, "DGM015"), Codes(result));
            Assert.False(HasCode(result, "DGM020"), Codes(result));
        }

        [Fact]
        public void ReportsAnInOutPinFedFromSomethingThatIsNotATag()
        {
            var project = ModelBuilder.Project();
            var source = ModelBuilder.FunctionBlock("FB_Source", ModelBuilder.Output("Out"));
            var sink = ModelBuilder.FunctionBlock("FB_Sink", ModelBuilder.InOut("Handle"));
            project.Plant.Blocks.Add(source);
            project.Plant.Blocks.Add(sink);

            var sheet = project.Sheets[0];
            var sourceNode = ModelBuilder.PlaceBlock(sheet, source, "S01", 0.0, 0.0);
            var sinkNode = ModelBuilder.PlaceBlock(sheet, sink, "K01", 200.0, 0.0);
            ModelBuilder.Connect(sheet, sourceNode, "Out", sinkNode, "Handle");

            Assert.True(HasCode(Validate(sheet), "DGM015"));
        }

        [Fact]
        public void ReportsATagWhoseTerminalDirectionContradictsItsPin()
        {
            var project = DemoProjects.Valve();
            var sheet = project.Sheets[0];
            var tag = sheet.Nodes.OfType<TagNode>().First();
            tag.TerminalDirection = tag.TerminalDirection == TerminalDirection.Source
                ? TerminalDirection.Sink
                : TerminalDirection.Source;

            Assert.True(HasCode(Validate(sheet), "DGM005"));
        }

        // ---------------------------------------------------------------
        // Visual groups
        // ---------------------------------------------------------------

        [Fact]
        public void AcceptsAGroupThatFitsInsideTheSheet()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            sheet.Groups.Add(new DiagramGroup("Region", 10.0, 10.0, 400.0, 300.0));

            Assert.True(Validate(sheet).IsValid, Codes(Validate(sheet)));
        }

        [Theory]
        [InlineData(-1.0, 10.0, 100.0, 100.0)]
        [InlineData(10.0, -1.0, 100.0, 100.0)]
        [InlineData(10.0, 10.0, 0.0, 100.0)]
        [InlineData(10.0, 10.0, 100.0, 0.0)]
        [InlineData(10.0, 10.0, -5.0, 100.0)]
        [InlineData(1.0E9, 10.0, 100.0, 100.0)]
        [InlineData(10.0, 10.0, 1.0E9, 100.0)]
        public void ReportsAGroupThatLeavesTheSheet(double x, double y, double width, double height)
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            sheet.Groups.Add(new DiagramGroup("Region", x, y, width, height));

            Assert.True(HasCode(Validate(sheet), "DGM044"));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void ReportsANonFiniteGroupDimension(double value)
        {
            // NaN makes every comparison false, so a bounds check written the
            // obvious way would accept it and the canvas would then fail to lay
            // the group out at all.
            var sheet = DemoProjects.Valve().Sheets[0];
            sheet.Groups.Add(new DiagramGroup("Region", 10.0, 10.0, value, 100.0));

            Assert.True(HasCode(Validate(sheet), "DGM044"));
        }

        [Fact]
        public void ReportsAGroupMemberThatIsNotOnTheSheet()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            var group = new DiagramGroup("Region", 10.0, 10.0, 100.0, 100.0);
            group.MemberNodeIds.Add(Guid.NewGuid());
            sheet.Groups.Add(group);

            Assert.True(HasCode(Validate(sheet), "DGM046"));
        }

        [Fact]
        public void ReportsTheSameNodeListedTwiceInOneGroup()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            var group = new DiagramGroup("Region", 10.0, 10.0, 100.0, 100.0);
            group.MemberNodeIds.Add(sheet.Nodes[0].Id);
            group.MemberNodeIds.Add(sheet.Nodes[0].Id);
            sheet.Groups.Add(group);

            Assert.True(HasCode(Validate(sheet), "DGM045"));
        }

        [Fact]
        public void ReportsANodeOwnedDirectlyByTwoGroups()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            foreach (var title in new[] { "A", "B" })
            {
                var group = new DiagramGroup(title, 10.0, 10.0, 100.0, 100.0);
                group.MemberNodeIds.Add(sheet.Nodes[0].Id);
                sheet.Groups.Add(group);
            }

            Assert.True(HasCode(Validate(sheet), "DGM050"));
        }

        [Fact]
        public void ReportsAGroupThatIsItsOwnParent()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            var group = new DiagramGroup("Region", 10.0, 10.0, 100.0, 100.0);
            group.ParentGroupId = group.Id;
            sheet.Groups.Add(group);

            Assert.True(HasCode(Validate(sheet), "DGM048"));
        }

        [Fact]
        public void ReportsAMissingParentGroup()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            var group = new DiagramGroup("Region", 10.0, 10.0, 100.0, 100.0);
            group.ParentGroupId = Guid.NewGuid();
            sheet.Groups.Add(group);

            Assert.True(HasCode(Validate(sheet), "DGM047"));
        }

        [Fact]
        public void ReportsAParentCycleBetweenGroups()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            var first = new DiagramGroup("A", 10.0, 10.0, 100.0, 100.0);
            var second = new DiagramGroup("B", 10.0, 10.0, 100.0, 100.0);
            var third = new DiagramGroup("C", 10.0, 10.0, 100.0, 100.0);
            first.ParentGroupId = second.Id;
            second.ParentGroupId = third.Id;
            third.ParentGroupId = first.Id;
            sheet.Groups.Add(first);
            sheet.Groups.Add(second);
            sheet.Groups.Add(third);

            var result = Validate(sheet);

            Assert.Equal(3, result.Issues.Count(issue => issue.Code == "DGM049"));
        }

        [Fact]
        public void AGroupHangingOffACycleIsNotItselfReportedAsCyclic()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            var first = new DiagramGroup("A", 10.0, 10.0, 100.0, 100.0);
            var second = new DiagramGroup("B", 10.0, 10.0, 100.0, 100.0);
            var leaf = new DiagramGroup("Leaf", 10.0, 10.0, 100.0, 100.0);
            first.ParentGroupId = second.Id;
            second.ParentGroupId = first.Id;
            leaf.ParentGroupId = first.Id;
            sheet.Groups.Add(first);
            sheet.Groups.Add(second);
            sheet.Groups.Add(leaf);

            var result = Validate(sheet);

            Assert.Equal(2, result.Issues.Count(issue => issue.Code == "DGM049"));
        }

        [Fact]
        public void ReportsANullGroupItem()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            sheet.Groups.Add(null);

            Assert.True(HasCode(Validate(sheet), "DGM041"));
        }

        [Fact]
        public void ReportsDuplicateGroupIds()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            var first = new DiagramGroup("A", 10.0, 10.0, 100.0, 100.0);
            var second = new DiagramGroup("B", 10.0, 10.0, 100.0, 100.0);
            second.Id = first.Id;
            sheet.Groups.Add(first);
            sheet.Groups.Add(second);

            Assert.True(HasCode(Validate(sheet), "DGM043"));
        }

        [Fact]
        public void ReportsEveryRemainingMalformedVisualShape()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            sheet.Zoom = double.NaN;
            sheet.Nodes[0].Pins[0].Id = Guid.Empty;
            sheet.Nodes[0].X = double.PositiveInfinity;
            var group = new DiagramGroup("Broken", 0.0, 0.0, 100.0, 100.0)
            {
                Id = Guid.Empty,
                MemberNodeIds = null
            };
            sheet.Groups.Add(group);

            var result = Validate(sheet);

            Assert.True(HasCode(result, "DGM067"), Codes(result));
            Assert.True(HasCode(result, "DGM009"), Codes(result));
            Assert.True(HasCode(result, "DGM068"), Codes(result));
            Assert.True(HasCode(result, "DGM042"), Codes(result));
            Assert.True(HasCode(result, "DGM051"), Codes(result));
        }

        [Fact]
        public void ReportsTerminalPinsWhoseDirectionContradictsTheirNodes()
        {
            var project = DemoProjects.Valve();
            var sheet = project.Sheets[0];
            var tag = sheet.Nodes.OfType<TagNode>().First();
            tag.Pins[0].Direction = tag.TerminalDirection == TerminalDirection.Source
                ? PinDirection.Input
                : PinDirection.Output;

            var definition = ModelBuilder.DbVariable("DB_Valve", "Data", "Bool");
            var dbVariable = ModelBuilder.PlaceDbVariable(sheet, definition, TerminalDirection.Source);
            dbVariable.Pins[0].Direction = PinDirection.Input;

            var result = Validate(sheet);

            Assert.True(HasCode(result, "DGM005"), Codes(result));
            Assert.True(HasCode(result, "DGM006"), Codes(result));
        }

        [Fact]
        public void ValidationDoesNotMutateTheSheet()
        {
            var sheet = DemoProjects.Valve().Sheets[0];
            var nodeCount = sheet.Nodes.Count;
            var wireCount = sheet.Wires.Count;
            var pinCount = ModelBuilder.AllPins(sheet).Count();

            Validate(sheet);
            Validate(sheet);

            Assert.Equal(nodeCount, sheet.Nodes.Count);
            Assert.Equal(wireCount, sheet.Wires.Count);
            Assert.Equal(pinCount, ModelBuilder.AllPins(sheet).Count());
        }
    }
}
