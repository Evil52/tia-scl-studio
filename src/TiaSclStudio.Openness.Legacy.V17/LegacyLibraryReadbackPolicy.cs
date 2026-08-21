using System;
using System.Collections.Generic;
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
        internal const long MaximumTotalSourceBytes = 64L * 1024L * 1024L;

        internal static string GetSourceExtension(TiaLibrarySourceKind kind)
        {
            return kind == TiaLibrarySourceKind.DataType ? ".udt" : ".scl";
        }

        internal static string GetCollisionKey(TiaLibrarySourceKind kind, string name)
        {
            return (kind == TiaLibrarySourceKind.DataType ? "TYPE|" : "BLOCK|") +
                (name ?? string.Empty);
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
