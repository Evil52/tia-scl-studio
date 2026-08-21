using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Validation;

namespace TiaSclStudio.App
{
    internal sealed class SheetAutoLayoutResult
    {
        internal SheetAutoLayoutResult(
            bool changed,
            int nodeCount,
            int cyclicNodeCount,
            int disconnectedNodeCount,
            int groupCount,
            bool sheetExpanded,
            bool executionOrderPreserved,
            double width,
            double height,
            string status)
        {
            Changed = changed;
            NodeCount = nodeCount;
            CyclicNodeCount = cyclicNodeCount;
            DisconnectedNodeCount = disconnectedNodeCount;
            GroupCount = groupCount;
            SheetExpanded = sheetExpanded;
            ExecutionOrderPreserved = executionOrderPreserved;
            Width = width;
            Height = height;
            Status = status ?? string.Empty;
        }

        internal bool Changed { get; private set; }

        internal int NodeCount { get; private set; }

        internal int CyclicNodeCount { get; private set; }

        internal int DisconnectedNodeCount { get; private set; }

        internal int GroupCount { get; private set; }

        internal bool SheetExpanded { get; private set; }

        internal bool ExecutionOrderPreserved { get; private set; }

        internal double Width { get; private set; }

        internal double Height { get; private set; }

        internal string Status { get; private set; }
    }

    /// <summary>
    /// Deterministic, model-only layout for a call sheet. The calculation does
    /// not create or remove nodes, pins, wires, or groups. It commits only after
    /// every proposed coordinate and group rectangle has been calculated.
    /// </summary>
    internal static class SheetAutoLayoutLogic
    {
        private const double OuterMargin = 72.0;
        private const double HorizontalGap = 112.0;
        private const double VerticalGap = 42.0;
        private const double SpecialZoneGap = 144.0;
        private const double SpecialSectionGap = 96.0;
        private const double MinimumSpecialZoneWidth = 1600.0;
        private const double MaximumPackingWidth = 6000.0;
        private const double MinimumMainLaneHeight = 1200.0;
        private const double MaximumMainLaneHeight = 2800.0;
        private const double IntraLayerLaneGap = 36.0;
        private const double PreferredLayoutAspect = 1.6;
        private const double GroupHorizontalPadding = 28.0;
        private const double GroupTopPadding = 48.0;
        private const double GroupBottomPadding = 28.0;
        private const double MinimumGroupWidth = 150.0;
        private const double MinimumGroupHeight = 96.0;
        private const double CanvasQuantum = 100.0;
        private const int CrossingReductionSweeps = 6;

        internal static SheetAutoLayoutResult Apply(
            CallSheet sheet,
            Func<CallNode, double> getWidth,
            Func<CallNode, double> getHeight)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException("sheet");
            }

            if (getWidth == null)
            {
                throw new ArgumentNullException("getWidth");
            }

            if (getHeight == null)
            {
                throw new ArgumentNullException("getHeight");
            }

            ValidateSheetShape(sheet);

            var records = CreateNodeRecords(sheet, getWidth, getHeight);
            var recordsById = records.ToDictionary(record => record.Node.Id);
            var groupRecords = CreateGroupRecords(sheet, recordsById);
            var oldWidth = sheet.Width;
            var oldHeight = sheet.Height;
            var oldExecutionOrder = TopologicalSorter.Sort(sheet).OrderedNodeIds.ToList();
            var plannedManualOrder = CreateExecutionOrderPlan(oldExecutionOrder);

            if (records.Count == 0)
            {
                return new SheetAutoLayoutResult(
                    false,
                    0,
                    0,
                    0,
                    groupRecords.Count,
                    false,
                    true,
                    sheet.Width,
                    sheet.Height,
                    "Лист пуст — размещать нечего.");
            }

            BuildGraph(sheet, recordsById);
            var cyclicNodeIds = FindCyclicNodeIds(records);
            foreach (var record in records)
            {
                record.IsCyclic = cyclicNodeIds.Contains(record.Node.Id);
                record.IsDisconnected = record.Node is NoteNode ||
                    (record.Predecessors.Count == 0 && record.Successors.Count == 0);
            }

            var maximumGroupDepth = groupRecords.Count == 0
                ? -1
                : groupRecords.Max(record => record.Depth);
            var left = OuterMargin +
                Math.Max(0, maximumGroupDepth + 1) * GroupHorizontalPadding;
            var top = OuterMargin +
                Math.Max(0, maximumGroupDepth + 1) * GroupTopPadding;

            var mainRecords = records
                .Where(record => !record.IsCyclic && !record.IsDisconnected)
                .ToList();
            var mainBounds = ArrangeMainGraph(mainRecords, left, top);

            var zoneWidth = CalculatePackingWidth(
                records.Where(record => record.IsCyclic || record.IsDisconnected),
                Math.Max(0.0, mainBounds.Right - left));
            var specialTop = mainRecords.Count == 0
                ? top
                : mainBounds.Bottom + SpecialZoneGap;
            var cyclicRecords = records
                .Where(record => record.IsCyclic)
                .OrderBy(record => record, NodeRecordStableComparer.Instance)
                .ToList();
            var cyclicBounds = ArrangeShelf(
                cyclicRecords,
                left,
                specialTop,
                zoneWidth);

