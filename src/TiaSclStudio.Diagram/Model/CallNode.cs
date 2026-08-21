using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace TiaSclStudio.Diagram.Model
{
    [XmlInclude(typeof(BlockCallNode))]
    [XmlInclude(typeof(TagNode))]
    [XmlInclude(typeof(ConstantNode))]
    [XmlInclude(typeof(LogicNode))]
    [XmlInclude(typeof(NoteNode))]
    public abstract class CallNode
    {
        protected CallNode()
        {
            Id = Guid.NewGuid();
            Title = string.Empty;
            Pins = new List<Pin>();
        }

        public Guid Id { get; set; }

        public string Title { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public int? ManualOrder { get; set; }

        [XmlArrayItem("Pin")]
        public List<Pin> Pins { get; set; }

        [XmlIgnore]
        public abstract CallNodeKind Kind { get; }
    }

    public sealed class BlockCallNode : CallNode
    {
        public BlockCallNode()
        {
            InstanceName = string.Empty;
        }

        [XmlIgnore]
        public override CallNodeKind Kind { get { return CallNodeKind.BlockCall; } }

        public Guid BlockDefinitionId { get; set; }

        public string InstanceName { get; set; }

        public bool UseMultiInstance { get; set; }
    }

    public sealed class TagNode : CallNode
    {
        public TagNode()
        {
            TagName = string.Empty;
            DataType = string.Empty;
        }

        [XmlIgnore]
        public override CallNodeKind Kind { get { return CallNodeKind.Tag; } }

        public Guid TagDefinitionId { get; set; }

        public string TagName { get; set; }

        public string DataType { get; set; }

        public TerminalDirection TerminalDirection { get; set; }
    }

    public sealed class ConstantNode : CallNode
    {
        public ConstantNode()
        {
            Literal = string.Empty;
            DataType = string.Empty;
        }

        [XmlIgnore]
        public override CallNodeKind Kind { get { return CallNodeKind.Constant; } }

        public string Literal { get; set; }

        public string DataType { get; set; }
    }

    public sealed class LogicNode : CallNode
    {
        [XmlIgnore]
        public override CallNodeKind Kind { get { return CallNodeKind.Logic; } }

        public LogicOperation Operation { get; set; }
    }

    public sealed class NoteNode : CallNode
    {
        public NoteNode()
        {
            Text = string.Empty;
        }

        [XmlIgnore]
        public override CallNodeKind Kind { get { return CallNodeKind.Note; } }

        public string Text { get; set; }
    }
}
