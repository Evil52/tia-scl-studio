using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Generation;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Generation;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;
using Xunit;

namespace TiaSclStudio.Diagram.Tests
{
    public sealed class CoverageEdgeCaseTests
    {
        [Fact]
        public void LogicContractRejectsUnknownOperationsAndInvalidOperandTypes()
        {
            IList<LogicPinContract> contracts;
            string error;

            Assert.False(LogicNodeContract.TryCreatePinContracts(
                (LogicOperation)999,
                "Bool",
                out contracts,
                out error));
            Assert.Contains("Unsupported", error);

            Assert.False(LogicNodeContract.TryCreatePinContracts(
                LogicOperation.And,
                "Int",
                out contracts,
                out error));
            Assert.Contains("Bool", error);

            Assert.False(LogicNodeContract.TryCreatePinContracts(
                LogicOperation.Equal,
                "Void",
                out contracts,
                out error));
            Assert.Contains("non-Void", error);
        }

        [Theory]
        [InlineData(LogicOperation.And, "Bool", 3)]
        [InlineData(LogicOperation.Or, "Bool", 3)]
        [InlineData(LogicOperation.Not, "Bool", 2)]
        [InlineData(LogicOperation.GreaterThan, "DInt", 3)]
        [InlineData(LogicOperation.LessThan, "Real", 3)]
        [InlineData(LogicOperation.Equal, "Word", 3)]
        public void EveryLogicOperationProducesAValidContract(
            LogicOperation operation,
            string operandType,
            int expectedCount)
        {
            IList<LogicPinContract> contracts;
            string error;

            Assert.True(LogicNodeContract.TryCreatePinContracts(
                operation,
                operandType,
                out contracts,
                out error), error);
            Assert.Equal(expectedCount, contracts.Count);
            Assert.True(contracts.Take(contracts.Count - 1).All(pin => pin.IsRequired));
            Assert.False(contracts[contracts.Count - 1].IsRequired);
        }

        [Fact]
        public void LogicValidationRejectsNullUnknownAndMissingComparisonPins()
        {
            IList<LogicPinContract> contracts;
            string error;

            Assert.Throws<ArgumentNullException>(
                () => LogicNodeContract.TryValidate(null, out contracts, out error));

            var unknown = new LogicNode { Operation = (LogicOperation)999 };
            Assert.False(LogicNodeContract.TryValidate(unknown, out contracts, out error));
            Assert.Contains("Unsupported", error);

            var comparison = new LogicNode { Operation = LogicOperation.Equal, Pins = null };
            Assert.False(LogicNodeContract.TryValidate(comparison, out contracts, out error));
            Assert.Contains("Left", error);
        }

        [Fact]
        public void LogicValidationRejectsDuplicateLeftWrongCountAndStalePins()
        {
            IList<LogicPinContract> contracts;
            string error;
            var comparison = DiagramNodeFactory.CreateLogic(
                LogicOperation.Equal,
                "Int",
                0.0,
                0.0);
            comparison.Pins.Add(new Pin
            {
                Name = "Left",
                Direction = PinDirection.Input,
                Role = PinRole.Logic,
                DataType = "Int",
                Order = 0
            });

            Assert.False(LogicNodeContract.TryValidate(comparison, out contracts, out error));
            Assert.Contains("exactly one Left", error);

            var boolean = DiagramNodeFactory.CreateLogic(LogicOperation.And, "Bool", 0.0, 0.0);
            boolean.Pins.RemoveAt(boolean.Pins.Count - 1);
            Assert.False(LogicNodeContract.TryValidate(boolean, out contracts, out error));
            Assert.Contains("pin count", error);

            boolean = DiagramNodeFactory.CreateLogic(LogicOperation.And, "Bool", 0.0, 0.0);
            boolean.Pins[1].Order = 0;
            Assert.False(LogicNodeContract.TryValidate(boolean, out contracts, out error));
            Assert.Contains("exactly one pin", error);

            boolean = DiagramNodeFactory.CreateLogic(LogicOperation.And, "Bool", 0.0, 0.0);
            boolean.Pins[0].Name = "OldName";
            Assert.False(LogicNodeContract.TryValidate(boolean, out contracts, out error));
            Assert.Contains("stale", error);
        }

        [Fact]
        public void ComparisonValidationRejectsAnInvalidLeftOperandType()
        {
            IList<LogicPinContract> contracts;
            string error;
            var comparison = DiagramNodeFactory.CreateLogic(
                LogicOperation.Equal,
                "Int",
                0.0,
                0.0);
            comparison.Pins.Single(pin => pin.Name == "Left").DataType = "Void";

            Assert.False(LogicNodeContract.TryValidate(comparison, out contracts, out error));
            Assert.Contains("non-Void", error);
        }

        [Theory]
        [InlineData(LogicOperation.And, "Bool")]
        [InlineData(LogicOperation.Not, "Bool")]
        [InlineData(LogicOperation.GreaterThan, "LReal")]
        public void FactoryLogicNodesPassTheirOwnContract(
            LogicOperation operation,
            string operandType)
        {
            IList<LogicPinContract> contracts;
            string error;
            var node = DiagramNodeFactory.CreateLogic(operation, operandType, 0.0, 0.0);

            Assert.True(LogicNodeContract.TryValidate(node, out contracts, out error), error);
            Assert.Equal(node.Pins.Count, contracts.Count);
        }

