using System;
using System.Linq;
using TiaSclStudio.Openness.Legacy.V17;
using Xunit;

namespace TiaSclStudio.Openness.Tests
{
    public sealed class LegacyExportReadbackPolicyTests
    {
        private const string Marker = "TIA_SCL_STUDIO:ABCD";

        [Fact]
        public void AcceptsExactReadbackAndIgnoresCaseAndUnrelatedItems()
        {
            var result = LegacyExportReadbackPolicy.Verify(
                new[]
                {
                    Object(LegacyExportReadbackObjectKind.DataType, "MotorData"),
                    Object(LegacyExportReadbackObjectKind.FunctionBlock, "FB_Motor"),
                    Object(LegacyExportReadbackObjectKind.Function, "FC_Scale"),
                    Object(LegacyExportReadbackObjectKind.DataBlock, "DB_Settings")
                },
                new[]
                {
                    Object(LegacyExportReadbackObjectKind.Function, "fc_scale", Marker),
                    Object(LegacyExportReadbackObjectKind.DataType, "MOTORDATA", "ignored"),
                    Object(LegacyExportReadbackObjectKind.FunctionBlock, "fb_motor", Marker),
                    Object(LegacyExportReadbackObjectKind.DataBlock, "db_settings", Marker),
                    Object(LegacyExportReadbackObjectKind.FunctionBlock, "Unrelated", "foreign")
                },
                new[] { Tag("PLC/Main", "Motor_On", "Bool", "%Q0.0") },
                new[]
                {
                    Tag("plc/main", "motor_on", "BOOL", "%q0.0"),
                    Tag("PLC/Other", "Unrelated", "Int", "%MW20")
                },
                Marker);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Issues);
        }

        [Fact]
        public void ReportsMissingAndWrongKindObjects()
        {
            var result = LegacyExportReadbackPolicy.Verify(
                new[]
                {
                    Object(LegacyExportReadbackObjectKind.FunctionBlock, "Missing"),
                    Object(LegacyExportReadbackObjectKind.Function, "WrongKind")
                },
                new[]
                {
                    Object(LegacyExportReadbackObjectKind.DataBlock, "wrongkind", Marker)
                },
                NoTags,
                NoTags,
                Marker);

            Assert.False(result.IsSuccess);
            Assert.Equal(2, result.Issues.Count);
            Assert.Contains(result.Issues, issue =>
                issue.Location == "OBJECT/FunctionBlock/Missing" &&
                issue.Message.Contains("missing after reopen"));
            Assert.Contains(result.Issues, issue =>
                issue.Location == "OBJECT/Function/WrongKind" &&
                issue.Message.Contains("wrong kind: DataBlock"));
        }

        [Fact]
        public void ReportsDuplicateActualObjectCaseInsensitively()
        {
            var result = LegacyExportReadbackPolicy.Verify(
                new[] { Object(LegacyExportReadbackObjectKind.FunctionBlock, "FB_Valve") },
                new[]
                {
                    Object(LegacyExportReadbackObjectKind.FunctionBlock, "FB_Valve", Marker),
                    Object(LegacyExportReadbackObjectKind.FunctionBlock, "fb_valve", Marker)
                },
                NoTags,
                NoTags,
                Marker);

            var issue = Assert.Single(result.Issues);
            Assert.Equal("OBJECT/FunctionBlock/FB_Valve", issue.Location);
            Assert.Contains("found 2", issue.Message);
        }

        [Fact]
        public void ReportsMixedKindObjectsWithTheSameNameAsAmbiguous()
        {
            var result = LegacyExportReadbackPolicy.Verify(
                new[] { Object(LegacyExportReadbackObjectKind.FunctionBlock, "Shared") },
                new[]
                {
                    Object(LegacyExportReadbackObjectKind.FunctionBlock, "Shared", Marker),
                    Object(LegacyExportReadbackObjectKind.DataType, "shared")
                },
                NoTags,
                NoTags,
                Marker);

            var issue = Assert.Single(result.Issues);
            Assert.Contains("found 2", issue.Message);
            Assert.Contains("DataType", issue.Message);
            Assert.Contains("FunctionBlock", issue.Message);
        }

