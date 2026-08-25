using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Generation;
using TiaSclStudio.Diagram.History;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;

namespace TiaSclStudio.App
{
    internal static class UdtLibraryEditorLogic
    {
        internal static IList<string> ValidateCandidates(
            DiagramProject project,
            IEnumerable<UdtDefinition> candidates)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            var projected = DiagramProjectSnapshot.Clone(project);
            Apply(projected, candidates);
            var messages = new ProjectValidator().Validate(projected.Plant).Errors
                .Select(issue => issue.Code + ": " + issue.Message + " (" + issue.Path + ")")
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // A UDT name also participates in the diagram-wide symbol table
            // (sheet names, generated sheet DBs and global instance DBs). Only
            // newly introduced compiler failures are reported here, so an
            // unrelated pre-existing wire error cannot block UDT maintenance.
            var compiler = new DiagramProjectCompiler();
            var previousErrors = new HashSet<string>(
                compiler.Compile(project).Issues
                    .Where(issue => issue.Severity == DiagramIssueSeverity.Error)
                    .Select(CompilerIssueKey),
                StringComparer.Ordinal);
            messages.AddRange(compiler.Compile(projected).Issues
                .Where(issue =>
                    issue.Severity == DiagramIssueSeverity.Error &&
                    !issue.Code.StartsWith("CORE_", StringComparison.Ordinal) &&
                    !previousErrors.Contains(CompilerIssueKey(issue)))
                .Select(issue => issue.Code + ": " + issue.Message));

            return messages.Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Applies a complete candidate UDT catalogue. Existing IDs determine rename
        /// mappings, so references can be rewritten without losing identity. The caller
        /// wraps this operation in one DiagramEditHistory transaction.
        /// </summary>
        internal static void Apply(
            DiagramProject project,
            IEnumerable<UdtDefinition> candidates)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            if (candidates == null)
            {
                throw new ArgumentNullException("candidates");
            }

            if (project.Plant == null)
            {
                project.Plant = new PlantProject();
            }

            var oldTypes = project.Plant.DataTypes ?? new List<UdtDefinition>();
            var newTypes = candidates
                .Where(item => item != null)
                .Select(UdtEditorWindow.CloneDataType)
                .ToList();
            var newById = newTypes
                .GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var renames = oldTypes
                .Where(item => item != null && newById.ContainsKey(item.Id))
                .Select(item => new UdtRename(item.Name, newById[item.Id].Name))
                .Where(item =>
                    SclName.IsValid(item.OldName) &&
                    SclName.IsValid(item.NewName) &&
                    !string.Equals(item.OldName, item.NewName, StringComparison.Ordinal))
                .ToList();

            CanonicalizeCandidateReferences(newTypes, renames);

            foreach (var block in project.Plant.Blocks ?? new List<BlockDefinition>())
            {
                if (block == null)
                {
                    continue;
                }

                block.ReturnType = RewriteByRenames(block.ReturnType, renames);
                RewriteInterface(block.Interface, renames);
            }

            foreach (var callBlock in project.Plant.CallBlocks ?? new List<CallBlockDefinition>())
            {
                if (callBlock == null)
                {
                    continue;
                }

                callBlock.ReturnType = RewriteByRenames(callBlock.ReturnType, renames);
                RewriteInterface(callBlock.Interface, renames);
            }

            foreach (var tag in project.Plant.Tags ?? new List<TagDefinition>())
            {
                if (tag != null)
                {
                    tag.DataType = RewriteByRenames(tag.DataType, renames);
                }
            }

