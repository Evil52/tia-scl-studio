using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TiaSclStudio.Core.Generation;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;

namespace TiaSclStudio.Diagram.Generation
{
    public sealed class DiagramSclGenerator
    {
        private sealed class OutputPlan
        {
            public string Expression { get; set; }

            public string DataType { get; set; }

            public List<string> Assignments { get; } = new List<string>();
        }

        public DiagramCompilationResult Generate(PlantProject project, CallSheet sheet)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            var validator = new DiagramValidator();
            var issues = new List<DiagramIssue>(validator.Validate(sheet).Issues);
            if (sheet == null)
            {
                return new DiagramCompilationResult(string.Empty, issues, new Guid[0]);
            }

            AppendCoreValidationIssues(project, issues);
            if (HasErrors(issues))
            {
                return new DiagramCompilationResult(string.Empty, issues, new Guid[0]);
            }

            var definitions = project.Blocks
                .GroupBy(block => block.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var tags = project.Tags
                .GroupBy(tag => tag.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var nodes = sheet.Nodes
                .GroupBy(node => node.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var pins = sheet.Nodes
                .SelectMany(node => node.Pins)
                .GroupBy(pin => pin.Id)
                .ToDictionary(group => group.Key, group => group.First());

            ValidateGenerationInputs(project, sheet, definitions, tags, issues);
            if (HasErrors(issues))
            {
                return new DiagramCompilationResult(string.Empty, issues, new Guid[0]);
            }

            var sort = TopologicalSorter.Sort(sheet);
            var executionNodes = sort.OrderedNodeIds
                .Select(id => nodes[id])
                .ToList();
            var blockOrder = executionNodes
                .OfType<BlockCallNode>()
                .ToList();

            var temporaryNames = new HashSet<string>(
                blockOrder
                    .Where(node => node.UseMultiInstance)
                    .Select(node => node.InstanceName),
                StringComparer.OrdinalIgnoreCase);
            var temporaries = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var outputPlans = BuildOutputPlans(
                sheet,
                nodes,
                pins,
                executionNodes,
                definitions,
                tags,
                temporaryNames,
                temporaries,
                issues);

            if (HasErrors(issues))
            {
                return new DiagramCompilationResult(string.Empty, issues, blockOrder.Select(node => node.Id));
            }

            var hasMultiInstances = blockOrder.Any(node => node.UseMultiInstance);
            var builder = new StringBuilder();
            AppendHeader(builder, sheet.Name, hasMultiInstances);
            AppendDeclarations(builder, blockOrder, definitions, temporaries, hasMultiInstances);
            builder.AppendLine("BEGIN");

            foreach (var note in sheet.Nodes.OfType<NoteNode>().OrderBy(node => node.Y).ThenBy(node => node.X))
            {
                AppendComment(builder, note.Text, "   ");
            }

            foreach (var executableNode in executionNodes)
            {
                var blockNode = executableNode as BlockCallNode;
                if (blockNode != null)
                {
                    var definition = definitions[blockNode.BlockDefinitionId];
                    AppendCall(builder, sheet, blockNode, definition, nodes, pins, tags, outputPlans, issues);
                }

                foreach (var pin in executableNode.Pins.Where(item => item.Direction == PinDirection.Output))
                {
                    OutputPlan plan;
                    if (!outputPlans.TryGetValue(pin.Id, out plan))
                    {
                        continue;
                    }

                    foreach (var assignment in plan.Assignments)
                    {
                        builder.Append("   ").Append(assignment).AppendLine(";");
                    }
                }
            }

            builder.AppendLine(hasMultiInstances ? "END_FUNCTION_BLOCK" : "END_FUNCTION");

            if (HasErrors(issues))
            {
                return new DiagramCompilationResult(string.Empty, issues, blockOrder.Select(node => node.Id));
            }

            var scl = builder.ToString();
            var generatedSources = BuildGeneratedSources(
                project,
                sheet,
                blockOrder,
                definitions,
                hasMultiInstances,
                scl);

            return new DiagramCompilationResult(
                scl,
                issues,
                blockOrder.Select(node => node.Id),
                generatedSources);
        }

        private static void AppendCoreValidationIssues(
            PlantProject project,
            IList<DiagramIssue> issues)
        {
            var validation = new ProjectValidator().Validate(project);
            foreach (var issue in validation.Issues)
            {
                issues.Add(new DiagramIssue(
                    issue.Severity == ValidationSeverity.Error
                        ? DiagramIssueSeverity.Error
                        : DiagramIssueSeverity.Warning,
                    "CORE_" + issue.Code,
                    issue.Path + ": " + issue.Message,
                    null,
                    null));
            }
        }

        private static void ValidateGenerationInputs(
            PlantProject project,
            CallSheet sheet,
            IDictionary<Guid, BlockDefinition> definitions,
            IDictionary<Guid, TagDefinition> tags,
            IList<DiagramIssue> issues)
        {
            var globalSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in project.Blocks.Select(item => item.Name)
                .Concat(project.DataTypes.Select(item => item.Name))
                .Concat(project.Tags.Select(item => item.Name))
                .Concat(project.CallBlocks.Select(item => item.Name)))
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    globalSymbols.Add(name);
                }
            }

