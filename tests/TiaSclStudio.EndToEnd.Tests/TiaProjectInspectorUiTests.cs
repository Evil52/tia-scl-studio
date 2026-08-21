using System;
using System.Linq;
using System.Windows.Controls;
using TiaSclStudio.EndToEnd.Tests;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;
using Xunit;

namespace TiaSclStudio.App
{
    [Collection(WpfCollection.Name)]
    public sealed class TiaProjectInspectorUiTests
    {
        private readonly WpfHost _host;

        public TiaProjectInspectorUiTests(WpfHost host)
        {
            _host = host;
        }

        [Fact]
        public void InspectorCommandRequiresTheCurrentConnectedTargetAndAnIdleWorker()
        {
            var connected = ConnectedStatus();

            Assert.True(TiaProjectInspectorLogic.CanStartRead(connected, true, false));
            Assert.False(TiaProjectInspectorLogic.CanStartRead(connected, false, false));
            Assert.False(TiaProjectInspectorLogic.CanStartRead(connected, true, true));
            Assert.False(TiaProjectInspectorLogic.CanStartRead(null, true, false));
        }

        [Fact]
        public void CompleteCatalogsProduceSortedReadOnlyRowsAndTargetMetadata()
        {
            var tags = new TiaPlcTagCatalog(
                true,
                true,
                "PLC_1",
                "Program",
                new[]
                {
                    Tag("PLC/TableB", "Zulu", "Word", "%MW2", "Second"),
                    Tag("PLC/TableA", "Alpha", "Bool", "%I0.0", "First")
                },
                null);
            var hardware = HardwareCatalog(
                Channel(TiaIoChannelKind.AnalogOutput, 2, 128, "%QW16", "Rack/AO"),
                Channel(TiaIoChannelKind.DigitalInput, 0, 0, "%I0.0", "Rack/DI"));

            var model = TiaProjectInspectorLogic.Prepare(
                ConnectedStatus(),
                true,
                @"C:\Projects\Line.ap20",
                string.Empty,
                string.Empty,
                tags,
                hardware);

            Assert.True(model.CanOpen, model.BlockingReason);
            Assert.Equal("PLC_1 / Program", model.TargetText);
            Assert.Equal("Alpha", model.Tags[0].Name);
            Assert.Equal("Zulu", model.Tags[1].Name);
            Assert.Equal("DI", model.HardwareChannels[0].Kind);
            Assert.Equal("AO", model.HardwareChannels[1].Kind);
            Assert.Equal(string.Empty, model.WarningText);
            Assert.Contains("S71200", model.CpuText);
        }

