using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TiaSclStudio.Openness.Legacy.V17
{
    internal enum LegacyProjectModifiedObservation
    {
        Clean = 0,
        Dirty = 1,
        Unknown = 2
    }

    internal sealed class LegacyUnsavedEvidenceDecision
    {
        internal LegacyUnsavedEvidenceDecision(
            bool hasCommittedChanges,
            bool dirtyWasObserved,
            bool hasUnsavedChanges)
        {
            HasCommittedChanges = hasCommittedChanges;
            DirtyWasObserved = dirtyWasObserved;
            HasUnsavedChanges = hasUnsavedChanges;
        }

        internal bool HasCommittedChanges { get; }

        internal bool DirtyWasObserved { get; }

        internal bool HasUnsavedChanges { get; }
    }

    /// <summary>
    /// Keeps conservative evidence of committed changes until Portal has first
    /// reported dirty and later clean. A clean value observed immediately after
    /// commit is not enough because IsModified can lag behind transaction finalization.
    /// </summary>
    internal static class LegacyUnsavedEvidencePolicy
    {
        internal static LegacyUnsavedEvidenceDecision Evaluate(
            bool hasCommittedChanges,
            bool dirtyWasObserved,
            LegacyProjectModifiedObservation observation)
        {
            if (!Enum.IsDefined(typeof(LegacyProjectModifiedObservation), observation))
            {
                throw new ArgumentOutOfRangeException(nameof(observation));
            }

            if (observation == LegacyProjectModifiedObservation.Dirty)
            {
                return new LegacyUnsavedEvidenceDecision(
                    hasCommittedChanges,
                    dirtyWasObserved || hasCommittedChanges,
                    true);
            }

            if (observation == LegacyProjectModifiedObservation.Unknown)
            {
                return new LegacyUnsavedEvidenceDecision(
                    hasCommittedChanges,
                    dirtyWasObserved,
                    true);
            }

            if (hasCommittedChanges && dirtyWasObserved)
            {
                return new LegacyUnsavedEvidenceDecision(false, false, false);
            }

            return new LegacyUnsavedEvidenceDecision(
                hasCommittedChanges,
                dirtyWasObserved,
                hasCommittedChanges);
        }
    }

    internal enum LegacyConnectionReleaseIntent
    {
        Reconnect = 0,
        FinalDispose = 1
    }

    internal enum LegacyConnectionReleaseAction
    {
        Release = 0,
        BlockReconnect = 1,
        PreserveForShutdown = 2,
        BlockUntilApplicationRestart = 3
    }

    internal static class LegacyConnectionReleasePolicy
    {
        internal static LegacyConnectionReleaseAction Decide(
            bool processLifecycleIsBlocked,
            bool hasUnsavedProjectChanges,
            bool adapterOwnsOrStartedHeadlessSession,
            LegacyConnectionReleaseIntent intent)
        {
            if (!Enum.IsDefined(typeof(LegacyConnectionReleaseIntent), intent))
            {
                throw new ArgumentOutOfRangeException(nameof(intent));
            }

            if (processLifecycleIsBlocked)
            {
                return LegacyConnectionReleaseAction.BlockUntilApplicationRestart;
            }

            if (!hasUnsavedProjectChanges || !adapterOwnsOrStartedHeadlessSession)
            {
                return LegacyConnectionReleaseAction.Release;
            }

            return intent == LegacyConnectionReleaseIntent.FinalDispose
                ? LegacyConnectionReleaseAction.PreserveForShutdown
                : LegacyConnectionReleaseAction.BlockReconnect;
        }

        internal static bool CanContinueCurrentSessionAfterBlockedReconnect(
            LegacyConnectionReleaseAction releaseAction,
            bool hasPortal,
            bool hasProject,
            bool hasPlcSoftware)
        {
            if (!Enum.IsDefined(typeof(LegacyConnectionReleaseAction), releaseAction))
            {
                throw new ArgumentOutOfRangeException(nameof(releaseAction));
            }

            return releaseAction == LegacyConnectionReleaseAction.BlockReconnect &&
                hasPortal &&
                hasProject &&
                hasPlcSoftware;
        }
    }

    internal enum LegacyTransactionFinalizationOutcome
    {
        RolledBack = 0,
        Committed = 1,
        Ambiguous = 2,
        RollbackFailed = 3
    }

    /// <summary>
    /// Classifies only outcomes that the transaction API has actually proved.
    /// Starting CommitOnDispose is intentionally treated as irreversible evidence:
    /// if that call or the following Dispose fails, the result is ambiguous.
    /// </summary>
    internal static class LegacyTransactionFinalizationPolicy
    {
        internal static LegacyTransactionFinalizationOutcome Evaluate(
            bool commitRequestStarted,
            bool commitRequestReturned,
            bool disposeReturned)
        {
            if (commitRequestReturned && !commitRequestStarted)
            {
                throw new ArgumentException(
                    "A commit request cannot return before it has started.",
                    nameof(commitRequestReturned));
            }

            if (commitRequestStarted)
            {
                return commitRequestReturned && disposeReturned
                    ? LegacyTransactionFinalizationOutcome.Committed
                    : LegacyTransactionFinalizationOutcome.Ambiguous;
            }

            return disposeReturned
                ? LegacyTransactionFinalizationOutcome.RolledBack
                : LegacyTransactionFinalizationOutcome.RollbackFailed;
        }

        internal static bool RequiresAmbiguityGuard(
            LegacyTransactionFinalizationOutcome outcome)
        {
            if (!Enum.IsDefined(typeof(LegacyTransactionFinalizationOutcome), outcome))
            {
                throw new ArgumentOutOfRangeException(nameof(outcome));
            }

            return outcome == LegacyTransactionFinalizationOutcome.Ambiguous ||
                outcome == LegacyTransactionFinalizationOutcome.RollbackFailed;
        }
    }

    /// <summary>
    /// Proves that save/reopen moved to a fresh adapter-owned Portal/project
    /// session instead of accidentally reading the still-open in-memory model.
    /// </summary>
    internal static class LegacyReopenVerificationPolicy
    {
        internal static bool IsFreshOwnedReopen(
            bool statusIsConnected,
            bool statusCanExport,
            bool statusHasErrors,
            object previousPortal,
            object currentPortal,
            object previousProject,
            object currentProject,
            bool targetSelected,
            bool portalStartedByAdapter,
            bool projectOpenedByAdapter,
            bool projectPathMatches)
        {
            return statusIsConnected &&
                statusCanExport &&
                !statusHasErrors &&
                previousPortal != null &&
                currentPortal != null &&
                !ReferenceEquals(previousPortal, currentPortal) &&
                previousProject != null &&
                currentProject != null &&
                !ReferenceEquals(previousProject, currentProject) &&
                targetSelected &&
                portalStartedByAdapter &&
                projectOpenedByAdapter &&
                projectPathMatches;
        }
    }

    /// <summary>
    /// Siemens-independent comparison policy for the save/reopen/export readback cycle.
    /// </summary>
    internal static class LegacyExportReadbackPolicy
    {
        internal static LegacyExportReadbackResult Verify(
            IEnumerable<LegacyExportReadbackObject> expectedObjects,
            IEnumerable<LegacyExportReadbackObject> actualObjects,
            IEnumerable<LegacyExportReadbackTag> expectedTags,
            IEnumerable<LegacyExportReadbackTag> actualTags,
            string ownershipMarker)
        {
            if (string.IsNullOrWhiteSpace(ownershipMarker))
            {
                throw new ArgumentException("An ownership marker is required.", nameof(ownershipMarker));
            }

            var expectedObjectList = Materialize(expectedObjects, nameof(expectedObjects));
            var actualObjectList = Materialize(actualObjects, nameof(actualObjects));
            var expectedTagList = Materialize(expectedTags, nameof(expectedTags));
            var actualTagList = Materialize(actualTags, nameof(actualTags));
            GuardReadbackCollectionSize(
                expectedObjectList.Count,
                LegacyLibraryReadbackPolicy.MaximumObjectCount,
                nameof(expectedObjects));
            GuardReadbackCollectionSize(
                actualObjectList.Count,
                LegacyLibraryReadbackPolicy.MaximumObjectCount,
                nameof(actualObjects));
            GuardReadbackCollectionSize(
                expectedTagList.Count,
                LegacyLibraryReadbackPolicy.MaximumTagCount,
                nameof(expectedTags));
            GuardReadbackCollectionSize(
                actualTagList.Count,
                LegacyLibraryReadbackPolicy.MaximumTagCount,
                nameof(actualTags));
            var actualObjectsByName = actualObjectList
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IList<LegacyExportReadbackObject>)group.ToList(),
                    StringComparer.OrdinalIgnoreCase);
            var actualTagsByTableAndName = actualTagList
                .GroupBy(item => item.TablePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    tableGroup => tableGroup.Key,
                    tableGroup => (IDictionary<string, IList<LegacyExportReadbackTag>>)
                        tableGroup
                            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(
                                nameGroup => nameGroup.Key,
                                nameGroup => (IList<LegacyExportReadbackTag>)nameGroup.ToList(),
                                StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
            var issues = new List<LegacyExportReadbackIssue>();

            foreach (var expected in expectedObjectList
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.Ordinal))
            {
                VerifyObject(expected, actualObjectsByName, ownershipMarker, issues);
            }

            foreach (var expected in expectedTagList
                .OrderBy(item => item.TablePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TablePath, StringComparer.Ordinal)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.Ordinal))
            {
                VerifyTag(expected, actualTagsByTableAndName, issues);
            }

            return new LegacyExportReadbackResult(issues
                .OrderBy(issue => issue.Location, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.Location, StringComparer.Ordinal)
                .ThenBy(issue => issue.Message, StringComparer.Ordinal)
                .ToArray());
        }

        private static void VerifyObject(
            LegacyExportReadbackObject expected,
            IDictionary<string, IList<LegacyExportReadbackObject>> actualObjectsByName,
            string ownershipMarker,
            ICollection<LegacyExportReadbackIssue> issues)
        {
            var location = "OBJECT/" + expected.Kind + "/" + expected.Name;
            IList<LegacyExportReadbackObject> sameName;
            if (!actualObjectsByName.TryGetValue(expected.Name, out sameName))
            {
                issues.Add(new LegacyExportReadbackIssue(
                    location,
                    "Expected " + expected.Kind + " '" + expected.Name + "' is missing after reopen."));
                return;
            }

            if (sameName.Count != 1)
            {
                var kinds = sameName
                    .Select(actual => actual.Kind)
                    .OrderBy(kind => kind)
                    .Select(kind => kind.ToString())
                    .ToArray();
                issues.Add(new LegacyExportReadbackIssue(
                    location,
                    "Expected exactly one PLC object named '" + expected.Name +
                    "' after reopen, but found " + sameName.Count +
                    " (" + string.Join(", ", kinds) + ")."));
                return;
            }

            var actualMatch = sameName[0];
            if (actualMatch.Kind != expected.Kind)
            {
                issues.Add(new LegacyExportReadbackIssue(
                    location,
                    "Expected " + expected.Kind + " '" + expected.Name +
                    "' was read back with the wrong kind: " + actualMatch.Kind + "."));
                return;
            }

            if (expected.Kind != LegacyExportReadbackObjectKind.DataType &&
                !StringComparer.Ordinal.Equals(actualMatch.OwnershipMarker, ownershipMarker))
            {
                issues.Add(new LegacyExportReadbackIssue(
                    location,
                    "The read-back " + expected.Kind + " '" + expected.Name +
                    "' does not have the exact expected ownership marker."));
            }
        }

        private static void VerifyTag(
            LegacyExportReadbackTag expected,
            IDictionary<string, IDictionary<string, IList<LegacyExportReadbackTag>>>
                actualTagsByTableAndName,
            ICollection<LegacyExportReadbackIssue> issues)
        {
            var location = "TAG/" + expected.TablePath + "/" + expected.Name;
            IDictionary<string, IList<LegacyExportReadbackTag>> tagsByName;
            IList<LegacyExportReadbackTag> matches;
            if (!actualTagsByTableAndName.TryGetValue(expected.TablePath, out tagsByName) ||
                !tagsByName.TryGetValue(expected.Name, out matches))
            {
                issues.Add(new LegacyExportReadbackIssue(
                    location,
                    "Expected PLC tag '" + expected.Name + "' in table '" + expected.TablePath +
                    "' is missing after reopen."));
                return;
            }

            if (matches.Count != 1)
            {
                issues.Add(new LegacyExportReadbackIssue(
                    location,
                    "Expected exactly one PLC tag '" + expected.Name + "' in table '" +
                    expected.TablePath + "' after reopen, but found " + matches.Count + "."));
                return;
            }

            var actualMatch = matches[0];
            if (!StringComparer.OrdinalIgnoreCase.Equals(actualMatch.DataType, expected.DataType))
            {
                issues.Add(new LegacyExportReadbackIssue(
                    location,
                    "PLC tag '" + expected.Name + "' has data type '" + actualMatch.DataType +
                    "' after reopen; expected '" + expected.DataType + "'."));
            }

            if (!StringComparer.OrdinalIgnoreCase.Equals(actualMatch.Address, expected.Address))
            {
                issues.Add(new LegacyExportReadbackIssue(
                    location,
                    "PLC tag '" + expected.Name + "' has address '" + actualMatch.Address +
                    "' after reopen; expected '" + expected.Address + "'."));
            }

            if (!StringComparer.Ordinal.Equals(actualMatch.Comment, expected.Comment))
            {
                issues.Add(new LegacyExportReadbackIssue(
                    location,
                    "PLC tag '" + expected.Name +
                    "' has a different editing/reference-language comment after reopen."));
            }
        }

        private static IList<T> Materialize<T>(IEnumerable<T> values, string parameterName)
            where T : class
        {
            if (values == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var result = values.ToArray();
            if (result.Any(value => value == null))
            {
                throw new ArgumentException("The collection cannot contain null items.", parameterName);
            }

            return result;
        }

        private static void GuardReadbackCollectionSize(
            int count,
            int maximumCount,
            string parameterName)
        {
            if (count > maximumCount)
            {
                throw new InvalidOperationException(
                    parameterName + " exceeds the configured readback safety limit of " +
                    maximumCount + " items.");
            }
        }
    }

    internal enum LegacyExportReadbackObjectKind
    {
        DataType = 0,
        FunctionBlock = 1,
        Function = 2,
        DataBlock = 3
    }

    internal sealed class LegacyExportReadbackObject
    {
        internal LegacyExportReadbackObject(
            LegacyExportReadbackObjectKind kind,
            string name,
            string ownershipMarker = null)
        {
            if (!Enum.IsDefined(typeof(LegacyExportReadbackObjectKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("An object name is required.", nameof(name));
            }

            Kind = kind;
            Name = name;
            OwnershipMarker = ownershipMarker ?? string.Empty;
        }

        internal LegacyExportReadbackObjectKind Kind { get; }

        internal string Name { get; }

        internal string OwnershipMarker { get; }
    }

    internal sealed class LegacyExportReadbackTag
    {
        internal LegacyExportReadbackTag(
            string tablePath,
            string name,
            string dataType,
            string address,
            string comment = null)
        {
            if (string.IsNullOrWhiteSpace(tablePath))
            {
                throw new ArgumentException("A PLC-tag table path is required.", nameof(tablePath));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A PLC-tag name is required.", nameof(name));
            }

            TablePath = tablePath;
            Name = name;
            DataType = dataType ?? string.Empty;
            Address = address ?? string.Empty;
            Comment = comment ?? string.Empty;
        }

        internal string TablePath { get; }

        internal string Name { get; }

        internal string DataType { get; }

        internal string Address { get; }

        internal string Comment { get; }
    }

    internal sealed class LegacyExportReadbackIssue
    {
        internal LegacyExportReadbackIssue(string location, string message)
        {
            Location = location ?? string.Empty;
            Message = message ?? string.Empty;
        }

        internal string Location { get; }

        internal string Message { get; }
    }

    internal sealed class LegacyExportReadbackResult
    {
        internal LegacyExportReadbackResult(IEnumerable<LegacyExportReadbackIssue> issues)
        {
            if (issues == null)
            {
                throw new ArgumentNullException(nameof(issues));
            }

            Issues = new ReadOnlyCollection<LegacyExportReadbackIssue>(issues.ToArray());
        }

        internal bool IsSuccess => Issues.Count == 0;

        internal IReadOnlyList<LegacyExportReadbackIssue> Issues { get; }
    }
}
