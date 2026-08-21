using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.Diagram.Validation
{
    public sealed class DiagramValidator
    {
        public DiagramValidationResult Validate(CallSheet sheet)
        {
            var issues = new List<DiagramIssue>();
            if (sheet == null)
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM000",
                    "Call sheet is missing.",
                    null,
                    null));
                return new DiagramValidationResult(issues);
            }

            ValidateSheetIdentityAndName(sheet, issues);

            // A project file written by a crashed session, or hand-edited, can
            // arrive with a missing collection or a hole inside one. Reject the
            // shape first: every check below walks these lists and would
            // otherwise fail with a NullReferenceException that tells the user
            // nothing about which sheet is broken.
            if (!ValidateShape(sheet, issues))
            {
                return new DiagramValidationResult(issues);
            }

            ValidateNodeAndPinIdentity(sheet, issues);
            ValidateGroups(sheet, issues);

            var nodes = sheet.Nodes
                .GroupBy(node => node.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var pins = sheet.Nodes
                .SelectMany(node => node.Pins)
                .GroupBy(pin => pin.Id)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (var node in sheet.Nodes)
            {
                ValidateNode(node, issues);
            }

            var targetUsage = new Dictionary<Guid, Guid>();
            foreach (var wire in sheet.Wires)
            {
                ValidateWire(wire, nodes, pins, targetUsage, issues);
            }

            foreach (var node in sheet.Nodes)
            {
                foreach (var pin in node.Pins.Where(item => item.Direction == PinDirection.Input && item.IsRequired))
                {
                    if (!targetUsage.ContainsKey(pin.Id))
                    {
                        issues.Add(new DiagramIssue(
                            DiagramIssueSeverity.Error,
                            "DGM020",
                            "Required pin '" + pin.Name + "' is not connected.",
                            node.Id,
                            null));
                    }
                }
            }

            if (!issues.Any(issue => IsIdentityIssue(issue.Code)))
            {
                var sortResult = TopologicalSorter.Sort(sheet);
                if (sortResult.HasCycle)
                {
                    foreach (var nodeId in sortResult.CyclicNodeIds)
                    {
                        issues.Add(new DiagramIssue(
                            DiagramIssueSeverity.Error,
                            "DGM030",
                            "The call graph contains a cycle. Break it explicitly with stored state.",
                            nodeId,
                            null));
                    }
                }
            }

            return new DiagramValidationResult(issues);
        }

        /// <summary>
        /// Returns false when the sheet cannot be walked safely. The reported
        /// codes stay in the same DGM04x family the group checks already use
        /// for a structurally missing collection.
        /// </summary>
        private static bool ValidateShape(CallSheet sheet, IList<DiagramIssue> issues)
        {
            var isSafe = true;

            if (sheet.Nodes == null)
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM060",
                    "Call-sheet node collection is missing.",
                    null,
                    null));
                isSafe = false;
            }
            else
            {
                if (sheet.Nodes.Any(node => node == null))
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "DGM061",
                        "Call-sheet node collection contains a null item.",
                        null,
                        null));
                    isSafe = false;
                }

                foreach (var node in sheet.Nodes.Where(node => node != null && node.Pins == null))
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "DGM062",
                        "Node pin collection is missing.",
                        node.Id,
                        null));
                    isSafe = false;
                }

                foreach (var node in sheet.Nodes.Where(node =>
                    node != null && node.Pins != null && node.Pins.Any(pin => pin == null)))
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "DGM063",
                        "Node pin collection contains a null item.",
                        node.Id,
                        null));
                    isSafe = false;
                }
            }

            if (sheet.Wires == null)
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM064",
                    "Call-sheet wire collection is missing.",
                    null,
                    null));
                isSafe = false;
            }
            else if (sheet.Wires.Any(wire => wire == null))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM065",
                    "Call-sheet wire collection contains a null item.",
                    null,
                    null));
                isSafe = false;
            }

            return isSafe;
        }

        private static void ValidateSheetIdentityAndName(CallSheet sheet, IList<DiagramIssue> issues)
        {
            string nameError;
            if (!SclName.TryValidate(sheet.Name, out nameError))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM006",
                    "Invalid call sheet name: " + nameError,
                    null,
                    null));
            }

            if (sheet.Id == Guid.Empty)
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM007",
                    "Call sheet id must not be empty.",
                    null,
                    null));
            }

            // The canvas lays everything out against these, and the group bounds
            // check compares against them. A NaN makes every comparison false,
            // so a corrupt value would quietly disable those checks instead of
            // failing anywhere the user can see.
            if (!IsFinite(sheet.Width) || !IsFinite(sheet.Height) ||
                sheet.Width <= 0.0 || sheet.Height <= 0.0)
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM066",
                    "Call sheet must have finite positive width and height.",
                    null,
                    null));
            }

            if (!IsFinite(sheet.Zoom) || sheet.Zoom <= 0.0)
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM067",
                    "Call sheet zoom must be a finite positive factor.",
                    null,
                    null));
            }
        }

        private static void ValidateNodeAndPinIdentity(CallSheet sheet, IList<DiagramIssue> issues)
        {
            foreach (var node in sheet.Nodes.Where(node => node.Id == Guid.Empty))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM008",
                    "Node id must not be empty.",
                    node.Id,
                    null));
            }

            foreach (var group in sheet.Nodes.GroupBy(node => node.Id).Where(group => group.Count() > 1))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM001",
                    "Duplicate node id '" + group.Key + "'.",
                    group.Key,
                    null));
            }

            foreach (var pin in sheet.Nodes.SelectMany(node => node.Pins).Where(pin => pin.Id == Guid.Empty))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM009",
                    "Pin id must not be empty.",
                    pin.NodeId,
                    null));
            }

            foreach (var group in sheet.Nodes.SelectMany(node => node.Pins).GroupBy(pin => pin.Id).Where(group => group.Count() > 1))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM002",
                    "Duplicate pin id '" + group.Key + "'.",
                    group.First().NodeId,
                    null));
            }

            foreach (var group in sheet.Wires.GroupBy(wire => wire.Id).Where(group => group.Count() > 1))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM003",
                    "Duplicate wire id '" + group.Key + "'.",
                    null,
                    group.Key));
            }

            foreach (var wire in sheet.Wires.Where(wire => wire.Id == Guid.Empty))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM016",
                    "Wire id must not be empty.",
                    null,
                    wire.Id));
            }

            ValidateCrossKindIdentity(sheet, issues);
        }

        private static void ValidateCrossKindIdentity(CallSheet sheet, IList<DiagramIssue> issues)
        {
            var owners = new Dictionary<Guid, string>();
            RegisterIdentityOwner(sheet.Id, "sheet", owners, issues, null, null);
            foreach (var node in sheet.Nodes)
            {
                RegisterIdentityOwner(node.Id, "node", owners, issues, node.Id, null);
                foreach (var pin in node.Pins)
                {
                    RegisterIdentityOwner(pin.Id, "pin", owners, issues, node.Id, null);
                }
            }

            foreach (var wire in sheet.Wires)
            {
                RegisterIdentityOwner(wire.Id, "wire", owners, issues, null, wire.Id);
            }

            foreach (var group in (sheet.Groups ?? new List<DiagramGroup>())
                .Where(item => item != null)
                .GroupBy(item => item.Id)
                .Select(items => items.First()))
            {
                RegisterIdentityOwner(group.Id, "group", owners, issues, null, null);
            }
        }

        private static void ValidateGroups(CallSheet sheet, IList<DiagramIssue> issues)
        {
            if (sheet.Groups == null)
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM040",
                    "Call-sheet group collection is missing.",
                    null,
                    null));
                return;
            }

            foreach (var group in sheet.Groups.Where(item => item == null))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM041",
                    "Call-sheet group collection contains a null item.",
                    null,
                    null));
            }

            var groups = sheet.Groups.Where(item => item != null).ToList();
            foreach (var group in groups.Where(item => item.Id == Guid.Empty))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM042",
                    "Visual group id must not be empty.",
                    null,
                    null));
            }

            foreach (var duplicate in groups
                .Where(item => item.Id != Guid.Empty)
                .GroupBy(item => item.Id)
                .Where(items => items.Count() > 1))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM043",
                    "Duplicate visual group id '" + duplicate.Key + "'.",
                    null,
                    null));
            }

            foreach (var group in groups)
            {
                if (!IsFinite(group.X) ||
                    !IsFinite(group.Y) ||
                    !IsFinite(group.Width) ||
                    !IsFinite(group.Height) ||
                    !IsFinite(sheet.Width) ||
                    !IsFinite(sheet.Height) ||
                    group.X < 0.0 ||
                    group.Y < 0.0 ||
                    group.Width <= 0.0 ||
                    group.Height <= 0.0 ||
                    sheet.Width <= 0.0 ||
                    sheet.Height <= 0.0 ||
                    group.X > sheet.Width ||
                    group.Y > sheet.Height ||
                    group.Width > sheet.Width - group.X ||
                    group.Height > sheet.Height - group.Y)
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "DGM044",
                        "Visual group '" + DescribeGroup(group) +
                        "' must have finite positive dimensions and stay inside the call sheet.",
                        null,
                        null));
                }
            }

            var nodeIds = new HashSet<Guid>(sheet.Nodes.Select(node => node.Id));
            foreach (var group in groups)
            {
                if (group.MemberNodeIds == null)
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "DGM051",
                        "Visual group '" + DescribeGroup(group) +
                        "' has no direct-member collection.",
                        null,
                        null));
                }

                var members = group.MemberNodeIds ?? new List<Guid>();
                foreach (var duplicate in members
                    .GroupBy(nodeId => nodeId)
                    .Where(items => items.Count() > 1))
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "DGM045",
                        "Visual group '" + DescribeGroup(group) +
                        "' contains node id '" + duplicate.Key + "' more than once.",
                        null,
                        null));
                }

                foreach (var memberNodeId in members.Distinct())
                {
                    if (memberNodeId == Guid.Empty || !nodeIds.Contains(memberNodeId))
                    {
                        issues.Add(new DiagramIssue(
                            DiagramIssueSeverity.Error,
                            "DGM046",
                            "Visual group '" + DescribeGroup(group) +
                            "' refers to missing node id '" + memberNodeId + "'.",
                            null,
                            null));
                    }
                }
            }

            ValidateGroupParents(groups, issues);
            ValidateDirectGroupMembership(groups, nodeIds, issues);
        }

        private static void ValidateGroupParents(
            IList<DiagramGroup> groups,
            IList<DiagramIssue> issues)
        {
            var groupIds = new HashSet<Guid>(
                groups.Where(group => group.Id != Guid.Empty).Select(group => group.Id));
            foreach (var group in groups)
            {
                if (group.ParentGroupId == Guid.Empty)
                {
                    continue;
                }

                if (group.ParentGroupId == group.Id)
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "DGM048",
                        "Visual group '" + DescribeGroup(group) + "' cannot be its own parent.",
                        null,
                        null));
                }
                else if (!groupIds.Contains(group.ParentGroupId))
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "DGM047",
                        "Visual group '" + DescribeGroup(group) +
                        "' refers to missing parent group id '" + group.ParentGroupId + "'.",
                        null,
                        null));
                }
            }

            var hasAmbiguousIds = groups.Any(group => group.Id == Guid.Empty) ||
                groups.GroupBy(group => group.Id).Any(items => items.Count() > 1);
            if (hasAmbiguousIds)
            {
                return;
            }

            var byId = groups.ToDictionary(group => group.Id);
            var cyclicIds = FindParentCycleIds(byId);
            foreach (var group in groups.Where(group => cyclicIds.Contains(group.Id)))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM049",
                    "Visual group '" + DescribeGroup(group) + "' participates in a parent cycle.",
                    null,
                    null));
            }
        }

        private static ISet<Guid> FindParentCycleIds(IDictionary<Guid, DiagramGroup> groups)
        {
            var cyclic = new HashSet<Guid>();
            var resolved = new HashSet<Guid>();
            foreach (var startId in groups.Keys)
            {
                if (resolved.Contains(startId))
                {
                    continue;
                }

                var path = new List<Guid>();
                var positions = new Dictionary<Guid, int>();
                var currentId = startId;
                while (currentId != Guid.Empty && groups.ContainsKey(currentId))
                {
                    int cycleStart;
                    if (positions.TryGetValue(currentId, out cycleStart))
                    {
                        for (var index = cycleStart; index < path.Count; index++)
                        {
                            cyclic.Add(path[index]);
                        }

                        break;
                    }

                    if (resolved.Contains(currentId))
                    {
                        break;
                    }

                    positions.Add(currentId, path.Count);
                    path.Add(currentId);
                    var parentId = groups[currentId].ParentGroupId;
                    if (parentId == currentId)
                    {
                        break;
                    }

                    currentId = parentId;
                }

                foreach (var pathId in path)
                {
                    resolved.Add(pathId);
                }
            }

            return cyclic;
        }

        private static void ValidateDirectGroupMembership(
            IEnumerable<DiagramGroup> groups,
            ISet<Guid> nodeIds,
            IList<DiagramIssue> issues)
        {
            var memberships = groups
                .SelectMany(group => (group.MemberNodeIds ?? new List<Guid>())
                    .Where(nodeId => nodeId != Guid.Empty && nodeIds.Contains(nodeId))
                    .Distinct()
                    .Select(nodeId => new GroupMembership(group, nodeId)))
                .GroupBy(item => item.NodeId)
                .Where(items => items.Count() > 1);

            foreach (var conflict in memberships)
            {
                foreach (var membership in conflict)
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "DGM050",
                        "Node id '" + conflict.Key + "' is a direct member of multiple groups; " +
                        "one owner is '" + DescribeGroup(membership.Group) + "'.",
                        membership.NodeId,
                        null));
                }
            }
        }

        private static string DescribeGroup(DiagramGroup group)
        {
            return string.IsNullOrWhiteSpace(group.Title)
                ? group.Id.ToString()
                : group.Title + " [" + group.Id + "]";
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class GroupMembership
        {
            public GroupMembership(DiagramGroup group, Guid nodeId)
            {
                Group = group;
                NodeId = nodeId;
            }

            public DiagramGroup Group { get; private set; }

            public Guid NodeId { get; private set; }
        }

        private static void RegisterIdentityOwner(
            Guid id,
            string kind,
            IDictionary<Guid, string> owners,
            IList<DiagramIssue> issues,
            Guid? nodeId,
            Guid? wireId)
        {
            if (id == Guid.Empty)
            {
                return;
            }

            string existingKind;
            if (!owners.TryGetValue(id, out existingKind))
            {
                owners.Add(id, kind);
                return;
            }

            if (string.Equals(existingKind, kind, StringComparison.Ordinal))
            {
                return;
            }

            issues.Add(new DiagramIssue(
                DiagramIssueSeverity.Error,
                "DGM017",
                "Stable id '" + id + "' is shared by a " + existingKind + " and a " + kind + ".",
                nodeId,
                wireId));
        }

        private static bool IsIdentityIssue(string code)
        {
            return code == "DGM001" || code == "DGM002" || code == "DGM003" ||
                code == "DGM007" || code == "DGM008" || code == "DGM009" ||
                code == "DGM016" || code == "DGM017";
        }

        private static void ValidateNode(CallNode node, IList<DiagramIssue> issues)
        {
            foreach (var pin in node.Pins.Where(pin => pin.NodeId != node.Id))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM004",
                    "Pin '" + pin.Name + "' refers to the wrong owner node.",
                    node.Id,
                    null));
            }

            if (!IsFinite(node.X) || !IsFinite(node.Y))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM068",
                    "Node position must be finite.",
                    node.Id,
                    null));
            }

            var tag = node as TagNode;
            if (tag != null && tag.Pins.Count == 1)
            {
                var expected = tag.TerminalDirection == TerminalDirection.Source
                    ? PinDirection.Output
                    : PinDirection.Input;
                if (tag.Pins[0].Direction != expected)
                {
                    issues.Add(new DiagramIssue(
                        DiagramIssueSeverity.Error,
                        "DGM005",
                        "Tag terminal direction does not match its pin direction.",
                        node.Id,
                        null));
                }
            }

        }

        private static void ValidateWire(
            Wire wire,
            IDictionary<Guid, CallNode> nodes,
            IDictionary<Guid, Pin> pins,
            IDictionary<Guid, Guid> targetUsage,
            IList<DiagramIssue> issues)
        {
            CallNode sourceNode;
            CallNode targetNode;
            Pin sourcePin;
            Pin targetPin;

            if (!nodes.TryGetValue(wire.SourceNodeId, out sourceNode) ||
                !pins.TryGetValue(wire.SourcePinId, out sourcePin) ||
                sourcePin.NodeId != wire.SourceNodeId)
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM010",
                    "Wire source endpoint does not exist.",
                    wire.SourceNodeId,
                    wire.Id));
                return;
            }

            if (!nodes.TryGetValue(wire.TargetNodeId, out targetNode) ||
                !pins.TryGetValue(wire.TargetPinId, out targetPin) ||
                targetPin.NodeId != wire.TargetNodeId)
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM011",
                    "Wire target endpoint does not exist.",
                    wire.TargetNodeId,
                    wire.Id));
                return;
            }

            if (sourcePin.Direction != PinDirection.Output || targetPin.Direction != PinDirection.Input)
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM012",
                    "A wire must connect an output pin to an input pin.",
                    targetNode.Id,
                    wire.Id));
            }

            if (!PlcTypeCompatibility.AreExactlyCompatible(sourcePin.DataType, targetPin.DataType))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM013",
                    "Type mismatch: '" + sourcePin.DataType + "' cannot be connected to '" + targetPin.DataType + "'.",
                    targetNode.Id,
                    wire.Id));
            }

            Guid existingWireId;
            if (targetUsage.TryGetValue(targetPin.Id, out existingWireId))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM014",
                    "Input pin '" + targetPin.Name + "' already has a wire.",
                    targetNode.Id,
                    wire.Id));
            }
            else
            {
                targetUsage.Add(targetPin.Id, wire.Id);
            }

            if (targetNode is BlockCallNode &&
                targetPin.Role == PinRole.Parameter &&
                targetPin.IsRequired &&
                !(sourceNode is TagNode))
            {
                issues.Add(new DiagramIssue(
                    DiagramIssueSeverity.Error,
                    "DGM015",
                    "An InOut pin must be connected to a writable tag in the first implementation.",
                    targetNode.Id,
                    wire.Id));
            }
        }
    }
}
