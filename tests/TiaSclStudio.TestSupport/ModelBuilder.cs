using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.TestSupport
{
    /// <summary>
    /// Builds valid Plant/diagram models with as little ceremony as possible so
    /// each test can state only the property it is actually exercising.
    /// </summary>
    public static class ModelBuilder
    {
        public static BlockDefinition FunctionBlock(string name, params InterfaceMember[] members)
        {
            var block = new BlockDefinition
            {
                Name = name,
                Kind = BlockKind.FunctionBlock,
                ReturnType = "Void",
                Version = "0.1",
                Description = string.Empty,
                SclBody = string.Empty
            };

            block.Interface.AddRange(members ?? new InterfaceMember[0]);
            return block;
        }

        public static BlockDefinition Function(
            string name,
            string returnType,
            params InterfaceMember[] members)
        {
            var block = new BlockDefinition
            {
                Name = name,
                Kind = BlockKind.Function,
                ReturnType = returnType,
                Version = "0.1",
                Description = string.Empty,
                SclBody = string.Empty
            };

            block.Interface.AddRange(members ?? new InterfaceMember[0]);
            return block;
        }

        public static InterfaceMember Input(string name, string dataType = "Bool", string initialValue = null)
        {
            return new InterfaceMember(name, dataType, InterfaceSection.Input)
            {
                InitialValue = initialValue ?? string.Empty
            };
        }

        public static InterfaceMember Output(string name, string dataType = "Bool")
        {
            return new InterfaceMember(name, dataType, InterfaceSection.Output);
        }

        public static InterfaceMember InOut(string name, string dataType = "Bool")
        {
            return new InterfaceMember(name, dataType, InterfaceSection.InOut);
        }

        public static InterfaceMember Static(string name, string dataType = "Bool")
        {
            return new InterfaceMember(name, dataType, InterfaceSection.Static);
        }

        public static InterfaceMember Temp(string name, string dataType = "Bool")
        {
            return new InterfaceMember(name, dataType, InterfaceSection.Temp);
        }

        public static TagDefinition Tag(string name, string dataType = "Bool", string address = null)
        {
            return new TagDefinition
            {
                Name = name,
                DataType = dataType,
                Address = address ?? "%M0.0"
            };
        }

        public static DataBlockVariableDefinition DbVariable(
            string dataBlockName,
            string variableName,
            string dataType,
            bool generateDataBlock = true)
        {
            return new DataBlockVariableDefinition
            {
                DataBlockName = dataBlockName,
                VariableName = variableName,
                DataType = dataType,
                GenerateDataBlock = generateDataBlock
            };
        }

        public static UdtDefinition Udt(string name, params UdtMember[] members)
        {
            var udt = new UdtDefinition { Name = name, Version = "0.1" };
            udt.Members.AddRange(members ?? new UdtMember[0]);
            return udt;
        }

        public static PlantProject Plant(string name = "TestPlant")
        {
            return new PlantProject { Name = name };
        }

        public static CallSheet Sheet(string name = "FC_CallUnits")
        {
            return new CallSheet { Name = name };
        }

        public static DiagramProject Project(string plantName = "TestPlant", string sheetName = "FC_CallUnits")
        {
            var project = new DiagramProject { Plant = Plant(plantName) };
            project.Sheets.Add(Sheet(sheetName));
            return project;
        }

        /// <summary>
        /// Places a block-call node whose pin contract is produced by the same
        /// factory the application uses, so a test starts from a refreshed node.
        /// </summary>
        public static BlockCallNode PlaceBlock(
            CallSheet sheet,
            BlockDefinition definition,
            string instanceName,
            double x = 100.0,
            double y = 100.0,
            bool useMultiInstance = false)
        {
            var node = DiagramNodeFactory.CreateBlockCall(definition, instanceName, x, y);
            node.UseMultiInstance = useMultiInstance;
            sheet.Nodes.Add(node);
            return node;
        }

        public static TagNode PlaceTag(
            CallSheet sheet,
            TagDefinition definition,
            TerminalDirection direction,
            double x = 10.0,
            double y = 10.0)
        {
            var node = DiagramNodeFactory.CreateTag(definition, direction, x, y);
            sheet.Nodes.Add(node);
            return node;
        }

        public static ConstantNode PlaceConstant(
            CallSheet sheet,
            string literal,
            string dataType,
            double x = 10.0,
            double y = 200.0)
        {
            var node = DiagramNodeFactory.CreateConstant(literal, dataType, x, y);
            sheet.Nodes.Add(node);
            return node;
        }

        public static DataBlockVariableNode PlaceDbVariable(
            CallSheet sheet,
            DataBlockVariableDefinition definition,
            TerminalDirection direction,
            double x = 10.0,
            double y = 10.0)
        {
            var node = DiagramNodeFactory.CreateDataBlockVariable(definition, direction, x, y);
            sheet.Nodes.Add(node);
            return node;
        }

        public static LogicNode PlaceLogic(
            CallSheet sheet,
            LogicOperation operation,
            string operandDataType = "Bool",
            double x = 50.0,
            double y = 50.0)
        {
            var node = DiagramNodeFactory.CreateLogic(operation, operandDataType, x, y);
            sheet.Nodes.Add(node);
            return node;
        }

        public static NoteNode PlaceNote(CallSheet sheet, string text, double x = 0.0, double y = 0.0)
        {
            var node = new NoteNode { Text = text, Title = "Note", X = x, Y = y };
            sheet.Nodes.Add(node);
            return node;
        }

        public static Wire Connect(CallSheet sheet, Pin source, Pin target)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            if (target == null)
            {
                throw new ArgumentNullException("target");
            }

            var wire = new Wire
            {
                SourceNodeId = source.NodeId,
                SourcePinId = source.Id,
                TargetNodeId = target.NodeId,
                TargetPinId = target.Id
            };

            sheet.Wires.Add(wire);
            return wire;
        }

        public static Wire Connect(
            CallSheet sheet,
            CallNode source,
            string sourcePin,
            CallNode target,
            string targetPin)
        {
            return Connect(sheet, PinOf(source, sourcePin), PinOf(target, targetPin));
        }

        public static Pin PinOf(CallNode node, string pinName)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }

            var matches = node.Pins
                .Where(pin => string.Equals(pin.Name, pinName, StringComparison.Ordinal))
                .ToList();
            if (matches.Count != 1)
            {
                throw new InvalidOperationException(
                    "Node has " + matches.Count + " pins named " + pinName + ", expected exactly one.");
            }

            return matches[0];
        }

        public static Pin OnlyPin(CallNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }

            if (node.Pins.Count != 1)
            {
                throw new InvalidOperationException(
                    "Node has " + node.Pins.Count + " pins, expected exactly one.");
            }

            return node.Pins[0];
        }

        public static IEnumerable<Pin> AllPins(CallSheet sheet)
        {
            return sheet.Nodes.SelectMany(node => node.Pins);
        }
    }
}
