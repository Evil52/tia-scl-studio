using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Openness.Gateway;
using Xunit;

namespace TiaSclStudio.Openness.Tests
{
    /// <summary>
    /// The export request is the boundary between the editor and anything that
    /// can change a customer's TIA project. It normalizes source order because
    /// TIA resolves symbols while importing: get the order wrong and the import
    /// fails halfway, leaving the project partially rewritten.
    /// </summary>
    public sealed class TiaExportRequestTests
    {
        private static TiaSourceFile Source(string path, int order)
        {
            return new TiaSourceFile(path, order, TiaSourceKind.Block);
        }

        [Fact]
        public void RejectsAMissingSourceList()
        {
            Assert.Throws<ArgumentNullException>(
                () => new TiaExportRequest("p", null, false));
        }

        [Fact]
        public void RejectsANullSourceInTheList()
        {
            Assert.Throws<ArgumentException>(
                () => new TiaExportRequest("p", new TiaSourceFile[] { null }, false));
        }

        [Fact]
        public void RejectsANullTagInTheList()
        {
            Assert.Throws<ArgumentException>(() => new TiaExportRequest(
                "p",
                new TiaSourceFile[0],
                false,
                null,
                null,
                new TiaPlcTag[] { null },
                false,
                TiaExportRequest.DefaultOwnershipMarker,
                false,
                false));
        }

        [Fact]
        public void AcceptsAnEmptySourceList()
        {
            var request = new TiaExportRequest("p", new TiaSourceFile[0], false);

            Assert.Empty(request.Sources);
        }

        [Fact]
        public void SortsSourcesByImportOrder()
        {
            var request = new TiaExportRequest(
                "p",
                new[] { Source("c.scl", 2), Source("a.scl", 0), Source("b.scl", 1) },
                false);

            Assert.Equal(
                new[] { "a.scl", "b.scl", "c.scl" },
                request.Sources.Select(source => source.FilePath).ToArray());
        }

        [Fact]
        public void BreaksImportOrderTiesByPathSoTheRequestIsReproducible()
        {
            // Two sources at the same rank must not import in whatever order the
            // caller happened to enumerate them: a rerun has to do the same thing.
            var first = new TiaExportRequest(
                "p",
                new[] { Source("z.scl", 0), Source("a.scl", 0), Source("m.scl", 0) },
                false);
            var second = new TiaExportRequest(
                "p",
                new[] { Source("m.scl", 0), Source("z.scl", 0), Source("a.scl", 0) },
                false);

            Assert.Equal(
                first.Sources.Select(source => source.FilePath).ToArray(),
                second.Sources.Select(source => source.FilePath).ToArray());
            Assert.Equal("a.scl", first.Sources[0].FilePath);
        }

        [Fact]
        public void SortsTagsByTableThenName()
        {
            var request = new TiaExportRequest(
                "p",
                new TiaSourceFile[0],
                false,
                null,
                null,
                new[]
                {
                    new TiaPlcTag("TableB", "Zulu", "Bool", "%I0.0"),
                    new TiaPlcTag("TableA", "Zulu", "Bool", "%I0.1"),
                    new TiaPlcTag("TableA", "Alpha", "Bool", "%I0.2")
                },
                true,
                TiaExportRequest.DefaultOwnershipMarker,
                false,
                false);

            Assert.Equal(
                new[] { "TableA/Alpha", "TableA/Zulu", "TableB/Zulu" },
                request.Tags.Select(tag => tag.TableName + "/" + tag.Name).ToArray());
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("  C:\\a.ap17  ", "C:\\a.ap17")]
        public void NormalizesTheProjectPath(string input, string expected)
        {
            Assert.Equal(expected, new TiaExportRequest(input, new TiaSourceFile[0], false).ProjectPath);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void FallsBackToTheDefaultOwnershipMarker(string marker)
        {
            // The marker is what tells a later export which blocks it is allowed
            // to overwrite. An empty one would either claim nothing or, worse,
            // match blocks the tool never created.
            var request = new TiaExportRequest(
                "p",
                new TiaSourceFile[0],
                false,
                null,
                null,
                null,
                false,
                marker,
                false,
                false);

            Assert.Equal(TiaExportRequest.DefaultOwnershipMarker, request.OwnershipMarker);
        }

        [Fact]
        public void TrimsAnExplicitOwnershipMarker()
        {
            var request = new TiaExportRequest(
                "p",
                new TiaSourceFile[0],
                false,
                null,
                null,
                null,
                false,
                "  MINE  ",
                false,
                false);

            Assert.Equal("MINE", request.OwnershipMarker);
        }

        [Fact]
        public void TheConvenienceConstructorDefaultsToTheSafeSettings()
        {
            var request = new TiaExportRequest("p", new TiaSourceFile[0], true);

            Assert.False(request.AllowTagUpdates);
            Assert.False(request.AllowOwnedBlockOverwrite);
            Assert.False(request.SaveProjectAfterExport);
            Assert.False(request.VerifySavedExportAfterReopen);
            Assert.Equal(TiaExportRequest.DefaultOwnershipMarker, request.OwnershipMarker);
        }

        [Fact]
        public void ReopenReadbackVerificationIsExplicitlyOptIn()
        {
            var request = new TiaExportRequest(
                "p",
                new TiaSourceFile[0],
                true,
                null,
                null,
                null,
                false,
                "MINE",
                true,
                true,
                true);

            Assert.True(request.SaveProjectAfterExport);
            Assert.True(request.VerifySavedExportAfterReopen);
        }

        [Fact]
        public void SourcesAndTagsAreExposedAsReadOnlyViews()
        {
            var request = new TiaExportRequest("p", new[] { Source("a.scl", 0) }, false);

            Assert.Throws<NotSupportedException>(
                () => ((IList<TiaSourceFile>)request.Sources).Add(Source("b.scl", 1)));
            Assert.Throws<NotSupportedException>(
                () => ((IList<TiaPlcTag>)request.Tags).Add(new TiaPlcTag("T", "N", "Bool", "%I0.0")));
        }

        [Fact]
        public void MutatingTheCallersListAfterwardsDoesNotChangeTheRequest()
        {
            var sources = new List<TiaSourceFile> { Source("a.scl", 0) };
            var request = new TiaExportRequest("p", sources, false);

            sources.Add(Source("b.scl", 1));

            Assert.Single(request.Sources);
        }

        // ---------------------------------------------------------------
        // The value objects the request is built from
        // ---------------------------------------------------------------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ASourceFileNeedsAPath(string path)
        {
            Assert.Throws<ArgumentException>(() => new TiaSourceFile(path, 0, TiaSourceKind.Block));
        }

        [Fact]
        public void ASourceFileRejectsANegativeImportOrder()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TiaSourceFile("a.scl", -1, TiaSourceKind.Block));
        }

