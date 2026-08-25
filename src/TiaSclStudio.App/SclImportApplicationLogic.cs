using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Importing;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Generation;
using TiaSclStudio.Diagram.History;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;

namespace TiaSclStudio.App
{
    internal enum SclLibraryImportAction
    {
        Add = 0,
        Update = 1,
        Skip = 2,
        Rejected = 3
    }

    internal sealed class SclLibraryImportPlanItem
    {
        internal SclLibraryImportPlanItem(
            SclLibraryImportItem source,
            SclLibraryImportAction action,
            string detail)
        {
            Source = source;
            Action = action;
            Detail = detail ?? string.Empty;
        }

        internal SclLibraryImportItem Source { get; private set; }

        internal BlockDefinition CandidateBlock { get; set; }

        internal UdtDefinition CandidateDataType { get; set; }

        internal BlockDefinition ExistingBlock { get; set; }

        internal UdtDefinition ExistingDataType { get; set; }

        internal BlockLibraryEditorLogic.BlockSynchronizationPlan Synchronization { get; set; }

        internal string ExpectedExistingSignature { get; set; }

        public string KindText
        {
            get
            {
                switch (Source.Kind)
                {
                    case SclImportedObjectKind.FunctionBlock: return "FB";
                    case SclImportedObjectKind.Function: return "FC";
                    default: return "UDT";
                }
            }
        }

        public string Name
        {
            get { return Source.Name; }
        }

        public int SourceLine
        {
            get { return Source.SourceLine; }
        }

        public SclLibraryImportAction Action { get; private set; }

        public string ActionText
        {
            get
            {
                switch (Action)
                {
                    case SclLibraryImportAction.Add: return "Добавить";
                    case SclLibraryImportAction.Update: return "Обновить";
                    case SclLibraryImportAction.Skip: return "Пропустить";
                    default: return "Заблокировано";
                }
            }
        }

        public string Detail { get; private set; }
    }

    internal sealed class SclLibraryImportPlanMessage
    {
        internal SclLibraryImportPlanMessage(
            SclImportDiagnosticSeverity severity,
            string code,
            string message,
            int line)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Line = line;
        }

        public SclImportDiagnosticSeverity Severity { get; private set; }
        public string Code { get; private set; }
        public string Message { get; private set; }
        public int Line { get; private set; }

