using System;
using System.Reflection;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using Xunit;

namespace TiaSclStudio.Core.Tests
{
    public sealed class PlcAddressingTests
    {
        [Theory]
        [InlineData(PlcSignalKind.DigitalInput, "Bool", 0, 0, "%I0.0")]
        [InlineData(PlcSignalKind.DigitalOutput, "Bool", 12, 7, "%Q12.7")]
        [InlineData(PlcSignalKind.AnalogInput, "Int", 64, null, "%IW64")]
        [InlineData(PlcSignalKind.AnalogInput, "Word", 64, null, "%IW64")]
        [InlineData(PlcSignalKind.AnalogOutput, "Real", 128, null, "%QD128")]
        [InlineData(PlcSignalKind.Memory, "Bool", 3, 2, "%M3.2")]
        [InlineData(PlcSignalKind.Memory, "Word", 10, null, "%MW10")]
        [InlineData(PlcSignalKind.Memory, "Real", 20, null, "%MD20")]
        public void BuildsCanonicalDirectAddress(
            PlcSignalKind kind,
            string dataType,
            int byteOffset,
            int? bitOffset,
            string expected)
        {
            string address;
            string error;

            Assert.True(PlcAddressing.TryBuild(
                PlcCpuFamily.S71200,
                kind,
                dataType,
                byteOffset,
                bitOffset,
                out address,
                out error), error);
            Assert.Equal(expected, address);
        }

        [Fact]
        public void DataTypesDependOnSignalKind()
        {
            Assert.Equal(new[] { "Bool" }, PlcAddressing.GetAllowedDataTypes(PlcSignalKind.DigitalInput));
            Assert.Equal(
                new[] { "Int", "Word", "Real" },
                PlcAddressing.GetAllowedDataTypes(PlcSignalKind.AnalogOutput));
            Assert.Equal(
                new[] { "Bool", "Int", "Word", "Real" },
                PlcAddressing.GetAllowedDataTypes(PlcSignalKind.Memory));
        }

        [Theory]
        [InlineData(PlcCpuFamily.S71200, PlcSignalKind.DigitalInput, "Bool", 1023, 7, true)]
        [InlineData(PlcCpuFamily.S71200, PlcSignalKind.DigitalInput, "Bool", 1024, 0, false)]
        [InlineData(PlcCpuFamily.S71200, PlcSignalKind.AnalogInput, "Word", 1022, null, true)]
        [InlineData(PlcCpuFamily.S71200, PlcSignalKind.AnalogInput, "Word", 1023, null, false)]
        [InlineData(PlcCpuFamily.S71200, PlcSignalKind.Memory, "Real", 8188, null, true)]
        [InlineData(PlcCpuFamily.S71200, PlcSignalKind.Memory, "Real", 8189, null, false)]
        [InlineData(PlcCpuFamily.S71500, PlcSignalKind.AnalogOutput, "Real", 32764, null, true)]
        [InlineData(PlcCpuFamily.S71500, PlcSignalKind.AnalogOutput, "Real", 32765, null, false)]
        public void EnforcesCpuRangeAndOperandWidth(
            PlcCpuFamily cpu,
            PlcSignalKind kind,
            string dataType,
            int byteOffset,
            int? bitOffset,
            bool expected)
        {
            string address;
            string error;

            Assert.Equal(expected, PlcAddressing.TryBuild(
                cpu,
                kind,
                dataType,
                byteOffset,
                bitOffset,
                out address,
                out error));
        }

        [Theory]
        [InlineData("i0.0", "Bool", "%I0.0")]
        [InlineData("%q7.6", "Bool", "%Q7.6")]
        [InlineData("iw64", "Int", "%IW64")]
        [InlineData("%QD128", "Real", "%QD128")]
        [InlineData("md20", "Real", "%MD20")]
        [InlineData("md24", "DInt", "%MD24")]
        public void ParsesCaseInsensitiveInputAndAddsPercentPrefix(
            string input,
            string dataType,
            string expected)
        {
            PlcAddressSpec spec;
            string address;
            string error;

            Assert.True(PlcAddressing.TryParse(
                PlcCpuFamily.S71500,
                input,
                dataType,
                out spec,
                out address,
                out error), error);
            Assert.Equal(expected, address);
            Assert.NotNull(spec);
        }

        [Theory]
        [InlineData("i.0", "Bool")]
        [InlineData("%I0", "Bool")]
        [InlineData("%I0.8", "Bool")]
        [InlineData("%IW64.0", "Word")]
        [InlineData("%IW64", "Real")]
        [InlineData("%ID64", "Int")]
        [InlineData("%QW1023", "Word")]
        public void RejectsMalformedOrIncompatibleAddress(string input, string dataType)
        {
            PlcAddressSpec spec;
            string address;
            string error;

            Assert.False(PlcAddressing.TryParse(
                PlcCpuFamily.S71200,
                input,
                dataType,
                out spec,
                out address,
                out error));
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Fact]
        public void UnknownLegacyProfileChecksSyntaxWithoutApplyingAConcreteCpuRange()
        {
            PlcAddressSpec spec;
            string address;
            string error;

            Assert.True(PlcAddressing.TryParse(
                PlcCpuFamily.Unknown,
                "%IW50000",
                "Word",
                out spec,
                out address,
                out error), error);
            Assert.Equal("%IW50000", address);
        }

