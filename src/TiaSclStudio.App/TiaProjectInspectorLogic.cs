using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;

namespace TiaSclStudio.App
{
    internal sealed class TiaProjectInspectorTagRow
    {
        internal TiaProjectInspectorTagRow(TiaPlcTagCatalogItem tag)
        {
            if (tag == null) throw new ArgumentNullException("tag");

            TablePath = tag.TablePath;
            Name = tag.Name;
            DataType = tag.DataType;
            LogicalAddress = tag.LogicalAddress;
            Comment = tag.Comment;
            SearchText = TiaProjectInspectorLogic.BuildSearchText(
                TablePath,
                Name,
                DataType,
                LogicalAddress,
                Comment);
        }

        public string TablePath { get; private set; }

        public string Name { get; private set; }

        public string DataType { get; private set; }

        public string LogicalAddress { get; private set; }

        public string Comment { get; private set; }

        internal string SearchText { get; private set; }
    }

    internal sealed class TiaProjectInspectorHardwareRow
    {
        internal TiaProjectInspectorHardwareRow(TiaHardwareIoChannel channel)
        {
            if (channel == null) throw new ArgumentNullException("channel");

            Kind = GetKindText(channel.Kind);
            Number = channel.Number;
            LogicalAddress = channel.LogicalAddress;
            Width = channel.WidthBits + " bit";
            SuggestedDataTypes = string.Join(", ", channel.SuggestedDataTypes ?? new string[0]);
            DeviceItemPath = channel.DeviceItemPath;
            DeviceItemType = BuildDeviceItemType(channel);
            SearchText = TiaProjectInspectorLogic.BuildSearchText(
                Kind,
                Number.ToString(CultureInfo.InvariantCulture),
                LogicalAddress,
                Width,
                SuggestedDataTypes,
                DeviceItemPath,
                DeviceItemType);
        }

        public string Kind { get; private set; }

        public int Number { get; private set; }

        public string LogicalAddress { get; private set; }

        public string Width { get; private set; }

        public string SuggestedDataTypes { get; private set; }

        public string DeviceItemPath { get; private set; }

        public string DeviceItemType { get; private set; }

        internal string SearchText { get; private set; }

        private static string GetKindText(TiaIoChannelKind kind)
        {
            switch (kind)
            {
                case TiaIoChannelKind.DigitalInput: return "DI";
                case TiaIoChannelKind.DigitalOutput: return "DO";
                case TiaIoChannelKind.AnalogInput: return "AI";
                case TiaIoChannelKind.AnalogOutput: return "AO";
                default: return kind.ToString();
            }
        }

        private static string BuildDeviceItemType(TiaHardwareIoChannel channel)
        {
            var parts = new[]
            {
                channel.DeviceItemTypeName,
                channel.DeviceItemTypeIdentifier
            }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return string.Join(" · ", parts);
        }
    }

    internal sealed class TiaProjectInspectorFilterDebounceState
    {
        private int _generation;
        private string _pendingQuery;
        private bool _hasPendingQuery;

        internal int Schedule(string query)
        {
            _generation = unchecked(_generation + 1);
            _pendingQuery = query ?? string.Empty;
            _hasPendingQuery = true;
            return _generation;
        }

        internal bool TryTake(int generation, out string query)
        {
            if (!_hasPendingQuery || generation != _generation)
            {
                query = string.Empty;
                return false;
            }

            query = _pendingQuery;
            _pendingQuery = null;
            _hasPendingQuery = false;
            return true;
        }

        internal void Cancel()
        {
            _generation = unchecked(_generation + 1);
            _pendingQuery = null;
            _hasPendingQuery = false;
        }
    }

    internal sealed class TiaProjectInspectorModel
    {
        internal TiaProjectInspectorModel(
            bool canOpen,
            string blockingReason,
            string targetText,
            string projectPath,
            string connectionText,
            string cpuText,
            string tagStatusText,
            string hardwareStatusText,
            string warningText,
            IEnumerable<TiaProjectInspectorTagRow> tags,
            IEnumerable<TiaProjectInspectorHardwareRow> hardwareChannels)
        {
            CanOpen = canOpen;
            BlockingReason = blockingReason ?? string.Empty;
            TargetText = targetText ?? string.Empty;
            ProjectPath = projectPath ?? string.Empty;
            ConnectionText = connectionText ?? string.Empty;
            CpuText = cpuText ?? string.Empty;
            TagStatusText = tagStatusText ?? string.Empty;
            HardwareStatusText = hardwareStatusText ?? string.Empty;
            WarningText = warningText ?? string.Empty;
            Tags = new ReadOnlyCollection<TiaProjectInspectorTagRow>(
                new List<TiaProjectInspectorTagRow>(tags ?? new TiaProjectInspectorTagRow[0]));
            HardwareChannels = new ReadOnlyCollection<TiaProjectInspectorHardwareRow>(
                new List<TiaProjectInspectorHardwareRow>(
                    hardwareChannels ?? new TiaProjectInspectorHardwareRow[0]));
        }

