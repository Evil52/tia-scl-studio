using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TiaSclStudio.App;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;
using Xunit;

namespace TiaSclStudio.EndToEnd.Tests
{
    /// <summary>
    /// Drives the window the product actually ships. Everything below the UI is
    /// covered by the unit and integration suites; what these tests can only
    /// find here is a control that was renamed, an event handler that was never
    /// wired, or a command that leaves the model and the canvas disagreeing.
    /// </summary>
    [Collection(WpfCollection.Name)]
    public sealed class MainWindowSmokeTests
    {
        private readonly WpfHost _host;

        public MainWindowSmokeTests(WpfHost host)
        {
            _host = host;
        }

        private T OnWindow<T>(Func<Window, T> action)
        {
            return _host.Invoke(() =>
            {
                var window = new MainWindow();
                try
                {
                    return action(window);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        private void OnWindow(Action<Window> action)
        {
            OnWindow<object>(window =>
            {
                action(window);
                return null;
            });
        }

        [Fact]
        public void TheWindowOpensOnADemoProjectWithoutThrowing()
        {
            OnWindow(window =>
            {
                var sheet = MainWindowProbe.Field<CallSheet>(window, "_sheet");
                var project = MainWindowProbe.Field<DiagramProject>(window, "_project");

                Assert.NotNull(project);
                Assert.NotNull(sheet);
                Assert.NotEmpty(sheet.Nodes);
                Assert.NotEmpty(project.Plant.Blocks);
            });
        }

        [Fact]
        public void ReopenReadbackVerificationRequiresSaveAndAConnectedTarget()
        {
            OnWindow(window =>
            {
                var verification = MainWindowProbe.Element<CheckBox>(
                    window,
                    "VerifySavedExportAfterReopenCheckBox");
                var save = MainWindowProbe.Element<CheckBox>(
                    window,
                    "SaveTiaProjectCheckBox");
                var connectionMode = MainWindowProbe.Element<ComboBox>(
                    window,
                    "TiaConnectionModeComboBox");

                Assert.False(verification.IsChecked == true);
                Assert.False(verification.IsEnabled);
                Assert.Contains("открыть заново", verification.Content.ToString());

                connectionMode.SelectedIndex = 2;
                MainWindowProbe.SetField(
                    window,
                    "_connectedTargetFingerprint",
                    (string)MainWindowProbe.Call(window, "BuildConnectionFingerprint"));
                MainWindowProbe.SetField(
                    window,
                    "_connectedTiaMode",
                    (TiaConnectionMode?)TiaConnectionMode.StartWithoutUserInterface);
                MainWindowProbe.Call(
                    window,
                    "ApplyGatewayStatus",
                    new TiaGatewayStatus(
                        TiaGatewayMode.LegacyAdapter,
                        true,
                        true,
                        "Connected",
                        null,
                        null));

                Assert.False(verification.IsEnabled);

                save.IsChecked = true;
                Assert.True(verification.IsEnabled);

                verification.IsChecked = true;
                connectionMode.SelectedIndex = 0;
                Assert.False(verification.IsChecked == true);
                Assert.False(verification.IsEnabled);

                connectionMode.SelectedIndex = 2;
                MainWindowProbe.SetField(
                    window,
                    "_connectedTargetFingerprint",
                    (string)MainWindowProbe.Call(window, "BuildConnectionFingerprint"));
                MainWindowProbe.Call(window, "UpdateOnlineCommandState");
                verification.IsChecked = true;
                save.IsChecked = false;
                Assert.False(verification.IsChecked == true);
                Assert.False(verification.IsEnabled);

                save.IsChecked = true;
                verification.IsChecked = true;
                connectionMode.SelectedIndex = 1;
                Assert.False(verification.IsChecked == true);
                Assert.False(verification.IsEnabled);
            });
        }

        [Fact]
        public void ReopenReadbackVerificationIsUnavailableForAttachToRunning()
        {
            OnWindow(window =>
            {
                var verification = MainWindowProbe.Element<CheckBox>(
                    window,
                    "VerifySavedExportAfterReopenCheckBox");
                var save = MainWindowProbe.Element<CheckBox>(
                    window,
                    "SaveTiaProjectCheckBox");

                MainWindowProbe.SetField(
                    window,
                    "_connectedTargetFingerprint",
                    (string)MainWindowProbe.Call(window, "BuildConnectionFingerprint"));
                MainWindowProbe.SetField(
                    window,
                    "_connectedTiaMode",
                    (TiaConnectionMode?)TiaConnectionMode.AttachToRunning);
                MainWindowProbe.Call(
                    window,
                    "ApplyGatewayStatus",
                    new TiaGatewayStatus(
                        TiaGatewayMode.LegacyAdapter,
                        true,
                        true,
                        "Connected",
                        null,
                        null));

                save.IsChecked = true;

                Assert.False(verification.IsEnabled);
                Assert.False(verification.IsChecked == true);
            });
        }

        [Fact]
        public void BlockedReconnectIsRecognizedAsTheRetainedCurrentTargetOnlyWhenExportIsSafe()
        {
            var diagnostic = new OpennessDiagnostic(
                OpennessDiagnosticCodes.ConnectionReleaseBlocked,
                DiagnosticSeverity.Error,
                "Reconnect was blocked to preserve the current session.");

            Assert.True(MainWindow.IsBlockedReconnectRetainingCurrentSession(
                new TiaGatewayStatus(
                    TiaGatewayMode.LegacyAdapter,
                    true,
                    true,
                    "Retained current session",
                    null,
                    new[] { diagnostic })));
            Assert.False(MainWindow.IsBlockedReconnectRetainingCurrentSession(
                new TiaGatewayStatus(
                    TiaGatewayMode.LegacyAdapter,
                    true,
                    false,
                    "Quarantined session",
                    null,
                    new[] { diagnostic })));
            Assert.False(MainWindow.IsBlockedReconnectRetainingCurrentSession(
                new TiaGatewayStatus(
                    TiaGatewayMode.LegacyAdapter,
                    true,
                    true,
                    "Ordinary connection failure",
                    null,
                    new[]
                    {
                        new OpennessDiagnostic(
                            OpennessDiagnosticCodes.ConnectionFailed,
                            DiagnosticSeverity.Error,
                            "Failed.")
                    })));
        }

        [Fact]
        public void FailedReadbackAfterSuccessfulSaveDoesNotReportHeadlessUnsavedChanges()
        {
            OnWindow(window =>
            {
                MainWindowProbe.SetField(
                    window,
                    "_connectedTiaMode",
                    (TiaConnectionMode?)TiaConnectionMode.StartWithoutUserInterface);
                MainWindowProbe.SetField(window, "_headlessUnsavedChangesRisk", true);

                var request = new TiaExportRequest(
                    "project.ap20",
                    new TiaSourceFile[0],
                    false,
                    null,
                    null,
                    null,
                    false,
                    "TIASCL",
                    false,
                    true,
                    true);
                var result = new TiaExportResult(
                    TiaExportOutcome.Failed,
                    1,
                    false,
                    new[]
                    {
                        new OpennessDiagnostic(
                            OpennessDiagnosticCodes.ProjectSaved,
                            DiagnosticSeverity.Information,
                            "Saved before readback."),
                        new OpennessDiagnostic(
                            OpennessDiagnosticCodes.ExportReadbackFailed,
                            DiagnosticSeverity.Error,
                            "Readback mismatch.")
                    },
                    true,
                    false);

                MainWindowProbe.Call(
                    window,
                    "UpdateHeadlessUnsavedChangesRisk",
                    request,
                    result);

                Assert.False(MainWindowProbe.Field<bool>(
                    window,
                    "_headlessUnsavedChangesRisk"));
            });
        }

        [Fact]
        public void AmbiguousTransactionFinalizationReportsHeadlessUnsavedChanges()
        {
            OnWindow(window =>
            {
                MainWindowProbe.SetField(
                    window,
                    "_connectedTiaMode",
                    (TiaConnectionMode?)TiaConnectionMode.StartWithoutUserInterface);

                var request = new TiaExportRequest(
                    "project.ap20",
                    new TiaSourceFile[0],
                    false);
                var result = new TiaExportResult(
                    TiaExportOutcome.Failed,
                    0,
                    false,
                    new[]
                    {
                        new OpennessDiagnostic(
                            OpennessDiagnosticCodes.UnsavedChangesRequireManualSave,
                            DiagnosticSeverity.Warning,
                            "Commit state is ambiguous.")
                    });

                MainWindowProbe.Call(
                    window,
                    "UpdateHeadlessUnsavedChangesRisk",
                    request,
                    result);

                Assert.True(MainWindowProbe.Field<bool>(
                    window,
                    "_headlessUnsavedChangesRisk"));

                // The assertion intentionally enables the real close guard.
                // Clear it before the shared host closes the window so the test
                // never opens a modal confirmation dialog on the STA thread.
                MainWindowProbe.SetField(window, "_headlessUnsavedChangesRisk", false);
            });
        }

        [Fact]
        public void TheDemoProjectItShipsWithCompilesCleanly()
        {
            // The first thing every new user sees has to be a working example.
            var scl = OnWindow(window =>
                MainWindowProbe.Element<TextBox>(window, "SclPreviewTextBox").Text);

            Assert.False(string.IsNullOrWhiteSpace(scl));
            Assert.Contains("FUNCTION", scl, StringComparison.Ordinal);
            Assert.Contains("END_FUNCTION", scl, StringComparison.Ordinal);
        }

        [Fact]
        public void TheSheetSelectorIsPopulated()
        {
            OnWindow(window =>
            {
                var selector = MainWindowProbe.Element<ComboBox>(window, "SheetSelector");

                Assert.NotEmpty(selector.Items);
                Assert.NotNull(selector.SelectedItem);
            });
        }

        [Theory]
        [InlineData("LogicPaletteAndButton")]
        [InlineData("NotePaletteButton")]
        [InlineData("TagSourcePaletteButton")]
        [InlineData("TagSinkPaletteButton")]
        [InlineData("ConstantPaletteButton")]
        [InlineData("AddGroupButton")]
        [InlineData("UngroupButton")]
        [InlineData("FitAllButton")]
        [InlineData("FitSelectionButton")]
        [InlineData("AutoLayoutButton")]
        [InlineData("UndoButton")]
        public void EveryToolbarCommandIsPresent(string name)
        {
            // A renamed x:Name silently breaks the handler wiring and nothing
            // else in the build notices.
            OnWindow(window => MainWindowProbe.Element<Button>(window, name));
        }

        [Fact]
        public void AddingALogicNodeChangesTheModelAndUndoPutsItBack()
        {
            OnWindow(window =>
            {
                var before = MainWindowProbe.Field<CallSheet>(window, "_sheet").Nodes.Count;

                MainWindowProbe.Click(window, "LogicPaletteAndButton");
                var afterAdd = MainWindowProbe.Field<CallSheet>(window, "_sheet").Nodes.Count;

                MainWindowProbe.Click(window, "UndoButton");
                var afterUndo = MainWindowProbe.Field<CallSheet>(window, "_sheet").Nodes.Count;

                Assert.Equal(before + 1, afterAdd);
                Assert.Equal(before, afterUndo);
            });
        }

        [Fact]
        public void UndoAndRedoReturnToExactlyTheSameDiagram()
        {
            OnWindow(window =>
            {
                MainWindowProbe.Click(window, "LogicPaletteAndButton");
                var afterAdd = Fingerprint(MainWindowProbe.Field<CallSheet>(window, "_sheet"));

                MainWindowProbe.Click(window, "UndoButton");
                MainWindowProbe.Click(window, "RedoButton");

                Assert.Equal(afterAdd, Fingerprint(MainWindowProbe.Field<CallSheet>(window, "_sheet")));
            });
        }

        [Fact]
        public void UndoIsDisabledOnAFreshProjectAndEnabledAfterAnEdit()
        {
            OnWindow(window =>
            {
                var undo = MainWindowProbe.Element<Button>(window, "UndoButton");
                Assert.False(undo.IsEnabled, "Undo is offered before anything has been edited.");

                MainWindowProbe.Click(window, "LogicPaletteAndButton");

                Assert.True(undo.IsEnabled, "Undo is not offered after an edit.");
            });
        }

        [Fact]
        public void UndoingEverythingIsSafeAndLeavesTheDemoIntact()
        {
            // Holding Ctrl+Z is the most common way a user leaves the happy path.
            OnWindow(window =>
            {
                var original = Fingerprint(MainWindowProbe.Field<CallSheet>(window, "_sheet"));

                for (var edit = 0; edit < 5; edit++)
                {
                    MainWindowProbe.Click(window, "LogicPaletteAndButton");
                }

                for (var undo = 0; undo < 20; undo++)
                {
                    var button = MainWindowProbe.Element<Button>(window, "UndoButton");
                    if (!button.IsEnabled)
                    {
                        break;
                    }

                    button.RaiseEvent(new RoutedEventArgs(
                        System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                }

                Assert.Equal(original, Fingerprint(MainWindowProbe.Field<CallSheet>(window, "_sheet")));
            });
        }

        [Fact]
        public void RenderingProducesAVisualForEveryNode()
        {
            OnWindow(window =>
            {
                MainWindowProbe.Call(window, "RenderDiagram");

                var sheet = MainWindowProbe.Field<CallSheet>(window, "_sheet");
                var visuals = MainWindowProbe.Field<Dictionary<Guid, FrameworkElement>>(window, "_nodeVisuals");

                Assert.Equal(sheet.Nodes.Count, visuals.Count);
                foreach (var node in sheet.Nodes)
                {
                    Assert.True(visuals.ContainsKey(node.Id), "Node " + node.Title + " was not rendered.");
                }
            });
        }

        [Fact]
        public void AVisualGroupIsRenderedOnceItIsAddedToTheSheet()
        {
            OnWindow(window =>
            {
                var sheet = MainWindowProbe.Field<CallSheet>(window, "_sheet");
                var group = new DiagramGroup("Smoke region", 20.0, 20.0, 420.0, 260.0);
                sheet.Groups.Add(group);

                MainWindowProbe.Call(window, "RenderDiagram");

                var groupVisuals = MainWindowProbe.Field<Dictionary<Guid, FrameworkElement>>(window, "_groupVisuals");
                Assert.True(groupVisuals.ContainsKey(group.Id), "The visual group was not rendered.");
            });
        }

        [Fact]
        public void SelectingAGroupShowsExactlyEightResizeHandlesOnOneAdornerAboveNodesAndWires()
        {
            OnWindow(window =>
            {
                var sheet = MainWindowProbe.Field<CallSheet>(window, "_sheet");
                var group = new DiagramGroup("Resizable region", 20.0, 20.0, 420.0, 260.0);
                sheet.Groups.Add(group);

                MainWindowProbe.Call(window, "RenderDiagram");
                MainWindowProbe.Call(window, "SelectGroup", group.Id);

                var canvas = MainWindowProbe.Element<Canvas>(window, "DiagramCanvas");
                var adorner = MainWindowProbe.Field<Grid>(window, "_groupResizeAdorner");
                Assert.NotNull(adorner);
                Assert.Same(canvas, adorner.Parent);
                Assert.Equal(group.Id, Assert.IsType<Guid>(adorner.Tag));

                var handles = adorner.Children
                    .OfType<Border>()
                    .Where(item => item.Uid.StartsWith("GroupResizeHandle-", StringComparison.Ordinal))
                    .ToList();
                Assert.Equal(8, handles.Count);
                Assert.Equal(8, handles.Select(item => item.Uid).Distinct().Count());

                var adornerZ = Panel.GetZIndex(adorner);
                Assert.Equal(40, adornerZ);
                Assert.All(
                    MainWindowProbe.Field<Dictionary<Guid, FrameworkElement>>(window, "_nodeVisuals").Values,
                    visual => Assert.True(Panel.GetZIndex(visual) < adornerZ));
                Assert.All(
                    MainWindowProbe.Field<List<System.Windows.Shapes.Path>>(window, "_wireVisuals"),
                    visual => Assert.True(Panel.GetZIndex(visual) < adornerZ));
            });
        }

        [Fact]
        public void ReRenderingASelectedGroupDoesNotAccumulateResizeAdorners()
        {
            OnWindow(window =>
            {
                var sheet = MainWindowProbe.Field<CallSheet>(window, "_sheet");
                var group = new DiagramGroup("Stable region", 20.0, 20.0, 420.0, 260.0);
                sheet.Groups.Add(group);

                MainWindowProbe.Call(window, "RenderDiagram");
                MainWindowProbe.Call(window, "SelectGroup", group.Id);
                for (var pass = 0; pass < 5; pass++)
                {
                    MainWindowProbe.Call(window, "RenderDiagram");
                }

                var canvas = MainWindowProbe.Element<Canvas>(window, "DiagramCanvas");
                var adorners = canvas.Children
                    .OfType<Grid>()
                    .Where(IsGroupResizeAdorner)
                    .ToList();
                var adorner = Assert.Single(adorners);
                Assert.Equal(
                    8,
                    adorner.Children.OfType<Border>().Count(item =>
                        item.Uid.StartsWith("GroupResizeHandle-", StringComparison.Ordinal)));

                MainWindowProbe.Call(window, "ClearDiagramSelection");

                Assert.Null(MainWindowProbe.Field<Grid>(window, "_groupResizeAdorner"));
                Assert.DoesNotContain(
                    canvas.Children.OfType<Grid>(),
                    IsGroupResizeAdorner);
            });
        }

        [Fact]
        public void NewNestedGroupBoundsAreClampedToTheirDirectParent()
        {
            OnWindow(window =>
            {
                var sheet = MainWindowProbe.Field<CallSheet>(window, "_sheet");
                var node = sheet.Nodes.First();
                node.X = 110.0;
                node.Y = 130.0;
                var parent = new DiagramGroup("Parent", 80.0, 80.0, 650.0, 420.0);
                parent.MemberNodeIds.Add(node.Id);
                sheet.Groups.Add(parent);

                var clamped = Assert.IsType<Rect>(MainWindowProbe.Call(
                    window,
                    "ClampGroupBounds",
                    new Rect(10.0, 10.0, 900.0, 700.0),
                    new List<Guid> { node.Id }));

                Assert.Equal(parent.X, clamped.X);
                Assert.Equal(parent.Y, clamped.Y);
                Assert.Equal(parent.Width, clamped.Width);
                Assert.Equal(parent.Height, clamped.Height);
            });
        }

        [Fact]
        public void RootGroupMoveCanReachTheSafeExtentInsteadOfStoppingAtCurrentSheetSize()
        {
            OnWindow(window =>
            {
                var sheet = MainWindowProbe.Field<CallSheet>(window, "_sheet");
                var group = new DiagramGroup("Movable root", 200.0, 180.0, 400.0, 260.0);
                sheet.Groups.Add(group);

                var delta = Assert.IsType<Vector>(MainWindowProbe.Call(
                    window,
                    "ClampGroupMoveDelta",
                    group.Id,
                    new Vector(100000.0, 100000.0)));

                Assert.Equal(50000.0 - group.X - group.Width, delta.X, 6);
                Assert.Equal(50000.0 - group.Y - group.Height, delta.Y, 6);
                Assert.True(delta.X > sheet.Width);
                Assert.True(delta.Y > sheet.Height);
            });
        }

        [Fact]
        public void RenderingRepeatedlyDoesNotAccumulateVisuals()
        {
            // A leak here shows up as an editor that slows down over an afternoon.
            OnWindow(window =>
            {
                MainWindowProbe.Call(window, "RenderDiagram");
                var first = MainWindowProbe.Field<Dictionary<Guid, FrameworkElement>>(window, "_nodeVisuals").Count;

                for (var pass = 0; pass < 10; pass++)
                {
                    MainWindowProbe.Call(window, "RenderDiagram");
                }

                var last = MainWindowProbe.Field<Dictionary<Guid, FrameworkElement>>(window, "_nodeVisuals").Count;
                Assert.Equal(first, last);
            });
        }

        [Fact]
        public void EveryEditorDialogCanBeConstructed()
        {
            _host.Invoke(() =>
            {
                var window = new MainWindow();
                try
                {
                    var project = MainWindowProbe.Field<DiagramProject>(window, "_project");
                    var sheet = MainWindowProbe.Field<CallSheet>(window, "_sheet");

                    var constant = new ConstantNodeCreateWindow(project, sheet.Id);
                    Assert.NotNull(constant.FindName("LiteralTextBox"));
                    Assert.NotNull(constant.FindName("DataTypeTextBox"));
                    constant.Close();

                    var tag = new TagNodeCreateWindow(project, sheet.Id, TerminalDirection.Source);
                    Assert.NotNull(tag.FindName("NewTagPanel"));
                    Assert.NotNull(tag.FindName("ExistingTagComboBox"));
                    tag.Close();

                    var group = new GroupEditorWindow("Smoke region", "comment");
                    Assert.NotNull(group.FindName("TitleTextBox"));
                    Assert.NotNull(group.FindName("CommentTextBox"));
                    group.Close();

                    var geometryGroup = new GroupEditorWindow(
                        "Geometry region",
                        "comment",
                        12.5,
                        24.5,
                        420.0,
                        260.0);
                    Assert.Equal(
                        Visibility.Visible,
                        Assert.IsType<StackPanel>(geometryGroup.FindName("GeometryPanel")).Visibility);
                    Assert.NotNull(geometryGroup.FindName("XTextBox"));
                    Assert.NotNull(geometryGroup.FindName("YTextBox"));
                    Assert.NotNull(geometryGroup.FindName("WidthTextBox"));
                    Assert.NotNull(geometryGroup.FindName("HeightTextBox"));
                    geometryGroup.Close();

                    var sheetProperties = new SheetPropertiesWindow(
                        "Smoke sheet",
                        2400.0,
                        1600.0,
                        candidate => string.IsNullOrWhiteSpace(candidate)
                            ? "Name is required."
                            : string.Empty);
                    Assert.NotNull(sheetProperties.FindName("NameTextBox"));
                    Assert.NotNull(sheetProperties.FindName("WidthTextBox"));
                    Assert.NotNull(sheetProperties.FindName("HeightTextBox"));
                    sheetProperties.Close();
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void LegacyAddressEditRequiresAnExplicitCpuChoice()
        {
            _host.Invoke(() =>
            {
                var addressEditor = new PlcAddressEditorControl();
                addressEditor.LoadValue(PlcCpuFamily.Unknown, "Real", "%MD10000");

                PlcCpuFamily cpuFamily;
                string dataType;
                string address;
                string error;
                Assert.False(addressEditor.TryGetValue(
                    out cpuFamily,
                    out dataType,
                    out address,
                    out error));
                Assert.Equal(PlcCpuFamily.Unknown, cpuFamily);

                var cpuCombo = Assert.IsType<ComboBox>(addressEditor.FindName("CpuComboBox"));
                Assert.Equal(3, cpuCombo.Items.Count);
                cpuCombo.SelectedIndex = 2;

                Assert.True(addressEditor.TryGetValue(
                    out cpuFamily,
                    out dataType,
                    out address,
                    out error), error);
                Assert.Equal(PlcCpuFamily.S71500, cpuFamily);
                Assert.Equal("Real", dataType);
                Assert.Equal("%MD10000", address);
            });
        }

        [Fact]
        public void OfflineTagPropertiesAllowAnExplicitCpuMigration()
        {
            _host.Invoke(() =>
            {
                var mainWindow = new MainWindow();
                try
                {
                    var project = MainWindowProbe.Field<DiagramProject>(mainWindow, "_project");
                    project.Plant.CpuFamily = PlcCpuFamily.Unknown;
                    var tagNode = project.Sheets
                        .SelectMany(sheet => sheet.Nodes)
                        .OfType<TagNode>()
                        .First();

                    var editor = new NodePropertiesWindow(tagNode, project);
                    try
                    {
                        var addressEditor = Assert.IsType<PlcAddressEditorControl>(
                            editor.FindName("TagAddressEditor"));
                        Assert.True(addressEditor.IsCpuSelectionEnabled);
                    }
                    finally
                    {
                        editor.Close();
                    }
                }
                finally
                {
                    mainWindow.Close();
                }
            });
        }

        [Fact]
        public void TheDiagnosticsGridIsBoundAndReportsNoErrorsForTheDemo()
        {
            OnWindow(window =>
            {
                var grid = MainWindowProbe.Element<DataGrid>(window, "DiagnosticsGrid");

                Assert.NotNull(grid.ItemsSource);
                Assert.DoesNotContain(
                    grid.ItemsSource.Cast<object>(),
                    item => item.ToString().IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0);
            });
        }

        [Fact]
        public void OpeningAndClosingTheWindowRepeatedlyIsStable()
        {
            // Each open runs Openness discovery and starts the gateway worker.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                OnWindow(window => Assert.NotNull(MainWindowProbe.Field<CallSheet>(window, "_sheet")));
            }
        }

        [Fact]
        public void UdtLibraryCardWithAMemberMaterializesWithoutADispatcherBindingFailure()
        {
            UdtLibraryWindow window = null;
            Exception dispatcherFailure = null;
            System.Windows.Threading.DispatcherUnhandledExceptionEventHandler handler =
                (sender, eventArgs) =>
                {
                    dispatcherFailure = eventArgs.Exception;
                    eventArgs.Handled = true;
                };

            try
            {
                _host.Invoke(() =>
                {
                    Application.Current.DispatcherUnhandledException += handler;
                    var project = new DiagramProject();
                    project.Plant.DataTypes.Add(new UdtDefinition
                    {
                        Name = "Payload",
                        Members = new List<UdtMember>
                        {
                            new UdtMember("Value", "Bool")
                        }
                    });

                    window = new UdtLibraryWindow(project);
                    window.Show();
                    window.UpdateLayout();
                });

                _host.DrainDispatcher();
                _host.Invoke(() =>
                {
                    var list = MainWindowProbe.Element<ListBox>(window, "DataTypeList");
                    list.UpdateLayout();
                    Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(0));
                });
                _host.DrainDispatcher();

                Assert.Null(dispatcherFailure);
            }
            finally
            {
                _host.Invoke(() =>
                {
                    Application.Current.DispatcherUnhandledException -= handler;
                    if (window != null)
                    {
                        window.Close();
                    }
                });
            }
        }

        private static string Fingerprint(CallSheet sheet)
        {
            return string.Join(
                "|",
                sheet.Nodes
                    .OrderBy(node => node.Id)
                    .Select(node => node.Id + ":" + node.Title + ":" + node.X + ":" + node.Y)
                    .Concat(sheet.Wires.OrderBy(wire => wire.Id).Select(wire => wire.Id.ToString())));
        }

        private static bool IsGroupResizeAdorner(Grid candidate)
        {
            return candidate != null && candidate.Children
                .OfType<Border>()
                .Any(item => item.Uid.StartsWith("GroupResizeHandle-", StringComparison.Ordinal));
        }
    }
}
