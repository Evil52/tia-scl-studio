using System;

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

        TiaHardwareIoCatalog ReadHardwareIoCatalog();

        TiaExportPreview PreviewExport(TiaExportRequest request);

        TiaExportResult Export(TiaExportRequest request, string confirmationToken);
    }
}
