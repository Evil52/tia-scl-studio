using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace TiaSclStudio.Diagram.Model
{
    /// <summary>
    /// A visual region on a call sheet. MemberNodeIds contains only direct
    /// members; membership in ancestor groups is implied by ParentGroupId.
    /// </summary>
    public sealed class DiagramGroup
    {
        public DiagramGroup()
        {
            Id = Guid.NewGuid();
            Title = string.Empty;
            Comment = string.Empty;
            Width = 320.0;
            Height = 200.0;
            MemberNodeIds = new List<Guid>();
        }

        public DiagramGroup(
            string title,
            double x,
            double y,
            double width,
            double height)
            : this()
        {
            Title = title ?? string.Empty;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public Guid Id { get; set; }

        /// <summary>Guid.Empty identifies a root-level group.</summary>
        public Guid ParentGroupId { get; set; }

        public string Title { get; set; }

        public string Comment { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        [XmlArrayItem("NodeId")]
        public List<Guid> MemberNodeIds { get; set; }
    }
}