        internal bool CanOpen { get; private set; }

        internal string BlockingReason { get; private set; }

        internal string TargetText { get; private set; }

        internal string ProjectPath { get; private set; }

        internal string ConnectionText { get; private set; }

        internal string CpuText { get; private set; }

        internal string TagStatusText { get; private set; }

        internal string HardwareStatusText { get; private set; }

        internal string WarningText { get; private set; }

        internal IReadOnlyList<TiaProjectInspectorTagRow> Tags { get; private set; }

        internal IReadOnlyList<TiaProjectInspectorHardwareRow> HardwareChannels { get; private set; }
    }

    internal static class TiaProjectInspectorLogic
    {
        internal static bool CanStartRead(
            TiaGatewayStatus status,
            bool isConnectedToCurrentTarget,
            bool isBusy)
        {
            return !isBusy &&
                isConnectedToCurrentTarget &&
                status != null &&
                status.IsConnected;
        }

        internal static TiaProjectInspectorModel Prepare(
            TiaGatewayStatus status,
            bool isConnectedToCurrentTarget,
            string projectPath,
            string requestedDeviceName,
            string requestedSoftwareName,
            TiaPlcTagCatalog tagCatalog,
            TiaHardwareIoCatalog hardwareCatalog)
        {
            if (status == null || !status.IsConnected || !isConnectedToCurrentTarget)
            {
                return Blocked("Сессия TIA потеряна или цель подключения изменилась.");
            }

            if (ContainsDiagnostic(status.Diagnostics, OpennessDiagnosticCodes.SessionLost) ||
                ContainsDiagnostic(
                    tagCatalog == null ? null : tagCatalog.Diagnostics,
                    OpennessDiagnosticCodes.SessionLost) ||
                ContainsDiagnostic(
                    hardwareCatalog == null ? null : hardwareCatalog.Diagnostics,
                    OpennessDiagnosticCodes.SessionLost))
            {
                return Blocked("Сессия TIA потеряна во время чтения; частичные данные не показываются.");
            }

            if (WasCancelled(tagCatalog == null ? null : tagCatalog.Diagnostics) ||
                WasCancelled(hardwareCatalog == null ? null : hardwareCatalog.Diagnostics))
            {
                return Blocked("Чтение инспектора TIA было отменено; частичные данные не показываются.");
            }

            var tagAvailable = tagCatalog != null && tagCatalog.IsAvailable;
            var hardwareAvailable = hardwareCatalog != null && hardwareCatalog.IsAvailable;
            if (!tagAvailable && !hardwareAvailable)
            {
                return Blocked("Ни PLC-теги, ни аппаратные I/O недоступны в текущей сессии TIA.");
            }

            var tags = tagAvailable
                ? tagCatalog.Tags
                    .Where(item => item != null)
                    .OrderBy(item => item.TablePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new TiaProjectInspectorTagRow(item))
                    .ToList()
                : new List<TiaProjectInspectorTagRow>();
            var hardware = hardwareAvailable
                ? hardwareCatalog.Channels
                    .Where(item => item != null)
                    .OrderBy(item => item.Kind)
                    .ThenBy(item => item.BitAddress)
                    .ThenBy(item => item.Number)
                    .Select(item => new TiaProjectInspectorHardwareRow(item))
                    .ToList()
                : new List<TiaProjectInspectorHardwareRow>();

            var warnings = new List<string>();
            if (!tagAvailable)
            {
                warnings.Add("PLC-теги недоступны; вкладка тегов оставлена пустой.");
            }
            else if (!tagCatalog.IsComplete)
            {
                warnings.Add("Снимок PLC-тегов неполный; показаны только успешно прочитанные теги.");
            }

            if (!hardwareAvailable)
            {
                warnings.Add("Аппаратные I/O недоступны; вкладка I/O оставлена пустой.");
            }

            warnings.AddRange(GetVisibleDiagnostics(
                tagCatalog == null ? null : tagCatalog.Diagnostics,
                hardwareCatalog == null ? null : hardwareCatalog.Diagnostics));

            var deviceName = FirstNotEmpty(
                tagCatalog == null ? null : tagCatalog.DeviceName,
                requestedDeviceName,
                "автовыбор PLC");
            var softwareName = FirstNotEmpty(
                tagCatalog == null ? null : tagCatalog.SoftwareName,
                requestedSoftwareName,
                "автовыбор PLC Software");

            return new TiaProjectInspectorModel(
                true,
                string.Empty,
                deviceName + " / " + softwareName,
                projectPath,
                BuildConnectionText(status),
                BuildCpuText(hardwareCatalog),
                tagAvailable
                    ? "Прочитано PLC-тегов: " + tags.Count +
                      (tagCatalog.IsComplete ? "." : "; снимок неполный.")
                    : "PLC-теги недоступны.",
                hardwareAvailable
                    ? "Сконфигурировано аппаратных каналов: " + hardware.Count + "."
                    : "Аппаратные I/O недоступны.",
                string.Join(Environment.NewLine, warnings.Distinct(StringComparer.Ordinal)),
                tags,
                hardware);
        }

