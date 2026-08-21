using System;
using System.Globalization;
using System.Threading;
using TiaSclStudio.Core.Validation;
using Xunit;

namespace TiaSclStudio.Core.Tests
{
    /// <summary>
    /// SclName is the single gate between user input and identifiers that are
    /// emitted into SCL without local quoting. Everything that gets past it
    /// ends up verbatim in a file that TIA Portal compiles, so the interesting
    /// cases are the ones that look like a name but are not.
    /// </summary>
    public sealed class SclNameTests
    {
        [Theory]
        [InlineData("Valve")]
        [InlineData("_private")]
        [InlineData("FB_Valve_01")]
        [InlineData("a")]
        [InlineData("_")]
        [InlineData("X9")]
        public void AcceptsPlainAsciiIdentifiers(string name)
        {
            string error;
            Assert.True(SclName.TryValidate(name, out error), name + " -> " + error);
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        public void RejectsNullWithoutThrowing()
        {
            string error;
            Assert.False(SclName.TryValidate(null, out error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("\r\n")]
        [InlineData(" ")]
        public void RejectsBlankNames(string name)
        {
            string error;
            Assert.False(SclName.TryValidate(name, out error));
        }

        [Theory]
        [InlineData(" Valve")]
        [InlineData("Valve ")]
        [InlineData("\tValve")]
        [InlineData("Valve\n")]
        public void RejectsSurroundingWhitespaceInsteadOfSilentlyTrimming(string name)
        {
            // Trimming here would let two different stored names collapse onto
            // one emitted symbol, so the caller has to fix the value instead.
            string error;
            Assert.False(SclName.TryValidate(name, out error));
        }

        /// <summary>
        /// In .NET, "$" also matches immediately before a trailing newline. If the
        /// leading/trailing whitespace guard were ever dropped, "Valve\n" would
        /// satisfy the identifier pattern and a newline would be emitted straight
        /// into an SCL declaration. This pins that hole shut.
        /// </summary>
        [Fact]
        public void RejectsTrailingNewlineThatTheIdentifierRegexAloneWouldAccept()
        {
            string error;
            Assert.False(SclName.TryValidate("Valve\n", out error));
            Assert.False(SclName.TryValidate("Valve\r\n", out error));
        }

        [Theory]
        [InlineData("Valve\nOpen")]
        [InlineData("Valve\rOpen")]
        [InlineData("Valve Open")]
        [InlineData("Valve;DROP")]
        [InlineData("Valve\"Quote")]
        [InlineData("Valve'Quote")]
        [InlineData("Valve//comment")]
        [InlineData("Valve(*comment*)")]
        [InlineData("Valve:=1")]
        public void RejectsEmbeddedSclSyntax(string name)
        {
            string error;
            Assert.False(SclName.TryValidate(name, out error));
        }

        [Fact]
        public void RejectsEmbeddedNulCharacter()
        {
            string error;
            Assert.False(SclName.TryValidate("Valve\0Open", out error));
        }

        [Theory]
        [InlineData("Клапан")]
        [InlineData("Ventil_Öffnen")]
        [InlineData("Valve²")]
        [InlineData("Ｖalve")]
        public void RejectsNonAsciiIdentifiers(string name)
        {
            // TIA accepts them only inside quoted globals; the generator emits
            // interface members unquoted, so non-ASCII must not get this far.
            string error;
            Assert.False(SclName.TryValidate(name, out error));
        }

        [Theory]
        [InlineData("1Valve")]
        [InlineData("9")]
        public void RejectsLeadingDigit(string name)
        {
            string error;
            Assert.False(SclName.TryValidate(name, out error));
        }

        [Fact]
        public void AcceptsExactlyTheMaximumLength()
        {
            var name = "A" + new string('x', SclName.MaximumLength - 1);
            Assert.Equal(SclName.MaximumLength, name.Length);

            string error;
            Assert.True(SclName.TryValidate(name, out error), error);
        }

        [Fact]
        public void RejectsOneCharacterOverTheMaximumLength()
        {
            var name = "A" + new string('x', SclName.MaximumLength);

            string error;
            Assert.False(SclName.TryValidate(name, out error));
            Assert.Contains(SclName.MaximumLength.ToString(CultureInfo.InvariantCulture), error);
        }

        [Theory]
        [InlineData("IF")]
        [InlineData("if")]
        [InlineData("If")]
        [InlineData("END_FUNCTION_BLOCK")]
        [InlineData("bool")]
        [InlineData("Time")]
        [InlineData("VAR_IN_OUT")]
        [InlineData("XOR")]
        public void RejectsReservedWordsRegardlessOfCasing(string name)
        {
            string error;
            Assert.False(SclName.TryValidate(name, out error));
            Assert.Contains("reserved", error, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("IFX")]
        [InlineData("MyIf")]
        [InlineData("BOOLEAN")]
        [InlineData("_IF")]
        public void AcceptsNamesThatMerelyContainAReservedWord(string name)
        {
            string error;
            Assert.True(SclName.TryValidate(name, out error), error);
        }

        /// <summary>
        /// Turkish casing maps "I" to a dotless lower-case letter. A reserved-word
        /// lookup that used the current culture would let "IF" through on a
        /// Turkish machine and produce SCL that only fails on the customer's PC.
        /// </summary>
        [Fact]
        public void ReservedWordLookupIsNotAffectedByTheTurkishCasingRule()
        {
            RunInCulture("tr-TR", () =>
            {
                string error;
                Assert.False(SclName.TryValidate("IF", out error));
                Assert.False(SclName.TryValidate("if", out error));
                Assert.False(SclName.TryValidate("INT", out error));
                Assert.True(SclName.TryValidate("Iron", out error), error);
            });
        }

        [Fact]
        public void IdentifierPatternIsNotAffectedByTheCurrentCulture()
        {
            RunInCulture("tr-TR", () =>
            {
                string error;
                Assert.True(SclName.TryValidate("Ignition", out error), error);
                Assert.False(SclName.TryValidate("ıgnition", out error));
            });
        }

        [Fact]
        public void IsValidAgreesWithTryValidate()
        {
            var samples = new[]
            {
                null, string.Empty, " ", "Valve", "IF", "1Valve", "Клапан",
                "Valve\n", "Valve Open", new string('x', SclName.MaximumLength + 1)
            };

            foreach (var sample in samples)
            {
                string error;
                Assert.Equal(SclName.TryValidate(sample, out error), SclName.IsValid(sample));
            }
        }

        private static void RunInCulture(string cultureName, Action action)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            var previousCulture = Thread.CurrentThread.CurrentCulture;
            var previousUiCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                action();
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previousCulture;
                Thread.CurrentThread.CurrentUICulture = previousUiCulture;
            }
        }
    }
}