            var disconnectedTop = cyclicRecords.Count == 0
                ? specialTop
                : cyclicBounds.Bottom + SpecialSectionGap;
            var disconnectedRecords = OrderDisconnectedRecords(records
                .Where(record => record.IsDisconnected && !record.IsCyclic));
            var disconnectedBounds = ArrangeShelf(
                disconnectedRecords,
                left,
                disconnectedTop,
                zoneWidth);

            var nodeBounds = CombineBounds(mainBounds, cyclicBounds, disconnectedBounds);
            var emptyGroupTop = nodeBounds.IsEmpty
                ? top
                : nodeBounds.Bottom + SpecialSectionGap;
            var emptyGroupPlans = ArrangeEmptyGroupSubtrees(
                groupRecords,
                left,
                emptyGroupTop,
                zoneWidth);
            var groupPlans = CalculateGroupPlans(
                groupRecords,
                recordsById,
                emptyGroupPlans);
            var groupBounds = GetGroupBounds(groupPlans);
            var allBounds = CombineBounds(nodeBounds, groupBounds);
            var requiredWidth = allBounds.IsEmpty
                ? oldWidth
                : allBounds.Right + OuterMargin;
            var requiredHeight = allBounds.IsEmpty
                ? oldHeight
                : allBounds.Bottom + OuterMargin;
            var newWidth = Math.Max(oldWidth, RoundUp(requiredWidth, CanvasQuantum));
            var newHeight = Math.Max(oldHeight, RoundUp(requiredHeight, CanvasQuantum));
            ValidatePlans(records, groupPlans, newWidth, newHeight);

            var oldState = CaptureOldState(sheet, records, groupRecords);
            var changed = HasChanges(
                sheet,
                records,
                groupPlans,
                plannedManualOrder,
                newWidth,
                newHeight);

            try
            {
                Commit(
                    sheet,
                    records,
                    groupPlans,
                    plannedManualOrder,
                    newWidth,
                    newHeight);

                var newExecutionOrder = TopologicalSorter.Sort(sheet).OrderedNodeIds;
                if (!oldExecutionOrder.SequenceEqual(newExecutionOrder))
                {
                    throw new InvalidOperationException(
                        "Auto-layout changed the executable-node order.");
                }
            }
            catch
            {
                RestoreOldState(sheet, oldState);
                throw;
            }

