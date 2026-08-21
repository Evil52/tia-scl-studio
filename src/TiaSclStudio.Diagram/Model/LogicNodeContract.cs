using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Diagram.Validation;

namespace TiaSclStudio.Diagram.Model
{
    internal sealed class LogicPinContract
    {
        public LogicPinContract(
            string name,
            PinDirection direction,
            string dataType,
            bool isRequired,
            int order)
        {
            Name = name;
            Direction = direction;
            DataType = dataType;
            IsRequired = isRequired;
            Order = order;
        }

        public string Name { get; private set; }

        public PinDirection Direction { get; private set; }

        public string DataType { get; private set; }

        public bool IsRequired { get; private set; }

        public int Order { get; private set; }
    }

    internal static class LogicNodeContract
    {
        public static bool TryCreatePinContracts(
            LogicOperation operation,
            string operandDataType,
            out IList<LogicPinContract> contracts,
            out string error)
        {
            contracts = new List<LogicPinContract>();
            error = string.Empty;

            if (!Enum.IsDefined(typeof(LogicOperation), operation))
            {
                error = "Unsupported logic operation '" + operation + "'.";
                return false;
            }

            if (IsBooleanOperation(operation))
            {
                if (!PlcTypeCompatibility.AreExactlyCompatible(operandDataType, "Bool"))
                {
                    error = "AND, OR and NOT require the Bool operand data type.";
                    return false;
                }

                if (operation == LogicOperation.Not)
                {
                    contracts.Add(Input("In", "Bool", 0));
                    contracts.Add(Output(1));
                    return true;
                }

                contracts.Add(Input("In1", "Bool", 0));
                contracts.Add(Input("In2", "Bool", 1));
                contracts.Add(Output(2));
                return true;
            }

            var comparisonType = (operandDataType ?? string.Empty).Trim();
            if (comparisonType.Length == 0 ||
                string.Equals(comparisonType, "Void", StringComparison.OrdinalIgnoreCase))
            {
                error = "A non-Void operand data type is required for a comparison.";
                return false;
            }

            contracts.Add(Input("Left", comparisonType, 0));
            contracts.Add(Input("Right", comparisonType, 1));
            contracts.Add(Output(2));
            return true;
        }

        public static bool TryValidate(
            LogicNode node,
            out IList<LogicPinContract> contracts,
            out string error)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }

            contracts = new List<LogicPinContract>();
            error = string.Empty;

            if (!Enum.IsDefined(typeof(LogicOperation), node.Operation))
            {
                error = "Unsupported logic operation '" + node.Operation + "'.";
                return false;
            }

            string operandDataType;
            if (IsBooleanOperation(node.Operation))
            {
                operandDataType = "Bool";
            }
            else
            {
                var leftPins = (node.Pins ?? new List<Pin>())
                    .Where(pin =>
                        pin != null &&
                        pin.Role == PinRole.Logic &&
                        pin.Direction == PinDirection.Input &&
                        pin.Order == 0 &&
                        string.Equals(pin.Name, "Left", StringComparison.Ordinal))
                    .ToList();
                if (leftPins.Count != 1)
                {
                    error = "Comparison logic nodes must contain exactly one Left input pin at order 0.";
                    return false;
                }

                operandDataType = leftPins[0].DataType;
            }

            if (!TryCreatePinContracts(node.Operation, operandDataType, out contracts, out error))
            {
                return false;
            }

            var pins = node.Pins ?? new List<Pin>();
            if (pins.Count != contracts.Count)
            {
                error = "Logic node pin count does not match operation '" + node.Operation + "'.";
                return false;
            }

            foreach (var contract in contracts)
            {
                var matches = pins.Where(candidate =>
                    candidate != null && candidate.Order == contract.Order).ToList();
                if (matches.Count != 1)
                {
                    error = "Logic node must contain exactly one pin at order " + contract.Order + ".";
                    return false;
                }

                var pin = matches[0];
                if (!string.Equals(pin.Name, contract.Name, StringComparison.Ordinal) ||
                    pin.Direction != contract.Direction ||
                    pin.Role != PinRole.Logic ||
                    pin.MemberId != Guid.Empty ||
                    pin.IsRequired != contract.IsRequired ||
                    !PlcTypeCompatibility.AreExactlyCompatible(pin.DataType, contract.DataType))
                {
                    error = "Logic pin '" + contract.Name + "' has a stale name, role, direction, requirement or data type.";
                    return false;
                }
            }

            return true;
        }

        public static bool TryGetOperator(LogicOperation operation, out string sclOperator)
        {
            switch (operation)
            {
                case LogicOperation.And:
                    sclOperator = "AND";
                    return true;
                case LogicOperation.Or:
                    sclOperator = "OR";
                    return true;
                case LogicOperation.Not:
                    sclOperator = "NOT";
                    return true;
                case LogicOperation.GreaterThan:
                    sclOperator = ">";
                    return true;
                case LogicOperation.LessThan:
                    sclOperator = "<";
                    return true;
                case LogicOperation.Equal:
                    sclOperator = "=";
                    return true;
                default:
                    sclOperator = string.Empty;
                    return false;
            }
        }

        public static string GetTitle(LogicOperation operation)
        {
            string sclOperator;
            if (!TryGetOperator(operation, out sclOperator))
            {
                throw new ArgumentOutOfRangeException("operation");
            }

            return sclOperator;
        }

        private static bool IsBooleanOperation(LogicOperation operation)
        {
            return operation == LogicOperation.And ||
                operation == LogicOperation.Or ||
                operation == LogicOperation.Not;
        }

        private static LogicPinContract Input(string name, string dataType, int order)
        {
            return new LogicPinContract(name, PinDirection.Input, dataType, true, order);
        }

        private static LogicPinContract Output(int order)
        {
            return new LogicPinContract("Result", PinDirection.Output, "Bool", false, order);
        }
    }
}
