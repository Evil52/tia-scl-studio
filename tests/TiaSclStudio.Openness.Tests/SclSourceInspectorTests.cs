using System;
using System.Linq;
using TiaSclStudio.Openness.Legacy.V17;
using Xunit;

namespace TiaSclStudio.Openness.Tests
{
    /// <summary>
    /// Ownership stamping rewrites SCL that is about to be imported into a live
    /// TIA project. It edits the text by absolute offset, so the comment mask
    /// it works from has to be exactly as long as the original, and it must
    /// refuse anything it cannot place precisely rather than guess.
    /// </summary>
    public sealed class SclSourceInspectorTests
    {
        private const string Marker = "TIASCL";

        private static string Block(string name, string extraHeader = "")
        {
            return
                "FUNCTION_BLOCK \"" + name + "\"\r\n" +
                "{ S7_Optimized_Access := 'TRUE' }\r\n" +
                "VERSION : 0.1\r\n" +
                extraHeader +
                "VAR_INPUT\r\n" +
                "   Open : Bool;\r\n" +
                "END_VAR\r\n" +
                "BEGIN\r\n" +
                "END_FUNCTION_BLOCK\r\n";
        }

        // ---------------------------------------------------------------
        // Declaration discovery
        // ---------------------------------------------------------------

        [Fact]
        public void FindsEveryDeclarationKind()
        {
            const string source =
                "TYPE \"UDT_A\"\r\nEND_TYPE\r\n" +
                "FUNCTION_BLOCK \"FB_A\"\r\nEND_FUNCTION_BLOCK\r\n" +
                "FUNCTION \"FC_A\" : Void\r\nEND_FUNCTION\r\n" +
                "DATA_BLOCK \"DB_A\"\r\nEND_DATA_BLOCK\r\n" +
                "ORGANIZATION_BLOCK \"OB_A\"\r\nEND_ORGANIZATION_BLOCK\r\n";

            var declarations = SclSourceInspector.FindDeclarations(source);

            Assert.Equal(
                new[] { "UDT_A", "FB_A", "FC_A", "DB_A", "OB_A" },
                declarations.Select(item => item.Name).ToArray());
            Assert.Equal(SclDeclarationKind.DataType, declarations[0].Kind);
            Assert.True(declarations[0].IsDataType);
            Assert.Equal(SclDeclarationKind.FunctionBlock, declarations[1].Kind);
            Assert.Equal(SclDeclarationKind.Function, declarations[2].Kind);
            Assert.Equal(SclDeclarationKind.DataBlock, declarations[3].Kind);
            Assert.Equal(SclDeclarationKind.OrganizationBlock, declarations[4].Kind);
        }

        [Fact]
        public void ToleratesANullSource()
        {
            Assert.Empty(SclSourceInspector.FindDeclarations(null));
        }

        [Fact]
        public void PrefersFunctionBlockOverFunction()
        {
            var declarations = SclSourceInspector.FindDeclarations(
                "FUNCTION_BLOCK \"FB_A\"\r\nEND_FUNCTION_BLOCK\r\n");

            Assert.Equal(SclDeclarationKind.FunctionBlock, declarations.Single().Kind);
        }

        [Fact]
        public void IgnoresTheClosingKeywords()
        {
            var declarations = SclSourceInspector.FindDeclarations(Block("FB_A"));

            Assert.Single(declarations);
        }

        [Fact]
        public void ReadsAnUnquotedName()
        {
            var declarations = SclSourceInspector.FindDeclarations("FUNCTION_BLOCK FB_A\r\n");

            Assert.Equal("FB_A", declarations.Single().Name);
        }

        [Fact]
        public void UnescapesADoubledQuoteInsideAQuotedName()
        {
            var declarations = SclSourceInspector.FindDeclarations("FUNCTION_BLOCK \"FB\"\"A\"\r\n");

            Assert.Equal("FB\"A", declarations.Single().Name);
        }

        [Fact]
        public void DoesNotSeeADeclarationInsideALineComment()
        {
            var declarations = SclSourceInspector.FindDeclarations(
                "// FUNCTION_BLOCK \"Commented\"\r\n" + Block("FB_Real"));

            Assert.Equal(new[] { "FB_Real" }, declarations.Select(item => item.Name).ToArray());
        }

        [Fact]
        public void DoesNotSeeADeclarationInsideABlockComment()
        {
            var declarations = SclSourceInspector.FindDeclarations(
                "(*\r\nFUNCTION_BLOCK \"Commented\"\r\n*)\r\n" + Block("FB_Real"));

            Assert.Equal(new[] { "FB_Real" }, declarations.Select(item => item.Name).ToArray());
        }

        [Fact]
        public void DoesNotSeeADeclarationInsideAStringLiteral()
        {
            // SCL string literals never span lines, so a keyword inside one can
            // never sit at the start of a line where the pattern would find it.
            var declarations = SclSourceInspector.FindDeclarations(
                "FUNCTION_BLOCK \"FB_Real\"\r\n" +
                "VERSION : 0.1\r\n" +
                "BEGIN\r\n" +
                "   #Text := 'FUNCTION_BLOCK Fake';\r\n" +
                "END_FUNCTION_BLOCK\r\n");

            Assert.Equal(new[] { "FB_Real" }, declarations.Select(item => item.Name).ToArray());
        }

