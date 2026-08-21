using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using TiaSclStudio.Core.Model;

namespace TiaSclStudio.Core.Validation
{
    /// <summary>
    /// A parsed direct PLC address. ByteOffset is always the byte at which the
    /// operand starts; BitOffset is only present for Bool operands.
    /// </summary>
    public sealed class PlcAddressSpec
    {
        public PlcAddressSpec(
            PlcCpuFamily cpuFamily,
            PlcSignalKind signalKind,
            string dataType,
            int byteOffset,
            int? bitOffset)
        {
            CpuFamily = cpuFamily;
            SignalKind = signalKind;
            DataType = dataType ?? string.Empty;
            ByteOffset = byteOffset;
            BitOffset = bitOffset;
        }

        public PlcCpuFamily CpuFamily { get; private set; }

        public PlcSignalKind SignalKind { get; private set; }

        public string DataType { get; private set; }

        public int ByteOffset { get; private set; }

        public int? BitOffset { get; private set; }
    }

    /// <summary>
    /// Builds and validates the direct I/Q/M operands used by the visual editor.
    /// The limits are the process-image / marker limits of the selected CPU
    /// family. Online hardware discovery may further restrict I/O addresses to
    /// channels that actually exist in a particular device configuration.
    /// </summary>
    public static class PlcAddressing
    {
        private static readonly Regex AddressPattern = new Regex(
            "^%?(IW|QW|MW|ID|QD|MD|I|Q|M)([0-9]+)(?:\\.([0-9]+))?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly string[] BoolOnly = { "Bool" };
        private static readonly string[] AnalogTypes = { "Int", "Word", "Real" };
        private static readonly string[] MemoryTypes = { "Bool", "Int", "Word", "Real" };

        public static IList<string> GetAllowedDataTypes(PlcSignalKind signalKind)
        {
            switch (signalKind)
            {
                case PlcSignalKind.DigitalInput:
                case PlcSignalKind.DigitalOutput:
                    return new List<string>(BoolOnly);
                case PlcSignalKind.AnalogInput:
                case PlcSignalKind.AnalogOutput:
                    return new List<string>(AnalogTypes);
                case PlcSignalKind.Memory:
                    return new List<string>(MemoryTypes);
                default:
                    throw new ArgumentOutOfRangeException("signalKind");
            }
        }

        public static int GetMaximumByteOffset(
            PlcCpuFamily cpuFamily,
            PlcSignalKind signalKind,
            string dataType)
        {
            EnsureDefined(cpuFamily, signalKind);

            if (cpuFamily == PlcCpuFamily.Unknown)
            {
                throw new ArgumentException("A concrete CPU family is required to calculate an address range.", "cpuFamily");
            }

            string normalizedType;
            if (!TryNormalizeAddressDataType(signalKind, dataType, out normalizedType))
            {
                throw new ArgumentException("The data type is not valid for the selected signal kind.", "dataType");
            }

            var byteCapacity = signalKind == PlcSignalKind.Memory
                ? (cpuFamily == PlcCpuFamily.S71200 ? 8192 : 16384)
                : (cpuFamily == PlcCpuFamily.S71200 ? 1024 : 32768);
            return byteCapacity - WidthInBytes(normalizedType);
        }

        public static bool TryBuild(
            PlcCpuFamily cpuFamily,
            PlcSignalKind signalKind,
            string dataType,
            int byteOffset,
            int? bitOffset,
            out string canonicalAddress,
            out string error)
        {
            return TryBuildCore(
                cpuFamily,
                signalKind,
                dataType,
                byteOffset,
                bitOffset,
                false,
                out canonicalAddress,
                out error);
        }

        private static bool TryBuildCore(
            PlcCpuFamily cpuFamily,
            PlcSignalKind signalKind,
            string dataType,
            int byteOffset,
            int? bitOffset,
            bool allowUnknownCpu,
            out string canonicalAddress,
            out string error)
        {
            canonicalAddress = string.Empty;
            error = string.Empty;

            if (!Enum.IsDefined(typeof(PlcCpuFamily), cpuFamily))
            {
                error = "Выберите поддерживаемое семейство CPU.";
                return false;
            }

            if (cpuFamily == PlcCpuFamily.Unknown && !allowUnknownCpu)
            {
                error = "Выберите S7-1200 или S7-1500.";
                return false;
            }

            if (!Enum.IsDefined(typeof(PlcSignalKind), signalKind))
            {
                error = "Выберите тип сигнала DI, DO, AI, AO или M.";
                return false;
            }

            string normalizedType;
            var dataTypeIsValid = allowUnknownCpu
                ? TryNormalizeAddressDataType(signalKind, dataType, out normalizedType)
                : TryNormalizeDataType(signalKind, dataType, out normalizedType);
            if (!dataTypeIsValid)
            {
                error = "Тип данных не подходит для выбранного сигнала.";
                return false;
            }

            if (byteOffset < 0)
            {
                error = "Смещение байта не может быть отрицательным.";
                return false;
            }

            var maximum = cpuFamily == PlcCpuFamily.Unknown
                ? int.MaxValue
                : GetMaximumByteOffset(cpuFamily, signalKind, normalizedType);
            if (byteOffset > maximum)
            {
                error = "Смещение выходит за диапазон выбранной CPU: допустимо 0.." +
                        maximum.ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }

            var isBit = string.Equals(normalizedType, "Bool", StringComparison.Ordinal);
            if (isBit)
            {
                if (!bitOffset.HasValue || bitOffset.Value < 0 || bitOffset.Value > 7)
                {
                    error = "Для Bool укажите номер бита от 0 до 7.";
                    return false;
                }
            }
            else if (bitOffset.HasValue)
            {
                error = "Номер бита задаётся только для Bool.";
                return false;
            }

            canonicalAddress = Prefix(signalKind, normalizedType) +
                               byteOffset.ToString(CultureInfo.InvariantCulture);
            if (isBit)
            {
                canonicalAddress += "." + bitOffset.Value.ToString(CultureInfo.InvariantCulture);
            }

            return true;
        }

        public static bool TryParse(
            PlcCpuFamily cpuFamily,
            string address,
            string dataType,
            out PlcAddressSpec spec,
            out string canonicalAddress,
            out string error)
        {
            spec = null;
            canonicalAddress = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(address))
            {
                error = "Укажите адрес PLC-тега.";
                return false;
            }

            var match = AddressPattern.Match(address.Trim());
            if (!match.Success)
            {
                error = "Адрес должен иметь вид %I0.0, %Q0.0, %IW64, %QW64, %ID64, %QD64, %M0.0, %MW10 или %MD10.";
                return false;
            }

            var operand = match.Groups[1].Value.ToUpperInvariant();
            PlcSignalKind signalKind;
            string addressType;
            if (!TryResolveOperand(operand, dataType, out signalKind, out addressType, out error))
            {
                return false;
            }

            int byteOffset;
            if (!int.TryParse(
                match.Groups[2].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out byteOffset))
            {
                error = "Смещение байта имеет неверный формат.";
                return false;
            }

            int? bitOffset = null;
            if (match.Groups[3].Success)
            {
                int parsedBit;
                if (!int.TryParse(
                    match.Groups[3].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out parsedBit))
                {
                    error = "Номер бита имеет неверный формат.";
                    return false;
                }

                bitOffset = parsedBit;
            }

            if (!TryBuildCore(
                cpuFamily,
                signalKind,
                addressType,
                byteOffset,
                bitOffset,
                true,
                out canonicalAddress,
                out error))
            {
                return false;
            }

            spec = new PlcAddressSpec(
                cpuFamily,
                signalKind,
                addressType,
                byteOffset,
                bitOffset);
            return true;
        }