            var sheetExpanded = newWidth > oldWidth || newHeight > oldHeight;
            return new SheetAutoLayoutResult(
                changed,
                records.Count,
                cyclicRecords.Count,
                disconnectedRecords.Count,
                groupRecords.Count,
                sheetExpanded,
                true,
                newWidth,
                newHeight,
                BuildStatus(records.Count, cyclicRecords.Count, disconnectedRecords.Count));
        }

        private static List<NodeRecord> CreateNodeRecords(
            CallSheet sheet,
            Func<CallNode, double> getWidth,
            Func<CallNode, double> getHeight)
        {
            var records = new List<NodeRecord>(sheet.Nodes.Count);
            var ids = new HashSet<Guid>();
            var noteOrder = sheet.Nodes
                .Select((node, index) => new IndexedNode(node, index))
                .Where(item => item.Node is NoteNode)
                .OrderBy(item => item.Node.Y)
                .ThenBy(item => item.Node.X)
                .ThenBy(item => item.Index)
                .Select((item, order) => new { item.Node.Id, Order = order })
                .ToDictionary(item => item.Id, item => item.Order);

            for (var index = 0; index < sheet.Nodes.Count; index++)
            {
                var node = sheet.Nodes[index];
                if (node == null)
                {
                    throw new InvalidOperationException(
                        "Call-sheet node collection contains a null item.");
                }

                if (node.Id == Guid.Empty || !ids.Add(node.Id))
                {
                    throw new InvalidOperationException(
                        "Auto-layout requires unique, non-empty node identifiers.");
                }

                var width = getWidth(node);
                var height = getHeight(node);
                if (!IsFinite(width) || !IsFinite(height) || width <= 0.0 || height <= 0.0)
                {
                    throw new InvalidOperationException(
                        "Auto-layout received an invalid visual size for node '" +
                        node.Title + "'.");
                }

                int savedNoteOrder;
                records.Add(new NodeRecord(
                    node,
                    width,
                    height,
                    index,
                    noteOrder.TryGetValue(node.Id, out savedNoteOrder)
                        ? savedNoteOrder
                        : -1));
            }

            return records;
        }

        private static void ValidateSheetShape(CallSheet sheet)
        {
            if (sheet.Nodes == null)
            {
                throw new InvalidOperationException("Call-sheet node collection is missing.");
            }

            if (sheet.Wires == null)
            {
                throw new InvalidOperationException("Call-sheet wire collection is missing.");
            }

            if (sheet.Groups == null)
            {
                throw new InvalidOperationException("Call-sheet group collection is missing.");
            }

            if (!IsFinite(sheet.Width) || !IsFinite(sheet.Height) ||
                sheet.Width <= 0.0 || sheet.Height <= 0.0)
            {
                throw new InvalidOperationException(
                    "Call sheet must have finite positive dimensions.");
            }
        }

        private static List<GroupRecord> CreateGroupRecords(
            CallSheet sheet,
            IDictionary<Guid, NodeRecord> nodes)
        {
            var groups = new List<GroupRecord>(sheet.Groups.Count);
            var byId = new Dictionary<Guid, GroupRecord>();
            foreach (var group in sheet.Groups)
            {
                if (group == null)
                {
                    throw new InvalidOperationException(
                        "Call-sheet group collection contains a null item.");
                }

                if (group.Id == Guid.Empty || byId.ContainsKey(group.Id))
                {
                    throw new InvalidOperationException(
                        "Auto-layout requires unique, non-empty group identifiers.");
                }

                if (group.MemberNodeIds == null)
                {
                    throw new InvalidOperationException(
                        "Visual group '" + group.Title + "' has no member collection.");
                }

                if (!IsFinite(group.X) || !IsFinite(group.Y) ||
                    !IsFinite(group.Width) || !IsFinite(group.Height) ||
                    group.X < 0.0 || group.Y < 0.0 ||
                    group.Width <= 0.0 || group.Height <= 0.0)
                {
                    throw new InvalidOperationException(
                        "Visual group '" + group.Title + "' has invalid geometry.");
                }

                var record = new GroupRecord(group);
                groups.Add(record);
                byId.Add(group.Id, record);
            }

            var directOwners = new Dictionary<Guid, Guid>();
            foreach (var record in groups)
            {
                if (record.Group.ParentGroupId != Guid.Empty)
                {
                    GroupRecord parent;
                    if (!byId.TryGetValue(record.Group.ParentGroupId, out parent))
                    {
                        throw new InvalidOperationException(
                            "Visual group '" + record.Group.Title +
                            "' refers to a missing parent group.");
                    }

                    record.Parent = parent;
                    parent.Children.Add(record);
                }

                foreach (var nodeId in record.Group.MemberNodeIds)
                {
                    if (!nodes.ContainsKey(nodeId))
                    {
                        throw new InvalidOperationException(
                            "Visual group '" + record.Group.Title +
                            "' refers to a missing node.");
                    }

                    Guid existingOwner;
                    if (directOwners.TryGetValue(nodeId, out existingOwner) &&
                        existingOwner != record.Group.Id)
                    {
                        throw new InvalidOperationException(
                            "A node cannot be a direct member of multiple visual groups.");
                    }

                    directOwners[nodeId] = record.Group.Id;
                }
            }

            foreach (var record in groups)
            {
                record.Depth = CalculateGroupDepth(record, groups.Count);
            }

            return groups;
        }

        private static int CalculateGroupDepth(GroupRecord record, int maximumSteps)
        {
            var visited = new HashSet<Guid>();
            var depth = 0;
            var current = record;
            while (current.Parent != null)
            {
                if (!visited.Add(current.Group.Id) || depth > maximumSteps)
                {
                    throw new InvalidOperationException(
                        "Visual-group parent hierarchy contains a cycle.");
                }

                depth++;
                current = current.Parent;
            }

            if (!visited.Add(current.Group.Id))
            {
                throw new InvalidOperationException(
                    "Visual-group parent hierarchy contains a cycle.");
            }

            return depth;
        }

        private static void BuildGraph(
            CallSheet sheet,
            IDictionary<Guid, NodeRecord> recordsById)
        {
            foreach (var wire in sheet.Wires)
            {
                if (wire == null)
                {
                    throw new InvalidOperationException(
                        "Call-sheet wire collection contains a null item.");
                }

                NodeRecord source;
                NodeRecord target;
                if (!recordsById.TryGetValue(wire.SourceNodeId, out source) ||
                    !recordsById.TryGetValue(wire.TargetNodeId, out target))
                {
                    continue;
                }

                if (!source.Successors.Contains(target))
                {
                    source.Successors.Add(target);
                    target.Predecessors.Add(source);
                }
            }

            foreach (var record in recordsById.Values)
            {
                record.Successors.Sort(NodeRecordStableComparer.Instance);
                record.Predecessors.Sort(NodeRecordStableComparer.Instance);
            }
        }

        private static ISet<Guid> FindCyclicNodeIds(IList<NodeRecord> records)
        {
            var finishOrder = new List<NodeRecord>(records.Count);
            var visited = new HashSet<Guid>();
            foreach (var root in records.OrderBy(
                record => record,
                NodeRecordStableComparer.Instance))
            {
                if (visited.Contains(root.Node.Id))
                {
                    continue;
                }

                var stack = new Stack<TraversalFrame>();
                visited.Add(root.Node.Id);
                stack.Push(new TraversalFrame(root, root.Successors));
                while (stack.Count != 0)
                {
                    var frame = stack.Peek();
                    if (frame.MoveNext())
                    {
                        var successor = frame.Current;
                        if (visited.Add(successor.Node.Id))
                        {
                            stack.Push(new TraversalFrame(successor, successor.Successors));
                        }

                        continue;
                    }

                    stack.Pop();
                    finishOrder.Add(frame.Node);
                }
            }

            var cyclic = new HashSet<Guid>();
            visited.Clear();
            for (var index = finishOrder.Count - 1; index >= 0; index--)
            {
                var root = finishOrder[index];
                if (!visited.Add(root.Node.Id))
                {
                    continue;
                }

                var component = new List<NodeRecord>();
                var stack = new Stack<NodeRecord>();
                stack.Push(root);
                while (stack.Count != 0)
                {
                    var current = stack.Pop();
                    component.Add(current);
                    for (var predecessorIndex = current.Predecessors.Count - 1;
                        predecessorIndex >= 0;
                        predecessorIndex--)
                    {
                        var predecessor = current.Predecessors[predecessorIndex];
                        if (visited.Add(predecessor.Node.Id))
                        {
                            stack.Push(predecessor);
                        }
                    }
                }

                if (component.Count > 1 || component[0].Successors.Contains(component[0]))
                {
                    foreach (var record in component)
                    {
                        cyclic.Add(record.Node.Id);
                    }
                }
            }

            return cyclic;
        }

        private static LayoutBounds ArrangeMainGraph(
            IList<NodeRecord> records,
            double left,
            double top)
        {
            if (records.Count == 0)
            {
                return LayoutBounds.Empty;
            }

            var recordsById = records.ToDictionary(record => record.Node.Id);
            var incoming = records.ToDictionary(
                record => record.Node.Id,
                record => record.Predecessors.Count(predecessor =>
                    recordsById.ContainsKey(predecessor.Node.Id)));
            var ready = new SortedSet<NodeRecord>(NodeRecordStableComparer.Instance);
            foreach (var record in records.Where(record => incoming[record.Node.Id] == 0))
            {
                ready.Add(record);
            }

            var ordered = new List<NodeRecord>(records.Count);
            var orderedIds = new HashSet<Guid>();
            while (ready.Count != 0)
            {
                var current = ready.Min;
                ready.Remove(current);
                ordered.Add(current);
                orderedIds.Add(current.Node.Id);
                foreach (var successor in current.Successors.Where(successor =>
                    recordsById.ContainsKey(successor.Node.Id)))
                {
                    incoming[successor.Node.Id]--;
                    if (incoming[successor.Node.Id] == 0)
                    {
                        ready.Add(successor);
                    }
                }
            }

            // A structurally unusual edge through a non-executable terminal can
            // still leave a residue after SCC extraction. Treat that residue as
            // its own deterministic tail instead of losing a node from layout.
            foreach (var residue in records
                .Where(record => !orderedIds.Contains(record.Node.Id))
                .OrderBy(record => record, NodeRecordStableComparer.Instance))
            {
                ordered.Add(residue);
            }

            foreach (var record in ordered)
            {
                var rank = GetBaseRank(record.Node);
                foreach (var predecessor in record.Predecessors)
                {
                    NodeRecord mainPredecessor;
                    if (recordsById.TryGetValue(predecessor.Node.Id, out mainPredecessor))
                    {
                        rank = Math.Max(rank, mainPredecessor.Layer + 1);
                    }
                }

                record.Layer = rank;
            }

            var layers = ordered
                .GroupBy(record => record.Layer)
                .OrderBy(group => group.Key)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(
                        record => record,
                        NodeRecordStableComparer.Instance).ToList());
            ReduceCrossings(layers);

            var currentLeft = left;
            var bounds = LayoutBounds.Empty;
            foreach (var pair in layers.OrderBy(pair => pair.Key))
            {
                var layer = pair.Value;
                var targetLaneHeight = CalculateMainLaneHeight(layer);
                var laneLeft = currentLeft;
                var currentTop = top;
                var laneWidth = 0.0;
                var layerRight = currentLeft;
                foreach (var record in layer)
                {
                    if (currentTop > top &&
                        currentTop + record.Height > top + targetLaneHeight)
                    {
                        laneLeft += laneWidth + IntraLayerLaneGap;
                        currentTop = top;
                        laneWidth = 0.0;
                    }

                    record.X = laneLeft;
                    record.Y = currentTop;
                    currentTop += record.Height + VerticalGap;
                    laneWidth = Math.Max(laneWidth, record.Width);
                    layerRight = Math.Max(layerRight, record.X + record.Width);
                    bounds = bounds.Include(record.X, record.Y, record.Width, record.Height);
                }

                currentLeft = layerRight + HorizontalGap;
            }

            return bounds;
        }

        private static void ReduceCrossings(IDictionary<int, List<NodeRecord>> layers)
        {
            if (layers.Count < 2)
            {
                return;
            }

            var layerNumbers = layers.Keys.OrderBy(value => value).ToList();
            for (var sweep = 0; sweep < CrossingReductionSweeps; sweep++)
            {
                var forward = sweep % 2 == 0;
                var traversal = forward
                    ? layerNumbers
                    : layerNumbers.AsEnumerable().Reverse();
                var positions = BuildLayerPositions(layers);
                foreach (var layerNumber in traversal)
                {
                    var current = layers[layerNumber];
                    var scored = current
                        .Select((record, index) => new CrossingScore(
                            record,
                            CalculateBarycenter(
                                forward ? record.Predecessors : record.Successors,
                                positions),
                            index))
                        .ToList();
                    scored.Sort(CrossingScore.Compare);
                    layers[layerNumber] = scored.Select(score => score.Record).ToList();
                    for (var index = 0; index < layers[layerNumber].Count; index++)
                    {
                        positions[layers[layerNumber][index].Node.Id] = index;
                    }
                }
            }
        }

        private static Dictionary<Guid, int> BuildLayerPositions(
            IDictionary<int, List<NodeRecord>> layers)
        {
            var positions = new Dictionary<Guid, int>();
            foreach (var pair in layers)
            {
                for (var index = 0; index < pair.Value.Count; index++)
                {
                    positions[pair.Value[index].Node.Id] = index;
                }
            }

            return positions;
        }

        private static double? CalculateBarycenter(
            IEnumerable<NodeRecord> neighbours,
            IDictionary<Guid, int> positions)
        {
            var values = new List<int>();
            foreach (var neighbour in neighbours)
            {
                int position;
                if (positions.TryGetValue(neighbour.Node.Id, out position))
                {
                    values.Add(position);
                }
            }

            return values.Count == 0 ? (double?)null : values.Average();
        }

        private static List<NodeRecord> OrderDisconnectedRecords(
            IEnumerable<NodeRecord> records)
        {
            return records
                .OrderBy(record => GetBaseRank(record.Node))
                .ThenBy(record => record.Node is NoteNode ? record.NoteOrder : -1)
                .ThenBy(record => record, NodeRecordStableComparer.Instance)
                .ToList();
        }

        private static LayoutBounds ArrangeShelf(
            IList<NodeRecord> records,
            double left,
            double top,
            double availableWidth)
        {
            if (records.Count == 0)
            {
                return LayoutBounds.Empty;
            }

            var rightLimit = left + availableWidth;
            var x = left;
            var y = top;
            var rowHeight = 0.0;
            var bounds = LayoutBounds.Empty;
            foreach (var record in records)
            {
                if (x > left && x + record.Width > rightLimit)
                {
                    x = left;
                    y += rowHeight + VerticalGap;
                    rowHeight = 0.0;
                }

                record.X = x;
                record.Y = y;
                bounds = bounds.Include(record.X, record.Y, record.Width, record.Height);
                rowHeight = Math.Max(rowHeight, record.Height);
                x += record.Width + HorizontalGap;
            }

            return bounds;
        }

        private static double CalculatePackingWidth(
            IEnumerable<NodeRecord> records,
            double mainWidth)
        {
            var area = 0.0;
            foreach (var record in records)
            {
                area += (record.Width + HorizontalGap) *
                    (record.Height + VerticalGap);
            }

            var desired = area <= 0.0
                ? MinimumSpecialZoneWidth
                : Math.Sqrt(area * PreferredLayoutAspect);
            return Math.Min(
                MaximumPackingWidth,
                Math.Max(MinimumSpecialZoneWidth, Math.Max(mainWidth, desired)));
        }

        private static double CalculateMainLaneHeight(IList<NodeRecord> records)
        {
            var area = records.Sum(record =>
                (record.Width + IntraLayerLaneGap) *
                (record.Height + VerticalGap));
            var desired = Math.Sqrt(area / PreferredLayoutAspect);
            return Math.Max(
                MinimumMainLaneHeight,
                Math.Min(MaximumMainLaneHeight, desired));
        }

        private static Dictionary<Guid, GroupPlan> CalculateGroupPlans(
            IList<GroupRecord> groups,
            IDictionary<Guid, NodeRecord> nodes,
            IDictionary<Guid, GroupPlan> emptyGroupPlans)
        {
            var plans = new Dictionary<Guid, GroupPlan>();
            foreach (var record in groups
                .OrderByDescending(item => item.Depth)
                .ThenBy(item => item.Group.Id))
            {
                var contents = LayoutBounds.Empty;
                foreach (var nodeId in record.Group.MemberNodeIds.Distinct())
                {
                    var node = nodes[nodeId];
                    contents = contents.Include(node.X, node.Y, node.Width, node.Height);
                }

                foreach (var child in record.Children.OrderBy(item => item.Group.Id))
                {
                    GroupPlan childPlan;
                    if (plans.TryGetValue(child.Group.Id, out childPlan))
                    {
                        contents = contents.Include(
                            childPlan.X,
                            childPlan.Y,
                            childPlan.Width,
                            childPlan.Height);
                    }
                }

                GroupPlan plan;
                if (!record.HasNodeContent &&
                    emptyGroupPlans.TryGetValue(record.Group.Id, out plan))
                {
                    // Preserve the internal geometry of an empty nested
                    // subtree while moving the subtree as one visual unit.
                }
                else if (contents.IsEmpty)
                {
                    plan = new GroupPlan(
                        record.Group.X,
                        record.Group.Y,
                        record.Group.Width,
                        record.Group.Height);
                }
                else
                {
                    var x = contents.Left - GroupHorizontalPadding;
                    var y = contents.Top - GroupTopPadding;
                    var width = Math.Max(
                        MinimumGroupWidth,
                        contents.Right + GroupHorizontalPadding - x);
                    var height = Math.Max(
                        MinimumGroupHeight,
                        contents.Bottom + GroupBottomPadding - y);
                    plan = new GroupPlan(x, y, width, height);
                }

                plans.Add(record.Group.Id, plan);
            }

            return plans;
        }

        private static Dictionary<Guid, GroupPlan> ArrangeEmptyGroupSubtrees(
            IList<GroupRecord> groups,
            double left,
            double top,
            double availableWidth)
        {
            foreach (var record in groups
                .OrderByDescending(item => item.Depth)
                .ThenBy(item => item.Group.Id))
            {
                record.HasNodeContent = record.Group.MemberNodeIds.Count != 0 ||
                    record.Children.Any(child => child.HasNodeContent);
            }

            var roots = groups
                .Where(record => !record.HasNodeContent &&
                    (record.Parent == null || record.Parent.HasNodeContent))
                .OrderBy(record => record.Group.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.Group.Id)
                .ToList();
            var plans = new Dictionary<Guid, GroupPlan>();
            if (roots.Count == 0)
            {
                return plans;
            }

            var rightLimit = left + availableWidth;
            var x = left;
            var y = top;
            var rowHeight = 0.0;
            foreach (var root in roots)
            {
                var subtree = EnumerateGroupSubtree(root);
                var subtreeBounds = LayoutBounds.Empty;
                foreach (var record in subtree)
                {
                    subtreeBounds = subtreeBounds.Include(
                        record.Group.X,
                        record.Group.Y,
                        record.Group.Width,
                        record.Group.Height);
                }

                var subtreeWidth = subtreeBounds.Right - subtreeBounds.Left;
                var subtreeHeight = subtreeBounds.Bottom - subtreeBounds.Top;
                if (x > left && x + subtreeWidth > rightLimit)
                {
                    x = left;
                    y += rowHeight + VerticalGap;
                    rowHeight = 0.0;
                }

                var deltaX = x - subtreeBounds.Left;
                var deltaY = y - subtreeBounds.Top;
                foreach (var record in subtree)
                {
                    plans[record.Group.Id] = new GroupPlan(
                        record.Group.X + deltaX,
                        record.Group.Y + deltaY,
                        record.Group.Width,
                        record.Group.Height);
                }

                x += subtreeWidth + HorizontalGap;
                rowHeight = Math.Max(rowHeight, subtreeHeight);
            }

            return plans;
        }

        private static List<GroupRecord> EnumerateGroupSubtree(GroupRecord root)
        {
            var result = new List<GroupRecord>();
            var queue = new Queue<GroupRecord>();
            queue.Enqueue(root);
            while (queue.Count != 0)
            {
                var current = queue.Dequeue();
                result.Add(current);
                foreach (var child in current.Children
                    .OrderBy(item => item.Group.Id))
                {
                    queue.Enqueue(child);
                }
            }

            return result;
        }

        private static LayoutBounds GetGroupBounds(
            IDictionary<Guid, GroupPlan> groupPlans)
        {
            var bounds = LayoutBounds.Empty;
            foreach (var plan in groupPlans.Values)
            {
                bounds = bounds.Include(plan.X, plan.Y, plan.Width, plan.Height);
            }

            return bounds;
        }

        private static Dictionary<Guid, int> CreateExecutionOrderPlan(
            IList<Guid> orderedNodeIds)
        {
            var result = new Dictionary<Guid, int>();
            for (var index = 0; index < orderedNodeIds.Count; index++)
            {
                result[orderedNodeIds[index]] = index;
            }

            return result;
        }

        private static OldLayoutState CaptureOldState(
            CallSheet sheet,
            IEnumerable<NodeRecord> records,
            IEnumerable<GroupRecord> groups)
        {
            return new OldLayoutState(
                sheet.Width,
                sheet.Height,
                records.ToDictionary(
                    record => record.Node.Id,
                    record => new OldNodeState(
                        record.Node.X,
                        record.Node.Y,
                        record.Node.ManualOrder)),
                groups.ToDictionary(
                    record => record.Group.Id,
                    record => new GroupPlan(
                        record.Group.X,
                        record.Group.Y,
                        record.Group.Width,
                        record.Group.Height)));
        }

        private static bool HasChanges(
            CallSheet sheet,
            IEnumerable<NodeRecord> records,
            IDictionary<Guid, GroupPlan> groups,
            IDictionary<Guid, int> manualOrder,
            double width,
            double height)
        {
            if (sheet.Width != width || sheet.Height != height)
            {
                return true;
            }

            foreach (var record in records)
            {
                int order;
                var proposedOrder = manualOrder.TryGetValue(record.Node.Id, out order)
                    ? (int?)order
                    : record.Node.ManualOrder;
                if (record.Node.X != record.X ||
                    record.Node.Y != record.Y ||
                    record.Node.ManualOrder != proposedOrder)
                {
                    return true;
                }
            }

            foreach (var pair in groups)
            {
                var group = sheet.Groups.First(item => item.Id == pair.Key);
                var plan = pair.Value;
                if (group.X != plan.X || group.Y != plan.Y ||
                    group.Width != plan.Width || group.Height != plan.Height)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidatePlans(
            IEnumerable<NodeRecord> records,
            IDictionary<Guid, GroupPlan> groups,
            double width,
            double height)
        {
            if (!IsFinite(width) || !IsFinite(height) ||
                width <= 0.0 || height <= 0.0 ||
                width > DenseNodePlacementLogic.MaximumSheetExtent ||
                height > DenseNodePlacementLogic.MaximumSheetExtent)
            {
                throw new InvalidOperationException(
                    "Auto-layout exceeds the maximum call-sheet extent of " +
                    DenseNodePlacementLogic.MaximumSheetExtent + ".");
            }

            foreach (var record in records)
            {
                if (!IsFinite(record.X) || !IsFinite(record.Y) ||
                    !IsFinite(record.Width) || !IsFinite(record.Height) ||
                    record.X < 0.0 || record.Y < 0.0 ||
                    record.X + record.Width > width ||
                    record.Y + record.Height > height)
                {
                    throw new InvalidOperationException(
                        "Auto-layout produced invalid geometry for node '" +
                        record.Node.Title + "'.");
                }
            }

            foreach (var plan in groups.Values)
            {
                if (!IsFinite(plan.X) || !IsFinite(plan.Y) ||
                    !IsFinite(plan.Width) || !IsFinite(plan.Height) ||
                    plan.X < 0.0 || plan.Y < 0.0 ||
                    plan.Width <= 0.0 || plan.Height <= 0.0 ||
                    plan.X + plan.Width > width ||
                    plan.Y + plan.Height > height)
                {
                    throw new InvalidOperationException(
                        "Auto-layout produced invalid visual-group geometry.");
                }
            }
        }

        private static void Commit(
            CallSheet sheet,
            IEnumerable<NodeRecord> records,
            IDictionary<Guid, GroupPlan> groups,
            IDictionary<Guid, int> manualOrder,
            double width,
            double height)
        {
            foreach (var record in records)
            {
                record.Node.X = record.X;
                record.Node.Y = record.Y;
                int order;
                if (manualOrder.TryGetValue(record.Node.Id, out order))
                {
                    record.Node.ManualOrder = order;
                }
            }

            foreach (var group in sheet.Groups)
            {
                var plan = groups[group.Id];
                group.X = plan.X;
                group.Y = plan.Y;
                group.Width = plan.Width;
                group.Height = plan.Height;
            }

            sheet.Width = width;
            sheet.Height = height;
        }

        private static void RestoreOldState(CallSheet sheet, OldLayoutState state)
        {
            foreach (var node in sheet.Nodes)
            {
                OldNodeState oldNode;
                if (state.Nodes.TryGetValue(node.Id, out oldNode))
                {
                    node.X = oldNode.X;
                    node.Y = oldNode.Y;
                    node.ManualOrder = oldNode.ManualOrder;
                }
            }

            foreach (var group in sheet.Groups)
            {
                GroupPlan oldGroup;
                if (state.Groups.TryGetValue(group.Id, out oldGroup))
                {
                    group.X = oldGroup.X;
                    group.Y = oldGroup.Y;
                    group.Width = oldGroup.Width;
                    group.Height = oldGroup.Height;
                }
            }

            sheet.Width = state.Width;
            sheet.Height = state.Height;
        }

        private static int GetBaseRank(CallNode node)
        {
            var tag = node as TagNode;
            if (tag != null)
            {
                return tag.TerminalDirection == TerminalDirection.Sink ? 3 : 0;
            }

            if (node is ConstantNode)
            {
                return 0;
            }

            if (node is LogicNode)
            {
                return 1;
            }

            if (node is BlockCallNode)
            {
                return 2;
            }

            return 4;
        }

        private static LayoutBounds CombineBounds(params LayoutBounds[] candidates)
        {
            var result = LayoutBounds.Empty;
            foreach (var candidate in candidates)
            {
                if (!candidate.IsEmpty)
                {
                    result = result.Include(
                        candidate.Left,
                        candidate.Top,
                        candidate.Right - candidate.Left,
                        candidate.Bottom - candidate.Top);
                }
            }

            return result;
        }

        private static double RoundUp(double value, double quantum)
        {
            return Math.Ceiling(value / quantum) * quantum;
        }

        private static string BuildStatus(
            int nodeCount,
            int cyclicNodeCount,
            int disconnectedNodeCount)
        {
            var status = "Автораскладка завершена: узлов " + nodeCount + ".";
            if (cyclicNodeCount != 0 || disconnectedNodeCount != 0)
            {
                status += " Отдельная зона: циклических " + cyclicNodeCount +
                    ", неподключённых " + disconnectedNodeCount + ".";
            }

            return status;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class IndexedNode
        {
            internal IndexedNode(CallNode node, int index)
            {
                Node = node;
                Index = index;
            }

            internal CallNode Node { get; private set; }

            internal int Index { get; private set; }
        }

        private sealed class NodeRecord
        {
            internal NodeRecord(
                CallNode node,
                double width,
                double height,
                int originalIndex,
                int noteOrder)
            {
                Node = node;
                Width = width;
                Height = height;
                OriginalIndex = originalIndex;
                NoteOrder = noteOrder;
                X = node.X;
                Y = node.Y;
                Predecessors = new List<NodeRecord>();
                Successors = new List<NodeRecord>();
            }

            internal CallNode Node { get; private set; }

            internal double Width { get; private set; }

            internal double Height { get; private set; }

            internal int OriginalIndex { get; private set; }

            internal int NoteOrder { get; private set; }

            internal List<NodeRecord> Predecessors { get; private set; }

            internal List<NodeRecord> Successors { get; private set; }

            internal int Layer { get; set; }

            internal bool IsCyclic { get; set; }

            internal bool IsDisconnected { get; set; }

            internal double X { get; set; }

            internal double Y { get; set; }
        }

        private sealed class NodeRecordStableComparer : IComparer<NodeRecord>
        {
            internal static readonly NodeRecordStableComparer Instance =
                new NodeRecordStableComparer();

            public int Compare(NodeRecord left, NodeRecord right)
            {
                if (ReferenceEquals(left, right))
                {
                    return 0;
                }

                if (left == null)
                {
                    return -1;
                }

                if (right == null)
                {
                    return 1;
                }

                var rankComparison = GetBaseRank(left.Node).CompareTo(GetBaseRank(right.Node));
                if (rankComparison != 0)
                {
                    return rankComparison;
                }

                var titleComparison = string.Compare(
                    left.Node.Title,
                    right.Node.Title,
                    StringComparison.OrdinalIgnoreCase);
                if (titleComparison != 0)
                {
                    return titleComparison;
                }

                var idComparison = left.Node.Id.CompareTo(right.Node.Id);
                return idComparison != 0
                    ? idComparison
                    : left.OriginalIndex.CompareTo(right.OriginalIndex);
            }
        }

        private sealed class TraversalFrame
        {
            private readonly IList<NodeRecord> _successors;
            private int _index = -1;

            internal TraversalFrame(NodeRecord node, IList<NodeRecord> successors)
            {
                Node = node;
                _successors = successors;
            }

            internal NodeRecord Node { get; private set; }

            internal NodeRecord Current { get; private set; }

            internal bool MoveNext()
            {
                _index++;
                if (_index >= _successors.Count)
                {
                    Current = null;
                    return false;
                }

                Current = _successors[_index];
                return true;
            }
        }

        private sealed class CrossingScore
        {
            internal CrossingScore(NodeRecord record, double? value, int previousIndex)
            {
                Record = record;
                Value = value;
                PreviousIndex = previousIndex;
            }

            internal NodeRecord Record { get; private set; }

            internal double? Value { get; private set; }

            internal int PreviousIndex { get; private set; }

            internal static int Compare(CrossingScore left, CrossingScore right)
            {
                if (left.Value.HasValue && right.Value.HasValue)
                {
                    var valueComparison = left.Value.Value.CompareTo(right.Value.Value);
                    if (valueComparison != 0)
                    {
                        return valueComparison;
                    }
                }
                else if (left.Value.HasValue)
                {
                    return -1;
                }
                else if (right.Value.HasValue)
                {
                    return 1;
                }

                var indexComparison = left.PreviousIndex.CompareTo(right.PreviousIndex);
                return indexComparison != 0
                    ? indexComparison
                    : NodeRecordStableComparer.Instance.Compare(left.Record, right.Record);
            }
        }

        private sealed class GroupRecord
        {
            internal GroupRecord(DiagramGroup group)
            {
                Group = group;
                Children = new List<GroupRecord>();
            }

            internal DiagramGroup Group { get; private set; }

            internal GroupRecord Parent { get; set; }

            internal List<GroupRecord> Children { get; private set; }

            internal int Depth { get; set; }

            internal bool HasNodeContent { get; set; }
        }

        private sealed class GroupPlan
        {
            internal GroupPlan(double x, double y, double width, double height)
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
        }

        private sealed class OldNodeState
        {
            internal OldNodeState(double x, double y, int? manualOrder)
            {
                X = x;
                Y = y;
                ManualOrder = manualOrder;
            }

            internal double X { get; private set; }

            internal double Y { get; private set; }

            internal int? ManualOrder { get; private set; }
        }

        private sealed class OldLayoutState
        {
            internal OldLayoutState(
                double width,
                double height,
                IDictionary<Guid, OldNodeState> nodes,
                IDictionary<Guid, GroupPlan> groups)
            {
                Width = width;
                Height = height;
                Nodes = nodes;
                Groups = groups;
            }

            internal double Width { get; private set; }

            internal double Height { get; private set; }

            internal IDictionary<Guid, OldNodeState> Nodes { get; private set; }

            internal IDictionary<Guid, GroupPlan> Groups { get; private set; }
        }

        private struct LayoutBounds
        {
            private LayoutBounds(
                bool isEmpty,
                double left,
                double top,
                double right,
                double bottom)
            {
                IsEmpty = isEmpty;
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }

            internal static LayoutBounds Empty
            {
                get { return new LayoutBounds(true, 0.0, 0.0, 0.0, 0.0); }
            }

            internal bool IsEmpty { get; private set; }

            internal double Left { get; private set; }

            internal double Top { get; private set; }

            internal double Right { get; private set; }

            internal double Bottom { get; private set; }

            internal LayoutBounds Include(double x, double y, double width, double height)
            {
                var right = x + width;
                var bottom = y + height;
                if (IsEmpty)
                {
                    return new LayoutBounds(false, x, y, right, bottom);
                }

                return new LayoutBounds(
                    false,
                    Math.Min(Left, x),
                    Math.Min(Top, y),
                    Math.Max(Right, right),
                    Math.Max(Bottom, bottom));
            }
        }
    }
}
