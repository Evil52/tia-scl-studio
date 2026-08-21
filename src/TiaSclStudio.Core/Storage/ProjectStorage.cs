using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TiaSclStudio.Core.Generation;

namespace TiaSclStudio.Core.Storage
{
    public static class ProjectStorage
    {
        private static readonly Encoding SourceEncoding = new UTF8Encoding(true);
        private static readonly char[] InvalidFileNameCharacters = Path.GetInvalidFileNameChars();

        /// <summary>Writes an SCL source as UTF-8 with BOM, preserving Cyrillic TIA comments.</summary>
        public static void WriteSource(string filePath, string source)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A source file path is required.", "filePath");
            }

            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, SourceEncoding))
            {
                writer.Write(source);
            }
        }

        public static void WriteSources(string directoryPath, IEnumerable<GeneratedSource> sources)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("An output directory is required.", "directoryPath");
            }

            if (sources == null)
            {
                throw new ArgumentNullException("sources");
            }

            var root = Path.GetFullPath(directoryPath);
            Directory.CreateDirectory(root);

            foreach (var source in sources)
            {
                if (source == null)
                {
                    continue;
                }

                ValidateFileName(source.FileName);
                WriteSource(Path.Combine(root, source.FileName), source.Content);
            }
        }

        private static void ValidateFileName(string fileName)
        {
            // The character check has to run first: Path.GetFileName throws its
            // own ArgumentException on '<', '>', '"' and '|', which would leak a
            // path-parsing error out of what is really a model problem.
            if (string.IsNullOrWhiteSpace(fileName) ||
                fileName.IndexOfAny(InvalidFileNameCharacters) >= 0 ||
                !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
                !string.Equals(fileName, fileName.TrimEnd(' ', '.'), StringComparison.Ordinal) ||
                IsReservedDeviceName(fileName))
            {
                throw new InvalidOperationException("Generated source has an unsafe file name: " + fileName);
            }
        }

        /// <summary>
        /// Windows keeps CON, PRN, AUX, NUL, COM1-9 and LPT1-9 reserved even
        /// when an extension follows, so "CON.scl" opens a device rather than a
        /// file. Depending on the device that either throws from deep inside
        /// FileStream or succeeds while writing the SCL nowhere at all, which
        /// would ship an import bundle one source short.
        /// </summary>
        private static bool IsReservedDeviceName(string fileName)
        {
            var separator = fileName.IndexOf('.');
            var baseName = separator < 0 ? fileName : fileName.Substring(0, separator);
            baseName = baseName.TrimEnd(' ');
            if (baseName.Length != 3 && baseName.Length != 4)
            {
                return false;
            }

            var upper = baseName.ToUpperInvariant();
            if (baseName.Length == 3)
            {
                return upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL";
            }

            var prefix = upper.Substring(0, 3);
            return (prefix == "COM" || prefix == "LPT") && upper[3] >= '1' && upper[3] <= '9';
        }
    }
}
