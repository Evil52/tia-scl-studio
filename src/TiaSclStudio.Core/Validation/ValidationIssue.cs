using TiaSclStudio.Core.Model;

namespace TiaSclStudio.Core.Validation
{
    public sealed class ValidationIssue
    {
        public ValidationIssue(ValidationSeverity severity, string code, string message, string path)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Path = path ?? string.Empty;
        }

        public ValidationSeverity Severity { get; private set; }

        public string Code { get; private set; }

        public string Message { get; private set; }

        /// <summary>Stable, UI-friendly location such as Blocks[Valve].Interface[Open].</summary>
        public string Path { get; private set; }

        public override string ToString()
        {
            return Severity + " " + Code + " at " + Path + ": " + Message;
        }
    }
}
