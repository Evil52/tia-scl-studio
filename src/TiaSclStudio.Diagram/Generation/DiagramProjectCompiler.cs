using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TiaSclStudio.Core.Generation;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;

namespace TiaSclStudio.Diagram.Generation
{
    /// <summary>
    /// Pure, project-wide compiler for Plant sources and every visual call
    /// sheet. No files are written and the supplied model is never mutated.
    /// </summary>
    public sealed class DiagramProjectCompiler
    {
        private sealed class SymbolOwner
        {
            public int? SheetIndex { get; set; }

            public string Description { get; set; }

            public string Kind { get; set; }
        }

        private sealed class SourceCandidate
        {
            public GeneratedSource Source { get; set; }

            public string FileName { get; set; }

            public string Origin { get; set; }

            public int Sequence { get; set; }
        }

        private sealed class IdentityOwner
        {
            public int SheetIndex { get; set; }

            public string Kind { get; set; }

            public string Description { get; set; }
        }

        public DiagramProjectCompilationResult Compile(DiagramProject project)
        {
            if (project == null)
            {
                var issue = ProjectIssue(
                    DiagramIssueSeverity.Error,
                    "DGP000",
                    "Diagram project is missing.");
                return new DiagramProjectCompilationResult(
                    new DiagramSheetCompilationResult[0],
                    new[] { issue },
                    new GeneratedSource[0]);
            }

            return Compile(project.Plant, project.Sheets);
        }

        public DiagramProjectCompilationResult Compile(
            PlantProject plant,
            IEnumerable<CallSheet> sheets)
        {
            var projectIssues = new List<DiagramProjectIssue>();
            var sheetList = sheets == null ? null : sheets.ToList();
            if (sheetList == null)
            {
                projectIssues.Add(ProjectIssue(
                    DiagramIssueSeverity.Error,
                    "DGP001",
                    "Call-sheet collection is missing."));
                sheetList = new List<CallSheet>();
            }
            else if (sheetList.Count == 0)
            {
                projectIssues.Add(ProjectIssue(
                    DiagramIssueSeverity.Error,
                    "DGP011",
                    "At least one call sheet is required."));
            }

            var plantShapeIsSafe = ValidatePlantShape(plant, projectIssues);
            if (plantShapeIsSafe)
            {
                AppendCoreIssues(plant, projectIssues);
            }

            var sheetExtras = CreateSheetIssueLists(sheetList.Count);
            ValidateSheetIdentityAcrossProject(sheetList, sheetExtras);
            ValidateStableIdentityAcrossProject(plant, sheetList, sheetExtras);
            if (plantShapeIsSafe)
            {
                ValidateGlobalSymbols(plant, sheetList, sheetExtras);
            }

            var sheetResults = new List<DiagramSheetCompilationResult>();
            for (var index = 0; index < sheetList.Count; index++)
            {
                var sheet = sheetList[index];
                var compilation = CompileSheet(plant, plantShapeIsSafe, sheet);
                var scopedIssues = compilation.Issues
                    .Select(issue => ScopeIssue(issue, sheet))
                    .Concat(sheetExtras[index])
                    .ToList();

                sheetResults.Add(new DiagramSheetCompilationResult(
                    index,
                    sheet == null ? (Guid?)null : sheet.Id,
                    sheet == null ? string.Empty : sheet.Name,
                    compilation,
                    scopedIssues));
            }

            var allIssues = new List<DiagramProjectIssue>(projectIssues);
            foreach (var result in sheetResults)
            {
                // Core validation is already represented once as a project issue.
                allIssues.AddRange(result.Issues.Where(issue =>
                    !issue.Code.StartsWith("CORE_", StringComparison.Ordinal)));
            }

            var generatedSources = new List<GeneratedSource>();
            if (!HasErrors(allIssues))
            {
                generatedSources.AddRange(BuildSourceBundle(
                    plant,
                    sheetList,
                    sheetResults,
                    allIssues));
            }

            if (HasErrors(allIssues))
            {
                generatedSources.Clear();
            }

            return new DiagramProjectCompilationResult(sheetResults, allIssues, generatedSources);
        }

