using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Openness.Gateway;

namespace TiaSclStudio.App
{
    /// <summary>
    /// Version-neutral, read-only projection of a TIA hardware snapshot into the
    /// address picker. Online I/O is fail-closed; marker memory remains manual.
    /// </summary>
    internal sealed class HardwareIoSelectionContext
    {
        private readonly IList<PlcAddressChannelChoice> choices;

        internal HardwareIoSelectionContext(
            PlcCpuFamily? fixedCpuFamily,
            IEnumerable<PlcAddressChannelChoice> choices,
            string unavailableReason)
        {
            FixedCpuFamily = fixedCpuFamily;
            this.choices = (choices ?? Enumerable.Empty<PlcAddressChannelChoice>()).ToList();
            Choices = this.choices.ToList().AsReadOnly();
            UnavailableReason = unavailableReason ?? string.Empty;
        }

        internal PlcCpuFamily? FixedCpuFamily { get; private set; }

        internal IReadOnlyList<PlcAddressChannelChoice> Choices { get; private set; }

        internal string UnavailableReason { get; private set; }

        internal bool TryValidateTag(
            string address,
            string dataType,
            out string error)
        {
            if (!FixedCpuFamily.HasValue)
            {
                error = string.IsNullOrWhiteSpace(UnavailableReason)
                    ? "Семейство CPU или аппаратные каналы PLC не удалось прочитать из TIA."
                    : UnavailableReason;
                return false;
            }

            PlcAddressSpec spec;
            string canonicalAddress;
            string parseError;
            if (!PlcAddressing.TryParse(
                FixedCpuFamily.Value,
                address,
                dataType,
                out spec,
                out canonicalAddress,
                out parseError))
            {
                error = parseError;
                return false;
            }

            if (spec.SignalKind == PlcSignalKind.Memory)
            {
                error = string.Empty;
                return true;
            }

            var match = choices.Any(choice =>
                string.Equals(choice.Address, canonicalAddress, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(choice.DataType, spec.DataType, StringComparison.OrdinalIgnoreCase));
            if (!match)
            {
                error = "Адрес " + canonicalAddress + " с типом " + spec.DataType +
                        " отсутствует в текущей аппаратной конфигурации PLC.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        internal bool TryValidateCreationResult(
            DiagramProject project,
            NodeCreationResult result,
            out string error)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            if (result.Kind != CallNodeKind.Tag)
            {
                error = "Операция не содержит PLC-тег.";
                return false;
            }

            if (FixedCpuFamily.HasValue &&
                result.TargetCpuFamily.HasValue &&
                FixedCpuFamily.Value != result.TargetCpuFamily.Value)
            {
                error = "Конфигурация CPU изменилась после открытия окна. " +
                        "Обновите каталог и выберите канал повторно.";
                return false;
            }

            if (result.CreateTagDefinition)
            {
                return TryValidateTag(result.TagAddress, result.DataType, out error);
            }

            var tags = ((project.Plant ?? new PlantProject()).Tags ?? new List<TagDefinition>())
                .Where(tag => tag != null && tag.Id == result.TagDefinitionId)
                .ToList();
            if (tags.Count != 1)
            {
                error = tags.Count == 0
                    ? "Выбранный PLC-тег больше не найден в проекте."
                    : "Идентификатор выбранного PLC-тега не уникален.";
                return false;
            }

            return TryValidateTag(tags[0].Address, tags[0].DataType, out error);
        }

        internal bool TryValidateEditResult(
            NodeEditResult result,
            out string error)
        {
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            if (result.Kind != CallNodeKind.Tag)
            {
                error = "Операция не содержит PLC-тег.";
                return false;
            }

            if (FixedCpuFamily.HasValue &&
                result.TargetCpuFamily.HasValue &&
                FixedCpuFamily.Value != result.TargetCpuFamily.Value)
            {
                error = "Конфигурация CPU изменилась после открытия окна. " +
                        "Обновите каталог и выберите канал повторно.";
                return false;
            }

            return TryValidateTag(result.TagAddress, result.DataType, out error);
        }

        internal bool IsStoredTagSelectable(TagDefinition tag)
        {
            string error;
            return tag != null && TryValidateTag(tag.Address, tag.DataType, out error);
        }
    }

