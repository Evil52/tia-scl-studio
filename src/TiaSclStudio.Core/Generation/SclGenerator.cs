using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;

namespace TiaSclStudio.Core.Generation
{
    /// <summary>Generates TIA Portal external-source SCL without using Openness.</summary>
    public sealed class SclGenerator
    {
        private const string NewLine = "\r\n";
        private readonly ProjectValidator _validator;

        public SclGenerator()
            : this(new ProjectValidator())
        {
        }

        public SclGenerator(ProjectValidator validator)
        {
            if (validator == null)
            {
                throw new ArgumentNullException("validator");
            }

            _validator = validator;
        }

        public string GenerateBlock(BlockDefinition block)
        {
            return GenerateBlock(block, new UdtDefinition[0]);
        }

        /// <summary>
        /// Generates a block and canonicalizes every known UDT reference against
        /// the supplied project catalogue. Legacy unquoted references therefore
        /// leave the generator as TIA-safe quoted identifiers.
        /// </summary>
        public string GenerateBlock(
            BlockDefinition block,
            IEnumerable<UdtDefinition> dataTypes)
        {
            if (block == null)
            {
                throw new ArgumentNullException("block");
            }

            if (dataTypes == null)
            {
                throw new ArgumentNullException("dataTypes");
            }

            var typeCatalogue = Safe(dataTypes);

            var builder = new StringBuilder();
            AppendComment(builder, block.Description, string.Empty);

            if (block.Kind == BlockKind.FunctionBlock)
            {
                builder.Append("FUNCTION_BLOCK ").Append(QuoteGlobal(block.Name)).Append(NewLine);
            }
            else
            {
                builder.Append("FUNCTION ").Append(QuoteGlobal(block.Name)).Append(" : ")
                    .Append(NormalizeReturnType(block.ReturnType, typeCatalogue)).Append(NewLine);
            }

            AppendOptimizedAccess(builder, block.OptimizedAccess);
            builder.Append("VERSION : ").Append(NormalizeVersion(block.Version)).Append(NewLine);
            AppendInterface(builder, block.Interface, null, typeCatalogue);
            builder.Append("BEGIN").Append(NewLine);
            AppendBody(builder, block.SclBody);
            builder.Append(block.Kind == BlockKind.FunctionBlock ? "END_FUNCTION_BLOCK" : "END_FUNCTION")
                .Append(NewLine);
            return builder.ToString();
        }

        public string GenerateUdt(UdtDefinition dataType)
        {
            return GenerateUdt(dataType, new[] { dataType });
        }

        private static string GenerateUdt(
            UdtDefinition dataType,
            IEnumerable<UdtDefinition> dataTypes)
        {
            if (dataType == null)
            {
                throw new ArgumentNullException("dataType");
            }

            var typeCatalogue = Safe(dataTypes);

            var builder = new StringBuilder();
            AppendComment(builder, dataType.Comment, string.Empty);
            builder.Append("TYPE ").Append(QuoteGlobal(dataType.Name)).Append(NewLine);
            builder.Append("VERSION : ").Append(NormalizeVersion(dataType.Version)).Append(NewLine);
            builder.Append("   STRUCT").Append(NewLine);
            foreach (var member in Safe(dataType.Members))
            {
                AppendDeclaration(
                    builder,
                    member.Name,
                    CanonicalizeDataType(member.DataType, typeCatalogue, false),
                    member.InitialValue,
                    member.Comment,
                    "      ");
            }

            builder.Append("   END_STRUCT;").Append(NewLine);
            builder.Append("END_TYPE").Append(NewLine);
            return builder.ToString();
        }

        public string GenerateUdts(IEnumerable<UdtDefinition> dataTypes)
        {
            if (dataTypes == null)
            {
                throw new ArgumentNullException("dataTypes");
            }

            var typeCatalogue = Safe(dataTypes);
            return JoinSources(
                PlcDataTypes.OrderUdtsByDependency(typeCatalogue)
                    .Select(dataType => GenerateUdt(dataType, typeCatalogue)));
        }

        public string GenerateInstanceDb(UnitDefinition unit, BlockDefinition block)
        {
            if (unit == null)
            {
                throw new ArgumentNullException("unit");
            }

            if (block == null)
            {
                throw new ArgumentNullException("block");
            }

            if (block.Kind != BlockKind.FunctionBlock)
            {
                throw new InvalidOperationException("Only an FB can have an instance data block.");
            }

            if (unit.UseMultiInstance)
            {
                throw new InvalidOperationException("A multi-instance is stored in the parent FB and has no global DB.");
            }

            return GenerateInstanceDbText(unit.Name, block.Name, unit.Comment, block.OptimizedAccess);
        }