        private static DiagramCompilationResult CompileSheet(
            PlantProject plant,
            bool plantShapeIsSafe,
            CallSheet sheet)
        {
            if (sheet == null)
            {
                return FailureCompilation("DGM000", "Call sheet is missing.");
            }

            if (!plantShapeIsSafe)
            {
                return FailureCompilation(
                    "DGP002",
                    "The call sheet cannot be compiled because the Plant model is missing or structurally invalid.");
            }

            string shapeError;
            if (!TryValidateSheetShape(sheet, out shapeError))
            {
                return FailureCompilation("DGP003", shapeError);
            }

            return new DiagramSclGenerator().Generate(plant, sheet);
        }

        private static DiagramCompilationResult FailureCompilation(string code, string message)
        {
            return new DiagramCompilationResult(
                string.Empty,
                new[]
                {
                    new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        code,
                        message,
                        null,
                        null)
                },
                new Guid[0]);
        }

        private static bool ValidatePlantShape(
            PlantProject plant,
            IList<DiagramProjectIssue> issues)
        {
            if (plant == null)
            {
                issues.Add(ProjectIssue(
                    DiagramIssueSeverity.Error,
                    "DGP004",
                    "Plant project is missing."));
                return false;
            }

            var isSafe = plant.Blocks != null &&
                plant.DataTypes != null &&
                plant.Tags != null &&
                plant.DataBlockVariables != null &&
                plant.CallBlocks != null &&
                !plant.Blocks.Any(item => item == null || item.Interface == null || item.Interface.Any(member => member == null)) &&
                !plant.DataTypes.Any(item => item == null || item.Members == null || item.Members.Any(member => member == null)) &&
                !plant.Tags.Any(item => item == null) &&
                !plant.DataBlockVariables.Any(item => item == null) &&
                !plant.CallBlocks.Any(item =>
                    item == null ||
                    item.Interface == null ||
                    item.Interface.Any(member => member == null) ||
                    item.Units == null ||
                    item.Units.Any(unit =>
                        unit == null ||
                        unit.Bindings == null ||
                        unit.Bindings.Any(binding => binding == null)));

            if (!isSafe)
            {
                issues.Add(ProjectIssue(
                    DiagramIssueSeverity.Error,
                    "DGP005",
                    "Plant model contains a missing collection or null model item."));
            }

            return isSafe;
        }

