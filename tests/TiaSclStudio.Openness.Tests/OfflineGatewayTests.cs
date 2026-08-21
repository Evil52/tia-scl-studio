using System;
using System.Linq;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Discovery;
using TiaSclStudio.Openness.Gateway;
using Xunit;

namespace TiaSclStudio.Openness.Tests
{
    /// <summary>
    /// The offline gateway is the default. Everything in CI, in a developer's
    /// checkout, and on a machine with no TIA Portal runs through it, so the
    /// property that matters is negative: it must never be able to reach or
    /// change a real project, no matter what it is asked to do.
    /// </summary>
    public sealed class OfflineGatewayTests
    {
        [Fact]
        public void ReportsItselfAsOfflineAndUnableToExport()
        {
            using (var gateway = new OfflineGateway())
            {
                var status = gateway.GetStatus();

                Assert.Equal(TiaGatewayMode.Offline, status.Mode);
                Assert.False(status.IsConnected);
                Assert.False(status.CanExport);
                Assert.Null(status.Installation);
            }
        }

        [Fact]
        public void AlwaysCarriesTheOfflineDiagnostic()
        {
            using (var gateway = new OfflineGateway())
            {
                Assert.Contains(
                    gateway.GetStatus().Diagnostics,
                    diagnostic => diagnostic.Code == OpennessDiagnosticCodes.OfflineMode);
            }
        }

        [Fact]
        public void KeepsTheDiagnosticsFromDiscovery()
        {
            var discovery = new OpennessDiscoveryResult(
                null,
                new[]
                {
                    new OpennessDiagnostic(
                        OpennessDiagnosticCodes.NoInstallationFound,
                        DiagnosticSeverity.Warning,
                        "No Portal installation was found.")
                });

            using (var gateway = new OfflineGateway(discovery))
            {
                var codes = gateway.GetStatus().Diagnostics.Select(item => item.Code).ToList();

                Assert.Contains(OpennessDiagnosticCodes.NoInstallationFound, codes);
                Assert.Contains(OpennessDiagnosticCodes.OfflineMode, codes);
            }
        }

        [Fact]
        public void ToleratesANullDiscoveryResult()
        {
            using (var gateway = new OfflineGateway(null))
            {
                Assert.Equal(TiaGatewayMode.Offline, gateway.GetStatus().Mode);
            }
        }

        [Theory]
        [InlineData(TiaConnectionMode.AttachToRunning)]
        [InlineData(TiaConnectionMode.StartWithoutUserInterface)]
        public void ConnectingChangesNothing(TiaConnectionMode mode)
        {
            using (var gateway = new OfflineGateway())
            {
                var status = gateway.Connect(new TiaConnectionRequest(mode));

                Assert.Equal(TiaGatewayMode.Offline, status.Mode);
                Assert.False(status.IsConnected);
                Assert.False(status.CanExport);
            }
        }

        [Fact]
        public void ConnectingWithANullRequestDoesNotThrow()
        {
            using (var gateway = new OfflineGateway())
            {
                Assert.False(gateway.Connect(null).IsConnected);
            }
        }

        [Fact]
        public void PreviewNeverAuthorizesAnExport()
        {
            using (var gateway = new OfflineGateway())
            {
                var preview = gateway.PreviewExport(Request());

                Assert.False(preview.CanExecute);
                Assert.Equal(string.Empty, preview.ConfirmationToken);
                Assert.Empty(preview.Operations);
                Assert.Empty(preview.Collisions);
                Assert.Contains(
                    preview.Diagnostics,
                    diagnostic => diagnostic.Code == OpennessDiagnosticCodes.ExportSkippedOffline);
            }
        }

        [Fact]
        public void PreviewRejectsAMissingRequest()
        {
            using (var gateway = new OfflineGateway())
            {
                var preview = gateway.PreviewExport(null);

                Assert.False(preview.CanExecute);
                Assert.Contains(
                    preview.Diagnostics,
                    diagnostic => diagnostic.Code == OpennessDiagnosticCodes.ExportRequestMissing &&
                        diagnostic.Severity == DiagnosticSeverity.Error);
            }
        }

