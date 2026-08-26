using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TiaSclStudio.Core.Model;

namespace TiaSclStudio.Core.Validation
{
    /// <summary>
    /// Canonical TIA/SCL data-type catalogue shared by editors, validation and import.
    /// User-defined types are stored as quoted global identifiers so generated external
    /// sources remain unambiguous. Legacy unquoted references are still accepted.
    /// </summary>
    public static class PlcDataTypes
    {
        private static readonly string[] BuiltInTypeValues =
        {
            "Bool",
            "Byte",
            "Word",
            "DWord",
            "LWord",
            "SInt",
            "USInt",
            "Int",
            "UInt",
            "DInt",
            "UDInt",
            "LInt",
            "ULInt",
            "Real",
            "LReal",
            "S5Time",
            "Time",
            "LTime",
            "Date",
            "LDate",
            "Time_Of_Day",
            "LTime_Of_Day",
            "Date_And_Time",
            "DTL",
            "Char",
            "WChar",
            "String",
            "WString"
        };

        private static readonly IDictionary<string, string> BuiltIns =
            BuildBuiltInLookup();

        private static readonly IDictionary<string, string> SystemBlockInstanceTypes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "TON_TIME", "TON_TIME" },
                { "TOF_TIME", "TOF_TIME" },
                { "TP_TIME", "TP_TIME" },
                { "TONR_TIME", "TONR_TIME" }
            };

        private static readonly Regex SizedStringPattern = new Regex(
            "^(String|WString)\\s*\\[\\s*([0-9]+)\\s*\\]$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            SclRegex.MatchTimeout);

        private static readonly Regex ArrayPattern = new Regex(
            "^Array\\s*\\[\\s*(.+?)\\s*\\]\\s*of\\s+(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            SclRegex.MatchTimeout);

        private static readonly Regex ArrayBoundPattern = new Regex(
            "^([+-]?[0-9]+)\\s*\\.\\.\\s*([+-]?[0-9]+)$",
            RegexOptions.CultureInvariant,
            SclRegex.MatchTimeout);

        private static readonly IReadOnlyList<string> ReadOnlyBuiltIns =
            new ReadOnlyCollection<string>(BuiltInTypeValues);

        public static IReadOnlyList<string> BuiltInTypes
        {
            get { return ReadOnlyBuiltIns; }
        }

        /// <summary>
        /// Returns values suitable for a non-editable editor ComboBox. UDTs use the
        /// exact canonical representation that must be stored in the model.
        /// </summary>
        public static IList<string> GetSelectableTypes(
            IEnumerable<UdtDefinition> dataTypes,
            bool includeVoid)
        {
            var choices = new List<string>();
            if (includeVoid)
            {
                choices.Add("Void");
            }

            choices.AddRange(BuiltInTypeValues);
            choices.AddRange(Safe(dataTypes)
                .Where(item => SclName.IsValid(item.Name))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => QuoteUdtName(item.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            return choices;
        }

        /// <summary>
        /// Resolves a type expression and returns its safe canonical spelling.
        /// Supported compound forms are bounded String/WString and recursively
        /// resolved Array declarations. Everything else fails closed.
        /// </summary>
        public static bool TryResolve(
            string dataType,
            IEnumerable<UdtDefinition> dataTypes,
            bool allowVoid,
            out string canonicalType,
            out Guid udtId)
        {
            canonicalType = string.Empty;
            udtId = Guid.Empty;
            if (string.IsNullOrWhiteSpace(dataType) || SclText.HasControlCharacters(dataType))
            {
                return false;
            }

            var candidate = dataType.Trim();
            if (allowVoid && string.Equals(candidate, "Void", StringComparison.OrdinalIgnoreCase))
            {
                canonicalType = "Void";
                return true;
            }

            string builtIn;
            if (BuiltIns.TryGetValue(candidate, out builtIn))
            {
                canonicalType = builtIn;
                return true;
            }

            var sizedString = SizedStringPattern.Match(candidate);
            if (sizedString.Success)
            {
                int length;
                if (!int.TryParse(
                        sizedString.Groups[2].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out length))
                {
                    return false;
                }

                var isWide = string.Equals(
                    sizedString.Groups[1].Value,
                    "WString",
                    StringComparison.OrdinalIgnoreCase);
                var maximumLength = isWide ? 16382 : 254;
                if (length < 1 || length > maximumLength)
                {
                    return false;
                }

                canonicalType = (isWide ? "WString" : "String") +
                    "[" + length.ToString(CultureInfo.InvariantCulture) + "]";
                return true;
            }

            var array = ArrayPattern.Match(candidate);
            if (array.Success)
            {
                string canonicalBounds;
                if (!TryNormalizeArrayBounds(array.Groups[1].Value, out canonicalBounds))
                {
                    return false;
                }

                string canonicalElement;
                if (!TryResolve(
                        array.Groups[2].Value,
                        dataTypes,
                        false,
                        out canonicalElement,
                        out udtId))
                {
                    return false;
                }

                canonicalType = "Array[" + canonicalBounds + "] of " + canonicalElement;
                return true;
            }

            var unquoted = UnquoteIdentifier(candidate);
            var matchingUdts = Safe(dataTypes)
                .Where(item => string.Equals(
                    item.Name,
                    unquoted,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matchingUdts.Count != 1 || !SclName.IsValid(matchingUdts[0].Name))
            {
                return false;
            }

            canonicalType = QuoteUdtName(matchingUdts[0].Name);
            udtId = matchingUdts[0].Id;
            return true;
        }

        public static bool TryGetReferencedUdt(
            string dataType,
            IEnumerable<UdtDefinition> dataTypes,
            out UdtDefinition referencedUdt)
        {
            referencedUdt = null;
            var udts = Safe(dataTypes);
            string canonical;
            Guid udtId;
            if (!TryResolve(dataType, udts, false, out canonical, out udtId) ||
                udtId == Guid.Empty)
            {
                return false;
            }

            referencedUdt = udts.FirstOrDefault(item => item.Id == udtId);
            return referencedUdt != null;
        }

        /// <summary>
        /// Resolves Siemens system FB instance types found in generated block
        /// interfaces. These are intentionally excluded from BuiltInTypes and
        /// general TryResolve because they are not valid scalar tag/UDT types.
        /// </summary>
        public static bool TryResolveSystemBlockInstanceType(
            string dataType,
            out string canonicalType)
        {
            canonicalType = string.Empty;
            if (string.IsNullOrWhiteSpace(dataType) || SclText.HasControlCharacters(dataType))
            {
                return false;
            }

            return SystemBlockInstanceTypes.TryGetValue(dataType.Trim(), out canonicalType);
        }

        public static string QuoteUdtName(string name)
        {
            if (!SclName.IsValid(name))
            {
                throw new ArgumentException("A UDT name must be a valid SCL identifier.", "name");
            }

            return "\"" + name + "\"";
        }

        public static bool ReferencesUdt(string dataType, string udtName)
        {
            if (!SclName.IsValid(udtName) || string.IsNullOrWhiteSpace(dataType))
            {
                return false;
            }

            var candidate = dataType.Trim();
            var array = ArrayPattern.Match(candidate);
            if (array.Success)
            {
                return ReferencesUdt(array.Groups[2].Value, udtName);
            }

            return string.Equals(
                UnquoteIdentifier(candidate),
                udtName,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Rewrites a direct or Array element UDT reference to canonical quoted form.</summary>
        public static string RewriteUdtReference(
            string dataType,
            string oldName,
            string newName)
        {
            if (!ReferencesUdt(dataType, oldName))
            {
                return dataType ?? string.Empty;
            }

            if (!SclName.IsValid(newName))
            {
                throw new ArgumentException("A UDT name must be a valid SCL identifier.", "newName");
            }

            var candidate = (dataType ?? string.Empty).Trim();
            var array = ArrayPattern.Match(candidate);
            if (!array.Success)
            {
                return QuoteUdtName(newName);
            }

            string canonicalBounds;
            if (!TryNormalizeArrayBounds(array.Groups[1].Value, out canonicalBounds))
            {
                return dataType ?? string.Empty;
            }

            return "Array[" + canonicalBounds + "] of " +
                RewriteUdtReference(array.Groups[2].Value, oldName, newName);
        }

        /// <summary>
        /// Orders UDTs so every referenced UDT precedes its consumer. A malformed,
        /// unresolved or cyclic dependency throws; ProjectValidator exposes those
        /// conditions as regular diagnostics before whole-project generation.
        /// </summary>
        public static IList<UdtDefinition> OrderUdtsByDependency(
            IEnumerable<UdtDefinition> dataTypes)
        {
            if (dataTypes == null)
            {
                throw new ArgumentNullException("dataTypes");
            }

            var udts = Safe(dataTypes);
            var originalIndex = udts
                .Select((item, index) => new { item.Id, Index = index })
                .GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => group.First().Index);
            var dependencies = new Dictionary<Guid, HashSet<Guid>>();
            foreach (var udt in udts)
            {
                var current = new HashSet<Guid>();
                foreach (var member in Safe(udt.Members))
                {
                    string canonical;
                    Guid referencedId;
                    if (!TryResolve(member.DataType, udts, false, out canonical, out referencedId))
                    {
                        throw new InvalidOperationException(
                            "UDT '" + udt.Name + "' contains an unresolved data type '" +
                            member.DataType + "'.");
                    }

                    if (referencedId != Guid.Empty)
                    {
                        current.Add(referencedId);
                    }
                }

                dependencies[udt.Id] = current;
            }

            var ordered = new List<UdtDefinition>(udts.Count);
            var state = new Dictionary<Guid, int>();
            foreach (var udt in udts)
            {
                Visit(udt, udts, dependencies, originalIndex, state, ordered);
            }

            return ordered;
        }

        private static void Visit(
            UdtDefinition udt,
            IList<UdtDefinition> udts,
            IDictionary<Guid, HashSet<Guid>> dependencies,
            IDictionary<Guid, int> originalIndex,
            IDictionary<Guid, int> state,
            IList<UdtDefinition> ordered)
        {
            int currentState;
            if (state.TryGetValue(udt.Id, out currentState))
            {
                if (currentState == 1)
                {
                    throw new InvalidOperationException(
                        "A cyclic UDT dependency includes '" + udt.Name + "'.");
                }

                return;
            }

            state[udt.Id] = 1;
            HashSet<Guid> referencedIds;
            if (dependencies.TryGetValue(udt.Id, out referencedIds))
            {
                foreach (var referencedId in referencedIds
                    .OrderBy(id => originalIndex.ContainsKey(id) ? originalIndex[id] : int.MaxValue))
                {
                    var referenced = udts.FirstOrDefault(item => item.Id == referencedId);
                    if (referenced == null)
                    {
                        throw new InvalidOperationException(
                            "UDT '" + udt.Name + "' references a missing UDT.");
                    }

                    Visit(referenced, udts, dependencies, originalIndex, state, ordered);
                }
            }

            state[udt.Id] = 2;
            ordered.Add(udt);
        }

        private static bool TryNormalizeArrayBounds(string value, out string canonical)
        {
            canonical = string.Empty;
            var parts = (value ?? string.Empty).Split(',');
            if (parts.Length == 0 || parts.Length > 6)
            {
                return false;
            }

            var normalized = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                var match = ArrayBoundPattern.Match(part.Trim());
                int lower;
                int upper;
                if (!match.Success ||
                    !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out lower) ||
                    !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out upper) ||
                    lower > upper)
                {
                    return false;
                }

                normalized.Add(
                    lower.ToString(CultureInfo.InvariantCulture) + ".." +
                    upper.ToString(CultureInfo.InvariantCulture));
            }

            canonical = string.Join(", ", normalized);
            return true;
        }

        private static IDictionary<string, string> BuildBuiltInLookup()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in BuiltInTypeValues)
            {
                values[value] = value;
            }

            values["TOD"] = "Time_Of_Day";
            values["LTOD"] = "LTime_Of_Day";
            values["DT"] = "Date_And_Time";
            return values;
        }

        private static string UnquoteIdentifier(string value)
        {
            if (value != null && value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2);
            }

            return value ?? string.Empty;
        }

        private static List<T> Safe<T>(IEnumerable<T> items)
            where T : class
        {
            return items == null ? new List<T>() : items.Where(item => item != null).ToList();
        }
    }
}
