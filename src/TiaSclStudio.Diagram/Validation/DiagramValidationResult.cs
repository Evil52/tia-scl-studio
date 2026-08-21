using System.Collections.Generic;
using System.Linq;

namespace TiaSclStudio.Diagram.Validation
{
    public sealed class DiagramValidationResult
    {
        public DiagramValidationResult(IEnumerable<DiagramIssue> issues)
        {
            Issues = new List<DiagramIssue>(issues ?? Enumerable.Empty<DiagramIssue>());
        }

        public IList<DiagramIssue> Issues { get; private set; }

        public bool IsValid
        {
            get { return Issues.All(issue => issue.Severity != DiagramIssueSeverity.Error); }
        }
    }
}
