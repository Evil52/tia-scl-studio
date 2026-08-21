using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;
using TiaSclStudio.Openness.Legacy.V17;
using Xunit;

namespace TiaSclStudio.Openness.Tests
{
    public sealed class LegacyLibraryReadbackPolicyTests
    {
        [Fact]
        public void WalksRootAndNestedGroupsDepthFirstWithRelativePaths()
        {
            var root = new FakeGroup("Program blocks", "RootBlock");
            var drives = new FakeGroup("Drives", "DriveA");
            drives.Children.Add(new FakeGroup("Safety", "SafeDrive"));
            root.Children.Add(drives);
            root.Children.Add(new FakeGroup("Utilities", "Scale"));

            var entries = LegacyLibraryReadbackPolicy.WalkGroupTree(
                    root,
                    group => group.Items,
                    group => group.Children,
                    group => group.Name)
                .Select(entry => entry.GroupPath + "|" + entry.Item)
                .ToArray();

            Assert.Equal(
                new[]
                {
                    "|RootBlock",
                    "Drives|DriveA",
                    "Drives/Safety|SafeDrive",
                    "Utilities|Scale"
                },
                entries);
        }

        [Fact]
        public void WalkTreatsNullItemAndChildCollectionsAsEmpty()
        {
            var root = new FakeGroup("Root");
            root.Items = null;
            root.Children = null;

            Assert.Empty(LegacyLibraryReadbackPolicy.WalkGroupTree(
                root,
                group => group.Items,
                group => group.Children,
                group => group.Name));
        }

        [Fact]
        public void SourceExtensionsMatchTheSiemensGenerateSourceContract()
        {
            Assert.Equal(".udt", LegacyLibraryReadbackPolicy.GetSourceExtension(
                TiaLibrarySourceKind.DataType));
            Assert.Equal(".scl", LegacyLibraryReadbackPolicy.GetSourceExtension(
                TiaLibrarySourceKind.FunctionBlock));
            Assert.Equal(".scl", LegacyLibraryReadbackPolicy.GetSourceExtension(
                TiaLibrarySourceKind.Function));
        }

        [Fact]
        public void BlockKindsShareOneCollisionNamespaceButUdtsDoNot()
        {
            var fb = LegacyLibraryReadbackPolicy.GetCollisionKey(
                TiaLibrarySourceKind.FunctionBlock,
                "Control");
            var fc = LegacyLibraryReadbackPolicy.GetCollisionKey(
                TiaLibrarySourceKind.Function,
                "Control");
            var udt = LegacyLibraryReadbackPolicy.GetCollisionKey(
                TiaLibrarySourceKind.DataType,
                "Control");

            Assert.Equal(fb, fc);
            Assert.NotEqual(fb, udt);
        }

        [Fact]
        public void SafetyBoundsMatchTheImporterEnvelope()
        {
            Assert.Equal(4096, LegacyLibraryReadbackPolicy.MaximumObjectCount);
            Assert.Equal(8L * 1024L * 1024L, LegacyLibraryReadbackPolicy.MaximumSourceBytesPerObject);
            Assert.True(LegacyLibraryReadbackPolicy.MaximumTotalSourceBytes >=
                LegacyLibraryReadbackPolicy.MaximumSourceBytesPerObject);
        }

        [Fact]
        public void TagTablePathsHaveExplicitScopesAndEscapedSegments()
        {
            var escapedGroup = LegacyLibraryReadbackPolicy.EscapeStablePathSegment("Area%/One");
            var root = LegacyLibraryReadbackPolicy.BuildTagTablePath(
                LegacyLibraryReadbackPolicy.GetRootTagScopePath(),
                escapedGroup,
                "Main/Tags");
            var unit = LegacyLibraryReadbackPolicy.BuildTagTablePath(
                LegacyLibraryReadbackPolicy.GetUnitTagScopePath("Unit/One"),
                escapedGroup,
                "Main/Tags");

            Assert.Equal("PLC/Area%25%2FOne/Main%2FTags", root);
            Assert.Equal("UNIT/Unit%2FOne/Area%25%2FOne/Main%2FTags", unit);
            Assert.NotEqual(root, unit);
        }

