using System;
using System.Collections.Generic;
using TiaSclStudio.Core.Model;

namespace TiaSclStudio.Core.Importing
{
    public enum SclImportedObjectKind
    {
        FunctionBlock = 0,
        Function = 1,
        UserDataType = 2
    }

    public enum SclImportDiagnosticSeverity
    {
        Warning = 0,
        Error = 1
    }

    /// <summary>
    /// A declaration-only object extracted from an SCL source. The executable
    /// body is deliberately never copied into the application model.
    /// </summary>
    public sealed class SclLibraryImportItem
    {
        internal SclLibraryImportItem(
            SclImportedObjectKind kind,
            string name,
            int sourceLine,
            BlockDefinition block,
            UdtDefinition dataType)
        {
            Kind = kind;
            Name = name ?? string.Empty;
            SourceLine = sourceLine;
            Block = block;
            DataType = dataType;
        }

        public SclImportedObjectKind Kind { get; private set; }

        public string Name { get; private set; }

        public int SourceLine { get; private set; }

        public BlockDefinition Block { get; private set; }

        public UdtDefinition DataType { get; private set; }
    }

    public sealed class SclImportDiagnostic
    {
        internal SclImportDiagnostic(
            SclImportDiagnosticSeverity severity,
            string code,
            string message,
            int line,
            int column)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Line = line;
            Column = column;
        }

        public SclImportDiagnosticSeverity Severity { get; private set; }

        public string Code { get; private set; }

        public string Message { get; private set; }

        public int Line { get; private set; }

        public int Column { get; private set; }
    }

    public sealed class SclLibraryImportResult
    {
        private readonly List<SclLibraryImportItem> _items = new List<SclLibraryImportItem>();
        private readonly List<SclImportDiagnostic> _diagnostics = new List<SclImportDiagnostic>();

        public IList<SclLibraryImportItem> Items
        {
            get { return _items.AsReadOnly(); }
        }

        public IList<SclImportDiagnostic> Diagnostics
        {
            get { return _diagnostics.AsReadOnly(); }
        }

        public bool HasErrors
        {
            get
            {
                foreach (var diagnostic in _diagnostics)
                {
                    if (diagnostic.Severity == SclImportDiagnosticSeverity.Error)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal void AddItem(SclLibraryImportItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            _items.Add(item);
        }

        internal void AddDiagnostic(
            SclImportDiagnosticSeverity severity,
            string code,
            string message,
            int line,
            int column)
        {
            _diagnostics.Add(new SclImportDiagnostic(severity, code, message, line, column));
        }
    }
}
