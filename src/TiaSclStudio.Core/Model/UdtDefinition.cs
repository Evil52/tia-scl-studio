using System;
using System.Collections.Generic;

namespace TiaSclStudio.Core.Model
{
    public sealed class UdtDefinition
    {
        public UdtDefinition()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            Version = "0.1";
            Comment = string.Empty;
            Members = new List<UdtMember>();
        }

        public Guid Id { get; set; }

        public string Name { get; set; }

        public string Version { get; set; }

        public string Comment { get; set; }

        public List<UdtMember> Members { get; set; }
    }

    public sealed class UdtMember
    {
        public UdtMember()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            DataType = "Bool";
            InitialValue = string.Empty;
            Comment = string.Empty;
        }

        public UdtMember(string name, string dataType)
            : this()
        {
            Name = name ?? string.Empty;
            DataType = dataType ?? string.Empty;
        }

        public Guid Id { get; set; }

        public string Name { get; set; }

        public string DataType { get; set; }

        public string InitialValue { get; set; }

        public string Comment { get; set; }
    }
}
