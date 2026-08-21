using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Model;

namespace TiaSclStudio.Core.Validation
{
    public sealed class ValidationResult
    {
        private readonly List<ValidationIssue> _issues = new List<ValidationIssue>();

        public IList<ValidationIssue> Issues
        {
            get { return _issues.AsReadOnly(); }
        }

        public IEnumerable<ValidationIssue> Errors
        {
            get { return _issues.Where(issue => issue.Severity == ValidationSeverity.Error); }
        }

        public IEnumerable<ValidationIssue> Warnings
        {
            get { return _issues.Where(issue => issue.Severity == ValidationSeverity.Warning); }
        }

        public bool IsValid
        {
            get { return !_issues.Any(issue => issue.Severity == ValidationSeverity.Error); }
        }

        internal void Add(ValidationSeverity severity, string code, string message, string path)
        {
            _issues.Add(new ValidationIssue(severity, code, message, path));
        }
    }
}
