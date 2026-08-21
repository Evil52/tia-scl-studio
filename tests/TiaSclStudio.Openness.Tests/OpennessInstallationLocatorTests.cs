using System;
using System.IO;
using System.Linq;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Discovery;
using TiaSclStudio.Openness.Model;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Openness.Tests
{
    /// <summary>
    /// Discovery runs at startup on machines the developers never see: locked
    /// down service accounts, half-removed Portal installs, network drives that
    /// time out. It is only allowed to fail by producing a diagnostic; an
    /// exception here means the application will not open at all.
    ///
    /// The locator always scans the registry in addition to the fallback roots
    /// it is given, so a build agent with a real TIA Portal finds real
    /// installations. Every assertion below is therefore scoped to the
    /// throwaway root the test created, never to the whole result.
    /// </summary>
    public sealed class OpennessInstallationLocatorTests
    {
        private static OpennessDiscoveryResult LocateIn(params string[] roots)
        {
            return new OpennessInstallationLocator(roots).Locate();
        }

        private static int InstallationsUnder(OpennessDiscoveryResult result, string root)
        {
            return result.Installations.Count(installation =>
                installation.AssemblyPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasDiagnosticUnder(OpennessDiscoveryResult result, string code, string root)
        {
            return result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == code &&
                diagnostic.Location.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void DiscoveryOnTheRealMachineNeverThrows()
        {
            var result = new OpennessInstallationLocator().Locate();

            Assert.NotNull(result);
            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.Code == OpennessDiagnosticCodes.DiscoveryCompleted);
        }

        [Fact]
        public void DiscoveryIsRepeatable()
        {
            var locator = new OpennessInstallationLocator();

            var first = locator.Locate();
            var second = locator.Locate();

            Assert.Equal(
                first.Installations.Select(item => item.AssemblyPath).ToArray(),
                second.Installations.Select(item => item.AssemblyPath).ToArray());
        }

        [Fact]
        public void DiscoveryOrdersInstallationsNewestFirst()
        {
            var installations = new OpennessInstallationLocator().Locate().Installations;

            for (var index = 1; index < installations.Count; index++)
            {
                Assert.True(
                    installations[index - 1].PortalVersion >= installations[index].PortalVersion,
                    "Installations are not ordered by descending Portal version.");
            }
        }

        [Fact]
        public void ToleratesANullRootCollection()
        {
            Assert.NotNull(new OpennessInstallationLocator(null).Locate());
        }

        [Theory]
        [InlineData((string)null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("|not|a|path|")]
        [InlineData("::::")]
        public void AnUnusableRootIsDroppedInsteadOfThrowing(string root)
        {
            // A stale roaming setting can easily contain one of these.
            var result = LocateIn(root);

            Assert.NotNull(result);
            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.Code == OpennessDiagnosticCodes.DiscoveryCompleted);
        }

        [Fact]
        public void ARootThatDoesNotExistContributesNothing()
        {
            using (var directory = new TemporaryDirectory())
            {
                var missing = Path.Combine(directory.Path, "no", "such", "place");

                var result = LocateIn(missing);

                Assert.Equal(0, InstallationsUnder(result, directory.Path));
            }
        }

        [Fact]
        public void ADirectoryThatOnlyLooksLikeAPortalInstallContributesNothing()
        {
            using (var directory = new TemporaryDirectory())
            {
                Directory.CreateDirectory(Path.Combine(directory.Path, "Portal V20", "PublicAPI", "V17"));

                var result = LocateIn(directory.Path);

                Assert.Equal(0, InstallationsUnder(result, directory.Path));
            }
        }

        /// <summary>
        /// A file called Siemens.Engineering.dll that is not a managed assembly
        /// must be rejected with a diagnostic rather than loaded or trusted.
        /// </summary>
        [Fact]
        public void AFileThatIsNotAManagedAssemblyIsRejectedWithADiagnostic()
        {
            using (var directory = new TemporaryDirectory())
            {
                var apiDirectory = Path.Combine(directory.Path, "Portal V20", "PublicAPI", "V17");
                Directory.CreateDirectory(apiDirectory);
                File.WriteAllText(
                    Path.Combine(apiDirectory, "Siemens.Engineering.dll"),
                    "this is not a PE image");

                var result = LocateIn(directory.Path);

                Assert.Equal(0, InstallationsUnder(result, directory.Path));
                Assert.True(
                    HasDiagnosticUnder(result, OpennessDiagnosticCodes.AssemblyInvalid, directory.Path),
                    "A bogus Openness assembly was ignored without a diagnostic.");
            }
        }

        [Fact]
        public void APortalOlderThanTheSupportedFloorIsSkippedSilently()
        {
            using (var directory = new TemporaryDirectory())
            {
                var apiDirectory = Path.Combine(directory.Path, "Portal V15", "PublicAPI", "V15");
                Directory.CreateDirectory(apiDirectory);
                File.WriteAllText(Path.Combine(apiDirectory, "Siemens.Engineering.dll"), "x");

                var result = LocateIn(directory.Path);

                Assert.Equal(0, InstallationsUnder(result, directory.Path));
                Assert.False(
                    HasDiagnosticUnder(result, OpennessDiagnosticCodes.AssemblyInvalid, directory.Path),
                    "An unsupported Portal version produced noise instead of being skipped.");
            }
        }

        [Fact]
        public void ADirectoryNameThatIsNotAPortalVersionIsIgnored()
        {
            using (var directory = new TemporaryDirectory())
            {
                Directory.CreateDirectory(Path.Combine(directory.Path, "Portal Very Old", "PublicAPI", "V17"));

                var result = LocateIn(directory.Path);

                Assert.Equal(0, InstallationsUnder(result, directory.Path));
            }
        }

        [Fact]
        public void RepeatedRootsDoNotProduceRepeatedInstallations()
        {
            using (var directory = new TemporaryDirectory())
            {
                var withSlash = directory.Path + Path.DirectorySeparatorChar;

                var result = LocateIn(directory.Path, directory.Path, withSlash);

                Assert.Equal(
                    result.Installations.Select(item => item.AssemblyPath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    result.Installations.Count);
            }
        }

        [Fact]
        public void EveryRunReportsWhetherAnythingWasFound()
        {
            var result = new OpennessInstallationLocator().Locate();

            var codes = result.Diagnostics.Select(item => item.Code).ToList();
            Assert.Contains(OpennessDiagnosticCodes.DiscoveryCompleted, codes);
            Assert.True(
                result.Installations.Count > 0
                    ? codes.Contains(OpennessDiagnosticCodes.InstallationFound)
                    : codes.Contains(OpennessDiagnosticCodes.NoInstallationFound),
                "The summary diagnostic does not match the installation count.");
        }

        [Fact]
        public void EveryDiagnosticIsUsableInTheUi()
        {
            var result = new OpennessInstallationLocator().Locate();

            Assert.All(result.Diagnostics, diagnostic =>
            {
                Assert.False(string.IsNullOrWhiteSpace(diagnostic.Code));
                Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
                Assert.NotNull(diagnostic.Location);
                Assert.NotNull(diagnostic.ExceptionType);
            });
        }

        [Fact]
        public void ResultCollectionsAreReadOnly()
        {
            var result = new OpennessDiscoveryResult(null, null);

            Assert.Throws<NotSupportedException>(() =>
                ((System.Collections.Generic.IList<OpennessInstallation>)result.Installations).Add(null));
            Assert.Throws<NotSupportedException>(() =>
                ((System.Collections.Generic.IList<OpennessDiagnostic>)result.Diagnostics).Add(null));
        }

        // ---------------------------------------------------------------
        // The installation value object
        // ---------------------------------------------------------------

        [Fact]
        public void AnInstallationNeedsBothVersionsAndBothPaths()
        {
            var version = new Version(20, 0);

            Assert.Throws<ArgumentNullException>(() => new OpennessInstallation(
                null, version, "root", "dll", OpennessApiFamily.LegacyV17ToV20,
                OpennessDiscoverySource.OpennessRegistry, "net48", "hklm"));
            Assert.Throws<ArgumentNullException>(() => new OpennessInstallation(
                version, null, "root", "dll", OpennessApiFamily.LegacyV17ToV20,
                OpennessDiscoverySource.OpennessRegistry, "net48", "hklm"));
            Assert.Throws<ArgumentException>(() => new OpennessInstallation(
                version, version, "  ", "dll", OpennessApiFamily.LegacyV17ToV20,
                OpennessDiscoverySource.OpennessRegistry, "net48", "hklm"));
            Assert.Throws<ArgumentException>(() => new OpennessInstallation(
                version, version, "root", "  ", OpennessApiFamily.LegacyV17ToV20,
                OpennessDiscoverySource.OpennessRegistry, "net48", "hklm"));
        }

        [Fact]
        public void AnInstallationDescribesItselfWithBothVersions()
        {
            var installation = new OpennessInstallation(
                new Version(20, 0),
                new Version(17, 0),
                "root",
                "dll",
                OpennessApiFamily.LegacyV17ToV20,
                OpennessDiscoverySource.OpennessRegistry,
                null,
                null);

            Assert.Equal("Portal V20, API V17 (LegacyV17ToV20)", installation.ToString());
            Assert.Equal(string.Empty, installation.TargetFrameworkMoniker);
            Assert.Equal(string.Empty, installation.RegistryPath);
        }
    }
}
