using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    internal static class GroupEditingLogic
    {
        internal const double MinimumWidth = 150.0;
        internal const double MinimumHeight = 96.0;
        internal const double MaximumGeometryExtent = SheetEditingLogic.MaximumSheetExtent;
        internal const int MaximumTitleLength = 80;
        internal const int MaximumCommentLength = 1000;

        internal static DiagramGroup CreateDraft(
            string title,
            string comment,
            double x,
            double y,
            double width,
            double height)
        {
            var group = new DiagramGroup(
                NormalizeTitle(title),
                x,
                y,
                width,
                height)
            {
                Comment = NormalizeComment(comment)
            };
            ValidateText(group.Title, group.Comment);
            ValidateBounds(group);
            return group;
        }

        internal static void Add(
            DiagramProject project,
            Guid sheetId,
            DiagramGroup draft,
            IEnumerable<Guid> selectedNodeIds)
        {
            var sheet = FindSheet(project, sheetId);
            if (draft == null)
            {
                throw new ArgumentNullException("draft");
            }

            ValidateText(draft.Title, draft.Comment);
            ValidateBounds(draft, sheet);
            if (draft.Id == Guid.Empty)
            {
                throw new InvalidOperationException("Идентификатор области не должен быть пустым.");
            }

            if (EnumerateProjectIds(project).Contains(draft.Id))
            {
                throw new InvalidOperationException("Идентификатор новой области уже используется в проекте.");
            }

            var members = (selectedNodeIds ?? Enumerable.Empty<Guid>()).Distinct().ToList();
            var selectedNodes = new List<CallNode>();
            foreach (var memberId in members)
            {
                var matches = (sheet.Nodes ?? new List<CallNode>())
                    .Where(node => node != null && node.Id == memberId)
                    .ToList();
                if (memberId == Guid.Empty || matches.Count != 1)
                {
                    throw new InvalidOperationException(
                        "Выделение содержит отсутствующий узел или дублирующийся ID узла.");
                }

                selectedNodes.Add(matches[0]);
            }

            var directParents = members
                .Select(id => FindDirectOwnerId(sheet, id))
                .Distinct()
                .ToList();
            if (directParents.Count > 1)
            {
                throw new InvalidOperationException(
                    "Для вложенной области выберите узлы с одним непосредственным владельцем.");
            }

            var parentId = directParents.Count == 0 ? Guid.Empty : directParents[0];
            DiagramGroup parent = null;
            if (parentId != Guid.Empty)
            {
                var parentMatches = sheet.Groups
                    .Where(item => item != null && item.Id == parentId)
                    .ToList();
                if (parentMatches.Count != 1)
                {
                    throw new InvalidOperationException(
                        "Родительская область отсутствует или имеет дублирующийся ID.");
                }

                parent = parentMatches[0];
            }

            EnsureNewGroupFitsParentAndMembers(draft, parent, selectedNodes);

            var group = new DiagramGroup
            {
                Id = draft.Id,
                ParentGroupId = parentId,
                Title = NormalizeTitle(draft.Title),
                Comment = NormalizeComment(draft.Comment),
                X = draft.X,
                Y = draft.Y,
                Width = draft.Width,
                Height = draft.Height,
                MemberNodeIds = new List<Guid>(members)
            };

            if (parent != null)
            {
                parent.MemberNodeIds.RemoveAll(id => members.Contains(id));
            }

            sheet.Groups.Add(group);
        }

        internal static void Edit(
            DiagramProject project,
            Guid sheetId,
            Guid groupId,
            string title,
            string comment)
        {
            var sheet = FindSheet(project, sheetId);
            var group = FindRequiredGroup(sheet, groupId);
            var normalizedTitle = NormalizeTitle(title);
            var normalizedComment = NormalizeComment(comment);
            ValidateText(normalizedTitle, normalizedComment);
            group.Title = normalizedTitle;
            group.Comment = normalizedComment;
        }

        /// <summary>
        /// Applies absolute geometry values as a property edit. Changing X or Y moves the
        /// complete group subtree and all of its member nodes. Width and height affect only
        /// the selected group's frame.
        /// </summary>
        internal static void EditGeometry(
            DiagramProject project,
            Guid sheetId,
            Guid groupId,
            double x,
            double y,
            double width,
            double height)
        {
            ApplyGeometry(
                project,
                sheetId,
                groupId,
                x,
                y,
                width,
                height,
                true);
        }

        /// <summary>
        /// Changes only the selected group's frame. This is the geometry operation used by
        /// resize handles, including handles that change the left or top edge.
        /// </summary>
        internal static void ResizeBounds(
            DiagramProject project,
            Guid sheetId,
            Guid groupId,
            double x,
            double y,
            double width,
            double height)
        {
            ApplyGeometry(
                project,
                sheetId,
                groupId,
                x,
                y,
                width,
                height,
                false);
        }

        internal static void Ungroup(
            DiagramProject project,
            Guid sheetId,
            Guid groupId)
        {
            var sheet = FindSheet(project, sheetId);
            var group = FindRequiredGroup(sheet, groupId);
            var parent = group.ParentGroupId == Guid.Empty
                ? null
                : FindGroup(sheet, group.ParentGroupId);
            if (group.ParentGroupId != Guid.Empty && parent == null)
            {
                throw new InvalidOperationException("Родительская область больше не существует.");
            }

            if (parent != null)
            {
                foreach (var memberId in group.MemberNodeIds ?? new List<Guid>())
                {
                    if (!parent.MemberNodeIds.Contains(memberId))
                    {
                        parent.MemberNodeIds.Add(memberId);
                    }
                }
            }

            foreach (var child in sheet.Groups.Where(item =>
                item != null && item.ParentGroupId == group.Id))
            {
                child.ParentGroupId = group.ParentGroupId;
            }

            sheet.Groups.Remove(group);
        }

        internal static void RemoveNodesFromGroups(CallSheet sheet, ISet<Guid> removedNodeIds)
        {
            if (sheet == null || removedNodeIds == null || removedNodeIds.Count == 0)
            {
                return;
            }

            foreach (var group in sheet.Groups ?? new List<DiagramGroup>())
            {
                if (group != null && group.MemberNodeIds != null)
                {
                    group.MemberNodeIds.RemoveAll(removedNodeIds.Contains);
                }
            }
        }

        internal static void Move(
            DiagramProject project,
            Guid sheetId,
            Guid groupId,
            double deltaX,
            double deltaY)
        {
            if (!IsFinite(deltaX) || !IsFinite(deltaY))
            {
                throw new ArgumentOutOfRangeException("deltaX", "Group movement must be finite.");
            }

            var sheet = FindSheet(project, sheetId);
            var group = FindRequiredGroup(sheet, groupId);
            ApplyGeometry(
                project,
                sheetId,
                groupId,
                group.X + deltaX,
                group.Y + deltaY,
                group.Width,
                group.Height,
                true);
        }

        internal static ISet<Guid> GetSubtreeGroupIds(CallSheet sheet, Guid rootGroupId)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException("sheet");
            }

            var result = new HashSet<Guid>();
            var queue = new Queue<Guid>();
            queue.Enqueue(rootGroupId);
            while (queue.Count != 0)
            {
                var current = queue.Dequeue();
                if (!result.Add(current))
                {
                    continue;
                }

                foreach (var child in (sheet.Groups ?? new List<DiagramGroup>())
                    .Where(group => group != null && group.ParentGroupId == current))
                {
                    queue.Enqueue(child.Id);
                }
            }

            return result;
        }

        private static void ApplyGeometry(
            DiagramProject project,
            Guid sheetId,
            Guid groupId,
            double x,
            double y,
            double width,
            double height,
            bool moveContents)
        {
            ValidateGeometryValues(x, y, width, height);

            var sheet = FindSheet(project, sheetId);
            var selectedGroup = FindRequiredGroup(sheet, groupId);
            ValidateExistingSheetExtent(sheet);
            ValidateBounds(selectedGroup);

            var deltaX = moveContents ? x - selectedGroup.X : 0.0;
            var deltaY = moveContents ? y - selectedGroup.Y : 0.0;

            var subtreeIds = GetSubtreeGroupIds(sheet, groupId);
            var movedGroups = sheet.Groups
                .Where(item => item != null && subtreeIds.Contains(item.Id))
                .ToList();
            if (movedGroups.Count != subtreeIds.Count)
            {
                throw new InvalidOperationException(
                    "The group subtree contains a missing or duplicate group identifier.");
            }

            var futureGroups = new Dictionary<Guid, GeometryBounds>();
            foreach (var group in movedGroups)
            {
                var future = new GeometryBounds(
                    group.Id == groupId ? x : group.X + deltaX,
                    group.Id == groupId ? y : group.Y + deltaY,
                    group.Id == groupId ? width : group.Width,
                    group.Id == groupId ? height : group.Height);
                ValidateSafeBounds(future, "group");
                futureGroups.Add(group.Id, future);
            }

            EnsureSelectedGroupFitsParent(
                sheet,
                selectedGroup,
                futureGroups);

            var memberIds = new HashSet<Guid>(movedGroups.SelectMany(group =>
                group.MemberNodeIds ?? new List<Guid>()));
            var movedNodes = new Dictionary<Guid, CallNode>();
            var futureNodes = new Dictionary<Guid, GeometryBounds>();
            foreach (var memberId in memberIds)
            {
                var matches = (sheet.Nodes ?? new List<CallNode>())
                    .Where(candidate => candidate != null && candidate.Id == memberId)
                    .ToList();
                if (memberId == Guid.Empty || matches.Count != 1)
                {
                    throw new InvalidOperationException(
                        "A group member node is missing or has a duplicate identifier.");
                }

                var node = matches[0];
                var future = new GeometryBounds(
                    node.X + deltaX,
                    node.Y + deltaY,
                    SheetEditingLogic.GetNodeWidth(node),
                    SheetEditingLogic.GetNodeHeight(node));
                ValidateSafeBounds(future, "node");
                movedNodes.Add(node.Id, node);
                futureNodes.Add(node.Id, future);
            }

            // S1244 asks for a tolerance. A resize by any amount at all is a
            // resize: this only distinguishes a pure move from a move that also
            // changes the frame, and a tolerance would classify a small genuine
            // resize as a move and skip the containment check below.
#pragma warning disable S1244
            var frameIsBeingResized = !moveContents ||
                width != selectedGroup.Width ||
                height != selectedGroup.Height;
#pragma warning restore S1244
            if (frameIsBeingResized)
            {
                EnsureSelectedGroupContainsItsContent(
                    selectedGroup,
                    subtreeIds,
                    futureGroups,
                    futureNodes);
            }

            var requiredRight = futureGroups.Values.Max(bounds => bounds.Right);
            var requiredBottom = futureGroups.Values.Max(bounds => bounds.Bottom);
            if (futureNodes.Count != 0)
            {
                requiredRight = Math.Max(
                    requiredRight,
                    futureNodes.Values.Max(bounds => bounds.Right));
                requiredBottom = Math.Max(
                    requiredBottom,
                    futureNodes.Values.Max(bounds => bounds.Bottom));
            }

            var grownWidth = SheetEditingLogic.GrowExtentToFit(
                sheet.Width,
                requiredRight);
            var grownHeight = SheetEditingLogic.GrowExtentToFit(
                sheet.Height,
                requiredBottom);

            // Commit only after the complete candidate has been validated. Wires and all
            // non-geometry state remain untouched, and the caller can record one history item.
            foreach (var group in movedGroups)
            {
                var future = futureGroups[group.Id];
                group.X = future.X;
                group.Y = future.Y;
                if (group.Id == groupId)
                {
                    group.Width = width;
                    group.Height = height;
                }
            }

            foreach (var pair in movedNodes)
            {
                var future = futureNodes[pair.Key];
                pair.Value.X = future.X;
                pair.Value.Y = future.Y;
            }

            sheet.Width = grownWidth;
            sheet.Height = grownHeight;
        }

        private static void EnsureSelectedGroupFitsParent(
            CallSheet sheet,
            DiagramGroup selectedGroup,
            IDictionary<Guid, GeometryBounds> futureGroups)
        {
            if (selectedGroup.ParentGroupId == Guid.Empty)
            {
                return;
            }

            var parentMatches = sheet.Groups
                .Where(group =>
                    group != null && group.Id == selectedGroup.ParentGroupId)
                .ToList();
            if (parentMatches.Count != 1)
            {
                throw new InvalidOperationException(
                    "The parent group is missing or has a duplicate identifier.");
            }

            GeometryBounds parentBounds;
            if (!futureGroups.TryGetValue(selectedGroup.ParentGroupId, out parentBounds))
            {
                var parent = parentMatches[0];
                parentBounds = new GeometryBounds(
                    parent.X,
                    parent.Y,
                    parent.Width,
                    parent.Height);
            }

            ValidateSafeBounds(parentBounds, "parent group");
            GeometryBounds selectedBounds;
            if (!futureGroups.TryGetValue(selectedGroup.Id, out selectedBounds) ||
                !Contains(parentBounds, selectedBounds))
            {
                throw new InvalidOperationException(
                    "The requested group bounds must remain inside the parent group.");
            }
        }

        private static void EnsureNewGroupFitsParentAndMembers(
            DiagramGroup draft,
            DiagramGroup parent,
            IEnumerable<CallNode> selectedNodes)
        {
            var draftBounds = new GeometryBounds(
                draft.X,
                draft.Y,
                draft.Width,
                draft.Height);
            ValidateSafeBounds(draftBounds, "group");

            if (parent != null)
            {
                var parentBounds = new GeometryBounds(
                    parent.X,
                    parent.Y,
                    parent.Width,
                    parent.Height);
                ValidateSafeBounds(parentBounds, "parent group");
                if (!Contains(parentBounds, draftBounds))
                {
                    throw new InvalidOperationException(
                        "The new group must remain inside its parent group.");
                }
            }

            foreach (var node in selectedNodes ?? Enumerable.Empty<CallNode>())
            {
                var nodeBounds = new GeometryBounds(
                    node.X,
                    node.Y,
                    SheetEditingLogic.GetNodeWidth(node),
                    SheetEditingLogic.GetNodeHeight(node));
                ValidateSafeBounds(nodeBounds, "node");
                if (!Contains(draftBounds, nodeBounds))
                {
                    throw new InvalidOperationException(
                        "The new group must contain every selected member node.");
                }
            }
        }

        private static void EnsureSelectedGroupContainsItsContent(
            DiagramGroup selectedGroup,
            ISet<Guid> subtreeIds,
            IDictionary<Guid, GeometryBounds> futureGroups,
            IDictionary<Guid, GeometryBounds> futureNodes)
        {
            var selectedBounds = futureGroups[selectedGroup.Id];
            foreach (var memberId in (selectedGroup.MemberNodeIds ?? new List<Guid>()).Distinct())
            {
                GeometryBounds memberBounds;
                if (!futureNodes.TryGetValue(memberId, out memberBounds) ||
                    !Contains(selectedBounds, memberBounds))
                {
                    throw new InvalidOperationException(
                        "The requested group bounds would clip a direct member node.");
                }
            }

            foreach (var descendantId in subtreeIds.Where(id => id != selectedGroup.Id))
            {
                GeometryBounds descendantBounds;
                if (!futureGroups.TryGetValue(descendantId, out descendantBounds) ||
                    !Contains(selectedBounds, descendantBounds))
                {
                    throw new InvalidOperationException(
                        "The requested group bounds would clip a child group.");
                }
            }
        }

        private static bool Contains(GeometryBounds outer, GeometryBounds inner)
        {
            return inner.X >= outer.X &&
                inner.Y >= outer.Y &&
                inner.Right <= outer.Right &&
                inner.Bottom <= outer.Bottom;
        }

        private static void ValidateGeometryValues(
            double x,
            double y,
            double width,
            double height)
        {
            var bounds = new GeometryBounds(x, y, width, height);
            if (!IsFinite(x) ||
                !IsFinite(y) ||
                !IsFinite(width) ||
                !IsFinite(height) ||
                x < 0.0 ||
                y < 0.0 ||
                width < MinimumWidth ||
                height < MinimumHeight ||
                width > MaximumGeometryExtent ||
                height > MaximumGeometryExtent ||
                x > MaximumGeometryExtent - width ||
                y > MaximumGeometryExtent - height)
            {
                throw new ArgumentOutOfRangeException(
                    "width",
                    "Group geometry must be finite, non-negative, at least " +
                    MinimumWidth + "x" + MinimumHeight +
                    ", and inside the " + MaximumGeometryExtent +
                    "x" + MaximumGeometryExtent + " safe extent.");
            }

            ValidateSafeBounds(bounds, "group");
        }

        private static void ValidateSafeBounds(GeometryBounds bounds, string itemKind)
        {
            if (!IsFinite(bounds.X) ||
                !IsFinite(bounds.Y) ||
                !IsFinite(bounds.Width) ||
                !IsFinite(bounds.Height) ||
                bounds.X < 0.0 ||
                bounds.Y < 0.0 ||
                bounds.Width < 0.0 ||
                bounds.Height < 0.0 ||
                bounds.Width > MaximumGeometryExtent ||
                bounds.Height > MaximumGeometryExtent ||
                bounds.X > MaximumGeometryExtent - bounds.Width ||
                bounds.Y > MaximumGeometryExtent - bounds.Height)
            {
                throw new InvalidOperationException(
                    "The " + itemKind +
                    " geometry exceeds the supported safe diagram extent.");
            }
        }

        private static void ValidateExistingSheetExtent(CallSheet sheet)
        {
            if (!IsFinite(sheet.Width) ||
                !IsFinite(sheet.Height) ||
                sheet.Width <= 0.0 ||
                sheet.Height <= 0.0 ||
                sheet.Width > MaximumGeometryExtent ||
                sheet.Height > MaximumGeometryExtent)
            {
                throw new InvalidOperationException(
                    "The existing sheet extent is invalid or exceeds the supported safe limit.");
            }
        }

        private struct GeometryBounds
        {
            internal GeometryBounds(double x, double y, double width, double height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }

            internal double X { get; private set; }

            internal double Y { get; private set; }

            internal double Width { get; private set; }

            internal double Height { get; private set; }

            internal double Right { get { return X + Width; } }

            internal double Bottom { get { return Y + Height; } }
        }

        private static CallSheet FindSheet(DiagramProject project, Guid sheetId)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            var matches = (project.Sheets ?? new List<CallSheet>())
                .Where(sheet => sheet != null && sheet.Id == sheetId)
                .ToList();
            if (sheetId == Guid.Empty || matches.Count != 1)
            {
                throw new InvalidOperationException("Целевой лист отсутствует или имеет неуникальный ID.");
            }

            if (matches[0].Groups == null)
            {
                throw new InvalidOperationException("Коллекция областей листа отсутствует.");
            }

            return matches[0];
        }

        private static DiagramGroup FindRequiredGroup(CallSheet sheet, Guid groupId)
        {
            var matches = sheet.Groups.Where(group =>
                group != null && group.Id == groupId).ToList();
            if (groupId == Guid.Empty || matches.Count != 1)
            {
                throw new InvalidOperationException("Область отсутствует или имеет неуникальный ID.");
            }

            return matches[0];
        }

        private static DiagramGroup FindGroup(CallSheet sheet, Guid groupId)
        {
            return sheet.Groups.FirstOrDefault(group =>
                group != null && group.Id == groupId);
        }

        private static Guid FindDirectOwnerId(CallSheet sheet, Guid nodeId)
        {
            var owners = sheet.Groups
                .Where(group => group != null &&
                    (group.MemberNodeIds ?? new List<Guid>()).Contains(nodeId))
                .Select(group => group.Id)
                .Distinct()
                .ToList();
            if (owners.Count > 1)
            {
                throw new InvalidOperationException(
                    "Узел уже является прямым участником нескольких областей.");
            }

            return owners.Count == 0 ? Guid.Empty : owners[0];
        }

        private static void ValidateText(string title, string comment)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new InvalidOperationException("Название области не должно быть пустым.");
            }

            if (title.Length > MaximumTitleLength)
            {
                throw new InvalidOperationException(
                    "Название области не должно превышать " + MaximumTitleLength + " символов.");
            }

            if (comment.Length > MaximumCommentLength)
            {
                throw new InvalidOperationException(
                    "Комментарий области не должен превышать " + MaximumCommentLength + " символов.");
            }

            if (!IsValidXmlText(title) || !IsValidXmlText(comment))
            {
                throw new InvalidOperationException("Текст области содержит недопустимые XML-символы.");
            }
        }

        private static void ValidateBounds(DiagramGroup group)
        {
            if (!IsFinite(group.X) || !IsFinite(group.Y) ||
                !IsFinite(group.Width) || !IsFinite(group.Height) ||
                group.X < 0.0 || group.Y < 0.0 ||
                group.Width < MinimumWidth || group.Height < MinimumHeight ||
                group.Width > MaximumGeometryExtent ||
                group.Height > MaximumGeometryExtent ||
                group.X > MaximumGeometryExtent - group.Width ||
                group.Y > MaximumGeometryExtent - group.Height)
            {
                throw new InvalidOperationException(
                    "Область должна иметь конечные координаты и размер не меньше " +
                    MinimumWidth + "×" + MinimumHeight +
                    ", полностью находясь внутри безопасного поля " +
                    MaximumGeometryExtent + "×" + MaximumGeometryExtent + ".");
            }
        }

        private static void ValidateBounds(DiagramGroup group, CallSheet sheet)
        {
            ValidateBounds(group);
            if (sheet == null ||
                !IsFinite(sheet.Width) ||
                !IsFinite(sheet.Height) ||
                sheet.Width <= 0.0 ||
                sheet.Height <= 0.0 ||
                group.X < 0.0 ||
                group.Y < 0.0 ||
                group.X > sheet.Width ||
                group.Y > sheet.Height ||
                group.Width > sheet.Width - group.X ||
                group.Height > sheet.Height - group.Y)
            {
                throw new InvalidOperationException("Область должна полностью находиться внутри листа.");
            }
        }

        private static string NormalizeTitle(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static string NormalizeComment(string value)
        {
            return value ?? string.Empty;
        }

        private static bool IsValidXmlText(string value)
        {
            try
            {
                XmlConvert.VerifyXmlChars(value ?? string.Empty);
                return true;
            }
            catch (XmlException)
            {
                return false;
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static ISet<Guid> EnumerateProjectIds(DiagramProject project)
        {
            var ids = new HashSet<Guid>();
            if (project.Plant != null)
            {
                ids.Add(project.Plant.Id);
                foreach (var block in project.Plant.Blocks ?? new List<TiaSclStudio.Core.Model.BlockDefinition>())
                {
                    if (block == null) continue;
                    ids.Add(block.Id);
                    foreach (var member in block.Interface ?? new List<TiaSclStudio.Core.Model.InterfaceMember>())
                    {
                        if (member != null) ids.Add(member.Id);
                    }
                }

                foreach (var dataType in project.Plant.DataTypes ?? new List<TiaSclStudio.Core.Model.UdtDefinition>())
                {
                    if (dataType == null) continue;
                    ids.Add(dataType.Id);
                    foreach (var member in dataType.Members ?? new List<TiaSclStudio.Core.Model.UdtMember>())
                    {
                        if (member != null) ids.Add(member.Id);
                    }
                }

                foreach (var tag in project.Plant.Tags ?? new List<TiaSclStudio.Core.Model.TagDefinition>())
                {
                    if (tag != null) ids.Add(tag.Id);
                }

                foreach (var callBlock in project.Plant.CallBlocks ?? new List<TiaSclStudio.Core.Model.CallBlockDefinition>())
                {
                    if (callBlock == null) continue;
                    ids.Add(callBlock.Id);
                    foreach (var member in callBlock.Interface ?? new List<TiaSclStudio.Core.Model.InterfaceMember>())
                    {
                        if (member != null) ids.Add(member.Id);
                    }

                    foreach (var unit in callBlock.Units ?? new List<TiaSclStudio.Core.Model.UnitDefinition>())
                    {
                        if (unit == null) continue;
                        ids.Add(unit.Id);
                        foreach (var binding in unit.Bindings ?? new List<TiaSclStudio.Core.Model.ParameterBinding>())
                        {
                            if (binding != null) ids.Add(binding.Id);
                        }
                    }
                }
            }

            foreach (var sheet in project.Sheets ?? new List<CallSheet>())
            {
                if (sheet == null) continue;
                ids.Add(sheet.Id);
                foreach (var node in sheet.Nodes ?? new List<CallNode>())
                {
                    if (node == null) continue;
                    ids.Add(node.Id);
                    foreach (var pin in node.Pins ?? new List<Pin>())
                    {
                        if (pin != null) ids.Add(pin.Id);
                    }
                }

                foreach (var wire in sheet.Wires ?? new List<Wire>())
                {
                    if (wire != null) ids.Add(wire.Id);
                }

                foreach (var group in sheet.Groups ?? new List<DiagramGroup>())
                {
                    if (group != null) ids.Add(group.Id);
                }
            }

            ids.Remove(Guid.Empty);
            return ids;
        }
    }
}