    internal static class HardwareIoSelectionPolicy
    {
        internal static HardwareIoSelectionContext FromOnlineCatalog(
            TiaHardwareIoCatalog catalog)
        {
            PlcCpuFamily? cpuFamily = null;
            if (catalog != null && catalog.IsAvailable)
            {
                cpuFamily = MapCpuFamily(catalog.CpuFamily);
            }

            if (!cpuFamily.HasValue)
            {
                return new HardwareIoSelectionContext(
                    null,
                    Enumerable.Empty<PlcAddressChannelChoice>(),
                    "TIA не вернула распознанное семейство CPU и точный каталог I/O. " +
                    "Создание и редактирование PLC-тегов заблокировано до обновления подключения.");
            }

            var choices = new List<PlcAddressChannelChoice>();
            foreach (var channel in catalog.Channels.Where(item => item != null))
            {
                PlcSignalKind signalKind;
                if (!TryMapSignalKind(channel.Kind, out signalKind))
                {
                    continue;
                }

                foreach (var suggestedType in channel.SuggestedDataTypes ?? new string[0])
                {
                    var dataType = PlcAddressing.GetAllowedDataTypes(signalKind)
                        .FirstOrDefault(value => string.Equals(
                            value,
                            suggestedType,
                            StringComparison.OrdinalIgnoreCase));
                    if (dataType == null)
                    {
                        continue;
                    }

                    PlcAddressSpec spec;
                    string canonicalAddress;
                    string error;
                    if (!PlcAddressing.TryParse(
                        cpuFamily.Value,
                        channel.LogicalAddress,
                        dataType,
                        out spec,
                        out canonicalAddress,
                        out error))
                    {
                        continue;
                    }

                    choices.Add(new PlcAddressChannelChoice(
                        BuildDisplayName(channel),
                        signalKind,
                        dataType,
                        canonicalAddress));
                }
            }

            var distinctChoices = choices
                .GroupBy(choice => new
                {
                    choice.SignalKind,
                    Address = choice.Address.ToUpperInvariant(),
                    DataType = choice.DataType.ToUpperInvariant()
                })
                .Select(group => group.First())
                .OrderBy(choice => choice.SignalKind)
                .ThenBy(choice => choice.Address, StringComparer.OrdinalIgnoreCase)
                .ThenBy(choice => choice.DataType, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new HardwareIoSelectionContext(
                cpuFamily,
                distinctChoices,
                distinctChoices.Count == 0
                    ? "В аппаратной конфигурации выбранной CPU нет поддерживаемых каналов I/O."
                    : string.Empty);
        }

        private static PlcCpuFamily? MapCpuFamily(TiaCpuFamily cpuFamily)
        {
            switch (cpuFamily)
            {
                case TiaCpuFamily.S71200:
                    return PlcCpuFamily.S71200;
                case TiaCpuFamily.S71500:
                    return PlcCpuFamily.S71500;
                default:
                    return null;
            }
        }

        private static bool TryMapSignalKind(
            TiaIoChannelKind channelKind,
            out PlcSignalKind signalKind)
        {
            switch (channelKind)
            {
                case TiaIoChannelKind.DigitalInput:
                    signalKind = PlcSignalKind.DigitalInput;
                    return true;
                case TiaIoChannelKind.DigitalOutput:
                    signalKind = PlcSignalKind.DigitalOutput;
                    return true;
                case TiaIoChannelKind.AnalogInput:
                    signalKind = PlcSignalKind.AnalogInput;
                    return true;
                case TiaIoChannelKind.AnalogOutput:
                    signalKind = PlcSignalKind.AnalogOutput;
                    return true;
                default:
                    signalKind = default(PlcSignalKind);
                    return false;
            }
        }

        private static string BuildDisplayName(TiaHardwareIoChannel channel)
        {
            var owner = !string.IsNullOrWhiteSpace(channel.DeviceItemPath)
                ? channel.DeviceItemPath.Trim()
                : (!string.IsNullOrWhiteSpace(channel.DeviceItemTypeName)
                    ? channel.DeviceItemTypeName.Trim()
                    : "PLC I/O");
            return owner + " · канал " + channel.Number;
        }
    }
}