            foreach (var sheet in project.Sheets ?? new List<CallSheet>())
            {
                if (sheet == null)
                {
                    continue;
                }

                foreach (var node in sheet.Nodes ?? new List<CallNode>())
                {
                    if (node == null)
                    {
                        continue;
                    }

                    var tagNode = node as TagNode;
                    if (tagNode != null)
                    {
                        tagNode.DataType = RewriteByRenames(tagNode.DataType, renames);
                    }

                    var dbVariableNode = node as DataBlockVariableNode;
                    if (dbVariableNode != null)
                    {
                        dbVariableNode.DataType = RewriteByRenames(dbVariableNode.DataType, renames);
                    }

                    var constantNode = node as ConstantNode;
                    if (constantNode != null)
                    {
                        constantNode.DataType = RewriteByRenames(constantNode.DataType, renames);
                    }

                    foreach (var pin in node.Pins ?? new List<Pin>())
                    {
                        if (pin != null)
                        {
                            pin.DataType = RewriteByRenames(pin.DataType, renames);
                        }
                    }
                }
            }

            foreach (var variable in project.Plant.DataBlockVariables ?? new List<DataBlockVariableDefinition>())
            {
                if (variable != null)
                {
                    variable.DataType = RewriteByRenames(variable.DataType, renames);
                }
            }

            project.Plant.DataTypes = newTypes;
        }

        internal static void RewriteReferences(
            IEnumerable<UdtDefinition> dataTypes,
            string oldName,
            string newName)
        {
            foreach (var dataType in dataTypes ?? new UdtDefinition[0])
            {
                if (dataType == null)
                {
                    continue;
                }

                foreach (var member in dataType.Members ?? new List<UdtMember>())
                {
                    if (member != null)
                    {
                        member.DataType = PlcDataTypes.RewriteUdtReference(
                            member.DataType,
                            oldName,
                            newName);
                    }
                }
            }
        }

        internal static IList<string> FindUsages(
            DiagramProject project,
            IEnumerable<UdtDefinition> workingDataTypes,
            Guid excludedDataTypeId,
            IEnumerable<string> referenceNames)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            var names = new HashSet<string>(
                (referenceNames ?? new string[0]).Where(SclName.IsValid),
                StringComparer.OrdinalIgnoreCase);
            var usages = new List<string>();
            foreach (var dataType in workingDataTypes ?? new UdtDefinition[0])
            {
                if (dataType == null || dataType.Id == excludedDataTypeId)
                {
                    continue;
                }

                foreach (var member in dataType.Members ?? new List<UdtMember>())
                {
                    if (member != null && ReferencesAny(member.DataType, names))
                    {
                        usages.Add("UDT «" + dataType.Name + "», поле «" + member.Name + "»");
                    }
                }
            }

            var plant = project.Plant ?? new PlantProject();
            foreach (var block in plant.Blocks ?? new List<BlockDefinition>())
            {
                if (block == null)
                {
                    continue;
                }

                AddInterfaceUsages(usages, "блок «" + block.Name + "»", block.Interface, names);
                if (ReferencesAny(block.ReturnType, names))
                {
                    usages.Add("возвращаемый тип блока «" + block.Name + "»");
                }
            }

            foreach (var callBlock in plant.CallBlocks ?? new List<CallBlockDefinition>())
            {
                if (callBlock == null)
                {
                    continue;
                }

                AddInterfaceUsages(
                    usages,
                    "блок вызовов «" + callBlock.Name + "»",
                    callBlock.Interface,
                    names);
                if (ReferencesAny(callBlock.ReturnType, names))
                {
                    usages.Add("возвращаемый тип блока вызовов «" + callBlock.Name + "»");
                }
            }

            foreach (var tag in plant.Tags ?? new List<TagDefinition>())
            {
                if (tag != null && ReferencesAny(tag.DataType, names))
                {
                    usages.Add("PLC-тег «" + tag.Name + "»");
                }
            }

