using System.Collections.Generic;
using System.Collections.ObjectModel;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Model;

namespace TiaSclStudio.Openness.Discovery
{
    public sealed class OpennessDiscoveryResult
    {
        public OpennessDiscoveryResult(
            IEnumerable<OpennessInstallation> installations,
            IEnumerable<OpennessDiagnostic> diagnostics)
        {
            Installations = new ReadOnlyCollection<OpennessInstallation>(
                new List<OpennessInstallation>(installations ?? new OpennessInstallation[0]));
            Diagnostics = new ReadOnlyCollection<OpennessDiagnostic>(
                new List<OpennessDiagnostic>(diagnostics ?? new OpennessDiagnostic[0]));
        }

        public IReadOnlyList<OpennessInstallation> Installations { get; }

        public IReadOnlyList<OpennessDiagnostic> Diagnostics { get; }
    }
}
