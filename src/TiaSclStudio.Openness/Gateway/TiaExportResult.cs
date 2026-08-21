using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TiaSclStudio.Openness.Diagnostics;

namespace TiaSclStudio.Openness.Gateway
{
    public sealed class TiaExportResult
    {
        public TiaExportResult(
            TiaExportOutcome outcome,
            int importedSourceCount,
            bool compilationAttempted,
            IEnumerable<OpennessDiagnostic> diagnostics)
        {
            if (importedSourceCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(importedSourceCount));
            }

            Outcome = outcome;
            ImportedSourceCount = importedSourceCount;
            CompilationAttempted = compilationAttempted;
            Diagnostics = new ReadOnlyCollection<OpennessDiagnostic>(
                new List<OpennessDiagnostic>(diagnostics ?? new OpennessDiagnostic[0]));
        }

        public TiaExportOutcome Outcome { get; }

        public int ImportedSourceCount { get; }

        public bool CompilationAttempted { get; }

        public IReadOnlyList<OpennessDiagnostic> Diagnostics { get; }
    }
}
