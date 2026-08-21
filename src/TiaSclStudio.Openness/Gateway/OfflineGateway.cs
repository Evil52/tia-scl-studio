using System.Collections.Generic;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Discovery;

namespace TiaSclStudio.Openness.Gateway
{
    /// <summary>
    /// A side-effect-free gateway for development, preview and CI. Export never
    /// opens a project, reads a source file, starts Portal or writes anything.
    /// </summary>
    public sealed class OfflineGateway : ITiaGateway
    {
        private readonly TiaGatewayStatus status;

        public OfflineGateway()
            : this(new OpennessDiscoveryResult(null, null))
        {
        }

        public OfflineGateway(OpennessDiscoveryResult discovery)
        {
            var diagnostics = new List<OpennessDiagnostic>();
            if (discovery != null)
            {
                diagnostics.AddRange(discovery.Diagnostics);
            }

            diagnostics.Add(new OpennessDiagnostic(
                OpennessDiagnosticCodes.OfflineMode,
                DiagnosticSeverity.Information,
                "Offline gateway is active. TIA Portal will not be opened or modified."));

            status = new TiaGatewayStatus(
                TiaGatewayMode.Offline,
                false,
                false,
                "Offline mode",
                null,
                diagnostics);
        }

        public TiaGatewayStatus GetStatus()
        {
            return status;
        }

        public TiaGatewayStatus Connect(TiaConnectionRequest request)
        {
            return status;
        }

        public TiaHardwareIoCatalog ReadHardwareIoCatalog()
        {
            return new TiaHardwareIoCatalog(
                false,
                TiaCpuFamily.Unknown,
                string.Empty,
                string.Empty,
                null,
                new[]
                {
                    new OpennessDiagnostic(
                        OpennessDiagnosticCodes.HardwareIoCatalogUnavailable,
                        DiagnosticSeverity.Information,
                        "Hardware I/O discovery is unavailable because the gateway is offline. No TIA Portal state was accessed.")
                });
        }

        public TiaExportPreview PreviewExport(TiaExportRequest request)
        {
            if (request == null)
            {
                return new TiaExportPreview(
                    false,
                    string.Empty,
                    null,
                    null,
                    new[]
                    {
                        new OpennessDiagnostic(
                            OpennessDiagnosticCodes.ExportRequestMissing,
                            DiagnosticSeverity.Error,
                            "An export request is required.")
                    });
            }

            return new TiaExportPreview(
                false,
                string.Empty,
                null,
                null,
                new[]
                {
                    new OpennessDiagnostic(
                        OpennessDiagnosticCodes.ExportSkippedOffline,
                        DiagnosticSeverity.Information,
                        "Preview is unavailable because the gateway is offline. No TIA Portal state was accessed.")
                });
        }

        public TiaExportResult Export(TiaExportRequest request, string confirmationToken)
        {
            if (request == null)
            {
                return new TiaExportResult(
                    TiaExportOutcome.Rejected,
                    0,
                    false,
                    new[]
                    {
                        new OpennessDiagnostic(
                            OpennessDiagnosticCodes.ExportRequestMissing,
                            DiagnosticSeverity.Error,
                            "An export request is required.")
                    });
            }

            return new TiaExportResult(
                TiaExportOutcome.NotExecuted,
                0,
                false,
                new[]
                {
                    new OpennessDiagnostic(
                        OpennessDiagnosticCodes.ExportSkippedOffline,
                        DiagnosticSeverity.Information,
                        "Export was skipped because the gateway is offline. No TIA Portal state was changed.")
                });
        }

        public void Dispose()
        {
        }
    }
}
