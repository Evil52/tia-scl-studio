using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.Diagram.Validation
{
    public sealed class TopologicalSortResult
    {
        public TopologicalSortResult(
            IEnumerable<Guid> orderedNodeIds,
            IEnumerable<Guid> cyclicNodeIds)
        {
            OrderedNodeIds = new List<Guid>(orderedNodeIds);
            CyclicNodeIds = new List<Guid>(cyclicNodeIds);
        }

        public IList<Guid> OrderedNodeIds { get; private set; }

        public IList<Guid> CyclicNodeIds { get; private set; }

        public bool HasCycle { get { return CyclicNodeIds.Count > 0; } }
    }

    public static class TopologicalSorter
    {
        public static TopologicalSortResult Sort(CallSheet sheet)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException("sheet");
            }

            // Sorting is also reached from public entry points that have not
            // run identity validation yet, so a duplicate id or a hole in a
            // collection has to degrade into a partial order here and be
            // reported as a diagnostic by the validator, not throw from inside
            // a LINQ call the caller cannot attribute to anything.
            var executable = (sheet.Nodes ?? new List<CallNode>())
                .Where(node => node != null && node.Id != Guid.Empty && IsExecutable(node))
                .GroupBy(node => node.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var incoming = executable.Keys.ToDictionary(id => id, id => 0);
            var outgoing = executable.Keys.ToDictionary(id => id, id => new HashSet<Guid>());

            foreach (var wire in (sheet.Wires ?? new List<Wire>()).Where(item => item != null))
            {
                if (!executable.ContainsKey(wire.SourceNodeId) ||
                    !executable.ContainsKey(wire.TargetNodeId))
                {
                    continue;
                }

                if (outgoing[wire.SourceNodeId].Add(wire.TargetNodeId))
                {
                    incoming[wire.TargetNodeId]++;
                }
            }

            var comparer = Comparer<CallNode>.Create(CompareNodes);
            var ready = new SortedSet<CallNode>(comparer);
            foreach (var node in executable.Values.Where(node => incoming[node.Id] == 0))
            {
                ready.Add(node);
            }

            var ordered = new List<Guid>();
            while (ready.Count > 0)
            {
                var node = ready.Min;
                ready.Remove(node);
                ordered.Add(node.Id);

                foreach (var targetId in outgoing[node.Id])
                {
                    incoming[targetId]--;
                    if (incoming[targetId] == 0)
                    {
                        ready.Add(executable[targetId]);
                    }
                }
            }

            var cyclicNodeIds = FindCyclicNodeIds(outgoing);
            var cyclic = executable.Values
                .Where(node => cyclicNodeIds.Contains(node.Id))
                .OrderBy(node => node, comparer)
                .Select(node => node.Id)
                .ToList();
            return new TopologicalSortResult(ordered, cyclic);
        }

        private static ISet<Guid> FindCyclicNodeIds(IDictionary<Guid, HashSet<Guid>> outgoing)
        {
            return new StronglyConnectedComponentFinder(outgoing).FindCyclicNodeIds();
        }

        private static bool IsExecutable(CallNode node)
        {
            return node is BlockCallNode || node is LogicNode;
        }

        private static int CompareNodes(CallNode left, CallNode right)
        {
            var orderComparison = Nullable.Compare(left.ManualOrder, right.ManualOrder);
            if (left.ManualOrder.HasValue || right.ManualOrder.HasValue)
            {
                if (!left.ManualOrder.HasValue)
                {
                    return 1;
                }

                if (!right.ManualOrder.HasValue)
                {
                    return -1;
                }

                if (orderComparison != 0)
                {
                    return orderComparison;
                }
            }

            var xComparison = left.X.CompareTo(right.X);
            if (xComparison != 0)
            {
                return xComparison;
            }

            var yComparison = left.Y.CompareTo(right.Y);
            if (yComparison != 0)
            {
                return yComparison;
            }

            return left.Id.CompareTo(right.Id);
        }

        private sealed class StronglyConnectedComponentFinder
        {
            private static readonly HashSet<Guid> EmptyTargets = new HashSet<Guid>();

            private readonly IDictionary<Guid, HashSet<Guid>> _outgoing;
            private readonly Dictionary<Guid, int> _indexes = new Dictionary<Guid, int>();
            private readonly Dictionary<Guid, int> _lowLinks = new Dictionary<Guid, int>();
            private readonly Stack<Guid> _stack = new Stack<Guid>();
            private readonly HashSet<Guid> _onStack = new HashSet<Guid>();
            private readonly HashSet<Guid> _cyclicNodeIds = new HashSet<Guid>();
            private int _nextIndex;

            public StronglyConnectedComponentFinder(IDictionary<Guid, HashSet<Guid>> outgoing)
            {
                _outgoing = outgoing;
            }

            public ISet<Guid> FindCyclicNodeIds()
            {
                foreach (var nodeId in _outgoing.Keys)
                {
                    if (!_indexes.ContainsKey(nodeId))
                    {
                        Visit(nodeId);
                    }
                }

                return _cyclicNodeIds;
            }

            /// <summary>
            /// Tarjan's walk over an explicit frame stack rather than the call
            /// stack. A recursive version needs one CLR frame per edge of the
            /// deepest path, so a sheet holding a long chain of calls exhausts
            /// the thread stack. That failure is not recoverable: Windows tears
            /// the process down with a modal system error instead of raising an
            /// exception the editor could report and survive.
            /// </summary>
            private void Visit(Guid rootId)
            {
                var frames = new Stack<Frame>();
                frames.Push(Open(rootId));

                while (frames.Count > 0)
                {
                    var frame = frames.Peek();
                    if (frame.Targets.MoveNext())
                    {
                        var targetId = frame.Targets.Current;
                        if (!_indexes.ContainsKey(targetId))
                        {
                            frames.Push(Open(targetId));
                        }
                        else if (_onStack.Contains(targetId))
                        {
                            _lowLinks[frame.NodeId] =
                                Math.Min(_lowLinks[frame.NodeId], _indexes[targetId]);
                        }

                        continue;
                    }

                    frames.Pop();
                    frame.Targets.Dispose();
                    Close(frame.NodeId);

                    if (frames.Count > 0)
                    {
                        var parentId = frames.Peek().NodeId;
                        _lowLinks[parentId] = Math.Min(_lowLinks[parentId], _lowLinks[frame.NodeId]);
                    }
                }
            }

            private Frame Open(Guid nodeId)
            {
                _indexes.Add(nodeId, _nextIndex);
                _lowLinks.Add(nodeId, _nextIndex);
                _nextIndex++;
                _stack.Push(nodeId);
                _onStack.Add(nodeId);
                return new Frame(nodeId, Targets(nodeId).GetEnumerator());
            }

            private void Close(Guid nodeId)
            {
                if (_lowLinks[nodeId] != _indexes[nodeId])
                {
                    return;
                }

                var component = new List<Guid>();
                Guid componentNodeId;
                do
                {
                    componentNodeId = _stack.Pop();
                    _onStack.Remove(componentNodeId);
                    component.Add(componentNodeId);
                }
                while (componentNodeId != nodeId);

                if (component.Count > 1 || Targets(component[0]).Contains(component[0]))
                {
                    foreach (var cyclicNodeId in component)
                    {
                        _cyclicNodeIds.Add(cyclicNodeId);
                    }
                }
            }

            private HashSet<Guid> Targets(Guid nodeId)
            {
                HashSet<Guid> targets;
                return _outgoing.TryGetValue(nodeId, out targets) ? targets : EmptyTargets;
            }

            private sealed class Frame
            {
                public Frame(Guid nodeId, IEnumerator<Guid> targets)
                {
                    NodeId = nodeId;
                    Targets = targets;
                }

                public Guid NodeId { get; private set; }

                public IEnumerator<Guid> Targets { get; private set; }
            }
        }
    }
}
