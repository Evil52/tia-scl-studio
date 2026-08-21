using System;
using System.Collections.Generic;

namespace TiaSclStudio.Core.Model
{
    public sealed class PlantProject
    {
        public PlantProject()
        {
            Id = Guid.NewGuid();
            Name = "NewProject";
            CpuFamily = PlcCpuFamily.Unknown;
            Blocks = new List<BlockDefinition>();
            DataTypes = new List<UdtDefinition>();
            Tags = new List<TagDefinition>();
            CallBlocks = new List<CallBlockDefinition>();
        }

        public Guid Id { get; set; }

        public string Name { get; set; }

        public PlcCpuFamily CpuFamily { get; set; }

        public List<BlockDefinition> Blocks { get; set; }

        public List<UdtDefinition> DataTypes { get; set; }

        public List<TagDefinition> Tags { get; set; }

        public List<CallBlockDefinition> CallBlocks { get; set; }
    }
}
