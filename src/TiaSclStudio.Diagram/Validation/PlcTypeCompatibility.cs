using System;
using System.Text;

namespace TiaSclStudio.Diagram.Validation
{
    public static class PlcTypeCompatibility
    {
        public static bool AreExactlyCompatible(string sourceType, string targetType)
        {
            return string.Equals(
                Normalize(sourceType),
                Normalize(targetType),
                StringComparison.OrdinalIgnoreCase);
        }

        public static string Normalize(string dataType)
        {
            if (string.IsNullOrWhiteSpace(dataType))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(dataType.Length);
            foreach (var character in dataType.Trim())
            {
                // Quotation marks around a global UDT name are canonical SCL,
                // while older project files may still contain the bare name.
                // Core validation guarantees quotes cannot occur elsewhere in
                // a valid type expression.
                if (!char.IsWhiteSpace(character) && character != '"')
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }
    }
}
