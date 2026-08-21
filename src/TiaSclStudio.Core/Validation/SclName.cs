using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TiaSclStudio.Core.Validation
{
    /// <summary>Conservative identifier rules for symbols emitted without local quoting.</summary>
    public static class SclName
    {
        public const int MaximumLength = 125;

        private static readonly Regex IdentifierPattern =
            new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

        private static readonly HashSet<string> ReservedWords = new HashSet<string>(
            new[]
            {
                "AND", "ANY", "ARRAY", "AT", "BEGIN", "BLOCK_DB", "BOOL", "BY", "BYTE",
                "CASE", "CHAR", "CONST", "CONSTANT", "CONTINUE", "DATA_BLOCK", "DATE", "DATE_AND_TIME", "DB_ANY",
                "DINT", "DIV", "DO", "DWORD", "ELSE", "ELSIF", "END_CASE", "END_CONST",
                "END_DATA_BLOCK", "END_FOR", "END_FUNCTION", "END_FUNCTION_BLOCK", "END_IF",
                "END_LABEL", "END_ORGANIZATION_BLOCK", "END_REPEAT", "END_STRUCT", "END_TYPE",
                "END_VAR", "END_WHILE", "ENO", "EN", "EXIT", "FALSE", "FOR", "FUNCTION",
                "FUNCTION_BLOCK", "GOTO", "IF", "INT", "LABEL", "LINT", "LREAL", "LWORD", "MOD", "NIL", "NON_RETAIN",
                "NOT", "OF", "OK", "OR", "ORGANIZATION_BLOCK", "POINTER", "PROGRAM", "REAL", "REPEAT", "RETURN",
                "S5TIME", "SINT", "STRING", "STRUCT", "THEN", "TIME", "TIME_OF_DAY", "TOD", "TO", "TRUE", "TYPE",
                "UDINT", "UINT", "ULINT", "UNTIL", "USINT", "VAR", "VAR_INPUT", "VAR_IN_OUT",
                "VAR_OUTPUT", "VAR_TEMP", "VAR_CONSTANT", "VOID", "WCHAR", "WHILE", "WORD", "WSTRING", "XOR"
            },
            StringComparer.OrdinalIgnoreCase);

        public static bool IsValid(string name)
        {
            string error;
            return TryValidate(name, out error);
        }

        public static bool TryValidate(string name, out string error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Name is required.";
                return false;
            }

            var trimmed = name.Trim();
            if (!string.Equals(name, trimmed, StringComparison.Ordinal))
            {
                error = "Name must not have leading or trailing whitespace.";
                return false;
            }

            if (trimmed.Length > MaximumLength)
            {
                error = "Name must not exceed " + MaximumLength + " characters.";
                return false;
            }

            if (!IdentifierPattern.IsMatch(trimmed))
            {
                error = "Name must start with an ASCII letter or underscore and contain only ASCII letters, digits and underscores.";
                return false;
            }

            if (ReservedWords.Contains(trimmed))
            {
                error = "Name is an SCL reserved word.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
