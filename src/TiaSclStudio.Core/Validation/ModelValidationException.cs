using System;
using System.Linq;

namespace TiaSclStudio.Core.Validation
{
    public sealed class ModelValidationException : InvalidOperationException
    {
        public ModelValidationException(ValidationResult result)
            : base(BuildMessage(result))
        {
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            Result = result;
        }

        public ValidationResult Result { get; private set; }

        private static string BuildMessage(ValidationResult result)
        {
            if (result == null)
            {
                return "The model is invalid.";
            }

            var errors = result.Errors.Select(issue => issue.ToString()).ToArray();
            return errors.Length == 0
                ? "The model is invalid."
                : "The model is invalid: " + string.Join(" | ", errors);
        }
    }
}
