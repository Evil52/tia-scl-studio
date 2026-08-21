using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;

namespace TiaSclStudio.Core.Importing
{
    /// <summary>
    /// Bounded, declaration-only parser for Siemens SCL sources. It recognizes
    /// FB, FC and TYPE/STRUCT declarations and never interprets or executes the
    /// executable part between BEGIN and END_FUNCTION(_BLOCK).
    /// </summary>
    public sealed class SclLibrarySourceParser
    {
        public const int MaximumSourceLength = 8 * 1024 * 1024;
        public const int MaximumObjectCount = 4096;
        public const int MaximumMemberCountPerObject = 32768;

        public SclLibraryImportResult Parse(string source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            var result = new SclLibraryImportResult();
            if (source.Length > MaximumSourceLength)
            {
                result.AddDiagnostic(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_SOURCE_TOO_LARGE",
                    "The SCL source exceeds the safe import limit of " + MaximumSourceLength + " characters.",
                    1,
                    1);
                return result;
            }

            var lines = LexLines(source, result);
            var precedingComments = new List<string>();
            var index = 0;
            var objectCount = 0;
            while (index < lines.Count)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line.Code))
                {
                    if (!string.IsNullOrWhiteSpace(line.Comment))
                    {
                        precedingComments.Add(line.Comment.Trim());
                    }

                    index++;
                    continue;
                }

                Header header;
                if (!TryParseHeader(line.Code, line.Number, out header))
                {
                    precedingComments.Clear();
                    index++;
                    continue;
                }

                objectCount++;
                if (objectCount > MaximumObjectCount)
                {
                    result.AddDiagnostic(
                        SclImportDiagnosticSeverity.Error,
                        "SCL_IMPORT_TOO_MANY_OBJECTS",
                        "The SCL source contains more than " + MaximumObjectCount + " importable objects.",
                        line.Number,
                        1);
                    break;
                }

                var endIndex = FindObjectEnd(lines, index + 1, header.EndKeyword);
                if (endIndex < 0)
                {
                    result.AddDiagnostic(
                        SclImportDiagnosticSeverity.Error,
                        "SCL_IMPORT_END_MISSING",
                        "The declaration '" + header.Name + "' has no matching " + header.EndKeyword + ".",
                        line.Number,
                        1);
                    break;
                }

                if (!SclName.IsValid(header.Name))
                {
                    string nameError;
                    SclName.TryValidate(header.Name, out nameError);
                    result.AddDiagnostic(
                        SclImportDiagnosticSeverity.Error,
                        "SCL_IMPORT_NAME_INVALID",
                        "Cannot import '" + header.Name + "': " + nameError,
                        line.Number,
                        1);
                }
                else
                {
                    var description = JoinComments(precedingComments, line.Comment);
                    if (header.Kind == SclImportedObjectKind.UserDataType)
                    {
                        ParseDataType(lines, index, endIndex, header, description, result);
                    }
                    else
                    {
                        ParseBlock(lines, index, endIndex, header, description, result);
                    }
                }

