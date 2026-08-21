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
            if (members.Any(id => id == Guid.Empty) ||
                members.Any(id => !sheet.Nodes.Any(node => node != null && node.Id == id)))
            {
                throw new InvalidOperationException("Выделение содержит отсутствующий узел.");
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
            var parent = parentId == Guid.Empty
                ? null
                : FindGroup(sheet, parentId);
            if (parentId != Guid.Empty && parent == null)
            {
                throw new InvalidOperationException("Родительская область больше не существует.");
            }

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
            FindRequiredGroup(sheet, groupId);
            var groupIds = GetSubtreeGroupIds(sheet, groupId);
            var movedGroups = sheet.Groups.Where(item =>
                item != null && groupIds.Contains(item.Id)).ToList();
            var memberIds = new HashSet<Guid>(
                movedGroups
                    .SelectMany(group => group.MemberNodeIds ?? new List<Guid>()));
            var movedNodes = sheet.Nodes.Where(item =>
                item != null && memberIds.Contains(item.Id)).ToList();

            foreach (var group in movedGroups)
            {
                var future = new DiagramGroup
                {
                    X = group.X + deltaX,
                    Y = group.Y + deltaY,
                    Width = group.Width,
                    Height = group.Height
                };
                ValidateBounds(future, sheet);
            }

            foreach (var node in movedNodes)
            {
                if (!IsFinite(node.X + deltaX) || !IsFinite(node.Y + deltaY))
                {
                    throw new InvalidOperationException("Координаты узла после перемещения недопустимы.");
                }
            }

            foreach (var group in movedGroups)
            {
                group.X += deltaX;
                group.Y += deltaY;
            }

            foreach (var node in movedNodes)
            {
                node.X += deltaX;
                node.Y += deltaY;
            }
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
                group.Width < MinimumWidth || group.Height < MinimumHeight)
            {
                throw new InvalidOperationException(
                    "Область должна иметь конечные координаты и размер не меньше " +
                    MinimumWidth + "×" + MinimumHeight + ".");
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
