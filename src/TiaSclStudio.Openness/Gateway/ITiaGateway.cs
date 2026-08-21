using System;
using System.Threading;

namespace TiaSclStudio.Openness.Gateway
{
    /// <summary>
    /// Version-neutral gateway. Implementations own all Siemens objects and must
    /// release them from Dispose; contracts never expose Siemens.Engineering types.
    /// </summary>
    public interface ITiaGateway : IDisposable
    {
        TiaGatewayStatus GetStatus();

        TiaGatewayStatus Connect(TiaConnectionRequest request);

        /// <summary>
        /// Reads configured hardware I/O without modifying TIA Portal.
        /// Cancellation is observed while traversing devices, addresses and channels.
        /// </summary>
        TiaHardwareIoCatalog ReadHardwareIoCatalog(CancellationToken cancellationToken);

        /// <summary>
        /// Reads configured PLC tags without modifying TIA Portal.
        /// Cancellation is observed between tables and tags.
        /// </summary>
        TiaPlcTagCatalog ReadPlcTagCatalog(CancellationToken cancellationToken);

        /// <summary>
        /// Reads FB, FC and PLC data-type sources from the selected PLC without
        /// modifying TIA Portal. Cancellation is observed between PLC objects.
        /// </summary>
        TiaLibrarySnapshot ReadLibrarySnapshot(CancellationToken cancellationToken);

        TiaExportPreview PreviewExport(TiaExportRequest request);

        TiaExportResult Export(TiaExportRequest request, string confirmationToken);
    }
}
