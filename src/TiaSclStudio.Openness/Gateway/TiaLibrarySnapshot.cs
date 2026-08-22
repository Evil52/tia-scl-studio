using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TiaSclStudio.Openness.Diagnostics;

namespace TiaSclStudio.Openness.Gateway
{
    /// <summary>
    /// Immutable read-only snapshot of FB, FC and PLC data-type declarations in
    /// the currently selected PLC. SCL blocks retain their generated source;
    /// LAD/FBD blocks can be represented by a read-only interface declaration.
    /// A snapshot can be available but incomplete when individual protected or
    /// unsupported objects were skipped.
    /// </summary>
    public sealed class TiaLibrarySnapshot
    {
        public TiaLibrarySnapshot(
            bool isAvailable,
            bool isComplete,
            string deviceName,
            string softwareName,
            IEnumerable<TiaLibrarySource> sources,
            IEnumerable<OpennessDiagnostic> diagnostics)
        {
            IsAvailable = isAvailable;
            IsComplete = isAvailable && isComplete;
            DeviceName = deviceName ?? string.Empty;
            SoftwareName = softwareName ?? string.Empty;
            Sources = new ReadOnlyCollection<TiaLibrarySource>(
                CopyNonNull(sources, nameof(sources)));
            Diagnostics = new ReadOnlyCollection<OpennessDiagnostic>(
                CopyNonNull(diagnostics, nameof(diagnostics)));
        }

        public bool IsAvailable { get; }

        public bool IsComplete { get; }

        public string DeviceName { get; }

        public string SoftwareName { get; }

        public IReadOnlyList<TiaLibrarySource> Sources { get; }

        public IReadOnlyList<OpennessDiagnostic> Diagnostics { get; }

        private static List<T> CopyNonNull<T>(IEnumerable<T> values, string parameterName)
            where T : class
        {
            var result = new List<T>();
            if (values == null)
            {
                return result;
            }

            foreach (var value in values)
            {
                if (value == null)
                {
                    throw new ArgumentException(
                        "Snapshot collections cannot contain null elements.",
                        parameterName);
                }

                result.Add(value);
            }

            return result;
        }
    }
}
