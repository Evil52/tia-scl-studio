using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TiaSclStudio.Openness.Adapters;
using TiaSclStudio.Openness.Gateway;
using TiaSclStudio.Openness.Model;

namespace TiaSclStudio.Openness.Legacy.V17
{
    public sealed class LegacyV17GatewayAdapterFactory : ITiaGatewayAdapterFactory
    {
        private static readonly object ProcessAssemblyLock = new object();
        private static readonly byte[] SiemensEngineeringPublicKeyToken =
            { 0xd2, 0x9e, 0xc8, 0x9b, 0xac, 0x04, 0x8f, 0x84 };
        private static string pinnedInstallationRoot;

        public OpennessApiFamily Family
        {
            get { return OpennessApiFamily.LegacyV17ToV20; }
        }

        public bool Supports(OpennessInstallation installation)
        {
            if (installation == null ||
                installation.Family != OpennessApiFamily.LegacyV17ToV20 ||
                installation.ApiVersion.Major != 17 ||
                !File.Exists(installation.AssemblyPath))
            {
                return false;
            }

            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(installation.AssemblyPath);
                return string.Equals(assemblyName.Name, "Siemens.Engineering", StringComparison.Ordinal) &&
                    assemblyName.Version != null &&
                    assemblyName.Version.Equals(new Version(17, 0, 0, 0)) &&
                    TokensEqual(assemblyName.GetPublicKeyToken(), SiemensEngineeringPublicKeyToken) &&
                    IsPathInside(installation.AssemblyPath, installation.RootPath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public ITiaGateway Create(OpennessInstallation installation)
        {
            if (!Supports(installation))
            {
                throw new ArgumentException(
                    "The selected installation is not a valid Siemens.Engineering V17 API.",
                    nameof(installation));
            }

            lock (ProcessAssemblyLock)
            {
                var selectedRoot = Path.GetFullPath(installation.RootPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly =>
                        string.Equals(assembly.GetName().Name, "Siemens.Engineering", StringComparison.Ordinal));
                if (alreadyLoaded != null)
                {
                    var loadedName = alreadyLoaded.GetName();
                    if (!IsPathInside(alreadyLoaded.Location, selectedRoot) ||
                        loadedName.Version == null ||
                        !loadedName.Version.Equals(new Version(17, 0, 0, 0)) ||
                        !TokensEqual(loadedName.GetPublicKeyToken(), SiemensEngineeringPublicKeyToken))
                    {
                        throw new InvalidOperationException(
                            "A different or invalid Siemens.Engineering facade is already loaded. " +
                            "Restart the application before selecting this Portal installation.");
                    }
                }

                if (!string.IsNullOrEmpty(pinnedInstallationRoot) &&
                    !string.Equals(pinnedInstallationRoot, selectedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The V17 Openness facade is pinned process-wide to '" + pinnedInstallationRoot +
                        "'. Restart the application before selecting '" + selectedRoot + "'.");
                }

                // The facade cannot be unloaded from an AppDomain. Pin before
                // activation because a failed activation may still load it.
                pinnedInstallationRoot = selectedRoot;
                var resolver = new SiemensAssemblyResolver(installation);
                try
                {
                    return LegacyV17GatewayActivator.Create(installation, resolver);
                }
                catch
                {
                    resolver.Dispose();
                    throw;
                }
            }
        }

        private static bool TokensEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsPathInside(string path, string rootPath)
        {
            try
            {
                var normalizedPath = Path.GetFullPath(path);
                var normalizedRoot = Path.GetFullPath(rootPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var boundary = normalizedRoot + Path.DirectorySeparatorChar;
                return normalizedPath.StartsWith(boundary, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    internal static class LegacyV17GatewayActivator
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ITiaGateway Create(
            OpennessInstallation installation,
            SiemensAssemblyResolver resolver)
        {
            return new LegacyV17Gateway(installation, resolver);
        }
    }
}