        [Fact]
        public void EscapingMakesSlashAndLiteralEscapeTextDistinct()
        {
            Assert.Equal("A%2FB", LegacyLibraryReadbackPolicy.EscapeStablePathSegment("A/B"));
            Assert.Equal("A%252FB", LegacyLibraryReadbackPolicy.EscapeStablePathSegment("A%2FB"));
        }

        [Fact]
        public void TagCatalogSafetyBoundsAreFiniteAndOrdered()
        {
            Assert.Equal(4096, LegacyLibraryReadbackPolicy.MaximumTagTableCount);
            Assert.True(LegacyLibraryReadbackPolicy.MaximumTagGroupCount >=
                LegacyLibraryReadbackPolicy.MaximumTagTableCount);
            Assert.True(LegacyLibraryReadbackPolicy.MaximumTagGroupDepth > 0);
            Assert.Equal(65536, LegacyLibraryReadbackPolicy.MaximumTagCount);
            Assert.True(LegacyLibraryReadbackPolicy.MaximumTagFieldCharacters > 0);
            Assert.True(LegacyLibraryReadbackPolicy.MaximumTagCatalogCharacters >=
                LegacyLibraryReadbackPolicy.MaximumTagFieldCharacters);
            Assert.True(LegacyLibraryReadbackPolicy.MaximumTagTopologyCharacters > 0);
            Assert.Equal(256, LegacyLibraryReadbackPolicy.MaximumTagDiagnosticCount);
            Assert.Equal(4096, LegacyLibraryReadbackPolicy.MaximumDiagnosticTextCharacters);
        }

        [Fact]
        public void TagTopologySignatureIsOrderIndependentButTracksScopesTablesAndTimestamps()
        {
            var tables = new[]
            {
                new KeyValuePair<string, long>("PLC/Main", 11),
                new KeyValuePair<string, long>("UNIT/Packaging/Commands", 22)
            };
            var baseline = LegacyLibraryReadbackPolicy.BuildTagTopologySignature(
                new[] { "PLC", "UNIT/Packaging" },
                tables);
            var reordered = LegacyLibraryReadbackPolicy.BuildTagTopologySignature(
                new[] { "UNIT/Packaging", "PLC" },
                tables.Reverse());
            var changedTimestamp = LegacyLibraryReadbackPolicy.BuildTagTopologySignature(
                new[] { "PLC", "UNIT/Packaging" },
                new[]
                {
                    new KeyValuePair<string, long>("PLC/Main", 12),
                    new KeyValuePair<string, long>("UNIT/Packaging/Commands", 22)
                });
            var emptyUnitRemoved = LegacyLibraryReadbackPolicy.BuildTagTopologySignature(
                new[] { "PLC" },
                tables);

            Assert.Equal(baseline, reordered);
            Assert.NotEqual(baseline, changedTimestamp);
            Assert.NotEqual(baseline, emptyUnitRemoved);
        }

        [Fact]
        public void TagTopologySignaturePreservesDuplicateCompositionEntries()
        {
            var once = LegacyLibraryReadbackPolicy.BuildTagTopologySignature(
                new[] { "PLC" },
                new[] { new KeyValuePair<string, long>("PLC/Main", 1) });
            var twice = LegacyLibraryReadbackPolicy.BuildTagTopologySignature(
                new[] { "PLC" },
                new[]
                {
                    new KeyValuePair<string, long>("PLC/Main", 1),
                    new KeyValuePair<string, long>("PLC/Main", 1)
                });

            Assert.NotEqual(once, twice);
        }

