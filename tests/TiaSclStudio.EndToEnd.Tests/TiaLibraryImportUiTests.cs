using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TiaSclStudio.Core.Importing;
using TiaSclStudio.EndToEnd.Tests;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.App
{
    [Collection(WpfCollection.Name)]
    public sealed class TiaLibraryImportUiTests
    {
        private readonly WpfHost _host;

        public TiaLibraryImportUiTests(WpfHost host)
        {
            _host = host;
        }

        [Fact]
        public void ReadCommandRequiresTheCurrentConnectedTargetAndAnIdleWorker()
        {
            var connected = new TiaGatewayStatus(
                TiaGatewayMode.LegacyAdapter,
                true,
                true,
                "Connected",
                null,
                null);

            Assert.True(TiaLibraryImportUiLogic.CanReadLibrary(connected, true, false));
            Assert.False(TiaLibraryImportUiLogic.CanReadLibrary(connected, false, false));
            Assert.False(TiaLibraryImportUiLogic.CanReadLibrary(connected, true, true));
            Assert.False(TiaLibraryImportUiLogic.CanReadLibrary(null, true, false));
        }

        [Fact]
        public void ACompleteSnapshotBecomesOneParserBatchWithoutLosingObjects()
        {
            var snapshot = Snapshot(
                true,
                Source(
                    TiaLibrarySourceKind.DataType,
                    "Payload",
                    "TYPE Payload\r\nSTRUCT\r\n Value : Int;\r\nEND_STRUCT;\r\nEND_TYPE"),
                Source(
                    TiaLibrarySourceKind.FunctionBlock,
                    "FB_UsePayload",
                    "FUNCTION_BLOCK FB_UsePayload\r\nVAR_INPUT\r\n Value : Payload;\r\nEND_VAR\r\nBEGIN\r\nEND_FUNCTION_BLOCK"));

            var preparation = TiaLibraryImportUiLogic.Prepare(snapshot);
            var parsed = new SclLibrarySourceParser().Parse(preparation.CombinedScl);

            Assert.True(preparation.CanPreview);
            Assert.True(preparation.IsComplete);
            Assert.Equal(string.Empty, preparation.WarningText);
            Assert.Equal(2, parsed.Items.Count);
            Assert.False(parsed.HasErrors, string.Join(", ", parsed.Diagnostics.Select(item => item.Code)));
            Assert.Contains("«PLC_1» / «Program»", preparation.TargetText);
        }

        [Fact]
        public void AnIncompleteSnapshotRequiresAVisibleWarningButCanBePreviewedExplicitly()
        {
            var diagnostic = new OpennessDiagnostic(
                "TIA_LIBRARY_OBJECT_SKIPPED",
                DiagnosticSeverity.Warning,
                "Protected block was skipped.",
                "Protected/FB_Secret");
            var snapshot = new TiaLibrarySnapshot(
                true,
                false,
                "PLC_1",
                "Program",
                new[]
                {
                    Source(
                        TiaLibrarySourceKind.Function,
                        "FC_Visible",
                        "FUNCTION FC_Visible : Void\r\nBEGIN\r\nEND_FUNCTION")
                },
                new[] { diagnostic });

            var preparation = TiaLibraryImportUiLogic.Prepare(snapshot);
            var presentation = TiaLibraryImportUiLogic.CreatePresentation(preparation);

            Assert.True(preparation.CanPreview);
            Assert.False(preparation.IsComplete);
            Assert.Contains("Снимок неполный", preparation.WarningText);
            Assert.Contains("TIA_LIBRARY_OBJECT_SKIPPED", preparation.WarningText);
            Assert.Equal(preparation.WarningText, presentation.WarningText);
        }

        [Fact]
        public void InterfaceOnlyLadSourceCanBePreviewedAndIsExplained()
        {
            var snapshot = new TiaLibrarySnapshot(
                true,
                true,
                "PLC_1",
                "Program",
                new[]
                {
                    new TiaLibrarySource(
                        TiaLibrarySourceKind.FunctionBlock,
                        "FB_Lad",
                        string.Empty,
                        "LAD",
                        "FUNCTION_BLOCK FB_Lad\r\nVAR_INPUT\r\n In1 : Bool;\r\nEND_VAR\r\nBEGIN\r\nEND_FUNCTION_BLOCK",
                        true)
                },
                null);

            var preparation = TiaLibraryImportUiLogic.Prepare(snapshot);
            var parsed = new SclLibrarySourceParser().Parse(preparation.CombinedScl);

            Assert.True(preparation.CanPreview);
            Assert.Contains("интерфейсных LAD/FBD-блоков 1", preparation.SummaryText);
            Assert.Contains("без SCL-тела", preparation.WarningText);
            Assert.Single(parsed.Items);
            Assert.False(parsed.HasErrors);
        }

        [Fact]
        public void UnavailableOrEmptySnapshotsNeverOpenAnImportPreview()
        {
            var unavailable = new TiaLibrarySnapshot(
                false,
                false,
                string.Empty,
                string.Empty,
                null,
                null);
            var empty = new TiaLibrarySnapshot(
                true,
                true,
                "PLC_1",
                "Program",
                null,
                null);

            Assert.False(TiaLibraryImportUiLogic.Prepare(unavailable).CanPreview);
            Assert.False(TiaLibraryImportUiLogic.Prepare(empty).CanPreview);
        }

        [Fact]
        public void ACancelledSnapshotNeverOffersItsPartialSourcesForImport()
        {
            var cancelled = new TiaLibrarySnapshot(
                true,
                false,
                "PLC_1",
                "Program",
                new[]
                {
                    Source(
                        TiaLibrarySourceKind.FunctionBlock,
                        "FB_Partial",
                        "FUNCTION_BLOCK FB_Partial\r\nBEGIN\r\nEND_FUNCTION_BLOCK")
                },
                new[]
                {
                    new OpennessDiagnostic(
                        OpennessDiagnosticCodes.LibrarySnapshotCancelled,
                        DiagnosticSeverity.Information,
                        "Readback was cancelled.")
                });

            var preparation = TiaLibraryImportUiLogic.Prepare(cancelled);

            Assert.False(preparation.CanPreview);
            Assert.False(preparation.IsComplete);
            Assert.Contains("отменено", preparation.SummaryText);
        }

        [Fact]
        public void ACombinedSnapshotAboveTheParserLimitIsRejectedWithoutTruncation()
        {
            var oversizedScl = "FUNCTION FC_Large : Void\r\nBEGIN\r\nEND_FUNCTION" +
                new string(' ', SclLibrarySourceParser.MaximumSourceLength);
            var snapshot = Snapshot(
                true,
                Source(TiaLibrarySourceKind.Function, "FC_Large", oversizedScl));

            var preparation = TiaLibraryImportUiLogic.Prepare(snapshot);

            Assert.False(preparation.CanPreview);
            Assert.Equal(string.Empty, preparation.CombinedScl);
            Assert.Contains("безопасный лимит", preparation.SummaryText);
        }

        [Fact]
        public void ConnectedTiaPreviewNamesTheSourceAndKeepsTheWarningVisible()
        {
            _host.Invoke(() =>
            {
                var project = ModelBuilder.Project();
                var parsed = new SclLibrarySourceParser().Parse(
                    "FUNCTION_BLOCK FB_Valve\r\nBEGIN\r\nEND_FUNCTION_BLOCK");
                var withoutReplacement = SclImportApplicationLogic.BuildPlan(project, parsed, false);
                var withReplacement = SclImportApplicationLogic.BuildPlan(project, parsed, true);
                var presentation = new SclImportPreviewPresentation(
                    "TIA preview",
                    "ИМПОРТ ИЗ TIA PORTAL",
                    "TIA Portal не изменяется.",
                    "Снимок неполный.");
                var window = new SclImportPreviewWindow(
                    withoutReplacement,
                    withReplacement,
                    presentation);
                try
                {
                    Assert.Equal("TIA preview", window.Title);
                    Assert.Equal(
                        "ИМПОРТ ИЗ TIA PORTAL",
                        Assert.IsType<TextBlock>(window.FindName("ImportHeadingText")).Text);
                    Assert.Contains(
                        "не изменяется",
                        Assert.IsType<TextBlock>(window.FindName("ImportSafetyText")).Text);
                    Assert.Equal(
                        Visibility.Visible,
                        Assert.IsType<Border>(window.FindName("ImportWarningPanel")).Visibility);
                    Assert.False(
                        Assert.IsType<CheckBox>(window.FindName("ReplaceExistingCheckBox")).IsChecked == true);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void MainWindowExposesAReadOnlyTiaLibraryCommandThatStartsDisabledOffline()
        {
            _host.Invoke(() =>
            {
                var window = new MainWindow();
                try
                {
                    var button = Assert.IsType<Button>(window.FindName("ReadTiaLibraryButton"));
                    var summary = Assert.IsType<TextBlock>(window.FindName("TiaLibraryReadSummaryText"));

                    Assert.False(button.IsEnabled);
                    Assert.Contains("TIA не изменяется", summary.Text);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        private static TiaLibrarySnapshot Snapshot(
            bool complete,
            params TiaLibrarySource[] sources)
        {
            return new TiaLibrarySnapshot(
                true,
                complete,
                "PLC_1",
                "Program",
                sources,
                null);
        }

        private static TiaLibrarySource Source(
            TiaLibrarySourceKind kind,
            string name,
            string scl)
        {
            return new TiaLibrarySource(kind, name, "", "SCL", scl);
        }
    }
}
