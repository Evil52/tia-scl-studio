using System;

namespace TiaSclStudio.Openness.Gateway
{
    public sealed class TiaExportCollision
    {
        public TiaExportCollision(
            TiaExportCollisionKind kind,
            string target,
            bool isOwned,
            bool canOverwrite,
            string description)
        {
            if (!Enum.IsDefined(typeof(TiaExportCollisionKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Kind = kind;
            Target = target ?? string.Empty;
            IsOwned = isOwned;
            CanOverwrite = canOverwrite;
            Description = description ?? string.Empty;
        }

        public TiaExportCollisionKind Kind { get; }

        public string Target { get; }

        public bool IsOwned { get; }

        public bool CanOverwrite { get; }

        public string Description { get; }
    }
}
