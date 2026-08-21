using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    public partial class MainWindow
    {
        private readonly HashSet<Guid> _selectedNodeIds = new HashSet<Guid>();
        private readonly Dictionary<Guid, Point> _sheetViewportOffsets =
            new Dictionary<Guid, Point>();

        private bool _suppressSheetSelectionChanged;
        private bool _isPanning;
        private MouseButton _panMouseButton;
        private Point _panStartPoint;
        private double _panStartHorizontalOffset;
        private double _panStartVerticalOffset;
        private bool _isLassoSelecting;
        private Point _lassoStartPoint;
        private Rectangle _lassoVisual;

        private void ResetSheetNavigationForLoadedProject()
        {
            CancelCanvasViewGestures();
            _sheetViewportOffsets.Clear();
            ClearNodeSelectionState();
            RefreshSheetSelector();
        }

        private void RefreshSheetSelector()
        {
            if (SheetSelector == null)
            {
                return;
            }

            _suppressSheetSelectionChanged = true;
            try
            {
                SheetSelector.ItemsSource = null;
                SheetSelector.ItemsSource = _project == null ? null : _project.Sheets;
                SheetSelector.SelectedItem = _sheet;
                SheetNameText.Text = _sheet == null ? string.Empty : _sheet.Name;
                SheetCountText.Text = _project == null || _project.Sheets == null
                    ? "0"
                    : _project.Sheets.Count.ToString();
            }
            finally
            {
                _suppressSheetSelectionChanged = false;
            }

            UpdateSheetCommandState();
        }

        private void UpdateSheetCommandState()
        {
            if (NewSheetButton == null)
            {
                return;
            }

            var hasSheet = _project != null && _sheet != null;
            NewSheetButton.IsEnabled = !_onlineBusy && _project != null;
            RenameSheetButton.IsEnabled = !_onlineBusy && hasSheet;
            DeleteSheetButton.IsEnabled =
                !_onlineBusy && hasSheet && _project.Sheets != null && _project.Sheets.Count > 1;
        }

        private void SheetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSheetSelectionChanged)
            {
                return;
            }

            var selected = SheetSelector.SelectedItem as CallSheet;
            if (selected == null || ReferenceEquals(selected, _sheet))
            {
                return;
            }

            if (!EnsureModelEditingAvailable() || IsCanvasViewGestureActive())
            {
                RefreshSheetSelector();
                return;
            }

            ActivateSheet(selected, true, "Открыт лист " + selected.Name);
        }

        private void NewSheet_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable() || _project == null)
            {
                return;
            }

            var defaultName = SheetEditingLogic.CreateUniqueDefaultName(_project);
            var name = ShowSheetNameDialog(
                "Новый лист вызовов",
                "Имя нового пустого листа FC",
                defaultName,
                null);
            if (name == null)
            {
                return;
            }

            CallSheet created = null;
            if (!TryCommitSemanticEdit(
                "Создание листа " + name,
                () => created = SheetEditingLogic.CreateSheet(_project, name)))
            {
                return;
            }

            _interactionDiagnostics.Clear();
            ActivateSheet(created, true, "Создан пустой лист FC " + created.Name);
        }

        private void RenameSheet_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable() || _project == null || _sheet == null)
            {
                return;
            }

            var editedSheet = _sheet;
            var dialog = new SheetPropertiesWindow(
                editedSheet.Name,
                editedSheet.Width,
                editedSheet.Height,
                candidate =>
                {
                    string error;
                    return SheetEditingLogic.TryValidateName(
                        _project,
                        editedSheet,
                        candidate,
                        out error)
                            ? string.Empty
                            : error;
                })
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var oldName = editedSheet.Name;
            var oldWidth = editedSheet.Width;
            var oldHeight = editedSheet.Height;
            if (string.Equals(dialog.ApprovedName, oldName, StringComparison.Ordinal) &&
                Math.Abs(dialog.ApprovedWidth - oldWidth) < 0.000001 &&
                Math.Abs(dialog.ApprovedHeight - oldHeight) < 0.000001)
            {
                return;
            }

            if (!TryCommitSemanticEdit(
                "Свойства листа " + oldName,
                () => SheetEditingLogic.EditProperties(
                    _project,
                    editedSheet,
                    dialog.ApprovedName,
                    dialog.ApprovedWidth,
                    dialog.ApprovedHeight)))
            {
                return;
            }

            _interactionDiagnostics.Clear();
            ApplyCurrentSheetExtentToCanvas();
            RefreshSheetSelector();
            RenderDiagram();
            RefreshCompilation(false);
            SetStatus(
                "Свойства листа обновлены: " + editedSheet.Name + " · " +
                Math.Round(editedSheet.Width) + "×" + Math.Round(editedSheet.Height));
        }

        private void DeleteSheet_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable() || _project == null || _sheet == null)
            {
                return;
            }

            if (_project.Sheets == null || _project.Sheets.Count <= 1)
            {
                SetStatus("В проекте должен оставаться минимум один лист вызовов");
                UpdateSheetCommandState();
                return;
            }

            var deleted = _sheet;
            var answer = MessageBox.Show(
                this,
                "Удалить лист «" + deleted.Name + "» вместе с " +
                deleted.Nodes.Count + " узлами и " + deleted.Wires.Count + " проводами?\n\n" +
                "Действие можно отменить через Ctrl+Z.",
                "Удаление листа",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            CallSheet next = null;
            if (!TryCommitSemanticEdit(
                "Удаление листа " + deleted.Name,
                () => next = SheetEditingLogic.DeleteSheet(_project, deleted)))
            {
                return;
            }

            _interactionDiagnostics.Clear();
            ActivateSheet(next, true, "Лист " + deleted.Name + " удалён");
        }

        private string ShowSheetNameDialog(
            string title,
            string prompt,
            string initialName,
            CallSheet editedSheet)
        {
            var dialog = new SheetNameWindow(
                title,
                prompt,
                initialName,
                candidate =>
                {
                    string error;
                    return SheetEditingLogic.TryValidateName(
                        _project,
                        editedSheet,
                        candidate,
                        out error)
                            ? string.Empty
                            : error;
                })
            {
                Owner = this
            };
            return dialog.ShowDialog() == true ? dialog.SheetName : null;
        }

        private void ActivateSheet(CallSheet sheet, bool refreshCompilation, string status)
        {
            if (sheet == null || _project == null || !_project.Sheets.Contains(sheet))
            {
                return;
            }

            CaptureCurrentSheetViewport();
            CancelCanvasViewGestures();
            CancelPendingConnection();
            ClearNodeSelectionState();
            _selectedWireId = null;
            _selectedGroupId = null;
            _sheet = sheet;
            ApplyCurrentSheetExtentToCanvas();
            RefreshSheetSelector();
            RenderDiagram();
            ApplyDiagramZoom();
            RestoreCurrentSheetViewport();
            if (refreshCompilation)
            {
                RefreshCompilation(false);
            }

            UpdateSelectionCommandState();
            if (!string.IsNullOrEmpty(status))
            {
                SetStatus(status);
            }
        }

        private void ApplyCurrentSheetExtentToCanvas()
        {
            if (_sheet == null || DiagramCanvas == null)
            {
                return;
            }

            DiagramCanvas.Width = Math.Max(
                SheetEditingLogic.MinimumSheetWidth,
                _sheet.Width);
            DiagramCanvas.Height = Math.Max(
                SheetEditingLogic.MinimumSheetHeight,
                _sheet.Height);
        }

        private void CaptureCurrentSheetViewport()
        {
            if (_sheet == null || DiagramScrollViewer == null)
            {
                return;
            }

            _sheetViewportOffsets[_sheet.Id] = new Point(
                DiagramScrollViewer.HorizontalOffset,
                DiagramScrollViewer.VerticalOffset);
        }

        private void RestoreCurrentSheetViewport()
        {
            if (_sheet == null || DiagramScrollViewer == null)
            {
                return;
            }

            var sheetId = _sheet.Id;
            Point offset;
            if (!_sheetViewportOffsets.TryGetValue(sheetId, out offset))
            {
                offset = new Point(0.0, 0.0);
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    if (_sheet == null || _sheet.Id != sheetId)
                    {
                        return;
                    }

                    DiagramScrollViewer.ScrollToHorizontalOffset(offset.X);
                    DiagramScrollViewer.ScrollToVerticalOffset(offset.Y);
                }));
        }

        private void DiagramScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var isMiddle = e.ChangedButton == MouseButton.Middle;
            var isSpaceLeft = e.ChangedButton == MouseButton.Left &&
                Keyboard.IsKeyDown(Key.Space);
            if ((!isMiddle && !isSpaceLeft) ||
                _onlineBusy ||
                _dragNode != null ||
                IsGroupDragActive() ||
                _isLassoSelecting)
            {
                return;
            }

            CancelPendingConnection();
            _isPanning = true;
            _panMouseButton = e.ChangedButton;
            _panStartPoint = e.GetPosition(DiagramScrollViewer);
            _panStartHorizontalOffset = DiagramScrollViewer.HorizontalOffset;
            _panStartVerticalOffset = DiagramScrollViewer.VerticalOffset;
            if (!DiagramScrollViewer.CaptureMouse())
            {
                _isPanning = false;
                return;
            }

            DiagramScrollViewer.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private void DiagramScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning)
            {
                return;
            }

            var buttonPressed = _panMouseButton == MouseButton.Middle
                ? e.MiddleButton == MouseButtonState.Pressed
                : e.LeftButton == MouseButtonState.Pressed;
            if (!buttonPressed || _onlineBusy || _dragNode != null || IsGroupDragActive())
            {
                EndCanvasPan();
                e.Handled = true;
                return;
            }

            var current = e.GetPosition(DiagramScrollViewer);
            DiagramScrollViewer.ScrollToHorizontalOffset(
                Math.Max(0.0, _panStartHorizontalOffset - (current.X - _panStartPoint.X)));
            DiagramScrollViewer.ScrollToVerticalOffset(
                Math.Max(0.0, _panStartVerticalOffset - (current.Y - _panStartPoint.Y)));
            e.Handled = true;
        }

        private void DiagramScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning && e.ChangedButton == _panMouseButton)
            {
                EndCanvasPan();
                e.Handled = true;
            }
        }

        private void DiagramScrollViewer_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                EndCanvasPan();
            }
        }

        private void EndCanvasPan()
        {
            if (!_isPanning)
            {
                return;
            }

            _isPanning = false;
            DiagramScrollViewer.ClearValue(CursorProperty);
            if (DiagramScrollViewer.IsMouseCaptured)
            {
                DiagramScrollViewer.ReleaseMouseCapture();
            }

            CaptureCurrentSheetViewport();
        }

        private bool BeginLassoSelection(MouseButtonEventArgs e)
        {
            if (e == null ||
                e.ChangedButton != MouseButton.Left ||
                e.OriginalSource != DiagramCanvas ||
                Keyboard.IsKeyDown(Key.Space) ||
                _onlineBusy ||
                _dragNode != null ||
                IsGroupDragActive() ||
                _isPanning)
            {
                return false;
            }

            CancelPendingConnection();
            DiagramCanvas.Focus();
            ClearDiagramSelection();
            _lassoStartPoint = e.GetPosition(DiagramCanvas);
            _lassoVisual = new Rectangle
            {
                Width = 0.0,
                Height = 0.0,
                Fill = new SolidColorBrush(Color.FromArgb(42, 54, 209, 244)),
                Stroke = GetBrush("AccentBrush"),
                StrokeThickness = 1.0,
                StrokeDashArray = new DoubleCollection { 4.0, 3.0 },
                IsHitTestVisible = false
            };
            Panel.SetZIndex(_lassoVisual, 100);
            Canvas.SetLeft(_lassoVisual, _lassoStartPoint.X);
            Canvas.SetTop(_lassoVisual, _lassoStartPoint.Y);
            DiagramCanvas.Children.Add(_lassoVisual);
            _isLassoSelecting = DiagramCanvas.CaptureMouse();
            if (!_isLassoSelecting)
            {
                DiagramCanvas.Children.Remove(_lassoVisual);
                _lassoVisual = null;
                return false;
            }

            return true;
        }

        private void DiagramCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isLassoSelecting)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed ||
                _onlineBusy ||
                _dragNode != null ||
                IsGroupDragActive())
            {
                CompleteLassoSelection(false);
                e.Handled = true;
                return;
            }

            UpdateLassoVisual(e.GetPosition(DiagramCanvas));
            e.Handled = true;
        }

        private void DiagramCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isLassoSelecting)
            {
                UpdateLassoVisual(e.GetPosition(DiagramCanvas));
                CompleteLassoSelection(true);
                e.Handled = true;
            }
        }

        private void DiagramCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isLassoSelecting)
            {
                CompleteLassoSelection(false);
            }
        }

        private void UpdateLassoVisual(Point current)
        {
            if (_lassoVisual == null)
            {
                return;
            }

            var left = Math.Min(_lassoStartPoint.X, current.X);
            var top = Math.Min(_lassoStartPoint.Y, current.Y);
            _lassoVisual.Width = Math.Abs(current.X - _lassoStartPoint.X);
            _lassoVisual.Height = Math.Abs(current.Y - _lassoStartPoint.Y);
            Canvas.SetLeft(_lassoVisual, left);
            Canvas.SetTop(_lassoVisual, top);
        }

        private void CompleteLassoSelection(bool commitSelection)
        {
            if (!_isLassoSelecting)
            {
                return;
            }

            var visual = _lassoVisual;
            var selectionBounds = visual == null
                ? Rect.Empty
                : new Rect(
                    Canvas.GetLeft(visual),
                    Canvas.GetTop(visual),
                    visual.Width,
                    visual.Height);
            _isLassoSelecting = false;
            _lassoVisual = null;
            if (visual != null)
            {
                DiagramCanvas.Children.Remove(visual);
            }

            if (DiagramCanvas.IsMouseCaptured)
            {
                DiagramCanvas.ReleaseMouseCapture();
            }

            if (!commitSelection || _sheet == null)
            {
                return;
            }

            ClearNodeSelectionState();
            _selectedWireId = null;
            if (!selectionBounds.IsEmpty &&
                selectionBounds.Width >= 4.0 &&
                selectionBounds.Height >= 4.0)
            {
                foreach (var node in _sheet.Nodes)
                {
                    var nodeBounds = new Rect(
                        node.X,
                        node.Y,
                        GetNodeWidth(node),
                        GetNodeHeight(node));
                    if (selectionBounds.IntersectsWith(nodeBounds))
                    {
                        _selectedNodeIds.Add(node.Id);
                    }
                }
            }

            _selectedNodeId = _sheet.Nodes
                .Where(node => _selectedNodeIds.Contains(node.Id))
                .Select(node => (Guid?)node.Id)
                .FirstOrDefault();
            UpdateSelectionVisuals();
            UpdateSelectionCommandState();
            SetStatus(_selectedNodeIds.Count == 0
                ? "Выделение очищено"
                : "Выбрано узлов: " + _selectedNodeIds.Count);
        }

        private void CancelCanvasViewGestures()
        {
            if (_isLassoSelecting)
            {
                CompleteLassoSelection(false);
            }

            if (_isPanning)
            {
                EndCanvasPan();
            }
        }

        private bool IsCanvasViewGestureActive()
        {
            return _isPanning || _isLassoSelecting;
        }

        private bool IsNodeSelected(Guid nodeId)
        {
            return _selectedNodeIds.Contains(nodeId) ||
                (_selectedNodeIds.Count == 0 &&
                 _selectedNodeId.HasValue &&
                 _selectedNodeId.Value == nodeId);
        }

        private void SetSingleNodeSelectionState(Guid nodeId)
        {
            _selectedNodeIds.Clear();
            _selectedNodeIds.Add(nodeId);
            _selectedNodeId = nodeId;
            _selectedGroupId = null;
        }

        private void ClearNodeSelectionState()
        {
            _selectedNodeIds.Clear();
            _selectedNodeId = null;
        }

        private void NormalizeNodeSelectionForCurrentSheet()
        {
            if (_sheet == null)
            {
                ClearNodeSelectionState();
                return;
            }

            var available = new HashSet<Guid>(_sheet.Nodes.Select(item => item.Id));
            _selectedNodeIds.RemoveWhere(item => !available.Contains(item));
            if (_selectedNodeIds.Count == 0 &&
                _selectedNodeId.HasValue &&
                available.Contains(_selectedNodeId.Value))
            {
                _selectedNodeIds.Add(_selectedNodeId.Value);
            }

            if (_selectedNodeIds.Count == 0)
            {
                _selectedNodeId = null;
            }
            else if (!_selectedNodeId.HasValue || !_selectedNodeIds.Contains(_selectedNodeId.Value))
            {
                _selectedNodeId = _sheet.Nodes
                    .Where(item => _selectedNodeIds.Contains(item.Id))
                    .Select(item => (Guid?)item.Id)
                    .FirstOrDefault();
            }
        }

        private IList<Guid> GetSelectedNodeIds()
        {
            NormalizeNodeSelectionForCurrentSheet();
            return _sheet == null
                ? new List<Guid>()
                : _sheet.Nodes
                    .Where(item => _selectedNodeIds.Contains(item.Id))
                    .Select(item => item.Id)
                    .ToList();
        }

        private static Guid DetermineHistoryActiveSheetId(
            DiagramProject original,
            DiagramProject restored,
            Guid previousActiveSheetId)
        {
            var originalIds = new HashSet<Guid>(
                (original.Sheets ?? new List<CallSheet>()).Select(item => item.Id));
            var added = (restored.Sheets ?? new List<CallSheet>())
                .Where(item => !originalIds.Contains(item.Id))
                .ToList();
            if (added.Count == 1)
            {
                return added[0].Id;
            }

            if ((restored.Sheets ?? new List<CallSheet>())
                .Any(item => item.Id == previousActiveSheetId))
            {
                return previousActiveSheetId;
            }

            var restoredSheets = restored.Sheets ?? new List<CallSheet>();
            var previousIndex = (original.Sheets ?? new List<CallSheet>())
                .FindIndex(item => item.Id == previousActiveSheetId);
            if (previousIndex >= 0 && restoredSheets.Count != 0)
            {
                return restoredSheets[Math.Min(previousIndex, restoredSheets.Count - 1)].Id;
            }

            var first = restoredSheets.FirstOrDefault();
            return first == null ? Guid.Empty : first.Id;
        }
    }
}
