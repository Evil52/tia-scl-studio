using System;

namespace TiaSclStudio.Openness.Gateway
{
    public sealed class TiaSourceFile
    {
        public TiaSourceFile(string filePath, int importOrder, TiaSourceKind kind)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A source file path is required.", nameof(filePath));
            }

            if (importOrder < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(importOrder));
            }

            if (!Enum.IsDefined(typeof(TiaSourceKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            FilePath = filePath;
            ImportOrder = importOrder;
            Kind = kind;
        }

        public string FilePath { get; }

        public int ImportOrder { get; }

        public TiaSourceKind Kind { get; }
    }
}
