using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;

namespace TiaSclStudio.Openness.Legacy.V17
{
    /// <summary>
    /// Siemens-independent policy shared by the real adapter and unit tests.
    /// </summary>
    internal static class LegacyLibraryReadbackPolicy
    {
        internal const int MaximumObjectCount = 4096;
        internal const long MaximumSourceBytesPerObject = 8L * 1024L * 1024L;
        internal const long MaximumStagedExportBytes = 64L * 1024L * 1024L;
        internal const long MaximumTotalSourceBytes = 64L * 1024L * 1024L;
        internal const int MaximumTagTableCount = 4096;
        internal const int MaximumTagGroupCount = 16384;
        internal const int MaximumTagGroupDepth = 256;
        internal const int MaximumTagCount = 65536;
        internal const int MaximumTagFieldCharacters = 1024 * 1024;
        internal const long MaximumTagCatalogCharacters = 32L * 1024L * 1024L;
        internal const long MaximumTagTopologyCharacters = 8L * 1024L * 1024L;
        internal const int MaximumSoftwareUnitCount = 4096;
        internal const long MaximumTargetedComLookupCount = 65536;
        internal const int MaximumTagDiagnosticCount = 256;
        internal const int MaximumDiagnosticTextCharacters = 4096;
        internal const int MaximumHardwareTreeDepth = 128;
        internal const int MaximumHardwareGroupCount = 4096;
        internal const int MaximumHardwareDeviceCount = 4096;
        internal const int MaximumHardwareDeviceItemCount = 65536;
        internal const int MaximumHardwareRegisteredAddressCount = 65536;
        internal const int MaximumHardwareChannelCount = 65536;
        internal const int MaximumHardwarePathCharacters = 4096;
        internal const int MaximumHardwareDiagnosticCount = 256;

        internal static string GetSourceExtension(TiaLibrarySourceKind kind)
        {
            return kind == TiaLibrarySourceKind.DataType ? ".udt" : ".scl";
        }

        internal static string GetCollisionKey(TiaLibrarySourceKind kind, string name)
        {
            return (kind == TiaLibrarySourceKind.DataType ? "TYPE|" : "BLOCK|") +
                (name ?? string.Empty);
        }

        internal static string EscapeStablePathSegment(string value)
        {
            return (value ?? string.Empty)
                .Replace("%", "%25")
                .Replace("/", "%2F");
        }

        internal static string GetRootTagScopePath()
        {
            return "PLC";
        }

        internal static string GetUnitTagScopePath(string unitName)
        {
            if (string.IsNullOrWhiteSpace(unitName))
            {
                throw new ArgumentException("A software-unit name is required.", nameof(unitName));
            }

            return "UNIT/" + EscapeStablePathSegment(unitName);
        }

        internal static string BuildTagTablePath(
            string scopePath,
            string escapedGroupPath,
            string tableName)
        {
            if (string.IsNullOrWhiteSpace(scopePath))
            {
                throw new ArgumentException("A tag-table scope path is required.", nameof(scopePath));
            }

            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new ArgumentException("A tag-table name is required.", nameof(tableName));
            }

            var groupPath = (escapedGroupPath ?? string.Empty).Trim('/');
            var prefix = scopePath.Trim('/');
            return string.IsNullOrEmpty(groupPath)
                ? prefix + "/" + EscapeStablePathSegment(tableName)
                : prefix + "/" + groupPath + "/" + EscapeStablePathSegment(tableName);
        }

