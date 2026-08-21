using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.Diagram.Storage
{
    public sealed class DiagramProjectStorage
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(DiagramProject));

        /// <summary>
        /// Saving is serialized across the process. Two windows on one project,
        /// or an autosave landing on top of a manual save, otherwise interleave
        /// inside File.Replace: one save fails and Windows leaves its own
        /// ~RF*.TMP backup files sitting next to the user's project.
        /// </summary>
        private static readonly object SaveGate = new object();

        public void Save(string path, DiagramProject project)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A project path is required.", "path");
            }

            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            EnsureSupportedVersion(project.FormatVersion);

            lock (SaveGate)
            {
                SaveCore(path, project);
            }
        }

        private static void SaveCore(string path, DiagramProject project)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = CreateTemporaryPath(directory, Path.GetFileName(fullPath));

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(true),
                Indent = true,
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace,
                CloseOutput = false
            };

            var originalFormatVersion = project.FormatVersion;
            var saveCompleted = false;
            try
            {
                // Groups were added in format v2 and the PLC CPU/address profile
                // in v3. Always stamp a successful save with the current version
                // so an older binary rejects fields it would otherwise ignore.
                project.FormatVersion = DiagramProject.CurrentFormatVersion;
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    using (var writer = XmlWriter.Create(stream, settings))
                    {
                        Serializer.Serialize(writer, project);
                        writer.Flush();
                    }

                    stream.Flush(true);
                }

                CommitStagedFile(temporaryPath, fullPath);
                saveCompleted = true;
            }
            finally
            {
                if (!saveCompleted)
                {
                    project.FormatVersion = originalFormatVersion;
                }

                TryDelete(temporaryPath);
            }
        }

        public DiagramProject Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A project path is required.", "path");
            }

            DiagramProject project;
            using (var stream = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
            {
                project = (DiagramProject)Serializer.Deserialize(reader);
            }

            EnsureSupportedVersion(project.FormatVersion);

            return project;
        }

        /// <summary>
        /// Swaps the staged file onto the target. A virus scanner or a backup
        /// agent can hold the target open for a moment, which surfaces as a
        /// transient sharing violation rather than a real failure, so a few
        /// short retries come before giving up. A permission problem is not
        /// transient and is reported immediately.
        /// </summary>
        private static void CommitStagedFile(string temporaryPath, string fullPath)
        {
            const int maximumAttempts = 5;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(fullPath))
                    {
                        File.Replace(temporaryPath, fullPath, null);
                    }
                    else
                    {
                        File.Move(temporaryPath, fullPath);
                    }

                    return;
                }
                catch (IOException) when (attempt < maximumAttempts)
                {
                    Thread.Sleep(20 * attempt);
                }
            }
        }

        private static void EnsureSupportedVersion(int formatVersion)
        {
            if (formatVersion != DiagramProject.LegacyFormatVersion &&
                formatVersion != DiagramProject.GroupFormatVersion &&
                formatVersion != DiagramProject.CurrentFormatVersion)
            {
                throw new NotSupportedException(
                    "Unsupported .tiasclproj format version " + formatVersion + ".");
            }
        }

        private static string CreateTemporaryPath(string directory, string fileName)
        {
            var targetDirectory = string.IsNullOrEmpty(directory)
                ? Directory.GetCurrentDirectory()
                : directory;
            string temporaryPath;
            do
            {
                temporaryPath = Path.Combine(
                    targetDirectory,
                    "." + fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
            }
            while (File.Exists(temporaryPath));

            return temporaryPath;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best effort: never mask the original serialization or replace failure.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort: never mask the original serialization or replace failure.
            }
        }
    }
}