        private static bool TryValidateSheetShape(CallSheet sheet, out string error)
        {
            if (sheet == null)
            {
                // DiagramSclGenerator owns the canonical DGM000 diagnostic.
                error = string.Empty;
                return true;
            }

            if (sheet.Nodes == null || sheet.Wires == null)
            {
                error = "Call sheet contains a missing Nodes or Wires collection.";
                return false;
            }

            if (sheet.Nodes.Any(node =>
                node == null ||
                node.Pins == null ||
                node.Pins.Any(pin => pin == null)) ||
                sheet.Wires.Any(wire => wire == null))
            {
                error = "Call sheet contains a null node, pin or wire.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static void AppendCoreIssues(
            PlantProject plant,
            IList<DiagramProjectIssue> issues)
        {
            var validation = new ProjectValidator().Validate(plant);
            foreach (var issue in validation.Issues)
            {
                issues.Add(ProjectIssue(
                    issue.Severity == ValidationSeverity.Error
                        ? DiagramIssueSeverity.Error
                        : DiagramIssueSeverity.Warning,
                    "CORE_" + issue.Code,
                    issue.Path + ": " + issue.Message));
            }
        }

        private static IList<List<DiagramProjectIssue>> CreateSheetIssueLists(int count)
        {
            var result = new List<List<DiagramProjectIssue>>(count);
            for (var index = 0; index < count; index++)
            {
                result.Add(new List<DiagramProjectIssue>());
            }

            return result;
        }

        private static void ValidateSheetIdentityAcrossProject(
            IList<CallSheet> sheets,
            IList<List<DiagramProjectIssue>> sheetIssues)
        {
            var duplicateNames = sheets
                .Select((sheet, index) => new { Sheet = sheet, Index = index })
                .Where(item => item.Sheet != null && !string.IsNullOrWhiteSpace(item.Sheet.Name))
                .GroupBy(item => item.Sheet.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1);

            foreach (var group in duplicateNames)
            {
                foreach (var item in group)
                {
                    sheetIssues[item.Index].Add(SheetIssue(
                        item.Sheet,
                        DiagramIssueSeverity.Error,
                        "DGP007",
                        "Duplicate call-sheet name '" + group.Key + "'."));
                }
            }
        }

        private static void ValidateStableIdentityAcrossProject(
            PlantProject plant,
            IList<CallSheet> sheets,
            IList<List<DiagramProjectIssue>> sheetIssues)
        {
            var identities = new Dictionary<Guid, IdentityOwner>();
            SeedPlantIdentities(plant, identities);
            for (var sheetIndex = 0; sheetIndex < sheets.Count; sheetIndex++)
            {
                var sheet = sheets[sheetIndex];
                if (sheet == null)
                {
                    continue;
                }

                RegisterProjectIdentity(
                    identities,
                    sheetIssues,
                    sheets,
                    sheetIndex,
                    sheet.Id,
                    "sheet",
                    "call sheet '" + sheet.Name + "'");

                foreach (var node in sheet.Nodes ?? new List<CallNode>())
                {
                    if (node == null)
                    {
                        continue;
                    }

                    RegisterProjectIdentity(
                        identities,
                        sheetIssues,
                        sheets,
                        sheetIndex,
                        node.Id,
                        "node",
                        "node '" + node.Title + "'");

                    foreach (var pin in node.Pins ?? new List<Pin>())
                    {
                        if (pin == null)
                        {
                            continue;
                        }

                        RegisterProjectIdentity(
                            identities,
                            sheetIssues,
                            sheets,
                            sheetIndex,
                            pin.Id,
                            "pin",
                            "pin '" + pin.Name + "'");
                    }
                }

                foreach (var wire in sheet.Wires ?? new List<Wire>())
                {
                    if (wire == null)
                    {
                        continue;
                    }

                    RegisterProjectIdentity(
                        identities,
                        sheetIssues,
                        sheets,
                        sheetIndex,
                        wire.Id,
                        "wire",
                        "wire '" + wire.Id + "'");
                }

                foreach (var group in sheet.Groups ?? new List<DiagramGroup>())
                {
                    if (group == null)
                    {
                        continue;
                    }

                    var groupName = string.IsNullOrWhiteSpace(group.Title)
                        ? group.Id.ToString()
                        : group.Title;
                    RegisterProjectIdentity(
                        identities,
                        sheetIssues,
                        sheets,
                        sheetIndex,
                        group.Id,
                        "group",
                        "visual group '" + groupName + "'");
                }
            }
        }

        private static void SeedPlantIdentities(
            PlantProject plant,
            IDictionary<Guid, IdentityOwner> identities)
        {
            if (plant == null)
            {
                return;
            }

            RegisterPlantIdentity(identities, plant.Id, "Plant project '" + plant.Name + "'");
            foreach (var block in plant.Blocks ?? new List<BlockDefinition>())
            {
                if (block == null)
                {
                    continue;
                }

                RegisterPlantIdentity(identities, block.Id, "Plant block '" + block.Name + "'");
                foreach (var member in block.Interface ?? new List<InterfaceMember>())
                {
                    if (member != null)
                    {
                        RegisterPlantIdentity(
                            identities,
                            member.Id,
                            "interface member '" + block.Name + "." + member.Name + "'");
                    }
                }
            }

            foreach (var dataType in plant.DataTypes ?? new List<UdtDefinition>())
            {
                if (dataType == null)
                {
                    continue;
                }

                RegisterPlantIdentity(identities, dataType.Id, "Plant data type '" + dataType.Name + "'");
                foreach (var member in dataType.Members ?? new List<UdtMember>())
                {
                    if (member != null)
                    {
                        RegisterPlantIdentity(
                            identities,
                            member.Id,
                            "data-type member '" + dataType.Name + "." + member.Name + "'");
                    }
                }
            }

            foreach (var tag in plant.Tags ?? new List<TagDefinition>())
            {
                if (tag != null)
                {
                    RegisterPlantIdentity(identities, tag.Id, "Plant tag '" + tag.Name + "'");
                }
            }


            foreach (var variable in plant.DataBlockVariables ?? new List<DataBlockVariableDefinition>())
            {
                if (variable != null)
                {
                    RegisterPlantIdentity(
                        identities,
                        variable.Id,
                        "global DB variable '" + variable.DataBlockName + "." + variable.VariableName + "'");
                }
            }

            foreach (var callBlock in plant.CallBlocks ?? new List<CallBlockDefinition>())
            {
                if (callBlock == null)
                {
                    continue;
                }

                RegisterPlantIdentity(
                    identities,
                    callBlock.Id,
                    "Plant call block '" + callBlock.Name + "'");
                foreach (var member in callBlock.Interface ?? new List<InterfaceMember>())
                {
                    if (member != null)
                    {
                        RegisterPlantIdentity(
                            identities,
                            member.Id,
                            "call-block member '" + callBlock.Name + "." + member.Name + "'");
                    }
                }

                foreach (var unit in callBlock.Units ?? new List<UnitDefinition>())
                {
                    if (unit == null)
                    {
                        continue;
                    }

                    RegisterPlantIdentity(
                        identities,
                        unit.Id,
                        "Plant unit '" + callBlock.Name + "." + unit.Name + "'");
                    foreach (var binding in unit.Bindings ?? new List<ParameterBinding>())
                    {
                        if (binding != null)
                        {
                            RegisterPlantIdentity(
                                identities,
                                binding.Id,
                                "parameter binding '" + callBlock.Name + "." + unit.Name + "." +
                                binding.ParameterName + "'");
                        }
                    }
                }
            }
        }

        private static void RegisterPlantIdentity(
            IDictionary<Guid, IdentityOwner> identities,
            Guid id,
            string description)
        {
            if (id == Guid.Empty || identities.ContainsKey(id))
            {
                return;
            }

            identities.Add(id, new IdentityOwner
            {
                SheetIndex = -1,
                Kind = "Plant model item",
                Description = description
            });
        }

        private static void RegisterProjectIdentity(
            IDictionary<Guid, IdentityOwner> identities,
            IList<List<DiagramProjectIssue>> sheetIssues,
            IList<CallSheet> sheets,
            int sheetIndex,
            Guid id,
            string kind,
            string description)
        {
            if (id == Guid.Empty)
            {
                return;
            }

            IdentityOwner existing;
            if (!identities.TryGetValue(id, out existing))
            {
                identities.Add(id, new IdentityOwner
                {
                    SheetIndex = sheetIndex,
                    Kind = kind,
                    Description = description
                });
                return;
            }

            if (existing.SheetIndex < 0)
            {
                var plantMessage = "Stable id '" + id + "' is shared by " +
                    existing.Description + " and " + description + " (" + kind +
                    ") on sheet '" + sheets[sheetIndex].Name + "'.";
                sheetIssues[sheetIndex].Add(SheetIssue(
                    sheets[sheetIndex],
                    DiagramIssueSeverity.Error,
                    "DGP006",
                    plantMessage));
                return;
            }

            if (existing.SheetIndex == sheetIndex)
            {
                var sameSheetMessage = "Stable id '" + id + "' is shared by " +
                    existing.Description + " (" + existing.Kind + ") and " +
                    description + " (" + kind + ") on sheet '" +
                    sheets[sheetIndex].Name + "'.";
                sheetIssues[sheetIndex].Add(SheetIssue(
                    sheets[sheetIndex],
                    DiagramIssueSeverity.Error,
                    "DGP006",
                    sameSheetMessage));
                return;
            }

            var message = "Stable id '" + id + "' is shared by " +
                existing.Description + " (" + existing.Kind + ") on sheet '" +
                sheets[existing.SheetIndex].Name + "' and " + description +
                " (" + kind + ") on sheet '" + sheets[sheetIndex].Name + "'.";

            sheetIssues[existing.SheetIndex].Add(SheetIssue(
                sheets[existing.SheetIndex],
                DiagramIssueSeverity.Error,
                "DGP006",
                message));
            sheetIssues[sheetIndex].Add(SheetIssue(
                sheets[sheetIndex],
                DiagramIssueSeverity.Error,
                "DGP006",
                message));
        }

        private static void ValidateGlobalSymbols(
            PlantProject plant,
            IList<CallSheet> sheets,
            IList<List<DiagramProjectIssue>> sheetIssues)
        {
            var registry = new Dictionary<string, SymbolOwner>(StringComparer.OrdinalIgnoreCase);
            var blocks = plant.Blocks;

            foreach (var block in plant.Blocks)
            {
                RegisterPlantSymbol(registry, block.Name, "Plant block");
            }

            foreach (var dataType in plant.DataTypes)
            {
                RegisterPlantSymbol(registry, dataType.Name, "Plant data type");
            }

            foreach (var tag in plant.Tags)
            {
                RegisterPlantSymbol(registry, tag.Name, "Plant tag");
            }

            foreach (var dbName in plant.DataBlockVariables
                .Where(item => item.GenerateDataBlock)
                .Select(item => item.DataBlockName)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                RegisterPlantSymbol(registry, dbName, "Plant global data block");
            }

            foreach (var callBlock in plant.CallBlocks)
            {
                RegisterPlantSymbol(registry, callBlock.Name, "Plant call block");
                if (callBlock.Kind == BlockKind.FunctionBlock)
                {
                    var dbName = string.IsNullOrWhiteSpace(callBlock.InstanceDbName)
                        ? callBlock.Name + "_DB"
                        : callBlock.InstanceDbName.Trim();
                    RegisterPlantSymbol(registry, dbName, "Plant call-block DB");
                }

                foreach (var unit in callBlock.Units.Where(item => !item.UseMultiInstance))
                {
                    var definition = ResolvePlantBlock(blocks, unit);
                    if (definition != null && definition.Kind == BlockKind.FunctionBlock)
                    {
                        RegisterPlantSymbol(registry, unit.Name, "Plant unit instance DB");
                    }
                }
            }

            for (var index = 0; index < sheets.Count; index++)
            {
                var sheet = sheets[index];
                string shapeError;
                if (!TryValidateSheetShape(sheet, out shapeError) || sheet == null)
                {
                    continue;
                }

                RegisterSheetSymbol(
                    registry,
                    sheetIssues[index],
                    index,
                    sheet,
                    sheet.Name,
                    "visual call block",
                    null);

                foreach (var node in sheet.Nodes.OfType<BlockCallNode>())
                {
                    var definition = blocks.FirstOrDefault(block => block.Id == node.BlockDefinitionId);
                    if (definition != null &&
                        definition.Kind == BlockKind.FunctionBlock &&
                        !node.UseMultiInstance)
                    {
                        RegisterSheetSymbol(
                            registry,
                            sheetIssues[index],
                            index,
                            sheet,
                            node.InstanceName,
                            "visual instance DB",
                            node.Id);
                    }
                }

                if (sheet.Nodes.OfType<BlockCallNode>().Any(node => node.UseMultiInstance) &&
                    !string.IsNullOrWhiteSpace(sheet.Name))
                {
                    RegisterSheetSymbol(
                        registry,
                        sheetIssues[index],
                        index,
                        sheet,
                        sheet.Name + "_DB",
                        "visual call-block DB",
                        null);
                }
            }
        }

        private static BlockDefinition ResolvePlantBlock(
            IEnumerable<BlockDefinition> blocks,
            UnitDefinition unit)
        {
            BlockDefinition result = null;
            if (unit.BlockId != Guid.Empty)
            {
                result = blocks.FirstOrDefault(block => block.Id == unit.BlockId);
            }

            if (result == null && !string.IsNullOrWhiteSpace(unit.BlockName))
            {
                result = blocks.FirstOrDefault(block => string.Equals(
                    block.Name,
                    unit.BlockName,
                    StringComparison.OrdinalIgnoreCase));
            }

            return result;
        }

        private static void RegisterPlantSymbol(
            IDictionary<string, SymbolOwner> registry,
            string symbol,
            string kind)
        {
            if (string.IsNullOrWhiteSpace(symbol) || registry.ContainsKey(symbol.Trim()))
            {
                return;
            }

            registry.Add(symbol.Trim(), new SymbolOwner
            {
                SheetIndex = null,
                Description = kind + " '" + symbol.Trim() + "'",
                Kind = kind
            });
        }

        private static void RegisterSheetSymbol(
            IDictionary<string, SymbolOwner> registry,
            IList<DiagramProjectIssue> issues,
            int sheetIndex,
            CallSheet sheet,
            string symbol,
            string kind,
            Guid? nodeId)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            var normalized = symbol.Trim();
            SymbolOwner existing;
            if (!registry.TryGetValue(normalized, out existing))
            {
                registry.Add(normalized, new SymbolOwner
                {
                    SheetIndex = sheetIndex,
                    Description = kind + " '" + normalized + "' on sheet '" + sheet.Name + "'",
                    Kind = kind
                });
                return;
            }

            // Local collisions are already reported precisely by the per-sheet
            // generator. Duplicate sheet names have their own identity issue.
            if (existing.SheetIndex == sheetIndex ||
                (string.Equals(existing.Kind, "visual call block", StringComparison.Ordinal) &&
                 string.Equals(kind, "visual call block", StringComparison.Ordinal)))
            {
                return;
            }

            issues.Add(new DiagramProjectIssue(
                DiagramIssueSeverity.Error,
                "DGP008",
                kind + " '" + normalized + "' collides with " + existing.Description + ".",
                sheet.Id,
                sheet.Name,
                nodeId,
                null));
        }