        [Fact]
        public void NewAddressBuilderRequiresAConcreteCpuFamily()
        {
            string address;
            string error;

            Assert.False(PlcAddressing.TryBuild(
                PlcCpuFamily.Unknown,
                PlcSignalKind.DigitalInput,
                "Bool",
                0,
                0,
                out address,
                out error));
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Fact]
        public void ProjectValidationUsesThePersistedConcreteCpuRange()
        {
            var project = new PlantProject
            {
                Name = "CpuRange",
                CpuFamily = PlcCpuFamily.S71200
            };
            project.Tags.Add(new TagDefinition
            {
                Name = "FarInput",
                DataType = "Word",
                Address = "%IW2000"
            });

            var result = new ProjectValidator().Validate(project);

            Assert.Contains(result.Errors, issue => issue.Code == "TAG_ADDRESS_INVALID");
        }

        [Fact]
        public void AddressRangesAndOverlapGuardsCoverInvalidInputs()
        {
            var input = new PlcAddressSpec(
                PlcCpuFamily.S71200,
                PlcSignalKind.DigitalInput,
                "Bool",
                0,
                0);
            var sameBit = new PlcAddressSpec(
                PlcCpuFamily.S71200,
                PlcSignalKind.DigitalInput,
                "Bool",
                0,
                0);
            var output = new PlcAddressSpec(
                PlcCpuFamily.S71200,
                PlcSignalKind.DigitalOutput,
                "Bool",
                0,
                0);

            Assert.True(input.Overlaps(sameBit));
            Assert.False(input.Overlaps(output));
            Assert.Equal("other", Assert.Throws<ArgumentNullException>(() => input.Overlaps(null)).ParamName);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PlcAddressing.GetAllowedDataTypes((PlcSignalKind)999));
            Assert.Throws<ArgumentException>(() =>
                PlcAddressing.GetMaximumByteOffset(
                    PlcCpuFamily.Unknown,
                    PlcSignalKind.Memory,
                    "Bool"));
            Assert.Throws<ArgumentException>(() =>
                PlcAddressing.GetMaximumByteOffset(
                    PlcCpuFamily.S71200,
                    PlcSignalKind.Memory,
                    "Date_And_Time"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PlcAddressing.GetMaximumByteOffset(
                    (PlcCpuFamily)999,
                    PlcSignalKind.Memory,
                    "Bool"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PlcAddressing.GetMaximumByteOffset(
                    PlcCpuFamily.S71200,
                    (PlcSignalKind)999,
                    "Bool"));
        }

        [Theory]
        [InlineData(999, (int)PlcSignalKind.Memory, "Bool", 0, null)]
        [InlineData((int)PlcCpuFamily.S71200, 999, "Bool", 0, null)]
        [InlineData((int)PlcCpuFamily.S71200, (int)PlcSignalKind.Memory, "Date_And_Time", 0, null)]
        [InlineData((int)PlcCpuFamily.S71200, (int)PlcSignalKind.Memory, "Bool", -1, 0)]
        public void BuilderRejectsEveryInvalidCategory(
            int cpu,
            int signal,
            string dataType,
            int byteOffset,
            int? bitOffset)
        {
            string address;
            string error;

            Assert.False(PlcAddressing.TryBuild(
                (PlcCpuFamily)cpu,
                (PlcSignalKind)signal,
                dataType,
                byteOffset,
                bitOffset,
                out address,
                out error));
            Assert.NotEmpty(error);
        }

        [Theory]
        [InlineData("%I999999999999999999999999999999.0", "Bool")]
        [InlineData("%I0.999999999999999999999999999999", "Bool")]
        public void ParserRejectsNumericOverflow(string rawAddress, string dataType)
        {
            PlcAddressSpec spec;
            string address;
            string error;

            Assert.False(PlcAddressing.TryParse(
                PlcCpuFamily.S71200,
                rawAddress,
                dataType,
                out spec,
                out address,
                out error));
            Assert.Null(spec);
            Assert.NotEmpty(error);
        }

        [Fact]
        public void PrefixDefensivelyRejectsAnUnknownSignalKind()
        {
            var prefix = typeof(PlcAddressing).GetMethod(
                "Prefix",
                BindingFlags.Static | BindingFlags.NonPublic);

            var exception = Assert.Throws<TargetInvocationException>(() =>
                prefix.Invoke(null, new object[] { (PlcSignalKind)999, "Bool" }));

            Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
        }
    }
}
