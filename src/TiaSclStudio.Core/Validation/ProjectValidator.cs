using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TiaSclStudio.Core.Model;

namespace TiaSclStudio.Core.Validation
{
    public sealed class ProjectValidator
    {
        private static readonly Regex VersionPattern =
            new Regex("^[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant, SclRegex.MatchTimeout);

        public ValidationResult Validate(PlantProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            var result = new ValidationResult();
            var ids = new Dictionary<Guid, string>();
            var globalSymbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            ValidateId(project.Id, "Project", ids, result);
            ValidateName(project.Name, "Project.Name", result);
            if (!Enum.IsDefined(typeof(PlcCpuFamily), project.CpuFamily))
            {
                result.Add(
                    ValidationSeverity.Error,
                    "INVALID_CPU_FAMILY",
                    "The target PLC CPU family is not supported.",
                    "Project.CpuFamily");
            }

            // Skipping a hole silently would report the model as valid and then
            // let the generator dereference it. Generation is the only consumer
            // that matters here, so a missing collection or an empty entry has
            // to be an error rather than something quietly filtered out.
            ValidateCollection(project.Blocks, "Project.Blocks", result);
            ValidateCollection(project.DataTypes, "Project.DataTypes", result);
            ValidateCollection(project.Tags, "Project.Tags", result);
            ValidateCollection(project.DataBlockVariables, "Project.DataBlockVariables", result);
            ValidateCollection(project.CallBlocks, "Project.CallBlocks", result);

            var blocks = Safe(project.Blocks);
            var dataTypes = Safe(project.DataTypes);
            var tags = Safe(project.Tags);
            var dataBlockVariables = Safe(project.DataBlockVariables);
            var callBlocks = Safe(project.CallBlocks);

            RegisterDuplicateNames(blocks.Select(block => block.Name), "Blocks", result);
            RegisterDuplicateNames(dataTypes.Select(dataType => dataType.Name), "DataTypes", result);
            RegisterDuplicateNames(tags.Select(tag => tag.Name), "Tags", result);
            RegisterDuplicateNames(callBlocks.Select(callBlock => callBlock.Name), "CallBlocks", result);

            foreach (var block in blocks)
            {
                var path = "Blocks[" + Display(block.Name) + "]";
                ValidateId(block.Id, path, ids, result);
                ValidateName(block.Name, path + ".Name", result);
                RegisterGlobalSymbol(globalSymbols, block.Name, path, result);
                ValidateVersion(block.Version, path + ".Version", result);
                ValidatePersistableText(block.Description, path + ".Description", "The block description", result);
                ValidatePersistableText(block.SclBody, path + ".SclBody", "The block body", result);
                ValidateCollection(block.Interface, path + ".Interface", result);
                ValidateBlockContract(
                    block.Kind,
                    block.ReturnType,
                    block.Interface,
                    dataTypes,
                    block.ImportedInterfaceOnly,
                    path,
                    ids,
                    result);
            }

            foreach (var dataType in dataTypes)
            {
                var path = "DataTypes[" + Display(dataType.Name) + "]";
                ValidateId(dataType.Id, path, ids, result);
                ValidateName(dataType.Name, path + ".Name", result);
                RegisterGlobalSymbol(globalSymbols, dataType.Name, path, result);
                ValidateVersion(dataType.Version, path + ".Version", result);
                ValidatePersistableText(dataType.Comment, path + ".Comment", "The data-type comment", result);
                ValidateCollection(dataType.Members, path + ".Members", result);
                ValidateUdtMembers(dataType.Members, dataTypes, path + ".Members", ids, result);
            }

            ValidateUdtDependencyGraph(dataTypes, result);

            var addressedTags = new List<AddressedTag>();
            foreach (var tag in tags)
            {
                var path = "Tags[" + Display(tag.Name) + "]";
                ValidateId(tag.Id, path, ids, result);
                ValidateName(tag.Name, path + ".Name", result);
                RegisterGlobalSymbol(globalSymbols, tag.Name, path, result);
                ValidateDataType(tag.DataType, dataTypes, false, path + ".DataType", result);
                ValidatePersistableText(tag.Comment, path + ".Comment", "The tag comment", result);
                if (string.IsNullOrWhiteSpace(tag.Address))
                {
                    result.Add(ValidationSeverity.Error, "TAG_ADDRESS_REQUIRED", "A PLC tag address is required.", path + ".Address");
                }
                else if (Enum.IsDefined(typeof(PlcCpuFamily), project.CpuFamily))
                {
                    PlcAddressSpec addressSpec;
                    string canonicalAddress;
                    string addressError;
                    if (!PlcAddressing.TryParse(
                        project.CpuFamily,
                        tag.Address,
                        tag.DataType,
                        out addressSpec,
                        out canonicalAddress,
                        out addressError))
                    {
                        result.Add(
                            ValidationSeverity.Error,
                            "TAG_ADDRESS_INVALID",
                            addressError,
                            path + ".Address");
                    }
                    else
                    {
                        addressedTags.Add(new AddressedTag(tag, path, canonicalAddress, addressSpec));
                    }
                }
            }

            ValidateAddressOverlaps(addressedTags, result);

            foreach (var group in dataBlockVariables.GroupBy(
                item => item.DataBlockName ?? string.Empty,
                StringComparer.OrdinalIgnoreCase))
            {
                var ownedStates = group.Select(item => item.GenerateDataBlock).Distinct().ToList();
                if (ownedStates.Count > 1)
                {
                    result.Add(
                        ValidationSeverity.Error,
                        "DB_OWNERSHIP_MIXED",
                        "One global DB cannot mix generated and reference-only variables.",
                        "DataBlockVariables[" + Display(group.Key) + "]");
                }

                if (group.Any(item => item.GenerateDataBlock))
                {
                    RegisterGlobalSymbol(
                        globalSymbols,
                        group.Key,
                        "DataBlockVariables[" + Display(group.Key) + "].DataBlockName",
                        result);
                }

                foreach (var duplicate in group
                    .GroupBy(item => item.VariableName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Where(items => items.Count() > 1))
                {
                    result.Add(
                        ValidationSeverity.Error,
                        "DUPLICATE_DB_VARIABLE",
                        "The global DB variable name is duplicated.",
                        "DataBlockVariables[" + Display(group.Key) + "]." + Display(duplicate.Key));
                }
            }

            foreach (var variable in dataBlockVariables)
            {
                var path = "DataBlockVariables[" + Display(variable.DataBlockName) + "." +
                    Display(variable.VariableName) + "]";
                ValidateId(variable.Id, path, ids, result);
                ValidateName(variable.DataBlockName, path + ".DataBlockName", result);
                ValidateName(variable.VariableName, path + ".VariableName", result);
                ValidateDataType(variable.DataType, dataTypes, false, path + ".DataType", result);
                ValidatePersistableText(variable.Comment, path + ".Comment", "The DB variable comment", result);

                string canonicalType;
                Guid udtId;
                if (PlcDataTypes.TryResolve(
                        variable.DataType,
                        dataTypes,
                        false,
                        out canonicalType,
                        out udtId) &&
                    udtId == Guid.Empty)
                {
                    result.Add(
                        ValidationSeverity.Error,
                        "DB_VARIABLE_UDT_REQUIRED",
                        "A visual DB variable must use a declared UDT.",
                        path + ".DataType");
                }
            }

            foreach (var callBlock in callBlocks)
            {
                var path = "CallBlocks[" + Display(callBlock.Name) + "]";
                ValidateId(callBlock.Id, path, ids, result);
                ValidateName(callBlock.Name, path + ".Name", result);
                RegisterGlobalSymbol(globalSymbols, callBlock.Name, path, result);
                ValidateVersion(callBlock.Version, path + ".Version", result);
                ValidatePersistableText(callBlock.Description, path + ".Description", "The call-block description", result);
                ValidateCollection(callBlock.Interface, path + ".Interface", result);
                ValidateBlockContract(
                    callBlock.Kind,
                    callBlock.ReturnType,
                    callBlock.Interface,
                    dataTypes,
                    false,
                    path,
                    ids,
                    result);
                ValidateCollection(callBlock.Units, path + ".Units", result);

                if (callBlock.Kind == BlockKind.FunctionBlock)
                {
                    var dbName = string.IsNullOrWhiteSpace(callBlock.InstanceDbName)
                        ? callBlock.Name + "_DB"
                        : callBlock.InstanceDbName.Trim();
                    ValidateName(dbName, path + ".InstanceDbName", result);
                    RegisterGlobalSymbol(globalSymbols, dbName, path + ".InstanceDbName", result);
                }
                else if (!string.IsNullOrWhiteSpace(callBlock.InstanceDbName))
                {
                    result.Add(
                        ValidationSeverity.Warning,
                        "FC_INSTANCE_DB_IGNORED",
                        "An FC has no instance DB; InstanceDbName is ignored.",
                        path + ".InstanceDbName");
                }

                ValidateUnits(callBlock, blocks, path, ids, globalSymbols, result);
            }

            return result;
        }

        public void ValidateOrThrow(PlantProject project)
        {
            var result = Validate(project);
            if (!result.IsValid)
            {
                throw new ModelValidationException(result);
            }
        }

        private static void ValidateBlockContract(
            BlockKind kind,
            string returnType,
            IEnumerable<InterfaceMember> members,
            IEnumerable<UdtDefinition> dataTypes,
            bool importedInterfaceOnly,
            string path,
            IDictionary<Guid, string> ids,
            ValidationResult result)
        {
            if (!Enum.IsDefined(typeof(BlockKind), kind))
            {
                result.Add(ValidationSeverity.Error, "INVALID_BLOCK_KIND", "Block kind is not supported.", path + ".Kind");
            }

            if (kind == BlockKind.Function && string.IsNullOrWhiteSpace(returnType))
            {
                result.Add(ValidationSeverity.Error, "RETURN_TYPE_REQUIRED", "An FC return type is required; use Void for no value.", path + ".ReturnType");
            }

            // The return type is written into the FUNCTION header line itself.
            ValidateSingleLine(returnType, path + ".ReturnType", "A return type", result);
            if (kind == BlockKind.Function && !string.IsNullOrWhiteSpace(returnType))
            {
                ValidateDataType(returnType, dataTypes, true, path + ".ReturnType", result);
            }

            if (kind == BlockKind.FunctionBlock &&
                !string.IsNullOrWhiteSpace(returnType) &&
                !string.Equals(returnType.Trim(), "Void", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(ValidationSeverity.Warning, "FB_RETURN_TYPE_IGNORED", "An FB does not have a return value.", path + ".ReturnType");
            }

            var memberList = Safe(members);
            RegisterDuplicateNames(memberList.Select(member => member.Name), path + ".Interface", result);
            foreach (var member in memberList)
            {
                var memberPath = path + ".Interface[" + Display(member.Name) + "]";
                ValidateId(member.Id, memberPath, ids, result);
                ValidateName(member.Name, memberPath + ".Name", result);
                string systemInstanceType;
                var importedSystemInstance = importedInterfaceOnly &&
                    kind == BlockKind.FunctionBlock &&
                    member.Section == InterfaceSection.Static &&
                    PlcDataTypes.TryResolveSystemBlockInstanceType(
                        member.DataType,
                        out systemInstanceType);
                if (!importedSystemInstance)
                {
                    ValidateDataType(member.DataType, dataTypes, false, memberPath + ".DataType", result);
                }
                ValidateSingleLine(
                    member.InitialValue,
                    memberPath + ".InitialValue",
                    "An initial value",
                    result);
                ValidatePersistableText(member.Comment, memberPath + ".Comment", "The member comment", result);

                if (!Enum.IsDefined(typeof(InterfaceSection), member.Section))
                {
                    result.Add(ValidationSeverity.Error, "INVALID_INTERFACE_SECTION", "Interface section is not supported.", memberPath + ".Section");
                }

                if (kind == BlockKind.Function && member.Section == InterfaceSection.Static)
                {
                    result.Add(ValidationSeverity.Error, "FC_STATIC_NOT_ALLOWED", "Static variables require an FB.", memberPath + ".Section");
                }
            }
        }

        private static void ValidateUdtMembers(
            IEnumerable<UdtMember> members,
            IEnumerable<UdtDefinition> dataTypes,
            string path,
            IDictionary<Guid, string> ids,
            ValidationResult result)
        {
            var memberList = Safe(members);
            if (memberList.Count == 0)
            {
                result.Add(ValidationSeverity.Warning, "EMPTY_UDT", "The UDT has no members.", path);
            }

            RegisterDuplicateNames(memberList.Select(member => member.Name), path, result);
            foreach (var member in memberList)
            {
                var memberPath = path + "[" + Display(member.Name) + "]";
                ValidateId(member.Id, memberPath, ids, result);
                ValidateName(member.Name, memberPath + ".Name", result);
                ValidateDataType(member.DataType, dataTypes, false, memberPath + ".DataType", result);
                ValidateSingleLine(
                    member.InitialValue,
                    memberPath + ".InitialValue",
                    "An initial value",
                    result);
                ValidatePersistableText(member.Comment, memberPath + ".Comment", "The member comment", result);
            }
        }

        private static void ValidateUnits(
            CallBlockDefinition callBlock,
            IList<BlockDefinition> blocks,
            string callBlockPath,
            IDictionary<Guid, string> ids,
            IDictionary<string, string> globalSymbols,
            ValidationResult result)
        {
            var units = Safe(callBlock.Units);
            RegisterDuplicateNames(units.Select(unit => unit.Name), callBlockPath + ".Units", result);
            var interfaceNames = new HashSet<string>(
                Safe(callBlock.Interface)
                    .Where(member => !string.IsNullOrWhiteSpace(member.Name))
                    .Select(member => member.Name.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var manualOrders = units
                .Where(unit => unit.ManualOrder.HasValue)
                .GroupBy(unit => unit.ManualOrder.Value)
                .Where(group => group.Count() > 1);
            foreach (var group in manualOrders)
            {
                result.Add(
                    ValidationSeverity.Warning,
                    "DUPLICATE_MANUAL_ORDER",
                    "Several units use manual order " + group.Key + "; their list order will break the tie.",
                    callBlockPath + ".Units");
            }

            foreach (var unit in units)
            {
                var path = callBlockPath + ".Units[" + Display(unit.Name) + "]";
                ValidateId(unit.Id, path, ids, result);
                ValidateName(unit.Name, path + ".Name", result);
                ValidatePersistableText(unit.Comment, path + ".Comment", "The unit comment", result);

                BlockDefinition block;
                var referenceKind = ResolveBlock(blocks, unit, out block);
                if (block == null)
                {
                    result.Add(
                        ValidationSeverity.Error,
                        "BLOCK_REFERENCE_NOT_FOUND",
                        "The referenced block cannot be found by BlockId or BlockName.",
                        path + ".BlockId");
                    ValidateBindingIds(unit.Bindings, path, ids, result);
                    continue;
                }

                if (referenceKind == ReferenceKind.NameFallback)
                {
                    result.Add(
                        ValidationSeverity.Warning,
                        unit.BlockId == Guid.Empty ? "LEGACY_BLOCK_REFERENCE" : "STALE_BLOCK_ID",
                        "The block was resolved by name. Save the model after calling UnitDefinition.BindTo to restore its stable ID reference.",
                        path + ".BlockId");
                }

                if (unit.UseMultiInstance && block.Kind != BlockKind.FunctionBlock)
                {
                    result.Add(ValidationSeverity.Error, "FC_MULTI_INSTANCE", "Only an FB can be a multi-instance.", path + ".UseMultiInstance");
                }

                if (unit.UseMultiInstance && callBlock.Kind != BlockKind.FunctionBlock)
                {
                    result.Add(ValidationSeverity.Error, "MULTI_INSTANCE_REQUIRES_FB", "A call block containing multi-instances must be an FB.", path + ".UseMultiInstance");
                }

                if (unit.UseMultiInstance && interfaceNames.Contains((unit.Name ?? string.Empty).Trim()))
                {
                    result.Add(
                        ValidationSeverity.Error,
                        "LOCAL_NAME_COLLISION",
                        "Multi-instance name '" + unit.Name + "' collides with a call-block interface member.",
                        path + ".Name");
                }

                if (block.Kind == BlockKind.FunctionBlock && !unit.UseMultiInstance)
                {
                    RegisterGlobalSymbol(globalSymbols, unit.Name, path, result);
                }

                ValidateBindings(unit, block, path, ids, result);
                ValidateCollection(unit.Bindings, path + ".Bindings", result);
                ValidateFunctionResult(unit, block, path, result);
            }
        }

        private static void ValidateBindings(
            UnitDefinition unit,
            BlockDefinition block,
            string path,
            IDictionary<Guid, string> ids,
            ValidationResult result)
        {
            var bindings = Safe(unit.Bindings);
            var resolvedParameters = new Dictionary<Guid, string>();
            var resolvedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var binding in bindings)
            {
                var bindingPath = path + ".Bindings[" + Display(binding.ParameterName) + "]";
                ValidateId(binding.Id, bindingPath, ids, result);
                ValidateSingleLine(
                    binding.Expression,
                    bindingPath + ".Expression",
                    "A binding expression",
                    result);

                InterfaceMember parameter;
                var referenceKind = ResolveParameter(block.Interface, binding, out parameter);
                if (parameter == null)
                {
                    result.Add(
                        ValidationSeverity.Error,
                        "PARAMETER_REFERENCE_NOT_FOUND",
                        "The referenced interface member cannot be found by ParameterId or ParameterName.",
                        bindingPath);
                    continue;
                }

                if (referenceKind == ReferenceKind.NameFallback)
                {
                    result.Add(
                        ValidationSeverity.Warning,
                        binding.ParameterId == Guid.Empty ? "LEGACY_PARAMETER_REFERENCE" : "STALE_PARAMETER_ID",
                        "The parameter was resolved by name. Save the model after calling ParameterBinding.BindTo to restore its stable ID reference.",
                        bindingPath + ".ParameterId");
                }

                if (parameter.Section != InterfaceSection.Input &&
                    parameter.Section != InterfaceSection.Output &&
                    parameter.Section != InterfaceSection.InOut)
                {
                    result.Add(
                        ValidationSeverity.Error,
                        "PARAMETER_NOT_CALLABLE",
                        "Only Input, Output and InOut members can be bound at a call.",
                        bindingPath);
                }

                string previousPath;
                if (parameter.Id != Guid.Empty && resolvedParameters.TryGetValue(parameter.Id, out previousPath))
                {
                    AddDuplicateBinding(result, bindingPath, parameter.Name, previousPath);
                }
                else if (parameter.Id != Guid.Empty)
                {
                    resolvedParameters.Add(parameter.Id, bindingPath);
                }

                if (resolvedNames.TryGetValue(parameter.Name ?? string.Empty, out previousPath))
                {
                    if (parameter.Id == Guid.Empty)
                    {
                        AddDuplicateBinding(result, bindingPath, parameter.Name, previousPath);
                    }
                }
                else
                {
                    resolvedNames.Add(parameter.Name ?? string.Empty, bindingPath);
                }
            }

            foreach (var parameter in Safe(block.Interface).Where(member => member.Section == InterfaceSection.InOut))
            {
                var binding = ResolveBinding(bindings, parameter);
                if (binding == null || string.IsNullOrWhiteSpace(binding.Expression))
                {
                    result.Add(
                        ValidationSeverity.Error,
                        "INOUT_BINDING_REQUIRED",
                        "InOut parameter '" + parameter.Name + "' must have a non-empty binding.",
                        path + ".Bindings");
                }
            }
        }

        private static void ValidateBindingIds(
            IEnumerable<ParameterBinding> bindings,
            string path,
            IDictionary<Guid, string> ids,
            ValidationResult result)
        {
            foreach (var binding in Safe(bindings))
            {
                ValidateId(binding.Id, path + ".Bindings[" + Display(binding.ParameterName) + "]", ids, result);
            }
        }

        private static void ValidateFunctionResult(
            UnitDefinition unit,
            BlockDefinition block,
            string path,
            ValidationResult result)
        {
            ValidateSingleLine(unit.ResultTarget, path + ".ResultTarget", "A result target", result);

            var hasReturnValue = block.Kind == BlockKind.Function &&
                                 !string.IsNullOrWhiteSpace(block.ReturnType) &&
                                 !string.Equals(block.ReturnType.Trim(), "Void", StringComparison.OrdinalIgnoreCase);
            if (hasReturnValue && string.IsNullOrWhiteSpace(unit.ResultTarget))
            {
                result.Add(
                    ValidationSeverity.Error,
                    "FC_RESULT_TARGET_REQUIRED",
                    "A non-Void FC requires a result target expression.",
                    path + ".ResultTarget");
            }
            else if (!hasReturnValue && !string.IsNullOrWhiteSpace(unit.ResultTarget))
            {
                result.Add(
                    ValidationSeverity.Warning,
                    "RESULT_TARGET_IGNORED",
                    "ResultTarget is ignored because this call has no return value.",
                    path + ".ResultTarget");
            }
        }

        private static void AddDuplicateBinding(
            ValidationResult result,
            string path,
            string parameterName,
            string previousPath)
        {
            result.Add(
                ValidationSeverity.Error,
                "DUPLICATE_BINDING",
                "Parameter '" + parameterName + "' is already bound at " + previousPath + ".",
                path);
        }

        private static ReferenceKind ResolveBlock(
            IEnumerable<BlockDefinition> blocks,
            UnitDefinition unit,
            out BlockDefinition block)
        {
            var blockList = Safe(blocks);
            if (unit.BlockId != Guid.Empty)
            {
                block = blockList.FirstOrDefault(candidate => candidate.Id == unit.BlockId);
                if (block != null)
                {
                    return ReferenceKind.StableId;
                }
            }

            block = blockList.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, unit.BlockName, StringComparison.OrdinalIgnoreCase));
            return block == null ? ReferenceKind.NotFound : ReferenceKind.NameFallback;
        }

        private static ReferenceKind ResolveParameter(
            IEnumerable<InterfaceMember> members,
            ParameterBinding binding,
            out InterfaceMember parameter)
        {
            var memberList = Safe(members);
            if (binding.ParameterId != Guid.Empty)
            {
                parameter = memberList.FirstOrDefault(candidate => candidate.Id == binding.ParameterId);
                if (parameter != null)
                {
                    return ReferenceKind.StableId;
                }
            }

            parameter = memberList.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, binding.ParameterName, StringComparison.OrdinalIgnoreCase));
            return parameter == null ? ReferenceKind.NotFound : ReferenceKind.NameFallback;
        }

