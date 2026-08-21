using System.Collections.Generic;
using System.Collections.ObjectModel;
using TiaSclStudio.Openness.Diagnostics;

namespace TiaSclStudio.Openness.Gateway
{
    /// <summary>
    /// Immutable read-only snapshot of hardware channels registered by the
    /// selected PLC address controller.
    /// </summary>
    public sealed class TiaHardwareIoCatalog
    {
        public TiaHardwareIoCatalog(
            bool isAvailable,
            TiaCpuFamily cpuFamily,
            string cpuModel,
            string cpuTypeIdentifier,
            IEnumerable<TiaHardwareIoChannel> channels,
            IEnumerable<OpennessDiagnostic> diagnostics)
        {
            IsAvailable = isAvailable;
            CpuFamily = cpuFamily;
            CpuModel = cpuModel ?? string.Empty;
            CpuTypeIdentifier = cpuTypeIdentifier ?? string.Empty;
            Channels = new ReadOnlyCollection<TiaHardwareIoChannel>(
                new List<TiaHardwareIoChannel>(channels ?? new TiaHardwareIoChannel[0]));
            Diagnostics = new ReadOnlyCollection<OpennessDiagnostic>(
                new List<OpennessDiagnostic>(diagnostics ?? new OpennessDiagnostic[0]));
        }

        public bool IsAvailable { get; }

        public TiaCpuFamily CpuFamily { get; }

        public string CpuModel { get; }

        public string CpuTypeIdentifier { get; }

        public IReadOnlyList<TiaHardwareIoChannel> Channels { get; }

        public IReadOnlyList<OpennessDiagnostic> Diagnostics { get; }
    }
}
