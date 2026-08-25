using System;
using System.Linq;
using TiaSclStudio.Core.Importing;
using TiaSclStudio.Core.Model;
using Xunit;

namespace TiaSclStudio.Core.Tests
{
    /// <summary>
    /// Import is deliberately declaration-only: it may learn a block's pins,
    /// but it must never reinterpret executable SCL and then claim to preserve
    /// the original implementation. These tests exercise the lexical borders
    /// which keep the interface and the body separate.
    /// </summary>
    public sealed class SclLibrarySourceParserTests
    {
        [Fact]
        public void ImportsEverySupportedBlockSectionWithoutCopyingTheBody()
        {
            var result = Parse(
                "// Valve contract",
                "FUNCTION_BLOCK \"FB_Valve\"",
                "{ S7_Optimized_Access := 'FALSE' }",
                "VERSION : 1.2",
                "VAR_INPUT",
                "   Open, Reset : Bool := FALSE; // commands",
                "   Message : String[20] := 'text; // still a string';",
                "END_VAR",
                "VAR_OUTPUT",
                "   State : \"ValveState\";",
                "END_VAR",
                "VAR_IN_OUT",
                "   Context : DWord;",
                "END_VAR",
                "VAR",
                "   Memory : Int;",
                "END_VAR",
                "VAR_TEMP",
                "   Scratch : Real;",
                "END_VAR",
                "VAR CONSTANT",
                "   Maximum : Int := 10;",
                "END_VAR",
                "BEGIN",
                "   // A declaration-looking statement in the body is not an interface member.",
                "   BodyOnly := 'FUNCTION_BLOCK Fake; END_FUNCTION_BLOCK';",
                "END_FUNCTION_BLOCK");

            Assert.False(result.HasErrors, Codes(result));
            var item = Assert.Single(result.Items);
            Assert.Equal(SclImportedObjectKind.FunctionBlock, item.Kind);
            Assert.Null(item.DataType);

            var block = item.Block;
            Assert.NotNull(block);
            Assert.Equal("FB_Valve", block.Name);
            Assert.Equal(BlockKind.FunctionBlock, block.Kind);
            Assert.Equal("Void", block.ReturnType);
            Assert.Equal("1.2", block.Version);
            Assert.False(block.OptimizedAccess);
            Assert.True(block.ImportedInterfaceOnly);
            Assert.Equal(string.Empty, block.SclBody);
            Assert.Contains("Valve contract", block.Description, StringComparison.Ordinal);
            Assert.Equal(8, block.Interface.Count);

            AssertMember(block, "Open", "Bool", InterfaceSection.Input, "FALSE");
            AssertMember(block, "Reset", "Bool", InterfaceSection.Input, "FALSE");
            AssertMember(
                block,
                "Message",
                "String[20]",
                InterfaceSection.Input,
                "'text; // still a string'");
            AssertMember(block, "State", "\"ValveState\"", InterfaceSection.Output, string.Empty);
            AssertMember(block, "Context", "DWord", InterfaceSection.InOut, string.Empty);
            AssertMember(block, "Memory", "Int", InterfaceSection.Static, string.Empty);
            AssertMember(block, "Scratch", "Real", InterfaceSection.Temp, string.Empty);
            AssertMember(block, "Maximum", "Int", InterfaceSection.Constant, "10");
            Assert.Equal("commands", block.Interface.Single(member => member.Name == "Open").Comment);
            Assert.Equal("commands", block.Interface.Single(member => member.Name == "Reset").Comment);
            Assert.DoesNotContain(block.Interface, member => member.Name == "BodyOnly");
        }

