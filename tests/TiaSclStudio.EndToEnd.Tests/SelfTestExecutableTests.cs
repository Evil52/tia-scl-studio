using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.EndToEnd.Tests
{
    /// <summary>
    /// Runs the shipped self-test executable as a separate process. That is the
    /// only check here that exercises real assembly loading, binding redirects
    /// and the x64 platform target of the deployed binaries, which an in-process
    /// test can never prove.
    /// </summary>
    public sealed class SelfTestExecutableTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

        private sealed class ProcessOutcome
        {
            public int ExitCode { get; set; }

            public string StandardOutput { get; set; }

            public string StandardError { get; set; }
        }

        private static string ExecutablePath()
        {
            var path = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "TiaSclStudio.SelfTest.exe");
            Assert.True(File.Exists(path), "The self-test executable was not copied next to the tests: " + path);
            return path;
        }

        /// <summary>
        /// Both streams are drained through the redirection events rather than
        /// read to the end one after the other. Reading one stream to completion
        /// while the other fills its pipe buffer deadlocks the child process,
        /// and the self-test writes to both.
        /// </summary>
        private static ProcessOutcome Run(string arguments, string workingDirectory = null)
        {
            var startInfo = new ProcessStartInfo(ExecutablePath(), arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? AppDomain.CurrentDomain.BaseDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var output = new StringBuilder();
            var error = new StringBuilder();

            using (var outputDrained = new ManualResetEventSlim(false))
            using (var errorDrained = new ManualResetEventSlim(false))
            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data == null)
                    {
                        outputDrained.Set();
                    }
                    else
                    {
                        output.AppendLine(args.Data);
                    }
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data == null)
                    {
                        errorDrained.Set();
                    }
                    else
                    {
                        error.AppendLine(args.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var milliseconds = (int)Timeout.TotalMilliseconds;
                if (!process.WaitForExit(milliseconds))
                {
                    process.Kill();
                    throw new TimeoutException("The self-test did not finish within " + Timeout + ".");
                }

                outputDrained.Wait(milliseconds);
                errorDrained.Wait(milliseconds);

                return new ProcessOutcome
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = output.ToString(),
                    StandardError = error.ToString()
                };
            }
        }

        private static ProcessOutcome RunIn(TemporaryDirectory directory)
        {
            return Run("\"" + directory.Path + "\"");
        }

        [Fact]
        public void TheSelfTestPassesAndReportsSuccessOnStandardOutput()
        {
            using (var directory = new TemporaryDirectory())
            {
                var outcome = RunIn(directory);

                Assert.True(
                    outcome.ExitCode == 0,
                    "The self-test exited with " + outcome.ExitCode + ".\n" +
                        outcome.StandardOutput + "\n" + outcome.StandardError);
                Assert.Contains("self-test passed", outcome.StandardOutput, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(string.Empty, outcome.StandardError.Trim());
            }
        }

        [Fact]
        public void TheSelfTestWritesTheImportBundleItClaimsTo()
        {
            using (var directory = new TemporaryDirectory())
            {
                RunIn(directory);

                var files = Directory.GetFiles(directory.Path).Select(Path.GetFileName).ToList();

                Assert.Contains("FB_Valve.scl", files);
                Assert.Contains("90_Instances.scl", files);
                Assert.Contains("99_FC_CallUnits.scl", files);
                Assert.Contains("ValveDemo.tiasclproj", files);
            }
        }

        [Fact]
        public void EverySclFileTheSelfTestWritesStartsWithAUtf8Bom()
        {
            using (var directory = new TemporaryDirectory())
            {
                RunIn(directory);

                foreach (var file in Directory.GetFiles(directory.Path, "*.scl"))
                {
                    var bytes = File.ReadAllBytes(file);
                    Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
                }
            }
        }

        [Fact]
        public void RunningTheSelfTestTwiceProducesByteIdenticalOutput()
        {
            // Non-deterministic generation would make every export look like a
            // change to whoever reviews the TIA project afterwards.
            using (var first = new TemporaryDirectory())
            using (var second = new TemporaryDirectory())
            {
                RunIn(first);
                RunIn(second);

                var files = Directory.GetFiles(first.Path, "*.scl");
                Assert.NotEmpty(files);

                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    Assert.Equal(
                        File.ReadAllBytes(file),
                        File.ReadAllBytes(Path.Combine(second.Path, name)));
                }
            }
        }

        [Fact]
        public void TheSelfTestDefaultsItsOutputDirectoryWhenGivenNoArguments()
        {
            using (var directory = new TemporaryDirectory())
            {
                var outcome = Run(string.Empty, directory.Path);

                Assert.Equal(0, outcome.ExitCode);
                Assert.True(
                    Directory.Exists(Path.Combine(directory.Path, "artifacts", "selftest")),
                    "The self-test did not fall back to its documented output directory.\n" +
                        outcome.StandardOutput);
            }
        }

        [Fact]
        public void TheSelfTestDoesNotWriteOutsideTheDirectoryItWasGiven()
        {
            using (var scratch = new TemporaryDirectory())
            {
                var target = Path.Combine(scratch.Path, "out");
                var sibling = Path.Combine(scratch.Path, "untouched");
                Directory.CreateDirectory(sibling);

                Run("\"" + target + "\"");

                Assert.Empty(Directory.GetFiles(sibling));
                Assert.Equal(
                    new[] { "out", "untouched" },
                    Directory.GetDirectories(scratch.Path).Select(Path.GetFileName).OrderBy(x => x).ToArray());
            }
        }
    }
}