        internal static bool MatchesFilter(TiaProjectInspectorTagRow row, string query)
        {
            return MatchesFilter(row, TokenizeFilter(query));
        }

        internal static bool MatchesFilter(TiaProjectInspectorHardwareRow row, string query)
        {
            return MatchesFilter(row, TokenizeFilter(query));
        }

        internal static bool MatchesFilter(
            TiaProjectInspectorTagRow row,
            IReadOnlyList<string> normalizedTokens)
        {
            return row != null && MatchesEveryToken(row.SearchText, normalizedTokens);
        }

        internal static bool MatchesFilter(
            TiaProjectInspectorHardwareRow row,
            IReadOnlyList<string> normalizedTokens)
        {
            return row != null && MatchesEveryToken(row.SearchText, normalizedTokens);
        }

        internal static string[] TokenizeFilter(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new string[0];
            }

            return query
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        internal static string BuildSearchText(params string[] values)
        {
            return string.Join(" ", values ?? new string[0]).ToUpperInvariant();
        }

        private static TiaProjectInspectorModel Blocked(string reason)
        {
            return new TiaProjectInspectorModel(
                false,
                reason,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                null);
        }

        private static bool WasCancelled(IEnumerable<OpennessDiagnostic> diagnostics)
        {
            return ContainsDiagnostic(
                       diagnostics,
                       OpennessDiagnosticCodes.PlcTagCatalogCancelled) ||
                   ContainsDiagnostic(
                       diagnostics,
                       OpennessDiagnosticCodes.HardwareIoCatalogCancelled);
        }

        private static bool ContainsDiagnostic(
            IEnumerable<OpennessDiagnostic> diagnostics,
            string code)
        {
            return (diagnostics ?? Enumerable.Empty<OpennessDiagnostic>()).Any(item =>
                item != null && string.Equals(item.Code, code, StringComparison.Ordinal));
        }

        private static IEnumerable<string> GetVisibleDiagnostics(
            IEnumerable<OpennessDiagnostic> tagDiagnostics,
            IEnumerable<OpennessDiagnostic> hardwareDiagnostics)
        {
            return (tagDiagnostics ?? Enumerable.Empty<OpennessDiagnostic>())
                .Concat(hardwareDiagnostics ?? Enumerable.Empty<OpennessDiagnostic>())
                .Where(item => item != null && item.Severity != DiagnosticSeverity.Information)
                .Take(8)
                .Select(item => item.Code + ": " + item.Message);
        }

        private static string BuildConnectionText(TiaGatewayStatus status)
        {
            var installation = status.Installation;
            return installation == null
                ? status.Description
                : status.Description + " · Portal V" + installation.PortalVersion.Major +
                  " / API V" + installation.ApiVersion.Major;
        }

        private static string BuildCpuText(TiaHardwareIoCatalog catalog)
        {
            if (catalog == null || !catalog.IsAvailable)
            {
                return "CPU / I/O недоступны";
            }

            var parts = new[]
            {
                catalog.CpuFamily.ToString(),
                catalog.CpuModel,
                catalog.CpuTypeIdentifier
            }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return string.Join(" · ", parts);
        }

        private static string FirstNotEmpty(params string[] values)
        {
            return values.First(value => !string.IsNullOrWhiteSpace(value)).Trim();
        }

        private static bool MatchesEveryToken(
            string normalizedSearchText,
            IReadOnlyList<string> normalizedTokens)
        {
            if (normalizedTokens == null || normalizedTokens.Count == 0)
            {
                return true;
            }

            for (var index = 0; index < normalizedTokens.Count; index++)
            {
                if (normalizedSearchText.IndexOf(
                    normalizedTokens[index],
                    StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
