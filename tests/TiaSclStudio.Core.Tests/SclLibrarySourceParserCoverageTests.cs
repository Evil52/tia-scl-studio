using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using System.Text;
using TiaSclStudio.Core.Importing;
using TiaSclStudio.Core.Model;
using Xunit;

namespace TiaSclStudio.Core.Tests
{
    public sealed class SclLibrarySourceParserCoverageTests
    {
        [Fact]
        public void ObjectCountIsBounded()
        {
            var source = new StringBuilder();
            for (var index = 0; index <= SclLibrarySourceParser.MaximumObjectCount; index++)
            {
                source.Append("FUNCTION_BLOCK FB_").Append(index).AppendLine();
                source.AppendLine("BEGIN");
                source.AppendLine("END_FUNCTION_BLOCK");
            }

            var result = new SclLibrarySourceParser().Parse(source.ToString());

            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.Code == "SCL_IMPORT_TOO_MANY_OBJECTS");
            Assert.Equal(SclLibrarySourceParser.MaximumObjectCount, result.Items.Count);
        }

        [Fact]
        public void BlockReportsInvalidVersionStaticFunctionAndMissingBegin()
        {
            var result = Parse(
                "FUNCTION FC_A : Void",
                "VERSION broken",
                "VAR",
                "   State : Int;",
                "END_VAR",
                "END_FUNCTION");

            AssertCodes(
                result,
                "SCL_IMPORT_VERSION_INVALID",
                "SCL_IMPORT_FC_STATIC_NOT_ALLOWED",
                "SCL_IMPORT_BEGIN_MISSING");
        }

        [Fact]
        public void UdtReportsInvalidVersionMissingAndAnonymousStructs()
        {
            var invalidVersion = Parse(
                "TYPE TypeA",
                "VERSION broken",
                "STRUCT",
                "   Nested : STRUCT;",
                "END_STRUCT;",
                "END_TYPE");
            AssertCodes(
                invalidVersion,
                "SCL_IMPORT_VERSION_INVALID",
                "SCL_IMPORT_ANONYMOUS_STRUCT_UNSUPPORTED");

            var missingStruct = Parse(
                "TYPE TypeB",
                "VERSION : 1.0",
                "END_TYPE");
            AssertCodes(missingStruct, "SCL_IMPORT_STRUCT_MISSING");
        }

