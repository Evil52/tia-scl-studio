using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Importing;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;

namespace TiaSclStudio.App
{
    internal sealed class TiaLibraryImportPreparation
    {
        internal TiaLibraryImportPreparation(
            bool canPreview,
            bool isComplete,
            string combinedScl,
            string targetText,
            string summaryText,
            string warningText)
        {
            CanPreview = canPreview;
            IsComplete = isComplete;
            CombinedScl = combinedScl ?? string.Empty;
            TargetText = targetText ?? string.Empty;
            SummaryText = summaryText ?? string.Empty;
            WarningText = warningText ?? string.Empty;
        }

        internal bool CanPreview { get; private set; }

        internal bool IsComplete { get; private set; }

        internal string CombinedScl { get; private set; }

        internal string TargetText { get; private set; }

        internal string SummaryText { get; private set; }

        internal string WarningText { get; private set; }
    }

    internal static class TiaLibraryImportUiLogic
    {
        internal static bool CanReadLibrary(
            TiaGatewayStatus status,
            bool isConnectedToCurrentTarget,
            bool isBusy)
        {
            return !isBusy &&
                isConnectedToCurrentTarget &&
                status != null &&
                status.IsConnected;
        }

        internal static TiaLibraryImportPreparation Prepare(TiaLibrarySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");

            var sources = snapshot.Sources == null
                ? new List<TiaLibrarySource>()
                : snapshot.Sources.Where(item => item != null).ToList();
            var usableSources = sources
                .Where(item => !string.IsNullOrWhiteSpace(item.SclText))
                .ToList();
            var omittedSources = sources.Count - usableSources.Count;
            var complete = snapshot.IsComplete && omittedSources == 0;
            var cancelled = snapshot.Diagnostics.Any(item =>
                item != null &&
                string.Equals(
                    item.Code,
                    OpennessDiagnosticCodes.LibrarySnapshotCancelled,
                    StringComparison.Ordinal));
            var target = BuildTargetText(snapshot.DeviceName, snapshot.SoftwareName);
            var separatorLength = (Environment.NewLine.Length * 2L) *
                Math.Max(0, usableSources.Count - 1);
            var combinedLength = usableSources.Sum(item => (long)item.SclText.Length) +
                separatorLength;
            if (combinedLength > SclLibrarySourceParser.MaximumSourceLength)
            {
                return new TiaLibraryImportPreparation(
                    false,
                    false,
                    string.Empty,
                    target,
                    "Снимок TIA превышает безопасный лимит импорта.",
                    "Импорт заблокирован: объём SCL больше " +
                    SclLibrarySourceParser.MaximumSourceLength + " символов.");
            }

            var combined = string.Join(
                Environment.NewLine + Environment.NewLine,
                usableSources.Select(item => item.SclText.Trim()));

            var warningLines = new List<string>();
            if (!complete)
            {
                warningLines.Add(
                    "Снимок неполный: часть объектов TIA не удалось прочитать. Отсутствующие объекты не будут удалены из локальной библиотеки.");
            }

            if (omittedSources > 0)
            {
                warningLines.Add("Объектов без SCL: " + omittedSources + ".");
            }

            warningLines.AddRange((snapshot.Diagnostics ?? new OpennessDiagnostic[0])
                .Where(item => item != null && item.Severity != DiagnosticSeverity.Information)
                .Take(6)
                .Select(item => item.Code + ": " + item.Message));

            var canPreview = snapshot.IsAvailable && usableSources.Count > 0 && !cancelled;
            var summary = cancelled
                ? "Чтение снимка TIA было отменено; частичные данные не импортируются."
                : !snapshot.IsAvailable
                ? "Библиотека TIA недоступна для чтения."
                : usableSources.Count == 0
                    ? "В снимке TIA нет FB, FC или UDT, которые можно импортировать."
                    : "Снимок " + target + ": прочитано объектов " + usableSources.Count +
                      (complete ? "." : "; снимок неполный.");

            return new TiaLibraryImportPreparation(
                canPreview,
                complete,
                combined,
                target,
                summary,
                string.Join(Environment.NewLine, warningLines));
        }

        internal static SclImportPreviewPresentation CreatePresentation(
            TiaLibraryImportPreparation preparation)
        {
            if (preparation == null) throw new ArgumentNullException("preparation");

            return new SclImportPreviewPresentation(
                "Предпросмотр импорта из TIA Portal",
                "ИМПОРТ FB / FC / UDT ИЗ TIA PORTAL",
                "Показан снимок " + preparation.TargetText +
                ", полученный только для чтения. TIA Portal не изменяется. " +
                "В локальную библиотеку попадают только объявления и интерфейсы; тела FB/FC не копируются.",
                preparation.WarningText);
        }

        private static string BuildTargetText(string deviceName, string softwareName)
        {
            var parts = new[] { deviceName, softwareName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => "«" + value.Trim() + "»")
                .ToArray();
            return parts.Length == 0 ? "подключённого PLC" : string.Join(" / ", parts);
        }
    }
}
