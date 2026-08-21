using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;

namespace TiaSclStudio.App
{
    internal sealed class LogicNodeEditResult
    {
        internal LogicNodeEditResult(Guid nodeId, LogicOperation operation, string operandDataType)
        {
            NodeId = nodeId;
            Operation = operation;
            OperandDataType = (operandDataType ?? string.Empty).Trim();
        }

        internal Guid NodeId { get; private set; }

        internal LogicOperation Operation { get; private set; }

        internal string OperandDataType { get; private set; }
    }

    internal static class LogicNodeEditingLogic
    {
        internal static bool IsBooleanOperation(LogicOperation operation)
        {
            return operation == LogicOperation.And ||
                operation == LogicOperation.Or ||
                operation == LogicOperation.Not;
        }

        internal static string GetOperandDataType(LogicNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }

            if (IsBooleanOperation(node.Operation))
            {
                return "Bool";
            }

            var input = (node.Pins ?? new List<Pin>())
                .Where(pin => pin != null && pin.Direction == PinDirection.Input)
                .OrderBy(pin => pin.Order)
                .FirstOrDefault();
            return input == null ? string.Empty : input.DataType ?? string.Empty;
        }

        internal static IList<string> ValidateCandidate(
            DiagramProject project,
            LogicNodeEditResult result)
        {
            var errors = new List<string>();
            if (project == null)
            {
                errors.Add("Проект не загружен.");
                return errors;
            }

            if (result == null)
            {
                errors.Add("Изменения логического узла не заданы.");
                return errors;
            }

            NodeContext context;
            if (!TryFindNode(project, result.NodeId, out context, out var lookupError))
            {
                errors.Add(lookupError);
                return errors;
            }

            LogicNode template;
            try
            {
                template = DiagramNodeFactory.CreateLogic(
                    result.Operation,
                    result.OperandDataType,
                    context.Node.X,
                    context.Node.Y);
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
                return errors;
            }

            try
            {
                ApplyStablePinIdentity(context.Node, template);
                ValidateWireCompatibility(context.Sheet, context.Node, template, errors);
            }
            catch (Exception exception)
            {
                errors.Add("Повреждён контракт пинов логического узла: " + exception.Message);
            }

            return errors;
        }

        internal static void Apply(DiagramProject project, LogicNodeEditResult result)
        {
            var errors = ValidateCandidate(project, result);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }

            NodeContext context;
            string lookupError;
            if (!TryFindNode(project, result.NodeId, out context, out lookupError))
            {
                throw new InvalidOperationException(lookupError);
            }

            var template = DiagramNodeFactory.CreateLogic(
                result.Operation,
                result.OperandDataType,
                context.Node.X,
                context.Node.Y);
            ApplyStablePinIdentity(context.Node, template);
            context.Node.Operation = template.Operation;
            context.Node.Title = template.Title;
            context.Node.Pins = template.Pins;
        }

        private static void ApplyStablePinIdentity(LogicNode existing, LogicNode template)
        {
            foreach (var pin in template.Pins)
            {
                var matches = pin.Direction == PinDirection.Output
                    ? existing.Pins.Where(candidate =>
                        candidate != null &&
                        candidate.Role == PinRole.Logic &&
                        candidate.Direction == PinDirection.Output).ToList()
                    : existing.Pins.Where(candidate =>
                        candidate != null &&
                        candidate.Role == PinRole.Logic &&
                        candidate.Direction == PinDirection.Input &&
                        candidate.Order == pin.Order).ToList();
                if (matches.Count > 1)
                {
                    throw new InvalidOperationException(
                        "найдено несколько пинов для позиции " + pin.Order + ".");
                }

                var oldPin = matches.FirstOrDefault();
                if (oldPin != null)
                {
                    pin.Id = oldPin.Id;
                }

                pin.NodeId = existing.Id;
            }
        }

        private static void ValidateWireCompatibility(
            CallSheet sheet,
            LogicNode existing,
            LogicNode template,
            IList<string> errors)
        {
            var newPins = template.Pins.ToDictionary(pin => pin.Id);
            var allPins = (sheet.Nodes ?? new List<CallNode>())
                .Where(node => node != null)
                .SelectMany(node => node.Pins ?? new List<Pin>())
                .Where(pin => pin != null)
                .GroupBy(pin => pin.Id)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (var oldPin in existing.Pins ?? new List<Pin>())
            {
                var incident = (sheet.Wires ?? new List<Wire>())
                    .Where(wire => wire.SourcePinId == oldPin.Id || wire.TargetPinId == oldPin.Id)
                    .ToList();
                Pin newPin;
                if (!newPins.TryGetValue(oldPin.Id, out newPin))
                {
                    if (incident.Count > 0)
                    {
                        errors.Add("Операция удаляет подключённый пин «" + oldPin.Name + "». Сначала удалите провод.");
                    }

                    continue;
                }

                foreach (var wire in incident)
                {
                    var otherPinId = wire.SourcePinId == oldPin.Id
                        ? wire.TargetPinId
                        : wire.SourcePinId;
                    Pin otherPin;
                    if (!allPins.TryGetValue(otherPinId, out otherPin))
                    {
                        errors.Add("Провод " + wire.Id + " содержит потерянный пин.");
                        continue;
                    }

                    if (!PlcTypeCompatibility.AreExactlyCompatible(newPin.DataType, otherPin.DataType))
                    {
                        errors.Add(
                            "Новый тип пина «" + newPin.Name + "» (" + newPin.DataType +
                            ") несовместим с подключённым типом " + otherPin.DataType + ".");
                    }
                }
            }
        }

        private static bool TryFindNode(
            DiagramProject project,
            Guid nodeId,
            out NodeContext context,
            out string error)
        {
            var matches = (project.Sheets ?? new List<CallSheet>())
                .Where(sheet => sheet != null)
                .SelectMany(sheet => (sheet.Nodes ?? new List<CallNode>())
                    .OfType<LogicNode>()
                    .Where(node => node.Id == nodeId)
                    .Select(node => new NodeContext(sheet, node)))
                .ToList();
            if (matches.Count != 1)
            {
                context = null;
                error = matches.Count == 0
                    ? "Логический узел больше не существует."
                    : "Идентификатор логического узла не уникален.";
                return false;
            }

            context = matches[0];
            error = string.Empty;
            return true;
        }

        private sealed class NodeContext
        {
            internal NodeContext(CallSheet sheet, LogicNode node)
            {
                Sheet = sheet;
                Node = node;
            }

            internal CallSheet Sheet { get; private set; }

            internal LogicNode Node { get; private set; }
        }
    }
}
