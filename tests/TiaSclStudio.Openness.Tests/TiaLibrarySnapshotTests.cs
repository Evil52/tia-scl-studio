using System;
using System.Collections.Generic;
using System.Threading;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;
using Xunit;

namespace TiaSclStudio.Openness.Tests
{
    public sealed class TiaLibrarySnapshotTests
    {
        [Fact]
        public void SourceKeepsOnlyVersionNeutralValues()
        {
            var source = new TiaLibrarySource(
                TiaLibrarySourceKind.FunctionBlock,
                "FB_Valve",
                "Unit/Packaging/Valves",
                "SCL",
                "FUNCTION_BLOCK \"FB_Valve\"\r\nEND_FUNCTION_BLOCK");

            Assert.Equal(TiaLibrarySourceKind.FunctionBlock, source.Kind);
            Assert.Equal("FB_Valve", source.Name);
            Assert.Equal("Unit/Packaging/Valves", source.GroupPath);
            Assert.Equal("SCL", source.ProgrammingLanguage);
            Assert.Contains("FUNCTION_BLOCK", source.SclText);
            Assert.False(source.IsInterfaceOnly);
        }

        [Fact]
        public void InterfaceOnlySourceKeepsItsReadOnlyOrigin()
        {
            var source = new TiaLibrarySource(
                TiaLibrarySourceKind.FunctionBlock,
                "FB_Lad",
                string.Empty,
                "LAD",
                "FUNCTION_BLOCK FB_Lad\r\nBEGIN\r\nEND_FUNCTION_BLOCK",
                true);

            Assert.True(source.IsInterfaceOnly);
            Assert.Equal("LAD", source.ProgrammingLanguage);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void SourceRejectsMissingNames(string name)
        {
            Assert.Throws<ArgumentException>(() => new TiaLibrarySource(
                TiaLibrarySourceKind.Function,
                name,
                null,
                "SCL",
                "FUNCTION FC_Test : Void\r\nEND_FUNCTION"));
        }

        [Fact]
        public void SourceRejectsUnknownKindsAndEmptyText()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TiaLibrarySource(
                (TiaLibrarySourceKind)999,
                "FC_Test",
                null,
                "SCL",
                "FUNCTION FC_Test : Void\r\nEND_FUNCTION"));
            Assert.Throws<ArgumentException>(() => new TiaLibrarySource(
                TiaLibrarySourceKind.Function,
                "FC_Test",
                null,
                "SCL",
                " "));
        }

        [Fact]
        public void SnapshotCollectionsAreDefensiveAndReadOnly()
        {
            var mutableSources = new List<TiaLibrarySource>
            {
                Source("FC_A")
            };
            var mutableDiagnostics = new List<OpennessDiagnostic>
            {
                new OpennessDiagnostic("READ", DiagnosticSeverity.Information, "Read.")
            };
            var snapshot = new TiaLibrarySnapshot(
                true,
                true,
                "PLC_1",
                "Software",
                mutableSources,
                mutableDiagnostics);

            mutableSources.Clear();
            mutableDiagnostics.Clear();

            Assert.True(snapshot.IsAvailable);
            Assert.True(snapshot.IsComplete);
            Assert.Single(snapshot.Sources);
            Assert.Single(snapshot.Diagnostics);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<TiaLibrarySource>)snapshot.Sources).Add(Source("FC_B")));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<OpennessDiagnostic>)snapshot.Diagnostics).Clear());
        }

        [Fact]
        public void UnavailableSnapshotCannotClaimCompleteness()
        {
            var snapshot = new TiaLibrarySnapshot(
                false,
                true,
                null,
                null,
                null,
                null);

            Assert.False(snapshot.IsAvailable);
            Assert.False(snapshot.IsComplete);
            Assert.Equal(string.Empty, snapshot.DeviceName);
            Assert.Equal(string.Empty, snapshot.SoftwareName);
        }

        [Fact]
        public void SnapshotRejectsNullCollectionElements()
        {
            Assert.Throws<ArgumentException>(() => new TiaLibrarySnapshot(
                true,
                true,
                "PLC",
                "Software",
                new TiaLibrarySource[] { null },
                null));
            Assert.Throws<ArgumentException>(() => new TiaLibrarySnapshot(
                true,
                true,
                "PLC",
                "Software",
                null,
                new OpennessDiagnostic[] { null }));
        }

        [Fact]
        public void OfflineGatewayReturnsAnExplicitUnavailableReadbackSnapshot()
        {
            using (var gateway = new OfflineGateway())
            {
                var cancelled = new CancellationToken(true);
                var snapshot = gateway.ReadLibrarySnapshot(cancelled);

                Assert.False(snapshot.IsAvailable);
                Assert.False(snapshot.IsComplete);
                Assert.Empty(snapshot.Sources);
                Assert.Contains(
                    snapshot.Diagnostics,
                    diagnostic =>
                        diagnostic.Code == OpennessDiagnosticCodes.LibrarySnapshotUnavailable &&
                        diagnostic.Severity == DiagnosticSeverity.Information);
            }
        }

        private static TiaLibrarySource Source(string name)
        {
            return new TiaLibrarySource(
                TiaLibrarySourceKind.Function,
                name,
                string.Empty,
                "SCL",
                "FUNCTION \"" + name + "\" : Void\r\nEND_FUNCTION");
        }
    }
}
