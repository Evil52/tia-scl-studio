using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TiaSclStudio.Diagram.History;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    public partial class MainWindow
    {
        private const double GroupPadding = 34.0;
        private const double DefaultGroupWidth = 420.0;
        private const double DefaultGroupHeight = 260.0;
        private const double GroupResizeCornerScreenSize = 11.0;
        private const double GroupResizeEdgeScreenLength = 24.0;
        private const double GroupResizeEdgeScreenThickness = 9.0;

        [Flags]
        private enum GroupResizeHandle
        {
            None = 0,
            Left = 1,
            Top = 2,
            Right = 4,
            Bottom = 8
        }

        private readonly Dictionary<Guid, FrameworkElement> _groupVisuals =
            new Dictionary<Guid, FrameworkElement>();

        private Guid? _selectedGroupId;
        private DiagramGroup _dragGroup;
        private FrameworkElement _dragGroupElement;
        private Point _dragGroupStartMouse;
        private Vector _dragGroupAppliedDelta;
        private DiagramProject _dragGroupBeforeProject;
        private bool _dragGroupMoved;

        private Grid _groupResizeAdorner;
        private Guid? _groupResizeAdornerGroupId;
        private DiagramGroup _resizeGroup;
        private FrameworkElement _resizeCaptureElement;
        private GroupResizeHandle _resizeHandle;
        private Point _resizeStartMouse;
        private Rect _resizeStartBounds;
        private DiagramProject _resizeBeforeProject;
        private bool _resizeChanged;

        private bool IsGroupDragActive()
        {
            return _dragGroup != null || _resizeGroup != null;
        }

        private void ResetGroupEditingForLoadedProject()
        {
            _selectedGroupId = null;
            ClearGroupDragState();
            ClearGroupResizeState();
            RemoveGroupResizeAdorner();
            _groupVisuals.Clear();
        }

        private void NormalizeGroupSelectionForCurrentSheet()
        {
            if (!_selectedGroupId.HasValue || _sheet == null ||
                !_sheet.Groups.Any(group =>
                    group != null && group.Id == _selectedGroupId.Value))
            {
                _selectedGroupId = null;
            }
        }

        private void RenderGroups()
        {
            _groupVisuals.Clear();
            _groupResizeAdorner = null;
            _groupResizeAdornerGroupId = null;
            if (_sheet == null)
            {
                return;
            }

            foreach (var group in _sheet.Groups
                .Where(item => item != null)
                .OrderBy(GetGroupDepth)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id))
            {
                var renderBounds = GetSafeGroupRenderBounds(group);
                var visual = CreateGroupVisual(
                    group,
                    renderBounds.Width,
                    renderBounds.Height);
                Canvas.SetLeft(visual, renderBounds.X);
                Canvas.SetTop(visual, renderBounds.Y);
                Panel.SetZIndex(visual, Math.Min(1, GetGroupDepth(group)));
                DiagramCanvas.Children.Add(visual);
                _groupVisuals[group.Id] = visual;
            }
        }

        private Rect GetSafeGroupRenderBounds(DiagramGroup group)
        {
            var canvasWidth = IsFinite(DiagramCanvas.Width) && DiagramCanvas.Width > 0.0
                ? DiagramCanvas.Width
                : 1600.0;
            var canvasHeight = IsFinite(DiagramCanvas.Height) && DiagramCanvas.Height > 0.0
                ? DiagramCanvas.Height
                : 900.0;
            var x = IsFinite(group.X)
                ? Clamp(group.X, 0.0, Math.Max(0.0, canvasWidth - 1.0))
                : 0.0;
            var y = IsFinite(group.Y)
                ? Clamp(group.Y, 0.0, Math.Max(0.0, canvasHeight - 1.0))
                : 0.0;
            var maxWidth = Math.Max(1.0, canvasWidth - x);
            var maxHeight = Math.Max(1.0, canvasHeight - y);
            var width = IsFinite(group.Width) && group.Width > 0.0
                ? Math.Min(group.Width, maxWidth)
                : Math.Min(DefaultGroupWidth, maxWidth);
            var height = IsFinite(group.Height) && group.Height > 0.0
                ? Math.Min(group.Height, maxHeight)
                : Math.Min(DefaultGroupHeight, maxHeight);
            return new Rect(x, y, width, height);
        }

        private FrameworkElement CreateGroupVisual(
            DiagramGroup group,
            double renderWidth,
            double renderHeight)
        {
            var selected = _selectedGroupId.HasValue &&
                _selectedGroupId.Value == group.Id;
            var root = new Grid
            {
                Width = renderWidth,
                Height = renderHeight,
                Tag = group
            };
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(58, 21, 52, 65)),
                BorderBrush = selected ? GetBrush("AccentBrush") : GetBrush("BorderStrongBrush"),
                BorderThickness = selected ? new Thickness(2) : new Thickness(1),
                CornerRadius = new CornerRadius(8),
                IsHitTestVisible = false,
                ToolTip = string.IsNullOrWhiteSpace(group.Comment)
                    ? "Область: " + group.Title
                    : group.Title + Environment.NewLine + group.Comment
            };

            var header = new Border
            {
                Height = 34.0,
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(178, 16, 39, 50)),
                BorderBrush = GetBrush("BorderBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(7, 7, 0, 0),
                Padding = new Thickness(11, 0, 9, 0),
                Tag = group,
                Cursor = Cursors.SizeAll,
                ToolTip = string.IsNullOrWhiteSpace(group.Comment)
                    ? "Область: " + group.Title
                    : group.Title + Environment.NewLine + group.Comment
            };
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.Children.Add(new TextBlock
            {
                Text = group.Title,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("TextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var badge = new TextBlock
            {
                Text = group.ParentGroupId == Guid.Empty ? "REGION" : "NESTED",
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 8,
                Foreground = GetBrush("AccentBrush")
            };
            Grid.SetColumn(badge, 1);
            headerGrid.Children.Add(badge);
            header.Child = headerGrid;
            root.Children.Add(border);

            if (!string.IsNullOrWhiteSpace(group.Comment))
            {
                var comment = new TextBlock
                {
                    Text = group.Comment,
                    Margin = new Thickness(11, 42, 11, 8),
                    VerticalAlignment = VerticalAlignment.Top,
                    MaxHeight = 38,
                    IsHitTestVisible = false,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontSize = 9,
                    Foreground = GetBrush("TextDimBrush"),
                    Opacity = 0.72
                };
                root.Children.Add(comment);
            }

            root.Children.Add(header);
            header.PreviewMouseLeftButtonDown += Group_MouseLeftButtonDown;
            header.MouseMove += Group_MouseMove;
            header.MouseLeftButtonUp += Group_MouseLeftButtonUp;
            header.LostMouseCapture += Group_LostMouseCapture;
            return root;
        }

        private void UpdateGroupResizeAdorner()
        {
            if (_sheet == null || !_selectedGroupId.HasValue || DiagramCanvas == null)
            {
                RemoveGroupResizeAdorner();
                return;
            }

            var group = _sheet.Groups.FirstOrDefault(item =>
                item != null && item.Id == _selectedGroupId.Value);
            if (group == null)
            {
                RemoveGroupResizeAdorner();
                return;
            }

            if (_groupResizeAdorner == null ||
                !_groupResizeAdornerGroupId.HasValue ||
                _groupResizeAdornerGroupId.Value != group.Id ||
                !DiagramCanvas.Children.Contains(_groupResizeAdorner))
            {
                RemoveGroupResizeAdorner();
                _groupResizeAdorner = CreateGroupResizeAdorner(group);
                _groupResizeAdornerGroupId = group.Id;
                Panel.SetZIndex(_groupResizeAdorner, 40);
                DiagramCanvas.Children.Add(_groupResizeAdorner);
            }

            UpdateGroupResizeAdornerVisual(group);
        }

        private Grid CreateGroupResizeAdorner(DiagramGroup group)
        {
            var adorner = new Grid
            {
                Tag = group.Id,
                Background = null,
                ClipToBounds = false,
                IsHitTestVisible = true
            };

            AddGroupResizeHandle(
                adorner,
                GroupResizeHandle.Left | GroupResizeHandle.Top,
                HorizontalAlignment.Left,
                VerticalAlignment.Top);
            AddGroupResizeHandle(
                adorner,
                GroupResizeHandle.Top,
                HorizontalAlignment.Center,
                VerticalAlignment.Top);
            AddGroupResizeHandle(
                adorner,
                GroupResizeHandle.Right | GroupResizeHandle.Top,
                HorizontalAlignment.Right,
                VerticalAlignment.Top);
            AddGroupResizeHandle(
                adorner,
                GroupResizeHandle.Right,
                HorizontalAlignment.Right,
                VerticalAlignment.Center);
            AddGroupResizeHandle(
                adorner,
                GroupResizeHandle.Right | GroupResizeHandle.Bottom,
                HorizontalAlignment.Right,
                VerticalAlignment.Bottom);
            AddGroupResizeHandle(
                adorner,
                GroupResizeHandle.Bottom,
                HorizontalAlignment.Center,
                VerticalAlignment.Bottom);
            AddGroupResizeHandle(
                adorner,
                GroupResizeHandle.Left | GroupResizeHandle.Bottom,
                HorizontalAlignment.Left,
                VerticalAlignment.Bottom);
            AddGroupResizeHandle(
                adorner,
                GroupResizeHandle.Left,
                HorizontalAlignment.Left,
                VerticalAlignment.Center);
            return adorner;
        }

        private void AddGroupResizeHandle(
            Grid adorner,
            GroupResizeHandle handle,
            HorizontalAlignment horizontalAlignment,
            VerticalAlignment verticalAlignment)
        {
            var element = new Border
            {
                Tag = handle,
                Uid = "GroupResizeHandle-" + GetGroupResizeHandleName(handle),
                HorizontalAlignment = horizontalAlignment,
                VerticalAlignment = verticalAlignment,
                Background = GetBrush("AccentBrush"),
                BorderBrush = GetBrush("WindowBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Cursor = GetGroupResizeCursor(handle),
                ToolTip = "Изменить размер области",
                SnapsToDevicePixels = true
            };
            element.PreviewMouseLeftButtonDown += GroupResize_MouseLeftButtonDown;
            element.MouseMove += GroupResize_MouseMove;
            element.MouseLeftButtonUp += GroupResize_MouseLeftButtonUp;
            element.LostMouseCapture += GroupResize_LostMouseCapture;
            Panel.SetZIndex(element, 1);
            adorner.Children.Add(element);
        }

        private void UpdateGroupResizeAdornerVisual(DiagramGroup group)
        {
            if (_groupResizeAdorner == null || group == null)
            {
                return;
            }

            _groupResizeAdorner.Width = group.Width;
            _groupResizeAdorner.Height = group.Height;
            Canvas.SetLeft(_groupResizeAdorner, group.X);
            Canvas.SetTop(_groupResizeAdorner, group.Y);

            var zoom = _sheet == null ? 1.0 : NormalizeZoom(_sheet.Zoom);
            var cornerSize = GroupResizeCornerScreenSize / zoom;
            var edgeLength = GroupResizeEdgeScreenLength / zoom;
            var edgeThickness = GroupResizeEdgeScreenThickness / zoom;
            foreach (var element in _groupResizeAdorner.Children.OfType<Border>())
            {
                var handle = element.Tag is GroupResizeHandle
                    ? (GroupResizeHandle)element.Tag
                    : GroupResizeHandle.None;
                var horizontalEdge = handle == GroupResizeHandle.Top ||
                    handle == GroupResizeHandle.Bottom;
                var verticalEdge = handle == GroupResizeHandle.Left ||
                    handle == GroupResizeHandle.Right;
                element.Width = horizontalEdge
                    ? edgeLength
                    : (verticalEdge ? edgeThickness : cornerSize);
                element.Height = verticalEdge
                    ? edgeLength
                    : (horizontalEdge ? edgeThickness : cornerSize);
            }
        }

        private void RemoveGroupResizeAdorner()
        {
            if (_groupResizeAdorner != null &&
                DiagramCanvas != null &&
                DiagramCanvas.Children.Contains(_groupResizeAdorner))
            {
                DiagramCanvas.Children.Remove(_groupResizeAdorner);
            }

            _groupResizeAdorner = null;
            _groupResizeAdornerGroupId = null;
        }

        private static Cursor GetGroupResizeCursor(GroupResizeHandle handle)
        {
            if (handle == (GroupResizeHandle.Left | GroupResizeHandle.Top) ||
                handle == (GroupResizeHandle.Right | GroupResizeHandle.Bottom))
            {
                return Cursors.SizeNWSE;
            }

            if (handle == (GroupResizeHandle.Right | GroupResizeHandle.Top) ||
                handle == (GroupResizeHandle.Left | GroupResizeHandle.Bottom))
            {
                return Cursors.SizeNESW;
            }

            return handle == GroupResizeHandle.Left || handle == GroupResizeHandle.Right
                ? Cursors.SizeWE
                : Cursors.SizeNS;
        }

        private static string GetGroupResizeHandleName(GroupResizeHandle handle)
        {
            if (handle == (GroupResizeHandle.Left | GroupResizeHandle.Top))
            {
                return "NorthWest";
            }

            if (handle == GroupResizeHandle.Top)
            {
                return "North";
            }

            if (handle == (GroupResizeHandle.Right | GroupResizeHandle.Top))
            {
                return "NorthEast";
            }

            if (handle == GroupResizeHandle.Right)
            {
                return "East";
            }

            if (handle == (GroupResizeHandle.Right | GroupResizeHandle.Bottom))
            {
                return "SouthEast";
            }

            if (handle == GroupResizeHandle.Bottom)
            {
                return "South";
            }

            if (handle == (GroupResizeHandle.Left | GroupResizeHandle.Bottom))
            {
                return "SouthWest";
            }

            return "West";
        }

        private int GetGroupDepth(DiagramGroup group)
        {
            if (_sheet == null || group == null)
            {
                return 0;
            }

            var depth = 0;
            var visited = new HashSet<Guid>();
            var parentId = group.ParentGroupId;
            while (parentId != Guid.Empty && visited.Add(parentId))
            {
                var parent = _sheet.Groups.FirstOrDefault(item =>
                    item != null && item.Id == parentId);
                if (parent == null)
                {
                    break;
                }

                depth++;
                parentId = parent.ParentGroupId;
            }

            return depth;
        }

        private void AddGroup_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable() || _sheet == null)
            {
                return;
            }

            var selectedIds = GetSelectedNodeIds();
            var title = CreateUniqueGroupTitle();
            var editor = new GroupEditorWindow(title, string.Empty) { Owner = this };
            if (editor.ShowDialog() != true)
            {
                return;
            }

            Rect bounds;
            if (selectedIds.Count == 0)
            {
                var center = GetViewportCenterInDiagram();
                bounds = new Rect(
                    center.X - DefaultGroupWidth / 2.0,
                    center.Y - DefaultGroupHeight / 2.0,
                    DefaultGroupWidth,
                    DefaultGroupHeight);
            }
            else
            {
                bounds = CalculateGroupBounds(selectedIds);
            }

            bounds = ClampGroupBounds(bounds, selectedIds);
            DiagramGroup draft;
            try
            {
                draft = GroupEditingLogic.CreateDraft(
                    editor.ApprovedTitle,
                    editor.ApprovedComment,
                    bounds.X,
                    bounds.Y,
                    bounds.Width,
                    bounds.Height);
            }
            catch (Exception exception)
            {
                ReportUnexpectedEditFailure("Создание области", exception);
                return;
            }

            if (!TryCommitSemanticEdit(
                "Создание области " + draft.Title,
                () => GroupEditingLogic.Add(_project, _sheet.Id, draft, selectedIds)))
            {
                return;
            }

            ClearNodeSelectionState();
            _selectedWireId = null;
            _selectedGroupId = draft.Id;
            RenderDiagram();
            RefreshCompilation(false);
            SetStatus(selectedIds.Count == 0
                ? "Добавлена пустая область " + draft.Title
                : "Узлы объединены в область " + draft.Title);
        }

        private void Ungroup_Click(object sender, RoutedEventArgs e)
        {
            UngroupSelectedGroup();
        }

        private void UngroupSelectedGroup()
        {
            if (!EnsureModelEditingAvailable() || _sheet == null || !_selectedGroupId.HasValue)
            {
                return;
            }

            var group = _sheet.Groups.FirstOrDefault(item =>
                item != null && item.Id == _selectedGroupId.Value);
            if (group == null)
            {
                _selectedGroupId = null;
                UpdateSelectionCommandState();
                return;
            }

            var title = group.Title;
            if (!TryCommitSemanticEdit(
                "Расформирование области " + title,
                () => GroupEditingLogic.Ungroup(_project, _sheet.Id, group.Id)))
            {
                return;
            }

            _selectedGroupId = null;
            RenderDiagram();
            RefreshCompilation(false);
            SetStatus("Область " + title + " расформирована; узлы и провода сохранены");
        }

        private void EditSelectedGroup()
        {
            if (!EnsureModelEditingAvailable() || _sheet == null || !_selectedGroupId.HasValue)
            {
                return;
            }

            var group = _sheet.Groups.FirstOrDefault(item =>
                item != null && item.Id == _selectedGroupId.Value);
            if (group == null)
            {
                return;
            }

            var editor = new GroupEditorWindow(
                group.Title,
                group.Comment,
                group.X,
                group.Y,
                group.Width,
                group.Height)
            {
                Owner = this
            };
            if (editor.ShowDialog() != true)
            {
                return;
            }

            var geometryChanged =
                !AreClose(group.X, editor.ApprovedX) ||
                !AreClose(group.Y, editor.ApprovedY) ||
                !AreClose(group.Width, editor.ApprovedWidth) ||
                !AreClose(group.Height, editor.ApprovedHeight);

            if (!TryCommitSemanticEdit(
                "Изменение области " + group.Title,
                () =>
                {
                    if (geometryChanged)
                    {
                        GroupEditingLogic.EditGeometry(
                            _project,
                            _sheet.Id,
                            group.Id,
                            editor.ApprovedX,
                            editor.ApprovedY,
                            editor.ApprovedWidth,
                            editor.ApprovedHeight);
                    }

                    GroupEditingLogic.Edit(
                        _project,
                        _sheet.Id,
                        group.Id,
                        editor.ApprovedTitle,
                        editor.ApprovedComment);
                }))
            {
                return;
            }

            ApplyCurrentSheetExtentToCanvas();
            RenderDiagram();
            RefreshCompilation(false);
            SetStatus("Область обновлена");
        }

        private void SelectGroup(Guid groupId)
        {
            DiagramCanvas.Focus();
            CancelPendingConnection();
            ClearNodeSelectionState();
            _selectedWireId = null;
            _selectedGroupId = groupId;
            UpdateSelectionVisuals();
            UpdateSelectionCommandState();
        }

        private void UpdateGroupSelectionVisuals()
        {
            foreach (var pair in _groupVisuals)
            {
                var root = pair.Value as Grid;
                var border = root == null
                    ? null
                    : root.Children.OfType<Border>().FirstOrDefault(item => !item.IsHitTestVisible);
                if (border == null)
                {
                    continue;
                }

                var selected = _selectedGroupId.HasValue &&
                    _selectedGroupId.Value == pair.Key;
                border.BorderBrush = selected ? GetBrush("AccentBrush") : GetBrush("BorderStrongBrush");
                border.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
            }

            UpdateGroupResizeAdorner();
        }

        private void UpdateGroupCommandState()
        {
            if (AddGroupButton != null)
            {
                AddGroupButton.IsEnabled = !_onlineBusy && _sheet != null;
            }

            if (UngroupButton != null)
            {
                UngroupButton.IsEnabled = !_onlineBusy && _selectedGroupId.HasValue;
            }
        }

        private void GroupResize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            var handle = element != null && element.Tag is GroupResizeHandle
                ? (GroupResizeHandle)element.Tag
                : GroupResizeHandle.None;
            if (e.ChangedButton != MouseButton.Left ||
                handle == GroupResizeHandle.None ||
                !_selectedGroupId.HasValue)
            {
                return;
            }

            BeginGroupResize(
                _selectedGroupId.Value,
                handle,
                element,
                e.GetPosition(DiagramCanvas));
            e.Handled = true;
        }

        private bool BeginGroupResize(
            Guid groupId,
            GroupResizeHandle handle,
            FrameworkElement captureElement,
            Point logicalPoint)
        {
            if (!EnsureModelEditingAvailable() ||
                _sheet == null ||
                captureElement == null ||
                handle == GroupResizeHandle.None)
            {
                return false;
            }

            var group = _sheet.Groups.FirstOrDefault(item =>
                item != null && item.Id == groupId);
            if (group == null)
            {
                return false;
            }

            DiagramProject before;
            try
            {
                before = DiagramProjectSnapshot.Clone(_project);
            }
            catch (Exception exception)
            {
                ReportUnexpectedEditFailure("Начало изменения размера области", exception);
                return false;
            }

            SelectGroup(group.Id);
            _resizeGroup = group;
            _resizeCaptureElement = captureElement;
            _resizeHandle = handle;
            _resizeStartMouse = logicalPoint;
            _resizeStartBounds = new Rect(group.X, group.Y, group.Width, group.Height);
            _resizeBeforeProject = before;
            _resizeChanged = false;
            if (!captureElement.CaptureMouse())
            {
                ClearGroupResizeState();
                SetStatus("Не удалось начать изменение размера области");
                return false;
            }

            UpdateViewportCommandState();
            return true;
        }

        private void GroupResize_MouseMove(object sender, MouseEventArgs e)
        {
            if (_resizeGroup == null || !ReferenceEquals(sender, _resizeCaptureElement))
            {
                return;
            }

            if (_onlineBusy)
            {
                CancelGroupDragForOnlineOperation();
                e.Handled = true;
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndGroupResize();
                e.Handled = true;
                return;
            }

            UpdateGroupResize(e.GetPosition(DiagramCanvas));
            e.Handled = true;
        }

        private bool UpdateGroupResize(Point logicalPoint)
        {
            if (_resizeGroup == null || _sheet == null)
            {
                return false;
            }

            var delta = logicalPoint - _resizeStartMouse;
            Rect candidate;
            try
            {
                candidate = CalculateGroupResizeBounds(
                    _resizeStartBounds,
                    _resizeHandle,
                    delta);
                GroupEditingLogic.ResizeBounds(
                    _project,
                    _sheet.Id,
                    _resizeGroup.Id,
                    candidate.X,
                    candidate.Y,
                    candidate.Width,
                    candidate.Height);
            }
            catch (ArgumentException exception)
            {
                SetStatus("Размер области не применён: " + exception.Message);
                return false;
            }
            catch (InvalidOperationException exception)
            {
                SetStatus("Размер области не применён: " + exception.Message);
                return false;
            }
            catch (Exception exception)
            {
                CancelGroupDragForOnlineOperation();
                ReportUnexpectedEditFailure("Изменение размера области", exception);
                return false;
            }

            ApplyCurrentSheetExtentToCanvas();
            FrameworkElement visual;
            if (_groupVisuals.TryGetValue(_resizeGroup.Id, out visual))
            {
                visual.Width = _resizeGroup.Width;
                visual.Height = _resizeGroup.Height;
                Canvas.SetLeft(visual, _resizeGroup.X);
                Canvas.SetTop(visual, _resizeGroup.Y);
            }

            UpdateGroupResizeAdornerVisual(_resizeGroup);
            _resizeChanged = _resizeChanged ||
                !AreClose(_resizeStartBounds.X, _resizeGroup.X) ||
                !AreClose(_resizeStartBounds.Y, _resizeGroup.Y) ||
                !AreClose(_resizeStartBounds.Width, _resizeGroup.Width) ||
                !AreClose(_resizeStartBounds.Height, _resizeGroup.Height);
            return true;
        }

        private static Rect CalculateGroupResizeBounds(
            Rect start,
            GroupResizeHandle handle,
            Vector delta)
        {
            if (!IsFinite(delta.X) || !IsFinite(delta.Y))
            {
                throw new ArgumentOutOfRangeException(
                    "delta",
                    "Resize delta must be finite.");
            }

            var x = start.X;
            var y = start.Y;
            var width = start.Width;
            var height = start.Height;
            if ((handle & GroupResizeHandle.Left) != 0)
            {
                var right = start.Right;
                width = Math.Max(GroupEditingLogic.MinimumWidth, start.Width - delta.X);
                x = right - width;
                if (x < 0.0)
                {
                    x = 0.0;
                    width = right;
                }
            }
            else if ((handle & GroupResizeHandle.Right) != 0)
            {
                width = Math.Max(GroupEditingLogic.MinimumWidth, start.Width + delta.X);
            }

            if ((handle & GroupResizeHandle.Top) != 0)
            {
                var bottom = start.Bottom;
                height = Math.Max(GroupEditingLogic.MinimumHeight, start.Height - delta.Y);
                y = bottom - height;
                if (y < 0.0)
                {
                    y = 0.0;
                    height = bottom;
                }
            }
            else if ((handle & GroupResizeHandle.Bottom) != 0)
            {
                height = Math.Max(GroupEditingLogic.MinimumHeight, start.Height + delta.Y);
            }

            return new Rect(x, y, width, height);
        }

        private void GroupResize_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_resizeGroup != null && e.ChangedButton == MouseButton.Left)
            {
                EndGroupResize();
                e.Handled = true;
            }
        }

        private void GroupResize_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_resizeGroup != null && !_onlineBusy)
            {
                EndGroupResize();
            }
        }

        private void EndGroupResize()
        {
            if (_resizeGroup == null)
            {
                return;
            }

            var title = _resizeGroup.Title;
            var changed = _resizeChanged;
            var before = _resizeBeforeProject;
            var element = _resizeCaptureElement;
            ClearGroupResizeState();
            if (element != null && element.IsMouseCaptured)
            {
                element.ReleaseMouseCapture();
            }

            if (changed && before != null)
            {
                var recorded = _editHistory.Record(
                    "Изменение размера области " + title,
                    before,
                    _project);
                UpdateHistoryCommandState();
                if (recorded)
                {
                    RenderDiagram();
                    RefreshCompilation(false);
                    SetStatus("Размер области " + title + " изменён");
                }
                else
                {
                    UpdateGroupResizeAdorner();
                    UpdateViewportCommandState();
                    SetStatus("Размер области не изменён");
                }
            }
            else
            {
                UpdateGroupResizeAdorner();
                UpdateViewportCommandState();
            }
        }

        private void ClearGroupResizeState()
        {
            _resizeGroup = null;
            _resizeCaptureElement = null;
            _resizeHandle = GroupResizeHandle.None;
            _resizeStartMouse = new Point();
            _resizeStartBounds = Rect.Empty;
            _resizeBeforeProject = null;
            _resizeChanged = false;
        }

        private static bool AreClose(double first, double second)
        {
            return Math.Abs(first - second) < 0.000001;
        }

        private void Group_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!EnsureModelEditingAvailable() || e.ChangedButton != MouseButton.Left)
            {
                e.Handled = true;
                return;
            }

            var element = sender as FrameworkElement;
            var group = element == null ? null : element.Tag as DiagramGroup;
            if (element == null || group == null || _sheet == null)
            {
                return;
            }

            SelectGroup(group.Id);
            if (e.ClickCount >= 2)
            {
                EditSelectedGroup();
                e.Handled = true;
                return;
            }

            try
            {
                _dragGroupBeforeProject = DiagramProjectSnapshot.Clone(_project);
            }
            catch (Exception exception)
            {
                ReportUnexpectedEditFailure("Начало перемещения области", exception);
                e.Handled = true;
                return;
            }

            _dragGroup = group;
            _dragGroupElement = element;
            _dragGroupStartMouse = e.GetPosition(DiagramCanvas);
            _dragGroupAppliedDelta = new Vector();
            _dragGroupMoved = false;
            if (!element.CaptureMouse())
            {
                ClearGroupDragState();
                SetStatus("Не удалось начать перемещение области");
            }

            e.Handled = true;
        }

        private void Group_MouseMove(object sender, MouseEventArgs e)
        {
            if (_onlineBusy)
            {
                CancelGroupDragForOnlineOperation();
                e.Handled = true;
                return;
            }

            if (_dragGroup == null || _dragGroupElement == null || _sheet == null)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndGroupDrag();
                return;
            }

            var current = e.GetPosition(DiagramCanvas);
            var desiredTotal = current - _dragGroupStartMouse;
            if (!_dragGroupMoved &&
                Math.Abs(desiredTotal.X) <= 2.0 &&
                Math.Abs(desiredTotal.Y) <= 2.0)
            {
                return;
            }

            var requestedIncremental = desiredTotal - _dragGroupAppliedDelta;
            var incremental = ClampGroupMoveDelta(_dragGroup.Id, requestedIncremental);
            if (Math.Abs(incremental.X) < 0.001 && Math.Abs(incremental.Y) < 0.001)
            {
                return;
            }

            try
            {
                GroupEditingLogic.Move(
                    _project,
                    _sheet.Id,
                    _dragGroup.Id,
                    incremental.X,
                    incremental.Y);
                _dragGroupAppliedDelta += incremental;
                _dragGroupMoved = true;
                ApplyCurrentSheetExtentToCanvas();
                var movedNodeIds = UpdateMovedGroupVisuals(_dragGroup.Id);
                UpdateIncidentWires(movedNodeIds);
            }
            catch (Exception exception)
            {
                CancelGroupDragForOnlineOperation();
                ReportUnexpectedEditFailure("Перемещение области", exception);
            }

            e.Handled = true;
        }

        private void Group_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragGroup != null)
            {
                EndGroupDrag();
                e.Handled = true;
            }
        }

        private void Group_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_dragGroup != null && !_onlineBusy)
            {
                EndGroupDrag();
            }
        }

        private void EndGroupDrag()
        {
            if (_dragGroup == null)
            {
                return;
            }

            var title = _dragGroup.Title;
            var moved = _dragGroupMoved;
            var before = _dragGroupBeforeProject;
            var element = _dragGroupElement;
            ClearGroupDragState();
            if (element != null && element.IsMouseCaptured)
            {
                element.ReleaseMouseCapture();
            }

            if (moved && before != null)
            {
                _editHistory.Record("Перемещение области " + title, before, _project);
                UpdateHistoryCommandState();
                RenderDiagram();
                RefreshCompilation(false);
                SetStatus("Область " + title + " перемещена вместе с содержимым");
            }
        }

        private void CancelGroupDragForOnlineOperation()
        {
            if (_dragGroup == null && _resizeGroup == null)
            {
                return;
            }

            var element = _resizeCaptureElement ?? _dragGroupElement;
            var beforeProject = _resizeBeforeProject ?? _dragGroupBeforeProject;
            try
            {
                if (_sheet != null && beforeProject != null)
                {
                    var beforeSheet = beforeProject.Sheets.SingleOrDefault(item =>
                        item != null && item.Id == _sheet.Id);
                    if (beforeSheet == null)
                    {
                        return;
                    }

                    _sheet.Width = beforeSheet.Width;
                    _sheet.Height = beforeSheet.Height;

                    var beforeGroups = beforeSheet.Groups
                        .Where(item => item != null)
                        .GroupBy(item => item.Id)
                        .ToDictionary(items => items.Key, items => items.First());
                    foreach (var group in _sheet.Groups.Where(item => item != null))
                    {
                        DiagramGroup beforeGroup;
                        if (beforeGroups.TryGetValue(group.Id, out beforeGroup))
                        {
                            group.X = beforeGroup.X;
                            group.Y = beforeGroup.Y;
                            group.Width = beforeGroup.Width;
                            group.Height = beforeGroup.Height;
                        }
                    }

                    var beforeNodes = beforeSheet.Nodes
                        .Where(item => item != null)
                        .GroupBy(item => item.Id)
                        .ToDictionary(items => items.Key, items => items.First());
                    foreach (var node in _sheet.Nodes.Where(item => item != null))
                    {
                        CallNode beforeNode;
                        if (beforeNodes.TryGetValue(node.Id, out beforeNode))
                        {
                            node.X = beforeNode.X;
                            node.Y = beforeNode.Y;
                        }
                    }
                }
            }
            finally
            {
                ClearGroupDragState();
                ClearGroupResizeState();
                if (element != null && element.IsMouseCaptured)
                {
                    element.ReleaseMouseCapture();
                }

                ApplyCurrentSheetExtentToCanvas();
                RenderDiagram();
                UpdateViewportCommandState();
            }
        }

        private void ClearGroupDragState()
        {
            _dragGroup = null;
            _dragGroupElement = null;
            _dragGroupBeforeProject = null;
            _dragGroupAppliedDelta = new Vector();
            _dragGroupMoved = false;
        }

        private ISet<Guid> UpdateMovedGroupVisuals(Guid groupId)
        {
            var groupIds = GroupEditingLogic.GetSubtreeGroupIds(_sheet, groupId);
            foreach (var id in groupIds)
            {
                DiagramGroup group;
                FrameworkElement visual;
                if (_groupVisuals.TryGetValue(id, out visual))
                {
                    group = _sheet.Groups.First(item => item != null && item.Id == id);
                    Canvas.SetLeft(visual, group.X);
                    Canvas.SetTop(visual, group.Y);
                }
            }

            var memberIds = new HashSet<Guid>(_sheet.Groups
                .Where(group => group != null && groupIds.Contains(group.Id))
                .SelectMany(group => group.MemberNodeIds ?? new List<Guid>()));
            foreach (var id in memberIds)
            {
                FrameworkElement visual;
                var node = _sheet.Nodes.FirstOrDefault(item => item != null && item.Id == id);
                if (node != null && _nodeVisuals.TryGetValue(id, out visual))
                {
                    Canvas.SetLeft(visual, node.X);
                    Canvas.SetTop(visual, node.Y);
                }
            }

            if (_selectedGroupId.HasValue && groupIds.Contains(_selectedGroupId.Value))
            {
                var selected = _sheet.Groups.FirstOrDefault(item =>
                    item != null && item.Id == _selectedGroupId.Value);
                UpdateGroupResizeAdornerVisual(selected);
            }

            return memberIds;
        }

        private Vector ClampGroupMoveDelta(Guid groupId, Vector desired)
        {
            var groupIds = GroupEditingLogic.GetSubtreeGroupIds(_sheet, groupId);
            var groups = _sheet.Groups.Where(group =>
                group != null && groupIds.Contains(group.Id)).ToList();
            var memberIds = new HashSet<Guid>(groups.SelectMany(group =>
                group.MemberNodeIds ?? new List<Guid>()));
            var nodes = _sheet.Nodes.Where(node =>
                node != null && memberIds.Contains(node.Id)).ToList();

            var left = groups.Min(group => group.X);
            var top = groups.Min(group => group.Y);
            var right = groups.Max(group => group.X + group.Width);
            var bottom = groups.Max(group => group.Y + group.Height);
            if (nodes.Count != 0)
            {
                left = Math.Min(left, nodes.Min(node => node.X));
                top = Math.Min(top, nodes.Min(node => node.Y));
                right = Math.Max(right, nodes.Max(node => node.X + GetNodeWidth(node)));
                bottom = Math.Max(bottom, nodes.Max(node => node.Y + GetNodeHeight(node)));
            }

            var minimumX = -left;
            var maximumX = Math.Max(
                minimumX,
                GroupEditingLogic.MaximumGeometryExtent - right);
            var minimumY = -top;
            var maximumY = Math.Max(
                minimumY,
                GroupEditingLogic.MaximumGeometryExtent - bottom);
            var selectedGroup = _sheet.Groups.FirstOrDefault(group =>
                group != null && group.Id == groupId);
            if (selectedGroup != null && selectedGroup.ParentGroupId != Guid.Empty)
            {
                var parent = _sheet.Groups.FirstOrDefault(group =>
                    group != null && group.Id == selectedGroup.ParentGroupId);
                if (parent != null)
                {
                    minimumX = Math.Max(minimumX, parent.X - left);
                    maximumX = Math.Min(maximumX, parent.X + parent.Width - right);
                    minimumY = Math.Max(minimumY, parent.Y - top);
                    maximumY = Math.Min(maximumY, parent.Y + parent.Height - bottom);
                }
            }

            return new Vector(
                Clamp(desired.X, minimumX, Math.Max(minimumX, maximumX)),
                Clamp(desired.Y, minimumY, Math.Max(minimumY, maximumY)));
        }

        private Rect CalculateGroupBounds(IList<Guid> selectedIds)
        {
            var nodes = _sheet.Nodes.Where(node => selectedIds.Contains(node.Id)).ToList();
            var left = nodes.Min(node => node.X) - GroupPadding;
            var top = nodes.Min(node => node.Y) - GroupPadding - 18.0;
            var right = nodes.Max(node => node.X + GetNodeWidth(node)) + GroupPadding;
            var bottom = nodes.Max(node => node.Y + GetNodeHeight(node)) + GroupPadding;
            return new Rect(
                left,
                top,
                Math.Max(GroupEditingLogic.MinimumWidth, right - left),
                Math.Max(GroupEditingLogic.MinimumHeight, bottom - top));
        }

        private Rect ClampGroupBounds(Rect bounds, IList<Guid> selectedIds)
        {
            var container = new Rect(0.0, 0.0, _sheet.Width, _sheet.Height);
            var parentIds = (selectedIds ?? new List<Guid>())
                .Select(id => _sheet.Groups.FirstOrDefault(group =>
                    group != null &&
                    group.MemberNodeIds != null &&
                    group.MemberNodeIds.Contains(id)))
                .Select(group => group == null ? Guid.Empty : group.Id)
                .Distinct()
                .ToList();
            if (parentIds.Count == 1 && parentIds[0] != Guid.Empty)
            {
                var parent = _sheet.Groups.FirstOrDefault(group =>
                    group != null && group.Id == parentIds[0]);
                if (parent != null)
                {
                    container = new Rect(parent.X, parent.Y, parent.Width, parent.Height);
                }
            }

            var width = Math.Min(
                Math.Max(bounds.Width, GroupEditingLogic.MinimumWidth),
                container.Width);
            var height = Math.Min(
                Math.Max(bounds.Height, GroupEditingLogic.MinimumHeight),
                container.Height);
            return new Rect(
                Clamp(
                    bounds.X,
                    container.X,
                    Math.Max(container.X, container.Right - width)),
                Clamp(
                    bounds.Y,
                    container.Y,
                    Math.Max(container.Y, container.Bottom - height)),
                width,
                height);
        }

        private Point GetViewportCenterInDiagram()
        {
            var zoom = NormalizeZoom(_sheet.Zoom);
            var width = DiagramScrollViewer.ViewportWidth > 0.0
                ? DiagramScrollViewer.ViewportWidth
                : 900.0;
            var height = DiagramScrollViewer.ViewportHeight > 0.0
                ? DiagramScrollViewer.ViewportHeight
                : 560.0;
            return new Point(
                (DiagramScrollViewer.HorizontalOffset + width / 2.0) / zoom,
                (DiagramScrollViewer.VerticalOffset + height / 2.0) / zoom);
        }

        private string CreateUniqueGroupTitle()
        {
            var used = new HashSet<string>(
                _sheet.Groups
                    .Where(group => group != null)
                    .Select(group => (group.Title ?? string.Empty).Trim()),
                StringComparer.OrdinalIgnoreCase);
            for (var number = 1; ; number++)
            {
                var candidate = "Область " + number;
                if (!used.Contains(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
