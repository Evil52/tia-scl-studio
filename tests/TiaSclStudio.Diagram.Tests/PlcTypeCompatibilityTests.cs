using TiaSclStudio.Diagram.Validation;
using Xunit;

namespace TiaSclStudio.Diagram.Tests
{
    /// <summary>
    /// This comparison decides whether a wire is legal. It is deliberately
    /// exact rather than convertible, so the interesting question is which
    /// spelling differences it forgives and which it must not.
    /// </summary>
    public sealed class PlcTypeCompatibilityTests
    {
        [Theory]
        [InlineData("Bool", "Bool")]
        [InlineData("Bool", "bool")]
        [InlineData("BOOL", "BooL")]
        [InlineData(" Int ", "Int")]
        [InlineData("Array[0..9] of Bool", "Array[0..9]ofBool")]
        [InlineData("Array [0 .. 9] of Bool", "Array[0..9] of Bool")]
        [InlineData("\"UDT_Valve\"", "\"UDT_Valve\"")]
        public void TreatsSpellingVariantsOfTheSameTypeAsCompatible(string left, string right)
        {
            Assert.True(PlcTypeCompatibility.AreExactlyCompatible(left, right));
            Assert.True(PlcTypeCompatibility.AreExactlyCompatible(right, left));
        }

        [Theory]
        [InlineData("Bool", "Int")]
        [InlineData("Int", "DInt")]
        [InlineData("Int", "Real")]
        [InlineData("Word", "Int")]
        [InlineData("Time", "DInt")]
        [InlineData("Array[0..9] of Bool", "Array[0..8] of Bool")]
        [InlineData("\"UDT_A\"", "\"UDT_B\"")]
        [InlineData("Bool", "")]
        [InlineData("Bool", null)]
        public void RefusesTypesThatWouldNeedAConversion(string left, string right)
        {
            // No implicit widening: the editor must not hide a conversion that
            // the PLC would perform differently from the engineer's intent.
            Assert.False(PlcTypeCompatibility.AreExactlyCompatible(left, right));
            Assert.False(PlcTypeCompatibility.AreExactlyCompatible(right, left));
        }

        /// <summary>
        /// Two pins that both lost their data type compare as compatible. That
        /// is only safe because every pin that reaches this check has already
        /// been matched against a validated interface member, and Core rejects
        /// an empty data type. This test states the coupling out loud.
        /// </summary>
        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("   ", null)]
        public void TwoUntypedPinsCompareAsCompatible(string left, string right)
        {
            Assert.True(PlcTypeCompatibility.AreExactlyCompatible(left, right));
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        [InlineData("\t\r\n", "")]
        [InlineData("Bool", "Bool")]
        [InlineData("  Array [0..9]  of  Bool  ", "Array[0..9]ofBool")]
        public void NormalizeStripsEveryWhitespaceCharacter(string input, string expected)
        {
            Assert.Equal(expected, PlcTypeCompatibility.Normalize(input));
        }

        [Fact]
        public void NormalizeIsIdempotent()
        {
            const string input = " Array [0 .. 9] of \t Bool ";
            var once = PlcTypeCompatibility.Normalize(input);

            Assert.Equal(once, PlcTypeCompatibility.Normalize(once));
        }
    }
}
