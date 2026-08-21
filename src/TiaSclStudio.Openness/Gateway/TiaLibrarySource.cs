using System;

namespace TiaSclStudio.Openness.Gateway
{
    /// <summary>
    /// Immutable, Siemens-type-free SCL representation of one PLC object.
    /// </summary>
    public sealed class TiaLibrarySource
    {
        public TiaLibrarySource(
            TiaLibrarySourceKind kind,
            string name,
            string groupPath,
            string programmingLanguage,
            string sclText)
        {
            if (!Enum.IsDefined(typeof(TiaLibrarySourceKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A PLC library source name is required.", nameof(name));
            }

            if (string.IsNullOrWhiteSpace(sclText))
            {
                throw new ArgumentException("Generated SCL source text is required.", nameof(sclText));
            }

            Kind = kind;
            Name = name;
            GroupPath = groupPath ?? string.Empty;
            ProgrammingLanguage = programmingLanguage ?? string.Empty;
            SclText = sclText;
        }

        public TiaLibrarySourceKind Kind { get; }

        public string Name { get; }

        /// <summary>
        /// PLC-relative user-group path. The system root is represented by an
        /// empty string and path segments are separated with '/'.
        /// </summary>
        public string GroupPath { get; }

        public string ProgrammingLanguage { get; }

        public string SclText { get; }
    }
}
