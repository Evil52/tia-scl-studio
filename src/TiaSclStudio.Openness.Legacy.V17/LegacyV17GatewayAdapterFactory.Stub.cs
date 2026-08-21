using System;
using TiaSclStudio.Openness.Adapters;
using TiaSclStudio.Openness.Gateway;
using TiaSclStudio.Openness.Model;

namespace TiaSclStudio.Openness.Legacy.V17
{
    /// <summary>
    /// Offline-build fallback used when no official Siemens.Engineering V17
    /// assembly is installed at build time.
    /// </summary>
    public sealed class LegacyV17GatewayAdapterFactory : ITiaGatewayAdapterFactory
    {
        public OpennessApiFamily Family
        {
            get { return OpennessApiFamily.LegacyV17ToV20; }
        }

        public bool Supports(OpennessInstallation installation)
        {
            return false;
        }

        public ITiaGateway Create(OpennessInstallation installation)
        {
            throw new PlatformNotSupportedException(
                "The V17 adapter was built without the official Siemens.Engineering V17 assembly. " +
                "Install TIA Portal Openness and rebuild, or set /p:TiaOpennessV17AssemblyPath=<path>.");
        }
    }
}
