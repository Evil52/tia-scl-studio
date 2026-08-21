using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.TestSupport
{
    /// <summary>
    /// Targeted corruptions of an otherwise valid model. Each one reproduces a
    /// state that a crashed session, a hand-edited project file or a version
    /// skew between the block library and a placed node can really produce.
    /// A deformed model must be rejected with a diagnostic, never with an
    /// unhandled exception and never with silently wrong SCL.
    /// </summary>
    public static class Deformations
    {
        /// <summary>Simulates an aborted rename that left two nodes on the same id.</summary>
        public static void DuplicateNodeId(CallSheet sheet)
        {
            if (sheet.Nodes.Count < 2)
            {
                throw new InvalidOperationException("The sheet needs at least two nodes.");
            }

            sheet.Nodes[1].Id = sheet.Nodes[0].Id;
        }

        /// <summary>Simulates a pin list copied between nodes without re-keying.</summary>
        public static void DuplicatePinId(CallSheet sheet)
        {
            var pins = ModelBuilder.AllPins(sheet).ToList();
            if (pins.Count < 2)
            {
                throw new InvalidOperationException("The sheet needs at least two pins.");
            }

            pins[1].Id = pins[0].Id;
        }

        /// <summary>Simulates a wire that outlived the node it pointed at.</summary>
        public static void DanglingWireSource(CallSheet sheet)
        {
            RequireWire(sheet).SourceNodeId = Guid.NewGuid();
        }

        public static void DanglingWireTarget(CallSheet sheet)
        {
            RequireWire(sheet).TargetPinId = Guid.NewGuid();
        }

        /// <summary>Simulates a stale block-library refresh that flipped a pin direction.</summary>
        public static void FlipPinDirection(CallNode node, string pinName)
        {
            var pin = ModelBuilder.PinOf(node, pinName);
            pin.Direction = pin.Direction == PinDirection.Input
                ? PinDirection.Output
                : PinDirection.Input;
        }

        /// <summary>Simulates a data type edited in the library but not on the sheet.</summary>
        public static void StalePinDataType(CallNode node, string pinName, string dataType)
        {
            ModelBuilder.PinOf(node, pinName).DataType = dataType;
        }

        /// <summary>Simulates a pin whose owner reference was not updated on paste.</summary>
        public static void OrphanPinOwner(CallNode node, string pinName)
        {
            ModelBuilder.PinOf(node, pinName).NodeId = Guid.NewGuid();
        }

        /// <summary>Simulates a project file written by a build that lost a collection.</summary>
        public static void NullNodeCollection(CallSheet sheet)
        {
            sheet.Nodes = null;
        }

        public static void NullWireCollection(CallSheet sheet)
        {
            sheet.Wires = null;
        }

        public static void NullGroupCollection(CallSheet sheet)
        {
            sheet.Groups = null;
        }

        /// <summary>Simulates a partially deserialized list that kept a hole.</summary>
        public static void InjectNullNode(CallSheet sheet)
        {
            sheet.Nodes.Add(null);
        }

        public static void InjectNullWire(CallSheet sheet)
        {
            sheet.Wires.Add(null);
        }

        /// <summary>Simulates an enum value from a newer file format.</summary>
        public static void UndefinedLogicOperation(LogicNode node)
        {
            node.Operation = (LogicOperation)9999;
        }

        /// <summary>
        /// Builds a chain of N block calls. Used to prove that graph algorithms
        /// stay inside their stack budget instead of dying with a
        /// StackOverflowException, which .NET cannot catch.
        /// </summary>
        public static CallSheet DeepChain(int depth, out List<BlockCallNode> nodes)
        {
            if (depth < 1)
            {
                throw new ArgumentOutOfRangeException("depth");
            }

            var input = ModelBuilder.Input("In");
            var output = ModelBuilder.Output("Out");
            var block = ModelBuilder.FunctionBlock("FB_Link", input, output);

            var sheet = ModelBuilder.Sheet("FC_Deep");
            nodes = new List<BlockCallNode>(depth);
            for (var index = 0; index < depth; index++)
            {
                nodes.Add(ModelBuilder.PlaceBlock(sheet, block, "L" + index, index * 10.0, 0.0));
            }

            for (var index = 1; index < depth; index++)
            {
                ModelBuilder.Connect(
                    sheet,
                    ModelBuilder.PinOf(nodes[index - 1], "Out"),
                    ModelBuilder.PinOf(nodes[index], "In"));
            }

            return sheet;
        }

        /// <summary>Closes a deep chain into one long cycle.</summary>
        public static CallSheet DeepCycle(int depth)
        {
            List<BlockCallNode> nodes;
            var sheet = DeepChain(depth, out nodes);
            ModelBuilder.Connect(
                sheet,
                ModelBuilder.PinOf(nodes[depth - 1], "Out"),
                ModelBuilder.PinOf(nodes[0], "In"));
            return sheet;
        }

        private static Wire RequireWire(CallSheet sheet)
        {
            if (sheet.Wires.Count == 0)
            {
                throw new InvalidOperationException("The sheet needs at least one wire.");
            }

            return sheet.Wires[0];
        }
    }
}