            foreach (var sheet in project.Sheets ?? new List<CallSheet>())
            {
                if (sheet == null)
                {
                    continue;
                }

                foreach (var node in sheet.Nodes ?? new List<CallNode>())
                {
                    if (node == null)
                    {
                        continue;
                    }

                    var tag = node as TagNode;
                    var constant = node as ConstantNode;
                    var dbVariable = node as DataBlockVariableNode;
                    if ((tag != null && ReferencesAny(tag.DataType, names)) ||
                        (dbVariable != null && ReferencesAny(dbVariable.DataType, names)) ||
                        (constant != null && ReferencesAny(constant.DataType, names)) ||
                        (node.Pins ?? new List<Pin>()).Any(pin =>
                            pin != null && ReferencesAny(pin.DataType, names)))
                    {
                        usages.Add("лист «" + sheet.Name + "», узел «" + node.Title + "»");
                    }
                }
            }

            foreach (var variable in plant.DataBlockVariables ?? new List<DataBlockVariableDefinition>())
            {
                if (variable != null && ReferencesAny(variable.DataType, names))
                {
                    usages.Add("глобальная DB-переменная «" + variable.DataBlockName + "." + variable.VariableName + "»");
                }
            }

            return usages.Distinct(StringComparer.Ordinal).ToList();
        }

        private static void RewriteInterface(
            IEnumerable<InterfaceMember> members,
            IList<UdtRename> renames)
        {
            foreach (var member in members ?? new InterfaceMember[0])
            {
                if (member != null)
                {
                    member.DataType = RewriteByRenames(member.DataType, renames);
                }
            }
        }

        private static void CanonicalizeCandidateReferences(
            IList<UdtDefinition> dataTypes,
            IList<UdtRename> renames)
        {
            foreach (var dataType in dataTypes)
            {
                foreach (var member in dataType.Members ?? new List<UdtMember>())
                {
                    if (member == null)
                    {
                        continue;
                    }

                    string canonical;
                    Guid udtId;
                    if (PlcDataTypes.TryResolve(
                            member.DataType,
                            dataTypes,
                            false,
                            out canonical,
                            out udtId))
                    {
                        member.DataType = canonical;
                        continue;
                    }

                    var rewritten = RewriteByRenames(member.DataType, renames);
                    member.DataType = PlcDataTypes.TryResolve(
                        rewritten,
                        dataTypes,
                        false,
                        out canonical,
                        out udtId)
                        ? canonical
                        : rewritten;
                }
            }
        }

        private static string RewriteByRenames(string dataType, IEnumerable<UdtRename> renames)
        {
            foreach (var rename in renames)
            {
                if (PlcDataTypes.ReferencesUdt(dataType, rename.OldName))
                {
                    return PlcDataTypes.RewriteUdtReference(
                        dataType,
                        rename.OldName,
                        rename.NewName);
                }
            }

            return dataType ?? string.Empty;
        }

        private static bool ReferencesAny(string dataType, IEnumerable<string> names)
        {
            return names.Any(name => PlcDataTypes.ReferencesUdt(dataType, name));
        }

        private static void AddInterfaceUsages(
            ICollection<string> usages,
            string owner,
            IEnumerable<InterfaceMember> members,
            IEnumerable<string> names)
        {
            foreach (var member in members ?? new InterfaceMember[0])
            {
                if (member != null && ReferencesAny(member.DataType, names))
                {
                    usages.Add(owner + ", интерфейс «" + member.Name + "»");
                }
            }
        }

        private static string CompilerIssueKey(DiagramProjectIssue issue)
        {
            return issue.Code + "\0" + issue.Message + "\0" +
                (issue.SheetId.HasValue ? issue.SheetId.Value.ToString("D") : string.Empty) + "\0" +
                (issue.NodeId.HasValue ? issue.NodeId.Value.ToString("D") : string.Empty) + "\0" +
                (issue.WireId.HasValue ? issue.WireId.Value.ToString("D") : string.Empty);
        }

        private sealed class UdtRename
        {
            internal UdtRename(string oldName, string newName)
            {
                OldName = oldName ?? string.Empty;
                NewName = newName ?? string.Empty;
            }

            internal string OldName { get; private set; }

            internal string NewName { get; private set; }
        }
    }
}
