using System;

namespace TiaSclStudio.Core.Model
{
    public sealed class InterfaceMember
    {
        public InterfaceMember()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            DataType = "Bool";
            InitialValue = string.Empty;
            Comment = string.Empty;
        }

        public InterfaceMember(string name, string dataType, InterfaceSection section)
            : this()
        {
            Name = name ?? string.Empty;
            DataType = dataType ?? string.Empty;
            Section = section;
        }

        public Guid Id { get; set; }

        public string Name { get; set; }

        public string DataType { get; set; }

        public InterfaceSection Section { get; set; }

        public string InitialValue { get; set; }

        public string Comment { get; set; }
    }
}
