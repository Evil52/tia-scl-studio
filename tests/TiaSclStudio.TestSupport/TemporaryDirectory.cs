using System;
using System.IO;

namespace TiaSclStudio.TestSupport
{
    /// <summary>
    /// A disposable scratch directory. Tests that touch the filesystem must not
    /// share state, so each one gets its own directory under the temp root.
    /// </summary>
    public sealed class TemporaryDirectory : IDisposable
    {
        private bool _disposed;

        public TemporaryDirectory(string prefix = "tiascl")
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                prefix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; private set; }

        public string File(string relativePath)
        {
            return System.IO.Path.Combine(Path, relativePath);
        }

        public string WriteAllText(string relativePath, string content)
        {
            var target = File(relativePath);
            var directory = System.IO.Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            System.IO.File.WriteAllText(target, content);
            return target;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch (IOException)
            {
                // A leaked scratch directory must never fail an otherwise green run.
            }
            catch (UnauthorizedAccessException)
            {
                // Same: cleanup is best effort.
            }
        }
    }
}
