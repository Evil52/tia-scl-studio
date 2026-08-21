using System;

namespace TiaSclStudio.Openness.Gateway
{
    public sealed class TiaPlcTag
    {
        public TiaPlcTag(
            string tableName,
            string name,
            string dataType,
            string logicalAddress,
            string comment = null)
        {
            TableName = Required(tableName, nameof(tableName));
            Name = Required(name, nameof(name));
            DataType = Required(dataType, nameof(dataType));
            LogicalAddress = logicalAddress == null ? string.Empty : logicalAddress.Trim();
            Comment = comment == null ? string.Empty : comment;
        }

        public string TableName { get; }

        public string Name { get; }

        public string DataType { get; }

        public string LogicalAddress { get; }

        public string Comment { get; }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A non-empty value is required.", parameterName);
            }

            return value.Trim();
        }
    }
}