            foreach (var callBlock in project.CallBlocks
                .Where(item => item.Kind == BlockKind.FunctionBlock))
            {
                var dbName = string.IsNullOrWhiteSpace(callBlock.InstanceDbName)
                    ? callBlock.Name + "_DB"
                    : callBlock.InstanceDbName.Trim();
                if (!string.IsNullOrWhiteSpace(dbName))
                {
                    globalSymbols.Add(dbName);
                }
            }

            foreach (var unit in project.CallBlocks
                .SelectMany(item => item.Units ?? new List<UnitDefinition>())
                .Where(item => item != null && !item.UseMultiInstance))
            {
                BlockDefinition definition;
                if (!definitions.TryGetValue(unit.BlockId, out definition))
                {
                    definition = project.Blocks.FirstOrDefault(item =>
                        string.Equals(item.Name, unit.BlockName, StringComparison.OrdinalIgnoreCase));
                }

                if (definition != null &&
                    definition.Kind == BlockKind.FunctionBlock &&
                    !string.IsNullOrWhiteSpace(unit.Name))
                {
                    globalSymbols.Add(unit.Name.Trim());
                }
            }

            if (!globalSymbols.Add(sheet.Name))
            {
                AddGenerationIssue(
                    issues,
                    "GEN012",
                    "Call-sheet name '" + sheet.Name + "' collides with an existing global symbol.",
                    null);
            }

