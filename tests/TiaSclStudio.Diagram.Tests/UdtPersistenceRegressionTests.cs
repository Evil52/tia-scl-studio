using System.Linq;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Storage;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.Diagram.Tests
{
    public sealed class UdtPersistenceRegressionTests
    {
        [Fact]
        public void SaveAndReopenPreserveUdtsReferencesIdsAndImportedInterfaceOwnership()
        {
            using (var directory = new TemporaryDirectory())
            {
                var project = ModelBuilder.Project("TypedProject", "FC_Calls");
                var state = ModelBuilder.Udt(
                    "MachineState",
                    new UdtMember("Code", "Int")
                    {
                        InitialValue = "0",
                        Comment = "Состояние машины"
                    });
                state.Version = "2.1";
                state.Comment = "Пользовательский тип";
                var envelope = ModelBuilder.Udt(
                    "Envelope",
                    new UdtMember("History", "Array[0..3] of \"MachineState\""));
                project.Plant.DataTypes.Add(state);
                project.Plant.DataTypes.Add(envelope);

                var imported = ModelBuilder.Function(
                    "FC_ReadState",
                    "\"MachineState\"",
                    ModelBuilder.Input("InputValue", "\"Envelope\""));
                imported.ImportedInterfaceOnly = true;
                imported.SclBody = string.Empty;
                project.Plant.Blocks.Add(imported);
                var path = directory.File("typed.tiasclproj");

                new DiagramProjectStorage().Save(path, project);
                var reloaded = new DiagramProjectStorage().Load(path);

                Assert.Equal(2, reloaded.Plant.DataTypes.Count);
                var loadedState = reloaded.Plant.DataTypes.Single(item => item.Name == "MachineState");
                Assert.Equal(state.Id, loadedState.Id);
                Assert.Equal(state.Members[0].Id, loadedState.Members[0].Id);
                Assert.Equal("2.1", loadedState.Version);
                Assert.Equal("Пользовательский тип", loadedState.Comment);
                Assert.Equal("0", loadedState.Members[0].InitialValue);
                Assert.Equal("Состояние машины", loadedState.Members[0].Comment);
                Assert.Equal(
                    "Array[0..3] of \"MachineState\"",
                    reloaded.Plant.DataTypes.Single(item => item.Name == "Envelope").Members[0].DataType);

                var loadedBlock = Assert.Single(reloaded.Plant.Blocks);
                Assert.Equal(imported.Id, loadedBlock.Id);
                Assert.True(loadedBlock.ImportedInterfaceOnly);
                Assert.Equal(string.Empty, loadedBlock.SclBody);
                Assert.Equal("\"MachineState\"", loadedBlock.ReturnType);
                Assert.Equal("\"Envelope\"", Assert.Single(loadedBlock.Interface).DataType);
            }
        }
    }
}