        [Fact]
        public void LogicOperatorsExposeEverySupportedTokenAndRejectUnknownValues()
        {
            var expected = new Dictionary<LogicOperation, string>
            {
                { LogicOperation.And, "AND" },
                { LogicOperation.Or, "OR" },
                { LogicOperation.Not, "NOT" },
                { LogicOperation.GreaterThan, ">" },
                { LogicOperation.LessThan, "<" },
                { LogicOperation.Equal, "=" }
            };

            foreach (var pair in expected)
            {
                string token;
                Assert.True(LogicNodeContract.TryGetOperator(pair.Key, out token));
                Assert.Equal(pair.Value, token);
                Assert.Equal(pair.Value, LogicNodeContract.GetTitle(pair.Key));
            }

            string unsupportedToken;
            Assert.False(LogicNodeContract.TryGetOperator(
                (LogicOperation)999,
                out unsupportedToken));
            Assert.Equal(string.Empty, unsupportedToken);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => LogicNodeContract.GetTitle((LogicOperation)999));
        }

        [Fact]
        public void DataBlockVariableFactoryRejectsNullAndCreatesBothTerminalDirections()
        {
            Assert.Throws<ArgumentNullException>(
                () => DiagramNodeFactory.CreateDataBlockVariable(
                    null,
                    TerminalDirection.Source,
                    0.0,
                    0.0));

            var definition = new DataBlockVariableDefinition
            {
                DataBlockName = "ValveDb",
                VariableName = "Valve",
                DataType = "Valve_Data"
            };
            var source = DiagramNodeFactory.CreateDataBlockVariable(
                definition,
                TerminalDirection.Source,
                10.0,
                20.0);
            var sink = DiagramNodeFactory.CreateDataBlockVariable(
                definition,
                TerminalDirection.Sink,
                30.0,
                40.0);

            Assert.Equal(CallNodeKind.DataBlockVariable, source.Kind);
            Assert.Equal("ValveDb.Valve", source.Title);
            Assert.Equal(PinDirection.Output, source.Pins.Single().Direction);
            Assert.Equal(PinDirection.Input, sink.Pins.Single().Direction);
        }

        [Fact]
        public void EveryConcreteNodeReportsItsKind()
        {
            Assert.Equal(CallNodeKind.BlockCall, new BlockCallNode().Kind);
            Assert.Equal(CallNodeKind.Tag, new TagNode().Kind);
            Assert.Equal(CallNodeKind.DataBlockVariable, new DataBlockVariableNode().Kind);
            Assert.Equal(CallNodeKind.Constant, new ConstantNode().Kind);
            Assert.Equal(CallNodeKind.Logic, new LogicNode().Kind);
            Assert.Equal(CallNodeKind.Note, new NoteNode().Kind);
        }

        [Fact]
        public void CompilationResultNormalizesNullsAndExposesAliases()
        {
            var sheet = new DiagramSheetCompilationResult(
                2,
                null,
                null,
                null,
                null);
            var project = new DiagramProjectCompilationResult(
                new[] { sheet },
                null,
                null);

            Assert.Equal(2, sheet.SheetIndex);
            Assert.Equal(string.Empty, sheet.SheetName);
            Assert.Same(sheet.Compilation, sheet.CompilationResult);
            Assert.Equal(string.Empty, sheet.Scl);
            Assert.Empty(sheet.Issues);
            Assert.True(sheet.IsSuccess);
            Assert.Same(project.Sheets, project.SheetResults);
            Assert.Same(project.GeneratedSources, project.Sources);
            Assert.Empty(project.Issues);
            Assert.Empty(project.GeneratedSources);
            Assert.True(project.IsSuccess);
        }

        [Fact]
        public void ProjectErrorsSuppressSourcesAndSheetErrorsFailTheProject()
        {
            var source = new GeneratedSource("FC_A.scl", "FUNCTION FC_A : Void", GeneratedSourceKind.Block, 1);
            var projectError = new DiagramProjectIssue(
                DiagramIssueSeverity.Error,
                null,
                null,
                null,
                null,
                null,
                null);
            var project = new DiagramProjectCompilationResult(
                null,
                new[] { projectError },
                new[] { source });

            Assert.Equal(string.Empty, projectError.Code);
            Assert.Equal(string.Empty, projectError.Message);
            Assert.Equal(string.Empty, projectError.SheetName);
            Assert.Empty(project.GeneratedSources);
            Assert.False(project.IsSuccess);

            var compilation = new DiagramCompilationResult(
                string.Empty,
                new[]
                {
                    new DiagramIssue(DiagramIssueSeverity.Error, "E", "bad", null, null)
                },
                null);
            var sheet = new DiagramSheetCompilationResult(
                0,
                Guid.NewGuid(),
                "Calls",
                compilation,
                null);
            project = new DiagramProjectCompilationResult(
                new[] { sheet },
                null,
                new[] { source });

            Assert.False(sheet.IsSuccess);
            Assert.False(project.IsSuccess);
            Assert.Single(project.GeneratedSources);
        }
    }
}