        [Fact]
        public void ImportsAFunctionReturnTypeAndQuotedMemberNames()
        {
            var result = Parse(
                "FUNCTION \"FC_Add\" : DInt",
                "VERSION : 2.0",
                "VAR_INPUT",
                "   \"Left\", \"Right\" : DInt;",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION");

            Assert.False(result.HasErrors, Codes(result));
            var block = Assert.Single(result.Items).Block;
            Assert.Equal(BlockKind.Function, block.Kind);
            Assert.Equal("DInt", block.ReturnType);
            Assert.Equal(new[] { "Left", "Right" }, block.Interface.Select(item => item.Name).ToArray());
        }

        [Fact]
        public void ImportsTiaMemberAttributesWithoutTreatingThemAsPartOfTheName()
        {
            var result = Parse(
                "FUNCTION_BLOCK Unit",
                "VAR_OUTPUT",
                "   Feedback { ExternalAccessible := 'False'; ExternalVisible := 'False';",
                "      ExternalWritable := 'False' } : Bool;",
                "   ExtFault { ExternalAccessible := 'True' } : Bool;",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            Assert.False(result.HasErrors, Codes(result));
            var block = Assert.Single(result.Items).Block;
            Assert.Equal(2, block.Interface.Count);
            AssertMember(block, "Feedback", "Bool", InterfaceSection.Output, string.Empty);
            AssertMember(block, "ExtFault", "Bool", InterfaceSection.Output, string.Empty);
        }

        [Fact]
        public void ImportsRetainSectionsAndSilentlySkipsNonInterfaceTiaMetadata()
        {
            var result = Parse(
                "FUNCTION_BLOCK Unit",
                "VAR_INPUT RETAIN",
                "   Enable : Bool;",
                "   tModeBits AT tInMode : Array[0..7] of Bool;",
                "END_VAR",
                "VAR RETAIN",
                "   State : Int;",
                "END_VAR",
                "VAR CONSTANT",
                "   UnitState.SwitchingOn : Int := 1;",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            Assert.False(result.HasErrors, Codes(result));
            var block = Assert.Single(result.Items).Block;
            AssertMember(block, "Enable", "Bool", InterfaceSection.Input, string.Empty);
            AssertMember(block, "State", "Int", InterfaceSection.Static, string.Empty);
            Assert.DoesNotContain(block.Interface, member => member.Name == "tModeBits");
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void ImportsDbSpecificOutputAsARegularOutputPin()
        {
            var result = Parse(
                "FUNCTION_BLOCK ModeSelector",
                "VAR_OUTPUT DB_SPECIFIC",
                "   Mode : Int;",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            Assert.False(result.HasErrors, Codes(result));
            var block = Assert.Single(result.Items).Block;
            AssertMember(block, "Mode", "Int", InterfaceSection.Output, string.Empty);
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void ImportsAUserDataTypeWithArraysInitialValuesAndComments()
        {
            var result = Parse(
                "// Process payload",
                "TYPE \"ProcessData\"",
                "VERSION : 3.4",
                "STRUCT",
                "   // sampled values",
                "   Values : Array[-1..2] of Real; // engineering units",
                "   Title : String[30] := 'TYPE Fake; END_TYPE';",
                "END_STRUCT;",
                "END_TYPE");

            Assert.False(result.HasErrors, Codes(result));
            var item = Assert.Single(result.Items);
            Assert.Equal(SclImportedObjectKind.UserDataType, item.Kind);
            Assert.Null(item.Block);

            var udt = item.DataType;
            Assert.Equal("ProcessData", udt.Name);
            Assert.Equal("3.4", udt.Version);
            Assert.Contains("Process payload", udt.Comment, StringComparison.Ordinal);
            Assert.Equal(2, udt.Members.Count);
            Assert.Equal("Array[-1..2] of Real", udt.Members[0].DataType);
            Assert.Contains("sampled values", udt.Members[0].Comment, StringComparison.Ordinal);
            Assert.Contains("engineering units", udt.Members[0].Comment, StringComparison.Ordinal);
            Assert.Equal("'TYPE Fake; END_TYPE'", udt.Members[1].InitialValue);
        }

        [Fact]
        public void ImportsSeveralTopLevelObjectsAndIgnoresFakeHeadersInComments()
        {
            var result = Parse(
                "(* FUNCTION_BLOCK Fake",
                "   END_FUNCTION_BLOCK *)",
                "TYPE TypeA",
                "STRUCT",
                "   Value : Bool;",
                "END_STRUCT;",
                "END_TYPE",
                "// FUNCTION Fake : Void",
                "FUNCTION_BLOCK FB_A",
                "BEGIN",
                "END_FUNCTION_BLOCK",
                "FUNCTION FC_A : Void",
                "BEGIN",
                "   Text := 'END_FUNCTION_BLOCK';",
                "END_FUNCTION");

            Assert.False(result.HasErrors, Codes(result));
            Assert.Equal(
                new[] { "TypeA", "FB_A", "FC_A" },
                result.Items.Select(item => item.Name).ToArray());
        }

        [Fact]
        public void RepeatingTheSameImportProducesStableObjectAndMemberIds()
        {
            var source = Lines(
                "FUNCTION_BLOCK FB_Stable",
                "VAR_INPUT",
                "   Enable : Bool;",
                "END_VAR",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            var first = new SclLibrarySourceParser().Parse(source);
            var second = new SclLibrarySourceParser().Parse(source);

            Assert.False(first.HasErrors, Codes(first));
            Assert.False(second.HasErrors, Codes(second));
            Assert.Equal(first.Items[0].Block.Id, second.Items[0].Block.Id);
            Assert.Equal(
                first.Items[0].Block.Interface[0].Id,
                second.Items[0].Block.Interface[0].Id);
        }

        [Theory]
        [InlineData(
            "FUNCTION_BLOCK FB_A\nVAR_INPUT\nValue : Bool;\nBEGIN\nEND_FUNCTION_BLOCK",
            "SCL_IMPORT_END_VAR_MISSING")]
        [InlineData(
            "FUNCTION_BLOCK FB_A\nVAR_INPUT\nValue : Bool\nEND_VAR\nBEGIN\nEND_FUNCTION_BLOCK",
            "SCL_IMPORT_SEMICOLON_MISSING")]
        [InlineData(
            "FUNCTION_BLOCK FB_A\nBEGIN",
            "SCL_IMPORT_END_MISSING")]
        [InlineData(
            "FUNCTION_BLOCK 9Bad\nBEGIN\nEND_FUNCTION_BLOCK",
            "SCL_IMPORT_NAME_INVALID")]
        [InlineData(
            "TYPE T\nSTRUCT\nNested : STRUCT\nValue : Bool;\nEND_STRUCT;\nEND_STRUCT;\nEND_TYPE",
            "SCL_IMPORT_ANONYMOUS_STRUCT_UNSUPPORTED")]
        [InlineData(
            "FUNCTION_BLOCK FB_A\nVAR_EXTERNAL\nValue : Bool;\nEND_VAR\nBEGIN\nEND_FUNCTION_BLOCK",
            "SCL_IMPORT_SECTION_UNSUPPORTED")]
        [InlineData(
            "TYPE T : STRUCT\nValue : Bool;\nEND_STRUCT;\nEND_TYPE",
            "SCL_IMPORT_INLINE_STRUCT_UNSUPPORTED")]
        public void MalformedOrUnsupportedDeclarationsProduceAnExplicitDiagnostic(
            string source,
            string expectedCode)
        {
            var result = new SclLibrarySourceParser().Parse(source);

            Assert.True(result.HasErrors, Codes(result));
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
        }

        [Theory]
        [InlineData("FUNCTION FC_MissingReturn\nBEGIN\nEND_FUNCTION")]
        [InlineData("FUNCTION_BLOCK FB_A\nVAR_INPUT\nValue : String := 'unterminated;\nEND_VAR\nBEGIN\nEND_FUNCTION_BLOCK")]
        [InlineData("FUNCTION_BLOCK FB_A\nVAR_INPUT\nValue : \"Unterminated;\nEND_VAR\nBEGIN\nEND_FUNCTION_BLOCK")]
        [InlineData("FUNCTION_BLOCK FB_A\nVAR_INPUT\nValue : Array[0..1 of Bool;\nEND_VAR\nBEGIN\nEND_FUNCTION_BLOCK")]
        [InlineData("FUNCTION_BLOCK FB_A\n(* never closed")]
        public void AmbiguousLexicalInputFailsClosed(string source)
        {
            var result = new SclLibrarySourceParser().Parse(source);

            Assert.True(result.HasErrors, Codes(result));
        }

        [Fact]
        public void DuplicateGlobalNamesAreRejectedCaseInsensitivelyAcrossKinds()
        {
            var result = Parse(
                "TYPE Shared",
                "STRUCT",
                "   Value : Bool;",
                "END_STRUCT;",
                "END_TYPE",
                "FUNCTION_BLOCK shared",
                "BEGIN",
                "END_FUNCTION_BLOCK");

            Assert.True(result.HasErrors, Codes(result));
            Assert.Equal(
                2,
                result.Diagnostics.Count(item => item.Code == "SCL_IMPORT_DUPLICATE_OBJECT"));
        }

        [Fact]
        public void ASourceWithoutImportableObjectsIsRejected()
        {
            var result = Parse("DATA_BLOCK DB_A", "BEGIN", "END_DATA_BLOCK");

            Assert.True(result.HasErrors, Codes(result));
            Assert.Contains(result.Diagnostics, item => item.Code == "SCL_IMPORT_NO_OBJECTS");
        }

        [Fact]
        public void ASourceOverTheSafetyLimitIsRejectedBeforeParsing()
        {
            var source = new string(' ', SclLibrarySourceParser.MaximumSourceLength + 1);

            var result = new SclLibrarySourceParser().Parse(source);

            Assert.True(result.HasErrors, Codes(result));
            Assert.Empty(result.Items);
            Assert.Contains(result.Diagnostics, item => item.Code == "SCL_IMPORT_SOURCE_TOO_LARGE");
        }

        [Fact]
        public void NullSourceIsAProgrammerError()
        {
            Assert.Throws<ArgumentNullException>(() => new SclLibrarySourceParser().Parse(null));
        }

        private static SclLibraryImportResult Parse(params string[] lines)
        {
            return new SclLibrarySourceParser().Parse(Lines(lines));
        }

        private static string Lines(params string[] lines)
        {
            return string.Join("\r\n", lines);
        }

        private static void AssertMember(
            BlockDefinition block,
            string name,
            string dataType,
            InterfaceSection section,
            string initialValue)
        {
            var member = block.Interface.Single(item => item.Name == name);
            Assert.Equal(dataType, member.DataType);
            Assert.Equal(section, member.Section);
            Assert.Equal(initialValue, member.InitialValue);
        }

        private static string Codes(SclLibraryImportResult result)
        {
            return string.Join(", ", result.Diagnostics.Select(item => item.Code));
        }
    }
}
