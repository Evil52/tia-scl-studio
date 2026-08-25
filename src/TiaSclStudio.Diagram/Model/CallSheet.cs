using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace TiaSclStudio.Diagram.Model
{
    public sealed class CallSheet
    {
        public CallSheet()
        {
            Id = Guid.NewGuid();
            Name = "FC_CallUnits";
            Width = 2400;
            Height = 1600;
            Zoom = 1.0;
            Nodes = new List<CallNode>();
            Wires = new List<Wire>();
            Groups = new List<DiagramGroup>();
        }

        public Guid Id { get; set; }

        public string Name { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public double Zoom { get; set; }

        [XmlArrayItem("BlockCall", typeof(BlockCallNode))]
        [XmlArrayItem("Tag", typeof(TagNode))]
        [XmlArrayItem("DataBlockVariable", typeof(DataBlockVariableNode))]
        [XmlArrayItem("Constant", typeof(ConstantNode))]
        [XmlArrayItem("Logic", typeof(LogicNode))]
        [XmlArrayItem("Note", typeof(NoteNode))]
        public List<CallNode> Nodes { get; set; }

        [XmlArrayItem("Wire")]
        public List<Wire> Wires { get; set; }

        [XmlArrayItem("Group")]
        public List<DiagramGroup> Groups { get; set; }
    }
}