        [Theory]
        [InlineData((int)LegacyExportReadbackObjectKind.FunctionBlock)]
        [InlineData((int)LegacyExportReadbackObjectKind.Function)]
        [InlineData((int)LegacyExportReadbackObjectKind.DataBlock)]
        public void BlockOwnershipMarkerIsCaseSensitive(int kindValue)
        {
            var kind = (LegacyExportReadbackObjectKind)kindValue;
            var result = LegacyExportReadbackPolicy.Verify(
                new[] { Object(kind, "Owned") },
                new[] { Object(kind, "Owned", Marker.ToLowerInvariant()) },
                NoTags,
                NoTags,
                Marker);

            var issue = Assert.Single(result.Issues);
            Assert.Contains("exact expected ownership marker", issue.Message);
        }

        [Fact]
        public void DataTypeDoesNotRequireAnOwnershipMarker()
        {
            var result = LegacyExportReadbackPolicy.Verify(
                new[] { Object(LegacyExportReadbackObjectKind.DataType, "Recipe") },
                new[] { Object(LegacyExportReadbackObjectKind.DataType, "Recipe", "foreign") },
                NoTags,
                NoTags,
                Marker);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void TagMustExistExactlyOnceInTheExpectedTable()
        {
            var missing = LegacyExportReadbackPolicy.Verify(
                NoObjects,
                NoObjects,
                new[] { Tag("PLC/Main", "Start", "Bool", "%I0.0") },
                new[] { Tag("PLC/Other", "Start", "Bool", "%I0.0") },
                Marker);
            var duplicated = LegacyExportReadbackPolicy.Verify(
                NoObjects,
                NoObjects,
                new[] { Tag("PLC/Main", "Start", "Bool", "%I0.0") },
                new[]
                {
                    Tag("PLC/Main", "Start", "Bool", "%I0.0"),
                    Tag("plc/main", "start", "BOOL", "%i0.0")
                },
                Marker);

            Assert.Contains("missing after reopen", Assert.Single(missing.Issues).Message);
            Assert.Contains("found 2", Assert.Single(duplicated.Issues).Message);
        }

        [Fact]
        public void TagDataTypeAndAddressAreComparedCaseInsensitivelyButExactly()
        {
            var accepted = LegacyExportReadbackPolicy.Verify(
                NoObjects,
                NoObjects,
                new[] { Tag("PLC/Main", "Value", "Word", "%QW64") },
                new[] { Tag("plc/main", "value", "WORD", "%qw64") },
                Marker);
            var rejected = LegacyExportReadbackPolicy.Verify(
                NoObjects,
                NoObjects,
                new[] { Tag("PLC/Main", "Value", "Word", "%QW64") },
                new[] { Tag("PLC/Main", "Value", "Int", "%QW66") },
                Marker);

            Assert.True(accepted.IsSuccess);
            Assert.Equal(2, rejected.Issues.Count);
            Assert.Contains(rejected.Issues, issue => issue.Message.Contains("data type 'Int'"));
            Assert.Contains(rejected.Issues, issue => issue.Message.Contains("address '%QW66'"));
        }

        [Fact]
        public void TagCommentIsComparedExactlyInTheWriteLanguage()
        {
            var accepted = LegacyExportReadbackPolicy.Verify(
                NoObjects,
                NoObjects,
                new[] { Tag("PLC/Main", "Start", "Bool", "%I0.0", "Start command") },
                new[] { Tag("plc/main", "start", "BOOL", "%i0.0", "Start command") },
                Marker);
            var rejected = LegacyExportReadbackPolicy.Verify(
                NoObjects,
                NoObjects,
                new[] { Tag("PLC/Main", "Start", "Bool", "%I0.0", "Start command") },
                new[] { Tag("PLC/Main", "Start", "Bool", "%I0.0", "START COMMAND") },
                Marker);

            Assert.True(accepted.IsSuccess);
            var issue = Assert.Single(rejected.Issues);
            Assert.Contains("different editing/reference-language comment", issue.Message);
        }

        [Fact]
        public void IssueOrderingDoesNotDependOnEnumerationOrder()
        {
            var expectedObjects = new[]
            {
                Object(LegacyExportReadbackObjectKind.Function, "Zulu"),
                Object(LegacyExportReadbackObjectKind.FunctionBlock, "Alpha")
            };
            var expectedTags = new[]
            {
                Tag("PLC/Zulu", "TagB", "Bool", "%M0.1"),
                Tag("PLC/Alpha", "TagA", "Bool", "%M0.0")
            };

            var first = LegacyExportReadbackPolicy.Verify(
                expectedObjects,
                NoObjects,
                expectedTags,
                NoTags,
                Marker);
            var second = LegacyExportReadbackPolicy.Verify(
                expectedObjects.Reverse(),
                NoObjects,
                expectedTags.Reverse(),
                NoTags,
                Marker);

            Assert.Equal(
                first.Issues.Select(issue => issue.Location + "|" + issue.Message),
                second.Issues.Select(issue => issue.Location + "|" + issue.Message));
        }

        [Fact]
        public void RejectsMissingMarkerCollectionsAndNullItems()
        {
            Assert.Throws<ArgumentException>(() => LegacyExportReadbackPolicy.Verify(
                NoObjects,
                NoObjects,
                NoTags,
                NoTags,
                " "));
            Assert.Throws<ArgumentNullException>(() => LegacyExportReadbackPolicy.Verify(
                null,
                NoObjects,
                NoTags,
                NoTags,
                Marker));
            Assert.Throws<ArgumentException>(() => LegacyExportReadbackPolicy.Verify(
                new LegacyExportReadbackObject[] { null },
                NoObjects,
                NoTags,
                NoTags,
                Marker));
        }

        [Fact]
        public void RejectsReadbackCollectionsBeyondTheSafetyEnvelope()
        {
            var tooManyObjects = Enumerable.Range(
                    0,
                    LegacyLibraryReadbackPolicy.MaximumObjectCount + 1)
                .Select(index => Object(
                    LegacyExportReadbackObjectKind.Function,
                    "FC_" + index))
                .ToArray();
            var tooManyTags = Enumerable.Range(
                    0,
                    LegacyLibraryReadbackPolicy.MaximumTagCount + 1)
                .Select(index => Tag(
                    "PLC/Main",
                    "Tag_" + index,
                    "Bool",
                    "%M" + index + ".0"))
                .ToArray();

            Assert.Throws<InvalidOperationException>(() =>
                LegacyExportReadbackPolicy.Verify(
                    tooManyObjects,
                    NoObjects,
                    NoTags,
                    NoTags,
                    Marker));
            Assert.Throws<InvalidOperationException>(() =>
                LegacyExportReadbackPolicy.Verify(
                    NoObjects,
                    NoObjects,
                    tooManyTags,
                    NoTags,
                    Marker));
        }

        private static readonly LegacyExportReadbackObject[] NoObjects =
            new LegacyExportReadbackObject[0];

        private static readonly LegacyExportReadbackTag[] NoTags =
            new LegacyExportReadbackTag[0];

        private static LegacyExportReadbackObject Object(
            LegacyExportReadbackObjectKind kind,
            string name,
            string marker = null)
        {
            return new LegacyExportReadbackObject(kind, name, marker);
        }

        private static LegacyExportReadbackTag Tag(
            string tablePath,
            string name,
            string dataType,
            string address,
            string comment = null)
        {
            return new LegacyExportReadbackTag(tablePath, name, dataType, address, comment);
        }
    }

