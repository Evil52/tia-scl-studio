using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.History;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    public partial class MainWindow
    {
        private const string LibraryBlockDragFormat = "TiaSclStudio.BlockDefinitionId";
        private const double MinimumDiagramZoom = 0.15;
        private const double MaximumDiagramZoom = 2.0;
        private const double DiagramZoomStep = 0.1;
        private const double DiagramFitScreenMargin = 44.0;
        private const double DiagramFitContentMargin = 24.0;

        private readonly DiagramEditHistory _editHistory = new DiagramEditHistory();

        private Guid? _selectedNodeId;
        private Guid? _selectedWireId;
        private Point _libraryDragStart;
        private Guid _libraryDragBlockId;
        private bool _libraryDragArmed;
        private DiagramProject _dragBeforeProject;
        private int _diagramFitRequestVersion;

        private void ResetCanvasEditingForLoadedProject()
        {
            _diagramFitRequestVersion++;
            _editHistory.Clear();
            ClearNodeSelectionState();
            _selectedWireId = null;
            ResetGroupEditingForLoadedProject();
            _dragBeforeProject = null;
            _libraryDragArmed = false;
            ResetSheetNavigationForLoadedProject();
            ApplyDiagramZoom();
            UpdateHistoryCommandState();
            UpdateSelectionCommandState();
        }

        private bool TryCommitSemanticEdit(string description, Action mutation)
        {
            if (!EnsureModelEditingAvailable() || _project == null || mutation == null)
            {
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

            DiagramProject before;
            try
            {
                before = DiagramProjectSnapshot.Clone(_project);
            }
            catch (Exception exception)
            {
                ReportUnexpectedEditFailure(description, exception);
                return false;
            }

            var activeSheetId = _sheet == null ? Guid.Empty : _sheet.Id;
            var selectedBlockId = GetSelectedLibraryBlockId();
            var zooms = CaptureSheetZooms(_project);
            try
            {
                mutation();
                _editHistory.Record(description, before, _project);
                UpdateHistoryCommandState();
                UpdateSheetCommandState();
                return true;
            }
            catch (Exception exception)
            {
                RestoreProjectAfterFailedEdit(
                    before,
                    activeSheetId,
                    selectedBlockId,
                    zooms,
                    description,
                    exception);
                return false;
            }
        }

        private void RestoreProjectAfterFailedEdit(
            DiagramProject before,
            Guid activeSheetId,
            Guid selectedBlockId,
            IDictionary<Guid, double> zooms,
            string description,
            Exception exception)
        {
            try
            {
                AdoptProjectModel(before, activeSheetId, selectedBlockId, zooms);
            }
            catch (Exception rollbackException)
            {
                _interactionDiagnostics.Clear();
                _interactionDiagnostics.Add(new DiagnosticItem(
                    "ERROR",
                    "UI_EDIT_ROLLBACK",
                    "Не удалось полностью восстановить модель после ошибки: " +
                    rollbackException.Message,
                    description));
            }

            ReportUnexpectedEditFailure(description, exception);
        }

        private void ReportUnexpectedEditFailure(string description, Exception exception)
        {
            _interactionDiagnostics.Clear();
            _interactionDiagnostics.Add(new DiagnosticItem(
                "ERROR",
                "UI_EDIT_FAILED",
                "Изменение отменено, исходное состояние восстановлено: " + exception.Message,
                description));
            RenderDiagram();
            RefreshCompilation(false);
            UpdateHistoryCommandState();
            UpdateSelectionCommandState();
            SetStatus("Изменение отменено из-за ошибки");
        }

        private void AdoptProjectModel(
            DiagramProject project,
            Guid activeSheetId,
            Guid selectedBlockId,
            IDictionary<Guid, double> zooms)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            NormalizeProject(project);
            if (project.Sheets.Count == 0)
            {
                throw new InvalidOperationException("В проекте нет листа вызовов.");
            }

            _diagramFitRequestVersion++;
            CaptureCurrentSheetViewport();
            CancelCanvasViewGestures();
            _project = project;
            _sheet = project.Sheets.FirstOrDefault(item => item.Id == activeSheetId) ?? project.Sheets[0];
            ApplySheetZooms(project, zooms);
            _interactionDiagnostics.Clear();
            CancelPendingConnection();
            ClearNodeSelectionState();
            _selectedWireId = null;

            DiagramCanvas.Width = Math.Max(900.0, _sheet.Width);
            DiagramCanvas.Height = Math.Max(560.0, _sheet.Height);
            LibraryList.ItemsSource = null;
            LibraryList.ItemsSource = _project.Plant.Blocks;
            LibraryList.SelectedItem = _project.Plant.Blocks.FirstOrDefault(
                item => item.Id == selectedBlockId);
            if (LibraryList.SelectedItem == null && _project.Plant.Blocks.Count > 0)
            {
                LibraryList.SelectedIndex = 0;
            }

            LibraryCountText.Text = _project.Plant.Blocks.Count.ToString();
            HeaderProjectNameText.Text = _project.Plant.Name;
            RefreshSheetSelector();
            ProjectPathText.Text = string.IsNullOrEmpty(_currentPath)
                ? "Проект не сохранён"
                : _currentPath;

            RenderDiagram();
            ApplyDiagramZoom();
            RestoreCurrentSheetViewport();
            UpdateLibraryCommandState();
            UpdateSelectionCommandState();
        }

        private static IDictionary<Guid, double> CaptureSheetZooms(DiagramProject project)
        {
            var result = new Dictionary<Guid, double>();
            if (project == null || project.Sheets == null)
            {
                return result;
            }

            foreach (var sheet in project.Sheets)
            {
                result[sheet.Id] = NormalizeZoom(sheet.Zoom);
            }

            return result;
        }

        private static void ApplySheetZooms(
            DiagramProject project,
            IDictionary<Guid, double> zooms)
        {
            if (project == null || project.Sheets == null || zooms == null)
            {
                return;
            }

            foreach (var sheet in project.Sheets)
            {
                double zoom;
                if (zooms.TryGetValue(sheet.Id, out zoom))
                {
                    sheet.Zoom = NormalizeZoom(zoom);
                }
            }
        }

        private Guid GetSelectedLibraryBlockId()
        {
            var block = LibraryList == null ? null : LibraryList.SelectedItem as BlockDefinition;
            return block == null ? Guid.Empty : block.Id;
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            ApplyHistoryStep(true);
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            ApplyHistoryStep(false);
        }

        private void ApplyHistoryStep(bool undo)
        {
            if (!EnsureModelEditingAvailable() || _project == null)
            {
                return;
            }

            if (_dragNode != null)
            {
                SetStatus("Сначала завершите перемещение узла");
                return;
            }

            if (IsCanvasViewGestureActive())
            {
                SetStatus("Сначала завершите выделение или панорамирование");
                return;
            }

            if (undo ? !_editHistory.CanUndo : !_editHistory.CanRedo)
            {
                return;
            }

            var description = undo
                ? _editHistory.UndoDescription
                : _editHistory.RedoDescription;
            DiagramProject original;
            try
            {
                original = DiagramProjectSnapshot.Clone(_project);
            }
            catch (Exception exception)
            {
                ReportUnexpectedEditFailure(
                    undo ? "Подготовка отмены" : "Подготовка повтора",
                    exception);
                return;
            }

            var activeSheetId = _sheet == null ? Guid.Empty : _sheet.Id;
            var selectedBlockId = GetSelectedLibraryBlockId();
            var zooms = CaptureSheetZooms(_project);
            try
            {
                var restored = undo ? _editHistory.Undo() : _editHistory.Redo();
                var restoredActiveSheetId = DetermineHistoryActiveSheetId(
                    original,
                    restored,
                    activeSheetId);
                AdoptProjectModel(restored, restoredActiveSheetId, selectedBlockId, zooms);
                RefreshCompilation(false);
                UpdateHistoryCommandState();
                SetStatus((undo ? "Отменено: " : "Повторено: ") + description);
            }
            catch (Exception exception)
            {
                try
                {
                    if (undo && _editHistory.CanRedo)
                    {
                        _editHistory.Redo();
                    }
                    else if (!undo && _editHistory.CanUndo)
                    {
                        _editHistory.Undo();
                    }

                    AdoptProjectModel(original, activeSheetId, selectedBlockId, zooms);
                }
                catch (Exception)
                {
                    // The original model object remains the final recovery boundary.
                    _project = original;
                    _sheet = original.Sheets.FirstOrDefault(item => item.Id == activeSheetId) ??
                        original.Sheets.FirstOrDefault();
                }

                ReportUnexpectedEditFailure(
                    undo ? "Отмена изменения" : "Повтор изменения",
                    exception);
            }
        }

        private void UpdateHistoryCommandState()
        {
            if (UndoButton == null || RedoButton == null)
            {
                return;
            }

            UndoButton.IsEnabled = !_onlineBusy && _editHistory.CanUndo;
            RedoButton.IsEnabled = !_onlineBusy && _editHistory.CanRedo;
            UndoButton.ToolTip = _editHistory.CanUndo
                ? "Отменить: " + _editHistory.UndoDescription + " (Ctrl+Z)"
                : "Нет изменений для отмены (Ctrl+Z)";
            RedoButton.ToolTip = _editHistory.CanRedo
                ? "Повторить: " + _editHistory.RedoDescription + " (Ctrl+Y)"
                : "Нет изменений для повтора (Ctrl+Y)";
        }

        private void SelectNode(Guid nodeId)
        {
            DiagramCanvas.Focus();
            SetSingleNodeSelectionState(nodeId);
            _selectedWireId = null;
            _selectedGroupId = null;
            UpdateSelectionVisuals();
            UpdateSelectionCommandState();
        }

        private void SelectWire(Guid wireId)
        {
            DiagramCanvas.Focus();
            CancelPendingConnection();
            ClearNodeSelectionState();
            _selectedWireId = wireId;
            _selectedGroupId = null;
            UpdateSelectionVisuals();
            UpdateSelectionCommandState();
        }

        private void ClearDiagramSelection()
        {
            ClearNodeSelectionState();
            _selectedWireId = null;
            _selectedGroupId = null;
            UpdateSelectionVisuals();
            UpdateSelectionCommandState();
        }

        private void UpdateSelectionVisuals()
        {
            foreach (var item in _nodeVisuals)
            {
                var border = item.Value as Border;
                if (border == null)
                {
                    continue;
                }

                var selected = IsNodeSelected(item.Key);
                border.BorderBrush = selected
                    ? GetBrush("AccentBrush")
                    : GetBrush("BorderStrongBrush");
                border.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
            }

            foreach (var path in _wireVisuals.Where(item => item.Uid == "WireVisible"))
            {
                var wire = path.Tag as Wire;
                var selected = wire != null &&
                    _selectedWireId.HasValue &&
                    _selectedWireId.Value == wire.Id;
                path.Stroke = selected ? GetBrush("WarningBrush") : GetBrush("AccentBrush");
                path.StrokeThickness = selected ? 3.6 : 2.4;
                path.Opacity = selected ? 1.0 : 0.88;
            }

            UpdateGroupSelectionVisuals();
        }

        private void UpdateSelectionCommandState()
        {
            if (DeleteSelectionButton != null)
            {
                DeleteSelectionButton.IsEnabled =
                    !_onlineBusy &&
                    (_selectedNodeIds.Count != 0 ||
                     _selectedNodeId.HasValue ||
                     _selectedWireId.HasValue ||
                     _selectedGroupId.HasValue);
            }

            UpdateGroupCommandState();
            UpdateViewportCommandState();
        }

        private void Wire_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_onlineBusy || e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            var path = sender as System.Windows.Shapes.Path;
            var wire = path == null ? null : path.Tag as Wire;
            if (wire == null)
            {
                return;
            }

            SelectWire(wire.Id);
            SetStatus("Провод выбран; Delete удалит соединение");
            e.Handled = true;
        }

        private void DiagramCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (BeginLassoSelection(e))
            {
                e.Handled = true;
            }
        }

        private void DeleteSelection_Click(object sender, RoutedEventArgs e)
        {
            DeleteDiagramSelection();
        }

        // S3168 asks for Task instead of void. This is the continuation of a
        // double-click handler, which WPF can only invoke as void, and the one
        // awaited call — RefreshHardwareIoCatalogAsync — reports every failure
        // through its own catch and never propagates. App.OnStartup installs a
        // dispatcher handler as the backstop for anything that still escapes.
