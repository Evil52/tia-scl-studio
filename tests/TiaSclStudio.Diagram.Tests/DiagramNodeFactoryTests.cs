using System;
using System.Linq;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Diagram.Tests
{
    /// <summary>
    /// The factory turns a library block into the pin contract the canvas and
    /// the generator both rely on. If it produces a pin the generator does not
    /// expect, the sheet looks fine and then refuses to compile.
    /// </summary>
    public sealed class DiagramNodeFactoryTests
    {
        [Fact]
        public void RejectsANullDefinition()
        {
            Assert.Throws<ArgumentNullException>(
                () => DiagramNodeFactory.CreateBlockCall(null, "A", 0.0, 0.0));
            Assert.Throws<ArgumentNullException>(
                () => DiagramNodeFactory.CreateTag(null, TerminalDirection.Source, 0.0, 0.0));
        }

        [Fact]
        public void PlacesInputInOutAndOutputPinsAndSkipsTheRest()
        {
            var block = ModelBuilder.FunctionBlock(
                "FB_A",
                ModelBuilder.Input("In"),
                ModelBuilder.InOut("Handle"),
                ModelBuilder.Output("Out"),
                ModelBuilder.Static("State"),
                ModelBuilder.Temp("Scratch"),
                new InterfaceMember("Limit", "Int", InterfaceSection.Constant));

            var node = DiagramNodeFactory.CreateBlockCall(block, "A01", 0.0, 0.0);

            Assert.Equal(
                new[] { "In", "Handle", "Out" },
                node.Pins.OrderBy(pin => pin.Order).Select(pin => pin.Name).ToArray());
        }

        [Fact]
        public void AnInOutPinIsAnInputAndIsRequired()
        {
            var block = ModelBuilder.FunctionBlock("FB_A", ModelBuilder.InOut("Handle"));

            var pin = DiagramNodeFactory.CreateBlockCall(block, "A01", 0.0, 0.0).Pins.Single();

            Assert.Equal(PinDirection.Input, pin.Direction);
            Assert.True(pin.IsRequired);
        }

        [Fact]
        public void APlainInputPinIsNotRequired()
        {
            var block = ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("In"));

            Assert.False(DiagramNodeFactory.CreateBlockCall(block, "A01", 0.0, 0.0).Pins.Single().IsRequired);
        }

        [Fact]
        public void EveryPinPointsBackAtItsOwnNodeAndInterfaceMember()
        {
            var input = ModelBuilder.Input("In");
            var output = ModelBuilder.Output("Out");
            var block = ModelBuilder.FunctionBlock("FB_A", input, output);

            var node = DiagramNodeFactory.CreateBlockCall(block, "A01", 0.0, 0.0);

            Assert.All(node.Pins, pin => Assert.Equal(node.Id, pin.NodeId));
            Assert.Equal(input.Id, ModelBuilder.PinOf(node, "In").MemberId);
            Assert.Equal(output.Id, ModelBuilder.PinOf(node, "Out").MemberId);
        }

        [Fact]
        public void PinOrderFollowsTheDeclaredInterfaceOrder()
        {
            var block = ModelBuilder.FunctionBlock(
                "FB_A",
                ModelBuilder.Output("Z"),
                ModelBuilder.Input("A"),
                ModelBuilder.Input("M"));

            var node = DiagramNodeFactory.CreateBlockCall(block, "A01", 0.0, 0.0);

            Assert.Equal(
                new[] { "Z", "A", "M" },
                node.Pins.OrderBy(pin => pin.Order).Select(pin => pin.Name).ToArray());
        }

        [Fact]
        public void AddsAReturnPinForANonVoidFunction()
        {
            var block = ModelBuilder.Function("FC_Scale", "Real", ModelBuilder.Input("Raw", "Int"));

            var node = DiagramNodeFactory.CreateBlockCall(block, "S01", 0.0, 0.0);
            var returnPin = node.Pins.Single(pin => pin.Role == PinRole.FunctionReturn);

            Assert.Equal("Return", returnPin.Name);
            Assert.Equal(PinDirection.Output, returnPin.Direction);
            Assert.Equal("Real", returnPin.DataType);
            Assert.Equal(Guid.Empty, returnPin.MemberId);
        }

        [Theory]
        [InlineData("Void")]
        [InlineData("void")]
        [InlineData("VOID")]
        public void OmitsTheReturnPinForAVoidFunction(string returnType)
        {
            var block = ModelBuilder.Function("FC_A", returnType);

            var node = DiagramNodeFactory.CreateBlockCall(block, "A01", 0.0, 0.0);

            Assert.DoesNotContain(node.Pins, pin => pin.Role == PinRole.FunctionReturn);
        }

        [Fact]
        public void OmitsTheReturnPinForAFunctionBlockEvenWithAStaleReturnType()
        {
            var block = ModelBuilder.FunctionBlock("FB_A");
            block.ReturnType = "Real";

            var node = DiagramNodeFactory.CreateBlockCall(block, "A01", 0.0, 0.0);

            Assert.DoesNotContain(node.Pins, pin => pin.Role == PinRole.FunctionReturn);
        }

        [Fact]
        public void ATagSourceGetsAnOutputPinAndASinkGetsAnInput()
        {
            var tag = ModelBuilder.Tag("Tag_A", "Int");

            var source = DiagramNodeFactory.CreateTag(tag, TerminalDirection.Source, 0.0, 0.0);
            var sink = DiagramNodeFactory.CreateTag(tag, TerminalDirection.Sink, 0.0, 0.0);

            Assert.Equal(PinDirection.Output, source.Pins.Single().Direction);
            Assert.Equal(PinDirection.Input, sink.Pins.Single().Direction);
            Assert.Equal("Int", source.Pins.Single().DataType);
        }

        [Fact]
        public void AConstantGetsExactlyOneOutputPin()
        {
            var node = DiagramNodeFactory.CreateConstant("T#3s", "Time", 0.0, 0.0);

            Assert.Equal("T#3s", node.Literal);
            Assert.Equal(PinDirection.Output, node.Pins.Single().Direction);
            Assert.Equal("Time", node.Pins.Single().DataType);
        }

        [Fact]
        public void AConstantToleratesNullTextWithoutThrowing()
        {
            var node = DiagramNodeFactory.CreateConstant(null, null, 0.0, 0.0);

            Assert.Equal(string.Empty, node.Literal);
            Assert.Equal(string.Empty, node.DataType);
        }

        // ---------------------------------------------------------------
        // Logic nodes
        // ---------------------------------------------------------------

        [Theory]
        [InlineData(LogicOperation.And, 3, "AND")]
        [InlineData(LogicOperation.Or, 3, "OR")]
        [InlineData(LogicOperation.Not, 2, "NOT")]
        public void BooleanLogicNodesGetTheirFixedBoolContract(
            LogicOperation operation,
            int expectedPins,
            string expectedTitle)
        {
            var node = DiagramNodeFactory.CreateLogic(operation, "Bool", 0.0, 0.0);

            Assert.Equal(expectedPins, node.Pins.Count);
            Assert.Equal(expectedTitle, node.Title);
            Assert.All(node.Pins, pin => Assert.Equal("Bool", pin.DataType));
            Assert.All(node.Pins, pin => Assert.Equal(PinRole.Logic, pin.Role));
            Assert.Single(node.Pins, pin => pin.Direction == PinDirection.Output);
        }

        [Theory]
        [InlineData(LogicOperation.And)]
        [InlineData(LogicOperation.Or)]
        [InlineData(LogicOperation.Not)]
        public void BooleanLogicRefusesANonBooleanOperandType(LogicOperation operation)
        {
            Assert.Throws<ArgumentException>(
                () => DiagramNodeFactory.CreateLogic(operation, "Int", 0.0, 0.0));
        }

        [Theory]
        [InlineData(LogicOperation.GreaterThan, ">")]
        [InlineData(LogicOperation.LessThan, "<")]
        [InlineData(LogicOperation.Equal, "=")]
        public void AComparisonTakesItsOperandTypeAndAlwaysReturnsBool(
            LogicOperation operation,
            string expectedTitle)
        {
            var node = DiagramNodeFactory.CreateLogic(operation, "Real", 0.0, 0.0);

            Assert.Equal(expectedTitle, node.Title);
            Assert.Equal("Real", ModelBuilder.PinOf(node, "Left").DataType);
            Assert.Equal("Real", ModelBuilder.PinOf(node, "Right").DataType);
            Assert.Equal("Bool", ModelBuilder.PinOf(node, "Result").DataType);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Void")]
        [InlineData("void")]
        public void AComparisonRefusesAnAbsentOperandType(string operandDataType)
        {
            Assert.Throws<ArgumentException>(
                () => DiagramNodeFactory.CreateLogic(LogicOperation.GreaterThan, operandDataType, 0.0, 0.0));
        }

        [Fact]
        public void RefusesAnOperationValueThatIsNotInTheEnum()
        {
            // A project file written by a newer build can carry an operation this
            // one does not know. It has to be refused at the boundary rather
            // than produce a node with no pins at all.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DiagramNodeFactory.CreateLogic((LogicOperation)9999, "Bool", 0.0, 0.0));
        }

        [Fact]
        public void EveryLogicInputPinIsRequiredAndTheOutputIsNot()
        {
            var node = DiagramNodeFactory.CreateLogic(LogicOperation.And, "Bool", 0.0, 0.0);

            Assert.All(
                node.Pins.Where(pin => pin.Direction == PinDirection.Input),
                pin => Assert.True(pin.IsRequired));
            Assert.False(node.Pins.Single(pin => pin.Direction == PinDirection.Output).IsRequired);
        }

        [Fact]
        public void EveryFactoryProducesDistinctPinIdentities()
        {
            var block = ModelBuilder.FunctionBlock("FB_A", ModelBuilder.Input("In"), ModelBuilder.Output("Out"));

            var first = DiagramNodeFactory.CreateBlockCall(block, "A01", 0.0, 0.0);
            var second = DiagramNodeFactory.CreateBlockCall(block, "A02", 0.0, 0.0);

            Assert.NotEqual(first.Id, second.Id);
            Assert.Empty(first.Pins.Select(pin => pin.Id).Intersect(second.Pins.Select(pin => pin.Id)));
        }
    }
}
