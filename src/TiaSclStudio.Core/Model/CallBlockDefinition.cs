using System;
using System.Collections.Generic;

namespace TiaSclStudio.Core.Model
{
    public sealed class CallBlockDefinition
    {
        public CallBlockDefinition()
        {
            Id = Guid.NewGuid();
            Name = "FC_CallUnits";
            Kind = BlockKind.Function;
            ReturnType = "Void";
            Version = "0.1";
            OptimizedAccess = true;
            InstanceDbName = string.Empty;
            Description = string.Empty;
            Interface = new List<InterfaceMember>();
            Units = new List<UnitDefinition>();
        }

        public Guid Id { get; set; }

        public string Name { get; set; }

        public BlockKind Kind { get; set; }

        public string ReturnType { get; set; }

        public string Version { get; set; }

        public bool OptimizedAccess { get; set; }

        /// <summary>Optional DB name for an FB call block. Defaults to Name + "_DB".</summary>
        public string InstanceDbName { get; set; }

        public string Description { get; set; }

        public List<InterfaceMember> Interface { get; set; }

        public List<UnitDefinition> Units { get; set; }
    }
}