#pragma warning disable S3168
        private async void EditNodeProperties(CallNode node)
#pragma warning restore S3168
        {
            if (!EnsureModelEditingAvailable() || node == null || _project == null)
            {
                return;
            }

            var logic = node as LogicNode;
            if (logic != null)
            {
                EditLogicNode(logic);
                return;
            }

            var note = node as NoteNode;
            if (note != null)
            {
                EditNoteNode(note);
                return;
            }

            if (!(node is BlockCallNode) && !(node is ConstantNode) && !(node is TagNode))
            {
                SetStatus("Свойства этого типа узла ещё не поддерживаются");
                return;
            }

            var editedNodeId = node.Id;
            var restrictHardwareIo = node is TagNode && IsHardwareCatalogOnlineRequired();
            var onlineTargetFingerprint = restrictHardwareIo
                ? _connectedTargetFingerprint
                : string.Empty;
            HardwareIoSelectionContext hardwareContext = null;
            if (restrictHardwareIo)
            {
                var catalog = await RefreshHardwareIoCatalogAsync();
                if (!EnsureModelEditingAvailable() ||
                    !IsNodeInCurrentProject(editedNodeId))
                {
                    return;
                }

                hardwareContext = HardwareIoSelectionPolicy.FromOnlineCatalog(catalog);
                if (!hardwareContext.FixedCpuFamily.HasValue)
                {
                    SetStatus("PLC-тег не изменён: CPU и аппаратный каталог не распознаны");
                    MessageBox.Show(
                        this,
                        hardwareContext.UnavailableReason,
                        "Аппаратная конфигурация недоступна",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            var editor = restrictHardwareIo
                ? new NodePropertiesWindow(
                    node,
                    _project,
                    hardwareContext.Choices,
                    true,
                    hardwareContext.FixedCpuFamily)
                : new NodePropertiesWindow(node, _project);
            editor.Owner = this;
            if (editor.ShowDialog() != true || editor.Result == null)
            {
                return;
            }

            var result = editor.Result;
            if (restrictHardwareIo)
            {
                var latestCatalog = await RefreshHardwareIoCatalogAsync();
                if (!EnsureModelEditingAvailable() ||
                    !IsNodeInCurrentProject(editedNodeId))
                {
                    return;
                }

                hardwareContext = HardwareIoSelectionPolicy.FromOnlineCatalog(latestCatalog);
                var hardwareError = string.Empty;
                if (!string.Equals(
                        onlineTargetFingerprint,
                        _connectedTargetFingerprint,
                        StringComparison.Ordinal) ||
                    !IsConnectedToCurrentTarget() ||
                    !hardwareContext.FixedCpuFamily.HasValue ||
                    !hardwareContext.TryValidateEditResult(result, out hardwareError))
                {
                    if (string.IsNullOrWhiteSpace(hardwareError))
                    {
                        hardwareError = "Подключение к TIA или выбранная CPU изменились.";
                    }

                    SetStatus("PLC-тег не изменён: аппаратная конфигурация изменилась");
                    MessageBox.Show(
                        this,
                        hardwareError + Environment.NewLine +
                        "Откройте свойства PLC-тега ещё раз — каталог будет прочитан заново.",
                        "Канал PLC больше недоступен",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            var description = "Изменение свойств узла " +
                (string.IsNullOrWhiteSpace(node.Title) ? node.Kind.ToString() : node.Title);
            if (!TryCommitSemanticEdit(
                description,
                () => NodeEditingLogic.Apply(_project, result)))
            {
                return;
            }

            _interactionDiagnostics.Clear();
            SetSingleNodeSelectionState(result.NodeId);
            _selectedWireId = null;
            RenderDiagram();
            RefreshCompilation(false);
            UpdateSelectionCommandState();
            SetStatus("Свойства узла обновлены");
        }

        private bool IsNodeInCurrentProject(Guid nodeId)
        {
            return _project != null &&
                   (_project.Sheets ?? new List<CallSheet>())
                       .Where(sheet => sheet != null)
                       .SelectMany(sheet => sheet.Nodes ?? new List<CallNode>())
                       .Any(item => item != null && item.Id == nodeId);
        }

        private void DeleteDiagramSelection()
        {
            if (!EnsureModelEditingAvailable() || _sheet == null)
            {
                return;
            }

            if (_selectedGroupId.HasValue)
            {
                UngroupSelectedGroup();
                return;
            }

            if (_selectedWireId.HasValue)
            {
                var wireId = _selectedWireId.Value;
                var wire = _sheet.Wires.FirstOrDefault(item => item.Id == wireId);
                if (wire == null)
                {
                    ClearDiagramSelection();
                    return;
                }

                if (!TryCommitSemanticEdit(
                    "Удаление провода",
                    () => _sheet.Wires.Remove(wire)))
                {
                    return;
                }

                _interactionDiagnostics.Clear();
                ClearDiagramSelection();
                RenderDiagram();
                RefreshCompilation(false);
                SetStatus("Провод удалён");
                return;
            }

            var selectedNodeIds = GetSelectedNodeIds();
            if (selectedNodeIds.Count == 0)
            {
                return;
            }

            var selectedIdSet = new HashSet<Guid>(selectedNodeIds);
            var nodes = _sheet.Nodes.Where(item => selectedIdSet.Contains(item.Id)).ToList();
            if (nodes.Count == 0)
            {
                ClearDiagramSelection();
                return;
            }

            var title = nodes.Count == 1
                ? (string.IsNullOrWhiteSpace(nodes[0].Title)
                    ? nodes[0].Kind.ToString()
                    : nodes[0].Title)
                : nodes.Count + " узлов";
            var removedWireCount = _sheet.Wires.Count(
                item => selectedIdSet.Contains(item.SourceNodeId) ||
                    selectedIdSet.Contains(item.TargetNodeId));
            if (!TryCommitSemanticEdit(
                nodes.Count == 1 ? "Удаление узла " + title : "Удаление узлов: " + nodes.Count,
                () =>
                {
                    _sheet.Wires.RemoveAll(
                        item => selectedIdSet.Contains(item.SourceNodeId) ||
                            selectedIdSet.Contains(item.TargetNodeId));
                    GroupEditingLogic.RemoveNodesFromGroups(_sheet, selectedIdSet);
                    _sheet.Nodes.RemoveAll(item => selectedIdSet.Contains(item.Id));
                }))
            {
                return;
            }

            _interactionDiagnostics.Clear();
            ClearDiagramSelection();
            RenderDiagram();
            RefreshCompilation(false);
            SetStatus(
                (nodes.Count == 1 ? "Узел '" + title + "' удалён" : "Удалено узлов: " + nodes.Count) +
                (removedWireCount == 0
                    ? string.Empty
                    : "; удалено проводов: " + removedWireCount));
        }

        private void LibraryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _libraryDragArmed = false;
            if (_onlineBusy || e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            var block = item == null ? null : item.DataContext as BlockDefinition;
            if (block == null)
            {
                return;
            }

            _libraryDragStart = e.GetPosition(LibraryList);
            _libraryDragBlockId = block.Id;
            _libraryDragArmed = true;
        }

        private void LibraryList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_libraryDragArmed || _onlineBusy || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var current = e.GetPosition(LibraryList);
            if (Math.Abs(current.X - _libraryDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _libraryDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var blockId = _libraryDragBlockId;
            _libraryDragArmed = false;
            var data = new DataObject();
            data.SetData(LibraryBlockDragFormat, blockId.ToString("D"));
            DragDrop.DoDragDrop(LibraryList, data, DragDropEffects.Copy);
        }

        private void DiagramCanvas_DragOver(object sender, DragEventArgs e)
        {
            Guid blockId;
            e.Effects = !_onlineBusy && TryGetDraggedBlockId(e.Data, out blockId) &&
                _project != null &&
                _project.Plant.Blocks.Any(item => item.Id == blockId)
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
            e.Handled = true;
        }

        private void DiagramCanvas_Drop(object sender, DragEventArgs e)
        {
            Guid blockId;
            if (_onlineBusy ||
                !TryGetDraggedBlockId(e.Data, out blockId) ||
                _project == null)
            {
                e.Handled = true;
                return;
            }

            var definition = _project.Plant.Blocks.FirstOrDefault(item => item.Id == blockId);
            if (definition != null)
            {
                var position = e.GetPosition(DiagramCanvas);
                AddBlockAt(definition, position.X, position.Y);
            }

            e.Handled = true;
        }

        private static bool TryGetDraggedBlockId(IDataObject data, out Guid blockId)
        {
            blockId = Guid.Empty;
            if (data == null || !data.GetDataPresent(LibraryBlockDragFormat))
            {
                return false;
            }

            return Guid.TryParse(data.GetData(LibraryBlockDragFormat) as string, out blockId);
        }

        private bool AddBlockAt(BlockDefinition definition, double centerX, double centerY)
        {
            if (!EnsureModelEditingAvailable() || definition == null || _sheet == null)
            {
                return false;
            }

            var instanceName = CreateUniqueInstanceName(definition);
            var node = DiagramNodeFactory.CreateBlockCall(
                definition,
                instanceName,
                0,
                0);
            var safeCenterX = IsFinite(centerX) ? centerX : DiagramCanvas.Width / 2.0;
            var safeCenterY = IsFinite(centerY) ? centerY : DiagramCanvas.Height / 2.0;

            if (!TryCommitSemanticEdit(
                "Добавление узла " + instanceName,
                () =>
                {
                    PositionNewNodeNear(
                        node,
                        safeCenterX - GetNodeWidth(node) / 2.0,
                        safeCenterY - GetNodeHeight(node) / 2.0);
                    _sheet.Nodes.Add(node);
                }))
            {
                return false;
            }

            _interactionDiagnostics.Clear();
            SetSingleNodeSelectionState(node.Id);
            _selectedWireId = null;
            RenderDiagram();
            RefreshCompilation(false);
            UpdateSelectionCommandState();
            SetStatus("На лист добавлен " + instanceName + " : " + definition.Name);
            return true;
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            SetDiagramZoom((_sheet == null ? 1.0 : _sheet.Zoom) - DiagramZoomStep);
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            SetDiagramZoom(1.0);
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            SetDiagramZoom((_sheet == null ? 1.0 : _sheet.Zoom) + DiagramZoomStep);
        }

        private void FitAll_Click(object sender, RoutedEventArgs e)
        {
            FitDiagramToAll();
        }

        private void FitSelection_Click(object sender, RoutedEventArgs e)
        {
            FitDiagramToSelection();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled || _onlineBusy || IsTypingFocus())
            {
                return;
            }

            var modifiers = Keyboard.Modifiers;
            bool fitSelection;
            if (!DiagramViewportFitLogic.TryResolveShortcut(
                e.Key,
                modifiers,
                out fitSelection))
            {
                return;
            }

            if (fitSelection)
            {
                FitDiagramToSelection();
            }
            else
            {
                FitDiagramToAll();
            }

            e.Handled = true;
        }

        private void FitDiagramToAll()
        {
            Rect bounds;
            var hasContent = TryGetAllDiagramBounds(out bounds);
            if (!hasContent)
            {
                bounds = GetSafeSheetBounds();
            }

            bool fullyFits;
            if (FitDiagramBounds(bounds, out fullyFits))
            {
                SetStatus(!fullyFits
                    ? "Минимальный масштаб 15%; схема больше окна — доступна прокрутка"
                    : hasContent
                        ? "Вся схема показана: " + Math.Round(_sheet.Zoom * 100.0) + "%"
                        : "Лист пуст; показано всё полотно: " + Math.Round(_sheet.Zoom * 100.0) + "%");
            }
        }

        private void FitDiagramToSelection()
        {
            Rect bounds;
            if (!TryGetSelectionDiagramBounds(out bounds))
            {
                SetStatus("Сначала выберите узлы, область или соединение");
                return;
            }

            bool fullyFits;
            if (FitDiagramBounds(bounds, out fullyFits))
            {
                SetStatus(fullyFits
                    ? "Выделение показано целиком: " + Math.Round(_sheet.Zoom * 100.0) + "%"
                    : "Минимальный масштаб 15%; выделение больше окна — доступна прокрутка");
            }
        }

        private bool FitDiagramBounds(Rect bounds, out bool fullyFits)
        {
            fullyFits = false;
            if (!CanChangeDiagramViewport() || bounds.IsEmpty || !IsFiniteRect(bounds))
            {
                return false;
            }

            var viewportWidth = GetSafeViewportDimension(
                DiagramScrollViewer.ViewportWidth,
                DiagramScrollViewer.ActualWidth,
                900.0);
            var viewportHeight = GetSafeViewportDimension(
                DiagramScrollViewer.ViewportHeight,
                DiagramScrollViewer.ActualHeight,
                560.0);
            var paddedBounds = bounds;
            paddedBounds.Inflate(DiagramFitContentMargin, DiagramFitContentMargin);
            if (!IsFiniteRect(paddedBounds))
            {
                return false;
            }

            var targetZoom = DiagramViewportFitLogic.CalculateZoom(
                paddedBounds,
                viewportWidth,
                viewportHeight,
                DiagramFitScreenMargin,
                MinimumDiagramZoom,
                MaximumDiagramZoom);
            fullyFits = DiagramViewportFitLogic.FitsAtZoom(
                paddedBounds,
                viewportWidth,
                viewportHeight,
                DiagramFitScreenMargin,
                targetZoom);
            var sheetId = _sheet.Id;
            var requestVersion = ++_diagramFitRequestVersion;
            _sheet.Zoom = NormalizeZoom(targetZoom);
            ApplyDiagramZoom();

            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    if (_sheet == null ||
                        _sheet.Id != sheetId ||
                        _onlineBusy ||
                        requestVersion != _diagramFitRequestVersion)
                    {
                        return;
                    }

                    DiagramCanvas.UpdateLayout();
                    DiagramScrollViewer.UpdateLayout();
                    var currentViewportWidth = GetSafeViewportDimension(
                        DiagramScrollViewer.ViewportWidth,
                        DiagramScrollViewer.ActualWidth,
                        viewportWidth);
                    var currentViewportHeight = GetSafeViewportDimension(
                        DiagramScrollViewer.ViewportHeight,
                        DiagramScrollViewer.ActualHeight,
                        viewportHeight);
                    var offset = DiagramViewportFitLogic.CalculateCenteredOffset(
                        bounds,
                        _sheet.Zoom,
                        currentViewportWidth,
                        currentViewportHeight);
                    var maximumHorizontalOffset = Math.Max(
                        0.0,
                        DiagramScrollViewer.ExtentWidth - currentViewportWidth);
                    var maximumVerticalOffset = Math.Max(
                        0.0,
                        DiagramScrollViewer.ExtentHeight - currentViewportHeight);
                    DiagramScrollViewer.ScrollToHorizontalOffset(
                        Clamp(offset.X, 0.0, maximumHorizontalOffset));
                    DiagramScrollViewer.ScrollToVerticalOffset(
                        Clamp(offset.Y, 0.0, maximumVerticalOffset));
                    CaptureCurrentSheetViewport();
                }));
            return true;
        }

        private bool CanChangeDiagramViewport()
        {
            if (_onlineBusy || _sheet == null || DiagramCanvas == null || DiagramScrollViewer == null)
            {
                return false;
            }

            if (_dragNode != null || IsGroupDragActive())
            {
                SetStatus("Сначала завершите перемещение на схеме");
                return false;
            }

            if (IsCanvasViewGestureActive())
            {
                SetStatus("Сначала завершите выделение или панорамирование");
                return false;
            }

            return true;
        }

        private bool TryGetAllDiagramBounds(out Rect bounds)
        {
            bounds = Rect.Empty;
            if (_sheet == null)
            {
                return false;
            }

            foreach (var node in _sheet.Nodes ?? new List<CallNode>())
            {
                Rect candidate;
                if (TryGetNodeBounds(node, out candidate))
                {
                    UnionBounds(ref bounds, candidate);
                }
            }

            foreach (var group in _sheet.Groups ?? new List<DiagramGroup>())
            {
                Rect candidate;
                if (TryGetGroupBounds(group, out candidate))
                {
                    UnionBounds(ref bounds, candidate);
                }
            }

            foreach (var wire in _sheet.Wires ?? new List<Wire>())
            {
                Rect candidate;
                if (TryGetWireBounds(wire, out candidate))
                {
                    UnionBounds(ref bounds, candidate);
                }
            }

            return !bounds.IsEmpty;
        }

        private bool TryGetSelectionDiagramBounds(out Rect bounds)
        {
            bounds = Rect.Empty;
            if (_sheet == null)
            {
                return false;
            }

            var selectedNodeIds = new HashSet<Guid>(_selectedNodeIds);
            if (_selectedNodeId.HasValue)
            {
                selectedNodeIds.Add(_selectedNodeId.Value);
            }

            if (_selectedWireId.HasValue)
            {
                var selectedWire = (_sheet.Wires ?? new List<Wire>())
                    .FirstOrDefault(item => item != null && item.Id == _selectedWireId.Value);
                if (selectedWire != null)
                {
                    selectedNodeIds.Add(selectedWire.SourceNodeId);
                    selectedNodeIds.Add(selectedWire.TargetNodeId);
                    Rect candidate;
                    if (TryGetWireBounds(selectedWire, out candidate))
                    {
                        UnionBounds(ref bounds, candidate);
                    }
                }
            }

            foreach (var node in (_sheet.Nodes ?? new List<CallNode>())
                .Where(item => item != null && selectedNodeIds.Contains(item.Id)))
            {
                Rect candidate;
                if (TryGetNodeBounds(node, out candidate))
                {
                    UnionBounds(ref bounds, candidate);
                }
            }

            if (_selectedGroupId.HasValue)
            {
                var selectedGroup = (_sheet.Groups ?? new List<DiagramGroup>())
                    .FirstOrDefault(item => item != null && item.Id == _selectedGroupId.Value);
                Rect candidate;
                if (TryGetGroupBounds(selectedGroup, out candidate))
                {
                    UnionBounds(ref bounds, candidate);
                }
            }

            return !bounds.IsEmpty;
        }

        private static bool TryGetNodeBounds(CallNode node, out Rect bounds)
        {
            bounds = Rect.Empty;
            if (node == null || !IsFinite(node.X) || !IsFinite(node.Y))
            {
                return false;
            }

            try
            {
                var width = GetNodeWidth(node);
                var height = GetNodeHeight(node);
                if (!IsFinite(width) || !IsFinite(height) || width <= 0.0 || height <= 0.0)
                {
                    return false;
                }

                bounds = new Rect(node.X, node.Y, width, height);
                return IsFiniteRect(bounds);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool TryGetGroupBounds(DiagramGroup group, out Rect bounds)
        {
            bounds = Rect.Empty;
            if (group == null)
            {
                return false;
            }

            try
            {
                bounds = GetSafeGroupRenderBounds(group);
                return !bounds.IsEmpty && IsFiniteRect(bounds);
            }
            catch (Exception)
            {
                bounds = Rect.Empty;
                return false;
            }
        }

        private bool TryGetWireBounds(Wire wire, out Rect bounds)
        {
            bounds = Rect.Empty;
            if (wire == null || _sheet == null)
            {
                return false;
            }

            var nodes = _sheet.Nodes ?? new List<CallNode>();
            var sourceNode = nodes.FirstOrDefault(item =>
                item != null && item.Id == wire.SourceNodeId);
            var targetNode = nodes.FirstOrDefault(item =>
                item != null && item.Id == wire.TargetNodeId);
            if (sourceNode == null || targetNode == null)
            {
                return false;
            }

            var sourcePin = (sourceNode.Pins ?? new List<Pin>())
                .FirstOrDefault(item => item != null && item.Id == wire.SourcePinId);
            var targetPin = (targetNode.Pins ?? new List<Pin>())
                .FirstOrDefault(item => item != null && item.Id == wire.TargetPinId);
            if (sourcePin == null || targetPin == null)
            {
                return false;
            }

            try
            {
                var start = GetPinPosition(sourceNode, sourcePin);
                var end = GetPinPosition(targetNode, targetPin);
                if (!IsFinite(start.X) ||
                    !IsFinite(start.Y) ||
                    !IsFinite(end.X) ||
                    !IsFinite(end.Y))
                {
                    return false;
                }

                var controlDistance = Math.Max(72.0, Math.Abs(end.X - start.X) * 0.48);
                var control1 = new Point(start.X + controlDistance, start.Y);
                var control2 = new Point(end.X - controlDistance, end.Y);
                var left = Math.Min(Math.Min(start.X, end.X), Math.Min(control1.X, control2.X));
                var top = Math.Min(Math.Min(start.Y, end.Y), Math.Min(control1.Y, control2.Y));
                var right = Math.Max(Math.Max(start.X, end.X), Math.Max(control1.X, control2.X));
                var bottom = Math.Max(Math.Max(start.Y, end.Y), Math.Max(control1.Y, control2.Y));
                bounds = new Rect(left, top, Math.Max(1.0, right - left), Math.Max(1.0, bottom - top));
                return IsFiniteRect(bounds);
            }
            catch (Exception)
            {
                bounds = Rect.Empty;
                return false;
            }
        }

        private Rect GetSafeSheetBounds()
        {
            var width = _sheet != null && IsFinite(_sheet.Width) && _sheet.Width > 0.0
                ? _sheet.Width
                : DiagramCanvas != null && IsFinite(DiagramCanvas.Width) && DiagramCanvas.Width > 0.0
                    ? DiagramCanvas.Width
                    : 900.0;
            var height = _sheet != null && IsFinite(_sheet.Height) && _sheet.Height > 0.0
                ? _sheet.Height
                : DiagramCanvas != null && IsFinite(DiagramCanvas.Height) && DiagramCanvas.Height > 0.0
                    ? DiagramCanvas.Height
                    : 560.0;
            return new Rect(0.0, 0.0, width, height);
        }

        private static void UnionBounds(ref Rect bounds, Rect candidate)
        {
            if (candidate.IsEmpty || !IsFiniteRect(candidate))
            {
                return;
            }

            if (bounds.IsEmpty)
            {
                bounds = candidate;
                return;
            }

            bounds.Union(candidate);
        }

        private static bool IsFiniteRect(Rect bounds)
        {
            return IsFinite(bounds.X) &&
                IsFinite(bounds.Y) &&
                IsFinite(bounds.Width) &&
                IsFinite(bounds.Height) &&
                IsFinite(bounds.X + bounds.Width) &&
                IsFinite(bounds.Y + bounds.Height) &&
                bounds.Width >= 0.0 &&
                bounds.Height >= 0.0;
        }

        private static double GetSafeViewportDimension(
            double viewportDimension,
            double actualDimension,
            double fallback)
        {
            if (IsFinite(viewportDimension) && viewportDimension > 1.0)
            {
                return viewportDimension;
            }

            return IsFinite(actualDimension) && actualDimension > 1.0
                ? actualDimension
                : fallback;
        }

        private bool HasFitSelection()
        {
            if (_sheet == null)
            {
                return false;
            }

            return _selectedNodeIds.Count != 0 ||
                _selectedNodeId.HasValue ||
                _selectedWireId.HasValue ||
                _selectedGroupId.HasValue;
        }

        private void UpdateViewportCommandState()
        {
            var viewAvailable = !_onlineBusy &&
                _sheet != null &&
                _dragNode == null &&
                !IsGroupDragActive() &&
                !IsCanvasViewGestureActive();
            if (FitAllButton != null)
            {
                FitAllButton.IsEnabled = viewAvailable;
            }

            if (FitSelectionButton != null)
            {
                FitSelectionButton.IsEnabled = viewAvailable && HasFitSelection();
            }

            var zoom = _sheet == null ? 1.0 : NormalizeZoom(_sheet.Zoom);
            if (ZoomOutButton != null)
            {
                ZoomOutButton.IsEnabled = viewAvailable && zoom > MinimumDiagramZoom + 0.0001;
            }

            if (ZoomResetButton != null)
            {
                ZoomResetButton.IsEnabled = viewAvailable;
            }

            if (ZoomInButton != null)
            {
                ZoomInButton.IsEnabled = viewAvailable && zoom < MaximumDiagramZoom - 0.0001;
            }
        }

        private void DiagramScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_onlineBusy || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                return;
            }

            if (_dragNode != null)
            {
                SetStatus("Сначала завершите перемещение узла");
                e.Handled = true;
                return;
            }

            var direction = e.Delta >= 0 ? 1.0 : -1.0;
            SetDiagramZoom((_sheet == null ? 1.0 : _sheet.Zoom) + direction * DiagramZoomStep);
            e.Handled = true;
        }

        private void SetDiagramZoom(double zoom)
        {
            if (!CanChangeDiagramViewport())
            {
                return;
            }

            _diagramFitRequestVersion++;
            _sheet.Zoom = NormalizeZoom(zoom);
            ApplyDiagramZoom();
            SetStatus("Масштаб листа: " + Math.Round(_sheet.Zoom * 100.0) + "%");
        }

        private void ApplyDiagramZoom()
        {
            if (_sheet == null || DiagramCanvas == null)
            {
                return;
            }

            _sheet.Zoom = NormalizeZoom(_sheet.Zoom);
            DiagramCanvas.LayoutTransform = new ScaleTransform(_sheet.Zoom, _sheet.Zoom);
            UpdateGroupResizeAdorner();
            if (ZoomResetButton != null)
            {
                ZoomResetButton.Content = Math.Round(_sheet.Zoom * 100.0) + "%";
            }

            UpdateViewportCommandState();
        }

        private static double NormalizeZoom(double zoom)
        {
            return IsFinite(zoom)
                ? Clamp(zoom, MinimumDiagramZoom, MaximumDiagramZoom)
                : 1.0;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsTypingFocus()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (focused == null)
            {
                return false;
            }

            if (FindVisualParent<TextBoxBase>(focused) != null ||
                FindVisualParent<PasswordBox>(focused) != null)
            {
                return true;
            }

            return FindVisualParent<ComboBox>(focused) != null ||
                FindVisualParent<ComboBoxItem>(focused) != null ||
                FindVisualParent<DataGridCell>(focused) != null ||
                FindVisualParent<Selector>(focused) != null;
        }

        private bool IsDiagramDeleteShortcutContext()
        {
            return DiagramCanvas != null &&
                ReferenceEquals(Keyboard.FocusedElement, DiagramCanvas);
        }
    }

    internal static class DiagramViewportFitLogic
    {
        internal static bool TryResolveShortcut(
            Key key,
            ModifierKeys modifiers,
            out bool fitSelection)
        {
            fitSelection = false;
            var controlPressed = (modifiers & ModifierKeys.Control) != 0;
            var unsupportedModifier =
                (modifiers & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0;
            var zeroPressed = key == Key.D0 || key == Key.NumPad0;
            if (!controlPressed || unsupportedModifier || !zeroPressed)
            {
                return false;
            }

            fitSelection = (modifiers & ModifierKeys.Shift) != 0;
            return true;
        }

        internal static double CalculateZoom(
            Rect contentBounds,
            double viewportWidth,
            double viewportHeight,
            double screenMargin,
            double minimumZoom,
            double maximumZoom)
        {
            if (contentBounds.IsEmpty ||
                !IsFinite(contentBounds.Width) ||
                !IsFinite(contentBounds.Height) ||
                !IsFinite(viewportWidth) ||
                !IsFinite(viewportHeight) ||
                viewportWidth <= 0.0 ||
                viewportHeight <= 0.0 ||
                !IsFinite(minimumZoom) ||
                !IsFinite(maximumZoom) ||
                minimumZoom <= 0.0 ||
                maximumZoom < minimumZoom)
            {
                return minimumZoom > 0.0 && IsFinite(minimumZoom) ? minimumZoom : 1.0;
            }

            var safeMargin = IsFinite(screenMargin) && screenMargin > 0.0
                ? screenMargin
                : 0.0;
            var availableWidth = Math.Max(1.0, viewportWidth - safeMargin * 2.0);
            var availableHeight = Math.Max(1.0, viewportHeight - safeMargin * 2.0);
            var width = Math.Max(1.0, contentBounds.Width);
            var height = Math.Max(1.0, contentBounds.Height);
            var zoom = Math.Min(availableWidth / width, availableHeight / height);
            if (!IsFinite(zoom))
            {
                return minimumZoom;
            }

            return Math.Max(minimumZoom, Math.Min(maximumZoom, zoom));
        }

        internal static bool FitsAtZoom(
            Rect contentBounds,
            double viewportWidth,
            double viewportHeight,
            double screenMargin,
            double zoom)
        {
            if (contentBounds.IsEmpty ||
                !IsFinite(contentBounds.Width) ||
                !IsFinite(contentBounds.Height) ||
                !IsFinite(viewportWidth) ||
                !IsFinite(viewportHeight) ||
                !IsFinite(zoom) ||
                viewportWidth <= 0.0 ||
                viewportHeight <= 0.0 ||
                zoom <= 0.0)
            {
                return false;
            }

            var safeMargin = IsFinite(screenMargin) && screenMargin > 0.0
                ? screenMargin
                : 0.0;
            var availableWidth = Math.Max(1.0, viewportWidth - safeMargin * 2.0);
            var availableHeight = Math.Max(1.0, viewportHeight - safeMargin * 2.0);
            const double tolerance = 0.5;
            return contentBounds.Width * zoom <= availableWidth + tolerance &&
                contentBounds.Height * zoom <= availableHeight + tolerance;
        }

        internal static Point CalculateCenteredOffset(
            Rect contentBounds,
            double zoom,
            double viewportWidth,
            double viewportHeight)
        {
            if (contentBounds.IsEmpty ||
                !IsFinite(contentBounds.X) ||
                !IsFinite(contentBounds.Y) ||
                !IsFinite(contentBounds.Width) ||
                !IsFinite(contentBounds.Height) ||
                !IsFinite(zoom) ||
                zoom <= 0.0 ||
                !IsFinite(viewportWidth) ||
                !IsFinite(viewportHeight))
            {
                return new Point(0.0, 0.0);
            }

            var horizontalOffset =
                (contentBounds.X + contentBounds.Width / 2.0) * zoom - viewportWidth / 2.0;
            var verticalOffset =
                (contentBounds.Y + contentBounds.Height / 2.0) * zoom - viewportHeight / 2.0;
            return IsFinite(horizontalOffset) && IsFinite(verticalOffset)
                ? new Point(horizontalOffset, verticalOffset)
                : new Point(0.0, 0.0);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