        public string DisplayText
        {
            get
            {
                return (Severity == SclImportDiagnosticSeverity.Error ? "ОШИБКА" : "ПРЕДУПРЕЖДЕНИЕ") +
                    " · " + Code + (Line > 0 ? " · строка " + Line : string.Empty) + " · " + Message;
            }
        }
    }

    internal sealed class SclLibraryImportPlan
    {
        internal SclLibraryImportPlan()
        {
            Items = new List<SclLibraryImportPlanItem>();
            Messages = new List<SclLibraryImportPlanMessage>();
        }

        public IList<SclLibraryImportPlanItem> Items { get; private set; }

        public IList<SclLibraryImportPlanMessage> Messages { get; private set; }

        public bool HasBlockingErrors
        {
            get
            {
                return Messages.Any(item => item.Severity == SclImportDiagnosticSeverity.Error) ||
                    Items.Any(item => item.Action == SclLibraryImportAction.Rejected);
            }
        }

        public bool CanApply
        {
            get
            {
                return !HasBlockingErrors && Items.Any(item =>
                    item.Action == SclLibraryImportAction.Add ||
                    item.Action == SclLibraryImportAction.Update);
            }
        }

        public int AddCount
        {
            get { return Items.Count(item => item.Action == SclLibraryImportAction.Add); }
        }

        public int UpdateCount
        {
            get { return Items.Count(item => item.Action == SclLibraryImportAction.Update); }
        }

        public int SkipCount
        {
            get { return Items.Count(item => item.Action == SclLibraryImportAction.Skip); }
        }
    }

    internal static class SclImportApplicationLogic
    {
        internal static SclLibraryImportPlan BuildPlan(
            DiagramProject project,
            SclLibraryImportResult parsed,
            bool replaceExisting)
        {
            if (project == null) throw new ArgumentNullException("project");
            if (parsed == null) throw new ArgumentNullException("parsed");
            if (project.Plant == null) throw new ArgumentException("The project has no plant model.", "project");

            var plan = new SclLibraryImportPlan();
            foreach (var diagnostic in parsed.Diagnostics)
            {
                plan.Messages.Add(new SclLibraryImportPlanMessage(
                    diagnostic.Severity,
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Line));
            }

            var plant = project.Plant;
            var blocks = plant.Blocks ?? new List<BlockDefinition>();
            var existingUdts = plant.DataTypes ?? new List<UdtDefinition>();
            var importedUdts = parsed.Items
                .Where(item => item.DataType != null)
                .Select(item => CloneDataType(item.DataType))
                .ToList();
            var importedUdtNames = new HashSet<string>(
                importedUdts.Select(item => item.Name),
                StringComparer.OrdinalIgnoreCase);
            var typeCatalog = existingUdts
                .Where(item => item != null && !importedUdtNames.Contains(item.Name))
                .Select(CloneDataType)
                .Concat(importedUdts)
                .ToList();

            CanonicalizeImportedTypes(parsed, typeCatalog, plan);
            ValidateUdtDependencyGraph(typeCatalog, plan);

            foreach (var source in parsed.Items)
            {
                if (source.Block != null)
                {
                    plan.Items.Add(BuildBlockItem(project, source, typeCatalog, replaceExisting, plan));
                }
                else if (source.DataType != null)
                {
                    var canonicalDataType = typeCatalog.FirstOrDefault(item => string.Equals(
                        item.Name,
                        source.Name,
                        StringComparison.OrdinalIgnoreCase));
                    plan.Items.Add(BuildDataTypeItem(
                        project,
                        source,
                        canonicalDataType == null ? source.DataType : canonicalDataType,
                        replaceExisting,
                        plan));
                }
            }

            if (parsed.HasErrors)
            {
                RejectAllOperations(plan, "Исходник содержит ошибки; пакет применяется только целиком.");
            }

            if (!plan.HasBlockingErrors)
            {
                AppendPreflightErrors(project, plan);
            }

            return plan;
        }

        internal static void Apply(DiagramProject project, SclLibraryImportPlan plan)
        {
            if (project == null) throw new ArgumentNullException("project");
            if (plan == null) throw new ArgumentNullException("plan");
            if (!plan.CanApply)
            {
                throw new InvalidOperationException("The SCL import plan is not applicable.");
            }

            EnsurePlanIsCurrent(project, plan);
            var staleErrors = GetNewCompilationErrors(project, plan);
            if (staleErrors.Count != 0)
            {
                throw new InvalidOperationException(
                    "The project changed after the import preview or the import no longer passes validation. Run the preview again.");
            }

            if (project.Plant.DataTypes == null) project.Plant.DataTypes = new List<UdtDefinition>();
            if (project.Plant.Blocks == null) project.Plant.Blocks = new List<BlockDefinition>();

            foreach (var item in plan.Items.Where(value => value.Action == SclLibraryImportAction.Update))
            {
                if (item.ExistingDataType != null)
                {
                    CopyDataType(item.ExistingDataType, item.CandidateDataType);
                }
            }

            foreach (var item in plan.Items.Where(value => value.Action == SclLibraryImportAction.Add))
            {
                if (item.CandidateDataType != null)
                {
                    project.Plant.DataTypes.Add(CloneDataType(item.CandidateDataType));
                }
            }

            foreach (var item in plan.Items.Where(value => value.Action == SclLibraryImportAction.Update))
            {
                if (item.ExistingBlock != null)
                {
                    BlockLibraryEditorLogic.ApplySynchronization(
                        item.ExistingBlock,
                        item.CandidateBlock,
                        item.Synchronization);
                }
            }

            foreach (var item in plan.Items.Where(value => value.Action == SclLibraryImportAction.Add))
            {
                if (item.CandidateBlock != null)
                {
                    project.Plant.Blocks.Add(CloneBlock(item.CandidateBlock));
                }
            }
        }

        private static SclLibraryImportPlanItem BuildBlockItem(
            DiagramProject project,
            SclLibraryImportItem source,
            IList<UdtDefinition> typeCatalog,
            bool replaceExisting,
            SclLibraryImportPlan plan)
        {
            var candidate = CloneBlock(source.Block);
            CanonicalizeBlock(candidate, source.SourceLine, typeCatalog, plan);
            var differentKindCollision = FindDifferentKindGlobalCollision(project, source.Name, false);
            if (!string.IsNullOrWhiteSpace(differentKindCollision))
            {
                return Reject(source, differentKindCollision);
            }

            var existing = (project.Plant.Blocks ?? new List<BlockDefinition>())
                .FirstOrDefault(item => item != null && string.Equals(
                    item.Name,
                    source.Name,
                    StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                var usageErrors = BlockLibraryEditorLogic.ValidateCandidateUsage(project, candidate);
                if (usageErrors.Count != 0)
                {
                    return Reject(source, string.Join(" ", usageErrors));
                }

                return new SclLibraryImportPlanItem(
                    source,
                    SclLibraryImportAction.Add,
                    "Новый интерфейс блока; исполняемая часть SCL не импортируется.")
                {
                    CandidateBlock = candidate
                };
            }

            if (existing.Kind != candidate.Kind)
            {
                return Reject(
                    source,
                    "Уже существует " + (existing.Kind == BlockKind.FunctionBlock ? "FB" : "FC") +
                    " с этим именем; автоматическая смена вида блока запрещена.");
            }

            PreserveBlockIds(existing, candidate);
            var updateUsageErrors = BlockLibraryEditorLogic.ValidateCandidateUsage(project, candidate);
            if (updateUsageErrors.Count != 0)
            {
                return Reject(source, string.Join(" ", updateUsageErrors));
            }

            if (!replaceExisting)
            {
                return new SclLibraryImportPlanItem(
                    source,
                    SclLibraryImportAction.Skip,
                    "Конфликт имени: существующий блок сохранён. Включите обновление конфликтов для замены интерфейса.")
                {
                    CandidateBlock = candidate,
                    ExistingBlock = existing,
                    ExpectedExistingSignature = BlockSignature(existing)
                };
            }

            var synchronization = BlockLibraryEditorLogic.BuildSynchronizationPlan(project, candidate);
            var detail = "Интерфейс существующего блока будет обновлён с сохранением ID.";
            if (synchronization.WireRemovals.Count > 0)
            {
                detail += " Несовместимых связей будет удалено: " + synchronization.WireRemovals.Count + ".";
            }

            return new SclLibraryImportPlanItem(source, SclLibraryImportAction.Update, detail)
            {
                CandidateBlock = candidate,
                ExistingBlock = existing,
                Synchronization = synchronization,
                ExpectedExistingSignature = BlockSignature(existing)
            };
        }

        private static SclLibraryImportPlanItem BuildDataTypeItem(
            DiagramProject project,
            SclLibraryImportItem source,
            UdtDefinition canonicalSource,
            bool replaceExisting,
            SclLibraryImportPlan plan)
        {
            var candidate = CloneDataType(canonicalSource);
            var differentKindCollision = FindDifferentKindGlobalCollision(project, source.Name, true);
            if (!string.IsNullOrWhiteSpace(differentKindCollision))
            {
                return Reject(source, differentKindCollision);
            }

            var existing = (project.Plant.DataTypes ?? new List<UdtDefinition>())
                .FirstOrDefault(item => item != null && string.Equals(
                    item.Name,
                    source.Name,
                    StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                return new SclLibraryImportPlanItem(
                    source,
                    SclLibraryImportAction.Add,
                    "Новый пользовательский тип данных.")
                {
                    CandidateDataType = candidate
                };
            }

            PreserveDataTypeIds(existing, candidate);
            if (!replaceExisting)
            {
                return new SclLibraryImportPlanItem(
                    source,
                    SclLibraryImportAction.Skip,
                    "Конфликт имени: существующий UDT сохранён. Включите обновление конфликтов для замены состава полей.")
                {
                    CandidateDataType = candidate,
                    ExistingDataType = existing,
                    ExpectedExistingSignature = DataTypeSignature(existing)
                };
            }

            return new SclLibraryImportPlanItem(
                source,
                SclLibraryImportAction.Update,
                "Существующий UDT будет обновлён с сохранением ID типа и совпадающих полей.")
            {
                CandidateDataType = candidate,
                ExistingDataType = existing,
                ExpectedExistingSignature = DataTypeSignature(existing)
            };
        }

        private static void CanonicalizeImportedTypes(
            SclLibraryImportResult parsed,
            IList<UdtDefinition> typeCatalog,
            SclLibraryImportPlan plan)
        {
            foreach (var source in parsed.Items.Where(item => item.DataType != null))
            {
                var catalogUdt = typeCatalog.FirstOrDefault(item => string.Equals(
                    item.Name,
                    source.Name,
                    StringComparison.OrdinalIgnoreCase));
                if (catalogUdt == null) continue;

                foreach (var member in catalogUdt.Members ?? new List<UdtMember>())
                {
                    string canonical;
                    Guid udtId;
                    if (PlcDataTypes.TryResolve(member.DataType, typeCatalog, false, out canonical, out udtId))
                    {
                        member.DataType = canonical;
                    }
                    else
                    {
                        AddTypeError(plan, source.SourceLine, source.Name + "." + member.Name, member.DataType);
                    }
                }
            }
        }

        private static void CanonicalizeBlock(
            BlockDefinition block,
            int sourceLine,
            IList<UdtDefinition> typeCatalog,
            SclLibraryImportPlan plan)
        {
            if (block.Kind == BlockKind.Function)
            {
                string canonical;
                Guid udtId;
                if (PlcDataTypes.TryResolve(block.ReturnType, typeCatalog, true, out canonical, out udtId))
                {
                    block.ReturnType = canonical;
                }
                else
                {
                    AddTypeError(plan, sourceLine, block.Name + ".Return", block.ReturnType);
                }
            }

            foreach (var member in block.Interface ?? new List<InterfaceMember>())
            {
                string canonical;
                Guid udtId;
                if (PlcDataTypes.TryResolve(member.DataType, typeCatalog, false, out canonical, out udtId))
                {
                    member.DataType = canonical;
                }
                else if (block.ImportedInterfaceOnly &&
                    block.Kind == BlockKind.FunctionBlock &&
                    member.Section == InterfaceSection.Static &&
                    PlcDataTypes.TryResolveSystemBlockInstanceType(member.DataType, out canonical))
                {
                    member.DataType = canonical;
                }
                else
                {
                    AddTypeError(plan, sourceLine, block.Name + "." + member.Name, member.DataType);
                }
            }
        }

        private static void ValidateUdtDependencyGraph(
            IList<UdtDefinition> typeCatalog,
            SclLibraryImportPlan plan)
        {
            try
            {
                PlcDataTypes.OrderUdtsByDependency(typeCatalog);
            }
            catch (InvalidOperationException exception)
            {
                plan.Messages.Add(new SclLibraryImportPlanMessage(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_UDT_DEPENDENCY",
                    exception.Message,
                    0));
            }
        }

        private static void AddTypeError(
            SclLibraryImportPlan plan,
            int line,
            string memberPath,
            string dataType)
        {
            plan.Messages.Add(new SclLibraryImportPlanMessage(
                SclImportDiagnosticSeverity.Error,
                "SCL_IMPORT_TYPE_UNRESOLVED",
                "Тип '" + dataType + "' для " + memberPath +
                    " не является поддерживаемым базовым типом или существующим/imported UDT.",
                line));
        }

        private static string FindDifferentKindGlobalCollision(
            DiagramProject project,
            string name,
            bool importingDataType)
        {
            var plant = project.Plant;
            if (importingDataType && (plant.Blocks ?? new List<BlockDefinition>()).Any(item =>
                item != null && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return "Имя занято блоком FB/FC.";
            }

            if (!importingDataType && (plant.DataTypes ?? new List<UdtDefinition>()).Any(item =>
                item != null && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return "Имя занято пользовательским типом данных.";
            }

            if ((plant.Tags ?? new List<TagDefinition>()).Any(item =>
                item != null && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return "Имя занято PLC-тегом.";
            }

            if ((plant.CallBlocks ?? new List<CallBlockDefinition>()).Any(item =>
                item != null && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return "Имя занято блоком вызовов.";
            }

            if ((project.Sheets ?? new List<CallSheet>()).Any(item =>
                item != null && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return "Имя занято листом вызовов.";
            }

            return string.Empty;
        }

        private static SclLibraryImportPlanItem Reject(SclLibraryImportItem source, string detail)
        {
            return new SclLibraryImportPlanItem(source, SclLibraryImportAction.Rejected, detail);
        }

        private static void RejectAllOperations(SclLibraryImportPlan plan, string reason)
        {
            if (!plan.Messages.Any(item => string.Equals(
                item.Code,
                "SCL_IMPORT_ATOMIC_BATCH_BLOCKED",
                StringComparison.Ordinal)))
            {
                plan.Messages.Add(new SclLibraryImportPlanMessage(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_ATOMIC_BATCH_BLOCKED",
                    reason,
                    0));
            }
        }

        private static void AppendPreflightErrors(
            DiagramProject project,
            SclLibraryImportPlan plan)
        {
            foreach (var issue in GetNewCompilationErrors(project, plan))
            {
                plan.Messages.Add(new SclLibraryImportPlanMessage(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_PREFLIGHT_" + issue.Code,
                    issue.Message,
                    0));
            }
        }

        private static IList<DiagramProjectIssue> GetNewCompilationErrors(
            DiagramProject project,
            SclLibraryImportPlan plan)
        {
            var compiler = new DiagramProjectCompiler();
            var baseline = compiler.Compile(project).Issues
                .Where(item => item.Severity == DiagramIssueSeverity.Error)
                .Select(CompilationIssueKey)
                .ToHashSet(StringComparer.Ordinal);
            var future = DiagramProjectSnapshot.Clone(project);
            ApplyDefinitionsToClone(future, plan);
            return compiler.Compile(future).Issues
                .Where(item => item.Severity == DiagramIssueSeverity.Error)
                .Where(item => !baseline.Contains(CompilationIssueKey(item)))
                .ToList();
        }

        private static void ApplyDefinitionsToClone(
            DiagramProject project,
            SclLibraryImportPlan plan)
        {
            foreach (var item in plan.Items.Where(value => value.Action == SclLibraryImportAction.Update))
            {
                if (item.CandidateDataType != null)
                {
                    var target = project.Plant.DataTypes.FirstOrDefault(value => value.Id == item.ExistingDataType.Id);
                    if (target == null) throw new InvalidOperationException("The previewed UDT target no longer exists.");
                    CopyDataType(target, item.CandidateDataType);
                }
            }

            foreach (var item in plan.Items.Where(value => value.Action == SclLibraryImportAction.Add))
            {
                if (item.CandidateDataType != null)
                {
                    project.Plant.DataTypes.Add(CloneDataType(item.CandidateDataType));
                }
            }

            foreach (var item in plan.Items.Where(value => value.Action == SclLibraryImportAction.Update))
            {
                if (item.CandidateBlock != null)
                {
                    var target = project.Plant.Blocks.FirstOrDefault(value => value.Id == item.ExistingBlock.Id);
                    if (target == null) throw new InvalidOperationException("The previewed block target no longer exists.");
                    var synchronization = BlockLibraryEditorLogic.BuildSynchronizationPlan(project, item.CandidateBlock);
                    BlockLibraryEditorLogic.ApplySynchronization(target, item.CandidateBlock, synchronization);
                }
            }

            foreach (var item in plan.Items.Where(value => value.Action == SclLibraryImportAction.Add))
            {
                if (item.CandidateBlock != null)
                {
                    project.Plant.Blocks.Add(CloneBlock(item.CandidateBlock));
                }
            }
        }

        private static string CompilationIssueKey(DiagramProjectIssue issue)
        {
            return issue.Code + "|" + issue.Message + "|" +
                (issue.SheetId.HasValue ? issue.SheetId.Value.ToString("N") : string.Empty) + "|" +
                (issue.NodeId.HasValue ? issue.NodeId.Value.ToString("N") : string.Empty) + "|" +
                (issue.WireId.HasValue ? issue.WireId.Value.ToString("N") : string.Empty);
        }

        private static void EnsurePlanIsCurrent(
            DiagramProject project,
            SclLibraryImportPlan plan)
        {
            foreach (var item in plan.Items.Where(value => value.Action == SclLibraryImportAction.Update))
            {
                if (item.ExistingBlock != null &&
                    (!(project.Plant.Blocks ?? new List<BlockDefinition>()).Any(value =>
                        object.ReferenceEquals(value, item.ExistingBlock) && value.Id == item.CandidateBlock.Id) ||
                     !string.Equals(
                         item.ExpectedExistingSignature,
                         BlockSignature(item.ExistingBlock),
                         StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("The previewed block was changed before import.");
                }

                if (item.ExistingDataType != null &&
                    (!(project.Plant.DataTypes ?? new List<UdtDefinition>()).Any(value =>
                        object.ReferenceEquals(value, item.ExistingDataType) && value.Id == item.CandidateDataType.Id) ||
                     !string.Equals(
                         item.ExpectedExistingSignature,
                         DataTypeSignature(item.ExistingDataType),
                         StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("The previewed UDT was changed before import.");
                }
            }

            foreach (var item in plan.Items.Where(value => value.Action == SclLibraryImportAction.Add))
            {
                if ((project.Plant.Blocks ?? new List<BlockDefinition>()).Any(value =>
                        value != null && string.Equals(value.Name, item.Name, StringComparison.OrdinalIgnoreCase)) ||
                    (project.Plant.DataTypes ?? new List<UdtDefinition>()).Any(value =>
                        value != null && string.Equals(value.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("A previewed new object now conflicts with the project.");
                }
            }
        }

        private static void PreserveBlockIds(BlockDefinition existing, BlockDefinition candidate)
        {
            candidate.Id = existing.Id;
            var existingMembers = existing.Interface ?? new List<InterfaceMember>();
            foreach (var member in candidate.Interface ?? new List<InterfaceMember>())
            {
                var matching = existingMembers.FirstOrDefault(item =>
                    item != null &&
                    item.Section == member.Section &&
                    string.Equals(item.Name, member.Name, StringComparison.OrdinalIgnoreCase));
                if (matching != null) member.Id = matching.Id;
            }
        }

        private static void PreserveDataTypeIds(UdtDefinition existing, UdtDefinition candidate)
        {
            candidate.Id = existing.Id;
            var existingMembers = existing.Members ?? new List<UdtMember>();
            foreach (var member in candidate.Members ?? new List<UdtMember>())
            {
                var matching = existingMembers.FirstOrDefault(item =>
                    item != null && string.Equals(item.Name, member.Name, StringComparison.OrdinalIgnoreCase));
                if (matching != null) member.Id = matching.Id;
            }
        }

        private static BlockDefinition CloneBlock(BlockDefinition source)
        {
            return new BlockDefinition
            {
                Id = source.Id,
                Name = source.Name ?? string.Empty,
                Kind = source.Kind,
                ReturnType = source.ReturnType ?? string.Empty,
                Version = source.Version ?? string.Empty,
                OptimizedAccess = source.OptimizedAccess,
                ImportedInterfaceOnly = true,
                Description = source.Description ?? string.Empty,
                SclBody = string.Empty,
                Interface = (source.Interface ?? new List<InterfaceMember>()).Select(member => new InterfaceMember
                {
                    Id = member.Id,
                    Name = member.Name ?? string.Empty,
                    DataType = member.DataType ?? string.Empty,
                    Section = member.Section,
                    InitialValue = member.InitialValue ?? string.Empty,
                    Comment = member.Comment ?? string.Empty
                }).ToList()
            };
        }

        private static UdtDefinition CloneDataType(UdtDefinition source)
        {
            return new UdtDefinition
            {
                Id = source.Id,
                Name = source.Name ?? string.Empty,
                Version = source.Version ?? string.Empty,
                Comment = source.Comment ?? string.Empty,
                Members = (source.Members ?? new List<UdtMember>()).Select(member => new UdtMember
                {
                    Id = member.Id,
                    Name = member.Name ?? string.Empty,
                    DataType = member.DataType ?? string.Empty,
                    InitialValue = member.InitialValue ?? string.Empty,
                    Comment = member.Comment ?? string.Empty
                }).ToList()
            };
        }

        private static void CopyDataType(UdtDefinition target, UdtDefinition source)
        {
            target.Name = source.Name;
            target.Version = source.Version;
            target.Comment = source.Comment;
            target.Members = CloneDataType(source).Members;
        }

        private static string BlockSignature(BlockDefinition block)
        {
            if (block == null) return string.Empty;
            return string.Join("\u001f", new[]
            {
                block.Id.ToString("N"),
                block.Name ?? string.Empty,
                block.Kind.ToString(),
                block.ReturnType ?? string.Empty,
                block.Version ?? string.Empty,
                block.OptimizedAccess.ToString(),
                block.ImportedInterfaceOnly.ToString(),
                block.Description ?? string.Empty,
                block.SclBody ?? string.Empty,
                string.Join("\u001e", (block.Interface ?? new List<InterfaceMember>()).Select(member =>
                    member == null
                        ? "<null>"
                        : string.Join("\u001d", new[]
                        {
                            member.Id.ToString("N"),
                            member.Name ?? string.Empty,
                            member.DataType ?? string.Empty,
                            member.Section.ToString(),
                            member.InitialValue ?? string.Empty,
                            member.Comment ?? string.Empty
                        })))
            });
        }

        private static string DataTypeSignature(UdtDefinition dataType)
        {
            if (dataType == null) return string.Empty;
            return string.Join("\u001f", new[]
            {
                dataType.Id.ToString("N"),
                dataType.Name ?? string.Empty,
                dataType.Version ?? string.Empty,
                dataType.Comment ?? string.Empty,
                string.Join("\u001e", (dataType.Members ?? new List<UdtMember>()).Select(member =>
                    member == null
                        ? "<null>"
                        : string.Join("\u001d", new[]
                        {
                            member.Id.ToString("N"),
                            member.Name ?? string.Empty,
                            member.DataType ?? string.Empty,
                            member.InitialValue ?? string.Empty,
                            member.Comment ?? string.Empty
                        })))
            });
        }
    }
}
