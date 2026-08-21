using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TiaSclStudio.Openness.Gateway
{
    /// <summary>
    /// Describes an export without containing Core model objects or Siemens API types.
    /// Source order is normalized here because TIA resolves symbols while importing.
    /// </summary>
    public sealed class TiaExportRequest
    {
        public const string DefaultOwnershipMarker = "TIASCL";

        public TiaExportRequest(
            string projectPath,
            IEnumerable<TiaSourceFile> sources,
            bool compileAfterImport,
            string targetDeviceName = null,
            string targetSoftwareName = null)
            : this(
                projectPath,
                sources,
                compileAfterImport,
                targetDeviceName,
                targetSoftwareName,
                null,
                false,
                DefaultOwnershipMarker,
                false,
                false)
        {
        }

        public TiaExportRequest(
            string projectPath,
            IEnumerable<TiaSourceFile> sources,
            bool compileAfterImport,
            string targetDeviceName,
            string targetSoftwareName,
            IEnumerable<TiaPlcTag> tags,
            bool allowTagUpdates,
            string ownershipMarker,
            bool allowOwnedBlockOverwrite,
            bool saveProjectAfterExport)
            : this(
                projectPath,
                sources,
                compileAfterImport,
                targetDeviceName,
                targetSoftwareName,
                tags,
                allowTagUpdates,
                ownershipMarker,
                allowOwnedBlockOverwrite,
                saveProjectAfterExport,
                false)
        {
        }

        public TiaExportRequest(
            string projectPath,
            IEnumerable<TiaSourceFile> sources,
            bool compileAfterImport,
            string targetDeviceName,
            string targetSoftwareName,
            IEnumerable<TiaPlcTag> tags,
            bool allowTagUpdates,
            string ownershipMarker,
            bool allowOwnedBlockOverwrite,
            bool saveProjectAfterExport,
            bool verifySavedExportAfterReopen)
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }

            var sourceList = sources.ToList();
            if (sourceList.Any(source => source == null))
            {
                throw new ArgumentException("The source list cannot contain null items.", nameof(sources));
            }

            var tagList = tags == null ? new List<TiaPlcTag>() : tags.ToList();
            if (tagList.Any(tag => tag == null))
            {
                throw new ArgumentException("The tag list cannot contain null items.", nameof(tags));
            }

            ProjectPath = projectPath == null ? string.Empty : projectPath.Trim();
            Sources = new ReadOnlyCollection<TiaSourceFile>(
                sourceList.OrderBy(source => source.ImportOrder).ThenBy(source => source.FilePath, StringComparer.OrdinalIgnoreCase).ToList());
            Tags = new ReadOnlyCollection<TiaPlcTag>(
                tagList.OrderBy(tag => tag.TableName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList());
            CompileAfterImport = compileAfterImport;
            TargetDeviceName = targetDeviceName == null ? string.Empty : targetDeviceName.Trim();
            TargetSoftwareName = targetSoftwareName == null ? string.Empty : targetSoftwareName.Trim();
            AllowTagUpdates = allowTagUpdates;
            OwnershipMarker = string.IsNullOrWhiteSpace(ownershipMarker)
                ? DefaultOwnershipMarker
                : ownershipMarker.Trim();
            AllowOwnedBlockOverwrite = allowOwnedBlockOverwrite;
            SaveProjectAfterExport = saveProjectAfterExport;
            VerifySavedExportAfterReopen = verifySavedExportAfterReopen;
        }

        public string ProjectPath { get; }

        public IReadOnlyList<TiaSourceFile> Sources { get; }

        public IReadOnlyList<TiaPlcTag> Tags { get; }

        public bool CompileAfterImport { get; }

        public string TargetDeviceName { get; }

        public string TargetSoftwareName { get; }

        public bool AllowTagUpdates { get; }

        public string OwnershipMarker { get; }

        public bool AllowOwnedBlockOverwrite { get; }

        public bool SaveProjectAfterExport { get; }

        /// <summary>
        /// Requests the fail-closed save, close/reopen and readback verification flow.
        /// The legacy gateway permits it only for an adapter-owned headless session.
        /// </summary>
        public bool VerifySavedExportAfterReopen { get; }
    }
}