        private static IList<GeneratedSource> BuildSourceBundle(
            PlantProject plant,
            IList<CallSheet> sheets,
            IList<DiagramSheetCompilationResult> sheetResults,
            IList<DiagramProjectIssue> issues)
        {
            var candidates = new List<SourceCandidate>();
            var sequence = 0;
            foreach (var source in new SclGenerator().GenerateProject(plant))
            {
                candidates.Add(new SourceCandidate
                {
                    Source = source,
                    FileName = MakeSafeFileName(source.FileName, "PlantSource"),
                    Origin = "Plant",
                    Sequence = sequence++
                });
            }

            for (var sheetIndex = 0; sheetIndex < sheetResults.Count; sheetIndex++)
            {
                var sheet = sheets[sheetIndex];
                var result = sheetResults[sheetIndex];
                foreach (var source in result.Compilation.GeneratedSources)
                {
                    if (source.Kind != GeneratedSourceKind.InstanceDataBlocks &&
                        source.Kind != GeneratedSourceKind.CallBlock &&
                        source.Kind != GeneratedSourceKind.CallBlockInstanceDataBlock)
                    {
                        continue;
                    }

                    var fileName = source.Kind == GeneratedSourceKind.InstanceDataBlocks
                        ? MakeVisualInstanceFileName(sheetIndex, sheet)
                        : MakeSafeFileName(source.FileName, "SheetSource_" + (sheetIndex + 1));
                    candidates.Add(new SourceCandidate
                    {
                        Source = source,
                        FileName = fileName,
                        Origin = "sheet '" + sheet.Name + "'",
                        Sequence = sequence++
                    });
                }
            }

            var byFileName = new Dictionary<string, SourceCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                SourceCandidate existing;
                if (!byFileName.TryGetValue(candidate.FileName, out existing))
                {
                    byFileName.Add(candidate.FileName, candidate);
                    continue;
                }

                var sameContent = string.Equals(
                    existing.Source.Content,
                    candidate.Source.Content,
                    StringComparison.Ordinal);
                issues.Add(ProjectIssue(
                    DiagramIssueSeverity.Error,
                    sameContent ? "DGP009" : "DGP010",
                    (sameContent ? "Duplicate" : "Conflicting") +
                    " generated source filename '" + candidate.FileName + "' from " +
                    existing.Origin + " and " + candidate.Origin + "."));
            }

