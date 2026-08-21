using System;

namespace TiaSclStudio.Diagram.Validation
{
    public enum DiagramIssueSeverity
    {
        Information,
        Warning,
        Error
    }

    public sealed class DiagramIssue
    {
        public DiagramIssue(
            DiagramIssueSeverity severity,
            string code,
            string message,
            Guid? nodeId,
            Guid? wireId)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            NodeId = nodeId;
            WireId = wireId;
        }

        public DiagramIssueSeverity Severity { get; private set; }

        public string Code { get; private set; }

        public string Message { get; private set; }

        public Guid? NodeId { get; private set; }

        public Guid? WireId { get; private set; }
    }
}
