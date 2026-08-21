using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Model;

namespace TiaSclStudio.Openness.Discovery
{
    /// <summary>
    /// Discovers installed Openness APIs without loading Siemens assemblies.
    /// Registry and file-system errors are converted to diagnostics so discovery
    /// remains safe in restricted service accounts and development environments.
    /// </summary>
    public sealed class OpennessInstallationLocator
    {
        private const string InstalledSoftwareRegistryPath = @"SOFTWARE\Siemens\Automation\_InstalledSW";
        private const string OpennessRegistryPath = @"SOFTWARE\Siemens\Automation\Openness";
        private const string LegacyAssemblyFileName = "Siemens.Engineering.dll";
        private const string ModularAssemblyFileName = "Siemens.Engineering.Base.dll";
        private const string Net48TargetFramework = "net48";

        private readonly IReadOnlyList<string> fallbackAutomationRoots;

        public OpennessInstallationLocator()
            : this(CreateDefaultFallbackRoots())
        {
        }

        /// <summary>
        /// Creates a locator with explicit Siemens Automation directories. This
        /// overload is useful for deterministic tests and custom installations.
        /// Each directory is expected to contain folders such as "Portal V20".
        /// </summary>
        public OpennessInstallationLocator(IEnumerable<string> fallbackAutomationRoots)
        {
            var roots = new List<string>();
            if (fallbackAutomationRoots != null)
            {
                foreach (var root in fallbackAutomationRoots)
                {
                    string normalized;
                    if (TryNormalizePath(root, out normalized) &&
                        !roots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    {
                        roots.Add(normalized);
                    }
                }
            }

            this.fallbackAutomationRoots = roots.AsReadOnly();
        }

        public OpennessDiscoveryResult Locate()
        {
            var context = new DiscoveryContext();

            DiscoverRegistryView(RegistryView.Registry64, context);
            DiscoverRegistryView(RegistryView.Registry32, context);
            DiscoverProgramFilesFallback(context);

            return context.BuildResult();
        }

        private static IEnumerable<string> CreateDefaultFallbackRoots()
        {
            var programFilesPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetEnvironmentVariable("ProgramW6432")
            };

            foreach (var programFilesPath in programFilesPaths)
            {
                if (!string.IsNullOrWhiteSpace(programFilesPath))
                {
                    yield return Path.Combine(programFilesPath, "Siemens", "Automation");
                }
            }
        }

