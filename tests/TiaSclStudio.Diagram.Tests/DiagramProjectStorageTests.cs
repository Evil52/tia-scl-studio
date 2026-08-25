using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Storage;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Diagram.Tests
{
    /// <summary>
    /// The .tiasclproj file is the user's work. A save that half-succeeds, or a
    /// load that accepts a file it cannot really represent, loses hours of
    /// engineering, so these tests concentrate on interrupted writes and on
    /// files this build has no business reading.
    /// </summary>
    public sealed class DiagramProjectStorageTests
    {
        private static DiagramProjectStorage Storage()
        {
            return new DiagramProjectStorage();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void RejectsABlankPath(string path)
        {
            Assert.Throws<ArgumentException>(() => Storage().Save(path, ModelBuilder.Project()));
            Assert.Throws<ArgumentException>(() => Storage().Load(path));
        }

        [Fact]
        public void RejectsANullProject()
        {
            using (var directory = new TemporaryDirectory())
            {
                Assert.Throws<ArgumentNullException>(
                    () => Storage().Save(directory.File("a.tiasclproj"), null));
            }
        }

        [Fact]
        public void RoundTripsTheReferenceProject()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("valve.tiasclproj");
                var project = DemoProjects.Valve();

                Storage().Save(path, project);
                var reloaded = Storage().Load(path);

                Assert.Equal(project.Plant.Name, reloaded.Plant.Name);
                Assert.Equal(project.Plant.Blocks.Count, reloaded.Plant.Blocks.Count);
                Assert.Equal(project.Sheets[0].Nodes.Count, reloaded.Sheets[0].Nodes.Count);
                Assert.Equal(project.Sheets[0].Wires.Count, reloaded.Sheets[0].Wires.Count);
            }
        }

        [Fact]
        public void RoundTripsAGlobalDbVariableTerminal()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("db-variable.tiasclproj");
                var project = ModelBuilder.Project();
                project.Plant.DataTypes.Add(ModelBuilder.Udt("Valve_Data"));
                var definition = ModelBuilder.DbVariable("DB_Valve", "Data", "Valve_Data");
                project.Plant.DataBlockVariables.Add(definition);
                var node = ModelBuilder.PlaceDbVariable(
                    project.Sheets[0],
                    definition,
                    TerminalDirection.Source);

                Storage().Save(path, project);
                var reloaded = Storage().Load(path);
                var reloadedNode = Assert.IsType<DataBlockVariableNode>(reloaded.Sheets[0].Nodes.Single());

                Assert.Equal(DiagramProject.CurrentFormatVersion, reloaded.FormatVersion);
                Assert.Equal(definition.Id, reloaded.Plant.DataBlockVariables.Single().Id);
                Assert.Equal(node.DefinitionId, reloadedNode.DefinitionId);
                Assert.Equal("DB_Valve", reloadedNode.DataBlockName);
                Assert.Equal("Data", reloadedNode.VariableName);
                Assert.Equal("Valve_Data", reloadedNode.DataType);
            }
        }

        [Fact]
        public void RoundTripPreservesEveryStableIdentity()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("valve.tiasclproj");
                var project = DemoProjects.Valve();
                var nodeIds = project.Sheets[0].Nodes.Select(node => node.Id).ToArray();
                var pinIds = ModelBuilder.AllPins(project.Sheets[0]).Select(pin => pin.Id).ToArray();
                var blockId = project.Plant.Blocks[0].Id;

                Storage().Save(path, project);
                var reloaded = Storage().Load(path);

                Assert.Equal(nodeIds, reloaded.Sheets[0].Nodes.Select(node => node.Id).ToArray());
                Assert.Equal(pinIds, ModelBuilder.AllPins(reloaded.Sheets[0]).Select(pin => pin.Id).ToArray());
                Assert.Equal(blockId, reloaded.Plant.Blocks[0].Id);
            }
        }

        [Fact]
        public void RoundTripPreservesNodeSubtypes()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("mixed.tiasclproj");
                var project = DemoProjects.LogicTree();
                ModelBuilder.PlaceConstant(project.Sheets[0], "T#3s", "Time");
                ModelBuilder.PlaceNote(project.Sheets[0], "a note");

                Storage().Save(path, project);
                var sheet = Storage().Load(path).Sheets[0];

                Assert.Single(sheet.Nodes.OfType<BlockCallNode>());
                Assert.Equal(2, sheet.Nodes.OfType<TagNode>().Count());
                Assert.Equal(2, sheet.Nodes.OfType<LogicNode>().Count());
                Assert.Single(sheet.Nodes.OfType<ConstantNode>());
                Assert.Single(sheet.Nodes.OfType<NoteNode>());
            }
        }

        [Fact]
        public void RoundTripPreservesCyrillicText()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("ru.tiasclproj");
                var project = DemoProjects.Valve();
                project.Plant.Blocks[0].Description = "Клапан подачи охлаждающей жидкости";
                ModelBuilder.PlaceNote(project.Sheets[0], "Заметка про участок №3");

                Storage().Save(path, project);
                var reloaded = Storage().Load(path);

                Assert.Equal(
                    "Клапан подачи охлаждающей жидкости",
                    reloaded.Plant.Blocks[0].Description);
                Assert.Equal(
                    "Заметка про участок №3",
                    reloaded.Sheets[0].Nodes.OfType<NoteNode>().Single().Text);
            }
        }

        [Fact]
        public void RoundTripPreservesVisualGroupsIncludingNesting()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("groups.tiasclproj");
                var project = DemoProjects.Valve();
                var parent = new DiagramGroup("Line", 10.0, 10.0, 600.0, 400.0);
                var child = new DiagramGroup("Unit", 20.0, 20.0, 200.0, 150.0)
                {
                    ParentGroupId = parent.Id
                };
                child.MemberNodeIds.Add(project.Sheets[0].Nodes[0].Id);
                project.Sheets[0].Groups.Add(parent);
                project.Sheets[0].Groups.Add(child);

                Storage().Save(path, project);
                var groups = Storage().Load(path).Sheets[0].Groups;

                Assert.Equal(2, groups.Count);
                Assert.Equal(parent.Id, groups.Single(group => group.Title == "Unit").ParentGroupId);
                Assert.Equal(
                    project.Sheets[0].Nodes[0].Id,
                    groups.Single(group => group.Title == "Unit").MemberNodeIds.Single());
            }
        }

        [Fact]
        public void SaveWritesUtf8WithABomAndWindowsLineEndings()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("a.tiasclproj");

                Storage().Save(path, ModelBuilder.Project());

                var bytes = File.ReadAllBytes(path);
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());

                var text = File.ReadAllText(path, new UTF8Encoding(true));
                Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty), StringComparison.Ordinal);
            }
        }

        [Fact]
        public void SaveStampsTheCurrentFormatVersion()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("a.tiasclproj");
                var project = ModelBuilder.Project();
                project.FormatVersion = DiagramProject.LegacyFormatVersion;

                Storage().Save(path, project);

                // A v1-only build must reject the file rather than silently drop
                // the visual groups that only v2 knows about.
                Assert.Equal(DiagramProject.CurrentFormatVersion, project.FormatVersion);
                Assert.Equal(DiagramProject.CurrentFormatVersion, Storage().Load(path).FormatVersion);
            }
        }

        [Fact]
        public void SaveCreatesMissingDirectories()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File(Path.Combine("a", "b", "c.tiasclproj"));

                Storage().Save(path, ModelBuilder.Project());

                Assert.True(File.Exists(path));
            }
        }

        [Fact]
        public void SaveLeavesNoTemporaryFileBehind()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("a.tiasclproj");

                Storage().Save(path, ModelBuilder.Project());
                Storage().Save(path, ModelBuilder.Project());

                Assert.Equal(new[] { "a.tiasclproj" }, Directory
                    .GetFiles(directory.Path)
                    .Select(Path.GetFileName)
                    .OrderBy(name => name)
                    .ToArray());
            }
        }

        /// <summary>
        /// A project whose free text carries a character XML cannot represent
        /// cannot be written at all. Pasting from a terminal log or a binary
        /// dump is enough to produce one, so this is the realistic way a save
        /// fails half way through.
        /// </summary>
        private static DiagramProject ProjectThatCannotBeSerialized()
        {
            var project = DemoProjects.Valve();
            project.Plant.Blocks[0].Description = "pasted from a log " + (char)0x00 + " and back";
            return project;
        }

        /// <summary>
        /// The save is staged through a temporary file and swapped in. When it
        /// fails, the previous version of the project must still be on disk
        /// intact, because that file is the only copy of the user's work.
        /// </summary>
        [Fact]
        public void AFailedSaveLeavesThePreviousFileUntouched()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("a.tiasclproj");
                Storage().Save(path, DemoProjects.Valve());
                var original = File.ReadAllBytes(path);

                Assert.ThrowsAny<Exception>(() => Storage().Save(path, ProjectThatCannotBeSerialized()));

                Assert.Equal(original, File.ReadAllBytes(path));
                Assert.Single(Directory.GetFiles(directory.Path));
            }
        }

        [Fact]
        public void AFailedSaveLeavesNoTemporaryFileBehind()
        {
            using (var directory = new TemporaryDirectory())
            {
                Assert.ThrowsAny<Exception>(
                    () => Storage().Save(directory.File("a.tiasclproj"), ProjectThatCannotBeSerialized()));

                Assert.Empty(Directory.GetFiles(directory.Path));
            }
        }

        [Fact]
        public void AFailedSaveRestoresTheProjectsOwnFormatVersion()
        {
            using (var directory = new TemporaryDirectory())
            {
                var project = ProjectThatCannotBeSerialized();
                project.FormatVersion = DiagramProject.LegacyFormatVersion;

                Assert.ThrowsAny<Exception>(
                    () => Storage().Save(directory.File("a.tiasclproj"), project));

                Assert.Equal(DiagramProject.LegacyFormatVersion, project.FormatVersion);
            }
        }

        /// <summary>
        /// Storage cannot rescue such a project, but validation can name the
        /// field before the user gets stranded with work that will not save.
        /// </summary>
        [Fact]
        public void ValidationNamesTheFieldThatMakesAProjectUnsaveable()
        {
            var project = ProjectThatCannotBeSerialized();

            var result = new TiaSclStudio.Core.Validation.ProjectValidator().Validate(project.Plant);

            Assert.Contains(
                result.Issues,
                issue => issue.Code == "NON_PERSISTABLE_TEXT" &&
                    issue.Path.EndsWith(".Description", StringComparison.Ordinal));
        }

        [Fact]
        public void SaveRefusesAnUnsupportedFormatVersionBeforeTouchingTheDisk()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("a.tiasclproj");
                var project = ModelBuilder.Project();
                project.FormatVersion = 99;

                Assert.Throws<NotSupportedException>(() => Storage().Save(path, project));
                Assert.Empty(Directory.GetFiles(directory.Path));
            }
        }

        [Fact]
        public void LoadRefusesAFileFromANewerFormat()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("a.tiasclproj");
                Storage().Save(path, ModelBuilder.Project());
                var text = File.ReadAllText(path, new UTF8Encoding(true))
                    .Replace(
                        "FormatVersion=\"" + DiagramProject.CurrentFormatVersion + "\"",
                        "FormatVersion=\"" + (DiagramProject.CurrentFormatVersion + 1) + "\"");
                File.WriteAllText(path, text, new UTF8Encoding(true));

                Assert.Throws<NotSupportedException>(() => Storage().Load(path));
            }
        }

        [Fact]
        public void LoadAcceptsTheLegacyFormatVersion()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("a.tiasclproj");
                Storage().Save(path, DemoProjects.Valve());
                var text = File.ReadAllText(path, new UTF8Encoding(true))
                    .Replace(
                        "FormatVersion=\"" + DiagramProject.CurrentFormatVersion + "\"",
                        "FormatVersion=\"" + DiagramProject.LegacyFormatVersion + "\"");
                text = Regex.Replace(
                    text,
                    "\\s*<CpuFamily>[^<]*</CpuFamily>",
                    string.Empty,
                    RegexOptions.CultureInvariant);
                File.WriteAllText(path, text, new UTF8Encoding(true));

                var reloaded = Storage().Load(path);

                Assert.Equal(DiagramProject.LegacyFormatVersion, reloaded.FormatVersion);
                Assert.Equal(PlcCpuFamily.Unknown, reloaded.Plant.CpuFamily);
                Assert.Single(reloaded.Sheets);
            }
        }

        [Fact]
        public void LoadAcceptsTheGroupFormatVersionWithoutACpuProfile()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("v2.tiasclproj");
                Storage().Save(path, DemoProjects.Valve());
                var text = File.ReadAllText(path, new UTF8Encoding(true))
                    .Replace(
                        "FormatVersion=\"" + DiagramProject.CurrentFormatVersion + "\"",
                        "FormatVersion=\"" + DiagramProject.GroupFormatVersion + "\"");
                text = Regex.Replace(
                    text,
                    "\\s*<CpuFamily>[^<]*</CpuFamily>",
                    string.Empty,
                    RegexOptions.CultureInvariant);
                File.WriteAllText(path, text, new UTF8Encoding(true));

                var reloaded = Storage().Load(path);

                Assert.Equal(DiagramProject.GroupFormatVersion, reloaded.FormatVersion);
                Assert.Equal(PlcCpuFamily.Unknown, reloaded.Plant.CpuFamily);
                Assert.Single(reloaded.Sheets);
            }
        }

        [Fact]
        public void LoadReportsAMissingFile()
        {
            using (var directory = new TemporaryDirectory())
            {
                Assert.Throws<FileNotFoundException>(() => Storage().Load(directory.File("gone.tiasclproj")));
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("not xml at all")]
        [InlineData("<TiaSclStudioProject")]
        [InlineData("<Wrong FormatVersion=\"2\" />")]
        [InlineData("<TiaSclStudioProject FormatVersion=\"2\"><Sheets><Sheet><Width>oops</Width></Sheet></Sheets></TiaSclStudioProject>")]
        public void LoadRefusesAFileThatIsNotAProject(string content)
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.WriteAllText("broken.tiasclproj", content);

                Assert.ThrowsAny<Exception>(() => Storage().Load(path));
            }
        }

        /// <summary>
        /// A project file can arrive by e-mail from a colleague. Resolving an
        /// external entity from it would turn opening a diagram into a file
        /// read or an outbound request on the engineer's machine.
        /// </summary>
        [Fact]
        public void LoadRefusesADocumentTypeDeclaration()
        {
            using (var directory = new TemporaryDirectory())
            {
                var secret = directory.WriteAllText("secret.txt", "confidential");
                var xml =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<!DOCTYPE TiaSclStudioProject [<!ENTITY leak SYSTEM \"file:///" +
                    secret.Replace('\\', '/') + "\">]>" +
                    "<TiaSclStudioProject FormatVersion=\"2\"><Plant><Name>&leak;</Name></Plant></TiaSclStudioProject>";
                var path = directory.WriteAllText("xxe.tiasclproj", xml);

                var exception = Assert.ThrowsAny<Exception>(() => Storage().Load(path));

                Assert.DoesNotContain("confidential", exception.ToString(), StringComparison.Ordinal);
            }
        }

        [Fact]
        public void SaveReplacesAnExistingFileInPlace()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("a.tiasclproj");
                Storage().Save(path, ModelBuilder.Project("First"));

                Storage().Save(path, ModelBuilder.Project("Second"));

                Assert.Equal("Second", Storage().Load(path).Plant.Name);
            }
        }

        [Fact]
        public void SaveSurfacesAReadOnlyTargetWithoutLosingTheOldContent()
        {
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("a.tiasclproj");
                Storage().Save(path, ModelBuilder.Project("First"));
                File.SetAttributes(path, FileAttributes.ReadOnly);
                try
                {
                    Assert.ThrowsAny<Exception>(() => Storage().Save(path, ModelBuilder.Project("Second")));
                }
                finally
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                }

                Assert.Equal("First", Storage().Load(path).Plant.Name);
            }
        }

        [Fact]
        public void ConcurrentSavesToTheSameFileAllLeaveAReadableProject()
        {
            // Two windows on the same project, or an autosave racing a manual
            // save, must not be able to produce a truncated file.
            using (var directory = new TemporaryDirectory())
            {
                var path = directory.File("a.tiasclproj");
                Storage().Save(path, ModelBuilder.Project("Seed"));

                var failures = 0;
                var threads = Enumerable.Range(0, 4).Select(index => new Thread(() =>
                {
                    for (var attempt = 0; attempt < 10; attempt++)
                    {
                        try
                        {
                            Storage().Save(path, ModelBuilder.Project("Writer" + index));
                        }
                        catch (IOException)
                        {
                            Interlocked.Increment(ref failures);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            Interlocked.Increment(ref failures);
                        }
                    }
                })).ToList();

                foreach (var thread in threads)
                {
                    thread.Start();
                }

                foreach (var thread in threads)
                {
                    Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "A writer thread did not finish.");
                }

                var reloaded = Storage().Load(path);
                Assert.StartsWith("Writer", reloaded.Plant.Name, StringComparison.Ordinal);

                var leftovers = Directory.GetFiles(directory.Path)
                    .Select(Path.GetFileName)
                    .Where(name => !string.Equals(name, "a.tiasclproj", StringComparison.Ordinal))
                    .ToArray();
                Assert.True(
                    leftovers.Length == 0,
                    "Concurrent saves left " + leftovers.Length + " staging file(s) in the project " +
                    "directory after " + failures + " failed save(s): " + string.Join(", ", leftovers));
            }
        }

        [Fact]
        public void SavedFilesAreByteIdenticalForTheSameModel()
        {
            using (var directory = new TemporaryDirectory())
            {
                var project = DemoProjects.Valve();
                var first = directory.File("first.tiasclproj");
                var second = directory.File("second.tiasclproj");

                Storage().Save(first, project);
                Storage().Save(second, project);

                Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
            }
        }
    }
}
