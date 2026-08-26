using System;
using System.Collections.Generic;
using System.Linq;
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
            RegexOptions.Multiline,
            SclRegexTimeouts.MatchTimeout);

        private static readonly Regex VersionPattern = new Regex(
            @"^[ \t]*VERSION[ \t]*:",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase |
            RegexOptions.Multiline,
            SclRegexTimeouts.MatchTimeout);

        private static readonly Regex HeaderTerminatorPattern = new Regex(
            @"^[ \t]*(?:VAR(?:_[A-Z_]+)?|BEGIN|NON_RETAIN|RETAIN)\b",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase |
            RegexOptions.Multiline,
            SclRegexTimeouts.MatchTimeout);

        private static readonly Regex FamilyPattern = new Regex(
            @"^[ \t]*FAMILY[ \t]*:[ \t]*(?<value>'(?:''|[^'\r\n])*'|[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase |
            RegexOptions.Multiline,
            SclRegexTimeouts.MatchTimeout);

        private static readonly Regex BeginPattern = new Regex(
            @"^[ \t]*BEGIN\b",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase |
            RegexOptions.Multiline,
            SclRegexTimeouts.MatchTimeout);

        private static readonly string[] CompoundOperators =
        {
            ":=", "=>", "<=", ">=", "<>", "**", "..", "&&", "||"
        };

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
        /// Returns the complete text of exactly one structurally closed top-level
        /// declaration. A missing or duplicate kind/name pair is refused instead
        /// of selecting an arbitrary declaration.
        /// </summary>
        public static string GetSingleDeclarationSource(
            string source,
            SclDeclarationKind kind,
            string name)
        {
            ValidateSourceAndName(source, name);
            var matches = FindDeclarationSpans(source)
                .Where(item => item.Declaration.Kind == kind &&
                    string.Equals(item.Declaration.Name, name, StringComparison.Ordinal))
                .ToList();
            if (matches.Count != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one " + kind + " declaration named '" + name +
                    "', but found " + matches.Count + ".");
            }

            return source.Substring(matches[0].Start, matches[0].Length);
        }

        /// <summary>
        /// Rebuilds a source from the selected, structurally complete top-level
        /// declarations in their original order. Source-level trivia between
        /// declarations is deliberately discarded so it cannot leave an invalid
        /// fragment behind.
        /// </summary>
        public static string KeepDeclarations(
            string source,
            IEnumerable<SclDeclaration> selectedDeclarations)
        {
            return FilterDeclarations(source, selectedDeclarations, true);
        }

        /// <summary>
        /// Rebuilds a source from every structurally complete top-level
        /// declaration except the selected declarations.
        /// </summary>
        public static string RemoveDeclarations(
            string source,
            IEnumerable<SclDeclaration> selectedDeclarations)
        {
            return FilterDeclarations(source, selectedDeclarations, false);
        }

        /// <summary>
        /// Produces a deterministic token stream for functional comparison.
        /// BOMs, comments and whitespace between tokens are ignored. String
        /// literals, quoted identifiers and operator token boundaries remain
        /// significant.
        /// </summary>
        public static string Canonicalize(string source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return string.Join(" ", Tokenize(source).Select(item => item.Text));
        }

        /// <summary>
        /// Returns the canonical declaration header and interface before the
        /// top-level BEGIN of one FB, FC or DB. The ownership FAMILY header is
        /// excluded because it is adapter metadata, not part of the PLC-facing
        /// interface.
        /// </summary>
        public static string GetCanonicalHeaderAndInterface(
            string source,
            SclDeclarationKind kind,
            string name)
        {
            if (kind != SclDeclarationKind.FunctionBlock &&
                kind != SclDeclarationKind.Function &&
                kind != SclDeclarationKind.DataBlock)
            {
                throw new ArgumentException(
                    "Only FUNCTION_BLOCK, FUNCTION and DATA_BLOCK declarations have a comparable header/interface.",
                    nameof(kind));
            }

            var declaration = GetSingleDeclarationSource(source, kind, name);
            var masked = RemoveComments(declaration);
            var begin = BeginPattern.Match(masked);
            if (!begin.Success)
            {
                throw new InvalidOperationException(
                    kind + " declaration '" + name + "' has no top-level BEGIN delimiter.");
            }

            var header = declaration.Substring(0, begin.Index);
            var maskedHeader = masked.Substring(0, begin.Index);
            var declarationMatch = DeclarationPattern.Match(maskedHeader);
            if (!declarationMatch.Success)
            {
                throw new InvalidOperationException(
                    "Cannot locate the header of " + kind + " declaration '" + name + "'.");
            }

            var metadataStart = FindNextLineStart(
                header,
                declarationMatch.Groups[1].Index + declarationMatch.Groups[1].Length,
                header.Length);
            var metadataEnd = header.Length;
            if (metadataStart >= 0)
            {
                var terminator = HeaderTerminatorPattern.Match(
                    maskedHeader,
                    metadataStart,
                    maskedHeader.Length - metadataStart);
                if (terminator.Success)
                {
                    metadataEnd = terminator.Index;
                }
            }

            Match family = null;
            if (metadataStart >= 0 && metadataStart < metadataEnd)
            {
                var candidate = FamilyPattern.Match(
                    maskedHeader,
                    metadataStart,
                    metadataEnd - metadataStart);
                while (candidate.Success && candidate.Index < metadataEnd)
                {
                    if (family != null)
                    {
                        throw new InvalidOperationException(
                            kind + " declaration '" + name + "' contains more than one FAMILY header.");
                    }

                    family = candidate;
                    candidate = candidate.NextMatch();
                }
            }

            if (family != null)
            {
                var lineStart = FindLineStart(header, family.Index);
                var lineEnd = FindLineEnd(header, family.Index);
                header = header.Remove(lineStart, lineEnd - lineStart);
            }

            return Canonicalize(header);
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

        private static string FilterDeclarations(
            string source,
            IEnumerable<SclDeclaration> selectedDeclarations,
            bool keepSelected)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (selectedDeclarations == null)
            {
                throw new ArgumentNullException(nameof(selectedDeclarations));
            }

            var selections = new List<SclDeclaration>();
            foreach (var selected in selectedDeclarations)
            {
                if (selected == null)
                {
                    throw new ArgumentException(
                        "A declaration selection cannot contain null.",
                        nameof(selectedDeclarations));
                }

                if (string.IsNullOrWhiteSpace(selected.Name))
                {
                    throw new ArgumentException(
                        "Every declaration selection requires a name.",
                        nameof(selectedDeclarations));
                }

                if (selections.Any(item => IsSameDeclaration(item, selected)))
                {
                    throw new ArgumentException(
                        "The declaration selection contains duplicate " + selected.Kind +
                        " entry '" + selected.Name + "'.",
                        nameof(selectedDeclarations));
                }

                selections.Add(selected);
            }

            var spans = FindDeclarationSpans(source);
            foreach (var selection in selections)
            {
                var count = spans.Count(item => IsSameDeclaration(item.Declaration, selection));
                if (count != 1)
                {
                    throw new InvalidOperationException(
                        "Expected exactly one " + selection.Kind + " declaration named '" +
                        selection.Name + "', but found " + count + ".");
                }
            }

            var builder = new StringBuilder(source.Length);
            foreach (var span in spans)
            {
                var selected = selections.Any(item => IsSameDeclaration(item, span.Declaration));
                if (selected == keepSelected)
                {
                    builder.Append(source, span.Start, span.Length);
                }
            }

            return builder.ToString();
        }

        private static IList<DeclarationSpan> FindDeclarationSpans(string source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var masked = RemoveComments(source);
            var matches = DeclarationPattern.Matches(masked);
            var spans = new List<DeclarationSpan>(matches.Count);
            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                var kind = ParseKind(match.Groups[1].Value.ToUpperInvariant());
                var name = GetDeclarationName(match);
                var start = FindLineStart(source, match.Groups[1].Index);
                var limit = index + 1 < matches.Count
                    ? FindLineStart(source, matches[index + 1].Groups[1].Index)
                    : source.Length;
                var endPattern = CreateEndPattern(kind);
                var end = endPattern.Match(masked, match.Groups[1].Index + match.Groups[1].Length);
                if (!end.Success || end.Index >= limit)
                {
                    throw new InvalidOperationException(
                        kind + " declaration '" + name + "' is not closed before the next top-level declaration or end of source.");
                }

                var duplicateEnd = end.NextMatch();
                if (duplicateEnd.Success && duplicateEnd.Index < limit)
                {
                    throw new InvalidOperationException(
                        kind + " declaration '" + name + "' contains more than one top-level closing keyword.");
                }

                spans.Add(new DeclarationSpan(
                    new SclDeclaration(kind, name),
                    start,
                    end.Index + end.Length - start));
            }

            return spans;
        }

        private static Regex CreateEndPattern(SclDeclarationKind kind)
        {
            string keyword;
            switch (kind)
            {
                case SclDeclarationKind.DataType:
                    keyword = "END_TYPE";
                    break;
                case SclDeclarationKind.FunctionBlock:
                    keyword = "END_FUNCTION_BLOCK";
                    break;
                case SclDeclarationKind.Function:
                    keyword = "END_FUNCTION";
                    break;
                case SclDeclarationKind.DataBlock:
                    keyword = "END_DATA_BLOCK";
                    break;
                default:
                    keyword = "END_ORGANIZATION_BLOCK";
                    break;
            }

            return new Regex(
                @"^[ \t]*" + keyword + @"\b[^\r\n]*(?:\r\n|\r|\n)?",
                RegexOptions.CultureInvariant |
                RegexOptions.IgnoreCase |
                RegexOptions.Multiline,
                SclRegexTimeouts.MatchTimeout);
        }

        private static IList<SclToken> Tokenize(string source)
        {
            var tokens = new List<SclToken>();
            for (var index = 0; index < source.Length;)
            {
                var current = source[index];
                var next = index + 1 < source.Length ? source[index + 1] : '\0';

                if (current == '\uFEFF' || char.IsWhiteSpace(current))
                {
                    index++;
                    continue;
                }

                if (current == '/' && next == '/')
                {
                    index += 2;
                    while (index < source.Length && source[index] != '\r' && source[index] != '\n')
                    {
                        index++;
                    }

                    continue;
                }

                if (current == '(' && next == '*')
                {
                    var commentStart = index;
                    index += 2;
                    while (index + 1 < source.Length &&
                        !(source[index] == '*' && source[index + 1] == ')'))
                    {
                        index++;
                    }

                    if (index + 1 >= source.Length)
                    {
                        throw new InvalidOperationException(
                            "SCL contains an unterminated block comment at offset " + commentStart + ".");
                    }

                    index += 2;
                    continue;
                }

                if (current == '\'' || current == '"')
                {
                    var delimiter = current;
                    var literalStart = index;
                    index++;
                    var closed = false;
                    while (index < source.Length)
                    {
                        if (source[index] == '\r' || source[index] == '\n')
                        {
                            break;
                        }

                        if (source[index] == delimiter)
                        {
                            if (index + 1 < source.Length && source[index + 1] == delimiter)
                            {
                                index += 2;
                                continue;
                            }

                            index++;
                            closed = true;
                            break;
                        }

                        index++;
                    }

                    if (!closed)
                    {
                        var kind = delimiter == '\'' ? "string literal" : "quoted identifier";
                        throw new InvalidOperationException(
                            "SCL contains an unterminated " + kind + " at offset " + literalStart + ".");
                    }

                    tokens.Add(new SclToken(source.Substring(literalStart, index - literalStart)));
                    continue;
                }

                string compoundOperator;
                if (TryReadCompoundOperator(source, index, out compoundOperator))
                {
                    tokens.Add(new SclToken(compoundOperator));
                    index += compoundOperator.Length;
                    continue;
                }

                if (IsCanonicalPunctuation(current))
                {
                    tokens.Add(new SclToken(current.ToString()));
                    index++;
                    continue;
                }

                var tokenStart = index;
                while (index < source.Length)
                {
                    current = source[index];
                    next = index + 1 < source.Length ? source[index + 1] : '\0';
                    if (current == '\uFEFF' ||
                        char.IsWhiteSpace(current) ||
                        current == '\'' ||
                        current == '"' ||
                        (current == '/' && next == '/') ||
                        (current == '(' && next == '*') ||
                        IsCanonicalPunctuation(current))
                    {
                        break;
                    }

                    index++;
                }

                if (index == tokenStart)
                {
                    throw new InvalidOperationException(
                        "Cannot tokenize SCL character at offset " + index + ".");
                }

                tokens.Add(new SclToken(source.Substring(tokenStart, index - tokenStart)));
            }

            return tokens;
        }

        private static bool TryReadCompoundOperator(string source, int index, out string value)
        {
            foreach (var candidate in CompoundOperators)
            {
                if (index + candidate.Length <= source.Length &&
                    string.CompareOrdinal(source, index, candidate, 0, candidate.Length) == 0)
                {
                    value = candidate;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static bool IsCanonicalPunctuation(char character)
        {
            if (character == '_' || character == '#' || character == '%' || character == '$' || character == '@')
            {
                return false;
            }

            return char.IsPunctuation(character) || char.IsSymbol(character);
        }

        private static bool IsSameDeclaration(SclDeclaration left, SclDeclaration right)
        {
            return left.Kind == right.Kind &&
                string.Equals(left.Name, right.Name, StringComparison.Ordinal);
        }

        private static void ValidateSourceAndName(string source, string name)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A declaration name is required.", nameof(name));
            }
        }

        private static int FindLineStart(string source, int index)
        {
            while (index > 0 && source[index - 1] != '\r' && source[index - 1] != '\n')
            {
                index--;
            }

            return index;
        }

        private static int FindLineEnd(string source, int index)
        {
            while (index < source.Length && source[index] != '\r' && source[index] != '\n')
            {
                index++;
            }

            if (index < source.Length && source[index] == '\r')
            {
                index++;
                if (index < source.Length && source[index] == '\n')
                {
                    index++;
                }
            }
            else if (index < source.Length && source[index] == '\n')
            {
                index++;
            }

            return index;
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

        private sealed class DeclarationSpan
        {
            public DeclarationSpan(SclDeclaration declaration, int start, int length)
            {
                Declaration = declaration;
                Start = start;
                Length = length;
            }

            public SclDeclaration Declaration { get; private set; }

            public int Start { get; private set; }

            public int Length { get; private set; }
        }

        private sealed class SclToken
        {
            public SclToken(string text)
            {
                Text = text;
            }

            public string Text { get; private set; }
        }
    }
}
