using System;

namespace TiaSclStudio.Openness.Diagnostics
{
    public sealed class OpennessDiagnostic
    {
        public OpennessDiagnostic(
            string code,
            DiagnosticSeverity severity,
            string message,
            string location = null,
            string exceptionType = null)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("A diagnostic code is required.", nameof(code));
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("A diagnostic message is required.", nameof(message));
            }

            Code = code;
            Severity = severity;
            Message = message;
            Location = location ?? string.Empty;
            ExceptionType = exceptionType ?? string.Empty;
        }

        public string Code { get; }

        public DiagnosticSeverity Severity { get; }

        public string Message { get; }

        public string Location { get; }

        public string ExceptionType { get; }

        public override string ToString()
        {
            return string.Format("{0}: {1}", Code, Message);
        }
    }
}
