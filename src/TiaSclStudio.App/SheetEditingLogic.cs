using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    internal static class SheetEditingLogic
    {
        internal const double DefaultSheetWidth = 1180.0;
        internal const double DefaultSheetHeight = 700.0;
        internal const double MinimumSheetWidth = 900.0;
        internal const double MinimumSheetHeight = 560.0;
        internal const double MaximumSheetExtent = 50000.0;

        internal static CallSheet CreateSheet(DiagramProject project, string name)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            string error;
            if (!TryValidateName(project, null, name, out error))
            {
                throw new InvalidOperationException(error);
            }

            if (project.Sheets == null)
            {
                project.Sheets = new List<CallSheet>();
            }

            var sheet = new CallSheet
            {
                Name = name,
                Width = DefaultSheetWidth,
                Height = DefaultSheetHeight,
                Zoom = 1.0
            };
            project.Sheets.Add(sheet);
            return sheet;
        }

        internal static void RenameSheet(
            DiagramProject project,
            CallSheet sheet,
            string name)
        {
            EnsureSheetBelongsToProject(project, sheet);
            string error;
            if (!TryValidateName(project, sheet, name, out error))
            {
                throw new InvalidOperationException(error);
            }

            sheet.Name = name;
        }

        internal static void EditProperties(
            DiagramProject project,
            CallSheet sheet,
            string name,
            double width,
            double height)
        {
            EnsureSheetBelongsToProject(project, sheet);

            string error;
            if (!TryValidateName(project, sheet, name, out error))
            {
                throw new InvalidOperationException(error);
            }

            ValidateSheetExtent(width, MinimumSheetWidth, "width");
            ValidateSheetExtent(height, MinimumSheetHeight, "height");
            EnsureContentFits(sheet, width, height);

            // Commit only after the complete candidate has been validated. The caller can
            // therefore record this method as one semantic history transaction.
            sheet.Name = name;
            sheet.Width = width;
            sheet.Height = height;
        }

        internal static CallSheet DeleteSheet(DiagramProject project, CallSheet sheet)
        {
            EnsureSheetBelongsToProject(project, sheet);
            if (project.Sheets.Count <= 1)
            {
                throw new InvalidOperationException("В проекте должен оставаться минимум один лист вызовов.");
            }

            var index = project.Sheets.IndexOf(sheet);
            project.Sheets.RemoveAt(index);
            return project.Sheets[Math.Min(index, project.Sheets.Count - 1)];
        }

        internal static string CreateUniqueDefaultName(DiagramProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            var sequence = 1;
            while (true)
            {
                var candidate = sequence == 1
                    ? "FC_CallUnits"
                    : "FC_CallUnits_" + sequence.ToString("00");
                string error;
                if (TryValidateName(project, null, candidate, out error))
                {
                    return candidate;
                }

                sequence++;
            }
        }

        internal static bool TryValidateName(
            DiagramProject project,
            CallSheet editedSheet,
            string candidateName,
            out string error)
        {
            if (project == null)
            {
                error = "Проект не загружен.";
                return false;
            }

            string sclError;
            if (!SclName.TryValidate(candidateName, out sclError))
            {
                error = "Недопустимое имя листа: " + TranslateSclNameError(sclError);
                return false;
            }

            var symbols = EnumerateReservedSymbols(project, editedSheet).ToList();
            var collision = symbols.FirstOrDefault(item => string.Equals(
                item.Name,
                candidateName,
                StringComparison.OrdinalIgnoreCase));
            if (collision != null)
            {
                error = "Имя «" + candidateName + "» уже занято: " + collision.Owner + ".";
                return false;
            }

            var hasMultiInstances = editedSheet != null && SheetHasMultiInstance(project, editedSheet);
            if (hasMultiInstances)
            {
                var dbName = candidateName + "_DB";
                if (!SclName.TryValidate(dbName, out sclError))
                {
                    error = "Для этого листа потребуется недопустимое имя DB «" + dbName +
                        "»: " + TranslateSclNameError(sclError);
                    return false;
                }

                collision = symbols.FirstOrDefault(item => string.Equals(
                    item.Name,
                    dbName,
                    StringComparison.OrdinalIgnoreCase));
                if (collision != null)
                {
                    error = "Имя DB листа «" + dbName + "» уже занято: " + collision.Owner + ".";
                    return false;
                }

                if (string.Equals(candidateName, dbName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Имя листа совпадает с именем создаваемого DB.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static IEnumerable<SymbolEntry> EnumerateReservedSymbols(
            DiagramProject project,
            CallSheet excludedSheet)
        {
            var plant = project.Plant ?? new PlantProject();
            foreach (var block in plant.Blocks ?? new List<BlockDefinition>())
            {
                if (block != null)
                {
                    yield return new SymbolEntry(block.Name, "блок " + block.Name);
                }
            }

            foreach (var dataType in plant.DataTypes ?? new List<UdtDefinition>())
            {
                if (dataType != null)
                {
                    yield return new SymbolEntry(dataType.Name, "тип данных " + dataType.Name);
                }
            }

            foreach (var tag in plant.Tags ?? new List<TagDefinition>())
            {
                if (tag != null)
                {
                    yield return new SymbolEntry(tag.Name, "PLC-тег " + tag.Name);
                }
            }

            var blocks = (plant.Blocks ?? new List<BlockDefinition>())
                .Where(item => item != null)
                .GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (var callBlock in plant.CallBlocks ?? new List<CallBlockDefinition>())
            {
                if (callBlock == null)
                {
                    continue;
                }

                yield return new SymbolEntry(callBlock.Name, "блок вызовов " + callBlock.Name);
                if (callBlock.Kind == BlockKind.FunctionBlock)
                {
                    var dbName = string.IsNullOrWhiteSpace(callBlock.InstanceDbName)
                        ? (callBlock.Name ?? string.Empty) + "_DB"
                        : callBlock.InstanceDbName.Trim();
                    yield return new SymbolEntry(dbName, "DB блока вызовов " + dbName);
                }

                foreach (var unit in callBlock.Units ?? new List<UnitDefinition>())
                {
                    if (unit == null || unit.UseMultiInstance)
                    {
                        continue;
                    }

                    BlockDefinition definition;
                    if (!blocks.TryGetValue(unit.BlockId, out definition))
                    {
                        definition = (plant.Blocks ?? new List<BlockDefinition>())
                            .FirstOrDefault(item =>
                                item != null &&
                                string.Equals(
                                    item.Name,
                                    unit.BlockName,
                                    StringComparison.OrdinalIgnoreCase));
                    }

                    if (definition != null && definition.Kind == BlockKind.FunctionBlock)
                    {
                        yield return new SymbolEntry(unit.Name, "instance DB " + unit.Name);
                    }
                }
            }

            foreach (var sheet in (project.Sheets ?? new List<CallSheet>())
                .Where(item => item != null && !ReferenceEquals(item, excludedSheet)))
            {
                yield return new SymbolEntry(sheet.Name, "лист вызовов " + sheet.Name);
                if (SheetHasMultiInstance(project, sheet))
                {
                    var dbName = (sheet.Name ?? string.Empty) + "_DB";
                    yield return new SymbolEntry(dbName, "DB листа вызовов " + dbName);
                }
            }

            foreach (var node in (project.Sheets ?? new List<CallSheet>())
                .Where(item => item != null)
                .SelectMany(item => item.Nodes ?? new List<CallNode>())
                .OfType<BlockCallNode>())
            {
                BlockDefinition definition;
                if (blocks.TryGetValue(node.BlockDefinitionId, out definition) &&
                    definition.Kind == BlockKind.FunctionBlock &&
                    !node.UseMultiInstance)
                {
                    yield return new SymbolEntry(
                        node.InstanceName,
                        "instance DB " + node.InstanceName);
                }
            }
        }

        private static bool SheetHasMultiInstance(DiagramProject project, CallSheet sheet)
        {
            var plant = project.Plant ?? new PlantProject();
            var blocks = (plant.Blocks ?? new List<BlockDefinition>())
                .Where(item => item != null)
                .GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (var node in (sheet.Nodes ?? new List<CallNode>()).OfType<BlockCallNode>())
            {
                BlockDefinition definition;
                if (blocks.TryGetValue(node.BlockDefinitionId, out definition) &&
                    definition.Kind == BlockKind.FunctionBlock &&
                    node.UseMultiInstance)
                {
                    return true;
                }
            }

            return false;
        }

        private static string TranslateSclNameError(string error)
        {
            if (string.Equals(error, "Name is required.", StringComparison.Ordinal))
            {
                return "имя обязательно.";
            }

            if (string.Equals(
                error,
                "Name must not have leading or trailing whitespace.",
                StringComparison.Ordinal))
            {
                return "уберите пробелы в начале и конце.";
            }

            if (error != null && error.StartsWith("Name must not exceed", StringComparison.Ordinal))
            {
                return "длина имени не должна превышать " + SclName.MaximumLength + " символов.";
            }

            if (string.Equals(
                error,
                "Name must start with an ASCII letter or underscore and contain only ASCII letters, digits and underscores.",
                StringComparison.Ordinal))
            {
                return "используйте латинские буквы, цифры и подчёркивание; первый символ — буква или подчёркивание.";
            }

            if (string.Equals(error, "Name is an SCL reserved word.", StringComparison.Ordinal))
            {
                return "это зарезервированное слово SCL.";
            }

            return "недопустимый SCL-идентификатор.";
        }

        private static void EnsureSheetBelongsToProject(
            DiagramProject project,
            CallSheet sheet)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            if (sheet == null)
            {
                throw new ArgumentNullException("sheet");
            }

            if (project.Sheets == null || !project.Sheets.Contains(sheet))
            {
                throw new InvalidOperationException("Лист не принадлежит текущему проекту.");
            }
        }

        private static void ValidateSheetExtent(
            double value,
            double minimum,
            string parameterName)
        {
            if (!IsFinite(value) || value < minimum || value > MaximumSheetExtent)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Sheet extent must be finite and between " + minimum +
                    " and " + MaximumSheetExtent + ".");
            }
        }

        private static void EnsureContentFits(CallSheet sheet, double width, double height)
        {
            foreach (var node in sheet.Nodes ?? new List<CallNode>())
            {
                if (node == null)
                {
                    continue;
                }

                var nodeWidth = GetNodeWidth(node);
                var nodeHeight = GetNodeHeight(node);
                if (!FitsInsideSheet(node.X, node.Y, nodeWidth, nodeHeight, width, height))
                {
                    throw new InvalidOperationException(
                        "The requested sheet size would clip node '" +
                        (node.Title ?? node.Id.ToString()) + "'.");
                }
            }

            foreach (var group in sheet.Groups ?? new List<DiagramGroup>())
            {
                if (group == null)
                {
                    continue;
                }

                if (!FitsInsideSheet(
                    group.X,
                    group.Y,
                    group.Width,
                    group.Height,
                    width,
                    height))
                {
                    throw new InvalidOperationException(
                        "The requested sheet size would clip group '" +
                        (group.Title ?? group.Id.ToString()) + "'.");
                }
            }
        }

        private static bool FitsInsideSheet(
            double x,
            double y,
            double itemWidth,
            double itemHeight,
            double sheetWidth,
            double sheetHeight)
        {
            return IsFinite(x) &&
                IsFinite(y) &&
                IsFinite(itemWidth) &&
                IsFinite(itemHeight) &&
                x >= 0.0 &&
                y >= 0.0 &&
                itemWidth >= 0.0 &&
                itemHeight >= 0.0 &&
                x <= sheetWidth &&
                y <= sheetHeight &&
                itemWidth <= sheetWidth - x &&
                itemHeight <= sheetHeight - y;
        }

        internal static double GetNodeWidth(CallNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }

            if (node is BlockCallNode)
            {
                return 272.0;
            }

            if (node is ConstantNode)
            {
                return 128.0;
            }

            if (node is TagNode)
            {
                return 154.0;
            }

            if (node is DataBlockVariableNode)
            {
                return 210.0;
            }

            if (node is LogicNode)
            {
                return 224.0;
            }

            if (node is NoteNode)
            {
                return 238.0;
            }

            return 220.0;
        }

        internal static double GetNodeHeight(CallNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }

            var pins = node.Pins ?? new List<Pin>();
            if (node is BlockCallNode)
            {
                var inputCount = pins.Count(pin =>
                    pin != null && pin.Direction == PinDirection.Input);
                var outputCount = pins.Count(pin =>
                    pin != null && pin.Direction == PinDirection.Output);
                return 54.0 + 18.0 + Math.Max(inputCount, outputCount) * 32.0;
            }

            if (node is LogicNode)
            {
                var inputCount = pins.Count(pin =>
                    pin != null && pin.Direction == PinDirection.Input);
                var outputCount = pins.Count(pin =>
                    pin != null && pin.Direction == PinDirection.Output);
                return 42.0 + 15.0 + Math.Max(inputCount, outputCount) * 32.0;
            }

            return node is NoteNode ? 112.0 : 44.0;
        }

        internal static double GrowExtentToFit(double current, double required)
        {
            if (!IsFinite(current) ||
                !IsFinite(required) ||
                current <= 0.0 ||
                current > MaximumSheetExtent ||
                required < 0.0 ||
                required > MaximumSheetExtent)
            {
                throw new InvalidOperationException(
                    "The diagram extent is invalid or exceeds the supported safe limit.");
            }

            if (required <= current)
            {
                return current;
            }

            const double growthStep = 256.0;
            return Math.Min(
                MaximumSheetExtent,
                Math.Ceiling(required / growthStep) * growthStep);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class SymbolEntry
        {
            internal SymbolEntry(string name, string owner)
            {
                Name = name ?? string.Empty;
                Owner = owner ?? string.Empty;
            }

            internal string Name { get; private set; }

            internal string Owner { get; private set; }
        }
    }

    internal sealed class DenseNodePlacementResult
    {
        internal DenseNodePlacementResult(
            double x,
            double y,
            double sheetWidth,
            double sheetHeight)
        {
            X = x;
            Y = y;
            SheetWidth = sheetWidth;
            SheetHeight = sheetHeight;
        }

        internal double X { get; private set; }

        internal double Y { get; private set; }

        internal double SheetWidth { get; private set; }

        internal double SheetHeight { get; private set; }
    }

    internal static class DenseNodePlacementLogic
    {
        internal const double DefaultSheetWidth = SheetEditingLogic.DefaultSheetWidth;
        internal const double DefaultSheetHeight = SheetEditingLogic.DefaultSheetHeight;
        internal const double MaximumSheetExtent = SheetEditingLogic.MaximumSheetExtent;
        internal const double NodeMargin = 24.0;
        internal const double NodeGap = 28.0;

        private const int MaximumSearchRings = 128;

        internal static DenseNodePlacementResult FindNearestFreePlacement(
            double requestedX,
            double requestedY,
            double nodeWidth,
            double nodeHeight,
            IEnumerable<Rect> occupiedBounds,
            double sheetWidth,
            double sheetHeight)
        {
            if (!IsFinitePositive(nodeWidth))
            {
                throw new ArgumentOutOfRangeException(
                    "nodeWidth",
                    "Node dimensions must be finite and positive.");
            }

            if (!IsFinitePositive(nodeHeight))
            {
                throw new ArgumentOutOfRangeException(
                    "nodeHeight",
                    "Node dimensions must be finite and positive.");
            }

            if (nodeWidth > MaximumSheetExtent - NodeMargin * 2.0 ||
                nodeHeight > MaximumSheetExtent - NodeMargin * 2.0)
            {
                throw new InvalidOperationException(
                    "The node is too large for the maximum diagram extent.");
            }

            var currentWidth = NormalizeExtent(sheetWidth, DefaultSheetWidth);
            var currentHeight = NormalizeExtent(sheetHeight, DefaultSheetHeight);
            var originX = Clamp(
                IsFinite(requestedX) ? requestedX : NodeMargin,
                NodeMargin,
                MaximumSheetExtent - nodeWidth - NodeMargin);
            var originY = Clamp(
                IsFinite(requestedY) ? requestedY : NodeMargin,
                NodeMargin,
                MaximumSheetExtent - nodeHeight - NodeMargin);
            var occupied = (occupiedBounds ?? Enumerable.Empty<Rect>())
                .Where(IsUsableBounds)
                .ToList();
            var stepX = nodeWidth + NodeGap;
            var stepY = nodeHeight + NodeGap;

            for (var ring = 0; ring <= MaximumSearchRings; ring++)
            {
                DenseNodePlacementResult best = null;
                var bestDistance = double.MaxValue;
                var bestGrowth = double.MaxValue;
                for (var row = -ring; row <= ring; row++)
                {
                    for (var column = -ring; column <= ring; column++)
                    {
                        if (Math.Max(Math.Abs(column), Math.Abs(row)) != ring)
                        {
                            continue;
                        }

                        var x = originX + column * stepX;
                        var y = originY + row * stepY;
                        if (!CanFitInsideMaximumExtent(x, y, nodeWidth, nodeHeight))
                        {
                            continue;
                        }

                        var candidate = new Rect(x, y, nodeWidth, nodeHeight);
                        if (occupied.Any(item => OverlapsWithGap(candidate, item, NodeGap)))
                        {
                            continue;
                        }

                        var requiredWidth = candidate.Right + NodeMargin;
                        var requiredHeight = candidate.Bottom + NodeMargin;
                        var grownWidth = GrowExtent(currentWidth, requiredWidth);
                        var grownHeight = GrowExtent(currentHeight, requiredHeight);
                        var distance = column * column + row * row;
                        var growth = grownWidth - currentWidth + grownHeight - currentHeight;
                        if (best == null ||
                            distance < bestDistance ||
                            (Math.Abs(distance - bestDistance) < 0.000001 && growth < bestGrowth) ||
                            (Math.Abs(distance - bestDistance) < 0.000001 &&
                             Math.Abs(growth - bestGrowth) < 0.000001 &&
                             IsBefore(x, y, best.X, best.Y)))
                        {
                            best = new DenseNodePlacementResult(
                                x,
                                y,
                                grownWidth,
                                grownHeight);
                            bestDistance = distance;
                            bestGrowth = growth;
                        }
                    }
                }

                if (best != null)
                {
                    return best;
                }
            }

            throw new InvalidOperationException(
                "No free node position was found within the maximum diagram extent.");
        }

        internal static bool OverlapsWithGap(Rect first, Rect second, double gap)
        {
            if (!IsUsableBounds(first) || !IsUsableBounds(second))
            {
                return false;
            }

            var safeGap = IsFinite(gap) && gap > 0.0 ? gap : 0.0;
            return first.Left < second.Right + safeGap &&
                first.Right + safeGap > second.Left &&
                first.Top < second.Bottom + safeGap &&
                first.Bottom + safeGap > second.Top;
        }

        private static bool CanFitInsideMaximumExtent(
            double x,
            double y,
            double width,
            double height)
        {
            return IsFinite(x) &&
                IsFinite(y) &&
                x >= NodeMargin &&
                y >= NodeMargin &&
                x + width + NodeMargin <= MaximumSheetExtent &&
                y + height + NodeMargin <= MaximumSheetExtent;
        }

        private static double NormalizeExtent(double value, double fallback)
        {
            if (!IsFinitePositive(value))
            {
                return fallback;
            }

            if (value > MaximumSheetExtent)
            {
                throw new InvalidOperationException(
                    "The existing diagram extent exceeds the supported safe limit.");
            }

            return Math.Max(value, fallback);
        }

        private static double GrowExtent(double current, double required)
        {
            return SheetEditingLogic.GrowExtentToFit(current, required);
        }

        private static bool IsBefore(
            double x,
            double y,
            double otherX,
            double otherY)
        {
            return y < otherY ||
                (Math.Abs(y - otherY) < 0.000001 && x < otherX);
        }

        private static bool IsUsableBounds(Rect bounds)
        {
            return !bounds.IsEmpty &&
                IsFinite(bounds.X) &&
                IsFinite(bounds.Y) &&
                IsFinitePositive(bounds.Width) &&
                IsFinitePositive(bounds.Height);
        }

        private static bool IsFinitePositive(double value)
        {
            return IsFinite(value) && value > 0.0;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
