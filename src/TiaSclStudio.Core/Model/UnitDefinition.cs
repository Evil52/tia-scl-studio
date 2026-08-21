using System;
using System.Collections.Generic;

namespace TiaSclStudio.Core.Model
{
    /// <summary>A placed FB/FC call together with its parameter bindings.</summary>
    public sealed class UnitDefinition
    {
        public UnitDefinition()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            BlockName = string.Empty;
            Comment = string.Empty;
            ResultTarget = string.Empty;
            Bindings = new List<ParameterBinding>();
        }

        public UnitDefinition(string name, BlockDefinition block)
            : this()
        {
            Name = name ?? string.Empty;
            BindTo(block);
        }

        public Guid Id { get; set; }

        public string Name { get; set; }

        /// <summary>Stable reference to BlockDefinition.</summary>
        public Guid BlockId { get; set; }

        /// <summary>Readable fallback used when importing an older project without IDs.</summary>
        public string BlockName { get; set; }

        /// <summary>When true, an FB is declared in VAR of the generated call FB.</summary>
        public bool UseMultiInstance { get; set; }

        /// <summary>Destination expression for the return value of a non-Void FC.</summary>
        public string ResultTarget { get; set; }

        public string Comment { get; set; }

        public int? ManualOrder { get; set; }

        public List<ParameterBinding> Bindings { get; set; }

        public void BindTo(BlockDefinition block)
        {
            if (block == null)
            {
                throw new ArgumentNullException("block");
            }

            BlockId = block.Id;
            BlockName = block.Name;
        }
    }
}
