using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TiaSclStudio.Openness.Model;

namespace TiaSclStudio.Openness.Legacy.V17
{
    internal sealed class SiemensAssemblyResolver : IDisposable
    {
        private readonly IDictionary<string, string> assemblyPaths;
        private readonly string installationRoot;
        private bool disposed;

        public SiemensAssemblyResolver(OpennessInstallation installation)
        {
            if (installation == null)
            {
                throw new ArgumentNullException(nameof(installation));
            }

            installationRoot = Path.GetFullPath(installation.RootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var publicApiDirectory = Path.GetDirectoryName(installation.AssemblyPath) ?? string.Empty;
            var runtimeApiDirectory = Path.Combine(installation.RootPath, "Bin", "PublicAPI");
            assemblyPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddCandidate("Siemens.Engineering", installation.AssemblyPath);
            AddCandidate("Siemens.Engineering.Hmi", Path.Combine(publicApiDirectory, "Siemens.Engineering.Hmi.dll"));
            AddCandidate("Siemens.Engineering.Contract", Path.Combine(runtimeApiDirectory, "Siemens.Engineering.Contract.dll"));
            AddCandidate(
                "Siemens.Engineering.ClientAdapter.Interfaces",
                Path.Combine(runtimeApiDirectory, "Siemens.Engineering.ClientAdapter.Interfaces.dll"));

            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly;
            disposed = true;
        }

        private Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            AssemblyName requestedName;
            try
            {
                requestedName = new AssemblyName(args.Name);
            }
            catch (Exception)
            {
                return null;
            }

            string path;
            if (!assemblyPaths.TryGetValue(requestedName.Name, out path) ||
                !File.Exists(path) ||
                !IsInsideInstallation(path))
            {
                return null;
            }

            AssemblyName candidateName;
            try
            {
                candidateName = AssemblyName.GetAssemblyName(path);
            }
            catch (Exception)
            {
                return null;
            }

            if (!string.Equals(requestedName.Name, candidateName.Name, StringComparison.Ordinal) ||
                (requestedName.Version != null && !requestedName.Version.Equals(candidateName.Version)) ||
                !TokensEqual(requestedName.GetPublicKeyToken(), candidateName.GetPublicKeyToken()))
            {
                return null;
            }

            return Assembly.LoadFrom(path);
        }

        private void AddCandidate(string expectedSimpleName, string path)
        {
            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return;
            }

            if (!IsInsideInstallation(normalizedPath) || !File.Exists(normalizedPath))
            {
                return;
            }

            AssemblyName candidateName;
            try
            {
                candidateName = AssemblyName.GetAssemblyName(normalizedPath);
            }
            catch (Exception)
            {
                return;
            }

            if (string.Equals(candidateName.Name, expectedSimpleName, StringComparison.Ordinal))
            {
                assemblyPaths.Add(expectedSimpleName, normalizedPath);
            }
        }

        private bool IsInsideInstallation(string path)
        {
            try
            {
                var boundary = installationRoot + Path.DirectorySeparatorChar;
                return Path.GetFullPath(path).StartsWith(boundary, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TokensEqual(byte[] left, byte[] right)
        {
            left = left ?? new byte[0];
            right = right ?? new byte[0];
            if (left.Length != right.Length)
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
    }
}
