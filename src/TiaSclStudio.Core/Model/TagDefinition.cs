using System;

namespace TiaSclStudio.Core.Model
{
    public sealed class TagDefinition
    {
        public TagDefinition()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            DataType = "Bool";
            Address = string.Empty;
            Comment = string.Empty;
        }

        public Guid Id { get; set; }

        public string Name { get; set; }

        public string DataType { get; set; }

        public string Address { get; set; }

        public string Comment { get; set; }
    }
}