        [Fact]
        public void InvalidAndDuplicateBlockMembersAreDiagnosedWhileOverlaysAreSkipped()
        {
            var result = Parse(
                "FUNCTION_BLOCK FB_A",
                "VAR_INPUT",
                "   MissingColon;",
                "   1Bad : Bool;",
                "   Good : Bool;",
                "   good : Bool;",
                "   Overlay AT Good : Bool;",
                "END_VAR",
                "VAR CONSTANT",
                "   Scope.Value : Int := 1;",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            AssertCodes(
                result,
                "SCL_IMPORT_DECLARATION_INVALID",
                "SCL_IMPORT_MEMBER_NAME_INVALID",
                "SCL_IMPORT_DUPLICATE_MEMBER");
            var block = Assert.Single(result.Items).Block;
            Assert.DoesNotContain(block.Interface, member => member.Name == "Overlay");
            Assert.DoesNotContain(block.Interface, member => member.Name == "Scope.Value");
        }

        [Fact]
        public void InvalidAndDuplicateUdtMembersAreDiagnosedWhileOverlaysAreSkipped()
        {
            var result = Parse(
                "TYPE TypeA",
                "STRUCT",
                "   MissingColon;",
                "   1Bad : Bool;",
                "   Good : Bool;",
                "   good : Bool;",
                "   Overlay AT Good : Bool;",
                "END_STRUCT;",
                "END_TYPE");

            AssertCodes(
                result,
                "SCL_IMPORT_DECLARATION_INVALID",
                "SCL_IMPORT_MEMBER_NAME_INVALID",
                "SCL_IMPORT_DUPLICATE_MEMBER");
            var dataType = Assert.Single(result.Items).DataType;
            Assert.DoesNotContain(dataType.Members, member => member.Name == "Overlay");
        }

        [Fact]
        public void MultilineDeclarationsPreserveCommentsAndQuotedDelimiters()
        {
            var result = Parse(
                "// leading",
                "FUNCTION_BLOCK \"FB_Quoted\"",
                "VAR_INPUT",
                "   // pending",
                "   A { ExternalAccessible := 'False' } : Array[0..1] of String[10] := [",
                "      'it''s;ok', \"a\"\"b\"]; // trailing",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            Assert.False(result.HasErrors, Codes(result));
            var block = Assert.Single(result.Items).Block;
            Assert.Equal("FB_Quoted", block.Name);
            var member = Assert.Single(block.Interface);
            Assert.Contains("pending", member.Comment);
            Assert.Contains("trailing", member.Comment);
            Assert.Contains("'it''s;ok'", member.InitialValue);
            Assert.Contains("\"a\"\"b\"", member.InitialValue);
        }

        [Theory]
        [InlineData("FUNCTION_BLOCK")]
        [InlineData("FUNCTION \"unterminated")]
        [InlineData("TYPE")]
        public void HeadersWithoutACompleteIdentifierAreNotImported(string header)
        {
            var result = Parse(header, "BEGIN", "END_FUNCTION_BLOCK");

            Assert.Empty(result.Items);
            Assert.True(result.HasErrors, Codes(result));
        }

        [Theory]
        [InlineData("Value : Array[0..1 of Bool;")]
        [InlineData("Value : Bool) ;")]
        [InlineData("Value : Bool] ;")]
        [InlineData("Value : Bool} ;")]
        [InlineData(": Bool;")]
        [InlineData("Value : ;")]
        public void UnbalancedOrIncompleteDeclarationsFailClosed(string declaration)
        {
            var result = Parse(
                "FUNCTION_BLOCK FB_A",
                "VAR_INPUT",
                declaration,
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            Assert.True(result.HasErrors, Codes(result));
        }

        [Fact]
        public void EmptyAndInlineCommentsAroundSectionsAreHandled()
        {
            var result = Parse(
                "FUNCTION_BLOCK FB_A // inline block comment",
                "VAR_INPUT",
                "   // comment without a declaration",
                "",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            Assert.False(result.HasErrors, Codes(result));
            Assert.Contains("inline block comment", Assert.Single(result.Items).Block.Description);
        }

        [Fact]
        public void BlockAndUdtMemberCountsAreBounded()
        {
            var names = string.Join(",", Enumerable.Range(
                0,
                SclLibrarySourceParser.MaximumMemberCountPerObject + 1)
                .Select(index => "M" + index));

            var block = Parse(
                "FUNCTION_BLOCK FB_Large",
                "VAR_INPUT",
                names + " : Bool;",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");
            var udt = Parse(
                "TYPE UdtLarge",
                "STRUCT",
                names + " : Bool;",
                "END_STRUCT;",
                "END_TYPE");

            AssertCodes(block, "SCL_IMPORT_TOO_MANY_MEMBERS");
            AssertCodes(udt, "SCL_IMPORT_TOO_MANY_MEMBERS");
        }

        [Theory]
        [InlineData("Value : := 1;")]
        [InlineData("\"\" : Bool;")]
        public void EmptyTypeOrEmptyQuotedMemberNameIsRejected(string declaration)
        {
            var result = Parse(
                "FUNCTION_BLOCK FB_A",
                "VAR_INPUT",
                declaration,
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            AssertCodes(result, "SCL_IMPORT_DECLARATION_INVALID");
        }

        [Fact]
        public void PrivateLexicalHelpersHandleEscapedQuotesAndBoundaryMisses()
        {
            var parser = typeof(SclLibrarySourceParser);

            var readIdentifier = parser.GetMethod(
                "TryReadIdentifier",
                BindingFlags.NonPublic | BindingFlags.Static);
            var identifierArguments = new object[] { "\"A\"\"B\"", 0, string.Empty };
            Assert.True((bool)readIdentifier.Invoke(null, identifierArguments));
            Assert.Equal("A\"B", identifierArguments[2]);

            var findTopLevel = parser.GetMethod(
                "FindTopLevel",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.Equal(6, (int)findTopLevel.Invoke(null, new object[] { "'a''b':x", ":", 0 }));
            Assert.Equal(6, (int)findTopLevel.Invoke(null, new object[] { "\"a\"\"b\":x", ":", 0 }));
            Assert.Equal(3, (int)findTopLevel.Invoke(null, new object[] { "(a):x", ":", 0 }));
            Assert.Equal(3, (int)findTopLevel.Invoke(null, new object[] { "{a}:x", ":", 0 }));

            var splitTopLevel = parser.GetMethod(
                "SplitTopLevel",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.Equal(2, ((IList)splitTopLevel.Invoke(
                null,
                new object[] { "'a''b',tail", ',' })).Count);
            Assert.Equal(2, ((IList)splitTopLevel.Invoke(
                null,
                new object[] { "\"a\"\"b\",tail", ',' })).Count);
            Assert.Equal(4, ((IList)splitTopLevel.Invoke(
                null,
                new object[] { "[a,b],(c,d),{e,f},tail", ',' })).Count);

            var balanced = parser.GetMethod(
                "HasBalancedDelimiters",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.True((bool)balanced.Invoke(null, new object[] { "(value)" }));

            var removeAttributes = parser.GetMethod(
                "RemoveTopLevelAttributeBlocks",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.Equal("'a''b' \"c\"\"d\"", (string)removeAttributes.Invoke(
                null,
                new object[] { "'a''b' \"c\"\"d\" { ignored := TRUE }" }));

            var removeModifier = parser.GetMethod(
                "RemoveTrailingSectionModifier",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.Equal(string.Empty, (string)removeModifier.Invoke(
                null,
                new object[] { null, "RETAIN" }));

            var containsWord = parser.GetMethod(
                "ContainsWord",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.False((bool)containsWord.Invoke(null, new object[] { "XA", "A" }));

            var findWord = parser.GetMethod(
                "FindTopLevelWord",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.Equal(-1, (int)findWord.Invoke(null, new object[] { "XA", "A" }));
        }

        [Fact]
        public void DeclarationCollectorPreservesACommentOnAnEmptyCodeLine()
        {
            var parser = typeof(SclLibrarySourceParser);
            var sourceLineType = parser.GetNestedType("SourceLine", BindingFlags.NonPublic);
            var collectorType = parser.GetNestedType("DeclarationCollector", BindingFlags.NonPublic);
            var importResult = new SclLibraryImportResult();
            var sourceLine = Activator.CreateInstance(sourceLineType, new object[] { 7, "", "pending" });
            var collector = Activator.CreateInstance(
                collectorType,
                new object[] { importResult, "FB_A" });

            var statements = (IList)collectorType.GetMethod("Accept").Invoke(
                collector,
                new[] { sourceLine });

            Assert.Empty(statements);

            var parenthesizedLine = Activator.CreateInstance(
                sourceLineType,
                new object[] { 8, "Value : (Bool);", "" });
            collectorType.GetMethod("Accept").Invoke(collector, new[] { parenthesizedLine });
        }

        private static SclLibraryImportResult Parse(params string[] lines)
        {
            return new SclLibrarySourceParser().Parse(string.Join("\r\n", lines));
        }

        private static void AssertCodes(
            SclLibraryImportResult result,
            params string[] expectedCodes)
        {
            foreach (var code in expectedCodes)
            {
                Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
            }
        }

        private static string Codes(SclLibraryImportResult result)
        {
            return string.Join(", ", result.Diagnostics.Select(item => item.Code));
        }
    }
}