        internal static string BuildTagTopologySignature(
            IEnumerable<string> scopePaths,
            IEnumerable<KeyValuePair<string, long>> tables)
        {
            var components = new List<string>();
            components.AddRange((scopePaths ?? Enumerable.Empty<string>())
                .Select(path => "S|" + (path ?? string.Empty)));
            components.AddRange((tables ?? Enumerable.Empty<KeyValuePair<string, long>>())
                .Select(table =>
                    "T|" + (table.Key ?? string.Empty) + "|" +
                    table.Value.ToString(CultureInfo.InvariantCulture)));
            components.Sort(StringComparer.Ordinal);

            var builder = new StringBuilder();
            long characterCount = 0;
            foreach (var component in components)
            {
                characterCount += component.Length;
                if (characterCount > MaximumTagTopologyCharacters)
                {
                    throw new InvalidOperationException(
                        "PLC-tag topology exceeds the configured safety limit.");
                }

                builder.Append(component.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(component);
            }

            using (var algorithm = SHA256.Create())
            {
                return string.Concat(algorithm
                    .ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()))
                    .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        internal static IEnumerable<LibraryGroupEntry<TItem>> WalkGroupTree<TGroup, TItem>(
            TGroup root,
            Func<TGroup, IEnumerable<TItem>> getItems,
            Func<TGroup, IEnumerable<TGroup>> getChildren,
            Func<TGroup, string> getName)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (getItems == null)
            {
                throw new ArgumentNullException(nameof(getItems));
            }

            if (getChildren == null)
            {
                throw new ArgumentNullException(nameof(getChildren));
            }

            if (getName == null)
            {
                throw new ArgumentNullException(nameof(getName));
            }

            return Walk(root, string.Empty, getItems, getChildren, getName);
        }

        private static IEnumerable<LibraryGroupEntry<TItem>> Walk<TGroup, TItem>(
            TGroup group,
            string groupPath,
            Func<TGroup, IEnumerable<TItem>> getItems,
            Func<TGroup, IEnumerable<TGroup>> getChildren,
            Func<TGroup, string> getName)
        {
            var items = getItems(group);
            if (items != null)
            {
                foreach (var item in items)
                {
                    yield return new LibraryGroupEntry<TItem>(item, groupPath);
                }
            }

            var children = getChildren(group);
            if (children == null)
            {
                yield break;
            }

            foreach (var child in children)
            {
                if (child == null)
                {
                    continue;
                }

                var childName = (getName(child) ?? string.Empty).Trim();
                var childPath = string.IsNullOrEmpty(groupPath)
                    ? childName
                    : groupPath + "/" + childName;
                foreach (var entry in Walk(child, childPath, getItems, getChildren, getName))
                {
                    yield return entry;
                }
            }
        }
    }

    /// <summary>
    /// Bounds requested-only Siemens composition lookups. Individual catalog and
    /// request limits are not sufficient because their product can otherwise
    /// produce hundreds of millions of remote Find calls on a nested project.
    /// </summary>
    internal sealed class LegacyTargetedComLookupBudget
    {
        private long consumed;

        internal long Consumed => consumed;

        internal void Consume(string operation)
        {
            if (consumed >= LegacyLibraryReadbackPolicy.MaximumTargetedComLookupCount)
            {
                throw new InvalidOperationException(
                    "The requested-only TIA lookup budget was exhausted before " +
                    (string.IsNullOrWhiteSpace(operation) ? "a composition lookup" : operation) +
                    ". Reduce the request size or simplify the PLC group topology.");
            }

            consumed++;
        }
    }

    internal sealed class BoundedDiagnosticCollection : Collection<OpennessDiagnostic>
    {
        private readonly int maximumCount;
        private readonly string summaryCode;
        private readonly string summaryLocation;
        private int suppressedCount;
        private DiagnosticSeverity suppressedSeverity;
        private bool sealedCollection;

        internal BoundedDiagnosticCollection(
            int maximumCount,
            string summaryCode,
            string summaryLocation)
        {
            if (maximumCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCount));
            }

            if (string.IsNullOrWhiteSpace(summaryCode))
            {
                throw new ArgumentException("A summary diagnostic code is required.", nameof(summaryCode));
            }

            this.maximumCount = maximumCount;
            this.summaryCode = summaryCode;
            this.summaryLocation = Limit(summaryLocation);
        }

        internal IList<OpennessDiagnostic> Seal()
        {
            if (!sealedCollection && suppressedCount > 0)
            {
                base.InsertItem(
                    Count,
                    new OpennessDiagnostic(
                        summaryCode,
                        suppressedSeverity,
                        suppressedCount.ToString(CultureInfo.InvariantCulture) +
                        " additional diagnostic message(s) were suppressed by the safety limit.",
                        summaryLocation));
            }

            sealedCollection = true;
            return this;
        }

        protected override void InsertItem(int index, OpennessDiagnostic item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (sealedCollection)
            {
                throw new InvalidOperationException("The diagnostic collection has already been sealed.");
            }

            if (Count >= maximumCount - 1)
            {
                suppressedCount++;
                if (item.Severity > suppressedSeverity)
                {
                    suppressedSeverity = item.Severity;
                }

                return;
            }

            base.InsertItem(Count, Normalize(item));
        }

        private static OpennessDiagnostic Normalize(OpennessDiagnostic item)
        {
            return new OpennessDiagnostic(
                item.Code,
                item.Severity,
                Limit(item.Message),
                Limit(item.Location),
                Limit(item.ExceptionType));
        }

        private static string Limit(string value)
        {
            var normalized = value ?? string.Empty;
            return normalized.Length <= LegacyLibraryReadbackPolicy.MaximumDiagnosticTextCharacters
                ? normalized
                : normalized.Substring(
                    0,
                    LegacyLibraryReadbackPolicy.MaximumDiagnosticTextCharacters - 3) + "...";
        }
    }

    internal sealed class LibraryGroupEntry<TItem>
    {
        internal LibraryGroupEntry(TItem item, string groupPath)
        {
            Item = item;
            GroupPath = groupPath ?? string.Empty;
        }

        internal TItem Item { get; }

        internal string GroupPath { get; }
    }
}
