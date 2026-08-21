using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;

namespace TiaSclStudio.App
{
    internal static class BlockLibraryEditorLogic
    {
        internal static IList<string> ValidateCandidateUsage(
            DiagramProject project,
            BlockDefinition candidate)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            if (candidate == null)
            {
                throw new ArgumentNullException("candidate");
            }

            var plant = project.Plant ?? new PlantProject();
            var blocks = plant.Blocks ?? new List<BlockDefinition>();
            var dataTypes = plant.DataTypes ?? new List<UdtDefinition>();
            var tags = plant.Tags ?? new List<TagDefinition>();
            var callBlocks = plant.CallBlocks ?? new List<CallBlockDefinition>();
            var sheets = project.Sheets ?? new List<CallSheet>();
            var errors = new List<string>();
            var symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var candidateUnitDbNames = new List<string>();
            var definitions = blocks
                .Where(item => item != null)
                .GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => group.First());

            RegisterNames(
                symbols,
                blocks.Where(item => item != null && item.Id != candidate.Id).Select(item => item.Name),
                "блок");
            RegisterNames(symbols, dataTypes.Select(item => item.Name), "тип данных");
            RegisterNames(symbols, tags.Select(item => item.Name), "тег");
            RegisterNames(symbols, callBlocks.Select(item => item.Name), "блок вызовов");
            RegisterNames(symbols, sheets.Select(item => item.Name), "лист вызовов");

            foreach (var sheet in sheets)
            {
                var nodes = sheet.Nodes ?? new List<CallNode>();
                var futureHasMultiInstance = nodes
                    .OfType<BlockCallNode>()
                    .Any(node =>
                        node.UseMultiInstance &&
                        (node.BlockDefinitionId != candidate.Id || candidate.Kind == BlockKind.FunctionBlock));
                if (futureHasMultiInstance)
                {
                    RegisterName(symbols, sheet.Name + "_DB", "DB листа вызовов");
                }
            }

            foreach (var callBlock in callBlocks.Where(item =>
                item != null && item.Kind == BlockKind.FunctionBlock))
            {
                var dbName = string.IsNullOrWhiteSpace(callBlock.InstanceDbName)
                    ? callBlock.Name + "_DB"
                    : callBlock.InstanceDbName;
                RegisterName(symbols, dbName, "DB блока вызовов");
            }