        [Fact]
        public void ExportDoesNothingAndSaysSo()
        {
            using (var gateway = new OfflineGateway())
            {
                var result = gateway.Export(Request(), "any-token");

                Assert.Equal(TiaExportOutcome.NotExecuted, result.Outcome);
                Assert.Equal(0, result.ImportedSourceCount);
                Assert.False(result.CompilationAttempted);
                Assert.Contains(
                    result.Diagnostics,
                    diagnostic => diagnostic.Code == OpennessDiagnosticCodes.ExportSkippedOffline);
            }
        }

        [Fact]
        public void ExportRejectsAMissingRequest()
        {
            using (var gateway = new OfflineGateway())
            {
                var result = gateway.Export(null, "any-token");

                Assert.Equal(TiaExportOutcome.Rejected, result.Outcome);
                Assert.Contains(
                    result.Diagnostics,
                    diagnostic => diagnostic.Code == OpennessDiagnosticCodes.ExportRequestMissing);
            }
        }

        [Fact]
        public void HardwareIoDiscoveryStaysOfflineAndReturnsNoGuessedChannels()
        {
            using (var gateway = new OfflineGateway())
            {
                var catalog = gateway.ReadHardwareIoCatalog();

                Assert.False(catalog.IsAvailable);
                Assert.Equal(TiaCpuFamily.Unknown, catalog.CpuFamily);
                Assert.Equal(string.Empty, catalog.CpuModel);
                Assert.Equal(string.Empty, catalog.CpuTypeIdentifier);
                Assert.Empty(catalog.Channels);
                Assert.Contains(
                    catalog.Diagnostics,
                    diagnostic =>
                        diagnostic.Code == OpennessDiagnosticCodes.HardwareIoCatalogUnavailable &&
                        diagnostic.Severity == DiagnosticSeverity.Information);
            }
        }

        [Fact]
        public void HardwareIoOfflineSnapshotCollectionsAreReadOnly()
        {
            using (var gateway = new OfflineGateway())
            {
                var catalog = gateway.ReadHardwareIoCatalog();

                Assert.Throws<NotSupportedException>(() =>
                    ((System.Collections.Generic.IList<TiaHardwareIoChannel>)catalog.Channels).Add(
                        new TiaHardwareIoChannel(
                            TiaIoChannelKind.DigitalInput,
                            0,
                            0,
                            1,
                            "%I0.0",
                            new[] { "Bool" },
                            "PLC/DI",
                            "type",
                            "DI")));
            }
        }

        /// <summary>
        /// A confirmation token is what a caller presents to prove a dry run
        /// happened. The offline gateway must not treat any value, including
        /// null or a forged one, as permission to act.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("forged")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        public void NoConfirmationTokenUnlocksAnExport(string token)
        {
            using (var gateway = new OfflineGateway())
            {
                Assert.Equal(TiaExportOutcome.NotExecuted, gateway.Export(Request(), token).Outcome);
            }
        }

        [Fact]
        public void ExportIsIdempotentAcrossRepeatedCalls()
        {
            using (var gateway = new OfflineGateway())
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    Assert.Equal(TiaExportOutcome.NotExecuted, gateway.Export(Request(), "t").Outcome);
                    Assert.False(gateway.GetStatus().CanExport);
                }
            }
        }

        [Fact]
        public void DisposingTwiceIsHarmless()
        {
            var gateway = new OfflineGateway();
            gateway.Dispose();
            gateway.Dispose();
        }

        [Fact]
        public void StatusIsStableAndItsDiagnosticsAreReadOnly()
        {
            using (var gateway = new OfflineGateway())
            {
                var first = gateway.GetStatus();
                var second = gateway.GetStatus();

                Assert.Equal(first.Description, second.Description);
                Assert.Equal(first.Diagnostics.Count, second.Diagnostics.Count);
                Assert.Throws<NotSupportedException>(() =>
                    ((System.Collections.Generic.IList<OpennessDiagnostic>)first.Diagnostics).Add(
                        new OpennessDiagnostic("X", DiagnosticSeverity.Information, "m")));
            }
        }

        private static TiaExportRequest Request()
        {
            return new TiaExportRequest(
                "C:\\projects\\Demo.ap17",
                new[] { new TiaSourceFile("C:\\out\\FB_Valve.scl", 0, TiaSourceKind.Block) },
                compileAfterImport: true);
        }
    }
}