        [Fact]
        public void DiagnosticCollectorCapsCountAndTextAndReportsSuppressedSeverity()
        {
            var diagnostics = new BoundedDiagnosticCollection(
                LegacyLibraryReadbackPolicy.MaximumTagDiagnosticCount,
                OpennessDiagnosticCodes.PlcTagCatalogDiagnosticsSuppressed,
                new string('L', 5000));
            for (var index = 0; index < 300; index++)
            {
                diagnostics.Add(new OpennessDiagnostic(
                    "TEST_" + index,
                    index == 299 ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                    new string('M', 5000),
                    new string('L', 5000),
                    new string('E', 5000)));
            }

            var sealedDiagnostics = diagnostics.Seal();

            Assert.Equal(LegacyLibraryReadbackPolicy.MaximumTagDiagnosticCount, sealedDiagnostics.Count);
            Assert.All(sealedDiagnostics, diagnostic =>
            {
                Assert.True(diagnostic.Message.Length <=
                    LegacyLibraryReadbackPolicy.MaximumDiagnosticTextCharacters);
                Assert.True(diagnostic.Location.Length <=
                    LegacyLibraryReadbackPolicy.MaximumDiagnosticTextCharacters);
                Assert.True(diagnostic.ExceptionType.Length <=
                    LegacyLibraryReadbackPolicy.MaximumDiagnosticTextCharacters);
            });
            Assert.Equal(
                OpennessDiagnosticCodes.PlcTagCatalogDiagnosticsSuppressed,
                sealedDiagnostics.Last().Code);
            Assert.Equal(DiagnosticSeverity.Error, sealedDiagnostics.Last().Severity);
            Assert.Same(sealedDiagnostics, diagnostics.Seal());
        }

        [Fact]
        public void HardwareTraversalSafetyBoundsAreFinite()
        {
            Assert.True(LegacyLibraryReadbackPolicy.MaximumHardwareTreeDepth > 0);
            Assert.True(LegacyLibraryReadbackPolicy.MaximumHardwareGroupCount > 0);
            Assert.True(LegacyLibraryReadbackPolicy.MaximumHardwareDeviceCount > 0);
            Assert.True(LegacyLibraryReadbackPolicy.MaximumHardwareDeviceItemCount > 0);
            Assert.True(LegacyLibraryReadbackPolicy.MaximumHardwareRegisteredAddressCount > 0);
            Assert.True(LegacyLibraryReadbackPolicy.MaximumHardwareChannelCount > 0);
            Assert.True(LegacyLibraryReadbackPolicy.MaximumHardwarePathCharacters > 0);
            Assert.Equal(256, LegacyLibraryReadbackPolicy.MaximumHardwareDiagnosticCount);
        }

        [Fact]
        public void TagTablePathRejectsMissingScopeTableOrUnitNames()
        {
            Assert.Throws<ArgumentException>(() =>
                LegacyLibraryReadbackPolicy.GetUnitTagScopePath(" "));
            Assert.Throws<ArgumentException>(() =>
                LegacyLibraryReadbackPolicy.BuildTagTablePath(null, null, "Table"));
            Assert.Throws<ArgumentException>(() =>
                LegacyLibraryReadbackPolicy.BuildTagTablePath("PLC", null, ""));
        }

        [Fact]
        public void WalkValidatesItsDelegates()
        {
            var root = new FakeGroup("Root");
            Assert.Throws<ArgumentNullException>(() =>
                LegacyLibraryReadbackPolicy.WalkGroupTree<FakeGroup, string>(
                    null,
                    group => group.Items,
                    group => group.Children,
                    group => group.Name));
            Assert.Throws<ArgumentNullException>(() =>
                LegacyLibraryReadbackPolicy.WalkGroupTree<FakeGroup, string>(
                    root,
                    null,
                    group => group.Children,
                    group => group.Name));
        }

        private sealed class FakeGroup
        {
            public FakeGroup(string name, params string[] items)
            {
                Name = name;
                Items = new List<string>(items ?? new string[0]);
                Children = new List<FakeGroup>();
            }

            public string Name { get; }

            public IList<string> Items { get; set; }

            public IList<FakeGroup> Children { get; set; }
        }
    }
}
