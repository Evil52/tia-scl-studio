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

        private readonly Dictionary<Guid, FrameworkElement> _groupVisuals =
            new Dictionary<Guid, FrameworkElement>();

        private Guid? _selectedGroupId;
        private DiagramGroup _dragGroup;
        private FrameworkElement _dragGroupElement;
        private Point _dragGroupStartMouse;
        private Vector _dragGroupAppliedDelta;
        private DiagramProject _dragGroupBeforeProject;
        private bool _dragGroupMoved;

        private bool IsGroupDragActive()
        {
            return _dragGroup != null;
        }

        private void ResetGroupEditingForLoadedProject()
        {
            _selectedGroupId = null;
            ClearGroupDragState();
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

            bounds = ClampGroupBounds(bounds);
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

            var editor = new GroupEditorWindow(group.Title, group.Comment) { Owner = this };
            if (editor.ShowDialog() != true)
            {
                return;
            }

            if (!TryCommitSemanticEdit(
                "Изменение области " + group.Title,
                () => GroupEditingLogic.Edit(
                    _project,
                    _sheet.Id,
                    group.Id,
                    editor.ApprovedTitle,
                    editor.ApprovedComment)))
            {
                return;
            }

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
            if (_dragGroup == null)
            {
                return;
            }

            var element = _dragGroupElement;
            try
            {
                if (_sheet != null && _dragGroupBeforeProject != null)
                {
                    var beforeSheet = _dragGroupBeforeProject.Sheets.SingleOrDefault(item =>
                        item != null && item.Id == _sheet.Id);
                    if (beforeSheet == null)
                    {
                        return;
                    }

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
                if (element != null && element.IsMouseCaptured)
                {
                    element.ReleaseMouseCapture();
                }

                RenderDiagram();
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

            return new Vector(
                Clamp(desired.X, -left, Math.Max(-left, _sheet.Width - right)),
                Clamp(desired.Y, -top, Math.Max(-top, _sheet.Height - bottom)));
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

        private Rect ClampGroupBounds(Rect bounds)
        {
            var width = Math.Min(Math.Max(bounds.Width, GroupEditingLogic.MinimumWidth), _sheet.Width);
            var height = Math.Min(Math.Max(bounds.Height, GroupEditingLogic.MinimumHeight), _sheet.Height);
            return new Rect(
                Clamp(bounds.X, 0.0, Math.Max(0.0, _sheet.Width - width)),
                Clamp(bounds.Y, 0.0, Math.Max(0.0, _sheet.Height - height)),
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
