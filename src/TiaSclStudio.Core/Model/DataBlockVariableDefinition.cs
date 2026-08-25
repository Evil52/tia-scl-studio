using System;

namespace TiaSclStudio.Core.Model
{
    /// <summary>
    /// A named variable inside a global data block. Several definitions may
    /// share DataBlockName; generated definitions are emitted into one DB.
    /// Reference-only definitions point at an object already owned by TIA.
    /// </summary>
    public sealed class DataBlockVariableDefinition
    {
        public DataBlockVariableDefinition()
        {
            Id = Guid.NewGuid();
            DataBlockName = string.Empty;
            VariableName = string.Empty;
            DataType = string.Empty;
            Comment = string.Empty;
            GenerateDataBlock = true;
        }

        public Guid Id { get; set; }

        public string DataBlockName { get; set; }

        public string VariableName { get; set; }

        public string DataType { get; set; }

        public string Comment { get; set; }

        public bool GenerateDataBlock { get; set; }
    }
}
