using System;
using System.Linq;
using TiaSclStudio.App;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.History;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Openness.Gateway;
using TiaSclStudio.TestSupport;
using Xunit;
using CoreCpuFamily = TiaSclStudio.Core.Model.PlcCpuFamily;
using GatewayCpuFamily = TiaSclStudio.Openness.Gateway.TiaCpuFamily;

namespace TiaSclStudio.EndToEnd.Tests
{
    /// <summary>
    /// Locks the fail-closed boundary between the immutable TIA hardware snapshot
    /// and every tag create/edit path in the application.
    /// </summary>
    public sealed class HardwareIoSelectionPolicyTests
    {
        [Theory]
        [InlineData(GatewayCpuFamily.S71200, CoreCpuFamily.S71200)]
        [InlineData(GatewayCpuFamily.S71500, CoreCpuFamily.S71500)]
        public void MapsOnlyConcreteCpuFamilies(
            GatewayCpuFamily gatewayCpu,
            CoreCpuFamily expectedCpu)
        {
            var context = HardwareIoSelectionPolicy.FromOnlineCatalog(
                Catalog(true, gatewayCpu));

            Assert.True(context.FixedCpuFamily.HasValue);
            Assert.Equal(expectedCpu, context.FixedCpuFamily.Value);
        }

        [Fact]
        public void DigitalInputsAndOutputsExposeOnlyBool()
        {
            var context = HardwareIoSelectionPolicy.FromOnlineCatalog(
                Catalog(
                    true,
                    GatewayCpuFamily.S71200,
                    Channel(
                        TiaIoChannelKind.DigitalInput,
                        0,
                        1,
                        "%I0.0",
                        "Bool",
                        "Int",
                        "Word",
                        "Real"),
                    Channel(
                        TiaIoChannelKind.DigitalOutput,
                        1,
                        1,
                        "%Q1.7",
                        "Bool",
                        "Int")));

            Assert.Equal(2, context.Choices.Count);
            Assert.Contains(
                context.Choices,
                choice => choice.SignalKind == PlcSignalKind.DigitalInput &&
                          choice.Address == "%I0.0" &&
                          choice.DataType == "Bool");
            Assert.Contains(
                context.Choices,
                choice => choice.SignalKind == PlcSignalKind.DigitalOutput &&
                          choice.Address == "%Q1.7" &&
                          choice.DataType == "Bool");
            Assert.DoesNotContain(
                context.Choices,
                choice => !string.Equals(choice.DataType, "Bool", StringComparison.Ordinal));
        }

        [Fact]
        public void SixteenBitAnalogChannelsExposeIntAndWord()
        {
            var context = HardwareIoSelectionPolicy.FromOnlineCatalog(
                Catalog(
                    true,
                    GatewayCpuFamily.S71500,
                    Channel(
                        TiaIoChannelKind.AnalogInput,
                        0,
                        16,
                        "%IW64",
                        "Int",
                        "Word"),
                    Channel(
                        TiaIoChannelKind.AnalogOutput,
                        1,
                        16,
                        "%QW128",
                        "Int",
                        "Word")));

            Assert.Equal(
                new[] { "Int", "Word" },
                context.Choices
                    .Where(choice => choice.SignalKind == PlcSignalKind.AnalogInput)
                    .Select(choice => choice.DataType)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray());
            Assert.Equal(
                new[] { "Int", "Word" },
                context.Choices
                    .Where(choice => choice.SignalKind == PlcSignalKind.AnalogOutput)
                    .Select(choice => choice.DataType)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray());
        }

        [Fact]
        public void ThirtyTwoBitAnalogChannelsExposeReal()
        {
            var context = HardwareIoSelectionPolicy.FromOnlineCatalog(
                Catalog(
                    true,
                    GatewayCpuFamily.S71500,
                    Channel(
                        TiaIoChannelKind.AnalogInput,
                        0,
                        32,
                        "%ID256",
                        "Real"),
                    Channel(
                        TiaIoChannelKind.AnalogOutput,
                        1,
                        32,
                        "%QD512",
                        "Real")));

            Assert.Equal(2, context.Choices.Count);
            Assert.All(
                context.Choices,
                choice => Assert.Equal("Real", choice.DataType));
            Assert.Contains(context.Choices, choice => choice.Address == "%ID256");
            Assert.Contains(context.Choices, choice => choice.Address == "%QD512");
        }

