using System;

namespace TiaSclStudio.Openness.Model
{
    /// <summary>
    /// A validated Portal/API pair. One Portal installation can expose more
    /// than one compatible API version (for example, Portal V20 exposes V17-V20).
    /// </summary>
    public sealed class OpennessInstallation
    {
        public OpennessInstallation(
            Version portalVersion,
            Version apiVersion,
            string rootPath,
            string assemblyPath,
            OpennessApiFamily family,
            OpennessDiscoverySource discoverySource,
            string targetFrameworkMoniker,
            string registryPath)
        {
            if (portalVersion == null)
            {
                throw new ArgumentNullException(nameof(portalVersion));
            }

            if (apiVersion == null)
            {
                throw new ArgumentNullException(nameof(apiVersion));
            }

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("The Portal root path is required.", nameof(rootPath));
            }

            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                throw new ArgumentException("The Openness assembly path is required.", nameof(assemblyPath));
            }

            PortalVersion = portalVersion;
            ApiVersion = apiVersion;
            RootPath = rootPath;
            AssemblyPath = assemblyPath;
            Family = family;
            DiscoverySource = discoverySource;
            TargetFrameworkMoniker = targetFrameworkMoniker ?? string.Empty;
            RegistryPath = registryPath ?? string.Empty;
        }

        public Version PortalVersion { get; }

        public Version ApiVersion { get; }

        public string RootPath { get; }

        public string AssemblyPath { get; }

        public OpennessApiFamily Family { get; }

        public OpennessDiscoverySource DiscoverySource { get; }

        public string TargetFrameworkMoniker { get; }

        public string RegistryPath { get; }

        public override string ToString()
        {
            return string.Format(
                "Portal V{0}, API V{1} ({2})",
                PortalVersion.Major,
                ApiVersion.Major,
                Family);
        }
    }
}
