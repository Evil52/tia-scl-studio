using System.Globalization;
using System.Text;

namespace TiaSclStudio.Core.Validation
{
    /// <summary>
    /// Guards for the free-text values that are copied verbatim into generated
    /// SCL. A data type, an initial value or a result target is written into
    /// the middle of a declaration or a call, so a line break inside one of
    /// them terminates that construct early and silently rewrites everything
    /// that follows it in the file.
    /// </summary>
    public static class SclText
    {
        /// <summary>
        /// True when the value contains a character that cannot appear inside a
        /// single SCL declaration or call: any C0/C1 control character, and the
        /// Unicode line and paragraph separators that a paste from a document
        /// editor can carry.
        /// </summary>
        public static bool HasControlCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (var character in value)
            {
                if (IsForbidden(character))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Collapses every forbidden character to a single space so a value that
        /// slipped past validation still cannot break out of its construct.
        /// SCL treats a run of whitespace as one separator, so this preserves
        /// the meaning of every value that was already well formed.
        /// </summary>
        public static string SingleLine(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (!HasControlCharacters(value))
            {
                return value.Trim();
            }

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(IsForbidden(character) ? ' ' : character);
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// True when the value holds a character XML cannot carry, which makes
        /// the whole project unsaveable rather than merely wrong. Tab, carriage
        /// return and line feed stay allowed: comments and block bodies are
        /// legitimately multi-line.
        /// </summary>
        public static bool HasNonPersistableCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (character == '\t' || character == '\n' || character == '\r')
                {
                    continue;
                }

                if (character < ' ' || character == (char)0xFFFE || character == (char)0xFFFF)
                {
                    return true;
                }

                if (char.IsHighSurrogate(character))
                {
                    // A lone half of a surrogate pair is not a character at all.
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    {
                        return true;
                    }

                    index++;
                    continue;
                }

                if (char.IsLowSurrogate(character))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsForbidden(char character)
        {
            // char.IsControl covers C0 and C1; the two separators below are not
            // control characters but still start a new line in most editors and
            // in the TIA import parser.
            if (char.IsControl(character))
            {
                return true;
            }

            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            return category == UnicodeCategory.LineSeparator ||
                category == UnicodeCategory.ParagraphSeparator;
        }
    }
}