        private static bool TryResolveOperand(
            string operand,
            string dataType,
            out PlcSignalKind signalKind,
            out string normalizedType,
            out string error)
        {
            signalKind = default(PlcSignalKind);
            normalizedType = string.Empty;
            error = string.Empty;

            switch (operand)
            {
                case "I":
                    signalKind = PlcSignalKind.DigitalInput;
                    break;
                case "Q":
                    signalKind = PlcSignalKind.DigitalOutput;
                    break;
                case "IW":
                case "ID":
                    signalKind = PlcSignalKind.AnalogInput;
                    break;
                case "QW":
                case "QD":
                    signalKind = PlcSignalKind.AnalogOutput;
                    break;
                case "M":
                case "MW":
                case "MD":
                    signalKind = PlcSignalKind.Memory;
                    break;
                default:
                    error = "Область адреса не поддерживается.";
                    return false;
            }

            if (!TryNormalizeAddressDataType(signalKind, dataType, out normalizedType))
            {
                error = "Тип данных не подходит для области адреса.";
                return false;
            }

            var expectedOperand = Prefix(signalKind, normalizedType).Substring(1);
            if (!string.Equals(operand, expectedOperand, StringComparison.Ordinal))
            {
                error = "Адрес " + operand + " не соответствует типу данных " + normalizedType + ".";
                return false;
            }

            return true;
        }

