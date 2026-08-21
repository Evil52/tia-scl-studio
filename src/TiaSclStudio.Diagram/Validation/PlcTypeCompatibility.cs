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
                if (!char.IsWhiteSpace(character))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }
    }
}
