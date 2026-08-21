using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using TiaSclStudio.Core.Importing;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;

namespace TiaSclStudio.App
{
    public partial class MainWindow
    {
        private CancellationTokenSource _tiaLibraryReadCancellation;

        private async void ReadTiaLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable() ||
                !TiaLibraryImportUiLogic.CanReadLibrary(
                    _tiaGatewayStatus,
                    IsConnectedToCurrentTarget(),
                    _onlineBusy))
            {
                ShowOnlineInputError(
                    "016",
                    "Сначала подключитесь к выбранному проекту TIA и PLC.");
                return;
            }

            GatewayLibraryReadResponse response = null;
            var cancellation = new CancellationTokenSource();
            _tiaLibraryReadCancellation = cancellation;
            SetOnlineBusy(true, "Чтение FB, FC и UDT из TIA Portal без изменения проекта…");
            try
            {
                response = await _tiaGatewayWorker.InvokeAsync(() =>
                {
                    var snapshot = _tiaGateway.ReadLibrarySnapshot(cancellation.Token);
                    var status = _tiaGateway.GetStatus();
                    return new GatewayLibraryReadResponse(snapshot, status);
                });

                _tiaGatewayStatus = response.Status;
                ReplaceOnlineDiagnostics(MergeGatewayAndLibraryDiagnostics(
                    response.Status,
                    response.Snapshot));
                ApplyGatewayStatus(response.Status);
            }
            catch (OperationCanceledException)
            {
                TiaLibraryReadSummaryText.Text = "Чтение библиотеки TIA отменено; проекты не изменены.";
                SetStatus("Чтение библиотеки TIA отменено");
                return;
            }
            catch (Exception exception)
            {
                ReplaceOnlineDiagnostics(new[]
                {
                    CreateUiOpennessDiagnostic(
                        OnlineDiagnosticPrefix + "016",
                        DiagnosticSeverity.Error,
                        "Не удалось прочитать FB, FC и UDT из TIA Portal. Проект TIA не изменялся.",
                        exception)
                });
                TiaLibraryReadSummaryText.Text = "Чтение библиотеки TIA завершилось ошибкой; локальный проект не изменён.";
                SetStatus("Библиотеку TIA прочитать не удалось; TIA и локальный проект не изменены");
                return;
            }
            finally
            {
                if (ReferenceEquals(_tiaLibraryReadCancellation, cancellation))
                {
                    _tiaLibraryReadCancellation = null;
                }

                cancellation.Dispose();
                SetOnlineBusy(false, string.Empty);
            }

            if (response == null ||
                response.Status == null ||
                !response.Status.IsConnected ||
                !IsConnectedToCurrentTarget())
            {
                TiaLibraryReadSummaryText.Text = "Сессия TIA потеряна во время чтения; импорт не выполнен.";
                SetStatus("Сессия TIA потеряна; локальный проект не изменён");
                return;
            }

            TiaLibraryImportPreparation preparation;
            try
            {
                preparation = TiaLibraryImportUiLogic.Prepare(response.Snapshot);
            }
            catch (Exception exception)
            {
                AddInteractionError(
                    "UI_TIA_LIBRARY_READ",
                    "Не удалось подготовить снимок TIA к предпросмотру: " + exception.Message,
                    string.Empty);
                return;
            }

            TiaLibraryReadSummaryText.Text = preparation.SummaryText;
            if (!preparation.CanPreview)
            {
                SetStatus(preparation.SummaryText + " Локальный проект не изменён.");
                return;
            }

            if (!preparation.IsComplete)
            {
                var confirmation = MessageBox.Show(
                    this,
                    preparation.WarningText +
                    Environment.NewLine + Environment.NewLine +
                    "Показать предпросмотр только прочитанных объектов?",
                    "Неполный снимок TIA",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirmation != MessageBoxResult.Yes)
                {
                    SetStatus("Импорт из неполного снимка TIA отменён; проекты не изменены");
                    return;
                }
            }

            try
            {
                var parsed = new SclLibrarySourceParser().Parse(preparation.CombinedScl);
                var plan = PreviewAndApplyLibraryImport(
                    parsed,
                    TiaLibraryImportUiLogic.CreatePresentation(preparation),
                    "Импорт интерфейсов из TIA " + preparation.TargetText);
                if (plan == null)
                {
                    SetStatus("Импорт из TIA отменён; TIA и локальный проект не изменены");
                    return;
                }

                TiaLibraryReadSummaryText.Text =
                    "Импорт из " + preparation.TargetText + ": добавлено " + plan.AddCount +
                    ", обновлено " + plan.UpdateCount + ". TIA Portal не изменялся.";
                SetStatus(TiaLibraryReadSummaryText.Text);
            }
            catch (Exception exception)
            {
                AddInteractionError(
                    "UI_TIA_LIBRARY_IMPORT",
                    "Не удалось импортировать снимок TIA: " + exception.Message,
                    preparation.TargetText);
            }
        }

        private bool CancelTiaLibraryRead()
        {
            var cancellation = _tiaLibraryReadCancellation;
            if (cancellation == null)
            {
                return false;
            }

            cancellation.Cancel();
            return true;
        }

        private static IEnumerable<OpennessDiagnostic> MergeGatewayAndLibraryDiagnostics(
            TiaGatewayStatus status,
            TiaLibrarySnapshot snapshot)
        {
            var diagnostics = new List<OpennessDiagnostic>();
            if (status != null)
            {
                diagnostics.AddRange(status.Diagnostics);
            }

            if (snapshot != null)
            {
                diagnostics.AddRange(snapshot.Diagnostics);
            }

            return diagnostics
                .Where(item => item != null)
                .GroupBy(item => new
                {
                    item.Code,
                    item.Severity,
                    item.Message,
                    item.Location,
                    item.ExceptionType
                })
                .Select(group => group.First())
                .ToList();
        }

        private sealed class GatewayLibraryReadResponse
        {
            internal GatewayLibraryReadResponse(
                TiaLibrarySnapshot snapshot,
                TiaGatewayStatus status)
            {
                Snapshot = snapshot;
                Status = status;
            }

            internal TiaLibrarySnapshot Snapshot { get; private set; }

            internal TiaGatewayStatus Status { get; private set; }
        }
    }
}
