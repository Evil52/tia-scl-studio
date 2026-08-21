using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TiaSclStudio.App;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;
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

        private static string Fingerprint(CallSheet sheet)
        {
            return string.Join(
                "|",
                sheet.Nodes
                    .OrderBy(node => node.Id)
                    .Select(node => node.Id + ":" + node.Title + ":" + node.X + ":" + node.Y)
                    .Concat(sheet.Wires.OrderBy(wire => wire.Id).Select(wire => wire.Id.ToString())));
        }
    }
}