        [Fact]
        public void OneUnavailableOrPartialCatalogOpensWithAnExplicitEmptyOrPartialSection()
        {
            var partialTags = new TiaPlcTagCatalog(
                true,
                false,
                "PLC_1",
                "Program",
                new[] { Tag("PLC/Default", "Visible", "Bool", "%I0.0", "") },
                new[]
                {
                    new OpennessDiagnostic(
                        OpennessDiagnosticCodes.PlcTagCatalogItemSkipped,
                        DiagnosticSeverity.Warning,
                        "A protected table was skipped.")
                });
            var unavailableHardware = new TiaHardwareIoCatalog(
                false,
                TiaCpuFamily.Unknown,
                string.Empty,
                string.Empty,
                null,
                new[]
                {
                    new OpennessDiagnostic(
                        OpennessDiagnosticCodes.HardwareIoCatalogUnavailable,
                        DiagnosticSeverity.Warning,
                        "Hardware is unavailable.")
                });

            var model = TiaProjectInspectorLogic.Prepare(
                ConnectedStatus(),
                true,
                "Line.ap20",
                "",
                "",
                partialTags,
                unavailableHardware);

            Assert.True(model.CanOpen, model.BlockingReason);
            Assert.Single(model.Tags);
            Assert.Empty(model.HardwareChannels);
            Assert.Contains("снимок PLC-тегов неполный", model.WarningText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("вкладка I/O оставлена пустой", model.WarningText);
        }

        [Fact]
        public void CancellationSessionLossAndTwoUnavailableCatalogsFailClosed()
        {
            var cancelledTags = new TiaPlcTagCatalog(
                true,
                false,
                "PLC_1",
                "Program",
                new[] { Tag("PLC/Default", "Partial", "Bool", "%I0.0", "") },
                new[]
                {
                    new OpennessDiagnostic(
                        OpennessDiagnosticCodes.PlcTagCatalogCancelled,
                        DiagnosticSeverity.Information,
                        "Cancelled.")
                });
            var unavailableTags = new TiaPlcTagCatalog(
                false,
                false,
                "",
                "",
                null,
                null);
            var unavailableHardware = new TiaHardwareIoCatalog(
                false,
                TiaCpuFamily.Unknown,
                "",
                "",
                null,
                null);
            var sessionLostTags = new TiaPlcTagCatalog(
                true,
                false,
                "PLC_1",
                "Program",
                new[] { Tag("PLC/Default", "Stale", "Bool", "%I0.1", "") },
                new[]
                {
                    new OpennessDiagnostic(
                        OpennessDiagnosticCodes.SessionLost,
                        DiagnosticSeverity.Error,
                        "Session was lost during discovery.")
                });
            var cancelledHardware = new TiaHardwareIoCatalog(
                false,
                TiaCpuFamily.Unknown,
                "",
                "",
                null,
                new[]
                {
                    new OpennessDiagnostic(
                        OpennessDiagnosticCodes.HardwareIoCatalogCancelled,
                        DiagnosticSeverity.Information,
                        "Cancelled.")
                });

            Assert.False(Prepare(cancelledTags, HardwareCatalog()).CanOpen);
            Assert.False(Prepare(
                new TiaPlcTagCatalog(true, true, "PLC_1", "Program", null, null),
                cancelledHardware).CanOpen);
            Assert.False(Prepare(sessionLostTags, HardwareCatalog()).CanOpen);
            Assert.False(Prepare(unavailableTags, unavailableHardware).CanOpen);
            Assert.False(TiaProjectInspectorLogic.Prepare(
                ConnectedStatus(),
                false,
                "Line.ap20",
                "PLC_1",
                "Program",
                new TiaPlcTagCatalog(true, true, "PLC_1", "Program", null, null),
                HardwareCatalog()).CanOpen);
        }

        [Fact]
        public void FilterMatchesEveryTokenAcrossNamesTypesAddressesAndPaths()
        {
            var tag = new TiaProjectInspectorTagRow(
                Tag("UNIT/Motion/Tags", "MotorReady", "Bool", "%I2.1", "Drive ready"));
            var hardware = new TiaProjectInspectorHardwareRow(
                Channel(TiaIoChannelKind.AnalogOutput, 3, 256, "%QW32", "Rack/Analog"));

            Assert.True(TiaProjectInspectorLogic.MatchesFilter(tag, "motor bool %i2"));
            Assert.True(TiaProjectInspectorLogic.MatchesFilter(tag, "motion drive"));
            Assert.False(TiaProjectInspectorLogic.MatchesFilter(tag, "motor real"));
            Assert.True(TiaProjectInspectorLogic.MatchesFilter(hardware, "ao %qw32 rack"));
            Assert.False(TiaProjectInspectorLogic.MatchesFilter(hardware, "digital"));
        }

        [Fact]
        public void FilterTokenizationNormalizesAndDeduplicatesOncePerRefresh()
        {
            var tokens = TiaProjectInspectorLogic.TokenizeFilter(
                "  motor\tBool  MOTOR\r\n%i2  ");
            var tag = new TiaProjectInspectorTagRow(
                Tag("UNIT/Motion/Tags", "MotorReady", "Bool", "%I2.1", "Drive ready"));

            Assert.Equal(new[] { "MOTOR", "BOOL", "%I2" }, tokens);
            Assert.True(TiaProjectInspectorLogic.MatchesFilter(tag, tokens));
            Assert.True(TiaProjectInspectorLogic.MatchesFilter(
                tag,
                TiaProjectInspectorLogic.TokenizeFilter("")));
        }

        [Fact]
        public void DebounceStateKeepsOnlyLatestQueryAndCancellationInvalidatesIt()
        {
            var state = new TiaProjectInspectorFilterDebounceState();
            var first = state.Schedule("motor");
            var second = state.Schedule("temperature");

            string query;
            Assert.False(state.TryTake(first, out query));
            Assert.True(state.TryTake(second, out query));
            Assert.Equal("temperature", query);
            Assert.False(state.TryTake(second, out query));

            var cancelled = state.Schedule("stale");
            state.Cancel();
            Assert.False(state.TryTake(cancelled, out query));
        }

        [Fact]
        public void WindowShowsTwoReadOnlySortableCopyableTablesAndFiltersBoth()
        {
            _host.Invoke(() =>
            {
                var tags = new TiaPlcTagCatalog(
                    true,
                    true,
                    "PLC_1",
                    "Program",
                    new[]
                    {
                        Tag("PLC/Default", "MotorReady", "Bool", "%I0.0", ""),
                        Tag("PLC/Default", "Temperature", "Real", "%MD4", "")
                    },
                    null);
                var model = Prepare(tags, HardwareCatalog(
                    Channel(TiaIoChannelKind.DigitalInput, 0, 0, "%I0.0", "Rack/DI"),
                    Channel(TiaIoChannelKind.AnalogOutput, 1, 128, "%QW16", "Rack/AO")));
                var window = new TiaProjectInspectorWindow(model);
                try
                {
                    var tagsGrid = Assert.IsType<DataGrid>(window.FindName("TagsGrid"));
                    var hardwareGrid = Assert.IsType<DataGrid>(window.FindName("HardwareGrid"));
                    var filter = Assert.IsType<TextBox>(window.FindName("FilterTextBox"));
                    var readOnlyText = Assert.IsType<TextBlock>(
                        window.FindName("InspectorReadOnlyText"));

                    Assert.True(tagsGrid.IsReadOnly);
                    Assert.True(hardwareGrid.IsReadOnly);
                    Assert.True(tagsGrid.CanUserSortColumns);
                    Assert.True(hardwareGrid.CanUserSortColumns);
                    Assert.Equal(DataGridClipboardCopyMode.IncludeHeader, tagsGrid.ClipboardCopyMode);
                    Assert.Equal(4096, filter.MaxLength);
                    Assert.Equal(2, tagsGrid.Items.Count);
                    Assert.Equal(2, hardwareGrid.Items.Count);

                    filter.Text = "motor";
                    window.ApplyFilterImmediately();
                    Assert.Single(tagsGrid.Items);
                    Assert.Empty(hardwareGrid.Items);
                    Assert.Contains("только чтение", readOnlyText.Text);
                    Assert.Contains("TIA Portal не изменяется", readOnlyText.Text);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void MainWindowInspectorButtonStartsDisabledOffline()
        {
            _host.Invoke(() =>
            {
                var window = new MainWindow();
                try
                {
                    var button = Assert.IsType<Button>(
                        window.FindName("OpenTiaProjectInspectorButton"));
                    var summary = Assert.IsType<TextBlock>(
                        window.FindName("TiaProjectInspectorSummaryText"));

                    Assert.False(button.IsEnabled);
                    Assert.Contains("TIA Portal не изменяется", summary.Text);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        private static TiaProjectInspectorModel Prepare(
            TiaPlcTagCatalog tags,
            TiaHardwareIoCatalog hardware)
        {
            return TiaProjectInspectorLogic.Prepare(
                ConnectedStatus(),
                true,
                "Line.ap20",
                "PLC_1",
                "Program",
                tags,
                hardware);
        }

        private static TiaGatewayStatus ConnectedStatus()
        {
            return new TiaGatewayStatus(
                TiaGatewayMode.LegacyAdapter,
                true,
                true,
                "Connected",
                null,
                null);
        }

        private static TiaPlcTagCatalogItem Tag(
            string tablePath,
            string name,
            string dataType,
            string address,
            string comment)
        {
            return new TiaPlcTagCatalogItem(
                tablePath,
                name,
                dataType,
                address,
                comment);
        }

        private static TiaHardwareIoCatalog HardwareCatalog(
            params TiaHardwareIoChannel[] channels)
        {
            return new TiaHardwareIoCatalog(
                true,
                TiaCpuFamily.S71200,
                "CPU 1211C",
                "6ES7 211",
                channels,
                null);
        }

        private static TiaHardwareIoChannel Channel(
            TiaIoChannelKind kind,
            int number,
            int bitAddress,
            string address,
            string path)
        {
            return new TiaHardwareIoChannel(
                kind,
                number,
                bitAddress,
                kind == TiaIoChannelKind.DigitalInput || kind == TiaIoChannelKind.DigitalOutput
                    ? 1u
                    : 16u,
                address,
                kind == TiaIoChannelKind.DigitalInput || kind == TiaIoChannelKind.DigitalOutput
                    ? new[] { "Bool" }
                    : new[] { "Int", "Word" },
                path,
                "OrderNumber:1",
                "Signal module");
        }
    }
}
