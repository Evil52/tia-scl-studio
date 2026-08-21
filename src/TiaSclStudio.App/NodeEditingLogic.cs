using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;

namespace TiaSclStudio.App
{
    /// <summary>
    /// Immutable description of a supported node edit. The object contains values only;
    /// the diagram is changed explicitly by <see cref="NodeEditingLogic.Apply"/>.
    /// </summary>
    public sealed class NodeEditResult
    {
        internal NodeEditResult(
            Guid nodeId,
            CallNodeKind kind,
            Guid blockDefinitionId,
            string instanceName,
            bool useMultiInstance,
            string literal,
            string dataType,
            Guid tagDefinitionId,
            string tagName,
            string tagAddress,
            string tagComment,
            TerminalDirection terminalDirection,
            PlcCpuFamily? targetCpuFamily)
        {
            NodeId = nodeId;
            Kind = kind;
            BlockDefinitionId = blockDefinitionId;
            InstanceName = instanceName ?? string.Empty;
            UseMultiInstance = useMultiInstance;
            Literal = literal ?? string.Empty;
            DataType = dataType ?? string.Empty;
            TagDefinitionId = tagDefinitionId;
            TagName = tagName ?? string.Empty;
            TagAddress = tagAddress ?? string.Empty;
            TagComment = tagComment ?? string.Empty;
            TerminalDirection = terminalDirection;
            TargetCpuFamily = targetCpuFamily;
        }

        public Guid NodeId { get; private set; }

        public CallNodeKind Kind { get; private set; }

        public Guid BlockDefinitionId { get; private set; }

        public string InstanceName { get; private set; }

        public bool UseMultiInstance { get; private set; }

        public string Literal { get; private set; }

        public string DataType { get; private set; }

        public Guid TagDefinitionId { get; private set; }

        public string TagName { get; private set; }

        public string TagAddress { get; private set; }

        public string TagComment { get; private set; }

        public TerminalDirection TerminalDirection { get; private set; }

        public PlcCpuFamily? TargetCpuFamily { get; private set; }
    }

    internal static class NodeEditingLogic
    {
        internal static CallNode CloneSupportedNode(CallNode source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            CallNode clone;
            var block = source as BlockCallNode;
            if (block != null)
            {
                clone = new BlockCallNode
                {
                    BlockDefinitionId = block.BlockDefinitionId,
                    InstanceName = block.InstanceName ?? string.Empty,
                    UseMultiInstance = block.UseMultiInstance
                };
            }
            else
            {
                var constant = source as ConstantNode;
                if (constant != null)
                {
                    clone = new ConstantNode
                    {
                        Literal = constant.Literal ?? string.Empty,
                        DataType = constant.DataType ?? string.Empty
                    };
                }
                else
                {
                    var tag = source as TagNode;
                    if (tag == null)
                    {
                        throw new NotSupportedException(
                            "The node-properties editor supports block calls, constants and tags only.");
                    }

                    clone = new TagNode
                    {
                        TagDefinitionId = tag.TagDefinitionId,
                        TagName = tag.TagName ?? string.Empty,
                        DataType = tag.DataType ?? string.Empty,
                        TerminalDirection = tag.TerminalDirection
                    };
                }
            }

            clone.Id = source.Id;
            clone.Title = source.Title ?? string.Empty;
            clone.X = source.X;
            clone.Y = source.Y;
            clone.ManualOrder = source.ManualOrder;
            clone.Pins = (source.Pins ?? new List<Pin>()).Select(ClonePin).ToList();
            return clone;
        }

        internal static TagDefinition CloneTag(TagDefinition source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            return new TagDefinition
            {
                Id = source.Id,
                Name = source.Name ?? string.Empty,
                DataType = source.DataType ?? string.Empty,
                Address = source.Address ?? string.Empty,
                Comment = source.Comment ?? string.Empty
            };
        }

        internal static NodeEditResult CreateBlockCallResult(
            BlockCallNode source,
            string instanceName,
            bool useMultiInstance)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            return new NodeEditResult(
                source.Id,
                CallNodeKind.BlockCall,
                source.BlockDefinitionId,
                (instanceName ?? string.Empty).Trim(),
                useMultiInstance,
                string.Empty,
                string.Empty,
                Guid.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                default(TerminalDirection),
                null);
        }