            if (HasErrors(issues))
            {
                return new GeneratedSource[0];
            }

            var ordered = candidates
                .OrderBy(candidate => DependencyRank(candidate.Source.Kind))
                .ThenBy(candidate => candidate.Sequence)
                .ToList();
            var resultSources = new List<GeneratedSource>(ordered.Count);
            for (var order = 0; order < ordered.Count; order++)
            {
                var candidate = ordered[order];
                resultSources.Add(new GeneratedSource(
                    candidate.FileName,
                    candidate.Source.Content,
                    candidate.Source.Kind,
                    order));
            }

            return resultSources;
        }

        private static int DependencyRank(GeneratedSourceKind kind)
        {
            switch (kind)
            {
                case GeneratedSourceKind.DataTypes:
                    return 0;
                case GeneratedSourceKind.Block:
                    return 1;
                case GeneratedSourceKind.GlobalDataBlocks:
                    return 2;
                case GeneratedSourceKind.InstanceDataBlocks:
                    return 3;
                case GeneratedSourceKind.CallBlock:
                    return 4;
                case GeneratedSourceKind.CallBlockInstanceDataBlock:
                    return 5;
                default:
                    return int.MaxValue;
            }
        }

        private static string MakeVisualInstanceFileName(int sheetIndex, CallSheet sheet)
        {
            var token = MakeSafeFileToken(sheet == null ? string.Empty : sheet.Name, "Sheet");
            return "90_Instances_" + (sheetIndex + 1).ToString("D3") + "_" + token + ".scl";
        }

        private static string MakeSafeFileName(string fileName, string fallback)
        {
            var leafName = string.IsNullOrWhiteSpace(fileName)
                ? fallback + ".scl"
                : Path.GetFileName(fileName.Trim());
            if (string.IsNullOrWhiteSpace(leafName))
            {
                leafName = fallback + ".scl";
            }

            var invalidCharacters = new HashSet<char>(Path.GetInvalidFileNameChars());
            var builder = new StringBuilder(leafName.Length);
            foreach (var character in leafName)
            {
                builder.Append(invalidCharacters.Contains(character) ? '_' : character);
            }

            var safeName = builder.ToString().TrimEnd(' ', '.');
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = fallback + ".scl";
            }

            var baseName = Path.GetFileNameWithoutExtension(safeName);
            if (IsReservedWindowsFileName(baseName))
            {
                safeName = "_" + safeName;
            }

            const int maximumFileNameLength = 180;
            if (safeName.Length > maximumFileNameLength)
            {
                var extension = Path.GetExtension(safeName);
                var hash = StableHash(safeName);
                var suffix = "_" + hash + extension;
                var rootLength = maximumFileNameLength - suffix.Length;
                safeName = Path.GetFileNameWithoutExtension(safeName)
                    .Substring(0, Math.Max(1, rootLength)) + suffix;
            }

            return safeName;
        }

        private static string MakeSafeFileToken(string value, string fallback)
        {
            var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var builder = new StringBuilder(source.Length);
            foreach (var character in source)
            {
                builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
            }

            return builder.Length == 0 ? fallback : builder.ToString();
        }

        private static bool IsReservedWindowsFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var upper = value.ToUpperInvariant();
            if (upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL")
            {
                return true;
            }

            if (upper.Length == 4 &&
                (upper.StartsWith("COM", StringComparison.Ordinal) ||
                 upper.StartsWith("LPT", StringComparison.Ordinal)))
            {
                return upper[3] >= '1' && upper[3] <= '9';
            }

            return false;
        }

        private static string StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var character in value)
                {
                    hash ^= char.ToUpperInvariant(character);
                    hash *= 16777619;
                }

                return hash.ToString("X8");
            }
        }

        private static DiagramProjectIssue ScopeIssue(DiagramIssue issue, CallSheet sheet)
        {
            return new DiagramProjectIssue(
                issue.Severity,
                issue.Code,
                issue.Message,
                sheet == null ? (Guid?)null : sheet.Id,
                sheet == null ? string.Empty : sheet.Name,
                issue.NodeId,
                issue.WireId);
        }

        private static DiagramProjectIssue SheetIssue(
            CallSheet sheet,
            DiagramIssueSeverity severity,
            string code,
            string message)
        {
            return new DiagramProjectIssue(
                severity,
                code,
                message,
                sheet == null ? (Guid?)null : sheet.Id,
                sheet == null ? string.Empty : sheet.Name,
                null,
                null);
        }

        private static DiagramProjectIssue ProjectIssue(
            DiagramIssueSeverity severity,
            string code,
            string message)
        {
            return new DiagramProjectIssue(
                severity,
                code,
                message,
                null,
                string.Empty,
                null,
                null);
        }

        private static bool HasErrors(IEnumerable<DiagramProjectIssue> issues)
        {
            return issues.Any(issue => issue.Severity == DiagramIssueSeverity.Error);
        }
    }
}
