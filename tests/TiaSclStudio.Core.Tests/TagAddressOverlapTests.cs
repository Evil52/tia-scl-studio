using System;
using System.Linq;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Core.Tests
{
    /// <summary>
    /// Two tags can each carry a perfectly valid address and still occupy the
    /// same bits of the process image. Writing one then silently changes the
    /// other, and nothing downstream notices: the SCL is well formed, TIA
    /// imports it, the PLC compiles it, and the plant misbehaves.
    ///
    /// Overlaying a word with its individual bits is idiomatic Siemens practice,
    /// so this is reported as a warning rather than an error. The point is that
    /// the engineer is told, not that they are stopped.
    /// </summary>
    public sealed class TagAddressOverlapTests
    {
        private const string OverlapCode = "TAG_ADDRESS_OVERLAP";

        private static ValidationResult Validate(params TagDefinition[] tags)
        {
            var project = ModelBuilder.Plant();
            project.CpuFamily = PlcCpuFamily.S71500;
            project.Tags.AddRange(tags);
            return new ProjectValidator().Validate(project);
        }

        private static TagDefinition Tag(string name, string dataType, string address)
        {
            return new TagDefinition { Name = name, DataType = dataType, Address = address };
        }

        private static bool HasOverlap(ValidationResult result)
        {
            return result.Issues.Any(issue => string.Equals(issue.Code, OverlapCode, StringComparison.Ordinal));
        }

        private static string Codes(ValidationResult result)
        {
            return string.Join(", ", result.Issues.Select(issue => issue.Code + "@" + issue.Path));
        }

        // ---------------------------------------------------------------
        // Overlaps that must be reported
        // ---------------------------------------------------------------

        [Fact]
        public void ReportsAWordOverlayingOneOfItsOwnBits()
        {
            // %MW10 covers bytes 10-11; %M10.0 is bit 0 of byte 10.
            var result = Validate(
                Tag("Status_Word", "Int", "%MW10"),
                Tag("Status_Bit", "Bool", "%M10.0"));

            Assert.True(HasOverlap(result), Codes(result));
        }

        [Fact]
        public void ReportsAWordOverlayingABitInItsSecondByte()
        {
            var result = Validate(
                Tag("Status_Word", "Int", "%MW10"),
                Tag("Status_Bit", "Bool", "%M11.7"));

            Assert.True(HasOverlap(result), Codes(result));
        }

        [Fact]
        public void ReportsTwoWordsThatShareAByte()
        {
            // %MW10 covers bytes 10-11 and %MW11 covers bytes 11-12.
            var result = Validate(
                Tag("First", "Int", "%MW10"),
                Tag("Second", "Int", "%MW11"));

            Assert.True(HasOverlap(result), Codes(result));
        }

        [Fact]
        public void ReportsADoubleWordOverlappingAWord()
        {
            // %MD20 covers bytes 20-23; %MW22 covers bytes 22-23.
            var result = Validate(
                Tag("Measurement", "Real", "%MD20"),
                Tag("Fraction", "Int", "%MW22"));

            Assert.True(HasOverlap(result), Codes(result));
        }

        [Fact]
        public void ReportsTheSameAddressUsedByTwoTags()
        {
            var result = Validate(
                Tag("Motor_Run", "Bool", "%M0.0"),
                Tag("Pump_Run", "Bool", "%M0.0"));

            Assert.True(HasOverlap(result), Codes(result));
        }

        /// <summary>
        /// The digital and analog input operands address the same process image.
        /// %IW0 covers input bytes 0-1, so it contains %I0.0.
        /// </summary>
        [Fact]
        public void ReportsAnAnalogInputOverlayingADigitalInput()
        {
            var result = Validate(
                Tag("Analog_In", "Int", "%IW0"),
                Tag("Digital_In", "Bool", "%I1.3"));

            Assert.True(HasOverlap(result), Codes(result));
        }

        [Fact]
        public void ReportsAnAnalogOutputOverlayingADigitalOutput()
        {
            var result = Validate(
                Tag("Analog_Out", "Int", "%QW4"),
                Tag("Digital_Out", "Bool", "%Q5.0"));

            Assert.True(HasOverlap(result), Codes(result));
        }

        [Fact]
        public void NamesBothTagsSoTheEngineerCanFindThem()
        {
            var result = Validate(
                Tag("Status_Word", "Int", "%MW10"),
                Tag("Status_Bit", "Bool", "%M10.0"));

            var issue = result.Issues.Single(item => item.Code == OverlapCode);
            Assert.Contains("Status_Word", issue.Message, StringComparison.Ordinal);
            Assert.Contains("Status_Bit", issue.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ReportsEachOverlappingPairOnce()
        {
            var result = Validate(
                Tag("A", "Int", "%MW10"),
                Tag("B", "Bool", "%M10.0"),
                Tag("C", "Bool", "%M10.1"));

            // A/B and A/C overlap; B and C do not.
            Assert.Equal(2, result.Issues.Count(issue => issue.Code == OverlapCode));
        }

        // ---------------------------------------------------------------
        // Layouts that are correct and must stay silent
        // ---------------------------------------------------------------

        [Fact]
        public void TwoBitsInTheSameByteDoNotOverlap()
        {
            var result = Validate(
                Tag("Bit_Zero", "Bool", "%M0.0"),
                Tag("Bit_One", "Bool", "%M0.1"));

            Assert.False(HasOverlap(result), Codes(result));
        }

        [Fact]
        public void AdjacentWordsDoNotOverlap()
        {
            var result = Validate(
                Tag("First", "Int", "%MW10"),
                Tag("Second", "Int", "%MW12"));

            Assert.False(HasOverlap(result), Codes(result));
        }

        [Fact]
        public void AdjacentDoubleWordsDoNotOverlap()
        {
            var result = Validate(
                Tag("First", "Real", "%MD20"),
                Tag("Second", "Real", "%MD24"));

            Assert.False(HasOverlap(result), Codes(result));
        }

        [Fact]
        public void TheSameOffsetInDifferentAreasDoesNotOverlap()
        {
            // Inputs, outputs and markers are separate address spaces.
            var result = Validate(
                Tag("In_Bit", "Bool", "%I0.0"),
                Tag("Out_Bit", "Bool", "%Q0.0"),
                Tag("Marker_Bit", "Bool", "%M0.0"));

            Assert.False(HasOverlap(result), Codes(result));
        }

        [Fact]
        public void AnInputWordAndAnOutputWordAtTheSameOffsetDoNotOverlap()
        {
            var result = Validate(
                Tag("In_Word", "Int", "%IW64"),
                Tag("Out_Word", "Int", "%QW64"));

            Assert.False(HasOverlap(result), Codes(result));
        }

        [Fact]
        public void TheReferenceProjectHasNoOverlaps()
        {
            var project = DemoProjects.Valve().Plant;
            project.CpuFamily = PlcCpuFamily.S71500;

            Assert.False(HasOverlap(new ProjectValidator().Validate(project)));
        }

        // ---------------------------------------------------------------
        // Interaction with the rest of validation
        // ---------------------------------------------------------------

        [Fact]
        public void AnOverlapIsAWarningAndDoesNotBlockGeneration()
        {
            // Overlaying a status word with its bits is normal Siemens practice.
            // Refusing to generate would make the tool unusable for it.
            var result = Validate(
                Tag("Status_Word", "Int", "%MW10"),
                Tag("Status_Bit", "Bool", "%M10.0"));

            Assert.True(HasOverlap(result));
            Assert.True(result.IsValid, Codes(result));
        }

        [Fact]
        public void OverlapsAreReportedEvenBeforeACpuFamilyIsChosen()
        {
            // The CPU family only caps the highest usable offset; the byte
            // layout of an operand does not depend on it. So a freshly imported
            // project still gets the warning rather than having to wait until
            // someone picks a CPU.
            var project = ModelBuilder.Plant();
            project.CpuFamily = PlcCpuFamily.Unknown;
            project.Tags.Add(Tag("Status_Word", "Int", "%MW10"));
            project.Tags.Add(Tag("Status_Bit", "Bool", "%M10.0"));

            Assert.True(HasOverlap(new ProjectValidator().Validate(project)));
        }

        [Fact]
        public void AnUnparseableAddressDoesNotProduceAnOverlapWarningAsWell()
        {
            // One clear error beats an error plus a speculative warning.
            var result = Validate(
                Tag("Broken", "Bool", "not an address"),
                Tag("Good", "Bool", "%M0.0"));

            Assert.Contains(result.Issues, issue => issue.Code == "TAG_ADDRESS_INVALID");
            Assert.False(HasOverlap(result), Codes(result));
        }

        [Fact]
        public void OverlapDetectionScalesToARealisticTagTable()
        {
            var project = ModelBuilder.Plant();
            project.CpuFamily = PlcCpuFamily.S71500;
            for (var index = 0; index < 2000; index++)
            {
                project.Tags.Add(Tag(
                    "Tag_" + index,
                    "Bool",
                    "%M" + (index / 8) + "." + (index % 8)));
            }

            var result = new ProjectValidator().Validate(project);

            Assert.False(HasOverlap(result), "A densely but correctly packed tag table was flagged.");
        }
    }
}