        internal static NodeEditResult CreateConstantResult(
            ConstantNode source,
            string literal,
            string dataType)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            return new NodeEditResult(
                source.Id,
                CallNodeKind.Constant,
                Guid.Empty,
                string.Empty,
                false,
                (literal ?? string.Empty).Trim(),
                (dataType ?? string.Empty).Trim(),
                Guid.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                default(TerminalDirection),
                null);
        }

        internal static NodeEditResult CreateTagResult(
            TagNode source,
            TagDefinition tag)
        {
            return CreateTagResult(source, tag, null);
        }

        internal static NodeEditResult CreateTagResult(
            TagNode source,
            TagDefinition tag,
            PlcCpuFamily? targetCpuFamily)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            if (tag == null)
            {
                throw new ArgumentNullException("tag");
            }

            return new NodeEditResult(
                source.Id,
                CallNodeKind.Tag,
                Guid.Empty,
                string.Empty,
                false,
                string.Empty,
                (tag.DataType ?? string.Empty).Trim(),
                source.TagDefinitionId,
                (tag.Name ?? string.Empty).Trim(),
                (tag.Address ?? string.Empty).Trim(),
                tag.Comment ?? string.Empty,
                source.TerminalDirection,
                targetCpuFamily);
        }

        internal static IList<string> ValidateCandidate(
            DiagramProject project,
            NodeEditResult candidate)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            if (candidate == null)
            {
                throw new ArgumentNullException("candidate");
            }

            var errors = new List<string>();
            var contexts = FindNodeContexts(project, candidate.NodeId);
            if (candidate.NodeId == Guid.Empty || contexts.Count != 1)
            {
                errors.Add(
                    contexts.Count > 1
                        ? "• Идентификатор узла встречается на нескольких листах. Редактирование заблокировано."
                        : "• Редактируемый узел больше не найден в проекте.");
                return errors;
            }

            var context = contexts[0];
            if (context.Node.Kind != candidate.Kind)
            {
                errors.Add("• Тип редактируемого узла изменился. Откройте свойства заново.");
                return errors;
            }

            switch (candidate.Kind)
            {
                case CallNodeKind.BlockCall:
                    ValidateBlockCall(project, context, candidate, errors);
                    break;
                case CallNodeKind.Constant:
                    ValidateConstant(project, context, candidate, errors);
                    break;
                case CallNodeKind.Tag:
                    ValidateTag(project, context, candidate, errors);
                    break;
                default:
                    errors.Add("• Этот тип узла пока нельзя редактировать в окне свойств.");
                    break;
            }

            return errors.Distinct(StringComparer.Ordinal).ToList();
        }

        internal static void Apply(DiagramProject project, NodeEditResult result)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            var errors = ValidateCandidate(project, result);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }

            var context = FindNodeContexts(project, result.NodeId).Single();
            var block = context.Node as BlockCallNode;
            if (block != null)
            {
                var definition = FindBlockDefinitions(project, block.BlockDefinitionId).Single();
                block.InstanceName = result.InstanceName;
                block.UseMultiInstance = definition.Kind == BlockKind.FunctionBlock && result.UseMultiInstance;
                return;
            }

            var constant = context.Node as ConstantNode;
            if (constant != null)
            {
                constant.Literal = result.Literal;
                constant.DataType = result.DataType;
                constant.Title = result.Literal;
                constant.Pins[0].DataType = result.DataType;
                return;
            }

            var tagNode = (TagNode)context.Node;
            var tag = FindTagDefinitions(project, result.TagDefinitionId).Single();
            var targetCpuFamily = result.TargetCpuFamily ?? project.Plant.CpuFamily;
            PlcAddressSpec addressSpec;
            string canonicalAddress;
            string addressError;
            if (!PlcAddressing.TryParse(
                targetCpuFamily,
                result.TagAddress,
                result.DataType,
                out addressSpec,
                out canonicalAddress,
                out addressError))
            {
                throw new InvalidOperationException(addressError);
            }

            tag.Name = result.TagName;
            tag.DataType = result.DataType;
            tag.Address = canonicalAddress;
            tag.Comment = result.TagComment;
            if (result.TargetCpuFamily.HasValue)
            {
                project.Plant.CpuFamily = result.TargetCpuFamily.Value;
            }

            foreach (var linkedNode in SafeSheets(project)
                .SelectMany(sheet => SafeNodes(sheet))
                .OfType<TagNode>()
                .Where(node => node.TagDefinitionId == result.TagDefinitionId))
            {
                linkedNode.TagName = result.TagName;
                linkedNode.DataType = result.DataType;
                linkedNode.Title = result.TagName;
                linkedNode.Pins[0].Name = result.TagName;
                linkedNode.Pins[0].DataType = result.DataType;
            }
        }

        private static void ValidateBlockCall(
            DiagramProject project,
            NodeContext context,
            NodeEditResult candidate,
            IList<string> errors)
        {
            var node = context.Node as BlockCallNode;
            if (node == null || node.BlockDefinitionId != candidate.BlockDefinitionId)
            {
                errors.Add("• Ссылка узла на библиотечный блок изменилась. Откройте свойства заново.");
                return;
            }

            var definitions = FindBlockDefinitions(project, candidate.BlockDefinitionId);
            if (definitions.Count != 1)
            {
                errors.Add(
                    definitions.Count == 0
                        ? "• Связанный библиотечный блок не найден."
                        : "• Идентификатор библиотечного блока не уникален.");
                return;
            }

            string nameError;
            if (!SclName.TryValidate(candidate.InstanceName, out nameError))
            {
                errors.Add("• Имя экземпляра: " + TranslateSclNameError(nameError));
            }

            var definition = definitions[0];
            if (definition.Kind == BlockKind.Function && candidate.UseMultiInstance)
            {
                errors.Add("• FC не поддерживает multi-instance. Этот режим доступен только для FB.");
            }

            var duplicate = SafeNodes(context.Sheet)
                .OfType<BlockCallNode>()
                .FirstOrDefault(other =>
                    other.Id != candidate.NodeId &&
                    string.Equals(
                        (other.InstanceName ?? string.Empty).Trim(),
                        candidate.InstanceName,
                        StringComparison.OrdinalIgnoreCase));
            if (duplicate != null)
            {
                errors.Add(
                    "• Имя экземпляра «" + candidate.InstanceName +
                    "» уже используется другим вызовом на этом листе.");
            }

            var effectiveMultiInstance =
                definition.Kind == BlockKind.FunctionBlock && candidate.UseMultiInstance;
            if (definition.Kind == BlockKind.FunctionBlock && !effectiveMultiInstance)
            {
                var owner = FindGlobalSymbolOwner(
                    project,
                    candidate.InstanceName,
                    candidate,
                    candidate.NodeId,
                    null,
                    null);
                if (owner != null)
                {
                    errors.Add(
                        "• Instance DB «" + candidate.InstanceName +
                        "» конфликтует с объектом проекта (" + owner + ").");
                }
            }

            if (SheetHasMultiInstance(project, context.Sheet, candidate))
            {
                var dbName = (context.Sheet.Name ?? string.Empty) + "_DB";
                string dbNameError;
                if (!SclName.TryValidate(dbName, out dbNameError))
                {
                    errors.Add("• Имя DB листа «" + dbName + "»: " + TranslateSclNameError(dbNameError));
                }
                else
                {
                    var dbOwner = FindGlobalSymbolOwner(
                        project,
                        dbName,
                        candidate,
                        null,
                        null,
                        context.Sheet.Id);
                    if (dbOwner != null)
                    {
                        errors.Add(
                            "• DB листа «" + dbName +
                            "» конфликтует с объектом проекта (" + dbOwner + ").");
                    }
                }
            }
        }

        private static void ValidateConstant(
            DiagramProject project,
            NodeContext context,
            NodeEditResult candidate,
            IList<string> errors)
        {
            var node = context.Node as ConstantNode;
            if (node == null)
            {
                errors.Add("• Редактируемый узел больше не является константой.");
                return;
            }

            if (string.IsNullOrWhiteSpace(candidate.Literal))
            {
                errors.Add("• Значение константы не должно быть пустым.");
            }
            else if (!NodeCreationLogic.IsSafeConstantLiteral(candidate.Literal))
            {
                errors.Add(
                    "• Литерал должен быть однострочным печатным SCL-фрагментом без ';' и маркеров комментариев.");
            }

            if (string.IsNullOrWhiteSpace(candidate.DataType))
            {
                errors.Add("• Укажите тип данных константы.");
            }
            else if (string.Equals(candidate.DataType.Trim(), "Void", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("• Тип Void нельзя использовать для константы.");
            }

            var pin = ValidateSingleTerminalPin(node, PinDirection.Output, errors);
            if (pin != null && !string.IsNullOrWhiteSpace(candidate.DataType))
            {
                ValidateAffectedPinIdentity(project, new[] { pin.Id }, errors);
                ValidateAffectedWireTypes(
                    project,
                    new[] { pin.Id },
                    candidate.DataType,
                    errors);
            }
        }

        private static void ValidateTag(
            DiagramProject project,
            NodeContext context,
            NodeEditResult candidate,
            IList<string> errors)
        {
            var sourceNode = context.Node as TagNode;
            if (!Enum.IsDefined(typeof(TerminalDirection), candidate.TerminalDirection))
            {
                errors.Add("• Направление PLC-терминала не поддерживается.");
                return;
            }

            if (sourceNode == null ||
                sourceNode.TagDefinitionId != candidate.TagDefinitionId ||
                sourceNode.TerminalDirection != candidate.TerminalDirection)
            {
                errors.Add("• Ссылка или направление терминала изменились. Откройте свойства заново.");
                return;
            }

            var definitions = FindTagDefinitions(project, candidate.TagDefinitionId);
            if (definitions.Count != 1)
            {
                errors.Add(
                    definitions.Count == 0
                        ? "• Связанный PLC-тег не найден."
                        : "• Идентификатор PLC-тега не уникален.");
                return;
            }

            string nameError;
            if (!SclName.TryValidate(candidate.TagName, out nameError))
            {
                errors.Add("• Имя PLC-тега: " + TranslateSclNameError(nameError));
            }

            if (string.IsNullOrWhiteSpace(candidate.DataType))
            {
                errors.Add("• Укажите тип данных PLC-тега.");
            }
            else if (string.Equals(candidate.DataType.Trim(), "Void", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("• Тип Void нельзя использовать для PLC-тега.");
            }

            if (string.IsNullOrWhiteSpace(candidate.TagAddress))
            {
                errors.Add("• Укажите адрес PLC-тега.");
            }
            else if (project.Plant != null)
            {
                var targetCpuFamily = candidate.TargetCpuFamily ?? project.Plant.CpuFamily;
                if (candidate.TargetCpuFamily.HasValue &&
                    targetCpuFamily != PlcCpuFamily.S71200 &&
                    targetCpuFamily != PlcCpuFamily.S71500)
                {
                    errors.Add("• Выберите S7-1200 или S7-1500.");
                }

                PlcAddressSpec addressSpec;
                string canonicalAddress;
                string addressError;
                if (!PlcAddressing.TryParse(
                    targetCpuFamily,
                    candidate.TagAddress,
                    candidate.DataType,
                    out addressSpec,
                    out canonicalAddress,
                    out addressError))
                {
                    errors.Add("• Адрес PLC-тега: " + addressError);
                }

                if (candidate.TargetCpuFamily.HasValue &&
                    (targetCpuFamily == PlcCpuFamily.S71200 ||
                     targetCpuFamily == PlcCpuFamily.S71500))
                {
                    foreach (var existingTag in (project.Plant.Tags ?? new List<TagDefinition>())
                        .Where(tag => tag != null && tag.Id != candidate.TagDefinitionId))
                    {
                        if (!PlcAddressing.TryParse(
                            targetCpuFamily,
                            existingTag.Address,
                            existingTag.DataType,
                            out addressSpec,
                            out canonicalAddress,
                            out addressError))
                        {
                            errors.Add(
                                "• Существующий PLC-тег «" + existingTag.Name +
                                "» не подходит для выбранной CPU: " + addressError);
                        }
                    }
                }
            }

            if (!NodeCreationLogic.IsValidXmlText(candidate.TagComment))
            {
                errors.Add("• Комментарий содержит символы, которые нельзя сохранить в XML проекта.");
            }

            var owner = FindGlobalSymbolOwner(
                project,
                candidate.TagName,
                candidate,
                null,
                candidate.TagDefinitionId,
                null);
            if (owner != null)
            {
                errors.Add(
                    "• PLC-тег «" + candidate.TagName +
                    "» конфликтует с объектом проекта (" + owner + ").");
            }

            var affectedPinIds = new List<Guid>();
            foreach (var linkedNode in SafeSheets(project)
                .SelectMany(sheet => SafeNodes(sheet))
                .OfType<TagNode>()
                .Where(node => node.TagDefinitionId == candidate.TagDefinitionId))
            {
                var expectedDirection = linkedNode.TerminalDirection == TerminalDirection.Source
                    ? PinDirection.Output
                    : PinDirection.Input;
                var pin = ValidateSingleTerminalPin(linkedNode, expectedDirection, errors);
                if (pin != null)
                {
                    affectedPinIds.Add(pin.Id);
                }
            }

            if (!string.IsNullOrWhiteSpace(candidate.DataType))
            {
                ValidateAffectedPinIdentity(project, affectedPinIds, errors);
                ValidateAffectedWireTypes(project, affectedPinIds, candidate.DataType, errors);
            }
        }

        private static void ValidateAffectedPinIdentity(
            DiagramProject project,
            IEnumerable<Guid> affectedPins,
            IList<string> errors)
        {
            var affected = affectedPins.ToList();
            if (affected.Any(id => id == Guid.Empty) ||
                affected.Count != affected.Distinct().Count())
            {
                errors.Add(
                    "• Идентификаторы изменяемых пинов пусты или повторяются; " +
                    "безопасное сохранение невозможно.");
                return;
            }

            var projectPinIds = SafeSheets(project)
                .SelectMany(sheet => SafeNodes(sheet))
                .SelectMany(node => node.Pins ?? new List<Pin>())
                .Select(pin => pin.Id)
                .ToList();
            if (affected.Any(id => projectPinIds.Count(candidate => candidate == id) != 1))
            {
                errors.Add(
                    "• Идентификатор изменяемого пина не уникален в проекте; " +
                    "безопасное сохранение невозможно.");
            }
        }

        private static Pin ValidateSingleTerminalPin(
            CallNode node,
            PinDirection expectedDirection,
            IList<string> errors)
        {
            var pins = node.Pins ?? new List<Pin>();
            if (pins.Count != 1 ||
                pins[0].NodeId != node.Id ||
                pins[0].Role != PinRole.Terminal ||
                pins[0].Direction != expectedDirection)
            {
                errors.Add(
                    "• Контракт терминала узла «" + (node.Title ?? node.Id.ToString("D")) +
                    "» повреждён; автоматическое исправление проводов запрещено.");
                return null;
            }

            return pins[0];
        }

        private static void ValidateAffectedWireTypes(
            DiagramProject project,
            IEnumerable<Guid> affectedPins,
            string futureType,
            IList<string> errors)
        {
            var affected = new HashSet<Guid>(affectedPins);
            if (affected.Count == 0)
            {
                return;
            }

            foreach (var sheet in SafeSheets(project))
            {
                var pins = SafeNodes(sheet)
                    .SelectMany(node => node.Pins ?? new List<Pin>())
                    .GroupBy(pin => pin.Id)
                    .ToDictionary(group => group.Key, group => group.First());

                foreach (var wire in sheet.Wires ?? new List<Wire>())
                {
                    if (!affected.Contains(wire.SourcePinId) && !affected.Contains(wire.TargetPinId))
                    {
                        continue;
                    }

                    Pin sourcePin;
                    Pin targetPin;
                    if (!pins.TryGetValue(wire.SourcePinId, out sourcePin) ||
                        !pins.TryGetValue(wire.TargetPinId, out targetPin))
                    {
                        errors.Add(
                            "• Провод «" + wire.Id.ToString("D") +
                            "» имеет отсутствующий конец; изменение узла заблокировано.");
                        continue;
                    }

                    var sourceType = affected.Contains(sourcePin.Id) ? futureType : sourcePin.DataType;
                    var targetType = affected.Contains(targetPin.Id) ? futureType : targetPin.DataType;
                    if (!PlcTypeCompatibility.AreExactlyCompatible(sourceType, targetType))
                    {
                        errors.Add(
                            "• Тип «" + futureType + "» несовместим с существующим проводом на листе «" +
                            (sheet.Name ?? string.Empty) + "». Сначала отсоедините провод вручную.");
                    }
                }
            }
        }

        private static string FindGlobalSymbolOwner(
            DiagramProject project,
            string name,
            NodeEditResult candidate,
            Guid? excludedNodeId,
            Guid? excludedTagId,
            Guid? excludedSheetDbId)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var normalized = name.Trim();
            return EnumerateGlobalSymbols(
                    project,
                    candidate,
                    excludedNodeId,
                    excludedTagId,
                    excludedSheetDbId)
                .Where(symbol => string.Equals(
                    symbol.Name,
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
                .Select(symbol => symbol.Owner)
                .FirstOrDefault();
        }

        private static IEnumerable<SymbolEntry> EnumerateGlobalSymbols(
            DiagramProject project,
            NodeEditResult candidate,
            Guid? excludedNodeId,
            Guid? excludedTagId,
            Guid? excludedSheetDbId)
        {
            var plant = project.Plant ?? new PlantProject();
            foreach (var block in plant.Blocks ?? new List<BlockDefinition>())
            {
                if (block != null)
                {
                    yield return new SymbolEntry(block.Name, "блок " + block.Name);
                }
            }

            foreach (var dataType in plant.DataTypes ?? new List<UdtDefinition>())
            {
                if (dataType != null)
                {
                    yield return new SymbolEntry(dataType.Name, "тип данных " + dataType.Name);
                }
            }

            foreach (var tag in plant.Tags ?? new List<TagDefinition>())
            {
                if (tag != null && (!excludedTagId.HasValue || tag.Id != excludedTagId.Value))
                {
                    yield return new SymbolEntry(tag.Name, "PLC-тег " + tag.Name);
                }
            }

            foreach (var callBlock in plant.CallBlocks ?? new List<CallBlockDefinition>())
            {
                if (callBlock == null)
                {
                    continue;
                }

                yield return new SymbolEntry(callBlock.Name, "блок вызовов " + callBlock.Name);
                if (callBlock.Kind == BlockKind.FunctionBlock)
                {
                    var dbName = string.IsNullOrWhiteSpace(callBlock.InstanceDbName)
                        ? (callBlock.Name ?? string.Empty) + "_DB"
                        : callBlock.InstanceDbName.Trim();
                    yield return new SymbolEntry(dbName, "DB блока вызовов " + dbName);
                }

                var blocksById = (plant.Blocks ?? new List<BlockDefinition>())
                    .Where(block => block != null)
                    .GroupBy(block => block.Id)
                    .ToDictionary(group => group.Key, group => group.First());
                foreach (var unit in callBlock.Units ?? new List<UnitDefinition>())
                {
                    if (unit == null || unit.UseMultiInstance)
                    {
                        continue;
                    }

                    BlockDefinition definition;
                    if (!blocksById.TryGetValue(unit.BlockId, out definition))
                    {
                        definition = (plant.Blocks ?? new List<BlockDefinition>())
                            .FirstOrDefault(block =>
                                block != null &&
                                string.Equals(
                                    block.Name,
                                    unit.BlockName,
                                    StringComparison.OrdinalIgnoreCase));
                    }

                    if (definition != null && definition.Kind == BlockKind.FunctionBlock)
                    {
                        yield return new SymbolEntry(unit.Name, "instance DB " + unit.Name);
                    }
                }
            }

            foreach (var sheet in SafeSheets(project))
            {
                yield return new SymbolEntry(sheet.Name, "лист вызовов " + sheet.Name);
                if (SheetHasMultiInstance(project, sheet, candidate) &&
                    (!excludedSheetDbId.HasValue || sheet.Id != excludedSheetDbId.Value))
                {
                    var dbName = (sheet.Name ?? string.Empty) + "_DB";
                    yield return new SymbolEntry(dbName, "DB листа вызовов " + dbName);
                }
            }

            var definitions = (plant.Blocks ?? new List<BlockDefinition>())
                .Where(block => block != null)
                .GroupBy(block => block.Id)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (var node in SafeSheets(project)
                .SelectMany(sheet => SafeNodes(sheet))
                .OfType<BlockCallNode>())
            {
                if (excludedNodeId.HasValue && node.Id == excludedNodeId.Value)
                {
                    continue;
                }

                BlockDefinition definition;
                if (!definitions.TryGetValue(node.BlockDefinitionId, out definition) ||
                    definition.Kind != BlockKind.FunctionBlock ||
                    GetFutureUseMultiInstance(node, definition, candidate))
                {
                    continue;
                }

                var instanceName = GetFutureInstanceName(node, candidate);
                yield return new SymbolEntry(instanceName, "instance DB " + instanceName);
            }
        }

        private static bool SheetHasMultiInstance(
            DiagramProject project,
            CallSheet sheet,
            NodeEditResult candidate)
        {
            var definitions = ((project.Plant ?? new PlantProject()).Blocks ?? new List<BlockDefinition>())
                .Where(block => block != null)
                .GroupBy(block => block.Id)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (var node in SafeNodes(sheet).OfType<BlockCallNode>())
            {
                BlockDefinition definition;
                if (definitions.TryGetValue(node.BlockDefinitionId, out definition) &&
                    definition.Kind == BlockKind.FunctionBlock &&
                    GetFutureUseMultiInstance(node, definition, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool GetFutureUseMultiInstance(
            BlockCallNode node,
            BlockDefinition definition,
            NodeEditResult candidate)
        {
            if (candidate != null &&
                candidate.Kind == CallNodeKind.BlockCall &&
                node.Id == candidate.NodeId)
            {
                return definition.Kind == BlockKind.FunctionBlock && candidate.UseMultiInstance;
            }

            return definition.Kind == BlockKind.FunctionBlock && node.UseMultiInstance;
        }

        private static string GetFutureInstanceName(
            BlockCallNode node,
            NodeEditResult candidate)
        {
            return candidate != null &&
                candidate.Kind == CallNodeKind.BlockCall &&
                node.Id == candidate.NodeId
                ? candidate.InstanceName
                : node.InstanceName;
        }

        private static IList<NodeContext> FindNodeContexts(DiagramProject project, Guid nodeId)
        {
            var contexts = new List<NodeContext>();
            foreach (var sheet in SafeSheets(project))
            {
                foreach (var node in SafeNodes(sheet).Where(item => item != null && item.Id == nodeId))
                {
                    contexts.Add(new NodeContext(sheet, node));
                }
            }

            return contexts;
        }

        private static IList<BlockDefinition> FindBlockDefinitions(DiagramProject project, Guid id)
        {
            return ((project.Plant ?? new PlantProject()).Blocks ?? new List<BlockDefinition>())
                .Where(definition => definition != null && definition.Id == id)
                .ToList();
        }

        private static IList<TagDefinition> FindTagDefinitions(DiagramProject project, Guid id)
        {
            return ((project.Plant ?? new PlantProject()).Tags ?? new List<TagDefinition>())
                .Where(tag => tag != null && tag.Id == id)
                .ToList();
        }

        private static IEnumerable<CallSheet> SafeSheets(DiagramProject project)
        {
            return (project.Sheets ?? new List<CallSheet>()).Where(sheet => sheet != null);
        }

        private static IEnumerable<CallNode> SafeNodes(CallSheet sheet)
        {
            return (sheet.Nodes ?? new List<CallNode>()).Where(node => node != null);
        }

        private static Pin ClonePin(Pin source)
        {
            return new Pin
            {
                Id = source.Id,
                NodeId = source.NodeId,
                MemberId = source.MemberId,
                Name = source.Name ?? string.Empty,
                Direction = source.Direction,
                Role = source.Role,
                DataType = source.DataType ?? string.Empty,
                IsRequired = source.IsRequired,
                Order = source.Order
            };
        }

        private static string TranslateSclNameError(string error)
        {
            if (string.Equals(error, "Name is required.", StringComparison.Ordinal))
            {
                return "имя обязательно.";
            }

            if (string.Equals(
                error,
                "Name must not have leading or trailing whitespace.",
                StringComparison.Ordinal))
            {
                return "уберите пробелы в начале и конце.";
            }

            if (error != null && error.StartsWith("Name must not exceed", StringComparison.Ordinal))
            {
                return "длина имени не должна превышать " + SclName.MaximumLength + " символов.";
            }

            if (string.Equals(
                error,
                "Name must start with an ASCII letter or underscore and contain only ASCII letters, digits and underscores.",
                StringComparison.Ordinal))
            {
                return "используйте латинские буквы, цифры и подчёркивание; первый символ — буква или подчёркивание.";
            }

            if (string.Equals(error, "Name is an SCL reserved word.", StringComparison.Ordinal))
            {
                return "это зарезервированное слово SCL.";
            }

            return "недопустимый SCL-идентификатор.";
        }

        private sealed class NodeContext
        {
            internal NodeContext(CallSheet sheet, CallNode node)
            {
                Sheet = sheet;
                Node = node;
            }

            internal CallSheet Sheet { get; private set; }

            internal CallNode Node { get; private set; }
        }

        private sealed class SymbolEntry
        {
            internal SymbolEntry(string name, string owner)
            {
                Name = (name ?? string.Empty).Trim();
                Owner = owner ?? string.Empty;
            }

            internal string Name { get; private set; }

            internal string Owner { get; private set; }
        }
    }
}
