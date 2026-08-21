using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TiaSclStudio.Openness.Diagnostics;

namespace TiaSclStudio.Openness.Gateway
{
    /// <summary>
    /// Immutable read-only PLC-tag snapshot. IsComplete is false when any
    /// table or tag could not be represented safely.
    /// </summary>
    public sealed class TiaPlcTagCatalog
    {
        public TiaPlcTagCatalog(
            bool isAvailable,
            bool isComplete,
            string deviceName,
            string softwareName,
            IEnumerable<TiaPlcTagCatalogItem> tags,
            IEnumerable<OpennessDiagnostic> diagnostics)
        {
            IsAvailable = isAvailable;
            IsComplete = isAvailable && isComplete;
            DeviceName = deviceName ?? string.Empty;
            SoftwareName = softwareName ?? string.Empty;
            Tags = new ReadOnlyCollection<TiaPlcTagCatalogItem>(
                CopyNonNull(tags, nameof(tags)));
            Diagnostics = new ReadOnlyCollection<OpennessDiagnostic>(
                CopyNonNull(diagnostics, nameof(diagnostics)));
        }

        public bool IsAvailable { get; }

        public bool IsComplete { get; }

        public string DeviceName { get; }

        public string SoftwareName { get; }

        public IReadOnlyList<TiaPlcTagCatalogItem> Tags { get; }

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
                        "Catalog collections cannot contain null elements.",
                        parameterName);
                }

                result.Add(value);
            }

            return result;
        }
    }
}