        private static void DiscoverRegistryView(RegistryView view, DiscoveryContext context)
        {
            var viewName = view == RegistryView.Registry64 ? "64-bit" : "32-bit";
            try
            {
                using (var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                {
                    DiscoverInstalledSoftwareRegistry(localMachine, viewName, context);
                    DiscoverOpennessRegistry(localMachine, viewName, context);
                }
            }
            catch (Exception exception)
            {
                context.AddDiagnostic(CreateExceptionDiagnostic(
                    OpennessDiagnosticCodes.RegistryAccessFailed,
                    DiagnosticSeverity.Warning,
                    "Cannot inspect the " + viewName + " HKLM registry view.",
                    "HKLM (" + viewName + ")",
                    exception));
            }
        }

        private static void DiscoverInstalledSoftwareRegistry(
            RegistryKey localMachine,
            string viewName,
            DiscoveryContext context)
        {
            using (var installedSoftware = OpenSubKeySafe(
                localMachine,
                InstalledSoftwareRegistryPath,
                "HKLM (" + viewName + @")\" + InstalledSoftwareRegistryPath,
                context))
            {
                if (installedSoftware == null)
                {
                    return;
                }

                for (var portalMajor = 17; portalMajor <= 20; portalMajor++)
                {
                    var relativePath = "TIAP" + portalMajor + @"\TIA_Opns";
                    var location = installedSoftware.Name + @"\" + relativePath;

                    using (var opennessPackage = OpenSubKeySafe(installedSoftware, relativePath, location, context))
                    {
                        if (opennessPackage == null)
                        {
                            continue;
                        }

                        var rootPath = ReadStringValueSafe(opennessPackage, "Path", location, context);
                        if (string.IsNullOrWhiteSpace(rootPath))
                        {
                            context.AddDiagnostic(new OpennessDiagnostic(
                                OpennessDiagnosticCodes.RegistryValueInvalid,
                                DiagnosticSeverity.Warning,
                                "The installed Openness package has no usable Path value.",
                                location));
                            continue;
                        }

                        DiscoverLegacyRoot(
                            new Version(portalMajor, 0),
                            rootPath,
                            OpennessDiscoverySource.InstalledSoftwareRegistry,
                            location,
                            context,
                            true);
                    }
                }
            }
        }

        private static void DiscoverOpennessRegistry(
            RegistryKey localMachine,
            string viewName,
            DiscoveryContext context)
        {
            var rootLocation = "HKLM (" + viewName + @")\" + OpennessRegistryPath;
            using (var opennessRoot = OpenSubKeySafe(localMachine, OpennessRegistryPath, rootLocation, context))
            {
                if (opennessRoot == null)
                {
                    return;
                }

                foreach (var portalKeyName in GetSubKeyNamesSafe(opennessRoot, rootLocation, context))
                {
                    Version portalVersion;
                    if (!TryParseVersionToken(portalKeyName, out portalVersion) || portalVersion.Major < 17)
                    {
                        // V21 also has a version-independent AllowList key.
                        continue;
                    }

                    var portalLocation = opennessRoot.Name + @"\" + portalKeyName;
                    using (var portalKey = OpenSubKeySafe(opennessRoot, portalKeyName, portalLocation, context))
                    {
                        if (portalKey == null)
                        {
                            continue;
                        }

                        using (var publicApiKey = OpenSubKeySafe(
                            portalKey,
                            "PublicAPI",
                            portalLocation + @"\PublicAPI",
                            context))
                        {
                            if (publicApiKey == null)
                            {
                                continue;
                            }

                            foreach (var apiKeyName in GetSubKeyNamesSafe(publicApiKey, publicApiKey.Name, context))
                            {
                                Version apiVersionFromKey;
                                if (!TryParseVersionToken(apiKeyName, out apiVersionFromKey) || apiVersionFromKey.Major < 17)
                                {
                                    continue;
                                }

                                var apiLocation = publicApiKey.Name + @"\" + apiKeyName;
                                using (var apiKey = OpenSubKeySafe(publicApiKey, apiKeyName, apiLocation, context))
                                {
                                    if (apiKey == null)
                                    {
                                        continue;
                                    }

                                    if (portalVersion.Major >= 21 || apiVersionFromKey.Major >= 21)
                                    {
                                        DiscoverModularRegistryApi(
                                            portalVersion,
                                            apiVersionFromKey,
                                            apiKey,
                                            apiLocation,
                                            context);
                                    }
                                    else
                                    {
                                        DiscoverLegacyRegistryApi(
                                            portalVersion,
                                            apiVersionFromKey,
                                            apiKey,
                                            apiLocation,
                                            context);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void DiscoverLegacyRegistryApi(
            Version portalVersion,
            Version apiVersionFromKey,
            RegistryKey apiKey,
            string location,
            DiscoveryContext context)
        {
            var assemblyPath = ReadStringValueSafe(apiKey, "Siemens.Engineering", location, context);
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                context.AddDiagnostic(new OpennessDiagnostic(
                    OpennessDiagnosticCodes.RegistryValueInvalid,
                    DiagnosticSeverity.Warning,
                    "The legacy PublicAPI key has no Siemens.Engineering value.",
                    location));
                return;
            }

            var apiVersion = ReadVersionValueSafe(apiKey, "AssemblyVersion", apiVersionFromKey);
            AddAssemblyCandidate(
                portalVersion,
                apiVersion,
                DerivePortalRoot(assemblyPath),
                assemblyPath,
                OpennessApiFamily.LegacyV17ToV20,
                OpennessDiscoverySource.OpennessRegistry,
                string.Empty,
                location,
                context);
        }

        private static void DiscoverModularRegistryApi(
            Version portalVersion,
            Version apiVersionFromKey,
            RegistryKey apiKey,
            string location,
            DiscoveryContext context)
        {
            RegistryKey net48Key = null;
            var net48Name = GetSubKeyNamesSafe(apiKey, location, context)
                .FirstOrDefault(name => string.Equals(name, Net48TargetFramework, StringComparison.OrdinalIgnoreCase));

            if (net48Name != null)
            {
                net48Key = OpenSubKeySafe(apiKey, net48Name, location + @"\" + net48Name, context);
            }

            using (net48Key)
            {
                if (net48Key == null)
                {
                    context.AddDiagnostic(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.RegistryValueInvalid,
                        DiagnosticSeverity.Warning,
                        "The modular PublicAPI key has no net48 target-framework subkey.",
                        location));
                    return;
                }

                var net48Location = location + @"\" + net48Name;
                var assemblyPath = ReadStringValueSafe(net48Key, "Siemens.Engineering.Base", net48Location, context);
                if (string.IsNullOrWhiteSpace(assemblyPath))
                {
                    context.AddDiagnostic(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.RegistryValueInvalid,
                        DiagnosticSeverity.Warning,
                        "The modular PublicAPI key has no Siemens.Engineering.Base value.",
                        net48Location));
                    return;
                }

                var apiVersion = ReadVersionValueSafe(net48Key, "AssemblyVersion", apiVersionFromKey);
                AddAssemblyCandidate(
                    portalVersion,
                    apiVersion,
                    DerivePortalRoot(assemblyPath),
                    assemblyPath,
                    OpennessApiFamily.ModularV21Plus,
                    OpennessDiscoverySource.OpennessRegistry,
                    Net48TargetFramework,
                    net48Location,
                    context);
            }
        }

        private void DiscoverProgramFilesFallback(DiscoveryContext context)
        {
            foreach (var automationRoot in fallbackAutomationRoots)
            {
                if (!Directory.Exists(automationRoot))
                {
                    continue;
                }

                string[] portalDirectories;
                try
                {
                    portalDirectories = Directory.GetDirectories(automationRoot, "Portal V*", SearchOption.TopDirectoryOnly);
                }
                catch (Exception exception)
                {
                    context.AddDiagnostic(CreateExceptionDiagnostic(
                        OpennessDiagnosticCodes.FallbackScanFailed,
                        DiagnosticSeverity.Warning,
                        "Cannot enumerate TIA Portal installation directories.",
                        automationRoot,
                        exception));
                    continue;
                }

                foreach (var portalDirectory in portalDirectories)
                {
                    Version portalVersion;
                    if (!TryParsePortalDirectoryVersion(portalDirectory, out portalVersion) || portalVersion.Major < 17)
                    {
                        continue;
                    }

                    if (portalVersion.Major <= 20)
                    {
                        DiscoverLegacyRoot(
                            portalVersion,
                            portalDirectory,
                            OpennessDiscoverySource.ProgramFilesFallback,
                            portalDirectory,
                            context,
                            false);
                    }
                    else
                    {
                        DiscoverModularRoot(
                            portalVersion,
                            portalDirectory,
                            OpennessDiscoverySource.ProgramFilesFallback,
                            portalDirectory,
                            context,
                            false);
                    }
                }
            }
        }

        private static void DiscoverLegacyRoot(
            Version portalVersion,
            string rootPath,
            OpennessDiscoverySource source,
            string location,
            DiscoveryContext context,
            bool reportMissing)
        {
            string normalizedRoot;
            if (!TryNormalizePath(rootPath, out normalizedRoot) || !Directory.Exists(normalizedRoot))
            {
                if (reportMissing)
                {
                    context.AddDiagnostic(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.RegistryValueInvalid,
                        DiagnosticSeverity.Warning,
                        "The registered TIA Portal installation directory does not exist.",
                        rootPath));
                }

                return;
            }

            var publicApiRoot = Path.Combine(normalizedRoot, "PublicAPI");
            string[] apiDirectories;
            try
            {
                apiDirectories = Directory.Exists(publicApiRoot)
                    ? Directory.GetDirectories(publicApiRoot, "V*", SearchOption.TopDirectoryOnly)
                    : new string[0];
            }
            catch (Exception exception)
            {
                context.AddDiagnostic(CreateExceptionDiagnostic(
                    OpennessDiagnosticCodes.FallbackScanFailed,
                    DiagnosticSeverity.Warning,
                    "Cannot enumerate legacy PublicAPI directories.",
                    publicApiRoot,
                    exception));
                return;
            }

            var foundCandidate = false;
            foreach (var apiDirectory in apiDirectories)
            {
                Version apiVersion;
                if (!TryParseApiDirectoryVersion(apiDirectory, out apiVersion) ||
                    apiVersion.Major < 17 || apiVersion.Major > 20)
                {
                    continue;
                }

                var assemblyPath = Path.Combine(apiDirectory, LegacyAssemblyFileName);
                if (!File.Exists(assemblyPath))
                {
                    continue;
                }

                foundCandidate = true;
                AddAssemblyCandidate(
                    portalVersion,
                    apiVersion,
                    normalizedRoot,
                    assemblyPath,
                    OpennessApiFamily.LegacyV17ToV20,
                    source,
                    string.Empty,
                    location,
                    context);
            }

            if (reportMissing && !foundCandidate)
            {
                context.AddDiagnostic(new OpennessDiagnostic(
                    OpennessDiagnosticCodes.AssemblyMissing,
                    DiagnosticSeverity.Warning,
                    "No valid V17-V20 Siemens.Engineering.dll candidate was found below the registered Portal root.",
                    publicApiRoot));
            }
        }

        private static void DiscoverModularRoot(
            Version portalVersion,
            string rootPath,
            OpennessDiscoverySource source,
            string location,
            DiscoveryContext context,
            bool reportMissing)
        {
            string normalizedRoot;
            if (!TryNormalizePath(rootPath, out normalizedRoot) || !Directory.Exists(normalizedRoot))
            {
                if (reportMissing)
                {
                    context.AddDiagnostic(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.RegistryValueInvalid,
                        DiagnosticSeverity.Warning,
                        "The registered TIA Portal installation directory does not exist.",
                        rootPath));
                }

                return;
            }

            var publicApiRoot = Path.Combine(normalizedRoot, "PublicAPI");
            string[] apiDirectories;
            try
            {
                apiDirectories = Directory.Exists(publicApiRoot)
                    ? Directory.GetDirectories(publicApiRoot, "V*", SearchOption.TopDirectoryOnly)
                    : new string[0];
            }
            catch (Exception exception)
            {
                context.AddDiagnostic(CreateExceptionDiagnostic(
                    OpennessDiagnosticCodes.FallbackScanFailed,
                    DiagnosticSeverity.Warning,
                    "Cannot enumerate modular PublicAPI directories.",
                    publicApiRoot,
                    exception));
                return;
            }

            var foundCandidate = false;
            foreach (var apiDirectory in apiDirectories)
            {
                Version apiVersion;
                if (!TryParseApiDirectoryVersion(apiDirectory, out apiVersion) || apiVersion.Major < 21)
                {
                    continue;
                }

                var assemblyPath = Path.Combine(apiDirectory, Net48TargetFramework, ModularAssemblyFileName);
                if (!File.Exists(assemblyPath))
                {
                    continue;
                }

                foundCandidate = true;
                AddAssemblyCandidate(
                    portalVersion,
                    apiVersion,
                    normalizedRoot,
                    assemblyPath,
                    OpennessApiFamily.ModularV21Plus,
                    source,
                    Net48TargetFramework,
                    location,
                    context);
            }

            if (reportMissing && !foundCandidate)
            {
                context.AddDiagnostic(new OpennessDiagnostic(
                    OpennessDiagnosticCodes.AssemblyMissing,
                    DiagnosticSeverity.Warning,
                    "No modular net48 Siemens.Engineering.Base.dll candidate was found below the registered Portal root.",
                    publicApiRoot));
            }
        }

        private static void AddAssemblyCandidate(
            Version portalVersion,
            Version apiVersionHint,
            string rootPath,
            string assemblyPath,
            OpennessApiFamily family,
            OpennessDiscoverySource source,
            string targetFrameworkMoniker,
            string registryPath,
            DiscoveryContext context)
        {
            string normalizedAssemblyPath;
            if (!TryNormalizePath(assemblyPath, out normalizedAssemblyPath) || !File.Exists(normalizedAssemblyPath))
            {
                context.AddDiagnostic(new OpennessDiagnostic(
                    OpennessDiagnosticCodes.AssemblyMissing,
                    DiagnosticSeverity.Warning,
                    "The registered Openness assembly does not exist.",
                    assemblyPath));
                return;
            }

            string normalizedRootPath;
            if (!TryNormalizePath(rootPath, out normalizedRootPath) || !Directory.Exists(normalizedRootPath))
            {
                context.AddDiagnostic(new OpennessDiagnostic(
                    OpennessDiagnosticCodes.RegistryValueInvalid,
                    DiagnosticSeverity.Warning,
                    "The Portal root derived for the Openness assembly does not exist.",
                    rootPath));
                return;
            }

            var expectedFileName = family == OpennessApiFamily.LegacyV17ToV20
                ? LegacyAssemblyFileName
                : ModularAssemblyFileName;
            if (!string.Equals(Path.GetFileName(normalizedAssemblyPath), expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                context.AddDiagnostic(new OpennessDiagnostic(
                    OpennessDiagnosticCodes.AssemblyInvalid,
                    DiagnosticSeverity.Warning,
                    "The registry value does not point to the expected " + expectedFileName + " assembly.",
                    normalizedAssemblyPath));
                return;
            }

            AssemblyName assemblyName;
            try
            {
                assemblyName = AssemblyName.GetAssemblyName(normalizedAssemblyPath);
            }
            catch (Exception exception)
            {
                context.AddDiagnostic(CreateExceptionDiagnostic(
                    OpennessDiagnosticCodes.AssemblyInvalid,
                    DiagnosticSeverity.Warning,
                    "The Openness DLL exists but its assembly metadata cannot be read.",
                    normalizedAssemblyPath,
                    exception));
                return;
            }

            var expectedAssemblyName = Path.GetFileNameWithoutExtension(expectedFileName);
            if (!string.Equals(assemblyName.Name, expectedAssemblyName, StringComparison.Ordinal))
            {
                context.AddDiagnostic(new OpennessDiagnostic(
                    OpennessDiagnosticCodes.AssemblyInvalid,
                    DiagnosticSeverity.Warning,
                    "The DLL assembly name does not match " + expectedAssemblyName + ".",
                    normalizedAssemblyPath));
                return;
            }

            var apiVersion = assemblyName.Version ?? apiVersionHint;
            if (apiVersion == null ||
                (family == OpennessApiFamily.LegacyV17ToV20 && (apiVersion.Major < 17 || apiVersion.Major > 20)) ||
                (family == OpennessApiFamily.ModularV21Plus && apiVersion.Major < 21))
            {
                context.AddDiagnostic(new OpennessDiagnostic(
                    OpennessDiagnosticCodes.AssemblyInvalid,
                    DiagnosticSeverity.Warning,
                    "The assembly version does not belong to the expected Openness API family.",
                    normalizedAssemblyPath));
                return;
            }

            context.AddInstallation(new OpennessInstallation(
                portalVersion,
                apiVersion,
                normalizedRootPath,
                normalizedAssemblyPath,
                family,
                source,
                targetFrameworkMoniker,
                registryPath));
        }

        private static RegistryKey OpenSubKeySafe(
            RegistryKey parent,
            string subKeyName,
            string location,
            DiscoveryContext context)
        {
            try
            {
                return parent.OpenSubKey(subKeyName, false);
            }
            catch (Exception exception)
            {
                context.AddDiagnostic(CreateExceptionDiagnostic(
                    OpennessDiagnosticCodes.RegistryAccessFailed,
                    DiagnosticSeverity.Warning,
                    "Cannot open an Openness registry key.",
                    location,
                    exception));
                return null;
            }
        }

        private static string[] GetSubKeyNamesSafe(
            RegistryKey key,
            string location,
            DiscoveryContext context)
        {
            try
            {
                return key.GetSubKeyNames();
            }
            catch (Exception exception)
            {
                context.AddDiagnostic(CreateExceptionDiagnostic(
                    OpennessDiagnosticCodes.RegistryAccessFailed,
                    DiagnosticSeverity.Warning,
                    "Cannot enumerate Openness registry subkeys.",
                    location,
                    exception));
                return new string[0];
            }
        }

        private static string ReadStringValueSafe(
            RegistryKey key,
            string valueName,
            string location,
            DiscoveryContext context)
        {
            try
            {
                return key.GetValue(valueName) as string;
            }
            catch (Exception exception)
            {
                context.AddDiagnostic(CreateExceptionDiagnostic(
                    OpennessDiagnosticCodes.RegistryAccessFailed,
                    DiagnosticSeverity.Warning,
                    "Cannot read the " + valueName + " registry value.",
                    location,
                    exception));
                return null;
            }
        }

        private static Version ReadVersionValueSafe(RegistryKey key, string valueName, Version fallback)
        {
            try
            {
                Version parsed;
                var value = key.GetValue(valueName) as string;
                return TryParseVersionToken(value, out parsed) ? parsed : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static OpennessDiagnostic CreateExceptionDiagnostic(
            string code,
            DiagnosticSeverity severity,
            string message,
            string location,
            Exception exception)
        {
            return new OpennessDiagnostic(
                code,
                severity,
                message,
                location,
                exception == null ? string.Empty : exception.GetType().FullName);
        }

        private static bool TryParsePortalDirectoryVersion(string directoryPath, out Version version)
        {
            version = null;
            try
            {
                var directoryName = new DirectoryInfo(directoryPath).Name;
                const string prefix = "Portal V";
                if (!directoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return TryParseVersionToken(directoryName.Substring(prefix.Length), out version);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryParseApiDirectoryVersion(string directoryPath, out Version version)
        {
            version = null;
            try
            {
                return TryParseVersionToken(new DirectoryInfo(directoryPath).Name, out version);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryParseVersionToken(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.Trim();
            if (text.StartsWith("V", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(1);
            }

            var length = 0;
            while (length < text.Length && (char.IsDigit(text[length]) || text[length] == '.'))
            {
                length++;
            }

            text = text.Substring(0, length).Trim('.');
            if (text.Length == 0)
            {
                return false;
            }

            if (text.IndexOf('.') < 0)
            {
                text += ".0";
            }

            return Version.TryParse(text, out version);
        }

        private static string DerivePortalRoot(string assemblyPath)
        {
            string normalized;
            if (!TryNormalizePath(assemblyPath, out normalized))
            {
                return assemblyPath;
            }

            try
            {
                var current = new FileInfo(normalized).Directory;
                while (current != null)
                {
                    if (string.Equals(current.Name, "PublicAPI", StringComparison.OrdinalIgnoreCase))
                    {
                        return current.Parent == null ? current.FullName : current.Parent.FullName;
                    }

                    current = current.Parent;
                }
            }
            catch (Exception)
            {
            }

            return Path.GetDirectoryName(normalized) ?? normalized;
        }

        private static bool TryNormalizePath(string path, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                normalized = Path.GetFullPath(path.Trim().Trim('"'));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private sealed class DiscoveryContext
        {
            private readonly Dictionary<string, OpennessInstallation> installations =
                new Dictionary<string, OpennessInstallation>(StringComparer.OrdinalIgnoreCase);
            private readonly List<OpennessDiagnostic> diagnostics = new List<OpennessDiagnostic>();

            public void AddInstallation(OpennessInstallation installation)
            {
                OpennessInstallation existing;
                if (!installations.TryGetValue(installation.AssemblyPath, out existing) ||
                    GetSourcePriority(installation.DiscoverySource) > GetSourcePriority(existing.DiscoverySource))
                {
                    installations[installation.AssemblyPath] = installation;
                }
            }

            public void AddDiagnostic(OpennessDiagnostic diagnostic)
            {
                diagnostics.Add(diagnostic);
            }

            public OpennessDiscoveryResult BuildResult()
            {
                var ordered = installations.Values
                    .OrderByDescending(installation => installation.PortalVersion)
                    .ThenByDescending(installation => installation.ApiVersion)
                    .ThenBy(installation => installation.AssemblyPath, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var installation in ordered)
                {
                    diagnostics.Add(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.InstallationFound,
                        DiagnosticSeverity.Information,
                        "Found " + installation + ".",
                        installation.AssemblyPath));
                }

                if (ordered.Count == 0)
                {
                    diagnostics.Add(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.NoInstallationFound,
                        DiagnosticSeverity.Warning,
                        "No supported TIA Portal Openness V17 or newer API was found."));
                }

                diagnostics.Add(new OpennessDiagnostic(
                    OpennessDiagnosticCodes.DiscoveryCompleted,
                    DiagnosticSeverity.Information,
                    "Openness discovery completed with " + ordered.Count + " validated API installation(s)."));

                return new OpennessDiscoveryResult(ordered, diagnostics);
            }

            private static int GetSourcePriority(OpennessDiscoverySource source)
            {
                switch (source)
                {
                    case OpennessDiscoverySource.OpennessRegistry:
                        return 3;
                    case OpennessDiscoverySource.InstalledSoftwareRegistry:
                        return 2;
                    default:
                        return 1;
                }
            }
        }
    }
}
