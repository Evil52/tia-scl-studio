using System;
using System.Collections.Generic;
using TiaSclStudio.Core.Generation;
using TiaSclStudio.Diagram.Validation;

namespace TiaSclStudio.Diagram.Generation
{
    public sealed class DiagramCompilationResult
    {
        public DiagramCompilationResult(
            string scl,
            IEnumerable<DiagramIssue> issues,
            IEnumerable<Guid> executionOrder)
            : this(scl, issues, executionOrder, new GeneratedSource[0])
        {
        }

        public DiagramCompilationResult(
            string scl,
            IEnumerable<DiagramIssue> issues,
            IEnumerable<Guid> executionOrder,
            IEnumerable<GeneratedSource> generatedSources)
        {
            Scl = scl ?? string.Empty;
            Issues = new List<DiagramIssue>(issues ?? new DiagramIssue[0]);
            ExecutionOrder = new List<Guid>(executionOrder ?? new Guid[0]);
            GeneratedSources = new List<GeneratedSource>(generatedSources ?? new GeneratedSource[0]);
        }

        public string Scl { get; private set; }

        public IList<DiagramIssue> Issues { get; private set; }

        public IList<Guid> ExecutionOrder { get; private set; }

        /// <summary>
        /// Complete import bundle in TIA dependency order. It is empty when
        /// validation fails. Scl is the call-sheet source within this bundle.
        /// </summary>
        public IList<GeneratedSource> GeneratedSources { get; private set; }

        public bool IsSuccess
        {
            get
            {
                foreach (var issue in Issues)
                {
                    if (issue.Severity == DiagramIssueSeverity.Error)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
