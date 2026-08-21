using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;

namespace TiaSclStudio.App
{
    public partial class MainWindow
    {
        private CancellationTokenSource _tiaProjectInspectorReadCancellation;

        private async void OpenTiaProjectInspector_Click(object sender, RoutedEventArgs e)
        {
            if (!TiaProjectInspectorLogic.CanStartRead(
                _tiaGatewayStatus,
                IsConnectedToCurrentTarget(),
                _onlineBusy))
            {
                ShowOnlineInputError(
                    "017",
                    "Сначала подключитесь к выбранному проекту TIA и PLC.");
                return;
            }

            _tiaHardwareIoCatalog = null;
            var cancellation = new CancellationTokenSource();
            _tiaProjectInspectorReadCancellation = cancellation;
            GatewayProjectInspectorResponse response = null;
            TiaProjectInspectorSummaryText.Text =
                "Чтение свежих снимков PLC-тегов и аппаратных I/O…";
            SetOnlineBusy(
                true,
                "Инспектор читает PLC-теги и аппаратные I/O без изменения TIA…");
            try
            {
                response = await _tiaGatewayWorker.InvokeAsync(() =>
                {
                    var tags = _tiaGateway.ReadPlcTagCatalog(cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    var hardware = _tiaGateway.ReadHardwareIoCatalog(cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    var status = _tiaGateway.GetStatus();
                    return new GatewayProjectInspectorResponse(tags, hardware, status);
                });

                _tiaGatewayStatus = response.Status;
                ReplaceOnlineDiagnostics(MergeProjectInspectorDiagnostics(response));
                ApplyGatewayStatus(response.Status);
            }
            catch (OperationCanceledException)
            {
                TiaProjectInspectorSummaryText.Text =
                    "Чтение инспектора отменено; частичные данные не показаны.";
                SetStatus("Чтение инспектора TIA отменено");
                return;
            }
            catch (Exception exception)
            {
                ReplaceOnlineDiagnostics(new[]
                {
                    CreateUiOpennessDiagnostic(
                        OnlineDiagnosticPrefix + "017",
                        DiagnosticSeverity.Error,
                        "Не удалось прочитать данные инспектора. TIA Portal не изменялся.",
                        exception)
                });
                TiaProjectInspectorSummaryText.Text =
                    "Чтение инспектора завершилось ошибкой; старые снимки не показываются.";
                SetStatus("Инспектор TIA не открыт; чтение завершилось ошибкой");
                return;
            }
            finally
            {
                if (ReferenceEquals(_tiaProjectInspectorReadCancellation, cancellation))
                {
                    _tiaProjectInspectorReadCancellation = null;
                }

                cancellation.Dispose();
                SetOnlineBusy(false, string.Empty);
            }

            if (response == null ||
                response.Status == null ||
                !response.Status.IsConnected ||
                !IsConnectedToCurrentTarget())
            {
                _tiaHardwareIoCatalog = null;
                TiaProjectInspectorSummaryText.Text =
                    "Сессия TIA потеряна во время чтения; инспектор не открыт.";
                SetStatus("Сессия TIA потеряна; инспектор не открыт");
                return;
            }

            _tiaHardwareIoCatalog = response.HardwareCatalog;
            var model = TiaProjectInspectorLogic.Prepare(
                response.Status,
                IsConnectedToCurrentTarget(),
                TiaProjectPathTextBox.Text == null ? string.Empty : TiaProjectPathTextBox.Text.Trim(),
                TiaDeviceNameTextBox.Text == null ? string.Empty : TiaDeviceNameTextBox.Text.Trim(),
                TiaSoftwareNameTextBox.Text == null ? string.Empty : TiaSoftwareNameTextBox.Text.Trim(),
                response.TagCatalog,
                response.HardwareCatalog);
            if (!model.CanOpen)
            {
                _tiaHardwareIoCatalog = null;
                TiaProjectInspectorSummaryText.Text = model.BlockingReason;
                SetStatus(model.BlockingReason);
                return;
            }

            TiaProjectInspectorSummaryText.Text =
                model.TagStatusText + " " + model.HardwareStatusText;
            SetStatus("Инспектор TIA готов; TIA Portal не изменялся");
            var inspector = new TiaProjectInspectorWindow(model)
            {
                Owner = this
            };
            inspector.ShowDialog();
        }

        private bool CancelTiaProjectInspectorRead()
        {
            var cancellation = _tiaProjectInspectorReadCancellation;
            if (cancellation == null)
            {
                return false;
            }

            cancellation.Cancel();
            return true;
        }

        private static IEnumerable<OpennessDiagnostic> MergeProjectInspectorDiagnostics(
            GatewayProjectInspectorResponse response)
        {
            if (response == null)
            {
                return Enumerable.Empty<OpennessDiagnostic>();
            }

            var diagnostics = new List<OpennessDiagnostic>();
            if (response.Status != null)
            {
                diagnostics.AddRange(response.Status.Diagnostics);
            }

            if (response.TagCatalog != null)
            {
                diagnostics.AddRange(response.TagCatalog.Diagnostics);
            }

            if (response.HardwareCatalog != null)
            {
                diagnostics.AddRange(response.HardwareCatalog.Diagnostics);
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

        private sealed class GatewayProjectInspectorResponse
        {
            internal GatewayProjectInspectorResponse(
                TiaPlcTagCatalog tagCatalog,
                TiaHardwareIoCatalog hardwareCatalog,
                TiaGatewayStatus status)
            {
                TagCatalog = tagCatalog;
                HardwareCatalog = hardwareCatalog;
                Status = status;
            }

            internal TiaPlcTagCatalog TagCatalog { get; private set; }

            internal TiaHardwareIoCatalog HardwareCatalog { get; private set; }

            internal TiaGatewayStatus Status { get; private set; }
        }
    }
}
