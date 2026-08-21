using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;

namespace TiaSclStudio.App
{
    public partial class MainWindow
    {
        private void LibraryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateLibraryCommandState();
        }

        private void LibraryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = FindVisualParent<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject);
            var block = item == null ? null : item.DataContext as BlockDefinition;
            if (block != null)
            {
                LibraryList.SelectedItem = block;
                EditSelectedBlock();
                e.Handled = true;
            }
        }

        private void NewBlock_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable())
            {
                return;
            }

            if (_project == null || _project.Plant == null)
            {
                return;
            }

            var draft = new BlockDefinition
            {
                Name = CreateUniqueBlockName("FB_NewBlock"),
                Kind = BlockKind.FunctionBlock,
                ReturnType = "Void",
                Version = "0.1",
                OptimizedAccess = true
            };

            while (true)
            {
                var editor = new BlockEditorWindow(draft, _project.Plant, true)
                {
                    Owner = this
                };
                if (editor.ShowDialog() != true || editor.EditedBlock == null)
                {
                    return;
                }

                var newBlock = editor.EditedBlock;
                var usageErrors = ValidateCandidateUsage(newBlock);
                if (usageErrors.Count > 0)
                {
                    MessageBox.Show(
                        this,
                        string.Join(Environment.NewLine, usageErrors),
                        "Добавление блока невозможно",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    draft = newBlock;
                    continue;
                }

                if (!TryCommitSemanticEdit(
                    "Добавление блока " + newBlock.Name,
                    () => _project.Plant.Blocks.Add(newBlock)))
                {
                    return;
                }

                RefreshLibrary(newBlock.Id);
                _interactionDiagnostics.Clear();
                RenderDiagram();
                RefreshCompilation(false);
                SetStatus("В библиотеку добавлен блок " + newBlock.Name);
                return;
            }
        }

        private void EditSelectedBlock_Click(object sender, RoutedEventArgs e)
        {
            EditSelectedBlock();
        }

        private void EditSelectedBlock()
        {
            if (!EnsureModelEditingAvailable())
            {
                return;
            }

            if (_project == null || _project.Plant == null)
            {
                return;
            }

            var target = LibraryList.SelectedItem as BlockDefinition;
            if (target == null)
            {
                MessageBox.Show(
                    this,
                    "Сначала выберите блок в библиотеке.",
                    "Редактор блоков",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var draft = target;
            while (true)
            {
                var editor = new BlockEditorWindow(draft, _project.Plant, false)
                {
                    Owner = this
                };
                if (editor.ShowDialog() != true || editor.EditedBlock == null)
                {
                    return;
                }

                var candidate = editor.EditedBlock;
                var usageErrors = ValidateCandidateUsage(candidate);
                if (usageErrors.Count > 0)
                {
                    MessageBox.Show(
                        this,
                        string.Join(Environment.NewLine, usageErrors),
                        "Изменение блока невозможно",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    draft = candidate;
                    continue;
                }

                var plan = BuildBlockSynchronizationPlan(candidate);
                if (plan.WireRemovals.Count > 0)
                {
                    var answer = MessageBox.Show(
                        this,
                        "После изменения интерфейса будет удалено соединений: " +
                        plan.WireRemovals.Count + ".\n\n" +
                        "Причина: пин удалён либо соединение стало несовместимым " +
                        "по направлению, типу или правилу InOut.\n\n" +
                        "Применить изменение и удалить эти соединения?",
                        "Подтверждение изменения интерфейса",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);
                    if (answer != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                if (!TryCommitSemanticEdit(
                    "Изменение блока " + target.Name,
                    () => ApplyBlockSynchronization(target, candidate, plan)))
                {
                    return;
                }

                RefreshLibrary(target.Id);
                _interactionDiagnostics.Clear();
                RenderDiagram();
                RefreshCompilation(false);
                SetStatus(
                    "Блок " + target.Name + " обновлён" +
                    (plan.WireRemovals.Count == 0
                        ? string.Empty
                        : "; удалено соединений: " + plan.WireRemovals.Count));
                return;
            }
        }

        private void AddSelectedBlockToSheet_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable())
            {
                return;
            }

            if (_project == null || _sheet == null)
            {
                return;
            }

            var definition = LibraryList.SelectedItem as BlockDefinition;
            if (definition == null)
            {
                MessageBox.Show(
                    this,
                    "Сначала выберите блок в библиотеке.",
                    "Добавление блока",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var blockCount = _sheet.Nodes.OfType<BlockCallNode>().Count();
            AddBlockAt(
                definition,
                330 + (blockCount % 3) * 54 + BlockNodeWidth / 2.0,
                92 + (blockCount % 5) * 58 + 95.0);
        }

        private void DeleteSelectedBlock_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable())
            {
                return;
            }

            if (_project == null || _project.Plant == null)
            {
                return;
            }

            var definition = LibraryList.SelectedItem as BlockDefinition;
            if (definition == null)
            {
                return;
            }

            var usages = FindBlockUsages(definition);
            if (usages.Count > 0)
            {
                var visibleUsages = usages.Take(8).ToList();
                var message = new StringBuilder();
                message.AppendLine("Блок «" + definition.Name + "» удалить нельзя: он используется.");
                message.AppendLine();
                foreach (var usage in visibleUsages)
                {
                    message.AppendLine("• " + usage);
                }

                if (usages.Count > visibleUsages.Count)
                {
                    message.AppendLine("• и ещё мест: " + (usages.Count - visibleUsages.Count));
                }

                MessageBox.Show(
                    this,
                    message.ToString(),
                    "Удаление блока заблокировано",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var answer = MessageBox.Show(
                this,
                "Удалить неиспользуемый блок «" + definition.Name + "» из библиотеки?",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            var oldIndex = _project.Plant.Blocks.IndexOf(definition);
            if (!TryCommitSemanticEdit(
                "Удаление блока " + definition.Name,
                () => _project.Plant.Blocks.Remove(definition)))
            {
                return;
            }

            var next = _project.Plant.Blocks.Count == 0
                ? null
                : _project.Plant.Blocks[Math.Min(oldIndex, _project.Plant.Blocks.Count - 1)];
            RefreshLibrary(next == null ? Guid.Empty : next.Id);
            _interactionDiagnostics.Clear();
            RenderDiagram();
            RefreshCompilation(false);
            SetStatus("Блок " + definition.Name + " удалён из библиотеки");
        }

        private bool EnsureModelEditingAvailable()
        {
            if (IsGroupDragActive())
            {
                SetStatus("Сначала завершите перемещение области");
                return false;
            }

            if (_dragNode != null)
            {
                SetStatus("Сначала завершите перемещение узла");
                return false;
            }

            if (IsCanvasViewGestureActive())
            {
                SetStatus("Сначала завершите выделение или панорамирование");
                return false;
            }

            if (!_onlineBusy)
            {
                return true;
            }

            SetStatus("Дождитесь завершения операции TIA Openness");
            return false;
        }

        private IList<string> FindBlockUsages(BlockDefinition definition)
        {
            var usages = new List<string>();
            foreach (var sheet in _project.Sheets)
            {
                foreach (var node in sheet.Nodes.OfType<BlockCallNode>()
                    .Where(item => item.BlockDefinitionId == definition.Id))
                {
                    usages.Add(
                        "лист «" + sheet.Name + "», экземпляр «" +
                        (string.IsNullOrWhiteSpace(node.InstanceName) ? "без имени" : node.InstanceName) + "»");
                }
            }

            var blockIds = new HashSet<Guid>(_project.Plant.Blocks.Select(block => block.Id));
            foreach (var callBlock in _project.Plant.CallBlocks)
            {
                foreach (var unit in callBlock.Units ?? new List<UnitDefinition>())
                {
                    var stableMatch = unit.BlockId == definition.Id;
                    var fallbackMatch =
                        (unit.BlockId == Guid.Empty || !blockIds.Contains(unit.BlockId)) &&
                        string.Equals(unit.BlockName, definition.Name, StringComparison.OrdinalIgnoreCase);
                    if (stableMatch || fallbackMatch)
                    {
                        usages.Add(
                            "Plant.CallBlocks «" + callBlock.Name + "», unit «" +
                            (string.IsNullOrWhiteSpace(unit.Name) ? "без имени" : unit.Name) + "»");
                    }
                }
            }

            return usages;
        }

        private void UpdateLibraryCommandState()
        {
            var hasSelection = LibraryList != null && LibraryList.SelectedItem is BlockDefinition;
            if (EditBlockButton != null)
            {
                EditBlockButton.IsEnabled = hasSelection;
            }

            if (AddSelectedBlockButton != null)
            {
                AddSelectedBlockButton.IsEnabled = hasSelection;
            }

            if (DeleteBlockButton != null)
            {
                DeleteBlockButton.IsEnabled = hasSelection;
            }
        }

        private void RefreshLibrary(Guid selectedBlockId)
        {
            LibraryList.ItemsSource = null;
            LibraryList.ItemsSource = _project.Plant.Blocks;
            LibraryList.SelectedItem = _project.Plant.Blocks.FirstOrDefault(block => block.Id == selectedBlockId);
            LibraryCountText.Text = _project.Plant.Blocks.Count.ToString();
            UpdateLibraryCommandState();
        }

        private string CreateUniqueBlockName(string preferredName)
        {
            var used = new HashSet<string>(
                _project.Plant.Blocks.Select(block => block.Name),
                StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(preferredName))
            {
                return preferredName;
            }

            var sequence = 2;
            string candidate;
            do
            {
                candidate = preferredName + "_" + sequence;
                sequence++;
            }
            while (used.Contains(candidate));

            return candidate;
        }

        private string CreateUniqueInstanceName(BlockDefinition definition)
        {
            var baseName = definition.Name ?? string.Empty;
            if (baseName.StartsWith("FB_", StringComparison.OrdinalIgnoreCase) ||
                baseName.StartsWith("FC_", StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName.Substring(3);
            }

            baseName = SanitizeInstanceBase(baseName);
            if (baseName.Length > 116)
            {
                baseName = baseName.Substring(0, 116);
            }

            var used = CollectGlobalAndInstanceNames();
            var sequence = 1;
            string candidate;
            do
            {
                candidate = baseName + "_" + sequence.ToString("00");
                sequence++;
            }
            while (used.Contains(candidate) || !SclName.IsValid(candidate));

            return candidate;
        }

        private static string SanitizeInstanceBase(string value)
        {
            var builder = new StringBuilder();
            foreach (var character in value ?? string.Empty)
            {
                builder.Append(
                    (character >= 'A' && character <= 'Z') ||
                    (character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9') ||
                    character == '_'
                        ? character
                        : '_');
            }

            var result = builder.ToString().Trim('_');
            if (string.IsNullOrEmpty(result) ||
                !((result[0] >= 'A' && result[0] <= 'Z') ||
                  (result[0] >= 'a' && result[0] <= 'z') ||
                  result[0] == '_'))
            {
                result = "Unit_" + result;
            }

            return SclName.IsValid(result) ? result : "Unit";
        }

        private HashSet<string> CollectGlobalAndInstanceNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddNames(names, _project.Plant.Blocks.Select(item => item.Name));
            AddNames(names, _project.Plant.DataTypes.Select(item => item.Name));
            AddNames(names, _project.Plant.Tags.Select(item => item.Name));
            AddNames(names, _project.Plant.CallBlocks.Select(item => item.Name));
            AddNames(names, _project.Sheets.Select(item => item.Name));
            AddNames(
                names,
                _project.Sheets
                    .Where(sheet => (sheet.Nodes ?? new List<CallNode>())
                        .OfType<BlockCallNode>()
                        .Any(node => node.UseMultiInstance))
                    .Select(sheet => sheet.Name + "_DB"));
            AddNames(
                names,
                _project.Plant.CallBlocks
                    .Where(item => item.Kind == BlockKind.FunctionBlock)
                    .Select(item => string.IsNullOrWhiteSpace(item.InstanceDbName)
                        ? item.Name + "_DB"
                        : item.InstanceDbName.Trim()));
            var blocksById = _project.Plant.Blocks
                .Where(item => item != null)
                .GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (var callBlock in _project.Plant.CallBlocks)
            {
                foreach (var unit in callBlock.Units ?? new List<UnitDefinition>())
                {
                    if (unit.UseMultiInstance)
                    {
                        continue;
                    }

                    BlockDefinition referencedBlock;
                    if (!blocksById.TryGetValue(unit.BlockId, out referencedBlock))
                    {
                        referencedBlock = _project.Plant.Blocks.FirstOrDefault(item =>
                            string.Equals(item.Name, unit.BlockName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (referencedBlock != null && referencedBlock.Kind == BlockKind.FunctionBlock)
                    {
                        AddNames(names, new[] { unit.Name });
                    }
                }
            }

            AddNames(
                names,
                _project.Sheets.SelectMany(sheet => sheet.Nodes)
                    .OfType<BlockCallNode>()
                    .Select(node => node.InstanceName));
            return names;
        }

        private IList<string> ValidateCandidateUsage(BlockDefinition candidate)
        {
            return BlockLibraryEditorLogic.ValidateCandidateUsage(_project, candidate);
        }

        private static void AddNames(ISet<string> names, IEnumerable<string> values)
        {
            foreach (var value in values.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                names.Add(value);
            }
        }

        private BlockLibraryEditorLogic.BlockSynchronizationPlan BuildBlockSynchronizationPlan(
            BlockDefinition candidate)
        {
            return BlockLibraryEditorLogic.BuildSynchronizationPlan(_project, candidate);
        }

        private static void ApplyBlockSynchronization(
            BlockDefinition target,
            BlockDefinition candidate,
            BlockLibraryEditorLogic.BlockSynchronizationPlan plan)
        {
            BlockLibraryEditorLogic.ApplySynchronization(target, candidate, plan);
        }
    }
}