        [Fact]
        public void UnavailableOrUnknownCatalogFailsClosedIncludingMemory()
        {
            var contexts = new[]
            {
                HardwareIoSelectionPolicy.FromOnlineCatalog(null),
                HardwareIoSelectionPolicy.FromOnlineCatalog(
                    Catalog(false, GatewayCpuFamily.S71200)),
                HardwareIoSelectionPolicy.FromOnlineCatalog(
                    Catalog(false, GatewayCpuFamily.Unknown)),
                HardwareIoSelectionPolicy.FromOnlineCatalog(
                    Catalog(true, GatewayCpuFamily.Unknown))
            };

            foreach (var context in contexts)
            {
                string error;
                Assert.False(context.FixedCpuFamily.HasValue);
                Assert.False(context.TryValidateTag("%I0.0", "Bool", out error));
                Assert.False(context.TryValidateTag("%M0.0", "Bool", out error));
            }
        }

        [Fact]
        public void HardwareMembershipUsesCanonicalAddressAndExactDataType()
        {
            var context = HardwareIoSelectionPolicy.FromOnlineCatalog(
                Catalog(
                    true,
                    GatewayCpuFamily.S71200,
                    Channel(
                        TiaIoChannelKind.AnalogInput,
                        0,
                        16,
                        "%IW64",
                        "Int",
                        "Word")));

            string error;
            Assert.True(context.TryValidateTag("iw064", "int", out error), error);
            Assert.True(context.TryValidateTag("%IW64", "Word", out error), error);
            Assert.False(context.TryValidateTag("%IW65", "Int", out error));
            Assert.False(context.TryValidateTag("%IW64", "Real", out error));
            Assert.False(context.TryValidateTag("%ID64", "Real", out error));
        }

        [Theory]
        [InlineData(GatewayCpuFamily.S71200, "%MD8188", "%MD8189")]
        [InlineData(GatewayCpuFamily.S71500, "%MD16380", "%MD16381")]
        public void MemoryStillUsesTheSelectedCpuRange(
            GatewayCpuFamily cpuFamily,
            string lastValid,
            string firstInvalid)
        {
            var context = HardwareIoSelectionPolicy.FromOnlineCatalog(
                Catalog(true, cpuFamily));

            string error;
            Assert.True(context.TryValidateTag(lastValid, "Real", out error), error);
            Assert.False(context.TryValidateTag(firstInvalid, "Real", out error));
        }

        [Fact]
        public void RejectsNewTagWhenCpuChangedWhileDialogWasOpen()
        {
            var project = ProjectWithCpu(CoreCpuFamily.S71500);
            var result = NodeCreationLogic.CreateNewTag(
                "InputFromOldCpu",
                "Bool",
                "%M0.0",
                string.Empty,
                TerminalDirection.Source,
                CoreCpuFamily.S71500);
            var latestContext = HardwareIoSelectionPolicy.FromOnlineCatalog(
                Catalog(true, GatewayCpuFamily.S71200));

            string error;
            Assert.False(
                latestContext.TryValidateCreationResult(project, result, out error));
        }

        [Fact]
        public void RejectsExistingTagWhenCpuChangedWhileDialogWasOpen()
        {
            var project = ProjectWithCpu(CoreCpuFamily.S71500);
            var tag = ModelBuilder.Tag("ExistingMarker", "Bool", "%M0.0");
            project.Plant.Tags.Add(tag);
            var result = NodeCreationLogic.CreateExistingTag(
                tag.Id,
                TerminalDirection.Source,
                CoreCpuFamily.S71500);
            var latestContext = HardwareIoSelectionPolicy.FromOnlineCatalog(
                Catalog(true, GatewayCpuFamily.S71200));

            string error;
            Assert.False(
                latestContext.TryValidateCreationResult(project, result, out error));
        }

        [Fact]
        public void RejectsTagEditWhenCpuChangedWhileDialogWasOpen()
        {
            var project = ProjectWithCpu(CoreCpuFamily.S71500);
            var tag = ModelBuilder.Tag("EditedMarker", "Bool", "%M0.0");
            project.Plant.Tags.Add(tag);
            var node = ModelBuilder.PlaceTag(
                project.Sheets[0],
                tag,
                TerminalDirection.Source);
            var result = NodeEditingLogic.CreateTagResult(
                node,
                tag,
                CoreCpuFamily.S71500);
            var latestContext = HardwareIoSelectionPolicy.FromOnlineCatalog(
                Catalog(true, GatewayCpuFamily.S71200));

            string error;
            Assert.False(latestContext.TryValidateEditResult(result, out error));
        }