        public string GenerateInstanceDbs(PlantProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            var sources = new List<string>();
            foreach (var callBlock in Safe(project.CallBlocks))
            {
                foreach (var unit in Safe(callBlock.Units))
                {
                    var block = ResolveBlock(project.Blocks, unit);
                    if (block.Kind == BlockKind.FunctionBlock && !unit.UseMultiInstance)
                    {
                        sources.Add(GenerateInstanceDb(unit, block));
                    }
                }
            }

            return JoinSources(sources);
        }

        public string GenerateGlobalDataBlocks(PlantProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            var builder = new StringBuilder();
            foreach (var group in Safe(project.DataBlockVariables)
                .Where(item => item.GenerateDataBlock)
                .GroupBy(item => item.DataBlockName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (builder.Length > 0)
                {
                    builder.Append(NewLine);
                }

                builder.Append("DATA_BLOCK ").Append(QuoteGlobal(group.Key)).Append(NewLine);
                AppendOptimizedAccess(builder, true);
                builder.Append("VERSION : 0.1").Append(NewLine);
                builder.Append("   VAR").Append(NewLine);
                foreach (var variable in group.OrderBy(item => item.VariableName, StringComparer.OrdinalIgnoreCase))
                {
                    AppendDeclaration(
                        builder,
                        variable.VariableName,
                        CanonicalizeDataType(variable.DataType, project.DataTypes, false),
                        string.Empty,
                        variable.Comment,
                        "      ");
                }

                builder.Append("   END_VAR").Append(NewLine);
                builder.Append("BEGIN").Append(NewLine);
                builder.Append("END_DATA_BLOCK").Append(NewLine);
            }

            return builder.ToString();
        }

        public string GenerateCallBlock(CallBlockDefinition callBlock, IEnumerable<BlockDefinition> blocks)
        {
            return GenerateCallBlock(callBlock, blocks, new UdtDefinition[0]);
        }

        public string GenerateCallBlock(
            CallBlockDefinition callBlock,
            IEnumerable<BlockDefinition> blocks,
            IEnumerable<UdtDefinition> dataTypes)
        {
            if (callBlock == null)
            {
                throw new ArgumentNullException("callBlock");
            }

            if (blocks == null)
            {
                throw new ArgumentNullException("blocks");
            }

            if (dataTypes == null)
            {
                throw new ArgumentNullException("dataTypes");
            }

            var blockList = blocks.ToList();
            var typeCatalogue = Safe(dataTypes);
            var builder = new StringBuilder();
            AppendComment(builder, callBlock.Description, string.Empty);

            if (callBlock.Kind == BlockKind.FunctionBlock)
            {
                builder.Append("FUNCTION_BLOCK ").Append(QuoteGlobal(callBlock.Name)).Append(NewLine);
            }
            else
            {
                builder.Append("FUNCTION ").Append(QuoteGlobal(callBlock.Name)).Append(" : ")
                    .Append(NormalizeReturnType(callBlock.ReturnType, typeCatalogue)).Append(NewLine);
            }

            AppendOptimizedAccess(builder, callBlock.OptimizedAccess);
            builder.Append("VERSION : ").Append(NormalizeVersion(callBlock.Version)).Append(NewLine);

            var multiInstances = new List<InstanceDeclaration>();
            foreach (var unit in Safe(callBlock.Units))
            {
                if (!unit.UseMultiInstance)
                {
                    continue;
                }

                var targetBlock = ResolveBlock(blockList, unit);
                multiInstances.Add(new InstanceDeclaration(unit.Name, targetBlock.Name, unit.Comment));
            }

            AppendInterface(builder, callBlock.Interface, multiInstances, typeCatalogue);
            builder.Append("BEGIN").Append(NewLine);

            foreach (var unit in OrderedUnits(callBlock.Units))
            {
                var targetBlock = ResolveBlock(blockList, unit);
                AppendUnitCall(builder, unit, targetBlock);
            }

            builder.Append(callBlock.Kind == BlockKind.FunctionBlock ? "END_FUNCTION_BLOCK" : "END_FUNCTION")
                .Append(NewLine);
            return builder.ToString();
        }

        public string GenerateCallBlock(CallBlockDefinition callBlock, PlantProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            return GenerateCallBlock(callBlock, project.Blocks, project.DataTypes);
        }

        public string GenerateCallBlockInstanceDb(CallBlockDefinition callBlock)
        {
            if (callBlock == null)
            {
                throw new ArgumentNullException("callBlock");
            }

            if (callBlock.Kind != BlockKind.FunctionBlock)
            {
                throw new InvalidOperationException("Only an FB call block can have an instance data block.");
            }

            var dbName = string.IsNullOrWhiteSpace(callBlock.InstanceDbName)
                ? callBlock.Name + "_DB"
                : callBlock.InstanceDbName.Trim();
            return GenerateInstanceDbText(dbName, callBlock.Name, callBlock.Description, callBlock.OptimizedAccess);
        }

        /// <summary>Validates and generates sources in the order required for TIA import.</summary>
        public IList<GeneratedSource> GenerateProject(PlantProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            var validation = _validator.Validate(project);
            if (!validation.IsValid)
            {
                throw new ModelValidationException(validation);
            }

            var result = new List<GeneratedSource>();
            var order = 0;

            if (Safe(project.DataTypes).Count > 0)
            {
                result.Add(new GeneratedSource(
                    "00_Types.scl",
                    GenerateUdts(project.DataTypes),
                    GeneratedSourceKind.DataTypes,
                    order++));
            }

            var globalDataBlocks = GenerateGlobalDataBlocks(project);
            if (!string.IsNullOrWhiteSpace(globalDataBlocks))
            {
                result.Add(new GeneratedSource(
                    "10_GlobalDataBlocks.scl",
                    globalDataBlocks,
                    GeneratedSourceKind.GlobalDataBlocks,
                    order++));
            }

            foreach (var block in Safe(project.Blocks))
            {
                if (block.ImportedInterfaceOnly)
                {
                    continue;
                }

                result.Add(new GeneratedSource(
                    block.Name + ".scl",
                    GenerateBlock(block, project.DataTypes),
                    GeneratedSourceKind.Block,
                    order++));
            }

            var instanceSource = GenerateInstanceDbs(project);
            if (!string.IsNullOrWhiteSpace(instanceSource))
            {
                result.Add(new GeneratedSource(
                    "90_Instances.scl",
                    instanceSource,
                    GeneratedSourceKind.InstanceDataBlocks,
                    order++));
            }

            foreach (var callBlock in Safe(project.CallBlocks))
            {
                result.Add(new GeneratedSource(
                    "99_" + callBlock.Name + ".scl",
                    GenerateCallBlock(callBlock, project.Blocks, project.DataTypes),
                    GeneratedSourceKind.CallBlock,
                    order++));

                if (callBlock.Kind == BlockKind.FunctionBlock)
                {
                    var dbName = string.IsNullOrWhiteSpace(callBlock.InstanceDbName)
                        ? callBlock.Name + "_DB"
                        : callBlock.InstanceDbName.Trim();
                    result.Add(new GeneratedSource(
                        "99z_" + dbName + ".scl",
                        GenerateCallBlockInstanceDb(callBlock),
                        GeneratedSourceKind.CallBlockInstanceDataBlock,
                        order++));
                }
            }

            return result;
        }

        private static void AppendUnitCall(StringBuilder builder, UnitDefinition unit, BlockDefinition block)
        {
            var description = string.IsNullOrWhiteSpace(unit.Comment) ? block.Description : unit.Comment;
            builder.Append("   // ---- ").Append(SingleLine(unit.Name)).Append(" : ")
                .Append(SingleLine(block.Name));
            if (!string.IsNullOrWhiteSpace(description))
            {
                builder.Append(" - ").Append(SingleLine(description));
            }

            builder.Append(" ----").Append(NewLine);

            var arguments = BuildArguments(unit, block);
            string invocation;
            if (block.Kind == BlockKind.FunctionBlock)
            {
                invocation = unit.UseMultiInstance ? "#" + unit.Name : QuoteGlobal(unit.Name);
            }
            else
            {
                invocation = QuoteGlobal(block.Name);
            }

            var returnsValue = block.Kind == BlockKind.Function && !IsVoid(block.ReturnType);
            if (returnsValue && string.IsNullOrWhiteSpace(unit.ResultTarget))
            {
                throw new InvalidOperationException(
                    "Non-Void FC unit '" + unit.Name + "' requires a result target.");
            }

            var head = returnsValue ? SingleLine(unit.ResultTarget) + " := " + invocation : invocation;
            builder.Append("   ").Append(head).Append("(");
            if (arguments.Count == 0)
            {
                builder.Append(");").Append(NewLine);
                return;
            }

            builder.Append(arguments[0]);
            var continuationIndent = new string(' ', 4 + head.Length);
            for (var index = 1; index < arguments.Count; index++)
            {
                builder.Append(",").Append(NewLine).Append(continuationIndent).Append(arguments[index]);
            }

            builder.Append(");").Append(NewLine);
        }

        private static List<string> BuildArguments(UnitDefinition unit, BlockDefinition block)
        {
            var arguments = new List<string>();
            var bindings = Safe(unit.Bindings);
            foreach (var parameter in Safe(block.Interface))
            {
                if (parameter.Section != InterfaceSection.Input &&
                    parameter.Section != InterfaceSection.Output &&
                    parameter.Section != InterfaceSection.InOut)
                {
                    continue;
                }

                var binding = ResolveBinding(bindings, parameter);
                var expression = binding == null ? string.Empty : binding.Expression;
                if (string.IsNullOrWhiteSpace(expression) && parameter.Section == InterfaceSection.Input)
                {
                    expression = parameter.InitialValue;
                }

                if (string.IsNullOrWhiteSpace(expression))
                {
                    if (parameter.Section == InterfaceSection.InOut)
                    {
                        throw new InvalidOperationException(
                            "InOut parameter '" + parameter.Name + "' of unit '" + unit.Name + "' is not bound.");
                    }

                    continue;
                }

                var assignment = parameter.Section == InterfaceSection.Output ? " => " : " := ";
                arguments.Add(SingleLine(parameter.Name) + assignment + SingleLine(expression));
            }

            return arguments;
        }

        private static ParameterBinding ResolveBinding(IEnumerable<ParameterBinding> bindings, InterfaceMember parameter)
        {
            if (parameter.Id != Guid.Empty)
            {
                var byId = bindings.FirstOrDefault(candidate => candidate != null && candidate.ParameterId == parameter.Id);
                if (byId != null)
                {
                    return byId;
                }
            }

            return bindings.FirstOrDefault(candidate =>
                candidate != null &&
                string.Equals(candidate.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase));
        }

        private static BlockDefinition ResolveBlock(IEnumerable<BlockDefinition> blocks, UnitDefinition unit)
        {
            if (unit == null)
            {
                throw new ArgumentNullException("unit");
            }

            var candidates = Safe(blocks);
            if (unit.BlockId != Guid.Empty)
            {
                var byId = candidates.FirstOrDefault(block => block != null && block.Id == unit.BlockId);
                if (byId != null)
                {
                    return byId;
                }
            }

            var byName = candidates.FirstOrDefault(block =>
                block != null &&
                string.Equals(block.Name, unit.BlockName, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
            {
                return byName;
            }

            throw new InvalidOperationException(
                "Block for unit '" + unit.Name + "' was not found (BlockId " + unit.BlockId + ").");
        }

        private static void AppendInterface(
            StringBuilder builder,
            IEnumerable<InterfaceMember> members,
            IEnumerable<InstanceDeclaration> instances,
            IEnumerable<UdtDefinition> dataTypes)
        {
            var memberList = Safe(members);
            var typeCatalogue = Safe(dataTypes);
            AppendSection(builder, "VAR_INPUT", memberList.Where(item => item.Section == InterfaceSection.Input), null, typeCatalogue);
            AppendSection(builder, "VAR_OUTPUT", memberList.Where(item => item.Section == InterfaceSection.Output), null, typeCatalogue);
            AppendSection(builder, "VAR_IN_OUT", memberList.Where(item => item.Section == InterfaceSection.InOut), null, typeCatalogue);
            AppendSection(builder, "VAR", memberList.Where(item => item.Section == InterfaceSection.Static), instances, typeCatalogue);
            AppendSection(builder, "VAR_TEMP", memberList.Where(item => item.Section == InterfaceSection.Temp), null, typeCatalogue);
            AppendSection(builder, "VAR CONSTANT", memberList.Where(item => item.Section == InterfaceSection.Constant), null, typeCatalogue);
        }

        private static void AppendSection(
            StringBuilder builder,
            string header,
            IEnumerable<InterfaceMember> members,
            IEnumerable<InstanceDeclaration> instances,
            IEnumerable<UdtDefinition> dataTypes)
        {
            var memberList = Safe(members);
            var instanceList = Safe(instances);
            if (memberList.Count == 0 && instanceList.Count == 0)
            {
                return;
            }

            builder.Append(header).Append(NewLine);
            foreach (var member in memberList)
            {
                AppendDeclaration(
                    builder,
                    member.Name,
                    CanonicalizeDataType(member.DataType, dataTypes, false),
                    member.InitialValue,
                    member.Comment,
                    "   ");
            }

            foreach (var instance in instanceList)
            {
                AppendDeclaration(builder, instance.Name, QuoteGlobal(instance.BlockName), string.Empty, instance.Comment, "   ");
            }

            builder.Append("END_VAR").Append(NewLine);
        }

        private static void AppendDeclaration(
            StringBuilder builder,
            string name,
            string dataType,
            string initialValue,
            string comment,
            string indent)
        {
            // Every part of a declaration has to stay on this one line. A line
            // break inside a data type or an initial value would end the
            // declaration and leave the rest of the interface as loose text.
            builder.Append(indent).Append(SingleLine(name)).Append(" : ").Append(SingleLine(dataType));
            if (!string.IsNullOrWhiteSpace(initialValue))
            {
                builder.Append(" := ").Append(SingleLine(initialValue));
            }

            builder.Append(";");
            if (!string.IsNullOrWhiteSpace(comment))
            {
                builder.Append(" // ").Append(SingleLine(comment));
            }

            builder.Append(NewLine);
        }

        private static void AppendOptimizedAccess(StringBuilder builder, bool optimizedAccess)
        {
            builder.Append("{ S7_Optimized_Access := '")
                .Append(optimizedAccess ? "TRUE" : "FALSE")
                .Append("' }")
                .Append(NewLine);
        }

        private static void AppendBody(StringBuilder builder, string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            var normalized = body.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');
            foreach (var line in normalized.Split('\n'))
            {
                builder.Append("   ").Append(line.TrimEnd()).Append(NewLine);
            }
        }

        private static void AppendComment(StringBuilder builder, string comment, string indent)
        {
            if (string.IsNullOrWhiteSpace(comment))
            {
                return;
            }

            var normalized = comment.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var line in normalized.Split('\n'))
            {
                builder.Append(indent).Append("// ").Append(line).Append(NewLine);
            }
        }

        private static string GenerateInstanceDbText(
            string dbName,
            string blockName,
            string comment,
            bool optimizedAccess)
        {
            var builder = new StringBuilder();
            AppendComment(builder, comment, string.Empty);
            builder.Append("DATA_BLOCK ").Append(QuoteGlobal(dbName)).Append(NewLine);
            AppendOptimizedAccess(builder, optimizedAccess);
            builder.Append("VERSION : 0.1").Append(NewLine);
            builder.Append("NON_RETAIN").Append(NewLine);
            builder.Append(QuoteGlobal(blockName)).Append(NewLine);
            builder.Append("BEGIN").Append(NewLine);
            builder.Append("END_DATA_BLOCK").Append(NewLine);
            return builder.ToString();
        }

        private static IEnumerable<UnitDefinition> OrderedUnits(IEnumerable<UnitDefinition> units)
        {
            return Safe(units)
                .Select((unit, index) => new { Unit = unit, Index = index })
                .OrderBy(item => item.Unit.ManualOrder ?? int.MaxValue)
                .ThenBy(item => item.Index)
                .Select(item => item.Unit);
        }

        private static string JoinSources(IEnumerable<string> sources)
        {
            return string.Join(NewLine, sources.Where(source => !string.IsNullOrWhiteSpace(source)));
        }

        private static string QuoteGlobal(string name)
        {
            return "\"" + (name ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }

        private static string NormalizeReturnType(
            string returnType,
            IEnumerable<UdtDefinition> dataTypes)
        {
            return string.IsNullOrWhiteSpace(returnType)
                ? "Void"
                : CanonicalizeDataType(returnType, dataTypes, true);
        }

        private static string CanonicalizeDataType(
            string dataType,
            IEnumerable<UdtDefinition> dataTypes,
            bool allowVoid)
        {
            string canonical;
            Guid udtId;
            return PlcDataTypes.TryResolve(
                dataType,
                dataTypes,
                allowVoid,
                out canonical,
                out udtId)
                ? canonical
                : SingleLine(dataType);
        }

        private static string NormalizeVersion(string version)
        {
            return string.IsNullOrWhiteSpace(version) ? "0.1" : SingleLine(version);
        }

        private static bool IsVoid(string returnType)
        {
            return string.IsNullOrWhiteSpace(returnType) ||
                   string.Equals(returnType.Trim(), "Void", StringComparison.OrdinalIgnoreCase);
        }

        private static string SingleLine(string value)
        {
            return SclText.SingleLine(value);
        }

        // Constrained to reference types: the null filter below is only
        // meaningful for them, and an unconstrained T would silently keep every
        // element of a value-type sequence.
        private static List<T> Safe<T>(IEnumerable<T> items)
            where T : class
        {
            return items == null ? new List<T>() : items.Where(item => item != null).ToList();
        }

        private sealed class InstanceDeclaration
        {
            public InstanceDeclaration(string name, string blockName, string comment)
            {
                Name = name;
                BlockName = blockName;
                Comment = comment;
            }

            public string Name { get; private set; }

            public string BlockName { get; private set; }

            public string Comment { get; private set; }
        }
    }
}