                precedingComments.Clear();
                index = endIndex + 1;
            }

            if (result.Items.Count == 0 && !result.HasErrors)
            {
                result.AddDiagnostic(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_NO_OBJECTS",
                    "No FUNCTION_BLOCK, FUNCTION or TYPE/STRUCT declaration was found.",
                    1,
                    1);
            }

            AddDuplicateDiagnostics(result);
            return result;
        }

        private static void ParseBlock(
            IList<SourceLine> lines,
            int headerIndex,
            int endIndex,
            Header header,
            string description,
            SclLibraryImportResult result)
        {
            var block = new BlockDefinition
            {
                Id = StableId("block", header.Kind.ToString(), header.Name),
                Name = header.Name,
                Kind = header.Kind == SclImportedObjectKind.FunctionBlock
                    ? BlockKind.FunctionBlock
                    : BlockKind.Function,
                ReturnType = header.Kind == SclImportedObjectKind.Function
                    ? (string.IsNullOrWhiteSpace(header.ReturnType) ? "Void" : CollapseWhitespace(header.ReturnType))
                    : "Void",
                Version = "0.1",
                OptimizedAccess = true,
                ImportedInterfaceOnly = true,
                Description = description,
                SclBody = string.Empty,
                Interface = new List<InterfaceMember>()
            };

            if (header.Kind == SclImportedObjectKind.Function &&
                string.IsNullOrWhiteSpace(header.ReturnType))
            {
                result.AddDiagnostic(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_FC_RETURN_MISSING",
                    "FUNCTION '" + header.Name + "' must declare an explicit return type after ':'. Use Void when it has no return value.",
                    lines[headerIndex].Number,
                    1);
            }

            InterfaceSection? section = null;
            var collector = new DeclarationCollector(result, header.Name);
            var reachedBegin = false;
            for (var index = headerIndex + 1; index < endIndex; index++)
            {
                var line = lines[index];
                var trimmed = line.Code.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    collector.AcceptComment(line.Comment);
                    continue;
                }

                if (StartsWithWord(trimmed, "BEGIN"))
                {
                    collector.FlushUnterminated(line.Number);
                    reachedBegin = true;
                    break;
                }

                if (!section.HasValue)
                {
                    InterfaceSection parsedSection;
                    if (TryParseSectionHeader(trimmed, out parsedSection))
                    {
                        section = parsedSection;
                        collector.Reset();
                        continue;
                    }

                    if (StartsWithWord(trimmed, "VAR") ||
                        trimmed.StartsWith("VAR_", StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddDiagnostic(
                            SclImportDiagnosticSeverity.Error,
                            "SCL_IMPORT_SECTION_UNSUPPORTED",
                            "The variable section '" + CollapseWhitespace(trimmed) +
                                "' in '" + block.Name + "' is not supported by the safe interface importer.",
                            line.Number,
                            1);
                        continue;
                    }

                    string version;
                    if (TryParseVersion(trimmed, out version))
                    {
                        block.Version = version;
                        continue;
                    }

                    if (StartsWithWord(trimmed, "VERSION"))
                    {
                        result.AddDiagnostic(
                            SclImportDiagnosticSeverity.Error,
                            "SCL_IMPORT_VERSION_INVALID",
                            "The VERSION declaration in '" + block.Name + "' is invalid.",
                            line.Number,
                            1);
                        continue;
                    }

                    bool optimized;
                    if (TryParseOptimizedAccess(trimmed, out optimized))
                    {
                        block.OptimizedAccess = optimized;
                    }

                    continue;
                }

                if (StartsWithWord(trimmed, "END_VAR"))
                {
                    collector.FlushUnterminated(line.Number);
                    section = null;
                    collector.Reset();
                    continue;
                }

                var statements = collector.Accept(line);
                foreach (var statement in statements)
                {
                    if (block.Kind == BlockKind.Function && section.Value == InterfaceSection.Static)
                    {
                        result.AddDiagnostic(
                            SclImportDiagnosticSeverity.Error,
                            "SCL_IMPORT_FC_STATIC_NOT_ALLOWED",
                            "FUNCTION '" + block.Name + "' cannot contain static variables.",
                            statement.Line,
                            1);
                        continue;
                    }

                    AddInterfaceMembers(block, section.Value, statement, result);
                    if (block.Interface.Count > MaximumMemberCountPerObject)
                    {
                        result.AddDiagnostic(
                            SclImportDiagnosticSeverity.Error,
                            "SCL_IMPORT_TOO_MANY_MEMBERS",
                            "The block '" + block.Name + "' exceeds the safe member limit.",
                            statement.Line,
                            1);
                        return;
                    }
                }
            }

            if (section.HasValue)
            {
                result.AddDiagnostic(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_END_VAR_MISSING",
                    "The block '" + block.Name + "' has an unterminated variable section.",
                    lines[endIndex].Number,
                    1);
            }

            if (!reachedBegin)
            {
                result.AddDiagnostic(
                    SclImportDiagnosticSeverity.Warning,
                    "SCL_IMPORT_BEGIN_MISSING",
                    "The block '" + block.Name + "' has no BEGIN marker; only the recognized interface was imported.",
                    lines[headerIndex].Number,
                    1);
            }

            AddDuplicateMemberDiagnostics(
                block.Interface.Select(member => member.Name),
                block.Name,
                lines[headerIndex].Number,
                result);

            result.AddItem(new SclLibraryImportItem(
                header.Kind,
                block.Name,
                lines[headerIndex].Number,
                block,
                null));
        }

        private static void ParseDataType(
            IList<SourceLine> lines,
            int headerIndex,
            int endIndex,
            Header header,
            string description,
            SclLibraryImportResult result)
        {
            var dataType = new UdtDefinition
            {
                Id = StableId("udt", header.Name),
                Name = header.Name,
                Version = "0.1",
                Comment = description,
                Members = new List<UdtMember>()
            };

            if (!string.IsNullOrWhiteSpace(header.Remainder))
            {
                result.AddDiagnostic(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_INLINE_STRUCT_UNSUPPORTED",
                    "TYPE '" + header.Name + "' must place STRUCT on a separate line; inline TYPE ... : STRUCT is not supported by the safe importer.",
                    lines[headerIndex].Number,
                    1);
            }

            var inStruct = false;
            var endedStruct = false;
            var collector = new DeclarationCollector(result, header.Name);
            for (var index = headerIndex + 1; index < endIndex; index++)
            {
                var line = lines[index];
                var trimmed = line.Code.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    collector.AcceptComment(line.Comment);
                    continue;
                }

                if (!inStruct)
                {
                    string version;
                    if (TryParseVersion(trimmed, out version))
                    {
                        dataType.Version = version;
                        continue;
                    }

                    if (StartsWithWord(trimmed, "VERSION"))
                    {
                        result.AddDiagnostic(
                            SclImportDiagnosticSeverity.Error,
                            "SCL_IMPORT_VERSION_INVALID",
                            "The VERSION declaration in '" + dataType.Name + "' is invalid.",
                            line.Number,
                            1);
                        continue;
                    }

                    if (StartsWithWord(trimmed, "STRUCT"))
                    {
                        inStruct = true;
                        collector.Reset();
                    }

                    continue;
                }

                if (StartsWithWord(trimmed, "END_STRUCT"))
                {
                    collector.FlushUnterminated(line.Number);
                    endedStruct = true;
                    break;
                }

                if (ContainsWord(trimmed, "STRUCT"))
                {
                    result.AddDiagnostic(
                        SclImportDiagnosticSeverity.Error,
                        "SCL_IMPORT_ANONYMOUS_STRUCT_UNSUPPORTED",
                        "Anonymous nested STRUCT members are not supported. Import the nested structure as a named UDT first.",
                        line.Number,
                        1);
                    continue;
                }

                foreach (var statement in collector.Accept(line))
                {
                    AddUdtMembers(dataType, statement, result);
                    if (dataType.Members.Count > MaximumMemberCountPerObject)
                    {
                        result.AddDiagnostic(
                            SclImportDiagnosticSeverity.Error,
                            "SCL_IMPORT_TOO_MANY_MEMBERS",
                            "The UDT '" + dataType.Name + "' exceeds the safe member limit.",
                            statement.Line,
                            1);
                        return;
                    }
                }
            }

            if (!inStruct || !endedStruct)
            {
                result.AddDiagnostic(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_STRUCT_MISSING",
                    "The TYPE '" + dataType.Name + "' must contain a complete STRUCT ... END_STRUCT declaration.",
                    lines[headerIndex].Number,
                    1);
            }

            AddDuplicateMemberDiagnostics(
                dataType.Members.Select(member => member.Name),
                dataType.Name,
                lines[headerIndex].Number,
                result);

            result.AddItem(new SclLibraryImportItem(
                SclImportedObjectKind.UserDataType,
                dataType.Name,
                lines[headerIndex].Number,
                null,
                dataType));
        }

        private static void AddInterfaceMembers(
            BlockDefinition block,
            InterfaceSection section,
            DeclarationStatement statement,
            SclLibraryImportResult result)
        {
            ParsedDeclaration declaration;
            if (!TryParseDeclaration(statement, out declaration))
            {
                result.AddDiagnostic(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_DECLARATION_INVALID",
                    "Cannot parse a declaration in block '" + block.Name + "': " + statement.Code.Trim(),
                    statement.Line,
                    1);
                return;
            }

            foreach (var name in declaration.Names)
            {
                string error;
                if (!SclName.TryValidate(name, out error))
                {
                    result.AddDiagnostic(
                        SclImportDiagnosticSeverity.Error,
                        "SCL_IMPORT_MEMBER_NAME_INVALID",
                        "Cannot import member '" + name + "' in block '" + block.Name + "': " + error,
                        statement.Line,
                        1);
                    continue;
                }

                block.Interface.Add(new InterfaceMember
                {
                    Id = StableId("block-member", block.Kind.ToString(), block.Name, section.ToString(), name),
                    Name = name,
                    DataType = declaration.DataType,
                    Section = section,
                    InitialValue = declaration.InitialValue,
                    Comment = statement.Comment
                });
            }
        }

        private static void AddUdtMembers(
            UdtDefinition dataType,
            DeclarationStatement statement,
            SclLibraryImportResult result)
        {
            ParsedDeclaration declaration;
            if (!TryParseDeclaration(statement, out declaration))
            {
                result.AddDiagnostic(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_DECLARATION_INVALID",
                    "Cannot parse a declaration in UDT '" + dataType.Name + "': " + statement.Code.Trim(),
                    statement.Line,
                    1);
                return;
            }

            foreach (var name in declaration.Names)
            {
                string error;
                if (!SclName.TryValidate(name, out error))
                {
                    result.AddDiagnostic(
                        SclImportDiagnosticSeverity.Error,
                        "SCL_IMPORT_MEMBER_NAME_INVALID",
                        "Cannot import member '" + name + "' in UDT '" + dataType.Name + "': " + error,
                        statement.Line,
                        1);
                    continue;
                }

                dataType.Members.Add(new UdtMember
                {
                    Id = StableId("udt-member", dataType.Name, name),
                    Name = name,
                    DataType = declaration.DataType,
                    InitialValue = declaration.InitialValue,
                    Comment = statement.Comment
                });
            }
        }

        private static bool TryParseDeclaration(
            DeclarationStatement statement,
            out ParsedDeclaration declaration)
        {
            declaration = null;
            var code = statement.Code == null ? string.Empty : statement.Code.Trim();
            if (code.EndsWith(";", StringComparison.Ordinal))
            {
                code = code.Substring(0, code.Length - 1).TrimEnd();
            }

            if (!HasBalancedDelimiters(code))
            {
                return false;
            }

            var colon = FindTopLevel(code, ":", 0);
            if (colon <= 0 || colon + 1 >= code.Length)
            {
                return false;
            }

            var namePart = code.Substring(0, colon).Trim();
            if (ContainsWord(namePart, "AT"))
            {
                return false;
            }

            var assignment = FindTopLevel(code, ":=", colon + 1);
            var dataTypePart = assignment < 0
                ? code.Substring(colon + 1)
                : code.Substring(colon + 1, assignment - colon - 1);
            var initialValue = assignment < 0 ? string.Empty : code.Substring(assignment + 2);
            dataTypePart = CollapseWhitespace(dataTypePart);
            initialValue = CollapseWhitespace(initialValue);
            if (string.IsNullOrWhiteSpace(dataTypePart))
            {
                return false;
            }

            var names = SplitTopLevel(namePart, ',')
                .Select(UnquoteIdentifier)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
            if (names.Count == 0)
            {
                return false;
            }

            declaration = new ParsedDeclaration(names, dataTypePart, initialValue);
            return true;
        }

        private static void AddDuplicateDiagnostics(SclLibraryImportResult result)
        {
            foreach (var group in result.Items
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
            {
                foreach (var item in group)
                {
                    result.AddDiagnostic(
                        SclImportDiagnosticSeverity.Error,
                        "SCL_IMPORT_DUPLICATE_OBJECT",
                        "The source declares the global object '" + item.Name + "' more than once.",
                        item.SourceLine,
                        1);
                }
            }
        }

        private static void AddDuplicateMemberDiagnostics(
            IEnumerable<string> memberNames,
            string owner,
            int line,
            SclLibraryImportResult result)
        {
            foreach (var name in (memberNames ?? new string[0])
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .GroupBy(item => item, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key))
            {
                result.AddDiagnostic(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_DUPLICATE_MEMBER",
                    "The declaration '" + owner + "' contains the member '" + name + "' more than once.",
                    line,
                    1);
            }
        }

        private static List<SourceLine> LexLines(string source, SclLibraryImportResult diagnostics)
        {
            var normalized = source.Replace("\r\n", "\n").Replace('\r', '\n');
            var rawLines = normalized.Split('\n');
            var result = new List<SourceLine>(rawLines.Length);
            var inBlockComment = false;
            for (var index = 0; index < rawLines.Length; index++)
            {
                var code = new StringBuilder(rawLines[index].Length);
                var comment = new StringBuilder();
                var inSingleQuote = false;
                var inDoubleQuote = false;
                var position = 0;
                while (position < rawLines[index].Length)
                {
                    var current = rawLines[index][position];
                    var next = position + 1 < rawLines[index].Length
                        ? rawLines[index][position + 1]
                        : '\0';

                    if (inBlockComment)
                    {
                        if (current == '*' && next == ')')
                        {
                            inBlockComment = false;
                            position += 2;
                        }
                        else
                        {
                            position++;
                        }

                        continue;
                    }

                    if (!inSingleQuote && !inDoubleQuote && current == '(' && next == '*')
                    {
                        inBlockComment = true;
                        position += 2;
                        continue;
                    }

                    if (!inSingleQuote && !inDoubleQuote && current == '/' && next == '/')
                    {
                        comment.Append(rawLines[index].Substring(position + 2));
                        break;
                    }

                    code.Append(current);
                    if (current == '\'' && !inDoubleQuote)
                    {
                        if (inSingleQuote && next == '\'')
                        {
                            code.Append(next);
                            position += 2;
                            continue;
                        }

                        inSingleQuote = !inSingleQuote;
                    }
                    else if (current == '"' && !inSingleQuote)
                    {
                        if (inDoubleQuote && next == '"')
                        {
                            code.Append(next);
                            position += 2;
                            continue;
                        }

                        inDoubleQuote = !inDoubleQuote;
                    }

                    position++;
                }

                if (inSingleQuote || inDoubleQuote)
                {
                    diagnostics.AddDiagnostic(
                        SclImportDiagnosticSeverity.Error,
                        "SCL_IMPORT_QUOTE_UNTERMINATED",
                        "An unterminated quoted literal or identifier was found.",
                        index + 1,
                        rawLines[index].Length == 0 ? 1 : rawLines[index].Length);
                }

                result.Add(new SourceLine(index + 1, code.ToString(), comment.ToString()));
            }

            if (inBlockComment)
            {
                diagnostics.AddDiagnostic(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_COMMENT_UNTERMINATED",
                    "An unterminated (* ... *) block comment was found.",
                    rawLines.Length,
                    rawLines.Length == 0 ? 1 : rawLines[rawLines.Length - 1].Length + 1);
            }

            return result;
        }

        private static bool TryParseHeader(string code, int line, out Header header)
        {
            header = null;
            var trimmed = code.Trim();
            SclImportedObjectKind kind;
            string keyword;
            string endKeyword;
            if (StartsWithWord(trimmed, "FUNCTION_BLOCK"))
            {
                kind = SclImportedObjectKind.FunctionBlock;
                keyword = "FUNCTION_BLOCK";
                endKeyword = "END_FUNCTION_BLOCK";
            }
            else if (StartsWithWord(trimmed, "FUNCTION"))
            {
                kind = SclImportedObjectKind.Function;
                keyword = "FUNCTION";
                endKeyword = "END_FUNCTION";
            }
            else if (StartsWithWord(trimmed, "TYPE"))
            {
                kind = SclImportedObjectKind.UserDataType;
                keyword = "TYPE";
                endKeyword = "END_TYPE";
            }
            else
            {
                return false;
            }

            var position = keyword.Length;
            SkipWhitespace(trimmed, ref position);
            string name;
            if (!TryReadIdentifier(trimmed, ref position, out name))
            {
                return false;
            }

            var returnType = string.Empty;
            if (kind == SclImportedObjectKind.Function)
            {
                SkipWhitespace(trimmed, ref position);
                if (position < trimmed.Length && trimmed[position] == ':')
                {
                    returnType = trimmed.Substring(position + 1).Trim();
                }
            }

            var remainder = position < trimmed.Length ? trimmed.Substring(position).Trim() : string.Empty;

            header = new Header(kind, name, returnType, endKeyword, line, remainder);
            return true;
        }

        private static int FindObjectEnd(IList<SourceLine> lines, int start, string endKeyword)
        {
            for (var index = start; index < lines.Count; index++)
            {
                if (StartsWithWord(lines[index].Code.Trim(), endKeyword))
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool TryParseSectionHeader(string value, out InterfaceSection section)
        {
            var normalized = CollapseWhitespace(value).Replace('_', ' ').Trim();
            if (string.Equals(normalized, "VAR INPUT", StringComparison.OrdinalIgnoreCase))
            {
                section = InterfaceSection.Input;
                return true;
            }

            if (string.Equals(normalized, "VAR OUTPUT", StringComparison.OrdinalIgnoreCase))
            {
                section = InterfaceSection.Output;
                return true;
            }

            if (string.Equals(normalized, "VAR IN OUT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "VAR INOUT", StringComparison.OrdinalIgnoreCase))
            {
                section = InterfaceSection.InOut;
                return true;
            }

            if (string.Equals(normalized, "VAR TEMP", StringComparison.OrdinalIgnoreCase))
            {
                section = InterfaceSection.Temp;
                return true;
            }

            if (string.Equals(normalized, "VAR CONSTANT", StringComparison.OrdinalIgnoreCase))
            {
                section = InterfaceSection.Constant;
                return true;
            }

            if (string.Equals(normalized, "VAR STAT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "VAR STATIC", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "VAR", StringComparison.OrdinalIgnoreCase))
            {
                section = InterfaceSection.Static;
                return true;
            }

            section = default(InterfaceSection);
            return false;
        }

        private static bool TryParseVersion(string value, out string version)
        {
            version = null;
            if (!StartsWithWord(value, "VERSION"))
            {
                return false;
            }

            var colon = value.IndexOf(':');
            if (colon < 0 || colon + 1 >= value.Length)
            {
                return false;
            }

            version = value.Substring(colon + 1).Trim().TrimEnd(';').Trim();
            return !string.IsNullOrWhiteSpace(version);
        }

        private static bool TryParseOptimizedAccess(string value, out bool optimized)
        {
            optimized = true;
            var marker = value.IndexOf("S7_Optimized_Access", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
            {
                return false;
            }

            var falseIndex = value.IndexOf("FALSE", marker, StringComparison.OrdinalIgnoreCase);
            optimized = falseIndex < 0;
            return true;
        }

        private static bool TryReadIdentifier(string value, ref int position, out string name)
        {
            name = string.Empty;
            if (position >= value.Length)
            {
                return false;
            }

            if (value[position] == '"')
            {
                position++;
                var builder = new StringBuilder();
                while (position < value.Length)
                {
                    if (value[position] == '"')
                    {
                        if (position + 1 < value.Length && value[position + 1] == '"')
                        {
                            builder.Append('"');
                            position += 2;
                            continue;
                        }

                        position++;
                        name = builder.ToString();
                        return true;
                    }

                    builder.Append(value[position++]);
                }

                return false;
            }

            var start = position;
            while (position < value.Length &&
                !char.IsWhiteSpace(value[position]) &&
                value[position] != ':' &&
                value[position] != ';')
            {
                position++;
            }

            name = value.Substring(start, position - start);
            return !string.IsNullOrWhiteSpace(name);
        }

        private static int FindTopLevel(string value, string token, int start)
        {
            var inSingle = false;
            var inDouble = false;
            var squareDepth = 0;
            var roundDepth = 0;
            for (var index = Math.Max(0, start); index <= value.Length - token.Length; index++)
            {
                var current = value[index];
                if (current == '\'' && !inDouble)
                {
                    if (inSingle && index + 1 < value.Length && value[index + 1] == '\'')
                    {
                        index++;
                        continue;
                    }

                    inSingle = !inSingle;
                    continue;
                }

                if (current == '"' && !inSingle)
                {
                    if (inDouble && index + 1 < value.Length && value[index + 1] == '"')
                    {
                        index++;
                        continue;
                    }

                    inDouble = !inDouble;
                    continue;
                }

                if (inSingle || inDouble)
                {
                    continue;
                }

                if (current == '[') squareDepth++;
                if (current == ']') squareDepth = Math.Max(0, squareDepth - 1);
                if (current == '(') roundDepth++;
                if (current == ')') roundDepth = Math.Max(0, roundDepth - 1);
                if (squareDepth == 0 && roundDepth == 0 &&
                    string.Compare(value, index, token, 0, token.Length, StringComparison.Ordinal) == 0)
                {
                    return index;
                }
            }

            return -1;
        }

        private static IList<string> SplitTopLevel(string value, char separator)
        {
            var result = new List<string>();
            var start = 0;
            var inDouble = false;
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] == '"')
                {
                    if (inDouble && index + 1 < value.Length && value[index + 1] == '"')
                    {
                        index++;
                        continue;
                    }

                    inDouble = !inDouble;
                }
                else if (!inDouble && value[index] == separator)
                {
                    result.Add(value.Substring(start, index - start).Trim());
                    start = index + 1;
                }
            }

            result.Add(value.Substring(start).Trim());
            return result;
        }

        private static bool HasBalancedDelimiters(string value)
        {
            var inSingle = false;
            var inDouble = false;
            var squareDepth = 0;
            var roundDepth = 0;
            for (var index = 0; index < (value ?? string.Empty).Length; index++)
            {
                var current = value[index];
                var next = index + 1 < value.Length ? value[index + 1] : '\0';
                if (current == '\'' && !inDouble)
                {
                    if (inSingle && next == '\'')
                    {
                        index++;
                        continue;
                    }

                    inSingle = !inSingle;
                    continue;
                }

                if (current == '"' && !inSingle)
                {
                    if (inDouble && next == '"')
                    {
                        index++;
                        continue;
                    }

                    inDouble = !inDouble;
                    continue;
                }

                if (inSingle || inDouble) continue;
                if (current == '[') squareDepth++;
                if (current == ']') squareDepth--;
                if (current == '(') roundDepth++;
                if (current == ')') roundDepth--;
                if (squareDepth < 0 || roundDepth < 0) return false;
            }

            return !inSingle && !inDouble && squareDepth == 0 && roundDepth == 0;
        }

        private static string UnquoteIdentifier(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                return trimmed.Substring(1, trimmed.Length - 2).Replace("\"\"", "\"");
            }

            return trimmed;
        }

        private static bool StartsWithWord(string value, string word)
        {
            if (value == null || word == null || value.Length < word.Length ||
                !value.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return value.Length == word.Length ||
                !(char.IsLetterOrDigit(value[word.Length]) || value[word.Length] == '_');
        }

        private static bool ContainsWord(string value, string word)
        {
            var start = 0;
            while (start < value.Length)
            {
                var index = value.IndexOf(word, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    return false;
                }

                var before = index == 0 || !(char.IsLetterOrDigit(value[index - 1]) || value[index - 1] == '_');
                var afterIndex = index + word.Length;
                var after = afterIndex == value.Length ||
                    !(char.IsLetterOrDigit(value[afterIndex]) || value[afterIndex] == '_');
                if (before && after)
                {
                    return true;
                }

                start = index + 1;
            }

            return false;
        }

        private static string CollapseWhitespace(string value)
        {
            var builder = new StringBuilder();
            var pendingSpace = false;
            var inSingle = false;
            var inDouble = false;
            foreach (var character in (value ?? string.Empty).Trim())
            {
                if (!inSingle && !inDouble && char.IsWhiteSpace(character))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(character);
                if (character == '\'' && !inDouble) inSingle = !inSingle;
                if (character == '"' && !inSingle) inDouble = !inDouble;
            }

            return builder.ToString();
        }

        private static string JoinComments(IEnumerable<string> comments, string inline)
        {
            var items = new List<string>(comments ?? new string[0]);
            if (!string.IsNullOrWhiteSpace(inline))
            {
                items.Add(inline.Trim());
            }

            return string.Join(Environment.NewLine, items.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        private static void SkipWhitespace(string value, ref int position)
        {
            while (position < value.Length && char.IsWhiteSpace(value[position]))
            {
                position++;
            }
        }

        private static Guid StableId(params string[] components)
        {
            var input = "tia-scl-studio:scl-import:v1|" +
                string.Join("|", components.Select(item => (item ?? string.Empty).Trim().ToUpperInvariant()));
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                var bytes = new byte[16];
                Array.Copy(hash, bytes, bytes.Length);
                bytes[7] = (byte)((bytes[7] & 0x0f) | 0x50);
                bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
                var id = new Guid(bytes);
                return id == Guid.Empty ? new Guid("8fe914b1-0502-52d1-91c2-04dd7b8705cd") : id;
            }
        }

        private sealed class Header
        {
            public Header(
                SclImportedObjectKind kind,
                string name,
                string returnType,
                string endKeyword,
                int line,
                string remainder)
            {
                Kind = kind;
                Name = name;
                ReturnType = returnType;
                EndKeyword = endKeyword;
                Line = line;
                Remainder = remainder ?? string.Empty;
            }

            public SclImportedObjectKind Kind { get; private set; }
            public string Name { get; private set; }
            public string ReturnType { get; private set; }
            public string EndKeyword { get; private set; }
            public int Line { get; private set; }
            public string Remainder { get; private set; }
        }

        private sealed class SourceLine
        {
            public SourceLine(int number, string code, string comment)
            {
                Number = number;
                Code = code ?? string.Empty;
                Comment = comment ?? string.Empty;
            }

            public int Number { get; private set; }
            public string Code { get; private set; }
            public string Comment { get; private set; }
        }

        private sealed class ParsedDeclaration
        {
            public ParsedDeclaration(IList<string> names, string dataType, string initialValue)
            {
                Names = names;
                DataType = dataType;
                InitialValue = initialValue;
            }

            public IList<string> Names { get; private set; }
            public string DataType { get; private set; }
            public string InitialValue { get; private set; }
        }

        private sealed class DeclarationStatement
        {
            public DeclarationStatement(string code, string comment, int line)
            {
                Code = code;
                Comment = comment;
                Line = line;
            }

            public string Code { get; private set; }
            public string Comment { get; private set; }
            public int Line { get; private set; }
        }

        private sealed class DeclarationCollector
        {
            private readonly SclLibraryImportResult _result;
            private readonly string _owner;
            private readonly StringBuilder _buffer = new StringBuilder();
            private readonly List<string> _pendingComments = new List<string>();
            private int _startLine;

            public DeclarationCollector(SclLibraryImportResult result, string owner)
            {
                _result = result;
                _owner = owner;
            }

            public void AcceptComment(string comment)
            {
                if (_buffer.Length == 0 && !string.IsNullOrWhiteSpace(comment))
                {
                    _pendingComments.Add(comment.Trim());
                }
            }

            public IList<DeclarationStatement> Accept(SourceLine line)
            {
                var result = new List<DeclarationStatement>();
                var code = line.Code;
                var segmentStart = 0;
                var inSingle = false;
                var inDouble = false;
                var squareDepth = 0;
                var roundDepth = 0;
                for (var index = 0; index < code.Length; index++)
                {
                    var current = code[index];
                    var next = index + 1 < code.Length ? code[index + 1] : '\0';
                    if (current == '\'' && !inDouble)
                    {
                        if (inSingle && next == '\'')
                        {
                            index++;
                            continue;
                        }

                        inSingle = !inSingle;
                    }
                    else if (current == '"' && !inSingle)
                    {
                        if (inDouble && next == '"')
                        {
                            index++;
                            continue;
                        }

                        inDouble = !inDouble;
                    }
                    else if (!inSingle && !inDouble)
                    {
                        if (current == '[') squareDepth++;
                        if (current == ']') squareDepth = Math.Max(0, squareDepth - 1);
                        if (current == '(') roundDepth++;
                        if (current == ')') roundDepth = Math.Max(0, roundDepth - 1);
                    }

                    if (current != ';' || inSingle || inDouble || squareDepth != 0 || roundDepth != 0)
                    {
                        continue;
                    }

                    AppendSegment(code.Substring(segmentStart, index - segmentStart + 1), line.Number);
                    var trailingComment = index == code.LastIndexOf(';') ? line.Comment : string.Empty;
                    var comments = new List<string>(_pendingComments);
                    if (!string.IsNullOrWhiteSpace(trailingComment)) comments.Add(trailingComment.Trim());
                    result.Add(new DeclarationStatement(
                        _buffer.ToString(),
                        string.Join(Environment.NewLine, comments),
                        _startLine));
                    _buffer.Clear();
                    _pendingComments.Clear();
                    _startLine = 0;
                    segmentStart = index + 1;
                }

                if (segmentStart < code.Length)
                {
                    AppendSegment(code.Substring(segmentStart), line.Number);
                }

                if (result.Count == 0 && _buffer.Length == 0)
                {
                    AcceptComment(line.Comment);
                }

                return result;
            }

            public void FlushUnterminated(int line)
            {
                if (_buffer.ToString().Trim().Length == 0)
                {
                    _buffer.Clear();
                    return;
                }

                _result.AddDiagnostic(
                    SclImportDiagnosticSeverity.Error,
                    "SCL_IMPORT_SEMICOLON_MISSING",
                    "An unterminated declaration was found in '" + _owner + "'.",
                    _startLine == 0 ? line : _startLine,
                    1);
                _buffer.Clear();
            }

            public void Reset()
            {
                _buffer.Clear();
                _pendingComments.Clear();
                _startLine = 0;
            }

            private void AppendSegment(string segment, int line)
            {
                if (string.IsNullOrWhiteSpace(segment))
                {
                    return;
                }

                if (_startLine == 0) _startLine = line;
                if (_buffer.Length > 0) _buffer.Append(' ');
                _buffer.Append(segment.Trim());
            }
        }
    }
}
