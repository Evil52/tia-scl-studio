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
            : this(
                outcome,
                importedSourceCount,
                compilationAttempted,
                diagnostics,
                false,
                false)
        {
        }

        public TiaExportResult(
            TiaExportOutcome outcome,
            int importedSourceCount,
            bool compilationAttempted,
            IEnumerable<OpennessDiagnostic> diagnostics,
            bool readbackAttempted,
            bool readbackSucceeded)
        {
            if (importedSourceCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(importedSourceCount));
            }

            if (readbackSucceeded && !readbackAttempted)
            {
                throw new ArgumentException(
                    "A successful readback must also be marked as attempted.",
                    nameof(readbackSucceeded));
            }

            Outcome = outcome;
            ImportedSourceCount = importedSourceCount;
            CompilationAttempted = compilationAttempted;
            ReadbackAttempted = readbackAttempted;
            ReadbackSucceeded = readbackSucceeded;
            Diagnostics = new ReadOnlyCollection<OpennessDiagnostic>(
                new List<OpennessDiagnostic>(diagnostics ?? new OpennessDiagnostic[0]));
        }

        public TiaExportOutcome Outcome { get; }

        public int ImportedSourceCount { get; }

        public bool CompilationAttempted { get; }

        public bool ReadbackAttempted { get; }

        public bool ReadbackSucceeded { get; }

        public IReadOnlyList<OpennessDiagnostic> Diagnostics { get; }
    }
}
