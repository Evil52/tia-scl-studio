using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaSclStudio.TestSupport
{
    /// <summary>Text helpers for asserting on generated SCL.</summary>
    public static class Scl
    {
        public static string Normalize(string source)
        {
            return (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }

        public static IList<string> Lines(string source)
        {
            return Normalize(source).Split('\n').ToList();
        }

        /// <summary>Zero-based index of the first line containing the fragment, or -1.</summary>
        public static int IndexOfLineContaining(string source, string fragment)
        {
            var lines = Lines(source);
            for (var index = 0; index < lines.Count; index++)
            {
                if (lines[index].IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    return index;
                }
            }

            return -1;
        }

        public static int CountOccurrences(string source, string fragment)
        {
            if (string.IsNullOrEmpty(fragment))
            {
                throw new ArgumentException("A fragment is required.", "fragment");
            }

            var text = Normalize(source);
            var count = 0;
            var index = text.IndexOf(fragment, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal);
            }

            return count;
        }

        /// <summary>
        /// The declaration section between a VAR-style header and its END_VAR,
        /// or an empty list when the section is absent.
        /// </summary>
        public static IList<string> Section(string source, string header)
        {
            var lines = Lines(source);
            var start = -1;
            for (var index = 0; index < lines.Count; index++)
            {
                if (string.Equals(lines[index].Trim(), header, StringComparison.Ordinal))
                {
                    start = index + 1;
                    break;
                }
            }

            if (start < 0)
            {
                return new List<string>();
            }

            var body = new List<string>();
            for (var index = start; index < lines.Count; index++)
            {
                if (string.Equals(lines[index].Trim(), "END_VAR", StringComparison.Ordinal))
                {
                    return body;
                }

                body.Add(lines[index].Trim());
            }

            throw new InvalidOperationException("Section " + header + " is not terminated by END_VAR.");
        }
    }
}
