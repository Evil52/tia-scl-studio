using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Diagram.Model;
using Xunit;

namespace TiaSclStudio.App
{
    /// <summary>
    /// Changing the operation of a logic node rebuilds its whole pin contract. The
    /// wires around it are addressed by pin id, so the edit has to keep the ids of
    /// the pins that survive and refuse outright when a connected pin would go away
    /// or change into an incompatible type.
    /// </summary>
    public sealed class LogicNodeEditingLogicTests
    {
        [Theory]
        [InlineData(LogicOperation.And, true)]
        [InlineData(LogicOperation.Or, true)]
        [InlineData(LogicOperation.Not, true)]
        [InlineData(LogicOperation.GreaterThan, false)]
        [InlineData(LogicOperation.LessThan, false)]
        [InlineData(LogicOperation.Equal, false)]
        public void BooleanOperationsAreTheOnesWithAFixedBoolOperand(
            LogicOperation operation,
            bool expected)
        {
            Assert.Equal(expected, LogicNodeEditingLogic.IsBooleanOperation(operation));
        }

        [Fact]
        public void BooleanOperandTypeIsBoolRegardlessOfWhatThePinsSay()
        {
            var node = DiagramNodeFactory.CreateLogic(LogicOperation.And, "Bool", 0.0, 0.0);
            node.Pins.First(pin => pin.Direction == PinDirection.Input).DataType = "Int";

            Assert.Equal("Bool", LogicNodeEditingLogic.GetOperandDataType(node));
        }

        [Fact]
        public void ComparisonOperandTypeComesFromTheFirstInputPin()
        {
            var node = DiagramNodeFactory.CreateLogic(LogicOperation.GreaterThan, "Int", 0.0, 0.0);

            Assert.Equal("Int", LogicNodeEditingLogic.GetOperandDataType(node));
        }

        [Fact]
        public void ComparisonOperandTypeIsEmptyWhenTheNodeHasNoInputPin()
        {
            var node = new LogicNode { Operation = LogicOperation.Equal };

            Assert.Equal(string.Empty, LogicNodeEditingLogic.GetOperandDataType(node));
        }

