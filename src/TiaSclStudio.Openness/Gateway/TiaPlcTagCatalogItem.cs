using System;

namespace TiaSclStudio.Openness.Gateway
{
    /// <summary>
    /// Immutable, Siemens-type-free snapshot of one PLC tag.
    /// </summary>
    public sealed class TiaPlcTagCatalogItem
    {
        public TiaPlcTagCatalogItem(
            string tablePath,
            string name,
            string dataType,
            string logicalAddress,
            string comment)
        {
            if (string.IsNullOrWhiteSpace(tablePath))
            {
                throw new ArgumentException("A stable PLC tag-table path is required.", nameof(tablePath));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A PLC tag name is required.", nameof(name));
            }

            if (string.IsNullOrWhiteSpace(dataType))
            {
                throw new ArgumentException("A PLC tag data type is required.", nameof(dataType));
            }

            TablePath = tablePath;
            Name = name;
            DataType = dataType;
            LogicalAddress = logicalAddress ?? string.Empty;
            Comment = comment ?? string.Empty;
        }

        /// <summary>
        /// Stable escaped path beginning with PLC/ for the root scope or
        /// UNIT/&lt;unit&gt;/ for a software-unit scope.
        /// </summary>
        public string TablePath { get; }

        public string Name { get; }

        public string DataType { get; }

        public string LogicalAddress { get; }

        /// <summary>
        /// One deterministic localized comment: editing language, then reference
        /// language, then the first available culture. Empty when unavailable.
        /// </summary>
        public string Comment { get; }
    }
}
