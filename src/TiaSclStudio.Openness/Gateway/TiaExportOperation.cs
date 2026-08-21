using System;

namespace TiaSclStudio.Openness.Gateway
{
    public sealed class TiaExportOperation
    {
        public TiaExportOperation(
            int order,
            TiaExportOperationKind kind,
            string target,
            string description)
        {
            if (order < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(order));
            }

            if (!Enum.IsDefined(typeof(TiaExportOperationKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Order = order;
            Kind = kind;
            Target = target ?? string.Empty;
            Description = description ?? string.Empty;
        }

        public int Order { get; }

        public TiaExportOperationKind Kind { get; }

        public string Target { get; }

        public string Description { get; }
    }
}
