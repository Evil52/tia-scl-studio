using System.Collections.Generic;
using System.Collections.ObjectModel;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Model;

namespace TiaSclStudio.Openness.Gateway
{
    public sealed class TiaGatewayStatus
    {
        public TiaGatewayStatus(
            TiaGatewayMode mode,
            bool isConnected,
            bool canExport,
            string description,
            OpennessInstallation installation,
            IEnumerable<OpennessDiagnostic> diagnostics)
        {
            Mode = mode;
            IsConnected = isConnected;
            CanExport = canExport;
            Description = description ?? string.Empty;
            Installation = installation;
            Diagnostics = new ReadOnlyCollection<OpennessDiagnostic>(
                new List<OpennessDiagnostic>(diagnostics ?? new OpennessDiagnostic[0]));
        }

        public TiaGatewayMode Mode { get; }

        public bool IsConnected { get; }

        public bool CanExport { get; }

        public string Description { get; }

        public OpennessInstallation Installation { get; }

        public IReadOnlyList<OpennessDiagnostic> Diagnostics { get; }
    }
}
