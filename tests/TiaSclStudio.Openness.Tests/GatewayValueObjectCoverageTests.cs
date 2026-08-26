using System;
using System.Collections.Generic;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;
using Xunit;

namespace TiaSclStudio.Openness.Tests
{
    public sealed class GatewayValueObjectCoverageTests
    {
        [Fact]
        public void ExportOperationValidatesAndNormalizesItsValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TiaExportOperation(-1, TiaExportOperationKind.TransactionalImport, null, null));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TiaExportOperation(0, (TiaExportOperationKind)999, null, null));

            var operation = new TiaExportOperation(
                3,
                TiaExportOperationKind.TransactionalImport,
                null,
                null);
            Assert.Equal(3, operation.Order);
            Assert.Equal(TiaExportOperationKind.TransactionalImport, operation.Kind);
            Assert.Equal(string.Empty, operation.Target);
            Assert.Equal(string.Empty, operation.Description);
        }

        [Fact]
        public void ExportCollisionValidatesAndExposesItsValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TiaExportCollision((TiaExportCollisionKind)999, null, false, false, null));

            var collision = new TiaExportCollision(
                TiaExportCollisionKind.Block,
                null,
                true,
                false,
                null);
            Assert.Equal(TiaExportCollisionKind.Block, collision.Kind);
            Assert.Equal(string.Empty, collision.Target);
            Assert.True(collision.IsOwned);
            Assert.False(collision.CanOverwrite);
            Assert.Equal(string.Empty, collision.Description);
        }

        [Theory]
        [InlineData(-1, 0, 1u, "%I0.0")]
        [InlineData(0, -1, 1u, "%I0.0")]
        [InlineData(0, 0, 0u, "%I0.0")]
        [InlineData(0, 0, 1u, null)]
        [InlineData(0, 0, 1u, "   ")]
        public void HardwareChannelRejectsInvalidGeometry(
            int number,
            int bitAddress,
            uint width,
            string address)
        {
            Assert.ThrowsAny<ArgumentException>(() => new TiaHardwareIoChannel(
                TiaIoChannelKind.DigitalInput,
                number,
                bitAddress,
                width,
                address,
                null,
                null,
                null,
                null));
        }

        [Fact]
        public void HardwareChannelCopiesAndNormalizesMetadata()
        {
            var types = new List<string> { "Bool" };
            var channel = new TiaHardwareIoChannel(
                TiaIoChannelKind.DigitalOutput,
                2,
                23,
                1,
                "  %Q2.7  ",
                types,
                null,
                null,
                null);
            types.Add("Word");

            Assert.Equal(TiaIoChannelKind.DigitalOutput, channel.Kind);
            Assert.Equal(2, channel.Number);
            Assert.Equal(23, channel.BitAddress);
            Assert.Equal(1u, channel.WidthBits);
            Assert.Equal("%Q2.7", channel.LogicalAddress);
            Assert.Equal(new[] { "Bool" }, channel.SuggestedDataTypes);
            Assert.Equal(string.Empty, channel.DeviceItemPath);
            Assert.Equal(string.Empty, channel.DeviceItemTypeIdentifier);
            Assert.Equal(string.Empty, channel.DeviceItemTypeName);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)channel.SuggestedDataTypes).Add("Int"));
        }

        [Fact]
        public void DiagnosticNormalizesOptionalFieldsAndFormatsForLogs()
        {
            var diagnostic = new OpennessDiagnostic(
                "CODE",
                DiagnosticSeverity.Warning,
                "message",
                null,
                null);

            Assert.Equal("CODE", diagnostic.Code);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("message", diagnostic.Message);
            Assert.Equal(string.Empty, diagnostic.Location);
            Assert.Equal(string.Empty, diagnostic.ExceptionType);
            Assert.Equal("CODE: message", diagnostic.ToString());
        }
    }
}