        [Fact]
        public void ExistingTagSelectionAllowsOnlyMemoryOrAnExactHardwareChannel()
        {
            var context = HardwareIoSelectionPolicy.FromOnlineCatalog(
                Catalog(
                    true,
                    GatewayCpuFamily.S71200,
                    Channel(
                        TiaIoChannelKind.DigitalInput,
                        0,
                        1,
                        "%I0.0",
                        "Bool")));

            Assert.True(context.IsStoredTagSelectable(
                ModelBuilder.Tag("CanonicalInput", "Bool", "i00.0")));
            Assert.True(context.IsStoredTagSelectable(
                ModelBuilder.Tag("Marker", "Bool", "%M12.3")));
            Assert.False(context.IsStoredTagSelectable(
                ModelBuilder.Tag("MissingOutput", "Bool", "%Q0.0")));
            Assert.False(context.IsStoredTagSelectable(
                ModelBuilder.Tag("WrongType", "Int", "%I0.0")));
            Assert.False(context.IsStoredTagSelectable(
                ModelBuilder.Tag("OutOfRangeMarker", "Real", "%MD8189")));
        }

        [Fact]
        public void CpuMigrationCannotInvalidateAnotherProjectTag()
        {
            var project = ProjectWithCpu(CoreCpuFamily.S71500);
            project.Plant.Tags.Add(
                ModelBuilder.Tag("FarMarker", "Real", "%MD10000"));
            var result = NodeCreationLogic.CreateNewTag(
                "NearMarker",
                "Bool",
                "%M0.0",
                string.Empty,
                TerminalDirection.Source,
                CoreCpuFamily.S71200);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                NodeCreationLogic.Apply(project, project.Sheets[0].Id, result));

            Assert.Contains("FarMarker", exception.Message, StringComparison.Ordinal);
            Assert.Equal(CoreCpuFamily.S71500, project.Plant.CpuFamily);
            Assert.Single(project.Plant.Tags);
            Assert.Empty(project.Sheets[0].Nodes);
        }

        [Fact]
        public void CpuAndTagCreationRoundTripAsOneHistoryEntry()
        {
            var project = ProjectWithCpu(CoreCpuFamily.S71200);
            var before = DiagramProjectSnapshot.Clone(project);
            var result = NodeCreationLogic.CreateNewTag(
                "LargeInput",
                "Word",
                "%IW2000",
                "Available only in the selected S7-1500 range.",
                TerminalDirection.Source,
                CoreCpuFamily.S71500);

            NodeCreationLogic.Apply(project, project.Sheets[0].Id, result);
            Assert.Equal(CoreCpuFamily.S71500, project.Plant.CpuFamily);
            Assert.Contains(project.Plant.Tags, tag => tag.Name == "LargeInput");
            Assert.Contains(
                project.Sheets[0].Nodes.OfType<TagNode>(),
                node => node.TagName == "LargeInput");

            var history = new DiagramEditHistory();
            Assert.True(history.Record("create PLC tag", before, project));
            Assert.Equal(1, history.UndoCount);

            var undone = history.Undo();
            Assert.Equal(CoreCpuFamily.S71200, undone.Plant.CpuFamily);
            Assert.DoesNotContain(undone.Plant.Tags, tag => tag.Name == "LargeInput");
            Assert.DoesNotContain(
                undone.Sheets[0].Nodes.OfType<TagNode>(),
                node => node.TagName == "LargeInput");

            var redone = history.Redo();
            Assert.Equal(CoreCpuFamily.S71500, redone.Plant.CpuFamily);
            Assert.Contains(redone.Plant.Tags, tag => tag.Name == "LargeInput");
            Assert.Contains(
                redone.Sheets[0].Nodes.OfType<TagNode>(),
                node => node.TagName == "LargeInput");
        }

        private static DiagramProject ProjectWithCpu(CoreCpuFamily cpuFamily)
        {
            var project = ModelBuilder.Project();
            project.Plant.CpuFamily = cpuFamily;
            return project;
        }

        private static TiaHardwareIoCatalog Catalog(
            bool isAvailable,
            GatewayCpuFamily cpuFamily,
            params TiaHardwareIoChannel[] channels)
        {
            return new TiaHardwareIoCatalog(
                isAvailable,
                cpuFamily,
                cpuFamily.ToString(),
                "test.cpu",
                channels,
                null);
        }

        private static TiaHardwareIoChannel Channel(
            TiaIoChannelKind kind,
            int number,
            uint widthBits,
            string address,
            params string[] dataTypes)
        {
            return new TiaHardwareIoChannel(
                kind,
                number,
                number * 8,
                widthBits,
                address,
                dataTypes,
                "PLC_1/Module_1",
                "test.module",
                "Test module");
        }
    }
}