        [Fact]
        public void OperandTypeOfANullNodeIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() =>
                LogicNodeEditingLogic.GetOperandDataType(null));
        }

        [Fact]
        public void ValidateReportsAMissingProjectOrResultInsteadOfThrowing()
        {
            var result = new LogicNodeEditResult(Guid.NewGuid(), LogicOperation.Or, "Bool");

            Assert.Single(LogicNodeEditingLogic.ValidateCandidate(null, result));
            Assert.Single(LogicNodeEditingLogic.ValidateCandidate(new DiagramProject(), null));
        }

        [Fact]
        public void ValidateReportsANodeThatIsNoLongerInTheProject()
        {
            LogicNode node;
            var project = BuildBooleanProject(out node);

            var errors = LogicNodeEditingLogic.ValidateCandidate(
                project,
                new LogicNodeEditResult(Guid.NewGuid(), LogicOperation.Or, "Bool"));

            Assert.Contains(errors, error => error.Contains("больше не существует"));
        }

        [Fact]
        public void ValidateReportsADuplicatedLogicNodeId()
        {
            LogicNode node;
            var project = BuildBooleanProject(out node);
            var clone = DiagramNodeFactory.CreateLogic(LogicOperation.And, "Bool", 500.0, 500.0);
            clone.Id = node.Id;
            project.Sheets[0].Nodes.Add(clone);

            var errors = LogicNodeEditingLogic.ValidateCandidate(
                project,
                new LogicNodeEditResult(node.Id, LogicOperation.Or, "Bool"));

            Assert.Contains(errors, error => error.Contains("не уникален"));
        }

        [Fact]
        public void ValidateSurfacesTheContractErrorForAnImpossibleOperandType()
        {
            LogicNode node;
            var project = BuildBooleanProject(out node);

            var errors = LogicNodeEditingLogic.ValidateCandidate(
                project,
                new LogicNodeEditResult(node.Id, LogicOperation.And, "Int"));

            Assert.NotEmpty(errors);
        }

        [Fact]
        public void ValidateSurfacesTheContractErrorForAnUndefinedOperation()
        {
            LogicNode node;
            var project = BuildBooleanProject(out node);

            var errors = LogicNodeEditingLogic.ValidateCandidate(
                project,
                new LogicNodeEditResult(node.Id, (LogicOperation)99, "Bool"));

            Assert.NotEmpty(errors);
        }

        [Fact]
        public void SwappingAndForOrKeepsEveryPinIdSoTheWiresStayAttached()
        {
            LogicNode node;
            var project = BuildBooleanProject(out node);
            var sheet = project.Sheets[0];
            var pinIdsBefore = node.Pins.Select(pin => pin.Id).OrderBy(id => id).ToList();
            var wireCountBefore = sheet.Wires.Count;

            LogicNodeEditingLogic.Apply(
                project,
                new LogicNodeEditResult(node.Id, LogicOperation.Or, "Bool"));

            Assert.Equal(LogicOperation.Or, node.Operation);
            Assert.Equal(pinIdsBefore, node.Pins.Select(pin => pin.Id).OrderBy(id => id).ToList());
            Assert.Equal(wireCountBefore, sheet.Wires.Count);
            Assert.All(sheet.Wires, wire =>
            {
                Assert.Contains(sheet.Nodes.SelectMany(item => item.Pins), pin => pin.Id == wire.SourcePinId);
                Assert.Contains(sheet.Nodes.SelectMany(item => item.Pins), pin => pin.Id == wire.TargetPinId);
            });
        }

        [Fact]
        public void EveryRebuiltPinStillPointsAtItsOwnNode()
        {
            LogicNode node;
            var project = BuildBooleanProject(out node);

            LogicNodeEditingLogic.Apply(
                project,
                new LogicNodeEditResult(node.Id, LogicOperation.Or, "Bool"));

            Assert.All(node.Pins, pin => Assert.Equal(node.Id, pin.NodeId));
        }

        /// <summary>
        /// NOT has a single input, so turning a wired two-input AND into a NOT would
        /// leave a wire pointing at a pin that no longer exists.
        /// </summary>
        [Fact]
        public void NarrowingToNotIsRejectedWhileTheSecondInputIsWired()
        {
            LogicNode node;
            var project = BuildBooleanProject(out node);
            var sheet = project.Sheets[0];
            WireABoolSourceInto(sheet, node, 1);

            var errors = LogicNodeEditingLogic.ValidateCandidate(
                project,
                new LogicNodeEditResult(node.Id, LogicOperation.Not, "Bool"));

            Assert.Contains(errors, error => error.Contains("Сначала удалите провод"));
            Assert.Throws<InvalidOperationException>(() => LogicNodeEditingLogic.Apply(
                project,
                new LogicNodeEditResult(node.Id, LogicOperation.Not, "Bool")));
            Assert.Equal(LogicOperation.And, node.Operation);
            Assert.Equal(3, node.Pins.Count);
        }

        [Fact]
        public void NarrowingToNotIsAllowedOnceTheSecondInputIsFree()
        {
            LogicNode node;
            var project = BuildBooleanProject(out node);

            LogicNodeEditingLogic.Apply(
                project,
                new LogicNodeEditResult(node.Id, LogicOperation.Not, "Bool"));

            Assert.Equal(LogicOperation.Not, node.Operation);
            Assert.Equal(2, node.Pins.Count);
            Assert.Single(node.Pins, pin => pin.Direction == PinDirection.Input);
        }

        /// <summary>
        /// Retyping a comparison rewrites the data type of pins that keep their id, so
        /// a wire that was type-checked against the old type has to be re-checked.
        /// </summary>
        [Fact]
        public void RetypingAComparisonIsRejectedWhenAWiredPinWouldBecomeIncompatible()
        {
            LogicNode node;
            var project = BuildComparisonProject(out node, "Int");
            var before = node.Pins.Select(pin => pin.DataType).ToList();

            var errors = LogicNodeEditingLogic.ValidateCandidate(
                project,
                new LogicNodeEditResult(node.Id, LogicOperation.GreaterThan, "Real"));

            Assert.Contains(errors, error => error.Contains("несовместим"));
            Assert.Throws<InvalidOperationException>(() => LogicNodeEditingLogic.Apply(
                project,
                new LogicNodeEditResult(node.Id, LogicOperation.GreaterThan, "Real")));
            Assert.Equal(before, node.Pins.Select(pin => pin.DataType).ToList());
        }

        [Fact]
        public void ChangingOnlyTheComparisonOperatorKeepsTheOperandTypeAndTheWire()
        {
            LogicNode node;
            var project = BuildComparisonProject(out node, "Int");

            LogicNodeEditingLogic.Apply(
                project,
                new LogicNodeEditResult(node.Id, LogicOperation.LessThan, "Int"));

            Assert.Equal(LogicOperation.LessThan, node.Operation);
            Assert.Equal("Int", LogicNodeEditingLogic.GetOperandDataType(node));
            Assert.Single(project.Sheets[0].Wires);
        }

        [Fact]
        public void TheResultTrimsTheOperandTypeItWasGiven()
        {
            var result = new LogicNodeEditResult(Guid.NewGuid(), LogicOperation.Equal, "  Int  ");

            Assert.Equal("Int", result.OperandDataType);
            Assert.Equal(
                string.Empty,
                new LogicNodeEditResult(Guid.NewGuid(), LogicOperation.And, null).OperandDataType);
        }

        private static DiagramProject BuildBooleanProject(out LogicNode node)
        {
            var project = new DiagramProject();
            var sheet = new CallSheet { Name = "FC_Logic", Width = 1180.0, Height = 700.0 };
            project.Sheets.Add(sheet);

            node = DiagramNodeFactory.CreateLogic(LogicOperation.And, "Bool", 300.0, 200.0);
            sheet.Nodes.Add(node);
            WireABoolSourceInto(sheet, node, 0);
            return project;
        }

        private static DiagramProject BuildComparisonProject(out LogicNode node, string operandType)
        {
            var project = new DiagramProject();
            var sheet = new CallSheet { Name = "FC_Logic", Width = 1180.0, Height = 700.0 };
            project.Sheets.Add(sheet);

            node = DiagramNodeFactory.CreateLogic(
                LogicOperation.GreaterThan,
                operandType,
                300.0,
                200.0);
            sheet.Nodes.Add(node);

            var target = node.Pins.Single(pin =>
                pin.Direction == PinDirection.Input && pin.Order == 0);
            var source = new TagNode { TagName = "Measured", DataType = operandType, X = 60.0, Y = 200.0 };
            var sourcePin = new Pin
            {
                NodeId = source.Id,
                Name = "Measured",
                Direction = PinDirection.Output,
                Role = PinRole.Terminal,
                DataType = operandType
            };
            source.Pins.Add(sourcePin);
            sheet.Nodes.Add(source);
            sheet.Wires.Add(new Wire
            {
                SourceNodeId = source.Id,
                SourcePinId = sourcePin.Id,
                TargetNodeId = node.Id,
                TargetPinId = target.Id
            });
            return project;
        }

        private static void WireABoolSourceInto(CallSheet sheet, LogicNode node, int inputOrder)
        {
            var target = node.Pins.Single(pin =>
                pin.Direction == PinDirection.Input && pin.Order == inputOrder);
            var source = new TagNode
            {
                TagName = "Start" + inputOrder,
                DataType = "Bool",
                X = 60.0,
                Y = 100.0 + inputOrder * 80.0
            };
            var sourcePin = new Pin
            {
                NodeId = source.Id,
                Name = source.TagName,
                Direction = PinDirection.Output,
                Role = PinRole.Terminal,
                DataType = "Bool"
            };
            source.Pins.Add(sourcePin);
            sheet.Nodes.Add(source);
            sheet.Wires.Add(new Wire
            {
                SourceNodeId = source.Id,
                SourcePinId = sourcePin.Id,
                TargetNodeId = node.Id,
                TargetPinId = target.Id
            });
        }
    }
}
