using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    public partial class MainWindow
    {
        private void AddConstantNode_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable() || _sheet == null || _project == null)
            {
                return;
            }

            var editor = new ConstantNodeCreateWindow(_project, _sheet.Id) { Owner = this };
            if (editor.ShowDialog() != true || editor.Result == null)
            {
                return;
            }

            var result = editor.Result;
            if (!TryCommitSemanticEdit(
                "Добавление константы " + result.Literal,
                () =>
                {
                    NodeCreationLogic.Apply(_project, _sheet.Id, result);
                    var node = _sheet.Nodes.Single(item => item.Id == result.NodeId);
                    PositionNewNodeAtViewportCenter(node);
                }))
            {
                return;
            }

            var created = _sheet.Nodes.Single(item => item.Id == result.NodeId);
            CompleteNodeCreation(created, "Константа " + result.Literal + " добавлена на лист");
        }

        private async void AddTagNode_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable() || _sheet == null || _project == null)
            {
                return;
            }

            var button = sender as Button;
            TerminalDirection direction;
            if (button == null ||
                !Enum.TryParse(button.Tag as string, false, out direction) ||
                !Enum.IsDefined(typeof(TerminalDirection), direction))
            {
                SetStatus("Не удалось определить направление PLC-тега");
                return;
            }

            var targetSheetId = _sheet.Id;
            var restrictHardwareIo = IsHardwareCatalogOnlineRequired();
            var onlineTargetFingerprint = restrictHardwareIo
                ? _connectedTargetFingerprint
                : string.Empty;
            HardwareIoSelectionContext hardwareContext = null;
            if (restrictHardwareIo)
            {
                var catalog = await RefreshHardwareIoCatalogAsync();
                if (!EnsureModelEditingAvailable() ||
                    _sheet == null ||
                    _sheet.Id != targetSheetId)
                {
                    return;
                }

                hardwareContext = HardwareIoSelectionPolicy.FromOnlineCatalog(catalog);
                if (!hardwareContext.FixedCpuFamily.HasValue)
                {
                    SetStatus("PLC-тег не добавлен: CPU и аппаратный каталог не распознаны");
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
                ? new TagNodeCreateWindow(
                    _project,
                    targetSheetId,
                    direction,
                    hardwareContext.Choices,
                    true,
                    hardwareContext.FixedCpuFamily)
                : new TagNodeCreateWindow(_project, targetSheetId, direction);
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
                    _sheet == null ||
                    _sheet.Id != targetSheetId)
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
                    !hardwareContext.TryValidateCreationResult(
                    _project,
                    result,
                    out hardwareError))
                {
                    if (string.IsNullOrWhiteSpace(hardwareError))
                    {
                        hardwareError = "Подключение к TIA или выбранная CPU изменились.";
                    }

                    SetStatus("PLC-тег не добавлен: аппаратная конфигурация изменилась");
                    MessageBox.Show(
                        this,
                        hardwareError + Environment.NewLine +
                        "Откройте создание PLC-тега ещё раз — каталог будет прочитан заново.",
                        "Канал PLC больше недоступен",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            if (!TryCommitSemanticEdit(
                result.CreateTagDefinition
                    ? "Создание PLC-тега " + result.TagName
                    : "Добавление терминала PLC-тега",
                () =>
                {
                    NodeCreationLogic.Apply(_project, targetSheetId, result);
                    var node = _sheet.Nodes.Single(item => item.Id == result.NodeId);
                    PositionNewNodeAtViewportCenter(node);
                }))
            {
                return;
            }

            var created = _sheet.Nodes.Single(item => item.Id == result.NodeId);
            CompleteNodeCreation(
                created,
                (direction == TerminalDirection.Source ? "Source" : "Sink") +
                "-терминал PLC-тега " + created.Title + " добавлен");
        }

        private void AddLogicNode_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable() || _sheet == null)
            {
                return;
            }

            var button = sender as Button;
            LogicOperation operation;
            if (button == null ||
                !Enum.TryParse(button.Tag as string, false, out operation) ||
                !Enum.IsDefined(typeof(LogicOperation), operation))
            {
                SetStatus("Не удалось определить логическую операцию");
                return;
            }

            var operandType = LogicNodeEditingLogic.IsBooleanOperation(operation)
                ? "Bool"
                : "Int";
            LogicNode node;
            try
            {
                node = DiagramNodeFactory.CreateLogic(operation, operandType, 0.0, 0.0);
            }
            catch (Exception exception)
            {
                ReportUnexpectedEditFailure("Добавление логического узла", exception);
                return;
            }

            if (!TryCommitSemanticEdit(
                "Добавление логического узла " + node.Title,
                () =>
                {
                    PositionNewNodeAtViewportCenter(node);
                    _sheet.Nodes.Add(node);
                }))
            {
                return;
            }

            CompleteNodeCreation(node, "Добавлен логический узел " + node.Title);
        }

        private void AddNoteNode_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable() || _sheet == null)
            {
                return;
            }

            var draft = NoteEditingLogic.Create("Новая заметка", 0.0, 0.0);
            var editor = new NoteEditorWindow(draft)
            {
                Owner = this
            };
            if (editor.ShowDialog() != true || string.IsNullOrEmpty(editor.ApprovedText))
            {
                return;
            }

            var node = NoteEditingLogic.Create(editor.ApprovedText, 0.0, 0.0);
            if (!TryCommitSemanticEdit(
                "Добавление заметки",
                () =>
                {
                    PositionNewNodeAtViewportCenter(node);
                    _sheet.Nodes.Add(node);
                }))
            {
                return;
            }

            CompleteNodeCreation(node, "Заметка добавлена на лист");
        }

        private void EditLogicNode(LogicNode node)
        {
            var editor = new LogicNodeEditorWindow(node, _project)
            {
                Owner = this
            };
            if (editor.ShowDialog() != true || editor.Result == null)
            {
                return;
            }

            var result = editor.Result;
            if (!TryCommitSemanticEdit(
                "Изменение логического узла " + node.Title,
                () => LogicNodeEditingLogic.Apply(_project, result)))
            {
                return;
            }

            _interactionDiagnostics.Clear();
            SetSingleNodeSelectionState(result.NodeId);
            _selectedWireId = null;
            RenderDiagram();
            RefreshCompilation(false);
            UpdateSelectionCommandState();
            SetStatus("Логический узел обновлён");
        }

        private void EditNoteNode(NoteNode node)
        {
            var editor = new NoteEditorWindow(node)
            {
                Owner = this
            };
            if (editor.ShowDialog() != true || string.IsNullOrEmpty(editor.ApprovedText))
            {
                return;
            }

            if (!TryCommitSemanticEdit(
                "Изменение заметки",
                () => NoteEditingLogic.Apply(_project, node.Id, editor.ApprovedText)))
            {
                return;
            }

            _interactionDiagnostics.Clear();
            SetSingleNodeSelectionState(node.Id);
            _selectedWireId = null;
            RenderDiagram();
            RefreshCompilation(false);
            UpdateSelectionCommandState();
            SetStatus("Заметка обновлена");
        }

        private void PositionNewNodeAtViewportCenter(CallNode node)
        {
            var zoom = _sheet == null ? 1.0 : NormalizeZoom(_sheet.Zoom);
            var viewportWidth = DiagramScrollViewer == null || DiagramScrollViewer.ViewportWidth <= 0.0
                ? DiagramCanvas.Width * zoom
                : DiagramScrollViewer.ViewportWidth;
            var viewportHeight = DiagramScrollViewer == null || DiagramScrollViewer.ViewportHeight <= 0.0
                ? DiagramCanvas.Height * zoom
                : DiagramScrollViewer.ViewportHeight;
            var horizontalOffset = DiagramScrollViewer == null ? 0.0 : DiagramScrollViewer.HorizontalOffset;
            var verticalOffset = DiagramScrollViewer == null ? 0.0 : DiagramScrollViewer.VerticalOffset;
            var centerX = (horizontalOffset + viewportWidth / 2.0) / zoom;
            var centerY = (verticalOffset + viewportHeight / 2.0) / zoom;

            PositionNewNodeNear(
                node,
                centerX - GetNodeWidth(node) / 2.0,
                centerY - GetNodeHeight(node) / 2.0);
        }

        private void PositionNewNodeNear(CallNode node, double requestedX, double requestedY)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }

            if (_sheet == null)
            {
                throw new InvalidOperationException("A call sheet is required to place a node.");
            }

            var occupiedBounds = (_sheet.Nodes ?? Enumerable.Empty<CallNode>())
                .Where(item =>
                    item != null &&
                    !ReferenceEquals(item, node) &&
                    item.Id != node.Id &&
                    IsFinite(item.X) &&
                    IsFinite(item.Y))
                .Select(item => new Rect(
                    item.X,
                    item.Y,
                    GetNodeWidth(item),
                    GetNodeHeight(item)))
                .ToList();
            var placement = DenseNodePlacementLogic.FindNearestFreePlacement(
                requestedX,
                requestedY,
                GetNodeWidth(node),
                GetNodeHeight(node),
                occupiedBounds,
                _sheet.Width,
                _sheet.Height);

            node.X = placement.X;
            node.Y = placement.Y;
            _sheet.Width = placement.SheetWidth;
            _sheet.Height = placement.SheetHeight;
            DiagramCanvas.Width = Math.Max(900.0, _sheet.Width);
            DiagramCanvas.Height = Math.Max(560.0, _sheet.Height);
        }

        private void CompleteNodeCreation(CallNode node, string status)
        {
            _interactionDiagnostics.Clear();
            SetSingleNodeSelectionState(node.Id);
            _selectedWireId = null;
            RenderDiagram();
            RefreshCompilation(false);
            UpdateSelectionCommandState();
            SetStatus(status);
        }
    }
}