            foreach (var node in sheet.Nodes.OfType<BlockCallNode>())
            {
                BlockDefinition definition;
                if (!definitions.TryGetValue(node.BlockDefinitionId, out definition))
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "GEN001",
                        "Block definition for node '" + node.Title + "' was not found.",
                        node.Id,
                        null));
                    continue;
                }

                string instanceNameError;
                if (!SclName.TryValidate(node.InstanceName, out instanceNameError))
                {
                    AddGenerationIssue(
                        issues,
                        "GEN013",
                        "Invalid instance/alias name '" + node.InstanceName + "': " + instanceNameError,
                        node.Id);
                }

                if (definition.Kind == BlockKind.Function && node.UseMultiInstance)
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "GEN002",
                        "Only FB calls can be multi-instances.",
                        node.Id,
                        null));
                }

                if (definition.Kind == BlockKind.FunctionBlock && !node.UseMultiInstance &&
                    !string.IsNullOrWhiteSpace(node.InstanceName) &&
                    !globalSymbols.Add(node.InstanceName))
                {
                    AddGenerationIssue(
                        issues,
                        "GEN014",
                        "Global instance DB name '" + node.InstanceName + "' collides with another symbol.",
                        node.Id);
                }

                foreach (var pin in node.Pins.Where(pin => pin.Role == PinRole.Parameter))
                {
                    var member = definition.Interface.FirstOrDefault(item => item.Id == pin.MemberId);
                    if (member == null || !IsCallable(member))
                    {
                        issues.Add(new DiagramIssue(
                            DiagramIssueSeverity.Error,
                            "GEN003",
                            "Pin '" + pin.Name + "' no longer exists in block '" + definition.Name + "'.",
                            node.Id,
                            null));
                    }
                }

                foreach (var member in definition.Interface.Where(IsCallable))
                {
                    var matchingPins = node.Pins
                        .Where(candidate => candidate.Role == PinRole.Parameter && candidate.MemberId == member.Id)
                        .ToList();
                    if (matchingPins.Count != 1)
                    {
                        AddGenerationIssue(
                            issues,
                            "GEN006",
                            "Placed block must contain exactly one current pin for '" + member.Name + "'. Refresh it from the library.",
                            node.Id);
                        continue;
                    }

                    var pin = matchingPins[0];
                    var expectedDirection = member.Section == InterfaceSection.Output
                        ? PinDirection.Output
                        : PinDirection.Input;
                    var expectedRequired = member.Section == InterfaceSection.InOut;
                    if (pin.Direction != expectedDirection ||
                        pin.IsRequired != expectedRequired ||
                        !PlcTypeCompatibility.AreExactlyCompatible(member.DataType, pin.DataType))
                    {
                        AddGenerationIssue(
                            issues,
                            "GEN004",
                            "Pin '" + member.Name + "' has a stale direction, requirement or data type. Refresh the placed block.",
                            node.Id);
                    }

                    if (member.Section == InterfaceSection.InOut &&
                        !sheet.Wires.Any(wire => wire.TargetPinId == pin.Id))
                    {
                        AddGenerationIssue(
                            issues,
                            "GEN015",
                            "Current InOut pin '" + member.Name + "' must be connected.",
                            node.Id);
                    }
                }

                var hasReturn = HasFunctionReturn(definition);
                var returnPins = node.Pins.Where(pin => pin.Role == PinRole.FunctionReturn).ToList();
                if ((!hasReturn && returnPins.Count != 0) || (hasReturn && returnPins.Count != 1))
                {
                    AddGenerationIssue(
                        issues,
                        "GEN007",
                        "Placed block has a stale function-return contract. Refresh it from the library.",
                        node.Id);
                }
                else if (hasReturn)
                {
                    var returnPin = returnPins[0];
                    if (returnPin.Direction != PinDirection.Output ||
                        returnPin.IsRequired ||
                        returnPin.MemberId != Guid.Empty ||
                        !PlcTypeCompatibility.AreExactlyCompatible(returnPin.DataType, definition.ReturnType))
                    {
                        AddGenerationIssue(
                            issues,
                            "GEN016",
                            "Function return pin has a stale direction or data type. Refresh the placed block.",
                            node.Id);
                    }
                }

                if (node.Pins.Any(pin => pin.Role != PinRole.Parameter && pin.Role != PinRole.FunctionReturn))
                {
                    AddGenerationIssue(
                        issues,
                        "GEN017",
                        "Block-call nodes may contain only parameter and function-return pins.",
                        node.Id);
                }
            }

            foreach (var node in sheet.Nodes.OfType<TagNode>())
            {
                TagDefinition tag;
                if (!tags.TryGetValue(node.TagDefinitionId, out tag))
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "GEN008",
                        "Tag definition for terminal '" + node.TagName + "' was not found.",
                        node.Id,
                        null));
                }
                else if (!PlcTypeCompatibility.AreExactlyCompatible(tag.DataType, node.DataType))
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "GEN009",
                        "Tag terminal '" + node.TagName + "' has a stale data type.",
                        node.Id,
                        null));
                }

                var expectedDirection = node.TerminalDirection == TerminalDirection.Source
                    ? PinDirection.Output
                    : PinDirection.Input;
                if (node.Pins.Count != 1 ||
                    node.Pins[0].Role != PinRole.Terminal ||
                    node.Pins[0].Direction != expectedDirection ||
                    !PlcTypeCompatibility.AreExactlyCompatible(node.Pins[0].DataType, node.DataType))
                {
                    AddGenerationIssue(
                        issues,
                        "GEN018",
                        "Tag terminal has a stale pin contract.",
                        node.Id);
                }
            }

            foreach (var node in sheet.Nodes.OfType<ConstantNode>())
            {
                if (string.IsNullOrWhiteSpace(node.Literal) ||
                    node.Pins.Count != 1 ||
                    node.Pins[0].Role != PinRole.Terminal ||
                    node.Pins[0].Direction != PinDirection.Output ||
                    !PlcTypeCompatibility.AreExactlyCompatible(node.Pins[0].DataType, node.DataType))
                {
                    AddGenerationIssue(
                        issues,
                        "GEN019",
                        "Constant node has an empty literal or stale pin contract.",
                        node.Id);
                }
            }

            foreach (var logic in sheet.Nodes.OfType<LogicNode>())
            {
                if (!Enum.IsDefined(typeof(LogicOperation), logic.Operation))
                {
                    AddGenerationIssue(
                        issues,
                        "GEN023",
                        "Unsupported logic operation '" + logic.Operation + "'.",
                        logic.Id);
                    continue;
                }

                IList<LogicPinContract> contracts;
                string contractError;
                if (!LogicNodeContract.TryValidate(logic, out contracts, out contractError))
                {
                    AddGenerationIssue(
                        issues,
                        "GEN024",
                        "Logic node has a stale pin contract: " + contractError,
                        logic.Id);
                }
            }

            foreach (var group in sheet.Nodes.OfType<BlockCallNode>()
                .Where(node => !string.IsNullOrWhiteSpace(node.InstanceName))
                .GroupBy(node => node.InstanceName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
            {
                foreach (var node in group)
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "GEN011",
                        "Duplicate instance name '" + group.Key + "'.",
                        node.Id,
                        null));
                }
            }

            if (sheet.Nodes.OfType<BlockCallNode>().Any(node => node.UseMultiInstance))
            {
                var dbName = sheet.Name + "_DB";
                string dbNameError;
                if (!SclName.TryValidate(dbName, out dbNameError))
                {
                    AddGenerationIssue(
                        issues,
                        "GEN021",
                        "Invalid call-block DB name '" + dbName + "': " + dbNameError,
                        null);
                }
                else if (!globalSymbols.Add(dbName))
                {
                    AddGenerationIssue(
                        issues,
                        "GEN022",
                        "Call-block DB name '" + dbName + "' collides with another global symbol.",
                        null);
                }
            }
        }

        private static bool IsCallable(InterfaceMember member)
        {
            return member.Section == InterfaceSection.Input ||
                member.Section == InterfaceSection.InOut ||
                member.Section == InterfaceSection.Output;
        }

        private static bool HasFunctionReturn(BlockDefinition definition)
        {
            return definition.Kind == BlockKind.Function &&
                !string.Equals(definition.ReturnType, "Void", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddGenerationIssue(
            IList<DiagramIssue> issues,
            string code,
            string message,
            Guid? nodeId)
        {
            issues.Add(new DiagramIssue(
                DiagramIssueSeverity.Error,
                code,
                message,
                nodeId,
                null));
        }

        private static Dictionary<Guid, OutputPlan> BuildOutputPlans(
            CallSheet sheet,
            IDictionary<Guid, CallNode> nodes,
            IDictionary<Guid, Pin> pins,
            IEnumerable<CallNode> executionNodes,
            IDictionary<Guid, BlockDefinition> definitions,
            IDictionary<Guid, TagDefinition> tags,
            ISet<string> temporaryNames,
            IDictionary<string, string> temporaries,
            IList<DiagramIssue> issues)
        {
            var plans = new Dictionary<Guid, OutputPlan>();

            foreach (var executableNode in executionNodes)
            {
                var blockNode = executableNode as BlockCallNode;
                if (blockNode != null)
                {
                    var definition = definitions[blockNode.BlockDefinitionId];
                    foreach (var pin in blockNode.Pins.Where(item => item.Direction == PinDirection.Output))
                    {
                        var outgoing = sheet.Wires.Where(wire => wire.SourcePinId == pin.Id).ToList();
                        var requiresResult = pin.Role == PinRole.FunctionReturn &&
                            definition.Kind == BlockKind.Function &&
                            !string.Equals(definition.ReturnType, "Void", StringComparison.OrdinalIgnoreCase);

                        if (outgoing.Count == 0 && !requiresResult)
                        {
                            continue;
                        }

                        var directTag = outgoing.Count == 1
                            ? nodes[outgoing[0].TargetNodeId] as TagNode
                            : null;

                        var plan = new OutputPlan { DataType = pin.DataType };
                        if (directTag != null)
                        {
                            plan.Expression = QuoteIdentifier(tags[directTag.TagDefinitionId].Name);
                        }
                        else
                        {
                            var tempName = MakeUniqueTemporaryName(
                                "tmp_" + blockNode.InstanceName + "_" + pin.Name,
                                temporaryNames);
                            plan.Expression = "#" + tempName;
                            temporaries.Add(tempName, pin.DataType);

                            foreach (var wire in outgoing)
                            {
                                var tag = nodes[wire.TargetNodeId] as TagNode;
                                if (tag != null)
                                {
                                    plan.Assignments.Add(QuoteIdentifier(tags[tag.TagDefinitionId].Name) + " := " + plan.Expression);
                                }
                            }
                        }

                        plans.Add(pin.Id, plan);
                    }

                    continue;
                }

                var logicNode = executableNode as LogicNode;
                if (logicNode == null)
                {
                    continue;
                }

                var outputPin = logicNode.Pins.Single(pin =>
                    pin.Role == PinRole.Logic &&
                    pin.Direction == PinDirection.Output);
                var logicOutgoing = sheet.Wires
                    .Where(wire => wire.SourcePinId == outputPin.Id)
                    .ToList();
                if (logicOutgoing.Count == 0)
                {
                    continue;
                }

                var logicExpression = BuildLogicExpression(
                    sheet,
                    logicNode,
                    nodes,
                    pins,
                    tags,
                    plans,
                    issues);
                if (string.IsNullOrEmpty(logicExpression))
                {
                    continue;
                }

                var logicPlan = new OutputPlan { DataType = outputPin.DataType };
                if (logicOutgoing.Count == 1)
                {
                    logicPlan.Expression = logicExpression;
                    var directTag = nodes[logicOutgoing[0].TargetNodeId] as TagNode;
                    if (directTag != null)
                    {
                        logicPlan.Assignments.Add(
                            QuoteIdentifier(tags[directTag.TagDefinitionId].Name) +
                            " := " + logicExpression);
                    }
                }
                else
                {
                    var tempName = MakeUniqueTemporaryName(
                        "tmp_logic_" + logicNode.Operation + "_Result",
                        temporaryNames);
                    logicPlan.Expression = "#" + tempName;
                    temporaries.Add(tempName, outputPin.DataType);
                    logicPlan.Assignments.Add(logicPlan.Expression + " := " + logicExpression);

                    foreach (var wire in logicOutgoing)
                    {
                        var tag = nodes[wire.TargetNodeId] as TagNode;
                        if (tag != null)
                        {
                            logicPlan.Assignments.Add(
                                QuoteIdentifier(tags[tag.TagDefinitionId].Name) +
                                " := " + logicPlan.Expression);
                        }
                    }
                }

                plans.Add(outputPin.Id, logicPlan);
            }

            return plans;
        }

        private static string BuildLogicExpression(
            CallSheet sheet,
            LogicNode node,
            IDictionary<Guid, CallNode> nodes,
            IDictionary<Guid, Pin> pins,
            IDictionary<Guid, TagDefinition> tags,
            IDictionary<Guid, OutputPlan> outputPlans,
            IList<DiagramIssue> issues)
        {
            var inputPins = node.Pins
                .Where(pin => pin.Role == PinRole.Logic && pin.Direction == PinDirection.Input)
                .OrderBy(pin => pin.Order)
                .ToList();
            var operands = new List<string>();
            foreach (var inputPin in inputPins)
            {
                var incoming = sheet.Wires.FirstOrDefault(wire => wire.TargetPinId == inputPin.Id);
                if (incoming == null)
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "GEN025",
                        "Cannot resolve logic input '" + inputPin.Name + "' because it is not connected.",
                        node.Id,
                        null));
                    return string.Empty;
                }

                var operand = ResolveSourceExpression(
                    incoming,
                    nodes,
                    pins,
                    tags,
                    outputPlans,
                    issues,
                    "GEN025");
                if (string.IsNullOrEmpty(operand))
                {
                    return string.Empty;
                }

                operands.Add(operand);
            }

            string sclOperator;
            if (!LogicNodeContract.TryGetOperator(node.Operation, out sclOperator))
            {
                AddGenerationIssue(
                    issues,
                    "GEN023",
                    "Unsupported logic operation '" + node.Operation + "'.",
                    node.Id);
                return string.Empty;
            }

            if (node.Operation == LogicOperation.Not)
            {
                return "(" + sclOperator + " " + operands[0] + ")";
            }

            return "(" + operands[0] + " " + sclOperator + " " + operands[1] + ")";
        }

        private static IList<GeneratedSource> BuildGeneratedSources(
            PlantProject project,
            CallSheet sheet,
            IEnumerable<BlockCallNode> blockOrder,
            IDictionary<Guid, BlockDefinition> definitions,
            bool hasMultiInstances,
            string callSheetScl)
        {
            var generator = new SclGenerator();
            var sources = new List<GeneratedSource>();
            var order = 0;

            if (project.DataTypes.Count > 0)
            {
                sources.Add(new GeneratedSource(
                    "00_Types.scl",
                    generator.GenerateUdts(project.DataTypes),
                    GeneratedSourceKind.DataTypes,
                    order++));
            }

            foreach (var block in project.Blocks)
            {
                if (block.ImportedInterfaceOnly)
                {
                    continue;
                }

                sources.Add(new GeneratedSource(
                    block.Name + ".scl",
                    generator.GenerateBlock(block),
                    GeneratedSourceKind.Block,
                    order++));
            }

            var instanceSources = new List<string>();
            foreach (var node in blockOrder)
            {
                var definition = definitions[node.BlockDefinitionId];
                if (definition.Kind != BlockKind.FunctionBlock || node.UseMultiInstance)
                {
                    continue;
                }

                var unit = new UnitDefinition(node.InstanceName, definition)
                {
                    Comment = definition.Description
                };
                instanceSources.Add(generator.GenerateInstanceDb(unit, definition));
            }

            if (instanceSources.Count > 0)
            {
                sources.Add(new GeneratedSource(
                    "90_Instances.scl",
                    string.Join("\r\n", instanceSources),
                    GeneratedSourceKind.InstanceDataBlocks,
                    order++));
            }

            sources.Add(new GeneratedSource(
                "99_" + sheet.Name + ".scl",
                callSheetScl,
                GeneratedSourceKind.CallBlock,
                order++));

            if (hasMultiInstances)
            {
                var callBlock = new CallBlockDefinition
                {
                    Name = sheet.Name,
                    Kind = BlockKind.FunctionBlock,
                    InstanceDbName = sheet.Name + "_DB"
                };
                sources.Add(new GeneratedSource(
                    "99z_" + callBlock.InstanceDbName + ".scl",
                    generator.GenerateCallBlockInstanceDb(callBlock),
                    GeneratedSourceKind.CallBlockInstanceDataBlock,
                    order));
            }

            return sources;
        }

        private static void AppendHeader(StringBuilder builder, string sheetName, bool isFunctionBlock)
        {
            if (isFunctionBlock)
            {
                builder.Append("FUNCTION_BLOCK ").AppendLine(QuoteIdentifier(sheetName));
            }
            else
            {
                builder.Append("FUNCTION ").Append(QuoteIdentifier(sheetName)).AppendLine(" : Void");
            }

            builder.AppendLine("{ S7_Optimized_Access := 'TRUE' }");
            builder.AppendLine("VERSION : 0.1");
        }

        private static void AppendDeclarations(
            StringBuilder builder,
            IEnumerable<BlockCallNode> blockOrder,
            IDictionary<Guid, BlockDefinition> definitions,
            IDictionary<string, string> temporaries,
            bool hasMultiInstances)
        {
            if (hasMultiInstances)
            {
                builder.AppendLine("VAR");
                foreach (var node in blockOrder.Where(item => item.UseMultiInstance))
                {
                    builder.Append("   ")
                        .Append(SanitizeIdentifier(node.InstanceName))
                        .Append(" : ")
                        .Append(QuoteIdentifier(definitions[node.BlockDefinitionId].Name))
                        .AppendLine(";");
                }

                builder.AppendLine("END_VAR");
            }

            if (temporaries.Count > 0)
            {
                builder.AppendLine("VAR_TEMP");
                foreach (var temporary in temporaries)
                {
                    builder.Append("   ")
                        .Append(temporary.Key)
                        .Append(" : ")
                        .Append(temporary.Value)
                        .AppendLine(";");
                }

                builder.AppendLine("END_VAR");
            }
        }

        private static void AppendCall(
            StringBuilder builder,
            CallSheet sheet,
            BlockCallNode node,
            BlockDefinition definition,
            IDictionary<Guid, CallNode> nodes,
            IDictionary<Guid, Pin> pins,
            IDictionary<Guid, TagDefinition> tags,
            IDictionary<Guid, OutputPlan> outputPlans,
            IList<DiagramIssue> issues)
        {
            if (!string.IsNullOrWhiteSpace(definition.Description) || !string.IsNullOrWhiteSpace(node.Title))
            {
                var comment = node.InstanceName + " : " + definition.Name;
                if (!string.IsNullOrWhiteSpace(definition.Description))
                {
                    comment += " — " + definition.Description;
                }

                AppendComment(builder, comment, "   ");
            }

            var arguments = new List<string>();
            foreach (var member in definition.Interface)
            {
                if (member.Section != InterfaceSection.Input &&
                    member.Section != InterfaceSection.InOut &&
                    member.Section != InterfaceSection.Output)
                {
                    continue;
                }

                var pin = node.Pins.FirstOrDefault(item => item.MemberId == member.Id);
                if (pin == null)
                {
                    continue;
                }

                if (member.Section == InterfaceSection.Output)
                {
                    OutputPlan output;
                    if (outputPlans.TryGetValue(pin.Id, out output))
                    {
                        arguments.Add(member.Name + " => " + output.Expression);
                    }

                    continue;
                }

                var incoming = sheet.Wires.FirstOrDefault(wire => wire.TargetPinId == pin.Id);
                if (incoming == null)
                {
                    if (member.Section == InterfaceSection.Input &&
                        !string.IsNullOrWhiteSpace(member.InitialValue))
                    {
                        arguments.Add(member.Name + " := " + member.InitialValue.Trim());
                    }

                    continue;
                }

                var expression = ResolveSourceExpression(
                    incoming,
                    nodes,
                    pins,
                    tags,
                    outputPlans,
                    issues,
                    "GEN020");
                if (!string.IsNullOrEmpty(expression))
                {
                    arguments.Add(member.Name + " := " + expression);
                }
            }

            var callExpression = definition.Kind == BlockKind.FunctionBlock
                ? (node.UseMultiInstance
                    ? "#" + SanitizeIdentifier(node.InstanceName)
                    : QuoteIdentifier(node.InstanceName))
                : QuoteIdentifier(definition.Name);

            var returnPin = node.Pins.FirstOrDefault(pin => pin.Role == PinRole.FunctionReturn);
            OutputPlan returnPlan;
            var prefix = returnPin != null && outputPlans.TryGetValue(returnPin.Id, out returnPlan)
                ? returnPlan.Expression + " := "
                : string.Empty;

            AppendInvocation(builder, prefix + callExpression, arguments);
        }

        private static string ResolveSourceExpression(
            Wire incoming,
            IDictionary<Guid, CallNode> nodes,
            IDictionary<Guid, Pin> pins,
            IDictionary<Guid, TagDefinition> tags,
            IDictionary<Guid, OutputPlan> outputPlans,
            IList<DiagramIssue> issues,
            string unresolvedIssueCode)
        {
            var sourceNode = nodes[incoming.SourceNodeId];
            var tag = sourceNode as TagNode;
            if (tag != null)
            {
                return QuoteIdentifier(tags[tag.TagDefinitionId].Name);
            }

            var constant = sourceNode as ConstantNode;
            if (constant != null)
            {
                return constant.Literal;
            }

            if (sourceNode is BlockCallNode || sourceNode is LogicNode)
            {
                OutputPlan plan;
                if (outputPlans.TryGetValue(incoming.SourcePinId, out plan))
                {
                    return plan.Expression;
                }
            }

            issues.Add(new DiagramIssue(
                DiagramIssueSeverity.Error,
                unresolvedIssueCode,
                "Cannot resolve an SCL expression for this wire source.",
                incoming.SourceNodeId,
                incoming.Id));
            return string.Empty;
        }

        private static void AppendInvocation(StringBuilder builder, string callExpression, IList<string> arguments)
        {
            builder.Append("   ").Append(callExpression);
            if (arguments.Count == 0)
            {
                builder.AppendLine("();");
                return;
            }

            builder.Append("(").Append(arguments[0]);
            var continuationIndent = new string(' ', 4 + callExpression.Length);
            for (var index = 1; index < arguments.Count; index++)
            {
                builder.AppendLine(",");
                builder.Append(continuationIndent).Append(arguments[index]);
            }

            builder.AppendLine(");");
        }

        private static void AppendComment(StringBuilder builder, string text, string indent)
        {
            var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var line in lines)
            {
                builder.Append(indent).Append("// ").AppendLine(line);
            }
        }

        private static string MakeUniqueTemporaryName(string candidate, ISet<string> usedNames)
        {
            var basis = SanitizeIdentifier(candidate);
            var unique = basis;
            var suffix = 2;
            while (!usedNames.Add(unique))
            {
                unique = basis + "_" + suffix++;
            }

            return unique;
        }

        private static string SanitizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "value";
            }

            var builder = new StringBuilder(value.Length + 1);
            foreach (var character in value)
            {
                builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
            }

            if (builder.Length == 0 || char.IsDigit(builder[0]))
            {
                builder.Insert(0, '_');
            }

            return builder.ToString();
        }

        private static string QuoteIdentifier(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }

        private static bool HasErrors(IEnumerable<DiagramIssue> issues)
        {
            return issues.Any(issue => issue.Severity == DiagramIssueSeverity.Error);
        }
    }
}
