using System;

namespace TiaSclStudio.Diagram.Model
{
    public sealed class Wire
    {
        public Wire()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public Guid SourceNodeId { get; set; }

        public Guid SourcePinId { get; set; }

        public Guid TargetNodeId { get; set; }

        public Guid TargetPinId { get; set; }
    }
}
