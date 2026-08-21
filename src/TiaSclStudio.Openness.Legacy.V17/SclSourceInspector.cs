using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaSclStudio.Openness.Legacy.V17
{
    internal enum SclDeclarationKind
    {
        DataType,
        FunctionBlock,
        Function,
        DataBlock,
        OrganizationBlock
    }

    internal sealed class SclDeclaration
    {
        public SclDeclaration(SclDeclarationKind kind, string name)
        {
            Kind = kind;
            Name = name;
        }

        public SclDeclarationKind Kind { get; private set; }

        public string Name { get; private set; }

        public bool IsDataType
        {
            get { return Kind == SclDeclarationKind.DataType; }
        }
    }

    internal static class SclSourceInspector
    {
        private static readonly Regex DeclarationPattern = new Regex(
            @"^\s*(TYPE|FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|ORGANIZATION_BLOCK)\s+(?:""((?:""""|[^""])*)""|([A-Za-z_][A-Za-z0-9_]*))",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase |
            RegexOptions.Multiline);

        private static readonly Regex VersionPattern = new Regex(
            @"^[ \t]*VERSION[ \t]*:",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase |
            RegexOptions.Multiline);

        private static readonly Regex HeaderTerminatorPattern = new Regex(
            @"^[ \t]*(?:VAR(?:_[A-Z_]+)?|BEGIN|NON_RETAIN|RETAIN)\b",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase |
            RegexOptions.Multiline);

        private static readonly Regex FamilyPattern = new Regex(
            @"^[ \t]*FAMILY[ \t]*:[ \t]*(?<value>'(?:''|[^'\r\n])*'|[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase |
            RegexOptions.Multiline);

        public static IList<SclDeclaration> FindDeclarations(string source)
        {
            var declarations = new List<SclDeclaration>();
            var withoutComments = RemoveComments(source ?? string.Empty);
            foreach (Match match in DeclarationPattern.Matches(withoutComments))
            {
                var keyword = match.Groups[1].Value.ToUpperInvariant();
                var name = match.Groups[2].Success
                    ? match.Groups[2].Value.Replace("\"\"", "\"")
                    : match.Groups[3].Value;
                declarations.Add(new SclDeclaration(ParseKind(keyword), name));
            }

            return declarations;
        }

        /// <summary>
        /// Returns a deterministic import copy with the requested FAMILY header
        /// on each block declaration. TYPE declarations are intentionally left
        /// unchanged because PlcType has no ownership header in Openness V17.
        /// </summary>
        public static string StampOwnershipFamily(string source, string marker)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (string.IsNullOrWhiteSpace(marker))
            {
                throw new ArgumentException("An ownership marker is required.", nameof(marker));
            }

            var masked = RemoveComments(source);
            var matches = DeclarationPattern.Matches(masked);
            var edits = new List<TextEdit>();
            for (var index = 0; index < matches.Count; index++)
            {
                var declaration = matches[index];
                var kind = ParseKind(declaration.Groups[1].Value.ToUpperInvariant());
                if (kind == SclDeclarationKind.DataType)
                {
                    continue;
                }

                var segmentEnd = index + 1 < matches.Count
                    ? matches[index + 1].Index
                    : source.Length;
                var headerStart = FindNextLineStart(source, declaration.Index + declaration.Length, segmentEnd);
                if (headerStart < 0)
                {
                    throw new InvalidOperationException(
                        "Cannot locate the header of block declaration '" + GetDeclarationName(declaration) + "'.");
                }

                var headerEnd = segmentEnd;
                var terminator = HeaderTerminatorPattern.Match(masked, headerStart, segmentEnd - headerStart);
                if (terminator.Success)
                {
                    headerEnd = terminator.Index;
                }

                var version = VersionPattern.Match(masked, headerStart, headerEnd - headerStart);
                if (!version.Success)
                {
                    throw new InvalidOperationException(
                        "Block declaration '" + GetDeclarationName(declaration) +
                        "' has no VERSION header; ownership FAMILY cannot be inserted safely.");
                }

                Match family = null;
                var candidate = FamilyPattern.Match(masked, headerStart, headerEnd - headerStart);
                while (candidate.Success && candidate.Index < headerEnd)
                {
                    if (family != null)
                    {
                        throw new InvalidOperationException(
                            "Block declaration '" + GetDeclarationName(declaration) +
                            "' contains more than one FAMILY header.");
                    }

                    family = candidate;
                    candidate = candidate.NextMatch();
                }

                var stampedValue = "'" + marker + "'";
                if (family != null)
                {
                    var value = family.Groups["value"];
                    edits.Add(new TextEdit(value.Index, value.Length, stampedValue));
                }
                else
                {
                    var indentationLength = 0;
                    while (version.Index + indentationLength < source.Length)
                    {
                        var character = source[version.Index + indentationLength];
                        if (character != ' ' && character != '\t')
                        {
                            break;
                        }

                        indentationLength++;
                    }

                    var indentation = source.Substring(version.Index, indentationLength);
                    var newLine = DetectNewLine(source);
                    edits.Add(new TextEdit(
                        version.Index,
                        0,
                        indentation + "FAMILY : " + stampedValue + newLine));
                }
            }

            var builder = new StringBuilder(source);
            edits.Sort((left, right) => right.Start.CompareTo(left.Start));
            foreach (var edit in edits)
            {
                builder.Remove(edit.Start, edit.Length);
                builder.Insert(edit.Start, edit.Replacement);
            }

            return builder.ToString();
        }

        private static int FindNextLineStart(string source, int startIndex, int limit)
        {
            for (var index = startIndex; index < limit; index++)
            {
                if (source[index] == '\n')
                {
                    return index + 1;
                }

                if (source[index] == '\r')
                {
                    return index + 1 < limit && source[index + 1] == '\n'
                        ? index + 2
                        : index + 1;
                }
            }

            return -1;
        }

        private static string GetDeclarationName(Match match)
        {
            return match.Groups[2].Success
                ? match.Groups[2].Value.Replace("\"\"", "\"")
                : match.Groups[3].Value;
        }

        private static string DetectNewLine(string source)
        {
            var lineFeed = source.IndexOf('\n');
            return lineFeed > 0 && source[lineFeed - 1] == '\r' ? "\r\n" : "\n";
        }

        private static SclDeclarationKind ParseKind(string keyword)
        {
            switch (keyword)
            {
                case "TYPE":
                    return SclDeclarationKind.DataType;
                case "FUNCTION_BLOCK":
                    return SclDeclarationKind.FunctionBlock;
                case "FUNCTION":
                    return SclDeclarationKind.Function;
                case "DATA_BLOCK":
                    return SclDeclarationKind.DataBlock;
                default:
                    return SclDeclarationKind.OrganizationBlock;
            }
        }

        private static string RemoveComments(string source)
        {
            var builder = new StringBuilder(source.Length);
            var inBlockComment = false;
            var inQuotedIdentifier = false;
            var inString = false;

            for (var index = 0; index < source.Length; index++)
            {
                var current = source[index];
                var next = index + 1 < source.Length ? source[index + 1] : '\0';

                if (inBlockComment)
                {
                    if (current == '*' && next == ')')
                    {
                        inBlockComment = false;
                        builder.Append("  ");
                        index++;
                    }
                    else
                    {
                        builder.Append(current == '\r' || current == '\n' ? current : ' ');
                    }

                    continue;
                }

                if (!inQuotedIdentifier && !inString && current == '(' && next == '*')
                {
                    inBlockComment = true;
                    builder.Append("  ");
                    index++;
                    continue;
                }

                if (!inQuotedIdentifier && !inString && current == '/' && next == '/')
                {
                    while (index < source.Length && source[index] != '\r' && source[index] != '\n')
                    {
                        builder.Append(' ');
                        index++;
                    }

                    if (index < source.Length)
                    {
                        builder.Append(source[index]);
                    }

                    continue;
                }

                if (!inString && current == '"')
                {
                    if (inQuotedIdentifier && next == '"')
                    {
                        builder.Append("\"\"");
                        index++;
                        continue;
                    }

                    inQuotedIdentifier = !inQuotedIdentifier;
                }
                else if (!inQuotedIdentifier && current == '\'')
                {
                    if (inString && next == '\'')
                    {
                        builder.Append("''");
                        index++;
                        continue;
                    }

                    inString = !inString;
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        private sealed class TextEdit
        {
            public TextEdit(int start, int length, string replacement)
            {
                Start = start;
                Length = length;
                Replacement = replacement;
            }

            public int Start { get; private set; }

            public int Length { get; private set; }

            public string Replacement { get; private set; }
        }
    }
}
