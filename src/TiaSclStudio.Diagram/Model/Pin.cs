using System;

namespace TiaSclStudio.Diagram.Model
{
    public sealed class Pin
    {
        public Pin()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            DataType = string.Empty;
            Role = PinRole.Parameter;
        }

        public Guid Id { get; set; }

        public Guid NodeId { get; set; }

        public Guid MemberId { get; set; }

        public string Name { get; set; }

        public PinDirection Direction { get; set; }

        public PinRole Role { get; set; }

        public string DataType { get; set; }

        public bool IsRequired { get; set; }

        public int Order { get; set; }
    }
}
