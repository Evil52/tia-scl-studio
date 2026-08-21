using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Generation;
using TiaSclStudio.Diagram.Validation;

namespace TiaSclStudio.Diagram.Generation
{
    /// <summary>
    /// A diagram issue augmented with the call-sheet identity. SheetId is null
    /// for project-wide issues (for example, Plant validation or source-bundle
    /// collisions).
    /// </summary>
    public sealed class DiagramProjectIssue
    {
        public DiagramProjectIssue(
            DiagramIssueSeverity severity,
            string code,
            string message,
            Guid? sheetId,
            string sheetName,
            Guid? nodeId,
            Guid? wireId)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            SheetId = sheetId;
            SheetName = sheetName ?? string.Empty;
            NodeId = nodeId;
            WireId = wireId;
        }

        public DiagramIssueSeverity Severity { get; private set; }

        public string Code { get; private set; }

        public string Message { get; private set; }

        public Guid? SheetId { get; private set; }

        public string SheetName { get; private set; }

        public Guid? NodeId { get; private set; }

        public Guid? WireId { get; private set; }
    }

    /// <summary>Compilation output and scoped diagnostics for one input sheet.</summary>
    public sealed class DiagramSheetCompilationResult
    {
        private readonly IList<DiagramProjectIssue> _issues;

        internal DiagramSheetCompilationResult(
            int sheetIndex,
            Guid? sheetId,
            string sheetName,
            DiagramCompilationResult compilation,
            IEnumerable<DiagramProjectIssue> issues)
        {
            SheetIndex = sheetIndex;
            SheetId = sheetId;
            SheetName = sheetName ?? string.Empty;
            Compilation = compilation ?? new DiagramCompilationResult(
                string.Empty,
                new DiagramIssue[0],
                new Guid[0]);
            _issues = new List<DiagramProjectIssue>(
                issues ?? Enumerable.Empty<DiagramProjectIssue>()).AsReadOnly();
        }

        /// <summary>Zero-based position in DiagramProject.Sheets.</summary>
        public int SheetIndex { get; private set; }

        public Guid? SheetId { get; private set; }

        public string SheetName { get; private set; }

        public DiagramCompilationResult Compilation { get; private set; }

        /// <summary>Alias convenient for callers that use the longer name.</summary>
        public DiagramCompilationResult CompilationResult
        {
            get { return Compilation; }
        }

        public string Scl
        {
            get { return Compilation.Scl; }
        }

        public IList<DiagramProjectIssue> Issues
        {
            get { return _issues; }
        }

        public bool IsSuccess
        {
            get
            {
                return Compilation.IsSuccess &&
                    !_issues.Any(issue => issue.Severity == DiagramIssueSeverity.Error);
            }
        }
    }

    /// <summary>
    /// Result of compiling the complete diagram project. GeneratedSources is
    /// deliberately empty whenever any project or sheet error exists.
    /// </summary>
    public sealed class DiagramProjectCompilationResult
    {
        private readonly IList<DiagramSheetCompilationResult> _sheets;
        private readonly IList<DiagramProjectIssue> _issues;
        private readonly IList<GeneratedSource> _generatedSources;

        internal DiagramProjectCompilationResult(
            IEnumerable<DiagramSheetCompilationResult> sheets,
            IEnumerable<DiagramProjectIssue> issues,
            IEnumerable<GeneratedSource> generatedSources)
        {
            _sheets = new List<DiagramSheetCompilationResult>(
                sheets ?? Enumerable.Empty<DiagramSheetCompilationResult>()).AsReadOnly();
            _issues = new List<DiagramProjectIssue>(
                issues ?? Enumerable.Empty<DiagramProjectIssue>()).AsReadOnly();

            var hasErrors = _issues.Any(issue => issue.Severity == DiagramIssueSeverity.Error);
            _generatedSources = new List<GeneratedSource>(
                hasErrors
                    ? Enumerable.Empty<GeneratedSource>()
                    : generatedSources ?? Enumerable.Empty<GeneratedSource>()).AsReadOnly();
        }

        public IList<DiagramSheetCompilationResult> Sheets
        {
            get { return _sheets; }
        }

        /// <summary>Alias for consumers that prefer an explicit result name.</summary>
        public IList<DiagramSheetCompilationResult> SheetResults
        {
            get { return _sheets; }
        }

        public IList<DiagramProjectIssue> Issues
        {
            get { return _issues; }
        }

        /// <summary>Complete TIA import bundle in dependency order.</summary>
        public IList<GeneratedSource> GeneratedSources
        {
            get { return _generatedSources; }
        }

        public IList<GeneratedSource> Sources
        {
            get { return _generatedSources; }
        }

        public bool IsSuccess
        {
            get
            {
                return !_issues.Any(issue => issue.Severity == DiagramIssueSeverity.Error) &&
                    _sheets.All(sheet => sheet.IsSuccess);
            }
        }
    }
}
