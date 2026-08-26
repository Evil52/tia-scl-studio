using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    internal sealed class DbVariableNodeCreationResult
    {
        internal DbVariableNodeCreationResult(
            DataBlockVariableDefinition definition,
            bool createDefinition,
            DataBlockVariableNode node)
        {
            Definition = definition;
            CreateDefinition = createDefinition;
            Node = node;
        }

        internal DataBlockVariableDefinition Definition { get; private set; }

        internal bool CreateDefinition { get; private set; }

        internal DataBlockVariableNode Node { get; private set; }
    }

    internal static class DbVariableNodeCreationLogic
    {
        internal static DbVariableNodeCreationResult CreateNew(
            DiagramProject project,
            string dataBlockName,
            string variableName,
            UdtDefinition dataType,
            string comment,
            bool generateDataBlock,
            TerminalDirection direction)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            if (dataType == null)
            {
                throw new ArgumentNullException("dataType");
            }

            var definition = new DataBlockVariableDefinition
            {
                DataBlockName = (dataBlockName ?? string.Empty).Trim(),
                VariableName = (variableName ?? string.Empty).Trim(),
                DataType = dataType.Name,
                Comment = comment ?? string.Empty,
                GenerateDataBlock = generateDataBlock
            };
            return new DbVariableNodeCreationResult(
                definition,
                true,
                DiagramNodeFactory.CreateDataBlockVariable(definition, direction, 0.0, 0.0));
        }

        internal static DbVariableNodeCreationResult CreateExisting(
            DataBlockVariableDefinition definition,
            TerminalDirection direction)
        {
            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            return new DbVariableNodeCreationResult(
                definition,
                false,
                DiagramNodeFactory.CreateDataBlockVariable(definition, direction, 0.0, 0.0));
        }

        internal static IList<string> Validate(
            DiagramProject project,
            Guid sheetId,
            DbVariableNodeCreationResult candidate)
        {
            var errors = new List<string>();
            if (project == null || project.Plant == null)
            {
                errors.Add("Проект PLC отсутствует.");
                return errors;
            }

            if (candidate == null || candidate.Definition == null || candidate.Node == null)
            {
                errors.Add("Описание DB-переменной отсутствует.");
                return errors;
            }

            if ((project.Sheets ?? new List<CallSheet>()).Count(sheet =>
                sheet != null && sheet.Id == sheetId) != 1)
            {
                errors.Add("Целевой лист отсутствует или имеет дублирующийся ID.");
            }

            string nameError;
            if (!SclName.TryValidate(candidate.Definition.DataBlockName, out nameError))
            {
                errors.Add("Имя DB: " + nameError);
            }

            if (!SclName.TryValidate(candidate.Definition.VariableName, out nameError))
            {
                errors.Add("Имя переменной: " + nameError);
            }

            if (!Enum.IsDefined(typeof(TerminalDirection), candidate.Node.TerminalDirection))
            {
                errors.Add("Направление терминала не поддерживается.");
            }

            var plant = project.Plant;
            var variables = plant.DataBlockVariables ?? new List<DataBlockVariableDefinition>();
            var types = plant.DataTypes ?? new List<UdtDefinition>();
            string canonicalType;
            Guid udtId;
            if (!PlcDataTypes.TryResolve(
                    candidate.Definition.DataType,
                    types,
                    false,
                    out canonicalType,
                    out udtId) ||
                udtId == Guid.Empty)
            {
                errors.Add("Выберите существующий пользовательский тип UDT.");
            }

            if (!candidate.CreateDefinition)
            {
                if (variables.Count(item => item != null && item.Id == candidate.Definition.Id) != 1)
                {
                    errors.Add("Выбранная DB-переменная больше не существует в проекте.");
                }

                return errors.Distinct(StringComparer.Ordinal).ToList();
            }

            if (variables.Any(item => item != null &&
                string.Equals(item.DataBlockName, candidate.Definition.DataBlockName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.VariableName, candidate.Definition.VariableName, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add("Такая DB-переменная уже описана. Выберите режим «Использовать существующую». ");
            }

            var sameDb = variables.Where(item => item != null && string.Equals(
                item.DataBlockName,
                candidate.Definition.DataBlockName,
                StringComparison.OrdinalIgnoreCase)).ToList();
            if (sameDb.Any() && sameDb.Any(item =>
                item.GenerateDataBlock != candidate.Definition.GenerateDataBlock))
            {
                errors.Add("Один DB нельзя одновременно создавать приложением и считать существующим в TIA.");
            }

            if (candidate.Definition.GenerateDataBlock &&
                sameDb.Count == 0 &&
                EnumerateOwnedGlobalSymbols(plant).Contains(candidate.Definition.DataBlockName))
            {
                errors.Add("Имя создаваемого DB конфликтует с существующим глобальным объектом проекта.");
            }

            return errors.Distinct(StringComparer.Ordinal).ToList();
        }

        internal static void Apply(
            DiagramProject project,
            Guid sheetId,
            DbVariableNodeCreationResult result)
        {
            var errors = Validate(project, sheetId, result);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }

            if (project.Plant.DataBlockVariables == null)
            {
                project.Plant.DataBlockVariables = new List<DataBlockVariableDefinition>();
            }

            if (result.CreateDefinition)
            {
                project.Plant.DataBlockVariables.Add(result.Definition);
            }

            project.Sheets.Single(sheet => sheet != null && sheet.Id == sheetId)
                .Nodes.Add(result.Node);
        }

        private static HashSet<string> EnumerateOwnedGlobalSymbols(PlantProject plant)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in (plant.Blocks ?? new List<BlockDefinition>())
                .Where(item => item != null)
                .Select(item => item.Name)
                .Concat((plant.DataTypes ?? new List<UdtDefinition>())
                    .Where(item => item != null)
                    .Select(item => item.Name))
                .Concat((plant.Tags ?? new List<TagDefinition>())
                    .Where(item => item != null)
                    .Select(item => item.Name))
                .Concat((plant.CallBlocks ?? new List<CallBlockDefinition>())
                    .Where(item => item != null)
                    .Select(item => item.Name)))
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name.Trim());
                }
            }

            foreach (var dbName in (plant.DataBlockVariables ?? new List<DataBlockVariableDefinition>())
                .Where(item => item != null && item.GenerateDataBlock)
                .Select(item => item.DataBlockName))
            {
                if (!string.IsNullOrWhiteSpace(dbName))
                {
                    names.Add(dbName.Trim());
                }
            }

            return names;
        }
    }
}
