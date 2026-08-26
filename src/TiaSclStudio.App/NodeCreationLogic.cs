using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    /// <summary>
    /// Immutable values for one constant or tag-terminal creation. Stable object
    /// identities are allocated once by NodeCreationLogic and reused by every
    /// validation pass and by the eventual Apply call.
    /// </summary>
    internal sealed class NodeCreationResult
    {
        private NodeCreationResult(
            CallNodeKind kind,
            Guid nodeId,
            Guid pinId,
            Guid tagDefinitionId,
            bool createTagDefinition,
            string literal,
            string dataType,
            string tagName,
            string tagAddress,
            string tagComment,
            TerminalDirection terminalDirection,
            PlcCpuFamily? targetCpuFamily)
        {
            Kind = kind;
            NodeId = nodeId;
            PinId = pinId;
            TagDefinitionId = tagDefinitionId;
            CreateTagDefinition = createTagDefinition;
            Literal = literal ?? string.Empty;
            DataType = dataType ?? string.Empty;
            TagName = tagName ?? string.Empty;
            TagAddress = tagAddress ?? string.Empty;
            TagComment = tagComment ?? string.Empty;
            TerminalDirection = terminalDirection;
            TargetCpuFamily = targetCpuFamily;
        }

        public CallNodeKind Kind { get; private set; }

        public Guid NodeId { get; private set; }

        public Guid PinId { get; private set; }

        public Guid TagDefinitionId { get; private set; }

        public bool CreateTagDefinition { get; private set; }

        public string Literal { get; private set; }

        public string DataType { get; private set; }

        public string TagName { get; private set; }

        public string TagAddress { get; private set; }

        public string TagComment { get; private set; }

        public TerminalDirection TerminalDirection { get; private set; }

        public PlcCpuFamily? TargetCpuFamily { get; private set; }

        internal static NodeCreationResult ForConstant(
            Guid nodeId,
            Guid pinId,
            string literal,
            string dataType)
        {
            return new NodeCreationResult(
                CallNodeKind.Constant,
                nodeId,
                pinId,
                Guid.Empty,
                false,
                literal,
                dataType,
                string.Empty,
                string.Empty,
                string.Empty,
                default(TerminalDirection),
                null);
        }

        internal static NodeCreationResult ForNewTag(
            Guid nodeId,
            Guid pinId,
            Guid tagDefinitionId,
            string name,
            string dataType,
            string address,
            string comment,
            TerminalDirection direction,
            PlcCpuFamily cpuFamily)
        {
            return new NodeCreationResult(
                CallNodeKind.Tag,
                nodeId,
                pinId,
                tagDefinitionId,
                true,
                string.Empty,
                dataType,
                name,
                address,
                comment,
                direction,
                cpuFamily);
        }

        internal static NodeCreationResult ForExistingTag(
            Guid nodeId,
            Guid pinId,
            Guid tagDefinitionId,
            TerminalDirection direction,
            PlcCpuFamily? targetCpuFamily)
        {
            return new NodeCreationResult(
                CallNodeKind.Tag,
                nodeId,
                pinId,
                tagDefinitionId,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                direction,
                targetCpuFamily);
        }
    }

    /// <summary>
    /// Validates and atomically commits direct constant and tag-terminal creation.
    /// The raw diagram factory remains useful for import fixtures; UI creation uses
    /// this stricter project-aware boundary.
    /// </summary>
    internal static class NodeCreationLogic
    {
        internal static NodeCreationResult CreateConstant(string literal, string dataType)
        {
            var nodeId = NewIdentity();
            var pinId = NewIdentity(nodeId);
            return NodeCreationResult.ForConstant(
                nodeId,
                pinId,
                (literal ?? string.Empty).Trim(' '),
                (dataType ?? string.Empty).Trim());
        }

        internal static NodeCreationResult CreateNewTag(
            string name,
            string dataType,
            string address,
            string comment,
            TerminalDirection direction)
        {
            return CreateNewTag(
                name,
                dataType,
                address,
                comment,
                direction,
                PlcCpuFamily.S71200);
        }

        internal static NodeCreationResult CreateNewTag(
            string name,
            string dataType,
            string address,
            string comment,
            TerminalDirection direction,
            PlcCpuFamily cpuFamily)
        {
            var tagId = NewIdentity();
            var nodeId = NewIdentity(tagId);
            var pinId = NewIdentity(tagId, nodeId);
            return NodeCreationResult.ForNewTag(
                nodeId,
                pinId,
                tagId,
                (name ?? string.Empty).Trim(),
                (dataType ?? string.Empty).Trim(),
                (address ?? string.Empty).Trim(),
                comment ?? string.Empty,
                direction,
                cpuFamily);
        }

        internal static NodeCreationResult CreateExistingTag(
            Guid tagDefinitionId,
            TerminalDirection direction)
        {
            return CreateExistingTag(tagDefinitionId, direction, null);
        }

        internal static NodeCreationResult CreateExistingTag(
            Guid tagDefinitionId,
            TerminalDirection direction,
            PlcCpuFamily? targetCpuFamily)
        {
            var nodeId = NewIdentity(tagDefinitionId);
            var pinId = NewIdentity(tagDefinitionId, nodeId);
            return NodeCreationResult.ForExistingTag(
                nodeId,
                pinId,
                tagDefinitionId,
                direction,
                targetCpuFamily);
        }

        internal static IList<string> Validate(
            DiagramProject project,
            Guid sheetId,
            NodeCreationResult candidate)
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
            var sheets = Safe(project.Sheets);
            var matchingSheets = sheetId == Guid.Empty
                ? new List<CallSheet>()
                : sheets.Where(sheet => sheet.Id == sheetId).ToList();
            if (sheetId == Guid.Empty || matchingSheets.Count != 1)
            {
                errors.Add(
                    matchingSheets.Count > 1
                        ? "The target sheet id is duplicated in the project."
                        : "The target sheet was not found in the project.");
            }
            else if (matchingSheets[0].Nodes == null)
            {
                errors.Add("The target sheet has no node collection.");
            }

            var identities = CollectProjectIdentities(project);
            ValidateFreshIdentity(candidate.NodeId, "node", identities, errors);
            ValidateFreshIdentity(candidate.PinId, "pin", identities, errors);
            if (candidate.NodeId != Guid.Empty && candidate.NodeId == candidate.PinId)
            {
                errors.Add("The new node and pin must have different stable ids.");
            }

            switch (candidate.Kind)
            {
                case CallNodeKind.Constant:
                    ValidateConstant(candidate, errors);
                    break;
                case CallNodeKind.Tag:
                    ValidateTag(project, candidate, identities, errors);
                    break;
                default:
                    errors.Add("Only constant and tag-terminal nodes can be created by this operation.");
                    break;
            }

            return errors.Distinct(StringComparer.Ordinal).ToList();
        }

        internal static void Apply(
            DiagramProject project,
            Guid sheetId,
            NodeCreationResult result)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            var errors = Validate(project, sheetId, result);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }

            var targetSheet = project.Sheets.Single(sheet => sheet != null && sheet.Id == sheetId);
            if (result.Kind == CallNodeKind.Constant)
            {
                targetSheet.Nodes.Add(CreateConstantNode(result));
                return;
            }

            var plant = project.Plant;
            TagDefinition definition;
            if (result.CreateTagDefinition)
            {
                PlcAddressSpec addressSpec;
                string canonicalAddress;
                string addressError;
                PlcAddressing.TryParse(
                    result.TargetCpuFamily.Value,
                    result.TagAddress,
                    result.DataType,
                    out addressSpec,
                    out canonicalAddress,
                    out addressError);

                definition = new TagDefinition
                {
                    Id = result.TagDefinitionId,
                    Name = result.TagName,
                    DataType = result.DataType,
                    Address = canonicalAddress,
                    Comment = result.TagComment
                };
            }
            else
            {
                definition = plant.Tags.Single(tag =>
                    tag != null && tag.Id == result.TagDefinitionId);
            }

            var node = CreateTagNode(result, definition);
            if (!result.CreateTagDefinition)
            {
                if (result.TargetCpuFamily.HasValue)
                {
                    plant.CpuFamily = result.TargetCpuFamily.Value;
                }

                targetSheet.Nodes.Add(node);
                return;
            }

            if (result.TargetCpuFamily.HasValue)
            {
                plant.CpuFamily = result.TargetCpuFamily.Value;
            }

            plant.Tags.Add(definition);
            targetSheet.Nodes.Add(node);
        }

        private static ConstantNode CreateConstantNode(NodeCreationResult result)
        {
            var node = DiagramNodeFactory.CreateConstant(
                result.Literal,
                result.DataType,
                0.0,
                0.0);
            ApplyStableTerminalIdentities(node, result);
            return node;
        }

        private static TagNode CreateTagNode(
            NodeCreationResult result,
            TagDefinition definition)
        {
            var node = DiagramNodeFactory.CreateTag(
                definition,
                result.TerminalDirection,
                0.0,
                0.0);
            ApplyStableTerminalIdentities(node, result);
            return node;
        }

        private static void ApplyStableTerminalIdentities(
            CallNode node,
            NodeCreationResult result)
        {
            node.Id = result.NodeId;
            var pin = node.Pins.Single();
            pin.Id = result.PinId;
            pin.NodeId = result.NodeId;
        }

        private static void ValidateConstant(
            NodeCreationResult candidate,
            IList<string> errors)
        {
            if (candidate.CreateTagDefinition || candidate.TagDefinitionId != Guid.Empty)
            {
                errors.Add("A constant creation result must not reference a tag definition.");
            }

            if (string.IsNullOrWhiteSpace(candidate.Literal))
            {
                errors.Add("A constant literal is required.");
            }
            else if (!IsSafeConstantLiteral(candidate.Literal))
            {
                errors.Add(
                    "A constant literal must be single-line printable text without SCL statement or comment delimiters.");
            }

            ValidateDataType(candidate.DataType, "constant", errors);
        }

        internal static bool IsSafeConstantLiteral(string literal)
        {
            if (literal.IndexOf(';') >= 0 ||
                literal.IndexOf("//", StringComparison.Ordinal) >= 0 ||
                literal.IndexOf("(*", StringComparison.Ordinal) >= 0 ||
                literal.IndexOf("*)", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            return !literal.Any(char.IsControl);
        }

        internal static bool IsValidXmlText(string value)
        {
            try
            {
                XmlConvert.VerifyXmlChars(value ?? string.Empty);
                return true;
            }
            catch (XmlException)
            {
                return false;
            }
        }

        private static void ValidateTag(
            DiagramProject project,
            NodeCreationResult candidate,
            IDictionary<Guid, List<string>> identities,
            IList<string> errors)
        {
            if (!Enum.IsDefined(typeof(TerminalDirection), candidate.TerminalDirection))
            {
                errors.Add("The tag-terminal direction is not supported.");
            }

            if (candidate.TagDefinitionId == Guid.Empty)
            {
                errors.Add("A tag definition requires a non-empty stable id.");
            }

            if (candidate.TagDefinitionId == candidate.NodeId ||
                candidate.TagDefinitionId == candidate.PinId)
            {
                errors.Add("The tag, node and pin must have different stable ids.");
            }

            var plant = project.Plant;
            if (plant == null || plant.Tags == null)
            {
                errors.Add("The project has no PLC-tag collection.");
                return;
            }

            if (candidate.CreateTagDefinition)
            {
                ValidateFreshIdentity(candidate.TagDefinitionId, "tag", identities, errors);
                ValidateTagFields(
                    candidate.TagName,
                    candidate.DataType,
                    candidate.TagAddress,
                    candidate.TagComment,
                    errors);

                if (!candidate.TargetCpuFamily.HasValue ||
                    (candidate.TargetCpuFamily.Value != PlcCpuFamily.S71200 &&
                     candidate.TargetCpuFamily.Value != PlcCpuFamily.S71500))
                {
                    errors.Add("A supported target CPU family is required for a new PLC tag.");
                }
                else
                {
                    ValidateDirectTagAddress(
                        candidate.TargetCpuFamily.Value,
                        candidate.DataType,
                        candidate.TagAddress,
                        "PLC tag '" + candidate.TagName + "'",
                        errors);

                    foreach (var existingTag in project.Plant.Tags.Where(tag => tag != null))
                    {
                        ValidateDirectTagAddress(
                            candidate.TargetCpuFamily.Value,
                            existingTag.DataType,
                            existingTag.Address,
                            "Existing PLC tag '" + existingTag.Name + "'",
                            errors);
                    }
                }

                var owner = FindGlobalSymbolOwner(project, candidate.TagName);
                if (owner != null)
                {
                    errors.Add(
                        "PLC tag '" + candidate.TagName +
                        "' collides with project object " + owner + ".");
                }

                return;
            }

            var definitions = Safe(plant.Tags)
                .Where(tag => tag.Id == candidate.TagDefinitionId)
                .ToList();
            if (definitions.Count != 1)
            {
                errors.Add(
                    definitions.Count > 1
                        ? "The referenced PLC-tag id is duplicated in the project."
                        : "The referenced PLC tag was not found in the project.");
                return;
            }

            List<string> owners;
            if (!identities.TryGetValue(candidate.TagDefinitionId, out owners) ||
                owners.Count != 1)
            {
                errors.Add("The referenced PLC-tag id collides with another project identity.");
            }

            var definition = definitions[0];
            ValidateTagFields(
                definition.Name,
                definition.DataType,
                definition.Address,
                definition.Comment,
                errors);
            var effectiveCpuFamily = candidate.TargetCpuFamily ?? project.Plant.CpuFamily;
            if (candidate.TargetCpuFamily.HasValue &&
                candidate.TargetCpuFamily.Value != PlcCpuFamily.S71200 &&
                candidate.TargetCpuFamily.Value != PlcCpuFamily.S71500)
            {
                errors.Add("A supported target CPU family is required for the PLC tag.");
            }
            else if (Enum.IsDefined(typeof(PlcCpuFamily), effectiveCpuFamily))
            {
                var tagsToValidate = candidate.TargetCpuFamily.HasValue
                    ? project.Plant.Tags.Where(tag => tag != null)
                    : new[] { definition };
                foreach (var tag in tagsToValidate)
                {
                    ValidateDirectTagAddress(
                        effectiveCpuFamily,
                        tag.DataType,
                        tag.Address,
                        "PLC tag '" + tag.Name + "'",
                        errors);
                }
            }
        }

        private static void ValidateDirectTagAddress(
            PlcCpuFamily cpuFamily,
            string dataType,
            string address,
            string subject,
            IList<string> errors)
        {
            PlcAddressSpec spec;
            string canonicalAddress;
            string addressError;
            if (!PlcAddressing.TryParse(
                cpuFamily,
                address,
                dataType,
                out spec,
                out canonicalAddress,
                out addressError))
            {
                errors.Add(subject + ": " + addressError);
            }
        }

        private static void ValidateTagFields(
            string name,
            string dataType,
            string address,
            string comment,
            IList<string> errors)
        {
            string nameError;
            if (!SclName.TryValidate(name, out nameError))
            {
                errors.Add("PLC-tag name: " + nameError);
            }

            ValidateDataType(dataType, "PLC tag", errors);
            if (string.IsNullOrWhiteSpace(address))
            {
                errors.Add("A PLC-tag address is required.");
            }

            if (!IsValidXmlText(comment))
            {
                errors.Add("A PLC-tag comment contains characters that cannot be saved to the project XML.");
            }
        }

        private static void ValidateDataType(
            string dataType,
            string subject,
            IList<string> errors)
        {
            if (string.IsNullOrWhiteSpace(dataType))
            {
                errors.Add("A data type is required for the " + subject + ".");
            }
            else if (string.Equals(
                dataType.Trim(),
                "Void",
                StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Void is not a valid data type for the " + subject + ".");
            }
        }

        private static void ValidateFreshIdentity(
            Guid id,
            string subject,
            IDictionary<Guid, List<string>> identities,
            IList<string> errors)
        {
            if (id == Guid.Empty)
            {
                errors.Add("The new " + subject + " requires a non-empty stable id.");
                return;
            }

            List<string> owners;
            if (identities.TryGetValue(id, out owners))
            {
                errors.Add(
                    "The new " + subject + " id is already used by " +
                    string.Join(", ", owners) + ".");
            }
        }

        private static IDictionary<Guid, List<string>> CollectProjectIdentities(
            DiagramProject project)
        {
            var result = new Dictionary<Guid, List<string>>();
            var plant = project.Plant;
            if (plant != null)
            {
                RegisterIdentity(result, plant.Id, "Plant project");
                foreach (var block in Safe(plant.Blocks))
                {
                    RegisterIdentity(result, block.Id, "Plant block");
                    foreach (var member in Safe(block.Interface))
                    {
                        RegisterIdentity(result, member.Id, "block interface member");
                    }
                }

                foreach (var dataType in Safe(plant.DataTypes))
                {
                    RegisterIdentity(result, dataType.Id, "Plant data type");
                    foreach (var member in Safe(dataType.Members))
                    {
                        RegisterIdentity(result, member.Id, "data-type member");
                    }
                }

                foreach (var tag in Safe(plant.Tags))
                {
                    RegisterIdentity(result, tag.Id, "PLC tag");
                }

                foreach (var callBlock in Safe(plant.CallBlocks))
                {
                    RegisterIdentity(result, callBlock.Id, "Plant call block");
                    foreach (var member in Safe(callBlock.Interface))
                    {
                        RegisterIdentity(result, member.Id, "call-block interface member");
                    }

                    foreach (var unit in Safe(callBlock.Units))
                    {
                        RegisterIdentity(result, unit.Id, "Plant call unit");
                        foreach (var binding in Safe(unit.Bindings))
                        {
                            RegisterIdentity(result, binding.Id, "parameter binding");
                        }
                    }
                }
            }

            foreach (var sheet in Safe(project.Sheets))
            {
                RegisterIdentity(result, sheet.Id, "diagram sheet");
                foreach (var group in Safe(sheet.Groups))
                {
                    RegisterIdentity(result, group.Id, "diagram group");
                }

                foreach (var node in Safe(sheet.Nodes))
                {
                    RegisterIdentity(result, node.Id, "diagram node");
                    foreach (var pin in Safe(node.Pins))
                    {
                        RegisterIdentity(result, pin.Id, "diagram pin");
                    }
                }

                foreach (var wire in Safe(sheet.Wires))
                {
                    RegisterIdentity(result, wire.Id, "diagram wire");
                }
            }

            return result;
        }

        private static void RegisterIdentity(
            IDictionary<Guid, List<string>> identities,
            Guid id,
            string owner)
        {
            if (id == Guid.Empty)
            {
                return;
            }

            List<string> owners;
            if (!identities.TryGetValue(id, out owners))
            {
                owners = new List<string>();
                identities.Add(id, owners);
            }

            owners.Add(owner);
        }

        private static string FindGlobalSymbolOwner(
            DiagramProject project,
            string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var normalized = name.Trim();
            return EnumerateGlobalSymbols(project)
                .Where(symbol => string.Equals(
                    symbol.Name,
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
                .Select(symbol => symbol.Owner)
                .FirstOrDefault();
        }

        private static IEnumerable<SymbolEntry> EnumerateGlobalSymbols(
            DiagramProject project)
        {
            var plant = project.Plant ?? new PlantProject();
            foreach (var block in Safe(plant.Blocks))
            {
                yield return new SymbolEntry(block.Name, "block " + block.Name);
            }

            foreach (var dataType in Safe(plant.DataTypes))
            {
                yield return new SymbolEntry(dataType.Name, "data type " + dataType.Name);
            }

            foreach (var tag in Safe(plant.Tags))
            {
                yield return new SymbolEntry(tag.Name, "PLC tag " + tag.Name);
            }

            var blocks = Safe(plant.Blocks);
            foreach (var callBlock in Safe(plant.CallBlocks))
            {
                yield return new SymbolEntry(
                    callBlock.Name,
                    "call block " + callBlock.Name);
                if (callBlock.Kind == BlockKind.FunctionBlock)
                {
                    var dbName = string.IsNullOrWhiteSpace(callBlock.InstanceDbName)
                        ? (callBlock.Name ?? string.Empty) + "_DB"
                        : callBlock.InstanceDbName.Trim();
                    yield return new SymbolEntry(dbName, "call-block DB " + dbName);
                }

                foreach (var unit in Safe(callBlock.Units))
                {
                    if (unit.UseMultiInstance)
                    {
                        continue;
                    }

                    var definition = ResolveBlock(blocks, unit.BlockId, unit.BlockName);
                    if (definition != null && definition.Kind == BlockKind.FunctionBlock)
                    {
                        yield return new SymbolEntry(
                            unit.Name,
                            "instance DB " + unit.Name);
                    }
                }
            }

            foreach (var sheet in Safe(project.Sheets))
            {
                yield return new SymbolEntry(
                    sheet.Name,
                    "call sheet " + sheet.Name);
                if (SheetHasMultiInstance(sheet, blocks))
                {
                    var dbName = (sheet.Name ?? string.Empty) + "_DB";
                    yield return new SymbolEntry(dbName, "sheet DB " + dbName);
                }
            }

            foreach (var node in Safe(project.Sheets)
                .SelectMany(sheet => Safe(sheet.Nodes))
                .OfType<BlockCallNode>())
            {
                var definition = ResolveBlock(
                    blocks,
                    node.BlockDefinitionId,
                    string.Empty);
                if (definition != null &&
                    definition.Kind == BlockKind.FunctionBlock &&
                    !node.UseMultiInstance)
                {
                    yield return new SymbolEntry(
                        node.InstanceName,
                        "instance DB " + node.InstanceName);
                }
            }
        }

        private static bool SheetHasMultiInstance(
            CallSheet sheet,
            IList<BlockDefinition> blocks)
        {
            foreach (var node in Safe(sheet.Nodes).OfType<BlockCallNode>())
            {
                var definition = ResolveBlock(
                    blocks,
                    node.BlockDefinitionId,
                    string.Empty);
                if (definition != null &&
                    definition.Kind == BlockKind.FunctionBlock &&
                    node.UseMultiInstance)
                {
                    return true;
                }
            }

            return false;
        }

        private static BlockDefinition ResolveBlock(
            IList<BlockDefinition> blocks,
            Guid id,
            string fallbackName)
        {
            if (id != Guid.Empty)
            {
                var byId = blocks.FirstOrDefault(block => block.Id == id);
                if (byId != null)
                {
                    return byId;
                }
            }

            return blocks.FirstOrDefault(block => string.Equals(
                block.Name,
                fallbackName,
                StringComparison.OrdinalIgnoreCase));
        }

        private static Guid NewIdentity(params Guid[] excluded)
        {
            var excludedIds = new HashSet<Guid>(excluded ?? new Guid[0]);
            Guid id;
            do
            {
                id = Guid.NewGuid();
            }
            while (id == Guid.Empty || excludedIds.Contains(id));

            return id;
        }

        private static List<T> Safe<T>(IEnumerable<T> items)
            where T : class
        {
            return items == null
                ? new List<T>()
                : items.Where(item => item != null).ToList();
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