        private static ParameterBinding ResolveBinding(
            IEnumerable<ParameterBinding> bindings,
            InterfaceMember parameter)
        {
            var bindingList = Safe(bindings);
            if (parameter.Id != Guid.Empty)
            {
                var byId = bindingList.FirstOrDefault(binding => binding.ParameterId == parameter.Id);
                if (byId != null)
                {
                    return byId;
                }
            }

            return bindingList.FirstOrDefault(binding =>
                string.Equals(binding.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateId(
            Guid id,
            string path,
            IDictionary<Guid, string> ids,
            ValidationResult result)
        {
            if (id == Guid.Empty)
            {
                result.Add(ValidationSeverity.Error, "EMPTY_ID", "A stable non-empty ID is required.", path + ".Id");
                return;
            }

            string existingPath;
            if (ids.TryGetValue(id, out existingPath))
            {
                result.Add(
                    ValidationSeverity.Error,
                    "DUPLICATE_ID",
                    "The ID is already used at " + existingPath + ".",
                    path + ".Id");
                return;
            }

            ids.Add(id, path);
        }

        private sealed class AddressedTag
        {
            public AddressedTag(TagDefinition tag, string path, string canonicalAddress, PlcAddressSpec spec)
            {
                Tag = tag;
                Path = path;
                CanonicalAddress = canonicalAddress;
                Spec = spec;
            }

            public TagDefinition Tag { get; private set; }

            public string Path { get; private set; }

            public string CanonicalAddress { get; private set; }

            public PlcAddressSpec Spec { get; private set; }
        }

        /// <summary>
        /// Reports tags whose addresses cover any of the same bits. Two valid
        /// addresses can still be the same memory: %MW10 contains %M10.0, and
        /// writing the word then silently changes the bit. Nothing downstream
        /// can catch it — the SCL is well formed and the PLC compiles it — so
        /// this is the only place the engineer can be told.
        ///
        /// It is a warning, not an error. Overlaying a status word with named
        /// bits is standard Siemens practice, and refusing to generate would
        /// make the tool unusable for a common, correct layout.
        /// </summary>
        private static void ValidateAddressOverlaps(
            IList<AddressedTag> addressedTags,
            ValidationResult result)
        {
            // Sorting by area and start bit turns the pairwise comparison into a
            // sweep: once a later tag starts after the current one ends, no tag
            // after it can overlap either.
            var ordered = addressedTags
                .OrderBy(item => item.Spec.Area)
                .ThenBy(item => item.Spec.FirstBit)
                .ThenBy(item => item.Spec.LastBit)
                .ToList();

            for (var index = 0; index < ordered.Count; index++)
            {
                var current = ordered[index];
                for (var other = index + 1; other < ordered.Count; other++)
                {
                    var candidate = ordered[other];
                    if (candidate.Spec.Area != current.Spec.Area ||
                        candidate.Spec.FirstBit > current.Spec.LastBit)
                    {
                        break;
                    }

                    if (ReferenceEquals(current.Tag, candidate.Tag))
                    {
                        continue;
                    }

                    result.Add(
                        ValidationSeverity.Warning,
                        "TAG_ADDRESS_OVERLAP",
                        "Tag '" + Display(current.Tag.Name) + "' at " + current.CanonicalAddress +
                        " occupies the same memory as '" + Display(candidate.Tag.Name) + "' at " +
                        candidate.CanonicalAddress + "; writing one changes the other.",
                        candidate.Path + ".Address");
                }
            }
        }

        private static void ValidateCollection<T>(IEnumerable<T> items, string path, ValidationResult result)
            where T : class
        {
            if (items == null)
            {
                result.Add(
                    ValidationSeverity.Error,
                    "MISSING_COLLECTION",
                    "The collection is missing and the model cannot be generated from.",
                    path);
                return;
            }

            if (items.Any(item => item == null))
            {
                result.Add(
                    ValidationSeverity.Error,
                    "NULL_MODEL_ITEM",
                    "The collection contains an empty entry; the project file is damaged.",
                    path);
            }
        }

        private static void ValidateName(string name, string path, ValidationResult result)
        {
            string error;
            if (!SclName.TryValidate(name, out error))
            {
                result.Add(ValidationSeverity.Error, "INVALID_NAME", error, path);
            }
        }

        private static void ValidateDataType(
            string dataType,
            IEnumerable<UdtDefinition> dataTypes,
            bool allowVoid,
            string path,
            ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(dataType))
            {
                result.Add(ValidationSeverity.Error, "DATA_TYPE_REQUIRED", "A data type is required.", path);
                return;
            }

            ValidateSingleLine(dataType, path, "A data type", result);
            string canonical;
            Guid udtId;
            if (!SclText.HasControlCharacters(dataType) &&
                !PlcDataTypes.TryResolve(dataType, dataTypes, allowVoid, out canonical, out udtId))
            {
                result.Add(
                    ValidationSeverity.Error,
                    "UNKNOWN_DATA_TYPE",
                    "The data type is not a supported TIA basic type or a declared UDT.",
                    path);
            }
        }

        private static void ValidateUdtDependencyGraph(
            IList<UdtDefinition> dataTypes,
            ValidationResult result)
        {
            var everyMemberResolves = dataTypes.All(dataType =>
                Safe(dataType.Members).All(member =>
                {
                    string canonical;
                    Guid udtId;
                    return PlcDataTypes.TryResolve(
                        member.DataType,
                        dataTypes,
                        false,
                        out canonical,
                        out udtId);
                }));
            if (!everyMemberResolves)
            {
                return;
            }

            try
            {
                PlcDataTypes.OrderUdtsByDependency(dataTypes);
            }
            catch (InvalidOperationException exception)
            {
                result.Add(
                    ValidationSeverity.Error,
                    "UDT_REFERENCE_CYCLE",
                    exception.Message,
                    "DataTypes");
            }
        }

        /// <summary>
        /// A value that lands inside one SCL declaration or one call argument
        /// must stay on one line. A line break would close the construct early
        /// and turn whatever follows into free-standing source text.
        /// </summary>
        private static void ValidateSingleLine(
            string value,
            string path,
            string subject,
            ValidationResult result)
        {
            if (SclText.HasControlCharacters(value))
            {
                result.Add(
                    ValidationSeverity.Error,
                    "CONTROL_CHARACTER",
                    subject + " must not contain a line break or any other control character.",
                    path);
            }
        }

        /// <summary>
        /// Free text may span lines, but a character XML cannot represent makes
        /// the whole project impossible to save. Reporting it as a normal issue
        /// lets the user find and fix the field; letting it through would strand
        /// them with unsaveable work and an exception from inside the serializer.
        /// </summary>
        private static void ValidatePersistableText(
            string value,
            string path,
            string subject,
            ValidationResult result)
        {
            if (SclText.HasNonPersistableCharacters(value))
            {
                result.Add(
                    ValidationSeverity.Error,
                    "NON_PERSISTABLE_TEXT",
                    subject + " contains a character that cannot be saved to the project file.",
                    path);
            }
        }

        private static void ValidateVersion(string version, string path, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(version) || !VersionPattern.IsMatch(version.Trim()))
            {
                result.Add(ValidationSeverity.Error, "INVALID_VERSION", "Version must use the numeric major.minor form, for example 0.1.", path);
            }
        }

        private static void RegisterDuplicateNames(
            IEnumerable<string> names,
            string path,
            ValidationResult result)
        {
            var firstIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    int firstIndex;
                    if (firstIndexes.TryGetValue(name.Trim(), out firstIndex))
                    {
                        result.Add(
                            ValidationSeverity.Error,
                            "DUPLICATE_NAME",
                            "Name '" + name.Trim() + "' duplicates item " + firstIndex + " (comparison is case-insensitive).",
                            path + "[" + index + "]");
                    }
                    else
                    {
                        firstIndexes.Add(name.Trim(), index);
                    }
                }

                index++;
            }
        }

        private static void RegisterGlobalSymbol(
            IDictionary<string, string> symbols,
            string name,
            string path,
            ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var normalized = name.Trim();
            string existingPath;
            if (symbols.TryGetValue(normalized, out existingPath))
            {
                result.Add(
                    ValidationSeverity.Error,
                    "NAME_COLLISION",
                    "Generated symbol '" + normalized + "' collides with " + existingPath + ".",
                    path);
                return;
            }

            symbols.Add(normalized, path);
        }

        private static string Display(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "?" : value.Trim();
        }

        // Constrained to reference types: the null filter below is only
        // meaningful for them, and an unconstrained T would silently keep every
        // element of a value-type sequence.
        private static List<T> Safe<T>(IEnumerable<T> items)
            where T : class
        {
            return items == null ? new List<T>() : items.Where(item => item != null).ToList();
        }

        private enum ReferenceKind
        {
            NotFound,
            StableId,
            NameFallback
        }
    }
}
