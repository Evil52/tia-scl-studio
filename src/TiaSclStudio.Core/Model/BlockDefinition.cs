using System;
using System.Collections.Generic;

namespace TiaSclStudio.Core.Model
{
    public sealed class BlockDefinition
    {
        public BlockDefinition()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            Kind = BlockKind.FunctionBlock;
            ReturnType = "Void";
            Version = "0.1";
            OptimizedAccess = true;
            Description = string.Empty;
            SclBody = string.Empty;
            Interface = new List<InterfaceMember>();
        }

        public Guid Id { get; set; }

        public string Name { get; set; }

        public BlockKind Kind { get; set; }

        /// <summary>Return type used by an FC. FB blocks always generate as Void.</summary>
        public string ReturnType { get; set; }

        public string Version { get; set; }

        public bool OptimizedAccess { get; set; }

        /// <summary>Marks a block whose body is owned by an existing TIA project.</summary>
        public bool ImportedInterfaceOnly { get; set; }

        public string Description { get; set; }

        public string SclBody { get; set; }

        public List<InterfaceMember> Interface { get; set; }
    }
}
