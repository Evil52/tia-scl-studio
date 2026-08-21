using System;
using System.IO;
using System.Linq;
using TiaSclStudio.Core.Storage;
using TiaSclStudio.Diagram.Generation;
using TiaSclStudio.Diagram.History;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Storage;
using TiaSclStudio.Openness.Gateway;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.EndToEnd.Tests
{
    /// <summary>
    /// The whole journey an engineer makes in one sitting: build a sheet, undo a
    /// mistake, save, close, reopen, compile, write the bundle and hand it to
    /// the gateway. Each step is covered elsewhere in isolation; what only shows
    /// up here is a step that quietly loses what the previous one produced.
    /// </summary>
    public sealed class UserJourneyTests
    {
        [Fact]
        public void BuildEditUndoSaveReopenCompileAndExport()
        {
            using (var directory = new TemporaryDirectory())
            {
                var storage = new DiagramProjectStorage();
                var history = new DiagramEditHistory();
                var projectPath = directory.File("Plant.tiasclproj");

                // 1. Start from a working sheet.
                var project = DemoProjects.Valve();
                var firstCompile = new DiagramProjectCompiler().Compile(project);
                Assert.True(firstCompile.IsSuccess, Describe(firstCompile));

                // 2. Add a second valve, then change your mind.
                var before = DiagramProjectSnapshot.Clone(project);
                var block = project.Plant.Blocks[0];
                ModelBuilder.PlaceBlock(project.Sheets[0], block, "V02", 400.0, 500.0);
                Assert.True(history.Record("add V02", before, project));

                project = history.Undo();
                Assert.Single(project.Sheets[0].Nodes.OfType<BlockCallNode>());

                // 3. Redo it and keep it this time.
                project = history.Redo();
                Assert.Equal(2, project.Sheets[0].Nodes.OfType<BlockCallNode>().Count());

                // 4. Save, close the tool, reopen.
                storage.Save(projectPath, project);
                var reopened = storage.Load(projectPath);
                Assert.Equal(
                    project.Sheets[0].Nodes.Count,
                    reopened.Sheets[0].Nodes.Count);

                // 5. Compile the reopened project and write the bundle.
                var compile = new DiagramProjectCompiler().Compile(reopened);
                Assert.True(compile.IsSuccess, Describe(compile));

                var exportDirectory = directory.File("export");
                ProjectStorage.WriteSources(exportDirectory, compile.GeneratedSources);
                Assert.Equal(compile.GeneratedSources.Count, Directory.GetFiles(exportDirectory).Length);

                // Both valves need their own instance data block.
                var instances = File.ReadAllText(
                    Directory.GetFiles(exportDirectory, "90_Instances*.scl").Single());
                Assert.Contains("DATA_BLOCK \"V01\"", instances, StringComparison.Ordinal);
                Assert.Contains("DATA_BLOCK \"V02\"", instances, StringComparison.Ordinal);

                // 6. Hand it to the gateway. Offline means nothing is touched.
                var request = new TiaExportRequest(
                    directory.File("Plant.ap17"),
                    compile.GeneratedSources.Select((source, index) => new TiaSourceFile(
                        Path.Combine(exportDirectory, source.FileName),
                        index,
                        TiaSourceKind.Block)),
                    compileAfterImport: true);

                using (var gateway = new OfflineGateway())
                {
                    var preview = gateway.PreviewExport(request);
                    var result = gateway.Export(request, preview.ConfirmationToken);

                    Assert.False(preview.CanExecute);
                    Assert.Equal(TiaExportOutcome.NotExecuted, result.Outcome);
                    Assert.False(File.Exists(directory.File("Plant.ap17")));
                }
            }
        }

        [Fact]
        public void AMistakeIsCaughtBeforeAnythingReachesDisk()
        {
            using (var directory = new TemporaryDirectory())
            {
                var project = DemoProjects.Valve();

                // Someone edits the library and changes a parameter's type
                // without refreshing the sheet.
                project.Plant.Blocks[0].Interface
                    .Single(member => member.Name == "Open")
                    .DataType = "Int";

                var compile = new DiagramProjectCompiler().Compile(project);

                Assert.False(compile.IsSuccess);
                Assert.NotEmpty(compile.Issues);

                var exportDirectory = directory.File("export");
                ProjectStorage.WriteSources(exportDirectory, compile.GeneratedSources);
                Assert.Empty(Directory.GetFiles(exportDirectory));
            }
        }

        [Fact]
        public void ClosingWithoutSavingLosesNothingThatWasAlreadySaved()
        {
            using (var directory = new TemporaryDirectory())
            {
                var storage = new DiagramProjectStorage();
                var path = directory.File("Plant.tiasclproj");
                var project = DemoProjects.Valve();

                storage.Save(path, project);
                var savedBytes = File.ReadAllBytes(path);

                // Unsaved edits after the save must not reach the file.
                project.Sheets[0].Nodes.Clear();
                project.Plant.Name = "NeverSaved";

                Assert.Equal(savedBytes, File.ReadAllBytes(path));
                Assert.Equal("ValveDemo", storage.Load(path).Plant.Name);
            }
        }

        [Fact]
        public void ADamagedProjectFileIsRefusedRatherThanPartiallyLoaded()
        {
            using (var directory = new TemporaryDirectory())
            {
                var storage = new DiagramProjectStorage();
                var path = directory.File("Plant.tiasclproj");
                storage.Save(path, DemoProjects.Valve());

                var bytes = File.ReadAllBytes(path);
                File.WriteAllBytes(path, bytes.Take(bytes.Length / 2).ToArray());

                Assert.ThrowsAny<Exception>(() => storage.Load(path));
            }
        }

        [Fact]
        public void TheSameProjectExportedTwiceProducesIdenticalFiles()
        {
            using (var first = new TemporaryDirectory())
            using (var second = new TemporaryDirectory())
            {
                var project = DemoProjects.LogicTree();

                var compileOne = new DiagramProjectCompiler().Compile(project);
                ProjectStorage.WriteSources(first.Path, compileOne.GeneratedSources);

                var compileTwo = new DiagramProjectCompiler().Compile(project);
                ProjectStorage.WriteSources(second.Path, compileTwo.GeneratedSources);

                foreach (var file in Directory.GetFiles(first.Path))
                {
                    Assert.Equal(
                        File.ReadAllBytes(file),
                        File.ReadAllBytes(Path.Combine(second.Path, Path.GetFileName(file))));
                }
            }
        }

        [Fact]
        public void AProjectWithTwoSheetsExportsBothOfThem()
        {
            using (var directory = new TemporaryDirectory())
            {
                var project = DemoProjects.Valve();
                var extra = DemoProjects.Chain();
                project.Plant.Blocks.AddRange(extra.Plant.Blocks);
                project.Plant.Tags.AddRange(extra.Plant.Tags);
                project.Sheets.Add(extra.Sheets[0]);

                var compile = new DiagramProjectCompiler().Compile(project);
                Assert.True(compile.IsSuccess, Describe(compile));

                ProjectStorage.WriteSources(directory.Path, compile.GeneratedSources);

                var files = Directory.GetFiles(directory.Path).Select(Path.GetFileName).ToList();
                Assert.Contains("99_FC_CallUnits.scl", files);
                Assert.Contains("99_FC_Chain.scl", files);
                Assert.Equal(files.Count, files.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            }
        }

        private static string Describe(DiagramProjectCompilationResult result)
        {
            return string.Join(
                "; ",
                result.Issues.Select(issue => issue.Code + " " + issue.Message));
        }
    }
}
