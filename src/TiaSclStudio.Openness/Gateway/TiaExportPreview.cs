using System.Collections.Generic;
using System.Collections.ObjectModel;
using TiaSclStudio.Openness.Diagnostics;

namespace TiaSclStudio.Openness.Gateway
{
    public sealed class TiaExportPreview
    {
        public TiaExportPreview(
            bool canExecute,
            string confirmationToken,
            IEnumerable<TiaExportOperation> operations,
            IEnumerable<TiaExportCollision> collisions,
            IEnumerable<OpennessDiagnostic> diagnostics)
        {
            CanExecute = canExecute;
            ConfirmationToken = confirmationToken ?? string.Empty;
            Operations = new ReadOnlyCollection<TiaExportOperation>(
                new List<TiaExportOperation>(operations ?? new TiaExportOperation[0]));
            Collisions = new ReadOnlyCollection<TiaExportCollision>(
                new List<TiaExportCollision>(collisions ?? new TiaExportCollision[0]));
            Diagnostics = new ReadOnlyCollection<OpennessDiagnostic>(
                new List<OpennessDiagnostic>(diagnostics ?? new OpennessDiagnostic[0]));
        }

        public bool CanExecute { get; }

        public string ConfirmationToken { get; }

        public IReadOnlyList<TiaExportOperation> Operations { get; }

        public IReadOnlyList<TiaExportCollision> Collisions { get; }

        public IReadOnlyList<OpennessDiagnostic> Diagnostics { get; }
    }
}