        [Fact]
        public void ASourceFileRejectsAKindThatIsNotInTheEnum()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TiaSourceFile("a.scl", 0, (TiaSourceKind)99));
        }

        [Theory]
        [InlineData(null, "N", "Bool")]
        [InlineData("T", null, "Bool")]
        [InlineData("T", "N", null)]
        [InlineData("   ", "N", "Bool")]
        public void ATagNeedsATableNameAndADataType(string table, string name, string dataType)
        {
            Assert.Throws<ArgumentException>(
                () => new TiaPlcTag(table, name, dataType, "%I0.0"));
        }

        [Fact]
        public void ATagToleratesAMissingAddressAndComment()
        {
            var tag = new TiaPlcTag("  T  ", "  N  ", "  Bool  ", null);

            Assert.Equal("T", tag.TableName);
            Assert.Equal("N", tag.Name);
            Assert.Equal("Bool", tag.DataType);
            Assert.Equal(string.Empty, tag.LogicalAddress);
            Assert.Equal(string.Empty, tag.Comment);
        }

        [Fact]
        public void AConnectionRequestRejectsAModeThatIsNotInTheEnum()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TiaConnectionRequest((TiaConnectionMode)42));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void AConnectionRequestRejectsANonPositiveProcessId(int processId)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TiaConnectionRequest(TiaConnectionMode.AttachToRunning, processId));
        }

        [Fact]
        public void AProcessIdOnlyMakesSenseWhenAttaching()
        {
            // Passing one while starting a headless Portal would silently attach
            // to some unrelated process instead.
            Assert.Throws<ArgumentException>(
                () => new TiaConnectionRequest(TiaConnectionMode.StartWithoutUserInterface, 1234));
        }

        [Fact]
        public void ADiagnosticNeedsACodeAndAMessage()
        {
            Assert.Throws<ArgumentException>(
                () => new TiaSclStudio.Openness.Diagnostics.OpennessDiagnostic(
                    " ",
                    TiaSclStudio.Openness.Diagnostics.DiagnosticSeverity.Error,
                    "message"));
            Assert.Throws<ArgumentException>(
                () => new TiaSclStudio.Openness.Diagnostics.OpennessDiagnostic(
                    "CODE",
                    TiaSclStudio.Openness.Diagnostics.DiagnosticSeverity.Error,
                    " "));
        }

        [Fact]
        public void AnExportResultRejectsANegativeImportCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TiaExportResult(TiaExportOutcome.Succeeded, -1, false, null));
        }

        [Fact]
        public void AnExportResultDefaultsToNoReadback()
        {
            var result = new TiaExportResult(
                TiaExportOutcome.Succeeded,
                1,
                true,
                null);

            Assert.False(result.ReadbackAttempted);
            Assert.False(result.ReadbackSucceeded);
        }

        [Fact]
        public void AnExportResultRecordsSuccessfulReadback()
        {
            var result = new TiaExportResult(
                TiaExportOutcome.Succeeded,
                1,
                true,
                null,
                true,
                true);

            Assert.True(result.ReadbackAttempted);
            Assert.True(result.ReadbackSucceeded);
        }

        [Fact]
        public void AnExportResultCannotReportSuccessWithoutAttemptingReadback()
        {
            Assert.Throws<ArgumentException>(() => new TiaExportResult(
                TiaExportOutcome.Succeeded,
                1,
                true,
                null,
                false,
                true));
        }
    }
}
