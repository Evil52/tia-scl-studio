using System;
using System.Collections.Generic;
using TiaSclStudio.Core.Model;

namespace TiaSclStudio.Diagram.Model
{
    public static class DiagramNodeFactory
    {
        public static BlockCallNode CreateBlockCall(
            BlockDefinition definition,
            string instanceName,
            double x,
            double y)
        {
            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            var node = new BlockCallNode
            {
                BlockDefinitionId = definition.Id,
                InstanceName = instanceName ?? string.Empty,
                Title = definition.Name,
                X = x,
                Y = y
            };

            var order = 0;
            foreach (var member in definition.Interface)
            {
                PinDirection direction;
                var include = true;

                switch (member.Section)
                {
                    case InterfaceSection.Input:
                    case InterfaceSection.InOut:
                        direction = PinDirection.Input;
                        break;
                    case InterfaceSection.Output:
                        direction = PinDirection.Output;
                        break;
                    default:
                        direction = PinDirection.Input;
                        include = false;
                        break;
                }

                if (!include)
                {
                    continue;
                }

                node.Pins.Add(new Pin
                {
                    NodeId = node.Id,
                    MemberId = member.Id,
                    Name = member.Name,
                    Direction = direction,
                    Role = PinRole.Parameter,
                    DataType = member.DataType,
                    IsRequired = member.Section == InterfaceSection.InOut,
                    Order = order++
                });
            }

            if (definition.Kind == BlockKind.Function &&
                !string.Equals(definition.ReturnType, "Void", StringComparison.OrdinalIgnoreCase))
            {
                node.Pins.Add(new Pin
                {
                    NodeId = node.Id,
                    Name = "Return",
                    Direction = PinDirection.Output,
                    Role = PinRole.FunctionReturn,
                    DataType = definition.ReturnType,
                    Order = order
                });
            }

            return node;
        }

        public static TagNode CreateTag(
            TagDefinition definition,
            TerminalDirection direction,
            double x,
            double y)
        {
            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            var node = new TagNode
            {
                TagDefinitionId = definition.Id,
                TagName = definition.Name,
                DataType = definition.DataType,
                TerminalDirection = direction,
                Title = definition.Name,
                X = x,
                Y = y
            };

            node.Pins.Add(new Pin
            {
                NodeId = node.Id,
                Name = definition.Name,
                Direction = direction == TerminalDirection.Source
                    ? PinDirection.Output
                    : PinDirection.Input,
                Role = PinRole.Terminal,
                DataType = definition.DataType,
                Order = 0
            });

            return node;
        }

        public static ConstantNode CreateConstant(
            string literal,
            string dataType,
            double x,
            double y)
        {
            var node = new ConstantNode
            {
                Literal = literal ?? string.Empty,
                DataType = dataType ?? string.Empty,
                Title = literal ?? string.Empty,
                X = x,
                Y = y
            };

            node.Pins.Add(new Pin
            {
                NodeId = node.Id,
                Name = "Value",
                Direction = PinDirection.Output,
                Role = PinRole.Terminal,
                DataType = node.DataType,
                Order = 0
            });

            return node;
        }

        /// <summary>
        /// Creates a logic node with its complete, deterministic pin contract.
        /// Boolean operations require operandDataType to be Bool. Comparison
        /// operations require one non-empty, non-Void operand type for both inputs.
        /// </summary>
        public static LogicNode CreateLogic(
            LogicOperation operation,
            string operandDataType,
            double x,
            double y)
        {
            IList<LogicPinContract> contracts;
            string error;
            if (!LogicNodeContract.TryCreatePinContracts(
                operation,
                operandDataType,
                out contracts,
                out error))
            {
                if (!Enum.IsDefined(typeof(LogicOperation), operation))
                {
                    throw new ArgumentOutOfRangeException("operation", operation, error);
                }

                throw new ArgumentException(error, "operandDataType");
            }

            var node = new LogicNode
            {
                Operation = operation,
                Title = LogicNodeContract.GetTitle(operation),
                X = x,
                Y = y
            };

            foreach (var contract in contracts)
            {
                node.Pins.Add(new Pin
                {
                    NodeId = node.Id,
                    MemberId = Guid.Empty,
                    Name = contract.Name,
                    Direction = contract.Direction,
                    Role = PinRole.Logic,
                    DataType = contract.DataType,
                    IsRequired = contract.IsRequired,
                    Order = contract.Order
                });
            }

            return node;
        }
    }
}