        private static bool TryNormalizeDataType(
            PlcSignalKind signalKind,
            string dataType,
            out string normalizedType)
        {
            normalizedType = string.Empty;
            var candidate = (dataType ?? string.Empty).Trim();
            foreach (var allowed in GetAllowedDataTypes(signalKind))
            {
                if (string.Equals(candidate, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    normalizedType = allowed;
                    return true;
                }
            }

            return false;
        }

        private static bool TryNormalizeAddressDataType(
            PlcSignalKind signalKind,
            string dataType,
            out string normalizedType)
        {
            if (TryNormalizeDataType(signalKind, dataType, out normalizedType))
            {
                return true;
            }

            var candidate = (dataType ?? string.Empty).Trim();
            if ((signalKind == PlcSignalKind.AnalogInput ||
                 signalKind == PlcSignalKind.AnalogOutput ||
                 signalKind == PlcSignalKind.Memory) &&
                (string.Equals(candidate, "DInt", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate, "DWord", StringComparison.OrdinalIgnoreCase)))
            {
                normalizedType = string.Equals(candidate, "DInt", StringComparison.OrdinalIgnoreCase)
                    ? "DInt"
                    : "DWord";
                return true;
            }

            return false;
        }

        private static string Prefix(PlcSignalKind signalKind, string dataType)
        {
            switch (signalKind)
            {
                case PlcSignalKind.DigitalInput:
                    return "%I";
                case PlcSignalKind.DigitalOutput:
                    return "%Q";
                case PlcSignalKind.AnalogInput:
                    return IsDoubleWordType(dataType) ? "%ID" : "%IW";
                case PlcSignalKind.AnalogOutput:
                    return IsDoubleWordType(dataType) ? "%QD" : "%QW";
                case PlcSignalKind.Memory:
                    if (string.Equals(dataType, "Bool", StringComparison.Ordinal))
                    {
                        return "%M";
                    }

                    return IsDoubleWordType(dataType) ? "%MD" : "%MW";
                default:
                    throw new ArgumentOutOfRangeException("signalKind");
            }
        }

        private static int WidthInBytes(string dataType)
        {
            if (IsDoubleWordType(dataType))
            {
                return 4;
            }

            if (string.Equals(dataType, "Int", StringComparison.Ordinal) ||
                string.Equals(dataType, "Word", StringComparison.Ordinal))
            {
                return 2;
            }

            return 1;
        }

        private static bool IsDoubleWordType(string dataType)
        {
            return string.Equals(dataType, "Real", StringComparison.Ordinal) ||
                   string.Equals(dataType, "DInt", StringComparison.Ordinal) ||
                   string.Equals(dataType, "DWord", StringComparison.Ordinal);
        }

        private static void EnsureDefined(PlcCpuFamily cpuFamily, PlcSignalKind signalKind)
        {
            if (!Enum.IsDefined(typeof(PlcCpuFamily), cpuFamily))
            {
                throw new ArgumentOutOfRangeException("cpuFamily");
            }

            if (!Enum.IsDefined(typeof(PlcSignalKind), signalKind))
            {
                throw new ArgumentOutOfRangeException("signalKind");
            }
        }
    }
}