        [Fact]
        public void AnApostropheInsideALineCommentDoesNotDesynchronizeTheMask()
        {
            // The comment is masked before the quote state machine sees it, so a
            // stray apostrophe in prose cannot swallow the rest of the file.
            var source =
                "// don't let this apostrophe start a string literal\r\n" +
                Block("FB_A") +
                Block("FB_B");

            var declarations = SclSourceInspector.FindDeclarations(source);

            Assert.Equal(new[] { "FB_A", "FB_B" }, declarations.Select(item => item.Name).ToArray());
        }

        [Fact]
        public void AnApostropheInsideAQuotedIdentifierDoesNotDesynchronizeTheMask()
        {
            var source = Block("FB_Don't") + Block("FB_B");

            var declarations = SclSourceInspector.FindDeclarations(source);

            Assert.Equal(new[] { "FB_Don't", "FB_B" }, declarations.Select(item => item.Name).ToArray());
        }

        // ---------------------------------------------------------------
        // Ownership stamping
        // ---------------------------------------------------------------

        [Fact]
        public void RejectsANullSource()
        {
            Assert.Throws<ArgumentNullException>(
                () => SclSourceInspector.StampOwnershipFamily(null, Marker));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void RejectsABlankMarker(string marker)
        {
            Assert.Throws<ArgumentException>(
                () => SclSourceInspector.StampOwnershipFamily(Block("FB_A"), marker));
        }

        [Fact]
        public void InsertsTheFamilyHeaderAboveVersion()
        {
            var stamped = SclSourceInspector.StampOwnershipFamily(Block("FB_A"), Marker);

            Assert.Contains("FAMILY : 'TIASCL'\r\nVERSION : 0.1", stamped, StringComparison.Ordinal);
        }

        [Fact]
        public void ReplacesAnExistingFamilyValue()
        {
            var source = Block("FB_A", "FAMILY : 'OLD'\r\n");

            var stamped = SclSourceInspector.StampOwnershipFamily(source, Marker);

            Assert.DoesNotContain("'OLD'", stamped, StringComparison.Ordinal);
            Assert.Equal(1, Occurrences(stamped, "FAMILY"));
            Assert.Contains("FAMILY : 'TIASCL'", stamped, StringComparison.Ordinal);
        }

        [Fact]
        public void ReplacesAnUnquotedFamilyValue()
        {
            var source = Block("FB_A", "FAMILY : Legacy\r\n");

            var stamped = SclSourceInspector.StampOwnershipFamily(source, Marker);

            Assert.Contains("FAMILY : 'TIASCL'", stamped, StringComparison.Ordinal);
            Assert.DoesNotContain("Legacy", stamped, StringComparison.Ordinal);
        }

        [Fact]
        public void StampsEveryBlockInAMultiBlockSource()
        {
            var source = Block("FB_A") + Block("FB_B") + Block("FB_C");

            var stamped = SclSourceInspector.StampOwnershipFamily(source, Marker);

            Assert.Equal(3, Occurrences(stamped, "FAMILY : 'TIASCL'"));
        }

        [Fact]
        public void LeavesDataTypesAlone()
        {
            // PlcType has no ownership header in the V17 API, so stamping one
            // would produce a source TIA refuses to import.
            const string source = "TYPE \"UDT_A\"\r\nVERSION : 0.1\r\n   STRUCT\r\n   END_STRUCT;\r\nEND_TYPE\r\n";

            var stamped = SclSourceInspector.StampOwnershipFamily(source, Marker);

            Assert.Equal(source, stamped);
        }

        [Fact]
        public void StampsBlocksAndSkipsTypesInAMixedSource()
        {
            var source =
                "TYPE \"UDT_A\"\r\nVERSION : 0.1\r\n   STRUCT\r\n   END_STRUCT;\r\nEND_TYPE\r\n" +
                Block("FB_A");

            var stamped = SclSourceInspector.StampOwnershipFamily(source, Marker);

            Assert.Equal(1, Occurrences(stamped, "FAMILY"));
            Assert.Contains("TYPE \"UDT_A\"\r\nVERSION : 0.1", stamped, StringComparison.Ordinal);
        }

        /// <summary>
        /// Without a VERSION line there is no anchor the header can be attached
        /// to, and guessing a position would corrupt the declaration. Refusing
        /// keeps the import honest.
        /// </summary>
        [Fact]
        public void RefusesABlockWithNoVersionHeader()
        {
            const string source =
                "FUNCTION_BLOCK \"FB_A\"\r\n" +
                "VAR_INPUT\r\n   Open : Bool;\r\nEND_VAR\r\n" +
                "BEGIN\r\nEND_FUNCTION_BLOCK\r\n";

            var exception = Assert.Throws<InvalidOperationException>(
                () => SclSourceInspector.StampOwnershipFamily(source, Marker));

            Assert.Contains("FB_A", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void RefusesABlockWithTwoFamilyHeaders()
        {
            var source = Block("FB_A", "FAMILY : 'ONE'\r\nFAMILY : 'TWO'\r\n");

            Assert.Throws<InvalidOperationException>(
                () => SclSourceInspector.StampOwnershipFamily(source, Marker));
        }

        [Fact]
        public void IgnoresAFamilyHeaderThatBelongsToTheNextBlock()
        {
            var source = Block("FB_A") + Block("FB_B", "FAMILY : 'OTHER'\r\n");

            var stamped = SclSourceInspector.StampOwnershipFamily(source, Marker);

            Assert.Equal(2, Occurrences(stamped, "FAMILY : 'TIASCL'"));
            Assert.DoesNotContain("'OTHER'", stamped, StringComparison.Ordinal);
        }

        [Fact]
        public void IgnoresAFamilyWordThatOnlyAppearsInTheBody()
        {
            var source =
                "FUNCTION_BLOCK \"FB_A\"\r\n" +
                "VERSION : 0.1\r\n" +
                "BEGIN\r\n" +
                "   // FAMILY : 'not a header'\r\n" +
                "END_FUNCTION_BLOCK\r\n";

            var stamped = SclSourceInspector.StampOwnershipFamily(source, Marker);

            Assert.Contains("FAMILY : 'TIASCL'\r\nVERSION", stamped, StringComparison.Ordinal);
            Assert.Contains("// FAMILY : 'not a header'", stamped, StringComparison.Ordinal);
        }

        [Fact]
        public void PreservesTheIndentationOfTheVersionLine()
        {
            const string source =
                "FUNCTION_BLOCK \"FB_A\"\r\n" +
                "    VERSION : 0.1\r\n" +
                "BEGIN\r\nEND_FUNCTION_BLOCK\r\n";

            var stamped = SclSourceInspector.StampOwnershipFamily(source, Marker);

            Assert.Contains("    FAMILY : 'TIASCL'\r\n    VERSION : 0.1", stamped, StringComparison.Ordinal);
        }

        [Fact]
        public void UsesTheLineEndingTheSourceAlreadyUses()
        {
            const string source = "FUNCTION_BLOCK \"FB_A\"\nVERSION : 0.1\nBEGIN\nEND_FUNCTION_BLOCK\n";

            var stamped = SclSourceInspector.StampOwnershipFamily(source, Marker);

            Assert.Contains("FAMILY : 'TIASCL'\nVERSION", stamped, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", stamped, StringComparison.Ordinal);
        }

        /// <summary>
        /// Stamping edits the original text using offsets found in a
        /// comment-masked copy. If masking ever changed the length, every edit
        /// after the first comment would land in the wrong place and quietly
        /// mangle the source.
        /// </summary>
        [Fact]
        public void StampingIsCorrectEvenWhenCommentsPrecedeTheBlocks()
        {
            var source =
                "(* a block comment\r\n   spanning lines *)\r\n" +
                "// a line comment with a ' quote and a \" quote\r\n" +
                Block("FB_A") +
                "(* another *)\r\n" +
                Block("FB_B");

            var stamped = SclSourceInspector.StampOwnershipFamily(source, Marker);

            Assert.Equal(2, Occurrences(stamped, "FAMILY : 'TIASCL'"));
            Assert.Contains("FUNCTION_BLOCK \"FB_A\"\r\n", stamped, StringComparison.Ordinal);
            Assert.Contains("FUNCTION_BLOCK \"FB_B\"\r\n", stamped, StringComparison.Ordinal);
            Assert.Contains("(* a block comment", stamped, StringComparison.Ordinal);
            Assert.Contains("(* another *)", stamped, StringComparison.Ordinal);
        }

        [Fact]
        public void StampingIsIdempotent()
        {
            var once = SclSourceInspector.StampOwnershipFamily(Block("FB_A"), Marker);

            var twice = SclSourceInspector.StampOwnershipFamily(once, Marker);

            Assert.Equal(once, twice);
        }

        [Fact]
        public void StampingLeavesTheRestOfTheSourceByteForByteIdentical()
        {
            var source = Block("FB_A");

            var stamped = SclSourceInspector.StampOwnershipFamily(source, Marker);

            Assert.Equal(source, stamped.Replace("FAMILY : 'TIASCL'\r\n", string.Empty));
        }

        [Fact]
        public void StampingAnEmptySourceIsANoOp()
        {
            Assert.Equal(string.Empty, SclSourceInspector.StampOwnershipFamily(string.Empty, Marker));
        }

        [Fact]
        public void PreservesCyrillicCommentsAroundTheStamp()
        {
            var source = "// Клапан подачи\r\n" + Block("FB_A");

            var stamped = SclSourceInspector.StampOwnershipFamily(source, Marker);

            Assert.Contains("// Клапан подачи", stamped, StringComparison.Ordinal);
            Assert.Contains("FAMILY : 'TIASCL'", stamped, StringComparison.Ordinal);
        }

        private static int Occurrences(string text, string fragment)
        {
            var count = 0;
            var index = text.IndexOf(fragment, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal);
            }

            return count;
        }
    }
}