            foreach (var callBlock in callBlocks.Where(item => item != null))
            {
                foreach (var unit in (callBlock.Units ?? new List<UnitDefinition>())
                    .Where(item => item != null && !item.UseMultiInstance))
                {
                    BlockDefinition definition;
                    if (!definitions.TryGetValue(unit.BlockId, out definition))
                    {
                        definition = blocks.FirstOrDefault(item =>
                            item != null &&
                            string.Equals(item.Name, unit.BlockName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (definition != null && definition.Id == candidate.Id)
                    {
                        if (candidate.Kind == BlockKind.FunctionBlock)
                        {
                            candidateUnitDbNames.Add(unit.Name);
                        }

                        continue;
                    }

                    if (definition != null && definition.Kind == BlockKind.FunctionBlock)
                    {
                        RegisterName(symbols, unit.Name, "instance DB блока вызовов");
                    }
                }
            }

            foreach (var node in sheets
                .SelectMany(sheet => sheet.Nodes ?? new List<CallNode>())
                .OfType<BlockCallNode>())
            {
                if (node.BlockDefinitionId == candidate.Id || node.UseMultiInstance)
                {
                    continue;
                }

                BlockDefinition definition;
                if (definitions.TryGetValue(node.BlockDefinitionId, out definition) &&
                    definition.Kind == BlockKind.FunctionBlock)
                {
                    RegisterName(symbols, node.InstanceName, "instance DB");
                }
            }

            string candidateNameOwner;
            if (symbols.TryGetValue(candidate.Name, out candidateNameOwner))
            {
                errors.Add(
                    "• Имя блока «" + candidate.Name + "» конфликтует с объектом проекта (" +
                    candidateNameOwner + ").");
            }
            else
            {
                RegisterName(symbols, candidate.Name, "изменяемый блок");
            }

            if (candidate.Kind != BlockKind.FunctionBlock)
            {
                return errors.Distinct(StringComparer.Ordinal).ToList();
            }

            foreach (var instanceDbName in candidateUnitDbNames)
            {
                string nameError;
                if (!SclName.TryValidate(instanceDbName, out nameError))
                {
                    errors.Add(
                        "• Некорректное имя instance DB «" + instanceDbName +
                        "» в блоке вызовов.");
                    continue;
                }

                string owner;
                if (symbols.TryGetValue(instanceDbName, out owner))
                {
                    errors.Add(
                        "• Instance DB «" + instanceDbName +
                        "» конфликтует с объектом проекта (" + owner + ").");
                }
                else
                {
                    symbols.Add(instanceDbName, "instance DB изменяемого блока в блоке вызовов");
                }
            }

            foreach (var node in sheets
                .SelectMany(sheet => sheet.Nodes ?? new List<CallNode>())
                .OfType<BlockCallNode>()
                .Where(item => item.BlockDefinitionId == candidate.Id && !item.UseMultiInstance))
            {
                string nameError;
                if (!SclName.TryValidate(node.InstanceName, out nameError))
                {
                    errors.Add("• Некорректное имя экземпляра «" + node.InstanceName + "» для FB.");
                    continue;
                }

                string owner;
                if (symbols.TryGetValue(node.InstanceName, out owner))
                {
                    errors.Add(
                        "• Instance DB «" + node.InstanceName + "» конфликтует с объектом проекта (" +
                        owner + ").");
                }
                else
                {
                    symbols.Add(node.InstanceName, "instance DB изменяемого блока");
                }
            }

            return errors.Distinct(StringComparer.Ordinal).ToList();
        }

        internal static BlockSynchronizationPlan BuildSynchronizationPlan(
            DiagramProject project,
            BlockDefinition candidate)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            if (candidate == null)
            {
                throw new ArgumentNullException("candidate");
            }

            var plan = new BlockSynchronizationPlan();
            foreach (var sheet in project.Sheets ?? new List<CallSheet>())
            {
                var sheetNodes = sheet.Nodes ?? new List<CallNode>();
                var sheetWires = sheet.Wires ?? new List<Wire>();
                var states = new Dictionary<Guid, SynchronizedNodeState>();
                foreach (var node in sheetNodes.OfType<BlockCallNode>()
                    .Where(item => item.BlockDefinitionId == candidate.Id))
                {
                    var state = new SynchronizedNodeState(node, CreateSynchronizedPins(node, candidate));
                    states[node.Id] = state;
                    plan.NodeStates.Add(state);
                }

                if (states.Count == 0)
                {
                    continue;
                }

                var nodes = sheetNodes
                    .GroupBy(node => node.Id)
                    .ToDictionary(group => group.Key, group => group.First());
                foreach (var wire in sheetWires)
                {
                    if (!states.ContainsKey(wire.SourceNodeId) && !states.ContainsKey(wire.TargetNodeId))
                    {
                        continue;
                    }

                    CallNode sourceNode;
                    CallNode targetNode;
                    if (!nodes.TryGetValue(wire.SourceNodeId, out sourceNode) ||
                        !nodes.TryGetValue(wire.TargetNodeId, out targetNode))
                    {
                        plan.WireRemovals.Add(new WireRemoval(sheet, wire));
                        continue;
                    }

                    var sourcePin = ResolveFuturePin(sourceNode, wire.SourcePinId, states);
                    var targetPin = ResolveFuturePin(targetNode, wire.TargetPinId, states);
                    if (sourcePin == null ||
                        targetPin == null ||
                        sourcePin.Direction != PinDirection.Output ||
                        targetPin.Direction != PinDirection.Input ||
                        !PlcTypeCompatibility.AreExactlyCompatible(sourcePin.DataType, targetPin.DataType) ||
                        (targetPin.IsRequired && !(sourceNode is TagNode)))
                    {
                        plan.WireRemovals.Add(new WireRemoval(sheet, wire));
                    }
                }
            }

            return plan;
        }

        internal static void ApplySynchronization(
            BlockDefinition target,
            BlockDefinition candidate,
            BlockSynchronizationPlan plan)
        {
            if (target == null)
            {
                throw new ArgumentNullException("target");
            }

            if (candidate == null)
            {
                throw new ArgumentNullException("candidate");
            }

            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }

            CopyBlockData(target, candidate);
            foreach (var state in plan.NodeStates)
            {
                state.Node.Title = target.Name;
                state.Node.Pins = state.Pins;
                if (target.Kind == BlockKind.Function)
                {
                    state.Node.UseMultiInstance = false;
                }
            }

            foreach (var removal in plan.WireRemovals)
            {
                removal.Sheet.Wires.Remove(removal.Wire);
            }
        }

        private static Pin ResolveFuturePin(
            CallNode node,
            Guid pinId,
            IDictionary<Guid, SynchronizedNodeState> states)
        {
            SynchronizedNodeState state;
            var pins = states.TryGetValue(node.Id, out state) ? state.Pins : node.Pins;
            return pins.FirstOrDefault(pin => pin.Id == pinId);
        }

