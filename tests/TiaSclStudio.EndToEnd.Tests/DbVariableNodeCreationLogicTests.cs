using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.App;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.EndToEnd.Tests
{
    public sealed class DbVariableNodeCreationLogicTests
    {
        [Fact]
        public void CreatesATrimmedWritableUdtReference()
        {
            var project = ProjectWithUdt(out var udt);

            var result = DbVariableNodeCreationLogic.CreateNew(
                project,
                "  DB_Valve  ",
                "  Data  ",
                udt,
                null,
                true,
                TerminalDirection.Source);

            Assert.True(result.CreateDefinition);
            Assert.Equal("DB_Valve", result.Definition.DataBlockName);
            Assert.Equal("Data", result.Definition.VariableName);
            Assert.Equal("Valve_Data", result.Definition.DataType);
            Assert.Equal(string.Empty, result.Definition.Comment);
            Assert.True(result.Definition.GenerateDataBlock);
            Assert.Equal(result.Definition.Id, result.Node.DefinitionId);
            Assert.Equal(TerminalDirection.Source, result.Node.TerminalDirection);
            Assert.Single(result.Node.Pins);
            Assert.Equal(PinDirection.Output, result.Node.Pins[0].Direction);
        }

        [Fact]
        public void CreationRequiresAProjectAndUdt()
        {
            var project = ProjectWithUdt(out var udt);

            Assert.Equal(
                "project",
                Assert.Throws<ArgumentNullException>(() =>
                    DbVariableNodeCreationLogic.CreateNew(
                        null, "DB", "Data", udt, string.Empty, true, TerminalDirection.Source))
                    .ParamName);
            Assert.Equal(
                "dataType",
                Assert.Throws<ArgumentNullException>(() =>
                    DbVariableNodeCreationLogic.CreateNew(
                        project, "DB", "Data", null, string.Empty, true, TerminalDirection.Source))
                    .ParamName);
        }

        [Fact]
        public void ExistingReferenceReusesItsDefinition()
        {
            var definition = ModelBuilder.DbVariable("Existing_DB", "Data", "Valve_Data", false);

            var result = DbVariableNodeCreationLogic.CreateExisting(
                definition,
                TerminalDirection.Sink);

            Assert.False(result.CreateDefinition);
            Assert.Same(definition, result.Definition);
            Assert.Equal(TerminalDirection.Sink, result.Node.TerminalDirection);
            Assert.Equal(PinDirection.Input, Assert.Single(result.Node.Pins).Direction);
            Assert.Equal(
                "definition",
                Assert.Throws<ArgumentNullException>(() =>
                    DbVariableNodeCreationLogic.CreateExisting(null, TerminalDirection.Source))
                    .ParamName);
        }

        [Fact]
        public void ValidationFailsClosedForMissingModels()
        {
            var project = ProjectWithUdt(out var udt);
            var candidate = NewCandidate(project, udt);

            Assert.Contains("Проект PLC отсутствует.",
                DbVariableNodeCreationLogic.Validate(null, Guid.NewGuid(), candidate));

            project.Plant = null;
            Assert.Contains("Проект PLC отсутствует.",
                DbVariableNodeCreationLogic.Validate(project, Guid.NewGuid(), candidate));

            project = ProjectWithUdt(out udt);
            Assert.Contains("Описание DB-переменной отсутствует.",
                DbVariableNodeCreationLogic.Validate(project, project.Sheets[0].Id, null));
            Assert.Contains("Описание DB-переменной отсутствует.",
                DbVariableNodeCreationLogic.Validate(
                    project,
                    project.Sheets[0].Id,
                    new DbVariableNodeCreationResult(null, false, null)));
        }

        [Fact]
        public void ValidationReportsInvalidSheetNamesDirectionAndTypeTogether()
        {
            var project = ProjectWithUdt(out var udt);
            var candidate = DbVariableNodeCreationLogic.CreateNew(
                project,
                "bad-name",
                "bad.name",
                udt,
                string.Empty,
                true,
                TerminalDirection.Source);
            candidate.Definition.DataType = "Missing_Udt";
            candidate.Node.TerminalDirection = (TerminalDirection)999;

            var errors = DbVariableNodeCreationLogic.Validate(project, Guid.NewGuid(), candidate);

            Assert.Contains(errors, error => error.StartsWith("Целевой лист", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.StartsWith("Имя DB:", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.StartsWith("Имя переменной:", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.StartsWith("Направление", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.StartsWith("Выберите существующий", StringComparison.Ordinal));
        }

        [Fact]
        public void ValidationHandlesNullCollectionsAndDuplicateSheetIds()
        {
            var project = ProjectWithUdt(out var udt);
            var sheetId = project.Sheets[0].Id;
            project.Sheets.Add(new CallSheet { Id = sheetId, Name = "Duplicate" });
            project.Plant.DataBlockVariables = null;
            project.Plant.DataTypes = null;
            project.Plant.Blocks = null;
            project.Plant.Tags = null;
            project.Plant.CallBlocks = null;

            var errors = DbVariableNodeCreationLogic.Validate(
                project,
                sheetId,
                NewCandidate(project, udt));

            Assert.Contains(errors, error => error.StartsWith("Целевой лист", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.StartsWith("Выберите существующий", StringComparison.Ordinal));
        }

        [Fact]
        public void ExistingReferenceMustStillBeInTheProjectExactlyOnce()
        {
            var project = ProjectWithUdt(out _);
            var definition = ModelBuilder.DbVariable("Existing_DB", "Data", "Valve_Data", false);
            var candidate = DbVariableNodeCreationLogic.CreateExisting(
                definition,
                TerminalDirection.Source);

            Assert.Contains(
                DbVariableNodeCreationLogic.Validate(project, project.Sheets[0].Id, candidate),
                error => error.StartsWith("Выбранная DB-переменная", StringComparison.Ordinal));

            project.Plant.DataBlockVariables.Add(definition);
            Assert.Empty(DbVariableNodeCreationLogic.Validate(project, project.Sheets[0].Id, candidate));

            project.Plant.DataBlockVariables.Add(definition);
            Assert.Contains(
                DbVariableNodeCreationLogic.Validate(project, project.Sheets[0].Id, candidate),
                error => error.StartsWith("Выбранная DB-переменная", StringComparison.Ordinal));
        }

        [Fact]
        public void NewDefinitionRejectsDuplicateAndMixedOwnership()
        {
            var project = ProjectWithUdt(out var udt);
            project.Plant.DataBlockVariables.Add(
                ModelBuilder.DbVariable("DB_Valve", "Data", "Valve_Data", false));

            var duplicate = DbVariableNodeCreationLogic.CreateNew(
                project,
                "db_valve",
                "data",
                udt,
                string.Empty,
                true,
                TerminalDirection.Source);

            var errors = DbVariableNodeCreationLogic.Validate(
                project,
                project.Sheets[0].Id,
                duplicate);

            Assert.Contains(errors, error => error.StartsWith("Такая DB-переменная", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.StartsWith("Один DB нельзя", StringComparison.Ordinal));
        }

        [Fact]
        public void AGeneratedDbCanOwnSeveralVariables()
        {
            var project = ProjectWithUdt(out var udt);
            project.Plant.DataBlockVariables.Add(
                ModelBuilder.DbVariable("DB_Valve", "First", "Valve_Data"));
            var candidate = DbVariableNodeCreationLogic.CreateNew(
                project,
                "DB_Valve",
                "Second",
                udt,
                string.Empty,
                true,
                TerminalDirection.Source);

            Assert.Empty(DbVariableNodeCreationLogic.Validate(
                project,
                project.Sheets[0].Id,
                candidate));
        }

        [Fact]
        public void GeneratedDbRejectsEveryOwnedGlobalSymbolKind()
        {
            var project = ProjectWithUdt(out var udt);
            project.Plant.Blocks.Add(ModelBuilder.FunctionBlock("BlockSymbol"));
            project.Plant.DataTypes.Add(ModelBuilder.Udt("TypeSymbol"));
            project.Plant.Tags.Add(ModelBuilder.Tag("TagSymbol"));
            project.Plant.CallBlocks.Add(new CallBlockDefinition { Name = "CallSymbol" });
            project.Plant.DataBlockVariables.Add(
                ModelBuilder.DbVariable("DbSymbol", "First", "Valve_Data"));

            foreach (var name in new[] { "BlockSymbol", "TypeSymbol", "TagSymbol", "CallSymbol" })
            {
                var candidate = DbVariableNodeCreationLogic.CreateNew(
                    project,
                    name,
                    "Data",
                    udt,
                    string.Empty,
                    true,
                    TerminalDirection.Source);
                Assert.Contains(
                    DbVariableNodeCreationLogic.Validate(project, project.Sheets[0].Id, candidate),
                    error => error.StartsWith("Имя создаваемого DB", StringComparison.Ordinal));
            }

            project.Plant.Blocks.Add(new BlockDefinition { Name = " " });
            project.Plant.Tags.Add(null);
            project.Plant.DataBlockVariables.Add(
                new DataBlockVariableDefinition { DataBlockName = " ", GenerateDataBlock = true });
            var valid = DbVariableNodeCreationLogic.CreateNew(
                project,
                "FreshDb",
                "Data",
                udt,
                string.Empty,
                true,
                TerminalDirection.Source);
            Assert.Empty(DbVariableNodeCreationLogic.Validate(project, project.Sheets[0].Id, valid));
        }

        [Fact]
        public void ApplyAddsDefinitionAndNodeAndInitializesTheCollection()
        {
            var project = ProjectWithUdt(out var udt);
            project.Plant.DataBlockVariables = null;
            var candidate = NewCandidate(project, udt);

            DbVariableNodeCreationLogic.Apply(project, project.Sheets[0].Id, candidate);

            Assert.Same(candidate.Definition, Assert.Single(project.Plant.DataBlockVariables));
            Assert.Same(candidate.Node, Assert.Single(project.Sheets[0].Nodes));
        }

        [Fact]
        public void ApplyExistingAddsOnlyANodeAndInvalidApplyIsAtomic()
        {
            var project = ProjectWithUdt(out _);
            var definition = ModelBuilder.DbVariable("Existing_DB", "Data", "Valve_Data", false);
            project.Plant.DataBlockVariables.Add(definition);
            var existing = DbVariableNodeCreationLogic.CreateExisting(
                definition,
                TerminalDirection.Source);

            DbVariableNodeCreationLogic.Apply(project, project.Sheets[0].Id, existing);

            Assert.Single(project.Plant.DataBlockVariables);
            Assert.Same(existing.Node, Assert.Single(project.Sheets[0].Nodes));

            var invalid = new DbVariableNodeCreationResult(null, true, null);
            Assert.Throws<InvalidOperationException>(() =>
                DbVariableNodeCreationLogic.Apply(project, project.Sheets[0].Id, invalid));
            Assert.Single(project.Plant.DataBlockVariables);
            Assert.Single(project.Sheets[0].Nodes);
        }

        private static DiagramProject ProjectWithUdt(out UdtDefinition udt)
        {
            var project = ModelBuilder.Project();
            udt = ModelBuilder.Udt("Valve_Data", new UdtMember("Open", "Bool"));
            project.Plant.DataTypes.Add(udt);
            return project;
        }

        private static DbVariableNodeCreationResult NewCandidate(
            DiagramProject project,
            UdtDefinition udt)
        {
            return DbVariableNodeCreationLogic.CreateNew(
                project,
                "DB_Valve",
                "Data",
                udt,
                "Valve state",
                true,
                TerminalDirection.Source);
        }
    }
}
