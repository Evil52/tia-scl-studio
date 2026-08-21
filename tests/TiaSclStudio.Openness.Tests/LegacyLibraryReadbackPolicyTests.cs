using System;
using System.Collections.Generic;
using System.Linq;
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
