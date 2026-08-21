using TiaSclStudio.Openness.Gateway;
using TiaSclStudio.Openness.Model;

namespace TiaSclStudio.Openness.Adapters
{
    /// <summary>
    /// Extension point for separate adapter assemblies. A legacy adapter can
    /// reference Siemens.Engineering.dll V17-V20, while a modular adapter can
    /// reference the V21+ Siemens.Engineering.Base/Step7 assemblies.
    /// </summary>
    public interface ITiaGatewayAdapterFactory
    {
        OpennessApiFamily Family { get; }

        bool Supports(OpennessInstallation installation);

        ITiaGateway Create(OpennessInstallation installation);
    }
}
