using System;
using System.IO;
using System.Linq;
using System.Text;
using TiaSclStudio.Core.Generation;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Storage;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Core.Tests
{
    /// <summary>
    /// ProjectStorage is the last hop before bytes leave the process. TIA
    /// Portal only reads the external source correctly when it is UTF-8 with a
    /// BOM, and the file name is attacker-influenced in the sense that it is
    /// derived from user-entered block names.
    /// </summary>
    public sealed class ProjectStorageTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void WriteSourceRejectsABlankPath(string path)
        {
            Assert.Throws<ArgumentException>(() => ProjectStorage.WriteSource(path, "x"));
        }

        [Fact]
        public void WriteSourceRejectsNullContent()
        {
            using (var directory = new TemporaryDirectory())
            {
                Assert.Throws<ArgumentNullException>(
                    () => ProjectStorage.WriteSource(directory.File("a.scl"), null));
            }
        }

        [Fact]
        public void WriteSourceCreatesMissingDirectories()
        {
            using (var directory = new TemporaryDirectory())
            {
                var target = directory.File(Path.Combine("a", "b", "c.scl"));

                ProjectStorage.WriteSource(target, "content");

                Assert.True(File.Exists(target));
            }
        }

        [Fact]
        public void WriteSourceEmitsUtf8WithABom()
        {
            using (var directory = new TemporaryDirectory())
            {
                var target = directory.File("a.scl");

                ProjectStorage.WriteSource(target, "AB");

                var bytes = File.ReadAllBytes(target);
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, 0x41, 0x42 }, bytes);
            }
        }

        [Fact]
        public void WriteSourcePreservesCyrillicComments()
        {
            using (var directory = new TemporaryDirectory())
            {
                var target = directory.File("a.scl");
                const string content = "// Клапан подачи\r\nFUNCTION_BLOCK \"FB_Valve\"\r\n";

                ProjectStorage.WriteSource(target, content);

                Assert.Equal(content, File.ReadAllText(target, new UTF8Encoding(true)));
            }
        }

        [Fact]
        public void WriteSourceDoesNotAppendATrailingNewline()
        {
            using (var directory = new TemporaryDirectory())
            {
                var target = directory.File("a.scl");

                ProjectStorage.WriteSource(target, "no-newline");

                var text = File.ReadAllText(target);
                Assert.EndsWith("no-newline", text, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void WriteSourceTruncatesAnExistingLongerFile()
        {
            using (var directory = new TemporaryDirectory())
            {
                var target = directory.File("a.scl");
                ProjectStorage.WriteSource(target, new string('x', 5000));

                ProjectStorage.WriteSource(target, "short");

                Assert.Equal("short", File.ReadAllText(target, new UTF8Encoding(true)));
            }
        }

        [Fact]
        public void WriteSourceAcceptsAnEmptyDocument()
        {
            using (var directory = new TemporaryDirectory())
            {
                var target = directory.File("a.scl");

                ProjectStorage.WriteSource(target, string.Empty);

                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(target));
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void WriteSourcesRejectsABlankDirectory(string path)
        {
            Assert.Throws<ArgumentException>(
                () => ProjectStorage.WriteSources(path, new GeneratedSource[0]));
        }

        [Fact]
        public void WriteSourcesRejectsANullCollection()
        {
            using (var directory = new TemporaryDirectory())
            {
                Assert.Throws<ArgumentNullException>(
                    () => ProjectStorage.WriteSources(directory.Path, null));
            }
        }

        [Fact]
        public void WriteSourcesSkipsNullItems()
        {
            using (var directory = new TemporaryDirectory())
            {
                var sources = new[]
                {
                    null,
                    new GeneratedSource("a.scl", "A", GeneratedSourceKind.Block, 0)
                };

                ProjectStorage.WriteSources(directory.Path, sources);

                Assert.Single(Directory.GetFiles(directory.Path));
            }
        }

        [Theory]
        [InlineData("..\\escape.scl")]
        [InlineData("../escape.scl")]
        [InlineData("sub\\escape.scl")]
        [InlineData("sub/escape.scl")]
        [InlineData("C:\\escape.scl")]
        [InlineData("\\\\server\\share\\escape.scl")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void WriteSourcesRefusesToLeaveTheOutputDirectory(string fileName)
        {
            using (var directory = new TemporaryDirectory())
            {
                var sources = new[] { new GeneratedSource(fileName, "A", GeneratedSourceKind.Block, 0) };

                Assert.ThrowsAny<Exception>(() => ProjectStorage.WriteSources(directory.Path, sources));
                Assert.Empty(Directory.GetFiles(directory.Path));
            }
        }

        [Theory]
        [InlineData("a<b.scl")]
        [InlineData("a>b.scl")]
        [InlineData("a|b.scl")]
        [InlineData("a:b.scl")]
        [InlineData("a?b.scl")]
        [InlineData("a*b.scl")]
        [InlineData("a\"b.scl")]
        public void WriteSourcesRefusesInvalidFileNameCharacters(string fileName)
        {
            using (var directory = new TemporaryDirectory())
            {
                var sources = new[] { new GeneratedSource(fileName, "A", GeneratedSourceKind.Block, 0) };

                Assert.Throws<InvalidOperationException>(
                    () => ProjectStorage.WriteSources(directory.Path, sources));
            }
        }

        /// <summary>
        /// Block names are user input and "CON", "PRN", "NUL", "COM1" and their
        /// friends stay reserved device names on Windows even with an extension.
        /// Opening one hands the writer a device instead of a file, so the SCL
        /// disappears and the bundle silently ships one source short.
        /// </summary>
        [Theory]
        [InlineData("CON.scl")]
        [InlineData("con.scl")]
        [InlineData("PRN.scl")]
        [InlineData("AUX.scl")]
        [InlineData("NUL.scl")]
        [InlineData("COM1.scl")]
        [InlineData("LPT1.scl")]
        public void WriteSourcesEitherRefusesOrActuallyWritesAReservedDeviceName(string fileName)
        {
            using (var directory = new TemporaryDirectory())
            {
                var sources = new[] { new GeneratedSource(fileName, "content", GeneratedSourceKind.Block, 0) };

                try
                {
                    ProjectStorage.WriteSources(directory.Path, sources);
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                catch (IOException)
                {
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }

                var written = Path.Combine(directory.Path, fileName);
                Assert.True(
                    File.Exists(written),
                    "WriteSources reported success but produced no file for " + fileName + ".");
            }
        }

        [Fact]
        public void WriteSourcesWritesEverySourceWithABom()
        {
            using (var directory = new TemporaryDirectory())
            {
                var sources = new[]
                {
                    new GeneratedSource("00_Types.scl", "TYPE\r\n", GeneratedSourceKind.DataTypes, 0),
                    new GeneratedSource("FB_A.scl", "FUNCTION_BLOCK\r\n", GeneratedSourceKind.Block, 1)
                };

                ProjectStorage.WriteSources(directory.Path, sources);

                foreach (var source in sources)
                {
                    var bytes = File.ReadAllBytes(Path.Combine(directory.Path, source.FileName));
                    Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
                }
            }
        }

        [Fact]
        public void WriteSourcesCreatesTheOutputDirectory()
        {
            using (var directory = new TemporaryDirectory())
            {
                var target = directory.File(Path.Combine("out", "deep"));

                ProjectStorage.WriteSources(target, new[]
                {
                    new GeneratedSource("a.scl", "A", GeneratedSourceKind.Block, 0)
                });

                Assert.True(File.Exists(Path.Combine(target, "a.scl")));
            }
        }

        [Fact]
        public void WriteSourcesRefusesADirectoryPathThatIsAlreadyAFile()
        {
            using (var directory = new TemporaryDirectory())
            {
                var occupied = directory.WriteAllText("occupied", "x");

                Assert.ThrowsAny<IOException>(() => ProjectStorage.WriteSources(occupied, new[]
                {
                    new GeneratedSource("a.scl", "A", GeneratedSourceKind.Block, 0)
                }));
            }
        }

        [Fact]
        public void WriteSourceSurfacesAReadOnlyTargetInsteadOfSwallowingIt()
        {
            using (var directory = new TemporaryDirectory())
            {
                var target = directory.WriteAllText("locked.scl", "old");
                File.SetAttributes(target, FileAttributes.ReadOnly);
                try
                {
                    Assert.Throws<UnauthorizedAccessException>(
                        () => ProjectStorage.WriteSource(target, "new"));
                    Assert.Equal("old", File.ReadAllText(target));
                }
                finally
                {
                    File.SetAttributes(target, FileAttributes.Normal);
                }
            }
        }

        [Fact]
        public void WriteSourceSurfacesAnExclusivelyLockedTarget()
        {
            using (var directory = new TemporaryDirectory())
            {
                var target = directory.File("busy.scl");
                using (new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    Assert.Throws<IOException>(() => ProjectStorage.WriteSource(target, "new"));
                }
            }
        }

        [Fact]
        public void WriteSourceAllowsAConcurrentReaderWhileWriting()
        {
            // The stream is opened with FileShare.Read so a running TIA import
            // can keep the file open without failing the write.
            using (var directory = new TemporaryDirectory())
            {
                var target = directory.WriteAllText("shared.scl", "old");
                using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    ProjectStorage.WriteSource(target, "new");
                }

                Assert.Equal("new", File.ReadAllText(target, new UTF8Encoding(true)));
            }
        }
    }
}
