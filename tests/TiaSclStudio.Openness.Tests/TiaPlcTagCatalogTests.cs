using System;
using System.Collections.Generic;
using System.Threading;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;
using Xunit;

namespace TiaSclStudio.Openness.Tests
{
    public sealed class TiaPlcTagCatalogTests
    {
        [Fact]
        public void ItemKeepsTheStableTableIdentityAndTagValues()
        {
            var item = new TiaPlcTagCatalogItem(
                "UNIT/Packaging/Inputs/Commands",
                "Start",
                "Bool",
                "%I0.0",
                "Start command");

            Assert.Equal("UNIT/Packaging/Inputs/Commands", item.TablePath);
            Assert.Equal("Start", item.Name);
            Assert.Equal("Bool", item.DataType);
            Assert.Equal("%I0.0", item.LogicalAddress);
            Assert.Equal("Start command", item.Comment);
        }

        [Theory]
        [InlineData(null, "Tag", "Bool")]
        [InlineData("", "Tag", "Bool")]
        [InlineData("PLC/Table", null, "Bool")]
        [InlineData("PLC/Table", "Tag", " ")]
        public void ItemRejectsMissingIdentityOrType(
            string tablePath,
            string name,
            string dataType)
        {
            Assert.Throws<ArgumentException>(() => new TiaPlcTagCatalogItem(
                tablePath,
                name,
                dataType,
                null,
                null));
        }

        [Fact]
        public void ItemNormalizesOptionalAddressAndComment()
        {
            var item = new TiaPlcTagCatalogItem(
                "PLC/Table",
                "Tag",
                "Bool",
                null,
                null);

            Assert.Equal(string.Empty, item.LogicalAddress);
            Assert.Equal(string.Empty, item.Comment);
        }

        [Fact]
        public void CatalogCollectionsAreDefensiveAndReadOnly()
        {
            var mutableTags = new List<TiaPlcTagCatalogItem> { Item("Tag1") };
            var mutableDiagnostics = new List<OpennessDiagnostic>
            {
                new OpennessDiagnostic("READ", DiagnosticSeverity.Information, "Read.")
            };
            var catalog = new TiaPlcTagCatalog(
                true,
                true,
                "PLC_1",
                "Software",
                mutableTags,
                mutableDiagnostics);

            mutableTags.Clear();
            mutableDiagnostics.Clear();

            Assert.True(catalog.IsAvailable);
            Assert.True(catalog.IsComplete);
            Assert.Single(catalog.Tags);
            Assert.Single(catalog.Diagnostics);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<TiaPlcTagCatalogItem>)catalog.Tags).Add(Item("Tag2")));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<OpennessDiagnostic>)catalog.Diagnostics).Clear());
        }

        [Fact]
        public void UnavailableCatalogCannotClaimCompleteness()
        {
            var catalog = new TiaPlcTagCatalog(
                false,
                true,
                null,
                null,
                null,
                null);

            Assert.False(catalog.IsAvailable);
            Assert.False(catalog.IsComplete);
            Assert.Equal(string.Empty, catalog.DeviceName);
            Assert.Equal(string.Empty, catalog.SoftwareName);
            Assert.Empty(catalog.Tags);
            Assert.Empty(catalog.Diagnostics);
        }

        [Fact]
        public void CatalogRejectsNullCollectionElements()
        {
            Assert.Throws<ArgumentException>(() => new TiaPlcTagCatalog(
                true,
                true,
                "PLC",
                "Software",
                new TiaPlcTagCatalogItem[] { null },
                null));
            Assert.Throws<ArgumentException>(() => new TiaPlcTagCatalog(
                true,
                true,
                "PLC",
                "Software",
                null,
                new OpennessDiagnostic[] { null }));
        }

        [Fact]
        public void OfflineGatewayReturnsAnExplicitUnavailableTagCatalog()
        {
            using (var gateway = new OfflineGateway())
            {
                var catalog = gateway.ReadPlcTagCatalog(new CancellationToken(true));

                Assert.False(catalog.IsAvailable);
                Assert.False(catalog.IsComplete);
                Assert.Empty(catalog.Tags);
                Assert.Contains(
                    catalog.Diagnostics,
                    diagnostic =>
                        diagnostic.Code == OpennessDiagnosticCodes.PlcTagCatalogUnavailable &&
                        diagnostic.Severity == DiagnosticSeverity.Information);
            }
        }

        [Fact]
        public void GatewayContractUsesOnlyVersionNeutralTypes()
        {
            var method = typeof(ITiaGateway).GetMethod(nameof(ITiaGateway.ReadPlcTagCatalog));

            Assert.NotNull(method);
            Assert.Equal(typeof(TiaPlcTagCatalog), method.ReturnType);
            var parameters = method.GetParameters();
            Assert.Single(parameters);
            Assert.Equal(typeof(CancellationToken), parameters[0].ParameterType);

            AssertNoSiemensPropertyTypes(typeof(TiaPlcTagCatalog));
            AssertNoSiemensPropertyTypes(typeof(TiaPlcTagCatalogItem));
        }

        private static void AssertNoSiemensPropertyTypes(Type type)
        {
            foreach (var property in type.GetProperties())
            {
                Assert.DoesNotContain(
                    "Siemens.Engineering",
                    property.PropertyType.AssemblyQualifiedName ?? string.Empty);
            }
        }

        private static TiaPlcTagCatalogItem Item(string name)
        {
            return new TiaPlcTagCatalogItem(
                "PLC/Default",
                name,
                "Bool",
                string.Empty,
                string.Empty);
        }
    }
}