    public sealed class LegacyUnsavedEvidencePolicyTests
    {
        [Fact]
        public void ImmediateCleanObservationDoesNotClearAnUnobservedCommit()
        {
            var decision = LegacyUnsavedEvidencePolicy.Evaluate(
                true,
                false,
                LegacyProjectModifiedObservation.Clean);

            Assert.True(decision.HasCommittedChanges);
            Assert.False(decision.DirtyWasObserved);
            Assert.True(decision.HasUnsavedChanges);
        }

        [Fact]
        public void DirtyThenCleanClearsCommittedEvidence()
        {
            var dirty = LegacyUnsavedEvidencePolicy.Evaluate(
                true,
                false,
                LegacyProjectModifiedObservation.Dirty);
            var clean = LegacyUnsavedEvidencePolicy.Evaluate(
                dirty.HasCommittedChanges,
                dirty.DirtyWasObserved,
                LegacyProjectModifiedObservation.Clean);

            Assert.True(dirty.HasUnsavedChanges);
            Assert.True(dirty.DirtyWasObserved);
            Assert.False(clean.HasCommittedChanges);
            Assert.False(clean.DirtyWasObserved);
            Assert.False(clean.HasUnsavedChanges);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UnknownProjectStateIsAlwaysUnsafe(bool hasCommittedChanges)
        {
            var decision = LegacyUnsavedEvidencePolicy.Evaluate(
                hasCommittedChanges,
                false,
                LegacyProjectModifiedObservation.Unknown);

            Assert.Equal(hasCommittedChanges, decision.HasCommittedChanges);
            Assert.True(decision.HasUnsavedChanges);
        }

        [Fact]
        public void UnrelatedDirtyProjectIsUnsafeWithoutInventingCommitEvidence()
        {
            var decision = LegacyUnsavedEvidencePolicy.Evaluate(
                false,
                false,
                LegacyProjectModifiedObservation.Dirty);

            Assert.False(decision.HasCommittedChanges);
            Assert.False(decision.DirtyWasObserved);
            Assert.True(decision.HasUnsavedChanges);
        }

        [Fact]
        public void RejectsUnknownObservationEnumValue()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LegacyUnsavedEvidencePolicy.Evaluate(
                    false,
                    false,
                    (LegacyProjectModifiedObservation)999));
        }
    }

    public sealed class LegacyConnectionReleasePolicyTests
    {
        [Fact]
        public void PriorLifecycleFaultBlocksEveryNewSessionUntilRestart()
        {
            Assert.Equal(
                LegacyConnectionReleaseAction.BlockUntilApplicationRestart,
                LegacyConnectionReleasePolicy.Decide(
                    true,
                    false,
                    false,
                    LegacyConnectionReleaseIntent.Reconnect));
        }

        [Fact]
        public void UnsavedOwnedOrHeadlessSessionBlocksReconnect()
        {
            Assert.Equal(
                LegacyConnectionReleaseAction.BlockReconnect,
                LegacyConnectionReleasePolicy.Decide(
                    false,
                    true,
                    true,
                    LegacyConnectionReleaseIntent.Reconnect));
        }

        [Fact]
        public void FinalDisposePreservesAnUnsavedOwnedOrHeadlessSession()
        {
            Assert.Equal(
                LegacyConnectionReleaseAction.PreserveForShutdown,
                LegacyConnectionReleasePolicy.Decide(
                    false,
                    true,
                    true,
                    LegacyConnectionReleaseIntent.FinalDispose));
        }

        [Fact]
        public void BlockedReconnectKeepsACompleteCurrentTargetAvailableForRecovery()
        {
            Assert.True(
                LegacyConnectionReleasePolicy.CanContinueCurrentSessionAfterBlockedReconnect(
                    LegacyConnectionReleaseAction.BlockReconnect,
                    true,
                    true,
                    true));
        }

        [Theory]
        [InlineData((int)LegacyConnectionReleaseAction.Release, true, true, true)]
        [InlineData((int)LegacyConnectionReleaseAction.PreserveForShutdown, true, true, true)]
        [InlineData((int)LegacyConnectionReleaseAction.BlockUntilApplicationRestart, true, true, true)]
        [InlineData((int)LegacyConnectionReleaseAction.BlockReconnect, false, true, true)]
        [InlineData((int)LegacyConnectionReleaseAction.BlockReconnect, true, false, true)]
        [InlineData((int)LegacyConnectionReleaseAction.BlockReconnect, true, true, false)]
        public void RecoveryIsUnavailableForAnyOtherReleaseOutcomeOrIncompleteTarget(
            int releaseAction,
            bool hasPortal,
            bool hasProject,
            bool hasPlcSoftware)
        {
            Assert.False(
                LegacyConnectionReleasePolicy.CanContinueCurrentSessionAfterBlockedReconnect(
                    (LegacyConnectionReleaseAction)releaseAction,
                    hasPortal,
                    hasProject,
                    hasPlcSoftware));
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void CleanOrForeignInteractiveConnectionCanRelease(
            bool hasUnsavedChanges,
            bool ownsOrStartedHeadless)
        {
            Assert.Equal(
                LegacyConnectionReleaseAction.Release,
                LegacyConnectionReleasePolicy.Decide(
                    false,
                    hasUnsavedChanges,
                    ownsOrStartedHeadless,
                    LegacyConnectionReleaseIntent.Reconnect));
        }

        [Fact]
        public void RejectsUnknownIntent()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LegacyConnectionReleasePolicy.Decide(false, false, false, (LegacyConnectionReleaseIntent)999));
        }

        [Fact]
        public void RecoveryAvailabilityRejectsUnknownReleaseAction()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LegacyConnectionReleasePolicy.CanContinueCurrentSessionAfterBlockedReconnect(
                    (LegacyConnectionReleaseAction)999,
                    true,
                    true,
                    true));
        }
    }

    public sealed class LegacyTransactionFinalizationPolicyTests
    {
        [Theory]
        [InlineData(false, false, true, (int)LegacyTransactionFinalizationOutcome.RolledBack)]
        [InlineData(false, false, false, (int)LegacyTransactionFinalizationOutcome.RollbackFailed)]
        [InlineData(true, false, true, (int)LegacyTransactionFinalizationOutcome.Ambiguous)]
        [InlineData(true, false, false, (int)LegacyTransactionFinalizationOutcome.Ambiguous)]
        [InlineData(true, true, false, (int)LegacyTransactionFinalizationOutcome.Ambiguous)]
        [InlineData(true, true, true, (int)LegacyTransactionFinalizationOutcome.Committed)]
        public void ClassifiesOnlyProvenCommitAndRollbackOutcomes(
            bool commitRequestStarted,
            bool commitRequestReturned,
            bool disposeReturned,
            int expectedValue)
        {
            var expected = (LegacyTransactionFinalizationOutcome)expectedValue;
            var actual = LegacyTransactionFinalizationPolicy.Evaluate(
                commitRequestStarted,
                commitRequestReturned,
                disposeReturned);

            Assert.Equal(expected, actual);
            Assert.Equal(
                expected == LegacyTransactionFinalizationOutcome.Ambiguous ||
                expected == LegacyTransactionFinalizationOutcome.RollbackFailed,
                LegacyTransactionFinalizationPolicy.RequiresAmbiguityGuard(actual));
        }

        [Fact]
        public void RejectsImpossibleAndUnknownFinalizationStates()
        {
            Assert.Throws<ArgumentException>(() =>
                LegacyTransactionFinalizationPolicy.Evaluate(false, true, true));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LegacyTransactionFinalizationPolicy.RequiresAmbiguityGuard(
                    (LegacyTransactionFinalizationOutcome)999));
        }
    }

    public sealed class LegacyReopenVerificationPolicyTests
    {
        [Fact]
        public void AcceptsOnlyAFreshOwnedErrorFreeReopen()
        {
            var previousPortal = new object();
            var currentPortal = new object();
            var previousProject = new object();
            var currentProject = new object();

            Assert.True(LegacyReopenVerificationPolicy.IsFreshOwnedReopen(
                true,
                true,
                false,
                previousPortal,
                currentPortal,
                previousProject,
                currentProject,
                true,
                true,
                true,
                true));
        }

        [Fact]
        public void RejectsTheOldInMemorySessionEvenWhenItsStatusCanExport()
        {
            var portal = new object();
            var project = new object();

            Assert.False(LegacyReopenVerificationPolicy.IsFreshOwnedReopen(
                true,
                true,
                false,
                portal,
                portal,
                project,
                project,
                true,
                true,
                true,
                true));
        }

        [Fact]
        public void RejectsErrorsForeignOwnershipMissingTargetAndWrongPath()
        {
            var previousPortal = new object();
            var currentPortal = new object();
            var previousProject = new object();
            var currentProject = new object();

            Assert.False(Verify(false, true, false, true, true, true));
            Assert.False(Verify(true, false, false, true, true, true));
            Assert.False(Verify(true, true, true, true, true, true));
            Assert.False(Verify(true, true, false, false, true, true));
            Assert.False(Verify(true, true, false, true, false, true));
            Assert.False(Verify(true, true, false, true, true, false));

            bool Verify(
                bool isConnected,
                bool canExport,
                bool hasErrors,
                bool targetSelected,
                bool adapterOwned,
                bool pathMatches)
            {
                return LegacyReopenVerificationPolicy.IsFreshOwnedReopen(
                    isConnected,
                    canExport,
                    hasErrors,
                    previousPortal,
                    currentPortal,
                    previousProject,
                    currentProject,
                    targetSelected,
                    adapterOwned,
                    adapterOwned,
                    pathMatches);
            }
        }
    }
}