        private static List<Pin> CreateSynchronizedPins(BlockCallNode node, BlockDefinition candidate)
        {
            var available = new List<Pin>(node.Pins ?? new List<Pin>());
            var synchronized = new List<Pin>();
            var order = 0;
            foreach (var member in (candidate.Interface ?? new List<InterfaceMember>()).Where(IsCallableMember))
            {
                var existing = available.FirstOrDefault(pin =>
                    pin.Role == PinRole.Parameter && pin.MemberId == member.Id);
                if (existing != null)
                {
                    available.Remove(existing);
                }

                synchronized.Add(new Pin
                {
                    Id = existing == null ? Guid.NewGuid() : existing.Id,
                    NodeId = node.Id,
                    MemberId = member.Id,
                    Name = member.Name,
                    Direction = member.Section == InterfaceSection.Output
                        ? PinDirection.Output
                        : PinDirection.Input,
                    Role = PinRole.Parameter,
                    DataType = member.DataType,
                    IsRequired = member.Section == InterfaceSection.InOut,
                    Order = order++
                });
            }

            if (HasFunctionReturn(candidate))
            {
                var existingReturn = available.FirstOrDefault(pin => pin.Role == PinRole.FunctionReturn);
                synchronized.Add(new Pin
                {
                    Id = existingReturn == null ? Guid.NewGuid() : existingReturn.Id,
                    NodeId = node.Id,
                    MemberId = Guid.Empty,
                    Name = "Return",
                    Direction = PinDirection.Output,
                    Role = PinRole.FunctionReturn,
                    DataType = candidate.ReturnType,
                    IsRequired = false,
                    Order = order
                });
            }

            return synchronized;
        }

        private static bool IsCallableMember(InterfaceMember member)
        {
            return member.Section == InterfaceSection.Input ||
                member.Section == InterfaceSection.Output ||
                member.Section == InterfaceSection.InOut;
        }

        private static bool HasFunctionReturn(BlockDefinition definition)
        {
            return definition.Kind == BlockKind.Function &&
                !string.Equals(definition.ReturnType, "Void", StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyBlockData(BlockDefinition target, BlockDefinition source)
        {
            target.Name = source.Name ?? string.Empty;
            target.Kind = source.Kind;
            target.ReturnType = source.ReturnType ?? string.Empty;
            target.Version = source.Version ?? string.Empty;
            target.OptimizedAccess = source.OptimizedAccess;
            target.ImportedInterfaceOnly = source.ImportedInterfaceOnly;
            target.Description = source.Description ?? string.Empty;
            target.SclBody = source.SclBody ?? string.Empty;
            target.Interface = (source.Interface ?? new List<InterfaceMember>())
                .Select(CloneMember)
                .ToList();
        }

        private static InterfaceMember CloneMember(InterfaceMember source)
        {
            return new InterfaceMember
            {
                Id = source.Id,
                Name = source.Name ?? string.Empty,
                DataType = source.DataType ?? string.Empty,
                Section = source.Section,
                InitialValue = source.InitialValue ?? string.Empty,
                Comment = source.Comment ?? string.Empty
            };
        }

        private static void RegisterNames(
            IDictionary<string, string> symbols,
            IEnumerable<string> values,
            string owner)
        {
            foreach (var value in values)
            {
                RegisterName(symbols, value, owner);
            }
        }

        private static void RegisterName(IDictionary<string, string> symbols, string name, string owner)
        {
            if (!string.IsNullOrWhiteSpace(name) && !symbols.ContainsKey(name))
            {
                symbols.Add(name, owner);
            }
        }

        internal sealed class BlockSynchronizationPlan
        {
            internal BlockSynchronizationPlan()
            {
                NodeStates = new List<SynchronizedNodeState>();
                WireRemovals = new List<WireRemoval>();
            }

            internal IList<SynchronizedNodeState> NodeStates { get; private set; }

            internal IList<WireRemoval> WireRemovals { get; private set; }
        }

        internal sealed class SynchronizedNodeState
        {
            internal SynchronizedNodeState(BlockCallNode node, List<Pin> pins)
            {
                Node = node;
                Pins = pins;
            }

            internal BlockCallNode Node { get; private set; }

            internal List<Pin> Pins { get; private set; }
        }

        internal sealed class WireRemoval
        {
            internal WireRemoval(CallSheet sheet, Wire wire)
            {
                Sheet = sheet;
                Wire = wire;
            }

            internal CallSheet Sheet { get; private set; }

            internal Wire Wire { get; private set; }
        }
    }
}
